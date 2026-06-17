using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Common.Core;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace ThermalComfort
{
    /// <summary>
    /// Obstacle Set (ObsSet) component for MRT calculation.
    /// Classifies obstacles into types (opaque, tree, translucent) to enable
    /// fine-grained direct radiation (DNI) transmission calculation.
    ///
    /// ENHANCED (2026-06-14):
    /// When solar rays are blocked by vegetation or translucent materials,
    /// DNI is partially transmitted rather than fully blocked:
    /// - Trees: Beer-Lambert law  I_t = I_DN * exp(-k * LAD * s)
    /// - Translucent sunshades: fixed transmittance factor
    /// - Opaque objects: fully block DNI (original behavior)
    /// </summary>
    public class ObsSetSettingsComponent : GH_Component
    {
        public ObsSetSettingsComponent()
          : base("Obstacle Set", "ObsSet",
              "Creates a classified obstacle set (ObsSet) for MRT calculation. " +
              "Supports opaque buildings, trees with canopy transmission (Beer-Lambert), " +
              "and translucent sunshades. Connect to MRT component's ObsSet input.",
              "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // ---- Tree-related parameters ----
            pManager.AddMeshParameter("Tree Detail", "TreeDet",
                "Optional: Detailed tree geometry mesh (leaves, branches) from Tree Processor. " +
                "When a solar ray hits this mesh, the point is shaded but DNI is partially " +
                "transmitted through the canopy via Beer-Lambert law.",
                GH_ParamAccess.list);
            pManager.AddMeshParameter("Tree Canopy", "TreeCan",
                "Optional: Simplified tree canopy envelope mesh(es) from Tree Processor. " +
                "Used to compute the geometric path length s [m] through vegetation " +
                "along the solar ray direction (entry-to-exit intersection distance).",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Leaf Area Density", "LAD",
                "Leaf Area Density (LAD) [m2/m3]. Leaf area per unit volume of canopy. " +
                "Default 1.0. Typical range: 0.5-8.0.",
                GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Extinction Coeff", "k",
                "Solar radiation (direct and diffuse solar radiation) extinction coefficient k [-] for Beer-Lambert law. " +
                "Default 0.5. Typical range: 0.5-0.8 (broadleaf), 0.3-0.5 (conifer).",
                GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Tree Temperature", "T_tree",
                "Optional: Tree canopy surface temperature [C]. Single value or 8760 values (hourly). " +
                "If omitted, EPW air temperature is used as fallback in downstream components." +
                "\nThis parameter is not read by the RadSim component; it is used solely for the MRT and soil thermal modules.",
                GH_ParamAccess.list);

            // ---- Translucent shade parameters ----
            pManager.AddMeshParameter("Translucent Shade", "TransShade",
                "Optional: Translucent sunshade / shading device mesh(es). " +
                "These materials partially transmit direct and diffuse solar radiation " +
                "according to the transmittance parameter.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Transmittance", "τ",
                "Shortwave radiation (direct and diffuse solar radiation) transmittance of translucent sunshades [-]. " +
                "Default 0.05. Range: 0.0 (opaque) to 1.0 (fully transparent). " +
                "Typical: perforated metal 0.05-0.15, fabric 0.02-0.30, PC sheet 0.60-0.85.",
                GH_ParamAccess.item, 0.05);
            pManager.AddNumberParameter("Translucent Temperature", "T_trans",
                "Optional: Translucent shade surface temperature [C]. Single value or 8760 values (hourly). " +
                "If omitted, EPW air temperature is used as fallback in downstream components." +
                "\nThis parameter is not read by the RadSim component; it is used solely for the MRT and soil thermal modules.",
                GH_ParamAccess.list);

            // ---- Opaque obstacle parameters ----
            pManager.AddMeshParameter("Opaque Objects", "Opaque",
                "Optional: Opaque obstacle meshes (buildings, walls, solid structures). " +
                "These fully block solar radiation with no transmission.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Opaque Temperature", "T_opaque",
                "Optional: Opaque obstacle surface temperature [C]. Single value or 8760 values (hourly). " +
                "If omitted, EPW air temperature is used as fallback in downstream components." +
                "\nThis parameter is not read by the RadSim component; it is used solely for the MRT and soil thermal modules.",
                GH_ParamAccess.list);

            // Make all inputs optional
            for (int i = 0; i < 10; i++)
                pManager[i].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Obstacle Set", "ObsSet",
                "Classified obstacle set encapsulating opaque buildings, trees with canopy transmission, " +
                "translucent sunshades, and surface temperatures for each obstacle type.",
                GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Read inputs
            List<Mesh> treeDetailMeshes = new List<Mesh>();
            List<Mesh> treeCanopyMeshes = new List<Mesh>();
            double leafAreaDensity = 1.0;
            double extinctionCoefficient = 0.65;
            List<Mesh> translucentShadeMeshes = new List<Mesh>();
            double translucentTransmittance = 0.05;
            List<Mesh> opaqueObjectMeshes = new List<Mesh>();
            List<double> treeTemps = new List<double>();
            List<double> translucentTemps = new List<double>();
            List<double> opaqueTemps = new List<double>();

            DA.GetDataList(0, treeDetailMeshes);
            DA.GetDataList(1, treeCanopyMeshes);
            DA.GetData(2, ref leafAreaDensity);
            DA.GetData(3, ref extinctionCoefficient);
            DA.GetDataList(4, treeTemps);
            DA.GetDataList(5, translucentShadeMeshes);
            DA.GetData(6, ref translucentTransmittance);
            DA.GetDataList(7, translucentTemps);
            DA.GetDataList(8, opaqueObjectMeshes);
            DA.GetDataList(9, opaqueTemps);

            // Validate and clamp parameters
            leafAreaDensity = Math.Max(0.01, Math.Min(50.0, leafAreaDensity));
            extinctionCoefficient = Math.Max(0.01, Math.Min(1.0, extinctionCoefficient));
            translucentTransmittance = Math.Max(0.0, Math.Min(1.0, translucentTransmittance));

            // Filter out null/invalid meshes
            treeDetailMeshes = FilterValidMeshes(treeDetailMeshes);
            treeCanopyMeshes = FilterValidMeshes(treeCanopyMeshes);
            translucentShadeMeshes = FilterValidMeshes(translucentShadeMeshes);
            opaqueObjectMeshes = FilterValidMeshes(opaqueObjectMeshes);

            // Build ObstacleSet with temperatures
            var obstacleSet = new ObstacleSet
            {
                TreeDetailMeshes = treeDetailMeshes,
                TreeCanopyMeshes = treeCanopyMeshes,
                LeafAreaDensity = leafAreaDensity,
                ExtinctionCoefficient = extinctionCoefficient,
                TranslucentShadeMeshes = translucentShadeMeshes,
                TranslucentTransmittance = translucentTransmittance,
                OpaqueObjectMeshes = opaqueObjectMeshes
            };

            // Store temperatures: single value → scalar, 8760 values → hourly list
            if (treeTemps.Count >= 8760)
                obstacleSet.HourlyTreeCanopyTemperatures = treeTemps;
            else if (treeTemps.Count > 0)
                obstacleSet.TreeCanopyTemperature = treeTemps[0];

            if (translucentTemps.Count >= 8760)
                obstacleSet.HourlyTranslucentSurfaceTemperatures = translucentTemps;
            else if (translucentTemps.Count > 0)
                obstacleSet.TranslucentSurfaceTemperature = translucentTemps[0];

            if (opaqueTemps.Count >= 8760)
                obstacleSet.HourlySurroundingSurfaceTemperatures = opaqueTemps;
            else if (opaqueTemps.Count > 0)
                obstacleSet.SurroundingSurfaceTemperature = opaqueTemps[0];

            // Summary message
            int totalMeshes = treeDetailMeshes.Count + treeCanopyMeshes.Count +
                              translucentShadeMeshes.Count + opaqueObjectMeshes.Count;
            if (totalMeshes > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"ObsSet: {totalMeshes} meshes — " +
                    $"TreeDet={treeDetailMeshes.Count}, TreeCan={treeCanopyMeshes.Count}, " +
                    $"TransShd={translucentShadeMeshes.Count}, Opaque={opaqueObjectMeshes.Count} | " +
                    $"LAD={leafAreaDensity:F2}, k={extinctionCoefficient:F2}, tau={translucentTransmittance:F3} | " +
                    $"Ttree={(treeTemps.Count > 0 ? (treeTemps.Count >= 8760 ? "8760h" : $"{treeTemps[0]:F1}C") : "auto")}, " +
                    $"Ttrans={(translucentTemps.Count > 0 ? (translucentTemps.Count >= 8760 ? "8760h" : $"{translucentTemps[0]:F1}C") : "auto")}, " +
                    $"Topaque={(opaqueTemps.Count > 0 ? (opaqueTemps.Count >= 8760 ? "8760h" : $"{opaqueTemps[0]:F1}C") : "auto")}");
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    "ObsSet: no meshes connected. MRT will calculate without obstacles.");
            }

            // Check consistency: if tree detail meshes exist but no canopy meshes,
            // DNI transmission through vegetation cannot be computed (no path length).
            if (treeDetailMeshes.Count > 0 && treeCanopyMeshes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Tree detail meshes provided but no canopy meshes. " +
                    "DNI transmission through vegetation will use zero path length. " +
                    "Connect Tree Canopy meshes for accurate Beer-Lambert calculation.");
            }

            DA.SetData(0, new GH_ObjectWrapper(obstacleSet));
        }

        /// <summary>
        /// Remove null and invalid meshes from the input list.
        /// </summary>
        private List<Mesh> FilterValidMeshes(List<Mesh> meshes)
        {
            var valid = new List<Mesh>();
            if (meshes == null) return valid;
            foreach (var m in meshes)
            {
                if (m != null && m.IsValid && m.Faces.Count > 0)
                    valid.Add(m);
            }
            return valid;
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        public override Guid ComponentGuid => new Guid("FB2ACA0E-DC6E-4802-8E2A-2A5D99E9ECB7");

        protected override System.Drawing.Bitmap Icon => Resources.icon_ObsSet;
    }
}
