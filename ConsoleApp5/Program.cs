using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

class Vertex
{
    public float x, y, z;
}
class VertexTexCoord
{
    public float u, v;
}
class VertexTexCoord16b
{
    public Int16 u, v;
}
class VertexNormal
{
    public float n1, n2, n3; // unused
}
class F3DVertex
{
    public short x, y, z, u, v;
    public byte r, g, b, a;
    public int materialID;
}
class Face
{
    public int[] v;
    public int[] vt;
    public int[] vn;
    public int cachedVInsideCode;
    public Material mat;
}
class Material
{
    public string name = "";
    public Bitmap image = null;
    public float opacity = 1; // todo
    public bool alphaChannel = false;
}
class MaterialKVP
{
    public string name;
    public Material mat;
}

namespace ConsoleApp5
{
    class Program
    {
        // Version 0.15
        public const float version = 0.15f;
        static bool doSupportTextures = true;
        static int scale = 100;

        static List<Vertex> vertices = new List<Vertex>();
        static List<VertexTexCoord> texCoordVertices = new List<VertexTexCoord>();
        static List<VertexNormal> normalVertices = new List<VertexNormal>();
        static List<Face> faces = new List<Face>();
        static Dictionary<string, Material> materials = new Dictionary<string, Material>();
        
        static int StrCmp(string a, string b)
        {
            if (a.Length != b.Length)
                return a.Length > b.Length ? 1 : -1;
            else
            {
                for (int i = 0; i < b.Length /* doesnt matter */ ; i++)
                {
                    if (a[i] < b[i])
                    {
                        return -1;
                    }
                    if (b[i] < a[i])
                    {
                        return 1;
                    }
                }
            }
            return 0;
        }

        static int FaceCompareShit(Face a, Face b)
        {
            return StrCmp(a.mat.name, b.mat.name);
        }

        static Int16 FloatToFixed16b(float value)
        {
            // int(round(min(max(value*2**15,-2**15),2**15)))
            int two5x = 2 * 2 * 2 * 2 * 2;
            int two15x = 32768;
            value *= two5x;
            return (Int16)Math.Round(Math.Min(Math.Max(value, -two15x), two15x - 1));
        }
        static VertexTexCoord16b ConvertUVToFixed16b(VertexTexCoord c, int tw, int th)
        {
            return new VertexTexCoord16b()
            {
                u = FloatToFixed16b(c.u * tw),
                v = FloatToFixed16b(c.v * th),
            };
        }

        static int GetPO2(int size)
        {
            if (size > 1024) return 11;
            if (size > 512) return 10;
            if (size > 256) return 9;
            if (size > 128) return 8;
            if (size > 64) return 7;
            if (size > 32) return 6;
            if (size > 16) return 5;
            if (size > 8) return 4;
            if (size > 4) return 3;
            if (size > 2) return 2;
            if (size > 1) return 1;
            return 0;
        }

        static void ImportMaterial(string matFileName)
        {
            string[] lines = File.ReadAllLines(matFileName);
            Material curMat = null;
            foreach(string line in lines) {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#")) continue;
                string[] tokens = line.Split(' ');
                if (tokens.Length < 1) continue; // safety
                if (tokens[0] == "newmtl")
                {
                    if (curMat != null)
                    {
                        if (curMat.image == null)
                        {
                            curMat.image = new Bitmap(32, 32);
                            for (int i = 0; i < 32*32; i++)
                            {
                                curMat.image.SetPixel(i % 32, i / 32, Color.White);
                            }
                        }
                        materials[curMat.name] = curMat;
                    }
                    curMat = new Material();
                    curMat.name = tokens[1];
                }
                else if (tokens[0].StartsWith("map_K") && tokens[0] != "map_Ks" /* dont include specular maps */ && curMat.image == null /* don't load the same img twice */ )
                {
                    string fn ="";
                    /*
                    int m;
                    m = matFileName.LastIndexOf("/");
                    if (m == -1) {
                        m = matFileName.LastIndexOf("\\");
                        if (m == -1) m = 0;
                    }
                    fn = matFileName.Substring(0, m) + (m==0?"":"/") + tokens[1];*/
                    fn = tokens[1]; // why overcomplicate yourself?
                    curMat.image = new Bitmap(fn);
                    bool alpha = false;
                    // find if theres alpha in the image
                    for (int i = 0; i < curMat.image.Width; i++)
                    {
                        for (int j = 0; j < curMat.image.Height; j++)
                        {
                            if (curMat.image.GetPixel(i,j).A < 255)
                            {
                                alpha = true; break;
                            }
                        }
                    }
                    curMat.alphaChannel = alpha;
                }
                else if (tokens[0] == "d")
                {
                    curMat.opacity = float.Parse(tokens[1], System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (tokens[0] == "Tr")
                {
                    curMat.opacity = 1F - float.Parse(tokens[1], System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            // push last material
            if (curMat != null)
            {
                if (curMat.image == null)
                {
                    curMat.image = new Bitmap(32, 32);
                    for (int i = 0; i < 32 * 32; i++)
                    {
                        curMat.image.SetPixel(i % 32, i / 32, Color.White);
                    }
                }
                materials[curMat.name] = curMat;
            }
        }

        static string StringToGoodCName(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if ("!@#$%^&*()+-={}[]:\";'\\,./<>?|`~".Contains(s[i]))
                {
                    s = s.Substring(0, i) + '_' + s.Substring(i + 1);
                }
            }
            return s;
        }

        static UInt16 ColorToRGBA16(Color c)
        {
            // 5R 5G 5B 1A
            int r = c.R >> 3;
            int g = c.G >> 3;
            int b = c.B >> 3;
            int a = c.A >> 7;
            return (ushort)(r << 11 | g << 6 | b << 1 | a);
        }

        static void Main(string[] args)
        {
            bool sketchupMode = false;
            if (args.Length < 1)
            {
                Console.WriteLine("usage: convert <your obj file> [-sum] [output name]");
                Console.WriteLine("   -sum: swaps the y and z axes (sketchup + lipid obj mode)");
                return;
            }
            if (args.Contains("-sum"))
            {
                Console.WriteLine("Activated Sketchup exporter mode.");
                sketchupMode = true;
            }
            string[] lines = File.ReadAllLines(args[0]);
            string curMat = "";
            Console.WriteLine("Parsing obj file...");
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("#")) continue;
                string[] tokens = line.Split(' ');
                if (tokens.Length < 1) continue; // safety
                switch (tokens[0])
                {
                    case "mtllib": {
                            ImportMaterial(tokens[1]);
                            break;
                        }
                    case "v":
                        {
                            // Add Vertex
                            Vertex vtx = new Vertex()
                            {
                                x = float.Parse(tokens[1], System.Globalization.CultureInfo.InvariantCulture), // always a dot, never comma
                                y = float.Parse(tokens[2], System.Globalization.CultureInfo.InvariantCulture), // always a dot, never comma
                                z = float.Parse(tokens[3], System.Globalization.CultureInfo.InvariantCulture), // always a dot, never comma
                            };
                            if (sketchupMode)
                            {
                                vtx.y = -vtx.y;
                                float f = vtx.z;
                                vtx.z = vtx.y;
                                vtx.y = f;
                            }
                            vertices.Add(vtx);
                            break;
                        }
                    case "vt":
                        {
                            // Add Vertex Texture Coordinate
                            VertexTexCoord vtx = new VertexTexCoord()
                            {
                                u = float.Parse(tokens[1], System.Globalization.CultureInfo.InvariantCulture), // always a dot, never comma
                                v = 1-float.Parse(tokens[2], System.Globalization.CultureInfo.InvariantCulture), // N64 requires this :)
                            };
                            texCoordVertices.Add(vtx);
                            break;
                        }
                    case "vn":
                        {
                            // Add Vertex Normal Coordinate
                            VertexNormal vtx = new VertexNormal()
                            {
                                n1 = float.Parse(tokens[1], System.Globalization.CultureInfo.InvariantCulture), // always a dot, never comma
                                n2 = float.Parse(tokens[2], System.Globalization.CultureInfo.InvariantCulture), // always a dot, never comma
                                n3 = float.Parse(tokens[3], System.Globalization.CultureInfo.InvariantCulture), // always a dot, never comma
                            };
                            if (sketchupMode)
                            {
                                vtx.n2 = -vtx.n2;
                                float f = vtx.n3;
                                vtx.n3 = vtx.n2;
                                vtx.n2 = f;
                            }
                            normalVertices.Add(vtx);
                            break;
                        }
                    case "usemtl":
                        {
                            curMat = tokens[1];
                            break;
                        }
                    case "f":
                        {
                            // face - always a triangle, quads not supported
                            int[] v = new int[3];
                            int[] vt = new int[3];
                            int[] vn = new int[3];
                            for (int i = 0; i < 3; i++)
                            {
                                string[] subTokens = tokens[i+1].Split('/');
                                if (subTokens.Length < 1)
                                {
                                    Console.WriteLine($"Error: Face with no coordinate {i}");
                                    return;
                                }
                                switch (subTokens.Length)
                                {
                                    case 1:
                                        // vertex coord
                                        v[i] = int.Parse(subTokens[0]) - 1; // apparently indexes start with 1 here :/
                                        break;
                                    case 2:
                                        // vertex tex coord
                                        v[i] = int.Parse(subTokens[0]) - 1;
                                        if (subTokens[1] == "") vt[i] = -1; else vt[i] = int.Parse(subTokens[1]) - 1;
                                        break;
                                    case 3:
                                        // vertex coord
                                        v[i] = int.Parse(subTokens[0]) - 1;
                                        if (subTokens[1] == "") vt[i] = -1; else vt[i] = int.Parse(subTokens[1]) - 1;
                                        vn[i] = int.Parse(subTokens[2]) - 1;
                                        break;
                                }
                            }
                            Face f = new Face()
                            {
                                v = v,
                                vn = vn,
                                vt = vt,
                                mat = materials[curMat]
                            };
                            faces.Add(f);
                            break;
                        }
                    default:
                        {
                            Console.WriteLine($"Warning: Unrecognized instruction {tokens[0]}, this may go downhill quick!");
                            break;
                        }
                }
            };

            // Loaded in everything, let's write it!
            Console.WriteLine("Loaded obj file, writing collision...");
            string outName = "output";
            if (args.Length>1)
            {
                outName = StringToGoodCName(args[1]);
            }

            // Sort faces by material name
            //faces.Sort(new Comparison<Face>(FaceCompareShit)); // makes it easier to clump things together

            //string curMatName = "";
            
            string s = $"// Written by `convert' tool version {version} (C) 2020 iProgramInCpp.\n\nconst Collision {outName}_collision[] = {{";
            s += $"\n\tCOL_INIT(),\n\tCOL_VERTEX_INIT({vertices.Count}),\n";
            foreach(Vertex v in vertices)
            {
                s += $"\tCOL_VERTEX({(int)(v.x * scale)},{(int)(v.y * scale)},{(int)(v.z * scale)}),\n";
            }

            foreach (var v in materials)
            {
                string s2 = ""; int mtlCnt = 0;
                foreach (Face f in faces)
                {
                    if (f.mat == v.Value)
                    {
                        s2 += $"\tCOL_TRI({f.v[0]},{f.v[1]},{f.v[2]}),\n";
                        mtlCnt++;
                    }
                }
                s += $"\t// {v.Value.name} Material...\n\tCOL_TRI_INIT(SURFACE_DEFAULT, {mtlCnt}),\n" + s2;
            }

            s += "\tCOL_TRI_STOP(),\n\tCOL_END(),\n};";
            File.WriteAllText(args[0] + "_outputCollision.inc.c", s);
            s = "";


            Console.WriteLine("Wrote collision of {0} verts and {1} tris, writing display list data...", vertices.Count, faces.Count);
            s += $"// Written by `convert' tool version {version} (C) 2020 iProgramInCpp.\n\nstatic const Lights1 {outName}_lights = gdSPDefLights1(0x3f,0x3f,0x3f,0xff,0xff,0xff,0x28,0x28,0x28);\n";
            s += $"static const Vtx {outName}_vertices[] = {{\n";
            int vtxIndex = 0;
            for (int i = 0; i < faces.Count; i++)
            {
                // Write each face as its own 3 vertexes
                // This will make the model disjointed, but it's fine for now,
                // as normals and UV mapping requires it

                // get the 3 verts
                Face f = faces[i];
                Vertex[] v = new Vertex[3]; 
                VertexTexCoord16b[] vt = new VertexTexCoord16b[3]; 
                byte[] vn = new byte[9];
                for (int j = 0; j < 3; j++)
                {
                    v[j] = vertices[f.v[j]];
                    vt[j] = ConvertUVToFixed16b(f.vt[j] == -1 ? new VertexTexCoord() : texCoordVertices[f.vt[j]], f.mat.image.Width, f.mat.image.Height);
                    vn[j * 3 + 0] = (byte)(127 * (normalVertices[f.vn[j]]).n1);
                    vn[j * 3 + 1] = (byte)(127 * (normalVertices[f.vn[j]]).n2);
                    vn[j * 3 + 2] = (byte)(127 * (normalVertices[f.vn[j]]).n3);
                    s += $"\t{{{{{{ {v[j].x * scale},{v[j].y * scale},{v[j].z * scale} }}, 0, {{ {vt[j].u},{vt[j].v} }}, {{ {vn[j * 3 + 0]},{vn[j * 3 + 1]},{vn[j * 3 + 2]},{0xff} }} }} }},\n";
                }
                f.cachedVInsideCode = vtxIndex;
                vtxIndex += 3;

                // support for 32x texs only for now
                /*
                Vertex v = vertices[i]; VertexTexCoord16b vt = ConvertUVToFixed16b(texCoordVertices[i],32,32);
                byte vtn1, vtn2, vtn3;
                vtn1 = 0xff;// TODO : (byte)(127 * normalVertices[i].n1);
                vtn2 = 0xff;// TODO : (byte)(127 * normalVertices[i].n2);
                vtn3 = 0xff;// TODO : (byte)(127 * normalVertices[i].n3);
                */
                //s += $"\t{{{{{{ {v.x * scale},{v.y * scale},{v.z * scale} }}, 0, {{ {vt.u},{vt.v} }}, {{ {vtn1},{vtn2},{vtn3},{0xff} }} }} }},\n";
            }
            s += "};\n\n";
            //List<string> displayListsToDraw = new List<string>();
            foreach (var materialKvp in materials)
            {
                if (materialKvp.Value.image == null) continue; // color only isnt supported!
                string goodMatName = StringToGoodCName(materialKvp.Key); // name
                // export texture
                s += "ALIGNED8 const u16 " + goodMatName + "_txt[] = {\n\t";
                for (int j = 0; j < materialKvp.Value.image.Height; j++) {
                    for (int i = 0; i < materialKvp.Value.image.Width; i++)
                    {
                        s += $"{ColorToRGBA16(materialKvp.Value.image.GetPixel(i,j))}, ";
                    }
                }
                s += "\n};\n\n";

                s += $"static const Gfx {outName}_draw_{goodMatName}_txt[] = {{\n"; 
                s += $"\tgsDPSetTextureImage(G_IM_FMT_RGBA, G_IM_SIZ_16b, 1, {goodMatName}_txt),\n"; 
                s += $"\tgsDPLoadSync(),\n"; 
                s += $"\tgsDPLoadBlock(G_TX_LOADTILE, 0, 0, {materialKvp.Value.image.Width} * {materialKvp.Value.image.Height} - 1, CALC_DXT({materialKvp.Value.image.Width}, G_IM_SIZ_16b_BYTES)),\n"; 
                s += $"\tgsSPLight(&{outName}_lights.l, 1),\n"; 
                s += $"\tgsSPLight(&{outName}_lights.a, 2),\n"; 
                s += $"\t\n"; 
                s += $"\t\n"; 
                /*
                s += $"\tgsDPSetTextureImage(G_IM_FMT_RGBA, G_IM_SIZ_16b, 1, {goodMatName}_txt),\n";
                s += $"\tgsDPLoadSync(),\n";
                s += $"\tgsDPLoadBlock(G_TX_LOADTILE, 0, 0, {materialKvp.Value.image.Width} * {materialKvp.Value.image.Height} - 1, CALC_DXT({materialKvp.Value.image.Width}, G_IM_SIZ_16b_BYTES)),\n";*/
                foreach(Face f in faces)
                {
                    if (f.mat.name == materialKvp.Value.name)
                    {
                        s += $"\tgsSPVertex(&{outName}_vertices[{f.cachedVInsideCode}], 3, 0),\n";
                        s += $"\tgsSP1Triangle(0,1,2,0x0),\n";
                    }
                }
                s += $"\tgsSPEndDisplayList(),\n}};\n\n";
                //displayListsToDraw.Add(outName + "_draw_" + goodMatName + "_txt");
            }
            /*
            s += $"const Gfx {outName}_main_display_list[] = {{\n";
            s += $"\tgsDPPipeSync(),\n";
            s += $"\tgsDPSetCombineMode(G_CC_MODULATERGB, G_CC_MODULATERGB),\n";
            s += $"\tgsDPSetTile(G_IM_FMT_RGBA, G_IM_SIZ_16b, 0, 0, G_TX_LOADTILE, 0, G_TX_WRAP | G_TX_NOMIRROR, G_TX_NOMASK, G_TX_NOLOD, G_TX_WRAP | G_TX_NOMIRROR, G_TX_NOMASK, G_TX_NOLOD),\n";
            s += $"\tgsSPTexture(0xffff,0xffff,0,G_TX_RENDERTILE,G_ON),\n";
            s += $"\tgsDPTileSync(),\n";
            // todo: change width/height
            foreach (string dl in displayListsToDraw)
            {
                / *
                s += $"\tgsDPSetTile(G_IM_FMT_RGBA, G_IM_SIZ_16b, 8, 0, G_TX_RENDERTILE, 0, G_TX_WRAP | G_TX_NOMIRROR, 5, G_TX_NOLOD, G_TX_WRAP | G_TX_NOMIRROR, 5, G_TX_NOLOD),\n";
                s += $"\tgsDPSetTileSize(0, 0, 0, (32 - 1) << G_TEXTURE_IMAGE_FRAC, (32 - 1) << G_TEXTURE_IMAGE_FRAC),\n";
                s += $"\tgsSPDisplayList({dl}),\n";
                s += $"\tgsDPTileSync(),\n";
                * /
                s += $"\tgsSPDisplayList({dl}),\n";
            }
            s += $"\tgsSPTexture(0xFFFF, 0xFFFF, 0, G_TX_RENDERTILE, G_OFF),\n";
            s += $"\tgsDPPipeSync(),\n";
            s += $"\tgsDPSetCombineMode(G_CC_SHADE, G_CC_SHADE),\n";
            s += $"\tgsSPEndDisplayList(),\n";
            s += "};";*/
            // using Fast 64 method
            s += $"const Gfx {outName}_main_display_list_opaque[] = {{\n";
            s += $"\tgsDPPipeSync(),\n";
            s += $"\tgsDPSetCombineMode(G_CC_MODULATERGB, G_CC_MODULATERGB),\n";
            s += $"\tgsSPClearGeometryMode(G_SHADING_SMOOTH),\n";
            s += $"\tgsDPSetTile(G_IM_FMT_RGBA, G_IM_SIZ_16b, 0, 0, G_TX_LOADTILE, 0, G_TX_WRAP | G_TX_NOMIRROR, G_TX_NOMASK, G_TX_NOLOD, G_TX_WRAP | G_TX_NOMIRROR, G_TX_NOMASK, G_TX_NOLOD),\n";
            s += $"\tgsSPTexture(0xFFFF, 0xFFFF, 0, G_TX_RENDERTILE, G_ON),\n";
            foreach (var materialKvp in materials)
            {
                if (materialKvp.Value.alphaChannel) continue; // This is for no alpha channel textures.
                if (materialKvp.Value.image == null) continue; // color only isnt supported!
                string goodMatName = StringToGoodCName(materialKvp.Key); // name
                s += $"\tgsDPTileSync(),\n";
                s += $"\tgsDPSetTile(G_IM_FMT_RGBA, G_IM_SIZ_16b, {materialKvp.Value.image.Width / 4}, 0, G_TX_RENDERTILE, 0, G_TX_WRAP | G_TX_NOMIRROR, {GetPO2(materialKvp.Value.image.Height)}, G_TX_NOLOD, G_TX_WRAP | G_TX_NOMIRROR, {GetPO2(materialKvp.Value.image.Width)}, G_TX_NOLOD),\n";
                s += $"\tgsDPSetTileSize(0, 0, 0, ({materialKvp.Value.image.Width} - 1) << G_TEXTURE_IMAGE_FRAC, ({materialKvp.Value.image.Height} - 1) << G_TEXTURE_IMAGE_FRAC),\n"; // todo image width-height
                //s += $"\t\n";
                s += $"\tgsSPDisplayList({outName}_draw_{goodMatName}_txt),\n";
            }
            s += $"\tgsSPTexture(0xFFFF, 0xFFFF, 0, G_TX_RENDERTILE, G_OFF),\n";
            s += $"\tgsDPPipeSync(),\n";
            s += $"\tgsSPSetGeometryMode(G_LIGHTING),\n";
            s += $"\tgsSPSetGeometryMode(G_SHADING_SMOOTH),\n";
            s += $"\tgsDPSetCombineMode(G_CC_SHADE, G_CC_SHADE),\n";
            s += $"\tgsSPEndDisplayList(),\n";
            s += $"}};";
            s += $"\n\n";
            s += $"const Gfx {outName}_main_display_list_alpha[] = {{\n";
            s += $"\tgsDPPipeSync(),\n";
            s += $"\tgsDPSetCombineMode(G_CC_MODULATERGBA, G_CC_MODULATERGBA),\n";
            s += $"\tgsSPClearGeometryMode(G_SHADING_SMOOTH),\n";
            s += $"\tgsDPSetTile(G_IM_FMT_RGBA, G_IM_SIZ_16b, 0, 0, G_TX_LOADTILE, 0, G_TX_WRAP | G_TX_NOMIRROR, G_TX_NOMASK, G_TX_NOLOD, G_TX_WRAP | G_TX_NOMIRROR, G_TX_NOMASK, G_TX_NOLOD),\n";
            s += $"\tgsSPTexture(0xFFFF, 0xFFFF, 0, G_TX_RENDERTILE, G_ON),\n";
            foreach (var materialKvp in materials)
            {
                if (!materialKvp.Value.alphaChannel) continue; // This is for alpha channel textures.
                if (materialKvp.Value.image == null) continue; // color only isnt supported!
                string goodMatName = StringToGoodCName(materialKvp.Key); // name
                s += $"\tgsDPTileSync(),\n";
                s += $"\tgsDPSetTile(G_IM_FMT_RGBA, G_IM_SIZ_16b, {materialKvp.Value.image.Width / 4}, 0, G_TX_RENDERTILE, 0, G_TX_WRAP | G_TX_NOMIRROR, {GetPO2(materialKvp.Value.image.Height)}, G_TX_NOLOD, G_TX_WRAP | G_TX_NOMIRROR, {GetPO2(materialKvp.Value.image.Width)}, G_TX_NOLOD),\n";
                s += $"\tgsDPSetTileSize(0, 0, 0, ({materialKvp.Value.image.Width} - 1) << G_TEXTURE_IMAGE_FRAC, ({materialKvp.Value.image.Height} - 1) << G_TEXTURE_IMAGE_FRAC),\n"; // todo image width-height
                //s += $"\t\n";
                s += $"\tgsSPDisplayList({outName}_draw_{goodMatName}_txt),\n";
            }
            s += $"\tgsSPTexture(0xFFFF, 0xFFFF, 0, G_TX_RENDERTILE, G_OFF),\n";
            s += $"\tgsDPPipeSync(),\n";
            s += $"\tgsSPSetGeometryMode(G_LIGHTING),\n";
            s += $"\tgsSPSetGeometryMode(G_SHADING_SMOOTH),\n";
            s += $"\tgsDPSetCombineMode(G_CC_SHADE, G_CC_SHADE),\n";
            s += $"\tgsSPEndDisplayList(),\n";
            s += $"}};";
            File.WriteAllText(args[0] + "_outputModel.inc.c", s);
            Console.WriteLine("Done!");
        }
    }
}
