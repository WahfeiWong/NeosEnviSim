using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeosVisualization
{
    public class PieChartComponent : GH_Component
    {
        public PieChartComponent()
            : base("Pie Chart", 
                   "PieC", 
                   "Generate pie chart from data analysis",
                   "Neos",
                   "Visualization")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Center", "C", "Center of pie chart (default: WorldXY origin)", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Data", "D", "Dataset for analysis", GH_ParamAccess.list);
            pManager.AddNumberParameter("Boundaries", "B", "Boundary values for data bins (exclusive min/max)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Radius", "R", "Radius of pie chart", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Sectors", "S", "Pie sectors as closed curves", GH_ParamAccess.list);
            pManager.AddPointParameter("Labels", "L", "Midpoint of each sector arc for labeling", GH_ParamAccess.list);
            pManager.AddNumberParameter("Ratios", "R", "Data ratio for each sector (decimal)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 获取输入参数
            Point3d center = Point3d.Origin;
            List<double> data = new List<double>();
            List<double> boundaries = new List<double>();
            double radius = 0.0;

            if (!DA.GetData(0, ref center)) return;
            if (!DA.GetDataList(1, data)) return;
            if (!DA.GetDataList(2, boundaries)) return;
            if (!DA.GetData(3, ref radius)) return;

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

            // 处理边界值
            boundaries = boundaries.Distinct().OrderBy(b => b).ToList();
            if (boundaries.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No boundaries provided - using single bin");
            }

            // 计算数据分布
            List<Bin> bins = CalculateBins(data, boundaries);

            // 生成饼图
            List<Curve> sectors = new List<Curve>();
            List<Point3d> labelPoints = new List<Point3d>();
            List<double> ratios = new List<double>();

            double currentAngle = 0.0;
            double total = bins.Sum(b => b.Count);

            foreach (Bin bin in bins)
            {
                if (bin.Count == 0) continue;

                double ratio = bin.Count / total;
                double angle = ratio * 360.0;

                // 创建扇形
                Curve sector = CreateSector(center, radius, currentAngle, currentAngle + angle);
                sectors.Add(sector);

                // 计算标签点
                double midAngle = currentAngle + angle / 2.0;
                Point3d labelPt = CalculateLabelPoint(center, radius, midAngle);
                labelPoints.Add(labelPt);
                ratios.Add(ratio);

                // 更新角度
                currentAngle += angle;
            }

            // 设置输出
            DA.SetDataList(0, sectors);
            DA.SetDataList(1, labelPoints);
            DA.SetDataList(2, ratios);
        }

        private List<Bin> CalculateBins(List<double> data, List<double> boundaries)
        {
            List<Bin> bins = new List<Bin>();
            int binCount = boundaries.Count + 1;

            // 初始化数据桶
            for (int i = 0; i < binCount; i++)
            {
                bins.Add(new Bin());
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

        private Curve CreateSector(Point3d center, double radius, double startAngle, double endAngle)
        {
            // 处理角度接近360度的情况
            double sweepAngle = endAngle - startAngle;
            if (Math.Abs(sweepAngle) >= 360.0 - 1e-6)
            {
                // 创建一个完整的圆
                Circle circle = new Circle(center, radius);
                return circle.ToNurbsCurve();
            }

            // 将角度转换为弧度
            double startRadians = RhinoMath.ToRadians(startAngle);
            double sweepRadians = RhinoMath.ToRadians(sweepAngle);

            // 计算圆弧的起点和终点
            Point3d startPoint = new Point3d(
                center.X + radius * Math.Cos(startRadians),
                center.Y + radius * Math.Sin(startRadians),
                center.Z
            );

            Point3d endPoint = new Point3d(
                center.X + radius * Math.Cos(startRadians + sweepRadians),
                center.Y + radius * Math.Sin(startRadians + sweepRadians),
                center.Z
            );

            // 使用三点法创建圆弧：起点、圆弧上的点、终点
            Point3d midPoint = CalculateMidPoint(center, radius, startRadians, sweepRadians);
            Arc arc = new Arc(startPoint, midPoint, endPoint);

            // 创建两条半径线（从圆心到起点和终点）
            Line startRadius = new Line(center, startPoint);
            Line endRadius = new Line(center, endPoint);

            // 创建闭合曲线 - 确保包含两条半径线和圆弧
            PolyCurve polyCurve = new PolyCurve();

            // 第一条半径线（从圆心到起点）
            polyCurve.Append(startRadius);

            // 圆弧（从起点到终点）
            polyCurve.Append(arc);

            // 第二条半径线（从终点回到圆心）
            // 注意：这里需要创建一条从终点指向圆心的线
            polyCurve.Append(new Line(endPoint, center));

            // 确保曲线闭合
            polyCurve.MakeClosed(0.001);

            return polyCurve;
        }

        private Point3d CalculateMidPoint(Point3d center, double radius, double startRadians, double sweepRadians)
        {
            // 计算圆弧中点位置
            double midAngle = startRadians + sweepRadians / 2.0;
            return new Point3d(
                center.X + radius * Math.Cos(midAngle),
                center.Y + radius * Math.Sin(midAngle),
                center.Z
            );
        }

        private Point3d CalculateLabelPoint(Point3d center, double radius, double angle)
        {
            double radians = RhinoMath.ToRadians(angle);
            return new Point3d(
                center.X + radius * Math.Cos(radians),
                center.Y + radius * Math.Sin(radians),
                center.Z
            );
        }
        protected override System.Drawing.Bitmap Icon => Resources.icon_PieChart;
        public override Guid ComponentGuid => new Guid("B0D5A8F1-2C4D-4E2F-9A3A-7D8B9C0D1E2F");
    }

    internal class Bin
    {
        public int Count { get; set; }
    }
}