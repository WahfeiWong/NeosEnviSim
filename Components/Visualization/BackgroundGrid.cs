using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;

namespace NeosVisualization
{
    public class GridSystemComponent : GH_Component
    {
        public GridSystemComponent() : base(
            "Background Grid",
            "BG",
            "Generates a grid system for data visualization background",
            "Neos",
            "Visualization")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Origin", "O", "Grid origin point (bottom-left corner)", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("X Spacing", "XS", "Spacing between vertical grid lines", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("X Divisions", "XD", "Number of divisions along X-axis", GH_ParamAccess.item, 10);
            pManager.AddNumberParameter("Y Spacing", "YS", "Spacing between horizontal grid lines", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Y Divisions", "YD", "Number of divisions along Y-axis", GH_ParamAccess.item, 10);
            pManager.AddNumberParameter("Y Break", "YB", "Y-coordinate break value to filter grid", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Grid Lines", "Lines", "Grid lines with Y >= Y Break", GH_ParamAccess.list);
            pManager.AddSurfaceParameter("Grid Cells", "Cells", "Grid cells with Y >= Y Break", GH_ParamAccess.list);
            pManager.AddLineParameter("X Axis", "X", "Main X-axis line at Y Break level", GH_ParamAccess.item);
            pManager.AddLineParameter("Y Axis", "Y", "Main Y-axis line from Y Break upwards", GH_ParamAccess.item);
            pManager.AddPointParameter("X Points", "XPts", "Grid points projected to Y Break line", GH_ParamAccess.list);
            pManager.AddPointParameter("Y Points", "YPts", "Grid points along Y-axis with Y >= Y Break", GH_ParamAccess.list);
            pManager.AddNumberParameter("Y Labels", "YLbls", "Y-coordinate differences for horizontal grid lines with Y >= Y Break", GH_ParamAccess.list);
            pManager.AddPointParameter("X Mid Points", "XMids", "Midpoints projected to Y Break line", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. 获取输入数据
            Point3d origin = Point3d.Origin;
            double xSpacing = 1.0;
            int xDivisions = 10;
            double ySpacing = 1.0;
            int yDivisions = 10;
            double yBreak = 0.0;

            if (!DA.GetData(0, ref origin)) return;
            if (!DA.GetData(1, ref xSpacing)) return;
            if (!DA.GetData(2, ref xDivisions)) return;
            if (!DA.GetData(3, ref ySpacing)) return;
            if (!DA.GetData(4, ref yDivisions)) return;
            if (!DA.GetData(5, ref yBreak)) return;

            // 验证输入
            if (xSpacing <= 0 || ySpacing <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Spacing values must be positive");
                return;
            }

            if (xDivisions <= 0 || yDivisions <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Division counts must be positive");
                return;
            }

            // 2. 计算网格尺寸
            double totalWidth = xSpacing * xDivisions;
            double totalHeight = ySpacing * yDivisions;

            // 3. 生成仅包含Y≥断点部分的网格元素
            var gridLines = GenerateUpperGridLines(origin, xSpacing, xDivisions, ySpacing, yDivisions, yBreak);
            var gridCells = GenerateUpperGridCells(origin, xSpacing, xDivisions, ySpacing, yDivisions, yBreak);
            var axes = GenerateUpperAxes(origin, totalWidth, totalHeight, yBreak);
            var gridPoints = GenerateUpperGridPoints(origin, xSpacing, xDivisions, ySpacing, yDivisions, yBreak);
            var yLabels = GenerateUpperYLabels(origin, ySpacing, yDivisions, yBreak);
            var xMidPoints = GenerateUpperXMidPoints(origin, xSpacing, xDivisions, yBreak);

            // 4. 设置输出
            DA.SetDataList(0, gridLines);
            DA.SetDataList(1, gridCells);
            DA.SetData(2, axes.xAxis);
            DA.SetData(3, axes.yAxis);
            DA.SetDataList(4, gridPoints.xPoints);
            DA.SetDataList(5, gridPoints.yPoints);
            DA.SetDataList(6, yLabels);
            DA.SetDataList(7, xMidPoints);
        }

        // 生成仅Y≥断点的网格线
        private List<Curve> GenerateUpperGridLines(
            Point3d origin,
            double xSpacing, int xDivisions,
            double ySpacing, int yDivisions,
            double yBreak)
        {
            List<Curve> lines = new List<Curve>();
            double totalWidth = xSpacing * xDivisions;
            double totalHeight = ySpacing * yDivisions;
            double tolerance = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;

            // 生成垂直线 - 从断点向上延伸
            for (int i = 0; i <= xDivisions; i++)
            {
                double x = origin.X + i * xSpacing;
                Line line = new Line(
                    new Point3d(x, yBreak, origin.Z),
                    new Point3d(x, origin.Y + totalHeight, origin.Z));
                lines.Add(new LineCurve(line));
            }

            // 生成水平线 - 仅断点及以上的部分
            for (int j = 0; j <= yDivisions; j++)
            {
                double y = origin.Y + j * ySpacing;
                if (y >= yBreak - tolerance)
                {
                    Line line = new Line(
                        new Point3d(origin.X, y, origin.Z),
                        new Point3d(origin.X + totalWidth, y, origin.Z));
                    lines.Add(new LineCurve(line));
                }
            }

            return lines;
        }

        // 生成仅Y≥断点的网格单元
        private List<Surface> GenerateUpperGridCells(
            Point3d origin,
            double xSpacing, int xDivisions,
            double ySpacing, int yDivisions,
            double yBreak)
        {
            List<Surface> cells = new List<Surface>();
            double tolerance = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;

            for (int i = 0; i < xDivisions; i++)
            {
                for (int j = 0; j < yDivisions; j++)
                {
                    double minY = origin.Y + j * ySpacing;
                    double maxY = origin.Y + (j + 1) * ySpacing;

                    // 仅包含完全或部分在断点上方的单元格
                    if (maxY >= yBreak - tolerance)
                    {
                        // 调整单元格底部到断点位置
                        double actualMinY = Math.Max(minY, yBreak);

                        Point3d pt1 = new Point3d(
                            origin.X + i * xSpacing,
                            actualMinY,
                            origin.Z);

                        Point3d pt2 = new Point3d(
                            origin.X + (i + 1) * xSpacing,
                            actualMinY,
                            origin.Z);

                        Point3d pt3 = new Point3d(
                            origin.X + (i + 1) * xSpacing,
                            origin.Y + (j + 1) * ySpacing,
                            origin.Z);

                        Point3d pt4 = new Point3d(
                            origin.X + i * xSpacing,
                            origin.Y + (j + 1) * ySpacing,
                            origin.Z);

                        cells.Add(NurbsSurface.CreateFromCorners(pt1, pt2, pt3, pt4));
                    }
                }
            }

            return cells;
        }

        // 生成坐标轴（X轴在断点位置，Y轴从断点向上）
        private (Line xAxis, Line yAxis) GenerateUpperAxes(
            Point3d origin,
            double totalWidth, double totalHeight,
            double yBreak)
        {
            // X轴（水平轴）在断点位置
            Line xAxis = new Line(
                new Point3d(origin.X, yBreak, origin.Z),
                new Point3d(origin.X + totalWidth, yBreak, origin.Z));

            // Y轴（垂直轴）从断点向上延伸
            Line yAxis = new Line(
                new Point3d(origin.X, yBreak, origin.Z),
                new Point3d(origin.X, origin.Y + totalHeight, origin.Z));

            return (xAxis, yAxis);
        }

        // 生成网格点（X点在断点线上，Y点仅断点及以上）
        private (List<Point3d> xPoints, List<Point3d> yPoints) GenerateUpperGridPoints(
            Point3d origin,
            double xSpacing, int xDivisions,
            double ySpacing, int yDivisions,
            double yBreak)
        {
            List<Point3d> xPoints = new List<Point3d>();
            List<Point3d> yPoints = new List<Point3d>();
            double tolerance = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;

            // X轴上的点投影到断点线
            for (int i = 0; i <= xDivisions; i++)
            {
                xPoints.Add(new Point3d(
                    origin.X + i * xSpacing,
                    yBreak,
                    origin.Z));
            }

            // Y轴上的点仅断点及以上
            for (int j = 0; j <= yDivisions; j++)
            {
                double y = origin.Y + j * ySpacing;
                if (y >= yBreak - tolerance)
                {
                    yPoints.Add(new Point3d(
                        origin.X,
                        y,
                        origin.Z));
                }
            }

            return (xPoints, yPoints);
        }

        // 生成Y轴标签（仅断点及以上）
        private List<double> GenerateUpperYLabels(Point3d origin, double ySpacing, int yDivisions, double yBreak)
        {
            List<double> yLabels = new List<double>();
            double tolerance = Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001;

            for (int j = 0; j <= yDivisions; j++)
            {
                double yValue = j * ySpacing;
                double absoluteY = origin.Y + yValue;

                if (absoluteY >= yBreak - tolerance)
                {
                    yLabels.Add(yValue);
                }
            }

            return yLabels;
        }

        // 生成X轴各格子中点投影到断点线
        private List<Point3d> GenerateUpperXMidPoints(Point3d origin, double xSpacing, int xDivisions, double yBreak)
        {
            List<Point3d> midPoints = new List<Point3d>();

            for (int i = 0; i < xDivisions; i++)
            {
                double midX = origin.X + (i + 0.5) * xSpacing;
                midPoints.Add(new Point3d(midX, yBreak, origin.Z));
            }

            return midPoints;
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_BackgroundGrid;
        public override Guid ComponentGuid => new Guid("eb1f1dab-ce69-48c5-8cb3-3ee522a75bc1");
    }
}