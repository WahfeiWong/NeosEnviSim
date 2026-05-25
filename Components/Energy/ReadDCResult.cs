using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using SolarPV.Core;
using NeosEnviSim.Properties;
using System;

namespace SolarPV
{
    /// <summary>
    /// Reads DCResult.txt from NeosRadSim and outputs DC generation data.
    /// </summary>
    public class ReadDCResultComponent : GH_Component
    {
        public ReadDCResultComponent()
          : base("Read DC Result", "ReadDC",
              "Reads DCResult.txt from NeosRadSim and outputs per-face, hourly, and total DC energy (front and rear).",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("DC File Path", "DCPath", "File path to DCResult.txt", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Total Front DC per Face", "FDC", "Total front DC energy per face in kWh, grouped by panel", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Total Rear DC per Face", "RDC", "Total rear DC energy per face in kWh, grouped by panel", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Hourly Front DC per Face", "HrFDC", "Hourly front DC energy per face in kWh, path = {panel, face, hour}", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Hourly Rear DC per Face", "HrRDC", "Hourly rear DC energy per face in kWh, path = {panel, face, hour}", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Hourly Total DC", "HrTotDC", "Total hourly DC energy across all faces in kWh", GH_ParamAccess.list);
            pManager.AddNumberParameter("Total Annual Front DC", "TotFDC", "Total front DC energy in kWh", GH_ParamAccess.item);
            pManager.AddNumberParameter("Total Annual Rear DC", "TotRDC", "Total rear DC energy in kWh", GH_ParamAccess.item);
            pManager.AddNumberParameter("Total Annual DC", "TotDC", "Total DC energy (front + rear) in kWh", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = "";
            if (!DA.GetData(0, ref filePath)) return;

            var result = ResultFileParser.LoadDCResult(filePath);
            if (result == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to load DC result file.");
                return;
            }

            GH_Structure<GH_Number> fdcTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> rdcTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> hrFdcTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> hrRdcTree = new GH_Structure<GH_Number>();

            for (int p = 0; p < result.FrontDCTotalPerFace.Count; p++)
            {
                for (int f = 0; f < result.FrontDCTotalPerFace[p].Count; f++)
                {
                    fdcTree.Append(new GH_Number(result.FrontDCTotalPerFace[p][f]), new GH_Path(p));
                    rdcTree.Append(new GH_Number(result.RearDCTotalPerFace[p][f]), new GH_Path(p));

                    for (int h = 0; h < result.HourlyFrontDCPerFace[p][f].Count; h++)
                    {
                        hrFdcTree.Append(new GH_Number(result.HourlyFrontDCPerFace[p][f][h]), new GH_Path(p, f, h));
                    }
                    for (int h = 0; h < result.HourlyRearDCPerFace[p][f].Count; h++)
                    {
                        hrRdcTree.Append(new GH_Number(result.HourlyRearDCPerFace[p][f][h]), new GH_Path(p, f, h));
                    }
                }
            }

            DA.SetDataTree(0, fdcTree);
            DA.SetDataTree(1, rdcTree);
            DA.SetDataTree(2, hrFdcTree);
            DA.SetDataTree(3, hrRdcTree);
            DA.SetDataList(4, result.HourlyTotalDC);
            DA.SetData(5, result.TotalAnnualFrontDC);
            DA.SetData(6, result.TotalAnnualRearDC);
            DA.SetData(7, result.TotalAnnualDC);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_ReadDC;
        public override Guid ComponentGuid => new Guid("C51CE73F-78E5-4CCA-A694-D0C550D02A7E");
    }
}
