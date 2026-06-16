using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Common.Core;
using ThermalComfort.Core;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ThermalComfort
{
    public class MRTComponent : GH_Component
    {
        public MRTComponent()
          : base("Outdoor MRT", "MRT",
              "Calculate outdoor Mean Radiant Temperature using SolarCal model (ASHRAE 55) " +
              "or RayMan model with reverse ray tracing for human solar exposure factor.",
              "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("EPW File", "EPW", "Path to EPW weather file", GH_ParamAccess.item);
            pManager.AddPointParameter("Analysis Points", "Pts",
                "GROUND points where a person stands for MRT calculation. " +
                "Must be at terrain level (Z=ground), NOT at meteorological height (e.g. 1.5m). " +
                "If your comfort analysis points are at typical 1.5m height for wind/temp/UTCI, " +
                "project them DOWN to ground level first before connecting here. " +
                "The H bod parameter defines the full body height range for exposure sampling.",
                GH_ParamAccess.list);
            // ENHANCED (2026-06-15): ObstacleSet input with strict type validation.
            // Only accepts ObstacleSet encapsulated data. No backward compatibility for
            // raw Brep/Mesh/Surface inputs — users must connect through ObsSet component.
            pManager.AddGenericParameter("Obstacle Set", "ObsSet",
                "Optional: Classified obstacle set (ObstacleSet) for exposure calculation. " +
                "Connect ObsSet component. Supports opaque buildings, trees with canopy transmission, " +
                "and translucent sunshades. " +
                "IMPORTANT: Only accepts ObstacleSet data type. Raw Mesh/Surface/Brep inputs are " +
                "NOT accepted — geometry must be pre-processed through the ObsSet component.",
                GH_ParamAccess.item);
            pManager.AddGenericParameter("MRT Settings", "MRTSet", "MRT configuration settings", GH_ParamAccess.item);
            // NEW (index 4): TimeSet moved from MRTsettings to MRT component directly
            pManager.AddGenericParameter("Time Settings", "TimeSet",
                "Optional: Simulation period. Default = full year (8760h). Connect SimulationTimeConfig. " +
                "If omitted, calculates full year by default.",
                GH_ParamAccess.item);
            // NEW (index 5): Air Temperature (Ta) input - tree access for spatially/temporally varying data
            pManager.AddNumberParameter("Air Temperature", "Ta",
                "Optional: air temperature [°C] for MRT calculation. Overrides EPW dry bulb temperature. " +
                "Four input modes supported:\n" +
                "  1) Single value → uniform fixed Ta for all hours and all points.\n" +
                "  2) N values (N = point count) → per-point fixed Ta; each point uses its own value.\n" +
                "  3) 8760 values → hourly time-varying Ta (shared by all points).\n" +
                "  4) N×8760 tree (N branches × 8760h) → spatially varying hourly Ta per point.\n" +
                "If omitted, falls back to EPW dry bulb temperature.",
                GH_ParamAccess.tree);
            // (index 6): Tg changed from list to tree for spatially varying data
            pManager.AddNumberParameter("Ground Temperature", "Tg",
                "Optional: ground surface temperature [°C]. Four input modes supported:\n" +
                "  1) Single value → uniform fixed Tg for all hours and all points.\n" +
                "  2) N values (N = point count) → per-point fixed Tg; each point uses its own value.\n" +
                "  3) 8760 values → hourly time-varying Tg from soil model.\n" +
                "  4) N×8760 tree (N branches × 8760h) → spatially varying hourly Tg from Soil Temp.\n" +
                "Overrides SurfaceTemperature in MRT Settings. If omitted, falls back to Ta.",
                GH_ParamAccess.tree);
            pManager.AddBooleanParameter("Run", "Run", "Set to true to execute simulation", GH_ParamAccess.item, false);

            pManager[2].Optional = true;  // Obstacles (ObsSet)
            pManager[3].Optional = true;  // MRTSet
            pManager[4].Optional = true;  // TimeSet
            pManager[5].Optional = true;  // Ta
            pManager[6].Optional = true;  // Tg
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // --- MRT-related outputs ---
            pManager.AddNumberParameter("MRT", "MRT", "Mean Radiant Temperature [°C] for each analysis point and hour", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Shortwave Delta T", "dTsw", "Shortwave temperature increment [°C]", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Longwave Delta T", "dTlw", "Total longwave temperature increment [°C]", GH_ParamAccess.tree);
            pManager.AddNumberParameter("LW Sky", "dTlw_sky", "Longwave sky component contribution [°C]", GH_ParamAccess.tree);
            pManager.AddNumberParameter("LW Ground", "dTlw_grd", "Longwave ground component contribution [°C]", GH_ParamAccess.tree);
            pManager.AddNumberParameter("LW Opaque", "dTlw_opq", "Longwave opaque obstacle component contribution [°C]", GH_ParamAccess.tree);
            pManager.AddNumberParameter("LW Tree", "dTlw_tree", "Longwave tree canopy component contribution [°C]", GH_ParamAccess.tree);
            pManager.AddNumberParameter("LW Translucent", "dTlw_trans", "Longwave translucent shade component contribution [°C]", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Hourly Avg MRT", "HrMRT", "Hourly average MRT across all points [°C]", GH_ParamAccess.list);
            // --- View factor outputs ---
            pManager.AddNumberParameter("Sky View Factor", "SVF", "Sky view factor [0-1] from full-sphere (4π) sampling for each point", GH_ParamAccess.list);
            pManager.AddNumberParameter("Ground View Factor", "GVF", "Ground view factor [0-1] from full-sphere (4π) sampling for each point", GH_ParamAccess.list);
            pManager.AddNumberParameter("Opaque View Factor", "OVF", "Opaque obstacle view factor [0-1] from full-sphere (4π)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Tree View Factor", "TVF", "Tree canopy view factor [0-1] from full-sphere (4π)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Translucent View Factor", "TRVF", "Translucent shade view factor [0-1] from full-sphere (4π)", GH_ParamAccess.list);
            // --- Exposure factor outputs ---
            pManager.AddNumberParameter("Exposure Factor", "Exp", "Solar exposure factor [0-1] for each point and hour", GH_ParamAccess.tree);
            pManager.AddNumberParameter("DNI Exposure Factor", "DNIExp", "Effective DNI exposure factor [0-1] accounting for transmission through vegetation and translucent materials", GH_ParamAccess.tree);
            // --- Other outputs ---
            pManager.AddVectorParameter("Sun Vectors", "SunVec", "Sun vectors for each analyzed hour", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string epwPath = "";
            List<Point3d> analysisPoints = new List<Point3d>();
            bool run = false;

            if (!DA.GetData(0, ref epwPath)) return;
            if (!DA.GetDataList(1, analysisPoints)) return;

            // =====================================================================
            // ENHANCED (2026-06-15): Strict ObstacleSet type validation
            // Only accepts ObstacleSet. Raw geometry (Brep/Mesh/Surface) is rejected
            // with a warning — users must pre-process through ObsSet component.
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
                    // STRICT VALIDATION (2026-06-15): Reject raw geometry inputs
                    string typeName = obsWrapper.Value.GetType().Name;
                    if (typeName.Contains("Brep") || typeName.Contains("Mesh") || 
                        typeName.Contains("Surface") || typeName.Contains("Geometry"))
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            $"ObsSet input rejected: received raw {typeName} geometry. " +
                            "You MUST connect the output of the 'ObsSet' component here. " +
                            "Raw Brep/Mesh/Surface inputs are NOT accepted — geometry must be " +
                            "pre-processed through the ObsSet component to create a classified ObstacleSet.");
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

            var mrtConfig = ExtractSettings<MRTConfig>(DA, 3) ?? new MRTConfig();

            // NEW (index 4): TimeSet directly on MRT component
            SimulationTimeConfig timeConfig = null;
            GH_ObjectWrapper timeWrapper = null;
            if (DA.GetData(4, ref timeWrapper))
            {
                if (timeWrapper?.Value is SimulationTimeConfig tc) timeConfig = tc;
            }

            GH_Structure<GH_Number> airTempTree = new GH_Structure<GH_Number>();
            DA.GetDataTree(5, out airTempTree);
            List<double> airTemperatures = new List<double>();
            if (airTempTree.DataCount > 0)
            {
                foreach (var branch in airTempTree.Branches)
                    foreach (var item in branch)
                        if (item is GH_Number gn) airTemperatures.Add(gn.Value);
            }

            // Read Ground Temperature (index 6) - tree access for spatially varying data
            GH_Structure<GH_Number> groundTempTree = new GH_Structure<GH_Number>();
            DA.GetDataTree(6, out groundTempTree);
            List<double> groundTemperatures = new List<double>();
            if (groundTempTree.DataCount > 0)
            {
                foreach (var branch in groundTempTree.Branches)
                    foreach (var item in branch)
                        if (item is GH_Number gn) groundTemperatures.Add(gn.Value);
            }

            // (index 7): Surrounding surface temperature (obstacle temperature)
            

            DA.GetData(7, ref run);

            if (!run)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Set 'Run' to true to execute simulation.");
                return;
            }

            if (!File.Exists(epwPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "EPW file not found: " + epwPath);
                return;
            }

            if (analysisPoints == null || analysisPoints.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No analysis points provided.");
                return;
            }

            double avgZ = analysisPoints.Average(p => p.Z);
            double zRange = analysisPoints.Max(p => p.Z) - analysisPoints.Min(p => p.Z);
            if (avgZ > 0.5 || zRange > 0.01)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Analysis points avg Z={avgZ:F2}m (range={zRange:F3}m). " +
                    $"MRT exposure assumes GROUND points. If these are 1.5m meteo points, project to ground first.");
            }

            EPWData epwData;
            try
            {
                epwData = new EPWData(epwPath);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Loaded EPW: {epwData.City}, Hours={epwData.HourCount}");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to parse EPW: " + ex.Message);
                return;
            }

            // =====================================================================
            // Time range: default = full year (8760h) if no TimeSet provided
            // =====================================================================
            int startHoy = 0;
            int endHoy = epwData.HourCount - 1;
            if (timeConfig != null)
            {
                timeConfig.GetHourRange(out startHoy, out endHoy, epwData.HourCount - 1);
                startHoy = Math.Max(0, Math.Min(epwData.HourCount - 1, startHoy));
                endHoy = Math.Max(0, Math.Min(epwData.HourCount - 1, endHoy));
                if (endHoy < startHoy) endHoy = startHoy;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Time range: HOY {startHoy} to {endHoy} (from TimeSet input).");
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "No TimeSet provided — calculating full year (8760h) by default.");
            }
            int numHours = endHoy - startHoy + 1;
            int nPoints = analysisPoints.Count;
            int epwHours = epwData.HourCount;

            // =====================================================================
            // Air Temperature (Ta) input mode detection
            // =====================================================================
            List<List<double>> perPointHourlyAirTemps = null;   // Mode 4: per-point hourly
            List<double> perPointFixedAirTemps = null;          // Mode 2: per-point fixed
            List<double> hourlyAirTemps = null;                 // Mode 3: hourly time series
            double? fixedAirTemp = null;                        // Mode 1: single value
            bool hasAirTemps = false;

            if (airTemperatures.Count > 0)
            {
                if (airTemperatures.Count == 1)
                {
                    fixedAirTemp = airTemperatures[0];
                    hasAirTemps = true;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Ta mode 1: uniform fixed air temperature {fixedAirTemp:F1}°C.");
                }
                else if (airTemperatures.Count == nPoints)
                {
                    perPointFixedAirTemps = new List<double>(airTemperatures);
                    hasAirTemps = true;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Ta mode 2: per-point fixed air temperatures ({nPoints} values, " +
                        $"range [{perPointFixedAirTemps.Min():F1}, {perPointFixedAirTemps.Max():F1}]°C).");
                }
                else if (airTemperatures.Count >= epwHours)
                {
                    if (airTempTree.Branches.Count == nPoints &&
                        airTempTree.Branches[0].Count >= epwHours)
                    {
                        perPointHourlyAirTemps = new List<List<double>>(nPoints);
                        for (int p = 0; p < nPoints; p++)
                        {
                            var branch = airTempTree.Branches[p];
                            var list = new List<double>(branch.Count);
                            foreach (var item in branch)
                                if (item is GH_Number gn) list.Add(gn.Value);
                            perPointHourlyAirTemps.Add(list);
                        }
                        hasAirTemps = true;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"Ta mode 4: spatially varying hourly ({nPoints} points x {perPointHourlyAirTemps[0].Count}h).");
                    }
                    else
                    {
                        hourlyAirTemps = airTemperatures;
                        hasAirTemps = true;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"Ta mode 3: hourly air temperatures ({airTemperatures.Count}h).");
                    }
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Ta input ({airTemperatures.Count} values) unrecognized. Using EPW dry bulb temperature.");
                }
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "Ta: not provided — using EPW dry bulb temperature as air temperature fallback.");
            }

            // =====================================================================
            // Ground Temperature (Tg) input mode detection
            // =====================================================================
            List<List<double>> perPointHourlyGroundTemps = null;
            List<double> perPointFixedGroundTemps = null;  // Mode 2: per-point fixed Tg
            List<double> hourlyGroundTemps = null;
            double? fixedGroundTemp = null;
            bool hasGroundTemps = false;

            if (groundTemperatures.Count > 0)
            {
                if (groundTemperatures.Count == 1)
                {
                    fixedGroundTemp = groundTemperatures[0];
                    hasGroundTemps = true;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Tg mode 1: uniform fixed ground temperature {fixedGroundTemp:F1}°C.");
                }
                else if (groundTemperatures.Count == nPoints)
                {
                    perPointFixedGroundTemps = new List<double>(groundTemperatures);
                    hasGroundTemps = true;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Tg mode 2: per-point fixed ground temperatures ({nPoints} values, " +
                        $"range [{perPointFixedGroundTemps.Min():F1}, {perPointFixedGroundTemps.Max():F1}]°C).");
                }
                else if (groundTemperatures.Count >= epwHours)
                {
                    if (groundTempTree.Branches.Count == nPoints &&
                        groundTempTree.Branches[0].Count >= epwHours)
                    {
                        perPointHourlyGroundTemps = new List<List<double>>(nPoints);
                        for (int p = 0; p < nPoints; p++)
                        {
                            var branch = groundTempTree.Branches[p];
                            var list = new List<double>(branch.Count);
                            foreach (var item in branch)
                                if (item is GH_Number gn) list.Add(gn.Value);
                            perPointHourlyGroundTemps.Add(list);
                        }
                        hasGroundTemps = true;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"Tg mode 4: spatially varying hourly ({nPoints} points x {perPointHourlyGroundTemps[0].Count}h).");
                    }
                    else
                    {
                        hourlyGroundTemps = groundTemperatures;
                        hasGroundTemps = true;
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                            $"Tg mode 3: hourly ground temperatures ({groundTemperatures.Count}h).");
                    }
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Tg input ({groundTemperatures.Count} values) unrecognized. Ignoring.");
                }
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

            string obstacleInfo = obstacleSet != null && obstacleSet.HasAnyObstacles
                ? $"ObsSet: Opaque={obstacleSet.OpaqueObjectMeshes?.Count ?? 0}, " +
                  $"TreeDet={obstacleSet.TreeDetailMeshes?.Count ?? 0}, " +
                  $"Canopy={obstacleSet.TreeCanopyMeshes?.Count ?? 0}, " +
                  $"TransShd={obstacleSet.TranslucentShadeMeshes?.Count ?? 0}"
                : "No obstacles";

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Analysis points: {analysisPoints.Count}, {obstacleInfo}");

            // =====================================================================
            // ENHANCED (2026-06-16): FULL-SPHERE (4\u03c0) DECOMPOSED view factor calculation
            // Computes 5 components: SVF, GVF, OVF_opaque, TVF, TRVF
            // Priority: Opaque > TreeDetail > TranslucentShade
            // =====================================================================
            double[] skyViewFactors = new double[nPoints];
            double[] groundViewFactors = new double[nPoints];
            double[] obstacleViewFactors = new double[nPoints];  // Now OPAQUE-ONLY
            double[] treeViewFactors = new double[nPoints];       // NEW 2026-06-16
            double[] translucentViewFactors = new double[nPoints]; // NEW 2026-06-16

            if (obstacleSet != null && obstacleSet.HasAnyObstacles)
            {
                // Use ObstacleSet-based 5-component decomposition
                HumanExposureModel.CalculateSphericalViewFactorsBatch(
                    analysisPoints, obstacleSet,
                    skyViewFactors, groundViewFactors, obstacleViewFactors,
                    treeViewFactors, translucentViewFactors,
                    mrtConfig.AnalysisHeight, mrtConfig.SVFSampleCount, mrtConfig.MaxRayDistance);
            }
            else if (svfObstacleMeshes.Count > 0)
            {
                // Legacy fallback: flat mesh list (no ObstacleSet)
                // OVF includes all obstacle types; TVF and TRVF will be 0
                HumanExposureModel.CalculateSphericalViewFactorsBatch(
                    analysisPoints, svfObstacleMeshes,
                    skyViewFactors, groundViewFactors, obstacleViewFactors,
                    mrtConfig.AnalysisHeight, mrtConfig.SVFSampleCount, mrtConfig.MaxRayDistance);
                for (int i = 0; i < nPoints; i++)
                {
                    treeViewFactors[i] = 0.0;
                    translucentViewFactors[i] = 0.0;
                }
            }
            else
            {
                for (int i = 0; i < nPoints; i++)
                {
                    skyViewFactors[i] = 0.5;
                    groundViewFactors[i] = 0.5;
                    obstacleViewFactors[i] = 0.0;
                    treeViewFactors[i] = 0.0;
                    translucentViewFactors[i] = 0.0;
                }
            }

            // Output structures
            GH_Structure<GH_Number> mrtTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> exposureTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> dniExposureTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> dTSwTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> dTLwTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> dTLwSkyTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> dTLwGroundTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> dTLwObsTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> dTLwTreeTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> dTLwTranslucentTree = new GH_Structure<GH_Number>();
            List<double> hourlyAvgMRT = new List<double>(numHours);
            List<Vector3d> sunVectors = new List<Vector3d>();

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Calculating MRT for {numHours}h (HOY {startHoy}-{endHoy}), " +
                $"{mrtConfig.ExposureSamplePoints} exposure samples, " +
                $"{mrtConfig.SVFSampleCount} sphere samples (4π)...");

            for (int hoy = startHoy; hoy <= endHoy; hoy++)
            {
                var record = epwData.GetHour(hoy);
                double hourAvgMRT = 0;

                var spaData = new SPACalculator.SPAData
                {
                    Year = record.Year, Month = record.Month, Day = record.Day,
                    Hour = record.Hour - 1, Minute = record.Minute, Second = 0,
                    Timezone = epwData.TimeZone,
                    Longitude = epwData.Longitude, Latitude = epwData.Latitude,
                    Elevation = epwData.Elevation,
                    Pressure = record.AtmosphericPressure / 100.0,
                    Temperature = record.DryBulbTemperature,
                    Function = SPACalculator.CalculationMode.SPA_ZA
                };

                int spaResult = SPACalculator.Calculate(ref spaData);
                if (spaResult != 0)
                {
                    for (int p = 0; p < nPoints; p++)
                    {
                        // SPA failure fallback: compute point-specific air temp if available
                        double fallbackAirTemp = record.DryBulbTemperature;
                        if (perPointFixedAirTemps != null && p < perPointFixedAirTemps.Count)
                            fallbackAirTemp = perPointFixedAirTemps[p];
                        else if (perPointHourlyAirTemps != null && p < perPointHourlyAirTemps.Count && hoy >= 0 && hoy < perPointHourlyAirTemps[p].Count)
                            fallbackAirTemp = perPointHourlyAirTemps[p][hoy];
                        else if (hourlyAirTemps != null && hoy >= 0 && hoy < hourlyAirTemps.Count)
                            fallbackAirTemp = hourlyAirTemps[hoy];
                        else if (fixedAirTemp.HasValue)
                            fallbackAirTemp = fixedAirTemp.Value;

                        mrtTree.Append(new GH_Number(fallbackAirTemp), new GH_Path(p));
                        exposureTree.Append(new GH_Number(1.0), new GH_Path(p));
                        dniExposureTree.Append(new GH_Number(1.0), new GH_Path(p));
                        dTSwTree.Append(new GH_Number(0.0), new GH_Path(p));
                        dTLwTree.Append(new GH_Number(0.0), new GH_Path(p));
                        dTLwSkyTree.Append(new GH_Number(0.0), new GH_Path(p));
                        dTLwGroundTree.Append(new GH_Number(0.0), new GH_Path(p));
                        dTLwObsTree.Append(new GH_Number(0.0), new GH_Path(p));
                    }
                    hourlyAvgMRT.Add(record.DryBulbTemperature);
                    sunVectors.Add(Vector3d.Unset);
                    continue;
                }

                double solarAltitude = MRTModel.SolarAltitudeFromZenith(spaData.Zenith);

                // ENHANCED (2026-06-15): Calculate both exposure factor (binary) and
                // DNI exposure factor (with transmission through vegetation/translucent materials)
                double[] exposureFactors = new double[nPoints];
                double[] dniExposureFactors = new double[nPoints];
                Vector3d sunVec = new Vector3d();

                if (spaData.Zenith < 90.8334)
                {
                    sunVec = SolarGeometry.SunVectorFromAzimuthZenith(spaData.Azimuth, spaData.Zenith);
                    sunVectors.Add(sunVec);
                    if (obstacleSet != null && obstacleSet.HasAnyObstacles)
                    {
                        // Use enhanced calculation with obstacle classification and transmission
                        HumanExposureModel.CalculateExposureFactorsWithTransmissionBatch(
                            analysisPoints, sunVec, obstacleSet,
                            exposureFactors, dniExposureFactors,
                            mrtConfig.BodyHeight, mrtConfig.ExposureSamplePoints, mrtConfig.MaxRayDistance);
                    }
                    else if (allObstacleMeshes.Count > 0)
                    {
                        // Legacy mode: no ObstacleSet, treat all as opaque
                        exposureFactors = HumanExposureModel.CalculateExposureFactorsBatch(
                            analysisPoints, sunVec, allObstacleMeshes,
                            mrtConfig.BodyHeight, mrtConfig.AnalysisHeight,
                            mrtConfig.ExposureSamplePoints, mrtConfig.MaxRayDistance);
                        for (int i = 0; i < nPoints; i++) dniExposureFactors[i] = exposureFactors[i];
                    }
                    else
                    {
                        for (int i = 0; i < nPoints; i++)
                        {
                            exposureFactors[i] = 1.0;
                            dniExposureFactors[i] = 1.0;
                        }
                    }
                }
                else
                {
                    sunVectors.Add(Vector3d.Unset);
                    for (int i = 0; i < nPoints; i++)
                    {
                        exposureFactors[i] = 0.0;
                        dniExposureFactors[i] = 0.0;
                    }
                }

                // Sky emissivity
                double effectiveSkyEps = mrtConfig.SkyEmissivity;
                if (effectiveSkyEps < 0)
                {
                    double dewPoint = record.DewPointTemperature;
                    double cloudCover = record.TotalSkyCover;
                    if (!double.IsNaN(dewPoint))
                    {
                        double td = dewPoint / 100.0;
                        effectiveSkyEps = 0.711 + 0.56 * td + 0.73 * td * td;
                        effectiveSkyEps = Math.Max(0.5, Math.Min(1.0, effectiveSkyEps));
                    }
                    else if (!double.IsNaN(cloudCover))
                    {
                        effectiveSkyEps = 0.75 + 0.02 * cloudCover;
                        effectiveSkyEps = Math.Max(0.5, Math.Min(1.0, effectiveSkyEps));
                    }
                    else effectiveSkyEps = 1.0;
                }

                int globalHoy = hoy;

                // Resolve hourly air temperature (for modes 1 and 3)
                double? hourAirTemp = fixedAirTemp;
                if (perPointHourlyAirTemps != null) hourAirTemp = null;
                else if (hourlyAirTemps != null && globalHoy >= 0 && globalHoy < hourlyAirTemps.Count)
                    hourAirTemp = hourlyAirTemps[globalHoy];

                // Resolve hourly ground temperature (for modes 1 and 3)
                double? hourGroundTemp = fixedGroundTemp;
                if (perPointHourlyGroundTemps != null) hourGroundTemp = null;
                else if (hourlyGroundTemps != null && globalHoy >= 0 && globalHoy < hourlyGroundTemps.Count)
                    hourGroundTemp = hourlyGroundTemps[globalHoy];

                for (int p = 0; p < nPoints; p++)
                {
                    // Resolve per-point air temperature
                    double? pointAirTempVal = hourAirTemp;
                    if (perPointFixedAirTemps != null && p < perPointFixedAirTemps.Count)
                        pointAirTempVal = perPointFixedAirTemps[p];  // Mode 2: per-point fixed
                    else if (perPointHourlyAirTemps != null && globalHoy >= 0 && globalHoy < perPointHourlyAirTemps[p].Count)
                        pointAirTempVal = perPointHourlyAirTemps[p][globalHoy];  // Mode 4: per-point hourly

                    double pointAirTemp = pointAirTempVal ?? record.DryBulbTemperature;

                    // Resolve per-point ground temperature
                    double? pointGroundTemp = hourGroundTemp;
                    if (perPointFixedGroundTemps != null && p < perPointFixedGroundTemps.Count)
                        pointGroundTemp = perPointFixedGroundTemps[p];  // Mode 2: per-point fixed
                    else if (perPointHourlyGroundTemps != null && globalHoy >= 0 && globalHoy < perPointHourlyGroundTemps[p].Count)
                        pointGroundTemp = perPointHourlyGroundTemps[p][globalHoy];  // Mode 4: per-point hourly

                    // Resolve per-point obstacle temperatures from ObstacleSet
                    double pointObstacleTemp = pointAirTemp;
                    double pointTreeCanopyTemp = pointAirTemp;
                    double pointTranslucentTemp = pointAirTemp;
                    if (obstacleSet != null)
                    {
                        pointObstacleTemp = obstacleSet.GetObstacleTemperature(globalHoy, pointAirTemp);
                        pointTreeCanopyTemp = obstacleSet.GetTreeCanopyTemperature(globalHoy, pointAirTemp);
                        pointTranslucentTemp = obstacleSet.GetTranslucentSurfaceTemperature(globalHoy, pointAirTemp);
                    }

                    var hourConfig = new MRTConfig
                    {
                        UseRayManModel = mrtConfig.UseRayManModel,
                        PostureFactor = mrtConfig.PostureFactor,
                        BodyAbsorptivity = mrtConfig.BodyAbsorptivity,
                        BodyEmissivity = mrtConfig.BodyEmissivity,
                        RadiativeHeatTransferCoeff = mrtConfig.RadiativeHeatTransferCoeff,
                        FloorReflectance = mrtConfig.FloorReflectance,
                        ExposureSamplePoints = mrtConfig.ExposureSamplePoints,
                        AnalysisHeight = mrtConfig.AnalysisHeight,
                        BodyHeight = mrtConfig.BodyHeight,
                        MaxRayDistance = mrtConfig.MaxRayDistance,
                        IncludeShortwave = mrtConfig.IncludeShortwave,
                        IncludeLongwave = mrtConfig.IncludeLongwave,
                        SkyEmissivity = effectiveSkyEps,
                        LongwaveLinearCoeff = mrtConfig.LongwaveLinearCoeff,
                        GroundTemperature = pointGroundTemp,        // from Tg input
                        // REMOVED: SurroundingSurfaceTemperature moved to ObstacleSet
                        GroundEmissivity = mrtConfig.GroundEmissivity,      // from MRT Settings
                        ObstacleEmissivity = mrtConfig.ObstacleEmissivity   // from MRT Settings
                    };

                    // ENHANCED (2026-06-15): Pass dniExposureFactor to MRT calculation
                    // for fine-grained direct radiation transmission through vegetation
                    // and translucent materials (Beer-Lambert law + multi-obstacle product).
                    // ENHANCED (2026-06-16): Pass decomposed view factors (TVF, TRVF) for precise
                    // diffuse radiation calculation in MRT model.
                    double mrt = MRTModel.CalculateMRT(
                        pointAirTemp,
                        record.DirectNormalRadiation,
                        record.DiffuseHorizontalRadiation,
                        record.GlobalHorizontalRadiation,
                        record.HorizontalInfraredRadiation,
                        skyViewFactors[p], groundViewFactors[p], obstacleViewFactors[p],
                        treeViewFactors[p], translucentViewFactors[p],
                        dniExposureFactors[p], solarAltitude, hourConfig, hourConfig.UseRayManModel,
                        obstacleSet,
                        pointObstacleTemp, pointTreeCanopyTemp, pointTranslucentTemp);

                    // FIVE-COMPONENT longwave decomposition (opaque + tree + translucent)
                    double surfaceTemp = pointAirTemp;
                    double refTemp = surfaceTemp;
                    double deltaT_lw_total = 0, deltaT_lw_sky = 0, deltaT_lw_ground = 0;
                    double deltaT_lw_obstacle = 0, deltaT_lw_tree = 0, deltaT_lw_translucent = 0;

                    if (hourConfig.IncludeLongwave)
                    {
                        double skyTemp = MRTModel.CalculateSkyTemperature(record.HorizontalInfraredRadiation, effectiveSkyEps);
                        double lwCoeff = hourConfig.LongwaveLinearCoeff > 0 ? hourConfig.LongwaveLinearCoeff : 0.5;
                        double groundTemp = pointGroundTemp ?? surfaceTemp;

                        deltaT_lw_sky = lwCoeff * skyViewFactors[p] * (skyTemp - refTemp);
                        deltaT_lw_ground = lwCoeff * groundViewFactors[p] * (groundTemp - refTemp);
                        deltaT_lw_obstacle = lwCoeff * obstacleViewFactors[p] * (pointObstacleTemp - refTemp);
                        deltaT_lw_tree = lwCoeff * treeViewFactors[p] * (pointTreeCanopyTemp - refTemp);
                        deltaT_lw_translucent = lwCoeff * translucentViewFactors[p] * (pointTranslucentTemp - refTemp);
                        deltaT_lw_total = deltaT_lw_sky + deltaT_lw_ground + deltaT_lw_obstacle
                                        + deltaT_lw_tree + deltaT_lw_translucent;
                    }

                    double deltaT_sw = mrt - surfaceTemp - deltaT_lw_total;

                    mrtTree.Append(new GH_Number(mrt), new GH_Path(p));
                    exposureTree.Append(new GH_Number(exposureFactors[p]), new GH_Path(p));
                    dniExposureTree.Append(new GH_Number(dniExposureFactors[p]), new GH_Path(p));
                    dTSwTree.Append(new GH_Number(deltaT_sw), new GH_Path(p));
                    dTLwTree.Append(new GH_Number(deltaT_lw_total), new GH_Path(p));
                    dTLwSkyTree.Append(new GH_Number(deltaT_lw_sky), new GH_Path(p));
                    dTLwGroundTree.Append(new GH_Number(deltaT_lw_ground), new GH_Path(p));
                    dTLwObsTree.Append(new GH_Number(deltaT_lw_obstacle), new GH_Path(p));
                    dTLwTreeTree.Append(new GH_Number(deltaT_lw_tree), new GH_Path(p));
                    dTLwTranslucentTree.Append(new GH_Number(deltaT_lw_translucent), new GH_Path(p));

                    hourAvgMRT += mrt;
                }

                hourlyAvgMRT.Add(hourAvgMRT / nPoints);
            }

            // --- MRT-related outputs (indices 0-8) ---
            DA.SetDataTree(0, mrtTree);
            DA.SetDataTree(1, dTSwTree);
            DA.SetDataTree(2, dTLwTree);
            DA.SetDataTree(3, dTLwSkyTree);
            DA.SetDataTree(4, dTLwGroundTree);
            DA.SetDataTree(5, dTLwObsTree);
            DA.SetDataTree(6, dTLwTreeTree);
            DA.SetDataTree(7, dTLwTranslucentTree);
            DA.SetDataList(8, hourlyAvgMRT);
            // --- View factor outputs (indices 9-13) ---
            DA.SetDataList(9, skyViewFactors.Select(s => new GH_Number(s)).Cast<IGH_Goo>().ToList());
            DA.SetDataList(10, groundViewFactors.Select(s => new GH_Number(s)).Cast<IGH_Goo>().ToList());
            DA.SetDataList(11, obstacleViewFactors.Select(s => new GH_Number(s)).Cast<IGH_Goo>().ToList());
            DA.SetDataList(12, treeViewFactors.Select(s => new GH_Number(s)).Cast<IGH_Goo>().ToList());
            DA.SetDataList(13, translucentViewFactors.Select(s => new GH_Number(s)).Cast<IGH_Goo>().ToList());
            // --- Exposure factor outputs (indices 14-15) ---
            DA.SetDataTree(14, exposureTree);
            DA.SetDataTree(15, dniExposureTree);
            // --- Other outputs (index 16) ---
            DA.SetDataList(16, sunVectors);

            double avgExp = exposureTree.FlattenData().OfType<GH_Number>().Select(n => n.Value).DefaultIfEmpty(0).Average();
            double avgDNIExp = dniExposureTree.FlattenData().OfType<GH_Number>().Select(n => n.Value).DefaultIfEmpty(0).Average();
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"MRT complete: {nPoints} points x {numHours}h. " +
                $"OVF={obstacleViewFactors.Average():F3} TVF={treeViewFactors.Average():F3} TRVF={translucentViewFactors.Average():F3} " +
                $"Exp={avgExp:F3} DNIExp={avgDNIExp:F3}");
        }
        private T ExtractSettings<T>(IGH_DataAccess DA, int index) where T : class
        {
            GH_ObjectWrapper wrapper = null;
            if (!DA.GetData(index, ref wrapper)) return null;
            if (wrapper?.Value is T config) return config;
            return null;
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_MRTcal;
        public override Guid ComponentGuid => new Guid("56235527-E4E8-4B10-9216-1A5AAFEF1F60");
    }
}
