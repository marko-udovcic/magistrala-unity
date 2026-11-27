// UKRAINIAN CITY facade details on our houses along the roads (HouseBuilder, near / mid level of the house cells): window frames
// (Window_F on plastered, Window_G on stone houses; the pack's glass left out, ours with its rooms stays), Balcony_F / Balcony_E,
// a downpipe (Pipe_B_1) with its gutter outlet (GutterDrain_A) and awnings (Awning_B) over shop windows. Baked once
// (Magistrala/Zgrade: paket – ispeci detalje) into canonical meshes (x across, centred; y up from 0; z out of the wall from 0)
// and ONE texture array pair (Magistrala/Paket, PaketDetalji / PaketDetaljiCut); HouseBuilder appends transformed copies into
// the cell mesh (submeshes 3 / 4) — no GameObject per detail. AC units stay the Tripo klima (instanced, BuildingsFromTiles).
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Magistrala.EditorTools
{
    public static class PaketDetalji
    {
        public const string DIR = PaketZgrade.OUT + "Detalji/";
        /// key, prefab, triangle cap, the pack's glass left out
        static readonly (string key, string prefab, int cap, bool noGlass)[] Items =
        {
            ("okvir_f", "Window_F", 700, true), ("okvir_g", "Window_G", 700, true),
            ("balkon_f", "Balcony_F", 1600, false), ("balkon_e", "Balcony_E", 1600, false),
            ("cijev", "Pipe_B_1 Variant", 1200, false), ("oluk", "GutterDrain_A", 200, false), ("tenda", "Awning_B", 900, false),
            // "vječna gradnja": a scaffolding bay (CGTrader free model, Royalty Free, modified: Detalji/skela) and the pack's rusty site fence
            ("skela", "#" + DIR + "skela/scaffolding.obj", 1200, false), ("ograda", "Fence_B", 700, false),
        };

        [MenuItem("Magistrala/Zgrade: paket – ispeci detalje")]
        public static void BakeMenu() => Debug.Log(Bake());

        public static string Bake()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Models/Paket/Detalji")) AssetDatabase.CreateFolder("Assets/Models/Paket", "Detalji");
            PaketZgrade.ResetLayers();
            var log = new StringBuilder(); var baked = new List<(string key, List<PaketZgrade.Part> parts, Bounds b)>();
            foreach (var (key, prefab, cap, noGlass) in Items)
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(prefab.StartsWith("#") ? prefab.Substring(1) : PaketZgrade.SRC + "Prefabs/" + prefab + ".prefab");
                if (!pf) { log.Append(prefab + " missing; "); continue; }
                var inst = (GameObject)Object.Instantiate(pf); inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); inst.transform.localScale = Vector3.one;
                log.Append(key + ":");
                var parts = PaketZgrade.Parts(inst, log, false); Object.DestroyImmediate(inst);
                if (noGlass) parts = parts.Where(p => !(PaketZgrade.layers[p.lay.layer].baseTex ?? "").Contains("Glass")).ToList();
                parts = PaketZgrade.Cap(parts, cap);
                // the scaffolding has no textures: galvanised steel and weathered planks as tints of the plain layer
                if (key == "skela") foreach (var p in parts) p.lay = new PaketZgrade.Lay { layer = 0, tri = true, scale = 1, tiling = Vector2.one,
                    tint = (p.v.Length > 0 && IsWood(p) ? new Color(.62f, .5f, .36f) : new Color(.66f, .68f, .7f)).linear };
                if (parts.Count == 0) continue;
                var b = parts[0].b; foreach (var p in parts) b.Encapsulate(p.b);
                baked.Add((key, parts, b));
                log.Append($" {b.size.x:F2} x {b.size.y:F2} x {b.size.z:F2} m, tris {parts.Sum(p => p.tri.Length / 3)}\n");
            }
            var layers = PaketZgrade.layers;
            var (alb, nor) = PaketZgrade.LayerPixels(layers, 512);
            var aA = PaketZgrade.Strip(alb, 512, true, DIR + "detalji_albedo.jpg", 90); var aN = PaketZgrade.Strip(nor, 512, false, DIR + "detalji_normal.jpg", 92);
            var mo = PaketZgrade.Mat("Detalji/PaketDetalji", false, aA, aN, Color.white); var mc = PaketZgrade.Mat("Detalji/PaketDetaljiCut", true, aA, aN, Color.white);
            foreach (var (key, parts, b) in baked)
            {
                // canonical frame: x centred, y from the bottom, z from the back (the wall side: the pack's details face +z)
                var o = new Vector3(b.center.x, b.min.y, b.min.z);
                foreach (var p in parts) for (int i = 0; i < p.v.Length; i++) p.v[i] -= o;
                var g = new PaketZgrade.Geo(); foreach (var p in parts) PaketZgrade.Emit(g, p, 0, .25f);
                PaketZgrade.Save(PaketZgrade.ToMesh(g, key), "Detalji/" + key);
            }
            AssetDatabase.SaveAssets(); cache.Clear();
            return log + $"layers {layers.Count}: " + string.Join(", ", layers.Select((l, i) => i + "=" + System.IO.Path.GetFileNameWithoutExtension(l.baseTex ?? "plain")));
        }

        // the plank: the flat, wide part of the bay
        static bool IsWood(PaketZgrade.Part p) => p.b.size.y < .15f || p.b.size.y < Mathf.Min(p.b.size.x, p.b.size.z) * .2f;

        public static int nSites, nBays, nSiteCand; public static readonly List<(Vector3 p, Vector3 n)> SiteSpots = new List<(Vector3, Vector3)>();
        /// a construction site on the facade (x0..x1 along it): scaffolding bays stacked to the eaves, 0.25 m off the wall, and the
        /// site fence `fenceOut` m in front (on the yard side of the road)
        public static void Site(HouseGeo g, Vector3 origin, Vector3 along, Vector3 outward, float x0, float x1, float ground, float top, float fenceOut)
        {
            var d = Get("skela"); if (d == null) return;
            int nx = Mathf.Max(1, Mathf.FloorToInt((x1 - x0) / d.size.x)), ny = Mathf.Max(1, Mathf.CeilToInt((top - ground - .3f) / d.size.y));
            float bw = (x1 - x0) / nx;
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                { Put(g, "skela", origin + along * (x0 + (i + .5f) * bw) + Vector3.up * (ground + j * d.size.y), along, outward, bw / d.size.x, 1, 1, .25f, ground, Color.white); nBays++; }
            var f = Get("ograda");
            if (f != null && fenceOut > d.size.z + .6f)
            {
                int nf = Mathf.Max(1, Mathf.RoundToInt((x1 - x0 + 2) / Mathf.Max(1, f.size.x)));
                float fw = (x1 - x0 + 2) / nf;
                for (int i = 0; i < nf; i++) Put(g, "ograda", origin + along * (x0 - 1 + (i + .5f) * fw) + Vector3.up * ground, along, outward, fw / f.size.x, 1, 1, fenceOut, ground, Color.white);
            }
            nSites++; SiteSpots.Add((origin + along * ((x0 + x1) / 2) + Vector3.up * ground, outward));
        }

        // ------------------------------------------------------------------ emission (HouseBuilder)
        sealed class D { public Vector3[] v, n; public Vector2[] uv; public List<Vector4> d = new List<Vector4>(); public Color[] c; public int[][] t; public Vector3 size; }
        static readonly Dictionary<string, D> cache = new Dictionary<string, D>();
        static D Get(string key)
        {
            if (cache.TryGetValue(key, out var d)) return d;
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(DIR + key + ".asset");
            if (m) { d = new D { v = m.vertices, n = m.normals, uv = m.uv, c = m.colors, t = new[] { m.GetTriangles(0), m.subMeshCount > 1 ? m.GetTriangles(1) : new int[0] }, size = m.bounds.max }; m.GetUVs(1, d.d); d.size = new Vector3(m.bounds.size.x, m.bounds.max.y, m.bounds.max.z); }
            return cache[key] = d;
        }
        public static bool Ready => Get("okvir_f") != null;
        public static int nFrames, nBalconies, nPipes, nAwnings;
        public static void ResetCounts() { nFrames = nBalconies = nPipes = nAwnings = nSites = nBays = nSiteCand = 0; SiteSpots.Clear(); cache.Clear(); }

        /// one detail into the house mesh: canonical (x, y, z) -> origin + along * x * sx + up * y * sy + outward * (z0 + z * sz)
        static void Put(HouseGeo g, string key, Vector3 origin, Vector3 along, Vector3 outward, float sx, float sy, float sz, float z0, float ground, Color tint)
        {
            var d = Get(key); if (d == null) return;
            bool flip = Vector3.Dot(Vector3.Cross(along, Vector3.up), outward) < 0; // (x, y, z) is left-handed in Unity: cross(x, y) = z
            int baseIdx = g.v.Count;
            for (int i = 0; i < d.v.Length; i++)
            {
                var q = d.v[i]; var p = origin + along * (q.x * sx) + Vector3.up * (q.y * sy) + outward * (z0 + q.z * sz);
                var nn = d.n[i]; var n = (along * (nn.x / sx) + Vector3.up * (nn.y / sy) + outward * (nn.z / sz)).normalized;
                g.v.Add(p); g.n.Add(n); g.uv.Add(d.uv[i]);
                var dd = d.d[i]; g.d.Add(new Vector4(dd.x, dd.y, p.y - ground, dd.w));
                g.c.Add(d.c[i] * tint); g.o.Add(Vector4.zero);
            }
            for (int s = 0; s < 2; s++)
            {
                var t = d.t[s]; var list = g.sub[3 + s];
                for (int i = 0; i < t.Length; i += 3)
                {
                    list.Add(baseIdx + t[i]);
                    if (flip) { list.Add(baseIdx + t[i + 2]); list.Add(baseIdx + t[i + 1]); }
                    else { list.Add(baseIdx + t[i + 1]); list.Add(baseIdx + t[i + 2]); }
                }
            }
        }

        /// a window frame filling the opening (x0..x1 along the facade from `origin`, y0..y1 absolute), its front 2 cm out of the wall
        public static void Frame(HouseGeo g, Vector3 origin, Vector3 along, Vector3 outward, float x0, float x1, float y0, float y1, bool stone, float ground)
        {
            string key = stone ? "okvir_g" : "okvir_f"; var d = Get(key); if (d == null) return;
            float w = x1 - x0 + .04f, h = y1 - y0 + .04f;
            // in the reveal: its back just behind our glass (recessed 0.18 m, 0.26 on stone houses), its front (the sill) 4 cm out of the wall
            float back = (stone ? -.26f : -.18f) - .03f;
            Put(g, key, origin + along * ((x0 + x1) / 2) + Vector3.up * (y0 - .02f), along, outward, w / d.size.x, h / d.size.y, (.04f - back) / d.size.z, back, ground,
                stone ? new Color(.95f, .92f, .88f) : Color.white);
            nFrames++;
        }

        /// a balcony slab + railing under a balcony door (centre x along the facade, floor level y), `depth` m deep
        public static void Balcony(HouseGeo g, Vector3 origin, Vector3 along, Vector3 outward, float centre, float floorY, float width, float depth, bool e, float ground)
        {
            string key = e ? "balkon_e" : "balkon_f"; var d = Get(key); if (d == null) return;
            // the slab's top at the floor (the pack's balconies stand on their slab: y 0 is its underside, ~0.18 m)
            Put(g, key, origin + along * centre + Vector3.up * (floorY - .18f), along, outward, width / d.size.x, 1, depth / d.size.z, -.05f, ground, new Color(1f, .96f, .88f));
            nBalconies++;
        }

        /// a downpipe at x from the ground to the eaves, with the gutter outlet at the top
        public static void Pipe(HouseGeo g, Vector3 origin, Vector3 along, Vector3 outward, float x, float ground, float top)
        {
            var d = Get("cijev"); if (d == null || top - ground < 2) return;
            Put(g, "cijev", origin + along * x + Vector3.up * ground, along, outward, 1, (top - ground) / d.size.y, 1, .02f, ground, new Color(.9f, .9f, .9f));
            var o = Get("oluk"); if (o != null) Put(g, "oluk", origin + along * x + Vector3.up * (top - o.size.y), along, outward, 1, 1, 1, .02f, ground, new Color(.9f, .9f, .9f));
            nPipes++;
        }

        /// an awning over a shop window (x0..x1, the window's top y), reaching `reach` m out
        public static void Awning(HouseGeo g, Vector3 origin, Vector3 along, Vector3 outward, float x0, float x1, float y, float reach, Color tint, float ground)
        {
            var d = Get("tenda"); if (d == null) return;
            float w = x1 - x0 + .2f;
            Put(g, "tenda", origin + along * ((x0 + x1) / 2) + Vector3.up * (y + .12f), along, outward, w / d.size.x, 1, reach / d.size.z, 0, ground, tint);
            nAwnings++;
        }

        public static Material[] Materials() => new[] { AssetDatabase.LoadAssetAtPath<Material>(DIR + "PaketDetalji.mat"), AssetDatabase.LoadAssetAtPath<Material>(DIR + "PaketDetaljiCut.mat") };
    }
}
#endif
