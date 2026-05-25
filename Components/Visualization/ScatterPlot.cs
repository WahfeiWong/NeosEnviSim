using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;

namespace NeosVisualization
{
    public class ScatterPlotComponent : GH_Component
    {
        public ScatterPlotComponent()
          : base("Scatter Plot",
                 "ScatterP",
                 "Creates a scatter plot with grid system from data tree",
                 "Neos",
                 "Visualization")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Data", "D", "Input data tree (Y-values)", GH_ParamAccess.tree);
            pManager.AddPointParameter("Origin", "O", "Bottom-left corner of the grid", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Grid Spacing X", "SX", "Spacing between vertical grid lines", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Grid Spacing Y", "SY", "Spacing between horizontal grid lines", GH_ParamAccess.item, 10.0);
            pManager.AddIntegerParameter("Y Segment Count", "YS", "Number of horizontal grid segments", GH_ParamAccess.item, 5);
            pManager.AddBooleanParameter("Jitter", "J", "Enable jitter on X-axis", GH_ParamAccess.item, false);
            pManager.AddIntervalParameter("Jitter Domain", "JD", "Jitter range on X-axis (relative to cell)", GH_ParamAccess.item, new Interval(-0.4, 0.4));
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Data Points", "P", "Scatter plot data points", GH_ParamAccess.tree);
            pManager.AddLineParameter("Grid Lines", "G", "Grid lines", GH_ParamAccess.list);
            pManager.AddPointParameter("X Ticks", "XT", "Grid points on X-axis", GH_ParamAccess.list);
            pManager.AddPointParameter("Y Ticks", "YT", "Grid points on Y-axis", GH_ParamAccess.list);      
            pManager.AddNumberParameter("Y Ticks Labels", "YTL", "Y coordinate differences relative to origin", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. Retrieve input data
            GH_Structure<GH_Number> dataTree = new GH_Structure<GH_Number>();
            Point3d origin = Point3d.Origin;
            double spacingX = 10;
            double spacingY = 10;
            int ySegmentCount = 5;
            bool jitterEnabled = false;
            Interval jitterDomain = new Interval(-0.4, 0.4);

            if (!DA.GetDataTree(0, out dataTree)) return;
            if (!DA.GetData(1, ref origin)) return;
            if (!DA.GetData(2, ref spacingX)) return;
            if (!DA.GetData(3, ref spacingY)) return;
            if (!DA.GetData(4, ref ySegmentCount)) return;
            if (!DA.GetData(5, ref jitterEnabled)) return;
            if (!DA.GetData(6, ref jitterDomain)) return;

            // Ensure valid segment count
            if (ySegmentCount < 1) ySegmentCount = 1;

            // Ensure Z=0 for all geometry
            origin = new Point3d(origin.X, origin.Y, 0);

            // 2. Initialize output containers
            GH_Structure<GH_Point> dataPoints = new GH_Structure<GH_Point>();
            List<Line> gridLines = new List<Line>();
            List<Point3d> xTicks = new List<Point3d>();
            List<Point3d> yTicks = new List<Point3d>();
            List<double> yTickLabels = new List<double>(); 

            // 3. Calculate data range
            int branchCount = dataTree.PathCount;
            if (branchCount == 0)
            {
                DA.SetDataTree(0, dataPoints);
                DA.SetDataList(1, gridLines);
                DA.SetDataList(2, xTicks);
                DA.SetDataList(3, yTicks);
                DA.SetDataList(4, yTickLabels); 
                return;
            }

            // 4. Calculate grid boundaries
            double gridMinX = origin.X;
            double gridMaxX = origin.X + branchCount * spacingX;
            double gridMinY = origin.Y;

            // Calculate Y-range based on segment count
            double gridMaxY = origin.Y + ySegmentCount * spacingY;

            // 5. Create vertical grid lines
            for (int i = 0; i <= branchCount; i++)
            {
                double x = gridMinX + i * spacingX;
                Line verticalLine = new Line(
                    new Point3d(x, gridMinY, 0),
                    new Point3d(x, gridMaxY, 0)
                );
                gridLines.Add(verticalLine);

                // Add X-axis ticks (bottom of grid)
                xTicks.Add(new Point3d(x, origin.Y, 0));
            }

            // 6. Create horizontal grid lines and Y ticks labels
            for (int i = 0; i <= ySegmentCount; i++) // +1 to include top line
            {
                double y = gridMinY + i * spacingY;
                Line horizontalLine = new Line(
                    new Point3d(gridMinX, y, 0),
                    new Point3d(gridMaxX, y, 0)
                );
                gridLines.Add(horizontalLine);

                // Add Y-axis ticks (left of grid)
                yTicks.Add(new Point3d(origin.X, y, 0));

                // Calculate and store Y tick label (difference from origin)
                double labelValue = y - origin.Y;
                yTickLabels.Add(labelValue);
            }

            // 7. Generate scatter plot points with jitter
            Random rand = new Random();
            int pathIndex = 0;

            foreach (GH_Path path in dataTree.Paths)
            {
                var branch = dataTree.get_Branch(path);
                List<GH_Number> values = new List<GH_Number>();

                foreach (IGH_Goo item in branch)
                {
                    if (item is GH_Number num)
                    {
                        values.Add(num);
                    }
                }

                double cellCenterX = origin.X + (pathIndex + 0.5) * spacingX;
                GH_Path newPath = new GH_Path(path);

                foreach (GH_Number num in values)
                {
                    double yValue = origin.Y + num.Value; // Apply origin offset to Y
                    double xValue = cellCenterX;

                    if (jitterEnabled)
                    {
                        double jitterAmount = jitterDomain.ParameterAt(rand.NextDouble());
                        xValue += jitterAmount * spacingX;
                    }

                    dataPoints.Append(new GH_Point(new Point3d(xValue, yValue, 0)), newPath);
                }
                pathIndex++;
            }

            // 8. Set outputs
            DA.SetDataTree(0, dataPoints);
            DA.SetDataList(1, gridLines);
            DA.SetDataList(2, xTicks);
            DA.SetDataList(3, yTicks);
            DA.SetDataList(4, yTickLabels); 
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_ScatterPlot;
        public override Guid ComponentGuid => new Guid("3D5A5B6C-7D8E-4F9A-A1B2-C3D4E5F6A7B8");
    }
}