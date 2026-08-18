using System;
using Common.Core;
namespace ThermalComfort.Core
{
    /// <summary>
    /// Mean Radiant Temperature (MRT) calculation models for outdoor environments.
    /// 
    /// Implements two methods:
    /// 1. SolarCal model (ASHRAE Standard 55) - recommended for engineering applications
    /// 2. RayMan model (view factor approach) - more detailed for research applications
    /// 
    /// 4PI CORRECTED (2026-08-18): all view factors (SVF/GVF/OVF/TVF/TRVF) are
    /// FULL-SPHERE normalized (open sky F_sky = 0.5, five components sum to 1).
    /// Under this convention the hemisphere share of the human body is already
    /// embedded in the view factors, so NO extra 0.5 factor may be applied to
    /// diffuse or ground-reflected shortwave absorption (ASHRAE 55 App C writes
    /// 0.5*f_svv because f_svv is hemisphere-normalized; F_sky(4pi) = f_svv/2).
    /// Sky temperature is derived from EPW horizontal infrared as an equivalent
    /// blackbody value (measured downwelling irradiance already contains the
    /// sky emissivity - it must NOT be divided by epsilon_sky again).
    ///
    /// References:
    /// [1] ASHRAE Standard 55-2017: Thermal Environmental Conditions for Human Occupancy.
    /// [2] Arens, E., et al. (2015). Modeling the comfort effects of short-wave solar radiation indoors. 
    ///     Building and Environment, 88, 3-9.
    /// [3] Matzarakis, A., Rutz, F., & Mayer, H. (2010).
    ///     Modelling radiation fluxes in simple and complex environments: basics of the RayMan model.
    ///     International Journal of Biometeorology, 54, 131-139.
    /// [4] ISO 7726:1998. Ergonomics of the thermal environment - Instruments for measuring physical quantities.
    /// [5] Thorsson, S., et al. (2007). Different methods for estimating the mean radiant temperature
    ///     in an outdoor urban setting. Int. J. Climatology, 27, 1983-1993.
    /// </summary>
    public static class MRTModel
    {
        #region SolarCal Model (ASHRAE Standard 55)

        /// <summary>
        /// Calculate Mean Radiant Temperature using the SolarCal model (ASHRAE Standard 55).
        ///
        /// PHYSICALLY CORRECTED (2026-05-15):
        /// Longwave radiation is now decomposed into THREE components based on full-spherical
        /// (4pi) view factors: sky (SVF), ground (GVF), and obstacles (OVF).
        ///
        /// ENHANCED (2026-06-14): Fine-grained DNI transmission through vegetation
        /// and translucent materials. The dniExposureFactor replaces exposureFactor
        /// in direct radiation calculations, supporting Beer-Lambert canopy transmission
        /// and translucent material transmittance.
        ///
        /// Conservation: SVF + GVF + OVF = 1.0
        ///
        /// Longwave correction:
        ///   deltaT_lw = coeff * [SVF * (T_sky - T_ref) + GVF * (T_ground - T_ref)
        ///                        + OVF * (T_surf - T_ref)]
        ///
        /// Where T_surf = SurroundingSurfaceTemperature ?? airTemp
        /// (temperature of obstacle surfaces).
        /// </summary>
        /// <param name="dniExposureFactor">Effective DNI exposure factor [0-1], accounts for
        /// transmission through vegetation (Beer-Lambert) and translucent materials.</param>
        /// <summary>
        /// ENHANCED (2026-06-16): Precise diffuse radiation with decomposed view factors.
        /// DHI_eff = SVF*DHI + TVF*DHI*exp(-k_c*LAD*l) + TRVF*DHI*tau
        /// </summary>
        public static double CalculateMRT_SolarCal(
            double airTemp,
            double directNormalIrradiance,
            double diffuseHorizontalIrradiance,
            double globalHorizontalIrradiance,
            double horizontalInfrared,
            double skyViewFactor,
            double groundViewFactor,
            double obstacleViewFactor,
            double treeViewFactor,
            double translucentViewFactor,
            double dniExposureFactor,
            double solarAltitude,
            MRTConfig config,
            ObstacleSet obstacleSet = null,
            double obstacleTemp = double.NaN,
            double treeCanopyTemp = double.NaN,
            double translucentTemp = double.NaN,
            double canopyThicknessOverride = double.NaN)
        {
            double groundTemp = config.GroundTemperature ?? airTemp;
            if (double.IsNaN(obstacleTemp)) obstacleTemp = airTemp;
            if (double.IsNaN(treeCanopyTemp)) treeCanopyTemp = airTemp;
            if (double.IsNaN(translucentTemp)) translucentTemp = airTemp;
            double refTemp = airTemp;
            const double stefanBoltzmann = 5.67e-8;

            double projectionFactor = GetProjectionFactor(solarAltitude);
            double bodySolarFlux = 0.0;

            if (config.IncludeShortwave)
            {
                // ENHANCED (2026-06-14): dniExposureFactor accounts for transmission
                // through vegetation (Beer-Lambert) and translucent materials
                double dni = CleanIrradiance(directNormalIrradiance);
                double directComponent = dniExposureFactor * projectionFactor * dni;

                // ENHANCED (2026-06-16): Decomposed diffuse radiation
                // DHI_eff = SVF*DHI + TVF*DHI*exp(-k*LAD*l) + TRVF*DHI*tau
                double canopyThickness = CanopyThicknessFor(obstacleSet, canopyThicknessOverride);
                double kcLAD = obstacleSet != null
                    ? obstacleSet.ExtinctionCoefficient * obstacleSet.LeafAreaDensity : 0.0;
                double treeTransmittance = Math.Exp(-kcLAD * canopyThickness);
                double tau = obstacleSet != null ? obstacleSet.TranslucentTransmittance : 0.0;

                double dhi = CleanIrradiance(diffuseHorizontalIrradiance);
                double ghi = CleanIrradiance(globalHorizontalIrradiance);

                // 4PI CORRECTED (2026-08-18): view factors are full-sphere normalized,
                // the hemisphere share is already inside F_sky/GVF. The legacy 0.5
                // factors double-counted it and halved diffuse + reflected absorption:
                //   diffuse   = f_eff * DHI * (F_sky + TVF*tau_t + TRVF*tau)
                //   reflected = f_eff * GVF * rho * GHI
                double effectiveDiffuse = dhi * (
                    skyViewFactor
                    + treeViewFactor * treeTransmittance
                    + translucentViewFactor * tau);

                double diffuseComponent = config.PostureFactor * effectiveDiffuse;
                double reflectedComponent = config.PostureFactor * groundViewFactor
                    * config.FloorReflectance * ghi;

                bodySolarFlux = directComponent + diffuseComponent + reflectedComponent;
            }

            double erfShortwave = bodySolarFlux * (config.BodyAbsorptivity / config.BodyEmissivity);
            double deltaT_sw = erfShortwave / (config.PostureFactor * config.RadiativeHeatTransferCoeff);

            double deltaT_lw = 0.0;
            if (config.IncludeLongwave)
            {
                // 4PI CORRECTED (2026-08-18): EPW horizontal infrared is the MEASURED
                // hemispheric downwelling longwave irradiance; its equivalent blackbody
                // temperature is (I_hor/sigma)^0.25 WITHOUT dividing by epsilon_sky.
                // Missing/invalid IR falls back to a clear-sky estimate (Ta - 15 K),
                // result clamped to [200, 340] K.
                double skyTempKelvin = Math.Max(200.0, Math.Min(340.0,
                    Math.Pow(ResolveDownwellingIR(horizontalInfrared, airTemp) / stefanBoltzmann, 0.25)));
                double skyTemp = skyTempKelvin - 273.15;

                // 4PI CORRECTED (2026-08-18): first-order linearization coefficient
                // of T_mrt^4 = SUM(F_i * T_i^4) is 1.0 (not 0.5).
                double lwCoeff = config.LongwaveLinearCoeff > 0 ? config.LongwaveLinearCoeff : 1.0;

                // FIVE-COMPONENT longwave decomposition
                deltaT_lw = lwCoeff * (
                    skyViewFactor * (skyTemp - refTemp) +
                    groundViewFactor * (groundTemp - refTemp) +
                    obstacleViewFactor * (obstacleTemp - refTemp) +
                    treeViewFactor * (treeCanopyTemp - refTemp) +
                    translucentViewFactor * (translucentTemp - refTemp));
            }

            return ClampMRT(airTemp + deltaT_sw + deltaT_lw);
        }

        #endregion

        #region RayMan Model (Alternative)

        /// <summary>
        /// Calculate Mean Radiant Temperature using the RayMan model.
        /// 
        /// PHYSICALLY CORRECTED (2026-05-15):
        /// Longwave radiation is now decomposed into THREE components based on full-spherical
        /// (4π) view factors: sky (SVF), ground (GVF), and obstacles (OVF).
        /// 
        /// ENHANCED (2026-06-14): Fine-grained DNI transmission through vegetation
        /// and translucent materials. The dniExposureFactor replaces exposureFactor
        /// in direct radiation calculations.
        ///
        /// Conservation: SVF + GVF + OVF = 1.0
        /// 
        /// The old incorrect formula groundVF = 0.5 * (1 - SVF) has been replaced.
        /// 
        /// EMissIVITY UPDATE (2026-05-20):
        /// Ground and obstacle emissivities are now user-configurable via MRT Settings
        /// (GroundEmissivity and ObstacleEmissivity). Defaults to 0.95.
        /// </summary>
        /// <param name="dniExposureFactor">Effective DNI exposure factor [0-1], accounts for
        /// transmission through vegetation (Beer-Lambert) and translucent materials.</param>
        /// <summary>
        /// ENHANCED (2026-06-16): Precise diffuse radiation with decomposed view factors.
        /// DHI_eff = SVF*DHI + TVF*DHI*exp(-k_c*LAD*l) + TRVF*DHI*tau
        /// </summary>
        public static double CalculateMRT_RayMan(
            double airTemp,
            double directNormalIrradiance,
            double diffuseHorizontalIrradiance,
            double globalHorizontalIrradiance,
            double horizontalInfrared,
            double skyViewFactor,
            double groundViewFactor,
            double obstacleViewFactor,
            double treeViewFactor,
            double translucentViewFactor,
            double dniExposureFactor,
            double solarAltitude,
            MRTConfig config,
            ObstacleSet obstacleSet = null,
            double obstacleTemp = double.NaN,
            double treeCanopyTemp = double.NaN,
            double translucentTemp = double.NaN,
            double canopyThicknessOverride = double.NaN)
        {
            const double stefanBoltzmann = 5.67e-8;
            double absorptivity = config.BodyAbsorptivity;
            double emissivity = config.BodyEmissivity;

            // Temperatures in Kelvin
            double groundTempK = (config.GroundTemperature ?? airTemp) + 273.15;
            if (double.IsNaN(obstacleTemp)) obstacleTemp = airTemp;
            if (double.IsNaN(treeCanopyTemp)) treeCanopyTemp = airTemp;
            if (double.IsNaN(translucentTemp)) translucentTemp = airTemp;
            double obstacleTempK = obstacleTemp + 273.15;
            double treeCanopyTempK = treeCanopyTemp + 273.15;
            double translucentTempK = translucentTemp + 273.15;

            // 4PI CORRECTED (2026-08-18): the sky slot uses the MEASURED EPW horizontal
            // infrared directly (equivalent blackbody downwelling irradiance). The old
            // inversion (I_hor/eps_sky)^0.25 re-emitted with eps_body inflated the sky
            // longwave by eps_body/eps_sky ~ 1.1-1.27. Missing/invalid IR falls back
            // to a clear-sky estimate (Ta - 15 K).
            double skyLongwave = ResolveDownwellingIR(horizontalInfrared, airTemp);
            double groundEps = config.GroundEmissivity > 0 ? config.GroundEmissivity : 0.95;
            double obstacleEps = config.ObstacleEmissivity > 0 ? config.ObstacleEmissivity : 0.95;
            double groundLongwave = groundEps * stefanBoltzmann * Math.Pow(groundTempK, 4);
            double obstacleLongwave = obstacleEps * stefanBoltzmann * Math.Pow(obstacleTempK, 4);

            // FIVE-COMPONENT longwave decomposition (ISO 7726 / Thorsson 2007):
            // sigma*T_mrt^4 = SUM(F_i * eps_i * sigma * T_i^4) + (alpha_sw/eps_lw)*E_sw/sigma
            double treeLongwave = obstacleEps * stefanBoltzmann * Math.Pow(treeCanopyTempK, 4);
            double translucentLongwave = obstacleEps * stefanBoltzmann * Math.Pow(translucentTempK, 4);
            double mrtK4 = (1.0 / stefanBoltzmann) * (
                skyLongwave * skyViewFactor +
                groundLongwave * groundViewFactor +
                obstacleLongwave * obstacleViewFactor +
                treeLongwave * treeViewFactor +
                translucentLongwave * translucentViewFactor);

            if (config.IncludeShortwave)
            {
                // 4PI CORRECTED (2026-08-18): shortwave enters mrtK4 EXACTLY ONCE with
                // no nested view factor multiplication and no 0.5 hemisphere factor
                // (F_sky is full-sphere normalized, open sky = 0.5):
                //   E_sw = DHI*(F_sky + TVF*tau_t + TRVF*tau) + rho*GHI*GVF + f_p*chi*DNI
                // Tree/translucent diffuse contributions are weighted by their OWN
                // view factors (previously mis-slotted into the sky bucket).
                double canopyThickness = CanopyThicknessFor(obstacleSet, canopyThicknessOverride);
                double kcLAD = obstacleSet != null
                    ? obstacleSet.ExtinctionCoefficient * obstacleSet.LeafAreaDensity : 0.0;
                double treeTransmittance = Math.Exp(-kcLAD * canopyThickness);
                double tau = obstacleSet != null ? obstacleSet.TranslucentTransmittance : 0.0;

                double dhi = CleanIrradiance(diffuseHorizontalIrradiance);
                double ghi = CleanIrradiance(globalHorizontalIrradiance);
                double dni = CleanIrradiance(directNormalIrradiance);

                double effectiveDiffuse = dhi * (
                    skyViewFactor
                    + treeViewFactor * treeTransmittance
                    + translucentViewFactor * tau);
                double reflectedShortwave = ghi * config.FloorReflectance * groundViewFactor;
                double projectionFactor = GetProjectionFactor(solarAltitude);
                double directSolar = dniExposureFactor * projectionFactor * dni;

                double shortWaveFlux = effectiveDiffuse + reflectedShortwave + directSolar;
                mrtK4 += absorptivity * shortWaveFlux / (emissivity * stefanBoltzmann);
            }

            return ClampMRT(Math.Pow(Math.Max(0.0, mrtK4), 0.25) - 273.15);
        }

        #endregion

        #region Unified Entry Point

        /// <summary>
        /// Unified MRT calculation entry point.
        /// 
        /// PHYSICALLY CORRECTED (2026-05-15):
        /// Now requires three view factors (SVF, GVF, OVF) from full-spherical sampling.
        /// The old two-parameter (SVF only) API is no longer supported.
        ///
        /// ENHANCED (2026-06-14):
        /// Added dniExposureFactor parameter for fine-grained direct radiation transmission
        /// through vegetation (Beer-Lambert law) and translucent materials.
        /// When obstacleSet is not used (legacy mode), set dniExposureFactor = exposureFactor.
        /// </summary>
        /// <param name="dniExposureFactor">Effective DNI exposure factor [0-1]. In legacy mode
        /// (no ObstacleSet), this equals exposureFactor. With ObstacleSet, it accounts for
        /// partial transmission through trees and translucent sunshades.</param>
        /// <summary>
        /// Unified MRT calculation entry point.
        ///
        /// ENHANCED (2026-06-16): Added treeViewFactor and translucentViewFactor for
        /// precise diffuse radiation calculation with decomposed view factors.
        /// DHI_eff = SVF*DHI + TVF*DHI*exp(-k_c*LAD*l) + TRVF*DHI*tau
        /// </summary>
        public static double CalculateMRT(
            double airTemp,
            double directNormalIrradiance,
            double diffuseHorizontalIrradiance,
            double globalHorizontalIrradiance,
            double horizontalInfrared,
            double skyViewFactor,
            double groundViewFactor,
            double obstacleViewFactor,
            double treeViewFactor,
            double translucentViewFactor,
            double dniExposureFactor,
            double solarAltitude,
            MRTConfig config,
            bool useRayMan = false,
            ObstacleSet obstacleSet = null,
            double obstacleTemp = double.NaN,
            double treeCanopyTemp = double.NaN,
            double translucentTemp = double.NaN,
            double canopyThicknessOverride = double.NaN)
        {
            if (useRayMan)
            {
                return CalculateMRT_RayMan(
                    airTemp, directNormalIrradiance, diffuseHorizontalIrradiance,
                    globalHorizontalIrradiance, horizontalInfrared,
                    skyViewFactor, groundViewFactor, obstacleViewFactor,
                    treeViewFactor, translucentViewFactor,
                    dniExposureFactor, solarAltitude, config, obstacleSet,
                    obstacleTemp, treeCanopyTemp, translucentTemp,
                    canopyThicknessOverride);
            }
            else
            {
                return CalculateMRT_SolarCal(
                    airTemp, directNormalIrradiance, diffuseHorizontalIrradiance,
                    globalHorizontalIrradiance, horizontalInfrared,
                    skyViewFactor, groundViewFactor, obstacleViewFactor,
                    treeViewFactor, translucentViewFactor,
                    dniExposureFactor, solarAltitude, config, obstacleSet,
                    obstacleTemp, treeCanopyTemp, translucentTemp,
                    canopyThicknessOverride);
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Solar projection factor f_dir for direct radiation on the human body.
        /// Based on solar altitude angle only (RayMan Eq. 7).
        /// NOTE: Projection factor and posture efficiency factor are INDEPENDENT.
        /// Posture factor f (standing=0.725, sitting=0.696) is applied separately.
        /// </summary>
        public static double GetProjectionFactor(double solarAltitudeDeg)
        {
            double gamma = Math.Max(0.0, Math.Min(90.0, solarAltitudeDeg));
            double gammaRad = gamma * Math.PI / 180.0;
            double fp = 0.308 * Math.Cos(gammaRad) * (0.998 - gamma * gamma / 50000.0);
            return Math.Max(0.0, Math.Min(0.308, fp));
        }

        public static double SolarAltitudeFromZenith(double zenithAngleDeg)
        {
            return Math.Max(0.0, 90.0 - zenithAngleDeg);
        }

        /// <summary>
        /// Calculate equivalent blackbody sky temperature from horizontal infrared radiation.
        /// 4PI CORRECTED (2026-08-18): EPW horizontal infrared is the measured hemispheric
        /// downwelling longwave irradiance, so the equivalent blackbody temperature is
        /// (I_hor/sigma)^0.25. The skyEmissivity parameter is kept for API compatibility
        /// but is NOT used in the inversion (dividing by epsilon_sky inflated the sky
        /// temperature; the emissivity effect is already contained in the measurement).
        /// Invalid/missing IR (NaN, Inf, <= 0) falls back to a moderate clear-sky
        /// estimate of 288 K; result clamped to [200, 340] K.
        /// </summary>
        public static double CalculateSkyTemperature(double horizontalInfrared, double skyEmissivity = 1.0)
        {
            const double stefanBoltzmann = 5.67e-8;
            double tSkyK;
            if (double.IsNaN(horizontalInfrared) || double.IsInfinity(horizontalInfrared)
                || horizontalInfrared <= 0)
            {
                tSkyK = 288.0;
            }
            else
            {
                tSkyK = Math.Pow(horizontalInfrared / stefanBoltzmann, 0.25);
            }
            return Math.Max(200.0, Math.Min(340.0, tSkyK)) - 273.15;
        }

        // ====================================================================
        // 4PI CORRECTED (2026-08-18): numerical hardening helpers
        // ====================================================================

        /// <summary>
        /// Resolve canopy characteristic thickness [m] (P-8 FIX).
        /// Uses the caller-computed override (computed once per simulation in
        /// OutdoorMRT, outside the 8760 h loop) when provided; otherwise falls
        /// back to computing it from the obstacle set (legacy per-call path,
        /// kept for API compatibility with older callers).
        /// </summary>
        private static double CanopyThicknessFor(ObstacleSet obstacleSet, double overrideValue)
        {
            if (!double.IsNaN(overrideValue)) return Math.Max(0.0, overrideValue);
            return obstacleSet != null
                ? HumanExposureModel.CalculateCanopyCharacteristicThickness(obstacleSet.TreeCanopyMeshes)
                : 0.0;
        }

        /// <summary>
        /// Sanitize an irradiance input: NaN/Infinity -> 0, negative -> 0.
        /// Applied at the entry of every shortwave term (DNI/DHI/GHI).
        /// </summary>
        private static double CleanIrradiance(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0.0;
            return v < 0.0 ? 0.0 : v;
        }

        /// <summary>
        /// Resolve the downwelling horizontal longwave irradiance [W/m2].
        /// Invalid/missing values (NaN, Inf, <= 0) fall back to a clear-sky
        /// estimate sigma*(Ta - 15 K)^4 based on air temperature.
        /// </summary>
        private static double ResolveDownwellingIR(double horizontalInfrared, double airTempC)
        {
            const double stefanBoltzmann = 5.67e-8;
            if (double.IsNaN(horizontalInfrared) || double.IsInfinity(horizontalInfrared)
                || horizontalInfrared <= 0)
            {
                return stefanBoltzmann * Math.Pow(airTempC + 273.15 - 15.0, 4.0);
            }
            return horizontalInfrared;
        }

        /// <summary>
        /// Clamp the final MRT to a physically admissible range [-60, 90] C.
        /// NaN/Infinity (impossible after entry sanitation) fall back to 15 C.
        /// </summary>
        private static double ClampMRT(double mrtC)
        {
            if (double.IsNaN(mrtC) || double.IsInfinity(mrtC)) return 15.0;
            if (mrtC < -60.0) return -60.0;
            if (mrtC > 90.0) return 90.0;
            return mrtC;
        }

        #endregion
    }
}
