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
    /// Soil Surface Settings component.
    /// Configures aerodynamic and radiation surface properties for Penman-Monteith ET calculations,
    /// and wind speed input with flexible multi-mode support.
    ///
    /// Wind speed modes:
    ///   - FromEPW (default): Use EPW wind speed
    ///   - SingleValue: One constant for all points all hours
    ///   - PerPointConstant: Per-point constant (list length = point count)
    ///   - TimeSeries: 8760 hourly values shared by all points
    ///   - PerPointTimeSeries: Tree {nPts}[nHours], each point has its own time series
    ///
    /// Input order (by category, wind speed last as optional):
    ///   0 z_obs     Wind measurement height      (aerodynamic base)
    ///   1 z0m       Momentum roughness length    (roughness)
    ///   2 z0h       Scalar roughness length      (roughness)
    ///   3 k         Von Karman constant          (aerodynamic base)
    ///   4 OverrideRa Direct aerodynamic resistance (ra control)
    ///   5 RaMax     Maximum ra bound             (ra bounds)
    ///   6 RaMin     Minimum ra bound             (ra bounds)
    ///   7 Stab      Stability correction toggle  (ra control)
    ///   8 Wind      Wind speed override          (optional, multi-mode)
    ///
    /// Output order:
    ///   0 SoilSurfSet  Surface aerodynamic configuration
    ///   2 Summary      Human-readable summary
    /// </summary>
    public class SoilSurfaceSettingsComponent : GH_Component
    {
        public SoilSurfaceSettingsComponent()
          : base("Soil Surface Settings", "SoilSurSet",
              "Configures aerodynamic roughness, wind measurement height, " +
              "stability correction, and flexible wind speed input for Penman-Monteith ET.",
              "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // ---- Aerodynamic base parameters (contiguous) ----
            pManager.AddNumberParameter("Wind Height", "z_obs",
                "Wind measurement height [m]. FAO56 standard = 2.0 m.",
                GH_ParamAccess.item, 2.0);

            // ---- Roughness parameters (contiguous) ----
            pManager.AddNumberParameter("Momentum z0", "z0m",
                "Momentum roughness length [m]. Bare soil = 0.001, short grass = 0.01, urban = 0.1-1.0.",
                GH_ParamAccess.item, 0.001);

            pManager.AddNumberParameter("Scalar z0", "z0h",
                "Scalar roughness length for heat/vapor [m]. Typically z0m/10 = 0.0001 for bare soil.",
                GH_ParamAccess.item, 0.0001);

            // ---- Aerodynamic constant ----
            pManager.AddNumberParameter("Karman", "k",
                "Von Karman constant [-]. Standard value 0.41.",
                GH_ParamAccess.item, 0.41);

            // ---- Ra control parameters (contiguous) ----
            pManager.AddNumberParameter("Override Ra", "Ra",
                "Direct aerodynamic resistance input [s/m]. If > 0, overrides log-law calculation. " +
                "Use -1 to enable automatic calculation from wind/roughness.",
                GH_ParamAccess.item, -1.0);

            // ---- Ra bounds (contiguous) ----
            pManager.AddNumberParameter("Max Ra", "RaMax",
                "Maximum allowed aerodynamic resistance [s/m]. Default 500.",
                GH_ParamAccess.item, 500.0);

            pManager.AddNumberParameter("Min Ra", "RaMin",
                "Minimum allowed aerodynamic resistance [s/m]. Default 10.",
                GH_ParamAccess.item, 10.0);

            // ---- Stability correction (ra control, after bounds) ----
            pManager.AddBooleanParameter("Stability Corr", "Stab",
                "Enable Louis (1979) atmospheric stability correction for ra. " +
                "Recommended for extreme temperature differences (hot dry surfaces).",
                GH_ParamAccess.item, false);

            // ---- Wind speed input (optional, last, supports multi-mode) ----
            pManager.AddNumberParameter("Wind Speed", "Wind",
                "Optional: wind speed input [m/s]. Supports multiple modes:\n" +
                "- Single value: all points, all hours\n" +
                "- Multiple values (count = nPts): per-point constant\n" +
                "- 8760 values: hourly time series (all points share)\n" +
                "- Tree {nPts}[nHours]: per-point per-hour time series\n" +
                "Leave empty to use EPW wind speed (default).",
                GH_ParamAccess.tree);
            pManager[8].Optional = true;

            for (int i = 0; i < 8; i++)
                pManager[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Soil Surface Settings", "SoilSurSet",
                "Encapsulated surface aerodynamic configuration (includes wind speed settings)", GH_ParamAccess.item);
            pManager.AddTextParameter("Summary", "Sum",
                "Human-readable parameter summary", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ---- Read inputs in new port order ----
            double zObs = 2.0;
            DA.GetData(0, ref zObs);

            double z0m = 0.001;
            DA.GetData(1, ref z0m);

            double z0h = 0.0001;
            DA.GetData(2, ref z0h);

            double karman = 0.41;
            DA.GetData(3, ref karman);

            double overrideRa = -1.0;
            DA.GetData(4, ref overrideRa);

            double raMax = 500.0;
            DA.GetData(5, ref raMax);

            double raMin = 10.0;
            DA.GetData(6, ref raMin);

            bool useStability = false;
            DA.GetData(7, ref useStability);

            // Clamp
            zObs = Math.Max(0.1, Math.Min(100.0, zObs));
            z0m = Math.Max(1e-6, Math.Min(10.0, z0m));
            z0h = Math.Max(1e-7, Math.Min(1.0, z0h));
            karman = Math.Max(0.3, Math.Min(0.5, karman));
            raMax = Math.Max(50.0, Math.Min(5000.0, raMax));
            raMin = Math.Max(1.0, Math.Min(raMax - 1.0, raMin));

            var config = new SoilSurfaceConfig
            {
                WindMeasurementHeight = zObs,
                MomentumRoughnessLength = z0m,
                ScalarRoughnessLength = z0h,
                KarmanConstant = karman,
                AerodynamicResistance = overrideRa,
                MaxAerodynamicResistance = raMax,
                MinAerodynamicResistance = raMin,
                UseStabilityCorrection = useStability
            };

            // ---- Parse wind speed input (port 8) ----
            WindSpeedConfig windConfig = ParseWindSpeedInput(DA, 8);

            string windModeDesc = windConfig.Mode == WindSpeedMode.FromEPW ? "EPW (auto)"
                : windConfig.Mode == WindSpeedMode.SingleValue ? $"Single={windConfig.SingleWindSpeed:F2} m/s"
                : windConfig.Mode == WindSpeedMode.PerPointConstant ? $"PerPoint ({windConfig.PerPointWindSpeed.Count} pts)"
                : windConfig.Mode == WindSpeedMode.TimeSeries ? $"TimeSeries ({windConfig.TimeSeriesWindSpeed.Count} hrs)"
                : $"PerPointTimeSeries ({windConfig.PerPointTimeSeriesWindSpeed.Count} pts)";

            string summary = $"== Soil Surface Settings ==\n" +
                $"Wind Height .......... {zObs:F2} m\n" +
                $"Momentum z0 .......... {z0m:F4} m\n" +
                $"Scalar z0 ............ {z0h:F5} m\n" +
                $"Karman ............... {karman:F3}\n" +
                $"Override Ra .......... {(overrideRa > 0 ? overrideRa.ToString("F1") + " s/m" : "Auto")}\n" +
                $"Ra Range ............. [{raMin:F0}, {raMax:F0}] s/m\n" +
                $"Stability Correct .... {useStability}\n" +
                $"Wind Speed ........... {windModeDesc}";

            // Encapsulate wind speed config into SoilSurfaceConfig
            // (eliminates separate WindCfg output port, simplifying downstream connections)
            config.WindSpeedConfig = windConfig;

            DA.SetData(0, new GH_ObjectWrapper(config));
            DA.SetData(1, summary);
        }

        /// <summary>
        /// Parse wind speed input from Grasshopper tree or list.
        /// Automatically determines mode based on data structure.
        /// </summary>
        private WindSpeedConfig ParseWindSpeedInput(IGH_DataAccess DA, int paramIndex)
        {
            GH_Structure<GH_Number> tree;
            if (!DA.GetDataTree(paramIndex, out tree) || tree == null || tree.IsEmpty)
            {
                return new WindSpeedConfig { Mode = WindSpeedMode.FromEPW };
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
                return new WindSpeedConfig
                {
                    Mode = WindSpeedMode.SingleValue,
                    SingleWindSpeed = tree.get_FirstItem(true).Value
                };
            }

            // Case 2: TimeSeries - single branch with ~8760 values (all points share).
            // Threshold 8000 avoids overlap with PerPointConstant (typical spatial point count).
            if (branches.Count == 1 && branches[0] != null && branches[0].Count >= 8000)
            {
                var series = new List<double>();
                foreach (var val in branches[0])
                    series.Add(val.Value);
                return new WindSpeedConfig
                {
                    Mode = WindSpeedMode.TimeSeries,
                    TimeSeriesWindSpeed = series
                };
            }

            // Case 3: PerPointConstant - single branch with values (count = nPts, typically 2..7999).
            if (branches.Count == 1 && branches[0] != null)
            {
                var perPoint = new List<double>();
                foreach (var val in branches[0])
                    perPoint.Add(val.Value);
                return new WindSpeedConfig
                {
                    Mode = WindSpeedMode.PerPointConstant,
                    PerPointWindSpeed = perPoint
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
                return new WindSpeedConfig
                {
                    Mode = WindSpeedMode.PerPointTimeSeries,
                    PerPointTimeSeriesWindSpeed = perPointSeries
                };
            }

            return new WindSpeedConfig { Mode = WindSpeedMode.FromEPW };
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_groundSurSet;
        public override Guid ComponentGuid => new Guid("49E5BC2D-8CB2-4F42-836E-1F5CE2579AD1");
    }
}
