//using System;
//using System.Collections.Generic;
//using System.Linq;
//using Rhino.Geometry;
//using Grasshopper.Kernel;
//using Grasshopper.Kernel.Types;
//using NeosEnviSim.Properties;

//namespace NeosUtility
//{
//    public class TreeGeometryProcessor : GH_Component
//    {
//        /// <summary>
//        /// 构造函数，初始化组件
//        /// </summary>
//        public TreeGeometryProcessor()
//          : base("Tree Processor", "TreeProc",
//              "Process tree geometry for CFD microclimate simulation",
//              "Neos", "Utility")
//        {
//        }

//        /// <summary>
//        /// 注册输入参数
//        /// </summary>
//        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
//        {
//            pManager.AddMeshParameter("Tree Model", "T", "Detailed tree geometry (one or more meshes)", GH_ParamAccess.list);
//            pManager.AddNumberParameter("Height", "H", "Tree height", GH_ParamAccess.item);
//            pManager.AddNumberParameter("Crown Radius", "R", "Crown radius", GH_ParamAccess.item);
//            pManager.AddPointParameter("Plant Point", "P", "Planting point (default origin)", GH_ParamAccess.item, Point3d.Origin);
//            pManager.AddNumberParameter("Edge Length", "L", "Grid spacing for shrink wrap mesh (TargetEdgeLength), controls mesh resolution", GH_ParamAccess.item, 1.0);
//            pManager.AddBooleanParameter("ShrinkWrap", "SW", "If true, use ShrinkWrap to generate crown mesh; if false, use Convex Hull", GH_ParamAccess.item, true);
//        }

//        /// <summary>
//        /// 注册输出参数
//        /// </summary>
//        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
//        {
//            pManager.AddMeshParameter("Projection Mesh", "PM", "Crown projection mesh (planar region(s) of shadow outline)", GH_ParamAccess.item);
//            pManager.AddNumberParameter("Crown Volume", "CV", "Crown volume (based on shrink wrap or convex hull)", GH_ParamAccess.item);
//            pManager.AddNumberParameter("Projection Area", "PA", "Crown projection area (area of projection mesh)", GH_ParamAccess.item);
//            pManager.AddNumberParameter("Leaf Area", "LA", "Total leaf area (sum of all mesh areas)", GH_ParamAccess.item);
//            pManager.AddNumberParameter("LAI", "LAI", "Leaf Area Index (Leaf Area / Projection Area)", GH_ParamAccess.item);
//            pManager.AddNumberParameter("LAD", "LAD", "Leaf Area Density (Leaf Area / Crown Volume)", GH_ParamAccess.item);
//            pManager.AddMeshParameter("Tree Detail", "TreeDet", "Scaled detailed tree model (merged meshes)", GH_ParamAccess.item);
//            pManager.AddMeshParameter("Tree Canopy", "TreeCan", "Simplified canopy model (generated using Mesh.ShrinkWrap or Convex Hull)", GH_ParamAccess.item);
//        }

//        /// <summary>
//        /// 解决问题的主逻辑
//        /// </summary>
//        protected override void SolveInstance(IGH_DataAccess DA)
//        {
//            // 获取输入数据
//            List<GH_Mesh> ghMeshes = new List<GH_Mesh>();
//            double height = 10.0;
//            double crownRadius = 5.0;
//            Point3d plantPoint = Point3d.Origin;
//            double shrinkEdgeLen = 0.1;
//            bool useShrinkWrap = true;

//            if (!DA.GetDataList(0, ghMeshes)) return;
//            if (!DA.GetData(1, ref height)) return;
//            if (!DA.GetData(2, ref crownRadius)) return;
//            if (!DA.GetData(3, ref plantPoint)) return;
//            if (!DA.GetData(4, ref shrinkEdgeLen)) return;
//            if (!DA.GetData(5, ref useShrinkWrap)) return;

//            // 将GH_Mesh转换为Rhino Mesh列表
//            List<Mesh> meshes = new List<Mesh>();
//            foreach (var ghMesh in ghMeshes)
//            {
//                if (ghMesh != null && ghMesh.Value != null)
//                    meshes.Add(ghMesh.Value.DuplicateMesh()); // 复制以免修改原数据
//            }

//            if (meshes.Count == 0)
//            {
//                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid meshes provided.");
//                return;
//            }

//            // 1. 计算原始包围盒
//            BoundingBox bboxOriginal = meshes[0].GetBoundingBox(true);
//            foreach (var mesh in meshes.Skip(1))
//                bboxOriginal.Union(mesh.GetBoundingBox(true));

//            // 2. 计算缩放因子
//            double originalWidth = bboxOriginal.Max.X - bboxOriginal.Min.X;
//            double originalDepth = bboxOriginal.Max.Y - bboxOriginal.Min.Y;
//            double originalHeight = bboxOriginal.Max.Z - bboxOriginal.Min.Z;

//            double scaleX = (originalWidth > 0) ? (2 * crownRadius) / originalWidth : 1.0;
//            double scaleY = (originalDepth > 0) ? (2 * crownRadius) / originalDepth : 1.0;
//            double scaleZ = (originalHeight > 0) ? height / originalHeight : 1.0;

//            // 3. 以原始包围盒中心进行非均匀缩放
//            Point3d centerOriginal = bboxOriginal.Center;
//            Plane planeAtCenter = new Plane(centerOriginal, Vector3d.ZAxis);
//            Transform scaleTransform = Transform.Scale(planeAtCenter, scaleX, scaleY, scaleZ);

//            foreach (var mesh in meshes)
//                mesh.Transform(scaleTransform);

//            // 4. 计算缩放后的包围盒
//            BoundingBox bboxScaled = meshes[0].GetBoundingBox(true);
//            foreach (var mesh in meshes.Skip(1))
//                bboxScaled.Union(mesh.GetBoundingBox(true));

//            // 5. 计算底面中心并移动至种植点
//            Point3d bottomCenter = new Point3d(bboxScaled.Center.X, bboxScaled.Center.Y, bboxScaled.Min.Z);
//            Vector3d moveVector = plantPoint - bottomCenter;
//            foreach (var mesh in meshes)
//                mesh.Translate(moveVector);

//            // 更新缩放后的包围盒（移动后）
//            bboxScaled = meshes[0].GetBoundingBox(true);
//            foreach (var mesh in meshes.Skip(1))
//                bboxScaled.Union(mesh.GetBoundingBox(true));

//            // 6. 计算树叶总面积（使用 AreaMassProperties）
//            double leafArea = 0.0;
//            foreach (var mesh in meshes)
//            {
//                AreaMassProperties amp = AreaMassProperties.Compute(mesh);
//                if (amp != null)
//                    leafArea += Math.Abs(amp.Area);
//            }

//            // 7. 计算树冠投影网格和投影面积（稳健方法）
//            Mesh projectionMesh = null;
//            double projectionArea = 0.0;
//            ComputeProjectionMesh(meshes, ref projectionMesh, ref projectionArea);

//            // 8. 生成树冠网格（根据用户选择）
//            Mesh crownWrapMesh = null;
//            double crownVolume = 0.0;

//            if (useShrinkWrap)
//            {
//                // 使用 ShrinkWrap
//                var shrinkParams = new ShrinkWrapParameters();
//                shrinkParams.TargetEdgeLength = shrinkEdgeLen;

//                try
//                {
//                    // 尝试使用 ShrinkWrap (Rhino 7+)
//                    crownWrapMesh = Mesh.ShrinkWrap(meshes, shrinkParams);
//                }
//                catch (MissingMethodException)
//                {
//                    // 当前 Rhino 版本不支持 ShrinkWrap，回退到凸包
//                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
//                        "Mesh.ShrinkWrap method not available in current Rhino version. Falling back to convex hull.");
//                    crownWrapMesh = CreateConvexHullFallback(meshes);
//                }
//                catch (Exception ex)
//                {
//                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Error creating shrink wrap mesh: " + ex.Message);
//                    // 尝试回退到凸包
//                    crownWrapMesh = CreateConvexHullFallback(meshes);
//                }
//            }
//            else
//            {
//                // 用户选择凸包
//                crownWrapMesh = CreateConvexHullFallback(meshes);
//            }

//            if (crownWrapMesh != null && crownWrapMesh.IsValid)
//            {
//                VolumeMassProperties vmp = VolumeMassProperties.Compute(crownWrapMesh);
//                crownVolume = vmp != null ? vmp.Volume : 0.0;
//            }

//            // 9. 计算LAI和LAD
//            double lai = (projectionArea > 0) ? leafArea / projectionArea : 0;
//            double lad = (crownVolume > 0) ? leafArea / crownVolume : 0;

//            // 10. 合并缩放后的精细模型为一个Mesh（方便输出）
//            Mesh scaledTreeMesh = new Mesh();
//            foreach (var mesh in meshes)
//                scaledTreeMesh.Append(mesh);

//            // 11. 设置输出
//            DA.SetData(0, projectionMesh);
//            DA.SetData(1, crownVolume);
//            DA.SetData(2, projectionArea);
//            DA.SetData(3, leafArea);
//            DA.SetData(4, lai);
//            DA.SetData(5, lad);
//            DA.SetData(6, scaledTreeMesh);
//            DA.SetData(7, crownWrapMesh);
//        }

//        /// <summary>
//        /// 稳健的投影面积计算方法：将所有网格面投影至XY平面，合并区域，提取轮廓线，生成平面网格并计算面积。
//        /// </summary>
//        private void ComputeProjectionMesh(List<Mesh> meshes, ref Mesh outMesh, ref double outArea)
//        {
//            // 1. 将所有网格投影到XY平面
//            Mesh totalProjMesh = new Mesh();
//            foreach (var mesh in meshes)
//            {
//                Mesh projMesh = mesh.DuplicateMesh();
//                for (int i = 0; i < projMesh.Vertices.Count; i++)
//                {
//                    Point3f pt = projMesh.Vertices[i];
//                    pt.Z = 0;
//                    projMesh.Vertices.SetVertex(i, pt);
//                }
//                totalProjMesh.Append(projMesh);
//            }

//            if (totalProjMesh.Faces.Count == 0)
//            {
//                outMesh = null;
//                outArea = 0;
//                return;
//            }

//            // 2. 提取裸边
//            Polyline[] nakedEdges = totalProjMesh.GetNakedEdges();
//            if (nakedEdges.Length == 0)
//            {
//                outMesh = null;
//                outArea = 0;
//                return;
//            }

//            // 3. 将每条裸边转换为 PolylineCurve
//            List<Curve> edgeCurves = new List<Curve>();
//            foreach (Polyline pl in nakedEdges)
//            {
//                if (pl.Count >= 2)
//                    edgeCurves.Add(new PolylineCurve(pl));
//            }

//            // 4. 连接相邻曲线，形成闭合轮廓
//            double tolerance = 0.01;
//            Curve[] joinedCurves = Curve.JoinCurves(edgeCurves, tolerance);
//            if (joinedCurves == null || joinedCurves.Length == 0)
//            {
//                outMesh = null;
//                outArea = 0;
//                return;
//            }

//            // 5. 筛选闭合曲线
//            List<Curve> closedCurves = new List<Curve>();
//            foreach (Curve crv in joinedCurves)
//                if (crv.IsClosed) closedCurves.Add(crv);

//            if (closedCurves.Count == 0)
//            {
//                outMesh = null;
//                outArea = 0;
//                return;
//            }

//            // 6. 生成平面 Brep（自动处理内外环）
//            Brep[] planarBreps = Brep.CreatePlanarBreps(closedCurves, tolerance);
//            if (planarBreps == null || planarBreps.Length == 0)
//            {
//                outMesh = null;
//                outArea = 0;
//                return;
//            }

//            // 7. 将每个 Brep 转为网格并合并
//            List<Mesh> regionMeshes = new List<Mesh>();
//            MeshingParameters mp = new MeshingParameters(0.01);

//            foreach (Brep brep in planarBreps)
//            {
//                Mesh[] meshesFromBrep = Mesh.CreateFromBrep(brep, mp);
//                if (meshesFromBrep != null)
//                    regionMeshes.AddRange(meshesFromBrep);
//            }

//            if (regionMeshes.Count == 0)
//            {
//                outMesh = null;
//                outArea = 0;
//                return;
//            }

//            // 合并所有区域网格为一个整体
//            Mesh combinedMesh = new Mesh();
//            foreach (Mesh m in regionMeshes) combinedMesh.Append(m);
//            combinedMesh.Weld(Math.PI); // 可选项，合并共面顶点

//            // 8. 基于合并后的网格计算投影面积（多个区域面积自动累加）
//            double totalArea = 0.0;
//            AreaMassProperties amp = AreaMassProperties.Compute(combinedMesh);
//            if (amp != null)
//                totalArea = amp.Area;

//            outMesh = combinedMesh;
//            outArea = totalArea;
//        }

//        /// <summary>
//        /// 回退方法：使用凸包生成树冠网格（当 ShrinkWrap 不可用或用户选择凸包时）
//        /// </summary>
//        private Mesh CreateConvexHullFallback(List<Mesh> meshes)
//        {
//            // 收集所有顶点
//            List<Point3d> points = new List<Point3d>();
//            foreach (var mesh in meshes)
//            {
//                foreach (var vert in mesh.Vertices)
//                    points.Add(vert);
//            }

//            if (points.Count < 4) return null;

//            try
//            {
//                int[][] hullFacets;
//                Mesh convexHull = Mesh.CreateConvexHull3D(points, out hullFacets, 0.01, 0.01);
//                return convexHull;
//            }
//            catch
//            {
//                return null;
//            }
//        }

//        /// <summary>
//        /// 组件唯一标识符
//        /// </summary>
//        public override Guid ComponentGuid
//        {
//            get { return new Guid("37444BF7-F117-4ED9-B8B0-5BD47809DB33"); }
//        }

//        /// <summary>
//        /// 图标（可选项，返回24x24像素的Bitmap）
//        /// </summary>
//        protected override System.Drawing.Bitmap Icon
//        {
//            get { return Resources.icon_TreeProcessor; }
//        }
//    }
//}



//修改版：基于简化树冠模型（ShrinkWrap或Convex Hull）计算投影面积，
//替代原始精细模型。这样可以更准确地反映树冠的实际投影区域，避免由于精细模型的复杂性导致的投影面积过大或过小的问题。
//同时保留了原有的稳健投影计算方法，确保在任何情况下都能得到合理的结果。
using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using NeosEnviSim.Properties;

namespace NeosUtility
{
    public class TreeGeometryProcessor : GH_Component
    {
        public TreeGeometryProcessor()
          : base("Tree Processor", "TreeProc",
              "Process tree geometry for CFD microclimate simulation",
              "Neos", "Utility")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Tree Model", "TM", "Detailed tree geometry (one or more meshes)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Height", "H", "Tree height", GH_ParamAccess.item);
            pManager.AddNumberParameter("Crown Radius", "R", "Crown radius", GH_ParamAccess.item);
            pManager.AddPointParameter("Plant Point", "P", "Planting point (default origin)", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Edge Length", "L", "Grid spacing for shrink wrap mesh (TargetEdgeLength), controls mesh resolution", GH_ParamAccess.item, 1.0);
            pManager.AddBooleanParameter("ShrinkWrap", "SW", "If true, use ShrinkWrap to generate crown mesh; if false, use Convex Hull", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Projection Mesh", "PM", "Crown projection mesh based on simplified canopy model (TreeCan)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Crown Volume", "CV", "Crown volume (based on shrink wrap or convex hull)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Projection Area", "PA", "Crown projection area based on simplified canopy model", GH_ParamAccess.item);
            pManager.AddNumberParameter("Leaf Area", "LA", "Total leaf area (sum of all mesh areas)", GH_ParamAccess.item);
            pManager.AddNumberParameter("LAI", "LAI", "Leaf Area Index (Leaf Area / Projection Area)", GH_ParamAccess.item);
            pManager.AddNumberParameter("LAD", "LAD", "Leaf Area Density (Leaf Area / Crown Volume)", GH_ParamAccess.item);
            pManager.AddMeshParameter("Tree Detail", "TreeDet", "Scaled detailed tree model (merged meshes)", GH_ParamAccess.item);
            pManager.AddMeshParameter("Tree Canopy", "TreeCan", "Simplified canopy model (generated using Mesh.ShrinkWrap or Convex Hull)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<GH_Mesh> ghMeshes = new List<GH_Mesh>();
            double height = 10.0;
            double crownRadius = 5.0;
            Point3d plantPoint = Point3d.Origin;
            double shrinkEdgeLen = 0.1;
            bool useShrinkWrap = true;

            if (!DA.GetDataList(0, ghMeshes)) return;
            if (!DA.GetData(1, ref height)) return;
            if (!DA.GetData(2, ref crownRadius)) return;
            if (!DA.GetData(3, ref plantPoint)) return;
            if (!DA.GetData(4, ref shrinkEdgeLen)) return;
            if (!DA.GetData(5, ref useShrinkWrap)) return;

            List<Mesh> meshes = new List<Mesh>();
            foreach (var ghMesh in ghMeshes)
            {
                if (ghMesh != null && ghMesh.Value != null)
                    meshes.Add(ghMesh.Value.DuplicateMesh());
            }

            if (meshes.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No valid meshes provided.");
                return;
            }

            BoundingBox bboxOriginal = meshes[0].GetBoundingBox(true);
            foreach (var mesh in meshes.Skip(1))
                bboxOriginal.Union(mesh.GetBoundingBox(true));

            double originalWidth = bboxOriginal.Max.X - bboxOriginal.Min.X;
            double originalDepth = bboxOriginal.Max.Y - bboxOriginal.Min.Y;
            double originalHeight = bboxOriginal.Max.Z - bboxOriginal.Min.Z;

            double scaleX = (originalWidth > 0) ? (2 * crownRadius) / originalWidth : 1.0;
            double scaleY = (originalDepth > 0) ? (2 * crownRadius) / originalDepth : 1.0;
            double scaleZ = (originalHeight > 0) ? height / originalHeight : 1.0;

            Point3d centerOriginal = bboxOriginal.Center;
            Plane planeAtCenter = new Plane(centerOriginal, Vector3d.ZAxis);
            Transform scaleTransform = Transform.Scale(planeAtCenter, scaleX, scaleY, scaleZ);

            foreach (var mesh in meshes)
                mesh.Transform(scaleTransform);

            BoundingBox bboxScaled = meshes[0].GetBoundingBox(true);
            foreach (var mesh in meshes.Skip(1))
                bboxScaled.Union(mesh.GetBoundingBox(true));

            Point3d bottomCenter = new Point3d(bboxScaled.Center.X, bboxScaled.Center.Y, bboxScaled.Min.Z);
            Vector3d moveVector = plantPoint - bottomCenter;
            foreach (var mesh in meshes)
                mesh.Translate(moveVector);

            bboxScaled = meshes[0].GetBoundingBox(true);
            foreach (var mesh in meshes.Skip(1))
                bboxScaled.Union(mesh.GetBoundingBox(true));

            double leafArea = 0.0;
            foreach (var mesh in meshes)
            {
                AreaMassProperties amp = AreaMassProperties.Compute(mesh);
                if (amp != null)
                    leafArea += Math.Abs(amp.Area);
            }

            // 7. Generate simplified canopy mesh (TreeCan): ShrinkWrap or Convex Hull
            // Must precede projection calculation because projection is based on the simplified canopy model
            Mesh crownWrapMesh = null;
            double crownVolume = 0.0;

            if (useShrinkWrap)
            {
                var shrinkParams = new ShrinkWrapParameters();
                shrinkParams.TargetEdgeLength = shrinkEdgeLen;
                try
                {
                    crownWrapMesh = Mesh.ShrinkWrap(meshes, shrinkParams);
                }
                catch (MissingMethodException)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "Mesh.ShrinkWrap not available. Falling back to convex hull.");
                    crownWrapMesh = CreateConvexHullFallback(meshes);
                }
                catch (Exception ex)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "ShrinkWrap error: " + ex.Message);
                    crownWrapMesh = CreateConvexHullFallback(meshes);
                }
            }
            else
            {
                crownWrapMesh = CreateConvexHullFallback(meshes);
            }

            if (crownWrapMesh != null && crownWrapMesh.IsValid)
            {
                VolumeMassProperties vmp = VolumeMassProperties.Compute(crownWrapMesh);
                crownVolume = vmp != null ? vmp.Volume : 0.0;
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    "Failed to create canopy mesh. Projection will use detailed tree mesh as fallback.");
            }

            // 8. Compute crown projection mesh and projection area — based on simplified canopy model (crownWrapMesh)
            Mesh projectionMesh = null;
            double projectionArea = 0.0;
            Mesh meshForProjection = (crownWrapMesh != null && crownWrapMesh.IsValid)
                ? crownWrapMesh : meshes[0];
            ComputeProjectionMesh(meshForProjection, ref projectionMesh, ref projectionArea);

            double lai = (projectionArea > 0) ? leafArea / projectionArea : 0;
            double lad = (crownVolume > 0) ? leafArea / crownVolume : 0;

            Mesh scaledTreeMesh = new Mesh();
            foreach (var mesh in meshes)
                scaledTreeMesh.Append(mesh);

            DA.SetData(0, projectionMesh);
            DA.SetData(1, crownVolume);
            DA.SetData(2, projectionArea);
            DA.SetData(3, leafArea);
            DA.SetData(4, lai);
            DA.SetData(5, lad);
            DA.SetData(6, scaledTreeMesh);
            DA.SetData(7, crownWrapMesh);
        }

        /// <summary>
        /// Flatten the simplified canopy mesh to Z=0, extract its 2D silhouette outline,
        /// and create a clean planar mesh from the outline. This avoids face overlap
        /// and double-counting of area caused by directly flattening a closed 3D mesh.
        /// </summary>
        private void ComputeProjectionMesh(Mesh mesh, ref Mesh outMesh, ref double outArea)
        {
            if (mesh == null || !mesh.IsValid)
            {
                outMesh = null;
                outArea = 0;
                return;
            }

            // 1. Flatten all vertices to Z=0
            Mesh flatMesh = mesh.DuplicateMesh();
            if (flatMesh == null || flatMesh.Vertices == null)
            {
                outMesh = null;
                outArea = 0;
                return;
            }

            for (int i = 0; i < flatMesh.Vertices.Count; i++)
            {
                Point3f pt = flatMesh.Vertices[i];
                pt.Z = 0;
                flatMesh.Vertices.SetVertex(i, pt);
            }
            flatMesh.Normals.ComputeNormals();

            // 2. Extract 2D silhouette outline from the flattened mesh
            // Note: GetOutlines returns Polyline[], not Curve[]. Convert to PolylineCurve.
            Polyline[] outlines = null;
            try { outlines = flatMesh.GetOutlines(Plane.WorldXY); }
            catch { outlines = null; }

            if (outlines == null || outlines.Length == 0)
            {
                outMesh = flatMesh;
                AreaMassProperties amp = AreaMassProperties.Compute(flatMesh);
                outArea = amp != null ? Math.Abs(amp.Area) : 0;
                return;
            }

            // 3. Convert Polylines to PolylineCurves and keep only closed ones
            List<Curve> closedOutlines = new List<Curve>();
            foreach (Polyline pl in outlines)
            {
                if (pl != null && pl.IsClosed)
                    closedOutlines.Add(new PolylineCurve(pl));
            }

            if (closedOutlines.Count == 0)
            {
                outMesh = flatMesh;
                AreaMassProperties amp = AreaMassProperties.Compute(flatMesh);
                outArea = amp != null ? Math.Abs(amp.Area) : 0;
                return;
            }

            // 4. Create planar Breps from closed outlines (single clean surface, no overlap)
            Brep[] planarBreps = Brep.CreatePlanarBreps(closedOutlines, 0.01);
            if (planarBreps == null || planarBreps.Length == 0)
            {
                outMesh = flatMesh;
                AreaMassProperties amp = AreaMassProperties.Compute(flatMesh);
                outArea = amp != null ? Math.Abs(amp.Area) : 0;
                return;
            }

            // 5. Merge planar Breps into a single clean mesh + compute exact area
            Mesh resultMesh = new Mesh();
            double totalArea = 0.0;
            MeshingParameters mp = new MeshingParameters(0.01);

            foreach (Brep brep in planarBreps)
            {
                if (brep == null) continue;
                totalArea += brep.GetArea();
                Mesh[] bm = Mesh.CreateFromBrep(brep, mp);
                if (bm != null)
                    foreach (Mesh m in bm)
                        if (m != null) resultMesh.Append(m);
            }

            if (resultMesh == null || resultMesh.Faces == null || resultMesh.Faces.Count == 0)
            {
                outMesh = flatMesh;
                outArea = totalArea;
                return;
            }

            outMesh = resultMesh;
            outArea = totalArea;
        }

        /// <summary>
        /// Fallback method: use convex hull to generate crown mesh when ShrinkWrap is unavailable or user selects convex hull
        /// </summary>
        private Mesh CreateConvexHullFallback(List<Mesh> meshes)
        {
            if (meshes == null || meshes.Count == 0) return null;

            List<Point3d> points = new List<Point3d>();
            foreach (var mesh in meshes)
            {
                if (mesh == null || mesh.Vertices == null) continue;
                foreach (var vert in mesh.Vertices)
                    points.Add(vert);
            }

            if (points.Count < 4) return null;

            try
            {
                int[][] hullFacets;
                Mesh convexHull = Mesh.CreateConvexHull3D(points, out hullFacets, 0.01, 0.01);
                return convexHull;
            }
            catch
            {
                return null;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("37444BF7-F117-4ED9-B8B0-5BD47809DB33"); }
        }

        protected override System.Drawing.Bitmap Icon
        {
            get { return Resources.icon_TreeProcessor; }
        }
    }
}
