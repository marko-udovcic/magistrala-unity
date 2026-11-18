// Building textures: the facade texture array (Magistrala/Facade) as two flipbook strips imported as Texture2DArray, and the
// procedural room atlas behind the window glass (Magistrala/WindowInterior). Strips are made once from Poly Haven CC0 1k jpgs
// (CREDITS.md) by Strips(srcDir) and committed; Materials() only configures importers and materials.
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class BuildingTextures
    {
        public const string Folder = "Assets/Textures/Buildings/";
        public const string AlbedoStrip = Folder + "facade_albedo.jpg", NormalStrip = Folder + "facade_normal.jpg", RoomAtlas = Folder + "rooms.png";
        public const int Layers = 7, Size = 1024;
        public const int Plaster = 0, Stone = 1, Roof = 2, Brick = 3, Concrete = 4, Louvre = 5, Plain = 6;

        // source (Poly Haven id) and the sRGB mean the layer is scaled to (null: grey, tinted per building)
        static readonly (string id, Color? mean, float grey)[] Src =
        {
            ("white_plaster_02", null, .95f),
            ("white_sandstone_blocks_02", Hex("D9CDB2"), 0),
            ("clay_roof_tiles", Hex("C8532F"), 0),
            ("large_red_bricks", Hex("B5573D"), 0),
            ("concrete_wall_008", null, .88f),
        };
        static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

        /// one-off: build the two strips from srcDir/{id}_diff_1k.jpg and {id}_nor_gl_1k.jpg, plus the room atlas
        public static string Strips(string srcDir)
        {
            var alb = new Color32[Size * Size * Layers]; var nor = new Color32[Size * Size * Layers];
            for (int l = 0; l < Layers; l++)
            {
                Color32[] a, n;
                if (l < Src.Length)
                {
                    a = Load(Path.Combine(srcDir, Src[l].id + "_diff_1k.jpg"));
                    n = Load(Path.Combine(srcDir, Src[l].id + "_nor_gl_1k.jpg"));
                    Normalise(a, Src[l].mean, Src[l].grey);
                }
                else (a, n) = l == Louvre ? LouvreLayer() : PlainLayer();
                // flipbook order: the first layer is the TOP row of the strip; Texture2D rows run bottom-up
                int row = Layers - 1 - l;
                Array.Copy(a, 0, alb, row * Size * Size, Size * Size);
                Array.Copy(n, 0, nor, row * Size * Size, Size * Size);
            }
            Directory.CreateDirectory(Folder);
            Write(AlbedoStrip, alb, Size, Size * Layers, 92);
            Write(NormalStrip, nor, Size, Size * Layers, 94);
            var rooms = Rooms(512);
            var t = new Texture2D(2048, 1024, TextureFormat.RGBA32, false); t.SetPixels32(rooms); t.Apply();
            File.WriteAllBytes(RoomAtlas, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.Refresh();
            Configure();
            return "strips written";
        }

        static Color32[] Load(string file)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!t.LoadImage(File.ReadAllBytes(file))) throw new InvalidOperationException("cannot read " + file);
            if (t.width != Size || t.height != Size) throw new InvalidOperationException(file + " is not 1024 x 1024");
            var p = t.GetPixels32(); UnityEngine.Object.DestroyImmediate(t); return p;
        }

        static void Normalise(Color32[] p, Color? mean, float grey)
        {
            double r = 0, g = 0, b = 0;
            foreach (var c in p) { r += c.r; g += c.g; b += c.b; }
            r /= p.Length * 255.0; g /= p.Length * 255.0; b /= p.Length * 255.0;
            for (int i = 0; i < p.Length; i++)
            {
                var c = p[i];
                if (mean is Color m)
                    p[i] = new Color32(B(c.r / 255.0 / r * m.r), B(c.g / 255.0 / g * m.g), B(c.b / 255.0 / b * m.b), 255);
                else
                {
                    // grey, the detail kept at 50 % contrast: the tint is the building's colour
                    double lum = (.3 * c.r / 255.0 / r + .59 * c.g / 255.0 / g + .11 * c.b / 255.0 / b);
                    double v = grey * (1 + (lum - 1) * .5);
                    p[i] = new Color32(B(v), B(v), B(v), 255);
                }
            }
        }
        static byte B(double v) => (byte)Mathf.Clamp(Mathf.RoundToInt((float)v * 255), 0, 255);

        // shutter louvres: 0.55 m repeat, 9 slats with dark gaps and a lit upper edge (both leaves, any colour)
        static (Color32[], Color32[]) LouvreLayer()
        {
            var a = new Color32[Size * Size]; var n = new Color32[Size * Size];
            var rnd = new System.Random(5);
            float[] grain = new float[Size]; for (int x = 0; x < Size; x++) grain[x] = (float)rnd.NextDouble();
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    float s = (y / (float)Size * 9) % 1; // 0 bottom of slat .. 1 top
                    float v = s < .12f ? .35f : s > .9f ? 1f : .82f + .1f * s;
                    v *= .94f + .06f * grain[(x * 7 + (int)(y / (Size / 9f)) * 131) % Size];
                    a[y * Size + x] = new Color32(B(v), B(v), B(v), 255);
                    float ny = s < .12f ? -.6f : s > .9f ? .5f : -.25f;
                    n[y * Size + x] = new Color32(128, B(ny * .5 + .5), B(Math.Sqrt(1 - ny * ny) * .5 + .5), 255);
                }
            return (a, n);
        }
        static (Color32[], Color32[]) PlainLayer()
        {
            var a = new Color32[Size * Size]; var n = new Color32[Size * Size]; var rnd = new System.Random(9);
            for (int i = 0; i < a.Length; i++) { byte v = B(.9 + .03 * rnd.NextDouble()); a[i] = new Color32(v, v, v, 255); n[i] = new Color32(128, 128, 255, 255); }
            return (a, n);
        }

        static void Write(string path, Color32[] px, int w, int h, int q)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false); t.SetPixels32(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToJPG(q)); UnityEngine.Object.DestroyImmediate(t);
        }

        // ---- procedural rooms: 8 cells (4 x 2), each a one-point perspective of a box room, back wall = FAR of the image
        const float FAR = .45f;
        static Color32[] Rooms(int s)
        {
            var px = new Color32[4 * s * 2 * s];
            var rnd = new System.Random(77);
            Color[] walls = { Hex("E9E2D2"), Hex("D8CBB0"), Hex("C9D3CF"), Hex("E6D3C3"), Hex("F0EDE6"), Hex("D2C6A8"), Hex("BFC9D6"), Hex("E8DFC4") };
            Color[] floors = { Hex("9A6B45"), Hex("B9A68A"), Hex("7E5A3C"), Hex("C9C1B3"), Hex("A07A55"), Hex("8F8A80"), Hex("B08A60"), Hex("D2CBBD") };
            for (int c = 0; c < 8; c++)
            {
                int ox = (c % 4) * s, oy = (c / 4) * s;
                Color wall = walls[c], floor = floors[c], ceil = Hex("F2F0EA");
                // furniture on the back wall (in back-wall coordinates -1..1): a wardrobe / sofa / shelf, a picture, a lamp
                float fx = (float)rnd.NextDouble() * 1.2f - .9f, fw = .5f + (float)rnd.NextDouble() * .6f, fh = c % 2 == 0 ? 1.4f : .7f;
                Color furn = Color.Lerp(Hex("5B4636"), Hex("B7A58C"), (float)rnd.NextDouble());
                float px0 = (float)rnd.NextDouble() * 1.0f - .3f; Color pic = Color.HSVToRGB((float)rnd.NextDouble(), .35f, .6f);
                bool tiles = c == 3 || c == 7;
                for (int y = 0; y < s; y++)
                    for (int x = 0; x < s; x++)
                    {
                        float u = (x + .5f) / s * 2 - 1, v = (y + .5f) / s * 2 - 1;
                        float r = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
                        Color col;
                        if (r <= FAR)
                        {
                            float bu = u / FAR, bv = v / FAR; col = wall * .92f;
                            if (bv < -1 + fh && bu > fx && bu < fx + fw) col = furn * (bv > -1 + fh - .06f ? 1.15f : 1f);
                            else if (bv > .05f && bv < .5f && bu > px0 && bu < px0 + .45f) col = Mathf.Abs(bv - .275f) > .2f || Mathf.Abs(bu - px0 - .225f) > .2f ? Hex("3A2E24") : pic;
                            if (bv < -.92f) col *= .7f; // skirting
                        }
                        else
                        {
                            // depth along the room 0 (window) .. 1 (back wall): image scale r = lerp(1, FAR, interp), interp nonlinear
                            float interp = (1 - r) / (1 - FAR); float ds = 1 / (1 - FAR) - 1;
                            float realZ = 1 / (1 - interp / (ds + 1)); float d = (realZ - 1) * ds;
                            if (v <= -r + 1e-4f) // floor
                            {
                                float across = u / r;
                                col = floor;
                                if (tiles) col *= ((Mathf.FloorToInt(across * 4 + 8) + Mathf.FloorToInt(d * 6)) % 2 == 0) ? 1f : .88f;
                                else col *= .9f + .1f * Mathf.PerlinNoise(across * 2.5f, d * 40) * (Mathf.Repeat(across * 6, 1) < .04f ? .5f : 1f);
                                col *= .55f + .35f * d; // darker near the window (floor seen from above, less lamp light)
                                if (c % 3 == 0 && Mathf.Abs(across) < .45f && d > .35f && d < .8f) col = Color.Lerp(col, Hex("7A3B32") * .7f, .7f); // rug
                            }
                            else if (v >= r - 1e-4f) // ceiling with a lamp
                            {
                                float across = u / r; col = ceil * (.8f + .15f * d);
                                if (across * across + (d - .5f) * (d - .5f) * 4 < .02f) col = Color.white * 1.0f;
                            }
                            else // side walls
                            {
                                float up = v / r; col = wall * (.78f + .12f * d);
                                if (up < -.92f) col *= .7f;
                                if (c % 4 == 1 && u < 0 && up < -.2f && d > .2f && d < .65f) col = furn * .8f; // a cupboard on the left
                                if (c % 4 == 2 && u > 0 && up > -.1f && up < .5f && d > .3f && d < .6f) col = pic * .8f;
                            }
                        }
                        px[(oy + y) * 4 * s + ox + x] = col;
                    }
            }
            return px;
        }

        /// importer settings (texture arrays from the strips) and the two materials
        public static void Configure()
        {
            Strip(AlbedoStrip, true); Strip(NormalStrip, false);
            var ri = AssetImporter.GetAtPath(RoomAtlas) as TextureImporter;
            if (ri != null) { ri.sRGBTexture = true; ri.mipmapEnabled = true; ri.wrapMode = TextureWrapMode.Clamp; ri.maxTextureSize = 2048; ri.SaveAndReimport(); }
        }
        static void Strip(string path, bool srgb)
        {
            var im = AssetImporter.GetAtPath(path) as TextureImporter;
            if (im == null) throw new InvalidOperationException("missing " + path);
            var st = new TextureImporterSettings(); im.ReadTextureSettings(st);
            st.textureShape = TextureImporterShape.Texture2DArray; st.flipbookRows = Layers; st.flipbookColumns = 1;
            st.sRGBTexture = srgb; st.mipmapEnabled = true; st.wrapMode = TextureWrapMode.Repeat; st.filterMode = FilterMode.Trilinear; st.aniso = 4;
            im.SetTextureSettings(st); im.maxTextureSize = 8192; im.textureCompression = TextureImporterCompression.Compressed;
            im.SaveAndReimport();
        }

        public static Material Facade() => Mat("Assets/Generated/Materials/BuildingFacade.mat", "Magistrala/Facade", m =>
        {
            m.SetTexture("_Albedo", AssetDatabase.LoadAssetAtPath<Texture2DArray>(AlbedoStrip));
            m.SetTexture("_Normal", AssetDatabase.LoadAssetAtPath<Texture2DArray>(NormalStrip));
        });
        public static Material Glass() => Mat("Assets/Generated/Materials/BuildingGlass.mat", "Magistrala/WindowInterior", m =>
            m.SetTexture("_Rooms", AssetDatabase.LoadAssetAtPath<Texture2D>(RoomAtlas)));

        static Material Mat(string path, string shaderName, Action<Material> set)
        {
            var sh = Shader.Find(shaderName); if (sh == null) throw new InvalidOperationException("shader " + shaderName);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { if (!AssetDatabase.IsValidFolder("Assets/Generated/Materials")) AssetDatabase.CreateFolder("Assets/Generated", "Materials"); m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            m.shader = sh; m.enableInstancing = false; set(m); EditorUtility.SetDirty(m); return m;
        }
    }
}
