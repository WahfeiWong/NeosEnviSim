using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Common.Core;
using NeosEnviSim.Properties;
using System;

namespace SoilThermophysics
{
    /// <summary>
    /// Soil Moisture Settings component.
    /// Configures beta (moisture availability) calculation method and soil resistance parameters.
    /// </summary>
    public class SoilMoistureSettingsComponent : GH_Component
    {
        public SoilMoistureSettingsComponent()
          : base("Soil Moisture Settings", "SoilMoistSet",
              "Configures soil moisture availability (beta factor) and " +
              "soil surface resistance for latent heat flux calculations.",
              "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Beta Method", "betaM",
                "Beta calculation method: 0=Noilhan(ISBA), 1=Direct, 2=KondoSaigusa, 3=PowerLaw",
                GH_ParamAccess.item, 0);

            pManager.AddNumberParameter("Soil Moisture", "wg",
                "Surface soil moisture [m3/m3]. For Noilhan: wg/wsat. Range 0.0-0.6.",
                GH_ParamAccess.item, 0.25);

            pManager.AddNumberParameter("Field Capacity", "wsat",
                "Saturation/field capacity [m3/m3]. Also used as wsat in Noilhan. Range 0.05-0.6.",
                GH_ParamAccess.item, 0.35);

            pManager.AddNumberParameter("Direct Beta", "beta",
                "Direct beta value [0-1]. Only used when Beta Method = 1 (Direct).",
                GH_ParamAccess.item, 0.5);

            pManager.AddIntegerParameter("Soil Index", "idx",
                "Soil texture index for KondoSaigusa method. 1=sand, 2=loamy sand, " +
                "3=loam, 4=silt loam, 5=clay. Only used when Beta Method = 2 (KondoSaigusa).",
                GH_ParamAccess.item, 3);

            pManager.AddNumberParameter("Beta Exp", "bExp",
                "Power law exponent for custom beta method. Only used when Beta Method = 3 (PowerLaw). " +
                "beta = (wg/wsat)^bExp. Default 1.0 (linear).",
                GH_ParamAccess.item, 1.0);

            pManager.AddNumberParameter("Min Rs", "rsMin",
                "Minimum soil surface resistance (wet) [s/m]. Default 50.",
                GH_ParamAccess.item, 50.0);

            pManager.AddNumberParameter("Max Rs", "rsMax",
                "Maximum soil surface resistance (dry) [s/m]. Default 500.",
                GH_ParamAccess.item, 500.0);

            pManager.AddNumberParameter("Override Rs", "Rs",
                "Direct soil resistance input [s/m]. If > 0, overrides all calculation. " +
                "Use -1 for automatic calculation.",
                GH_ParamAccess.item, -1.0);

            pManager.AddBooleanParameter("Force Wet", "wet",
                "Force wet surface (e.g., after rain). Sets beta = ForcedWetBeta.",
                GH_ParamAccess.item, false);

            for (int i = 0; i < 10; i++)
                pManager[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Moisture Settings", "SoilMoistSet",
                "Encapsulated moisture availability configuration", GH_ParamAccess.item);
            pManager.AddTextParameter("Summary", "Sum",
                "Human-readable parameter summary", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int betaMethod = 0;
            double wg = 0.25, wsat = 0.35, directBeta = 0.5;
            int soilIndex = 3;
            double betaExp = 1.0, rsMin = 50.0, rsMax = 500.0, overrideRs = -1.0;
            bool forceWet = false;

            DA.GetData(0, ref betaMethod);
            DA.GetData(1, ref wg);
            DA.GetData(2, ref wsat);
            DA.GetData(3, ref directBeta);
            DA.GetData(4, ref soilIndex);
            DA.GetData(5, ref betaExp);
            DA.GetData(6, ref rsMin);
            DA.GetData(7, ref rsMax);
            DA.GetData(8, ref overrideRs);
            DA.GetData(9, ref forceWet);

            // Clamp and validate
            betaMethod = Math.Max(0, Math.Min(3, betaMethod));
            wg = Math.Max(0.0, Math.Min(0.8, wg));
            wsat = Math.Max(0.01, Math.Min(0.9, wsat));
            if (wg > wsat) wg = wsat;
            directBeta = Math.Max(0.0, Math.Min(1.0, directBeta));
            soilIndex = Math.Max(1, Math.Min(5, soilIndex));
            betaExp = Math.Max(0.1, Math.Min(5.0, betaExp));
            rsMin = Math.Max(10.0, Math.Min(1000.0, rsMin));
            rsMax = Math.Max(rsMin + 10.0, Math.Min(10000.0, rsMax));

            var method = (BetaMethod)betaMethod;
            string[] methodNames = { "Noilhan(ISBA)", "Direct", "KondoSaigusa", "PowerLaw" }; // FIX: reordered to match BetaMethod enum

            var config = new SoilMoistureConfig
            {
                BetaMethod = method,
                SurfaceSoilMoisture = wg,
                FieldCapacity = wsat,
                DirectBeta = directBeta,
                SoilTextureIndex = soilIndex,
                BetaExponent = betaExp,
                MinSoilResistance = rsMin,
                MaxSoilResistance = rsMax,
                SoilSurfaceResistance = overrideRs,
                ForceWetSurface = forceWet
            };

            string summary = $"=== Soil Moisture Settings ===\n" +
                $"Beta Method .......... {methodNames[betaMethod]}\n" +
                $"Soil Moisture (wg) ... {wg:F3} m3/m3\n" +
                $"Field Capacity ....... {wsat:F3} m3/m3\n" +
                $"Direct Beta .......... {directBeta:F2}\n" +
                $"Soil Texture Index ... {soilIndex}\n" +
                $"Beta Exponent ........ {betaExp:F2}\n" +
                $"Soil Rs Range ........ [{rsMin:F0}, {rsMax:F0}] s/m\n" +
                $"Override Rs .......... {(overrideRs > 0 ? overrideRs.ToString("F1") + " s/m" : "Auto")}\n" +
                $"Force Wet Surface .... {forceWet}";

            DA.SetData(0, new GH_ObjectWrapper(config));
            DA.SetData(1, summary);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_groundMoistSet;
        public override Guid ComponentGuid => new Guid("1474299C-1AD4-4CF9-B515-40ED0AAA6442");
    }
}
