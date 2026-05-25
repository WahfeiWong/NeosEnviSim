using System;

namespace SolarPV.Core
{
    /// <summary>
    /// Photovoltaic cell temperature and efficiency correction models.
    /// 
    /// References:
    /// [1] Fuentes, M.K. (1987). A Simplified Thermal Model for Flat-Plate Photovoltaic Arrays. 
    ///     Sandia National Laboratories, SAND85-0330.
    /// [2] Faiman, D. (2008). Assessing the outdoor operating temperature of photovoltaic modules. 
    ///     Progress in Photovoltaics, 16(4), 307-315.
    /// [3] King, D.L., Boyson, W.E., & Kratochvil, J.A. (2004). 
    ///     Photovoltaic Array Performance Model. Sandia National Laboratories, SAND2004-3535.
    /// [4] IEC 61215-1:2016 - Terrestrial photovoltaic (PV) modules - Design qualification and type approval.
    /// </summary>
    public static class TemperatureModel
    {
        /// <summary>
        /// Calculate PV cell operating temperature using the Faiman model.
        /// 
        /// The Faiman model provides a more physically accurate representation than basic NOCT:
        /// T_cell = T_amb + G / (U0 + U1 * v_wind)
        /// 
        /// Where U0 and U1 are thermal loss coefficients that depend on mounting configuration.
        /// 
        /// Reference: Faiman, D. (2008). Progress in Photovoltaics, 16(4), 307-315.
        /// </summary>
        /// <param name="ambientTemp">Ambient air temperature [°C]</param>
        /// <param name="irradiance">Solar irradiance on the cell surface [W/m²]</param>
        /// <param name="windSpeed">Wind speed [m/s], measured at 10m height</param>
        /// <param name="mountingType">Mounting configuration affecting heat transfer</param>
        /// <returns>Cell temperature [°C]</returns>
        public static double CalculateCellTemperatureFaiman(
            double ambientTemp,
            double irradiance,
            double windSpeed = 1.0,
            MountingType mountingType = MountingType.FreeStanding)
        {
            // Get thermal loss coefficients based on mounting type
            GetThermalCoefficients(mountingType, out double u0, out double u1);

            // Wind speed correction - ensure non-negative
            windSpeed = Math.Max(0.0, windSpeed);

            // Faiman model: T_cell = T_amb + G / (U0 + U1 * v)
            // U0 captures constant heat loss (conduction, natural convection, radiation)
            // U1 captures wind-dependent heat loss (forced convection)
            double denominator = u0 + u1 * windSpeed;

            // Prevent division by zero
            if (denominator < 0.1) denominator = 0.1;

            double tCell = ambientTemp + irradiance / denominator;
            return tCell;
        }

        /// <summary>
        /// Calculate PV cell operating temperature using the standard NOCT model.
        /// 
        /// Standard NOCT conditions: irradiance = 800 W/m², ambient = 20°C, wind = 1 m/s
        /// T_cell = T_amb + (NOCT - 20) / 800 * G * thermalRatio
        /// 
        /// where thermalRatio = (1 - eta) / (1 - eta_ref) accounts for the fact that
        /// higher-efficiency modules convert more incoming radiation to electricity,
        /// leaving less energy to be dissipated as heat.
        /// 
        /// The NOCT value is typically measured at a reference module efficiency of ~10%.
        /// For a module with efficiency eta, the temperature rise scales proportionally
        /// to the heat generation fraction (1 - eta).
        /// 
        /// Reference: IEC 61215-1:2016; Faiman (2008) thermal balance principles.
        /// </summary>
        /// <param name="ambientTemp">Ambient air temperature [°C]</param>
        /// <param name="irradiance">Solar irradiance [W/m²]</param>
        /// <param name="noct">Nominal Operating Cell Temperature [°C], typically 45-48°C</param>
        /// <param name="moduleEfficiency">Module efficiency at STC [fraction, 0-1], default 0.20</param>
        /// <param name="noctRefEfficiency">Reference efficiency at which NOCT was measured [0-1], default 0.10</param>
        /// <returns>Cell temperature [°C]</returns>
        public static double CalculateCellTemperatureNOCT(
            double ambientTemp,
            double irradiance,
            double noct = 45.0,
            double moduleEfficiency = 0.20,
            double noctRefEfficiency = 0.10)
        {
            // FIX P3: Physics-based efficiency correction using thermal balance ratio.
            // NOCT is measured at a reference efficiency (typically ~10%).
            // Higher-efficiency modules generate less heat per unit irradiance.
            // Heat generation fraction: (1 - eta) / (1 - eta_ref)
            // This scales the temperature RISE, not the absolute temperature.
            double etaRef = (noctRefEfficiency > 0) ? noctRefEfficiency : 0.10;
            double thermalRatio = (1.0 - moduleEfficiency) / (1.0 - etaRef);
            thermalRatio = Math.Max(0.5, Math.Min(2.0, thermalRatio));

            double deltaT = (noct - 20.0) / 800.0 * irradiance * thermalRatio;
            return ambientTemp + deltaT;
        }

        /// <summary>
        /// Estimate cell temperature using the Sandia PV Array Performance Model.
        /// 
        /// T_module = E * exp(a + b * v) + T_amb
        /// T_cell = T_module + E / E0 * deltaT
        /// 
        /// Where:
        /// - E = irradiance [W/m²]
        /// - v = wind speed [m/s]
        /// - a, b = empirical coefficients
        /// - deltaT = temperature difference between cell and module back surface at 1000 W/m²
        /// - E0 = 1000 W/m² (reference irradiance)
        /// 
        /// Reference: King et al. (2004). Sandia National Laboratories, SAND2004-3535.
        /// </summary>
        /// <param name="ambientTemp">Ambient temperature [°C]</param>
        /// <param name="irradiance">Irradiance [W/m²]</param>
        /// <param name="windSpeed">Wind speed [m/s]</param>
        /// <param name="a">Sandia parameter a, default -3.47 for glass/cell/polymer</param>
        /// <param name="b">Sandia parameter b, default -0.0594</param>
        /// <param name="deltaT">Temperature difference at 1000 W/m² [°C], default 3</param>
        /// <returns>Cell temperature [°C]</returns>
        public static double SandiaCellTemperature(
            double ambientTemp,
            double irradiance,
            double windSpeed = 1.0,
            double a = -3.47,
            double b = -0.0594,
            double deltaT = 3.0)
        {
            windSpeed = Math.Max(0.0, windSpeed);
            irradiance = Math.Max(0.0, irradiance);

            // T_module = ambient + irradiance * exp(a + b * windspeed)
            double tModule = ambientTemp + irradiance * Math.Exp(a + b * windSpeed);

            // T_cell = T_module + (E/E0) * deltaT
            double tCell = tModule + (irradiance / 1000.0) * deltaT;

            return tCell;
        }

        /// <summary>
        /// Unified cell temperature calculation - selects appropriate model based on available data.
        /// Prefers Faiman model for accuracy, falls back to NOCT if wind data unavailable.
        /// </summary>
        /// <param name="ambientTemp">Ambient temperature [°C]</param>
        /// <param name="irradiance">Incident irradiance [W/m²]</param>
        /// <param name="windSpeed">Wind speed [m/s], if available</param>
        /// <param name="noct">NOCT value [°C], used as fallback</param>
        /// <param name="mountingType">Mounting configuration</param>
        /// <param name="moduleEfficiency">Module efficiency at STC [0-1], for NOCT thermal ratio correction</param>
        /// <param name="noctRefEfficiency">Reference efficiency at which NOCT was measured [0-1], default 0.10</param>
        /// <returns>Cell temperature [°C]</returns>
        public static double CalculateCellTemperature(
            double ambientTemp,
            double irradiance,
            double windSpeed,
            double noct = 45.0,
            MountingType mountingType = MountingType.FreeStanding,
            double moduleEfficiency = 0.20,
            double noctRefEfficiency = 0.10)
        {
            // Use Faiman model when wind speed is available (more accurate)
            if (windSpeed >= 0 && mountingType != MountingType.NoWindCorrection)
            {
                return CalculateCellTemperatureFaiman(ambientTemp, irradiance, windSpeed, mountingType);
            }

            // Fall back to NOCT model when wind data is not available
            return CalculateCellTemperatureNOCT(ambientTemp, irradiance, noct, moduleEfficiency, noctRefEfficiency);
        }

        /// <summary>
        /// Calculate temperature-corrected PV efficiency.
        /// 
        /// eta(T) = eta_STC * [1 + gamma * (T_cell - T_STC)]
        /// 
        /// Where gamma is the temperature coefficient of power (negative for Si).
        /// 
        /// Reference: IEC 60891:2009 - Photovoltaic devices - Procedures for temperature and irradiance corrections.
        /// </summary>
        /// <param name="efficiencyStc">Module efficiency at STC (25°C) [fraction, 0-1]</param>
        /// <param name="tempCoefficient">Temperature coefficient of power [%/°C or 1/°C]. 
        /// Typically -0.35 to -0.45 %/°C for crystalline silicon.</param>
        /// <param name="cellTemperature">Actual cell temperature [°C]</param>
        /// <param name="stcTemperature">STC reference temperature [°C], default 25</param>
        /// <param name="tempCoefficientIsPercent">True if coefficient is in %/°C, false if in 1/°C</param>
        /// <returns>Temperature-corrected efficiency [fraction, 0-1]</returns>
        public static double CorrectEfficiencyForTemperature(
            double efficiencyStc,
            double tempCoefficient,
            double cellTemperature,
            double stcTemperature = 25.0,
            bool tempCoefficientIsPercent = true)
        {
            // Convert coefficient to fraction form if given in percent
            double gamma = tempCoefficientIsPercent ? tempCoefficient / 100.0 : tempCoefficient;

            double deltaT = cellTemperature - stcTemperature;
            double correctionFactor = 1.0 + gamma * deltaT;

            // Clamp to reasonable bounds
            correctionFactor = Math.Max(0.0, correctionFactor);

            return efficiencyStc * correctionFactor;
        }

        /// <summary>
        /// Calculate DC power output of a PV module accounting for irradiance and temperature.
        /// 
        /// Uses the standard linear model:
        /// P = G * A * eta_STC * [1 + gamma * (T_cell - T_STC)]
        /// 
        /// Alternatively expressed as:
        /// P = P_STC * (G / G_STC) * [1 + gamma * (T_cell - T_STC)]
        /// where P_STC = G_STC * A * eta_STC
        /// 
        /// Reference: IEC 60891:2009
        /// </summary>
        /// <param name="irradiance">Incident irradiance on module surface [W/m²]</param>
        /// <param name="moduleArea">Module area [m²]</param>
        /// <param name="efficiencyStc">Efficiency at STC [fraction, 0-1]</param>
        /// <param name="tempCoefficient">Temperature coefficient [%/°C or 1/°C]</param>
        /// <param name="cellTemperature">Cell temperature [°C]</param>
        /// <param name="stcIrradiance">STC irradiance [W/m²], default 1000</param>
        /// <param name="stcTemperature">STC temperature [°C], default 25</param>
        /// <param name="tempCoefficientIsPercent">True if %/°C</param>
        /// <returns>DC power in Watts</returns>
        public static double CalculateDcPower(
            double irradiance,
            double moduleArea,
            double efficiencyStc,
            double tempCoefficient,
            double cellTemperature,
            double stcIrradiance = 1000.0,
            double stcTemperature = 25.0,
            bool tempCoefficientIsPercent = true)
        {
            double correctedEff = CorrectEfficiencyForTemperature(
                efficiencyStc, tempCoefficient, cellTemperature, stcTemperature, tempCoefficientIsPercent);

            // P = G * A * eta(T)
            // This is equivalent to: P = P_STC * (G/G_STC) * [1 + gamma*(T-25)]
            // since P_STC = 1000 * A * eta_STC
            double power = irradiance * moduleArea * correctedEff;
            return Math.Max(0.0, power);
        }

        /// <summary>
        /// Get thermal loss coefficients for different mounting configurations.
        /// 
        /// Values from Faiman (2008) and subsequent validation studies.
        /// U0 [W/(m²·K)] - constant heat loss coefficient
        /// U1 [W/(m²·K)/(m/s)] - wind-dependent heat loss coefficient
        /// </summary>
        private static void GetThermalCoefficients(MountingType type, out double u0, out double u1)
        {
            switch (type)
            {
                case MountingType.FreeStanding:
                    // Open rack mounting (best ventilation)
                    u0 = 25.0; u1 = 6.84;
                    break;
                case MountingType.RoofMounted:
                    // Roof-mounted with some rear ventilation
                    u0 = 20.0; u1 = 5.70;
                    break;
                case MountingType.BuildingIntegrated:
                    // BIPV (minimal rear ventilation)
                    u0 = 15.0; u1 = 4.50;
                    break;
                case MountingType.Concentrator:
                    // CPV systems (typically actively cooled)
                    u0 = 30.0; u1 = 8.00;
                    break;
                default:
                    u0 = 25.0; u1 = 6.84;
                    break;
            }
        }
    }

    /// <summary>
    /// PV module mounting type configurations affecting thermal behavior.
    /// </summary>
    public enum MountingType
    {
        /// <summary>Free-standing open rack (best ventilation, lowest temperatures)</summary>
        FreeStanding,
        /// <summary>Roof-mounted with rear ventilation gap</summary>
        RoofMounted,
        /// <summary>Building-integrated (BIPV, minimal ventilation, highest temperatures)</summary>
        BuildingIntegrated,
        /// <summary>Concentrator PV (active cooling)</summary>
        Concentrator,
        /// <summary>Use NOCT model without wind correction</summary>
        NoWindCorrection
    }
}
