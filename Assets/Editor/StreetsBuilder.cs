// Menu Magistrala/Build streets — the web game's own road graph around Marko's models: graph.json + the junction-override layers
// (the web loader's order, as GameDataBuilder), road-closures.json and stops.json, built as drivable streets in the core terrain
// area (game x −10240 … 0, z −7168 … −4096). Ports of the web code (BalkanDriver client/src/world2, read-only):
//   worker/buildRoads.ts     asphalt ribbon per graph segment (halfWidth from format/roadSpec.ts), 1.6 m verges dropping 0.4 m,
//                            side roads trimmed at the through road's edge, width tapers at degree-2 nodes, junction discs with
//                            kerb fillets, collider flaps; inside the model squares format/modelJunctions.ts modelCuts / cutAway
//                            (ribbons end at the arm ends), armSnap (the last 14 m pulled onto the arm's asphalt corners) and
//                            ownedByModel (triangles on a model's footprint dropped)
//   tiles/materials.ts       Croatian markings as paint quads: edge lines, centre line 3/9 m, lane lines (one-way 6/18 m)
//   worker/roadFurniture.ts  urban sidewalks (verge 'urban_kerb', not the D8): 0.15 m mountable kerb + 1.6 m footway
//   ui/DeliverySite.tsx      stop courts (footprint-led sites: rounded paved rectangle with a kerbed surround) + bay lines
//   closures/Roadworks2.tsx  works barrier at every closure point of road-closures.json outside the models (+ model paveEnds)
//   worker/buildTerrain.ts   ground pushed under the streets (render ry − 0.18 within halfWidth + 1.6 m), courts flattened
// Meshes merged per 256 m chunk (asphalt / paint / verge / kerb / footway / court), static, no shadows (flat); one MeshCollider
// per chunk with the Asphalt physic material. A 2 m cover mask (Assets/Generated/Streets/cover.bytes) keeps the terrain details
// and trees off the streets (GraphicsPass.Details / WorldBuilder.Trees read it; Build also clears the current ones).
// Plus the streets outside Marko's models that our graph lacks (OutsideStreets.cs, ~/Documents/mag-11-19/ceste_izvan_modela.json):
// added as edges of the same kind (drawn up to the models' paving, the ground also raised to them), graph edges split at their T's.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static partial class StreetsBuilder
    {
        public const double AX0 = -10240, AX1 = 0, AZ0 = -7168, AZ1 = -4096; // (AX0: mag-11-19 Štafilić, = WorldBuilder.TX0)
        const string OUT = WorldBuilder.GEN + "/Streets";
        const float CHUNK = 256;

        // ------------------------------------------------------------------ graph
        class N { public long id; public double x, y, z, rbR; public string kind = ""; }
        class E
        {
            public long id, a, b; public bool oneway, roundabout, ortho; public bool? dual; public int lanes;
            public string type = "", rf = "", name = "", spur, verge, profile;
            public double laneW = double.NaN, shoulderW = double.NaN, innerStripW = double.NaN, medianWidth = double.NaN;
            public List<double[]> pts; public List<(string k, double f, double t)> spans = new List<(string, double, double)>();
            public double[] cum; public Spec sp; public bool closed; public bool? closedOwn; // closedOwn: the edge's own `closed` (model stubs, road editor)
            public double L => cum[cum.Length - 1];
            // streets outside the models (OutsideStreets): end trims at a through road that was not split, the models at the ends
            public bool outside, noPaint; public double trimA, trimB, mhwA = double.NaN, mhwB = double.NaN; public ModelJ mA, mB; public long splitOf = -1;
            public E Clone() { var c = (E)MemberwiseClone(); c.spans = new List<(string, double, double)>(spans); return c; }
        }
        class Spec { public double laneW, medianHalf, innerStrip, shoulder, halfWidth, edgeLine; public int lanes; public bool dual, oneway, barriers; }

        static List<N> nodes; static Dictionary<long, int> nIdx; static List<E> edges; static Dictionary<long, int> eIdx; static List<int>[] adj;
        static Dictionary<long, List<(int e, int i)>> grid; const double GC = 32;
        static HashSet<int> hidden;

        static bool IsD8(E e) => e.type != "ramp" && (e.rf.Split(';').Any(r => Regex.IsMatch(r.Trim(), @"^D\s*8$", RegexOptions.IgnoreCase)) ||
            Regex.IsMatch(e.name ?? "", @"^(Cesta pape Ivana Pavla II\.?|Jadranska magistrala|Jadro I{1,2})$", RegexOptions.IgnoreCase));

        static Spec RoadSpec(E e)
        {
            int lanes = IsD8(e) ? 2 : Math.Max(1, e.lanes == 0 ? 1 : e.lanes);
            double laneW, shoulder;
            switch (e.type)
            {
                case "motorway": laneW = 3.75; shoulder = 2.5; break;
                case "ramp": laneW = 3.75; shoulder = 1.5; break;
                case "trunk": case "primary": laneW = 3.5; shoulder = .75; break;
                case "secondary": laneW = 3.25; shoulder = .5; break;
                default: laneW = 3.0; shoulder = .3; break;
            }
            bool dual = !e.oneway && (e.dual ?? (e.type == "motorway" || (e.type == "trunk" && lanes >= 2)));
            laneW = IsD8(e) ? (e.ortho && !double.IsNaN(e.laneW) ? e.laneW : 3.5) : (!double.IsNaN(e.laneW) ? e.laneW : laneW);
            if (!double.IsNaN(e.shoulderW)) shoulder = e.shoulderW;
            double mh = dual ? Math.Max(0, (double.IsNaN(e.medianWidth) ? 4 : e.medianWidth) / 2) : 0;
            double strip = dual ? (double.IsNaN(e.innerStripW) ? .75 : e.innerStripW) : 0;
            double edge = e.oneway ? lanes * laneW / 2 : dual ? mh + strip + lanes * laneW : lanes * laneW;
            return new Spec { laneW = laneW, lanes = lanes, medianHalf = mh, innerStrip = strip, dual = dual, oneway = e.oneway, shoulder = shoulder, halfWidth = edge + shoulder, edgeLine = edge, barriers = e.type == "motorway" || (dual && e.type == "trunk" && lanes >= 2) };
        }

        static N ReadNode(Dictionary<string, object> o, N into = null)
        {
            var n = into ?? new N();
            if (o.ContainsKey("id")) n.id = (long)MiniJson.D(o["id"]);
            if (o.ContainsKey("x")) n.x = MiniJson.D(o["x"]);
            if (o.ContainsKey("y")) n.y = MiniJson.D(o["y"]);
            if (o.ContainsKey("z")) n.z = MiniJson.D(o["z"]);
            if (o.ContainsKey("kind")) n.kind = o["kind"] as string ?? "";
            if (o.ContainsKey("roundaboutRadius")) n.rbR = MiniJson.D(o["roundaboutRadius"]);
            return n;
        }
        static double Num(Dictionary<string, object> o, string k, double cur) => o.ContainsKey(k) ? (o[k] == null ? double.NaN : MiniJson.D(o[k])) : cur;
        static E ReadEdge(Dictionary<string, object> o, E into)
        {
            var e = into ?? new E();
            if (o.ContainsKey("id")) e.id = (long)MiniJson.D(o["id"]);
            if (o.ContainsKey("a")) e.a = (long)MiniJson.D(o["a"]);
            if (o.ContainsKey("b")) e.b = (long)MiniJson.D(o["b"]);
            if (o.ContainsKey("oneway")) e.oneway = o["oneway"] is bool b && b;
            if (o.ContainsKey("roundabout")) e.roundabout = o["roundabout"] is bool rb && rb;
            if (o.ContainsKey("ortho")) e.ortho = o["ortho"] != null && !(o["ortho"] is bool ob && !ob);
            if (o.ContainsKey("dual")) e.dual = o["dual"] is bool d ? d : (bool?)null;
            if (o.ContainsKey("lanes")) e.lanes = o["lanes"] == null ? 0 : (int)Math.Round(MiniJson.D(o["lanes"]));
            if (o.ContainsKey("type")) e.type = o["type"] as string ?? "";
            if (o.ContainsKey("ref")) e.rf = o["ref"] as string ?? "";
            if (o.ContainsKey("name")) e.name = o["name"] as string ?? "";
            if (o.ContainsKey("spur")) e.spur = o["spur"] as string;
            if (o.ContainsKey("verge")) e.verge = o["verge"] as string;
            if (o.ContainsKey("roadProfile")) e.profile = o["roadProfile"] as string;
            if (o.ContainsKey("closed")) e.closedOwn = o["closed"] is bool cl ? cl : (bool?)null;
            e.laneW = Num(o, "laneW", e.laneW); e.shoulderW = Num(o, "shoulderW", e.shoulderW); e.innerStripW = Num(o, "innerStripW", e.innerStripW); e.medianWidth = Num(o, "medianWidth", e.medianWidth);
            if (o.ContainsKey("spans"))
            {
                e.spans = new List<(string, double, double)>();
                foreach (var so in MiniJson.L(o["spans"]) ?? new List<object>()) { var s = MiniJson.O(so); e.spans.Add((s["kind"] as string, MiniJson.D(s["from"]), MiniJson.D(s["to"]))); }
            }
            if (o.ContainsKey("points"))
            {
                e.pts = new List<double[]>();
                foreach (var p in MiniJson.L(o["points"])) { var q = MiniJson.L(p); e.pts.Add(new[] { MiniJson.D(q[0]), MiniJson.D(q[1]), MiniJson.D(q[2]) }); }
            }
            return e;
        }
        static double Sq(double v) => v * v;

        /// GameDataBuilder.Apply: one override entry atomically (junctionOverrides.ts), false = ignored
        static bool Apply(Dictionary<string, object> entry, Dictionary<long, N> ns, Dictionary<long, E> es)
        {
            var at = MiniJson.L(MiniJson.G(entry, "at")); var ov = MiniJson.O(MiniJson.G(entry, "overrides"));
            if (ov == null) return false;
            N anchor = null; string id = (string)entry["id"];
            if (id.StartsWith("graph-node-") && long.TryParse(id.Substring(11).Split('#')[0], out long nid)) ns.TryGetValue(nid, out anchor);
            if (anchor == null && at != null)
            {
                double ax = MiniJson.D(at[0]), az = MiniJson.D(at[1]), bd = 25;
                foreach (var n in ns.Values) { double d = Sq(n.x - ax) + Sq(n.z - az); if (d < bd) { bd = d; anchor = n; } }
            }
            if (anchor == null) return false;
            if (MiniJson.O(MiniJson.G(entry, "signature")) is Dictionary<string, object> sig && sig.ContainsKey("arms"))
            {
                int arms = 0; foreach (var e in es.Values) if (e.a == anchor.id || e.b == anchor.id) arms++;
                if (arms != (int)MiniJson.D(sig["arms"])) return false;
            }
            List<object> Lst(string k) => MiniJson.L(MiniJson.G(ov, k)) ?? new List<object>();
            foreach (var o in Lst("nodes")) if (!ns.ContainsKey((long)MiniJson.D(MiniJson.O(o)["id"]))) return false;
            foreach (var o in Lst("edges")) if (!es.ContainsKey((long)MiniJson.D(MiniJson.O(o)["id"]))) return false;
            foreach (var o in Lst("removeEdges")) if (!es.ContainsKey((long)MiniJson.D(o))) return false;
            foreach (var o in Lst("addNodes")) if (ns.ContainsKey((long)MiniJson.D(MiniJson.O(o)["id"]))) return false;
            foreach (var o in Lst("addEdges")) if (es.ContainsKey((long)MiniJson.D(MiniJson.O(o)["id"]))) return false;
            foreach (var o in Lst("nodes")) { var d = MiniJson.O(o); ReadNode(d, ns[(long)MiniJson.D(d["id"])]); }
            foreach (var o in Lst("addNodes")) { var n = ReadNode(MiniJson.O(o)); ns[n.id] = n; }
            foreach (var o in Lst("edges")) { var d = MiniJson.O(o); ReadEdge(d, es[(long)MiniJson.D(d["id"])]); }
            foreach (var o in Lst("addEdges")) { var e = ReadEdge(MiniJson.O(o), null); es[e.id] = e; }
            foreach (var o in Lst("removeEdges")) es.Remove((long)MiniJson.D(o));
            return true;
        }

        static string LoadGraph()
        {
            var g = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "graph.json")));
            var ns = new Dictionary<long, N>(); var es = new Dictionary<long, E>();
            foreach (var o in MiniJson.L(g["nodes"])) { var n = ReadNode(MiniJson.O(o)); ns[n.id] = n; }
            foreach (var o in MiniJson.L(g["edges"])) { var e = ReadEdge(MiniJson.O(o), null); es[e.id] = e; }
            var files = new List<string> { "junction-overrides.json" };
            var ix = W.REGION + "junction-overrides.index.json";
            if (File.Exists(ix)) foreach (var f in MiniJson.L(MiniJson.Parse(File.ReadAllText(ix)))) files.Add((string)f);
            var order = new List<string>(); var byId = new Dictionary<string, Dictionary<string, object>>();
            foreach (var f in files)
            {
                var path = W.REGION + f; if (!File.Exists(path)) continue;
                foreach (var eo in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(path)))["overrides"])) { var en = MiniJson.O(eo); var id = (string)en["id"]; if (!byId.ContainsKey(id)) order.Add(id); byId[id] = en; }
            }
            int ok = 0; foreach (var id in order) if (Apply(byId[id], ns, es)) ok++;
            OutsideStreets.DropSuperseded(ns, es, e => e.a, e => e.b); // (a package street replaces them: OutsideStreets.Superseded)
            var closed = new HashSet<long>();
            foreach (var c in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "road-closures.json")))["closedEdgeIds"])) closed.Add((long)MiniJson.D(c));

            // Same s11 producer correction as the GPS graph: do not place a works barrier on
            // the short Lindlarska arm that continues openly towards the warehouse.
            if (!closed.Contains(GameDataBuilder.LindlarskaArm) && es.TryGetValue(GameDataBuilder.LindlarskaArm, out var arm)
                && es.TryGetValue(GameDataBuilder.LindlarskaContinuation, out var continuation)
                && GameDataBuilder.IsWarehouseLink(arm.b, arm.name, arm.oneway, continuation.a, continuation.name, continuation.oneway, continuation.closedOwn))
                arm.closedOwn = false;

            nodes = ns.Values.ToList(); var ids = new HashSet<long>(ns.Keys);
            edges = es.Values.Where(e => e.pts != null && e.pts.Count >= 2 && ids.Contains(e.a) && ids.Contains(e.b)).ToList();
            int fitted = 0; double fitMax = 0, relaxMax = 0;
            var ny = ns.ToDictionary(kv => kv.Key, kv => kv.Value.y);
            int relaxed = RelaxNearModels(ny, edges.Select(e => (e.a, e.b, PlanLen(e.pts))).ToList(), ref relaxMax);
            foreach (var kv in ny) ns[kv.Key].y = kv.Value;
            foreach (var e in edges) fitted += FitEndHeights(e.pts, ns[e.a].y, ns[e.b].y, ref fitMax);
            // (graphIndex.ts setRoadClosures: an edge's own `closed` wins over the closures file)
            foreach (var e in edges) e.closed = e.closedOwn ?? closed.Contains(e.id);
            fileClosed = closed;
            Index();
            return $"graph {nodes.Count} nodes, {edges.Count} edges ({ok}/{order.Count} override entries), {closed.Count} closed ids, {fitted} edge ends brought to their node's height (max {fitMax:F2} m; {relaxed} nodes beside models eased, max {relaxMax:F2} m)";
        }
        public static double PlanLen(List<double[]> p)
        {
            double L = 0; for (int i = 1; i < p.Count; i++) L += Math.Sqrt(Sq(p[i][0] - p[i - 1][0]) + Sq(p[i][2] - p[i - 1][2])); return L;
        }
        /// an edge whose end is not at its node's height (mag-11-20's −7000000084514 starts 0.53 m over node 785, where the warehouse
        /// spur and Bardejovska stay: a wall between the junction disc and the ribbon at the Lukšić warehouse exit): the difference
        /// faded out linearly along the edge (≤ 2 % extra grade, at most the whole edge); the node — shared by the other arms — wins.
        /// Differences over 3 m are left (a level crossing that is not one). Also GameDataBuilder (the GPS / traffic graph).
        public static int FitEndHeights(List<double[]> pts, double yA, double yB, ref double maxD)
        {
            if (pts == null || pts.Count < 2) return 0;
            var cum = new double[pts.Count];
            for (int i = 1; i < pts.Count; i++) cum[i] = cum[i - 1] + Math.Sqrt(Sq(pts[i][0] - pts[i - 1][0]) + Sq(pts[i][2] - pts[i - 1][2]));
            double L = cum[pts.Count - 1]; if (L < 1) return 0;
            int n = 0;
            foreach (bool atA in new[] { true, false })
            {
                double d = (atA ? yA : yB) - pts[atA ? 0 : pts.Count - 1][1];
                if (Math.Abs(d) < .03 || Math.Abs(d) > 3) continue;
                double F = Math.Min(L, Math.Max(10, Math.Abs(d) / .02));
                for (int i = 0; i < pts.Count; i++)
                {
                    double s = atA ? cum[i] : L - cum[i]; if (s >= F) continue;
                    var p = (double[])pts[i].Clone(); p[1] += d * (1 - s / F); pts[i] = p;
                }
                n++; maxD = Math.Max(maxD, Math.Abs(d));
            }
            return n;
        }
        /// ceste-pregled 2026-11-23: a street node next to a model's arm end at another height (the graph's OSM / DEM node vs the model's
        /// own arm: Sućurac 02 Fulgencija Careva 44.5 vs 41.0 m over 16 m, 47 Zagorski put, 43) made a short ramp of 15–25 % between the
        /// model and the street. The model nodes (ids −7e12…−8e12, the importer's) are pinned; the free nodes within two hops of them are
        /// moved (≤ 3 m) so no edge between them is steeper than G — the difference spread over the street instead of one short edge.
        /// `y` in / out (node heights), `es` the edges (a, b, plan length). Also GameDataBuilder (the GPS / traffic graph), before FitEndHeights.
        public static int RelaxNearModels(Dictionary<long, double> y, List<(long a, long b, double len)> es, ref double maxD, double G = .09)
        {
            static bool Model(long id) => id <= -7_000_000_000_000 && id > -8_000_000_000_000;
            var adj = new Dictionary<long, List<int>>();
            for (int i = 0; i < es.Count; i++) foreach (var n in new[] { es[i].a, es[i].b }) { if (!adj.TryGetValue(n, out var l)) adj[n] = l = new List<int>(); l.Add(i); }
            // free: not a model node, ≤ 2 hops from one
            var hop = new Dictionary<long, int>(); var q = new Queue<long>();
            foreach (var id in y.Keys) if (Model(id) && adj.ContainsKey(id)) { hop[id] = 0; q.Enqueue(id); }
            while (q.Count > 0)
            {
                var n = q.Dequeue(); if (hop[n] >= 2) continue;
                foreach (int i in adj[n]) { long o = es[i].a == n ? es[i].b : es[i].a; if (!hop.ContainsKey(o) && y.ContainsKey(o)) { hop[o] = hop[n] + 1; q.Enqueue(o); } }
            }
            var free = new HashSet<long>(hop.Where(kv => kv.Value > 0 && !Model(kv.Key)).Select(kv => kv.Key));
            var y0 = free.ToDictionary(n => n, n => y[n]);
            var act = new List<int>(); for (int i = 0; i < es.Count; i++) if ((free.Contains(es[i].a) || free.Contains(es[i].b)) && hop.ContainsKey(es[i].a) && hop.ContainsKey(es[i].b) && es[i].len > .5) act.Add(i);
            for (int it = 0; it < 60; it++)
            {
                bool any = false;
                foreach (int i in act)
                {
                    var (a, b, len) = es[i]; double d = y[b] - y[a], lim = G * len; if (Math.Abs(d) <= lim + 1e-3) continue;
                    // an edge over 3 m off its limit is a level difference the graph means (a bridge end, steps): left alone
                    if (Math.Abs(d) - lim > 3.5) continue;
                    double ex = (Math.Abs(d) - lim) * Math.Sign(d); bool fa = free.Contains(a), fb = free.Contains(b);
                    if (fa && fb) { y[a] += ex / 2; y[b] -= ex / 2; } else if (fa) y[a] += ex; else if (fb) y[b] -= ex; else continue;
                    any = true;
                }
                foreach (var n in free) y[n] = Math.Max(y0[n] - 3, Math.Min(y0[n] + 3, y[n]));
                if (!any) break;
            }
            int moved = 0; foreach (var n in free) { double d = Math.Abs(y[n] - y0[n]); if (d > .03) { moved++; maxD = Math.Max(maxD, d); } }
            return moved;
        }
        /// node / edge indices, segment grid, lengths, road specs, the D8 one-way repair and the hidden ramps (after any change of nodes / edges)
        static void Index()
        {
            nIdx = new Dictionary<long, int>(); for (int i = 0; i < nodes.Count; i++) nIdx[nodes[i].id] = i;
            eIdx = new Dictionary<long, int>(); adj = new List<int>[nodes.Count]; for (int i = 0; i < adj.Length; i++) adj[i] = new List<int>();
            grid = new Dictionary<long, List<(int, int)>>();
            for (int ei = 0; ei < edges.Count; ei++)
            {
                var e = edges[ei]; eIdx[e.id] = ei;
                e.cum = new double[e.pts.Count];
                for (int i = 1; i < e.pts.Count; i++) { var p = e.pts[i]; var q = e.pts[i - 1]; e.cum[i] = e.cum[i - 1] + Math.Sqrt(Sq(p[0] - q[0]) + Sq(p[1] - q[1]) + Sq(p[2] - q[2])); }
                adj[nIdx[e.a]].Add(ei); adj[nIdx[e.b]].Add(ei);
                for (int i = 0; i + 1 < e.pts.Count; i++)
                {
                    var p = e.pts[i]; var q = e.pts[i + 1];
                    int x0 = (int)Math.Floor(Math.Min(p[0], q[0]) / GC), x1 = (int)Math.Floor(Math.Max(p[0], q[0]) / GC), z0 = (int)Math.Floor(Math.Min(p[2], q[2]) / GC), z1 = (int)Math.Floor(Math.Max(p[2], q[2]) / GC);
                    for (int cz = z0; cz <= z1; cz++) for (int cx = x0; cx <= x1; cx++) { long k = Key(cx, cz); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<(int, int)>(); l.Add((ei, i)); }
                }
            }
            // graphIndex.ts: D8 edges that lost oneway in the producer's contraction (an opposing parallel D8 carriageway beside them)
            foreach (var e in edges) e.sp = RoadSpec(e);
            for (int ei = 0; ei < edges.Count; ei++)
            {
                var e = edges[ei]; if (!IsD8(e) || e.oneway || e.dual == true) continue;
                int paired = 0, lateral = 0;
                foreach (var f in new[] { .2, .35, .5, .65, .8 })
                {
                    var p = PointAt(ei, e.L * f);
                    var h = Nearest(p.x, p.z, 60, k => k != ei && IsD8(edges[k]) && edges[k].oneway && !(edges[k].a == e.a || edges[k].a == e.b || edges[k].b == e.a || edges[k].b == e.b));
                    if (h.ei >= 0 && Math.Abs((h.x - p.x) * p.tx + (h.z - p.z) * p.tz) >= h.dist * .5) h.ei = -1;
                    if (h.ei >= 0 && h.dist > 6 && Math.Abs(h.y - p.y) < 4) { lateral++; if (Math.Abs(h.tx * p.tx + h.tz * p.tz) > .94) paired++; }
                }
                if (paired >= 4 || (paired >= 3 && paired == lateral)) { e.oneway = true; e.dual = false; e.sp = RoadSpec(e); }
            }
            hidden = new HashSet<int>();
            for (int ei = 0; ei < edges.Count; ei++)
            {
                var e = edges[ei];
                if (e.type == "ramp" && !e.oneway && e.L > 120 && edges.Any(o => IsD8(o) && ((o.a == e.a && o.b == e.b) || (o.b == e.a && o.a == e.b)) && o.L / e.L > .9 && o.L / e.L < 1.1)) hidden.Add(ei);
            }
        }

        // ------------------------------------------------------------------ streets outside the models (OutsideStreets: ceste_izvan_modela.json)
        /// the graph as OutsideStreets sees it (after LoadGraph + LoadModels); splittable = an edge no model / closure / span refers to
        static List<OutsideStreets.GEdgeV> OutsideView(out List<OutsideStreets.GNode> gn)
        {
            var refd = new HashSet<long>(); foreach (var m in models) { refd.UnionWith(m.edges); foreach (var a in m.arms) refd.Add(a.edge); }
            gn = nodes.Select((n, i) => new OutsideStreets.GNode { id = n.id, x = n.x, y = n.y, z = n.z, deg = adj[i].Count }).ToList();
            var gv = new List<OutsideStreets.GEdgeV>();
            for (int ei = 0; ei < edges.Count; ei++)
            {
                var e = edges[ei]; if (hidden.Contains(ei) || e.spans.Any(s => s.k == "tunnel")) continue;
                bool nearModel = e.pts.Any(p => ModelAt(p[0], p[2], 20) != null);
                gv.Add(new OutsideStreets.GEdgeV
                {
                    id = e.id, a = e.a, b = e.b, pts = e.pts, hw = e.sp.halfWidth,
                    splittable = !refd.Contains(e.id) && !nearModel && !e.closed && e.spans.Count == 0 && !IsD8(e) && e.spur == null && !e.roundabout && e.type != "ramp" && e.type != "motorway",
                });
            }
            return gv;
        }
        /// the network of the streets outside the models for this graph (GameDataBuilder routes on the same one)
        public static OutsideStreets.Net OutsideNet()
        {
            LoadGraph(); LoadModels();
            var gv = OutsideView(out var gn);
            return OutsideStreets.Compute(gn, gv, models.Select(m => m.j).ToList(), AX0, AX1, AZ0, AZ1);
        }
        /// split polyline `pts` at the point nearest (x, z) (2D): (before, after), both containing the split point
        public static (List<double[]>, List<double[]>) SplitAt(List<double[]> pts, double x, double y, double z)
        {
            int bi = 0; double bd = double.MaxValue, bt = 0;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var p = pts[i]; var q = pts[i + 1]; double bx = q[0] - p[0], bz = q[2] - p[2], l2 = bx * bx + bz * bz;
                double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((x - p[0]) * bx + (z - p[2]) * bz) / l2));
                double d = Sq(x - p[0] - bx * t) + Sq(z - p[2] - bz * t); if (d < bd) { bd = d; bi = i; bt = t; }
            }
            var s = new[] { x, y, z };
            var A = pts.GetRange(0, bi + 1); if (bt > 1e-3) A.Add(s); else A[A.Count - 1] = s;
            var B = new List<double[]> { s }; B.AddRange(pts.GetRange(bi + 1, pts.Count - bi - 1)); if (bt >= 1 - 1e-3 && B.Count > 2) B.RemoveAt(1);
            return (A, B);
        }
        static string ApplyOutside()
        {
            var gv = OutsideView(out var gn);
            var net = OutsideStreets.Compute(gn, gv, models.Select(m => m.j).ToList(), AX0, AX1, AZ0, AZ1);
            foreach (var nn in net.nodes) nodes.Add(new N { id = nn.id, x = nn.x, y = nn.y, z = nn.z, kind = "junction" });
            long sid = OutsideStreets.SPLIT0;
            foreach (var sp in net.splits)
            {
                if (!sp.render) continue;
                var nn = net.nodes.First(q => q.id == sp.node); var e = edges.FirstOrDefault(q => q.id == sp.edge);
                if (e == null) continue;
                // (several splits on one edge: the part that holds the point)
                var part = edges.Where(q => q.id == sp.edge || q.splitOf == sp.edge).OrderBy(q => DistTo(q.pts, nn.x, nn.z)).First();
                var (A, B) = SplitAt(part.pts, nn.x, nn.y, nn.z);
                var e2 = part.Clone(); e2.id = sid++; e2.splitOf = sp.edge; e2.a = nn.id; e2.pts = B;
                part.b = nn.id; part.pts = A; edges.Add(e2);
            }
            foreach (var st in net.streets)
            {
                // sidewalk candidates: the outside streets' own rule (≥ 5.5 m, not service / access / links) or a village street of
                // ≥ 5 m (tertiary / unclassified / residential); BuildEdge keeps them only on built-up land, never on trunk roads / the D8
                bool walk = st.cls != "trunk" && st.cls != "motorway" && (st.sidewalk || st.width >= 5 && (st.cls == "tertiary" || st.cls == "unclassified" || st.cls == "residential" || st.cls == "living_street"));
                var e = new E
                {
                    id = st.id, a = st.a, b = st.b, oneway = st.oneway, lanes = st.lanes, name = st.name ?? "",
                    type = st.cls == "primary" || st.cls == "secondary" || st.cls == "tertiary" ? st.cls : "residential", pts = st.pts,
                    shoulderW = .25, verge = walk ? "urban_kerb" : null, profile = walk ? "urban_local" : null, dual = false,
                    outside = true, trimA = st.trimA, trimB = st.trimB, mA = st.modelA, mB = st.modelB, mhwA = st.mhwA, mhwB = st.mhwB, noPaint = !st.paint,
                };
                e.laneW = Math.Max(1.6, st.oneway ? (st.width - .5) / st.lanes : (st.width / 2 - .25) / st.lanes);
                // a T onto a road that is not split: the drawn street stops 5 cm onto that road's asphalt, 1.5 cm under it (the
                // polyline itself is cut, so the far side's sidewalk is not taken for blocked); the GPS edge keeps the full line
                e.pts = new List<double[]>(st.pts);
                if (st.trimA > 0) { e.pts = CutStart(e.pts, st.trimA + .1); e.pts[0][1] -= .015; e.trimA = 0; }
                if (st.trimB > 0) { e.pts.Reverse(); e.pts = CutStart(e.pts, st.trimB + .1); e.pts[0][1] -= .015; e.pts.Reverse(); e.trimB = 0; }
                edges.Add(e);
            }
            Index();
            foreach (var l in net.log.Skip(1)) Debug.Log("[Magistrala] " + l);
            return net.log.Count > 0 ? net.log[0] : "outside streets: none";
        }
        /// the polyline without its first `d` metres (2D), the cut point interpolated (copies of the points)
        static List<double[]> CutStart(List<double[]> pts, double d)
        {
            double acc = 0;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i]; double l = Math.Sqrt(Sq(b[0] - a[0]) + Sq(b[2] - a[2]));
                if (acc + l > d && pts.Count - i >= 1)
                {
                    double t = (d - acc) / Math.Max(1e-9, l);
                    var o = new List<double[]> { new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t } };
                    for (int k = i; k < pts.Count; k++) o.Add((double[])pts[k].Clone());
                    return o.Count >= 2 ? o : pts;
                }
                acc += l;
            }
            return pts;
        }
        public static double DistTo(List<double[]> pts, double x, double z)
        {
            double bd = double.MaxValue;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var p = pts[i]; var q = pts[i + 1]; double bx = q[0] - p[0], bz = q[2] - p[2], l2 = bx * bx + bz * bz;
                double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((x - p[0]) * bx + (z - p[2]) * bz) / l2));
                bd = Math.Min(bd, Sq(x - p[0] - bx * t) + Sq(z - p[2] - bz * t));
            }
            return bd;
        }
        static long Key(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;

        struct P { public double x, y, z, tx, tz; }
        static P PointAt(int ei, double s)
        {
            var e = edges[ei]; var c = e.cum; s = Math.Max(0, Math.Min(c[c.Length - 1], s));
            int lo = 0, hi = c.Length - 1;
            while (hi - lo > 1) { int m = (lo + hi) >> 1; if (c[m] <= s) lo = m; else hi = m; }
            var p = e.pts[lo]; var q = e.pts[hi]; double t = (s - c[lo]) / Math.Max(1e-9, c[hi] - c[lo]);
            double bx = q[0] - p[0], bz = q[2] - p[2], l = Math.Sqrt(bx * bx + bz * bz); if (l < 1e-9) l = 1;
            return new P { x = p[0] + bx * t, y = p[1] + (q[1] - p[1]) * t, z = p[2] + bz * t, tx = bx / l, tz = bz / l };
        }
        struct Hit { public int ei; public double s, dist, x, y, z, tx, tz; }
        static Hit Nearest(double x, double z, double maxD, Func<int, bool> filt = null)
        {
            var best = new Hit { ei = -1, dist = maxD };
            int x0 = (int)Math.Floor((x - maxD) / GC), x1 = (int)Math.Floor((x + maxD) / GC), z0 = (int)Math.Floor((z - maxD) / GC), z1 = (int)Math.Floor((z + maxD) / GC);
            for (int cz = z0; cz <= z1; cz++)
                for (int cx = x0; cx <= x1; cx++)
                {
                    if (!grid.TryGetValue(Key(cx, cz), out var l)) continue;
                    foreach (var (ei, i) in l)
                    {
                        if (filt != null && !filt(ei)) continue;
                        var e = edges[ei]; var p = e.pts[i]; var q = e.pts[i + 1];
                        double bx = q[0] - p[0], bz = q[2] - p[2], l2 = bx * bx + bz * bz;
                        double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((x - p[0]) * bx + (z - p[2]) * bz) / l2));
                        double px = p[0] + bx * t, pz = p[2] + bz * t, d = Math.Sqrt(Sq(x - px) + Sq(z - pz));
                        if (d >= best.dist) continue;
                        double ll = Math.Sqrt(l2); if (ll < 1e-9) ll = 1;
                        best = new Hit { ei = ei, s = e.cum[i] + (e.cum[i + 1] - e.cum[i]) * t, dist = d, x = px, y = p[1] + (q[1] - p[1]) * t, z = pz, tx = bx / ll, tz = bz / ll };
                    }
                }
            return best;
        }
        static int Roundabout(long nodeId)
        {
            if (!nIdx.TryGetValue(nodeId, out int i)) return 0;
            if (adj[i].Any(k => edges[k].roundabout)) return 0;
            return (int)Math.Round(nodes[i].rbR * 1000) == 0 ? 0 : 1;
        }
        static double RbR(long nodeId) => Roundabout(nodeId) > 0 ? nodes[nIdx[nodeId]].rbR : 0;
        static bool DuplicateRibbon(int ei, double s)
        {
            if (hidden.Contains(ei)) return true;
            var e = edges[ei]; if (s < 15 || e.L - s < 15) return false;
            var p = PointAt(ei, s);
            var h = Nearest(p.x, p.z, 6, k => { if (k == ei) return false; var o = edges[k]; return IsD8(o) && (e.type == "ramp" || IsD8(e) && o.id < e.id) && (e.oneway == o.oneway || e.type == "ramp"); });
            if (h.ei < 0) return false;
            double dot = p.tx * h.tx + p.tz * h.tz;
            return h.s > 15 && edges[h.ei].L - h.s > 15 && Math.Abs(h.y - p.y) < 1.5 && (e.oneway ? dot > .97 : Math.Abs(dot) > .97);
        }

        // ------------------------------------------------------------------ models (model-junctions.json: the fields WorldData skips)
        class Arm { public long edge; public bool outA; public double[] cut, left, right; }
        class MX
        {
            public ModelJ j; public bool footprint, furniture; public HashSet<long> edges = new HashSet<long>(); public HashSet<long> nodes = new HashSet<long>();
            public List<Arm> arms = new List<Arm>();
            public List<(List<double[]> o, List<(List<double[]> p, bool isl)> h)> outline = new List<(List<double[]>, List<(List<double[]>, bool)>)>();
            public List<double[]> paveEnds = new List<double[]>();
        }
        static List<MX> models;
        static List<double[]> Ring(object o) { var r = new List<double[]>(); foreach (var p in MiniJson.L(o)) { var q = MiniJson.L(p); r.Add(new[] { MiniJson.D(q[0]), MiniJson.D(q[1]) }); } return r; }
        static double[] V3(object o) { var q = MiniJson.L(o); return new[] { MiniJson.D(q[0]), MiniJson.D(q[1]), MiniJson.D(q[2]) }; }
        static void LoadModels()
        {
            var all = WorldData.Models(-1e9, 1e9).ToDictionary(m => m.key);
            models = new List<MX>();
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "model-junctions.json")));
            foreach (var jo in MiniJson.L(root["junctions"]))
            {
                var j = MiniJson.O(jo); var m = new MX { j = all[(string)j["key"]] };
                m.footprint = MiniJson.G(j, "owns") as string == "footprint"; m.furniture = MiniJson.G(j, "furniture") is bool f && f;
                foreach (var e in MiniJson.L(MiniJson.G(j, "edges")) ?? new List<object>()) m.edges.Add((long)MiniJson.D(e));
                foreach (var e in MiniJson.L(MiniJson.G(j, "nodes")) ?? new List<object>()) m.nodes.Add((long)MiniJson.D(e));
                foreach (var ao in MiniJson.L(MiniJson.G(j, "arms")) ?? new List<object>())
                {
                    var a = MiniJson.O(ao);
                    m.arms.Add(new Arm { edge = (long)MiniJson.D(a["edge"]), outA = (string)a["outside"] == "a", cut = V3(a["cut"]), left = V3(a["left"]), right = V3(a["right"]) });
                }
                foreach (var oo in MiniJson.L(MiniJson.G(j, "outline")) ?? new List<object>())
                {
                    var o = MiniJson.O(oo); var hs = new List<(List<double[]>, bool)>();
                    foreach (var ho in MiniJson.L(MiniJson.G(o, "h")) ?? new List<object>()) { var h = MiniJson.O(ho); hs.Add((Ring(h["p"]), h.ContainsKey("isl") && (h["isl"] is bool ib ? ib : MiniJson.D(h["isl"]) != 0))); }
                    m.outline.Add((Ring(o["o"]), hs));
                }
                foreach (var po in MiniJson.L(MiniJson.G(j, "paveEnds")) ?? new List<object>()) { var p = MiniJson.O(po); m.paveEnds.Add(new[] { MiniJson.D(p["x"]), MiniJson.D(p["y"]), MiniJson.D(p["z"]), MiniJson.D(p["a"]), MiniJson.D(p["w"]) }); }
                models.Add(m);
            }
        }
        static int Cell(ModelJ m, double x, double z) => m.grid.Cell(x, z);
        static MX ModelAt(double x, double z, double margin = 0)
        {
            foreach (var m in models) if (Math.Abs(x - m.j.x) <= m.j.half + margin && Math.Abs(z - m.j.z) <= m.j.half + margin) return m;
            return null;
        }
        const double OWN_MARGIN = 4;
        static MX ModelOwnerAt(double x, double z)
        {
            var m = ModelAt(x, z); if (m == null || !m.footprint) return m;
            int c = Cell(m.j, x, z); return c >= 0 && m.j.grid.foot[c] <= OWN_MARGIN ? m : null;
        }
        static bool InRing(List<double[]> r, double x, double z)
        {
            bool hit = false;
            for (int i = 0, k = r.Count - 1; i < r.Count; k = i++) { var a = r[i]; var b = r[k]; if ((a[1] > z) != (b[1] > z) && x < (b[0] - a[0]) * (z - a[1]) / (b[1] - a[1]) + a[0]) hit = !hit; }
            return hit;
        }
        static double RingDist(List<double[]> r, double x, double z)
        {
            double best = double.PositiveInfinity;
            for (int i = 0, k = r.Count - 1; i < r.Count; k = i++)
            {
                var a = r[k]; var b = r[i]; double dx = b[0] - a[0], dz = b[1] - a[1], L2 = dx * dx + dz * dz; if (L2 < 1e-9) L2 = 1e-9;
                double t = Math.Max(0, Math.Min(1, ((x - a[0]) * dx + (z - a[1]) * dz) / L2));
                best = Math.Min(best, Math.Sqrt(Sq(x - a[0] - dx * t) + Sq(z - a[1] - dz * t)));
            }
            return best;
        }
        /// modelSurfaceDist: 0 on a model's paved outline (asphalt + raised islands), else the distance to it
        static double SurfaceDist(IEnumerable<MX> list, double x, double z)
        {
            double best = double.PositiveInfinity;
            foreach (var m in list)
            {
                if (Math.Abs(x - m.j.x) > m.j.half + 12 || Math.Abs(z - m.j.z) > m.j.half + 12 || m.outline.Count == 0) continue;
                foreach (var a in m.outline)
                {
                    bool on = InRing(a.o, x, z) && !a.h.Any(h => !h.isl && InRing(h.p, x, z)) || a.h.Any(h => h.isl && InRing(h.p, x, z));
                    if (on) return 0;
                    best = Math.Min(best, RingDist(a.o, x, z)); foreach (var h in a.h) best = Math.Min(best, RingDist(h.p, x, z));
                }
            }
            return best;
        }
        static bool OwnedByModel(double x, double z)
        {
            var m = ModelOwnerAt(x, z); if (m == null) return false;
            if (!m.footprint) { int c = Cell(m.j, x, z); if (c >= 0 && m.j.grid.foot[c] > 1.0) return false; }
            foreach (var a in m.arms)
            {
                double ox = -(a.right[2] - a.left[2]), oz = a.right[0] - a.left[0], l = Math.Sqrt(ox * ox + oz * oz); if (l < 1e-9) l = 1;
                ox /= l; oz /= l;
                if (ox * (a.cut[0] - m.j.x) + oz * (a.cut[2] - m.j.z) < 0) { ox = -ox; oz = -oz; }
                double dx = x - a.cut[0], dz = z - a.cut[2];
                if (dx * ox + dz * oz > -0.05 && Math.Abs(-dx * oz + dz * ox) < 15) return false;
            }
            return true;
        }

        class Cut { public double k0, k1; public Arm arm; public double sCut = double.NaN; public MX model; public bool endSnap; }
        static Dictionary<int, Cut> cuts;
        static void ModelCuts()
        {
            cuts = new Dictionary<int, Cut>();
            foreach (var m in models)
            {
                var armOf = new Dictionary<long, Arm>(); foreach (var a in m.arms) armOf[a.edge] = a;
                double R = m.j.half; var seen = new HashSet<int>();
                for (int cz = (int)Math.Floor((m.j.z - R) / GC); cz <= (int)Math.Floor((m.j.z + R) / GC); cz++)
                    for (int cx = (int)Math.Floor((m.j.x - R) / GC); cx <= (int)Math.Floor((m.j.x + R) / GC); cx++)
                    {
                        if (!grid.TryGetValue(Key(cx, cz), out var l)) continue;
                        foreach (var (ei, i) in l)
                        {
                            var p = edges[ei].pts[i]; var q = edges[ei].pts[i + 1];
                            if (Math.Max(p[0], q[0]) < m.j.x - R || Math.Min(p[0], q[0]) > m.j.x + R || Math.Max(p[2], q[2]) < m.j.z - R || Math.Min(p[2], q[2]) > m.j.z + R) continue;
                            seen.Add(ei);
                        }
                    }
                foreach (int ei in seen)
                {
                    var e = edges[ei]; double L = e.L;
                    cuts.TryGetValue(ei, out var prev);
                    if (armOf.TryGetValue(e.id, out var arm))
                    {
                        var h = Nearest(arm.cut[0], arm.cut[2], 30, k => k == ei); if (h.ei < 0) continue;
                        double k0 = arm.outA ? 0 : h.s, k1 = arm.outA ? h.s : L;
                        if (prev?.arm != null) { prev.k0 = Math.Max(prev.k0, k0); prev.k1 = Math.Min(prev.k1, k1); continue; }
                        cuts[ei] = new Cut { k0 = k0, k1 = k1, arm = arm, sCut = h.s, model = m };
                    }
                    else if (prev?.arm == null && (!m.footprint || m.edges.Contains(e.id))) cuts[ei] = new Cut { k0 = 0, k1 = L, model = m };
                }
            }
            EndSnaps();
            EndHeightsToModels();
        }
        /// ceste-pregled 2: a street of ours leaving a model's paving (it starts at a model node; the model part is dropped) left it at the
        /// graph's height, not the paving's: d19 Pumpa Kaštela forecourt 0.67 m, 51, 53, 54. The difference at the paving's edge is faded
        /// out along the street (≥ 10 m, ≤ 3 % extra grade); differences over 2 m are left (another level).
        static int endHeights; static double endHeightMax;
        static void EndHeightsToModels()
        {
            endHeights = 0; endHeightMax = 0; var modelNodesAll = new HashSet<long>(models.SelectMany(m => m.nodes));
            foreach (var e in edges)
            {
                if (e.outside || e.sp == null || e.pts.Count < 2 || e.L < 30 || e.type == "ramp") continue;
                foreach (bool atA in new[] { true, false })
                {
                    long nid = atA ? e.a : e.b; if (!modelNodesAll.Contains(nid)) continue;
                    var P = e.pts; int n = P.Count; double[] cum = e.cum;
                    int Ix(int t) => atA ? t : n - 1 - t;
                    double S(int t) => atA ? cum[Ix(t)] : e.L - cum[Ix(t)];
                    // the first point off the paving, walking in from the model node
                    int t0 = -1; double surf = double.NaN;
                    for (int t = 0; t < n && S(t) < 60; t++)
                    {
                        var p = P[Ix(t)]; double sh = double.NaN;
                        foreach (var m in models) { sh = m.j.Surface(p[0], p[2]); if (!double.IsNaN(sh)) break; }
                        if (double.IsNaN(sh)) { t0 = t; break; }
                        surf = sh;
                    }
                    if (t0 <= 0 || double.IsNaN(surf)) continue;
                    double d = surf - P[Ix(t0 - 1)][1];
                    if (Math.Abs(d) < .05 || Math.Abs(d) > 2) continue;
                    double s0 = S(t0 - 1), F = Math.Max(10, Math.Abs(d) / .03);
                    for (int t = 0; t < n; t++)
                    {
                        double ds = S(t) - s0; if (ds >= F) break;
                        var q = (double[])P[Ix(t)].Clone(); q[1] += d * (ds <= 0 ? 1 : 1 - Smooth(ds / F)); P[Ix(t)] = q;
                    }
                    endHeights++; endHeightMax = Math.Max(endHeightMax, Math.Abs(d));
                }
            }
        }
        /// ceste-pregled 2026-11-23: a graph street that STARTS at a model node (the model's own edge is cut away whole, so no arm and no
        /// ArmSnap) met the model road's end square-on with its own width and direction: arrow / wedge ends and lips where the model
        /// road is wider or turned (c04 Vodovodna, 02 Fulgencija Careva, s04 Ponikvice, 20). Such an edge gets an arm made from the model
        /// road whose end is at the node (its end corners, model surface heights): the last 14 m are pulled onto it as at a cut arm.
        static int endSnaps;
        static void EndSnaps()
        {
            endSnaps = 0;
            for (int ei = 0; ei < edges.Count; ei++)
            {
                var e = edges[ei]; if (e.outside || e.sp == null || cuts.ContainsKey(ei) || e.L < 20) continue;
                foreach (bool atA in new[] { true, false })
                {
                    long nid = atA ? e.a : e.b; var m = models.FirstOrDefault(q => q.nodes.Contains(nid)); if (m == null) continue;
                    var np = atA ? e.pts[0] : e.pts[e.pts.Count - 1];
                    // the model road with an end at the node (≤ 4 m), drawn at ground level
                    double best = 4; double[] end = null, prevP = null; double mw = 0;
                    foreach (var r in m.j.roads)
                    {
                        if (r.lvl != 0 || r.p.Count < 2) continue;
                        foreach (bool first in new[] { true, false })
                        {
                            var q = first ? r.p[0] : r.p[r.p.Count - 1]; double d = Math.Sqrt(Sq(q[0] - np[0]) + Sq(q[1] - np[2]));
                            if (d < best) { best = d; end = q; prevP = first ? r.p[1] : r.p[r.p.Count - 2]; mw = r.w / 2; }
                        }
                    }
                    if (end == null || Math.Abs(mw - e.sp.halfWidth) > 3) continue;
                    // the model road must run on into ours (≤ 35° apart), else the node is a T on the model's road: nothing to snap to
                    double mx = end[0] - prevP[0], mz = end[1] - prevP[1], ml = Math.Sqrt(mx * mx + mz * mz); if (ml < 1e-6) continue; mx /= ml; mz /= ml;
                    var far = atA ? PointAt(ei, Math.Min(10, e.L)) : PointAt(ei, Math.Max(0, e.L - 10));
                    double ox = far.x - np[0], oz = far.z - np[2], ol = Math.Sqrt(ox * ox + oz * oz); if (ol < 1e-6) continue; ox /= ol; oz /= ol;
                    if (mx * ox + mz * oz < Math.Cos(35 * Math.PI / 180)) continue;
                    // corners across the model road's end, ordered by OUR frame at the node (ArmSnap maps u = −w → left, +w → right)
                    var at = PointAt(ei, atA ? 0 : e.L); double rx = -at.tz, rz = at.tx;
                    double px = -mz, pz = mx; if (px * rx + pz * rz < 0) { px = -px; pz = -pz; }
                    double Y(double x, double z) { double h = m.j.Surface(x, z); return double.IsNaN(h) ? np[1] : h; }
                    double lx = end[0] - px * mw, lz = end[1] - pz * mw, gx = end[0] + px * mw, gz = end[1] + pz * mw;
                    var arm = new Arm { edge = e.id, outA = !atA, cut = new[] { end[0], np[1], end[1] }, left = new[] { lx, Y(lx, lz), lz }, right = new[] { gx, Y(gx, gz), gz } };
                    // (one end only: an edge between two models keeps the first; a 20 m+ edge has room for one 14 m snap at each end, but Cut holds one)
                    cuts[ei] = new Cut { k0 = 0, k1 = e.L, arm = arm, sCut = atA ? 0 : e.L, model = m, endSnap = true }; endSnaps++;
                    Debug.Log(FormattableString.Invariant($"[endsnap] edge {e.id} '{e.name}' {(atA ? "A" : "B")} node {nid} {m.j.key}: our hw {e.sp.halfWidth:F1} model {mw:F1}, off {best:F1} m, angle {Math.Acos(Math.Min(1, mx * ox + mz * oz)) * 180 / Math.PI:F0}°, y node {np[1]:F2} L {arm.left[1]:F2} R {arm.right[1]:F2}"));
                    break;
                }
            }
        }
        static bool CutAway(Cut c, int ei, double s0, double s1)
        {
            if (c == null) return false;
            if (s1 <= c.k0 + 1e-6 || s0 >= c.k1 - 1e-6) return true;
            if (c.arm != null) return false;
            var p = PointAt(ei, (s0 + s1) / 2); var m = c.model;
            if (!(Math.Abs(p.x - m.j.x) < m.j.half && Math.Abs(p.z - m.j.z) < m.j.half)) return false;
            if (m.footprint) return true;
            var a = PointAt(ei, s0); var b = PointAt(ei, s1);
            return SurfaceDist(new[] { m }, a.x, a.z) < 1.0 && SurfaceDist(new[] { m }, b.x, b.z) < 1.0;
        }
        const double SNAP = 14;
        static double Smooth(double t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);
        struct Fr { public double x, y, z, rx, rz, s; }
        static double[] ArmSnap(Cut c, double s, double u, double w, Fr at)
        {
            if (c?.arm == null || double.IsNaN(c.sCut)) return new double[3];
            double d = Math.Abs(s - c.sCut); if (d >= SNAP) return new double[3];
            double k = Smooth(1 - d / SNAP); var lf = c.arm.left; var rt = c.arm.right;
            double f = Math.Max(0, Math.Min(1, (u + w) / (2 * w)));
            double tx = lf[0] + (rt[0] - lf[0]) * f, ty = lf[1] + (rt[1] - lf[1]) * f, tz = lf[2] + (rt[2] - lf[2]) * f;
            if (Math.Abs(u) > w) { double o = Math.Abs(u) - w, sd = Math.Sign(u); tx += at.rx * o * sd; tz += at.rz * o * sd; }
            double ox = at.x + at.rx * u, oz = at.z + at.rz * u;
            return new[] { (tx - ox) * k, (ty - at.y) * k, (tz - oz) * k };
        }

        // ------------------------------------------------------------------ output (game coordinates in, per chunk / material out)
        const int MAT_ASPHALT = 0, MAT_PAINT = 1, MAT_VERGE = 2, MAT_KERB = 3, MAT_WALK = 4, MAT_COURT = 5, MAT_BAY = 6, NMAT = 7;
        class Chunk
        {
            public List<Vector3>[] v = new List<Vector3>[NMAT]; public List<Vector2>[] u0 = new List<Vector2>[NMAT], u1 = new List<Vector2>[NMAT], u2 = new List<Vector2>[NMAT];
            public List<Vector3> cv = new List<Vector3>();
            public Chunk() { for (int k = 0; k < NMAT; k++) { v[k] = new List<Vector3>(); u0[k] = new List<Vector2>(); u1[k] = new List<Vector2>(); u2[k] = new List<Vector2>(); } }
        }
        static Dictionary<(int, int), Chunk> chunks;
        struct G { public double x, y, z; public Vector2 f1, f2; public G(double x, double y, double z) { this.x = x; this.y = y; this.z = z; f1 = f2 = Vector2.zero; } }
        enum Drop { Owned, OnModel, None }
        static int tris, dropped;
        static Chunk ChunkAt(Vector3 c) { var k = ((int)Math.Floor(c.x / CHUNK), (int)Math.Floor(c.z / CHUNK)); if (!chunks.TryGetValue(k, out var ch)) chunks[k] = ch = new Chunk(); return ch; }
        static bool Dropped(Drop d, double cx, double cz) => d == Drop.Owned ? OwnedByModel(cx, cz) : d == Drop.OnModel ? SurfaceDist(models, cx, cz) < 0.02 : false;

        /// triangle (game coords), wound to face `face` (Unity direction); mat < 0: collider only
        static void Tri(int mat, G a, G b, G c, Vector3 face, Drop drop, bool coll)
        {
            double cx = (a.x + b.x + c.x) / 3, cz = (a.z + b.z + c.z) / 3;
            if (Dropped(drop, cx, cz)) { dropped++; return; }
            Vector3 A = W.U(a.x, a.y, a.z), B = W.U(b.x, b.y, b.z), C = W.U(c.x, c.y, c.z);
            var n = Vector3.Cross(B - A, C - A);
            if (Vector3.Dot(n, face) < 0) { var t = B; B = C; C = t; var g = b; b = c; c = g; n = -n; }
            var ch = ChunkAt((A + B + C) / 3);
            if (mat >= 0)
            {
                bool top = Mathf.Abs(n.normalized.y) > .5f;
                foreach (var (P, g) in new[] { (A, a), (B, b), (C, c) })
                {
                    ch.v[mat].Add(P);
                    ch.u0[mat].Add(mat == MAT_ASPHALT || mat == MAT_COURT && courtAsphalt ? new Vector2(P.x, P.z) : top ? new Vector2(P.x, P.z) * (mat == MAT_PAINT || mat == MAT_BAY ? .2f : mat == MAT_COURT ? .12f : .25f) : new Vector2((P.x + P.z) * .5f, P.y) * .5f);
                    ch.u1[mat].Add(g.f1); ch.u2[mat].Add(g.f2);
                }
                tris++;
            }
            if (coll) { ch.cv.Add(A); ch.cv.Add(B); ch.cv.Add(C); }
        }
        static bool courtAsphalt;
        static void Quad(int mat, G a, G b, G c, G d, Vector3 face, Drop drop, bool coll) { Tri(mat, a, b, c, face, drop, coll); Tri(mat, a, c, d, face, drop, coll); }

        // ground pushed under the streets: capsules (x0 z0 y0 x1 z1 y1 reach) and courts
        static List<double[]> carve; static List<(double x, double z, double y, double fx, double fz, double hl, double hw)> courts;
        static void Carve(double x0, double z0, double y0, double x1, double z1, double y1, double reach) => carve.Add(new[] { x0, z0, y0, x1, z1, y1, reach });

        // ------------------------------------------------------------------ streets
        static bool InArea(double x, double z) => x >= AX0 && x < AX1 && z >= AZ0 && z < AZ1;

        static (bool through, double trim) JunctionEnd(int ei, bool atA)
        {
            var e = edges[ei]; long nid = atA ? e.a : e.b; int ni = nIdx[nid];
            double rb = RbR(nid); if (rb > 0) return (false, Math.Max(0, rb - 1));
            if (adj[ni].Count < 3) return (false, 0);
            double[] DirOut(int k)
            {
                var q = edges[k]; var Pp = q.pts; bool fromA = nIdx[q.a] == ni && (k != ei || atA);
                var p0 = fromA ? Pp[0] : Pp[Pp.Count - 1]; var p1 = fromA ? Pp[Math.Min(3, Pp.Count - 1)] : Pp[Math.Max(0, Pp.Count - 4)];
                double l = Math.Sqrt(Sq(p1[0] - p0[0]) + Sq(p1[2] - p0[2])); if (l < 1e-9) l = 1;
                return new[] { (p1[0] - p0[0]) / l, (p1[2] - p0[2]) / l };
            }
            var others = adj[ni].Where(k => k != ei && edges[k].spur == null).ToList();
            var me = DirOut(ei);
            if (others.Any(k => { var d = DirOut(k); return me[0] * d[0] + me[1] * d[1] < -0.9; })) return (true, 0);
            double trim = 0;
            for (int x = 0; x < others.Count; x++)
                for (int y = x + 1; y < others.Count; y++)
                {
                    var a = DirOut(others[x]); var b = DirOut(others[y]);
                    if (a[0] * b[0] + a[1] * b[1] < -0.9) trim = Math.Max(trim, Math.Min(edges[others[x]].sp.halfWidth, edges[others[y]].sp.halfWidth) - 0.3);
                }
            return (false, trim);
        }
        static double NodeR(long id)
        {
            double rb = RbR(id); if (rb > 0) return rb + 9;
            if (!nIdx.TryGetValue(id, out int ni) || adj[ni].Count < 3) return -1;
            double r = 0; foreach (var k in adj[ni]) r = Math.Max(r, edges[k].sp.halfWidth);
            return r + 3 + 9;
        }
        class Taper { public double hwN, mhN, Lt; public bool marks; }
        static HashSet<long> modelNodes;
        static Taper TaperAt(int ei, long id)
        {
            var e = edges[ei]; var sp = e.sp;
            if (!nIdx.TryGetValue(id, out int ni) || RbR(id) > 0 || modelNodes.Contains(id)) return null;
            var others = adj[ni].Where(k => k != ei && edges[k].spur == null).ToList();
            if (others.Count != 1 || adj[ni].Count(k => edges[k].spur == null) != 2) return null;
            var os = edges[others[0]].sp;
            double omh = os.oneway ? 0 : os.medianHalf, mh0 = sp.oneway ? 0 : sp.medianHalf, dw = Math.Abs(os.halfWidth - sp.halfWidth);
            bool changed = dw > 0.6 || Math.Abs(omh - mh0) > 0.3 || os.lanes != sp.lanes || os.oneway != sp.oneway;
            if (!changed) return null;
            return new Taper { hwN = (sp.halfWidth + os.halfWidth) / 2, mhN = Math.Min(mh0, omh), Lt = Math.Max(30, Math.Min(80, 8 * dw + 10 * Math.Abs(omh - mh0) + 20)), marks = os.lanes != sp.lanes || os.oneway != sp.oneway || Math.Abs(omh - mh0) > 0.3 };
        }

        /// ceste-pregled round 2: a street of ours that runs into a model's owned ground (square / footprint) and only meets the model's
        /// paving some metres further in lost that stretch to OwnedByModel: barrier on the model's stub, grass, then our street 5–15 m
        /// later (05 Suvača, s06, 04, 53, 54, c05, c08). Such an approach — owned ground from where the street enters it until the centre
        /// line first reaches the model's paving, ≤ 40 m — is drawn and dropped only on the paving itself (+ 2 m under it).
        static int approaches, approachSkipped;
        static List<(double a, double b)> Approach(int ei)
        {
            var e = edges[ei]; var r = new List<(double, double)>(); double L = e.L; int N = (int)Math.Ceiling(L);
            if (N < 2) return r;
            var owned = new bool[N + 1]; var pave = new bool[N + 1];
            for (int k = 0; k <= N; k++) { var p = PointAt(ei, Math.Min(L, k)); owned[k] = OwnedByModel(p.x, p.z); pave[k] = SurfaceDist(models, p.x, p.z) < 1.0; }
            // from the A side inwards and from the B side inwards
            foreach (bool fwd in new[] { true, false })
            {
                int Idx(int t) => fwd ? t : N - t;
                for (int t = 1; t <= N; t++)
                {
                    if (!owned[Idx(t)] || owned[Idx(t - 1)] || pave[Idx(t)]) continue;   // enters owned ground off the paving here
                    int u = t; while (u <= N && u - t <= 40 && owned[Idx(u)] && !pave[Idx(u)]) u++;
                    if (u > N || u - t > 40 || !pave[Idx(u)]) continue;                   // no paving within 40 m: the model owns it
                    double s0 = Math.Min(L, Idx(t)), s1 = Math.Min(L, Idx(u));
                    // the street's height brought to the paving it now reaches (05 Suvača met the stub 1.3 m low): faded out away from it
                    var pc = PointAt(ei, s1); double sy = double.NaN;
                    foreach (var m in models) { sy = m.j.Surface(pc.x, pc.z); if (!double.IsNaN(sy)) break; }
                    double dy = double.IsNaN(sy) ? 0 : sy - pc.y;
                    // round 2b: not where it made a new step — ramps (04), short connector edges (02 Fulgencija Careva, 22 m), and where
                    // the model's paving across our width is not level with its centre (45 Marina Studina: a 0.7 m seam at the T)
                    if (e.type == "ramp" || L < 30) { approachSkipped++; continue; }
                    double hwE = e.sp.halfWidth, cross = 0;
                    foreach (int sg in new[] { -1, 1 })
                    {
                        double ex = pc.x - pc.tz * hwE * sg, ez = pc.z + pc.tx * hwE * sg, ey = double.NaN;
                        foreach (var m in models) { ey = m.j.Surface(ex, ez); if (!double.IsNaN(ey)) break; }
                        if (!double.IsNaN(ey) && !double.IsNaN(sy)) cross = Math.Max(cross, Math.Abs(ey - sy));
                    }
                    if (cross > .3) { approachSkipped++; continue; }
                    // … nor a short (< 8 m) T onto a model road running across ours (45 Marina Studina × Žanića: our verge dropped beside its carriageway,
                    // a 0.7 m seam); longer approaches stay (d21 Kafana Kaštela 2, 15 m) as do stubs carrying our street on (05 Suvača)
                    {
                        double bd = 1e9, mdx = 0, mdz = 0;
                        foreach (var m in models)
                            foreach (var mr in m.j.roads)
                            {
                                if (mr.lvl != 0) continue;
                                for (int k = 0; k + 1 < mr.p.Count; k++)
                                {
                                    double ax = mr.p[k][0], az = mr.p[k][1], bx = mr.p[k + 1][0] - ax, bz = mr.p[k + 1][1] - az, l2 = bx * bx + bz * bz; if (l2 < 1e-6) continue;
                                    double tt = Math.Max(0, Math.Min(1, ((pc.x - ax) * bx + (pc.z - az) * bz) / l2)), dd = Sq(pc.x - ax - bx * tt) + Sq(pc.z - az - bz * tt);
                                    if (dd < bd) { bd = dd; double l = Math.Sqrt(l2); mdx = bx / l; mdz = bz / l; }
                                }
                            }
                        if (Math.Abs(s1 - s0) < 8 && bd < 15 * 15 && Math.Abs(mdx * pc.tx + mdz * pc.tz) < .6) { approachSkipped++; continue; }
                    }
                    // … nor where a model surface (its kerb / sidewalk / island colliders, World/Roads) sits 0.3–3 m off the drawn approach
                    bool clash = false;
                    for (double sq = Math.Min(s0, s1); sq <= Math.Max(s0, s1) + 2 && !clash; sq += 1)
                    {
                        var pq = PointAt(ei, Math.Min(L, sq)); double yq = pq.y + dy;
                        foreach (int sg in new[] { -1, 0, 1 })
                        {
                            var uq = W.U(pq.x - pq.tz * hwE * .9 * sg, 0, pq.z + pq.tx * hwE * .9 * sg); uq.y = (float)yq + 4;
                            foreach (var h in Physics.RaycastAll(uq, Vector3.down, 8, ~0, QueryTriggerInteraction.Ignore))
                                if (h.collider.transform.parent && h.collider.transform.parent.name == "Roads" && Math.Abs(h.point.y - yq) > .3) { clash = true; break; }
                            if (clash) break;
                        }
                    }
                    if (clash) { approachSkipped++; continue; }
                    if (Math.Abs(dy) > .05 && Math.Abs(dy) < 2.5)
                    {
                        double F = Math.Min(Math.Max(12, Math.Abs(dy) / .04), Math.Max(6, L - 2)); var P = e.pts;
                        for (int i = 0; i < P.Count; i++)
                        {
                            double ds = fwd ? s1 - e.cum[i] : e.cum[i] - s1; if (ds >= F) continue;
                            var q = (double[])P[i].Clone(); q[1] += dy * (ds <= 0 ? 1 : 1 - Smooth(ds / F)); P[i] = q;
                        }
                    }
                    double a = Math.Min(s0, s1) - 1, b = Math.Max(s0, s1) + 1;
                    if (fwd) b += 2; else a -= 2;
                    r.Add((Math.Max(0, a), Math.Min(L, b))); approaches++;
                }
            }
            return r;
        }

        static double kmDrawn; static int segsDrawn, sidewalkSegs;

        static void BuildEdge(int ei)
        {
            var e = edges[ei]; var sp = e.sp; var pts = e.pts; var cum = e.cum; int n = pts.Count; double L = e.L, hw = sp.halfWidth;
            int degA = adj[nIdx[e.a]].Count, degB = adj[nIdx[e.b]].Count;
            double clearA = NodeR(e.a), clearB = NodeR(e.b);
            cuts.TryGetValue(ei, out var mc);
            Fr cutF = default; bool hasCut = mc?.arm != null && !double.IsNaN(mc.sCut);
            if (hasCut) { var p = PointAt(ei, mc.sCut); cutF = new Fr { x = p.x, y = p.y, z = p.z, rx = -p.tz, rz = p.tx }; }
            double[] Snap(double s, double u, double w) => hasCut ? ArmSnap(mc, s, u, w, cutF) : new double[3];
            var endA = e.trimA > 0 ? (through: false, trim: e.trimA) : JunctionEnd(ei, true); var endB = e.trimB > 0 ? (through: false, trim: e.trimB) : JunctionEnd(ei, false);
            double MarkW(double s)
            {
                double w = 1;
                if ((degA >= 3 || endA.trim > 0) && !endA.through) w = Math.Min(w, Math.Max(0, Math.Min(1, (s - Math.Max(endA.trim, 4) - 4) / 8)));
                if ((degB >= 3 || endB.trim > 0) && !endB.through) w = Math.Min(w, Math.Max(0, Math.Min(1, (L - s - Math.Max(endB.trim, 4) - 4) / 8)));
                return w;
            }
            Fr Frame(int i)
            {
                var a = pts[Math.Max(0, i - 1)]; var b = pts[Math.Min(n - 1, i + 1)];
                double tx = b[0] - a[0], tz = b[2] - a[2], l = Math.Sqrt(tx * tx + tz * tz); if (l < 1e-9) l = 1; tx /= l; tz /= l;
                return new Fr { x = pts[i][0], y = pts[i][1], z = pts[i][2], rx = -tz, rz = tx, s = cum[i] };
            }
            Fr FrameAt(double s) { var p = PointAt(ei, s); return new Fr { x = p.x, y = p.y, z = p.z, rx = -p.tz, rz = p.tx, s = s }; }
            var tA = TaperAt(ei, e.a); var tB = TaperAt(ei, e.b);
            (double w, double mh, double mk) Shape(double s)
            {
                double w = hw, mh = sp.oneway ? -1 : sp.medianHalf, mk = 1;
                Taper T = null; double d = 0;
                if (tA != null && (tB == null || s <= L - s)) { T = tA; d = s; } else if (tB != null) { T = tB; d = L - s; }
                if (T != null && d < T.Lt)
                {
                    double k = Smooth(d / T.Lt); w = T.hwN + (hw - T.hwN) * k;
                    if (mh >= 0) mh = T.mhN + (mh - T.mhN) * k;
                    if (T.marks) mk = Smooth((d - 4) / (T.Lt * 0.45));
                }
                // an outside street meeting a model's road: its width tapered to the model's asphalt over 15 m
                if (!double.IsNaN(e.mhwA) && s < 15) w = e.mhwA + (w - e.mhwA) * Smooth(s / 15);
                if (!double.IsNaN(e.mhwB) && L - s < 15) w = e.mhwB + (w - e.mhwB) * Smooth((L - s) / 15);
                return (w, mh, mk);
            }
            bool VergeOK(double s) => !((clearA > 0 && s < clearA - 9) || (clearB > 0 && s > L - (clearB - 9)));
            bool Junction(double s) => (clearA > 0 && s < clearA) || (clearB > 0 && s > L - clearB);
            bool d8 = Regex.IsMatch(e.rf, @"^D\s?8$");
            bool sidewalkEdge = e.spur == null && !d8 && e.verge == "urban_kerb"; // (OSM sidewalk tags or not: built-up land decides, below)
            int lanesTot = sp.oneway ? sp.lanes : 2 * sp.lanes;
            // position of lateral u (half width w) at frame F, height offset dy, with the arm snap
            // an outside street ending on a model: the cross-section tilted onto the model's edge over the last 10 m
            var mendCache = new Dictionary<(bool, long), double>();
            double MEndDy(Fr F, double u)
            {
                double dy = 0;
                foreach (var (m, atB) in new[] { (e.mA, false), (e.mB, true) })
                {
                    if (m == null) continue; double d = atB ? L - F.s : F.s; if (d >= 10) continue;
                    var key = (atB, (long)Math.Round(u * 4));
                    if (!mendCache.TryGetValue(key, out double off))
                    {
                        var EF = FrameAt(atB ? L : 0); double sg = atB ? 1 : -1;
                        double ys = OutsideStreets.ModelY(m, EF.x + EF.rx * u, EF.z + EF.rz * u, EF.rz * sg, -EF.rx * sg);
                        off = double.IsNaN(ys) ? 0 : Math.Max(-.5, Math.Min(.5, ys - EF.y)); mendCache[key] = off;
                    }
                    dy += off * Smooth(1 - d / 10);
                }
                return dy;
            }
            G Pos(Fr F, double u, double w, double dy)
            {
                var sn = Snap(F.s, u, w);
                return new G(F.x + F.rx * u + sn[0], F.y + dy + sn[1] + (e.outside ? MEndDy(F, u) : 0), F.z + F.rz * u + sn[2]);
            }
            bool Blocked(Fr F, double u, double margin)
            {
                double x = F.x + F.rx * u, z = F.z + F.rz * u;
                var h = Nearest(x, z, 24, k => k != ei);
                return h.ei >= 0 && h.dist < edges[h.ei].sp.halfWidth + margin && Math.Abs(h.y - F.y) < 3;
            }
            bool inFurnTile(double x, double z) => ModelAt(x, z)?.furniture == true;
            // (an outside street runs up to a model's paving: on a tile's own unpaved ground it is still drawn)
            var own0 = e.outside ? Drop.OnModel : Drop.Owned;
            // a street of ours hanging on a footprint model's node without an arm cut (mag-11-19 tiles: our street kept up to the end of
            // the model's closed side stub): its last 14 m drop only on the model's paving, not 4 m around it — else a grass gap
            // between the stub's end and our street
            bool Hangs(long nid) => (mc?.arm == null || mc.endSnap) && models.Any(m => m.footprint && m.nodes.Contains(nid) && !m.edges.Contains(e.id));
            bool hangA = !e.outside && Hangs(e.a), hangB = !e.outside && Hangs(e.b);
            var approach = e.outside ? new List<(double a, double b)>() : Approach(ei);

            // sidewalks, decided per side every ~2 m (StreetsSidewalks.cs): built-up land (±30 m), the junction corner on that side
            // only (SideClear: the far side of a T runs on), other streets / driveways at the kerb, the footway narrowed to the
            // footprints / guardrails (dropped under 1 m), runs under 8 m dropped; lowered kerbs (2 cm) at the run ends
            bool walkEdge = sidewalkEdge && !IsD8(e) && e.type != "motorway" && e.type != "ramp";
            int NS = walkEdge ? Math.Max(2, (int)Math.Ceiling(L / 2) + 1) : 0; double DS = NS > 1 ? L / (NS - 1) : 1;
            var swOk = new bool[2][]; var swW = new double[2][]; var swK = new double[2][]; var swCap = new bool[2][];
            // built-up: a building on either side (a street through a village has its footways on both sides, also along a car park)
            var builtAny = new bool[NS];
            for (int k = 0; k < NS && walkEdge; k++)
            {
                double s = k * DS; var F = FrameAt(s); double w = Shape(s).w;
                foreach (int sd in new[] { -1, 1 }) if (!builtAny[k] && BuiltUp(F.x + F.rx * sd * w, F.z + F.rz * sd * w, F.rx * sd, F.rz * sd)) builtAny[k] = true;
            }
            var bu = Dilate(builtAny, (int)Math.Ceiling(30 / Math.Max(.5, DS)));
            if (walkEdge)
                foreach (int side in new[] { -1, 1 })
                {
                    int si = side < 0 ? 0 : 1;
                    var ok = new bool[NS]; var stop = new bool[NS]; var wd = new double[NS];
                    double cA = SideClear(ei, true, side), cB = SideClear(ei, false, side);
                    for (int k = 0; k < NS; k++)
                    {
                        double s = k * DS; var F = FrameAt(s); double w = Shape(s).w;
                        double rx = F.rx * side, rz = F.rz * side, ex = F.x + rx * w, ez = F.z + rz * w;
                        if (e.oneway && side != 1) { stop[k] = true; continue; }
                        // (neutral: outside the area, a furniture tile, cut away by a model — the run ends there without a ramp)
                        if (!InArea(F.x, F.z) || inFurnTile(F.x, F.z) || (!e.outside && mc != null && CutAway(mc, ei, Math.Max(0, s - 1), Math.Min(L, s + 1)))) continue;
                        if ((cA > 0 && s < cA) || (cB > 0 && s > L - cB) || Blocked(F, side * (w + .25), 1.1) || Blocked(F, side * (w + 1.3), .5)) { stop[k] = true; continue; }
                        wd[k] = FitWidth(ex, ez, rx, rz); if (wd[k] <= 0) { stop[k] = true; continue; }
                        ok[k] = true;
                    }
                    for (int k = 0; k < NS; k++) if (ok[k] && !bu[k]) { ok[k] = false; stop[k] = true; }
                    for (int k = 0; k < NS;)
                    {
                        if (!ok[k]) { k++; continue; }
                        int k0 = k; while (k < NS && ok[k]) k++;
                        if ((k - 1 - k0) * DS < 8) for (int q = k0; q < k; q++) { ok[q] = false; stop[q] = true; }
                    }
                    var w2 = new double[NS]; var kh = new double[NS]; var cap = new bool[NS];
                    for (int k = 0; k < NS; k++) { double mw = wd[k]; if (k > 0 && ok[k - 1]) mw = Math.Min(mw, wd[k - 1]); if (k + 1 < NS && ok[k + 1]) mw = Math.Min(mw, wd[k + 1]); w2[k] = mw; }
                    bool openA = cA == 0 && degA >= 2, openB = cB == 0 && degB >= 2;
                    for (int k = 0; k < NS;)
                    {
                        if (!ok[k]) { k++; continue; }
                        int k0 = k; while (k < NS && ok[k]) k++; int k1 = k - 1;
                        // a run end at a stop (junction corner, driveway, field, wall, dead end): lowered kerb; at a neutral sample or an
                        // edge end that runs on into the next edge: full height (+ an end cap where nothing continues)
                        bool rampA = k0 == 0 ? !openA : stop[k0 - 1], rampB = k1 == NS - 1 ? !openB : stop[k1 + 1];
                        for (int q = k0; q <= k1; q++) { double d = Math.Min(rampA ? (q - k0) * DS : 1e9, rampB ? (k1 - q) * DS : 1e9); kh[q] = .02 + .13 * Smooth(d / 1.5); }
                        cap[k0] = !(k0 == 0 && openA); cap[k1] = !(k1 == NS - 1 && openB);
                    }
                    swOk[si] = ok; swW[si] = w2; swK[si] = kh; swCap[si] = cap;
                }

            for (int i = 0; i + 1 < n; i++)
            {
                int j = i + 1;
                var own = (hangA && cum[i] < 14) || (hangB && L - cum[j] < 14) || approach.Any(q => cum[j] >= q.a && cum[i] <= q.b) ? Drop.OnModel : own0;
                double mx = (pts[i][0] + pts[j][0]) / 2, mz = (pts[i][2] + pts[j][2]) / 2;
                if (!InArea(mx, mz)) continue;
                double smid = (cum[i] + cum[j]) / 2;
                if (DuplicateRibbon(ei, smid)) continue;
                if (!e.outside && CutAway(mc, ei, cum[i], cum[j])) continue; // (outside streets: only their triangles on a model's paving go)
                if ((endA.trim > 0 && cum[j] <= endA.trim) || (endB.trim > 0 && cum[i] >= L - endB.trim)) continue;
                string kind = null; foreach (var sp2 in e.spans) if (smid >= sp2.f && smid <= sp2.t) kind = sp2.k;
                if (kind == "tunnel") continue;
                var A = Frame(i); var B = Frame(j);
                if (endA.trim > 0 && A.s < endA.trim) A = FrameAt(endA.trim);
                if (endB.trim > 0 && B.s > L - endB.trim) B = FrameAt(L - endB.trim);
                if (mc?.arm != null && A.s < mc.k0) A = FrameAt(mc.k0);
                if (mc?.arm != null && B.s > mc.k1) B = FrameAt(mc.k1);
                if (B.s - A.s < 0.02) continue;
                var shA = Shape(A.s); var shB = Shape(B.s); double wA = shA.w, wB = shB.w;
                bool verge = kind == null && VergeOK(A.s) && VergeOK(B.s);
                int before = tris;

                // asphalt (+ collider)
                G R(Fr F, double u, double w) { var g = Pos(F, u, w, 0); g.f1 = new Vector2((float)u, (float)F.s); g.f2 = new Vector2((float)w, (float)Math.Max(2 * w / lanesTot, 3.1)); return g; }
                Quad(MAT_ASPHALT, R(A, -wA, wA), R(B, -wB, wB), R(B, wB, wB), R(A, wA, wA), Vector3.up, own, true);
                bool any = tris > before;
                // collider flaps: a 3 m ramp 0.9 m down from each edge (a wheel off the asphalt climbs back)
                if (kind == null)
                    foreach (int sd in new[] { -1, 1 })
                    {
                        var ea = Pos(A, wA * sd, wA, 0); var eb = Pos(B, wB * sd, wB, 0);
                        var oa = new G(ea.x + A.rx * 3 * sd, ea.y - .9, ea.z + A.rz * 3 * sd); var ob = new G(eb.x + B.rx * 3 * sd, eb.y - .9, eb.z + B.rz * 3 * sd);
                        Quad(-1, ea, eb, ob, oa, Vector3.up, own, true);
                    }
                // sides: urban sidewalk (kerb 0.15 m) or a gravel verge
                foreach (int side in new[] { -1, 1 })
                {
                    int si = side < 0 ? 0 : 1;
                    var ok = walkEdge && kind == null ? swOk[si] : null;
                    var outA = new Vector3((float)(A.rx * side), 0, (float)(-A.rz * side));
                    if (ok == null)
                    {
                        if (verge) Quad(MAT_VERGE, Pos(A, side * wA, wA, -.01), Pos(B, side * wB, wB, -.01), Pos(B, side * (wB + 1.6), wB, -.4), Pos(A, side * (wA + 1.6), wA, -.4), Vector3.up, own, false);
                        continue;
                    }
                    // pieces between the 2 m samples, each corner on the straight asphalt edge A → B (lerped, as the ribbon's quad)
                    const double KW = .18;
                    G S(double f, double du, double dy) { var a = Pos(A, side * (wA + du), wA, dy); var b = Pos(B, side * (wB + du), wB, dy); return new G(a.x + (b.x - a.x) * f, a.y + (b.y - a.y) * f, a.z + (b.z - a.z) * f); }
                    var br = new List<double> { A.s };
                    for (int k = (int)Math.Floor(A.s / DS) + 1; k * DS < B.s - 1e-6; k++) if (k * DS > A.s + 1e-6) br.Add(k * DS);
                    br.Add(B.s);
                    double span = B.s - A.s; bool drew = false;
                    var tg = Vector3.Normalize(new Vector3((float)(B.x - A.x), 0, (float)(-(B.z - A.z))));
                    for (int p = 0; p + 1 < br.Count; p++)
                    {
                        double sa = br[p], sb = br[p + 1]; int k0 = Math.Max(0, Math.Min(NS - 2, (int)Math.Floor((sa + sb) / 2 / DS))), k1 = k0 + 1;
                        double fa = (sa - A.s) / span, fb = (sb - A.s) / span;
                        if (ok[k0] && ok[k1])
                        {
                            double ta = Math.Max(0, Math.Min(1, (sa - k0 * DS) / DS)), tb = Math.Max(0, Math.Min(1, (sb - k0 * DS) / DS));
                            var sw = swW[si]; var kh = swK[si];
                            double Wa = sw[k0] + (sw[k1] - sw[k0]) * ta, Wb = sw[k0] + (sw[k1] - sw[k0]) * tb, Ka = kh[k0] + (kh[k1] - kh[k0]) * ta, Kb = kh[k0] + (kh[k1] - kh[k0]) * tb;
                            Quad(MAT_KERB, S(fa, 0, 0), S(fb, 0, 0), S(fb, 0, Kb), S(fa, 0, Ka), -outA, Drop.OnModel, false);
                            Quad(MAT_KERB, S(fa, 0, Ka), S(fb, 0, Kb), S(fb, KW, Kb), S(fa, KW, Ka), Vector3.up, Drop.OnModel, false);
                            Quad(MAT_WALK, S(fa, KW, Ka), S(fb, KW, Kb), S(fb, Wb, Kb), S(fa, Wa, Ka), Vector3.up, Drop.OnModel, false);
                            Quad(MAT_KERB, S(fa, Wa, Ka), S(fb, Wb, Kb), S(fb, Wb, -.4), S(fa, Wa, -.4), outA, Drop.OnModel, false);
                            Quad(-1, S(fa, 0, Ka), S(fb, 0, Kb), S(fb, Wb, Kb), S(fa, Wa, Ka), Vector3.up, Drop.OnModel, true);
                            // end caps where the run stops
                            if (ta < 1e-6 && swCap[si][k0] && (k0 == 0 || !ok[k0 - 1])) Quad(MAT_KERB, S(fa, 0, 0), S(fa, Wa, -.4), S(fa, Wa, Ka), S(fa, 0, Ka), -tg, Drop.OnModel, false);
                            if (tb > 1 - 1e-6 && swCap[si][k1] && (k1 == NS - 1 || !ok[k1 + 1])) Quad(MAT_KERB, S(fb, 0, 0), S(fb, Wb, -.4), S(fb, Wb, Kb), S(fb, 0, Kb), tg, Drop.OnModel, false);
                            var c0 = S(fa, Wa / 2, 0); var c1 = S(fb, Wb / 2, 0); CountWalk(walkOurs, c0.x, c0.z, Math.Sqrt(Sq(c1.x - c0.x) + Sq(c1.z - c0.z)));
                            drew = true;
                        }
                        else if (verge)
                            Quad(MAT_VERGE, S(fa, 0, -.01), S(fb, 0, -.01), S(fb, 1.6, -.4), S(fa, 1.6, -.4), Vector3.up, own, false);
                    }
                    if (drew) sidewalkSegs++;
                }
                // markings (tiles/materials.ts), not on driveways
                if (e.spur == null && !e.noPaint)
                {
                    double mkA = MarkW(A.s) * shA.mk, mkB = MarkW(B.s) * shB.mk;
                    if (Math.Min(mkA, mkB) > .5) Paint(sp, shA.mh, A, B, wA, wB, Pos);
                }
                if (any)
                {
                    segsDrawn++; kmDrawn += (B.s - A.s) / 1000;
                    var ca = Pos(A, 0, wA, 0); var cb = Pos(B, 0, wB, 0);
                    Carve(ca.x, ca.z, Math.Min(ca.y, Pos(A, -wA, wA, 0).y), cb.x, cb.z, Math.Min(cb.y, Pos(B, -wB, wB, 0).y), Math.Max(wA, wB) + 1.6);
                    // (the snap can tilt the cross-section: the lower edge's height is used)
                    var la = Pos(A, -wA, wA, 0); var ra = Pos(A, wA, wA, 0); var lb = Pos(B, -wB, wB, 0); var rb = Pos(B, wB, wB, 0);
                    Carve(la.x, la.z, la.y, lb.x, lb.z, lb.y, 1.6); Carve(ra.x, ra.z, ra.y, rb.x, rb.z, rb.y, 1.6);
                    // outside streets: the ground also raised to the street (it follows our ground only smoothed)
                    if (e.outside) carve.Add(new[] { ca.x, ca.z, Math.Min(la.y, ra.y), cb.x, cb.z, Math.Min(lb.y, rb.y), Math.Max(wA, wB) + 1.6, 1 });
                    // our streets above the ground (override edges climbing to a model's road, mag-11-20 at the Lukšić warehouse: 1–1.9 m):
                    // the ground raised to them too, an embankment a truck off the road drives back up (not under a bridge)
                    else if (kind == null) carve.Add(new[] { ca.x, ca.z, Math.Min(la.y, ra.y), cb.x, cb.z, Math.Min(lb.y, rb.y), Math.Max(wA, wB) + 1.6, 2 });
                }
            }
        }

        delegate G PosFn(Fr F, double u, double w, double dy);
        static void Paint(Spec sp, double mh, Fr A, Fr B, double wA, double wB, PosFn pos)
        {
            double lw = sp.laneW, nl = sp.lanes, lineW = lw > 3.7 ? .25 : .15;
            void Line(double u, double width, double on, double period)
            {
                if (Math.Abs(u) > Math.Min(wA, wB) + .01) return;
                double s0 = A.s, s1 = B.s;
                if (period <= 0) { Strip(u, width, s0, s1); return; }
                for (double k = Math.Floor(s0 / period) * period; k < s1; k += period) { double a = Math.Max(s0, k), b = Math.Min(s1, k + on); if (b > a + .01) Strip(u, width, a, b); }
            }
            void Strip(double u, double width, double a, double b)
            {
                double fa = (a - A.s) / (B.s - A.s), fb = (b - A.s) / (B.s - A.s);
                G At(double f, double uu) { var p = pos(A, uu, wA, .015); var q = pos(B, uu, wB, .015); return new G(p.x + (q.x - p.x) * f, p.y + (q.y - p.y) * f, p.z + (q.z - p.z) * f); }
                Quad(MAT_PAINT, At(fa, u - width / 2), At(fb, u - width / 2), At(fb, u + width / 2), At(fa, u + width / 2), Vector3.up, Drop.Owned, false);
            }
            if (mh < -.5)
            {
                double halfW = nl * lw * .5;
                Line(-halfW, lineW, 0, 0); Line(halfW, lineW, 0, 0);
                for (int k = 1; k < 4; k++) if (k < nl) Line(-halfW + k * lw, lineW, 6, 18);
            }
            else if (mh > 0)
            {
                foreach (int sd in new[] { -1, 1 })
                {
                    double q0 = mh + sp.innerStrip;
                    Line(sd * q0, lineW, 0, 0); Line(sd * (q0 + nl * lw), lineW, 0, 0);
                    for (int k = 1; k < 4; k++) if (k < nl) Line(sd * (q0 + k * lw), lineW, 6, 18);
                }
            }
            else
            {
                Line(-nl * lw, lineW, 0, 0); Line(nl * lw, lineW, 0, 0);
                if (nl < 1.5) Line(0, lineW, 3, 9);
                else { Line(-.12, .12, 0, 0); Line(.12, .12, 0, 0); }
                for (int k = 1; k < 4; k++) if (k < nl) { Line(-k * lw, lineW, 3, 9); Line(k * lw, lineW, 3, 9); }
            }
        }

        // ------------------------------------------------------------------ junction discs + kerb fillets (buildRoads.ts fallback patch)
        static double KerbRadius(string type) => type == "motorway" || type == "trunk" ? 12 : type == "primary" || type == "secondary" || type == "ramp" ? 9 : 6;
        static int discs;
        static void BuildJunction(int ni)
        {
            var nd = nodes[ni]; double rbR = RbR(nd.id);
            if ((adj[ni].Count < 3 && rbR <= 0) || !InArea(nd.x, nd.z)) return;
            double R = 0; foreach (var k in adj[ni]) R = Math.Max(R, edges[k].sp.halfWidth);
            R += 1.2; if (rbR > 0) R = Math.Max(R + 1, rbR);
            double y = nd.y - .025; int K = rbR > 0 ? 40 : 20; int before = tris;
            var c = new G(nd.x, y, nd.z);
            for (int k = 0; k < K; k++)
            {
                double a0 = k * Math.PI * 2 / K, a1 = (k + 1) * Math.PI * 2 / K;
                Tri(MAT_ASPHALT, c, new G(nd.x + Math.Cos(a0) * R, y, nd.z + Math.Sin(a0) * R), new G(nd.x + Math.Cos(a1) * R, y, nd.z + Math.Sin(a1) * R), Vector3.up, Drop.Owned, true);
            }
            if (tris > before) { discs++; Carve(nd.x, nd.z, y, nd.x, nd.z, y, R + 1.6); }
            if (rbR > 0) { Island(nd.x, y, nd.z, Math.Max(2.5, R - 6.5)); return; }
            // fillets between neighbouring arms 45–137° apart
            var arms = new List<(int k, bool fromA, double L, double dx, double dz, double ang, double hw, string type, bool site)>();
            foreach (var k in adj[ni])
            {
                var q = edges[k]; bool site = q.spur != null && Math.Abs(q.sp.laneW - 2.75) < 1e-6 && q.sp.shoulder == 0;
                if (q.spur != null && !site) continue;
                var ends = new List<bool>(); if (nIdx[q.a] == ni) ends.Add(true); if (nIdx[q.b] == ni) ends.Add(false);
                foreach (var fa in ends)
                {
                    double s = Math.Min(8, q.L * .5); var p = PointAt(k, fa ? s : q.L - s);
                    double dx = p.x - nd.x, dz = p.z - nd.z, l = Math.Sqrt(dx * dx + dz * dz); if (l < .5) continue;
                    arms.Add((k, fa, q.L, dx / l, dz / l, Math.Atan2(dz, dx), q.sp.halfWidth, q.type, site));
                }
            }
            if (arms.Count < 2) return;
            arms.Sort((p, q) => p.ang.CompareTo(q.ang));
            double hwMax = arms.Max(a => a.hw);
            double YAt(int idx, double s) { var a = arms[idx]; return PointAt(a.k, a.fromA ? Math.Min(a.L, s) : Math.Max(0, a.L - s)).y; }
            for (int i = 0; i < arms.Count; i++)
            {
                int ib = (i + 1) % arms.Count; if (ib == i) continue; var A = arms[i]; var B = arms[ib];
                double th = B.ang - A.ang; if (th <= 0) th += Math.PI * 2; if (th < .8 || th > 2.4) continue;
                double ax = nd.x - A.dz * A.hw, az = nd.z + A.dx * A.hw, bx = nd.x + B.dz * B.hw, bz = nd.z - B.dx * B.hw;
                double det = A.dz * B.dx - A.dx * B.dz; if (Math.Abs(det) < 1e-3) continue;
                double rx = bx - ax, rz = bz - az, t = (B.dx * rz - B.dz * rx) / det, u = (A.dx * rz - A.dz * rx) / det;
                if (t < 0 || u < 0) continue;
                double px = ax + A.dx * t, pz = az + A.dz * t, tanH = Math.Tan(th / 2);
                double r = Math.Max(Math.Max(KerbRadius(A.type), KerbRadius(B.type)), A.site || B.site ? 9 : 0);
                double Tmax = Math.Min(Math.Min(14, (A.L - t) * .6), Math.Min((B.L - u) * .6, hwMax + 11 - Math.Sqrt(Sq(px - nd.x) + Sq(pz - nd.z))));
                if (r / tanH > Tmax) r = Tmax * tanH; if (r < 1.5) continue;
                double T = r / tanH, bxs = A.dx + B.dx, bzs = A.dz + B.dz, bl = Math.Sqrt(bxs * bxs + bzs * bzs); if (bl < 1e-9) bl = 1; bxs /= bl; bzs /= bl;
                double dc = r / Math.Sin(th / 2), ccx = px + bxs * dc, ccz = pz + bzs * dc;
                double a1 = Math.Atan2(pz + A.dz * T - ccz, px + A.dx * T - ccx), sweep = Math.Atan2(pz + B.dz * T - ccz, px + B.dx * T - ccx) - a1;
                while (sweep > Math.PI) sweep -= Math.PI * 2; while (sweep < -Math.PI) sweep += Math.PI * 2;
                double yA = YAt(i, t + T) - .03, yB = YAt(ib, u + T) - .03;
                int KK = Math.Max(3, (int)Math.Ceiling(Math.Abs(sweep) / .2));
                var p0 = new G(px, (YAt(i, t) + YAt(ib, u)) / 2 - .03, pz); G prev = default; bool has = false;
                for (int k = 0; k <= KK; k++)
                {
                    double a = a1 + sweep * k / KK; var g = new G(ccx + Math.Cos(a) * r, yA + (yB - yA) * k / KK, ccz + Math.Sin(a) * r);
                    if (has)
                    {
                        Tri(MAT_ASPHALT, p0, prev, g, Vector3.up, Drop.Owned, true);
                        // collider flaps off the arc like the ribbons' (towards the corner's centre, ≤ 0.9 m down at 30 %): the fillet's
                        // edge stood 0.56 m over the ground beside the Lukšić warehouse exit (a wall for a truck cutting the corner)
                        double fl = Math.Min(3, r * .8);
                        G Out(G q) { double dx = ccx - q.x, dz = ccz - q.z, l = Math.Sqrt(dx * dx + dz * dz); if (l < 1e-6) l = 1; return new G(q.x + dx / l * fl, q.y - .3 * fl, q.z + dz / l * fl); }
                        Quad(-1, prev, g, Out(g), Out(prev), Vector3.up, Drop.Owned, true);
                    }
                    prev = g; has = true;
                    // the ground under the fillet at its own heights: a spoke from the corner to each arc point (one flat capsule p → c at
                    // the lower arm's height, r + 1.6 = 10.6 m round a site spur, dug a 1.7 m pit beside the warehouse exit's climbing arm),
                    // and raised to it where it is lower (as the ribbons' embankments)
                    Carve(p0.x, p0.z, p0.y, g.x, g.z, g.y, 1.6);
                    carve.Add(new[] { p0.x, p0.z, p0.y, g.x, g.z, g.y, 1.6, 2 });
                }
            }
        }
        /// raised island of a collapsed roundabout (kerb ring, apron, planted centre — buildRoads.ts roundaboutIsland, simplified)
        static void Island(double cx, double y, double cz, double Ri)
        {
            const int K = 32;
            G At(double r, double a, double yy) => new G(cx + Math.Cos(a) * r, yy, cz + Math.Sin(a) * r);
            for (int k = 0; k < K; k++)
            {
                double a0 = k * Math.PI * 2 / K, a1 = (k + 1) * Math.PI * 2 / K; var o = new Vector3((float)Math.Cos((a0 + a1) / 2), 0, (float)-Math.Sin((a0 + a1) / 2));
                Quad(MAT_KERB, At(Ri, a0, y - .2), At(Ri, a1, y - .2), At(Ri, a1, y + .12), At(Ri, a0, y + .12), o, Drop.Owned, true);
                Quad(MAT_KERB, At(Ri - .3, a0, y + .12), At(Ri, a0, y + .12), At(Ri, a1, y + .12), At(Ri - .3, a1, y + .12), Vector3.up, Drop.Owned, true);
                Tri(MAT_VERGE, new G(cx, y + .1, cz), At(Ri - .3, a0, y + .1), At(Ri - .3, a1, y + .1), Vector3.up, Drop.Owned, true);
            }
        }

        // ------------------------------------------------------------------ stop courts + bays
        static int nCourts;
        // the models' road colliders (World/Roads, as WorldBuilder.FixHoles): asphalt, sidewalks, islands of Marko's models
        static List<Collider> paveCols;
        /// height of a model's paving (road or sidewalk) at game (x, z) within 3 m of `nearY`, NaN = none
        static double PaveY(double gx, double gz, double nearY)
        {
            var o = W.U(gx, nearY + 20, gz); var ray = new Ray(o, Vector3.down); double best = double.NaN;
            foreach (var cl in paveCols)
            {
                var bb = cl.bounds; if (o.x < bb.min.x || o.x > bb.max.x || o.z < bb.min.z || o.z > bb.max.z) continue;
                if (cl.Raycast(ray, out var h, 60) && Math.Abs(h.point.y - nearY) < 3) best = double.IsNaN(best) ? h.point.y : Math.Max(best, h.point.y);
            }
            return best;
        }
        static void BuildCourts(List<string> log)
        {
            paveCols = new List<Collider>();
            var roads = GameObject.Find("World")?.transform.Find("Roads");
            if (roads) foreach (var cl in roads.GetComponentsInChildren<MeshCollider>()) if (cl.transform.parent == roads) paveCols.Add(cl);
            Physics.SyncTransforms();
            foreach (var so in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "stops.json")))["stops"]))
            {
                var s = MiniJson.O(so); string kind = (string)s["kind"];
                double x = MiniJson.D(s["x"]), z = MiniJson.D(s["z"]);
                if (kind == "radar" || kind == "border" || !InArea(x, z)) continue;
                var site = MiniJson.O(MiniJson.G(s, "site")); if (site == null) continue;
                double y = MiniJson.D(MiniJson.G(s, "y") ?? 0.0), yaw = MiniJson.D(s["yaw"]), hl = MiniJson.D(s["hl"]), hw = MiniJson.D(s["hw"]);
                double fx = Math.Sin(yaw), fz = Math.Cos(yaw), ax = fz, az = -fx; // forward, across (DeliverySite.tsx: local z → (fx, fz), local x → (fz, −fx))
                if (SurfaceDist(models, x, z) < .5) { log.Add($"{s["id"]}: on a model's paving, no court"); continue; }
                courtAsphalt = (site.ContainsKey("surface") ? site["surface"] as string : "") != "concrete";
                // rounded rectangle (r 2 m), surround × 1.04 (kerb concrete); straight sides cut into ≤ 1 m pieces
                var ring0 = new List<(double lx, double lz)>();
                double rr = Math.Min(2, Math.Min(hw, hl) * .5);
                foreach (var (cxl, czl, a0) in new[] { (hw - rr, hl - rr, 0.0), (-hw + rr, hl - rr, Math.PI / 2), (-hw + rr, -hl + rr, Math.PI), (hw - rr, -hl + rr, Math.PI * 1.5) })
                    for (int k = 0; k <= 4; k++) { double a = a0 + k * Math.PI / 8; ring0.Add((cxl + Math.Cos(a) * rr, czl + Math.Sin(a) * rr)); }
                var ring = new List<(double lx, double lz)>();
                for (int k = 0; k < ring0.Count; k++)
                {
                    var p = ring0[k]; var q = ring0[(k + 1) % ring0.Count]; int nk = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(Sq(q.lx - p.lx) + Sq(q.lz - p.lz))));
                    for (int i = 0; i < nk; i++) ring.Add((p.lx + (q.lx - p.lx) * i / nk, p.lz + (q.lz - p.lz) * i / nk));
                }
                // a court beside a model's road (mag-11-19 tiles: the stop's own street replaced by the model, e.g. ducan_stari on s07's
                // old road): the court ends 0.2 m short of the model's paving (each ring point pulled in towards the centre, so the
                // paving's edge is followed, not a fan of wedges dropped on it); a court whose spur the model removed (GameDataBuilder.
                // ReattachStops joins it to that road) also sits at the paving's height (its y is the old graph's) — the truck rolls from
                // the court straight onto the sidewalk / road
                var sc = Enumerable.Repeat(1.0, ring.Count).ToArray(); var kerbSc = Enumerable.Repeat(1.04, ring.Count).ToArray();
                var contact = new List<double>(); int pulled = 0;
                if (paveCols.Count > 0 && double.IsNaN(PaveY(x, z, y)))
                    for (int k = 0; k < ring.Count; k++)
                    {
                        double len = Math.Sqrt(Sq(ring[k].lx) + Sq(ring[k].lz)); if (len < 1e-6) continue;
                        double ux = (ax * ring[k].lx + fx * ring[k].lz) / len, uz = (az * ring[k].lx + fz * ring[k].lz) / len;
                        for (double d = .5; d <= len * 1.04 + .3; d += .1)
                        {
                            double py = PaveY(x + ux * d, z + uz * d, y); if (double.IsNaN(py)) continue;
                            sc[k] = Math.Max(.3, Math.Min(1, (d - .2) / len)); kerbSc[k] = Math.Max(sc[k], Math.Min(1.04, (d - .02) / len));
                            if (d - .2 < len) { contact.Add(py); pulled++; }
                            break;
                        }
                    }
                // (only a stop cut off from its own spur / access edge: one that still has it keeps the height that edge was built to)
                long spurE = MiniJson.G(s, "spur") is Dictionary<string, object> spo && spo.ContainsKey("edge") ? (long)MiniJson.D(spo["edge"]) : long.MinValue;
                long accE = MiniJson.G(s, "access") is Dictionary<string, object> aco && aco.ContainsKey("edge") ? (long)MiniJson.D(aco["edge"]) : long.MinValue;
                bool cutOff = !eIdx.ContainsKey(spurE) && !eIdx.ContainsKey(accE);
                if (contact.Count > 0 && cutOff)
                {
                    contact.Sort(); double py = contact[contact.Count / 2];
                    if (Math.Abs(py - y) < 2) { log.Add($"{s["id"]}: court at the model paving's height {py:F2} (stop y {y:F2}), {pulled} of {ring.Count} edge points pulled in off it"); y = py - .02; }
                }
                double dyBay = y - MiniJson.D(MiniJson.G(s, "y") ?? 0.0);
                G W2(double lx, double lz, double yy, double sc1 = 1) => new G(x + (ax * lx + fx * lz) * sc1, yy, z + (az * lx + fz * lz) * sc1);
                var c = W2(0, 0, y + .03);
                for (int k = 0; k < ring.Count; k++)
                {
                    int k1 = (k + 1) % ring.Count; var p = ring[k]; var q = ring[k1];
                    Tri(MAT_COURT, c, W2(p.lx, p.lz, y + .03, sc[k]), W2(q.lx, q.lz, y + .03, sc[k1]), Vector3.up, Drop.OnModel, false);
                    Tri(-1, new G(x, y + .01, z), W2(p.lx, p.lz, y + .01, sc[k]), W2(q.lx, q.lz, y + .01, sc[k1]), Vector3.up, Drop.OnModel, true);
                    Quad(MAT_KERB, W2(p.lx, p.lz, y + .02, sc[k]), W2(p.lx, p.lz, y + .02, kerbSc[k]), W2(q.lx, q.lz, y + .02, kerbSc[k1]), W2(q.lx, q.lz, y + .02, sc[k1]), Vector3.up, Drop.OnModel, false);
                    var o = W2(p.lx, p.lz, 0, kerbSc[k]); var oq = W2(q.lx, q.lz, 0, kerbSc[k1]);
                    var dir = new Vector3((float)((p.lx + q.lx) * ax + (p.lz + q.lz) * fx), 0, (float)-((p.lx + q.lx) * az + (p.lz + q.lz) * fz));
                    Quad(MAT_KERB, new G(o.x, y + .02, o.z), new G(oq.x, y + .02, oq.z), new G(oq.x, y - .35, oq.z), new G(o.x, y - .35, o.z), dir, Drop.OnModel, false);
                }
                string surf = courtAsphalt ? "asphalt" : "concrete"; courtAsphalt = false;
                // bay lines (DeliverySite: ±(halfWid + 0.25), 0.12 wide, along the court)
                foreach (var bo in MiniJson.L(MiniJson.G(s, "bays")) ?? new List<object>())
                {
                    var b = MiniJson.O(bo); double bx = MiniJson.D(b["x"]), bz = MiniJson.D(b["z"]), by = MiniJson.D(MiniJson.G(b, "y") ?? MiniJson.G(s, "y") ?? 0.0) + dyBay;
                    double bhl = MiniJson.G(b, "hl") is object o1 ? MiniJson.D(o1) : 9.5, bhw = MiniJson.G(b, "hw") is object o2 ? MiniJson.D(o2) : 2.6;
                    foreach (int sd in new[] { -1, 1 })
                    {
                        double u = sd * (bhw + .25);
                        G Bp(double du, double dl) => new G(bx + ax * (u + du) + fx * dl, by + .045, bz + az * (u + du) + fz * dl);
                        Quad(MAT_BAY, Bp(-.06, -bhl), Bp(-.06, bhl), Bp(.06, bhl), Bp(.06, -bhl), Vector3.up, Drop.OnModel, false);
                    }
                }
                courts.Add((x, z, y, fx, fz, hl + .5, hw + .5));
                nCourts++; log.Add($"{s["id"]}: court {2 * hl:F0} × {2 * hw:F0} m, {surf}");
            }
        }

        // ------------------------------------------------------------------ closures (works barrier)
        static List<(double x, double y, double z, double heading, double neg, double pos)> barriers;
        static void FindClosures()
        {
            barriers = new List<(double, double, double, double, double, double)>(); barriersDropped = 0; ownClosures = 0;
            // (a model's paved end / closed arm that an outside street now continues: no barrier — mag-11-19 tiles' side stubs that our
            // graph lacks but ceste_izvan_modela.json has)
            var outEnds = edges.Where(e => e.outside).SelectMany(e => new[] { e.pts[0], e.pts[e.pts.Count - 1] }).ToList();
            bool continued(double x, double z) => outEnds.Any(p => Sq(p[0] - x) + Sq(p[2] - z) < 12 * 12);
            var cf = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "road-closures.json")));
            foreach (var co in MiniJson.L(cf["closures"]))
            {
                var c = MiniJson.O(co); long eid = (long)MiniJson.D(c["edgeId"]); double x = MiniJson.D(c["x"]), z = MiniJson.D(c["z"]);
                if (!InArea(x, z) || !eIdx.TryGetValue(eid, out int ei) || !edges[ei].closed) continue;
                var m = ModelOwnerAt(x, z);
                if (m != null)
                {
                    var e = edges[ei]; var nd = nodes[nIdx[(string)c["whichEnd"] == "a" ? e.a : e.b]];
                    if (!(m.edges.Contains(eid) && nd.kind == "end")) continue;
                    if (continued(x, z)) { barriersDropped++; continue; }
                }
                var p = PointAt(ei, MiniJson.D(c["alongEdgeM"])); double ext = edges[ei].sp.halfWidth + .5;
                barriers.Add((p.x, p.y, p.z, MiniJson.D(c["heading"]), ext, ext));
            }
            // closures/Roadworks2.tsx: barriers at both ends (3 m in) of every edge closed by its own `closed` — inside a model's square only
            // at the dead end of the model's closed arm (mag-11-20: c09's D8 to Trogir, 41's three arms, the tiles' side stubs; the GLB's own
            // `barriers` are not drawn)
            for (int ei = 0; ei < edges.Count; ei++)
            {
                var e = edges[ei]; if (e.closedOwn != true || fileClosed.Contains(e.id) || e.L < 8) continue;
                foreach (bool atA in new[] { true, false })
                {
                    var p = PointAt(ei, atA ? 3 : e.L - 3); if (!InArea(p.x, p.z)) continue;
                    var m = ModelOwnerAt(p.x, p.z);
                    if (m != null && !(m.edges.Contains(e.id) && nodes[nIdx[atA ? e.a : e.b]].kind == "end")) continue;
                    if (continued(p.x, p.z)) { barriersDropped++; continue; }
                    double ext = e.sp.halfWidth + .5;
                    barriers.Add((p.x, p.y, p.z, atA ? Math.Atan2(p.tz, p.tx) : Math.Atan2(-p.tz, -p.tx), ext, ext));
                    ownClosures++;
                }
            }
            foreach (var m in models) foreach (var q in m.paveEnds)
                if (!InArea(q[0], q[2])) continue;
                else if (continued(q[0], q[2])) barriersDropped++;
                else barriers.Add((q[0], q[1], q[2], q[3], q[4] / 2 + .3, q[4] / 2 + .3));
        }
        static int barriersDropped, ownClosures; static HashSet<long> fileClosed = new HashSet<long>();
        static void BuildBarriers(GameObject root)
        {
            if (barriers.Count == 0) return;
            var mats = new[] { BarrierStripes(), WorldBuilder.Lit("WorksSteel", new Color(.4f, .43f, .45f), .3f, .4f), WorldBuilder.Lit("WorksRubber", new Color(.08f, .085f, .09f), .05f), Amber() };
            var v = new List<Vector3>[4]; var uv = new List<Vector2>[4]; var nr = new List<Vector3>[4]; var ix = new List<int>[4];
            for (int k = 0; k < 4; k++) { v[k] = new List<Vector3>(); uv[k] = new List<Vector2>(); nr[k] = new List<Vector3>(); ix[k] = new List<int>(); }
            var go = new GameObject("barriers"); go.isStatic = true; go.transform.SetParent(root.transform, false);
            foreach (var bz in barriers)
            {
                // local frame: +x across (left of the closing direction), +z = heading (away from the open road), y up
                var hd = new Vector3((float)Math.Cos(bz.heading), 0, (float)-Math.Sin(bz.heading));
                var across = new Vector3(-hd.z, 0, hd.x);
                double width = bz.neg + bz.pos, mid = (bz.pos - bz.neg) / 2;
                var o = W.U(bz.x, bz.y + .02, bz.z);
                void Box(int mat, double lx, double ly, double lz, double sx, double sy, double sz)
                {
                    var c = o + across * (float)(lx + mid) + Vector3.up * (float)ly + hd * (float)lz;
                    var ex = across * (float)(sx / 2); var ey = Vector3.up * (float)(sy / 2); var ez = hd * (float)(sz / 2);
                    foreach (var (n, a, b) in new[] { (ex, ey, ez), (-ex, ey, -ez), (ey, ez, ex), (-ey, -ez, ex), (ez, ex, ey), (-ez, -ex, ey) })
                    {
                        // face centre c + n, spanned by a, b; wound outward
                        var p0 = c + n - a - b; var p1 = c + n - a + b; var p2 = c + n + a + b; var p3 = c + n + a - b;
                        if (Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), n) < 0) { var t = p1; p1 = p3; p3 = t; }
                        int i0 = v[mat].Count; var nn = n.normalized;
                        foreach (var p in new[] { p0, p1, p2, p3 }) { v[mat].Add(p); nr[mat].Add(nn); float along = Vector3.Dot(p - o, across); uv[mat].Add(new Vector2(along / 1.0f, (p.y - o.y) / .3f)); }
                        ix[mat].AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
                    }
                }
                Box(0, 0, .85, 0, width, .3, .2); Box(0, 0, 1.3, 0, width, .3, .2);
                foreach (int sd in new[] { -1, 1 })
                {
                    double px = sd * (width / 2 - Math.Min(.25, width / 8));
                    Box(1, px, .7, 0, .12, 1.4, .16); Box(2, px, .08, 0, .65, .16, .9); Box(3, px, 1.6, 0, .23, .23, .23);
                }
                Box(1, width / 2 - 1, 1.7, 0, .07, 2.9, .07); Box(0, width / 2 - 1, 2.35, .06, 1.6, 1.2, .03);
                var col = new GameObject("c"); col.isStatic = true; col.transform.SetParent(go.transform, false);
                col.transform.position = o + across * (float)mid + Vector3.up * .8f; col.transform.rotation = Quaternion.LookRotation(hd);
                var bc = col.AddComponent<BoxCollider>(); bc.size = new Vector3((float)width, 1.6f, .5f);
            }
            var mesh = new Mesh { name = "barriers", indexFormat = IndexFormat.UInt32 };
            var all = new List<Vector3>(); var allN = new List<Vector3>(); var allU = new List<Vector2>(); var subs = new List<int[]>(); var sm = new List<Material>();
            for (int k = 0; k < 4; k++) { if (v[k].Count == 0) continue; int b0 = all.Count; all.AddRange(v[k]); allN.AddRange(nr[k]); allU.AddRange(uv[k]); subs.Add(ix[k].Select(q => q + b0).ToArray()); sm.Add(mats[k]); }
            mesh.SetVertices(all); mesh.SetNormals(allN); mesh.SetUVs(0, allU); mesh.subMeshCount = subs.Count; for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
            mesh.RecalculateBounds(); Save(mesh, OUT + "/barriers.asset");
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = sm.ToArray();
        }
        static Material BarrierStripes()
        {
            string p = "Assets/Textures/works_stripes.png";
            if (!File.Exists(p))
            {
                int N = 128; var t = new Texture2D(N, N / 4, TextureFormat.RGB24, true);
                for (int y = 0; y < N / 4; y++) for (int x = 0; x < N; x++) { bool red = ((x + y * 2) / (N / 4)) % 2 == 0; t.SetPixel(x, y, red ? new Color(.72f, .08f, .06f) : new Color(.92f, .92f, .9f)); }
                File.WriteAllBytes(p, t.EncodeToPNG()); AssetDatabase.ImportAsset(p);
            }
            var m = WorldBuilder.Lit("WorksStripes", Color.white, .35f); m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(p)); EditorUtility.SetDirty(m); return m;
        }
        static Material Amber()
        {
            var m = WorldBuilder.Lit("WorksAmber", new Color(1f, .7f, .05f), .5f);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(1f, .55f, 0) * 2.2f); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; EditorUtility.SetDirty(m); return m;
        }

        // ------------------------------------------------------------------ materials
        static Material[] Mats()
        {
            var asph = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.GEN + "/Materials/AsphaltFlat.mat") ?? GraphicsPass.AsphaltMaterial();
            var paint = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.GEN + "/Materials/PaintFlat.mat") ?? GraphicsPass.PaintMaterial();
            var grey = "concrete_grey_d.jpg"; // (concrete_d.jpg is brown: the grey copy WorldBuilder.DeckMat made)
            var verge = WorldBuilder.Lit("StreetVerge", new Color(.78f, .76f, .72f), .05f, 0, "PolyHaven/gravel_ground_01_diff.jpg", "PolyHaven/gravel_ground_01_nor.jpg", .7f); // (the terrain's pale gravel layer, as the near-road blend)
            var kerb = WorldBuilder.Lit("StreetKerb", new Color(1f, 1f, .97f), .12f, 0, grey, "concrete_n.jpg", .6f);
            var walk = WorldBuilder.Lit("StreetWalk", new Color(.86f, .85f, .83f), .14f, 0, grey, "concrete_n.jpg", .8f);
            var court = WorldBuilder.Lit("StreetCourt", new Color(.82f, .81f, .79f), .12f, 0, grey, "concrete_n.jpg", .6f);
            var bay = WorldBuilder.Lit("PaintBay", new Color(.86f, .8f, .55f), .3f, 0, null);
            if (paint.GetTexture("_BaseMap")) bay.SetTexture("_BaseMap", paint.GetTexture("_BaseMap"));
            return new[] { asph, paint, verge, kerb, walk, court, bay };
        }
        static void Save(UnityEngine.Object o, string path) { if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(o, path); }

        /// carriageway segments (game x / z of both ends, half width of the asphalt) of the streets Build draws: graph + outside
        /// streets (PropsBuilder keeps props off them)
        /// ceste-pregled: the graph / outside edges within r of a point (id, kind, name, half width, distance, height there, ends)
        public static List<string> EdgesNear(double x, double z, double r)
        {
            LoadGraph(); LoadModels(); ApplyOutside(); ModelCuts();
            var o = new List<string>();
            foreach (var e in edges)
            {
                if (e.pts == null || e.sp == null) continue;
                double best = 1e9, by = 0; int bi = 0;
                for (int i = 0; i < e.pts.Count; i++) { double d = Math.Sqrt(Sq(e.pts[i][0] - x) + Sq(e.pts[i][2] - z)); if (d < best) { best = d; by = e.pts[i][1]; bi = i; } }
                if (best > r) continue;
                var a = e.pts[0]; var b = e.pts[e.pts.Count - 1];
                o.Add(FormattableString.Invariant($"edge {e.id} {(e.outside ? "OUT" : e.splitOf >= 0 ? "SPLIT" : "graph")} '{e.name}' {e.type} hw {e.sp.halfWidth:F1} d {best:F1} y {by:F2} pt {bi}/{e.pts.Count} A {e.a}@({a[0]:F0},{a[2]:F0},{a[1]:F1}) B {e.b}@({b[0]:F0},{b[2]:F0},{b[1]:F1}) mA {e.mA?.key} mB {e.mB?.key} closed {e.closed} cut {(cuts != null && cuts.ContainsKey(edges.IndexOf(e)) ? (cuts[edges.IndexOf(e)].arm != null ? "arm" : "model") : "-")}"));
            }
            return o;
        }
        public static List<(double ax, double az, double bx, double bz, double hw)> Carriageways()
        {
            LoadGraph(); LoadModels(); ApplyOutside();
            var r = new List<(double, double, double, double, double)>();
            foreach (var e in edges)
            {
                if (e.pts == null || e.sp == null) continue;
                for (int i = 0; i + 1 < e.pts.Count; i++) r.Add((e.pts[i][0], e.pts[i][2], e.pts[i + 1][0], e.pts[i + 1][2], e.sp.halfWidth));
            }
            return r;
        }

        // ------------------------------------------------------------------ build
        [MenuItem("Magistrala/Build streets")]
        public static string Build()
        {
            MainScene.Ensure(); Physics.SyncTransforms();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var log = new List<string>();
            log.Add(LoadGraph()); LoadModels(); log.Add(ApplyOutside()); ModelCuts(); log.Add($"{endSnaps} street ends snapped onto the model road they start at"); approaches = 0; approachSkipped = 0; log.Add($"{endHeights} street ends brought to the model paving they leave (max {endHeightMax:F2} m)");
            log.Add(LoadFeet()); walkOurs = new Dictionary<string, double>();
            modelNodes = new HashSet<long>(models.SelectMany(m => m.nodes));
            chunks = new Dictionary<(int, int), Chunk>(); carve = new List<double[]>(); courts = new List<(double, double, double, double, double, double, double)>();
            tris = dropped = segsDrawn = sidewalkSegs = discs = nCourts = 0; kmDrawn = 0;
            if (!AssetDatabase.IsValidFolder(OUT)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Streets");
            foreach (var f in Directory.GetFiles(OUT, "chunk_*.asset")) AssetDatabase.DeleteAsset(f);
            for (int ei = 0; ei < edges.Count; ei++)
            {
                var e = edges[ei]; bool near = false;
                foreach (var p in e.pts) if (InArea(p[0], p[2])) { near = true; break; }
                if (near) BuildEdge(ei);
            }
            for (int ni = 0; ni < nodes.Count; ni++) BuildJunction(ni);
            BuildCourts(log); log.Add($"{approaches} street approaches drawn up to a model's paving, {approachSkipped} left as before (ramp / short edge / paving not level across) (ceste-pregled 2)");
            log.Add(BuildModelWalks());
            log.Add(WalkStats());
            FindClosures();

            var root = WorldBuilder.Root("Streets");
            var mats = Mats();
            var phys = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(WorldBuilder.GEN + "/Roads/Asphalt.physicMaterial");
            int nv = 0;
            foreach (var kv in chunks)
            {
                var ch = kv.Value; string name = $"chunk_{kv.Key.Item1}_{kv.Key.Item2}";
                var allV = new List<Vector3>(); var allU0 = new List<Vector2>(); var allU1 = new List<Vector2>(); var allU2 = new List<Vector2>(); var subs = new List<int[]>(); var sm = new List<Material>();
                for (int k = 0; k < NMAT; k++)
                {
                    if (ch.v[k].Count == 0) continue;
                    var map = new Dictionary<(Vector3, Vector2, Vector2, Vector2), int>(); var ix = new int[ch.v[k].Count];
                    for (int i = 0; i < ix.Length; i++)
                    {
                        var key = (ch.v[k][i], ch.u0[k][i], ch.u1[k][i], ch.u2[k][i]);
                        if (!map.TryGetValue(key, out int id)) { id = allV.Count; map[key] = id; allV.Add(key.Item1); allU0.Add(key.Item2); allU1.Add(key.Item3); allU2.Add(key.Item4); }
                        ix[i] = id;
                    }
                    subs.Add(ix); sm.Add(mats[k]);
                }
                var go = new GameObject(name); go.isStatic = true; go.transform.SetParent(root.transform, false);
                if (allV.Count > 0)
                {
                    var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                    mesh.SetVertices(allV); mesh.SetUVs(0, allU0); mesh.SetUVs(1, allU1); mesh.SetUVs(2, allU2);
                    mesh.subMeshCount = subs.Count; for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
                    mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                    Save(mesh, $"{OUT}/{name}.asset"); nv += allV.Count;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = sm.ToArray(); mr.shadowCastingMode = ShadowCastingMode.Off;
                }
                if (ch.cv.Count > 0)
                {
                    var map = new Dictionary<Vector3, int>(); var cv = new List<Vector3>(); var ci = new List<int>();
                    foreach (var p in ch.cv) { if (!map.TryGetValue(p, out int id)) { id = cv.Count; map[p] = id; cv.Add(p); } ci.Add(id); }
                    var cm = new Mesh { name = name + "-col", indexFormat = IndexFormat.UInt32 }; cm.SetVertices(cv); cm.SetTriangles(ci, 0); cm.RecalculateBounds();
                    Save(cm, $"{OUT}/{name}-col.asset");
                    var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = cm; mc.sharedMaterial = phys;
                }
            }
            BuildBarriers(root);
            AssetDatabase.SaveAssets();
            log.Add($"streets: {kmDrawn:F1} km ribbon ({segsDrawn} segments, {sidewalkSegs} sidewalk sides), {discs} junction discs, {nCourts} courts, {barriers.Count} barriers ({barriersDropped} dropped: an outside street continues the stub); {tris} triangles ({dropped} dropped on models), {nv} vertices, {chunks.Count} chunks");
            log.Add(WriteCover()); // (before the ground: WalkTerrain keeps off our streets)
            log.Add(WriteWalkCover());
            log.Add(CarveTerrain());
            log.Add(ClearDetails());
            log.Add(ClearTrees());
            log.Add(RivaBuilder.Build()); // (the riva: on the streets' colliders and their carved ground)
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.Add($"{sw.ElapsedMilliseconds / 1000.0:F1} s");
            var s2 = string.Join("\n", log); Debug.Log("[Magistrala] " + s2); return s2;
        }

        // ------------------------------------------------------------------ terrain under the streets
        static List<ModelJ> wbModels;
        static double Ground(double gx, double gz, double step)
        {
            double h = Tiles.Height(gx, gz); var q = WorldData.ModelTerrain(wbModels, gx, gz, step, h); return q ?? h;
        }
        static string CarveTerrain()
        {
            wbModels = WorldData.Models(-1e9, 1e9);
            int changed = 0, tiles = 0, filled = 0, cut = 0;
            foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.Terrain>(FindObjectsSortMode.None))
            {
                var parts = t.name.Split(' ')[1].Split(','); int tx = int.Parse(parts[0]), tz = int.Parse(parts[1]);
                if (tx < WorldBuilder.TX0 || tx > WorldBuilder.TX1 || tz < WorldBuilder.TZ0 || tz > WorldBuilder.TZ1) continue;
                var td = t.terrainData; int res = td.heightmapResolution; double step = 1024.0 / (res - 1);
                var H = td.GetHeights(0, 0, res, res); int n0 = changed;
                float Get(int r, int c) => WorldBuilder.YMIN + H[r, c] * WorldBuilder.YSIZE;
                void Set(int r, int c, double h) { H[r, c] = Mathf.Clamp01((float)((h - WorldBuilder.YMIN) / WorldBuilder.YSIZE)); changed++; }
                double ox = tx * 1024, oz1 = (tz + 1) * 1024;
                // courts first (flat, blended back over 8 m), then the street corridors (only ever lower the ground)
                foreach (var b in courts)
                {
                    double R = Math.Sqrt(b.hl * b.hl + b.hw * b.hw) + 10;
                    for (int r = Math.Max(0, (int)Math.Floor((oz1 - (b.z + R)) / step)); r <= Math.Min(res - 1, (int)Math.Ceiling((oz1 - (b.z - R)) / step)); r++)
                        for (int c = Math.Max(0, (int)Math.Floor((b.x - R - ox) / step)); c <= Math.Min(res - 1, (int)Math.Ceiling((b.x + R - ox) / step)); c++)
                        {
                            double gx = ox + c * step, gz = oz1 - r * step, dx = gx - b.x, dz = gz - b.z;
                            double lz = Math.Abs(dx * b.fx + dz * b.fz) - b.hl, lx = Math.Abs(dx * b.fz - dz * b.fx) - b.hw;
                            double outd = Math.Max(lx, lz) <= 0 ? 0 : Math.Sqrt(Sq(Math.Max(0, lx)) + Sq(Math.Max(0, lz)));
                            if (outd > 8) continue;
                            double w = outd <= step ? 1 : 1 - (outd - step) / (8 - step); double k = Smooth(w);
                            // (a court beside a model's road: faded out towards its asphalt, from the models' ground — not raised over the
                            // model's road: ducan_stari's flat 8 m blend at the old graph's y had put the grass 0.6 m over s07's road)
                            double sd = WorldData.SurfDist(wbModels, gx, gz); if (sd < 4) k *= Smooth((sd - 1.5) / 2.5);
                            double h = Ground(gx, gz, step); Set(r, c, h + (b.y - .06 - h) * k); // (from the baked ground: a rebuild gives the same court)
                        }
                }
                // outside streets' fills: the ground set to the street within reach, blended back over 6 m (raise or lower)
                foreach (var q in carve)
                {
                    if (q.Length < 8 || q[7] != 1) continue;
                    double reach = q[6], x0 = Math.Min(q[0], q[3]) - reach - 6, x1 = Math.Max(q[0], q[3]) + reach + 6, z0 = Math.Min(q[1], q[4]) - reach - 6, z1 = Math.Max(q[1], q[4]) + reach + 6;
                    if (x1 < ox || x0 > ox + 1024 || z1 < oz1 - 1024 || z0 > oz1) continue;
                    double bx = q[3] - q[0], bz = q[4] - q[1], l2 = bx * bx + bz * bz;
                    for (int r = Math.Max(0, (int)Math.Floor((oz1 - z1) / step)); r <= Math.Min(res - 1, (int)Math.Ceiling((oz1 - z0) / step)); r++)
                        for (int c = Math.Max(0, (int)Math.Floor((x0 - ox) / step)); c <= Math.Min(res - 1, (int)Math.Ceiling((x1 - ox) / step)); c++)
                        {
                            double gx = ox + c * step, gz = oz1 - r * step;
                            double tt = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((gx - q[0]) * bx + (gz - q[1]) * bz) / l2));
                            double dd = Math.Sqrt(Sq(gx - q[0] - bx * tt) + Sq(gz - q[1] - bz * tt)); if (dd > reach + 6) continue;
                            // (blended against the ground as WorldBuilder made it, not the current one: a rebuild gives the same ground)
                            double ry = q[2] + (q[5] - q[2]) * tt - .18, k = dd <= reach ? 1 : Smooth(1 - (dd - reach) / 6);
                            // (the models' ground is WorldBuilder's / ModelTerrain's: faded out towards their paving)
                            double sd = WorldData.SurfDist(wbModels, gx, gz); if (sd < 4) k *= Smooth((sd - 1.5) / 2.5);
                            double h = k >= 1 ? 0 : Ground(gx, gz, step);
                            Set(r, c, h + (ry - h) * k);
                        }
                }
                // our streets' embankments (raise only, before the corridors below lower the ground under every street again): within
                // reach the ground at the street's lower edge − 0.18 m, blended back to the baked ground at ≤ ~1:5 (the 0.9 m collider flaps
                // ended in the air over the ground a 1–1.9 m street rides above: a truck off the road could not climb back). Not inside a
                // court (its own flat ground), faded out at the models' paving, nothing over 3.5 m (a viaduct the graph does not mark).
                const double FILLB = 20;
                foreach (var q in carve)
                {
                    if (q.Length < 8 || q[7] != 2) continue;
                    double reach = q[6], x0 = Math.Min(q[0], q[3]) - reach - FILLB, x1 = Math.Max(q[0], q[3]) + reach + FILLB, z0 = Math.Min(q[1], q[4]) - reach - FILLB, z1 = Math.Max(q[1], q[4]) + reach + FILLB;
                    if (x1 < ox || x0 > ox + 1024 || z1 < oz1 - 1024 || z0 > oz1) continue;
                    double bx = q[3] - q[0], bz = q[4] - q[1], l2 = bx * bx + bz * bz;
                    var nearCourts = courts.Where(b => { double R = Math.Sqrt(b.hl * b.hl + b.hw * b.hw) + 1; return b.x + R > x0 && b.x - R < x1 && b.z + R > z0 && b.z - R < z1; }).ToList();
                    for (int r = Math.Max(0, (int)Math.Floor((oz1 - z1) / step)); r <= Math.Min(res - 1, (int)Math.Ceiling((oz1 - z0) / step)); r++)
                        for (int c = Math.Max(0, (int)Math.Floor((x0 - ox) / step)); c <= Math.Min(res - 1, (int)Math.Ceiling((x1 - ox) / step)); c++)
                        {
                            double gx = ox + c * step, gz = oz1 - r * step;
                            double tt = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((gx - q[0]) * bx + (gz - q[1]) * bz) / l2));
                            double dd = Math.Sqrt(Sq(gx - q[0] - bx * tt) + Sq(gz - q[1] - bz * tt)); if (dd > reach + FILLB) continue;
                            double ry = q[2] + (q[5] - q[2]) * tt - .18; float cur = Get(r, c);
                            if (cur >= ry - .02) continue;
                            double h = Ground(gx, gz, step), rise = ry - h; if (rise <= .02 || rise > 3.5) continue;
                            double B = Math.Max(6, Math.Min(FILLB, rise * 7.5)); if (dd > reach + B) continue;
                            double k = dd <= reach ? 1 : Smooth(1 - (dd - reach) / B);
                            double sd = WorldData.SurfDist(wbModels, gx, gz); if (sd < 4) k *= Smooth((sd - 1.5) / 2.5);
                            double target = h + rise * k; if (target <= cur + .005) continue;
                            if (nearCourts.Any(b => { double dx = gx - b.x, dz = gz - b.z; return Math.Abs(dx * b.fx + dz * b.fz) <= b.hl + 1 && Math.Abs(dx * b.fz - dz * b.fx) <= b.hw + 1; })) continue;
                            Set(r, c, target); filled++;
                        }
                }
                // the street corridors: lowered to the street within reach; beyond it a cut slope (ceste-pregled 2026-11-23: the ground was
                // only lowered inside reach, so a street in a hillside ran between vertical terrain walls up to 6 m high — 51 north, Sućurac
                // 02, 47, s04). The batter: at most CUT m up per m out, over <= BATTER m, only lowering; not within 4 m of a model's paving,
                // never under another street's corridor that is higher than the slope there, not in / beside a court (its own flat ground).
                const double BATTER = 20, CUT = .7;
                // (another street's corridor beside a cut: the slope stops where it would undercut it — a segment grid, 32 m cells)
                var segGrid = new Dictionary<long, List<double[]>>();
                foreach (var q in carve)
                {
                    if (q.Length >= 8) continue;
                    double rr = q[6] + 1;
                    for (int gz2 = (int)Math.Floor((Math.Min(q[1], q[4]) - rr) / 32); gz2 <= (int)Math.Floor((Math.Max(q[1], q[4]) + rr) / 32); gz2++)
                        for (int gx2 = (int)Math.Floor((Math.Min(q[0], q[3]) - rr) / 32); gx2 <= (int)Math.Floor((Math.Max(q[0], q[3]) + rr) / 32); gx2++)
                        { long key = Key(gx2, gz2); if (!segGrid.TryGetValue(key, out var l)) segGrid[key] = l = new List<double[]>(); l.Add(q); }
                }
                bool Undercuts(double[] own, double gx, double gz, double lim)
                {
                    if (!segGrid.TryGetValue(Key((int)Math.Floor(gx / 32), (int)Math.Floor(gz / 32)), out var l)) return false;
                    foreach (var o in l)
                    {
                        if (ReferenceEquals(o, own)) continue;
                        double ox2 = o[3] - o[0], oz2 = o[4] - o[1], ol = ox2 * ox2 + oz2 * oz2;
                        double t2 = ol < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((gx - o[0]) * ox2 + (gz - o[1]) * oz2) / ol));
                        if (Math.Sqrt(Sq(gx - o[0] - ox2 * t2) + Sq(gz - o[1] - oz2 * t2)) > o[6] + 1) continue;
                        if (o[2] + (o[5] - o[2]) * t2 - .18 > lim + .05) return true;
                    }
                    return false;
                }
                foreach (var q in carve)
                {
                    if (q.Length >= 8) continue;
                    double reach = q[6]; // (half width + 1.6 m = the web's hw + step · 0.8 on the 2 m grid)
                    double x0 = Math.Min(q[0], q[3]) - reach - BATTER, x1 = Math.Max(q[0], q[3]) + reach + BATTER, z0 = Math.Min(q[1], q[4]) - reach - BATTER, z1 = Math.Max(q[1], q[4]) + reach + BATTER;
                    if (x1 < ox || x0 > ox + 1024 || z1 < oz1 - 1024 || z0 > oz1) continue;
                    double bx = q[3] - q[0], bz = q[4] - q[1], l2 = bx * bx + bz * bz;
                    var nearCourts = courts.Where(b => { double R = Math.Sqrt(b.hl * b.hl + b.hw * b.hw) + 9; return b.x + R > x0 && b.x - R < x1 && b.z + R > z0 && b.z - R < z1; }).ToList();
                    for (int r = Math.Max(0, (int)Math.Floor((oz1 - z1) / step)); r <= Math.Min(res - 1, (int)Math.Ceiling((oz1 - z0) / step)); r++)
                        for (int c = Math.Max(0, (int)Math.Floor((x0 - ox) / step)); c <= Math.Min(res - 1, (int)Math.Ceiling((x1 - ox) / step)); c++)
                        {
                            double gx = ox + c * step, gz = oz1 - r * step;
                            double tt = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((gx - q[0]) * bx + (gz - q[1]) * bz) / l2));
                            double dd = Math.Sqrt(Sq(gx - q[0] - bx * tt) + Sq(gz - q[1] - bz * tt)); if (dd > reach + BATTER) continue;
                            double ry = q[2] + (q[5] - q[2]) * tt - .18; float cur = Get(r, c);
                            if (dd <= reach) { if (cur > ry) Set(r, c, ry); continue; }
                            double lim = ry + (dd - reach) * CUT; if (cur <= lim + .01) continue;
                            // (the models' ground strip is not drawn: the terrain is its only picture, a cut through it is fine away from the paving)
                            if (WorldData.SurfDist(wbModels, gx, gz) < 4 || Undercuts(q, gx, gz, lim)) continue;
                            if (nearCourts.Any(b => { double dx = gx - b.x, dz = gz - b.z; return Math.Abs(dx * b.fx + dz * b.fz) <= b.hl + 8 && Math.Abs(dx * b.fz - dz * b.fx) <= b.hw + 8; })) continue;
                            Set(r, c, lim); cut++;
                        }
                }
                WalkTerrain(ox, oz1, step, res, Get, Set);
                if (changed > n0) { td.SetHeights(0, 0, H); EditorUtility.SetDirty(td); tiles++; }
            }
            AssetDatabase.SaveAssets();
            return $"terrain: {changed} height samples lowered / flattened on {tiles} tiles ({filled} raised to a street above the ground, {cut} cut slopes beside streets in a hillside)";
        }

        // ------------------------------------------------------------------ cover mask (2 m cells over the area): details / trees stay off
        const double MC = 2; static readonly int MNX = (int)((AX1 - AX0) / MC), MNZ = (int)((AZ1 - AZ0) / MC);
        const string COVER = OUT + "/cover.bytes";
        static byte[] cover;
        static string WriteCover()
        {
            var m = new byte[(MNX * MNZ + 7) / 8]; int n = 0;
            void Mark(double gx, double gz) { int i = (int)Math.Floor((gx - AX0) / MC), j = (int)Math.Floor((gz - AZ0) / MC); if (i < 0 || j < 0 || i >= MNX || j >= MNZ) return; int b = j * MNX + i; if ((m[b >> 3] & (1 << (b & 7))) == 0) { m[b >> 3] |= (byte)(1 << (b & 7)); n++; } }
            foreach (var q in carve)
            {
                double reach = q[6] + .3, bx = q[3] - q[0], bz = q[4] - q[1], l2 = bx * bx + bz * bz;
                for (double gz = Math.Floor((Math.Min(q[1], q[4]) - reach) / MC) * MC; gz <= Math.Max(q[1], q[4]) + reach; gz += MC)
                    for (double gx = Math.Floor((Math.Min(q[0], q[3]) - reach) / MC) * MC; gx <= Math.Max(q[0], q[3]) + reach; gx += MC)
                    {
                        double cx = gx + MC / 2, cz = gz + MC / 2, tt = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((cx - q[0]) * bx + (cz - q[1]) * bz) / l2));
                        if (Math.Sqrt(Sq(cx - q[0] - bx * tt) + Sq(cz - q[1] - bz * tt)) <= reach + MC * .71) Mark(cx, cz);
                    }
            }
            foreach (var b in courts)
            {
                double R = Math.Sqrt(b.hl * b.hl + b.hw * b.hw) + 1;
                for (double gz = b.z - R; gz <= b.z + R; gz += MC) for (double gx = b.x - R; gx <= b.x + R; gx += MC)
                    {
                        double dx = gx - b.x, dz = gz - b.z;
                        if (Math.Abs(dx * b.fx + dz * b.fz) <= b.hl + 1 && Math.Abs(dx * b.fz - dz * b.fx) <= b.hw + 1) Mark(gx, gz);
                    }
            }
            File.WriteAllBytes(COVER, m); AssetDatabase.ImportAsset(COVER); cover = m;
            return $"cover mask: {n} cells ({n * MC * MC / 1e6:F2} km²)";
        }
        /// street / court within `r` m of game (x, z) (Assets/Generated/Streets/cover.bytes, false when no streets were built)
        /// (the model-road footways count only on their own footprint, not within `r`: the olives beside them stay)
        public static bool Covered(double gx, double gz, double r = 0) => CoveredMain(gx, gz, r) || WalkCovered(gx, gz);
        static bool CoveredMain(double gx, double gz, double r)
        {
            if (cover == null) { if (!File.Exists(COVER)) return false; cover = File.ReadAllBytes(COVER); }
            int k = (int)Math.Ceiling(r / MC);
            int i0 = (int)Math.Floor((gx - AX0) / MC), j0 = (int)Math.Floor((gz - AZ0) / MC);
            for (int j = j0 - k; j <= j0 + k; j++)
                for (int i = i0 - k; i <= i0 + k; i++)
                {
                    if (i < 0 || j < 0 || i >= MNX || j >= MNZ) continue;
                    int b = j * MNX + i; if ((cover[b >> 3] & (1 << (b & 7))) != 0) return true;
                }
            return false;
        }
        public static void ResetCover() { cover = null; walkCover = null; }

        static string ClearDetails()
        {
            int cleared = 0;
            foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.Terrain>(FindObjectsSortMode.None))
            {
                var td = t.terrainData; int R = td.detailResolution; if (R <= 0 || td.detailPrototypes.Length == 0) continue;
                var parts = t.name.Split(' ')[1].Split(','); int tx = int.Parse(parts[0]), tz = int.Parse(parts[1]);
                double cs = 1024.0 / R; bool dirty = false;
                for (int l = 0; l < td.detailPrototypes.Length; l++)
                {
                    var D = td.GetDetailLayer(0, 0, R, R, l); bool ch = false;
                    for (int r = 0; r < R; r++) for (int c = 0; c < R; c++)
                        {
                            if (D[r, c] == 0) continue;
                            double gx = tx * 1024 + (c + .5) * cs, gz = (tz + 1) * 1024 - (r + .5) * cs;
                            if (Covered(gx, gz, 1)) { D[r, c] = 0; ch = true; cleared++; }
                        }
                    if (ch) { td.SetDetailLayer(0, 0, l, D); dirty = true; }
                }
                if (dirty) EditorUtility.SetDirty(td);
            }
            AssetDatabase.SaveAssets();
            return $"terrain details: {cleared} cells cleared";
        }
        static string ClearTrees()
        {
            string p = WorldBuilder.GEN + "/trees.bytes"; if (!File.Exists(p)) return "trees: none";
            var b = File.ReadAllBytes(p); var outB = new List<byte>(b.Length); int rec = 24, n = 0;
            for (int o = 0; o + rec <= b.Length; o += rec)
            {
                var g = W.G(new Vector3(BitConverter.ToSingle(b, o), 0, BitConverter.ToSingle(b, o + 8)));
                if (Covered(g.x, g.y, 1.5)) { n++; continue; }
                for (int k = 0; k < rec; k++) outB.Add(b[o + k]);
            }
            if (n > 0) { File.WriteAllBytes(p, outB.ToArray()); AssetDatabase.ImportAsset(p); }
            return $"trees: {n} removed from the streets";
        }
    }
}
