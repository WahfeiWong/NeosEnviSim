using System;
using Common.Core;
namespace SolarPV.Core
{
    /// <summary>
    /// Inverter and MPPT (Maximum Power Point Tracking) Model.
    /// 
    /// Based on the PVWatts inverter model which uses a simplified part-load efficiency curve
    /// derived from Sandia National Laboratories empirical data.
    /// 
    /// References:
    /// [1] Dobos, A.P. (2012). PVWatts Version 5 Manual. NREL/TP-6A20-62641.
    ///     https://www.nrel.gov/docs/fy14osti/62641.pdf
    /// [2] King, D.L., Boyson, W.E., & Kratochvil, J.A. (2004). Photovoltaic Array Performance Model. Sandia National Laboratories, SAND2004-3535.
    /// </summary>
    public static class InverterMPPTModel
    {
        /// <summary>
        /// Calculate AC power output from DC power using the PVWatts inverter model.
        /// 
        /// The PVWatts model uses a normalized efficiency curve:
        /// eta = eta_nom * [ -0.0162*zeta - 0.0059/zeta + 0.9858 ]  for 0.01 &lt;= zeta &lt;= 1.2
        /// where zeta = P_dc / P_dc0 (normalized DC power)
        /// 
        /// For zeta &lt; 0.01 (very low power), linear interpolation from origin.
        /// For zeta &gt; 1.2 (overload), efficiency decreases linearly.
        /// 
        /// Reference: Dobos (2012), NREL/TP-6A20-62641, Eq. 8-9.
        /// </summary>
        /// <param name="dcPower">DC input power [W]</param>
        /// <param name="dcVoltage">DC input voltage [V]</param>
        /// <param name="config">Inverter configuration</param>
        /// <param name="clippingLossWatts">Output: energy lost due to AC power clipping [W]</param>
        /// <returns>AC output power [W]</returns>
        public static double CalculateACPower(
            double dcPower, double dcVoltage,
            InverterConfig config,
            out double clippingLossWatts)
        {
            clippingLossWatts = 0;

            if (!config.EnableInverterModel)
                return dcPower;

            // Nighttime tare losses
            if (dcPower <= 0)
                return 0; // Return 0 instead of negative to avoid negative energy sums

            // Check MPPT voltage range
            if (dcVoltage < config.MPPTMinVoltage || dcVoltage > config.MPPTMaxVoltage)
            {
                return 0;
            }

            // Calculate DC rating from AC rating and nominal efficiency
            // P_dc0 = P_ac_rated / eta_nom
            double dcRating = config.RatedPower / config.NominalEfficiency;

            if (dcRating <= 0)
                return 0;

            // Normalized DC power (zeta)
            double zeta = dcPower / dcRating;

            // Calculate operating efficiency using PVWatts polynomial
            // eta_op = eta_nom * PLR, where PLR is the part-load ratio
            double plr; // Part-load ratio

            if (zeta >= 0.01 && zeta <= 1.2)
            {
                // PVWatts standard efficiency curve (Dobos, 2012)
                plr = -0.0162 * zeta - 0.0059 / zeta + 0.9858;
                plr = Math.Max(0.0, Math.Min(1.0, plr));
            }
            else if (zeta < 0.01)
            {
                // Linear interpolation for very low power
                plr = zeta / 0.01 * (-0.0162 * 0.01 - 0.0059 / 0.01 + 0.9858);
                plr = Math.Max(0.0, plr);
            }
            else // zeta > 1.2
            {
                // Efficiency decreases linearly above overload
                plr = -0.0162 * 1.2 - 0.0059 / 1.2 + 0.9858;
                plr = Math.Max(0.0, plr) * (1.0 - 0.05 * (zeta - 1.2));
                plr = Math.Max(0.0, plr);
            }

            double etaOp = config.NominalEfficiency * plr;
            etaOp = Math.Max(0.0, Math.Min(0.99, etaOp));

            double acPower = dcPower * etaOp;

            // Apply AC power clipping limit
            double maxACPower = config.RatedPower * config.ClippingRatio;
            if (acPower > maxACPower)
            {
                clippingLossWatts = acPower - maxACPower;
                acPower = maxACPower;
            }

            // Apply nighttime tare loss if output is very small
            if (acPower < config.NightTareLoss)
            {
                acPower = 0;
            }

            return Math.Max(0, acPower);
        }

        /// <summary>
        /// Aggregate DC to AC conversion for a string of modules.
        /// Calculates string voltage based on temperature and checks MPPT window.
        /// 
        /// FIXED 2026-05-03:
        /// - modulesPerString is now calculated once at initialization and kept fixed,
        ///   instead of being recalculated every hour based on operating temperature.
        ///   Real PV systems have fixed physical string configurations.
        /// - When modulesPerString=0 (auto mode), computes at STC conditions (25°C)
        ///   for stable sizing. Subsequent hours only check voltage window feasibility.
        /// 
        /// String voltage: V_string = N_modules * V_mp * [1 + gamma_v * (T_cell - 25)]
        /// where gamma_v ≈ -0.004 /°C (typical crystalline Si voltage temperature coefficient)
        /// </summary>
        /// <param name="totalDcPower">Total DC power from all modules [W]</param>
        /// <param name="avgCellTemp">Average cell temperature [°C]</param>
        /// <param name="config">Inverter configuration</param>
        /// <param name="panelCount">Number of PV panels/modules</param>
        /// <param name="clippingLossWatts">Output: clipping loss [W]</param>
        /// <param name="modulesPerString">Modules per string (0 = auto-calculate once at STC)</param>
        /// <param name="nominalVmp">Nominal Vmp per module at STC [V], default 35V</param>
        /// <param name="tempCoeffV">Voltage temperature coefficient [1/°C], default -0.004</param>
        /// <returns>AC power [W]</returns>
        public static double AggregateDCtoAC(
            double totalDcPower, double avgCellTemp,
            InverterConfig config, int panelCount,
            out double clippingLossWatts,
            ref int modulesPerString,  // FIX P5: ref instead of modifying config
            double nominalVmp = 35.0,
            double tempCoeffV = -0.004,
            double nominalVoc = 42.0,    // FIX P2: STC open-circuit voltage [V]
            double tempCoeffVoc = -0.003) // FIX P2: Voc temperature coefficient [1/°C]
        {
            clippingLossWatts = 0;

            if (!config.EnableInverterModel)
                return totalDcPower;

            if (totalDcPower <= 0)
                return 0;

            if (panelCount <= 0)
                return 0;

            // FIX P5: Auto-size strings at STC conditions (25°C) for stable, fixed configuration.
            // Real systems do not reconfigure strings hourly based on temperature.
            if (modulesPerString <= 0)
            {
                // Calculate at STC (25°C) for fixed string sizing
                double stcTempDeviation = 0.0; // 25°C - 25°C = 0
                double stcVoltageFactor = 1.0 + tempCoeffV * stcTempDeviation;

                // Target: string voltage near MPPT midpoint at STC
                double targetVoltage = (config.MPPTMinVoltage + config.MPPTMaxVoltage) / 2.0;
                modulesPerString = Math.Max(1, (int)(targetVoltage / (nominalVmp * stcVoltageFactor)));

                // FIX P5: Do NOT store back to config - use the ref parameter instead.
                // This prevents side effects when multiple components share the same config object.
            }

            // Calculate string voltage at operating temperature (Vmp-based for MPPT tracking)
            double tempDeviationFinal = avgCellTemp - 25.0;
            double voltageFactorFinal = 1.0 + tempCoeffV * tempDeviationFinal;
            double stringVoltageVmp = nominalVmp * modulesPerString * voltageFactorFinal;

            // FIX P2: Check open-circuit voltage for cold-start overvoltage protection.
            // At startup (before MPPT engages), the array is at open-circuit, so voltage = Voc.
            // If Voc exceeds the inverter's absolute maximum DC voltage, it will refuse to start.
            double vocFactorFinal = 1.0 + tempCoeffVoc * tempDeviationFinal;
            double stringVoltageVoc = nominalVoc * modulesPerString * vocFactorFinal;
            if (stringVoltageVoc > config.MPPTMaxVoltage + 100)  //Voc过压保护阈值默认提高100V，通常比MPPTMaxVoltage高一些以允许正常启动
            {
                return 0; // Overvoltage protection: Voc exceeds inverter maximum
            }

            // Check if Vmp is within MPPT window
            if (stringVoltageVmp < config.MPPTMinVoltage || stringVoltageVmp > config.MPPTMaxVoltage)
            {
                // FIX P5: Do NOT modify modulesPerString here. In real systems, string count
                // is physically fixed. If the voltage is out of range, the system simply stops.
                return 0; // Cannot operate within MPPT window with fixed string count
            }

            return CalculateACPower(totalDcPower, stringVoltageVmp, config, out clippingLossWatts);
        }
    }
}
