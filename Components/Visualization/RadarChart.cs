using Grasshopper.Kernel;
using Rhino.Geometry;
using NeosEnviSim.Properties;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NeosVisualization
{
    public class RadarChartComponent : GH_Component
    {
        public RadarChartComponent()
          : base("Radar Chart", 
                 "RadarC",
                 "Creates a radar chart visualization",
                 "Neos",
                 "Visualization")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Origin", "O", "Center point of the radar chart", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Data", "D", "Data values for each axis", GH_ParamAccess.list);
            pManager.AddTextParameter("Categories", "C", "Category names for each axis", GH_ParamAccess.list);
            pManager.AddNumberParameter("Radius Step", "RS", "Radius difference between concentric circles", GH_ParamAccess.item, 1.0);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Radar Polygon", "P", "Main radar polygon", GH_ParamAccess.item);
            pManager.AddCurveParameter("Concentric Circles", "CC", "Concentric circles", GH_ParamAccess.list);
            pManager.AddNumberParameter("Circle Radii", "R", "Radii of concentric circles", GH_ParamAccess.list);
            pManager.AddBrepParameter("Ring Surfaces", "RS", "Surfaces of concentric rings", GH_ParamAccess.list);
            pManager.AddCurveParameter("Radial Axes", "RA", "Radial axes lines", GH_ParamAccess.list);
            pManager.AddPointParameter("Circle-Y Points", "CYP", "Points where concentric circles intersect positive Y-axis", GH_ParamAccess.list);
            pManager.AddPointParameter("Axis End Points", "AEP", "Points where radial axes intersect outermost circle", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 获取输入数据
            Point3d origin = Point3d.Origin;
            List<double> data = new List<double>();
            List<string> categories = new List<string>();
            double radiusStep = 1.0;

            if (!DA.GetData(0, ref origin)) return;
            if (!DA.GetDataList(1, data)) return;
            if (!DA.GetDataList(2, categories)) return;
            if (!DA.GetData(3, ref radiusStep)) return;

            if (data.Count < 3)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "At least 3 data points required");
                return;
            }

            if (data.Count != categories.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Data and categories count must match");
                return;
            }

            if (radiusStep <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Radius step must be positive");
                return;
            }

            // 1. 计算同心圆参数
            double maxValue = data.Max();
            int circleCount = (int)Math.Ceiling(maxValue / radiusStep);
            if (circleCount == 0) circleCount = 1;

            List<double> radii = new List<double>();
            for (int i = 1; i <= circleCount; i++)
            {
                radii.Add(i * radiusStep);
            }

            // 2. 创建同心圆
            List<Circle> circles = new List<Circle>();
            List<Brep> ringSurfaces = new List<Brep>();
            Plane plane = Plane.WorldXY;
            plane.Origin = origin;

            // 创建同心圆和圆环面
            foreach (double radius in radii)
            {
                Circle circle = new Circle(plane, radius);
                circles.Add(circle);

                // 创建圆环面（除了第一个圆盘）
                if (radius == radii.First())
                {
                    // 第一个圆是实心圆盘
                    ringSurfaces.Add(Brep.CreatePlanarBreps(new Curve[] { circle.ToNurbsCurve() }, 0.01)[0]);
                }
                else
                {
                    // 后续圆环
                    double prevRadius = radii[radii.IndexOf(radius) - 1];
                    Circle prevCircle = new Circle(plane, prevRadius);

                    Brep ring = Brep.CreateFromLoft(
                        new Curve[] { prevCircle.ToNurbsCurve(), circle.ToNurbsCurve() },
                        Point3d.Unset,
                        Point3d.Unset,
                        LoftType.Normal,
                        false
                    )[0];
                    ringSurfaces.Add(ring);
                }
            }

            // 3. 创建径向轴
            int axisCount = data.Count;
            double angleStep = 2 * Math.PI / axisCount;
            List<Line> radialAxes = new List<Line>();
            List<Point3d> axisEndPoints = new List<Point3d>();

            for (int i = 0; i < axisCount; i++)
            {
                double angle = i * angleStep;
                Vector3d direction = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
                Point3d endPoint = origin + direction * radii.Last();
                radialAxes.Add(new Line(origin, endPoint));
                axisEndPoints.Add(endPoint);
            }

            // 4. 创建雷达多边形
            Polyline radarPoly = new Polyline();
            for (int i = 0; i < data.Count; i++)
            {
                double angle = i * angleStep;
                double value = data[i];
                Vector3d direction = new Vector3d(Math.Cos(angle), Math.Sin(angle), 0);
                radarPoly.Add(origin + direction * value);
            }
            radarPoly.Add(radarPoly.First()); // 闭合多边形

            // 5. 计算同心圆与Y轴的交点（Y轴正方向）
            List<Point3d> circleYPoints = new List<Point3d>();
            foreach (double radius in radii)
            {
                circleYPoints.Add(origin + new Vector3d(0, radius, 0));
            }

            // 设置输出
            DA.SetData(0, radarPoly.ToNurbsCurve());
            DA.SetDataList(1, circles.Select(c => c.ToNurbsCurve()));
            DA.SetDataList(2, radii);
            DA.SetDataList(3, ringSurfaces);
            DA.SetDataList(4, radialAxes.Select(l => new LineCurve(l)));
            DA.SetDataList(5, circleYPoints);
            DA.SetDataList(6, axisEndPoints);
        }

        // 组件图标和ID
        public override Guid ComponentGuid => new Guid("012D1E22-B8B1-4104-B380-38FF6B397591");
        protected override System.Drawing.Bitmap Icon => Resources.icon_RadarChart;
    }
}
