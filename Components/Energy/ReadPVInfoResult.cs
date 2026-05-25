using Grasshopper.Kernel;
using SolarPV.Core;
using NeosEnviSim.Properties;
using System;

namespace SolarPV
{
    /// <summary>
    /// Reads PVInfoResult.txt from NeosRadSim and outputs PV system information.
    /// </summary>
    public class ReadPVInfoResultComponent : GH_Component
    {
        public ReadPVInfoResultComponent()
          : base("Read PV Info Result", "ReadPVInfo",
              "Reads PVInfoResult.txt from NeosRadSim and outputs cell temperatures, effective irradiance, ambient temperatures, wind speeds, and inverter losses.",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("PV Info File Path", "InfoPath", "File path to PVInfoResult.txt", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Hourly Cell Temperatures", "Tcell", "Average cell temperature per hour (C)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Hourly Effective Irradiance", "Eeff", "Average effective front-side irradiance per hour (W/m2)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Hourly Ambient Temperatures", "Tamb", "Ambient dry-bulb temperature per hour (C)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Hourly Wind Speeds", "Wind", "Wind speed per hour (m/s)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Total Clipping Loss", "ClipLoss", "Total energy lost due to inverter AC power clipping in kWh", GH_ParamAccess.item);
            pManager.AddNumberParameter("Total Inverter Loss", "InvLoss", "Total energy lost due to inverter efficiency and MPPT in kWh", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = "";
            if (!DA.GetData(0, ref filePath)) return;

            var result = ResultFileParser.LoadPVInfoResult(filePath);
            if (result == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to load PV info result file.");
                return;
            }

            DA.SetDataList(0, result.HourlyCellTemperatures);
            DA.SetDataList(1, result.HourlyEffectiveIrradiance);
            DA.SetDataList(2, result.HourlyAmbientTemperatures);
            DA.SetDataList(3, result.HourlyWindSpeeds);
            DA.SetData(4, result.TotalAnnualClippingLoss);
            DA.SetData(5, result.TotalAnnualInverterLoss);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_ReadPVproperty;
        public override Guid ComponentGuid => new Guid("171E5641-04DB-4429-B892-B458A1DEFFA6");
    }
}
