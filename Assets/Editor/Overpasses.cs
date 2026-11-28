// Nadvožnjaci (28. 11., Marko: "nadvožnjaci su očajni"): the bridge decks of the road GLBs (node `deck`: 41, 47, 51 over the D8,
// the d08 footbridge, the D8's own deck on d11) were drawn as they come — a 1.1 m slab with solid 0.85 m parapets in one noisy
// stone texture, square box piers — and read as a lumpy stone arch. The real overpasses over the D8 in Kaštela are a thin concrete
// slab on precast girders, a cornice / edge beam on both sides, pairs of ROUND piers under a pier cap in the median, blue steel
// railings (vertical bars) with framed mesh screens above them, and stairs with blue railings at the ends of a footbridge.
//
// The GLB deck is split into its parts (connected pieces of the mesh, by vertex colour class):
//   soffit   the underside strip (steel colour): its two long boundary chains give the bridge's stations (left / right edge, y of
//            the underside) every ~1.5 m; the deck top above each station is the GLB's road surface
//   piers    12-triangle kerb-concrete boxes reaching the ground: replaced by two round columns under a pier cap
//   main     road surface, kerbs, parapets, abutment / wing walls: over the span only the walkable top is kept (plus walls that
//            reach the ground: the abutments); the parapets and slab sides are dropped and rebuilt here
// Lanes, the road surface and the deck's collider are the GLB's as before (parapets still collide as the railing's barrier);
// only the box piers' collider is replaced by capsules on the round columns.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class Overpasses
    {
        public static bool On = true;
        /// decks that carry the D8 itself (railing without the anti-throw screen)
        static readonly string[] NoScreen = { "dionica-d11" };

        public class Bridge { public Vector3[] L, R; public float[] top; public float half; public bool foot, screen; public float extA, extB, ground; }
        public class Pier { public Vector3 c, a; public float la, lb, y0, y1; }
        public class Plan { public bool[] drop, dropColl; public List<Bridge> bridges = new List<Bridge>(); public List<Pier> piers = new List<Pier>(); }

        // ------------------------------------------------------------------ analysis
        /// cls: colour class of each GLB vertex (WorldBuilder.CLS: 0 asphalt, 1 paint, 2 steel, 3 kerb, 4 light concrete, 5 paving)
        public static Plan Analyze(ModelJ m, GlbMesh gm, Vector3[] P, Func<int, int> cls)
        {
            if (!On) return null;
            int nt = gm.idx.Length / 3;
            var plan = new Plan { drop = new bool[nt], dropColl = new bool[nt] };
            // union-find over welded positions + triangles
            var par = new int[P.Length]; for (int i = 0; i < par.Length; i++) par[i] = i;
            int F(int x) { while (par[x] != x) { par[x] = par[par[x]]; x = par[x]; } return x; }
            var weld = new Dictionary<Vector3Int, int>(); var wid = new int[P.Length];
            for (int i = 0; i < P.Length; i++)
            {
                var k = Vector3Int.RoundToInt(P[i] * 100);
                if (weld.TryGetValue(k, out int j)) { par[F(i)] = F(j); wid[i] = j; } else { weld[k] = i; wid[i] = i; }
            }
            for (int t = 0; t < nt; t++) { int a = gm.idx[3 * t], b = gm.idx[3 * t + 1], c = gm.idx[3 * t + 2]; par[F(b)] = F(a); par[F(c)] = F(a); }
            var comp = new Dictionary<int, List<int>>();
            for (int t = 0; t < nt; t++) { int r = F(gm.idx[3 * t]); if (!comp.TryGetValue(r, out var l)) comp[r] = l = new List<int>(); l.Add(t); }

            var mainTris = new List<int>(); var soffits = new List<List<int>>();
            foreach (var l in comp.Values)
            {
                int steel = 0, kerb = 0; float y0 = 1e9f, y1 = -1e9f;
                foreach (int t in l) { int c0 = cls(gm.idx[3 * t]); if (c0 == 2) steel++; if (c0 == 3) kerb++; for (int q = 0; q < 3; q++) { float y = P[gm.idx[3 * t + q]].y; y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); } }
                if (steel == l.Count) soffits.Add(l);
                else if (l.Count <= 12 && kerb * 2 >= l.Count && y1 - y0 > 2)
                {
                    var pr = PierOf(l, gm, P, y0, y1); if (pr == null) { mainTris.AddRange(l); continue; }
                    plan.piers.Add(pr); foreach (int t in l) { plan.drop[t] = true; plan.dropColl[t] = true; }
                }
                else mainTris.AddRange(l);
            }
            // the walkable up-faces of the main pieces (deck top heights)
            var up = new List<(Vector3 a, Vector3 b, Vector3 c)>();
            foreach (int t in mainTris)
            {
                Vector3 a = P[gm.idx[3 * t]], b = P[gm.idx[3 * t + 1]], c = P[gm.idx[3 * t + 2]];
                if (Mathf.Abs(Vector3.Cross(b - a, c - a).normalized.y) > .6f) up.Add((a, b, c));
            }
            float mainLow = float.MaxValue; foreach (int t in mainTris) for (int q = 0; q < 3; q++) mainLow = Mathf.Min(mainLow, P[gm.idx[3 * t + q]].y);
            foreach (var s in soffits)
            {
                var br = BridgeOf(s, gm, P, wid, up); if (br == null) continue;
                br.screen = !NoScreen.Contains(m.key); br.foot = br.half < 2.3f; br.ground = mainLow;
                plan.bridges.Add(br); foreach (int t in s) plan.drop[t] = true;   // (the soffit keeps its collider)
            }
            if (plan.bridges.Count == 0 && plan.piers.Count == 0) return null;
            // over the span: keep the deck top and the walls that reach the ground, drop the parapets / slab sides
            foreach (int t in mainTris)
            {
                Vector3 a = P[gm.idx[3 * t]], b = P[gm.idx[3 * t + 1]], c = P[gm.idx[3 * t + 2]];
                var ce = (a + b + c) / 3; var n = Vector3.Cross(b - a, c - a).normalized; float ymin = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
                foreach (var br in plan.bridges)
                {
                    if (!InSpan(br, ce, .7f, out float top, out float soff)) continue;
                    if (n.y > .6f && ce.y <= top + .4f) break;           // road / footway / kerb top
                    if (n.y < -.6f && ce.y <= top + .4f && ymin > soff - .3f) { plan.drop[t] = true; break; } // slab underside bits
                    if (ymin < soff - .8f) break;                         // abutment / wing wall down to the ground
                    plan.drop[t] = true; break;                           // parapet, slab side
                }
            }
            // a footbridge's walkway past the soffit ends (where the stairs start)
            foreach (var br in plan.bridges)
            {
                int N = br.L.Length;
                br.extA = Ext(br, 0, 1, up); br.extB = Ext(br, N - 1, N - 2, up);
            }
            return plan;
        }

        static float Ext(Bridge br, int i, int j, List<(Vector3 a, Vector3 b, Vector3 c)> up)
        {
            var M = (br.L[i] + br.R[i]) / 2; var d = (M - (br.L[j] + br.R[j]) / 2); d.y = 0; d.Normalize();
            var e = br.R[i] - br.L[i]; e.y = 0; e.Normalize(); float best = 0;
            foreach (var (a, b, c) in up)
                foreach (var v in new[] { a, b, c })
                {
                    var r = v - M; float s = Vector3.Dot(r, d), u = Vector3.Dot(r, e);
                    if (s > 0 && s < 10 && Mathf.Abs(u) < br.half && Mathf.Abs(v.y - br.top[i]) < .6f) best = Mathf.Max(best, s);
                }
            return best;
        }

        static Pier PierOf(List<int> l, GlbMesh gm, Vector3[] P, float y0, float y1)
        {
            var pts = new List<Vector3>(); foreach (int t in l) for (int q = 0; q < 3; q++) pts.Add(P[gm.idx[3 * t + q]]);
            var c = Vector3.zero; foreach (var p in pts) c += p; c /= pts.Count; c.y = 0;
            // the box's long axis: the longest horizontal edge between its corner points
            float best = 0; var ax = Vector3.right;
            for (int i = 0; i < pts.Count; i++) for (int j = i + 1; j < pts.Count; j++)
                {
                    var d = pts[j] - pts[i]; if (Mathf.Abs(d.y) > .05f) continue; d.y = 0; if (d.magnitude > best) { best = d.magnitude; ax = d.normalized; }
                }
            var bx = new Vector3(-ax.z, 0, ax.x); float a0 = 1e9f, a1 = -1e9f, b0 = 1e9f, b1 = -1e9f;
            foreach (var p in pts) { var r = p - c; r.y = 0; float u = Vector3.Dot(r, ax), v = Vector3.Dot(r, bx); a0 = Mathf.Min(a0, u); a1 = Mathf.Max(a1, u); b0 = Mathf.Min(b0, v); b1 = Mathf.Max(b1, v); }
            if (a1 - a0 < .3f) return null;
            c += ax * (a0 + a1) / 2 + bx * (b0 + b1) / 2;
            return new Pier { c = c, a = ax, la = a1 - a0, lb = b1 - b0, y0 = y0, y1 = y1 };
        }

        static Bridge BridgeOf(List<int> tris, GlbMesh gm, Vector3[] P, int[] wid, List<(Vector3 a, Vector3 b, Vector3 c)> up)
        {
            // boundary edges (welded ids) of the strip, walked as one loop
            var cnt = new Dictionary<(int, int), int>();
            foreach (int t in tris)
                for (int q = 0; q < 3; q++)
                {
                    int a = wid[gm.idx[3 * t + q]], b = wid[gm.idx[3 * t + (q + 1) % 3]]; var k = a < b ? (a, b) : (b, a);
                    cnt.TryGetValue(k, out int n); cnt[k] = n + 1;
                }
            var bnd = cnt.Where(kv => kv.Value == 1).Select(kv => kv.Key).ToList();
            var adj = new Dictionary<int, List<int>>();
            foreach (var (a, b) in bnd) { if (!adj.ContainsKey(a)) adj[a] = new List<int>(); if (!adj.ContainsKey(b)) adj[b] = new List<int>(); adj[a].Add(b); adj[b].Add(a); }
            if (adj.Count < 4 || adj.Values.Any(v => v.Count != 2)) return null;
            var loop = new List<int> { adj.Keys.First() };
            int prev = -1, cur = loop[0];
            while (true) { int nx = adj[cur][0] == prev ? adj[cur][1] : adj[cur][0]; if (nx == loop[0]) break; loop.Add(nx); prev = cur; cur = nx; if (loop.Count > adj.Count) return null; }
            if (loop.Count != adj.Count) return null;
            // the two ends: the two longest boundary edges
            int n0 = loop.Count; var len = new float[n0];
            for (int i = 0; i < n0; i++) { var d = P[loop[(i + 1) % n0]] - P[loop[i]]; d.y = 0; len[i] = d.magnitude; }
            var ord = Enumerable.Range(0, n0).OrderByDescending(i => len[i]).ToArray(); int e1 = ord[0], e2 = ord[1];
            if (Math.Abs(e1 - e2) == 1 || Math.Abs(e1 - e2) == n0 - 1) return null;
            if (e1 > e2) { int tmp = e1; e1 = e2; e2 = tmp; }
            var A = new List<Vector3>(); for (int i = e1 + 1; i <= e2; i++) A.Add(P[loop[i]]);
            var B = new List<Vector3>(); for (int i = e2 + 1; i <= e1 + n0; i++) B.Add(P[loop[i % n0]]);
            B.Reverse();   // both chains from the e1 end
            if ((A[0] - B[0]).sqrMagnitude > (A[0] - B[B.Count - 1]).sqrMagnitude) B.Reverse();
            float la = Len(A), lb = Len(B); if (la < 3) return null;
            int N = Mathf.Max(2, Mathf.CeilToInt(Mathf.Max(la, lb) / 1.5f) + 1);
            var br = new Bridge { L = new Vector3[N], R = new Vector3[N], top = new float[N] };
            float hw = 0;
            for (int i = 0; i < N; i++)
            {
                float f = i / (float)(N - 1); br.L[i] = At(A, f * la); br.R[i] = At(B, f * lb);
                var dd = br.R[i] - br.L[i]; dd.y = 0; hw += dd.magnitude / 2;
            }
            br.half = hw / N;
            // a consistent left / right: L on the left of the travel direction
            var dir = (br.L[N - 1] + br.R[N - 1]) - (br.L[0] + br.R[0]); var ac = br.R[0] - br.L[0];
            if (dir.x * ac.z - dir.z * ac.x > 0) { var t = br.L; br.L = br.R; br.R = t; }
            for (int i = 0; i < N; i++)
            {
                var M = (br.L[i] + br.R[i]) / 2; float soff = M.y, best = float.MaxValue;
                foreach (var (a, b, c) in up)
                {
                    if (!In2(M, a, b, c, out float y)) continue;
                    if (y > soff + .3f && y < soff + 3f && y < best) best = y;
                }
                br.top[i] = best < float.MaxValue ? best : soff + 1.1f;
            }
            // (a station with no top found above: from its neighbours)
            return br;
        }

        static float Len(List<Vector3> a) { float s = 0; for (int i = 0; i + 1 < a.Count; i++) s += Vector3.Distance(a[i], a[i + 1]); return s; }
        static Vector3 At(List<Vector3> a, float s)
        {
            for (int i = 0; i + 1 < a.Count; i++) { float L = Vector3.Distance(a[i], a[i + 1]); if (s <= L || i + 2 == a.Count) return Vector3.Lerp(a[i], a[i + 1], L > 1e-5f ? Mathf.Clamp01(s / L) : 0); s -= L; }
            return a[a.Count - 1];
        }
        static bool In2(Vector3 p, Vector3 a, Vector3 b, Vector3 c, out float y)
        {
            y = 0; float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z); if (Mathf.Abs(d) < 1e-6f) return false;
            float l1 = ((b.z - c.z) * (p.x - c.x) + (c.x - b.x) * (p.z - c.z)) / d, l2 = ((c.z - a.z) * (p.x - c.x) + (a.x - c.x) * (p.z - c.z)) / d, l3 = 1 - l1 - l2;
            if (l1 < -1e-4f || l2 < -1e-4f || l3 < -1e-4f) return false;
            y = l1 * a.y + l2 * b.y + l3 * c.y; return true;
        }
        /// p over the bridge's span (laterally widened by `pad`): the deck top and underside there
        static bool InSpan(Bridge br, Vector3 p, float pad, out float top, out float soff)
        {
            top = soff = 0;
            for (int i = 0; i + 1 < br.L.Length; i++)
            {
                Vector3 e0 = (br.R[i] - br.L[i]).normalized * pad, e1 = (br.R[i + 1] - br.L[i + 1]).normalized * pad;
                Vector3 a = br.L[i] - e0, b = br.R[i] + e0, c = br.R[i + 1] + e1, d = br.L[i + 1] - e1;
                if (In2(p, a, b, c, out _) || In2(p, a, c, d, out _))
                {
                    var m0 = (br.L[i] + br.R[i]) / 2; var m1 = (br.L[i + 1] + br.R[i + 1]) / 2; var dm = m1 - m0; dm.y = 0;
                    float f = dm.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector3.Dot(p - m0, dm) / dm.sqrMagnitude) : 0;
                    top = Mathf.Lerp(br.top[i], br.top[i + 1], f); soff = Mathf.Lerp(m0.y, m1.y, f); return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ materials
        static Material concrete, blue, bars, screen;
        public static Material Concrete()
        {
            // Marko's approved concrete_wall_007 (Poly Haven CC0), clean light grey, ~3 m per repeat on the metre UVs below
            var m = WorldBuilder.Lit("MostBeton", new Color(.93f, .93f, .91f), .12f, 0, "PolyHaven/concrete_wall_007_d.jpg", "PolyHaven/concrete_wall_007_n.jpg", .5f);
            m.SetTextureScale("_BaseMap", new Vector2(.33f, .33f)); EditorUtility.SetDirty(m); return m;
        }
        static void Mats()
        {
            concrete = Concrete();
            blue = WorldBuilder.Lit("MostPlava", new Color(.09f, .29f, .66f), .5f, .35f);
            bars = Cut("MostResetka", Grid("most_resetka", true));
            screen = Cut("MostMreza", Grid("most_mreza3", false));
        }
        static Material Cut(string name, Texture2D tex)
        {
            var m = WorldBuilder.Lit(name, new Color(.09f, .29f, .66f), .45f, .35f);
            m.SetTexture("_BaseMap", tex); m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", .5f); m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", 0); m.doubleSidedGI = true; m.renderQueue = (int)RenderQueue.AlphaTest; m.SetOverrideTag("RenderType", "TransparentCutout");
            EditorUtility.SetDirty(m); return m;
        }
        /// alpha textures: bars = one vertical bar per tile (u along the rail), mesh = a square wire cell
        static Texture2D Grid(string name, bool barsOnly)
        {
            string p = WorldBuilder.GEN + "/Materials/" + name + ".png";
            if (!File.Exists(p))
            {
                int S = 64; var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
                for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
                    {
                        bool on = barsOnly ? (x < 9) : (x < 3 || y < 3);
                        t.SetPixel(x, y, new Color(1, 1, 1, on ? 1 : 0));
                    }
                File.WriteAllBytes(p, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t); AssetDatabase.ImportAsset(p);
                var imp = (TextureImporter)AssetImporter.GetAtPath(p);
                imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.mipMapsPreserveCoverage = barsOnly;   // (the fine mesh fades out with distance, as the real screens do) imp.alphaTestReferenceValue = .5f;
                imp.wrapMode = TextureWrapMode.Repeat; imp.anisoLevel = 4; imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }

        // ------------------------------------------------------------------ geometry
        class Geo
        {
            public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int>[] sub = { new List<int>(), new List<int>(), new List<int>(), new List<int>() };
            /// a quad a-b-c-d facing `out` (winding fixed to it); uv: metres on the face's plane (or given)
            public void Quad(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 o, Vector2[] uvs = null)
            {
                var nn = Vector3.Cross(b - a, c - a); if (nn.sqrMagnitude < 1e-10f) nn = Vector3.Cross(c - a, d - a);
                nn.Normalize(); if (Vector3.Dot(nn, o) < 0) { (b, d) = (d, b); nn = -nn; if (uvs != null) (uvs[1], uvs[3]) = (uvs[3], uvs[1]); }
                int i0 = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); for (int k = 0; k < 4; k++) n.Add(nn);
                if (uvs != null) uv.AddRange(uvs); else foreach (var p in new[] { a, b, c, d }) uv.Add(UV(p, nn));
                sub[s].AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }
            public void Tri(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 o)
            {
                var nn = Vector3.Cross(b - a, c - a).normalized; if (Vector3.Dot(nn, o) < 0) { (b, c) = (c, b); nn = -nn; }
                int i0 = v.Count; v.Add(a); v.Add(b); v.Add(c); for (int k = 0; k < 3; k++) { n.Add(nn); }
                uv.Add(UV(a, nn)); uv.Add(UV(b, nn)); uv.Add(UV(c, nn)); sub[s].AddRange(new[] { i0, i0 + 1, i0 + 2 });
            }
            static Vector2 UV(Vector3 p, Vector3 nn)
            {
                if (Mathf.Abs(nn.y) > .6f) return new Vector2(p.x, p.z);
                var t = new Vector3(-nn.z, 0, nn.x).normalized; return new Vector2(Vector3.Dot(p, t), p.y);
            }
            /// an axis box: centre, half sizes along the unit axes ax (horizontal), up, bx
            public void Box(int s, Vector3 c, Vector3 ax, float ha, float hy, float hb, bool bottom = false)
            {
                var bx = Vector3.Cross(Vector3.up, ax).normalized; Vector3 A = ax * ha, Y = Vector3.up * hy, Bv = bx * hb;
                Vector3 P(int i, int j, int k) => c + A * i + Y * j + Bv * k;
                Quad(s, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), ax); Quad(s, P(-1, -1, -1), P(-1, 1, -1), P(-1, 1, 1), P(-1, -1, 1), -ax);
                Quad(s, P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), bx); Quad(s, P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), -bx);
                Quad(s, P(-1, 1, -1), P(1, 1, -1), P(1, 1, 1), P(-1, 1, 1), Vector3.up);
                if (bottom) Quad(s, P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), Vector3.down);
            }
            /// a box beam from a to b (any slope), section w (horizontal, across) × h (vertical)
            public void Beam(int s, Vector3 a, Vector3 b, float w, float h)
            {
                var d = b - a; if (d.sqrMagnitude < 1e-6f) return; var dh = d; dh.y = 0;
                var side = dh.sqrMagnitude > 1e-6f ? Vector3.Cross(Vector3.up, dh).normalized : Vector3.right;
                Vector3 S = side * w / 2, U = Vector3.up * h / 2;
                Quad(s, a + S + U, b + S + U, b - S + U, a - S + U, Vector3.up); Quad(s, a + S - U, b + S - U, b - S - U, a - S - U, Vector3.down);
                Quad(s, a + S - U, b + S - U, b + S + U, a + S + U, side); Quad(s, a - S - U, b - S - U, b - S + U, a - S + U, -side);
            }
            public void Cyl(int s, Vector3 b, float r, float h, int seg = 14)
            {
                for (int i = 0; i < seg; i++)
                {
                    float a0 = i * Mathf.PI * 2 / seg, a1 = (i + 1) * Mathf.PI * 2 / seg;
                    var p0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)); var p1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                    int i0 = v.Count; float u0 = r * a0, u1 = r * a1;
                    v.Add(b + p0 * r); v.Add(b + p1 * r); v.Add(b + p1 * r + Vector3.up * h); v.Add(b + p0 * r + Vector3.up * h);
                    n.Add(p0); n.Add(p1); n.Add(p1); n.Add(p0);
                    uv.Add(new Vector2(u0, b.y)); uv.Add(new Vector2(u1, b.y)); uv.Add(new Vector2(u1, b.y + h)); uv.Add(new Vector2(u0, b.y + h));
                    // (Unity: clockwise seen from outside)
                    sub[s].AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
                }
            }
            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
                var used = Enumerable.Range(0, sub.Length).Where(i => sub[i].Count > 0).ToArray(); m.subMeshCount = used.Length;
                for (int i = 0; i < used.Length; i++) m.SetTriangles(sub[used[i]], i);
                m.RecalculateTangents(); m.RecalculateBounds(); Used = used; return m;
            }
            public int[] Used;
        }
        const int CON = 0, BLUE = 1, BARS = 2, SCREEN = 3;

        /// the procedural bridges / piers / stairs of model m as child "most" of its road object (local frame of the model)
        public static int Build(ModelJ m, GameObject go, Plan plan, List<ModelJ> models)
        {
            if (plan == null) return 0;
            Mats();
            var g = new Geo();
            foreach (var br in plan.bridges) Deck(g, br);
            foreach (var pr in plan.piers) PierGeo(g, pr);
            foreach (var br in plan.bridges) if (br.foot) { Stairs(g, br, 0, 1, br.extA, -1, m, models); Stairs(g, br, br.L.Length - 1, br.L.Length - 2, br.extB, 1, m, models); }
            var mesh = g.Mesh(m.key + "-most");
            string path = $"{WorldBuilder.GEN}/Roads/{m.key}-most.asset";
            if (File.Exists(path)) AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path);
            var o = new GameObject("most"); o.isStatic = true; o.transform.SetParent(go.transform, false);
            o.AddComponent<MeshFilter>().sharedMesh = mesh;
            var all = new[] { concrete, blue, bars, screen };
            var mr = o.AddComponent<MeshRenderer>(); mr.sharedMaterials = g.Used.Select(i => all[i]).ToArray(); mr.shadowCastingMode = ShadowCastingMode.On;
            // the round columns collide (the box piers' collider is gone)
            foreach (var pr in plan.piers)
                foreach (var (c, r, h) in Columns(pr))
                {
                    var cc = new GameObject("stup"); cc.isStatic = true; cc.transform.SetParent(o.transform, false); cc.transform.localPosition = c + Vector3.up * h / 2;
                    var cap = cc.AddComponent<CapsuleCollider>(); cap.radius = r; cap.height = h + 2 * r; cap.direction = 1;
                }
            return mesh.triangles.Length / 3;
        }

        static IEnumerable<(Vector3 c, float r, float h)> Columns(Pier p)
        {
            float D = p.la >= 3.5f ? 1.0f : .7f, off = Mathf.Max(D / 2 + .15f, p.la / 2 - D / 2 - .2f);
            float capH = p.la >= 3.5f ? .9f : .6f, y0 = p.y0 - .4f, h = p.y1 - capH - y0;
            for (int s = -1; s <= 1; s += 2) yield return (p.c + p.a * off * s + Vector3.up * y0, D / 2, h);
        }

        static void PierGeo(Geo g, Pier p)
        {
            float D = p.la >= 3.5f ? 1.0f : .7f, capH = p.la >= 3.5f ? .9f : .6f;
            foreach (var (c, r, h) in Columns(p)) g.Cyl(CON, c, r, h + .02f, 16);
            // the pier cap (crosshead): the girders sit on it
            float ha = p.la / 2 + .45f, hb = Mathf.Max(p.lb, 1.1f) / 2 + .1f;
            g.Box(CON, p.c + Vector3.up * (p.y1 - capH / 2), p.a, ha, capH / 2, hb, true);
        }

        static void Deck(Geo g, Bridge br)
        {
            int N = br.L.Length; float half = br.half;
            var M = new Vector3[N]; var E = new Vector3[N]; var T = new float[N];
            for (int i = 0; i < N; i++)
            {
                M[i] = (br.L[i] + br.R[i]) / 2; var e = br.R[i] - br.L[i]; e.y = 0; E[i] = e.normalized; T[i] = br.top[i] - M[i].y;
            }
            Vector3 At(int i, float u, float dy) => new Vector3(M[i].x, br.top[i] + dy, M[i].z) + E[i] * u;
            // the deck's depth below the road (girders down to the GLB underside, so the clearance stays)
            float Dep(int i) => Mathf.Clamp(T[i], .7f, 2.2f);
            // slab underside
            for (int i = 0; i + 1 < N; i++)
                g.Quad(CON, At(i, -half + .3f, -.3f), At(i, half - .3f, -.3f), At(i + 1, half - .3f, -.3f), At(i + 1, -half + .3f, -.3f), Vector3.down);
            // edge beam + cornice + cap on both sides, swept profile (lateral from the edge inwards, dy from the road)
            for (int s = -1; s <= 1; s += 2)
            {
                var prof = new[] { (-.5f, -.02f), (-.5f, .25f), (.12f, .25f), (.12f, -.12f), (0f, -.12f), (0f, -.95f), (-.3f, -.95f), (-.3f, -.3f) };
                for (int k = 0; k + 1 < prof.Length; k++)
                    for (int i = 0; i + 1 < N; i++)
                    {
                        float u0 = s * (half + prof[k].Item1), u1 = s * (half + prof[k + 1].Item1);
                        float d0 = prof[k].Item2, d1 = prof[k + 1].Item2;
                        if (k == 5) { d0 = -.12f; d1 = -Mathf.Min(.95f, Dep(i) - .05f); }
                        if (k == 6 || k == 7) { if (k == 6) d0 = -Mathf.Min(.95f, Dep(i) - .05f); d1 = k == 6 ? d0 : -.3f; if (k == 7) d0 = -Mathf.Min(.95f, Dep(i) - .05f); }
                        var a = At(i, u0, d0); var b = At(i, u1, d1); var c = At(i + 1, u1, d1); var d = At(i + 1, u0, d0);
                        // outward: away from a point inside the profile
                        var inside = (At(i, s * (half - .2f), -.05f) + At(i + 1, s * (half - .2f), -.05f)) / 2;
                        var mid = (a + b + c + d) / 4; var o = mid - inside; var along = M[i + 1] - M[i]; along.Normalize();
                        o -= Vector3.Dot(o, along) * along;
                        g.Quad(CON, a, b, c, d, o);
                    }
            }
            // precast girders: webs + bottom flanges, from the slab to the GLB underside
            int ng = Mathf.Max(2, Mathf.RoundToInt((2 * half - 1.2f) / 1.5f) + 1);
            for (int j = 0; j < ng; j++)
            {
                float u = Mathf.Lerp(-half + .75f, half - .75f, ng == 1 ? .5f : j / (float)(ng - 1));
                for (int i = 0; i + 1 < N; i++)
                {
                    float b0 = -Dep(i), b1 = -Dep(i + 1); if (Dep(i) < .75f) continue;
                    foreach (int s in new[] { -1, 1 })
                    {
                        // web side, flange side, flange top
                        g.Quad(CON, At(i, u + s * .11f, -.3f), At(i, u + s * .11f, b0 + .22f), At(i + 1, u + s * .11f, b1 + .22f), At(i + 1, u + s * .11f, -.3f), E[i] * s);
                        g.Quad(CON, At(i, u + s * .33f, b0 + .22f), At(i, u + s * .33f, b0), At(i + 1, u + s * .33f, b1), At(i + 1, u + s * .33f, b1 + .22f), E[i] * s);
                        g.Quad(CON, At(i, u + s * .11f, b0 + .22f), At(i, u + s * .33f, b0 + .16f), At(i + 1, u + s * .33f, b1 + .16f), At(i + 1, u + s * .11f, b1 + .22f), Vector3.up + E[i] * s);
                    }
                    g.Quad(CON, At(i, u - .33f, b0), At(i, u + .33f, b0), At(i + 1, u + .33f, b1), At(i + 1, u - .33f, b1), Vector3.down);
                }
            }
            // end diaphragms (closing the girders and the slab at both ends)
            foreach (int i in new[] { 0, N - 1 })
            {
                var o = i == 0 ? M[0] - M[1] : M[N - 1] - M[N - 2]; o.y = 0;
                g.Quad(CON, At(i, -half, -.02f), At(i, half, -.02f), At(i, half, -Dep(i)), At(i, -half, -Dep(i)), o);
            }
            // blue railing on the cap of both edges, with the framed mesh screen above it
            for (int s = -1; s <= 1; s += 2)
            {
                var line = new Vector3[N]; for (int i = 0; i < N; i++) line[i] = At(i, s * (half - .2f), .25f);
                Railing(g, line, br.screen ? 2.6f : 0, br.foot);
            }
        }

        /// blue steel railing along a base line (cap top): posts ~every 2 m, hand rail 1.1 m, bottom rail, vertical bars; screen > 0:
        /// posts to that height, a framed wire mesh panel from 1.2 m to it
        static void Railing(Geo g, Vector3[] line, float screen, bool foot)
        {
            float H = 1.1f; float total = 0; for (int i = 0; i + 1 < line.Length; i++) total += Vector3.Distance(line[i], line[i + 1]);
            if (total < .5f) return;
            int np = Mathf.Max(1, Mathf.RoundToInt(total / 2f)); float step = total / np;
            Vector3 P(float s) { for (int i = 0; i + 1 < line.Length; i++) { float L = Vector3.Distance(line[i], line[i + 1]); if (s <= L || i + 2 == line.Length) return Vector3.Lerp(line[i], line[i + 1], L > 1e-5f ? Mathf.Clamp01(s / L) : 0); s -= L; } return line[line.Length - 1]; }
            float top = screen > 0 ? screen : H;
            for (int k = 0; k <= np; k++)
            {
                var p = P(k * step); var d = P(Mathf.Min(total, k * step + .1f)) - P(Mathf.Max(0, k * step - .1f)); d.y = 0; d = d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.right;
                g.Box(BLUE, p + Vector3.up * top / 2, d, .04f, top / 2, .04f);
            }
            float run = 0;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                Vector3 a = line[i], b = line[i + 1]; float L = Vector3.Distance(a, b); if (L < 1e-3f) continue;
                g.Beam(BLUE, a + Vector3.up * H, b + Vector3.up * H, .07f, .06f);
                g.Beam(BLUE, a + Vector3.up * .1f, b + Vector3.up * .1f, .05f, .04f);
                var side = Vector3.Cross(Vector3.up, b - a).normalized;
                // bars: one per 0.13 m (texture u), between the rails
                g.Quad(BARS, a + Vector3.up * .12f, b + Vector3.up * .12f, b + Vector3.up * (H - .03f), a + Vector3.up * (H - .03f), side,
                    new[] { new Vector2(run / .13f, 0), new Vector2((run + L) / .13f, 0), new Vector2((run + L) / .13f, 1), new Vector2(run / .13f, 1) });
                if (screen > 0)
                {
                    g.Beam(BLUE, a + Vector3.up * (H + .1f), b + Vector3.up * (H + .1f), .05f, .05f);
                    g.Beam(BLUE, a + Vector3.up * screen, b + Vector3.up * screen, .06f, .06f);
                    float h0 = H + .12f, h1 = screen - .03f;
                    g.Quad(SCREEN, a + Vector3.up * h0, b + Vector3.up * h0, b + Vector3.up * h1, a + Vector3.up * h1, side,
                        new[] { new Vector2(run / .08f, h0 / .08f), new Vector2((run + L) / .08f, h0 / .08f), new Vector2((run + L) / .08f, h1 / .08f), new Vector2(run / .08f, h1 / .08f) });
                }
                run += L;
            }
        }

        /// a footbridge's stairs: a landing past the walkway's end, then a straight flight along the road below (side = ±across),
        /// concrete stringers and steps, blue railings both sides, a round column under the landing
        static void Stairs(Geo g, Bridge br, int i, int j, float ext, int side, ModelJ m, List<ModelJ> models)
        {
            var M = (br.L[i] + br.R[i]) / 2; var d = M - (br.L[j] + br.R[j]) / 2; d.y = 0; d.Normalize();
            var e = br.R[i] - br.L[i]; e.y = 0; e.Normalize(); e *= side;
            float y = br.top[i]; const float W = 1.8f, LAND = 2.2f, RISE = .17f, TREAD = .29f;
            double Ground(Vector3 p)
            {
                double gx = m.x + p.x, gz = m.z - p.z, h = Tiles.Height(gx, gz);
                var q = WorldData.ModelTerrain(models, gx, gz, 2, h); double v = (q ?? h) - m.y;
                // (no terrain data there: the abutment's foot)
                return double.IsNaN(v) || double.IsInfinity(v) || v > y - .5 ? br.ground : v;
            }
            // landing: centred LAND/2 past the walkway end, as wide as the deck and the flight together
            var lc = M + d * (ext + LAND / 2); lc.y = y;
            g.Box(CON, lc + Vector3.up * -.15f, d, LAND / 2, .15f, br.half + .1f, true);
            float gy0 = (float)Ground(lc); if (y - gy0 > .6f) foreach (var off in new[] { -1f, 1f }) { var cb = lc + e * off * (br.half * .6f); cb.y = gy0 - .3f; g.Cyl(CON, cb, .3f, y - .3f - cb.y, 12); }
            // the flight: from the landing's side edge (e), centred on the landing along d, down to the ground
            var across = d; int n = 0; float yy = y;
            var cl = lc + e * (br.half + .1f);
            float ground = (float)Ground(cl);
            while (yy - RISE > ground - .05f && n < 60)
            {
                var p0 = cl + e * (n * TREAD); var p1 = p0 + e * TREAD; float y1 = yy - RISE;
                // riser + tread
                g.Quad(CON, new Vector3(p0.x, yy, p0.z) - across * W / 2, new Vector3(p0.x, yy, p0.z) + across * W / 2, new Vector3(p0.x, y1, p0.z) + across * W / 2, new Vector3(p0.x, y1, p0.z) - across * W / 2, e);
                g.Quad(CON, new Vector3(p0.x, y1, p0.z) - across * W / 2, new Vector3(p0.x, y1, p0.z) + across * W / 2, new Vector3(p1.x, y1, p1.z) + across * W / 2, new Vector3(p1.x, y1, p1.z) - across * W / 2, Vector3.up);
                yy = y1; n++; ground = (float)Ground(p1);
            }
            if (n == 0) return;
            var end = cl + e * (n * TREAD); float yEnd = yy;
            // the flight's sloped underside and the two stringers (0.2 m wide, 0.35 m above the nosings)
            Vector3 A0 = new Vector3(cl.x, y, cl.z), A1 = new Vector3(end.x, yEnd, end.z);
            g.Quad(CON, A0 - across * W / 2 + Vector3.down * .35f, A0 + across * W / 2 + Vector3.down * .35f, A1 + across * W / 2 + Vector3.down * .35f, A1 - across * W / 2 + Vector3.down * .35f, Vector3.down - e * .5f);
            foreach (float s in new[] { -1f, 1f })
            {
                var o = across * s * (W / 2 + .1f);
                g.Beam(CON, A0 + o + Vector3.up * -.05f, A1 + o + Vector3.up * -.05f, .2f, .75f);
                // the railing on the stringer: posts + hand rail + bars, following the slope
                var b0 = A0 + o + Vector3.up * .32f; var b1 = A1 + o + Vector3.up * .32f; float L = Vector3.Distance(b0, b1);
                int np = Mathf.Max(1, Mathf.RoundToInt(L / 1.8f));
                for (int k = 0; k <= np; k++) { var p = Vector3.Lerp(b0, b1, k / (float)np); g.Box(BLUE, p + Vector3.up * .55f, e, .04f, .55f, .04f); }
                g.Beam(BLUE, b0 + Vector3.up * 1.05f, b1 + Vector3.up * 1.05f, .07f, .06f);
                g.Beam(BLUE, b0 + Vector3.up * .1f, b1 + Vector3.up * .1f, .05f, .04f);
                g.Quad(BARS, b0 + Vector3.up * .12f, b1 + Vector3.up * .12f, b1 + Vector3.up * 1.0f, b0 + Vector3.up * 1.0f, across * s,
                    new[] { new Vector2(0, 0), new Vector2(L / .13f, 0), new Vector2(L / .13f, 1), new Vector2(0, 1) });
            }
            // landing railing on its outer end (across the walkway's end, away from the bridge) and its free side
            var le = lc + d * (LAND / 2 - .1f);
            Railing(g, new[] { le - e * (br.half + .1f) + Vector3.up * 0, le + e * (br.half + .1f) }, 0, true);
        }
    }
}
