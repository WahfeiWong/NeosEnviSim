using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SoilThermophysics.Core;
using Common.Core;
using ThermalComfort.Core;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SoilThermophysics
{
    /// <summary>
    /// Spatial Soil Thermal Simulator - Multi-point version with per-point radiation correction.
    ///
    /// Performs complete spatial radiation processing:
    ///   1. SPA solar position pre-computation (hourly azimuth/zenith)
    ///   2. Sky View Factor (SVF) calculation from obstacles
    ///   3. Solar exposure factor batch detection (ground-level shadow tracing per point per hour)
    ///      - Uses human exposure core methodology with bodyHeight=0, sampleCount=1
    ///   4. Shortwave separation &amp; correction:
    ///        GHI_actual = DHI * SVF + DNI * sin(alt) * Exposure + rho_sur * GHI_sur * (1-SVF)
    ///   5. Longwave separation &amp; correction:
    ///        L_actual = L_sky * SVF + epsilon_sur * sigma * (T_obs^4 * OVF + T_tree^4 * TVF + T_trans^4 * TRVF)
    ///   6. Parallel Force-Restore + Penman-Monteith single-source simulation per point
    ///
    /// Integrated inputs:
    ///   - GroundSet: encapsulates mesh, points, d1, d2, HExp, SVFN, RhoSur, EpsSur
    ///   - ObsSet: classified obstacles for fine-grained DNI transmission (NEW 2026-06-15)
    ///   - WindCfg: flexible wind speed (EPW / single / per-point / time-series / full 2D)
    ///
    /// Time range support:
    ///   - Default: full-year simulation
    ///   - Optional TimeSet: partial period simulation with automatic pre-heat
    ///   - Optional PreHeat: pre-heat duration in hours (default 2) when TimeSet is active
    ///
    /// Input order (required first, then by category, Folder penultimate, Run last):
    ///   0 EPW        Weather file path              (required)
    ///   1 GroundSet  Ground surface config          (required)
    ///   2 ObsSet     Classified obstacle set        (optional, NEW 2026-06-15)
    ///   3 SoilThermSet  Soil thermal config         (required)
    ///   4 SoilSurSet Surface aerodynamic config     (optional)
    ///   5 SoilMoistSet Moisture config              (optional)
        ///   7 TimeSet    Time range config              (optional)
    ///   8 PreHeat    Pre-heat duration [h]          (optional, default 24)
    ///   9 Folder     Output folder path             (penultimate)
    ///  10 Run        Execution toggle               (last)
    /// </summary>
    public class SpatialSoilThermalSimulatorComponent : GH_Component
    {
        public SpatialSoilThermalSimulatorComponent()
          : base("Spatial Soil Thermal Simulator", "SpSoilSim",
              "Spatial soil temperature and latent heat simulation at multiple points " +
              "with per-point radiation correction (SVF, solar exposure, surround reflectance). " +
              "Integrated GroundSet for geometry/environment params. " +
              "Independent ObsSet input for classified obstacle DNI transmission (Beer-Lambert). " +
              "Flexible wind speed input via separate WindCfg port. " +
              "Supports optional time range filtering with automatic pre-heat.",
              "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // ---- Required: EPW weather ----
            pManager.AddTextParameter("EPW Path", "EPW",
                "Path to EPW weather file", GH_ParamAccess.item);

            // ---- Required: Ground Set (geometry + environment) ----
            pManager.AddGenericParameter("Ground Set", "GroundSet",
                "Ground surface configuration (from Ground Surface Settings). " +
                "Encapsulates mesh, analysis points, layer depths, HExp, SVFN, " +
                "surround reflectance/emissivity.",
                GH_ParamAccess.item);

            // ---- NEW (2026-06-15): Independent ObsSet input ----
            // Previously ObsSet was passed through GroundSet. Now it is a separate input
            // for better modularity and consistent data flow across all simulator components.
            pManager.AddGenericParameter("Obstacle Set", "ObsSet",
                "Optional: Classified obstacle set (ObstacleSet) for fine-grained DNI transmission. " +
                "Connect ObsSet component. Supports opaque buildings, trees with Beer-Lambert canopy " +
                "transmission, and translucent sunshades. " +
                "IMPORTANT: Only accepts ObstacleSet data type. Raw Mesh/Surface/Brep inputs are " +
                "NOT accepted — geometry must be pre-processed through the ObsSet component.",
                GH_ParamAccess.item);
            pManager[2].Optional = true;

            // ---- Required: Soil Thermal config ----
            pManager.AddGenericParameter("Soil Thermal Set", "SoilThermSet",
                "Soil thermal configuration (from Soil Thermal Settings)", GH_ParamAccess.item);

            // ---- Optional: Soil Surface config (aerodynamic parameters + wind speed) ----
            pManager.AddGenericParameter("Soil Surface Set", "SoilSurSet",
                "Surface aerodynamic configuration (from Soil Surface Settings). " +
                "Contains z0, ra bounds, stability correction, AND wind speed override settings.",
                GH_ParamAccess.item);
            pManager[4].Optional = true;

            // ---- Optional: Soil Moisture config ----
            pManager.AddGenericParameter("Soil Moisture Set", "SoilMoistSet",
                "Soil moisture configuration (optional, from Soil Moisture Settings)", GH_ParamAccess.item);
            pManager[5].Optional = true;

            // ---- Optional: Time range config ----
            pManager.AddGenericParameter("Time Settings", "TimeSet",
                "Optional: time period configuration (from Time Settings component). " +
                "If not provided, full-year simulation is performed.",
                GH_ParamAccess.item);
            pManager[6].Optional = true;

            // ---- Optional: Pre-heat duration ----
            pManager.AddIntegerParameter("Pre-heat Hours", "PreHeat",
                "Pre-heat duration in hours for non-full-year simulation (default 2). " +
                "Only effective when TimeSet is connected. Results during pre-heat are discarded.",
                GH_ParamAccess.item, 2);
            pManager[7].Optional = true;

            // Output folder
            pManager.AddTextParameter("Output Folder", "Folder",
                "Folder path for saving spatial simulation results", GH_ParamAccess.item);
            pManager[8].Optional = true;

            // Run toggle
            pManager.AddBooleanParameter("Run", "Run",
                "Set to true to execute the simulation.", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // ENHANCED (2026-06-16): Added TVF and TRVF outputs for decomposed view factors.
            // Ports 0-4 unchanged. TVF=5, TRVF=6 are NEW.
            pManager.AddGenericParameter("Spatial Result", "Result",
                "Spatial simulation result object", GH_ParamAccess.item);
            pManager.AddTextParameter("Log", "Log",
                "Execution log", GH_ParamAccess.list);
            pManager.AddNumberParameter("Annual ET", "ETann",
                "Total annual ET [mm] per point", GH_ParamAccess.list);
            pManager.AddNumberParameter("Mean Tg", "TgMean",
                "Mean annual ground temperature [C] per point", GH_ParamAccess.list);
            pManager.AddNumberParameter("SVF", "SVF",
                "Sky view factor per point (2\u03c0)", GH_ParamAccess.list);
            // NEW 2026-06-16: Decomposed view factors for precise diffuse radiation
            pManager.AddNumberParameter("Tree View Factor", "TVF",
                "Tree canopy view factor per point (2\u03c0). Directions blocked by tree detail meshes. NEW 2026-06-16.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Translucent View Factor", "TRVF",
                "Translucent shade view factor per point (2\u03c0). Directions blocked by translucent shade meshes. NEW 2026-06-16.", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var logs = new List<string>();

            try
            {
                // ---- Last (10): Check Run toggle ----
                bool run = false;
                DA.GetData(9, ref run);
                if (!run)
                {
                    logs.Add("Simulation paused. Set 'Run' to true to execute.");
                    DA.SetDataList(1, logs);
                    return;
                }

                // ---- 0: EPW ----
                string epwPath = "";
                if (!DA.GetData(0, ref epwPath)) return;
                if (!File.Exists(epwPath))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"EPW not found: {epwPath}");
                    return;
                }

                EPWData epwData;
                try
                {
                    epwData = new EPWData(epwPath);
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"EPW parse error: {ex.Message}");
                    return;
                }

                // ---- 1: Ground Set (geometry + environment, NO LONGER contains ObsSet) ----
                var groundConfig = ExtractConfig<GroundSurfaceConfig>(DA, 1);
                if (groundConfig == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                        "GroundSet required. Connect Ground Surface Settings' GroundSet output. " +
                        "Do NOT connect Pts or Mesh - those are preview outputs.");
                    return;
                }

                var analysisPoints = groundConfig.AnalysisPoints;
                if (analysisPoints == null || analysisPoints.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No analysis points in GroundSet.");
                    return;
                }

                double exposureHeight = groundConfig.ExposureHeight;
                int svfSampleCount = groundConfig.SVFSampleCount;
                double surroundReflectance = groundConfig.SurroundReflectance;
                double surroundEmissivity = groundConfig.SurroundEmissivity;
                double topLayerDepth = groundConfig.TopLayerDepth;
                double deepLayerDepth = groundConfig.DeepLayerDepth;

                // =====================================================================
                // NEW (2026-06-15): Independent ObsSet input (index 2)
                // With strict type validation. Only accepts ObstacleSet.
                // =====================================================================
                ObstacleSet obstacleSet = null;
                GH_ObjectWrapper obsWrapper = null;
                if (DA.GetData(2, ref obsWrapper))
                {
                    if (obsWrapper?.Value is ObstacleSet os)
                    {
                        obstacleSet = os;
                    }
                    else if (obsWrapper?.Value != null)
                    {
                        // STRICT VALIDATION: Reject raw geometry inputs
                        string typeName = obsWrapper.Value.GetType().Name;
                        if (typeName.Contains("Brep") || typeName.Contains("Mesh") ||
                            typeName.Contains("Surface") || typeName.Contains("Geometry"))
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                                $"ObsSet input rejected: received raw {typeName} geometry. " +
                                "You MUST connect the output of the 'ObsSet' component here. " +
                                "Raw Brep/Mesh/Surface inputs are NOT accepted.");
                            return;
                        }
                        else
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                                $"ObsSet input rejected: expected ObstacleSet but received {typeName}. " +
                                "Connect the output of the 'ObsSet' component.");
                            return;
                        }
                    }
                }

                // Fallback: if no independent ObsSet connected, try GroundSet.ObstacleSet
                // for transitional compatibility. GroundSet no longer receives ObsSet from
                // GroundSettings, but this fallback preserves compatibility with custom GroundSet sources.
                if (obstacleSet == null && groundConfig.ObstacleSet != null && groundConfig.ObstacleSet.HasAnyObstacles)
                {
                    obstacleSet = groundConfig.ObstacleSet;
                    logs.Add("NOTE: Using ObstacleSet from GroundSet (legacy path). " +
                             "Consider connecting ObsSet directly to the SpSoilSim component.");
                }

                // =====================================================================
                // ENHANCED (2026-06-15): Separate SVF meshes from DNI meshes.
                // SVF uses Opaque + TreeDetail + TranslucentShade (physical occlusion).
                // TreeCanopy is excluded from SVF — it exists only for Beer-Lambert
                // path-length calculation in DNI transmission, not for view-factor occlusion.
                // =====================================================================
                List<Mesh> svfObstacleMeshes = new List<Mesh>();
                List<Mesh> allObstacleMeshes = new List<Mesh>();
                if (obstacleSet != null)
                {
                    if (obstacleSet.OpaqueObjectMeshes != null)
                    {
                        svfObstacleMeshes.AddRange(obstacleSet.OpaqueObjectMeshes);
                        allObstacleMeshes.AddRange(obstacleSet.OpaqueObjectMeshes);
                    }
                    if (obstacleSet.TreeDetailMeshes != null)
                    {
                        svfObstacleMeshes.AddRange(obstacleSet.TreeDetailMeshes);
                        allObstacleMeshes.AddRange(obstacleSet.TreeDetailMeshes);
                    }
                    if (obstacleSet.TranslucentShadeMeshes != null)
                    {
                        svfObstacleMeshes.AddRange(obstacleSet.TranslucentShadeMeshes);
                        allObstacleMeshes.AddRange(obstacleSet.TranslucentShadeMeshes);
                    }
                    if (obstacleSet.TreeCanopyMeshes != null)
                        allObstacleMeshes.AddRange(obstacleSet.TreeCanopyMeshes);
                }

                // ---- 3: Soil Thermal config (was index 2, now index 3) ----
                var soilConfig = ExtractConfig<SoilThermalConfig>(DA, 3);
                if (soilConfig == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "SoilThSet required. Connect Soil Temperature Settings' output.");
                    return;
                }

                // Apply layer depths from GroundSet to SoilThermalConfig
                soilConfig.TopLayerDepth = topLayerDepth;
                soilConfig.DeepLayerDepth = deepLayerDepth;

                // ---- 4: Soil Surface config (was index 3, now index 4) ----
                var surfConfig = ExtractConfig<SoilSurfaceConfig>(DA, 4) ?? new SoilSurfaceConfig();
                WindSpeedConfig windConfig = surfConfig.WindSpeedConfig;

                // ---- 5: Soil Moisture config (was index 4, now index 5) ----
                var moistConfig = ExtractConfig<SoilMoistureConfig>(DA, 5) ?? new SoilMoistureConfig();

                // ---- 6: TimeSet (was index 7) ----
                SimulationTimeConfig timeConfig = null;
                GH_ObjectWrapper timeWrapper = null;
                if (DA.GetData(6, ref timeWrapper) && timeWrapper?.Value is SimulationTimeConfig tc)
                {
                    timeConfig = tc;
                }

                // ---- 8: Pre-heat duration (was index 8, now index 7) ----
                int preHeatHours = 24;
                DA.GetData(7, ref preHeatHours);
                preHeatHours = Math.Max(0, preHeatHours);  // ensure non-negative

                // Determine simulation time range
                int nHoursTotal = epwData.HourCount;
                int outStartHoy = 0;
                int outEndHoy = nHoursTotal - 1;
                bool isPartial = false;

                if (timeConfig != null)
                {
                    timeConfig.GetHourRange(out outStartHoy, out outEndHoy, nHoursTotal - 1);
                    isPartial = !(outStartHoy == 0 && outEndHoy >= nHoursTotal - 1);
                }

                // Pre-heat logic for non-full-year simulation
                int simStartHoy = 0;
                int simEndHoy = outEndHoy;

                if (isPartial)
                {
                    simStartHoy = Math.Max(0, outStartHoy - preHeatHours);
                    int actualPreHeat = outStartHoy - simStartHoy;

                    logs.Add($"=== Partial Simulation Mode ===");
                    logs.Add($"Requested output: HOY {outStartHoy}-{outEndHoy} ({outEndHoy - outStartHoy + 1} hours)");
                    logs.Add($"Pre-heat period: HOY {simStartHoy}-{outStartHoy - 1} ({actualPreHeat} hours, results discarded)");

                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Non-full-year simulation enabled with {actualPreHeat} hours of preheating.\n" +
                    $"Computation range: HOY {simStartHoy}-{simEndHoy},\n" +
                    $"output range: HOY {outStartHoy}-{outEndHoy}.\n" +
                    $"Preheating period results are automatically discarded to ensure physical convergence.");
                }
                else
                {
                    logs.Add($"=== Full-Year Simulation Mode ===");
                    logs.Add($"Total hours: {nHoursTotal}");
                }

                // ---- 9: Output Folder (was index 8, now index 9) ----
                string folder = "";
                if (!DA.GetData(8, ref folder)) return;
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                int nPts = analysisPoints.Count;
                double timeStepHours = epwData.TimeStepHours;
                int outHours = outEndHoy - outStartHoy + 1;

                string obsLog = obstacleSet != null && obstacleSet.HasAnyObstacles
                    ? $"ObsSet: Opaque={obstacleSet.OpaqueObjectMeshes?.Count ?? 0}, " +
                      $"TreeDet={obstacleSet.TreeDetailMeshes?.Count ?? 0}, " +
                      $"Canopy={obstacleSet.TreeCanopyMeshes?.Count ?? 0}, " +
                      $"TransShd={obstacleSet.TranslucentShadeMeshes?.Count ?? 0}"
                    : "Obstacles: None";

                logs.Add($"=== Spatial Soil Thermal Simulator ===");
                logs.Add($"Points: {nPts}, {obsLog}");
                logs.Add($"d1={topLayerDepth:F3}m d2={deepLayerDepth:F3}m HExp={exposureHeight:F3}m");
                logs.Add($"RhoSur={surroundReflectance:F2} EpsSur={surroundEmissivity:F2} SVFN={svfSampleCount}");
                if (isPartial)
                    logs.Add($"Sim: HOY {simStartHoy}-{simEndHoy} (output: HOY {outStartHoy}-{outEndHoy})");
                logs.Add($"LE Method: {soilConfig.LatentHeatMethod}");

                // Wind speed mode info
                if (windConfig != null && windConfig.Mode != WindSpeedMode.FromEPW)
                {
                    string windDesc = windConfig.Mode == WindSpeedMode.SingleValue
                        ? $"single={windConfig.SingleWindSpeed:F2}m/s"
                        : windConfig.Mode == WindSpeedMode.PerPointConstant
                        ? $"per-pt-const ({windConfig.PerPointWindSpeed.Count}pts)"
                        : windConfig.Mode == WindSpeedMode.TimeSeries
                        ? $"time-series ({windConfig.TimeSeriesWindSpeed.Count}hrs)"
                        : $"per-pt-series ({windConfig.PerPointTimeSeriesWindSpeed.Count}pts)";
                    logs.Add($"Wind override: {windDesc}");
                }
                else
                {
                    logs.Add("Wind: using EPW (auto)");
                }

                // Air temperature mode info
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

                // =====================================================================
                // ENHANCED (2026-06-16): Decomposed view factor calculation
                // Computes: SVF (sky), TVF (tree), TRVF (translucent), OVF_opaque (opaque)
                // Conservation: SVF + TVF + TRVF + OVF_opaque = 1.0
                // =====================================================================
                var svf = new double[nPts];
                var tvf = new double[nPts];    // NEW 2026-06-16
                var trvf = new double[nPts];   // NEW 2026-06-16
                var ovfOpaque = new double[nPts]; // NEW 2026-06-16 (opaque-only)

                if (obstacleSet != null && obstacleSet.HasAnyObstacles)
                {
                    // Use decomposed view factor calculation with ObstacleSet
                    HumanExposureModel.CalculateDecomposedViewFactorsBatch(
                        analysisPoints, obstacleSet,
                        svf, tvf, trvf, ovfOpaque,
                        exposureHeight, svfSampleCount);
                    logs.Add($"SVF range: [{svf.Min():F3}, {svf.Max():F3}] (avg={svf.Average():F3}), " +
                             $"TVF=[{tvf.Min():F3},{tvf.Max():F3}], TRVF=[{trvf.Min():F3},{trvf.Max():F3}]");
                }
                else if (svfObstacleMeshes.Count > 0)
                {
                    // Legacy: flat obstacle mesh list (no decomposition)
                    Parallel.For(0, nPts, i =>
                    {
                        svf[i] = HumanExposureModel.CalculateSkyViewFactorForPoint(
                            analysisPoints[i], svfObstacleMeshes, exposureHeight, svfSampleCount);
                    });
                    for (int i = 0; i < nPts; i++) { tvf[i] = 0.0; trvf[i] = 0.0; ovfOpaque[i] = 1.0 - svf[i]; }
                    logs.Add($"SVF range: [{svf.Min():F3}, {svf.Max():F3}] (avg={svf.Average():F3}) [legacy mode]");
                }
                else
                {
                    for (int i = 0; i < nPts; i++)
                    {
                        svf[i] = 1.0; tvf[i] = 0.0; trvf[i] = 0.0; ovfOpaque[i] = 0.0;
                    }
                    logs.Add("No obstacles: SVF=1.0 for all points.");
                }

                // Precompute tree canopy transmission parameters (used in diffuse correction)
                double canopyThickness = obstacleSet != null
                    ? HumanExposureModel.CalculateCanopyCharacteristicThickness(obstacleSet.TreeCanopyMeshes)
                    : 0.0;
                double kcLAD = obstacleSet != null
                    ? obstacleSet.ExtinctionCoefficient * obstacleSet.LeafAreaDensity : 0.0;
                double treeTransmittance = Math.Exp(-kcLAD * canopyThickness);
                double translucentTau = obstacleSet != null ? obstacleSet.TranslucentTransmittance : 0.0;

                // =====================================================================
                // Phase 2: Solar position (compute for all hours)
                // =====================================================================
                var sunVectors = new Vector3d[nHoursTotal];
                var solarAltitudes = new double[nHoursTotal];

                for (int hoy = 0; hoy < nHoursTotal; hoy++)
                {
                    var record = epwData.GetHour(hoy);

                    var spaData = new SPACalculator.SPAData
                    {
                        Year = record.Year,
                        Month = record.Month,
                        Day = record.Day,
                        Hour = record.Hour - 1,
                        Minute = 0,
                        Second = 0,
                        Timezone = epwData.TimeZone,
                        Longitude = epwData.Longitude,
                        Latitude = epwData.Latitude,
                        Elevation = epwData.Elevation,
                        Pressure = record.AtmosphericPressure / 100.0,
                        Temperature = record.DryBulbTemperature,
                        Function = SPACalculator.CalculationMode.SPA_ZA
                    };

                    int spaResult = SPACalculator.Calculate(ref spaData);
                    if (spaResult != 0 || spaData.Zenith >= 90.0)
                    {
                        sunVectors[hoy] = Vector3d.Unset;
                        solarAltitudes[hoy] = 0.0;
                        continue;
                    }

                    double altitudeDeg = 90.0 - spaData.Zenith;
                    solarAltitudes[hoy] = altitudeDeg * Math.PI / 180.0;
                    sunVectors[hoy] = SolarGeometry.SunVectorFromAzimuthZenith(spaData.Azimuth, spaData.Zenith);
                }

                logs.Add("Solar position pre-computation complete.");

                // =====================================================================
                // Phase 3: Ground-level DNI exposure factor calculation (ENHANCED 2026-06-15)
                // Uses classified ObstacleSet with fine-grained DNI transmission:
                //   - Opaque: DNI = 0 (full block)
                //   - Tree: DNI * exp(-k * LAD * s) (Beer-Lambert canopy transmission)
                //   - Translucent: DNI * tau (fixed transmittance)
                //   - Multiple non-opaque obstacles: PRODUCT of individual transmissions
                // Ground level: bodyHeight = 0, sampleCount = 1 (single ray per point)
                // =====================================================================
                var dniExposureFactors = new double[nPts, nHoursTotal];
                int expComputeStart = isPartial ? simStartHoy : 0;
                int expComputeEnd = isPartial ? simEndHoy : nHoursTotal - 1;

                double groundLevelBodyHeight = 0.0;
                int groundLevelSamplePoints = 1;
                double maxRayDistance = 500.0;

                for (int hoy = expComputeStart; hoy <= expComputeEnd; hoy++)
                {
                    var sunVec = sunVectors[hoy];
                    if (sunVec == Vector3d.Unset)
                    {
                        for (int p = 0; p < nPts; p++)
                            dniExposureFactors[p, hoy] = 0.0;
                        continue;
                    }

                    if (obstacleSet != null && obstacleSet.HasAnyObstacles)
                    {
                        // ENHANCED (2026-06-15): Use ObstacleSet with cumulative transmission product
                        // CalculateRayDNITransmission correctly handles multiple non-opaque obstacles
                        // by multiplying their individual transmission factors.
                        double[] batch = HumanExposureModel.CalculateDNIExposureFactorsBatch(
                            analysisPoints, sunVec, obstacleSet,
                            groundLevelBodyHeight, groundLevelSamplePoints, maxRayDistance);

                        for (int p = 0; p < nPts; p++)
                            dniExposureFactors[p, hoy] = batch[p];
                    }
                    else if (allObstacleMeshes.Count > 0)
                    {
                        // Legacy fallback: no ObstacleSet, treat all as opaque
                        double[] batch = HumanExposureModel.CalculateExposureFactorsBatch(
                            analysisPoints, sunVec, allObstacleMeshes,
                            groundLevelBodyHeight, exposureHeight,
                            groundLevelSamplePoints, maxRayDistance);

                        for (int p = 0; p < nPts; p++)
                            dniExposureFactors[p, hoy] = batch[p];
                    }
                    else
                    {
                        for (int p = 0; p < nPts; p++)
                            dniExposureFactors[p, hoy] = 1.0;
                    }
                }

                logs.Add("Ground-level DNI exposure computation complete (with multi-obstacle transmission product).");

                // =====================================================================
                // Phase 4: Initialize models
                // =====================================================================
                double firstAirT = epwData.GetHour(simStartHoy).DryBulbTemperature;
                double annualMean = EstimateAnnualMean(epwData);

                var models = new SoilTemperatureModel[nPts];
                for (int p = 0; p < nPts; p++)
                {
                    models[p] = new SoilTemperatureModel(
                        (SoilThermalConfig)soilConfig.Clone(),
                        (SoilSurfaceConfig)surfConfig.Clone(),
                        (SoilMoistureConfig)moistConfig.Clone())
                    {
                        SubStepsPerHour = soilConfig.SubStepsPerHour,
                        EnergyBalanceTolerance = soilConfig.EnergyBalanceTolerance,
                        MaxIterations = soilConfig.MaxIterations
                    };
                    models[p].Initialize(firstAirT, annualMean);
                }

                // =====================================================================
                // Phase 5: Main simulation loop (only over simulation range)
                // =====================================================================
                var allTg = InitListList<double>(nPts);
                var allT2 = InitListList<double>(nPts);
                var allLE = InitListList<double>(nPts);
                var allBeta = InitListList<double>(nPts);
                var allRa = InitListList<double>(nPts);
                var allRsSoil = InitListList<double>(nPts);
                var allRn = InitListList<double>(nPts);
                var allH = InitListList<double>(nPts);
                var allG = InitListList<double>(nPts);
                var allET = InitListList<double>(nPts);
                var allETref = InitListList<double>(nPts);

                int simHours = simEndHoy - simStartHoy + 1;
                int resultsSkipped = 0;

                for (int hoy = simStartHoy; hoy <= simEndHoy; hoy++)
                {
                    var record = epwData.GetHour(hoy);
                    double dhi = record.DiffuseHorizontalRadiation;
                    double dni = record.DirectNormalRadiation;
                    double lDownEpw = record.HorizontalInfraredRadiation;
                    double sinAlt = Math.Sin(solarAltitudes[hoy]);
                    double tAir = record.DryBulbTemperature;
                    double epwWind = record.WindSpeed;

                    Parallel.For(0, nPts, new ParallelOptions
                    { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) }, pIdx =>
                    {
                        // Per-point corrected SHORTWAVE (ENHANCED 2026-06-15)
                        // dniExposureFactors accounts for transmission through vegetation
                        // (Beer-Lambert) and translucent materials, with multi-obstacle product.
                        // ENHANCED (2026-06-16): Precise diffuse radiation with decomposed view factors.
                        // DHI_eff = SVF*DHI + TVF*DHI*exp(-k*LAD*l) + TRVF*DHI*tau
                        double diffuseActual = dhi * svf[pIdx]
                                             + dhi * tvf[pIdx] * treeTransmittance
                                             + dhi * trvf[pIdx] * translucentTau;
                        double directHorizontal = dni * sinAlt * dniExposureFactors[pIdx, hoy];
                        // Surround GHI assumes open horizontal ground (SVF=1, no shading).
                        // Correct formula: GHI_surround = DHI + DNI*sin(alt) for open horizontal surface
                        double ghiSurround = dhi + dni * sinAlt;
                        // Surround reflectance uses total non-sky fraction (all obstacles contribute
                        // to ground-level reflection: opaque, tree, and translucent)
                        double nonSkyFraction = 1.0 - svf[pIdx];
                        double reflectedActual = surroundReflectance * ghiSurround * nonSkyFraction;

                        double ghiActual = diffuseActual + directHorizontal + reflectedActual;
                        ghiActual = Math.Max(0.0, ghiActual);

                        // Air temperature override (if configured)
                        double airTempOverride = (soilConfig.AirTemperatureConfig != null
                            && soilConfig.AirTemperatureConfig.Mode != AirTemperatureMode.FromEPW)
                            ? soilConfig.AirTemperatureConfig.GetAirTemperature(pIdx, hoy, tAir)
                            : -999;
                        double effectiveTair = airTempOverride > -273.15 ? airTempOverride : tAir;

                        // Per-point corrected LONGWAVE with decomposed surface temperatures
                        double lSky = lDownEpw * svf[pIdx];

                        // Get surface temperatures from ObstacleSet (fallback to air temperature)
                        double obsSurTemp = effectiveTair;
                        double treeSurTemp = effectiveTair;
                        double transSurTemp = effectiveTair;
                        if (obstacleSet != null)
                        {
                            obsSurTemp = obstacleSet.GetObstacleTemperature(hoy, effectiveTair);
                            treeSurTemp = obstacleSet.GetTreeCanopyTemperature(hoy, effectiveTair);
                            transSurTemp = obstacleSet.GetTranslucentSurfaceTemperature(hoy, effectiveTair);
                        }

                        double lSurround = surroundEmissivity * 5.67e-8 * (
                            Math.Pow(obsSurTemp + 273.15, 4) * ovfOpaque[pIdx] +
                            Math.Pow(treeSurTemp + 273.15, 4) * tvf[pIdx] +
                            Math.Pow(transSurTemp + 273.15, 4) * trvf[pIdx]);
                        double lDownActual = lSky + lSurround;

                        // Wind speed override (if configured)
                        double windOverride = (windConfig != null && windConfig.Mode != WindSpeedMode.FromEPW)
                            ? windConfig.GetWindSpeed(pIdx, hoy, epwWind)
                            : -1;

                        // Get relative humidity override from SoilThermalConfig (if configured)
                        double rhOverride = (soilConfig.RelativeHumidityConfig != null
                            && soilConfig.RelativeHumidityConfig.Mode != RelativeHumidityMode.FromEPW)
                            ? soilConfig.RelativeHumidityConfig.GetRelativeHumidity(pIdx, hoy, record.RelativeHumidity)
                            : -1;
                        // Run model with wind speed, air temperature, and RH overrides
                        models[pIdx].Step(record, timeStepHours, ghiActual, lDownActual, windOverride, airTempOverride, rhOverride);

                        // Store results (only for output range, skip pre-heat)
                        if (hoy >= outStartHoy)
                        {
                            allTg[pIdx].Add(models[pIdx].T1);
                            allT2[pIdx].Add(models[pIdx].T2);
                            allLE[pIdx].Add(models[pIdx].LastLatentHeat);
                            allBeta[pIdx].Add(models[pIdx].LastBeta);
                            allRa[pIdx].Add(models[pIdx].LastRa);
                            allRsSoil[pIdx].Add(models[pIdx].LastRsSoil);
                            allRn[pIdx].Add(models[pIdx].LastNetRadiation);
                            allH[pIdx].Add(models[pIdx].LastSensibleHeat);
                            allG[pIdx].Add(models[pIdx].LastGroundHeatFlux);

                            double lambdaT = (2.501e6 - 2361.0 * effectiveTair);
                            double et_mmh = models[pIdx].LastLatentHeat / lambdaT * 3600.0;
                            allET[pIdx].Add(et_mmh);

                            double etRef = models[pIdx].CalculateReferenceET(record,
                                models[pIdx].LastNetRadiation, models[pIdx].LastGroundHeatFlux, windOverride, airTempOverride, rhOverride);
                            allETref[pIdx].Add(etRef);
                        }
                    });

                    if (hoy < outStartHoy)
                        resultsSkipped++;

                    if (hoy > 0 && hoy % 1000 == 0)
                        logs.Add($"  Progress: {hoy - simStartHoy + 1}/{simHours} hours...");
                }

                if (isPartial)
                    logs.Add($"Pre-heat complete: {resultsSkipped} hours discarded, {outHours} hours output.");

                logs.Add($"Computed {simHours} hours x {nPts} points. Output {outHours} hours x {nPts} points.");

                // =====================================================================
                // Phase 6: Assemble result
                // =====================================================================
                var result = new SoilThermalSimulationResult
                {
                    EPWCity = epwData.City,
                    EPWCountry = epwData.Country,
                    Latitude = epwData.Latitude,
                    Longitude = epwData.Longitude,
                    TimeZone = epwData.TimeZone,
                    Elevation = epwData.Elevation,
                    AnalyzedHours = outHours,
                    TotalPoints = nPts,
                    IsSpatial = true,
                    LatentHeatMethodUsed = soilConfig.LatentHeatMethod,
                    SoilTemp = { HourlyGroundTemperature = allTg, HourlyDeepTemperature = allT2,
                                 AnnualMeanTemperature = allTg.Select(t => t.Average()).ToList() },
                    LEFlux = { HourlyLE = allLE, HourlyBeta = allBeta, HourlyRa = allRa,
                               HourlyRsSoil = allRsSoil },
                    ET = { HourlyET = allET, HourlyReferenceET = allETref,
                           TotalAnnualET = allET.Select(e => e.Sum()).ToList() },
                    EnergyBalance = { HourlyNetRadiation = allRn, HourlySensibleHeat = allH,
                                      HourlyLatentHeat = allLE, HourlyGroundHeat = allG }
                };

                result.SaveToFolder(folder);
                logs.Add($"Results saved to: {folder}");

                double maxOverall = double.MinValue, minOverall = double.MaxValue;
                for (int p = 0; p < nPts; p++)
                    for (int h = 0; h < allTg[p].Count; h++)
                    {
                        double t = allTg[p][h];
                        if (t > maxOverall) maxOverall = t;
                        if (t < minOverall) minOverall = t;
                    }
                logs.Add($"Tg range: [{minOverall:F1}, {maxOverall:F1}] C");

                // ---- ALL OUTPUT INDICES UNCHANGED ----
                DA.SetData(0, new GH_ObjectWrapper(result));
                DA.SetDataList(1, logs);
                DA.SetDataList(2, allET.Select(e => e.Sum()).ToList());
                DA.SetDataList(3, allTg.Select(t => t.Average()).ToList());
                DA.SetDataList(4, svf.ToList());
                // NEW 2026-06-16: Output decomposed view factors
                DA.SetDataList(5, tvf.ToList());
                DA.SetDataList(6, trvf.ToList());
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Spatial error: {ex.Message}");
                logs.Add($"ERROR: {ex.Message}");
                if (ex.InnerException != null)
                    logs.Add($"  Inner: {ex.InnerException.Message}");
                DA.SetDataList(1, logs);
            }
        }

        /// <summary>
        /// Extract typed config from a GH_ObjectWrapper at the given input index.
        /// Returns null if not connected or wrong type.
        /// </summary>
        private T ExtractConfig<T>(IGH_DataAccess DA, int index) where T : class
        {
            GH_ObjectWrapper w = null;
            if (!DA.GetData(index, ref w) || w?.Value == null) return null;
            if (w.Value is T config) return config;
            // Type mismatch - provide helpful error
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                $"Port [{index}]: expected {typeof(T).Name} but received {w.Value.GetType().Name}. Check wiring.");
            return null;
        }

        private static List<List<T>> InitListList<T>(int count)
        {
            var result = new List<List<T>>(count);
            for (int i = 0; i < count; i++) result.Add(new List<T>());
            return result;
        }

        private double EstimateAnnualMean(EPWData epw)
        {
            int samples = epw.HourCount;
            if (samples == 0) return 15.0;
            double sum = 0.0;
            for (int i = 0; i < samples; i++)
                sum += epw.GetHour(i).DryBulbTemperature;
            return sum / samples;
        }

        public override GH_Exposure Exposure => GH_Exposure.senary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_spatialSoilTempModel;
        public override Guid ComponentGuid => new Guid("6F1E0EC3-182E-4966-95B7-A67DB662797A");
    }
}
