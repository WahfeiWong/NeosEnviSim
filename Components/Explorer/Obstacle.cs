using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;

namespace NeosExplorer
{
    public class ObstacleComponent : GH_Component
    {
        public ObstacleComponent()
          : base("Obstacle", "Obstacle",
                "Define obstacles for agents",
                "Neos", "NeosExplorer")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Obstacles", "O", "Closed obstacle polyline(Draw counterclockwise)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Repulsion", "R", "Repulsion strength (N)", GH_ParamAccess.item, 500.0);
            pManager.AddNumberParameter("Effect Distance", "D", "Effect distance (m)", GH_ParamAccess.item, 0.2);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Obstacle", "O", "Obstacle object", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Curve curve = null;
            double repulsion = 500.0;
            double distance = 0.2;

            if (!DA.GetData(0, ref curve)) return;
            DA.GetData(1, ref repulsion);
            DA.GetData(2, ref distance);

            Obstacle obstacle = new Obstacle(curve)
            {
                RepulsionStrength = repulsion,
                EffectDistance = distance
            };

            DA.SetData(0, obstacle);
        }
        protected override System.Drawing.Bitmap Icon => Resources.icon_obstacle;
        public override Guid ComponentGuid => new Guid("DC98B0BC-32F8-4A7B-8BCE-ACAD2C6F4892");
    }

    public class Obstacle
    {
        public Curve Geometry { get; set; }
        public double RepulsionStrength { get; set; } = 500.0;
        public double EffectDistance { get; set; } = 0.2;

        public Obstacle(Curve geometry)
        {
            Geometry = geometry;
        }

        public Vector3d GetForce(Agent agent)
        {
            double t;
            double maximumDistance = 0.0;
            bool success = Geometry.ClosestPoint(agent.Position, out t, maximumDistance);

            Point3d closestPoint;
            double distance;

            if (success)
            {
                closestPoint = Geometry.PointAt(t);
                distance = agent.Position.DistanceTo(closestPoint);
            }
            else
            {
                closestPoint = agent.Position;
                distance = 0;
            }

            double effectiveDistance = distance - agent.Radius;
            if (effectiveDistance <= 0) effectiveDistance = 0.001;

            Vector3d direction = agent.Position - closestPoint;
            direction.Unitize();

            double forceMagnitude = RepulsionStrength * Math.Exp(-effectiveDistance / EffectDistance);
            return direction * forceMagnitude;
        }
    }
}