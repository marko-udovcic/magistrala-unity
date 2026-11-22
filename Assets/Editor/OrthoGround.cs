// The DGU orthophoto (DOF LiDAR 2022./23., 25 cm; registered WMS) as the ground colour of the terrain (probe: Kaštel Lukšić, docs/dof-tlo.md).
//
//   1. Tools/dof/dof_mosaic.py TX,TZ  — the photo on the game grid, 2048² (0.5 m/px) per terrain tile, in the gitignored
//      BalkanDriver cache (tools/world/dgu/cache/dof/mosaic-lidar/t_TX_TZ.png; the WMS responses stay in that cache only).
//   2. Magistrala/Grafika: ortofoto tlo (Build()) — per tile:
//      mask   building footprints (tiles + the package's roadside buildings) dilated 3 m, our road surfaces (model asphalt /
//             islands, graph streets' cover) dilated 2 m (the package D8 lies 1.5–3 m north of the photo's, docs/ceste-ortofoto-mjerenje.md): roofs and cars are not ground colour
//      fill   the masked pixels from the ground around them (pull-push inpainting), then a light 3×3 blur (JPEG blocks, cars)
//      ratio  photo colour (linear, matched per channel so its region average = our layers' average) ÷ the mean colour of our splat there (Σ weight × the layer texture's mean): the shader
//             multiplies the tiling layers by it, so the layers keep their own detail up close and the photo gives the colour
//             (paths, dry patches, gardens, car parks, tree shadows); 1 = unchanged at sea, under water and at a tile border
//             with a non-photo neighbour (faded over FADE m)
//      out    Assets/Generated/Terrain/Macro/macro_TX_TZ.png (linear, ratio / 2, BC1 + mips, 2.7 MB in memory per tile) and a
//             material on Assets/Shaders/TerrainMacro.shader for the tile (its basemap gen applies the same tint far away)
//   The player's -dof 0 switch (GpuSwitches) puts the stock material back for A/B.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class OrthoGround
    {
        /// all of Kaštela: every core tile (WorldBuilder TX0…TX1 × TZ0…TZ1, 30 × 1 km², docs/dof-tlo.md)
        public static readonly (int tx, int tz)[] TILES = CoreTiles();
        static (int, int)[] CoreTiles()
        {
            var l = new List<(int, int)>();
            for (int tz = WorldBuilder.TZ0; tz <= WorldBuilder.TZ1; tz++) for (int tx = WorldBuilder.TX0; tx <= WorldBuilder.TX1; tx++) l.Add((tx, tz));
            return l.ToArray();
        }
        /// size of the macro texture in the game: 1024² = 1 m/px, BC1 + mips 0.7 MB per tile (2048² = 2.7 MB, ×30 = +85 MB of
        /// memory and build for a difference seen only from a few metres; the layers give the detail up close)
        public static int OutN = 1024;
        public static string Source = "lidar"; // Tools/dof/dof_mosaic.py --src (lidar = DOF LiDAR 2022./23., dof2526 = DOF 2025./26.)
        static string MOSAIC => W.BD + $"/tools/world/dgu/cache/dof/mosaic-{Source}/";
        const string OUT = WorldBuilder.GEN + "/Terrain/Macro";
        const int N = 2048; const double PX = 0.5;
        public static float Bright = 1f, Sat = 1f, Contrast = 1f, RMin = .45f, RMax = 2.4f;
        // the photo's own shadows (trees, houses, walls our world doesn't have there) are lifted (Deshadow); Strength = how much
        // of the photo's ratio is applied (0.7 = 70 %), RMin: the photo never darkens our ground below 45 %
        public static float Strength = .85f, ShadowHi = .85f, ShadowLift = .75f; const int SHADOW_R = 24; const double FADE = 128, BDIL = 3, RDIL = 2;
        // Mediterranean grading (Marko 22. 11.: "pustinja"): the summer photo is itself yellow-orange and our dry layers are too.
        //   HueMatch  how much of our layers' average hue the photo is pulled to (0 = the photo's own hue, 1 = the old full match
        //             that made every town the yellow of the dry-scrub layer); its brightness is still matched in full
        //   Green     vegetated pixels (excess green (2g − r − b) / (r + g + b) > 0) a little greener: g up, r down
        //   Earth     bare orange / ochre pixels (r > g > b) desaturated toward their grey
        public static float HueMatch = .3f, Green = .22f, Earth = .4f;

        static float Lin(float s) => s <= .04045f ? s / 12.92f : Mathf.Pow((s + .055f) / 1.055f, 2.4f);

        /// mean linear colour of a terrain layer's diffuse texture (its file read directly: the import is not readable)
        static Vector3 LayerMean(TerrainLayer l)
        {
            var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(l.diffuseTexture)));
            var px = t.GetPixels32(); double r = 0, g = 0, b = 0; int n = 0;
            for (int i = 0; i < px.Length; i += 7) { r += Lin(px[i].r / 255f); g += Lin(px[i].g / 255f); b += Lin(px[i].b / 255f); n++; }
            UnityEngine.Object.DestroyImmediate(t);
            return Vector3.Scale(new Vector3((float)(r / n), (float)(g / n), (float)(b / n)), new Vector3(l.diffuseRemapMax.x, l.diffuseRemapMax.y, l.diffuseRemapMax.z)); // per channel (NaturePass layers are tinted)
        }

        // ---------------------------------------------------------------- masks (Unity texture rows: row 0 = south edge)
        static double Gx(int tx, int i) => tx * 1024 + (i + .5) * PX;
        static double Gz(int tz, int r) => (tz + 1) * 1024 - (r + .5) * PX;

        static void FillPoly(bool[] m, int tx, int tz, List<Vector2> P)
        {
            // P in game (x, z); scanline over texture rows
            float zmin = float.MaxValue, zmax = float.MinValue; foreach (var p in P) { zmin = Mathf.Min(zmin, p.y); zmax = Mathf.Max(zmax, p.y); }
            int r0 = Mathf.Max(0, (int)Math.Floor(((tz + 1) * 1024 - zmax) / PX - .5)), r1 = Mathf.Min(N - 1, (int)Math.Ceiling(((tz + 1) * 1024 - zmin) / PX - .5));
            var xs = new List<double>();
            for (int r = r0; r <= r1; r++)
            {
                double z = Gz(tz, r); xs.Clear();
                for (int k = 0; k < P.Count; k++)
                {
                    var a = P[k]; var b = P[(k + 1) % P.Count];
                    if ((a.y <= z) != (b.y <= z)) xs.Add(a.x + (z - a.y) / (b.y - a.y) * (b.x - a.x));
                }
                xs.Sort();
                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int i0 = Mathf.Max(0, (int)Math.Ceiling((xs[k] - tx * 1024) / PX - .5)), i1 = Mathf.Min(N - 1, (int)Math.Floor((xs[k + 1] - tx * 1024) / PX - .5));
                    for (int i = i0; i <= i1; i++) m[r * N + i] = true;
                }
            }
        }

        /// dilate by d metres (separable max with a square kernel ≈ the footprint grown by d)
        static bool[] Dilate(bool[] m, double d)
        {
            int k = (int)Math.Round(d / PX); var a = new bool[m.Length]; var b = new bool[m.Length];
            for (int r = 0; r < N; r++) { int last = -99999; for (int i = 0; i < N; i++) { if (m[r * N + i]) last = i; a[r * N + i] = i - last <= k; } last = 99999; for (int i = N - 1; i >= 0; i--) { if (m[r * N + i]) last = i; if (last - i <= k) a[r * N + i] = true; } }
            for (int i = 0; i < N; i++) { int last = -99999; for (int r = 0; r < N; r++) { if (a[r * N + i]) last = r; b[r * N + i] = r - last <= k; } last = 99999; for (int r = N - 1; r >= 0; r--) { if (a[r * N + i]) last = r; if (last - r <= k) b[r * N + i] = true; } }
            return b;
        }

        /// for NaturePass (weeds along wall bases): the footprint mask of tile tx, tz (N², 0.5 m, row 0 = south) and its dilation
        public const int MaskN = N;
        public static bool[] BuildingMask(int tx, int tz, double dil) => Buildings(tx, tz, dil);
        public static bool[] Grow(bool[] m, double d) => Dilate(m, d);

        static bool[] Buildings(int tx, int tz, double dil = BDIL)
        {
            var m = new bool[N * N];
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    var tile = Tiles.Get(tx + dx, tz + dz); if (tile?.buildingsJson == null) continue;
                    foreach (var bo in MiniJson.L(MiniJson.Parse(tile.buildingsJson)))
                    {
                        var pts = MiniJson.L(MiniJson.O(bo)["pts"]); if (pts.Count < 3) continue;
                        var P = new List<Vector2>(); bool near = false;
                        foreach (var p in pts)
                        {
                            var q = MiniJson.L(p); var v = new Vector2((float)MiniJson.D(q[0]), (float)MiniJson.D(q[1])); P.Add(v);
                            if (v.x > tx * 1024 - 20 && v.x < tx * 1024 + 1044 && v.y > tz * 1024 - 20 && v.y < tz * 1024 + 1044) near = true;
                        }
                        if (near) FillPoly(m, tx, tz, P);
                    }
                }
            var mb = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "model-buildings.json")));
            foreach (var o in MiniJson.L(mb["objects"]))
            {
                var d = MiniJson.O(o); if (!d.ContainsKey("w") || !d.ContainsKey("l")) continue;
                double x = MiniJson.D(d["x"]), z = MiniJson.D(d["z"]), a = MiniJson.D(MiniJson.G(d, "a") ?? 0.0), w = MiniJson.D(d["w"]) / 2, l = MiniJson.D(d["l"]) / 2;
                if (x < tx * 1024 - 60 || x > tx * 1024 + 1084 || z < tz * 1024 - 60 || z > tz * 1024 + 1084) continue;
                double ca = Math.Cos(a), sa = Math.Sin(a);
                var P = new List<Vector2>();
                foreach (var (u, v) in new[] { (-w, -l), (w, -l), (w, l), (-w, l) }) P.Add(new Vector2((float)(x - u * sa + v * ca), (float)(z + u * ca + v * sa))); // BuildingsBuilder: w along n = (-sin, cos), l along t = (cos, sin)
                FillPoly(m, tx, tz, P);
            }
            return dil > 0 ? Dilate(m, dil) : m;
        }

        static bool[] Roads(int tx, int tz, List<ModelJ> models, double dil = RDIL)
        {
            var near = models.FindAll(mj => mj.x + mj.half + 14 > tx * 1024 && mj.x - mj.half - 14 < tx * 1024 + 1024 && mj.z + mj.half + 14 > tz * 1024 && mj.z - mj.half - 14 < tz * 1024 + 1024);
            var m = new bool[N * N];
            // 1 m test grid (the street cover is a 2 m grid, the model surfaces 1 m)
            for (int r = 0; r < N; r += 2)
                for (int i = 0; i < N; i += 2)
                {
                    double gx = Gx(tx, i) + .25, gz = Gz(tz, r) - .25;
                    bool on = StreetsBuilder.Covered(gx, gz) || (near.Count > 0 && WorldData.SurfDist(near, gx, gz) < .5);
                    if (on) { m[r * N + i] = m[r * N + i + 1] = m[(r + 1) * N + i] = m[(r + 1) * N + i + 1] = true; }
                }
            return dil > 0 ? Dilate(m, dil) : m;
        }

        // ---------------------------------------------------------------- pull-push fill of the masked pixels
        static void Inpaint(float[][] ch, bool[] valid, int n)
        {
            var levels = new List<(float[][] c, float[] w, int n)>();
            var w0 = new float[n * n]; for (int i = 0; i < w0.Length; i++) w0[i] = valid[i] ? 1 : 0;
            var c0 = new float[3][]; for (int k = 0; k < 3; k++) { c0[k] = new float[n * n]; for (int i = 0; i < w0.Length; i++) c0[k][i] = ch[k][i] * w0[i]; }
            levels.Add((c0, w0, n));
            while (levels[levels.Count - 1].n > 1)
            {
                var (pc, pw, pn) = levels[levels.Count - 1]; int m = pn / 2;
                var w = new float[m * m]; var c = new float[3][]; for (int k = 0; k < 3; k++) c[k] = new float[m * m];
                for (int y = 0; y < m; y++)
                    for (int x = 0; x < m; x++)
                    {
                        int a = (2 * y) * pn + 2 * x, o = y * m + x;
                        float s = pw[a] + pw[a + 1] + pw[a + pn] + pw[a + pn + 1];
                        w[o] = Mathf.Min(1, s);
                        for (int k = 0; k < 3; k++) { float v = pc[k][a] + pc[k][a + 1] + pc[k][a + pn] + pc[k][a + pn + 1]; c[k][o] = s > 0 ? v / s * w[o] : 0; }
                    }
                levels.Add((c, w, m));
            }
            // push: fill each level's missing weight from the coarser level (bilinear-ish: nearest parent)
            for (int L = levels.Count - 2; L >= 0; L--)
            {
                var (c, w, ln) = levels[L]; var (pc, pw, pn) = levels[L + 1];
                for (int y = 0; y < ln; y++)
                    for (int x = 0; x < ln; x++)
                    {
                        int o = y * ln + x; if (w[o] >= 1) continue;
                        // bilinear sample of the parent at this pixel's centre
                        float fx = Mathf.Clamp((x + .5f) / 2 - .5f, 0, pn - 1), fy = Mathf.Clamp((y + .5f) / 2 - .5f, 0, pn - 1);
                        int x0 = (int)fx, y0 = (int)fy, x1 = Mathf.Min(pn - 1, x0 + 1), y1 = Mathf.Min(pn - 1, y0 + 1); float ax = fx - x0, ay = fy - y0;
                        float rem = 1 - w[o];
                        for (int k = 0; k < 3; k++)
                        {
                            float Pv(int xx, int yy) { int q = yy * pn + xx; return pw[q] > 0 ? pc[k][q] / pw[q] : 0; }
                            float v = (Pv(x0, y0) * (1 - ax) + Pv(x1, y0) * ax) * (1 - ay) + (Pv(x0, y1) * (1 - ax) + Pv(x1, y1) * ax) * ay;
                            c[k][o] += v * rem;
                        }
                        w[o] = 1;
                    }
            }
            for (int k = 0; k < 3; k++) for (int i = 0; i < n * n; i++) if (!valid[i]) ch[k][i] = levels[0].c[k][i];
        }

        /// Box sums over (2R+1)² (clamped at the edges) via an integral image.
        static float[] BoxSum(float[] a, int R)
        {
            var I = new double[(N + 1) * (N + 1)];
            for (int y = 0; y < N; y++) { double row = 0; for (int x = 0; x < N; x++) { row += a[y * N + x]; I[(y + 1) * (N + 1) + x + 1] = I[y * (N + 1) + x + 1] + row; } }
            var o = new float[N * N];
            for (int y = 0; y < N; y++)
            {
                int y0 = Math.Max(0, y - R), y1 = Math.Min(N, y + R + 1);
                for (int x = 0; x < N; x++)
                {
                    int x0 = Math.Max(0, x - R), x1 = Math.Min(N, x + R + 1);
                    o[y * N + x] = (float)(I[y1 * (N + 1) + x1] - I[y0 * (N + 1) + x1] - I[y1 * (N + 1) + x0] + I[y0 * (N + 1) + x0]);
                }
            }
            return o;
        }

        /// Lifts the photo's shadows / dark crowns (trees and walls standing where ours are not: dark patches on empty ground)
        /// without moving any colour: a pixel darker than ShadowHi × its local (SHADOW_R px = 12 m) mean is brightened by
        /// (target / lum)^ShadowLift (≤ ×3) and its blue shadow cast pulled halfway to grey. A gain, not a replacement: no halos,
        /// the olive rows and field edges stay sharp (tested on t_-7_-6 against a lit-mean replacement, which smeared them).
        static void Deshadow(float[][] ch)
        {
            int n = N * N; var lum = new float[n];
            for (int i = 0; i < n; i++) lum[i] = .2126f * ch[0][i] + .7152f * ch[1][i] + .0722f * ch[2][i];
            var one = new float[n]; for (int i = 0; i < n; i++) one[i] = 1;
            var area = BoxSum(one, SHADOW_R); var big = BoxSum(lum, SHADOW_R);
            for (int i = 0; i < n; i++)
            {
                float tgt = big[i] / area[i] * ShadowHi; if (lum[i] >= tgt) continue;
                float g = Mathf.Min(3f, Mathf.Pow(tgt / Mathf.Max(lum[i], 1e-4f), ShadowLift));
                float k = Mathf.Clamp01((g - 1) / 1.5f) * .5f, y = 0;
                for (int c = 0; c < 3; c++) { ch[c][i] *= g; }
                y = .2126f * ch[0][i] + .7152f * ch[1][i] + .0722f * ch[2][i];
                for (int c = 0; c < 3; c++) ch[c][i] = Mathf.Lerp(ch[c][i], y, k);
            }
        }

        /// box-downsample N² channels to OutN²
        static float[][] Down(float[][] a)
        {
            int M = OutN, f = N / M; if (f <= 1) return a;
            var o = new float[a.Length][]; float inv = 1f / (f * f);
            for (int k = 0; k < a.Length; k++)
            {
                o[k] = new float[M * M];
                for (int y = 0; y < M; y++)
                    for (int x = 0; x < M; x++)
                    {
                        float v = 0; for (int dy = 0; dy < f; dy++) for (int dx = 0; dx < f; dx++) v += a[k][(y * f + dy) * N + x * f + dx];
                        o[k][y * M + x] = v * inv;
                    }
            }
            return o;
        }

        static void Blur3(float[] a)
        {
            var t = new float[a.Length];
            for (int r = 0; r < N; r++) for (int i = 0; i < N; i++) { int i0 = Math.Max(0, i - 1), i1 = Math.Min(N - 1, i + 1); t[r * N + i] = (a[r * N + i0] + a[r * N + i] + a[r * N + i1]) / 3; }
            for (int r = 0; r < N; r++) { int r0 = Math.Max(0, r - 1), r1 = Math.Min(N - 1, r + 1); for (int i = 0; i < N; i++) a[r * N + i] = (t[r0 * N + i] + t[r * N + i] + t[r1 * N + i]) / 3; }
        }

        /// Mediterranean grading of one linear photo pixel (see Green / Earth): luminance kept
        static void Grade(ref float r, ref float g, ref float b)
        {
            float sum = r + g + b + 1e-4f, y = .2126f * r + .7152f * g + .0722f * b;
            float exg = (2 * g - r - b) / sum;
            float wg = Mathf.Clamp01(exg / .12f) * Green;                       // vegetation: a little greener
            r *= 1 - .6f * wg; g *= 1 + wg; b *= 1 - .2f * wg;
            float orange = Mathf.Clamp01((r - b) / sum / .35f) * Mathf.Clamp01(-exg / .06f + .5f); // ochre / orange, not green
            float k = orange * Earth;
            r = Mathf.Lerp(r, y, k); g = Mathf.Lerp(g, y, k); b = Mathf.Lerp(b, y, k);
            float y2 = .2126f * r + .7152f * g + .0722f * b; if (y2 > 1e-5f) { float q = y / y2; r *= q; g *= q; b *= q; }
        }

        /// for NaturePass.Details: the photo per detail cell of tile tx, tz (R², rows from the NORTH edge like the detail
        /// layers there): green = vegetation (excess green, 0..1), bare = bright earth / gravel / dry ground (0..1); null without the mosaic
        public static (float[,] green, float[,] bare) PhotoCells(int tx, int tz, int R)
        {
            string src = MOSAIC + $"t_{tx}_{tz}.png"; if (!File.Exists(src)) return (null, null);
            var img = new Texture2D(2, 2); img.LoadImage(File.ReadAllBytes(src));
            if (img.width != N) { UnityEngine.Object.DestroyImmediate(img); return (null, null); }
            var px = img.GetPixels32(); UnityEngine.Object.DestroyImmediate(img);
            int f = N / R; var gr = new float[R, R]; var ba = new float[R, R];
            for (int r = 0; r < R; r++)
                for (int c = 0; c < R; c++)
                {
                    int tr0 = (R - 1 - r) * f; float sg = 0, sb = 0; int n = 0; // texture row 0 = south
                    for (int a = 0; a < f; a += 2)
                        for (int q = 0; q < f; q += 2)
                        {
                            var p = px[(tr0 + a) * N + c * f + q]; float R0 = p.r / 255f, G0 = p.g / 255f, B0 = p.b / 255f;
                            float sum = R0 + G0 + B0 + 1e-3f, exg = (2 * G0 - R0 - B0) / sum, lum = .2126f * R0 + .7152f * G0 + .0722f * B0;
                            sg += Mathf.Clamp01((exg - .01f) / .09f) * Mathf.Clamp01((lum - .08f) / .1f); // green, not deep shadow
                            sb += Mathf.Clamp01((lum - .36f) / .18f) * Mathf.Clamp01((.03f - exg) / .05f); n++;
                        }
                    gr[r, c] = sg / n; ba[r, c] = sb / n;
                }
            return (gr, ba);
        }

        // ---------------------------------------------------------------- build
        [MenuItem("Magistrala/Grafika: ortofoto tlo")]
        public static string Build()
        {
            if (!AssetDatabase.IsValidFolder(OUT)) AssetDatabase.CreateFolder(WorldBuilder.GEN + "/Terrain", "Macro");
            var models = WorldData.Models();
            var shader = Shader.Find("Magistrala/Terrain Lit Macro");
            var stock = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat");
            var terrains = new Dictionary<(int, int), Terrain>();
            foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                var parts = t.name.Split(' ')[1].Split(','); terrains[(int.Parse(parts[0]), int.Parse(parts[1]))] = t;
            }
            var set = new HashSet<(int, int)>(TILES);
            var log = new System.Text.StringBuilder();
            // pass 1: photo (filled), our splat's mean colour and the photo's weight per pixel; region sums for the colour match
            var work = new List<(int tx, int tz, Terrain t, float[][] ch, float[][] mean, float[] s, string stat)>();
            double[] sumP = new double[3], sumM = new double[3];
            foreach (var (tx, tz) in TILES)
            {
                if (!terrains.TryGetValue((tx, tz), out var terrain)) { log.Append($"{tx},{tz}: no terrain; "); continue; }
                string src = MOSAIC + $"t_{tx}_{tz}.png";
                if (!File.Exists(src)) { log.Append($"{tx},{tz}: no mosaic (run Tools/dof/dof_mosaic.py --src {Source} {tx},{tz}); "); continue; }
                var img = new Texture2D(2, 2); img.LoadImage(File.ReadAllBytes(src));
                if (img.width != N) { log.Append($"{tx},{tz}: mosaic {img.width}²; "); continue; }
                // LoadImage: row 0 = the PNG's bottom row = south (the mosaic is north up), as the terrain uv
                var px = img.GetPixels32(); UnityEngine.Object.DestroyImmediate(img);
                var bm = Buildings(tx, tz); var rm = Roads(tx, tz, models);
                // roofs / pools the footprints miss (buildings not in our data, roofs leaning 2–3 m off their footprint in the
                // photo): tile-red, bright white-grey and pool-blue pixels, grown 1 m
                var pm = new bool[N * N]; int np = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    int r = px[i].r, g = px[i].g, b = px[i].b;
                    pm[i] = (r > g * 1.22f && r > b * 1.4f && r > 95) || (r + g + b > 600 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) < 40) || (b > r * 1.25f && b > g * 1.02f && b > 110);
                }
                pm = Dilate(pm, 1);
                for (int i = 0; i < px.Length; i++) if (pm[i] && !bm[i] && !rm[i]) np++;
                var ch = new float[3][]; for (int k = 0; k < 3; k++) ch[k] = new float[N * N];
                var valid = new bool[N * N]; int nb = 0, nr = 0;
                for (int i = 0; i < px.Length; i++)
                {
                    for (int k = 0; k < 3; k++) ch[k][i] = Lin(px[i][k] / 255f);
                    valid[i] = px[i].a > 128 && !bm[i] && !rm[i] && !pm[i];
                    if (bm[i]) nb++; else if (rm[i]) nr++;
                }
                Inpaint(ch, valid, N);
                for (int k = 0; k < 3; k++) Blur3(ch[k]);
                Deshadow(ch);

                var td = terrain.terrainData; var layers = td.terrainLayers; int L = layers.Length;
                var means = new Vector3[L]; for (int k = 0; k < L; k++) means[k] = LayerMean(layers[k]);
                int ar = td.alphamapWidth; var A = td.GetAlphamaps(0, 0, ar, ar);
                var mean = new float[3][]; for (int k = 0; k < 3; k++) mean[k] = new float[N * N];
                var S = new float[N * N];
                for (int r = 0; r < N; r++)
                    for (int i = 0; i < N; i++)
                    {
                        int o = r * N + i;
                        double gx = Gx(tx, i), gz = Gz(tz, r);
                        // alphamap: [row = uv.y, col = uv.x], row 0 = south like ours
                        float fu = Mathf.Clamp((i + .5f) / N * ar - .5f, 0, ar - 1.001f), fv = Mathf.Clamp((r + .5f) / N * ar - .5f, 0, ar - 1.001f);
                        int u0 = (int)fu, v0 = (int)fv; float au = fu - u0, av = fv - v0;
                        Vector3 m = Vector3.zero;
                        for (int k = 0; k < L; k++)
                        {
                            float wgt = (A[v0, u0, k] * (1 - au) + A[v0, u0 + 1, k] * au) * (1 - av) + (A[v0 + 1, u0, k] * (1 - au) + A[v0 + 1, u0 + 1, k] * au) * av;
                            m += wgt * means[k];
                        }
                        mean[0][o] = m.x; mean[1][o] = m.y; mean[2][o] = m.z;
                        float s = 1;
                        double h = Tiles.Height(gx, gz); s *= Mathf.Clamp01((float)(h - .4)); // sea / beach under water: ours
                        // border to a tile without the photo: fade to unchanged
                        double ex = gx - tx * 1024, ez = gz - tz * 1024;
                        if (!set.Contains((tx - 1, tz))) s *= Mathf.Clamp01((float)(ex / FADE));
                        if (!set.Contains((tx + 1, tz))) s *= Mathf.Clamp01((float)((1024 - ex) / FADE));
                        if (!set.Contains((tx, tz - 1))) s *= Mathf.Clamp01((float)(ez / FADE));
                        if (!set.Contains((tx, tz + 1))) s *= Mathf.Clamp01((float)((1024 - ez) / FADE));
                        S[o] = s;
                        if (s > .99f && valid[o]) for (int k = 0; k < 3; k++) { sumP[k] += ch[k][o]; sumM[k] += mean[k][o]; }
                    }
                // kept for pass 2 at the output size (30 tiles × 2048² floats would be ~3.4 GB in the editor)
                work.Add((tx, tz, terrain, Down(ch), Down(mean), Down(new[] { S })[0], $"masked buildings {nb * 100f / px.Length:F1} %, roads {nr * 100f / px.Length:F1} %, photo roofs {np * 100f / px.Length:F1} %"));
            }
            // colour match (one per channel for the whole photo area, no seams between tiles): the photo's average ground colour
            // becomes our layers' average × Bright — the photo gives where it is lighter / darker / greener / drier, the palette
            // (and the lighting tuned for it) stays ours
            double Yl(double[] c) => .2126 * c[0] + .7152 * c[1] + .0722 * c[2];
            double yP = Yl(sumP), yM = Yl(sumM);
            var G = new float[3];
            for (int k = 0; k < 3; k++)
                G[k] = sumP[k] > 0 && yP > 0 ? (float)(yM / yP * Math.Pow(sumM[k] / yM / (sumP[k] / yP), HueMatch)) * Bright : 1;
            log.Append($"colour match gain {G[0]:F2}/{G[1]:F2}/{G[2]:F2}; ");
            var assign = new List<(Terrain t, Material m, string p)>();
            int M = OutN;
            foreach (var (tx, tz, terrain, ch, mean, S, stat) in work)
            {
                var outPx = new Color32[M * M]; var lums = new List<float>();
                for (int o = 0; o < M * M; o++)
                {
                    float s = S[o];
                    float pr = ch[0][o] * G[0], pg = ch[1][o] * G[1], pb = ch[2][o] * G[2];
                    float y = .2126f * pr + .7152f * pg + .0722f * pb;
                    pr = y + (pr - y) * Sat; pg = y + (pg - y) * Sat; pb = y + (pb - y) * Sat;
                    Grade(ref pr, ref pg, ref pb);
                    float R(float p, float m) => Mathf.Lerp(1, Mathf.Clamp(Mathf.Pow(p / Mathf.Max(m, .005f), Contrast), RMin, RMax), s * Strength);
                    float rr = R(pr, mean[0][o]), rg = R(pg, mean[1][o]), rb = R(pb, mean[2][o]);
                    if (s > .99f && (o & 63) == 0) lums.Add(.2126f * rr + .7152f * rg + .0722f * rb);
                    outPx[o] = new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(rr * 127.5f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(rg * 127.5f), 0, 255), (byte)Mathf.Clamp(Mathf.RoundToInt(rb * 127.5f), 0, 255), 255);
                }
                lums.Sort();
                var tex = new Texture2D(M, M, TextureFormat.RGBA32, false, true); tex.SetPixels32(outPx);
                string p = $"{OUT}/macro_{tx}_{tz}.png"; File.WriteAllBytes(p, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(p);
                var imp = (TextureImporter)AssetImporter.GetAtPath(p);
                imp.sRGBTexture = false; imp.mipmapEnabled = true; imp.wrapMode = TextureWrapMode.Clamp; imp.anisoLevel = 4; imp.maxTextureSize = OutN;
                imp.textureCompression = TextureImporterCompression.Compressed; imp.alphaSource = TextureImporterAlphaSource.None;
                imp.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name = "Standalone", overridden = true, maxTextureSize = OutN, format = TextureImporterFormat.DXT1 });
                imp.SaveAndReimport();
                var mt = AssetDatabase.LoadAssetAtPath<Material>($"{OUT}/terrain_{tx}_{tz}.mat");
                if (!mt) { mt = new Material(shader); AssetDatabase.CreateAsset(mt, $"{OUT}/terrain_{tx}_{tz}.mat"); }
                mt.shader = shader; if (stock) mt.shaderKeywords = stock.shaderKeywords; // (not CopyPropertiesFromMaterial: it dropped _MacroTex from the material)
                assign.Add((terrain, mt, p));
                float q(float f) => lums.Count > 0 ? lums[(int)(f * (lums.Count - 1))] : 0;
                log.Append($"{tx},{tz}: {stat}, ratio lum p10/50/90 {q(.1f):F2}/{q(.5f):F2}/{q(.9f):F2}; ");
            }
            // (a material without the texture samples the default grey: the whole ground at ×0.43 — checked here)
            AssetDatabase.Refresh();
            foreach (var (terrain, mt, p) in assign)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (!tex) throw new Exception("macro texture not imported: " + p);
                mt.SetTexture("_MacroTex", tex); EditorUtility.SetDirty(mt);
                if (mt.GetTexture("_MacroTex") != tex) throw new Exception("macro texture not set on " + mt.name);
                terrain.materialTemplate = mt; EditorUtility.SetDirty(terrain);
            }
            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log("[Magistrala] ortofoto tlo: " + log);
            return log.ToString();
        }

        /// debug: the mask + fill of one tile as a PNG (north up) for the alignment check against the photo
        public static string DebugMasks(int tx, int tz, string path)
        {
            var models = WorldData.Models(); var bm = Buildings(tx, tz, 0); var rm = Roads(tx, tz, models, 0);
            var t = new Texture2D(N, N, TextureFormat.RGBA32, false);
            var px = new Color32[N * N];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32((byte)(bm[i] ? 255 : 0), (byte)(rm[i] ? 255 : 0), 0, 255);
            t.SetPixels32(px); File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            return path;
        }
    }
}
