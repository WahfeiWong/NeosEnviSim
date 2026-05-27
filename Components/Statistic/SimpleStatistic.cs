using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;

namespace NeosStatistic
{
    public class StatisticsComponent : GH_Component
    {
        public StatisticsComponent()
          : base("Statistic",
                 "Sta",
                 "Calculates mean, variance, standard deviation, and median",
                 "Neos",
                 "Statistic")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Data", "D", "Input data points", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Sample", "S", "True for sample statistics (n-1), false for population (n)", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // 调整输出顺序：数据量、平均值、中位数、方差、标准差、最大值、最小值
            pManager.AddNumberParameter("Data Count", "N", "Number of data points", GH_ParamAccess.item);
            pManager.AddNumberParameter("Mean", "μ", "Mean (average) of the data", GH_ParamAccess.item);
            pManager.AddNumberParameter("Median", "M", "Median of the data", GH_ParamAccess.item);
            pManager.AddNumberParameter("Variance", "σ²", "Calculated variance", GH_ParamAccess.item);
            pManager.AddNumberParameter("StdDev", "σ", "Standard deviation", GH_ParamAccess.item);
            pManager.AddNumberParameter("Maximum", "Max", "Maximum value in the data", GH_ParamAccess.item);
            pManager.AddNumberParameter("Minimum", "Min", "Minimum value in the data", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 获取输入数据
            List<double> data = new List<double>();
            bool isSample = true;

            if (!DA.GetDataList(0, data)) return;
            if (!DA.GetData(1, ref isSample)) return;

            // 检查数据有效性
            if (data.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input values could not be null");
                return;
            }

            if (isSample && data.Count < 2)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Sample statistics require at least 2 data points");
                return;
            }

            // 计算基本统计量
            int count = data.Count;
            double mean = data.Average();
            double median = CalculateMedian(data);

            // 计算方差和标准差
            double divisor = isSample ? count - 1 : count;
            double sumSquaredDifferences = data.Sum(x => Math.Pow(x - mean, 2));
            double variance = sumSquaredDifferences / divisor;
            double stdDev = Math.Sqrt(variance);

            // 计算最大值和最小值
            double max = data.Max();
            double min = data.Min();

            // 按新顺序设置输出
            DA.SetData(0, count);
            DA.SetData(1, mean);
            DA.SetData(2, median);
            DA.SetData(3, variance);
            DA.SetData(4, stdDev);
            DA.SetData(5, max);
            DA.SetData(6, min);
        }

        private double CalculateMedian(List<double> data)
        {
            List<double> sortedData = new List<double>(data);
            sortedData.Sort();
            int count = sortedData.Count;

            if (count % 2 == 0)
            {
                int midIndex = count / 2;
                return (sortedData[midIndex - 1] + sortedData[midIndex]) / 2.0;
            }
            else
            {
                return sortedData[count / 2];
            }
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_statistic;
        public override Guid ComponentGuid => new Guid("2A3B4C5D-6E7F-8A9B-0C1D-2E3F4A5B6C7E");
    }
}