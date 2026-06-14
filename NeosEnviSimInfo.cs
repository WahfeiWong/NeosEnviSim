using Grasshopper.Kernel;
using NeosEnviSim.Properties;
using System;
using System.Drawing;

namespace NeosEnviSim
{
    public class NeosEnviSimInfo : GH_AssemblyInfo
    {
        public override string Name => "NeosEnviSim";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => Resources.icon_NeosEnviSim;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "Tools for building environment simulation";

        public override Guid Id => new Guid("B184E258-C42E-488E-93CB-FBA0666A0923");

        //Return a string identifying you or your company.
        public override string AuthorName => "Huafei Huang";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "wongwahfai@163.com";

        //Return a string representing the version.  This returns the same version as the assembly.
        public override string AssemblyVersion => GetType().Assembly.GetName().Version.ToString();
    }
}