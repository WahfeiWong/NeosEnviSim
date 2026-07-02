# PST (Physiological Subjective Temperature) 模块

本模块基于 MENEX_2005 两阶段人体热平衡模型，计算室外环境中的生理主观温度（PST）、生理应变指数（STI）、生理应变等级（PhS）及适应后平均皮肤温度（SkinTemp）。模型通过代谢产热、辐射平衡、对流换热、蒸发散热与呼吸热损失的热平衡方程，模拟人体从初始接触环境到经过 15–20 分钟生理适应后的热感觉变化。

模块包含三个组件：
- **PST Weather Settings**：气象参数配置组件（含 Goff-Gratch 水汽压计算与地表温度自动估算）
- **PST Human Settings**：人体生理参数配置组件（含服装热阻温度自适应公式）
- **PST Simulator**：核心计算组件，实现 MENEX_2005 两阶段热平衡方法

---

## 1. PST 物理模型（PstSimulator.cs）

### 1.1 热平衡方程

MENEX_2005 模型以人体热平衡方程为基础：

$$M + Q + C + E + \text{Res} = S$$

其中：
- $M$：代谢产热率 [W/m²]
- $Q$：辐射热平衡 [W/m²]（$Q = R + L$，太阳辐射吸收 + 净长波辐射）
- $C$：对流换热 [W/m²]
- $E$：蒸发散热 [W/m²]
- $\text{Res}$：呼吸热损失 [W/m²]
- $S$：净体热蓄存 [W/m²]

### 1.2 两阶段计算方法

**第一阶段（初始稳态）：**
以环境接触时的初始皮肤温度 $T_s$ 计算各热流分量，输出 STI（主观温度指数）和 PhS（生理应变比 $C/E$）。

**第二阶段（适应性稳态）：**
根据第一阶段蒸发热损失 $E$ 修正皮肤温度（蒸发冷却效应），以调整后的皮肤温度 $T_{sR}$ 重新计算长波辐射、对流与蒸发项。太阳辐射吸收 $R$、代谢产热 $M$ 和呼吸热损失 $\text{Res}$ 保持第一阶段数值不变。输出 PST、PhS 等级和适应后皮肤温度。

### 1.3  clothing 与换热系数

**服装热阻 $I_{cl}$ [clo]：**

当 `AutoClo = true` 时，采用 MENEX_2005 温度自适应公式：

$$
I_{cl} = \begin{cases}
3.0 & t < -30\,^\circ\text{C} \\
1.691 - 0.0436 \cdot t & -30 \leq t \leq 25\,^\circ\text{C} \\
0.6 & t > 25\,^\circ\text{C}
\end{cases}$$

当 `AutoClo = false` 时，直接使用用户输入的 `CloValue`。

**对流与辐射换热系数 $h_c$ [W/(m²·K)]：**

$$h_c = (0.013 \cdot p - 0.04 \cdot t - 0.503) \cdot (v + v')^{0.4}$$

其中 $p$ 为大气压 [hPa]，$t$ 为空气温度 [°C]，$v$ 为风速 [m/s]，$v'$ 为步行速度 [m/s]。当 $h_c < 0.5$ 时取 0.5。

**服装热传导系数 $h_c'$ [W/(m²·K)]：**

$$h_c' = \frac{(0.013 \cdot p - 0.04 \cdot t - 0.503) \cdot 0.53}{I_{cl} \cdot \left[1 - 0.27 \cdot (v + v')^{0.4}\right]}$$

**服装缩减系数 $I_{rc}$：**

$$I_{rc} = \frac{h_c'}{h_c' + h_c + 21.55 \times 10^{-8} \cdot T^3}$$

其中 $T = 273 + t$ 为空气温度 [K]。取值范围 $[0.001, 1.0]$。

**蒸发缩减系数 $I_e$：**

$$I_e = \frac{h_c'}{h_c' + h_c}$$

取值范围 $[0.001, 1.0]$。

### 1.4 第一阶段：初始稳态

**初始平均皮肤温度 $T_s$ [°C]：**

$$T_s = 26.4 + 0.02138 \cdot \text{MRT} + 0.2095 \cdot t - 0.0185 \cdot f - 0.009 \cdot v + 0.6 \cdot (I_{cl} - 1) + 0.00128 \cdot M$$

其中 MRT 为平均辐射温度 [°C]，$f$ 为相对湿度 [%]，$M$ 为代谢率 [W/m²]。取值范围约束为 $[22.0, 37.5]$ °C。

**长波辐射分量：**

地面长波辐射：

$$L_g = \varepsilon_s \cdot \sigma \cdot (273 + T_g)^4$$

大气长波辐射：

$$L_a = \varepsilon_s \cdot \sigma \cdot (273 + t)^4 \cdot \left(0.82 - 0.25 \times 10^{-0.094 \cdot e}\right)$$

人体长波发射：

$$L_s = \varepsilon_h \cdot \sigma \cdot (273 + T_s)^4$$

净长波辐射：

$$L = (0.5 \cdot L_g + 0.5 \cdot L_a - L_s) \cdot I_{rc}$$

**吸收太阳辐射 $R$ [W/m²]：**

模型通过 MRT 反推人体吸收的太阳辐射总量：

$$R = \left[\varepsilon_h \cdot \sigma \cdot (273 + \text{MRT})^4 - \varepsilon_s \cdot \sigma \cdot (273 + t)^4\right] \cdot I_{rc}$$

> **注意：** 此处的 MRT 应由外部模块（如 RayMan 或 SolarCal）计算提供，已综合考虑直接辐射、散射辐射、地面反射及周围表面长波辐射。

**辐射平衡：**

$$Q = R + L$$

**蒸发换热系数 $h_e$ [hPa·m²/W]：**

$$h_e = \frac{\left[t \cdot (0.00006 \cdot t - 0.00002 \cdot p + 0.011) + 0.02 \cdot p - 0.773\right] \cdot 0.53}{I_{cl} \cdot \left[1 - 0.27 \cdot (v + v')^{0.4}\right]}$$

当 $h_e < 0.01$ 时取 0.01。

**皮肤温度饱和水汽压 $e_s$ [hPa]：**

$$e_s = \exp(0.058 \cdot T_s + 2.003)$$

**皮肤湿润度 $w$：**

$$
w = \begin{cases}
1.0 & T_s > 36.5\,^\circ\text{C} \\
0.001 & T_s < 22.0\,^\circ\text{C} \\
\dfrac{1.031}{37.5 - T_s} - 0.065 & \text{其他}
\end{cases}$$

取值范围约束为 $[0.001, 1.0]$。

**蒸发热损失 $E$ [W/m²]：**

$$E = h_e \cdot (e - e_s) \cdot w \cdot I_e - \left[0.42 \cdot (M - 58) - 5.04\right]$$

**对流换热 $C$ [W/m²]：**

$$C = h_c \cdot (t - T_s) \cdot I_{rc}$$

**呼吸热损失 $\text{Res}$ [W/m²]：**

$$\text{Res} = 0.0014 \cdot M \cdot (t - 35) + 0.0173 \cdot M \cdot (0.1 \cdot e - 5.624)$$

**净体热蓄存 $S$ [W/m²]：**

$$S = M + Q + C + E + \text{Res}$$

**STI（主观温度指数）计算：**

$$\text{STI} = \begin{cases}
\text{MRT} - \left\{\left[\dfrac{|S|^{0.75}}{\varepsilon_h \cdot \sigma} + 273^4\right]^{0.25} - 273\right\} & S < 0 \\
\text{MRT} + \left\{\left[\dfrac{|S|^{0.75}}{\varepsilon_h \cdot \sigma} + 273^4\right]^{0.25} - 273\right\} & S \geq 0
\end{cases}$$

**PhS 比值（生理应变比）：**

$$\text{PhS} = \frac{C}{E}$$

当 $|E| < 0.001$ 时，取符号相关的极值（$E > 0$ 时为 1000，$E < 0$ 时为 -1000）。

### 1.5 第二阶段：适应性稳态

**皮肤温度修正（蒸发冷却效应）：**

$$\Delta T_s = \begin{cases}
(E + 50) \cdot 0.066 & E < -50\,\text{W/m}^2 \\
0 & E \geq -50\,\text{W/m}^2
\end{cases}$$

$$T_{sR} = T_s + \Delta T_s$$

取值范围约束为 $[22.0, 37.5]$ °C。

固定值（来自第一阶段）：$M$（代谢率）、$R$（吸收太阳辐射）、$\text{Res}$（呼吸热损失）。

**重新计算的辐射分量：**

$$L_{sR} = \varepsilon_h \cdot \sigma \cdot (273 + T_{sR})^4$$

$$L_R = (0.5 \cdot L_g + 0.5 \cdot L_a - L_{sR}) \cdot I_{rc}$$

$$Q_R = R + L_R$$

**服装内平均辐射温度 iMRT [°C]：**

$$\text{iMRT} = \left[\frac{R + (L_a + L_g) \cdot 0.5 \cdot I_{rc} + 0.5 \cdot L_s}{\varepsilon_h \cdot \sigma}\right]^{0.25} - 273$$

**适应后对对流换热 $C_R$ [W/m²]：**

$$C_R = h_c \cdot (\text{iMRT} - T_{sR}) \cdot I_{rc}$$

**服装内水汽压 $e^*$ [hPa]：**

$$e^* = 6.12 \times 10^{\frac{7.5 \cdot \text{iMRT}}{237.7 + \text{iMRT}}} \times 0.01 \cdot f$$

**适应后皮肤温度饱和水汽压：**

$$e_{sR} = \exp(0.058 \cdot T_{sR} + 2.003)$$

**适应后皮肤湿润度 $w_R$：**

同第一阶段公式，以 $T_{sR}$ 替代 $T_s$。

**适应后蒸发热损失 $E_R$ [W/m²]：**

$$E_R = h_e \cdot \sqrt{v + v'} \cdot (e^* - e_{sR}) \cdot w_R \cdot I_e - \left[0.42 \cdot (M - 58) - 5.04\right]$$

**适应后净体热蓄存 $S_R$ [W/m²]：**

$$S_R = M + Q_R + C_R + E_R + \text{Res}$$

### 1.6 PST（生理主观温度）

$$\text{PST} = \begin{cases}
\text{iMRT} - \left\{\left[\dfrac{|S_R|^{0.75}}{\varepsilon_h \cdot \sigma} + 273^4\right]^{0.25} - 273\right\} & S_R < 0 \\
\text{iMRT} + \left\{\left[\dfrac{|S_R|^{0.75}}{\varepsilon_h \cdot \sigma} + 273^4\right]^{0.25} - 273\right\} & S_R \geq 0
\end{cases}$$

### 1.7 总热损失

$$\text{HeatLoss} = M - S_R = -(Q_R + C_R + E_R + \text{Res})$$

### 1.8 PhS 等级划分

| PhS 比值范围 | 等级描述 |
|:---:|:---|
| $< 0$ | 极端热应激 (Extreme hot strain) |
| $0 \sim 0.24$ | 重度热应激 (Great hot strain) |
| $0.25 \sim 0.74$ | 中度热应激 (Moderate hot strain) |
| $0.75 \sim 1.50$ | 热中性 (Thermoneutral) |
| $1.51 \sim 4.00$ | 中度冷应激 (Moderate cold strain) |
| $4.01 \sim 8.00$ | 重度冷应激 (Great cold strain) |
| $> 8.00$ | 极端冷应激 (Extreme cold strain) |

### 1.9 PST 热感觉等级

| PST [°C] | 热感觉描述 |
|:---:|:---|
| $<-36$ | 严寒 (Frosty) |
| $-36 \sim -16.1$ | 很冷 (Very cold) |
| $-16 \sim 4$ | 冷 (Cold) |
| $4.1 \sim 14$ | 凉爽 (Cool) |
| $14.1 \sim 24$ | 舒适 (Comfortable) |
| $24.1 \sim 34$ | 温暖 (Warm) |
| $34.1 \sim 44$ | 热 (Hot) |
| $44.1 \sim 54$ | 很热 (Very hot) |
| $> 54$ | 酷热 (Sweltering) |

---

## 2. 气象参数设置组件（PstWeatherSettings.cs）

### 2.1 功能说明

配置 PST 模拟所需的气象参数，支持 Goff-Gratch 公式从相对湿度自动计算实际水汽压，以及基于云覆盖率自动估算地表温度。

### 2.2 输入参数

| 索引 | 参数 | 标识 | 单位 | 默认值 | 说明 |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | AirTemp | Ta | °C | 26.0 | 空气干球温度 |
| 1 | WindSpeed | Va | m/s | 0.5 | 1.1m 高度处风速 |
| 2 | RH | RH | % | 50.0 | 相对湿度，无论 AutoVP 设置均需提供 |
| 3 | VP | VP | hPa | — | 实际水汽压，AutoVP = False 时必需 |
| 4 | AutoVP | AutoVP | — | true | true 时用 Goff-Gratch 公式自动计算水汽压；false 时直接使用 VP 输入 |
| 5 | Pressure | P | hPa | 1013.25 | 大气压，为零时使用标准大气压 1013.25 hPa |
| 6 | MRT | MRT | °C | — | 平均辐射温度，应由外部模块计算提供 |
| 7 | CloudCover | CC | % | 0.0 | 云覆盖率 0–100%，AutoTg = true 时用于估算地表温度 |
| 8 | AutoTg | AutoTg | — | true | true 时按 MENEX_2005 公式由云覆盖率估算 Tg；false 时使用 Tg 输入 |
| 9 | Tg | Tg | °C | 26.0 | 地表温度，AutoTg = false 时生效 |

### 2.3 Goff-Gratch 饱和水汽压公式

当 `AutoVP = true` 时，采用 Goff-Gratch 公式由空气温度和相对湿度计算实际水汽压。

**水面（$t \geq 0$ °C）：**

$$\begin{aligned}
\log_{10} e_s = &-7.90298 \cdot \left(\frac{T_{st}}{T} - 1\right) + 5.02808 \cdot \log_{10}\left(\frac{T_{st}}{T}\right) \\
&- 1.3816 \times 10^{-7} \cdot \left(10^{11.344 \cdot (1 - T/T_{st})} - 1\right) \\
&+ 8.1328 \times 10^{-3} \cdot \left(10^{-3.49149 \cdot (T_{st}/T - 1)} - 1\right) + \log_{10} e_{st}
\end{aligned}$$

**冰面（$t < 0$ °C）：**

$$\log_{10} e_s = -9.09718 \cdot \left(\frac{T_0}{T} - 1\right) - 3.56654 \cdot \log_{10}\left(\frac{T_0}{T}\right) + 0.876793 \cdot \left(1 - \frac{T}{T_0}\right) + \log_{10} e_0$$

实际水汽压：

$$e = e_s \cdot \frac{\text{RH}}{100}$$

其中：
- $T = 273.15 + t$ [K]
- $T_{st} = 373.16$ K（沸点温度），$e_{st} = 1013.246$ hPa
- $T_0 = 273.16$ K（三相点温度），$e_0 = 6.1071$ hPa

### 2.4 地表温度估算（AutoTg）

当 `AutoTg = true` 时，基于云覆盖率 $N$ 和空气温度估算地表温度：

$$T_g = \begin{cases}
t & N \geq 80\% \\
1.25 \cdot t & N < 80\% \text{ 且 } t \geq 0\,^\circ\text{C} \\
0.9 \cdot t & N < 80\% \text{ 且 } t < 0\,^\circ\text{C}
\end{cases}$$

### 2.5 输出参数

| 索引 | 参数 | 说明 |
|:---:|:---:|:---|
| 0 | WeatherSet | 结构化气象数据集，连接至 PST Simulator 的 WS 输入端 |

---

## 3. 人体参数设置组件（PstHumanSettings.cs）

### 3.1 功能说明

配置 PST 模拟所需的人体生理参数，支持服装热阻的温度自适应调整。

### 3.2 输入参数

| 索引 | 参数 | 标识 | 单位 | 默认值 | 说明 |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | MetRate | M | W/m² | 135 | 代谢产热率，默认对应步行 4 km/h（ISO 8996），范围 58–400 W/m² |
| 1 | AutoClo | AutoClo | — | false | true 时按 MENEX_2005 公式自动调整服装热阻（忽略 CloValue 输入）；false 时使用 CloValue |
| 2 | CloValue | Icl | clo | 0.8 | 服装热阻，夏季典型值；AutoClo = true 时被忽略 |
| 3 | AlbedoClo | Alb | % | 30 | 服装表面太阳辐射反照率，典型夏季服装取值，范围 10–90% |
| 4 | WalkSpeed | Vw | m/s | 1.1 | 人体相对于空气的运动速度，默认约 4 km/h 步行速度 |

> **注意：** 平均皮肤温度 $T_s$ 和皮肤湿润度 $w$ 是 MENEX_2005 模型的内部迭代变量，不作为输入端暴露。

### 3.3 输出参数

| 索引 | 参数 | 说明 |
|:---:|:---:|:---|
| 0 | HumanSet | 结构化人体生理参数集，连接至 PST Simulator 的 HS 输入端 |

---

## 4. PST 模拟器组件（PstSimulator.cs）

### 4.1 输入参数

| 索引 | 参数 | 类型 | 说明 |
|:---:|:---:|:---:|:---|
| 0 | WeatherSet | Generic | 结构化气象数据，来自 PST Weather Settings 组件 |
| 1 | HumanSet | Generic | 结构化人体参数，来自 PST Human Settings 组件 |

### 4.2 输出参数

| 索引 | 参数 | 标识 | 说明 |
|:---:|:---:|:---:|:---|
| 0 | PST | PST | 生理主观温度 [°C]，15–20 分钟适应后的最终热感觉指标 |
| 1 | STI | STI | 生理应变指数 [无量纲]，$C/E$ 比值，反映体温调节适应方向和强度 |
| 2 | PhS | PhS | 生理应变等级文字描述，由 STI 值划分 |
| 3 | HeatLoss | HL | 人体总热损失 [W/m²]，$\text{HL} = M - S_R$ |
| 4 | SkinTemp | Ts | 适应后平均皮肤温度 [°C]，$T_{sR}$ |

---

## 5. 关键参数物理约束

| 参数 | 下限 | 上限 | 说明 |
|:---:|:---:|:---:|:---|
| $I_{cl}$ | 0.1 | 3.5 | 服装热阻 [clo] |
| $T_s$ / $T_{sR}$ | 22.0 | 37.5 | 皮肤温度 [°C] |
| $w$ / $w_R$ | 0.001 | 1.0 | 皮肤湿润度 |
| $I_{rc}$ | 0.001 | 1.0 | 服装缩减系数 |
| $I_e$ | 0.001 | 1.0 | 蒸发缩减系数 |
| $h_c$ | 0.5 | — | 对流辐射换热系数 [W/(m²·K)] |
| $h_c'$ | 0.1 | — | 服装热传导系数 [W/(m²·K)] |
| $h_e$ | 0.01 | — | 蒸发换热系数 [hPa·m²/W] |
| MetRate | 58 | — | 代谢率 [W/m²] |
| RH | 0 | 100 | 相对湿度 [%] |
| CloudCover | 0 | 100 | 云覆盖率 [%] |
| Alb | 0 | 100 | 服装反照率 [%] |

---

## 6. 参考文献

1. Blazejczyk, K. (2005). MENEX_2005 — the updated version of man-environment heat exchange model. *Proceedings of the 11th International Conference on Environmental Ergonomics*, Ystad, Sweden, 222–225.

2. Blazejczyk, K. (1994). New climatological- and -physiological model of the human heat balance outdoor (MENEX). *Zeszyty IGiPZ PAN*, 28, 27–58.

3. Fanger, P.O. (1970). *Thermal Comfort: Analysis and Applications in Environmental Engineering*. McGraw-Hill, New York.

4. ISO 8996 (2004). Ergonomics of the thermal environment — Determination of metabolic rate. International Organization for Standardization, Geneva.

5. Goff, J.A., & Gratch, S. (1946). Low-pressure properties of water from -160 to 212 F. *Transactions of the American Society of Heating and Ventilating Engineers*, 95–122.

6. World Meteorological Organization (1988). General meteorological standards and recommended practices, Appendix A, *WMO Technical Regulations*, WMO-No. 49.
