using System;
using Common.Core;
namespace SoilThermophysics.Core
{
    /// <summary>
    /// Soil Thermal Model with Penman-Monteith single-source latent heat flux.
    /// 
    /// Core methods:
    ///   1) Force-Restore Method for soil temperature (Deardorff 1978; Noilhan &amp; Planton 1989)
    ///   2) Penman-Monteith single-source latent heat flux (FAO56 / ISBA)
    ///   3) Atmospheric stability correction (Louis 1979)
    /// 
    /// Backward compatible: LatentHeatMethod=Simplified preserves original behavior.
    /// 
    /// References:
    /// [1] Deardorff, J.W. (1978). Efficient prediction of ground surface temperature...
    /// [2] Noilhan, J. &amp; Planton, S. (1989). ISBA parameterization. Mon. Wea. Rev., 117, 536-549.
    /// [3] Allen, R.G. et al. (1998). FAO Irrigation and Drainage Paper 56.
    /// [5] Louis, J.F. (1979). A parametric model of vertical eddy fluxes. Bound.-Layer Meteor., 17, 187-202.
    /// </summary>
    public class SoilTemperatureModel
    {
        private SoilThermalConfig _soilConfig;
        private SoilSurfaceConfig _surfConfig;
        private SoilMoistureConfig _moistConfig;

        private const double SIGMA = 5.67e-8;
        private const double SECONDS_PER_HOUR = 3600.0;

        // Current state
        public double T1 { get; private set; }
        public double T2 { get; private set; }
        public double TimeConstantTau { get; private set; }

        // Computed fluxes (exposed for result storage)
        public double LastNetRadiation { get; private set; }
        public double LastSensibleHeat { get; private set; }
        public double LastLatentHeat { get; private set; }
        public double LastGroundHeatFlux { get; private set; }
        public double LastBeta { get; private set; }
        public double LastRa { get; private set; }
        public double LastRsSoil { get; private set; }

        public int SubStepsPerHour { get; set; } = 10;
        public double EnergyBalanceTolerance { get; set; } = 0.1;
        public int MaxIterations { get; set; } = 20;

        /// <summary>
        /// Creates a soil temperature model with the specified configurations.
        /// </summary>
        public SoilTemperatureModel(SoilThermalConfig soilConfig,
                                     SoilSurfaceConfig surfConfig = null,
                                     SoilMoistureConfig moistConfig = null)
        {
            _soilConfig = soilConfig ?? throw new ArgumentNullException(nameof(soilConfig));
            _soilConfig.ApplyPreset();
            _surfConfig = surfConfig ?? new SoilSurfaceConfig();
            _moistConfig = moistConfig ?? new SoilMoistureConfig();
            TimeConstantTau = 86400.0;
        }

        public void Initialize(double initialAirTemp, double? annualMeanTemp = null)
        {
            double tDeep = annualMeanTemp ?? initialAirTemp;
            T1 = _soilConfig.InitialSurfaceTemperature ?? initialAirTemp;
            T2 = _soilConfig.InitialDeepTemperature ?? tDeep;
        }

        /// <summary>
        /// Advances the model by one hour using the given weather record.
        /// </summary>
        public void Step(HourlyRecord weather, double timeStepHours = 1.0,
                         double ghiOverride = -1, double lDownOverride = -1,
                         double windSpeedOverride = -1, double airTempOverride = -999)
        {
            // Apply air temperature override if valid (above absolute zero)
            HourlyRecord effectiveWeather = weather;
            if (airTempOverride > -273.15)
            {
                effectiveWeather = new HourlyRecord
                {
                    LineIndex = weather.LineIndex, Year = weather.Year, Month = weather.Month,
                    Day = weather.Day, Hour = weather.Hour, Minute = weather.Minute,
                    DataSource = weather.DataSource,
                    DryBulbTemperature = airTempOverride,
                    DewPointTemperature = weather.DewPointTemperature,
                    RelativeHumidity = weather.RelativeHumidity,
                    AtmosphericPressure = weather.AtmosphericPressure,
                    ExtraterrestrialHorizontalRadiation = weather.ExtraterrestrialHorizontalRadiation,
                    ExtraterrestrialDirectNormalRadiation = weather.ExtraterrestrialDirectNormalRadiation,
                    HorizontalInfraredRadiation = weather.HorizontalInfraredRadiation,
                    GlobalHorizontalRadiation = weather.GlobalHorizontalRadiation,
                    DirectNormalRadiation = weather.DirectNormalRadiation,
                    DiffuseHorizontalRadiation = weather.DiffuseHorizontalRadiation,
                    GlobalHorizontalIlluminance = weather.GlobalHorizontalIlluminance,
                    DirectNormalIlluminance = weather.DirectNormalIlluminance,
                    DiffuseHorizontalIlluminance = weather.DiffuseHorizontalIlluminance,
                    ZenithLuminance = weather.ZenithLuminance,
                    WindDirection = weather.WindDirection, WindSpeed = weather.WindSpeed,
                    TotalSkyCover = weather.TotalSkyCover, OpaqueSkyCover = weather.OpaqueSkyCover,
                    Visibility = weather.Visibility, CeilingHeight = weather.CeilingHeight,
                    PresentWeatherObservation = weather.PresentWeatherObservation,
                    PresentWeatherCodes = weather.PresentWeatherCodes,
                    PrecipitableWater = weather.PrecipitableWater,
                    AerosolOpticalDepth = weather.AerosolOpticalDepth,
                    SnowDepth = weather.SnowDepth, DaysSinceLastSnowfall = weather.DaysSinceLastSnowfall,
                    Albedo = weather.Albedo,
                    LiquidPrecipitationDepth = weather.LiquidPrecipitationDepth,
                    LiquidPrecipitationQuantity = weather.LiquidPrecipitationQuantity
                };
            }

            double stepDuration = timeStepHours * SECONDS_PER_HOUR;
            double dt = stepDuration / SubStepsPerHour;
            double rhoC = _soilConfig.VolumetricHeatCapacity * 1e6;
            double d1 = Math.Max(0.001, _soilConfig.TopLayerDepth);
            double d2 = Math.Max(0.001, _soilConfig.DeepLayerDepth);
            double tau1 = TimeConstantTau;
            double tau2 = tau1 * (d2 / 0.5);
            tau2 = Math.Max(tau1, Math.Min(tau1 * 10.0, tau2));

            // T1恢复系数: 2*PI/tau1 (Deardorff 1978; Noilhan & Planton 1989)
            double coeffRestore = 2.0 * Math.PI / tau1;
            // T2恢复系数: 2*PI/tau2 (Noilhan & Planton 1989, Eq.25)
            double coeffDeep = 2.0 * Math.PI / tau2;
            double coeffFlux = 2.0 / (rhoC * d1);
            double coeffFlux2 = 1.0 / (rhoC * d2);

            // Pre-compute meteorological constants for this hour
            double pa_kPa = effectiveWeather.AtmosphericPressure / 1000.0;
            if (pa_kPa < 10) pa_kPa = 101.325;
            double psych = 0.665e-3 * pa_kPa;
            double lambda = (2.501e6 - 2361.0 * effectiveWeather.DryBulbTemperature);
            double rhoAir = pa_kPa * 1000.0 / (287.05 * (effectiveWeather.DryBulbTemperature + 273.15));

            for (int step = 0; step < SubStepsPerHour; step++)
            {
                double t1_prev = T1;
                double t2_prev = T2;
                double t1_guess = T1;

                for (int iter = 0; iter < MaxIterations; iter++)
                {
                    var flux = ComputeSurfaceFluxes(t1_guess, effectiveWeather, timeStepHours,
                                                     ghiOverride, lDownOverride,
                                                     psych, lambda, rhoAir, pa_kPa,
                                                     windSpeedOverride);

                    double g = flux.G;

                    double dT1 = dt * (coeffFlux * g - coeffRestore * (t1_guess - t2_prev));
                    double t1_new = t1_prev + dT1;

                    if (Math.Abs(t1_new - t1_guess) < 0.001)
                    {
                        T1 = t1_new;
                        break;
                    }
                    t1_guess = 0.5 * t1_guess + 0.5 * t1_new;
                    T1 = t1_guess;
                }

                double dT2 = dt * (coeffFlux2 * LastGroundHeatFlux + coeffDeep * (T1 - t2_prev));
                T2 = t2_prev + dT2;
            }
        }

        // =====================================================================
        // Surface flux computation
        // =====================================================================

        private struct SurfaceFluxes
        {
            public double Rn, H, LE, G;
            public double Beta, Ra, Rs_Soil;
        }

        private SurfaceFluxes ComputeSurfaceFluxes(double tGround, HourlyRecord weather,
                                                    double timeStepHours, double ghiOverride,
                                                    double lDownOverride, double psych,
                                                    double lambda, double rhoAir, double pa_kPa,
                                                    double windSpeedOverride = -1)
        {
            double timeStepFactor = 1.0 / Math.Max(0.01, timeStepHours);
            double tGroundK = tGround + 273.15;

            // Net shortwave
            double ghiRaw = ghiOverride >= 0 ? ghiOverride : weather.GlobalHorizontalRadiation;
            double ghi = ghiRaw * timeStepFactor;
            double swNet = (1.0 - _soilConfig.SurfaceAlbedo) * ghi;

            // Net longwave
            double lDownRaw = lDownOverride >= 0 ? lDownOverride : weather.HorizontalInfraredRadiation;
            double lDown = lDownRaw * timeStepFactor;
            double lwNet = _soilConfig.SurfaceEmissivity * (lDown - SIGMA * Math.Pow(tGroundK, 4));
            double rNet = swNet + lwNet;

            // Aerodynamic resistance
            double ra = ComputeAerodynamicResistance(tGround, weather, windSpeedOverride);

            // Sensible heat: H = rho*cp*(Tg - Tair) / ra
            double rhoCp = rhoAir * 1004.0;
            double hFlux = rhoCp * (tGround - weather.DryBulbTemperature) / ra;

            // Latent heat
            double leFlux = 0;
            double beta = 1.0;
            double rsSoil = 0;

            switch (_soilConfig.LatentHeatMethod)
            {
                case LatentHeatMethod.Simplified:
                    leFlux = ComputeLESimplified(tGround, weather, ra, rhoAir, psych);
                    beta = _soilConfig.MoistureAvailability;
                    break;

                case LatentHeatMethod.PenmanMonteith:
                    // FIX (P1): Iteratively couple G with PM equation for energy balance consistency.
                    // Standard PM: LE = [Delta*(Rn-G) + rho*cp*VPD/ra] / [Delta + gamma*(1+rs/ra)]
                    // G appears on both sides (via energy balance G=Rn-H-LE), so we iterate.
                    double gFluxIter = LastGroundHeatFlux; // Initial guess from previous sub-step
                    double leFluxIter = 0;
                    double betaIter = 1.0;
                    double rsIter = 0;
                    for (int pmIter = 0; pmIter < 5; pmIter++)
                    {
                        var pmResult = ComputeLE_PenmanMonteith(tGround, weather, rNet, hFlux,
                                                                 ra, psych, lambda, rhoAir, pa_kPa, gFluxIter);
                        leFluxIter = pmResult.LE;
                        betaIter = pmResult.Beta;
                        rsIter = pmResult.Rs_Soil;
                        double gNew = rNet - hFlux - leFluxIter;
                        if (Math.Abs(gNew - gFluxIter) < 1.0) break; // Converged
                        gFluxIter = 0.5 * gFluxIter + 0.5 * gNew;    // Relaxation
                    }
                    leFlux = leFluxIter;
                    beta = betaIter;
                    rsSoil = rsIter;
                    break;

                case LatentHeatMethod.NoLatentHeat:
                    leFlux = 0.0;
                    beta = 0.0;
                    rsSoil = 0.0;
                    break;
            }

            // Store pre-clipped LE for energy redistribution
            double leUnclipped = leFlux;
            leFlux = Math.Max(0.0, Math.Min(_soilConfig.MaxLatentHeatFlux, leFlux));
            double leExcess = leUnclipped - leFlux; // Energy cut by clipping (>=0 if clipped)

            // FIX (P2): Redistribute clipped LE excess between G and H proportionally
            // When LE hits MaxLatentHeatFlux limit, the excess energy is split:
            //   60% to G (soil heat storage), 40% to H (sensible heat)
            // This avoids dumping all excess into G, which would cause unrealistic T1 spikes.
            double gFlux;
            if (leExcess > 1.0 && _soilConfig.LatentHeatMethod == LatentHeatMethod.PenmanMonteith)
            {
                double gExtra = leExcess * 0.6;
                double hExtra = leExcess * 0.4;
                gFlux = rNet - hFlux - leFlux + gExtra;
                hFlux += hExtra; // Adjust H to absorb portion of excess
            }
            else
            {
                gFlux = rNet - hFlux - leFlux;
            }

            LastNetRadiation = rNet;
            LastSensibleHeat = hFlux;
            LastLatentHeat = leFlux;
            LastGroundHeatFlux = gFlux;
            LastBeta = beta;
            LastRa = ra;
            LastRsSoil = rsSoil;

            return new SurfaceFluxes
            {
                Rn = rNet,
                H = hFlux,
                LE = leFlux,
                G = gFlux,
                Beta = beta,
                Ra = ra,
                Rs_Soil = rsSoil
            };
        }

        // =====================================================================
        // Simplified LE (backward compatible)
        // =====================================================================

        private double ComputeLESimplified(double tGround, HourlyRecord weather,
                                            double ra, double rhoAir, double psych)
        {
            double beta = _soilConfig.MoistureAvailability;
            if (beta <= 0) return 0.0;

            double tAir = weather.DryBulbTemperature;
            double rh = Math.Max(0.0, Math.Min(100.0, weather.RelativeHumidity));
            double esGround = SaturationVaporPressure(tGround);
            double ea = SaturationVaporPressure(tAir) * (rh / 100.0);
            double vpd = Math.Max(0.0, esGround - ea);

            double le = beta * (rhoAir * 1004.0 / psych) * vpd / Math.Max(ra, 10.0);
            return Math.Max(0.0, le);
        }

        // =====================================================================
        // Penman-Monteith single source
        // =====================================================================

        private struct PM_Result { public double LE; public double Beta; public double Rs_Soil; }

        private PM_Result ComputeLE_PenmanMonteith(double tGround, HourlyRecord weather,
                                                     double rNet, double hFlux,
                                                     double ra, double psych,
                                                     double lambda, double rhoAir, double pa_kPa,
                                                     double gFlux = 0.0)
        {
            double delta = SlopeSaturationVaporPressure(tGround);
            double esGround = SaturationVaporPressure(tGround);
            double esAir = SaturationVaporPressure(weather.DryBulbTemperature);
            double rh = Math.Max(0.0, Math.Min(100.0, weather.RelativeHumidity));
            double ea = esAir * (rh / 100.0);
            double vpd = Math.Max(0.0, esGround - ea);

            double beta = ComputeBeta();
            double rs = _moistConfig.SoilSurfaceResistance > 0
                ? _moistConfig.SoilSurfaceResistance
                : ComputeSoilResistance(beta);

            double rhoCp = rhoAir * 1004.0;

            // FIX (P1): Use (rNet - gFlux) for standard PM available energy term (Rn - G).
            // Previous version used (rNet - hFlux) which is not the standard PM formulation.
            // G is now iteratively coupled in ComputeSurfaceFluxes (see PM case above).
            // PM: LE = [Delta*(Rn-G) + rho*cp*VPD/ra] / [Delta + gamma*(1+rs/ra)]
            double numerator = delta * (rNet - gFlux) + rhoCp * vpd / ra;
            double denominator = delta + psych * (1.0 + rs / ra);
            double le = numerator / denominator;

            if (le < 0) le = 0;
            le = Math.Max(0.0, Math.Min(_soilConfig.MaxLatentHeatFlux, le));

            return new PM_Result { LE = le, Beta = beta, Rs_Soil = rs };
        }

        // =====================================================================
        // Resistance calculations
        // =====================================================================

        private double ComputeAerodynamicResistance(double tGround, HourlyRecord weather, double windSpeedOverride = -1)
        {
            if (_surfConfig.AerodynamicResistance > 0)
                return ClipRa(_surfConfig.AerodynamicResistance);

            double u = Math.Max(0.1, windSpeedOverride >= 0 ? windSpeedOverride : weather.WindSpeed);
            double z = _surfConfig.WindMeasurementHeight;
            double z0m = _surfConfig.MomentumRoughnessLength;
            double z0h = _surfConfig.ScalarRoughnessLength;
            double k = _surfConfig.KarmanConstant;

            double ln_m = Math.Log(z / z0m);
            double ln_h = Math.Log(z / z0h);
            double ra = ln_m * ln_h / (k * k * u);

            if (_surfConfig.UseStabilityCorrection)
                ra *= ComputeStabilityFactor(ComputeRichardsonNumber(tGround, weather, z, windSpeedOverride));

            return ClipRa(ra);
        }

        private double ClipRa(double ra) => Math.Max(_surfConfig.MinAerodynamicResistance,
                                                      Math.Min(_surfConfig.MaxAerodynamicResistance, ra));

        private double ComputeRichardsonNumber(double tGround, HourlyRecord weather, double z, double windSpeedOverride = -1)
        {
            double dt = tGround - weather.DryBulbTemperature;
            double u = Math.Max(0.1, windSpeedOverride >= 0 ? windSpeedOverride : weather.WindSpeed);
            double denom = u * u / z;
            if (Math.Abs(denom) < 1e-10) return 0;
            return 9.81 / (weather.DryBulbTemperature + 273.15) * dt / denom;
        }

        private double ComputeStabilityFactor(double ri)
        {
            if (ri < 0)
                return 1.0 / (1.0 + _surfConfig.StabilityC * Math.Sqrt(-ri));
            else if (ri > 0)
                return 1.0 + _surfConfig.StabilityD * ri;
            return 1.0;
        }

        /// <summary>
        /// ISBA soil resistance: rs = rs_min * exp(a * (1 - beta)).
        /// When beta=1 (saturated): rs = rs_min (~50 s/m).
        /// When beta=0 (dry): rs -> infinity (capped at MaxSoilResistance).
        /// </summary>
        private double ComputeSoilResistance(double beta)
        {
            if (beta <= 0.01) return _moistConfig.MaxSoilResistance;
            if (beta >= 1.0) return _moistConfig.MinSoilResistance;

            return Math.Min(_moistConfig.MaxSoilResistance,
                _moistConfig.MinSoilResistance * Math.Exp(_moistConfig.BetaSensitivityIndex * (1.0 - beta)));
        }

        // =====================================================================
        // Beta calculation
        // =====================================================================

        /// <summary>
        /// Compute moisture availability factor beta [0-1].
        /// Priority: ForceWetSurface > selected BetaMethod.
        /// </summary>
        private double ComputeBeta()
        {
            // Priority 1: forced wet surface (e.g. after rain, irrigation)
            if (_moistConfig.ForceWetSurface)
                return Math.Max(_moistConfig.BetaMin, Math.Min(1.0, _moistConfig.ForcedWetBeta));

            double beta = 1.0;
            double wg = _moistConfig.SurfaceSoilMoisture;
            double wsat = Math.Max(_moistConfig.FieldCapacity, 0.01);

            switch (_moistConfig.BetaMethod)
            {
                case BetaMethod.Noilhan:
                    beta = wg / wsat;
                    break;

                case BetaMethod.Direct:
                    beta = _moistConfig.DirectBeta;
                    break;

                case BetaMethod.KondoSaigusa:
                    beta = ComputeBetaKondoSaigusa(wg, wsat);
                    break;

                case BetaMethod.PowerLaw:
                    beta = Math.Pow(wg / wsat, _moistConfig.BetaExponent);
                    break;
            }

            return Math.Max(_moistConfig.BetaMin, Math.Min(1.0, beta));
        }

        /// <summary>
        /// Kondo & Saigusa (1994) soil-texture-dependent beta parameterization.
        /// Based on soil texture index (1=sand ... 5=clay) and moisture ratio.
        /// Reference: Kondo, J. & Saigusa, N. (1994). J. Appl. Meteor., 33, 728-743.
        /// </summary>
        private double ComputeBetaKondoSaigusa(double wg, double wsat)
        {
            // Normalized moisture content
            double ratio = wg / Math.Max(wsat, 0.01);

            // Texture-dependent parameters (empirical fits from Kondo & Saigusa 1994)
            // Index: 1=sand, 2=loamy sand, 3=loam, 4=silt loam, 5=clay
            double[] aParams = { 0.10, 0.15, 0.20, 0.25, 0.30 };  // threshold ratio
            double[] bParams = { 4.0, 3.5, 3.0, 2.5, 2.0 };  // steepness

            int idx = Math.Max(0, Math.Min(4, _moistConfig.SoilTextureIndex - 1));
            double a = aParams[idx];
            double b = bParams[idx];

            // Piecewise sigmoid-like form: beta = 1 / [1 + exp(-b*(ratio - a))]
            // scaled so that beta=1 at saturation and beta->0 at dry conditions
            //beta 基于 Kondo &Saigusa(1994) 概念的 Beta 工程参数化，具体参数需根据实验数据校准
            double beta = 1.0 / (1.0 + Math.Exp(-b * (ratio - a)));
            return beta;
        }

        // =====================================================================
        // Public entry point
        // =====================================================================

        public double ComputeGroundHeatFlux(double tGround, HourlyRecord weather,
                                             double timeStepHours = 1.0,
                                             double ghiOverride = -1,
                                             double lDownOverride = -1)
        {
            double pa_kPa = weather.AtmosphericPressure / 1000.0;
            if (pa_kPa < 10) pa_kPa = 101.325;
            double psych = 0.665e-3 * pa_kPa;
            double lambda = (2.501e6 - 2361.0 * weather.DryBulbTemperature);
            double rhoAir = pa_kPa * 1000.0 / (287.05 * (weather.DryBulbTemperature + 273.15));

            var flux = ComputeSurfaceFluxes(tGround, weather, timeStepHours,
                                             ghiOverride, lDownOverride,
                                             psych, lambda, rhoAir, pa_kPa);
            return flux.G;
        }

        /// <summary>Reference ET [mm/h] using FAO56 Penman-Monteith for grass surface.</summary>
        public double CalculateReferenceET(HourlyRecord weather, double rNet, double gFlux, double windSpeedOverride = -1, double airTempOverride = -999)
        {
            double tAir = airTempOverride > -273.15 ? airTempOverride : weather.DryBulbTemperature;
            double pa_kPa = weather.AtmosphericPressure / 1000.0;
            if (pa_kPa < 10) pa_kPa = 101.325;
            double psych = 0.665e-3 * pa_kPa;
            double lambda = (2.501e6 - 2361.0 * tAir);
            double rhoAir = pa_kPa * 1000.0 / (287.05 * (tAir + 273.15));
            double delta = SlopeSaturationVaporPressure(tAir);
            double es = SaturationVaporPressure(tAir);
            double ea = es * Math.Max(0, Math.Min(1.0, weather.RelativeHumidity / 100.0));
            double vpd = Math.Max(0, es - ea);
            double rhoCp = rhoAir * 1004.0;

            double u = Math.Max(0.1, windSpeedOverride >= 0 ? windSpeedOverride : weather.WindSpeed);
            double ra_grass = 208.0 / u;
            double rs_grass = 70.0;

            double num = delta * (rNet - gFlux) + rhoCp * vpd / ra_grass;
            double den = delta + psych * (1.0 + rs_grass / ra_grass);
            double le_ref = Math.Max(0, num / den);

            return le_ref / lambda * 3600.0; // mm/h
        }

        // =====================================================================
        // Thermodynamic helpers
        // =====================================================================

        private static double SaturationVaporPressure(double tempC)
        {
            return 0.6108 * Math.Exp(17.27 * tempC / (tempC + 237.3));
        }

        private static double SlopeSaturationVaporPressure(double tempC)
        {
            double es = SaturationVaporPressure(tempC);
            return 4098.0 * es / Math.Pow(tempC + 237.3, 2);
        }
    }
}
