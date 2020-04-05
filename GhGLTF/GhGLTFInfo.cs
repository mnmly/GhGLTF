using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace MNML
{
    public class GhGLTFInfo : GH_AssemblyInfo
    {
        public override string Name => "GhGLTF Info";

        //Return a 24x24 pixel bitmap to represent this GHA library.
        public override Bitmap Icon => null;

        //Return a short string describing the purpose of this GHA library.
        public override string Description => "";

        public override Guid Id => new Guid("DC1A4147-E31E-460A-9806-407D1CE6685E");

        //Return a string identifying you or your company.
        public override string AuthorName => "";

        //Return a string representing your preferred contact details.
        public override string AuthorContact => "";
    }
}