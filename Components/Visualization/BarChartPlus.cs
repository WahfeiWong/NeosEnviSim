using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using NeosEnviSim.Properties;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace NeosVisualization
{
    public class BarChartPlusEnhanced : GH_Component
    {
        // 私有字段：存储用于视口绘制的几何数据
        private List<Rectangle3d> _bars = new List<Rectangle3d>();
        private List<Line> _gridLines = new List<Line>();
        private List<Line> _sdLines = new List<Line>();
        private Polyline _trendLine = new Polyline();
        private List<TextLabel> _labels = new List<TextLabel>();

        // 私有字段：存储样式属性
        private Color _gridColor, _barColor, _trendColor, _sdColor, _labelColor, _topColor;
        private int _gridWeight, _barWeight;
        private double _sdWeight;
        private bool _showTrend;

        public double labelOffset = 0.3; // 标签位置偏移量

        // 内部类：用于封装文本绘制信息
        private class TextLabel
        {
            public Point3d Position;
            public string Text;
            public Color Color;
            public double Size;
            public TextHorizontalAlignment HAlign;
            public TextVerticalAlignment VAlign;
        }

        public BarChartPlusEnhanced()
          : base("Bar Chart", "BarChart",
              "Comprehensive bar chart component with Mean, SD, Grid, and Trend line.",
              "Neos", "Visualization")
        { }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // 基础几何参数 (Index 0-5)
            pManager.AddPointParameter("Origin", "O", "Chart origin point", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Data", "D", "Input DataTree (each branch calculates Mean/SD)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Bar Width", "BW", "Width of each bar", GH_ParamAccess.item, 0.8);
            pManager.AddNumberParameter("Scale Factor", "S", "Height scale factor", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Y Spacing", "dY", "Y-axis grid interval (actual units)", GH_ParamAccess.item, 1.0);
            pManager.AddTextParameter("X Labels", "XL", "Text labels for X axis", GH_ParamAccess.list);

            // 显示与样式参数 (Index 6-16)
            pManager.AddNumberParameter("Label Size", "LS", "Text size for XY labels", GH_ParamAccess.item, 0.5);
            pManager.AddColourParameter("Label Color", "LC", "Text color for XY labels", GH_ParamAccess.item, Color.Black);
            pManager.AddNumberParameter("Top Label Size", "TS", "Data label size (Set 0 to hide)", GH_ParamAccess.item, 0.5);
            pManager.AddColourParameter("Top Label Color", "TC", "Data label color", GH_ParamAccess.item, Color.Black);
            pManager.AddIntegerParameter("Grid Weight", "GW", "Line weight of grid", GH_ParamAccess.item, 1);
            pManager.AddColourParameter("Grid Color", "GC", "Color of grid lines", GH_ParamAccess.item, Color.DimGray);
            pManager.AddIntegerParameter("Bar Weight", "BWt", "Line weight of bars", GH_ParamAccess.item, 2);
            pManager.AddColourParameter("Bar Color", "BC", "Color of bar outlines", GH_ParamAccess.item, Color.DarkBlue);
            pManager.AddBooleanParameter("Show Trend", "ST", "Draw polyline connecting means", GH_ParamAccess.item, true);
            pManager.AddColourParameter("Trend Color", "TrC", "Trend line color", GH_ParamAccess.item, Color.Red);
            pManager.AddColourParameter("SD Color", "SDC", "Standard Deviation line color", GH_ParamAccess.item, Color.Black);
            pManager.AddNumberParameter("Lable Offset", "LO", "Offset distance of label position", GH_ParamAccess.item, 0.3);

            for (int i = 5; i <= 16; i++) pManager[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Means", "M", "Calculated mean values", GH_ParamAccess.list);
            pManager.AddNumberParameter("Standard Deviations", "SD", "Calculated SD values", GH_ParamAccess.list);
            pManager.AddCurveParameter("Bars", "B", "Bar outlines (Rectangles)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. 获取输入数据
            Point3d origin = Point3d.Origin;
            GH_Structure<GH_Number> dataTree;
            double barWidth = 0.8, scale = 1.0, ySpacing = 1.0;
            List<string> xLabels = new List<string>();

            if (!DA.GetData(0, ref origin)) return;
            if (!DA.GetDataTree(1, out dataTree)) return;
            DA.GetData(2, ref barWidth);
            DA.GetData(3, ref scale);
            DA.GetData(4, ref ySpacing);
            DA.GetDataList(5, xLabels);

            // 2. 获取样式参数
            double labelSize = 0.5, topSize = 0.5;
            DA.GetData(6, ref labelSize); DA.GetData(7, ref _labelColor);
            DA.GetData(8, ref topSize); DA.GetData(9, ref _topColor);
            DA.GetData(10, ref _gridWeight); DA.GetData(11, ref _gridColor);
            DA.GetData(12, ref _barWeight); DA.GetData(13, ref _barColor);
            DA.GetData(14, ref _showTrend); DA.GetData(15, ref _trendColor);
            DA.GetData(16, ref _sdColor);
            DA.GetData(17, ref labelOffset);
            _sdWeight = _barWeight * 1.5;

            // 3. 初始化/清理
            _bars.Clear(); _gridLines.Clear(); _sdLines.Clear(); _labels.Clear();
            List<Point3d> trendPoints = new List<Point3d>();
            List<double> meanValues = new List<double>();
            List<double> sdValues = new List<double>();
            List<Curve> barCurves = new List<Curve>();

            double xSpacing = barWidth * 2.0;
            double maxVisualHeight = 0;

            // 4. 处理数据树分支
            int groupIndex = 0;
            foreach (GH_Path path in dataTree.Paths)
            {
                var values = dataTree.get_Branch(path)
                    .Cast<GH_Number>()
                     .Select(x => x.Value)
                     .ToList();
                if (values.Count == 0) continue;

                // 统计计算
                double mean = values.Average();
                double variance = values.Select(v => Math.Pow(v - mean, 2)).Sum() / values.Count;
                double sd = Math.Sqrt(variance);

                meanValues.Add(mean);
                sdValues.Add(sd);

                // 视觉高度计算
                double vMean = mean * scale;
                double vSD = sd * scale;
                double currentX = origin.X + groupIndex * xSpacing;
                double centerX = currentX + (xSpacing / 2.0);

                // A. 绘制 Bar 矩形
                Plane barPlane = new Plane(new Point3d(currentX + (xSpacing - barWidth) / 2.0, origin.Y, origin.Z), Vector3d.ZAxis);
                Rectangle3d rect = new Rectangle3d(barPlane, barWidth, vMean);
                _bars.Add(rect);
                barCurves.Add(rect.ToPolyline().ToPolylineCurve());

                // 关键点：矩形顶部中点、标准差上下限点
                Point3d meanPt = new Point3d(centerX, origin.Y + vMean, origin.Z);
                Point3d topSDPt = new Point3d(centerX, meanPt.Y + vSD, origin.Z);
                Point3d bottomSDPt = new Point3d(centerX, meanPt.Y - vSD, origin.Z);

                // B. 标准差 "工" 字线
                _sdLines.Add(new Line(topSDPt, bottomSDPt)); // 垂直主轴线
                double capW = barWidth * 0.5;
                _sdLines.Add(new Line(new Point3d(centerX - capW / 2, topSDPt.Y, origin.Z), new Point3d(centerX + capW / 2, topSDPt.Y, origin.Z))); // 顶横线
                _sdLines.Add(new Line(new Point3d(centerX - capW / 2, bottomSDPt.Y, origin.Z), new Point3d(centerX + capW / 2, bottomSDPt.Y, origin.Z))); // 底横线

                // C. 文本标注
                // 标注点位置设为 topSDPt（工字顶部）
                if (topSize > 0 && sd != 0)
                {
                    _labels.Add(new TextLabel
                    {
                        Position = new Point3d(topSDPt.X, topSDPt.Y + labelOffset, topSDPt.Z),
                        Text = string.Format("{0:F2} \u00B1 {1:F2}", mean, sd),
                        Color = _topColor,
                        Size = topSize,
                        HAlign = TextHorizontalAlignment.Center,
                        VAlign = TextVerticalAlignment.Bottom
                    });
                }
                else
                {
                    _labels.Add(new TextLabel
                    {
                        Position = new Point3d(topSDPt.X, topSDPt.Y + labelOffset, topSDPt.Z),
                        Text = mean.ToString("F2"),
                        Color = _topColor,
                        Size = topSize,
                        HAlign = TextHorizontalAlignment.Center,
                        VAlign = TextVerticalAlignment.Bottom
                    });
                }

                // X 轴标签
                if (groupIndex < xLabels.Count)
                {
                    _labels.Add(new TextLabel
                    {
                        Position = new Point3d(centerX, origin.Y - labelOffset, origin.Z),
                        Text = xLabels[groupIndex],
                        Color = _labelColor,
                        Size = labelSize,
                        HAlign = TextHorizontalAlignment.Center,
                        VAlign = TextVerticalAlignment.Top
                    });
                }

                trendPoints.Add(meanPt);
                maxVisualHeight = Math.Max(maxVisualHeight, Math.Max(vMean + vSD, vMean));
                groupIndex++;
            }

            // 5. 趋势线
            _trendLine = new Polyline(trendPoints);

            // 6. 网格系统
            double totalChartWidth = groupIndex * xSpacing;
            int yIntervals = (int)Math.Ceiling((maxVisualHeight / scale) / ySpacing);
            if (yIntervals < 1) yIntervals = 1;

            for (int i = 0; i <= yIntervals; i++)
            {
                double actualValY = i * ySpacing;
                double visualY = actualValY * scale;
                _gridLines.Add(new Line(new Point3d(origin.X, origin.Y + visualY, origin.Z), new Point3d(origin.X + totalChartWidth, origin.Y + visualY, origin.Z)));

                // Y 轴数字标注
                _labels.Add(new TextLabel
                {
                    Position = new Point3d(origin.X - labelOffset, origin.Y + visualY, origin.Z),
                    Text = actualValY.ToString(),
                    Color = _labelColor,
                    Size = labelSize,
                    HAlign = TextHorizontalAlignment.Right,
                    VAlign = TextVerticalAlignment.Middle
                });
            }

            // 网格最左与最右竖线
            double fullGridHeight = yIntervals * ySpacing * scale;
            _gridLines.Add(new Line(origin, new Point3d(origin.X, origin.Y + fullGridHeight, origin.Z)));
            _gridLines.Add(new Line(new Point3d(origin.X + totalChartWidth, origin.Y, origin.Z), new Point3d(origin.X + totalChartWidth, origin.Y + fullGridHeight, origin.Z)));

            // 7. 设置输出
            DA.SetDataList(0, meanValues);
            DA.SetDataList(1, sdValues);
            DA.SetDataList(2, barCurves);
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            // 绘制顺序：网格 -> 标准差线 -> 柱状矩形 -> 趋势线 -> 文本标签

            foreach (Line gl in _gridLines)
                args.Display.DrawLine(gl, _gridColor, _gridWeight);

            foreach (Line sl in _sdLines)
                args.Display.DrawLine(sl, _sdColor, (int)_sdWeight);

            foreach (Rectangle3d bar in _bars)
                args.Display.DrawPolyline(bar.ToPolyline(), _barColor, _barWeight);

            if (_showTrend && _trendLine.Count > 1)
                args.Display.DrawPolyline(_trendLine, _trendColor, _barWeight);

            foreach (var lb in _labels)
            {
                args.Display.Draw3dText(lb.Text, lb.Color, new Plane(lb.Position, Vector3d.ZAxis), lb.Size, "Arial", false, false, lb.HAlign, lb.VAlign);
            }
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox b = base.ClippingBox;
                foreach (var rect in _bars) b.Union(rect.BoundingBox);
                foreach (var line in _sdLines) b.Union(line.BoundingBox);
                return b;
            }
        }

        public override Guid ComponentGuid => new Guid("27AF4D0F-5E64-4BFF-8E88-9BD46D462AF0");

        protected override Bitmap Icon => Resources.icon_barchart;
    }
}









