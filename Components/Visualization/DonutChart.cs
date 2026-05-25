using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeosVisualization
{
    public class DonutChartComponent : GH_Component
    {
        public DonutChartComponent()
            : base("Donut Chart",
                   "DonutC",
                   "Generate ring chart from data analysis",
                   "Neos",
                   "Visualization")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Center", "C", "Center of ring chart (default: WorldXY origin)", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Data", "D", "Dataset for analysis", GH_ParamAccess.list);
            pManager.AddNumberParameter("Boundaries", "B", "Boundary values for data bins (exclusive min/max)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Radius", "R", "Outer radius of ring chart", GH_ParamAccess.item);
            pManager.AddNumberParameter("Ring Width", "W", "Width of the ring (difference between outer and inner radius)", GH_ParamAccess.item, 0.5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Segments", "S", "Ring segments as closed curves", GH_ParamAccess.list);
            pManager.AddPointParameter("Labels", "L", "Midpoint of each segment arc for labeling", GH_ParamAccess.list);
            pManager.AddNumberParameter("Ratios", "R", "Data ratio for each segment (decimal)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 获取输入参数
            Point3d center = Point3d.Origin;
            List<double> data = new List<double>();
            List<double> boundaries = new List<double>();
            double radius = 0.0;
            double ringWidth = 0.5;

            if (!DA.GetData(0, ref center)) return;
            if (!DA.GetDataList(1, data)) return;
            if (!DA.GetDataList(2, boundaries)) return;
            if (!DA.GetData(3, ref radius)) return;
            if (!DA.GetData(4, ref ringWidth)) return;

            // 验证输入
            if (data.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Data set is empty");
                return;
            }

            if (radius <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Radius must be positive");
                return;
            }

            if (ringWidth <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Ring width must be positive");
                return;
            }

            if (ringWidth >= radius)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Ring width must be less than radius");
                return;
            }

            // 处理边界值
            boundaries = boundaries.Distinct().OrderBy(b => b).ToList();
            if (boundaries.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No boundaries provided - using single bin");
            }

            // 计算数据分布
            List<RingBin> bins = CalculateBins(data, boundaries);

            // 生成圆环图
            List<Curve> segments = new List<Curve>();
            List<Point3d> labelPoints = new List<Point3d>();
            List<double> ratios = new List<double>();

            double currentAngle = 0.0; // 从X轴正向开始
            double total = bins.Sum(b => b.Count);
            double innerRadius = radius - ringWidth;

            foreach (RingBin bin in bins)
            {
                if (bin.Count == 0) continue;

                double ratio = bin.Count / total;
                double sweepAngle = ratio * 360.0;

                // 创建环段
                Curve segment = CreateRingSegment(center, innerRadius, radius, currentAngle, currentAngle + sweepAngle);
                segments.Add(segment);

                // 计算标签点（在环的中间位置）
                double midAngle = currentAngle + sweepAngle / 2.0;
                double midRadius = (innerRadius + radius) / 2.0;
                Point3d labelPt = CalculateLabelPoint(center, midRadius, midAngle);
                labelPoints.Add(labelPt);
                ratios.Add(ratio);

                // 更新角度
                currentAngle += sweepAngle;
            }

            // 设置输出
            DA.SetDataList(0, segments);
            DA.SetDataList(1, labelPoints);
            DA.SetDataList(2, ratios);
        }

        private List<RingBin> CalculateBins(List<double> data, List<double> boundaries)
        {
            List<RingBin> bins = new List<RingBin>();
            int binCount = boundaries.Count + 1;

            // 初始化数据桶
            for (int i = 0; i < binCount; i++)
            {
                bins.Add(new RingBin());
            }

            // 分配数据到桶中
            foreach (double value in data)
            {
                int binIndex = boundaries.FindIndex(b => value < b);
                if (binIndex < 0) binIndex = binCount - 1;
                bins[binIndex].Count++;
            }

            return bins;
        }

        private Curve CreateRingSegment(Point3d center, double innerRadius, double outerRadius, double startAngleDeg, double endAngleDeg)
        {
            double tolerance = 1e-6;
            double sweepAngleDeg = endAngleDeg - startAngleDeg;

            // 处理扫掠角大于等于360度的情况
            if (sweepAngleDeg >= 360.0 - tolerance)
            {
                // 创建两个同心圆
                Circle outerCircle = new Circle(center, outerRadius);
                Circle innerCircle = new Circle(center, innerRadius);

                // 创建环面
                Curve outerCurve = new ArcCurve(outerCircle);
                Curve innerCurve = new ArcCurve(innerCircle);

                // 反转内圆曲线方向
                innerCurve.Reverse();

                // 构建闭合曲线
                PolyCurve ring = new PolyCurve();
                ring.Append(outerCurve);
                ring.Append(innerCurve);
                ring.MakeClosed(tolerance);
                return ring;
            }

            // 将角度转换为弧度
            double startRad = RhinoMath.ToRadians(startAngleDeg);
            double endRad = RhinoMath.ToRadians(endAngleDeg);

            // 计算关键点
            Point3d outerStart = CalculatePointOnCircle(center, outerRadius, startAngleDeg);
            Point3d outerEnd = CalculatePointOnCircle(center, outerRadius, endAngleDeg);
            Point3d outerMid = CalculatePointOnCircle(center, outerRadius, (startAngleDeg + endAngleDeg) / 2);

            Point3d innerStart = CalculatePointOnCircle(center, innerRadius, startAngleDeg);
            Point3d innerEnd = CalculatePointOnCircle(center, innerRadius, endAngleDeg);
            Point3d innerMid = CalculatePointOnCircle(center, innerRadius, (startAngleDeg + endAngleDeg) / 2);

            // 创建外圆弧（顺时针方向）
            Arc outerArc = new Arc(outerStart, outerMid, outerEnd);

            // 创建内圆弧（顺时针方向）
            Arc innerArc = new Arc(innerEnd, innerMid, innerStart);

            // 创建连接线
            Line startRadial = new Line(innerStart, outerStart);
            Line endRadial = new Line(outerEnd, innerEnd);

            // 构建环段
            PolyCurve ringSegment = new PolyCurve();
            ringSegment.Append(outerArc);      // 外圆弧
            ringSegment.Append(endRadial);     // 端部径向线
            ringSegment.Append(innerArc);      // 内圆弧
            ringSegment.Append(startRadial);   // 起始径向线

            // 确保曲线闭合
            if (!ringSegment.IsClosed)
            {
                ringSegment.MakeClosed(tolerance);
            }

            return ringSegment;
        }

        // 辅助函数：计算圆上的点
        private Point3d CalculatePointOnCircle(Point3d center, double radius, double angleDeg)
        {
            double radians = RhinoMath.ToRadians(angleDeg);
            return new Point3d(
                center.X + radius * Math.Cos(radians),
                center.Y + radius * Math.Sin(radians),
                center.Z
            );
        }

        private Point3d CalculateLabelPoint(Point3d center, double radius, double angleDeg)
        {
            double radians = RhinoMath.ToRadians(angleDeg);
            return new Point3d(
                center.X + radius * Math.Cos(radians),
                center.Y + radius * Math.Sin(radians),
                center.Z
            );
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_DonutChart;
        public override Guid ComponentGuid => new Guid("48AD613F-45BB-418A-9F82-1F15F6A9076F");
    }

    internal class RingBin
    {
        public int Count { get; set; }
    }
}