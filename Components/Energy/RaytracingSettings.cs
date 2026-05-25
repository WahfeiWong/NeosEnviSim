using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Common.Core;
using NeosEnviSim.Properties;
using System;

namespace SolarPV
{
    public class RaytracingSettingsComponent : GH_Component
    {
        public RaytracingSettingsComponent()
          : base("Raytracing Settings", "RaySet",
              "Configures raytracing parameters for sky view factor SVF hemispherical sampling and direct sun shadow detection.",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // FIX 2024-05-03: Default increased from 145 to 500 for engineering-grade accuracy.
            // Previous default of 145 produced only ~72 effective hemisphere directions due to
            // Z<0 filtering, which is insufficient for accurate SVF calculation.
            pManager.AddIntegerParameter("SVF Sample Count", "SVF_N",
                "Number of sky hemisphere sampling directions for Sky View Factor calculation. " +
                "Higher values improve SVF accuracy but increase computation time. " +
                "Recommended: 500+ for engineering accuracy, 1000+ for high precision (default 500)", 
                GH_ParamAccess.item, 500);
            pManager.AddNumberParameter("SVF Ray Offset", "SVF_Off",
                "Ray origin offset along surface normal for SVF rays in meters. Prevents self-intersection of the mesh face being analyzed (default 0.01)", GH_ParamAccess.item, 0.01);
            pManager.AddNumberParameter("Shadow Ray Offset", "Shad_Off",
                "Ray origin offset along SUN DIRECTION for direct sun shadow rays in meters. " +
                "Small offset prevents false self-shading at the ray origin. " +
                "FIXED 2024-05-03: offset direction corrected from surface normal to sun vector (default 0.001)",
                GH_ParamAccess.item, 0.001);
            pManager.AddNumberParameter("Max Trace Distance", "TraDist",
                "Maximum trace distance for shadow rays in meters. Obstacles beyond this distance are ignored, improving performance for large scenes (default 10000)", GH_ParamAccess.item, 10000.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Raytracing Settings", "RaySet",
                "Encapsulated raytracing configuration for PV simulation", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int svfN = 500;
            double svfOff = 0.01, shadOff = 0.001, maxDist = 10000.0;

            DA.GetData(0, ref svfN);
            DA.GetData(1, ref svfOff);
            DA.GetData(2, ref shadOff);
            DA.GetData(3, ref maxDist);

            var config = new RaytracingConfig
            {
                SVFSampleCount = Math.Max(10, Math.Min(10000, svfN)),
                SVFRayOffset = Math.Max(0.0001, svfOff),
                ShadowRayOffset = Math.Max(0.0001, shadOff),
                MaxShadowDistance = Math.Max(1.0, maxDist)
            };

            DA.SetData(0, new GH_ObjectWrapper(config));
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_RaytracingSet;
        public override Guid ComponentGuid => new Guid("62D03269-272A-465A-AA1F-169C6474BDB2");
    }
}
