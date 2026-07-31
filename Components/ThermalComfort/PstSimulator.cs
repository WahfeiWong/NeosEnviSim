using System;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using ThermalComfort.Core;

namespace ThermalComfort
{
    /// <summary>
    /// PST Simulator - Core calculation component implementing the MENEX_2005 two-step
    /// human heat balance method.
    /// 
    /// Model Equation: M + Q + C + E + Res = S
    /// where M=metabolic, Q=radiation balance, C=convection, E=evaporation,
    /// Res=respiration, S=net heat storage. All fluxes in W/m2.
    /// 
    /// Two-Step Method:
    /// Step 1 (Steady State 1): Calculate initial heat balance components using
    ///    ambient skin temperature. Outputs STI (Subjective Temperature Index).
    /// Step 2 (Steady State 2): Adjust skin temperature for thermoregulatory
    ///    adaptation (evaporative cooling effect), recalculate heat fluxes
    ///    with the adapted skin temperature. Outputs PST, PhS, HL, SkinTemp.
    ///    Step 2 is a single-pass calculation per MENEX_2005.
    /// 
    /// References:
    /// - Blazejczyk, K. (2005). MENEX_2005 - the updated version of man-environment 
    ///   heat exchange model. Proc. 11th Int. Conf. on Environmental Ergonomics, 
    ///   Ystad, Sweden, 222-225.
    /// - Blazejczyk, K., & Matzarakis, A. (2007). Assessment of bioclimatic 
    ///   differentiation of Poland based on the human heat balance. 
    ///   Geographia Polonica, 80, 63-82.
    /// - Blazejczyk, K., Epstein, Y., Jendritzky, G., Staiger, H., & Tinz, B. (2012).
    ///   Comparison of UTCI to selected thermal indices. Int J Biometeorol, 56, 515-535.
    /// </summary>
    public class PstSimulator : GH_Component
    {
        // Physical constants
        private const double SIGMA = 5.667e-8;    // Stefan-Boltzmann constant (W/m2/K4)
        private const double EPS_S = 0.97;         // Surface emissivity (natural objects)
        private const double EPS_H = 0.95;         // Human body emissivity

        public PstSimulator()
            : base("PST", "PST",
                  "Calculates Physiological Subjective Temperature (PST) using the " +
                  "MENEX_2005 two-step human heat balance model.",
                  "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("WeatherSet", "WS",
                "Structured weather data from PST Weather Settings component.",
                GH_ParamAccess.item);

            pManager.AddGenericParameter("HumanSet", "HS",
                "Structured human parameter data from PST Human Settings component.",
                GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            // Output 0: PST
            pManager.AddNumberParameter("PST", "PST",
                "Physiological Subjective Temperature (degC). Final thermal sensation " +
                "index after 15-20 min adaptation. Range interpretation: " +
                "<-36 frosty, -36 to -16.1 very cold, -16 to 4 cold, 4.1-14 cool, " +
                "14.1-24 comfortable, 24.1-34 warm, 34.1-44 hot, 44.1-54 very hot, " +
                ">54 sweltering.", GH_ParamAccess.item);

            // Output 1: STI (numerical, degC)
            pManager.AddNumberParameter("STI", "STI",
                "Subjective Temperature Index (degC). Thermal load felt before " +
                "adaptation processes. Range: <-38 extremely cold, -38 to -20.1 very cold, " +
                "-20 to -0.5 cold, -0.4 to 22.5 cool, 22.6-31.9 comfortable, 32-45.9 warm, " +
                "46-54.9 hot, 55-69.9 very hot, >=70 sweltering.", GH_ParamAccess.item);

            // Output 2: PhS (numerical, dimensionless)
            pManager.AddNumberParameter("PhS", "PhS",
                "Physiological Strain (dimensionless). Ratio of convective to " +
                "evaporative heat fluxes (C/E) from initial state. Indicates direction " +
                "and intensity of thermoregulatory adaptation: " +
                "<0 extreme hot strain, 0-0.24 great hot strain, 0.25-0.74 moderate hot, " +
                "0.75-1.50 thermoneutral, 1.51-4.00 moderate cold, 4.01-8.00 great cold, " +
                ">8.00 extreme cold strain.", GH_ParamAccess.item);

            // Output 3: PhS category (descriptive text)
            pManager.AddTextParameter("PhS_cat", "PhS_cat",
                "Physiological Strain category (descriptive scale). Derived from PhS value.",
                GH_ParamAccess.item);

            // Output 4: HL (Heat Load in man, dimensionless)
            pManager.AddNumberParameter("HL", "HL",
                "Heat Load in man (dimensionless). Load of central thermoregulation " +
                "system due to adaptation processes. Range: <=0.250 extreme cold stress, " +
                "0.251-0.820 great cold stress, 0.821-0.975 moderate cold stress, " +
                "0.976-1.025 thermoneutral, 1.026-1.180 moderate hot load, " +
                "1.181-1.750 great hot load, >=1.751 extreme hot load.", GH_ParamAccess.item);

            // Output 5: SkinTemp
            pManager.AddNumberParameter("SkinTemp", "Ts_R",
                "Mean skin temperature after adaptation (degC). " +
                "Result of thermoregulatory adjustment process.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Read inputs ---
            GH_PstWeatherSet ghWeather = null;
            if (!DA.GetData(0, ref ghWeather) || ghWeather?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "WeatherSet input is required. Connect PST Weather Settings component.");
                return;
            }

            GH_PstHumanSet ghHuman = null;
            if (!DA.GetData(1, ref ghHuman) || ghHuman?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "HumanSet input is required. Connect PST Human Settings component.");
                return;
            }

            PstWeatherSet w = ghWeather.Value;
            PstHumanSet h = ghHuman.Value;

            // --- Input consistency validation ---
            if (!h.AutoMet)
            {
                double expectedMet = 58.0 + 70.0 * h.WalkSpeed;
                if (Math.Abs(h.MetRate - expectedMet) > 30.0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"MetRate ({h.MetRate:F0} W/m2) and WalkSpeed ({h.WalkSpeed:F1} m/s) " +
                        $"appear inconsistent (expected M ~{expectedMet:F0} W/m2). " +
                        "Consider enabling AutoMet in PST Human Settings for automatic " +
                        "metabolic rate calculation from walk speed.");
                }
            }
            else
            {
                double expectedMet = 58.0 + 70.0 * h.WalkSpeed;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"AutoMet enabled: MetRate = {h.MetRate:F0} W/m2 " +
                    $"(calculated from WalkSpeed {h.WalkSpeed:F1} m/s, " +
                    $"ISO 8996: M = 58 + 70 * v).");
            }

            // === STEP 1: INITIAL STEADY STATE ===

            // Air temperature (K)
            double T_kelvin = 273.0 + w.AirTemp;

            // Clothing insulation (clo) - MENEX_2005 formula or user-specified
            double Icl;
            if (h.AutoClo)
            {
                if (w.AirTemp < -30.0)
                    Icl = 3.0;
                else if (w.AirTemp > 25.0)
                    Icl = 0.6;
                else
                    Icl = 1.691 - 0.0436 * w.AirTemp;
            }
            else
            {
                Icl = h.CloValue;
            }
            Icl = Math.Max(0.1, Math.Min(3.5, Icl));

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Used clothing insulation Icl = {Icl:F2} clo " +
                $"(AutoClo = {(h.AutoClo ? "ON" : "OFF")}, " +
                $"AirTemp = {w.AirTemp:F1}°C)");

            // Ground surface temperature Tg (degC)
            double Tg;
            if (w.AutoTg)
            {
                if (w.CloudCover >= 80.0)
                    Tg = w.AirTemp;
                else if (w.AirTemp >= 0)
                    Tg = 1.25 * w.AirTemp;
                else
                    Tg = 0.9 * w.AirTemp;
            }
            else
            {
                Tg = w.Tg;
            }

            // Combined air velocity (m/s)
            double v_combined = w.WindSpeed + h.WalkSpeed;
            if (v_combined < 0.1) v_combined = 0.1;

            // Coefficient of convective and radiative heat transfer hc (K/W/m2)
            double hc_coeff = (0.013 * w.Pressure - 0.04 * w.AirTemp - 0.503)
                            * Math.Pow(v_combined, 0.4);
            if (hc_coeff < 0.5) hc_coeff = 0.5;

            // Coefficient of heat transfer through clothing hc' (K/W/m2)
            double denom = Icl * (1.0 - 0.27 * Math.Pow(v_combined, 0.4));
            if (denom < 0.01) denom = 0.01;
            double hc_prime = (0.013 * w.Pressure - 0.04 * w.AirTemp - 0.503)
                            * 0.53 / denom;
            if (hc_prime < 0.1) hc_prime = 0.1;

            // Clothing reduction coefficient Irc (dimensionless)
            double Irc = hc_prime / (hc_prime + hc_coeff + 21.55e-8 * Math.Pow(T_kelvin, 3.0));
            if (Irc < 0.001) Irc = 0.001;
            if (Irc > 1.0) Irc = 1.0;

            // Clothing evaporation reduction coefficient Ie (dimensionless)
            double Ie = hc_prime / (hc_prime + hc_coeff);
            if (Ie < 0.001) Ie = 0.001;
            if (Ie > 1.0) Ie = 1.0;

            // Mean skin temperature Ts (degC) - Blazejczyk empirical formula
            double Ts = (26.4 + 0.02138 * w.MRT + 0.2095 * w.AirTemp
                      - 0.0185 * w.RH - 0.009 * w.WindSpeed)
                      + 0.6 * (Icl - 1.0) + 0.00128 * h.MetRate;

            if (Ts < 22.0) Ts = 22.0;
            if (Ts > 37.5) Ts = 37.5;

            // --- Long-wave radiation components ---
            double Lg = EPS_S * SIGMA * Math.Pow(273.0 + Tg, 4.0);
            double La = EPS_S * SIGMA * Math.Pow(T_kelvin, 4.0)
                      * (0.82 - 0.25 * Math.Pow(10.0, -0.094 * w.VapPres));
            double Ls = EPS_H * SIGMA * Math.Pow(273.0 + Ts, 4.0);
            double L = (0.5 * Lg + 0.5 * La - Ls) * Irc;

            // --- Absorbed solar radiation R using SolMrt model ---
            double R = (EPS_H * SIGMA * Math.Pow(273.0 + w.MRT, 4.0)
                      - EPS_S * SIGMA * Math.Pow(T_kelvin, 4.0)) * Irc;

            // Radiation balance Q = R + L
            double Q = R + L;

            // --- Evaporative heat transfer coefficient he (hPa/W/m2) ---
            double he = (w.AirTemp * (0.00006 * w.AirTemp - 0.00002 * w.Pressure + 0.011)
                       + 0.02 * w.Pressure - 0.773)
                       * 0.53 / denom;
            if (he < 0.01) he = 0.01;

            // --- Saturated vapour pressure at skin temperature es (hPa) ---
            double es = Math.Exp(0.058 * Ts + 2.003);

            // Skin wettedness w (dimensionless)
            double w_skin;
            if (Ts > 36.5)
                w_skin = 1.0;
            else if (Ts < 22.0)
                w_skin = 0.001;
            else
                w_skin = 1.031 / (37.5 - Ts) - 0.065;
            if (w_skin < 0.001) w_skin = 0.001;
            if (w_skin > 1.0) w_skin = 1.0;

            // --- Evaporative heat loss E (W/m2) ---
            double E = he * (w.VapPres - es) * w_skin * Ie
                     - (0.42 * (h.MetRate - 58.0) - 5.04);

            // --- Convective heat exchange C (W/m2) ---
            double C = hc_coeff * (w.AirTemp - Ts) * Irc;

            // --- Respiratory heat loss Res (W/m2) ---
            double Res = 0.0014 * h.MetRate * (w.AirTemp - 35.0)
                       + 0.0173 * h.MetRate * (0.1 * w.VapPres - 5.624);

            // --- Net heat storage S (W/m2) ---
            double S = h.MetRate + Q + C + E + Res;

            // === Calculate STI (Subjective Temperature Index) from Step 1 ===
            double absS_pow = Math.Pow(Math.Abs(S), 0.75);
            double stiCorrection = Math.Pow(absS_pow / (EPS_H * SIGMA) + Math.Pow(273.0, 4.0), 0.25) - 273.0;
            double STI = S < 0 ? w.MRT - stiCorrection : w.MRT + stiCorrection;

            // === Calculate PhS (Physiological Strain) from Step 1 ===
            double PhS_ratio;
            if (Math.Abs(E) < 0.001)
                PhS_ratio = E > 0 ? 1000.0 : -1000.0;
            else
                PhS_ratio = C / E;

            // === Calculate HL (Heat Load in man) from Step 1 ===
            double HL;
            double baseHL = (S + 1000.0) / 1000.0;
            if (S < 0.0 && E >= -50.0)
                HL = Math.Pow(baseHL, 5.0 / (1.0 + R));
            else if (S >= 0.0 && E >= -50.0)
                HL = Math.Pow(baseHL, 2.0 - 1.0 / (1.0 + R));
            else if (S < 0.0 && E < -50.0)
                HL = (E / -50.0) * Math.Pow(baseHL, 5.0 / (1.0 + R));
            else
                HL = (E / -50.0) * Math.Pow(baseHL, 2.0 - 1.0 / (1.0 + R));

            // === STEP 2: ADAPTIVE STEADY STATE (SINGLE-PASS) ===

            // Skin temperature adjustment for evaporative cooling
            double dTs = (E < -50.0) ? (E + 50.0) * 0.066 : 0.0;
            double Ts_R = Ts + dTs;
            if (Ts_R < 22.0) Ts_R = 22.0;
            if (Ts_R > 37.5) Ts_R = 37.5;

            double M_fixed = h.MetRate;
            double R_fixed = R;
            double Res_fixed = Res;

            // Resultant body long-wave emission at adapted skin temperature
            double Ls_R = EPS_H * SIGMA * Math.Pow(273.0 + Ts_R, 4.0);

            // Resultant net long-wave radiation
            double L_R = (0.5 * Lg + 0.5 * La - Ls_R) * Irc;

            // Resultant radiation balance
            double Q_R = R_fixed + L_R;

            // Inner mean radiant temperature iMrt (under clothing)
            // CRITICAL FIX: The document formula iMrt = {[R+(La+Lg)*Irc+0.5*Ls]/(sh*sigma)}^0.25-273
            // fails boundary-condition tests: at Irc=0 (perfect insulation) it gives ~0.84*Ts
            // instead of the physically required Ts. The corrected formula derived from the
            // clothing-cavity radiation balance is:
            //   iMrt = {[R + L + Ls]/(eps_h*sigma)}^0.25 - 273
            // where L = (0.5*Lg+0.5*La-Ls)*Irc is the net long-wave radiation from Step 1.
            // This is equivalent to R + (0.5*La+0.5*Lg)*Irc + Ls*(1-Irc) and satisfies:
            //   - Irc=1 (no clothing): iMrt -> MRT  (or Ta when MRT=Ta)
            //   - Irc=0 (perfect insulation): iMrt -> Ts
            double iMrt = Math.Pow((R_fixed + L + Ls) / (EPS_H * SIGMA), 0.25) - 273.0;

            // Resultant convective heat exchange C_R
            double C_R = hc_coeff * (iMrt - Ts_R) * Irc;

            // Vapour pressure under clothing e* (hPa)
            double e_star = 6.12 * Math.Pow(10.0, 7.5 * iMrt / (237.7 + iMrt))
                          * 0.01 * w.RH;

            // Saturated vapour pressure at resultant skin temperature es_R
            double es_R = Math.Exp(0.058 * Ts_R + 2.003);

            // Skin wettedness at resultant skin temperature w_R
            double w_R;
            if (Ts_R > 36.5)
                w_R = 1.0;
            else if (Ts_R < 22.0)
                w_R = 0.001;
            else
                w_R = 1.031 / (37.5 - Ts_R) - 0.065;
            if (w_R < 0.001) w_R = 0.001;
            if (w_R > 1.0) w_R = 1.0;

            // Resultant evaporative heat loss E_R
            double E_R = he * Math.Sqrt(v_combined) * (e_star - es_R) * w_R * Ie
                       - (0.42 * (M_fixed - 58.0) - 5.04);

            // Resultant net heat storage S_R (W/m2)
            double S_R = M_fixed + Q_R + C_R + E_R + Res_fixed;

            // === Calculate PST (Physiological Subjective Temperature) ===
            double absSR_pow = Math.Pow(Math.Abs(S_R), 0.75);
            double pstCorrection = Math.Pow(absSR_pow / (EPS_H * SIGMA) + Math.Pow(273.0, 4.0), 0.25) - 273.0;
            double PST = S_R < 0 ? iMrt - pstCorrection : iMrt + pstCorrection;

            // === PhS category string ===
            string PhS_category;
            if (PhS_ratio < 0.0)
                PhS_category = "Extreme hot strain";
            else if (PhS_ratio < 0.25)
                PhS_category = "Great hot strain";
            else if (PhS_ratio < 0.75)
                PhS_category = "Moderate hot strain";
            else if (PhS_ratio <= 1.50)
                PhS_category = "Thermoneutral (slight strain)";
            else if (PhS_ratio <= 4.00)
                PhS_category = "Moderate cold strain";
            else if (PhS_ratio <= 8.00)
                PhS_category = "Great cold strain";
            else
                PhS_category = "Extreme cold strain";

            // --- Runtime warnings for extreme conditions ---
            if (Math.Abs(S_R) > 200.0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Resultant net heat storage |S_R| = {Math.Abs(S_R):F1} W/m2 is very high. " +
                    "MENEX_2005 may be outside its valid range under these conditions " +
                    $"(MRT={w.MRT:F1}C, t={w.AirTemp:F1}C). " +
                    "Consider using UTCI or PET for extreme conditions, " +
                    "or verify MRT input is reasonable.");
            }

            // --- Output results ---
            DA.SetData(0, PST);
            DA.SetData(1, STI);
            DA.SetData(2, PhS_ratio);
            DA.SetData(3, PhS_category);
            DA.SetData(4, HL);
            DA.SetData(5, Ts_R);
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => Resources.PSTsim;
        public override Guid ComponentGuid => new Guid("75190B23-A3DA-4F32-B212-3169768A7484");
    }
}