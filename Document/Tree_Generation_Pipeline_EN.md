# Parametric Tree Generation and Geometry Processing Pipeline

## 1. Recursive Branch Generation (Tree Generator)

The tree generator constructs the tree skeleton based on a recursive fractal principle. Its core is a depth-first recursive branching algorithm. Input parameters include: trunk starting point $S$, trunk height $H$, branch spread coefficient $R$ (controls $\tan\theta$), maximum recursion depth $D$, maximum number of child branches per node $B$, division probability graph $DG$ (branching probability at each depth), length decay graph $LG$ (length multiplier at each depth), trunk radius $TR$, radius decay factor $RD$, pipe segment count $PS$, and random seed $Sd$.

### 1.1 Recursive Branching Algorithm

The algorithm starts from a single trunk segment $L_0$ (from $S$ to $S + (0,0,H)$) and derives child branches layer by layer through the recursive function `RecursiveBranch`. At recursion level $d$, the algorithm first determines whether to continue branching with probability $DG[d]$; if aborted, the current branch is recorded as a terminal leaf branch. The branch length correction factor is:

$$\lambda_d = LG[d] \cdot \lambda_{d-1}$$

where $\lambda_0 = 1.0$ is the initial correction factor. The generation of child branches follows the following geometric construction process:

1. **Parent branch direction vector**: Obtain the direction vector $\vec{b}$ from the parent branch start point to the end point.
2. **Rotation radius**: At the parent branch end point, construct a circle with radius $r = R \cdot |\vec{b}|$.
3. **Rotation plane**: Use the parent branch direction $\vec{b}$ as the normal, passing through the end point $P_{\text{end}}$ to construct the plane $\Pi$.
4. **Base point on the circle**: On the plane $\Pi$, take the point $P_{\text{base}}$ closest to the tree root direction.
5. **Child branch direction**: The child branch extends along the direction from $P_{\text{end}}$ to $P_{\text{base}} - \lambda_d \cdot \vec{b}$.
6. **Circumferential uniform rotation**: The actual number of child branches is $n_d = \max(1, \text{round}(B \cdot DG[d]))$, with each child branch uniformly rotated around the parent branch axis by $2\pi/n_d$.

### 1.2 Branch Meshing

Each branch segment is converted into a cylindrical mesh via the `Mesh.CreateFromCurvePipe` method. The radius of the branch at level $d$ is:

$$r_d = TR \cdot RD^d$$

with a lower bound truncation $r_d \geq 0.001$ to avoid numerical issues. The cylinder segment count is controlled by parameter $PS$, and all branch meshes are merged into a single tree mesh `treeMesh`.

---

## 2. Leaf Cluster Surface Distribution (Leaf Distributor)

The leaf distributor component receives the tree mesh (`TM`) and branch line list (`BL`) output from the tree generator, and distributes the input leaf cluster mesh model onto the canopy region via a convex hull surface distribution algorithm.

### 2.1 Trunk Identification and Canopy Point Set Construction

First, all branch lines are traversed, and the trunk is identified by the criterion of the lowest midpoint $Z$ coordinate:

$$\text{trunk} = \arg\min_i \frac{Z_{\text{start}}^{(i)} + Z_{\text{end}}^{(i)}}{2}$$

After excluding the trunk, for each remaining branch line (i.e., branches), the start point $\mathbf{p}_s$, end point $\mathbf{p}_e$, and midpoint $\mathbf{p}_m = (\mathbf{p}_s + \mathbf{p}_e)/2$ are extracted to form the canopy point set $\mathcal{P}$. This point set contains $3(N-1)$ 3D points ($N$ is the total number of branches), uniformly covering the spatial extent of the canopy skeleton.

### 2.2 QuickHull 3D Construction

A 3D convex hull mesh is constructed from the canopy point set $\mathcal{P}$ using the QuickHull 3D incremental algorithm:

**Step 1 — Initial Tetrahedron**: Take the extreme points $\mathbf{p}_{\min X}$ and $\mathbf{p}_{\max X}$ along the $X$ axis, find the point farthest from the line segment $\mathbf{p}_2$ to form the base plane, then find the point farthest from that plane $\mathbf{p}_3$, forming a non-degenerate initial tetrahedron.

**Step 2 — Interior Reference Point**: Compute the centroid of the tetrahedron as the interior reference point $\mathbf{p}_{\text{int}}$, used to ensure all face normals point outward.

**Step 3 — Face-Point Assignment**: For non-tetrahedron vertices, determine which face's positive (outer) side they lie on, and add them to that face's `Outside` set.

**Step 4 — Incremental Expansion**: Maintain a queue of faces to be processed. For each face, take the farthest point $\mathbf{p}_f$ from its `Outside` set, and find all faces visible from $\mathbf{p}_f$ to form the visible set $\mathcal{V}$. The boundary edges between visible and non-visible faces form the horizon edges. New faces are constructed from $\mathbf{p}_f$ to each horizon edge, with outward-facing normals verified against $\mathbf{p}_{\text{int}}$, and orphan points are reassigned to the new faces. Iterate until all `Outside` sets are empty.

**Step 5 — Mesh Output**: Triangulate the hull faces and output `hullMesh`.

### 2.3 Area-Weighted Surface Point Distribution

Generate $N_c$ distributed points on the convex hull surface using an area-weighted random sampling strategy:

1. Compute the area of each triangular face and build a cumulative area array $A_{\text{cum}}$.
2. Select a triangle face with area-weighted probability $P_i = A_i / A_{\text{total}}$.
3. Generate uniformly random barycentric coordinates within the selected triangle: let $u, v \sim U(0,1)$, if $u+v > 1$ then reflect to $(1-u, 1-v)$, let $w = 1-u-v$, the distributed point is $\mathbf{p} = u\mathbf{a} + v\mathbf{b} + w\mathbf{c}$.

### 2.4 Orient Transformation

Using the leaf cluster mesh bounding box center $\mathbf{c}_{\text{leaf}}$ as the source reference point, a composite affine transformation is constructed for each distribution target point $\mathbf{p}_t$:

$$\mathbf{T} = \mathbf{T}_{\text{translate}}(\mathbf{p}_t - \mathbf{c}_{\text{leaf}}) \cdot \mathbf{R}_z(\theta_z) \cdot \mathbf{R}_y(\theta_y) \cdot \mathbf{R}_x(\theta_x) \cdot \mathbf{S}(\mathbf{c}_{\text{leaf}}, s)$$

where the scale factor $s$ is user-specified via `SF` (when $SF \geq 0$), or randomly selected within the interval $[\text{SR}_{\min}, \text{SR}_{\max}]$ (when $SF < 0$). The transformed leaf cluster meshes are finally merged into the tree mesh output.

---

## 3. Tree Geometry Parameterization Processing (Tree Geometry Processor)

The tree geometry processing component scales the parametrically generated tree model to user-specified physical dimensions and calculates canopy geometric characteristic parameters.

### 3.1 Non-Uniform Scaling

Receives the complete tree mesh with leaf clusters (`TM`), planting point (`P`), target tree height (`H`), and target crown radius (`R`), and computes the original bounding box dimensions:

$$W_{\text{orig}},\; D_{\text{orig}},\; H_{\text{orig}}$$

Construct a non-uniform scaling transformation to fit the original tree to the target dimensions:

$$\mathbf{T}_{\text{scale}} = \text{Scale}\big(\mathbf{c}_{\text{bbox}}, \; \frac{2R}{W_{\text{orig}}}, \; \frac{2R}{D_{\text{orig}}}, \; \frac{H}{H_{\text{orig}}}\big)$$

After scaling, align the bottom center of the tree to the planting point $\mathbf{P}$ via a translation transformation:

$$\mathbf{T}_{\text{move}} = \text{Translate}\big(\mathbf{P} - \mathbf{b}_{\text{center}}\big)$$

### 3.2 Simplified Canopy Model

To accurately compute projection and volume, a simplified canopy model is generated from the scaled complete tree mesh, providing two strategies:

- **ShrinkWrap** (default): Wraps the complex tree geometry into a closed approximate envelope mesh via `Mesh.ShrinkWrap`, with the target edge length parameter `Edge Length` ($L$) controlling mesh resolution.
- **Convex Hull** (fallback): When ShrinkWrap is unavailable, collects all mesh vertices and calls `Mesh.CreateConvexHull3D` to construct a convex hull mesh.

Canopy volume is computed via `VolumeMassProperties.Compute`:

$$V_{\text{crown}} = \text{Volume}(\text{TreeCan})$$

### 3.3 Projection Area Calculation (Based on Simplified Canopy Model)

The canopy projection calculation is based on the simplified canopy model (rather than the original detailed model), avoiding area overestimation caused by face overlap:

1. Project the simplified canopy mesh onto the XY plane (set all vertex $Z$ coordinates to zero).
2. Extract the 2D silhouette outline from the flattened mesh.
3. Construct a planar Brep from the closed outline (automatically handling inner and outer loops).
4. Precisely compute the projection area $A_{\text{proj}}$ via `Brep.GetArea()`.

The projection mesh (`Projection Mesh`) is a 2D region mesh on the XY plane, directly usable for shadow calculation and CFD pre-processing.

### 3.4 Canopy Parameter Calculation

- **Leaf Area (LA)**: Total surface area of the scaled complete tree mesh:

$$LA = \sum_{i} \text{Area}(\text{mesh}_i)$$

- **Leaf Area Index (LAI)**: Total leaf area per unit projected area:

$$LAI = \frac{LA}{A_{\text{proj}}}$$

- **Leaf Area Density (LAD)**: Total leaf area per unit canopy volume:

$$LAD = \frac{LA}{V_{\text{crown}}}$$

### 3.5 Output Data

| Output Port | Symbol | Description |
|:---:|:---:|:---|
| Projection Mesh | $\text{PM}$ | Crown projection region mesh on the XY plane (based on simplified canopy model) |
| Crown Volume | $V_{\text{crown}}$ | Canopy volume (based on ShrinkWrap or Convex Hull) |
| Projection Area | $A_{\text{proj}}$ | Crown projection area (based on simplified canopy model) |
| Leaf Area | $LA$ | Total leaf area |
| LAI | $LAI$ | Leaf Area Index |
| LAD | $LAD$ | Leaf Area Density |
| Tree Detail | — | Scaled detailed tree model (merged mesh) |
| Tree Canopy | $\text{TreeCan}$ | Simplified canopy envelope mesh |

---

## 4. Complete Pipeline Workflow

```
Tree Generator (S, H, R, D, B, DG, LG, TR, RD, PS, Sd)
       |
       |---- TM (Tree Mesh) ----> Leaf Distributor (LM, TM, BL, CC, DS, SF, SR, SS)
       |---- BL (Branch Lines) -->    |
                                     |
                              Tree Model (TM)
                                     |
                                     v
                    Tree Geometry Processor (TM, P, H, R, L, SW)
                                     |
              +----------------------+----------------------+
              |                      |                      |
         Tree Detail            Tree Canopy          Projection Mesh
              |                      |                      |
            LA                 V_crown                A_proj
              |                      |                      |
            LAI = LA/A_proj    LAD = LA/V_crown
```

This pipeline implements a complete closed loop from parametric generation to physical dimension adaptation, and then to canopy characteristic parameter extraction. All stages maintain reproducibility (controlled via random seed) and user controllability.

### Key Design Notes

- **Projection Area Calculation**: Projection is based on the simplified canopy model (ShrinkWrap or Convex Hull) rather than the original detailed model, avoiding face overlap and double-counting issues inherent in direct projection of complex 3D meshes.
- **Canopy Model Priority**: `ShrinkWrap` is the default strategy, which generates a tighter canopy envelope mesh suitable for most cases. `Convex Hull` serves as a fallback when ShrinkWrap is unavailable or fails.
- **Leaf Cluster Distribution**: The convex hull used for leaf distribution is constructed from the start, end, and midpoints of branch lines (excluding the trunk), ensuring the hull wraps the branch skeleton and provides a reliable canopy boundary for point distribution.
