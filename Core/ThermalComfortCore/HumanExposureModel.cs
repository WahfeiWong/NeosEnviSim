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
        /// Classify which type of obstacle blocks sunlight from reaching the sample point.
        ///
        /// PHYSICALLY CORRECTED (2026-06-14):
        /// Light travels from sun to ground (forward direction). The obstacle that blocks
        /// light FIRST from the sun direction determines DNI. In backward ray tracing
        /// (from human to sun), this is the FARTHEST intersection along the ray.
        ///
        /// Critical rule: Opaque objects have absolute priority. If ANY opaque mesh is
        /// intersected by the ray, all tree/translucent obstacles behind it are in shadow
        /// and must NOT contribute to DNI transmission.
        ///
        /// Algorithm:
        ///   1. Check all Opaque meshes — any hit → return Opaque (DNI = 0)
        ///   2. No opaque hit → find FARTHEST Tree/Translucent hit (sun-side first)
        ///   3. No hit at all → return None (full DNI)
        ///
        /// This ensures that vegetation or translucent sunshades located in the shadow
        /// of a building (behind it from sun's perspective) do not incorrectly transmit
        /// DNI to the sample point.
        /// </summary>
        /// <param name="ray">Ray to test (from sample point toward sun)</param>
        /// <param name="obstacleSet">Classified obstacle set</param>
        /// <param name="maxRayDistance">Maximum ray distance</param>
        /// <param name="hitDistance">Output: distance to the blocking obstacle</param>
        /// <returns>Type of obstacle that blocks sunlight</returns>
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
            // If any opaque mesh is on the ray path, it fully blocks direct radiation.
            // Tree/translucent obstacles behind an opaque object are in shadow and
            // must NOT transmit DNI.
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
            // STEP 2: No opaque on this ray. Find the FARTHEST Tree/Translucent hit.
            // Farthest from human = closest from sun direction = first obstacle that
            // sunlight encounters along its forward path.
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
            {
                hitDistance = farthestHit;
            }

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
        ///   - Ray not blocked: contribution = 1.0 (full DNI)
        ///   - Ray hits opaque object: contribution = 0.0 (no DNI)
        ///   - Ray hits tree detail: contribution = exp(-k * LAD * s) (Beer-Lambert)
        ///   - Ray hits translucent shade: contribution = tau (fixed transmittance)
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
        /// <returns>Effective DNI exposure factor [0, 1] (can slightly exceed 1 if tau > 1, clamped)</returns>
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

                // Classify the first obstacle hit by this ray
                ObstacleType hitType = ClassifyRayHit(ray, obstacleSet, maxRayDistance, out double hitDist);

                double contribution;
                switch (hitType)
                {
                    case ObstacleType.None:
                        // No obstacle — full DNI
                        contribution = 1.0;
                        break;

                    case ObstacleType.Opaque:
                        // Fully opaque — no DNI transmitted
                        contribution = 0.0;
                        break;

                    case ObstacleType.Tree:
                        // Beer-Lambert law through vegetation canopy
                        double pathLength = CalculateCanopyPathLength(
                            samplePoint, sunDir,
                            obstacleSet.TreeCanopyMeshes, maxRayDistance);
                        double k = obstacleSet.ExtinctionCoefficient;
                        double lad = obstacleSet.LeafAreaDensity;
                        contribution = Math.Exp(-k * lad * pathLength);
                        break;

                    case ObstacleType.Translucent:
                        // Fixed transmittance for translucent materials
                        contribution = obstacleSet.TranslucentTransmittance;
                        break;

                    default:
                        contribution = 1.0;
                        break;
                }

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
        /// vegetation and translucent materials.
        ///
        /// Both values are returned for use in different parts of MRT calculation:
        /// - f_exp: used for non-DNI purposes (if needed in future extensions)
        /// - f_dni: used for direct radiation component in MRT
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

                ObstacleType hitType = ClassifyRayHit(ray, obstacleSet, maxRayDistance, out double hitDist);

                if (hitType == ObstacleType.None)
                {
                    exposedCount++;
                    totalDNIContribution += 1.0;
                }
                else if (hitType == ObstacleType.Opaque)
                {
                    // Shaded, no DNI contribution
                    totalDNIContribution += 0.0;
                }
                else if (hitType == ObstacleType.Tree)
                {
                    // Shaded but DNI partially transmitted through canopy
                    double pathLength = CalculateCanopyPathLength(
                        samplePoint, sunDir,
                        obstacleSet.TreeCanopyMeshes, maxRayDistance);
                    double k = obstacleSet.ExtinctionCoefficient;
                    double lad = obstacleSet.LeafAreaDensity;
                    double transmission = Math.Exp(-k * lad * pathLength);
                    totalDNIContribution += transmission;
                }
                else if (hitType == ObstacleType.Translucent)
                {
                    // Shaded but DNI partially transmitted through translucent material
                    totalDNIContribution += obstacleSet.TranslucentTransmittance;
                }
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
        // NEW: Full-spherical (4pi) view factor decomposition for MRT
        // ========================================================================

        /// <summary>
        /// Calculate full-spherical (4pi) view factors for a single point.
        /// Decomposes the entire sphere into three components:
        ///   - SVF: Sky View Factor (visible sky)
        ///   - GVF: Ground View Factor (visible ground)
        ///   - OVF: Obstacle View Factor (blocked by obstacles)
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
