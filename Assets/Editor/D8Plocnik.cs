// D8 pločnik (28. 11., Marko: "D8 kod Lukšića ima rubni kamen; od Kambelovačkog mosta do Lukšić križanja ima trotoar uzdignut s
// dvije strane"): the D8 between the Kambelovac overpass (krizanje-51) and the Lukšić junction (krizanje-01) has a raised concrete
// sidewalk behind a concrete kerb on BOTH outer edges, a grassy bank behind it; on the north side it carries on west past the
// junction (by the showroom). Built here where the road models do not already have one:
//   where   walked along the two D8 traffic corridors (TrafficData; right of travel = the outer edge), one section per metre: the
//           GLB asphalt's outer boundary past the outermost lane (binary search on LaneProfile.OnAsphalt, followed out to 6.5 m:
//           right-turn lanes and bus bays keep the sidewalk behind them); a section is built when its 1.9 m footprint is free —
//           not asphalt, not a model sidewalk / island (the d02–d06 tiles have their own in places: those stay), not a graph street
//           (StreetsBuilder cover mask: side streets and driveways mapped in OSM). So the sidewalk stops at junction mouths and
//           slip roads by itself, with a lowered kerb ramp (2 m) at each open end; next to a model sidewalk it just butts on.
//   lowered where a mapped street / driveway (StreetsBuilder cover) meets the back of the sidewalk, and
//   gaps    the Mapillary inventory (BalkanDriver tools/world/mapillary/d8rub.json, Codex 28. 11.: Mapillary contributors CC BY-SA
//           4.0, OSM contributors ODbL): driveways / curb cuts get a lowered kerb (4.5 m, 3 cm upstand)
//   shape   kerb 0.16 m wide, 0.15 m upstand over the road edge (raycast on the road models' colliders), 2 cm chamfer; concrete
//           slab; a narrow outer edge kerb and a face down to the ground behind (the bank's foot)
//   details from the same inventory: cabinets (grey, behind the sidewalk), manhole covers on the slab, gully grates on the asphalt
//           at the kerb; where the inventory is thin, a cover every ~70 m and a grate every ~45 m (seeded by position)
// World/Roads/D8Plocnik: ~150 m chunks with MeshColliders (cars mount it like any kerb; lanes, LaneProfile, traffic and the
// autopilot are unchanged — the kerb starts at the asphalt's edge outside the lanes). Covered() keeps every plant off it
// (VegetationClean, D8Rub) and the terrain grass. Off: D8Plocnik.On = false.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static partial class D8Plocnik
    {
        public static bool On = true;
        public const string NAME = "D8Plocnik";
        public const string INV = W.BD + "/tools/world/mapillary/d8rub.json";
        public const float H = .15f, KW = .16f, WID = 1.75f, OKW = .1f, CHUNK = 150;
        /// game x range per corridor (its outer side): south side Kambelovac → Lukšić, north side on past the junction to the west
        static (string cor, float x0, float x1)[] Ranges = { ("D8 istok", -6045, -4300), ("D8 zapad", -6175, -4300) };

        static Magistrala.Traffic.TrafficData TD() => AssetDatabase.FindAssets("t:TrafficData").Select(g => AssetDatabase.LoadAssetAtPath<Magistrala.Traffic.TrafficData>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(d => d && d.corridors != null);

        public class Run { public string cor; public bool south; public List<Vector3> p = new List<Vector3>(); public List<Vector3> n = new List<Vector3>(); public List<float> low = new List<float>(); public bool rampA, rampB; }
        static List<Run> runs;
        static Dictionary<long, List<(Vector2 a, Vector2 b, Vector2 n)>> grid;

        // ------------------------------------------------------------------ inventory
        public class Pt { public string kind, side; public double x, z; public float conf; }
        static List<Pt> inv, gaps;
        static void LoadInv()
        {
            inv = new List<Pt>(); gaps = new List<Pt>();
            if (!File.Exists(INV)) { Debug.LogWarning("[d8plocnik] no inventory " + INV); return; }
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(INV)));
            foreach (var o in MiniJson.L(root["points"]))
            {
                var p = MiniJson.O(o);
                inv.Add(new Pt { kind = (string)p["kind"], side = MiniJson.G(p, "side") as string ?? "", x = MiniJson.D(p["x"]), z = MiniJson.D(p["z"]), conf = MiniJson.G(p, "confidence") is object c ? MiniJson.F(c) : 0 });
            }
            // the showroom on the Lukšić north-west corner (D8Luksic.Salon, the owner's blocks 01:bld:46–48 at x −6067 … −6051): the
            // lowered kerb into its car park, and the grey cabinet on the grass strip west of it (Marko's photo of that corner)
            gaps.Add(new Pt { kind = "driveway", side = "north", x = -6059, z = -5606, conf = .5f });
            inv.Add(new Pt { kind = "cabinet", side = "north", x = -6080, z = -5608, conf = .5f });
            foreach (var o in MiniJson.L(root["gaps"]))
            {
                var p = MiniJson.O(o);
                gaps.Add(new Pt { kind = (string)p["kind"], side = MiniJson.G(p, "side") as string ?? "", x = MiniJson.D(p["x"]), z = MiniJson.D(p["z"]), conf = MiniJson.G(p, "confidence") is object c ? MiniJson.F(c) : 0 });
            }
        }

        // ------------------------------------------------------------------ layout (2D, no physics)
        public static List<Run> Layout(List<ModelJ> models)
        {
            if (runs != null) return runs;
            runs = new List<Run>(); grid = new Dictionary<long, List<(Vector2, Vector2, Vector2)>>();
            if (!On) return runs;
            var td = TD(); if (!td) { Debug.LogWarning("[d8plocnik] no TrafficData"); return runs; }
            if (inv == null) LoadInv();
            bool Asph(Vector2 g) => LaneProfile.OnAsphalt(models, g.x, g.y);
            bool ModelPaved(Vector2 g)
            {
                foreach (var m in models)
                    if (Math.Abs(g.x - m.x) < m.half + 25 && Math.Abs(g.y - m.z) < m.half + 25 && (LaneProfile.Hit(m, "sidewalk", g.x, g.y) || LaneProfile.Hit(m, "island", g.x, g.y))) return true;
                return false;
            }
            foreach (var (cn, x0, x1) in Ranges)
            {
                var c = td.corridors.FirstOrDefault(q => q.name.StartsWith(cn)); if (c == null) continue;
                bool south = cn.Contains("istok");
                // samples every ~1 m: (point on the asphalt edge, outward normal, ok, why-not: 0 ok, 1 too wide / street, 2 model paving)
                var S = new List<(Vector3 a, Vector3 r, float b, int why)>();
                for (int i = 0; i + 1 < c.axis.Length; i++)
                    for (int h = 0; h < 2; h++)
                    {
                        float t = h * .5f;
                        var ax = Vector3.Lerp(c.axis[i], c.axis[i + 1], t); var gx = W.G(ax).x; if (gx < x0 || gx > x1) continue;
                        var r = Vector3.Lerp(c.right[i], c.right[i + 1], t); r.y = 0; r.Normalize();
                        float hi = float.NegativeInfinity;
                        for (int k = 0; k < c.maxLanes; k++) foreach (int j in new[] { i, i + 1 }) { float o = c.laneOff[j * c.maxLanes + k]; if (!float.IsNaN(o)) hi = Mathf.Max(hi, o); }
                        if (float.IsNegativeInfinity(hi)) { S.Add((ax, r, 0, 1)); continue; }
                        float e = hi + 1.75f, b = float.NaN;
                        for (float d = e - .6f; d < e + 6.5f; d += .1f)
                            if (!Asph(W.G(ax + r * d)) && !Asph(W.G(ax + r * (d + .3f)))) { float lo = d - .1f, up = d; for (int q = 0; q < 7; q++) { float m = (lo + up) / 2; if (Asph(W.G(ax + r * m))) lo = m; else up = m; } b = up; break; }
                        if (float.IsNaN(b)) { S.Add((ax, r, 0, 1)); continue; }
                        int why = 0;
                        foreach (float o in new[] { .2f, .8f, 1.4f, WID + .15f })
                        {
                            var g = W.G(ax + r * (b + o));
                            if (Asph(g) || StreetsBuilder.CoveredStreet(g.x, g.y, 0)) { why = 1; break; }
                            if (ModelPaved(g)) { why = 2; break; }
                        }
                        // a mapped street / driveway that stops just behind the sidewalk: a lowered kerb there (why 3, still built)
                        if (why == 0) foreach (float o in new[] { WID + 1.0f, WID + 2.5f }) { var g = W.G(ax + r * (b + o)); if (StreetsBuilder.CoveredStreet(g.x, g.y, 0)) { why = 3; break; } }
                        S.Add((ax, r, b, why));
                    }
                // runs of free sections
                int s0 = 0;
                while (s0 < S.Count)
                {
                    if (S[s0].why != 0 && S[s0].why != 3) { s0++; continue; }
                    int s1 = s0; while (s1 + 1 < S.Count && (S[s1 + 1].why == 0 || S[s1 + 1].why == 3)) s1++;
                    if (s1 - s0 >= 6)
                    {
                        var bs = S.Skip(s0).Take(s1 - s0 + 1).Select(q => (double)q.b).ToArray();
                        var sm = MedianKerbsMean(MedianKerbsMed(bs));
                        var run = new Run { cor = c.name, south = south, rampA = s0 == 0 || S[s0 - 1].why != 2, rampB = s1 == S.Count - 1 || S[s1 + 1].why != 2 };
                        // (at the stretch ends the sidewalk carries on as the model's or ends at a junction: a ramp either way)
                        for (int k = 0; k <= s1 - s0; k++)
                        {
                            var q = S[s0 + k]; float b = Mathf.Max((float)sm[k], q.b);   // never over the asphalt
                            run.p.Add(q.a + q.r * (b - .01f)); run.n.Add(q.r); run.low.Add(q.why == 3 ? 1 : 0);
                        }
                        runs.Add(run);
                    }
                    s0 = s1 + 1;
                }
            }
            // lowered kerbs: the inventory's driveways / curb cuts (4.5 m), the ramps at open ends (2 m)
            foreach (var g in gaps.Where(q => q.kind == "driveway" || q.kind == "curb_cut"))
                foreach (var r in runs)
                {
                    if (r.south != (g.side == "south")) continue;
                    int bi = -1; double bd = 12;
                    for (int i = 0; i < r.p.Count; i++) { var q = W.G(r.p[i]); double d = Math.Sqrt((q.x - g.x) * (q.x - g.x) + (q.y - g.z) * (q.y - g.z)); if (d < bd) { bd = d; bi = i; } }
                    if (bi < 0) continue;
                    for (int i = Math.Max(0, bi - 4); i <= Math.Min(r.p.Count - 1, bi + 4); i++) r.low[i] = Mathf.Max(r.low[i], Mathf.Abs(i - bi) <= 2 ? 1 : .5f);
                }
            foreach (var r in runs)
            {
                var src = r.low.ToArray();
                for (int i = 0; i < src.Length; i++) if (src[i] >= 1) for (int d = -3; d <= 3; d++) { int j = i + d; if (j >= 0 && j < src.Length) r.low[j] = Mathf.Max(r.low[j], Math.Abs(d) <= 1 ? 1 : .5f); }
            }
            foreach (var r in runs)
                for (int k = 0; k < 3 && k < r.p.Count; k++)
                {
                    float f = k == 0 ? 1 : k == 1 ? .66f : .33f;
                    if (r.rampA) r.low[k] = Mathf.Max(r.low[k], f);
                    if (r.rampB) r.low[r.p.Count - 1 - k] = Mathf.Max(r.low[r.p.Count - 1 - k], f);
                }
            // the 2D index for Covered
            foreach (var r in runs)
                for (int i = 0; i + 1 < r.p.Count; i++)
                {
                    var a = W.G(r.p[i]); var b = W.G(r.p[i + 1]); var n = W.G(r.p[i] + r.n[i]) - a;
                    var mid = (a + b) / 2; long k = Key(mid.x, mid.y);
                    if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<(Vector2, Vector2, Vector2)>();
                    l.Add((a, b, n));
                }
            return runs;
        }
        static long Key(double x, double z) => ((long)Math.Floor(x / 8) << 32) ^ (uint)(int)Math.Floor(z / 8);
        public static void Reset() { runs = null; grid = null; inv = null; gaps = null; }

        /// game (x, z) on the sidewalk (kerb front to its outer edge) or within pad m of it
        public static bool Covered(double x, double z, double pad = 0)
        {
            if (!On) return false;
            if (grid == null) Layout(WorldData.Models());
            long cx = (long)Math.Floor(x / 8), cz = (long)Math.Floor(z / 8);
            var p = new Vector2((float)x, (float)z);
            for (long a = -1; a <= 1; a++) for (long b = -1; b <= 1; b++)
                {
                    if (!grid.TryGetValue(((cx + a) << 32) ^ (uint)(int)(cz + b), out var l)) continue;
                    foreach (var (A, B, n) in l)
                    {
                        var t = B - A; float L = t.magnitude; if (L < 1e-3f) continue; t /= L;
                        float u = Vector2.Dot(p - A, t), v = Vector2.Dot(p - A, n);
                        if (u >= -pad - .3f && u <= L + pad + .3f && v >= -pad && v <= WID + pad) return true;
                    }
                }
            return false;
        }

        static double[] MedianKerbsMed(double[] a)
        {
            var r = (double[])a.Clone();
            for (int i = 1; i + 1 < a.Length; i++) { var w = new[] { a[i - 1], a[i], a[i + 1] }; Array.Sort(w); r[i] = w[1]; }
            return r;
        }
        static double[] MedianKerbsMean(double[] a)
        {
            var r = new double[a.Length];
            for (int i = 0; i < a.Length; i++) { double s = 0; int n = 0; for (int k = -2; k <= 2; k++) if (i + k >= 0 && i + k < a.Length) { s += a[i + k]; n++; } r[i] = s / n; }
            return r;
        }

        // ------------------------------------------------------------------ meshes
        [MenuItem("Magistrala/D8: pločnik Kambelovac – Lukšić")]
        public static string Menu()
        {
            var roads = GameObject.Find("World")?.transform.Find("Roads"); if (!roads) return "no World/Roads";
            // the road models' own kerb / paving materials (WorldBuilder.RoadMats [3] / [5]), as the median uses them
            var med = roads.Find(MedianKerbs.NAME)?.GetComponentInChildren<MeshRenderer>();
            var kerb = med ? med.sharedMaterials[0] : null;
            Material pave = null;
            foreach (var mr in roads.GetComponentsInChildren<MeshRenderer>()) { pave = mr.sharedMaterials.FirstOrDefault(m => m && m.name == "Paving"); if (pave) break; }
            if (!kerb || !pave) return "no kerb / paving material (Build roads first)";
            var s = Build(WorldData.Models(), roads, kerb, pave); Debug.Log(s); return s;
        }

        public static string Build(List<ModelJ> models, Transform roads, Material kerb, Material pave)
        {
            Reset();
            var old = roads.Find(NAME); if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
            if (!On) return "[d8plocnik] off";
            var rs = Layout(models);
            var root = new GameObject(NAME); root.isStatic = true; root.transform.SetParent(roads, false);
            string dir = WorldBuilder.GEN + "/Roads/D8Plocnik"; if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(WorldBuilder.GEN + "/Roads", "D8Plocnik");
            Physics.SyncTransforms();
            float Road(Vector3 u, float hint)
            {
                float best = float.NaN;
                foreach (var h in Physics.RaycastAll(new Vector3(u.x, hint + 2.5f, u.z), Vector3.down, 6, ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider.transform.IsChildOf(roads) && !MedianKerbs.IsMedian(h.collider) && !h.collider.transform.IsChildOf(root.transform) && h.normal.y > .9f && (float.IsNaN(best) || Math.Abs(h.point.y - hint) < Math.Abs(best - hint))) best = h.point.y;
                return best;
            }
            float Ground(Vector3 u, float hint)
            {
                float best = float.NaN;
                foreach (var h in Physics.RaycastAll(new Vector3(u.x, hint + 4f, u.z), Vector3.down, 9, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (h.collider.transform.IsChildOf(root.transform)) continue;
                    var b = h.collider.bounds.size; if (!(h.collider is TerrainCollider) && !(h.collider.transform.IsChildOf(roads)) && b.x < 60 && b.z < 60) continue;   // props
                    if (float.IsNaN(best) || Math.Abs(h.point.y - hint) < Math.Abs(best - hint)) best = h.point.y;
                }
                return best;
            }
            var chunks = new Dictionary<int, (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> k, List<int> s)>();
            (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> k, List<int> s) G(Vector3 at)
            {
                int c = (int)Math.Floor(W.G(at).x / CHUNK);
                if (!chunks.TryGetValue(c, out var g)) chunks[c] = g = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>(), new List<int>());
                return g;
            }
            void Quad((List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> k, List<int> s) g, bool isKerb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 o)
            {
                var nn = Vector3.Cross(b - a, c - a); if (nn.sqrMagnitude < 1e-10f) nn = Vector3.Cross(c - a, d - a); if (nn.sqrMagnitude < 1e-12f) return; nn.Normalize();
                if (Vector3.Dot(nn, o) < 0) { (b, d) = (d, b); nn = -nn; }
                int i0 = g.v.Count; g.v.Add(a); g.v.Add(b); g.v.Add(c); g.v.Add(d);
                foreach (var p in new[] { a, b, c, d })
                {
                    g.n.Add(nn);
                    if (Mathf.Abs(nn.y) > .6f) g.uv.Add(new Vector2(p.x, p.z) * (isKerb ? 1 : .5f));
                    else { var t = new Vector3(-nn.z, 0, nn.x).normalized; g.uv.Add(new Vector2(Vector3.Dot(p, t), p.y)); }
                }
                var l = isKerb ? g.k : g.s; l.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }
            double metres = 0; int nSec = 0, nLow = 0;
            var secs = new List<(Run r, List<Vector3[]> s, List<float> top)>();
            foreach (var r in rs)
            {
                var sec = new List<Vector3[]>(); var tops = new List<float>(); float last = float.NaN, lastG = float.NaN;
                for (int i = 0; i < r.p.Count; i++)
                {
                    var a = r.p[i]; var n = r.n[i]; var gg = W.G(a);
                    float hint = (float)(WorldData.ModelTerrain(models, gg.x, gg.y, 2, Tiles.Height(gg.x, gg.y)) ?? Tiles.Height(gg.x, gg.y));
                    if (!float.IsNaN(last)) hint = last;
                    float y = Road(a - n * .25f, hint); if (float.IsNaN(y)) y = float.IsNaN(last) ? hint : last; last = y;
                    float gr = Ground(a + n * (WID + .35f), y); if (float.IsNaN(gr)) gr = float.IsNaN(lastG) ? y : lastG; lastG = gr;
                    float up = Mathf.Lerp(H, .03f, r.low[i]); if (r.low[i] > .9f) nLow++;
                    float top = y + up, foot = Mathf.Min(gr, top) - .12f;
                    Vector3 P(float o, float yy) { var q = a + n * o; q.y = yy; return q; }
                    // 0 front foot, 1 front top (chamfer start), 2 chamfer end, 3 kerb back, 4 slab outer end, 5 outer kerb top, 6 outer foot
                    sec.Add(new[] { P(0, y - .05f), P(0, top - .02f), P(.02f, top), P(KW, top), P(WID - OKW, top + .008f), P(WID, top + .008f), P(WID, foot) });
                    tops.Add(top); nSec++;
                }
                secs.Add((r, sec, tops));
                for (int i = 0; i + 1 < sec.Count; i++)
                {
                    var g = G(sec[i][0]); Vector3[] A = sec[i], B = sec[i + 1];
                    var lat = r.n[i]; var up = Vector3.up;
                    metres += Vector3.Distance(A[0], B[0]);
                    Quad(g, true, A[0], A[1], B[1], B[0], -lat);
                    Quad(g, true, A[1], A[2], B[2], B[1], -lat + up);
                    Quad(g, true, A[2], A[3], B[3], B[2], up);
                    Quad(g, false, A[3], A[4], B[4], B[3], up);
                    Quad(g, true, A[4], A[5], B[5], B[4], up);
                    Quad(g, true, A[5], A[6], B[6], B[5], lat);
                }
                // end caps
                foreach (var (i, o) in new[] { (0, -1), (sec.Count - 1, 1) })
                {
                    var A = sec[i]; var g = G(A[0]);
                    var t = (o < 0 ? r.p[Math.Min(1, r.p.Count - 1)] - r.p[0] : r.p[r.p.Count - 1] - r.p[Math.Max(0, r.p.Count - 2)]); t.y = 0; t.Normalize(); t *= o;
                    float yb = Mathf.Min(A[0].y, A[6].y);
                    for (int k = 0; k + 1 < A.Length - 1; k++)
                    {
                        Vector3 a = A[k], b = A[k + 1];
                        Quad(g, true, a, b, new Vector3(b.x, yb, b.z), new Vector3(a.x, yb, a.z), t);
                    }
                }
            }
            int tris = 0;
            foreach (var kv in chunks.OrderBy(k => k.Key))
            {
                var g = kv.Value; if (g.v.Count == 0) continue;
                var o = g.v[0]; o.y = 0; var lv = g.v.Select(p => p - o).ToList();
                var mesh = new Mesh { name = $"d8plocnik_{kv.Key}", indexFormat = lv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(lv); mesh.SetNormals(g.n); mesh.SetUVs(0, g.uv); mesh.subMeshCount = 2;
                mesh.SetTriangles(g.k, 0); mesh.SetTriangles(g.s, 1); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                string p = $"{dir}/{mesh.name}.asset"; if (File.Exists(p)) AssetDatabase.DeleteAsset(p); AssetDatabase.CreateAsset(mesh, p);
                var go = new GameObject(mesh.name); go.isStatic = true; go.transform.SetParent(root.transform, false); go.transform.position = o;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { kerb, pave }; mr.shadowCastingMode = ShadowCastingMode.On;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                tris += (g.k.Count + g.s.Count) / 3;
            }
            string det = Details(root.transform, secs, dir);
            AssetDatabase.SaveAssets();
            return $"[d8plocnik] {rs.Count} runs, {metres:F0} m of raised sidewalk ({nSec} sections, {nLow} lowered), {chunks.Count} chunks, {tris} tris; {det}";
        }
    }
}
