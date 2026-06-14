# MRT (Mean Radiant Temperature) 模块

本模块用于计算室外环境中人体的平均辐射温度（MRT），基于反向光线追踪的建筑/植被遮挡分析，支持SolarCal（ASHRAE 55）和RayMan两种计算模型，并采用全4π球面视角系数分解实现物理上正确的长波辐射计算。

---

## 1. MRT 物理模型（MRTmodel.cs）

### 1.1 SolarCal 模型（ASHRAE 55）

基于 ASHRAE Standard 55 的 SolarCal 模型，将短波太阳辐射增量与三方向长波辐射分解相结合。

**短波辐射增量：**

人体接收的太阳辐射通量由直接辐射、散射辐射和地面反射三部分组成：

$$I_{\text{body}} = f_{\text{exp}} \cdot f_p(\gamma) \cdot I_{\text{DN}} + \frac{1}{2} f \cdot F_{\text{SVF}} \cdot I_{\text{DH}} + \frac{1}{2} f \cdot F_{\text{GVF}} \cdot \rho_g \cdot I_{\text{GH}}$$

其中：
- $f_{\text{exp}}$：暴露因子（0–1），由反向光线追踪计算
- $f_p(\gamma)$：太阳投影系数（见下式）
- $I_{\text{DN}}$：直接法向辐射 [W/m²]
- $I_{\text{DH}}$：水平面散射辐射 [W/m²]
- $I_{\text{GH}}$：水平面总辐射 [W/m²]
- $f$：姿态效率因子（站立 $f=0.725$，坐姿 $f=0.696$）
- $F_{\text{SVF}}$：天空视角系数
- $F_{\text{GVF}}$：地面视角系数
- $\rho_g$：地面反射率

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

直接太阳辐射项（仅当 $I_{\text{DN}} > 0$ 且 $f_{\text{exp}} > 0$ 时）：

$$I_{\text{direct}} = f_{\text{exp}} \cdot f_p(\gamma) \cdot I_{\text{DN}}$$

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
| 2 | Obstacles | Brep List | 障碍物/环境几何体（可选） |
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
| 4 | Exp | 太阳暴露因子 [0–1]，每点每时 |
| 5 | dT_sw | 短波辐射引起的MRT增量 [°C] |
| 6 | dT_lw | 长波辐射总MRT增量 [°C] |
| 7 | dT_lw_sky | 天空长波分量贡献 [°C] |
| 8 | dT_lw_grd | 地面长波分量贡献 [°C] |
| 9 | dT_lw_obs | 障碍物长波分量贡献 [°C] |
| 10 | HrMRT | 所有点逐时平均MRT [°C] |
| 11 | SunVec | 每个分析时刻的太阳向量 |

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

## 5. 参考文献

1. ASHRAE Standard 55-2017: Thermal Environmental Conditions for Human Occupancy. American Society of Heating, Refrigerating and Air-Conditioning Engineers, Atlanta, GA, 2017.

2. Arens, E., et al. (2015). Modeling the comfort effects of short-wave solar radiation indoors. *Building and Environment*, 88, 3-9. https://doi.org/10.1016/j.buildenv.2014.09.004

3. Matzarakis, A., Rutz, F., & Mayer, H. (2010). Modelling radiation fluxes in simple and complex environments: basics of the RayMan model. *International Journal of Biometeorology*, 54, 131-139. https://doi.org/10.1007/s00484-009-0261-5

4. ISO 7726:1998. Ergonomics of the thermal environment — Instruments for measuring physical quantities. International Organization for Standardization, Geneva, 1998.
