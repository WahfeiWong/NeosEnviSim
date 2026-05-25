using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SolarPV.Core
{
    /// <summary>
    /// Automatic geometry inference for PV array parameters from input panel surfaces.
    /// 
    /// When the user sets RowSpacing or ModuleHeight to -1 (auto-infer), this class
    /// analyzes the spatial distribution of panel face centers to estimate:
    ///   - RowSpacing: average distance between parallel rows of panels
    ///   - ModuleHeight: average height of panel centroids above the lowest panel
    /// 
    /// The algorithm assumes panels are arranged in approximately parallel rows
    /// (typical for ground-mount or rooftop PV arrays). For irregular/non-array
    /// geometries, the inferred values are best-effort approximations.
    /// 
    /// Reference approach: DeepSeek review recommendation (Section 3 & 6).
    /// </summary>
    public static class AutoGeometryInference
    {
        /// <summary>
        /// Infer row spacing and module height from a collection of panel face centers.
        /// 
        /// Algorithm for RowSpacing:
        /// 1. Compute the mean panel normal vector (dominant tilt direction)
        /// 2. Define the "row normal" as the horizontal direction perpendicular to the
        ///    mean normal's projection onto the XY plane. This is the direction along
        ///    which rows are separated.
        /// 3. Project all face centers onto the row normal axis
        /// 4. Cluster the projected coordinates to identify distinct rows
        /// 5. Compute average spacing between adjacent row clusters
        /// 
        /// Algorithm for ModuleHeight:
        /// 1. Find the minimum Z coordinate among all face centers (ground reference)
        /// 2. Compute mean Z - min Z as average module height above ground
        /// </summary>
        /// <param name="faceCenters">Collection of panel face center points</param>
        /// <param name="faceNormals">Collection of panel face normal vectors (corresponding to centers)</param>
        /// <param name="inferredRowSpacing">Output: inferred row-to-row spacing [m]</param>
        /// <param name="inferredModuleHeight">Output: inferred module centroid height [m]</param>
        /// <param name="confidence">Output: confidence level [0-1] based on array regularity</param>
        /// <returns>True if inference succeeded with at least low confidence</returns>
        public static bool InferArrayGeometry(
            List<Point3d> faceCenters,
            List<Vector3d> faceNormals,
            out double inferredRowSpacing,
            out double inferredModuleHeight,
            out double confidence)
        {
            inferredRowSpacing = 2.0;   // fallback default
            inferredModuleHeight = 1.0; // fallback default
            confidence = 0.0;

            if (faceCenters == null || faceCenters.Count < 2)
                return false;

            // ===== Module Height Inference =====
            double minZ = faceCenters.Min(p => p.Z);
            double maxZ = faceCenters.Max(p => p.Z);
            double meanZ = faceCenters.Average(p => p.Z);
            inferredModuleHeight = meanZ - minZ;

            // Clamp to physically reasonable range [0.1, 50m]
            inferredModuleHeight = Math.Max(0.1, Math.Min(50.0, inferredModuleHeight));

            // ===== Row Spacing Inference =====
            if (faceNormals == null || faceNormals.Count < 2 || faceNormals.Count != faceCenters.Count)
            {
                // Cannot infer row spacing without normals; use height-based fallback
                confidence = 0.3;
                return true;
            }

            // 1. Compute mean normal (dominant panel orientation)
            Vector3d meanNormal = new Vector3d(0, 0, 0);
            foreach (var n in faceNormals)
            {
                Vector3d nu = n;
                nu.Unitize();
                meanNormal += nu;
            }
            meanNormal.Unitize();

            // 2. Compute "row normal" = horizontal direction perpendicular to mean normal
            // For a typical south-facing tilted array:
            //   meanNormal has a southward horizontal component
            //   rowNormal points east-west (perpendicular to row direction)
            // Panel rows run PARALLEL to the mean normal's horizontal projection,
            // so row-to-row spacing is measured PERPENDICULAR to it.
            Vector3d meanNormalXY = new Vector3d(meanNormal.X, meanNormal.Y, 0);
            if (meanNormalXY.Length < 0.001)
            {
                // Panels are nearly horizontal; row direction is arbitrary
                // Use X-axis as default row direction
                meanNormalXY = new Vector3d(1, 0, 0);
            }
            meanNormalXY.Unitize();

            // Row normal is perpendicular to the row direction in the XY plane
            // If row direction is (dx, dy), row normal is (-dy, dx) or (dy, -dx)
            Vector3d rowNormal = new Vector3d(-meanNormalXY.Y, meanNormalXY.X, 0);
            rowNormal.Unitize();

            // 3. Project all face centers onto the row normal axis
            double[] projections = new double[faceCenters.Count];
            for (int i = 0; i < faceCenters.Count; i++)
            {
                projections[i] = faceCenters[i].X * rowNormal.X
                               + faceCenters[i].Y * rowNormal.Y;
            }

            Array.Sort(projections);

            // 4. Compute gaps between sorted projections
            List<double> gaps = new List<double>();
            for (int i = 1; i < projections.Length; i++)
            {
                double gap = projections[i] - projections[i - 1];
                if (gap > 0.01) // ignore near-zero gaps (same-row panels)
                    gaps.Add(gap);
            }

            if (gaps.Count == 0)
            {
                // All panels on a single "row"; cannot infer spacing
                confidence = 0.3;
                return true;
            }

            // 5. Use robust statistics: median of gaps above threshold
            // Filter outliers using IQR method
            gaps.Sort();
            double medianGap = gaps[gaps.Count / 2];

            if (gaps.Count >= 4)
            {
                double q1 = gaps[gaps.Count / 4];
                double q3 = gaps[3 * gaps.Count / 4];
                double iqr = q3 - q1;
                double lowerBound = q1 - 1.5 * iqr;
                double upperBound = q3 + 1.5 * iqr;

                var filteredGaps = gaps.Where(g => g >= lowerBound && g <= upperBound).ToList();
                if (filteredGaps.Count > 0)
                {
                    inferredRowSpacing = filteredGaps.Average();
                }
                else
                {
                    inferredRowSpacing = medianGap;
                }
            }
            else
            {
                inferredRowSpacing = medianGap;
            }

            // Clamp to physically reasonable range [0.5, 50m]
            inferredRowSpacing = Math.Max(0.5, Math.Min(50.0, inferredRowSpacing));

            // 6. Compute confidence based on gap consistency (coefficient of variation)
            if (gaps.Count >= 3)
            {
                double meanGap = gaps.Average();
                double variance = gaps.Average(g => (g - meanGap) * (g - meanGap));
                double stdDev = Math.Sqrt(variance);
                double cv = (meanGap > 0.01) ? stdDev / meanGap : 1.0;
                // CV near 0 = very regular (high confidence); CV > 0.5 = irregular (low confidence)
                confidence = Math.Max(0.1, Math.Min(1.0, 1.0 - 2.0 * cv));
            }
            else
            {
                confidence = 0.5;
            }

            return true;
        }

        /// <summary>
        /// Quick helper: extract face centers and normals from FaceData lists.
        /// </summary>
        public static void ExtractGeometryData(
            List<List<object>> allFaceData,  // Using object to avoid FaceData type dependency
            out List<Point3d> centers,
            out List<Vector3d> normals)
        {
            centers = new List<Point3d>();
            normals = new List<Vector3d>();

            // This method is a placeholder; actual extraction happens in PVSimulator
            // where FaceData type is accessible. The inference is called directly
            // with extracted Point3d and Vector3d collections.
        }
    }
}
