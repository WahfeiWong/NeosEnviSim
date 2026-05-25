using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;

namespace NeosOptics
{
    public class MixedLightMDERComponent : GH_Component
    {
        public MixedLightMDERComponent()
          : base("Mixed Light MDER", "MixedMDER",
              "Calculates Melanopic Daylight Efficacy Ratio (MDER) for mixed light environment (daylight + artificial light)",
              "Neos", "Optics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Daylight Illuminance", "Ed", "Vertical illuminance of natural light (lux)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Daylight MDER", "dMDER", "Melanopic Daylight Efficacy Ratio of daylight", GH_ParamAccess.item);
            pManager.AddNumberParameter("Electric Light Illuminance", "Ee", "Vertical illuminance of artificial light (lux)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Electric Light MDER", "eMDER", "Melanopic Daylight Efficacy Ratio of artificial light", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("MixedLightMDER", "MDER", "Average Melanopic Daylight Efficacy Ratio for mixed light environment", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 获取输入参数
            List<double> edList = new List<double>();
            double rd = 0.0;
            List<double> eeList = new List<double>();
            double re = 0.0;

            if (!DA.GetDataList(0, edList)) return;
            if (!DA.GetData(1, ref rd)) return;
            if (!DA.GetDataList(2, eeList)) return;
            if (!DA.GetData(3, ref re)) return;

            // 验证输入列表长度
            if (edList.Count != eeList.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Daylight and electric light illuminance lists must have the same number of values");
                return;
            }

            int pointCount = edList.Count;
            if (pointCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input lists cannot be empty");
                return;
            }

            double sumRc = 0.0;
            //double sumEml = 0.0;

            for (int i = 0; i < pointCount; i++)
            {
                double ed = edList[i];
                double ee = eeList[i];

                // 验证非负值
                if (ed < 0 || ee < 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Illuminance values cannot be negative");
                    return;
                }

                double totalIlluminance = ed + ee;//该点的融合光照度
                double eml = ed * rd + ee * re; // 该点的EML
                //sumEml += eml;

                // 计算该点的R值
                if (totalIlluminance > 0)
                {
                    sumRc += eml / totalIlluminance;
                }
                // 当总照度为0时，该点R值默认为0（不累加）
            }

            // 计算平均R值
            double rc = sumRc / pointCount;

            DA.SetData(0, rc);
        
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_Rc;
        public override Guid ComponentGuid => new Guid("2BE8C919-3182-4B42-BC74-D4AC22D6AEE2");
    }
}