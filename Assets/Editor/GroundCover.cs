// Tlo mjesta (docs/audit-stari.md): what lies on the ground between the houses of Kaštela, from Assets/Editor/tlo-kastela.json
// (Tools/tlo/tlo_build.py: OSM + the DGU orthophoto ground build of the web game; derived vectors only):
//   pitches   OSM leisure=pitch + the photo-measured ones (Valovo's small pitches): the ground levelled under them (Tiles.Height
//             hook: flat over the pitch + 2.5 m, blended over 8 m), natural grass with mowing stripes / artificial turf / court /
//             clay surface, white markings by sport (FIFA lines scaled to the pitch, tennis, basketball / handball), goals with
//             nets, basketball hoops, a tennis net, a chain-link fence round it (5 m ball-stop behind the goals of a big pitch,
//             a 4 m cage round a small one), a gap on one long side; BoxColliders on fence runs, posts, poles and nets
//   surfaces  car parks (asphalt + bay lines), pools (water + coping), courts, playground rubber from the ground build, draped
//             on the terrain (+4–6 cm), dropped where a building, a street, a model road or the riva is
//   Hard()    1 m raster of everything above: no grass detail (GraphicsPass.Details), no tree (WorldBuilder.Trees) there
// Built by Magistrala/Rekviziti: postavi (PropsBuilder.Build calls Build first: benches and parked cars then avoid the fences).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static partial class GroundCover
    {
        public const string PATH = "Assets/Editor/tlo-kastela.json";
        const string OUT = WorldBuilder.GEN + "/Tlo";
        public const double FLAT = 2.5, BLEND = 8;

        public class Pitch
        {
            public string id, name, sport, surface; public Color photo = new Color(.5f, .55f, .5f);
            public double cx, cz, ux, uz, A, B, level; public double[] ring;
            public double bx0, bz0, bx1, bz1; public int group;
            public bool Big => A >= 80;
            public bool Small => A < 50;
            public bool Goals => sport == "soccer" || sport == "multi" || sport == "handball" || sport == "futsal" || sport == "" || sport.Contains("soccer");
            public bool Hoops => sport == "basketball" || (sport == "multi" && A < 50);
        }
        public class Surf { public string k; public double[] v; public int[] t; }
        static List<Pitch> pitches; static List<Surf> surf;
        public static List<(string id, string name, double[] ring)> Parks = new List<(string, string, double[])>();
        public static List<double[]> Cars = new List<double[]>(), Hedges = new List<double[]>();
        static bool loaded;

        public static IReadOnlyList<Pitch> Pitches { get { Load(); return pitches ?? new List<Pitch>(); } }
        public static void Reset() { loaded = false; pitches = null; surf = null; hard = null; }

        static bool Load()
        {
            if (loaded) return pitches != null;
            loaded = true;
            if (!File.Exists(PATH)) return false;
            var j = MiniJson.O(MiniJson.Parse(File.ReadAllText(PATH)));
            double[] Arr(object o) => MiniJson.L(o).Select(MiniJson.D).ToArray();
            var ps = new List<Pitch>();
            foreach (var o in MiniJson.L(j["pitches"]))
            {
                var d = MiniJson.O(o); var r = Arr(d["rect"]);
                var p = new Pitch { id = (string)d["id"], name = (string)d["name"] ?? "", sport = ((string)d["sport"] ?? "").ToLowerInvariant(), surface = (string)d["surface"] ?? "grass", cx = r[0], cz = r[1], ux = r[2], uz = r[3], A = r[4], B = r[5], ring = Arr(d["ring"]) };
                if (MiniJson.G(d, "photo") is List<object> c && c.Count == 3) p.photo = new Color(MiniJson.F(c[0]) / 255f, MiniJson.F(c[1]) / 255f, MiniJson.F(c[2]) / 255f);
                double R = Math.Sqrt(p.A * p.A + p.B * p.B) / 2 + FLAT + BLEND;
                p.bx0 = p.cx - R; p.bx1 = p.cx + R; p.bz0 = p.cz - R; p.bz1 = p.cz + R;
                ps.Add(p);
            }
            // one level per group of pitches whose flat zones touch (Valovo: the main pitch and its two small ones)
            for (int i = 0; i < ps.Count; i++) ps[i].group = i;
            for (int i = 0; i < ps.Count; i++)
                for (int k = i + 1; k < ps.Count; k++)
                    if (ps[i].bx0 < ps[k].bx1 && ps[k].bx0 < ps[i].bx1 && ps[i].bz0 < ps[k].bz1 && ps[k].bz0 < ps[i].bz1 && Dist(ps[i], ps[k].cx, ps[k].cz) < Math.Sqrt(ps[k].A * ps[k].A + ps[k].B * ps[k].B) / 2 + 6)
                    { int g = ps[k].group; foreach (var q in ps) if (q.group == g) q.group = ps[i].group; }
            foreach (var grp in ps.GroupBy(p => p.group))
            {
                var hs = new List<double>();
                foreach (var p in grp)
                    for (double a = -p.A / 2; a <= p.A / 2; a += 4)
                        for (double b = -p.B / 2; b <= p.B / 2; b += 4)
                            hs.Add(Tiles.Raw(p.cx + p.ux * a - p.uz * b, p.cz + p.uz * a + p.ux * b));
                hs.Sort(); double lv = hs.Count > 0 ? hs[hs.Count / 2] : 0;
                foreach (var p in grp) p.level = Math.Max(lv, .8);
            }
            pitches = ps;
            surf = new List<Surf>();
            foreach (var o in MiniJson.L(j["surf"])) { var d = MiniJson.O(o); surf.Add(new Surf { k = (string)d["k"], v = Arr(d["v"]), t = MiniJson.L(d["t"]).Select(x => (int)MiniJson.D(x)).ToArray() }); }
            Parks.Clear();
            foreach (var o in MiniJson.L(j["parks"])) { var d = MiniJson.O(o); Parks.Add(((string)d["id"], (string)d["name"] ?? "", Arr(d["ring"]))); }
            Cars = MiniJson.L(j["cars"]).Select(Arr).ToList();
            Hedges = MiniJson.L(j["hedges"]).Select(Arr).ToList();
            LoadMly();                       // the Mapillary car parks (GroundCoverMapillary.cs)
            return true;
        }

        /// distance outside a pitch's flat zone (≤ 0 inside), game frame
        static double Dist(Pitch p, double gx, double gz, double grow = FLAT)
        {
            double dx = gx - p.cx, dz = gz - p.cz;
            double a = Math.Abs(dx * p.ux + dz * p.uz) - (p.A / 2 + grow), b = Math.Abs(-dx * p.uz + dz * p.ux) - (p.B / 2 + grow);
            if (a <= 0 && b <= 0) return Math.Max(a, b);
            return Math.Sqrt(Math.Max(a, 0) * Math.Max(a, 0) + Math.Max(b, 0) * Math.Max(b, 0));
        }

        /// the Tiles.Height hook: the ground levelled under the pitches
        public static double Level(double gx, double gz, double raw)
        {
            if (!Load()) return raw;
            foreach (var p in pitches)
            {
                if (gx < p.bx0 || gx > p.bx1 || gz < p.bz0 || gz > p.bz1) continue;
                double d = Dist(p, gx, gz);
                if (d >= BLEND) continue;
                if (d <= 0) return p.level;
                double t = d / BLEND; t = t * t * (3 - 2 * t);
                return p.level + (raw - p.level) * t;
            }
            return raw;
        }

        // ------------------------------------------------------------------ hard raster (1 m cells over the core)
        const double HX0 = -10240, HZ0 = -7168, HC = 1; const int HNX = 10240, HNZ = 3072;
        static System.Collections.BitArray hard;
        /// a pitch (+ its fence), a car park, pool, court or playground within pad m
        public static bool Hard(double gx, double gz, double pad = 0)
        {
            if (hard == null) { if (!Load()) return false; Raster(); }
            int k = (int)Math.Ceiling(pad / HC), i0 = (int)Math.Floor((gx - HX0) / HC), j0 = (int)Math.Floor((gz - HZ0) / HC);
            for (int j = j0 - k; j <= j0 + k; j++)
                for (int i = i0 - k; i <= i0 + k; i++)
                    if (i >= 0 && j >= 0 && i < HNX && j < HNZ && hard[j * HNX + i]) return true;
            return false;
        }
        static void Raster()
        {
            hard = new System.Collections.BitArray(HNX * HNZ);
            void Mark(double x0, double z0, double x1, double z1, Func<double, double, bool> inside)
            {
                for (int j = Math.Max(0, (int)Math.Floor((z0 - HZ0) / HC)); j <= Math.Min(HNZ - 1, (int)Math.Floor((z1 - HZ0) / HC)); j++)
                    for (int i = Math.Max(0, (int)Math.Floor((x0 - HX0) / HC)); i <= Math.Min(HNX - 1, (int)Math.Floor((x1 - HX0) / HC)); i++)
                        if (inside(HX0 + (i + .5) * HC, HZ0 + (j + .5) * HC)) hard[j * HNX + i] = true;
            }
            foreach (var p in pitches) Mark(p.bx0, p.bz0, p.bx1, p.bz1, (x, z) => Dist(p, x, z) <= .5);
            MarkMly(Mark);                   // the Mapillary car parks that were built (GroundCoverMapillary.cs)
            foreach (var s in surf)
            {
                if (s.k == "paint") continue;
                for (int t = 0; t + 2 < s.t.Length; t += 3)
                {
                    double ax = s.v[s.t[t] * 2], az = s.v[s.t[t] * 2 + 1], bx = s.v[s.t[t + 1] * 2], bz = s.v[s.t[t + 1] * 2 + 1], cx = s.v[s.t[t + 2] * 2], cz = s.v[s.t[t + 2] * 2 + 1];
                    Mark(Math.Min(ax, Math.Min(bx, cx)) - 1, Math.Min(az, Math.Min(bz, cz)) - 1, Math.Max(ax, Math.Max(bx, cx)) + 1, Math.Max(az, Math.Max(bz, cz)) + 1,
                        (x, z) => InTri(x, z, ax, az, bx, bz, cx, cz, .5));
                }
            }
        }
        static bool InTri(double x, double z, double ax, double az, double bx, double bz, double cx, double cz, double pad)
        {
            double d1 = (x - bx) * (az - bz) - (ax - bx) * (z - bz), d2 = (x - cx) * (bz - cz) - (bx - cx) * (z - cz), d3 = (x - ax) * (cz - az) - (cx - ax) * (z - az);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            if (!(neg && pos)) return true;
            if (pad <= 0) return false;
            return SegD(x, z, ax, az, bx, bz) < pad || SegD(x, z, bx, bz, cx, cz) < pad || SegD(x, z, cx, cz, ax, az) < pad;
        }
        static double SegD(double x, double z, double ax, double az, double bx, double bz)
        {
            double vx = bx - ax, vz = bz - az, l2 = vx * vx + vz * vz, t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((x - ax) * vx + (z - az) * vz) / l2));
            double dx = x - ax - vx * t, dz = z - az - vz * t; return Math.Sqrt(dx * dx + dz * dz);
        }

        // ------------------------------------------------------------------ mesh building
        class MB
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<int> t = new List<int>();
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
            {
                int o = v.Count; v.Add(a); v.Add(b); v.Add(c); uv.Add(ua); uv.Add(ub); uv.Add(uc);
                if (Vector3.Cross(b - a, c - a).y >= 0) { t.Add(o); t.Add(o + 1); t.Add(o + 2); } else { t.Add(o); t.Add(o + 2); t.Add(o + 1); }
            }
            /// a quad facing n (a b c d around it)
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {
                int o = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); uv.Add(ua); uv.Add(ub); uv.Add(uc); uv.Add(ud);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0) t.AddRange(new[] { o, o + 1, o + 2, o, o + 2, o + 3 }); else t.AddRange(new[] { o, o + 2, o + 1, o, o + 3, o + 2 });
            }
            /// an oriented box: centre, half extents along right / up / forward
            public void Box(Vector3 c, Vector3 r, Vector3 u, Vector3 f)
            {
                Vector3 P(int i, int j, int k) => c + r * i + u * j + f * k;
                var z = Vector2.zero; var o = Vector2.one;
                Quad(P(1, -1, -1), P(1, -1, 1), P(1, 1, 1), P(1, 1, -1), r, z, new Vector2(1, 0), o, new Vector2(0, 1));
                Quad(P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), -r, z, new Vector2(1, 0), o, new Vector2(0, 1));
                Quad(P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), u, z, new Vector2(1, 0), o, new Vector2(0, 1));
                Quad(P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), -f, z, new Vector2(1, 0), o, new Vector2(0, 1));
                Quad(P(-1, -1, 1), P(-1, 1, 1), P(1, 1, 1), P(1, -1, 1), f, z, new Vector2(1, 0), o, new Vector2(0, 1));
            }
            /// a bar between two points (square section s)
            public void Bar(Vector3 a, Vector3 b, float s)
            {
                var f = (b - a); float L = f.magnitude; if (L < 1e-4f) return; f /= L;
                var r = Vector3.Cross(Mathf.Abs(f.y) > .9f ? Vector3.forward : Vector3.up, f).normalized; var u = Vector3.Cross(f, r);
                Box((a + b) * .5f, r * s / 2, u * s / 2, f * L / 2);
            }
        }

        static readonly Dictionary<(Material, int), MB> chunks = new Dictionary<(Material, int), MB>();
        static MB Get(Material m, double gx, double gz)
        {
            int key = ((int)Math.Floor(gx / 256) + 100) * 1000 + (int)Math.Floor(gz / 256) + 100;
            if (!chunks.TryGetValue((m, key), out var mb)) chunks[(m, key)] = mb = new MB();
            return mb;
        }

        static float GroundY(double gx, double gz, out bool blocked)
        {
            blocked = false; var u = W.U(gx, 0, gz);
            var hits = Physics.RaycastAll(new Vector3(u.x, 400, u.z), Vector3.down, 900, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (h.collider is TerrainCollider) return h.point.y;
                if (Under(h.collider.transform, "Tlo") || Under(h.collider.transform, "Props") || Under(h.collider.transform, "ParkedCars") || h.collider.attachedRigidbody) continue;
                blocked = true; return h.point.y; // a building, a street, a model road, the riva paving
            }
            return float.NaN;
        }
        static bool Under(Transform t, string name) { for (var p = t; p != null; p = p.parent) if (p.name == name && p.parent && p.parent.name == "World") return true; return false; }

        // ------------------------------------------------------------------ materials / textures
        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = Path.GetDirectoryName(p).Replace('\\', '/'); Dir(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(p)); } }
        static Texture2D TexGen(string name, int w, int h, Func<int, int, Color> f, bool alpha)
        {
            string p = $"{OUT}/{name}.png";
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h]; for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = f(x, y);
            t.SetPixels(px); File.WriteAllBytes(p, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(p);
            if (AssetImporter.GetAtPath(p) is TextureImporter imp)
            {
                imp.wrapMode = TextureWrapMode.Repeat; imp.alphaIsTransparency = alpha; imp.mipmapEnabled = true; imp.anisoLevel = 4; imp.maxTextureSize = 512;
                imp.textureCompression = TextureImporterCompression.Compressed; imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }
        static float N(int x, int y, int s) { unchecked { uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(s * 83492791); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return (h & 0xffff) / 65535f; } }
        static Material Mat(string name, Color c, float smooth, Texture2D tex = null, bool clip = false, bool twoSided = false)
        {
            string p = $"{OUT}/{name}.mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0); m.SetTexture("_BaseMap", tex);
            if (clip) { m.SetFloat("_AlphaClip", 1); m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_Cutoff", .45f); m.renderQueue = 2450; m.SetOverrideTag("RenderType", "TransparentCutout"); }
            if (twoSided) { m.SetFloat("_Cull", 0); m.doubleSidedGI = true; }
            m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
        }
        static Material turf, turfArt, courtGrey, clay, lines, posts, net, fence, fencePost, asphalt, paint, water, coping, rubber, tartan;
        static readonly Dictionary<string, Material> courts = new Dictionary<string, Material>();
        static void Materials()
        {
            Dir(OUT);
            // natural grass: fine noise, two mowing stripes per texture (u along the pitch: 1 texture = 10 m)
            var tg = TexGen("trava-teren", 256, 256, (x, y) =>
            {
                float n = .82f + .18f * N(x, y, 1) + .08f * (N(x / 4, y / 4, 2) - .5f) + .06f * (N(x / 16, y / 16, 3) - .5f);
                float stripe = x < 128 ? 1.07f : .94f; float v = n * stripe;
                return new Color(.31f * v, .43f * v, .22f * v, 1);
            }, false);
            var ta = TexGen("umjetna-trava", 256, 256, (x, y) =>
            {
                float n = .9f + .1f * N(x, y, 4) + .04f * (N(x / 8, y / 8, 5) - .5f); float stripe = x < 128 ? 1.04f : .96f; float v = n * stripe;
                return new Color(.17f * v, .31f * v, .19f * v, 1);
            }, false);
            var tn = TexGen("mreza", 128, 128, (x, y) => { bool l = x % 16 < 2 || y % 16 < 2; return new Color(1, 1, 1, l ? 1 : 0); }, true);
            var tf = TexGen("ograda-pletivo", 128, 128, (x, y) =>
            {
                // chain-link diamonds (6 cm mesh at 2 m per texture: 16 px per diamond)
                int a = (x + y) % 22, b = (x - y + 1100) % 22; bool w = a < 2 || b < 2; return new Color(1, 1, 1, w ? 1 : 0);
            }, true);
            turf = Mat("teren-trava", Color.white, .08f, tg); turfArt = Mat("teren-umjetna", Color.white, .18f, ta);
            courtGrey = Mat("teren-beton", new Color(.62f, .63f, .61f), .2f, WorldBuilder.Tex("concrete_grey_d.jpg"));
            clay = Mat("teren-zemlja", new Color(.72f, .45f, .33f), .05f, WorldBuilder.Tex("red_soil_d.jpg"));
            lines = Mat("teren-crte", new Color(.94f, .94f, .92f), .25f);
            posts = Mat("teren-stup", new Color(.95f, .95f, .95f), .55f);
            net = Mat("teren-mreza", new Color(.92f, .92f, .92f), .1f, tn, true, true);
            fence = Mat("ograda-zica", new Color(.42f, .46f, .44f), .35f, tf, true, true);
            fencePost = Mat("ograda-stup", new Color(.3f, .36f, .33f), .4f);
            asphalt = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.GEN + "/Materials/AsphaltFlat.mat") ?? GraphicsPass.AsphaltMaterial();
            paint = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.GEN + "/Materials/PaintFlat.mat") ?? GraphicsPass.PaintMaterial();
            water = Mat("bazen-voda", new Color(.25f, .68f, .82f), .93f);
            coping = Mat("bazen-rub", new Color(.86f, .85f, .81f), .3f, WorldBuilder.Tex("concrete_d.jpg"));
            rubber = Mat("igraliste-guma", new Color(.55f, .25f, .2f), .1f);
            tartan = Mat("staza-tartan", new Color(.62f, .28f, .22f), .15f);
            courts.Clear();
        }
        /// a hard court in the photo's colour (a little more saturated), shared per rounded colour
        static Material Court(Color c)
        {
            Color.RGBToHSV(c, out float h, out float s, out float v); var cc = Color.HSVToRGB(h, Mathf.Min(1, s * 1.6f), Mathf.Clamp(v * 1.05f, .25f, .8f));
            string key = $"{Mathf.RoundToInt(cc.r * 8)}{Mathf.RoundToInt(cc.g * 8)}{Mathf.RoundToInt(cc.b * 8)}";
            if (!courts.TryGetValue(key, out var m)) courts[key] = m = Mat("teren-podloga-" + key, cc, .25f, WorldBuilder.Tex("concrete_grey_d.jpg"));
            return m;
        }

        // ------------------------------------------------------------------ build
        static List<string> log;
        static Transform root, colRoot;
        public static int nPitches, nGoals, nHoops, nFenceM, nTris, nDropped;

        public static string Build(List<string> outLog = null)
        {
            Reset(); log = outLog ?? new List<string>();
            if (!Load()) { log.Add("[tlo] no " + PATH); return "no data"; }
            Physics.SyncTransforms();
            Materials(); chunks.Clear();
            var r = WorldBuilder.Root("Tlo"); root = r.transform;
            colRoot = new GameObject("kolajderi").transform; colRoot.SetParent(root, false); colRoot.gameObject.isStatic = true;
            nPitches = nGoals = nHoops = nFenceM = nTris = nDropped = 0;
            var models = WorldData.Models();
            Surfaces(models);
            MlyPrepare(models);              // the Mapillary car parks: ground grid and bays (their asphalt after the parked cars)
            foreach (var p in pitches) PitchBuild(p);
            int nm = Flush();
            var s = $"[tlo] {nPitches} pitches ({nGoals} goals, {nHoops} hoops, {nFenceM} m fence), {surf.Count} surfaces ({nTris} triangles, {nDropped} dropped), {nm} meshes";
            log.Add(s); return s;
        }

        static int Flush()
        {
            int n = 0;
            foreach (var kv in chunks)
            {
                var mb = kv.Value; if (mb.v.Count == 0) continue;
                var mesh = new Mesh { name = $"tlo-{kv.Key.Item1.name}-{kv.Key.Item2}", indexFormat = mb.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(mb.v); mesh.SetUVs(0, mb.uv); mesh.SetTriangles(mb.t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                string p = $"{OUT}/{mesh.name}.asset"; if (File.Exists(p)) AssetDatabase.DeleteAsset(p); AssetDatabase.CreateAsset(mesh, p);
                var go = new GameObject(mesh.name); go.isStatic = true; go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = kv.Key.Item1;
                // ground decals cast nothing; posts, nets and fences do
                bool flat = kv.Key.Item1 != posts && kv.Key.Item1 != net && kv.Key.Item1 != fence && kv.Key.Item1 != fencePost;
                mr.shadowCastingMode = flat ? ShadowCastingMode.Off : ShadowCastingMode.On;
                n++;
            }
            chunks.Clear(); AssetDatabase.SaveAssets();
            return n;
        }

        // ------------------------------------------------------------------ surfaces of the ground build
        static void Surfaces(List<ModelJ> models)
        {
            foreach (var s in surf)
            {
                Material m = s.k == "parking" ? asphalt : s.k == "paint" ? paint : s.k == "pool" ? water : s.k == "rubber" ? rubber : s.k == "tartan" ? tartan
                    : s.k == "clay" ? clay : s.k == "court" ? Court(new Color(.36f, .5f, .4f)) : s.k == "turf" ? turf : null;
                if (m == null) continue;
                float lift = s.k == "paint" ? .075f : s.k == "pool" ? .03f : .055f;
                int nv = s.v.Length / 2; var ys = new float[nv]; var ok = new bool[nv];
                for (int i = 0; i < nv; i++) { ys[i] = GroundY(s.v[2 * i], s.v[2 * i + 1], out bool bl); ok[i] = !bl && !float.IsNaN(ys[i]); }
                if (s.k == "pool") { float lo = float.MaxValue; for (int i = 0; i < nv; i++) if (ok[i]) lo = Math.Min(lo, ys[i]); for (int i = 0; i < nv; i++) ys[i] = lo; }
                var edges = new Dictionary<(int, int), int>();
                for (int t = 0; t + 2 < s.t.Length; t += 3)
                {
                    int a = s.t[t], b = s.t[t + 1], c = s.t[t + 2];
                    double cx = (s.v[2 * a] + s.v[2 * b] + s.v[2 * c]) / 3, cz = (s.v[2 * a + 1] + s.v[2 * b + 1] + s.v[2 * c + 1]) / 3;
                    if (!ok[a] || !ok[b] || !ok[c] || StreetsBuilder.Covered(cx, cz, 0) || WorldData.SurfDist(models, cx, cz) < .5 || RivaBuilder.Class(cx, cz) != 0) { nDropped++; continue; }
                    var mb = Get(m, cx, cz);
                    Vector3 P(int i) => W.U(s.v[2 * i], ys[i] + lift, s.v[2 * i + 1]);
                    Vector2 UV(int i) => new Vector2((float)s.v[2 * i] / 4f, (float)s.v[2 * i + 1] / 4f);
                    mb.Tri(P(a), P(b), P(c), UV(a), UV(b), UV(c)); nTris++;
                    foreach (var e in new[] { (a, b), (b, c), (c, a) }) { var k = e.Item1 < e.Item2 ? e : (e.Item2, e.Item1); edges.TryGetValue(k, out int q); edges[k] = q + 1; }
                }
                if (s.k != "pool") continue;
                // pool coping: a 0.3 m stone strip on the outline (edges used by one triangle), a little above the water
                foreach (var e in edges.Where(e => e.Value == 1).Select(e => e.Key))
                {
                    var A = new Vector2((float)s.v[2 * e.Item1], (float)s.v[2 * e.Item1 + 1]); var B = new Vector2((float)s.v[2 * e.Item2], (float)s.v[2 * e.Item2 + 1]);
                    var d = B - A; if (d.magnitude < .05f) continue; var n = new Vector2(-d.y, d.x).normalized * .17f; float y = ys[e.Item1] + .09f;
                    var mb = Get(coping, A.x, A.y);
                    mb.Quad(W.U(A.x - n.x, y, A.y - n.y), W.U(B.x - n.x, y, B.y - n.y), W.U(B.x + n.x, y, B.y + n.y), W.U(A.x + n.x, y, A.y + n.y), Vector3.up, Vector2.zero, new Vector2(1, 0), Vector2.one, new Vector2(0, 1));
                }
            }
        }

        // ------------------------------------------------------------------ pitches
        /// game (a along, b across, height above the pitch) -> Unity
        static Vector3 PU(Pitch p, double a, double b, double y) => W.U(p.cx + p.ux * a - p.uz * b, p.level + y, p.cz + p.uz * a + p.ux * b);

        static void PitchBuild(Pitch p)
        {
            nPitches++;
            Material surfM = p.surface == "artificial_turf" ? turfArt : p.surface == "grass" ? turf : p.surface == "clay" ? clay
                : p.surface == "paved" || p.surface == "acrylic" || p.surface == "asphalt" || p.surface == "concrete" ? Court(p.photo) : turf;
            double ha = p.A / 2 + 1.5, hb = p.B / 2 + 1.5;    // the surface reaches the fence line
            const double S = 4;
            int na = Math.Max(1, (int)Math.Ceiling(2 * ha / S)), nb = Math.Max(1, (int)Math.Ceiling(2 * hb / S));
            for (int i = 0; i < na; i++)
                for (int k = 0; k < nb; k++)
                {
                    double a0 = -ha + 2 * ha * i / na, a1 = -ha + 2 * ha * (i + 1) / na, b0 = -hb + 2 * hb * k / nb, b1 = -hb + 2 * hb * (k + 1) / nb;
                    var mb = Get(surfM, p.cx, p.cz);
                    Vector2 UV(double a, double b) => new Vector2((float)(a / 10), (float)(b / 10));
                    mb.Quad(PU(p, a0, b0, .04), PU(p, a1, b0, .04), PU(p, a1, b1, .04), PU(p, a0, b1, .04), Vector3.up, UV(a0, b0), UV(a1, b0), UV(a1, b1), UV(a0, b1));
                }
            // markings
            var lm = Get(lines, p.cx, p.cz);
            foreach (var (a0, b0, a1, b1) in Markings(p))
            {
                var A = new Vector2((float)a0, (float)b0); var B = new Vector2((float)a1, (float)b1); var d = B - A; if (d.magnitude < .05f) continue;
                var n = new Vector2(-d.y, d.x).normalized * .06f;
                lm.Quad(PU(p, A.x - n.x, A.y - n.y, .055), PU(p, B.x - n.x, B.y - n.y, .055), PU(p, B.x + n.x, B.y + n.y, .055), PU(p, A.x + n.x, A.y + n.y, .055), Vector3.up, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
            }
            double ea = p.A / 2 - 1, eb = p.B / 2 - 1;
            if (p.Goals && p.sport != "basketball")
            {
                float gw = p.A >= 90 ? 7.32f : p.A >= 45 ? 5f : 3f, gh = p.A >= 90 ? 2.44f : 2f;
                foreach (int sg in new[] { -1, 1 }) Goal(p, sg * ea, sg, gw, gh);
            }
            if (p.Hoops) foreach (int sg in new[] { -1, 1 }) Hoop(p, sg * ea, sg);
            if (p.sport == "tennis") TennisNet(p, eb);
            Fence(p);
        }

        /// line segments (a0, b0, a1, b1) in the pitch frame: the ground build's pitch_lines (FIFA markings scaled to the length)
        static List<(double, double, double, double)> Markings(Pitch p)
        {
            var S = new List<(double, double, double, double)>();
            double a = p.A / 2 - 1, b = p.B / 2 - 1; if (a <= 2 || b <= 2) return S;
            void L(double a0, double b0, double a1, double b1) => S.Add((a0, b0, a1, b1));
            void Arc(double a0, double b0, double r, double t0, double t1, int n) { for (int k = 0; k < n; k++) { double u0 = t0 + (t1 - t0) * k / n, u1 = t0 + (t1 - t0) * (k + 1) / n; L(a0 + r * Math.Cos(u0), b0 + r * Math.Sin(u0), a0 + r * Math.Cos(u1), b0 + r * Math.Sin(u1)); } }
            L(-a, -b, a, -b); L(a, -b, a, b); L(a, b, -a, b); L(-a, b, -a, -b);
            string s = p.sport;
            if (s == "tennis")
            {
                L(0, -b, 0, b); L(-a, -b * .75, a, -b * .75); L(-a, b * .75, a, b * .75);
                L(-a * .55, 0, a * .55, 0); L(-a * .55, -b * .75, -a * .55, b * .75); L(a * .55, -b * .75, a * .55, b * .75);
            }
            else if (s == "basketball" || s == "volleyball" || s == "handball" || (s == "multi" && p.Small))
            {
                L(0, -b, 0, b); Arc(0, 0, Math.Min(1.8, b * .3), 0, 2 * Math.PI, 20);
                if (s == "basketball" || s == "multi") foreach (int sg in new[] { -1, 1 }) Arc(sg * a, 0, Math.Min(6.75, b * .9), sg > 0 ? Math.PI / 2 : -Math.PI / 2, sg > 0 ? 3 * Math.PI / 2 : Math.PI / 2, 20);
                if (s == "handball") foreach (int sg in new[] { -1, 1 }) Arc(sg * a, 0, Math.Min(6, b * .8), sg > 0 ? Math.PI / 2 : -Math.PI / 2, sg > 0 ? 3 * Math.PI / 2 : Math.PI / 2, 20);
            }
            else
            {
                double k = Math.Min(1.0, a / 52.5);
                L(0, -b, 0, b); Arc(0, 0, 9.15 * k, 0, 2 * Math.PI, 32);
                foreach (int sg in new[] { -1, 1 })
                {
                    double pa = 16.5 * k, pb = Math.Min(20.15 * k, b - .5), ga = 5.5 * k, gb = Math.Min(9.16 * k, b - .5);
                    L(sg * a, -pb, sg * (a - pa), -pb); L(sg * (a - pa), -pb, sg * (a - pa), pb); L(sg * (a - pa), pb, sg * a, pb);
                    L(sg * a, -gb, sg * (a - ga), -gb); L(sg * (a - ga), -gb, sg * (a - ga), gb); L(sg * (a - ga), gb, sg * a, gb);
                    double ps = 11 * k; L(sg * (a - ps) - .15, 0, sg * (a - ps) + .15, 0);
                    Arc(sg * (a - ps), 0, 9.15 * k, sg > 0 ? Math.PI - .93 : -.93, sg > 0 ? Math.PI + .93 : .93, 10);
                }
            }
            return S;
        }

        static void Col(Vector3 c, Vector3 size, Quaternion rot, string name)
        {
            var go = new GameObject(name); go.isStatic = true; go.transform.SetParent(colRoot, false);
            go.transform.SetPositionAndRotation(c, rot); go.AddComponent<BoxCollider>().size = size;
        }
        static Quaternion Rot(Pitch p) { var f = W.U(p.cx + p.ux, 0, p.cz + p.uz) - W.U(p.cx, 0, p.cz); return Quaternion.LookRotation(new Vector3(f.x, 0, f.z).normalized); }

        static void Goal(Pitch p, double aLine, int sg, float w, float h)
        {
            var pm = Get(posts, p.cx, p.cz); var nm = Get(net, p.cx, p.cz);
            double d = h * .8, hw = w / 2;
            Vector3 G(double out_, double b, double y) => PU(p, aLine + sg * out_, b, y);
            pm.Bar(G(0, -hw, 0), G(0, -hw, h), .12f); pm.Bar(G(0, hw, 0), G(0, hw, h), .12f); pm.Bar(G(0, -hw - .06, h), G(0, hw + .06, h), .12f);
            pm.Bar(G(d, -hw, .02), G(d, hw, .02), .05f); pm.Bar(G(0, -hw, h), G(d * .45, -hw, h * .92), .04f); pm.Bar(G(0, hw, h), G(d * .45, hw, h * .92), .04f);
            pm.Bar(G(d * .45, -hw, h * .92), G(d, -hw, 0), .04f); pm.Bar(G(d * .45, hw, h * .92), G(d, hw, 0), .04f); pm.Bar(G(d * .45, -hw, h * .92), G(d * .45, hw, h * .92), .04f);
            var outN = PU(p, sg, 0, 0) - PU(p, 0, 0, 0);
            float W2 = w / 2;
            nm.Quad(G(0, -hw, h), G(0, hw, h), G(d * .45, hw, h * .92), G(d * .45, -hw, h * .92), Vector3.up, new Vector2(0, 0), new Vector2(W2, 0), new Vector2(W2, .5f), new Vector2(0, .5f));
            nm.Quad(G(d * .45, -hw, h * .92), G(d * .45, hw, h * .92), G(d, hw, 0), G(d, -hw, 0), outN, new Vector2(0, 0), new Vector2(W2, 0), new Vector2(W2, h / 2), new Vector2(0, h / 2));
            foreach (int s2 in new[] { -1, 1 })
                nm.Quad(G(0, s2 * hw, 0), G(0, s2 * hw, h), G(d * .45, s2 * hw, h * .92), G(d, s2 * hw, 0), Vector3.Cross(outN, Vector3.up) * s2, Vector2.zero, new Vector2(0, h / 2), new Vector2((float)d * .22f, h / 2), new Vector2((float)d / 2, 0));
            var rot = Rot(p);
            Col(G(0, -hw, h / 2), new Vector3(.14f, h, .14f), rot, "vrata-stup"); Col(G(0, hw, h / 2), new Vector3(.14f, h, .14f), rot, "vrata-stup");
            Col(G(0, 0, h), new Vector3(w, .14f, .14f), rot, "vrata-precka"); Col(G(d * .8, 0, h * .3), new Vector3(w, h * .6f, .08f), rot, "vrata-mreza");
            nGoals++;
        }

        static void Hoop(Pitch p, double aLine, int sg)
        {
            var pm = Get(fencePost, p.cx, p.cz); var wm = Get(posts, p.cx, p.cz);
            Vector3 G(double in_, double b, double y) => PU(p, aLine - sg * in_, b, y);
            pm.Bar(G(-1.0, 0, 0), G(-1.0, 0, 3.95), .14f); pm.Bar(G(-1.0, 0, 3.8), G(1.15, 0, 3.8), .1f);
            wm.Box(G(1.2, 0, 3.4), (G(1.2, .9, 3.4) - G(1.2, 0, 3.4)), Vector3.up * .53f, (G(1.25, 0, 3.4) - G(1.2, 0, 3.4)) * .6f);
            var rm = Get(fencePost, p.cx, p.cz);
            for (int k = 0; k < 10; k++)
            {
                double t0 = 2 * Math.PI * k / 10, t1 = 2 * Math.PI * (k + 1) / 10;
                rm.Bar(G(1.575 + .23 * Math.Cos(t0), .23 * Math.Sin(t0), 3.05), G(1.575 + .23 * Math.Cos(t1), .23 * Math.Sin(t1), 3.05), .025f);
            }
            Col(G(-1.0, 0, 1.97), new Vector3(.16f, 3.95f, .16f), Rot(p), "kos-stup");
            nHoops++;
        }

        static void TennisNet(Pitch p, double eb)
        {
            var pm = Get(fencePost, p.cx, p.cz); var nm = Get(net, p.cx, p.cz);
            double hb = eb * .95;
            pm.Bar(PU(p, 0, -hb - .3, 0), PU(p, 0, -hb - .3, 1.07), .08f); pm.Bar(PU(p, 0, hb + .3, 0), PU(p, 0, hb + .3, 1.07), .08f);
            nm.Quad(PU(p, 0, -hb, .05), PU(p, 0, hb, .05), PU(p, 0, hb, 1.02), PU(p, 0, -hb, 1.02), PU(p, 1, 0, 0) - PU(p, 0, 0, 0), Vector2.zero, new Vector2((float)hb, 0), new Vector2((float)hb, .5f), new Vector2(0, .5f));
            Col(PU(p, 0, 0, .5), new Vector3(.06f, 1f, (float)(2 * hb + .6)), Rot(p) * Quaternion.Euler(0, 90, 0), "tenis-mreza");
        }

        /// chain-link fence on the pitch's outline + 1.5 m: 3 m panels, a panel dropped where a building / street / model road /
        /// riva is (rays) or another pitch's surface continues (Valovo: the small pitches beside the big one); a 3 m gap at the
        /// middle of one long side; colliders per run of panels
        static void Fence(Pitch p)
        {
            double ha = p.A / 2 + 1.5, hb = p.B / 2 + 1.5;
            float hLong = p.sport == "tennis" ? 3.5f : p.Small ? 4f : 2.2f, hShort = p.sport == "tennis" ? 3.5f : p.Small ? 4f : p.Big ? 5f : 3f;
            var corners = new[] { (-ha, -hb), (ha, -hb), (ha, hb), (-ha, hb) };
            var fm = Get(fence, p.cx, p.cz); var pm = Get(fencePost, p.cx, p.cz);
            for (int side = 0; side < 4; side++)
            {
                var (a0, b0) = corners[side]; var (a1, b1) = corners[(side + 1) % 4];
                bool longSide = side % 2 == 0; float h = longSide ? hLong : hShort;
                double L = Math.Sqrt((a1 - a0) * (a1 - a0) + (b1 - b0) * (b1 - b0)); int n = Math.Max(1, (int)Math.Ceiling(L / 3));
                var keep = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    double t = (i + .5) / n, a = a0 + (a1 - a0) * t, b = b0 + (b1 - b0) * t;
                    bool gate = side == 0 && Math.Abs(i - (n - 1) / 2.0) < .6 && !p.Small;
                    keep[i] = !gate && PanelFree(p, a, b);
                }
                int run = -1;
                for (int i = 0; i <= n; i++)
                {
                    if (i < n && keep[i]) { if (run < 0) run = i; continue; }
                    if (run < 0) continue;
                    // panels run .. i-1
                    double ta = (double)run / n, tb = (double)i / n;
                    Vector3 A(double t, double y) => PU(p, a0 + (a1 - a0) * t, b0 + (b1 - b0) * t, y);
                    for (int k = run; k <= i; k++) pm.Bar(A((double)k / n, 0), A((double)k / n, h + .05), .06f);
                    pm.Bar(A(ta, h), A(tb, h), .045f);
                    var nrm = Vector3.Cross(A(tb, 0) - A(ta, 0), Vector3.up).normalized;
                    float len = (A(tb, 0) - A(ta, 0)).magnitude;
                    fm.Quad(A(ta, .03), A(tb, .03), A(tb, h), A(ta, h), nrm, new Vector2(0, 0), new Vector2(len / 2, 0), new Vector2(len / 2, h / 2), new Vector2(0, h / 2));
                    var mid = (A(ta, h / 2) + A(tb, h / 2)) / 2; var dir = (A(tb, 0) - A(ta, 0)).normalized;
                    Col(mid, new Vector3(.1f, h, len), Quaternion.LookRotation(dir), "ograda");
                    nFenceM += (int)len;
                    run = -1;
                }
            }
        }

        static bool PanelFree(Pitch p, double a, double b)
        {
            var u = PU(p, a, b, 0); var g = W.G(u);
            if (StreetsBuilder.Covered(g.x, g.y, 0) || RivaBuilder.Class(g.x, g.y) != 0) return false;
            GroundY(g.x, g.y, out bool blocked); if (blocked) return false;
            // inside another pitch's surface (a shared edge): no fence between them
            foreach (var q in pitches) if (q != p && Dist(q, g.x, g.y, 1.4) < -.2) return false;
            return true;
        }
    }
}
