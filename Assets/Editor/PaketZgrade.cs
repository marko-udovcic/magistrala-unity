// UKRAINIAN CITY pack buildings (Assets/OdesaUkraine, NOT in git) as game buildings: halls / sheds and school-like civic blocks
// on the matching Kaštela footprints (tools/world/zgrade-paket/kandidati.json, Codex).
//   Bake():  per pack model one merged mesh per LOD (LOD0 capped, LOD1 ~25 %, LOD2 a closed height-field hull that also casts the
//            shadow and is the collider), every material of the pack folded into ONE texture array pair (Magistrala/Paket:
//            opaque + cutout material), Cyrillic signs / ads / decals stripped. -> Assets/Models/Paket/ (committed) and
//            Assets/Models/Paket/paket-modeli.json (footprint, front side, tris).
//   Match(): fits the models to the candidates' OBBs (±15 % per axis, halls also tiled 1-4 along x 1-2 across), front towards
//            the nearest carriageway, mirrored / tinted variants, protected sites (firm warehouse, stops) kept -> Assets/Editor/zgrade-paket.json
//   Place(): called at the end of BuildingsFromTiles.Build (World/Buildings/paket); BuildingsFromTiles skips the replaced ids.
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Magistrala.EditorTools
{
    public static class PaketZgrade
    {
        public const string SRC = "Assets/OdesaUkraine/", OUT = "Assets/Models/Paket/", PLACE_JSON = "Assets/Editor/zgrade-paket.json";
        public const string META = OUT + "paket-modeli.json";
        public static string Candidates => W.BD + "/tools/world/zgrade-paket/kandidati.json";
        public const int TEX = 1024;

        /// the models, kind (hall / civic) and LOD0 triangle cap
        public static readonly (string name, bool hall, int cap)[] Models =
        {
            ("Building_E", true, 15000), ("Building_F", true, 15000), ("Building_G", true, 15000), ("Building_5", true, 15000), ("Building_6", true, 15000),
            // Marko's free CGTrader models (Royalty Free, modified; sources in Assets/Models/Paket/Izvor, not in git): "Industrial
            // Building 49 PBR" by CGBuildings (a grey sheet-metal hall) and "Free Modern Low-Poly Apartment Building" by Pro-pixel
            ("Hala49", true, 15000), ("Stambena", false, 35000),
            ("Building_2", false, 30000), ("Building_8", false, 30000), ("Building_10", false, 30000), ("Building_H", false, 30000), ("Building_9", false, 30000),
        };
        /// the source of a model: the pack's prefab, or a model file of ours
        public static string Source(string name) => name == "Hala49" ? OUT + "Izvor/hala49.fbx" : name == "Stambena" ? OUT + "Izvor/stambena.glb" : SRC + "Prefabs/Buildings/" + name + ".prefab";
        /// never baked: Cyrillic signage / ads / house numbers, projected decals, transparent bits, ground pieces under the building
        static readonly string[] StripWords = { "advert", "sign", "address", "poster", "banner", "number", "label", "graffiti", "decal", "plate", "boombarrier", "pavement", "curb", "dirt", "grass" };

        // ------------------------------------------------------------------ materials -> layers
        public sealed class Lay { public int layer; public Color tint; public bool tri, cut; public float scale; public Vector2 tiling; public float gloss; public string key; }
        public sealed class LayerSrc { public string baseTex, normalTex, maskTex; public Vector2 smooth; public bool cut; public Color mean; public float gloss = .2f; public bool lighten; }

        static readonly Dictionary<string, int> layerOf = new Dictionary<string, int>();
        internal static readonly List<LayerSrc> layers = new List<LayerSrc>();
        static readonly Dictionary<Material, Lay> lays = new Dictionary<Material, Lay>();

        static string TexPath(Material m, string prop) { if (!m.HasProperty(prop)) return null; var t = m.GetTexture(prop); return t ? AssetDatabase.GetAssetPath(t) : null; }
        static bool Has(Material m, string p) => m.HasProperty(p);

        public static bool Stripped(Material m)
        {
            if (!m) return true;
            string s = m.shader.name.ToLowerInvariant(), n = m.name.ToLowerInvariant();
            if (s.Contains("decal") || s.Contains("lit_transparent")) return true;
            foreach (var w in StripWords) if (n.Contains(w)) return true;
            var bt = TexPath(m, "_BaseColor") ?? TexPath(m, "_Base_Color_MN") ?? TexPath(m, "_BaseMap");
            if (bt != null) { var b = Path.GetFileName(bt).ToLowerInvariant(); foreach (var w in StripWords) if (b.Contains(w)) return true; }
            return false;
        }

        /// the layer of a pack material (registers its textures on first use)
        public static Lay LayOf(Material m)
        {
            if (lays.TryGetValue(m, out var l)) return l;
            // Hala49 comes without its textures (the FBX only): the pack's corrugated sheet metal, triplanar, light grey
            if (m.name == "IBuilding49")
            {
                var ms = AssetDatabase.LoadAssetAtPath<Material>(SRC + "Materials/Unique/MetalSheet_B.mat");
                if (ms) { var b = LayOf(ms); return lays[m] = new Lay { layer = b.layer, tri = true, scale = b.tri ? b.scale : .5f, tiling = Vector2.one, tint = new Color(.6f, .62f, .62f).linear, gloss = 1, key = b.key }; }
            }
            string sh = m.shader.name;
            bool blend = sh.Contains("TriplanarBlend"), urp = sh.StartsWith("Universal Render Pipeline");
            string bt, nt, mt; Color c; bool tri; float ws; Vector2 tiling, sm;
            if (blend)
            {
                bt = TexPath(m, "_Base_Color_MN"); nt = TexPath(m, "_Normal_Map_MN"); mt = TexPath(m, "_Mask_Map_MN");
                c = m.GetColor("_Color_MN"); tri = m.GetFloat("_Triplanar_MN") > .5f; ws = m.GetFloat("_World_Scale_MN");
                var t4 = m.GetVector("_Tiling_MN"); tiling = new Vector2(t4.x, t4.y); var s4 = m.GetVector("_Smoothness_Remap_MN"); sm = new Vector2(s4.x, s4.y);
            }
            else if (Has(m, "baseColorTexture"))
            {
                // glTFast (glTF-pbrMetallicRoughness): base colour factor x texture, normal; roughness factor
                bt = TexPath(m, "baseColorTexture"); nt = TexPath(m, "normalTexture"); mt = null;
                c = Has(m, "baseColorFactor") ? m.GetColor("baseColorFactor") : Color.white; tri = false; ws = 1; tiling = Vector2.one;
                float rgh = Has(m, "roughnessFactor") ? m.GetFloat("roughnessFactor") : .8f; sm = new Vector2(1 - rgh, 1 - rgh);
                // glass (balcony railings, windows): opaque, dark blue-grey and glossy
                if (m.renderQueue >= 3000 || c.a < .9f) { c = new Color(.36f, .42f, .46f); sm = new Vector2(.85f, .85f); }
                // the apartment block's charcoal render / dark trims (untextured colours): a light render instead (Kaštela: white,
                // light grey, warm beige by the variants); the ochre / wood accents stay
                else if (.3f * c.r + .59f * c.g + .11f * c.b < .3f) c = Color.Lerp(c, new Color(.9f, .88f, .84f), .85f);
            }
            else if (urp)
            {
                bt = TexPath(m, "_BaseMap") ?? TexPath(m, "_MainTex"); nt = TexPath(m, "_BumpMap"); mt = null;
                c = Has(m, "_BaseColor") ? m.GetColor("_BaseColor") : Has(m, "_Color") ? m.GetColor("_Color") : Color.white;
                tri = false; ws = 1; tiling = Vector2.one; sm = new Vector2(0, Has(m, "_Smoothness") ? m.GetFloat("_Smoothness") : .2f);
            }
            else
            {
                bt = TexPath(m, "_BaseColor"); nt = TexPath(m, "_Normal"); mt = TexPath(m, "_MaskMap");
                c = Has(m, "_Color") ? m.GetColor("_Color") : Color.white; tri = Has(m, "_Triplanar") && m.GetFloat("_Triplanar") > .5f; ws = Has(m, "_World_Scale") ? m.GetFloat("_World_Scale") : 1;
                var t4 = Has(m, "_Tiling") ? m.GetVector("_Tiling") : Vector4.one; tiling = new Vector2(t4.x, t4.y);
                var s4 = Has(m, "_SmoothnessRemap") ? m.GetVector("_SmoothnessRemap") : new Vector4(0, .5f); sm = new Vector2(s4.x, s4.y);
            }
            bool cut = sh.Contains("Alpha");
            string key = (bt ?? "n:" + (nt ?? "-")) + (cut ? "|cut" : "");
            if (!layerOf.TryGetValue(key, out int li)) { li = layers.Count; layerOf[key] = li; layers.Add(new LayerSrc { baseTex = bt, normalTex = nt, maskTex = mt, smooth = sm, cut = cut }); }
            // the material's tint: alpha of the pack's colours is 0 on the blend graphs (unused)
            c.a = 1;
            l = new Lay { layer = li, tint = c.linear, tri = tri, cut = cut, scale = ws <= 0 ? 1 : ws, tiling = tiling, gloss = 1, key = key };
            return lays[m] = l;
        }

        // ------------------------------------------------------------------ mesh baking
        internal sealed class Geo
        {
            public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>();
            public List<Vector4> d = new List<Vector4>(); public List<Color> c = new List<Color>(); public List<int>[] sub = { new List<int>(), new List<int>() };
            public int Tris => (sub[0].Count + sub[1].Count) / 3;
        }
        internal sealed class Part { public Vector3[] v, n; public Vector2[] uv; public int[] tri; public Lay lay; public float area; public Bounds b; }

        internal static List<Part> Parts(GameObject inst, StringBuilder log, bool dropGround = true)
        {
            var res = new List<Part>(); int nStrip = 0;
            foreach (var r in inst.GetComponentsInChildren<MeshRenderer>(false))
            {
                var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null; if (!mesh || !r.enabled) continue;
                var M = r.localToWorldMatrix; bool flip = M.determinant < 0; var NM = M.inverse.transpose;
                var V = mesh.vertices; var N = mesh.normals; var UV = mesh.uv;
                var mats = r.sharedMaterials;
                for (int sm = 0; sm < mesh.subMeshCount; sm++)
                {
                    var mat = mats[Math.Min(sm, mats.Length - 1)];
                    if (Stripped(mat)) { nStrip++; continue; }
                    var lay = LayOf(mat);
                    var tr = mesh.GetTriangles(sm); if (tr.Length == 0) continue;
                    var p = new Part { lay = lay, v = new Vector3[tr.Length], n = new Vector3[tr.Length], uv = new Vector2[tr.Length], tri = new int[tr.Length] };
                    for (int i = 0; i < tr.Length; i += 3)
                    {
                        int a = tr[i], b = tr[i + (flip ? 2 : 1)], c = tr[i + (flip ? 1 : 2)];
                        int[] ix = { a, b, c };
                        for (int k = 0; k < 3; k++)
                        {
                            p.v[i + k] = M.MultiplyPoint3x4(V[ix[k]]);
                            p.n[i + k] = N.Length > 0 ? ((Vector3)(NM * (Vector4)N[ix[k]])).normalized : Vector3.up;
                            p.uv[i + k] = UV.Length > 0 ? UV[ix[k]] : Vector2.zero;
                            p.tri[i + k] = i + k;
                        }
                        p.area += Vector3.Cross(p.v[i + 1] - p.v[i], p.v[i + 2] - p.v[i]).magnitude * .5f;
                    }
                    p.b = new Bounds(p.v[0], Vector3.zero); foreach (var q in p.v) p.b.Encapsulate(q);
                    // ground pieces round the building (aprons, kerbs): our terrain / yards do that
                    if (dropGround && p.b.max.y < .35f && p.b.size.y < .3f) { nStrip++; continue; }
                    res.Add(p);
                }
            }
            log.Append($" parts {res.Count}, stripped submeshes {nStrip};");
            return res;
        }

        internal static void Emit(Geo g, Part p, float ground, float dirt)
        {
            var L = p.lay; int sub = L.cut ? 1 : 0;
            for (int i = 0; i < p.v.Length; i += 3)
            {
                Vector3 a = p.v[i], b = p.v[i + 1], c = p.v[i + 2];
                var fn = Vector3.Cross(b - a, c - a); float ax = Mathf.Abs(fn.x), ay = Mathf.Abs(fn.y), az = Mathf.Abs(fn.z);
                for (int k = 0; k < 3; k++)
                {
                    var q = p.v[i + k]; Vector2 uv;
                    if (L.tri)
                    {
                        // the pack's triplanar graphs: world metres x scale, per face its dominant axis (u horizontal, v up on walls)
                        uv = ay >= ax && ay >= az ? new Vector2(q.x, q.z) : ax >= az ? new Vector2(q.z, q.y) : new Vector2(q.x, q.y);
                        uv *= L.scale;
                    }
                    else uv = p.uv[i + k];
                    uv = Vector2.Scale(uv, L.tiling);
                    g.sub[sub].Add(g.v.Count);
                    g.v.Add(q); g.n.Add(p.n[i + k]); g.uv.Add(uv); g.c.Add(L.tint);
                    g.d.Add(new Vector4(L.layer, layers[L.layer].gloss, q.y - ground, dirt));
                }
            }
        }

        internal static Mesh ToMesh(Geo g, string name)
        {
            Weld(g);
            var m = new Mesh { name = name, indexFormat = g.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(g.v); m.SetNormals(g.n); m.SetUVs(0, g.uv); m.SetUVs(1, g.d); m.SetColors(g.c);
            m.subMeshCount = 2; m.SetTriangles(g.sub[0], 0); m.SetTriangles(g.sub[1], 1);
            m.RecalculateBounds(); m.RecalculateTangents(); m.Optimize();
            return m;
        }

        /// merge identical vertices (the triplanar faces were emitted unwelded)
        static void Weld(Geo g)
        {
            var map = new Dictionary<(Vector3, Vector3, Vector2, Vector4, Color), int>(); var idx = new int[g.v.Count];
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var d = new List<Vector4>(); var c = new List<Color>();
            for (int i = 0; i < g.v.Count; i++)
            {
                var k = (g.v[i], g.n[i], g.uv[i], g.d[i], g.c[i]);
                if (!map.TryGetValue(k, out int j)) { j = v.Count; map[k] = j; v.Add(g.v[i]); n.Add(g.n[i]); uv.Add(g.uv[i]); d.Add(g.d[i]); c.Add(g.c[i]); }
                idx[i] = j;
            }
            g.v = v; g.n = n; g.uv = uv; g.d = d; g.c = c;
            foreach (var s in g.sub) for (int i = 0; i < s.Count; i++) s[i] = idx[s[i]];
        }

        /// the parts kept under a triangle cap: the most detailed (triangles per m²) go first; glass / walls / roofs stay
        internal static List<Part> Cap(List<Part> parts, int cap)
        {
            int total = parts.Sum(p => p.tri.Length / 3); if (total <= cap) return parts;
            var order = parts.OrderByDescending(p => p.tri.Length / 3f / Mathf.Max(.05f, p.area)).ToList();
            var drop = new HashSet<Part>();
            foreach (var p in order) { if (total <= cap) break; if (p.area > 25) continue; drop.Add(p); total -= p.tri.Length / 3; }
            return parts.Where(p => !drop.Contains(p)).ToList();
        }

        // ------------------------------------------------------------------ the far level: a closed height-field hull
        const float CELLH = 1f;
        static Geo Hull(List<Part> parts, Bounds bb, int plainLayer, out float fill, bool skirt = false)
        {
            int nx = Mathf.CeilToInt(bb.size.x / CELLH) + 1, nz = Mathf.CeilToInt(bb.size.z / CELLH) + 1;
            var h = new float[nx + 1, nz + 1]; var col = new Color[nx, nz]; var top = new float[nx, nz];
            for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) top[i, j] = -1;
            for (int i = 0; i <= nx; i++) for (int j = 0; j <= nz; j++) h[i, j] = -1;
            // wall colour: area-weighted mean of the near-vertical faces' layer colour x tint
            Color wall = Color.black; float wA = 0;
            foreach (var p in parts)
            {
                if (p.lay.cut) continue;
                var mean = layers[p.lay.layer].mean * p.lay.tint;
                for (int t = 0; t < p.v.Length; t += 3)
                {
                    Vector3 a = p.v[t], b = p.v[t + 1], c = p.v[t + 2];
                    var fn = Vector3.Cross(b - a, c - a); float ar = fn.magnitude * .5f; if (ar < 1e-5f) continue; fn.Normalize();
                    if (Mathf.Abs(fn.y) < .3f) { wall += mean * ar; wA += ar; continue; }
                    if (fn.y < 0) continue;
                    // rasterise the upward face into cell centres and corners (max height)
                    float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x)), z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                    for (int i = Mathf.Max(0, Mathf.FloorToInt((x0 - bb.min.x) / CELLH * 2)); i <= Mathf.Min(2 * nx, Mathf.CeilToInt((x1 - bb.min.x) / CELLH * 2)); i++)
                        for (int j = Mathf.Max(0, Mathf.FloorToInt((z0 - bb.min.z) / CELLH * 2)); j <= Mathf.Min(2 * nz, Mathf.CeilToInt((z1 - bb.min.z) / CELLH * 2)); j++)
                        {
                            float px = bb.min.x + i * CELLH / 2, pz = bb.min.z + j * CELLH / 2;
                            if (!Bary(px, pz, a, b, c, out float y)) continue;
                            if ((i & 1) == 0 && (j & 1) == 0) { int ci = i / 2, cj = j / 2; if (ci <= nx && cj <= nz) h[ci, cj] = Mathf.Max(h[ci, cj], y); }
                            if ((i & 1) == 1 && (j & 1) == 1) { int ci = i / 2, cj = j / 2; if (ci < nx && cj < nz && y > top[ci, cj]) { top[ci, cj] = y; col[ci, cj] = mean; } }
                        }
                }
            }
            if (wA > 0) wall /= wA; else wall = new Color(.5f, .5f, .5f);
            var g = new Geo(); int covered = 0;
            bool In(int i, int j) => i >= 0 && j >= 0 && i < nx && j < nz && top[i, j] > .5f;
            float H(int i, int j) { float m = h[i, j]; if (m < 0) { for (int di = -1; di <= 0; di++) for (int dj = -1; dj <= 0; dj++) if (In(i + di, j + dj)) m = Mathf.Max(m, top[i + di, j + dj]); } return m; }
            float Hc(int i, int j, float self) { float m = h[i, j]; return Mathf.Abs(m - self) > 1.2f ? self : m; }
            Vector3 P(int i, int j, float y) => new Vector3(bb.min.x + i * CELLH, y, bb.min.z + j * CELLH);
            void Q(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color k, Vector3 nrm)
            {
                foreach (var q in new[] { a, b, c, a, c, d }) { g.sub[0].Add(g.v.Count); g.v.Add(q); g.n.Add(nrm); g.uv.Add(new Vector2(q.x + q.z, q.y) * .1f); g.c.Add(k); g.d.Add(new Vector4(plainLayer, .3f, q.y, .3f)); }
            }
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (!In(i, j)) continue; covered++;
                    float t = top[i, j];
                    Vector3 a = P(i, j, Hc(i, j, t)), b = P(i, j + 1, Hc(i, j + 1, t)), c = P(i + 1, j + 1, Hc(i + 1, j + 1, t)), d = P(i + 1, j, Hc(i + 1, j, t));
                    if (!skirt) Q(a, b, c, d, col[i, j], Vector3.up);
                    else { a.y = b.y = c.y = d.y = bb.min.y + .03f; }
                    // walls where the neighbour is empty (down to below ground) or a step down
                    void Side(int oi, int oj, Vector3 e0, Vector3 e1, Vector3 nrm)
                    {
                        if (skirt && In(oi, oj)) return;
                        float bot = In(oi, oj) ? Mathf.Max(-1.5f, top[oi, oj]) : -1.5f; if (In(oi, oj) && top[oi, oj] >= t - .5f) return;
                        Q(new Vector3(e0.x, bot, e0.z), new Vector3(e1.x, bot, e1.z), e1, e0, wall, nrm);
                    }
                    Side(i - 1, j, b, a, Vector3.left); Side(i + 1, j, d, c, Vector3.right); Side(i, j - 1, a, d, Vector3.back); Side(i, j + 1, c, b, Vector3.forward);
                }
            fill = covered * CELLH * CELLH / Mathf.Max(1, bb.size.x * bb.size.z);
            return g;
        }
        static bool Bary(float px, float pz, Vector3 a, Vector3 b, Vector3 c, out float y)
        {
            y = 0; float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z); if (Mathf.Abs(d) < 1e-7f) return false;
            float l1 = ((b.z - c.z) * (px - c.x) + (c.x - b.x) * (pz - c.z)) / d, l2 = ((c.z - a.z) * (px - c.x) + (a.x - c.x) * (pz - c.z)) / d, l3 = 1 - l1 - l2;
            if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) return false;
            y = l1 * a.y + l2 * b.y + l3 * c.y; return true;
        }

        // ------------------------------------------------------------------ textures
        internal static Color32[] Read(string path, int size, Color32 fallback)
        {
            var px = new Color32[size * size];
            if (path == null || !File.Exists(path)) { for (int i = 0; i < px.Length; i++) px[i] = fallback; return px; }
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true); t.LoadImage(File.ReadAllBytes(path));
            Texture src = t; RenderTexture prev = null;
            int w = t.width;
            while (w > size)
            {
                w = Mathf.Max(size, w / 2);
                var rt = RenderTexture.GetTemporary(w, w, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); rt.filterMode = FilterMode.Bilinear;
                Graphics.Blit(src, rt); if (prev) RenderTexture.ReleaseTemporary(prev); prev = rt; src = rt;
            }
            if (src == t && t.width != size)
            {
                var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear); Graphics.Blit(t, rt); prev = rt; src = rt;
            }
            if (src is RenderTexture r)
            {
                var o = new Texture2D(size, size, TextureFormat.RGBA32, false, true); var a = RenderTexture.active; RenderTexture.active = r;
                o.ReadPixels(new Rect(0, 0, size, size), 0, 0); o.Apply(); RenderTexture.active = a; px = o.GetPixels32(); Object.DestroyImmediate(o); RenderTexture.ReleaseTemporary(r);
            }
            else px = t.GetPixels32();
            Object.DestroyImmediate(t);
            return px;
        }

        public static Texture2DArray MakeArray(List<Color32[]> src, int size, bool linear, string path)
        {
            var arr = new Texture2DArray(size, size, src.Count, TextureFormat.DXT5, true, linear);
            for (int l = 0; l < src.Count; l++)
            {
                var t = new Texture2D(size, size, TextureFormat.RGBA32, true, linear); t.SetPixels32(src[l]); t.Apply(true);
                EditorUtility.CompressTexture(t, TextureFormat.DXT5, TextureCompressionQuality.Normal);
                for (int m = 0; m < t.mipmapCount; m++) arr.SetPixelData(t.GetPixelData<byte>(m), m, l);
                Object.DestroyImmediate(t);
            }
            arr.wrapMode = TextureWrapMode.Repeat; arr.filterMode = FilterMode.Trilinear; arr.anisoLevel = 4;
            arr.Apply(false, false);
            var old = AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
            if (old) { EditorUtility.CopySerialized(arr, old); Object.DestroyImmediate(arr); EditorUtility.SetDirty(old); return old; }
            AssetDatabase.CreateAsset(arr, path); return arr;
        }

        /// the layers' albedo (base x AO) and normal (r x, g y, b cutout alpha) pixels; mean colour and smoothness per layer
        public static (List<Color32[]> alb, List<Color32[]> nor) LayerPixels(List<LayerSrc> L, int size)
        {
            var alb = new List<Color32[]>(); var nor = new List<Color32[]>();
            foreach (var s in L)
            {
                var a = Read(s.baseTex, size, new Color32(255, 255, 255, 255));
                var n = Read(s.normalTex, size, new Color32(128, 128, 255, 255));
                var m = Read(s.maskTex, size, new Color32(0, 255, 0, 64));
                var o = new Color32[a.Length]; double mr = 0, mg = 0, mb = 0, gs = 0;
                for (int i = 0; i < a.Length; i++)
                {
                    float ao = m[i].g / 255f, k = Mathf.Lerp(1, ao, .6f);
                    // the textures are sRGB: AO multiplies the linear value
                    Color c = new Color32(a[i].r, a[i].g, a[i].b, 255); var lin = c.linear * k; c = lin.gamma;
                    byte cut = s.cut ? a[i].a : (byte)255;
                    a[i] = new Color32((byte)Mathf.Clamp(c.r * 255, 0, 255), (byte)Mathf.Clamp(c.g * 255, 0, 255), (byte)Mathf.Clamp(c.b * 255, 0, 255), 255);
                    mr += lin.r; mg += lin.g; mb += lin.b; gs += Mathf.Lerp(s.smooth.x, s.smooth.y, m[i].a / 255f);
                    o[i] = new Color32(n[i].r, n[i].g, cut, 255);
                }
                s.mean = new Color((float)(mr / a.Length), (float)(mg / a.Length), (float)(mb / a.Length));
                // a dark facade (the apartment block's charcoal render) brought up to a light render: the variants tint it
                float lum = .3f * s.mean.r + .59f * s.mean.g + .11f * s.mean.b;
                if (s.lighten && lum < .3f)
                {
                    float kk = .5f / Mathf.Max(.02f, lum);
                    for (int i = 0; i < a.Length; i++) { var lc = ((Color)a[i]).linear * kk; var gc = lc.gamma; a[i] = new Color32((byte)Mathf.Clamp(gc.r * 255, 0, 255), (byte)Mathf.Clamp(gc.g * 255, 0, 255), (byte)Mathf.Clamp(gc.b * 255, 0, 255), 255); }
                    s.mean *= kk;
                }
                s.gloss = (float)(gs / a.Length);
                alb.Add(a); nor.Add(o);
            }
            return (alb, nor);
        }

        /// the layers as one JPG flipbook (COLS per row, the first layer top left), imported as a Texture2DArray (as BuildingTextures)
        public static Texture2DArray Strip(List<Color32[]> src, int size, bool srgb, string path, int q)
        {
            int cols = Mathf.Min(8, src.Count), rows = (src.Count + cols - 1) / cols, W = cols * size, H = rows * size;
            var px = new Color32[W * H]; for (int i = 0; i < px.Length; i++) px[i] = new Color32(128, 128, 128, 255);
            for (int l = 0; l < src.Count; l++)
            {
                int cx = l % cols, cy = rows - 1 - l / cols; // Texture2D rows run bottom-up
                for (int y = 0; y < size; y++) Array.Copy(src[l], y * size, px, (cy * size + y) * W + cx * size, size);
            }
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false); t.SetPixels32(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToJPG(q)); Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var im = (TextureImporter)AssetImporter.GetAtPath(path);
            var st = new TextureImporterSettings(); im.ReadTextureSettings(st);
            st.textureShape = TextureImporterShape.Texture2DArray; st.flipbookRows = rows; st.flipbookColumns = cols;
            st.sRGBTexture = srgb; st.mipmapEnabled = true; st.wrapMode = TextureWrapMode.Repeat; st.filterMode = FilterMode.Trilinear; st.aniso = 4;
            im.SetTextureSettings(st); im.maxTextureSize = 16384; im.textureCompression = TextureImporterCompression.Compressed;
            im.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2DArray>(path);
        }

        public static Material Mat(string name, bool cut, Texture2DArray alb, Texture2DArray nor, Color tint, float gain = 1)
        {
            string path = OUT + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Magistrala/Paket")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Magistrala/Paket");
            m.SetTexture("_Albedo", alb); m.SetTexture("_Normal", nor);
            m.SetTexture("_Grime", AssetDatabase.LoadAssetAtPath<Texture2D>(BuildingTextures.GrimeAtlas));
            m.SetFloat("_Gain", gain); m.SetColor("_Tint", tint);
            if (cut) { m.EnableKeyword("_ALPHATEST_ON"); m.SetFloat("_AlphaTest", 1); m.SetFloat("_Cull", 0); m.renderQueue = 2450; }
            else { m.DisableKeyword("_ALPHATEST_ON"); m.SetFloat("_AlphaTest", 0); m.SetFloat("_Cull", 2); m.renderQueue = -1; }
            m.enableInstancing = false; EditorUtility.SetDirty(m); return m;
        }

        /// the material variants (colour shift per placement against repeats): 0 as the pack, 1 warmer / lighter, 2 greyer
        public static readonly (string suffix, Color tint)[] Variants = { ("", new Color(1, 1, 1)), ("_topla", new Color(1.07f, 1.0f, .9f)), ("_siva", new Color(.93f, .95f, .97f)) };

        // ------------------------------------------------------------------ model metadata
        [Serializable] public sealed class ModelMeta { public string name; public bool hall; public float cx, cz, len, wid, ax, az, height, fill, frontX, frontZ; public int tris0, tris1, tris2, layers; }
        [Serializable] sealed class MetaFile { public List<ModelMeta> models = new List<ModelMeta>(); public List<string> layerKeys = new List<string>(); }

        /// footprint OBB (min-area rectangle of the convex hull) of the low vertices, model space
        static (Vector2 c, Vector2 axis, float len, float wid) Obb(IEnumerable<Vector2> pts)
        {
            var P = pts.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            float Cr(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
            var H = new List<Vector2>();
            foreach (var p in P) { while (H.Count >= 2 && Cr(H[H.Count - 2], H[H.Count - 1], p) <= 0) H.RemoveAt(H.Count - 1); H.Add(p); }
            int lo = H.Count; for (int i = P.Count - 2; i >= 0; i--) { var p = P[i]; while (H.Count > lo && Cr(H[H.Count - 2], H[H.Count - 1], p) <= 0) H.RemoveAt(H.Count - 1); H.Add(p); }
            H.RemoveAt(H.Count - 1);
            float best = float.MaxValue; (Vector2, Vector2, float, float) res = default;
            for (int i = 0; i < H.Count; i++)
            {
                var e = H[(i + 1) % H.Count] - H[i]; if (e.sqrMagnitude < 1e-6f) continue; e.Normalize(); var nn = new Vector2(-e.y, e.x);
                float u0 = 1e9f, u1 = -1e9f, v0 = 1e9f, v1 = -1e9f;
                foreach (var p in H) { float u = Vector2.Dot(p, e), v = Vector2.Dot(p, nn); u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u); v0 = Mathf.Min(v0, v); v1 = Mathf.Max(v1, v); }
                float ar = (u1 - u0) * (v1 - v0); if (ar >= best) continue; best = ar;
                var c = e * (u0 + u1) / 2 + nn * (v0 + v1) / 2;
                res = u1 - u0 >= v1 - v0 ? (c, e, u1 - u0, v1 - v0) : (c, nn, v1 - v0, u1 - u0);
            }
            if (res.Item2.x < 0 || (Mathf.Abs(res.Item2.x) < 1e-4f && res.Item2.y < 0)) res.Item2 = -res.Item2;
            return res;
        }

        internal static void ResetLayers()
        {
            layerOf.Clear(); layers.Clear(); lays.Clear();
            layers.Add(new LayerSrc { smooth = new Vector2(.2f, .2f) }); layerOf["plain"] = 0;
        }

        [MenuItem("Magistrala/Zgrade: paket – ispeci modele")]
        public static void BakeMenu() => Debug.Log(Bake());

        public static string Bake()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Models/Paket")) AssetDatabase.CreateFolder("Assets/Models", "Paket");
            ResetLayers(); // layer 0: plain white for the far hull (vertex colour only)
            var log = new StringBuilder(); var meta = new MetaFile();
            var baked = new List<(string name, bool hall, List<Part> l0, List<Part> l1, Bounds bb)>();
            foreach (var (name, hall, cap) in Models)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(Source(name));
                if (!pf) { log.Append(name + " missing; "); continue; }
                var inst = (GameObject)Object.Instantiate(pf); inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); inst.transform.localScale = Vector3.one;
                log.Append(name + ":");
                var parts = Parts(inst, log); Object.DestroyImmediate(inst);
                if (name == "Stambena")
                {
                    // its facade: the layer with the most wall area, lightened (white / light grey / beige by the variants)
                    var wallA = new Dictionary<int, float>();
                    foreach (var p in parts) { float va = 0; for (int t = 0; t < p.v.Length; t += 3) { var fn = Vector3.Cross(p.v[t + 1] - p.v[t], p.v[t + 2] - p.v[t]); if (Mathf.Abs(fn.normalized.y) < .3f) va += fn.magnitude * .5f; } wallA[p.lay.layer] = (wallA.TryGetValue(p.lay.layer, out var q) ? q : 0) + va; }
                    float tot = wallA.Values.Sum();
                    foreach (var kv in wallA) if (kv.Key != 0 && kv.Value > tot * .08f && layers[kv.Key].baseTex != null) layers[kv.Key].lighten = true;
                }
                if (parts.Count == 0) { log.Append(" nothing left\n"); continue; }
                var l0 = Cap(parts, cap); var l1 = Cap(l0, Mathf.RoundToInt(l0.Sum(p => p.tri.Length / 3) * .25f));
                var bb = l0[0].b; foreach (var p in l0) bb.Encapsulate(p.b);
                baked.Add((name, hall, l0, l1, bb));
                log.Append($" tris {parts.Sum(p => p.tri.Length / 3)} -> {l0.Sum(p => p.tri.Length / 3)} / {l1.Sum(p => p.tri.Length / 3)}\n");
            }
            // textures: only the layers of the models placed (zgrade-paket.json; all when there is none yet), compacted
            var usedModels = new HashSet<string>(Placements().Select(p => p.model));
            var keep = new List<int> { 0 }; var remap = new Dictionary<int, int> { { 0, 0 } };
            foreach (var bk in baked) if (usedModels.Count == 0 || usedModels.Contains(bk.name)) foreach (var p in bk.l0) if (!remap.ContainsKey(p.lay.layer)) { remap[p.lay.layer] = keep.Count; keep.Add(p.lay.layer); }
            foreach (var l in lays.Values) l.layer = remap.TryGetValue(l.layer, out var nl) ? nl : 0;
            var kept = keep.Select(i => layers[i]).ToList(); layers.Clear(); layers.AddRange(kept);
            bool Used(string nm) => usedModels.Count == 0 || usedModels.Contains(nm);
            var (alb, nor) = LayerPixels(layers, TEX);
            var aA = Strip(alb, TEX, true, OUT + "paket_albedo.jpg", 90); var aN = Strip(nor, TEX, false, OUT + "paket_normal.jpg", 92);
            foreach (var f in new[] { "paket_albedo.asset", "paket_normal.asset" }) AssetDatabase.DeleteAsset(OUT + f);
            foreach (var (suf, tint) in Variants) { Mat("Paket" + suf, false, aA, aN, tint); Mat("PaketCut" + suf, true, aA, aN, tint); }
            foreach (var (name, hall, l0, l1, bb) in baked)
            {
                bool used = Used(name);
                var g0 = new Geo(); var g1 = new Geo();
                float dirt = hall ? .45f : .3f;
                foreach (var p in l0) Emit(g0, p, bb.min.y, dirt);
                foreach (var p in l1) Emit(g1, p, bb.min.y, dirt);
                var g2 = Hull(l0, bb, 0, out float fill);
                // the plinth (on sloping ground the low side shows up to 1.5 m of it): the hull's outer walls below the ground line
                var sk = Hull(l0, bb, 0, out _, true);
                foreach (var g in new[] { g0, g1 })
                {
                    int o = g.v.Count; g.v.AddRange(sk.v); g.n.AddRange(sk.n); g.uv.AddRange(sk.uv); g.d.AddRange(sk.d);
                    foreach (var k in sk.c) g.c.Add(new Color(.42f, .41f, .39f)); foreach (var t in sk.sub[0]) g.sub[0].Add(o + t);
                }
                if (used) { Save(ToMesh(g0, name + "_lod0"), name + "_lod0"); Save(ToMesh(g1, name + "_lod1"), name + "_lod1"); Save(ToMesh(g2, name + "_lod2"), name + "_lod2"); }
                // footprint from the vertices up to 1.5 m; front = the long side with more glass / door area
                var low = l0.Where(p => !p.lay.cut).SelectMany(p => p.v).Where(v => v.y < bb.min.y + 1.5f).Select(v => new Vector2(Mathf.Round(v.x * 20) / 20, Mathf.Round(v.z * 20) / 20));
                var (c, ax, len, wid) = Obb(low);
                var side = new Vector2(-ax.y, ax.x); float plus = 0, minus = 0;
                foreach (var p in l0)
                {
                    var src = layers[p.lay.layer].baseTex ?? ""; string f = Path.GetFileName(src).ToLowerInvariant();
                    if (!(f.Contains("glass") || f.Contains("door") || f.Contains("gate") || f.Contains("window") || f.Contains("shutter"))) continue;
                    var pc = new Vector2(p.b.center.x, p.b.center.z) - c; float s = Vector2.Dot(pc, side);
                    if (s > wid * .3f) plus += p.area; else if (s < -wid * .3f) minus += p.area;
                }
                var front = plus >= minus ? side : -side;
                meta.models.Add(new ModelMeta { name = name, hall = hall, cx = c.x, cz = c.y, len = len, wid = wid, ax = ax.x, az = ax.y, height = bb.size.y, fill = fill, frontX = front.x, frontZ = front.y, tris0 = g0.Tris, tris1 = g1.Tris, tris2 = g2.Tris, layers = layers.Count });
                log.Append($"{name}: {len:F1} x {wid:F1} x {bb.size.y:F1} m, fill {fill:F2}, front ({front.x:F2},{front.y:F2}), tris {g0.Tris}/{g1.Tris}/{g2.Tris}\n");
            }
            foreach (var (name, _, _) in Models) if (!Used(name) || !baked.Any(b => b.name == name)) foreach (var l in new[] { "_lod0", "_lod1", "_lod2" }) AssetDatabase.DeleteAsset(OUT + name + l + ".asset");
            foreach (var om in Meta()) if (!meta.models.Any(x => x.name == om.name)) meta.models.Add(om); // models not placed: measured earlier
            meta.layerKeys = layers.Select(l => (l.baseTex ?? "-") + (l.cut ? " (cut)" : "")).ToList();
            File.WriteAllText(META, JsonUtility.ToJson(meta, true)); AssetDatabase.ImportAsset(META);
            AssetDatabase.SaveAssets();
            log.Append($"layers {layers.Count}: " + string.Join(", ", layers.Select((l, i) => i + "=" + Path.GetFileNameWithoutExtension(l.baseTex ?? "plain") + (l.cut ? "*" : ""))));
            return log.ToString();
        }

        internal static void Save(Mesh m, string file)
        {
            string p = OUT + file + ".asset"; var old = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (old) { old.Clear(); EditorUtility.CopySerialized(m, old); Object.DestroyImmediate(m); EditorUtility.SetDirty(old); }
            else AssetDatabase.CreateAsset(m, p);
        }

        public static List<ModelMeta> Meta()
        {
            if (!File.Exists(META)) return new List<ModelMeta>();
            return JsonUtility.FromJson<MetaFile>(File.ReadAllText(META)).models;
        }

        // ------------------------------------------------------------------ matching
        [Serializable] public sealed class Placement
        {
            public string id, osm, type, name, model; public int nAlong = 1, nAcross = 1, variant; public bool mirror; public float sy = 1;
            public double gx, gz; public float ux, uz, sx, sz, len, wid; // centre (game), long axis (unity xz), per-axis scale of one unit, target OBB
            public float fx, fz; // front direction (unity xz)
            public List<string> ids = new List<string>(); // the tile buildings it replaces
        }
        [Serializable] sealed class PlaceFile { public string note; public List<Placement> placements = new List<Placement>(); }

        public static List<Placement> Placements()
        {
            if (!File.Exists(PLACE_JSON)) return new List<Placement>();
            return JsonUtility.FromJson<PlaceFile>(File.ReadAllText(PLACE_JSON)).placements;
        }
        static HashSet<string> replaced;
        public static void ResetReplacements() => replaced = null;

        // One owner per replacement footprint: castles > stations/stores > pack halls.
        // Replaced() and Place() must filter identically or a rejected hall leaves a hole in the tiles.
        static bool HigherPriority(Placement p)
        {
            double len = Math.Sqrt(p.ux * p.ux + p.uz * p.uz);
            double ax = len > 0 ? p.ux / len : 1, az = len > 0 ? -p.uz / len : 0;
            var foot = new List<BuildingsFromTiles.Vector2d>();
            foreach (var q in new[] { (-1, -1), (1, -1), (1, 1), (-1, 1) })
                foot.Add(new BuildingsFromTiles.Vector2d(p.gx + ax * q.Item1 * p.len / 2 - az * q.Item2 * p.wid / 2,
                                                        p.gz + az * q.Item1 * p.len / 2 + ax * q.Item2 * p.wid / 2));
            if (Kasteli.Excluded(foot, p.gx, p.gz)) return true;
            foreach (var id in p.ids)
                if (Kasteli.ExcludedTile(id) || Benzinske.SkipTile(id)) return true;
            return false;
        }
        /// tile building ids (tx_tz:i) a pack building replaces (BuildingsFromTiles skips them)
        public static bool Replaced(string id)
        {
            if (id == null) return false;
            if (replaced == null) { replaced = new HashSet<string>(); foreach (var p in Placements()) if (!HigherPriority(p)) foreach (var i in p.ids) replaced.Add(i); }
            return replaced.Contains(id);
        }

        const float SMIN = .85f, SMAX = 1.15f;

        [MenuItem("Magistrala/Zgrade: paket – uparivanje (kandidati.json)")]
        public static void MatchMenu() => Debug.Log(Match());

        /// needs Yards (the carriageways) set up: run inside / after BuildingsFromTiles.Build, or calls Yards.Begin itself
        public static string Match()
        {
            var models = Meta(); if (models.Count == 0) return "no " + META + ": bake first";
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(Candidates)));
            var cands = MiniJson.L(root["candidates"]);
            Yards.Begin(WorldData.Models(), new List<double[]>());
            var res = new PlaceFile { note = "PaketZgrade.Match from tools/world/zgrade-paket/kandidati.json: pack model per footprint (unity axis ux,uz; scale per unit; tiles nAlong x nAcross)" };
            var useCount = new Dictionary<string, int>(); var log = new StringBuilder(); int nProt = 0, nNoFit = 0, nRect = 0;
            foreach (var co in cands)
            {
                var c = MiniJson.O(co);
                var prot = MiniJson.O(c["protection"]); if ((bool)prot["excludedFromReplacement"]) { nProt++; continue; }
                string id = (string)c["id"]; if (!id.Contains(":")) continue; // OSM-only: nothing of ours to replace
                string type = (string)c["type"];
                string nm = c.ContainsKey("name") ? c["name"] as string : null;
                if (nm != null && (nm.Contains("Policij") || nm.Contains("Crkv") || nm.Contains("Dom zdravlja"))) { nNoFit++; continue; } // not a hall, whatever OSM says
                var obb = MiniJson.O(c["obb"]); double L = MiniJson.D(obb["lengthM"]), Wd = MiniJson.D(obb["widthM"]);
                double rect = obb.ContainsKey("rectangularity") && obb["rectangularity"] != null ? MiniJson.D(obb["rectangularity"]) : 1;
                var ring = MiniJson.L(MiniJson.L(MiniJson.O(obb["polygon"])["coordinates"])[0]);
                var pts = ring.Select(p => { var q = MiniJson.L(p); return W.U(MiniJson.D(q[0]), 0, MiniJson.D(q[1])); }).ToList();
                var ctr = MiniJson.L(obb["centerXZ"]);
                double gx = MiniJson.D(ctr[0]), gz = MiniJson.D(ctr[1]);
                // the long axis in Unity space from the OBB polygon
                Vector3 e0 = pts[1] - pts[0], e1 = pts[2] - pts[1]; var ax3 = e0.magnitude >= e1.magnitude ? e0 : e1; var axis = new Vector2(ax3.x, ax3.z).normalized;
                if (axis.x < 0) axis = -axis;
                bool school = type == "skola_vrtic" || type == "javna";
                // best fit: smallest scale deviation; halls may tile; civic models only for schools / public / retail
                Placement best = null; float bestErr = 1e9f;
                foreach (var m in models)
                {
                    if (school ? m.hall && m.name != "Building_5" : !m.hall) continue;
                    if (m.name == "Building_8" && Mathf.Abs(m.fill - (float)rect) > .2f) continue; // the L-shaped school wants an L-shaped footprint
                    // halls tile (one hall type per site, at most 4 units); the 2-storey block 5 and the tall shed 6 only alone
                    bool tiles = m.hall && m.name != "Building_5" && m.name != "Building_6";
                    for (int na = 1; na <= (tiles ? 4 : 1); na++)
                        for (int nc = 1; nc <= (tiles ? 2 : 1); nc++)
                            foreach (bool swap in new[] { false, true })
                            {
                                if (na * nc > 4 || swap && na * nc > 1) continue;
                                // swap: the model's long axis across the footprint (tiles side by side)
                                float ml = swap ? m.wid * na : m.len * na, mw = swap ? m.len * nc : m.wid * nc;
                                float sl = (float)L / ml, sw = (float)Wd / mw;
                                if (sl < SMIN || sl > SMAX || sw < SMIN || sw > SMAX) continue;
                                float err = Mathf.Abs(Mathf.Log(sl)) + Mathf.Abs(Mathf.Log(sw)) + .04f * (na * nc - 1) + (swap ? .05f : 0) + .03f * (useCount.TryGetValue(m.name, out var u) ? u : 0);
                                if (err >= bestErr) continue;
                                bestErr = err;
                                best = new Placement { id = id, model = m.name, nAlong = swap ? nc : na, nAcross = swap ? na : nc, sx = swap ? sw : sl, sz = swap ? sl : sw, len = (float)L, wid = (float)Wd, gx = gx, gz = gz };
                                // the model's long axis: along the footprint, or across it (swap)
                                var ma = swap ? new Vector2(-axis.y, axis.x) : axis; best.ux = ma.x; best.uz = ma.y;
                            }
                }
                if (best == null) { nNoFit++; continue; }
                // the front faces the nearest carriageway: test both sides of the model's width axis
                var mm = models.First(m => m.name == best.model);
                var A = new Vector2(best.ux, best.uz); var S = new Vector2(-A.y, A.x);
                float halfW = (best.nAcross * mm.wid * best.sz) / 2 + 3, halfL = (best.nAlong * mm.len * best.sx) / 2 + 3;
                var cu = W.U(gx, 0, gz);
                double Cl(Vector2 d, float off) { var p = new Vector3(cu.x + d.x * off, 0, cu.z + d.y * off); var g = W.G(p); return Yards.Clear(g.x, g.y).clear; }
                double sPlus = Cl(S, halfW), sMinus = Cl(-S, halfW), aPlus = Cl(A, halfL), aMinus = Cl(-A, halfL);
                var fr = sPlus <= sMinus ? S : -S;
                best.fx = fr.x; best.fz = fr.y;
                best.mirror = Hash(gx, gz) < .5;
                best.variant = (int)(Hash(gx + 7, gz) * Variants.Length) % Variants.Length;
                best.type = type; best.name = c.ContainsKey("name") ? c["name"] as string : null; best.osm = c.ContainsKey("osmId") ? c["osmId"] as string : null;
                best.ids.Add(id);
                res.placements.Add(best); useCount[best.model] = (useCount.TryGetValue(best.model, out var k) ? k : 0) + 1;
                if (rect < .8) nRect++;
            }
            // newer 3-5 storey apartment blocks (Stambena): the storey index (eaves 9-17 m), a near-square footprint the model fits
            // (±15 %), in Kaštela, at most 8, at least 400 m apart, picked in a fixed pseudo-random order
            var st = models.FirstOrDefault(m => m.name == "Stambena"); int nApt = 0;
            const string KATOVI = "Assets/Generated/Game/Resources/katovi-index.json";
            if (st != null && File.Exists(KATOVI))
            {
                var kat = MiniJson.O(MiniJson.Parse(File.ReadAllText(KATOVI)));
                var cand = new List<(double key, Placement p)>();
                foreach (var ro in MiniJson.L(kat["b"]))
                {
                    var r = MiniJson.L(ro); string id = (string)r[0]; double h = MiniJson.D(r[4]);
                    if (h < 9 || h > 17 || res.placements.Any(p => p.ids.Contains(id))) continue;
                    var xs = MiniJson.L(r[5]); var gp = new List<Vector2>(); double cx = 0, cz = 0;
                    for (int i = 0; i + 1 < xs.Count; i += 2) { double x = MiniJson.D(xs[i]), z = MiniJson.D(xs[i + 1]); gp.Add(new Vector2((float)x, (float)z)); cx += x; cz += z; }
                    if (gp.Count < 4) continue; cx /= gp.Count; cz /= gp.Count;
                    if (cx < -10240 || cx > -400 || cz > -3800) continue;
                    double area = 0; for (int i = 0; i < gp.Count; i++) { var a = gp[i]; var b = gp[(i + 1) % gp.Count]; area += a.x * b.y - b.x * a.y; } area = Math.Abs(area) / 2;
                    var (oc, oax, len, wid) = Obb(gp.Select(q => { var u = W.U(q.x, 0, q.y); return new Vector2(u.x, u.z); }));
                    if (area / (len * wid) < .85) continue;
                    float sl = len / st.len, sw = wid / st.wid;
                    if (sl < SMIN || sl > SMAX || sw < SMIN || sw > SMAX) continue;
                    var gc = W.G(new Vector3(oc.x, 0, oc.y));
                    var pl = new Placement { id = id, model = "Stambena", type = "stambena", sx = sl, sz = sw, len = len, wid = wid, gx = gc.x, gz = gc.y, ux = oax.x, uz = oax.y,
                        sy = Mathf.Clamp((float)(h + 1.5) / st.height, SMIN, SMAX) };
                    pl.ids.Add(id); cand.Add((Hash(cx * .37, cz * .91), pl));
                }
                foreach (var (_, pl) in cand.OrderBy(c => c.key))
                {
                    if (nApt >= 8) break;
                    if (res.placements.Any(q => q.model == "Stambena" && (q.gx - pl.gx) * (q.gx - pl.gx) + (q.gz - pl.gz) * (q.gz - pl.gz) < 400 * 400)) continue;
                    var A = new Vector2(pl.ux, pl.uz); var S = new Vector2(-A.y, A.x); var cu = W.U(pl.gx, 0, pl.gz);
                    double Cl(Vector2 d) { var g = W.G(new Vector3(cu.x + d.x * (pl.wid / 2 + 3), 0, cu.z + d.y * (pl.wid / 2 + 3))); return Yards.Clear(g.x, g.y).clear; }
                    var fr = Cl(S) <= Cl(-S) ? S : -S; pl.fx = fr.x; pl.fz = fr.y;
                    pl.mirror = Hash(pl.gx, pl.gz) < .5; pl.variant = (int)(Hash(pl.gx + 7, pl.gz) * Variants.Length) % Variants.Length;
                    res.placements.Add(pl); nApt++;
                }
                if (nApt > 0) useCount["Stambena"] = nApt;
            }
            File.WriteAllText(PLACE_JSON, JsonUtility.ToJson(res, true)); AssetDatabase.ImportAsset(PLACE_JSON); replaced = null;
            log.Append($"placements {res.placements.Count} (protected {nProt}, no fit {nNoFit}): " + string.Join(", ", useCount.Select(kv => kv.Key + " " + kv.Value)));
            return log.ToString();
        }
        static double Hash(double x, double z) { double v = Math.Sin(x * 12.9898 + z * 78.233) * 43758.5453; return v - Math.Floor(v); }

        // ------------------------------------------------------------------ placement
        // LOD switch distances (m, chase camera fov 55, lodBias 2 as BuildingsFromTiles): LOD1 from 90 m, hull from 350 m
        const float LOD1_AT = 90, LOD2_AT = 350;

        [MenuItem("Magistrala/Zgrade: paket (hale, škole)")]
        public static void Menu()
        {
            // the full rebuild: our tile buildings without the replaced ones, then the pack's (BuildingsFromTiles calls Place)
            replaced = null; MainScene.Ensure(); BuildingsFromTiles.Build();
        }

        public static string Place(Transform buildings, Func<double, double, double> ground)
        {
            var old = buildings.Find("paket"); if (old) Object.DestroyImmediate(old.gameObject);
            var P = Placements(); if (P.Count == 0) return "paket: no placements";
            var meta = Meta().ToDictionary(m => m.name);
            var root = new GameObject("paket"); root.transform.SetParent(buildings, false); root.isStatic = true;
            int n = 0, units = 0; long tris = 0;
            foreach (var p in P)
            {
                if (HigherPriority(p)) continue;
                if (!meta.TryGetValue(p.model, out var m)) continue;
                var l0 = AssetDatabase.LoadAssetAtPath<Mesh>(OUT + p.model + "_lod0.asset"); var l1 = AssetDatabase.LoadAssetAtPath<Mesh>(OUT + p.model + "_lod1.asset"); var l2 = AssetDatabase.LoadAssetAtPath<Mesh>(OUT + p.model + "_lod2.asset");
                if (!l0 || !l1 || !l2) continue;
                var v = Variants[Mathf.Clamp(p.variant, 0, Variants.Length - 1)].suffix;
                var mats = new[] { AssetDatabase.LoadAssetAtPath<Material>(OUT + "Paket" + v + ".mat"), AssetDatabase.LoadAssetAtPath<Material>(OUT + "PaketCut" + v + ".mat") };
                var c = W.U(p.gx, 0, p.gz);
                var A = new Vector3(p.ux, 0, p.uz).normalized; var S = new Vector3(-A.z, 0, A.x);
                var go = new GameObject($"{p.model} {p.id}"); go.isStatic = true; go.transform.SetParent(root.transform, false);
                for (int ia = 0; ia < p.nAlong; ia++)
                    for (int ic = 0; ic < p.nAcross; ic++)
                    {
                        float ul = m.len * p.sx, uw = m.wid * p.sz;
                        var uc = c + A * ((ia - (p.nAlong - 1) / 2f) * ul) + S * ((ic - (p.nAcross - 1) / 2f) * uw);
                        // the unit's front: the placement's front for the row facing it, the back row turned round
                        var fr = new Vector3(p.fx, 0, p.fz); if (p.nAcross > 1 && Vector3.Dot(uc - c, fr) < -.1f) fr = -fr;
                        // the model's frame (footprint centre, front = its +z'); its long axis is its right axis
                        var mF = new Vector3(m.frontX, 0, m.frontZ).normalized; var mc = new Vector3(m.cx, 0, m.cz);
                        bool mir = p.mirror ^ ((ia + ic) % 2 == 1);
                        var unitGo = new GameObject("u" + ia + ic); unitGo.isStatic = true; unitGo.transform.SetParent(go.transform, false);
                        // ground: the highest corner minus a little (the plinth reaches 1.5 m below the model's ground)
                        double gmin = 1e9, gmax = -1e9;
                        foreach (var (da, dc) in new[] { (-.5f, -.5f), (.5f, -.5f), (.5f, .5f), (-.5f, .5f), (0f, 0f) })
                        {
                            var q = uc + A * (da * ul) + S * (dc * uw); var g2 = W.G(q); double h = ground(g2.x, g2.y); gmin = Math.Min(gmin, h); gmax = Math.Max(gmax, h);
                        }
                        float y = (float)(gmax - Math.Min(.6, (gmax - gmin) * .3));
                        unitGo.transform.localPosition = new Vector3(uc.x, y, uc.z);
                        unitGo.transform.localRotation = Quaternion.LookRotation(fr, Vector3.up);
                        unitGo.transform.localScale = new Vector3(mir ? -p.sx : p.sx, p.sy > 0 ? p.sy : 1, p.sz);
                        var Ri = Quaternion.Inverse(Quaternion.LookRotation(mF, Vector3.up));
                        var mg = new GameObject("m"); mg.isStatic = true; mg.transform.SetParent(unitGo.transform, false);
                        mg.transform.localRotation = Ri; mg.transform.localPosition = Ri * -mc;
                        unitGo = mg;
                        var r0 = Rend(unitGo, "lod0", l0, mats, ShadowCastingMode.Off); var r1 = Rend(unitGo, "lod1", l1, mats, ShadowCastingMode.Off);
                        var r2 = Rend(unitGo, "lod2", l2, new[] { mats[0] }, ShadowCastingMode.Off);
                        var sh = Rend(unitGo, "hull", l2, new[] { mats[0] }, ShadowCastingMode.ShadowsOnly); sh.gameObject.isStatic = false;
                        sh.gameObject.AddComponent<MeshCollider>().sharedMesh = l2;
                        var lg = unitGo.transform.parent.gameObject.AddComponent<LODGroup>();
                        float size = Mathf.Max(ul, uw);
                        float H(float d) => Mathf.Clamp01(2f * size / (d * 2 * Mathf.Tan(27.5f * Mathf.Deg2Rad)));
                        lg.SetLODs(new[] { new LOD(H(LOD1_AT), new Renderer[] { r0 }), new LOD(H(LOD2_AT), new Renderer[] { r1 }), new LOD(0f, new Renderer[] { r2 }) });
                        lg.fadeMode = LODFadeMode.None;
                        units++; tris += m.tris0;
                    }
                n++;
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(buildings.gameObject.scene);
            return $"paket: {n} buildings ({units} units, LOD0 {tris / 1000}k tris)";
        }

        static Renderer Rend(GameObject go, string name, Mesh mesh, Material[] mats, ShadowCastingMode shadows)
        {
            var c = new GameObject(name); c.isStatic = true; c.transform.SetParent(go.transform, false);
            c.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = c.AddComponent<MeshRenderer>(); r.sharedMaterials = mesh.subMeshCount > 1 && mesh.GetIndexCount(1) > 0 ? mats : new[] { mats[0] };
            r.shadowCastingMode = shadows; r.receiveShadows = true;
            // no static batching (one mesh per model, shared by every placement)
            GameObjectUtility.SetStaticEditorFlags(c, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            return r;
        }
    }
}
#endif
