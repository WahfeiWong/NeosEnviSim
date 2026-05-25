using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using NeosEnviSim.Properties;

namespace NeosVisualization
{
    public class DataIntervalDistributionChartComponent : GH_Component
    {
        public DataIntervalDistributionChartComponent()
          : base("DataIntervalDistributionChart", "DisChart",
              "Creates a data interval distribution chart based on input parameters",
              "Neos", "Visualization")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Origin", "O", "Drawing origin (local coordinate system)", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddPointParameter("ReferencePoint", "RP", "Output reference point for clipping", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Data", "D", "Input data set (observations)", GH_ParamAccess.list);
            pManager.AddNumberParameter("XGridSpacing", "XS", "X-axis grid spacing in drawing units", GH_ParamAccess.item, 1);

            // 添加 XValueRange 参数
            pManager.AddIntervalParameter("XValueRange", "XR", "Value range represented by X grid spacing (default: [0,1])", GH_ParamAccess.item);

            pManager.AddNumberParameter("YGridSpacing", "YS", "Y-axis grid spacing in drawing units", GH_ParamAccess.item, 1);
            pManager.AddIntegerParameter("YCountPerStep", "YC", "Observation count per Y grid step", GH_ParamAccess.item, 1);

            // 为 XValueRange 设置默认值
            pManager[4].Optional = true; // 标记为可选
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddLineParameter("GridLines", "GL", "Visible grid lines", GH_ParamAccess.list);
            pManager.AddCurveParameter("Bars", "B", "Bar chart rectangles as closed curves", GH_ParamAccess.list);
            pManager.AddPointParameter("FeaturePoints", "FP", "Characteristic points of data within output grid", GH_ParamAccess.list);
            pManager.AddNumberParameter("FeatureValues", "FV", "Y-values of feature points (counts)", GH_ParamAccess.list);
            pManager.AddCurveParameter("TrendLine", "TL", "Interpolated trend line", GH_ParamAccess.item);
            pManager.AddPointParameter("XTicks", "XT", "X-axis tick points within output grid", GH_ParamAccess.list);
            pManager.AddNumberParameter("XTickValues", "XTV", "Values for X-axis ticks", GH_ParamAccess.list);
            pManager.AddPointParameter("YTicks", "YT", "Y-axis tick points within output grid", GH_ParamAccess.list);
            pManager.AddNumberParameter("YTickValues", "YTV", "Values for Y-axis ticks", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. Retrieve input data
            Point3d origin = Point3d.Unset;
            Point3d refPoint = Point3d.Unset;
            List<double> data = new List<double>();
            double xStep = 0;
            Interval xValueRange = new Interval(0, 1); // 默认值 [0,1]
            double yStep = 0;
            int yCountPerStep = 0;

            if (!DA.GetData(0, ref origin)) return;
            if (!DA.GetData(1, ref refPoint)) return;
            if (!DA.GetDataList(2, data)) return;
            if (!DA.GetData(3, ref xStep)) return;

            // 修复默认值逻辑 - 使用更可靠的方法
            if (!DA.GetData(4, ref xValueRange) || !xValueRange.IsValid)
            {
                // 使用默认值 [0,1]
                xValueRange = new Interval(0, 1);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Using default XValueRange [0,1]");
            }

            if (!DA.GetData(5, ref yStep)) return;
            if (!DA.GetData(6, ref yCountPerStep)) return;

            // 2. Validate inputs
            if (xStep <= 0 || yStep <= 0 || yCountPerStep <= 0 || !xValueRange.IsValid || data.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid input values");
                return;
            }

            // 3. Calculate data bins
            double binWidth = xValueRange.Length;
            double minVal = data.Min();
            double maxVal = data.Max();

            int binCount = (int)Math.Ceiling((maxVal - xValueRange.Min) / binWidth);
            binCount = Math.Max(1, binCount);

            int[] bins = new int[binCount];
            foreach (double val in data)
            {
                int index = (int)Math.Floor((val - xValueRange.Min) / binWidth);
                if (index >= 0 && index < binCount) bins[index]++;
            }

            // 4. Create geometry containers
            List<Line> gridLines = new List<Line>();
            List<Polyline> bars = new List<Polyline>();
            List<Point3d> featurePoints = new List<Point3d>();
            List<double> featureValues = new List<double>();
            List<Point3d> xTicks = new List<Point3d>();
            List<double> xTickValues = new List<double>();
            List<Point3d> yTicks = new List<Point3d>();
            List<double> yTickValues = new List<double>();

            // 5. Calculate chart dimensions
            double maxX = origin.X + (binCount + 1) * xStep;
            double maxCount = bins.Max();
            double maxY = origin.Y + ((maxCount / yCountPerStep) + 1) * yStep;

            // 6. 找到输出网格的最左侧X坐标
            double minOutputX = double.MaxValue;

            // 创建垂直网格线并找到最左侧输出的X坐标
            for (int i = 0; i <= binCount + 1; i++)
            {
                double x = origin.X + i * xStep;
                if (x < refPoint.X) continue;

                // 记录最左侧输出的X坐标
                if (x < minOutputX) minOutputX = x;

                Line vertLine = new Line(
                    new Point3d(x, Math.Max(origin.Y, refPoint.Y), origin.Z),
                    new Point3d(x, maxY, origin.Z)
                );
                gridLines.Add(vertLine);
            }

            // 如果没有找到有效的minOutputX，使用参考点的X坐标
            if (minOutputX == double.MaxValue) minOutputX = refPoint.X;

            // 创建水平网格线
            int yLineCount = (int)Math.Ceiling((maxY - origin.Y) / yStep);
            for (int j = 0; j <= yLineCount; j++)
            {
                double y = origin.Y + j * yStep;
                if (y < refPoint.Y) continue;

                Line horzLine = new Line(
                    new Point3d(minOutputX, y, origin.Z),
                    new Point3d(maxX, y, origin.Z)
                );
                gridLines.Add(horzLine);
            }

            // 7. 计算底部Y坐标（用于刻度点）
            double bottomY = Math.Max(origin.Y, refPoint.Y);

            // 8. Create bars and feature points (only within output grid)
            for (int i = 0; i < binCount; i++)
            {
                double xLeft = origin.X + i * xStep;
                double xRight = xLeft + xStep;
                double yVal = origin.Y + (bins[i] * yStep / yCountPerStep);

                // 跳过完全在参考点左侧或下方的柱状图
                if (xRight < refPoint.X || yVal < refPoint.Y) continue;

                // 创建柱状图矩形（闭合多边形）
                Polyline bar = new Polyline();
                bar.Add(xLeft, bottomY, origin.Z);
                bar.Add(xRight, bottomY, origin.Z);
                bar.Add(xRight, Math.Max(yVal, refPoint.Y), origin.Z);
                bar.Add(xLeft, Math.Max(yVal, refPoint.Y), origin.Z);
                bar.Add(bar[0]); // 闭合多边形
                bars.Add(bar);

                // 创建特征点（柱顶中点）仅当在输出网格内
                Point3d fp = new Point3d(
                    (xLeft + xRight) / 2,
                    Math.Max(yVal, refPoint.Y),
                    origin.Z
                );

                // 确保特征点在输出网格内
                if (fp.X >= minOutputX && fp.Y >= refPoint.Y)
                {
                    featurePoints.Add(fp);
                    featureValues.Add(bins[i]);
                }
            }

            // 9. Create trend line (only within output grid)
            Curve trendCurve = null;
            if (featurePoints.Count > 1)
            {
                Polyline trendLine = new Polyline(featurePoints);
                if (trendLine.Count > 1)
                    trendCurve = trendLine.ToNurbsCurve();
            }

            // 10. Create X-axis ticks (only within output grid, avoiding bottom-left corner)
            for (int i = 0; i <= binCount; i++)
            {
                double x = origin.X + i * xStep;
                // 跳过参考点左侧的点
                if (x < refPoint.X) continue;

                // 创建刻度点（在底部线上，但不在左下角）
                if (x > minOutputX || (Math.Abs(x - minOutputX) < 0.0001))
                {
                    xTicks.Add(new Point3d(x, bottomY, origin.Z));
                    xTickValues.Add(xValueRange.Min + i * binWidth);
                }
            }

            // 11. Create Y-axis ticks (only within output grid, avoiding bottom-left corner)
            for (int j = 0; j <= yLineCount; j++)
            {
                double y = origin.Y + j * yStep;
                // 跳过参考点下方的点
                if (y < refPoint.Y) continue;

                // 跳过左下角点（当y等于底部Y时）
                if (y > bottomY || (Math.Abs(y - bottomY) < 0.0001 && j > 0))
                {
                    yTicks.Add(new Point3d(minOutputX, y, origin.Z));
                    yTickValues.Add(j * yCountPerStep);
                }
            }

            // 12. Set outputs
            DA.SetDataList(0, gridLines);
            DA.SetDataList(1, bars.Select(b => (Curve)b.ToNurbsCurve()));
            DA.SetDataList(2, featurePoints);
            DA.SetDataList(3, featureValues);
            DA.SetData(4, trendCurve);
            DA.SetDataList(5, xTicks);
            DA.SetDataList(6, xTickValues);
            DA.SetDataList(7, yTicks);
            DA.SetDataList(8, yTickValues);
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_DataIntervalDistributionChart;
        public override Guid ComponentGuid => new Guid("F6BCBDE7-9F9F-49DF-BF34-B67060D7B777");
    }
}