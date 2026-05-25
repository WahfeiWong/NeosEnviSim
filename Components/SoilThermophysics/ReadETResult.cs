using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using NeosEnviSim.Properties;
using SoilThermophysics.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SoilThermophysics
{
    /// <summary>
    /// Read ET Result component.
    /// Extracts total evapotranspiration (ET) and reference ET.
    /// Outputs all points' values grouped by hour (hour-major format).
    /// Tree structure: each branch index = time sequence (one hour), containing all points' values.
    /// </summary>
    public class ReadETResultComponent : GH_Component
    {
        public ReadETResultComponent()
          : base("Read ET Result", "ReadET",
              "Extracts total evapotranspiration (ET) and reference ET. " +
              "Outputs all points per hour.",
              "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Result", "Result",
                "Soil Thermal Simulation Result", GH_ParamAccess.item);
            pManager.AddTextParameter("File Path", "Path",
                "Optional file path to ETResult.txt", GH_ParamAccess.item);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("ET", "ET",
                "Hourly total evapotranspiration [mm/h]. Tree: branch index = hour, each branch = all points at that hour.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("ET Ref", "ETref",
                "Hourly reference ET (FAO56) [mm/h]. Tree: branch index = hour, each branch = all points at that hour.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Annual ET", "ETann",
                "Mean total annual ET [mm] averaged across all points", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ObjectWrapper wrapper = null;
            DA.GetData(0, ref wrapper);

            string filePath = "";
            DA.GetData(1, ref filePath);

            ETResult etResult = null;

            if (wrapper?.Value is SoilThermalSimulationResult sim)
                etResult = sim.ET;
            else if (wrapper?.Value is ETResult direct)
                etResult = direct;

            if (etResult == null && !string.IsNullOrEmpty(filePath))
                etResult = ETParser.Load(filePath);

            if (etResult == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid ET result.");
                return;
            }

            // Transpose from point-major to hour-major format
            var etTransposed = TransposeByHour(etResult.HourlyET);
            var etRefTransposed = TransposeByHour(etResult.HourlyReferenceET);

            // Compute mean annual ET across all points
            double meanAnnualET = 0.0;
            if (etResult.TotalAnnualET != null && etResult.TotalAnnualET.Count > 0)
                meanAnnualET = etResult.TotalAnnualET.Average();

            DA.SetDataTree(0, ToDataTree(etTransposed));
            DA.SetDataTree(1, ToDataTree(etRefTransposed));
            DA.SetData(2, meanAnnualET);
        }

        /// <summary>
        /// Transpose point-major data (List[point][hour]) to hour-major format (List[hour][point]).
        /// Each outer list index represents one hour, containing all points' values for that hour.
        /// </summary>
        private GH_Structure<GH_Number> ToDataTree(List<List<double>> hourMajorData)
        {
            var tree = new GH_Structure<GH_Number>();
            if (hourMajorData == null) return tree;
            for (int h = 0; h < hourMajorData.Count; h++)
            {
                var path = new GH_Path(h);
                var branch = hourMajorData[h];
                if (branch != null)
                    foreach (double val in branch)
                        tree.Append(new GH_Number(val), path);
            }
            return tree;
        }

        private List<List<double>> TransposeByHour(List<List<double>> pointMajorData)
        {
            if (pointMajorData == null || pointMajorData.Count == 0)
                return new List<List<double>>();

            int nPoints = pointMajorData.Count;
            int nHours = 0;
            for (int p = 0; p < nPoints; p++)
                if (pointMajorData[p] != null)
                    nHours = Math.Max(nHours, pointMajorData[p].Count);

            if (nHours == 0) return new List<List<double>>();

            var result = new List<List<double>>(nHours);
            for (int h = 0; h < nHours; h++)
            {
                var hourValues = new List<double>(nPoints);
                for (int p = 0; p < nPoints; p++)
                {
                    var ptList = pointMajorData[p];
                    double val = (ptList != null && h < ptList.Count) ? ptList[h] : 0;
                    hourValues.Add(val);
                }
                result.Add(hourValues);
            }
            return result;
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_readET;
        public override Guid ComponentGuid => new Guid("7C8D0566-0A78-40CA-B098-3850766BB6A8");
    }
}
