//2.0.6版
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace NeosExplorer
{
    public class ExplorerSimulator : GH_Component
    {
        private List<Agent> _currentAgents = new List<Agent>();
        private int _currentStep = 0;

        private bool _simulationCompleted = false;
        private List<Agent> _agentSpawnQueue = new List<Agent>();
        private Dictionary<Guid, Point3d?> _agentGuidingPoints = new Dictionary<Guid, Point3d?>();
        private Dictionary<Guid, int> _agentInertiaCounters = new Dictionary<Guid, int>();
        private Dictionary<Obstacle, Curve> _obstacleOffsetCache = new Dictionary<Obstacle, Curve>();

        // 用于跟踪每个生成点的生成状态
        private Dictionary<Guid, int> _spawnTimers = new Dictionary<Guid, int>();
        private Dictionary<Guid, Agent> _spawnPoints = new Dictionary<Guid, Agent>();

        // 添加输入状态跟踪字段
        private int _lastAgentCount = -1;
        private int _lastObstacleCount = -1;
        private int _lastSettingsHash = 0;
        private int _lastEvacuationAreaCount = -1;

        // 新增：疏散完成时间
        private double? _evacuationTime = null;
        // 添加疏散完成率跟踪字段
        private int _initialAgentCount = 0;
        private double _evacuationRatio = 0.95;
        private int _targetRemaining = 0;

        // 默认参数值
        private const int DefaultSteps = 5;
        private const int DefaultMaxAgents = 20;
        private const int DefaultMaxIterations = 0;
        private const double DefaultGuideThreshold = 1;
        private const int DefaultInertiaDuration = 15;
        private const double DefaultDampingCoefficient = 0.5;
        private const double DefaultNavigationOffset = 0.5;


        public ExplorerSimulator()
          : base("Explorer Simulator", "ExpSim",
                "Agent-based simulator for pedestrian exploratory behavior",
                "Neos", "NeosExplorer")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Agents", "A", "Agent objects", GH_ParamAccess.list);
            pManager.AddGenericParameter("Obstacles", "O", "Obstacle objects", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Reset", "R", "Reset simulation", GH_ParamAccess.item, false);
            pManager.AddGenericParameter("Simulation Settings", "SS", "Global simulation settings", GH_ParamAccess.item);
            pManager.AddCurveParameter("Evacuation Areas", "EA", "Closed polylines for evacuation areas", GH_ParamAccess.list);
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Agents", "A", "Updated agents", GH_ParamAccess.list);
            pManager.AddPointParameter("Trajectories", "T", "Agent trajectories", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("Current Step", "S", "Current simulation step", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Completed", "C", "True if simulation completed", GH_ParamAccess.item);
            pManager.AddNumberParameter("Total Time", "TT", "Total simulation time in seconds(The duration in the model world does not necessarily equal the time consumed by simulation; this duration is equivalent to the time required for an organism to complete the same behavior in the real world.)", GH_ParamAccess.item);
            pManager.AddTextParameter("Evacuation Time", "ET", "Time required to reach the evacuation completion ratio.(This duration is equivalent to the time required for an organism to complete the same behavior in the real world.)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Agent> inputAgents = new List<Agent>();
            List<Obstacle> obstacles = new List<Obstacle>();
            bool reset = false;
            SimulationConfig settings = null;
            List<Curve> evacuationAreas = new List<Curve>(); // 新增：疏散范围


            if (!DA.GetDataList(0, inputAgents)) return;
            if (!DA.GetDataList(1, obstacles)) return;
            DA.GetData(2, ref reset);
            DA.GetData(3, ref settings); // 获取仿真设置
            DA.GetDataList(4, evacuationAreas); // 获取疏散范围

            // 应用设置或使用默认值
            int steps = settings?.Steps ?? DefaultSteps;
            int maxAgents = settings?.MaxAgents ?? DefaultMaxAgents;
            int maxIterations = settings?.MaxIterations ?? DefaultMaxIterations;
            double guideThreshold = settings?.GuideThreshold ?? DefaultGuideThreshold;
            int inertiaDuration = settings?.InertiaDuration ?? DefaultInertiaDuration;
            double dampingCoefficient = settings?.DampingCoefficient ?? DefaultDampingCoefficient;
            double navigationOffset = settings?.NavigationOffset ?? DefaultNavigationOffset;
            double timeStep = settings?.TimeStep ?? 0.1;
            double _evacuationRatio = settings?.EvacuationRatio ?? 0.95;

            // 计算初始Agent数量（生成点数量）
            int _initialAgentCount = inputAgents.Count;
            int _targetRemaining = (int)Math.Ceiling(_initialAgentCount * (1 - _evacuationRatio));

            // 检查输入数据是否变化（除了Reset之外的输入）
            int currentAgentCount = inputAgents.Count;
            int currentObstacleCount = obstacles.Count;
            int currentSettingsHash = settings?.GetHashCode() ?? 0;
            int currentEvacuationAreaCount = evacuationAreas.Count;

            bool inputChanged =
                currentAgentCount != _lastAgentCount ||
                currentObstacleCount != _lastObstacleCount ||
                currentSettingsHash != _lastSettingsHash ||
                currentEvacuationAreaCount != _lastEvacuationAreaCount;

            // 更新状态跟踪
            _lastAgentCount = currentAgentCount;
            _lastObstacleCount = currentObstacleCount;
            _lastSettingsHash = currentSettingsHash;
            _lastEvacuationAreaCount = currentEvacuationAreaCount;

            // 如果输入变化且不是由Reset触发的，则执行重置
            if (inputChanged && !reset)
            {
                _currentAgents.Clear();
                _currentStep = 0;
                _simulationCompleted = false;
                _spawnTimers.Clear();
                _spawnPoints.Clear();
                _obstacleOffsetCache.Clear();
                _agentGuidingPoints.Clear();
                _agentInertiaCounters.Clear();
                _evacuationTime = null;
            }
            //新增结束


            // 重置时清除所有状态
            if (reset)
            {
                _currentAgents.Clear();
                _currentStep = 0;
                _simulationCompleted = false;
                _spawnTimers.Clear();
                _spawnPoints.Clear();
                _obstacleOffsetCache.Clear();
                _agentGuidingPoints.Clear();
                _agentInertiaCounters.Clear();
                _evacuationTime = null; // 重置疏散时间
            }

            // 更新生成点
            foreach (var agent in inputAgents)
            {
                if (!_spawnPoints.ContainsKey(agent.ID))
                {
                    // 新生成点
                    _spawnPoints[agent.ID] = agent.ShallowCopy();
                    _spawnTimers[agent.ID] = 0; // 立即生成
                }
            }

            if (maxIterations > 0 && _currentStep >= maxIterations)
            {
                _simulationCompleted = true;
                DA.SetData(3, true);
                return;
            }

            for (int i = 0; i < steps; i++)
            {
                if (maxIterations > 0 && _currentStep >= maxIterations)
                {
                    _simulationCompleted = true;
                    break;
                }

                _currentStep++;

                // 生成新代理
                foreach (var kvp in _spawnPoints)
                {
                    var spawnPoint = kvp.Value;
                    _spawnTimers[kvp.Key]--;

                    if (_spawnTimers[kvp.Key] <= 0 && _currentAgents.Count < maxAgents)
                    {
                        // 创建新代理
                        Agent newAgent = spawnPoint.ShallowCopy();
                        newAgent.Position = spawnPoint.Position; // 从生成点位置开始
                        newAgent.Velocity = Vector3d.Zero;
                        newAgent.Force = Vector3d.Zero;
                        newAgent.Trajectory.Clear();
                        newAgent.Trajectory.Add(newAgent.Position);

                        _currentAgents.Add(newAgent);
                        _agentGuidingPoints[newAgent.ID] = null;
                        _agentInertiaCounters[newAgent.ID] = 0;

                        // 重置生成计时器
                        _spawnTimers[kvp.Key] = spawnPoint.SpawnInterval;
                    }
                }
                SimulateStep(obstacles, guideThreshold, inertiaDuration, dampingCoefficient, navigationOffset, timeStep);

                // 检查疏散区域是否达到目标剩余人数
                if (!_evacuationTime.HasValue && evacuationAreas.Count > 0)
                {
                    int remainingCount = CountAgentsInEvacuationAreas(evacuationAreas);
                    if (remainingCount <= _targetRemaining)
                    {
                        _evacuationTime = _currentStep * timeStep;
                    }
                }
            }

            DA.SetDataList(0, _currentAgents);
            DA.SetData(2, _currentStep);
            DA.SetData(3, _simulationCompleted);
            DA.SetData(4, _currentStep * timeStep);

            DataTree<Point3d> trajectories = new DataTree<Point3d>();
            for (int i = 0; i < _currentAgents.Count; i++)
            {
                GH_Path path = new GH_Path(i);
                trajectories.AddRange(_currentAgents[i].Trajectory, path);
            }
            DA.SetDataTree(1, trajectories);

            // 更新疏散时间输出
            if (evacuationAreas.Count == 0)
            {
                DA.SetData(5, "No evacuation area input");
            }
            else if (_evacuationTime.HasValue)
            {
                DA.SetData(5, _evacuationTime.Value.ToString("F2"));
            }
            else
            {
                DA.SetData(5, $"Not evacuated yet ({CountAgentsInEvacuationAreas(evacuationAreas)}/{_targetRemaining})");
            }


        }

        private void SimulateStep(List<Obstacle> obstacles, double guideThreshold,
                     int inertiaDuration, double dampingCoefficient,
                     double navigationOffset, double timeStep) // 新增timeStep参数
        {
            // 加强障碍物检测
            UpdateObstacleDetection(obstacles, navigationOffset);

            //检查是否静止状态
            CheckStuckAgents();

            List<Agent> agentsToRemove = new List<Agent>();
            Dictionary<Obstacle, Curve> offsetCurves = new Dictionary<Obstacle, Curve>();

            // 为所有障碍物生成偏移曲线
            foreach (var obstacle in obstacles)
            {
                Curve offsetCurve = GetOffsetCurve(obstacle, navigationOffset);
                offsetCurves[obstacle] = offsetCurve ?? obstacle.Geometry;
            }

            foreach (var agent in _currentAgents)
            {
                // 1. 更新运动方向（基于实际位移）
                if (agent.Trajectory.Count >= 2)
                {
                    Point3d prevPos = agent.Trajectory[agent.Trajectory.Count - 1];
                    Vector3d actualMovement = agent.Position - prevPos;

                    if (actualMovement.Length > 0.001)
                    {
                        agent.MovementDirection = actualMovement / actualMovement.Length;
                    }
                    // 如果没有位移但速度不为零，使用速度方向
                    else if (agent.Velocity.Length > 0.001)
                    {
                        agent.MovementDirection = agent.Velocity / agent.Velocity.Length;
                    }
                }
                // 2. 处理停留和离开逻辑
                if (agent.IsAtInterest)
                {
                    // 每次时间步检测随机离开
                    if (agent.ShouldLeave())
                    {
                        HandleLeaveInterest(agent); // 新增的离开处理方法
                    }
                    // 检测停留时间结束
                    else if (agent.StayCounter >= agent.StayDuration)
                    {
                        HandleLeaveInterest(agent);
                    }
                    else
                    {
                        // 继续停留
                        agent.StayCounter++;
                        agent.Velocity = Vector3d.Zero;
                        agent.Trajectory.Add(agent.Position);
                        continue; // 跳过后续移动逻辑
                    }
                }

                // 3. 检测是否到达兴趣点
                if (agent.InterestPoint.HasValue && !agent.IsAtInterest && !agent.JustLeftInterest)
                {
                    double distanceToInterest = agent.Position.DistanceTo(agent.InterestPoint.Value);
                    if (distanceToInterest < agent.InterestRadius)
                    {
                        agent.IsAtInterest = true;
                        agent.StayCounter = 0;
                        continue;
                    }
                }

                // 4. 检测是否到达终点
                double distanceToDestination = agent.Position.DistanceTo(agent.Destination);
                if (distanceToDestination < agent.DestinationRadius)
                {
                    agentsToRemove.Add(agent);
                    continue;
                }

                // 5. 自主寻路模式
                Vector3d drivingForce = Vector3d.Zero;
                Vector3d desiredVelocity = Vector3d.Zero;
                bool useInertia = false;

                // 确定当前目标和强度系数
                double currentStrength = 1.0;
                Point3d? currentTarget = null;

                if (agent.InterestPoint.HasValue)
                {
                    currentTarget = agent.InterestPoint.Value;
                    currentStrength = agent.InterestStrength;
                }
                else
                {
                    currentTarget = agent.Destination;
                    currentStrength = agent.DestinationStrength;
                }

                // 检查惯性状态
                if (_agentInertiaCounters.ContainsKey(agent.ID) &&
                    _agentInertiaCounters[agent.ID] > 0)
                {
                    // 惯性模式使用实际运动方向
                    Vector3d inertiaVelocity = agent.MovementDirection * agent.DesiredSpeed * currentStrength;

                    // 如果存在引导点，结合引导力
                    Vector3d combinedVelocity = inertiaVelocity;
                    if (_agentGuidingPoints.ContainsKey(agent.ID) && _agentGuidingPoints[agent.ID].HasValue)
                    {
                        Vector3d guideDirection = _agentGuidingPoints[agent.ID].Value - agent.Position;
                        if (guideDirection.Length > 0.001)
                        {
                            guideDirection.Unitize();
                            Vector3d guideVelocity = guideDirection * agent.DesiredSpeed * currentStrength;

                            // 结合惯性速度和引导速度（权重四六分）
                            combinedVelocity = (inertiaVelocity * 0.6) + (guideVelocity * 0.4);
                        }
                    }

                    desiredVelocity = combinedVelocity;

                    //desiredVelocity = inertiaVelocity;//可行修改，inertia duration需要调大，惯性状态不结合引导力

                    // 递减计数器
                    _agentInertiaCounters[agent.ID]--;

                    // 当剩余步数小于等于5时卸载引导力
                    if (_agentInertiaCounters[agent.ID] <= 5)
                    {
                        _agentGuidingPoints[agent.ID] = null;
                    }

                    // 当惯性结束时卸载引导力并清除惯性状态
                    if (_agentInertiaCounters[agent.ID] == 0)
                    {
                        _agentGuidingPoints[agent.ID] = null;
                        _agentInertiaCounters[agent.ID] = 0; // 明确清除惯性状态
                    }

                    useInertia = true;
                }
                else
                {
                    if (currentTarget != null)
                    {
                        Vector3d directionToTarget = currentTarget.Value - agent.Position;
                        if (directionToTarget.Length > 0.001)
                        {
                            directionToTarget.Unitize();

                            // 应用强度系数到期望速度
                            desiredVelocity = directionToTarget * agent.DesiredSpeed * currentStrength;

                            Point3d? closestVertex = null;

                            // 探索模式：仅在路径前方有障碍物或卡住静止时时检测引导点
                            if (HasObstacleInPath(agent.Position, currentTarget.Value, obstacles, agent.Radius) || agent.IsStuck)
                            {
                                closestVertex = FindClosestVertexInView(
                                    agent.Position,
                                    agent.MovementDirection,
                                    obstacles,
                                    agent,
                                    offsetCurves);
                            }

                            if (closestVertex.HasValue)
                            {
                                // 计算到顶点的距离
                                double distanceToVertex = agent.Position.DistanceTo(closestVertex.Value);

                                // 当距离小于阈值时激活惯性
                                if (distanceToVertex < guideThreshold)
                                {

                                    // 激活惯性计数器
                                    _agentInertiaCounters[agent.ID] = inertiaDuration;

                                    // 如果惯性时长≤5步，立即卸载引导力
                                    if (inertiaDuration <= 5)
                                    {
                                        _agentGuidingPoints[agent.ID] = null;
                                    }
                                }

                                else
                                {
                                    // 设置引导点并计算方向
                                    _agentGuidingPoints[agent.ID] = closestVertex;
                                    Vector3d guideDirection = closestVertex.Value - agent.Position;
                                    if (guideDirection.Length > 0.001)
                                    {
                                        guideDirection.Unitize();

                                        // 应用强度系数到引导点期望速度
                                        desiredVelocity = guideDirection * agent.DesiredSpeed * currentStrength;
                                    }
                                }
                            }
                            else
                            {
                                // 无障碍物，清除引导点
                                _agentGuidingPoints[agent.ID] = null;
                            }
                        }
                    }

                    // 如果存在引导点，优先使用引导点
                    if (_agentGuidingPoints.ContainsKey(agent.ID) && _agentGuidingPoints[agent.ID].HasValue)
                    {
                        Vector3d guideDirection = _agentGuidingPoints[agent.ID].Value - agent.Position;
                        if (guideDirection.Length > 0.001)
                        {
                            guideDirection.Unitize();

                            // 应用强度系数到引导点期望速度
                            desiredVelocity = guideDirection * agent.DesiredSpeed * currentStrength;
                        }
                    }
                }


                // 6. 计算驱动力
                if (desiredVelocity.Length > 0)
                {
                    Vector3d velocityDiff = desiredVelocity - agent.Velocity;


                    // 应用强度系数到驱动力
                    drivingForce = (agent.Mass / agent.RelaxationTime) * velocityDiff * currentStrength;
                }

                // 7. 障碍物力计算（使用原始曲线）
                Vector3d obstacleForce = Vector3d.Zero;
                foreach (var obstacle in obstacles)
                {
                    obstacleForce += obstacle.GetForce(agent);
                }

                // 8. 社会力计算
                Vector3d socialForce = Vector3d.Zero;
                foreach (var other in _currentAgents)
                {
                    if (other != agent)
                    {
                        double distance = agent.Position.DistanceTo(other.Position);
                        double minDistance = agent.Radius + other.Radius;
                        double effectiveDistance = distance - minDistance; // 负值表示重叠

                        // 增加检测范围到10倍最小距离
                        if (distance < 10.0 * minDistance)
                        {
                            Vector3d direction = agent.Position - other.Position;
                            if (direction.Length > 0.001)
                            {
                                direction.Unitize();

                                // 1. 强排斥力（当agent间距小于身体半径之和）
                                if (effectiveDistance < 0)
                                {
                                    // 指数增长的强排斥力
                                    double overlap = -effectiveDistance; // 重叠量
                                    double avgRepulsion = (agent.RepulsionStrength + other.RepulsionStrength) / 2.0;
                                    double forceMagnitude = avgRepulsion * Math.Exp(overlap / 0.1);
                                    socialForce += direction * forceMagnitude;
                                }
                                // 2. 中等排斥力（间距在1-2倍半径之间）
                                else if (distance < 2.0 * minDistance)
                                {
                                    // 中等排斥力
                                    double avgRepulsion = (agent.RepulsionStrength + other.RepulsionStrength) / 2.0;
                                    double forceMagnitude = avgRepulsion * Math.Exp(-distance / minDistance);
                                    socialForce += direction * forceMagnitude;
                                }
                                // 3. 弱排斥力（间距在2-10倍半径之间）
                                else
                                {
                                    // 弱排斥力
                                    double forceMagnitude = agent.RepulsionStrength * Math.Exp(-distance / (2.0 * minDistance));
                                    socialForce += direction * forceMagnitude;
                                }
                            }
                        }
                    }
                }

                // 9. 添加阻尼力
                Vector3d dampingForce = -dampingCoefficient * agent.Velocity;

                agent.Force = drivingForce + obstacleForce + socialForce + dampingForce;

                // 10. 运动更新
                Vector3d acceleration = agent.Force / agent.Mass;
                agent.Velocity += acceleration * timeStep;

                // 限制最大速度
                if (agent.Velocity.Length > agent.MaxSpeed)
                {
                    agent.Velocity = agent.Velocity / agent.Velocity.Length * agent.MaxSpeed;
                }

                // 更新位置
                agent.Position += agent.Velocity * timeStep;
                agent.Trajectory.Add(agent.Position);

                // 重置"刚离开"标志
                agent.JustLeftInterest = false;
            }

            // 移除到达终点的Agent
            foreach (var agent in agentsToRemove)
            {
                _currentAgents.Remove(agent);
                if (_agentGuidingPoints.ContainsKey(agent.ID))
                {
                    _agentGuidingPoints.Remove(agent.ID);
                }
                if (_agentInertiaCounters.ContainsKey(agent.ID))
                {
                    _agentInertiaCounters.Remove(agent.ID);
                }
            }
        }


        //以下为辅助方法
        //静止检测方法
        private void CheckStuckAgents()
        {
            foreach (var agent in _currentAgents)
            {

                // 检查是否卡住（StayDuration+10步后的位置与当前位置距离小于0.1）
                int checkSteps = agent.StayDuration + 10;
                if (agent.Trajectory.Count > checkSteps)
                {
                    Point3d currentPos = agent.Position;
                    Point3d oldPos = agent.Trajectory[agent.Trajectory.Count - checkSteps - 1];

                    if (currentPos.DistanceTo(oldPos) < agent.Radius * 0.5)
                    {
                        agent.IsStuck = true;
                        // 触发立即寻找新引导点
                        // _agentGuidingPoints[agent.ID] = null;
                    }
                    else
                    {
                        agent.IsStuck = false;
                    }
                }
            }
        }



        // 加强障碍物检测
        private void UpdateObstacleDetection(List<Obstacle> obstacles, double navigationOffset)
        {
            foreach (var obstacle in obstacles)
            {
                // 如果缓存中没有或需要更新，重新计算偏移曲线
                if (!_obstacleOffsetCache.ContainsKey(obstacle)
                    || _obstacleOffsetCache[obstacle] == null)
                {
                    _obstacleOffsetCache[obstacle] = GetOffsetCurve(obstacle, navigationOffset);
                }
            }
        }

        /// 增强的障碍物检测方法
        private bool HasObstacleInPath(Point3d start, Point3d end, List<Obstacle> obstacles, double radius)
        {
            // 如果起点和终点非常接近，直接返回无障碍物
            if (start.DistanceTo(end) < radius * 0.5)
                return false;

            Line line = new Line(start, end);
            double lineLength = start.DistanceTo(end);

            foreach (var obstacle in obstacles)
            {
                if (obstacle.Geometry == null) continue;

                // 使用带偏移的缓冲区检测
                Curve buffer = null;
                if (_obstacleOffsetCache.ContainsKey(obstacle))
                {
                    buffer = _obstacleOffsetCache[obstacle];
                }
                else
                {
                    buffer = obstacle.Geometry.Offset(Plane.WorldXY, radius, 0.1, CurveOffsetCornerStyle.Sharp)[0];
                }

                if (buffer == null) continue;

                var intersection = Rhino.Geometry.Intersect.Intersection.CurveCurve(
                    new LineCurve(line), buffer, 0.01, 0.01);

                if (intersection.Count > 0)
                {
                    foreach (var eventData in intersection)
                    {
                        // 排除端点
                        if (eventData.ParameterA > 0.01 && eventData.ParameterA < 0.99)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }



        // 视野扇形内寻找最近顶点（使用偏移曲线）作为引导点方法
        private Point3d? FindClosestVertexInView(Point3d position, Vector3d movementDirection,
                                                List<Obstacle> obstacles, Agent agent,
                                                Dictionary<Obstacle, Curve> offsetCurves)
        {
            double minDistance = double.MaxValue;
            Point3d? closestVertex = null;
            double viewAngle = agent.ViewAngle;
            double viewDistance = agent.ViewDistance;

            foreach (var obstacle in obstacles)
            {
                if (obstacle.Geometry == null) continue;

                // 获取偏移曲线
                Curve offsetCurve = offsetCurves.ContainsKey(obstacle) ?
                    offsetCurves[obstacle] : obstacle.Geometry;

                Polyline polyline;
                if (!offsetCurve.TryGetPolyline(out polyline))
                {
                    double tolerance = 0.1;
                    PolylineCurve polyCurve = offsetCurve.ToPolyline(0, 0, 0, 1000, 0, tolerance, 0, 0, true);
                    if (polyCurve != null && polyCurve.TryGetPolyline(out polyline))
                    {
                        // 成功转换
                    }
                    else
                    {
                        continue;
                    }
                }

                foreach (var vertex in polyline)
                {
                    Vector3d toVertex = vertex - position;
                    double distance = toVertex.Length;

                    // 跳过超出视距的点
                    if (distance > viewDistance) continue;

                    // 跳过太近的点
                    if (distance < agent.Radius * 0.5) continue;

                    toVertex.Unitize();

                    // 检查是否在视野范围内
                    double dotProduct = Vector3d.Multiply(movementDirection, toVertex);
                    double angle = Math.Abs(Math.Acos(dotProduct));

                    if (angle <= viewAngle / 2) // 视野是圆锥形，所以除以2
                    {
                        if (distance < minDistance)
                        {
                            minDistance = distance;
                            closestVertex = vertex;
                        }
                    }
                }
            }

            return closestVertex;
        }

        // 离开处理方法
        private void HandleLeaveInterest(Agent agent)
        {
            agent.IsAtInterest = false;
            agent.StayCounter = 0;
            agent.JustLeftInterest = true;

            bool hasNext = agent.MoveToNextInterestPoint();
            if (!hasNext) agent.ClearInterestPoint();
        }

        // 为障碍物生成偏移曲线（用于导航点计算）
        private Curve GetOffsetCurve(Obstacle obstacle, double offset)
        {
            if (offset <= 0.001) return obstacle.Geometry;

            try
            {
                // 尝试获取缓存
                if (_obstacleOffsetCache.TryGetValue(obstacle, out Curve cachedCurve))
                {
                    return cachedCurve;
                }

                // 生成偏移曲线
                Curve offsetCurve = obstacle.Geometry.Offset(Plane.WorldXY, offset, 0.1, CurveOffsetCornerStyle.Sharp)[0];
                _obstacleOffsetCache[obstacle] = offsetCurve;
                return offsetCurve;
            }
            catch
            {
                // 偏移失败时返回原始曲线
                return obstacle.Geometry;
            }
        }

        // 新增方法：统计疏散区域内的Agent数量
        private int CountAgentsInEvacuationAreas(List<Curve> evacuationAreas)
        {
            int count = 0;
            foreach (var agent in _currentAgents)
            {
                Point3d pt = agent.Position;
                pt.Z = 0; // 投影到XY平面
                foreach (var area in evacuationAreas)
                {
                    if (!area.IsClosed) continue;

                    var containment = area.Contains(pt, Plane.WorldXY, 0.1);
                    if (containment == PointContainment.Inside || containment == PointContainment.Coincident)
                    {
                        count++;
                        break; // 一个Agent只计一次，即使位于多个区域
                    }
                }
            }
            return count;
        }


        protected override System.Drawing.Bitmap Icon => Resources.iconEXPSIM;
        public override Guid ComponentGuid => new Guid("8FB54F80-9F69-465A-8547-86BE8552E34F");
    }

}