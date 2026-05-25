using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace NeosUtility
{
    public class PVPanelProcessorComponent : GH_Component
    {
        // 构造函数：定义组件名称、说明及所属的Category和Subcategory
        public PVPanelProcessorComponent()
          : base("PV Panel Processor", "PVPanel",
              "Subdivides a base surface into PV panels with scaling, rotation, and translation capabilities.",
              "Neos", "Utility")
        {
        }

        // 注册输入参数
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddSurfaceParameter("Base Surface", "S", "Base surface for PV panels", GH_ParamAccess.item);
            pManager.AddIntegerParameter("U Count", "U", "Number of subdivisions in U direction", GH_ParamAccess.item, 10);
            pManager.AddIntegerParameter("V Count", "V", "Number of subdivisions in V direction", GH_ParamAccess.item, 10);
            pManager.AddNumberParameter("Scale Factor", "SF", "Scale factor from the centroid of each panel", GH_ParamAccess.item, 0.95);
            pManager.AddIntegerParameter("Rotation Axis Mode", "RM", "0: U-Axis Median, 1: V-Axis Median, 2-5: Sequential Edges", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Tilt Angle (Pi)", "TA", "Rotation angle in multiples of Pi.e.g.:0.2 = 0.2 * Pi", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Horizontal Rotation Angle (Pi)", "HA", "Horizontal rotation angle in XY plane around centroid's vertical axis (multiples of Pi)", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Z Move", "Z", "Translation amount in Z direction", GH_ParamAccess.item, 0.0);
        }

        // 注册输出参数
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("PV Panels", "Panels", "Processed subdivided PV panels", GH_ParamAccess.list);
        }

        // 核心运算逻辑
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 初始化参数默认值
            Surface baseSrf = null;
            int uCount = 1;
            int vCount = 1;
            double scaleFactor = 1.0;
            int axisMode = 0;
            double rotAnglePi = 0.0;
            double horizRotAnglePi = 0.0; // 【新增】水平旋转角度变量
            double zMove = 0.0;

            // 读取输入数据，注意索引的变化
            if (!DA.GetData(0, ref baseSrf)) return;
            if (!DA.GetData(1, ref uCount)) return;
            if (!DA.GetData(2, ref vCount)) return;
            if (!DA.GetData(3, ref scaleFactor)) return;
            if (!DA.GetData(4, ref axisMode)) return;
            if (!DA.GetData(5, ref rotAnglePi)) return;
            if (!DA.GetData(6, ref horizRotAnglePi)) return; // 【新增】读取水平旋转角度 (Index 6)
            if (!DA.GetData(7, ref zMove)) return;           // Z Move 顺延为 Index 7

            // 确保UV细分数量最小为1，避免除数为零
            if (uCount < 1) uCount = 1;
            if (vCount < 1) vCount = 1;

            List<Brep> panels = new List<Brep>();

            // 获取输入曲面的U和V原始区间
            Interval uDomain = baseSrf.Domain(0);
            Interval vDomain = baseSrf.Domain(1);

            // 计算每个方向的步长
            double uStep = uDomain.Length / uCount;
            double vStep = vDomain.Length / vCount;

            // 嵌套循环生成等分面块 (ISOTRIME)
            for (int i = 0; i < uCount; i++)
            {
                for (int j = 0; j < vCount; j++)
                {
                    // 计算当前子面块所在的UV区间
                    double u0 = uDomain.Min + i * uStep;
                    double u1 = uDomain.Min + (i + 1) * uStep;
                    double v0 = vDomain.Min + j * vStep;
                    double v1 = vDomain.Min + (j + 1) * vStep;

                    Interval subU = new Interval(u0, u1);
                    Interval subV = new Interval(v0, v1);

                    // 裁剪得到细分曲面
                    Surface subSrf = baseSrf.Trim(subU, subV);
                    if (subSrf == null) continue;

                    // 将 Surface 转换为 Brep 以便使用复杂的几何变换等方法
                    Brep panelBrep = subSrf.ToBrep();

                    // 1. 获取中心点
                    AreaMassProperties amp = AreaMassProperties.Compute(subSrf);
                    Point3d centroid = amp != null ? amp.Centroid : Point3d.Origin;

                    // 定义中心点缩放变换矩阵
                    Transform tScale = Transform.Scale(centroid, scaleFactor);

                    // 【新增】定义水平旋转矩阵 (以 centroid 为中心点，沿绝对Z轴(垂直轴)旋转)
                    Transform tHorizRot = Transform.Identity;
                    if (Math.Abs(horizRotAnglePi) > 1e-8)
                    {
                        tHorizRot = Transform.Rotation(horizRotAnglePi * Math.PI, Vector3d.ZAxis, centroid);
                    }

                    // 合并 缩放 与 水平旋转（生成后续找旋转轴基准点的前置变换）
                    // 这样边缘轴(Axis)也会随着面板一起在水平方向上发生转动
                    Transform tPreRot = tHorizRot * tScale;

                    // 2. 获取细分曲面的四个起始角点 (对应UV边界值)
                    Point3d p00 = subSrf.PointAt(u0, v0);
                    Point3d p10 = subSrf.PointAt(u1, v0);
                    Point3d p11 = subSrf.PointAt(u1, v1);
                    Point3d p01 = subSrf.PointAt(u0, v1);

                    // 对角点应用前置变换（缩放+水平旋转），以获取准确的翻折(Tilt)旋转轴线点
                    p00.Transform(tPreRot);
                    p10.Transform(tPreRot);
                    p11.Transform(tPreRot);
                    p01.Transform(tPreRot);

                    Point3d axisStart = new Point3d();
                    Point3d axisEnd = new Point3d();

                    // 根据旋转轴模式，确定翻折旋转轴
                    switch (axisMode)
                    {
                        case 0:
                            // U向中心轴（V向中点的连线为轴）
                            axisStart = GetMidPoint(p00, p01);
                            axisEnd = GetMidPoint(p10, p11);
                            break;
                        case 1:
                            // V向中心轴（U向中点的连线为轴）
                            axisStart = GetMidPoint(p00, p10);
                            axisEnd = GetMidPoint(p01, p11);
                            break;
                        case 2:
                            // 边1: p00 到 p10
                            axisStart = p00; axisEnd = p10;
                            break;
                        case 3:
                            // 边2: p10 到 p11
                            axisStart = p10; axisEnd = p11;
                            break;
                        case 4:
                            // 边3: p11 到 p01
                            axisStart = p11; axisEnd = p01;
                            break;
                        case 5:
                            // 边4: p01 到 p00
                            axisStart = p01; axisEnd = p00;
                            break;
                        default:
                            // 默认使用U向中心轴
                            axisStart = GetMidPoint(p00, p01);
                            axisEnd = GetMidPoint(p10, p11);
                            break;
                    }

                    Vector3d axisDir = axisEnd - axisStart;
                    Transform tRot = Transform.Identity;

                    // 如果旋转轴方向向量有效，则生成倾斜旋转矩阵
                    if (axisDir.Length > 1e-8)
                    {
                        // 角度使用 参数 * Pi
                        tRot = Transform.Rotation(rotAnglePi * Math.PI, axisDir, axisStart);
                    }

                    // 3. 定义Z向移动变换矩阵
                    Transform tMove = Transform.Translation(new Vector3d(0, 0, zMove));

                    // 4. 组合所有变换矩阵（Z向平移 * 面板边翻折 * 水平旋转 * 面板缩放）
                    Transform tFinal = tMove * tRot * tPreRot;

                    // 应用最终的变换矩阵到单个Brep并添加到列表
                    panelBrep.Transform(tFinal);
                    panels.Add(panelBrep);
                }
            }

            // 赋值输出端
            DA.SetDataList(0, panels);
        }

        // 辅助方法：求取两空间点的中点
        private Point3d GetMidPoint(Point3d pA, Point3d pB)
        {
            return new Point3d((pA.X + pB.X) * 0.5, (pA.Y + pB.Y) * 0.5, (pA.Z + pB.Z) * 0.5);
        }

        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                return Resources.icon_PVpanel;
            }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("141C9D7C-8DC5-423D-933D-5BBED61A7FC4"); }
        }
    }
}