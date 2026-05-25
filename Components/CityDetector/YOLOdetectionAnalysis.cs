using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using Rhino.Geometry;

namespace CitySemSegPlugin.Components
{
    public class YOLODetectionAnalysisComponent : GH_Component
    {
        public YOLODetectionAnalysisComponent()
            : base("YOLO Detection Analysis", "YOLO Analysis",
                  "Count detected objects per class and output as formatted strings (e.g., 'Person: 15').",
                  "Neos", "CityDetector")
        { }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("ClassNames", "Names", "Detected class names from YOLO Detector", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Analysis", "A", "List of strings: 'ClassName: Count'", GH_ParamAccess.list);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<string> classNames = new List<string>();
            if (!DA.GetDataList(0, classNames) || classNames == null)
                return;

            // Group by class name and count
            var counts = classNames
                .GroupBy(name => name)
                .Select(g => new { Class = g.Key, Count = g.Count() })
                .OrderBy(x => x.Class); // alphabetical order

            List<string> result = counts.Select(x => $"{x.Class}: {x.Count}").ToList();

            DA.SetDataList(0, result);
        }

        protected override System.Drawing.Bitmap Icon => Resources.icon_detectionAnalysis;
        public override Guid ComponentGuid => new Guid("52011990-8160-4385-B6F6-74697F041A7D"); 
    }
}
