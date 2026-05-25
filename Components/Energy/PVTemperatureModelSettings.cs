using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SolarPV.Core;
using Common.Core;
using NeosEnviSim.Properties;
using System;

namespace SolarPV
{
    public class TemperatureSettingsComponent : GH_Component
    {
        public TemperatureSettingsComponent()
          : base("Temperature Model Settings", "TempSet",
              "Configures cell temperature and thermal derating parameters for PV power output correction. " +
              "Uses Faiman model (with wind speed) or NOCT model (without wind).",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Enable Temperature Model", "EnT",
                "Apply temperature correction to PV efficiency (default true)", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("NOCT", "NOCT",
                "Nominal Operating Cell Temperature [°C]. At 800 W/m², 20°C ambient, 1 m/s wind. Typical 45-48°C (default 45)", GH_ParamAccess.item, 45.0);
            pManager.AddNumberParameter("Temp Coefficient", "TC",
                "Temperature coefficient of maximum power [%/°C]. Crystalline Si: -0.35 to -0.45 (default -0.4)", GH_ParamAccess.item, -0.4);
            pManager.AddBooleanParameter("Temp Coeff Is Percent", "IsPct",
               "True if Temp Coefficient is in %/°C (default, e.g. -0.4). " +
               "False if in absolute 1/°C (e.g. -0.004). Default true.",
               GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Wind Speed Factor", "WSF",
                "Multiplier on EPW wind speed. Use lower values for sheltered installations (default 1.0)", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Mounting Type", "Mount",
                "0=Free-standing (best cooling), 1=Roof-mounted, 2=Building-integrated (worst cooling), 3=No wind correction (default 0)", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("NOCT Reference Efficiency", "EtaRef",
                "Module efficiency [-] at which NOCT was measured. Standard NOCT test uses ~10% efficiency modules. " +
                "For modern high-efficiency modules, this affects the thermal ratio correction. " +
                "Typical: 0.08-0.12. Default 0.10 (industry standard).",
                GH_ParamAccess.item, 0.10);
           
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Temperature Settings", "TempSet",
                "Encapsulated temperature model configuration", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool enable = true;
            double noct = 45.0, tc = -0.4, windFactor = 1.0, etaRef = 0.10;
            bool tempCoeffIsPercent = true;
            int mountingType = 0;

            DA.GetData(0, ref enable);
            DA.GetData(1, ref noct);
            DA.GetData(2, ref tc);
            DA.GetData(3, ref tempCoeffIsPercent);
            DA.GetData(4, ref windFactor);
            DA.GetData(5, ref mountingType);
            DA.GetData(6, ref etaRef);
            
            MountingType mtype;
            switch (mountingType)
            {
                case 1: mtype = MountingType.RoofMounted; break;
                case 2: mtype = MountingType.BuildingIntegrated; break;
                case 3: mtype = MountingType.NoWindCorrection; break;
                default: mtype = MountingType.FreeStanding; break;
            }

            var config = new TemperatureConfig
            {
                EnableTemperatureModel = enable,
                NOCT = Math.Max(30, Math.Min(80, noct)),
                TempCoefficient = Math.Max(-1.0, Math.Min(0, tc)),
                TempCoeffIsPercent = tempCoeffIsPercent,
                WindSpeedFactor = Math.Max(0.1, windFactor),
                MountingType = mtype,
                NOCTReferenceEfficiency = (etaRef > 0) ? Math.Max(0.01, Math.Min(0.5, etaRef)) : 0.10
            };

            DA.SetData(0, new GH_ObjectWrapper(config));
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_TempModelSet;
        public override Guid ComponentGuid => new Guid("9BB481E2-51EA-4D56-95E1-C8DA5CE112AA");
    }
}
