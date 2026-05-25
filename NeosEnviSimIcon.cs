using Grasshopper;
using Grasshopper.Kernel;

namespace NeosEnviSim
{
    public class PluginCategoryIcon : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            // 名称必须和组件的 Category 字符串完全一致
            Instances.ComponentServer.AddCategoryIcon("Neos", Properties.Resources.icon_NeosEnviSim);
            // (可选) 添加一个单字符缩写，显示在折叠面板上
            Instances.ComponentServer.AddCategorySymbolName("Neos", 'N');
            return GH_LoadingInstruction.Proceed;
        }
    }
}