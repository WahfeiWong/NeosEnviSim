using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using SolarPV.Core;
using NeosEnviSim.Properties;
using System;

namespace SolarPV
{
    /// <summary>
    /// Reads ViewResult.txt from NeosRadSim and outputs SVF and obstacle view factors.
    /// </summary>
    public class ReadViewResultComponent : GH_Component
    {
        public ReadViewResultComponent()
          : base("Read View Result", "ReadView",
              "Reads ViewResult.txt from NeosRadSim and outputs Sky View Factors and obstacle view factors for each face.",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("View File Path", "ViewPath", "File path to ViewResult.txt", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Front SVF", "SVF", "Sky View Factor for each face (0-1) from celestial hemisphere (2π), grouped by panel", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Rear SVF", "RSVF", "Rear-side Sky View Factor for each face (0-1) from celestial hemisphere (2π), grouped by panel", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Front Obstacle View Factor", "FOVF", "Front obstacle view factor (fraction of hemisphere blocked by obstacles), grouped by panel", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Rear Obstacle View Factor", "ROVF", "Rear obstacle view factor (fraction of hemisphere blocked by obstacles), grouped by panel", GH_ParamAccess.tree);
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

            for (int p = 0; p < result.FrontSVF.Count; p++)
            {
                for (int f = 0; f < result.FrontSVF[p].Count; f++)
                {
                    svfTree.Append(new GH_Number(result.FrontSVF[p][f]), new GH_Path(p));
                    rsvfTree.Append(new GH_Number(result.RearSVF[p][f]), new GH_Path(p));
                    fovfTree.Append(new GH_Number(result.FrontObstacleViewFactor[p][f]), new GH_Path(p));
                    rovfTree.Append(new GH_Number(result.RearObstacleViewFactor[p][f]), new GH_Path(p));
                }
            }

            DA.SetDataTree(0, svfTree);
            DA.SetDataTree(1, rsvfTree);
            DA.SetDataTree(2, fovfTree);
            DA.SetDataTree(3, rovfTree);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_ReadViewResult;
        public override Guid ComponentGuid => new Guid("281F55E0-1A93-4770-9D07-4E771671DC5E");
    }
}
