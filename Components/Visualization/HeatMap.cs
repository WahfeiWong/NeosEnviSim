using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace NeosVisualization
{
    public class SpatialHeatmapComponent : GH_Component
    {
        private string _legendTitle = "Value";
        private double _textHeight = 0.3;
        private int _legendSegments = 10;
        private List<TextLabel> _textLabels;
        private BoundingBox _meshBBox;

        // Contour label fields
        private bool _enableContourLabels = false;
        private double _contourLabelHeight = 0.15;
        private List<TextLabel> _contourTextLabels;

        private class TextLabel
        {
            public Point3d Position;
            public string Text;
            public Color Color;
            public double Height;
            public TextHorizontalAlignment HAlign;
            public TextVerticalAlignment VAlign;
        }

        public SpatialHeatmapComponent()
          : base("Simple Heatmap", "Heatmap",
              "Color mesh faces based on input values and generate contour lines with color legend",
              "Neos", "Visualization")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Values", "V", "Analysis Result Values (Face Data)", GH_ParamAccess.list);
            pManager.AddMeshParameter("Mesh", "M", "Input Mesh", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Contour Count", "C", "Number of contour lines", GH_ParamAccess.item, 10);
            pManager.AddBooleanParameter("Contour Label Enable", "CE", "Enable contour line value labels", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Contour Label Height", "CLH", "Height of contour label text in model units", GH_ParamAccess.item, 0.15);
            pManager.AddIntegerParameter("Legend Segments", "LS", "Number of color segments in the legend bar (default 10)", GH_ParamAccess.item, 10);
            pManager.AddNumberParameter("Legend Text Height", "LTH", "Height of legend text in model units", GH_ParamAccess.item, 0.15);
            pManager.AddTextParameter("Legend Title", "LT", "Title displayed above the color legend", GH_ParamAccess.item, "Value");
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddCurveParameter("ContourLines", "L", "Contour Lines", GH_ParamAccess.list);
            pManager.AddMeshParameter("Heatmap", "H", "Colored Mesh", GH_ParamAccess.item);
            pManager.AddMeshParameter("Legend", "Lgd", "Color legend bar mesh", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // CRITICAL: Clear stored labels immediately so disconnected inputs
            // do not retain stale viewport annotations from previous run.
            _textLabels = new List<TextLabel>();
            _contourTextLabels = new List<TextLabel>();
            _meshBBox = BoundingBox.Empty;

            List<double> faceValues = new List<double>();
            Mesh inputMesh = null;
            int contourCount = 10;
            bool enableContourLabels = false;
            double contourLabelHeight = 0.15;
            int legendSegments = 10;
            double textHeight = 0.3;
            string legendTitle = "Value";

            if (!DA.GetDataList(0, faceValues)) { ExpirePreview(true); return; }
            if (!DA.GetData(1, ref inputMesh)) { ExpirePreview(true); return; }
            DA.GetData(2, ref contourCount);
            DA.GetData(3, ref enableContourLabels);
            DA.GetData(4, ref contourLabelHeight);
            DA.GetData(5, ref legendSegments);
            DA.GetData(6, ref textHeight);
            DA.GetData(7, ref legendTitle);

            _enableContourLabels = enableContourLabels;
            _contourLabelHeight = Math.Max(0.01, contourLabelHeight);
            _legendTitle = legendTitle ?? "Value";
            _textHeight = Math.Max(0.01, textHeight);
            _legendSegments = Math.Max(2, Math.Min(50, legendSegments));

            if (inputMesh == null || !inputMesh.IsValid) { ExpirePreview(true); return; }
            if (faceValues.Count != inputMesh.Faces.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"The number of input values ({faceValues.Count}) does not match the number of mesh faces ({inputMesh.Faces.Count})");
                ExpirePreview(true);
                return;
            }

            double min = faceValues.Min();
            double max = faceValues.Max();
            double range = max - min;

            // Generate Heatmap
            Mesh coloredMesh = new Mesh();
            coloredMesh.Vertices.Capacity = inputMesh.Faces.Count * 4;
            coloredMesh.VertexColors.Capacity = inputMesh.Faces.Count * 4;
            coloredMesh.Faces.Capacity = inputMesh.Faces.Count;

            for (int i = 0; i < inputMesh.Faces.Count; i++)
            {
                var face = inputMesh.Faces[i];
                double val = faceValues[i];
                double t = 0.5;
                if (range > 1e-9) t = (val - min) / range;
                Color c = ColorFromGradient(t);

                int vIndex = coloredMesh.Vertices.Count;
                coloredMesh.Vertices.Add(inputMesh.Vertices[face.A]);
                coloredMesh.Vertices.Add(inputMesh.Vertices[face.B]);
                coloredMesh.Vertices.Add(inputMesh.Vertices[face.C]);
                coloredMesh.VertexColors.Add(c);
                coloredMesh.VertexColors.Add(c);
                coloredMesh.VertexColors.Add(c);

                if (face.IsQuad)
                {
                    coloredMesh.Vertices.Add(inputMesh.Vertices[face.D]);
                    coloredMesh.VertexColors.Add(c);
                    coloredMesh.Faces.AddFace(vIndex, vIndex + 1, vIndex + 2, vIndex + 3);
                }
                else
                {
                    coloredMesh.Faces.AddFace(vIndex, vIndex + 1, vIndex + 2);
                }
            }
            coloredMesh.Normals.ComputeNormals();

            // Prepare contour data
            double[] vertexValues = new double[inputMesh.Vertices.Count];
            int[] vertexCounts = new int[inputMesh.Vertices.Count];
            for (int i = 0; i < inputMesh.Faces.Count; i++)
            {
                var f = inputMesh.Faces[i];
                double val = faceValues[i];
                vertexValues[f.A] += val; vertexCounts[f.A]++;
                vertexValues[f.B] += val; vertexCounts[f.B]++;
                vertexValues[f.C] += val; vertexCounts[f.C]++;
                if (f.IsQuad) { vertexValues[f.D] += val; vertexCounts[f.D]++; }
            }
            for (int i = 0; i < vertexValues.Length; i++)
            {
                if (vertexCounts[i] > 0) vertexValues[i] /= vertexCounts[i];
                else vertexValues[i] = min;
            }

            // Generate contour lines with level information preserved
            var allSegments = new System.Collections.Concurrent.ConcurrentBag<(double level, Curve curve)>();
            if (range > 1e-9 && contourCount > 0)
            {
                List<double> levels = new List<double>();
                double step = range / (double)(contourCount + 1);
                for (int i = 1; i <= contourCount; i++) levels.Add(min + step * i);
                Parallel.ForEach(inputMesh.Faces, face =>
                {
                    int[] ids = face.IsQuad ? new int[] { face.A, face.B, face.C, face.D } : new int[] { face.A, face.B, face.C };
                    int edgeCount = face.IsQuad ? 4 : 3;
                    foreach (double level in levels)
                    {
                        List<Point3d> pts = new List<Point3d>();
                        for (int j = 0; j < edgeCount; j++)
                        {
                            int id1 = ids[j], id2 = ids[(j + 1) % edgeCount];
                            double v1 = vertexValues[id1], v2 = vertexValues[id2];
                            if ((v1 <= level && v2 > level) || (v2 <= level && v1 > level))
                            {
                                double f = (level - v1) / (v2 - v1);
                                Point3d p1 = inputMesh.Vertices[id1], p2 = inputMesh.Vertices[id2];
                                pts.Add(p1 + (p2 - p1) * f);
                            }
                        }
                        if (pts.Count == 2) allSegments.Add((level, new LineCurve(pts[0], pts[1])));
                    }
                });
            }

            // Group line segments by level and join curves
            var linesByLevel = new Dictionary<double, List<Curve>>();
            foreach (var (level, curve) in allSegments)
            {
                if (!linesByLevel.ContainsKey(level))
                    linesByLevel[level] = new List<Curve>();
                linesByLevel[level].Add(curve);
            }

            List<Curve> joinedContours = new List<Curve>();
            List<double> joinedContourLevels = new List<double>();
            if (linesByLevel.Count > 0)
            {
                double joinTol = 1e-6;
                if (Rhino.RhinoDoc.ActiveDoc != null)
                    joinTol = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;

                foreach (var kvp in linesByLevel.OrderBy(k => k.Key))
                {
                    if (kvp.Value.Count > 0)
                    {
                        var joined = Curve.JoinCurves(kvp.Value, joinTol);
                        foreach (var jc in joined)
                        {
                            if (jc != null && jc.IsValid)
                            {
                                joinedContours.Add(jc);
                                joinedContourLevels.Add(kvp.Key);
                            }
                        }
                    }
                }
            }

            // Generate contour labels at midpoint of each contour line
            if (_enableContourLabels && joinedContours.Count > 0)
            {
                for (int i = 0; i < joinedContours.Count; i++)
                {
                    Curve contour = joinedContours[i];
                    double level = joinedContourLevels[i];
                    if (contour == null || !contour.IsValid) continue;

                    Point3d midPoint = contour.PointAtNormalizedLength(0.5);

                    _contourTextLabels.Add(new TextLabel
                    {
                        Position = midPoint,
                        Text = level.ToString("F2"),
                        Color = Color.Black,
                        Height = _contourLabelHeight,
                        HAlign = TextHorizontalAlignment.Center,
                        VAlign = TextVerticalAlignment.Middle
                    });
                }
            }

            // Generate Legend
            _meshBBox = inputMesh.GetBoundingBox(false);
            double meshHeight = _meshBBox.Max.Y - _meshBBox.Min.Y;
            if (meshHeight < 1e-6) meshHeight = 1.0;
            double legendGap = meshHeight * 0.05;
            double legendWidth = meshHeight / 20.0;
            int seg = _legendSegments;
            double labelOffset = legendWidth * 0.6;
            Point3d legendOrigin = new Point3d(_meshBBox.Max.X + legendGap, _meshBBox.Min.Y, _meshBBox.Min.Z);

            Mesh legendMesh = new Mesh();
            for (int i = 0; i < seg; i++)
            {
                double t0 = (double)i / seg, t1 = (double)(i + 1) / seg;
                double y0 = _meshBBox.Min.Y + t0 * meshHeight, y1 = _meshBBox.Min.Y + t1 * meshHeight;
                Color c0 = ColorFromGradient(t0);
                int v0 = legendMesh.Vertices.Count;
                legendMesh.Vertices.Add(new Point3d(legendOrigin.X, y0, legendOrigin.Z));
                legendMesh.Vertices.Add(new Point3d(legendOrigin.X + legendWidth, y0, legendOrigin.Z));
                legendMesh.Vertices.Add(new Point3d(legendOrigin.X + legendWidth, y1, legendOrigin.Z));
                legendMesh.Vertices.Add(new Point3d(legendOrigin.X, y1, legendOrigin.Z));
                legendMesh.VertexColors.Add(c0); legendMesh.VertexColors.Add(c0);
                legendMesh.VertexColors.Add(c0); legendMesh.VertexColors.Add(c0);
                legendMesh.Faces.AddFace(v0, v0 + 1, v0 + 2, v0 + 3);
            }
            legendMesh.Normals.ComputeNormals();

            // Title label
            _textLabels.Add(new TextLabel
            {
                Position = new Point3d(legendOrigin.X, _meshBBox.Max.Y + legendGap * 0.8, legendOrigin.Z),
                Text = _legendTitle,
                Color = Color.Black,
                Height = _textHeight * 1.1,
                HAlign = TextHorizontalAlignment.Left,
                VAlign = TextVerticalAlignment.Bottom
            });

            // Value labels (seg + 1 boundaries)
            for (int i = 0; i <= seg; i++)
            {
                double t = (double)i / seg;
                double val = min + t * range;
                double y = _meshBBox.Min.Y + t * meshHeight;
                _textLabels.Add(new TextLabel
                {
                    Position = new Point3d(legendOrigin.X + legendWidth + labelOffset, y, legendOrigin.Z),
                    Text = val.ToString("F2"),
                    Color = Color.Black,
                    Height = _textHeight,
                    HAlign = TextHorizontalAlignment.Left,
                    VAlign = TextVerticalAlignment.Middle
                });
            }

            DA.SetDataList(0, joinedContours);
            DA.SetData(1, coloredMesh);
            DA.SetData(2, legendMesh);
            this.OnDisplayExpired(true);
        }

        // Custom RGB color gradient stops (Blue to Red diverging colormap)
        private static readonly int[,] _gradientColors = new int[,]
        {
            { 49, 54, 149 },    // 0
            { 69, 117, 180 },   // 1
            { 116, 173, 209 },  // 2
            { 171, 217, 233 },  // 3
            { 224, 243, 248 },  // 4
            { 255, 255, 191 },  // 5
            { 254, 224, 144 },  // 6
            { 253, 174, 97 },   // 7
            { 244, 109, 67 },   // 8
            { 215, 48, 39 },    // 9
            { 165, 0, 38 }      // 10
        };

        private Color ColorFromGradient(double t)
        {
            // Clamp t to [0, 1]
            if (t <= 0.0) return Color.FromArgb(255, _gradientColors[0, 0], _gradientColors[0, 1], _gradientColors[0, 2]);
            if (t >= 1.0) return Color.FromArgb(255, _gradientColors[10, 0], _gradientColors[10, 1], _gradientColors[10, 2]);

            int n = 10; // number of intervals between 11 stops
            int idx = (int)(t * n); // 0-9
            if (idx >= n) idx = n - 1;

            double localT = (t - (double)idx / n) * n; // 0.0 - 1.0 within segment

            int r = (int)Math.Round(_gradientColors[idx, 0] + (_gradientColors[idx + 1, 0] - _gradientColors[idx, 0]) * localT);
            int g = (int)Math.Round(_gradientColors[idx, 1] + (_gradientColors[idx + 1, 1] - _gradientColors[idx, 1]) * localT);
            int b = (int)Math.Round(_gradientColors[idx, 2] + (_gradientColors[idx + 1, 2] - _gradientColors[idx, 2]) * localT);

            return Color.FromArgb(255, r, g, b);
        }

        public override void DrawViewportWires(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            // Draw legend text labels
            if (_textLabels != null)
            {
                foreach (TextLabel label in _textLabels)
                {
                    Plane textPlane = new Plane(label.Position, Vector3d.ZAxis);
                    args.Display.Draw3dText(label.Text, label.Color, textPlane, label.Height,
                        "Arial", false, false, label.HAlign, label.VAlign);
                }
            }
            // Draw contour line value labels
            if (_contourTextLabels != null)
            {
                foreach (TextLabel label in _contourTextLabels)
                {
                    Plane textPlane = new Plane(label.Position, Vector3d.ZAxis);
                    args.Display.Draw3dText(label.Text, label.Color, textPlane, label.Height,
                        "Arial", false, false, label.HAlign, label.VAlign);
                }
            }
        }

        public override BoundingBox ClippingBox
        {
            get
            {
                BoundingBox bbox = base.ClippingBox;
                if (_textLabels != null && _textLabels.Count > 0)
                    foreach (TextLabel label in _textLabels)
                        bbox.Union(new BoundingBox(label.Position, label.Position));
                if (_contourTextLabels != null && _contourTextLabels.Count > 0)
                    foreach (TextLabel label in _contourTextLabels)
                        bbox.Union(new BoundingBox(label.Position, label.Position));
                return bbox;
            }
        }

        public override Guid ComponentGuid => new Guid("B6D688CF-1F6D-4ECE-B7F4-E441CA409CE9");
        protected override System.Drawing.Bitmap Icon => Resources.icon_HeatMap;
    }
}
