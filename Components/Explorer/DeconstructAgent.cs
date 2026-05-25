using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using NeosEnviSim.Properties;
using Rhino.Geometry;

namespace NeosExplorer
{
    public class DeconstructAgent : GH_Component
    {
        public DeconstructAgent()
          : base("Deconstruct Agent", "DeconAg",
                "Visualize crowd simulation results with detailed agent information",
                "Neos", "NeosExplorer")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Agents", "A", "Agent objects", GH_ParamAccess.list);
            pManager.AddPointParameter("Trajectories", "T", "Agent trajectories as point lists", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Cone Scale", "CS", "Scale factor for view cone visualization", GH_ParamAccess.item, 0.05);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
 
            pManager.AddPointParameter("Positions", "P", "Current positions of agents", GH_ParamAccess.list);
            pManager.AddCircleParameter("Circles", "C", "Agent body circles", GH_ParamAccess.list);
            pManager.AddVectorParameter("Velocities", "V", "Current velocity vectors", GH_ParamAccess.list);
            pManager.AddVectorParameter("Forces", "F", "Current force vectors", GH_ParamAccess.list);
            pManager.AddCurveParameter("Trajectories", "Tr", "Agent trajectory curves", GH_ParamAccess.tree);
            pManager.AddPointParameter("Initial Pos", "IP", "Spawn positions", GH_ParamAccess.list);
            pManager.AddPointParameter("Destinations", "D", "Agent destination points", GH_ParamAccess.list);
            pManager.AddPointParameter("Interests", "I", "Current interest points", GH_ParamAccess.list);
            pManager.AddNumberParameter("Speeds", "Sp", "Current speeds (m/s)", GH_ParamAccess.list);
            pManager.AddCurveParameter("View Sectors", "VS", "Agent view sectors as curves (two radii and an arc)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Agent> agents = new List<Agent>();
            if (!DA.GetDataList(0, agents)) return;

            GH_Structure<GH_Point> trajectoriesTree = new GH_Structure<GH_Point>();
            if (!DA.GetDataTree(1, out trajectoriesTree)) return;

            // 获取缩放因子输入
            double coneScale = 0.5;
            DA.GetData(2, ref coneScale);

            // Agent geometry and movement outputs
            List<Point3d> positions = new List<Point3d>();
            List<Circle> circles = new List<Circle>();
            List<Vector3d> velocities = new List<Vector3d>();
            List<Vector3d> forces = new List<Vector3d>();
            GH_Structure<GH_Curve> trajectoryCurves = new GH_Structure<GH_Curve>();

            // 新增InitialPos和ViewSectors
            List<Point3d> initialPositions = new List<Point3d>();
            List<Point3d> destinations = new List<Point3d>();
            List<Point3d> interests = new List<Point3d>();
            List<double> speeds = new List<double>();
            List<Curve> viewSectors = new List<Curve>(); 

            // Process each agent
            for (int i = 0; i < agents.Count; i++)
            {
                Agent agent = agents[i];

                // Basic geometry and movement
                positions.Add(agent.Position);
                circles.Add(new Circle(Plane.WorldXY, agent.Position, agent.Radius));
                velocities.Add(agent.Velocity);
                forces.Add(agent.Force);

                // 收集初始位置
                initialPositions.Add(agent.InitialPosition);

                destinations.Add(agent.Destination);
                interests.Add(agent.InterestPoint ?? Point3d.Unset);
                speeds.Add(agent.Velocity.Length);

                // 生成视野扇形（使用输入的缩放因子）
                try
                {
                    Curve sectorCurve = CreateViewSectorCurve(agent, coneScale);
                    viewSectors.Add(sectorCurve);
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Failed to create view sector for agent {i}: {ex.Message}");
                }

                // Trajectory curves
                if (trajectoriesTree.PathExists(new GH_Path(i)))
                {
                    List<Point3d> points = new List<Point3d>();
                    foreach (GH_Point pt in trajectoriesTree.get_Branch(new GH_Path(i)))
                    {
                        points.Add(pt.Value);
                    }

                    if (points.Count > 1)
                    {
                        trajectoryCurves.Append(new GH_Curve(new Polyline(points).ToPolylineCurve()), new GH_Path(i));
                    }
                    else if (points.Count == 1)
                    {
                        // Single point trajectory: create zero-length line
                        trajectoryCurves.Append(
                            new GH_Curve(new LineCurve(points[0], points[0])),
                            new GH_Path(i)
                        );
                    }
                }
            }

            // Set all outputs
            DA.SetDataList(0, positions);
            DA.SetDataList(1, circles);
            DA.SetDataList(2, velocities);
            DA.SetDataList(3, forces);
            DA.SetDataTree(4, trajectoryCurves);
            DA.SetDataList(5, initialPositions);      // 原Status位置
            DA.SetDataList(6, destinations);
            DA.SetDataList(7, interests);
            DA.SetDataList(8, speeds);
            DA.SetDataList(9, viewSectors);           // 原StayCounters位置
        }

        // 视野扇形生成方法（使用缩放因子）
        private Curve CreateViewSectorCurve(Agent agent, double scaleFactor = 0.5)
        {
            // 计算扇形半径（使用缩放因子）
            double radius = agent.ViewDistance * scaleFactor;
            if (radius < agent.Radius * 2) radius = agent.Radius * 2;

            // 获取运动方向角度 (XY平面)
            double directionAngle = 0.0;
            if (agent.MovementDirection.Length > 0.001)
            {
                Vector3d dir = agent.MovementDirection;
                dir.Z = 0; // 确保在XY平面
                if (dir.Length > 0.001)
                {
                    dir.Unitize();
                    directionAngle = Math.Atan2(dir.Y, dir.X);
                }
            }

            // 计算起始和结束角度
            double startAngle = directionAngle - agent.ViewAngle / 2;
            double endAngle = directionAngle + agent.ViewAngle / 2;

            // 计算圆弧的两个端点
            Point3d startPoint = new Point3d(
                agent.Position.X + Math.Cos(startAngle) * radius,
                agent.Position.Y + Math.Sin(startAngle) * radius,
                agent.Position.Z);

            Point3d endPoint = new Point3d(
                agent.Position.X + Math.Cos(endAngle) * radius,
                agent.Position.Y + Math.Sin(endAngle) * radius,
                agent.Position.Z);

            // 创建两条半径线
            Line radiusLine1 = new Line(agent.Position, startPoint);
            Line radiusLine2 = new Line(agent.Position, endPoint);

            // 创建圆弧
            Curve arcCurve;
            try
            {
                // 使用三点法创建圆弧
                Point3d midPoint = new Point3d(
                    agent.Position.X + Math.Cos(directionAngle) * radius,
                    agent.Position.Y + Math.Sin(directionAngle) * radius,
                    agent.Position.Z);

                Arc arc = new Arc(startPoint, midPoint, endPoint);
                arcCurve = arc.ToNurbsCurve();
            }
            catch
            {
                // 如果三点法失败，使用起点、切向和终点创建圆弧
                try
                {
                    Vector3d tangent = new Vector3d(
                        -Math.Sin(startAngle),
                        Math.Cos(startAngle),
                        0);

                    Arc arc = new Arc(startPoint, tangent, endPoint);
                    arcCurve = arc.ToNurbsCurve();
                }
                catch
                {
                    // 如果所有方法都失败，创建直线
                    arcCurve = new Line(startPoint, endPoint).ToNurbsCurve();
                }
            }

            // 将三条曲线组合成一条复合曲线
            Curve[] curvesToJoin = new Curve[]
            {
                radiusLine1.ToNurbsCurve(),
                arcCurve,
                radiusLine2.ToNurbsCurve()
            };

            // 使用JoinCurves组合曲线
            Curve joinedCurve = Curve.JoinCurves(curvesToJoin, 0.001)[0];

            return joinedCurve;
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_DeconstructAgent;
        public override Guid ComponentGuid => new Guid("4606400D-8DA2-48B5-9881-819D04CC424C");
    }
}