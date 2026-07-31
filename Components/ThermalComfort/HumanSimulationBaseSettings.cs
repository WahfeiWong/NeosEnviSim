using System;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using ThermalComfort.Core;

namespace ThermalComfort
{
    /// <summary>
    /// Simulation Base Settings - Exposes internal simulation parameters for advanced control.
    /// 
    /// Defines the reference environment (EqT baseline), EqT binary search precision,
    /// physiology coefficients, and transient simulation control. All parameters have
    /// sensible defaults aligned with PET/UTCI standards. Connect output to Human
    /// Thermoregulation Simulator's SimBaseSet input (optional - simulator works
    /// without it using defaults).
    ///
    /// Input parameter groups (from top to bottom):
    ///   0-3:  Reference environment (EqT baseline definition)
    ///   4:    EqT search (binary search precision)
    ///   5-7:  Physiology coefficients
    ///   8-10: Transient control (duration, time step, relaxation factor)
    ///
    /// Reference standards:
    /// - PET: Hoppe (1999). Int J Biometeorol, 43, 71-75.
    /// - UTCI: Broede et al. (2012). Int J Biometeorol, 56, 475-482.
    /// - PMV: Fanger (1970). Thermal Comfort Analysis and Applications in Environmental Engineering.
    /// </summary>
    public class SimulationBaseSettings : GH_Component
    {
        public SimulationBaseSettings()
            : base("Human Thermoregulation Base Settings", "SimBase",
                  "Configure internal simulation parameters: reference environment, " +
                  "EqT search precision, physiology coefficients, and transient " +
                  "simulation control (duration, time step, relaxation). Optional - " +
                  "simulator uses PET defaults if not connected.",
                  "Neos", "Thermophysics")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("4F897D09-6F47-45EB-9F4D-0EF85DA387B4");
        protected override System.Drawing.Bitmap Icon => Resources.icon_HumanSimBase;


        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // =====================================================================
            // Group 1: Reference Environment (defines EqT baseline) — indices 0-3
            // =====================================================================

            pManager.AddNumberParameter("RefMetRate", "Mref",
                "Reference metabolic rate [W/m2]. The activity level at which EqT is defined. " +
                "UTCI: 135 (walking 4 km/h). PET: 80 (standing). PMV: 70 (seated office). " +
                "Lower values make EqT more sensitive to cold; higher values to heat.",
                GH_ParamAccess.item, 135.0);
            pManager[0].Optional = true;

            pManager.AddNumberParameter("RefWindSpeed", "Vref",
                "Reference wind speed [m/s]. Still air = more stringent comfort. " +
                "UTCI: 0.5 m/s (10m height). PET: 0.1 m/s (still air). PMV: ~0.1 m/s. " +
                "Range: 0.05-0.5 m/s.",
                GH_ParamAccess.item, 0.5);
            pManager[1].Optional = true;

            pManager.AddNumberParameter("RefRH", "RHref",
                "Reference relative humidity [%]. All major standards (PET/UTCI/PMV) use 50%. " +
                "Range: 10-90%.",
                GH_ParamAccess.item, 50.0);
            pManager[2].Optional = true;

            pManager.AddNumberParameter("RefIcl", "Iclref",
                "Reference clothing insulation [clo]. PET: 0.5 clo (light summer). " +
                "UTCI: adaptive by temp. Range: 0.3-1.0 clo.",
                GH_ParamAccess.item, 0.5);
            pManager[3].Optional = true;

            // =====================================================================
            // Group 2: EqT Search — index 4
            // =====================================================================

            pManager.AddIntegerParameter("EqTSearchIter", "EqTN",
                "Binary search iterations for equivalent temperature. Default 20. " +
                "Each iteration halves the search interval. 20 iterations ~ 0.05 C precision. " +
                "10 = 0.1 C, 30 = 0.0001 C (overkill).",
                GH_ParamAccess.item, 20);
            pManager[4].Optional = true;

            // =====================================================================
            // Group 3: Physiology Coefficients — indices 5-7
            // =====================================================================

            pManager.AddNumberParameter("InsensibleDiff", "wDiff",
                "Baseline skin wetness from insensible perspiration (transepidermal water loss). " +
                "Fraction of maximum evaporation capacity even without active sweating. " +
                "Gagge et al. (1971): 0.06. Range: 0.02-0.10.",
                GH_ParamAccess.item, 0.06);
            pManager[5].Optional = true;

            pManager.AddNumberParameter("AgeAttenuation", "AgeAtt",
                "Thermoregulatory response attenuation factor for seniors (>65 years). " +
                "Multiplies vasoconstriction, vasodilation, and sweating responses. " +
                "Fiala (2012): 0.75 (25% reduction). Range: 0.5-1.0 (1.0 = no attenuation).",
                GH_ParamAccess.item, 0.75);
            pManager[6].Optional = true;

            pManager.AddNumberParameter("SexMetFactor", "SexMet",
                "Female basal metabolic rate as fraction of male. ISO 8996 Annex B: 0.90. " +
                "Applied to all tissue layers' basal metabolism. Range: 0.85-0.95.",
                GH_ParamAccess.item, 0.90);
            pManager[7].Optional = true;

            // =====================================================================
            // Group 4: Transient Control — indices 8-10
            // =====================================================================

            pManager.AddNumberParameter("TransientDuration", "Tdur",
                "Duration of the transient simulation [seconds]. Default 1800 s (30 min). " +
                "The physiological state at the end of this duration is used as the " +
                "anchor for equivalent temperature. Longer durations allow more thermal " +
                "strain to accumulate, improving sensitivity in moderate environments. " +
                "Shorter durations reduce computational cost. Range: 60-14400 s.",
                GH_ParamAccess.item, 1800.0);
            pManager[8].Optional = true;

            pManager.AddNumberParameter("TransientTimeStep", "Tdt",
                "Time step for the transient simulation [seconds]. Default 60 s. " +
                "The implicit Euler method ensures unconditional stability at any step size. " +
                "Larger steps (120-300 s) reduce computational cost at the expense of " +
                "temporal accuracy in the active system response. Smaller steps (10-30 s) " +
                "resolve rapid physiological changes but increase cost proportionally. " +
                "Range: 5-600 s.",
                GH_ParamAccess.item, 60.0);
            pManager[9].Optional = true;

            pManager.AddNumberParameter("BlpRelax", "Alpha",
                "Blood pool relaxation factor for the transient solver. Default 0.85. " +
                "Controls the damping of blood pool temperature updates between time steps. " +
                "Lower values (0.5-0.7) improve stability at the cost of slower response. " +
                "Higher values (0.9-1.0) give faster response but may cause oscillations. " +
                "Range: 0.3-1.0.",
                GH_ParamAccess.item, 0.85);
            pManager[10].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SimBaseSet", "SBS",
                "Simulation base settings. Connect to Human Thermoregulation Simulator " +
                "SimBaseSet input (optional - simulator uses defaults if not connected). " +
                "Contains reference environment, EqT search precision, physiology " +
                "coefficients, and transient simulation parameters.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Read inputs (all have defaults) ---
            // Group 1: Reference environment (0-3)
            double refMetRate = 135.0;     DA.GetData(0, ref refMetRate);
            double refWindSpeed = 0.5;     DA.GetData(1, ref refWindSpeed);
            double refRH = 50.0;           DA.GetData(2, ref refRH);
            double refIcl = 0.5;           DA.GetData(3, ref refIcl);

            // Group 2: EqT search (4)
            int eqTSearchIter = 20;        DA.GetData(4, ref eqTSearchIter);

            // Group 3: Physiology coefficients (5-7)
            double insensibleDiff = 0.06;  DA.GetData(5, ref insensibleDiff);
            double ageAttenuation = 0.75;  DA.GetData(6, ref ageAttenuation);
            double sexMetFactor = 0.90;    DA.GetData(7, ref sexMetFactor);

            // Group 4: Transient control (8-10)
            double transientDuration = 1800.0;  DA.GetData(8, ref transientDuration);
            double transientTimeStep = 60.0;    DA.GetData(9, ref transientTimeStep);
            double blpRelax = 0.85;             DA.GetData(10, ref blpRelax);

            // --- Validation with warnings ---

            // Group 1: Reference environment
            if (refMetRate < 40 || refMetRate > 500)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"RefMetRate {refMetRate:F0} outside typical range (40-500 W/m2). " +
                    "Standard values: UTCI=135, PET=80, PMV=70.");

            if (refWindSpeed < 0.01 || refWindSpeed > 5.0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"RefWindSpeed {refWindSpeed:F1} outside typical range (0.01-5 m/s). " +
                    "Standard values: PET=0.1, UTCI=0.5.");

            if (refRH < 1 || refRH > 100)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "RefRH must be 1-100%. Clamped.");
                refRH = Math.Max(1, Math.Min(100, refRH));
            }

            if (refIcl < 0.1 || refIcl > 2.0)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"RefIcl {refIcl:F1} outside typical range (0.1-2.0 clo). " +
                    "Standard: PET=0.5 clo.");

            // Group 2: EqT search
            if (eqTSearchIter < 5 || eqTSearchIter > 50)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "EqTSearchIter clamped to 5-50 range.");
                eqTSearchIter = Math.Max(5, Math.Min(50, eqTSearchIter));
            }

            // Group 3: Physiology coefficients
            if (insensibleDiff < 0 || insensibleDiff > 0.5)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "InsensibleDiff clamped to 0-0.5 range.");
                insensibleDiff = Math.Max(0, Math.Min(0.5, insensibleDiff));
            }

            if (ageAttenuation < 0 || ageAttenuation > 1.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "AgeAttenuation clamped to 0-1.0 range.");
                ageAttenuation = Math.Max(0, Math.Min(1.0, ageAttenuation));
            }

            if (sexMetFactor < 0.5 || sexMetFactor > 1.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "SexMetFactor clamped to 0.5-1.0 range.");
                sexMetFactor = Math.Max(0.5, Math.Min(1.0, sexMetFactor));
            }

            // Group 4: Transient control
            if (transientDuration < 60 || transientDuration > 14400)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "TransientDuration clamped to 60-14400 s range.");
                transientDuration = Math.Max(60, Math.Min(14400, transientDuration));
            }

            if (transientTimeStep < 5 || transientTimeStep > 600)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "TransientTimeStep clamped to 5-600 s range.");
                transientTimeStep = Math.Max(5, Math.Min(600, transientTimeStep));
            }

            if (blpRelax < 0.1 || blpRelax > 1.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "BlpRelax clamped to 0.1-1.0 range.");
                blpRelax = Math.Max(0.1, Math.Min(1.0, blpRelax));
            }

            // --- Build output ---
            var settings = new SimulationSettings
            {
                // Group 1: Reference environment
                RefMetRate = refMetRate,
                RefWindSpeed = refWindSpeed,
                RefRH = refRH,
                RefIcl = refIcl,

                // Group 2: EqT search
                EqTSearchIter = eqTSearchIter,

                // Group 3: Physiology coefficients
                InsensibleDiff = insensibleDiff,
                AgeAttenuation = ageAttenuation,
                SexMetFactor = sexMetFactor,

                // Group 4: Transient control
                TransientDurationMinutes = transientDuration / 60.0, // Convert s → min
                TransientTimeStep = transientTimeStep,
                BlpRelax = blpRelax
            };

            DA.SetData(0, new GH_SimulationSettings(settings));
        }
    }
}