using System;
using System.Collections.Generic;
using System.IO;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;

namespace NeosOptics
{
    public class SPDProcessorComponent : GH_Component
    {
        public SPDProcessorComponent()
          : base("SPD Processor", "SPDProc",
              "Processes spectral power distribution data (380-780nm)",
              "Neos", "Optics")
        {
        }

        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Directory", "Dir", "Output directory path", GH_ParamAccess.item);
            pManager.AddTextParameter("Filename", "Name", "Output filename (without extension)", GH_ParamAccess.item);
            pManager.AddNumberParameter("SPD Data", "Data", "List of 401 intensity values (380-780nm)", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Write File", "W", "Set to true to write output file", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("SPD File Path", "SFP", "Full path to the output file", GH_ParamAccess.item);
            pManager.AddTextParameter("Status", "St", "Operation status message", GH_ParamAccess.item);
            pManager.AddTextParameter("Preview", "Pre", "Preview of first 5 lines", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            // Retrieve input data
            string directory = "";
            string filename = "";
            List<double> spdData = new List<double>();
            bool writeFile = false;

            if (!DA.GetData(0, ref directory)) return;
            if (!DA.GetData(1, ref filename)) return;
            if (!DA.GetDataList(2, spdData)) return;
            if (!DA.GetData(3, ref writeFile)) return;

            // Validate data length
            if (spdData.Count != 401)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                    $"Input data has {spdData.Count} values, expected 401 for 380-780nm range.");
                DA.SetData(0, "");
                DA.SetData(1, "Error: Invalid data length");
                DA.SetData(2, "");
                return;
            }

            // Create full path
            if (!filename.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                filename += ".txt";
            }
            string fullPath = Path.Combine(directory, filename);

            // Prepare output text
            List<string> lines = new List<string>();
            for (int i = 0; i < 401; i++)
            {
                int wavelength = 380 + i;
                lines.Add($"{wavelength} {spdData[i]:F6}");
            }

            // Generate preview
            string preview = "First 5 lines:\n";
            for (int i = 0; i < Math.Min(5, lines.Count); i++)
            {
                preview += lines[i] + "\n";
            }

            // Write to file if enabled
            string status = "File not written (write disabled)";
            if (writeFile)
            {
                try
                {
                    // Ensure directory exists
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Write file
                    File.WriteAllLines(fullPath, lines);
                    status = $"Successfully wrote {fullPath}";
                }
                catch (Exception ex)
                {
                    status = $"Error writing file: {ex.Message}";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, status);
                }
            }

            // Set outputs
            DA.SetData(0, fullPath);
            DA.SetData(1, status);
            DA.SetData(2, preview);
        }

        
        protected override System.Drawing.Bitmap Icon => Resources.icon_SPDprocessor; 
        public override Guid ComponentGuid => new Guid("0A49B3E6-DFF9-4984-B1C6-1005893BE5F1");

        
    }
}

