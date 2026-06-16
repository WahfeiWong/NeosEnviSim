using Geometry.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Common.Core;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SoilThermophysics
{
    /// <summary>
    /// Ground Surface Settings component.
    ///
    /// Converts Brep/Surface ground geometry into structured mesh with per-face center points,
    /// converts context obstacles into triangulated meshes,
    /// and encapsulates ground geometry parameters (layer depths, exposure height, SVF samples,
    /// surrounding reflectance/emissivity) and obstacle meshes into a GroundSurfaceConfig
    /// for SpatialSoilThermalSimulator.
    ///
    /// Mesh generation is delegated to Geometry.Core (Rhino gridded meshing),
    /// which fills to irregular edges with both quad and triangulated faces.
    ///
    /// ENHANCED (2026-06-15): ObsSet input removed. Obstacle data should be connected directly
    /// to the SpatialSoilThermalSimulator component's ObsSet input port.
    ///
    /// Input order (required first, then by category):
    ///   0 GSurf     Ground geometry (required)
    ///   1 Res       Mesh resolution
    ///   2 d1        Top layer depth
    ///   3 d2        Deep layer depth
    ///   4 HExp      Exposure tracing height
    ///   5 SVFN      SVF sample count
    ///   6 RhoSur    Surround reflectance
    ///   7 EpsSur    Surround emissivity
    ///
    /// Output order:
    ///   0 GroundSet Encapsulated configuration
    ///   1 Mesh      Generated mesh
    ///   2 Pts       Analysis points
    ///   3 Summary   Human-readable summary
    /// </summary>
    public class GroundSurfaceSettingsComponent : GH_Component
    {
        public GroundSurfaceSettingsComponent()
          : base("Ground Settings", "GroundSet",
              "Converts ground Brep/Surface to mesh + analysis points, " +
              "and encapsulates ground geometry and environment parameters. " +
              "Outputs GroundSet for SpatialSoilThermalSimulator. ", 
              "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // ---- Required: ground geometry ----
            pManager.AddBrepParameter("Ground Surface", "GSurf",
                "Input ground Brep or Surface(s). Each face will be meshed independently.",
                GH_ParamAccess.list);

            // ---- Mesh parameters ----
            pManager.AddNumberParameter("Resolution", "Res",
                "Target grid cell size [m]. Smaller = finer mesh. Default 1.0.",
                GH_ParamAccess.item, 1.0);
            pManager[1].Optional = true;

            // ---- Soil layer depths (contiguous) ----
            pManager.AddNumberParameter("Top Layer d1", "d1",
                "Top soil layer thickness [m]. Default 0.05 (daily forcing).",
                GH_ParamAccess.item, 0.05);
            pManager[2].Optional = true;

            pManager.AddNumberParameter("Deep Layer d2", "d2",
                "Deep soil layer thickness [m]. Default 0.5 (daily average).",
                GH_ParamAccess.item, 0.5);
            pManager[3].Optional = true;

            // ---- Exposure/SVF parameters (contiguous) ----
            pManager.AddNumberParameter("Exposure Height", "HExp",
                "Height above ground for solar exposure ray tracing [m]. Default 0.01.",
                GH_ParamAccess.item, 0.01);
            pManager[4].Optional = true;

            pManager.AddIntegerParameter("SVF Samples", "SVFN",
                "Hemisphere samples for Sky View Factor calculation. Default 500.",
                GH_ParamAccess.item, 500);
            pManager[5].Optional = true;

            // ---- Surrounding environment properties (contiguous) ----
            pManager.AddNumberParameter("Surround Reflectance", "RhoSur",
                "Average shortwave reflectance of surrounding surfaces [-]. " +
                "Typical: grass/soil 0.2, urban 0.15-0.3. Default 0.2.",
                GH_ParamAccess.item, 0.2);
            pManager[6].Optional = true;

            pManager.AddNumberParameter("Surround Emissivity", "EpsSur",
                "Average longwave emissivity of surrounding surfaces [-]. Default 0.95.",
                GH_ParamAccess.item, 0.95);
            pManager[7].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Ground Set", "GroundSet",
                "Encapsulated ground surface configuration for SpatialSoilThermalSimulator",
                GH_ParamAccess.item);

            pManager.AddMeshParameter("Mesh", "Mesh",
                "Generated mesh for the ground surface (quad + triangulated at irregular boundaries)",
                GH_ParamAccess.item);

            pManager.AddPointParameter("Points", "Pts",
                "Analysis points (mesh face centers) on ground surface",
                GH_ParamAccess.list);

            pManager.AddTextParameter("Summary", "Sum",
                "Ground surface configuration summary", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // ---- Input 0: Ground Surface Brep ----
            var breps = new List<Brep>();
            if (!DA.GetDataList(0, breps) || breps == null || breps.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least one ground surface Brep required.");
                return;
            }

            // ---- Inputs 1-7: Parameters (indices shifted -1 after ObsSet removal) ----
            double resolution = 1.0;
            double d1 = 0.05, d2 = 0.5;
            double exposureHeight = 0.01;
            int svfSampleCount = 500;
            double surroundReflectance = 0.2;
            double surroundEmissivity = 0.95;

            DA.GetData(1, ref resolution);
            DA.GetData(2, ref d1);
            DA.GetData(3, ref d2);
            DA.GetData(4, ref exposureHeight);
            DA.GetData(5, ref svfSampleCount);
            DA.GetData(6, ref surroundReflectance);
            DA.GetData(7, ref surroundEmissivity);

            // Clamp values
            resolution = Math.Max(0.01, resolution);
            d1 = Math.Max(0.001, Math.Min(1.0, d1));
            d2 = Math.Max(0.001, Math.Min(5.0, d2));
            exposureHeight = Math.Max(0.0, Math.Min(1.0, exposureHeight));
            svfSampleCount = Math.Max(50, Math.Min(5000, svfSampleCount));
            surroundReflectance = Math.Max(0.0, Math.Min(1.0, surroundReflectance));
            surroundEmissivity = Math.Max(0.0, Math.Min(1.0, surroundEmissivity));

            // (2026-06-15) ObsSet no longer populated from input. Empty default.
            var obstacleSet = new ObstacleSet();

            // ---- Mesh Generation via Geometry.Core ----
            var allMeshes = new List<Mesh>();
            var allPoints = new List<Point3d>();
            int totalFaces = 0;
            int surfaceCount = 0;

            foreach (var brep in breps)
            {
                if (brep == null || !brep.IsValid) continue;

                for (int faceIdx = 0; faceIdx < brep.Faces.Count; faceIdx++)
                {
                    var face = brep.Faces[faceIdx];
                    if (!face.IsValid) continue;

                    // Use Geometry.Core: Rhino gridded meshing (fills to irregular edges)
                    Mesh mesh = FaceMesher.CreateGriddedMesh(face, resolution);
                    if (mesh == null || mesh.Faces.Count == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            $"Face {faceIdx}: failed to create mesh. Resolution may be too coarse.");
                        continue;
                    }

                    mesh.Normals.ComputeNormals();
                    mesh.FaceNormals.ComputeFaceNormals();

                    // Extract face centers as analysis points
                    for (int i = 0; i < mesh.Faces.Count; i++)
                    {
                        Point3d center = FaceExtractor.GetCenter(mesh, i);
                        allPoints.Add(center);
                        totalFaces++;
                    }

                    allMeshes.Add(mesh);
                    surfaceCount++;
                }
            }

            if (allPoints.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    "No valid mesh faces generated. Check input geometry and resolution.");
                return;
            }

            // Merge all meshes into one
            Mesh mergedMesh = new Mesh();
            foreach (var m in allMeshes)
                mergedMesh.Append(m);
            mergedMesh.Normals.ComputeNormals();
            mergedMesh.Compact();

            // ---- Assemble GroundSurfaceConfig ----
            var config = new GroundSurfaceConfig
            {
                AnalysisPoints = allPoints,
                GroundMesh = mergedMesh,
                TopLayerDepth = d1,
                DeepLayerDepth = d2,
                ExposureHeight = exposureHeight,
                SVFSampleCount = svfSampleCount,
                SurroundReflectance = surroundReflectance,
                SurroundEmissivity = surroundEmissivity,
                MeshResolution = resolution,
                ObstacleSet = obstacleSet  // empty (2026-06-15)
            };

            string summary = $"=== Ground Surface Settings ===\n" +
                $"Surfaces ............. {surfaceCount}\n" +
                $"Mesh Faces ........... {totalFaces}\n" +
                $"Analysis Points ...... {allPoints.Count}\n" +
                $"Obstacles (ObsSet) ... None (connect ObsSet directly to SpSoilSim)\n" +
                $"Resolution ........... {resolution:F2} m\n" +
                $"Layer Depths ......... d1={d1:F3}m d2={d2:F3}m\n" +
                $"Exposure Height ...... {exposureHeight:F3} m\n" +
                $"SVF Samples .......... {svfSampleCount}\n" +
                $"Reflectance .......... {surroundReflectance:F2}\n" +
                $"Emissivity ........... {surroundEmissivity:F2}";

            // Output indices unchanged
            DA.SetData(0, new GH_ObjectWrapper(config));
            DA.SetData(1, mergedMesh);
            DA.SetDataList(2, allPoints);
            DA.SetData(3, summary);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_groundEnviSet;
        public override Guid ComponentGuid => new Guid("30140414-3A2D-4628-9DE7-082CD9309F46");
    }
}
