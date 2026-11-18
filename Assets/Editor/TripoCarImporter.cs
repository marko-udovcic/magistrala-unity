// Magistrala/Auti: uvezi Tripo modele — AI-generated cars (Tripo, image-to-3D, one mesh + one 4K baseColor JPEG, fragmented atlas)
// turned into traffic kinds (TrafficKind: lod0 / lod1 / lod2 parts + far mesh) drawn with the Magistrala/TripoCar shader.
// Per car: Assets/Models/Vehicles/Tripo/<name>.glb + <name>.json (Config below). Output: Assets/Generated/Vehicles/Tripo/<kind>/.
//   1. GLB read directly (positions, normals, uv, indices, the JPEG); glTF -> Unity (x mirrored, winding flipped, v flipped).
//   2. Normalise: the long axis onto Z, the front to +Z (red tail-lamp texels are at the rear; or Config.yaw), length to Config.length,
//      width to Config.bodyWidth (measured below the mirrors), y = 0 on the tyres, x / z centred.
//   3. Wheels: dark texels low in each quadrant -> wheel centre, radius, outer / inner face; triangles in the cylinder become 4
//      wheel meshes (origin = wheel centre, spun by TrafficSystem); a dark disc closes the arch behind each wheel.
//   4. Light bar (Config.lightbar): the saturated blue box high on the roof is cut out, our own bar mesh replaces it (beacon lamps 5 / 6).
//   5. Masks: every triangle rasterised in UV space at 2K; per texel the albedo (4K, 2x2 box) + object-space position / normal ->
//      class (paint, livery, glass, tyre, rim, head lamp, tail lamp, trim). Baked: albedo (livery texels painted over with plain paint,
//      a = smoothness) + mask (r paint, g glass, b metal, a lamp); charts dilated 6 texels.
//   6. Submeshes by triangle class: 0 body (paint part), 1 head lamps (lamp 4), 2 tail lamps (lamp 1); LODs by UnityMeshSimplifier
//      (MIT, UPM git package) with the same UVs, so the same textures; far mesh ~450 triangles with per-triangle colours (vertex colour,
//      mapped onto the shared far palette by TrafficBuilder).
//   7. Materials: one paint material per Config colour (_PaintColor), lamp off / on variants, wheels, the bar.
// TrafficBuilder.Kinds() picks the imported cars up (TripoKinds); this menu also rebuilds the traffic data when a Traffic object is open.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Magistrala.Traffic;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class TripoCarImporter
    {
        public const string SRC = "Assets/Models/Vehicles/Tripo";
        public const string OUT = "Assets/Generated/Vehicles/Tripo";
        const int MASK = 2048;

        /// <name>.json next to the GLB (JsonUtility; omitted fields keep these defaults; lengths in metres, final car frame:
        /// +Z forward, origin at the centre of the footprint, y = 0 on the ground)
        [Serializable]
        public class Config
        {
            public string kind;                 // traffic kind name (default: the file name)
            public float length = 4.5f;         // bumper to bumper
            public float bodyWidth;             // body width without mirrors (0: uniform scale from length)
            public float height;                // roof height without bar (0: uniform)
            public string yaw = "auto";         // "auto" | degrees about y applied to the raw model before normalising
            public string livery = "none";      // none | policija | hitna | taksi (Assets/Textures/Vehicles/Livery/<livery>.png)
            public string[] colors = { "#f2f2f0" };
            public float[] colorWeights;        // parallel to colors (default 1 each)
            public float weight = 2, vMul = 1, a = 1.8f, b = 3.2f, T = 1.3f;
            public bool lightbar;               // cut Tripo's roof box out, put our light bar there
            public string beaconColor = "#2f6bff";
            public float beaconShare = .5f;     // share of the cars of this kind driving with beacons on
            public float barWidth = 1.2f, barDepth = .3f, barHeight = .12f;
            // livery placement (0 = derived from the car's size): band (z front, z rear, y0, y1), side text (z centre, y centre, width),
            // bonnet text (z centre, width), rear text (y centre, width); bonnetMirror: mirrored bonnet text (ambulances)
            public float[] band, sideText, bonnetText, rearText;
            public bool bonnetMirror;
            public string liveryTint = "#ffffff";
        }

        [Serializable]
        public class Info
        {
            public string kind; public float len, halfW, h, wheelR;
            public Vector3[] wheels;
            public int tris0, tris1, tris2, trisFar;
            public bool bar;
        }

        // ------------------------------------------------------------------ menu
        [MenuItem("Magistrala/Auti: uvezi Tripo modele")]
        public static void ImportAllMenu()
        {
            var log = ImportAll();
            Debug.Log(log);
            if (GameObject.Find("Traffic")) TrafficBuilder.Build();
        }

        public static string ImportAll()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var glb in Directory.GetFiles(SRC, "*.glb").OrderBy(x => x))
            {
                var cfgPath = Path.ChangeExtension(glb, ".json");
                if (!File.Exists(cfgPath)) { sb.AppendLine($"[tripo] {Path.GetFileName(glb)}: no {Path.GetFileName(cfgPath)}, skipped"); continue; }
                try { sb.AppendLine(Import(glb.Replace('\\', '/'), cfgPath)); }
                catch (Exception e) { sb.AppendLine($"[tripo] {Path.GetFileName(glb)} FAILED: {e}"); }
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        public static Config LoadConfig(string cfgPath)
        {
            var c = JsonUtility.FromJson<Config>(File.ReadAllText(cfgPath));
            if (string.IsNullOrEmpty(c.kind)) c.kind = Path.GetFileNameWithoutExtension(cfgPath);
            if (c.colorWeights == null || c.colorWeights.Length != c.colors.Length) c.colorWeights = c.colors.Select(_ => 1f).ToArray();
            return c;
        }

        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = Path.GetDirectoryName(p).Replace('\\', '/'); Dir(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(p)); } }

        // ------------------------------------------------------------------ GLB
        class Raw { public Vector3[] P, N; public Vector2[] UV; public int[] I; public byte[] image; }

        static Raw ReadGlb(string path)
        {
            var b = File.ReadAllBytes(path);
            int jl = BitConverter.ToInt32(b, 12);
            var j = MiniJson.O(MiniJson.Parse(System.Text.Encoding.UTF8.GetString(b, 20, jl)));
            int bin = 20 + jl + 8;
            var accs = MiniJson.L(j["accessors"]); var views = MiniJson.L(j["bufferViews"]);
            int Off(Dictionary<string, object> a, out int count)
            {
                var v = MiniJson.O(views[(int)MiniJson.D(a["bufferView"])]);
                count = (int)MiniJson.D(a["count"]);
                return bin + (int)(MiniJson.G(v, "byteOffset") is object o ? MiniJson.D(o) : 0) + (int)(MiniJson.G(a, "byteOffset") is object o2 ? MiniJson.D(o2) : 0);
            }
            var prim = MiniJson.O(MiniJson.L(MiniJson.O(MiniJson.L(j["meshes"])[0])["primitives"])[0]);
            var attr = MiniJson.O(prim["attributes"]);
            Vector3[] V3(int ai) { var a = MiniJson.O(accs[ai]); int o = Off(a, out int n); var r = new Vector3[n]; for (int i = 0; i < n; i++) r[i] = new Vector3(BitConverter.ToSingle(b, o + 12 * i), BitConverter.ToSingle(b, o + 12 * i + 4), BitConverter.ToSingle(b, o + 12 * i + 8)); return r; }
            Vector2[] V2(int ai) { var a = MiniJson.O(accs[ai]); int o = Off(a, out int n); var r = new Vector2[n]; for (int i = 0; i < n; i++) r[i] = new Vector2(BitConverter.ToSingle(b, o + 8 * i), BitConverter.ToSingle(b, o + 8 * i + 4)); return r; }
            var raw = new Raw { P = V3((int)MiniJson.D(attr["POSITION"])), N = V3((int)MiniJson.D(attr["NORMAL"])), UV = V2((int)MiniJson.D(attr["TEXCOORD_0"])) };
            {
                var a = MiniJson.O(accs[(int)MiniJson.D(prim["indices"])]); int o = Off(a, out int n); int ct = (int)MiniJson.D(a["componentType"]);
                raw.I = new int[n];
                for (int i = 0; i < n; i++) raw.I[i] = ct == 5125 ? (int)BitConverter.ToUInt32(b, o + 4 * i) : ct == 5123 ? BitConverter.ToUInt16(b, o + 2 * i) : b[o + i];
            }
            var img = MiniJson.O(MiniJson.L(j["images"])[0]);
            var iv = MiniJson.O(views[(int)MiniJson.D(img["bufferView"])]);
            int io = bin + (int)(MiniJson.G(iv, "byteOffset") is object q ? MiniJson.D(q) : 0), il = (int)MiniJson.D(iv["byteLength"]);
            raw.image = new byte[il]; Array.Copy(b, io, raw.image, 0, il);
            // glTF (right-handed, v down) -> Unity (left-handed, v up)
            for (int i = 0; i < raw.P.Length; i++) { raw.P[i].x = -raw.P[i].x; raw.N[i].x = -raw.N[i].x; raw.UV[i].y = 1 - raw.UV[i].y; }
            for (int t = 0; t < raw.I.Length; t += 3) { int s = raw.I[t + 1]; raw.I[t + 1] = raw.I[t + 2]; raw.I[t + 2] = s; }
            return raw;
        }

        // ------------------------------------------------------------------ texture sampling
        class Tex
        {
            public int w, h; public Color32[] px;
            public Color At(Vector2 uv)
            {
                int x = Mathf.Clamp((int)(uv.x * w), 0, w - 1), y = Mathf.Clamp((int)(uv.y * h), 0, h - 1);
                return px[y * w + x];
            }
            /// 2x2 box at the centre of a 2K texel (the source is 4K)
            public Color Box(Vector2 uv)
            {
                int x = Mathf.Clamp((int)(uv.x * w - .5f), 0, w - 2), y = Mathf.Clamp((int)(uv.y * h - .5f), 0, h - 2);
                Color a = px[y * w + x], b = px[y * w + x + 1], c = px[(y + 1) * w + x], d = px[(y + 1) * w + x + 1];
                return (a + b + c + d) * .25f;
            }
        }

        static float Lum(Color c) => .299f * c.r + .587f * c.g + .114f * c.b;
        static float Sat(Color c) { float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b)); return mx < 1e-3f ? 0 : (mx - mn) / mx; }
        static bool Red(Color c) => c.r > .33f && c.r > 1.7f * c.g && c.r > 1.6f * c.b;
        static bool Blue(Color c) => c.b > .3f && c.b - c.r > .18f && c.b > c.g + .05f;
        static bool Yellow(Color c) => c.r > .45f && c.g > .35f && c.b < .55f * c.g && Mathf.Abs(c.r - c.g) < .3f;

        // classes
        const int PAINT = 0, LIVERY = 1, GLASS = 2, TYRE = 3, RIM = 4, HEAD = 5, TAIL = 6, TRIM = 7;

        class Car
        {
            public Config cfg; public Vector3[] P, N; public Vector2[] UV; public int[] I; public Tex tex;
            public float L, hw, H;                   // final length, body half width, roof height (no bar)
            public Vector3[] wc = new Vector3[4];    // wheel centres (x = middle of the tyre), radius, half tyre width
            public float wr, wInner;                 // radius, |x| of the inner tyre face
            public Bounds bar; public bool hasBar;
            public int[] triCls;                     // per triangle: -1 cut, 0 body, 1 head, 2 tail, 10 + w wheel w
        }

        static float Median(List<float> v) { if (v.Count == 0) return 0; v.Sort(); return v[v.Count / 2]; }
        static float Pct(List<float> v, float q) { if (v.Count == 0) return 0; v.Sort(); return v[Mathf.Clamp((int)(q * (v.Count - 1)), 0, v.Count - 1)]; }

        // ------------------------------------------------------------------ import one car
        public static string Import(string glbPath, string cfgPath)
        {
            var cfg = LoadConfig(cfgPath);
            var raw = ReadGlb(glbPath);
            var t2 = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            t2.LoadImage(raw.image);
            var tex = new Tex { w = t2.width, h = t2.height, px = t2.GetPixels32() };
            UnityEngine.Object.DestroyImmediate(t2);
            var car = new Car { cfg = cfg, P = raw.P, N = raw.N, UV = raw.UV, I = raw.I, tex = tex };
            var log = new System.Text.StringBuilder($"[tripo] {cfg.kind}: {raw.I.Length / 3} tris, tex {tex.w}");
            Normalise(car, log);
            Wheels(car, log);
            if (cfg.lightbar) Bar(car, log);
            var (albedo, mask, paintRef, darkUV, paintUV) = BakeMasks(car, log);
            ClassifyTriangles(car, mask, paintUV);
            string dir = $"{OUT}/{cfg.kind}"; Dir(dir);
            var albTex = SaveTex(albedo, $"{dir}/{cfg.kind}_albedo.png", true);
            var maskTex = SaveTex(mask, $"{dir}/{cfg.kind}_mask.png", false);
            var info = new Info { kind = cfg.kind, len = car.L, halfW = car.hw, h = car.H, wheelR = car.wr, wheels = car.wc, bar = car.hasBar };
            BuildMeshes(car, dir, darkUV, paintUV, info, log);
            BuildMaterials(car, dir, albTex, maskTex, paintRef);
            File.WriteAllText($"{dir}/{cfg.kind}_info.json", JsonUtility.ToJson(info, true));
            AssetDatabase.ImportAsset($"{dir}/{cfg.kind}_info.json");
            log.Append($"; lods {info.tris0}/{info.tris1}/{info.tris2}/far {info.trisFar}");
            return log.ToString();
        }

        static void Normalise(Car c, System.Text.StringBuilder log)
        {
            var P = c.P; var N = c.N;
            Bounds B() { var b = new Bounds(P[0], Vector3.zero); foreach (var p in P) b.Encapsulate(p); return b; }
            var b0 = B();
            float yaw;
            if (c.cfg.yaw != "auto") yaw = float.Parse(c.cfg.yaw, System.Globalization.CultureInfo.InvariantCulture);
            else
            {
                yaw = b0.size.x > b0.size.z ? 90 : 0;  // the long axis onto z
                // front: the red (tail lamp) texels sit at the rear end
                var q = Quaternion.Euler(0, yaw, 0); float sum = 0, wsum = 0;
                for (int t = 0; t < c.I.Length; t += 3)
                {
                    var uv = (c.UV[c.I[t]] + c.UV[c.I[t + 1]] + c.UV[c.I[t + 2]]) / 3;
                    if (!Red(c.tex.At(uv))) continue;
                    var p = q * ((P[c.I[t]] + P[c.I[t + 1]] + P[c.I[t + 2]]) / 3 - b0.center);
                    float a = Vector3.Cross(P[c.I[t + 1]] - P[c.I[t]], P[c.I[t + 2]] - P[c.I[t]]).magnitude;
                    sum += p.z * a; wsum += a;
                }
                if (wsum > 0 && sum / wsum > 0) yaw += 180;
                log.Append($", red centroid {(wsum > 0 ? sum / wsum : 0):F3} -> yaw {yaw}");
            }
            var rot = Quaternion.Euler(0, yaw, 0);
            for (int i = 0; i < P.Length; i++) { P[i] = rot * P[i]; N[i] = rot * N[i]; }
            var b = B();
            float s = c.cfg.length / b.size.z, sy = s, sx = s;
            if (c.cfg.height > 0) sy = c.cfg.height / b.size.y; // roof incl. anything on it: approximate
            float hwRaw = 0;
            foreach (var p in P) if (p.y < b.min.y + .55f * b.size.y) hwRaw = Mathf.Max(hwRaw, Mathf.Abs(p.x - b.center.x));
            if (c.cfg.bodyWidth > 0) sx = c.cfg.bodyWidth / (2 * hwRaw);
            for (int i = 0; i < P.Length; i++)
            {
                var p = P[i];
                P[i] = new Vector3((p.x - b.center.x) * sx, (p.y - b.min.y) * sy, (p.z - b.center.z) * s);
                N[i] = new Vector3(N[i].x / sx, N[i].y / sy, N[i].z / s).normalized;
            }
            c.L = c.cfg.length; c.hw = hwRaw * sx; c.H = b.size.y * sy;
            log.Append($", raw {b.size.x:F3}x{b.size.y:F3}x{b.size.z:F3}, scale x {sx:F2} y {sy:F2} z {s:F2}, body width {2 * c.hw:F2} m, height {c.H:F2} m");
        }

        static Vector3 Centroid(Car c, int t) => (c.P[c.I[t]] + c.P[c.I[t + 1]] + c.P[c.I[t + 2]]) / 3;
        static Vector2 CentroidUV(Car c, int t) => (c.UV[c.I[t]] + c.UV[c.I[t + 1]] + c.UV[c.I[t + 2]]) / 3;

        static void Wheels(Car c, System.Text.StringBuilder log)
        {
            // the contact patches: the lowest vertices, one group per quadrant -> wheel z; the tread = the cylinder of radius r about
            // (y = r, z) with the most vertices on it -> r; outer face = the widest vertex inside it
            var zc = new float[4]; var outer = new float[4]; float rSum = 0;
            for (int w = 0; w < 4; w++)
            {
                bool left = w % 2 == 0, front = w < 2;
                var low = new List<float>();
                foreach (var p in c.P) if (p.y < .02f && (p.x < 0) == left && (p.z > 0) == front && Mathf.Abs(p.z) > .1f * c.L) low.Add(p.z);
                zc[w] = Median(low);
                float bestR = .33f; int bestN = -1;
                for (float r = .24f; r < .48f; r += .005f)
                {
                    int n = 0;
                    foreach (var p in c.P)
                    {
                        if ((p.x < 0) != left || Mathf.Abs(p.x) < .7f * c.hw || p.y > 2 * r) continue;
                        float dy = p.y - r, dz = p.z - zc[w]; float d = Mathf.Sqrt(dy * dy + dz * dz);
                        if (Mathf.Abs(d - r) < .012f && p.y > .03f) n++;
                    }
                    if (n > bestN) { bestN = n; bestR = r; }
                }
                rSum += bestR;
                var xs = new List<float>();
                foreach (var p in c.P) { float dy = p.y - bestR, dz = p.z - zc[w]; if ((p.x < 0) == left && dy * dy + dz * dz < bestR * bestR * .9f) xs.Add(Mathf.Abs(p.x)); }
                outer[w] = Pct(xs, .995f);
            }
            c.wr = rSum / 4;
            float xo = (outer[0] + outer[1] + outer[2] + outer[3]) / 4, tw = Mathf.Clamp(.62f * c.wr, .18f, .27f);
            c.wInner = xo - tw;
            float zf = (zc[0] + zc[1]) / 2, zr = (zc[2] + zc[3]) / 2;
            for (int w = 0; w < 4; w++) c.wc[w] = new Vector3((w % 2 == 0 ? -1 : 1) * (xo - tw / 2), c.wr, w < 2 ? zf : zr);
            log.Append($", wheels r {c.wr:F3} track {2 * (xo - tw / 2):F2} base {zf - zr:F2} (z {zc[0]:F2} {zc[1]:F2} / {zc[2]:F2} {zc[3]:F2})");
        }

        static int WheelAt(Car c, Vector3 p, float grow = 1.03f)
        {
            if (Mathf.Abs(p.x) < c.wInner) return -1;
            for (int w = 0; w < 4; w++)
            {
                if (Mathf.Sign(p.x) != Mathf.Sign(c.wc[w].x)) continue;
                float dy = p.y - c.wc[w].y, dz = p.z - c.wc[w].z;
                if (dy * dy + dz * dz < c.wr * c.wr * grow * grow) return w;
            }
            return -1;
        }

        static void Bar(Car c, System.Text.StringBuilder log)
        {
            var pts = new List<Vector3>();
            for (int t = 0; t < c.I.Length; t += 3)
            {
                var p = Centroid(c, t);
                if (p.y < .8f * c.H || Mathf.Abs(p.x) > .8f * c.hw) continue;
                if (Blue(c.tex.At(CentroidUV(c, t)))) pts.Add(p);
            }
            if (pts.Count < 20) { log.Append(", no light bar found"); return; }
            var xs = pts.Select(p => p.x).ToList(); var ys = pts.Select(p => p.y).ToList(); var zs = pts.Select(p => p.z).ToList();
            var min = new Vector3(Pct(xs, .01f), Pct(ys, .02f), Pct(zs, .01f)); var max = new Vector3(Pct(xs, .99f), Pct(ys, .99f), Pct(zs, .99f));
            c.bar = new Bounds((min + max) / 2, max - min); c.hasBar = true;
            // the roof below it: the car's height is the roof, not the box
            c.H = min.y - .02f;
            log.Append($", Tripo bar box {c.bar.size.x:F2}x{c.bar.size.y:F2}x{c.bar.size.z:F2} at y {min.y:F2}, roof {c.H:F2} m");
        }

        static bool InBar(Car c, Vector3 p, Color col)
        {
            if (!c.hasBar) return false;
            var b = c.bar;
            if (p.x < b.min.x - .03f || p.x > b.max.x + .03f || p.z < b.min.z - .03f || p.z > b.max.z + .03f) return false;
            if (p.y > b.min.y - .015f) return true;
            return p.y > b.min.y - .1f && Lum(col) < .22f; // the dark feet
        }

        // ------------------------------------------------------------------ masks
        static int Classify(Car c, Vector3 p, Vector3 n, Color col)
        {
            float lum = Lum(col), sat = Sat(col);
            float zf = p.z / (c.L / 2), yf = p.y / c.H, xf = Mathf.Abs(p.x) / c.hw;
            if (WheelAt(c, p, 1.0f) >= 0) return lum < .2f || sat > .3f ? TYRE : RIM;
            // red: tail lamps at the rear end; elsewhere baked livery (ambulance bands, mirrored lettering)
            if (Red(col)) return zf < -.8f || (zf < -.6f && Mathf.Abs(n.z) > .5f) ? TAIL : LIVERY;
            // the baked livery (band, the mirrored wordmarks): saturated blue / yellow, also the dark navy text
            if (sat > .3f && (Blue(col) || Yellow(col) || (col.b > .2f && col.b > 1.6f * col.r && col.b > 1.25f * col.g))) return LIVERY;
            if (zf > .8f && yf > .4f && yf < .64f && xf > .48f && lum > .12f && lum < .78f && n.z > .3f) return HEAD;
            if (yf > .56f && lum < .3f && sat < .45f && Mathf.Abs(n.y) < .93f) return GLASS;
            if (lum < .26f) return TRIM;
            if (lum > .4f) return PAINT;
            return sat < .2f && lum > .3f ? PAINT : TRIM;
        }

        static (Color[] albedo, Color[] mask, float paintRef, Vector2 darkUV, Vector2 paintUV) BakeMasks(Car c, System.Text.StringBuilder log)
        {
            int S = MASK;
            var alb = new Color[S * S]; var cls = new sbyte[S * S]; for (int i = 0; i < cls.Length; i++) cls[i] = -1;
            var pos = new Vector3[S * S];
            // rasterise each triangle in UV space; per texel: albedo (2x2 of the 4K map), class from colour + position + normal
            for (int t = 0; t < c.I.Length; t += 3)
            {
                int i0 = c.I[t], i1 = c.I[t + 1], i2 = c.I[t + 2];
                Vector2 a = c.UV[i0] * S, b = c.UV[i1] * S, d = c.UV[i2] * S;
                var fn = Vector3.Cross(c.P[i1] - c.P[i0], c.P[i2] - c.P[i0]);
                int x0 = Mathf.Max(0, (int)Mathf.Floor(Mathf.Min(a.x, Mathf.Min(b.x, d.x)))), x1 = Mathf.Min(S - 1, (int)Mathf.Ceil(Mathf.Max(a.x, Mathf.Max(b.x, d.x))));
                int y0 = Mathf.Max(0, (int)Mathf.Floor(Mathf.Min(a.y, Mathf.Min(b.y, d.y)))), y1 = Mathf.Min(S - 1, (int)Mathf.Ceil(Mathf.Max(a.y, Mathf.Max(b.y, d.y))));
                float area = (b.x - a.x) * (d.y - a.y) - (d.x - a.x) * (b.y - a.y);
                if (Mathf.Abs(area) < 1e-8f) continue;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        var q = new Vector2(x + .5f, y + .5f);
                        float w1 = ((q.x - a.x) * (d.y - a.y) - (d.x - a.x) * (q.y - a.y)) / area;
                        float w2 = ((b.x - a.x) * (q.y - a.y) - (q.x - a.x) * (b.y - a.y)) / area;
                        float w0 = 1 - w1 - w2;
                        const float e = -.02f;
                        if (w0 < e || w1 < e || w2 < e) continue;
                        int k = y * S + x;
                        var p = c.P[i0] * w0 + c.P[i1] * w1 + c.P[i2] * w2;
                        var n = (c.N[i0] * w0 + c.N[i1] * w1 + c.N[i2] * w2).normalized;
                        if (Vector3.Dot(n, fn) < 0) n = -n;
                        var col = c.tex.Box(q / S);
                        alb[k] = col; pos[k] = p;
                        cls[k] = (sbyte)Classify(c, p, n, col);
                    }
            }
            // plain paint: median luminance of the paint texels
            var pl = new List<float>(); for (int k = 0; k < cls.Length; k += 7) if (cls[k] == PAINT) pl.Add(Lum(alb[k]));
            float paintRef = Mathf.Max(.3f, Pct(pl, .6f));
            Color paintCol = new Color(paintRef, paintRef, paintRef);
            // livery: dilate 2 texels into light / bluish neighbours (the anti-aliased edges of the baked band and text)
            for (int it = 0; it < 2; it++)
            {
                var add = new List<int>();
                for (int y = 1; y < S - 1; y++)
                    for (int x = 1; x < S - 1; x++)
                    {
                        int k = y * S + x; if (cls[k] != PAINT) continue;
                        if (cls[k - 1] == LIVERY || cls[k + 1] == LIVERY || cls[k - S] == LIVERY || cls[k + S] == LIVERY) add.Add(k);
                    }
                foreach (var k in add) cls[k] = LIVERY;
            }
            var mask = new Color[S * S];
            Vector2 darkUV = new Vector2(-1, -1); int nCls0 = 0; var counts = new int[8];
            for (int k = 0; k < cls.Length; k++)
            {
                int cl = cls[k]; if (cl < 0) continue; nCls0++; counts[cl]++;
                var col = alb[k];
                float sm;
                switch (cl)
                {
                    case LIVERY: col = paintCol; sm = .55f; mask[k] = new Color(1, 0, 0, 0); break;
                    case PAINT: sm = .55f; mask[k] = new Color(1, 0, 0, 0); break;
                    case GLASS: sm = .96f; mask[k] = new Color(0, 1, 0, 0); col *= .8f; break;
                    case TYRE: sm = .18f; mask[k] = new Color(0, 0, 0, 0); col = Color.Lerp(col, new Color(.035f, .035f, .037f), .4f); if (darkUV.x < 0 && Lum(alb[k]) < .08f) darkUV = new Vector2((k % S + .5f) / S, (k / S + .5f) / S); break;
                    case RIM: sm = .78f; mask[k] = new Color(0, 0, 1, 0); break;
                    case HEAD: sm = .92f; mask[k] = new Color(0, 0, .3f, Mathf.Clamp01((Lum(col) - .2f) * 1.6f)); break;
                    case TAIL: sm = .9f; mask[k] = new Color(0, 0, 0, Mathf.Clamp01(col.r * 1.3f)); break;
                    default: sm = .36f; mask[k] = new Color(0, 0, 0, 0); break;
                }
                col.a = sm; alb[k] = col;
            }
            // chart padding: grow every chart 6 texels into the empty atlas (bilinear / mips don't pull in garbage)
            var filled = new bool[S * S]; for (int k = 0; k < filled.Length; k++) filled[k] = cls[k] >= 0;
            for (int it = 0; it < 6; it++)
            {
                var nf = (bool[])filled.Clone();
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        int k = y * S + x; if (filled[k]) continue;
                        Color sa = default, sm = default; int n = 0;
                        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= S || yy >= S) continue;
                            int kk = yy * S + xx; if (!filled[kk]) continue;
                            sa += alb[kk]; sm += mask[kk]; n++;
                        }
                        if (n > 0) { alb[k] = sa / n; mask[k] = sm / n; nf[k] = true; }
                    }
                filled = nf;
            }
            log.Append($", texels {nCls0}: paint {counts[PAINT]} livery {counts[LIVERY]} glass {counts[GLASS]} tyre {counts[TYRE]} rim {counts[RIM]} head {counts[HEAD]} tail {counts[TAIL]} trim {counts[TRIM]}, paintRef {paintRef:F2}");
            c_cls = cls;
            // a texel deep inside plain paint (the roof patch)
            Vector2 paintUV = new Vector2(.5f, .5f);
            for (int y = 4; y < S - 4 && paintUV.x == .5f; y += 3)
                for (int x = 4; x < S - 4; x += 3)
                {
                    bool ok = true;
                    for (int dy = -3; dy <= 3 && ok; dy++) for (int dx = -3; dx <= 3 && ok; dx++) { int kk = (y + dy) * S + x + dx; ok = cls[kk] == PAINT && Mathf.Abs(Lum(alb[kk]) - paintRef) < .04f; }
                    if (ok) { paintUV = new Vector2((x + .5f) / S, (y + .5f) / S); break; }
                }
            return (alb, mask, paintRef, darkUV, paintUV);
        }
        static sbyte[] c_cls; // last baked class map (triangle classification)

        static void ClassifyTriangles(Car c, Color[] mask, Vector2 paintUV)
        {
            int S = MASK; var cls = c_cls;
            c.triCls = new int[c.I.Length / 3];
            for (int t = 0; t < c.I.Length; t += 3)
            {
                var p = Centroid(c, t);
                var uv = CentroidUV(c, t);
                Color col = c.tex.At(uv);
                if (InBar(c, p, col))
                {
                    // the box's underside closes the roof (Tripo left no roof below it): kept, turned up, plain paint
                    var fn = Vector3.Cross(c.P[c.I[t + 1]] - c.P[c.I[t]], c.P[c.I[t + 2]] - c.P[c.I[t]]).normalized;
                    if (fn.y < -.7f && p.y < c.bar.min.y + .03f)
                    {
                        int sw = c.I[t + 1]; c.I[t + 1] = c.I[t + 2]; c.I[t + 2] = sw;
                        for (int q = 0; q < 3; q++) { c.N[c.I[t + q]] = Vector3.up; c.UV[c.I[t + q]] = paintUV; }
                        c.triCls[t / 3] = 0;
                    }
                    else c.triCls[t / 3] = -1;
                    continue;
                }
                int w = WheelAt(c, p);
                if (w >= 0) { c.triCls[t / 3] = 10 + w; continue; }
                // lamps: the centroid + the 3 corners, majority
                int head = 0, tail = 0;
                for (int q = 0; q < 4; q++)
                {
                    var u = q == 3 ? uv : Vector2.Lerp(c.UV[c.I[t + q]], uv, .25f);
                    int x = Mathf.Clamp((int)(u.x * S), 0, S - 1), y = Mathf.Clamp((int)(u.y * S), 0, S - 1);
                    int k = cls[y * S + x]; if (k == HEAD) head++; else if (k == TAIL) tail++;
                }
                c.triCls[t / 3] = head >= 2 ? 1 : tail >= 2 ? 2 : 0;
            }
        }

        static Texture2D SaveTex(Color[] px, string path, bool srgb)
        {
            var t = new Texture2D(MASK, MASK, TextureFormat.RGBA32, false, !srgb);
            t.SetPixels(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = srgb; imp.mipmapEnabled = true; imp.alphaIsTransparency = false; imp.anisoLevel = 4;
            imp.maxTextureSize = MASK; imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ meshes
        class MB
        {
            public List<Vector3> V = new List<Vector3>(); public List<Vector3> N = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>();
            public List<Color> C = new List<Color>();
            public List<int>[] T; readonly Dictionary<int, int> map = new Dictionary<int, int>();
            public MB(int subs) { T = new List<int>[subs]; for (int i = 0; i < subs; i++) T[i] = new List<int>(); }
            public int Src(Car c, int i, Vector3 off)
            {
                if (!map.TryGetValue(i, out int j)) { j = V.Count; map[i] = j; V.Add(c.P[i] - off); N.Add(c.N[i]); UV.Add(c.UV[i]); }
                return j;
            }
            public int Add(Vector3 p, Vector3 n, Vector2 uv) { V.Add(p); N.Add(n); UV.Add(uv); return V.Count - 1; }
            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); if (C.Count == V.Count) m.SetColors(C);
                m.subMeshCount = T.Length; for (int s = 0; s < T.Length; s++) m.SetTriangles(T[s], s);
                m.RecalculateBounds();
                return m;
            }
        }

        static Mesh Save(Mesh m, string path)
        {
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old) { old.Clear(); EditorUtility.CopySerialized(m, old); EditorUtility.SetDirty(old); return old; }
            AssetDatabase.CreateAsset(m, path); return m;
        }

        static Mesh SimplifyKeepUV(Mesh src, float quality, bool seams, string name)
        {
            var ms = new UnityMeshSimplifier.MeshSimplifier();
            var o = UnityMeshSimplifier.SimplificationOptions.Default;
            o.PreserveUVSeamEdges = seams; o.PreserveBorderEdges = false; o.EnableSmartLink = true; o.VertexLinkDistance = .0005;
            o.PreserveSurfaceCurvature = false; o.MaxIterationCount = 200; o.Agressiveness = 7;
            ms.SimplificationOptions = o;
            ms.Initialize(src);
            ms.SimplifyMesh(quality);
            var m = ms.ToMesh(); m.name = name;
            m.RecalculateBounds();
            return m;
        }

        /// LODs of a fragmented atlas: UV-preserving decimation stalls (~8k on the police car), so: weld by position, simplify the
        /// geometry only, then give each output triangle the affine UV map of its nearest source triangle (corners extrapolated in
        /// that triangle's plane; the charts are padded). src: source triangle ids, off: subtracted from positions.
        static (MB mb, int[] srcOf) Decimate(Car c, List<int> src, Vector3 off, int target, bool lampSubs, float cell = .001f)
        {
            var key = new Dictionary<Vector3Int, int>(); var V = new List<Vector3>(); var Nn = new List<Vector3>(); var T = new List<int>(); var cnt = new List<int>();
            var seen = new HashSet<(int, int, int)>();
            foreach (int t in src)
            {
                int a0 = -1, a1 = -1, a2 = -1;
                for (int q = 0; q < 3; q++)
                {
                    var p = c.P[c.I[t * 3 + q]];
                    // cell > 1 mm: vertex clustering first (closes Tripo's cracks, which stall the quadric decimation)
                    var k = new Vector3Int(Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.y / cell), Mathf.RoundToInt(p.z / cell));
                    if (!key.TryGetValue(k, out int j)) { j = V.Count; key[k] = j; V.Add(Vector3.zero); Nn.Add(Vector3.zero); cnt.Add(0); }
                    Nn[j] += c.N[c.I[t * 3 + q]]; V[j] += p; cnt[j]++;
                    if (q == 0) a0 = j; else if (q == 1) a1 = j; else a2 = j;
                }
                if (a0 == a1 || a1 == a2 || a0 == a2) continue;
                int lo3 = Mathf.Min(a0, Mathf.Min(a1, a2)), hi3 = Mathf.Max(a0, Mathf.Max(a1, a2));
                if (!seen.Add((lo3, a0 + a1 + a2 - lo3 - hi3, hi3))) continue;
                T.Add(a0); T.Add(a1); T.Add(a2);
            }
            for (int i = 0; i < Nn.Count; i++) { Nn[i] = Nn[i].normalized; V[i] /= Mathf.Max(1, cnt[i]); }
            var wm = new Mesh { indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 }; wm.SetVertices(V); wm.SetNormals(Nn); wm.SetTriangles(T, 0);
            var ms = new UnityMeshSimplifier.MeshSimplifier();
            var o = UnityMeshSimplifier.SimplificationOptions.Default;
            o.PreserveUVSeamEdges = false; o.PreserveBorderEdges = false; o.EnableSmartLink = false; o.MaxIterationCount = 300; o.Agressiveness = 8;
            ms.SimplificationOptions = o; ms.Initialize(wm); ms.SimplifyMesh(Mathf.Clamp01((float)target / Mathf.Max(1, T.Count / 3)));
            var lo = ms.ToMesh(); var lv = lo.vertices; var ln = lo.normals; var lt = lo.GetTriangles(0);
            UnityEngine.Object.DestroyImmediate(wm);
            // grid of the source triangles' centroids (10 cm)
            var grid = new Dictionary<Vector3Int, List<int>>();
            Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x * 10), Mathf.FloorToInt(p.y * 10), Mathf.FloorToInt(p.z * 10));
            foreach (int t in src) { var cc = Centroid(c, t * 3); var k = Cell(cc); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(); l.Add(t); }
            var mb = new MB(lampSubs ? 3 : 1); var srcOf = new int[lt.Length / 3];
            for (int t = 0; t < lt.Length; t += 3)
            {
                Vector3 p0 = lv[lt[t]], p1 = lv[lt[t + 1]], p2 = lv[lt[t + 2]], cen = (p0 + p1 + p2) / 3;
                var fn = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                int best = -1; float bd = 1e9f; var k0 = Cell(cen);
                for (int r = 0; r <= 3 && best < 0; r++)
                    for (int dx = -r; dx <= r; dx++) for (int dy = -r; dy <= r; dy++) for (int dz = -r; dz <= r; dz++)
                    {
                        if (!grid.TryGetValue(k0 + new Vector3Int(dx, dy, dz), out var l)) continue;
                        foreach (int s in l)
                        {
                            var sc = Centroid(c, s * 3);
                            var sn = Vector3.Cross(c.P[c.I[s * 3 + 1]] - c.P[c.I[s * 3]], c.P[c.I[s * 3 + 2]] - c.P[c.I[s * 3]]).normalized;
                            float d = (sc - cen).sqrMagnitude * (1.5f - .5f * Vector3.Dot(sn, fn)); // prefer the same facing
                            if (d < bd) { bd = d; best = s; }
                        }
                    }
                if (best < 0) best = src[0];
                srcOf[t / 3] = best;
                // affine uv of the source triangle, extrapolated
                Vector3 a = c.P[c.I[best * 3]], b = c.P[c.I[best * 3 + 1]], d3 = c.P[c.I[best * 3 + 2]];
                Vector2 ua = c.UV[c.I[best * 3]], ub = c.UV[c.I[best * 3 + 1]], ud = c.UV[c.I[best * 3 + 2]];
                Vector3 e1 = b - a, e2 = d3 - a; float d11 = Vector3.Dot(e1, e1), d12 = Vector3.Dot(e1, e2), d22 = Vector3.Dot(e2, e2), den = d11 * d22 - d12 * d12;
                Vector2 UVat(Vector3 p)
                {
                    if (Mathf.Abs(den) < 1e-12f) return ua;
                    var e = p - a; float q1 = Vector3.Dot(e, e1), q2 = Vector3.Dot(e, e2);
                    float v = (d22 * q1 - d12 * q2) / den, w = (d11 * q2 - d12 * q1) / den;
                    // keep the extrapolation modest: pull far corners towards the source triangle
                    float u0 = 1 - v - w; float m = Mathf.Min(u0, Mathf.Min(v, w));
                    if (m < -.6f) { float s = .6f / -m; v = (v - 1f / 3) * s + 1f / 3; w = (w - 1f / 3) * s + 1f / 3; u0 = 1 - v - w; }
                    return ua * u0 + ub * v + ud * w;
                }
                int sub = lampSubs ? Mathf.Clamp(c.triCls[best], 0, 2) : 0;
                for (int q = 0; q < 3; q++)
                {
                    var p = lv[lt[t + q]];
                    mb.T[sub].Add(mb.Add(p - off, ln != null && ln.Length == lv.Length ? ln[lt[t + q]] : fn, UVat(p)));
                }
            }
            UnityEngine.Object.DestroyImmediate(lo);
            return (mb, srcOf);
        }

        static int Tris(Mesh m) { int n = 0; for (int s = 0; s < m.subMeshCount; s++) n += (int)m.GetIndexCount(s) / 3; return n; }

        static void BuildMeshes(Car c, string dir, Vector2 darkUV, Vector2 paintUV, Info info, System.Text.StringBuilder log)
        {
            string k = c.cfg.kind;
            var body = new MB(3);
            var wheels = new MB[4]; for (int w = 0; w < 4; w++) wheels[w] = new MB(1);
            for (int t = 0; t < c.I.Length; t += 3)
            {
                int cl = c.triCls[t / 3]; if (cl < 0) continue;
                if (cl == 3)
                {
                    for (int q = 2; q >= 0; q--) body.T[0].Add(body.Add(c.P[c.I[t + q]], Vector3.up, paintUV));
                    continue;
                }
                if (cl >= 10) { var wb = wheels[cl - 10]; for (int q = 0; q < 3; q++) wb.T[0].Add(wb.Src(c, c.I[t + q], c.wc[cl - 10])); continue; }
                for (int q = 0; q < 3; q++) body.T[cl].Add(body.Src(c, c.I[t + q], Vector3.zero));
            }
            // dark discs behind the wheels (the tyre cut leaves the arch open)
            if (darkUV.x < 0) darkUV = new Vector2(.001f, .001f);
            for (int w = 0; w < 4; w++)
            {
                var ctr = c.wc[w]; float sx = Mathf.Sign(ctr.x);
                var cc = new Vector3(sx * (c.wInner - .01f), ctr.y, ctr.z); var nrm = new Vector3(sx, 0, 0);
                int i0 = body.Add(cc, nrm, darkUV); const int SEG = 20;
                for (int s = 0; s < SEG; s++)
                {
                    float a0 = s * Mathf.PI * 2 / SEG, a1 = (s + 1) * Mathf.PI * 2 / SEG; float r = c.wr * 1.02f;
                    int j0 = body.Add(cc + new Vector3(0, Mathf.Sin(a0) * r, Mathf.Cos(a0) * r), nrm, darkUV);
                    int j1 = body.Add(cc + new Vector3(0, Mathf.Sin(a1) * r, Mathf.Cos(a1) * r), nrm, darkUV);
                    // facing outwards (+sx): both windings so it reads from either side
                    body.T[0].Add(i0); body.T[0].Add(j0); body.T[0].Add(j1);
                    body.T[0].Add(i0); body.T[0].Add(j1); body.T[0].Add(j0);
                }
            }
            var b0 = body.ToMesh(k + "_lod0");
            var w0 = wheels.Select((wb, i) => wb.ToMesh($"{k}_wheel{i}_lod0")).ToArray();
            var bodySrc = new List<int>(); var wheelSrc = new[] { new List<int>(), new List<int>(), new List<int>(), new List<int>() }; var allSrc = new List<int>();
            for (int t = 0; t < c.triCls.Length; t++) { int cl = c.triCls[t]; if (cl < 0 || cl == 3) continue; allSrc.Add(t); if (cl >= 10) wheelSrc[cl - 10].Add(t); else bodySrc.Add(t); }
            Mesh Dm(List<int> src, Vector3 off, int n, bool subs, string name) { var m = Decimate(c, src, off, n, subs).mb.ToMesh(name); return m; }
            var b1 = Dm(bodySrc, Vector3.zero, 7200, true, k + "_lod1");
            var b2 = Decimate(c, bodySrc, Vector3.zero, 1800, true, .045f).mb.ToMesh(k + "_lod2");
            var w1 = Enumerable.Range(0, 4).Select(i => Dm(wheelSrc[i], c.wc[i], 220, false, $"{k}_wheel{i}_lod1")).ToArray();
            var w2 = Enumerable.Range(0, 4).Select(i => Dm(wheelSrc[i], c.wc[i], 50, false, $"{k}_wheel{i}_lod2")).ToArray();
            Save(b0, $"{dir}/{k}_lod0.asset"); Save(b1, $"{dir}/{k}_lod1.asset"); Save(b2, $"{dir}/{k}_lod2.asset");
            for (int i = 0; i < 4; i++) { Save(w0[i], $"{dir}/{k}_wheel{i}_lod0.asset"); Save(w1[i], $"{dir}/{k}_wheel{i}_lod1.asset"); Save(w2[i], $"{dir}/{k}_wheel{i}_lod2.asset"); }
            info.tris0 = Tris(b0) + w0.Sum(Tris); info.tris1 = Tris(b1) + w1.Sum(Tris); info.tris2 = Tris(b2) + w2.Sum(Tris);
            var far = FarMesh(c, allSrc, k); info.trisFar = Tris(far);
            Save(far, $"{dir}/{k}_far.asset");
            if (c.hasBar) Save(BarMesh(c, k), $"{dir}/{k}_bar.asset");
        }

        /// far mesh: everything (wheels in place) decimated to ~450 triangles, per-triangle colour from its nearest source triangle;
        /// submesh 0 = palette (vertex colour, TrafficBuilder maps it onto the far palette), 1 = paint, 2 = tail lamps
        static Mesh FarMesh(Car c, List<int> src, string k)
        {
            var (mb, srcOf) = Decimate(c, src, Vector3.zero, 450, false, .12f);
            var far = new MB(3); int S = MASK;
            for (int t = 0; t < srcOf.Length; t++)
            {
                var uv = CentroidUV(c, srcOf[t] * 3);
                int x = Mathf.Clamp((int)(uv.x * S), 0, S - 1), y = Mathf.Clamp((int)(uv.y * S), 0, S - 1);
                int cl = c_cls[y * S + x]; var col = c.tex.At(uv);
                if (cl == GLASS) col = new Color(.08f, .09f, .1f); else if (cl == TYRE) col = new Color(.04f, .04f, .04f);
                int sub = cl == TAIL ? 2 : cl == PAINT ? 1 : 0;
                var cc = new Color(Mathf.Round(col.r * 12) / 12, Mathf.Round(col.g * 12) / 12, Mathf.Round(col.b * 12) / 12, 1);
                for (int q = 0; q < 3; q++) { int i = mb.T[0][t * 3 + q]; int j = far.Add(mb.V[i], mb.N[i], Vector2.zero); far.C.Add(cc); far.T[sub].Add(j); }
            }
            return far.ToMesh(k + "_far");
        }

        /// our light bar: base plate (submesh 3), centre panel with the wordmark (0, UVs on the livery sheet's white panel), two lenses (1, 2)
        static Mesh BarMesh(Car c, string k)
        {
            var mb = new MB(4);
            float W = Mathf.Min(c.cfg.barWidth, c.bar.size.x * 1.05f), D = c.cfg.barDepth, Hh = c.cfg.barHeight;
            // over the front feet of Tripo's box, on its underside
            float zc = c.bar.max.z - D * .6f, y0 = c.bar.min.y - .01f;
            void Box(int sub, Vector3 mn, Vector3 mx, Vector4 uvRect)
            {
                var ctr = (mn + mx) / 2; var e = (mx - mn) / 2;
                for (int f = 0; f < 6; f++)
                {
                    var n = f == 0 ? Vector3.right : f == 1 ? Vector3.left : f == 2 ? Vector3.up : f == 3 ? Vector3.down : f == 4 ? Vector3.forward : Vector3.back;
                    var u = Mathf.Abs(n.y) > .5f ? Vector3.right : Vector3.Cross(Vector3.up, n).normalized * -1;
                    if (f == 4) u = Vector3.left; if (f == 5) u = Vector3.right;
                    var v = Vector3.Cross(n, u);
                    if (Vector3.Dot(v, Vector3.up) < 0 && Mathf.Abs(n.y) < .5f) v = -v;
                    var fc = ctr + Vector3.Scale(n, e);
                    float eu = Mathf.Abs(Vector3.Dot(u, e)), ev = Mathf.Abs(Vector3.Dot(v, e));
                    int b = mb.V.Count;
                    for (int q = 0; q < 4; q++)
                    {
                        float su = q == 0 || q == 3 ? -1 : 1, sv = q < 2 ? -1 : 1;
                        bool text = (f == 4 || f == 5) && uvRect.z > 0;
                        var uv = text ? new Vector2(uvRect.x + (su * .5f + .5f) * uvRect.z, uvRect.y + (sv * .5f + .5f) * uvRect.w) : new Vector2(.97f, .99f);
                        mb.Add(fc + u * su * eu + v * sv * ev, n, uv);
                    }
                    // winding: (0,1,2)(0,2,3) must face n
                    var fnn = Vector3.Cross(mb.V[b + 1] - mb.V[b], mb.V[b + 2] - mb.V[b]);
                    if (Vector3.Dot(fnn, n) > 0) { mb.T[sub].AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 }); }
                    else { mb.T[sub].AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 }); }
                }
            }
            float lens = W * .36f;
            Box(3, new Vector3(-W / 2 - .01f, y0, zc - D / 2 - .01f), new Vector3(W / 2 + .01f, y0 + .035f, zc + D / 2 + .01f), Vector4.zero);
            Box(0, new Vector3(-W / 2 + lens, y0 + .035f, zc - D / 2 + .01f), new Vector3(W / 2 - lens, y0 + Hh * .85f, zc + D / 2 - .01f), new Vector4(.5f, .75f, .5f, .25f));
            Box(1, new Vector3(-W / 2, y0 + .035f, zc - D / 2), new Vector3(-W / 2 + lens, y0 + Hh, zc + D / 2), Vector4.zero);
            Box(2, new Vector3(W / 2 - lens, y0 + .035f, zc - D / 2), new Vector3(W / 2, y0 + Hh, zc + D / 2), Vector4.zero);
            return mb.ToMesh(k + "_bar");
        }

        // ------------------------------------------------------------------ materials
        static Material Mat(string path, Shader sh)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
            else m.shader = sh;
            m.enableInstancing = true; m.name = Path.GetFileNameWithoutExtension(path);
            return m;
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        static void BuildMaterials(Car c, string dir, Texture2D alb, Texture2D mask, float paintRef)
        {
            var sh = Shader.Find("Magistrala/TripoCar");
            var cfg = c.cfg; string k = cfg.kind;
            var liv = cfg.livery != "none" ? AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Textures/Vehicles/Livery/{cfg.livery}.png") : null;
            float L = c.L, H = c.H;
            Vector4 V(float[] a, Vector4 def) => a != null && a.Length > 0 ? new Vector4(a[0], a.Length > 1 ? a[1] : 0, a.Length > 2 ? a[2] : 0, a.Length > 3 ? a[3] : 0) : def;
            void Setup(Material m, Color paint, bool livery)
            {
                m.SetTexture("_BaseMap", alb); m.SetTexture("_MaskMap", mask); m.SetFloat("_PaintRef", paintRef);
                m.SetColor("_PaintColor", paint); m.SetColor("_LampColor", Color.black);
                m.SetFloat("_Livery", livery && liv ? 1 : 0);
                if (liv) m.SetTexture("_LiveryTex", liv);
                m.SetVector("_LivBand", V(cfg.band, new Vector4(.44f * L, -.47f * L, .43f * H, .54f * H)));
                m.SetVector("_LivSide", V(cfg.sideText, new Vector4(.08f * L, .33f * H, .23f * L, 0)));
                var top = V(cfg.bonnetText, new Vector4(.34f * L, .55f * c.hw * 2, 0, 0)); top.z = cfg.bonnetMirror ? 1 : 0;
                m.SetVector("_LivTop", top);
                m.SetVector("_LivRear", V(cfg.rearText, new Vector4(.6f * H, .26f * c.hw * 2, 0, 0)));
                m.SetColor("_LivColor", Hex(cfg.liveryTint));
                EditorUtility.SetDirty(m);
            }
            for (int i = 0; i < cfg.colors.Length; i++) Setup(Mat($"{dir}/{k}_paint_{cfg.colors[i].TrimStart('#')}.mat", sh), Hex(cfg.colors[i]), true);
            var head = Mat($"{dir}/{k}_head.mat", sh); Setup(head, Hex(cfg.colors[0]), false);
            var headOn = Mat($"{dir}/{k}_head_on.mat", sh); Setup(headOn, Hex(cfg.colors[0]), false); headOn.SetColor("_LampColor", new Color(4f, 3.9f, 3.6f));
            var tail = Mat($"{dir}/{k}_tail.mat", sh); Setup(tail, Hex(cfg.colors[0]), false); tail.SetColor("_LampColor", new Color(.25f, .01f, .005f));
            var tailOn = Mat($"{dir}/{k}_tail_on.mat", sh); Setup(tailOn, Hex(cfg.colors[0]), false); tailOn.SetColor("_LampColor", new Color(7f, .3f, .15f));
            var wheel = Mat($"{dir}/{k}_wheel.mat", sh); Setup(wheel, Color.white, false);
            if (c.hasBar)
            {
                var lit = Shader.Find("Universal Render Pipeline/Lit");
                var bc = Hex(cfg.beaconColor);
                var panel = Mat($"{dir}/{k}_bar_panel.mat", lit); panel.SetTexture("_BaseMap", liv); panel.SetColor("_BaseColor", Color.white); panel.SetFloat("_Smoothness", .6f);
                var plate = Mat($"{dir}/{k}_bar_base.mat", lit); plate.SetColor("_BaseColor", new Color(.03f, .03f, .035f)); plate.SetFloat("_Smoothness", .4f);
                var lens = Mat($"{dir}/{k}_bar_lens.mat", lit); lens.SetColor("_BaseColor", bc * .55f); lens.SetFloat("_Smoothness", .92f);
                lens.EnableKeyword("_EMISSION"); lens.SetColor("_EmissionColor", bc * .05f); lens.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                var lensOn = Mat($"{dir}/{k}_bar_lens_on.mat", lit); lensOn.CopyPropertiesFromMaterial(lens); lensOn.enableInstancing = true;
                lensOn.EnableKeyword("_EMISSION"); lensOn.SetColor("_EmissionColor", new Color(bc.r, bc.g, bc.b) * 9f); lensOn.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                foreach (var m in new[] { panel, plate, lens, lensOn }) EditorUtility.SetDirty(m);
            }
            foreach (var m in new[] { head, headOn, tail, tailOn, wheel }) EditorUtility.SetDirty(m);
        }

        // ------------------------------------------------------------------ preview (edit mode, the open scene's lights / sky)
        /// a 3 x 2 sheet of the imported car (f34, left, right, rear, top, r34) at lod, written to png; lamps: lit variants
        public static string Preview(string kind, string png, int lod = 0, bool lamps = false, int size = 640)
        {
            string dir = $"{OUT}/{kind}";
            var cfg = LoadConfig($"{SRC}/{kind}.json");
            var info = JsonUtility.FromJson<Info>(File.ReadAllText($"{dir}/{kind}_info.json"));
            var root = new GameObject("tripo_preview"); root.transform.position = new Vector3(0, 800, 0);
            Mesh M(string n) => AssetDatabase.LoadAssetAtPath<Mesh>($"{dir}/{kind}_{n}.asset");
            Material Mt(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{kind}_{n}.mat");
            void Part(Mesh m, Material[] mats, Vector3 at)
            {
                var g = new GameObject("p"); g.transform.SetParent(root.transform, false); g.transform.localPosition = at;
                g.AddComponent<MeshFilter>().sharedMesh = m; g.AddComponent<MeshRenderer>().sharedMaterials = mats;
            }
            var paint = Mt("paint_" + cfg.colors[0].TrimStart('#'));
            Part(M("lod" + lod), new[] { paint, lamps ? Mt("head_on") : Mt("head"), lamps ? Mt("tail_on") : Mt("tail") }, Vector3.zero);
            for (int w = 0; w < 4; w++) Part(M($"wheel{w}_lod{lod}"), new[] { Mt("wheel") }, info.wheels[w]);
            if (info.bar) Part(M("bar"), new[] { Mt("bar_panel"), lamps ? Mt("bar_lens_on") : Mt("bar_lens"), Mt("bar_lens"), Mt("bar_base") }, Vector3.zero);
            // ground plate
            var gp = GameObject.CreatePrimitive(PrimitiveType.Plane); gp.transform.SetParent(root.transform, false); gp.transform.localScale = Vector3.one * 2;
            var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")); gm.SetColor("_BaseColor", new Color(.35f, .35f, .36f)); gp.GetComponent<MeshRenderer>().sharedMaterial = gm;
            var camGo = new GameObject("tripo_cam"); var cam = camGo.AddComponent<Camera>(); cam.fieldOfView = 35; cam.nearClipPlane = .1f; cam.farClipPlane = 3000;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.62f, .68f, .75f);
            var rt = new RenderTexture(size, size * 3 / 4, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt;
            var sheet = new Texture2D(size * 3, size * 3 / 4 * 2, TextureFormat.RGB24, false);
            var ctr = root.transform.position + Vector3.up * .8f; float d = info.len * 1.35f;
            Vector3[] eyes = { new Vector3(.75f, .25f, .8f), new Vector3(-1, .12f, 0), new Vector3(1, .12f, 0), new Vector3(0, .25f, -1), new Vector3(.01f, 1, .3f), new Vector3(.7f, .3f, -.8f) };
            for (int i = 0; i < 6; i++)
            {
                cam.transform.position = ctr + eyes[i].normalized * d; cam.transform.LookAt(ctr);
                cam.Render();
                RenderTexture.active = rt; var tmp = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                tmp.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tmp.Apply();
                sheet.SetPixels((i % 3) * rt.width, (1 - i / 3) * rt.height, rt.width, rt.height, tmp.GetPixels());
                UnityEngine.Object.DestroyImmediate(tmp);
            }
            RenderTexture.active = null; sheet.Apply();
            File.WriteAllBytes(png, sheet.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(sheet); cam.targetTexture = null; rt.Release();
            UnityEngine.Object.DestroyImmediate(camGo); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(gm);
            return png;
        }

        // ------------------------------------------------------------------ traffic kinds (TrafficBuilder)
        /// every imported Tripo car as a TrafficKind; farColor: TrafficBuilder's palette (vertex colour -> palette uv), urp: its far material helper
        public static List<TrafficKind> TrafficKinds(Func<Color, Vector2> farColor, Func<string, Color, float, Color?, Material> urp)
        {
            var list = new List<TrafficKind>();
            foreach (var cfgPath in Directory.GetFiles(SRC, "*.json").OrderBy(x => x))
            {
                var cfg = LoadConfig(cfgPath); string dir = $"{OUT}/{cfg.kind}";
                var infoAsset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{dir}/{cfg.kind}_info.json");
                if (!infoAsset) { Debug.LogWarning($"[tripo] {cfg.kind}: not imported (Magistrala/Auti: uvezi Tripo modele)"); continue; }
                var info = JsonUtility.FromJson<Info>(infoAsset.text); string k = cfg.kind;
                Mesh M(string n) => AssetDatabase.LoadAssetAtPath<Mesh>($"{dir}/{k}_{n}.asset");
                Material Mt(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{k}_{n}.mat");
                TrafficPart[] Parts(int lod)
                {
                    var body = M("lod" + lod);
                    var ps = new List<TrafficPart>
                    {
                        new TrafficPart { mesh = body, sub = 0, paint = true, local = Matrix4x4.identity },
                        new TrafficPart { mesh = body, sub = 1, mat = Mt("head"), matOn = Mt("head_on"), lamp = 4, local = Matrix4x4.identity },
                        new TrafficPart { mesh = body, sub = 2, mat = Mt("tail"), matOn = Mt("tail_on"), lamp = 1, local = Matrix4x4.identity },
                    };
                    for (int w = 0; w < 4; w++)
                        ps.Add(new TrafficPart { mesh = M($"wheel{w}_lod{lod}"), sub = 0, mat = Mt("wheel"), local = Matrix4x4.Translate(info.wheels[w]), wheel = true, radius = info.wheelR });
                    if (info.bar && M("bar"))
                    {
                        var bar = M("bar");
                        ps.Add(new TrafficPart { mesh = bar, sub = 0, mat = Mt("bar_panel"), local = Matrix4x4.identity });
                        ps.Add(new TrafficPart { mesh = bar, sub = 3, mat = Mt("bar_base"), local = Matrix4x4.identity });
                        ps.Add(new TrafficPart { mesh = bar, sub = 1, mat = Mt("bar_lens"), matOn = Mt("bar_lens_on"), lamp = 5, local = Matrix4x4.identity });
                        ps.Add(new TrafficPart { mesh = bar, sub = 2, mat = Mt("bar_lens"), matOn = Mt("bar_lens_on"), lamp = 6, local = Matrix4x4.identity });
                    }
                    return ps.ToArray();
                }
                var kd = new TrafficKind
                {
                    name = k, len = info.len, halfW = info.halfW, h = info.h, vMul = cfg.vMul, a = cfg.a, b = cfg.b, T = cfg.T, weight = cfg.weight,
                    lod0 = Parts(0), lod1 = Parts(1), lod2 = Parts(2), lodNear = 25, lodMid = 45, beaconShare = info.bar ? cfg.beaconShare : 0,
                };
                kd.paints = cfg.colors.Select(cc => Mt("paint_" + cc.TrimStart('#'))).ToArray(); kd.paintW = cfg.colorWeights.ToArray();
                // far mesh: its vertex colours onto the shared palette
                var far = M("far");
                if (far)
                {
                    var cols = far.colors; var uv = new Vector2[cols.Length];
                    for (int i = 0; i < cols.Length; i++) uv[i] = farColor(cols[i]);
                    far.uv = uv; EditorUtility.SetDirty(far);
                    kd.farMesh = far;
                    kd.farPaints = cfg.colors.Select(cc => urp($"far_paint_{cc.TrimStart('#')}", Hex(cc), .62f, null)).ToArray();
                    kd.farTail = urp("far_tail", new Color(.55f, .08f, .07f), .5f, null);
                    kd.farTailOn = urp("far_tail_on", new Color(.8f, .1f, .08f), .5f, new Color(6f, .25f, .15f));
                }
                list.Add(kd);
            }
            return list;
        }
    }
}
