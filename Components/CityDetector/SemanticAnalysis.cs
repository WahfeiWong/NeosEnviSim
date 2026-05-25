using System;
using System.Drawing;
using System.Collections.Generic;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using NeosEnviSim.Properties;

namespace CitySemSegPlugin.Components
{
    public class SemanticAnalysisComponent : GH_Component
    {
        public SemanticAnalysisComponent()
            : base("Semantic Analysis", "SemAn", "Compute Visible Green Index (VGI), Sky View Factor (SVF), pedestrian count estimation, etc.", "Neos", "CityDetector") { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddIntegerParameter("TrainIDs", "ID", "Segmented class IDs", GH_ParamAccess.list);
            pManager.AddPointParameter("Points", "P", "Pixel center points", GH_ParamAccess.list);
            pManager.AddIntegerParameter("Threshold", "T", "If the original image resolution is extremely high (e.g., 1820*1024), it is recommended to increase the threshold to 50 or even 100", GH_ParamAccess.item, 10);
            pManager.AddIntegerParameter("Width", "W", "Image width", GH_ParamAccess.item, 1820);
            pManager.AddIntegerParameter("Height", "H", "Image height", GH_ParamAccess.item, 1024);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddPointParameter("PointsByClass", "Pts", "Points grouped by class", GH_ParamAccess.tree);
            pManager.AddNumberParameter("VGI", "VGI", "Visible Green Index (vegetation + terrain ratio)", GH_ParamAccess.item);
            pManager.AddNumberParameter("SVF", "SVF", "Sky View Factor (sky ratio)", GH_ParamAccess.item);
            pManager.AddNumberParameter("BVF", "BVF", "Building View Factor (building + wall ratio)", GH_ParamAccess.item);
            pManager.AddNumberParameter("RAI", "RAI", "Road Area Index (road + sidewalk ratio)", GH_ParamAccess.item);
            pManager.AddNumberParameter("SEI", "SEI", "Spatial Enclosure Index ,SEI = BuildingArea / (BuildingArea + SkyArea)", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Pedestrians", "Ped", "Estimated pedestrian count (person + rider)", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<int> ids = new List<int>();
            List<Point3d> points = new List<Point3d>();
            int threshold = 10; // 默认值
            int width = 0, height = 0;

            if (!DA.GetDataList(0, ids)) return;
            if (!DA.GetDataList(1, points)) return;
            // 读取新增的阈值参数（索引2）
            DA.GetData(2, ref threshold);
            // 宽度和高度现在索引为3和4
            DA.GetData(3, ref width);
            DA.GetData(4, ref height);

            if (ids.Count != points.Count || ids.Count != width * height)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Input data W and H does not match image dimensions!");
                return;
            }

            DataTree<Point3d> categorizedTree = new DataTree<Point3d>();
            int[] classCounts = new int[19];
            bool[,] isPerson = new bool[width, height];

            // 1. 遍历所有像素，统计每个类别的数量，并构建分类树
            for (int i = 0; i < ids.Count; i++)
            {
                // 如果模型传出了 -1（未分类像素/背景），进行防越界保护
                int id = ids[i];
                if (id < 0 || id >= 19) continue;

                classCounts[id]++;
                categorizedTree.Add(points[i], new GH_Path(id));

                if (id == 11 || id == 12) // 11=Person(行人), 12=Rider(骑行者)
                {
                    int x = i % width;
                    int y = i / width;
                    isPerson[x, y] = true;
                }
            }

            // 2. 严谨的学术指标计算
            float total = Math.Max(ids.Count, 1f); // 防止空数据除以0

            float vgi = (classCounts[8] + classCounts[9]) / total;
            float svf = classCounts[10] / total;
            float bvf = (classCounts[2] + classCounts[3]) / total;
            float rai = (classCounts[0] + classCounts[1]) / total;
            float bldArea = classCounts[2] + classCounts[3];
            float skyArea = classCounts[10];
            float sei = 0f;

            if (bldArea + skyArea > 0)
            {
                // 标准化围合度 (0.0 ~ 1.0)，越接近1表示越压抑，越接近0表示越开阔
                sei = bldArea / (bldArea + skyArea);              
            }         

            int pedCount = CountPedestrians(isPerson, width, height, threshold);

            DA.SetDataTree(0, categorizedTree);
            DA.SetData(1, vgi);
            DA.SetData(2, svf);
            DA.SetData(3, bvf);
            DA.SetData(4, rai);
            DA.SetData(5, sei);
            DA.SetData(6, pedCount);
        }

        // 广度优先搜索 (BFS) 计算连通域数量预估行人数
        private int CountPedestrians(bool[,] grid, int width, int height, int minBlobSize)
        {
            int count = 0;
            bool[,] visited = new bool[width, height];

            int[] dx = new int[] { -1, 1, 0, 0, -1, -1, 1, 1 };
            int[] dy = new int[] { 0, 0, -1, 1, -1, 1, -1, 1 };

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (grid[x, y] && !visited[x, y])
                    {
                        count++;
                        Queue<System.Drawing.Point> q = new Queue<System.Drawing.Point>();
                        q.Enqueue(new System.Drawing.Point(x, y));
                        visited[x, y] = true;

                        int pixelBlobSize = 0;

                        while (q.Count > 0)
                        {
                            System.Drawing.Point p = q.Dequeue();
                            pixelBlobSize++;

                            for (int k = 0; k < 8; k++)
                            {
                                int nx = p.X + dx[k];
                                int ny = p.Y + dy[k];

                                if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                                {
                                    if (grid[nx, ny] && !visited[nx, ny])
                                    {
                                        visited[nx, ny] = true;
                                        q.Enqueue(new System.Drawing.Point(nx, ny));
                                    }
                                }
                            }
                        }

                        // 噪点过滤：连通像素少于指定阈值不认为是有效行人
                        // 阈值由用户输入，可根据图像分辨率调整
                        if (pixelBlobSize < minBlobSize)
                        {
                            count--;
                        }
                    }
                }
            }
            return count;
        }
        protected override System.Drawing.Bitmap Icon => Resources.icon_SemanticAnalysis;
        public override Guid ComponentGuid => new Guid("24D04042-2641-4E2E-9647-F103CB9CCBFF");
    }
}
