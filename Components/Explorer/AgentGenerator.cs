using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace NeosExplorer
{
    public class AgentGenerator : GH_Component
    {
        public AgentGenerator()
          : base("Agent Generator", "Start",
                "Generate agents at starting points",
                "Neos", "NeosExplorer")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Starting Positions", "SP", "Starting positions", GH_ParamAccess.item);
            pManager.AddNumberParameter("Mass", "M", "Agent mass (kg)", GH_ParamAccess.item, 65.0);
            pManager.AddNumberParameter("Desired Speed", "Vd", "Desired walking speed (m/s)", GH_ParamAccess.item, 1.3);
            pManager.AddNumberParameter("Max Speed", "Vmax", "Maximum speed (m/s)", GH_ParamAccess.item, 2.5);
            pManager.AddNumberParameter("Radius", "R", "Body radius (m)", GH_ParamAccess.item, 0.3);
            pManager.AddNumberParameter("Repulsion Strength", "RS", "Agent repulsion strength (N)", GH_ParamAccess.item, 3.0);
            pManager.AddNumberParameter("View Angle", "VA", "Agent view angle in radians (180° = π ≈ 3.14)", GH_ParamAccess.item, Math.PI);
            pManager.AddNumberParameter("View Distance", "VD", "Agent view distance (m)", GH_ParamAccess.item, 20.0);
            pManager.AddIntegerParameter("Departure Interval", "DI", "Agent departure interval(time steps)", GH_ParamAccess.item, 50);
            pManager.AddNumberParameter("Relaxation Time", "RT", "Relaxation time for driving force (s)", GH_ParamAccess.item, 0.5);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Agent", "A", "Generated agent", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Point3d position = Point3d.Origin;
            double mass = 65.0;
            double desiredSpeed = 1.3;
            double maxSpeed = 2.5;
            double radius = 0.3;
            double repulsionStrength = 5.0;
            double viewAngle = Math.PI;
            double viewDistance = 20.0;
            int spawnInterval = 50;
            double relaxationTime = 0.5;

            // 获取输入参数
            DA.GetData(0, ref position);
            DA.GetData(1, ref mass);
            DA.GetData(2, ref desiredSpeed);
            DA.GetData(3, ref maxSpeed);
            DA.GetData(4, ref radius);
            DA.GetData(5, ref repulsionStrength);
            DA.GetData(6, ref viewAngle);
            DA.GetData(7, ref viewDistance);
            DA.GetData(8, ref spawnInterval);
            DA.GetData(9, ref relaxationTime); 

            Agent agent = new Agent(position)
            {
                Mass = mass,
                DesiredSpeed = desiredSpeed,
                MaxSpeed = maxSpeed,
                Radius = radius,
                RepulsionStrength = repulsionStrength,
                InitialPosition = position,
                ViewAngle = viewAngle,
                ViewDistance = viewDistance,
                SpawnInterval = spawnInterval,
                RelaxationTime = relaxationTime
            };

            DA.SetData(0, agent);
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_AgentGenerator;
        public override Guid ComponentGuid => new Guid("432D334A-13EE-47A1-90B7-6435EB871099");
    }

    public class Agent
    {
        // 路径导航相关属性
        public Polyline Path { get; set; }
        public int CurrentPathIndex { get; set; } = 1; // 从第二个点开始
        public int InertiaCounter { get; set; } = 0;
        public int InertiaDuration { get; set; } = 15;
        // 随机数生成器 (修复点)
        private Random _random = new Random(Guid.NewGuid().GetHashCode());

        // 生成间隔属性
        public int SpawnInterval { get; set; } = 50;

        // 生成计数器
        public int SpawnCounter { get; set; } = 0;

        // 唯一标识
        public Guid ID { get; } = Guid.NewGuid();

        // 位置和运动
        public Point3d Position { get; set; }
        public Vector3d Velocity { get; set; }
        public Vector3d Force { get; set; }
        public Vector3d MovementDirection { get; set; } = Vector3d.Zero;

        // 物理属性
        public double Mass { get; set; } = 65.0;
        public double DesiredSpeed { get; set; } = 1.3;
        public double MaxSpeed { get; set; } = 2.5;
        public double Radius { get; set; } = 0.3;
        public double RepulsionStrength { get; set; } = 5.0;

        // 感知参数
        public double ViewAngle { get; set; } = Math.PI;
        public double ViewDistance { get; set; } = 20.0;

        // 兴趣点行为
        public double InterestStrength { get; set; } = 1.0;
        public double InterestRadius { get; set; } = 1.0;
        public int StayDuration { get; set; } = 5;
        public int StayCounter { get; set; } = 0;
        public bool IsAtInterest { get; set; } = false;
        public double LeaveProbability { get; set; } = 0.05;
        public bool JustLeftInterest { get; set; } = false;
        
        public List<Point3d> InterestPoints { get; set; } = new List<Point3d>();
        private int _currentInterestIndex = -1;
        public int CurrentInterestPointIndex { get; set; } = -1;

        // 兴趣点访问次序控制：false=自动按距离排序（默认），true=按输入索引顺序
        public bool UseIndexOrder { get; set; } = false;

        // 返程兴趣点次序控制：false=反转次序（默认，如2-1-0），true=与正向一致（如0-1-2），仅在UseIndexOrder=true时有效
        public bool ReverseReturnOrder { get; set; } = false;

        // 目的地
        public Point3d Destination { get; set; }
        public double DestinationStrength { get; set; } = 1.0;
        public double DestinationRadius { get; set; } = 1.0;
        // 添加新属性
       

        // 轨迹记录
        public List<Point3d> Trajectory { get; } = new List<Point3d>();
        public Point3d InitialPosition { get; set; }

        // 卡住不动相关记录属性
        public int StuckCounter { get; set; } = 0;
        public bool IsStuck { get; set; } = false;
        public Point3d? RecoveryTarget { get; set; } = null;
        public double RelaxationTime { get; set; } = 0.5; 
        // 构造函数
        public Agent(Point3d position)
        {
            Position = position;
            Velocity = Vector3d.Zero;
            Force = Vector3d.Zero;
            InitialPosition = position;
            Trajectory.Add(position);
        }

        // 兴趣点访问器
        public Point3d? InterestPoint
        {
            get
            {
                if (_currentInterestIndex >= 0 && _currentInterestIndex < InterestPoints.Count)
                    return InterestPoints[_currentInterestIndex];
                return null;
            }
        }

        
        /// 获取当前路径目标点
        public Point3d? GetCurrentTarget()
        {
            if (Path == null || CurrentPathIndex >= Path.Count)
            {
                return Destination;
            }
            return Path[CurrentPathIndex];
        }

        //获取当前路段应该用的强度系数
        public double GetCurrentStrengthFactor()
        {
            // 如果还有未访问的兴趣点，使用兴趣点强度
            if (InterestPoints != null &&
                CurrentInterestPointIndex < InterestPoints.Count - 1)
            {
                return InterestStrength;
            }
            // 否则使用目的地强度
            return DestinationStrength;
        }
      

        //处理到达路径点
        public void HandleReachWaypoint()
        {
            if (Path == null || CurrentPathIndex >= Path.Count)
                return;

            Point3d currentTarget = Path[CurrentPathIndex];

            // 检查是否是兴趣点
            bool isInterestPoint = false;
            foreach (var ip in InterestPoints)
            {
                if (ip.DistanceTo(currentTarget) < InterestRadius)
                {
                    isInterestPoint = true;
                    break;
                }
            }

            if (isInterestPoint)
            {
                // 处理兴趣点停留
                IsAtInterest = true;
                StayCounter = 0;
            }
            else
            {
                // 普通路径点，激活惯性
                InertiaCounter = InertiaDuration;
            }

            // 移动到下一个路径点
            CurrentPathIndex++;
        }

        // 清除当前兴趣点
        public void ClearInterestPoint()
        {
            _currentInterestIndex = -1;
        }

        public bool ShouldLeave()
        {
            if (IsAtInterest)
            {
                //Random rand = new Random();
                //return rand.NextDouble() < LeaveProbability;
                return _random.NextDouble() < LeaveProbability;
            }
            return false;
        }

        //兴趣点访问方法，支持自动距离排序和索引顺序两种模式
        public bool MoveToNextInterestPoint()
        {
            // 如果没有兴趣点，返回false
            if (InterestPoints == null || InterestPoints.Count == 0)
            {
                _currentInterestIndex = -1;
                return false;
            }

            // 首次设置（选择第一个点）
            if (_currentInterestIndex < 0)
            {
                _currentInterestIndex = 0;
                return true;
            }

            // 索引顺序模式：直接按列表索引依次访问
            if (UseIndexOrder)
            {
                if (_currentInterestIndex + 1 < InterestPoints.Count)
                {
                    _currentInterestIndex++;
                    return true;
                }
                else
                {
                    _currentInterestIndex = -1;
                    return false;
                }
            }

            // 自动模式：找到下一个最近的点（贪心算法）
            Point3d lastVisited = InterestPoints[_currentInterestIndex];
            double minDistance = double.MaxValue;
            int nextIndex = -1;

            for (int i = _currentInterestIndex + 1; i < InterestPoints.Count; i++)
            {
                double dist = lastVisited.DistanceTo(InterestPoints[i]);
                if (dist < minDistance)
                {
                    minDistance = dist;
                    nextIndex = i;
                }
            }

            // 如果没有找到可访问的点
            if (nextIndex < 0)
            {
                _currentInterestIndex = -1;
                return false;
            }

            // 交换位置使最近点成为下一个
            var temp = InterestPoints[_currentInterestIndex + 1];
            InterestPoints[_currentInterestIndex + 1] = InterestPoints[nextIndex];
            InterestPoints[nextIndex] = temp;

            _currentInterestIndex++;
            return true;
        }

        // 创建代理的浅拷贝
        public Agent ShallowCopy()
        {
            return new Agent(Position)
            {
                // 路径导航属性
                Path = this.Path != null ? new Polyline(this.Path) : null,
                CurrentPathIndex = this.CurrentPathIndex,
                InertiaCounter = this.InertiaCounter,
                InertiaDuration = this.InertiaDuration,

                // 运动状态
                Velocity = Velocity,
                Force = Force,
                MovementDirection = MovementDirection,

                // 物理属性
                Mass = Mass,
                DesiredSpeed = DesiredSpeed,
                MaxSpeed = MaxSpeed,
                Radius = Radius,
                RepulsionStrength = RepulsionStrength,

                // 感知参数
                ViewAngle = ViewAngle,
                ViewDistance = ViewDistance,

                // 兴趣点行为
                InterestStrength = InterestStrength,
                InterestRadius = InterestRadius,
                StayDuration = StayDuration,
                StayCounter = StayCounter,
                IsAtInterest = IsAtInterest,
                LeaveProbability = LeaveProbability,
                JustLeftInterest = JustLeftInterest,              
                InterestPoints = new List<Point3d>(this.InterestPoints),
                _currentInterestIndex = this._currentInterestIndex,
                CurrentInterestPointIndex = this.CurrentInterestPointIndex,
                UseIndexOrder = this.UseIndexOrder,
                ReverseReturnOrder = this.ReverseReturnOrder,

                // 目的地
                Destination = Destination,
                DestinationStrength = DestinationStrength,
                DestinationRadius = DestinationRadius,

                // 轨迹和初始位置
                InitialPosition = InitialPosition,

                // 生成间隔属性
                SpawnInterval = SpawnInterval,
                SpawnCounter = SpawnCounter,

                // 复制轨迹（但不包括历史点）
                Trajectory = { Position }, // 只保留当前位置
                // 复制随机数生成器
                _random = new Random(this._random.Next())
            };
        }
    }
}