using System;
using System.Collections.Generic;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using Common.Core;

namespace ThermalComfort.Core
{
    /// <summary>
    /// Human exposure factor calculation using reverse ray tracing.
    /// Enhanced with full-spherical (4pi) view factor decomposition for MRT longwave radiation.
    /// 
    /// The exposure factor (f_exp) represents the fraction of the body directly exposed
    /// to solar radiation, accounting for building/vegetation shading.
    /// </summary>
    public static class HumanExposureModel
    {
        /// <summary>
        /// Calculate solar exposure factor using reverse ray tracing.
        /// </summary>
        public static double CalculateExposureFactor(
            Point3d analysisPoint,
            Vector3d sunVec,
            List<Mesh> obstacleMeshes,
            double bodyHeight = 1.7,
            double analysisHeight = 1.5,
            int samplePointCount = 3,
            double maxRayDistance = 500.0,
            double rayOffset = 0)
        {
            if (obstacleMeshes == null || obstacleMeshes.Count == 0)
                return 1.0;

            if (samplePointCount < 1)
                samplePointCount = 1;

            Vector3d sunDir = sunVec;
            if (sunDir.Length < 0.001)
                return 1.0;
            sunDir.Unitize();

            if (sunDir.Z < 0.001)
                return 0.0;

            int exposedPoints = 0;
            int totalPoints = 0;
            double groundZ = analysisPoint.Z;

            for (int i = 0; i < samplePointCount; i++)
            {
                double t = samplePointCount > 1 ? (double)i / (samplePointCount - 1) : 0.5;
                double height = t * bodyHeight;
                Point3d samplePoint = new Point3d(analysisPoint.X, analysisPoint.Y, groundZ + height);
                Point3d rayOrigin = samplePoint;
                Ray3d ray = new Ray3d(rayOrigin, sunDir);

                bool isShaded = false;
                foreach (var mesh in obstacleMeshes)
                {
                    if (mesh == null || mesh.Faces.Count == 0) continue;
                    double tHit = Intersection.MeshRay(mesh, ray);
                    if (tHit > 0.001 && tHit < maxRayDistance)
                    {
                        isShaded = true;
                        break;
                    }
                }

                if (!isShaded) exposedPoints++;
                totalPoints++;
            }

            return totalPoints > 0 ? (double)exposedPoints / totalPoints : 1.0;
        }

        /// <summary>
        /// Batch exposure factor calculation.
        /// </summary>
        public static double[] CalculateExposureFactorsBatch(
            List<Point3d> analysisPoints,
            Vector3d sunVec,
            List<Mesh> obstacleMeshes,
            double bodyHeight = 1.7,
            double analysisHeight = 1.5,
            int samplePointCount = 3,
            double maxRayDistance = 500.0,
            double rayOffset = 0)
        {
            int n = analysisPoints.Count;
            double[] results = new double[n];

            System.Threading.Tasks.Parallel.For(0, n, i =>
            {
                results[i] = CalculateExposureFactor(
                    analysisPoints[i], sunVec, obstacleMeshes,
                    bodyHeight, analysisHeight, samplePointCount,
                    maxRayDistance, rayOffset);
            });

            return results;
        }

        // ========================================================================
        // ENHANCED: DNI exposure factor with obstacle transmission support
        // ========================================================================

        /// <summary>
        /// Calculate the geometric path length s [m] through vegetation canopy meshes
        /// along a given ray direction. Uses MeshLine to find all entry/exit intersections.
        ///
        /// This is the key input for Beer-Lambert law:
        ///   I_transmitted = I_DN * exp(-k * LAD * s)
        ///
        /// where s is the total distance between the first (entry) and last (exit)
        /// intersection points with the canopy envelope meshes.
        /// </summary>
        /// <param name="rayOrigin">Ray starting point</param>
        /// <param name="rayDir">Normalized ray direction</param>
        /// <param name="canopyMeshes">Simplified canopy envelope meshes</param>
        /// <param name="maxRayDistance">Maximum ray tracing distance [m]</param>
        /// <returns>Path length s [m] through canopy. 0 if no intersection.</returns>
        public static double CalculateCanopyPathLength(
            Point3d rayOrigin,
            Vector3d rayDir,
            List<Mesh> canopyMeshes,
            double maxRayDistance = 500.0)
        {
            if (canopyMeshes == null || canopyMeshes.Count == 0)
                return 0.0;

            double minT = double.MaxValue;
            double maxT = double.MinValue;
            bool hasHit = false;

            Line rayLine = new Line(rayOrigin, rayOrigin + rayDir * maxRayDistance);

            foreach (var mesh in canopyMeshes)
            {
                if (mesh == null || mesh.Faces.Count == 0) continue;

                // MeshLine returns Point3d[] — all intersection points between the line and mesh
                Point3d[] intersections = Intersection.MeshLine(mesh, rayLine);
                if (intersections != null && intersections.Length > 0)
                {
                    foreach (Point3d intersection in intersections)
                    {
                        // Calculate parametric distance t from rayOrigin to intersection
                        // t = (intersection - rayOrigin) · rayDir (rayDir is unitized)
                        double t = (intersection - rayOrigin) * rayDir;
                        if (t > 0.001 && t < maxRayDistance)
                        {
                            if (t < minT) minT = t;
                            if (t > maxT) maxT = t;
                            hasHit = true;
                        }
                    }
                }
            }

            if (!hasHit)
                return 0.0;

            // Path length = exit point - entry point
            double pathLength = maxT - minT;
            return Math.Max(0.0, pathLength);
        }

        /// <summary>
        /// Calculate the cumulative DNI transmission through all non-opaque obstacles
        /// along a ray path.
        ///
        /// PHYSICALLY CORRECTED (2026-06-15):
        /// When a ray intersects multiple non-opaque obstacles, the DNI transmission
        /// is the PRODUCT of individual transmissions, not just the nearest one from
        /// the sun direction.
        ///
        /// Algorithm:
        ///   1. Check all Opaque meshes — any hit → transmission = 0 (full block)
        ///   2. Check TreeDetail meshes — any hit → apply Beer-Lambert canopy transmission
        ///   3. Check TranslucentShade meshes — each hit → apply fixed transmittance (multiplied)
        ///   4. Return product of all non-opaque transmissions (1.0 if no hits)
        ///
        /// For multiple tree hits, the total canopy path length is used (additive in exponent).
        /// For multiple translucent hits, transmittance is multiplied for each intersected mesh.
        /// For mixed tree + translucent, the product of both contributions is applied.
        /// </summary>
        /// <param name="ray">Ray to test (from sample point toward sun)</param>
        /// <param name="obstacleSet">Classified obstacle set</param>
        /// <param name="maxRayDistance">Maximum ray distance</param>
        /// <returns>Cumulative DNI transmission factor [0, 1]. 0 = fully blocked, 1 = no obstruction.</returns>
        public static double CalculateRayDNITransmission(
            Ray3d ray,
            ObstacleSet obstacleSet,
            double maxRayDistance)
        {
            if (obstacleSet == null || !obstacleSet.HasAnyObstacles)
                return 1.0;

            // ========================================================================
            // STEP 1: Opaque objects — ABSOLUTE PRIORITY
            // If any opaque mesh is on the ray path, it fully blocks direct radiation.
            // All tree/translucent obstacles behind an opaque object are in shadow.
            // ========================================================================
            if (obstacleSet.OpaqueObjectMeshes != null)
            {
                foreach (var mesh in obstacleSet.OpaqueObjectMeshes)
                {
                    if (mesh == null || mesh.Faces.Count == 0) continue;
                    double tHit = Intersection.MeshRay(mesh, ray);
                    if (tHit > 0.001 && tHit < maxRayDistance)
                        return 0.0;
                }
            }

            // ========================================================================
            // STEP 2: Calculate cumulative non-opaque transmission as PRODUCT
            // When multiple non-opaque obstacles intersect the ray, their individual
            // DNI contributions multiply (not just taking the nearest one).
            // ========================================================================
            double transmission = 1.0;

            // --- Tree detail hits → Beer-Lambert canopy transmission ---
            // Any hit on TreeDetailMeshes triggers Beer-Lambert attenuation using
            // the total geometric path length through all TreeCanopyMeshes.
            if (obstacleSet.TreeDetailMeshes != null && obstacleSet.TreeDetailMeshes.Count > 0)
            {
                bool hasTreeHit = false;
                foreach (var mesh in obstacleSet.TreeDetailMeshes)
                {
                    if (mesh == null || mesh.Faces.Count == 0) continue;
                    double tHit = Intersection.MeshRay(mesh, ray);
                    if (tHit > 0.001 && tHit < maxRayDistance)
                    {
                        hasTreeHit = true;
                        break; // At least one tree hit triggers canopy attenuation
                    }
                }

                if (hasTreeHit)
                {
                    double pathLength = CalculateCanopyPathLength(
                        ray.Position, ray.Direction,
                        obstacleSet.TreeCanopyMeshes, maxRayDistance);
                    double treeTransmission = Math.Exp(
                        -obstacleSet.ExtinctionCoefficient * obstacleSet.LeafAreaDensity * pathLength);
                    transmission *= treeTransmission;
                }
            }

            // --- Translucent shade hits → fixed transmittance per hit ---
            // Each intersected translucent shade mesh contributes its own transmittance factor.
            // Multiple translucent hits multiply (e.g., two shades with τ=0.5 → total τ=0.25).
            if (obstacleSet.TranslucentShadeMeshes != null && obstacleSet.TranslucentShadeMeshes.Count > 0)
            {
                foreach (var mesh in obstacleSet.TranslucentShadeMeshes)
                {
                    if (mesh == null || mesh.Faces.Count == 0) continue;
                    double tHit = Intersection.MeshRay(mesh, ray);
                    if (tHit > 0.001 && tHit < maxRayDistance)
                    {
                        transmission *= obstacleSet.TranslucentTransmittance;
                    }
                }
            }

            return Math.Max(0.0, Math.Min(1.0, transmission));
        }

        /// <summary>
        /// [LEGACY] Classify which type of obstacle blocks sunlight from reaching the sample point.
        /// 
        /// DEPRECATED (2026-06-15): This method is kept for backward compatibility of external callers.
        /// For DNI transmission calculation, use <see cref="CalculateRayDNITransmission"/> instead,
        /// which correctly handles multiple non-opaque obstacles by multiplying their individual
        /// transmission factors.
        /// 
        /// When this method reports Tree or Translucent, the actual DNI contribution may be further
        /// attenuated by other non-opaque obstacles intersected by the same ray.
        /// </summary>
        [Obsolete("Use CalculateRayDNITransmission for physically correct multi-obstacle DNI calculation.")]
        public static ObstacleType ClassifyRayHit(
            Ray3d ray,
            ObstacleSet obstacleSet,
            double maxRayDistance,
            out double hitDistance)
        {
            hitDistance = double.MaxValue;

            if (obstacleSet == null)
                return ObstacleType.None;

            // ========================================================================
            // STEP 1: Opaque objects — ABSOLUTE PRIORITY
            // ========================================================================
            if (obstacleSet.OpaqueObjectMeshes != null)
            {
                foreach (var mesh in obstacleSet.OpaqueObjectMeshes)
                {
                    if (mesh == null || mesh.Faces.Count == 0) continue;
                    double tHit = Intersection.MeshRay(mesh, ray);
                    if (tHit > 0.001 && tHit < maxRayDistance)
                    {
                        hitDistance = tHit;
                        return ObstacleType.Opaque;
                    }
                }
            }

            // ========================================================================
            // STEP 2: Find the farthest Tree/Translucent hit (nearest from sun)
            // ========================================================================
            ObstacleType hitType = ObstacleType.None;
            double farthestHit = -1.0;

            if (obstacleSet.TreeDetailMeshes != null)
            {
                foreach (var mesh in obstacleSet.TreeDetailMeshes)
                {
                    if (mesh == null || mesh.Faces.Count == 0) continue;
                    double tHit = Intersection.MeshRay(mesh, ray);
                    if (tHit > 0.001 && tHit < maxRayDistance && tHit > farthestHit)
                    {
                        farthestHit = tHit;
                        hitType = ObstacleType.Tree;
                    }
                }
            }

            if (obstacleSet.TranslucentShadeMeshes != null)
            {
                foreach (var mesh in obstacleSet.TranslucentShadeMeshes)
                {
                    if (mesh == null || mesh.Faces.Count == 0) continue;
                    double tHit = Intersection.MeshRay(mesh, ray);
                    if (tHit > 0.001 && tHit < maxRayDistance && tHit > farthestHit)
                    {
                        farthestHit = tHit;
                        hitType = ObstacleType.Translucent;
                    }
                }
            }

            if (hitType != ObstacleType.None)
                hitDistance = farthestHit;

            return hitType;
        }

        /// <summary>
        /// Calculate the effective DNI exposure factor considering obstacle transmission.
        ///
        /// This extends the traditional exposure factor (f_exp) by accounting for
        /// partial transmission of direct radiation through vegetation (Beer-Lambert)
        /// and translucent materials (fixed transmittance).
        ///
        /// ALGORITHM:
        /// For each sample point along body height:
        ///   - Use CalculateRayDNITransmission to compute cumulative transmission
        ///     through ALL non-opaque obstacles (product of individual transmissions)
        ///
        /// Returns the average contribution across all sample points.
        ///
        /// This value replaces exposureFactor in direct radiation calculations:
        ///   I_direct = dniExposureFactor * f_p(gamma) * I_DN
        /// </summary>
        /// <param name="analysisPoint">Ground analysis point</param>
        /// <param name="sunVec">Sun direction vector</param>
        /// <param name="obstacleSet">Classified obstacle set (null = no obstacles)</param>
        /// <param name="bodyHeight">Total body height [m]</param>
        /// <param name="samplePointCount">Number of vertical sample points</param>
        /// <param name="maxRayDistance">Maximum ray distance [m]</param>
        /// <returns>Effective DNI exposure factor [0, 1]</returns>
        public static double CalculateDNIExposureFactor(
            Point3d analysisPoint,
            Vector3d sunVec,
            ObstacleSet obstacleSet,
            double bodyHeight = 1.7,
            int samplePointCount = 3,
            double maxRayDistance = 500.0)
        {
            if (obstacleSet == null || !obstacleSet.HasAnyObstacles)
                return 1.0;

            if (samplePointCount < 1)
                samplePointCount = 1;

            Vector3d sunDir = sunVec;
            if (sunDir.Length < 0.001)
                return 1.0;
            sunDir.Unitize();

            if (sunDir.Z < 0.001)
                return 0.0;

            double totalContribution = 0.0;
            double groundZ = analysisPoint.Z;

            for (int i = 0; i < samplePointCount; i++)
            {
                double t = samplePointCount > 1 ? (double)i / (samplePointCount - 1) : 0.5;
                double height = t * bodyHeight;
                Point3d samplePoint = new Point3d(analysisPoint.X, analysisPoint.Y, groundZ + height);
                Ray3d ray = new Ray3d(samplePoint, sunDir);

                // ENHANCED (2026-06-15): Use cumulative transmission product through ALL obstacles
                double contribution = CalculateRayDNITransmission(ray, obstacleSet, maxRayDistance);

                totalContribution += contribution;
            }

            return samplePointCount > 0 ? totalContribution / samplePointCount : 1.0;
        }

        /// <summary>
        /// Calculate both the traditional exposure factor AND the DNI exposure factor
        /// with transmission support in a single pass.
        ///
        /// The traditional exposure factor (f_exp) represents the fraction of the body
        /// directly exposed to solar radiation (binary: exposed or shaded).
        ///
        /// The DNI exposure factor (f_dni) accounts for partial transmission through
        /// vegetation and translucent materials. When multiple non-opaque obstacles
        /// intersect the same ray, their individual transmission factors multiply.
        ///
        /// Both values are returned for use in different parts of MRT calculation:
        /// - f_exp: used for binary shading analysis
        /// - f_dni: used for direct radiation component in MRT (physically correct)
        /// </summary>
        public static void CalculateExposureFactorsWithTransmission(
            Point3d analysisPoint,
            Vector3d sunVec,
            ObstacleSet obstacleSet,
            out double exposureFactor,
            out double dniExposureFactor,
            double bodyHeight = 1.7,
            int samplePointCount = 3,
            double maxRayDistance = 500.0)
        {
            exposureFactor = 1.0;
            dniExposureFactor = 1.0;

            if (obstacleSet == null || !obstacleSet.HasAnyObstacles)
                return;

            if (samplePointCount < 1)
                samplePointCount = 1;

            Vector3d sunDir = sunVec;
            if (sunDir.Length < 0.001)
                return;
            sunDir.Unitize();

            if (sunDir.Z < 0.001)
            {
                exposureFactor = 0.0;
                dniExposureFactor = 0.0;
                return;
            }

            int exposedCount = 0;
            double totalDNIContribution = 0.0;
            double groundZ = analysisPoint.Z;

            for (int i = 0; i < samplePointCount; i++)
            {
                double t = samplePointCount > 1 ? (double)i / (samplePointCount - 1) : 0.5;
                double height = t * bodyHeight;
                Point3d samplePoint = new Point3d(analysisPoint.X, analysisPoint.Y, groundZ + height);
                Ray3d ray = new Ray3d(samplePoint, sunDir);

                // ENHANCED (2026-06-15): Use cumulative transmission product
                double contribution = CalculateRayDNITransmission(ray, obstacleSet, maxRayDistance);

                if (contribution >= 0.999) // Fully exposed (within tolerance)
                    exposedCount++;

                totalDNIContribution += contribution;
            }

            exposureFactor = (double)samplePointCount > 0 ? (double)exposedCount / samplePointCount : 1.0;
            dniExposureFactor = (double)samplePointCount > 0 ? totalDNIContribution / samplePointCount : 1.0;
        }

        /// <summary>
        /// Batch calculation of DNI exposure factor with transmission support.
        /// </summary>
        public static double[] CalculateDNIExposureFactorsBatch(
            List<Point3d> analysisPoints,
            Vector3d sunVec,
            ObstacleSet obstacleSet,
            double bodyHeight = 1.7,
            int samplePointCount = 3,
            double maxRayDistance = 500.0)
        {
            int n = analysisPoints.Count;
            double[] results = new double[n];

            if (obstacleSet == null || !obstacleSet.HasAnyObstacles)
            {
                for (int i = 0; i < n; i++) results[i] = 1.0;
                return results;
            }

            System.Threading.Tasks.Parallel.For(0, n, i =>
            {
                results[i] = CalculateDNIExposureFactor(
                    analysisPoints[i], sunVec, obstacleSet,
                    bodyHeight, samplePointCount, maxRayDistance);
            });

            return results;
        }

        /// <summary>
        /// Batch calculation of both exposure factor and DNI exposure factor.
        /// Returns two pre-allocated arrays (exposureFactors and dniExposureFactors).
        /// </summary>
        public static void CalculateExposureFactorsWithTransmissionBatch(
            List<Point3d> analysisPoints,
            Vector3d sunVec,
            ObstacleSet obstacleSet,
            double[] exposureFactors,
            double[] dniExposureFactors,
            double bodyHeight = 1.7,
            int samplePointCount = 3,
            double maxRayDistance = 500.0)
        {
            int n = analysisPoints.Count;

            if (exposureFactors == null || exposureFactors.Length < n)
                throw new ArgumentException("exposureFactors must be pre-allocated with length >= analysisPoints.Count");
            if (dniExposureFactors == null || dniExposureFactors.Length < n)
                throw new ArgumentException("dniExposureFactors must be pre-allocated with length >= analysisPoints.Count");

            if (obstacleSet == null || !obstacleSet.HasAnyObstacles)
            {
                for (int i = 0; i < n; i++)
                {
                    exposureFactors[i] = 1.0;
                    dniExposureFactors[i] = 1.0;
                }
                return;
            }

            System.Threading.Tasks.Parallel.For(0, n, i =>
            {
                CalculateExposureFactorsWithTransmission(
                    analysisPoints[i], sunVec, obstacleSet,
                    out exposureFactors[i], out dniExposureFactors[i],
                    bodyHeight, samplePointCount, maxRayDistance);
            });
        }

        /// <summary>
        /// Sky view factor (upper hemisphere only).
        /// For full-spherical view factors, use CalculateSphericalViewFactors.
        /// </summary>
        public static double CalculateSkyViewFactorForPoint(
            Point3d analysisPoint,
            List<Mesh> obstacleMeshes,
            double analysisHeight = 1.5,
            int sampleCount = 500,
            double maxRayDistance = 500.0)
        {
            Point3d eyePoint = new Point3d(
                analysisPoint.X, analysisPoint.Y, analysisPoint.Z + analysisHeight);

            var directions = SolarGeometry.GenerateHemisphereDirections(sampleCount);

            if (obstacleMeshes == null || obstacleMeshes.Count == 0)
                return 1.0;

            int visibleRays = 0;
            int totalRays = directions.Count;

            foreach (var dir in directions)
            {
                if (dir.Z < 0.001) continue;

                Point3d rayOrigin = eyePoint + dir * 0.01;
                Ray3d ray = new Ray3d(rayOrigin, dir);

                bool isBlocked = false;
                foreach (var mesh in obstacleMeshes)
                {
                    if (mesh == null || mesh.Faces.Count == 0) continue;
                    double tHit = Intersection.MeshRay(mesh, ray);
                    if (tHit > 0.001 && tHit < maxRayDistance)
                    {
                        isBlocked = true;
                        break;
                    }
                }

                if (!isBlocked) visibleRays++;
            }

            return totalRays > 0 ? (double)visibleRays / totalRays : 1.0;
        }

        // ========================================================================
        // ENHANCED (2026-06-16): Decomposed view factors for diffuse radiation
        // ========================================================================

        /// <summary>
        /// Calculate the characteristic canopy thickness l [m] from TreeCanopyMeshes.
        /// Uses the Z-axis extent of the combined bounding box of all canopy meshes.
        /// This value is used in the Beer-Lambert term for tree canopy diffuse radiation:
        ///   DHI_tree = TVF * DHI * exp(-k_c * LAD * l)
        /// </summary>
        /// <param name="treeCanopyMeshes">Simplified tree canopy envelope meshes</param>
        /// <returns>Characteristic canopy thickness l [m]. 0 if no canopy meshes.</returns>
        public static double CalculateCanopyCharacteristicThickness(
            List<Mesh> treeCanopyMeshes)
        {
            if (treeCanopyMeshes == null || treeCanopyMeshes.Count == 0)
                return 0.0;

            var bbox = new BoundingBox();
            foreach (var mesh in treeCanopyMeshes)
            {
                if (mesh != null && mesh.IsValid && mesh.Faces.Count > 0)
                    bbox.Union(mesh.GetBoundingBox(false));
            }
            if (!bbox.IsValid)
                return 0.0;

            return Math.Max(0.0, bbox.Max.Z - bbox.Min.Z);
        }

        /// <summary>
        /// Calculate decomposed view factors for the upper hemisphere (sky dome).
        /// Decomposes the hemisphere into four mutually exclusive components:
        /// - SVF: visible sky (no obstruction)
        /// - TVF: tree canopy view factor (blocked by tree detail meshes)
        /// - TRVF: translucent view factor (blocked by translucent shade meshes)
        /// - OVF_opaque: opaque obstacle view factor (blocked by opaque objects only)
        ///
        /// PHYSICAL RATIONALE (2026-06-16):
        /// For diffuse radiation calculation, each hemisphere direction is classified
        /// by the nearest obstacle type encountered. Priority when overlapping:
        /// Opaque &gt; TreeDetail &gt; TranslucentShade.
        /// This ensures that directions blocked by both trees and buildings are
        /// assigned to the opaque category (the building occludes the tree).
        ///
        /// Conservation: SVF + TVF + TRVF + OVF_opaque = 1.0
        ///
        /// These decomposed view factors enable precise diffuse radiation calculation:
        /// DHI_eff = SVF*DHI + TVF*DHI*exp(-k_c*LAD*l) + TRVF*DHI*tau
        /// </summary>
        public static void CalculateDecomposedViewFactors(
            Point3d analysisPoint,
            ObstacleSet obstacleSet,
            out double svf, out double tvf, out double trvf, out double ovfOpaque,
            double analysisHeight = 1.5,
            int sampleCount = 500,
            double maxRayDistance = 500.0)
        {
            // Default values (no obstacles)
            svf = 1.0;
            tvf = 0.0;
            trvf = 0.0;
            ovfOpaque = 0.0;

            if (obstacleSet == null || !obstacleSet.HasAnyObstacles)
                return;

            Point3d eyePoint = new Point3d(
                analysisPoint.X, analysisPoint.Y, analysisPoint.Z + analysisHeight);

            var directions = SolarGeometry.GenerateHemisphereDirections(sampleCount);

            int skyRays = 0;
            int treeRays = 0;
            int translucentRays = 0;
            int opaqueRays = 0;
            int totalRays = directions.Count;

            foreach (var dir in directions)
            {
                if (dir.Z < 0.001) continue;

                Point3d rayOrigin = eyePoint + dir * 0.01;
                Ray3d ray = new Ray3d(rayOrigin, dir);

                // Priority: Opaque &gt; TreeDetail &gt; TranslucentShade
                bool classified = false;

                // STEP 1: Check opaque objects (absolute priority)
                if (!classified && obstacleSet.OpaqueObjectMeshes != null)
                {
                    foreach (var mesh in obstacleSet.OpaqueObjectMeshes)
                    {
                        if (mesh == null || mesh.Faces.Count == 0) continue;
                        double tHit = Intersection.MeshRay(mesh, ray);
                        if (tHit > 0.001 && tHit < maxRayDistance)
                        {
                            opaqueRays++;
                            classified = true;
                            break;
                        }
                    }
                }

                // STEP 2: Check tree detail meshes
                if (!classified && obstacleSet.TreeDetailMeshes != null)
                {
                    foreach (var mesh in obstacleSet.TreeDetailMeshes)
                    {
                        if (mesh == null || mesh.Faces.Count == 0) continue;
                        double tHit = Intersection.MeshRay(mesh, ray);
                        if (tHit > 0.001 && tHit < maxRayDistance)
                        {
                            treeRays++;
                            classified = true;
                            break;
                        }
                    }
                }

                // STEP 3: Check translucent shade meshes
                if (!classified && obstacleSet.TranslucentShadeMeshes != null)
                {
                    foreach (var mesh in obstacleSet.TranslucentShadeMeshes)
                    {
                        if (mesh == null || mesh.Faces.Count == 0) continue;
                        double tHit = Intersection.MeshRay(mesh, ray);
                        if (tHit > 0.001 && tHit < maxRayDistance)
                        {
                            translucentRays++;
                            classified = true;
                            break;
                        }
                    }
                }

                // STEP 4: Unoccluded → sky
                if (!classified)
                    skyRays++;
            }

            if (totalRays > 0)
            {
                svf = (double)skyRays / totalRays;
                tvf = (double)treeRays / totalRays;
                trvf = (double)translucentRays / totalRays;
                ovfOpaque = (double)opaqueRays / totalRays;
            }
        }

        /// <summary>
        /// Batch calculation of decomposed view factors (parallel).
        /// Results are written into pre-allocated arrays.
        /// </summary>
        public static void CalculateDecomposedViewFactorsBatch(
            List<Point3d> analysisPoints,
            ObstacleSet obstacleSet,
            double[] svfResults,
            double[] tvfResults,
            double[] trvfResults,
            double[] ovfOpaqueResults,
            double analysisHeight = 1.5,
            int sampleCount = 500,
            double maxRayDistance = 500.0)
        {
            int n = analysisPoints.Count;

            if (svfResults == null || svfResults.Length < n)
                throw new ArgumentException("svfResults must be pre-allocated with length >= analysisPoints.Count");
            if (tvfResults == null || tvfResults.Length < n)
                throw new ArgumentException("tvfResults must be pre-allocated with length >= analysisPoints.Count");
            if (trvfResults == null || trvfResults.Length < n)
                throw new ArgumentException("trvfResults must be pre-allocated with length >= analysisPoints.Count");
            if (ovfOpaqueResults == null || ovfOpaqueResults.Length < n)
                throw new ArgumentException("ovfOpaqueResults must be pre-allocated with length >= analysisPoints.Count");

            if (obstacleSet == null || !obstacleSet.HasAnyObstacles)
            {
                for (int i = 0; i < n; i++)
                {
                    svfResults[i] = 1.0;
                    tvfResults[i] = 0.0;
                    trvfResults[i] = 0.0;
                    ovfOpaqueResults[i] = 0.0;
                }
                return;
            }

            System.Threading.Tasks.Parallel.For(0, n, i =>
            {
                double s, t, tr, o;
                CalculateDecomposedViewFactors(
                    analysisPoints[i], obstacleSet,
                    out s, out t, out tr, out o,
                    analysisHeight, sampleCount, maxRayDistance);
                svfResults[i] = s;
                tvfResults[i] = t;
                trvfResults[i] = tr;
                ovfOpaqueResults[i] = o;
            });
        }

        // ========================================================================
        // NEW: Full-spherical (4pi) view factor decomposition for MRT
        // ========================================================================

        /// <summary>
        /// [ENHANCED] Calculate full-spherical (4pi) view factors with ObstacleSet.
        /// Decomposes the entire sphere into FIVE components:
        ///   - SVF: Sky View Factor (visible sky, Z &gt; 0, no obstacle)
        ///   - GVF: Ground View Factor (visible ground, Z &lt; 0, no obstacle)
        ///   - OVF_opaque: Opaque Obstacle View Factor (blocked by opaque objects only)
        ///   - TVF: Tree View Factor (blocked by tree detail meshes, no opaque overlap)
        ///   - TRVF: Translucent View Factor (blocked by translucent shade meshes)
        /// Conservation: SVF + GVF + OVF_opaque + TVF + TRVF = 1.0
        ///
        /// ENHANCED (2026-06-16): OVF is now decomposed into OVF_opaque only.
        /// TVF and TRVF are separated for precise diffuse radiation calculation.
        /// Priority for overlapping obstacles: Opaque &gt; TreeDetail &gt; TranslucentShade.
        /// </summary>
        public static void CalculateSphericalViewFactors(
            Point3d analysisPoint,
            ObstacleSet obstacleSet,
            out double svf, out double gvf, out double ovfOpaque, out double tvf, out double trvf,
            double analysisHeight = 1.5,
            int sampleCount = 1000,
            double maxRayDistance = 500.0)
        {
            svf = 0.5;
            gvf = 0.5;
            ovfOpaque = 0.0;
            tvf = 0.0;
            trvf = 0.0;

            Point3d eyePoint = new Point3d(
                analysisPoint.X, analysisPoint.Y, analysisPoint.Z + analysisHeight);

            var directions = SolarGeometry.GenerateSphereDirections(sampleCount);

            if (obstacleSet == null || !obstacleSet.HasAnyObstacles)
                return;

            int skyRays = 0;
            int groundRays = 0;
            int opaqueRays = 0;
            int treeRays = 0;
            int translucentRays = 0;
            int totalRays = 0;

            foreach (var dir in directions)
            {
                Point3d rayOrigin = eyePoint + dir * 0.01;
                Ray3d ray = new Ray3d(rayOrigin, dir);

                // Priority: Opaque &gt; TreeDetail &gt; TranslucentShade
                bool classified = false;

                // STEP 1: Check opaque objects (absolute priority)
                if (obstacleSet.OpaqueObjectMeshes != null)
                {
                    foreach (var mesh in obstacleSet.OpaqueObjectMeshes)
                    {
                        if (mesh == null || mesh.Faces.Count == 0) continue;
                        double tHit = Intersection.MeshRay(mesh, ray);
                        if (tHit > 0.001 && tHit < maxRayDistance)
                        {
                            opaqueRays++;
                            classified = true;
                            break;
                        }
                    }
                }

                // STEP 2: Check tree detail meshes
                if (!classified && obstacleSet.TreeDetailMeshes != null)
                {
                    foreach (var mesh in obstacleSet.TreeDetailMeshes)
                    {
                        if (mesh == null || mesh.Faces.Count == 0) continue;
                        double tHit = Intersection.MeshRay(mesh, ray);
                        if (tHit > 0.001 && tHit < maxRayDistance)
                        {
                            treeRays++;
                            classified = true;
                            break;
                        }
                    }
                }

                // STEP 3: Check translucent shade meshes
                if (!classified && obstacleSet.TranslucentShadeMeshes != null)
                {
                    foreach (var mesh in obstacleSet.TranslucentShadeMeshes)
                    {
                        if (mesh == null || mesh.Faces.Count == 0) continue;
                        double tHit = Intersection.MeshRay(mesh, ray);
                        if (tHit > 0.001 && tHit < maxRayDistance)
                        {
                            translucentRays++;
                            classified = true;
                            break;
                        }
                    }
                }

                totalRays++;
                if (classified)
                    continue; // Already counted in obstacle category
                else if (dir.Z > 0.001)
                    skyRays++;
                else if (dir.Z < -0.001)
                    groundRays++;
            }

            if (totalRays > 0)
            {
                svf = (double)skyRays / totalRays;
                gvf = (double)groundRays / totalRays;
                ovfOpaque = (double)opaqueRays / totalRays;
                tvf = (double)treeRays / totalRays;
                trvf = (double)translucentRays / totalRays;
            }
        }

        /// <summary>
        /// Batch full-spherical view factor calculation with ObstacleSet (parallel, 5-component).
        /// ENHANCED (2026-06-16): Returns decomposed OVF_opaque, TVF, TRVF separately.
        /// </summary>
        public static void CalculateSphericalViewFactorsBatch(
            List<Point3d> analysisPoints,
            ObstacleSet obstacleSet,
            double[] svfResults,
            double[] gvfResults,
            double[] ovfOpaqueResults,
            double[] tvfResults,
            double[] trvfResults,
            double analysisHeight = 1.5,
            int sampleCount = 1000,
            double maxRayDistance = 500.0)
        {
            int n = analysisPoints.Count;

            if (svfResults == null || svfResults.Length < n) throw new ArgumentException("svfResults must be pre-allocated");
            if (gvfResults == null || gvfResults.Length < n) throw new ArgumentException("gvfResults must be pre-allocated");
            if (ovfOpaqueResults == null || ovfOpaqueResults.Length < n) throw new ArgumentException("ovfOpaqueResults must be pre-allocated");
            if (tvfResults == null || tvfResults.Length < n) throw new ArgumentException("tvfResults must be pre-allocated");
            if (trvfResults == null || trvfResults.Length < n) throw new ArgumentException("trvfResults must be pre-allocated");

            if (obstacleSet == null || !obstacleSet.HasAnyObstacles)
            {
                for (int i = 0; i < n; i++)
                {
                    svfResults[i] = 0.5;
                    gvfResults[i] = 0.5;
                    ovfOpaqueResults[i] = 0.0;
                    tvfResults[i] = 0.0;
                    trvfResults[i] = 0.0;
                }
                return;
            }

            System.Threading.Tasks.Parallel.For(0, n, i =>
            {
                double s, g, o, t, tr;
                CalculateSphericalViewFactors(
                    analysisPoints[i], obstacleSet,
                    out s, out g, out o, out t, out tr,
                    analysisHeight, sampleCount, maxRayDistance);
                svfResults[i] = s;
                gvfResults[i] = g;
                ovfOpaqueResults[i] = o;
                tvfResults[i] = t;
                trvfResults[i] = tr;
            });
        }

        /// <summary>
        /// [LEGACY] Calculate full-spherical (4pi) view factors using flat mesh list.
        /// OVF includes ALL obstacle types (opaque + tree + translucent combined).
        /// For decomposed view factors with separate TVF/TRVF, use the ObstacleSet overload.
        ///
        /// Decomposes the entire sphere into three components:
        ///   - SVF: Sky View Factor (visible sky)
        ///   - GVF: Ground View Factor (visible ground)
        ///   - OVF: Obstacle View Factor (blocked by any obstacles)
        /// Conservation: SVF + GVF + OVF = 1.0
        /// </summary>
        public static void CalculateSphericalViewFactors(
            Point3d analysisPoint,
            List<Mesh> obstacleMeshes,
            out double svf,
            out double gvf,
            out double ovf,
            double analysisHeight = 1.5,
            int sampleCount = 1000,
            double maxRayDistance = 500.0)
        {
            svf = 0.5;
            gvf = 0.5;
            ovf = 0.0;

            Point3d eyePoint = new Point3d(
                analysisPoint.X, analysisPoint.Y, analysisPoint.Z + analysisHeight);

            var directions = SolarGeometry.GenerateSphereDirections(sampleCount);

            if (obstacleMeshes == null || obstacleMeshes.Count == 0)
                return;

            int skyRays = 0;
            int groundRays = 0;
            int obstacleRays = 0;
            int totalRays = 0;

            foreach (var dir in directions)
            {
                Point3d rayOrigin = eyePoint + dir * 0.01;
                Ray3d ray = new Ray3d(rayOrigin, dir);

                bool isBlocked = false;
                foreach (var mesh in obstacleMeshes)
                {
                    if (mesh == null || mesh.Faces.Count == 0) continue;
                    double tHit = Intersection.MeshRay(mesh, ray);
                    if (tHit > 0.001 && tHit < maxRayDistance)
                    {
                        isBlocked = true;
                        break;
                    }
                }

                totalRays++;
                if (isBlocked) obstacleRays++;
                else if (dir.Z > 0.001) skyRays++;
                else if (dir.Z < -0.001) groundRays++;
            }

            if (totalRays > 0)
            {
                svf = (double)skyRays / totalRays;
                gvf = (double)groundRays / totalRays;
                ovf = (double)obstacleRays / totalRays;
            }
        }

        /// <summary>
        /// Batch full-spherical view factor calculation (parallel).
        /// Results are written into pre-allocated arrays (not out params) to avoid
        /// C# lambda capture restrictions on ref/out parameters.
        /// </summary>
        /// <param name="analysisPoints">Ground points for analysis</param>
        /// <param name="obstacleMeshes">Obstacle meshes</param>
        /// <param name="svfResults">Pre-allocated array for SVF results (filled by this method)</param>
        /// <param name="gvfResults">Pre-allocated array for GVF results</param>
        /// <param name="ovfResults">Pre-allocated array for OVF results</param>
        /// <param name="analysisHeight">Eye height above ground [m]</param>
        /// <param name="sampleCount">Number of full-sphere samples</param>
        /// <param name="maxRayDistance">Max ray distance [m]</param>
        public static void CalculateSphericalViewFactorsBatch(
            List<Point3d> analysisPoints,
            List<Mesh> obstacleMeshes,
            double[] svfResults,
            double[] gvfResults,
            double[] ovfResults,
            double analysisHeight = 1.5,
            int sampleCount = 1000,
            double maxRayDistance = 500.0)
        {
            int n = analysisPoints.Count;

            if (svfResults == null || svfResults.Length < n) throw new ArgumentException("svfResults must be pre-allocated with length >= analysisPoints.Count");
            if (gvfResults == null || gvfResults.Length < n) throw new ArgumentException("gvfResults must be pre-allocated with length >= analysisPoints.Count");
            if (ovfResults == null || ovfResults.Length < n) throw new ArgumentException("ovfResults must be pre-allocated with length >= analysisPoints.Count");

            if (obstacleMeshes == null || obstacleMeshes.Count == 0)
            {
                for (int i = 0; i < n; i++)
                {
                    svfResults[i] = 0.5;
                    gvfResults[i] = 0.5;
                    ovfResults[i] = 0.0;
                }
                return;
            }

            System.Threading.Tasks.Parallel.For(0, n, i =>
            {
                double s, g, o;
                CalculateSphericalViewFactors(
                    analysisPoints[i], obstacleMeshes,
                    out s, out g, out o,
                    analysisHeight, sampleCount, maxRayDistance);
                svfResults[i] = s;
                gvfResults[i] = g;
                ovfResults[i] = o;
            });
        }
    }
}
