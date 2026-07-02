using System;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using ThermalComfort.Core;
namespace ThermalComfort
{
    /// <summary>
    /// PST Human Settings - Human physiological parameter configuration component.
    /// Outputs a structured PST Human Set for the PST Simulator.
    /// 
    /// Default values based on MENEX_2005 standard assumptions:
    /// - MetRate = 135 W/m2 (walking at 4 km/h, ISO 8996)
    /// - CloValue = 0.8 clo (summer typical clothing)
    /// - AlbedoClo = 30% (clothing solar reflectance)
    /// - WalkSpeed = 1.1 m/s (walking speed relative to air)
    /// 
    /// AutoMet feature: When enabled, metabolic rate is auto-calculated from walk speed
    /// using the ISO 8996 simplified relation: M = 58 + 70 * v_walk (m/s).
    /// When disabled, a consistency check warns if MetRate and WalkSpeed deviate
    /// by more than 30 W/m2 from the expected relationship.
    /// 
    /// References:
    /// - Blazejczyk, K. (2005). MENEX_2005 - the updated version of man-environment 
    ///   heat exchange model. Proceedings of the 11th International Conference on 
    ///   Environmental Ergonomics, Ystad, Sweden, 222-225.
    /// - ISO 8996 (2004). Ergonomics of the thermal environment - Determination of 
    ///   metabolic rate.
    /// - Fanger, P.O. (1970). Thermal Comfort: Analysis and Applications in 
    ///   Environmental Engineering. McGraw-Hill, New York.
    /// </summary>
    public class PstHumanSettings : GH_Component
    {
        public PstHumanSettings()
            : base("PST Human Settings", "PSTHuman",
                  "Configures human physiological parameters for PST simulation. " +
                  "Mean skin temperature (Ts) and skin wettedness (w) are internal " +
                  "iterative variables computed by the MENEX_2005 model, not exposed " +
                  "as inputs.",
                  "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // --- Metabolic rate group (AutoMet + MetRate + WalkSpeed) ---

            // Auto metabolic rate toggle
            pManager.AddBooleanParameter("AutoMet", "AutoMet",
                "If True (default), metabolic rate is auto-calculated from WalkSpeed " +
                "using the ISO 8996 simplified relation: M = 58 + 70 * v_walk (m/s). " +
                "e.g. v=0 -> M=58 W/m2 (rest), v=1.1 -> M=135 W/m2 (walking 4 km/h). " +
                "If False, uses the user-specified MetRate directly, and a consistency " +
                "check warns if MetRate and WalkSpeed deviate by >30 W/m2.",
                GH_ParamAccess.item, true);
            pManager[0].Optional = true;

            // Metabolic rate (W/m2), default 135 (walking 4km/h)
            pManager.AddNumberParameter("MetRate", "M",
                "Metabolic heat production (W/m2). Sum of basal metabolic rate and " +
                "activity-related heat production. Default 135 W/m2 corresponds to " +
                "walking at 4 km/h (ISO 8996, Jendritzky et al. 2002). " +
                "When AutoMet = True, this input is ignored. " +
                "Range: 58-400 W/m2.",
                GH_ParamAccess.item, 135.0);
            pManager[1].Optional = true;

            // Walking speed (m/s), default 1.1
            pManager.AddNumberParameter("WalkSpeed", "Vw",
                "Velocity of person's motion relative to air (m/s). " +
                "Default 1.1 m/s (approx. 4 km/h walking speed). " +
                "Combined with wind speed for convective heat transfer calculation. " +
                "Also used for auto-calculating metabolic rate when AutoMet = True. " +
                "Range: 0-3 m/s.",
                GH_ParamAccess.item, 1.1);
            pManager[2].Optional = true;

            // --- Clothing group (AutoClo + CloValue) ---

            // Auto clothing insulation toggle
            pManager.AddBooleanParameter("AutoClo", "AutoClo",
                "If True (default), clothing insulation is auto-adjusted by air temperature " +
                "using the MENEX_2005 formula: Icl = 1.691 - 0.0436*t (clamped: " +
                "t<-30C => 3.0 clo, t>25C => 0.6 clo), ignoring the CloValue input. " +
                "If False, uses the user-specified CloValue as the absolute clo value.",
                GH_ParamAccess.item, false);
            pManager[3].Optional = true;

            // Clothing insulation (clo), default 0.8 (summer typical)
            pManager.AddNumberParameter("CloValue", "Icl",
                "Clothing insulation (clo). Summer typical value 0.8 clo. " +
                "When AutoClo = True, this input is ignored and Icl is set by the " +
                "MENEX_2005 temperature-adaptive formula. " +
                "When AutoClo = False, this value is used directly as the absolute clo value.",
                GH_ParamAccess.item, 0.8);
            pManager[4].Optional = true;

            // Clothing albedo (%), default 30%
            pManager.AddNumberParameter("AlbedoClo", "Alb",
                "Clothing surface solar radiation reflectance/albedo (%). " +
                "Default 30% for typical summer clothing. Range 10-90%. " +
                "Affects absorbed solar radiation calculation.",
                GH_ParamAccess.item, 30.0);
            pManager[5].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("HumanSet", "HS",
                "Structured human parameter set for PST Simulator.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Read inputs with defaults ---
            // Input order: AutoMet(0), MetRate(1), WalkSpeed(2), AutoClo(3), CloValue(4), AlbedoClo(5)

            // 1. AutoMet (must read first to determine MetRate source)
            bool autoMet = true;
            DA.GetData(0, ref autoMet);

            // 2. WalkSpeed (must read before MetRate calculation)
            double walkSpeed = 1.1;
            DA.GetData(2, ref walkSpeed);
            if (walkSpeed < 0) walkSpeed = 0;

            // 3. MetRate (auto-calculated or user-specified)
            double metRate = 135.0;
            if (autoMet)
            {
                // ISO 8996 simplified relation: M = 58 + 70 * v_walk (m/s)
                // Reference points: v=0 m/s -> M=58 W/m2 (rest), v=1.1 m/s -> M=135 W/m2 (4 km/h)
                metRate = 58.0 + 70.0 * walkSpeed;
                // Clamp to valid range
                if (metRate < 58.0) metRate = 58.0;
                if (metRate > 400.0) metRate = 400.0;
            }
            else
            {
                DA.GetData(1, ref metRate);
                if (metRate < 58) metRate = 58;

                // --- Consistency check: MetRate vs WalkSpeed ---
                double expectedMet = 58.0 + 70.0 * walkSpeed;
                if (Math.Abs(metRate - expectedMet) > 30.0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Metabolic rate (M={metRate:F0} W/m2) and walk speed (v={walkSpeed:F1} m/s) " +
                        $"are inconsistent. Expected M ~{expectedMet:F0} W/m2 for this walk speed " +
                        $"(ISO 8996: M = 58 + 70 * v). Deviation = {Math.Abs(metRate - expectedMet):F0} W/m2. " +
                        "Consider enabling AutoMet or adjusting the values.");
                }
            }

            // 4. AutoClo
            bool autoClo = true;
            DA.GetData(3, ref autoClo);

            // 5. CloValue
            double cloValue = 0.8;
            DA.GetData(4, ref cloValue);
            if (cloValue < 0.1) cloValue = 0.1;

            // 6. AlbedoClo
            double albedoClo = 30.0;
            DA.GetData(5, ref albedoClo);
            if (albedoClo < 0) albedoClo = 0;
            if (albedoClo > 100) albedoClo = 100;

            // --- Additional physical consistency checks ---
            // WalkSpeed > 2.0 m/s (fast walk/jog) with MetRate < 160 W/m2 is suspicious
            if (walkSpeed > 2.0 && metRate < 160.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Walk speed ({walkSpeed:F1} m/s) suggests fast walking or jogging, " +
                    $"but metabolic rate ({metRate:F0} W/m2) is low. " +
                    "Expected M >= 200 W/m2 for this activity level.");
            }

            // --- Create and output human set (wrapped as GH_Goo for Grasshopper wire) ---
            var humanSet = new PstHumanSet
            {
                MetRate = metRate,
                AutoMet = autoMet,
                WalkSpeed = walkSpeed,
                AutoClo = autoClo,
                CloValue = cloValue,
                AlbedoClo = albedoClo
            };

            DA.SetData(0, new GH_PstHumanSet(humanSet));
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_PSThumanSet;
        public override Guid ComponentGuid => new Guid("6B05D9D3-E4DF-40DE-8DE6-5F284C68538B");
    }
}
