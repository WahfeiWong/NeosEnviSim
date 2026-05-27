using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Common.Core;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;

namespace SoilThermophysics
{
    /// <summary>
    /// Soil Temperature Settings component.
    /// Configures soil thermal properties (Force-Restore) and latent heat method selection.
    /// Supports flexible air temperature input to override EPW dry bulb temperature.
    ///
    /// Air temperature modes:
    ///   - FromEPW (default): Use EPW dry bulb temperature
    ///   - SingleValue: One constant for all points all hours
    ///   - PerPointConstant: Per-point constant (list length = point count)
    ///   - TimeSeries: 8760 hourly values shared by all points
    ///   - PerPointTimeSeries: Tree {nPts}[nHours], each point has its own time series
    ///
    /// Input order:
    ///   0 soilType  Soil type preset
    ///   1 rhoCp     Volumetric heat capacity
    ///   2 alb       Shortwave albedo
    ///   3 emiss     Longwave emissivity
    ///   4 leMeth    Latent heat method
    ///   5 leMax     Maximum latent heat flux
    ///   6 sub       Sub-steps per hour
    ///   7 AirTemp   Air temperature override (optional, multi-mode tree)
    ///   8 RH        Relative humidity override (optional, multi-mode tree)
    ///
    /// Output order:
    ///   0 SoilThermSet  Soil thermal configuration (includes air temperature settings)
    ///   1 Summary    Human-readable summary
    /// </summary>
    public class SoilTemperatureSettingsComponent : GH_Component
    {
        public SoilTemperatureSettingsComponent()
          : base("Soil Thermal Settings", "SoilThermSet",
              "Configures soil thermal properties (Force-Restore) " +
              "and latent heat method (Simplified, Penman-Monteith, or No Latent Heat). " +
              "Supports flexible air temperature and relative humidity input to override EPW values.",
              "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // ---- Soil type preset ----
            pManager.AddIntegerParameter("Soil Type", "SoilType",
                "Soil type: 0=DrySand, 1=WetSand, 2=DryClay, 3=WetClay, 4=AsphaltConcrete, 5=Custom",
                GH_ParamAccess.item, 2);

            // ---- Volumetric heat capacity (core parameter for Force-Restore) ----
            pManager.AddNumberParameter("Volumetric Heat Capacity", "rhoCp",
                "Volumetric heat capacity [MJ/(m3*K)]. Auto-set by soil type unless Custom.",
                GH_ParamAccess.item, 1.4);

            // ---- Surface radiation properties ----
            pManager.AddNumberParameter("Albedo", "alb",
                "Shortwave albedo [-]. 0.15-0.25 soil, 0.1 asphalt.",
                GH_ParamAccess.item, 0.2);

            pManager.AddNumberParameter("Emissivity", "emiss",
                "Longwave emissivity [-]. ~0.95 for natural surfaces.",
                GH_ParamAccess.item, 0.95);

            // ---- Latent heat method selection ----
            pManager.AddIntegerParameter("LE Method", "LeMethod",
                "Latent heat method: 0=Simplified, 1=PenmanMonteith, 2=NoLatentHeat.",
                GH_ParamAccess.item, 1);

            pManager.AddNumberParameter("Max LE", "leMax",
                "Maximum latent heat flux [W/m2]. Default 1000.",
                GH_ParamAccess.item, 1000.0);

            pManager.AddIntegerParameter("Sub-steps", "sub",
                "Sub-steps per hour. Default 10.", GH_ParamAccess.item, 10);

            // ---- Air temperature input (optional, last, supports multi-mode) ----
            pManager.AddNumberParameter("Air Temperature", "AirTemp",
                "Optional: air temperature input [C] to override EPW dry bulb temperature. " +
                "Supports multiple modes:\n" +
                "- Single value: all points, all hours\n" +
                "- Multiple values (count = nPts): per-point constant\n" +
                "- 8760 values: hourly time series (all points share)\n" +
                "- Tree {nPts}[nHours]: per-point per-hour time series\n" +
                "Leave empty to use EPW air temperature (default).",
                GH_ParamAccess.tree);
            pManager[7].Optional = true;

            // ---- Relative humidity input (optional, last, supports multi-mode) ----
            pManager.AddNumberParameter("Relative Humidity", "RH",
                "Optional: relative humidity input [%] to override EPW RH. " +
                "When AirTemp is overridden, also overriding RH ensures physical consistency " +
                "between temperature, humidity, and vapor pressure deficit (VPD). " +
                "Supports the same multi-mode input structure as AirTemp:" +
                "- Single value: all points, all hours" +
                "- Multiple values (count = nPts): per-point constant" +
                "- 8760 values: hourly time series (all points share)" +
                "- Tree {nPts}[nHours]: per-point per-hour time series." +
                "\nLeave empty to use EPW relative humidity (default).",
                GH_ParamAccess.tree);
            pManager[8].Optional = true;

            for (int i = 0; i < 8; i++)
                pManager[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Thermal Settings", "SoilThermSet",
                "Soil thermal configuration (includes air temperature and RH settings)", GH_ParamAccess.item);
            pManager.AddTextParameter("Summary", "Sum",
                "Parameter summary", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int soilType = 2;
            double rhoCp = 1.4;
            double alb = 0.2, emiss = 0.95;
            int leMethod = 0, subSteps = 10;
            double leMax = 1000.0;

            DA.GetData(0, ref soilType);
            DA.GetData(1, ref rhoCp);
            DA.GetData(2, ref alb);
            DA.GetData(3, ref emiss);
            DA.GetData(4, ref leMethod);
            DA.GetData(5, ref leMax);
            DA.GetData(6, ref subSteps);

            // Clamp
            soilType = Math.Max(0, Math.Min(5, soilType));
            alb = Math.Max(0.0, Math.Min(1.0, alb));
            emiss = Math.Max(0.0, Math.Min(1.0, emiss));
            leMethod = Math.Max(0, Math.Min(2, leMethod));
            subSteps = Math.Max(1, Math.Min(60, subSteps));
            leMax = Math.Max(0, Math.Min(2000, leMax));

            var sType = (SoilType)soilType;
            var lhMethod = (LatentHeatMethod)leMethod;
            string[] typeNames = { "DrySand", "WetSand", "DryClay", "WetClay", "Asphalt", "Custom" };
            string[] lhNames = { "Simplified", "PenmanMonteith", "NoLatentHeat" };

            var config = new SoilThermalConfig
            {
                SoilType = sType,
                VolumetricHeatCapacity = rhoCp,
                SurfaceAlbedo = alb,
                SurfaceEmissivity = emiss,
                LatentHeatMethod = lhMethod,
                MaxLatentHeatFlux = leMax,
                SubStepsPerHour = subSteps
                // ThermalConductivity and ThermalDiffusivity are retained as internal variables,
                // auto-set by ApplyPreset() based on soil type. Not exposed as inputs since
                // they do not participate in the current Force-Restore calculation.
            };

            config.ApplyPreset();

            // ---- Parse air temperature input (port 7) ----
            AirTemperatureConfig airTempConfig = ParseAirTemperatureInput(DA, 7);

            string airTempModeDesc = airTempConfig.Mode == AirTemperatureMode.FromEPW ? "EPW (auto)"
                : airTempConfig.Mode == AirTemperatureMode.SingleValue ? $"Single={airTempConfig.SingleAirTemperature:F1}C"
                : airTempConfig.Mode == AirTemperatureMode.PerPointConstant ? $"PerPoint ({airTempConfig.PerPointAirTemperature.Count} pts)"
                : airTempConfig.Mode == AirTemperatureMode.TimeSeries ? $"TimeSeries ({airTempConfig.TimeSeriesAirTemperature.Count} hrs)"
                : $"PerPointTimeSeries ({airTempConfig.PerPointTimeSeriesAirTemperature.Count} pts)";

            config.AirTemperatureConfig = airTempConfig;

            // ---- Parse relative humidity input (port 8) ----
            RelativeHumidityConfig rhConfig = ParseRelativeHumidityInput(DA, 8);

            string rhModeDesc = rhConfig.Mode == RelativeHumidityMode.FromEPW ? "EPW (auto)"
                : rhConfig.Mode == RelativeHumidityMode.SingleValue ? $"Single={rhConfig.SingleRelativeHumidity:F1}%"
                : rhConfig.Mode == RelativeHumidityMode.PerPointConstant ? $"PerPoint ({rhConfig.PerPointRelativeHumidity.Count} pts)"
                : rhConfig.Mode == RelativeHumidityMode.TimeSeries ? $"TimeSeries ({rhConfig.TimeSeriesRelativeHumidity.Count} hrs)"
                : $"PerPointTimeSeries ({rhConfig.PerPointTimeSeriesRelativeHumidity.Count} pts)";

            config.RelativeHumidityConfig = rhConfig;

            string summary = $"=== Soil Temperature Settings ===\n" +
                $"Soil Type ............ {typeNames[soilType]}\n" +
                $"Heat Capacity ........ {config.VolumetricHeatCapacity:F2} MJ/(m3*K)\n" +
                $"Albedo ............... {config.SurfaceAlbedo:F2}\n" +
                $"Emissivity ........... {config.SurfaceEmissivity:F2}\n" +
                $"LE Method ............ {lhNames[leMethod]}\n" +
                $"Max LE ............... {leMax:F0} W/m2\n" +
                $"Sub-steps/hour ....... {subSteps}\n" +
                $"Air Temperature ...... {airTempModeDesc}\n" +
                $"Relative Humidity .... {rhModeDesc}";

            DA.SetData(0, new GH_ObjectWrapper(config));
            DA.SetData(1, summary);
        }

        /// <summary>
        /// Parse air temperature input from Grasshopper tree or list.
        /// Automatically determines mode based on data structure.
        /// </summary>
        private AirTemperatureConfig ParseAirTemperatureInput(IGH_DataAccess DA, int paramIndex)
        {
            GH_Structure<GH_Number> tree;
            if (!DA.GetDataTree(paramIndex, out tree) || tree == null || tree.IsEmpty)
            {
                return new AirTemperatureConfig { Mode = AirTemperatureMode.FromEPW };
            }

            var branches = tree.Branches;
            int totalValues = 0;
            foreach (var branch in branches)
            {
                if (branch != null)
                    totalValues += branch.Count;
            }

            // Case 1: Single value (flat tree with 1 value)
            if (totalValues == 1)
            {
                return new AirTemperatureConfig
                {
                    Mode = AirTemperatureMode.SingleValue,
                    SingleAirTemperature = tree.get_FirstItem(true).Value
                };
            }

            // Case 2: TimeSeries - single branch with ~8760 values (all points share).
            // Threshold 8000 avoids overlap with PerPointConstant (typical spatial point count).
            if (branches.Count == 1 && branches[0] != null && branches[0].Count >= 8000)
            {
                var series = new List<double>();
                foreach (var val in branches[0])
                    series.Add(val.Value);
                return new AirTemperatureConfig
                {
                    Mode = AirTemperatureMode.TimeSeries,
                    TimeSeriesAirTemperature = series
                };
            }

            // Case 3: PerPointConstant - single branch with values (count = nPts, typically 2..7999).
            if (branches.Count == 1 && branches[0] != null)
            {
                var perPoint = new List<double>();
                foreach (var val in branches[0])
                    perPoint.Add(val.Value);
                return new AirTemperatureConfig
                {
                    Mode = AirTemperatureMode.PerPointConstant,
                    PerPointAirTemperature = perPoint
                };
            }

            // Case 4: PerPointTimeSeries - multiple branches, each with time series
            if (branches.Count > 1)
            {
                var perPointSeries = new List<List<double>>();
                foreach (var branch in branches)
                {
                    var series = new List<double>();
                    if (branch != null)
                    {
                        foreach (var val in branch)
                            series.Add(val.Value);
                    }
                    perPointSeries.Add(series);
                }
                return new AirTemperatureConfig
                {
                    Mode = AirTemperatureMode.PerPointTimeSeries,
                    PerPointTimeSeriesAirTemperature = perPointSeries
                };
            }

            return new AirTemperatureConfig { Mode = AirTemperatureMode.FromEPW };
        }

        /// <summary>
        /// Parse relative humidity input from Grasshopper tree or list.
        /// Automatically determines mode based on data structure.
        /// Same multi-mode logic as ParseAirTemperatureInput.
        /// </summary>
        private RelativeHumidityConfig ParseRelativeHumidityInput(IGH_DataAccess DA, int paramIndex)
        {
            GH_Structure<GH_Number> tree;
            if (!DA.GetDataTree(paramIndex, out tree) || tree == null || tree.IsEmpty)
            {
                return new RelativeHumidityConfig { Mode = RelativeHumidityMode.FromEPW };
            }

            var branches = tree.Branches;
            int totalValues = 0;
            foreach (var branch in branches)
            {
                if (branch != null)
                    totalValues += branch.Count;
            }

            // Case 1: Single value (flat tree with 1 value)
            if (totalValues == 1)
            {
                return new RelativeHumidityConfig
                {
                    Mode = RelativeHumidityMode.SingleValue,
                    SingleRelativeHumidity = tree.get_FirstItem(true).Value
                };
            }

            // Case 2: TimeSeries - single branch with ~8760 values (all points share).
            // Threshold 8000 avoids overlap with PerPointConstant (typical spatial point count).
            if (branches.Count == 1 && branches[0] != null && branches[0].Count >= 8000)
            {
                var series = new List<double>();
                foreach (var val in branches[0])
                    series.Add(val.Value);
                return new RelativeHumidityConfig
                {
                    Mode = RelativeHumidityMode.TimeSeries,
                    TimeSeriesRelativeHumidity = series
                };
            }

            // Case 3: PerPointConstant - single branch with values (count = nPts, typically 2..7999).
            if (branches.Count == 1 && branches[0] != null)
            {
                var perPoint = new List<double>();
                foreach (var val in branches[0])
                    perPoint.Add(val.Value);
                return new RelativeHumidityConfig
                {
                    Mode = RelativeHumidityMode.PerPointConstant,
                    PerPointRelativeHumidity = perPoint
                };
            }

            // Case 4: PerPointTimeSeries - multiple branches, each with time series
            if (branches.Count > 1)
            {
                var perPointSeries = new List<List<double>>();
                foreach (var branch in branches)
                {
                    var series = new List<double>();
                    if (branch != null)
                    {
                        foreach (var val in branch)
                            series.Add(val.Value);
                    }
                    perPointSeries.Add(series);
                }
                return new RelativeHumidityConfig
                {
                    Mode = RelativeHumidityMode.PerPointTimeSeries,
                    PerPointTimeSeriesRelativeHumidity = perPointSeries
                };
            }

            return new RelativeHumidityConfig { Mode = RelativeHumidityMode.FromEPW };
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_groundTempSet;
        public override Guid ComponentGuid => new Guid("7A903B7A-3C08-4540-999C-52A7C4DE78A1");
    }
}
