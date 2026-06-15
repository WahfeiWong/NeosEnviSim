using Geometry.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using SolarPV.Core;
using Common.Core;
using ThermalComfort.Core;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SolarPV
{
    /// <summary>
    /// NeosRadSim - General-purpose radiation simulation core component.
    ///
    /// Based on backward ray tracing to calculate solar radiation and sunshine-related physical metrics.
    /// This is the universal calculation engine for the Neos plugin.
    ///
    /// Computes:
    /// - Geometry data (mesh, face centers, sun vectors)
    /// - View factors (SVF, obstacle view factor via hemispherical ray tracing)
    /// - Sunshine duration (hours when reverse ray from face reaches sun unobstructed)
    /// - Radiation (hourly and total effective irradiance per face, front and rear)
    /// - PV system info (cell temperature, effective irradiance, clipping loss)
    /// - DC generation (per-face, hourly, total, front and rear)
    /// - AC generation (per-face, hourly, total, front and rear via inverter model)
    ///
    /// Results are saved to an output folder in 6 categorized files.
    ///
    /// ENHANCED (2026-06-15): Obs input changed from Brep List to ObstacleSet (ObsSet) for
    /// fine-grained DNI transmission calculation, consistent with SpatialSoilThermalSimulator
    /// and Outdoor MRT components. When a ray intersects multiple non-opaque obstacles,
    /// individual DNI transmission factors multiply (Beer-Lambert for trees, fixed tau for
    /// translucent shades). ObsSet input only accepts ObstacleSet type; raw geometry is rejected.
    ///
    /// History:
    /// - 2024-05-03: Original PVSimulator fixes (Perez SVF, per-face temperature, shadow ray offset, IAM, nighttime AC, stratified SVF, leap year)
    /// - 2025-05-04: P1-P11 fixes (Perez fallback, Voc protection, NOCT correction, ref modulesPerString, bifacial warning, etc.)
    /// - 2026-05-04: Auto geometry inference, rear SVF ray tracing, NOCT eta_ref config, Parallel.For exception handling, EPW sub-hourly support
    /// - 2026-05-05: Renamed to NeosRadSim. Added sunshine duration, obstacle view factor, per-face hourly radiation,
    ///              categorized file outputs, 6 reader components.
    /// - 2026-05-22: Unified mesh generation via Geometry.Core (Rhino gridded meshing, aligns with Ladybug LB Generate Point Grid).
    /// - 2026-06-15: Obs input changed to ObstacleSet (ObsSet) with fine-grained DNI transmission (product of multi-obstacle transmissions).
    /// </summary>
    public class NeosRadSim : GH_Component
    {
        public NeosRadSim()
          : base("RadSim", "RadSim",
              "Universal radiation simulation core using EPW data, NREL-SPA solar positioning, " +
              "backward raytracing shading analysis, anisotropic sky models, bifacial modules, and inverter MPPT(Maximum Power Point Tracking) modeling. " +
              "ObsSet input for fine-grained DNI transmission (Beer-Lambert). " +
              "Results are saved to categorized files for downstream components.",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("EPW File", "EPW", "Path to EPW weather file", GH_ParamAccess.item);

            // === ENHANCED (2026-06-15): Obs changed from Brep List to ObstacleSet (ObsSet) ===
            pManager.AddGenericParameter("Obstacle Set", "ObsSet",
                "Optional: Classified obstacle set (ObstacleSet) for fine-grained DNI transmission. " +
                "Connect ObsSet component. Supports opaque buildings (full block), trees " +
                "(Beer-Lambert canopy transmission exp(-k*LAD*s)), and translucent sunshades (fixed tau). " +
                "When multiple non-opaque obstacles intersect the same ray, their individual " +
                "transmission factors multiply for physically correct DNI attenuation. " +
                "IMPORTANT: Only accepts ObstacleSet data type. Raw Mesh/Surface/Brep inputs are " +
                "NOT accepted -- geometry must be pre-processed through the ObsSet component.",
                GH_ParamAccess.item);

            pManager.AddBrepParameter("Measurement Surfaces", "MS", "Sensor placement Surfaces or Breps (will be meshed)", GH_ParamAccess.list);

            pManager.AddGenericParameter("Time Settings", "TimeSet",
                "Optional: Simulation time period configuration", GH_ParamAccess.item);
            pManager.AddGenericParameter("PV Settings", "PVSet",
                "Optional: PV module configuration", GH_ParamAccess.item);
            pManager.AddGenericParameter("Temperature Settings", "TempSet",
                "Optional: Temperature model configuration", GH_ParamAccess.item);
            pManager.AddGenericParameter("Sky Model Settings", "SkySet",
                "Optional: Sky model configuration", GH_ParamAccess.item);
            pManager.AddGenericParameter("Raytracing Settings", "RaySet",
                "Optional: Raytracing configuration", GH_ParamAccess.item);
            pManager.AddGenericParameter("Inverter Settings", "InvSet",
                "Optional: Inverter MPPT configuration", GH_ParamAccess.item);

            pManager.AddTextParameter("Output Folder", "Folder",
                "Folder path where result files will be saved. Created if it does not exist.", GH_ParamAccess.item, "");

            pManager.AddBooleanParameter("Run", "Run", "Set to true to execute simulation", GH_ParamAccess.item, false);

            for (int i = 3; i <= 8; i++)
                pManager[i].Optional = true;
            pManager[1].Optional = true;  // ObsSet
            pManager[9].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Geometry Result", "GeoPath",
                "File path to GeometryResult.txt (mesh, face centers, sun vectors)", GH_ParamAccess.item);
            pManager.AddTextParameter("View Result", "ViewPath",
                "File path to ViewResult.txt (SVF, obstacle view factors)", GH_ParamAccess.item);
            pManager.AddTextParameter("Sun & Radiation Result", "SRPath",
                "File path to SunRadiationResult.txt (sunshine hours, hourly/total radiation)", GH_ParamAccess.item);
            pManager.AddTextParameter("PV Info Result", "InfoPath",
                "File path to PVInfoResult.txt (cell temps, irradiance, clip loss)", GH_ParamAccess.item);
            pManager.AddTextParameter("DC Result", "DCPath",
                "File path to DCResult.txt (per-face hourly/total DC, front and rear)", GH_ParamAccess.item);
            pManager.AddTextParameter("AC Result", "ACPath",
                "File path to ACResult.txt (per-face hourly/total AC, front and rear via inverter model)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string epwPath = "";
            List<Brep> pvPanels = new List<Brep>();
            string outputFolder = "";
            bool run = false;

            if (!DA.GetData(0, ref epwPath)) return;
            if (!DA.GetDataList(2, pvPanels)) return;
            DA.GetData(9, ref outputFolder);
            DA.GetData(10, ref run);

            if (!run)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Set 'Run' to true to execute simulation.");
                return;
            }

            // If no output folder specified, use a default temp location
            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                outputFolder = Path.Combine(Path.GetTempPath(), "NeosRadSim_Results", Guid.NewGuid().ToString("N").Substring(0, 8));
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"No output folder specified. Using temporary: {outputFolder}");
            }

            // =====================================================================
            // 1: ObstacleSet (ENHANCED 2026-06-15)
            // Strict type validation. Only accepts ObstacleSet.
            // Raw Brep/Mesh/Surface inputs are rejected with an error.
            // =====================================================================
            ObstacleSet obstacleSet = null;
            List<Mesh> obstacleMeshes = new List<Mesh>();
            List<Mesh> svfObstacleMeshes = new List<Mesh>();
            GH_ObjectWrapper obsWrapper = null;
            if (DA.GetData(1, ref obsWrapper))
            {
                if (obsWrapper?.Value is ObstacleSet os)
                {
                    obstacleSet = os;
                    // SVF meshes: Opaque + TreeDetail + TranslucentShade (no TreeCanopy)
                    // TreeCanopy is for Beer-Lambert path-length only, not SVF occlusion.
                    if (obstacleSet.OpaqueObjectMeshes != null)
                    {
                        svfObstacleMeshes.AddRange(obstacleSet.OpaqueObjectMeshes);
                        obstacleMeshes.AddRange(obstacleSet.OpaqueObjectMeshes);
                    }
                    if (obstacleSet.TreeDetailMeshes != null)
                    {
                        svfObstacleMeshes.AddRange(obstacleSet.TreeDetailMeshes);
                        obstacleMeshes.AddRange(obstacleSet.TreeDetailMeshes);
                    }
                    if (obstacleSet.TranslucentShadeMeshes != null)
                    {
                        svfObstacleMeshes.AddRange(obstacleSet.TranslucentShadeMeshes);
                        obstacleMeshes.AddRange(obstacleSet.TranslucentShadeMeshes);
                    }
                    if (obstacleSet.TreeCanopyMeshes != null)
                        obstacleMeshes.AddRange(obstacleSet.TreeCanopyMeshes);
                }
                else if (obsWrapper?.Value != null)
                {
                    string typeName = obsWrapper.Value.GetType().Name;
                    if (typeName.Contains("Brep") || typeName.Contains("Mesh") ||
                        typeName.Contains("Surface") || typeName.Contains("Geometry"))
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            $"ObsSet input rejected: received raw {typeName} geometry. " +
                            "You MUST connect the output of the 'ObsSet' component here. " +
                            "Raw Brep/Mesh/Surface inputs are NOT accepted -- geometry must be " +
                            "pre-processed through the ObsSet component.");
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

            var timeConfig = ExtractSettings<SimulationTimeConfig>(DA, 3) ?? new SimulationTimeConfig();
            var pvConfig = ExtractSettings<PVConfig>(DA, 4) ?? new PVConfig();

            if (pvConfig.EnableBifacial)
            {
                bool isRegularArray = pvConfig.RowSpacing > 0.01 && pvConfig.ModuleHeight > 0.01
                    && !double.IsInfinity(pvConfig.RowSpacing);
                if (!isRegularArray)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        "Bifacial shading factor is only validated for regular arrays. " +
                        "Irregular geometry detected (check RowSpacing and ModuleHeight). " +
                        "Set RowSpacing=100m to disable row-to-row shading for non-uniform layouts.");
                }
            }
            var tempConfig = ExtractSettings<TemperatureConfig>(DA, 5) ?? new TemperatureConfig();
            var skyConfig = ExtractSettings<SkyModelConfig>(DA, 6) ?? new SkyModelConfig();
            var rayConfig = ExtractSettings<RaytracingConfig>(DA, 7) ?? new RaytracingConfig();
            var invConfig = ExtractSettings<InverterConfig>(DA, 8) ?? new InverterConfig();

            double systemLossFactor = 1.0 - Math.Max(0.0, Math.Min(1.0, pvConfig.SystemLossFactor));
            int startHoy, endHoy;

            if (!File.Exists(epwPath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "EPW file not found: " + epwPath);
                return;
            }

            if (pvPanels == null || pvPanels.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "No PV panels provided.");
                return;
            }

            EPWData epwData;
            try
            {
                epwData = new EPWData(epwPath);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Loaded EPW: {epwData.City}, {epwData.Country}. " +
                    $"Lat={epwData.Latitude:F2}, Lon={epwData.Longitude:F2}, " +
                    $"Elev={epwData.Elevation:F0}m, Hours={epwData.HourCount}, " +
                    $"RecordsPerHour={epwData.RecordsPerHour}");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to parse EPW: " + ex.Message);
                return;
            }

            timeConfig.GetHourRange(out startHoy, out endHoy, epwData.HourCount - 1);
            startHoy = Math.Max(0, Math.Min(epwData.HourCount - 1, startHoy));
            endHoy = Math.Max(0, Math.Min(epwData.HourCount - 1, endHoy));
            if (startHoy > endHoy) { int tmp = startHoy; startHoy = endHoy; endHoy = tmp; }

            string obsInfo = obstacleSet != null && obstacleSet.HasAnyObstacles
                ? $"ObsSet: Opaque={obstacleSet.OpaqueObjectMeshes?.Count ?? 0}, " +
                  $"TreeDet={obstacleSet.TreeDetailMeshes?.Count ?? 0}, " +
                  $"Canopy={obstacleSet.TreeCanopyMeshes?.Count ?? 0}, " +
                  $"TransShd={obstacleSet.TranslucentShadeMeshes?.Count ?? 0}"
                : $"Obstacle meshes: {obstacleMeshes.Count}";

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, obsInfo);

            List<Mesh> pvMeshes = new List<Mesh>();
            List<List<FaceData>> allFaceData = new List<List<FaceData>>();

            foreach (var brep in pvPanels)
            {
                if (brep == null || !brep.IsValid) continue;

                foreach (var face in brep.Faces)
                {
                    if (!face.IsValid) continue;

                    // Use Geometry.Core: Rhino gridded meshing (fills to irregular edges)
                    Mesh mesh = FaceMesher.CreateGriddedMesh(face, pvConfig.GridResolution);
                    if (mesh == null || mesh.Faces.Count == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            "Failed to create mesh for a PV face. Resolution may be too coarse.");
                        continue;
                    }

                    mesh.Normals.ComputeNormals();
                    mesh.FaceNormals.ComputeFaceNormals();
                    pvMeshes.Add(mesh);

                    bool flipMesh = BrepMeshing.ShouldFlipNormals(mesh);

                    List<FaceData> faceDataList = new List<FaceData>();
                    for (int i = 0; i < mesh.Faces.Count; i++)
                    {
                        Point3d center = FaceExtractor.GetCenter(mesh, i);
                        Vector3d normal = (Vector3d)mesh.FaceNormals[i];

                        if (flipMesh) normal = -normal;
                        normal.Unitize();

                        faceDataList.Add(new FaceData
                        {
                            Center = center,
                            Normal = normal,
                            Area = FaceExtractor.GetArea(mesh, i) * pvConfig.ActiveAreaRatio,
                            TiltAngle = SolarGeometry.TiltAngleFromNormal(normal) * Math.PI / 180.0,
                            SVF = 0,
                            RearSVF = 0,
                            FrontObstacleViewFactor = 0,
                            RearObstacleViewFactor = 0,
                            TotalEnergy = 0,
                            TotalRearEnergy = 0,
                            HourlyEnergy = new List<double>(),
                            HourlyRearEnergy = new List<double>(),
                            FrontSunshineHours = 0,
                            RearSunshineHours = 0,
                            HourlyFrontRadiation = new List<double>(),
                            HourlyRearRadiation = new List<double>(),
                            TotalFrontRadiation = 0,
                            TotalRearRadiation = 0,
                            HourlyFrontDC = new List<double>(),
                            HourlyRearDC = new List<double>(),
                            HourlyFrontAC = new List<double>(),
                            HourlyRearAC = new List<double>()
                        });
                    }
                    allFaceData.Add(faceDataList);
                }
            }

            if (pvMeshes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid PV panel meshes generated.");
                return;
            }

            int totalFaces = allFaceData.Sum(p => p.Count);

            if (pvConfig.EnableBifacial && (pvConfig.RowSpacing < 0 || pvConfig.ModuleHeight < 0))
            {
                var centers = new List<Point3d>();
                var normals = new List<Vector3d>();
                foreach (var panel in allFaceData)
                {
                    foreach (var fd in panel)
                    {
                        centers.Add(fd.Center);
                        normals.Add(fd.Normal);
                    }
                }

                double inferredRowSpacing, inferredModuleHeight, confidence;
                bool success = AutoGeometryInference.InferArrayGeometry(
                    centers, normals,
                    out inferredRowSpacing, out inferredModuleHeight, out confidence);

                if (success)
                {
                    if (pvConfig.RowSpacing < 0)
                        pvConfig.RowSpacing = inferredRowSpacing;
                    if (pvConfig.ModuleHeight < 0)
                        pvConfig.ModuleHeight = inferredModuleHeight;

                    string confidenceMsg = confidence > 0.7 ? "high" :
                                          confidence > 0.4 ? "medium" : "low";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                        $"Auto-inferred geometry: RowSpacing={pvConfig.RowSpacing:F2}m, " +
                        $"ModuleHeight={pvConfig.ModuleHeight:F2}m (confidence: {confidenceMsg})");
                }
                else
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "Failed to auto-infer geometry. Using defaults: RowSpacing=2.0m, Height=1.0m.");
                    if (pvConfig.RowSpacing < 0) pvConfig.RowSpacing = 2.0;
                    if (pvConfig.ModuleHeight < 0) pvConfig.ModuleHeight = 1.0;
                }
            }

            //debug提示信息
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"PV Panels: {pvMeshes.Count} meshes, {totalFaces} total faces, " +
                $"Bifacial={pvConfig.EnableBifacial}, Perez={skyConfig.UsePerezModel}" +
                $", HC={skyConfig.HorizonBrighteningCoeff}, CC={skyConfig.CircumsolarBrighteningCoeff}");


            // Calculate Sky View Factors
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Calculating Sky View Factors...");
            // ENHANCED (2026-06-15): SVF uses Opaque + TreeDetail + TranslucentShade only.
            // TreeCanopy is excluded — it exists only for Beer-Lambert path-length in DNI.
            CalculateSVF(allFaceData, svfObstacleMeshes, pvMeshes, rayConfig.SVFSampleCount, rayConfig.SVFRayOffset);

            if (pvConfig.EnableBifacial)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Calculating rear-side Sky View Factors...");
                CalculateRearSVF(allFaceData, svfObstacleMeshes, pvMeshes, rayConfig.SVFSampleCount, rayConfig.SVFRayOffset);
            }

            int numHours = endHoy - startHoy + 1;
            List<double> hourlyTotalDc = new List<double>(numHours);
            List<double> hourlyTotalAc = new List<double>(numHours);
            List<double> hourlyCellTemps = new List<double>(numHours);
            List<double> hourlyIrradiance = new List<double>(numHours);
            List<double> hourlyAmbientTemps = new List<double>(numHours);
            List<double> hourlyWindSpeeds = new List<double>(numHours);
            List<Vector3d> sunVectors = new List<Vector3d>();
            List<int> analyzedHours = new List<int>();
            double totalClippingLoss = 0;
            double totalInverterLoss = 0;

            for (int i = 0; i < numHours; i++)
            {
                hourlyTotalDc.Add(0);
                hourlyTotalAc.Add(0);
                hourlyCellTemps.Add(0);
                hourlyIrradiance.Add(0);
                hourlyAmbientTemps.Add(0);
                hourlyWindSpeeds.Add(0);
            }

            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Simulating {numHours} hours from HOY {startHoy} to {endHoy}...");

            int fixedModulesPerString = invConfig.ModulesPerString;
            double timeStepHours = epwData.TimeStepHours;

            // =====================================================================
            // Pre-compute all face centers for batch DNI exposure calculation
            // =====================================================================
            var allFaceCenters = new List<Point3d>();
            var faceIndexToPanel = new List<int>(); // map flat face index -> panel index
            var faceIndexToLocal = new List<int>(); // map flat face index -> local face index
            for (int panelIdx = 0; panelIdx < allFaceData.Count; panelIdx++)
            {
                for (int faceIdx = 0; faceIdx < allFaceData[panelIdx].Count; faceIdx++)
                {
                    allFaceCenters.Add(allFaceData[panelIdx][faceIdx].Center);
                    faceIndexToPanel.Add(panelIdx);
                    faceIndexToLocal.Add(faceIdx);
                }
            }

            // Main simulation loop
            for (int hoy = startHoy; hoy <= endHoy; hoy++)
            {
                int hourIndex = hoy - startHoy;
                var record = epwData.GetHour(hoy);

                double windSpeed = Math.Max(0, record.WindSpeed * tempConfig.WindSpeedFactor);
                hourlyAmbientTemps[hourIndex] = record.DryBulbTemperature;
                hourlyWindSpeeds[hourIndex] = windSpeed;

                if (record.DirectNormalRadiation <= 0 && record.DiffuseHorizontalRadiation <= 0)
                {
                    foreach (var panel in allFaceData)
                    {
                        foreach (var fd in panel)
                        {
                            fd.HourlyEnergy.Add(0.0);
                            fd.HourlyFrontDC.Add(0.0);
                            fd.HourlyFrontRadiation.Add(0.0);
                            fd.HourlyFrontAC.Add(0.0);
                            if (pvConfig.EnableBifacial)
                            {
                                fd.HourlyRearEnergy.Add(0.0);
                                fd.HourlyRearDC.Add(0.0);
                                fd.HourlyRearRadiation.Add(0.0);
                                fd.HourlyRearAC.Add(0.0);
                            }
                        }
                    }
                    hourlyTotalDc[hourIndex] = 0;
                    hourlyTotalAc[hourIndex] = 0;
                    hourlyCellTemps[hourIndex] = record.DryBulbTemperature;
                    hourlyIrradiance[hourIndex] = 0;
                    continue;
                }

                var spaData = new SPACalculator.SPAData
                {
                    Year = record.Year,
                    Month = record.Month,
                    Day = record.Day,
                    Hour = record.Hour - 1,
                    Minute = record.Minute,
                    Second = 0,
                    Timezone = epwData.TimeZone,
                    DeltaUt1 = 0,
                    DeltaT = SPACalculator.EstimateDeltaT(record.Year),
                    Longitude = epwData.Longitude,
                    Latitude = epwData.Latitude,
                    Elevation = epwData.Elevation,
                    Pressure = record.AtmosphericPressure / 100.0,
                    Temperature = record.DryBulbTemperature,
                    Function = SPACalculator.CalculationMode.SPA_ZA
                };

                int spaResult = SPACalculator.Calculate(ref spaData);
                if (spaResult != 0)
                {
                    foreach (var panel in allFaceData)
                    {
                        foreach (var fd in panel)
                        {
                            fd.HourlyEnergy.Add(0.0);
                            fd.HourlyFrontDC.Add(0.0);
                            fd.HourlyFrontRadiation.Add(0.0);
                            fd.HourlyFrontAC.Add(0.0);
                            if (pvConfig.EnableBifacial)
                            {
                                fd.HourlyRearEnergy.Add(0.0);
                                fd.HourlyRearDC.Add(0.0);
                                fd.HourlyRearRadiation.Add(0.0);
                                fd.HourlyRearAC.Add(0.0);
                            }
                        }
                    }
                    hourlyTotalDc[hourIndex] = 0;
                    hourlyTotalAc[hourIndex] = 0;
                    hourlyCellTemps[hourIndex] = record.DryBulbTemperature;
                    hourlyIrradiance[hourIndex] = 0;
                    continue;
                }

                if (spaData.Zenith >= 90.8334)
                {
                    foreach (var panel in allFaceData)
                    {
                        foreach (var fd in panel)
                        {
                            fd.HourlyEnergy.Add(0.0);
                            fd.HourlyFrontDC.Add(0.0);
                            fd.HourlyFrontRadiation.Add(0.0);
                            fd.HourlyFrontAC.Add(0.0);
                            if (pvConfig.EnableBifacial)
                            {
                                fd.HourlyRearEnergy.Add(0.0);
                                fd.HourlyRearDC.Add(0.0);
                                fd.HourlyRearRadiation.Add(0.0);
                                fd.HourlyRearAC.Add(0.0);
                            }
                        }
                    }
                    hourlyTotalDc[hourIndex] = 0;
                    hourlyTotalAc[hourIndex] = 0;
                    hourlyCellTemps[hourIndex] = record.DryBulbTemperature;
                    hourlyIrradiance[hourIndex] = 0;
                    continue;
                }

                Vector3d sunVec = SolarGeometry.SunVectorFromAzimuthZenith(spaData.Azimuth, spaData.Zenith);
                sunVectors.Add(sunVec);
                analyzedHours.Add(hoy);

                // =====================================================================
                // ENHANCED (2026-06-15): Batch DNI exposure factor calculation
                // Uses ObstacleSet with fine-grained transmission:
                //   - Opaque: transmission = 0
                //   - Tree: Beer-Lambert canopy transmission
                //   - Translucent: fixed tau per mesh (multiple multiply)
                // Replaces the legacy binary IsShaded for DNI calculation.
                // =====================================================================
                double[] dniTransmissionFactors = null;
                if (obstacleSet != null && obstacleSet.HasAnyObstacles)
                {
                    dniTransmissionFactors = HumanExposureModel.CalculateDNIExposureFactorsBatch(
                        allFaceCenters, sunVec, obstacleSet,
                        0.0, 1, rayConfig.MaxShadowDistance);
                }

                double hourTotalDc = 0;
                double hourTotalIrr = 0;
                double hourTotalCellTemp = 0;
                int faceCount = 0;

                for (int panelIdx = 0; panelIdx < allFaceData.Count; panelIdx++)
                {
                    var faceList = allFaceData[panelIdx];
                    for (int faceIdx = 0; faceIdx < faceList.Count; faceIdx++)
                    {
                        var fd = faceList[faceIdx];

                        double directEffective = 0;
                        double cosTheta = fd.Normal * sunVec;
                        cosTheta = Math.Max(-1.0, Math.Min(1.0, cosTheta));
                        double incidenceRad = Math.Acos(Math.Abs(cosTheta));

                        // ENHANCED (2026-06-15): Fine-grained DNI transmission
                        // Legacy: bool isShaded = IsShaded(...); frontSunVisible = !isShaded;
                        // New: dniTransmissionFactor accounts for partial transmission through
                        // vegetation (Beer-Lambert) and translucent materials (fixed tau).
                        double dniTransmissionFactor = 1.0;
                        if (dniTransmissionFactors != null)
                        {
                            int flatIdx = 0;
                            for (int pi = 0; pi < panelIdx; pi++) flatIdx += allFaceData[pi].Count;
                            flatIdx += faceIdx;
                            dniTransmissionFactor = dniTransmissionFactors[flatIdx];
                        }
                        else if (obstacleMeshes.Count > 0 && cosTheta > 0)
                        {
                            // Legacy fallback: no ObstacleSet, use binary IsShaded
                            bool isShaded = IsShaded(fd.Center, sunVec, obstacleMeshes, pvMeshes,
                                panelIdx, rayConfig.ShadowRayOffset, rayConfig.MaxShadowDistance);
                            dniTransmissionFactor = isShaded ? 0.0 : 1.0;
                        }

                        // Front-side sunshine tracking (geometric: sun above horizon and some DNI reaches face)
                        bool frontSunVisible = dniTransmissionFactor > 0.001 && cosTheta > 0;
                        if (frontSunVisible)
                            fd.FrontSunshineHours += timeStepHours;

                        // Direct irradiance on front side with DNI transmission factor
                        if (cosTheta > 0 && record.DirectNormalRadiation > 0 && dniTransmissionFactor > 0.001)
                        {
                            double iam = SolarGeometry.IncidenceAngleModifier(
                                incidenceRad * 180.0 / Math.PI,
                                pvConfig.IAMCoefficient);
                            directEffective = record.DirectNormalRadiation * cosTheta * iam * dniTransmissionFactor;
                        }

                        double diffuseEffective;
                        if (skyConfig.UsePerezModel && record.DiffuseHorizontalRadiation > 0)
                        {
                            double perezDiffuse = PerezSkyModel.CalculateDiffuseTilted(
                                record.DiffuseHorizontalRadiation,
                                record.DirectNormalRadiation,
                                record.GlobalHorizontalRadiation,
                                spaData.Zenith,
                                fd.TiltAngle,
                                incidenceRad,
                                skyConfig);

                            // When SVF is very low (mostly obstructed), visible sky patch is
                            // small enough that anisotropy is lost; fall back to isotropic.
                            // Threshold 0.3 is empirically chosen for typical urban canyons.
                            if (fd.SVF < 0.3)
                            {
                                diffuseEffective = SolarGeometry.IsotropicDiffuseIrradiance(
                                    record.DiffuseHorizontalRadiation, fd.TiltAngle) * fd.SVF;
                            }
                            else
                            {
                                // Perez assumes full sky dome visibility; scale by SVF to
                                // account for obstacle occlusion of the anisotropic sky.
                                diffuseEffective = perezDiffuse * fd.SVF;
                            }
                        }
                        else
                        {
                            diffuseEffective = SolarGeometry.IsotropicDiffuseIrradiance(
                                record.DiffuseHorizontalRadiation, fd.TiltAngle) * fd.SVF;
                        }

                        double groundReflected = SolarGeometry.GroundReflectedIrradiance(
                            record.GlobalHorizontalRadiation, pvConfig.Albedo, fd.TiltAngle);

                        double totalIrradiance = directEffective + diffuseEffective + groundReflected;
                        totalIrradiance = Math.Max(0, totalIrradiance);

                        // Track front radiation [Wh/m2]
                        fd.HourlyFrontRadiation.Add(totalIrradiance);
                        fd.TotalFrontRadiation += totalIrradiance;

                        hourTotalIrr += totalIrradiance;

                        double faceCellTemp = tempConfig.EnableTemperatureModel
                            ? TemperatureModel.CalculateCellTemperature(
                                record.DryBulbTemperature, totalIrradiance, windSpeed,
                                tempConfig.NOCT, tempConfig.MountingType,
                                pvConfig.Efficiency, tempConfig.NOCTReferenceEfficiency)
                            : record.DryBulbTemperature;

                        hourTotalCellTemp += faceCellTemp;

                        double eff = pvConfig.Efficiency;
                        if (tempConfig.EnableTemperatureModel)
                        {
                            eff = TemperatureModel.CorrectEfficiencyForTemperature(
                                pvConfig.Efficiency, tempConfig.TempCoefficient,
                                faceCellTemp, 25.0, tempConfig.TempCoeffIsPercent);
                        }

                        double faceEnergyKwh = totalIrradiance * fd.Area * eff / 1000.0 * systemLossFactor;
                        fd.HourlyEnergy.Add(faceEnergyKwh);
                        fd.HourlyFrontDC.Add(faceEnergyKwh);
                        fd.TotalEnergy += faceEnergyKwh;
                        hourTotalDc += faceEnergyKwh;

                        double rearEnergyKwh = 0;
                        if (pvConfig.EnableBifacial)
                        {
                            double rearIrradiance = BifacialModel.CalculateRearIrradiance(
                                record.GlobalHorizontalRadiation,
                                record.DiffuseHorizontalRadiation,
                                record.DirectNormalRadiation,
                                pvConfig.Albedo,
                                fd.TiltAngle,
                                pvConfig.ModuleHeight,
                                pvConfig.RowSpacing,
                                fd.SVF,
                                fd.RearSVF,
                                pvConfig.RearGainFactor);

                            double grossArea = fd.Area / pvConfig.ActiveAreaRatio;
                            double rearPowerWatts = BifacialModel.CalculateBifacialGain(
                                rearIrradiance, totalIrradiance,
                                pvConfig.BifacialityFactor, eff,
                                pvConfig.ActiveAreaRatio, grossArea);

                            rearEnergyKwh = rearPowerWatts / 1000.0 * systemLossFactor;
                            fd.HourlyRearEnergy.Add(rearEnergyKwh);
                            fd.HourlyRearDC.Add(rearEnergyKwh);
                            fd.TotalRearEnergy += rearEnergyKwh;
                            hourTotalDc += rearEnergyKwh;
                            fd.HourlyEnergy[fd.HourlyEnergy.Count - 1] += rearEnergyKwh;
                            fd.TotalEnergy += rearEnergyKwh;

                            // Track rear radiation [Wh/m2]
                            fd.HourlyRearRadiation.Add(rearIrradiance);
                            fd.TotalRearRadiation += rearIrradiance;

                            // Rear sunshine: sun visible from back side (geometric check)
                            bool rearSunVisible = false;
                            if (-cosTheta > 0)
                            {
                                // For rear sunshine, use legacy IsShaded (binary) since DNI
                                // transmission factor is for front-side direct radiation only
                                bool isShadedRear = IsShaded(fd.Center, sunVec, obstacleMeshes, pvMeshes, panelIdx,
                                    rayConfig.ShadowRayOffset, rayConfig.MaxShadowDistance);
                                rearSunVisible = !isShadedRear;
                                if (rearSunVisible)
                                    fd.RearSunshineHours += timeStepHours;
                            }
                        }
                        else
                        {
                            fd.HourlyRearEnergy.Add(0.0);
                            fd.HourlyRearDC.Add(0.0);
                            fd.HourlyRearRadiation.Add(0.0);
                        }

                        faceCount++;
                    }
                }

                hourlyTotalDc[hourIndex] = hourTotalDc;
                hourlyIrradiance[hourIndex] = faceCount > 0 ? hourTotalIrr / faceCount : 0;
                hourlyCellTemps[hourIndex] = faceCount > 0 ? hourTotalCellTemp / faceCount : record.DryBulbTemperature;

                // Inverter conversion
                double acEfficiencyFactor = 1.0;
                if (invConfig.EnableInverterModel)
                {
                    double dcPowerWatts = hourTotalDc * 1000.0 / timeStepHours;
                    double acPowerWatts = InverterMPPTModel.AggregateDCtoAC(
                        dcPowerWatts, hourlyCellTemps[hourIndex],
                        invConfig, allFaceData.Count, out double hourlyClipLoss,
                        ref fixedModulesPerString, invConfig.NominalVmp, invConfig.TempCoeffVoltage,
                        invConfig.NominalVoc, invConfig.TempCoeffVoc);

                    hourlyTotalAc[hourIndex] = acPowerWatts / 1000.0 * timeStepHours;
                    totalClippingLoss += hourlyClipLoss / 1000.0 * timeStepHours;

                    double inverterLossThisHour = (hourTotalDc - hourlyTotalAc[hourIndex]);
                    if (inverterLossThisHour > 0)
                        totalInverterLoss += inverterLossThisHour;

                    if (dcPowerWatts > 0)
                        acEfficiencyFactor = acPowerWatts / dcPowerWatts;
                }
                else
                {
                    hourlyTotalAc[hourIndex] = hourTotalDc;
                }

                // Apply AC efficiency factor to each face for per-face AC tracking
                for (int p = 0; p < allFaceData.Count; p++)
                {
                    var faceList = allFaceData[p];
                    for (int f = 0; f < faceList.Count; f++)
                    {
                        var fd = faceList[f];
                        double frontAc = fd.HourlyFrontDC[fd.HourlyFrontDC.Count - 1] * acEfficiencyFactor;
                        double rearAc = 0;
                        if (pvConfig.EnableBifacial)
                            rearAc = fd.HourlyRearDC[fd.HourlyRearDC.Count - 1] * acEfficiencyFactor;

                        fd.HourlyFrontAC.Add(frontAc);
                        fd.HourlyRearAC.Add(rearAc);
                    }
                }
            }

            // Build and save the result container
            var result = new RadiationSimulationResult
            {
                EPWCity = epwData.City,
                EPWCountry = epwData.Country,
                Latitude = epwData.Latitude,
                Longitude = epwData.Longitude,
                TimeZone = epwData.TimeZone,
                Elevation = epwData.Elevation,
                AnalyzedHours = numHours,
                TotalFaces = totalFaces,
                PanelCount = pvMeshes.Count,
                BifacialEnabled = pvConfig.EnableBifacial,
                PerezModelUsed = skyConfig.UsePerezModel,
                InverterModelUsed = invConfig.EnableInverterModel,
                TemperatureModelUsed = tempConfig.EnableTemperatureModel,
                SystemLossFactor = pvConfig.SystemLossFactor
            };

            PopulateGeometryResult(result.Geometry, pvMeshes, allFaceData, sunVectors);
            PopulateViewResult(result.View, allFaceData);
            PopulateSunRadiationResult(result.SunRadiation, allFaceData, pvConfig.EnableBifacial, numHours);
            PopulatePVInfoResult(result.PVInfo, hourlyCellTemps, hourlyIrradiance, hourlyAmbientTemps, hourlyWindSpeeds, totalClippingLoss, totalInverterLoss, numHours);
            PopulateDCResult(result.DC, allFaceData, hourlyTotalDc, pvConfig.EnableBifacial, numHours);
            PopulateACResult(result.AC, allFaceData, hourlyTotalAc, pvConfig.EnableBifacial, numHours);

            try
            {
                result.SaveToFolder(outputFolder);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"Results saved to: {outputFolder}");
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "Failed to save results: " + ex.Message);
                return;
            }

            // Set outputs: file paths (ALL INDICES UNCHANGED)
            DA.SetData(0, Path.Combine(outputFolder, "GeometryResult.txt"));
            DA.SetData(1, Path.Combine(outputFolder, "ViewResult.txt"));
            DA.SetData(2, Path.Combine(outputFolder, "SunRadiationResult.txt"));
            DA.SetData(3, Path.Combine(outputFolder, "PVInfoResult.txt"));
            DA.SetData(4, Path.Combine(outputFolder, "DCResult.txt"));
            DA.SetData(5, Path.Combine(outputFolder, "ACResult.txt"));

            double totalDcKwh = hourlyTotalDc.Sum();
            double totalAcKwh = hourlyTotalAc.Sum();
            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                $"Simulation complete. Total DC: {totalDcKwh:F2} kWh, Total AC: {totalAcKwh:F2} kWh" +
                (pvConfig.EnableBifacial ? $", Rear DC: {result.DC.TotalAnnualRearDC:F2} kWh" : "") +
                (invConfig.EnableInverterModel ? $", Clipping Loss: {totalClippingLoss:F2} kWh" : ""));
        }

        private T ExtractSettings<T>(IGH_DataAccess DA, int index) where T : class
        {
            GH_ObjectWrapper wrapper = null;
            if (!DA.GetData(index, ref wrapper)) return null;
            if (wrapper?.Value is T config) return config;
            return null;
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_RadSim;
        public override Guid ComponentGuid => new Guid("8901C43D-FFBB-43EF-A34C-C7F6D9A749CD");

        #region Result Population

        private void PopulateGeometryResult(GeometryResult geo, List<Mesh> pvMeshes, List<List<FaceData>> allFaceData, List<Vector3d> sunVectors)
        {
            geo.PanelCount = pvMeshes.Count;
            geo.TotalFaces = allFaceData.Sum(p => p.Count);
            geo.SunVectors = new List<Vector3d>(sunVectors);

            foreach (var panel in allFaceData)
            {
                var centers = new List<Point3d>();
                var normals = new List<Vector3d>();
                var areas = new List<double>();
                var tilts = new List<double>();
                foreach (var fd in panel)
                {
                    centers.Add(fd.Center);
                    normals.Add(fd.Normal);
                    areas.Add(fd.Area);
                    tilts.Add(fd.TiltAngle * 180.0 / Math.PI);
                }
                geo.FaceCenters.Add(centers);
                geo.FaceNormals.Add(normals);
                geo.FaceAreas.Add(areas);
                geo.TiltAngles.Add(tilts);
            }

            // Store mesh data for reconstruction
            for (int m = 0; m < pvMeshes.Count; m++)
            {
                var mesh = pvMeshes[m];
                var mdata = new MeshData();
                foreach (var v in mesh.Vertices)
                    mdata.VertexCoordinates.Add(new double[] { v.X, v.Y, v.Z });
                foreach (var f in mesh.Faces)
                    mdata.FaceIndices.Add(new[] { f.A, f.B, f.C, f.D });
                geo.Meshes.Add(mdata);
            }
        }

        private void PopulateViewResult(ViewResult view, List<List<FaceData>> allFaceData)
        {
            view.PanelCount = allFaceData.Count;
            view.TotalFaces = allFaceData.Sum(p => p.Count);
            foreach (var panel in allFaceData)
            {
                var svfList = new List<double>();
                var rearSvfList = new List<double>();
                var obsFront = new List<double>();
                var obsRear = new List<double>();
                foreach (var fd in panel)
                {
                    svfList.Add(fd.SVF);
                    rearSvfList.Add(fd.RearSVF);
                    obsFront.Add(fd.FrontObstacleViewFactor);
                    obsRear.Add(fd.RearObstacleViewFactor);
                }
                view.FrontSVF.Add(svfList);
                view.RearSVF.Add(rearSvfList);
                view.FrontObstacleViewFactor.Add(obsFront);
                view.RearObstacleViewFactor.Add(obsRear);
            }
        }

        private void PopulateSunRadiationResult(SunRadiationResult sr, List<List<FaceData>> allFaceData, bool bifacial, int hourCount)
        {
            sr.PanelCount = allFaceData.Count;
            sr.TotalFaces = allFaceData.Sum(p => p.Count);
            sr.HourCount = hourCount;
            sr.BifacialEnabled = bifacial;
            foreach (var panel in allFaceData)
            {
                var frontSun = new List<double>();
                var rearSun = new List<double>();
                var hourlyFrontRad = new List<List<double>>();
                var hourlyRearRad = new List<List<double>>();
                var totalFrontRad = new List<double>();
                var totalRearRad = new List<double>();
                foreach (var fd in panel)
                {
                    frontSun.Add(fd.FrontSunshineHours);
                    rearSun.Add(fd.RearSunshineHours);
                    hourlyFrontRad.Add(new List<double>(fd.HourlyFrontRadiation));
                    hourlyRearRad.Add(new List<double>(fd.HourlyRearRadiation));
                    totalFrontRad.Add(fd.TotalFrontRadiation);
                    totalRearRad.Add(fd.TotalRearRadiation);
                }
                sr.FrontSunshineHours.Add(frontSun);
                sr.RearSunshineHours.Add(rearSun);
                sr.HourlyFrontRadiation.Add(hourlyFrontRad);
                sr.HourlyRearRadiation.Add(hourlyRearRad);
                sr.TotalFrontRadiation.Add(totalFrontRad);
                sr.TotalRearRadiation.Add(totalRearRad);
            }
        }

        private void PopulatePVInfoResult(PVInfoResult pi, List<double> hourlyCellTemps, List<double> hourlyIrr,
            List<double> hourlyAmbient, List<double> hourlyWind, double clipLoss, double invLoss, int hourCount)
        {
            pi.HourCount = hourCount;
            pi.HourlyCellTemperatures = new List<double>(hourlyCellTemps);
            pi.HourlyEffectiveIrradiance = new List<double>(hourlyIrr);
            pi.HourlyAmbientTemperatures = new List<double>(hourlyAmbient);
            pi.HourlyWindSpeeds = new List<double>(hourlyWind);
            pi.TotalAnnualClippingLoss = clipLoss;
            pi.TotalAnnualInverterLoss = invLoss;
        }

        private void PopulateDCResult(DCResult dc, List<List<FaceData>> allFaceData, List<double> hourlyTotalDc, bool bifacial, int hourCount)
        {
            dc.PanelCount = allFaceData.Count;
            dc.TotalFaces = allFaceData.Sum(p => p.Count);
            dc.HourCount = hourCount;
            dc.BifacialEnabled = bifacial;
            dc.HourlyTotalDC = new List<double>(hourlyTotalDc);

            double totalFront = 0, totalRear = 0;
            foreach (var panel in allFaceData)
            {
                var frontTotal = new List<double>();
                var rearTotal = new List<double>();
                var hourlyFront = new List<List<double>>();
                var hourlyRear = new List<List<double>>();
                foreach (var fd in panel)
                {
                    frontTotal.Add(fd.HourlyFrontDC.Sum());
                    rearTotal.Add(fd.HourlyRearDC.Sum());
                    hourlyFront.Add(new List<double>(fd.HourlyFrontDC));
                    hourlyRear.Add(new List<double>(fd.HourlyRearDC));
                    totalFront += fd.HourlyFrontDC.Sum();
                    totalRear += fd.HourlyRearDC.Sum();
                }
                dc.FrontDCTotalPerFace.Add(frontTotal);
                dc.RearDCTotalPerFace.Add(rearTotal);
                dc.HourlyFrontDCPerFace.Add(hourlyFront);
                dc.HourlyRearDCPerFace.Add(hourlyRear);
            }
            dc.TotalAnnualFrontDC = totalFront;
            dc.TotalAnnualRearDC = totalRear;
            dc.TotalAnnualDC = totalFront + totalRear;
        }

        private void PopulateACResult(ACResult ac, List<List<FaceData>> allFaceData, List<double> hourlyTotalAc, bool bifacial, int hourCount)
        {
            ac.PanelCount = allFaceData.Count;
            ac.TotalFaces = allFaceData.Sum(p => p.Count);
            ac.HourCount = hourCount;
            ac.BifacialEnabled = bifacial;
            ac.HourlyTotalAC = new List<double>(hourlyTotalAc);

            double totalFront = 0, totalRear = 0;
            foreach (var panel in allFaceData)
            {
                var frontTotal = new List<double>();
                var rearTotal = new List<double>();
                var hourlyFront = new List<List<double>>();
                var hourlyRear = new List<List<double>>();
                foreach (var fd in panel)
                {
                    frontTotal.Add(fd.HourlyFrontAC.Sum());
                    rearTotal.Add(fd.HourlyRearAC.Sum());
                    hourlyFront.Add(new List<double>(fd.HourlyFrontAC));
                    hourlyRear.Add(new List<double>(fd.HourlyRearAC));
                    totalFront += fd.HourlyFrontAC.Sum();
                    totalRear += fd.HourlyRearAC.Sum();
                }
                ac.FrontACTotalPerFace.Add(frontTotal);
                ac.RearACTotalPerFace.Add(rearTotal);
                ac.HourlyFrontACPerFace.Add(hourlyFront);
                ac.HourlyRearACPerFace.Add(hourlyRear);
            }
            ac.TotalAnnualFrontAC = totalFront;
            ac.TotalAnnualRearAC = totalRear;
            ac.TotalAnnualAC = totalFront + totalRear;
        }

        #endregion

        #region Private Methods

        private bool IsShaded(Point3d origin, Vector3d sunDir, List<Mesh> obstacles,
            List<Mesh> pvMeshes, int currentPanelIndex,
            double rayOffset, double maxDist)
        {
            Point3d rayOrigin = origin + sunDir * rayOffset;
            Ray3d ray = new Ray3d(rayOrigin, sunDir);

            // Check explicit obstacles first
            if (obstacles != null)
            {
                foreach (var mesh in obstacles)
                {
                    if (mesh == null || mesh.Faces.Count == 0) continue;
                    double t = Intersection.MeshRay(mesh, ray);
                    if (t > 0.01 && t < maxDist)
                    {
                        return true;
                    }
                }
            }

            // Check other detector surfaces (PV panels) as obstacles, excluding self
            if (pvMeshes != null)
            {
                for (int m = 0; m < pvMeshes.Count; m++)
                {
                    if (m == currentPanelIndex) continue; // Skip self to prevent self-occlusion
                    var mesh = pvMeshes[m];
                    if (mesh == null || mesh.Faces.Count == 0) continue;
                    double t = Intersection.MeshRay(mesh, ray);
                    if (t > 0.01 && t < maxDist)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void CalculateSVF(List<List<FaceData>> allFaceData, List<Mesh> obstacles,
            List<Mesh> pvMeshes, int sampleCount, double rayOffset)
        {
            Mesh combinedObstacles = new Mesh();
            if (obstacles != null)
            {
                foreach (var obs in obstacles)
                    if (obs != null) combinedObstacles.Append(obs);
            }

            List<Vector3d> skyDirections = SolarGeometry.GenerateHemisphereDirections(sampleCount);
            int totalSkyDirs = skyDirections.Count;

            bool hasObstacles = combinedObstacles.Faces.Count > 0;
            bool hasOtherPanels = pvMeshes != null && pvMeshes.Count > 0;

            // Fast path: no obstacles at all (neither explicit nor other detector surfaces)
            if (!hasObstacles && !hasOtherPanels)
            {
                foreach (var panel in allFaceData)
                {
                    foreach (var fd in panel)
                    {
                        int relevantDirs = 0;
                        foreach (var dir in skyDirections)
                        {
                            if (dir * fd.Normal > 0.001) relevantDirs++;
                        }
                        fd.SVF = totalSkyDirs > 0 ? (double)relevantDirs / totalSkyDirs : 1.0;
                        fd.FrontObstacleViewFactor = 0.0;
                    }
                }
                return;
            }

            Parallel.For(0, allFaceData.Count, p =>
            {
                try
                {
                    var faceList = allFaceData[p];
                    for (int f = 0; f < faceList.Count; f++)
                    {
                        var fd = faceList[f];
                        int visibleRays = 0;
                        int totalRays = 0;

                        Point3d rayOrigin = fd.Center + fd.Normal * rayOffset;

                        foreach (var dir in skyDirections)
                        {
                            if (dir * fd.Normal < 0.001) continue;
                            totalRays++;

                            Ray3d ray = new Ray3d(rayOrigin, dir);
                            bool isBlocked = false;

                            // Check explicit obstacles
                            if (hasObstacles)
                            {
                                double t = Intersection.MeshRay(combinedObstacles, ray);
                                if (t > 0 && t <= 500)
                                {
                                    isBlocked = true;
                                }
                            }

                            // Check other detector surfaces as obstacles, excluding self
                            if (!isBlocked && hasOtherPanels)
                            {
                                for (int m = 0; m < pvMeshes.Count; m++)
                                {
                                    if (m == p) continue; // Skip self (same panel index)
                                    var mesh = pvMeshes[m];
                                    if (mesh == null || mesh.Faces.Count == 0) continue;
                                    double t = Intersection.MeshRay(mesh, ray);
                                    if (t > 0 && t <= 500)
                                    {
                                        isBlocked = true;
                                        break;
                                    }
                                }
                            }

                            if (!isBlocked)
                                visibleRays++;
                        }

                        fd.SVF = totalRays > 0 ? (double)visibleRays / totalRays : 1.0;
                        fd.FrontObstacleViewFactor = totalRays > 0 ? (double)(totalRays - visibleRays) / totalRays : 0.0;
                    }
                }
                catch
                {
                    var faceList = allFaceData[p];
                    for (int f = 0; f < faceList.Count; f++)
                    {
                        faceList[f].SVF = 0.0;
                        faceList[f].FrontObstacleViewFactor = 1.0;
                    }
                }
            });
        }

        private void CalculateRearSVF(List<List<FaceData>> allFaceData, List<Mesh> obstacles,
            List<Mesh> pvMeshes, int sampleCount, double rayOffset)
        {
            Mesh combinedObstacles = new Mesh();
            if (obstacles != null)
            {
                foreach (var obs in obstacles)
                    if (obs != null) combinedObstacles.Append(obs);
            }

            List<Vector3d> skyDirections = SolarGeometry.GenerateHemisphereDirections(sampleCount);

            bool hasObstacles = combinedObstacles.Faces.Count > 0;
            bool hasOtherPanels = pvMeshes != null && pvMeshes.Count > 0;

            // Fast path: no obstacles at all (neither explicit nor other detector surfaces)
            if (!hasObstacles && !hasOtherPanels)
            {
                foreach (var panel in allFaceData)
                {
                    foreach (var fd in panel)
                    {
                        double vfSkyRear = (1.0 - Math.Cos(fd.TiltAngle)) / 2.0;
                        fd.RearSVF = vfSkyRear;
                        fd.RearObstacleViewFactor = 0.0;
                    }
                }
                return;
            }

            Parallel.For(0, allFaceData.Count, p =>
            {
                try
                {
                    var faceList = allFaceData[p];
                    for (int f = 0; f < faceList.Count; f++)
                    {
                        var fd = faceList[f];
                        Vector3d rearNormal = -fd.Normal;
                        rearNormal.Unitize();

                        int visibleRays = 0;
                        int totalRays = 0;

                        Point3d rayOrigin = fd.Center + rearNormal * rayOffset;

                        foreach (var dir in skyDirections)
                        {
                            if (dir * rearNormal < 0.001) continue;
                            totalRays++;

                            Ray3d ray = new Ray3d(rayOrigin, dir);
                            bool isBlocked = false;

                            // Check explicit obstacles
                            if (hasObstacles)
                            {
                                double t = Intersection.MeshRay(combinedObstacles, ray);
                                if (t > 0 && t <= 500)
                                {
                                    isBlocked = true;
                                }
                            }

                            // Check other detector surfaces as obstacles, excluding self
                            if (!isBlocked && hasOtherPanels)
                            {
                                for (int m = 0; m < pvMeshes.Count; m++)
                                {
                                    if (m == p) continue; // Skip self (same panel index)
                                    var mesh = pvMeshes[m];
                                    if (mesh == null || mesh.Faces.Count == 0) continue;
                                    double t = Intersection.MeshRay(mesh, ray);
                                    if (t > 0 && t <= 500)
                                    {
                                        isBlocked = true;
                                        break;
                                    }
                                }
                            }

                            if (!isBlocked)
                                visibleRays++;
                        }

                        fd.RearSVF = totalRays > 0 ? (double)visibleRays / totalRays : 0.0;
                        fd.RearObstacleViewFactor = totalRays > 0 ? (double)(totalRays - visibleRays) / totalRays : 0.0;
                    }
                }
                catch
                {
                    var faceList = allFaceData[p];
                    for (int f = 0; f < faceList.Count; f++)
                    {
                        faceList[f].RearSVF = 0.0;
                        faceList[f].RearObstacleViewFactor = 1.0;
                    }
                }
            });
        }

        private class FaceData
        {
            public Point3d Center;
            public Vector3d Normal;
            public double Area;
            public double TiltAngle;
            public double SVF;
            public double RearSVF;
            public double FrontObstacleViewFactor;
            public double RearObstacleViewFactor;
            public double TotalEnergy;
            public double TotalRearEnergy;
            public List<double> HourlyEnergy = new List<double>();
            public List<double> HourlyRearEnergy = new List<double>();
            public double FrontSunshineHours;
            public double RearSunshineHours;
            public List<double> HourlyFrontRadiation = new List<double>();
            public List<double> HourlyRearRadiation = new List<double>();
            public double TotalFrontRadiation;
            public double TotalRearRadiation;
            public List<double> HourlyFrontDC = new List<double>();
            public List<double> HourlyRearDC = new List<double>();
            public List<double> HourlyFrontAC = new List<double>();
            public List<double> HourlyRearAC = new List<double>();
        }

        #endregion
    }
}
