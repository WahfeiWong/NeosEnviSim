using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using SolarPV.Core;
using NeosEnviSim.Properties;
using System;

namespace SolarPV
{
    /// <summary>
    /// Reads SunRadiationResult.txt from NeosRadSim and outputs sunshine duration and radiation data.
    /// </summary>
    public class ReadSunRadiationResultComponent : GH_Component
    {
        public ReadSunRadiationResultComponent()
          : base("Read Sun & Radiation Result", "ReadSunRad",
              "Reads SunRadiationResult.txt from NeosRadSim and outputs sunshine hours and radiation (hourly and total) per face.",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Sun Radiation File Path", "SRPath", "File path to SunRadiationResult.txt", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Front Sunshine Hours", "FSun", "Front sunshine duration per face in hours, grouped by panel", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Rear Sunshine Hours", "RSun", "Rear sunshine duration per face in hours, grouped by panel", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Hourly Front Radiation", "HrFRad", "Hourly front effective radiation per face [Wh/m2], path = {panel, face, hour}", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Hourly Rear Radiation", "HrRRad", "Hourly rear effective radiation per face [Wh/m2], path = {panel, face, hour}", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Total Front Radiation", "TotFRad", "Total front effective radiation per face [Wh/m2], grouped by panel", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Total Rear Radiation", "TotRRad", "Total rear effective radiation per face [Wh/m2], grouped by panel", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = "";
            if (!DA.GetData(0, ref filePath)) return;

            var result = ResultFileParser.LoadSunRadiationResult(filePath);
            if (result == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to load sun radiation result file.");
                return;
            }

            GH_Structure<GH_Number> fsunTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> rsunTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> hrFRadTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> hrRRadTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> totFRadTree = new GH_Structure<GH_Number>();
            GH_Structure<GH_Number> totRRadTree = new GH_Structure<GH_Number>();

            for (int p = 0; p < result.FrontSunshineHours.Count; p++)
            {
                for (int f = 0; f < result.FrontSunshineHours[p].Count; f++)
                {
                    fsunTree.Append(new GH_Number(result.FrontSunshineHours[p][f]), new GH_Path(p));
                    rsunTree.Append(new GH_Number(result.RearSunshineHours[p][f]), new GH_Path(p));
                    totFRadTree.Append(new GH_Number(result.TotalFrontRadiation[p][f]), new GH_Path(p));
                    totRRadTree.Append(new GH_Number(result.TotalRearRadiation[p][f]), new GH_Path(p));

                    for (int h = 0; h < result.HourlyFrontRadiation[p][f].Count; h++)
                    {
                        hrFRadTree.Append(new GH_Number(result.HourlyFrontRadiation[p][f][h]), new GH_Path(p, f, h));
                    }
                    for (int h = 0; h < result.HourlyRearRadiation[p][f].Count; h++)
                    {
                        hrRRadTree.Append(new GH_Number(result.HourlyRearRadiation[p][f][h]), new GH_Path(p, f, h));
                    }
                }
            }

            DA.SetDataTree(0, fsunTree);
            DA.SetDataTree(1, rsunTree);
            DA.SetDataTree(2, hrFRadTree);
            DA.SetDataTree(3, hrRRadTree);
            DA.SetDataTree(4, totFRadTree);
            DA.SetDataTree(5, totRRadTree);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_ReadRad;
        public override Guid ComponentGuid => new Guid("539BD57C-F47D-4763-B409-A5EDB7CA7BCC");
    }
}
