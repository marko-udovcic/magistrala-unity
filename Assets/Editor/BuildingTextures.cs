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
        /// grime masks (ambientCG CC0 leaking decals, 512², linear): r dense rain streaks, g sparse runs, b single drips; 1 = dirty
        public const string GrimeAtlas = Folder + "grime.png";
        public const int Layers = 8, Size = 1024;
        public const int Plaster = 0, Stone = 1, Roof = 2, Brick = 3, Concrete = 4, Louvre = 5, Plain = 6, Rubble = 7;
        /// the plaster variants (Poly Haven CC0 2K plasters, 1024 per layer; Tools/zbuke.py from assets-src/polyhaven-zbuke): a second
        /// texture array the plaster / concrete layers switch to (Look.variant 1..9 = layer 0..8, 0 = layer 0 above), plus the near micro grain
        public const string PlasterFolder = "Assets/Textures/PolyHaven/zbuke/";
        public const string PlasterAlbedo = PlasterFolder + "zbuke_albedo.jpg", PlasterNormal = PlasterFolder + "zbuke_normal.jpg", Micro = PlasterFolder + "mikro.png";
        public const int PlasterLayers = 16;
        public const int PConcreteWall = 1, PWhiteRough = 2, PCracked = 3, PPreconcrete = 4, PBeige = 5, PPainted = 6, PYellow = 7, PRedWeathered = 8, PGrey = 9;
        /// not a plaster: the trimmed yard hedge (Tools/zbuke/zivica.py; HouseBuilder Slot.Hedge, its colour kept, no weathering)
        public const int PHedge = 10;
        /// Marko's approved materials in the plaster array (zbuke.py layers 10 / 11): old peeling render (worn_plaster_wall) and the
        /// corrugated metal of hall roofs (corrugated_iron_02, HouseBuilder FlatRoof of industrial buildings)
        public const int PWorn = 11, PMetal = 12;
        /// the UKRAINIAN CITY pack's layers appended by PackLayers() (layers 12..14): light plasters Plaster_H / Plaster_J (tinted per
        /// house) and the dressed stone Brick_E (a variant of the stone layer, Facade: variant 15 on layer 1). 15 is the last variant
        /// the wall code holds (64 * variant < 1024).
        public const int PPackH = 13, PPackJ = 14, PStoneE = 15;
        /// Paket2 (28. 11.): the pack's barrel tiles RoofTiles_A as _Plaster layer 15, a variant of the ROOF layer (Facade: variant 15 on
        /// layer 2), only on the houses inside the Paket2 areas (Assets/Editor/paket2-podrucja.json)
        public const int PRoofPack = 15;

        // source (Poly Haven id) and the sRGB mean the layer is scaled to (null: grey, tinted per building)
        // grey layers keep `contrast` of their detail (plaster 0.8: the trowel marks and blotches read at street level)
        static readonly (string id, Color? mean, float grey, float contrast)[] Src =
        {
            ("white_plaster_02", null, .86f, .8f),
            // dressed limestone blocks in courses (Marko's approved pick 25. 11.: white_sandstone_blocks_02; before: sandstone_blocks_08)
            ("white_sandstone_blocks_02", Hex("C6BEAB"), 0, 1),
            ("clay_roof_tiles_02", Hex("A9573F"), 0, 1),
            ("large_red_bricks", Hex("B5573D"), 0, 1),
            // bare concrete with formwork lines (approved: concrete_wall_007; before concrete_wall_008)
            ("concrete_wall_007", null, .8f, .7f),
        };
        // layer 5: shutter louvres (approved: painted_metal_shutter, grey, tinted per house; before procedural)
        static readonly (string id, Color? mean, float grey, float contrast) LouvreSrc = ("painted_metal_shutter", null, .84f, 1);
        // layer 7 (after the louvre / plain layers): dry-stone yard walls, Dalmatian limestone rubble (approved: stone_wall; before old_stone_wall)
        static readonly (string id, Color? mean, float grey, float contrast) RubbleSrc = ("stone_wall", Hex("C9BDA3"), 0, 1);
        static (string id, Color? mean, float grey, float contrast) SrcOf(int l) => l == Rubble ? RubbleSrc : l == Louvre ? LouvreSrc : Src[l];
        /// Marko's approved Poly Haven set (assets-src/polyhaven-materijali, 25. 11. 2026) into the committed strips: the 1k copies
        /// that Tools/zbuke/materijali.py writes into dir1k (stone, roof, brick, concrete, louvre, rubble; plaster / plain stay)
        public static string Approved(string dir1k)
        {
            var log = new System.Text.StringBuilder();
            foreach (int l in new[] { Stone, Roof, Brick, Concrete, Louvre, Rubble }) log.Append(ReplaceLayer(l, dir1k)).Append("; ");
            return log.ToString();
        }
        static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

        /// one-off: build the two strips from srcDir/{id}_diff_1k.jpg and {id}_nor_gl_1k.jpg, plus the room atlas
        public static string Strips(string srcDir)
        {
            var alb = new Color32[Size * Size * Layers]; var nor = new Color32[Size * Size * Layers];
            for (int l = 0; l < Layers; l++)
            {
                Color32[] a, n;
                if (l < Src.Length || l == Rubble || l == Louvre)
                {
                    var src = SrcOf(l);
                    a = Load(Path.Combine(srcDir, src.id + "_diff_1k.jpg"));
                    n = Load(Path.Combine(srcDir, src.id + "_nor_gl_1k.jpg"));
                    Normalise(a, src.mean, src.grey, src.contrast);
                    // the normal strip's blue holds the roughness (z is rebuilt in the shader)
                    var ro = Load(Path.Combine(srcDir, src.id + "_rough_1k.jpg"));
                    for (int i = 0; i < n.Length; i++) n[i].b = ro[i].r;
                }
                else (a, n) = PlainLayer();
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
            Grime(srcDir);
            AssetDatabase.Refresh();
            Configure();
            return "strips written";
        }

        /// replace one layer of the committed strips from srcDir/{id}_diff_1k.jpg, _nor_gl_1k.jpg, _rough_1k.jpg (the other
        /// layers' sources need not be present)
        public static string ReplaceLayer(int layer, string srcDir)
        {
            var src = SrcOf(layer);
            var a = Load(Path.Combine(srcDir, src.id + "_diff_1k.jpg")); var n = Load(Path.Combine(srcDir, src.id + "_nor_gl_1k.jpg"));
            Normalise(a, src.mean, src.grey, src.contrast);
            var ro = Load(Path.Combine(srcDir, src.id + "_rough_1k.jpg"));
            for (int i = 0; i < n.Length; i++) n[i].b = ro[i].r;
            int row = Layers - 1 - layer;
            foreach (var (path, px, q) in new[] { (AlbedoStrip, a, 92), (NormalStrip, n, 94) })
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(File.ReadAllBytes(path));
                if (t.width != Size || t.height != Size * Layers) throw new InvalidOperationException(path + " is not a strip");
                var all = t.GetPixels32(); UnityEngine.Object.DestroyImmediate(t);
                Array.Copy(px, 0, all, row * Size * Size, Size * Size);
                Write(path, all, Size, Size * Layers, q);
            }
            AssetDatabase.Refresh(); Configure();
            return $"layer {layer} <- {src.id}";
        }

        /// append (or replace) the pack's layers 12..14 of the plaster strips: Plaster_H, Plaster_J (grey, the house's tint) and
        /// Brick_E (dressed stone, scaled to the stone layer's limestone mean); the normal strip's blue = roughness
        public static string PackLayers()
        {
            const string T = PaketZgrade.SRC + "Textures/";
            var src = new[] { ("Plaster_H", (Color?)null, .86f, .8f), ("Plaster_J", (Color?)null, .86f, .8f), ("Brick_E", (Color?)Hex("C6BEAB"), 0f, 1f) };
            var A = new Color32[Size * Size * PlasterLayers]; var N = new Color32[Size * Size * PlasterLayers];
            foreach (var (path, dst) in new[] { (PlasterAlbedo, A), (PlasterNormal, N) })
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(File.ReadAllBytes(path));
                var all = t.GetPixels32(); int layersIn = t.height / Size; UnityEngine.Object.DestroyImmediate(t);
                if (layersIn < 12) throw new InvalidOperationException(path + ": " + layersIn + " layers");
                // the first layer is the TOP row: keep layers 0..11 at the top of the taller strip
                int keep = 12;
                Array.Copy(all, (layersIn - keep) * Size * Size, dst, (PlasterLayers - keep) * Size * Size, keep * Size * Size);
            }
            for (int k = 0; k < src.Length; k++)
            {
                var (id, mean, grey, contrast) = src[k];
                var a = PaketZgrade.Read(T + id + "/" + id + "_BaseMap.png", Size, new Color32(200, 200, 200, 255));
                var n = PaketZgrade.Read(T + id + "/" + id + "_Normal.png", Size, new Color32(128, 128, 255, 255));
                var m = PaketZgrade.Read(T + id + "/" + id + "_MaskMap.png", Size, new Color32(0, 255, 0, 64));
                Normalise(a, mean, grey, contrast);
                for (int i = 0; i < n.Length; i++) n[i] = new Color32(n[i].r, n[i].g, (byte)(255 - m[i].a), 255);
                int row = PlasterLayers - 1 - (12 + k);
                Array.Copy(a, 0, A, row * Size * Size, Size * Size); Array.Copy(n, 0, N, row * Size * Size, Size * Size);
            }
            Write(PlasterAlbedo, A, Size, Size * PlasterLayers, 92); Write(PlasterNormal, N, Size, Size * PlasterLayers, 94);
            AssetDatabase.Refresh(); Configure();
            return "plaster strips: " + PlasterLayers + " layers (pack: Plaster_H, Plaster_J, Brick_E); " + PackRoof();
        }

        /// Paket2: the pack's barrel roof tiles (RoofTiles_A, scaled to the roof layer's terracotta) as the strip's layer 15 (its top
        /// row); a 15-layer strip (before Paket2) grows by one, a 16-layer one gets its layer 15 replaced
        public static string PackRoof()
        {
            const string T = PaketZgrade.SRC + "Textures/RoofTiles_A/RoofTiles_A";
            if (!File.Exists(T + "_BaseMap.png")) return "PackRoof: " + T + "_BaseMap.png missing (extract the pack, docs/paket2.md)";
            var a = PaketZgrade.Read(T + "_BaseMap.png", Size, new Color32(170, 90, 60, 255));
            var n = PaketZgrade.Read(T + "_Normal.png", Size, new Color32(128, 128, 255, 255));
            var m = PaketZgrade.Read(T + "_MaskMap.png", Size, new Color32(0, 255, 0, 64));
            // the pack's AO (mask g) deepens the gaps between the tiles before the levelling
            for (int i = 0; i < a.Length; i++) { float ao = Mathf.Lerp(1, m[i].g / 255f, .7f); a[i] = new Color32((byte)(a[i].r * ao), (byte)(a[i].g * ao), (byte)(a[i].b * ao), 255); }
            Normalise(a, Hex("A9573F"), 0, 1);
            for (int i = 0; i < n.Length; i++) n[i] = new Color32(n[i].r, n[i].g, (byte)(255 - m[i].a), 255);
            var log = new System.Text.StringBuilder();
            foreach (var (path, layer) in new[] { (PlasterAlbedo, a), (PlasterNormal, n) })
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(File.ReadAllBytes(path));
                var all = t.GetPixels32(); int layersIn = t.height / Size; UnityEngine.Object.DestroyImmediate(t);
                if (layersIn != PlasterLayers - 1 && layersIn != PlasterLayers) throw new InvalidOperationException(path + ": " + layersIn + " layers");
                var dst = new Color32[Size * Size * PlasterLayers];
                // GetPixels32 starts at the BOTTOM row = the last layer; the top row (end of the array) is layer 0
                int keep = Math.Min(layersIn, PlasterLayers - 1);
                Array.Copy(all, (layersIn - keep) * Size * Size, dst, (PlasterLayers - keep) * Size * Size, keep * Size * Size);
                Array.Copy(layer, 0, dst, 0, Size * Size);
                Write(path, dst, Size, Size * PlasterLayers, path == PlasterAlbedo ? 92 : 94);
                log.Append(Path.GetFileName(path)).Append(' ').Append(layersIn).Append("->").Append(PlasterLayers).Append("; ");
            }
            AssetDatabase.Refresh(); Configure();
            return "PackRoof: " + log;
        }

        /// grime atlas from srcDir/Leaking012A|016A|017A_1K-JPG_Color.jpg (white = clean): inverted, levelled, 512²
        static void Grime(string srcDir)
        {
            const int G = 512; var px = new Color32[G * G];
            string[] ids = { "Leaking012A", "Leaking016A", "Leaking017A" };
            for (int ch = 0; ch < 3; ch++)
            {
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                t.LoadImage(File.ReadAllBytes(Path.Combine(srcDir, ids[ch] + "_1K-JPG_Color.jpg")));
                var src = t.GetPixels32(); int w = t.width; UnityEngine.Object.DestroyImmediate(t);
                int k = w / G; float max = 0; var v = new float[G * G];
                for (int y = 0; y < G; y++) for (int x = 0; x < G; x++)
                {
                    float sum = 0; for (int j = 0; j < k; j++) for (int i = 0; i < k; i++) { var c = src[(y * k + j) * w + x * k + i]; sum += 1 - (c.r + c.g + c.b) / 765f; }
                    v[y * G + x] = sum / (k * k); max = Mathf.Max(max, v[y * G + x]);
                }
                for (int i = 0; i < v.Length; i++) { byte b = B(Mathf.Pow(v[i] / Mathf.Max(.01f, max), .7f)); if (ch == 0) px[i].r = b; else if (ch == 1) px[i].g = b; else px[i].b = b; px[i].a = 255; }
            }
            var o = new Texture2D(G, G, TextureFormat.RGBA32, false); o.SetPixels32(px); o.Apply();
            File.WriteAllBytes(GrimeAtlas, o.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(o);
        }

        static Color32[] Load(string file)
        {
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!t.LoadImage(File.ReadAllBytes(file))) throw new InvalidOperationException("cannot read " + file);
            if (t.width != Size || t.height != Size) throw new InvalidOperationException(file + " is not 1024 x 1024");
            var p = t.GetPixels32(); UnityEngine.Object.DestroyImmediate(t); return p;
        }

        static void Normalise(Color32[] p, Color? mean, float grey, float contrast)
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
                    // grey, the detail kept at `contrast`: the tint is the building's colour
                    double lum = (.3 * c.r / 255.0 / r + .59 * c.g / 255.0 / g + .11 * c.b / 255.0 / b);
                    double v = grey * (1 + (lum - 1) * contrast);
                    p[i] = new Color32(B(v), B(v), B(v), 255);
                }
            }
        }
        static byte B(double v) => (byte)Mathf.Clamp(Mathf.RoundToInt((float)v * 255), 0, 255);

        // (unused since painted_metal_shutter, kept for reference) shutter louvres: 0.55 m repeat, 9 slats with dark gaps and a lit upper edge (both leaves, any colour)
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
                    n[y * Size + x] = new Color32(128, B(ny * .5 + .5), B(.6), 255); // b: roughness (painted wood)
                }
            return (a, n);
        }
        static (Color32[], Color32[]) PlainLayer()
        {
            var a = new Color32[Size * Size]; var n = new Color32[Size * Size]; var rnd = new System.Random(9);
            for (int i = 0; i < a.Length; i++) { byte v = B(.9 + .03 * rnd.NextDouble()); a[i] = new Color32(v, v, v, 255); n[i] = new Color32(128, 128, B(.5), 255); }
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
            if (File.Exists(PlasterAlbedo)) { Strip(PlasterAlbedo, true, PlasterLayers); Strip(PlasterNormal, false, PlasterLayers); }
            if (AssetImporter.GetAtPath(Micro) is TextureImporter mi)
            { mi.sRGBTexture = false; mi.mipmapEnabled = true; mi.wrapMode = TextureWrapMode.Repeat; mi.maxTextureSize = 512; mi.anisoLevel = 4; mi.textureCompression = TextureImporterCompression.Compressed; mi.SaveAndReimport(); }
            var ri = AssetImporter.GetAtPath(RoomAtlas) as TextureImporter;
            if (ri != null) { ri.sRGBTexture = true; ri.mipmapEnabled = true; ri.wrapMode = TextureWrapMode.Clamp; ri.maxTextureSize = 2048; ri.SaveAndReimport(); }
            var gi = AssetImporter.GetAtPath(GrimeAtlas) as TextureImporter;
            if (gi != null) { gi.sRGBTexture = false; gi.mipmapEnabled = true; gi.wrapMode = TextureWrapMode.Repeat; gi.maxTextureSize = 512; gi.textureCompression = TextureImporterCompression.Compressed; gi.SaveAndReimport(); }
        }
        static void Strip(string path, bool srgb, int layers = Layers)
        {
            var im = AssetImporter.GetAtPath(path) as TextureImporter;
            if (im == null) throw new InvalidOperationException("missing " + path);
            var st = new TextureImporterSettings(); im.ReadTextureSettings(st);
            st.textureShape = TextureImporterShape.Texture2DArray; st.flipbookRows = layers; st.flipbookColumns = 1;
            st.sRGBTexture = srgb; st.mipmapEnabled = true; st.wrapMode = TextureWrapMode.Repeat; st.filterMode = FilterMode.Trilinear; st.aniso = 4;
            // high-quality compression (BC7 on desktop): the normal xy + roughness strips blocked visibly in BC1; ~+27 MB GPU in all
            im.SetTextureSettings(st); im.maxTextureSize = 16384; im.textureCompression = TextureImporterCompression.CompressedHQ;
            im.SaveAndReimport();
        }

        public static Material Facade() => Mat("Assets/Generated/Materials/BuildingFacade.mat", "Magistrala/Facade", m =>
        {
            m.SetTexture("_Albedo", AssetDatabase.LoadAssetAtPath<Texture2DArray>(AlbedoStrip));
            m.SetTexture("_Normal", AssetDatabase.LoadAssetAtPath<Texture2DArray>(NormalStrip));
            m.SetTexture("_Grime", AssetDatabase.LoadAssetAtPath<Texture2D>(GrimeAtlas));
            var pa = AssetDatabase.LoadAssetAtPath<Texture2DArray>(PlasterAlbedo);
            if (!pa && File.Exists(PlasterAlbedo)) { Configure(); pa = AssetDatabase.LoadAssetAtPath<Texture2DArray>(PlasterAlbedo); }
            m.SetTexture("_Plaster", pa); m.SetTexture("_PlasterN", AssetDatabase.LoadAssetAtPath<Texture2DArray>(PlasterNormal));
            m.SetTexture("_Micro", AssetDatabase.LoadAssetAtPath<Texture2D>(Micro));
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
