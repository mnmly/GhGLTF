using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using WebSocketSharp;
using SharpGLTF;
using SharpGLTF.Scenes;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Schema2;
using SharpGLTF.IO;
using System.IO;

using BYTES = System.ArraySegment<byte>;
using Buffer = System.Buffer;

namespace MNML
{
    public class GhGLTFComponent : GH_Component
    {

        WebSocket socket = null;
        Debouncer debouncer = new Debouncer(TimeSpan.FromMilliseconds(200));

        bool IsEmbedBuffer = true;
        bool IsPrettyPrint = true;
        bool IsWriteBinary = true;

        /// <summary>
        /// Each implementation of GH_Component must provide a public 
        /// constructor without any arguments.
        /// Category represents the Tab in which the component will appear, 
        /// Subcategory the panel. If you use non-existing tab or panel names, 
        /// new tabs/panels will automatically be created.
        /// </summary>
        public GhGLTFComponent()
          : base("Export as glTF", "Export glTF",
            "Export as glTF", "MNML", "Export")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGeometryParameter("Geometry", "G", "Geometry (Mesh/Curve/Point)", GH_ParamAccess.list);
            pManager.AddTextParameter("Object Names", "ON", "Object Names", GH_ParamAccess.list);
            pManager.AddTextParameter("Material Names", "MN", "Material Names", GH_ParamAccess.list);
            pManager.AddTextParameter("Output Path", "P", "Output Path", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Flip Axis", "F", "Map Rhino Z to OBJ Y", GH_ParamAccess.item, true);
            pManager.AddNumberParameter("Curve Resolution Factor", "R", "Resolution for converting curves into polylines ([0 - 1])", GH_ParamAccess.item, 0.2);
            pManager.AddGenericParameter("Socket", "S", "Established Websocket Client", GH_ParamAccess.item);
            pManager.AddTextParameter("Socket Message", "M", "Message to send", GH_ParamAccess.item);

            // If you want to change properties of certain parameters, 
            // you can use the pManager instance to access them by index:
            pManager[1].Optional = true;
            pManager[2].Optional = true;
            pManager[4].Optional = true;
            pManager[5].Optional = true;
            pManager[6].Optional = true;
            pManager[7].Optional = true;

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object can be used to retrieve data from input parameters and 
        /// to store data in output parameters.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            var tolerance = Rhino.RhinoDoc.ActiveDoc.ModelAbsoluteTolerance;
            var angleTolerance = Rhino.RhinoDoc.ActiveDoc.ModelAngleToleranceRadians;
            var geometries = new List<IGH_Goo>();
            var objectNames = new List<String>();
            var materialNames = new List<String>();
            string payloadString = null;
            var flip = true;
            String path = "";
            var resoltuionFactor = 0.2;

            if (!DA.GetDataList(0, geometries)) return;
            if (!DA.GetData(3, ref path)) return;
            if (!DA.GetData(6, ref socket)) { socket = null; }

            DA.GetDataList(1, objectNames);
            DA.GetDataList(2, materialNames);
            DA.GetData(4, ref flip);
            DA.GetData(5, ref resoltuionFactor);
            DA.GetData(7, ref payloadString);

            if (resoltuionFactor <= 0)
            {
                resoltuionFactor = tolerance;
            }


            Action action = () =>
            {
                var _model = SharpGLTF.Schema2.ModelRoot.CreateModel();
                var _scene = _model.UseScene(0);
                var names = new List<String>();
                var points = new List<float>();
                var pointName = "Point";

                for (int j = 0; j < geometries.Count; j++)
                {
                    var name = objectNames.Count > j ? objectNames[j] : ("object-" + j);
                    names.Add(name);
                    if (geometries[j] is GH_Mesh)
                    {
                        var mesh = (geometries[j] as GH_Mesh).Value;
                        var materialName = materialNames.Count > j ? materialNames[j] : "Default";

                        mesh.Faces.ConvertQuadsToTriangles();
                        mesh.Normals.ComputeNormals();

                        var uvs = mesh.TextureCoordinates.ToFloatArray();
                        var normals = mesh.Normals.ToFloatArray();
                        var faces = mesh.Faces.ToIntArray(true);

                        var vertices = mesh.Vertices.ToFloatArray();
                        if (flip)
                        {
                            for (var n = 0; n < vertices.Length / 3; n++)
                            {
                                var tmpY = vertices[n * 3 + 1];
                                vertices[n * 3 + 1] = vertices[n * 3 + 2];
                                vertices[n * 3 + 2] = -tmpY;
                            }
                            for (var n = 0; n < normals.Length / 3; n++)
                            {
                                var tmpY = normals[n * 3 + 1];
                                normals[n * 3 + 1] = normals[n * 3 + 2];
                                normals[n * 3 + 2] = -tmpY;
                            }
                        }


                        var _node = _scene.CreateNode(name);
                        var _mesh = _model.CreateMesh();
                        var _mat = _model.CreateMaterial(materialName);
                        var _accessors = new List<Accessor>();
                        var _bufferComponents = new List<int> { 1, 3, 2, 3 };
                        var _bufferDimensionTypes = new List<DimensionType> { DimensionType.SCALAR, DimensionType.VEC3, DimensionType.VEC2, DimensionType.VEC3 };
                        var _bufferCount = new List<int> { mesh.Faces.TriangleCount * 3, mesh.Vertices.Count, mesh.TextureCoordinates.Count, mesh.Normals.Count };
                        var _numberOfBytes = new List<int> { sizeof(int), sizeof(float), sizeof(float), sizeof(float) };

                        for (var i = 0; i < 4; i++)
                        {
                            var _itemCount = _bufferCount[i];
                            var _accessor = _model.CreateAccessor();
                            var count = _itemCount * _numberOfBytes[i] * _bufferComponents[i];
                            var bytes = new byte[count];
                            switch (i)
                            {
                                case 0: System.Buffer.BlockCopy(faces, 0, bytes, 0, count); break;
                                case 1: System.Buffer.BlockCopy(vertices, 0, bytes, 0, count); break;
                                case 2: System.Buffer.BlockCopy(uvs, 0, bytes, 0, count); break;
                                case 3: System.Buffer.BlockCopy(normals, 0, bytes, 0, count); break;
                            }
                            BYTES content = new ArraySegment<byte>(bytes, 0, count);
                            var _bufferView = _model.UseBufferView(content);
                            var _bufferDimensionType = _bufferDimensionTypes[i];
                            if (i == 0)
                            {
                                _accessor.SetIndexData(_bufferView, 0, _itemCount, IndexEncodingType.UNSIGNED_INT);
                            }
                            else
                            {
                                _accessor.SetVertexData(_bufferView, 0, _itemCount, _bufferDimensionType);

                            }
                            _accessors.Add(_accessor);
                        }

                        var _primitive = _mesh.CreatePrimitive();
                        _primitive.SetIndexAccessor(_accessors[0]);
                        _primitive.SetVertexAccessor("POSITION", _accessors[1]);
                        _primitive.SetVertexAccessor("TEXCOORD_0", _accessors[2]);
                        _primitive.SetVertexAccessor("NORMAL", _accessors[3]);
                        _primitive.DrawPrimitiveType = PrimitiveType.TRIANGLES;
                        _node.Mesh = _mesh;
                    }
                    else if (geometries[j] is GH_Curve)
                    {
                        var curve = (geometries[j] as GH_Curve).Value;

                        var length = curve.GetLength();
                        var minimumLength = length * tolerance * 100.0 * resoltuionFactor;
                        var maximumLength = length;
                        var polyline = curve.ToPolyline(tolerance, angleTolerance, minimumLength, maximumLength);
                        var vertices = new List<float>();
                        var materialName = materialNames.Count > j ? materialNames[j] : "Default";
                        for (var i = 0; i < polyline.PointCount; i++)
                        {
                            var p = polyline.Point(i);
                            vertices.Add((float)p.X);
                            vertices.Add(flip ? (float)p.Z : (float)p.Y);
                            vertices.Add(flip ? -(float)p.Y :(float)p.Z);
                        }

                        var _node = _scene.CreateNode(name);
                        var _mesh = _model.CreateMesh();
                        var _mat = _model.CreateMaterial(materialName);
                        var _accessors = new List<Accessor>();
                        var _bufferComponents = new List<int> { 3 };
                        var _bufferDimensionTypes = new List<DimensionType> { DimensionType.VEC3 };
                        var _bufferCount = new List<int> { vertices.Count / 3 };
                        var _numberOfBytes = new List<int> { sizeof(float) };
                        var _itemCount = _bufferCount[0];
                        var _accessor = _model.CreateAccessor();
                        var count = _itemCount * _numberOfBytes[0] * _bufferComponents[0];
                        var bytes = new byte[count];
                        System.Buffer.BlockCopy(vertices.ToArray(), 0, bytes, 0, count);
                        BYTES content = new ArraySegment<byte>(bytes, 0, count);
                        var _bufferView = _model.UseBufferView(content);
                        var _bufferDimensionType = _bufferDimensionTypes[0];
                        _accessor.SetVertexData(_bufferView, 0, _itemCount, _bufferDimensionType);
                        _accessors.Add(_accessor);
                        var _primitive = _mesh.CreatePrimitive();
                        _primitive.SetVertexAccessor("POSITION", _accessors[0]);
                        _primitive.DrawPrimitiveType = curve.IsClosed ? PrimitiveType.LINE_LOOP : PrimitiveType.LINE_STRIP;
                        _node.Mesh = _mesh;
                    }
                    else if (geometries[j] is GH_Point)
                    {
                        pointName = name;
                        var p = (geometries[j] as GH_Point).Value;
                        points.Add((float)p.X);
                        points.Add(flip ? (float)p.Z : (float)p.Y);
                        points.Add(flip ? -(float)p.Y : (float)p.Z);
                    }
                }
                if (points.Count > 0)
                {
                    var _node = _scene.CreateNode(pointName);
                    var _mesh = _model.CreateMesh();
                    var _mat = _model.CreateMaterial("Point");
                    var _accessors = new List<Accessor>();
                    var _bufferComponents = new List<int> { 3 };
                    var _bufferDimensionTypes = new List<DimensionType> { DimensionType.VEC3 };
                    var _bufferCount = new List<int> { points.Count / 3 };
                    var _numberOfBytes = new List<int> { sizeof(float) };
                    var _itemCount = _bufferCount[0];
                    var _accessor = _model.CreateAccessor();
                    var count = _itemCount * _numberOfBytes[0] * _bufferComponents[0];
                    var bytes = new byte[count];
                    System.Buffer.BlockCopy(points.ToArray(), 0, bytes, 0, count);
                    BYTES content = new ArraySegment<byte>(bytes, 0, count);
                    var _bufferView = _model.UseBufferView(content);
                    var _bufferDimensionType = _bufferDimensionTypes[0];
                    _accessor.SetVertexData(_bufferView, 0, _itemCount, _bufferDimensionType);
                    _accessors.Add(_accessor);
                    var _primitive = _mesh.CreatePrimitive();
                    _primitive.SetVertexAccessor("POSITION", _accessors[0]);
                    _primitive.DrawPrimitiveType = PrimitiveType.POINTS;
                    _node.Mesh = _mesh;
                }

                var _writeSetting = new WriteSettings();
                _writeSetting.MergeBuffers = true;

                using (var stream = File.Create(path))
                {
                    _model.WriteGLB(stream, _writeSetting);
                }

                if (socket != null)
                {
                    if (payloadString == null)
                    {
                        payloadString = String.Format("{{\"action\":\"broadcast\",\"data\": \"{0}\"}}", path);
                    }
                    socket.Send(payloadString);
                }
        };
        //action();
        // Finally assign the spiral to the output parameter.
        debouncer.Debounce(action);
        }



        /// <summary>
        /// The Exposure property controls where in the panel a component icon 
        /// will appear. There are seven possible locations (primary to septenary), 
        /// each of which can be combined with the GH_Exposure.obscure flag, which 
        /// ensures the component will only be visible on panel dropdowns.
        /// </summary>
        public override GH_Exposure Exposure => GH_Exposure.primary;

        /*
        public override void AppendAdditionalMenuItems(ToolStripDropDown menu)
        {
            {
                ToolStripMenuItem itemA = Menu_AppendItem(menu, "Embed Buffer",
    Menu_OptionClicked, null, true, IsEmbedBuffer);
                // Specifically assign a tooltip text to the menu item.
                itemA.ToolTipText = "Export buffer .bin as separate files.";
            }
 
            {
                ToolStripMenuItem itemB = Menu_AppendItem(menu, "Write Binary",
    Menu_OptionClicked, null, true, IsWriteBinary);
                // Specifically assign a tooltip text to the menu item.
                itemB.ToolTipText = "Write in Binary format";
            }
            {
                ToolStripMenuItem itemC = Menu_AppendItem(menu, "Pretty Print",
    Menu_OptionClicked, null, true, IsPrettyPrint);
                // Specifically assign a tooltip text to the menu item.
                itemC.ToolTipText = "Prettey Print JSON";
            }
        }

        private void Menu_OptionClicked(object sender, EventArgs e)
        {
            var item = (ToolStripMenuItem)sender;
            switch(item.Text)
            {
                case "Embed Buffer": IsEmbedBuffer = !IsEmbedBuffer; break;
                case "Pretty Print": IsPrettyPrint = !IsPrettyPrint; break;
                case "Write Binary": IsWriteBinary = !IsWriteBinary; break;

            }
        }*/

        /// <summary>
        /// Provides an Icon for every component that will be visible in the User Interface.
        /// Icons need to be 24x24 pixels.
        /// </summary>
        //protected override System.Drawing.Bitmap Icon
        //{
        //    get
        //    {
        //        // You can add image files to your project resources and access them like this:
        //        //return Resources.IconForThisComponent;
        //        return null;
        //    }
        //}

        /// <summary>
        /// Each component must have a unique Guid to identify it. 
        /// It is vital this Guid doesn't change otherwise old ghx files 
        /// that use the old ID will partially fail during loading.
        /// </summary>
        public override Guid ComponentGuid => new Guid("B67894EE-F053-47C0-8E6B-2D3E8AF5938A");
    }
}