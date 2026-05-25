using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace NeosExplorer
{
    public class Destination : GH_Component
    {
        public Destination()
          : base("Destination", "End",
                "Define destination points for agents",
                "Neos", "NeosExplorer")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Agents", "A", "Agent objects", GH_ParamAccess.list);
            pManager.AddPointParameter("Destination", "DP", "Destination position", GH_ParamAccess.item);
            pManager.AddNumberParameter("Strength Factor", "SF", "Destination attraction strength factor", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Radius", "R", "Arrival radius (m)", GH_ParamAccess.item, 1.0);
            pManager.AddBooleanParameter("Add Return Trip", "RT", "Add return trip agents", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
           
            pManager.AddGenericParameter("Agents", "A", "Agents with destination", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Agent> agents = new List<Agent>();
            Point3d position = Point3d.Origin;
            double strength = 1.0;
            double radius = 1.0;
            bool addReturnTrip = false;

            if (!DA.GetDataList(0, agents)) return;
            if (!DA.GetData(1, ref position)) return;
            DA.GetData(2, ref strength);
            DA.GetData(3, ref radius);
            DA.GetData(4, ref addReturnTrip);

            List<Agent> allAgents = new List<Agent>();

            foreach (var agent in agents)
            {
                Agent updatedAgent = agent.ShallowCopy();
                updatedAgent.Destination = position;
                updatedAgent.DestinationStrength = strength; // 设置目的地强度
                updatedAgent.DestinationRadius = radius;
                allAgents.Add(updatedAgent);

                if (addReturnTrip)
                {
                    Agent returnAgent = agent.ShallowCopy();
                    returnAgent.Destination = agent.InitialPosition;
                    returnAgent.InitialPosition = position;
                    returnAgent.Position = position;

                    // 关键修复：显式设置返程agent的目的地强度
                    returnAgent.DestinationStrength = strength;
                    returnAgent.DestinationRadius = radius;

                    if (returnAgent.InterestPoints != null && returnAgent.InterestPoints.Count > 0)
                    {
                        returnAgent.InterestPoints.Reverse();
                        returnAgent.ClearInterestPoint();
                        returnAgent.MoveToNextInterestPoint();
                    }

                    allAgents.Add(returnAgent);
                }
            }

            DA.SetDataList(0, allAgents);
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_destination;
        public override Guid ComponentGuid => new Guid("B5FCC464-E1F1-4F2A-AA6B-9E3E6D9A6B16");
    }
}