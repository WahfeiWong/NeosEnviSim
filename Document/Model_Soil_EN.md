# Soil Thermal Physics Module

## 1. Model Overview

The soil thermal physics module simulates hourly ground surface temperature and surface energy balance using a coupled Force-Restore + Penman-Monteith (single-source) approach. The model consists of three core components:

| Component | Method | Reference |
|-----------|--------|-----------|
| Soil temperature dynamics | Force-Restore Method | Deardorff (1978); Noilhan & Planton (1989) |
| Latent heat flux | Penman-Monteith (single-source) | Allen et al. (1998) FAO-56; ISBA |
| Aerodynamic resistance | Log-law with stability correction | Louis (1979) |
| Solar position | SPA (Solar Position Algorithm) | Reda & Andreas (2004), NREL |

The module provides two simulators:
- **SoilThermalSimulator** (`SoilSim`): Single-point simulation
- **SpatialSoilThermalSimulator** (`SpSoilSim`): Multi-point spatial simulation with per-point radiation correction (SVF, solar exposure, surrounding reflectance/emissivity)

---

## 2. Force-Restore Soil Temperature Model

### 2.1 Top Layer Temperature Update

The ground surface temperature $T_1$ (top layer) is updated via the Force-Restore equation:

$$\frac{dT_1}{dt} = C_{flux} \cdot G - C_{restore} \cdot (T_1 - T_2)$$

In discrete form (sub-step iteration):

$$T_1^{new} = T_1^{prev} + \Delta t \cdot \left[ C_{flux} \cdot G - C_{restore} \cdot (T_1^{guess} - T_2^{prev}) \right]$$

| Parameter | Symbol | Unit | Description |
|-----------|--------|------|-------------|
| Top layer temperature | $T_1$ | $^{\circ}\mathrm{C}$ | Ground surface temperature (state variable) |
| Deep layer temperature | $T_2$ | $^{\circ}\mathrm{C}$ | Deep soil temperature (state variable) |
| Ground heat flux | $G$ | $\mathrm{W/m^2}$ | Net heat flux into the soil |
| Time step | $\Delta t$ | $\mathrm{s}$ | Sub-step duration $= 3600 / N_{sub} / N_{hourly}$ |
| Sub-steps per hour | $N_{sub}$ | -- | Default 10, range [1, 60] |

### 2.2 Deep Layer Temperature Update

$$\frac{dT_2}{dt} = C_{flux2} \cdot G + C_{deep} \cdot (T_1 - T_2)$$

Discrete form:

$$T_2^{new} = T_2^{prev} + \Delta t \cdot \left[ C_{flux2} \cdot G_{stored} + C_{deep} \cdot (T_1 - T_2^{prev}) \right]$$

### 2.3 Force-Restore Coefficients

**Restoration coefficient** (Deardorff 1978; Noilhan & Planton 1989, Eq.24):

$$C_{restore} = \frac{2\pi}{\tau_1}$$

**Deep layer restoration coefficient** (Noilhan & Planton 1989, Eq.25):

$$C_{deep} = \frac{2\pi}{\tau_2}$$

**Flux coefficients**:

$$C_{flux} = \frac{2}{\rho C \cdot d_1}, \qquad C_{flux2} = \frac{1}{\rho C \cdot d_2}$$

**Time constants**:

$$\tau_1 = 86400 \; \mathrm{s} \; (24 \; \mathrm{h}), \qquad \tau_2 = \tau_1 \cdot \frac{d_2}{0.5}$$

with clipping: $\tau_2 = \mathrm{clip}(\tau_2, \; \tau_1, \; 10\tau_1)$

| Parameter | Symbol | Unit | Description | Default | Range |
|-----------|--------|------|-------------|---------|-------|
| Volumetric heat capacity | $\rho C$ | $\mathrm{MJ/(m^3 \cdot K)}$ | Soil heat storage capacity | 1.4 | [0.1, 10] |
| Top layer depth | $d_1$ | $\mathrm{m}$ | Thickness of surface soil layer | 0.05 | [0.001, 1.0] |
| Deep layer depth | $d_2$ | $\mathrm{m}$ | Thickness of deep soil layer | 0.5 | [0.001, 5.0] |
| Diurnal time constant | $\tau_1$ | $\mathrm{s}$ | 24-hour period | 86400 | Fixed |

> **Note:** $\rho C$ is converted to $\mathrm{J/(m^3 \cdot K)}$ internally: $\rho C_{SI} = \rho C \times 10^6$

### 2.4 Soil Thermal Property Presets

| Soil Type | $\lambda$ (W/mK) | $\rho C$ (MJ/m$^3$K) | $\alpha$ (m$^2$/s) | Albedo | Emissivity |
|-----------|:---:|:---:|:---|:---:|:---:|
| Dry Sand | 0.3 | 1.3 | $0.23 \times 10^{-6}$ | 0.25 | 0.95 |
| Wet Sand | 2.5 | 2.5 | $1.00 \times 10^{-6}$ | 0.20 | 0.95 |
| Dry Clay | 0.5 | 1.4 | $0.36 \times 10^{-6}$ | 0.20 | 0.95 |
| Wet Clay | 2.0 | 2.8 | $0.71 \times 10^{-6}$ | 0.15 | 0.95 |
| Asphalt/Concrete | 1.25 | 2.15 | $0.58 \times 10^{-6}$ | 0.15 | 0.95 |
| Custom | User-defined | User-defined | User-defined | User | User |

---

## 3. Surface Energy Balance

### 3.1 Energy Balance Equation

$$R_n = H + LE + G$$

| Flux | Symbol | Unit | Description |
|------|--------|------|-------------|
| Net radiation | $R_n$ | $\mathrm{W/m^2}$ | Net radiative energy at surface |
| Sensible heat | $H$ | $\mathrm{W/m^2}$ | Convective heat transfer to atmosphere |
| Latent heat | $LE$ | $\mathrm{W/m^2}$ | Evapotranspiration energy consumption |
| Ground heat flux | $G$ | $\mathrm{W/m^2}$ | Heat conducted into soil (residual) |

### 3.2 Net Shortwave Radiation

$$R_{n,sw} = (1 - \alpha) \cdot \frac{GHI_{raw}}{\max(0.01, \Delta t_{hours})}$$

| Parameter | Symbol | Unit | Description |
|-----------|--------|------|-------------|
| Surface albedo | $\alpha$ | -- | Shortwave reflectance | 
| Global horizontal irradiance | $GHI_{raw}$ | $\mathrm{Wh/m^2}$ | From EPW or override (hourly accumulated) |
| Time step factor | -- | -- | Converts hourly accumulation to instantaneous rate |

> **Note:** The $GHI_{raw}$ value from EPW represents accumulation over the previous hour. The `timeStepFactor` $= 1 / \max(0.01, \Delta t_{hours})$ converts this to an average rate.

### 3.3 Net Longwave Radiation

$$R_{n,lw} = \varepsilon_s \cdot (L_{\downarrow} - \sigma T_{g,K}^4)$$

$$R_n = R_{n,sw} + R_{n,lw}$$

| Parameter | Symbol | Unit | Description | Default |
|-----------|--------|------|-------------|---------|
| Surface emissivity | $\varepsilon_s$ | -- | Longwave emissivity of ground | 0.95 |
| Downward longwave | $L_{\downarrow}$ | $\mathrm{W/m^2}$ | From EPW HIR or override | EPW |
| Stefan-Boltzmann constant | $\sigma$ | $\mathrm{W/(m^2 \cdot K^4)}$ | $5.67 \times 10^{-8}$ | Fixed |
| Ground temperature (K) | $T_{g,K}$ | $\mathrm{K}$ | $T_g + 273.15$ | -- |

### 3.4 Sensible Heat Flux

$$H = \rho C_p \cdot \frac{T_g - T_{air}}{r_a}$$

$$\rho C_p = \rho_{air} \cdot 1004$$

| Parameter | Symbol | Unit | Description |
|-----------|--------|------|-------------|
| Air density | $\rho_{air}$ | $\mathrm{kg/m^3}$ | Computed from ideal gas law (see Section 7) |
| Specific heat of air | $C_p$ | $\mathrm{J/(kg \cdot K)}$ | 1004 (dry air at constant pressure) |
| Ground temperature | $T_g$ | $^{\circ}\mathrm{C}$ | $T_1$ from Force-Restore |
| Air temperature | $T_{air}$ | $^{\circ}\mathrm{C}$ | EPW dry bulb or user override |
| Aerodynamic resistance | $r_a$ | $\mathrm{s/m}$ | Computed from log-law (see Section 8) |

---

## 4. Latent Heat Flux (LE)

The module supports three LE calculation methods selected via `LatentHeatMethod`:

### 4.1 Method 1: Penman-Monteith (Recommended)

The Penman-Monteith equation for a single-source (bare soil/uniform surface):

$$LE = \frac{\Delta \cdot (R_n - G) + \rho C_p \cdot VPD / r_a}{\Delta + \gamma \cdot (1 + r_s / r_a)}$$

**Iterative coupling with G:** Since $G = R_n - H - LE$, $G$ appears on both sides. The model iterates (max 5 iterations with relaxation factor 0.5):

$$G^{new} = R_n - H - LE^{(k)}, \qquad G^{(k+1)} = 0.5 \cdot G^{(k)} + 0.5 \cdot G^{new}$$

Convergence criterion: $|G^{new} - G^{(k)}| < 1.0 \; \mathrm{W/m^2}$

| Parameter | Symbol | Unit | Description | Source |
|-----------|--------|------|-------------|--------|
| Slope of saturation vapor pressure curve | $\Delta$ | $\mathrm{kPa/^{\circ}C}$ | Section 6.2 | Tetens derivative |
| Psychrometric constant | $\gamma$ | $\mathrm{kPa/^{\circ}C}$ | Section 7 | From air pressure |
| Vapor pressure deficit | $VPD$ | $\mathrm{kPa}$ | $e_s(T_g) - e_a$ | Section 6 |
| Soil surface resistance | $r_s$ | $\mathrm{s/m}$ | Section 9.2 | ISBA model |

**LE clipping and energy redistribution:**

When $LE$ exceeds the physical upper bound $LE_{max}$ (default 1000 W/m$^2$):

$$LE_{clipped} = \mathrm{clip}(LE, \; 0, \; LE_{max})$$

$$LE_{excess} = LE - LE_{clipped}$$

The excess energy is redistributed (60% to G, 40% to H) to maintain energy balance:

$$G_{final} = G + 0.6 \cdot LE_{excess}, \qquad H_{final} = H + 0.4 \cdot LE_{excess}$$

### 4.2 Method 2: Simplified (Backward Compatible)

$$LE = \beta_{moist} \cdot \frac{\rho_{air} C_p}{\gamma} \cdot \frac{VPD}{r_a}$$

with lower bound on $r_a$: $r_a \geq 10 \; \mathrm{s/m}$

| Parameter | Symbol | Unit | Default | Range |
|-----------|--------|------|---------|-------|
| Moisture availability | $\beta_{moist}$ | -- | 0.3 | [0, 1] |

### 4.3 Method 3: No Latent Heat

$$LE = 0, \qquad \beta = 0, \qquad r_s = 0$$

Useful for sensitivity analysis and dry surface conditions.

---

## 5. Thermodynamic Properties

### 5.1 Saturation Vapor Pressure (Tetens, 1930)

$$e_s(T) = 0.6108 \cdot \exp\left(\frac{17.27 \cdot T}{T + 237.3}\right)$$

| Parameter | Symbol | Unit | Description |
|-----------|--------|------|-------------|
| Temperature | $T$ | $^{\circ}\mathrm{C}$ | Air or ground temperature |
| Saturation vapor pressure | $e_s(T)$ | $\mathrm{kPa}$ | At temperature $T$ |

### 5.2 Slope of Saturation Vapor Pressure Curve

$$\Delta(T) = \frac{4098 \cdot e_s(T)}{(T + 237.3)^2}$$

### 5.3 Actual Vapor Pressure

From relative humidity:

$$e_a = e_s(T_{air}) \cdot \frac{RH}{100}$$

From RH override (when user provides custom RH):

$$e_a = e_s(T_{air}^{override}) \cdot \frac{RH^{override}}{100}$$

| Parameter | Symbol | Unit | Description | Source |
|-----------|--------|------|-------------|--------|
| Relative humidity | $RH$ | % | From EPW or user override | EPW field #9 or `RH` input |

### 5.4 Vapor Pressure Deficit (VPD)

$$VPD = \max(0, \; e_s(T_g) - e_a)$$

### 5.5 Temperature-Corrected Latent Heat of Vaporization

$$\lambda(T) = 2.501 \times 10^6 - 2361 \cdot T$$

| Parameter | Symbol | Unit | Description |
|-----------|--------|------|-------------|
| Latent heat of vaporization | $\lambda$ | $\mathrm{J/kg}$ | Temperature-dependent |

---

## 6. Air Density and Psychrometric Constant

### 6.1 Air Density (Ideal Gas Law)

$$\rho_{air} = \frac{P_a \times 1000}{R_d \cdot (T_{air} + 273.15)}$$

| Parameter | Symbol | Unit | Value | Description |
|-----------|--------|------|-------|-------------|
| Atmospheric pressure | $P_a$ | $\mathrm{kPa}$ | EPW or 101.325 | Station pressure |
| Gas constant for dry air | $R_d$ | $\mathrm{J/(kg \cdot K)}$ | 287.05 | Fixed |
| Air temperature | $T_{air}$ | $^{\circ}\mathrm{C}$ | EPW or override | -- |

### 6.2 Psychrometric Constant

$$\gamma = 0.665 \times 10^{-3} \cdot P_a$$

| Parameter | Symbol | Unit | Description |
|-----------|--------|------|-------------|
| Psychrometric constant | $\gamma$ | $\mathrm{kPa/^{\circ}C}$ | Function of air pressure |

### 6.3 Atmospheric Pressure Fallback

When EPW pressure is missing (marked as 9999 or < 10 kPa):

$$P_a = 101.325 \; \mathrm{kPa} \quad \text{(standard sea level pressure)}$$

---

## 7. Aerodynamic Resistance

### 7.1 Log-Law Resistance

$$r_a = \frac{\ln(z/z_{0m}) \cdot \ln(z/z_{0h})}{k^2 \cdot u}$$

| Parameter | Symbol | Unit | Default | Range | Description |
|-----------|--------|------|---------|-------|-------------|
| Wind measurement height | $z$ | $\mathrm{m}$ | 2.0 | [0.1, 100] | Height of wind speed measurement |
| Momentum roughness length | $z_{0m}$ | $\mathrm{m}$ | 0.001 | [$10^{-6}$, 10] | Bare soil: 0.001; Short grass: 0.01; Urban: 0.1-1.0 |
| Scalar roughness length | $z_{0h}$ | $\mathrm{m}$ | 0.0001 | [$10^{-7}$, 1] | Typically $z_{0m}/10$ |
| Von Karman constant | $k$ | -- | 0.41 | [0.3, 0.5] | Universal constant |
| Wind speed | $u$ | $\mathrm{m/s}$ | EPW or override | $\geq 0.1$ | Clipped to minimum 0.1 m/s |

### 7.2 Richardson Number (Atmospheric Stability)

$$Ri = \frac{g \cdot (T_g - T_{air}) \cdot z}{(T_{air} + 273.15) \cdot u^2}$$

| Parameter | Symbol | Unit | Value | Description |
|-----------|--------|------|-------|-------------|
| Gravitational acceleration | $g$ | $\mathrm{m/s^2}$ | 9.81 | Fixed |

### 7.3 Louis (1979) Stability Correction

$$f_{stab} = \begin{cases}
\displaystyle\frac{1}{1 + c \cdot \sqrt{-Ri}} & Ri < 0 \quad \text{(unstable)} \\[12pt]
1 + d \cdot Ri & Ri > 0 \quad \text{(stable)} \\[12pt]
1 & Ri = 0 \quad \text{(neutral)}
\end{cases}$$

Corrected resistance: $r_a^{corr} = r_a \cdot f_{stab}$

| Parameter | Symbol | Default | Description |
|-----------|--------|---------|-------------|
| Stability parameter c | $c$ | 5.0 | Louis (1979) coefficient |
| Stability parameter d | $d$ | 5.0 | Louis (1979) coefficient |

### 7.4 Resistance Clipping

$$r_a = \mathrm{clip}(r_a, \; r_a^{min}, \; r_a^{max})$$

| Parameter | Symbol | Unit | Default | Range |
|-----------|--------|------|---------|-------|
| Minimum resistance | $r_a^{min}$ | $\mathrm{s/m}$ | 10 | [1, $r_a^{max}$-1] |
| Maximum resistance | $r_a^{max}$ | $\mathrm{s/m}$ | 500 | [50, 5000] |

---

## 8. Soil Moisture Availability ($\beta$ Factor)

### 8.1 Beta Calculation Methods

Four methods are available via `BetaMethod`:

**Method 0: Noilhan & Planton (ISBA)**

$$\beta = \frac{w_g}{w_{sat}}$$

**Method 1: Direct (User-specified)**

$$\beta = \beta_{direct}$$

**Method 2: Kondo & Saigusa (1994)**

$$\beta = \frac{1}{1 + \exp\left[-b \cdot \left(\frac{w_g}{w_{sat}} - a\right)\right]}$$

| Texture Index | Soil Type | $a$ (threshold) | $b$ (steepness) |
|:---:|:---:|:---:|:---:|
| 1 | Sand | 0.10 | 4.0 |
| 2 | Loamy Sand | 0.15 | 3.5 |
| 3 | Loam | 0.20 | 3.0 |
| 4 | Silt Loam | 0.25 | 2.5 |
| 5 | Clay | 0.30 | 2.0 |

**Method 3: Power Law**

$$\beta = \left(\frac{w_g}{w_{sat}}\right)^{b_{exp}}$$

| Parameter | Symbol | Unit | Default | Range | Description |
|-----------|--------|------|---------|-------|-------------|
| Surface soil moisture | $w_g$ | $\mathrm{m^3/m^3}$ | 0.25 | [0, 0.8] | Volumetric soil water content |
| Field capacity | $w_{sat}$ | $\mathrm{m^3/m^3}$ | 0.35 | [0.01, 0.9] | Saturation/field capacity |
| Beta exponent | $b_{exp}$ | -- | 1.0 | [0.1, 5.0] | Power law exponent |
| Minimum beta | $\beta_{min}$ | -- | 0.05 | [0, 1] | Lower bound |

Beta is always clipped: $\beta = \mathrm{clip}(\beta, \; \beta_{min}, \; 1.0)$

### 8.2 Soil Surface Resistance (ISBA Model)

$$r_s = r_s^{min} \cdot \exp\left[a_{sens} \cdot (1 - \beta)\right]$$

With boundary conditions:
- When $\beta \geq 1.0$: $r_s = r_s^{min}$ (saturated, minimum resistance)
- When $\beta \leq 0.01$: $r_s = r_s^{max}$ (dry, maximum resistance)

| Parameter | Symbol | Unit | Default | Range | Description |
|-----------|--------|------|---------|-------|-------------|
| Min soil resistance | $r_s^{min}$ | $\mathrm{s/m}$ | 50 | [10, 1000] | Wet soil surface resistance |
| Max soil resistance | $r_s^{max}$ | $\mathrm{s/m}$ | 500 | [$r_s^{min}$+10, 10000] | Dry soil surface resistance |
| Beta sensitivity | $a_{sens}$ | -- | 5.0 | -- | ISBA sensitivity index |

---

## 9. Evapotranspiration Conversion

### 9.1 LE to ET

Hourly evapotranspiration from latent heat flux:

$$ET = \frac{LE}{\lambda(T_{air})} \times 3600 \quad [\mathrm{mm/h}]$$

### 9.2 Reference ET (FAO-56 Penman-Monteith for Grass)

The reference ET calculation uses the FAO-56 standard parameters for a hypothetical grass reference surface ($h = 0.12$ m):

$$ET_{ref} = \frac{1}{\lambda(T_{air})} \cdot \frac{\Delta \cdot (R_n - G) + \rho C_p \cdot VPD / r_a^{grass}}{\Delta + \gamma \cdot (1 + r_s^{grass} / r_a^{grass})} \times 3600 \quad [\mathrm{mm/h}]$$

With fixed reference parameters:

| Parameter | Value | Description |
|-----------|-------|-------------|
| $r_a^{grass}$ | $208 / u$ | Grass aerodynamic resistance [s/m] |
| $r_s^{grass}$ | 70 | Bulk surface resistance for grass [s/m] |

---

## 10. Spatial Radiation Correction (Spatial Simulator Only)

The spatial simulator applies per-point radiation corrections using sky view factor (SVF), solar exposure factor, and surrounding surface properties.

### 10.1 Corrected Shortwave (GHI)

$$GHI_{actual} = DHI \cdot SVF + DNI \cdot \sin(\alpha) \cdot f_{exp} + \rho_{sur} \cdot GHI_{surround} \cdot (1 - SVF)$$

where:

$$GHI_{surround} = DHI + DNI \cdot \sin(\alpha)$$

### 10.2 Corrected Longwave

$$L_{\downarrow}^{actual} = L_{sky} \cdot SVF + \varepsilon_{sur} \cdot \sigma \cdot T_{sur,K}^4 \cdot (1 - SVF)$$

where:

$$L_{sky} = HIR_{EPW} \cdot SVF$$

| Parameter | Symbol | Unit | Default | Description |
|-----------|--------|------|---------|-------------|
| Sky View Factor | $SVF$ | -- | 1.0 (no obstacles) | [0, 1]; fraction of sky hemisphere visible |
| Solar exposure factor | $f_{exp}$ | -- | -- | [0, 1]; fraction of direct sun unobstructed |
| Solar altitude | $\alpha$ | rad | -- | From SPA solar position |
| Surround reflectance | $\rho_{sur}$ | -- | 0.2 | Shortwave reflectance of surrounding surfaces |
| Surround emissivity | $\varepsilon_{sur}$ | -- | 0.95 | Longwave emissivity of surrounding surfaces |
| Surround temperature | $T_{sur}$ | $^{\circ}\mathrm{C}$ | $T_{air}$ | Surrounding surface temperature (user override or EPW air temp) |

---

## 11. Input/Output Ports

### 11.1 Soil Thermal Settings Component

| Port | Name | Type | Required | Description |
|------|------|------|----------|-------------|
| 0 | `SoilType` | Integer | No | 0=DrySand, 1=WetSand, 2=DryClay, 3=WetClay, 4=Asphalt, 5=Custom |
| 1 | `rhoCp` | Number | No | Volumetric heat capacity [MJ/(m$^3$K)] |
| 2 | `alb` | Number | No | Shortwave albedo [--] |
| 3 | `emiss` | Number | No | Longwave emissivity [--] |
| 4 | `LeMethod` | Integer | No | 0=Simplified, 1=PenmanMonteith, 2=NoLatentHeat |
| 5 | `leMax` | Number | No | Maximum latent heat flux [W/m$^2$] |
| 6 | `sub` | Integer | No | Sub-steps per hour |
| 7 | `AirTemp` | Tree | No | Air temperature override (single/per-point/time-series/per-point-time-series) |
| 8 | `RH` | Tree | No | Relative humidity override (same multi-mode structure as AirTemp) |

### 11.2 Soil Surface Settings Component

| Port | Name | Type | Required | Description |
|------|------|------|----------|-------------|
| 0 | `z_obs` | Number | No | Wind measurement height [m] (default 2.0) |
| 1 | `z0m` | Number | No | Momentum roughness length [m] (default 0.001) |
| 2 | `z0h` | Number | No | Scalar roughness length [m] (default 0.0001) |
| 3 | `k` | Number | No | Von Karman constant (default 0.41) |
| 4 | `Ra` | Number | No | Direct aerodynamic resistance override [s/m] |
| 5 | `RaMax` | Number | No | Maximum aerodynamic resistance [s/m] |
| 6 | `RaMin` | Number | No | Minimum aerodynamic resistance [s/m] |
| 7 | `Stab` | Boolean | No | Enable Louis (1979) stability correction |
| 8 | `Wind` | Tree | No | Wind speed override (multi-mode, encapsulated in SoilSurSet) |

### 11.3 Soil Moisture Settings Component

| Port | Name | Type | Required | Description |
|------|------|------|----------|-------------|
| 0 | `betaM` | Integer | No | 0=Noilhan, 1=Direct, 2=KondoSaigusa, 3=PowerLaw |
| 1 | `wg` | Number | No | Surface soil moisture [m$^3$/m$^3$] |
| 2 | `wsat` | Number | No | Field capacity [m$^3$/m$^3$] |
| 3 | `beta` | Number | No | Direct beta value (when betaM=1) |
| 4 | `idx` | Integer | No | Soil texture index 1-5 (when betaM=2) |
| 5 | `bExp` | Number | No | Power law exponent (when betaM=3) |
| 6 | `rsMin` | Number | No | Min soil resistance [s/m] |
| 7 | `rsMax` | Number | No | Max soil resistance [s/m] |
| 8 | `Rs` | Number | No | Direct soil resistance override [s/m] |
| 9 | `wet` | Boolean | No | Force wet surface |

### 11.4 Ground Surface Settings Component (Spatial Only)

| Port | Name | Type | Required | Description |
|------|------|------|----------|-------------|
| 0 | `GSurf` | Brep | **Yes** | Ground surface geometry |
| 1 | `Obst` | Brep | No | Context obstacles |
| 2 | `Res` | Number | No | Mesh resolution [m] |
| 3 | `d1` | Number | No | Top layer depth [m] |
| 4 | `d2` | Number | No | Deep layer depth [m] |
| 5 | `HExp` | Number | No | Exposure tracing height [m] |
| 6 | `SVFN` | Integer | No | SVF sample count |
| 7 | `RhoSur` | Number | No | Surround shortwave reflectance |
| 8 | `EpsSur` | Number | No | Surround longwave emissivity |

---

## 12. Physical Consistency Between Temperature and Humidity

When overriding air temperature without adjusting relative humidity, the physical consistency between temperature ($T_{air}$), relative humidity ($RH$), and actual vapor pressure ($e_a$) can be violated because:

$$e_a = e_s(T_{air}) \cdot \frac{RH}{100}$$

If $T_{air}$ changes but $RH$ stays fixed, $e_a$ changes non-physically. To maintain consistency, provide a custom `RH` input (port 8 on Soil Thermal Settings) whenever `AirTemp` is overridden:

$$RH^{new} = \frac{e_s(T_{air}^{original})}{e_s(T_{air}^{override})} \cdot RH^{original} \cdot 100 \quad \text{(to preserve } e_a \text{)}$$

The `RH` input supports the same multi-mode structure as `AirTemp`: single value, per-point constant, time series, or per-point time series.

---

## 13. Numerical Implementation Details

### 13.1 Sub-stepping and Iteration

| Parameter | Default | Range | Description |
|-----------|---------|-------|-------------|
| SubStepsPerHour | 10 | [1, 60] | Sub-steps within each hour |
| MaxIterations | 20 | -- | Max surface energy balance iterations per sub-step |
| EnergyBalanceTolerance | 0.1 | -- | Convergence tolerance [W/m$^2$] |
| PM MaxIterations | 5 | -- | Max Penman-Monteith G-LE coupling iterations |
| PM Convergence | 1.0 | -- | PM coupling tolerance [W/m$^2$] |
| T1 Convergence | 0.001 | -- | Temperature convergence [K] |

### 13.2 State Variable Initialization

$$T_1^{init} = T_{air}^{first} \; \text{or} \; T_{user}^{surf}, \qquad T_2^{init} = T_{mean}^{annual} \; \text{or} \; T_{air}^{first}$$

---

## 14. References

1. **Deardorff, J.W.** (1978). Efficient prediction of ground surface temperature and moisture, with inclusion of a layer of vegetation. *J. Geophys. Res.*, 83(C4), 1889-1903.

2. **Noilhan, J. & Planton, S.** (1989). A simple parameterization of land surface processes for meteorological models. *Mon. Wea. Rev.*, 117, 536-549.

3. **Allen, R.G., Pereira, L.S., Raes, D. & Smith, M.** (1998). *Crop Evapotranspiration: Guidelines for Computing Crop Water Requirements*. FAO Irrigation and Drainage Paper 56, Rome, Italy.

4. **Louis, J.F.** (1979). A parametric model of vertical eddy fluxes in the atmosphere. *Bound.-Layer Meteor.*, 17, 187-202.

5. **Kondo, J. & Saigusa, N.** (1994). Modelling the evaporation from bare soil with a formula for vaporization in the soil pores. *J. Meteorol. Soc. Jpn.*, 72(3), 413-420.

6. **Tetens, O.** (1930). Uber einige meteorologische Begriffe. *Z. Geophys.*, 6, 297-309.

7. **Reda, I. & Andreas, A.** (2004). Solar Position Algorithm for Solar Radiation Applications. *Solar Energy*, 76(5), 577-589. https://doi.org/10.1016/j.solener.2003.12.003

---

