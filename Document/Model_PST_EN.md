# PST (Physiological Subjective Temperature) Module

This module calculates the Physiological Subjective Temperature (PST), Physiological Strain Index (STI), Physiological Strain category (PhS), and adapted mean skin temperature (SkinTemp) for the human body in outdoor environments, based on the MENEX_2005 two-step human heat balance model. The model simulates the change in human thermal sensation from initial contact with ambient conditions to after 15–20 minutes of physiological adaptation, through the heat balance equation of metabolic heat production, radiative balance, convective heat exchange, evaporative heat loss, and respiratory heat loss.

The module consists of three components:
- **PST Weather Settings**: Meteorological parameter configuration component (includes Goff-Gratch vapour pressure calculation and automatic ground temperature estimation)
- **PST Human Settings**: Human physiological parameter configuration component (includes temperature-adaptive clothing insulation formula)
- **PST Simulator**: Core calculation component implementing the MENEX_2005 two-step heat balance method

---

## 1. PST Physical Model (PstSimulator.cs)

### 1.1 Heat Balance Equation

The MENEX_2005 model is based on the human heat balance equation:

$$M + Q + C + E + \text{Res} = S$$

Where:
- $M$: Metabolic heat production [W/m²]
- $Q$: Radiation balance [W/m²] ($Q = R + L$, absorbed solar radiation + net longwave radiation)
- $C$: Convective heat exchange [W/m²]
- $E$: Evaporative heat loss [W/m²]
- $\text{Res}$: Respiratory heat loss [W/m²]
- $S$: Net heat storage [W/m²]

### 1.2 Two-Step Calculation Method

**Step 1 (Initial Steady State):**
Calculate each heat flux component using the initial skin temperature $T_s$ at contact with ambient conditions. Outputs STI (Subjective Temperature Index) and PhS (physiological strain ratio $C/E$).

**Step 2 (Adaptive Steady State):**
Adjust skin temperature based on the evaporative heat loss $E$ from Step 1 (evaporative cooling effect), then recalculate longwave radiation, convection, and evaporation terms using the adjusted skin temperature $T_{sR}$. Absorbed solar radiation $R$, metabolic heat production $M$, and respiratory heat loss $\text{Res}$ remain fixed at their Step 1 values. Outputs PST, PhS category, and adapted skin temperature.

### 1.3 Clothing and Heat Transfer Coefficients

**Clothing insulation $I_{cl}$ [clo]:**

When `AutoClo = true`, the MENEX_2005 temperature-adaptive formula is used:

$$
I_{cl} = \begin{cases}
3.0 & t < -30\,^\circ\text{C} \\
1.691 - 0.0436 \cdot t & -30 \leq t \leq 25\,^\circ\text{C} \\
0.6 & t > 25\,^\circ\text{C}
\end{cases}$$

When `AutoClo = false`, the user-specified `CloValue` is used directly.

**Convective and radiative heat transfer coefficient $h_c$ [W/(m²·K)]:**

$$h_c = (0.013 \cdot p - 0.04 \cdot t - 0.503) \cdot (v + v')^{0.4}$$

Where $p$ is atmospheric pressure [hPa], $t$ is air temperature [°C], $v$ is wind speed [m/s], and $v'$ is walking speed [m/s]. When $h_c < 0.5$, it is clamped to 0.5.

**Clothing heat transfer coefficient $h_c'$ [W/(m²·K)]:**

$$h_c' = \frac{(0.013 \cdot p - 0.04 \cdot t - 0.503) \cdot 0.53}{I_{cl} \cdot \left[1 - 0.27 \cdot (v + v')^{0.4}\right]}$$

**Clothing reduction coefficient $I_{rc}$:**

$$I_{rc} = \frac{h_c'}{h_c' + h_c + 21.55 \times 10^{-8} \cdot T^3}$$

Where $T = 273 + t$ is air temperature [K]. Clamped to $[0.001, 1.0]$.

**Evaporation reduction coefficient $I_e$:**

$$I_e = \frac{h_c'}{h_c' + h_c}$$

Clamped to $[0.001, 1.0]$.

### 1.4 Step 1: Initial Steady State

**Initial mean skin temperature $T_s$ [°C]:**

$$T_s = 26.4 + 0.02138 \cdot \text{MRT} + 0.2095 \cdot t - 0.0185 \cdot f - 0.009 \cdot v + 0.6 \cdot (I_{cl} - 1) + 0.00128 \cdot M$$

Where MRT is mean radiant temperature [°C], $f$ is relative humidity [%], and $M$ is metabolic rate [W/m²]. Constrained to $[22.0, 37.5]$ °C.

**Longwave radiation components:**

Ground longwave radiation:

$$L_g = \varepsilon_s \cdot \sigma \cdot (273 + T_g)^4$$

Atmospheric longwave radiation:

$$L_a = \varepsilon_s \cdot \sigma \cdot (273 + t)^4 \cdot \left(0.82 - 0.25 \times 10^{-0.094 \cdot e}\right)$$

Body longwave emission:

$$L_s = \varepsilon_h \cdot \sigma \cdot (273 + T_s)^4$$

Net longwave radiation:

$$L = (0.5 \cdot L_g + 0.5 \cdot L_a - L_s) \cdot I_{rc}$$

**Absorbed solar radiation $R$ [W/m²]:**

The model derives the total solar radiation absorbed by the human body from MRT:

$$R = \left[\varepsilon_h \cdot \sigma \cdot (273 + \text{MRT})^4 - \varepsilon_s \cdot \sigma \cdot (273 + t)^4\right] \cdot I_{rc}$$

> **Note:** The MRT input should be calculated externally (e.g., via RayMan or SolarCal), already comprehensively accounting for direct radiation, diffuse radiation, ground reflection, and surrounding surface longwave radiation.

**Radiation balance:**

$$Q = R + L$$

**Evaporative heat transfer coefficient $h_e$ [hPa·m²/W]:**

$$h_e = \frac{\left[t \cdot (0.00006 \cdot t - 0.00002 \cdot p + 0.011) + 0.02 \cdot p - 0.773\right] \cdot 0.53}{I_{cl} \cdot \left[1 - 0.27 \cdot (v + v')^{0.4}\right]}$$

When $h_e < 0.01$, it is clamped to 0.01.

**Saturated vapour pressure at skin temperature $e_s$ [hPa]:**

$$e_s = \exp(0.058 \cdot T_s + 2.003)$$

**Skin wettedness $w$:**

$$
w = \begin{cases}
1.0 & T_s > 36.5\,^\circ\text{C} \\
0.001 & T_s < 22.0\,^\circ\text{C} \\
\frac{1.031}{37.5 - T_s} - 0.065 & \text{otherwise}
\end{cases}$$

Clamped to $[0.001, 1.0]$.

**Evaporative heat loss $E$ [W/m²]:**

$$E = h_e \cdot (e - e_s) \cdot w \cdot I_e - \left[0.42 \cdot (M - 58) - 5.04\right]$$

**Convective heat exchange $C$ [W/m²]:**

$$C = h_c \cdot (t - T_s) \cdot I_{rc}$$

**Respiratory heat loss $\text{Res}$ [W/m²]:**

$$\text{Res} = 0.0014 \cdot M \cdot (t - 35) + 0.0173 \cdot M \cdot (0.1 \cdot e - 5.624)$$

**Net heat storage $S$ [W/m²]:**

$$S = M + Q + C + E + \text{Res}$$

**STI (Subjective Temperature Index):**

$$\text{STI} = \begin{cases}
\text{MRT} - \left\{\left[\frac{|S|^{0.75}}{\varepsilon_h \cdot \sigma} + 273^4\right]^{0.25} - 273\right\} & S < 0 \\
\text{MRT} + \left\{\left[\frac{|S|^{0.75}}{\varepsilon_h \cdot \sigma} + 273^4\right]^{0.25} - 273\right\} & S \geq 0
\end{cases}$$

**PhS ratio (physiological strain ratio):**

$$\text{PhS} = \frac{C}{E}$$

When $|E| < 0.001$, sign-dependent extreme values are used (1000 when $E > 0$, -1000 when $E < 0$).

### 1.5 Step 2: Adaptive Steady State

**Skin temperature correction (evaporative cooling effect):**

$$\Delta T_s = \begin{cases}
(E + 50) \cdot 0.066 & E < -50\,\text{W/m}^2 \\
0 & E \geq -50\,\text{W/m}^2
\end{cases}$$

$$T_{sR} = T_s + \Delta T_s$$

Constrained to $[22.0, 37.5]$ °C.

Fixed values (from Step 1): $M$ (metabolic rate), $R$ (absorbed solar radiation), $\text{Res}$ (respiratory heat loss).

**Recalculated radiation components:**

$$L_{sR} = \varepsilon_h \cdot \sigma \cdot (273 + T_{sR})^4$$

$$L_R = (0.5 \cdot L_g + 0.5 \cdot L_a - L_{sR}) \cdot I_{rc}$$

$$Q_R = R + L_R$$

**Inner mean radiant temperature iMRT [°C]:**

$$\text{iMRT} = \left[\frac{R + (L_a + L_g) \cdot 0.5 \cdot I_{rc} + 0.5 \cdot L_s}{\varepsilon_h \cdot \sigma}\right]^{0.25} - 273$$

**Adapted convective heat exchange $C_R$ [W/m²]:**

$$C_R = h_c \cdot (\text{iMRT} - T_{sR}) \cdot I_{rc}$$

**Vapour pressure under clothing $e^*$ [hPa]:**

$$e^* = 6.12 \times 10^{\frac{7.5 \cdot \text{iMRT}}{237.7 + \text{iMRT}}} \times 0.01 \cdot f$$

**Saturated vapour pressure at adapted skin temperature:**

$$e_{sR} = \exp(0.058 \cdot T_{sR} + 2.003)$$

**Adapted skin wettedness $w_R$:**

Same formula as Step 1, using $T_{sR}$ instead of $T_s$.

**Adapted evaporative heat loss $E_R$ [W/m²]:**

$$E_R = h_e \cdot \sqrt{v + v'} \cdot (e^* - e_{sR}) \cdot w_R \cdot I_e - \left[0.42 \cdot (M - 58) - 5.04\right]$$

**Adapted net heat storage $S_R$ [W/m²]:**

$$S_R = M + Q_R + C_R + E_R + \text{Res}$$

### 1.6 PST (Physiological Subjective Temperature)

$$\text{PST} = \begin{cases}
\text{iMRT} - \left\{\left[\frac{|S_R|^{0.75}}{\varepsilon_h \cdot \sigma} + 273^4\right]^{0.25} - 273\right\} & S_R < 0 \\
\text{iMRT} + \left\{\left[\frac{|S_R|^{0.75}}{\varepsilon_h \cdot \sigma} + 273^4\right]^{0.25} - 273\right\} & S_R \geq 0
\end{cases}$$

### 1.7 Total Heat Loss

$$\text{HeatLoss} = M - S_R = -(Q_R + C_R + E_R + \text{Res})$$

### 1.8 PhS Categories

| PhS Ratio Range | Category Description |
|:---:|:---|
| $< 0$ | Extreme hot strain |
| $0 \sim 0.24$ | Great hot strain |
| $0.25 \sim 0.74$ | Moderate hot strain |
| $0.75 \sim 1.50$ | Thermoneutral (slight strain) |
| $1.51 \sim 4.00$ | Moderate cold strain |
| $4.01 \sim 8.00$ | Great cold strain |
| $> 8.00$ | Extreme cold strain |

### 1.9 PST Thermal Sensation Scale

| PST [°C] | Thermal Sensation |
|:---:|:---|
| $<-36$ | Frosty |
| $-36 \sim -16.1$ | Very cold |
| $-16 \sim 4$ | Cold |
| $4.1 \sim 14$ | Cool |
| $14.1 \sim 24$ | Comfortable |
| $24.1 \sim 34$ | Warm |
| $34.1 \sim 44$ | Hot |
| $44.1 \sim 54$ | Very hot |
| $> 54$ | Sweltering |

---

## 2. Weather Settings Component (PstWeatherSettings.cs)

### 2.1 Description

Configures meteorological parameters for PST simulation. Supports automatic calculation of actual vapour pressure from relative humidity using the Goff-Gratch formula, and automatic estimation of ground surface temperature based on cloud cover.

### 2.2 Input Parameters

| Index | Parameter | ID | Unit | Default | Description |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | AirTemp | Ta | °C | 26.0 | Air dry-bulb temperature |
| 1 | WindSpeed | Va | m/s | 0.5 | Wind speed at 1.1 m height |
| 2 | RH | RH | % | 50.0 | Relative humidity; required regardless of AutoVP setting |
| 3 | VP | VP | hPa | — | Actual vapour pressure; required when AutoVP = false |
| 4 | AutoVP | AutoVP | — | true | If true, calculate vapour pressure from RH using Goff-Gratch; if false, use direct VP input |
| 5 | Pressure | P | hPa | 1013.25 | Atmospheric pressure; if zero, standard 1013.25 hPa is used |
| 6 | MRT | MRT | °C | — | Mean radiant temperature; should be calculated externally |
| 7 | CloudCover | CC | % | 0.0 | Cloud cover 0–100%; used to estimate Tg when AutoTg = true |
| 8 | AutoTg | AutoTg | — | true | If true, estimate Tg from cloud cover using MENEX_2005 formula; if false, use Tg input |
| 9 | Tg | Tg | °C | 26.0 | Ground surface temperature; effective when AutoTg = false |

### 2.3 Goff-Gratch Saturated Vapour Pressure Formula

When `AutoVP = true`, the actual vapour pressure is calculated from air temperature and relative humidity using the Goff-Gratch formula.

**Over water ($t \geq 0$ °C):**

$$\begin{aligned}
\log_{10} e_s = &-7.90298 \cdot \left(\frac{T_{st}}{T} - 1\right) + 5.02808 \cdot \log_{10}\left(\frac{T_{st}}{T}\right) \\
&- 1.3816 \times 10^{-7} \cdot \left(10^{11.344 \cdot (1 - T/T_{st})} - 1\right) \\
&+ 8.1328 \times 10^{-3} \cdot \left(10^{-3.49149 \cdot (T_{st}/T - 1)} - 1\right) + \log_{10} e_{st}
\end{aligned}$$

**Over ice ($t < 0$ °C):**

$$\log_{10} e_s = -9.09718 \cdot \left(\frac{T_0}{T} - 1\right) - 3.56654 \cdot \log_{10}\left(\frac{T_0}{T}\right) + 0.876793 \cdot \left(1 - \frac{T}{T_0}\right) + \log_{10} e_0$$

Actual vapour pressure:

$$e = e_s \cdot \frac{\text{RH}}{100}$$

Where:
- $T = 273.15 + t$ [K]
- $T_{st} = 373.16$ K (steam point temperature), $e_{st} = 1013.246$ hPa
- $T_0 = 273.16$ K (triple point temperature), $e_0 = 6.1071$ hPa

### 2.4 Ground Temperature Estimation (AutoTg)

When `AutoTg = true`, ground surface temperature is estimated from cloud cover $N$ and air temperature:

$$T_g = \begin{cases}
t & N \geq 80\% \\
1.25 \cdot t & N < 80\% \text{ and } t \geq 0\,^\circ\text{C} \\
0.9 \cdot t & N < 80\% \text{ and } t < 0\,^\circ\text{C}
\end{cases}$$

### 2.5 Output Parameters

| Index | Parameter | Description |
|:---:|:---:|:---|
| 0 | WeatherSet | Structured weather data set, connect to PST Simulator WS input |

---

## 3. Human Settings Component (PstHumanSettings.cs)

### 3.1 Description

Configures human physiological parameters for PST simulation. Supports temperature-adaptive adjustment of clothing insulation.

### 3.2 Input Parameters

| Index | Parameter | ID | Unit | Default | Description |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | MetRate | M | W/m² | 135 | Metabolic heat production; default corresponds to walking 4 km/h (ISO 8996); range 58–400 W/m² |
| 1 | AutoClo | AutoClo | — | false | If true, auto-adjust clothing insulation by MENEX_2005 formula (ignores CloValue); if false, uses CloValue |
| 2 | CloValue | Icl | clo | 0.8 | Clothing insulation; summer typical value; ignored when AutoClo = true |
| 3 | AlbedoClo | Alb | % | 30 | Clothing surface solar reflectance/albedo; typical summer clothing; range 10–90% |
| 4 | WalkSpeed | Vw | m/s | 1.1 | Velocity of person's motion relative to air; default ~4 km/h walking speed |

> **Note:** Mean skin temperature $T_s$ and skin wettedness $w$ are internal iterative variables of the MENEX_2005 model and are not exposed as inputs.

### 3.3 Output Parameters

| Index | Parameter | Description |
|:---:|:---:|:---|
| 0 | HumanSet | Structured human physiological parameter set, connect to PST Simulator HS input |

---

## 4. PST Simulator Component (PstSimulator.cs)

### 4.1 Input Parameters

| Index | Parameter | Type | Description |
|:---:|:---:|:---:|:---|
| 0 | WeatherSet | Generic | Structured weather data from PST Weather Settings component |
| 1 | HumanSet | Generic | Structured human parameter data from PST Human Settings component |

### 4.2 Output Parameters

| Index | Parameter | ID | Description |
|:---:|:---:|:---:|:---|
| 0 | PST | PST | Physiological Subjective Temperature [°C]; final thermal sensation index after 15–20 min adaptation |
| 1 | STI | STI | Subjective Temperature Index [dimensionless]; $C/E$ ratio indicating direction and intensity of thermoregulatory adaptation |
| 2 | PhS | PhS | Physiological Strain category string; derived from STI value |
| 3 | HeatLoss | HL | Total heat loss from the body [W/m²]; $\text{HL} = M - S_R$ |
| 4 | SkinTemp | Ts | Mean skin temperature after adaptation [°C]; $T_{sR}$ |

---

## 5. Key Parameter Physical Constraints

| Parameter | Lower Limit | Upper Limit | Description |
|:---:|:---:|:---:|:---|
| $I_{cl}$ | 0.1 | 3.5 | Clothing insulation [clo] |
| $T_s$ / $T_{sR}$ | 22.0 | 37.5 | Skin temperature [°C] |
| $w$ / $w_R$ | 0.001 | 1.0 | Skin wettedness |
| $I_{rc}$ | 0.001 | 1.0 | Clothing reduction coefficient |
| $I_e$ | 0.001 | 1.0 | Evaporation reduction coefficient |
| $h_c$ | 0.5 | — | Convective and radiative HTC [W/(m²·K)] |
| $h_c'$ | 0.1 | — | Clothing heat transfer coefficient [W/(m²·K)] |
| $h_e$ | 0.01 | — | Evaporative HTC [hPa·m²/W] |
| MetRate | 58 | — | Metabolic rate [W/m²] |
| RH | 0 | 100 | Relative humidity [%] |
| CloudCover | 0 | 100 | Cloud cover [%] |
| Alb | 0 | 100 | Clothing albedo [%] |

---

## 6. References

1. Blazejczyk, K. (2005). MENEX_2005 — the updated version of man-environment heat exchange model. *Proceedings of the 11th International Conference on Environmental Ergonomics*, Ystad, Sweden, 222–225.

2. Blazejczyk, K. (1994). New climatological- and -physiological model of the human heat balance outdoor (MENEX). *Zeszyty IGiPZ PAN*, 28, 27–58.

3. Fanger, P.O. (1970). *Thermal Comfort: Analysis and Applications in Environmental Engineering*. McGraw-Hill, New York.

4. ISO 8996 (2004). Ergonomics of the thermal environment — Determination of metabolic rate. International Organization for Standardization, Geneva.

5. Goff, J.A., & Gratch, S. (1946). Low-pressure properties of water from -160 to 212 F. *Transactions of the American Society of Heating and Ventilating Engineers*, 95–122.

6. World Meteorological Organization (1988). General meteorological standards and recommended practices, Appendix A, *WMO Technical Regulations*, WMO-No. 49.
