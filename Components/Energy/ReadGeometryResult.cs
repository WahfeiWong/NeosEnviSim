using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using SolarPV.Core;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace SolarPV
{
    /// <summary>
    /// Reads GeometryResult.txt from NeosRadSim and outputs geometry data.
    /// </summary>
    public class ReadGeometryResultComponent : GH_Component
    {
        public ReadGeometryResultComponent()
          : base("Read Geometry Result", "ReadGeo",
              "Reads GeometryResult.txt from NeosRadSim and outputs mesh, face centers, normals, areas, tilt angles, and sun vectors.",
              "Neos", "RadSim")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Geometry File Path", "GeoPath", "File path to GeometryResult.txt", GH_ParamAccess.item);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Meshes", "M", "Reconstructed PV panel meshes", GH_ParamAccess.list);
            pManager.AddPointParameter("Face Centers", "C", "Center point of each mesh face, grouped by panel", GH_ParamAccess.tree);
            pManager.AddVectorParameter("Face Normals", "N", "Normal vector of each mesh face, grouped by panel", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Face Areas", "A", "Area of each mesh face in m2, grouped by panel", GH_ParamAccess.tree);
            pManager.AddNumberParameter("Tilt Angles", "T", "Tilt angle of each face in degrees, grouped by panel", GH_ParamAccess.tree);
            pManager.AddVectorParameter("Sun Vectors", "SunVec", "Sun vectors for each analyzed hour", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string filePath = "";
            if (!DA.GetData(0, ref filePath)) return;

            var result = ResultFileParser.LoadGeometryResult(filePath);
            if (result == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to load geometry result file.");
                return;
            }

            // Reconstruct meshes
            var meshes = new List<Mesh>();
            foreach (var mdata in result.Meshes)
            {
                var mesh = new Mesh();
                foreach (var vc in mdata.VertexCoordinates)
                    mesh.Vertices.Add(vc[0], vc[1], vc[2]);
                foreach (var fi in mdata.FaceIndices)
                {
                    if (fi[3] >= 0 && fi[3] < mesh.Vertices.Count)
                        mesh.Faces.AddFace(fi[0], fi[1], fi[2], fi[3]);
                    else
                        mesh.Faces.AddFace(fi[0], fi[1], fi[2]);
                }
                mesh.Normals.ComputeNormals();
                mesh.FaceNormals.ComputeFaceNormals();
                mesh.Compact();
                meshes.Add(mesh);
            }

            // Face centers tree
            GH_Structure<GH_Point> centerTree = new GH_Structure<GH_Point>();
            for (int p = 0; p < result.FaceCenters.Count; p++)
                for (int f = 0; f < result.FaceCenters[p].Count; f++)
                    centerTree.Append(new GH_Point(result.FaceCenters[p][f]), new GH_Path(p));

            // Face normals tree
            GH_Structure<GH_Vector> normalTree = new GH_Structure<GH_Vector>();
            for (int p = 0; p < result.FaceNormals.Count; p++)
                for (int f = 0; f < result.FaceNormals[p].Count; f++)
                    normalTree.Append(new GH_Vector(result.FaceNormals[p][f]), new GH_Path(p));

            // Face areas tree
            GH_Structure<GH_Number> areaTree = new GH_Structure<GH_Number>();
            for (int p = 0; p < result.FaceAreas.Count; p++)
                for (int f = 0; f < result.FaceAreas[p].Count; f++)
                    areaTree.Append(new GH_Number(result.FaceAreas[p][f]), new GH_Path(p));

            // Tilt angles tree
            GH_Structure<GH_Number> tiltTree = new GH_Structure<GH_Number>();
            for (int p = 0; p < result.TiltAngles.Count; p++)
                for (int f = 0; f < result.TiltAngles[p].Count; f++)
                    tiltTree.Append(new GH_Number(result.TiltAngles[p][f]), new GH_Path(p));

            DA.SetDataList(0, meshes);
            DA.SetDataTree(1, centerTree);
            DA.SetDataTree(2, normalTree);
            DA.SetDataTree(3, areaTree);
            DA.SetDataTree(4, tiltTree);
            DA.SetDataList(5, result.SunVectors);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;
        protected override System.Drawing.Bitmap Icon => Resources.icon_ReadGeo;
        public override Guid ComponentGuid => new Guid("0EF4C49A-7924-4C4B-8C83-CB6FF382B778");
    }
}
