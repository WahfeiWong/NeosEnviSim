using System;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;

namespace NeosStatistic
{
    public class MathRound : GH_Component
    {
        public MathRound()
          : base("Math.Round",
                 "MR",
                 "C# Math.Round function.Round the input value to a specified number of decimal places.",
                 "Neos",
                 "Statistic")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("number", "N", "input numbers", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("decimal", "decm", "input decimals", GH_ParamAccess.item, 2);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("rounded_number", "RN", "rounded numbers", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double num = 0;
            int decm = 2;
            double rounded_number;
            DA.GetData(0, ref num);
            DA.GetData(1, ref decm);
            rounded_number = Math.Round(num, decm);
            DA.SetData(0, rounded_number);
        }

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// You can add image files to your project resources and access them like this:
        /// return Resources.IconForThisComponent;
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.icon_round;

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("2A3B4C5D-6E7F-8A9B-0C1D-2E3F4A5B6C7D");
    }
}