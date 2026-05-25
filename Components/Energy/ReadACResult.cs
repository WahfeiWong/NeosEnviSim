using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using SolarPV.Core;
using NeosEnviSim.Properties;
using System;

namespace SolarPV
{
    /// <summary>
    /// Reads ACResult.txt from NeosRadSim and outputs AC generation data.
    /// </summary>
    public class ReadACResultComponent : GH_Component
    {
        public ReadACResultComponent()
          : base("Read AC Result", "ReadAC",
              "Reads ACResult.txt from NeosRadSim and outputs per-face, hourly, and total AC energy (front and rear) after inverter conversion.",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("AC File Path", "ACPath", "File path to ACResult.txt", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Total Front AC per Face", "FAC", "Total front AC energy per face in kWh, grouped by panel", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Total Rear AC per Face", "RAC", "Total rear AC energy per face in kWh, grouped by panel", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Hourly Front AC per Face", "HrFAC", "Hourly front AC energy per face in kWh, path = {panel, face, hour}", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Hourly Rear AC per Face", "HrRAC", "Hourly rear AC energy per face in kWh, path = {panel, face, hour}", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Hourly Total AC", "HrTotAC", "Total hourly AC energy across all faces in kWh", GH_ParamAccess.list);
            pManager.AddNumberParameter("Total Annual Front AC", "TotFAC", "Total front AC energy in kWh", GH_ParamAccess.item);
            pManager.AddNumberParameter("Total Annual Rear AC", "TotRAC", "Total rear AC energy in kWh", GH_ParamAccess.item);
            pManager.AddNumberParameter("Total Annual AC", "TotAC", "Total AC energy (front + rear) in kWh", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = "";
            if (!DA.GetData(0, ref filePath)) return;

            var result = ResultFileParser.LoadACResult(filePath);
            if (result == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to load AC result file.");
                return;
            }

            GH_Structure<GH_Number> facTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> racTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> hrFacTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> hrRacTree = new GH_Structure<GH_Number>();

            for (int p = 0; p < result.FrontACTotalPerFace.Count; p++)
            {
                for (int f = 0; f < result.FrontACTotalPerFace[p].Count; f++)
                {
                    facTree.Append(new GH_Number(result.FrontACTotalPerFace[p][f]), new GH_Path(p));
                    racTree.Append(new GH_Number(result.RearACTotalPerFace[p][f]), new GH_Path(p));

                    for (int h = 0; h < result.HourlyFrontACPerFace[p][f].Count; h++)
                    {
                        hrFacTree.Append(new GH_Number(result.HourlyFrontACPerFace[p][f][h]), new GH_Path(p, f, h));
                    }
                    for (int h = 0; h < result.HourlyRearACPerFace[p][f].Count; h++)
                    {
                        hrRacTree.Append(new GH_Number(result.HourlyRearACPerFace[p][f][h]), new GH_Path(p, f, h));
                    }
                }
            }

            DA.SetDataTree(0, facTree);
            DA.SetDataTree(1, racTree);
            DA.SetDataTree(2, hrFacTree);
            DA.SetDataTree(3, hrRacTree);
            DA.SetDataList(4, result.HourlyTotalAC);
            DA.SetData(5, result.TotalAnnualFrontAC);
            DA.SetData(6, result.TotalAnnualRearAC);
            DA.SetData(7, result.TotalAnnualAC);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_ReadAC;
        public override Guid ComponentGuid => new Guid("B418F7D5-DE79-43CE-A098-31EEE3E49487");
    }
}
