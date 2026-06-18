using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeosExplorer
{
    public class InterestPoint : GH_Component
    {
        public InterestPoint()
          : base("Point of Interest", "Interest",
                "Define points of interest for agents with behavior control",
                "Neos", "NeosExplorer")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Agents", "A", "Agent objects", GH_ParamAccess.list);
            pManager.AddPointParameter("Points of Interest", "POIs", "POIs positions", GH_ParamAccess.list);
            pManager.AddNumberParameter("Strength Factor", "SF", "Attraction strength factor", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Radius", "R", "Interest radius (m)", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Stay Duration", "D", "Stay duration in time steps", GH_ParamAccess.item, 5);
            pManager.AddNumberParameter("Leave Probability", "LP", "Probability of leaving per step [0-1]", GH_ParamAccess.item, 0.05);
            pManager.AddBooleanParameter("Use Index Order", "IO", "Visit order: false=auto by distance (default), true=by input index", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Keep Return Order", "KRO", "Return trip order: false=reverse (default), true=same as forward. Only effective when Use Index Order=true", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Agents", "A", "Agents with points of interest", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Agent> agents = new List<Agent>();
            List<Point3d> positions = new List<Point3d>();
            double strength = 1.0;
            double radius = 1.0;
            int duration = 5;
            double leaveProbability = 0.05;
            bool useIndexOrder = false;

            if (!DA.GetDataList(0, agents)) return;
            if (!DA.GetDataList(1, positions)) return;
            DA.GetData(2, ref strength);
            DA.GetData(3, ref radius);
            DA.GetData(4, ref duration);
            DA.GetData(5, ref leaveProbability);
            DA.GetData(6, ref useIndexOrder);
            bool keepReturnOrder = false;
            DA.GetData(7, ref keepReturnOrder);

            List<Agent> updatedAgents = new List<Agent>();

            foreach (var agent in agents)
            {
                Agent updatedAgent = agent.ShallowCopy();
                updatedAgent.UseIndexOrder = useIndexOrder;
                updatedAgent.ReverseReturnOrder = keepReturnOrder;

                if (positions.Count > 0)
                {
                    if (useIndexOrder)
                    {
                        // 索引顺序模式：保持输入列表的原始顺序
                        updatedAgent.InterestPoints = new List<Point3d>(positions);
                    }
                    else
                    {
                        // 自动模式：按与起点的距离从小到大排序
                        updatedAgent.InterestPoints = positions
                            .OrderBy(p => p.DistanceTo(updatedAgent.InitialPosition))
                            .ToList();
                    }
                    updatedAgent.ClearInterestPoint();
                    updatedAgent.MoveToNextInterestPoint();
                }
                else
                {
                    updatedAgent.InterestPoints = new List<Point3d>();
                    updatedAgent.ClearInterestPoint();
                }

                updatedAgent.InterestStrength = strength;
                updatedAgent.InterestRadius = radius;
                updatedAgent.StayDuration = duration;
                updatedAgent.LeaveProbability = leaveProbability;
                updatedAgents.Add(updatedAgent);
            }

            DA.SetDataList(0, updatedAgents);
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_interest;
        public override Guid ComponentGuid => new Guid("E3D0EBF3-21D3-431B-ADEB-BC9B4B689813");
    }
}