using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using NeosEnviSim.Properties;
using Rhino;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeosExplorer
{
    public class PedestrianSimulator : GH_Component
    {
        private List<Agent> _currentAgents = new List<Agent>();
        private int _currentStep = 0;
        private bool _simulationCompleted = false;
        private Dictionary<Guid, int> _spawnTimers = new Dictionary<Guid, int>();
        private Dictionary<Guid, Agent> _spawnPoints = new Dictionary<Guid, Agent>();
        private Dictionary<Obstacle, Curve> _obstacleOffsetCache = new Dictionary<Obstacle, Curve>();
        private Dictionary<Guid, Polyline> _agentPaths = new Dictionary<Guid, Polyline>();
        private Dictionary<Guid, int> _agentPathIndices = new Dictionary<Guid, int>();
        private Dictionary<Guid, int> _agentInertiaCounters = new Dictionary<Guid, int>();
        private Dictionary<Guid, int> _agentStayCounters = new Dictionary<Guid, int>();
        private Dictionary<Guid, Polyline> _spawnPointPaths = new Dictionary<Guid, Polyline>();

        // 添加疏散相关字段
        private double? _evacuationTime = null;
        private int _initialAgentCount = 0;
        private double _evacuationRatio = 0.95;
        private int _targetRemaining = 0;

        // 添加输入状态跟踪字段
        private int _lastAgentCount = -1;
        private int _lastObstacleCount = -1;
        private int _lastSettingsHash = 0;
        private int _lastEvacuationAreaCount = -1;

        // 默认参数值
        private const int DefaultSteps = 5;
        private const int DefaultMaxAgents = 20;
        private const int DefaultMaxIterations = 0;
        private const double DefaultGuideThreshold = 1;
        private const int DefaultInertiaDuration = 15;
        private const double DefaultDampingCoefficient = 0.5;
        private const double DefaultNavigationOffset = 0.5;
        private const int DefaultRecoverySteps = 25;
        public PedestrianSimulator()
          : base("Pedestrian Simulator", "PedSim",
                "Agent-based simulator with shortest path guidance",
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
            pManager.AddCurveParameter("Shortest Paths", "SP", "Shortest paths for each spawn point", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Total Time", "TT", "Total simulation time (s).The duration in the model world does not necessarily equal the time consumed by simulation; this duration is equivalent to the time required for an organism to complete the same behavior in real world.", GH_ParamAccess.item);
            pManager.AddTextParameter("Evacuation Time", "ET", "Time required to reach the evacuation completion ratio (s).\nThis duration is equivalent to the time required for an organism to complete the same behavior in real world.", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Agent> inputAgents = new List<Agent>();
            List<Obstacle> obstacles = new List<Obstacle>();
            bool reset = false;
            SimulationConfig settings = null;
            List<Curve> evacuationAreas = new List<Curve>();

            if (!DA.GetDataList(0, inputAgents)) return;
            if (!DA.GetDataList(1, obstacles)) return;
            DA.GetData(2, ref reset);
            DA.GetData(3, ref settings);
            DA.GetDataList(4, evacuationAreas);

            // 应用设置或使用默认值
            double timeStep = settings?.TimeStep ?? 0.1;
            int steps = settings?.Steps ?? DefaultSteps;
            int recoverySteps = settings?.RecoverySteps ?? DefaultRecoverySteps;
            int maxAgents = settings?.MaxAgents ?? DefaultMaxAgents;
            int maxIterations = settings?.MaxIterations ?? DefaultMaxIterations;
            double guideThreshold = settings?.GuideThreshold ?? DefaultGuideThreshold;
            int inertiaDuration = settings?.InertiaDuration ?? DefaultInertiaDuration;
            double dampingCoefficient = settings?.DampingCoefficient ?? DefaultDampingCoefficient;
            double navigationOffset = settings?.NavigationOffset ?? DefaultNavigationOffset;
            double _evacuationRatio = settings?.EvacuationRatio ?? 0.95;

            // 计算初始Agent数量（生成点数量）
            int _initialAgentCount = inputAgents.Count;
            int _targetRemaining = (int)Math.Ceiling(_initialAgentCount * (1 - _evacuationRatio));


            // 新增，检查输入数据是否变化（除了Reset之外的输入）
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
                _agentPaths.Clear();
                _agentPathIndices.Clear();
                _agentInertiaCounters.Clear();
                _agentStayCounters.Clear();
                _spawnPointPaths.Clear();
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
                _agentPaths.Clear();
                _agentPathIndices.Clear();
                _agentInertiaCounters.Clear();
                _agentStayCounters.Clear();
                _spawnPointPaths.Clear();
                _evacuationTime = null;
                foreach (var agent in _currentAgents)
                {
                    agent.IsStuck = false;
                    agent.StuckCounter = 0;
                    agent.RecoveryTarget = null;
                }
            }

            // 更新生成点状态
            foreach (var agent in inputAgents)
            {
                if (_spawnPoints.TryGetValue(agent.ID, out var existingSpawnPoint))
                {
                    // 检测参数变化（新增）
                    bool pointsChanged = !existingSpawnPoint.InterestPoints.SequenceEqual(
                        agent.InterestPoints, new Point3dComparer());

                    bool destChanged = existingSpawnPoint.Destination.DistanceTo(agent.Destination) > 0.01;

                    if (pointsChanged || destChanged)
                    {
                        // 参数变化，移除旧路径（新增）
                        _spawnPointPaths.Remove(agent.ID);
                    }

                    // 更新属性
                    existingSpawnPoint.Position = agent.Position;
                    existingSpawnPoint.InterestPoints = agent.InterestPoints;
                    existingSpawnPoint.Destination = agent.Destination;
                }
                else
                {
                    // 新生成点
                    Agent spawnPoint = agent.ShallowCopy();
                    _spawnPoints[agent.ID] = spawnPoint;
                    _spawnTimers[agent.ID] = agent.SpawnInterval > 0 ?
                        new Random(agent.ID.GetHashCode()).Next(0, agent.SpawnInterval) : 0;
                }
            }

            // 移除不再存在的生成点
            var keysToRemove = _spawnPoints.Keys.Except(inputAgents.Select(a => a.ID)).ToList();
            foreach (var key in keysToRemove)
            {
                _spawnPoints.Remove(key);
                _spawnTimers.Remove(key);
                _spawnPointPaths.Remove(key); // 新增：移除相关路径
            }

            // 检查最大迭代次数
            if (maxIterations > 0 && _currentStep >= maxIterations)
            {
                _simulationCompleted = true;
                DA.SetData(3, true);
                return;
            }

            // 在模拟开始前计算所有生成点路径
            CalculateSpawnPointPaths(obstacles, navigationOffset);

            // 主模拟循环
            for (int i = 0; i < steps; i++)
            {
                if (maxIterations > 0 && _currentStep >= maxIterations)
                {
                    _simulationCompleted = true;
                    break;
                }

                _currentStep++;

                // 生成新代理（按固定顺序处理）
                var spawnKeys = _spawnPoints.Keys.OrderBy(k => k).ToList();
                foreach (var key in spawnKeys)
                {
                    var spawnPoint = _spawnPoints[key];

                    // 确保计时器存在
                    if (!_spawnTimers.ContainsKey(key))
                    {
                        _spawnTimers[key] = spawnPoint.SpawnInterval;
                    }

                    _spawnTimers[key]--;

                    if (_spawnTimers[key] <= 0 && _currentAgents.Count < maxAgents)
                    {
                        Agent newAgent = spawnPoint.ShallowCopy();
                        newAgent.Position = spawnPoint.Position;
                        newAgent.Velocity = Vector3d.Zero;
                        newAgent.Force = Vector3d.Zero;
                        newAgent.Trajectory.Clear();
                        newAgent.Trajectory.Add(newAgent.Position);

                        _currentAgents.Add(newAgent);
                        _spawnTimers[key] = spawnPoint.SpawnInterval;

                        // 新增：为新代理分配预计算路径
                        if (_spawnPointPaths.TryGetValue(key, out Polyline path))
                        {
                            _agentPaths[newAgent.ID] = path;
                            _agentPathIndices[newAgent.ID] = 1; // 从第一个路径点开始
                            _agentInertiaCounters[newAgent.ID] = 0;
                            _agentStayCounters[newAgent.ID] = 0;
                        }
                    }
                }
                SimulateStep(obstacles, guideThreshold, inertiaDuration,
                            dampingCoefficient, navigationOffset, timeStep, recoverySteps);

                // 新增：检查疏散区域是否达到目标剩余人数
                if (!_evacuationTime.HasValue && evacuationAreas.Count > 0)
                {
                    int remainingCount = CountAgentsInEvacuationAreas(evacuationAreas);
                    if (remainingCount <= _targetRemaining)
                    {
                        _evacuationTime = _currentStep * timeStep;
                    }
                }
            }

            // 设置输出
            DA.SetDataList(0, _currentAgents);
            DA.SetData(2, _currentStep);
            DA.SetData(3, _simulationCompleted);
            DA.SetData(5, _currentStep * timeStep); // 计算并输出总时长

            // 轨迹输出
            DataTree<Point3d> trajectories = new DataTree<Point3d>();
            for (int i = 0; i < _currentAgents.Count; i++)
            {
                GH_Path path = new GH_Path(i);
                trajectories.AddRange(_currentAgents[i].Trajectory, path);
            }
            DA.SetDataTree(1, trajectories);

            // 输出起点至终点的最短路径
            DataTree<Polyline> shortestPaths = new DataTree<Polyline>();
            var spawnPointKeys = _spawnPointPaths.Keys.OrderBy(k => k).ToList();
            for (int i = 0; i < spawnPointKeys.Count; i++)
            {
                Guid spawnId = spawnPointKeys[i];
                if (_spawnPointPaths.TryGetValue(spawnId, out Polyline path))
                {
                    GH_Path branch = new GH_Path(i);
                    shortestPaths.Add(path, branch);
                }
            }
            DA.SetDataTree(4, shortestPaths);

            // 更新疏散时间输出
            if (evacuationAreas.Count == 0)
            {
                DA.SetData(6, "No evacuation area input");
            }
            else if (_evacuationTime.HasValue)
            {
                DA.SetData(6, _evacuationTime.Value.ToString("F2"));
            }
            else
            {
                DA.SetData(6, $"Not evacuated yet ({CountAgentsInEvacuationAreas(evacuationAreas)}/{_targetRemaining})");
            }
        }

        //计算所有生成点的静态路径
        private void CalculateSpawnPointPaths(List<Obstacle> obstacles, double navigationOffset)
        {
            foreach (var keyValuePair in _spawnPoints)
            {
                Guid spawnId = keyValuePair.Key;
                Agent spawnPoint = keyValuePair.Value;

                if (!_spawnPointPaths.ContainsKey(spawnId))
                {
                    try
                    {
                        Point3d start = spawnPoint.Position;
                        List<Point3d> interestPoints = new List<Point3d>(spawnPoint.InterestPoints);
                        Point3d end = spawnPoint.Destination;

                        // 投影到XY平面
                        start.Z = 0;
                        end.Z = 0;
                        for (int i = 0; i < interestPoints.Count; i++)
                        {
                            Point3d pt = interestPoints[i];
                            pt.Z = 0;
                            interestPoints[i] = pt;
                        }

                        // 计算路径
                        Polyline path = CalculateStaticPath(start, interestPoints, end, obstacles, navigationOffset);

                        if (path != null && path.Count >= 2)
                        {
                            _spawnPointPaths[spawnId] = path;
                            RhinoApp.WriteLine($"Path calculated for spawn point {spawnId}");
                        }
                    }
                    catch (Exception ex)
                    {
                        RhinoApp.WriteLine($"Path calculation error for spawn point {spawnId}: {ex.Message}");
                    }
                }
            }
        }

        // 基于可见性图与Astar算法的静态路径计算方法
        public static Polyline CalculateStaticPath(Point3d start, List<Point3d> interestPoints, Point3d end,
                                           List<Obstacle> obstacles, double navigationOffset)
        {
            try
            {
                // 1. 准备数据
                List<Point3d> points = interestPoints ?? new List<Point3d>();

                // 2. 处理障碍物：转换为曲线
                List<Curve> obstacleCurves = new List<Curve>();
                foreach (var obstacle in obstacles)
                {
                    if (obstacle != null && obstacle.Geometry != null)
                    {
                        obstacleCurves.Add(obstacle.Geometry);
                    }
                }

                // 3. 计算偏移曲线
                List<Curve> offsetCurves = new List<Curve>();
                List<Line> obstacleSegments = new List<Line>();
                List<Point3d> obstacleVertices = new List<Point3d>();
                Point3dComparer comparer = new Point3dComparer();

                foreach (Curve obstacle in obstacleCurves)
                {
                    if (obstacle == null || !obstacle.IsClosed) continue;

                    Curve offsetCrv = null;
                    try
                    {
                        offsetCrv = (navigationOffset != 0) ?
                            obstacle.Offset(Plane.WorldXY, navigationOffset, 0.1, CurveOffsetCornerStyle.Sharp)[0] :
                            obstacle.DuplicateCurve();
                    }
                    catch
                    {
                        offsetCrv = obstacle.DuplicateCurve();
                    }

                    offsetCurves.Add(offsetCrv);

                    // 转换为多段线并提取线段
                    if (offsetCrv.ToPolyline(0, 0, 1, 0, 0, 0.1, 0, 0, true) is PolylineCurve plCurve)
                    {
                        Polyline pl;
                        if (plCurve.TryGetPolyline(out pl))
                        {
                            for (int i = 0; i < pl.SegmentCount; i++)
                            {
                                Line segment = pl.SegmentAt(i);
                                obstacleSegments.Add(segment);
                            }
                            obstacleVertices.AddRange(pl);
                        }
                    }
                }

                // 4. 检查点是否在障碍物内
                if (IsPointInAnyObstacle(start, offsetCurves) ||
                    points.Any(pt => IsPointInAnyObstacle(pt, offsetCurves)) ||
                    IsPointInAnyObstacle(end, offsetCurves))
                {
                    RhinoApp.WriteLine("Path calculation skipped: point inside obstacle");
                    return null;
                }

                // 5. 收集所有网络顶点
                List<Point3d> allVertices = new List<Point3d> { start };
                allVertices.AddRange(points);
                allVertices.Add(end);
                allVertices.AddRange(obstacleVertices);
                allVertices = allVertices.Distinct(comparer).ToList();

                // 6. 生成潜在连接
                List<Line> potentialConnections = new List<Line>();
                for (int i = 0; i < allVertices.Count; i++)
                {
                    for (int j = i + 1; j < allVertices.Count; j++)
                    {
                        Point3d p1 = allVertices[i];
                        Point3d p2 = allVertices[j];
                        if (!comparer.Equals(p1, p2))
                        {
                            potentialConnections.Add(new Line(p1, p2));
                        }
                    }
                }

                // 7. 创建边界段集合
                var boundarySegmentSet = new HashSet<Tuple<Point3d, Point3d>>(new UndirectedLineComparer(comparer));
                foreach (Line segment in obstacleSegments)
                {
                    var normalizedSegment = NormalizeLine(segment, comparer);
                    boundarySegmentSet.Add(normalizedSegment);
                }

                // 8. 过滤有效连接
                List<Line> validConnections = new List<Line>();
                foreach (Line connection in potentialConnections)
                {
                    var normalizedConn = NormalizeLine(connection, comparer);

                    // 边界段总是允许
                    if (boundarySegmentSet.Contains(normalizedConn))
                    {
                        validConnections.Add(connection);
                    }
                    // 非边界连接检查
                    else if (!LineIntersectsAnyOffsetCurve(connection, offsetCurves, comparer))
                    {
                        // 检查连接上的样本点
                        bool anyPointInside = false;
                        Point3d p1 = connection.PointAt(0.25);
                        Point3d p2 = connection.PointAt(0.5);
                        Point3d p3 = connection.PointAt(0.75);

                        if (IsPointInAnyObstacle(p1, offsetCurves) ||
                            IsPointInAnyObstacle(p2, offsetCurves) ||
                            IsPointInAnyObstacle(p3, offsetCurves))
                        {
                            anyPointInside = true;
                        }

                        if (!anyPointInside)
                        {
                            validConnections.Add(connection);
                        }
                    }
                }

                // 9. 构建图
                Graph networkGraph = BuildGraph(allVertices, validConnections, comparer);

                // 10. 排序兴趣点（按距起点的直线距离）
                List<Point3d> sortedPoints = points
                    .OrderBy(p => p.DistanceTo(start))
                    .ToList();

                // 11. 创建路径点序列
                List<Point3d> waypoints = new List<Point3d> { start };
                waypoints.AddRange(sortedPoints);
                waypoints.Add(end);

                // 12. 计算完整路径
                List<Point3d> fullPath = new List<Point3d>();
                for (int i = 0; i < waypoints.Count - 1; i++)
                {
                    Point3d from = waypoints[i];
                    Point3d to = waypoints[i + 1];

                    List<Point3d> segmentPath = networkGraph.AStarPath(from, to, comparer);

                    if (segmentPath == null || segmentPath.Count < 2)
                    {
                        RhinoApp.WriteLine($"Path segment not found: {from} to {to}");
                        return null;
                    }

                    // 移除连接处的重复点
                    if (i > 0 && fullPath.Count > 0 &&
                        comparer.Equals(fullPath.Last(), segmentPath.First()))
                    {
                        segmentPath.RemoveAt(0);
                    }

                    fullPath.AddRange(segmentPath);
                }

                // 13. 返回路径
                if (fullPath.Count >= 2)
                {
                    return new Polyline(fullPath);
                }

                return null;
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"Path calculation error: {ex.Message}");
                return null;
            }
        }
        //模拟主方法
        private void SimulateStep(List<Obstacle> obstacles, double guideThreshold,
                     int inertiaDuration, double dampingCoefficient,
                     double navigationOffset, double timeStep, int recoverySteps)
        {
            // 在模拟步骤开始前检测卡住Agent
            CheckAndHandleStuckAgents(obstacles, guideThreshold, navigationOffset);
            List<Agent> agentsToRemove = new List<Agent>();

            foreach (var agent in _currentAgents)
            {
                // 处理卡住恢复状态
                if (agent.IsStuck)
                {
                    agent.StuckCounter++;

                    // 检查是否有恢复目标
                    if (agent.RecoveryTarget.HasValue)
                    {
                        // 1. 计算驱动力（朝向恢复目标）
                        Vector3d recoveryDrivingForce = Vector3d.Zero;
                        Vector3d recoveryDesiredVelocity = Vector3d.Zero;
                        double recoveryStrengthFactor = agent.GetCurrentStrengthFactor();
                        Vector3d directionToTarget = agent.RecoveryTarget.Value - agent.Position;
                        if (directionToTarget.Length > 0.001)
                        {
                            directionToTarget.Unitize();

                            recoveryDesiredVelocity = directionToTarget * agent.DesiredSpeed * recoveryStrengthFactor;
                        }

                        // 计算驱动力
                        if (recoveryDesiredVelocity.Length > 0)
                        {
                            Vector3d velocityDiff = recoveryDesiredVelocity - agent.Velocity;
                            double relaxationTime = agent.RelaxationTime;
                            recoveryDrivingForce = (agent.Mass / relaxationTime) * velocityDiff * recoveryStrengthFactor;
                        }

                        // 2. 障碍物力计算
                        Vector3d recoveryObstacleForce = Vector3d.Zero;
                        foreach (var obstacle in obstacles)
                        {
                            recoveryObstacleForce += obstacle.GetForce(agent);
                        }

                        // 3. 社会力计算
                        Vector3d recoverySocialForce = Vector3d.Zero;
                        foreach (var other in _currentAgents)
                        {
                            if (other != agent)
                            {
                                double distance = agent.Position.DistanceTo(other.Position);
                                double minDistance = agent.Radius + other.Radius;
                                double effectiveDistance = distance - minDistance;

                                if (distance < 10.0 * minDistance)
                                {
                                    Vector3d direction = agent.Position - other.Position;
                                    if (direction.Length > 0.001)
                                    {
                                        direction.Unitize();

                                        if (effectiveDistance < 0)
                                        {
                                            double overlap = -effectiveDistance;
                                            double avgRepulsion = (agent.RepulsionStrength + other.RepulsionStrength) / 2.0;
                                            double forceMagnitude = avgRepulsion * Math.Exp(overlap / 0.1);
                                            recoverySocialForce += direction * forceMagnitude;
                                        }
                                        else if (distance < 2.0 * minDistance)
                                        {
                                            double avgRepulsion = (agent.RepulsionStrength + other.RepulsionStrength) / 2.0;
                                            double forceMagnitude = avgRepulsion * Math.Exp(-distance / minDistance);
                                            recoverySocialForce += direction * forceMagnitude;
                                        }
                                        else
                                        {
                                            double forceMagnitude = agent.RepulsionStrength * Math.Exp(-distance / (2.0 * minDistance));
                                            recoverySocialForce += direction * forceMagnitude;
                                        }
                                    }
                                }
                            }
                        }

                        // 4. 添加阻尼力
                        Vector3d recoveryDampingForce = -dampingCoefficient * agent.Velocity;

                        //合力计算
                        agent.Force = recoveryDrivingForce + recoveryObstacleForce + recoverySocialForce + recoveryDampingForce;

                        // 5. 运动更新
                        Vector3d recoveryAcceleration = agent.Force / agent.Mass;
                        agent.Velocity += recoveryAcceleration * timeStep;

                        // 限制最大速度
                        if (agent.Velocity.Length > agent.MaxSpeed)
                        {
                            agent.Velocity = agent.Velocity / agent.Velocity.Length * agent.MaxSpeed;
                        }

                        // 更新位置
                        agent.Position += agent.Velocity * timeStep;
                        agent.Trajectory.Add(agent.Position);

                        // 更新运动方向
                        if (agent.Trajectory.Count >= 2)
                        {
                            Point3d prevPos = agent.Trajectory[agent.Trajectory.Count - 2];
                            Vector3d actualMovement = agent.Position - prevPos;

                            if (actualMovement.Length > 0.001)
                            {
                                agent.MovementDirection = actualMovement / actualMovement.Length;
                            }
                        }

                        // 检查是否到达恢复点
                        if (agent.Position.DistanceTo(agent.RecoveryTarget.Value) < guideThreshold)
                        {
                            agent.IsStuck = false;
                            agent.RecoveryTarget = null;
                            RhinoApp.WriteLine($"Agent {agent.ID} recovered from stuck state.");
                        }

                        // 最多尝试恢复recoverySteps的步数
                        if (agent.StuckCounter > recoverySteps)
                        {
                            agent.IsStuck = false;
                            agent.RecoveryTarget = null;
                            RhinoApp.WriteLine($"Agent {agent.ID} stuck recovery timeout.");
                        }

                        continue; // 跳过正常行为逻辑
                    }
                    else
                    {
                        // 没有恢复目标，强制解除卡住状态
                        agent.IsStuck = false;
                    }
                }
                //新增结束：卡住恢复状态

               
                // 检查是否有路径
                if (!_agentPaths.ContainsKey(agent.ID)) continue;

                Polyline path = _agentPaths[agent.ID];
                int currentIndex = _agentPathIndices[agent.ID];

                // 检查是否到达终点
                if (currentIndex >= path.Count)
                {
                    agentsToRemove.Add(agent);
                    continue;
                }

                // 获取当前目标点
                Point3d currentTarget = path[currentIndex];


                //// 处理停留状态-原代码
                //if (_agentStayCounters.ContainsKey(agent.ID) && _agentStayCounters[agent.ID] > 0)
                //{
                //    // 每一步都检查是否应该离开
                //    if (agent.ShouldLeave())
                //    {
                //        _agentStayCounters[agent.ID] = 0; // 立即结束停留
                //        agent.IsAtInterest = false;
                //        agent.JustLeftInterest = true; // 标记刚刚离开
                //    }
                //    else
                //    {
                //        _agentStayCounters[agent.ID]--; // 减少停留时间
                //    }

                //    // 如果还在停留状态
                //    if (_agentStayCounters[agent.ID] > 0)
                //    {
                //        agent.Velocity = Vector3d.Zero;
                //        agent.Trajectory.Add(agent.Position);
                //        continue; // 停留步跳过本步的移动计算
                //    }
                //    else
                //    {
                //        agent.IsAtInterest = false;
                //        agent.JustLeftInterest = true; // 添加：标记离开
                //    }
                //}



                // 处理停留状态-修改
                if (_agentStayCounters.ContainsKey(agent.ID) && _agentStayCounters[agent.ID] > 0)
                {
                    // 检查是否应该离开
                    if (agent.ShouldLeave())
                    {
                        _agentStayCounters[agent.ID] = 0;
                        agent.IsAtInterest = false;
                        agent.JustLeftInterest = true;

                        // 关键修复：离开后立即指向下一个路径点
                        _agentPathIndices[agent.ID] = currentIndex + 1;
                    }
                    else
                    {
                        _agentStayCounters[agent.ID]--;
                    }

                    if (_agentStayCounters[agent.ID] > 0)
                    {
                        agent.Velocity = Vector3d.Zero;
                        agent.Trajectory.Add(agent.Position);
                        continue;// 停留步跳过本步的移动计算
                    }
                    else
                    {
                        agent.IsAtInterest = false;
                        agent.JustLeftInterest = true;

                        // 关键修复：停留结束后指向下一个路径点
                        _agentPathIndices[agent.ID] = currentIndex + 1;
                    }
                }
                //修改结束


                // 重新获取当前目标点（索引可能已更新）
                currentIndex = _agentPathIndices[agent.ID];
                if (currentIndex >= path.Count)
                {
                    agentsToRemove.Add(agent);
                    continue;
                }
                currentTarget = path[currentIndex];



                // 检查是否到达兴趣点 ===
                bool isInterestPoint = agent.InterestPoints.Any(p =>
                    new Point3dComparer().Equals(p, currentTarget));
                double distanceToTarget = agent.Position.DistanceTo(currentTarget);

                // 首先检查是否是终点（使用 DestinationRadius）
                bool isDestination = (currentIndex == path.Count - 1);
                if (isDestination)
                {
                    if (distanceToTarget < agent.DestinationRadius)
                    {
                        agentsToRemove.Add(agent);
                        continue;
                    }
                }



                // 然后检查是否是兴趣点（使用 InterestRadius）-原代码
                //else if (isInterestPoint)
                //{
                //    if (distanceToTarget < agent.InterestRadius)
                //    {
                //        // 到达兴趣点，开始停留
                //        _agentStayCounters[agent.ID] = agent.StayDuration;
                //        agent.IsAtInterest = true;                      
                //        agent.CurrentInterestPointIndex++;

                //        if (agent.ShouldLeave())
                //        {
                //            _agentStayCounters[agent.ID] = 0;
                //            agent.IsAtInterest = false;
                //            agent.JustLeftInterest = true;                                                  
                //        }
                //        else
                //        {
                //            // 移动到下一个路径点
                //            _agentPathIndices[agent.ID] = currentIndex + 1;
                //            agent.Velocity = Vector3d.Zero;
                //            agent.Trajectory.Add(agent.Position);
                //            continue; // 跳过本步的移动计算
                //        }
                //    }
                //}


                // 然后检查是否是兴趣点（使用 InterestRadius）-修改
                else if (isInterestPoint)
                {
                    if (distanceToTarget < agent.InterestRadius)
                    {
                        _agentStayCounters[agent.ID] = agent.StayDuration;
                        agent.IsAtInterest = true;

                        // 关键修复：立即更新兴趣点索引
                        agent.CurrentInterestPointIndex++;

                        if (agent.ShouldLeave())
                        {
                            _agentStayCounters[agent.ID] = 0;
                            agent.IsAtInterest = false;
                            agent.JustLeftInterest = true;
                            _agentPathIndices[agent.ID] = currentIndex + 1; // 指向下一个点
                        }
                    }
                }
                //修改结束


                // 最后检查普通路径点（使用 guideThreshold）
                else if (distanceToTarget < guideThreshold)
                {
                    // 激活惯性
                    _agentInertiaCounters[agent.ID] = inertiaDuration;
                    _agentPathIndices[agent.ID] = currentIndex + 1;
                    continue;
                }


                // 1. 计算驱动力（指向当前目标点）
                Vector3d drivingForce = Vector3d.Zero;
                Vector3d desiredVelocity = Vector3d.Zero;
                double strengthFactor = agent.GetCurrentStrengthFactor();

                // 检查惯性状态
                if (_agentInertiaCounters.ContainsKey(agent.ID) &&
                    _agentInertiaCounters[agent.ID] > 0)
                {
                    // 惯性模式使用实际运动方向
                    Vector3d inertiaVelocity = agent.MovementDirection * agent.DesiredSpeed * strengthFactor;
                    desiredVelocity = inertiaVelocity;
                    _agentInertiaCounters[agent.ID]--;
                }
                else
                {
                    // 正常模式使用目标方向
                    Vector3d directionToTarget = currentTarget - agent.Position;
                    if (directionToTarget.Length > 0.001)
                    {
                        directionToTarget.Unitize();
                        desiredVelocity = directionToTarget * agent.DesiredSpeed * strengthFactor;
                    }
                }

                // 计算驱动力
                if (desiredVelocity.Length > 0)
                {
                    Vector3d velocityDiff = desiredVelocity - agent.Velocity;
                    double relaxationTime = agent.RelaxationTime;
                    drivingForce = (agent.Mass / relaxationTime) * velocityDiff * strengthFactor;
                }

                // 2. 障碍物力计算
                Vector3d obstacleForce = Vector3d.Zero;
                foreach (var obstacle in obstacles)
                {
                    obstacleForce += obstacle.GetForce(agent);
                }

                // 3. 社会力计算
                Vector3d socialForce = Vector3d.Zero;
                foreach (var other in _currentAgents)
                {
                    if (other != agent)
                    {
                        double distance = agent.Position.DistanceTo(other.Position);
                        double minDistance = agent.Radius + other.Radius;
                        double effectiveDistance = distance - minDistance; // 负值表示重叠，正值表示身体圆柱体外侧间距

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

                // 4. 添加阻尼力
                Vector3d dampingForce = -dampingCoefficient * agent.Velocity;
                agent.Force = drivingForce + obstacleForce + socialForce + dampingForce;

                // 5. 运动更新
                Vector3d acceleration = agent.Force / agent.Mass;
                agent.Velocity += acceleration * timeStep;

                // 限制最大速度
                if (agent.Velocity.Length > agent.MaxSpeed)
                {
                    agent.Velocity = agent.Velocity / agent.Velocity.Length * agent.MaxSpeed;
                }

                // 更新位置和运动方向
                agent.Position += agent.Velocity * timeStep;
                agent.Trajectory.Add(agent.Position);

                // 更新运动方向（基于实际位移）
                if (agent.Trajectory.Count >= 2)
                {
                    Point3d prevPos = agent.Trajectory[agent.Trajectory.Count - 2];
                    Vector3d actualMovement = agent.Position - prevPos;

                    if (actualMovement.Length > 0.001)
                    {
                        agent.MovementDirection = actualMovement / actualMovement.Length;
                    }
                }
            }

            // 移除到达终点的代理
            foreach (var agent in agentsToRemove)
            {
                _currentAgents.Remove(agent);
                if (_agentPaths.ContainsKey(agent.ID)) _agentPaths.Remove(agent.ID);
                if (_agentPathIndices.ContainsKey(agent.ID)) _agentPathIndices.Remove(agent.ID);
                if (_agentInertiaCounters.ContainsKey(agent.ID)) _agentInertiaCounters.Remove(agent.ID);
                if (_agentStayCounters.ContainsKey(agent.ID)) _agentStayCounters.Remove(agent.ID);
            }
        }

        // 以下为辅助方法
        // 新增：将FindClosestVertexInView方法改为公共静态方法，以便复用
        public static Point3d? FindClosestPointInView(
            Point3d position,
            Vector3d movementDirection,
            IEnumerable<Point3d> points,
            double viewAngle,
            double viewDistance,
            double minDistanceThreshold = 0.1)
        {
            double minDistance = double.MaxValue;
            Point3d? closestPoint = null;

            foreach (var point in points)
            {
                Vector3d toPoint = point - position;
                double distance = toPoint.Length;

                // 跳过超出视距的点
                if (distance > viewDistance) continue;

                // 跳过太近的点
                if (distance < minDistanceThreshold) continue;

                toPoint.Unitize();

                // 检查是否在视野范围内
                double dotProduct = Vector3d.Multiply(movementDirection, toPoint);
                double angle = Math.Abs(Math.Acos(dotProduct));

                if (angle <= viewAngle / 2) // 视野是圆锥形，所以除以2
                {
                    if (distance < minDistance)
                    {
                        minDistance = distance;
                        closestPoint = point;
                    }
                }
            }

            return closestPoint;
        }
        // 新增结束：将FindClosestVertexInView方法改为公共静态方法，以便复用



        // 新增：卡住检测辅助方法
        private void CheckAndHandleStuckAgents(
         List<Obstacle> obstacles,
         double guideThreshold,
         double navigationOffset)
        {
            foreach (var agent in _currentAgents)
            {
                // 跳过已标记为卡住正在恢复的Agent
                if (agent.IsStuck) continue;

                // 跳过没有路径的Agent
                if (!_agentPaths.ContainsKey(agent.ID)) continue;

                // 检查是否卡住（StayDuration+10步后的位置与当前位置距离小于0.1）
                int checkSteps = agent.StayDuration + 10;
                if (agent.Trajectory.Count > checkSteps)
                {
                    Point3d currentPos = agent.Position;
                    Point3d oldPos = agent.Trajectory[agent.Trajectory.Count - checkSteps - 1];

                    if (currentPos.DistanceTo(oldPos) < 0.1)
                    {
                        // 标记为卡住状态
                        agent.IsStuck = true;
                        agent.StuckCounter = 0;

                        // 获取路径点
                        Polyline path = _agentPaths[agent.ID];
                        List<Point3d> pathPoints = new List<Point3d>(path);

                        // 寻找视野内最近的路径点
                        agent.RecoveryTarget = FindClosestPointInView(
                            agent.Position,
                            agent.MovementDirection,
                            pathPoints,
                            agent.ViewAngle,
                            agent.ViewDistance,
                            agent.Radius * 2);

                        // 如果找不到有效点，使用下一个路径点
                        if (agent.RecoveryTarget == null && _agentPathIndices.ContainsKey(agent.ID))
                        {
                            int idx = _agentPathIndices[agent.ID];
                            if (idx < path.Count)
                            {
                                agent.RecoveryTarget = path[idx];
                            }
                        }

                        RhinoApp.WriteLine($"Agent {agent.ID} stuck! Recovery target set.");
                    }
                }
            }
        }
        //新增结束：agent卡住检测机制


        public static bool IsPointInAnyObstacle(Point3d pt, List<Curve> obstacles)
        {
            foreach (Curve crv in obstacles)
            {
                if (crv == null) continue;

                PointContainment containment = crv.Contains(pt, Plane.WorldXY, 0.1);
                if (containment == PointContainment.Inside || containment == PointContainment.Coincident)
                    return true;
            }
            return false;
        }

        public static bool LineIntersectsAnyOffsetCurve(Line line, List<Curve> curves, Point3dComparer comparer)
        {
            LineCurve testCurve = new LineCurve(line);

            foreach (Curve curve in curves)
            {
                if (curve == null) continue;

                CurveIntersections intersections = Intersection.CurveCurve(testCurve, curve, 0.1, 0.1);
                if (HasInternalIntersection(intersections, testCurve, curve, comparer))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool HasInternalIntersection(CurveIntersections intersections, Curve testCurve, Curve obstacleCurve, Point3dComparer comparer)
        {
            if (intersections == null || intersections.Count == 0)
                return false;

            for (int i = 0; i < intersections.Count; i++)
            {
                IntersectionEvent eventData = intersections[i];

                // 跳过端点相交
                if (IsEndpointIntersection(eventData, testCurve, obstacleCurve, comparer))
                    continue;

                // 任何其他相交都被视为无效
                return true;
            }
            return false;
        }

        public static bool IsEndpointIntersection(IntersectionEvent eventData, Curve testCurve, Curve obstacleCurve, Point3dComparer comparer)
        {
            Point3d intersectionPt = eventData.PointA;

            // 检查交点是否是测试曲线的起点或终点
            Point3d testStart = testCurve.PointAtStart;
            Point3d testEnd = testCurve.PointAtEnd;

            bool isTestEndpoint = comparer.Equals(intersectionPt, testStart) ||
                                  comparer.Equals(intersectionPt, testEnd);

            // 检查交点是否是障碍物曲线的顶点
            bool isObstacleVertex = false;
            if (obstacleCurve is PolylineCurve plCurve)
            {
                Polyline pl;
                if (plCurve.TryGetPolyline(out pl))
                {
                    foreach (Point3d vertex in pl)
                    {
                        if (comparer.Equals(intersectionPt, vertex))
                        {
                            isObstacleVertex = true;
                            break;
                        }
                    }
                }
            }

            return isTestEndpoint && isObstacleVertex;
        }

        public static Tuple<Point3d, Point3d> NormalizeLine(Line line, Point3dComparer comparer)
        {
            // 确保一致的顺序进行无向比较
            return ComparePoints(line.From, line.To, comparer) < 0 ?
                Tuple.Create(line.From, line.To) :
                Tuple.Create(line.To, line.From);
        }

        public static int ComparePoints(Point3d a, Point3d b, Point3dComparer comparer)
        {
            // 使用基于容差的比较
            if (comparer.Equals(a, b)) return 0;
            if (Math.Abs(a.X - b.X) > 0.05) return a.X.CompareTo(b.X);
            if (Math.Abs(a.Y - b.Y) > 0.05) return a.Y.CompareTo(b.Y);
            return a.Z.CompareTo(b.Z);
        }

        public static Graph BuildGraph(List<Point3d> vertices, List<Line> connections, Point3dComparer comparer)
        {
            Graph graph = new Graph(comparer);

            // 添加所有顶点
            foreach (Point3d vertex in vertices)
            {
                graph.AddVertex(vertex);
            }

            // 添加连接
            foreach (Line connection in connections)
            {
                double length = connection.Length;
                graph.AddEdge(connection.From, connection.To, length);
            }

            return graph;
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


        protected override System.Drawing.Bitmap Icon => Resources.iconPEDSIM;
        public override Guid ComponentGuid => new Guid("585122B7-4028-43A0-AC3F-2A20DCF1A17D");

        // 以下为辅助类
        public class Point3dComparer : IEqualityComparer<Point3d>
        {
            public bool Equals(Point3d p1, Point3d p2)
            {
                return p1.DistanceTo(p2) < 0.1;
            }

            public int GetHashCode(Point3d p)
            {
                return p.X.GetHashCode() ^ p.Y.GetHashCode() ^ p.Z.GetHashCode();
            }
        }

        public class UndirectedLineComparer : IEqualityComparer<Tuple<Point3d, Point3d>>
        {
            private Point3dComparer _pointComparer;

            public UndirectedLineComparer(Point3dComparer comparer)
            {
                _pointComparer = comparer;
            }

            public bool Equals(Tuple<Point3d, Point3d> x, Tuple<Point3d, Point3d> y)
            {
                return (_pointComparer.Equals(x.Item1, y.Item1) && _pointComparer.Equals(x.Item2, y.Item2)) ||
                       (_pointComparer.Equals(x.Item1, y.Item2) && _pointComparer.Equals(x.Item2, y.Item1));
            }

            public int GetHashCode(Tuple<Point3d, Point3d> obj)
            {
                int h1 = _pointComparer.GetHashCode(obj.Item1);
                int h2 = _pointComparer.GetHashCode(obj.Item2);
                return h1 ^ h2; // 顺序不敏感的哈希
            }
        }

        public class Graph
        {
            private Dictionary<Point3d, List<Edge>> adjacencyList;
            private Point3dComparer comparer;

            public Graph(Point3dComparer comp)
            {
                comparer = comp;
                adjacencyList = new Dictionary<Point3d, List<Edge>>(comparer);
            }

            public void AddVertex(Point3d vertex)
            {
                if (!adjacencyList.ContainsKey(vertex))
                {
                    adjacencyList[vertex] = new List<Edge>();
                }
            }

            public void AddEdge(Point3d from, Point3d to, double weight)
            {
                if (!adjacencyList.ContainsKey(from)) AddVertex(from);
                if (!adjacencyList.ContainsKey(to)) AddVertex(to);

                // 添加双向边
                adjacencyList[from].Add(new Edge(to, weight));
                adjacencyList[to].Add(new Edge(from, weight));
            }

            public List<Point3d> AStarPath(Point3d start, Point3d end, Point3dComparer comp)
            {
                if (!adjacencyList.ContainsKey(start) || !adjacencyList.ContainsKey(end))
                    return null;

                var openSet = new PriorityQueue<Node>();
                var gScore = new Dictionary<Point3d, double>(comparer);
                var cameFrom = new Dictionary<Point3d, Point3d>(comparer);

                // 初始化所有节点的gScore为无穷大
                foreach (var vertex in adjacencyList.Keys)
                {
                    gScore[vertex] = double.MaxValue;
                }

                // 起始节点
                gScore[start] = 0;
                double hStart = Heuristic(start, end);
                openSet.Enqueue(new Node(start, 0, hStart));

                while (openSet.Count > 0)
                {
                    Node current = openSet.Dequeue();

                    if (comp.Equals(current.Point, end))
                    {
                        return ReconstructPath(cameFrom, current.Point, comp);
                    }

                    foreach (Edge neighborEdge in adjacencyList[current.Point])
                    {
                        Point3d neighbor = neighborEdge.Destination;
                        double tentativeGScore = gScore[current.Point] + neighborEdge.Weight;

                        double currentGScore;
                        if (!gScore.TryGetValue(neighbor, out currentGScore))
                        {
                            currentGScore = double.MaxValue;
                        }

                        if (tentativeGScore < currentGScore)
                        {
                            cameFrom[neighbor] = current.Point;
                            gScore[neighbor] = tentativeGScore;

                            double fScore = tentativeGScore + Heuristic(neighbor, end);
                            openSet.Enqueue(new Node(neighbor, tentativeGScore, fScore));
                        }
                    }
                }
                return null;
            }

            private double Heuristic(Point3d a, Point3d b)
            {
                // 简单的欧几里得距离启发式
                return a.DistanceTo(b);
            }

            private List<Point3d> ReconstructPath(Dictionary<Point3d, Point3d> cameFrom, Point3d current, Point3dComparer comp)
            {
                List<Point3d> path = new List<Point3d> { current };

                while (cameFrom.ContainsKey(current))
                {
                    current = cameFrom[current];
                    path.Add(current);
                }

                path.Reverse();
                return path;
            }
        }

        public class Edge
        {
            public Point3d Destination { get; }
            public double Weight { get; }

            public Edge(Point3d destination, double weight)
            {
                Destination = destination;
                Weight = weight;
            }
        }

        public class Node : IComparable<Node>
        {
            public Point3d Point { get; }
            public double GScore { get; }
            public double FScore { get; }

            public Node(Point3d point, double gScore, double fScore)
            {
                Point = point;
                GScore = gScore;
                FScore = fScore;
            }

            public int CompareTo(Node other)
            {
                return FScore.CompareTo(other.FScore);
            }
        }

        public class PriorityQueue<T> where T : IComparable<T>
        {
            private List<T> data;

            public PriorityQueue()
            {
                data = new List<T>();
            }

            public int Count => data.Count;

            public void Enqueue(T item)
            {
                data.Add(item);
                int ci = data.Count - 1;
                while (ci > 0)
                {
                    int pi = (ci - 1) / 2;
                    if (data[ci].CompareTo(data[pi]) >= 0) break;
                    T tmp = data[ci]; data[ci] = data[pi]; data[pi] = tmp;
                    ci = pi;
                }
            }

            public T Dequeue()
            {
                if (data.Count == 0) throw new InvalidOperationException("Queue is empty");
                int li = data.Count - 1;
                T frontItem = data[0];
                data[0] = data[li];
                data.RemoveAt(li);

                --li;
                int pi = 0;
                while (true)
                {
                    int ci = pi * 2 + 1;
                    if (ci > li) break;
                    int rc = ci + 1;
                    if (rc <= li && data[rc].CompareTo(data[ci]) < 0)
                        ci = rc;
                    if (data[pi].CompareTo(data[ci]) <= 0) break;
                    T tmp = data[pi]; data[pi] = data[ci]; data[ci] = tmp;
                    pi = ci;
                }
                return frontItem;
            }
        }
    }
}