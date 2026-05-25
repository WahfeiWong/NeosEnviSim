using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using System;

namespace NeosExplorer
{
    public class SimulationSettings : GH_Component
    {
        public SimulationSettings()
          : base("Simulation Settings", "Settings",
                "Configure simulation parameters",
                "Neos", "NeosExplorer")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("Steps Per Iteration", "SPI", "Number of steps to simulate per iteration(iteration steps per visible frame)", GH_ParamAccess.item, 5);
            pManager.AddNumberParameter("Time Per Step", "TPS", "Simulation time per step (s)", GH_ParamAccess.item, 0.1);
            pManager.AddIntegerParameter("Recovery Steps", "RS", "Max steps for backward recovery when encountering congestion/obstacles that halt motion(just for Pedestrian Simulator)", GH_ParamAccess.item, 25);
            pManager.AddIntegerParameter("Max Agents", "MA", "Maximum number of agents in simulation", GH_ParamAccess.item, 20);
            pManager.AddIntegerParameter("Max Iteration Steps", "MI", "Max simulation iterations in time steps (0=unlimited)", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Guide Threshold", "GT", "Guide force unloading threshold(distance in meters)", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Inertia Duration", "ID", "Duration of inertia effect in time steps", GH_ParamAccess.item, 15);
            pManager.AddNumberParameter("Damping Coefficient", "DC", "Velocity damping coefficient [0-1]", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Navigation Offset", "NO", "Offset value for obstacle curves when finding navigation points (m)", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Evacuation Completion Ratio", "ECR", "evacuated agents / initial agents in evacuation area.[0-1]", GH_ParamAccess.item, 0.95);
            // 新增参数：漫游比例
            pManager.AddNumberParameter("Wander Ratio", "WR", "Ratio of agents using explorer mode [0.0 = All Pedestrian, 1.0 = All Explorer]" +
                "\n(just for Hybrid Simulator)", GH_ParamAccess.item, 0.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Settings", "SS", "Simulation settings", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            int steps = 5;
            double timeStep = 0.1;
            int recoverySteps = 25;
            int maxAgents = 20;
            int maxIterations = 0;
            double guideThreshold = 1;
            int inertiaDuration = 15;
            double dampingCoefficient = 0.5;
            double navigationOffset = 0.5;
            double evacuationRatio = 0.95;
            double wanderRatio = 0.0; // 默认值

            DA.GetData(0, ref steps);
            DA.GetData(1, ref timeStep);
            DA.GetData(2, ref recoverySteps);
            DA.GetData(3, ref maxAgents);
            DA.GetData(4, ref maxIterations);
            DA.GetData(5, ref guideThreshold);
            DA.GetData(6, ref inertiaDuration);
            DA.GetData(7, ref dampingCoefficient);
            DA.GetData(8, ref navigationOffset);
            DA.GetData(9, ref evacuationRatio);
            DA.GetData(10, ref wanderRatio); // 获取漫游比例

            // 限制比例在0-1之间
            wanderRatio = Math.Max(0.0, Math.Min(1.0, wanderRatio));

            var settings = new SimulationConfig
            {
                Steps = steps,
                TimeStep = timeStep,
                RecoverySteps = recoverySteps,
                MaxAgents = maxAgents,
                MaxIterations = maxIterations,
                GuideThreshold = guideThreshold,
                InertiaDuration = inertiaDuration,
                DampingCoefficient = dampingCoefficient,
                NavigationOffset = navigationOffset,
                EvacuationRatio = evacuationRatio,
                WanderRatio = wanderRatio
            };

            DA.SetData(0, settings);
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_simulationSettinngs;
        public override Guid ComponentGuid => new Guid("43AB2F78-76C0-40F9-8E01-86E80C179C56");
    }

    public class SimulationConfig
    {
        public int Steps { get; set; } = 5;
        public double TimeStep { get; set; } = 0.1;
        public int RecoverySteps { get; set; } = 25;
        public int MaxAgents { get; set; } = 20;
        public int MaxIterations { get; set; } = 0;
        public double GuideThreshold { get; set; } = 1;
        public int InertiaDuration { get; set; } = 15;
        public double DampingCoefficient { get; set; } = 0.5;
        public double NavigationOffset { get; set; } = 0.5;
        public double EvacuationRatio { get; set; } = 0.95;
        public double WanderRatio { get; set; } = 0.0;
    }
}