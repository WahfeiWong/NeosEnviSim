# MRT (Mean Radiant Temperature) 模块

本模块用于计算室外环境中人体的平均辐射温度（MRT），基于反向光线追踪的建筑/植被遮挡分析，支持 SolarCal（ASHRAE 55）和 RayMan 两种计算模型。采用全 4π 球面视角因子**五分量分解**（天空、地面、不透光障碍物、树木、半透明遮阳），长波辐射与漫射辐射均按分解后的视角因子逐类计算，实现物理上正确的辐射环境建模。

**当前版本要点（2026-06-16）：**
1. **五分量视角因子分解**：SVF / GVF / OVF_opaque / TVF / TRVF 互斥划分，满足守恒；重叠方向按 不透光 > 树木细节 > 半透明遮阳 优先级归类。
2. **五分量长波辐射分解**：天空、地面、不透光障碍物、树木冠层、半透明遮阳各自独立计算长波贡献，每类表面温度独立配置。
3. **表面温度输入移至 ObsSet 组件**：MRT 组件的 Tsur 输入端已移除；不透光表面温度（T_opaque）、树冠温度（T_tree）、半透明表面温度（T_trans）由 ObsSet 组件提供，支持单值或 8760 逐时序列，未输入时回退为空气温度。
4. **MRT 组件 I/O 重排**：8 个输入端、17 个输出端（MRT 相关 9 项、视角因子 5 项、暴露因子 2 项、其他 1 项），长波五分量贡献逐项输出。

**历史增强（2026-06-15）：** 修正 DNI 物理模型——当射线同时与多类非 Opaque 障碍物相交时，DNI 贡献率为各障碍物贡献率的**乘积**，而非仅考虑从太阳方向最近的遮挡。新增 `CalculateRayDNITransmission` 核心方法，替代原有的 `ClassifyRayHit` 单障碍物分类逻辑。ObsSet 输入端**不再向后兼容**，仅接受 ObstacleSet 封装数据。

**历史增强（2026-06-14）：** 引入精细化 DNI（直接法向辐射）暴露因子计算，支持三种障碍物类型的差异化透射处理：不透光物体（完全阻挡）、树木（Beer-Lambert 冠层透射）、半透明遮阳构件（固定透射率）。

---

## 1. MRT 物理模型（MRTmodel.cs）

### 1.1 SolarCal 模型（ASHRAE 55）

基于 ASHRAE Standard 55 的 SolarCal 模型，将短波太阳辐射增量与**五分量长波辐射分解**相结合。

**短波辐射增量：**

人体接收的太阳辐射通量由直接辐射、散射辐射和地面反射三部分组成：

$$I_{\text{body}} = f_{\text{DNI}} \cdot f_p(\gamma) \cdot I_{\text{DN}} + \frac{1}{2} f \cdot I_{\text{DH,eff}} + \frac{1}{2} f \cdot F_{\text{GVF}} \cdot \rho_g \cdot I_{\text{GH}}$$

**有效漫射辐照度（五分量分解）：**

$$I_{\text{DH,eff}} = I_{\text{DH}} \cdot \left( F_{\text{SVF}} + F_{\text{TVF}} \cdot e^{-k_c \cdot \text{LAD} \cdot l} + F_{\text{TRVF}} \cdot \tau \right)$$

其中：
- $F_{\text{SVF}}$：天空视角因子（可见天空方向比例）
- $F_{\text{TVF}}$：树木视角因子（被树木细节网格遮挡的方向比例）
- $F_{\text{TRVF}}$：半透明视角因子（被半透明遮阳网格遮挡的方向比例）
- $k_c$：树冠消光系数
- $\text{LAD}$：叶面积密度 [m²/m³]
- $l$：特征树冠厚度 [m]，取所有树冠简化模型合并包围盒的 Z 向高度
- $\tau$：半透明材料短波透射率

即：天空方向漫射全量到达，树冠方向漫射经 Beer-Lambert 衰减，半透明方向漫射按透射率 $\tau$ 衰减；不透光障碍物方向（$F_{\text{OVF,opaque}}$）对漫射辐射无贡献。

其他符号：
- $f_{\text{DNI}}$：**有效 DNI 暴露因子**（0–1），综合暴露与透射效应（见第 2.1 节增强说明）
- $f_p(\gamma)$：太阳投影系数（见下式）
- $I_{\text{DN}}$：直接法向辐射 [W/m²]
- $I_{\text{DH}}$：水平面散射辐射 [W/m²]
- $I_{\text{GH}}$：水平面总辐射 [W/m²]
- $f$：姿态效率因子（站立 $f=0.725$，坐姿 $f=0.696$）
- $F_{\text{GVF}}$：地面视角系数
- $\rho_g$：地面反射率

> **注意（2026-06-15）：** 直接辐射项使用有效 DNI 暴露因子 $f_{\text{DNI}}$ 替代传统的二值暴露因子 $f_{\text{exp}}$。$f_{\text{DNI}}$ 综合考虑了障碍物类型（不透光/树木/半透明遮阳）对直接辐射的差异化透射效应，且当射线同时穿过多种非 Opaque 障碍物时，各项透射率**相乘**（物理修正）。当未连接 ObsSet 组件（无障碍物）时，$f_{\text{DNI}} = 1.0$（全额直射）。

短波引起的 MRT 增量：

$$\Delta T_{\text{sw}} = \frac{I_{\text{body}} \cdot (\alpha / \varepsilon)}{f \cdot h_r}$$

其中 $\alpha$ 为人体短波吸收率，$\varepsilon$ 为人体长波发射率，$h_r$ 为辐射换热系数 [W/(m²·K)]。

**五分量长波辐射分解（2026-06-16）：**

长波辐射引起的 MRT 增量按五类环境表面分别计算后线性叠加：

$$\Delta T_{\text{lw}} = c_{\text{lw}} \cdot \left[ F_{\text{SVF}} (T_{\text{sky}} - T_{\text{ref}}) + F_{\text{GVF}} (T_g - T_{\text{ref}}) + F_{\text{OVF,opaque}} (T_{\text{opaque}} - T_{\text{ref}}) + F_{\text{TVF}} (T_{\text{canopy}} - T_{\text{ref}}) + F_{\text{TRVF}} (T_{\text{trans}} - T_{\text{ref}}) \right]$$

其中：
- $c_{\text{lw}}$：长波线性化系数（默认 0.5）
- $T_{\text{ref}}$：参考温度，取空气温度 $T_a$ [°C]

各分量的视角因子、表面温度及其数据来源：

| 分量 | 视角因子 | 表面温度 | 温度来源 | 缺省回退 |
|:---:|:---:|:---:|:---|:---:|
| 天空 | $F_{\text{SVF}}$ | $T_{\text{sky}}$ | 由水平面红外辐射 $I_{\text{IR}}$ 与天空发射率 $\varepsilon_{\text{sky}}$ 反演（见下式） | — |
| 地面 | $F_{\text{GVF}}$ | $T_g$ | MRT 组件 Tg 输入端（支持 4 种输入模式） | $T_a$ |
| 不透光障碍物 | $F_{\text{OVF,opaque}}$ | $T_{\text{opaque}}$ | ObsSet 组件 T_opaque 输入端（单值或 8760 逐时） | $T_a$ |
| 树木冠层 | $F_{\text{TVF}}$ | $T_{\text{canopy}}$ | ObsSet 组件 T_tree 输入端（单值或 8760 逐时） | $T_a$ |
| 半透明遮阳 | $F_{\text{TRVF}}$ | $T_{\text{trans}}$ | ObsSet 组件 T_trans 输入端（单值或 8760 逐时） | $T_a$ |

各分量贡献由 MRT 组件逐项输出（dTlw_sky / dTlw_grd / dTlw_opq / dTlw_tree / dTlw_trans），总增量 $\Delta T_{\text{lw}}$ 为五者之和（见第 3.2 节）。

**五分量视角因子分解：**

全球面（4π）视角因子分解为五个互斥分量：

| 分量 | 符号 | 说明 |
|:---:|:---:|:---|
| 天空视角因子 | $F_{\text{SVF}}$ | 可见天空（Z > 0，无遮挡） |
| 地面视角因子 | $F_{\text{GVF}}$ | 可见地面（Z < 0，无遮挡） |
| 不透光障碍物视角因子 | $F_{\text{OVF,opaque}}$ | 仅被不透光物体遮挡 |
| 树木视角因子 | $F_{\text{TVF}}$ | 被树木细节网格遮挡 |
| 半透明视角因子 | $F_{\text{TRVF}}$ | 被半透明遮阳网格遮挡 |

**守恒关系：**

$$F_{\text{SVF}} + F_{\text{GVF}} + F_{\text{OVF,opaque}} + F_{\text{TVF}} + F_{\text{TRVF}} = 1.0$$

**重叠判定优先级：** 不透光 > 树木细节 > 半透明遮阳。当某方向同时被多类障碍物遮挡时，归入优先级最高的类别。

> **注意：** OVF 现为**仅不透光障碍物**（此前包含所有障碍物类型）。TVF 和 TRVF 分别表示被树木细节网格和半透明遮阳网格遮挡的全球面比例。

**最终 MRT：**

$$T_{\text{MRT}} = T_a + \Delta T_{\text{sw}} + \Delta T_{\text{lw}}$$

**太阳投影系数：**

$$f_p(\gamma) = 0.308 \cdot \cos\gamma \cdot \left(0.998 - \frac{\gamma^2}{50000}\right), \quad \gamma \in [0°, 90°]$$

其中 $\gamma$ 为太阳高度角 [°]。

**天空等效温度：**

$$T_{\text{sky}} = \left( \frac{I_{\text{IR}}}{\varepsilon_{\text{sky}} \cdot \sigma} \right)^{0.25} - 273.15$$

其中 $I_{\text{IR}}$ 为水平面红外辐射 [W/m²]，$\sigma = 5.67 \times 10^{-8}$ W/(m²·K⁴)。

当 $\varepsilon_{\text{sky}} < 0$（自动模式，默认）时，使用露点温度计算：

$$\varepsilon_{\text{sky}} = 0.711 + 0.56 \cdot \frac{T_d}{100} + 0.73 \cdot \left(\frac{T_d}{100}\right)^2$$

其中 $T_d$ 为露点温度 [°C]，结果被约束在 [0.5, 1.0] 范围内。若露点数据缺失，则回退为按总云量估算（$\varepsilon_{\text{sky}} = 0.75 + 0.02 N$，$N$ 为总云量，同样约束在 [0.5, 1.0]）；两者均缺失时取 $\varepsilon_{\text{sky}} = 1.0$（黑体天空）。

### 1.2 RayMan 模型

基于 Matzarakis 等人的 RayMan 模型，采用完整的辐射平衡方法（四次方形式）。

**长波辐射同样按五分量分解**，与 SolarCal 共用同一组五分量视角因子：

$$T_{\text{MRT}} = (\text{mrtK}_4)^{0.25} - 273.15$$

其中：

$$\text{mrtK}_4 = \frac{1}{\sigma} \left[ \left( L_{\text{sky}} + \frac{\alpha}{\varepsilon} I_{\text{DH,eff}} \right) F_{\text{SVF}} + \left( L_g + \frac{\alpha}{\varepsilon} \rho_g I_{\text{GH}} \right) F_{\text{GVF}} + L_{\text{obs}} F_{\text{OVF,opaque}} + L_{\text{tree}} F_{\text{TVF}} + L_{\text{trans}} F_{\text{TRVF}} \right] + \frac{\alpha \cdot I_{\text{direct}}}{\varepsilon \cdot \sigma}$$

各方向长波辐射（温度均为开尔文）：

$$L_{\text{sky}} = \varepsilon \cdot \sigma \cdot T_{\text{sky,K}}^4$$

$$L_g = \varepsilon_g \cdot \sigma \cdot T_{g,\text{K}}^4$$

$$L_{\text{obs}} = \varepsilon_{\text{obs}} \cdot \sigma \cdot T_{\text{opaque,K}}^4$$

$$L_{\text{tree}} = \varepsilon_{\text{obs}} \cdot \sigma \cdot T_{\text{canopy,K}}^4$$

$$L_{\text{trans}} = \varepsilon_{\text{obs}} \cdot \sigma \cdot T_{\text{trans,K}}^4$$

其中：
- $T_{\text{sky,K}}$：由 $I_{\text{IR}}$ 与 $\varepsilon_{\text{sky}}$ 反演的天空等效温度 [K]；天空长波项以人体发射率 $\varepsilon$ 计入人体吸收
- $\varepsilon_g$：地面长波发射率（MRT Settings 的 EpsGrd，默认 0.95）
- $\varepsilon_{\text{obs}}$：障碍物长波发射率（MRT Settings 的 EpsObs，默认 0.95）；**不透光障碍物、树木冠层与半透明遮阳三类表面共用该发射率**
- $T_{\text{opaque,K}}$、$T_{\text{canopy,K}}$、$T_{\text{trans,K}}$：三类障碍物表面温度 [K]，来源与 SolarCal 相同（ObsSet 组件，未输入时回退为 $T_a$）
- $I_{\text{DH,eff}}$：五分量分解的有效漫射辐照度（公式同 SolarCal，见第 1.1 节）
- 地面反射短波项 $\rho_g I_{\text{GH}}$ 仅计入地面方向（$F_{\text{GVF}}$）

直接太阳辐射项（仅当 $I_{\text{DN}} > 0$ 且 $f_{\text{DNI}} > 0$ 时）：

$$I_{\text{direct}} = f_{\text{DNI}} \cdot f_p(\gamma) \cdot I_{\text{DN}}$$

> **注意（2026-06-15）：** 直接辐射项使用 $f_{\text{DNI}}$ 替代 $f_{\text{exp}}$。$f_{\text{DNI}}$ 通过 ObstacleSet 和 HumanExposureModel 中的精细化计算获得，当射线穿过多种非 Opaque 障碍物时，各项透射率相乘（见第 2.1 节）。

### 1.3 统一入口

`CalculateMRT()` 方法根据 `useRayMan` 参数自动选择上述两种模型之一。

---

## 2. 人体暴露因子与视角系数模型（HumanExposureModel.cs）

### 2.1 太阳暴露因子计算（增强版，2026-06-15）

使用反向光线追踪计算人体被太阳直接照射的比例。本模块提供两种暴露因子：

- **$f_{\text{exp}}$（传统暴露因子）：** 二值判断，仅确定采样点是否被遮挡（0 或 1）
- **$f_{\text{DNI}}$（有效 DNI 暴露因子）：** 综合暴露与透射效应，支持三种障碍物类型的差异化处理，且多障碍物透射率**相乘**

**传统暴露因子算法：**

1. 沿人体高度方向均匀布置 $N$ 个采样点（默认 $N=3$，从地面到全身高度）
2. 对每个采样点，沿太阳方向发射射线
3. 检测射线是否与障碍物网格相交
4. 暴露因子 = 未被遮挡的采样点数 / 总采样点数

$$f_{\text{exp}} = \frac{N_{\text{exposed}}}{N_{\text{total}}}$$

**有效 DNI 暴露因子 $f_{\text{DNI}}$（增强版，2026-06-15）：**

对每个高度采样点沿太阳方向发射射线，计算穿过**所有**非 Opaque 障碍物的累积透射率：

| 击中类型 | DNI 贡献 | 物理含义 |
|:---:|:---:|:---|
| 无遮挡 | 1.0 | 全额直接辐射 |
| 不透光物体 (Opaque) | 0.0 | 完全阻挡，其后方所有 Tree/Translucent 均处于阴影中 |
| 树木细节模型 (Tree) | $\exp(-k \cdot \text{LAD} \cdot s)$ | Beer-Lambert 冠层透射（路径长度可跨多个树冠累加） |
| 半透明遮阳构件 (Translucent) | $\tau$ | 固定透射率（每个相交的 Translucent Mesh 独立贡献，多个相乘） |

**多障碍物 DNI 透射（2026-06-15 物理修正）：**

当射线同时穿过多种（或多个同类）非 Opaque 障碍物时，总 DNI 透射率为各障碍物透射率的**乘积**：

$$T_{\text{total}} = T_{\text{tree}} \times T_{\text{translucent,1}} \times T_{\text{translucent,2}} \times \cdots$$

其中：
- $T_{\text{tree}} = \exp(-k \cdot \text{LAD} \cdot s_{\text{total}})$：穿过所有树冠的总透射率（总路径长度 $s_{\text{total}}$ 为各树冠路径长度之和）
- $T_{\text{translucent},i} = \tau$：第 $i$ 个半透明遮阳构件的透射率（每个独立相乘）

**示例：** 光线先穿过树冠（透射率 0.6），再穿过两个半透明遮阳板（透射率各 0.5）：

$$T_{\text{total}} = 0.6 \times 0.5 \times 0.5 = 0.15$$

有效 DNI 暴露因子为所有采样点 DNI 贡献的平均值：

$$f_{\text{DNI}} = \frac{1}{N} \sum_{i=1}^{N} \text{DNI}_{\text{contrib},i}$$

**核心物理逻辑：**

1. **Opaque 绝对优先**：射线路径上任何 Opaque 交点 → DNI = 0
2. **Tree 透射**：命中任何 TreeDetail → 使用总冠层路径长度计算 Beer-Lambert 透射
3. **Translucent 透射**：命中每个 TranslucentShade Mesh → 各自乘以 $\tau$
4. **乘积法则**：所有非 Opaque 透射率相乘，结果 clamp 到 [0, 1]

**Beer-Lambert 冠层透射方程：**

$$I_{\text{transmitted}} = I_{\text{DN}} \cdot \exp(-k \cdot \text{LAD} \cdot s)$$

其中：
- $s$：光线穿过树木冠层的几何路径长度 [m]，通过射线与简化冠层模型的进出交点计算
- $k$：太阳辐射消光系数 [-]，默认 0.5，典型范围 0.5–0.8（阔叶）、0.3–0.5（针叶）
- $\text{LAD}$：叶面积密度 [m²/m³]，默认 1.0，典型范围 0.5–8.0
- $\tau$：遮阳构件直接太阳辐射透射率 [-]，默认 0.05

**参数：**

| 参数 | 单位 | 默认值 | 说明 |
|:---:|:---:|:---:|:---|
| bodyHeight | m | 1.7 | 人体总高度 |
| analysisHeight | m | 1.5 | 分析眼高度（用于视角系数） |
| samplePointCount | - | 3 | 高度方向采样点数 |
| maxRayDistance | m | 500.0 | 最大射线追踪距离 |
| rayOffset | m | 0 | 射线起点偏移 |

**植被冠层路径长度计算：**

使用 `CalculateCanopyPathLength` 方法，通过 `MeshLine` 射线与树木简化冠层模型求交，获取所有进出交点参数，计算几何路径长度：

$$s = t_{\text{exit}} - t_{\text{entry}}$$

其中 $t_{\text{entry}}$ 为射线进入冠层的第一个有效交点参数，$t_{\text{exit}}$ 为离开冠层的最后一个有效交点参数。

**核心方法：**

| 方法 | 功能 | 状态 |
|:---|:---|:---|
| `CalculateRayDNITransmission()` | 计算射线穿过所有障碍物的累积 DNI 透射率 [0, 1] | **新增（2026-06-15）** |
| `ClassifyRayHit()` | 射线击中分类（单障碍物，取最近） | **已弃用**，保留向后兼容 |
| `CalculateCanopyPathLength()` | 计算光线穿过冠层的几何路径长度 $s$ | 不变 |
| `CalculateDNIExposureFactor()` | 单点有效 DNI 暴露因子计算 | 内部调用 `CalculateRayDNITransmission` |
| `CalculateDNIExposureFactorsBatch()` | 批量有效 DNI 暴露因子计算（并行） | 内部调用 `CalculateDNIExposureFactor` |

### 2.2 全 4π 球面视角系数分解（五分量）

将人体周围的全空间（4π 球面）分解为**五个互斥分量**，满足守恒条件：

$$F_{\text{SVF}} + F_{\text{GVF}} + F_{\text{OVF,opaque}} + F_{\text{TVF}} + F_{\text{TRVF}} = 1.0$$

| 分量 | 符号 | 说明 |
|:---:|:---:|:---|
| 天空视角因子 | $F_{\text{SVF}}$ | 可见天空（Z > 0，无遮挡） |
| 地面视角因子 | $F_{\text{GVF}}$ | 可见地面（Z < 0，无遮挡） |
| 不透光障碍物视角因子 | $F_{\text{OVF,opaque}}$ | 仅被不透光物体遮挡 |
| 树木视角因子 | $F_{\text{TVF}}$ | 被树木细节网格遮挡 |
| 半透明视角因子 | $F_{\text{TRVF}}$ | 被半透明遮阳网格遮挡 |

**算法流程：**

1. 使用 Fibonacci（黄金角）螺旋生成 $N$ 个均匀分布的全球面方向向量
2. 从分析点（眼高度）沿每个方向发射射线
3. 按优先级逐级分类（**不透光 > 树木细节 > 半透明遮阳**）：
   - 射线命中不透光物体 → 计入 $F_{\text{OVF,opaque}}$
   - 否则命中树木细节网格 → 计入 $F_{\text{TVF}}$
   - 否则命中半透明遮阳网格 → 计入 $F_{\text{TRVF}}$
   - 均未被遮挡且 $Z > 0$ → 计入 $F_{\text{SVF}}$
   - 均未被遮挡且 $Z < 0$ → 计入 $F_{\text{GVF}}$

> **说明：** 树木简化冠层模型（TreeCanopy）**不参与**视角因子遮挡判断，仅用于 Beer-Lambert 路径长度计算；视角因子遮挡仅基于 Opaque + TreeDetail + TranslucentShade 三类网格。

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

## 3. MRT Calculator 组件（OutdoorMRT.cs）

### 3.1 输入参数

| 索引 | 参数 | 类型 | 说明 |
|:---:|:---:|:---:|:---|
| 0 | EPW File | Text | EPW 气象文件路径 |
| 1 | Analysis Points | Point3d List | 地面分析点（必须位于地表 Z=0，非气象高度） |
| 2 | Obstacle Set | Generic | **ObsSet（2026-06-15）**：仅接受 ObstacleSet 封装数据。连接 ObsSet 组件。支持不透光建筑、树木冠层透射（Beer-Lambert）、半透明遮阳及各类表面温度。**不再接受 List&lt;Brep&gt;/List&lt;Mesh&gt; 直接输入** |
| 3 | MRT Settings | Generic | MRT 配置设置（可选，默认新建设置） |
| 4 | Time Settings | Generic | 模拟时间段（可选，默认全年 8760h） |
| 5 | Air Temperature (Ta) | Number Tree | 空气温度 [°C]，支持 4 种输入模式（可选） |
| 6 | Ground Temperature (Tg) | Number Tree | 地面温度 [°C]，支持 4 种输入模式（可选） |
| 7 | Run | Boolean | 设置为 true 执行模拟 |

> **重要变更（2026-06-16）：** Tsur（周围表面温度）输入端已**移除**。不透光障碍物、树木冠层、半透明遮阳的表面温度分别由 ObsSet 组件的 T_opaque、T_tree、T_trans 输入端提供（支持单值或 8760 逐时序列），未输入时回退为空气温度 $T_a$。

> **重要变更（2026-06-15）：** ObsSet 输入端（索引 2）**不再向后兼容**。仅接受 ObstacleSet 类型数据，不接受 List&lt;Brep&gt; 或 List&lt;Mesh&gt;。输入几何遮挡必须通过 ObsSet 组件预处理。

**Ta/Tg 输入模式：**

| 模式 | 数据量 | 说明 |
|:---:|:---:|:---|
| 1 | 1 个值 | 统一固定温度，所有时刻所有点 |
| 2 | N 个值（N=点数） | 逐点固定温度 |
| 3 | 8760 个值 | 逐时变化温度（所有点共享） |
| 4 | N×8760 Tree | 逐点逐时变化温度 |

### 3.2 输出参数（17 项）

**MRT 相关（索引 0–8）：**

| 索引 | 参数 | 说明 |
|:---:|:---:|:---|
| 0 | MRT | 平均辐射温度 [°C]，每点每时（Tree，分支=点） |
| 1 | dTsw | 短波辐射引起的 MRT 增量 [°C] |
| 2 | dTlw | 长波辐射总 MRT 增量 [°C]（五分量之和） |
| 3 | dTlw_sky | 天空长波分量贡献 [°C]，$c_{\text{lw}} \cdot F_{\text{SVF}} \cdot (T_{\text{sky}} - T_{\text{ref}})$ |
| 4 | dTlw_grd | 地面长波分量贡献 [°C]，$c_{\text{lw}} \cdot F_{\text{GVF}} \cdot (T_g - T_{\text{ref}})$ |
| 5 | dTlw_opq | 不透光障碍物长波分量贡献 [°C]，$c_{\text{lw}} \cdot F_{\text{OVF,opaque}} \cdot (T_{\text{opaque}} - T_{\text{ref}})$ |
| 6 | dTlw_tree | 树木冠层长波分量贡献 [°C]，$c_{\text{lw}} \cdot F_{\text{TVF}} \cdot (T_{\text{canopy}} - T_{\text{ref}})$ |
| 7 | dTlw_trans | 半透明遮阳长波分量贡献 [°C]，$c_{\text{lw}} \cdot F_{\text{TRVF}} \cdot (T_{\text{trans}} - T_{\text{ref}})$ |
| 8 | HrMRT | 所有点逐时平均 MRT [°C]（List） |

**视角因子（索引 9–13，List，每点一个值）：**

| 索引 | 参数 | 说明 |
|:---:|:---:|:---|
| 9 | SVF | 天空视角因子 [0–1]，全 4π 采样 |
| 10 | GVF | 地面视角因子 [0–1]，全 4π 采样 |
| 11 | OVF | 不透光障碍物视角因子 [0–1]（**仅不透光**，全 4π 采样） |
| 12 | TVF | 树木视角因子 [0–1]，全 4π 采样 |
| 13 | TRVF | 半透明遮阳视角因子 [0–1]，全 4π 采样 |

**暴露因子（索引 14–15，Tree，每点每时）：**

| 索引 | 参数 | 说明 |
|:---:|:---:|:---|
| 14 | Exp | 太阳暴露因子 $f_{\text{exp}}$ [0–1]（二值：暴露/遮挡） |
| 15 | DNIExp | 有效 DNI 暴露因子 $f_{\text{DNI}}$ [0–1]，综合暴露与多障碍物透射 |

**其他（索引 16）：**

| 索引 | 参数 | 说明 |
|:---:|:---:|:---|
| 16 | SunVec | 每个分析时刻的太阳向量（List） |

---

## 4. MRT 设置组件（MRTsettings.cs）

### 4.1 输入参数

| 参数 | 标识 | 单位 | 默认值 | 说明 |
|:---:|:---:|:---:|:---:|:---|
| Use RayMan | RayMan | - | false | 使用 RayMan 模型替代 SolarCal |
| Posture | Post | - | 0 | 人体姿态：0=站立(f=0.725)，1=坐姿(f=0.696) |
| Body Absorptivity | Alpha | - | 0.7 | 短波吸收率 |
| Body Emissivity | Epsilon | - | 0.95 | 长波发射率 |
| Radiative HTC | Hr | W/(m²·K) | 6.012 | 辐射换热系数 |
| Floor Reflectance | Rho | - | 0.25 | 地面反射率 |
| Exposure Samples | NExp | - | 3 | 暴露因子射线追踪采样点数 |
| Eye Height for SVF | HSVF | m | 1.5 | 视角系数计算眼高度 |
| Body Height | HBod | m | 1.7 | 人体总高度 |
| Sky Emissivity | SkyEps | - | -1 | 天空发射率，-1=自动（露点计算） |
| SVF Sample Count | SVF_N | - | 1000 | 全 4π 球面采样数 |
| Longwave Coeff | LwCoeff | - | 0.5 | 长波线性化系数 |
| Ground Emissivity | EpsGrd | - | 0.95 | 地面长波发射率（仅 RayMan 使用） |
| Obstacle Emissivity | EpsObs | - | 0.95 | 障碍物长波发射率（仅 RayMan 使用；不透光、树冠、半透明三类表面共用） |

---

## 5. 障碍物设置组件（ObsSet.cs）

### 5.1 功能说明

ObsSet 组件用于创建分类障碍物设置集（`ObstacleSet`），为 MRT 组件、SpatialSoilThermalSimulator 组件和 RadSim 组件提供统一的障碍物输入。通过将障碍物分类为不透光物体、树木（含冠层透射）和半透明遮阳构件，实现精细化的直接辐射（DNI）计算与五分量长波辐射分解。

**核心原理（2026-06-15 修正）：**
- 当反向光线追踪的采样点被遮挡时，传统方法直接忽略该点的 DNI 贡献（$f_{\text{exp}}=0$ → DNI=0）
- 增强版方法根据障碍物类型，通过 Beer-Lambert 定律或固定透射率计算 DNI 的部分透射
- **物理修正**：当射线同时穿过多种非 Opaque 障碍物时，各项透射率**相乘**，而非仅取最近的一个
- 有效 DNI 暴露因子 $f_{\text{DNI}}$ 综合暴露与透射效应，替代原 $f_{\text{exp}}$ 用于直接辐射计算

**表面温度设置（2026-06-16 新增）：**
- ObsSet 同时为三类障碍物提供独立的表面温度输入（T_opaque / T_tree / T_trans），用于 MRT 与土壤热模块的五分量长波辐射计算
- 每个温度输入支持**单值**（全年固定）或 **8760 个值**（逐时变化）两种模式
- 未输入时回退为逐时空气温度 $T_a$
- 三个温度输入**不被 RadSim 组件读取**，仅用于 MRT 与土壤热模块

### 5.2 输入参数

| 索引 | 参数 | 标识 | 类型 | 默认值 | 说明 |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | Tree Detail | TreeDet | Mesh List | — | 树的细节模型（树叶、枝干），来自 Tree Processor 组件 |
| 1 | Tree Canopy | TreeCan | Mesh List | — | 树木简化冠层模型，用于计算光线穿过冠层的几何路径长度 |
| 2 | Leaf Area Density | LAD | Number | 1.0 | 叶面积密度 [m²/m³]，表示单位冠层体积的叶面积（约束范围 0.01–50） |
| 3 | Extinction Coeff | k | Number | 0.5 | 太阳辐射消光系数 [-]，Beer-Lambert 定律参数，典型范围 0.5–0.8（约束范围 0.01–1.0） |
| 4 | Tree Temperature | T_tree | Number List | — | 树冠表面温度 [°C]，单值或 8760 逐时值（可选；未输入回退为 $T_a$） |
| 5 | Translucent Shade | TransShade | Mesh List | — | 半透明遮阳构件（穿孔金属板、织物等） |
| 6 | Transmittance | τ | Number | 0.05 | 遮阳构件短波透射率 [-]，范围 0.0–1.0 |
| 7 | Translucent Temperature | T_trans | Number List | — | 半透明遮阳表面温度 [°C]，单值或 8760 逐时值（可选；未输入回退为 $T_a$） |
| 8 | Opaque Objects | Opaque | Mesh List | — | 不透光物体（建筑、围墙等），完全阻挡直接辐射 |
| 9 | Opaque Temperature | T_opaque | Number List | — | 不透光表面温度 [°C]，单值或 8760 逐时值（可选；未输入回退为 $T_a$） |

全部 10 个输入端均为可选（Optional）。

**典型透射率参考值：**

| 材料类型 | 透射率范围 |
|:---:|:---:|
| 穿孔金属板 | 0.05–0.15 |
| 遮阳织物 | 0.02–0.30 |
| 聚碳酸酯板 (PC) | 0.60–0.85 |
| 玻璃 | 0.70–0.90 |

### 5.3 输出参数

| 索引 | 参数 | 说明 |
|:---:|:---:|:---|
| 0 | ObsSet | 分类障碍物设置集（含各类表面温度），连接至 MRT/SpSoilSim/RadSim 组件的 ObsSet 输入端 |

### 5.4 使用注意事项

1. **Tree Detail vs Tree Canopy**：Tree Detail 用于射线击中检测（判断点是否在树荫下）和 **SVF 视野因子遮挡判断**；Tree Canopy **仅**用于 Beer-Lambert 路径长度计算（光线穿过冠层的几何距离 $s$），**不参与 SVF 遮挡**。SVF 计算仅使用 Opaque + TreeDetail + TranslucentShade 的物理遮挡，TreeCanopy 因是收缩包裹而不应作为遮挡体。两者缺一不可——若提供了 Tree Detail 但未提供 Tree Canopy，DNI 透射将使用零路径长度。

2. **严格类型要求（2026-06-15）**：MRT、SpSoilSim 和 RadSim 组件的 ObsSet 输入端**仅接受 ObstacleSet 类型数据**，不再向后兼容 List&lt;Brep&gt; 或 List&lt;Mesh&gt; 直接输入。几何遮挡必须通过 ObsSet 组件预处理。

3. **Mesh 输入**：ObsSet 组件本身支持直接输入 Mesh 类型。若输入 Surface 或 Brep 类型，Grasshopper 可隐式转换为 Mesh。

4. **温度输入模式**：T_opaque / T_tree / T_trans 输入 ≥8760 个值时按逐时序列使用；输入 1 个值时全年固定；未输入时下游组件自动回退为逐时空气温度。

---

## 6. 参考文献

1. ASHRAE Standard 55-2017: Thermal Environmental Conditions for Human Occupancy. American Society of Heating, Refrigerating and Air-Conditioning Engineers, Atlanta, GA, 2017.

2. Arens, E., et al. (2015). Modeling the comfort effects of short-wave solar radiation indoors. *Building and Environment*, 88, 3-9. https://doi.org/10.1016/j.buildenv.2014.09.004

3. Matzarakis, A., Rutz, F., & Mayer, H. (2010). Modelling radiation fluxes in simple and complex environments: basics of the RayMan model. *International Journal of Biometeorology*, 54, 131-139. https://doi.org/10.1007/s00484-009-0261-5

4. ISO 7726:1998. Ergonomics of the thermal environment — Instruments for measuring physical quantities. International Organization for Standardization, Geneva, 1998.
