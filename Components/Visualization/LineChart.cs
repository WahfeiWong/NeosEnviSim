using Grasshopper.Kernel;
using Rhino.Geometry;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeosVisualization
{
    public class LineChartComponent : GH_Component
    {
        public LineChartComponent()
          : base("Line Chart",
                 "LineC",
                 "Draws a line chart with grid lines",
                 "Neos",
                 "Visualization")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Data", "D", "Input data set", GH_ParamAccess.list);
            pManager.AddPointParameter("Origin", "O", "Chart origin point (World XY plane)", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("X Spacing", "Xs", "Grid spacing in X direction", GH_ParamAccess.item, 10.0);
            pManager.AddNumberParameter("Y Spacing", "Ys", "Grid spacing in Y direction", GH_ParamAccess.item, 10.0);
            // 新增Y轴起始值输入
            pManager.AddNumberParameter("Y Start Value", "YSV", "Start value for Y grid (world Y coordinate)", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Polyline", "P", "Resulting polyline", GH_ParamAccess.item);
            pManager.AddCurveParameter("Grid Lines", "G", "Grid lines", GH_ParamAccess.list);
            pManager.AddPointParameter("Data Points", "DP", "Data points", GH_ParamAccess.list);
            pManager.AddPointParameter("X Axis Points", "XAP", "Grid intersections on X-axis", GH_ParamAccess.list);
            pManager.AddPointParameter("Y Axis Points", "YAP", "Grid intersections on Y-axis", GH_ParamAccess.list);
            pManager.AddNumberParameter("Y Axis Labels", "YL", "Y-axis labels (Y coordinate differences from origin)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 获取输入数据
            List<double> data = new List<double>();
            Point3d origin = Point3d.Origin;
            double xSpacing = 10.0;
            double ySpacing = 10.0;
            double yStart = 0.0; // 新增YSV输入

            if (!DA.GetDataList(0, data)) return;
            if (!DA.GetData(1, ref origin)) return;
            if (!DA.GetData(2, ref xSpacing) || xSpacing <= 0) return;
            if (!DA.GetData(3, ref ySpacing) || ySpacing <= 0) return;
            if (!DA.GetData(4, ref yStart)) return; // 获取YSV

            if (data.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Data set is empty");
                return;
            }

            // 计算数据点的绝对Y坐标
            double absMinY = data.Min() + origin.Y;
            double absMaxY = data.Max() + origin.Y;

            // 计算网格顶部（不小于最大数据点Y值且是ySpacing的倍数）
            double gridTop = Math.Ceiling(absMaxY / ySpacing) * ySpacing;
            // 确保网格顶部不小于YSV
            if (gridTop < yStart) gridTop = yStart;

            // 计算水平网格线数量和实际网格顶部
            int yGridCount;
            double yTop;
            if (Math.Abs(gridTop - yStart) < 1e-10)
            {
                yGridCount = 1;
                yTop = yStart;
            }
            else
            {
                yGridCount = (int)Math.Ceiling((gridTop - yStart) / ySpacing) + 1;
                yTop = yStart + (yGridCount - 1) * ySpacing;
            }

            // 计算X范围
            double xStart = origin.X;
            double xEnd = origin.X + (2 * data.Count) * xSpacing;

            // 创建网格线
            List<Line> gridLines = new List<Line>();
            List<Point3d> xAxisPoints = new List<Point3d>();
            List<Point3d> yAxisPoints = new List<Point3d>();

            // 生成水平网格线（Y方向）
            for (int j = 0; j < yGridCount; j++)
            {
                double y = yStart + j * ySpacing;
                Line horizontalLine = new Line(
                    new Point3d(xStart, y, origin.Z),
                    new Point3d(xEnd, y, origin.Z));
                gridLines.Add(horizontalLine);

                // 添加到Y轴点集（跳过第一条线，即不包含左下角点）
                if (j > 0)
                {
                    yAxisPoints.Add(new Point3d(origin.X, y, origin.Z));
                }
            }

            // 生成垂直网格线（X方向）
            for (int i = 0; i <= 2 * data.Count; i++)
            {
                double x = origin.X + i * xSpacing;
                Line verticalLine = new Line(
                    new Point3d(x, yStart, origin.Z), // 从YSV开始
                    new Point3d(x, yTop, origin.Z));  // 到实际网格顶部
                gridLines.Add(verticalLine);
            }

            // 生成X轴刻度点（数据点在YSV水平线上的投影）
            for (int i = 0; i < data.Count; i++)
            {
                double x = origin.X + (2 * i + 1) * xSpacing;
                xAxisPoints.Add(new Point3d(x, yStart, origin.Z));
            }

            // 计算Y轴标记值
            List<double> yLabels = new List<double>();
            foreach (Point3d pt in yAxisPoints)
            {
                yLabels.Add(pt.Y - origin.Y);
            }

            // 创建数据点
            List<Point3d> dataPoints = new List<Point3d>();
            for (int i = 0; i < data.Count; i++)
            {
                double x = origin.X + (2 * i + 1) * xSpacing;
                double y = origin.Y + data[i];
                dataPoints.Add(new Point3d(x, y, origin.Z));
            }

            // 创建折线
            Polyline polyline = new Polyline(dataPoints);

            // 设置输出
            DA.SetData(0, polyline);
            DA.SetDataList(1, gridLines);
            DA.SetDataList(2, dataPoints);
            DA.SetDataList(3, xAxisPoints);
            DA.SetDataList(4, yAxisPoints);
            DA.SetDataList(5, yLabels);
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_LineChart;
        public override Guid ComponentGuid => new Guid("E0C2F6A3-8D4A-4F7F-BF1C-1A3B8C9D0E2F");
    }
}