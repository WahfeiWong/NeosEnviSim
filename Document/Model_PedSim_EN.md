# Pedestrian Simulation Module (NeosExplorer)

## 1. Model Overview

The pedestrian simulation module implements micro-scale crowd simulation based on the **Helbing Social Force Model (SFM)**, combined with **Visibility Graph + A\* path planning** and an **autonomous exploration behavior model**. The module supports three behavioral modes: Path Following, Exploring, and Hybrid.

| Component | Method | Reference |
|-----------|--------|-----------|
| Crowd motion dynamics | Helbing Social Force Model | Helbing & Molnár (1995); Helbing, Farkas & Vicsek (2000) |
| Path planning | Visibility Graph + A\* algorithm | Lozano-Pérez & Wesley (1979); Hart, Nilsson & Raphael (1968) |
| Autonomous exploration | Inertia-perception guidance model | Local navigation based on vision cone |
| Hybrid behavior | Proportional dual-mode allocation | -- |

The module provides three core simulators and supporting components:

| Component | Short | Description |
|-----------|-------|-------------|
| **PedestrianSimulator** | `PedSim` | Path-following mode: SFM guided by global shortest path |
| **ExplorerSimulator** | `ExpSim` | Exploration mode: inertia-based and locally perceived exploration |
| **HybridSimulator** | `HybSim` | Hybrid mode: allocates path-following and exploration via `WanderRatio` |
| **AgentGenerator** | `Start` | Agent generator, defines pedestrian initial attributes |
| **Destination** | `End` | Destination setting, supports round trips |
| **InterestPoint** | `Interest` | Point-of-interest setting, controls dwell behavior |
| **Obstacle** | `Obstacle` | Obstacle definition with repulsion parameters |
| **PathNetwork** | `Path` | Standalone shortest path computation (visibility graph + A\*) |
| **DeconstructAgent** | `DeconAg` | Basic agent visualization (position, velocity, trajectory, view cone) |
| **DeconstructAgentPlus** | `DeconAg+` | Enhanced agent visualization (with physics properties, status info) |
| **CrowdDensityVisualization** | `DensityViz` | Crowd density grid visualization |
| **SimulationSettings** | `Settings` | Global simulation parameter configuration |

---

## 2. Social Force Model

### 2.1 Equation of Motion

Each agent $i$ follows Newton's second law:

$$m_i \frac{d\mathbf{v}_i}{dt} = \mathbf{f}_i^{drive} + \mathbf{f}_i^{obs} + \mathbf{f}_i^{soc} + \mathbf{f}_i^{damp}$$

Velocity update:

$$\mathbf{v}_i(t + \Delta t) = \mathbf{v}_i(t) + \frac{\mathbf{F}_i}{m_i} \Delta t$$

Position update:

$$\mathbf{x}_i(t + \Delta t) = \mathbf{x}_i(t) + \mathbf{v}_i(t + \Delta t) \Delta t$$

| Parameter | Symbol | Unit | Description |
|-----------|--------|------|-------------|
| Agent mass | $m_i$ | $\mathrm{kg}$ | Pedestrian mass (state variable) |
| Velocity | $\mathbf{v}_i$ | $\mathrm{m/s}$ | Current velocity vector (state variable) |
| Position | $\mathbf{x}_i$ | $\mathrm{m}$ | Current position vector (state variable) |
| Total force | $\mathbf{F}_i$ | $\mathrm{N}$ | Net force acting on the agent |
| Time step | $\Delta t$ | $\mathrm{s}$ | Simulation step size $= \mathrm{TPS}$ |

### 2.2 Driving Force

The driving force propels the agent toward its target, driving its velocity toward the desired speed:

$$\mathbf{f}_i^{drive} = \frac{m_i}{\tau_i} \left( \mathbf{v}_i^{desired} - \mathbf{v}_i \right) \cdot s_i$$

The desired velocity:

$$\mathbf{v}_i^{desired} = \mathbf{e}_i \cdot v_i^{des} \cdot s_i$$

The unit vector $\mathbf{e}_i$ depends on the operating mode:
- **Path-following mode**: $\mathbf{e}_i = \frac{\mathbf{x}_{target} - \mathbf{x}_i}{\|\mathbf{x}_{target} - \mathbf{x}_i\|}$, pointing to the current path waypoint
- **Inertia mode**: $\mathbf{e}_i = \mathbf{d}_i^{move}$, using the actual movement direction
- **Exploration mode (with guide point)**: $\mathbf{e}_i = 0.6 \mathbf{d}_i^{move} + 0.4 \frac{\mathbf{x}_{guide} - \mathbf{x}_i}{\|\mathbf{x}_{guide} - \mathbf{x}_i\|}$, blending inertia and guidance

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| Desired speed | $v_i^{des}$ | $\mathrm{m/s}$ | Pedestrian desired walking speed | 1.3 | [0.1, 5.0] |
| Relaxation time | $\tau_i$ | $\mathrm{s}$ | Characteristic time to adjust speed | 0.5 | [0.01, 10.0] |
| Strength factor | $s_i$ | -- | Waypoint attraction coefficient | 1.0 | [0.01, 10.0] |
| Movement direction | $\mathbf{d}_i^{move}$ | -- | Normalized actual movement direction | -- | -- |

### 2.3 Obstacle Repulsion Force

Obstacles exert an exponentially decaying repulsive force on agents:

$$\mathbf{f}_{ij}^{obs} = A_j \cdot \exp\left( -\frac{d_{ij}^{eff}}{B_j} \right) \cdot \mathbf{n}_{ij}$$

Effective distance:

$$d_{ij}^{eff} = \max\left( d_{ij} - r_i, \; 0.001 \right)$$

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| Obstacle repulsion strength | $A_j$ | $\mathrm{N}$ | Maximum repulsive force from obstacle | 500 | [0, 10000] |
| Obstacle effect distance | $B_j$ | $\mathrm{m}$ | Effective range of repulsion | 0.2 | [0.01, 10.0] |
| Agent body radius | $r_i$ | $\mathrm{m}$ | Pedestrian body cylinder radius | 0.3 | [0.05, 2.0] |
| Agent-obstacle distance | $d_{ij}$ | $\mathrm{m}$ | Distance from agent center to obstacle nearest point | -- | -- |
| Unit direction | $\mathbf{n}_{ij}$ | -- | Unit vector from obstacle to agent | -- | -- |

> **Note:** Obstacle curves must be closed polylines drawn counterclockwise. The repulsive force acts along the normal direction of the obstacle offset curve.

### 2.4 Social Force (Pedestrian Interaction)

Inter-pedestrian repulsion is divided into three intensity tiers, with detection range $10 \times d_{min}$:

$$d_{min} = r_i + r_j$$

$$d_{eff} = d_{ij} - d_{min}$$

**Tier 1 — Strong repulsion (body overlap, $d_{eff} < 0$):**

$$\mathbf{f}_{ij}^{soc,strong} = \frac{A_i + A_j}{2} \cdot \exp\left( \frac{-d_{eff}}{0.1} \right) \cdot \mathbf{n}_{ij}$$

**Tier 2 — Medium repulsion (close range, $d_{min} \leq d_{ij} < 2d_{min}$):**

$$\mathbf{f}_{ij}^{soc,med} = \frac{A_i + A_j}{2} \cdot \exp\left( -\frac{d_{ij}}{d_{min}} \right) \cdot \mathbf{n}_{ij}$$

**Tier 3 — Weak repulsion (social distance, $2d_{min} \leq d_{ij} < 10d_{min}$):**

$$\mathbf{f}_{ij}^{soc,weak} = A_i \cdot \exp\left( -\frac{d_{ij}}{2d_{min}} \right) \cdot \mathbf{n}_{ij}$$

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| Agent repulsion strength | $A_i$ | $\mathrm{N}$ | Social repulsion strength of agent $i$ | 5.0 | [0.1, 100.0] |
| Inter-agent distance | $d_{ij}$ | $\mathrm{m}$ | Euclidean distance between agents $i$ and $j$ | -- | -- |
| Minimum body distance | $d_{min}$ | $\mathrm{m}$ | Sum of two agents' body radii | -- | -- |
| Effective spacing | $d_{eff}$ | $\mathrm{m}$ | Distance outside body cylinders (negative = overlap) | -- | -- |

> **Physical interpretation:** The exponential denominator $0.1\,\mathrm{m}$ is the overlap decay characteristic length, reflecting the short-range repulsion of body elastic compression.

### 2.5 Damping Force

Models environmental friction and energy dissipation:

$$\mathbf{f}_i^{damp} = -\mu \cdot \mathbf{v}_i$$

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| Damping coefficient | $\mu$ | $\mathrm{N \cdot s/m}$ or $\mathrm{kg/s}$ | Velocity attenuation coefficient | 0.5 | [0, 1.0] |

### 2.6 Speed Limitation

Agent velocity is clamped to the maximum speed:

$$\|\mathbf{v}_i\| = \min\left( \|\mathbf{v}_i\|, \; v_i^{max} \right)$$

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| Maximum speed | $v_i^{max}$ | $\mathrm{m/s}$ | Upper bound on agent speed | 2.5 | [0.5, 10.0] |

---

## 3. Path Planning Model (Visibility Graph + A\*)

### 3.1 Algorithm Pipeline

Path-following mode uses the visibility graph method combined with the A\* algorithm to compute a global shortest path from start to destination via waypoints:

1. **Obstacle offset**: Offset obstacle contours outward by $\delta_{nav}$ to generate a navigation safety boundary
2. **Vertex collection**: Gather start, waypoints, destination, and all offset obstacle vertices
3. **Visibility graph construction**: Connect all mutually visible vertex pairs (lines that do not intersect obstacle offset curves internally)
4. **A\* search**: Search for shortest path using Euclidean heuristic $h(\mathbf{x}) = \|\mathbf{x} - \mathbf{x}_{goal}\|$

### 3.2 Path Segmentation

When multiple points of interest exist, the waypoint connection order depends on the POI visit mode:

**Auto mode (`UseIndexOrder = false`, default)**: POIs are sorted by ascending straight-line distance from the start point:

$$\mathrm{waypoints} = [\mathbf{x}_{start}, \; \mathrm{sort}(\mathbf{x}_{POI}, \|\cdot - \mathbf{x}_{start}\|), \; \mathbf{x}_{end}]$$

**Index-order mode (`UseIndexOrder = true`)**: POIs are visited strictly in the original input list order:

$$\mathrm{waypoints} = [\mathbf{x}_{start}, \; \mathbf{x}_{POI}^{(0)}, \; \mathbf{x}_{POI}^{(1)}, \; \dots, \; \mathbf{x}_{end}]$$

Each segment is independently solved via A\*, with duplicate points at junctions removed. Index-order mode also supports return-trip order control: when `KeepReturnOrder = false` (default), the return trip visits POIs in reverse order; when `KeepReturnOrder = true`, the return trip follows the same order as the forward trip.

### 3.3 Visibility Test

Connection validity is determined by dual testing:
- **Curve intersection test**: Using Rhino `CurveCurve` intersection, excluding endpoint intersections
- **Sampling test**: Sampling at $0.25,\, 0.5,\, 0.75$ along the connection to check if any sample point lies inside an obstacle

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| Navigation offset | $\delta_{nav}$ | $\mathrm{m}$ | Obstacle safety boundary offset distance | 0.5 | [0, 5.0] |

---

## 4. Autonomous Exploration Behavior Model

### 4.1 Behavior Overview

In exploration mode, agents navigate in real time based on local perception without relying on precomputed paths. Core mechanisms include:
- **Guide point system**: Detects obstacle vertices within the vision cone as navigation guide points
- **Inertia effect**: After reaching a guide point, inertia is activated to maintain the original movement direction for several steps
- **Dynamic obstacle avoidance**: Detects obstacles ahead in real time and switches to guide-point navigation

### 4.2 Guide Point Detection

Finds the nearest obstacle vertex within the agent's vision cone:

$$\theta = \arccos\left( \mathbf{d}_i^{move} \cdot \frac{\mathbf{x}_{vertex} - \mathbf{x}_i}{\|\mathbf{x}_{vertex} - \mathbf{x}_i\|} \right) \leq \frac{\alpha_i}{2}$$

$$d_{vertex} \leq D_i^{view}, \quad d_{vertex} \geq 0.5 r_i$$

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| View angle | $\alpha_i$ | $\mathrm{rad}$ | Agent vision cone full angle | $\pi$ (180°) | [0, $2\pi$] |
| View distance | $D_i^{view}$ | $\mathrm{m}$ | Maximum visible distance | 20.0 | [0.1, 100.0] |

### 4.3 Inertia-Guidance Velocity Composition

When inertia is active, the desired velocity is a weighted blend of inertia and guidance directions:

$$\mathbf{v}^{desired} = 0.6 \cdot \mathbf{v}^{inertia} + 0.4 \cdot \mathbf{v}^{guide}$$

where:

$$\mathbf{v}^{inertia} = \mathbf{d}_i^{move} \cdot v_i^{des} \cdot s_i$$

$$\mathbf{v}^{guide} = \frac{\mathbf{x}_{guide} - \mathbf{x}_i}{\|\mathbf{x}_{guide} - \mathbf{x}_i\|} \cdot v_i^{des} \cdot s_i$$

> **Weight rationale:** The 0.6/0.4 inertia/guidance weight ensures motion continuity and prevents agents from jittering between guide points.

### 4.4 Obstacle Collision Detection

Detects whether the line from the agent to its target intersects any obstacle offset curve (excluding endpoints):

$$\mathrm{blocked} = \mathrm{Intersection}(\overline{\mathbf{x}_i \mathbf{x}_{target}}, \; \mathrm{Offset}(\mathrm{Obstacle}, r_i)) \neq \emptyset$$

When blocked, guide-point navigation is activated; when clear, the guide point is cleared and the agent proceeds directly to the target.

---

## 5. Hybrid Mode

### 5.1 Mode Allocation

Behavioral mode allocation is controlled by the `WanderRatio` parameter:

$$\mathrm{mode}_i = \begin{cases} \text{PathFollowing} & \text{if } \xi_i \geq W_R \\[8pt] \text{Exploring} & \text{if } \xi_i < W_R \end{cases}$$

where $\xi_i \sim U(0,1)$ is a uniform random number assigned at agent spawn.

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| Wander ratio | $W_R$ | -- | Fraction of agents in exploration mode | 0.0 | [0, 1.0] |

- $W_R = 0$: All agents use path-following mode
- $W_R = 1$: All agents use exploration mode
- $0 < W_R < 1$: Randomly allocated by proportion

---

## 6. Points of Interest and Dwell Behavior

### 6.1 POI Visit Mechanism

POI visits support two modes, controlled by the `UseIndexOrder` parameter:

**Auto mode (`UseIndexOrder = false`, default)**: POIs are sorted by ascending straight-line distance from the start point, using a greedy nearest-neighbor strategy:

$$\mathrm{POI}_{sorted} = \mathrm{sort}(\{\mathbf{x}_{POI}\}, \; \|\cdot - \mathbf{x}_{start}\|)$$

**Index-order mode (`UseIndexOrder = true`)**: POIs are visited strictly in the original input list order without re-sorting.

When an agent reaches a POI (distance $< r_{interest}$), it enters a dwell state:
- Dwell counter initialized to `StayDuration`
- Each step attempts to leave with probability $p_{leave}$
- If the dwell counter reaches zero, the agent is forced to leave

#### Return-Trip Order Control

When index-order mode is enabled, the `KeepReturnOrder` parameter controls the return-trip POI visit order (only effective when `Add Return Trip = true`):

| Parameter | Default | Forward Visit | Return Visit |
|-----------|---------|---------------|--------------|
| `KeepReturnOrder = false` | default | 0 → 1 → 2 | 2 → 1 → 0 (reversed) |
| `KeepReturnOrder = true` | | 0 → 1 → 2 | 0 → 1 → 2 (same as forward) |

### 6.2 Dwell Parameters

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| POI strength | $s_{interest}$ | -- | Attraction coefficient of POI | 1.0 | [0.01, 10.0] |
| POI radius | $r_{interest}$ | $\mathrm{m}$ | Distance threshold for POI arrival | 1.0 | [0.01, 10.0] |
| Stay duration | $T_{stay}$ | steps | Maximum dwell time at POI | 5 | [0, 1000] |
| Leave probability | $p_{leave}$ | -- | Per-step probability of leaving | 0.05 | [0, 1.0] |
| Destination strength | $s_{dest}$ | -- | Attraction coefficient of destination | 1.0 | [0.01, 10.0] |
| Destination radius | $r_{dest}$ | $\mathrm{m}$ | Distance threshold for destination arrival | 1.0 | [0.01, 10.0] |

> **Note:** `GetCurrentStrengthFactor()` returns the POI strength when the agent has unvisited POIs; otherwise returns the destination strength.

### 6.3 Visit Order Control Parameters

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| Use Index Order | `IO` | -- | `false`=auto sort by distance, `true`=by input index order | false | {false, true} |
| Keep Return Order | `KRO` | -- | `false`=reverse on return (default), `true`=same as forward; only effective when `IO = true` | false | {false, true} |

---

## 7. Stuck Detection and Recovery

### 7.1 Detection Condition

An agent is marked as stuck when its displacement over $T_{stay} + 10$ steps is below a threshold:

$$\|\mathbf{x}_i(t) - \mathbf{x}_i(t - T_{stay} - 10)\| < \epsilon_{stuck}$$

| Mode | Threshold $\epsilon_{stuck}$ | Description |
|------|:---:|:---|
| Path Following | $0.1\,\mathrm{m}$ | Fixed threshold |
| Exploring | $0.5 \times r_i$ | Proportional to agent radius |

### 7.2 Recovery Mechanism

Recovery procedure for path-following mode:
1. Find the nearest path point within the vision cone as `RecoveryTarget`
2. Drive the agent toward the recovery point with destination strength
3. Upon reaching the recovery point, the stuck state is released and normal path following resumes
4. If the recovery point is not reached within `RecoverySteps` steps, the stuck state is forcefully released

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| Recovery steps | $N_{recover}$ | steps | Maximum recovery attempt steps | 25 | [0, 1000] |

Recovery for exploration mode:
- `IsStuck = true` triggers guide point re-search
- Automatically released when sufficient displacement is detected

---

## 8. Spawn and Scheduling System

### 8.1 Agent Spawning

Each spawn point produces agents at fixed intervals:

$$t_{spawn}^{next} = t_{spawn}^{prev} + \Delta t_{spawn}$$

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| Spawn interval | $\Delta t_{spawn}$ | steps | Interval between consecutive spawns from the same point | 50 | [0, 10000] |
| Max agents | $N_{max}$ | -- | Maximum number of simultaneously active agents | 20 | [1, 10000] |

### 8.2 Generator Parameters

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| Agent mass | $m$ | $\mathrm{kg}$ | Pedestrian mass | 65.0 | [1.0, 500.0] |
| Desired speed | $v^{des}$ | $\mathrm{m/s}$ | Normal walking desired speed | 1.3 | [0.1, 5.0] |
| Maximum speed | $v^{max}$ | $\mathrm{m/s}$ | Speed upper bound | 2.5 | [0.5, 10.0] |
| Body radius | $r$ | $\mathrm{m}$ | Pedestrian body cylinder radius | 0.3 | [0.05, 2.0] |
| Repulsion strength | $A$ | $\mathrm{N}$ | Social repulsion strength | 5.0 | [0.1, 100.0] |
| View angle | $\alpha$ | $\mathrm{rad}$ | Vision cone full angle | $\pi$ | [0, $2\pi$] |
| View distance | $D^{view}$ | $\mathrm{m}$ | Maximum visible distance | 20.0 | [0.1, 100.0] |
| Relaxation time | $\tau$ | $\mathrm{s}$ | Driving force response time constant | 0.5 | [0.01, 10.0] |

---

## 9. Evacuation Analysis

### 9.1 Evacuation Criterion

Evacuation areas are defined by closed polylines. When the number of agents inside the evacuation area falls below the target value, the evacuation time is recorded:

$$N_{remain}(t) \leq N_{target} = \lceil N_{initial} \cdot (1 - \eta_{evac}) \rceil$$

$$t_{evac} = n_{step} \cdot \Delta t$$

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| Evacuation ratio | $\eta_{evac}$ | -- | Required fraction of evacuated agents | 0.95 | [0, 1.0] |
| Evacuation time | $t_{evac}$ | $\mathrm{s}$ | Time to reach evacuation ratio | -- | -- |

---

## 10. Input/Output Ports

### 10.1 Agent Generator (`Start`) — Agent Generation

| Port | Name | Type | Required | Description |
|------|------|------|----------|-------------|
| 0 | `SP` | Point | **Yes** | Starting position (spawn point coordinate) |
| 1 | `M` | Number | No | Mass $m$ [kg] (default 65.0) |
| 2 | `Vd` | Number | No | Desired speed $v^{des}$ [m/s] (default 1.3) |
| 3 | `Vmax` | Number | No | Maximum speed $v^{max}$ [m/s] (default 2.5) |
| 4 | `R` | Number | No | Body radius $r$ [m] (default 0.3) |
| 5 | `RS` | Number | No | Repulsion strength $A$ [N] (default 5.0) |
| 6 | `VA` | Number | No | View angle $\alpha$ [rad] (default $\pi$) |
| 7 | `VD` | Number | No | View distance $D^{view}$ [m] (default 20.0) |
| 8 | `DI` | Integer | No | Spawn interval $\Delta t_{spawn}$ [steps] (default 50) |
| 9 | `RT` | Number | No | Relaxation time $\tau$ [s] (default 0.5) |

**Output:** Agent object (`A`)

### 10.2 Destination (`End`) — Destination Setting

| Port | Name | Type | Required | Description |
|------|------|------|----------|-------------|
| 0 | `A` | Generic | **Yes** | Agent object list |
| 1 | `DP` | Point | **Yes** | Destination coordinate |
| 2 | `SF` | Number | No | Destination strength coefficient $s_{dest}$ (default 1.0) |
| 3 | `R` | Number | No | Arrival radius $r_{dest}$ [m] (default 1.0) |
| 4 | `RT` | Boolean | No | Add return-trip agents (default false) |

**Output:** Updated Agent list (`A`)

### 10.3 Interest Point (`Interest`) — Point of Interest Setting

| Port | Name | Type | Required | Description |
|------|------|------|----------|-------------|
| 0 | `A` | Generic | **Yes** | Agent object list |
| 1 | `POIs` | Point | **Yes** | List of POI coordinates |
| 2 | `SF` | Number | No | POI strength coefficient $s_{interest}$ (default 1.0) |
| 3 | `R` | Number | No | Arrival radius $r_{interest}$ [m] (default 1.0) |
| 4 | `D` | Integer | No | Stay duration $T_{stay}$ [steps] (default 5) |
| 5 | `LP` | Number | No | Leave probability $p_{leave}$ (default 0.05) |
| 6 | `IO` | Boolean | No | Use index order: `false`=auto sort by distance (default), `true`=by input index order |
| 7 | `KRO` | Boolean | No | Keep return order: `false`=reverse on return (default), `true`=same as forward; only effective when `IO = true` |

**Output:** Updated Agent list (`A`)

### 10.4 Obstacle — Obstacle Definition

| Port | Name | Type | Required | Description |
|------|------|------|----------|-------------|
| 0 | `O` | Curve | **Yes** | Closed polyline (counterclockwise) |
| 1 | `R` | Number | No | Repulsion strength $A_j$ [N] (default 500) |
| 2 | `D` | Number | No | Effect distance $B_j$ [m] (default 0.2) |

**Output:** Obstacle object (`O`)

### 10.5 Simulation Settings — Simulation Configuration

| Port | Name | Type | Required | Description |
|------|------|------|----------|-------------|
| 0 | `SPI` | Integer | No | Steps per iteration (default 5) |
| 1 | `TPS` | Number | No | Time step $\Delta t$ [s] (default 0.1) |
| 2 | `RS` | Integer | No | Recovery steps $N_{recover}$ (default 25) |
| 3 | `MA` | Integer | No | Maximum agents $N_{max}$ (default 20) |
| 4 | `MI` | Integer | No | Maximum iterations (0=unlimited, default 0) |
| 5 | `GT` | Number | No | Guide threshold $\delta_{guide}$ [m] (default 1.0) |
| 6 | `ID` | Integer | No | Inertia duration $T_{inertia}$ [steps] (default 15) |
| 7 | `DC` | Number | No | Damping coefficient $\mu$ (default 0.5) |
| 8 | `NO` | Number | No | Navigation offset $\delta_{nav}$ [m] (default 0.5) |
| 9 | `ECR` | Number | No | Evacuation completion ratio $\eta_{evac}$ (default 0.95) |
| 10 | `WR` | Number | No | Wander ratio $W_R$ (HybSim only, default 0.0) |

### 10.6 Pedestrian Simulator (`PedSim`) — Pedestrian Simulator

| Port | Name | Type | Required | Description |
|------|------|------|----------|-------------|
| 0 | `A` | Generic | **Yes** | Agent object list |
| 1 | `O` | Generic | **Yes** | Obstacle object list |
| 2 | `R` | Boolean | No | Reset simulation (default false) |
| 3 | `SS` | Generic | No | Simulation settings object |
| 4 | `EA` | Curve | No | Evacuation area closed curves |

| Output | Name | Type | Description |
|--------|------|------|-------------|
| 0 | `A` | Generic | Updated Agent list |
| 1 | `T` | Point Tree | Agent trajectory points |
| 2 | `S` | Integer | Current simulation step |
| 3 | `C` | Boolean | Simulation completion flag |
| 4 | `SP` | Curve Tree | Shortest paths from spawn points to destination |
| 5 | `TT` | Number | Total simulation time [s] |
| 6 | `ET` | Text | Evacuation time [s] or status info |

### 10.7 Explorer Simulator (`ExpSim`) — Exploration Simulator

Ports and outputs same as Pedestrian Simulator, with shortest path output `SP` replaced by `TT` (total time).

### 10.8 Hybrid Simulator (`HybSim`) — Hybrid Simulator

Ports same as Pedestrian Simulator. Additional outputs:

| Output | Name | Type | Description |
|--------|------|------|-------------|
| 4 | `SP` | Curve Tree | Pre-calculated static paths (path-following mode agents only) |

### 10.9 Path Network (`Path`) — Standalone Path Network

| Port | Name | Type | Required | Description |
|------|------|------|----------|-------------|
| 0 | `SP` | Point | **Yes** | Starting position |
| 1 | `POIs` | Point | No | List of POIs (optional) |
| 2 | `DP` | Point | **Yes** | Destination |
| 3 | `O` | Curve | No | Obstacle curve list (optional) |
| 4 | `NO` | Number | No | Navigation offset $\delta_{nav}$ [m] (default 0.5) |
| 5 | `IO` | Boolean | No | Use index order: `false`=auto sort by distance (default), `true`=by input index order |

| Output | Name | Type | Description |
|--------|------|------|-------------|
| 0 | `N` | Curve | Path network geometry (visible edges + offset boundaries) |
| 1 | `SP` | Curve | Shortest path polyline |
| 2 | `V` | Point | Path vertices |
| 3 | `SL` | Number | Segment lengths [m] |
| 4 | `TL` | Number | Total path length [m] |

### 10.10 Crowd Density Visualization (`DensityViz`) — Density Visualization

| Port | Name | Type | Required | Description |
|------|------|------|----------|-------------|
| 0 | `P` | Point | **Yes** | Agent position list |
| 1 | `O` | Point | No | Grid origin (default Origin) |
| 2 | `S` | Number | No | Grid cell spacing [m] (default 2.0) |
| 3 | `X` | Integer | No | Number of cells in X (default 10) |
| 4 | `Y` | Integer | No | Number of cells in Y (default 10) |

| Output | Name | Type | Description |
|--------|------|------|-------------|
| 0 | `M` | Mesh | Full density mesh (Ladybug heatmap colors) |
| 1 | `M'` | Mesh | Non-empty cell density mesh |
| 2 | `C` | Integer | Agent count per cell |
| 3 | `D` | Number | Density percentage per cell |
| 4 | `Col` | Colour | Color per cell |

### 10.11 Deconstruct Agent Plus (`DeconAg+`) — Enhanced Deconstruction

| Output Group | Port | Name | Description |
|--------------|------|------|-------------|
| Geometry & Motion | 0 | `P` | Current position |
| | 1 | `C` | Body circle (radius $r_i$) |
| | 2 | `V` | Velocity vector |
| | 3 | `F` | Total force vector |
| | 4 | `Tr` | Trajectory curve |
| | 5 | `MD` | Movement direction vector |
| Status Info | 6 | `S` | Status text (MOVING/STAYING/LEAVING) |
| | 7 | `D` | Destination coordinate |
| | 8 | `I` | Current interest point |
| | 9 | `IP` | Initial position |
| | 10 | `Sp` | Current speed [m/s] |
| | 11 | `SC` | Remaining stay time |
| | 12 | `AI` | At interest point flag |
| Physical Properties | 13 | `M` | Mass [kg] |
| | 14 | `DS` | Desired speed [m/s] |
| | 15 | `MS` | Maximum speed [m/s] |
| | 16 | `R` | Body radius [m] |
| | 17 | `RS` | Repulsion strength [N] |
| Perception | 18 | `VA` | View angle [rad] |
| | 19 | `VD` | View distance [m] |
| | 20 | `VS` | View sector curve |

---

## 11. Numerical Implementation Details

### 11.1 Time and Stepping

| Parameter | Symbol | Default | Range | Description |
|-----------|--------|---------|-------|-------------|
| Time step | $\Delta t$ | 0.1 | [0.001, 10] | Actual time represented per step [s] |
| Steps per iteration | $N_{steps}$ | 5 | [1, 1000] | Simulation steps executed per Grasshopper solver pass |
| Max iterations | $N_{max}$ | 0 | [0, $\infty$] | Total step limit (0 = unlimited) |
| Inertia duration | $T_{inertia}$ | 15 | [0, 1000] | Steps to maintain inertia after reaching a waypoint |
| Guide threshold | $\delta_{guide}$ | 1.0 | [0.001, 100] | Distance threshold for waypoint arrival [m] |

### 11.2 Coordinate System and Projection

All computations are performed on the XY plane:
- Agent positions, obstacles, and path points are projected to $Z=0$ before solving
- Velocity vectors use only X and Y components

### 11.3 State Initialization

- Initial velocity: $\mathbf{v}(0) = \mathbf{0}$
- Initial movement direction: $\mathbf{d}^{move}(0) = \mathbf{0}$
- Spawn timer: $t_{spawn} = \mathrm{rand}(0, \Delta t_{spawn})$ (uniform random initialization)

---

## 12. References

1. **Helbing, D. & Molnár, P.** (1995). Social force model for pedestrian dynamics. *Physical Review E*, 51(5), 4282-4286.

2. **Helbing, D., Farkas, I. & Vicsek, T.** (2000). Simulating dynamical features of escape panic. *Nature*, 407, 487-490.

3. **Hart, P.E., Nilsson, N.J. & Raphael, B.** (1968). A Formal Basis for the Heuristic Determination of Minimum Cost Paths. *IEEE Transactions on Systems Science and Cybernetics*, 4(2), 100-107.

4. **Lozano-Pérez, T. & Wesley, M.A.** (1979). An algorithm for planning collision-free paths among polyhedral obstacles. *Communications of the ACM*, 22(10), 560-570. https://doi.org/10.1145/359156.359164

---
