using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;
using NeosEnviSim.Properties;

namespace NeosVisualization
{
    public class NeosBuildingPhysicsInfo : GH_AssemblyInfo
    {
        public override string Name => "Neos";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => Resources.icon_NeosEnviSim;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "Data Analysis and Visualization Toolkit";

        public override Guid Id => new Guid("389BE865-0C46-4798-B1FD-E307B7AE57C4");

        //Return a string identifying you or your company.
        public override string AuthorName => "黄华飞/HuangHuafei/WongWahFai";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "wongwahfai@163.com";

        //Return a string representing the version.  This returns the same version as the assembly.
        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}

