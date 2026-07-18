# MRT (Mean Radiant Temperature) Module

This module calculates the Mean Radiant Temperature (MRT) for the human body in outdoor environments. It is based on backward ray-tracing building/vegetation occlusion analysis and supports both the SolarCal (ASHRAE 55) and RayMan calculation models. Full-sphere (4π) view factors are decomposed into **five components** (sky, ground, opaque obstacles, trees, translucent shades), and both longwave and diffuse radiation are computed per component, achieving physically correct radiative environment modeling.

**Current Version Highlights (2026-06-16):**
1. **Five-component view factor decomposition**: SVF / GVF / OVF_opaque / TVF / TRVF are mutually exclusive and satisfy conservation; overlapping directions are classified by priority Opaque > TreeDetail > TranslucentShade.
2. **Five-component longwave decomposition**: sky, ground, opaque obstacles, tree canopy, and translucent shade each contribute an independent longwave term with its own surface temperature.
3. **Surface temperatures moved to the ObsSet component**: the Tsur input has been removed from the MRT component; opaque surface temperature (T_opaque), tree canopy temperature (T_tree), and translucent surface temperature (T_trans) are provided via the ObsSet component as a single value or an 8760-hourly series, falling back to air temperature when omitted.
4. **MRT component I/O reorganized**: 8 inputs and 17 outputs (9 MRT-related, 5 view factors, 2 exposure factors, 1 other), with each of the five longwave component contributions output individually.

**Previous Enhancement (2026-06-15):** Corrected DNI physics — when a ray intersects multiple non-opaque obstacle types, the DNI contribution is the **product** of individual contributions from each (or multiple same-type) obstacle, rather than considering only the nearest occlusion from the sun direction. Added `CalculateRayDNITransmission` core method, replacing the legacy `ClassifyRayHit` single-obstacle classification logic. ObsSet input is **no longer backward-compatible**, only accepts ObstacleSet encapsulated data.

**Previous Enhancement (2026-06-14):** Introduces fine-grained Direct Normal Irradiance (DNI) exposure factor calculation, supporting differentiated transmission through three obstacle types: opaque objects (full block), trees (Beer-Lambert canopy transmission), and translucent sunshades (fixed transmittance).

---

## 1. MRT Physical Model (MRTmodel.cs)

### 1.1 SolarCal Model (ASHRAE 55)

Based on the SolarCal model from ASHRAE Standard 55, combining the shortwave solar radiation increment with a **five-component longwave radiation decomposition**.

**Shortwave Radiation Increment:**

The solar radiation flux received by the human body consists of direct radiation, diffuse radiation, and ground-reflected radiation:

$$I_{\text{body}} = f_{\text{DNI}} \cdot f_p(\gamma) \cdot I_{\text{DN}} + \frac{1}{2} f \cdot I_{\text{DH,eff}} + \frac{1}{2} f \cdot F_{\text{GVF}} \cdot \rho_g \cdot I_{\text{GH}}$$

**Effective diffuse irradiance (five-component decomposition):**

$$I_{\text{DH,eff}} = I_{\text{DH}} \cdot \left( F_{\text{SVF}} + F_{\text{TVF}} \cdot e^{-k_c \cdot \text{LAD} \cdot l} + F_{\text{TRVF}} \cdot \tau \right)$$

Where:
- $F_{\text{SVF}}$: Sky view factor (fraction of directions with visible sky)
- $F_{\text{TVF}}$: Tree view factor (fraction of directions blocked by tree detail meshes)
- $F_{\text{TRVF}}$: Translucent view factor (fraction of directions blocked by translucent shade meshes)
- $k_c$: Canopy extinction coefficient
- $\text{LAD}$: Leaf area density [m²/m³]
- $l$: Characteristic canopy thickness [m], taken as the Z-extent of the combined bounding box of all simplified canopy meshes
- $\tau$: Shortwave transmittance of translucent materials

That is: diffuse radiation from sky directions arrives in full; diffuse from tree directions is attenuated by the Beer-Lambert law; diffuse from translucent directions is attenuated by the transmittance $\tau$; opaque obstacle directions ($F_{\text{OVF,opaque}}$) contribute no diffuse radiation.

Additional symbols:
- $f_{\text{DNI}}$: **Effective DNI exposure factor** (0–1), combining exposure and transmission effects (see Section 2.1)
- $f_p(\gamma)$: Solar projection coefficient (see equation below)
- $I_{\text{DN}}$: Direct normal irradiance [W/m²]
- $I_{\text{DH}}$: Horizontal diffuse irradiance [W/m²]
- $I_{\text{GH}}$: Horizontal global irradiance [W/m²]
- $f$: Posture efficiency factor (standing $f=0.725$, sitting $f=0.696$)
- $F_{\text{GVF}}$: Ground view factor
- $\rho_g$: Ground reflectance

> **Note (2026-06-15):** The direct radiation term now uses the effective DNI exposure factor $f_{\text{DNI}}$ instead of the traditional binary exposure factor $f_{\text{exp}}$. $f_{\text{DNI}}$ considers the differentiated transmission effects of obstacle types (opaque / tree / translucent sunshade) on direct radiation, and when a ray passes through multiple non-opaque obstacles, individual transmittances **multiply** (physical correction). When the ObsSet component is not connected (no obstacles), $f_{\text{DNI}} = 1.0$ (full direct).

MRT increment due to shortwave radiation:

$$\Delta T_{\text{sw}} = \frac{I_{\text{body}} \cdot (\alpha / \varepsilon)}{f \cdot h_r}$$

Where $\alpha$ is the human body shortwave absorptivity, $\varepsilon$ is the human body longwave emissivity, and $h_r$ is the radiative heat transfer coefficient [W/(m²·K)].

**Five-Component Longwave Radiation Decomposition (2026-06-16):**

The MRT increment due to longwave radiation is computed separately for each of the five environmental surface categories and summed linearly:

$$\Delta T_{\text{lw}} = c_{\text{lw}} \cdot \left[ F_{\text{SVF}} (T_{\text{sky}} - T_{\text{ref}}) + F_{\text{GVF}} (T_g - T_{\text{ref}}) + F_{\text{OVF,opaque}} (T_{\text{opaque}} - T_{\text{ref}}) + F_{\text{TVF}} (T_{\text{canopy}} - T_{\text{ref}}) + F_{\text{TRVF}} (T_{\text{trans}} - T_{\text{ref}}) \right]$$

Where:
- $c_{\text{lw}}$: Longwave linearization coefficient (default 0.5)
- $T_{\text{ref}}$: Reference temperature, equal to the air temperature $T_a$ [°C]

View factors, surface temperatures, and their data sources for each component:

| Component | View Factor | Surface Temp. | Temperature Source | Fallback |
|:---:|:---:|:---:|:---|:---:|
| Sky | $F_{\text{SVF}}$ | $T_{\text{sky}}$ | Derived from horizontal infrared $I_{\text{IR}}$ and sky emissivity $\varepsilon_{\text{sky}}$ (see below) | — |
| Ground | $F_{\text{GVF}}$ | $T_g$ | Tg input of the MRT component (4 input modes) | $T_a$ |
| Opaque obstacle | $F_{\text{OVF,opaque}}$ | $T_{\text{opaque}}$ | T_opaque input of the ObsSet component (single value or 8760 hourly) | $T_a$ |
| Tree canopy | $F_{\text{TVF}}$ | $T_{\text{canopy}}$ | T_tree input of the ObsSet component (single value or 8760 hourly) | $T_a$ |
| Translucent shade | $F_{\text{TRVF}}$ | $T_{\text{trans}}$ | T_trans input of the ObsSet component (single value or 8760 hourly) | $T_a$ |

Each component contribution is output individually by the MRT component (dTlw_sky / dTlw_grd / dTlw_opq / dTlw_tree / dTlw_trans); the total increment $\Delta T_{\text{lw}}$ is their sum (see Section 3.2).

**Five-Component View Factor Decomposition:**

The full-sphere (4π) view factors are decomposed into five mutually exclusive components:

| Component | Symbol | Description |
|:---:|:---:|:---|
| Sky View Factor | $F_{\text{SVF}}$ | Visible sky (Z > 0, no obstruction) |
| Ground View Factor | $F_{\text{GVF}}$ | Visible ground (Z < 0, no obstruction) |
| Opaque Obstacle View Factor | $F_{\text{OVF,opaque}}$ | Blocked by opaque objects only |
| Tree View Factor | $F_{\text{TVF}}$ | Blocked by tree detail meshes |
| Translucent View Factor | $F_{\text{TRVF}}$ | Blocked by translucent shade meshes |

**Conservation:**

$$F_{\text{SVF}} + F_{\text{GVF}} + F_{\text{OVF,opaque}} + F_{\text{TVF}} + F_{\text{TRVF}} = 1.0$$

**Overlap Resolution Priority:** Opaque > TreeDetail > TranslucentShade. When a direction is blocked by multiple obstacle types, it is assigned to the highest-priority category.

> **Note:** The OVF is now **OPAQUE-ONLY** (previously included all obstacle types). TVF and TRVF represent the fractions of the full sphere blocked by tree detail meshes and translucent shade meshes, respectively.

**Final MRT:**

$$T_{\text{MRT}} = T_a + \Delta T_{\text{sw}} + \Delta T_{\text{lw}}$$

**Solar Projection Coefficient:**

$$f_p(\gamma) = 0.308 \cdot \cos\gamma \cdot \left(0.998 - \frac{\gamma^2}{50000}\right), \quad \gamma \in [0°, 90°]$$

Where $\gamma$ is the solar altitude angle [°].

**Sky Effective Temperature:**

$$T_{\text{sky}} = \left( \frac{I_{\text{IR}}}{\varepsilon_{\text{sky}} \cdot \sigma} \right)^{0.25} - 273.15$$

Where $I_{\text{IR}}$ is the horizontal infrared radiation [W/m²], $\sigma = 5.67 \times 10^{-8}$ W/(m²·K⁴).

When $\varepsilon_{\text{sky}} < 0$ (auto mode, default), dew point temperature is used:

$$\varepsilon_{\text{sky}} = 0.711 + 0.56 \cdot \frac{T_d}{100} + 0.73 \cdot \left(\frac{T_d}{100}\right)^2$$

Where $T_d$ is the dew point temperature [°C], and the result is constrained within [0.5, 1.0]. If dew point data is missing, total sky cover is used instead ($\varepsilon_{\text{sky}} = 0.75 + 0.02 N$, where $N$ is the total sky cover, also constrained to [0.5, 1.0]); if both are missing, $\varepsilon_{\text{sky}} = 1.0$ (blackbody sky).

### 1.2 RayMan Model

Based on the RayMan model by Matzarakis et al., employing a complete radiation balance method (quartic form).

**Longwave radiation uses the same five-component decomposition** as SolarCal, sharing the same set of decomposed view factors:

$$T_{\text{MRT}} = (\text{mrtK}_4)^{0.25} - 273.15$$

Where:

$$\text{mrtK}_4 = \frac{1}{\sigma} \left[ \left( L_{\text{sky}} + \frac{\alpha}{\varepsilon} I_{\text{DH,eff}} \right) F_{\text{SVF}} + \left( L_g + \frac{\alpha}{\varepsilon} \rho_g I_{\text{GH}} \right) F_{\text{GVF}} + L_{\text{obs}} F_{\text{OVF,opaque}} + L_{\text{tree}} F_{\text{TVF}} + L_{\text{trans}} F_{\text{TRVF}} \right] + \frac{\alpha \cdot I_{\text{direct}}}{\varepsilon \cdot \sigma}$$

Longwave radiation in each direction (all temperatures in Kelvin):

$$L_{\text{sky}} = \varepsilon \cdot \sigma \cdot T_{\text{sky,K}}^4$$

$$L_g = \varepsilon_g \cdot \sigma \cdot T_{g,\text{K}}^4$$

$$L_{\text{obs}} = \varepsilon_{\text{obs}} \cdot \sigma \cdot T_{\text{opaque,K}}^4$$

$$L_{\text{tree}} = \varepsilon_{\text{obs}} \cdot \sigma \cdot T_{\text{canopy,K}}^4$$

$$L_{\text{trans}} = \varepsilon_{\text{obs}} \cdot \sigma \cdot T_{\text{trans,K}}^4$$

Where:
- $T_{\text{sky,K}}$: Sky effective temperature [K] derived from $I_{\text{IR}}$ and $\varepsilon_{\text{sky}}$; the sky longwave term is weighted by the body emissivity $\varepsilon$ as absorbed by the human body
- $\varepsilon_g$: Ground longwave emissivity (EpsGrd in MRT Settings, default 0.95)
- $\varepsilon_{\text{obs}}$: Obstacle longwave emissivity (EpsObs in MRT Settings, default 0.95); **shared by all three obstacle surface categories: opaque objects, tree canopy, and translucent shade**
- $T_{\text{opaque,K}}$, $T_{\text{canopy,K}}$, $T_{\text{trans,K}}$: Surface temperatures [K] of the three obstacle categories, sourced the same way as in SolarCal (via the ObsSet component; fallback to $T_a$ when not provided)
- $I_{\text{DH,eff}}$: Effective diffuse irradiance from the five-component decomposition (same formula as SolarCal, see Section 1.1)
- The ground-reflected shortwave term $\rho_g I_{\text{GH}}$ is applied to the ground direction only ($F_{\text{GVF}}$)

Direct solar radiation term (applied only when $I_{\text{DN}} > 0$ and $f_{\text{DNI}} > 0$):

$$I_{\text{direct}} = f_{\text{DNI}} \cdot f_p(\gamma) \cdot I_{\text{DN}}$$

> **Note (2026-06-15):** The direct radiation term now uses $f_{\text{DNI}}$ instead of $f_{\text{exp}}$. $f_{\text{DNI}}$ is obtained through the fine-grained calculation in ObstacleSet and HumanExposureModel, with individual transmittances multiplying when a ray passes through multiple non-opaque obstacles (see Section 2.1).

### 1.3 Unified Entry Point

The `CalculateMRT()` method automatically selects one of the above two models based on the `useRayMan` parameter.

---

## 2. Human Exposure Factor and View Factor Model (HumanExposureModel.cs)

### 2.1 Solar Exposure Factor Calculation (Enhanced, 2026-06-15)

Backward ray tracing is used to calculate the proportion of the human body directly illuminated by the sun. This module provides two types of exposure factors:

- **$f_{\text{exp}}$ (Traditional exposure factor):** Binary judgment, only determines whether a sample point is shaded (0 or 1)
- **$f_{\text{DNI}}$ (Effective DNI exposure factor):** Combines exposure and transmission effects, supporting differentiated treatment of three obstacle types, with multi-obstacle transmittances **multiplying**

**Traditional Exposure Factor Algorithm:**

1. Evenly distribute $N$ sampling points along the body height direction (default $N=3$, from ground level to full body height)
2. For each sampling point, emit a ray along the solar direction
3. Detect whether the ray intersects with the obstacle mesh
4. Exposure factor = number of unoccluded sampling points / total number of sampling points

$$f_{\text{exp}} = \frac{N_{\text{exposed}}}{N_{\text{total}}}$$

**Effective DNI Exposure Factor $f_{\text{DNI}}$ (Enhanced, 2026-06-15):**

For each height sample point, a ray is cast along the solar direction and the cumulative transmission through **all** non-opaque obstacles is computed:

| Hit Type | DNI Contribution | Physical Meaning |
|:---:|:---:|:---|
| No occlusion | 1.0 | Full direct radiation |
| Opaque object | 0.0 | Fully blocked; all Tree/Translucent behind it are in shadow |
| Tree detail mesh | $\exp(-k \cdot \text{LAD} \cdot s)$ | Beer-Lambert canopy transmission (path length additive across canopies) |
| Translucent sunshade | $\tau$ | Fixed transmittance (each intersected Translucent Mesh contributes independently, multiple hits multiply) |

**Multi-Obstacle DNI Transmission (2026-06-15 Physical Correction):**

When a ray simultaneously passes through multiple (or multiple same-type) non-opaque obstacles, the total DNI transmission is the **product** of individual obstacle transmissions:

$$T_{\text{total}} = T_{\text{tree}} \times T_{\text{translucent,1}} \times T_{\text{translucent,2}} \times \cdots$$

Where:
- $T_{\text{tree}} = \exp(-k \cdot \text{LAD} \cdot s_{\text{total}})$: Total transmission through all canopies (total path length $s_{\text{total}}$ is the sum of individual canopy path lengths)
- $T_{\text{translucent},i} = \tau$: Transmittance of the $i$-th translucent sunshade mesh (each multiplied independently)

**Example:** A ray passes through a tree canopy (transmittance 0.6), then through two translucent sunshades (transmittance 0.5 each):

$$T_{\text{total}} = 0.6 \times 0.5 \times 0.5 = 0.15$$

The effective DNI exposure factor is the average of all sample point contributions:

$$f_{\text{DNI}} = \frac{1}{N} \sum_{i=1}^{N} \text{DNI}_{\text{contrib},i}$$

**Core Physical Logic:**

1. **Opaque absolute priority**: Any Opaque intersection on the ray path → DNI = 0
2. **Tree transmission**: Any TreeDetail hit → Beer-Lambert transmission using total canopy path length
3. **Translucent transmission**: Each TranslucentShade Mesh hit → multiply by $\tau$
4. **Product rule**: All non-opaque transmittances multiply, result clamped to [0, 1]

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

**Core Methods:**

| Method | Function | Status |
|:---|:---|:---|
| `CalculateRayDNITransmission()` | Compute cumulative DNI transmission [0, 1] through all obstacles | **New (2026-06-15)** |
| `ClassifyRayHit()` | Ray-hit classification (single obstacle, nearest) | **Deprecated**, retained for compatibility |
| `CalculateCanopyPathLength()` | Computes geometric path length $s$ through canopy | Unchanged |
| `CalculateDNIExposureFactor()` | Single-point effective DNI exposure factor | Internally calls `CalculateRayDNITransmission` |
| `CalculateDNIExposureFactorsBatch()` | Batch effective DNI exposure factor (parallel) | Internally calls `CalculateDNIExposureFactor` |

### 2.2 Full 4π Spherical View Factor Decomposition (Five Components)

The full surrounding space of the human body (4π sphere) is decomposed into **five mutually exclusive components**, satisfying the conservation condition:

$$F_{\text{SVF}} + F_{\text{GVF}} + F_{\text{OVF,opaque}} + F_{\text{TVF}} + F_{\text{TRVF}} = 1.0$$

| Component | Symbol | Description |
|:---:|:---:|:---|
| Sky View Factor | $F_{\text{SVF}}$ | Visible sky (Z > 0, no obstruction) |
| Ground View Factor | $F_{\text{GVF}}$ | Visible ground (Z < 0, no obstruction) |
| Opaque Obstacle View Factor | $F_{\text{OVF,opaque}}$ | Blocked by opaque objects only |
| Tree View Factor | $F_{\text{TVF}}$ | Blocked by tree detail meshes |
| Translucent View Factor | $F_{\text{TRVF}}$ | Blocked by translucent shade meshes |

**Algorithm Flow:**

1. Use the Fibonacci (golden angle) spiral to generate $N$ uniformly distributed full-sphere direction vectors
2. Emit a ray from the analysis point (eye height) in each direction
3. Classify each ray by priority (**Opaque > TreeDetail > TranslucentShade**):
   - Ray hits an opaque object → counted toward $F_{\text{OVF,opaque}}$
   - Otherwise hits a tree detail mesh → counted toward $F_{\text{TVF}}$
   - Otherwise hits a translucent shade mesh → counted toward $F_{\text{TRVF}}$
   - Unoccluded and $Z > 0$ → counted toward $F_{\text{SVF}}$
   - Unoccluded and $Z < 0$ → counted toward $F_{\text{GVF}}$

> **Note:** The simplified tree canopy envelope (TreeCanopy) does **not** participate in view-factor occlusion; it is used only for Beer-Lambert path-length calculation. View-factor occlusion is based solely on the Opaque + TreeDetail + TranslucentShade meshes.

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

## 3. MRT Calculator Component (OutdoorMRT.cs)

### 3.1 Input Parameters

| Index | Parameter | Type | Description |
|:---:|:---:|:---:|:---|
| 0 | EPW File | Text | EPW weather file path |
| 1 | Analysis Points | Point3d List | Ground analysis points (must be located at ground surface Z=0, not meteorological height) |
| 2 | Obstacle Set | Generic | **ObsSet (2026-06-15)**: Only accepts ObstacleSet encapsulated data. Connect ObsSet component. Supports opaque buildings, tree canopy transmission (Beer-Lambert), translucent sunshades, and per-category surface temperatures. **List&lt;Brep&gt;/List&lt;Mesh&gt; direct input no longer accepted** |
| 3 | MRT Settings | Generic | MRT configuration settings (optional, default new settings) |
| 4 | Time Settings | Generic | Simulation time period (optional, default full year 8760h) |
| 5 | Air Temperature (Ta) | Number Tree | Air temperature [°C], supports 4 input modes (optional) |
| 6 | Ground Temperature (Tg) | Number Tree | Ground temperature [°C], supports 4 input modes (optional) |
| 7 | Run | Boolean | Set to true to execute simulation |

> **Important Change (2026-06-16):** The Tsur (surrounding surface temperature) input has been **removed**. The surface temperatures of opaque obstacles, tree canopies, and translucent shades are provided via the T_opaque, T_tree, and T_trans inputs of the ObsSet component (single value or 8760-hourly series), falling back to air temperature $T_a$ when omitted.

> **Important Change (2026-06-15):** ObsSet input (index 2) is **no longer backward-compatible**. Only accepts ObstacleSet type data; List&lt;Brep&gt; or List&lt;Mesh&gt; are rejected. Geometric obstacles must be pre-processed through the ObsSet component.

**Ta/Tg Input Modes:**

| Mode | Data Quantity | Description |
|:---:|:---:|:---|
| 1 | 1 value | Uniform fixed temperature, all time steps and all points |
| 2 | N values (N = number of points) | Per-point fixed temperature |
| 3 | 8760 values | Hourly varying temperature (shared by all points) |
| 4 | N×8760 Tree | Per-point hourly varying temperature |

### 3.2 Output Parameters (17 items)

**MRT-related (indices 0–8):**

| Index | Parameter | Description |
|:---:|:---:|:---|
| 0 | MRT | Mean radiant temperature [°C], per point per hour (Tree, branch = point) |
| 1 | dTsw | MRT increment due to shortwave radiation [°C] |
| 2 | dTlw | Total MRT increment due to longwave radiation [°C] (sum of the five components) |
| 3 | dTlw_sky | Sky longwave component contribution [°C], $c_{\text{lw}} \cdot F_{\text{SVF}} \cdot (T_{\text{sky}} - T_{\text{ref}})$ |
| 4 | dTlw_grd | Ground longwave component contribution [°C], $c_{\text{lw}} \cdot F_{\text{GVF}} \cdot (T_g - T_{\text{ref}})$ |
| 5 | dTlw_opq | Opaque obstacle longwave component contribution [°C], $c_{\text{lw}} \cdot F_{\text{OVF,opaque}} \cdot (T_{\text{opaque}} - T_{\text{ref}})$ |
| 6 | dTlw_tree | Tree canopy longwave component contribution [°C], $c_{\text{lw}} \cdot F_{\text{TVF}} \cdot (T_{\text{canopy}} - T_{\text{ref}})$ |
| 7 | dTlw_trans | Translucent shade longwave component contribution [°C], $c_{\text{lw}} \cdot F_{\text{TRVF}} \cdot (T_{\text{trans}} - T_{\text{ref}})$ |
| 8 | HrMRT | Hourly average MRT across all points [°C] (List) |

**View factors (indices 9–13, List, one value per point):**

| Index | Parameter | Description |
|:---:|:---:|:---|
| 9 | SVF | Sky view factor [0–1], full 4π sampling |
| 10 | GVF | Ground view factor [0–1], full 4π sampling |
| 11 | OVF | Opaque obstacle view factor [0–1] (**opaque only**, full 4π sampling) |
| 12 | TVF | Tree view factor [0–1], full 4π sampling |
| 13 | TRVF | Translucent shade view factor [0–1], full 4π sampling |

**Exposure factors (indices 14–15, Tree, per point per hour):**

| Index | Parameter | Description |
|:---:|:---:|:---|
| 14 | Exp | Solar exposure factor $f_{\text{exp}}$ [0–1] (binary: exposed/shaded) |
| 15 | DNIExp | Effective DNI exposure factor $f_{\text{DNI}}$ [0–1], combining exposure and multi-obstacle transmission |

**Other (index 16):**

| Index | Parameter | Description |
|:---:|:---:|:---|
| 16 | SunVec | Solar vector for each analysis time step (List) |

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
| Obstacle Emissivity | EpsObs | - | 0.95 | Obstacle longwave emissivity (RayMan only; shared by opaque, tree canopy, and translucent surfaces) |

---

## 5. Obstacle Set Component (ObsSet.cs)

### 5.1 Description

The ObsSet component creates a classified obstacle set (`ObstacleSet`) that provides unified obstacle input for the MRT component, SpatialSoilThermalSimulator component, and RadSim component. By classifying obstacles into opaque objects, trees (with canopy transmission), and translucent sunshades, it enables fine-grained direct normal irradiance (DNI) calculation and five-component longwave radiation decomposition.

**Core Principle (2026-06-15 Corrected):**
- When a sample point is blocked by backward ray tracing, the traditional method completely ignores DNI contribution ($f_{\text{exp}}=0$ → DNI=0)
- The enhanced method computes partial DNI transmission based on obstacle type, using Beer-Lambert law for vegetation and fixed transmittance for translucent materials
- **Physical correction**: When a ray passes through multiple non-opaque obstacles, individual transmittances **multiply**, rather than taking only the nearest one
- The effective DNI exposure factor $f_{\text{DNI}}$ combines exposure and transmission effects, replacing $f_{\text{exp}}$ in direct radiation calculations

**Surface Temperature Settings (New 2026-06-16):**
- ObsSet also provides independent surface temperature inputs for the three obstacle categories (T_opaque / T_tree / T_trans), used for the five-component longwave calculation in the MRT and soil thermal modules
- Each temperature input accepts either a **single value** (fixed for the whole year) or **8760 values** (hourly series)
- When omitted, the temperature falls back to the hourly air temperature $T_a$
- The three temperature inputs are **not read by the RadSim component**; they are used solely by the MRT and soil thermal modules

### 5.2 Input Parameters

| Index | Parameter | ID | Type | Default | Description |
|:---:|:---:|:---:|:---:|:---:|:---|
| 0 | Tree Detail | TreeDet | Mesh List | — | Detailed tree geometry mesh (leaves, branches) from Tree Processor |
| 1 | Tree Canopy | TreeCan | Mesh List | — | Simplified tree canopy envelope mesh(es) for path-length calculation |
| 2 | Leaf Area Density | LAD | Number | 1.0 | Leaf area density [m²/m³], leaf area per unit canopy volume (clamped to 0.01–50) |
| 3 | Extinction Coeff | k | Number | 0.5 | Solar radiation extinction coefficient [-], Beer-Lambert parameter, typical 0.5–0.8 (clamped to 0.01–1.0) |
| 4 | Tree Temperature | T_tree | Number List | — | Tree canopy surface temperature [°C], single value or 8760 hourly values (optional; falls back to $T_a$) |
| 5 | Translucent Shade | TransShade | Mesh List | — | Translucent sunshade / shading device mesh(es) |
| 6 | Transmittance | τ | Number | 0.05 | Shortwave transmittance of translucent sunshades [-], range 0.0–1.0 |
| 7 | Translucent Temperature | T_trans | Number List | — | Translucent shade surface temperature [°C], single value or 8760 hourly values (optional; falls back to $T_a$) |
| 8 | Opaque Objects | Opaque | Mesh List | — | Opaque obstacles (buildings, walls) that fully block direct radiation |
| 9 | Opaque Temperature | T_opaque | Number List | — | Opaque obstacle surface temperature [°C], single value or 8760 hourly values (optional; falls back to $T_a$) |

All 10 inputs are optional.

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
| 0 | ObsSet | Classified obstacle set (with per-category surface temperatures), connect to MRT/SpSoilSim/RadSim component's ObsSet input |

### 5.4 Usage Notes

1. **Tree Detail vs Tree Canopy**: Tree Detail is used for ray-hit detection (determining if a point is under tree shade) **and SVF view-factor occlusion judgment**; Tree Canopy is **only** used for Beer-Lambert path-length calculation (geometric distance $s$ the ray travels through the canopy) and **does not participate in SVF occlusion**. SVF calculation uses only Opaque + TreeDetail + TranslucentShade for physical occlusion — TreeCanopy is a simplified shrinkwrap envelope and should not be used as an occluding body. Both are required; if Tree Detail is provided without Tree Canopy, DNI transmission uses zero path length.

2. **Strict Type Requirement (2026-06-15)**: The ObsSet inputs of MRT, SpSoilSim, and RadSim components **only accept ObstacleSet type data**, no longer backward-compatible with List&lt;Brep&gt; or List&lt;Mesh&gt; direct input. Geometric obstacles must be pre-processed through the ObsSet component.

3. **Mesh Input**: The ObsSet component itself supports direct Mesh type input. If Surface or Brep types are provided, Grasshopper can implicitly convert them to Mesh.

4. **Temperature Input Modes**: T_opaque / T_tree / T_trans accept ≥8760 values as an hourly series; a single value is applied year-round; when omitted, downstream components fall back to the hourly air temperature.

---

## 6. References

1. ASHRAE Standard 55-2017: Thermal Environmental Conditions for Human Occupancy. American Society of Heating, Refrigerating and Air-Conditioning Engineers, Atlanta, GA, 2017.

2. Arens, E., et al. (2015). Modeling the comfort effects of short-wave solar radiation indoors. *Building and Environment*, 88, 3-9. https://doi.org/10.1016/j.buildenv.2014.09.004

3. Matzarakis, A., Rutz, F., & Mayer, H. (2010). Modelling radiation fluxes in simple and complex environments: basics of the RayMan model. *International Journal of Biometeorology*, 54, 131-139. https://doi.org/10.1007/s00484-009-0261-5

4. ISO 7726:1998. Ergonomics of the thermal environment — Instruments for measuring physical quantities. International Organization for Standardization, Geneva, 1998.
