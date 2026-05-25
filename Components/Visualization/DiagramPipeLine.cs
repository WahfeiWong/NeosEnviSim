using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace NeosVisualization
{
    public class DiagramPipeComponent : GH_Component
    {
        /// <summary>
        /// 初始化组件的名称、昵称、描述和分类
        /// </summary>
        public DiagramPipeComponent()
          : base("Diagram Pipe Line", "Diagram Pipe",
              "Generates a display-ready radius-varying, dashed tubular diagram line with an optional conical arrow.",
              "Neos", "Visualization")
        {
        }

        /// <summary>
        /// 注册输入参数
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddCurveParameter("Curve", "C", "Input reference curve", GH_ParamAccess.item);
            pManager.AddNumberParameter("Start Radius", "SR", "Radius at the start point", GH_ParamAccess.item, 0.25);
            pManager.AddNumberParameter("End Radius", "ER", "Radius at the end point (where it connects to the arrow)", GH_ParamAccess.item, 0.25);
            pManager.AddNumberParameter("Segment Length", "SL", "Length of each dash segment", GH_ParamAccess.item, 3.0);
            pManager.AddNumberParameter("Segment Gap", "SG", "Gap between dashes (0 for continuous pipe)", GH_ParamAccess.item, 0.5);
            pManager.AddBooleanParameter("Has Arrow", "HA", "Whether to draw a conical arrow at the end", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Arrow Base Radius", "ABR", "Radius of the arrow cone base", GH_ParamAccess.item, 0.8);
            pManager.AddNumberParameter("Arrow Height", "AH", "Height of the arrow cone", GH_ParamAccess.item, 1.5);
            pManager.AddBooleanParameter("Reverse Direction", "RD", "Reverse the direction of the curve and arrow", GH_ParamAccess.item, false);
            pManager.AddColourParameter("Color", "Col", "Display color of the generated geometry", GH_ParamAccess.item, Color.Black);
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
            double startRadius = 0.25;
            double endRadius = 0.25;
            double segLen = 3.0;
            double segGap = 0.4;
            bool hasArrow = true;
            double arrowBaseRadius = 0.8;
            double arrowHeight = 1.5;
            bool reverseDir = false;
            Color color = Color.Black;

            if (!DA.GetData(0, ref inputCurve)) return;
            DA.GetData(1, ref startRadius);
            DA.GetData(2, ref endRadius);
            DA.GetData(3, ref segLen);
            DA.GetData(4, ref segGap);
            DA.GetData(5, ref hasArrow);
            DA.GetData(6, ref arrowBaseRadius);
            DA.GetData(7, ref arrowHeight);
            DA.GetData(8, ref reverseDir);
            DA.GetData(9, ref color);

            if (inputCurve == null || !inputCurve.IsValid) return;

            // 复制以防止修改原始输入，处理反转方向
            Curve crv = inputCurve.DuplicateCurve();
            if (reverseDir) crv.Reverse();

            double totalLen = crv.GetLength();
            if (totalLen <= 1e-6) return;

            // 处理圆锥箭头长度：确保箭头长度不超过曲线总长度
            double arrowLen = hasArrow ? Math.Min(arrowHeight, totalLen) : 0.0;
            double lineLen = totalLen - arrowLen;

            Mesh finalMesh = new Mesh();
            int sides = 16; // 圆管及圆锥的分段精度

            // 2. 生成虚线或实线圆管主体部分
            if (lineLen > 1e-6)
            {
                if (segGap <= 0.0)
                {
                    // 无间隔，生成单段连续圆管网格
                    Mesh m = CreateDashPipeMesh(crv, 0.0, lineLen, lineLen, startRadius, endRadius, sides);
                    if (m != null) finalMesh.Append(m);
                }
                else
                {
                    // 有间隔，循环生成多段虚线圆管网格
                    double currentDist = 0.0;
                    while (currentDist < lineLen - 1e-6)
                    {
                        double nextDist = Math.Min(currentDist + segLen, lineLen);
                        Mesh m = CreateDashPipeMesh(crv, currentDist, nextDist, lineLen, startRadius, endRadius, sides);
                        if (m != null) finalMesh.Append(m);
                        currentDist = nextDist + segGap;
                    }
                }
            }

            // 3. 生成圆锥形箭头部分
            if (hasArrow && arrowLen > 1e-6)
            {
                Mesh arrowMesh = CreateArrowConeMesh(crv, lineLen, totalLen, arrowBaseRadius, sides);
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
        /// 构建圆管线段（虚线中的每一段）的网格模型
        /// </summary>
        private Mesh CreateDashPipeMesh(Curve crv, double distStart, double distEnd, double totalLineLen, double rStart, double rEnd, int sides)
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
                Plane frame = GetStableFrame(dash, t);

                double fraction = (double)i / steps;
                double globalDist = distStart + dashLen * fraction;

                // 利用插值计算当前位置渐变半径
                double ratio = (totalLineLen > 1e-6) ? (globalDist / totalLineLen) : 0.0;
                double R = rStart + (rEnd - rStart) * ratio;

                // 生成圆环上的点
                for (int j = 0; j < sides; j++)
                {
                    double angle = 2.0 * Math.PI * j / sides;
                    // frame.XAxis 和 YAxis 对应内部稳定求出的 N 与 B
                    Point3d pt = frame.PointAt(Math.Cos(angle) * R, Math.Sin(angle) * R);
                    mesh.Vertices.Add(pt);
                }
            }

            // 生成管壁侧面拓扑面
            for (int i = 0; i < steps; i++)
            {
                int row0 = i * sides;
                int row1 = (i + 1) * sides;
                for (int j = 0; j < sides; j++)
                {
                    int next_j = (j + 1) % sides;
                    // 逆时针规则，保证法线朝外
                    mesh.Faces.AddFace(row0 + j, row0 + next_j, row1 + next_j, row1 + j);
                }
            }

            // 生成起点的封盖
            int startCenterIdx = mesh.Vertices.Count;
            mesh.Vertices.Add(dash.PointAtStart);
            for (int j = 0; j < sides; j++)
            {
                int next_j = (j + 1) % sides;
                // 对于起点，管口向后看，面法线需朝外
                mesh.Faces.AddFace(startCenterIdx, next_j, j);
            }

            // 生成终点的封盖
            int endCenterIdx = mesh.Vertices.Count;
            mesh.Vertices.Add(dash.PointAtEnd);
            int lastRow = steps * sides;
            for (int j = 0; j < sides; j++)
            {
                int next_j = (j + 1) % sides;
                // 对于终点，管口向前看，面法线朝向前方
                mesh.Faces.AddFace(endCenterIdx, lastRow + j, lastRow + next_j);
            }

            return mesh;
        }

        /// <summary>
        /// 构建圆锥状箭头的网格模型
        /// </summary>
        private Mesh CreateArrowConeMesh(Curve crv, double distBase, double distTip, double arrowRadius, int sides)
        {
            crv.LengthParameter(distBase, out double tBase);
            crv.LengthParameter(distTip, out double tTip);

            Plane baseFrame = GetStableFrame(crv, tBase);
            Point3d tipPt = crv.PointAt(tTip);

            Mesh mesh = new Mesh();

            // 生成底部圆环顶点
            for (int j = 0; j < sides; j++)
            {
                double angle = 2.0 * Math.PI * j / sides;
                Point3d pt = baseFrame.PointAt(Math.Cos(angle) * arrowRadius, Math.Sin(angle) * arrowRadius);
                mesh.Vertices.Add(pt);
            }

            // 圆锥尖端顶点
            int tipIdx = mesh.Vertices.Count;
            mesh.Vertices.Add(tipPt);

            // 底部中心顶点（用于封盖）
            int baseCenterIdx = mesh.Vertices.Count;
            mesh.Vertices.Add(baseFrame.Origin);

            // 生成拓扑面
            for (int j = 0; j < sides; j++)
            {
                int next_j = (j + 1) % sides;
                // 侧面（圆锥面）
                mesh.Faces.AddFace(j, next_j, tipIdx);
                // 底部封底面（保证法向外翻）
                mesh.Faces.AddFace(baseCenterIdx, next_j, j);
            }

            return mesh;
        }

        /// <summary>
        /// 获取稳定正交参考平面的辅助方法，防止圆管在三维空间中严重扭转
        /// </summary>
        private Plane GetStableFrame(Curve crv, double t)
        {
            Point3d P = crv.PointAt(t);
            Vector3d T = crv.TangentAt(t);

            // 利用全局Z轴与切线叉乘构造稳定的副法线
            Vector3d N = Vector3d.CrossProduct(Vector3d.ZAxis, T);
            if (N.Length < 1e-6)
            {
                N = Vector3d.CrossProduct(Vector3d.XAxis, T);
                if (N.Length < 1e-6) N = Vector3d.CrossProduct(Vector3d.YAxis, T);
            }
            N.Unitize();

            // 构造第三个正交基
            Vector3d B = Vector3d.CrossProduct(T, N);
            B.Unitize();

            // 生成平面，法向(Z轴)为切线方向
            return new Plane(P, N, B);
        }

        /// <summary>
        /// 提供组件图标
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                // 如果您有对应Icon，可将返回null替换为对应资源，例如:
                // return Resources.icon_DiagramPipe;
                return Resources.icon_DiagramPipe;
            }
        }

        /// <summary>
        /// 获取组件在Grasshopper文档中的唯一ID
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("8CDFEBF0-D8E6-40AE-BE9E-A8C654E4AE9F"); }
        }
    }
}

