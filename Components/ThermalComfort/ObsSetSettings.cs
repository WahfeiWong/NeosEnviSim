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
            // (0) Tree detail meshes — detailed tree geometry for ray-hit detection
            pManager.AddMeshParameter("Tree Detail", "TreeDet",
                "Optional: Detailed tree geometry mesh (leaves, branches) from Tree Processor. " +
                "When a solar ray hits this mesh, the point is shaded but DNI is partially " +
                "transmitted through the canopy via Beer-Lambert law.",
                GH_ParamAccess.list);

            // (1) Tree canopy meshes — simplified canopy envelopes for path-length calculation
            pManager.AddMeshParameter("Tree Canopy", "TreeCan",
                "Optional: Simplified tree canopy envelope mesh(es) from Tree Processor. " +
                "Used to compute the geometric path length s [m] through vegetation " +
                "along the solar ray direction (entry-to-exit intersection distance).",
                GH_ParamAccess.list);

            // (2) Leaf Area Density (LAD)
            pManager.AddNumberParameter("Leaf Area Density", "LAD",
                "Leaf Area Density (LAD) [m²/m³]. Leaf area per unit volume of canopy. " +
                "Default 1.0. Typical range: 0.5–8.0 depending on species and season.",
                GH_ParamAccess.item, 1.0);

            // (3) Extinction coefficient k
            pManager.AddNumberParameter("Extinction Coeff", "k",
                "Solar radiation extinction coefficient k [-] for Beer-Lambert law. " +
                "Default 0.5. Typical range: 0.5–0.8 (broadleaf), 0.3–0.5 (conifer). " +
                "Equation: I_t = I_DN * exp(-k * LAD * s).",
                GH_ParamAccess.item, 0.5);

            // (4) Translucent sunshade meshes
            pManager.AddMeshParameter("Translucent Shade", "TransShade",
                "Optional: Translucent sunshade / shading device mesh(es). " +
                "These materials partially transmit direct solar radiation " +
                "according to the transmittance parameter.",
                GH_ParamAccess.list);

            // (5) Translucent transmittance
            pManager.AddNumberParameter("Transmittance", "Tans",
                "Direct solar radiation transmittance of translucent sunshades [-]. " +
                "Default 0.05. Range: 0.0 (opaque) to 1.0 (fully transparent). " +
                "Typical: perforated metal 0.05–0.15, fabric 0.02–0.30, PC (Polycarbonate) sheet 0.60–0.85.",
                GH_ParamAccess.item, 0.05);

            // (6) Opaque object meshes
            pManager.AddMeshParameter("Opaque Objects", "Opaque",
                "Optional: Opaque obstacle meshes (buildings, walls, solid structures). " +
                "These fully block direct solar radiation with no transmission.",
                GH_ParamAccess.list);

            // Make all inputs optional
            pManager[0].Optional = true;
            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[3].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Obstacle Set", "ObsSet",
                "Classified obstacle set for MRT component. Encapsulates opaque buildings, " +
                "trees with canopy transmission, and translucent sunshades.",
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

            DA.GetDataList(0, treeDetailMeshes);
            DA.GetDataList(1, treeCanopyMeshes);
            DA.GetData(2, ref leafAreaDensity);
            DA.GetData(3, ref extinctionCoefficient);
            DA.GetDataList(4, translucentShadeMeshes);
            DA.GetData(5, ref translucentTransmittance);
            DA.GetDataList(6, opaqueObjectMeshes);

            // Validate and clamp parameters
            leafAreaDensity = Math.Max(0.01, Math.Min(50.0, leafAreaDensity));
            extinctionCoefficient = Math.Max(0.01, Math.Min(5.0, extinctionCoefficient));
            translucentTransmittance = Math.Max(0.0, Math.Min(1.0, translucentTransmittance));

            // Filter out null/invalid meshes
            treeDetailMeshes = FilterValidMeshes(treeDetailMeshes);
            treeCanopyMeshes = FilterValidMeshes(treeCanopyMeshes);
            translucentShadeMeshes = FilterValidMeshes(translucentShadeMeshes);
            opaqueObjectMeshes = FilterValidMeshes(opaqueObjectMeshes);

            // Build ObstacleSet
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

            // Summary message
            int totalMeshes = treeDetailMeshes.Count + treeCanopyMeshes.Count +
                              translucentShadeMeshes.Count + opaqueObjectMeshes.Count;
            if (totalMeshes > 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark,
                    $"ObsSet: {totalMeshes} meshes — " +
                    $"TreeDet={treeDetailMeshes.Count}, TreeCan={treeCanopyMeshes.Count}, " +
                    $"TransShd={translucentShadeMeshes.Count}, Opaque={opaqueObjectMeshes.Count} | " +
                    $"LAD={leafAreaDensity:F2}, k={extinctionCoefficient:F2}, Tau={translucentTransmittance:F3}");
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
