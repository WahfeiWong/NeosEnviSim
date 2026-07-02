// ============================================================================
// Grasshopper Native Component - Dry-Bulb Temperature from Wet-Bulb & RH
// ============================================================================
// Class:     DryBulbFromWetBulbComponent
// Inherits:  Grasshopper.Kernel.GH_Component
// Function:  Calculate dry-bulb temperature from wet-bulb temperature,
//            relative humidity, atmospheric pressure, and air velocity.
//            Uses the Goff‑Gratch saturation vapour pressure formula and a
//            velocity‑dependent psychrometric constant.
//
// Inputs:    twb    - Wet‑bulb temperature [°C]
//            rh     - Relative humidity [%] (0 – 100)
//            P      - Atmospheric pressure [Pa] (default 101 325 Pa)
//            V      - Air speed over wet‑bulb [m/s] (default 5.0)
//
// Outputs:   ta     - Dry‑bulb temperature [°C]
//            resid  - Residual of the solved equation (should be near zero)
//                     (unit: hPa)
//
// ============================================================================
// REFERENCES
// ============================================================================
// [1] Goff, J. A., & Gratch, S. (1946). Low-pressure properties of water from
//     −160 to 212 °F. Trans. ASHVE, 52, 95–122.
// [2] Goff, J. A. (1957). Saturation pressure of water on the new Kelvin
//     temperature scale. Trans. ASHAE, 63, 347–354.
// [3] List, R. J. (1951). Smithsonian Meteorological Tables (6th rev. ed.).
//     Smithsonian Institution Press. (5th reprint 1984).
// [4] World Meteorological Organization. (1988). General Meteorological
//     Standards and Recommended Practices. WMO Tech. Reg., WMO-No. 49, App. A.
// [5] World Meteorological Organization. (2014). Guide to Meteorological
//     Instruments and Methods of Observation (CIMO Guide). WMO-No. 8.
// [6] ASHRAE. (2021). ASHRAE Handbook – Fundamentals (SI ed.). Chapter 1,
//     Psychrometrics. American Society of Heating, Refrigerating and
//     Air-Conditioning Engineers.
// [7] Press, W. H., Teukolsky, S. A., Vetterling, W. T., & Flannery, B. P.
//     (2007). Numerical Recipes: The Art of Scientific Computing (3rd ed.).
//     Cambridge University Press. Chapter 9.
// [8] ISO. (1998). Ergonomics of the thermal environment – Instruments for
//     measuring physical quantities (ISO 7726:1998).
// ============================================================================

using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using System;

namespace ThermalComfort
{
    public class DryBulbFromWetBulbComponent : GH_Component
    {
        private const double DEFAULT_PRESSURE = 101325.0;
        private const int MAX_ITERATIONS = 100;
        private const double TOLERANCE = 1e-6;
        private const double DERIV_DELTA = 0.001; // for numerical derivative

        public DryBulbFromWetBulbComponent()
            : base(
                name: "Dry Bulb Temperature",
                nickname: "DryBulbTemp",
                description: "Calculates dry‑bulb temperature from wet‑bulb, RH, pressure and air velocity. Uses Goff‑Gratch saturation pressure and velocity‑dependent psychrometric constant.",
                category: "Neos",
                subCategory: "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter(
                name: "Wet Bulb Temp",
                nickname: "Twb",
                description: "Wet‑bulb temperature [°C].",
                access: GH_ParamAccess.item,
                @default: 20.0);

            pManager.AddNumberParameter(
                name: "Relative Humidity",
                nickname: "RH",
                description: "Relative humidity [%] (0 – 100).",
                access: GH_ParamAccess.item,
                @default: 50.0);

            pManager.AddNumberParameter(
                name: "Pressure",
                nickname: "P",
                description: "Atmospheric pressure [Pa]. Default 101 325 Pa.",
                access: GH_ParamAccess.item,
                @default: DEFAULT_PRESSURE);

            pManager.AddNumberParameter(
                name: "Wind Speed",
                nickname: "Va",
                description: "Air speed over the wet‑bulb thermometer [m/s]. Default 5.0 (ventilated).",
                access: GH_ParamAccess.item,
                @default: 5.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter(
                name: "Dry Bulb Temp",
                nickname: "Ta",
                description: "Computed dry‑bulb temperature [°C].",
                access: GH_ParamAccess.item);

            pManager.AddNumberParameter(
                name: "Residual",
                nickname: "Res",
                description: "Residual of the solved equation (should be near zero). Units: hPa.",
                access: GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- 1. Retrieve inputs ---
            double twb = 20.0;
            double rhPercent = 50.0;
            double P_Pa = DEFAULT_PRESSURE;
            double V = 5.0;

            if (!DA.GetData(0, ref twb)) return;
            if (!DA.GetData(1, ref rhPercent)) return;
            if (!DA.GetData(2, ref P_Pa)) return;
            if (!DA.GetData(3, ref V)) V = 5.0;

            // --- 2. Validate inputs ---
            if (rhPercent < 0 || rhPercent > 100)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Relative humidity must be in [0, 100] %.");
                return;
            }
            double rh = rhPercent / 100.0;

            if (P_Pa <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Atmospheric pressure must be positive.");
                return;
            }
            double P_hPa = P_Pa / 100.0; // Pa → hPa

            if (twb < -40 || twb > 60)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Wet‑bulb temperature outside typical range (-40 °C to 60 °C).");

            if (V < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Wind speed cannot be negative.");
                return;
            }

            // --- 3. Compute psychrometric constant A based on wind speed ---
            // Interpolation between ASHRAE values: 0.000662 at 5 m/s (ventilated),
            // 0.0008 at 0.5 m/s (natural ventilation). Linear interpolation.
            double A;
            if (V >= 5.0)
                A = 0.000662;
            else if (V <= 0.5)
                A = 0.0008;
            else
                A = 0.000662 + (0.0008 - 0.000662) * (5.0 - V) / 4.5;

            // --- 4. Goff‑Gratch saturation vapour pressure (hPa) ---
            double es_goff_gratch(double T_C)
            {
                double T_K = T_C + 273.15;
                double log10p;

                if (T_K >= 273.15) // over water (including 0°C)
                {
                    double a = 373.16 / T_K;
                    double log10_373_T = Math.Log10(a);
                    double term1 = -7.90298 * (a - 1.0);
                    double term2 = 5.02808 * log10_373_T;
                    double term3 = -1.3816e-7 * (Math.Pow(10.0, 11.344 * (1.0 - T_K / 373.16)) - 1.0);
                    double term4 = 8.1328e-3 * (Math.Pow(10.0, -3.49149 * (a - 1.0)) - 1.0);
                    log10p = term1 + term2 + term3 + term4 + Math.Log10(1013.246);
                }
                else // over ice (T_K < 273.15)
                {
                    double b = 273.16 / T_K;
                    double term1 = -9.09718 * (b - 1.0);
                    double term2 = -3.56654 * Math.Log10(b);
                    double term3 = 0.876793 * (1.0 - T_K / 273.16);
                    log10p = term1 + term2 + term3 + Math.Log10(6.1071);
                }

                return Math.Pow(10.0, log10p);
            }

            // Numerical derivative of es with respect to T (hPa/°C)
            double des_dT(double T_C)
            {
                double delta = 0.001;
                return (es_goff_gratch(T_C + delta) - es_goff_gratch(T_C - delta)) / (2.0 * delta);
            }

            // --- 5. Define the psychrometric function f(Ta) and its derivative ---
            // f(Ta) = RH * es(Ta) - es(Twb) + A * P_hPa * (Ta - Twb) = 0
            double f(double Tdb)
            {
                return rh * es_goff_gratch(Tdb) - es_goff_gratch(twb) + A * P_hPa * (Tdb - twb);
            }

            // Derivative via numerical differentiation of f
            double df(double Tdb)
            {
                double delta = 0.001;
                return (f(Tdb + delta) - f(Tdb - delta)) / (2.0 * delta);
            }

            // --- 6. Newton‑Raphson iteration ---
            double Ta = twb + 5.0;
            if (Ta < twb) Ta = twb + 0.1;

            double residual = f(Ta);
            int iter = 0;
            bool converged = false;
            double Ta_new = Ta;

            while (iter < MAX_ITERATIONS)
            {
                double f_val = f(Ta);
                double df_val = df(Ta);

                if (Math.Abs(df_val) < 1e-12)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Derivative near zero; Newton‑Raphson may not converge.");
                    break;
                }

                Ta_new = Ta - f_val / df_val;

                // Physical constraint: Ta >= Twb
                if (Ta_new < twb)
                    Ta_new = twb + 0.01;

                residual = f(Ta_new);
                if (Math.Abs(residual) < TOLERANCE)
                {
                    converged = true;
                    Ta = Ta_new;
                    break;
                }

                Ta = Ta_new;
                iter++;
            }

            if (!converged)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Newton‑Raphson did not converge after {MAX_ITERATIONS} iterations. Residual = {residual:F6} hPa.");
            }

            if (double.IsNaN(Ta) || double.IsInfinity(Ta))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Computation produced invalid result.");
                return;
            }

            if (Ta < -50 || Ta > 70)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Computed dry‑bulb temperature ({Ta:F2}°C) is outside typical range.");

            // --- 7. Set outputs ---
            DA.SetData(0, Ta);
            DA.SetData(1, residual);
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_CalTa;
        public override Guid ComponentGuid => new Guid("2B9A25EB-9D82-4635-9666-221E5DE41D3B");
    }
}