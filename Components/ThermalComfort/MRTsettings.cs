using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Common.Core;
using NeosEnviSim.Properties;
using System;

namespace ThermalComfort
{
    /// <summary>
    /// MRT Calculation Settings component (thermal comfort parameters only).
    /// Temperature inputs (ground, obstacles) are provided directly to the MRT component.
    /// </summary>
    public class MRTSettingsComponent : GH_Component
    {
        public MRTSettingsComponent()
          : base("MRT Settings", "MRTSet",
              "Configures human thermal parameters for Mean Radiant Temperature calculation. " +
              "Temperature inputs (ground, obstacles) are provided directly to the MRT component.",
              "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Use RayMan", "RayMan",
                "Use RayMan model instead of SolarCal (ASHRAE 55). Default false.",
                GH_ParamAccess.item, false);

            pManager.AddIntegerParameter("Posture", "Post",
                "Human posture (SolarCal): 0=Standing (PostureFactor f=0.725), 1=Sitting (PostureFactor f=0.696)", GH_ParamAccess.item, 0);

            pManager.AddNumberParameter("Body Absorptivity", "Alpha",
                "Shortwave absorptivity [-], typical 0.7", GH_ParamAccess.item, 0.7);

            pManager.AddNumberParameter("Body Emissivity", "Epsilon",
                "Longwave emissivity [-], typical 0.95", GH_ParamAccess.item, 0.95);

            pManager.AddNumberParameter("Radiative HTC", "Hr",
                "Radiative heat transfer coefficient [W/(m²·K)] for SolarCal model, ASHRAE 55 default 6.012", GH_ParamAccess.item, 6.012);

            pManager.AddNumberParameter("Floor Reflectance", "Rho",
                "Ground/floor reflectance [-], default 0.25", GH_ParamAccess.item, 0.25);

            pManager.AddIntegerParameter("Exposure Samples", "NExp",
                "Vertical sample points for exposure ray tracing (default 3)", GH_ParamAccess.item, 3);

            pManager.AddNumberParameter("Eye Height for SVF", "HSVF",
                "Height of eye point for view factor calculation [m], default 1.5", GH_ParamAccess.item, 1.5);

            pManager.AddNumberParameter("Body Height", "HBod",
                "Total body height [m], default 1.7", GH_ParamAccess.item, 1.7);

            pManager.AddNumberParameter("Sky Emissivity", "SkyEps",
                "Sky emissivity for longwave. -1 = auto from dew point (default -1)", GH_ParamAccess.item, -1.0);

            pManager.AddIntegerParameter("SVF Sample Count", "SVF_N",
                "Full sphere sample count for view factors (default 1000)", GH_ParamAccess.item, 1000);

            pManager.AddNumberParameter("Longwave Coeff", "LwCoeff",
                "SolarCal longwave linearization coefficient, default 0.5", GH_ParamAccess.item, 0.5);

            pManager.AddNumberParameter("Ground Emissivity", "EpsGrd",
                "Ground surface longwave emissivity [-]. ONLY used by RayMan model (ignored in SolarCal). " +
                "Typical values: grass/vegetation 0.95-0.98, concrete 0.88-0.94, asphalt 0.90-0.96, water 0.95-0.96. " +
                "Default 0.95.",
                GH_ParamAccess.item, 0.95);

            pManager.AddNumberParameter("Obstacle Emissivity", "EpsObs",
                "Obstacle (surrounding surfaces) longwave emissivity [-]. ONLY used by RayMan model (ignored in SolarCal). " +
                "Typical values: concrete/brick 0.88-0.95, glass 0.84-0.90, vegetation 0.95-0.98, metal 0.20-0.70. " +
                "Default 0.95.",
                GH_ParamAccess.item, 0.95);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("MRT Settings", "MRTSet",
                "Encapsulated MRT configuration (human parameters only)", GH_ParamAccess.item);
           
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool useRayMan = false;
            int posture = 0;
            double absorptivity = 0.7, emissivity = 0.95, htc = 6.012, floorReflectance = 0.25;
            int exposureSamples = 3;
            double analysisHeight = 1.5, bodyHeight = 1.7, skyEmissivity = -1.0;
            int svfSampleCount = 1000;
            double longwaveCoeff = 0.5;
            double groundEmissivity = 0.95, obstacleEmissivity = 0.95;

            DA.GetData(0, ref useRayMan);
            DA.GetData(1, ref posture);
            DA.GetData(2, ref absorptivity);
            DA.GetData(3, ref emissivity);
            DA.GetData(4, ref htc);
            DA.GetData(5, ref floorReflectance);
            DA.GetData(6, ref exposureSamples);
            DA.GetData(7, ref analysisHeight);
            DA.GetData(8, ref bodyHeight);
            DA.GetData(9, ref skyEmissivity);
            DA.GetData(10, ref svfSampleCount);
            DA.GetData(11, ref longwaveCoeff);
            DA.GetData(12, ref groundEmissivity);
            DA.GetData(13, ref obstacleEmissivity);

            // Clamp values
            posture = Math.Max(0, Math.Min(1, posture));
            absorptivity = Math.Max(0.1, Math.Min(1.0, absorptivity));
            emissivity = Math.Max(0.8, Math.Min(1.0, emissivity));
            htc = Math.Max(1.0, Math.Min(20.0, htc));
            floorReflectance = Math.Max(0.0, Math.Min(1.0, floorReflectance));
            exposureSamples = Math.Max(1, Math.Min(20, exposureSamples));
            analysisHeight = Math.Max(0.01, Math.Min(3.0, analysisHeight));
            bodyHeight = Math.Max(0.5, Math.Min(2.5, bodyHeight));
            svfSampleCount = Math.Max(100, Math.Min(10000, svfSampleCount));
            longwaveCoeff = Math.Max(0.1, Math.Min(2.0, longwaveCoeff));
            groundEmissivity = Math.Max(0.5, Math.Min(1.0, groundEmissivity));
            obstacleEmissivity = Math.Max(0.2, Math.Min(1.0, obstacleEmissivity));

            double postureFactor = (posture == 0) ? 0.725 : 0.696;

            var config = new MRTConfig
            {
                UseRayManModel = useRayMan,
                PostureFactor = postureFactor,
                BodyAbsorptivity = absorptivity,
                BodyEmissivity = emissivity,
                RadiativeHeatTransferCoeff = htc,
                FloorReflectance = floorReflectance,
                ExposureSamplePoints = exposureSamples,
                AnalysisHeight = analysisHeight,
                BodyHeight = bodyHeight,
                IncludeShortwave = true,
                IncludeLongwave = true,
                SkyEmissivity = skyEmissivity,
                SVFSampleCount = svfSampleCount,
                LongwaveLinearCoeff = longwaveCoeff,
                GroundEmissivity = groundEmissivity,
                ObstacleEmissivity = obstacleEmissivity
            };

            DA.SetData(0, new GH_ObjectWrapper(config));
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_MRTset;
        public override Guid ComponentGuid => new Guid("455D5C05-9F1A-4CFD-9DB4-82CFAE2EE3AA");
    }
}