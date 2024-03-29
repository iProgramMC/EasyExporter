using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Globalization;

class Vertex
{
    public float x, y, z;
    public int r = 255, g = 255, b = 255;
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

    public float[] localU;
    public float[] localV;
    public int cachedVInsideCode;
    public Material mat;
}
class Light
{
    public int ambR =  63, ambG =  63, ambB =  63, 
               difR = 255, difG = 255, difB = 255, 
               dirX = 40, dirY = 40, dirZ = 40;

    public void Set(int r, int g, int b)
    {
        difR = r; difG = g; difB = b;
        ambR = difR >> 2;
        ambG = difG >> 2;
        ambB = difB >> 2;
    }
    public void SetDir(int x, int y, int z)
    {
        dirX = x; dirY = y; dirZ = z;
    }
}
class Material
{
    public string name = "";
    public Bitmap image = null;
    public float opacity = 1; // todo
    public bool alphaChannel = false;
    public int lightIndex = 0;
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
        public const float version = 0.2f;
        static bool doSupportTextures = true;
        static int scale = 100;

        public static bool g_isVerbose = false;

        public static void LogVerbose(string text)
        {
            if (g_isVerbose)
                Console.WriteLine(text);
        }

        static List<Vertex> vertices = new List<Vertex>();
        static List<VertexTexCoord> texCoordVertices = new List<VertexTexCoord>();
        static List<VertexNormal> normalVertices = new List<VertexNormal>();
        static List<Face> faces = new List<Face>();
        static Dictionary<string, Material> materials = new Dictionary<string, Material>();
        static List<Light> lights = new List<Light>();
        static Dictionary<int, int> colorToLightIndex = new Dictionary<int, int>();
        static int LX = 40, LY = 40, LZ = 40;

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
        static VertexTexCoord16b ConvertUVToFixed16b(VertexTexCoord c, int tw, int th, bool precise)
        {
            return new VertexTexCoord16b()
            {
                u = FloatToFixed16b((c.u * tw)-(precise?1:0)),
                v = FloatToFixed16b((c.v * th)-(precise?1:0)),
            };
        }

        static void ReadVertexColorFile(string filename)
        {
            string[] lines = File.ReadAllLines(filename);
            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.StartsWith("//")) continue;
                if (line.StartsWith("#")) continue;
                string[] tokens = line.Split('|');
                if (tokens.Length < 1) continue; // safety
                if (tokens[0] == "vertex_color")
                {
                    if (tokens.Length >= 5)
                    {
                        int index = int.Parse(tokens[1]);
                        float rf = float.Parse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture);
                        float gf = float.Parse(tokens[3], NumberStyles.Float, CultureInfo.InvariantCulture);
                        float bf = float.Parse(tokens[4], NumberStyles.Float, CultureInfo.InvariantCulture);

                        int r = (int)(rf * 255), g = (int)(gf * 255), b = (int)(bf * 255);
                        vertices[index].r = r;
                        vertices[index].g = g;
                        vertices[index].b = b;
                    }
                }
            }
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
            foreach (string line in lines)
            {
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
                            curMat.image = new Bitmap(2, 2);
                            for (int i = 0; i < 2 * 2; i++)
                            {
                                curMat.image.SetPixel(i & 0x1, i >> 1, Color.White);//!optimizations pls
                            }
                        }
                        if (materials.ContainsKey(curMat.name)) Console.WriteLine($"Warning: Material {curMat.name} already exists and was overridden.");
                        materials[curMat.name] = curMat;
                    }
                    curMat = new Material();
                    curMat.name = tokens[1];
                }
                else if (tokens[0].StartsWith("map_K") && tokens[0] != "map_Ks" /* dont include specular maps */ && curMat.image == null /* don't load the same img twice */ )
                {
                    string fn = "";
                    fn = tokens[1].Replace("\\\\", "\\"); //! Blender paths use \\ instead of one \ on window
                    try
                    {
                        curMat.image = new Bitmap(fn);
                        bool alpha = false;
                        // find if theres alpha in the image
                        for (int i = 0; i < curMat.image.Width; i++)
                        {
                            for (int j = 0; j < curMat.image.Height; j++)
                            {
                                if (curMat.image.GetPixel(i, j).A < 255)
                                {
                                    alpha = true; break;
                                }
                            }
                        }
                        curMat.alphaChannel = alpha;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error: Could not load texture {fn} referenced by material {curMat.name}: {ex.Message}. Aborting");
                        Environment.Exit(1);
                    }
                }
                else if (tokens[0] == "d")
                {
                    curMat.opacity = float.Parse(tokens[1], CultureInfo.InvariantCulture);
                }
                else if (tokens[0] == "Kd")
                {
                    float rf = float.Parse(tokens[1], CultureInfo.InvariantCulture);
                    float gf = float.Parse(tokens[2], CultureInfo.InvariantCulture);
                    float bf = float.Parse(tokens[3], CultureInfo.InvariantCulture);
                    int r = (int)(rf * 255), g = (int)(gf * 255), b = (int)(bf * 255);

                    int idn = r << 16 | g << 8 | b;
                    if (!colorToLightIndex.ContainsKey(idn))
                    {
                        colorToLightIndex[idn] = lights.Count;
                        Light n = new Light();
                        n.SetDir(LX, LY, LZ);
                        n.Set   (r,  g,  b);
                        lights.Add(n);
                    }
                    curMat.lightIndex = colorToLightIndex[idn];
                }
                else if (tokens[0] == "Tr")
                {
                    curMat.opacity = 1F - float.Parse(tokens[1], CultureInfo.InvariantCulture);
                }
            }
            // push last material
            if (curMat != null)
            {
                if (curMat.image == null)
                {
                    curMat.image = new Bitmap(2, 2);
                    for (int i = 0; i < 2 * 2; i++)
                    {
                        curMat.image.SetPixel(i % 2, i / 2, Color.White);
                    }
                }
                materials[curMat.name] = curMat;
            }
        }

        static bool IsInBound(float fx, float fy, float minx, float miny, float maxx, float maxy)
        {
            return !(fx < minx || fx > maxx || fy < miny || fy > maxy);
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

        static bool DoesStringContainAnyOfTheseSubstrings(string mainStr, string[] substrs)
        {
            string h = mainStr.ToLower();
            foreach (string substr in substrs)
                if (h.Contains(substr)) return true;
            return false;
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
        
        static UInt16 ColorToIA16(Color c)
        {
            // 5R 5G 5B 1A
            //average
            int i = c.R;
            //int i = (c.R + c.B + c.G) / 3;
            int a = c.A;
            return (ushort)(i << 8 | a);
        }

        static void Main(string[] args)
        {
            bool sketchupMode = false;
            bool nuf = false, fe = true, doNotOptimize = false, precise = false;
            string vertexColorFileName = "";
            Console.WriteLine($"EasyExporter V{version} (C) 2020-2021 iProgramInCpp");
            Console.WriteLine("[!!BETA!!] This tool is beta-ware, so some stuff might not work correctly. If some faces aren't drawing properly, use \"-nop\".");
            if (args.Length < 1)
            {
                Console.WriteLine("usage: convert <your obj file> [output name] [-sum] [-noscale] [-nuf] [-nop] [-fe]");
                Console.WriteLine("   -sum: swaps the y and z axes (sketchup + lipid obj mode)");
                Console.WriteLine("   -noscale: sets the scaling factor to 1");
                Console.WriteLine("   -verbose: actively shows the progress of the app");
                Console.WriteLine("   -nuf:  disables fixing UV overflow (experimental). Use this if you're sure you don't have UV overflow in your model, or if the UV overflow fix fails.");
                Console.WriteLine("   -nofe: disables the UV fixing automatically done by program, not recommended unless you know what you're doing!");
                Console.WriteLine("   -nop:  do not optimize the model (loads 3 vertices per triangle, is slower but more reliable)");
                Console.WriteLine("   -prec: avoid seams in model mesh (will do 0-990 instead of 0-1024 in terms of UV coordinates going from 0 to 1)");
                Console.WriteLine("   -vtxf=<filename>: Since OBJ doesn't support vertex colors, use the bpy tool provided to specify vertex colors using this switch.");
                return;
            }
            foreach (string arg in args)
            {
                if (arg.StartsWith("-vtxf="))
                {
                    vertexColorFileName = arg.Substring("-vtxf=".Length);
                    Console.WriteLine("Using vertex colors located at : " + vertexColorFileName);
                }
            }
            if (args.Contains("-prec"))
            {
                Console.WriteLine("Texture precision enabled");
                precise = true;
            }
            if (args.Contains("-verbose"))
            {
                Console.WriteLine("Verbose log enabled.");
                g_isVerbose = true;
            }
            if (args.Contains("-nofe"))
            {
                Console.WriteLine("UV Fix Extreme disabled.");
                fe = false;
            }
            if (args.Contains("-nop"))
            {
                Console.WriteLine("Model is not being optimized.");
                doNotOptimize = true;
            }
            if (args.Contains("-sum"))
            {
                Console.WriteLine("Activated Sketchup exporter mode.");

                LogVerbose("Adding default Layer_Layer0 material, sometimes it's used and not implemented in the mtl file itself!!");

                //add a new material named Layer_Layer0
                Material curMatA = new Material()
                {
                    name = "Layer_Layer0",
                    alphaChannel = false,
                };
                curMatA.image = new Bitmap(8, 8);
                for (int i = 0; i < 8 * 8; i++)
                {
                    curMatA.image.SetPixel(i % 8, i / 8, Color.White);
                }
                materials["Layer_Layer0"] = curMatA;

                sketchupMode = true;
            }
            if (args.Contains("-noscale"))
            {
                Console.WriteLine("Scaling factor set to 1.");
                scale = 1;
            }
            if (args.Contains("-nuf"))
            {
                Console.WriteLine("Disabled UV overflow fixing.");
                if (fe)
                {
                    fe = false;
                    Console.WriteLine("Warning: -fe and -nuf switches both running at the same time, -nuf takes priority over -fe");
                }
                nuf = true;
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
                    case "mtllib":
                        {
                            LogVerbose($"Importing material library from \"{tokens[1]}\"");
                            ImportMaterial(tokens[1]);
                            break;
                        }
                    case "v":
                        {
                            // Add Vertex
                            Vertex vtx = new Vertex()
                            {
                                x = float.Parse(tokens[1], CultureInfo.InvariantCulture), // always a dot, never comma
                                y = float.Parse(tokens[2], CultureInfo.InvariantCulture), // always a dot, never comma
                                z = float.Parse(tokens[3], CultureInfo.InvariantCulture), // always a dot, never comma
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
                                u = float.Parse(tokens[1], CultureInfo.InvariantCulture), // always a dot, never comma
                                v = 1 - float.Parse(tokens[2], CultureInfo.InvariantCulture), // N64 requires this :)
                            };
                            texCoordVertices.Add(vtx);
                            break;
                        }
                    case "vn":
                        {
                            // Add Vertex Normal Coordinate
                            VertexNormal vtx = new VertexNormal()
                            {
                                n1 = float.Parse(tokens[1], CultureInfo.InvariantCulture), // always a dot, never comma
                                n2 = float.Parse(tokens[2], CultureInfo.InvariantCulture), // always a dot, never comma
                                n3 = float.Parse(tokens[3], CultureInfo.InvariantCulture), // always a dot, never comma
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
                            LogVerbose("Switching material to " + tokens[1]);
                            curMat = tokens[1];
                            break;
                        }
                    case "f":
                        {
                            // face - always a triangle, quads not supported
                            int[] v = new int[3];
                            int[] vt = new int[3];
                            int[] vn = new int[3];
                            float[] localU = new float[3], localV = new float[3];
                            for (int i = 0; i < 3; i++)
                            {
                                string[] subTokens = tokens[i + 1].Split('/');
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
                                        if (subTokens[1] == "")
                                        { vt[i] = -1; localU[i] = 0; localV[i] = 0; }
                                        else
                                        { vt[i] = int.Parse(subTokens[1]) - 1; localU[i] = texCoordVertices[vt[i]].u; localV[i] = texCoordVertices[vt[i]].v; }


                                        break;
                                    case 3:
                                        // vertex coord
                                        v[i] = int.Parse(subTokens[0]) - 1;
                                        if (subTokens[1] == "")
                                        { vt[i] = -1; localU[i] = 0; localV[i] = 0; }
                                        else
                                        { vt[i] = int.Parse(subTokens[1]) - 1; localU[i] = texCoordVertices[vt[i]].u; localV[i] = texCoordVertices[vt[i]].v; }
                                        vn[i] = int.Parse(subTokens[2]) - 1;
                                        break;
                                }
                            }
                            Face f;
                            try
                            {
                                f = new Face()
                                {
                                    v = v,
                                    vn = vn,
                                    vt = vt,
                                    localU = localU,
                                    localV = localV,
                                    mat = materials[curMat]
                                };
                                faces.Add(f);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error while creating display lists: {curMat} material does not exist or is bad.");
                                Environment.Exit(1);
                            }
                            break;
                        }
                    default:
                        {
                            Console.WriteLine($"Warning: Unrecognized instruction {tokens[0]}, this may go downhill quick!");
                            break;
                        }
                }
            };

            Console.WriteLine("Loaded obj file. Reading vertex colors if applicable...");
            if (vertexColorFileName.Length > 0)
            {
                ReadVertexColorFile(vertexColorFileName);
            }
            else Console.WriteLine("No vertex colors specified.");

            // Loaded in everything, let's write it!
            Console.WriteLine("Writing collision...");
            string outName = "output";
            if (args.Length > 1)
            {
                outName = StringToGoodCName(args[1]);
            }

            // Sort faces by material name
            //faces.Sort(new Comparison<Face>(FaceCompareShit)); // makes it easier to clump things together

            //string curMatName = "";

            string[] unlitMaterialNames = new string[] { "fence", "unlit", "nolit" };
            string[] doubleSideMaterialNames = new string[] { "fence", "twoside" };
            string[] lavaMaterialNames = new string[] { "lava" };
            string[] slipperyMaterialNames = new string[] { "slip" };
            string[] noCollideMaterialNames = new string[] { "nocoll", "fake", "visonly", "visual" };
            string[] noVisualMaterialNames = new string[] { "novis", "collonly", "collision" };
            string[] deathBarrierMatNames = new string[] { "deathplane" };
            string[] ia16MatNames = new string[] { "ia16" };
            bool evenMakeUnlitMatls = true;

            string s = $"// Written by EasyExporter V{version} (C) 2020-2021 iProgramInCpp.\n\nconst Collision {outName}_collision[] = {{";
            s += $"\n\tCOL_INIT(),\n\tCOL_VERTEX_INIT({vertices.Count}),\n";
            foreach (Vertex v in vertices)
            {
                s += $"\tCOL_VERTEX({(int)(v.x * scale)},{(int)(v.y * scale)},{(int)(v.z * scale)}),\n";
            }

            foreach (var v in materials)
            {
                string matType = "SURFACE_DEFAULT";
                string header = "", end = "";

                //if (unlitMaterialNames.Contains(v.Value.name))
                if (DoesStringContainAnyOfTheseSubstrings(v.Value.name, lavaMaterialNames     )) matType = "SURFACE_BURNING";
                if (DoesStringContainAnyOfTheseSubstrings(v.Value.name, deathBarrierMatNames  )) matType = "SURFACE_DEATH_PLANE";
                if (DoesStringContainAnyOfTheseSubstrings(v.Value.name, slipperyMaterialNames )) matType = "SURFACE_SLIPPERY";
                if (DoesStringContainAnyOfTheseSubstrings(v.Value.name, noCollideMaterialNames)) { matType = "SURFACE_SLIPPERY"; header = "/*"; end = "*/"; }

                LogVerbose("Writing collision for " + v.Value.name + ".");
                string s2 = ""; int mtlCnt = 0;
                foreach (Face f in faces)
                {
                    if (f.mat == v.Value)
                    {
                        s2 += $"\tCOL_TRI({f.v[0]},{f.v[1]},{f.v[2]}),\n";
                        mtlCnt++;
                    }
                }
                s += $"\t// {v.Value.name} Material...\n\t"+header+$"COL_TRI_INIT({matType}, {mtlCnt}),\n" + s2+end;
            }

            s += "\tCOL_TRI_STOP(),\n\tCOL_END(),\n};";
            File.WriteAllText(args[0] + "_outputCollision.inc.c", s);
            s = "";


            Console.WriteLine("Wrote collision of {0} verts and {1} tris, writing display list data...", vertices.Count, faces.Count);
            s += $"// Written by EasyExporter V{version} (C) 2020-2021 iProgramInCpp.\n\n";
            s += $"static const Lights1 {outName}_lights[] = {{\n";// gdSPDefLights1(0x3f,0x3f,0x3f,0xff,0xff,0xff,0x28,0x28,0x28);\n";
            s += "\t//             ambient R G B    diffuse R G B    light dir\n";
            int lol = 0;
            foreach (Light l in lights)
            {
                s += $"\tgdSPDefLights1(0x{l.ambR:X2},0x{l.ambG:X2},0x{l.ambB:X2},  0x{l.difR:X2},0x{l.difG:X2},0x{l.difB:X2},  0x{l.dirX:X2},0x{l.dirY:X2},0x{l.dirZ:X2})";
                lol++;
                if (lol != lights.Count) s += ",";
                s += "\n";
            }
            s += "};\n";
            s += $"static const Vtx {outName}_vertices[] = {{\n";
            int vtxIndex = 0;
            LogVerbose("Writing vertex data...");
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
                    //vt[j] = ConvertUVToFixed16b(f.vt[j] == -1 ? new VertexTexCoord() : texCoordVertices[f.vt[j]], f.mat.image.Width, f.mat.image.Height);
                    vn[j * 3 + 0] = (byte)(127 * (normalVertices[f.vn[j]]).n1);
                    vn[j * 3 + 1] = (byte)(127 * (normalVertices[f.vn[j]]).n2);
                    vn[j * 3 + 2] = (byte)(127 * (normalVertices[f.vn[j]]).n3);
                    //Console.WriteLine(curMat);
                    if (DoesStringContainAnyOfTheseSubstrings(f.mat.name, unlitMaterialNames) && evenMakeUnlitMatls) //! hardcoded for now
                    {
                        vn[j * 3 + 0] = (byte)vertices[f.v[j]].r;
                        vn[j * 3 + 1] = (byte)vertices[f.v[j]].g;
                        vn[j * 3 + 2] = (byte)vertices[f.v[j]].b;
                    }
                }

                // This is code that's designed to fix UV overflow on certain exporters.
                int iterCount = 100000;
                if (f.vt[0] == -1) // no texture?
                {
                    vt[0] = new VertexTexCoord16b();
                    vt[1] = new VertexTexCoord16b();
                    vt[2] = new VertexTexCoord16b();
                    goto skipOverflowFix;
                }
                //disabled UV overflow fix?
                if (nuf) goto skipOverflowFix;

                if (fe)
                {
                    // UV Fix Extreme
                    float[] ue = new float[3], ve = new float[3];
                    float avgU = 0, avgV = 0;
                    for (int q = 0; q < 3; q++)
                    {
                        ue[q] = f.localU[q];
                        ve[q] = f.localV[q];
                        avgU += f.localU[q];
                        avgV += f.localV[q];
                        //f.localU[q] = (float)(f.localU[q] - Math.Floor(f.localU[q]));
                        //f.localV[q] = (float)(f.localV[q] - Math.Floor(f.localV[q]));
                    }
                    avgU /= 3; avgV /= 3;
                    float nAvgU = (float)(avgU - Math.Floor(avgU));
                    float nAvgV = (float)(avgV - Math.Floor(avgV));
                    //calculate delta
                    float deltaU = nAvgU - avgU;
                    float deltaV = nAvgV - avgV;
                    for (int q = 0; q < 3; q++)
                    {
                        f.localU[q] += deltaU;
                        f.localV[q] += deltaV;
                    }
                }
                else
                {
                    float minu = 999999, maxu = -999999, minv = 999999, maxv = -999999;
                    for (int j = 0; j < 3; j++)
                    {
                        //if (IsInBound(f.localU[j], f.localV[j], minx, miny, maxx, maxy) {
                        minu = Math.Min(minu, f.localU[j]);
                        minv = Math.Min(minv, f.localV[j]);
                        maxu = Math.Max(maxu, f.localU[j]);
                        maxv = Math.Max(maxv, f.localV[j]);
                        //}
                    }
                    float dx = Math.Abs(maxu - minu);
                    float dy = Math.Abs(maxv - minv);
                    if (dx + 4 >= f.mat.image.Width || dy + 4 >= f.mat.image.Height)
                    {
                        Console.WriteLine("Error: uv overflow so big that it can't be fixed automatically. Please fix it yourself. Some faces might look weird.");
                        goto skipOverflowFix;
                    }

                    float minx = -16 / (f.mat.image.Width / 32f), miny = -16 / (f.mat.image.Height / 32f);
                    float maxx = 15 / (f.mat.image.Width / 32f), maxy = 15 / (f.mat.image.Height / 32f);

                    bool firstIter = true;

                    int coordsThatNeedFixing = 0;//bitflag
                    do
                    {
                        coordsThatNeedFixing = 0;
                        for (int j = 0; j < 3; j++)
                        {
                            if (!IsInBound(f.localU[j], f.localV[j], minx, miny, maxx, maxy))
                            {
                                if (firstIter) Console.WriteLine("Warning: UV overflow present in your model. Fixed the face automatically. You might see this when working with LIPID OBJ exporter for sketchup, this is only because of the way the addon works.");
                                coordsThatNeedFixing |= (1 << i);
                                firstIter = false;
                                //! check what this vert has done wrong. Moving by integers has no real effect on what the model looks like.
                                if (f.localU[j] < minx)
                                {
                                    float badu = f.localU[j];
                                    for (int r = 0; r < 3; r++)
                                    {
                                        f.localU[r] -= (float)Math.Floor(badu - minx);
                                    }
                                    iterCount--;
                                }
                                if (f.localU[j] > maxx)
                                {
                                    float badu = f.localU[j];
                                    for (int r = 0; r < 3; r++)
                                    {
                                        f.localU[r] -= (float)Math.Ceiling(badu - maxx);
                                    }
                                    iterCount--;
                                }
                                if (f.localV[j] < miny)
                                {
                                    float badv = f.localV[j];
                                    for (int r = 0; r < 3; r++)
                                    {
                                        f.localV[r] -= (float)Math.Floor(badv - miny);
                                    }
                                    iterCount--;
                                }
                                if (f.localV[j] > maxy)
                                {
                                    float badv = f.localV[j];
                                    for (int r = 0; r < 3; r++)
                                    {
                                        f.localV[r] -= (float)Math.Ceiling(badv - maxy);
                                    }
                                    iterCount--;
                                }
                            }

                        }

                    } while (coordsThatNeedFixing != 0 && iterCount > 0);
                    //goto fixedOverflowAlready;
                }
                skipOverflowFix:

                if (iterCount <= 0)
                {
                    Console.WriteLine("Severe warning: trying to fix UV overflow resulted in an infinite loop. Please fix your model.");
                }

                //fixedOverflowAlready:
                for (int j = 0; j < 3; j++)
                {
                    VertexTexCoord vtc = new VertexTexCoord();
                    if (f.vt[j] != -1)
                    {
                        vtc.u = f.localU[j];
                        vtc.v = f.localV[j];
                    }
                    vt[j] = ConvertUVToFixed16b(vtc/*f.vt[j] == -1 ? new VertexTexCoord() : texCoordVertices[f.vt[j]]*/, f.mat.image.Width, f.mat.image.Height, precise);
                    s += $"\t{{{{{{ {v[j].x * scale},\t{v[j].y * scale},\t{v[j].z * scale}\t }}, 0, {{\t {vt[j].u},\t{vt[j].v} }}, {{ \t{vn[j * 3 + 0]},\t{vn[j * 3 + 1]},\t{vn[j * 3 + 2]},\t{0xff} }} }} }},\n";
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

            //!TODO: move to using StringBuilder, makes it faster and abuses GC less
            s += "};\n\n";

            LogVerbose("Separating each material used in the mesh into its own display list...");

            //List<string> displayListsToDraw = new List<string>();
            foreach (var materialKvp in materials)
            {
                if (materialKvp.Value.image == null) continue; // color only isnt supported!
                if (DoesStringContainAnyOfTheseSubstrings(materialKvp.Value.name, noVisualMaterialNames)) continue;
                bool isIA16 = DoesStringContainAnyOfTheseSubstrings(materialKvp.Value.name, ia16MatNames);
                LogVerbose("Writing texture " + materialKvp.Value.name);
                string goodMatName = StringToGoodCName(materialKvp.Key); // name
                // export texture

                int pixels_written = 0, total_pixels = materialKvp.Value.image.Height * materialKvp.Value.image.Width;

                StringBuilder stringBuilder = new StringBuilder(8192);
                s += "ALIGNED8 const u16 " + goodMatName + "_txt[] = {\n\t";
                for (int j = 0; j < materialKvp.Value.image.Height; j++)
                {
                    for (int e = 0; e < materialKvp.Value.image.Width; e++)
                    {
                        stringBuilder.Append($"{(isIA16 ? ColorToIA16(materialKvp.Value.image.GetPixel(e,j)) : ColorToRGBA16(materialKvp.Value.image.GetPixel(e, j)))}, ");
                        pixels_written++;
                    }
                    if (g_isVerbose)
                        Console.Write($"\rWrote {pixels_written} pixels out of {total_pixels}.");
                }
                s += stringBuilder.ToString();
                s += "\n};\n\n";
                if (g_isVerbose)
                    Console.WriteLine();

                LogVerbose("Writing display list for the material...");

                s += $"static const Gfx {outName}_draw_{goodMatName}_txt[] = {{\n";
                s += $"\tgsDPSetTextureImage(G_IM_FMT_{(isIA16 ? "IA" : "RGBA")}, G_IM_SIZ_16b, 1, {goodMatName}_txt),\n";
                s += $"\tgsDPLoadSync(),\n";
                s += $"\tgsDPLoadBlock(G_TX_LOADTILE, 0, 0, {materialKvp.Value.image.Width} * {materialKvp.Value.image.Height} - 1, CALC_DXT({materialKvp.Value.image.Width}, G_IM_SIZ_16b_BYTES)),\n";
                s += $"\tgsSPLight(&{outName}_lights[{materialKvp.Value.lightIndex}].l, 1),\n";
                s += $"\tgsSPLight(&{outName}_lights[{materialKvp.Value.lightIndex}].a, 2),\n";
                s += $"\t\n";
                if (DoesStringContainAnyOfTheseSubstrings(materialKvp.Key, unlitMaterialNames) && evenMakeUnlitMatls) //! hardcoded for now
                {
                    s += $"\tgsSPClearGeometryMode(G_LIGHTING),\n";
                }
                if (DoesStringContainAnyOfTheseSubstrings(materialKvp.Key, doubleSideMaterialNames) && evenMakeUnlitMatls) //! hardcoded for now
                {
                    s += $"\tgsSPClearGeometryMode(G_CULL_BACK),\n";
                }
                s += $"\t\n";
                /*
                s += $"\tgsDPSetTextureImage(G_IM_FMT_RGBA, G_IM_SIZ_16b, 1, {goodMatName}_txt),\n";
                s += $"\tgsDPLoadSync(),\n";
                s += $"\tgsDPLoadBlock(G_TX_LOADTILE, 0, 0, {materialKvp.Value.image.Width} * {materialKvp.Value.image.Height} - 1, CALC_DXT({materialKvp.Value.image.Width}, G_IM_SIZ_16b_BYTES)),\n";*/
                int faceCount = 0;
                int faceStart = -1, eeai = 0;
                foreach (Face f in faces)
                {
                    if (f.mat.name == materialKvp.Value.name)
                    {
                        if (doNotOptimize)
                        {
                            s += $"\tgsSPVertex(&{outName}_vertices[{f.cachedVInsideCode}], 3, 0),\n";
                            s += $"\tgsSP1Triangle(0,1,2,0x0),\n";
                        }
                        else
                        {
                            faceCount++;
                            if (faceStart == -1)
                                faceStart = eeai;
                        }
                    }
                    eeai++;
                }

                if (!doNotOptimize)
                {
                    if (faceStart != -1)
                    {
                        int amountsOf5s = faceCount / 5;
                        int leftover = faceCount % 5, i;
                        for (i = 0; i < amountsOf5s; i++)
                        {
                            Face first_face = faces[i * 5 + faceStart];
                            s += $"\tgsSPVertex(&{outName}_vertices[{first_face.cachedVInsideCode}], 15, 0),\n";
                            for (int j = 0; j < 5; j++)
                            {
                                Face f = faces[i * 5 + j + faceStart];
                                s += $"\tgsSP1Triangle({0 + j * 3},{1 + j * 3},{2 + j * 3},0x0),\n";
                            }
                        }
                        if (leftover != 0)
                        {
                            Face first_face2 = faces[i * 5 + faceStart];
                            s += $"\tgsSPVertex(&{outName}_vertices[{first_face2.cachedVInsideCode}], 15, 0),\n";
                            for (int j = 0; j < leftover; j++)
                            {
                                Face f = faces[i * 5 + j + faceStart];
                                s += $"\tgsSP1Triangle({0 + j * 3},{1 + j * 3},{2 + j * 3},0x0),\n";
                            }
                        }
                    }
                    else
                    {
                        s += $"//! This is not drawing anything, please remove!!\t\n";
                    }
                }
                s += $"\t\n";
                if (DoesStringContainAnyOfTheseSubstrings(materialKvp.Key, unlitMaterialNames) && evenMakeUnlitMatls) //! hardcoded for now
                {
                    s += $"\tgsSPSetGeometryMode(G_LIGHTING),\n";
                }
                if (DoesStringContainAnyOfTheseSubstrings(materialKvp.Key, doubleSideMaterialNames) && evenMakeUnlitMatls) //! hardcoded for now
                {
                    s += $"\tgsSPSetGeometryMode(G_CULL_BACK),\n";
                }
                s += $"\t\n";
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

            LogVerbose("Done writing material DLs, writing main DL that unites them all together");

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
                bool isIA16 = DoesStringContainAnyOfTheseSubstrings(materialKvp.Value.name, ia16MatNames);
                s += $"\tgsDPSetTile(G_IM_FMT_{(isIA16?"IA":"RGBA")}, G_IM_SIZ_16b, {materialKvp.Value.image.Width / 4}, 0, G_TX_RENDERTILE, 0, G_TX_WRAP | G_TX_NOMIRROR, {GetPO2(materialKvp.Value.image.Height)}, G_TX_NOLOD, G_TX_WRAP | G_TX_NOMIRROR, {GetPO2(materialKvp.Value.image.Width)}, G_TX_NOLOD),\n";
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
                bool isIA16 = DoesStringContainAnyOfTheseSubstrings(materialKvp.Value.name, ia16MatNames);
                s += $"\tgsDPSetTile(G_IM_FMT_{(isIA16 ? "IA" : "RGBA")}, G_IM_SIZ_16b, {materialKvp.Value.image.Width / 4}, 0, G_TX_RENDERTILE, 0, G_TX_WRAP | G_TX_NOMIRROR, {GetPO2(materialKvp.Value.image.Height)}, G_TX_NOLOD, G_TX_WRAP | G_TX_NOMIRROR, {GetPO2(materialKvp.Value.image.Width)}, G_TX_NOLOD),\n";
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
