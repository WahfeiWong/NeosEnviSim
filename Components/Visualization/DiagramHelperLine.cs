using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;

namespace NeosVisualization
{
    public class DiagramHelperLineComponent : GH_Component
    {
        /// <summary>
        /// 初始化组件的名称、昵称、描述和分类
        /// </summary>
        public DiagramHelperLineComponent()
          : base("Diagram Helper Line", "Diagram Line",
              "Generates a display-ready width-varying, dashed, extruded diagram line with an optional arrow.",
              "Neos", "Visualization")
        {
        }

        /// <summary>
        /// 注册输入参数
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve", "C", "Input curve to process", GH_ParamAccess.item);
            pManager.AddNumberParameter("Start Width", "SW", "Width at the start point", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("End Width", "EW", "Width at the end point (where it connects to the arrow)", GH_ParamAccess.item, 0.5);
            pManager.AddNumberParameter("Thickness", "T", "Extrusion thickness for 3D display", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Segment Length", "SL", "Length of each dash segment", GH_ParamAccess.item, 3.0);
            pManager.AddNumberParameter("Segment Gap", "SG", "Gap between dashes (0 for continuous line)", GH_ParamAccess.item, 0.4);
            pManager.AddBooleanParameter("Has Arrow", "HA", "Whether to draw an arrow at the end", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Arrow Tail Width", "AW", "Width of the arrow base", GH_ParamAccess.item, 2.5);
            pManager.AddBooleanParameter("Reverse Direction", "RD", "Reverse the direction of the curve and arrow", GH_ParamAccess.item, false);
            pManager.AddColourParameter("Color", "Col", "Display color of the generated geometry", GH_ParamAccess.item, Color.Black);

            // 新增的输入端：U向平行面，默认值为 XY Plane
            pManager.AddPlaneParameter("U Parallel Plane", "UP", "The plane to which the U direction is parallel (V is curve direction). Accepts XY, XZ, YZ planes etc.", GH_ParamAccess.item, Plane.WorldXY);
        }

        /// <summary>
        /// 注册输出参数
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "Generated colored visualization mesh", GH_ParamAccess.item);
        }

        /// <summary>
        /// 核心计算逻辑
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. 获取输入数据
            Curve inputCurve = null;
            double startWidth = 0.5;
            double endWidth = 0.5;
            double thickness = 0.0;
            double segLen = 3.0;
            double segGap = 0.4;
            bool hasArrow = true;
            double arrowTailWidth = 2.5;
            bool reverseDir = false;
            Color color = Color.Black;
            Plane uPlane = Plane.WorldXY;

            if (!DA.GetData(0, ref inputCurve)) return;
            DA.GetData(1, ref startWidth);
            DA.GetData(2, ref endWidth);
            DA.GetData(3, ref thickness);
            DA.GetData(4, ref segLen);
            DA.GetData(5, ref segGap);
            DA.GetData(6, ref hasArrow);
            DA.GetData(7, ref arrowTailWidth);
            DA.GetData(8, ref reverseDir);
            DA.GetData(9, ref color);
            DA.GetData(10, ref uPlane);

            if (inputCurve == null || !inputCurve.IsValid) return;

            // 复制以防止修改原始输入，处理反转方向
            Curve crv = inputCurve.DuplicateCurve();
            if (reverseDir) crv.Reverse();

            double totalLen = crv.GetLength();
            if (totalLen <= 1e-6) return;

            // 获取平面的法向量，用于后续叉乘计算U方向以及立体挤出
            Vector3d planeNormal = uPlane.ZAxis;

            // 处理箭头长度：确保箭头长度不超过曲线总长度
            double arrowLen = hasArrow ? Math.Min(segLen, totalLen) : 0.0;
            double lineLen = totalLen - arrowLen;

            Mesh finalMesh = new Mesh();

            // 2. 生成虚线或实线主体部分
            if (lineLen > 1e-6)
            {
                if (segGap <= 0.0)
                {
                    // 无间隔，生成单段连续网格
                    Mesh m = CreateDashMesh(crv, 0.0, lineLen, lineLen, startWidth, endWidth, thickness, planeNormal);
                    if (m != null) finalMesh.Append(m);
                }
                else
                {
                    // 有间隔，循环生成多段虚线网格
                    double currentDist = 0.0;
                    while (currentDist < lineLen - 1e-6)
                    {
                        double nextDist = Math.Min(currentDist + segLen, lineLen);
                        Mesh m = CreateDashMesh(crv, currentDist, nextDist, lineLen, startWidth, endWidth, thickness, planeNormal);
                        if (m != null) finalMesh.Append(m);
                        currentDist = nextDist + segGap;
                    }
                }
            }

            // 3. 生成箭头部分
            if (hasArrow && arrowLen > 1e-6)
            {
                Mesh arrowMesh = CreateArrowMesh(crv, lineLen, totalLen, arrowTailWidth, thickness, planeNormal);
                if (arrowMesh != null) finalMesh.Append(arrowMesh);
            }

            // 4. 后处理与着色
            if (finalMesh.IsValid && finalMesh.Faces.Count > 0)
            {
                // 为网格设置整体顶点颜色以实现Grasshopper中的自带渲染预览
                finalMesh.VertexColors.CreateMonotoneMesh(color);
                finalMesh.Normals.ComputeNormals();
                finalMesh.Compact();
                DA.SetData(0, finalMesh);
            }
        }

        /// <summary>
        /// 构建线段（虚线中的每一段）的网格模型
        /// </summary>
        private Mesh CreateDashMesh(Curve crv, double distStart, double distEnd, double totalLineLen, double wStart, double wEnd, double thickness, Vector3d planeNormal)
        {
            crv.LengthParameter(distStart, out double t0);
            crv.LengthParameter(distEnd, out double t1);

            Curve dash = crv.Trim(t0, t1);
            if (dash == null) return null;

            double dashLen = distEnd - distStart;
            // 动态决定细分步数，确保曲线平滑，并限制最大上限防止卡死
            int steps = Math.Max(1, (int)Math.Ceiling(dashLen / 0.1));
            if (steps > 1000) steps = 1000;

            double[] t_vals = dash.DivideByCount(steps, true);
            // 处理极端情况下的无效分割
            if (t_vals == null)
            {
                t_vals = new double[steps + 1];
                for (int i = 0; i <= steps; i++)
                    t_vals[i] = dash.Domain.ParameterAt((double)i / steps);
            }

            Mesh mesh = new Mesh();

            // 生成顶点
            for (int i = 0; i < t_vals.Length; i++)
            {
                double t = t_vals[i];
                Point3d P = dash.PointAt(t);
                Vector3d T = dash.TangentAt(t);

                // 计算法线：方向平行于输入的U向面（即垂直于平面法向与切线的所在平面）
                Vector3d N = Vector3d.CrossProduct(planeNormal, T);

                // 切线绝对垂直于输入平面时的回退机制
                if (N.Length < 1e-6)
                {
                    N = Vector3d.CrossProduct(Vector3d.XAxis, T);
                    if (N.Length < 1e-6) N = Vector3d.CrossProduct(Vector3d.YAxis, T);
                }
                N.Unitize();

                double fraction = (double)i / steps;
                double globalDist = distStart + dashLen * fraction;

                // 利用插值计算当前位置渐变宽度
                double ratio = (totalLineLen > 1e-6) ? (globalDist / totalLineLen) : 0.0;
                double W = wStart + (wEnd - wStart) * ratio;

                Point3d P_left = P + N * (W * 0.5);
                Point3d P_right = P - N * (W * 0.5);

                if (Math.Abs(thickness) <= 1e-6)
                {
                    mesh.Vertices.Add(P_left);
                    mesh.Vertices.Add(P_right);
                }
                else
                {
                    // 沿着平面法向上下挤出构造立体点
                    Point3d P_lt = P_left + planeNormal * (thickness * 0.5);
                    Point3d P_rt = P_right + planeNormal * (thickness * 0.5);
                    Point3d P_lb = P_left - planeNormal * (thickness * 0.5);
                    Point3d P_rb = P_right - planeNormal * (thickness * 0.5);

                    mesh.Vertices.Add(P_lt);
                    mesh.Vertices.Add(P_rt);
                    mesh.Vertices.Add(P_lb);
                    mesh.Vertices.Add(P_rb);
                }
            }

            // 生成拓扑面
            if (Math.Abs(thickness) <= 1e-6)
            {
                for (int i = 0; i < steps; i++)
                {
                    int i0 = i * 2;
                    int i1 = (i + 1) * 2;
                    mesh.Faces.AddFace(i0, i0 + 1, i1 + 1, i1);
                }
            }
            else
            {
                for (int i = 0; i < steps; i++)
                {
                    int i0 = i * 4;
                    int i1 = (i + 1) * 4;
                    mesh.Faces.AddFace(i0, i0 + 1, i1 + 1, i1);           // Top
                    mesh.Faces.AddFace(i0 + 3, i0 + 2, i1 + 2, i1 + 3);   // Bottom
                    mesh.Faces.AddFace(i0 + 2, i0 + 0, i1 + 0, i1 + 2);   // Left
                    mesh.Faces.AddFace(i0 + 1, i0 + 3, i1 + 3, i1 + 1);   // Right
                }
                // 起点截面
                mesh.Faces.AddFace(0, 2, 3, 1);
                // 终点截面
                int last = steps * 4;
                mesh.Faces.AddFace(last, last + 1, last + 3, last + 2);
            }

            return mesh;
        }

        /// <summary>
        /// 构建箭头的网格模型
        /// </summary>
        private Mesh CreateArrowMesh(Curve crv, double distBase, double distTip, double tailWidth, double thickness, Vector3d planeNormal)
        {
            crv.LengthParameter(distBase, out double tBase);
            crv.LengthParameter(distTip, out double tTip);

            Point3d P_base = crv.PointAt(tBase);
            Vector3d T_base = crv.TangentAt(tBase);

            // 计算箭头底部的拓扑展开方向
            Vector3d N_base = Vector3d.CrossProduct(planeNormal, T_base);
            if (N_base.Length < 1e-6)
            {
                N_base = Vector3d.CrossProduct(Vector3d.XAxis, T_base);
                if (N_base.Length < 1e-6) N_base = Vector3d.CrossProduct(Vector3d.YAxis, T_base);
            }
            N_base.Unitize();

            Point3d P_tip = crv.PointAt(tTip);

            Point3d B_left = P_base + N_base * (tailWidth * 0.5);
            Point3d B_right = P_base - N_base * (tailWidth * 0.5);

            Mesh mesh = new Mesh();

            if (Math.Abs(thickness) <= 1e-6)
            {
                mesh.Vertices.Add(B_left);
                mesh.Vertices.Add(B_right);
                mesh.Vertices.Add(P_tip);
                mesh.Faces.AddFace(0, 1, 2);
            }
            else
            {
                // 沿着平面法向上下挤出
                Point3d B_lt = B_left + planeNormal * (thickness * 0.5);
                Point3d B_rt = B_right + planeNormal * (thickness * 0.5);
                Point3d B_lb = B_left - planeNormal * (thickness * 0.5);
                Point3d B_rb = B_right - planeNormal * (thickness * 0.5);

                Point3d Tip_t = P_tip + planeNormal * (thickness * 0.5);
                Point3d Tip_b = P_tip - planeNormal * (thickness * 0.5);

                mesh.Vertices.Add(B_lt); // 0
                mesh.Vertices.Add(B_rt); // 1
                mesh.Vertices.Add(B_lb); // 2
                mesh.Vertices.Add(B_rb); // 3
                mesh.Vertices.Add(Tip_t); // 4
                mesh.Vertices.Add(Tip_b); // 5

                mesh.Faces.AddFace(0, 1, 4);       // Top
                mesh.Faces.AddFace(3, 2, 5);       // Bottom
                mesh.Faces.AddFace(2, 0, 4, 5);    // Left
                mesh.Faces.AddFace(1, 3, 5, 4);    // Right
                mesh.Faces.AddFace(0, 2, 3, 1);    // Back Cap
            }

            return mesh;
        }

        /// <summary>
        /// 提供组件图标
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {

                return Resources.icon_DiagramLine;
            }
        }

        /// <summary>
        /// 获取组件在Grasshopper文档中的唯一ID
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("F7736A70-A0EC-475E-BABB-0693BA3845BE"); }
        }
    }
}