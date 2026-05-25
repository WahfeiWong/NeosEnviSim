using Microsoft.ML.OnnxRuntime;

namespace ML.Core
{
    // 1. 数据载体类：用于在预处理组件和分割组件之间传递高维数组
    public class TensorDataWrapper
    {
        public float[] Data { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }


    public class YOLOTensorData
    {
        public float[] Data { get; set; }          // Flat array of shape [1, 3, InputSize, InputSize]
        public int InputSize { get; set; }          // YOLO model input size (e.g., 640)
        public int OriginalWidth { get; set; }      // Width of the resized image (after cropping & scaling to 16:9)
        public int OriginalHeight { get; set; }     // Height of the resized image
    }

    // 2. 单例模型管理器：确保无论多少个组件，ONNX模型在内存中只存在一份
    public static class SegFormerModelManager
    {
        private static InferenceSession _session;
        private static string _currentModelPath = string.Empty;
        private static readonly object _lock = new object();

        public static InferenceSession GetSession(string modelPath)
        {
            lock (_lock) // 线程锁，防止多线程同时初始化
            {
                if (_session == null || _currentModelPath != modelPath)
                {
                    if (_session != null) _session.Dispose();

                    SessionOptions options = new SessionOptions();
                    // 默认使用CPU，若需GPU可改为 options.AppendExecutionProvider_CUDA(0);
                    options.AppendExecutionProvider_CPU(0);

                    _session = new InferenceSession(modelPath, options);
                    _currentModelPath = modelPath;
                }
                return _session;
            }
        }
    }
}