using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using NeosEnviSim.Properties;
using ThermalComfort.Core;

namespace ThermalComfort
{
    /// <summary>
    /// UTCI Human Settings - Human physiological and activity parameter configuration.
    /// 
    /// Supports AutoMet: metabolic rate auto-calculated from walk speed per ISO 8996.
    /// Includes consistency check between manually input MetRate and WalkSpeed.
    /// 
    /// Reference:
    /// - Fiala, D. et al. (2012). Int J Biometeorol, 56, 419-431.
    /// - ISO 8996 (2004). Ergonomics - Determination of metabolic rate.
    /// - Havenith, G. et al. (2012). Int J Biometeorol, 56, 461-470 (UTCI clothing).
    /// </summary>
    public class UtciHumanSettings : GH_Component
    {
        public UtciHumanSettings()
            : base("UTCI Human Settings", "UTCI_HSet",
                  "Configure human/activity parameters for UTCI simulation. " +
                  "Supports automatic metabolic rate calculation from walk speed.",
                  "Neos", "Thermophysics")
        { }


        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("1C8392B4-71C4-4E44-9B02-6FD5E1038857");
        protected override System.Drawing.Bitmap Icon => Resources.icon_UTCIhumanSet;


        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // --- Metabolic rate group (AutoMet + MetRate + WalkSpeed) ---

            pManager.AddBooleanParameter("AutoMet", "AutoMet",
                "If true (default), metabolic rate is auto-calculated from WalkSpeed " +
                "using ISO 8996: M = 58 + 70 * v_walk [m/s]. " +
                "e.g. v=0 -> M=58 (rest), v=1.1 -> M=135 (walk 4km/h). " +
                "If false, uses MetRate input directly with consistency check.",
                GH_ParamAccess.item, true);
            pManager[0].Optional = true;

            pManager.AddNumberParameter("MetRate", "M",
                "Metabolic heat production [W/m2]. AutoMet=true: ignored. " +
                "AutoMet=false: used as absolute value. Default 80 W/m2 for UTCI reference " +
                "(standing, light activity). Range: 58-400 W/m2.",
                GH_ParamAccess.item, 80.0);
            pManager[1].Optional = true;

            pManager.AddNumberParameter("WalkSpeed", "Vw",
                "Walking / running speed [m/s]. Used for: (a) auto-calculating metabolic rate " +
                "when AutoMet=true (ISO 8996: M = 58 + 70 * v), (b) effective air speed " +
                "for convective heat transfer (v_eff = sqrt(v_wind^2 + v_walk^2)). " +
                "Default 1.1 (walking 4 km/h). Range: 0-8 m/s (human sprint limit ~12 m/s).",
                GH_ParamAccess.item, 1.1);
            pManager[2].Optional = true;

            // --- Posture ---
            pManager.AddIntegerParameter("Posture", "Pos",
                "Body posture: 0 = standing (default), 1 = sitting. Affects effective " +
                "radiant body area ratio (f_eff = 0.80 standing, 0.74 sitting).",
                GH_ParamAccess.item, 0);
            pManager[3].Optional = true;

            // --- Clothing group ---
            pManager.AddBooleanParameter("AutoClo", "AutoClo",
                "If true (default), clothing insulation is auto-adjusted by air temperature " +
                "using the UTCI clothing model (Havenith et al. 2012) in Weather Settings. " +
                "If false, uses CloValue input.",
                GH_ParamAccess.item, true);
            pManager[4].Optional = true;

            pManager.AddNumberParameter("CloValue", "Icl",
                "Clothing insulation [clo]. Used when AutoClo=false and AutoClo in " +
                "Weather Settings is also false. Summer: 0.5, Winter: 1.0.",
                GH_ParamAccess.item, 0.5);
            pManager[5].Optional = true;

            // --- Anthropometry ---
            pManager.AddNumberParameter("BodyWeight", "W",
                "Body weight [kg]. Default 73.5 kg (Fiala reference man).",
                GH_ParamAccess.item, 73.5);
            pManager[6].Optional = true;

            pManager.AddNumberParameter("BodyHeight", "H",
                "Body height [m]. Default 1.75 m. Used for DuBois area calculation.",
                GH_ParamAccess.item, 1.75);
            pManager[7].Optional = true;

            pManager.AddNumberParameter("Age", "Age",
                "Age [years]. Default 35. Affects UTCI reference response.",
                GH_ParamAccess.item, 35.0);
            pManager[8].Optional = true;

            pManager.AddIntegerParameter("Sex", "Sex",
                "Biological sex: 0 = male (default), 1 = female. Affects skin temp " +
                "distribution and metabolic assumptions.",
                GH_ParamAccess.item, 0);
            pManager[9].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("HumanSet", "HS",
                "Structured human/activity data for UTCI Simulator",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Read inputs ---
            bool autoMet = true; DA.GetData(0, ref autoMet);
            double metRate = 80.0; DA.GetData(1, ref metRate);
            double walkSpeed = 0.0; DA.GetData(2, ref walkSpeed);
            int posture = 0; DA.GetData(3, ref posture);
            bool autoClo = true; DA.GetData(4, ref autoClo);
            double cloValue = 0.5; DA.GetData(5, ref cloValue);
            double bodyWeight = 73.5; DA.GetData(6, ref bodyWeight);
            double bodyHeight = 1.75; DA.GetData(7, ref bodyHeight);
            double age = 35.0; DA.GetData(8, ref age);
            int sex = 0; DA.GetData(9, ref sex);

            // --- Clamp ---
            if (walkSpeed < 0) walkSpeed = 0;
            const double MAX_WALK_SPEED = 8.0; // human sprint practical limit
            if (walkSpeed > MAX_WALK_SPEED)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Walk speed {walkSpeed:F1} m/s exceeds human practical limit ({MAX_WALK_SPEED} m/s). " +
                    "Clamped to maximum. World-class sprinters reach ~12 m/s.");
                walkSpeed = MAX_WALK_SPEED;
            }
            if (bodyWeight < 30) bodyWeight = 30;
            if (bodyHeight < 1.0) bodyHeight = 1.0;
            if (age < 0) age = 0;
            if (posture < 0 || posture > 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Posture must be 0 (standing) or 1 (sitting). Using 0 (standing).");
                posture = 0;
            }
            if (sex < 0 || sex > 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Sex must be 0 (male) or 1 (female). Using 0 (male).");
                sex = 0;
            }

            // --- AutoMet calculation ---
            if (autoMet)
            {
                // ISO 8996 simplified relation: M = 58 + 70 * v_walk
                // Ref: ISO 8996 (2004), Table A.1
                // Valid range: v = 0-5 m/s (M = 58-408 W/m2). Beyond v=5 m/s,
                // linear relation underestimates; cap at ISO 8996 Annex H limit.
                metRate = 58.0 + 70.0 * walkSpeed;
                const double ISO_8996_MAX = 465.0; // W/m2, Annex H maximum
                if (metRate < 58.0) metRate = 58.0;
                if (metRate > ISO_8996_MAX)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"AutoMet: calculated M = {metRate:F0} W/m2 exceeds ISO 8996 limit " +
                        $"({ISO_8996_MAX} W/m2). Cap applied. For v > 5.8 m/s, consider " +
                        "manual MetRate input.");
                    metRate = ISO_8996_MAX;
                }
            }
            else
            {
                // Consistency check
                if (metRate < 58.0) metRate = 58.0;
                double expectedMet = 58.0 + 70.0 * walkSpeed;
                if (Math.Abs(metRate - expectedMet) > 30.0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"MetRate ({metRate:F0} W/m2) and WalkSpeed ({walkSpeed:F1} m/s) " +
                        $"are inconsistent. Expected M ~{expectedMet:F0} W/m2. " +
                        "Consider enabling AutoMet.");
                }
                if (walkSpeed > 2.0 && metRate < 160.0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Walk speed {walkSpeed:F1} m/s suggests fast locomotion, but " +
                        $"MetRate {metRate:F0} W/m2 is low. Expected >= {58 + 70 * walkSpeed:F0} W/m2.");
                }
            }

            // --- Output ---
            var humanSet = new UtciHumanSet
            {
                MetRate = metRate,
                AutoMet = autoMet,
                WalkSpeed = walkSpeed,
                Posture = posture,
                AutoClo = autoClo,
                CloValue = cloValue,
                BodyWeight = bodyWeight,
                BodyHeight = bodyHeight,
                Age = age,
                Sex = sex
            };

            DA.SetData(0, new GH_UtciHumanSet(humanSet));
        }
    }
}
