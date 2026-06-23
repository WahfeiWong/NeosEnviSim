using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace NeosUtility
{
    /// <summary>
    /// Recursively generated tree with branch lines output.
    /// Based on molab.eu tutorial "Recursively Generated Tree".
    /// </summary>
    public class RecursiveTreeComponent : GH_Component
    {
        public RecursiveTreeComponent()
          : base("Tree Generator", "Tree",
              "Generate a recursive tree with branch mesh and branch lines output. " +
              "Inputs DG and LG accept number lists (e.g., from Graph Mapper + Range).",
              "Neos", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Trunk Start", "S", "Root point of the tree.", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Trunk Height", "H", "Height of the tree trunk. Trunk End = Start + (0,0,H).", GH_ParamAccess.item, 3.0);
            pManager.AddNumberParameter("Branch Spread", "R", "Controls branching angle (tan theta).", GH_ParamAccess.item, 0.6);
            pManager.AddIntegerParameter("Recursion Depth", "D", "Maximum recursion depth.", GH_ParamAccess.item, 3);
            pManager.AddIntegerParameter("Max Branches", "B", "Max child branches at each division point.", GH_ParamAccess.item, 5);
            pManager.AddNumberParameter("Division Graph", "DG", "Branching probability per depth level (0-1).", GH_ParamAccess.list);
            pManager.AddNumberParameter("Length Graph", "LG", "Length multiplier per depth level.", GH_ParamAccess.list);
            pManager.AddNumberParameter("Trunk Radius", "TR", "Main trunk radius.", GH_ParamAccess.item, 0.1);
            pManager.AddNumberParameter("Radius Decay", "RD", "Radius decay factor per depth (0-1).", GH_ParamAccess.item, 0.7);
            pManager.AddIntegerParameter("Pipe Segments", "PS", "Pipe circumference segments.", GH_ParamAccess.item, 6);
            pManager.AddIntegerParameter("Seed", "Sd", "Random seed for reproducibility.", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Tree Mesh", "TM", "3D mesh with piped branches", GH_ParamAccess.item);
            pManager.AddCurveParameter("Branch Lines", "BL", "All branch lines", GH_ParamAccess.list);
        }

        private struct BranchInfo
        {
            public LineCurve Line;
            public int Depth;
            public BranchInfo(LineCurve line, int depth) { Line = line; Depth = depth; }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Point3d startPoint = Point3d.Unset;
            double treeHeight = 5.0;
            double radiusModifier = 0.03;
            int maxDepth = 4;
            int divisionBase = 3;
            List<double> divisionProbs = new List<double>();
            List<double> lengthMods = new List<double>();
            double trunkRadius = 0.05;
            double radiusDecay = 0.6;
            int pipeSegments = 6;
            int seed = -1;

            if (!DA.GetData(0, ref startPoint)) return;
            if (!DA.GetData(1, ref treeHeight)) return;
            if (!DA.GetData(2, ref radiusModifier)) return;
            if (!DA.GetData(3, ref maxDepth)) return;
            if (!DA.GetData(4, ref divisionBase)) return;
            if (!DA.GetDataList(5, divisionProbs)) return;
            if (!DA.GetDataList(6, lengthMods)) return;
            if (!DA.GetData(7, ref trunkRadius)) return;
            if (!DA.GetData(8, ref radiusDecay)) return;
            if (!DA.GetData(9, ref pipeSegments)) return;
            if (!DA.GetData(10, ref seed)) return;

            if (treeHeight <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Tree Height (H) must be > 0."); return; }
            if (maxDepth < 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Recursion Depth (D) must be >= 0."); return; }
            if (divisionBase < 1) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Max Branches (B) must be >= 1."); return; }
            if (trunkRadius <= 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Trunk Radius (TR) must be > 0."); return; }
            if (radiusDecay <= 0 || radiusDecay > 1) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Radius Decay (RD) must be in (0, 1]."); return; }
            if (pipeSegments < 3) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Pipe Segments (PS) must be >= 3."); return; }

            Point3d endPoint = new Point3d(startPoint.X, startPoint.Y, startPoint.Z + treeHeight);

            int requiredLength = maxDepth;
            divisionProbs = EnsureListLength(divisionProbs, requiredLength, 0.5);
            lengthMods = EnsureListLength(lengthMods, requiredLength, 1.0);
            for (int i = 0; i < divisionProbs.Count; i++) divisionProbs[i] = Math.Max(0, Math.Min(1, divisionProbs[i]));
            for (int i = 0; i < lengthMods.Count; i++) lengthMods[i] = Math.Max(0.01, lengthMods[i]);

            List<BranchInfo> allBranches = new List<BranchInfo>();
            LineCurve trunk = new LineCurve(startPoint, endPoint);
            Random random = (seed >= 0) ? new Random(seed) : new Random(Guid.NewGuid().GetHashCode());

            RecursiveBranch(0, maxDepth, trunk, radiusModifier, 1.0, allBranches, startPoint, divisionBase, divisionProbs, lengthMods, random);

            Mesh treeMesh = new Mesh();
            List<LineCurve> branchLines = new List<LineCurve>();

            foreach (BranchInfo bi in allBranches)
            {
                branchLines.Add(bi.Line);
                double branchRadius = trunkRadius * Math.Pow(radiusDecay, bi.Depth);
                branchRadius = Math.Max(0.001, branchRadius);
                Mesh bm = Mesh.CreateFromCurvePipe(bi.Line, branchRadius, pipeSegments, 1, MeshPipeCapStyle.Flat, false, null);
                if (bm != null && bm.Faces.Count > 0) treeMesh.Append(bm);
            }

            treeMesh.Normals.ComputeNormals();
            treeMesh.Compact();

            DA.SetData(0, treeMesh);
            DA.SetDataList(1, branchLines);
        }

        private List<double> EnsureListLength(List<double> list, int requiredLength, double defaultValue)
        {
            if (list == null) list = new List<double>();
            while (list.Count < requiredLength) list.Add(defaultValue);
            return list;
        }

        private void RecursiveBranch(int depth, int maxDepth, LineCurve parentBranch,
            double radiusModifier, double moverModifier, List<BranchInfo> allBranches,
            Point3d treeBasePoint, int division, List<double> divisionGraph,
            List<double> branchLengthGraph, Random random)
        {
            if (depth >= maxDepth) return;
            if (random.NextDouble() > divisionGraph[depth]) return;

            double branchLengthModifier = branchLengthGraph[depth] * moverModifier;

            Vector3d branch = new Vector3d(
                parentBranch.PointAtStart.X - parentBranch.PointAtEnd.X,
                parentBranch.PointAtStart.Y - parentBranch.PointAtEnd.Y,
                parentBranch.PointAtStart.Z - parentBranch.PointAtEnd.Z);

            double radius = radiusModifier * branch.Length;
            Point3d to = parentBranch.PointAtEnd;
            Plane aimPlane = new Plane(to, branch);
            Circle cil = new Circle(aimPlane, radius * branchLengthModifier);
            Point3d spodni = cil.ClosestPoint(treeBasePoint);

            allBranches.Add(new BranchInfo(parentBranch, depth));

            LineCurve vetev1 = new LineCurve(to, new Point3d(
                spodni.X - branch.X * branchLengthModifier,
                spodni.Y - branch.Y * branchLengthModifier,
                spodni.Z - branch.Z * branchLengthModifier));

            int divisionCount = (int)Math.Max(1, Math.Round(division * divisionGraph[depth]));

            for (int i = 0; i < divisionCount; i++)
            {
                LineCurve vetev2 = new LineCurve(vetev1);
                vetev2.Rotate(i * 2 * Math.PI / divisionCount, branch, to);
                RecursiveBranch(depth + 1, maxDepth, vetev2, radiusModifier, moverModifier,
                    allBranches, treeBasePoint, division, divisionGraph, branchLengthGraph, random);
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.primary;
        public override Guid ComponentGuid => new Guid("3781D9F5-A262-4A07-A94E-368D73A0DD07");

        protected override System.Drawing.Bitmap Icon
        {
            get { return Resources.icon_TreeGen; }
        }
    }
}
