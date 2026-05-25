using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;

namespace CitySemSegPlugin.Components
{
    public class SemanticClassLegendComponent : GH_Component
    {
        public SemanticClassLegendComponent()
            : base("ID Class", "C", "Output Cityscapes standard class legend and present classes in the current image", "Neos", "CityDetector") { }

        // Cityscapes 19 个核心类别的标准字典
        private readonly Dictionary<int, string> CityscapesClasses = new Dictionary<int, string>
        {
            {0, "Road (道路)"}, {1, "Sidewalk (人行道)"}, {2, "Building (建筑)"}, {3, "Wall (墙体)"},
            {4, "Fence (栅栏)"}, {5, "Pole (杆/柱)"}, {6, "Traffic Light (红绿灯)"}, {7, "Traffic Sign (交通标志)"},
            {8, "Vegetation (植被)"}, {9, "Terrain (地形/草地)"}, {10, "Sky (天空)"}, {11, "Person (行人)"},
            {12, "Rider (骑行者)"}, {13, "Car (小汽车)"}, {14, "Truck (卡车)"}, {15, "Bus (公交车)"},
            {16, "Train (火车)"}, {17, "Motorcycle (摩托车)"}, {18, "Bicycle (自行车)"}
        };

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("TrainIDs", "ID", "List of class IDs from segmentation component", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("All Classes", "All", "Complete 19-class ID", GH_ParamAccess.list);
            pManager.AddTextParameter("Present Classes", "Present", "Distinct classes present in the current image", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<int> ids = new List<int>();
            if (!DA.GetDataList(0, ids)) return;

            // 输出端 1：完整的 19 种分类图例
            List<string> allClasses = new List<string>();
            foreach (var kvp in CityscapesClasses)
            {
                allClasses.Add($"ID={kvp.Key}, {kvp.Value}");
            }

            // 输出端 2：当前图像包含的去重类别
            HashSet<int> uniqueIds = new HashSet<int>(ids); // 利用HashSet极速去重
            List<int> sortedUnique = uniqueIds.ToList();
            sortedUnique.Sort(); // 按 0-18 顺序排列

            List<string> presentClasses = new List<string>();
            foreach (int id in sortedUnique)
            {
                if (CityscapesClasses.TryGetValue(id, out string name))
                {
                    presentClasses.Add($"ID={id}, {name}");
                }
            }

            DA.SetDataList(0, allClasses);
            DA.SetDataList(1, presentClasses);
        }
        protected override System.Drawing.Bitmap Icon => Resources.icon_SemanticClass;
        public override Guid ComponentGuid => new Guid("28363447-601B-4FC4-846C-6B1A9F037B5E");
    }
}
