using System;
using SolarPV.Core;
using System.Collections.Generic;
using System.Linq;
namespace Common.Core
{
    #region Soil Temperature and ET Configs

    /// <summary>
    /// Soil thermal property presets based on typical soil and surface types.
    /// Reference values from Campbell (1985) and engineering heat transfer literature.
    /// </summary>
    public enum SoilType
    {
        DrySand,
        WetSand,
        DryClay,
        WetClay,
        AsphaltConcrete,
        Custom
    }

    /// <summary>
    /// Latent heat flux calculation method selection.
    /// Simplified preserves original behavior. PenmanMonteith is the upgraded PM method.
    /// NoLatentHeat (2) shuts down LE completely for sensitivity tests.
    /// </summary>
    public enum LatentHeatMethod
    {
        /// <summary>Original simplified approach. LE = 0 when MoistureAvailability=0, or simple bulk formula.</summary>
        Simplified,
        /// <summary>Single-source Penman-Monteith for bare soil / uniform surface (FAO56 / ISBA).</summary>
        PenmanMonteith,
        /// <summary>Shut down latent heat flux completely (LE = 0, Beta = 0).</summary>
        NoLatentHeat = 2
    }

    /// <summary>
    /// Beta (moisture availability) calculation method.
    /// </summary>
    public enum BetaMethod
    {
        /// <summary>Noilhan & Planton ISBA: beta = wg / wsat.</summary>
        Noilhan,
        /// <summary>Direct user-specified constant beta.</summary>
        Direct,
        /// <summary>Kondo & Saigusa (1994) soil-texture-dependent parameterization.</summary>
        KondoSaigusa,
        /// <summary>Power law: beta = (wg/wsat)^bExp.</summary>
        PowerLaw
    }

    /// <summary>
    /// Configuration for soil thermal model (Force-Restore Method).
    /// Calculates hourly ground surface temperature from weather data and soil properties.
    /// </summary>
    [Serializable]
    public class SoilThermalConfig : ICloneable
    {
        /// <summary>Selected soil type preset. Use Custom to override individual properties.</summary>
        public SoilType SoilType { get; set; } = SoilType.DryClay;

        /// <summary>Soil thermal conductivity [W/(m*K)].</summary>
        public double ThermalConductivity { get; set; } = 0.5;

        /// <summary>Soil volumetric heat capacity [MJ/(m^3*K)].</summary>
        public double VolumetricHeatCapacity { get; set; } = 1.4;

        /// <summary>Soil thermal diffusivity [m^2/s].</summary>
        public double ThermalDiffusivity { get; set; } = 0.36e-6;

        /// <summary>Surface shortwave albedo (reflectance) [-].</summary>
        public double SurfaceAlbedo { get; set; } = 0.2;

        /// <summary>Surface longwave emissivity [-].</summary>
        public double SurfaceEmissivity { get; set; } = 0.95;

        /// <summary>Top layer thickness for Force-Restore [m]. Default 0.05.</summary>
        public double TopLayerDepth { get; set; } = 0.05;

        /// <summary>Deep layer thickness for Force-Restore [m]. Default 0.5.</summary>
        public double DeepLayerDepth { get; set; } = 0.5;

        /// <summary>Initial surface temperature [C]. If null, auto from EPW.</summary>
        public double? InitialSurfaceTemperature { get; set; } = null;

        /// <summary>Initial deep layer temperature [C]. If null, auto from EPW.</summary>
        public double? InitialDeepTemperature { get; set; } = null;

        /// <summary>Latent heat calculation method. Default Simplified for backward compatibility.</summary>
        public LatentHeatMethod LatentHeatMethod { get; set; } = LatentHeatMethod.Simplified;

        /// <summary>Surface moisture availability [0-1]. Legacy field; prefer SoilMoistureConfig for PM methods.</summary>
        public double MoistureAvailability { get; set; } = 0.3;

        /// <summary>Soil surface roughness length [m] for convective heat transfer. Legacy; prefer SoilSurfaceConfig.</summary>
        public double RoughnessLength { get; set; } = 0.01;

        /// <summary>Optional annual average air temperature [C] for deep layer initialization.</summary>
        public double? AnnualMeanTemperature { get; set; } = null;

        /// <summary>Maximum allowed latent heat flux [W/m^2]. Physical upper bound.</summary>
        public double MaxLatentHeatFlux { get; set; } = 1000.0;

        /// <summary>Sub-steps per hour for numerical stability. Default 10.</summary>
        public int SubStepsPerHour { get; set; } = 10;

        /// <summary>Convergence tolerance for surface energy balance iteration [W/m^2].</summary>
        public double EnergyBalanceTolerance { get; set; } = 0.1;

        /// <summary>Maximum iterations for surface energy balance at each sub-step.</summary>
        public int MaxIterations { get; set; } = 20;

        // FIX (P3): Removed redundant FieldCapacity and WiltingPoint.
        // These fields existed in both SoilThermalConfig and SoilMoistureConfig,
        // but only SoilMoistureConfig.FieldCapacity is actually used in calculations.
        // Use SoilMoistureConfig.FieldCapacity for all moisture-related settings.

        /// <summary>
        /// Air temperature configuration for overriding EPW dry bulb temperature.
        /// If null or Mode=FromEPW, EPW dry bulb temperature is used automatically.
        /// </summary>
        public AirTemperatureConfig AirTemperatureConfig { get; set; }

        /// <summary>
        /// Relative humidity configuration for overriding EPW relative humidity.
        /// If null or Mode=FromEPW, EPW relative humidity is used automatically.
        /// When air temperature is overridden, overriding RH ensures physical consistency
        /// between temperature, humidity, and VPD in latent heat calculations.
        /// </summary>
        public RelativeHumidityConfig RelativeHumidityConfig { get; set; }

        public object Clone()
        {
            return new SoilThermalConfig
            {
                SoilType = this.SoilType,
                ThermalConductivity = this.ThermalConductivity,
                VolumetricHeatCapacity = this.VolumetricHeatCapacity,
                ThermalDiffusivity = this.ThermalDiffusivity,
                SurfaceAlbedo = this.SurfaceAlbedo,
                SurfaceEmissivity = this.SurfaceEmissivity,
                TopLayerDepth = this.TopLayerDepth,
                DeepLayerDepth = this.DeepLayerDepth,
                InitialSurfaceTemperature = this.InitialSurfaceTemperature,
                InitialDeepTemperature = this.InitialDeepTemperature,
                LatentHeatMethod = this.LatentHeatMethod,
                MoistureAvailability = this.MoistureAvailability,
                RoughnessLength = this.RoughnessLength,
                AnnualMeanTemperature = this.AnnualMeanTemperature,
                MaxLatentHeatFlux = this.MaxLatentHeatFlux,
                SubStepsPerHour = this.SubStepsPerHour,
                EnergyBalanceTolerance = this.EnergyBalanceTolerance,
                MaxIterations = this.MaxIterations,
                // FieldCapacity and WiltingPoint removed - use SoilMoistureConfig
                AirTemperatureConfig = this.AirTemperatureConfig?.Clone() as AirTemperatureConfig,
                RelativeHumidityConfig = this.RelativeHumidityConfig?.Clone() as RelativeHumidityConfig
            };
        }

        public void ApplyPreset()
        {
            switch (SoilType)
            {
                case SoilType.DrySand:
                    ThermalConductivity = 0.3;
                    VolumetricHeatCapacity = 1.3;
                    ThermalDiffusivity = 0.23e-6;
                    SurfaceAlbedo = 0.25;
                    SurfaceEmissivity = 0.95;
                    break;
                case SoilType.WetSand:
                    ThermalConductivity = 2.5;
                    VolumetricHeatCapacity = 2.5;
                    ThermalDiffusivity = 1.00e-6;
                    SurfaceAlbedo = 0.20;
                    SurfaceEmissivity = 0.95;
                    break;
                case SoilType.DryClay:
                    ThermalConductivity = 0.5;
                    VolumetricHeatCapacity = 1.4;
                    ThermalDiffusivity = 0.36e-6;
                    SurfaceAlbedo = 0.20;
                    SurfaceEmissivity = 0.95;
                    break;
                case SoilType.WetClay:
                    ThermalConductivity = 2.0;
                    VolumetricHeatCapacity = 2.8;
                    ThermalDiffusivity = 0.71e-6;
                    SurfaceAlbedo = 0.15;
                    SurfaceEmissivity = 0.95;
                    break;
                case SoilType.AsphaltConcrete:
                    ThermalConductivity = 1.25;
                    VolumetricHeatCapacity = 2.15;
                    ThermalDiffusivity = 0.58e-6;
                    SurfaceAlbedo = 0.15;
                    SurfaceEmissivity = 0.95;
                    break;
                case SoilType.Custom:
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Surface aerodynamic and radiation properties configuration.
    /// Separated from SoilThermalConfig for cleaner parameter management.
    /// </summary>
    [Serializable]
    public class SoilSurfaceConfig : ICloneable
    {
        /// <summary>Wind measurement height [m]. Default 2.0 (FAO56 standard).</summary>
        public double WindMeasurementHeight { get; set; } = 2.0;

        /// <summary>Momentum roughness length [m]. Default 0.001 (bare soil, FAO56).</summary>
        public double MomentumRoughnessLength { get; set; } = 0.001;

        /// <summary>Scalar roughness length for heat/vapor [m]. Default 0.0001 (z0m/10).</summary>
        public double ScalarRoughnessLength { get; set; } = 0.0001;

        /// <summary>Von Karman constant [-]. Default 0.41.</summary>
        public double KarmanConstant { get; set; } = 0.41;

        /// <summary>Direct aerodynamic resistance input [s/m]. If &gt; 0, overrides calculation.</summary>
        public double AerodynamicResistance { get; set; } = -1.0;

        /// <summary>Enable atmospheric stability correction (Louis 1979). Default false.</summary>
        public bool UseStabilityCorrection { get; set; } = false;

        /// <summary>Stability correction parameter b for momentum (Louis 1979). Default 4.7.</summary>
        public double StabilityB_Momentum { get; set; } = 4.7;

        /// <summary>Stability correction parameter b for heat (Louis 1979). Default 4.7.</summary>
        public double StabilityB_Heat { get; set; } = 4.7;

        /// <summary>Stability correction parameter c (Louis 1979). Default 5.0.</summary>
        public double StabilityC { get; set; } = 5.0;

        /// <summary>Stability correction parameter d (Louis 1979). Default 5.0.</summary>
        public double StabilityD { get; set; } = 5.0;

        /// <summary>Maximum allowed aerodynamic resistance [s/m]. Default 500.</summary>
        public double MaxAerodynamicResistance { get; set; } = 500.0;

        /// <summary>Minimum allowed aerodynamic resistance [s/m]. Default 10.</summary>
        public double MinAerodynamicResistance { get; set; } = 10.0;

        /// <summary>Surface shortwave albedo [-]. Overrides SoilThermalConfig if set.</summary>
        public double? SurfaceAlbedoOverride { get; set; } = null;

        /// <summary>Surface longwave emissivity [-]. Overrides SoilThermalConfig if set.</summary>
        public double? SurfaceEmissivityOverride { get; set; } = null;

        /// <summary>
        /// Wind speed configuration for overriding EPW wind speed.
        /// If null or Mode=FromEPW, EPW wind speed is used automatically.
        /// Encapsulated within SoilSurfaceConfig to simplify data flow:
        /// SoilSurfaceSettings outputs a single SoilSurfSet object containing both
        /// aerodynamic parameters and wind speed configuration.
        /// </summary>
        public WindSpeedConfig WindSpeedConfig { get; set; }

        public object Clone()
        {
            return new SoilSurfaceConfig
            {
                WindMeasurementHeight = this.WindMeasurementHeight,
                MomentumRoughnessLength = this.MomentumRoughnessLength,
                ScalarRoughnessLength = this.ScalarRoughnessLength,
                KarmanConstant = this.KarmanConstant,
                AerodynamicResistance = this.AerodynamicResistance,
                UseStabilityCorrection = this.UseStabilityCorrection,
                StabilityB_Momentum = this.StabilityB_Momentum,
                StabilityB_Heat = this.StabilityB_Heat,
                StabilityC = this.StabilityC,
                StabilityD = this.StabilityD,
                MaxAerodynamicResistance = this.MaxAerodynamicResistance,
                MinAerodynamicResistance = this.MinAerodynamicResistance,
                SurfaceAlbedoOverride = this.SurfaceAlbedoOverride,
                SurfaceEmissivityOverride = this.SurfaceEmissivityOverride,
                WindSpeedConfig = this.WindSpeedConfig?.Clone() as WindSpeedConfig
            };
        }
    }

    /// <summary>
    /// Soil moisture and beta factor configuration.
    /// Controls moisture availability for latent heat flux calculations (PM single-source).
    /// </summary>
    [Serializable]
    public class SoilMoistureConfig : ICloneable
    {
        /// <summary>Beta calculation method. Default Noilhan (ISBA).</summary>
        public BetaMethod BetaMethod { get; set; } = BetaMethod.Noilhan;

        /// <summary>Surface soil moisture (wg) [m^3/m^3]. Default 0.25.</summary>
        public double SurfaceSoilMoisture { get; set; } = 0.25;

        /// <summary>Field capacity / saturation moisture (wsat) [m^3/m^3]. Default 0.35.</summary>
        public double FieldCapacity { get; set; } = 0.35;

        /// <summary>Direct beta value [0-1]. Used when BetaMethod=Direct.</summary>
        public double DirectBeta { get; set; } = 0.5;

        /// <summary>Soil texture index for KondoSaigusa method. 1=sand, 3=loam, 5=clay.</summary>
        public int SoilTextureIndex { get; set; } = 3;

        /// <summary>Power law exponent for custom beta method. Default 1.0.</summary>
        public double BetaExponent { get; set; } = 1.0;

        /// <summary>Minimum beta value (lower bound). Default 0.05.</summary>
        public double BetaMin { get; set; } = 0.05;

        /// <summary>Minimum soil surface resistance [s/m]. Default 50 (wet soil).</summary>
        public double MinSoilResistance { get; set; } = 50.0;

        /// <summary>Maximum (dry) soil surface resistance [s/m]. Default 500.</summary>
        public double MaxSoilResistance { get; set; } = 500.0;

        /// <summary>Beta decay sensitivity index 'a'. Default 5.0 (Noilhan ISBA).</summary>
        public double BetaSensitivityIndex { get; set; } = 5.0;

        /// <summary>Direct soil resistance input [s/m]. If &gt; 0, overrides calculation.</summary>
        public double SoilSurfaceResistance { get; set; } = -1.0;

        /// <summary>Enable forced wet surface (e.g., after rain). Default false.</summary>
        public bool ForceWetSurface { get; set; } = false;

        /// <summary>Forced wet beta value when ForceWetSurface=true. Default 1.0.</summary>
        public double ForcedWetBeta { get; set; } = 1.0;

        public object Clone()
        {
            return new SoilMoistureConfig
            {
                BetaMethod = this.BetaMethod,
                SurfaceSoilMoisture = this.SurfaceSoilMoisture,
                FieldCapacity = this.FieldCapacity,
                DirectBeta = this.DirectBeta,
                SoilTextureIndex = this.SoilTextureIndex,
                BetaExponent = this.BetaExponent,
                BetaMin = this.BetaMin,
                MinSoilResistance = this.MinSoilResistance,
                MaxSoilResistance = this.MaxSoilResistance,
                BetaSensitivityIndex = this.BetaSensitivityIndex,
                SoilSurfaceResistance = this.SoilSurfaceResistance,
                ForceWetSurface = this.ForceWetSurface,
                ForcedWetBeta = this.ForcedWetBeta
            };
        }
    }

    #endregion

    #region PV Simulation Configs

    [Serializable]
    public class SimulationTimeConfig
    {
        public int StartMonth { get; set; }
        public int StartDay { get; set; }
        public int StartHour { get; set; }
        public int EndMonth { get; set; }
        public int EndDay { get; set; }
        public int EndHour { get; set; }

        public SimulationTimeConfig()
        {
            StartMonth = 1; StartDay = 1; StartHour = 0;
            EndMonth = 12; EndDay = 31; EndHour = 23;
        }

        public void GetHourRange(out int startHoy, out int endHoy, int maxHoy = 8759)
        {
            bool isLeapYear = maxHoy >= 8783;
            startHoy = DateToHoy(StartMonth, StartDay, StartHour, isLeapYear);
            endHoy = DateToHoy(EndMonth, EndDay, EndHour, isLeapYear);
            if (endHoy < startHoy) endHoy = startHoy;
            if (startHoy < 0) startHoy = 0;
            if (endHoy > maxHoy) endHoy = maxHoy;
        }

        private static int DateToHoy(int month, int day, int hour, bool isLeapYear = false)
        {
            int[] daysInMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
            if (isLeapYear) daysInMonth[1] = 29;
            int hoy = 0;
            for (int m = 1; m < month; m++)
                hoy += daysInMonth[m - 1] * 24;
            hoy += (day - 1) * 24;
            hoy += hour;
            return Math.Min(hoy, 8783);
        }

        public override string ToString()
        {
            return $"Analysis Period: {StartMonth:D2}/{StartDay:D2} {StartHour:D2}:00 - {EndMonth:D2}/{EndDay:D2} {EndHour:D2}:00";
        }
    }

    [Serializable]
    public class PVConfig
    {
        public bool EnableBifacial { get; set; } = false;
        public double GridResolution { get; set; } = 0.5;
        public double Efficiency { get; set; } = 0.20;
        public double ActiveAreaRatio { get; set; } = 0.9;
        public double Albedo { get; set; } = 0.2;
        public double BifacialityFactor { get; set; } = 0.7;
        public double RearGainFactor { get; set; } = 1.0;
        public double RowSpacing { get; set; } = 2.0;
        public double ModuleHeight { get; set; } = 1.0;
        public double IAMCoefficient { get; set; } = 0.05;
        public double SystemLossFactor { get; set; } = 0.14;
    }

    [Serializable]
    public class TemperatureConfig
    {
        public bool EnableTemperatureModel { get; set; } = true;
        public double NOCT { get; set; } = 45.0;
        public double TempCoefficient { get; set; } = -0.4;
        public bool TempCoeffIsPercent { get; set; } = true;
        public double WindSpeedFactor { get; set; } = 1.0;
        public MountingType MountingType { get; set; } = MountingType.FreeStanding;
        public double NOCTReferenceEfficiency { get; set; } = 0.10;
    }

    [Serializable]
    public class SkyModelConfig
    {
        public bool UsePerezModel { get; set; } = false;
        public double HorizonBrighteningCoeff { get; set; } = 1.0;
        public double CircumsolarBrighteningCoeff { get; set; } = 1.0;
        public double DirectDiffuseRatioThreshold { get; set; } = 0.01;
    }

    [Serializable]
    public class InverterConfig
    {
        public bool EnableInverterModel { get; set; } = true;
        public double RatedPower { get; set; } = 10000.0;
        public double NominalEfficiency { get; set; } = 0.96;
        public double MPPTMinVoltage { get; set; } = 200.0;
        public double MPPTMaxVoltage { get; set; } = 800.0;
        public double ClippingRatio { get; set; } = 1.0;
        public double NightTareLoss { get; set; } = 5.0;
        public int ModulesPerString { get; set; } = 0;
        public double NominalVmp { get; set; } = 35.0;
        public double TempCoeffVoltage { get; set; } = -0.004;
        public double NominalVoc { get; set; } = 42.0;
        public double TempCoeffVoc { get; set; } = -0.003;
    }

    [Serializable]
    public class RaytracingConfig
    {
        public int SVFSampleCount { get; set; } = 500;
        public double SVFRayOffset { get; set; } = 0.01;
        public double ShadowRayOffset { get; set; } = 0.001;
        public double MaxShadowDistance { get; set; } = 10000.0;
    }

    #endregion

    #region MRT Calculation Configs

    /// <summary>
    /// Obstacle type classification for fine-grained direct radiation (DNI) calculation.
    /// Used to determine how solar rays interact with different obstacle materials.
    /// </summary>
    public enum ObstacleType
    {
        None, Opaque, Tree, Translucent
    }

    /// <summary>
    /// Structured obstacle set with classified obstacle types for fine-grained DNI
    /// transmission through vegetation and translucent materials.
    /// </summary>
    [Serializable]
    public class ObstacleSet
    {
        // ---- Tree-related parameters ----
        public List<Rhino.Geometry.Mesh> TreeDetailMeshes { get; set; } = new List<Rhino.Geometry.Mesh>();
        public List<Rhino.Geometry.Mesh> TreeCanopyMeshes { get; set; } = new List<Rhino.Geometry.Mesh>();
        public double LeafAreaDensity { get; set; } = 1.0;
        public double ExtinctionCoefficient { get; set; } = 0.65;
        public double TreeCanopyTemperature { get; set; } = double.NaN;
        public List<double> HourlyTreeCanopyTemperatures { get; set; } = null;

        // ---- Translucent shade parameters ----
        public List<Rhino.Geometry.Mesh> TranslucentShadeMeshes { get; set; } = new List<Rhino.Geometry.Mesh>();
        public double TranslucentTransmittance { get; set; } = 0.05;
        public double TranslucentSurfaceTemperature { get; set; } = double.NaN;
        public List<double> HourlyTranslucentSurfaceTemperatures { get; set; } = null;

        // ---- Opaque obstacle parameters ----
        public List<Rhino.Geometry.Mesh> OpaqueObjectMeshes { get; set; } = new List<Rhino.Geometry.Mesh>();
        public double SurroundingSurfaceTemperature { get; set; } = double.NaN;
        public List<double> HourlySurroundingSurfaceTemperatures { get; set; } = null;

        public bool HasAnyObstacles =>
            (TreeDetailMeshes?.Count > 0) || (TreeCanopyMeshes?.Count > 0) ||
            (TranslucentShadeMeshes?.Count > 0) || (OpaqueObjectMeshes?.Count > 0);

        public List<Rhino.Geometry.Mesh> GetAllMeshes()
        {
            var all = new List<Rhino.Geometry.Mesh>();
            if (TreeDetailMeshes != null) all.AddRange(TreeDetailMeshes);
            if (TreeCanopyMeshes != null) all.AddRange(TreeCanopyMeshes);
            if (TranslucentShadeMeshes != null) all.AddRange(TranslucentShadeMeshes);
            if (OpaqueObjectMeshes != null) all.AddRange(OpaqueObjectMeshes);
            return all;
        }

        /// <summary>Get obstacle surface temperature for the given hour. Falls back to airTemp if not set.</summary>
        public double GetObstacleTemperature(int hoy, double airTemp)
        {
            if (HourlySurroundingSurfaceTemperatures != null && hoy >= 0 && hoy < HourlySurroundingSurfaceTemperatures.Count)
                return HourlySurroundingSurfaceTemperatures[hoy];
            if (!double.IsNaN(SurroundingSurfaceTemperature)) return SurroundingSurfaceTemperature;
            return airTemp;
        }

        /// <summary>Get tree canopy temperature for the given hour. Falls back to airTemp if not set.</summary>
        public double GetTreeCanopyTemperature(int hoy, double airTemp)
        {
            if (HourlyTreeCanopyTemperatures != null && hoy >= 0 && hoy < HourlyTreeCanopyTemperatures.Count)
                return HourlyTreeCanopyTemperatures[hoy];
            if (!double.IsNaN(TreeCanopyTemperature)) return TreeCanopyTemperature;
            return airTemp;
        }

        /// <summary>Get translucent surface temperature for the given hour. Falls back to airTemp if not set.</summary>
        public double GetTranslucentSurfaceTemperature(int hoy, double airTemp)
        {
            if (HourlyTranslucentSurfaceTemperatures != null && hoy >= 0 && hoy < HourlyTranslucentSurfaceTemperatures.Count)
                return HourlyTranslucentSurfaceTemperatures[hoy];
            if (!double.IsNaN(TranslucentSurfaceTemperature)) return TranslucentSurfaceTemperature;
            return airTemp;
        }
    }

    [Serializable]
    public class MRTConfig
    {
        public double PostureFactor { get; set; } = 0.725;
        public double BodyAbsorptivity { get; set; } = 0.7;
        public double BodyEmissivity { get; set; } = 0.95;
        public double RadiativeHeatTransferCoeff { get; set; } = 6.012;
        public double FloorReflectance { get; set; } = 0.25;
        public int ExposureSamplePoints { get; set; } = 3;
        public double AnalysisHeight { get; set; } = 1.5;
        public double BodyHeight { get; set; } = 1.7;
        public bool IncludeShortwave { get; set; } = true;
        public bool IncludeLongwave { get; set; } = true;
        public double MaxRayDistance { get; set; } = 500.0;
        public double? GroundTemperature { get; set; } = null;
        public bool UseRayManModel { get; set; } = false;
        public int SVFSampleCount { get; set; } = 1000;
        public double SkyEmissivity { get; set; } = -1.0;
        public double LongwaveLinearCoeff { get; set; } = 0.5;

        /// <summary>
        /// Ground surface longwave emissivity [-].
        /// Only used by the RayMan model (ignored in SolarCal).
        /// Typical values: grass/vegetation 0.95-0.98, concrete 0.88-0.94,
        /// asphalt 0.90-0.96, water 0.95-0.96.
        /// Default 0.95.
        /// </summary>
        public double GroundEmissivity { get; set; } = 0.95;

        /// <summary>
        /// Obstacle (surrounding surfaces) longwave emissivity [-].
        /// Only used by the RayMan model (ignored in SolarCal).
        /// Typical values: concrete/brick 0.88-0.95, glass 0.84-0.90,
        /// vegetation 0.95-0.98, metal 0.20-0.70.
        /// Default 0.95.
        /// </summary>
        public double ObstacleEmissivity { get; set; } = 0.95;
    }

    [Serializable]
    public enum HumanPosture
    {
        Standing,
        Sitting
    }

    #endregion


    #region Ground Surface and Wind Speed Configs

    /// <summary>
    /// Relative humidity input mode for flexible RH configuration in soil thermal simulation.
    /// Supports overriding EPW relative humidity with user-provided values to maintain
    /// physical consistency with overridden air temperature.
    /// </summary>
    public enum RelativeHumidityMode
    {
        /// <summary>Use EPW relative humidity (default behavior).</summary>
        FromEPW,
        /// <summary>Single constant value for all points and all hours.</summary>
        SingleValue,
        /// <summary>Per-point constant: one value per point, applied to all hours.</summary>
        PerPointConstant,
        /// <summary>Time series: one value per hour, shared by all points.</summary>
        TimeSeries,
        /// <summary>Per-point time series: each point has its own hourly time series.</summary>
        PerPointTimeSeries
    }

    /// <summary>
    /// Relative humidity configuration for soil thermal simulation.
    /// Supports flexible RH input: EPW, single value, per-point constant, time series, or full 2D array.
    /// When air temperature is overridden, RH should also be overridden to maintain physical consistency
    /// between temperature, humidity, and vapor pressure deficit (VPD).
    /// </summary>
    [Serializable]
    public class RelativeHumidityConfig : ICloneable
    {
        /// <summary>Relative humidity input mode.</summary>
        public RelativeHumidityMode Mode { get; set; } = RelativeHumidityMode.FromEPW;

        /// <summary>Single constant relative humidity [%] for SingleValue mode.</summary>
        public double SingleRelativeHumidity { get; set; } = 50.0;

        /// <summary>Per-point constant relative humidity [%] for PerPointConstant mode. Length = nPts.</summary>
        public List<double> PerPointRelativeHumidity { get; set; } = new List<double>();

        /// <summary>Shared hourly relative humidity time series [%] for TimeSeries mode. Length = nHours.</summary>
        public List<double> TimeSeriesRelativeHumidity { get; set; } = new List<double>();

        /// <summary>Per-point hourly relative humidity [%] for PerPointTimeSeries mode. [nPts][nHours].</summary>
        public List<List<double>> PerPointTimeSeriesRelativeHumidity { get; set; } = new List<List<double>>();

        /// <summary>
        /// Get relative humidity for a specific point and hour.
        /// Falls back to EPW value if mode is FromEPW or data is missing.
        /// Returns value clamped to [0, 100] to ensure physical validity.
        /// </summary>
        public double GetRelativeHumidity(int pointIndex, int hourIndex, double epwRelativeHumidity)
        {
            double rh;
            switch (Mode)
            {
                case RelativeHumidityMode.SingleValue:
                    rh = SingleRelativeHumidity;
                    break;

                case RelativeHumidityMode.PerPointConstant:
                    if (PerPointRelativeHumidity != null && pointIndex >= 0 && pointIndex < PerPointRelativeHumidity.Count)
                        rh = PerPointRelativeHumidity[pointIndex];
                    else
                        rh = epwRelativeHumidity;
                    break;

                case RelativeHumidityMode.TimeSeries:
                    if (TimeSeriesRelativeHumidity != null && hourIndex >= 0 && hourIndex < TimeSeriesRelativeHumidity.Count)
                        rh = TimeSeriesRelativeHumidity[hourIndex];
                    else
                        rh = epwRelativeHumidity;
                    break;

                case RelativeHumidityMode.PerPointTimeSeries:
                    if (PerPointTimeSeriesRelativeHumidity != null && pointIndex >= 0 && pointIndex < PerPointTimeSeriesRelativeHumidity.Count)
                    {
                        var ptSeries = PerPointTimeSeriesRelativeHumidity[pointIndex];
                        if (ptSeries != null && hourIndex >= 0 && hourIndex < ptSeries.Count)
                            rh = ptSeries[hourIndex];
                        else
                            rh = epwRelativeHumidity;
                    }
                    else
                        rh = epwRelativeHumidity;
                    break;

                case RelativeHumidityMode.FromEPW:
                default:
                    rh = epwRelativeHumidity;
                    break;
            }
            // Clamp to physically valid range [0, 100]
            return Math.Max(0.0, Math.Min(100.0, rh));
        }

        public object Clone()
        {
            return new RelativeHumidityConfig
            {
                Mode = this.Mode,
                SingleRelativeHumidity = this.SingleRelativeHumidity,
                PerPointRelativeHumidity = this.PerPointRelativeHumidity != null ? new List<double>(this.PerPointRelativeHumidity) : null,
                TimeSeriesRelativeHumidity = this.TimeSeriesRelativeHumidity != null ? new List<double>(this.TimeSeriesRelativeHumidity) : null,
                PerPointTimeSeriesRelativeHumidity = this.PerPointTimeSeriesRelativeHumidity != null
                    ? this.PerPointTimeSeriesRelativeHumidity.Select(s => s != null ? new List<double>(s) : null).ToList()
                    : null
            };
        }
    }

    /// <summary>
    /// Air temperature input mode for flexible air temperature configuration in soil thermal simulation.
    /// Supports overriding EPW dry bulb temperature with user-provided values.
    /// </summary>
    public enum AirTemperatureMode
    {
        /// <summary>Use EPW dry bulb temperature (default behavior).</summary>
        FromEPW,
        /// <summary>Single constant value for all points and all hours.</summary>
        SingleValue,
        /// <summary>Per-point constant: one value per point, applied to all hours.</summary>
        PerPointConstant,
        /// <summary>Time series: one value per hour, shared by all points.</summary>
        TimeSeries,
        /// <summary>Per-point time series: each point has its own hourly time series.</summary>
        PerPointTimeSeries
    }

    /// <summary>
    /// Air temperature configuration for soil thermal simulation.
    /// Supports flexible air temperature input: EPW, single value, per-point constant, time series, or full 2D array.
    /// </summary>
    [Serializable]
    public class AirTemperatureConfig : ICloneable
    {
        /// <summary>Air temperature input mode.</summary>
        public AirTemperatureMode Mode { get; set; } = AirTemperatureMode.FromEPW;

        /// <summary>Single constant air temperature [C] for SingleValue mode.</summary>
        public double SingleAirTemperature { get; set; } = 20.0;

        /// <summary>Per-point constant air temperatures [C] for PerPointConstant mode. Length = nPts.</summary>
        public List<double> PerPointAirTemperature { get; set; } = new List<double>();

        /// <summary>Shared hourly air temperature time series [C] for TimeSeries mode. Length = nHours.</summary>
        public List<double> TimeSeriesAirTemperature { get; set; } = new List<double>();

        /// <summary>Per-point hourly air temperature [C] for PerPointTimeSeries mode. [nPts][nHours].</summary>
        public List<List<double>> PerPointTimeSeriesAirTemperature { get; set; } = new List<List<double>>();

        /// <summary>
        /// Get air temperature for a specific point and hour.
        /// Falls back to EPW value if mode is FromEPW or data is missing.
        /// </summary>
        public double GetAirTemperature(int pointIndex, int hourIndex, double epwAirTemperature)
        {
            switch (Mode)
            {
                case AirTemperatureMode.SingleValue:
                    return SingleAirTemperature;

                case AirTemperatureMode.PerPointConstant:
                    if (PerPointAirTemperature != null && pointIndex >= 0 && pointIndex < PerPointAirTemperature.Count)
                        return PerPointAirTemperature[pointIndex];
                    return epwAirTemperature;

                case AirTemperatureMode.TimeSeries:
                    if (TimeSeriesAirTemperature != null && hourIndex >= 0 && hourIndex < TimeSeriesAirTemperature.Count)
                        return TimeSeriesAirTemperature[hourIndex];
                    return epwAirTemperature;

                case AirTemperatureMode.PerPointTimeSeries:
                    if (PerPointTimeSeriesAirTemperature != null && pointIndex >= 0 && pointIndex < PerPointTimeSeriesAirTemperature.Count)
                    {
                        var ptSeries = PerPointTimeSeriesAirTemperature[pointIndex];
                        if (ptSeries != null && hourIndex >= 0 && hourIndex < ptSeries.Count)
                            return ptSeries[hourIndex];
                    }
                    return epwAirTemperature;

                case AirTemperatureMode.FromEPW:
                default:
                    return epwAirTemperature;
            }
        }

        public object Clone()
        {
            return new AirTemperatureConfig
            {
                Mode = this.Mode,
                SingleAirTemperature = this.SingleAirTemperature,
                PerPointAirTemperature = this.PerPointAirTemperature != null ? new List<double>(this.PerPointAirTemperature) : null,
                TimeSeriesAirTemperature = this.TimeSeriesAirTemperature != null ? new List<double>(this.TimeSeriesAirTemperature) : null,
                PerPointTimeSeriesAirTemperature = this.PerPointTimeSeriesAirTemperature != null
                    ? this.PerPointTimeSeriesAirTemperature.Select(s => s != null ? new List<double>(s) : null).ToList()
                    : null
            };
        }
    }

    /// <summary>
    /// Wind speed input mode for flexible wind speed configuration in soil thermal simulation.
    /// </summary>
    public enum WindSpeedMode
    {
        /// <summary>Use EPW wind speed (default behavior).</summary>
        FromEPW,
        /// <summary>Single constant value for all points and all hours.</summary>
        SingleValue,
        /// <summary>Per-point constant: one value per point, applied to all hours.</summary>
        PerPointConstant,
        /// <summary>Time series: one value per hour, shared by all points.</summary>
        TimeSeries,
        /// <summary>Per-point time series: each point has its own hourly time series.</summary>
        PerPointTimeSeries
    }

    /// <summary>
    /// Wind speed configuration for soil thermal simulation.
    /// Supports flexible wind input: EPW, single value, per-point constant, time series, or full 2D array.
    /// </summary>
    [Serializable]
    public class WindSpeedConfig : ICloneable
    {
        /// <summary>Wind speed input mode.</summary>
        public WindSpeedMode Mode { get; set; } = WindSpeedMode.FromEPW;

        /// <summary>Single constant wind speed [m/s] for SingleValue mode.</summary>
        public double SingleWindSpeed { get; set; } = 1.0;

        /// <summary>Per-point constant wind speeds [m/s] for PerPointConstant mode. Length = nPts.</summary>
        public List<double> PerPointWindSpeed { get; set; } = new List<double>();

        /// <summary>Shared hourly wind speed time series [m/s] for TimeSeries mode. Length = nHours.</summary>
        public List<double> TimeSeriesWindSpeed { get; set; } = new List<double>();

        /// <summary>Per-point hourly wind speed [m/s] for PerPointTimeSeries mode. [nPts][nHours].</summary>
        public List<List<double>> PerPointTimeSeriesWindSpeed { get; set; } = new List<List<double>>();

        /// <summary>
        /// Get wind speed for a specific point and hour.
        /// Falls back to EPW value if mode is FromEPW or data is missing.
        /// </summary>
        public double GetWindSpeed(int pointIndex, int hourIndex, double epwWindSpeed)
        {
            switch (Mode)
            {
                case WindSpeedMode.SingleValue:
                    return SingleWindSpeed;

                case WindSpeedMode.PerPointConstant:
                    if (PerPointWindSpeed != null && pointIndex >= 0 && pointIndex < PerPointWindSpeed.Count)
                        return PerPointWindSpeed[pointIndex];
                    return epwWindSpeed;

                case WindSpeedMode.TimeSeries:
                    if (TimeSeriesWindSpeed != null && hourIndex >= 0 && hourIndex < TimeSeriesWindSpeed.Count)
                        return TimeSeriesWindSpeed[hourIndex];
                    return epwWindSpeed;

                case WindSpeedMode.PerPointTimeSeries:
                    if (PerPointTimeSeriesWindSpeed != null && pointIndex >= 0 && pointIndex < PerPointTimeSeriesWindSpeed.Count)
                    {
                        var ptSeries = PerPointTimeSeriesWindSpeed[pointIndex];
                        if (ptSeries != null && hourIndex >= 0 && hourIndex < ptSeries.Count)
                            return ptSeries[hourIndex];
                    }
                    return epwWindSpeed;

                case WindSpeedMode.FromEPW:
                default:
                    return epwWindSpeed;
            }
        }

        public object Clone()
        {
            return new WindSpeedConfig
            {
                Mode = this.Mode,
                SingleWindSpeed = this.SingleWindSpeed,
                PerPointWindSpeed = this.PerPointWindSpeed != null ? new List<double>(this.PerPointWindSpeed) : null,
                TimeSeriesWindSpeed = this.TimeSeriesWindSpeed != null ? new List<double>(this.TimeSeriesWindSpeed) : null,
                PerPointTimeSeriesWindSpeed = this.PerPointTimeSeriesWindSpeed != null
                    ? this.PerPointTimeSeriesWindSpeed.Select(s => s != null ? new List<double>(s) : null).ToList()
                    : null
            };
        }
    }

    /// <summary>
    /// Ground surface configuration for spatial soil thermal simulation.
    /// Encapsulates ground geometry, environment parameters, and layer settings.
    /// Output from Ground Surface Settings component, consumed by SpatialSoilThermalSimulator.
    /// </summary>
    [Serializable]
    public class GroundSurfaceConfig
    {
        /// <summary>Analysis points on ground surface (mesh face centers).</summary>
        public List<Rhino.Geometry.Point3d> AnalysisPoints { get; set; } = new List<Rhino.Geometry.Point3d>();

        /// <summary>Generated mesh for the ground surface.</summary>
        public Rhino.Geometry.Mesh GroundMesh { get; set; }

        /// <summary>Top layer depth d1 [m]. Default 0.05.</summary>
        public double TopLayerDepth { get; set; } = 0.05;

        /// <summary>Deep layer depth d2 [m]. Default 0.5.</summary>
        public double DeepLayerDepth { get; set; } = 0.5;

        /// <summary>Height above ground for solar exposure ray tracing [m]. Default 0.01.</summary>
        public double ExposureHeight { get; set; } = 0.01;

        /// <summary>Hemisphere samples for SVF. Default 500.</summary>
        public int SVFSampleCount { get; set; } = 500;

        /// <summary>Average shortwave reflectance of surrounding surfaces [-]. Default 0.2.</summary>
        public double SurroundReflectance { get; set; } = 0.2;

        /// <summary>Average longwave emissivity of surrounding surfaces [-]. Default 0.95.</summary>
        public double SurroundEmissivity { get; set; } = 0.95;

        /// <summary>Mesh resolution used for ground surface discretization [m].</summary>
        public double MeshResolution { get; set; } = 1.0;

        /// <summary>
        /// ENHANCED (2026-06-14): Classified obstacle set for fine-grained DNI transmission.
        /// Replaces the flat ObstacleMeshes list. Supports opaque buildings, trees with
        /// Beer-Lambert canopy attenuation, and translucent sunshades.
        /// Backward compatible: legacy code can use ObstacleMeshes property (auto-maps to ObstacleSet).
        /// </summary>
        public ObstacleSet ObstacleSet { get; set; } = new ObstacleSet();

        /// <summary>
        /// Backward-compatible access to all obstacle meshes as a flat list.
        /// Returns ObstacleSet.GetAllMeshes() if an ObstacleSet is configured,
        /// otherwise an empty list.
        /// </summary>
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public List<Rhino.Geometry.Mesh> ObstacleMeshes
        {
            get => ObstacleSet?.GetAllMeshes() ?? new List<Rhino.Geometry.Mesh>();
            set
            {
                if (ObstacleSet == null) ObstacleSet = new ObstacleSet();
                ObstacleSet.OpaqueObjectMeshes = value ?? new List<Rhino.Geometry.Mesh>();
            }
        }

        /// <summary>Number of analysis points.</summary>
        public int PointCount => AnalysisPoints?.Count ?? 0;

        /// <summary>Number of classified obstacle categories with meshes.</summary>
        public int ObstacleCount => ObstacleSet?.HasAnyObstacles == true
            ? (ObstacleSet.TreeDetailMeshes?.Count ?? 0)
            + (ObstacleSet.TreeCanopyMeshes?.Count ?? 0)
            + (ObstacleSet.TranslucentShadeMeshes?.Count ?? 0)
            + (ObstacleSet.OpaqueObjectMeshes?.Count ?? 0) : 0;
    }

    #endregion
}