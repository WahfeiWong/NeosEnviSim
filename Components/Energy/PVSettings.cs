using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Common.Core;
using NeosEnviSim.Properties;
using System;

namespace SolarPV
{
    /// <summary>
    /// PV Module Settings component.
    /// Outputs PVConfig object for use with Photovoltaic Simulator component.
    /// </summary>
    public class PVSettingsComponent : GH_Component
    {
        public PVSettingsComponent()
          : base("PV Settings", "PVSet",
              "Configures photovoltaic module parameters including efficiency, bifacial options, " +
              "albedo, and array geometry.",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Enable Bifacial", "Bif",
                "Enable bifacial rear-side energy calculation (default false)", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Grid Resolution", "Res",
                "Analysis grid cell size in meters (default 0.5)", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Efficiency", "Eff",
                "PV module efficiency at STC [-], typical 0.15-0.22 (default 0.20)", GH_ParamAccess.item, 0.20);
            pManager.AddNumberParameter("Active Area Ratio", "AAR",
                "Ratio of active cell area to gross module area [-] (default 0.9)", GH_ParamAccess.item, 0.9);
            pManager.AddNumberParameter("Albedo", "Alb",
                "Ground reflectance [-]. Grass=0.2, Concrete=0.3, Snow=0.6-0.8 (default 0.2). " +
                "Affects ground-reflected irradiance on tilted panels and bifacial rear gain.",
                GH_ParamAccess.item, 0.2);
            pManager.AddNumberParameter("Bifaciality Factor", "BiFac",
                "Rear-to-front efficiency ratio [-], typical 0.65-0.90 (default 0.7)", GH_ParamAccess.item, 0.7);
            pManager.AddNumberParameter("Row Spacing", "RS",
                "Row-to-row spacing for bifacial shading calculation [m]. " +
                "Set to -1 to auto-infer from input geometry (default -1).",
                GH_ParamAccess.item, -1.0);
            pManager.AddNumberParameter("Module Height", "MH",
                "Module centroid height above ground for bifacial calculation [m]. " +
                "Set to -1 to auto-infer from input geometry (default -1).",
                GH_ParamAccess.item, -1.0);
            pManager.AddNumberParameter("Rear Gain Factor", "RGF",
                "Empirical correction factor for rear-side irradiance [-] (default 1.0)", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("IAM Coefficient", "IAM",
                "Incidence Angle Modifier coefficient b0 [-]. " +
                "Typical values: 0.05 for standard PV glass, 0.03 for anti-reflective coating, " +
                "0.10-0.15 for non-standard materials. Affects direct irradiance at high incidence angles. " +
                "Reference: ASHRAE Handbook - Solar Energy Use (default 0.05)",
                GH_ParamAccess.item, 0.05);
            pManager.AddNumberParameter("System Loss Factor", "LF",
                "Total system performance loss rate [-], i.e. fraction of energy lost to factors " +
                "NOT already modeled by this component (see below). " +
                "Enter as a decimal: 0.14 = 14% loss, output multiplier internally = 1 - 0.14 = 0.86. " +
                "Typical values: 0.05-0.25 for real systems, 0.0 for ideal/theoretical (default 0.14). " +
                "--- ALREADY MODELED (do NOT include in this factor) --- " +
                "Shading (raytracing), Temperature derating (TempSet), IAM (above), Inverter losses (InvSet), Bifacial gain. " +
                "--- INCLUDED IN THIS FACTOR --- " +
                "Soiling/dust 1-8%, Mismatch 1-2%, DC/AC wiring 1-3%, Connections 0.5-1%, " +
                "LID light-induced degradation 1-2%, Availability/downtime 1-3%, Snow 0-5%, Aging 0.5%/yr. " +
                "Reference: NREL PVWatts defaults (14% total loss).",
                GH_ParamAccess.item, 0.14);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("PV Settings", "PVSet",
                "Encapsulated PV module configuration", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool enableBifacial = false;
            double gridResolution = 0.5;
            double efficiency = 0.20;
            double activeAreaRatio = 0.9;
            double albedo = 0.2;
            double bifacialityFactor = 0.7;
            double rowSpacing = -1.0;
            double moduleHeight = -1.0;
            double rearGainFactor = 1.0;
            double iamCoeff = 0.05;
            double systemLossFactor = 0.14;

            DA.GetData(0, ref enableBifacial);
            DA.GetData(1, ref gridResolution);
            DA.GetData(2, ref efficiency);
            DA.GetData(3, ref activeAreaRatio);
            DA.GetData(4, ref albedo);
            DA.GetData(5, ref bifacialityFactor);
            DA.GetData(6, ref rowSpacing);
            DA.GetData(7, ref moduleHeight);
            DA.GetData(8, ref rearGainFactor);
            DA.GetData(9, ref iamCoeff);
            DA.GetData(10, ref systemLossFactor);

            var config = new PVConfig
            {
                EnableBifacial = enableBifacial,
                GridResolution = Math.Max(0.01, gridResolution),
                Efficiency = Math.Max(0.01, Math.Min(1.0, efficiency)),
                ActiveAreaRatio = Math.Max(0.01, Math.Min(1.0, activeAreaRatio)),
                Albedo = Math.Max(0.0, Math.Min(1.0, albedo)),
                BifacialityFactor = Math.Max(0.0, Math.Min(1.0, bifacialityFactor)),
                RowSpacing = rowSpacing,              // Allow -1 for auto-infer
                ModuleHeight = moduleHeight,          // Allow -1 for auto-infer
                RearGainFactor = Math.Max(0.0, rearGainFactor),
                IAMCoefficient = Math.Max(0.0, Math.Min(0.5, iamCoeff)),
                SystemLossFactor = Math.Max(0.0, Math.Min(1.0, systemLossFactor))
            };

            DA.SetData(0, new GH_ObjectWrapper(config));
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_PVset;
        public override Guid ComponentGuid => new Guid("81F0DC9D-219D-4A54-864A-9C66DCD12CE1");
    }
}
