using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using SolarPV.Core;
using NeosEnviSim.Properties;
using System;

namespace SolarPV
{
    /// <summary>
    /// Reads ViewResult.txt from NeosRadSim and outputs SVF, obstacle view factors,
    /// TVF (Tree View Factor), and TRVF (Translucent View Factor).
    ///
    /// ENHANCED (2026-06-16):
    /// - OVF is now OPAQUE-ONLY (previously included all obstacle types)
    /// - Added TVF: tree canopy view factor (directions blocked by tree detail meshes)
    /// - Added TRVF: translucent view factor (directions blocked by translucent shade meshes)
    /// - Conservation: SVF + OVF_opaque + TVF + TRVF = 1.0 (for upper hemisphere)
    /// - These decomposed view factors enable precise diffuse radiation calculation:
    ///   DHI_eff = SVF*DHI + TVF*DHI*exp(-k_c*LAD*l) + TRVF*DHI*tau
    /// </summary>
    public class ReadViewResultComponent : GH_Component
    {
        public ReadViewResultComponent()
          : base("Read View Result", "ReadView",
              "Reads ViewResult.txt from NeosRadSim and outputs Sky View Factors (SVF), " +
              "opaque obstacle view factors (OVF), tree view factors (TVF), and " +
              "translucent view factors (TRVF) for each face.",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("View File Path", "ViewPath", "File path to ViewResult.txt", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            // (0-1) SVF - unchanged
            pManager.AddNumberParameter("Front SVF", "SVF", "Sky View Factor for each face (0-1) from celestial hemisphere (2\u03c0), grouped by panel", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Rear SVF", "RSVF", "Rear-side Sky View Factor for each face (0-1), grouped by panel", GH_ParamAccess.tree);
            // (2-3) OVF - ENHANCED 2026-06-16: now OPAQUE-ONLY
            pManager.AddNumberParameter("Front Opaque VF", "FOVF", "Front OPAQUE obstacle view factor (0-1).", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Rear Opaque VF", "ROVF", "Rear OPAQUE obstacle view factor (0-1).", GH_ParamAccess.tree);
            // (4-5) TVF - NEW 2026-06-16
            pManager.AddNumberParameter("Front Tree VF", "FTVF", "Front tree canopy view factor (0-1). Directions blocked by tree detail meshes. Used in DHI_eff = TVF*DHI*exp(-k*LAD*l).", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Rear Tree VF", "RTVF", "Rear tree canopy view factor (0-1).", GH_ParamAccess.tree);
            // (6-7) TRVF - NEW 2026-06-16
            pManager.AddNumberParameter("Front Translucent VF", "FTRVF", "Front translucent shade view factor (0-1).Directions blocked by translucent shade meshes. Used in DHI_eff = TRVF*DHI*tau.", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Rear Translucent VF", "RTRVF", "Rear translucent shade view factor (0-1).", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = "";
            if (!DA.GetData(0, ref filePath)) return;

            var result = ResultFileParser.LoadViewResult(filePath);
            if (result == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to load view result file.");
                return;
            }

            GH_Structure<GH_Number> svfTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> rsvfTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> fovfTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> rovfTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> ftvfTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> rtvfTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> ftrvfTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> rtrvfTree = new GH_Structure<GH_Number>();

            int panelCount = result.FrontSVF.Count;
            for (int p = 0; p < panelCount; p++)
            {
                int faceCount = result.FrontSVF[p].Count;
                for (int f = 0; f < faceCount; f++)
                {
                    svfTree.Append(new GH_Number(result.FrontSVF[p][f]), new GH_Path(p));
                    rsvfTree.Append(new GH_Number(result.RearSVF[p][f]), new GH_Path(p));
                    fovfTree.Append(new GH_Number(result.FrontObstacleViewFactor[p][f]), new GH_Path(p));
                    rovfTree.Append(new GH_Number(result.RearObstacleViewFactor[p][f]), new GH_Path(p));

                    // NEW 2026-06-16: TVF and TRVF (with fallback to 0 for legacy files)
                    ftvfTree.Append(new GH_Number(
                        result.FrontTVF != null && p < result.FrontTVF.Count && f < result.FrontTVF[p].Count
                        ? result.FrontTVF[p][f] : 0.0), new GH_Path(p));
                    rtvfTree.Append(new GH_Number(
                        result.RearTVF != null && p < result.RearTVF.Count && f < result.RearTVF[p].Count
                        ? result.RearTVF[p][f] : 0.0), new GH_Path(p));
                    ftrvfTree.Append(new GH_Number(
                        result.FrontTRVF != null && p < result.FrontTRVF.Count && f < result.FrontTRVF[p].Count
                        ? result.FrontTRVF[p][f] : 0.0), new GH_Path(p));
                    rtrvfTree.Append(new GH_Number(
                        result.RearTRVF != null && p < result.RearTRVF.Count && f < result.RearTRVF[p].Count
                        ? result.RearTRVF[p][f] : 0.0), new GH_Path(p));
                }
            }

            DA.SetDataTree(0, svfTree);
            DA.SetDataTree(1, rsvfTree);
            DA.SetDataTree(2, fovfTree);
            DA.SetDataTree(3, rovfTree);
            DA.SetDataTree(4, ftvfTree);   
            DA.SetDataTree(5, rtvfTree);   
            DA.SetDataTree(6, ftrvfTree);  
            DA.SetDataTree(7, rtrvfTree);  
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_ReadViewResult;
        public override Guid ComponentGuid => new Guid("281F55E0-1A93-4770-9D07-4E771671DC5E");
    }
}
