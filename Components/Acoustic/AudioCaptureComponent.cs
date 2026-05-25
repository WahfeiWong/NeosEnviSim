using System;
using Grasshopper.Kernel;
using NAudio.Wave;
using NAudio.CoreAudioApi;
using NeosEnviSim.Properties;

namespace NeosAcoustic
{
    public class AudioCaptureComponent : GH_Component
    {
        // 音频捕获对象
        private WasapiCapture capture = null;
        // 存储最新样本值
        private float lastLeft = 0f;
        private float lastRight = 0f;
        // 存储峰值
        private float peakLeft = 0f;
        private float peakRight = 0f;
        // 存储RMS值
        private float rmsLeft = 0f;
        private float rmsRight = 0f;
        // 线程安全锁
        private readonly object lockObject = new object();
        // 重置标志
        private bool resetRequested = false;
        // 处理模式 (1:Peak, 2:RMS, 3:Latest)
        private int processingMode = 3;

        public AudioCaptureComponent()
          : base("Audio Capture", "AuCap",
            "Capture audio from system microphone with processing mode\n1:Peak, 2:RMS, 3:Latest (Using Trigger component to continuously sample the sound signal)",
            "Neos", "Acoustic")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Record", "Rec", "Start/stop recording", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Reset", "Rst", "Reset audio values", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Sample Interval", "Intv", "Processing interval in seconds (default=0.1)", GH_ParamAccess.item, 0.1);
            pManager.AddIntegerParameter("Mode", "Md", "Processing mode: 1=Peak, 2=RMS, 3=Latest (default=3)", GH_ParamAccess.item, 3);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Left Channel", "L", "Left channel output based on processing mode", GH_ParamAccess.item);
            pManager.AddNumberParameter("Right Channel", "R", "Right channel output based on processing mode", GH_ParamAccess.item);
            pManager.AddNumberParameter("Volume", "Vol", "Current volume level (normalized value)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 获取输入参数
            bool record = false;
            bool reset = false;
            double interval = 0.1;
            int mode = 3;

            if (!DA.GetData(0, ref record)) return;
            DA.GetData(1, ref reset);
            DA.GetData(2, ref interval);
            DA.GetData(3, ref mode);

            // 更新处理模式 (只接受1-3的值)
            processingMode = mode < 1 ? 1 : (mode > 3 ? 3 : mode);

            // 处理重置请求
            if (reset && !resetRequested)
            {
                lock (lockObject)
                {
                    lastLeft = 0f;
                    lastRight = 0f;
                    peakLeft = 0f;
                    peakRight = 0f;
                    rmsLeft = 0f;
                    rmsRight = 0f;
                }
                resetRequested = true;
            }
            else if (!reset)
            {
                resetRequested = false;
            }

            // 管理音频捕获
            ManageAudioCapture(record);

            // 根据模式选择输出值
            double left, right, volume;
            lock (lockObject)
            {
                switch (processingMode)
                {
                    case 1: // Peak
                        left = peakLeft;
                        right = peakRight;
                        break;
                    case 2: // RMS
                        left = rmsLeft;
                        right = rmsRight;
                        break;
                    case 3: // Latest
                    default:
                        left = lastLeft;
                        right = lastRight;
                        break;
                }

                // 计算音量（基于当前输出模式）
                volume = (Math.Abs(left) + Math.Abs(right)) / 2.0;
            }

            DA.SetData(0, left);
            DA.SetData(1, right);
            DA.SetData(2, volume);
        }

        private void ManageAudioCapture(bool record)
        {
            try
            {
                if (record && capture == null)
                {
                    // 初始化音频捕获
                    capture = new WasapiCapture();
                    capture.DataAvailable += Capture_DataAvailable;
                    capture.StartRecording();
                }
                else if (!record && capture != null)
                {
                    // 停止并释放资源
                    capture.StopRecording();
                    capture.DataAvailable -= Capture_DataAvailable;
                    capture.Dispose();
                    capture = null;
                }
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Audio init failed: {ex.Message}");
            }
        }

        private void Capture_DataAvailable(object sender, WaveInEventArgs e)
        {
            int bytesPerSample = capture.WaveFormat.BitsPerSample / 8;
            int channels = capture.WaveFormat.Channels;
            int sampleCount = e.BytesRecorded / (bytesPerSample * channels);

            // 用于存储临时计算结果
            float tempLeft = 0f;
            float tempRight = 0f;
            float maxLeft = 0f;
            float maxRight = 0f;
            double sumSquaresLeft = 0.0;
            double sumSquaresRight = 0.0;

            // 遍历缓冲区中的所有样本
            for (int i = 0; i < sampleCount; i++)
            {
                int offset = i * bytesPerSample * channels;
                float sampleLeft = 0f;
                float sampleRight = 0f;

                // 解析样本
                if (capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    sampleLeft = BitConverter.ToSingle(e.Buffer, offset);
                    if (channels > 1)
                    {
                        sampleRight = BitConverter.ToSingle(e.Buffer, offset + 4);
                    }
                    else
                    {
                        sampleRight = sampleLeft;
                    }
                }
                else if (capture.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
                {
                    if (bytesPerSample == 2)
                    {
                        sampleLeft = BitConverter.ToInt16(e.Buffer, offset) / 32768f;
                        if (channels > 1)
                        {
                            sampleRight = BitConverter.ToInt16(e.Buffer, offset + 2) / 32768f;
                        }
                        else
                        {
                            sampleRight = sampleLeft;
                        }
                    }
                    else if (bytesPerSample == 4)
                    {
                        sampleLeft = BitConverter.ToInt32(e.Buffer, offset) / 2147483648f;
                        if (channels > 1)
                        {
                            sampleRight = BitConverter.ToInt32(e.Buffer, offset + 4) / 2147483648f;
                        }
                        else
                        {
                            sampleRight = sampleLeft;
                        }
                    }
                }

                // 更新最新样本
                tempLeft = sampleLeft;
                tempRight = sampleRight;

                // 更新峰值
                float absLeft = Math.Abs(sampleLeft);
                float absRight = Math.Abs(sampleRight);
                if (absLeft > maxLeft) maxLeft = absLeft;
                if (absRight > maxRight) maxRight = absRight;

                // 累加平方和用于RMS计算
                sumSquaresLeft += sampleLeft * sampleLeft;
                sumSquaresRight += sampleRight * sampleRight;
            }

            // 正确计算RMS：平方和的平均值的平方根
            double rmsL = sampleCount > 0 ? Math.Sqrt(sumSquaresLeft / sampleCount) : 0.0;
            double rmsR = sampleCount > 0 ? Math.Sqrt(sumSquaresRight / sampleCount) : 0.0;

            // 安全更新共享变量
            lock (lockObject)
            {
                lastLeft = tempLeft;
                lastRight = tempRight;
                peakLeft = maxLeft;
                peakRight = maxRight;
                rmsLeft = (float)rmsL;
                rmsRight = (float)rmsR;
            }
        }

        protected override void BeforeSolveInstance()
        {
            base.BeforeSolveInstance();
            if (resetRequested)
            {
                lock (lockObject)
                {
                    lastLeft = 0f;
                    lastRight = 0f;
                    peakLeft = 0f;
                    peakRight = 0f;
                    rmsLeft = 0f;
                    rmsRight = 0f;
                }
                resetRequested = false;
            }
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_soundCapture;
        public override Guid ComponentGuid => new Guid("c8c37638-856f-4cae-b6a2-888b19ccdf99");
    }
}