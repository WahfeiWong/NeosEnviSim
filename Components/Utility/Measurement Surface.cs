using Geometry.Core;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeosUtility
{
    /// <summary>
    /// Brep/Surface to structured mesh with per-face metadata extraction.
    /// Mirrors Ladybug's LB Generate Point Grid with two modes:
    /// - Quad Only = false (default): Rhino gridded meshing that fills to edges
    ///   with both quad and triangulated faces.
    /// - Quad Only = true: Pure quad grid where boundary cells outside the face
    ///   are removed (leaving gaps at irregular edges).
    /// </summary>
    public class BrepToStructuredMesh : GH_Component
    {
        public BrepToStructuredMesh()
          : base("Measurement Surface", "BrepMesh",
              "Convert Brep or Surface faces into meshes with per-face borders, centers and normals. " +
              "Quad Only = false uses Rhino gridded meshing (quad + tri at boundaries). " +
              "Quad Only = true produces pure quads with gaps at irregular edges.",
              "Neos", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Measurement Surface", "MS", "Input Brep or Surface(s)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Resolution", "Res", "Target grid cell size", GH_ParamAccess.item, 1.0);
            pManager.AddBooleanParameter("Quad Only", "QO", "If true, generate only quad faces (gaps at irregular edges). " +
                "If false, use Rhino meshing with quad + triangulated faces filling to edges. Default is false.",
                GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Mesh per input face", GH_ParamAccess.tree);
            pManager.AddCurveParameter("Face Borders", "B", "Border polyline for each mesh face", GH_ParamAccess.tree);
            pManager.AddPointParameter("Face Centers", "Pts", "Center point of each mesh face", GH_ParamAccess.tree);
            pManager.AddVectorParameter("Face Normals", "N", "Unit normal vector of each mesh face", GH_ParamAccess.tree);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Brep> breps = new List<Brep>();
            double resolution = 1.0;
            bool quadOnly = false;

            if (!DA.GetDataList(0, breps)) return;
            if (!DA.GetData(1, ref resolution)) return;
            DA.GetData(2, ref quadOnly);

            if (breps == null || breps.Count == 0) return;
            if (resolution <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Resolution must be > 0.");
                return;
            }

            GH_Structure<GH_Mesh> meshTree = new GH_Structure<GH_Mesh>();
            GH_Structure<GH_Curve> borderTree = new GH_Structure<GH_Curve>();
            GH_Structure<GH_Point> centerTree = new GH_Structure<GH_Point>();
            GH_Structure<GH_Vector> normalTree = new GH_Structure<GH_Vector>();

            int pathIndex = 0;

            foreach (var brep in breps)
            {
                if (brep == null || !brep.IsValid) continue;

                for (int faceIdx = 0; faceIdx < brep.Faces.Count; faceIdx++)
                {
                    var face = brep.Faces[faceIdx];
                    if (!face.IsValid) continue;

                    Mesh mesh = quadOnly
                        ? FaceMesher.CreatePureQuadMesh(face, resolution)
                        : FaceMesher.CreateGriddedMesh(face, resolution);

                    if (mesh == null || mesh.Faces.Count == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                            $"Face {faceIdx}: failed to create mesh. Resolution may be too coarse.");
                        continue;
                    }

                    mesh.Normals.ComputeNormals();
                    mesh.FaceNormals.ComputeFaceNormals();

                    GH_Path path = new GH_Path(pathIndex);

                    meshTree.Append(new GH_Mesh(mesh), path);

                    for (int i = 0; i < mesh.Faces.Count; i++)
                    {
                        Point3d center = FaceExtractor.GetCenter(mesh, i);
                        Vector3d normal = mesh.FaceNormals.Count > i
                            ? (Vector3d)mesh.FaceNormals[i]
                            : Vector3d.ZAxis;
                        normal.Unitize();

                        Polyline border = FaceExtractor.GetBorder(mesh, i);

                        borderTree.Append(new GH_Curve(border.ToNurbsCurve()), path);
                        centerTree.Append(new GH_Point(center), path);
                        normalTree.Append(new GH_Vector(normal), path);
                    }

                    pathIndex++;
                }
            }

            DA.SetDataTree(0, meshTree);
            DA.SetDataTree(1, borderTree);
            DA.SetDataTree(2, centerTree);
            DA.SetDataTree(3, normalTree);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;
        public override Guid ComponentGuid => new Guid("4617F675-11C2-4D72-B649-FE12F7C2E961");

        protected override System.Drawing.Bitmap Icon
        {
            get { return Resources.icon_measurementSurface; }
        }
    }
}
