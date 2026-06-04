# 行人模拟模块 (NeosExplorer)

## 1. 模型概述

行人模拟模块基于 **Helbing 社会力模型 (Social Force Model, SFM)**，结合 **可视图 (Visibility Graph) + A\* 路径规划** 与 **自主探索行为模型**，实现多模式人群微观仿真。模块支持三种行为模式：路径跟随 (Path Following)、自主探索 (Exploring) 以及混合模式 (Hybrid)。

| 组件 | 方法 | 参考文献 |
|-----------|--------|-----------|
| 人群运动动力学 | Helbing 社会力模型 | Helbing & Molnár (1995); Helbing, Farkas & Vicsek (2000) |
| 路径规划 | 可视图 + A\* 算法 | Lozano-Pérez & Wesley (1979); Hart, Nilsson & Raphael (1968) |
| 自主探索 | 惯性感知引导模型 | 基于视野锥的局部导航 |
| 混合行为 | 比例分配双模式 | -- |

本模块提供三个核心模拟器及配套组件：

| 组件 | 简称 | 功能描述 |
|-----------|------|-------------|
| **PedestrianSimulator** | `PedSim` | 路径跟随模式：基于全局最短路径引导的社会力模型 |
| **ExplorerSimulator** | `ExpSim` | 自主探索模式：基于惯性与局部感知的探索行为 |
| **HybridSimulator** | `HybSim` | 混合模式：按 `WanderRatio` 比例分配路径跟随与探索行为 |
| **AgentGenerator** | `Start` | 代理生成器，定义行人初始属性 |
| **Destination** | `End` | 目的地设置，支持往返行程 |
| **InterestPoint** | `Interest` | 兴趣点设置，控制停留行为 |
| **Obstacle** | `Obstacle` | 障碍物定义，含排斥力参数 |
| **PathNetwork** | `Path` | 独立最短路径计算（可视图 + A\*） |
| **DeconstructAgent** | `DeconAg` | 基础代理可视化（位置、速度、轨迹、视野扇形） |
| **DeconstructAgentPlus** | `DeconAg+` | 增强代理可视化（含物理属性、状态信息） |
| **CrowdDensityVisualization** | `DensityViz` | 人群密度网格可视化 |
| **SimulationSettings** | `Settings` | 全局仿真参数配置 |

---

## 2. 社会力模型 (Social Force Model)

### 2.1 运动方程

每个代理 $i$ 的运动遵循牛顿第二定律：

$$m_i \frac{d\mathbf{v}_i}{dt} = \mathbf{f}_i^{drive} + \mathbf{f}_i^{obs} + \mathbf{f}_i^{soc} + \mathbf{f}_i^{damp}$$

速度更新：

$$\mathbf{v}_i(t + \Delta t) = \mathbf{v}_i(t) + \frac{\mathbf{F}_i}{m_i} \Delta t$$

位置更新：

$$\mathbf{x}_i(t + \Delta t) = \mathbf{x}_i(t) + \mathbf{v}_i(t + \Delta t) \Delta t$$

| 参数 | 符号 | 单位 | 说明 |
|-----------|--------|------|-------------|
| 代理质量 | $m_i$ | $\mathrm{kg}$ | 行人质量（状态变量） |
| 速度 | $\mathbf{v}_i$ | $\mathrm{m/s}$ | 当前速度向量（状态变量） |
| 位置 | $\mathbf{x}_i$ | $\mathrm{m}$ | 当前位置向量（状态变量） |
| 合力 | $\mathbf{F}_i$ | $\mathrm{N}$ | 作用于代理的合力 |
| 时间步长 | $\Delta t$ | $\mathrm{s}$ | 仿真步长 $= \mathrm{TPS}$ |

### 2.2 驱动力 (Driving Force)

代理朝向目标点移动的驱动力，使其趋向期望速度：

$$\mathbf{f}_i^{drive} = \frac{m_i}{\tau_i} \left( \mathbf{v}_i^{desired} - \mathbf{v}_i \right) \cdot s_i$$

其中期望速度：

$$\mathbf{v}_i^{desired} = \mathbf{e}_i \cdot v_i^{des} \cdot s_i$$

单位向量 $\mathbf{e}_i$ 根据运行模式确定：
- **路径跟随模式**：$\mathbf{e}_i = \frac{\mathbf{x}_{target} - \mathbf{x}_i}{\|\mathbf{x}_{target} - \mathbf{x}_i\|}$，指向当前路径点
- **惯性模式**：$\mathbf{e}_i = \mathbf{d}_i^{move}$，使用实际运动方向
- **探索模式（引导点存在）**：$\mathbf{e}_i = 0.6 \mathbf{d}_i^{move} + 0.4 \frac{\mathbf{x}_{guide} - \mathbf{x}_i}{\|\mathbf{x}_{guide} - \mathbf{x}_i\|}$，结合惯性与引导方向

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 期望速度 | $v_i^{des}$ | $\mathrm{m/s}$ | 行人期望行走速度 | 1.3 | [0.1, 5.0] |
| 松弛时间 | $\tau_i$ | $\mathrm{s}$ | 速度调节至期望速度的特征时间 | 0.5 | [0.01, 10.0] |
| 强度因子 | $s_i$ | -- | 路段吸引力系数（兴趣点强度或目的地强度）| 1.0 | [0.01, 10.0] |
| 运动方向 | $\mathbf{d}_i^{move}$ | -- | 归一化的实际运动方向向量 | -- | -- |

### 2.3 障碍物排斥力 (Obstacle Repulsion Force)

障碍物对代理产生指数衰减的排斥力：

$$\mathbf{f}_{ij}^{obs} = A_j \cdot \exp\left( -\frac{d_{ij}^{eff}}{B_j} \right) \cdot \mathbf{n}_{ij}$$

有效距离：

$$d_{ij}^{eff} = \max\left( d_{ij} - r_i, \; 0.001 \right)$$

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 障碍物排斥强度 | $A_j$ | $\mathrm{N}$ | 障碍物对行人的最大排斥力 | 500 | [0, 10000] |
| 障碍物作用距离 | $B_j$ | $\mathrm{m}$ | 排斥力有效作用范围 | 0.2 | [0.01, 10.0] |
| 代理身体半径 | $r_i$ | $\mathrm{m}$ | 行人身体圆柱体半径 | 0.3 | [0.05, 2.0] |
| 代理到障碍物距离 | $d_{ij}$ | $\mathrm{m}$ | 代理中心到障碍物最近点的距离 | -- | -- |
| 单位方向向量 | $\mathbf{n}_{ij}$ | -- | 从障碍物指向代理的单位向量 | -- | -- |

> **注意：** 障碍物曲线需为逆时针绘制的闭合多段线。排斥力作用于障碍物偏移曲线的法线方向。

### 2.4 社会力（行人交互力）

行人之间的排斥力按间距分为三个强度等级，检测范围为 $10 \times d_{min}$：

$$d_{min} = r_i + r_j$$

$$d_{eff} = d_{ij} - d_{min}$$

**等级 1 — 强排斥力（身体重叠，$d_{eff} < 0$）：**

$$\mathbf{f}_{ij}^{soc,strong} = \frac{A_i + A_j}{2} \cdot \exp\left( \frac{-d_{eff}}{0.1} \right) \cdot \mathbf{n}_{ij}$$

**等级 2 — 中等排斥力（近距离，$d_{min} \leq d_{ij} < 2d_{min}$）：**

$$\mathbf{f}_{ij}^{soc,med} = \frac{A_i + A_j}{2} \cdot \exp\left( -\frac{d_{ij}}{d_{min}} \right) \cdot \mathbf{n}_{ij}$$

**等级 3 — 弱排斥力（社交距离，$2d_{min} \leq d_{ij} < 10d_{min}$）：**

$$\mathbf{f}_{ij}^{soc,weak} = A_i \cdot \exp\left( -\frac{d_{ij}}{2d_{min}} \right) \cdot \mathbf{n}_{ij}$$

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 代理排斥强度 | $A_i$ | $\mathrm{N}$ | 行人 i 的社会排斥强度 | 5.0 | [0.1, 100.0] |
| 行人间距 | $d_{ij}$ | $\mathrm{m}$ | 代理 i 与 j 的欧氏距离 | -- | -- |
| 最小身体间距 | $d_{min}$ | $\mathrm{m}$ | 两代理身体半径之和 | -- | -- |
| 有效间距 | $d_{eff}$ | $\mathrm{m}$ | 身体圆柱体外侧间距（负值表示重叠） | -- | -- |

> **物理意义：** 指数项中的 $0.1\,\mathrm{m}$ 为重叠衰减特征长度，反映身体弹性压缩的短程排斥特性。

### 2.5 阻尼力 (Damping Force)

模拟环境摩擦与能量耗散：

$$\mathbf{f}_i^{damp} = -\mu \cdot \mathbf{v}_i$$

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 阻尼系数 | $\mu$ | $\mathrm{N \cdot s/m}$ 或 $\mathrm{kg/s}$ | 速度衰减系数 | 0.5 | [0, 1.0] |

### 2.6 速度限制

代理速度被限制在最大速度范围内：

$$\|\mathbf{v}_i\| = \min\left( \|\mathbf{v}_i\|, \; v_i^{max} \right)$$

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 最大速度 | $v_i^{max}$ | $\mathrm{m/s}$ | 代理速度上限 | 2.5 | [0.5, 10.0] |

---

## 3. 路径规划模型（可视图 + A\* 算法）

### 3.1 算法流程

路径跟随模式使用可视图法与 A\* 算法计算从起点经兴趣点至目的地的全局最短路径：

1. **障碍物偏移**：将障碍物轮廓向外偏移 $\delta_{nav}$ 生成导航安全边界
2. **顶点收集**：收集起点、兴趣点、终点及所有偏移障碍物顶点
3. **可视图构建**：连接所有可见的顶点对（连线不与障碍物偏移曲线内部相交）
4. **A\* 寻路**：使用欧氏距离启发式 $h(\mathbf{x}) = \|\mathbf{x} - \mathbf{x}_{goal}\|$ 搜索最短路径

### 3.2 路径分段

当存在多个兴趣点时，路径按兴趣点与起点的直线距离升序分段连接：

$$\mathrm{waypoints} = [\mathbf{x}_{start}, \; \mathrm{sort}(\mathbf{x}_{POI}, \|\cdot - \mathbf{x}_{start}\|), \; \mathbf{x}_{end}]$$

每段路径独立调用 A\* 并在连接处去重。

### 3.3 可见性判断

连线有效性通过双重检测判定：
- **曲线相交检测**：使用 Rhino `CurveCurve` 交集检测，排除端点相交
- **采样点检测**：在连线的 $0.25,\, 0.5,\, 0.75$ 处采样，检查是否在障碍物内部

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 导航偏移 | $\delta_{nav}$ | $\mathrm{m}$ | 障碍物安全边界偏移距离 | 0.5 | [0, 5.0] |

---

## 4. 自主探索行为模型

### 4.1 行为概述

探索模式下代理不依赖预计算路径，而是基于局部感知实时导航。核心机制包括：
- **引导点系统**：检测视野内的障碍物顶点作为导航引导点
- **惯性效应**：到达引导点后激活惯性，维持原运动方向若干步
- **动态避障**：实时检测前方障碍物，切换至引导点导航

### 4.2 引导点检测

在代理的视野锥形区域内寻找最近的障碍物顶点：

$$\theta = \arccos\left( \mathbf{d}_i^{move} \cdot \frac{\mathbf{x}_{vertex} - \mathbf{x}_i}{\|\mathbf{x}_{vertex} - \mathbf{x}_i\|} \right) \leq \frac{\alpha_i}{2}$$

$$d_{vertex} \leq D_i^{view}, \quad d_{vertex} \geq 0.5 r_i$$

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 视野角度 | $\alpha_i$ | $\mathrm{rad}$ | 代理视野圆锥半角 | $\pi$ (180°) | [0, $2\pi$] |
| 视野距离 | $D_i^{view}$ | $\mathrm{m}$ | 代理最远可视距离 | 20.0 | [0.1, 100.0] |

### 4.3 惯性引导速度合成

当惯性激活时，期望速度为惯性方向与引导点方向的加权合成：

$$\mathbf{v}^{desired} = 0.6 \cdot \mathbf{v}^{inertia} + 0.4 \cdot \mathbf{v}^{guide}$$

其中：

$$\mathbf{v}^{inertia} = \mathbf{d}_i^{move} \cdot v_i^{des} \cdot s_i$$

$$\mathbf{v}^{guide} = \frac{\mathbf{x}_{guide} - \mathbf{x}_i}{\|\mathbf{x}_{guide} - \mathbf{x}_i\|} \cdot v_i^{des} \cdot s_i$$

> **权重说明：** 0.6/0.4 的惯性/引导权重确保运动连续性，避免代理在引导点间抖动。

### 4.4 障碍物碰撞检测

检测从代理到目标点的连线是否与障碍物偏移曲线相交（排除端点）：

$$\mathrm{blocked} = \mathrm{Intersection}(\overline{\mathbf{x}_i \mathbf{x}_{target}}, \; \mathrm{Offset}(\mathrm{Obstacle}, r_i)) \neq \emptyset$$

当检测到障碍时，激活引导点导航；路径畅通时清空引导点并直达目标。

---

## 5. 混合模式 (Hybrid Mode)

### 5.1 模式分配

通过 `WanderRatio` 参数控制行为模式的比例分配：

$$\mathrm{mode}_i = \begin{cases} \text{PathFollowing} & \text{if } \xi_i \geq W_R \\[8pt] \text{Exploring} & \text{if } \xi_i < W_R \end{cases}$$

其中 $\xi_i \sim U(0,1)$ 为代理生成时的均匀随机数。

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 漫游比例 | $W_R$ | -- | 探索模式代理占比 | 0.0 | [0, 1.0] |

- $W_R = 0$：全部代理使用路径跟随模式
- $W_R = 1$：全部代理使用探索模式
- $0 < W_R < 1$：按比例随机分配

---

## 6. 兴趣点与停留行为

### 6.1 兴趣点访问机制

兴趣点按与起点的直线距离升序排列：

$$\mathrm{POI}_{sorted} = \mathrm{sort}(\{\mathbf{x}_{POI}\}, \; \|\cdot - \mathbf{x}_{start}\|)$$

代理到达兴趣点（距离 $< r_{interest}$）后进入停留状态：
- 停留计数器初始化为 `StayDuration`
- 每步以概率 $p_{leave}$ 尝试离开
- 若停留计数器归零，强制离开

### 6.2 停留参数

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 兴趣点强度 | $s_{interest}$ | -- | 兴趣点对代理的吸引力系数 | 1.0 | [0.01, 10.0] |
| 兴趣点半径 | $r_{interest}$ | $\mathrm{m}$ | 判定到达兴趣点的距离阈值 | 1.0 | [0.01, 10.0] |
| 停留时长 | $T_{stay}$ | steps | 在兴趣点的最大停留时间步数 | 5 | [0, 1000] |
| 离开概率 | $p_{leave}$ | -- | 每步尝试离开的概率 | 0.05 | [0, 1.0] |
| 目的地强度 | $s_{dest}$ | -- | 目的地对代理的吸引力系数 | 1.0 | [0.01, 10.0] |
| 目的地半径 | $r_{dest}$ | $\mathrm{m}$ | 判定到达目的地的距离阈值 | 1.0 | [0.01, 10.0] |

> **注意：** 当代理还有未访问的兴趣点时，`GetCurrentStrengthFactor()` 返回兴趣点强度；否则返回目的地强度。

---

## 7. 卡住检测与恢复机制

### 7.1 检测条件

当代理在 $T_{stay} + 10$ 步内的位移小于阈值时判定为卡住：

$$\|\mathbf{x}_i(t) - \mathbf{x}_i(t - T_{stay} - 10)\| < \epsilon_{stuck}$$

| 模式 | 阈值 $\epsilon_{stuck}$ | 说明 |
|------|:---:|:---|
| 路径跟随 | $0.1\,\mathrm{m}$ | 固定阈值 |
| 探索模式 | $0.5 \times r_i$ | 与代理半径相关 |

### 7.2 恢复机制

路径跟随模式下的恢复流程：
1. 在视野内寻找最近的路径点作为 `RecoveryTarget`
2. 以目的地强度驱动代理向恢复点移动
3. 到达恢复点后解除卡住状态，恢复正常路径跟随
4. 若超过 `RecoverySteps` 步未到达，强制解除卡住

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 恢复步数 | $N_{recover}$ | steps | 最大恢复尝试步数 | 25 | [0, 1000] |

探索模式下的恢复：
- `IsStuck` 为 `true` 时触发引导点重搜索
- 检测到足够位移后自动解除

---

## 8. 生成与调度系统

### 8.1 代理生成

每个生成点按固定间隔产生代理：

$$t_{spawn}^{next} = t_{spawn}^{prev} + \Delta t_{spawn}$$

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 生成间隔 | $\Delta t_{spawn}$ | steps | 同一生成点连续产生代理的时间间隔 | 50 | [0, 10000] |
| 最大代理数 | $N_{max}$ | -- | 仿真中同时存在的最大代理数量 | 20 | [1, 10000] |

### 8.2 生成器参数

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 代理质量 | $m$ | $\mathrm{kg}$ | 行人质量 | 65.0 | [1.0, 500.0] |
| 期望速度 | $v^{des}$ | $\mathrm{m/s}$ | 正常行走期望速度 | 1.3 | [0.1, 5.0] |
| 最大速度 | $v^{max}$ | $\mathrm{m/s}$ | 速度上限 | 2.5 | [0.5, 10.0] |
| 身体半径 | $r$ | $\mathrm{m}$ | 行人身体圆柱体半径 | 0.3 | [0.05, 2.0] |
| 排斥强度 | $A$ | $\mathrm{N}$ | 社会排斥力强度 | 5.0 | [0.1, 100.0] |
| 视野角度 | $\alpha$ | $\mathrm{rad}$ | 视野圆锥全角 | $\pi$ | [0, $2\pi$] |
| 视野距离 | $D^{view}$ | $\mathrm{m}$ | 最远可视距离 | 20.0 | [0.1, 100.0] |
| 松弛时间 | $\tau$ | $\mathrm{s}$ | 驱动力响应时间常数 | 0.5 | [0.01, 10.0] |

---

## 9. 疏散分析

### 9.1 疏散判定

疏散区域由闭合多段线定义。当疏散区域内的代理数量降至目标值以下时，记录疏散时间：

$$N_{remain}(t) \leq N_{target} = \lceil N_{initial} \cdot (1 - \eta_{evac}) \rceil$$

$$t_{evac} = n_{step} \cdot \Delta t$$

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 疏散完成率 | $\eta_{evac}$ | -- | 判定疏散完成的代理比例 | 0.95 | [0, 1.0] |
| 疏散时间 | $t_{evac}$ | $\mathrm{s}$ | 达到疏散完成率所需时间 | -- | -- |

---

## 10. 输入/输出端口

### 10.1 Agent Generator (`Start`) — 代理生成

| 端口 | 名称 | 类型 | 必填 | 说明 |
|------|------|------|----------|-------------|
| 0 | `SP` | Point | **是** | 起始位置（生成点坐标） |
| 1 | `M` | Number | 否 | 质量 $m$ [kg]（默认 65.0） |
| 2 | `Vd` | Number | 否 | 期望速度 $v^{des}$ [m/s]（默认 1.3） |
| 3 | `Vmax` | Number | 否 | 最大速度 $v^{max}$ [m/s]（默认 2.5） |
| 4 | `R` | Number | 否 | 身体半径 $r$ [m]（默认 0.3） |
| 5 | `RS` | Number | 否 | 排斥强度 $A$ [N]（默认 5.0） |
| 6 | `VA` | Number | 否 | 视野角度 $\alpha$ [rad]（默认 $\pi$） |
| 7 | `VD` | Number | 否 | 视野距离 $D^{view}$ [m]（默认 20.0） |
| 8 | `DI` | Integer | 否 | 生成间隔 $\Delta t_{spawn}$ [steps]（默认 50） |
| 9 | `RT` | Number | 否 | 松弛时间 $\tau$ [s]（默认 0.5） |

**输出：** Agent 对象 (`A`)

### 10.2 Destination (`End`) — 目的地设置

| 端口 | 名称 | 类型 | 必填 | 说明 |
|------|------|------|----------|-------------|
| 0 | `A` | Generic | **是** | Agent 对象列表 |
| 1 | `DP` | Point | **是** | 目的地坐标 |
| 2 | `SF` | Number | 否 | 目的地强度系数 $s_{dest}$（默认 1.0） |
| 3 | `R` | Number | 否 | 到达半径 $r_{dest}$ [m]（默认 1.0） |
| 4 | `RT` | Boolean | 否 | 添加返程代理（默认 false） |

**输出：** 更新后的 Agent 列表 (`A`)

### 10.3 Interest Point (`Interest`) — 兴趣点设置

| 端口 | 名称 | 类型 | 必填 | 说明 |
|------|------|------|----------|-------------|
| 0 | `A` | Generic | **是** | Agent 对象列表 |
| 1 | `POIs` | Point | **是** | 兴趣点坐标列表 |
| 2 | `SF` | Number | 否 | 兴趣点强度系数 $s_{interest}$（默认 1.0） |
| 3 | `R` | Number | 否 | 到达半径 $r_{interest}$ [m]（默认 1.0） |
| 4 | `D` | Integer | 否 | 停留时长 $T_{stay}$ [steps]（默认 5） |
| 5 | `LP` | Number | 否 | 离开概率 $p_{leave}$（默认 0.05） |

**输出：** 更新后的 Agent 列表 (`A`)

### 10.4 Obstacle — 障碍物定义

| 端口 | 名称 | 类型 | 必填 | 说明 |
|------|------|------|----------|-------------|
| 0 | `O` | Curve | **是** | 闭合多段线（逆时针绘制） |
| 1 | `R` | Number | 否 | 排斥强度 $A_j$ [N]（默认 500） |
| 2 | `D` | Number | 否 | 作用距离 $B_j$ [m]（默认 0.2） |

**输出：** Obstacle 对象 (`O`)

### 10.5 Simulation Settings — 仿真设置

| 端口 | 名称 | 类型 | 必填 | 说明 |
|------|------|------|----------|-------------|
| 0 | `SPI` | Integer | 否 | 每迭代步数（默认 5） |
| 1 | `TPS` | Number | 否 | 步长 $\Delta t$ [s]（默认 0.1） |
| 2 | `RS` | Integer | 否 | 恢复步数 $N_{recover}$（默认 25） |
| 3 | `MA` | Integer | 否 | 最大代理数 $N_{max}$（默认 20） |
| 4 | `MI` | Integer | 否 | 最大迭代步数（0=无限，默认 0） |
| 5 | `GT` | Number | 否 | 引导阈值 $\delta_{guide}$ [m]（默认 1.0） |
| 6 | `ID` | Integer | 否 | 惯性时长 $T_{inertia}$ [steps]（默认 15） |
| 7 | `DC` | Number | 否 | 阻尼系数 $\mu$（默认 0.5） |
| 8 | `NO` | Number | 否 | 导航偏移 $\delta_{nav}$ [m]（默认 0.5） |
| 9 | `ECR` | Number | 否 | 疏散完成率 $\eta_{evac}$（默认 0.95） |
| 10 | `WR` | Number | 否 | 漫游比例 $W_R$（仅 HybSim，默认 0.0） |

### 10.6 Pedestrian Simulator (`PedSim`) — 行人模拟器

| 端口 | 名称 | 类型 | 必填 | 说明 |
|------|------|------|----------|-------------|
| 0 | `A` | Generic | **是** | Agent 对象列表 |
| 1 | `O` | Generic | **是** | Obstacle 对象列表 |
| 2 | `R` | Boolean | 否 | 重置仿真（默认 false） |
| 3 | `SS` | Generic | 否 | 仿真设置对象 |
| 4 | `EA` | Curve | 否 | 疏散区域闭合曲线 |

| 输出 | 名称 | 类型 | 说明 |
|------|------|------|-------------|
| 0 | `A` | Generic | 更新后的 Agent 列表 |
| 1 | `T` | Point Tree | 代理轨迹点 |
| 2 | `S` | Integer | 当前仿真步数 |
| 3 | `C` | Boolean | 仿真完成标志 |
| 4 | `SP` | Curve Tree | 各生成点至目的地的最短路径 |
| 5 | `TT` | Number | 总仿真时间 [s] |
| 6 | `ET` | Text | 疏散时间 [s] 或状态信息 |

### 10.7 Explorer Simulator (`ExpSim`) — 探索模拟器

端口与输出同 Pedestrian Simulator，最短路径输出 `SP` 替换为 `TT`（总时间）。

### 10.8 Hybrid Simulator (`HybSim`) — 混合模拟器

端口同 Pedestrian Simulator。输出增加：

| 输出 | 名称 | 类型 | 说明 |
|------|------|------|-------------|
| 4 | `SP` | Curve Tree | 预计算的静态路径（仅路径跟随模式代理） |

### 10.9 Path Network (`Path`) — 独立路径网络

| 端口 | 名称 | 类型 | 必填 | 说明 |
|------|------|------|----------|-------------|
| 0 | `SP` | Point | **是** | 起点 |
| 1 | `POIs` | Point | 否 | 兴趣点列表（可选） |
| 2 | `DP` | Point | **是** | 终点 |
| 3 | `O` | Curve | 否 | 障碍物曲线列表（可选） |
| 4 | `NO` | Number | 否 | 导航偏移 $\delta_{nav}$ [m]（默认 0.5） |

| 输出 | 名称 | 类型 | 说明 |
|------|------|------|-------------|
| 0 | `N` | Curve | 路径网络几何（含可视边与偏移边界） |
| 1 | `SP` | Curve | 最短路径多段线 |
| 2 | `V` | Point | 路径顶点 |
| 3 | `SL` | Number | 各段长度 [m] |
| 4 | `TL` | Number | 总路径长度 [m] |

### 10.10 Crowd Density Visualization (`DensityViz`) — 密度可视化

| 端口 | 名称 | 类型 | 必填 | 说明 |
|------|------|------|----------|-------------|
| 0 | `P` | Point | **是** | 代理位置列表 |
| 1 | `O` | Point | 否 | 网格原点（默认 Origin） |
| 2 | `S` | Number | 否 | 网格间距 [m]（默认 2.0） |
| 3 | `X` | Integer | 否 | X 方向单元数（默认 10） |
| 4 | `Y` | Integer | 否 | Y 方向单元数（默认 10） |

| 输出 | 名称 | 类型 | 说明 |
|------|------|------|-------------|
| 0 | `M` | Mesh | 完整密度网格（Ladybug 热图配色） |
| 1 | `M'` | Mesh | 非空单元的密度网格 |
| 2 | `C` | Integer | 每单元代理数量 |
| 3 | `D` | Number | 每单元密度百分比 |
| 4 | `Col` | Colour | 每单元颜色 |

### 10.11 Deconstruct Agent Plus (`DeconAg+`) — 增强解构

| 输出组 | 端口 | 名称 | 说明 |
|--------|------|------|-------------|
| 几何运动 | 0 | `P` | 当前位置 |
| | 1 | `C` | 身体圆形（半径 $r_i$） |
| | 2 | `V` | 速度向量 |
| | 3 | `F` | 合力向量 |
| | 4 | `Tr` | 轨迹曲线 |
| | 5 | `MD` | 运动方向向量 |
| 状态信息 | 6 | `S` | 状态文本（MOVING/STAYING/LEAVING） |
| | 7 | `D` | 目的地坐标 |
| | 8 | `I` | 当前兴趣点 |
| | 9 | `IP` | 初始位置 |
| | 10 | `Sp` | 当前速度 [m/s] |
| | 11 | `SC` | 剩余停留时间 |
| | 12 | `AI` | 是否在兴趣点 |
| 物理属性 | 13 | `M` | 质量 [kg] |
| | 14 | `DS` | 期望速度 [m/s] |
| | 15 | `MS` | 最大速度 [m/s] |
| | 16 | `R` | 身体半径 [m] |
| | 17 | `RS` | 排斥强度 [N] |
| 感知参数 | 18 | `VA` | 视野角度 [rad] |
| | 19 | `VD` | 视野距离 [m] |
| | 20 | `VS` | 视野扇形曲线 |

---

## 11. 数值实现细节

### 11.1 时间与步进

| 参数 | 符号 | 默认值 | 范围 | 说明 |
|-----------|--------|---------|-------|-------------|
| 仿真步长 | $\Delta t$ | 0.1 | [0.001, 10] | 每步代表的实际时间 [s] |
| 每迭代步数 | $N_{steps}$ | 5 | [1, 1000] | 每次 Grasshopper 解算执行的仿真步数 |
| 最大迭代步数 | $N_{max}$ | 0 | [0, $\infty$] | 仿真总步数上限（0=无限） |
| 惯性时长 | $T_{inertia}$ | 15 | [0, 1000] | 到达路径点后维持惯性的步数 |
| 引导阈值 | $\delta_{guide}$ | 1.0 | [0.001, 100] | 判定到达路径点的距离阈值 [m] |

### 11.2 坐标系与投影

所有计算均在 XY 平面投影进行：
- 代理位置、障碍物、路径点均在求解前投影至 $Z=0$ 平面
- 速度向量仅使用 X、Y 分量

### 11.3 状态初始化

- 代理初始速度：$\mathbf{v}(0) = \mathbf{0}$
- 初始运动方向：$\mathbf{d}^{move}(0) = \mathbf{0}$
- 生成计时器：$t_{spawn} = \mathrm{rand}(0, \Delta t_{spawn})$（均匀随机初始化）

---

## 12. 参考文献

1. **Helbing, D. & Molnár, P.** (1995). Social force model for pedestrian dynamics. *Physical Review E*, 51(5), 4282-4286.

2. **Helbing, D., Farkas, I. & Vicsek, T.** (2000). Simulating dynamical features of escape panic. *Nature*, 407, 487-490.

3. **Hart, P.E., Nilsson, N.J. & Raphael, B.** (1968). A Formal Basis for the Heuristic Determination of Minimum Cost Paths. *IEEE Transactions on Systems Science and Cybernetics*, 4(2), 100-107.

4. **Lozano-Pérez, T. & Wesley, M.A.** (1979). An algorithm for planning collision-free paths among polyhedral obstacles. *Communications of the ACM*, 22(10), 560-570. https://doi.org/10.1145/359156.359164

---
