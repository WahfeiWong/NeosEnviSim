using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;

namespace NeosOptics
{
    public class MelanopicDERComponent : GH_Component
    {
        // 视黑素及明视觉响应数据表 (380-780nm, 5nm间隔)
        private static readonly double[] Wavelengths = Enumerable.Range(0, 81).Select(i => 380.0 + i * 5.0).ToArray();

        private static readonly double[] MelanopicSensitivity = new double[] {
            0.000010, 0.000019, 0.000035, 0.000067, 0.000130, 0.000260, 0.000526, 0.000906, 0.001565, 0.002134,
            0.002895, 0.003658, 0.004580, 0.005406, 0.006315, 0.007182, 0.008076, 0.008956, 0.009812, 0.010467,
            0.011013, 0.011299, 0.011406, 0.011315, 0.011017, 0.010519, 0.009842, 0.008956, 0.007980, 0.006951,
            0.005923, 0.004933, 0.004011, 0.003184, 0.002460, 0.001848, 0.001352, 0.000962, 0.000670, 0.000456,
            0.000307, 0.000204, 0.000134, 0.000088, 0.000058, 0.000038, 0.000025, 0.000016, 0.000011, 0.000007,
            0.000005, 0.000003, 0.000002, 0.000001, 0.000001, 0.000001, 0.000000, 0.000000, 0.000000, 0.000000,
            0.000000, 0.000000, 0.000000, 0.000000, 0.000000, 0.000000, 0.000000, 0.000000, 0.000000, 0.000000,
            0.000000, 0.000000, 0.000000, 0.000000, 0.000000, 0.000000, 0.000000, 0.000000, 0.000000, 0.000000,
            0.000000
        };

        private static readonly double[] PhotopicSensitivity = new double[] {
            0.000039, 0.000064, 0.000120, 0.000217, 0.000396, 0.000640, 0.001210, 0.002180, 0.004000, 0.007300,
            0.011600, 0.016840, 0.023000, 0.029800, 0.038000, 0.048000, 0.060000, 0.073900, 0.090980, 0.112600,
            0.139020, 0.169300, 0.208020, 0.258600, 0.323000, 0.407300, 0.503000, 0.608200, 0.710000, 0.793200,
            0.862000, 0.914850, 0.954000, 0.980300, 0.994950, 1.000000, 0.995000, 0.978600, 0.952000, 0.915400,
            0.870000, 0.816300, 0.757000, 0.694900, 0.631000, 0.566800, 0.503000, 0.441200, 0.381000, 0.321000,
            0.265000, 0.217000, 0.175000, 0.138200, 0.107000, 0.081600, 0.061000, 0.044580, 0.032000, 0.023200,
            0.017000, 0.011920, 0.008210, 0.005723, 0.004102, 0.002929, 0.002091, 0.001484, 0.001047, 0.000740,
            0.000520, 0.000361, 0.000249, 0.000172, 0.000120, 0.000085, 0.000060, 0.000042, 0.000030, 0.000021,
            0.000015
        };

        public MelanopicDERComponent()
          : base("Melanopic DER", "MDER",
              "Calculates Melanopic Daylight Efficacy Ratio (MDER) from SPD data.\nMDER is independent of the normalization factor, " +
                "as it relies solely on absolute spectral irradiance integrals.",
              "Neos", "Optics")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("SPD File Path", "SFP", "Path to SPD text file (.txt).\nThe format of each line of data is: wavelength (unit: nm) + space + irradiance value (unit: W/(m²·nm)),The wavelength interval of the SPD data is not restricted to 1nm.", GH_ParamAccess.item);
            pManager.AddNumberParameter("Normalization Factor", "NF", "Factor to denormalize SPD data (default 1.0).\n" +
                "MDER is independent of the normalization factor,as it relies solely on absolute spectral irradiance integrals.", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Km", "Km", "Maximum spectral luminous efficacy for photopic vision (default 683.002 lm/W)", GH_ParamAccess.item, 683.002);
            pManager.AddNumberParameter("γ（gama）", "γ", "Conversion constant (conversion factor between EML and melanopic EDI values,default 0.9063)", GH_ParamAccess.item, 0.9063);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("MDER", "MDER", "Melanopic Daylight Efficacy Ratio(Melanopic Ratio)", GH_ParamAccess.item);
            pManager.AddNumberParameter("EML", "EML", "Equivalent Melanopic Lux(lux)", GH_ParamAccess.item);
            pManager.AddNumberParameter("mEDI", "mEDI", "Melanopic Equivalent Daylight Illuminance(lux)", GH_ParamAccess.item);
            pManager.AddNumberParameter("Visual Lux", "Lux", "Visual illuminance (lux)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 获取输入参数
            string filePath = "";
            double normalizationFactor = 1.0;
            double km = 683.002;
            double gama = 0.9063;

            if (!DA.GetData(0, ref filePath)) return;
            if (!DA.GetData(1, ref normalizationFactor)) return;
            if (!DA.GetData(2, ref km)) return;
            if (!DA.GetData(3, ref gama)) return;

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Invalid SPD file path");
                return;
            }

            try
            {
                // 读取SPD文件
                var spdData = ReadSpdFile(filePath);

                if (spdData.Count == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "No valid SPD data found");
                    return;
                }

                // 应用归一化因子还原SPD数据
                if (normalizationFactor != 1.0)
                {
                    foreach (var dataPoint in spdData)
                    {
                        dataPoint.Intensity *= normalizationFactor;
                    }
                }

                // 检查SPD数据是否已归一化（最大值为1）
                double maxIntensity = spdData.Max(d => d.Intensity);
                if (Math.Abs(maxIntensity - 1.0) < 0.01 && normalizationFactor == 1.0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        "SPD appears normalized but normalization factor is 1.0.EML and EDI results may be incorrect.");
                }

                // 检查波长范围
                double minWavelength = spdData.Min(d => d.Wavelength);
                double maxWavelength = spdData.Max(d => d.Wavelength);

                if (minWavelength > 380 || maxWavelength < 780)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                        $"SPD data range ({minWavelength}-{maxWavelength}nm) does not fully cover 380-780nm range");
                }

                // 插值函数值
                foreach (var dataPoint in spdData)
                {
                    dataPoint.MelanopicValue = Interpolate(Wavelengths, MelanopicSensitivity, dataPoint.Wavelength);
                    dataPoint.PhotopicValue = Interpolate(Wavelengths, PhotopicSensitivity, dataPoint.Wavelength);
                }

                // 按波长排序
                spdData = spdData.OrderBy(d => d.Wavelength).ToList();

                // 计算积分
                double melanopicIntegral = CalculateIntegral(spdData, d => d.MelanopicValue * d.Intensity);
                double photopicIntegral = CalculateIntegral(spdData, d => d.PhotopicValue * d.Intensity);

                // 计算最终值
                double eml = 106.857 * km * melanopicIntegral;
                double edi = eml * gama;
                double visualLux = km * photopicIntegral;
                double mder = visualLux > 0 ? eml / visualLux : 0;

                // 设置输出
                DA.SetData(0, mder);
                DA.SetData(1, eml);
                DA.SetData(2, edi);
                DA.SetData(3, visualLux);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Processing error: {ex.Message}");
            }
        }

        // 读取SPD文件
        private List<SpdData> ReadSpdFile(string filePath)
        {
            var spdData = new List<SpdData>();

            foreach (string line in File.ReadAllLines(filePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 2 &&
                    double.TryParse(parts[0], out double wavelength) &&
                    double.TryParse(parts[1], out double intensity))
                {
                    spdData.Add(new SpdData(wavelength, intensity));
                }
            }

            return spdData;
        }

        // 线性插值
        private double Interpolate(double[] xValues, double[] yValues, double x)
        {
            // 在边界外直接返回边界值
            if (x <= xValues[0]) return yValues[0];
            if (x >= xValues[xValues.Length - 1]) return yValues[yValues.Length - 1];

            // 查找最近的索引
            int index = Array.BinarySearch(xValues, x);
            if (index >= 0) return yValues[index]; // 精确匹配

            // 线性插值
            index = ~index;
            double x0 = xValues[index - 1];
            double x1 = xValues[index];
            double y0 = yValues[index - 1];
            double y1 = yValues[index];

            return y0 + (y1 - y0) * (x - x0) / (x1 - x0);
        }

        // 使用梯形法则计算数值积分
        private double CalculateIntegral(List<SpdData> data, Func<SpdData, double> valueSelector)
        {
            double integral = 0.0;

            for (int i = 1; i < data.Count; i++)
            {
                double width = data[i].Wavelength - data[i - 1].Wavelength;
                double avgValue = (valueSelector(data[i - 1]) + valueSelector(data[i])) / 2.0;
                integral += avgValue * width;
            }

            return integral;
        }

        // SPD数据点类（增加可写属性）
        private class SpdData
        {
            public double Wavelength { get; }
            public double Intensity { get; set; } // 改为可写属性
            public double MelanopicValue { get; set; }
            public double PhotopicValue { get; set; }

            public SpdData(double wavelength, double intensity)
            {
                Wavelength = wavelength;
                Intensity = intensity;
            }
        }

        // 组件图标和GUID
        protected override System.Drawing.Bitmap Icon => Resources.icon_EML; 
        public override Guid ComponentGuid => new Guid("89ACFACE-D473-4960-B3DC-A2198C0A4538");
    }
}


