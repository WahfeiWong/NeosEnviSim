using System;
using Common.Core;
namespace SolarPV.Core
{
    /// <summary>
    /// Perez Anisotropic Sky Model for diffuse irradiance on tilted surfaces.
    /// 
    /// Based on:
    /// [1] Perez, R., Seals, R., Ineichen, P., Stewart, R., & Menicucci, D. (1987). 
    ///     A new simplified version of the Perez diffuse irradiance model for tilted surfaces. 
    ///     Solar Energy, 39(3), 221-231.
    /// [2] Perez, R., Ineichen, P., Seals, R., Michalsky, J., & Stewart, R. (1990).
    ///     Modeling daylight availability and irradiance components from direct and global irradiance.
    ///     Solar Energy, 44(5), 271-289.
    /// 
    /// This implementation follows the 1990 formulation with the 8-bin epsilon coefficient table.
    /// </summary>
    public static class PerezSkyModel
    {
        /// <summary>
        /// Calculate diffuse irradiance on a tilted surface using the Perez anisotropic sky model.
        /// 
        /// Formula (from Perez et al., 1990):
        /// D_tilted = DHI * [ (1 - F1) * (1 + cos β) / 2 + F1 * a / b + F2 * sin β ]
        /// 
        /// Where:
        /// - DHI = diffuse horizontal irradiance [W/m²]
        /// - β = surface tilt angle [rad]
        /// - F1, F2 = circumsolar and horizon/horizon brightening coefficients
        /// - a = max(0, cos θ) where θ = angle of incidence [rad]
        /// - b = max(cos(85°), cos θz) where θz = zenith angle [rad]
        /// </summary>
        /// <param name="dhi">Diffuse horizontal irradiance [W/m²]</param>
        /// <param name="dni">Direct normal irradiance [W/m²]</param>
        /// <param name="ghi">Global horizontal irradiance [W/m²]</param>
        /// <param name="zenithDeg">Solar zenith angle [degrees], 0°=overhead, 90°=horizon</param>
        /// <param name="tiltRad">Surface tilt from horizontal [radians], 0=horizontal, π/2=vertical</param>
        /// <param name="incidenceRad">Angle of incidence [radians]</param>
        /// <returns>Diffuse irradiance on tilted surface [W/m²]</returns>
        public static double CalculateDiffuseTilted(
        double dhi, double dni, double ghi,
        double zenithDeg, double tiltRad, double incidenceRad,
        SkyModelConfig config = null)
        {
            // 提取配置参数（如未传入则使用默认值）
            double horizonCoeff = 1.0;
            double circumsolarCoeff = 1.0;
            double ddThreshold = 0.01;

            if (config != null)
            {
                horizonCoeff = Math.Max(0.0, config.HorizonBrighteningCoeff);
                circumsolarCoeff = Math.Max(0.0, config.CircumsolarBrighteningCoeff);
                ddThreshold = Math.Max(0.0, Math.Min(1.0, config.DirectDiffuseRatioThreshold));
            }

            // 修复：使用 DirectDiffuseRatioThreshold 判断数值稳定性
            // 当散射比例极低时，Perez模型易数值不稳定，回退到各向同性模型
            if (dhi <= 0 || zenithDeg >= 90.0 || (ghi > 0 && dhi / ghi < ddThreshold))
                return dhi * (1.0 + Math.Cos(tiltRad)) / 2.0;

            double zenithRad = zenithDeg * Math.PI / 180.0;
            double cosZenith = Math.Cos(zenithRad);

            // Prevent division by very small cos(zenith) near horizon
            if (cosZenith < 0.01) cosZenith = 0.01;

            // Calculate epsilon (sky's brightness parameter)
            // epsilon = [(DHI + DNI) / DHI + 5.535e-6 * θz³] / [1 + 5.535e-6 * θz³]
            // where θz is in degrees
            // 
            // From Perez et al. (1990), Eq. 1:
            // When DHI → 0, epsilon → 1 (overcast sky limit)
            double epsilon;
            double zenithDegCubed = Math.Pow(zenithDeg, 3);
            double epsilonCorrection = 5.535e-6 * zenithDegCubed;

            if (dhi > 0.001)
                epsilon = ((dhi + dni) / dhi + epsilonCorrection) / (1.0 + epsilonCorrection);
            else
                epsilon = 1e6; // 或根据DNI/GHI推断晴天

            // Clamp epsilon to valid range of coefficient table [1, >8]
            epsilon = Math.Max(1.0, epsilon);

            // Calculate delta (atmospheric clarity parameter)
            // delta = DHI * m / I0, where m = air mass = 1/cos(θz), I0 = 1367 W/m²
            double airMass = 1.0 / cosZenith;
            double delta = dhi * airMass / 1367.0;
            delta = Math.Max(0.0, delta); // Ensure non-negative

            // Get Perez coefficients F1 and F2 based on epsilon bin
            GetCoefficients(epsilon, out double f11, out double f12, out double f13,
                           out double f21, out double f22, out double f23);

            // Calculate F1 (circumsolar brightening coefficient)
            // F1 = max(0, f11 + f12 * delta + f13 * zenithRad)
            double F1 = f11 + f12 * delta + f13 * zenithRad;
            F1 = Math.Max(0.0, F1) * circumsolarCoeff;

            // Calculate F2 (horizon brightening coefficient)
            double F2 = f21 + f22 * delta + f23 * zenithRad;
            F2 = Math.Max(0.0, F2) * horizonCoeff;

            // Calculate geometric terms
            double cosTilt = Math.Cos(tiltRad);
            double sinTilt = Math.Sin(tiltRad);
            double cosIncidence = Math.Cos(incidenceRad);

            // a = max(0, cos θ) - projection factor for circumsolar region
            double a = Math.Max(0.0, cosIncidence);

            // b = max(cos(85°), cos θz) - prevents division by near-zero at horizon
            double b = Math.Max(Math.Cos(85.0 * Math.PI / 180.0), cosZenith);

            // Perez model main equation (Perez et al., 1990, Eq. 2):
            // D_tilted = DHI * [ (1 - F1) * (1 + cos β)/2 + F1 * a/b + F2 * sin β ]

            double isotropicComponent = (1.0 - F1) * (1.0 + cosTilt) / 2.0;
            double circumsolarComponent = F1 * a / b;
            double horizonComponent = F2 * sinTilt;

            double diffuseTilted = dhi * (isotropicComponent + circumsolarComponent + horizonComponent);

            // Ensure non-negative result
            return Math.Max(0.0, diffuseTilted);
        }

        /// <summary>
        /// Get Perez model coefficients for a given epsilon bin.
        /// 
        /// Coefficient table from Perez et al. (1990), Table 1.
        /// 8 epsilon bins covering sky conditions from overcast to very clear.
        /// </summary>
        /// <param name="epsilon">Sky brightness parameter</param>
        /// <param name="f11">F1 coefficient constant term</param>
        /// <param name="f12">F1 coefficient delta term</param>
        /// <param name="f13">F1 coefficient zenith term</param>
        /// <param name="f21">F2 coefficient constant term</param>
        /// <param name="f22">F2 coefficient delta term</param>
        /// <param name="f23">F2 coefficient zenith term</param>
        private static void GetCoefficients(double epsilon,
            out double f11, out double f12, out double f13,
            out double f21, out double f22, out double f23)
        {
            // Epsilon bins and corresponding coefficients
            // Bin 1: Overcast (ε < 1.065)
            // Bin 2: Overcast/Partly Cloudy (1.065 ≤ ε < 1.230)
            // Bin 3: Partly Cloudy (1.230 ≤ ε < 1.500)
            // Bin 4: Partly Cloudy (1.500 ≤ ε < 1.950)
            // Bin 5: Clear/Partly Cloudy (1.950 ≤ ε < 2.800)
            // Bin 6: Clear (2.800 ≤ ε < 4.500)
            // Bin 7: Very Clear (4.500 ≤ ε < 6.200)
            // Bin 8: Extremely Clear (ε ≥ 6.200)
            if (epsilon < 1.065)
            {
                f11 = -0.008; f12 = 0.588; f13 = -0.062;
                f21 = -0.060; f22 = 0.072; f23 = -0.022;
            }
            else if (epsilon < 1.230)
            {
                f11 = 0.130; f12 = 0.683; f13 = -0.151;
                f21 = -0.019; f22 = 0.066; f23 = -0.029;
            }
            else if (epsilon < 1.500)
            {
                f11 = 0.330; f12 = 0.487; f13 = -0.221;
                f21 = 0.055; f22 = -0.064; f23 = -0.026;
            }
            else if (epsilon < 1.950)
            {
                f11 = 0.568; f12 = 0.187; f13 = -0.295;
                f21 = 0.109; f22 = -0.152; f23 = -0.014;
            }
            else if (epsilon < 2.800)
            {
                f11 = 0.873; f12 = -0.392; f13 = -0.362;
                f21 = 0.226; f22 = -0.462; f23 = 0.001;
            }
            else if (epsilon < 4.500)
            {
                f11 = 1.132; f12 = -1.237; f13 = -0.412;
                f21 = 0.288; f22 = -0.823; f23 = 0.056;
            }
            else if (epsilon < 6.200)
            {
                f11 = 1.060; f12 = -1.600; f13 = -0.359;
                f21 = 0.264; f22 = -1.127; f23 = 0.131;
            }
            else
            {
                // epsilon >= 6.200 - extremely clear sky
                f11 = 0.678; f12 = -0.327; f13 = -0.250;
                f21 = 0.156; f22 = -1.377; f23 = 0.251;
            }
        }
    }
}
