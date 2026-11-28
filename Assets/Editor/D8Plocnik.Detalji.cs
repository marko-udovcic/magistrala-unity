// D8 pločnik — the small things on and along it (D8Plocnik.cs): manhole covers on the slab, gully grates in the gutter at the kerb,
// grey utility cabinets behind the sidewalk. Positions from the Mapillary inventory (d8rub.json: manhole / drain / cabinet with a
// side north / south), snapped to the nearest built section of that side; where it is thin a cover every ~70 m and a grate every
// ~45 m (seeded by position). One procedural mesh per 300 m (four materials), cabinets with box colliders.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static partial class D8Plocnik
    {
        public static int NManhole, NGrate, NCabinet;
        class Acc { public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<int>[] t = { new List<int>(), new List<int>(), new List<int>(), new List<int>() }; }

        static void Face(Acc a, int sub, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 nn)
        {
            int i = a.v.Count; a.v.Add(p0); a.v.Add(p1); a.v.Add(p2); a.v.Add(p3);
            for (int k = 0; k < 4; k++) a.n.Add(nn);
            a.uv.Add(new Vector2(0, 0)); a.uv.Add(new Vector2(0, 1)); a.uv.Add(new Vector2(1, 1)); a.uv.Add(new Vector2(1, 0));
            if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), nn) >= 0) a.t[sub].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else a.t[sub].AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }
        /// box: centre bottom c, along t, out n, size (along, height, across)
        static void BoxAt(Acc a, int sub, Vector3 c, Vector3 t, Vector3 n, Vector3 s, bool bottom = false)
        {
            Vector3 X = t * s.x / 2, Y = Vector3.up * s.y, Z = n * s.z / 2;
            Vector3 P(int i, int j, int k) => c + X * (i * 2 - 1) + Y * j + Z * (k * 2 - 1);
            Face(a, sub, P(0, 1, 0), P(0, 1, 1), P(1, 1, 1), P(1, 1, 0), Vector3.up);
            Face(a, sub, P(0, 0, 0), P(0, 1, 0), P(1, 1, 0), P(1, 0, 0), -n);
            Face(a, sub, P(0, 0, 1), P(0, 1, 1), P(1, 1, 1), P(1, 0, 1), n);
            Face(a, sub, P(0, 0, 0), P(0, 1, 0), P(0, 1, 1), P(0, 0, 1), -t);
            Face(a, sub, P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1), t);
        }
        static void Disc(Acc a, int sub, Vector3 c, float r, float h)
        {
            const int N = 18; int i0 = a.v.Count;
            a.v.Add(c + Vector3.up * h); a.n.Add(Vector3.up); a.uv.Add(new Vector2(.5f, .5f));
            for (int k = 0; k < N; k++) { float t = k * Mathf.PI * 2 / N; a.v.Add(c + new Vector3(Mathf.Cos(t) * r, h, Mathf.Sin(t) * r)); a.n.Add(Vector3.up); a.uv.Add(new Vector2(.5f + Mathf.Cos(t) * .5f, .5f + Mathf.Sin(t) * .5f)); }
            for (int k = 0; k < N; k++) a.t[sub].AddRange(new[] { i0, i0 + 1 + (k + 1) % N, i0 + 1 + k });
            for (int k = 0; k < N; k++)
            {
                float t0 = k * Mathf.PI * 2 / N, t1 = (k + 1) * Mathf.PI * 2 / N;
                Vector3 d0 = new Vector3(Mathf.Cos(t0), 0, Mathf.Sin(t0)), d1 = new Vector3(Mathf.Cos(t1), 0, Mathf.Sin(t1));
                Face(a, sub, c + d0 * r, c + d0 * r + Vector3.up * h, c + d1 * r + Vector3.up * h, c + d1 * r, (d0 + d1).normalized);
            }
        }

        static string Details(Transform root, List<(Run r, List<Vector3[]> s, List<float> top)> secs, string dir)
        {
            NManhole = NGrate = NCabinet = 0;
            var go = new GameObject("Detalji"); go.isStatic = true; go.transform.SetParent(root, false);
            var iron = WorldBuilder.Lit("D8Sahta", new Color(.2f, .19f, .18f), .3f, .4f);
            var grate = WorldBuilder.Lit("D8Slivnik", new Color(.09f, .09f, .09f), .25f, .5f);
            var cab = WorldBuilder.Lit("D8Ormaric", new Color(.74f, .75f, .74f), .25f, 0);
            var plinth = WorldBuilder.Lit("D8OrmaricPodnozje", new Color(.55f, .55f, .53f), .1f, 0);
            var acc = new Dictionary<int, Acc>();
            Acc A(Vector3 u) { int k = (int)Math.Floor(W.G(u).x / 300); if (!acc.TryGetValue(k, out var a)) acc[k] = a = new Acc(); return a; }
            // nearest section of a side to game (x, z) within reach: (run, index)
            (int r, int i) Near(bool south, double x, double z, double reach)
            {
                int br = -1, bi = -1; double bd = reach * reach;
                for (int r = 0; r < secs.Count; r++)
                {
                    if (secs[r].r.south != south) continue;
                    var P = secs[r].r.p;
                    for (int i = 0; i < P.Count; i++) { var g = W.G(P[i]); double d = (g.x - x) * (g.x - x) + (g.y - z) * (g.y - z); if (d < bd) { bd = d; br = r; bi = i; } }
                }
                return (br, bi);
            }
            var used = new List<(string k, Vector2 g)>();
            bool Free(string k, Vector2 g, float d) => !used.Any(u => u.k == k && (u.g - g).sqrMagnitude < d * d);
            Vector3 Tan(Run r, int i) { var t = r.p[Math.Min(i + 1, r.p.Count - 1)] - r.p[Math.Max(i - 1, 0)]; t.y = 0; return t.normalized; }
            void Manhole(int r, int i)
            {
                var run = secs[r].r; if (run.low[i] > .1f) return; var g = W.G(run.p[i]); if (!Free("m", g, 12)) return;
                var c = run.p[i] + run.n[i] * (KW + (WID - KW - OKW) * .5f); c.y = secs[r].top[i];
                Disc(A(c), 0, c, .32f, .012f); used.Add(("m", g)); NManhole++;
            }
            void Grate(int r, int i)
            {
                var run = secs[r].r; var g = W.G(run.p[i]); if (!Free("g", g, 15)) return;
                var t = Tan(run, i); var c = run.p[i] - run.n[i] * .24f; c.y = secs[r].s[i][0].y + .05f - .004f;   // the road surface
                BoxAt(A(c), 1, c, t, run.n[i], new Vector3(.55f, .012f, .42f)); used.Add(("g", g)); NGrate++;
            }
            void Cabinet(int r, int i, int seed)
            {
                var run = secs[r].r; var g = W.G(run.p[i]); if (!Free("c", g, 3.5f)) return;
                var t = Tan(run, i); var n = run.n[i];
                var c = run.p[i] + n * (WID + .3f); c.y = secs[r].s[i][6].y + .12f;   // the bank's foot behind the sidewalk
                // (on the ground there: drop it)
                if (Physics.Raycast(c + Vector3.up * 2, Vector3.down, out var h, 4, ~0, QueryTriggerInteraction.Ignore) && !h.collider.transform.IsChildOf(root)) c.y = h.point.y;
                float wide = NaturePass.R01(g.x, g.y, seed) < .5f ? .8f : 1.05f, tall = NaturePass.R01(g.x, g.y, seed + 1) < .5f ? 1.1f : 1.3f;
                var a = A(c);
                BoxAt(a, 3, c - Vector3.up * .15f, t, n, new Vector3(wide + .1f, .25f, .45f));
                BoxAt(a, 2, c + Vector3.up * .1f, t, n, new Vector3(wide, tall, .34f));
                BoxAt(a, 2, c + Vector3.up * (.1f + tall), t, n, new Vector3(wide + .04f, .03f, .38f));   // the lid's lip
                var col = new GameObject("ormaric"); col.isStatic = true; col.transform.SetParent(go.transform, false);
                col.transform.SetPositionAndRotation(c + Vector3.up * (.1f + tall / 2), Quaternion.LookRotation(n, Vector3.up));
                col.AddComponent<BoxCollider>().size = new Vector3(wide, tall + .2f, .34f);
                used.Add(("c", g)); NCabinet++;
            }
            if (inv == null) LoadInv();
            // the inventory first (detected before candidates)
            foreach (var p in inv.Where(p => p.side == "north" || p.side == "south").OrderByDescending(p => p.conf))
            {
                bool south = p.side == "south";
                if (p.kind == "manhole") { var (r, i) = Near(south, p.x, p.z, 8); if (r >= 0) Manhole(r, i); }
                else if (p.kind == "drain") { var (r, i) = Near(south, p.x, p.z, 8); if (r >= 0) Grate(r, i); }
                else if (p.kind == "cabinet") { var (r, i) = Near(south, p.x, p.z, 9); if (r >= 0) Cabinet(r, i, 901); }
            }
            // plausible spacing where the inventory is thin
            for (int r = 0; r < secs.Count; r++)
            {
                var run = secs[r].r;
                for (int i = 6; i < run.p.Count - 6; i++)
                {
                    var g = W.G(run.p[i]);
                    if (Free("m", g, 70) && NaturePass.R01(g.x, g.y, 911) < .08f) Manhole(r, i);
                    if (Free("g", g, 45) && NaturePass.R01(g.x, g.y, 913) < .1f) Grate(r, i);
                }
            }
            foreach (var kv in acc.OrderBy(k => k.Key))
            {
                var a = kv.Value; if (a.v.Count == 0) continue;
                var o = a.v[0]; o.y = 0;
                var mesh = new Mesh { name = $"d8plocnik_detalji_{kv.Key}" };
                mesh.SetVertices(a.v.Select(p => p - o).ToList()); mesh.SetNormals(a.n); mesh.SetUVs(0, a.uv);
                var mats = new List<Material>(); var all = new[] { iron, grate, cab, plinth }; int sm = 0;
                mesh.subMeshCount = a.t.Count(l => l.Count > 0);
                for (int k = 0; k < 4; k++) if (a.t[k].Count > 0) { mesh.SetTriangles(a.t[k], sm++); mats.Add(all[k]); }
                mesh.RecalculateBounds();
                string p = $"{dir}/{mesh.name}.asset"; if (File.Exists(p)) AssetDatabase.DeleteAsset(p); AssetDatabase.CreateAsset(mesh, p);
                var m = new GameObject(mesh.name); m.isStatic = true; m.transform.SetParent(go.transform, false); m.transform.position = o;
                m.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = m.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats.ToArray(); mr.shadowCastingMode = ShadowCastingMode.On;
            }
            return $"details: {NManhole} manholes, {NGrate} grates, {NCabinet} cabinets";
        }
    }
}
