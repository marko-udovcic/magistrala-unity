// D8 Lukšić – Kambelovac, the things around the roads (28. 11., Marko's photos of the Lukšić junction, visual reference only):
//   otoci   the kerbed traffic islands of the road models on the stretch (GLB `island` of krizanje-01 and d01–d06, up to 250 m²):
//           a trimmed box hedge (0.75 m) over the island's inside (0.5 m in from its kerb), white gravel at its nose — around the
//           owner's keep-right bollard (junction-overrides-modeli.json) or, without one, at both ends of the island — and nothing
//           across a zebra (krizanja.json / dionice.json `zebra`, ±1.2 m) or at a signal pole
//   salon   the showroom on the north-west corner of the Lukšić junction: the owner's three flat-roofed blocks (model-buildings.json
//           01:bld:46–48, "KERAMIKA") keep their footprints; on their faces toward the D8 and the north arm a glazed ground floor
//           (dark glass, mullions) and a dark window band above; in front a small paved car park with stalls and white planters
//           (its lowered kerb is in D8Plocnik); along the D8 west of it the dark horizontal-slat fence on a grey concrete plinth
//           wall behind a dry-grass strip; a dark sign pylon with an invented name (KERAPOLIS — no real brand)
// Run by Build signals at its end (after every placer that the hedges / fence have to respect) or alone: Magistrala/D8: Lukšić
// otoci i salon. World/D8Luksic.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class D8Luksic
    {
        public static bool On = true;
        const string NAME = "D8Luksic";
        static readonly string[] IslandModels = { "krizanje-01", "dionica-d01", "dionica-d02", "dionica-d03", "dionica-d04", "dionica-d05", "dionica-d06" };
        static readonly string[] SalonIds = { "01:bld:46", "01:bld:47", "01:bld:48" };

        class Acc
        {
            public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<int>[] t;
            public Acc(int subs) { t = new List<int>[subs]; for (int i = 0; i < subs; i++) t[i] = new List<int>(); }
            public void Quad(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 o, float uvScale = 1)
            {
                var nn = Vector3.Cross(b - a, c - a); if (nn.sqrMagnitude < 1e-12f) return; nn.Normalize();
                if (Vector3.Dot(nn, o) < 0) { (b, d) = (d, b); nn = -nn; }
                int i = v.Count; v.Add(a); v.Add(b); v.Add(c); v.Add(d); for (int k = 0; k < 4; k++) n.Add(nn);
                foreach (var p in new[] { a, b, c, d })
                {
                    if (Mathf.Abs(nn.y) > .6f) uv.Add(new Vector2(p.x, p.z) * uvScale);
                    else { var tt = new Vector3(-nn.z, 0, nn.x).normalized; uv.Add(new Vector2(Vector3.Dot(p, tt), p.y) * uvScale); }
                }
                t[s].AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            }
            /// box: centre of its bottom, along f, size (across, height, along)
            public void Box(int s, Vector3 c, Vector3 f, Vector3 sz, float uvScale = 1, bool bottom = false)
            {
                f.y = 0; f.Normalize(); var r = Vector3.Cross(Vector3.up, f);
                Vector3 X = r * sz.x / 2, Y = Vector3.up * sz.y, Z = f * sz.z / 2;
                Vector3 P(int i, int j, int k) => c + X * (i * 2 - 1) + Y * j + Z * (k * 2 - 1);
                Quad(s, P(0, 1, 0), P(1, 1, 0), P(1, 1, 1), P(0, 1, 1), Vector3.up, uvScale);
                Quad(s, P(0, 0, 0), P(0, 1, 0), P(0, 1, 1), P(0, 0, 1), -r, uvScale); Quad(s, P(1, 0, 0), P(1, 1, 0), P(1, 1, 1), P(1, 0, 1), r, uvScale);
                Quad(s, P(0, 0, 0), P(1, 0, 0), P(1, 1, 0), P(0, 1, 0), -f, uvScale); Quad(s, P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1), f, uvScale);
                if (bottom) Quad(s, P(0, 0, 0), P(1, 0, 0), P(1, 0, 1), P(0, 0, 1), -Vector3.up, uvScale);
            }
            public GameObject Emit(string name, Transform parent, Material[] mats, string dir, bool collider = false)
            {
                if (v.Count == 0) return null;
                var o = v[0]; o.y = 0;
                var mesh = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(v.Select(p => p - o).ToList()); mesh.SetNormals(n); mesh.SetUVs(0, uv);
                var used = new List<Material>(); int sm = 0; mesh.subMeshCount = t.Count(l => l.Count > 0);
                for (int k = 0; k < t.Length; k++) if (t[k].Count > 0) { mesh.SetTriangles(t[k], sm++); used.Add(mats[k]); }
                mesh.RecalculateTangents(); mesh.RecalculateBounds();
                string p = $"{dir}/{name}.asset"; if (File.Exists(p)) AssetDatabase.DeleteAsset(p); AssetDatabase.CreateAsset(mesh, p);
                var go = new GameObject(name); go.isStatic = true; go.transform.SetParent(parent, false); go.transform.position = o;
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = used.ToArray(); mr.shadowCastingMode = ShadowCastingMode.On;
                if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
                return go;
            }
        }

        /// the showroom's car park (game x0, x1, z0, z1): no plants / terrain grass on it (VegetationClean, GraphicsPass)
        static (float x0, float x1, float z0, float z1)? park;
        public static bool Covered(double x, double z)
        {
            if (!On) return false;
            if (park == null) park = Park(WorldData.Models(), Blocks());
            var p = park.Value; return x > p.x0 - .3 && x < p.x1 + .3 && z > p.z0 - .3 && z < p.z1 + .3;
        }

        [MenuItem("Magistrala/D8: Lukšić otoci i salon")]
        public static string Menu() { var s = Build(); Debug.Log(s); return s; }

        public static string Build()
        {
            var world = GameObject.Find("World"); if (!world) return "no World";
            var old = world.transform.Find(NAME); if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
            if (!On) return "[d8luksic] off";
            var root = new GameObject(NAME); root.isStatic = true; root.transform.SetParent(world.transform, false);
            string dir = WorldBuilder.GEN + "/Props/D8Luksic"; Directory.CreateDirectory(dir); AssetDatabase.Refresh();
            Physics.SyncTransforms();
            var models = WorldData.Models();
            string a = Islands(models, root.transform, dir), b = Salon(models, root.transform, dir);
            AssetDatabase.SaveAssets();
            return $"[d8luksic] {a}; {b}";
        }

        /// the highest collider surface under game (x, z) near yHint (World/Roads: islands, kerbs), NaN none
        static float Surf(Vector3 u, float hint, Transform skip)
        {
            float best = float.NaN;
            foreach (var h in Physics.RaycastAll(new Vector3(u.x, hint + 3, u.z), Vector3.down, 7, ~0, QueryTriggerInteraction.Ignore))
            {
                if (skip && h.collider.transform.IsChildOf(skip)) continue;
                var b = h.collider.bounds.size; if (!(h.collider is TerrainCollider) && !(h.collider is MeshCollider) && b.x < 40 && b.z < 40) continue;   // posts, cabinets
                if (float.IsNaN(best) || h.point.y > best) best = h.point.y;
            }
            return best;
        }

        // ------------------------------------------------------------------ islands
        static string Islands(List<ModelJ> models, Transform root, string dir)
        {
            var hedgeM = WorldBuilder.Lit("D8ZivicaOtok", new Color(.62f, .8f, .45f), .06f, 0, "grass_maquis_d.jpg");
            var gravelM = WorldBuilder.Lit("D8SljunakBijeli", new Color(1.45f, 1.45f, 1.4f), .15f, 0, "gravel_d.jpg", "gravel_n.jpg", .8f);
            // zebras (game x, z, along-road dir, w, l) and the keep-right bollards / signal poles of the owner's props
            var zebras = new List<(Vector2 c, Vector2 t, float w, float l)>(); var bollards = new List<Vector2>(); var poles = new List<Vector2>();
            var byGlb = models.ToDictionary(m => Path.GetFileNameWithoutExtension(m.glb), m => m);
            foreach (var f in new[] { PaketData.Need(PaketData.KRIZANJA), PaketData.Need(PaketData.DIONICE) })
            {
                if (!File.Exists(f)) continue;
                var r0 = MiniJson.O(MiniJson.Parse(File.ReadAllText(f)));
                foreach (var jo in MiniJson.L(MiniJson.G(r0, "junctions") ?? MiniJson.G(r0, "tiles")))
                {
                    var j = MiniJson.O(jo); if (!(j["file"] is string file) || !byGlb.TryGetValue(file, out var m) || !IslandModels.Contains(m.key)) continue;
                    foreach (var oo in MiniJson.L(MiniJson.G(j, "objs")) ?? new List<object>())
                    {
                        var o = MiniJson.O(oo); if ((string)o["k"] != "zebra") continue;
                        double lx = MiniJson.D(o["x"]), lz = MiniJson.D(o["z"]);
                        if (m.lin != null) { double nx = m.lin[0] * lx + m.lin[1] * lz, nz = m.lin[2] * lx + m.lin[3] * lz; lx = nx; lz = nz; }
                        float ang = MiniJson.F(o["a"]);
                        zebras.Add((new Vector2((float)(m.x + lx), (float)(m.z + lz)), new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)), MiniJson.F(o["w"]), MiniJson.G(o, "l") is object lo ? MiniJson.F(lo) : 4));
                    }
                }
            }
            var ov = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "junction-overrides-modeli.json")));
            foreach (var o in MiniJson.L(ov["overrides"]))
            {
                var oo = MiniJson.O(MiniJson.O(o)["overrides"]);
                foreach (var key in new[] { "nodes", "addNodes" })
                    foreach (var nd in MiniJson.L(MiniJson.G(oo, key)) ?? new List<object>())
                        foreach (var po in MiniJson.L(MiniJson.G(MiniJson.O(nd), "props")) ?? new List<object>())
                        {
                            var p = MiniJson.O(po); var g = new Vector2((float)MiniJson.D(p["x"]), (float)MiniJson.D(p["z"]));
                            string kind = (string)p["kind"];
                            if (kind == "bollard_keep_right") bollards.Add(g); else if (kind.StartsWith("signal") || kind == "gantry") poles.Add(g);
                        }
            }
            bool OnZebra(Vector2 g) => zebras.Any(z =>
            {
                var d = g - z.c; float along = Vector2.Dot(d, z.t), across = Vector2.Dot(d, new Vector2(-z.t.y, z.t.x));
                return Mathf.Abs(along) < z.l / 2 + 1.2f && Mathf.Abs(across) < z.w / 2 + 1.2f;
            });
            var roads = GameObject.Find("World/Roads")?.transform;
            var acc = new Acc(2); int nIsl = 0, nHedge = 0, nGravel = 0; const float C = .5f;
            foreach (var m in models.Where(q => IslandModels.Contains(q.key)))
            {
                int N = (int)Math.Ceiling(2 * (m.half + 10) / C); double x0 = m.x - m.half - 10, z0 = m.z - m.half - 10;
                var isl = new bool[N, N];
                for (int i = 0; i < N; i++) for (int k = 0; k < N; k++) isl[i, k] = LaneProfile.Hit(m, "island", x0 + (i + .5) * C, z0 + (k + .5) * C);
                var seen = new bool[N, N];
                for (int i0 = 0; i0 < N; i0++) for (int k0 = 0; k0 < N; k0++)
                    {
                        if (!isl[i0, k0] || seen[i0, k0]) continue;
                        var cells = new List<(int i, int k)>(); var st = new Stack<(int, int)>(); st.Push((i0, k0)); seen[i0, k0] = true;
                        while (st.Count > 0)
                        {
                            var (i, k) = st.Pop(); cells.Add((i, k));
                            foreach (var (di, dk) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                            { int a = i + di, b = k + dk; if (a >= 0 && b >= 0 && a < N && b < N && isl[a, b] && !seen[a, b]) { seen[a, b] = true; st.Push((a, b)); } }
                        }
                        float area = cells.Count * C * C; if (area < 3 || area > 250) continue;
                        nIsl++;
                        Vector2 G(int i, int k) => new Vector2((float)(x0 + (i + .5) * C), (float)(z0 + (k + .5) * C));
                        var pts = cells.Select(c => G(c.i, c.k)).ToList();
                        var bol = bollards.Where(b => pts.Any(p => (p - b).sqrMagnitude < 1.5f)).ToList();
                        // ends: without a bollard, the two cells farthest apart
                        if (bol.Count == 0)
                        {
                            var p0 = pts.OrderByDescending(p => (p - pts[0]).sqrMagnitude).First(); var p1 = pts.OrderByDescending(p => (p - p0).sqrMagnitude).First();
                            bol.Add(p0); bol.Add(p1);
                        }
                        float rG = bol.Count == 1 ? 2.4f : 1.6f;
                        // hedge cells (0.5 m in from the kerb) → rows along x merged into one clipped block each; gravel cells flat
                        var hedge = new HashSet<(int, int)>(); var ys = new Dictionary<(int, int), float>();
                        foreach (var (i, k) in cells)
                        {
                            var g = G(i, k);
                            bool inner = true; for (int di = -1; di <= 1 && inner; di++) for (int dk = -1; dk <= 1; dk++) { int a = i + di, b = k + dk; if (a < 0 || b < 0 || a >= N || b >= N || !isl[a, b]) { inner = false; break; } }
                            if (!inner) continue;
                            var u = W.U(g.x, 0, g.y); float y = Surf(u, (float)m.y + 1, root); if (float.IsNaN(y)) continue; u.y = y;
                            bool grav = bol.Any(b => (b - g).magnitude < rG);
                            if (OnZebra(g) || poles.Any(p => (p - g).magnitude < .9f) || (!grav && bol.Any(b => (b - g).magnitude < .7f))) continue;
                            if (grav) { acc.Quad(1, u + new Vector3(-C / 2, .025f, -C / 2), u + new Vector3(-C / 2, .025f, C / 2), u + new Vector3(C / 2, .025f, C / 2), u + new Vector3(C / 2, .025f, -C / 2), Vector3.up, .5f); nGravel++; }
                            else if (area <= 90) { hedge.Add((i, k)); ys[(i, k)] = y; nHedge++; }
                        }
                        // (the big paved corner islands keep their paving: a hedge only on the small ones, as in the photos)
                        const float HH = .72f;
                        foreach (var k in hedge.Select(c => c.Item2).Distinct())
                        {
                            var row = hedge.Where(c => c.Item2 == k).Select(c => c.Item1).OrderBy(i => i).ToList();
                            for (int q = 0; q < row.Count;)
                            {
                                int e = q; while (e + 1 < row.Count && row[e + 1] == row[e] + 1) e++;
                                var ga = G(row[q], k); var gb = G(row[e], k);
                                float y = Enumerable.Range(q, e - q + 1).Min(t => ys[(row[t], k)]);
                                var ua = W.U(ga.x - C / 2, y - .05f, ga.y); var ub = W.U(gb.x + C / 2, y - .05f, gb.y);
                                acc.Box(0, (ua + ub) / 2, Vector3.right, new Vector3(C + .01f, HH + .05f, (ub - ua).magnitude + .01f), 1.2f);
                                q = e + 1;
                            }
                        }
                    }
            }
            acc.Emit("d8luksic_otoci", root, new[] { hedgeM, gravelM }, dir);
            return $"islands {nIsl}: hedge {nHedge * C * C:F0} m², white gravel {nGravel * C * C:F0} m²";
        }

        // ------------------------------------------------------------------ the showroom
        class Blk { public double x, z, a, w, l, h; public Vector2 C(int su, int sv) { var u = new Vector2((float)Math.Cos(a), (float)Math.Sin(a)); var v = new Vector2(-u.y, u.x); return new Vector2((float)x, (float)z) + u * (float)(su * w / 2) + v * (float)(sv * l / 2); } }

        static string Salon(List<ModelJ> models, Transform root, string dir)
        {
            var blks = Blocks();
            if (blks.Count == 0) return "salon: blocks not found";
            var glass = WorldBuilder.Lit("SalonStaklo", new Color(.1f, .13f, .16f), .93f, .3f);
            var frame = WorldBuilder.Lit("SalonOkvir", new Color(.16f, .17f, .18f), .4f, .5f);
            var slat = WorldBuilder.Lit("SalonOgradaLamele", new Color(.17f, .18f, .19f), .3f, .2f);
            var plinthM = WorldBuilder.Lit("SalonOgradaZid", new Color(.62f, .62f, .6f), .08f, 0, "PolyHaven/concrete_wall_007_d.jpg");
            var paveM = WorldBuilder.Lit("SalonParkiraliste", new Color(.86f, .85f, .82f), .12f, 0, "concrete_grey_d.jpg", "concrete_n.jpg", .6f);
            var whiteM = WorldBuilder.Lit("SalonBijelo", new Color(.93f, .93f, .9f), .2f, 0);
            var greenM = WorldBuilder.Lit("D8ZivicaOtok", new Color(.62f, .8f, .45f), .06f, 0, "grass_maquis_d.jpg");
            var signM = SignMat();
            var mats = new[] { glass, frame, slat, plinthM, paveM, whiteM, greenM, signM };
            var acc = new Acc(mats.Length); var accCol = new Acc(mats.Length);
            float Gnd(Vector2 g, float hint) { var u = W.U(g.x, 0, g.y); float y = Surf(u, hint, root); return float.IsNaN(y) ? (float)BuildingsFromTiles.Ground(models, g.x, g.y) : y; }
            float baseY = blks.Min(b => (float)BuildingsFromTiles.Ground(models, b.x, b.z));   // (as the blocks stand: BuildingsBuilder's ground)
            int nGlass = 0;
            // glazing: the faces whose outward normal points south (+z, the D8) or east (+x, the north arm), not inside another block
            foreach (var b in blks)
                foreach (var (su, sv) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    Vector2 p0 = su != 0 ? b.C(su, -1) : b.C(-1, sv), p1 = su != 0 ? b.C(su, 1) : b.C(1, sv);
                    var mid = (p0 + p1) / 2; var cen = new Vector2((float)b.x, (float)b.z); var nrm = (mid - cen).normalized;
                    if (nrm.y < .7f && nrm.x < .7f) continue;
                    var probe = mid + nrm * .6f;
                    if (blks.Any(o => o != b && Inside(o, probe))) continue;
                    var A = W.U(p0.x, baseY, p0.y); var B = W.U(p1.x, baseY, p1.y); var nU = W.U(mid.x + nrm.x, 0, mid.y + nrm.y) - W.U(mid.x, 0, mid.y); nU.Normalize();
                    A += nU * .05f; B += nU * .05f; var f = B - A; float L = f.magnitude; f /= L;
                    float top = (float)Math.Min(3.6, b.h - .6);
                    // ground floor: glass 0.3 … top, a mullion every ~1.6 m, transom at 2.6 m
                    acc.Quad(0, A + f * .3f + Vector3.up * .3f, A + f * .3f + Vector3.up * top, B - f * .3f + Vector3.up * top, B - f * .3f + Vector3.up * .3f, nU);
                    int nm = Mathf.Max(2, Mathf.RoundToInt((L - .6f) / 1.6f));
                    for (int k = 0; k <= nm; k++) acc.Box(1, Vector3.Lerp(A + f * .3f, B - f * .3f, k / (float)nm) + Vector3.up * .3f + nU * .03f, f, new Vector3(.08f, top - .3f, .08f));
                    acc.Box(1, (A + B) / 2 + Vector3.up * 2.62f + nU * .03f, f, new Vector3(.08f, .08f, L - .6f));
                    acc.Box(1, (A + B) / 2 + Vector3.up * top + nU * .03f, f, new Vector3(.1f, .12f, L - .5f));
                    // the upper floor: a dark window band (the photo's large dark windows)
                    if (b.h > 6) acc.Quad(0, A + f * .8f + Vector3.up * 4.3f, A + f * .8f + Vector3.up * 6.3f, B - f * .8f + Vector3.up * 6.3f, B - f * .8f + Vector3.up * 4.3f, nU);
                    nGlass++;
                }
            // the D8 north sidewalk sections in front (D8Plocnik): the car park between it and the blocks, the fence west of them
            var runs = D8Plocnik.Layout(models).Where(r => !r.south).ToList();
            float bx0 = blks.Min(b => Mathf.Min(b.C(-1, -1).x, b.C(1, 1).x, b.C(-1, 1).x, b.C(1, -1).x)), bx1 = blks.Max(b => Mathf.Max(b.C(-1, -1).x, b.C(1, 1).x, b.C(-1, 1).x, b.C(1, -1).x));
            float front = blks.Max(b => Mathf.Max(b.C(-1, -1).y, b.C(1, 1).y, b.C(-1, 1).y, b.C(1, -1).y));   // the blocks' south face (game z)
            var secs = runs.SelectMany(r => r.p.Select((p, i) => (g: W.G(p), n: W.G(p + r.n[i]) - W.G(p), u: p))).ToList();
            (Vector2 g, Vector2 n) At(float gx) { var s = secs.OrderBy(q => Mathf.Abs(q.g.x - gx)).First(); return (s.g, s.n); }
            int nStall = 0, nPlanter = 0; float fenceL = 0;
            if (secs.Count > 0)
            {
                // car park: x from the blocks' west face to their east face, z from 0.4 m behind the sidewalk to the blocks
                var pk = Park(models, blks); park = pk; float cz1 = pk.z0, cz0 = pk.z1;   // (north = −z)
                if (pk.x1 > pk.x0)
                {
                    // one grid with shared corner heights (no steps between the pavers' cells)
                    int nx = Mathf.Max(1, Mathf.CeilToInt(bx1 - bx0)), nz = Mathf.Max(1, Mathf.CeilToInt(cz0 - cz1));
                    var hy = new float[nx + 1, nz + 1];
                    for (int i = 0; i <= nx; i++) for (int k = 0; k <= nz; k++) hy[i, k] = Gnd(new Vector2(Mathf.Lerp(bx0, bx1, i / (float)nx), Mathf.Lerp(cz1, cz0, k / (float)nz)), baseY) + .05f;
                    Vector3 Pc(int i, int k) => W.U(Mathf.Lerp(bx0, bx1, i / (float)nx), hy[i, k], Mathf.Lerp(cz1, cz0, k / (float)nz));
                    for (int i = 0; i < nx; i++) for (int k = 0; k < nz; k++) acc.Quad(4, Pc(i, k), Pc(i + 1, k), Pc(i + 1, k + 1), Pc(i, k + 1), Vector3.up, .5f);
                    // stalls against the blocks (2.5 m, 5 m deep), white lines
                    for (float gx = bx0 + .5f; gx <= bx1 - .5f; gx += 2.5f)
                    {
                        var a = W.U(gx, 0, cz1); var b = W.U(gx, 0, Mathf.Min(cz1 + 5, cz0 - 1)); a.y = Gnd(new Vector2(gx, cz1 + 2.5f), baseY) + .052f; b.y = a.y;
                        var f = b - a; acc.Quad(5, a - Vector3.right * .06f, a + Vector3.right * .06f, b + Vector3.right * .06f, b - Vector3.right * .06f, Vector3.up); nStall++;
                    }
                    // white planters with a clipped shrub along the front, every ~4 m
                    for (float gx = bx0 + 1.5f; gx <= bx1 - 1; gx += 4f)
                    {
                        var g = new Vector2(gx, cz0 - .7f); var u = W.U(g.x, Gnd(g, baseY), g.y);
                        acc.Box(5, u, Vector3.forward, new Vector3(.6f, .55f, .6f)); acc.Box(6, u + Vector3.up * .55f, Vector3.forward, new Vector3(.5f, .35f, .5f), .7f);
                        accCol.Box(5, u, Vector3.forward, new Vector3(.6f, .9f, .6f)); nPlanter++;
                    }
                }
                // the fence: from the blocks' west face westwards, 2.6 m behind the sidewalk (a dry-grass strip in between)
                var fence = secs.Where(s => s.g.x < bx0 - .3f && s.g.x > bx0 - 48).OrderByDescending(s => s.g.x).ToList();
                for (int i = 0; i + 1 < fence.Count; i += 2)
                {
                    int j = Math.Min(i + 2, fence.Count - 1);
                    Vector2 g0 = fence[i].g + fence[i].n * (D8Plocnik.WID + 2.6f), g1 = fence[j].g + fence[j].n * (D8Plocnik.WID + 2.6f);
                    float y0 = Gnd(g0, baseY), y1 = Gnd(g1, baseY);
                    var A = W.U(g0.x, y0, g0.y); var B = W.U(g1.x, y1, g1.y); var f = B - A; f.y = 0; float L = f.magnitude; if (L < .2f) continue; fenceL += L;
                    var mid = (A + B) / 2; mid.y = Mathf.Min(y0, y1) - .1f;
                    acc.Box(3, mid, f, new Vector3(.25f, 1.0f, L + .02f), .5f);                                   // plinth wall 0.9 m
                    for (int k = 0; k < 7; k++) acc.Box(2, mid + Vector3.up * (1.0f + .06f + k * .19f), f, new Vector3(.05f, .14f, L + .02f));   // horizontal slats
                    if (i % 4 == 0) acc.Box(2, A + Vector3.up * (mid.y - A.y + 1.0f), f, new Vector3(.08f, 1.4f, .08f));                   // post
                    accCol.Box(3, mid, f, new Vector3(.3f, 2.4f, L));
                }
                // the sign pylon at the car park's west corner, its face toward the D8
                {
                    var s = At(bx0 + .8f); var g = s.g + s.n * (D8Plocnik.WID + 1.2f); g.x = bx0 + .8f; var u = W.U(g.x, Gnd(g, baseY), g.y);
                    var nU = (W.U(s.g.x - s.n.x, 0, s.g.y - s.n.y) - W.U(s.g.x, 0, s.g.y)).normalized;   // toward the road
                    var f = Vector3.Cross(nU, Vector3.up);
                    acc.Box(1, u, f, new Vector3(.36f, 5.2f, 1.25f));
                    foreach (int sd in new[] { 1, -1 })
                    {
                        var c = u + nU * sd * .185f + Vector3.up * 3.1f; var r = f * .55f * sd; var up = Vector3.up * 1.9f;
                        acc.Quad(7, c - r - up, c - r + up, c + r + up, c + r - up, nU * sd);
                    }
                    accCol.Box(1, u, f, new Vector3(.4f, 5.2f, 1.3f));
                }
            }
            // uv of the sign face: whole texture on the pylon quads (Quad set world uv; fix the last 8 uvs)
            FixSignUv(acc);
            acc.Emit("d8luksic_salon", root, mats, dir);
            var col = accCol.Emit("d8luksic_salon_kolizija", root, mats, dir, true); if (col) col.GetComponent<MeshRenderer>().enabled = false;
            return $"salon: {nGlass} glazed faces, car park {nStall} stalls / {nPlanter} planters, fence {fenceL:F0} m, sign pylon";
        }
        static List<Blk> Blocks()
        {
            var blks = new List<Blk>();
            var mb = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "model-buildings.json")));
            foreach (var oo in MiniJson.L(mb["objects"]))
            {
                var o = MiniJson.O(oo); if (!(MiniJson.G(o, "id") is string id) || !SalonIds.Contains(id)) continue;
                blks.Add(new Blk { x = MiniJson.D(o["x"]), z = MiniJson.D(o["z"]), a = MiniJson.D(o["a"]), w = MiniJson.D(o["w"]), l = MiniJson.D(o["l"]), h = MiniJson.D(o["h"]) });
            }
            return blks;
        }
        /// the car park rect (game x0, x1, z0, z1; x0 > x1 when there is none): between the blocks' south face and 0.4 m behind the
        /// D8 north sidewalk, as wide as the blocks
        static (float x0, float x1, float z0, float z1) Park(List<ModelJ> models, List<Blk> blks)
        {
            if (blks.Count == 0) return (1, 0, 1, 0);
            float bx0 = blks.Min(b => Mathf.Min(b.C(-1, -1).x, b.C(1, 1).x, b.C(-1, 1).x, b.C(1, -1).x)), bx1 = blks.Max(b => Mathf.Max(b.C(-1, -1).x, b.C(1, 1).x, b.C(-1, 1).x, b.C(1, -1).x));
            float front = blks.Max(b => Mathf.Max(b.C(-1, -1).y, b.C(1, 1).y, b.C(-1, 1).y, b.C(1, -1).y));
            var secs = D8Plocnik.Layout(models).Where(r => !r.south).SelectMany(r => r.p.Select(p => W.G(p))).ToList();
            if (secs.Count == 0) return (1, 0, 1, 0);
            float mid = (bx0 + bx1) / 2; var s = secs.OrderBy(q => Mathf.Abs(q.x - mid)).First();
            float cz0 = s.y - (D8Plocnik.WID + .4f);
            return cz0 - front > 3 ? (bx0, bx1, front, cz0) : (1, 0, 1, 0);
        }
        static bool Inside(Blk b, Vector2 p)
        {
            var u = new Vector2((float)Math.Cos(b.a), (float)Math.Sin(b.a)); var v = new Vector2(-u.y, u.x); var d = p - new Vector2((float)b.x, (float)b.z);
            return Mathf.Abs(Vector2.Dot(d, u)) < b.w / 2 && Mathf.Abs(Vector2.Dot(d, v)) < b.l / 2;
        }
        static void FixSignUv(Acc a)
        {
            var t = a.t[7]; if (t.Count == 0) return;
            for (int q = 0; q < t.Count; q += 6)
            {
                int i = t[q]; // the quad's 4 vertices i..i+3 (a: -r-up, b: -r+up, c: +r+up, d: +r-up) — after a possible swap of b / d
                var P = new[] { a.v[i], a.v[i + 1], a.v[i + 2], a.v[i + 3] }; float ymin = P.Min(p => p.y), ymax = P.Max(p => p.y);
                var n = a.n[i]; var right = Vector3.Cross(Vector3.up, n).normalized * -1; var c = (P[0] + P[2]) / 2;
                for (int k = 0; k < 4; k++) a.uv[i + k] = new Vector2(Vector3.Dot(P[k] - c, right) > 0 ? 1 : 0, (P[k].y - ymin) / (ymax - ymin));
            }
        }
        /// the pylon's face: an invented showroom name, white on dark (Generated/Props/D8Luksic/salon_znak.png)
        static Material SignMat()
        {
            string png = WorldBuilder.GEN + "/Props/D8Luksic/salon_znak.png";
            const int Wp = 256, Hp = 896;
            var cv = new SignCanvas(Wp, Hp); cv.Clear(new Color(.09f, .1f, .11f, 1));
            var font = SignFont.Load(); var white = new Color(.95f, .95f, .93f, 1); var accent = new Color(.86f, .55f, .2f, 1);
            cv.Fill(Sd.Box(128, 120, 92, 70, 6), white);
            font.Centred(cv, "KP", 128, 150, 70, 160, new Color(.09f, .1f, .11f, 1));
            font.Centred(cv, "KERAPOLIS", 128, 290, 34, 230, white);
            cv.Fill(Sd.Box(128, 320, 70, 3), accent);
            font.Centred(cv, "SALON", 128, 420, 30, 220, white);
            font.Centred(cv, "KERAMIKE", 128, 480, 30, 220, white);
            font.Centred(cv, "I SANITARIJE", 128, 540, 30, 230, white);
            font.Centred(cv, "Lukšić", 128, 820, 26, 220, accent);
            var tex = new Texture2D(Wp, Hp, TextureFormat.RGBA32, false);
            var px = new Color[Wp * Hp];
            for (int y = 0; y < Hp; y++) for (int x = 0; x < Wp; x++) { var c = cv.px[y * Wp + x]; c.a = 1; px[(Hp - 1 - y) * Wp + x] = c; }
            tex.SetPixels(px); tex.Apply(); File.WriteAllBytes(png, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(png);
            var m = WorldBuilder.Lit("SalonZnak", Color.white, .35f, 0);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(png)); EditorUtility.SetDirty(m);
            return m;
        }
    }
}
