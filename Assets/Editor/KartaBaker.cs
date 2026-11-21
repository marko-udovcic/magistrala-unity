// Menu Magistrala/Karta: ispeci — the full-screen map's data (KartaData, WorldMap.cs), from our own data only:
//   ground    two textures (the two styles, Pal: flat / dark) over game x −10752 … 1536, z −8192 … −3072 (12.3 × 5.1 km) at 2 m a
//             pixel (6144 × 2560 each, DXT1 with mips ≈ 10.5 MB each in the build): the sea from the baked ground heights
//             (Tiles.Height < −0.05, as GameDataBuilder.BakeSea), antialiased on the height gradient; land coloured by the tiles'
//             ground classes (Tiles.Surface: garrigue, olives / vineyards, rock, paving) and the vegetation mask
//             (Assets/Editor/vegetacija-maska.png: woody clumps, groves, built-up land); flat: solid fills and a sand strip along
//             the shore; dark: lighter (teal) water near the shore, higher ground warmer, hillshade from the heights on a 4 m grid
//             (the junction models' asphalt is left to the live roads)
//   buildings the town's footprints as one flat mesh (near-white vertex colours, tinted per style by WorldMap): the tiles' footprints with the orthophoto shift
//             (OrthoRoofs), the houses only the orthophoto shows, minus those on / under a model building (as BuildingsFromTiles),
//             plus the package's roadside buildings and pump canopies (model-buildings.json)
//   places    each place of GameWorld.placeNames with ≥ 40 buildings: its name at the mean of its buildings' centres
// Output: Assets/Generated/Map/Resources/Karta.asset (+ Assets/Generated/Map/karta-podloga.png, karta-podloga-tamna.png, KartaZgrade.asset); not in git
// (Assets/Generated is ignored) — the map is baked here, or before a player build when missing (GameDataBuilder's preprocess).
using System;
using System.Collections.Generic;
using System.IO;
using Magistrala.Game;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class KartaBaker
    {
        const string Dir = "Assets/Generated/Map", ResDir = Dir + "/Resources";
        public const string AssetPath = ResDir + "/" + KartaData.ResourcePath + ".asset";
        const string PngPath = Dir + "/karta-podloga.png", PngDarkPath = Dir + "/karta-podloga-tamna.png", MeshPath = Dir + "/KartaZgrade.asset";
        const string GamePath = "Assets/Generated/Game/Resources/" + GameWorld.ResourcePath + ".asset";
        // the map's extent (game frame) and resolution
        const double GX0 = -10752, GX1 = 1536, GZ0 = -8192, GZ1 = -3072;
        const float MPP = 2, HC = 4; // ground pixel, height / class grid cell (m)

        /// two styles (owner 2026-11-21), both baked; WorldMap switches with Tab (default WorldMap.DefaultDark):
        ///   flat  a printed-city-map look, modern: solid fills, no hillshade — medium blue sea, a sand strip along the shore, two
        ///         greens (woods / maquis clumps; fields, groves, garrigue), light grey land, white buildings, bold dark roads
        ///   dark  calm and dark, the sea clearly blue (teal near the shore), greenery clearly green, dry ground a warm dark tan,
        ///         soft hillshade; every value ≤ 0.68 so the ×gain storage below does not clip
        sealed class Pal { public Color Land, Urban, Scrub, Wood, Grove, Rock, HighLand, Ridge, SeaShallow, SeaDeep, Shore; public bool relief; public float gain; }
        static readonly Pal Flat = new Pal
        {
            Land = Hex(0xE3E1DA), Urban = Hex(0xDAD8D2), Scrub = Hex(0xC8DEA6), Wood = Hex(0x93C47D), Grove = Hex(0xB9D894), Rock = Hex(0xD6D3CB),
            HighLand = Hex(0xE3E1DA), Ridge = Hex(0xE3E1DA), SeaShallow = Hex(0x5F9FD8), SeaDeep = Hex(0x5F9FD8), Shore = Hex(0xEDDB9C), relief = false, gain = 1,
        };
        static readonly Pal DarkP = new Pal
        {
            Land = Hex(0x3E3628), Urban = Hex(0x3A3733), Scrub = Hex(0x3B3F2A), Wood = Hex(0x1E4D2A), Grove = Hex(0x3E5C33), Rock = Hex(0x45413A),
            HighLand = Hex(0x463D2C), Ridge = Hex(0x4C4434), SeaShallow = Hex(0x26808F), SeaDeep = Hex(0x1A4878), Shore = Hex(0x3E3628), relief = true, gain = 8,
        };
        static Color Hex(int v) => new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1);

        [MenuItem("Magistrala/Karta: ispeci")]
        public static void BakeMenu()
        {
            var w = AssetDatabase.LoadAssetAtPath<GameWorld>(GamePath);
            if (!w) { Debug.LogError("[karta] no " + GamePath + " — run Magistrala/Igra: podaci first"); return; }
            Bake(w);
        }

        public static bool Missing => !File.Exists(AssetPath);

        public static void Bake(GameWorld world)
        {
            var t0 = DateTime.Now;
            Directory.CreateDirectory(ResDir);
            Vector3 a = W.U(GX0, 0, GZ1), b = W.U(GX1, 0, GZ0); // south-west, north-east corners (Unity)
            Vector2 origin = new Vector2(a.x, a.z), size = new Vector2(b.x - a.x, b.z - a.z);
            int TW = Mathf.RoundToInt(size.x / MPP), TH = Mathf.RoundToInt(size.y / MPP);

            // ---- the buildings first (their centres also place the names)
            var foot = Footprints(out int nModelSkip);
            var mesh = BuildMesh(foot, out int nTris);

            // ---- the 4 m grids: height, land colours per style (before hillshade), shade
            int GW = Mathf.CeilToInt(size.x / HC) + 1, GH = Mathf.CeilToInt(size.y / HC) + 1;
            var hg = new float[GW * GH]; var cf = new Color[GW * GH]; var cd = new Color[GW * GH];
            bool veg = File.Exists(VegetationMask.PATH);
            for (int j = 0; j < GH; j++)
                for (int i = 0; i < GW; i++)
                {
                    var g = W.G(new Vector3(origin.x + i * HC, 0, origin.y + j * HC));
                    float h = (float)Tiles.Height(g.x, g.y); hg[j * GW + i] = h;
                    cf[j * GW + i] = LandColour(g.x, g.y, h, veg, Flat);
                    cd[j * GW + i] = LandColour(g.x, g.y, h, veg, DarkP);
                }
            // light blur of the land colours (the 8 m class grid's steps)
            cf = Blur(cf, GW, GH); cd = Blur(cd, GW, GH);
            // near the shore: the land mask blurred (~50 m), for the shallow-water tint (not the depth: the data's open-sea fill
            // of −6 m would show its tile edges); the sea mask blurred (~12 m) for the flat style's sand strip
            var near = new float[GW * GH]; var shoreG = new float[GW * GH];
            for (int k = 0; k < near.Length; k++) { near[k] = hg[k] < -0.05f ? 0 : 1; shoreG[k] = 1 - near[k]; }
            BoxBlur(near, GW, GH, 12); BoxBlur(near, GW, GH, 12);
            BoxBlur(shoreG, GW, GH, 3);
            var shade = new float[GW * GH];
            Vector3 L = new Vector3(-0.4f, 0.82f, 0.4f).normalized; // from the north-west, ~55° up (x east, y up, z north)
            for (int j = 0; j < GH; j++)
                for (int i = 0; i < GW; i++)
                {
                    float hx = hg[j * GW + Mathf.Min(GW - 1, i + 1)] - hg[j * GW + Mathf.Max(0, i - 1)];
                    float hz = hg[Mathf.Min(GH - 1, j + 1) * GW + i] - hg[Mathf.Max(0, j - 1) * GW + i];
                    const float EX = 1.6f; // relief exaggeration
                    var n = new Vector3(-hx * EX / (2 * HC), 1, -hz * EX / (2 * HC)).normalized;
                    shade[j * GW + i] = Vector3.Dot(n, L) / L.y;
                }
            // softened (the outer tiles' heights come in coarse steps that read as blocks)
            BoxBlur(shade, GW, GH, 3); BoxBlur(shade, GW, GH, 3);

            // ---- the 2 m pictures
            var pf = new Color32[TW * TH]; var pd = new Color32[TW * TH];
            float thr = -0.05f;
            for (int j = 0; j < TH; j++)
            {
                float gy = ((j + .5f) * MPP) / HC; int iy = Mathf.Min(GH - 2, (int)gy); float ay = gy - iy;
                for (int i = 0; i < TW; i++)
                {
                    float gx = ((i + .5f) * MPP) / HC; int ix = Mathf.Min(GW - 2, (int)gx); float ax = gx - ix;
                    int k00 = iy * GW + ix, k10 = k00 + 1, k01 = k00 + GW, k11 = k01 + 1;
                    float h = Bi(hg[k00], hg[k10], hg[k01], hg[k11], ax, ay);
                    float dhx = Mathf.Lerp(hg[k10] - hg[k00], hg[k11] - hg[k01], ay) / HC, dhz = Mathf.Lerp(hg[k01] - hg[k00], hg[k11] - hg[k10], ax) / HC;
                    float grad = Mathf.Sqrt(dhx * dhx + dhz * dhz);
                    // sea cover: the threshold crossing antialiased over one pixel
                    float sea = Mathf.Clamp01((thr - h) / Mathf.Max(grad * MPP, 0.02f) + .5f);
                    float nr = Bi(near[k00], near[k10], near[k01], near[k11], ax, ay);
                    float sh = Bi(shoreG[k00], shoreG[k10], shoreG[k01], shoreG[k11], ax, ay);
                    float s = Bi(shade[k00], shade[k10], shade[k01], shade[k11], ax, ay);
                    pf[j * TW + i] = Pixel(Flat, Bi(cf[k00], cf[k10], cf[k01], cf[k11], ax, ay), sea, nr, sh, s, h);
                    pd[j * TW + i] = Pixel(DarkP, Bi(cd[k00], cd[k10], cd[k01], cd[k11], ax, ay), sea, nr, sh, s, h);
                }
            }
            var ground = SavePng(PngPath, pf, TW, TH);
            var groundDark = SavePng(PngDarkPath, pd, TW, TH);

            // ---- the mesh asset
            var oldMesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (oldMesh) { oldMesh.Clear(); EditorUtility.CopySerialized(mesh, oldMesh); UnityEngine.Object.DestroyImmediate(mesh); mesh = oldMesh; EditorUtility.SetDirty(mesh); }
            else AssetDatabase.CreateAsset(mesh, MeshPath);

            // ---- places
            var sum = new Dictionary<string, (double x, double z, int n)>();
            foreach (var f in foot)
            {
                string p = world.PlaceAt(new Vector3(f.cx, 0, f.cz)); if (string.IsNullOrEmpty(p)) continue;
                sum.TryGetValue(p, out var s); sum[p] = (s.x + f.cx, s.z + f.cz, s.n + 1);
            }
            var places = new List<(string name, Vector2 p, int n)>();
            foreach (var kv in sum) if (kv.Value.n >= 40) places.Add((kv.Key, new Vector2((float)(kv.Value.x / kv.Value.n), (float)(kv.Value.z / kv.Value.n)), kv.Value.n));
            places.Sort((x, y) => y.n.CompareTo(x.n));

            var data = AssetDatabase.LoadAssetAtPath<KartaData>(AssetPath);
            bool fresh = !data; if (fresh) data = ScriptableObject.CreateInstance<KartaData>();
            data.ground = ground; data.groundDark = groundDark; data.gain = Flat.gain; data.gainDark = DarkP.gain; data.buildings = mesh; data.origin = origin; data.size = size; data.metresPerPixel = MPP;
            data.placeNames = places.ConvertAll(p => p.name).ToArray(); data.placePos = places.ConvertAll(p => p.p).ToArray(); data.placeCount = places.ConvertAll(p => p.n).ToArray();
            data.buildingCount = foot.Count;
            data.source = $"{TW}×{TH} px of {MPP} m, {foot.Count} buildings ({nTris} triangles, {nModelSkip} under models skipped), {places.Count} places, {DateTime.Now:yyyy-MM-dd HH:mm}";
            if (fresh) AssetDatabase.CreateAsset(data, AssetPath); else EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            Debug.Log($"[karta] {AssetPath}: {data.source}; places {string.Join(", ", places.ConvertAll(p => p.name + " " + p.n))}; {(DateTime.Now - t0).TotalSeconds:0.0} s");
        }

        /// one ground pixel of a style: land (relief), the flat style's sand strip, the sea; stored for the map's render texture,
        /// which IMGUI shows gamma-encoded twice (WorldMap.Lc): the colour linearised twice, ×gain so a dark palette keeps its
        /// tones in 8 bits (the map draws it tinted 1 / gain)
        static Color32 Pixel(Pal p, Color land, float sea, float nearLand, float nearSea, float shade, float h)
        {
            if (p.relief) land *= Mathf.Clamp(Mathf.Lerp(1, shade, .5f), .8f, 1.07f);
            else if (nearSea > 0.06f && h < 4) land = Color.Lerp(land, p.Shore, Mathf.Clamp01((nearSea - 0.06f) / 0.05f));
            Color c = land;
            if (sea > 0) c = Color.Lerp(land, Color.Lerp(p.SeaDeep, p.SeaShallow, Mathf.Clamp01(nearLand * 2.2f)), sea);
            c = (c.linear.linear * p.gain).gamma; c.a = 1;
            return c;
        }

        static Texture2D SavePng(string path, Color32[] px, int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(px); tex.Apply(false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            if (AssetImporter.GetAtPath(path) is TextureImporter ti)
            {
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.None;
                ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4;
                ti.npotScale = TextureImporterNPOTScale.None; ti.maxTextureSize = 8192; ti.isReadable = false;
                ti.textureCompression = TextureImporterCompression.Compressed; ti.compressionQuality = 100;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static float Bi(float a, float b, float c, float d, float x, float y) => (a * (1 - x) + b * x) * (1 - y) + (c * (1 - x) + d * x) * y;
        static Color Bi(Color a, Color b, Color c, Color d, float x, float y) => (a * (1 - x) + b * x) * (1 - y) + (c * (1 - x) + d * x) * y;

        /// separable box blur in place (radius r cells), edges clamped
        static void BoxBlur(float[] a, int w, int h, int r)
        {
            var tmp = new float[Math.Max(w, h)];
            for (int j = 0; j < h; j++)
            {
                for (int i = 0; i < w; i++) { float s = 0; for (int q = -r; q <= r; q++) s += a[j * w + Mathf.Clamp(i + q, 0, w - 1)]; tmp[i] = s / (2 * r + 1); }
                for (int i = 0; i < w; i++) a[j * w + i] = tmp[i];
            }
            for (int i = 0; i < w; i++)
            {
                for (int j = 0; j < h; j++) { float s = 0; for (int q = -r; q <= r; q++) s += a[Mathf.Clamp(j + q, 0, h - 1) * w + i]; tmp[j] = s / (2 * r + 1); }
                for (int j = 0; j < h; j++) a[j * w + i] = tmp[j];
            }
        }

        static Color[] Blur(Color[] c, int w, int h)
        {
            var o = new Color[c.Length];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    Color s = c[j * w + i] * 4; float n = 4;
                    if (i > 0) { s += c[j * w + i - 1]; n++; }
                    if (i < w - 1) { s += c[j * w + i + 1]; n++; }
                    if (j > 0) { s += c[(j - 1) * w + i]; n++; }
                    if (j < h - 1) { s += c[(j + 1) * w + i]; n++; }
                    o[j * w + i] = s / n;
                }
            return o;
        }

        /// the land colour at a game point (one style): ground class, vegetation mask, height (dark style only)
        static Color LandColour(double gx, double gz, float h, bool veg, Pal p)
        {
            Color c;
            switch (Tiles.Surface(gx, gz))
            {
                case 1: c = p.Rock; break;                                     // rock
                case 4: c = Color.Lerp(p.Land, p.Urban, .5f); break;           // residential
                case 5: case 7: c = p.Urban; break;                            // paved
                case 8: case 9: c = p.Grove; break;                            // olive rows, vineyards
                case 10: c = Color.Lerp(p.Land, p.Scrub, .8f); break;          // garrigue
                case 0: case 11: c = p.relief ? Color.Lerp(p.Land, p.Scrub, .45f) : p.Land; break; // scrub, gardens
                default: c = p.relief ? Color.Lerp(p.Land, p.Scrub, .3f) : p.Land; break;
            }
            if (veg)
            {
                int v = VegetationMask.At(gx, gz);
                // the mask covers the WorldBuilder core only: faded out over its last 400 m (no rectangle on the map)
                float fe = Mathf.Clamp01((float)Math.Min(Math.Min(gx + 10240, -gx), Math.Min(gz + 7168, -4096 - gz)) / 400f);
                Color c0 = c;
                if ((v & VegetationMask.BUILT) != 0) c = Color.Lerp(c, p.Urban, .75f);
                if ((v & VegetationMask.ORCHARD) != 0) c = Color.Lerp(c, p.Grove, .8f);
                int cl = v & VegetationMask.CLUMP;
                if (cl != 0) c = p.relief ? Color.Lerp(c, p.Wood, .55f + .45f * cl / 15f) : p.Wood;
                c = Color.Lerp(c0, c, fe);
            }
            // higher ground: warmer, darker (the Kozjak slope; dark style)
            if (p.relief && h > 60) c = Color.Lerp(c, p.HighLand, Mathf.Clamp01((h - 60) / 260f) * .55f);
            if (p.relief && h > 320) c = Color.Lerp(c, p.Ridge, Mathf.Clamp01((h - 320) / 300f) * .6f);
            return c;
        }

        // ------------------------------------------------------------------ footprints
        sealed class Foot { public List<Vector2> P; public float cx, cz; public Color col; }

        static List<Foot> Footprints(out int nModelSkip)
        {
            var models = WorldData.Models();
            var outL = new List<Foot>(); nModelSkip = 0;
            var rects = new List<double[]>();
            var pk = new List<(double x, double z, double a, double w, double l, bool canopy)>();
            var mbPath = W.REGION + "model-buildings.json";
            if (File.Exists(mbPath))
                foreach (var o in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(mbPath)))["objects"]))
                {
                    var d = MiniJson.O(o); if (!d.ContainsKey("w") || !d.ContainsKey("l")) continue;
                    var r = new[] { MiniJson.D(d["x"]), MiniJson.D(d["z"]), MiniJson.D(MiniJson.G(d, "a") ?? 0.0), MiniJson.D(d["w"]), MiniJson.D(d["l"]) };
                    rects.Add(r);
                    string k = MiniJson.G(d, "k") as string ?? "bld";
                    if (k != "sign") pk.Add((r[0], r[1], r[2], r[3], r[4], k == "canopy"));
                }
            var feed = new List<List<BuildingsFromTiles.Vector2d>>();
            for (int tz = WorldBuilder.TZ0; tz <= WorldBuilder.TZ1; tz++)
                for (int tx = WorldBuilder.TX0; tx <= WorldBuilder.TX1; tx++)
                {
                    var tile = Tiles.Get(tx, tz); if (tile?.buildingsJson == null) continue;
                    var list = MiniJson.L(MiniJson.Parse(tile.buildingsJson));
                    for (int bi = 0; bi < list.Count; bi++)
                    {
                        var bo = MiniJson.O(list[bi]);
                        var pts = MiniJson.L(bo["pts"]); if (pts == null || pts.Count < 3) continue;
                        var P = new List<BuildingsFromTiles.Vector2d>();
                        foreach (var p in pts) { var q = MiniJson.L(p); P.Add(new BuildingsFromTiles.Vector2d(MiniJson.D(q[0]), MiniJson.D(q[1]))); }
                        if (P[0].x == P[P.Count - 1].x && P[0].z == P[P.Count - 1].z) P.RemoveAt(P.Count - 1);
                        if (P.Count < 3) continue;
                        double ox = 0, oz = 0; foreach (var p in P) { ox += p.x; oz += p.z; } ox /= P.Count; oz /= P.Count;
                        var rec = OrthoRoofs.Get(tx, tz, bi, ox, oz);
                        if (rec != null && (rec.shx != 0 || rec.shz != 0)) for (int i = 0; i < P.Count; i++) P[i] = new BuildingsFromTiles.Vector2d(P[i].x + rec.shx, P[i].z + rec.shz);
                        feed.Add(P);
                    }
                }
            foreach (var m in OrthoRoofs.Missing()) feed.Add(m.P);
            foreach (var P in feed)
            {
                double cx = 0, cz = 0; foreach (var p in P) { cx += p.x; cz += p.z; } cx /= P.Count; cz /= P.Count;
                if (WorldData.FootDist(models, cx, cz) < 3 || BuildingsFromTiles.Covered(rects, P, cx, cz)) { nModelSkip++; continue; }
                bool onFoot = false; foreach (var p in P) if (WorldData.FootDist(models, p.x, p.z) < 1.5) { onFoot = true; break; }
                if (onFoot) { nModelSkip++; continue; }
                var f = new Foot { P = new List<Vector2>(P.Count) };
                foreach (var p in P) { var u = W.U(p.x, 0, p.z); f.P.Add(new Vector2(u.x, u.z)); }
                var uc = W.U(cx, 0, cz); f.cx = uc.x; f.cz = uc.z;
                float v = (float)(Math.Abs(Math.Sin(cx * 12.9898 + cz * 78.233) * 43758.5453) % 1);
                f.col = Color.white * (0.955f + 0.045f * v); f.col.a = 1;
                outL.Add(f);
            }
            foreach (var r in pk)
            {
                double tx = Math.Cos(r.a), tz = Math.Sin(r.a), nx = -tz, nz = tx;
                var f = new Foot { P = new List<Vector2>(4) };
                foreach (var (sw, sl) in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                {
                    double gx = r.x + nx * sw * r.w / 2 + tx * sl * r.l / 2, gz = r.z + nz * sw * r.w / 2 + tz * sl * r.l / 2;
                    var u = W.U(gx, 0, gz); f.P.Add(new Vector2(u.x, u.z));
                }
                var uc = W.U(r.x, 0, r.z); f.cx = uc.x; f.cz = uc.z; f.col = r.canopy ? new Color(.86f, .9f, .96f, 1) : Color.white;
                outL.Add(f);
            }
            return outL;
        }

        static Mesh BuildMesh(List<Foot> foot, out int nTris)
        {
            var v = new List<Vector3>(); var c = new List<Color>(); var idx = new List<int>();
            var tri = new List<int>();
            foreach (var f in foot)
            {
                var P = f.P;
                // counter-clockwise (x east, z north)
                double area = 0; for (int i = 0; i < P.Count; i++) { var p = P[i]; var q = P[(i + 1) % P.Count]; area += p.x * q.y - q.x * p.y; }
                if (Math.Abs(area) < 1) continue;
                if (area < 0) P.Reverse();
                tri.Clear(); Ear(P, tri);
                int b0 = v.Count;
                // vertex colours go in linearised twice (drawn unconverted, then shown gamma-encoded twice: WorldMap.Lc); float
                // colours, the dark palette's values would not survive 8 bits
                Color col = f.col.linear.linear; col.a = 1;
                foreach (var p in P) { v.Add(new Vector3(p.x, p.y, 0)); c.Add(col); }
                foreach (var t in tri) idx.Add(b0 + t);
            }
            var m = new Mesh { name = "karta zgrade", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.SetVertices(v); m.SetColors(c); m.SetTriangles(idx, 0, true);
            nTris = idx.Count / 3;
            return m;
        }

        /// ear clipping of a counter-clockwise simple polygon (a fan where it gets stuck)
        static void Ear(List<Vector2> P, List<int> outT)
        {
            var ix = new List<int>(); for (int i = 0; i < P.Count; i++) ix.Add(i);
            int guard = 0;
            while (ix.Count > 3 && guard++ < 4 * P.Count + 20)
            {
                bool cut = false;
                for (int k = 0; k < ix.Count; k++)
                {
                    int i0 = ix[(k + ix.Count - 1) % ix.Count], i1 = ix[k], i2 = ix[(k + 1) % ix.Count];
                    Vector2 a = P[i0], b = P[i1], d = P[i2];
                    float cr = (b.x - a.x) * (d.y - a.y) - (b.y - a.y) * (d.x - a.x);
                    if (cr <= 1e-6f) continue;
                    bool inside = false;
                    for (int q = 0; q < ix.Count && !inside; q++)
                    {
                        int iq = ix[q]; if (iq == i0 || iq == i1 || iq == i2) continue;
                        inside = InTri(P[iq], a, b, d);
                    }
                    if (inside) continue;
                    outT.Add(i0); outT.Add(i1); outT.Add(i2); ix.RemoveAt(k); cut = true; break;
                }
                if (!cut) break;
            }
            for (int k = 1; k + 1 < ix.Count; k++) { outT.Add(ix[0]); outT.Add(ix[k]); outT.Add(ix[k + 1]); }
        }
        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (b.x - a.x) * (p.y - a.y) - (b.y - a.y) * (p.x - a.x);
            float d2 = (c.x - b.x) * (p.y - b.y) - (c.y - b.y) * (p.x - b.x);
            float d3 = (a.x - c.x) * (p.y - c.y) - (a.y - c.y) * (p.x - c.x);
            return d1 >= 0 && d2 >= 0 && d3 >= 0;
        }
    }
}
