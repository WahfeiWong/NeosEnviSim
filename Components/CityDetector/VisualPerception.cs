using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;

namespace CitySemSegPlugin.Components
{
    public class VisualPerceptionComponent : GH_Component
    {
        public VisualPerceptionComponent()
            : base("Visual Perception", "VisPerc", "Compute Visual Entropy (VE) and Color Richness Index (CRI)", "Neos", "CityDetector") { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Points", "P", "Pixel center points (reserved for future use)", GH_ParamAccess.list);
            pManager.AddVectorParameter("Colors", "Col", "Pixel colors (RGB Vector3d, values in range 0-255)", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Color Richness Index (CRI)", "CRI", "Colorfulness metric (Hasler & Süsstrunk)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Visual Entropy (VE)", "VE", "Shannon entropy based on grayscale histogram", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Point3d> points = new List<Point3d>();
            List<Vector3d> colors = new List<Vector3d>();

            if (!DA.GetDataList(0, points)) return;
            if (!DA.GetDataList(1, colors)) return;

            if (colors.Count == 0) return;

            int totalPixels = colors.Count;

            // ==========================================
            // 准备工作：灰度直方图 (用于 VE) 与 色彩累加器 (用于 CRI)
            // ==========================================
            int[] histogram = new int[256];

            double sumRg = 0, sumRg2 = 0;
            double sumYb = 0, sumYb2 = 0;

            for (int i = 0; i < totalPixels; i++)
            {
                Vector3d c = colors[i];

                // 如果不是 RGB 值 (0-255), 抛出异常提示信息
                if (c.X < 0 || c.X > 255 || c.Y < 0 || c.Y > 255 || c.Z < 0 || c.Z > 255)
                {
                    throw new Exception("Colors must be RGB vectors with each component in range 0-255.");
                }

                double R = c.X;
                double G = c.Y;
                double B = c.Z;

                // RGB 转灰度 (心理学亮度公式)
                int gray = (int)(0.299 * R + 0.587 * G + 0.114 * B);

                // CRI 色彩对立空间计算 (Red-Green 和 Yellow-Blue)
                double rg = R - G;
                double yb = 0.5 * (R + G) - B;

                sumRg += rg;
                sumRg2 += rg * rg;
                sumYb += yb;
                sumYb2 += yb * yb;

                // 约束边界并计入直方图
                if (gray < 0) gray = 0;
                if (gray > 255) gray = 255;
                histogram[gray]++;
            }

            // ==========================================
            // 指标 1：计算视觉图像熵 Visual Entropy (VE)
            // ==========================================
            double entropy = 0.0;
            for (int i = 0; i < 256; i++)
            {
                if (histogram[i] > 0)
                {
                    double p = (double)histogram[i] / totalPixels;
                    entropy -= p * Math.Log(p, 2.0);
                }
            }

            // ==========================================
            // 指标 2：计算色彩丰富度 Color Richness Index (CRI)
            // ==========================================
            double cri = 0.0;
            // 求 rg 与 yb 的均值与标准差
            double meanRg = sumRg / totalPixels;
            double varRg = (sumRg2 / totalPixels) - (meanRg * meanRg);
            double stdRg = Math.Sqrt(Math.Max(0, varRg));

            double meanYb = sumYb / totalPixels;
            double varYb = (sumYb2 / totalPixels) - (meanYb * meanYb);
            double stdYb = Math.Sqrt(Math.Max(0, varYb));

            // Hasler and Süsstrunk 色彩丰富度标准公式
            double stdRoot = Math.Sqrt(stdRg * stdRg + stdYb * stdYb);
            double meanRoot = Math.Sqrt(meanRg * meanRg + meanYb * meanYb);

            cri = stdRoot + 0.3 * meanRoot;

            DA.SetData(0, cri);
            DA.SetData(1, entropy);
        }
        protected override System.Drawing.Bitmap Icon => Resources.icon_visualPerception;
        public override Guid ComponentGuid => new Guid("FB206294-9684-4CA5-9640-BD87E6CF52C1");
    }
}

//Resources.icon_visualPerception