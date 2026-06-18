using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeosExplorer
{
    public class HybridSimulator : GH_Component
    {
        // === 模式定义 ===
        public enum AgentMode
        {
            PathFollowing,
            Exploring
        }

        // === 核心状态 ===
        private List<Agent> _currentAgents = new List<Agent>();
        private Dictionary<Guid, AgentMode> _agentModes = new Dictionary<Guid, AgentMode>();

        // === 生成相关 ===
        private Dictionary<Guid, int> _spawnTimers = new Dictionary<Guid, int>();
        private Dictionary<Guid, Agent> _spawnPoints = new Dictionary<Guid, Agent>();
        private Random _rnd = new Random();

        // === 路径导向模式 (Pedestrian) 数据 ===
        private Dictionary<Guid, Polyline> _agentPaths = new Dictionary<Guid, Polyline>();
        private Dictionary<Guid, int> _agentPathIndices = new Dictionary<Guid, int>();
        private Dictionary<Guid, Polyline> _spawnPointStaticPaths = new Dictionary<Guid, Polyline>();

        // === 自主探索模式 (Explorer) 数据 ===
        private Dictionary<Guid, Point3d?> _agentGuidingPoints = new Dictionary<Guid, Point3d?>();
        private Dictionary<Obstacle, Curve> _obstacleOffsetCache = new Dictionary<Obstacle, Curve>(); // 仅用于导航(NavigationOffset)

        // === 通用计数器 ===
        private int _currentStep = 0;
        private bool _simulationCompleted = false;
        private Dictionary<Guid, int> _agentInertiaCounters = new Dictionary<Guid, int>();
        private Dictionary<Guid, int> _agentStayCounters = new Dictionary<Guid, int>();

        // === 状态跟踪 ===
        private int _lastAgentCount = -1;
        private int _lastObstacleCount = -1;
        private int _lastSettingsHash = 0;
        private double? _evacuationTime = null;
        private int _targetRemaining = 0;

        public HybridSimulator()
          : base("Hybrid Simulator", "HybSim",
                "Simulate mixed crowd behavior: combines static path finding and dynamic exploration.",
                "Neos", "NeosExplorer")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Agents", "A", "Agent objects", GH_ParamAccess.list);
            pManager.AddGenericParameter("Obstacles", "O", "Obstacle objects", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Reset", "R", "Reset simulation", GH_ParamAccess.item, false);
            pManager.AddGenericParameter("Simulation Settings", "SS", "Settings including 'Wander Ratio'", GH_ParamAccess.item);
            pManager.AddCurveParameter("Evacuation Areas", "EA", "Closed polylines for evacuation areas", GH_ParamAccess.list);
            pManager[4].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Agents", "A", "Updated agents", GH_ParamAccess.list);
            pManager.AddPointParameter("Trajectories", "T", "Agent trajectories", GH_ParamAccess.tree);
            pManager.AddIntegerParameter("Current Step", "S", "Current simulation step", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Completed", "C", "Simulation completed", GH_ParamAccess.item);
            pManager.AddCurveParameter("Shortest Paths", "SP", "Pre-calculated static paths", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Total Time", "TT", "Total simulation time (s)", GH_ParamAccess.item);
            pManager.AddTextParameter("Evacuation Time", "ET", "Evacuation time info.\nTime required to reach the evacuation completion ratio (s).\nThis duration is equivalent to the time required for an organism to complete the same behavior in real world.", GH_ParamAccess.item);
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

            int steps = settings?.Steps ?? 5;
            double timeStep = settings?.TimeStep ?? 0.1;
            int maxAgents = settings?.MaxAgents ?? 20;
            int maxIterations = settings?.MaxIterations ?? 0;
            double wanderRatio = settings?.WanderRatio ?? 0.0;
            double navigationOffset = settings?.NavigationOffset ?? 0.5;
            double guideThreshold = settings?.GuideThreshold ?? 1.0;
            int inertiaDuration = settings?.InertiaDuration ?? 15;
            double dampingCoefficient = settings?.DampingCoefficient ?? 0.5;
            int recoverySteps = settings?.RecoverySteps ?? 25;
            double evacuationRatio = settings?.EvacuationRatio ?? 0.95;

            int initialCount = inputAgents.Count;
            _targetRemaining = (int)Math.Ceiling(initialCount * (1 - evacuationRatio));

            int currentSettingsHash = settings?.GetHashCode() ?? 0;
            bool inputChanged = inputAgents.Count != _lastAgentCount ||
                                obstacles.Count != _lastObstacleCount ||
                                currentSettingsHash != _lastSettingsHash;

            _lastAgentCount = inputAgents.Count;
            _lastObstacleCount = obstacles.Count;
            _lastSettingsHash = currentSettingsHash;

            if (inputChanged && !reset) reset = true;

            if (reset)
            {
                _currentAgents.Clear();
                _agentModes.Clear();
                _currentStep = 0;
                _simulationCompleted = false;
                _spawnTimers.Clear();
                _spawnPoints.Clear();
                _agentPaths.Clear();
                _agentPathIndices.Clear();
                _spawnPointStaticPaths.Clear();
                _agentGuidingPoints.Clear();
                _obstacleOffsetCache.Clear();
                _agentInertiaCounters.Clear();
                _agentStayCounters.Clear();
                _evacuationTime = null;

                foreach (var agent in inputAgents)
                {
                    _spawnPoints[agent.ID] = agent.ShallowCopy();
                    _spawnTimers[agent.ID] = agent.SpawnInterval > 0 ? _rnd.Next(0, agent.SpawnInterval) : 0;
                }

                PrecalculatePaths(obstacles, navigationOffset);
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

                SpawnAgents(maxAgents, wanderRatio);
                HybridSimulateStep(obstacles, guideThreshold, inertiaDuration, dampingCoefficient, navigationOffset, timeStep, recoverySteps);
                CheckEvacuationStatus(evacuationAreas, timeStep);
            }

            DA.SetDataList(0, _currentAgents);
            DataTree<Point3d> trajectories = new DataTree<Point3d>();
            for (int i = 0; i < _currentAgents.Count; i++)
                trajectories.AddRange(_currentAgents[i].Trajectory, new GH_Path(i));
            DA.SetDataTree(1, trajectories);

            DA.SetData(2, _currentStep);
            DA.SetData(3, _simulationCompleted);

            DataTree<Polyline> staticPathsOut = new DataTree<Polyline>();
            int pIdx = 0;
            foreach (var path in _spawnPointStaticPaths.Values)
                staticPathsOut.Add(path, new GH_Path(pIdx++));
            DA.SetDataTree(4, staticPathsOut);

            DA.SetData(5, _currentStep * timeStep);

            if (evacuationAreas.Count == 0) DA.SetData(6, "No Area");
            else if (_evacuationTime.HasValue) DA.SetData(6, _evacuationTime.Value.ToString("F2"));
            else DA.SetData(6, $"Not evacuated yet \r\n ({CountAgentsInEvacuationAreas(evacuationAreas)}/{_targetRemaining})");
        }

        private void SpawnAgents(int maxAgents, double wanderRatio)
        {
            var keys = _spawnPoints.Keys.OrderBy(k => k).ToList();
            foreach (var key in keys)
            {
                var sp = _spawnPoints[key];
                if (!_spawnTimers.ContainsKey(key)) _spawnTimers[key] = sp.SpawnInterval;
                _spawnTimers[key]--;

                if (_spawnTimers[key] <= 0 && _currentAgents.Count < maxAgents)
                {
                    Agent newAgent = sp.ShallowCopy();
                    newAgent.Position = sp.Position;
                    newAgent.Velocity = Vector3d.Zero;
                    newAgent.Force = Vector3d.Zero;
                    newAgent.Trajectory.Clear();
                    newAgent.Trajectory.Add(newAgent.Position);
                    // 确保起步时 MovementDirection 为 0，避免干扰视角计算
                    newAgent.MovementDirection = Vector3d.Zero;

                    bool isExplorer = _rnd.NextDouble() < wanderRatio;
                    if (!isExplorer && !_spawnPointStaticPaths.ContainsKey(key))
                        isExplorer = true;

                    AgentMode mode = isExplorer ? AgentMode.Exploring : AgentMode.PathFollowing;
                    _agentModes[newAgent.ID] = mode;

                    if (mode == AgentMode.PathFollowing)
                    {
                        _agentPaths[newAgent.ID] = _spawnPointStaticPaths[key];
                        _agentPathIndices[newAgent.ID] = 1;
                    }
                    else
                    {
                        _agentGuidingPoints[newAgent.ID] = null;
                    }

                    _agentInertiaCounters[newAgent.ID] = 0;
                    _agentStayCounters[newAgent.ID] = 0;

                    _currentAgents.Add(newAgent);
                    _spawnTimers[key] = sp.SpawnInterval;
                }
            }
        }

        private void HybridSimulateStep(List<Obstacle> obstacles, double guideThreshold, int inertiaDuration, double damping, double navOffset, double timeStep, int recoverySteps)
        {
            UpdateObstacleCache(obstacles, navOffset);
            UpdateStuckState(guideThreshold);

            List<Agent> toRemove = new List<Agent>();

            foreach (var agent in _currentAgents)
            {
                AgentMode mode = _agentModes[agent.ID];
                UpdateMoveDir(agent);

                bool isStaying = false;

                if (mode == AgentMode.PathFollowing)
                {
                    if (HandleStay(agent)) isStaying = true;
                }
                else if (mode == AgentMode.Exploring)
                {
                    if (agent.IsAtInterest)
                    {
                        if (agent.ShouldLeave() || agent.StayCounter >= agent.StayDuration)
                        {
                            agent.IsAtInterest = false;
                            agent.StayCounter = 0;
                            agent.JustLeftInterest = true;
                            if (!agent.MoveToNextInterestPoint()) agent.ClearInterestPoint();
                        }
                        else
                        {
                            agent.StayCounter++;
                            isStaying = true;
                        }
                    }
                }

                if (isStaying)
                {
                    agent.Velocity = Vector3d.Zero;
                    agent.Trajectory.Add(agent.Position);
                    continue;
                }

                Vector3d drivingForce = Vector3d.Zero;
                bool remove = false;

                if (mode == AgentMode.PathFollowing && agent.IsStuck)
                {
                    HandleStuckRecovery(agent, obstacles, damping, timeStep, guideThreshold, recoverySteps);
                    continue;
                }

                if (mode == AgentMode.PathFollowing)
                {
                    drivingForce = CalculatePedestrianForce(agent, inertiaDuration, guideThreshold, out remove);
                    if (_agentStayCounters.ContainsKey(agent.ID) && _agentStayCounters[agent.ID] > 0)
                    {
                        agent.Velocity = Vector3d.Zero;
                        agent.Trajectory.Add(agent.Position);
                        continue;
                    }
                }
                else
                {
                    drivingForce = CalculateExplorerForce(agent, obstacles, inertiaDuration, guideThreshold, out remove);
                    if (agent.IsAtInterest)
                    {
                        agent.Velocity = Vector3d.Zero;
                        agent.Trajectory.Add(agent.Position);
                        continue;
                    }
                }

                if (remove)
                {
                    toRemove.Add(agent);
                    continue;
                }

                Vector3d envForce = CalculateSharedForces(agent, obstacles);
                Vector3d dampingForce = -damping * agent.Velocity;
                agent.Force = drivingForce + envForce + dampingForce;

                Vector3d acc = agent.Force / agent.Mass;
                agent.Velocity += acc * timeStep;

                if (agent.Velocity.Length > agent.MaxSpeed)
                    agent.Velocity = agent.Velocity / agent.Velocity.Length * agent.MaxSpeed;

                agent.Position += agent.Velocity * timeStep;
                agent.Trajectory.Add(agent.Position);
                agent.JustLeftInterest = false;
            }

            foreach (var a in toRemove) CleanupAgent(a);
        }

        private void UpdateStuckState(double guideThreshold)
        {
            foreach (var agent in _currentAgents)
            {
                AgentMode mode = _agentModes[agent.ID];

                if (mode == AgentMode.PathFollowing)
                {
                    if (agent.IsStuck) continue;
                    if (_agentStayCounters.ContainsKey(agent.ID) && _agentStayCounters[agent.ID] > 0) continue;
                    if (agent.JustLeftInterest) continue;
                    if (agent.IsAtInterest) continue;

                    int checkSteps = agent.StayDuration + 10;
                    if (agent.Trajectory.Count > checkSteps)
                    {
                        if (agent.Position.DistanceTo(agent.Trajectory[agent.Trajectory.Count - checkSteps - 1]) < 0.1)
                        {
                            agent.IsStuck = true;
                            agent.StuckCounter = 0;

                            if (_agentPaths.ContainsKey(agent.ID))
                            {
                                agent.RecoveryTarget = PedestrianSimulator.FindClosestPointInView(
                                    agent.Position, agent.MovementDirection, _agentPaths[agent.ID],
                                    agent.ViewAngle, agent.ViewDistance);
                            }
                        }
                    }
                }
                else // AgentMode.Exploring
                {
                    // 完全复刻 ExplorerSimulator 中的静止检测，实时更新真假值
                    int checkSteps = agent.StayDuration + 10;
                    if (agent.Trajectory.Count > checkSteps)
                    {
                        Point3d currentPos = agent.Position;
                        Point3d oldPos = agent.Trajectory[agent.Trajectory.Count - checkSteps - 1];

                        if (currentPos.DistanceTo(oldPos) < agent.Radius * 0.5)
                        {
                            agent.IsStuck = true;
                        }
                        else
                        {
                            agent.IsStuck = false;
                        }
                    }
                }
            }
        }

        private Vector3d CalculatePedestrianForce(Agent agent, int inertiaDuration, double guideThreshold, out bool remove)
        {
            remove = false;
            Polyline path = _agentPaths[agent.ID];
            int idx = _agentPathIndices[agent.ID];

            if (idx >= path.Count) { remove = true; return Vector3d.Zero; }
            Point3d target = path[idx];

            bool isDest = (idx == path.Count - 1);
            double dist = agent.Position.DistanceTo(target);
            if (isDest && dist < agent.DestinationRadius) { remove = true; return Vector3d.Zero; }

            bool isCurrentTargetInterest = agent.InterestPoints.Any(p => p.DistanceTo(target) < 0.001);

            if (isCurrentTargetInterest)
            {
                if (dist < agent.InterestRadius)
                {
                    StartStay(agent);
                    agent.CurrentInterestPointIndex++;
                    return Vector3d.Zero;
                }
            }
            else
            {
                if (dist < guideThreshold)
                {
                    _agentInertiaCounters[agent.ID] = inertiaDuration;
                    _agentPathIndices[agent.ID] = idx + 1;
                    Vector3d inertia = agent.MovementDirection * agent.DesiredSpeed * agent.GetCurrentStrengthFactor();
                    return (agent.Mass / agent.RelaxationTime) * (inertia - agent.Velocity);
                }
            }

            return GetDrivingForceToTarget(agent, target);
        }

        // --- Explorer 模式修复 ---
        private Vector3d CalculateExplorerForce(Agent agent, List<Obstacle> obstacles, int inertiaDuration, double guideThreshold, out bool remove)
        {
            remove = false;

            if (agent.InterestPoint.HasValue && !agent.IsAtInterest && !agent.JustLeftInterest)
            {
                if (agent.Position.DistanceTo(agent.InterestPoint.Value) < agent.InterestRadius)
                {
                    agent.IsAtInterest = true;
                    agent.StayCounter = 0;
                    return Vector3d.Zero;
                }
            }
            if (agent.Position.DistanceTo(agent.Destination) < agent.DestinationRadius)
            {
                remove = true;
                return Vector3d.Zero;
            }

            Point3d finalTarget = agent.InterestPoint ?? agent.Destination;
            double strength = agent.GetCurrentStrengthFactor();
            Vector3d desiredVel = Vector3d.Zero;

            if (_agentInertiaCounters.ContainsKey(agent.ID) && _agentInertiaCounters[agent.ID] > 0)
            {
                Vector3d inertiaVel = agent.MovementDirection * agent.DesiredSpeed * strength;
                Vector3d combinedVel = inertiaVel;
                if (_agentGuidingPoints[agent.ID].HasValue)
                {
                    Vector3d gDir = _agentGuidingPoints[agent.ID].Value - agent.Position;
                    if (gDir.Length > 0.001)
                    {
                        gDir.Unitize();
                        // 修复点: 完全对齐 ExplorerSimulator 中的权重 (0.6 与 0.4)
                        combinedVel = (inertiaVel * 0.6) + (gDir * agent.DesiredSpeed * strength * 0.4);
                    }
                }
                desiredVel = combinedVel;
                _agentInertiaCounters[agent.ID]--;
                if (_agentInertiaCounters[agent.ID] <= 5) _agentGuidingPoints[agent.ID] = null;
                if (_agentInertiaCounters[agent.ID] == 0) _agentGuidingPoints[agent.ID] = null;
            }
            else
            {
                Vector3d directionToTarget = finalTarget - agent.Position;
                if (directionToTarget.Length > 0.001)
                {
                    directionToTarget.Unitize();
                    desiredVel = directionToTarget * agent.DesiredSpeed * strength;
                }

                // 核心修复：完全复刻 ExplorerSimulator 中的障碍物检测判定
                bool blocked = HasObstacleLineOriginal(agent.Position, finalTarget, obstacles, agent.Radius);

                if (blocked || agent.IsStuck)
                {
                    // 寻找特征点
                    Point3d? guide = FindBestGuidePoint(agent, obstacles);

                    if (guide.HasValue)
                    {
                        double distToVertex = agent.Position.DistanceTo(guide.Value);
                        if (distToVertex < guideThreshold)
                        {
                            _agentInertiaCounters[agent.ID] = inertiaDuration;
                            if (inertiaDuration <= 5) _agentGuidingPoints[agent.ID] = null;
                        }
                        else
                        {
                            _agentGuidingPoints[agent.ID] = guide;
                            Vector3d gDir = guide.Value - agent.Position;
                            if (gDir.Length > 0.001)
                            {
                                gDir.Unitize();
                                desiredVel = gDir * agent.DesiredSpeed * strength;
                            }
                        }
                    }
                    else
                    {
                        // 被挡住但没看见特征点 -> 清空引导点
                        _agentGuidingPoints[agent.ID] = null;
                    }
                }
                else
                {
                    // 路径无障碍 -> 强制清空引导点，直奔目标
                    _agentGuidingPoints[agent.ID] = null;
                }

                // 如果存在引导点，优先覆盖方向
                if (_agentGuidingPoints[agent.ID].HasValue)
                {
                    Vector3d gDir = _agentGuidingPoints[agent.ID].Value - agent.Position;
                    if (gDir.Length > 0.001)
                    {
                        gDir.Unitize();
                        desiredVel = gDir * agent.DesiredSpeed * strength;
                    }
                }
            }

            if (desiredVel.Length > 0)
            {
                Vector3d vDiff = desiredVel - agent.Velocity;
                return (agent.Mass / agent.RelaxationTime) * vDiff * strength;
            }
            return Vector3d.Zero;
        }

        private Vector3d CalculateSharedForces(Agent agent, List<Obstacle> obstacles)
        {
            Vector3d total = Vector3d.Zero;
            foreach (var obs in obstacles) total += obs.GetForce(agent);
            foreach (var other in _currentAgents)
            {
                if (other == agent) continue;
                double dist = agent.Position.DistanceTo(other.Position);
                double minDist = agent.Radius + other.Radius;
                double effDist = dist - minDist;
                if (dist < 10 * minDist)
                {
                    Vector3d dir = agent.Position - other.Position;
                    if (dir.Length > 0.001) dir.Unitize();
                    double avgRep = (agent.RepulsionStrength + other.RepulsionStrength) / 2.0;
                    if (effDist < 0) total += dir * avgRep * Math.Exp(-effDist / 0.1);
                    else if (dist < 2 * minDist) total += dir * avgRep * Math.Exp(-dist / minDist);
                    else total += dir * agent.RepulsionStrength * Math.Exp(-dist / (2 * minDist));
                }
            }
            return total;
        }

        private void PrecalculatePaths(List<Obstacle> obstacles, double offset)
        {
            foreach (var kvp in _spawnPoints)
            {
                try
                {
                    Point3d start = kvp.Value.Position; start.Z = 0;
                    Point3d end = kvp.Value.Destination; end.Z = 0;
                    List<Point3d> interests = kvp.Value.InterestPoints.Select(p => { var pt = p; pt.Z = 0; return pt; }).ToList();
                    Polyline path = PedestrianSimulator.CalculateStaticPath(start, interests, end, obstacles, offset, kvp.Value.UseIndexOrder);
                    if (path != null && path.Count >= 2) _spawnPointStaticPaths[kvp.Key] = path;
                }
                catch { }
            }
        }

        private void UpdateObstacleCache(List<Obstacle> obstacles, double offset)
        {
            foreach (var obs in obstacles)
            {
                if (!_obstacleOffsetCache.ContainsKey(obs) || _obstacleOffsetCache[obs] == null)
                {
                    try { _obstacleOffsetCache[obs] = obs.Geometry.Offset(Plane.WorldXY, offset, 0.1, CurveOffsetCornerStyle.Sharp)[0]; }
                    catch { _obstacleOffsetCache[obs] = obs.Geometry; }
                }
            }
        }

        // *** 关键方法：复刻原版基于 Radius 的障碍物检测 ***
        private bool HasObstacleLineOriginal(Point3d start, Point3d end, List<Obstacle> obstacles, double radius)
        {
            if (start.DistanceTo(end) < radius * 0.5) return false;
            Line line = new Line(start, end);

            foreach (var obstacle in obstacles)
            {
                if (obstacle.Geometry == null) continue;

                // 修复点: 复刻 ExplorerSimulator: 优先使用缓存 (实际上其带有 navigationOffset)
                Curve buffer = null;
                if (_obstacleOffsetCache.ContainsKey(obstacle) && _obstacleOffsetCache[obstacle] != null)
                {
                    buffer = _obstacleOffsetCache[obstacle];
                }
                else
                {
                    try
                    {
                        Curve[] offsets = obstacle.Geometry.Offset(Plane.WorldXY, radius, 0.1, CurveOffsetCornerStyle.Sharp);
                        if (offsets != null && offsets.Length > 0) buffer = offsets[0];
                    }
                    catch { }
                }

                if (buffer == null) continue;

                var intersection = Intersection.CurveCurve(new LineCurve(line), buffer, 0.01, 0.01);

                if (intersection.Count > 0)
                {
                    foreach (var eventData in intersection)
                    {
                        // 排除端点 (ExplorerSimulator逻辑)
                        if (eventData.ParameterA > 0.01 && eventData.ParameterA < 0.99)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private Point3d? FindBestGuidePoint(Agent agent, List<Obstacle> obstacles)
        {
            Point3d? best = null;
            double minDist = double.MaxValue;

            Vector3d viewDir = agent.MovementDirection;

            foreach (var obs in obstacles)
            {
                if (obs.Geometry == null) continue;

                Curve c = _obstacleOffsetCache.ContainsKey(obs) && _obstacleOffsetCache[obs] != null ? _obstacleOffsetCache[obs] : obs.Geometry;

                if (!c.TryGetPolyline(out Polyline pl))
                {
                    PolylineCurve polyCurve = c.ToPolyline(0, 0, 0, 1000, 0, 0.1, 0, 0, true);
                    if (polyCurve != null) polyCurve.TryGetPolyline(out pl);
                }

                if (pl == null) continue;

                // 修复点: 完全复刻 ExplorerSimulator 的视线筛选逻辑
                foreach (var vertex in pl)
                {
                    Vector3d toVertex = vertex - agent.Position;
                    double distance = toVertex.Length;

                    if (distance > agent.ViewDistance) continue;
                    if (distance < agent.Radius * 0.5) continue;

                    toVertex.Unitize();

                    double dotProduct = Vector3d.Multiply(viewDir, toVertex);
                    double angle = Math.Abs(Math.Acos(dotProduct));

                    if (angle <= agent.ViewAngle / 2)
                    {
                        if (distance < minDist)
                        {
                            minDist = distance;
                            best = vertex;
                        }
                    }
                }
            }
            return best;
        }

        private void HandleStuckRecovery(Agent agent, List<Obstacle> obstacles, double damping, double timeStep, double threshold, int maxSteps)
        {
            agent.StuckCounter++;
            if (agent.RecoveryTarget.HasValue)
            {
                Vector3d desired = agent.RecoveryTarget.Value - agent.Position;
                if (desired.Length > 0.001) desired.Unitize();
                desired *= agent.DesiredSpeed;
                Vector3d drive = (agent.Mass / agent.RelaxationTime) * (desired - agent.Velocity);
                Vector3d obsForce = Vector3d.Zero;
                foreach (var o in obstacles) obsForce += o.GetForce(agent);
                agent.Force = drive + obsForce - damping * agent.Velocity;
                agent.Velocity += (agent.Force / agent.Mass) * timeStep;
                agent.Position += agent.Velocity * timeStep;
                agent.Trajectory.Add(agent.Position);
                if (agent.Position.DistanceTo(agent.RecoveryTarget.Value) < threshold) { agent.IsStuck = false; agent.RecoveryTarget = null; }
            }
            else agent.IsStuck = false;
            if (agent.StuckCounter > maxSteps) { agent.IsStuck = false; agent.RecoveryTarget = null; }
        }

        private Vector3d GetDrivingForceToTarget(Agent agent, Point3d target)
        {
            Vector3d dir = target - agent.Position;
            if (dir.Length > 0.001) dir.Unitize();
            Vector3d desVel = dir * agent.DesiredSpeed * agent.GetCurrentStrengthFactor();
            return (agent.Mass / agent.RelaxationTime) * (desVel - agent.Velocity) * agent.GetCurrentStrengthFactor();
        }

        private bool HandleStay(Agent agent)
        {
            if (_agentStayCounters.ContainsKey(agent.ID) && _agentStayCounters[agent.ID] > 0)
            {
                if (agent.ShouldLeave()) { EndStay(agent); _agentPathIndices[agent.ID]++; return false; }
                _agentStayCounters[agent.ID]--;
                if (_agentStayCounters[agent.ID] > 0) return true;
                EndStay(agent); _agentPathIndices[agent.ID]++; return false;
            }
            return false;
        }

        private void StartStay(Agent agent) { _agentStayCounters[agent.ID] = agent.StayDuration; agent.IsAtInterest = true; }
        private void EndStay(Agent agent) { _agentStayCounters[agent.ID] = 0; agent.IsAtInterest = false; agent.JustLeftInterest = true; }

        private void UpdateMoveDir(Agent agent)
        {
            if (agent.Velocity.Length > 0.001)
            {
                agent.MovementDirection = agent.Velocity / agent.Velocity.Length;
            }
            else if (agent.Trajectory.Count >= 2)
            {
                Vector3d m = agent.Position - agent.Trajectory[agent.Trajectory.Count - 2];
                if (m.Length > 0.001) agent.MovementDirection = m / m.Length;
            }
        }

        private void CleanupAgent(Agent agent)
        {
            _currentAgents.Remove(agent); _agentModes.Remove(agent.ID); _agentPaths.Remove(agent.ID);
            _agentPathIndices.Remove(agent.ID); _agentStayCounters.Remove(agent.ID);
            _agentInertiaCounters.Remove(agent.ID); _agentGuidingPoints.Remove(agent.ID);
        }
        private int CountAgentsInEvacuationAreas(List<Curve> areas)
        {
            int c = 0;
            foreach (var a in _currentAgents) { Point3d p = a.Position; p.Z = 0; foreach (var area in areas) if (area.Contains(p, Plane.WorldXY, 0.1) == PointContainment.Inside) { c++; break; } }
            return c;
        }
        private void CheckEvacuationStatus(List<Curve> areas, double ts)
        {
            if (!_evacuationTime.HasValue && areas.Count > 0) { if (CountAgentsInEvacuationAreas(areas) <= _targetRemaining) _evacuationTime = _currentStep * ts; }
        }

        protected override System.Drawing.Bitmap Icon => Resources.iconHybSim;
        public override Guid ComponentGuid => new Guid("076A4D5A-9AC7-456B-8133-5F2E19BDFC2B");
    }
}
