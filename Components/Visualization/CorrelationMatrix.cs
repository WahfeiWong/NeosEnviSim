using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using NeosEnviSim.Properties;
using Rhino.Geometry;

namespace NeosVisualization
{
    public class CorrelationMatrixComponent : GH_Component
    {
        public CorrelationMatrixComponent()
          : base("Correlation Matrix", "CorrMatrix",
              "Computes and visualizes Pearson correlation matrix between independent and dependent variables",
              "Neos", "Visualization")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Origin", "O", "Local coordinate system origin (default: world origin)", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Independent Variables", "X", "Independent variables data tree", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Dependent Variables", "Y", "Dependent variables data tree", GH_ParamAccess.tree);
            pManager.AddNumberParameter("X Spacing", "DX", "Grid spacing in X direction", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Y Spacing", "DY", "Grid spacing in Y direction", GH_ParamAccess.item, 1.0);
            pManager.AddColourParameter("Positive Colors", "C+", "Color range for positive correlations (default: white to red)", GH_ParamAccess.list);
            pManager[5].Optional = true;
            pManager.AddColourParameter("Negative Colors", "C-", "Color range for negative correlations (default: white to blue)", GH_ParamAccess.list);
            pManager[6].Optional = true;
            pManager.AddBooleanParameter("Sample StdDev", "S", "True for sample (n-1), False for population (n)", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("Grid Lines", "Lines", "Background grid lines", GH_ParamAccess.list);
            pManager.AddMeshParameter("Correlation Mesh", "Mesh", "Colored mesh showing correlations", GH_ParamAccess.item);
            pManager.AddSurfaceParameter("Grid Surfaces", "Surfaces", "Surfaces for each grid cell", GH_ParamAccess.list);
            pManager.AddPointParameter("Face Centers", "Centers", "Center points of each grid face", GH_ParamAccess.list);
            pManager.AddNumberParameter("Correlation Coefficients", "R", "Pearson correlation coefficients", GH_ParamAccess.tree);
            pManager.AddColourParameter("Face Colors", "Colors", "Color for each grid face", GH_ParamAccess.list);
            pManager.AddPointParameter("Left Axis Points", "LeftPts", "Midpoints on left Y-axis", GH_ParamAccess.list);
            pManager.AddPointParameter("Bottom Axis Points", "BottomPts", "Midpoints on bottom X-axis", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. Retrieve inputs
            Point3d origin = Point3d.Origin;
            GH_Structure<GH_Number> xTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> yTree = new GH_Structure<GH_Number>();
            double dx = 1.0;
            double dy = 1.0;
            List<Color> posColors = new List<Color>();
            List<Color> negColors = new List<Color>();
            bool useSampleStdDev = true;

            DA.GetData(0, ref origin);
            DA.GetDataTree(1, out xTree);
            DA.GetDataTree(2, out yTree);
            DA.GetData(3, ref dx);
            DA.GetData(4, ref dy);
            DA.GetDataList(5, posColors);
            DA.GetDataList(6, negColors);
            DA.GetData(7, ref useSampleStdDev);

            // Set default colors if not provided
            if (posColors.Count == 0)
            {
                posColors.Add(Color.White);
                posColors.Add(Color.Red);
            }
            if (negColors.Count == 0)
            {
                negColors.Add(Color.White);
                negColors.Add(Color.Blue);
            }

            // 2. Prepare data
            int numX = xTree.Branches.Count;
            int numY = yTree.Branches.Count;

            // Check for valid data
            if (numX == 0 || numY == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input trees contain no branches");
                return;
            }

            // 3. Create containers
            List<Line> gridLines = new List<Line>();
            Mesh corrMesh = new Mesh();
            List<Surface> gridSurfaces = new List<Surface>();
            List<Point3d> faceCenters = new List<Point3d>();
            GH_Structure<GH_Number> corrTree = new GH_Structure<GH_Number>();
            List<Color> faceColors = new List<Color>();
            List<Point3d> leftAxisPoints = new List<Point3d>();
            List<Point3d> bottomAxisPoints = new List<Point3d>();

            // 4. Precompute grid points for grid lines and surfaces
            Point3d[,] gridPoints = new Point3d[numX + 1, numY + 1];
            for (int j = 0; j <= numY; j++)
            {
                for (int i = 0; i <= numX; i++)
                {
                    gridPoints[i, j] = new Point3d(
                        origin.X + i * dx,
                        origin.Y + j * dy,
                        origin.Z
                    );
                }
            }

            // 5. Compute correlations and create geometry
            for (int i = 0; i < numX; i++)
            {
                GH_Path path = new GH_Path(i);
                corrTree.EnsurePath(path);

                for (int j = 0; j < numY; j++)
                {
                    // Get data
                    List<double> xVals = GetValues(xTree.Branches[i]);
                    List<double> yVals = GetValues(yTree.Branches[j]);

                    // Calculate Pearson correlation
                    double r = CalculatePearsonCorrelation(xVals, yVals, useSampleStdDev);
                    corrTree.Append(new GH_Number(r), path);

                    // Get color for correlation value
                    Color faceColor = GetColorForCorrelation(r, posColors, negColors);
                    faceColors.Add(faceColor);

                    // Create grid surface (rectangle)
                    Point3d p1 = gridPoints[i, j];
                    Point3d p2 = gridPoints[i + 1, j];
                    Point3d p3 = gridPoints[i + 1, j + 1];
                    Point3d p4 = gridPoints[i, j + 1];
                    Surface rect = NurbsSurface.CreateFromCorners(p1, p2, p3, p4);
                    gridSurfaces.Add(rect);

                    // Calculate face center
                    Point3d center = new Point3d(
                        origin.X + (i + 0.5) * dx,
                        origin.Y + (j + 0.5) * dy,
                        origin.Z
                    );
                    faceCenters.Add(center);

                    // For first column, add to left axis points
                    if (i == 0)
                    {
                        leftAxisPoints.Add(new Point3d(origin.X, center.Y, origin.Z));
                    }

                    // For last row, add to bottom axis points
                    if (j == 0)
                    {
                        bottomAxisPoints.Add(new Point3d(center.X, origin.Y, origin.Z));
                    }

                    // Create mesh face with independent vertices (for solid face coloring)
                    int startIndex = corrMesh.Vertices.Count;

                    // Add vertices for this face only
                    corrMesh.Vertices.Add(p1);
                    corrMesh.Vertices.Add(p2);
                    corrMesh.Vertices.Add(p3);
                    corrMesh.Vertices.Add(p4);

                    // Add face
                    corrMesh.Faces.AddFace(startIndex, startIndex + 1, startIndex + 2, startIndex + 3);

                    // Assign same color to all vertices of this face
                    for (int k = 0; k < 4; k++)
                    {
                        corrMesh.VertexColors.Add(faceColor);
                    }
                }
            }

            // 6. Create grid lines
            // Horizontal lines
            for (int j = 0; j <= numY; j++)
            {
                for (int i = 0; i < numX; i++)
                {
                    Point3d p1 = gridPoints[i, j];
                    Point3d p2 = gridPoints[i + 1, j];
                    gridLines.Add(new Line(p1, p2));
                }
            }

            // Vertical lines
            for (int i = 0; i <= numX; i++)
            {
                for (int j = 0; j < numY; j++)
                {
                    Point3d p1 = gridPoints[i, j];
                    Point3d p2 = gridPoints[i, j + 1];
                    gridLines.Add(new Line(p1, p2));
                }
            }

            // 7. Set outputs
            DA.SetDataList(0, gridLines);
            DA.SetData(1, corrMesh);
            DA.SetDataList(2, gridSurfaces);
            DA.SetDataList(3, faceCenters);
            DA.SetDataTree(4, corrTree);
            DA.SetDataList(5, faceColors);
            DA.SetDataList(6, leftAxisPoints);
            DA.SetDataList(7, bottomAxisPoints);
        }

        private List<double> GetValues(List<GH_Number> numbers)
        {
            List<double> vals = new List<double>();
            foreach (GH_Number num in numbers)
            {
                if (num != null)
                    vals.Add(num.Value);
            }
            return vals;
        }

        private double CalculatePearsonCorrelation(List<double> x, List<double> y, bool useSampleStdDev)
        {
            // Handle insufficient data
            if (x.Count < 2 || y.Count < 2 || x.Count != y.Count)
                return double.NaN;

            int n = x.Count;
            double sumX = 0, sumY = 0, sumXY = 0;
            double sumXSq = 0, sumYSq = 0;

            for (int i = 0; i < n; i++)
            {
                double xi = x[i];
                double yi = y[i];
                sumX += xi;
                sumY += yi;
                sumXY += xi * yi;
                sumXSq += xi * xi;
                sumYSq += yi * yi;
            }

            double numerator = sumXY - (sumX * sumY) / n;
            double denominatorX = sumXSq - (sumX * sumX) / n;
            double denominatorY = sumYSq - (sumY * sumY) / n;

            if (denominatorX <= 0 || denominatorY <= 0)
                return double.NaN;

            double r = numerator / Math.Sqrt(denominatorX * denominatorY);

            // Apply population vs sample adjustment
            if (useSampleStdDev && n > 1)
            {
                r = r * Math.Sqrt(n) / Math.Sqrt(n - 1);
            }

            // Ensure result is within [-1, 1] range
            r = Math.Max(-1.0, Math.Min(1.0, r));

            return double.IsNaN(r) ? 0 : r;
        }

        private Color GetColorForCorrelation(double r, List<Color> posColors, List<Color> negColors)
        {
            if (double.IsNaN(r)) return Color.Gray;

            // Handle color interpolation
            if (r >= 0)
            {
                return InterpolateColor(posColors[0], posColors[1], (float)r);
            }
            else
            {
                return InterpolateColor(negColors[0], negColors[1], (float)Math.Abs(r));
            }
        }

        private Color InterpolateColor(Color c1, Color c2, float t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)(c1.R + (c2.R - c1.R) * t),
                (int)(c1.G + (c2.G - c1.G) * t),
                (int)(c1.B + (c2.B - c1.B) * t)
            );
        }
        protected override System.Drawing.Bitmap Icon => Resources.icon_CorrelationMatrix;
        public override Guid ComponentGuid => new Guid("C2B864E8-19D1-4A22-BA65-F4991B968A0B");
    }
}


