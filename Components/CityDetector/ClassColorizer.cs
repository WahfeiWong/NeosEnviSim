using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using NeosEnviSim.Properties;

namespace CitySemSegPlugin.Components
{
    public class ClassColorizerComponent : GH_Component
    {
        // Cityscapes 19-class names (English only for legend)
        private readonly Dictionary<int, string> CityscapesClasses = new Dictionary<int, string>
        {
            {0, "Road"}, {1, "Sidewalk"}, {2, "Building"}, {3, "Wall"},
            {4, "Fence"}, {5, "Pole"}, {6, "Traffic Light"}, {7, "Traffic Sign"},
            {8, "Vegetation"}, {9, "Terrain"}, {10, "Sky"}, {11, "Person"},
            {12, "Rider"}, {13, "Car"}, {14, "Truck"}, {15, "Bus"},
            {16, "Train"}, {17, "Motorcycle"}, {18, "Bicycle"}
        };

        public ClassColorizerComponent()
            : base("Class Colorizer", "Colorizer",
                  "Color a pixel mesh according to class IDs. Each face is assigned the color of its pixel.\n" +
                  "Input mesh must be the 'PixelMesh' from ImagePreProcessor (each pixel = independent quad).",
                  "Neos", "CityDetector")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("PixelMesh", "M", "Mesh from ImagePreProcessor (each pixel corresponds to a quad face)", GH_ParamAccess.item);
            pManager.AddIntegerParameter("TrainIDs", "ID", "List of class IDs (one per pixel, same order as faces)", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("ColoredMesh", "M", "Mesh with vertex colors set according to class IDs", GH_ParamAccess.item);
            pManager.AddColourParameter("PixelColors", "C", "Color value for each pixel (same order as input IDs)", GH_ParamAccess.list);
            pManager.AddTextParameter("ColorLegend", "L", "RGB color to class mapping for all 19 Cityscapes classes (e.g., \"128, 64, 128 : Road\")", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh inputMesh = null;
            List<int> ids = new List<int>();

            if (!DA.GetData(0, ref inputMesh)) return;
            if (!DA.GetDataList(1, ids)) return;

            // Validate mesh structure
            if (inputMesh.Faces.Count != ids.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Number of mesh faces ({inputMesh.Faces.Count}) does not match number of IDs ({ids.Count}).");
                return;
            }
            if (inputMesh.Vertices.Count != 4 * ids.Count)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error,
                    $"Mesh vertices count ({inputMesh.Vertices.Count}) is not 4 × number of IDs. Expected mesh with independent quads.");
                return;
            }

            // Cityscapes 19‑class color map (RGB)
            Color[] classColors = new Color[19];
            classColors[0] = Color.FromArgb(128, 64, 128);   // Road
            classColors[1] = Color.FromArgb(244, 35, 232);   // Sidewalk
            classColors[2] = Color.FromArgb(70, 70, 70);     // Building
            classColors[3] = Color.FromArgb(102, 102, 156);  // Wall
            classColors[4] = Color.FromArgb(190, 153, 153);  // Fence
            classColors[5] = Color.FromArgb(153, 153, 153);  // Pole
            classColors[6] = Color.FromArgb(250, 170, 30);   // Traffic Light
            classColors[7] = Color.FromArgb(220, 220, 0);    // Traffic Sign
            classColors[8] = Color.FromArgb(107, 142, 35);   // Vegetation
            classColors[9] = Color.FromArgb(152, 251, 152);  // Terrain
            classColors[10] = Color.FromArgb(70, 130, 180);  // Sky
            classColors[11] = Color.FromArgb(220, 20, 60);   // Person
            classColors[12] = Color.FromArgb(255, 0, 0);     // Rider
            classColors[13] = Color.FromArgb(0, 0, 142);     // Car
            classColors[14] = Color.FromArgb(0, 0, 70);      // Truck
            classColors[15] = Color.FromArgb(0, 60, 100);    // Bus
            classColors[16] = Color.FromArgb(0, 80, 100);    // Train
            classColors[17] = Color.FromArgb(0, 0, 230);     // Motorcycle
            classColors[18] = Color.FromArgb(119, 11, 32);   // Bicycle

            Color defaultColor = Color.FromArgb(128, 128, 128); // Gray for invalid IDs

            // Deep copy the mesh (vertices & faces only)
            Mesh coloredMesh = new Mesh();
            coloredMesh.Vertices.AddVertices(inputMesh.Vertices);
            coloredMesh.Faces.AddFaces(inputMesh.Faces);
            coloredMesh.VertexColors.Capacity = coloredMesh.Vertices.Count;

            // Initialize vertex colors to black (will be overwritten)
            for (int i = 0; i < coloredMesh.Vertices.Count; i++)
                coloredMesh.VertexColors.Add(Color.Black);

            List<Color> pixelColors = new List<Color>(ids.Count);

            // Assign colors per face
            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];
                Color col;
                if (id >= 0 && id < classColors.Length)
                    col = classColors[id];
                else
                    col = defaultColor;

                pixelColors.Add(col);

                int baseIdx = i * 4; // each face uses 4 consecutive vertices
                coloredMesh.VertexColors.SetColor(baseIdx, col);
                coloredMesh.VertexColors.SetColor(baseIdx + 1, col);
                coloredMesh.VertexColors.SetColor(baseIdx + 2, col);
                coloredMesh.VertexColors.SetColor(baseIdx + 3, col);
            }

            // Build color legend for all 19 classes
            List<string> colorLegend = new List<string>();
            for (int i = 0; i < classColors.Length; i++)
            {
                Color c = classColors[i];
                string name = CityscapesClasses.ContainsKey(i) ? CityscapesClasses[i] : "Unknown";
                colorLegend.Add($"{c.R}, {c.G}, {c.B} : {name}");
            }

            DA.SetData(0, coloredMesh);
            DA.SetDataList(1, pixelColors);
            DA.SetDataList(2, colorLegend);
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_ClassColor;
        public override Guid ComponentGuid => new Guid("AAE65AD0-35A5-4AF9-958A-A94FF0A00DEC");
    }
}