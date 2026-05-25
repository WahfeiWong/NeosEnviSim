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
