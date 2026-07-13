using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Common.Core;
using SoilThermophysics.Core;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SoilThermophysics
{
    /// <summary>
    /// Soil Thermal Simulator - Core simulation component.
    /// 
    /// Performs hour-by-hour soil temperature and latent heat flux calculation
    /// using Force-Restore + Penman-Monteith (single-source).
    /// Saves results to categorized files in the output folder.
    /// 
    /// Architecture:
    ///   Inputs:  3 Config objects + EPW path + Point + Run toggle + OutputFolder
    ///   Output:  Result object + saves to categorized files
    /// </summary>
    public class SoilThermalSimulatorComponent : GH_Component
    {
        public SoilThermalSimulatorComponent()
          : base("Soil Thermal Simulator", "SoilSim",
              "Hourly soil temperature and latent heat flux simulation " +
              "using Force-Restore + Penman-Monteith single-source. Saves results to output folder.",
              "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Thermal Set", "SoilThermSet",
                "Soil thermal configuration (from Soil Thermal Settings).", GH_ParamAccess.item);

            pManager.AddGenericParameter("Soil Surface Set", "SoilSurSet",
                "Surface aerodynamic configuration (from Soil Surface Settings). Optional.",
                GH_ParamAccess.item);
            pManager[1].Optional = true;

            pManager.AddGenericParameter("Soil Moisture Set", "SoilMoistSet",
                "Soil moisture/beta configuration (from Soil Moisture Settings). Optional.",
                GH_ParamAccess.item);
            pManager[2].Optional = true;

            pManager.AddTextParameter("EPW Path", "EPW",
                "Path to EPW weather file.", GH_ParamAccess.item);

            // ---- Optional: Layer depths (override SoilThSet defaults) ----
            pManager.AddNumberParameter("Top Layer", "d1",
                "Top soil layer thickness [m] overriding SoilThSet value. " +
                "Default 0.05. Only used in non-spatial mode (SpatialSim uses GroundSet d1).",
                GH_ParamAccess.item, 0.05);
            pManager[4].Optional = true;

            pManager.AddNumberParameter("Deep Layer", "d2",
                "Deep soil layer thickness [m] overriding SoilThSet value. " +
                "Default 0.5. Only used in non-spatial mode (SpatialSim uses GroundSet d2).",
                GH_ParamAccess.item, 0.5);
            pManager[5].Optional = true;

            pManager.AddTextParameter("Output Folder", "Folder",
                "Folder for saving results. Files: SoilTempResult.txt, LEFluxResult.txt, " +
                "ETResult.txt, EnergyBalanceResult.txt",
                GH_ParamAccess.item);
            pManager[6].Optional = true;

            pManager.AddBooleanParameter("Run", "Run",
                "Set to true to execute the simulation.", GH_ParamAccess.item, false);

        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Result", "Result",
                "Simulation result for downstream Read* components", GH_ParamAccess.item);
            pManager.AddTextParameter("Log", "Log",
                "Execution log and summary", GH_ParamAccess.list);
            pManager.AddNumberParameter("Annual ET", "ETann",
                "Total annual ET [mm]", GH_ParamAccess.item);
            pManager.AddNumberParameter("Annual Tg Mean", "TgMean",
                "Mean annual ground temperature [C]", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var logs = new List<string>();

            try
            {
                // ---- Check Run toggle ----
                bool run = false;
                DA.GetData(7, ref run);
                if (!run)
                {
                    logs.Add("Simulation paused. Set 'Run' to true to execute.");
                    DA.SetDataList(1, logs);
                    return;
                }

                // ---- Extract Configs ----
                var soilConfig = ExtractConfig<SoilThermalConfig>(DA, 0);
                var surfConfig = ExtractConfig<SoilSurfaceConfig>(DA, 1) ?? new SoilSurfaceConfig();
                var moistConfig = ExtractConfig<SoilMoistureConfig>(DA, 2) ?? new SoilMoistureConfig();

                if (soilConfig == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "Soil Thermal Settings is required.");
                    return;
                }

                logs.Add($"=== Soil Thermal Simulator ===");
                logs.Add($"LE Method: {soilConfig.LatentHeatMethod}");
                if (soilConfig.AirTemperatureConfig != null && soilConfig.AirTemperatureConfig.Mode != AirTemperatureMode.FromEPW)
                {
                    var atc = soilConfig.AirTemperatureConfig;
                    string airDesc = atc.Mode == AirTemperatureMode.SingleValue
                        ? $"single={atc.SingleAirTemperature:F1}C"
                        : atc.Mode == AirTemperatureMode.PerPointConstant
                        ? $"per-pt-const ({atc.PerPointAirTemperature.Count}pts)"
                        : atc.Mode == AirTemperatureMode.TimeSeries
                        ? $"time-series ({atc.TimeSeriesAirTemperature.Count}hrs)"
                        : $"per-pt-series ({atc.PerPointTimeSeriesAirTemperature.Count}pts)";
                    logs.Add($"Air temp override: {airDesc}");
                }
                else
                {
                    logs.Add("Air temperature: using EPW (auto)");
                }
                if (soilConfig.RelativeHumidityConfig != null && soilConfig.RelativeHumidityConfig.Mode != RelativeHumidityMode.FromEPW)
                {
                    var rhc = soilConfig.RelativeHumidityConfig;
                    string rhDesc = rhc.Mode == RelativeHumidityMode.SingleValue
                        ? $"single={rhc.SingleRelativeHumidity:F1}%"
                        : rhc.Mode == RelativeHumidityMode.PerPointConstant
                        ? $"per-pt-const ({rhc.PerPointRelativeHumidity.Count}pts)"
                        : rhc.Mode == RelativeHumidityMode.TimeSeries
                        ? $"time-series ({rhc.TimeSeriesRelativeHumidity.Count}hrs)"
                        : $"per-pt-series ({rhc.PerPointTimeSeriesRelativeHumidity.Count}pts)";
                    logs.Add($"Relative humidity override: {rhDesc}");
                }
                else
                {
                    logs.Add("Relative humidity: using EPW (auto)");
                }

                // ---- Load EPW ----
                string epwPath = "";
                if (!DA.GetData(3, ref epwPath)) return;
                if (!File.Exists(epwPath))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"EPW file not found: {epwPath}");
                    return;
                }

                EPWData epwData;
                try
                {
                    epwData = new EPWData(epwPath);
                    logs.Add($"EPW: {epwData.City}, Hours={epwData.HourCount}, " +
                             $"Lat={epwData.Latitude:F2}");
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Failed to parse EPW: {ex.Message}");
                    return;
                }

                // ---- Output Folder ----
                string folder = "";
                if (!DA.GetData(6, ref folder)) return;
                if (!Directory.Exists(folder))
                {
                    try { Directory.CreateDirectory(folder); }
                    catch (Exception ex)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Cannot create folder: {ex.Message}");
                        return;
                    }
                }

                // ---- Optional: Override d1/d2 from direct inputs ----
                double d1_override = 0.05, d2_override = 0.5;
                if (DA.GetData(4, ref d1_override))
                    soilConfig.TopLayerDepth = Math.Max(0.001, Math.Min(1.0, d1_override));
                if (DA.GetData(5, ref d2_override))
                    soilConfig.DeepLayerDepth = Math.Max(0.001, Math.Min(5.0, d2_override));

                // ---- Initialize Model ----
                var model = new SoilTemperatureModel(soilConfig, surfConfig, moistConfig)
                {
                    SubStepsPerHour = soilConfig.SubStepsPerHour,
                    EnergyBalanceTolerance = soilConfig.EnergyBalanceTolerance,
                    MaxIterations = soilConfig.MaxIterations
                };

                double firstAirT = epwData.GetHour(0).DryBulbTemperature;
                double annualMean = EstimateAnnualMean(epwData);
                model.Initialize(firstAirT, annualMean);

                logs.Add($"Initial Tg: {model.T1:F2}C, T2: {model.T2:F2}C");

                // ---- Time-stepping ----
                int nHours = epwData.HourCount;
                double timeStepHours = epwData.TimeStepHours;

                var result = new SoilThermalSimulationResult
                {
                    EPWCity = epwData.City,
                    EPWCountry = epwData.Country,
                    Latitude = epwData.Latitude,
                    Longitude = epwData.Longitude,
                    TimeZone = epwData.TimeZone,
                    Elevation = epwData.Elevation,
                    AnalyzedHours = nHours,
                    TotalPoints = 1,
                    IsSpatial = false,
                    LatentHeatMethodUsed = soilConfig.LatentHeatMethod
                };

                var tgList = new List<double>(nHours);
                var t2List = new List<double>(nHours);
                var leList = new List<double>(nHours);
                var betaList = new List<double>(nHours);
                var raList = new List<double>(nHours);
                var rsSoilList = new List<double>(nHours);
                var rnList = new List<double>(nHours);
                var hList = new List<double>(nHours);
                var gList = new List<double>(nHours);
                var etList = new List<double>(nHours);
                var etRefList = new List<double>(nHours);

                for (int hoy = 0; hoy < nHours; hoy++)
                {
                    var rec = epwData.GetHour(hoy);
                    // Get wind speed override from SoilSurfaceConfig (if configured)
                    double windOverride = (surfConfig.WindSpeedConfig != null
                        && surfConfig.WindSpeedConfig.Mode != WindSpeedMode.FromEPW)
                        ? surfConfig.WindSpeedConfig.GetWindSpeed(0, hoy, rec.WindSpeed)
                        : -1;
                    // Get air temperature override from SoilThermalConfig (if configured)
                    double airTempOverride = (soilConfig.AirTemperatureConfig != null
                        && soilConfig.AirTemperatureConfig.Mode != AirTemperatureMode.FromEPW)
                        ? soilConfig.AirTemperatureConfig.GetAirTemperature(0, hoy, rec.DryBulbTemperature)
                        : -999;
                    // Get relative humidity override from SoilThermalConfig (if configured)
                    double rhOverride = (soilConfig.RelativeHumidityConfig != null
                        && soilConfig.RelativeHumidityConfig.Mode != RelativeHumidityMode.FromEPW)
                        ? soilConfig.RelativeHumidityConfig.GetRelativeHumidity(0, hoy, rec.RelativeHumidity)
                        : -1;
                    model.Step(rec, timeStepHours, -1, -1, windOverride, airTempOverride, rhOverride);

                    tgList.Add(model.T1);
                    t2List.Add(model.T2);
                    leList.Add(model.LastLatentHeat);
                    betaList.Add(model.LastBeta);
                    raList.Add(model.LastRa);
                    rsSoilList.Add(model.LastRsSoil);
                    rnList.Add(model.LastNetRadiation);
                    hList.Add(model.LastSensibleHeat);
                    gList.Add(model.LastGroundHeatFlux);

                    // Convert LE [W/m2] to ET [mm/h]
                    // Use temperature-corrected latent heat of vaporization for accurate conversion
                    double lambdaT = (2.501e6 - 2361.0 * rec.DryBulbTemperature);
                    double et_mmh = model.LastLatentHeat / lambdaT * 3600.0;
                    etList.Add(et_mmh);

                    // Reference ET (FAO56 grass)
                    double etRef = model.CalculateReferenceET(rec, model.LastNetRadiation, model.LastGroundHeatFlux, windOverride, airTempOverride, rhOverride);
                    etRefList.Add(etRef);

                    if (hoy > 0 && hoy % 1000 == 0)
                        logs.Add($"  Progress: {hoy}/{nHours} hours...");
                }

                // Assemble result
                result.SoilTemp.HourlyGroundTemperature.Add(tgList);
                result.SoilTemp.HourlyDeepTemperature.Add(t2List);
                result.SoilTemp.AnnualMeanTemperature.Add(tgList.Average());

                result.LEFlux.HourlyLE.Add(leList);
                result.LEFlux.HourlyBeta.Add(betaList);
                result.LEFlux.HourlyRa.Add(raList);
                result.LEFlux.HourlyRsSoil.Add(rsSoilList);

                result.ET.HourlyET.Add(etList);
                result.ET.HourlyReferenceET.Add(etRefList);
                result.ET.TotalAnnualET.Add(etList.Sum());

                result.EnergyBalance.HourlyNetRadiation.Add(rnList);
                result.EnergyBalance.HourlySensibleHeat.Add(hList);
                result.EnergyBalance.HourlyLatentHeat.Add(leList);
                result.EnergyBalance.HourlyGroundHeat.Add(gList);

                result.SaveToFolder(folder);
                logs.Add($"Results saved to: {folder}");

                double annualET = etList.Sum();
                double tgMean = tgList.Average();
                logs.Add($"=== Summary ===");
                logs.Add($"Mean Tg: {tgMean:F2} C, Range: [{tgList.Min():F1}, {tgList.Max():F1}]");
                logs.Add($"Mean LE: {leList.Average():F2} W/m2, Max LE: {leList.Max():F2} W/m2");

                DA.SetData(0, new GH_ObjectWrapper(result));
                DA.SetDataList(1, logs);
                DA.SetData(2, annualET);
                DA.SetData(3, tgMean);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Simulation error: {ex.Message}");
                logs.Add($"ERROR: {ex.Message}");
                if (ex.InnerException != null)
                    logs.Add($"  Inner: {ex.InnerException.Message}");
                DA.SetDataList(1, logs);
            }
        }

        private T ExtractConfig<T>(IGH_DataAccess DA, int index) where T : class
        {
            GH_ObjectWrapper wrapper = null;
            if (!DA.GetData(index, ref wrapper) || wrapper?.Value == null) return null;
            return wrapper.Value as T;
        }

        private double EstimateAnnualMean(EPWData epw)
        {
            // FIX (P2): Use full-year average (all EPW hours) instead of first 168 hours.
            // Previous version used only the first week (Jan 1-7), causing systematic
            // cold bias for Northern Hemisphere sites (~5-15°C error).
            int samples = epw.HourCount;
            if (samples == 0) return 15.0;
            double sum = 0.0;
            for (int i = 0; i < samples; i++)
                sum += epw.GetHour(i).DryBulbTemperature;
            return sum / samples;
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_soilTempModel;
        public override Guid ComponentGuid => new Guid("C5A9CF7E-89FC-4960-8B0E-170F43F50455");
    }
}

