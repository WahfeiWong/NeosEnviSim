using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using NeosEnviSim.Properties;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace NeosVisualization
{
    public class BoxPlotIntegrated : GH_Component
    {
        // 绘制数据存储字段
        private List<Line> _gridLines;              // 网格线（水平+垂直）
        private List<Rectangle3d> _boxes;           // 箱体矩形
        private List<Line> _medianLines;            // 中位数线
        private List<Line> _whiskerLines;           // 须线（垂直+水平端帽）
        private List<Circle> _outlierCircles;       // 异常值圆点
        private List<TextLabel> _textLabels;        // 文本标签（X轴标签、Y轴数值）

        // 样式字段（从输入参数读取）
        private Color _labelColor;                  // 标签文字颜色
        private Color _boxColor;                    // 箱体边框和须线颜色
        private Color _gridColor;                   // 网格线颜色
        private double _lineWeight;                 // 箱体和须线线宽
        private double _gridWeight;                 // 网格线线宽
        private double _textHeight;                 // 文字高度（世界单位）
        private double _outlierRadius;               // 异常值圆点半径（世界单位）

        // 内部文本标签类
        private class TextLabel
        {
            public Point3d Position;
            public string Text;
            public Color Color;
            public double Height;
            public TextHorizontalAlignment HAlign;
            public TextVerticalAlignment VAlign;
        }

        public BoxPlotIntegrated()
          : base("Box Plot", "BoxPlot",
                "Create a multi-column box plot with direct viewport drawing",
                "Neos", "Visualization")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Origin", "O", "Drawing origin point", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Data", "D", "Input data tree (one branch per box)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Box Width", "W", "Width of each box", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Scale Factor", "S", "Visual scale factor", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Grid X Spacing", "GX", "Width of each grid column", GH_ParamAccess.item, 20.0);
            pManager.AddNumberParameter("Grid Y Spacing", "GY", "Vertical grid line spacing (data units)", GH_ParamAccess.item, 10.0);
            pManager.AddTextParameter("X Labels", "XL", "Labels for each column", GH_ParamAccess.list);
            pManager.AddColourParameter("Label Color", "LC", "Text color for labels", GH_ParamAccess.item, Color.Black);
            pManager.AddNumberParameter("Line Weight", "LW", "Box plot line weight", GH_ParamAccess.item, 1.0);
            pManager.AddColourParameter("Box Color", "BC", "Box and whisker color", GH_ParamAccess.item, Color.DarkBlue);
            pManager.AddNumberParameter("Grid Weight", "GW", "Grid line weight", GH_ParamAccess.item, 0.5);
            pManager.AddColourParameter("Grid Color", "GC", "Grid line color", GH_ParamAccess.item, Color.Gray);
            pManager.AddNumberParameter("Text Height", "TH", "Text label height (world units)", GH_ParamAccess.item, 2.0);
            pManager.AddNumberParameter("Outlier Radius ", "R", "Outlier Radius (world units)", GH_ParamAccess.item, 0.2);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // 所有输出均按列组织为数据树（每个分支对应一个输入数据列）
            pManager.AddNumberParameter("Statistics", "S", "Per-column statistics: Min, Q1, Median, Q3, Max (tree)", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Outliers", "Out", "Per-column outlier values (tree)", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Box Curves", "B", "Per-column closed polyline curve for each box (Q1-Q3 rectangle)", GH_ParamAccess.tree);
            pManager.AddPointParameter("Outlier Points", "OP", "Per-column outlier point coordinates (tree)", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. 获取基础输入
            Point3d origin = Point3d.Origin;
            GH_Structure<GH_Number> dataTree;
            double boxWidth = 10.0, scale = 1.0, gridX = 20.0, gridY = 10.0;
            List<string> xLabels = new List<string>();

            if (!DA.GetData(0, ref origin)) return;
            if (!DA.GetDataTree(1, out dataTree)) return;
            DA.GetData(2, ref boxWidth);
            DA.GetData(3, ref scale);
            DA.GetData(4, ref gridX);
            DA.GetData(5, ref gridY);
            DA.GetDataList(6, xLabels);

            // 2. 获取样式参数
            DA.GetData(7, ref _labelColor);
            DA.GetData(8, ref _lineWeight);
            DA.GetData(9, ref _boxColor);
            DA.GetData(10, ref _gridWeight);
            DA.GetData(11, ref _gridColor);
            DA.GetData(12, ref _textHeight);
            DA.GetData(13, ref _outlierRadius);

            int columnCount = dataTree.PathCount;
            if (columnCount == 0) return;

            // 3. 初始化存储字段
            _gridLines = new List<Line>();
            _boxes = new List<Rectangle3d>();
            _medianLines = new List<Line>();
            _whiskerLines = new List<Line>();
            _outlierCircles = new List<Circle>();
            _textLabels = new List<TextLabel>();

            // 构建输出数据树
            GH_Structure<GH_Number> statsTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> outliersTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Curve> boxCurvesTree = new GH_Structure<GH_Curve>();
            GH_Structure<GH_Point> outlierPointsTree = new GH_Structure<GH_Point>();

            // 4. 确定全局Y轴范围（数据单位）
            double globalMin = double.MaxValue;
            double globalMax = double.MinValue;
            foreach (var branch in dataTree.Branches)
            {
                if (branch.Count == 0) continue;
                foreach (GH_Number num in branch)
                {
                    double val = num.Value;
                    globalMin = Math.Min(globalMin, val);
                    globalMax = Math.Max(globalMax, val);
                }
            }

            // 5. 构建网格系统（水平线、垂直线及Y轴标签）
            double yStart = Math.Floor(globalMin / gridY) * gridY - gridY;
            double yEnd = Math.Ceiling(globalMax / gridY) * gridY + gridY;
            int stepCount = (int)Math.Round((yEnd - yStart) / gridY);
            double totalWidth = columnCount * gridX;

            // 水平网格线及Y轴标签
            for (int i = 0; i <= stepCount; i++)
            {
                double currentVal = yStart + i * gridY;
                double drawY = origin.Y + currentVal * scale;

                Point3d left = new Point3d(origin.X, drawY, origin.Z);
                Point3d right = new Point3d(origin.X + totalWidth, drawY, origin.Z);
                _gridLines.Add(new Line(left, right));

                // Y轴数值标签
                _textLabels.Add(new TextLabel
                {
                    Position = new Point3d(origin.X - 1.0, drawY, origin.Z),
                    Text = currentVal.ToString(),
                    Color = _labelColor,
                    Height = _textHeight,
                    HAlign = TextHorizontalAlignment.Right,
                    VAlign = TextVerticalAlignment.Middle
                });
            }

            // 垂直网格线（封闭表格）
            for (int i = 0; i <= columnCount; i++)
            {
                double x = origin.X + i * gridX;
                Point3d bottom = new Point3d(x, origin.Y + yStart * scale, origin.Z);
                Point3d top = new Point3d(x, origin.Y + yEnd * scale, origin.Z);
                _gridLines.Add(new Line(bottom, top));
            }

            // 6. 为每一列数据绘制箱型图并填充输出树
            for (int i = 0; i < columnCount; i++)
            {
                List<double> data = dataTree.Branches[i].Select(x => x.Value).ToList();
                if (data.Count == 0) continue;
                data.Sort();

                // 计算统计量
                double min = data.First();
                double max = data.Last();
                double q1 = GetQuartile(data, 0.25);
                double median = GetQuartile(data, 0.5);
                double q3 = GetQuartile(data, 0.75);
                double iqr = q3 - q1;
                double adjMin = data.Where(v => v >= q1 - 1.5 * iqr).Min();
                double adjMax = data.Where(v => v <= q3 + 1.5 * iqr).Max();

                double colCenterX = origin.X + (i + 0.5) * gridX;
                double boxLeft = colCenterX - boxWidth / 2;
                double boxRight = colCenterX + boxWidth / 2;

                // 转换Y坐标
                double yQ1 = origin.Y + q1 * scale;
                double yQ3 = origin.Y + q3 * scale;
                double yMedian = origin.Y + median * scale;
                double yAdjMin = origin.Y + adjMin * scale;
                double yAdjMax = origin.Y + adjMax * scale;

                // 箱体矩形
                Plane boxPlane = new Plane(new Point3d(boxLeft, yQ1, origin.Z), Vector3d.ZAxis);
                Rectangle3d boxRect = new Rectangle3d(boxPlane, boxWidth, yQ3 - yQ1);
                _boxes.Add(boxRect);

                // 输出箱体闭合曲线（每个列一个曲线）
                GH_Path path = new GH_Path(i);
                boxCurvesTree.Append(new GH_Curve(boxRect.ToPolyline().ToPolylineCurve()), path);

                // 中位数线
                Line medianLine = new Line(new Point3d(boxLeft, yMedian, origin.Z), new Point3d(boxRight, yMedian, origin.Z));
                _medianLines.Add(medianLine);

                // 上下须线垂直部分
                Line upperWhiskerVert = new Line(new Point3d(colCenterX, yQ3, origin.Z), new Point3d(colCenterX, yAdjMax, origin.Z));
                Line lowerWhiskerVert = new Line(new Point3d(colCenterX, yQ1, origin.Z), new Point3d(colCenterX, yAdjMin, origin.Z));
                _whiskerLines.Add(upperWhiskerVert);
                _whiskerLines.Add(lowerWhiskerVert);

                // 上下须线水平端帽（长度 = 0.85 * boxWidth）
                double capLength = 0.85 * boxWidth;
                double leftCapX = colCenterX - capLength / 2;
                double rightCapX = colCenterX + capLength / 2;
                Line lowerCap = new Line(new Point3d(leftCapX, yAdjMin, origin.Z), new Point3d(rightCapX, yAdjMin, origin.Z));
                Line upperCap = new Line(new Point3d(leftCapX, yAdjMax, origin.Z), new Point3d(rightCapX, yAdjMax, origin.Z));
                _whiskerLines.Add(lowerCap);
                _whiskerLines.Add(upperCap);

                // 异常值圆圈
                double outlierRadius = _outlierRadius;
                List<double> columnOutliers = new List<double>();
                List<Point3d> columnOutlierPoints = new List<Point3d>();
                foreach (double val in data.Where(v => v < q1 - 1.5 * iqr || v > q3 + 1.5 * iqr))
                {
                    Point3d center = new Point3d(colCenterX, origin.Y + val * scale, origin.Z);
                    Circle outlierCircle = new Circle(center, outlierRadius);
                    _outlierCircles.Add(outlierCircle);
                    columnOutliers.Add(val);
                    columnOutlierPoints.Add(center);
                }

                // 填充输出树：Statistics（每列5个值）
                statsTree.Append(new GH_Number(min), path);
                statsTree.Append(new GH_Number(q1), path);
                statsTree.Append(new GH_Number(median), path);
                statsTree.Append(new GH_Number(q3), path);
                statsTree.Append(new GH_Number(max), path);

                // 填充异常值数值和坐标点
                foreach (double outVal in columnOutliers)
                    outliersTree.Append(new GH_Number(outVal), path);
                foreach (Point3d pt in columnOutlierPoints)
                    outlierPointsTree.Append(new GH_Point(pt), path);

                // X轴标签
                if (i < xLabels.Count)
                {
                    _textLabels.Add(new TextLabel
                    {
                        Position = new Point3d(colCenterX, origin.Y + yStart * scale - _textHeight * 0.5, origin.Z),
                        Text = xLabels[i],
                        Color = _labelColor,
                        Height = _textHeight,
                        HAlign = TextHorizontalAlignment.Center,
                        VAlign = TextVerticalAlignment.Top
                    });
                }
            }

            // 7. 设置输出数据
            DA.SetDataTree(0, statsTree);
            DA.SetDataTree(1, outliersTree);
            DA.SetDataTree(2, boxCurvesTree);
            DA.SetDataTree(3, outlierPointsTree);

            // 刷新视口绘制
            this.OnDisplayExpired(true);
        }

        // 计算分位数
        private double GetQuartile(List<double> sortedData, double quartile)
        {
            double n = sortedData.Count;
            double pos = (n - 1) * quartile;
            int index = (int)pos;
            double fraction = pos - index;
            if (index + 1 < n)
                return sortedData[index] * (1 - fraction) + sortedData[index + 1] * fraction;
            return sortedData[index];
        }

        // 视口绘制：按顺序绘制所有图形元素
        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            // 1. 网格线
            if (_gridLines != null)
            {
                foreach (Line line in _gridLines)
                    args.Display.DrawLine(line, _gridColor, (int)_gridWeight);
            }

            // 2. 须线（垂直+水平端帽）
            if (_whiskerLines != null)
            {
                foreach (Line line in _whiskerLines)
                    args.Display.DrawLine(line, _boxColor, (int)_lineWeight);
            }

            // 3. 箱体矩形边框
            if (_boxes != null)
            {
                foreach (Rectangle3d rect in _boxes)
                    args.Display.DrawPolyline(rect.ToPolyline(), _boxColor, (int)_lineWeight);
            }

            // 4. 中位数线
            if (_medianLines != null)
            {
                foreach (Line line in _medianLines)
                    args.Display.DrawLine(line, _boxColor, (int)(_lineWeight + 1));
            }

            // 5. 异常值圆点
            if (_outlierCircles != null)
            {
                foreach (Circle circle in _outlierCircles)
                    args.Display.DrawCircle(circle, _boxColor, 1);
            }

            // 6. 文本标签
            if (_textLabels != null)
            {
                foreach (TextLabel label in _textLabels)
                {
                    Plane textPlane = new Plane(label.Position, Vector3d.ZAxis);
                    args.Display.Draw3dText(label.Text, label.Color, textPlane, label.Height, "Arial", false, false, label.HAlign, label.VAlign);
                }
            }
        }

        // 包围盒计算
        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox bbox = BoundingBox.Empty;
                if (_gridLines != null)
                    foreach (Line line in _gridLines)
                        bbox.Union(line.BoundingBox);
                if (_boxes != null)
                    foreach (Rectangle3d rect in _boxes)
                        bbox.Union(rect.BoundingBox);
                if (_medianLines != null)
                    foreach (Line line in _medianLines)
                        bbox.Union(line.BoundingBox);
                if (_whiskerLines != null)
                    foreach (Line line in _whiskerLines)
                        bbox.Union(line.BoundingBox);
                if (_outlierCircles != null)
                    foreach (Circle circle in _outlierCircles)
                        bbox.Union(circle.BoundingBox);
                if (_textLabels != null)
                    foreach (TextLabel label in _textLabels)
                        bbox.Union(new BoundingBox(label.Position, label.Position));
                return bbox;
            }
        }
        protected override System.Drawing.Bitmap Icon => Resources.icon_BoxPlot;
        public override Guid ComponentGuid => new Guid("eef6468d-ef6b-47ec-b4d3-536d687bef7f");
    }
}