using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Common.Core;
using NeosEnviSim.Properties;
using System;

namespace SolarPV
{
    public class SkyModelSettingsComponent : GH_Component
    {
        public SkyModelSettingsComponent()
          : base("Sky Model Settings", "SkySet",
              "Selects between isotropic and Perez anisotropic sky models for diffuse irradiance distribution on tilted surfaces.",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Use Perez Model", "Perez",
                "Use Perez anisotropic sky model for diffuse irradiance calculation. False uses isotropic sky model with uniform sky dome brightness (default false)", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Horizon Coefficient", "HC",
                "Scaling factor for horizon brightening component in Perez model. " +
                "Increase for environments with bright horizon reflections such as snow or water. " +
                "NOW ACTIVE: directly scales F2 coefficient in Perez equation (default 1.0)",
                GH_ParamAccess.item, 1.0);

            pManager.AddNumberParameter("Circumsolar Coefficient", "CC",
                "Scaling factor for circumsolar brightening component in Perez model. " +
                "Increase for clear sky conditions with strong forward scattering. " +
                "NOW ACTIVE: directly scales F1 coefficient in Perez equation (default 1.0)",
                GH_ParamAccess.item, 1.0);

            pManager.AddNumberParameter("Diffuse Ratio Threshold", "DRT",
                "Minimum DHI/GHI ratio required to trigger anisotropic calculation. " +
                "NOW ACTIVE: when DHI/GHI falls below this value, model falls back to isotropic sky " +
                "to prevent numerical instability (default 0.01)",
                GH_ParamAccess.item, 0.01);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Sky Model Settings", "SkySet",
                "Encapsulated sky model configuration for PV simulation", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool usePerez = false;
            double horizonCoeff = 1.0, circumsolarCoeff = 1.0, threshold = 0.01;

            DA.GetData(0, ref usePerez);
            DA.GetData(1, ref horizonCoeff);
            DA.GetData(2, ref circumsolarCoeff);
            DA.GetData(3, ref threshold);

            var config = new SkyModelConfig
            {
                UsePerezModel = usePerez,
                HorizonBrighteningCoeff = Math.Max(0, horizonCoeff),
                CircumsolarBrighteningCoeff = Math.Max(0, circumsolarCoeff),
                DirectDiffuseRatioThreshold = Math.Max(0.001, Math.Min(1.0, threshold))
            };

            DA.SetData(0, new GH_ObjectWrapper(config));
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_SkySet;
        public override Guid ComponentGuid => new Guid("B0970AF8-79AA-49A9-9687-804B7618A263");
    }
}
