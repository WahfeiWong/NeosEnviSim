using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;

namespace NeosStatistic
{
    public class NumRatioComponent : GH_Component
    {
        public NumRatioComponent()
          : base("Number Ratio", 
                 "NR", 
                 "Calculates specific numbers ratio based on input values and thresholds",
                 "Neos",
                 "Statistic")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Numbers", "N", "List of numbers", GH_ParamAccess.list);
            pManager.AddNumberParameter("Lower Threshold", "Lo", "Lower threshold", GH_ParamAccess.item);
            pManager.AddNumberParameter("Upper Threshold", "Up", "Upper threshold", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Ratio", "R", "Proportion of subdomain values", GH_ParamAccess.item);
            pManager.AddIntegerParameter("CountInSubdomain", "C_in", "Count of values in subdomain", GH_ParamAccess.item);
            pManager.AddIntegerParameter("CountOutSubdomain", "C_out", "Count of values out of subdomain", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 初始化输入变量
            var numbers = new List<double>();
            double lowerThreshold = 0;
            double upperThreshold = 0;

            // 获取数据（带错误检查）
            if (!DA.GetDataList(0, numbers))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid number list");
                return;
            }

            if (!DA.GetData(1, ref lowerThreshold) ||
                !DA.GetData(2, ref upperThreshold))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid thresholds");
                return;
            }

            // 处理空列表
            if (numbers.Count == 0)
            {
                DA.SetData(0, 0.0);
                DA.SetData(1, 0);
                DA.SetData(2, 0);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Empty number list");
                return;
            }

            // 计算有效范围
            double actualLower = Math.Min(lowerThreshold, upperThreshold);
            double actualUpper = Math.Max(lowerThreshold, upperThreshold);

            // 统计范围内外的数量
            int total = numbers.Count;
            int countIn = numbers.Count(v => v >= actualLower && v <= actualUpper);
            int countOut = total - countIn;

            // 计算比例
            double ratio = total > 0 ? Math.Round(countIn / (double)total, 4) : 0;

            // 设置输出
            DA.SetData(0, ratio);
            DA.SetData(1, countIn);
            DA.SetData(2, countOut);
        }

        // 添加组件图标支持
        protected override System.Drawing.Bitmap Icon => Resources.icon_numratio;

        // 添加组件GUID
        public override Guid ComponentGuid => new Guid("3B7A05A5-9FC6-4C38-ADD5-E9FCA5D5942E");
    }
}



