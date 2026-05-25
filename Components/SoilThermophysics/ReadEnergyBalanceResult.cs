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
    /// Read Energy Balance Result component.
    /// Extracts surface energy balance four components: Rn, H, LE, G.
    /// Includes closure check (Rn - H - LE - G).
    /// Outputs all points' values grouped by hour (hour-major format).
    /// Tree structure: each branch index = time sequence (one hour), containing all points' values.
    /// </summary>
    public class ReadEnergyBalanceResultComponent : GH_Component
    {
        public ReadEnergyBalanceResultComponent()
          : base("Read Energy Balance Result", "ReadEB",
              "Extracts surface energy balance components: net radiation (Rn), " +
              "sensible heat (H), latent heat (LE), and ground heat flux (G). " +
              "Includes energy balance closure check. Outputs all points per hour.",
              "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Result", "Result",
                "Soil Thermal Simulation Result", GH_ParamAccess.item);
            pManager.AddTextParameter("File Path", "Path",
                "Optional file path to EnergyBalanceResult.txt", GH_ParamAccess.item);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Rn", "Rn",
                "Hourly net radiation [W/m2]. Tree: branch index = hour, each branch = all points at that hour.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("H", "H",
                "Hourly sensible heat flux [W/m2]. Tree: branch index = hour, each branch = all points at that hour.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("LE", "LE",
                "Hourly latent heat flux [W/m2]. Tree: branch index = hour, each branch = all points at that hour.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("G", "G",
                "Hourly ground heat flux [W/m2]. Tree: branch index = hour, each branch = all points at that hour.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Closure", "Close",
                "Energy balance closure residual (Rn-H-LE-G) [W/m2]. Tree: branch index = hour, each branch = all points." +
                "\nClosure = 0 by construction (G is computed as residual)",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Mean Rn", "RnMean",
                "Mean net radiation [W/m2] across all points and hours", GH_ParamAccess.item);
            pManager.AddNumberParameter("Mean H", "HMean",
                "Mean sensible heat [W/m2] across all points and hours", GH_ParamAccess.item);
            pManager.AddNumberParameter("Mean LE", "LEMean",
                "Mean latent heat [W/m2] across all points and hours", GH_ParamAccess.item);
            pManager.AddNumberParameter("Mean G", "GMean",
                "Mean ground heat [W/m2] across all points and hours", GH_ParamAccess.item);
            pManager.AddNumberParameter("Bowen Ratio", "Bowen",
                "Mean Bowen ratio (H/LE) across all points and hours", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ObjectWrapper wrapper = null;
            DA.GetData(0, ref wrapper);

            string filePath = "";
            DA.GetData(1, ref filePath);

            EnergyBalanceResult ebResult = null;

            if (wrapper?.Value is SoilThermalSimulationResult sim)
                ebResult = sim.EnergyBalance;
            else if (wrapper?.Value is EnergyBalanceResult direct)
                ebResult = direct;

            if (ebResult == null && !string.IsNullOrEmpty(filePath))
                ebResult = EnergyBalanceParser.Load(filePath);

            if (ebResult == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid energy balance result.");
                return;
            }

            // Extract data for all points
            int nPts = ebResult.HourlyNetRadiation?.Count ?? 0;
            if (nPts == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Empty energy balance data.");
                return;
            }

            // Get per-point time series
            var allRn = new List<List<double>>();
            var allH = new List<List<double>>();
            var allLE = new List<List<double>>();
            var allG = new List<List<double>>();
            var allClosure = new List<List<double>>();

            for (int p = 0; p < nPts; p++)
            {
                var rn = GetSafe(ebResult.HourlyNetRadiation, p);
                var h = GetSafe(ebResult.HourlySensibleHeat, p);
                var le = GetSafe(ebResult.HourlyLatentHeat, p);
                var g = GetSafe(ebResult.HourlyGroundHeat, p);

                // Compute closure per point
                int n = Math.Min(Math.Min(rn.Count, h.Count), Math.Min(le.Count, g.Count));
                var closure = new List<double>(n);
                for (int i = 0; i < n; i++)
                    closure.Add(rn[i] - h[i] - le[i] - g[i]);

                allRn.Add(rn);
                allH.Add(h);
                allLE.Add(le);
                allG.Add(g);
                allClosure.Add(closure);
            }

            // Transpose from point-major to hour-major format
            var rnTransposed = TransposeByHour(allRn);
            var hTransposed = TransposeByHour(allH);
            var leTransposed = TransposeByHour(allLE);
            var gTransposed = TransposeByHour(allG);
            var closureTransposed = TransposeByHour(allClosure);

            // Compute global means across all points and hours
            double meanRn = ComputeGlobalMean(allRn);
            double meanH = ComputeGlobalMean(allH);
            double meanLE = ComputeGlobalMean(allLE);
            double meanG = ComputeGlobalMean(allG);
            double bowen = meanLE > 0.1 ? meanH / meanLE : 0;

            DA.SetDataTree(0, ToDataTree(rnTransposed));
            DA.SetDataTree(1, ToDataTree(hTransposed));
            DA.SetDataTree(2, ToDataTree(leTransposed));
            DA.SetDataTree(3, ToDataTree(gTransposed));
            DA.SetDataTree(4, ToDataTree(closureTransposed));
            DA.SetData(5, meanRn);
            DA.SetData(6, meanH);
            DA.SetData(7, meanLE);
            DA.SetData(8, meanG);
            DA.SetData(9, bowen);
        }

        private List<double> GetSafe(List<List<double>> data, int idx)
        {
            return (data != null && idx >= 0 && idx < data.Count)
                ? (data[idx] ?? new List<double>())
                : new List<double>();
        }

        /// <summary>
        /// Compute global mean across all points and all hours.
        /// </summary>
        private double ComputeGlobalMean(List<List<double>> pointMajorData)
        {
            if (pointMajorData == null || pointMajorData.Count == 0) return 0;
            double sum = 0;
            int count = 0;
            foreach (var ptList in pointMajorData)
            {
                if (ptList != null && ptList.Count > 0)
                {
                    sum += ptList.Sum();
                    count += ptList.Count;
                }
            }
            return count > 0 ? sum / count : 0;
        }

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

        /// <summary>
        /// Transpose point-major data (List[point][hour]) to hour-major format (List[hour][point]).
        /// Each outer list index represents one hour, containing all points' values for that hour.
        /// </summary>
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
        protected override System.Drawing.Bitmap Icon => Resources.icon_readEnergy;
        public override Guid ComponentGuid => new Guid("3E0C8B7A-E33E-42AA-8208-3F44ED61B2B3");
    }
}
