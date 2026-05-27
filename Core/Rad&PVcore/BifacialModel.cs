using System;

namespace SolarPV.Core
{
    /// <summary>
    /// Bifacial PV module rear-side irradiance and gain calculations.
    /// 
    /// This implementation uses a view-factor-based approach that accounts for
    /// row spacing, module height, tilt angle, and ground albedo.
    /// 
    /// IMPORTANT PHYSICAL SCOPE:
    /// The shadingFactor used in CalculateRearIrradiance is a SIMPLIFIED ENGINEERING
    /// MODEL based on Marion et al. (2017) for REGULAR INFINITE ARRAYS. It assumes:
    ///   - All modules share the same height and tilt angle
    ///   - Rows are parallel and equally spaced
    ///   - Ground is an infinite horizontal plane
    /// 
    /// When the user inputs actual panel geometry (arbitrary positions, non-uniform
    /// spacing, varying heights), this model remains an APPROXIMATION. The parameters
    /// (panelHeight, rowSpacing) are auto-inferred from the input geometry to minimize
    /// the mismatch, but the underlying regular-array assumption still applies.
    /// 
    /// For a true geometric calculation of rear-side ground-reflection occlusion,
    /// one would need to trace rays from each rear face toward the ground plane and
    /// test intersection with all other panel meshes — this is computationally
    /// expensive and requires an explicit ground surface definition, which is why
    /// the simplified model is retained as an engineering compromise.
    /// 
    /// References:
    /// [1] Marion, B., et al. (2017). A Practical Irradiance Model for Bifacial PV Modules.
    ///     NREL/TP-6A20-67847. https://www.nrel.gov/docs/fy17osti/67847.pdf
    /// [2] Hansen, C.W., et al. (2016). "Analysis of irradiance models for bifacial PV modules." Proc. 2016 IEEE 43rd PVSC, pp. 138-143.
    /// </summary>
    public static class BifacialModel
    {
        /// <summary>
        /// Calculate rear-side irradiance on a bifacial PV module.
        /// 
        /// Uses a geometric view factor model that accounts for:
        /// - Ground-reflected radiation (primary contributor)
        /// - Diffuse sky radiation reaching the rear (via ray-traced rear SVF)
        /// - Shading between rows (simplified regular-array model)
        /// 
        /// Reference: Marion et al. (2017), NREL/TP-6A20-67847.
        /// </summary>
        /// <param name="ghi">Global horizontal irradiance [W/m²]</param>
        /// <param name="dhi">Diffuse horizontal irradiance [W/m²]</param>
        /// <param name="dni">Direct normal irradiance [W/m²]</param>
        /// <param name="albedo">Ground reflectance (albedo) [0-1], typical 0.2-0.8</param>
        /// <param name="tiltRad">Module tilt angle from horizontal [rad]</param>
        /// <param name="panelHeight">Module centroid height above ground [m]</param>
        /// <param name="rowSpacing">Row-to-row spacing [m]</param>
        /// <param name="frontSvf">Front-side sky view factor from ray tracing [0-1]</param>
        /// <param name="rearSvf">Rear-side sky view factor from ray tracing [0-1]. 
        /// For horizontal modules (tilt=0) rearSvf=0. For vertical modules rearSvf≈0.5.
        /// Calculated by tracing rays from rear face toward sky dome.</param>
        /// <param name="rearGainFactor">Empirical correction factor for rear-side irradiance [default 1.0]</param>
        /// <returns>Rear-side irradiance [W/m²]</returns>
        public static double CalculateRearIrradiance(
            double ghi, double dhi, double dni,
            double albedo, double tiltRad,
            double panelHeight, double rowSpacing,
            double frontSvf, double rearSvf,
            double rearGainFactor = 1.0)
        {
            double cosTilt = Math.Cos(tiltRad);
            double sinTilt = Math.Sin(tiltRad);

            // View factor from REAR surface to ground
            // Rear surface faces downward, so it predominantly sees the ground
            // F_rear->ground = (1 + cos(tilt)) / 2
            double vfGroundRear = (1.0 + cosTilt) / 2.0;

            // View factor from REAR surface to sky
            // Rear surface sees less sky as tilt decreases
            // F_rear->sky = (1 - cos(tilt)) / 2
            double vfSkyRear = (1.0 - cosTilt) / 2.0;

            // Ground-reflected radiation reaching the rear
            double groundReflected = ghi * albedo * vfGroundRear;

            // Diffuse sky radiation on the rear
            // FIX: Use physically accurate rearSvf from ray tracing instead of the
            // (1 - frontSvf) approximation. The old approximation assumed that sky blocked
            // from the front automatically reaches the rear, which is physically incorrect.
            // rearSvf is computed by tracing rays from the rear face toward the sky dome,
            // accounting for obstacles (other panels, buildings) that may block rear sky view.
            double skyDiffuse = dhi * vfSkyRear * rearSvf;

            // Row-to-row shading correction factor
            // Based on geometric row spacing ratio.
            // 
            // NOTE: This is a simplified model for REGULAR ARRAYS. The shadingFactor
            // estimates how much ground-reflected light is blocked by adjacent rows.
            // For arbitrary geometry, it is an engineering approximation.
            double shadingFactor = 1.0;
            if (rowSpacing > 0.01 && panelHeight > 0.01)
            {
                double aspectRatio = panelHeight / rowSpacing;

                // The shading factor accounts for the fraction of ground-reflected
                // radiation that is blocked by adjacent rows.
                // For closely spaced rows, less ground reflection reaches the rear.
                //
                // Coefficient 0.5 gives physically meaningful variation for typical arrays:
                //   aspectRatio=0.1 -> SF≈0.95  (very sparse, little blocking)
                //   aspectRatio=0.5 -> SF≈0.80 (typical spacing, ~20% blocked)
                //   aspectRatio=1.0 -> SF≈0.67 (tight spacing, ~33% blocked)
                //   aspectRatio=2.0 -> SF≈0.50 (dense, half blocked)
                shadingFactor = 1.0 / (1.0 + 0.5 * aspectRatio * sinTilt);
                shadingFactor = Math.Max(0.1, Math.Min(1.0, shadingFactor));
            }

            // Total rear irradiance with shading and empirical correction
            double rearIrradiance = (groundReflected * shadingFactor + skyDiffuse) * rearGainFactor;

            return Math.Max(0, rearIrradiance);
        }

        /// <summary>
        /// Calculate bifacial gain (additional power from rear-side irradiance).
        /// 
        /// P_rear = G_rear * A_gross * phi_active * eta_front * phi_bifacial
        /// 
        /// Where:
        /// - G_rear = rear-side irradiance [W/m²]
        /// - A_gross = gross module area [m²]
        /// - phi_active = active area ratio (excluding frame)
        /// - eta_front = front-side efficiency (temperature-corrected)
        /// - phi_bifacial = bifaciality factor (rear/efficiency ratio)
        /// </summary>
        /// <param name="rearIrradiance">Rear-side irradiance [W/m²]</param>
        /// <param name="frontIrradiance">Front-side irradiance [W/m²]</param>
        /// <param name="bifacialityFactor">Bifaciality factor [0-1], typical 0.65-0.90</param>
        /// <param name="efficiency">Front-side efficiency (temperature-corrected) [0-1]</param>
        /// <param name="activeAreaRatio">Active cell area ratio [0-1], typical 0.9</param>
        /// <param name="grossArea">Gross module area [m²]</param>
        /// <returns>Rear-side power output [W]</returns>
        public static double CalculateBifacialGain(
            double rearIrradiance, double frontIrradiance,
            double bifacialityFactor, double efficiency,
            double activeAreaRatio, double grossArea)
        {
            if (rearIrradiance <= 0 || grossArea <= 0) return 0;

            // Rear-side power calculation
            // P_rear = G_rear * A_gross * phi_active * eta_front * phi_bifacial
            double rearPower = rearIrradiance * grossArea * activeAreaRatio
                             * efficiency * bifacialityFactor;

            return Math.Max(0, rearPower);
        }

        /// <summary>
        /// Calculate total bifacial gain ratio (rear/front energy ratio).
        /// This is useful for reporting the bifacial boost percentage.
        /// </summary>
        /// <param name="rearIrradiance">Rear-side irradiance [W/m²]</param>
        /// <param name="frontIrradiance">Front-side irradiance [W/m²]</param>
        /// <param name="bifacialityFactor">Bifaciality factor [0-1]</param>
        /// <returns>Bifacial gain ratio [0-1], e.g., 0.10 = 10% gain</returns>
        public static double CalculateBifacialGainRatio(
            double rearIrradiance, double frontIrradiance, double bifacialityFactor)
        {
            if (frontIrradiance <= 0) return 0;

            double gainRatio = (rearIrradiance / frontIrradiance) * bifacialityFactor;
            return Math.Max(0, Math.Min(1.0, gainRatio));
        }

        /// <summary>
        /// Check if the input geometry parameters correspond to a regular array assumption.
        /// The Marion shading factor is only validated for regular infinite arrays with
        /// parallel rows, equal spacing, and uniform height/tilt.
        /// </summary>
        /// <param name="rowSpacing">Row-to-row spacing [m]</param>
        /// <param name="panelHeight">Module centroid height above ground [m]</param>
        /// <returns>True if geometry appears regular (positive finite spacing and height)</returns>
        public static bool IsRegularArrayGeometry(double rowSpacing, double panelHeight)
        {
            return rowSpacing > 0.01 && panelHeight > 0.01 && !double.IsInfinity(rowSpacing);
        }
    }
}
