using System;
using System.Collections.Generic;
using Rhino.Geometry;

namespace Common.Core
{
    /// <summary>
    /// Solar geometry helper functions for raytracing and sky dome sampling.
    /// </summary>
    public static class SolarGeometry
    {
        /// <summary>
        /// Convert astronomical azimuth (0=South, clockwise, SPA internal convention) 
        /// to geographic azimuth (0=North, clockwise, compass convention).
        /// 
        /// Geographic azimuth = Astronomical azimuth + 180° (mod 360°)
        /// </summary>
        public static double AstroToGeographicAzimuth(double astroAzimuth)
        {
            double geo = astroAzimuth + 180.0;
            geo = geo % 360.0;
            if (geo < 0) geo += 360.0;
            return geo;
        }

        /// <summary>
        /// Convert geographic azimuth (0=North, clockwise) to astronomical azimuth (0=South, clockwise).
        /// </summary>
        public static double GeographicToAstroAzimuth(double geographicAzimuth)
        {
            double astro = geographicAzimuth - 180.0;
            astro = astro % 360.0;
            if (astro < 0) astro += 360.0;
            return astro;
        }

        /// <summary>
        /// Calculate sun direction vector from geographic azimuth and zenith angle.
        /// 
        /// Geographic convention:
        /// - Azimuth: 0°=North, 90°=East, 180°=South, 270°=West (clockwise from North)
        /// - Zenith: 0°=overhead, 90°=horizon
        /// 
        /// Returns unit vector pointing FROM the ground TO the sun.
        /// Coordinate system: X=East, Y=North, Z=Up.
        /// </summary>
        /// <param name="azimuthGeo">Geographic azimuth [degrees], 0=North, clockwise</param>
        /// <param name="zenith">Zenith angle [degrees], 0=overhead, 90=horizon</param>
        /// <returns>Unit vector pointing to the sun</returns>
        public static Vector3d SunVectorFromAzimuthZenith(double azimuthGeo, double zenith)
        {
            double azRad = azimuthGeo * Math.PI / 180.0;
            double zenRad = zenith * Math.PI / 180.0;

            // Sun elevation angle
            double eleRad = Math.PI / 2.0 - zenRad;

            // Standard geographic to Cartesian conversion:
            // X = sin(azimuth) * sin(zenith) = cos(azimuth) * cos(elevation) ... wait
            // 
            // Correct conversion (0°=North, cw):
            // X (East) = sin(azimuth) * sin(zenith)
            // Y (North) = cos(azimuth) * sin(zenith)
            // Z (Up) = cos(zenith) = sin(elevation)
            double east = Math.Sin(azRad) * Math.Sin(zenRad);   // X: East component
            double north = Math.Cos(azRad) * Math.Sin(zenRad);  // Y: North component
            double up = Math.Cos(zenRad);                       // Z: Up component

            Vector3d sunVec = new Vector3d(east, north, up);
            sunVec.Unitize();
            return sunVec;
        }

        /// <summary>
        /// Generate uniformly distributed directions over the upper hemisphere (Z >= 0).
        /// Uses the Fibonacci (golden angle) spiral for near-uniform distribution.
        /// 
        /// FIXED: Previously generated 'count' global directions then filtered Z&lt;0,
        /// resulting in only ~count/2 effective hemisphere directions.
        /// Now generates exactly 'count' upper hemisphere directions.
        /// 
        /// For SVF calculation, the sample count should be at least 500-1000
        /// for commercial-grade accuracy.
        /// </summary>
        /// <param name="count">Number of upper hemisphere sample directions</param>
        /// <returns>List of unit vectors covering the upper hemisphere (Z &gt;= 0)</returns>
        public static List<Vector3d> GenerateHemisphereDirections(int count)
        {
            var dirs = new List<Vector3d>(count);
            double phi = Math.PI * (3.0 - Math.Sqrt(5.0)); // Golden angle ~2.39996 rad

            // FIXED: Generate exactly 'count' directions over the upper hemisphere only.
            // Map index i to z in [1, 0] (upper hemisphere only) using equal-area distribution.
            for (int i = 0; i < count; i++)
            {
                // Equal-area mapping: z = sqrt(1 - i/count) gives uniform distribution
                // over the hemisphere solid angle. Use z in [1, 0].
                double z = Math.Sqrt(1.0 - (double)i / count);
                double radius = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
                double theta = phi * i;
                double x = Math.Cos(theta) * radius;
                double y = Math.Sin(theta) * radius;
                dirs.Add(new Vector3d(x, y, z));
            }
            return dirs;
        }

        /// <summary>
        /// Generate uniformly distributed directions over the FULL SPHERE (4π steradians).
        /// Uses the Fibonacci (golden angle) spiral extended to full spherical coverage.
        /// 
        /// This is required for MRT calculation where the human body receives radiation
        /// from all directions (sky above, ground below, obstacles in between).
        /// 
        /// The directions are uniformly distributed over the entire unit sphere surface.
        /// For full-sphere view factor calculation, use at least 1000-2000 samples.
        /// </summary>
        /// <param name="count">Number of full-sphere sample directions</param>
        /// <returns>List of unit vectors uniformly distributed over the full sphere</returns>
        public static List<Vector3d> GenerateSphereDirections(int count)
        {
            var dirs = new List<Vector3d>(count);
            double phi = Math.PI * (3.0 - Math.Sqrt(5.0));
            for (int i = 0; i < count; i++)
            {
                double z = 1.0 - (2.0 * i + 1.0) / count;  // [-1, 1]
                double radius = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
                double theta = phi * i;
                double x = Math.Cos(theta) * radius;
                double y = Math.Sin(theta) * radius;
                dirs.Add(new Vector3d(x, y, z));
            }
            return dirs;
        }

        /// <summary>
        /// Generate uniformly distributed directions over the LOWER hemisphere (Z &lt;= 0).
        /// Used for ground view factor calculation in MRT.
        /// </summary>
        /// <param name="count">Number of lower hemisphere sample directions</param>
        /// <returns>List of unit vectors covering the lower hemisphere (Z &lt;= 0)</returns>
        public static List<Vector3d> GenerateLowerHemisphereDirections(int count)
        {
            var dirs = new List<Vector3d>(count);
            double phi = Math.PI * (3.0 - Math.Sqrt(5.0));
            for (int i = 0; i < count; i++)
            {
                double z = -Math.Sqrt(1.0 - (double)i / count);  // [-1, 0]
                double radius = Math.Sqrt(Math.Max(0.0, 1.0 - z * z));
                double theta = phi * i;
                double x = Math.Cos(theta) * radius;
                double y = Math.Sin(theta) * radius;
                dirs.Add(new Vector3d(x, y, z));
            }
            return dirs;
        }

        /// <summary>
        /// Generate stratified hemisphere sample directions for more uniform coverage.
        /// Uses equal-area projection for better angular distribution than Fibonacci spiral.
        /// </summary>
        /// <param name="count">Number of samples</param>
        /// <returns>Uniformly distributed hemisphere directions</returns>
        public static List<Vector3d> GenerateStratifiedHemisphereDirections(int count)
        {
            var dirs = new List<Vector3d>();

            // Use a stratified approach: divide hemisphere into equal-area regions
            int nLayers = (int)Math.Sqrt(count);
            int nAzimuthal = (int)Math.Ceiling((double)count / nLayers);

            for (int i = 0; i < nLayers; i++)
            {
                // Equal-area division in zenith angle
                double z1 = 1.0 - (double)i / nLayers;
                double z2 = 1.0 - (double)(i + 1) / nLayers;
                double zMid = (z1 + z2) / 2.0;
                double zenith = Math.Acos(zMid);
                double sinZenith = Math.Sin(zenith);

                for (int j = 0; j < nAzimuthal; j++)
                {
                    if (dirs.Count >= count) break;
                    double azimuth = 2.0 * Math.PI * j / nAzimuthal;
                    double x = sinZenith * Math.Cos(azimuth);
                    double y = sinZenith * Math.Sin(azimuth);
                    double z = zMid;
                    dirs.Add(new Vector3d(x, y, z));
                }
            }

            return dirs;
        }

        /// <summary>
        /// Transform a direction from Z-up hemisphere to align with a given surface normal.
        /// Used to orient sky sample directions to a surface's local coordinate system.
        /// </summary>
        /// <param name="dir">Direction in Z-up hemisphere</param>
        /// <param name="normal">Surface normal to align with</param>
        /// <returns>Transformed direction aligned with the surface normal</returns>
        public static Vector3d TransformToNormalSpace(Vector3d dir, Vector3d normal)
        {
            normal.Unitize();
            Vector3d zAxis = Vector3d.ZAxis;

            if (normal.IsParallelTo(zAxis, 1e-6) != 0)
            {
                return normal.Z > 0 ? dir : new Vector3d(dir.X, dir.Y, -dir.Z);
            }

            Vector3d axis = Vector3d.CrossProduct(zAxis, normal);
            axis.Unitize();
            double angle = Math.Acos(Math.Max(-1.0, Math.Min(1.0, zAxis * normal)));
            Transform rot = Transform.Rotation(angle, axis, Point3d.Origin);
            Vector3d result = dir;
            result.Transform(rot);
            return result;
        }

        /// <summary>
        /// Calculate tilt angle of a surface normal from horizontal.
        /// </summary>
        /// <param name="normal">Surface normal vector</param>
        /// <returns>Tilt angle [degrees], 0=horizontal, 90=vertical</returns>
        public static double TiltAngleFromNormal(Vector3d normal)
        {
            normal.Unitize();
            double cosAngle = Math.Abs(normal * Vector3d.ZAxis);
            cosAngle = Math.Max(-1.0, Math.Min(1.0, cosAngle));
            return Math.Acos(cosAngle) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Calculate incidence angle modifier (IAM) for direct radiation.
        /// 
        /// Uses ASHRAE simplified model: IAM = 1 - b0 * (1/cos(theta) - 1)
        /// 
        /// Reference: ASHRAE Handbook - HVAC Applications, Chapter 35 (Solar Energy Use).
        /// </summary>
        /// <param name="incidenceAngleDeg">Incidence angle [degrees]</param>
        /// <param name="b0">IAM coefficient, default 0.05 for typical glass</param>
        /// <returns>Incidence angle modifier [0-1]</returns>
        public static double IncidenceAngleModifier(double incidenceAngleDeg, double b0 = 0.05)
        {
            double thetaRad = incidenceAngleDeg * Math.PI / 180.0;
            double cosTheta = Math.Cos(thetaRad);
            if (cosTheta <= 0.01) return 0;
            double iam = 1.0 - b0 * (1.0 / cosTheta - 1.0);
            return Math.Max(0, Math.Min(1, iam));
        }

        /// <summary>
        /// Calculate direct irradiance on a tilted surface.
        /// EPW辐照度单位为Wh/m²（时间步长累计值），数值上对于1小时数据等于平均W/m²
        /// I_direct = DNI * cos(θ)
        /// where θ is the angle of incidence.
        /// </summary>
        /// <param name="dni">Direct normal irradiance [W/m²]</param>
        /// <param name="cosTheta">Cosine of incidence angle [-]</param>
        /// <returns>Direct irradiance on tilted surface [W/m²]</returns>
        public static double DirectTiltedIrradiance(double dni, double cosTheta)
        {
            if (cosTheta <= 0) return 0;
            return dni * cosTheta;
        }

        /// <summary>
        /// Calculate diffuse irradiance on a tilted surface using isotropic sky model.
        /// EPW辐照度单位为Wh/m²（时间步长累计值），数值上对于1小时数据等于平均W/m²
        /// I_diffuse = DHI * (1 + cos β) / 2
        /// where β is the surface tilt angle.
        /// 
        /// Reference: Liu & Jordan (1963), Solar Energy, 7(2), 53-59.
        /// </summary>
        /// <param name="dhi">Diffuse horizontal irradiance [W/m²]</param>
        /// <param name="tiltRad">Surface tilt angle [radians]</param>
        /// <returns>Diffuse irradiance on tilted surface [W/m²]</returns>
        public static double IsotropicDiffuseIrradiance(double dhi, double tiltRad)
        {
            return dhi * (1.0 + Math.Cos(tiltRad)) / 2.0;
        }

        /// <summary>
        /// Calculate ground-reflected irradiance on a tilted surface.
        /// 
        /// I_ground = GHI * ρ * (1 - cos β) / 2
        /// where ρ is the ground albedo.
        /// 
        /// Note: Some models use DHI instead of GHI for more conservative estimates.
        /// </summary>
        /// <param name="ghi">Global horizontal irradiance [W/m²]</param>
        /// <param name="albedo">Ground albedo [0-1]</param>
        /// <param name="tiltRad">Surface tilt angle [radians]</param>
        /// <returns>Ground-reflected irradiance [W/m²]</returns>
        public static double GroundReflectedIrradiance(double ghi, double albedo, double tiltRad)
        {
            return ghi * albedo * (1.0 - Math.Cos(tiltRad)) / 2.0;
        }
    }
}
