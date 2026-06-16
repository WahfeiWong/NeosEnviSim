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
    /// References:
    /// [1] ASHRAE Standard 55-2017: Thermal Environmental Conditions for Human Occupancy.
    /// [2] Arens, E., et al. (2015). Modeling the comfort effects of short-wave solar radiation indoors. 
    ///     Building and Environment, 88, 3-9.
    /// [3] Matzarakis, A., Rutz, F., & Mayer, H. (2010).
    ///     Modelling radiation fluxes in simple and complex environments: basics of the RayMan model.
    ///     International Journal of Biometeorology, 54, 131-139.
    /// [4] ISO 7726:1998. Ergonomics of the thermal environment - Instruments for measuring physical quantities.
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
            double translucentTemp = double.NaN)
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
                double directComponent = dniExposureFactor * projectionFactor * directNormalIrradiance;

                // ENHANCED (2026-06-16): Decomposed diffuse radiation
                // DHI_eff = SVF*DHI + TVF*DHI*exp(-k*LAD*l) + TRVF*DHI*tau
                double canopyThickness = obstacleSet != null
                    ? HumanExposureModel.CalculateCanopyCharacteristicThickness(obstacleSet.TreeCanopyMeshes)
                    : 0.0;
                double kcLAD = obstacleSet != null
                    ? obstacleSet.ExtinctionCoefficient * obstacleSet.LeafAreaDensity : 0.0;
                double treeTransmittance = Math.Exp(-kcLAD * canopyThickness);
                double tau = obstacleSet != null ? obstacleSet.TranslucentTransmittance : 0.0;

                double effectiveDiffuse = diffuseHorizontalIrradiance * (
                    skyViewFactor
                    + treeViewFactor * treeTransmittance
                    + translucentViewFactor * tau);

                double diffuseComponent = 0.5 * config.PostureFactor * effectiveDiffuse;
                double reflectedComponent = 0.5 * groundViewFactor * config.PostureFactor
                    * config.FloorReflectance * globalHorizontalIrradiance;

                bodySolarFlux = directComponent + diffuseComponent + reflectedComponent;
            }

            double erfShortwave = bodySolarFlux * (config.BodyAbsorptivity / config.BodyEmissivity);
            double deltaT_sw = erfShortwave / (config.PostureFactor * config.RadiativeHeatTransferCoeff);

            double deltaT_lw = 0.0;
            if (config.IncludeLongwave)
            {
                double skyEps = config.SkyEmissivity > 0 ? config.SkyEmissivity : 1.0;
                double skyTempKelvin = Math.Pow(
                    horizontalInfrared / (skyEps * stefanBoltzmann), 0.25);
                double skyTemp = skyTempKelvin - 273.15;

                double lwCoeff = config.LongwaveLinearCoeff > 0 ? config.LongwaveLinearCoeff : 0.5;

                // FIVE-COMPONENT longwave decomposition
                deltaT_lw = lwCoeff * (
                    skyViewFactor * (skyTemp - refTemp) +
                    groundViewFactor * (groundTemp - refTemp) +
                    obstacleViewFactor * (obstacleTemp - refTemp) +
                    treeViewFactor * (treeCanopyTemp - refTemp) +
                    translucentViewFactor * (translucentTemp - refTemp));
            }

            return airTemp + deltaT_sw + deltaT_lw;
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
            double translucentTemp = double.NaN)
        {
            const double stefanBoltzmann = 5.67e-8;
            double absorptivity = config.BodyAbsorptivity;
            double emissivity = config.BodyEmissivity;
            double skyEps = config.SkyEmissivity > 0 ? config.SkyEmissivity : 1.0;

            // Temperatures in Kelvin
            double groundTempK = (config.GroundTemperature ?? airTemp) + 273.15;
            if (double.IsNaN(obstacleTemp)) obstacleTemp = airTemp;
            if (double.IsNaN(treeCanopyTemp)) treeCanopyTemp = airTemp;
            if (double.IsNaN(translucentTemp)) translucentTemp = airTemp;
            double obstacleTempK = obstacleTemp + 273.15;
            double treeCanopyTempK = treeCanopyTemp + 273.15;
            double translucentTempK = translucentTemp + 273.15;

            double skyTempK = Math.Pow(horizontalInfrared / (skyEps * stefanBoltzmann), 0.25);
            double skyLongwave = emissivity * stefanBoltzmann * Math.Pow(skyTempK, 4);
            double groundEps = config.GroundEmissivity > 0 ? config.GroundEmissivity : 0.95;
            double obstacleEps = config.ObstacleEmissivity > 0 ? config.ObstacleEmissivity : 0.95;
            double groundLongwave = groundEps * stefanBoltzmann * Math.Pow(groundTempK, 4);
            double obstacleLongwave = obstacleEps * stefanBoltzmann * Math.Pow(obstacleTempK, 4);

            // ENHANCED (2026-06-16): Decompose effective diffuse irradiance
            double canopyThickness = obstacleSet != null
                ? HumanExposureModel.CalculateCanopyCharacteristicThickness(obstacleSet.TreeCanopyMeshes)
                : 0.0;
            double kcLAD = obstacleSet != null
                ? obstacleSet.ExtinctionCoefficient * obstacleSet.LeafAreaDensity : 0.0;
            double treeTransmittance = Math.Exp(-kcLAD * canopyThickness);
            double tau = obstacleSet != null ? obstacleSet.TranslucentTransmittance : 0.0;

            double effectiveDiffuse = diffuseHorizontalIrradiance * (
                skyViewFactor
                + treeViewFactor * treeTransmittance
                + translucentViewFactor * tau);

            // FIVE-COMPONENT longwave decomposition
            double treeLongwave = obstacleEps * stefanBoltzmann * Math.Pow(treeCanopyTempK, 4);
            double translucentLongwave = obstacleEps * stefanBoltzmann * Math.Pow(translucentTempK, 4);
            double mrtK4 = (1.0 / stefanBoltzmann) * (
                (skyLongwave + absorptivity * effectiveDiffuse / emissivity) * skyViewFactor +
                (groundLongwave + absorptivity * globalHorizontalIrradiance * config.FloorReflectance / emissivity)
                    * groundViewFactor +
                obstacleLongwave * obstacleViewFactor +
                treeLongwave * treeViewFactor +
                translucentLongwave * translucentViewFactor);

            if (directNormalIrradiance > 0 && dniExposureFactor > 0)
            {
                double projectionFactor = GetProjectionFactor(solarAltitude);
                // ENHANCED (2026-06-14): dniExposureFactor accounts for transmission
                // through vegetation (Beer-Lambert) and translucent materials
                double directSolar = dniExposureFactor * projectionFactor * directNormalIrradiance;
                mrtK4 += absorptivity * directSolar / (emissivity * stefanBoltzmann);
            }

            return Math.Pow(Math.Max(0, mrtK4), 0.25) - 273.15;
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
            double translucentTemp = double.NaN)
        {
            if (useRayMan)
            {
                return CalculateMRT_RayMan(
                    airTemp, directNormalIrradiance, diffuseHorizontalIrradiance,
                    globalHorizontalIrradiance, horizontalInfrared,
                    skyViewFactor, groundViewFactor, obstacleViewFactor,
                    treeViewFactor, translucentViewFactor,
                    dniExposureFactor, solarAltitude, config, obstacleSet,
                    obstacleTemp, treeCanopyTemp, translucentTemp);
            }
            else
            {
                return CalculateMRT_SolarCal(
                    airTemp, directNormalIrradiance, diffuseHorizontalIrradiance,
                    globalHorizontalIrradiance, horizontalInfrared,
                    skyViewFactor, groundViewFactor, obstacleViewFactor,
                    treeViewFactor, translucentViewFactor,
                    dniExposureFactor, solarAltitude, config, obstacleSet,
                    obstacleTemp, treeCanopyTemp, translucentTemp);
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
        /// Uses sky emissivity (NOT body emissivity). Default epsilon_sky=1.0 gives blackbody
        /// equivalent temperature; use Bergeron formula (approx 0.7-0.95) for physical sky temp.
        /// </summary>
        public static double CalculateSkyTemperature(double horizontalInfrared, double skyEmissivity = 1.0)
        {
            const double stefanBoltzmann = 5.67e-8;
            return Math.Pow(horizontalInfrared / (skyEmissivity * stefanBoltzmann), 0.25) - 273.15;
        }

        #endregion
    }
}
