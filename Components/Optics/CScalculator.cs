using System;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;

namespace NeosOptics
{
    public class CircadianStimulusComponent : GH_Component
    {
        public CircadianStimulusComponent()
          : base("Circadian Stimulus", "CS",
              "Calculates Circadian Stimulus (CS) from Melanopic Equivalent Daylight Illuminance (EDI).\nThis component employs a simplified CS calculation method, and the light source should be warm white light",
              "Neos", "Optics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Melanopic EDI", "mEDI", "Melanopic Equivalent Daylight Illuminance (lux)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Duration", "D", "light exposure durations(0.5-3.0h)", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Lighting Mode", "M", "Spatial distribution type of luminous stimulus.\n" +
                "1 : full visual feld, as with a Ganzfeld,distribution factor = 2.0 \n" +
                "2 : central visual feld, as with a discrete light box on a desk,distribution factor = 1.0 \n" +
                "3 : superior visual feld, as from ceiling mounted down-light fxtures,distribution factor = 0.5", GH_ParamAccess.item, 2);
            pManager.AddNumberParameter("Spectral Calibration", "SC","Correction Factor between the Melanopic Spectral Efficiency Function (CIE S 026:2018) " +
                "and the Classic ipRGC Sensitivity Template (Wyszecki & Stiles, 1982).\n" +
                "Default=1.0 (ideal warm white). Must be calibrated for other lights ",GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("CS", "CS", "Circadian Stimulus (0-0.7)", GH_ParamAccess.item);
            pManager.AddTextParameter("Impact Level", "Impact", "Biological impact description", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double edi = 0.0;
            double duration = 0.5;
            int mode = 1;
            double distributionFactor = 2.0;
            double spectralCalibrationFactor = 1.0;

            if (!DA.GetData(0, ref edi)) return;         
            if (!DA.GetData(1, ref duration)) return;
            if (!DA.GetData(2, ref mode)) return;
            if (!DA.GetData(3, ref spectralCalibrationFactor)) return;

            if (edi < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "EML value cannot be negative. Using absolute value.");
                edi = Math.Abs(edi);
            }

            if (duration < 0.5 || duration > 3.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Duration must be between 0.5 and 3.0 hours. Clamping value.");
                duration = Math.Min(3.0, Math.Max(0.5, duration));
            }

            if (mode == 1)
            { distributionFactor = 2.0; }
            else if (mode == 2)
            { distributionFactor = 1.0; }
            else if (mode == 3)
            { distributionFactor = 0.5; }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "Lighting Mode should input integer parameter 1，2 or 3");
                return;
            }


            // 计算昼夜节律刺激值CS
            double cs = CalculateCircadianStimulus(edi, duration, distributionFactor,spectralCalibrationFactor);

            // 获取生物影响描述
            string impact = GetBiologicalImpact(cs);

            DA.SetData(0, Math.Round(cs, 2));
            DA.SetData(1, impact);
        }


        // CS计算公式 (简化公式,仅适用于暖白光)
        private double CalculateCircadianStimulus(double edi, double duration, double distributionFactor, double spectralCalibrationFactor)
        {
            double CompositeCoefficient = 4.35 * spectralCalibrationFactor / 683.002;
            double baseValue = CompositeCoefficient * duration * distributionFactor * edi;
            return 0.7 * (1 - 1 / (1 + Math.Pow(baseValue, 1.1026)));
        }
  

        // 根据CS值获取生物影响描述
        private string GetBiologicalImpact(double cs)
        {
            if (cs < 0.1) return "Minimal impact (nighttime levels)";
            if (cs < 0.2) return "Low impact (typical indoor evening light)";
            if (cs < 0.3) return "Moderate impact (morning/evening light)";
            if (cs < 0.4) return "Significant impact (daytime indoor light)";
            if (cs < 0.5) return "Strong impact (bright indoor/outdoor morning)";
            return "Very strong impact (midday sunlight)";
        }

        // 组件图标和GUID
        protected override System.Drawing.Bitmap Icon => Resources.icon_CS;
        public override Guid ComponentGuid => new Guid("85711193-CC3D-406E-84D6-30BF9D65F8AB");
    }
}