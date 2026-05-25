using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ML.Core;
using NeosEnviSim.Properties;

namespace CitySemSegPlugin.Components
{
    public class SemanticSegmentationComponent : GH_Component
    {
        public SemanticSegmentationComponent()
            : base("City Semantic Segmentator", "City Segmentator", 
                  "Run ONNX(Open Neural Network Exchange) inference (smart parsing built-in Argmax)", "Neos", "CityDetector") { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("TensorData", "T", "Preprocessed Tensor", GH_ParamAccess.item);
            pManager.AddTextParameter("ModelPath", "M", "File path to ONNX model(Such as CitySemSegFormer Model named citysemsegformer.onnx)", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Run", "R", "Run inference", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddIntegerParameter("TrainIDs", "ID", "Class ID per pixel", GH_ParamAccess.list);
            pManager.AddTextParameter("DebugInfo", "Info", "Model original shape and diagnostic info", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            bool run = false;
            DA.GetData(2, ref run);
            if (!run) return;

            GH_ObjectWrapper wrapper = null;
            string modelPath = "";

            if (!DA.GetData(0, ref wrapper)) return;
            if (!DA.GetData(1, ref modelPath)) return;

            TensorDataWrapper tensorData = wrapper.Value as TensorDataWrapper;
            if (tensorData == null || tensorData.Data == null) return;

            try
            {
                var session = SegFormerModelManager.GetSession(modelPath);
                if (session == null) return;

                // 1. 构建标准的 NCHW 输入张量 (模型明确要求 [1, 3, H, W])
                string inputName = session.InputMetadata.Keys.First();
                var inputTensor = new DenseTensor<float>(tensorData.Data, new int[] { 1, 3, tensorData.Height, tensorData.Width });
                var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) };

                // 2. 运行推理
                using (var results = session.Run(inputs))
                {
                    var firstResult = results.First();
                    if (firstResult == null) return;

                    // 提取各个格式的张量引用
                    var floatOut = firstResult.AsTensor<float>();
                    var longOut = firstResult.AsTensor<long>();
                    var intOut = firstResult.AsTensor<int>();

                    // 获取模型的真实输出维度
                    int[] dims = floatOut?.Dimensions.ToArray() ??
                                 longOut?.Dimensions.ToArray() ??
                                 intOut?.Dimensions.ToArray();

                    if (dims == null) throw new Exception("Unable to get output dimensions!");
                    string rawShape = string.Join(",", dims);

                    int outH = 0;
                    int outW = 0;
                    bool isAlreadyArgmaxed = false;

                    // ==========================================
                    // 【核心修复】：精准定位高度(H)和宽度(W)
                    // ==========================================
                    if (dims.Length == 4)
                    {
                        if (dims[3] == 1)
                        {
                            // 命中了您的模型：[1, 1024, 1820, 1]
                            outH = dims[1];
                            outW = dims[2];
                            isAlreadyArgmaxed = true;
                        }
                        else if (dims[1] == 1 && !dims.Contains(19))
                        {
                            // 备用格式：[1, 1, 1024, 1820]
                            outH = dims[2];
                            outW = dims[3];
                            isAlreadyArgmaxed = true;
                        }
                        else
                        {
                            // 传统的 19 通道概率图格式
                            isAlreadyArgmaxed = false;
                            outH = dims[2];
                            outW = dims[3];
                        }
                    }
                    else if (dims.Length == 3)
                    {
                        // 命中了剥离通道的降维格式：[1, 1024, 1820]
                        outH = dims[1];
                        outW = dims[2];
                        isAlreadyArgmaxed = true;
                    }

                    if (outH == 0 || outW == 0) throw new Exception("Failed to parse valid height/width dimensions!");

                    int[] rawIds = new int[outH * outW];

                    // ==========================================
                    // 【核心修复】：直接读取 ID 数据，杜绝错位
                    // ==========================================
                    if (isAlreadyArgmaxed)
                    {
                        // 既然是 [1, 1024, 1820, 1]，说明里面存的就是 ID，直接线性读取！
                        Parallel.For(0, outH * outW, i =>
                        {
                            if (floatOut != null) rawIds[i] = (int)floatOut.GetValue(i);
                            else if (longOut != null) rawIds[i] = (int)longOut.GetValue(i);
                            else if (intOut != null) rawIds[i] = intOut.GetValue(i);
                        });
                    }
                    else
                    {
                        // 仅当模型确实输出了 19 个通道时，才执行概率比较提取
                        Parallel.For(0, outH * outW, i =>
                        {
                            int maxClass = 0; float maxProb = float.MinValue;
                            for (int c = 0; c < 19; c++)
                            {
                                int index = c * outH * outW + i;
                                float prob = floatOut.GetValue(index);
                                if (prob > maxProb) { maxProb = prob; maxClass = c; }
                            }
                            rawIds[i] = maxClass;
                        });
                    }

                    // ==========================================
                    // 3. 将结果无损对齐到您的 Grasshopper 点集
                    // ==========================================
                    int targetW = tensorData.Width;
                    int targetH = tensorData.Height;
                    int[] finalIds = new int[targetW * targetH];

                    if (outW != targetW || outH != targetH)
                    {
                        Parallel.For(0, targetH, y =>
                        {
                            for (int x = 0; x < targetW; x++)
                            {
                                int srcX = Math.Min((int)(x * (float)outW / targetW), outW - 1);
                                int srcY = Math.Min((int)(y * (float)outH / targetH), outH - 1);
                                finalIds[y * targetW + x] = rawIds[srcY * outW + srcX];
                            }
                        });
                    }
                    else
                    {
                        finalIds = rawIds;
                    }

                    DA.SetDataList(0, finalIds);
                    DA.SetData(1, $"Parsed successfully!" +
                        $"\nOriginal shape: [{rawShape}]" +
                        $"\nRecognized resolution: {outW}x{outH}" +
                        $"\nMode: {(isAlreadyArgmaxed ? "Direct ID read" : "Probability calculation")}");
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Inference exception: " + ex.Message);
            }
        }
        protected override System.Drawing.Bitmap Icon => Resources.icon_CitySemSegmentator;
        public override Guid ComponentGuid => new Guid("C3C2436C-A60F-46A8-96A3-1C6F00BB5A0F");
    }
}
