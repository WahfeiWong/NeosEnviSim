using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace NeosAcoustic
{
    public class SoundVisualization : GH_Component
    {
        public SoundVisualization()
          : base("Sound Visualization", "SoundVis",
              "Create concentric rings whose heights are driven by a list of sound values.",
              "Neos", "Acoustic")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Values", "V", "Sound values list (length = number of circles)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Scale Factor", "S", "Multiplier for each value", GH_ParamAccess.item, 100.0);
            pManager.AddNumberParameter("Extra Offset", "O", "Constant added to scaled value (e.g., +15)", GH_ParamAccess.item, 15.0);
            pManager.AddNumberParameter("Start Radius", "Sr", "Radius of innermost circle", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Step Radius", "St", "Radius increment per circle", GH_ParamAccess.item, 1.0);
            pManager.AddIntegerParameter("Segments", "N", "Number of division points per circle", GH_ParamAccess.item, 50);
            pManager.AddPlaneParameter("Plane", "P", "Base plane for all circles", GH_ParamAccess.item, Plane.WorldXY);
            pManager.AddBooleanParameter("Reverse", "R", "Reverse the order of values (default true)", GH_ParamAccess.item, true);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("Curves", "C", "Generated concentric circles (polylines)", GH_ParamAccess.list);
            pManager.AddPointParameter("Points", "P", "All division points", GH_ParamAccess.list);
            pManager.AddColourParameter("Colors", "Col", "Colors based on distance to origin", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Inputs
            List<double> values = new List<double>();
            double scale = 100.0, offset = 15.0, startR = 0.0, stepR = 1.0;
            int segments = 50;
            Plane plane = Plane.WorldXY;
            bool reverse = true;

            if (!DA.GetDataList(0, values) || values.Count == 0) return;
            DA.GetData(1, ref scale);
            DA.GetData(2, ref offset);
            DA.GetData(3, ref startR);
            DA.GetData(4, ref stepR);
            DA.GetData(5, ref segments);
            DA.GetData(6, ref plane);
            DA.GetData(7, ref reverse);

            segments = Math.Max(3, segments);
            int circleCount = values.Count;

            // Transform values: Z = value * scale + offset
            List<double> heights = new List<double>();
            foreach (double v in values)
                heights.Add(v * scale + offset);
            if (reverse) heights.Reverse();

            // Generate points on each circle
            List<Point3d>[] circlePts = new List<Point3d>[circleCount];
            double[] radii = new double[circleCount];

            for (int i = 0; i < circleCount; i++)
            {
                double r = startR + i * stepR;
                radii[i] = r;
                double z = heights[i];

                List<Point3d> pts = new List<Point3d>();
                for (int j = 0; j < segments; j++)
                {
                    double angle = 2 * Math.PI * j / segments;
                    double x = r * Math.Cos(angle);
                    double y = r * Math.Sin(angle);
                    pts.Add(plane.PointAt(x, y, z));
                }
                circlePts[i] = pts;
            }

            // Build output lists
            List<Curve> curves = new List<Curve>();
            List<Point3d> allPoints = new List<Point3d>();
            List<Color> colors = new List<Color>();

            // Closed polylines for each circle (the only curves output)
            for (int i = 0; i < circleCount; i++)
            {
                Polyline poly = new Polyline(circlePts[i]);
                poly.Add(poly[0]); // close
                if (poly.IsValid)
                    curves.Add(new PolylineCurve(poly));
            }

            // Collect all points and compute max distance for color normalization
            double maxDist = 0;
            for (int i = 0; i < circleCount; i++)
                foreach (Point3d p in circlePts[i])
                {
                    double d = p.DistanceTo(Point3d.Origin);
                    if (d > maxDist) maxDist = d;
                    allPoints.Add(p);
                }

            // Gradient stops from original GH script
            Color[] gradient = new Color[]
            {
                Color.FromArgb(234, 38, 0),    // red
                Color.FromArgb(234, 126, 0),   // orange
                Color.FromArgb(254, 244, 84),  // yellow
                Color.FromArgb(173, 203, 249), // light blue
                Color.FromArgb(75, 107, 169)   // dark blue
            };

            for (int i = 0; i < circleCount; i++)
                for (int j = 0; j < segments; j++)
                {
                    double dist = circlePts[i][j].DistanceTo(Point3d.Origin);
                    double t = (maxDist > 0) ? dist / maxDist : 0;
                    t = Math.Max(0, Math.Min(1, t));
                    colors.Add(InterpolateColor(gradient, t));
                }

            DA.SetDataList(0, curves);
            DA.SetDataList(1, allPoints);
            DA.SetDataList(2, colors);
        }

        private Color InterpolateColor(Color[] colors, double t)
        {
            if (colors.Length == 0) return Color.Black;
            if (colors.Length == 1) return colors[0];
            if (t <= 0) return colors[0];
            if (t >= 1) return colors[colors.Length - 1];

            double step = 1.0 / (colors.Length - 1);
            int idx = (int)(t / step);
            double local = (t - idx * step) / step;

            Color c1 = colors[idx];
            Color c2 = colors[idx + 1];

            int r = (int)(c1.R + (c2.R - c1.R) * local);
            int g = (int)(c1.G + (c2.G - c1.G) * local);
            int b = (int)(c1.B + (c2.B - c1.B) * local);
            int a = (int)(c1.A + (c2.A - c1.A) * local);
            return Color.FromArgb(a, r, g, b);
        }

        public override Guid ComponentGuid => new Guid("57505c40-048b-4658-a149-fa0e69a85d53");
        protected override Bitmap Icon => Resources.icon_soundVisualization;
    }
}