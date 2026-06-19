// ============================================================================
// Grasshopper Native Component - MRT Calculator (ISO 7726:1998)
// ============================================================================
// Class:     MrtComponent
// Inherits:  Grasshopper.Kernel.GH_Component
// Function:  Calculate Mean Radiant Temperature (MRT) from black globe thermometer
//            measurements according to ISO 7726:1998.
//
// Inputs:    tg     - Globe temperature [°C]
//            ta     - Air temperature [°C]  
//            ws     - Air velocity [m/s] (used only for forced convection)
//            D      - Globe diameter [m] (default 0.15 m)
//            eps    - Globe emissivity (default 0.95)
//            forced - Forced convection toggle (false = natural, true = forced)
//
// Outputs:   mrt    - Mean radiant temperature [°C]
//            hcg    - Convective heat transfer coefficient [W/(m²·K)] (debug)
//
// References:
//   [1] ISO 7726:1998, "Ergonomics of the thermal environment — Instruments
//       for measuring physical quantities", Annex B — Determination of the
//       mean radiant temperature using a black globe thermometer.
//       International Organization for Standardization, Geneva, 1998.
//       - Equation (B.1): General MRT formula
//       - Equation (B.3): Natural convection coefficient h_cg
//       - Equation (B.4): Forced convection coefficient h_cg
//
//   [2] ISO 7243:2017, "Ergonomics of the thermal environment — Assessment
//       of heat stress using the WBGT (wet bulb globe temperature) index".
//
//   [3] Thorsson, S., Lindberg, F., Eliasson, I., & Holmer, B. (2007).
//       "Different methods for estimating the mean radiant temperature in
//       an outdoor urban setting", International Journal of Climatology,
//       27(14), 1983-1993.
//
//   [4] Omori, T., Yamasaki, Y., Kawasaki, T., & Ishikawa, Y. (2020).
//       "Comparison between globe thermometer method and radiation
//       thermometer method for mean radiant temperature in a street canyon",
//       Sustainable Cities and Society, 62, 102417.
//
// ============================================================================

using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using System;

namespace ThermalComfort
{
    /// <summary>
    /// Mean Radiant Temperature (MRT) Calculator - Grasshopper native component
    /// 
    /// Calculates the mean radiant temperature from globe temperature (Tg),
    /// air temperature (Ta), and air velocity (v) according to ISO 7726:1998.
    /// 
    /// Physical background:
    /// The mean radiant temperature (MRT) is the uniform surface temperature of
    /// an imaginary black enclosure in which the radiant heat exchange between
    /// the human body and the surfaces equals that in the actual non‑uniform
    /// environment. The globe thermometer estimates MRT by measuring the
    /// equilibrium temperature of a painted black hollow copper sphere, which
    /// is simultaneously affected by radiative exchange with the surroundings
    /// and convective exchange with the air.
    /// </summary>
    public class MrtComponent : GH_Component
    {
        // =====================================================================
        // Physical Constants
        // =====================================================================

        /// <summary>
        /// Stefan-Boltzmann constant [σ]
        /// Unit: W/(m²·K⁴)
        /// Source: ISO 7726:1998 Annex B
        /// </summary>
        private const double SIGMA = 5.67e-8;

        /// <summary>
        /// Offset for converting Celsius to Kelvin
        /// Unit: K (°C + 273.15 = K)
        /// </summary>
        private const double OFFSET = 273.15;

        /// <summary>
        /// Default globe diameter [D]
        /// Unit: m
        /// Default: 0.15 m (150 mm)
        /// Source: ISO 7726:1998 Section 5.2
        /// </summary>
        private const double DEFAULT_GLOBE_DIAMETER = 0.15;

        /// <summary>
        /// Default globe surface emissivity [ε]
        /// Unit: dimensionless (0 ~ 1)
        /// Default: 0.95
        /// Source: ISO 7726:1998 — typical value for matt black paint coating
        /// </summary>
        private const double DEFAULT_GLOBE_EMISSIVITY = 0.95;

        /// <summary>
        /// Empirical constant for natural convection heat transfer coefficient
        /// Source: ISO 7726:1998, Equation (B.3)
        /// </summary>
        private const double NATURAL_CONV_CONST = 1.4;

        /// <summary>
        /// Empirical constant for forced convection heat transfer coefficient
        /// Source: ISO 7726:1998, Equation (B.4)
        /// </summary>
        private const double FORCED_CONV_CONST = 6.3;

        // =====================================================================
        // Constructor
        // =====================================================================

        /// <summary>
        /// Initialises a new instance of the MRT calculator component.
        /// </summary>
        public MrtComponent()
            : base(
                name: "Measure MRT",
                nickname: "MRT",
                description: "Mean Radiant Temperature (MRT) calculator based on ISO 7726:1998. It calculates MRT from globe temperature, air temperature, and air velocity.",
                category: "Neos",
                subCategory: "Thermophysics")
        {
        }

        // =====================================================================
        // GUID
        // =====================================================================

        /// <summary>
        /// The globally unique identifier for the component.
        /// Each Grasshopper component must have a unique GUID and cannot be reused.
        /// Generate using the GuidGen tool or Visual Studio's "Create GUID" feature.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("82CAB6E4-64E4-4D2C-869B-BE90DF112049"); }
        }

        // =====================================================================
        // Register Input Parameters
        // =====================================================================

        /// <summary>
        /// Registers all input parameters.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Input 0: Globe temperature [tg]
            pManager.AddNumberParameter(
                name: "Black Globe Temp",
                nickname: "Tg",
                description: "Globe temperature [°C]. Measurement reading from the globe thermometer.",
                access: GH_ParamAccess.item,
                @default: 25.0);

            // Input 1: Air temperature [ta]
            pManager.AddNumberParameter(
                name: "Air Temp",
                nickname: "Ta",
                description: "Air temperature [°C]. Dry‑bulb temperature of the surrounding environment.",
                access: GH_ParamAccess.item,
                @default: 25.0);

            // Input 2: Wind speed [ws]
            pManager.AddNumberParameter(
                name: "Wind Speed",
                nickname: "va",
                description: "Wind speed [m/s]. Used only when forced convection mode (forced=true) is selected. Ignored in natural convection mode.",
                access: GH_ParamAccess.item,
                @default: 0.0);

            // Input 3: Globe diameter [D]
            pManager.AddNumberParameter(
                name: "Globe Diameter",
                nickname: "D",
                description: "Globe diameter [m]. Default is 0.15 m (ISO 7726:1998 standard size).",
                access: GH_ParamAccess.item,
                @default: DEFAULT_GLOBE_DIAMETER);

            // Input 4: Globe emissivity [eps]
            pManager.AddNumberParameter(
                name: "Emissivity",
                nickname: "E",
                description: "Globe surface emissivity [dimensionless, 0~1]. Default is 0.95 (standard matt black paint coating).",
                access: GH_ParamAccess.item,
                @default: DEFAULT_GLOBE_EMISSIVITY);

            // Input 5: Forced convection toggle [forced]
            pManager.AddBooleanParameter(
                name: "Forced Convection",
                nickname: "FC",
                description: "Forced convection toggle. false = natural convection (default, for indoor low‑wind environments); true = forced convection (outdoor windy conditions, requires wind speed input).",
                access: GH_ParamAccess.item,
                @default: false);
        }

        // =====================================================================
        // Register Output Parameters
        // =====================================================================

        /// <summary>
        /// Registers all output parameters.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // Output 0: Mean radiant temperature [mrt]
            pManager.AddNumberParameter(
                name: "MRT",
                nickname: "MRT",
                description: "Mean radiant temperature [°C]. Result computed according to ISO 7726:1998.",
                access: GH_ParamAccess.item);

            // Output 1: Convective heat transfer coefficient [hcg]
            pManager.AddNumberParameter(
                name: "Convection Coef",
                nickname: "hcg",
                description: "Convective heat transfer coefficient [W/(m²·K)]. Intermediate result for debugging.",
                access: GH_ParamAccess.item);
        }

        // =====================================================================
        // Core Solution Logic (Solve Instance)
        // =====================================================================

        /// <summary>
        /// Executes the main MRT calculation logic.
        /// This method is called by the Grasshopper engine whenever any input parameter changes.
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // -----------------------------------------------------------------
            // Step 1: Retrieve all input parameters by numeric index
            // (Indices correspond to the order in RegisterInputParams)
            // -----------------------------------------------------------------

            double tg = 25.0;
            double ta = 25.0;
            double ws = 0.0;
            double D = DEFAULT_GLOBE_DIAMETER;
            double eps = DEFAULT_GLOBE_EMISSIVITY;
            bool forced = false;

            if (!DA.GetData(0, ref tg)) return;
            if (!DA.GetData(1, ref ta)) return;
            if (!DA.GetData(2, ref ws)) return;
            if (!DA.GetData(3, ref D)) return;
            if (!DA.GetData(4, ref eps)) return;
            if (!DA.GetData(5, ref forced)) return;

            // -----------------------------------------------------------------
            // Step 2: Validate parameter validity
            // -----------------------------------------------------------------

            if (D <= 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Globe diameter D must be greater than zero. The standard value is 0.15 m.");
                return;
            }

            if (eps <= 0 || eps > 1.0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Emissivity eps must be in the range (0, 1]. The standard value is 0.95.");
                return;
            }

            if (ws < 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Wind speed ws cannot be negative.");
                return;
            }

            // -----------------------------------------------------------------
            // Step 3: Calculate convective heat transfer coefficient h_cg [W/(m²·K)]
            // -----------------------------------------------------------------
            // Natural convection (Eq. B.3):  h_cg = 1.4 * ((|tg - ta|) / D)^0.25
            // Forced convection (Eq. B.4):   h_cg = 6.3 * (v^0.6 / D^0.4)
            // -----------------------------------------------------------------

            double hcg = 0.0;

            if (forced)
            {
                hcg = FORCED_CONV_CONST * Math.Pow(ws, 0.6) / Math.Pow(D, 0.4);

                if (ws == 0)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        "Wind speed is zero in forced convection mode. Results may be inaccurate; consider switching to natural convection mode.");
                }
            }
            else
            {
                double dt = Math.Abs(tg - ta);
                if (dt < 1e-12)
                    hcg = 0.0;
                else
                    hcg = NATURAL_CONV_CONST * Math.Pow(dt / D, 0.25);
            }

            // -----------------------------------------------------------------
            // Step 4: Compute MRT⁴ (Kelvin⁴) and validate physical bounds
            // -----------------------------------------------------------------
            // ISO 7726:1998, Eq. (B.1):
            //   MRT_K^4 = Tg_K^4 + (h_cg / (eps * sigma)) * (tg - ta)
            // -----------------------------------------------------------------

            double tgK = tg + OFFSET;
            double hrg = eps * SIGMA;
            double mrtK4 = Math.Pow(tgK, 4) + (hcg / hrg) * (tg - ta);

            // The fourth power of a Kelvin temperature must be strictly positive.
            // If not, the input conditions are outside the empirical model's valid range.
            if (mrtK4 <= 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "The computed MRT⁴ is non‑positive, which implies an average radiant temperature below absolute zero (−273.15 °C).\n" +
                    "This usually indicates that the input globe temperature and air temperature differ too greatly, pushing the empirical formula beyond its applicable range.\n" +
                    "Please check that your inputs (especially tg and ta) lie within reasonable environmental bounds (e.g., −20 °C to 60 °C).");
                return;
            }

            // -----------------------------------------------------------------
            // Step 5: Convert back to Celsius and final validation
            // -----------------------------------------------------------------

            double mrt = Math.Pow(mrtK4, 0.25) - OFFSET;

            if (double.IsNaN(mrt) || double.IsInfinity(mrt))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "MRT result is invalid (NaN or Infinity). Please check input parameters for reasonableness.");
                return;
            }

            // -----------------------------------------------------------------
            // Step 6: Set output data by numeric index
            // (Indices correspond to the order in RegisterOutputParams)
            // -----------------------------------------------------------------

            DA.SetData(0, mrt);
            DA.SetData(1, hcg);
        }

        // =====================================================================
        // Component Exposure & Icon
        // =====================================================================

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_measureMRTpng;
    }
}