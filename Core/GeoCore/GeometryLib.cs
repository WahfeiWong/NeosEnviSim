using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Geometry.Core
{
    /// <summary>
    /// Shared geometry utilities for mesh generation and per-face data extraction.
    /// Aligns with Ladybug's LB Generate Point Grid default mode:
    /// - Gridded meshing via Rhino.Mesh.CreateFromBrep fills to irregular edges
    ///   with both quad and triangulated faces.
    /// - Pure quad mode keeps only fully interior cells (gaps at boundaries).
    /// </summary>
    public static class FaceMesher
    {
        // ============================================================
        //  Mode A: Rhino Gridded Mesh (default, fills to edges)
        // ============================================================

        /// <summary>
        /// Creates a gridded mesh from a BrepFace using Rhino's Mesh.CreateFromBrep.
        /// Fills the face to its edges with both quad and triangulated faces.
        /// This is the recommended mode for simulation components.
        /// </summary>
        /// <param name="face">The BrepFace to mesh.</param>
        /// <param name="resolution">Target grid cell size.</param>
        /// <returns>A Mesh, or null if creation failed.</returns>
        public static Mesh CreateGriddedMesh(BrepFace face, double resolution)
        {
            Brep faceBrep = face.DuplicateFace(false);
            if (faceBrep == null || !faceBrep.IsValid)
                return null;

            MeshingParameters mp = new MeshingParameters
            {
                MaximumEdgeLength = resolution,
                MinimumEdgeLength = resolution,
                GridAspectRatio = 1.0
            };

            Mesh[] meshes = Mesh.CreateFromBrep(faceBrep, mp);
            if (meshes == null || meshes.Length == 0)
                return null;

            Mesh mesh = new Mesh();
            foreach (var m in meshes)
            {
                if (m != null && m.Faces.Count > 0)
                    mesh.Append(m);
            }

            mesh.Compact();
            return mesh.Faces.Count > 0 ? mesh : null;
        }

        // ============================================================
        //  Mode B: Pure Quad Mesh (gaps at irregular edges)
        // ============================================================

        /// <summary>
        /// Creates a pure quad mesh from a planar BrepFace.
        /// Only fully interior quad cells are kept; boundary cells that are
        /// partially outside the face outline are removed (creating gaps).
        /// Falls back to CreateGriddedMesh for non-planar faces.
        /// </summary>
        /// <param name="face">The BrepFace to mesh.</param>
        /// <param name="resolution">Target grid cell size.</param>
        /// <returns>A Mesh, or null if creation failed.</returns>
        public static Mesh CreatePureQuadMesh(BrepFace face, double resolution)
        {
            if (!face.TryGetPlane(out Plane plane, 0.01))
                return CreateGriddedMesh(face, resolution); // fallback

            Curve outerCurve = face.OuterLoop.To3dCurve();
            if (outerCurve == null) return null;

            double tol = resolution * 0.05;
            if (tol < 1e-6) tol = 1e-6;

            BoundingBox bbox = outerCurve.GetBoundingBox(plane);
            double minU = bbox.Min.X, maxU = bbox.Max.X;
            double minV = bbox.Min.Y, maxV = bbox.Max.Y;

            // Collect hole curves
            List<Curve> holeCurves = new List<Curve>();
            foreach (var loop in face.Loops)
            {
                if (loop.LoopType == BrepLoopType.Outer) continue;
                var holeCrv = loop.To3dCurve();
                if (holeCrv != null) holeCurves.Add(holeCrv);
            }

            // Grid dimensions: num = max(1, floor(dom / dim)), dim = dom / num
            double uRange = maxU - minU;
            double vRange = maxV - minV;
            int cols = Math.Max(1, (int)(uRange / resolution));
            int rows = Math.Max(1, (int)(vRange / resolution));

            double stepU = uRange / cols;
            double stepV = vRange / rows;

            Mesh mesh = new Mesh();
            bool[,] insideMap = new bool[rows + 1, cols + 1];
            int[,] vIndices = new int[rows + 1, cols + 1];

            // Generate vertices and test containment
            for (int r = 0; r <= rows; r++)
            {
                for (int c = 0; c <= cols; c++)
                {
                    double u = minU + c * stepU;
                    double v = minV + r * stepV;
                    if (r == rows) v = maxV;
                    if (c == cols) u = maxU;

                    Point3d pt = plane.PointAt(u, v);
                    vIndices[r, c] = mesh.Vertices.Count;
                    mesh.Vertices.Add(pt);

                    insideMap[r, c] = IsPointInsideFace(pt, outerCurve, holeCurves, plane, tol);
                }
            }

            // Create faces only for fully interior quad cells
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if (insideMap[r, c] && insideMap[r, c + 1] &&
                        insideMap[r + 1, c + 1] && insideMap[r + 1, c])
                    {
                        mesh.Faces.AddFace(
                            vIndices[r, c],
                            vIndices[r, c + 1],
                            vIndices[r + 1, c + 1],
                            vIndices[r + 1, c]);
                    }
                }
            }

            mesh.Normals.ComputeNormals();
            mesh.FaceNormals.ComputeFaceNormals();
            mesh.Compact();

            return mesh.Faces.Count > 0 ? mesh : null;
        }

        /// <summary>
        /// Checks if a point lies inside the face boundary (outside all holes).
        /// </summary>
        public static bool IsPointInsideFace(Point3d point, Curve outerCurve,
            List<Curve> holeCurves, Plane plane, double tol)
        {
            if (outerCurve.Contains(point, plane, tol) == PointContainment.Outside)
                return false;

            foreach (var hole in holeCurves)
            {
                if (hole.Contains(point, plane, tol) == PointContainment.Inside)
                    return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Per-face data extraction utilities for generated meshes.
    /// </summary>
    public static class FaceExtractor
    {
        /// <summary>Center point of a mesh face (quad or triangle).</summary>
        public static Point3d GetCenter(Mesh mesh, int faceIndex)
        {
            var face = mesh.Faces[faceIndex];
            Point3d a = mesh.Vertices[face.A];
            Point3d b = mesh.Vertices[face.B];
            Point3d c = mesh.Vertices[face.C];
            if (face.IsQuad)
            {
                Point3d d = mesh.Vertices[face.D];
                return (a + b + c + d) * 0.25;
            }
            return (a + b + c) * (1.0 / 3.0);
        }

        /// <summary>Area of a mesh face (quad or triangle).</summary>
        public static double GetArea(Mesh mesh, int faceIndex)
        {
            var face = mesh.Faces[faceIndex];
            if (face.IsQuad)
            {
                Point3d a = mesh.Vertices[face.A];
                Point3d b = mesh.Vertices[face.B];
                Point3d c = mesh.Vertices[face.C];
                Point3d d = mesh.Vertices[face.D];
                double area1 = Vector3d.CrossProduct(b - a, c - a).Length * 0.5;
                double area2 = Vector3d.CrossProduct(c - a, d - a).Length * 0.5;
                return area1 + area2;
            }
            else
            {
                Point3d a = mesh.Vertices[face.A];
                Point3d b = mesh.Vertices[face.B];
                Point3d c = mesh.Vertices[face.C];
                return Vector3d.CrossProduct(b - a, c - a).Length * 0.5;
            }
        }

        /// <summary>Closed polyline border of a mesh face.</summary>
        public static Polyline GetBorder(Mesh mesh, int faceIndex)
        {
            var face = mesh.Faces[faceIndex];
            var pts = new List<Point3d>
            {
                mesh.Vertices[face.A],
                mesh.Vertices[face.B],
                mesh.Vertices[face.C]
            };
            if (face.IsQuad)
                pts.Add(mesh.Vertices[face.D]);
            pts.Add(mesh.Vertices[face.A]); // close
            return new Polyline(pts);
        }
    }

    /// <summary>
    /// Brep to triangulated mesh conversion for obstacles and context geometry.
    /// </summary>
    public static class BrepMeshing
    {
        /// <summary>
        /// Converts a list of Breps to meshes using Rhino's default meshing parameters.
        /// Used for obstacle / context geometry conversion.
        /// </summary>
        public static List<Mesh> ConvertBrepsToMeshes(IEnumerable<Brep> breps)
        {
            var result = new List<Mesh>();
            if (breps == null) return result;

            foreach (var brep in breps)
            {
                if (brep == null || !brep.IsValid) continue;
                var meshes = Mesh.CreateFromBrep(brep, MeshingParameters.Default);
                if (meshes != null)
                {
                    foreach (var m in meshes)
                    {
                        if (m != null && m.IsValid)
                            result.Add(m);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Determines whether a mesh's normal should be flipped based on Z orientation.
        /// For planar meshes, checks if the reference normal points downward.
        /// For non-planar meshes, uses the average normal direction.
        /// </summary>
        public static bool ShouldFlipNormals(Mesh mesh)
        {
            if (mesh == null || mesh.Faces.Count == 0) return false;

            Vector3d refNormal = (Vector3d)mesh.FaceNormals[0];
            refNormal.Unitize();

            // Check planarity
            bool isPlanar = true;
            for (int i = 1; i < mesh.Faces.Count; i++)
            {
                Vector3d n = (Vector3d)mesh.FaceNormals[i];
                n.Unitize();
                if (Math.Abs(refNormal * n) < 0.99)
                {
                    isPlanar = false;
                    break;
                }
            }

            if (isPlanar)
                return refNormal.Z < 0;

            Vector3d avgNormal = Vector3d.Zero;
            for (int i = 0; i < mesh.Faces.Count; i++)
                avgNormal += (Vector3d)mesh.FaceNormals[i];
            avgNormal /= mesh.Faces.Count;
            return avgNormal.Z < -0.1;
        }
    }
}
