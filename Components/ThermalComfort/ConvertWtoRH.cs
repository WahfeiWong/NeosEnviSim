// ============================================================================
// Grasshopper Native Component - Humidity Ratio to Relative Humidity
// ============================================================================
// Class:     ConvertWtoRHComponent
// Inherits:  Grasshopper.Kernel.GH_Component
// Function:  Converts humidity ratio (mixing ratio) to relative humidity (RH)
//            given dry-bulb temperature and atmospheric pressure.
//
// Inputs:    W      - Humidity ratio (mixing ratio) [kg/kg]
//            Ta     - Dry-bulb temperature [°C]
//            P      - Atmospheric pressure [kPa] (default 101.325 kPa)
//
// Outputs:   RH     - Relative humidity [%]
//
// References:
//   [1] ASHRAE Handbook – Fundamentals, Chapter 1: Psychrometrics.
//       American Society of Heating, Refrigerating and Air-Conditioning
//       Engineers, Atlanta, GA.
//       - Definition of humidity ratio: W = 0.622 * (e / (P - e))
//       - Derivation of relative humidity from humidity ratio.
//
//   [2] Goff, J.A., and Gratch, S. (1946). "Low-pressure properties of water
//       from -160 to 212 °F". In Transactions of the American Society of
//       Heating and Ventilating Engineers, Vol. 52, pp. 95-122.
//       (Source of the saturation vapour pressure equations used below for
//       both liquid water and ice.)
//
//   [3] World Meteorological Organization (WMO). (2008). Guide to Meteorological
//       Instruments and Methods of Observation. WMO-No. 8, Geneva.
//       (General reference for psychrometric calculations.)
//
//   [4] ISO 7726:1998, "Ergonomics of the thermal environment — Instruments
//       for measuring physical quantities". Annex on psychrometric formulae.
// ============================================================================

using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using System;

namespace ThermalComfort
{
    /// <summary>
    /// Converts humidity ratio (mixing ratio) to relative humidity.
    /// 
    /// Important physical distinction:
    /// - This component uses "Humidity Ratio" (r, also called mixing ratio),
    ///   defined as the mass of water vapour per unit mass of dry air [kg/kg].
    /// - It does NOT use the meteorological "Specific Humidity" (q), which is
    ///   defined as mass of water vapour per unit mass of moist air [kg/kg].
    /// 
    /// Mathematically, the relation used is:
    ///   r = 0.622 * (e / (P - e))
    /// where e is the actual vapour pressure and P is total atmospheric pressure.
    /// Rearranging for e gives:  e = (r * P) / (0.622 + r)
    /// Then, RH = (e / e_s(Ta)) * 100%.
    /// </summary>
    public class ConvertWtoRHComponent : GH_Component
    {
        // =====================================================================
        // Constants
        // =====================================================================

        /// <summary>
        /// Ratio of molecular weight of water vapour to dry air (0.62198).
        /// Commonly approximated as 0.622 in engineering psychrometrics.
        /// </summary>
        private const double MOLAR_RATIO = 0.622;

        /// <summary>
        /// Default atmospheric pressure at sea level.
        /// Unit: kPa
        /// Source: Standard atmosphere (101.325 kPa).
        /// </summary>
        private const double DEFAULT_PRESSURE_KPA = 101.325;

        public ConvertWtoRHComponent()
          : base("ConvertWtoRH",
                 "W to RH",
                 "Convert humidity ratio (mixing ratio) to relative humidity",
                 "Neos",
                 "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Input 0: Humidity ratio (W)
            pManager.AddNumberParameter(
                name: "Humidity Ratio",
                nickname: "W",
                description: "Humidity ratio (mixing ratio) [kg/kg]. This is the mass of water vapour per unit mass of dry air." +
                "\nTypical range: 0 to 0.04 kg/kg (0 to 40 g/kg) for normal air.",
                access: GH_ParamAccess.item);

            // Input 1: Dry-bulb temperature (Ta)
            pManager.AddNumberParameter(
                name: "Dry Bulb Temp",
                nickname: "Ta",
                description: "Air dry-bulb temperature [°C]. This is the standard ambient air temperature.",
                access: GH_ParamAccess.item);

            // Input 2: Atmospheric pressure (P)
            pManager.AddNumberParameter(
                name: "Pressure",
                nickname: "P",
                description: "Total atmospheric pressure [kPa]. Default is 101.325 kPa (standard sea-level pressure).",
                access: GH_ParamAccess.item,
                @default: DEFAULT_PRESSURE_KPA);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter(
                name: "Relative Humidity",
                nickname: "RH",
                description: "Relative humidity [%] (range 0 to 100).",
                access: GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // -----------------------------------------------------------------
            // Step 1: Retrieve inputs
            // -----------------------------------------------------------------
            double W = 0.0;   // Humidity ratio [kg/kg]
            double Ta = 0.0;  // Dry-bulb temperature [°C]
            double P = 0.0;   // Atmospheric pressure [kPa]

            if (!DA.GetData(0, ref W)) return;
            if (!DA.GetData(1, ref Ta)) return;
            if (!DA.GetData(2, ref P)) return;

            // -----------------------------------------------------------------
            // Step 2: Validate inputs
            // -----------------------------------------------------------------
            if (W < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Humidity ratio (W) must be ≥ 0.");
                return;
            }

            if (P <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Atmospheric pressure (P) must be > 0.");
                return;
            }

            // Optional: Warn about extremely high W values
            if (W > 0.10) // 100 g/kg is unrealistically high for most environments
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Humidity ratio is very high (> 0.10 kg/kg). Verify input values.");
            }

            // -----------------------------------------------------------------
            // Step 3: Calculate saturation vapour pressure at dry-bulb temperature
            // -----------------------------------------------------------------
            // Convert Ta from Celsius to Kelvin for the saturation formula
            double T_K = Ta + 273.15;
            double e_s_hPa = CalculateSaturationVapourPressure(T_K);
            double e_s_kPa = e_s_hPa * 0.1; // Convert hPa to kPa

            // -----------------------------------------------------------------
            // Step 4: Calculate actual vapour pressure (e) from humidity ratio (W)
            // -----------------------------------------------------------------
            // Psychrometric relation:
            //   W = 0.622 * (e / (P - e))
            // Rearranging to solve for e:
            //   e = (W * P) / (0.622 + W)
            //
            // where:
            //   W = humidity ratio [kg/kg]
            //   P = total atmospheric pressure [kPa]
            //   e = actual water vapour pressure [kPa]
            // -----------------------------------------------------------------
            double denominator = MOLAR_RATIO + W;
            if (Math.Abs(denominator) < double.Epsilon)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Division by zero: Molar ratio (0.622) + W cannot be zero.");
                return;
            }

            double e_kPa = (W * P) / denominator;

            // -----------------------------------------------------------------
            // Step 5: Compute relative humidity (RH)
            // -----------------------------------------------------------------
            // Definition: RH = (e / e_s) * 100%
            // where e_s is the saturation vapour pressure at the given dry-bulb temperature.
            // -----------------------------------------------------------------
            if (e_s_kPa <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Saturation vapour pressure is non-positive. Check temperature input.");
                return;
            }

            double rh = (e_kPa / e_s_kPa) * 100.0;

            // Clamp relative humidity to valid physical range [0, 100%]
            if (rh < 0)
            {
                // This could happen due to numerical issues; set to 0.
                rh = 0.0;
            }
            else if (rh > 100.0)
            {
                // If RH > 100%, it implies supersaturation or input error.
                // Clamp to 100% and warn the user.
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Computed RH ({rh:F2}%) exceeds 100%. Clamped to 100%. This may indicate input values are inconsistent (e.g., W too high for given temperature).");
                rh = 100.0;
            }

            // Round to two decimal places for cleaner output
            rh = Math.Round(rh, 2);

            // -----------------------------------------------------------------
            // Step 6: Set output
            // -----------------------------------------------------------------
            DA.SetData(0, rh);
        }

        // =====================================================================
        // Saturation Vapour Pressure Calculator (Goff-Gratch Formulation)
        // =====================================================================

        /// <summary>
        /// Calculates the saturation vapour pressure using the Goff-Gratch
        /// formulation (1946), as adopted by the World Meteorological
        /// Organization (WMO) and ASHRAE Fundamentals.
        /// 
        /// This function provides separate equations for the liquid-water phase
        /// (above 273.15 K) and the ice phase (below 273.15 K).
        /// 
        /// Units: hPa (hectopascals), numerically equivalent to millibars.
        /// 
        /// References:
        ///   - Goff, J.A., and Gratch, S. (1946). Low-pressure properties of
        ///     water from -160 to 212 °F. ASHVE Transactions, Vol. 52.
        ///   - World Meteorological Organization (WMO). (2008). Guide to
        ///     Meteorological Instruments and Methods of Observation.
        ///     WMO-No. 8, Geneva.
        ///   - ASHRAE Handbook – Fundamentals, Chapter 1.
        /// </summary>
        /// <param name="T">Absolute temperature [K].</param>
        /// <returns>Saturation vapour pressure [hPa].</returns>
        private double CalculateSaturationVapourPressure(double T)
        {
            // -----------------------------------------------------------------
            // Above freezing point of water (0 °C = 273.15 K)
            // Formulation for water surface (liquid water).
            // -----------------------------------------------------------------
            if (T > 273.15)
            {
                double ratio = 373.16 / T; // Ratio of boiling point to current T
                double log10_es = -7.90298 * (ratio - 1)
                                  + 5.02808 * Math.Log10(ratio)
                                  - 1.3816e-7 * (Math.Pow(10, 11.344 * (1 - T / 373.16)) - 1)
                                  + 8.1328e-3 * (Math.Pow(10, -3.49149 * (ratio - 1)) - 1)
                                  + Math.Log10(1013.246); // Saturation pressure at boiling point (1013.246 hPa)
                return Math.Pow(10, log10_es);
            }
            // -----------------------------------------------------------------
            // Below freezing point of water (0 °C = 273.15 K)
            // Formulation for ice surface (sublimation).
            // -----------------------------------------------------------------
            else if (T < 273.15)
            {
                double ratio = 273.16 / T; // Ratio of triple point to current T
                double log10_es = -9.09718 * (ratio - 1)
                                  - 3.56654 * Math.Log10(ratio)
                                  + 0.876793 * (1 - T / 273.16)
                                  + Math.Log10(6.1071); // Saturation pressure at triple point (6.1071 hPa)
                return Math.Pow(10, log10_es);
            }
            // -----------------------------------------------------------------
            // Exactly at the triple point (0 °C = 273.15 K)
            // Both formulations converge to 6.1071 hPa.
            // -----------------------------------------------------------------
            else
            {
                return 6.1071;
            }
        }


        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_WtoRH2;
        public override Guid ComponentGuid => new Guid("{818BC2AD-1B8D-45A0-BEF0-53E6D24792A6}");
    }
}

