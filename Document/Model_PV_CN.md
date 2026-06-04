# 光伏（PV）性能模拟模块

本模块包含从太阳辐射计算到光伏系统发电量估算的完整模拟链，涵盖太阳几何计算、天空散射模型、双面光伏模型、组件温度模型以及逆变器/MPPT模型，基于EPW气象数据进行逐时性能模拟。

---

## 1. 太阳几何计算（SolarGeometry.cs）

### 1.1 入射角修正（IAM）

基于 ASHRAE 简化模型：

$$\text{IAM}(\theta) = 1 - b_0 \left( \frac{1}{\cos\theta} - 1 \right)$$

其中：
- $\theta$：入射角 [°]
- $b_0$：IAM系数（标准光伏玻璃默认 0.05，减反射涂层 0.03）

当 $\cos\theta \leq 0.01$ 时，$\text{IAM} = 0$。

### 1.2 各向同性散射模型

基于 Liu & Jordan（1963）：

$$I_{d,\text{tilted}} = I_{\text{DH}} \cdot \frac{1 + \cos\beta}{2}$$

其中：
- $I_{\text{DH}}$：水平面散射辐射 [W/m²]
- $\beta$：表面倾斜角 [rad]

### 1.3 地面反射辐射

$$I_{\text{ground}} = I_{\text{GH}} \cdot \rho_g \cdot \frac{1 - \cos\beta}{2}$$

其中：
- $I_{\text{GH}}$：水平面总辐射 [W/m²]
- $\rho_g$：地面反照率（Albedo）

### 1.4 倾斜面直接辐射

$$I_{\text{direct}} = I_{\text{DN}} \cdot \cos\theta \cdot \text{IAM}(\theta)$$

其中：
- $I_{\text{DN}}$：直接法向辐射 [W/m²]
- $\theta$：入射角 [rad]

当 $\cos\theta \leq 0$ 时（太阳在表面背面），$I_{\text{direct}} = 0$。

### 1.5 太阳向量计算

从地理方位角（0°=北，顺时针）和天顶角计算太阳方向单位向量（坐标系：X=东，Y=北，Z=上）：

$$\vec{s} = (\sin A \cdot \sin Z,\; \cos A \cdot \sin Z,\; \cos Z)$$

其中 $A$ 为地理方位角，$Z$ 为天顶角。

### 1.6 半球/全球面方向采样

**Fibonacci 上半球采样**（用于SVF计算）：

$$z_i = \sqrt{1 - \frac{i}{N}}, \quad r_i = \sqrt{1 - z_i^2}, \quad \theta_i = \phi \cdot i$$

**Fibonacci 全球面采样**（用于MRT的4π视角系数）：

$$z_i = 1 - \frac{2i + 1}{N}, \quad r_i = \sqrt{1 - z_i^2}, \quad \theta_i = \phi \cdot i$$

其中 $\phi = \pi(3 - \sqrt{5})$ 为黄金角。

---

## 2. Perez 天空散射模型（PerezSkyModel.cs）

基于 Perez, Ineichen 等人（1987, 1990）的天空各向异性散射模型。

### 2.1 主方程

$$I_{d,\text{tilted}} = I_{\text{DH}} \cdot \left[ (1 - F_1) \frac{1 + \cos\beta}{2} + F_1 \frac{a}{b} + F_2 \sin\beta \right]$$

其中：
- $F_1$：环日明亮系数
- $F_2$：地平线明亮系数
- $a = \max(0, \cos\theta)$
- $b = \max(\cos 85°, \cos Z)$，$Z$ 为天顶角

### 2.2 天空亮度参数

天空清晰度参数 $\varepsilon$：

$$\varepsilon = \frac{(I_{\text{DH}} + I_{\text{DN}}) / I_{\text{DH}} + 5.535 \times 10^{-6} \cdot Z^3}{1 + 5.535 \times 10^{-6} \cdot Z^3}$$

大气清澈度参数 $\Delta$：

$$\Delta = \frac{I_{\text{DH}} \cdot m}{1367}, \quad m = \frac{1}{\cos Z}$$

### 2.3 系数 $F_1$ 和 $F_2$

$$F_1 = \max(0,\; f_{11} + f_{12} \cdot \Delta + f_{13} \cdot Z_{\text{rad}}) \cdot c_{\text{cs}}$$

$$F_2 = \max(0,\; f_{21} + f_{22} \cdot \Delta + f_{23} \cdot Z_{\text{rad}}) \cdot c_{\text{hb}}$$

其中 $c_{\text{cs}}$ 和 $c_{\text{hb}}$ 分别为环日和地平线系数缩放因子。

**8组系数表（按 $\varepsilon$ 分档）：**

| 档位 | $\varepsilon$ 范围 | 天空条件 |
|:---:|:---:|:---|
| 1 | < 1.065 | 阴天 |
| 2 | 1.065 – 1.230 | 阴/少云 |
| 3 | 1.230 – 1.500 | 少云 |
| 4 | 1.500 – 1.950 | 少云 |
| 5 | 1.950 – 2.800 | 晴/少云 |
| 6 | 2.800 – 4.500 | 晴天 |
| 7 | 4.500 – 6.200 | 很晴 |
| 8 | $\geq$ 6.200 | 极晴 |

### 2.4 回退机制

当 $I_{\text{DH}} \leq 0$ 或 $Z \geq 90°$ 或 $I_{\text{DH}} / I_{\text{GH}} < \text{阈值}$（默认 0.01）时，回退到各向同性模型。

当 $F_{\text{SVF}} < 0.3$（重度遮挡）时，Perez结果按 $F_{\text{SVF}}$ 缩放。

---

## 3. 双面光伏模型（BifacialModel.cs）

### 3.1 背面辐照度

$$I_{\text{rear}} = \big( I_{\text{GH}} \cdot \rho_g \cdot F_{r \to g} \cdot S_f + I_{\text{DH}} \cdot F_{r \to \text{sky}} \cdot F_{\text{SVF,rear}} \big) \cdot f_{\text{rg}}$$

其中：
- $F_{r \to g} = (1 + \cos\beta)/2$：背面到地面的视角系数
- $F_{r \to \text{sky}} = (1 - \cos\beta)/2$：背面到天空的视角系数
- $S_f$：行间遮挡修正因子
- $f_{\text{rg}}$：背面增益经验修正因子

**行间遮挡修正因子：**

$$S_f = \frac{1}{1 + 0.5 \cdot (H / D) \cdot \sin\beta}$$

其中 $H$ 为组件安装高度，$D$ 为行间距。结果被约束在 [0.1, 1.0]。

### 3.2 双面增益功率

$$P_{\text{rear}} = I_{\text{rear}} \cdot A_{\text{gross}} \cdot \phi_{\text{active}} \cdot \eta_{\text{front}} \cdot \phi_{\text{bifacial}}$$

其中：
- $A_{\text{gross}}$：组件毛面积 [m²]
- $\phi_{\text{active}}$：有效面积比（默认 0.9）
- $\eta_{\text{front}}$：温度修正后的正面效率
- $\phi_{\text{bifacial}}$：双面系数（默认 0.7）

### 3.3 双面增益比

$$\text{BG} = \frac{I_{\text{rear}}}{I_{\text{front}}} \cdot \phi_{\text{bifacial}}$$

---

## 4. 光伏组件温度模型（PVTemperatureModel.cs）

### 4.1 Faiman 模型（优先使用）

$$T_{\text{cell}} = T_a + \frac{I_{\text{POA}}}{U_0 + U_1 \cdot v_w}$$

其中：
- $T_a$：环境温度 [°C]
- $I_{\text{POA}}$：倾斜面辐照度 [W/m²]
- $v_w$：风速 [m/s]
- $U_0, U_1$：与安装方式相关的热损失系数

**不同安装方式的热损失系数：**

| 安装方式 | $U_0$ [W/(m²·K)] | $U_1$ [W/(m²·K)/(m/s)] |
|:---:|:---:|:---:|
| FreeStanding（独立支架） | 25.0 | 6.84 |
| RoofMounted（屋顶安装） | 20.0 | 5.70 |
| BuildingIntegrated（BIPV） | 15.0 | 4.50 |
| Concentrator（聚光） | 30.0 | 8.00 |

### 4.2 NOCT 模型（无风速时回退）

$$T_{\text{cell}} = T_a + \frac{(\text{NOCT} - 20)}{800} \cdot I_{\text{POA}} \cdot R_{\text{thermal}}$$

其中热比 $R_{\text{thermal}}$ 考虑效率修正：

$$R_{\text{thermal}} = \frac{1 - \eta}{1 - \eta_{\text{ref}}}$$

$\eta_{\text{ref}}$ 为NOCT测试时的参考效率（默认 0.10），结果被约束在 [0.5, 2.0]。

### 4.3 Sandia 温度模型

$$T_{\text{module}} = T_a + I_{\text{POA}} \cdot e^{a + b \cdot v_w}$$

$$T_{\text{cell}} = T_{\text{module}} + \frac{I_{\text{POA}}}{1000} \cdot \Delta T$$

默认参数：$a = -3.47$，$b = -0.0594$，$\Delta T = 3$ °C。

### 4.4 温度修正效率

$$\eta(T) = \eta_{\text{STC}} \cdot \big[ 1 + \gamma \cdot (T_{\text{cell}} - T_{\text{STC}}) \big]$$

其中 $\gamma$ 为最大功率温度系数 [%/°C 或 1/°C]。

---

## 5. 逆变器与MPPT模型（InverterMPPTModel.cs）

### 5.1 PVWatts 逆变器模型

基于 Dobos（2014）的 PVWatts Version 5 模型。

**部分负载效率曲线：**

$$\zeta = \frac{P_{\text{dc}}}{P_{\text{dc0}}}, \quad P_{\text{dc0}} = \frac{P_{\text{ac,rated}}}{\eta_{\text{nom}}}$$

$$\text{PLR} = \begin{cases} -0.0162\zeta - 0.0059/\zeta + 0.9858 & 0.01 \leq \zeta \leq 1.2 \\ \zeta/0.01 \cdot \text{PLR}|_{\zeta=0.01} & \zeta < 0.01 \\ \text{PLR}|_{\zeta=1.2} \cdot \big[1 - 0.05(\zeta - 1.2)\big] & \zeta > 1.2 \end{cases}$$

$$\eta_{\text{op}} = \eta_{\text{nom}} \cdot \text{PLR}$$

$$P_{\text{ac}} = P_{\text{dc}} \cdot \eta_{\text{op}}$$

**削峰（Clipping）：** 若 $P_{\text{ac}} > P_{\text{ac,rated}} \times \text{ClipRatio}$，则限制输出并记录削峰损失。

**夜间待机损耗：** 若输出 $<$ TareLoss，则输出置 0。

### 5.2 组串电压计算

$$V_{\text{string}} = N_s \cdot V_{\text{mp}} \cdot \big[1 + \gamma_V \cdot (T_{\text{cell}} - 25)\big]$$

$$V_{\text{oc,string}} = N_s \cdot V_{\text{oc}} \cdot \big[1 + \gamma_{V_{\text{oc}}} \cdot (T_{\text{cell}} - 25)\big]$$

MPPT窗口检查：若 $V_{\text{string}} < V_{\text{min}}$ 或 $V_{\text{string}} > V_{\text{max}}$，或 $V_{\text{oc,string}} > V_{\text{max}} + 100$ V，则逆变器停机。

---

## 6. 辐射模拟引擎（NeosRadSim.cs）

### 6.1 输入参数

| 索引 | 参数 | 类型 | 说明 |
|:---:|:---:|:---:|:---|
| 0 | EPW File | Text | EPW气象文件路径 |
| 1 | Obstacles | Brep List | 环境障碍物（可选） |
| 2 | Measurement Surfaces | Brep List | 待测表面（PV面板或传感器面） |
| 3 | Time Settings | Generic | 时间设置（可选） |
| 4 | PV Settings | Generic | PV组件设置（可选） |
| 5 | Temperature Settings | Generic | 温度模型设置（可选） |
| 6 | Sky Model Settings | Generic | 天空模型设置（可选） |
| 7 | Raytracing Settings | Generic | 光线追踪设置（可选） |
| 8 | Inverter Settings | Generic | 逆变器设置（可选） |
| 9 | Output Folder | Text | 结果输出文件夹（可选） |
| 10 | Run | Boolean | 执行开关 |

### 6.2 输出结果（6个分类文件）

| 文件 | 内容 |
|:---:|:---|
| GeometryResult.txt | 网格数据、面中心、法向量、面积、倾斜角、太阳向量 |
| ViewResult.txt | 正面/背面SVF、正面/背面障碍物视角系数 |
| SunRadiationResult.txt | 日照时数、逐时/累计辐射量（正面/背面） |
| PVInfoResult.txt | 电池温度、有效辐照度、削峰损失、逆变器损失 |
| DCResult.txt | 逐时/累计DC发电量（正面/背面） |
| ACResult.txt | 逐时/累计AC发电量（正面/背面） |

### 6.3 模拟流程

1. 读取EPW气象数据 → 解析经纬度、时区、高程
2. 表面网格化 → 使用 Geometry.Core 生成规则网格
3. 计算天空视角系数（SVF）→ Fibonacci半球采样 + 光线追踪
4. 若为双面模式 → 额外计算背面SVF
5. **逐时循环：**
   - NREL-SPA计算太阳位置
   - 阴影检测（直接太阳射线追踪）
   - 倾斜面辐照度 = 直接（含IAM）+ 散射（Perez或各向同性）+ 地面反射
   - 组件温度（Faiman/NOCT/Sandia）
   - 温度修正效率
   - DC发电量
   - 若为双面模式 → 背面辐照度 + 双面增益
   - 若为逆变器模式 → DC-AC转换（含削峰和MPPT窗口检查）
6. 保存6类结果文件

---

## 7. 设置组件参数汇总

### 7.1 PV Settings（PVSettings.cs）

| 参数 | 单位 | 默认值 | 说明 |
|:---:|:---:|:---:|:---|
| Enable Bifacial | - | false | 启用双面光伏计算 |
| Grid Resolution | m | 0.5 | 分析网格尺寸 |
| Efficiency | - | 0.20 | STC组件效率 |
| Active Area Ratio | - | 0.9 | 有效面积比 |
| Albedo | - | 0.2 | 地面反照率 |
| Bifaciality Factor | - | 0.7 | 双面系数 |
| Row Spacing | m | -1 | 行间距（-1=自动推断） |
| Module Height | m | -1 | 组件高度（-1=自动推断） |
| Rear Gain Factor | - | 1.0 | 背面增益经验修正 |
| IAM Coefficient | - | 0.05 | 入射角修正系数 $b_0$ |
| System Loss Factor | - | 0.14 | 系统总损失率（14%） |

### 7.2 Temperature Settings（PVTemperatureModelSettings.cs）

| 参数 | 单位 | 默认值 | 说明 |
|:---:|:---:|:---:|:---|
| Enable Temperature Model | - | true | 启用温度修正 |
| NOCT | °C | 45 | 标称工作电池温度 |
| Temp Coefficient | %/°C | -0.4 | 功率温度系数 |
| Temp Coeff Is Percent | - | true | 系数是否为百分比形式 |
| Wind Speed Factor | - | 1.0 | 风速乘数 |
| Mounting Type | - | 0 | 0=独立/1=屋顶/2=BIPV/3=无风修正 |
| NOCT Reference Efficiency | - | 0.10 | NOCT测试参考效率 |

### 7.3 Sky Model Settings（SkyModelSettings.cs）

| 参数 | 单位 | 默认值 | 说明 |
|:---:|:---:|:---:|:---|
| Use Perez Model | - | false | 使用Perez各向异性模型 |
| Horizon Coefficient | - | 1.0 | 地平线明亮分量缩放 |
| Circumsolar Coefficient | - | 1.0 | 环日明亮分量缩放 |
| Diffuse Ratio Threshold | - | 0.01 | DHI/GHI最小阈值 |

### 7.4 Raytracing Settings（RaytracingSettings.cs）

| 参数 | 单位 | 默认值 | 说明 |
|:---:|:---:|:---:|:---|
| SVF Sample Count | - | 500 | 半球采样方向数 |
| SVF Ray Offset | m | 0.01 | SVF射线起点偏移 |
| Shadow Ray Offset | m | 0.001 | 阴影射线起点偏移 |
| Max Trace Distance | m | 10000 | 最大追踪距离 |

### 7.5 Inverter MPPT Settings（InverterMPPTSettings.cs）

| 参数 | 单位 | 默认值 | 说明 |
|:---:|:---:|:---:|:---|
| Enable Inverter Model | - | true | 启用DC-AC转换 |
| Rated AC Power | W | 10000 | 逆变器额定AC功率 |
| Nominal Efficiency | - | 0.96 | 标称效率 |
| MPPT Min Voltage | V | 200 | 最小MPPT电压 |
| MPPT Max Voltage | V | 800 | 最大MPPT电压 |
| Clipping Ratio | - | 1.0 | AC削峰比 |
| Night Tare Loss | W | 5 | 夜间待机损耗 |
| Nominal Vmp | V | 35 | 组件STC最大功率点电压 |
| Temp Coeff Voltage | 1/°C | -0.004 | Vmp温度系数 |
| Nominal Voc | V | 42 | 组件STC开路电压 |
| Temp Coeff Voc | 1/°C | -0.003 | Voc温度系数 |

### 7.6 Time Settings（TimeSettings.cs）

| 参数 | 单位 | 默认值 | 说明 |
|:---:|:---:|:---:|:---|
| Start Month | - | 1 | 起始月份 |
| Start Day | - | 1 | 起始日期 |
| Start Hour | - | 0 | 起始小时（0–23） |
| End Month | - | 12 | 结束月份 |
| End Day | - | 31 | 结束日期 |
| End Hour | - | 23 | 结束小时（0–23） |

---

## 8. 参考文献

1. Reda, I., & Andreas, A. (2004). Solar position algorithm for solar radiation applications. *Solar Energy*, 76(5), 577-589. https://doi.org/10.1016/j.solener.2003.12.003

2. Liu, B.Y.H., & Jordan, R.C. (1963). The long-term average performance of flat-plate solar energy collectors. *Solar Energy*, 7(2), 53-74. https://doi.org/10.1016/0038-092X(63)90006-9

3. Perez, R., Seals, R., Ineichen, P., Stewart, R., & Menicucci, D. (1987). A new simplified version of the Perez diffuse irradiance model for tilted surfaces. *Solar Energy*, 39(3), 221-231. https://doi.org/10.1016/0038-092X(87)80031-2

4. Perez, R., Ineichen, P., Seals, R., Michalsky, J., & Stewart, R. (1990). Modeling daylight availability and irradiance components from direct and global irradiance. *Solar Energy*, 44(5), 271-289. https://doi.org/10.1016/0038-092X(90)90055-H

5. Marion, B., MacAlpine, S., Deline, C., Asgharzadeh, A., Toor, F., Riley, D., Stein, J., & Hansen, C. (2017). A practical irradiance model for bifacial PV modules. In *2017 IEEE 44th Photovoltaic Specialist Conference (PVSC)*, pp. 1537-1542. https://doi.org/10.1109/PVSC.2017.8366263

6. Fuentes, M.K. (1987). A Simplified Thermal Model for Flat-Plate Photovoltaic Arrays. *Sandia National Laboratories Report*, SAND85-0330, Albuquerque, NM.

7. Faiman, D. (2008). Assessing the outdoor operating temperature of photovoltaic modules. *Progress in Photovoltaics: Research and Applications*, 16(4), 307-315. https://doi.org/10.1002/pip.813

8. King, D.L., Boyson, W.E., & Kratochvil, J.A. (2004). Photovoltaic Array Performance Model. *Sandia National Laboratories Report*, SAND2004-3535, Albuquerque, NM.

9. IEC 61215-1:2016. Terrestrial photovoltaic (PV) modules — Design qualification and type approval — Part 1: Test requirements. International Electrotechnical Commission, Geneva, 2016.

10. Dobos, A.P. (2014). PVWatts Version 5 Manual. *National Renewable Energy Laboratory Technical Report*, NREL/TP-6A20-62641, Golden, CO. https://doi.org/10.2172/1158421
