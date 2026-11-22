// Parked cars as one "bulk" draw per kind (Marko 2026-11-22): a traffic kind's parts baked into ONE mesh per distance level and
// its textures into ONE atlas pair per kind, drawn by Magistrala.Traffic.ParkedCars with the Magistrala/ParkedCar shader.
//   level 0  the kind's lod1 parts (body submeshes + 4 wheels; lod0 is 14–40k triangles with interior, lod1 ~10–16k without)
//   level 1  lod2 (~4–8k)          level 2  the far mesh (~1k, per-triangle colours on a white atlas cell)
//   atlas    every distinct TripoCar texture pair (albedo, a = smoothness / mask r paint g glass b metal) of the parts gets a
//            power-of-two cell (≤ 1024, halved largest first until all fit 2048²; quadtree packing), plain materials (the opaque
//            glass) a 16 px solid cell; the part's UV is wrapped inside its cell by the shader (uv1 = cell rect), so tiling sources
//            keep working; plates stay our quads (uv.x > 1.5) on the plate atlas, the row per instance
//   paint    the paint texels keep the mask's r: the shader tints them with the per-instance colour (TrafficKind.parkedTint)
//   lamps    off (no emission but the unlit tail's faint red); lens smoothness / reflector metal of the lamp submeshes kept (uv2)
// Called from Magistrala/Build traffic for every parkable kind. Output: Assets/Generated/Traffic/Parked/.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Magistrala.Traffic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class ParkedBake
    {
        const string DIR = "Assets/Generated/Traffic/Parked";
        const int A = 2048, CAP = 1024, SOLID = 16;

        /// the parking filter: passenger cars and vans 3.3–5.4 m long, ≤ 2.1 m high, no beacons, no bus / truck, with paints and weight
        public static bool Parkable(TrafficKind kd)
        {
            if (kd == null) return false;
            string nm = (kd.name ?? "").ToLowerInvariant();
            return kd.len >= 3.3f && kd.len <= 5.4f && kd.h <= 2.1f && kd.beaconShare <= 0 && kd.paints != null && kd.paints.Length > 0 && kd.weight > 0
                   && !nm.Contains("bus") && !nm.Contains("coach") && !nm.Contains("truck") && !nm.Contains("benzer");
        }

        class Cell
        {
            public string key; public Texture alb, mask; public Color32 sa, sm; public bool solid, body;
            public int size, x, y; public Vector4 rect;
        }

        static bool Tripo(Material m) => m && m.shader && m.shader.name == "Magistrala/TripoCar";

        public static string Bake(TrafficKind kd)
        {
            kd.parked = null; kd.parkedMat = null; kd.parkedTint = null;
            var L0 = kd.lod1 != null && kd.lod1.Length > 0 ? kd.lod1 : kd.lod0;
            var L1 = kd.lod2 != null && kd.lod2.Length > 0 ? kd.lod2 : L0;
            var paint0 = kd.paints != null && kd.paints.Length > 0 ? kd.paints[0] : null;
            if (L0 == null || !kd.farMesh || !Tripo(paint0)) { Debug.Log($"[parked] {kd.name}: no TripoCar parts / far mesh — far mesh path"); return null; }
            Dir(DIR);

            // ---- cells
            var cells = new List<Cell>(); var byKey = new Dictionary<string, Cell>();
            Cell Get(string key, Func<Cell> make) { if (!byKey.TryGetValue(key, out var c)) { c = make(); c.key = key; byKey[key] = c; cells.Add(c); } return c; }
            Cell Solid(string key, Color a, Color m) => Get("solid:" + key, () => new Cell { solid = true, sa = a, sm = m, size = SOLID });
            Material MatOf(TrafficPart pt) => pt.paint ? paint0 : pt.mat;
            Cell CellOf(TrafficPart pt)
            {
                var m = MatOf(pt);
                if (Tripo(m))
                {
                    var ta = m.GetTexture("_BaseMap"); var tm = m.GetTexture("_MaskMap");
                    if (ta) return Get(AssetDatabase.GetAssetPath(ta) + "|" + (tm ? AssetDatabase.GetAssetPath(tm) : ""), () => new Cell { alb = ta, mask = tm, body = pt.paint });
                }
                // plain URP material (the opaque glass): its colour / smoothness on a solid cell, glass flagged in the mask
                Color b = m && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.gray;
                float sm = m && m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : .4f;
                bool glass = m && m.name.ToLowerInvariant().Contains("glass");
                if (glass) b = new Color(Mathf.Clamp01(b.r / .55f), Mathf.Clamp01(b.g / .55f), Mathf.Clamp01(b.b / .55f));   // the shader darkens glass × 0.55
                return Solid($"{(m ? m.name : "none")}", new Color(b.r, b.g, b.b, sm), glass ? new Color(0, 1, 0, 0) : Color.clear);
            }
            var white = Solid("white", new Color(1, 1, 1, .35f), Color.clear);
            var paintCell = Solid("paint", new Color(1, 1, 1, .5f), new Color(1, 0, 0, 0));
            foreach (var pt in L0.Concat(L1)) CellOf(pt);

            // ---- sizes: native (≤ CAP), the largest halved (non-body first) until everything fits A²
            foreach (var c in cells) if (!c.solid) c.size = Mathf.Min(CAP, Mathf.NextPowerOfTwo(Mathf.Max(c.alb.width, c.alb.height, (int)SOLID)));
            long Area() => cells.Sum(c => (long)c.size * c.size);
            while (Area() > (long)A * A)
            {
                var big = cells.Where(c => c.size > SOLID).OrderByDescending(c => c.size).ThenBy(c => c.body ? 1 : 0).FirstOrDefault();
                if (big == null) break; big.size /= 2;
            }
            // quadtree packing (power-of-two squares, largest first, always fits when the area does)
            var free = new List<(int x, int y, int s)> { (0, 0, A) };
            foreach (var c in cells.OrderByDescending(c => c.size))
            {
                int fi = -1; for (int i = 0; i < free.Count; i++) if (free[i].s >= c.size && (fi < 0 || free[i].s < free[fi].s)) fi = i;
                if (fi < 0) throw new Exception($"[parked] {kd.name}: atlas full");
                var f = free[fi]; free.RemoveAt(fi);
                while (f.s > c.size) { int h = f.s / 2; free.Add((f.x + h, f.y, h)); free.Add((f.x, f.y + h, h)); free.Add((f.x + h, f.y + h, h)); f = (f.x, f.y, h); }
                c.x = f.x; c.y = f.y; c.rect = new Vector4((float)c.x / A, (float)c.y / A, (float)c.size / A, (float)c.size / A);
            }

            // ---- atlas pixels (raw PNG bytes: no colour conversion, the import settings decide sRGB)
            var pa = new Color32[A * A]; var pm = new Color32[A * A];
            for (int i = 0; i < pm.Length; i++) { pa[i] = new Color32(255, 0, 255, 0); pm[i] = new Color32(0, 0, 0, 0); }
            foreach (var c in cells)
            {
                if (c.solid) { Fill(pa, c, c.sa); Fill(pm, c, c.sm); continue; }
                Blit(pa, c, c.alb, new Color32(255, 255, 255, 90));
                Blit(pm, c, c.mask, new Color32(0, 0, 0, 0));
            }
            var albT = SaveTex(pa, $"{DIR}/{kd.name}_parked_albedo.png", true);
            var maskT = SaveTex(pm, $"{DIR}/{kd.name}_parked_mask.png", false);

            // ---- meshes
            Vector4 Flags(TrafficPart pt)
            {
                var m = MatOf(pt);
                if (!Tripo(m)) return Vector4.zero;
                return new Vector4(m.GetFloat("_Grime") > 0 ? 1 : 0, m.GetFloat("_LampAll"), m.GetFloat("_LampMetal"), pt.lamp == 1 ? 1 : 0);
            }
            Mesh Parts(TrafficPart[] parts, string name)
            {
                var b = new MB();
                foreach (var pt in parts) b.Add(pt.mesh, pt.sub, pt.local, CellOf(pt).rect, Flags(pt), null, Color.white);
                return b.ToMesh(name);
            }
            var m0 = Parts(L0, $"{kd.name}_parked0");
            var m1 = Parts(L1, $"{kd.name}_parked1");
            var mf = new MB(); var fc = kd.farMesh.colors;
            for (int s = 0; s < kd.farMesh.subMeshCount && s < 3; s++)
                mf.Add(kd.farMesh, s, Matrix4x4.identity, (s == 1 ? paintCell : white).rect, Vector4.zero, s == 0 && fc != null && fc.Length == kd.farMesh.vertexCount ? fc : null,
                       s == 2 ? new Color(.55f, .08f, .07f) : Color.white, true);
            var m2 = mf.ToMesh($"{kd.name}_parked2");
            kd.parked = new[] { SaveMesh(m0, $"{DIR}/{kd.name}_parked0.asset"), SaveMesh(m1, $"{DIR}/{kd.name}_parked1.asset"), SaveMesh(m2, $"{DIR}/{kd.name}_parked2.asset") };

            // ---- material (the kind's paint material's shading constants)
            string mp = $"{DIR}/{kd.name}_parked.mat";
            var sh = Shader.Find("Magistrala/ParkedCar");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!mat) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, mp); } else mat.shader = sh;
            mat.SetTexture("_BaseMap", albT); mat.SetTexture("_MaskMap", maskT); mat.SetFloat("_AtlasTexel", 1f / A);
            foreach (var p in new[] { "_PaintRef", "_PaintSmooth", "_CoatSmooth", "_Flake", "_Grime", "_GrimeH" }) mat.SetFloat(p, paint0.GetFloat(p));
            mat.SetVector("_WheelZ", paint0.GetVector("_WheelZ")); mat.SetVector("_PlateRect", paint0.GetVector("_PlateRect"));
            mat.SetTexture("_GrimeTex", paint0.GetTexture("_GrimeTex")); mat.SetTexture("_PlateTex", paint0.GetTexture("_PlateTex"));
            mat.enableInstancing = true; EditorUtility.SetDirty(mat);
            kd.parkedMat = mat;
            kd.parkedTint = kd.paints.Select(p => p && p.HasProperty("_PaintColor") ? (Vector4)p.GetColor("_PaintColor").linear : new Vector4(.8f, .8f, .8f, 0)).ToArray();
            string log = $"[parked] {kd.name}: {Tris(m0)} / {Tris(m1)} / {Tris(m2)} triangles, atlas {A}² with {cells.Count} cells ({string.Join(", ", cells.Where(c => !c.solid).Select(c => c.size))} + {cells.Count(c => c.solid)} solid)";
            Debug.Log(log);
            return log;
        }

        // ------------------------------------------------------------------ pixels
        static void Fill(Color32[] px, Cell c, Color32 v) { for (int y = 0; y < c.size; y++) for (int x = 0; x < c.size; x++) px[(c.y + y) * A + c.x + x] = v; }

        /// the source PNG resampled into the cell: box filter down (power-of-two ratios), nearest up
        static void Blit(Color32[] px, Cell c, Texture src, Color32 fallback)
        {
            string path = src ? AssetDatabase.GetAssetPath(src) : null;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) { Fill(px, c, fallback); return; }
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!t.LoadImage(File.ReadAllBytes(path))) { UnityEngine.Object.DestroyImmediate(t); Fill(px, c, fallback); return; }
            var sp = t.GetPixels32(); int w = t.width, h = t.height; UnityEngine.Object.DestroyImmediate(t);
            int S = c.size;
            for (int y = 0; y < S; y++)
            {
                int y0 = y * h / S, y1 = Mathf.Max(y0 + 1, (y + 1) * h / S);
                for (int x = 0; x < S; x++)
                {
                    int x0 = x * w / S, x1 = Mathf.Max(x0 + 1, (x + 1) * w / S);
                    int r = 0, g = 0, b = 0, a = 0, n = 0;
                    for (int yy = y0; yy < y1; yy++) for (int xx = x0; xx < x1; xx++) { var q = sp[yy * w + xx]; r += q.r; g += q.g; b += q.b; a += q.a; n++; }
                    px[(c.y + y) * A + c.x + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                }
            }
        }

        static Texture2D SaveTex(Color32[] px, string path, bool srgb)
        {
            var t = new Texture2D(A, A, TextureFormat.RGBA32, false, !srgb);
            t.SetPixels32(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = srgb; imp.mipmapEnabled = true; imp.alphaIsTransparency = false; imp.anisoLevel = 4; imp.wrapMode = TextureWrapMode.Clamp;
            imp.maxTextureSize = A; imp.textureCompression = TextureImporterCompression.CompressedHQ; imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ meshes
        class MB
        {
            readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>();
            readonly List<Vector2> U0 = new List<Vector2>(); readonly List<Vector4> U1 = new List<Vector4>(), U2 = new List<Vector4>();
            readonly List<Color> C = new List<Color>(); readonly List<int> T = new List<int>();

            /// one submesh of a part (its local matrix applied), every vertex tagged with the part's atlas cell / flags / tint;
            /// cols: per-vertex colours (sRGB) instead of the tint; flat: the far mesh's UVs are palette coords, not texture UVs
            public void Add(Mesh m, int sub, Matrix4x4 local, Vector4 cell, Vector4 flags, Color[] cols, Color tint, bool flat = false)
            {
                if (!m || sub >= m.subMeshCount) return;
                var v = m.vertices; var n = m.normals; var uv = m.uv; var tri = m.GetTriangles(sub);
                bool flip = local.determinant < 0; var map = new Dictionary<int, int>();
                var lt = tint.linear;
                int Vx(int i)
                {
                    if (map.TryGetValue(i, out int j)) return j;
                    j = V.Count; map[i] = j;
                    V.Add(local.MultiplyPoint3x4(v[i]));
                    N.Add(n != null && n.Length == v.Length ? local.MultiplyVector(n[i]).normalized : Vector3.up);
                    U0.Add(!flat && uv != null && uv.Length == v.Length ? uv[i] : new Vector2(.5f, .5f));
                    U1.Add(cell); U2.Add(flags);
                    C.Add(cols != null ? cols[i].linear : lt);
                    return j;
                }
                for (int t = 0; t + 2 < tri.Length; t += 3)
                {
                    int a = Vx(tri[t]), b = Vx(tri[t + 1]), c = Vx(tri[t + 2]);
                    T.Add(a); if (flip) { T.Add(c); T.Add(b); } else { T.Add(b); T.Add(c); }
                }
            }

            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, U0); m.SetUVs(1, U1); m.SetUVs(2, U2); m.SetColors(C);
                m.SetTriangles(T, 0); m.RecalculateBounds();
                return m;
            }
        }

        static int Tris(Mesh m) { int n = 0; if (!m) return 0; for (int s = 0; s < m.subMeshCount; s++) n += (int)m.GetIndexCount(s) / 3; return n; }

        static Mesh SaveMesh(Mesh m, string path)
        {
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old) { old.Clear(); EditorUtility.CopySerialized(m, old); EditorUtility.SetDirty(old); return old; }
            AssetDatabase.CreateAsset(m, path); return m;
        }

        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = Path.GetDirectoryName(p).Replace('\\', '/'); Dir(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(p)); } }
    }
}
