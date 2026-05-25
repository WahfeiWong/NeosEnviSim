using NeosEnviSim.Properties;
using Grasshopper.Kernel;
using System;

namespace NeosAcoustic
{
    public class NormalizedToSPL : GH_Component
    {
        public NormalizedToSPL()
          : base("Normalized to SPL", "NormToSPL",
              "Convert normalized audio values to Sound Pressure Level (SPL)",
               "Neos", "Acoustic")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Normalized Value", "Norm", "Normalized audio value (-1.0 to 1.0)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Max SPL", "MaxSPL", "Maximum SPL in dB (Maximum sound pressure level that the microphone system is capable of measuring,default = 120 dB)", GH_ParamAccess.item, 120.0);
            pManager.AddNumberParameter("Ref Pressure", "Ref", "Reference pressure in Pa (Reference value for the lower limit of human hearing threshold,default = 20μPa = 0.00002 Pa)", GH_ParamAccess.item, 0.00002);
            pManager.AddNumberParameter("Calibration", "Cal", "Microphone calibration factor,the microphone sensitivity coefficient requires measurement and calibration using professional equipment.(default = 1.0)", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("SPL", "SPL", "Sound Pressure Level in dB", GH_ParamAccess.item);
            pManager.AddNumberParameter("Pressure", "Pa", "Actual pressure in Pascals", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 获取输入参数
            double normalizedValue = 0.0;
            double maxSPL = 120.0;
            double refPressure = 0.00002; // 20 μPa
            double calibration = 1.0;//

            if (!DA.GetData(0, ref normalizedValue)) return;
            DA.GetData(1, ref maxSPL);
            DA.GetData(2, ref refPressure);
            DA.GetData(3, ref calibration);

            // 确保输入值在有效范围内
            normalizedValue = Math.Max(-1.0, Math.Min(1.0, normalizedValue));

            // 计算实际声压值（Pa）
            // 公式：p_actual = |normalizedValue| * p_max * calibration
            // 其中 p_max = 10^(maxSPL/20) * refPressure
            double p_max = Math.Pow(10, maxSPL / 20.0) * refPressure;
            double actualPressure = Math.Abs(normalizedValue) * p_max * calibration;

            // 计算声压级（SPL）
            // 公式：SPL = 20 * log10(p_actual / p_ref)
            double spl = 20.0 * Math.Log10(actualPressure / refPressure);

            // 确保SPL不会低于0
            spl = Math.Max(0, spl);

            // 设置输出
            DA.SetData(0, spl);
            DA.SetData(1, actualPressure);
        }
        protected override System.Drawing.Bitmap Icon => Resources.icon_toSPL;
        public override Guid ComponentGuid => new Guid("6B792807-2A0A-437D-AFF9-5E1ADF7FC6AE");
    }
}
