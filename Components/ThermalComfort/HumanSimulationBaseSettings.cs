using System;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using ThermalComfort.Core;

namespace ThermalComfort
{
    /// <summary>
    /// Simulation Base Settings - Exposes internal simulation parameters for advanced control.
    /// 
    /// Defines the reference environment (EqT baseline), solver convergence criteria,
    /// and physiology coefficients. All parameters have sensible defaults aligned with
    /// PET/UTCI standards. Connect output to Human Thermoregulation Simulator's
    /// SimBaseSet input (optional - simulator works without it using defaults).
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
                  "solver control, and physiology coefficients. Optional - simulator uses " +
                  "PET defaults if not connected.",
                  "Neos", "Thermophysics")
        { }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("4F897D09-6F47-45EB-9F4D-0EF85DA387B4");
        protected override System.Drawing.Bitmap Icon => Resources.icon_HumanSimBase;


        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            // =====================================================================
            // Group 1: Reference Environment (defines EqT baseline)
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
            // Group 2: Solver Control
            // =====================================================================

            pManager.AddIntegerParameter("MaxIter", "MaxIter",
                "Maximum iterations for single CoreSolve. Default 200. " +
                "Increase to 500+ for extreme environments (desert/high humidity). " +
                "Decrease to 100 for faster batch processing of mild environments.",
                GH_ParamAccess.item, 200);
            pManager[4].Optional = true;

            pManager.AddNumberParameter("ResidTol", "Tol",
                "Blood pool temperature convergence tolerance [K]. Default 0.005 K (5 mK). " +
                "Stricter (0.001) for research precision. Looser (0.01) for speed.",
                GH_ParamAccess.item, 0.005);
            pManager[5].Optional = true;

            pManager.AddNumberParameter("BlpRelax", "Alpha",
                "Blood pool relaxation factor (0.3-0.9). Default 0.7. " +
                "Lower values improve stability in extreme environments (desert/Arctic) " +
                "but increase iterations. Higher values converge faster in mild conditions.",
                GH_ParamAccess.item, 0.7);
            pManager[6].Optional = true;

            pManager.AddIntegerParameter("EqTSearchIter", "EqTN",
                "Binary search iterations for equivalent temperature. Default 20. " +
                "Each iteration halves the search interval. 20 iterations ~ 0.05 C precision. " +
                "10 = 0.1 C, 30 = 0.0001 C (overkill).",
                GH_ParamAccess.item, 20);
            pManager[7].Optional = true;

            // =====================================================================
            // Group 3: Physiology Coefficients
            // =====================================================================

            pManager.AddNumberParameter("InsensibleDiff", "wDiff",
                "Baseline skin wetness from insensible perspiration (transepidermal water loss). " +
                "Fraction of maximum evaporation capacity even without active sweating. " +
                "Gagge et al. (1971): 0.06. Range: 0.02-0.10.",
                GH_ParamAccess.item, 0.06);
            pManager[8].Optional = true;

            pManager.AddNumberParameter("AgeAttenuation", "AgeAtt",
                "Thermoregulatory response attenuation factor for seniors (>65 years). " +
                "Multiplies vasoconstriction, vasodilation, and sweating responses. " +
                "Fiala (2012): 0.75 (25% reduction). Range: 0.5-1.0 (1.0 = no attenuation).",
                GH_ParamAccess.item, 0.75);
            pManager[9].Optional = true;

            pManager.AddNumberParameter("SexMetFactor", "SexMet",
                "Female basal metabolic rate as fraction of male. ISO 8996 Annex B: 0.90. " +
                "Applied to all tissue layers' basal metabolism. Range: 0.85-0.95.",
                GH_ParamAccess.item, 0.90);
            pManager[10].Optional = true;
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("SimBaseSet", "SBS",
                "Simulation base settings. Connect to Human Thermoregulation Simulator " +
                "SimBaseSet input (optional - simulator uses PET defaults if not connected).",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Read inputs (all have defaults) ---
            double refMetRate = 135.0;     DA.GetData(0, ref refMetRate);
            double refWindSpeed = 0.5;     DA.GetData(1, ref refWindSpeed);
            double refRH = 50.0;           DA.GetData(2, ref refRH);
            double refIcl = 0.5;           DA.GetData(3, ref refIcl);
            int maxIter = 200;             DA.GetData(4, ref maxIter);
            double residTol = 0.005;       DA.GetData(5, ref residTol);
            double blpRelax = 0.7;         DA.GetData(6, ref blpRelax);
            int eqTSearchIter = 20;        DA.GetData(7, ref eqTSearchIter);
            double insensibleDiff = 0.06;  DA.GetData(8, ref insensibleDiff);
            double ageAttenuation = 0.75;  DA.GetData(9, ref ageAttenuation);
            double sexMetFactor = 0.90;    DA.GetData(10, ref sexMetFactor);

            // --- Validation with warnings ---
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

            if (maxIter < 10 || maxIter > 2000)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "MaxIter clamped to 10-2000 range.");
                maxIter = Math.Max(10, Math.Min(2000, maxIter));
            }

            if (residTol < 0.0001 || residTol > 1.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "ResidTol clamped to 0.0001-1.0 K range.");
                residTol = Math.Max(0.0001, Math.Min(1.0, residTol));
            }

            if (blpRelax < 0.1 || blpRelax > 1.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "BlpRelax clamped to 0.1-1.0 range.");
                blpRelax = Math.Max(0.1, Math.Min(1.0, blpRelax));
            }

            if (eqTSearchIter < 5 || eqTSearchIter > 50)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "EqTSearchIter clamped to 5-50 range.");
                eqTSearchIter = Math.Max(5, Math.Min(50, eqTSearchIter));
            }

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

            // --- Build output ---
            var settings = new SimulationSettings
            {
                RefMetRate = refMetRate,
                RefWindSpeed = refWindSpeed,
                RefRH = refRH,
                RefIcl = refIcl,
                MaxIter = maxIter,
                ResidTol = residTol,
                BlpRelax = blpRelax,
                EqTSearchIter = eqTSearchIter,
                InsensibleDiff = insensibleDiff,
                AgeAttenuation = ageAttenuation,
                SexMetFactor = sexMetFactor
            };

            DA.SetData(0, new GH_SimulationSettings(settings));
        }
    }
}
