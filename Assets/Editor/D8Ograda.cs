// D8 razdjelna ograda Kambelovac → Lukšić (28. 11., Marko with his photo of the approach to Lukšić: "na ovoj dionici u sredini nema
// oleandera — uzak razdjelni pojas s dvostrukom čeličnom ogradom; samo jedan grm na početku trake za lijevo skretanje"):
//   ograda   between the Kambelovac overpass (game x −4390) and the start of the Lukšić left-turn bay (−5905) the median carries a
//            DOUBLE guardrail: one W-beam along each median edge (just inside the raised kerbs), facing its carriageway, on short
//            posts every 2 m (beam centre 0.6 m), sloped terminals at the ends and at median openings. The road tiles' own single median rail (d02–d06 GLB `guardrail` inside the median and
//            its `rails` colliders) is dropped there so the two do not stand side by side; the outer rails stay as they are.
//            Colliders: 0.45 m boxes per ~6 m (Rail physics material), like the section rails.
//   bez živice  no median oleanders / hedge plants from the Lukšić junction to the overpass (OleanderRows, MedianHedge via NoHedge)
//            except ONE oleander bush where the median opens into the westbound left-turn bay (Bush).
// The median line is MedianHedge.MedianLine (the strip between the carriageways, 1 m columns); posts stand on the raised median
// (MedianKerbs, World/Roads/Razdjelni). Off: D8Ograda.On = false.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class D8Ograda
    {
        public static bool On = true;
        public const string NAME = "D8Ograda";
        /// guardrail stretch (game x) and the hedge-free stretch (the junction to the overpass)
        public const double X0 = -5905, X1 = -4390, HX0 = -6045;
        public const double BUSH_X = -5898;
        static List<(double x, double zc, double half)> cols;
        static Dictionary<int, (double x, double zc, double half)> byX;

        static void Cols(List<ModelJ> models)
        {
            if (cols != null) return;
            cols = MedianHedge.MedianLine(models, 1.0).Where(c => c.x > HX0 - 5 && c.x < X1 + 5).OrderBy(c => c.x).ToList();
            byX = new Dictionary<int, (double, double, double)>();
            foreach (var c in cols) byX[(int)Math.Floor(c.x)] = c;
        }
        public static void Reset() { cols = null; byX = null; }
        static bool Col(double x, out (double x, double zc, double half) c)
        {
            int k = (int)Math.Floor(x);
            for (int d = 0; d <= 2; d++) { if (byX.TryGetValue(k - d, out c) || byX.TryGetValue(k + d, out c)) return true; }
            c = default; return false;
        }
        public static bool InMedian(List<ModelJ> models, double x, double z, double x0, double x1, double pad)
        {
            if (!On || x < x0 || x > x1) return false;
            Cols(models);
            return Col(x, out var c) && Math.Abs(z - c.zc) < c.half + pad;
        }
        /// the bush at the start of the left-turn bay (game x, z)
        public static Vector2 Bush(List<ModelJ> models)
        {
            Cols(models); return Col(BUSH_X, out var c) ? new Vector2((float)c.x, (float)c.zc) : new Vector2(float.NaN, float.NaN);
        }
        /// no median hedge / oleander here (the bush itself excepted)
        public static bool NoHedge(double x, double z)
        {
            if (!On || x < HX0 || x > X1) return false;
            var models = WorldData.Models();
            if (!InMedian(models, x, z, HX0, X1, .9)) return false;
            var b = Bush(models); return !(Math.Abs(b.x - x) < .8 && Math.Abs(b.y - z) < .8);
        }
        /// a GLB guardrail triangle (model-local P, see WorldBuilder) inside the median of the guardrail stretch
        public static bool DropGlb(List<ModelJ> models, ModelJ m, Vector3 a, Vector3 b, Vector3 c)
        {
            if (!On) return false;
            var p = (a + b + c) / 3; double x = m.x + p.x, z = m.z - p.z;
            return InMedian(models, x, z, X0 - 3, X1 + 3, .45);
        }
        /// drop the section rails (colliders, RailSeams) that run inside the median of the stretch
        public static int FilterRails(List<ModelJ> models)
        {
            if (!On) return 0; int n = 0;
            foreach (var m in models)
                n += m.rails.RemoveAll(r => r.Count > 0 && r.Count(q => InMedian(models, q[0], q[1], X0 - 3, X1 + 3, .45)) * 2 > r.Count);
            return n;
        }

        static double[] Smooth(double[] a)
        {
            var r = new double[a.Length];
            for (int i = 0; i < a.Length; i++) { double s = 0; int k = 0; for (int d = -3; d <= 3; d++) if (i + d >= 0 && i + d < a.Length) { s += a[i + d]; k++; } r[i] = s / k; }
            return r;
        }

        public static string Build(List<ModelJ> models, Transform roads, Material steel, PhysicsMaterial railPhys)
        {
            var old = roads.Find(NAME); if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
            if (!On) return "[d8ograda] off";
            Reset(); Cols(models);
            var root = new GameObject(NAME); root.isStatic = true; root.transform.SetParent(roads, false);
            string dir = WorldBuilder.GEN + "/Roads/D8Ograda"; if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(WorldBuilder.GEN + "/Roads", "D8Ograda");
            Physics.SyncTransforms();
            // runs of median columns in the stretch
            var runs = new List<List<(double x, double zc, double half)>>(); List<(double, double, double)> cur = null; double lastX = -1e9;
            foreach (var c in cols.Where(c => c.x >= X0 && c.x <= X1))
            {
                if (cur == null || c.x - lastX > 1.6) { cur = new List<(double, double, double)>(); runs.Add(cur); }
                cur.Add(c); lastX = c.x;
            }
            runs.RemoveAll(r => r.Count < 8);
            float Top(Vector3 u)
            {
                float best = float.NaN;
                foreach (var h in Physics.RaycastAll(u + Vector3.up * 3, Vector3.down, 8, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider.transform.IsChildOf(root.transform)) continue;
                    if (MedianKerbs.IsMedian(h.collider)) return h.point.y;
                    if (float.IsNaN(best) || h.point.y > best) best = h.point.y;
                }
                return best;
            }
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            var meshes = new List<(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t)>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 o)
            {
                var nn = Vector3.Cross(b - a, c - a); if (nn.sqrMagnitude < 1e-12f) return; nn.Normalize();
                if (Vector3.Dot(nn, o) < 0) { (b, d) = (d, b); nn = -nn; }
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); for (int k = 0; k < 4; k++) n.Add(nn);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
                t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            void Box(Vector3 c, Vector3 f, Vector3 s)   // centre, forward (along), size (across, height, along)
            {
                var r = Vector3.Cross(Vector3.up, f).normalized; Vector3 X = r * s.x / 2, Y = Vector3.up * s.y / 2, Z = f * s.z / 2;
                Vector3 P(int i, int j, int k) => c + X * (i * 2 - 1) + Y * (j * 2 - 1) + Z * (k * 2 - 1);
                Quad(P(0, 1, 0), P(1, 1, 0), P(1, 1, 1), P(0, 1, 1), Vector3.up);
                Quad(P(0, 0, 0), P(0, 1, 0), P(0, 1, 1), P(0, 0, 1), -r); Quad(P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1), r);
                Quad(P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), -f); Quad(P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), f);
            }
            void Flush() { if (v.Count == 0) return; meshes.Add((new List<Vector3>(v), new List<Vector3>(n), new List<Vector2>(uv), new List<int>(t))); v.Clear(); n.Clear(); uv.Clear(); t.Clear(); }
            double metres = 0; int posts = 0, boxes = 0;
            var colRoot = new GameObject("kolizija"); colRoot.isStatic = true; colRoot.transform.SetParent(root.transform, false);
            foreach (var r in runs)
            {
                // smoothed centre line and half width; the two beam lines just inside the median kerbs (MedianKerbs: 0.18 m kerb)
                var zs = Smooth(r.Select(c => c.zc).ToArray()); var hs = Smooth(r.Select(c => c.half).ToArray());
                int N = r.Count;
                // beam height 0.6 m (centre), down to the ground over the last 4 m at both ends (terminals)
                float Hb(int i) { int e = Math.Min(i, N - 1 - i); return e >= 4 ? .6f : Mathf.Lerp(.08f, .6f, e / 4f); }
                foreach (int s in new[] { -1, 1 })   // -1: the north edge (facing the westbound carriageway), +1: the south edge
                {
                    var P = new Vector3[N]; var O = new Vector3[N];
                    for (int i = 0; i < N; i++)
                    {
                        double inset = Math.Max(.12, Math.Min(.38, hs[i] - .2)), z = zs[i] + s * (hs[i] - inset);
                        var u = W.U(r[i].x, 0, z); u.y = (float)Tiles.Height(r[i].x, z) + 2; float y = Top(u); u.y = float.IsNaN(y) ? (float)Tiles.Height(r[i].x, z) : y; P[i] = u;
                        O[i] = (W.U(r[i].x, 0, z + s) - W.U(r[i].x, 0, z)).normalized;   // outward = toward its carriageway
                    }
                    for (int i = 0; i + 1 < N; i++)
                    {
                        var a = P[i]; var b = P[i + 1]; var f = b - a; f.y = 0; float L = f.magnitude; if (L < .05f) continue; f /= L;
                        var o = O[i]; if (s < 0) metres += L;
                        // W-beam: two folds facing the carriageway (0.31 m tall), 0.1 m in front of the post line
                        Vector3 A0 = a + Vector3.up * Hb(i), B0 = b + Vector3.up * Hb(i + 1); float hh = .155f;
                        Quad(A0 + Vector3.up * hh, A0 + o * .05f + Vector3.up * hh * .35f, B0 + o * .05f + Vector3.up * hh * .35f, B0 + Vector3.up * hh, o + Vector3.up * .5f);
                        Quad(A0 + o * .05f + Vector3.up * hh * .35f, A0 + o * .02f, B0 + o * .02f, B0 + o * .05f + Vector3.up * hh * .35f, o - Vector3.up * .3f);
                        Quad(A0 + o * .02f, A0 + o * .05f - Vector3.up * hh * .35f, B0 + o * .05f - Vector3.up * hh * .35f, B0 + o * .02f, o + Vector3.up * .3f);
                        Quad(A0 + o * .05f - Vector3.up * hh * .35f, A0 - Vector3.up * hh, B0 - Vector3.up * hh, B0 + o * .05f - Vector3.up * hh * .35f, o - Vector3.up * .5f);
                        Quad(A0 + Vector3.up * hh, A0 - Vector3.up * hh, B0 - Vector3.up * hh, B0 + Vector3.up * hh, -o);   // back
                        if (i % 2 == 0 && i >= 2 && i <= N - 3)
                        {
                            Box(a - o * .17f + Vector3.up * .37f, f, new Vector3(.1f, .78f, .1f)); posts++;          // short post behind the beam
                            Box(a - o * .07f + Vector3.up * .6f, f, new Vector3(.12f, .14f, .1f));                    // spacer block
                        }
                        if (v.Count > 30000) Flush();
                    }
                    // colliders: a box per ~6 m along each beam
                    for (int i = 0; i + 1 < N; i += 6)
                    {
                        int j = Math.Min(i + 6, N - 1); var a = P[i]; var b = P[j]; var f = b - a; f.y = 0; float L = f.magnitude; if (L < .5f) continue;
                        var go = new GameObject("o"); go.isStatic = true; go.transform.SetParent(colRoot.transform, false);
                        go.transform.SetPositionAndRotation((a + b) / 2 - O[i] * .06f + Vector3.up * .45f, Quaternion.LookRotation(f / L, Vector3.up));
                        var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(.26f, .9f, L + .1f); bc.sharedMaterial = railPhys; boxes++;
                    }
                }
            }
            Flush();
            int idx = 0, tris = 0;
            foreach (var m in meshes)
            {
                var o = m.v[0]; o.y = 0;
                var mesh = new Mesh { name = $"d8ograda_{idx++}" };
                mesh.SetVertices(m.v.Select(p => p - o).ToList()); mesh.SetNormals(m.n); mesh.SetUVs(0, m.uv); mesh.SetTriangles(m.t, 0); mesh.RecalculateBounds();
                string p = $"{dir}/{mesh.name}.asset"; if (File.Exists(p)) AssetDatabase.DeleteAsset(p); AssetDatabase.CreateAsset(mesh, p);
                var go = new GameObject(mesh.name); go.isStatic = true; go.transform.SetParent(root.transform, false); go.transform.position = o;
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = steel; mr.shadowCastingMode = ShadowCastingMode.On;
                tris += m.t.Count / 3;
            }
            return $"[d8ograda] {runs.Count} runs, {metres:F0} m of median with two guardrails, {posts} posts, {boxes} collider boxes, {tris} tris";
        }
    }
}
