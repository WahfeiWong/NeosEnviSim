# MRT (Mean Radiant Temperature) Module

This module calculates the Mean Radiant Temperature (MRT) for the human body in outdoor environments. It is based on backward ray-tracing building/vegetation occlusion analysis, supports both the SolarCal (ASHRAE 55) and RayMan calculation models, and employs full 4π spherical view factor decomposition to achieve physically correct longwave radiation computation.

**Enhanced (2026-06-14):** Introduces fine-grained Direct Normal Irradiance (DNI) exposure factor calculation, supporting differentiated transmission through three obstacle types: opaque objects (full block), trees (Beer-Lambert canopy transmission), and translucent sunshades (fixed transmittance). A classified Obstacle Set (ObstacleSet) replaces the original flat Brep list input to achieve more accurate direct radiation calculation.

---

## 1. MRT Physical Model (MRTmodel.cs)

### 1.1 SolarCal Model (ASHRAE 55)

Based on the SolarCal model from ASHRAE Standard 55, combining shortwave solar radiation increment with three-directional longwave radiation decomposition.

**Shortwave Radiation Increment:**

The solar radiation flux received by the human body consists of direct radiation, diffuse radiation, and ground-reflected radiation:

$$I_{\text{body}} = f_{\text{DNI}} \cdot f_p(\gamma) \cdot I_{\text{DN}} + \frac{1}{2} f \cdot F_{\text{SVF}} \cdot I_{\text{DH}} + \frac{1}{2} f \cdot F_{\text{GVF}} \cdot \rho_g \cdot I_{\text{GH}}$$

Where:
- $f_{\text{DNI}}$: **Effective DNI exposure factor** (0–1), combining exposure and transmission effects (see Section 2.1 enhanced description)
- $f_p(\gamma)$: Solar projection coefficient (see equation below)
- $I_{\text{DN}}$: Direct normal irradiance [W/m²]
- $I_{\text{DH}}$: Horizontal diffuse irradiance [W/m²]
- $I_{\text{GH}}$: Horizontal global irradiance [W/m²]
- $f$: Posture efficiency factor (standing $f=0.725$, sitting $f=0.696$)
- $F_{\text{SVF}}$: Sky view factor
- $F_{\text{GVF}}$: Ground view factor
- $\rho_g$: Ground reflectance

> **Note (2026-06-14):** The direct radiation term now uses the effective DNI exposure factor $f_{\text{DNI}}$ instead of the traditional binary exposure factor $f_{\text{exp}}$. $f_{\text{DNI}}$ considers the differentiated transmission effects of obstacle types (opaque / tree / translucent sunshade) on direct radiation. When the ObsSet component is not connected (no obstacle classification), $f_{\text{DNI}} = f_{\text{exp}}$, maintaining backward compatibility.

MRT increment due to shortwave radiation:

$$\Delta T_{\text{sw}} = \frac{I_{\text{body}} \cdot (\alpha / \varepsilon)}{f \cdot h_r}$$

Where $\alpha$ is the human body shortwave absorptivity, $\varepsilon$ is the human body longwave emissivity, and $h_r$ is the radiative heat transfer coefficient [W/(m²·K)].

**Three-Directional Longwave Radiation Decomposition:**

$$\Delta T_{\text{lw}} = c_{\text{lw}} \cdot \left[ F_{\text{SVF}} \cdot (T_{\text{sky}} - T_{\text{ref}}) + F_{\text{GVF}} \cdot (T_g - T_{\text{ref}}) + F_{\text{OVF}} \cdot (T_{\text{obs}} - T_{\text{ref}}) \right]$$

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

$$T_{\text{MRT}} = (\text{mrtK}_4)^{0.25} - 273.15$$

Where:

$$\text{mrtK}_4 = \frac{1}{\sigma} \left( \left[ L_{\text{sky}} + \frac{\alpha}{\varepsilon} I_{\text{DH}} \right] F_{\text{SVF}} + \left[ L_g + \frac{\alpha}{\varepsilon} \rho_g I_{\text{GH}} \right] F_{\text{GVF}} + L_{\text{obs}} F_{\text{OVF}} \right) + \frac{\alpha \cdot I_{\text{direct}}}{\varepsilon \cdot \sigma}$$

Longwave radiation in each direction:

$$L_{\text{sky}} = \varepsilon_{\text{sky}} \cdot \sigma \cdot T_{\text{sky,K}}^4$$

$$L_g = \varepsilon_g \cdot \sigma \cdot T_{g,\text{K}}^4$$

$$L_{\text{obs}} = \varepsilon_{\text{obs}} \cdot \sigma \cdot T_{\text{obs,K}}^4$$

Direct solar radiation term (applied only when $I_{\text{DN}} > 0$ and $f_{\text{DNI}} > 0$):

$$I_{\text{direct}} = f_{\text{DNI}} \cdot f_p(\gamma) \cdot I_{\text{DN}}$$

> **Note (2026-06-14):** The direct radiation term now uses $f_{\text{DNI}}$ instead of $f_{\text{exp}}$. $f_{\text{DNI}}$ is obtained through the fine-grained calculation in ObstacleSet and HumanExposureModel (see Section 2).

### 1.3 Unified Entry Point

The `CalculateMRT()` method automatically selects one of the above two models based on the `useRayMan` parameter.

---

## 2. Human Exposure Factor and View Factor Model (HumanExposureModel.cs)

### 2.1 Solar Exposure Factor Calculation (Enhanced, 2026-06-14)

Backward ray tracing is used to calculate the proportion of the human body directly illuminated by the sun. This module provides two types of exposure factors:

- **$f_{\text{exp}}$ (Traditional exposure factor):** Binary judgment, only determines whether a sample point is shaded (0 or 1)
- **$f_{\text{DNI}}$ (Effective DNI exposure factor):** Combines exposure and transmission effects, supporting differentiated treatment of three obstacle types

**Traditional Exposure Factor Algorithm (backward compatible):**

1. Evenly distribute $N$ sampling points along the body height direction (default $N=3$, from ground level to full body height)
2. For each sampling point, emit a ray along the solar direction
3. Detect whether the ray intersects with the obstacle mesh
4. Exposure factor = number of unoccluded sampling points / total number of sampling points

$$f_{\text{exp}} = \frac{N_{\text{exposed}}}{N_{\text{total}}}$$

**Effective DNI Exposure Factor $f_{\text{DNI}}$ (Enhanced):**

For each height sample point, a ray is cast along the solar direction and the DNI contribution is computed based on the hit obstacle type:

| Hit Type | DNI Contribution | Physical Meaning |
|:---:|:---:|:---|
| No occlusion | 1.0 | Full direct radiation |
| Opaque object | 0.0 | Fully blocked; all Tree/Translucent behind it are in shadow |
| Tree detail mesh | $\exp(-k \cdot \text{LAD} \cdot s)$ | Beer-Lambert canopy transmission (only when no Opaque on ray path) |
| Translucent sunshade | $\tau$ | Fixed transmittance (only when no Opaque on ray path) |

The effective DNI exposure factor is the average of all sample point contributions:

$$f_{\text{DNI}} = \frac{1}{N} \sum_{i=1}^{N} \text{DNI}_{\text{contrib},i}$$

**Physical Logic Correction (2026-06-14):**

Light travels from the sun toward the ground (forward direction). The code uses backward ray tracing (casting rays from the human sample point toward the sun). The critical physical constraint is: **Opaque objects have absolute blocking priority** — if any opaque obstacle exists on the ray path, all tree or translucent sunshade obstacles behind it are in the building's shadow and must NOT contribute to DNI transmission.

Decision logic (corrected):
1. Check all Opaque meshes first — **any intersection immediately returns Opaque** (DNI = 0)
2. No opaque intersection → find the **farthest** Tree/Translucent intersection from the human (equivalent to the first obstacle from the sun direction)
3. If a ray intersects multiple non-opaque obstacle types, the **nearest from the sun direction** (farthest from the human) determines the obstacle type

**Beer-Lambert Canopy Transmission Equation:**

$$I_{\text{transmitted}} = I_{\text{DN}} \cdot \exp(-k \cdot \text{LAD} \cdot s)$$

Where:
- $s$: Geometric path length through the tree canopy [m], computed from entry/exit intersection points of the ray with the simplified canopy mesh
- $k$: Solar radiation extinction coefficient [-], default 0.5, typical range 0.5–0.8 (broadleaf), 0.3–0.5 (conifer)
- $\text{LAD}$: Leaf area density [m²/m³], default 1.0, typical range 0.5–8.0
- $\tau$: Sunshade direct solar transmittance [-], default 0.05

**Parameters:**

| Parameter | Unit | Default | Description |
|:---:|:---:|:---:|:---|
| bodyHeight | m | 1.7 | Total human body height |
| analysisHeight | m | 1.5 | Analysis eye height (for view factor calculation) |
| samplePointCount | - | 3 | Number of sampling points in the height direction |
| maxRayDistance | m | 500.0 | Maximum ray tracing distance |
| rayOffset | m | 0 | Ray starting point offset |

**Vegetation Canopy Path Length Calculation:**

Using `CalculateCanopyPathLength`, the geometric path length is computed via `MeshLine` ray intersection with the simplified canopy mesh:

$$s = t_{\text{exit}} - t_{\text{entry}}$$

Where $t_{\text{entry}}$ is the first valid intersection parameter where the ray enters the canopy, and $t_{\text{exit}}$ is the last valid intersection where it exits.

**New Core Methods:**

| Method | Function |
|:---|:---|
| `ClassifyRayHit()` | Ray-hit classification: Opaque absolute priority, non-Opaque uses farthest intersection |
| `CalculateCanopyPathLength()` | Computes geometric path length $s$ through canopy |
| `CalculateDNIExposureFactor()` | Single-point effective DNI exposure factor calculation |
| `CalculateDNIExposureFactorsBatch()` | Batch effective DNI exposure factor calculation (parallel) |

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

## 3. MRT Calculator Component (MRTcalculator.cs)

### 3.1 Input Parameters

| Index | Parameter | Type | Description |
|:---:|:---:|:---:|:---|
| 0 | EPW File | Text | EPW weather file path |
| 1 | Analysis Points | Point3d List | Ground analysis points (must be located at ground surface Z=0, not meteorological height) |
| 2 | Obstacle Set | Generic | **Enhanced (2026-06-14)**: Classified obstacle set (ObstacleSet). Connect ObsSet component. Supports opaque buildings, tree canopy transmission (Beer-Lambert), and translucent sunshades. Backward compatible: accepts List<Brep> or List<Mesh> as opaque obstacles (optional) |
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
| 4 | Exp | Solar exposure factor $f_{\text{exp}}$ [0–1], per point per hour (binary: exposed/shaded) |
| 5 | **DNIExp** | **New**: Effective DNI exposure factor $f_{\text{DNI}}$ [0–1], combining exposure and transmission |
| 6 | dT_sw | MRT increment due to shortwave radiation [°C] |
| 7 | dT_lw | Total MRT increment due to longwave radiation [°C] |
| 8 | dT_lw_sky | Sky longwave component contribution [°C] |
| 9 | dT_lw_grd | Ground longwave component contribution [°C] |
| 10 | dT_lw_obs | Obstacle longwave component contribution [°C] |
| 11 | HrMRT | Hourly average MRT of all points [°C] |
| 12 | SunVec | Solar vector for each analysis time step |

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

## 5. Obstacle Set Component (ObsSetSettings.cs)

### 5.1 Description

The ObsSet component creates a classified obstacle set (`ObstacleSet`) that replaces the original flat Brep list input for the MRT component and the GroundSet component. By classifying obstacles into opaque objects, trees (with canopy transmission), and translucent sunshades, it enables fine-grained direct normal irradiance (DNI) calculation.

**Key Principle:**
- When a sample point is blocked by backward ray tracing, the traditional method completely ignores DNI contribution ($f_{\text{exp}}=0$ → DNI=0)
- The enhanced method computes partial DNI transmission based on obstacle type, using Beer-Lambert law for vegetation and fixed transmittance for translucent materials
- The effective DNI exposure factor $f_{\text{DNI}}$ combines exposure and transmission effects, replacing $f_{\text{exp}}$ in direct radiation calculations

### 5.2 Input Parameters

| Index | Parameter | ID | Type | Default | Description |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | Tree Detail | TreeDet | Mesh List | — | Detailed tree geometry mesh (leaves, branches) from Tree Processor |
| 1 | Tree Canopy | TreeCan | Mesh List | — | Simplified tree canopy envelope mesh(es) for path-length calculation |
| 2 | Leaf Area Density | LAD | Number | 1.0 | Leaf area density [m²/m³], leaf area per unit canopy volume |
| 3 | Extinction Coeff | k | Number | 0.5 | Solar radiation extinction coefficient [-], Beer-Lambert parameter, typical 0.5–0.8 |
| 4 | Translucent Shade | TransShd | Mesh List | — | Translucent sunshade / shading device mesh(es) |
| 5 | Transmittance | Tau | Number | 0.05 | Direct solar transmittance of translucent sunshades [-], range 0.0–1.0 |
| 6 | Opaque Objects | Opaque | Mesh List | — | Opaque obstacles (buildings, walls) that fully block direct radiation |

**Typical Transmittance Reference Values:**

| Material Type | Transmittance Range |
|:---:|:---:|
| Perforated metal | 0.05–0.15 |
| Shade fabric | 0.02–0.30 |
| Polycarbonate (PC) sheet | 0.60–0.85 |
| Glass | 0.70–0.90 |

### 5.3 Output Parameters

| Index | Parameter | Description |
|:---:|:---:|:---|
| 0 | ObsSet | Classified obstacle set, connect to MRT component's Obstacle Set input or GroundSet's ObsSet input |

### 5.4 Usage Notes

1. **Tree Detail vs Tree Canopy**: Tree Detail is used for ray-hit detection (determining if a point is under tree shade), while Tree Canopy is used for path-length calculation (distance the ray travels through the canopy). Both are required; if Tree Detail is provided without Tree Canopy, the component issues a warning and DNI transmission uses zero path length.

2. **Backward Compatibility**: The Obstacle Set input of the MRT component and GroundSet component remains backward compatible, still accepting List<Brep> or List<Mesh> inputs (automatically classified as Opaque obstacles).

3. **Mesh Input**: Supports direct Mesh type input. If Surface or Brep types are provided, Grasshopper can implicitly convert them to Mesh.

---

## 6. References

1. ASHRAE Standard 55-2017: Thermal Environmental Conditions for Human Occupancy. American Society of Heating, Refrigerating and Air-Conditioning Engineers, Atlanta, GA, 2017.

2. Arens, E., et al. (2015). Modeling the comfort effects of short-wave solar radiation indoors. *Building and Environment*, 88, 3-9. https://doi.org/10.1016/j.buildenv.2014.09.004

3. Matzarakis, A., Rutz, F., & Mayer, H. (2010). Modelling radiation fluxes in simple and complex environments: basics of the RayMan model. *International Journal of Biometeorology*, 54, 131-139. https://doi.org/10.1007/s00484-009-0261-5

4. ISO 7726:1998. Ergonomics of the thermal environment — Instruments for measuring physical quantities. International Organization for Standardization, Geneva, 1998.
