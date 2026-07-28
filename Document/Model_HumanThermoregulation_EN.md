# Human Thermoregulation Module

This module solves the steady-state human thermal balance using the Fiala multi-segment physiology model (Fiala 1998/2001/2012) and outputs the **Physiological Equivalent Temperature (EqT)** and **Dynamic Thermal Sensation (DTS)**. The human body is discretized into 12 segments with 5 tissue layers each. The bioheat equation is solved iteratively while coupling active thermoregulatory mechanisms including shivering, skin blood flow, and sweating. The equivalent temperature is obtained by matching the internal stress index S between the actual activity level and a reference environment through binary search, enabling comparison across different metabolic rates.

**Current Version Highlights:**
1. **Fiala 12-segment × 5-layer physiology model**: Head, neck, shoulders, arms, hands, thorax, abdomen, legs, feet, face, forehead, and pelvis; each layer carries tissue properties for brain/lung, bone, muscle, viscera, fat, inner skin, and outer skin.
2. **Active thermoregulatory system**: Non-linear tanh control equations describe shivering heat production, vasoconstriction, vasodilation, and sweating, with age attenuation and sex-based basal metabolism correction.
3. **Equivalent temperature EqT calculation**: Based on PET/UTCI reference conditions, binary search matches the internal stress index S so that any activity level can be normalized to the standard reference person.
4. **Optional clothing model**: Supports the UTCI adaptive clothing model (Havenith 2012) or user-specified clothing insulation.
5. **Parallel batch solving**: Supports parallel computation for multiple weather/human-state combinations.

---

## 1. Core Physical Model (HumanThermoregulationSimulator.cs)

### 1.1 Body Geometry and Segment Division

The human body is divided into 12 segments, each simplified as a sphere (head, face, forehead) or cylinder (all others). Each segment consists of 5 radial nodes from the core radius $R_c$ to the outer radius $R$; layer thicknesses are assigned by the `Frac` array. Segment surface area $A$, volume $V$, and convection coefficients (natural $A_{\text{nat}}$, forced $A_{\text{frc}}$, mixed $A_{\text{mix}}$) are taken from Fiala (1998) Appendix Table A.1.

12 segments and their geometric properties:

| Segment | Type | Outer Radius R [m] | Core Radius Rc [m] | Surface Area A [m²] | View Factor Vf |
|:---:|:---:|:---:|:---:|:---:|:---:|
| Head | Sphere | 0.086 | 0.040 | 0.092 | 0.95 |
| Neck | Cylinder | 0.062 | 0.035 | 0.070 | 0.90 |
| Shoulders | Cylinder | 0.075 | 0.045 | 0.100 | 0.75 |
| Arms | Cylinder | 0.044 | 0.022 | 0.280 | 0.85 |
| Hands | Cylinder | 0.025 | 0.012 | 0.078 | 0.88 |
| Thorax | Cylinder | 0.135 | 0.085 | 0.240 | 0.82 |
| Abdomen | Cylinder | 0.130 | 0.080 | 0.210 | 0.80 |
| Legs | Cylinder | 0.072 | 0.038 | 0.580 | 0.78 |
| Feet | Cylinder | 0.032 | 0.016 | 0.110 | 0.70 |
| Face | Sphere | 0.045 | 0.025 | 0.025 | 0.95 |
| Forehead | Sphere | 0.042 | 0.022 | 0.016 | 0.95 |
| Pelvis | Cylinder | 0.120 | 0.070 | 0.150 | 0.75 |

The 5 tissue layers are:

| Layer Index | Tissue Layer | Description |
|:---:|:---:|:---|
| 0 | Core | Brain/lung tissue (head/face/forehead) or equivalent core tissue |
| 1 | Muscle | Main heat source and shivering source |
| 2 | Bone | Low-metabolism supporting tissue |
| 3 | Inner skin | Skin blood flow layer regulated by vasodilation/vasoconstriction |
| 4 | Outer skin | Interface for convection, radiation, and evaporation with the environment |

### 1.2 Bioheat Equation

Each node satisfies the steady-state bioheat equation:

$$\nabla \cdot (k \nabla T) + q_m + \beta (T_{\text{bla}} - T) = 0$$

Where:
- $k$: tissue thermal conductivity [W/(m·K)]
- $q_m$: volumetric heat production rate [W/m³], including basal metabolism, activity heat, and shivering
- $\beta = \rho_{\text{blood}} c_{\text{blood}} w_{\text{bl}}$: blood-flow heat-capacity coefficient [W/(m³·K)]
- $T_{\text{bla}}$: arterial blood temperature [°C]
- $w_{\text{bl}}$: local blood perfusion rate [s⁻¹]

The model uses radial discretization and TDMA (Thomas algorithm) to solve the tridiagonal linear system, with the outermost node coupled to the environmental boundary condition.

### 1.3 Convective Heat Transfer Coefficient

The segment surface convective heat transfer coefficient is determined by a mixed-convection correlation:

$$h_c = \max\left( \sqrt[3]{h_{\text{nat}}^3 + h_{\text{frc}}^3}, \; h_{\text{mix}} \right)$$

Where:

$$h_{\text{nat}} = A_{\text{nat}} \cdot |T_{\text{sk}} - T_a|^{0.25}$$

$$h_{\text{frc}} = A_{\text{frc}} \cdot v_a^{0.5}$$

$$h_{\text{mix}} = A_{\text{mix}} \cdot |T_{\text{sk}} - T_a|^{0.25} \cdot v_a^{0.25}$$

$v_a$ is the effective air speed [m/s], combining ambient wind and walking speed:

$$v_a = \begin{cases}
\sqrt{v_{\text{wind}}^2 + v_{\text{walk}}^2}, & v_{\text{walk}} \leq 1.2 \, \text{m/s} \\
v_{\text{wind}} + 0.4 \, v_{\text{walk}}, & v_{\text{walk}} > 1.2 \, \text{m/s}
\end{cases}$$

### 1.4 Radiative Heat Transfer

A linearized radiative heat transfer coefficient is used:

$$h_r = 4 \sigma \left( 273.15 + \frac{T_{\text{sk}} + T_{\text{MRT}}}{2} \right)^3$$

Where $\sigma = 5.67 \times 10^{-8}$ W/(m²·K⁴). The effective radiative area equals the segment surface area multiplied by the posture factor $f_{\text{eff}}$:

| Posture | $f_{\text{eff}}$ |
|:---:|:---:|
| Standing | 0.80 |
| Sitting | 0.74 |

### 1.5 Clothing Insulation Model

Total clothing insulation $I_{\text{cl}}$ [clo] can be specified manually or determined automatically by the UTCI adaptive clothing model. The clothing surface area factor is:

$$f_{\text{cl}} = 1.0 + 0.31 \, I_{\text{cl}}$$

Clothing evaporation efficiency:

$$\eta_{\text{cl}} = \frac{1}{1 + h_c I_{\text{cl}} / i_m}$$

Where $i_m = 0.38$ is the clothing moisture permeability index, and $I_{\text{cl}}$ must be converted to [m²·K/W] ($1 \, \text{clo} = 0.155 \, \text{m²·K/W}$).

UTCI adaptive clothing model (Havenith 2012) piecewise-linear anchor points:

| Air Temperature [°C] | Clothing Insulation [clo] |
|:---:|:---:|
| ≤ -5 | 1.30 |
| 5 | 1.05 |
| 15 | 0.80 |
| 26 | 0.55 |
| 32 | 0.40 |
| 36 | 0.30 |
| ≥ 36 | 0.30 |

### 1.6 Evaporative Heat Loss

Maximum evaporative heat loss:

$$E_{\text{max}} = h_{\text{le}} \, \eta_{\text{cl}} \, (p_{\text{sk,sat}} - p_a) \, A$$

Where $h_{\text{le}} = h_c \cdot 0.0165$ is the evaporative heat transfer coefficient [W/(m²·Pa)], $p_a$ is ambient water vapor pressure [Pa], and $p_{\text{sk,sat}}$ is skin surface saturated vapor pressure [Pa].

Actual evaporative heat loss:

$$Q_{\text{evap}} = w_{\text{total}} \, E_{\text{max}}$$

Total skin wettedness combines baseline insensible perspiration and sweating:

$$w_{\text{total}} = w_{\text{insensible}} + (1 - w_{\text{insensible}}) \, w_{\text{sw}}$$

Where $w_{\text{insensible}}$ defaults to 0.06 (Gagge 1971), and $w_{\text{sw}}$ is computed from sweat rate and $E_{\text{max}}$.

### 1.7 Active Thermoregulatory System

The model uses mean skin temperature $T_{\text{sk,m}}$ and hypothalamus temperature $T_{\text{hy}}$ as feedback signals to regulate four physiological responses.

Control equations (Fiala 1998/2001):

**Shivering heat production [W]**:

$$Sh = \max\left( 0, \; 10[\tanh(0.51 \Delta T_{\text{sk}} + 4.19) - 1] \Delta T_{\text{sk}} - 27.5 \Delta T_{\text{hy}} - 28.5 \right)$$

**Vasoconstriction [-]**:

$$Cs = \max\left( 0, \; 35[\tanh(0.29 \Delta T_{\text{sk}} + 1.11) - 1] \Delta T_{\text{sk}} - 7.7 \Delta T_{\text{hy}} \right)$$

**Vasodilation [W/K]**:

$$Dl = \max\left( 0, \; 16[\tanh(1.92 \Delta T_{\text{sk}} - 2.53) + 1] \Delta T_{\text{sk}} + 30[\tanh(3.51 \Delta T_{\text{hy}} - 1.48) + 1] \Delta T_{\text{hy}} \right)$$

**Sweat rate [g/min]**:

$$Sw = \max\left( 0, \; \min\left( 30, \; [0.65 \tanh(0.82 \Delta T_{\text{sk}} - 0.47) + 1.15] \Delta T_{\text{sk}} + [5.6 \tanh(3.14 \Delta T_{\text{hy}} - 1.83) + 6.4] \Delta T_{\text{hy}} \right) \right)$$

Where:
- $\Delta T_{\text{sk}} = T_{\text{sk,m}} - T_{\text{sk,0}}$, $T_{\text{sk,0}} = 34.4$ °C
- $\Delta T_{\text{hy}} = T_{\text{hy}} - T_{\text{hy,0}}$, $T_{\text{hy,0}} = 37.0$ °C

Skin blood flow correction (Fiala 1998 Eq. 4.8):

$$\beta_{\text{skin}} = \beta_{\text{skin,0}} \cdot \frac{1 + D_{\text{dl}} Dl \, e^{-Dl/50}}{1 + D_{\text{cs}} Cs} \cdot 2^{(T_{\text{skin}} - 34.4)/10}$$

Where $D_{\text{dl}}$ and $D_{\text{cs}}$ are segment-specific distribution coefficients.

Age correction: when age > 65 years, vasoconstriction, vasodilation, and sweating responses are multiplied by the `AgeAttenuation` coefficient (default 0.75).

Sex correction: female basal metabolism is multiplied by `SexMetFactor` (default 0.90, ISO 8996 Annex B).

### 1.8 Respiratory Heat Loss

Following Fiala (1998) §3.4.5:

$$C_{\text{res}} = 0.0014 \, M \, (34 - T_a) \quad [\text{W/m²}]$$

$$E_{\text{res}} = 0.0023 \, M \, (44 - p_{a,\text{mmHg}}) \quad [\text{W/m²}]$$

Where $p_{a,\text{mmHg}} = p_{a,\text{hPa}} \times 0.75006$. Total respiratory heat loss $Q_{\text{res}} = (C_{\text{res}} + E_{\text{res}}) A_d$ is subtracted from activity heat production.

### 1.9 Metabolic Heat Production

Metabolic rate $M$ [W/m²] can be entered manually or computed automatically from walking speed (ISO 8996):

$$M = 58 + 70 \, v_{\text{walk}}$$

Where $v_{\text{walk}}$ is walking speed [m/s]. For example, $v=0$ gives $M=58$ W/m² (rest), and $v=1.1$ gives $M=135$ W/m² (walking 4 km/h).

Mechanical efficiency:

$$\eta = \begin{cases}
0, & M \leq 1.6 \, \text{met} \\
0.39 \, \text{met} - 0.60, & M > 1.6 \, \text{met}
\end{cases}$$

Effective activity heat production:

$$H_{\text{wk}} = (M - 0.8 \times 58.2) \, A_d \, (1 - \eta) - Q_{\text{res}}$$

This heat is distributed uniformly by volume to all tissue layers.

### 1.10 Equivalent Temperature EqT Solver

Equivalent temperature is defined as the air temperature in the reference environment ($M_{\text{ref}}$, $v_{\text{ref}}$, $RH_{\text{ref}}$, $I_{\text{cl,ref}}$) that would produce the same internal stress index $S$ in the reference person as in the actual environment.

Default reference person (UTCI): $M_{\text{ref}} = 135$ W/m², $v_{\text{ref}} = 0.5$ m/s, $RH_{\text{ref}} = 50\%$, $I_{\text{cl,ref}} = 0.5$ clo.

Algorithm:
1. Solve CoreSolve for the actual environment to obtain $T_{\text{sk}}$, $T_{\text{core}}$, $w_{\text{sk}}$, and compute $S_{\text{actual}}$.
2. Build the reference person with fixed clothing $I_{\text{cl,ref}}$.
3. Binary-search the reference air temperature $T_r$ within [-50, 50] °C, solving CoreSolve and computing $S_{\text{ref}}$ each iteration.
4. The $T_r$ that minimizes $|S_{\text{ref}} - S_{\text{actual}}|$ is the EqT.

### 1.11 Dynamic Thermal Sensation DTS

DTS maps the internal stress index S to the [-3, +3] interval via tanh:

$$DTS = 3 \tanh(S)$$

The internal stress index S is composed as follows (Fiala 1998/2003):

$$S = f_{\text{sk}} + \phi + \tau_{\text{neg}} + \tau_{\text{pos}} + f_{\text{core}} + f_{\text{ex}} + f_{\text{wet}}$$

Components:
- Skin temperature contribution: $f_{\text{sk}} = b_1 (T_{\text{sk}} - T_{\text{sk,0}})$, where $b_1 = 1.026$ (warm) or $0.298$ (cold)
- Core temperature interaction term $\phi$: non-zero only when $\Delta T_{\text{hy}} > 0$ and $\Delta T_{\text{sk}} < 5$ K
- Negative dynamic effect: $\tau_{\text{neg}} = 0.114 \, dT_{\text{sk}}/dt \cdot \phi$ (when $dT_{\text{sk}}/dt < 0$)
- Direct core contribution: $f_{\text{core}} = 0.3 \, \Delta T_{\text{hy}}$
- Exercise tolerance offset: $f_{\text{ex}} = -0.035$ (when $M > 100$ W/m²)
- Skin wettedness contribution: $f_{\text{wet}} = 1.5 \max(0, w_{\text{sk}} - 0.06)$

Setpoints corrected for metabolic rate:

$$T_{\text{sk,0}} = 34.4 - 0.028 (M - 58.2)$$

$$T_{\text{hy,0}} = 37.0 + 0.0015 (M - 58.2)$$

---

## 2. Human Physiology Component (HumanPhysiology.cs)

### 2.1 Description

Configures human physiological and activity parameters and outputs a structured `UtciHumanSet` for the simulator. Supports manual metabolic rate input or automatic calculation from walking speed, and manual clothing insulation or automatic adjustment by the UTCI clothing model.

### 2.2 Input Parameters

| Index | Parameter | ID | Type | Default | Description |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | AutoMet | AutoMet | Boolean | true | Auto-calculate metabolic rate from WalkSpeed |
| 1 | MetRate | M | Number | 80.0 | Metabolic heat production [W/m²]; ignored when AutoMet=true |
| 2 | WalkSpeed | Vw | Number | 1.1 | Walking/running speed [m/s]; used for AutoMet and effective air speed |
| 3 | Posture | Pos | Integer | 0 | Posture: 0=standing, 1=sitting |
| 4 | AutoClo | AutoClo | Boolean | false | Auto-adjust clothing insulation by air temperature (executed in HumanThermalEnvironment) |
| 5 | CloValue | Icl | Number | 0.5 | Clothing insulation [clo]; used when AutoClo=false |
| 6 | BodyWeight | W | Number | 73.5 | Body weight [kg] |
| 7 | BodyHeight | H | Number | 1.75 | Body height [m] |
| 8 | Age | Age | Number | 35.0 | Age [years] |
| 9 | Sex | Sex | Integer | 0 | Sex: 0=male, 1=female |

**Constraints:**
- WalkSpeed: 0–8 m/s; warning and clamp if exceeded
- BodyWeight: ≥30 kg
- BodyHeight: ≥1.0 m
- Posture: 0 or 1; reset to 0 otherwise
- Sex: 0 or 1; reset to 0 otherwise
- When AutoMet=false, a warning is issued if MetRate and WalkSpeed are inconsistent (difference > 30 W/m²)

### 2.3 Output Parameters

| Index | Parameter | Description |
|:---:|:---:|:---|
| 0 | HumanPhysiology | Structured human/activity data (UtciHumanSet) |

---

## 3. Human Thermal Environment Component (HumanThermalEnvironment.cs)

### 3.1 Description

Configures environmental parameters and outputs a structured `UtciWeatherSet`. Includes the Goff-Gratch saturated vapor pressure formula and supports automatic actual vapor pressure calculation from relative humidity.

### 3.2 Input Parameters

| Index | Parameter | ID | Type | Default | Description |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | AirTemp | Ta | Number | 26.0 | Dry-bulb air temperature [°C] |
| 1 | MRT | MRT | Number | 26.0 | Mean radiant temperature [°C]; defaults to Ta if not provided |
| 2 | RH | RH | Number | 50.0 | Relative humidity [%] |
| 3 | AutoVP | AutoVP | Boolean | true | Auto-calculate actual vapor pressure from RH |
| 4 | VP | VP | Number | 16.8 | Actual water vapor pressure [hPa]; used when AutoVP=false |
| 5 | WindSpeed | Va | Number | 1.0 | Wind speed at 1.5 m pedestrian height [m/s] |
| 6 | Pressure | P | Number | 1013.25 | Atmospheric pressure [hPa] |

**Constraints:**
- RH: 0–100%; clamped if exceeded
- WindSpeed: 0.01–17.0 m/s; clamped if exceeded
- Pressure: fallback to 1013.25 hPa if ≤0

### 3.3 Goff-Gratch Saturated Vapor Pressure

Actual vapor pressure:

$$p_a = p_{\text{sat}}(T_a) \cdot \frac{RH}{100}$$

Saturated vapor pressure $p_{\text{sat}}$ [hPa] is computed by the Goff-Gratch formula:

$$\log_{10} p_{\text{sat}} = -7.90298 \left(\frac{T_{\text{st}}}{T} - 1\right) + 5.02808 \log_{10}\left(\frac{T_{\text{st}}}{T}\right) - 1.3816 \times 10^{-7} \left(10^{11.344(1 - T/T_{\text{st}})} - 1\right) + 8.1328 \times 10^{-3} \left(10^{-3.49149(T_{\text{st}}/T - 1)} - 1\right) + \log_{10}(1013.246)$$

Where $T_{\text{st}} = 373.16$ K and $T = 273.15 + T_a$ [K].

### 3.4 Output Parameters

| Index | Parameter | Description |
|:---:|:---:|:---|
| 0 | Human Thermal Environment | Structured weather/environmental data (UtciWeatherSet) |

---

## 4. Simulation Base Settings Component (HumanSimulationBaseSettings.cs)

### 4.1 Description

Exposes internal solver parameters for advanced users to override defaults. If not connected, the simulator uses PET defaults ($M=80$ W/m², $v=0.1$ m/s, $RH=50\%$, $I_{\text{cl}}=0.5$ clo).

### 4.2 Input Parameters

| Index | Parameter | ID | Unit | Default | Description |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | RefMetRate | Mref | W/m² | 135.0 | Reference metabolic rate; UTCI=135, PET=80, PMV=70 |
| 1 | RefWindSpeed | Vref | m/s | 0.5 | Reference wind speed; UTCI=0.5, PET=0.1 |
| 2 | RefRH | RHref | % | 50.0 | Reference relative humidity |
| 3 | RefIcl | Iclref | clo | 0.5 | Reference clothing insulation |
| 4 | MaxIter | MaxIter | - | 200 | Maximum iterations per CoreSolve |
| 5 | ResidTol | Tol | K | 0.005 | Blood-pool temperature convergence tolerance |
| 6 | BlpRelax | Alpha | - | 0.7 | Blood-pool relaxation factor (0.1–1.0) |
| 7 | EqTSearchIter | EqTN | - | 20 | Binary-search iterations for EqT |
| 8 | InsensibleDiff | wDiff | - | 0.06 | Baseline skin wetness from insensible perspiration |
| 9 | AgeAttenuation | AgeAtt | - | 0.75 | Thermoregulatory response attenuation factor for age >65 |
| 10 | SexMetFactor | SexMet | - | 0.90 | Female basal metabolism as fraction of male |

**Constraints:** Warnings and clamping to reasonable ranges are applied when parameters exceed typical ranges.

### 4.3 Output Parameters

| Index | Parameter | Description |
|:---:|:---:|:---|
| 0 | SimBaseSet | Simulation base settings (SimulationSettings) |

---

## 5. Human Thermoregulation Simulator Component (HumanThermoregulationSimulator.cs)

### 5.1 Description

Core solver component. Receives structured environmental data, human data, and optional base settings, then solves EqT, DTS, and physiological responses for each state in parallel.

### 5.2 Input Parameters

| Index | Parameter | ID | Type | Description |
|:---:|:---:|:---:|:---:|:---|
| 0 | Human Thermal Environment | HTE | Generic List | Structured environmental data from Human Thermal Environment |
| 1 | Human Physiology | HP | Generic List | Structured human data from Human Physiology; single item is broadcast to all environment items |
| 2 | SimBaseSet | SBS | Generic | Base settings from Simulation Base Settings (optional) |
| 3 | Run | Run | Boolean | Set to true to execute simulation |

**Batch rules:**
- Environment count $n$ and human count must be 1:1, or human count must be 1 (auto-broadcast)
- A remark is issued when $n > 1000$

### 5.3 Output Parameters

| Index | Parameter | ID | Description |
|:---:|:---:|:---:|:---|
| 0 | EquivTemp | EqT | Physiological equivalent temperature [°C] |
| 1 | DTS | DTS | Dynamic thermal sensation [-3 to +3] |
| 2 | MeanSkinTemp | Tsk | Area-weighted mean skin temperature [°C] |
| 3 | CoreTemp | Tco | Hypothalamus (core) temperature [°C] |
| 4 | SweatRate | Sw | Total sweat rate [g/min] |
| 5 | Shivering | Sh | Total shivering heat production [W] |
| 6 | Iterations | Iter | Number of iterations to convergence |
| 7 | Converged | Conv | Whether the simulation converged |

---

## 6. Data Structures and Grasshopper Wrappers

The module defines the following core data structures in the `ThermalComfort.Core` namespace:

| Data Structure | Description |
|:---:|:---|
| `UtciWeatherSet` | Environmental parameter container (Ta, RH, Va, MRT, VP, P) |
| `UtciHumanSet` | Human/activity parameter container (M, Vw, Posture, Icl, W, H, Age, Sex, etc.) |
| `SimulationSettings` | Solver and reference environment settings |
| `UtciResultSet` | Complete result container (EqT, DTS, temperatures, regulatory responses, heat balance components, convergence info) |

Corresponding Grasshopper Goo wrapper classes:

| Goo Class | Type Name | Description |
|:---:|:---:|:---|
| `GH_UtciWeatherSet` | UTCI Weather Set | Wrapper for environmental data on Grasshopper wires |
| `GH_UtciHumanSet` | UTCI Human Set | Wrapper for human data on Grasshopper wires |
| `GH_SimulationSettings` | Simulation Settings | Wrapper for base settings on Grasshopper wires |
| `GH_UtciResultSet` | EqT Result Set | Wrapper for results on Grasshopper wires |

---

## 7. Usage Notes

1. **MRT input**: It is recommended to use the MRT module of this plugin (SolarCal or RayMan) to compute mean radiant temperature for accurate outdoor radiative correction.
2. **Wind height**: WindSpeed should be at 1.5 m pedestrian height; if a meteorological station 10 m wind speed is provided, convert it using a logarithmic profile first.
3. **Clothing model**: When AutoClo=true, HumanThermalEnvironment automatically applies the UTCI clothing model based on air temperature; when AutoClo=false, CloValue is used.
4. **Reference environment**: EqT strongly depends on RefMetRate. When comparing comfort across different activity levels, keep RefMetRate consistent (e.g., UTCI default 135 W/m²).
5. **Convergence**: Extreme environments (desert, high humidity, polar) may require increasing MaxIter or relaxing ResidTol; failed convergence outputs NaN.
6. **Age correction**: AgeAttenuation is applied only when Age > 65 years to model reduced thermoregulatory response in seniors.
7. **Altitude correction**: Pressure input is used for atmospheric pressure correction affecting convection and evaporation; for high-altitude scenarios, provide the actual atmospheric pressure.

---

## 8. References

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
