using Grasshopper.Kernel;
using Rhino.Geometry;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeosVisualization
{
    public class CorrelationScatterPlotComponent : GH_Component
    {
        public CorrelationScatterPlotComponent()
          : base("Correlation Scatter Plot",
                 "CorrelationPlot",
                 "Generates scatter plot with correlation analysis",
                 "Neos",
                 "Visualization")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Origin", "O", "Plot origin point", GH_ParamAccess.item, new Point3d(0, 0, 0));
            pManager.AddNumberParameter("X Data", "X", "Independent variable data", GH_ParamAccess.list);
            pManager.AddNumberParameter("Y Data", "Y", "Dependent variable data", GH_ParamAccess.list);
            pManager.AddNumberParameter("Scale X", "SX", "X-axis scaling factor", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Scale Y", "SY", "Y-axis scaling factor", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Grid Spacing X", "DX", "X-axis grid spacing", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Grid Spacing Y", "DY", "Y-axis grid spacing", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Correlation Threshold","CT", 
                "The absolute value of Pearson's correlation coefficient R used as the threshold for determining whether data are correlated",GH_ParamAccess.item, 0.5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("Data Points", "P", "Plotted data points", GH_ParamAccess.list);
            pManager.AddLineParameter("Grid Lines", "G", "Coordinate grid lines", GH_ParamAccess.list);
            pManager.AddPointParameter("Left Grid Points", "LP", "Points on left grid line for labeling", GH_ParamAccess.list);
            pManager.AddPointParameter("Bottom Grid Points", "BP", "Points on bottom grid line for labeling", GH_ParamAccess.list);
            pManager.AddTextParameter("X Labels", "XL", "X-axis value labels", GH_ParamAccess.list);
            pManager.AddTextParameter("Y Labels", "YL", "Y-axis value labels", GH_ParamAccess.list);
            pManager.AddTextParameter("Regression", "Reg", "Regression equation or message", GH_ParamAccess.item);
            pManager.AddNumberParameter("Correlation", "R", "Correlation coefficient", GH_ParamAccess.item);
            pManager.AddLineParameter("Regression Line", "L", "Regression line within X data range", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 获取输入参数
            Point3d origin = new Point3d();
            List<double> xData = new List<double>();
            List<double> yData = new List<double>();
            double scaleX = 1.0;
            double scaleY = 1.0;
            double gridSpacingX = 1.0;
            double gridSpacingY = 1.0;
            double thresholdR = 0.5;

            if (!DA.GetData(0, ref origin)) return;
            if (!DA.GetDataList(1, xData)) return;
            if (!DA.GetDataList(2, yData)) return;
            if (!DA.GetData(3, ref scaleX)) return;
            if (!DA.GetData(4, ref scaleY)) return;
            if (!DA.GetData(5, ref gridSpacingX)) return;
            if (!DA.GetData(6, ref gridSpacingY)) return;
            if (!DA.GetData(7, ref thresholdR)) return; 

            // 验证数据
            if (xData.Count != yData.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "X and Y data must have the same number of elements");
                return;
            }

            if (xData.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No data provided");
                return;
            }

            if (gridSpacingX <= 0 || gridSpacingY <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Grid spacing must be positive values");
                return;
            }

            // 计算缩放后的数据点位置
            List<Point3d> dataPoints = new List<Point3d>();
            for (int i = 0; i < xData.Count; i++)
            {
                dataPoints.Add(new Point3d(
                    origin.X + xData[i] * scaleX,
                    origin.Y + yData[i] * scaleY,
                    origin.Z
                ));
            }

            // 计算数据范围（缩放后）
            double minX = xData.Min() * scaleX;
            double maxX = xData.Max() * scaleX;
            double minY = yData.Min() * scaleY;
            double maxY = yData.Max() * scaleY;

            // 计算网格范围（基于缩放后的坐标）
            double gridMinX = Math.Floor(minX / gridSpacingX) * gridSpacingX;
            double gridMaxX = Math.Ceiling(maxX / gridSpacingX) * gridSpacingX;
            double gridMinY = Math.Floor(minY / gridSpacingY) * gridSpacingY;
            double gridMaxY = Math.Ceiling(maxY / gridSpacingY) * gridSpacingY;

            // 确保网格覆盖所有数据点
            if (gridMinX > minX) gridMinX -= gridSpacingX;
            if (gridMaxX < maxX) gridMaxX += gridSpacingX;
            if (gridMinY > minY) gridMinY -= gridSpacingY;
            if (gridMaxY < maxY) gridMaxY += gridSpacingY;

            // 生成网格线
            List<Line> gridLines = new List<Line>();
            List<Point3d> leftGridPoints = new List<Point3d>();
            List<Point3d> bottomGridPoints = new List<Point3d>();
            List<string> xLabels = new List<string>();
            List<string> yLabels = new List<string>();

            // 垂直线（平行于Y轴）
            for (double x = gridMinX; x <= gridMaxX; x += gridSpacingX)
            {
                Point3d start = new Point3d(origin.X + x, origin.Y + gridMinY, origin.Z);
                Point3d end = new Point3d(origin.X + x, origin.Y + gridMaxY, origin.Z);
                gridLines.Add(new Line(start, end));
            }

            // 水平线（平行于X轴）
            for (double y = gridMinY; y <= gridMaxY; y += gridSpacingY)
            {
                Point3d start = new Point3d(origin.X + gridMinX, origin.Y + y, origin.Z);
                Point3d end = new Point3d(origin.X + gridMaxX, origin.Y + y, origin.Z);
                gridLines.Add(new Line(start, end));
            }

            // 生成左侧网格点（最左侧垂直线上的点，不包括左下角）
            double leftX = gridMinX;
            for (double y = gridMinY + gridSpacingY; y <= gridMaxY; y += gridSpacingY)
            {
                leftGridPoints.Add(new Point3d(origin.X + leftX, origin.Y + y, origin.Z));
                // Y轴标签值（实际数据值，注意：除以缩放系数得到原始值）
                yLabels.Add((y / scaleY).ToString("F2"));
            }

            // 生成底部网格点（最下方水平线上的点，不包括左下角）
            double bottomY = gridMinY;
            for (double x = gridMinX + gridSpacingX; x <= gridMaxX; x += gridSpacingX)
            {
                bottomGridPoints.Add(new Point3d(origin.X + x, origin.Y + bottomY, origin.Z));
                // X轴标签值（实际数据值，注意：除以缩放系数得到原始值）
                xLabels.Add((x / scaleX).ToString("F2"));
            }

            // 回归分析和相关性计算（使用原始数据，不受缩放影响）
            string regressionEquation = "";
            double correlation = 0.0;
            double m = 0.0;
            double b = 0.0;
            Line regressionLine = Line.Unset;

            if (xData.Count > 1)
            {
                // 计算皮尔逊相关系数
                double meanX = xData.Average();
                double meanY = yData.Average();

                double sumNumerator = 0.0;
                double sumDenomX = 0.0;
                double sumDenomY = 0.0;

                for (int i = 0; i < xData.Count; i++)
                {
                    double devX = xData[i] - meanX;
                    double devY = yData[i] - meanY;

                    sumNumerator += devX * devY;
                    sumDenomX += devX * devX;
                    sumDenomY += devY * devY;
                }

                correlation = sumNumerator / Math.Sqrt(sumDenomX * sumDenomY);

                // 计算回归系数
                m = sumNumerator / sumDenomX;
                b = meanY - m * meanX;

                // 判断是否显著相关（阈值可调整）
                if (Math.Abs(correlation) > thresholdR)
                {
                    regressionEquation = $"y = {m:F4}x + {b:F4} (R = {correlation:F3})";
                }
                else
                {
                    regressionEquation = $"No significant correlation (R = {correlation:F3})";
                }

                // 创建回归直线（在X数据范围内）
                double minXValue = xData.Min();
                double maxXValue = xData.Max();

                // 计算起点和终点（原始数据坐标）
                Point3d startPt = new Point3d(
                    origin.X + minXValue * scaleX,
                    origin.Y + (m * minXValue + b) * scaleY,
                    origin.Z
                );

                Point3d endPt = new Point3d(
                    origin.X + maxXValue * scaleX,
                    origin.Y + (m * maxXValue + b) * scaleY,
                    origin.Z
                );

                regressionLine = new Line(startPt, endPt);
            }
            else
            {
                regressionEquation = "Insufficient data for correlation analysis";
            }

            // 设置输出
            DA.SetDataList(0, dataPoints);
            DA.SetDataList(1, gridLines);
            DA.SetDataList(2, leftGridPoints);
            DA.SetDataList(3, bottomGridPoints);
            DA.SetDataList(4, xLabels);
            DA.SetDataList(5, yLabels);
            DA.SetData(6, regressionEquation);
            DA.SetData(7, correlation);
            DA.SetData(8, regressionLine);
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_CorrelationScatterPlot;
        public override Guid ComponentGuid => new Guid("7DA06C15-E5A1-43C9-874E-0BD52F693473");
    }
}