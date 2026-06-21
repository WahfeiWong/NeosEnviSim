// ============================================================================
// Grasshopper Native Component - Dry-Bulb Temperature from Wet-Bulb & RH
// ============================================================================
// Class:     DryBulbFromWetBulbComponent
// Inherits:  Grasshopper.Kernel.GH_Component
// Function:  Calculate dry-bulb temperature from wet-bulb temperature and
//            relative humidity, given atmospheric pressure. The computation
//            uses the psychrometric equation (dry‑wet bulb relation) and
//            solves for the dry‑bulb temperature using the Newton‑Raphson
//            method.
//
// Inputs:    twb    - Wet‑bulb temperature [°C]
//            rh     - Relative humidity [%] (0 – 100)
//            P      - Atmospheric pressure [Pa] (default 101 325 Pa)
//
// Outputs:   ta     - Dry‑bulb temperature [°C]
//            resid  - Residual of the solved equation (should be near zero)
//                     (unit: hPa)
//
// References:
//   [1] ASHRAE Handbook – Fundamentals, Psychrometrics chapter.
//       Wet‑bulb temperature is defined as the temperature at which
//       water evaporates into air to saturate it, and the relation between
//       dry‑bulb (T), wet‑bulb (Tw), relative humidity (RH) and pressure (P)
//       is given by:
//           RH * e_s(T) = e_s(Tw) - A * P * (T - Tw)
//       where A = 0.000662 (1/°C) when pressures are in hPa, and e_s is
//       the saturation water vapour pressure.
//
//   [2] Tetens formula for saturation vapour pressure:
//           e_s(T) = 6.112 * exp(17.67 * T / (T + 243.5))   [hPa]
//
//   [3] ISO 7726:1998 – Instruments for measuring physical quantities,
//       annex on psychrometric calculations (general reference).
// ============================================================================

using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using System;

namespace ThermalComfort
{
    /// <summary>
    /// Component to compute dry‑bulb temperature from wet‑bulb temperature,
    /// relative humidity, and atmospheric pressure using the psychrometric
    /// equation.
    /// </summary>
    public class DryBulbFromWetBulbComponent : GH_Component
    {

        /// <summary>
        /// Psychrometric constant (A) in the dry‑wet bulb equation.
        /// Unit: 1/°C
        /// Used when pressure and vapour pressures are expressed in hPa.
        /// Source: ASHRAE Fundamentals.
        /// </summary>
        private const double PSYCHROM_CONST = 0.000662;

        /// <summary>
        /// Default atmospheric pressure at sea level.
        /// Unit: Pa
        /// </summary>
        private const double DEFAULT_PRESSURE = 101325.0;

        /// <summary>
        /// Maximum allowed Newton‑Raphson iterations.
        /// </summary>
        private const int MAX_ITERATIONS = 100;

        /// <summary>
        /// Convergence tolerance for the residual (in hPa).
        /// </summary>
        private const double TOLERANCE = 1e-6;

        /// <summary>
        /// Small delta for numerical derivative if analytic derivative fails.
        /// </summary>
        private const double DERIV_DELTA = 0.001;

        public DryBulbFromWetBulbComponent()
            : base(
                name: "Dry Bulb Temperature",
                nickname: "DryBulbTemp",
                description: "Calculates dry‑bulb temperature from wet‑bulb temperature, relative humidity, and atmospheric pressure. Uses the psychrometric equation and Newton‑Raphson iteration.",
                category: "Neos",
                subCategory: "Thermophysics")
        {
        }   

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Input 0: Wet‑bulb temperature [twb]
            pManager.AddNumberParameter(
                name: "Wet Bulb Temp",
                nickname: "Twb",
                description: "Wet‑bulb temperature [°C]. Typically measured with a wetted wick ventilated thermometer.",
                access: GH_ParamAccess.item,
                @default: 20.0);

            // Input 1: Relative humidity [rh]
            pManager.AddNumberParameter(
                name: "Relative Humidity",
                nickname: "RH",
                description: "Relative humidity [%] (range 0 – 100).",
                access: GH_ParamAccess.item,
                @default: 50.0);

            // Input 2: Atmospheric pressure [P]
            pManager.AddNumberParameter(
                name: "Pressure",
                nickname: "P",
                description: "Atmospheric pressure [Pa]. Default is 101 325 Pa (sea‑level standard).",
                access: GH_ParamAccess.item,
                @default: DEFAULT_PRESSURE);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // Output 0: Dry‑bulb temperature [ta]
            pManager.AddNumberParameter(
                name: "Dry Bulb Temp",
                nickname: "Ta",
                description: "Computed dry‑bulb temperature [°C].",
                access: GH_ParamAccess.item);

            // Output 1: Residual [resid]
            pManager.AddNumberParameter(
                name: "Residual",
                nickname: "Res",
                description: "Residual of the solved equation (should be near zero). Units: hPa. A small absolute value indicates good convergence.",
                access: GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // -----------------------------------------------------------------
            // Step 1: Retrieve inputs
            // -----------------------------------------------------------------

            double twb = 20.0;
            double rhPercent = 50.0;
            double P_Pa = DEFAULT_PRESSURE;

            if (!DA.GetData(0, ref twb)) return;
            if (!DA.GetData(1, ref rhPercent)) return;
            if (!DA.GetData(2, ref P_Pa)) return;

            // -----------------------------------------------------------------
            // Step 2: Validate inputs
            // -----------------------------------------------------------------

            // Check relative humidity range
            if (rhPercent < 0 || rhPercent > 100)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Relative humidity must be in the range [0, 100] %.");
                return;
            }
            double rh = rhPercent / 100.0; // convert to fraction

            // Check pressure > 0
            if (P_Pa <= 0)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Atmospheric pressure must be positive.");
                return;
            }
            double P_hPa = P_Pa / 100.0; // convert Pa to hPa for psychrometric equation

            // Check wet‑bulb temperature within reasonable range (e.g., -40 to 60°C)
            if (twb < -40 || twb > 60)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    "Wet‑bulb temperature is outside typical environmental range (-40 °C to 60 °C). Results may be unreliable.");
            }

            // -----------------------------------------------------------------
            // Step 3: Define the psychrometric function and its derivative
            // -----------------------------------------------------------------

            // Saturation vapour pressure (hPa) using Tetens formula
            double es(double T)
            {
                // T in °C, returns hPa
                return 6.112 * Math.Exp(17.67 * T / (T + 243.5));
            }

            // Derivative of es w.r.t. T (hPa/°C)
            double des_dT(double T)
            {
                // analytic derivative: d(es)/dT = es * 17.67 * 243.5 / (T + 243.5)^2
                return es(T) * 17.67 * 243.5 / Math.Pow(T + 243.5, 2);
            }

            // The function f(Ta) that must be zero:
            // f(Ta) = RH * es(Ta) - es(Twb) + A * P_hPa * (Ta - Twb) = 0
            double f(double Tdb)
            {
                return rh * es(Tdb) - es(twb) + PSYCHROM_CONST * P_hPa * (Tdb - twb);
            }

            // Derivative of f w.r.t. Ta:
            // f'(Ta) = RH * des_dT(Ta) + A * P_hPa
            double df(double Tdb)
            {
                return rh * des_dT(Tdb) + PSYCHROM_CONST * P_hPa;
            }

            // -----------------------------------------------------------------
            // Step 4: Initial guess and Newton‑Raphson iteration
            // -----------------------------------------------------------------

            // Start with Ta = Twb + 5°C (reasonable for moderate RH)
            double Ta = twb + 5.0;
            // Ensure Ta is not below Twb (physically impossible)
            if (Ta < twb) Ta = twb + 0.1;

            double residual = f(Ta);
            int iter = 0;
            bool converged = false;
            double Ta_new = Ta;

            // Newton‑Raphson loop
            while (iter < MAX_ITERATIONS)
            {
                double f_val = f(Ta);
                double df_val = df(Ta);

                // Check for near‑zero derivative to avoid division by zero
                if (Math.Abs(df_val) < 1e-12)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Warning,
                        "Derivative near zero at current guess. Newton‑Raphson may not converge.");
                    break;
                }

                Ta_new = Ta - f_val / df_val;

                // Ensure the new guess is physically plausible (Ta >= Twb)
                if (Ta_new < twb)
                {
                    // If the solution moves below Twb, set to Twb + small offset and continue
                    Ta_new = twb + 0.01;
                }

                residual = f(Ta_new);
                // Check convergence
                if (Math.Abs(residual) < TOLERANCE)
                {
                    converged = true;
                    Ta = Ta_new;
                    break;
                }

                // Update and prepare next iteration
                Ta = Ta_new;
                iter++;
            }

            // -----------------------------------------------------------------
            // Step 5: Check convergence and report
            // -----------------------------------------------------------------

            if (!converged)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    $"Newton‑Raphson did not converge after {MAX_ITERATIONS} iterations. Residual = {residual:F6} hPa. Consider checking inputs.");
                // Still output the best estimate
            }

            // Safety: if Ta is NaN or Inf, set to error
            if (double.IsNaN(Ta) || double.IsInfinity(Ta))
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    "Computation produced invalid result (NaN or Infinity). Please check input values.");
                return;
            }

            // Optional: if Ta is unrealistically outside [-50, 70] °C, warn
            if (Ta < -50 || Ta > 70)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Warning,
                    $"Computed dry‑bulb temperature ({Ta:F2}°C) is outside typical environmental range. Results may be unreliable.");
            }

            // -----------------------------------------------------------------
            // Step 6: Set outputs
            // -----------------------------------------------------------------

            DA.SetData(0, Ta);
            DA.SetData(1, residual);
        }


        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_CalTa;
        public override Guid ComponentGuid
        {
            get { return new Guid("2B9A25EB-9D82-4635-9666-221E5DE41D3B"); }
        }
    }
}