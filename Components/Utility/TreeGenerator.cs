using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace NeosUtility
{
    /// <summary>
    /// Recursively generated tree with branch details.
    /// Based on molab.eu tutorial "Recursively Generated Tree".
    /// Inputs DG and LG now accept number lists (e.g., from Graph Mapper + Range).
    /// </summary>
    public class RecursiveTreeComponent : GH_Component
    {
        public RecursiveTreeComponent()
          : base("Recursive Tree", "Tree",
              "Generate a recursive tree with branch lines and mesh output. " +
              "Input DG and LG as number lists (one value per depth level).",
              "Neos", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // from: root point of the entire tree
            pManager.AddPointParameter("Trunk Start", "S", "Root point of the tree. Determines where the trunk begins and the direction reference for all branch orientations.", GH_ParamAccess.item);
            // to: top point of the initial trunk
            pManager.AddPointParameter("Trunk End", "E", "Top point of the initial trunk. Determines the trunk's direction and length. For a vertical tree, use the same X,Y as Start with a higher Z.", GH_ParamAccess.item);
            // radiusModifier: controls the spread angle of child branches (tan(angle))
            pManager.AddNumberParameter("Branch Spread", "R", "Controls the branching angle (tan theta). R=0.03 gives ~1.7deg (narrow), R=0.3 gives ~16.7deg (wide), R=1.0 gives 45deg (max). Values above 1.0 may produce artifacts.", GH_ParamAccess.item, 0.03);
            // maxDepth: recursion depth
            pManager.AddIntegerParameter("Recursion Depth", "D", "Maximum recursion depth. D=1 generates the trunk plus one level of child branches. D=4 generates 4 levels. Higher values produce richer trees but increase computation exponentially. Use with caution.", GH_ParamAccess.item, 4);
            // division: maximum branches per division point
            pManager.AddIntegerParameter("Max Branches", "B", "Maximum number of child branches created at each division point. Actual count = Round(B * DG[depth]). Also affects computation cost exponentially.", GH_ParamAccess.item, 3);
            // divisionGraph: probability of continuing to divide at each depth
            pManager.AddNumberParameter("Division Graph", "DG", "Probability of continuing to branch at each depth level (0-1). Index 0 controls the trunk (should be 1.0). Use a decreasing sequence: high at top, low at bottom. Length should equal D.", GH_ParamAccess.list);
            // branchLengthGraph: length multiplier at each depth
            pManager.AddNumberParameter("Length Graph", "LG", "Branch length multiplier at each depth level. Index 0 controls trunk length factor (typically 1.0). Use a decreasing sequence for shorter branches at deeper levels. Length should equal D.", GH_ParamAccess.list);
            // TR: trunk radius — base radius of the main trunk
            pManager.AddNumberParameter("Trunk Radius", "TR", "Radius of the main trunk (depth=0). Each subsequent level of branches is reduced by the Radius Decay factor. E.g., TR=0.05 with RD=0.6 gives: trunk=0.05, 1st branches=0.03, 2nd=0.018, etc.", GH_ParamAccess.item, 0.05);
            // RD: radius decay per depth level
            pManager.AddNumberParameter("Radius Decay", "RD", "Branch radius decay factor per depth level (0-1). Each level's radius = previous * RD. RD=0.6 gives strong tapering, RD=0.8 gives gentle tapering.", GH_ParamAccess.item, 0.6);
            // PS: pipe segments for mesh output
            pManager.AddIntegerParameter("Pipe Segments", "PS", "Number of segments around the pipe circumference for mesh output. Higher values produce smoother pipes but more faces.", GH_ParamAccess.item, 6);
            // Seed: random seed for reproducible trees
            pManager.AddIntegerParameter("Seed", "Sd", "Random seed for reproducible tree generation. Seed >= 0 produces deterministic results (same seed = same tree). Seed < 0 produces random trees each time.", GH_ParamAccess.item, 0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Tree Mesh", "M", "3D mesh of the tree with piped branches", GH_ParamAccess.item);
            pManager.AddCurveParameter("Branch Lines", "L", "All branch lines of the tree", GH_ParamAccess.list);
        }

        /// <summary>
        /// Internal structure to track each branch line with its depth level.
        /// </summary>
        private struct BranchInfo
        {
            public LineCurve Line;
            public int Depth;

            public BranchInfo(LineCurve line, int depth)
            {
                Line = line;
                Depth = depth;
            }
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // --- Get Inputs ---
            Point3d startPoint = Point3d.Unset;
            Point3d endPoint = Point3d.Unset;
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
            if (!DA.GetData(1, ref endPoint)) return;
            if (!DA.GetData(2, ref radiusModifier)) return;
            if (!DA.GetData(3, ref maxDepth)) return;
            if (!DA.GetData(4, ref divisionBase)) return;
            if (!DA.GetDataList(5, divisionProbs)) return;
            if (!DA.GetDataList(6, lengthMods)) return;
            if (!DA.GetData(7, ref trunkRadius)) return;
            if (!DA.GetData(8, ref radiusDecay)) return;
            if (!DA.GetData(9, ref pipeSegments)) return;
            if (!DA.GetData(10, ref seed)) return;

            // Validate inputs
            if (maxDepth < 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Recursion Depth (D) must be >= 0.");
                return;
            }
            if (divisionBase < 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Max Branches (B) must be >= 1.");
                return;
            }
            if (trunkRadius <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Trunk Radius (TR) must be > 0.");
                return;
            }
            if (radiusDecay <= 0 || radiusDecay > 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Radius Decay (RD) must be in range (0, 1].");
                return;
            }
            if (pipeSegments < 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Pipe Segments (PS) must be >= 3.");
                return;
            }

            // Ensure lists are long enough; if too short, fill with defaults.
            // divisionGraph[depth] is accessed for depth = 0 to maxDepth-1, so maxDepth items needed.
            int requiredLength = maxDepth;
            divisionProbs = EnsureListLength(divisionProbs, requiredLength, 0.5);
            lengthMods = EnsureListLength(lengthMods, requiredLength, 1.0);

            // Clamp probabilities to [0,1]
            for (int i = 0; i < divisionProbs.Count; i++)
            {
                divisionProbs[i] = Math.Max(0, Math.Min(1, divisionProbs[i]));
            }
            // Ensure length modifiers are positive
            for (int i = 0; i < lengthMods.Count; i++)
            {
                lengthMods[i] = Math.Max(0.01, lengthMods[i]);
            }

            // --- Generate tree ---
            List<BranchInfo> allBranches = new List<BranchInfo>();

            // Create initial trunk
            LineCurve trunk = new LineCurve(startPoint, endPoint);

            // Create a single Random instance to be reused throughout recursion.
            // If seed >= 0, use it for deterministic (reproducible) results.
            // If seed < 0, use a random seed for varied results each run.
            Random random;
            if (seed >= 0)
                random = new Random(seed);
            else
                random = new Random(Guid.NewGuid().GetHashCode());

            // Start recursive generation from depth 0
            // Following the original molab.eu tutorial logic:
            // trunk is passed as 'rodic' (parent branch) and will be added to output
            // inside the recursive function when division conditions are met.
            RecursiveBranch(
                depth: 0,
                maxDepth: maxDepth,
                parentBranch: trunk,
                radiusModifier: radiusModifier,
                moverModifier: 1.0,
                allBranches: allBranches,
                treeBasePoint: startPoint,
                division: divisionBase,
                divisionGraph: divisionProbs,
                branchLengthGraph: lengthMods,
                random: random);

            // --- Build pipe mesh from branch lines ---
            Mesh treeMesh = new Mesh();
            List<LineCurve> branchLines = new List<LineCurve>();

            foreach (BranchInfo branchInfo in allBranches)
            {
                LineCurve line = branchInfo.Line;
                int depth = branchInfo.Depth;
                branchLines.Add(line);

                // Calculate radius for this branch using exponential decay based on depth
                // radius = trunkRadius * (radiusDecay ^ depth)
                // depth=0 (trunk): radius = trunkRadius
                // depth=1: radius = trunkRadius * radiusDecay
                // depth=2: radius = trunkRadius * radiusDecay^2
                double branchRadius = trunkRadius * Math.Pow(radiusDecay, depth);
                branchRadius = Math.Max(0.001, branchRadius);

                Mesh branchMesh = Mesh.CreateFromCurvePipe(
                    line,
                    branchRadius,
                    pipeSegments,
                    1,
                    MeshPipeCapStyle.Flat,
                    false,
                    null
                );

                if (branchMesh != null && branchMesh.Faces.Count > 0)
                {
                    treeMesh.Append(branchMesh);
                }
            }

            treeMesh.Normals.ComputeNormals();
            treeMesh.Compact();

            // --- Outputs ---
            DA.SetData(0, treeMesh);
            DA.SetDataList(1, branchLines);
        }

        /// <summary>
        /// Ensures a list has at least requiredLength items, filling missing with defaultValue.
        /// </summary>
        private List<double> EnsureListLength(List<double> list, int requiredLength, double defaultValue)
        {
            if (list == null) list = new List<double>();
            while (list.Count < requiredLength)
                list.Add(defaultValue);
            return list;
        }

        /// <summary>
        /// Recursive function that generates branches.
        /// Faithfully follows the original molab.eu tutorial algorithm.
        /// </summary>
        private void RecursiveBranch(
            int depth,
            int maxDepth,
            LineCurve parentBranch,
            double radiusModifier,
            double moverModifier,
            List<BranchInfo> allBranches,
            Point3d treeBasePoint,
            int division,
            List<double> divisionGraph,
            List<double> branchLengthGraph,
            Random random)
        {
            // Check if we are within the maximal depth
            if (depth >= maxDepth) return;

            // Generate random number to decide whether this branch continues dividing
            double randomer = random.NextDouble();

            // Check probability from division graph for current depth
            if (randomer > divisionGraph[depth]) return;

            // Get branch length modifier from graph for current depth
            double branchLengthModifier = branchLengthGraph[depth] * moverModifier;

            // --- Core geometry calculation (following original tutorial exactly) ---

            // Create vector from the previous branch (parent branch)
            // Note: This is Start - End (reverse direction), NOT End - Start
            Vector3d branch = new Vector3d(
                parentBranch.PointAtStart.X - parentBranch.PointAtEnd.X,
                parentBranch.PointAtStart.Y - parentBranch.PointAtEnd.Y,
                parentBranch.PointAtStart.Z - parentBranch.PointAtEnd.Z);

            // Variable radius helps create the new branches of the right size
            double radius = radiusModifier * branch.Length;

            // to = endpoint of parent branch (start of new branches)
            Point3d to = new Point3d(
                parentBranch.PointAtEnd.X,
                parentBranch.PointAtEnd.Y,
                parentBranch.PointAtEnd.Z);

            // New plane perpendicular to parent branch and going through the ending point
            Plane aimPlane = new Plane(to, branch);

            // Makes circle on the plane with the defined radius
            Circle cil = new Circle(aimPlane, radius * branchLengthModifier);

            // Creates a point on the circle which is closest to the bottom of the whole tree
            Point3d spodni = cil.ClosestPoint(treeBasePoint);

            // Add the parent branch to the output list with its depth
            allBranches.Add(new BranchInfo(parentBranch, depth));

            // Create the first child branch (continuation branch / vetev1)
            // From the endpoint of parent branch to the new point
            LineCurve vetev1 = new LineCurve(to, new Point3d(
                spodni.X - branch.X * branchLengthModifier,
                spodni.Y - branch.Y * branchLengthModifier,
                spodni.Z - branch.Z * branchLengthModifier));

            // Variable containing the amount of new divisions based on the graph parameter
            double newDivision = division * divisionGraph[depth];
            int divisionCount = (int)Math.Max(1, Math.Round(newDivision));

            // Starts cycle running repeatedly based on how many divisions we now need
            for (int i = 0; i < divisionCount; i++)
            {
                LineCurve vetev2 = new LineCurve(vetev1);

                // Rotate this branch around the parent branch by an angle of 360 / divisionCount
                vetev2.Rotate(
                    (i * 2 * Math.PI / divisionCount),
                    branch,
                    to);

                // Run the function again recursively with increased depth
                RecursiveBranch(
                    depth + 1,
                    maxDepth,
                    vetev2,
                    radiusModifier,
                    moverModifier,
                    allBranches,
                    treeBasePoint,
                    division,
                    divisionGraph,
                    branchLengthGraph,
                    random);
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
