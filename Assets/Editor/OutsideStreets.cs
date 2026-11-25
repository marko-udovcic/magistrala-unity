// Streets outside Marko's road models: ceste_izvan_modela.json (PaketData.CESTE, mag-25-11 Kaštela) (the package's roads that no hand-built
// model covers, world_1to1 frame: x east, z south, origin Štafilić), turned into new streets of our graph where the graph does
// not already have them. Shared by StreetsBuilder (drawn like the graph streets: ribbon, markings, kerbs, terrain) and
// GameDataBuilder (GPS / RoadRouter), so both see the same network.
//   frame      package → game: the affine fit of paket-approaches.json (alignment.transform), as the model importer
//              (tools/world/krizanja/import-model.ts) places the models; Unity's z flip is W.U as everywhere
//   geometry   corners filleted (R 8 m), densified to ≤ 3 m; width / lanes / one-way from the json
//   dedupe     a sample is "ours" when a graph edge runs parallel within its half width + 1 m, or a game model's paving is
//              under it; only the uncovered runs (≥ 6 m) are built
//   heights    our baked ground (tiles) smoothed along the road, σ 30 m (the json y is Marko's DEM, ~1.3 m lower, and bent
//              towards models we may not have; used only where our ground is sea); then pinned at the ends: a graph node /
//              edge → its height, a model → its surface as loaded in game (model-junctions.json grid, so re-imported models
//              with new heights are followed), blended over 15–60 m; grades over 12 % relaxed (Laplacian, pinned ends kept)
//   joins      piece end on a model that is in the game → ends on the model's asphalt (width tapered to it; for the GPS the
//              graph edge through the model ≤ 25 m away is split); else a graph node (≤ 12 m) / a graph edge (≤ half width
//              + 4 m, or straight ahead ≤ 25 m), same level ±3 m → connected (the edge split there; for drawing only where no
//              model / closure / span refers to it, else the street stops on its asphalt), else other new streets' ends
//              (≤ 3 m) / middles (≤ 3 m, straight ahead ≤ 10 m), else a dead end
//   area       only what lies in StreetsBuilder's area (core terrain, game x −8192 … 0): west Kaštela waits for the terrain
//   extra      + Assets/Editor/ulice-dodatne.json (same schema, game frame): streets the package lacks that a signal looks at —
//              Gomilica's two signalised junctions (semafori-bez-modela.json); OSM + DGU orthophoto (03 Tamunić: in the package since mag-23-11-v2)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class OutsideStreets
    {
        public const string SRC = PaketData.CESTE; // (mag-24-11 Kaštela: Putalj → shops, shopping zone, rivas, old road Lukšić → Sućurac; v2: join heights, Vukovarska to 21)
        /// streets the package lacks but a signal looks at (Gomilica of semafori-bez-modela.json): same schema, game frame
        public const string EXTRA = "Assets/Editor/ulice-dodatne.json";
        public const long NODE0 = 9_000_000, EDGE0 = 9_200_000, SPLIT0 = 9_400_000;

        /// override edges a package street replaces, taken out of the graph right after the override layers (StreetsBuilder.LoadGraph,
        /// GameDataBuilder.Build / MergedGraph), with their end nodes when nothing else uses them:
        ///   −7000000050526  51 Kambelovac: the importer's link from the south ramps' end towards Krešimira — flat at 57.7 m (drawn as
        ///                   a ribbon floating up to 13 m over the slope), starting at its own node 6.8 m off the ramps' node 50011 and
        ///                   ending at a node Krešimira does not reach (13 m lower): neither the GPS nor the AI could get from the ramps
        ///                   to Krešimira or back. Without it the package's access-e08 "Ulica kralja Krešimira (spoj na D8)" (its
        ///                   designed climb 46.6 → 56.5 m) is built: from Krešimira's end (access-e07) onto the model where both
        ///                   ramps meet, joined like every package street ending on a model (the ramp at the join split for the GPS).
        public static readonly long[] Superseded = { -7000000050526 };
        /// remove Superseded from a merged graph (edge ends a / b by the accessors); the nodes only they used go too
        public static int DropSuperseded<N, E>(Dictionary<long, N> nodes, Dictionary<long, E> edges, Func<E, long> a, Func<E, long> b)
        {
            int n = 0;
            foreach (long id in Superseded)
            {
                if (!edges.TryGetValue(id, out var e)) continue;
                edges.Remove(id); n++;
                foreach (long nd in new[] { a(e), b(e) }) if (!edges.Values.Any(o => a(o) == nd || b(o) == nd)) nodes.Remove(nd);
            }
            return n;
        }

        // ---- the graph as seen by both builders
        public class GNode { public long id; public double x, y, z; public int deg; }
        public class GEdgeV { public long id, a, b; public List<double[]> pts; public double hw; public bool splittable; }

        // ---- result
        public class NNode { public long id; public double x, y, z; }
        public class Street
        {
            public long id, a, b; public string name = "", cls = "", src = ""; public double width; public int lanes, maxspeed; public bool oneway;
            public List<double[]> pts; public double trimA, trimB; public ModelJ modelA, modelB; public double mhwA = double.NaN, mhwB = double.NaN; public bool paint, sidewalk;
            public double maxGrade; public double L => Len(pts);
        }
        public class Split { public long edge, node; public double s; public bool render; }
        public class Net { public List<NNode> nodes = new List<NNode>(); public List<Street> streets = new List<Street>(); public List<Split> splits = new List<Split>(); public List<string> log = new List<string>(); }

        static double Sq(double v) => v * v;
        /// pieces whose heights the package designed (README_11-24.md: shops_nodes.py, careva_fix.py, descents_fix.py): the shopping zone
        /// of Kaštel Sućurac (access-s*, Ulica satnije Putalj b31, the old road there b30 / b37 / b38 / e05 / e09) and the descents with
        /// their level crossings (Careva e26–e28, Krešimira e08, Tamunić b39, Putaljski put e15 / e16)
        static readonly HashSet<string> DESIGNED = new HashSet<string> { "access-b31", "access-b30", "access-b37", "access-b38", "access-e05", "access-e09", "access-e26", "access-e27", "access-e28", "access-e08", "access-b39", "access-e15", "access-e16" };
        static bool Designed(string id) { string b = id.Split('#')[0]; return DESIGNED.Contains(b) || (b.StartsWith("access-s") && b.Length == 10 && char.IsDigit(b[8])); }
        static double Smooth(double t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);
        static double Len(List<double[]> p) { double L = 0; for (int i = 1; i < p.Count; i++) L += Math.Sqrt(Sq(p[i][0] - p[i - 1][0]) + Sq(p[i][2] - p[i - 1][2])); return L; }

        // ---- graph segment index
        static List<GEdgeV> G; static Dictionary<long, GNode> GN; static Dictionary<long, List<(int e, int i)>> grid; const double GC = 32;
        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
        struct Hit { public int e; public double s, d, y, tx, tz, x, z; }
        static Hit NearestG(double x, double z, double maxD, double yRef = double.NaN)
        {
            var best = new Hit { e = -1, d = maxD };
            for (int cz = (int)Math.Floor((z - maxD) / GC); cz <= (int)Math.Floor((z + maxD) / GC); cz++)
                for (int cx = (int)Math.Floor((x - maxD) / GC); cx <= (int)Math.Floor((x + maxD) / GC); cx++)
                {
                    if (!grid.TryGetValue(Key(cx, cz), out var l)) continue;
                    foreach (var (e, i) in l)
                    {
                        var p = G[e].pts[i]; var q = G[e].pts[i + 1];
                        double bx = q[0] - p[0], bz = q[2] - p[2], l2 = bx * bx + bz * bz, t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((x - p[0]) * bx + (z - p[2]) * bz) / l2));
                        double px = p[0] + bx * t, pz = p[2] + bz * t, d = Math.Sqrt(Sq(x - px) + Sq(z - pz));
                        // (distance beyond the edge's own half width: a wide road counts as near from further away)
                        double dd = d - G[e].hw; if (dd >= best.d) continue;
                        if (!double.IsNaN(yRef) && Math.Abs(p[1] + (q[1] - p[1]) * t - yRef) > 3) continue; // (another level: a bridge, the D8 above)
                        double ll = Math.Sqrt(l2); if (ll < 1e-9) ll = 1;
                        double s = 0; for (int k = 0; k < i; k++) s += Math.Sqrt(Sq(G[e].pts[k + 1][0] - G[e].pts[k][0]) + Sq(G[e].pts[k + 1][2] - G[e].pts[k][2]));
                        best = new Hit { e = e, s = s + ll * t, d = dd, y = p[1] + (q[1] - p[1]) * t, tx = bx / ll, tz = bz / ll, x = px, z = pz };
                    }
                }
            return best;
        }
        /// graph covers (x, z) going (tx, tz): a parallel edge with the point within its half width + 1 m
        static bool Covered(double x, double z, double tx, double tz)
        {
            for (int cz = (int)Math.Floor((z - 20) / GC); cz <= (int)Math.Floor((z + 20) / GC); cz++)
                for (int cx = (int)Math.Floor((x - 20) / GC); cx <= (int)Math.Floor((x + 20) / GC); cx++)
                {
                    if (!grid.TryGetValue(Key(cx, cz), out var l)) continue;
                    foreach (var (e, i) in l)
                    {
                        var p = G[e].pts[i]; var q = G[e].pts[i + 1];
                        double bx = q[0] - p[0], bz = q[2] - p[2], l2 = bx * bx + bz * bz; if (l2 < 1e-6) continue;
                        double t = Math.Max(0, Math.Min(1, ((x - p[0]) * bx + (z - p[2]) * bz) / l2));
                        double d = Math.Sqrt(Sq(x - p[0] - bx * t) + Sq(z - p[2] - bz * t));
                        if (d < G[e].hw + 1 && Math.Abs((tx * bx + tz * bz) / Math.Sqrt(l2)) > .8) return true;
                    }
                }
            return false;
        }

        // ---- geometry
        static List<double[]> Fillet(List<double[]> P, double R)
        {
            if (P.Count < 3) return P;
            var o = new List<double[]> { P[0] };
            for (int i = 1; i + 1 < P.Count; i++)
            {
                var a = P[i - 1]; var b = P[i]; var c = P[i + 1];
                double l1 = Math.Sqrt(Sq(b[0] - a[0]) + Sq(b[2] - a[2])), l2 = Math.Sqrt(Sq(c[0] - b[0]) + Sq(c[2] - b[2]));
                if (l1 < .05 || l2 < .05) { o.Add(b); continue; }
                double ux = (b[0] - a[0]) / l1, uz = (b[2] - a[2]) / l1, vx = (c[0] - b[0]) / l2, vz = (c[2] - b[2]) / l2;
                double th = Math.Acos(Math.Max(-1, Math.Min(1, ux * vx + uz * vz)));
                if (th < 8 * Math.PI / 180) { o.Add(b); continue; }
                double t = Math.Min(R * Math.Tan(th / 2), .45 * Math.Min(l1, l2));
                var p0 = new[] { b[0] - ux * t, b[1] - (b[1] - a[1]) * t / l1, b[2] - uz * t }; var p2 = new[] { b[0] + vx * t, b[1] + (c[1] - b[1]) * t / l2, b[2] + vz * t };
                int n = Math.Max(2, (int)Math.Ceiling(t));
                for (int k = 0; k <= n; k++)
                {
                    double f = (double)k / n, g = 1 - f;
                    o.Add(new[] { g * g * p0[0] + 2 * g * f * b[0] + f * f * p2[0], g * g * p0[1] + 2 * g * f * b[1] + f * f * p2[1], g * g * p0[2] + 2 * g * f * b[2] + f * f * p2[2] });
                }
            }
            o.Add(P[P.Count - 1]); return o;
        }
        static List<double[]> Densify(List<double[]> P, double step)
        {
            var o = new List<double[]> { P[0] };
            for (int i = 1; i < P.Count; i++)
            {
                var a = P[i - 1]; var b = P[i]; double L = Math.Sqrt(Sq(b[0] - a[0]) + Sq(b[2] - a[2])); if (L < .05) continue;
                int n = Math.Max(1, (int)Math.Ceiling(L / step));
                for (int k = 1; k <= n; k++) { double f = (double)k / n; o.Add(new[] { a[0] + (b[0] - a[0]) * f, a[1] + (b[1] - a[1]) * f, a[2] + (b[2] - a[2]) * f }); }
            }
            return o;
        }
        static double[] Cum(List<double[]> P) { var c = new double[P.Count]; for (int i = 1; i < P.Count; i++) c[i] = c[i - 1] + Math.Sqrt(Sq(P[i][0] - P[i - 1][0]) + Sq(P[i][2] - P[i - 1][2])); return c; }
        static double[] Gauss(double[] v, double[] s, double sigma)
        {
            var o = new double[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                double sw = 0, sv = 0;
                for (int k = 0; k < v.Length; k++) { double d = s[k] - s[i]; if (Math.Abs(d) > 3 * sigma) continue; double w = Math.Exp(-d * d / (2 * sigma * sigma)); sw += w; sv += w * v[k]; }
                o[i] = sv / sw;
            }
            return o;
        }

        /// model surface as loaded in game at (x, z), searching up to 3 m along (dx, dz) (the join point sits at the asphalt's end)
        public static double ModelY(ModelJ m, double x, double z, double dx, double dz) => ModelY(m, x, z, dx, dz, 3, out _);
        /// … up to maxT m; ahead = how far along (dx, dz) the paving starts (0 when the point is on it or the paving is behind)
        public static double ModelY(ModelJ m, double x, double z, double dx, double dz, double maxT, out double ahead)
        {
            ahead = 0;
            for (double t = 0; t <= maxT; t += .25)
                foreach (int sd in new[] { 1, -1 })
                {
                    if (sd < 0 && t > 3) continue; // (behind the end only as before: 3 m)
                    double h = m.Surface(x + dx * t * sd, z + dz * t * sd); if (!double.IsNaN(h)) { ahead = sd > 0 ? t : 0; return h; }
                }
            return double.NaN;
        }

        // ---- the run ends
        class End { public int kind; /*0 free, 1 graph node, 2 graph edge, 3 model, 4 new node*/ public long node = -1; public int gedge = -1; public double s, y = double.NaN; public ModelJ model; public double trim, mhw = double.NaN; public double[] ext; public double[] rpt; }
        class Run { public string id, name, cls; public double width; public int lanes, maxspeed; public bool oneway; public List<double[]> p; public End a = new End(), b = new End(); }

        public static Net Compute(List<GNode> nodes, List<GEdgeV> edges, List<ModelJ> models, double ax0, double ax1, double az0, double az1)
        {
            var net = new Net();
            PaketData.Need(SRC);
            G = edges; GN = nodes.ToDictionary(n => n.id);
            grid = new Dictionary<long, List<(int, int)>>();
            for (int e = 0; e < G.Count; e++)
                for (int i = 0; i + 1 < G[e].pts.Count; i++)
                {
                    var p = G[e].pts[i]; var q = G[e].pts[i + 1];
                    for (int cz = (int)Math.Floor(Math.Min(p[2], q[2]) / GC); cz <= (int)Math.Floor(Math.Max(p[2], q[2]) / GC); cz++)
                        for (int cx = (int)Math.Floor(Math.Min(p[0], q[0]) / GC); cx <= (int)Math.Floor(Math.Max(p[0], q[0]) / GC); cx++)
                        { long k = Key(cx, cz); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<(int, int)>(); l.Add((e, i)); }
                }
            var pa = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "paket-approaches.json")));
            var T = MiniJson.O(MiniJson.G(MiniJson.O(pa["alignment"]), "transform") ?? pa["transform"]);
            double Ta = MiniJson.D(T["a"]), Tb = MiniJson.D(T["b"]), Tc = MiniJson.D(T["c"]), Td = MiniJson.D(T["d"]), Ttx = MiniJson.D(T["tx"]), Ttz = MiniJson.D(T["tz"]);
            var byGlb = new Dictionary<string, ModelJ>(); foreach (var m in models) byGlb[m.glb] = m;
            bool InA(double x, double z) => x >= ax0 && x < ax1 && z >= az0 && z < az1;

            var runs = new List<Run>(); int pieces = 0, dupPieces = 0; double kmAll = 0, kmNew = 0;
            var srcRoads = MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(SRC)))["roads"]).Select(rd => (rd, pkg: true)).ToList();
            if (File.Exists(EXTRA)) srcRoads.AddRange(MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(EXTRA)))["roads"]).Select(rd => (rd, pkg: false)));
            foreach (var (ro, pkg) in srcRoads)
            {
                var r = MiniJson.O(ro);
                var P = new List<double[]>();
                foreach (var po in MiniJson.L(r["points"]))
                {
                    var q = MiniJson.L(po); double x = MiniJson.D(q[0]), z = MiniJson.D(q[2]);
                    P.Add(pkg ? new[] { Ta * x + Tb * z + Ttx, MiniJson.D(q[1]), Tc * x + Td * z + Ttz } : new[] { x, MiniJson.D(q[1]), z });
                }
                if (P.Count < 2 || !P.Any(p => InA(p[0], p[2]))) continue;
                pieces++;
                // a join point → the model when it is in the game
                ModelJ JoinModel(string k) { var j = MiniJson.O(MiniJson.G(r, k)); if (j == null) return null; byGlb.TryGetValue(Path.GetFileName((string)j["model"]), out var m); return m; }
                var mS = JoinModel("start"); var mE = JoinModel("end");
                var S = Densify(Fillet(P, 8), 3); var c = Cum(S); int n = S.Count; kmAll += c[n - 1] / 1000;
                // heights: our baked ground smoothed along the road (σ 30 m, as the json's own profile is Marko's DEM smoothed; its
                // y is not used as such: it is ~1.3 m below our ground and raised / lowered near models we may not have yet); where
                // our ground is sea (the coast differs) the json height
                var d = new double[n]; for (int i = 0; i < n; i++) { double h = Tiles.Height(S[i][0], S[i][2]); d[i] = h < .8 ? Math.Max(.8, S[i][1]) : h; }
                if (pkg && Designed(r["id"].ToString()))
                {
                    // mag-24-11: heights the package designed (the shopping zone of Sućurac with Ulica satnije Putalj under the D8, the
                    // descents from the D8 to the level crossings): its own profile, moved onto our ground by the median difference —
                    // our ground smoothed gave 12 % ramps through the car parks
                    var dv = Enumerable.Range(0, n).Select(i => d[i] - S[i][1]).OrderBy(v => v).ToList(); double off = dv[n / 2];
                    for (int i = 0; i < n; i++) S[i][1] += off;
                }
                else { var ds = Gauss(d, c, 30); for (int i = 0; i < n; i++) S[i][1] = ds[i]; }
                // coverage per sample
                var cov = new bool[n];
                for (int i = 0; i < n; i++)
                {
                    var a = S[Math.Max(0, i - 1)]; var b = S[Math.Min(n - 1, i + 1)]; double tx = b[0] - a[0], tz = b[2] - a[2], l = Math.Sqrt(tx * tx + tz * tz); if (l < 1e-9) l = 1;
                    cov[i] = !InA(S[i][0], S[i][2]) || WorldData.SurfDist(models, S[i][0], S[i][2]) < .3 || Covered(S[i][0], S[i][2], tx / l, tz / l);
                }
                int built = 0;
                for (int i = 0; i < n;)
                {
                    if (cov[i]) { i++; continue; }
                    int j = i; while (j + 1 < n && !cov[j + 1]) j++;
                    int i0 = Math.Max(0, i - 1), j0 = Math.Min(n - 1, j + 1); // (one covered sample each side: the run reaches the road it leaves)
                    if (c[j0] - c[i0] >= 6)
                    {
                        var run = new Run
                        {
                            id = r["id"].ToString(), name = r["name"] as string ?? "", cls = r["cls"] as string ?? "", width = MiniJson.D(r["width"]),
                            oneway = r["oneway"] is bool ow && ow, maxspeed = r.TryGetValue("maxspeed", out var ms) && ms != null ? (int)MiniJson.D(ms) : 0, p = S.GetRange(i0, j0 - i0 + 1).Select(q => (double[])q.Clone()).ToList(),
                        };
                        int lf = (int)MiniJson.D(r["lanesForward"]), lb = (int)MiniJson.D(r["lanesBackward"]);
                        run.lanes = Math.Max(1, run.oneway ? lf : Math.Max(lf, lb));
                        // (the piece's join point may lie a sample or two inside the model's paving — those samples are "covered", so the
                        // run stops ≤ 4 m short of it: still the join onto that model. 51: access-e08 ended 2.3 m short of the south
                        // ramps' node, as a dead end 3.6 m under the model's asphalt — Krešimira cut off from the ramps for the GPS)
                        if (mS != null && (i0 == 0 || c[i0] <= 4)) { run.a.kind = 3; run.a.model = mS; }
                        if (mE != null && (j0 == n - 1 || c[n - 1] - c[j0] <= 4)) { run.b.kind = 3; run.b.model = mE; }
                        runs.Add(run); built++; kmNew += (c[j0] - c[i0]) / 1000;
                    }
                    i = j + 1;
                }
                if (built == 0) dupPieces++;
            }

            // ---- ends: model / graph node / graph edge
            long nextNode = NODE0;
            void EndOn(Run run, bool atB)
            {
                var P = run.p; var e = atB ? run.b : run.a; int k = atB ? P.Count - 1 : 0, k2 = atB ? Math.Max(0, P.Count - 3) : Math.Min(P.Count - 1, 2);
                double dx = P[k][0] - P[k2][0], dz = P[k][2] - P[k2][2], l = Math.Sqrt(dx * dx + dz * dz); if (l < 1e-9) l = 1; dx /= l; dz /= l; // outward
                // (the joined point keeps the street's own height: the pin e.y is blended in at the end)
                // (a join point beside the street's line — a model stub's end node 1–2 m off the package line —: the last metres are bent
                // over to it, smoothstep over max(10 m, 4 × the offset), instead of one sharp jog at the end)
                void Put(double x, double y, double z)
                {
                    double ox = x - P[k][0], oz = z - P[k][2], al = ox * dx + oz * dz, lx = ox - al * dx, lz = oz - al * dz, lat = Math.Sqrt(lx * lx + lz * lz);
                    if (lat > .2 && lat < 4)
                    {
                        double Lb = Math.Max(10, 4 * lat), sAcc = 0;
                        for (int i = k, st = atB ? -1 : 1; i >= 0 && i < P.Count; i += st)
                        {
                            if (i != k) sAcc += Math.Sqrt(Sq(P[i][0] - P[i - st][0]) + Sq(P[i][2] - P[i - st][2]));
                            if (sAcc >= Lb) break;
                            double t = 1 - sAcc / Lb, w = t * t * (3 - 2 * t);
                            P[i] = new[] { P[i][0] + lx * w, P[i][1], P[i][2] + lz * w };
                        }
                    }
                    var q = new[] { x, P[k][1], z }; if (Math.Sqrt(Sq(x - P[k][0]) + Sq(z - P[k][2])) < .5) P[k] = q; else if (atB) P.Add(q); else P.Insert(0, q);
                }
                if (e.kind == 3)
                {
                    // (mag-25-11 moved the ends onto the model road axis in the package's frame; where our import cut that model's arm
                    // shorter, the paving starts up to 6 m further on — 19 Put Zagoraca: the 3 m search missed it and the street fell back
                    // to a graph node 2 m to the side, leaving a wedge of ground. Now up to 8 m ahead, and the street runs on to it)
                    double y = ModelY(e.model, P[k][0], P[k][2], dx, dz, 8, out double ahead);
                    if (double.IsNaN(y)) { e.kind = 0; e.model = null; }
                    else
                    {
                        double ex = P[k][0], ez = P[k][2]; // (before Put: at the start it inserts the new end at P[0])
                        e.y = y; Put(ex + dx * (ahead + .5), y, ez + dz * (ahead + .5));
                        // the model's asphalt width 1 m in from the join (the street tapers to it), when it is a road end and not a wide paving
                        double m1 = ahead + (atB ? 1.5 : 2.0), cx = ex + dx * m1, cz = ez + dz * m1, uL = -1, uR = -1; // (as before: 1 m in from the join; at the start P[k] used to be the inserted end)
                        if (!double.IsNaN(e.model.Surface(cx, cz)))
                        {
                            for (double u = 0; u <= 10 && uR < 0; u += .25) if (double.IsNaN(e.model.Surface(cx - dz * u, cz + dx * u))) uR = u;
                            for (double u = 0; u <= 10 && uL < 0; u += .25) if (double.IsNaN(e.model.Surface(cx + dz * u, cz - dx * u))) uL = u;
                        }
                        if (uL > 0 && uR > 0 && (uL + uR) / 2 >= 1.5 && (uL + uR) / 2 <= run.width * .8 + 1) e.mhw = (uL + uR) / 2;
                        // routing: the model's road in our graph (an edge through the square at this level ≤ 25 m away; only the GPS
                        // graph is split there, the drawn street ends on the model)
                        var hr = NearestG(P[k][0], P[k][2], 25, y);
                        if (hr.e >= 0) { e.gedge = hr.e; e.s = hr.s; e.rpt = new[] { hr.x, hr.y, hr.z }; }
                        return;
                    }
                }
                // a graph node near the end
                GNode bestN = null; double bd = 12;
                foreach (var gn in GN.Values) { double dd = Math.Sqrt(Sq(gn.x - P[k][0]) + Sq(gn.z - P[k][2])); if ((gn.deg == 1 ? dd < bd : dd < Math.Min(bd, 6)) && Math.Abs(gn.y - P[k][1]) < 3) { bd = dd; bestN = gn; } }
                // or an edge: at the end, or straight ahead
                Hit h = NearestG(P[k][0], P[k][2], 4, P[k][1]);
                if (h.e < 0) for (double t = 1; t <= 25 && h.e < 0; t += 1) { var hh = NearestG(P[k][0] + dx * t, P[k][2] + dz * t, 0, P[k][1]); if (hh.e >= 0) h = hh; }
                if (bestN == null && h.e >= 0)
                {
                    var ge = G[h.e]; double L = Len(ge.pts);
                    // close to one of the edge's nodes: that node
                    if (h.s < 8 && Math.Abs(GN[ge.a].y - P[k][1]) < 3) bestN = GN[ge.a]; else if (L - h.s < 8 && Math.Abs(GN[ge.b].y - P[k][1]) < 3) bestN = GN[ge.b];
                    else
                    {
                        e.kind = 2; e.gedge = h.e; e.s = h.s; e.y = h.y; e.node = nextNode++; e.trim = ge.splittable ? 0 : Math.Max(0, ge.hw - .15);  // (StreetsBuilder cuts the line at trim + 0.1: 5 cm onto the asphalt)
                        Put(h.x, h.y, h.z); return;
                    }
                }
                if (bestN != null) { e.kind = 1; e.node = bestN.id; e.y = bestN.y; Put(bestN.x, bestN.y, bestN.z); }
            }
            foreach (var run in runs) { EndOn(run, false); EndOn(run, true); }

            // ---- free ends: other new streets' ends, then their middles
            var free = new List<(Run r, bool atB)>();
            foreach (var run in runs) { if (run.a.kind == 0) free.Add((run, false)); if (run.b.kind == 0) free.Add((run, true)); }
            double[] EndPt(Run r, bool atB) => atB ? r.p[r.p.Count - 1] : r.p[0];
            End EndOf(Run r, bool atB) => atB ? r.b : r.a;
            for (int i = 0; i < free.Count; i++)
            {
                var ei = EndOf(free[i].r, free[i].atB); if (ei.kind != 0) continue;
                var pi = EndPt(free[i].r, free[i].atB); var grp = new List<int> { i };
                for (int k = i + 1; k < free.Count; k++) { var pk = EndPt(free[k].r, free[k].atB); if (EndOf(free[k].r, free[k].atB).kind == 0 && Math.Sqrt(Sq(pk[0] - pi[0]) + Sq(pk[2] - pi[2])) < 3) grp.Add(k); }
                if (grp.Count < 2) continue;
                long id = nextNode++; double mx = 0, my = 0, mz = 0;
                foreach (var k in grp) { var p = EndPt(free[k].r, free[k].atB); mx += p[0] / grp.Count; my += p[1] / grp.Count; mz += p[2] / grp.Count; }
                foreach (var k in grp) { var e = EndOf(free[k].r, free[k].atB); e.kind = 4; e.node = id; e.y = my; var p = EndPt(free[k].r, free[k].atB); p[0] = mx; p[1] = my; p[2] = mz; }
                net.nodes.Add(new NNode { id = id, x = mx, y = my, z = mz });
            }
            // a free end on another new street's middle: split that street there
            var cutsAt = new Dictionary<Run, List<(int idx, long node)>>();
            foreach (var (r, atB) in free)
            {
                var e = EndOf(r, atB); if (e.kind != 0) continue;
                var P = r.p; var p = EndPt(r, atB); int k2 = atB ? Math.Max(0, P.Count - 3) : Math.Min(P.Count - 1, 2);
                double dx = p[0] - P[k2][0], dz = p[2] - P[k2][2], l = Math.Sqrt(dx * dx + dz * dz); if (l < 1e-9) l = 1; dx /= l; dz /= l;
                Run best = null; int bi = -1; double bd = 1e9;
                foreach (var o in runs)
                {
                    if (o == r) continue; var oc = Cum(o.p);
                    for (int q = 1; q + 1 < o.p.Count; q++)
                    {
                        if (oc[q] < 4 || oc[oc.Length - 1] - oc[q] < 4) continue;
                        for (double t = 0; t <= 10; t += 1)
                        {
                            double dd = Math.Sqrt(Sq(o.p[q][0] - p[0] - dx * t) + Sq(o.p[q][2] - p[2] - dz * t)); double lim = t == 0 ? 3 : Math.Max(1.5, o.width / 2);
                            if (dd < lim && dd + t < bd) { bd = dd + t; best = o; bi = q; }
                        }
                    }
                }
                if (best == null) continue;
                if (!cutsAt.TryGetValue(best, out var lst)) cutsAt[best] = lst = new List<(int, long)>();
                long nid = -1; foreach (var c in lst) if (Math.Abs(c.idx - bi) <= 1) nid = c.node;
                if (nid < 0) { nid = nextNode++; lst.Add((bi, nid)); var q = best.p[bi]; net.nodes.Add(new NNode { id = nid, x = q[0], y = q[1], z = q[2] }); }
                var qq = best.p[lst.First(c => c.node == nid).idx];
                e.kind = 4; e.node = nid; e.y = qq[1];
                e.ext = new[] { qq[0], qq[1], qq[2] };
            }
            var all = new List<Run>();
            foreach (var r in runs)
            {
                if (!cutsAt.TryGetValue(r, out var lst)) { all.Add(r); continue; }
                lst.Sort((x, y) => x.idx.CompareTo(y.idx)); int from = 0; End prev = r.a;
                foreach (var (idx, node) in lst)
                {
                    var part = new Run { id = r.id, name = r.name, cls = r.cls, width = r.width, lanes = r.lanes, oneway = r.oneway, p = r.p.GetRange(from, idx - from + 1), a = prev, b = new End { kind = 4, node = node, y = r.p[idx][1] } };
                    all.Add(part); prev = new End { kind = 4, node = node, y = r.p[idx][1] }; from = idx;
                }
                all.Add(new Run { id = r.id, name = r.name, cls = r.cls, width = r.width, lanes = r.lanes, oneway = r.oneway, p = r.p.GetRange(from, r.p.Count - from), a = prev, b = r.b });
            }

            // ---- nodes of the remaining ends, heights pinned, streets out
            long nextEdge = EDGE0; int steep = 0;
            foreach (var r in all)
            {
                foreach (var atB in new[] { false, true })
                {
                    var e = EndOf(r, atB); var p = EndPt(r, atB);
                    if (e.kind == 3 && e.rpt != null && e.node < 0)
                    {
                        e.node = nextNode++; net.nodes.Add(new NNode { id = e.node, x = e.rpt[0], y = e.rpt[1], z = e.rpt[2] });
                        net.splits.Add(new Split { edge = G[e.gedge].id, s = e.s, node = e.node, render = false });
                    }
                    if ((e.kind == 0 || e.kind == 3) && e.node < 0) { e.node = nextNode++; net.nodes.Add(new NNode { id = e.node, x = p[0], y = double.IsNaN(e.y) ? p[1] : e.y, z = p[2] }); }
                    if (e.kind == 2)
                    {
                        if (!net.nodes.Any(q => q.id == e.node)) net.nodes.Add(new NNode { id = e.node, x = p[0], y = e.y, z = p[2] });
                        if (!net.splits.Any(q => q.node == e.node)) net.splits.Add(new Split { edge = G[e.gedge].id, s = e.s, node = e.node, render = G[e.gedge].splittable });
                    }
                }
                var R0 = new List<double[]>(r.p); if (r.a.ext != null) R0.Insert(0, r.a.ext); if (r.b.ext != null) R0.Add(r.b.ext);
                var P = Densify(R0, 3); var c = Cum(P); double L = c[c.Length - 1]; if (L < 1) continue;
                // end pins, blended over 15–60 m (shared between both ends on a short street)
                double dA = double.IsNaN(r.a.y) ? 0 : r.a.y - P[0][1], dB = double.IsNaN(r.b.y) ? 0 : r.b.y - P[P.Count - 1][1];
                bool pinA = !double.IsNaN(r.a.y), pinB = !double.IsNaN(r.b.y);
                double LA = Math.Max(15, Math.Min(60, 8 * Math.Abs(dA) + 15)), LB = Math.Max(15, Math.Min(60, 8 * Math.Abs(dB) + 15));
                if (pinA && pinB && LA + LB > L) { double f = L / (LA + LB); LA *= f; LB *= f; }
                for (int i = 0; i < P.Count; i++)
                {
                    double wA = !pinA ? 0 : 1 - Smooth(c[i] / LA), wB = !pinB ? 0 : 1 - Smooth((L - c[i]) / LB);
                    P[i][1] += dA * wA + dB * wB;
                }
                if (pinA) P[0][1] = r.a.y; if (pinB) P[P.Count - 1][1] = r.b.y;
                // grade relax: where a stretch is steeper than 12 %, Laplacian smoothing (the pinned ends stay)
                for (int it = 0; it < 3000; it++) // (3000: access-e08, 12.6 m up over 115 m between two pinned ends, was left at 15.5 % after 300)
                {
                    bool any = false;
                    for (int i = 1; i + 1 < P.Count; i++)
                    {
                        double g0 = Math.Abs(P[i][1] - P[i - 1][1]) / Math.Max(.1, c[i] - c[i - 1]), g1 = Math.Abs(P[i + 1][1] - P[i][1]) / Math.Max(.1, c[i + 1] - c[i]);
                        if (Math.Max(g0, g1) <= .12) continue;
                        double f = (c[i] - c[i - 1]) / Math.Max(.1, c[i + 1] - c[i - 1]); P[i][1] += .5 * (P[i - 1][1] + (P[i + 1][1] - P[i - 1][1]) * f - P[i][1]); any = true;
                    }
                    if (!pinA && P.Count > 1 && Math.Abs(P[0][1] - P[1][1]) / Math.Max(.1, c[1]) > .12) { P[0][1] = P[1][1] + Math.Sign(P[0][1] - P[1][1]) * .12 * c[1]; any = true; }
                    if (!pinB && P.Count > 1) { int z = P.Count - 1; double dz2 = c[z] - c[z - 1]; if (Math.Abs(P[z][1] - P[z - 1][1]) / Math.Max(.1, dz2) > .12) { P[z][1] = P[z - 1][1] + Math.Sign(P[z][1] - P[z - 1][1]) * .12 * dz2; any = true; } }
                    if (!any) break;
                }
                double g = 0; for (int i = 4; i < P.Count; i++) { double ds = c[i] - c[i - 4]; if (ds > 5) g = Math.Max(g, Math.Abs(P[i][1] - P[i - 4][1]) / ds); }
                var st = new Street
                {
                    id = nextEdge++, a = r.a.node, b = r.b.node, name = r.name, cls = r.cls, src = r.id, width = r.width, lanes = r.lanes, maxspeed = r.maxspeed, oneway = r.oneway, pts = P,
                    trimA = r.a.kind == 2 ? r.a.trim : 0, trimB = r.b.kind == 2 ? r.b.trim : 0, modelA = r.a.model, modelB = r.b.model, mhwA = r.a.mhw, mhwB = r.b.mhw,
                    paint = r.width >= 5.5, sidewalk = r.width >= 5.5 && r.cls != "service" && r.cls != "access" && !r.cls.EndsWith("_link"), maxGrade = g,
                };
                if (g > .12) { steep++; net.log.Add($"  steep: {r.id} {r.name} {g * 100:F1} %"); }
                net.streets.Add(st);
            }
            var kinds = all.SelectMany(r => new[] { r.a.kind, r.b.kind }).GroupBy(k => k).OrderBy(q => q.Key).Select(q => $"{new[] { "dead end", "graph node", "graph edge", "model", "new node" }[q.Key]} {q.Count()}");
            net.log.Insert(0, $"outside streets ({Path.GetFileName(SRC)}): {pieces} pieces in the area ({kmAll:F1} km), {dupPieces} wholly ours already; built {net.streets.Count} streets, {net.streets.Sum(s => s.L) / 1000:F2} km; ends: {string.Join(", ", kinds)}; {net.splits.Count} graph edges split; {steep} steeper than 12 %");
            return net;
        }
    }
}
