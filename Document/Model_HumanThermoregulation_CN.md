# 人体热调节模型（Human Thermoregulation）模块

本模块基于 Fiala 多节段生理模型（Fiala 1998/2001/2012）采用瞬态逼近法求解等效温度，输出**生理等效温度（EqT）**与**动态热感觉（DTS）**。模型将人体离散为 12 个节段、每节段 5 层组织，迭代求解生物热方程（Bioheat Equation），并耦合颤抖、皮肤血流、出汗等主动体温调节机制。瞬态逼近法在中性初始条件下运行可配置时长的瞬态模拟，通过隐式欧拉法推进生物热方程，避免稳态解在极端条件下可能不存在的问题。等效温度通过在当前活动水平与参考环境之间匹配生理应变指标 S = (Tsk - 34.4) + 3.0×(Tcore - 37.0) 的二分搜索获得，使结果可跨不同代谢率进行比较。

**当前版本要点：**
1. **Fiala 12 节段 × 5 层生理模型**：头部、颈部、肩部、手臂、手、胸腔、腹部、腿部、足、面部、前额、骨盆，每层包含脑/肺、骨、肌肉、内脏、脂肪、内皮肤、外皮肤等组织属性。
2. **主动体温调节系统**：非线性 tanh 控制方程描述颤抖产热、血管收缩、血管舒张与出汗散热，并支持年龄衰减与性别基础代谢修正。
3. **等效温度 EqT 计算**：基于 PET/UTCI 参考环境，采用瞬态逼近法，在中性条件下运行可配置时长的瞬态模拟，通过隐式欧拉法求解生物热方程，匹配 Tsk + 3×Tcore 生理应变指标，将任意活动水平统一换算到标准参考人体的活动状态。
4. **服装模型可选**：支持 UTCI 自适应服装模型（Havenith 2012）或用户手动指定服装热阻。
5. **并行批量求解**：支持对多个气象/人体状态组合进行并行计算。

---

## 1. 核心物理模型（HumanThermoregulationSimulator.cs）

### 1.1 人体几何与节段划分

模型将人体划分为 12 个节段，每个节段简化为球体（头部、面部、前额）或圆柱体（其余节段）。各节段由核心半径 $R_c$ 到外表面半径 $R$ 的 5 层节点构成，每层厚度按 `Frac` 数组分配。节段表面积 $A$、体积 $V$ 与对流系数（自然对流 $A_{\text{nat}}$、强制对流 $A_{\text{frc}}$、混合对流 $A_{\text{mix}}$）均来自 Fiala（1998）附录表 A.1。

12 个节段及其几何特征：

| 节段 | 类型 | 外半径 R [m] | 核心半径 Rc [m] | 表面积 A [m²] | 视角因子 Vf |
|:---:|:---:|:---:|:---:|:---:|:---:|
| Head | 球体 | 0.086 | 0.040 | 0.092 | 0.95 |
| Neck | 圆柱 | 0.062 | 0.035 | 0.070 | 0.90 |
| Shoulders | 圆柱 | 0.075 | 0.045 | 0.100 | 0.75 |
| Arms | 圆柱 | 0.044 | 0.022 | 0.280 | 0.85 |
| Hands | 圆柱 | 0.025 | 0.012 | 0.078 | 0.88 |
| Thorax | 圆柱 | 0.135 | 0.085 | 0.240 | 0.82 |
| Abdomen | 圆柱 | 0.130 | 0.080 | 0.210 | 0.80 |
| Legs | 圆柱 | 0.072 | 0.038 | 0.580 | 0.78 |
| Feet | 圆柱 | 0.032 | 0.016 | 0.110 | 0.70 |
| Face | 球体 | 0.045 | 0.025 | 0.025 | 0.95 |
| Forehead | 球体 | 0.042 | 0.022 | 0.016 | 0.95 |
| Pelvis | 圆柱 | 0.120 | 0.070 | 0.150 | 0.75 |

5 层组织依次为：

| 层索引 | 组织层 | 说明 |
|:---:|:---:|:---|
| 0 | 核心层 | 脑/肺组织（头部/面部/前额）或等效核心组织 |
| 1 | 肌肉层 | 主要产热与颤抖源 |
| 2 | 骨层 | 低代谢支撑组织 |
| 3 | 内皮肤层 | 受血管舒张/收缩调控的皮肤血流层 |
| 4 | 外皮肤层 | 与环境进行对流、辐射、蒸发的界面 |

### 1.2 瞬态生物热方程（隐式欧拉法）

每个节点满足瞬态生物热方程：

$$\rho c \frac{\partial T}{\partial t} = \nabla \cdot (k \nabla T) + q_m + \beta (T_{\text{bla}} - T)$$

其中：
- $\rho$：组织密度 [kg/m³]
- $c$：组织比热容 [J/(kg·K)]
- $k$：组织导热系数 [W/(m·K)]
- $q_m$：组织体积产热率 [W/m³]，包含基础代谢、活动产热与颤抖产热
- $\beta = \rho_{\text{blood}} c_{\text{blood}} w_{\text{bl}}$：血流热容系数 [W/(m³·K)]
- $T_{\text{bla}}$：动脉血温度 [°C]
- $w_{\text{bl}}$：局部血流灌注率 [s⁻¹]

模型采用径向离散与 TDMA（Thomas 算法）求解三对角线性系统，外层节点与环境边界条件耦合。采用隐式欧拉时间推进（无条件稳定，支持可配置时间步长）。

### 1.3 对流换热系数

节段表面对流换热系数由混合对流关联式确定：

$$h_c = \max\left( \sqrt[3]{h_{\text{nat}}^3 + h_{\text{frc}}^3}, \; h_{\text{mix}} \right)$$

其中：

$$h_{\text{nat}} = A_{\text{nat}} \cdot |T_{\text{sk}} - T_a|^{0.25}$$

$$h_{\text{frc}} = A_{\text{frc}} \cdot v_a^{0.5}$$

$$h_{\text{mix}} = A_{\text{mix}} \cdot |T_{\text{sk}} - T_a|^{0.25} \cdot v_a^{0.25}$$

$v_a$ 为有效空气速度 [m/s]，综合考虑环境风速与人体行走速度：

$$v_a = \begin{cases}
\sqrt{v_{\text{wind}}^2 + v_{\text{walk}}^2}, & v_{\text{walk}} \leq 1.2 \, \text{m/s} \\
v_{\text{wind}} + 0.4 \, v_{\text{walk}}, & v_{\text{walk}} > 1.2 \, \text{m/s}
\end{cases}$$

### 1.4 辐射换热

采用线性化辐射换热系数：

$$h_r = 4 \sigma \left( 273.15 + \frac{T_{\text{sk}} + T_{\text{MRT}}}{2} \right)^3$$

其中 $\sigma = 5.67 \times 10^{-8}$ W/(m²·K⁴)。有效辐射面积为表面积乘以姿态因子 $f_{\text{eff}}$：

| 姿态 | $f_{\text{eff}}$ |
|:---:|:---:|
| 站立 | 0.80 |
| 坐姿 | 0.74 |

### 1.5 服装热阻模型

服装总热阻 $I_{\text{cl}}$ [clo] 可手动指定，也可由 UTCI 自适应服装模型自动确定。服装外表面积系数：

$$f_{\text{cl}} = 1.0 + 0.31 \, I_{\text{cl}}$$

服装蒸发效率：

$$\eta_{\text{cl}} = \frac{1}{1 + h_c I_{\text{cl}} / i_m}$$

其中 $i_m = 0.38$ 为服装透湿指数，$I_{\text{cl}}$ 需转换为 [m²·K/W]（$1 \, \text{clo} = 0.155 \, \text{m²·K/W}$）。

UTCI 自适应服装模型（Havenith 2012）分段线性插值锚点：

| 空气温度 [°C] | 服装热阻 [clo] |
|:---:|:---:|
| ≤ -5 | 1.30 |
| 5 | 1.05 |
| 15 | 0.80 |
| 26 | 0.55 |
| 32 | 0.40 |
| 36 | 0.30 |
| ≥ 36 | 0.30 |

### 1.6 蒸发散热

最大潜在蒸发散热：

$$E_{\text{max}} = h_{\text{le}} \, \eta_{\text{cl}} \, (p_{\text{sk,sat}} - p_a) \, A$$

其中 $h_{\text{le}} = h_c \cdot 0.0165$ 为蒸发换热系数 [W/(m²·Pa)]，$p_a$ 为环境水蒸气分压 [Pa]，$p_{\text{sk,sat}}$ 为皮肤表面饱和蒸气压 [Pa]。

实际蒸发散热：

$$Q_{\text{evap}} = w_{\text{total}} \, E_{\text{max}}$$

皮肤总湿润度由基础不感蒸发与出汗共同贡献：

$$w_{\text{total}} = w_{\text{insensible}} + (1 - w_{\text{insensible}}) \, w_{\text{sw}}$$

其中 $w_{\text{insensible}}$ 默认 0.06（Gagge 1971），$w_{\text{sw}}$ 由出汗率与 $E_{\text{max}}$ 计算。

### 1.7 主动体温调节系统

模型以平均皮肤温度 $T_{\text{sk,m}}$ 与下丘脑温度 $T_{\text{hy}}$ 为反馈信号，调节四项生理响应。

控制方程（Fiala 1998/2001）：

**颤抖产热 [W]**：

$$Sh = \max\left( 0, \; 10[\tanh(0.51 \Delta T_{\text{sk}} + 4.19) - 1] \Delta T_{\text{sk}} - 27.5 \Delta T_{\text{hy}} - 28.5 \right)$$

**血管收缩 [-]**：

$$Cs = \max\left( 0, \; 35[\tanh(0.29 \Delta T_{\text{sk}} + 1.11) - 1] \Delta T_{\text{sk}} - 7.7 \Delta T_{\text{hy}} \right)$$

**血管舒张 [W/K]**：

$$Dl = \max\left( 0, \; 16[\tanh(1.92 \Delta T_{\text{sk}} - 2.53) + 1] \Delta T_{\text{sk}} + 30[\tanh(3.51 \Delta T_{\text{hy}} - 1.48) + 1] \Delta T_{\text{hy}} \right)$$

**出汗率 [g/min]**：

$$Sw = \max\left( 0, \; \min\left( 30, \; [0.65 \tanh(0.82 \Delta T_{\text{sk}} - 0.47) + 1.15] \Delta T_{\text{sk}} + [5.6 \tanh(3.14 \Delta T_{\text{hy}} - 1.83) + 6.4] \Delta T_{\text{hy}} \right) \right)$$

其中：
- $\Delta T_{\text{sk}} = T_{\text{sk,m}} - T_{\text{sk,0}}$，$T_{\text{sk,0}} = 34.4$ °C
- $\Delta T_{\text{hy}} = T_{\text{hy}} - T_{\text{hy,0}}$，$T_{\text{hy,0}} = 37.0$ °C

皮肤血流修正（Fiala 1998 Eq. 4.8）：

$$\beta_{\text{skin}} = \beta_{\text{skin,0}} \cdot \frac{1 + D_{\text{dl}} Dl \, e^{-Dl/50}}{1 + D_{\text{cs}} Cs} \cdot 2^{(T_{\text{skin}} - 34.4)/10}$$

其中 $D_{\text{dl}}$、$D_{\text{cs}}$ 为节段特异性分布系数。

年龄修正：当年龄 > 65 岁时，血管收缩、血管舒张与出汗响应乘以 `AgeAttenuation` 系数（默认 0.75）。

性别修正：女性基础代谢率乘以 `SexMetFactor`（默认 0.90，ISO 8996 Annex B）。

### 1.8 呼吸热损失

按 Fiala（1998）§3.4.5：

$$C_{\text{res}} = 0.0014 \, M \, (34 - T_a) \quad [\text{W/m²}]$$

$$E_{\text{res}} = 0.0023 \, M \, (44 - p_{a,\text{mmHg}}) \quad [\text{W/m²}]$$

其中 $p_{a,\text{mmHg}} = p_{a,\text{hPa}} \times 0.75006$。总呼吸热损失 $Q_{\text{res}} = (C_{\text{res}} + E_{\text{res}}) A_d$，并从活动产热中扣除。

### 1.9 代谢产热

代谢率 $M$ [W/m²] 可手动输入，也可由步行速度自动计算（ISO 8996）：

$$M = 58 + 70 \, v_{\text{walk}}$$

其中 $v_{\text{walk}}$ 为行走速度 [m/s]。例如 $v=0$ 时 $M=58$ W/m²（静息），$v=1.1$ 时 $M=135$ W/m²（步行 4 km/h）。

机械效率：

$$\eta = \begin{cases}
0, & M \leq 1.6 \, \text{met} \\
0.39 \, \text{met} - 0.60, & M > 1.6 \, \text{met}
\end{cases}$$

有效活动产热：

$$H_{\text{wk}} = (M - 0.8 \times 58.2) \, A_d \, (1 - \eta) - Q_{\text{res}}$$

该产热按体积均匀分配到所有组织层。

### 1.10 瞬态逼近法等效温度求解

等效温度定义为：在参考环境（$M_{\text{ref}}$、$v_{\text{ref}}$、$RH_{\text{ref}}$、$I_{\text{cl,ref}}$）下，使参考人体产生与实际环境相同生理应变指标 $S$ 的空气温度。

参考人体默认（UTCI）：$M_{\text{ref}} = 135$ W/m²，$v_{\text{ref}} = 0.5$ m/s，$RH_{\text{ref}} = 50\%$，$I_{\text{cl,ref}} = 0.5$ clo。

在极端环境条件下，稳态生物热方程可能不存在物理可行的解。为此，本模块采用瞬态逼近法替代稳态求解：

算法流程：
1. 在实际环境下，从中性初始状态（$T_{\text{sk,0}} = 34.4$ °C，$T_{\text{core,0}} = 37.0$ °C）运行可配置时长的瞬态模拟（默认 30 分钟，时间步长 60 秒），通过隐式欧拉法求解瞬态生物热方程，得到 $T_{\text{sk}}$、$T_{\text{core}}$，并计算生理应变指标 $S_{\text{actual}}$。
2. 构建参考人体，服装固定为 $I_{\text{cl,ref}}$。
3. 在 $[0, \max(T_a, \text{MRT}) + 20]$ °C 范围内二分搜索参考空气温度 $T_r$，每次执行瞬态模拟并计算 $S_{\text{ref}}$。
4. 当 $|S_{\text{ref}} - S_{\text{actual}}|$ 最小时，$T_r$ 即为 EqT。

生理应变指标定义为：

$$S = (T_{\text{sk}} - 34.4) + 3.0 \times (T_{\text{core}} - 37.0)$$

可配置参数：
- `TransientDuration`：瞬态模拟时长（秒，默认 1800）
- `TransientTimeStep`：瞬态模拟时间步长（秒，默认 60）

### 1.11 动态热感觉 DTS

DTS 将内部应激指数 S 通过 tanh 映射到 [-3, +3] 区间：

$$DTS = 3 \tanh(S)$$

内部应激指数 S 的组成（Fiala 1998/2003）：

$$S = f_{\text{sk}} + \phi + \tau_{\text{neg}} + \tau_{\text{pos}} + f_{\text{core}} + f_{\text{ex}} + f_{\text{wet}}$$

各项：
- 皮肤温度贡献：$f_{\text{sk}} = b_1 (T_{\text{sk}} - T_{\text{sk,0}})$，其中 $b_1 = 1.026$（过热）或 $0.298$（过冷）
- 核心温度交互项 $\phi$：仅在 $\Delta T_{\text{hy}} > 0$ 且 $\Delta T_{\text{sk}} < 5$ K 时非零
- 动态负效应：$\tau_{\text{neg}} = 0.114 \, dT_{\text{sk}}/dt \cdot \phi$（当 $dT_{\text{sk}}/dt < 0$）
- 核心直接贡献：$f_{\text{core}} = 0.3 \, \Delta T_{\text{hy}}$
- 运动耐受偏移：$f_{\text{ex}} = -0.035$（当 $M > 100$ W/m²）
- 皮肤湿润贡献：$f_{\text{wet}} = 1.5 \max(0, w_{\text{sk}} - 0.06)$

设定点随代谢率修正：

$$T_{\text{sk,0}} = 34.4 - 0.028 (M - 58.2)$$

$$T_{\text{hy,0}} = 37.0 + 0.0015 (M - 58.2)$$

---

## 2. 人体生理组件（HumanPhysiology.cs）

### 2.1 功能说明

用于配置人体生理与活动参数，输出结构化的 `UtciHumanSet` 数据，供模拟器使用。支持代谢率手动输入或按步行速度自动计算，支持服装热阻手动输入或由 UTCI 服装模型自动调整。

### 2.2 输入参数

| 索引 | 参数 | 标识 | 类型 | 默认值 | 说明 |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | AutoMet | AutoMet | Boolean | true | 是否根据 WalkSpeed 自动计算代谢率 |
| 1 | MetRate | M | Number | 80.0 | 代谢产热 [W/m²]，AutoMet=true 时忽略 |
| 2 | WalkSpeed | Vw | Number | 1.1 | 行走/跑步速度 [m/s]，用于 AutoMet 与有效风速 |
| 3 | Posture | Pos | Integer | 0 | 姿态：0=站立，1=坐姿 |
| 4 | AutoClo | AutoClo | Boolean | false | 是否由空气温度自动调整服装热阻（在 HumanThermalEnvironment 中执行） |
| 5 | CloValue | Icl | Number | 0.5 | 服装热阻 [clo]，AutoClo=false 时使用 |
| 6 | BodyWeight | W | Number | 73.5 | 体重 [kg] |
| 7 | BodyHeight | H | Number | 1.75 | 身高 [m] |
| 8 | Age | Age | Number | 35.0 | 年龄 [岁] |
| 9 | Sex | Sex | Integer | 0 | 性别：0=男性，1=女性 |

**参数约束：**
- WalkSpeed：0–8 m/s，超出时警告并截断
- BodyWeight：≥30 kg
- BodyHeight：≥1.0 m
- Posture：0 或 1，否则重置为 0
- Sex：0 或 1，否则重置为 0
- AutoMet=false 时，若 MetRate 与 WalkSpeed 不一致（差值 > 30 W/m²）会发出警告

### 2.3 输出参数

| 索引 | 参数 | 说明 |
|:---:|:---:|:---|
| 0 | HumanPhysiology | 结构化人体/活动数据（UtciHumanSet） |

---

## 3. 人体热环境组件（HumanThermalEnvironment.cs）

### 3.1 功能说明

用于配置环境参数，输出结构化的 `UtciWeatherSet` 数据。内置 Goff-Gratch 饱和蒸气压公式，支持由相对湿度自动计算实际水汽压。

### 3.2 输入参数

| 索引 | 参数 | 标识 | 类型 | 默认值 | 说明 |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | AirTemp | Ta | Number | 26.0 | 空气干球温度 [°C] |
| 1 | MRT | MRT | Number | 26.0 | 平均辐射温度 [°C]，未输入时默认等于 Ta |
| 2 | RH | RH | Number | 50.0 | 相对湿度 [%] |
| 3 | AutoVP | AutoVP | Boolean | true | 是否由 RH 自动计算实际水汽压 |
| 4 | VP | VP | Number | 16.8 | 实际水汽压 [hPa]，AutoVP=false 时使用 |
| 5 | WindSpeed | Va | Number | 1.0 | 1.5 m 行人高度风速 [m/s] |
| 6 | Pressure | P | Number | 1013.25 | 大气压 [hPa] |

**参数约束：**
- RH：0–100%，超出时截断
- WindSpeed：0.01–17.0 m/s，超出时截断
- Pressure：≤0 时回退为 1013.25 hPa

### 3.3 Goff-Gratch 饱和蒸气压

实际水汽压：

$$p_a = p_{\text{sat}}(T_a) \cdot \frac{RH}{100}$$

饱和蒸气压 $p_{\text{sat}}$ [hPa] 由 Goff-Gratch 公式计算：

$$\log_{10} p_{\text{sat}} = -7.90298 \left(\frac{T_{\text{st}}}{T} - 1\right) + 5.02808 \log_{10}\left(\frac{T_{\text{st}}}{T}\right) - 1.3816 \times 10^{-7} \left(10^{11.344(1 - T/T_{\text{st}})} - 1\right) + 8.1328 \times 10^{-3} \left(10^{-3.49149(T_{\text{st}}/T - 1)} - 1\right) + \log_{10}(1013.246)$$

其中 $T_{\text{st}} = 373.16$ K，$T = 273.15 + T_a$ [K]。

### 3.4 输出参数

| 索引 | 参数 | 说明 |
|:---:|:---:|:---|
| 0 | Human Thermal Environment | 结构化气象/环境数据（UtciWeatherSet） |

---

## 4. 模拟基础设置组件（HumanSimulationBaseSettings.cs）

### 4.1 功能说明

暴露内部求解器参数，供高级用户覆盖默认值。若未连接，模拟器使用 PET 默认值（$M=80$ W/m²，$v=0.1$ m/s，$RH=50\%$，$I_{\text{cl}}=0.5$ clo）。

### 4.2 输入参数

| 索引 | 参数 | 标识 | 单位 | 默认值 | 说明 |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | RefMetRate | Mref | W/m² | 135.0 | 参考代谢率，UTCI=135，PET=80，PMV=70 |
| 1 | RefWindSpeed | Vref | m/s | 0.5 | 参考风速，UTCI=0.5，PET=0.1 |
| 2 | RefRH | RHref | % | 50.0 | 参考相对湿度 |
| 3 | RefIcl | Iclref | clo | 0.5 | 参考服装热阻 |
| 4 | EqTSearchIter | EqTN | - | 20 | EqT 二分搜索迭代次数 |
| 5 | InsensibleDiff | wDiff | - | 0.06 | 不感蒸发基础皮肤湿润度 |
| 6 | AgeAttenuation | AgeAtt | - | 0.75 | >65 岁体温调节响应衰减系数 |
| 7 | SexMetFactor | SexMet | - | 0.90 | 女性基础代谢相对男性比例 |
| 8 | TransientDuration | TDur | s | 1800 | 瞬态模拟时长（秒） |
| 9 | TransientTimeStep | TStep | s | 60 | 瞬态模拟时间步长（秒） |
| 10 | BlpRelax | Alpha | - | 0.85 | 血池温度松弛因子（0.1–1.0） |

**参数约束：** 各参数超出典型范围时发出警告并截断到合理区间。

### 4.3 输出参数

| 索引 | 参数 | 说明 |
|:---:|:---:|:---|
| 0 | SimBaseSet | 模拟基础设置（SimulationSettings） |

---

## 5. 人体热调节模拟器组件（HumanThermoregulationSimulator.cs）

### 5.1 功能说明

核心求解组件，接收结构化的环境数据、人体数据与可选基础设置，并行求解每个状态点的 EqT、DTS 及生理响应。

### 5.2 输入参数

| 索引 | 参数 | 标识 | 类型 | 说明 |
|:---:|:---:|:---:|:---:|:---|
| 0 | Human Thermal Environment | HTE | Generic List | 来自 Human Thermal Environment 的结构化环境数据 |
| 1 | Human Physiology | HP | Generic List | 来自 Human Physiology 的结构化人体数据；单条时会广播到所有环境条目 |
| 2 | SimBaseSet | SBS | Generic | 来自 Simulation Base Settings 的基础设置（可选） |
| 3 | Run | Run | Boolean | 设为 true 执行模拟 |

**批量规则：**
- 环境条目数 $n$ 与人体条目数需为 1:1，或人体条目数为 1（自动广播）
- $n > 1000$ 时会提示批量较大

### 5.3 输出参数

| 索引 | 参数 | 标识 | 说明 |
|:---:|:---:|:---:|:---|
| 0 | EquivTemp | EqT | 生理等效温度 [°C] |
| 1 | DTS | DTS | 动态热感觉 [-3 到 +3] |
| 2 | MeanSkinTemp | Tsk | 面积加权平均皮肤温度 [°C] |
| 3 | CoreTemp | Tco | 下丘脑（核心）温度 [°C] |
| 4 | SweatRate | Sw | 总出汗率 [g/min] |
| 5 | Shivering | Sh | 总颤抖产热 [W] |

---

## 6. 数据结构与 Grasshopper 封装

模块在 `ThermalComfort.Core` 命名空间定义以下核心数据结构：

| 数据结构 | 说明 |
|:---:|:---|
| `UtciWeatherSet` | 环境参数容器（Ta、RH、Va、MRT、VP、P） |
| `UtciHumanSet` | 人体/活动参数容器（M、Vw、Posture、Icl、W、H、Age、Sex 等） |
| `SimulationSettings` | 求解器与参考环境设置 |
| `UtciResultSet` | 完整结果容器（EqT、DTS、温度、调节响应、热平衡分量） |

对应的 Grasshopper Goo 包装类：

| Goo 类 | 类型名称 | 说明 |
|:---:|:---:|:---:|
| `GH_UtciWeatherSet` | UTCI Weather Set | 环境数据在 Grasshopper 线缆中的封装 |
| `GH_UtciHumanSet` | UTCI Human Set | 人体数据在 Grasshopper 线缆中的封装 |
| `GH_SimulationSettings` | Simulation Settings | 基础设置在 Grasshopper 线缆中的封装 |
| `GH_UtciResultSet` | EqT Result Set | 结果在 Grasshopper 线缆中的封装 |

---

## 7. 使用注意事项

1. **MRT 输入**：建议使用本插件的 MRT 模块（SolarCal 或 RayMan）计算平均辐射温度，以获得室外环境的辐射修正。
2. **风速高度**：WindSpeed 应为 1.5 m 行人高度风速；若输入为气象站 10 m 风速，需先按对数廓线转换。
3. **服装模型**：AutoClo=true 时，HumanThermalEnvironment 组件会自动根据空气温度调用 UTCI 服装模型；AutoClo=false 时采用 CloValue。
4. **参考环境**：EqT 强烈依赖 RefMetRate。比较不同活动水平的舒适度时，建议保持 RefMetRate 一致（如 UTCI 默认 135 W/m²）。
5. **年龄修正**：仅当 Age > 65 岁时才应用 AgeAttenuation，模拟老年人体温调节响应下降。
6. **海拔修正**：Pressure 输入用于大气压修正，影响对流与蒸发；高海拔场景建议输入实际大气压。
7. **瞬态模拟时长**：瞬态模拟时长（TransientDuration）影响等效温度对初始条件的记忆效应。较短的模拟时长（如 10 分钟）可能保留更多初始中性状态的惯性，较长的模拟时长（如 60 分钟）使结果更接近稳态。在极端环境条件下建议适当延长模拟时长。

---

## 8. 参考文献

1. Fiala, D. (1998). *Dynamic Simulation of Human Heat Transfer and Thermal Comfort*. PhD Thesis, De Montfort University.
2. Fiala, D., Lomas, K. J., & Stohrer, M. (2001). Computer prediction of human thermoregulatory and temperature responses to a wide range of environmental conditions. *International Journal of Biometeorology*, 45(3), 143-159.
3. Fiala, D., et al. (2012). Physiologically equivalent temperature. *International Journal of Biometeorology*, 56, 419-431.
4. Fiala, D., Lomas, K. J., & Stohrer, M. (2003). First principles modelling of thermal sensation responses in steady and transient conditions. *International Journal of Biometeorology*, 47(4), 179-191.
5. Havenith, G., et al. (2012). The UTCI-clothing model. *International Journal of Biometeorology*, 56, 461-470.
6. Broede, P., et al. (2012). The Universal Thermal Climate Index UTCI in operational use. *International Journal of Biometeorology*, 56, 475-482.
7. Hoppe, P. (1999). The physiological equivalent temperature — a universal index for the biometeorological assessment of the thermal environment. *International Journal of Biometeorology*, 43, 71-75.
8. Gagge, A. P., Stolwijk, J. A. J., & Hardy, J. D. (1971). Comfort and thermal sensations and associated physiological responses at various ambient temperatures. *Environmental Research*, 1(1), 1-20.
9. ISO 8996 (2004). *Ergonomics of the thermal environment — Determination of metabolic rate*. International Organization for Standardization, Geneva.
10. Goff, J. A., & Gratch, S. (1946). Low-pressure properties of water from -160 to 212 °F. *Transactions of the American Society of Heating and Ventilating Engineers*, 52, 95-122.