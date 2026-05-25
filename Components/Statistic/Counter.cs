using System;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;

namespace NeosStatistic
{
    public class UniversalCounterComponent : GH_Component
    {
        // 内部状态变量
        private double _current = 0;
        private bool _prevReset = false;

        public UniversalCounterComponent()
          : base("Universal Counter", "Counter",
            "Incremental counter with reset and limit (Using GH Trigger component,supports integers and decimals)",
            "Neos", "Statistic")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddBooleanParameter("Reset", "R", "Reset counter to start value", GH_ParamAccess.item, false);
            pManager.AddNumberParameter("Start", "S", "Starting value (integer or decimal)", GH_ParamAccess.item, 0.0);
            pManager.AddNumberParameter("Step", "St", "Increment step (integer or decimal)", GH_ParamAccess.item, 1.0);
            pManager.AddNumberParameter("Limit", "L", "Maximum limit value (integer or decimal)", GH_ParamAccess.item, 100.0);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Counter Value", "CV", "Current counter value", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 获取输入参数
            bool reset = false;
            double start = 0.0;
            double step = 1.0;
            double limit = 100.0;

            if (!DA.GetData(0, ref reset)) return;
            if (!DA.GetData(1, ref start)) return;
            if (!DA.GetData(2, ref step)) return;
            if (!DA.GetData(3, ref limit)) return;

            // 处理复位逻辑（上升沿触发）
            if (reset && !_prevReset)
            {
                _current = start;
            }
            _prevReset = reset;

            // 非复位状态下的计数逻辑
            if (!reset)
            {
                // 确保计数器从起始值开始
                if ((step >= 0 && _current < start) || (step < 0 && _current > start))
                {
                    _current = start;
                }
                // 检查是否在有效范围内
                else if ((step >= 0 && _current <= limit) || (step < 0 && _current >= limit))
                {
                    _current += step;
                }
            }

            // 应用限制
            double counterValue;
            if (step >= 0)
            {
                counterValue = Math.Min(_current, limit);
            }
            else
            {
                counterValue = Math.Max(_current, limit);
            }

            DA.SetData(0, counterValue);
        }

        // 组件GUID
        public override Guid ComponentGuid => new Guid("B33CBB9E-D394-42DE-86F6-4EE2DFA5B1D4");

        // 组件图标（可选）
        protected override System.Drawing.Bitmap Icon => Resources.iconCounter;
    }
}

