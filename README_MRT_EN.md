# MRT (Mean Radiant Temperature) Module

This module calculates the Mean Radiant Temperature (MRT) for the human body in outdoor environments. It is based on backward ray-tracing building/vegetation occlusion analysis, supports both the SolarCal (ASHRAE 55) and RayMan calculation models, and employs full 4π spherical view factor decomposition to achieve physically correct longwave radiation computation.

---

## 1. MRT Physical Model (MRTmodel.cs)

### 1.1 SolarCal Model (ASHRAE 55)

Based on the SolarCal model from ASHRAE Standard 55, combining shortwave solar radiation increment with three-directional longwave radiation decomposition.

**Shortwave Radiation Increment:**

The solar radiation flux received by the human body consists of direct radiation, diffuse radiation, and ground-reflected radiation:

$$I_{\text{body}} = f_{\text{exp}} \cdot f_p(\gamma) \cdot I_{\text{DN}} + \frac{1}{2} f \cdot F_{\text{SVF}} \cdot I_{\text{DH}} + \frac{1}{2} f \cdot F_{\text{GVF}} \cdot \rho_g \cdot I_{\text{GH}}$$

Where:
- $f_{\text{exp}}$: Exposure factor (0–1), computed by backward ray tracing
- $f_p(\gamma)$: Solar projection coefficient (see equation below)
- $I_{\text{DN}}$: Direct normal irradiance [W/m²]
- $I_{\text{DH}}$: Horizontal diffuse irradiance [W/m²]
- $I_{\text{GH}}$: Horizontal global irradiance [W/m²]
- $f$: Posture efficiency factor (standing $f=0.725$, sitting $f=0.696$)
- $F_{\text{SVF}}$: Sky view factor
- $F_{\text{GVF}}$: Ground view factor
- $\rho_g$: Ground reflectance

MRT increment due to shortwave radiation:

$$\Delta T_{\text{sw}} = \frac{I_{\text{body}} \cdot (\alpha / \varepsilon)}{f \cdot h_r}$$

Where $\alpha$ is the human body shortwave absorptivity, $\varepsilon$ is the human body longwave emissivity, and $h_r$ is the radiative heat transfer coefficient [W/(m²·K)].

**Three-Directional Longwave Radiation Decomposition:**

$$\Delta T_{\text{lw}} = c_{\text{lw}} \cdot \big[ F_{\text{SVF}} \cdot (T_{\text{sky}} - T_{\text{ref}}) + F_{\text{GVF}} \cdot (T_g - T_{\text{ref}}) + F_{\text{OVF}} \cdot (T_{\text{obs}} - T_{\text{ref}}) \big]$$

Where:
- $c_{\text{lw}}$: Longwave linearization coefficient (default 0.5)
- $T_{\text{sky}}$: Sky effective temperature [°C]
- $T_g$: Ground temperature [°C]
- $T_{\text{obs}}$: Surrounding obstacle surface temperature [°C]
- $T_{\text{ref}}$: Reference temperature (equal to air temperature) [°C]
- $F_{\text{OVF}}$: Obstacle view factor

View factor conservation:

$$F_{\text{SVF}} + F_{\text{GVF}} + F_{\text{OVF}} = 1.0$$

**Final MRT:**

$$T_{\text{MRT}} = T_a + \Delta T_{\text{sw}} + \Delta T_{\text{lw}}$$

**Solar Projection Coefficient:**

$$f_p(\gamma) = 0.308 \cdot \cos\gamma \cdot \left(0.998 - \frac{\gamma^2}{50000}\right), \quad \gamma \in [0°, 90°]$$

Where $\gamma$ is the solar altitude angle [°].

**Sky Effective Temperature:**

$$T_{\text{sky}} = \left( \frac{I_{\text{IR}}}{\varepsilon_{\text{sky}} \cdot \sigma} \right)^{0.25} - 273.15$$

Where $I_{\text{IR}}$ is the horizontal infrared radiation [W/m²], $\sigma = 5.67 \times 10^{-8}$ W/(m²·K⁴).

When $\varepsilon_{\text{sky}} < 0$ (auto mode), dew point temperature is used for calculation:

$$\varepsilon_{\text{sky}} = 0.711 + 0.56 \cdot T_d + 0.73 \cdot T_d^2$$

Where $T_d$ is the dew point temperature [°C], and the result is constrained within the range [0.5, 1.0].

### 1.2 RayMan Model

Based on the RayMan model by Matzarakis et al., employing a complete radiation balance method.

Longwave radiation in quartic form:

$$T_{\text{MRT}} = \bigl( \text{mrtK}_4 \bigr)^{0.25} - 273.15$$

Where:

$$\text{mrtK}_4 = \frac{1}{\sigma} \Big\{ \big[ L_{\text{sky}} + \tfrac{\alpha}{\varepsilon} I_{\text{DH}} \big] F_{\text{SVF}} + \big[ L_g + \tfrac{\alpha}{\varepsilon} \rho_g I_{\text{GH}} \big] F_{\text{GVF}} + L_{\text{obs}} F_{\text{OVF}} \Big\} + \frac{\alpha \cdot I_{\text{direct}}}{\varepsilon \cdot \sigma}$$

Longwave radiation in each direction:

$$L_{\text{sky}} = \varepsilon_{\text{sky}} \cdot \sigma \cdot T_{\text{sky,K}}^4$$

$$L_g = \varepsilon_g \cdot \sigma \cdot T_{g,\text{K}}^4$$

$$L_{\text{obs}} = \varepsilon_{\text{obs}} \cdot \sigma \cdot T_{\text{obs,K}}^4$$

Direct solar radiation term (applied only when $I_{\text{DN}} > 0$ and $f_{\text{exp}} > 0$):

$$I_{\text{direct}} = f_{\text{exp}} \cdot f_p(\gamma) \cdot I_{\text{DN}}$$

### 1.3 Unified Entry Point

The `CalculateMRT()` method automatically selects one of the above two models based on the `useRayMan` parameter.

---

## 2. Human Exposure Factor and View Factor Model (HumanExposureModel.cs)

### 2.1 Solar Exposure Factor Calculation

Backward ray tracing is used to calculate the proportion of the human body directly illuminated by the sun, $f_{\text{exp}} \in [0, 1]$.

**Algorithm Flow:**

1. Evenly distribute $N$ sampling points along the body height direction (default $N=3$, from ground level to full body height)
2. For each sampling point, emit a ray along the solar direction
3. Detect whether the ray intersects with the obstacle mesh
4. Exposure factor = number of unoccluded sampling points / total number of sampling points

$$f_{\text{exp}} = \frac{N_{\text{exposed}}}{N_{\text{total}}}$$

**Parameters:**

| Parameter | Unit | Default | Description |
|:---:|:---:|:---:|:---|
| bodyHeight | m | 1.7 | Total human body height |
| analysisHeight | m | 1.5 | Analysis eye height (for view factor calculation) |
| samplePointCount | - | 3 | Number of sampling points in the height direction |
| maxRayDistance | m | 500.0 | Maximum ray tracing distance |
| rayOffset | m | 0 | Ray starting point offset |

### 2.2 Full 4π Spherical View Factor Decomposition

The full surrounding space of the human body (4π sphere) is decomposed into three parts, satisfying the conservation condition:

$$F_{\text{SVF}} + F_{\text{GVF}} + F_{\text{OVF}} = 1.0$$

**Algorithm Flow:**

1. Use the Fibonacci (golden angle) spiral to generate $N$ uniformly distributed full-sphere direction vectors
2. Emit a ray from the analysis point in each direction
3. Classify results based on ray tracing:
   - Ray unoccluded and $Z > 0$ → counted toward sky view factor $F_{\text{SVF}}$
   - Ray unoccluded and $Z < 0$ → counted toward ground view factor $F_{\text{GVF}}$
   - Ray occluded by obstacles → counted toward obstacle view factor $F_{\text{OVF}}$

**Fibonacci Sphere Sampling:**

$$z_i = 1 - \frac{2i + 1}{N}, \quad r_i = \sqrt{1 - z_i^2}, \quad \theta_i = \phi \cdot i$$

$$\vec{d}_i = (r_i \cos\theta_i,\; r_i \sin\theta_i,\; z_i)$$

Where $\phi = \pi(3 - \sqrt{5}) \approx 2.39996$ rad is the golden angle.

### 2.3 Upper-Hemisphere Sky View Factor

Used for traditional sky view factor calculation considering only the upper hemisphere (no longer used in longwave decomposition, retained for compatibility):

$$F_{\text{SVF}} = \frac{N_{\text{visible,sky}}}{N_{\text{total}}}$$

Direction generation uses Fibonacci upper-hemisphere sampling:

$$z_i = \sqrt{1 - \frac{i}{N}}, \quad i = 0, 1, \ldots, N-1$$

---

## 3. MRT Grasshopper Component (MRTcalculator.cs)

### 3.1 Input Parameters

| Index | Parameter | Type | Description |
|:---:|:---:|:---:|:---|
| 0 | EPW File | Text | EPW weather file path |
| 1 | Analysis Points | Point3d List | Ground analysis points (must be located at ground surface Z=0, not meteorological height) |
| 2 | Obstacles | Brep List | Obstacles / environmental geometry (optional) |
| 3 | MRT Settings | Generic | MRT configuration settings (optional, default new settings) |
| 4 | Time Settings | Generic | Simulation time period (optional, default full year 8760h) |
| 5 | Air Temperature (Ta) | Number Tree | Air temperature [°C], supports 4 input modes (optional) |
| 6 | Ground Temperature (Tg) | Number Tree | Ground temperature [°C], supports 4 input modes (optional) |
| 7 | Surrounding Surface Temp (Tsur) | Number List | Surrounding obstacle surface temperature [°C] (optional) |
| 8 | Run | Boolean | Set to true to execute simulation |

**Ta/Tg Input Modes:**

| Mode | Data Quantity | Description |
|:---:|:---:|:---|
| 1 | 1 value | Uniform fixed temperature, all time steps and all points |
| 2 | N values (N = number of points) | Per-point fixed temperature |
| 3 | 8760 values | Hourly varying temperature (shared by all points) |
| 4 | N×8760 Tree | Per-point hourly varying temperature |

### 3.2 Output Parameters

| Index | Parameter | Description |
|:---:|:---:|:---|
| 0 | MRT | Mean radiant temperature [°C], per point per hour |
| 1 | SVF | Sky view factor [0–1], full 4π sampling |
| 2 | GVF | Ground view factor [0–1], full 4π sampling |
| 3 | OVF | Obstacle view factor [0–1], full 4π sampling |
| 4 | Exp | Solar exposure factor [0–1], per point per hour |
| 5 | dT_sw | MRT increment due to shortwave radiation [°C] |
| 6 | dT_lw | Total MRT increment due to longwave radiation [°C] |
| 7 | dT_lw_sky | Sky longwave component contribution [°C] |
| 8 | dT_lw_grd | Ground longwave component contribution [°C] |
| 9 | dT_lw_obs | Obstacle longwave component contribution [°C] |
| 10 | HrMRT | Hourly average MRT of all points [°C] |
| 11 | SunVec | Solar vector for each analysis time step |

---

## 4. MRT Settings Component (MRTsettings.cs)

### 4.1 Input Parameters

| Parameter | ID | Unit | Default | Description |
|:---:|:---:|:---:|:---:|:---|
| Use RayMan | RayMan | - | false | Use RayMan model instead of SolarCal |
| Posture | Post | - | 0 | Body posture: 0=standing (f=0.725), 1=sitting (f=0.696) |
| Body Absorptivity | Alpha | - | 0.7 | Shortwave absorptivity |
| Body Emissivity | Epsilon | - | 0.95 | Longwave emissivity |
| Radiative HTC | Hr | W/(m²·K) | 6.012 | Radiative heat transfer coefficient |
| Floor Reflectance | Rho | - | 0.25 | Ground reflectance |
| Exposure Samples | NExp | - | 3 | Number of ray tracing sampling points for exposure factor |
| Eye Height for SVF | HSVF | m | 1.5 | Eye height for view factor calculation |
| Body Height | HBod | m | 1.7 | Total human body height |
| Sky Emissivity | SkyEps | - | -1 | Sky emissivity, -1=auto (dew point calculation) |
| SVF Sample Count | SVF_N | - | 1000 | Full 4π sphere sample count |
| Longwave Coeff | LwCoeff | - | 0.5 | Longwave linearization coefficient |
| Ground Emissivity | EpsGrd | - | 0.95 | Ground longwave emissivity (RayMan only) |
| Obstacle Emissivity | EpsObs | - | 0.95 | Obstacle longwave emissivity (RayMan only) |

---

## 5. References

1. ASHRAE Standard 55-2017: Thermal Environmental Conditions for Human Occupancy. American Society of Heating, Refrigerating and Air-Conditioning Engineers, Atlanta, GA, 2017.

2. Arens, E., et al. (2015). Modeling the comfort effects of short-wave solar radiation indoors. *Building and Environment*, 88, 3-9. https://doi.org/10.1016/j.buildenv.2014.09.004

3. Matzarakis, A., Rutz, F., & Mayer, H. (2010). Modelling radiation fluxes in simple and complex environments: basics of the RayMan model. *International Journal of Biometeorology*, 54, 131-139. https://doi.org/10.1007/s00484-009-0261-5

4. ISO 7726:1998. Ergonomics of the thermal environment — Instruments for measuring physical quantities. International Organization for Standardization, Geneva, 1998.
