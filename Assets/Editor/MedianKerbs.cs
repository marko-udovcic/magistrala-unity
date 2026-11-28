// Razdjelni pojas D8 (28. 11., Marko: "cijelom dužinom D8 ona ne leži na cesti ovaj srednji dio nego je odvojen kao kornizom
// kamenom rubnim sa obje strane"): the D8's central median through Kaštela is not flush with the asphalt — it is a strip raised
// ~18 cm behind a concrete kerb on both edges, soil / dry grass on top, the oleanders planted in it.
// The median is not in the road GLBs (it is the terrain between the carriageways), so it is found as MedianHedge finds it
// (MedianLine: a strip off the asphalt with a carriageway north and south, one column per metre along x), each edge refined to
// the GLB asphalt's boundary (binary search on LaneProfile.OnAsphalt), lightly smoothed, and built as kerbs + a soil top
// following the asphalt edge heights (raycast on the road models' colliders). Junction openings / median crossings are asphalt,
// so the strip stops there and gets end faces.
// World/Roads/Razdjelni: meshes in ~150 m chunks with their MeshColliders — the plants (OleanderRows, VegetationClean, Surface
// prefers the median's top) and the median lamps (PropsBuilder: first hit from above) stand on it. The kerbs sit on the
// asphalt's edge outside the lanes (the lane geometry, LaneProfile, traffic and the autopilot path are unchanged).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class MedianKerbs
    {
        public static bool On = true;
        public const string NAME = "Razdjelni";
        const float H = .18f, KW = .18f, FILL = .13f, CHUNK = 150;

        public static bool IsMedian(Collider c) => c && c.transform.parent && c.transform.parent.name == NAME;

        public static string Build(List<ModelJ> models, Transform roads, Material kerb)
        {
            if (!On) return "[razdjelni] off";
            var cols = MedianHedge.MedianLine(models, 1.0).OrderBy(c => c.x).ToList();
            if (cols.Count == 0) return "[razdjelni] no median columns";
            bool Asph(double x, double z) => LaneProfile.OnAsphalt(models, x, z);
            // the edge between the median at zIn and the asphalt toward zOut (game z), to ~1 cm
            double Edge(double x, double zIn, double zOut)
            {
                double a = zIn, b = zOut;
                if (Asph(x, a)) return double.NaN;
                if (!Asph(x, b)) { b = zOut + Math.Sign(zOut - zIn) * .5; if (!Asph(x, b)) return double.NaN; }
                for (int k = 0; k < 9; k++) { double mid = (a + b) / 2; if (Asph(x, mid)) b = mid; else a = mid; }
                return (a + b) / 2;
            }
            // runs: consecutive metres with the centre line continuing
            var runs = new List<List<(double x, double lo, double hi)>>();
            var open = new List<List<(double x, double lo, double hi)>>();
            foreach (var (x, zc, half) in cols)
            {
                double lo = Edge(x, zc, zc - half - .3), hi = Edge(x, zc, zc + half + .3);
                if (double.IsNaN(lo) || double.IsNaN(hi) || hi - lo < .9) continue;
                var r = open.FirstOrDefault(q => x - q[q.Count - 1].x < 1.6 && Math.Abs((q[q.Count - 1].lo + q[q.Count - 1].hi) / 2 - (lo + hi) / 2) < 1);
                if (r == null) { r = new List<(double, double, double)>(); runs.Add(r); open.Add(r); }
                r.Add((x, lo, hi));
                open.RemoveAll(q => x - q[q.Count - 1].x > 1.6);
            }
            runs.RemoveAll(r => r.Count < 4);
            // smoothing: a 3-median then a 5-mean of each edge (the asphalt's boundary steps on the GLB triangles)
            foreach (var r in runs)
            {
                var lo = r.Select(c => c.lo).ToArray(); var hi = r.Select(c => c.hi).ToArray();
                lo = Mean(Med(lo)); hi = Mean(Med(hi));
                for (int i = 0; i < r.Count; i++) r[i] = (r[i].x, lo[i], hi[i]);
                // a section every 2 m (the edges are smooth by now; half the triangles)
                var thin = r.Where((c, i) => i % 2 == 0 || i == r.Count - 1).ToList(); r.Clear(); r.AddRange(thin);
            }
            // road heights at the edges (on the road models' colliders, just outside the median)
            Physics.SyncTransforms();
            float Road(double x, double z, double hint)
            {
                var u = W.U(x, 0, z); float best = float.NaN;
                // (from just above the ground: not onto an overpass deck above the median)
                foreach (var h in Physics.RaycastAll(new Vector3(u.x, (float)hint + 2.5f, u.z), Vector3.down, 6, ~0, QueryTriggerInteraction.Ignore))
                    if (h.collider.transform.IsChildOf(roads) && !IsMedian(h.collider) && h.normal.y > .9f && (float.IsNaN(best) || Math.Abs(h.point.y - hint) < Math.Abs(best - hint))) best = h.point.y;
                return best;
            }
            double Ter(double x, double z) { double h = Tiles.Height(x, z); return WorldData.ModelTerrain(models, x, z, 2, h) ?? h; }

            var old = roads.Find(NAME); if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var root = new GameObject(NAME); root.isStatic = true; root.transform.SetParent(roads, false);
            string dir = WorldBuilder.GEN + "/Roads/Razdjelni"; if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(WorldBuilder.GEN + "/Roads", "Razdjelni");
            var soil = WorldBuilder.Lit("RazdjelniTlo", new Color(.86f, .85f, .8f), .04f, 0, "dry_grass_d.jpg");
            soil.SetTextureScale("_BaseMap", new Vector2(.33f, .33f)); EditorUtility.SetDirty(soil);

            var chunks = new Dictionary<int, (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> k, List<int> s)>();
            (List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> k, List<int> s) G(double x)
            {
                int c = (int)Math.Floor(x / CHUNK);
                if (!chunks.TryGetValue(c, out var g)) chunks[c] = g = (new List<Vector3>(), new List<Vector3>(), new List<Vector2>(), new List<int>(), new List<int>());
                return g;
            }
            void Quad((List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> k, List<int> s) g, bool isKerb, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 o)
            {
                var nn = Vector3.Cross(b - a, c - a); if (nn.sqrMagnitude < 1e-10f) nn = Vector3.Cross(c - a, d - a); nn.Normalize();
                if (Vector3.Dot(nn, o) < 0) { (b, d) = (d, b); nn = -nn; }
                int i0 = g.v.Count; g.v.Add(a); g.v.Add(b); g.v.Add(c); g.v.Add(d);
                foreach (var p in new[] { a, b, c, d })
                {
                    g.n.Add(nn);
                    if (Mathf.Abs(nn.y) > .6f) g.uv.Add(new Vector2(p.x, p.z));
                    else { var t = new Vector3(-nn.z, 0, nn.x).normalized; g.uv.Add(new Vector2(Vector3.Dot(p, t), p.y)); }
                }
                var l = isKerb ? g.k : g.s; l.AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }
            int nCols = 0; double metres = 0;
            foreach (var r in runs)
            {
                // the section at each column (unity coords): lateral points from the lo edge to the hi edge
                var sec = new List<Vector3[]>();
                float lastL = float.NaN, lastR = float.NaN;
                foreach (var (x, lo, hi) in r)
                {
                    double t = Ter(x, (lo + hi) / 2);
                    float yl = Road(x, lo - .25, t), yr = Road(x, hi + .25, t);
                    if (float.IsNaN(yl)) yl = float.IsNaN(lastL) ? (float)t : lastL; if (float.IsNaN(yr)) yr = float.IsNaN(lastR) ? (float)t : lastR;
                    lastL = yl; lastR = yr;
                    double zc = (lo + hi) / 2; float yc = Mathf.Max((yl + yr) / 2 + FILL + .03f, (float)t + .04f);
                    Vector3 P(double z, float y) => W.U(x, y, z);
                    // (game z grows north = unity −z): 0 lo outer foot, 1 lo outer top, 2 lo chamfer, 3 lo inner top, 4 lo inner foot (fill),
                    // 5 centre fill, 6 hi inner foot, 7 hi inner top, 8 hi chamfer, 9 hi outer top, 10 hi outer foot
                    sec.Add(new[]
                    {
                        P(lo, yl - .04f), P(lo, yl + H - .025f), P(lo + .025, yl + H), P(lo + KW, yl + H), P(lo + KW, yl + FILL),
                        P(zc, yc),
                        P(hi - KW, yr + FILL), P(hi - KW, yr + H), P(hi - .025, yr + H), P(hi, yr + H - .025f), P(hi, yr - .04f),
                    });
                    nCols++;
                }
                for (int i = 0; i + 1 < sec.Count; i++)
                {
                    var g = G(r[i].x); Vector3[] A = sec[i], B = sec[i + 1];
                    var up = Vector3.up; var lat = (A[10] - A[0]); lat.y = 0; lat.Normalize();   // lo → hi
                    metres += Vector3.Distance(A[5], B[5]);
                    Quad(g, true, A[0], A[1], B[1], B[0], -lat);       // lo kerb face (toward the lo carriageway)
                    Quad(g, true, A[1], A[2], B[2], B[1], -lat + up);  // chamfer
                    Quad(g, true, A[2], A[3], B[3], B[2], up);         // kerb top
                    Quad(g, true, A[3], A[4], B[4], B[3], lat);        // kerb back above the soil
                    Quad(g, false, A[4], A[5], B[5], B[4], up);        // soil
                    Quad(g, false, A[5], A[6], B[6], B[5], up);
                    Quad(g, true, A[6], A[7], B[7], B[6], -lat);
                    Quad(g, true, A[7], A[8], B[8], B[7], up);
                    Quad(g, true, A[8], A[9], B[9], B[8], lat + up);
                    Quad(g, true, A[9], A[10], B[10], B[9], lat);
                }
                // end faces: the kerb wraps the nose (a concrete end), down to the road
                foreach (var (i, o) in new[] { (0, -1), (sec.Count - 1, 1) })
                {
                    var A = sec[i]; var g = G(r[i].x); var nd = new Vector3(o, 0, 0);
                    float yb = Mathf.Min(A[0].y, A[10].y);
                    for (int k = 0; k + 1 < A.Length; k++)
                    {
                        Vector3 a = A[k], b = A[k + 1]; if (Mathf.Abs(a.z - b.z) < 1e-4f) continue;
                        Quad(g, true, a, b, new Vector3(b.x, yb, b.z), new Vector3(a.x, yb, a.z), nd);
                    }
                }
            }
            int tris = 0;
            foreach (var kv in chunks.OrderBy(k => k.Key))
            {
                var g = kv.Value; if (g.v.Count == 0) continue;
                // local to the chunk's first vertex (precision, culling bounds)
                var o = g.v[0]; o.y = 0; var lv = g.v.Select(p => p - o).ToList();
                var mesh = new Mesh { name = $"razdjelni_{kv.Key}", indexFormat = lv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(lv); mesh.SetNormals(g.n); mesh.SetUVs(0, g.uv); mesh.subMeshCount = 2;
                mesh.SetTriangles(g.k, 0); mesh.SetTriangles(g.s, 1); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                string p = $"{dir}/{mesh.name}.asset"; if (File.Exists(p)) AssetDatabase.DeleteAsset(p); AssetDatabase.CreateAsset(mesh, p);
                var go = new GameObject(mesh.name); go.isStatic = true; go.transform.SetParent(root.transform, false); go.transform.position = o;
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { kerb, soil }; mr.shadowCastingMode = ShadowCastingMode.On;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                tris += (g.k.Count + g.s.Count) / 3;
            }
            return $"[razdjelni] {runs.Count} runs, {metres:F0} m of raised median ({nCols} columns), {chunks.Count} chunks, {tris} tris";
        }

        static double[] Med(double[] a)
        {
            var r = (double[])a.Clone();
            for (int i = 1; i + 1 < a.Length; i++) { var w = new[] { a[i - 1], a[i], a[i + 1] }; Array.Sort(w); r[i] = w[1]; }
            return r;
        }
        static double[] Mean(double[] a)
        {
            var r = new double[a.Length];
            for (int i = 0; i < a.Length; i++) { double s = 0; int n = 0; for (int k = -2; k <= 2; k++) if (i + k >= 0 && i + k < a.Length) { s += a[i + k]; n++; } r[i] = s / n; }
            return r;
        }
    }
}
