using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using NeosEnviSim.Properties;
using Rhino.Geometry;

namespace NeosExplorer
{
    public class DeconstructAgentPlus : GH_Component
    {
        public DeconstructAgentPlus()
          : base("Deconstruct Agent Plus", "DeconAg+",
                "Enhanced agent visualization with comprehensive outputs and view cones",
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
            // ==== 代理几何与运动 ==== [输出0-5]
            pManager.AddPointParameter("Positions", "P", "Current positions", GH_ParamAccess.list);
            pManager.AddCircleParameter("Circles", "C", "Agent body circles", GH_ParamAccess.list);
            pManager.AddVectorParameter("Velocities", "V", "Velocity vectors", GH_ParamAccess.list);
            pManager.AddVectorParameter("Forces", "F", "Force vectors", GH_ParamAccess.list);
            pManager.AddCurveParameter("Trajectories", "Tr", "Trajectory curves", GH_ParamAccess.tree);
            pManager.AddVectorParameter("Movement Dirs", "MD", "Movement direction vectors", GH_ParamAccess.list);

            // ==== 代理状态信息 ==== [输出6-12]
            pManager.AddTextParameter("Status", "S", "Agent status info", GH_ParamAccess.list);
            pManager.AddPointParameter("Destinations", "D", "Destination points", GH_ParamAccess.list);
            pManager.AddPointParameter("Interests", "I", "Current interest points", GH_ParamAccess.list);
            pManager.AddPointParameter("Initial Pos", "IP", "Spawn positions", GH_ParamAccess.list);
            pManager.AddNumberParameter("Speeds", "Sp", "Current speeds (m/s)", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Stay Count", "SC", "Remaining stay time", GH_ParamAccess.list);
            pManager.AddBooleanParameter("At Interest", "AI", "At interest point", GH_ParamAccess.list);

            // ==== 代理物理属性 ==== [输出13-17]
            pManager.AddNumberParameter("Masses", "M", "Masses (kg)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Desired Speed", "DS", "Desired speeds", GH_ParamAccess.list);
            pManager.AddNumberParameter("Max Speed", "MS", "Max speeds", GH_ParamAccess.list);
            pManager.AddNumberParameter("Radii", "R", "Body radii (m)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Repulsion", "RS", "Repulsion strengths", GH_ParamAccess.list);

            // ==== 感知参数 ==== [输出18-20]
            pManager.AddNumberParameter("View Angles", "VA", "FOV angles (rad)", GH_ParamAccess.list);
            pManager.AddNumberParameter("View Dist", "VD", "View distances (m)", GH_ParamAccess.list);
            pManager.AddCurveParameter("View Sectors", "VS", "Agent view sectors as curves (two radii and an arc)", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 输入处理
            List<Agent> agents = new List<Agent>();
            if (!DA.GetDataList(0, agents)) return;

            GH_Structure<GH_Point> trajectoriesTree;
            if (!DA.GetDataTree(1, out trajectoriesTree)) return;

            double coneScale = 0.5;
            DA.GetData(2, ref coneScale);

            // ===== 输出容器 =====
            // 几何与运动
            var positions = new List<Point3d>();
            var circles = new List<Circle>();
            var velocities = new List<Vector3d>();
            var forces = new List<Vector3d>();
            var movementDirs = new List<Vector3d>();
            var trajectoryCurves = new GH_Structure<GH_Curve>();

            // 状态信息
            var statuses = new List<string>();
            var destinations = new List<Point3d>();
            var interests = new List<Point3d>();
            var initialPositions = new List<Point3d>();
            var speeds = new List<double>();
            var stayCounters = new List<int>();
            var atInterests = new List<bool>();

            // 物理属性
            var masses = new List<double>();
            var desiredSpeeds = new List<double>();
            var maxSpeeds = new List<double>();
            var radii = new List<double>();
            var repulsions = new List<double>();

            // 感知参数
            var viewAngles = new List<double>();
            var viewDistances = new List<double>();
            var viewSectors = new List<Curve>();
            // 处理每个代理
            for (int i = 0; i < agents.Count; i++)
            {
                Agent agent = agents[i];

                // 几何与运动
                positions.Add(agent.Position);
                circles.Add(new Circle(Plane.WorldXY, agent.Position, agent.Radius));
                velocities.Add(agent.Velocity);
                forces.Add(agent.Force);
                movementDirs.Add(agent.MovementDirection);

                // 状态信息
                string status = agent.IsAtInterest ?
                    $"STAYING ({agent.StayCounter}/{agent.StayDuration})" : "MOVING";
                if (agent.JustLeftInterest) status = "LEAVING";

                statuses.Add($"{agent.ID.ToString().Substring(0, 8)}: {status}\n" +
                             $"Speed: {agent.Velocity.Length:F2}m/s\n" +
                             $"Mass: {agent.Mass}kg");

                destinations.Add(agent.Destination);
                interests.Add(agent.InterestPoint ?? Point3d.Unset);
                initialPositions.Add(agent.InitialPosition);
                speeds.Add(agent.Velocity.Length);
                stayCounters.Add(agent.StayDuration - agent.StayCounter);
                atInterests.Add(agent.IsAtInterest);

                // 物理属性
                masses.Add(agent.Mass);
                desiredSpeeds.Add(agent.DesiredSpeed);
                maxSpeeds.Add(agent.MaxSpeed);
                radii.Add(agent.Radius);
                repulsions.Add(agent.RepulsionStrength);

                // 感知参数
                viewAngles.Add(agent.ViewAngle);
                viewDistances.Add(agent.ViewDistance);

                // 轨迹处理
                if (trajectoriesTree.PathExists(new GH_Path(i)))
                {
                    var points = new List<Point3d>();
                    foreach (GH_Point pt in trajectoriesTree.get_Branch(new GH_Path(i)))
                        points.Add(pt.Value);

                    if (points.Count > 1)
                        trajectoryCurves.Append(new GH_Curve(new Polyline(points).ToPolylineCurve()), new GH_Path(i));
                }

                try
                {
                    // 创建组合后的扇形曲线（单条曲线）
                    Curve sectorCurve = CreateViewSectorCurve(agent, coneScale);
                    viewSectors.Add(sectorCurve);  // 添加组合曲线到输出列表
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"Failed to create view sector for agent {i}: {ex.Message}");
                }
            }

            // ===== 设置所有输出 =====
            // 几何与运动 [0-5]
            DA.SetDataList(0, positions);
            DA.SetDataList(1, circles);
            DA.SetDataList(2, velocities);
            DA.SetDataList(3, forces);
            DA.SetDataTree(4, trajectoryCurves);
            DA.SetDataList(5, movementDirs);

            // 状态信息 [6-12]
            DA.SetDataList(6, statuses);
            DA.SetDataList(7, destinations);
            DA.SetDataList(8, interests);
            DA.SetDataList(9, initialPositions);
            DA.SetDataList(10, speeds);
            DA.SetDataList(11, stayCounters);
            DA.SetDataList(12, atInterests);

            // 物理属性 [13-17]
            DA.SetDataList(13, masses);
            DA.SetDataList(14, desiredSpeeds);
            DA.SetDataList(15, maxSpeeds);
            DA.SetDataList(16, radii);
            DA.SetDataList(17, repulsions);

            // 感知参数 [18-19]
            DA.SetDataList(18, viewAngles);
            DA.SetDataList(19, viewDistances);

            // 视野可视化 [20]
            DA.SetDataList(20, viewSectors);
        }

        //创建扇形线（两条半径+圆弧）
        private Curve CreateViewSectorCurve(Agent agent, double scaleFactor = 0.5)
        {
            // 计算扇形半径
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

        protected override System.Drawing.Bitmap Icon => Resources.icon_DeconstructAgentPlus;
        public override Guid ComponentGuid => new Guid("8635A1D5-EACF-4DD8-90C0-C210D3F0ACCF");
    }
}