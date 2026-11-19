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
    public class GameDataBuilder : IPreprocessBuildWithReport
    {
        const string OutDir = "Assets/Generated/Game/Resources";
        const string OutPath = OutDir + "/" + GameWorld.ResourcePath + ".asset";
        // stops: the Unity world (Lukšić → Sućurac and a margin), game coordinates; the graph is kept whole (the GPS may lead
        // out of the built world, e.g. the D8 westbound from Pumpa Kaštela turns round only past Trogir)
        const double MinX = WorldData.MIN_X, MaxX = 300, MinZ = -7000, MaxZ = -3000; // (mag-11-19: Štafilić)

        public int callbackOrder => 0;
        public void OnPreprocessBuild(BuildReport report) { if (!File.Exists(OutPath)) Build(); }

        class N { public long id; public double x, y, z; }
        class E { public long id, a, b; public bool oneway; public double length; public string name; public List<double[]> pts; public int lanes; public string spur; }

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

            // ---- clip to the Unity world, index
            bool In(N n) => true;
            var nIndex = new Dictionary<long, int>(); var nList = new List<Vector3>();
            foreach (var n in nodes.Values) if (In(n)) { nIndex[n.id] = nList.Count; nList.Add(W.U(n.x, n.y, n.z)); }
            var eList = new List<GEdge>(); var pts = new List<Vector3>();
            foreach (var e in edges.Values)
            {
                if (!nIndex.TryGetValue(e.a, out int ia) || !nIndex.TryGetValue(e.b, out int ib) || e.pts == null || e.pts.Count < 2) continue;
                var ge = new GEdge { a = ia, b = ib, oneway = e.oneway, closed = closed.Contains(e.id), length = (float)e.length, name = e.name ?? "", p0 = pts.Count, pn = e.pts.Count, lanes = (byte)Math.Max(1, e.lanes) };
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
                stops.Add(st);
            }

            // ---- distances
            var dist = new float[stops.Count * stops.Count];
            for (int i = 0; i < dist.Length; i++) dist[i] = -1;
            var table = MiniJson.O(MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "distances.json")))["table"]);
            for (int i = 0; i < stops.Count; i++)
            {
                var row = MiniJson.O(MiniJson.G(table, stops[i].id)); if (row == null) continue;
                for (int j = 0; j < stops.Count; j++) if (MiniJson.G(row, stops[j].id) is object v) dist[i * stops.Count + j] = MiniJson.F(v);
            }

            Directory.CreateDirectory(OutDir);
            var asset = AssetDatabase.LoadAssetAtPath<GameWorld>(OutPath);
            bool fresh = !asset; if (fresh) asset = ScriptableObject.CreateInstance<GameWorld>();
            asset.stops = stops.ToArray(); asset.nodes = nList.ToArray(); asset.edges = eList.ToArray(); asset.pts = pts.ToArray(); asset.dist = dist;
            asset.source = $"graph.json + {applied} override entries ({skipped} skipped), {closed.Count} closed edge ids, {DateTime.Now:yyyy-MM-dd HH:mm}";
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
            if (o.ContainsKey("lanes")) e.lanes = (int)MiniJson.D(o["lanes"]);
            if (o.ContainsKey("spur")) e.spur = o["spur"] as string;
            if (o.ContainsKey("points"))
            {
                e.pts = new List<double[]>();
                foreach (var p in MiniJson.L(o["points"])) { var q = MiniJson.L(p); e.pts.Add(new[] { MiniJson.D(q[0]), MiniJson.D(q[1]), MiniJson.D(q[2]) }); }
                if (!o.ContainsKey("length")) { double L = 0; for (int i = 1; i < e.pts.Count; i++) L += Math.Sqrt(Sq(e.pts[i][0] - e.pts[i - 1][0]) + Sq(e.pts[i][1] - e.pts[i - 1][1]) + Sq(e.pts[i][2] - e.pts[i - 1][2])); e.length = L; }
            }
            return e;
        }
        static double Sq(double v) => v * v;

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
    }
}
