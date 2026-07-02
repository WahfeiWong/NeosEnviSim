# 参数化树木生成与几何处理管线

## 1. 递归分枝生成（Tree Generator）

树木生成器基于递归分形原理构建树木骨架。其核心为深度优先的递归分枝算法，输入参数包括：树干起点 $S$、树干高度 $H$、分枝扩散角系数 $R$（控制 $\tan\theta$）、最大递归深度 $D$、每节点最大分枝数 $B$、分枝概率图 $DG$（各深度的分枝概率）、长度衰减图 $LG$（各深度的长度倍率）、树干半径 $TR$、半径衰减系数 $RD$、管段剖分数 $PS$ 以及随机种子 $Sd$。

### 1.1 递归分枝算法

算法从单一主干线段 $L_0$（从 $S$ 到 $S + (0,0,H)$）开始，通过递归函数 `RecursiveBranch` 逐层衍生子分枝。在第 $d$ 层递归中，算法首先以概率 $DG[d]$ 决定是否继续分枝；若中止，则当前分枝作为末梢叶枝被记录。分枝长度修正因子为：

$$\lambda_d = LG[d] \cdot \lambda_{d-1}$$

其中 $\lambda_0 = 1.0$ 为初始修正因子。子分枝的生成遵循以下几何构造过程：

1. **父枝方向向量**：由父枝起点指向终点得到方向向量 $\vec{b}$。
2. **旋转半径**：在父枝末端点处以半径 $r = R \cdot |\vec{b}|$ 作圆。
3. **旋转平面**：以父枝方向 $\vec{b}$ 为法向，过末端点 $P_{\text{end}}$ 构造平面 $\Pi$。
4. **圆上基点**：在平面 $\Pi$ 上取圆的最接近树根方向的点 $P_{\text{base}}$。
5. **子枝方向**：子枝沿 $P_{\text{end}}$ 指向 $P_{\text{base}} - \lambda_d \cdot \vec{b}$ 的方向延伸。
6. **周向均匀旋转**：实际子枝数量为 $n_d = \max(1, \text{round}(B \cdot DG[d]))$，各子枝围绕父枝轴线均匀旋转 $2\pi/n_d$ 分布。

### 1.2 枝干网格化

每条分枝线段通过 `Mesh.CreateFromCurvePipe` 方法转换为圆柱网格。第 $d$ 层分枝的半径为：

$$r_d = TR \cdot RD^d$$

并通过下限截断 $r_d \geq 0.001$ 避免数值问题。圆柱剖分数由参数 $PS$ 控制，所有分枝网格合并为单一树木网格 `treeMesh`。

---

## 2. 叶簇表面散布（Leaf Distributor）

叶簇散布组件接收树生成器输出的树木网格（`TM`）与分枝线列表（`BL`），将输入的叶簇网格模型通过凸包表面散布算法分布于树冠区域。

### 2.1 主干识别与冠层点集构建

首先遍历所有分枝线，通过中点 $Z$ 坐标最低准则识别主干（ trunk ）：

$$\text{trunk} = \arg\min_i \frac{Z_{\text{start}}^{(i)} + Z_{\text{end}}^{(i)}}{2}$$

排除主干后，对其余分枝线（即分枝）提取每段的起点 $\mathbf{p}_s$、终点 $\mathbf{p}_e$ 与中点 $\mathbf{p}_m = (\mathbf{p}_s + \mathbf{p}_e)/2$，构成冠层点集 $\mathcal{P}$。该点集包含 $3(N-1)$ 个三维点（$N$ 为分枝总数），均匀覆盖树冠骨架的空间范围。

### 2.2 QuickHull 3D 凸包构建

基于冠层点集 $\mathcal{P}$ 构建三维凸包网格，采用 QuickHull 3D 增量算法：

**Step 1 — 初始四面体**：沿 $X$ 轴取极值点 $\mathbf{p}_{\min X}$ 与 $\mathbf{p}_{\max X}$，寻找距线段最远的点 $\mathbf{p}_2$ 构造基准面，再寻找距该面最远的点 $\mathbf{p}_3$，构成非退化初始四面体。

**Step 2 — 内部参考点**：计算四面体几何中心作为内部参考点 $\mathbf{p}_{\text{int}}$，用于保证所有面法向朝外。

**Step 3 — 面-点归属**：对非四面体顶点，判断其位于哪个面的正（外）侧，加入该面的 `Outside` 集合。

**Step 4 — 增量扩展**：维护待处理面队列。对每一面，取其 `Outside` 集合中距离最远的点 $\mathbf{p}_f$，寻找所有从 $\mathbf{p}_f$ 可见的面构成可见集 $\mathcal{V}$。可见面与不可见面之间的边界边构成地平线（horizon edges）。从 $\mathbf{p}_f$ 向各地平线边构造新面，以 $\mathbf{p}_{\text{int}}$ 验证法向朝外，并将孤儿点重新分配至新面。迭代直至所有面的 `Outside` 为空。

**Step 5 — 网格输出**：将凸包面三角化后输出 `hullMesh`。

### 2.3 面积加权表面点散布

在凸包表面生成指定数量 $N_c$ 的散布点，采用面积加权的随机采样策略（等效于 Grasshopper 的 Populate Geometry 组件）：

1. 计算各三角面面积并构建累积面积数组 $A_{\text{cum}}$。
2. 以概率 $P_i = A_i / A_{\text{total}}$ 面积加权随机选择三角面。
3. 在选中的三角形内生成均匀随机重心坐标：设 $u, v \sim U(0,1)$，若 $u+v > 1$ 则反射为 $(1-u, 1-v)$，令 $w = 1-u-v$，散布点为 $\mathbf{p} = u\mathbf{a} + v\mathbf{b} + w\mathbf{c}$。

### 2.4 Orient 变换

以叶簇网格包围盒中心 $\mathbf{c}_{\text{leaf}}$ 为源参考点，对每个散布目标点 $\mathbf{p}_t$ 构建复合仿射变换（等效于 Grasshopper Orient 组件）：

$$\mathbf{T} = \mathbf{T}_{\text{translate}}(\mathbf{p}_t - \mathbf{c}_{\text{leaf}}) \cdot \mathbf{R}_z(\theta_z) \cdot \mathbf{R}_y(\theta_y) \cdot \mathbf{R}_x(\theta_x) \cdot \mathbf{S}(\mathbf{c}_{\text{leaf}}, s)$$

其中缩放因子 $s$ 可由用户指定（`SF`），或基于区间 $[\text{SR}_{\min}, \text{SR}_{\max}]$ 随机选取。最终将变换后的叶簇网格合并至树木网格输出。

---

## 3. 树木几何参数化处理（Tree Geometry Processor）

树木几何处理组件将参数化生成的树木模型缩放到用户指定的物理尺寸，并计算冠层几何特征参数。

### 3.1 非均匀缩放

接收含叶簇的完整树木网格（`TM`）、种植点（`P`）、目标树高（`H`）与目标冠幅半径（`R`），计算原始包围盒尺寸：

$$W_{\text{orig}}, D_{\text{orig}}, H_{\text{orig}}$$

构造非均匀缩放变换，使原始树木适配目标尺寸：

$$\mathbf{T}_{\text{scale}} = \text{Scale}\big(\mathbf{c}_{\text{bbox}}, \; \frac{2R}{W_{\text{orig}}}, \; \frac{2R}{D_{\text{orig}}}, \; \frac{H}{H_{\text{orig}}}\big)$$

缩放后通过平移变换将树木底面中心对齐至种植点 $\mathbf{P}$：

$$\mathbf{T}_{\text{move}} = \text{Translate}\big(\mathbf{P} - \mathbf{b}_{\text{center}}\big)$$

### 3.2 简化冠层模型

为精确计算投影与体积，基于缩放后的完整树木网格生成简化冠层模型，提供两种策略：

- **ShrinkWrap**（默认）：通过 `Mesh.ShrinkWrap` 将复杂树木几何收缩包裹为封闭的近似包络网格，目标边长参数 `Edge Length`（$L$）控制网格分辨率。
- **Convex Hull**（回退）：当 ShrinkWrap 不可用时，收集所有网格顶点调用 `Mesh.CreateConvexHull3D` 构建凸包网格。

冠层体积通过 `VolumeMassProperties.Compute` 计算：

$$V_{\text{crown}} = \text{Volume}(\text{TreeCan})$$

### 3.3 投影面积计算

冠层投影计算基于简化冠层模型（而非原始精细模型），避免面片重叠导致的面积高估：

1. 将简化冠层网格所有顶点 $Z$ 坐标置零（投影至 XY 平面）。
2. 提取平面投影后的二维轮廓线（outline）。
3. 由闭合轮廓构造平面 Brep（自动处理内外环）。
4. 通过 `Brep.GetArea()` 精确计算投影面积 $A_{\text{proj}}$。

投影网格 `Projection Mesh` 为 XY 平面上的二维区域网格，可直接用于阴影计算与 CFD 前置处理。

### 3.4 冠层参数计算

- **叶面积**（LA）：计算缩放后完整树木网格的总表面积：

$$LA = \sum_{i} \text{Area}(\text{mesh}_i)$$

- **叶面积指数**（LAI）：单位投影面积上的叶面积总量：

$$LAI = \frac{LA}{A_{\text{proj}}}$$

- **叶面积密度**（LAD）：单位冠层体积内的叶面积总量：

$$LAD = \frac{LA}{V_{\text{crown}}}$$

### 3.5 输出数据

| 输出端口 | 符号 | 含义 |
|---------|------|------|
| Tree Detail | — | 缩放后的精细树木模型（合并 Mesh） |
| Tree Canopy | $\text{TreeCan}$ | 简化冠层包络网格 |
| Projection Mesh | $\text{PM}$ | XY 平面上的树冠投影区域网格 |
| Crown Volume | $V_{\text{crown}}$ | 冠层体积 |
| Projection Area | $A_{\text{proj}}$ | 冠层投影面积 |
| Leaf Area | $LA$ | 总叶面积 |
| LAI | $LAI$ | 叶面积指数 |
| LAD | $LAD$ | 叶面积密度 |

---

## 4. 完整管线工作流

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

该管线实现了从参数化生成到物理尺寸适配、再到冠层特征参数提取的完整闭环，所有环节均保持可复现性（通过随机种子控制）与用户可控性。
