using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace NeosUtility
{
    /// <summary>
    /// Leaf Distributor component.
    /// Distributes leaf clusters onto a tree mesh using convex hull surface population.
    /// Based on branch lines, builds a convex hull from branch geometry and orients
    /// leaf clusters to randomly distributed points on the hull surface.
    /// </summary>
    public class LeafDistributorComponent : GH_Component
    {
        public LeafDistributorComponent()
          : base("Leaf Distributor", "Leaf",
              "Distribute leaf clusters onto a tree mesh. " +
              "Builds a convex hull from branch geometry and populates leaf clusters on the hull surface.",
              "Neos", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // Leaf cluster mesh(es)
            pManager.AddMeshParameter("Leaf Mesh", "LM", "Leaf cluster mesh(es). Multiple meshes will be joined into a single unified mesh internally.", GH_ParamAccess.list);
            // Tree Mesh from Tree Generator
            pManager.AddMeshParameter("Tree Mesh", "TM", "Tree mesh output from the Tree Generator component.", GH_ParamAccess.item);
            // Branch Lines from Tree Generator
            pManager.AddCurveParameter("Branch Lines", "BL", "Branch lines output from the Tree Generator component.", GH_ParamAccess.list);
            // Cluster Count
            pManager.AddIntegerParameter("Cluster Count", "CC", "Number of leaf clusters to distribute on the canopy surface. Default: 100.", GH_ParamAccess.item, 500);
            // Distribution Seed
            pManager.AddIntegerParameter("Distribution Seed", "DS", "Random seed for leaf cluster distribution on hull surface. Default: 0.", GH_ParamAccess.item, 0);
            // Scale Factor
            pManager.AddNumberParameter("Scale Factor", "SF", "Scale factor for all leaf clusters. Use -1 for random scaling within Scale Range. Default: -1.", GH_ParamAccess.item, -1);
            // Scale Range
            pManager.AddIntervalParameter("Scale Range", "SR", "Scale range when SF=-1. Default: 0.1 to 1.2.", GH_ParamAccess.item, new Interval(0.1, 3.0));
            // Scale Seed
            pManager.AddIntegerParameter("Scale Seed", "SS", "Random seed for leaf cluster scaling (only used when SF=-1). Default: 0.", GH_ParamAccess.item, 0);

            // Make leaf mesh optional
            pManager[0].Optional = true;
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Tree Model", "TM", "Complete tree mesh with leaf clusters appended", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Mesh> leafMeshes = new List<Mesh>();
            Mesh treeMesh = null;
            List<Curve> branchCurves = new List<Curve>();
            int clusterCount = 100;
            int distributionSeed = 0;
            double scaleFactor = -1;
            Interval scaleRange = new Interval(0.1, 1.2);
            int scaleSeed = 0;

            DA.GetDataList(0, leafMeshes);
            if (!DA.GetData(1, ref treeMesh)) return;
            if (!DA.GetDataList(2, branchCurves)) return;
            DA.GetData(3, ref clusterCount);
            DA.GetData(4, ref distributionSeed);
            DA.GetData(5, ref scaleFactor);
            DA.GetData(6, ref scaleRange);
            DA.GetData(7, ref scaleSeed);

            // Validate inputs
            if (treeMesh == null || treeMesh.Faces.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Tree Mesh (TM) is null or empty.");
                return;
            }
            if (branchCurves == null || branchCurves.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Branch Lines (BL) is null or empty.");
                return;
            }
            if (clusterCount < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cluster Count (CC) must be >= 0.");
                return;
            }
            if (scaleRange == null)
                scaleRange = new Interval(0.1, 1.2);
            if (scaleRange.Length <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Scale Range has zero or negative length. Using default 0.1 to 1.2.");
                scaleRange = new Interval(0.1, 1.2);
            }

            // Join multiple leaf meshes into a single unified mesh
            Mesh leafMesh = null;
            if (leafMeshes.Count > 0)
            {
                Mesh joined = new Mesh();
                foreach (Mesh m in leafMeshes)
                {
                    if (m != null)
                        joined.Append(m.DuplicateMesh());
                }
                if (joined.Vertices.Count > 0)
                {
                    joined.Weld(Math.PI);
                    joined.Normals.ComputeNormals();
                    joined.Compact();
                    leafMesh = joined;
                }
            }

            // If no leaf mesh or cluster count is 0, output tree mesh as-is
            if (leafMesh == null || clusterCount == 0)
            {
                DA.SetData(0, treeMesh.DuplicateMesh());
                if (leafMesh == null && clusterCount > 0)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No leaf mesh provided. Outputting tree mesh without leaves.");
                return;
            }

            // ---- Build convex hull from branch lines ----
            Mesh resultMesh = treeMesh.DuplicateMesh();

            // Identify trunk by finding the line whose midpoint has the lowest Z
            int trunkIndex = 0;
            double minMidZ = double.MaxValue;
            for (int i = 0; i < branchCurves.Count; i++)
            {
                Point3d ptStart = branchCurves[i].PointAtStart;
                Point3d ptEnd = branchCurves[i].PointAtEnd;
                double midZ = (ptStart.Z + ptEnd.Z) * 0.5;
                if (midZ < minMidZ)
                {
                    minMidZ = midZ;
                    trunkIndex = i;
                }
            }

            // Collect start, end, and mid points of all branch lines EXCEPT the trunk
            List<Point3d> canopyPoints = new List<Point3d>((branchCurves.Count - 1) * 3);
            for (int i = 0; i < branchCurves.Count; i++)
            {
                if (i == trunkIndex) continue; // Skip trunk
                Point3d ptStart = branchCurves[i].PointAtStart;
                Point3d ptEnd = branchCurves[i].PointAtEnd;
                Point3d midPoint = new Point3d(
                    (ptStart.X + ptEnd.X) * 0.5,
                    (ptStart.Y + ptEnd.Y) * 0.5,
                    (ptStart.Z + ptEnd.Z) * 0.5);
                canopyPoints.Add(ptStart);
                canopyPoints.Add(ptEnd);
                canopyPoints.Add(midPoint);
            }

            if (canopyPoints.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "No branch points found after removing trunk. Try increasing Recursion Depth.");
                DA.SetData(0, resultMesh);
                return;
            }

            // Build convex hull mesh
            Mesh hullMesh = BuildHullMesh(canopyPoints);
            if (hullMesh == null || hullMesh.Faces.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Failed to build convex hull from branch points. Points may be coplanar or collinear.");
                DA.SetData(0, resultMesh);
                return;
            }

            // ---- Populate geometry: distribute points on hull surface ----
            List<Point3d> surfacePoints = PopulateHullSurface(hullMesh, clusterCount, distributionSeed);
            if (surfacePoints.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No surface points generated on convex hull.");
                DA.SetData(0, resultMesh);
                return;
            }

            // ---- Orient leaf clusters to surface points ----
            // Get leaf mesh bounding box center (volume center) as source reference point
            BoundingBox leafBBox = leafMesh.GetBoundingBox(true);
            Point3d leafCenter = leafBBox.Center;

            Random scaleRandom = new Random(scaleSeed);
            Random rotRandom = new Random(distributionSeed);

            foreach (Point3d targetPoint in surfacePoints)
            {
                Mesh leafClone = leafMesh.DuplicateMesh();

                // Get effective scale
                double scale;
                if (scaleFactor < 0)
                    scale = scaleRange.Min + scaleRandom.NextDouble() * (scaleRange.Max - scaleRange.Min);
                else
                    scale = scaleFactor;
                scale = Math.Max(0.001, scale);

                // Build transform: Scale -> RotateXYZ -> Translate (like GH Orient)
                Transform xf = Transform.Identity;

                // 1. Scale from leaf center
                if (Math.Abs(scale - 1.0) > 1e-10)
                    xf = Transform.Scale(leafCenter, scale) * xf;

                // 2. Random rotation around leaf center (XYZ) - use single Random for deterministic behavior
                xf = Transform.Rotation(rotRandom.NextDouble() * 2.0 * Math.PI, Vector3d.XAxis, leafCenter) * xf;
                xf = Transform.Rotation(rotRandom.NextDouble() * 2.0 * Math.PI, Vector3d.YAxis, leafCenter) * xf;
                xf = Transform.Rotation(rotRandom.NextDouble() * 2.0 * Math.PI, Vector3d.ZAxis, leafCenter) * xf;

                // 3. Translate from leaf center to target point (Orient)
                xf = Transform.Translation(targetPoint - leafCenter) * xf;

                leafClone.Transform(xf);
                leafClone.Normals.ComputeNormals();
                leafClone.Compact();
                resultMesh.Append(leafClone);
            }

            resultMesh.Normals.ComputeNormals();
            resultMesh.Compact();

            if (surfacePoints.Count < clusterCount)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Placed {surfacePoints.Count} of {clusterCount} leaf clusters.");
            }

            DA.SetData(0, resultMesh);
        }

        /// <summary>
        /// Populates points on the surface of a convex hull mesh using area-weighted random sampling.
        /// Equivalent to Grasshopper's Populate Geometry component.
        /// </summary>
        private List<Point3d> PopulateHullSurface(Mesh hullMesh, int count, int seed)
        {
            List<Point3d> points = new List<Point3d>(count);

            if (hullMesh == null || hullMesh.Faces.Count == 0 || count <= 0)
                return points;

            // Build triangle data with accumulated areas for weighted sampling
            List<double> cumAreas = new List<double>();
            List<int[]> triangles = new List<int[]>();
            double totalArea = 0;

            for (int i = 0; i < hullMesh.Faces.Count; i++)
            {
                MeshFace face = hullMesh.Faces[i];
                Point3d a, b, c;

                if (face.IsTriangle)
                {
                    a = hullMesh.Vertices[face.A];
                    b = hullMesh.Vertices[face.B];
                    c = hullMesh.Vertices[face.C];
                }
                else if (face.IsQuad)
                {
                    // Split quad into two triangles, use first triangle
                    a = hullMesh.Vertices[face.A];
                    b = hullMesh.Vertices[face.B];
                    c = hullMesh.Vertices[face.C];
                }
                else
                {
                    continue;
                }

                double area = 0.5 * Vector3d.CrossProduct(b - a, c - a).Length;
                if (area > 1e-12)
                {
                    totalArea += area;
                    cumAreas.Add(totalArea);
                    triangles.Add(new int[] { face.A, face.B, face.C });
                }
            }

            if (totalArea <= 0 || triangles.Count == 0)
                return points;

            Random random = new Random(seed);

            for (int i = 0; i < count; i++)
            {
                // Area-weighted random triangle selection
                double r = random.NextDouble() * totalArea;
                int triIndex = 0;
                for (int j = 0; j < cumAreas.Count; j++)
                {
                    if (r <= cumAreas[j])
                    {
                        triIndex = j;
                        break;
                    }
                }

                // Random barycentric coordinates on the selected triangle
                int[] tri = triangles[triIndex];
                Point3d a = hullMesh.Vertices[tri[0]];
                Point3d b = hullMesh.Vertices[tri[1]];
                Point3d c = hullMesh.Vertices[tri[2]];

                double u = random.NextDouble();
                double v = random.NextDouble();

                // Ensure point is inside triangle (reflect if outside)
                if (u + v > 1.0)
                {
                    u = 1.0 - u;
                    v = 1.0 - v;
                }

                double w = 1.0 - u - v;
                Point3d pt = a * u + b * v + c * w;
                points.Add(pt);
            }

            return points;
        }

        #region QuickHull 3D

        /// <summary>
        /// Builds a convex hull mesh directly from points using QuickHull 3D.
        /// </summary>
        private Mesh BuildHullMesh(List<Point3d> points)
        {
            if (points == null || points.Count < 4)
                return null;

            // Remove near-duplicate points
            List<Point3d> unique = new List<Point3d>();
            const double tol = 1e-8;
            foreach (Point3d p in points)
            {
                bool dup = false;
                foreach (Point3d u in unique)
                {
                    if (Math.Abs(p.X - u.X) < tol && Math.Abs(p.Y - u.Y) < tol && Math.Abs(p.Z - u.Z) < tol)
                    { dup = true; break; }
                }
                if (!dup) unique.Add(p);
            }

            if (unique.Count < 4)
                return null;

            return ComputeQuickHull3D(unique);
        }

        /// <summary>
        /// QuickHull 3D implementation. Builds a true convex hull mesh.
        /// Uses an interior reference point to ensure all face normals point outward.
        /// </summary>
        private Mesh ComputeQuickHull3D(List<Point3d> pts)
        {
            int n = pts.Count;

            // ---- Step 1: Find initial tetrahedron ----
            int minX = 0, maxX = 0;
            for (int i = 1; i < n; i++)
            {
                if (pts[i].X < pts[minX].X) minX = i;
                if (pts[i].X > pts[maxX].X) maxX = i;
            }

            double maxD = 0;
            int p2 = -1;
            for (int i = 0; i < n; i++)
            {
                if (i == minX || i == maxX) continue;
                double d = new Line(pts[minX], pts[maxX]).DistanceTo(pts[i], true);
                if (d > maxD) { maxD = d; p2 = i; }
            }
            if (p2 < 0 || maxD < 1e-12) return null;

            Plane basePl = new Plane(pts[minX], pts[maxX], pts[p2]);
            maxD = 0;
            int p3 = -1;
            for (int i = 0; i < n; i++)
            {
                if (i == minX || i == maxX || i == p2) continue;
                double d = Math.Abs(basePl.DistanceTo(pts[i]));
                if (d > maxD) { maxD = d; p3 = i; }
            }
            if (p3 < 0 || maxD < 1e-12) return null;

            Vector3d v01 = pts[maxX] - pts[minX];
            Vector3d v02 = pts[p2] - pts[minX];
            Vector3d v03 = pts[p3] - pts[minX];
            double vol = Vector3d.CrossProduct(v01, v02) * v03;
            if (vol < 0)
            {
                int tmp = maxX; maxX = p2; p2 = tmp;
            }

            Point3d interior = new Point3d(
                (pts[minX].X + pts[maxX].X + pts[p2].X + pts[p3].X) * 0.25,
                (pts[minX].Y + pts[maxX].Y + pts[p2].Y + pts[p3].Y) * 0.25,
                (pts[minX].Z + pts[maxX].Z + pts[p2].Z + pts[p3].Z) * 0.25);

            // ---- Step 2: Create initial 4 faces ----
            List<QhFace> faces = new List<QhFace>();
            int[][] tetraFaces = new int[][]
            {
                new int[] { minX, maxX, p2 },
                new int[] { minX, p3, maxX },
                new int[] { minX, p2, p3 },
                new int[] { maxX, p3, p2 }
            };
            foreach (var tf in tetraFaces)
                faces.Add(new QhFace(tf[0], tf[1], tf[2], pts, interior));

            // ---- Step 3: Assign outside points to faces ----
            for (int i = 0; i < n; i++)
            {
                if (i == minX || i == maxX || i == p2 || i == p3) continue;
                foreach (QhFace f in faces)
                {
                    if (f.IsPointOutside(pts[i], pts))
                    {
                        f.Outside.Add(i);
                        break;
                    }
                }
            }

            // ---- Step 4: Iteratively process faces with outside points ----
            var queue = new Queue<QhFace>();
            foreach (QhFace f in faces) queue.Enqueue(f);

            // Safety limit to prevent infinite loops
            int maxIterations = n * 100;
            int iterations = 0;

            while (queue.Count > 0 && iterations < maxIterations)
            {
                iterations++;
                QhFace face = queue.Dequeue();
                if (face.Outside.Count == 0) continue;
                if (!faces.Contains(face)) continue; // Face may have been removed

                int farthest = -1;
                double farthestDist = 1e-10;
                foreach (int pid in face.Outside)
                {
                    double d = face.SignedDistance(pts[pid], pts);
                    if (d > farthestDist) { farthestDist = d; farthest = pid; }
                }
                if (farthest < 0) continue;

                // Find all faces visible from the farthest point
                List<QhFace> visible = new List<QhFace>();
                foreach (QhFace f in faces)
                {
                    if (f.IsVisibleFrom(pts[farthest], pts))
                        visible.Add(f);
                }
                if (visible.Count == 0) continue;

                // Collect orphaned points from visible faces (using HashSet to avoid duplicates)
                HashSet<int> orphans = new HashSet<int>();
                foreach (QhFace vf in visible)
                {
                    foreach (int op in vf.Outside)
                        if (op != farthest) orphans.Add(op);
                }

                // Find horizon edges: edges belonging to exactly one visible face
                List<Tuple<int, int>> horizon = new List<Tuple<int, int>>();
                foreach (QhFace vf in visible)
                {
                    TryAddHorizonEdge(vf.A, vf.B, visible, horizon);
                    TryAddHorizonEdge(vf.B, vf.C, visible, horizon);
                    TryAddHorizonEdge(vf.C, vf.A, visible, horizon);
                }

                // Remove visible faces
                foreach (QhFace vf in visible)
                {
                    faces.Remove(vf);
                    // Also remove from queue if it's still there
                    // (it will be skipped by the faces.Contains check when dequeued)
                }

                // Create new faces from horizon edges to farthest point
                foreach (var edge in horizon)
                {
                    QhFace nf = new QhFace(edge.Item1, edge.Item2, farthest, pts, interior);

                    // Verify: interior point must be on the negative (inside) side
                    // If interior is outside, the face normal points inward -> flip
                    if (nf.IsPointOutside(interior, pts))
                        nf.Flip(pts);

                    // Re-assign orphaned points to the new face
                    foreach (int op in orphans)
                    {
                        if (nf.IsPointOutside(pts[op], pts))
                            nf.Outside.Add(op);
                    }
                    faces.Add(nf);
                    queue.Enqueue(nf);
                }
            }

            // ---- Step 5: Build final mesh ----
            Mesh hull = new Mesh();
            for (int i = 0; i < pts.Count; i++)
                hull.Vertices.Add(pts[i]);
            foreach (QhFace f in faces)
                hull.Faces.AddFace(f.A, f.B, f.C);

            hull.Normals.ComputeNormals();
            hull.Compact();
            return hull;
        }

        private void TryAddHorizonEdge(int a, int b, List<QhFace> visibleFaces, List<Tuple<int, int>> horizon)
        {
            int visibleCount = 0;
            foreach (QhFace f in visibleFaces)
            {
                if (f.HasEdge(a, b)) visibleCount++;
            }
            if (visibleCount != 1) return;

            foreach (var he in horizon)
            {
                if ((he.Item1 == a && he.Item2 == b) || (he.Item1 == b && he.Item2 == a))
                    return;
            }
            horizon.Add(Tuple.Create(a, b));
        }

        /// <summary>
        /// QuickHull face with guaranteed outward-pointing normal.
        /// </summary>
        private class QhFace
        {
            public int A, B, C;
            public List<int> Outside;
            private Plane _plane;

            public QhFace(int a, int b, int c, List<Point3d> pts, Point3d interior)
            {
                A = a; B = b; C = c;
                Outside = new List<int>();

                // Create plane and ensure normal points outward (away from interior)
                _plane = new Plane(pts[A], pts[B], pts[C]);
                Vector3d toInterior = interior - pts[A];
                if (toInterior * _plane.Normal > 0)
                {
                    // Normal points toward interior -> flip
                    int tmp = B; B = C; C = tmp;
                    _plane = new Plane(pts[A], pts[B], pts[C]);
                }
            }

            /// <summary>True if point is on the positive (outside) side of this face.</summary>
            public bool IsPointOutside(Point3d p, List<Point3d> pts)
            {
                return (p - pts[A]) * _plane.Normal > 1e-10;
            }

            /// <summary>
            /// Checks if a point is visible from this face (point is on the positive side).
            /// Used for finding visible faces from a farthest point.
            /// </summary>
            public bool IsVisibleFrom(Point3d p, List<Point3d> pts)
            {
                return (p - pts[A]) * _plane.Normal > 1e-10;
            }

            public double SignedDistance(Point3d p, List<Point3d> pts)
            {
                return (p - pts[A]) * _plane.Normal;
            }

            public bool HasEdge(int a, int b)
            {
                return (A == a && B == b) || (B == a && C == b) || (C == a && A == b) ||
                       (A == b && B == a) || (B == b && C == a) || (C == b && A == a);
            }

            public void Flip(List<Point3d> pts)
            {
                int tmp = B; B = C; C = tmp;
                _plane = new Plane(pts[A], pts[B], pts[C]);
            }
        }

        #endregion

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("8556607F-734E-4D47-9018-2E9A9A6546F6");

        protected override System.Drawing.Bitmap Icon
        {
            get { return Resources.icon_Leaf; }
        }
    }
}
