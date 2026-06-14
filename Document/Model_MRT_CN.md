# MRT (Mean Radiant Temperature) 模块

本模块用于计算室外环境中人体的平均辐射温度（MRT），基于反向光线追踪的建筑/植被遮挡分析，支持SolarCal（ASHRAE 55）和RayMan两种计算模型，并采用全4π球面视角系数分解实现物理上正确的长波辐射计算。

---

## 1. MRT 物理模型（MRTmodel.cs）

### 1.1 SolarCal 模型（ASHRAE 55）

基于 ASHRAE Standard 55 的 SolarCal 模型，将短波太阳辐射增量与三方向长波辐射分解相结合。

**短波辐射增量：**

人体接收的太阳辐射通量由直接辐射、散射辐射和地面反射三部分组成：

$$I_{\text{body}} = f_{\text{DNI}} \cdot f_p(\gamma) \cdot I_{\text{DN}} + \frac{1}{2} f \cdot F_{\text{SVF}} \cdot I_{\text{DH}} + \frac{1}{2} f \cdot F_{\text{GVF}} \cdot \rho_g \cdot I_{\text{GH}}$$

其中：
- $f_{\text{DNI}}$：有效DNI暴露因子（0–1），由反向光线追踪结合障碍物透射计算得出
- $f_p(\gamma)$：太阳投影系数（见下式）
- $I_{\text{DN}}$：直接法向辐射 [W/m²]
- $I_{\text{DH}}$：水平面散射辐射 [W/m²]
- $I_{\text{GH}}$：水平面总辐射 [W/m²]
- $f$：姿态效率因子（站立 $f=0.725$，坐姿 $f=0.696$）
- $F_{\text{SVF}}$：天空视角系数
- $F_{\text{GVF}}$：地面视角系数
- $\rho_g$：地面反射率

**有效DNI暴露因子 $f_{\text{DNI}}$（增强版，2026-06-14）：**

传统暴露因子 $f_{\text{exp}}$ 仅判断采样点是否被遮挡（二值：0或1）。为精细计算障碍物下的直接辐射贡献，引入有效DNI暴露因子 $f_{\text{DNI}}$，支持三种障碍物类型的差异化处理。

**物理逻辑修正（2026-06-14）：** 光线从太阳射向地面（正向），代码使用反向光线追踪（从人体采样点向太阳方向发射射线）。关键物理约束：**不透光物体 (Opaque) 具有绝对遮挡优先权**——如果射线路径上存在任何不透光障碍物，其后面的树木或半透明遮阳构件均处于建筑阴影中，不应再计算DNI透射。

判断逻辑（修正后）：
1. 射线路径上**有任何不透光物体** → DNI = 0（完全阻挡）
2. 无Opaque时 → 找**从人体方向最远**的Tree/Translucent交点（等价于从太阳方向第一个遇到的障碍物）
3. 全部无遮挡 → DNI = 全额

| 击中类型 | DNI贡献 | 物理含义 |
|:---:|:---:|:---|
| 无遮挡 | 1.0 | 全额直接辐射 |
| 不透光物体 (Opaque) | 0.0 | 完全阻挡，其后方所有Tree/Translucent均处于阴影中 |
| 树木细节模型 (Tree) | $\exp(-k \cdot \text{LAD} \cdot s)$ | Beer-Lambert冠层透射（仅当射线路径无Opaque时） |
| 半透明遮阳构件 (Translucent) | $\tau$ | 固定透射率透射（仅当射线路径无Opaque时） |

有效DNI暴露因子为所有采样点DNI贡献的平均值：

$$f_{\text{DNI}} = \frac{1}{N} \sum_{i=1}^{N} \text{DNI}_{\text{contrib},i}$$

其中冠层路径长度 $s$ [m] 为反向光线与树木简化冠层模型（Mesh）两个交点间的距离，$k$ 为太阳辐射消光系数（默认0.65），$\text{LAD}$ 为叶面积密度 [m²/m³]，$\tau$ 为遮阳构件直接太阳辐射透射率。

**Beer-Lambert 冠层透射方程：**

$$I_{\text{transmitted}} = I_{\text{DN}} \cdot \exp(-k \cdot \text{LAD} \cdot s)$$

短波引起的MRT增量：

$$\Delta T_{\text{sw}} = \frac{I_{\text{body}} \cdot (\alpha / \varepsilon)}{f \cdot h_r}$$

其中 $\alpha$ 为人体短波吸收率，$\varepsilon$ 为人体长波发射率，$h_r$ 为辐射换热系数 [W/(m²·K)]。

**三方向长波辐射分解：**

$$\Delta T_{\text{lw}} = c_{\text{lw}} \cdot \left[ F_{\text{SVF}} \cdot (T_{\text{sky}} - T_{\text{ref}}) + F_{\text{GVF}} \cdot (T_g - T_{\text{ref}}) + F_{\text{OVF}} \cdot (T_{\text{obs}} - T_{\text{ref}}) \right]$$

其中：
- $c_{\text{lw}}$：长波线性化系数（默认 0.5）
- $T_{\text{sky}}$：天空等效温度 [°C]
- $T_g$：地面温度 [°C]
- $T_{\text{obs}}$：周围障碍物表面温度 [°C]
- $T_{\text{ref}}$：参考温度（取空气温度）[°C]
- $F_{\text{OVF}}$：障碍物视角系数

视角系数守恒：

$$F_{\text{SVF}} + F_{\text{GVF}} + F_{\text{OVF}} = 1.0$$

**最终MRT：**

$$T_{\text{MRT}} = T_a + \Delta T_{\text{sw}} + \Delta T_{\text{lw}}$$

**太阳投影系数：**

$$f_p(\gamma) = 0.308 \cdot \cos\gamma \cdot \left(0.998 - \frac{\gamma^2}{50000}\right), \quad \gamma \in [0°, 90°]$$

其中 $\gamma$ 为太阳高度角 [°]。

**天空等效温度：**

$$T_{\text{sky}} = \left( \frac{I_{\text{IR}}}{\varepsilon_{\text{sky}} \cdot \sigma} \right)^{0.25} - 273.15$$

其中 $I_{\text{IR}}$ 为水平面红外辐射 [W/m²]，$\sigma = 5.67 \times 10^{-8}$ W/(m²·K⁴)。

当 $\varepsilon_{\text{sky}} < 0$（自动模式）时，使用露点温度计算：

$$\varepsilon_{\text{sky}} = 0.711 + 0.56 \cdot T_d + 0.73 \cdot T_d^2$$

其中 $T_d$ 为露点温度 [°C]，结果被约束在 [0.5, 1.0] 范围内。

### 1.2 RayMan 模型

基于 Matzarakis 等人的 RayMan 模型，采用完整的辐射平衡方法。

长波辐射的四次方形式：

$$T_{\text{MRT}} = (\text{mrtK}_4)^{0.25} - 273.15$$

其中：

$$\text{mrtK}_4 = \frac{1}{\sigma} \left( \left[ L_{\text{sky}} + \frac{\alpha}{\varepsilon} I_{\text{DH}} \right] F_{\text{SVF}} + \left[ L_g + \frac{\alpha}{\varepsilon} \rho_g I_{\text{GH}} \right] F_{\text{GVF}} + L_{\text{obs}} F_{\text{OVF}} \right) + \frac{\alpha \cdot I_{\text{direct}}}{\varepsilon \cdot \sigma}$$

各方向长波辐射：

$$L_{\text{sky}} = \varepsilon_{\text{sky}} \cdot \sigma \cdot T_{\text{sky,K}}^4$$

$$L_g = \varepsilon_g \cdot \sigma \cdot T_{g,\text{K}}^4$$

$$L_{\text{obs}} = \varepsilon_{\text{obs}} \cdot \sigma \cdot T_{\text{obs,K}}^4$$

直接太阳辐射项（仅当 $I_{\text{DN}} > 0$ 且 $f_{\text{DNI}} > 0$ 时）：

$$I_{\text{direct}} = f_{\text{DNI}} \cdot f_p(\gamma) \cdot I_{\text{DN}}$$

其中 $f_{\text{DNI}}$ 为有效DNI暴露因子，已综合考量植被冠层透射（Beer-Lambert定律）与半透明材质透射。

### 1.3 统一入口

`CalculateMRT()` 方法根据 `useRayMan` 参数自动选择上述两种模型之一。

---

## 2. 人体暴露因子与视角系数模型（HumanExposureModel.cs）

### 2.1 太阳暴露因子计算

使用反向光线追踪计算人体被太阳直接照射的比例 $f_{\text{exp}} \in [0, 1]$。

**算法流程：**

1. 沿人体高度方向均匀布置 $N$ 个采样点（默认 $N=3$，从地面到全身高度）
2. 对每个采样点，沿太阳方向发射射线
3. 检测射线是否与障碍物网格相交
4. 暴露因子 = 未被遮挡的采样点数 / 总采样点数

$$f_{\text{exp}} = \frac{N_{\text{exposed}}}{N_{\text{total}}}$$

**参数：**

| 参数 | 单位 | 默认值 | 说明 |
|:---:|:---:|:---:|:---|
| bodyHeight | m | 1.7 | 人体总高度 |
| analysisHeight | m | 1.5 | 分析眼高度（用于视角系数） |
| samplePointCount | - | 3 | 高度方向采样点数 |
| maxRayDistance | m | 500.0 | 最大射线追踪距离 |
| rayOffset | m | 0 | 射线起点偏移 |

**有效DNI暴露因子 $f_{\text{DNI}}$ 计算（增强版，2026-06-14）：**

新增方法 `CalculateExposureFactorsWithTransmission` 同时计算传统暴露因子 $f_{\text{exp}}$ 和有效DNI暴露因子 $f_{\text{DNI}}$：

- $f_{\text{exp}}$：二值暴露比例（仅判断遮挡/未遮挡）
- $f_{\text{DNI}}$：综合暴露与透射的DNI有效因子，替代 $f_{\text{exp}}$ 用于直接辐射MRT计算

对每个采样点，射线分类逻辑（修正后）：
1. 先检测所有Opaque Mesh——**有任何交点立即返回Opaque**（射线路径上存在不透光障碍物，其后方Tree/Translucent均处于阴影中，DNI=0）
2. 无Opaque交点时，在所有Tree/Translucent Mesh中找**从人体方向最远**的交点（等价于从太阳方向第一个障碍物）
3. 若射线同时与多类非Opaque障碍物相交，以**从太阳方向最近**（从人体方向最远）的障碍物类型为准

**植被冠层路径长度计算：**

使用 `CalculateCanopyPathLength` 方法，通过 `MeshLine` 射线与树木简化冠层模型求交，获取所有进出交点参数，计算几何路径长度：

$$s = t_{\text{exit}} - t_{\text{entry}}$$

其中 $t_{\text{entry}}$ 为射线进入冠层的第一个有效交点参数，$t_{\text{exit}}$ 为离开冠层的最后一个有效交点参数。

### 2.2 全4π球面视角系数分解

将人体周围的全空间（4π球面）分解为三个部分，满足守恒条件：

$$F_{\text{SVF}} + F_{\text{GVF}} + F_{\text{OVF}} = 1.0$$

**算法流程：**

1. 使用 Fibonacci（黄金角）螺旋生成 $N$ 个均匀分布的全球面方向向量
2. 从分析点沿每个方向发射射线
3. 根据射线结果分类：
   - 射线未被遮挡且 $Z > 0$ → 计入天空视角系数 $F_{\text{SVF}}$
   - 射线未被遮挡且 $Z < 0$ → 计入地面视角系数 $F_{\text{GVF}}$
   - 射线被障碍物遮挡 → 计入障碍物视角系数 $F_{\text{OVF}}$

**Fibonacci 球面采样：**

$$z_i = 1 - \frac{2i + 1}{N}, \quad r_i = \sqrt{1 - z_i^2}, \quad \theta_i = \phi \cdot i$$

$$\vec{d}_i = (r_i \cos\theta_i,\; r_i \sin\theta_i,\; z_i)$$

其中 $\phi = \pi(3 - \sqrt{5}) \approx 2.39996$ rad 为黄金角。

### 2.3 上半球天空视角系数

用于传统的仅天空视角系数计算（长波分解中已不使用，保留用于兼容）：

$$F_{\text{SVF}} = \frac{N_{\text{visible,sky}}}{N_{\text{total}}}$$

方向生成使用 Fibonacci 上半球采样：

$$z_i = \sqrt{1 - \frac{i}{N}}, \quad i = 0, 1, \ldots, N-1$$

---

## 3. MRT Grasshopper 组件（MRTcalculator.cs）

### 3.1 输入参数

| 索引 | 参数 | 类型 | 说明 |
|:---:|:---:|:---:|:---|
| 0 | EPW File | Text | EPW气象文件路径 |
| 1 | Analysis Points | Point3d List | 地面分析点（必须位于地表Z=0，非气象高度） |
| 2 | Obstacle Set | Generic | **增强版（2026-06-14）**：障碍物设置集（ObsSet），支持分类障碍物（不透光/树木/半透明）。连接ObsSet组件。向后兼容：可接受List<Brep>或List<Mesh>作为不透光障碍物（可选） |
| 3 | MRT Settings | Generic | MRT配置设置（可选，默认新建设置） |
| 4 | Time Settings | Generic | 模拟时间段（可选，默认全年8760h） |
| 5 | Air Temperature (Ta) | Number Tree | 空气温度 [°C]，支持4种输入模式（可选） |
| 6 | Ground Temperature (Tg) | Number Tree | 地面温度 [°C]，支持4种输入模式（可选） |
| 7 | Surrounding Surface Temp (Tsur) | Number List | 周围障碍物表面温度 [°C]（可选） |
| 8 | Run | Boolean | 设置为true执行模拟 |

**Ta/Tg 输入模式：**

| 模式 | 数据量 | 说明 |
|:---:|:---:|:---|
| 1 | 1个值 | 统一固定温度，所有时刻所有点 |
| 2 | N个值（N=点数） | 逐点固定温度 |
| 3 | 8760个值 | 逐时变化温度（所有点共享） |
| 4 | N×8760 Tree | 逐点逐时变化温度 |

### 3.2 输出参数

| 索引 | 参数 | 说明 |
|:---:|:---:|:---|
| 0 | MRT | 平均辐射温度 [°C]，每点每时 |
| 1 | SVF | 天空视角系数 [0–1]，全4π采样 |
| 2 | GVF | 地面视角系数 [0–1]，全4π采样 |
| 3 | OVF | 障碍物视角系数 [0–1]，全4π采样 |
| 4 | Exp | 太阳暴露因子 $f_{\text{exp}}$ [0–1]，每点每时（二值：遮挡/未遮挡） |
| 5 | DNIExp | **新增**：有效DNI暴露因子 $f_{\text{DNI}}$ [0–1]，综合暴露与透射 |
| 6 | dT_sw | 短波辐射引起的MRT增量 [°C] |
| 7 | dT_lw | 长波辐射总MRT增量 [°C] |
| 8 | dT_lw_sky | 天空长波分量贡献 [°C] |
| 9 | dT_lw_grd | 地面长波分量贡献 [°C] |
| 10 | dT_lw_obs | 障碍物长波分量贡献 [°C] |
| 11 | HrMRT | 所有点逐时平均MRT [°C] |
| 12 | SunVec | 每个分析时刻的太阳向量 |

---

## 4. MRT 设置组件（MRTsettings.cs）

### 4.1 输入参数

| 参数 | 标识 | 单位 | 默认值 | 说明 |
|:---:|:---:|:---:|:---:|:---|
| Use RayMan | RayMan | - | false | 使用RayMan模型替代SolarCal |
| Posture | Post | - | 0 | 人体姿态：0=站立(f=0.725)，1=坐姿(f=0.696) |
| Body Absorptivity | Alpha | - | 0.7 | 短波吸收率 |
| Body Emissivity | Epsilon | - | 0.95 | 长波发射率 |
| Radiative HTC | Hr | W/(m²·K) | 6.012 | 辐射换热系数 |
| Floor Reflectance | Rho | - | 0.25 | 地面反射率 |
| Exposure Samples | NExp | - | 3 | 暴露因子射线追踪采样点数 |
| Eye Height for SVF | HSVF | m | 1.5 | 视角系数计算眼高度 |
| Body Height | HBod | m | 1.7 | 人体总高度 |
| Sky Emissivity | SkyEps | - | -1 | 天空发射率，-1=自动（露点计算） |
| SVF Sample Count | SVF_N | - | 1000 | 全4π球面采样数 |
| Longwave Coeff | LwCoeff | - | 0.5 | 长波线性化系数 |
| Ground Emissivity | EpsGrd | - | 0.95 | 地面长波发射率（仅RayMan使用） |
| Obstacle Emissivity | EpsObs | - | 0.95 | 障碍物长波发射率（仅RayMan使用） |

---

## 3. 障碍物设置组件（ObsSetSettings.cs）

### 3.1 功能说明

ObsSet组件用于创建分类障碍物设置集（`ObstacleSet`），替代MRT组件原有的平面Brep列表输入。通过将障碍物分类为不透光物体、树木（含冠层透射）和半透明遮阳构件，实现精细化的直接辐射（DNI）计算。

**核心原理：**
- 当反向光线追踪的采样点被遮挡时，传统方法直接忽略该点的DNI贡献（$f_{\text{exp}}=0$ → DNI=0）
- 增强版方法根据障碍物类型，通过Beer-Lambert定律或固定透射率计算DNI的部分透射
- 有效DNI暴露因子 $f_{\text{DNI}}$ 综合暴露与透射效应，替代原 $f_{\text{exp}}$ 用于直接辐射计算

### 3.2 输入参数

| 索引 | 参数 | 标识 | 类型 | 默认值 | 说明 |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | Tree Detail | TreeDet | Mesh List | — | 树的细节模型（树叶、枝干），来自Tree Processor组件 |
| 1 | Tree Canopy | TreeCan | Mesh List | — | 树木简化冠层模型，用于计算光线穿过冠层的几何路径长度 |
| 2 | Leaf Area Density | LAD | Number | 1.0 | 叶面积密度 [m²/m³]，表示单位冠层体积的叶面积 |
| 3 | Extinction Coeff | k | Number | 0.65 | 太阳辐射消光系数 [-]，Beer-Lambert定律参数，典型范围0.5–0.8 |
| 4 | Translucent Shade | TransShd | Mesh List | — | 半透明遮阳构件（穿孔金属板、织物等） |
| 5 | Transmittance | Tau | Number | 0.05 | 遮阳构件直接太阳辐射透射率 [-]，范围0.0–1.0 |
| 6 | Opaque Objects | Opaque | Mesh List | — | 不透光物体（建筑、围墙等），完全阻挡直接辐射 |

**典型透射率参考值：**

| 材料类型 | 透射率范围 |
|:---:|:---:|
| 穿孔金属板 | 0.05–0.15 |
| 遮阳织物 | 0.02–0.30 |
| 聚碳酸酯板 (PC) | 0.60–0.85 |
| 玻璃 | 0.70–0.90 |

### 3.3 输出参数

| 索引 | 参数 | 说明 |
|:---:|:---:|:---|
| 0 | ObsSet | 分类障碍物设置集，连接至MRT组件的Obstacle Set输入端 |

### 3.4 使用注意事项

1. **Tree Detail vs Tree Canopy**：Tree Detail用于射线击中检测（判断点是否在树荫下），Tree Canopy用于路径长度计算（光线穿过冠层的距离）。两者缺一不可，若提供了Tree Detail但未提供Tree Canopy，组件会发出警告，且DNI透射将使用零路径长度。

2. **向后兼容**：MRT组件的Obstacle Set输入端向后兼容，仍可接受List<Brep>或List<Mesh>作为输入（自动归类为Opaque不透光物体）。

3. **Mesh输入**：支持直接输入Mesh类型。若输入Surface或Brep类型，Grasshopper可隐式转换为Mesh。

---

## 4. 参考文献

1. ASHRAE Standard 55-2017: Thermal Environmental Conditions for Human Occupancy. American Society of Heating, Refrigerating and Air-Conditioning Engineers, Atlanta, GA, 2017.

2. Arens, E., et al. (2015). Modeling the comfort effects of short-wave solar radiation indoors. *Building and Environment*, 88, 3-9. https://doi.org/10.1016/j.buildenv.2014.09.004

3. Matzarakis, A., Rutz, F., & Mayer, H. (2010). Modelling radiation fluxes in simple and complex environments: basics of the RayMan model. *International Journal of Biometeorology*, 54, 131-139. https://doi.org/10.1007/s00484-009-0261-5

4. ISO 7726:1998. Ergonomics of the thermal environment — Instruments for measuring physical quantities. International Organization for Standardization, Geneva, 1998.
