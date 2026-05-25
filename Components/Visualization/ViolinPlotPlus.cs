using Grasshopper.Kernel;
using Rhino.Geometry;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel.Types;

namespace NeosVisualization
{
    public class ViolinPlotCombinationComponent : GH_Component
    {
        public ViolinPlotCombinationComponent()
          : base("Violin Plot Plus",
                 "VPS",
                 "Generates violin plots from multiple datasets with background grid",
                 "Neos",
                 "Visualization")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Data", "D", "Datasets to visualize (multiple branches)", GH_ParamAccess.tree);
            pManager.AddPointParameter("Origin", "O", "Base point for the plot (grid lower-left corner)", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Max Width", "W", "Maximum width of violin plot", GH_ParamAccess.item, 10.0);
            pManager.AddIntegerParameter("Resolution", "R", "Number of points for density curve", GH_ParamAccess.item, 100);
            pManager.AddNumberParameter("Bandwidth", "B", "Kernel density bandwidth (0=auto)", GH_ParamAccess.item, 0.0);
            // Grid parameters
            pManager.AddNumberParameter("Grid X Spacing", "GXS", "Spacing between vertical grid lines", GH_ParamAccess.item, 20.0);
            pManager.AddNumberParameter("Grid Y Spacing", "GYS", "Spacing between horizontal grid lines", GH_ParamAccess.item, 10.0);
            pManager.AddIntegerParameter("Grid Y Divisions", "GYD", "Number of horizontal grid divisions", GH_ParamAccess.item, 10);
            pManager.AddNumberParameter("Y Grid Start", "YGS", "Y coordinate value for the first grid line", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Violin Shapes", "VS", "Violin outline curves", GH_ParamAccess.list);
            pManager.AddLineParameter("Median Lines", "ML", "Median indicator lines", GH_ParamAccess.list);
            pManager.AddPointParameter("Data Points", "DP", "Original data points", GH_ParamAccess.tree);
            pManager.AddLineParameter("Mean Lines", "ME", "Mean indicator lines", GH_ParamAccess.list);
            pManager.AddLineParameter("Sigma Lines", "SL", "Lines at mean ± standard deviation", GH_ParamAccess.tree);
            // Grid outputs
            pManager.AddLineParameter("Grid Lines", "GL", "Background grid lines", GH_ParamAccess.list);
            pManager.AddPointParameter("X Ticks", "XT", "Tick points at bottom of grid (midpoints)", GH_ParamAccess.list);
            pManager.AddPointParameter("Y Ticks", "YT", "Tick points on Y axis", GH_ParamAccess.list);
            pManager.AddNumberParameter("Y Tick Values", "YV", "Y tick values", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Get input data
            Grasshopper.Kernel.Data.GH_Structure<Grasshopper.Kernel.Types.GH_Number> dataTree;
            Point3d origin = Point3d.Origin;
            double maxWidth = 10.0;
            int resolution = 100;
            double bandwidth = 0.0;
            double gridXSpacing = 20.0;
            double gridYSpacing = 10.0;
            int gridYDivisions = 10;
            double gridYStart = 0.0;

            if (!DA.GetDataTree(0, out dataTree)) return;
            if (!DA.GetData(1, ref origin)) return;
            if (!DA.GetData(2, ref maxWidth)) return;
            if (!DA.GetData(3, ref resolution)) return;
            DA.GetData(4, ref bandwidth);
            DA.GetData(5, ref gridXSpacing);
            DA.GetData(6, ref gridYSpacing);
            DA.GetData(7, ref gridYDivisions);
            DA.GetData(8, ref gridYStart);

            // Convert data to list of lists
            var datasets = new List<List<double>>();
            foreach (var path in dataTree.Paths)
            {
                var branch = dataTree[path];
                datasets.Add(branch.Select(ghNum => ghNum.Value).ToList());
            }

            int datasetCount = datasets.Count;
            if (datasetCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No data provided");
                return;
            }

            // Prepare output containers
            var violinCurves = new List<Curve>();
            var medianLines = new List<Line>();
            var dataPoints = new Grasshopper.DataTree<Point3d>();
            var meanLines = new List<Line>();
            var sigmaLines = new Grasshopper.DataTree<Line>();
            var gridLines = new List<Line>();

            // Calculate X positions for datasets (grid midpoints)
            var xPositions = new double[datasetCount];
            for (int i = 0; i < datasetCount; i++)
            {
                xPositions[i] = origin.X + (i + 0.5) * gridXSpacing;
            }

            // Create grid lines (starting from Y Grid Start)
            CreateGridLines(origin, gridXSpacing, gridYSpacing, gridYDivisions, gridYStart, datasetCount, gridLines);

            // Process each dataset - violin plots remain fixed relative to origin
            for (int i = 0; i < datasetCount; i++)
            {
                var data = datasets[i];
                double xPos = xPositions[i];

                if (data.Count < 2)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Dataset {i + 1} requires at least 2 values");
                    continue;
                }

                // Calculate statistics
                double median = CalculateMedian(data);
                double mean = data.Average();
                double stdDev = CalculateStandardDeviation(data);
                double iqr = CalculateIQR(data);
                double min = data.Min();
                double max = data.Max();

                // Auto-calculate bandwidth using improved method
                if (bandwidth <= 0)
                {
                    bandwidth = CalculateBandwidth(data, stdDev, iqr);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Dataset {i + 1}: Auto bandwidth = {bandwidth:F4} (n={data.Count}, SD={stdDev:F2}, IQR={iqr:F2})");
                }

                // Create violin plot center point (fixed relative to origin)
                Point3d violinCenter = new Point3d(xPos, origin.Y, origin.Z);

                // Generate violin curve (fixed relative to origin)
                PolylineCurve violinCurve = CreateViolinCurve(data, violinCenter, mean, stdDev, maxWidth, bandwidth, resolution);
                violinCurves.Add(violinCurve);

                // Create median line (fixed relative to origin)
                Line medianLine = new Line(
                    new Point3d(xPos - maxWidth / 2, origin.Y + median, origin.Z),
                    new Point3d(xPos + maxWidth / 2, origin.Y + median, origin.Z)
                );
                medianLines.Add(medianLine);

                // Create data points (fixed relative to origin)
                var branchPoints = new List<Point3d>();
                foreach (double d in data)
                {
                    branchPoints.Add(new Point3d(xPos, origin.Y + d, origin.Z));
                }
                dataPoints.AddRange(branchPoints, new Grasshopper.Kernel.Data.GH_Path(i));

                // Create mean line (fixed relative to origin)
                Line meanLine = new Line(
                    new Point3d(xPos - maxWidth / 2, origin.Y + mean, origin.Z),
                    new Point3d(xPos + maxWidth / 2, origin.Y + mean, origin.Z)
                );
                meanLines.Add(meanLine);

                // Create ±sigma lines (fixed relative to origin)
                var branchSigmaLines = new List<Line>();
                branchSigmaLines.Add(new Line( // +sigma line
                    new Point3d(xPos - maxWidth / 2, origin.Y + mean + stdDev, origin.Z),
                    new Point3d(xPos + maxWidth / 2, origin.Y + mean + stdDev, origin.Z)
                ));
                branchSigmaLines.Add(new Line( // -sigma line
                    new Point3d(xPos - maxWidth / 2, origin.Y + mean - stdDev, origin.Z),
                    new Point3d(xPos + maxWidth / 2, origin.Y + mean - stdDev, origin.Z)
                ));
                sigmaLines.AddRange(branchSigmaLines, new Grasshopper.Kernel.Data.GH_Path(i));
            }

            // Create tick points
            List<Point3d> xTicks = new List<Point3d>();
            for (int i = 0; i < datasetCount; i++)
            {
                // X ticks at grid base (y=origin.Y + gridYStart)
                xTicks.Add(new Point3d(
                    origin.X + (i + 0.5) * gridXSpacing,
                    origin.Y + gridYStart,
                    origin.Z
                ));
            }

            List<Point3d> yTicks = new List<Point3d>();
            List<double> yTickValues = new List<double>();
            // Only ticks above Y Grid Start (ignore the first grid line at Y Grid Start)
            for (int i = 1; i <= gridYDivisions; i++)
            {
                double yValue = gridYStart + i * gridYSpacing;
                yTicks.Add(new Point3d(origin.X, origin.Y + yValue, origin.Z));
                yTickValues.Add(yValue);
            }

            // Set outputs
            DA.SetDataList(0, violinCurves);
            DA.SetDataList(1, medianLines);
            DA.SetDataTree(2, dataPoints);
            DA.SetDataList(3, meanLines);
            DA.SetDataTree(4, sigmaLines);
            DA.SetDataList(5, gridLines);
            DA.SetDataList(6, xTicks);
            DA.SetDataList(7, yTicks);
            DA.SetDataList(8, yTickValues);
        }

        private void CreateGridLines(Point3d origin, double gridXSpacing, double gridYSpacing,
                                   int gridYDivisions, double gridYStart, int datasetCount,
                                   List<Line> gridLines)
        {
            // Vertical grid lines (X direction) - starting from Y Grid Start
            for (int i = 0; i <= datasetCount; i++)
            {
                double x = origin.X + i * gridXSpacing;
                double yStart = origin.Y + gridYStart;
                double yEnd = origin.Y + gridYStart + gridYDivisions * gridYSpacing;

                gridLines.Add(new Line(
                    new Point3d(x, yStart, origin.Z),
                    new Point3d(x, yEnd, origin.Z)
                ));
            }

            // Horizontal grid lines (Y direction) - starting from Y Grid Start
            for (int i = 0; i <= gridYDivisions; i++)
            {
                double y = origin.Y + gridYStart + i * gridYSpacing;
                double xStart = origin.X;
                double xEnd = origin.X + datasetCount * gridXSpacing;

                gridLines.Add(new Line(
                    new Point3d(xStart, y, origin.Z),
                    new Point3d(xEnd, y, origin.Z)
                ));
            }
        }

        private PolylineCurve CreateViolinCurve(List<double> data, Point3d center,
            double mean, double stdDev, double maxWidth, double bandwidth, int resolution)
        {
            // Calculate dynamic range based on mean and stdDev
            double padding = Math.Max(stdDev * 3.0, (data.Max() - data.Min()) * 0.5) * 1.05;
            double start = mean - padding;
            double end = mean + padding;
            double step = (end - start) / resolution;

            // Calculate density values
            double[] density = new double[resolution + 1];
            for (int i = 0; i <= resolution; i++)
            {
                double position = start + i * step;
                density[i] = GaussianKDE(data, position, bandwidth);
            }

            // Normalize and scale density values
            double maxDensity = density.Max();
            double scaleFactor = maxDensity > 0 ? maxWidth / (2 * maxDensity) : 0;

            // Create outline points
            List<Point3d> points = new List<Point3d>();

            // Right curve
            for (int i = 0; i <= resolution; i++)
            {
                double x = center.X + density[i] * scaleFactor;
                double y = center.Y + start + i * step;
                points.Add(new Point3d(x, y, center.Z));
            }

            // Left curve (reverse order)
            for (int i = resolution; i >= 0; i--)
            {
                double x = center.X - density[i] * scaleFactor;
                double y = center.Y + start + i * step;
                points.Add(new Point3d(x, y, center.Z));
            }

            // Close curve
            points.Add(points[0]);

            return new PolylineCurve(points);
        }

        private double GaussianKDE(List<double> data, double x, double bandwidth)
        {
            // Gaussian kernel density estimation
            double sum = 0;
            double constant = 1.0 / (Math.Sqrt(2 * Math.PI) * bandwidth);

            foreach (double d in data)
            {
                double u = (x - d) / bandwidth;
                sum += constant * Math.Exp(-0.5 * u * u);
            }

            return sum / data.Count;
        }

        private double CalculateBandwidth(List<double> data, double stdDev, double iqr)
        {
            // Silverman's rule of thumb with adjustments
            double n = data.Count;
            double h = 0.9 * Math.Min(stdDev, iqr / 1.34) * Math.Pow(n, -0.2);

            // Fallback to Silverman's simplified rule if h is too small
            if (h <= 0 || double.IsNaN(h))
            {
                h = 1.06 * stdDev * Math.Pow(n, -0.2);
            }

            // Minimum bandwidth protection
            double dataRange = data.Max() - data.Min();
            if (h <= 0 || h < dataRange / 100.0)
            {
                h = dataRange / 10.0;
            }

            return h;
        }

        private double CalculateMedian(List<double> data)
        {
            List<double> sorted = new List<double>(data);
            sorted.Sort();

            int count = sorted.Count;
            if (count % 2 == 0)
                return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
            else
                return sorted[count / 2];
        }

        private double CalculateIQR(List<double> data)
        {
            if (data.Count < 4)
                return (data.Max() - data.Min()) / 2.0;

            List<double> sorted = new List<double>(data);
            sorted.Sort();

            int count = sorted.Count;
            int q1Index = (int)Math.Floor(count * 0.25);
            int q3Index = (int)Math.Floor(count * 0.75);

            double q1 = sorted[q1Index];
            double q3 = sorted[q3Index];

            return q3 - q1;
        }

        private double CalculateStandardDeviation(List<double> data)
        {
            if (data.Count <= 1) return 0;

            double mean = data.Average();
            double sumSq = data.Sum(d => (d - mean) * (d - mean));
            return Math.Sqrt(sumSq / (data.Count - 1)); // 样本标准差 (n-1)
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_ViolinPlot;
        public override Guid ComponentGuid => new Guid("EFF7C7B6-3ADF-4EF3-8D54-5A3CC877EF6F");
    }
}