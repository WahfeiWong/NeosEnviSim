using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ML.Core;
using NeosEnviSim.Properties;

namespace CitySemSegPlugin.Components
{
    public class YOLODetectorComponent : GH_Component
    {
        // COCO class names (80 classes)
        private static readonly string[] COCOClasses = new string[]
        {
            "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck", "boat",
            "traffic light", "fire hydrant", "stop sign", "parking meter", "bench", "bird", "cat",
            "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra", "giraffe", "backpack",
            "umbrella", "handbag", "tie", "suitcase", "frisbee", "skis", "snowboard", "sports ball",
            "kite", "baseball bat", "baseball glove", "skateboard", "surfboard", "tennis racket",
            "bottle", "wine glass", "cup", "fork", "knife", "spoon", "bowl", "banana", "apple",
            "sandwich", "orange", "broccoli", "carrot", "hot dog", "pizza", "donut", "cake", "chair",
            "couch", "potted plant", "bed", "dining table", "toilet", "tv", "laptop", "mouse",
            "remote", "keyboard", "cell phone", "microwave", "oven", "toaster", "sink", "refrigerator",
            "book", "clock", "vase", "scissors", "teddy bear", "hair drier", "toothbrush"
        };

        private static readonly object _lock = new object();
        private static InferenceSession _session;
        private static string _currentModelPath = string.Empty;

        public YOLODetectorComponent()
            : base("YOLO Detector", "YOLO",
                  "Run YOLO object detection using ONNX model (auto NMS). Input YOLO tensor from ImagePreProcessor.",
                  "Neos", "CityDetector")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("YOLOtensor", "YT", "YOLO tensor data from ImagePreProcessor", GH_ParamAccess.item);
            pManager.AddTextParameter("ModelPath", "M", "Path to YOLO ONNX model file", GH_ParamAccess.item);
            pManager.AddNumberParameter("ConfThreshold", "CT", "Confidence threshold ,default 0.25)", GH_ParamAccess.item, 0.25);
            pManager.AddNumberParameter("IoUThreshold", "IoUT", "IoU(Intersection over Union) threshold for NMS(Non-Maximum Suppression),default 0.7)", GH_ParamAccess.item, 0.7);
            pManager.AddBooleanParameter("Run", "R", "Run detection", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("ClassNames", "Names", "Detected class names", GH_ParamAccess.list);
            pManager.AddNumberParameter("Confidences", "Conf", "Detection confidences", GH_ParamAccess.list);
            pManager.AddPointParameter("Centers", "Centers", "Bounding box center points (image coordinates, y negative)", GH_ParamAccess.list);
            pManager.AddRectangleParameter("BoundingBoxes", "Recs", "Bounding box rectangles", GH_ParamAccess.list);
            pManager.AddTextParameter("AllClasses", "All", "All detectable class names (COCO,Common Objects in Context)", GH_ParamAccess.list);
            pManager.AddTextParameter("DebugInfo", "Info", "Debug information", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool run = false;
            DA.GetData(4, ref run);
            if (!run) return;

            GH_ObjectWrapper wrapper = null;
            string modelPath = "";
            double confThreshold = 0.25;
            double iouThreshold = 0.7;

            if (!DA.GetData(0, ref wrapper)) return;
            if (!DA.GetData(1, ref modelPath)) return;
            DA.GetData(2, ref confThreshold);
            DA.GetData(3, ref iouThreshold);

            YOLOTensorData yoloData = wrapper.Value as YOLOTensorData;
            if (yoloData?.Data == null) return;

            try
            {
                Stopwatch sw = Stopwatch.StartNew();

                // Get or create inference session (singleton)
                InferenceSession session = GetSession(modelPath);

                // Prepare input tensor
                var inputTensor = new DenseTensor<float>(yoloData.Data, new[] { 1, 3, yoloData.InputSize, yoloData.InputSize });
                string inputName = session.InputMetadata.Keys.First();
                var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };

                // Run inference
                using (var results = session.Run(inputs))
                {
                    var output = results.First().AsTensor<float>();
                    var dims = output.Dimensions.ToArray(); // Expected: [1, 84, 8400]

                    if (dims.Length != 3 || dims[0] != 1 || dims[1] != 84 || dims[2] != 8400)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                            $"Unexpected output shape: [{string.Join(",", dims)}]. Expected [1,84,8400].");
                        return;
                    }

                    int numPredictions = dims[2];
                    int numClasses = dims[1] - 4; // 84 - 4 = 80 (COCO)

                    // Post-process: parse predictions, apply confidence threshold, NMS
                    List<Detection> detections = ParseDetections(output, numPredictions, numClasses, (float)confThreshold);
                    List<Detection> finalDetections = NonMaxSuppression(detections, (float)iouThreshold);

                    // Convert coordinates to original image space
                    float scale = Math.Min((float)yoloData.InputSize / yoloData.OriginalWidth,
                                            (float)yoloData.InputSize / yoloData.OriginalHeight);
                    int newW = (int)(yoloData.OriginalWidth * scale);
                    int newH = (int)(yoloData.OriginalHeight * scale);
                    int padX = (yoloData.InputSize - newW) / 2;
                    int padY = (yoloData.InputSize - newH) / 2;

                    List<string> classNames = new List<string>();
                    List<double> confidences = new List<double>();
                    List<Point3d> centers = new List<Point3d>();
                    List<Rectangle3d> boxes = new List<Rectangle3d>();

                    foreach (var det in finalDetections)
                    {
                        // Convert normalized box [0..1] to original image pixels
                        float cx = (det.X - padX) / scale;
                        float cy = (det.Y - padY) / scale;
                        float w = det.Width / scale;
                        float h = det.Height / scale;

                        // Bounding box corners in image coordinates (origin top-left, y down)
                        float left = cx - w / 2;
                        float top = cy - h / 2;
                        float right = cx + w / 2;
                        float bottom = cy + h / 2;

                        // Clamp to image boundaries (optional)
                        left = Math.Max(0, left);
                        top = Math.Max(0, top);
                        right = Math.Min(yoloData.OriginalWidth, right);
                        bottom = Math.Min(yoloData.OriginalHeight, bottom);

                        // Prepare outputs (y negative for Rhino top view)
                        Point3d centerPt = new Point3d(cx, -cy, 0);
                        Point3d bottomLeft = new Point3d(left, -top, 0);      // top-left in image -> y negative for Rhino
                        Point3d bottomRight = new Point3d(right, -top, 0);
                        Point3d topRight = new Point3d(right, -bottom, 0);
                        Point3d topLeft = new Point3d(left, -bottom, 0);

                        Rectangle3d rect = new Rectangle3d(
                            new Plane(Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis),
                            bottomLeft,
                            topRight
                        );

                        classNames.Add(COCOClasses[det.ClassId]);
                        confidences.Add(det.Confidence);
                        centers.Add(centerPt);
                        boxes.Add(rect);
                    }

                    sw.Stop();

                    string debugInfo = $"Detection completed in {sw.ElapsedMilliseconds} ms\n" +
                                       $"Input size: {yoloData.InputSize}\n" +
                                       $"Original image: {yoloData.OriginalWidth} x {yoloData.OriginalHeight}\n" +
                                       $"Detections: {finalDetections.Count}";

                    DA.SetDataList(0, classNames);
                    DA.SetDataList(1, confidences);
                    DA.SetDataList(2, centers);
                    DA.SetDataList(3, boxes);
                    DA.SetDataList(4, COCOClasses); // All detectable classes
                    DA.SetData(5, debugInfo);
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "YOLO inference error: " + ex.Message);
            }
        }

        // Simple detection structure
        private class Detection
        {
            public int ClassId { get; set; }
            public float Confidence { get; set; }
            public float X { get; set; }      // center x (normalized 0..1)
            public float Y { get; set; }      // center y (normalized 0..1)
            public float Width { get; set; }  // normalized width
            public float Height { get; set; } // normalized height
        }

        private List<Detection> ParseDetections(Tensor<float> output, int numPredictions, int numClasses, float confThreshold)
        {
            List<Detection> detections = new List<Detection>();
            for (int i = 0; i < numPredictions; i++)
            {
                // Each prediction: [cx, cy, w, h, class1, class2, ...]
                float cx = output[0, 0, i];
                float cy = output[0, 1, i];
                float w = output[0, 2, i];
                float h = output[0, 3, i];

                // Find best class
                float maxConf = 0;
                int bestClass = -1;
                for (int c = 0; c < numClasses; c++)
                {
                    float conf = output[0, 4 + c, i];
                    if (conf > maxConf)
                    {
                        maxConf = conf;
                        bestClass = c;
                    }
                }

                if (bestClass >= 0 && maxConf > confThreshold)
                {
                    detections.Add(new Detection
                    {
                        ClassId = bestClass,
                        Confidence = maxConf,
                        X = cx,
                        Y = cy,
                        Width = w,
                        Height = h
                    });
                }
            }
            return detections;
        }

        private List<Detection> NonMaxSuppression(List<Detection> detections, float iouThreshold)
        {
            // Group by class
            var grouped = detections.GroupBy(d => d.ClassId);
            List<Detection> result = new List<Detection>();

            foreach (var group in grouped)
            {
                var list = group.OrderByDescending(d => d.Confidence).ToList();
                while (list.Count > 0)
                {
                    Detection best = list[0];
                    result.Add(best);
                    list.RemoveAt(0);

                    // Remove boxes with IoU > threshold with the best
                    list.RemoveAll(d => CalculateIoU(best, d) > iouThreshold);
                }
            }
            return result;
        }

        private float CalculateIoU(Detection a, Detection b)
        {
            // Convert normalized boxes to (x1,y1,x2,y2) format
            float a_x1 = a.X - a.Width / 2;
            float a_y1 = a.Y - a.Height / 2;
            float a_x2 = a.X + a.Width / 2;
            float a_y2 = a.Y + a.Height / 2;

            float b_x1 = b.X - b.Width / 2;
            float b_y1 = b.Y - b.Height / 2;
            float b_x2 = b.X + b.Width / 2;
            float b_y2 = b.Y + b.Height / 2;

            float interX1 = Math.Max(a_x1, b_x1);
            float interY1 = Math.Max(a_y1, b_y1);
            float interX2 = Math.Min(a_x2, b_x2);
            float interY2 = Math.Min(a_y2, b_y2);

            float interArea = Math.Max(0, interX2 - interX1) * Math.Max(0, interY2 - interY1);
            float areaA = (a_x2 - a_x1) * (a_y2 - a_y1);
            float areaB = (b_x2 - b_x1) * (b_y2 - b_y1);

            return interArea / (areaA + areaB - interArea);
        }

        // Singleton model manager (reuse SegFormerModelManager pattern)
        private InferenceSession GetSession(string modelPath)
        {
            lock (_lock)
            {
                if (_session == null || _currentModelPath != modelPath)
                {
                    _session?.Dispose();
                    SessionOptions options = new SessionOptions();
                    options.AppendExecutionProvider_CPU(0);
                    _session = new InferenceSession(modelPath, options);
                    _currentModelPath = modelPath;
                }
                return _session;
            }
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_detector;
        public override Guid ComponentGuid => new Guid("43E23610-3663-4B4E-9DCC-FA6095C36F52");
    }
}