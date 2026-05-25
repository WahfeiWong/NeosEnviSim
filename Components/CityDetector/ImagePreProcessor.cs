using System;
using System.Drawing;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using ML.Core;
using NeosEnviSim.Properties;

namespace CitySemSegPlugin.Components
{
    public class ImagePreProcessorComponent : GH_Component
    {
        public ImagePreProcessorComponent()
            : base("Image PreProcessor", "ImgPre",
                  "Preprocess image for CitySemSegFormer (1820x1024) and YOLO (square input).\n" +
                  "The image is center-cropped to 16:9, resized to specified Width/Height.\n" +
                  "YOLO tensor is generated from the resized image with letterbox padding.",
                  "Neos", "CityDetector")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("ImagePath", "P", "File path to the image", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Width", "W", "Target image width for SegFormer,default 1820", GH_ParamAccess.item, 1820);
            pManager.AddIntegerParameter("Height", "H", "Target image height for SegFormer,,default 1024", GH_ParamAccess.item, 1024);          
            pManager.AddIntegerParameter("YOLOInputSize", "YS", "YOLO model input size (square),default 640", GH_ParamAccess.item, 640);
            pManager.AddBooleanParameter("ColorMode", "C", "True for RGB, False for HSV,,default true", GH_ParamAccess.item, true);
            pManager.AddBooleanParameter("Run", "R", "Run processing", GH_ParamAccess.item, false);  
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("TensorData", "T", "Output tensor for SegFormer model", GH_ParamAccess.item);
            pManager.AddGenericParameter("YOLOTensor", "YT", "Output tensor for YOLO model (with metadata)", GH_ParamAccess.item);
            pManager.AddPointParameter("Points", "P", "Pixel center points (for visualization)", GH_ParamAccess.list);
            pManager.AddVectorParameter("Colors", "Col", "RGB/HSV color vectors", GH_ParamAccess.list);
            pManager.AddMeshParameter("PixelMesh", "M", "Mesh where each pixel corresponds to a quad face", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool run = false;
            DA.GetData(5, ref run);
            if (!run) return;

            string path = "";
            int width = 1820, height = 1024;
            int yoloSize = 640;
            bool colorMode = true;

            if (!DA.GetData(0, ref path)) return;
            DA.GetData(1, ref width);
            DA.GetData(2, ref height);
            DA.GetData(3, ref yoloSize);
            DA.GetData(4, ref colorMode);
            

            using (Bitmap original = new Bitmap(path))
            using (Bitmap resized = new Bitmap(width, height))
            {
                // --- Step 1: Crop & resize to target 16:9 for SegFormer ---
                using (Graphics g = Graphics.FromImage(resized))
                {
                    float targetRatio = (float)width / height;
                    float origRatio = (float)original.Width / original.Height;

                    int cropW = original.Width;
                    int cropH = original.Height;
                    int cropX = 0, cropY = 0;

                    if (origRatio > targetRatio)
                    {
                        cropW = (int)(original.Height * targetRatio);
                        cropX = (original.Width - cropW) / 2;
                    }
                    else
                    {
                        cropH = (int)(original.Width / targetRatio);
                        cropY = (original.Height - cropH) / 2;
                    }

                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(original, new Rectangle(0, 0, width, height), new Rectangle(cropX, cropY, cropW, cropH), GraphicsUnit.Pixel);
                }

                // --- Step 2: Build SegFormer tensor, points, colors, pixel mesh (unchanged) ---
                float[] segTensor = new float[3 * width * height];
                List<Point3d> points = new List<Point3d>(width * height);
                List<Vector3d> colors = new List<Vector3d>(width * height);

                float[] mean = new float[] { 0.485f, 0.456f, 0.406f };
                float[] std = new float[] { 0.229f, 0.224f, 0.225f };

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        Color px = resized.GetPixel(x, y);
                        points.Add(new Point3d(x, -y, 0));

                        if (colorMode)
                            colors.Add(new Vector3d(px.R, px.G, px.B));
                        else
                            colors.Add(new Vector3d(px.GetHue(), px.GetSaturation(), px.GetBrightness()));

                        int rIdx = 0 * (width * height) + y * width + x;
                        int gIdx = 1 * (width * height) + y * width + x;
                        int bIdx = 2 * (width * height) + y * width + x;

                        segTensor[rIdx] = ((px.R / 255f) - mean[0]) / std[0];
                        segTensor[gIdx] = ((px.G / 255f) - mean[1]) / std[1];
                        segTensor[bIdx] = ((px.B / 255f) - mean[2]) / std[2];
                    }
                }

                // Pixel mesh (independent quads)
                Mesh pixelMesh = new Mesh();
                int totalQuads = width * height;
                pixelMesh.Vertices.Capacity = 4 * totalQuads;
                pixelMesh.Faces.Capacity = totalQuads;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        pixelMesh.Vertices.Add(x, -y, 0);          // BL
                        pixelMesh.Vertices.Add(x + 1, -y, 0);      // BR
                        pixelMesh.Vertices.Add(x + 1, -y - 1, 0);  // TR
                        pixelMesh.Vertices.Add(x, -y - 1, 0);      // TL
                        int baseIdx = 4 * (y * width + x);
                        pixelMesh.Faces.AddFace(baseIdx, baseIdx + 1, baseIdx + 2, baseIdx + 3);
                    }
                }
                pixelMesh.Compact();

                // --- Step 3: Build YOLO tensor from resized image (letterbox to square) ---
                float[] yoloTensor = GenerateYOLOTensor(resized, yoloSize, out int padX, out int padY, out float scale);

                YOLOTensorData yoloData = new YOLOTensorData
                {
                    Data = yoloTensor,
                    InputSize = yoloSize,
                    OriginalWidth = width,
                    OriginalHeight = height
                };

                TensorDataWrapper segWrapper = new TensorDataWrapper { Data = segTensor, Width = width, Height = height };

                // --- Set outputs (new order) ---
                DA.SetData(0, new Grasshopper.Kernel.Types.GH_ObjectWrapper(segWrapper));
                DA.SetData(1, new Grasshopper.Kernel.Types.GH_ObjectWrapper(yoloData));  // New YOLO tensor
                DA.SetDataList(2, points);
                DA.SetDataList(3, colors);
                DA.SetData(4, pixelMesh);
            }
        }

        private float[] GenerateYOLOTensor(Bitmap src, int targetSize, out int padX, out int padY, out float scale)
        {
            // Letterbox resize: keep aspect ratio, pad with gray (114) to square
            int srcW = src.Width;
            int srcH = src.Height;
            scale = Math.Min((float)targetSize / srcW, (float)targetSize / srcH);
            int newW = (int)(srcW * scale);
            int newH = (int)(srcH * scale);
            padX = (targetSize - newW) / 2;
            padY = (targetSize - newH) / 2;

            using (Bitmap square = new Bitmap(targetSize, targetSize))
            using (Graphics g = Graphics.FromImage(square))
            {
                g.Clear(Color.FromArgb(114, 114, 114)); // YOLO typical padding color
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, new Rectangle(padX, padY, newW, newH));

                float[] tensor = new float[3 * targetSize * targetSize];
                for (int y = 0; y < targetSize; y++)
                {
                    for (int x = 0; x < targetSize; x++)
                    {
                        Color c = square.GetPixel(x, y);
                        // Normalize to [0,1]
                        tensor[0 * targetSize * targetSize + y * targetSize + x] = c.R / 255f;
                        tensor[1 * targetSize * targetSize + y * targetSize + x] = c.G / 255f;
                        tensor[2 * targetSize * targetSize + y * targetSize + x] = c.B / 255f;
                    }
                }
                return tensor;
            }
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_imageProcessor;
        public override Guid ComponentGuid => new Guid("AC9FB54B-56DC-470C-BC66-B8C458C78B1D");
    }
}