# Photovoltaic (PV) Performance Simulation Module

This module contains a complete simulation chain from solar radiation calculation to photovoltaic system power generation estimation, covering solar geometry calculation, sky diffuse model, bifacial PV model, module temperature model, and inverter/MPPT model, performing hourly performance simulation based on EPW meteorological data.

---

## 1. Solar Geometry Calculation (SolarGeometry.cs)

### 1.1 Incidence Angle Modifier (IAM)

Based on the ASHRAE simplified model:

$$\text{IAM}(\theta) = 1 - b_0 \left( \frac{1}{\cos\theta} - 1 \right)$$

Where:
- $\theta$: Incidence angle [°]
- $b_0$: IAM coefficient (standard PV glass default 0.05, anti-reflective coating 0.03)

When $\cos\theta \leq 0.01$, $\text{IAM} = 0$.

### 1.2 Isotropic Diffuse Model

Based on Liu & Jordan (1963):

$$I_{d,\text{tilted}} = I_{\text{DH}} \cdot \frac{1 + \cos\beta}{2}$$

Where:
- $I_{\text{DH}}$: Horizontal diffuse irradiance [W/m²]
- $\beta$: Surface tilt angle [rad]

### 1.3 Ground-Reflected Radiation

$$I_{\text{ground}} = I_{\text{GH}} \cdot \rho_g \cdot \frac{1 - \cos\beta}{2}$$

Where:
- $I_{\text{GH}}$: Horizontal global irradiance [W/m²]
- $\rho_g$: Ground albedo

### 1.4 Tilted Surface Direct Radiation

$$I_{\text{direct}} = I_{\text{DN}} \cdot \cos\theta \cdot \text{IAM}(\theta)$$

Where:
- $I_{\text{DN}}$: Direct normal irradiance [W/m²]
- $\theta$: Incidence angle [rad]

When $\cos\theta \leq 0$ (sun behind the surface), $I_{\text{direct}} = 0$.

### 1.5 Solar Vector Calculation

From geographic azimuth (0°=North, clockwise) and zenith angle, compute the solar direction unit vector (coordinate system: X=East, Y=North, Z=Up):

$$\vec{s} = (\sin A \cdot \sin Z,\; \cos A \cdot \sin Z,\; \cos Z)$$

Where $A$ is the geographic azimuth and $Z$ is the zenith angle.

### 1.6 Hemisphere / Full-Sphere Direction Sampling

**Fibonacci Upper-Hemisphere Sampling** (for SVF calculation):

$$z_i = \sqrt{1 - \frac{i}{N}}, \quad r_i = \sqrt{1 - z_i^2}, \quad \theta_i = \phi \cdot i$$

**Fibonacci Full-Sphere Sampling** (for MRT 4π view factors):

$$z_i = 1 - \frac{2i + 1}{N}, \quad r_i = \sqrt{1 - z_i^2}, \quad \theta_i = \phi \cdot i$$

Where $\phi = \pi(3 - \sqrt{5})$ is the golden angle.

---

## 2. Decomposed View Factors for Diffuse Radiation (HumanExposureModel.cs)

### 2.1 Physical Basis

**ENHANCED (2026-06-16):** To accurately simulate the effects of vegetation and translucent sunshades on diffuse radiation, the traditional single SVF (Sky View Factor) approach is extended to four mutually exclusive decomposed view factors:

| View Factor | Symbol | Description |
|:---:|:---:|:---|
| Sky View Factor | $F_{	ext{SVF}}$ | Visible sky directions (no obstruction) |
| Opaque Obstacle View Factor | $F_{	ext{OVF,opaque}}$ | Directions blocked by opaque objects only |
| Tree View Factor | $F_{	ext{TVF}}$ | Directions blocked by tree detail meshes |
| Translucent View Factor | $F_{	ext{TRVF}}$ | Directions blocked by translucent shade meshes |

**Conservation:**

$$F_{	ext{SVF}} + F_{	ext{OVF,opaque}} + F_{	ext{TVF}} + F_{	ext{TRVF}} = 1.0 \quad 	ext{(upper hemisphere)}$$

**Overlap Resolution Priority:** When a ray direction intersects multiple obstacle types simultaneously, the priority is: **Opaque > TreeDetail > TranslucentShade**. This ensures directions blocked by both buildings and trees are assigned to the opaque category (the building occludes the tree from the analysis point's perspective).

### 2.2 Effective Diffuse Irradiance

The effective diffuse irradiance on a tilted surface accounts for partial transmission through tree canopies and translucent materials:

$$I_{	ext{DH,eff}} = I_{	ext{DH,base}} \cdot \left( F_{	ext{SVF}} + F_{	ext{TVF}} \cdot e^{-k_c \cdot 	ext{LAD} \cdot l} + F_{	ext{TRVF}} \cdot 	au ight)$$

Where:
- $I_{	ext{DH,base}}$: Full-hemisphere diffuse irradiance (isotropic or Perez) [W/m²]
- $k_c$: Extinction coefficient of tree canopy [-]
- $	ext{LAD}$: Leaf area density [m²/m³]
- $l$: Characteristic canopy thickness [m], computed as the Z-axis extent of the combined bounding box of all TreeCanopyMeshes
- $	au$: Shortwave transmittance of translucent shade material [-]
- $e^{-k_c \cdot 	ext{LAD} \cdot l}$: Beer-Lambert canopy transmission factor

**Tree Canopy Characteristic Thickness:**

$$l = \max(0, \; Z_{\max} - Z_{\min})$$

Where $Z_{\max}$ and $Z_{\min}$ are the maximum and minimum Z-coordinates of the combined bounding box of all simplified tree canopy envelope meshes (TreeCanopyMeshes).

### 2.3 SVF-Obstacle Factor (SVF-OVF) Definition

The decomposed view factors replace the previous single SVF and OVF (which combined all obstacle types). The **SVF remains unchanged** (still representing the visible sky fraction), while the **OVF is now OPAQUE-ONLY** (previously included opaque objects, tree detail meshes, and translucent shade meshes combined).

**Before (Legacy):**
- OVF = opaque + tree + translucent (all obstacles combined)

**After (Enhanced):**
- OVF_opaque = opaque objects only
- TVF = tree detail meshes
- TRVF = translucent shade meshes

---

## 3. Perez Sky Diffuse Model (PerezSkyModel.cs)

Based on the Perez, Ineichen et al. (1987, 1990) sky anisotropic diffuse model.

### 2.1 Main Equation

$$I_{d,\text{tilted}} = I_{\text{DH}} \cdot \left[ (1 - F_1) \frac{1 + \cos\beta}{2} + F_1 \frac{a}{b} + F_2 \sin\beta \right]$$

Where:
- $F_1$: Circumsolar brightening coefficient
- $F_2$: Horizon brightening coefficient
- $a = \max(0, \cos\theta)$
- $b = \max(\cos 85°, \cos Z)$, $Z$ is the zenith angle

### 2.2 Sky Brightness Parameters

Sky clearness parameter $\varepsilon$:

$$\varepsilon = \frac{(I_{\text{DH}} + I_{\text{DN}}) / I_{\text{DH}} + 5.535 \times 10^{-6} \cdot Z^3}{1 + 5.535 \times 10^{-6} \cdot Z^3}$$

Atmospheric clearness parameter $\Delta$:

$$\Delta = \frac{I_{\text{DH}} \cdot m}{1367}, \quad m = \frac{1}{\cos Z}$$

### 2.3 Coefficients $F_1$ and $F_2$

$$F_1 = \max(0,\; f_{11} + f_{12} \cdot \Delta + f_{13} \cdot Z_{\text{rad}}) \cdot c_{\text{cs}}$$

$$F_2 = \max(0,\; f_{21} + f_{22} \cdot \Delta + f_{23} \cdot Z_{\text{rad}}) \cdot c_{\text{hb}}$$

Where $c_{\text{cs}}$ and $c_{\text{hb}}$ are the circumsolar and horizon coefficient scaling factors, respectively.

**8-Group Coefficient Table (by $\varepsilon$ bins):**

| Bin | $\varepsilon$ Range | Sky Condition |
|:---:|:---:|:---|
| 1 | < 1.065 | Overcast |
| 2 | 1.065 – 1.230 | Overcast / Partly cloudy |
| 3 | 1.230 – 1.500 | Partly cloudy |
| 4 | 1.500 – 1.950 | Partly cloudy |
| 5 | 1.950 – 2.800 | Clear / Partly cloudy |
| 6 | 2.800 – 4.500 | Clear |
| 7 | 4.500 – 6.200 | Very clear |
| 8 | $\geq$ 6.200 | Extremely clear |

### 2.4 Fallback Mechanism

When $I_{\text{DH}} \leq 0$ or $Z \geq 90°$ or $I_{\text{DH}} / I_{\text{GH}} <$ threshold (default 0.01), fall back to the isotropic model.

When $F_{\text{SVF}} < 0.3$ (heavy occlusion), Perez results are scaled by $F_{\text{SVF}}$.

---

## 4. Bifacial PV Model (BifacialModel.cs)

### 7.1 Rear-Side Irradiance

$$I_{\text{rear}} = \big( I_{\text{GH}} \cdot \rho_g \cdot F_{r \to g} \cdot S_f + I_{\text{DH}} \cdot F_{r \to \text{sky}} \cdot F_{\text{SVF,rear}} \big) \cdot f_{\text{rg}}$$

Where:
- $F_{r \to g} = (1 + \cos\beta)/2$: Rear-to-ground view factor
- $F_{r \to \text{sky}} = (1 - \cos\beta)/2$: Rear-to-sky view factor
- $S_f$: Row-to-row shading correction factor
- $f_{\text{rg}}$: Rear-side gain empirical correction factor

**Row-to-Row Shading Correction Factor:**

$$S_f = \frac{1}{1 + 0.5 \cdot (H / D) \cdot \sin\beta}$$

Where $H$ is the module installation height and $D$ is the row spacing. The result is constrained within [0.1, 1.0].

### 7.2 Bifacial Gain Power

$$P_{\text{rear}} = I_{\text{rear}} \cdot A_{\text{gross}} \cdot \phi_{\text{active}} \cdot \eta_{\text{front}} \cdot \phi_{\text{bifacial}}$$

Where:
- $A_{\text{gross}}$: Module gross area [m²]
- $\phi_{\text{active}}$: Active area ratio (default 0.9)
- $\eta_{\text{front}}$: Temperature-corrected front-side efficiency
- $\phi_{\text{bifacial}}$: Bifaciality factor (default 0.7)

### 5.3 Bifacial Gain Ratio

$$\text{BG} = \frac{I_{\text{rear}}}{I_{\text{front}}} \cdot \phi_{\text{bifacial}}$$

---

## 5. PV Module Temperature Model (PVTemperatureModel.cs)

### 7.1 Faiman Model (Preferred)

$$T_{\text{cell}} = T_a + \frac{I_{\text{POA}}}{U_0 + U_1 \cdot v_w}$$

Where:
- $T_a$: Ambient temperature [°C]
- $I_{\text{POA}}$: Plane-of-array irradiance [W/m²]
- $v_w$: Wind speed [m/s]
- $U_0, U_1$: Heat loss coefficients related to mounting method

**Heat Loss Coefficients for Different Mounting Methods:**

| Mounting Method | $U_0$ [W/(m²·K)] | $U_1$ [W/(m²·K)/(m/s)] |
|:---:|:---:|:---:|
| FreeStanding | 25.0 | 6.84 |
| RoofMounted | 20.0 | 5.70 |
| BuildingIntegrated (BIPV) | 15.0 | 4.50 |
| Concentrator | 30.0 | 8.00 |

### 7.2 NOCT Model (Fallback when no wind speed)

$$T_{\text{cell}} = T_a + \frac{(\text{NOCT} - 20)}{800} \cdot I_{\text{POA}} \cdot R_{\text{thermal}}$$

Where the thermal ratio $R_{\text{thermal}}$ accounts for efficiency correction:

$$R_{\text{thermal}} = \frac{1 - \eta}{1 - \eta_{\text{ref}}}$$

$\eta_{\text{ref}}$ is the reference efficiency at NOCT test conditions (default 0.10), and the result is constrained within [0.5, 2.0].

### 5.3 Sandia Temperature Model

$$T_{\text{module}} = T_a + I_{\text{POA}} \cdot e^{a + b \cdot v_w}$$

$$T_{\text{cell}} = T_{\text{module}} + \frac{I_{\text{POA}}}{1000} \cdot \Delta T$$

Default parameters: $a = -3.47$, $b = -0.0594$, $\Delta T = 3$ °C.

### 5.4 Temperature-Corrected Efficiency

$$\eta(T) = \eta_{\text{STC}} \cdot \big[ 1 + \gamma \cdot (T_{\text{cell}} - T_{\text{STC}}) \big]$$

Where $\gamma$ is the maximum power temperature coefficient [%/°C or 1/°C].

---

## 6. Inverter and MPPT Model (InverterMPPTModel.cs)

### 7.1 PVWatts Inverter Model

Based on the PVWatts Version 5 model by Dobos (2014).

**Partial Load Efficiency Curve:**

$$\zeta = \frac{P_{\text{dc}}}{P_{\text{dc0}}}, \quad P_{\text{dc0}} = \frac{P_{\text{ac,rated}}}{\eta_{\text{nom}}}$$

$$\text{PLR} = \begin{cases} -0.0162\zeta - 0.0059/\zeta + 0.9858 & 0.01 \leq \zeta \leq 1.2 \\ \zeta/0.01 \cdot \text{PLR}|_{\zeta=0.01} & \zeta < 0.01 \\ \text{PLR}|_{\zeta=1.2} \cdot \big[1 - 0.05(\zeta - 1.2)\big] & \zeta > 1.2 \end{cases}$$

$$\eta_{\text{op}} = \eta_{\text{nom}} \cdot \text{PLR}$$

$$P_{\text{ac}} = P_{\text{dc}} \cdot \eta_{\text{op}}$$

**Clipping:** If $P_{\text{ac}} > P_{\text{ac,rated}} \times \text{ClipRatio}$, output is limited and clipping loss is recorded.

**Nighttime Tare Loss:** If output $<$ TareLoss, output is set to 0.

### 7.2 String Voltage Calculation

$$V_{\text{string}} = N_s \cdot V_{\text{mp}} \cdot \big[1 + \gamma_V \cdot (T_{\text{cell}} - 25)\big]$$

$$V_{\text{oc,string}} = N_s \cdot V_{\text{oc}} \cdot \big[1 + \gamma_{V_{\text{oc}}} \cdot (T_{\text{cell}} - 25)\big]$$

MPPT window check: If $V_{\text{string}} < V_{\text{min}}$ or $V_{\text{string}} > V_{\text{max}}$, or $V_{\text{oc,string}} > V_{\text{max}} + 100$ V, the inverter shuts down.

---

## 7. Radiation Simulation Engine (NeosRadSim.cs)

### 7.1 Input Parameters

| Index | Parameter | Type | Description |
|:---:|:---:|:---:|:---|
| 0 | EPW File | Text | EPW weather file path |
| 1 | Obstacles | Brep List | Environmental obstacles (optional) |
| 2 | Measurement Surfaces | Brep List | Surfaces to be measured (PV panels or sensor surfaces) |
| 3 | Time Settings | Generic | Time settings (optional) |
| 4 | PV Settings | Generic | PV module settings (optional) |
| 5 | Temperature Settings | Generic | Temperature model settings (optional) |
| 6 | Sky Model Settings | Generic | Sky model settings (optional) |
| 7 | Raytracing Settings | Generic | Ray tracing settings (optional) |
| 8 | Inverter Settings | Generic | Inverter settings (optional) |
| 9 | Output Folder | Text | Result output folder (optional) |
| 10 | Run | Boolean | Execution switch |

### 7.2 Output Results (6 Category Files)

| File | Content |
|:---:|:---|
| GeometryResult.txt | Mesh data, face centers, normal vectors, areas, tilt angles, solar vectors |
| ViewResult.txt | Front/rear SVF, front/rear OVF (opaque-only), front/rear TVF, front/rear TRVF |
| SunRadiationResult.txt | Sunshine hours, hourly/cumulative radiation (front/rear) |
| PVInfoResult.txt | Cell temperature, effective irradiance, clipping loss, inverter loss |
| DCResult.txt | Hourly/cumulative DC power generation (front/rear) |
| ACResult.txt | Hourly/cumulative AC power generation (front/rear) |

### 7.3 Simulation Flow

1. Read EPW meteorological data → parse latitude, longitude, time zone, elevation
2. Surface meshing → use Geometry.Core to generate regular grids
3. Calculate decomposed view factors → Fibonacci hemisphere sampling + ray tracing
   - SVF (sky), OVF_opaque (opaque only), TVF (tree detail), TRVF (translucent shade)
4. If in bifacial mode → additionally calculate rear-side decomposed view factors
5. **Hourly loop:**
   - NREL-SPA solar position calculation
   - Shadow detection (direct solar ray tracing)
   - Tilted surface irradiance = direct (with IAM) + diffuse (Perez/isotropic with TVF/TRVF correction) + ground reflection
   - Module temperature (Faiman/NOCT/Sandia)
   - Temperature-corrected efficiency
   - DC power generation
   - If in bifacial mode → rear-side irradiance + bifacial gain
   - If in inverter mode → DC-AC conversion (with clipping and MPPT window check)
6. Save 6 categories of result files

---

## 7. Settings Component Parameter Summary

### 7.1 PV Settings (PVSettings.cs)

| Parameter | Unit | Default | Description |
|:---:|:---:|:---:|:---|
| Enable Bifacial | - | false | Enable bifacial PV calculation |
| Grid Resolution | m | 0.5 | Analysis grid size |
| Efficiency | - | 0.20 | STC module efficiency |
| Active Area Ratio | - | 0.9 | Active area ratio |
| Albedo | - | 0.2 | Ground albedo |
| Bifaciality Factor | - | 0.7 | Bifaciality factor |
| Row Spacing | m | -1 | Row spacing (-1=auto-infer) |
| Module Height | m | -1 | Module height (-1=auto-infer) |
| Rear Gain Factor | - | 1.0 | Rear-side gain empirical correction |
| IAM Coefficient | - | 0.05 | Incidence angle modifier coefficient $b_0$ |
| System Loss Factor | - | 0.14 | Total system loss rate (14%) |

### 7.2 Temperature Settings (PVTemperatureModelSettings.cs)

| Parameter | Unit | Default | Description |
|:---:|:---:|:---:|:---|
| Enable Temperature Model | - | true | Enable temperature correction |
| NOCT | °C | 45 | Nominal operating cell temperature |
| Temp Coefficient | %/°C | -0.4 | Power temperature coefficient |
| Temp Coeff Is Percent | - | true | Whether the coefficient is in percent form |
| Wind Speed Factor | - | 1.0 | Wind speed multiplier |
| Mounting Type | - | 0 | 0=free-standing / 1=roof-mounted / 2=BIPV / 3=no wind correction |
| NOCT Reference Efficiency | - | 0.10 | NOCT test reference efficiency |

### 7.3 Sky Model Settings (SkyModelSettings.cs)

| Parameter | Unit | Default | Description |
|:---:|:---:|:---:|:---|
| Use Perez Model | - | false | Use Perez anisotropic model |
| Horizon Coefficient | - | 1.0 | Horizon brightening component scaling |
| Circumsolar Coefficient | - | 1.0 | Circumsolar brightening component scaling |
| Diffuse Ratio Threshold | - | 0.01 | DHI/GHI minimum threshold |

### 7.4 Raytracing Settings (RaytracingSettings.cs)

| Parameter | Unit | Default | Description |
|:---:|:---:|:---:|:---|
| SVF Sample Count | - | 500 | Number of hemisphere sampling directions |
| SVF Ray Offset | m | 0.01 | SVF ray starting point offset |
| Shadow Ray Offset | m | 0.001 | Shadow ray starting point offset |
| Max Trace Distance | m | 10000 | Maximum tracing distance |

### 7.5 Inverter MPPT Settings (InverterMPPTSettings.cs)

| Parameter | Unit | Default | Description |
|:---:|:---:|:---:|:---|
| Enable Inverter Model | - | true | Enable DC-AC conversion |
| Rated AC Power | W | 10000 | Inverter rated AC power |
| Nominal Efficiency | - | 0.96 | Nominal efficiency |
| MPPT Min Voltage | V | 200 | Minimum MPPT voltage |
| MPPT Max Voltage | V | 800 | Maximum MPPT voltage |
| Clipping Ratio | - | 1.0 | AC clipping ratio |
| Night Tare Loss | W | 5 | Nighttime tare loss |
| Nominal Vmp | V | 35 | Module STC maximum power point voltage |
| Temp Coeff Voltage | 1/°C | -0.004 | Vmp temperature coefficient |
| Nominal Voc | V | 42 | Module STC open-circuit voltage |
| Temp Coeff Voc | 1/°C | -0.003 | Voc temperature coefficient |

### 7.6 Time Settings (TimeSettings.cs)

| Parameter | Unit | Default | Description |
|:---:|:---:|:---:|:---|
| Start Month | - | 1 | Start month |
| Start Day | - | 1 | Start day |
| Start Hour | - | 0 | Start hour (0–23) |
| End Month | - | 12 | End month |
| End Day | - | 31 | End day |
| End Hour | - | 23 | End hour (0–23) |

---

## 8. References

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
