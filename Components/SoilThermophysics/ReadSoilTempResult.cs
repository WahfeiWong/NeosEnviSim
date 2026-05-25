using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using SoilThermophysics.Core;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SoilThermophysics
{
    /// <summary>
    /// Read Soil Temperature Result component.
    /// Extracts hourly ground surface temperature (Tg) and deep layer temperature (T2).
    /// Outputs all points' values grouped by hour (hour-major format).
    /// Tree structure: each branch index = time sequence (one hour), containing all points' values.
    /// </summary>
    public class ReadSoilTempResultComponent : GH_Component
    {
        public ReadSoilTempResultComponent()
          : base("Read Soil Temp Result", "ReadTg",
              "Extracts hourly ground temperature (Tg) and deep temperature (T2) " +
              "from Soil Thermal Simulator results. Outputs all points per hour.",
              "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Result", "Result",
                "Soil Thermal Simulation Result (from SoilSim or SpSoilSim)", GH_ParamAccess.item);

            pManager.AddTextParameter("File Path", "Path",
                "Optional: direct file path to SoilTempResult.txt (bypasses Result object)",
                GH_ParamAccess.item);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Tg", "Tg",
                "Hourly ground surface temperature [C]. Tree: branch index = hour, each branch = all points at that hour.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("T2", "T2",
                "Hourly deep layer temperature [C]. Tree: branch index = hour, each branch = all points at that hour.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Tg Mean", "TgMean",
                "Mean ground temperature [C] across all points and hours", GH_ParamAccess.item);
            pManager.AddNumberParameter("Tg Max", "TgMax",
                "Maximum ground temperature [C] across all points and hours", GH_ParamAccess.item);
            pManager.AddNumberParameter("Tg Min", "TgMin",
                "Minimum ground temperature [C] across all points and hours", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ObjectWrapper resultWrapper = null;
            DA.GetData(0, ref resultWrapper);

            string filePath = "";
            DA.GetData(1, ref filePath);

            SoilTempResult tempResult = null;

            // Try Result object first
            if (resultWrapper?.Value is SoilThermalSimulationResult simResult)
            {
                tempResult = simResult.SoilTemp;
            }
            else if (resultWrapper?.Value is SoilTempResult direct)
            {
                tempResult = direct;
            }

            // Fallback to file
            if (tempResult == null && !string.IsNullOrEmpty(filePath))
            {
                tempResult = SoilTempParser.Load(filePath);
            }

            if (tempResult == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No valid result or file path provided.");
                return;
            }

            // Transpose from point-major to hour-major format
            var tgTransposed = TransposeByHour(tempResult.HourlyGroundTemperature);
            var t2Transposed = TransposeByHour(tempResult.HourlyDeepTemperature);

            // Compute global statistics across all points and hours
            double globalSum = 0.0;
            int globalCount = 0;
            double globalMax = double.MinValue;
            double globalMin = double.MaxValue;

            foreach (var hourList in tgTransposed)
            {
                if (hourList == null || hourList.Count == 0) continue;
                foreach (double val in hourList)
                {
                    globalSum += val;
                    globalCount++;
                    if (val > globalMax) globalMax = val;
                    if (val < globalMin) globalMin = val;
                }
            }

            double globalMean = globalCount > 0 ? globalSum / globalCount : 0;
            if (globalMax == double.MinValue) globalMax = 0;
            if (globalMin == double.MaxValue) globalMin = 0;

            // Output as tree: each branch = one hour, containing all points' values
            DA.SetDataTree(0, ToDataTree(tgTransposed));
            DA.SetDataTree(1, ToDataTree(t2Transposed));
            DA.SetData(2, globalMean);
            DA.SetData(3, globalMax);
            DA.SetData(4, globalMin);
        }

        /// <summary>
        /// Convert hour-major List&lt;List&lt;double&gt;&gt; to GH_Structure&lt;GH_Number&gt; for tree output.
        /// Each outer list index becomes a branch path (hour), each inner value becomes a GH_Number.
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
        protected override System.Drawing.Bitmap Icon => Resources.icon_readTemp;
        public override Guid ComponentGuid => new Guid("B3F4AE0B-88D6-4895-9D64-94D57D922074");
    }
}
