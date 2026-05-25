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
    /// Read LE Flux Result component.
    /// Extracts latent heat flux, beta, aerodynamic resistance, and soil surface resistance.
    /// Outputs all points' values grouped by hour (hour-major format).
    /// Tree structure: each branch index = time sequence (one hour), containing all points' values.
    /// </summary>
    public class ReadLEFluxResultComponent : GH_Component
    {
        public ReadLEFluxResultComponent()
          : base("Read LE Flux Result", "ReadLE",
              "Extracts latent heat flux (LE), beta factor, " +
              "aerodynamic resistance (ra), and soil surface resistance (rs_soil). " +
              "Outputs all points per hour.",
              "Neos", "Thermophysics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Result", "Result",
                "Soil Thermal Simulation Result", GH_ParamAccess.item);
            pManager.AddTextParameter("File Path", "Path",
                "Optional file path to LEFluxResult.txt", GH_ParamAccess.item);
            pManager[1].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("LE", "LE",
                "Hourly latent heat flux [W/m2]. Tree: branch index = hour, each branch = all points at that hour.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Beta", "Beta",
                "Hourly moisture availability factor [-]. Tree: branch index = hour, each branch = all points at that hour.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Ra", "Ra",
                "Hourly aerodynamic resistance [s/m]. Tree: branch index = hour, each branch = all points at that hour.",
                GH_ParamAccess.list);
            pManager.AddNumberParameter("Rs Soil", "RsSoil",
                "Hourly soil surface resistance [s/m]. Tree: branch index = hour, each branch = all points at that hour.",
                GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_ObjectWrapper wrapper = null;
            DA.GetData(0, ref wrapper);

            string filePath = "";
            DA.GetData(1, ref filePath);

            LEFluxResult leResult = null;

            if (wrapper?.Value is SoilThermalSimulationResult sim)
                leResult = sim.LEFlux;
            else if (wrapper?.Value is LEFluxResult direct)
                leResult = direct;

            if (leResult == null && !string.IsNullOrEmpty(filePath))
                leResult = LEFluxParser.Load(filePath);

            if (leResult == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid LE flux result.");
                return;
            }

            // Transpose from point-major to hour-major format
            var leTransposed = TransposeByHour(leResult.HourlyLE);
            var betaTransposed = TransposeByHour(leResult.HourlyBeta);
            var raTransposed = TransposeByHour(leResult.HourlyRa);
            var rsSoilTransposed = TransposeByHour(leResult.HourlyRsSoil);

            DA.SetDataTree(0, ToDataTree(leTransposed));
            DA.SetDataTree(1, ToDataTree(betaTransposed));
            DA.SetDataTree(2, ToDataTree(raTransposed));
            DA.SetDataTree(3, ToDataTree(rsSoilTransposed));
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
        protected override System.Drawing.Bitmap Icon => Resources.icon_readLE;
        public override Guid ComponentGuid => new Guid("92FC65E2-49F7-4936-92D0-9F79D3D68FFB");
    }
}
