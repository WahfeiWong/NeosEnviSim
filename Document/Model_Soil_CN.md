# 土壤热物理模块

## 1. 模型概述

土壤热物理模块采用耦合的 Force-Restore + Penman-Monteith（单源）方法，逐时模拟地表温度和地表能量平衡。模型由三个核心组件构成：

| 组件 | 方法 | 参考文献 |
|-----------|--------|-----------|
| 土壤温度动态 | Force-Restore 方法 | Deardorff (1978); Noilhan & Planton (1989) |
| 潜热通量 | Penman-Monteith（单源） | Allen et al. (1998) FAO-56; ISBA |
| 空气动力学阻力 | 对数律含稳定度修正 | Louis (1979) |
| 太阳位置 | SPA（太阳位置算法） | Reda & Andreas (2004), NREL |

本模块提供两个模拟器：
- **SoilThermalSimulator** (`SoilSim`)：单点模拟
- **SpatialSoilThermalSimulator** (`SpSoilSim`)：多点空间模拟，支持逐点辐射修正（SVF、精细化 DNI 暴露因子、周围反射率/发射率）

**增强版（2026-06-14）：** 空间模拟器引入精细化 DNI（直接法向辐射）暴露因子计算，替代原有的二值暴露因子 $f_{\text{exp}}$。通过分类障碍物设置集（ObstacleSet）支持三种障碍物类型的差异化透射处理：不透光物体（完全阻挡）、树木（Beer-Lambert 冠层透射）、半透明遮阳构件（固定透射率），实现更精确的短波辐射计算。

---

## 2. Force-Restore 土壤温度模型

### 2.1 表层温度更新

地表温度 $T_1$（表层）通过 Force-Restore 方程更新：

$$\frac{dT_1}{dt} = C_{\text{flux}} \cdot G - C_{\text{restore}} \cdot (T_1 - T_2)$$

离散形式（子步迭代）：

$$T_1^{\text{new}} = T_1^{\text{prev}} + \Delta t \cdot \left[ C_{\text{flux}} \cdot G - C_{\text{restore}} \cdot (T_1^{\text{guess}} - T_2^{\text{prev}}) \right]$$

| 参数 | 符号 | 单位 | 说明 |
|-----------|--------|------|-------------|
| 表层温度 | $T_1$ | $^{\circ}\mathrm{C}$ | 地表温度（状态变量） |
| 深层温度 | $T_2$ | $^{\circ}\mathrm{C}$ | 深层土壤温度（状态变量） |
| 土壤热通量 | $G$ | $\mathrm{W/m^2}$ | 进入土壤的净热通量 |
| 时间步长 | $\Delta t$ | $\mathrm{s}$ | 子步时长 $= 3600 / N_{\text{sub}} / N_{\text{hourly}}$ |
| 每小时子步数 | $N_{\text{sub}}$ | -- | 默认 10，范围 [1, 60] |

### 2.2 深层温度更新

$$\frac{dT_2}{dt} = C_{\text{flux2}} \cdot G + C_{\text{deep}} \cdot (T_1 - T_2)$$

离散形式：

$$T_2^{\text{new}} = T_2^{\text{prev}} + \Delta t \cdot \left[ C_{\text{flux2}} \cdot G_{\text{stored}} + C_{\text{deep}} \cdot (T_1 - T_2^{\text{prev}}) \right]$$

### 2.3 Force-Restore 系数

**恢复系数** (Deardorff 1978; Noilhan & Planton 1989, 式24)：

$$C_{\text{restore}} = \frac{2\pi}{\tau_1}$$

**深层恢复系数** (Noilhan & Planton 1989, 式25)：

$$C_{\text{deep}} = \frac{2\pi}{\tau_2}$$

**通量系数**：

$$C_{\text{flux}} = \frac{2}{\rho C \cdot d_1}, \qquad C_{\text{flux2}} = \frac{1}{\rho C \cdot d_2}$$

**时间常数**：

$$\tau_1 = 86400 \; \mathrm{s} \; (24 \; \mathrm{h}), \qquad \tau_2 = \tau_1 \cdot \frac{d_2}{0.5}$$

带裁剪：$\tau_2 = \mathrm{clip}(\tau_2, \; \tau_1, \; 10\tau_1)$

| 参数 | 符号 | 单位 | 说明 | 默认值 | 范围 |
|-----------|--------|------|-------------|---------|-------|
| 体积热容量 | $\rho C$ | $\mathrm{MJ/(m^3 \cdot K)}$ | 土壤储热能力 | 1.4 | [0.1, 10] |
| 表层深度 | $d_1$ | $\mathrm{m}$ | 地表土壤层厚度 | 0.05 | [0.001, 1.0] |
| 深层深度 | $d_2$ | $\mathrm{m}$ | 深层土壤层厚度 | 0.5 | [0.001, 5.0] |
| 昼夜时间常数 | $\tau_1$ | $\mathrm{s}$ | 24小时周期 | 86400 | 固定值 |

> **注意：** $\rho C$ 在内部转换为 $\mathrm{J/(m^3 \cdot K)}$：$\rho C_{\text{SI}} = \rho C \times 10^6$

### 2.4 土壤热物性预设

| 土壤类型 | $\lambda$ (W/mK) | $\rho C$ (MJ/m$^3$K) | $\alpha$ (m$^2$/s) | 反照率 | 发射率 |
|-----------|:---:|:---:|:---|:---:|:---:|
| 干沙 | 0.3 | 1.3 | $0.23 \times 10^{-6}$ | 0.25 | 0.95 |
| 湿沙 | 2.5 | 2.5 | $1.00 \times 10^{-6}$ | 0.20 | 0.95 |
| 干黏土 | 0.5 | 1.4 | $0.36 \times 10^{-6}$ | 0.20 | 0.95 |
| 湿黏土 | 2.0 | 2.8 | $0.71 \times 10^{-6}$ | 0.15 | 0.95 |
| 沥青/混凝土 | 1.25 | 2.15 | $0.58 \times 10^{-6}$ | 0.15 | 0.95 |
| 自定义 | 用户定义 | 用户定义 | 用户定义 | 用户定义 | 用户定义 |

---

## 3. 地表能量平衡

### 3.1 能量平衡方程

$$R_n = H + LE + G$$

| 通量 | 符号 | 单位 | 说明 |
|------|--------|------|-------------|
| 净辐射 | $R_n$ | $\mathrm{W/m^2}$ | 地表净辐射能量 |
| 感热 | $H$ | $\mathrm{W/m^2}$ | 向大气的对流换热 |
| 潜热 | $LE$ | $\mathrm{W/m^2}$ | 蒸散发能耗 |
| 土壤热通量 | $G$ | $\mathrm{W/m^2}$ | 传入土壤的热量（余项） |

### 3.2 净短波辐射

$$R_{n,\text{sw}} = (1 - \alpha) \cdot \frac{GHI_{\text{raw}}}{\max(0.01, \Delta t_{\text{hours}})}$$

| 参数 | 符号 | 单位 | 说明 |
|-----------|--------|------|-------------|
| 地表反照率 | $\alpha$ | -- | 短波反射率 |
| 水平面总辐射 | $GHI_{\text{raw}}$ | $\mathrm{Wh/m^2}$ | 来自EPW或用户覆盖值（逐时累积量） |
| 时间步长因子 | -- | -- | 将小时累积量转换为瞬时速率 |

> **注意：** EPW 中的 $GHI_{\text{raw}}$ 值表示前一小时内的累积量。`timeStepFactor` $= 1 / \max(0.01, \Delta t_{\text{hours}})$ 将其转换为平均速率。

### 3.3 净长波辐射

$$R_{n,\text{lw}} = \varepsilon_s \cdot (L_{\downarrow} - \sigma T_{g,\text{K}}^4)$$

$$R_n = R_{n,\text{sw}} + R_{n,\text{lw}}$$

| 参数 | 符号 | 单位 | 说明 | 默认值 |
|-----------|--------|------|-------------|---------|
| 地表发射率 | $\varepsilon_s$ | -- | 地面长波发射率 | 0.95 |
| 向下长波辐射 | $L_{\downarrow}$ | $\mathrm{W/m^2}$ | 来自EPW HIR或用户覆盖值 | EPW |
| 斯特藩-玻尔兹曼常数 | $\sigma$ | $\mathrm{W/(m^2 \cdot K^4)}$ | $5.67 \times 10^{-8}$ | 固定值 |
| 地表温度（K） | $T_{g,\text{K}}$ | $\mathrm{K}$ | $T_g + 273.15$ | -- |

### 3.4 感热通量

$$H = \rho C_p \cdot \frac{T_g - T_{\text{air}}}{r_a}$$

$$\rho C_p = \rho_{\text{air}} \cdot 1004$$

| 参数 | 符号 | 单位 | 说明 |
|-----------|--------|------|-------------|
| 空气密度 | $\rho_{\text{air}}$ | $\mathrm{kg/m^3}$ | 由理想气体定律计算（见第7节） |
| 空气比热 | $C_p$ | $\mathrm{J/(kg \cdot K)}$ | 1004（定压干空气） |
| 地表温度 | $T_g$ | $^{\circ}\mathrm{C}$ | Force-Restore 输出的 $T_1$ |
| 空气温度 | $T_{\text{air}}$ | $^{\circ}\mathrm{C}$ | EPW干球温度或用户覆盖值 |
| 空气动力学阻力 | $r_a$ | $\mathrm{s/m}$ | 由对数律计算（见第8节） |

---

## 4. 潜热通量 (LE)

模块支持通过 `LatentHeatMethod` 选择三种 LE 计算方法：

### 4.1 方法 1: Penman-Monteith（推荐）

单源（裸土/均匀表面）的 Penman-Monteith 方程：

$$LE = \frac{\Delta \cdot (R_n - G) + \rho C_p \cdot VPD / r_a}{\Delta + \gamma \cdot (1 + r_s / r_a)}$$

**与 G 的迭代耦合：** 由于 $G = R_n - H - LE$，$G$ 出现在等式两侧。模型进行迭代（最多5次，松弛因子0.5）：

$$G^{\text{new}} = R_n - H - LE^{(k)}, \qquad G^{(k+1)} = 0.5 \cdot G^{(k)} + 0.5 \cdot G^{\text{new}}$$

收敛准则：$|G^{\text{new}} - G^{(k)}| < 1.0 \; \mathrm{W/m^2}$

| 参数 | 符号 | 单位 | 说明 | 来源 |
|-----------|--------|------|-------------|--------|
| 饱和水汽压曲线斜率 | $\Delta$ | $\mathrm{kPa/^{\circ}C}$ | 第6.2节 | Tetens 导数 |
| 干湿表常数 | $\gamma$ | $\mathrm{kPa/^{\circ}C}$ | 第7节 | 由气压计算 |
| 水汽压亏缺 | $VPD$ | $\mathrm{kPa}$ | $e_s(T_g) - e_a$ | 第6节 |
| 土壤表面阻力 | $r_s$ | $\mathrm{s/m}$ | 第9.2节 | ISBA 模型 |

**LE 裁剪与能量再分配：**

当 $LE$ 超过物理上限 $LE_{\text{max}}$（默认 1000 W/m$^2$）时：

$$LE_{\text{clipped}} = \mathrm{clip}(LE, \; 0, \; LE_{\text{max}})$$

$$LE_{\text{excess}} = LE - LE_{\text{clipped}}$$

多余能量按 60% 分配给 G、40% 分配给 H 进行再分配，以维持能量平衡：

$$G_{\text{final}} = G + 0.6 \cdot LE_{\text{excess}}, \qquad H_{\text{final}} = H + 0.4 \cdot LE_{\text{excess}}$$

### 4.2 方法 2: 简化法（向后兼容）

$$LE = \beta_{\text{moist}} \cdot \frac{\rho_{\text{air}} C_p}{\gamma} \cdot \frac{VPD}{r_a}$$

对 $r_a$ 设置下限：$r_a \geq 10 \; \mathrm{s/m}$

| 参数 | 符号 | 单位 | 默认值 | 范围 |
|-----------|--------|------|---------|-------|
| 水分可用性 | $\beta_{\text{moist}}$ | -- | 0.3 | [0, 1] |

### 4.3 方法 3: 无潜热

$$LE = 0, \qquad \beta = 0, \qquad r_s = 0$$

适用于敏感性分析和干燥表面条件。

---

## 5. 热力学性质

### 5.1 饱和水汽压 (Tetens, 1930)

$$e_s(T) = 0.6108 \cdot \exp\left(\frac{17.27 \cdot T}{T + 237.3}\right)$$

| 参数 | 符号 | 单位 | 说明 |
|-----------|--------|------|-------------|
| 温度 | $T$ | $^{\circ}\mathrm{C}$ | 空气温度或地表温度 |
| 饱和水汽压 | $e_s(T)$ | $\mathrm{kPa}$ | 在温度 $T$ 下 |

### 5.2 饱和水汽压曲线斜率

$$\Delta(T) = \frac{4098 \cdot e_s(T)}{(T + 237.3)^2}$$

### 5.3 实际水汽压

由相对湿度计算：

$$e_a = e_s(T_{\text{air}}) \cdot \frac{RH}{100}$$

由 RH 覆盖值计算（当用户提供自定义 RH 时）：

$$e_a = e_s(T_{\text{air}}^{\text{override}}) \cdot \frac{RH^{\text{override}}}{100}$$

| 参数 | 符号 | 单位 | 说明 | 来源 |
|-----------|--------|------|-------------|--------|
| 相对湿度 | $RH$ | % | 来自EPW或用户覆盖值 | EPW 第9字段或 `RH` 输入 |

### 5.4 水汽压亏缺 (VPD)

$$VPD = \max(0, \; e_s(T_g) - e_a)$$

### 5.5 温度修正的汽化潜热

$$\lambda(T) = 2.501 \times 10^6 - 2361 \cdot T$$

| 参数 | 符号 | 单位 | 说明 |
|-----------|--------|------|-------------|
| 汽化潜热 | $\lambda$ | $\mathrm{J/kg}$ | 温度相关 |

---

## 6. 空气密度与干湿表常数

### 6.1 空气密度（理想气体定律）

$$\rho_{\text{air}} = \frac{P_a \times 1000}{R_d \cdot (T_{\text{air}} + 273.15)}$$

| 参数 | 符号 | 单位 | 数值 | 说明 |
|-----------|--------|------|-------|-------------|
| 大气压力 | $P_a$ | $\mathrm{kPa}$ | EPW 或 101.325 | 站点气压 |
| 干空气气体常数 | $R_d$ | $\mathrm{J/(kg \cdot K)}$ | 287.05 | 固定值 |
| 空气温度 | $T_{\text{air}}$ | $^{\circ}\mathrm{C}$ | EPW 或覆盖值 | -- |

### 6.2 干湿表常数

$$\gamma = 0.665 \times 10^{-3} \cdot P_a$$

| 参数 | 符号 | 单位 | 说明 |
|-----------|--------|------|-------------|
| 干湿表常数 | $\gamma$ | $\mathrm{kPa/^{\circ}C}$ | 气压的函数 |

### 6.3 大气压力回退

当 EPW 气压缺失（标记为 9999 或 < 10 kPa）时：

$$P_a = 101.325 \; \mathrm{kPa} \quad \text{（标准海平面气压）}$$

---

## 7. 空气动力学阻力

### 7.1 对数律阻力

$$r_a = \frac{\ln(z/z_{0m}) \cdot \ln(z/z_{0h})}{k^2 \cdot u}$$

| 参数 | 符号 | 单位 | 默认值 | 范围 | 说明 |
|-----------|--------|------|---------|-------|-------------|
| 风速测量高度 | $z$ | $\mathrm{m}$ | 2.0 | [0.1, 100] | 风速测量高度 |
| 动量粗糙长度 | $z_{0m}$ | $\mathrm{m}$ | 0.001 | [$10^{-6}$, 10] | 裸土: 0.001; 短草: 0.01; 城市: 0.1-1.0 |
| 标量粗糙长度 | $z_{0h}$ | $\mathrm{m}$ | 0.0001 | [$10^{-7}$, 1] | 通常取 $z_{0m}/10$ |
| 冯·卡门常数 | $k$ | -- | 0.41 | [0.3, 0.5] | 普适常数 |
| 风速 | $u$ | $\mathrm{m/s}$ | EPW 或覆盖值 | $\geq 0.1$ | 裁剪至最小 0.1 m/s |

### 7.2 理查森数（大气稳定度）

$$Ri = \frac{g \cdot (T_g - T_{\text{air}}) \cdot z}{(T_{\text{air}} + 273.15) \cdot u^2}$$

| 参数 | 符号 | 单位 | 数值 | 说明 |
|-----------|--------|------|-------|-------------|
| 重力加速度 | $g$ | $\mathrm{m/s^2}$ | 9.81 | 固定值 |

### 7.3 Louis (1979) 稳定度修正

$$f_{\text{stab}} = \begin{cases}
\displaystyle\frac{1}{1 + c \cdot \sqrt{-Ri}} & Ri < 0 \quad \text{（不稳定）} \\[12pt]
1 + d \cdot Ri & Ri > 0 \quad \text{（稳定）} \\[12pt]
1 & Ri = 0 \quad \text{（中性）}
\end{cases}$$

修正后的阻力：$r_a^{\text{corr}} = r_a \cdot f_{\text{stab}}$

| 参数 | 符号 | 默认值 | 说明 |
|-----------|--------|---------|-------------|
| 稳定度参数 c | $c$ | 5.0 | Louis (1979) 系数 |
| 稳定度参数 d | $d$ | 5.0 | Louis (1979) 系数 |

### 7.4 阻力裁剪

$$r_a = \mathrm{clip}(r_a, \; r_a^{\text{min}}, \; r_a^{\text{max}})$$

| 参数 | 符号 | 单位 | 默认值 | 范围 |
|-----------|--------|------|---------|-------|
| 最小阻力 | $r_a^{\text{min}}$ | $\mathrm{s/m}$ | 10 | [1, $r_a^{\text{max}}$-1] |
| 最大阻力 | $r_a^{\text{max}}$ | $\mathrm{s/m}$ | 500 | [50, 5000] |

---

## 8. 土壤水分可用性 ($\beta$ 因子)

### 8.1 Beta 计算方法

通过 `BetaMethod` 可选择四种方法：

**方法 0: Noilhan & Planton (ISBA)**

$$\beta = \frac{w_g}{w_{\text{sat}}}$$

**方法 1: 直接法（用户指定）**

$$\beta = \beta_{\text{direct}}$$

**方法 2: Kondo & Saigusa (1994)**

$$\beta = \frac{1}{1 + \exp\left[-b \cdot \left(\frac{w_g}{w_{\text{sat}}} - a\right)\right]}$$

| 质地索引 | 土壤类型 | $a$ (阈值) | $b$ (陡度) |
|:---:|:---:|:---:|:---:|
| 1 | 沙土 | 0.10 | 4.0 |
| 2 | 壤质沙土 | 0.15 | 3.5 |
| 3 | 壤土 | 0.20 | 3.0 |
| 4 | 粉砂壤土 | 0.25 | 2.5 |
| 5 | 黏土 | 0.30 | 2.0 |

**方法 3: 幂律**

$$\beta = \left(\frac{w_g}{w_{\text{sat}}}\right)^{b_{\text{exp}}}$$

| 参数 | 符号 | 单位 | 默认值 | 范围 | 说明 |
|-----------|--------|------|---------|-------|-------------|
| 表层土壤含水量 | $w_g$ | $\mathrm{m^3/m^3}$ | 0.25 | [0, 0.8] | 体积土壤含水量 |
| 田间持水量 | $w_{\text{sat}}$ | $\mathrm{m^3/m^3}$ | 0.35 | [0.01, 0.9] | 饱和含水量/田间持水量 |
| Beta 指数 | $b_{\text{exp}}$ | -- | 1.0 | [0.1, 5.0] | 幂律指数 |
| 最小 beta | $\beta_{\text{min}}$ | -- | 0.05 | [0, 1] | 下限 |

Beta 始终经过裁剪：$\beta = \mathrm{clip}(\beta, \; \beta_{\text{min}}, \; 1.0)$

### 8.2 土壤表面阻力 (ISBA 模型)

$$r_s = r_s^{\text{min}} \cdot \exp\left[a_{\text{sens}} \cdot (1 - \beta)\right]$$

边界条件：
- 当 $\beta \geq 1.0$：$r_s = r_s^{\text{min}}$（饱和，最小阻力）
- 当 $\beta \leq 0.01$：$r_s = r_s^{\text{max}}$（干燥，最大阻力）

| 参数 | 符号 | 单位 | 默认值 | 范围 | 说明 |
|-----------|--------|------|---------|-------|-------------|
| 最小土壤阻力 | $r_s^{\text{min}}$ | $\mathrm{s/m}$ | 50 | [10, 1000] | 湿润土壤表面阻力 |
| 最大土壤阻力 | $r_s^{\text{max}}$ | $\mathrm{s/m}$ | 500 | [$r_s^{\text{min}}$+10, 10000] | 干燥土壤表面阻力 |
| Beta 敏感度 | $a_{\text{sens}}$ | -- | 5.0 | -- | ISBA 敏感度指数 |

---

## 9. 蒸散发转换

### 9.1 LE 转 ET

由潜热通量计算逐时蒸散发量：

$$ET = \frac{LE}{\lambda(T_{\text{air}})} \times 3600 \quad [\mathrm{mm/h}]$$

### 9.2 参考 ET（FAO-56 Penman-Monteith 草地）

参考蒸散发计算采用 FAO-56 标准参数，对应假想的草地参考表面（$h = 0.12$ m）：

$$ET_{\text{ref}} = \frac{1}{\lambda(T_{\text{air}})} \cdot \frac{\Delta \cdot (R_n - G) + \rho C_p \cdot VPD / r_a^{\text{grass}}}{\Delta + \gamma \cdot (1 + r_s^{\text{grass}} / r_a^{\text{grass}})} \times 3600 \quad [\mathrm{mm/h}]$$

固定参考参数：

| 参数 | 数值 | 说明 |
|-----------|-------|-------------|
| $r_a^{\text{grass}}$ | $208 / u$ | 草地空气动力学阻力 [s/m] |
| $r_s^{\text{grass}}$ | 70 | 草地整体表面阻力 [s/m] |

---

## 10. 空间辐射修正（仅空间模拟器）

空间模拟器利用天空视角系数（SVF）、有效 DNI 暴露因子和周围表面属性进行逐点辐射修正。

### 10.1 修正短波辐射 (GHI)

$$GHI_{\text{actual}} = DHI \cdot SVF + DNI \cdot \sin(\alpha) \cdot f_{\text{DNI}} + \rho_{\text{sur}} \cdot GHI_{\text{surround}} \cdot (1 - SVF)$$

其中：

$$GHI_{\text{surround}} = DHI + DNI \cdot \sin(\alpha)$$

**有效 DNI 暴露因子 $f_{\text{DNI}}$（增强版，2026-06-14）：**

替代原有的二值暴露因子 $f_{\text{exp}}$（仅判断遮挡/未遮挡），引入有效 DNI 暴露因子 $f_{\text{DNI}}$，支持三种障碍物类型的差异化透射处理。

**物理逻辑修正（2026-06-14）：** 光线从太阳射向地面（正向），代码使用反向光线追踪（从地面采样点向太阳方向发射射线）。关键物理约束：**不透光物体 (Opaque) 具有绝对遮挡优先权**——如果射线路径上存在任何不透光障碍物，其后面的树木或半透明遮阳构件均处于建筑阴影中，不应再计算 DNI 透射。

判断逻辑（修正后）：
1. 射线路径上**有任何不透光物体** → DNI = 0（完全阻挡）
2. 无 Opaque 时 → 找**从太阳方向最近**的 Tree/Translucent 障碍物
3. 全部无遮挡 → DNI = 全额

| 击中类型 | DNI 贡献 | 物理含义 |
|:---:|:---:|:---|
| 无遮挡 | 1.0 | 全额直接辐射 |
| 不透光物体 (Opaque) | 0.0 | 完全阻挡，其后方 Tree/Translucent 均处于阴影中 |
| 树木细节模型 (Tree) | $\exp(-k \cdot \text{LAD} \cdot s)$ | Beer-Lambert 冠层透射（仅当射线路径无 Opaque 时） |
| 半透明遮阳构件 (Translucent) | $\tau$ | 固定透射率透射（仅当射线路径无 Opaque 时） |

植被冠层透射方程（Beer-Lambert 定律）：

$$I_{\text{transmitted}} = I_{\text{DN}} \cdot \exp(-k \cdot \text{LAD} \cdot s)$$

其中：
- $s$：光线穿过树木冠层的几何路径长度 [m]，通过 `CalculateCanopyPathLength` 方法计算（射线与简化冠层模型的进出交点距离）
- $k$：消光系数 [-]，默认 0.65，典型范围 0.5–0.8（阔叶）、0.3–0.5（针叶）
- $\text{LAD}$：叶面积密度 [m²/m³]，默认 1.0，典型范围 0.5–8.0
- $\tau$：遮阳构件透射率 [-]，默认 0.05

**计算流程（SpatialSoilThermalSimulator.cs）：**

1. 从 `GroundSurfaceConfig.ObstacleSet` 读取分类障碍物设置
2. 调用 `GetAllMeshes()` 获取全部 Mesh 用于 SVF 计算
3. 对每个时刻、每个地面点，调用 `CalculateDNIExposureFactorsBatch()` 计算 $f_{\text{DNI}}$
4. 将 $f_{\text{DNI}}$ 代入短波辐射修正公式计算 $GHI_{\text{actual}}$
5. 修正后的 $GHI_{\text{actual}}$ 作为输入驱动 Force-Restore + Penman-Monteith 模拟

**向后兼容：** 当未连接 ObsSet 组件（无障碍物分类）时，使用传统的 `CalculateExposureFactorsBatch()` 方法，$f_{\text{DNI}} = f_{\text{exp}}$。

### 10.2 修正长波辐射

$$L_{\downarrow}^{\text{actual}} = L_{\text{sky}} \cdot SVF + \varepsilon_{\text{sur}} \cdot \sigma \cdot T_{\text{sur,K}}^4 \cdot (1 - SVF)$$

其中：

$$L_{\text{sky}} = HIR_{\text{EPW}} \cdot SVF$$

| 参数 | 符号 | 单位 | 默认值 | 说明 |
|-----------|--------|------|---------|-------------|
| 天空视角系数 | $SVF$ | -- | 1.0（无障碍物） | [0, 1]；可见天空半球的比例 |
| 有效 DNI 暴露因子 | $f_{\text{DNI}}$ | -- | -- | [0, 1]；综合暴露与透射的 DNI 有效因子 |
| 太阳暴露因子 | $f_{\text{exp}}$ | -- | -- | [0, 1]；二值：太阳直射未被遮挡的比例（遗留字段） |
| 太阳高度角 | $\alpha$ | rad | -- | 由 SPA 太阳位置计算 |
| 周围反射率 | $\rho_{\text{sur}}$ | -- | 0.2 | 周围表面短波反射率 |
| 周围发射率 | $\varepsilon_{\text{sur}}$ | -- | 0.95 | 周围表面长波发射率 |
| 周围温度 | $T_{\text{sur}}$ | $^{\circ}\mathrm{C}$ | $T_{\text{air}}$ | 周围表面温度（用户覆盖值或 EPW 空气温度） |

---

## 11. 输入/输出端口

### 11.1 土壤热设置组件

| 端口 | 名称 | 类型 | 必填 | 说明 |
|------|------|------|----------|-------------|
| 0 | `SoilType` | Integer | 否 | 0=干沙, 1=湿沙, 2=干黏土, 3=湿黏土, 4=沥青, 5=自定义 |
| 1 | `rhoCp` | Number | 否 | 体积热容量 [MJ/(m$^3$K)] |
| 2 | `alb` | Number | 否 | 短波反照率 [--] |
| 3 | `emiss` | Number | 否 | 长波发射率 [--] |
| 4 | `LeMethod` | Integer | 否 | 0=简化法, 1=PenmanMonteith, 2=无潜热 |
| 5 | `leMax` | Number | 否 | 最大潜热通量 [W/m$^2$] |
| 6 | `sub` | Integer | 否 | 每小时子步数 |
| 7 | `AirTemp` | Tree | 否 | 空气温度覆盖值（单值/逐点/时间序列/逐点时间序列） |
| 8 | `RH` | Tree | 否 | 相对湿度覆盖值（与 AirTemp 相同的多模式结构） |

### 11.2 土壤表面设置组件

| 端口 | 名称 | 类型 | 必填 | 说明 |
|------|------|------|----------|-------------|
| 0 | `z_obs` | Number | 否 | 风速测量高度 [m]（默认 2.0） |
| 1 | `z0m` | Number | 否 | 动量粗糙长度 [m]（默认 0.001） |
| 2 | `z0h` | Number | 否 | 标量粗糙长度 [m]（默认 0.0001） |
| 3 | `k` | Number | 否 | 冯·卡门常数（默认 0.41） |
| 4 | `Ra` | Number | 否 | 直接空气动力学阻力覆盖值 [s/m] |
| 5 | `RaMax` | Number | 否 | 最大空气动力学阻力 [s/m] |
| 6 | `RaMin` | Number | 否 | 最小空气动力学阻力 [s/m] |
| 7 | `Stab` | Boolean | 否 | 启用 Louis (1979) 稳定度修正 |
| 8 | `Wind` | Tree | 否 | 风速覆盖值（多模式，封装在 SoilSurSet 中） |

### 11.3 土壤水分设置组件

| 端口 | 名称 | 类型 | 必填 | 说明 |
|------|------|------|----------|-------------|
| 0 | `betaM` | Integer | 否 | 0=Noilhan, 1=直接法, 2=KondoSaigusa, 3=幂律 |
| 1 | `wg` | Number | 否 | 表层土壤含水量 [m$^3$/m$^3$] |
| 2 | `wsat` | Number | 否 | 田间持水量 [m$^3$/m$^3$] |
| 3 | `beta` | Number | 否 | 直接 beta 值（当 betaM=1 时） |
| 4 | `idx` | Integer | 否 | 土壤质地索引 1-5（当 betaM=2 时） |
| 5 | `bExp` | Number | 否 | 幂律指数（当 betaM=3 时） |
| 6 | `rsMin` | Number | 否 | 最小土壤阻力 [s/m] |
| 7 | `rsMax` | Number | 否 | 最大土壤阻力 [s/m] |
| 8 | `Rs` | Number | 否 | 直接土壤阻力覆盖值 [s/m] |
| 9 | `wet` | Boolean | 否 | 强制湿润表面 |

### 11.4 地面表面设置组件（仅空间模式）

| 端口 | 名称 | 类型 | 必填 | 说明 |
|------|------|------|----------|-------------|
| 0 | `GSurf` | Brep | **是** | 地面表面几何体 |
| 1 | `ObsSet` | Generic | 否 | **增强版（2026-06-14）**：分类障碍物设置集（ObstacleSet）。连接 ObsSet 组件。支持不透光建筑、树木冠层透射（Beer-Lambert）、半透明遮阳。向后兼容：可接受 List<Brep> 或 List<Mesh> |
| 2 | `Res` | Number | 否 | 网格分辨率 [m] |
| 3 | `d1` | Number | 否 | 表层深度 [m] |
| 4 | `d2` | Number | 否 | 深层深度 [m] |
| 5 | `HExp` | Number | 否 | 暴露追踪高度 [m] |
| 6 | `SVFN` | Integer | 否 | SVF 采样数 |
| 7 | `RhoSur` | Number | 否 | 周围短波反射率 |
| 8 | `EpsSur` | Number | 否 | 周围长波发射率 |

---

## 12. 温度与湿度之间的物理一致性

当覆盖空气温度而不调整相对湿度时，温度 ($T_{\text{air}}$)、相对湿度 ($RH$) 与实际水汽压 ($e_a$) 之间的物理一致性可能被破坏，因为：

$$e_a = e_s(T_{\text{air}}) \cdot \frac{RH}{100}$$

如果 $T_{\text{air}}$ 改变而 $RH$ 保持不变，$e_a$ 会以非物理的方式变化。为保持一致性，每当覆盖 `AirTemp` 时，应同时提供自定义 `RH` 输入（土壤热设置的第8端口）：

$$RH^{\text{new}} = \frac{e_s(T_{\text{air}}^{\text{original}})}{e_s(T_{\text{air}}^{\text{override}})} \cdot RH^{\text{original}} \cdot 100 \quad \text{（以保持 } e_a \text{ 不变）}$$

`RH` 输入支持与 `AirTemp` 相同的多模式结构：单值、逐点常数、时间序列、或逐点时间序列。

---

## 13. 数值实现细节

### 13.1 子步进与迭代

| 参数 | 默认值 | 范围 | 说明 |
|-----------|---------|-------|-------------|
| SubStepsPerHour | 10 | [1, 60] | 每小时内的子步数 |
| MaxIterations | 20 | -- | 每子步地表能量平衡最大迭代次数 |
| EnergyBalanceTolerance | 0.1 | -- | 收敛容差 [W/m$^2$] |
| PM MaxIterations | 5 | -- | Penman-Monteith G-LE 耦合最大迭代次数 |
| PM Convergence | 1.0 | -- | PM 耦合容差 [W/m$^2$] |
| T1 Convergence | 0.001 | -- | 温度收敛 [K] |

### 13.2 状态变量初始化

$$T_1^{\text{init}} = T_{\text{air}}^{\text{first}} \; \text{或} \; T_{\text{user}}^{\text{surf}}, \qquad T_2^{\text{init}} = T_{\text{mean}}^{\text{annual}} \; \text{或} \; T_{\text{air}}^{\text{first}}$$

---

## 14. 参考文献

1. **Deardorff, J.W.** (1978). Efficient prediction of ground surface temperature and moisture, with inclusion of a layer of vegetation. *J. Geophys. Res.*, 83(C4), 1889-1903.

2. **Noilhan, J. & Planton, S.** (1989). A simple parameterization of land surface processes for meteorological models. *Mon. Wea. Rev.*, 117, 536-549.

3. **Allen, R.G., Pereira, L.S., Raes, D. & Smith, M.** (1998). *Crop Evapotranspiration: Guidelines for Computing Crop Water Requirements*. FAO Irrigation and Drainage Paper 56, Rome, Italy.

4. **Louis, J.F.** (1979). A parametric model of vertical eddy fluxes in the atmosphere. *Bound.-Layer Meteor.*, 17, 187-202.

5. **Kondo, J. & Saigusa, N.** (1994). Modelling the evaporation from bare soil with a formula for vaporization in the soil pores. *J. Meteorol. Soc. Jpn.*, 72(3), 413-420.

6. **Tetens, O.** (1930). Uber einige meteorologische Begriffe. *Z. Geophys.*, 6, 297-309.

7. **Reda, I. & Andreas, A.** (2004). Solar Position Algorithm for Solar Radiation Applications. *Solar Energy*, 76(5), 577-589. https://doi.org/10.1016/j.solener.2003.12.003
