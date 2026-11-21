// Menu Magistrala/Igra: podaci — the gameplay data of the Kaštela stretch (Assets/Generated/Game/Resources/MagistralaGame.asset):
// stops, the drivable road graph and the stop-to-stop road distances, read from the web game (BalkanDriver repo, read-only).
// The graph is graph.json with the junction override layers applied in the web loader's order (junction-overrides.json, then
// every file of junction-overrides.index.json; entries merged by id, a later file wins), like
// client/src/world2/format/junctionOverrides.ts: per entry the anchor signature (arm count) is checked, then node / edge
// patches, addNodes / addEdges, removeEdges. road-closures.json marks closed edges (no routing through them).
// Also runs before a player build when the asset is missing.
using System;
using System.Collections.Generic;
using System.IO;
using Magistrala.Game;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public partial class GameDataBuilder : IPreprocessBuildWithReport
    {
        const string OutDir = "Assets/Generated/Game/Resources";
        const string OutPath = OutDir + "/" + GameWorld.ResourcePath + ".asset";
        // stops: the Unity world (Lukšić → Sućurac and a margin), game coordinates; the graph is kept whole (the GPS may lead
        // out of the built world, e.g. the D8 westbound from Pumpa Kaštela turns round only past Trogir)
        const double MinX = WorldData.MIN_X, MaxX = 300, MinZ = -7000, MaxZ = -3000; // (mag-11-19: Štafilić)

        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) { if (!File.Exists(OutPath)) Build(); }

        class N { public long id; public double x, y, z; }
        class E { public long id, a, b; public bool oneway; public double length; public string name, rf; public List<double[]> pts; public int lanes, maxspeed; public string spur, type; public bool? closedOwn; }

        [MenuItem("Magistrala/Igra: podaci (stajališta, graf cesta)")]
        public static void Build()
        {
            var g = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "graph.json")));
            var nodes = new Dictionary<long, N>(); var edges = new Dictionary<long, E>();
            foreach (var o in MiniJson.L(g["nodes"])) { var n = ReadNode(MiniJson.O(o)); nodes[n.id] = n; }
            foreach (var o in MiniJson.L(g["edges"])) { var e = ReadEdge(MiniJson.O(o), null); edges[e.id] = e; }

            // ---- junction overrides, in the loader's order, merged by id
            var files = new List<string> { "junction-overrides.json" };
            var idx = W.REGION + "junction-overrides.index.json";
            if (File.Exists(idx)) foreach (var f in MiniJson.L(MiniJson.Parse(File.ReadAllText(idx)))) files.Add((string)f);
            var order = new List<string>(); var byId = new Dictionary<string, Dictionary<string, object>>();
            foreach (var f in files)
            {
                var path = W.REGION + f; if (!File.Exists(path)) continue;
                foreach (var eo in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(path)))["overrides"]))
                {
                    var en = MiniJson.O(eo); var id = (string)en["id"];
                    if (!byId.ContainsKey(id)) order.Add(id);
                    byId[id] = en;
                }
            }
            int applied = 0, skipped = 0;
            foreach (var id in order) { if (Apply(byId[id], nodes, edges)) applied++; else skipped++; }

            // ---- closures
            var closed = new HashSet<long>();
            var cf = W.REGION + "road-closures.json";
            if (File.Exists(cf)) foreach (var c in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(cf)))["closedEdgeIds"])) closed.Add((long)MiniJson.D(c));
            // (graphIndex.ts: an edge's own `closed` wins over the file — the models' closed arms, mag-11-20 c09's D8 to Trogir)
            foreach (var e in edges.Values) if (e.closedOwn == true) closed.Add(e.id); else if (e.closedOwn == false) closed.Remove(e.id);

            // ---- streets outside the models (OutsideStreets, the same network StreetsBuilder draws): new nodes / edges, graph edges
            // split where a new street meets them (the split-off part keeps the closure of its edge)
            var net = StreetsBuilder.OutsideNet(); var splitOf = new Dictionary<long, long>(); long sid = OutsideStreets.SPLIT0 + 500_000;
            foreach (var nn in net.nodes) nodes[nn.id] = new N { id = nn.id, x = nn.x, y = nn.y, z = nn.z };
            foreach (var sp in net.splits)
            {
                var nn = nodes[sp.node]; E part = null; double bd = double.MaxValue;
                foreach (var e in edges.Values)
                {
                    if (e.id != sp.edge && !(splitOf.TryGetValue(e.id, out long o) && o == sp.edge)) continue;
                    double d = StreetsBuilder.DistTo(e.pts, nn.x, nn.z); if (d < bd) { bd = d; part = e; }
                }
                if (part == null) continue;
                var (A, B) = StreetsBuilder.SplitAt(part.pts, nn.x, nn.y, nn.z);
                var e2 = new E { id = sid++, a = nn.id, b = part.b, oneway = part.oneway, name = part.name, rf = part.rf, lanes = part.lanes, maxspeed = part.maxspeed, type = part.type, spur = part.spur, pts = B };
                splitOf[e2.id] = sp.edge; part.b = nn.id; part.pts = A;
                part.length = PolyLen(A); e2.length = PolyLen(B); edges[e2.id] = e2;
            }
            foreach (var st in net.streets) edges[st.id] = new E { id = st.id, a = st.a, b = st.b, oneway = st.oneway, name = st.name, lanes = st.lanes, maxspeed = st.maxspeed, type = st.cls.EndsWith("_link") ? "ramp" : st.cls, pts = st.pts, length = PolyLen(st.pts) };
            foreach (var kv in splitOf) if (closed.Contains(kv.Value)) closed.Add(kv.Key);
            Debug.Log("[igra] " + (net.log.Count > 0 ? net.log[0] : "outside streets: none"));

            // ---- mag-11-19 delivery points: each snapped onto its street (the edge split there, GameDataBuilder.Tours.cs)
            var pk = SnapPackage(nodes, edges, closed, out var tourDefs);

            // ---- stops whose spur / access edge a model override removed: re-joined to the nearest open street
            var reAccess = ReattachStops(nodes, edges, closed);

            // ---- clip to the Unity world, index
            bool In(N n) => true;
            var nIndex = new Dictionary<long, int>(); var nList = new List<Vector3>();
            foreach (var n in nodes.Values) if (In(n)) { nIndex[n.id] = nList.Count; nList.Add(W.U(n.x, n.y, n.z)); }
            var eList = new List<GEdge>(); var pts = new List<Vector3>(); var limitStats = new Dictionary<string, double>();
            foreach (var e in edges.Values)
            {
                if (!nIndex.TryGetValue(e.a, out int ia) || !nIndex.TryGetValue(e.b, out int ib) || e.pts == null || e.pts.Count < 2) continue;
                var ge = new GEdge { a = ia, b = ib, oneway = e.oneway, closed = closed.Contains(e.id), length = (float)e.length, name = e.name ?? "", road = RoadRef(e.rf), p0 = pts.Count, pn = e.pts.Count, lanes = (byte)Math.Max(1, e.lanes), limit = (byte)SpeedLimit(e, limitStats) };
                foreach (var p in e.pts) pts.Add(W.U(p[0], p[1], p[2]));
                eList.Add(ge);
            }

            // ---- stops in the world
            var stops = new List<GStop>();
            foreach (var so in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "stops.json")))["stops"]))
            {
                var s = MiniJson.O(so); string kind = (string)s["kind"];
                double x = MiniJson.D(s["x"]), z = MiniJson.D(s["z"]);
                if (kind == "radar" || x < MinX || x > MaxX || z < MinZ || z > MaxZ) continue;
                var st = new GStop { id = (string)s["id"], name = (string)s["name"], kind = kind, pos = W.U(x, MiniJson.D(MiniJson.G(s, "y") ?? 0.0), z) };
                var bays = MiniJson.L(MiniJson.G(s, "bays"));
                var bay = bays != null && bays.Count > 0 ? MiniJson.O(bays[(bays.Count - 1) / 2]) : s;
                double yaw = MiniJson.D(bay["yaw"]);
                st.bay = W.U(MiniJson.D(bay["x"]), MiniJson.D(MiniJson.G(bay, "y") ?? s["y"]), MiniJson.D(bay["z"]));
                // engine.ts: ramp direction (rx, rz) = (-sin yaw, -cos yaw) in game x / z → Unity (x, -z)
                st.bayDir = new Vector2(-(float)Math.Sin(yaw), (float)Math.Cos(yaw));
                st.bayHl = MiniJson.G(bay, "hl") is object hl ? MiniJson.F(hl) : 9.5f;
                st.bayHw = MiniJson.G(bay, "hw") is object hw ? MiniJson.F(hw) : 2.6f;
                if (MiniJson.G(s, "spur") is Dictionary<string, object> sp && nIndex.TryGetValue((long)MiniJson.D(sp["padNode"]), out int pn)) st.padNode = pn;
                if (MiniJson.G(s, "access") is Dictionary<string, object> ac && ac.ContainsKey("node") && nIndex.TryGetValue((long)MiniJson.D(ac["node"]), out int an)) st.accessNode = an;
                if (reAccess.TryGetValue(st.id, out long ran) && nIndex.TryGetValue(ran, out int rai)) st.accessNode = rai;
                stops.Add(st);
            }

            // ---- the package's points as stops: only those the GPS reaches from the Lukšić warehouse and back
            AddPackageStops(pk, stops, nIndex, eList, pts, nList.Count, tourDefs);

            // ---- distances
            var dist = new float[stops.Count * stops.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var table = MiniJson.O(MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "distances.json")))["table"]);
            for (int i = 0; i < stops.Count; i++)
            {
                var row = MiniJson.O(MiniJson.G(table, stops[i].id)); if (row == null) continue;
                for (int j = 0; j < stops.Count; j++) if (MiniJson.G(row, stops[j].id) is object v) dist[i * stops.Count + j] = MiniJson.F(v);
            }
            // pairs distances.json does not have (every pair with a package point): our graph's road metres (Dijkstra pad → pad)
            FillDistances(stops, eList, pts, nList.Count, dist);

            Directory.CreateDirectory(OutDir);
            var asset = AssetDatabase.LoadAssetAtPath<GameWorld>(OutPath);
            bool fresh = !asset; if (fresh) asset = ScriptableObject.CreateInstance<GameWorld>();
            asset.stops = stops.ToArray(); asset.nodes = nList.ToArray(); asset.edges = eList.ToArray(); asset.pts = pts.ToArray(); asset.dist = dist; asset.tours = tourDefs.ToArray();
            BakePlaces(asset);
            BakeSea(asset);
            var lsb = new System.Text.StringBuilder(); double ltot = 0; foreach (var v in limitStats.Values) ltot += v;
            foreach (var kv in limitStats) lsb.Append($" {kv.Key} {kv.Value / 1000:0.0} km ({kv.Value / Math.Max(1, ltot) * 100:0} %);");
            Debug.Log("[igra] speed limits by source:" + lsb);
            asset.source = $"graph.json + {applied} override entries ({skipped} skipped), {closed.Count} closed edge ids, {net.streets.Count} outside streets ({net.splits.Count} splits), {DateTime.Now:yyyy-MM-dd HH:mm}";
            if (fresh) AssetDatabase.CreateAsset(asset, OutPath); else EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Debug.Log($"[igra] {OutPath}: {stops.Count} stops, {nList.Count} nodes, {eList.Count} edges ({eList.FindAll(e => e.closed).Count} closed), {pts.Count} points; {asset.source}");
        }

        static N ReadNode(Dictionary<string, object> o, N into = null)
        {
            var n = into ?? new N();
            if (o.ContainsKey("id")) n.id = (long)MiniJson.D(o["id"]);
            if (o.ContainsKey("x")) n.x = MiniJson.D(o["x"]);
            if (o.ContainsKey("y")) n.y = MiniJson.D(o["y"]);
            if (o.ContainsKey("z")) n.z = MiniJson.D(o["z"]);
            return n;
        }

        static E ReadEdge(Dictionary<string, object> o, E into)
        {
            var e = into ?? new E();
            if (o.ContainsKey("id")) e.id = (long)MiniJson.D(o["id"]);
            if (o.ContainsKey("a")) e.a = (long)MiniJson.D(o["a"]);
            if (o.ContainsKey("b")) e.b = (long)MiniJson.D(o["b"]);
            if (o.ContainsKey("oneway")) e.oneway = o["oneway"] is bool b && b;
            if (o.ContainsKey("length")) e.length = MiniJson.D(o["length"]);
            if (o.ContainsKey("name")) e.name = o["name"] as string;
            if (o.ContainsKey("ref")) e.rf = o["ref"] as string;
            if (o.ContainsKey("lanes")) e.lanes = (int)MiniJson.D(o["lanes"]);
            if (o.ContainsKey("maxspeed") && o["maxspeed"] != null) e.maxspeed = (int)MiniJson.D(o["maxspeed"]);
            if (o.ContainsKey("type")) e.type = o["type"] as string;
            if (o.ContainsKey("spur")) e.spur = o["spur"] as string;
            if (o.ContainsKey("closed")) e.closedOwn = o["closed"] is bool cl ? cl : (bool?)null;
            if (o.ContainsKey("points"))
            {
                e.pts = new List<double[]>();
                foreach (var p in MiniJson.L(o["points"])) { var q = MiniJson.L(p); e.pts.Add(new[] { MiniJson.D(q[0]), MiniJson.D(q[1]), MiniJson.D(q[2]) }); }
                if (!o.ContainsKey("length")) { double L = 0; for (int i = 1; i < e.pts.Count; i++) L += Math.Sqrt(Sq(e.pts[i][0] - e.pts[i - 1][0]) + Sq(e.pts[i][1] - e.pts[i - 1][1]) + Sq(e.pts[i][2] - e.pts[i - 1][2])); e.length = L; }
            }
            return e;
        }
        static double Sq(double v) => v * v;
        static double PolyLen(List<double[]> p) { double L = 0; for (int i = 1; i < p.Count; i++) L += Math.Sqrt(Sq(p[i][0] - p[i - 1][0]) + Sq(p[i][1] - p[i - 1][1]) + Sq(p[i][2] - p[i - 1][2])); return L; }

        /// one override entry, atomically (junctionOverrides.ts applyJunctionOverrides): false = ignored (stale signature / missing ids)
        static bool Apply(Dictionary<string, object> entry, Dictionary<long, N> nodes, Dictionary<long, E> edges)
        {
            var at = MiniJson.L(MiniJson.G(entry, "at")); var ov = MiniJson.O(MiniJson.G(entry, "overrides"));
            if (ov == null) return false;
            // anchor: the node of a graph-node-<n> id, else the nearest node within 5 m of `at`
            N anchor = null; string id = (string)entry["id"];
            if (id.StartsWith("graph-node-") && long.TryParse(id.Substring(11).Split('#')[0], out long nid)) nodes.TryGetValue(nid, out anchor);
            if (anchor == null && at != null)
            {
                double ax = MiniJson.D(at[0]), az = MiniJson.D(at[1]), bd = 25;
                foreach (var n in nodes.Values) { double d = Sq(n.x - ax) + Sq(n.z - az); if (d < bd) { bd = d; anchor = n; } }
            }
            if (anchor == null) { Debug.LogWarning($"[igra] override {id}: no anchor node"); return false; }
            if (MiniJson.O(MiniJson.G(entry, "signature")) is Dictionary<string, object> sig && sig.ContainsKey("arms"))
            {
                int arms = 0; foreach (var e in edges.Values) if (e.a == anchor.id || e.b == anchor.id) arms++;
                if (arms != (int)MiniJson.D(sig["arms"])) { Debug.LogWarning($"[igra] override {id}: stale signature ({arms} arms, wants {sig["arms"]})"); return false; }
            }
            // validate first (atomic)
            foreach (var o in MiniJson.L(MiniJson.G(ov, "nodes")) ?? new List<object>()) if (!nodes.ContainsKey((long)MiniJson.D(MiniJson.O(o)["id"]))) return Fail(id, "patched node missing");
            foreach (var o in MiniJson.L(MiniJson.G(ov, "edges")) ?? new List<object>()) if (!edges.ContainsKey((long)MiniJson.D(MiniJson.O(o)["id"]))) return Fail(id, "patched edge missing");
            foreach (var o in MiniJson.L(MiniJson.G(ov, "removeEdges")) ?? new List<object>()) if (!edges.ContainsKey((long)MiniJson.D(o))) return Fail(id, "removed edge missing");
            foreach (var o in MiniJson.L(MiniJson.G(ov, "addNodes")) ?? new List<object>()) if (nodes.ContainsKey((long)MiniJson.D(MiniJson.O(o)["id"]))) return Fail(id, "added node exists");
            foreach (var o in MiniJson.L(MiniJson.G(ov, "addEdges")) ?? new List<object>()) if (edges.ContainsKey((long)MiniJson.D(MiniJson.O(o)["id"]))) return Fail(id, "added edge exists");
            // apply
            foreach (var o in MiniJson.L(MiniJson.G(ov, "nodes")) ?? new List<object>()) { var d = MiniJson.O(o); ReadNode(d, nodes[(long)MiniJson.D(d["id"])]); }
            foreach (var o in MiniJson.L(MiniJson.G(ov, "addNodes")) ?? new List<object>()) { var n = ReadNode(MiniJson.O(o)); nodes[n.id] = n; }
            foreach (var o in MiniJson.L(MiniJson.G(ov, "edges")) ?? new List<object>()) { var d = MiniJson.O(o); ReadEdge(d, edges[(long)MiniJson.D(d["id"])]); }
            foreach (var o in MiniJson.L(MiniJson.G(ov, "addEdges")) ?? new List<object>()) { var e = ReadEdge(MiniJson.O(o), null); edges[e.id] = e; }
            foreach (var o in MiniJson.L(MiniJson.G(ov, "removeEdges")) ?? new List<object>()) edges.Remove((long)MiniJson.D(o));
            return true;
        }
        static bool Fail(string id, string why) { Debug.LogWarning($"[igra] override {id}: {why}"); return false; }

        // ------------------------------------------------------------------ stops cut off by the models
        const long ReNode0 = 9_700_000, ReEdge0 = 9_750_000;
        const double ReMax = 40;
        /// A model override (mag-11-19 tiles) replaces the graph edges around it, also the spur and access edge of a stop of stops.json
        /// whose street it rebuilt: ducan_stari's Fuležina (3514 / 3515 / 3516, s07, its stub now a closed side street), dvor_basica /
        /// kastela_kafana (s13), banka_stari. Such a stop's pad node is left without edges: no GPS route to or from it, no job. The pad
        /// is joined again to the nearest open street that the warehouse reaches and is reached from (the edge split there, a straight
        /// spur of the stop's name); StreetsBuilder.BuildCourts draws its court up to that street's paving. A stop without a spur only
        /// gets the new access node. Returns stop id → new access node.
        static Dictionary<string, long> ReattachStops(Dictionary<long, N> nodes, Dictionary<long, E> edges, HashSet<long> closed)
        {
            var res = new Dictionary<string, long>();
            var stopsJ = MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "stops.json")))["stops"]);
            var deg = new Dictionary<long, int>();
            foreach (var e in edges.Values) { deg.TryGetValue(e.a, out int da); deg[e.a] = da + 1; deg.TryGetValue(e.b, out int db); deg[e.b] = db + 1; }
            bool Has(long n) => deg.TryGetValue(n, out int d) && d > 0;
            // the warehouse's part of the open graph, both ways
            long home = -1;
            foreach (var so in stopsJ) { var s = MiniJson.O(so); if ((string)s["id"] == MagistralaGame.Warehouse && MiniJson.G(s, "spur") is Dictionary<string, object> sp) home = (long)MiniJson.D(sp["padNode"]); }
            if (home < 0 || !Has(home)) { Debug.LogWarning("[igra] reattach: no warehouse pad in the graph"); return res; }
            HashSet<long> Reach(bool back)
            {
                var adj = new Dictionary<long, List<long>>();
                void Add(long u, long v) { if (!adj.TryGetValue(u, out var l)) adj[u] = l = new List<long>(); l.Add(v); }
                foreach (var e in edges.Values)
                {
                    if (closed.Contains(e.id) || e.pts == null || e.pts.Count < 2) continue;
                    if (!back) Add(e.a, e.b); else Add(e.b, e.a);
                    if (!e.oneway) { if (!back) Add(e.b, e.a); else Add(e.a, e.b); }
                }
                var seen = new HashSet<long> { home }; var st = new Stack<long>(); st.Push(home);
                while (st.Count > 0) { var u = st.Pop(); if (adj.TryGetValue(u, out var l)) foreach (var v in l) if (seen.Add(v)) st.Push(v); }
                return seen;
            }
            var fwd = Reach(false); var bwd = Reach(true);
            int k = 0;
            foreach (var so in stopsJ)
            {
                var s = MiniJson.O(so); string id = (string)s["id"], kind = (string)s["kind"];
                double sx = MiniJson.D(s["x"]), sz = MiniJson.D(s["z"]);
                if (kind == "radar" || sx < MinX || sx > MaxX || sz < MinZ || sz > MaxZ) continue;
                long pad = MiniJson.G(s, "spur") is Dictionary<string, object> sp ? (long)MiniJson.D(sp["padNode"]) : -1;
                long acc = MiniJson.G(s, "access") is Dictionary<string, object> ac && ac.ContainsKey("node") ? (long)MiniJson.D(ac["node"]) : -1;
                if (pad >= 0 ? Has(pad) || !nodes.ContainsKey(pad) : acc < 0 || Has(acc)) continue;
                // the nearest point of an open, ordinary street both ends of which the warehouse reaches and is reached from
                double ox = pad >= 0 ? nodes[pad].x : sx, oz = pad >= 0 ? nodes[pad].z : sz;
                E best = null; double bd = double.MaxValue; int bi = 0; double bt = 0, bs = 0;
                foreach (var e in edges.Values)
                {
                    if (closed.Contains(e.id) || e.pts == null || e.pts.Count < 2 || e.spur != null || e.type == "motorway" || e.type == "ramp") continue;
                    if (!(fwd.Contains(e.a) && bwd.Contains(e.a) && fwd.Contains(e.b) && bwd.Contains(e.b))) continue;
                    double pen = (e.name ?? "").StartsWith("D8") || (e.oneway && e.lanes >= 2) ? D8Penalty : 0, acc0 = 0;
                    for (int i = 0; i + 1 < e.pts.Count; i++)
                    {
                        var a = e.pts[i]; var b = e.pts[i + 1]; double bx = b[0] - a[0], bz = b[2] - a[2], l2 = bx * bx + bz * bz, L = Math.Sqrt(l2);
                        double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((ox - a[0]) * bx + (oz - a[2]) * bz) / l2));
                        double d = Math.Sqrt(Sq(ox - a[0] - bx * t) + Sq(oz - a[2] - bz * t)) + pen;
                        if (d < bd) { bd = d; best = e; bi = i; bt = t; bs = acc0 + L * t; }
                        acc0 += L;
                    }
                }
                if (best == null || bd > ReMax) { Debug.LogWarning($"[igra] reattach {id}: no open street within {ReMax:0} m (nearest {bd:0} m)"); continue; }
                var A = best.pts[bi]; var B = best.pts[bi + 1];
                double px = A[0] + (B[0] - A[0]) * bt, py = A[1] + (B[1] - A[1]) * bt, pz = A[2] + (B[2] - A[2]) * bt;
                long nid; double len = PolyLen(best.pts);
                if (bs < 2.5) nid = best.a;
                else if (len - bs < 2.5) nid = best.b;
                else
                {
                    nid = ReNode0 + k; nodes[nid] = new N { id = nid, x = px, y = py, z = pz };
                    var (P1, P2) = StreetsBuilder.SplitAt(best.pts, px, py, pz);
                    var e2 = new E { id = ReEdge0 + k, a = nid, b = best.b, oneway = best.oneway, name = best.name, rf = best.rf, lanes = best.lanes, maxspeed = best.maxspeed, type = best.type, spur = best.spur, pts = P2, length = PolyLen(P2) };
                    best.b = nid; best.pts = P1; best.length = PolyLen(P1); edges[e2.id] = e2;
                    if (closed.Contains(best.id)) closed.Add(e2.id);
                }
                k++;
                res[id] = nid;
                string how = "access node only";
                if (pad >= 0)
                {
                    var pn = nodes[pad]; var an = nodes[nid];
                    var sp2 = new List<double[]> { new[] { an.x, an.y, an.z }, new[] { pn.x, pn.y, pn.z } };
                    edges[ReEdge0 + 500 + k] = new E { id = ReEdge0 + 500 + k, a = nid, b = pad, name = (string)s["name"], type = "service", spur = id, lanes = 1, maxspeed = 20, pts = sp2, length = PolyLen(sp2) };
                    how = $"spur {PolyLen(sp2):0.0} m to the pad";
                }
                Debug.Log($"[igra] reattach {id}: its spur / access edge is gone (model override) → {best.name} ({best.id}) {bd:0.0} m away, {how}");
            }
            return res;
        }
    }
}
