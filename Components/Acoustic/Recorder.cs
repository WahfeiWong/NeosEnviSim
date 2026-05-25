using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using NeosEnviSim.Properties;

namespace NeosAcoustic
{
    public class DataRecorderComponent : GH_Component
    {
        // 存储记录的数据
        private List<IGH_Goo> _recordedData = new List<IGH_Goo>();
        // 记录状态
        private bool _isRecording = false;
        // 最大记录数量
        private int _maxRecords = 50;

        /// <summary>
        /// 组件构造函数
        /// </summary>
        public DataRecorderComponent()
          : base("Universal Recorder", "Recorder",
              "Records data when enabled, clears when reset",
              "Neos", "Acoustic")
        {
        }

        /// <summary>
        /// 注册输入参数
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Record", "Rec", "Start/stop recording", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Reset", "Rst", "Clear all recorded data", GH_ParamAccess.item, false);
            pManager.AddGenericParameter("Data", "D", "Data to record", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Max Records", "N", "Maximum number of records to keep", GH_ParamAccess.item, 50);
        }

        /// <summary>
        /// 注册输出参数
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Recorded Data", "RD", "All recorded data", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Count", "C", "Current record count", GH_ParamAccess.item);
        }

        /// <summary>
        /// 核心处理逻辑
        /// </summary>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 1. 获取输入值
            bool record = false;
            bool reset = false;
            IGH_Goo data = null;
            int maxRecords = 100;

            if (!DA.GetData(0, ref record)) return;
            if (!DA.GetData(1, ref reset)) return;
            if (!DA.GetData(2, ref data)) return;
            if (!DA.GetData(3, ref maxRecords)) return;

            // 2. 处理重置请求
            if (reset)
            {
                _recordedData.Clear();
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "All data cleared");
            }

            // 3. 更新记录状态
            if (record && !_isRecording)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Recording STARTED");
            }
            else if (!record && _isRecording)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Recording STOPPED");
            }
            _isRecording = record;

            // 4. 更新最大记录数
            _maxRecords = Math.Max(1, maxRecords);

            // 5. 记录数据（当记录开关开启时）
            if (_isRecording)
            {
                // 限制记录数量
                while (_recordedData.Count >= _maxRecords)
                {
                    _recordedData.RemoveAt(0);
                }

                _recordedData.Add(data.Duplicate());
            }

            // 6. 设置输出
            DA.SetDataList(0, _recordedData);
            DA.SetData(1, _recordedData.Count);
        }

        /// <summary>
        /// 组件图标与guid
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Resources.icon_recorder; 
        public override Guid ComponentGuid => new Guid("FB854850-9710-4AF2-95EA-4DC7570FDC95");
       
    }
}