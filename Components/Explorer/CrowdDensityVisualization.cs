using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Drawing;
using NeosEnviSim.Properties;

namespace NeosExplorer
{
    public class CrowdDensityVisualization : GH_Component
    {
        public CrowdDensityVisualization()
          : base("Crowd Density Visualization", "DensityViz",
                "Visualize crowd density using a colored grid",
                "Neos", "NeosExplorer")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Agent Positions", "P", "Agent positions", GH_ParamAccess.list);
            pManager.AddPointParameter("Grid Origin", "O", "Grid origin point", GH_ParamAccess.item, Point3d.Origin);
            pManager.AddNumberParameter("Grid Spacing", "S", "Grid cell size", GH_ParamAccess.item, 2.0);
            pManager.AddIntegerParameter("X Cells", "X", "Number of cells in X direction", GH_ParamAccess.item, 10);
            pManager.AddIntegerParameter("Y Cells", "Y", "Number of cells in Y direction", GH_ParamAccess.item, 10);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Density Mesh", "M", "Colored mesh showing crowd density (all cells)", GH_ParamAccess.item);
            pManager.AddMeshParameter("Colored Mesh (Non-Empty)", "M'", "Colored mesh showing only cells with agents", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Agent Counts", "C", "Agent count per grid cell", GH_ParamAccess.list);
            pManager.AddNumberParameter("Density Percentages", "D", "Density percentage per cell", GH_ParamAccess.list);
            pManager.AddColourParameter("Cell Colors", "Col", "Color for each grid cell", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // 获取输入数据
            List<Point3d> agentPositions = new List<Point3d>();
            Point3d gridOrigin = Point3d.Origin;
            double gridSpacing = 2.0;
            int xCells = 10;
            int yCells = 10;

            if (!DA.GetDataList(0, agentPositions)) return;
            DA.GetData(1, ref gridOrigin);
            DA.GetData(2, ref gridSpacing);
            DA.GetData(3, ref xCells);
            DA.GetData(4, ref yCells);

            // 验证输入
            if (gridSpacing <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Grid spacing must be greater than zero");
                return;
            }

            if (xCells <= 0 || yCells <= 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Cell counts must be greater than zero");
                return;
            }

            // 统计每个网格单元内的代理数量
            int totalCells = xCells * yCells;
            int[] agentCounts = new int[totalCells];
            double[] densityPercentages = new double[totalCells];
            Color[] cellColors = new Color[totalCells];

            // 计算密度值
            int totalAgents = agentPositions.Count;
            double maxDensity = 0;

            // 计算每个网格单元的代理数量
            foreach (Point3d position in agentPositions)
            {
                // 计算代理所在的网格索引
                double xOffset = position.X - gridOrigin.X;
                double yOffset = position.Y - gridOrigin.Y;

                int xIndex = (int)Math.Floor(xOffset / gridSpacing);
                int yIndex = (int)Math.Floor(yOffset / gridSpacing);

                // 确保索引在有效范围内
                if (xIndex >= 0 && xIndex < xCells && yIndex >= 0 && yIndex < yCells)
                {
                    int cellIndex = yIndex * xCells + xIndex;
                    agentCounts[cellIndex]++;

                    // 更新最大密度值
                    if (agentCounts[cellIndex] > maxDensity)
                    {
                        maxDensity = agentCounts[cellIndex];
                    }
                }
            }

            // 计算密度百分比和颜色
            for (int i = 0; i < totalCells; i++)
            {
                // 计算密度百分比
                densityPercentages[i] = totalAgents > 0 ?
                    (double)agentCounts[i] / totalAgents * 100 : 0;

                // 计算归一化密度值 (0-1)
                double normalizedDensity = maxDensity > 0 ?
                    (double)agentCounts[i] / maxDensity : 0;

                // 使用Ladybug热图风格的渐变
                cellColors[i] = InterpolateLadybugColor(normalizedDensity);
            }

            // 创建完整网格（包含所有单元格）
            Mesh fullDensityMesh = CreateGridMesh(gridOrigin, gridSpacing, xCells, yCells, cellColors);

            // 创建非空网格（仅包含有代理的单元格）
            Mesh nonEmptyDensityMesh = CreateNonEmptyGridMesh(gridOrigin, gridSpacing, xCells, yCells, agentCounts, cellColors);

            // 设置输出
            DA.SetData(0, fullDensityMesh);
            DA.SetData(1, nonEmptyDensityMesh);
            DA.SetDataList(2, agentCounts);
            DA.SetDataList(3, densityPercentages);
            DA.SetDataList(4, cellColors);
        }

        private Mesh CreateGridMesh(Point3d origin, double spacing, int xCells, int yCells, Color[] cellColors)
        {
            Mesh mesh = new Mesh();

            // 为每个单元格创建独立的顶点和面
            for (int j = 0; j < yCells; j++)
            {
                for (int i = 0; i < xCells; i++)
                {
                    // 计算当前单元格的索引
                    int cellIndex = j * xCells + i;
                    Color cellColor = cellColors[cellIndex];

                    // 计算单元格角点
                    double x0 = origin.X + i * spacing;
                    double x1 = origin.X + (i + 1) * spacing;
                    double y0 = origin.Y + j * spacing;
                    double y1 = origin.Y + (j + 1) * spacing;

                    // 添加顶点（每个单元格有独立的顶点）
                    int startIndex = mesh.Vertices.Count;
                    mesh.Vertices.Add(new Point3d(x0, y0, origin.Z)); // 顶点0
                    mesh.Vertices.Add(new Point3d(x1, y0, origin.Z)); // 顶点1
                    mesh.Vertices.Add(new Point3d(x1, y1, origin.Z)); // 顶点2
                    mesh.Vertices.Add(new Point3d(x0, y1, origin.Z)); // 顶点3

                    // 添加四边形面
                    mesh.Faces.AddFace(
                        startIndex,     // 顶点0
                        startIndex + 1, // 顶点1
                        startIndex + 2, // 顶点2
                        startIndex + 3  // 顶点3
                    );

                    // 为每个顶点设置相同的颜色（实现面着色）
                    for (int k = 0; k < 4; k++)
                    {
                        mesh.VertexColors.Add(cellColor);
                    }
                }
            }

            return mesh;
        }

        private Mesh CreateNonEmptyGridMesh(Point3d origin, double spacing, int xCells, int yCells, int[] agentCounts, Color[] cellColors)
        {
            Mesh mesh = new Mesh();

            // 仅处理有代理的单元格
            for (int j = 0; j < yCells; j++)
            {
                for (int i = 0; i < xCells; i++)
                {
                    // 计算当前单元格的索引
                    int cellIndex = j * xCells + i;

                    // 只处理有代理的单元格
                    if (agentCounts[cellIndex] == 0)
                        continue;

                    Color cellColor = cellColors[cellIndex];

                    // 计算单元格角点
                    double x0 = origin.X + i * spacing;
                    double x1 = origin.X + (i + 1) * spacing;
                    double y0 = origin.Y + j * spacing;
                    double y1 = origin.Y + (j + 1) * spacing;

                    // 添加顶点（每个单元格有独立的顶点）
                    int startIndex = mesh.Vertices.Count;
                    mesh.Vertices.Add(new Point3d(x0, y0, origin.Z)); // 顶点0
                    mesh.Vertices.Add(new Point3d(x1, y0, origin.Z)); // 顶点1
                    mesh.Vertices.Add(new Point3d(x1, y1, origin.Z)); // 顶点2
                    mesh.Vertices.Add(new Point3d(x0, y1, origin.Z)); // 顶点3

                    // 添加四边形面
                    mesh.Faces.AddFace(
                        startIndex,     // 顶点0
                        startIndex + 1, // 顶点1
                        startIndex + 2, // 顶点2
                        startIndex + 3  // 顶点3
                    );

                    // 为每个顶点设置相同的颜色
                    for (int k = 0; k < 4; k++)
                    {
                        mesh.VertexColors.Add(cellColor);
                    }
                }
            }

            return mesh;
        }

        // Ladybug热图风格的颜色插值
        private Color InterpolateLadybugColor(double t)
        {
            // 定义Ladybug热图风格的渐变
            // 从蓝色(低密度) -> 青色 -> 绿色 -> 黄色 -> 红色(高密度)
            if (t <= 0.25)
            {
                // 蓝色到青色
                double ratio = t / 0.25;
                int r = (int)(0 * ratio);
                int g = (int)(0 * ratio + 255 * ratio);
                int b = (int)(255 * (1 - ratio) + 255 * ratio);
                return Color.FromArgb(r, g, b);
            }
            else if (t <= 0.5)
            {
                // 青色到绿色
                double ratio = (t - 0.25) / 0.25;
                int r = (int)(0 * (1 - ratio) + 0 * ratio);
                int g = (int)(255 * (1 - ratio) + 255 * ratio);
                int b = (int)(255 * (1 - ratio) + 0 * ratio);
                return Color.FromArgb(r, g, b);
            }
            else if (t <= 0.75)
            {
                // 绿色到黄色
                double ratio = (t - 0.5) / 0.25;
                int r = (int)(0 * (1 - ratio) + 255 * ratio);
                int g = (int)(255 * (1 - ratio) + 255 * ratio);
                int b = 0;
                return Color.FromArgb(r, g, b);
            }
            else
            {
                // 黄色到红色
                double ratio = (t - 0.75) / 0.25;
                int r = (int)(255 * (1 - ratio) + 255 * ratio);
                int g = (int)(255 * (1 - ratio) + 0 * ratio);
                int b = 0;
                return Color.FromArgb(r, g, b);
            }
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_CrowdVisualization;
        public override Guid ComponentGuid => new Guid("2410DC23-6E08-4BBE-A016-8A8F00942D15");
    }
}