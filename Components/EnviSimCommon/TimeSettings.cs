using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Common.Core;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;

namespace EnviSimCommonComponent
{
    public class SimulationTimeSettingsComponent : GH_Component
    {
        public SimulationTimeSettingsComponent()
          : base("Time Settings", "TimeSet",
              "Configures the analysis time period for PV, MRT, and soil thermal simulation using month/day/hour inputs.",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Start Month", "SM",
                "Start month of analysis period (1-12, default 1)", GH_ParamAccess.item, 1);
            pManager.AddIntegerParameter("Start Day", "SD",
                "Start day of analysis period (1-31, default 1)", GH_ParamAccess.item, 1);
            pManager.AddIntegerParameter("Start Hour", "SH",
                "Start hour of day in 24-hour format (0-23, default 0)", GH_ParamAccess.item, 0);
            pManager.AddIntegerParameter("End Month", "EM",
                "End month of analysis period (1-12, default 12)", GH_ParamAccess.item, 12);
            pManager.AddIntegerParameter("End Day", "ED",
                "End day of analysis period (1-31, default 31)", GH_ParamAccess.item, 31);
            pManager.AddIntegerParameter("End Hour", "EH",
                "End hour of day in 24-hour format (0-23, default 23)", GH_ParamAccess.item, 23);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Time Settings", "TimeSet",
                "Encapsulated time period configuration for PV, MRT, and soil thermal simulation", GH_ParamAccess.item);
            pManager.AddIntegerParameter("HOY", "HOY",
                "List of Hours-of-Year (0-based) within the analysis period, from start to end inclusive", GH_ParamAccess.list);
            pManager.AddTextParameter("Period Info", "Info",
                "Human-readable description of the analysis period and corresponding hour-of-year range", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int sM = 1, sD = 1, sH = 0, eM = 12, eD = 31, eH = 23;
            DA.GetData(0, ref sM); DA.GetData(1, ref sD); DA.GetData(2, ref sH);
            DA.GetData(3, ref eM); DA.GetData(4, ref eD); DA.GetData(5, ref eH);

            sM = Math.Max(1, Math.Min(12, sM));
            sD = Math.Max(1, Math.Min(31, sD));
            sH = Math.Max(0, Math.Min(23, sH));
            eM = Math.Max(1, Math.Min(12, eM));
            eD = Math.Max(1, Math.Min(31, eD));
            eH = Math.Max(0, Math.Min(23, eH));

            var config = new SimulationTimeConfig
            {
                StartMonth = sM,
                StartDay = sD,
                StartHour = sH,
                EndMonth = eM,
                EndDay = eD,
                EndHour = eH
            };

            int startHoy, endHoy;
            config.GetHourRange(out startHoy, out endHoy);
            string info = config.ToString() + $" (HOY: {startHoy}-{endHoy}, {(endHoy - startHoy + 1)} hours)";

            // Generate full HOY list from start to end (inclusive)
            List<int> hoyList = new List<int>(endHoy - startHoy + 1);
            for (int h = startHoy; h <= endHoy; h++)
            {
                hoyList.Add(h);
            }

            DA.SetData(0, new GH_ObjectWrapper(config));
            DA.SetDataList(1, hoyList);
            DA.SetData(2, info);  
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_timeSet;
        public override Guid ComponentGuid => new Guid("9A4A79DA-2B2A-4382-A24A-A23384FC4460");
    }
}
