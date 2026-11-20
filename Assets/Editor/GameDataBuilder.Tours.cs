// Menu Magistrala/Igra: podaci, part 2 — Marko's delivery points and tours (package mag-11-19, ~/Documents/mag-11-19/world_1to1.json,
// read-only): checkpoints / tours, Kaštela only.
//   frame     package (x east, z south, origin Štafilić) → game: the affine fit of paket-approaches.json (alignment.transform), as
//             OutsideStreets / the model importer; game → Unity: W.U
//   kept      points inside the streets' area (StreetsBuilder AX0..AX1, AZ0..AZ1: Štafilić → Sućurac) that have tours; dropped with
//             the reason in the log (and Logs/ture-uvoz.txt): outside the world (Split / Solin), no tours (bases, rest stops), no
//             street within 45 m, the GPS finds no way from the Lukšić warehouse there or back
//   snapped   the checkpoint's `stop` onto the nearest open street (the D8 / dual carriageways count 40 m further: a café is not
//             served from the expressway), the edge split there (new node = the GPS target); the delivery bay lies along the street
//   names     Assets/Editor/imena-firmi.json: real → fictional (no real company names in the game); a business point no key hits
//             gets a generic name by its kind
//   vehicle   maxVehicle "mali" (van or small truck) on the waterfront points ("(riva)" in the package: README "meant for the small
//             truck or a van", BILJESKE "velikim kamionom ne možeš na rivu"); stored only, not enforced; the Atego counts as allowed on the riva (Marko 2026-11-20), the rule is for bigger trucks later
//   distances every pair with a package point: our graph's road metres (Dijkstra pad → pad, one-ways, closed edges skipped)
// The tours themselves (which points, how many stops) are put together in the game (MagistralaGame.Tours.cs).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Magistrala.Game;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public partial class GameDataBuilder
    {
        const string PKG = "/Users/macair/Documents/mag-11-19/world_1to1.json";
        const string NAMES = "Assets/Editor/imena-firmi.json";
        const string REPORT = "Logs/ture-uvoz.txt";
        const double SnapMax = 45, D8Penalty = 40;
        const long PkNode0 = 9_800_000, PkEdge0 = 9_850_000;

        class PK
        {
            public int id; public string name, real, kind, role; public double ox, oz, sx, sz; public List<string> tours = new List<string>();
            public long node = -1; public double bx, by, bz, tx, tz, snapD; public string edgeName, why; public bool riva;
        }

        static readonly List<string> report = new List<string>();

        static List<PK> SnapPackage(Dictionary<long, N> nodes, Dictionary<long, E> edges, HashSet<long> closed, out List<GTour> tourDefs)
        {
            report.Clear(); tourDefs = new List<GTour>();
            var res = new List<PK>();
            if (!File.Exists(PKG)) { Debug.LogWarning($"[igra] {PKG} missing: no delivery points"); return res; }
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(PKG)));
            var pa = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "paket-approaches.json")));
            var T = MiniJson.O(MiniJson.G(MiniJson.O(pa["alignment"]), "transform") ?? pa["transform"]);
            double Ta = MiniJson.D(T["a"]), Tb = MiniJson.D(T["b"]), Tc = MiniJson.D(T["c"]), Td = MiniJson.D(T["d"]), Ttx = MiniJson.D(T["tx"]), Ttz = MiniJson.D(T["tz"]);
            (double, double) G(List<object> p) { double x = MiniJson.D(p[0]), z = MiniJson.D(p[1]); return (Ta * x + Tb * z + Ttx, Tc * x + Td * z + Ttz); }

            var names = new Dictionary<string, string>();
            if (File.Exists(NAMES)) foreach (var kv in MiniJson.O(MiniJson.O(MiniJson.Parse(File.ReadAllText(NAMES)))["imena"])) names[kv.Key] = (string)kv.Value;
            var keys = names.Keys.OrderByDescending(k => k.Length).ToList();

            foreach (var to in MiniJson.L(root["tours"]))
            {
                var t = MiniJson.O(to);
                tourDefs.Add(new GTour { id = (string)t["id"], cargo = (string)t["cargo"], route = (string)t["route"], who = (string)t["who"], payment = (string)t["payment"] });
            }

            var houses = new Dictionary<string, int>();
            foreach (var co in MiniJson.L(root["checkpoints"]))
            {
                var c = MiniJson.O(co);
                var p = new PK { id = (int)MiniJson.D(c["id"]), real = (string)c["name"], kind = (string)c["kind"], role = (string)c["role"] };
                (p.ox, p.oz) = G(MiniJson.L(c["object"])); (p.sx, p.sz) = G(MiniJson.L(c["stop"]));
                foreach (var tr in MiniJson.L(c["tours"])) { var l = MiniJson.L(tr); p.tours.Add((string)l[0] + ":" + (string)l[1]); }
                p.riva = p.real.Contains("(riva)");
                // the display name: the longest key of imena-firmi.json in the real name, replaced
                string k = keys.FirstOrDefault(x => p.real.Contains(x));
                if (k != null) p.name = p.real.Replace(k, names[k]);
                else if (p.kind == "obiteljska kuća") p.name = p.real;
                else
                {
                    p.name = char.ToUpper(p.kind[0]) + p.kind.Substring(1) + (p.riva ? " (riva)" : "");
                    if (p.tours.Count > 0) Debug.LogWarning($"[igra] point {p.id} '{p.real}': no fictional name in {NAMES}, shown as '{p.name}'");
                }
                if (p.kind == "obiteljska kuća") { houses.TryGetValue(p.name, out int n); houses[p.name] = n + 1; }
                res.Add(p);
            }
            // houses on the same street: the package id as the house number
            foreach (var p in res) if (p.kind == "obiteljska kuća" && houses[p.name] > 1) p.name += " " + p.id;

            foreach (var p in res)
            {
                if (p.tours.Count == 0) { p.why = $"bez tura ({p.role})"; continue; }
                if (!(p.sx >= StreetsBuilder.AX0 && p.sx < StreetsBuilder.AX1 && p.sz >= StreetsBuilder.AZ0 && p.sz < StreetsBuilder.AZ1)) { p.why = $"izvan Unity svijeta (game x {p.sx:0}, z {p.sz:0}: Split / Solin)"; continue; }
                // nearest open street
                E best = null; double bd = double.MaxValue, bs = 0; int bi = 0; double bt = 0;
                foreach (var e in edges.Values)
                {
                    if (closed.Contains(e.id) || e.pts == null || e.pts.Count < 2 || e.spur != null) continue;
                    double pen = (e.name ?? "").StartsWith("D8") || (e.oneway && e.lanes >= 2) ? D8Penalty : 0;
                    double s = 0;
                    for (int i = 0; i + 1 < e.pts.Count; i++)
                    {
                        var a = e.pts[i]; var b = e.pts[i + 1]; double bx = b[0] - a[0], bz = b[2] - a[2], l2 = bx * bx + bz * bz, L = Math.Sqrt(l2);
                        double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((p.sx - a[0]) * bx + (p.sz - a[2]) * bz) / l2));
                        double d = Math.Sqrt(Sq(p.sx - a[0] - bx * t) + Sq(p.sz - a[2] - bz * t)) + pen;
                        if (d < bd) { bd = d; best = e; bi = i; bt = t; bs = s + L * t; }
                        s += L;
                    }
                }
                if (best == null || bd > SnapMax) { p.why = $"nema ulice grafa u krugu {SnapMax:0} m (najbliža {bd:0} m)"; continue; }
                var A = best.pts[bi]; var B = best.pts[bi + 1];
                p.bx = A[0] + (B[0] - A[0]) * bt; p.by = A[1] + (B[1] - A[1]) * bt; p.bz = A[2] + (B[2] - A[2]) * bt;
                double tl = Math.Sqrt(Sq(B[0] - A[0]) + Sq(B[2] - A[2])); p.tx = (B[0] - A[0]) / Math.Max(1e-6, tl); p.tz = (B[2] - A[2]) / Math.Max(1e-6, tl);
                p.snapD = bd; p.edgeName = best.name;
                double len = PolyLen(best.pts);
                if (bs < 2.5) p.node = best.a;
                else if (len - bs < 2.5) p.node = best.b;
                else
                {
                    long nid = PkNode0 + p.id; nodes[nid] = new N { id = nid, x = p.bx, y = p.by, z = p.bz };
                    var (P1, P2) = StreetsBuilder.SplitAt(best.pts, p.bx, p.by, p.bz);
                    var e2 = new E { id = PkEdge0 + p.id, a = nid, b = best.b, oneway = best.oneway, name = best.name, rf = best.rf, lanes = best.lanes, spur = best.spur, pts = P2, length = PolyLen(P2) };
                    best.b = nid; best.pts = P1; best.length = PolyLen(P1); edges[e2.id] = e2;
                    p.node = nid;
                }
            }
            return res;
        }

        static void AddPackageStops(List<PK> pk, List<GStop> stops, Dictionary<long, int> nIndex, List<GEdge> eList, List<Vector3> pts, int nNodes, List<GTour> tourDefs)
        {
            int depot = stops.FindIndex(s => s.id == MagistralaGame.Warehouse);
            int dn = depot < 0 ? -1 : stops[depot].padNode >= 0 ? stops[depot].padNode : stops[depot].accessNode;
            float[] from = dn >= 0 ? Dijkstra(eList, pts, nNodes, dn, false) : null, back = dn >= 0 ? Dijkstra(eList, pts, nNodes, dn, true) : null;
            int kept = 0;
            foreach (var p in pk)
            {
                if (p.why != null) continue;
                if (!nIndex.TryGetValue(p.node, out int ni)) { p.why = "čvor izvan grafa"; continue; }
                if (from == null) { p.why = "nema skladišta u Lukšiću (kastela_skladiste)"; continue; }
                bool there = from[ni] < float.MaxValue, home = back[ni] < float.MaxValue;
                if (!there || !home) { p.why = $"GPS: nema puta {(there ? "natrag u skladište" : home ? "od skladišta" : "ni od skladišta ni natrag")} (ulica {p.edgeName}, otok u grafu)"; continue; }
                var st = new GStop
                {
                    id = "p" + p.id, name = p.name, kind = p.kind, pid = p.id, tours = p.tours.ToArray(), maxVehicle = p.riva ? "mali" : "",
                    pos = W.U(p.ox, p.by, p.oz), bay = W.U(p.bx, p.by, p.bz), bayDir = new Vector2((float)p.tx, -(float)p.tz).normalized,
                    bayHl = 9.5f, bayHw = 2.6f, padNode = ni, accessNode = ni,
                };
                stops.Add(st); kept++;
                report.Add($"ok   {p.id,3} {p.name} ← {p.real} [{p.kind}] {string.Join(" ", p.tours)} · ulica {p.edgeName} ({p.snapD:0} m) · od skladišta {from[ni] / 1000:0.0} km, natrag {back[ni] / 1000:0.0} km");
            }
            foreach (var p in pk) if (p.why != null) report.Add($"van  {p.id,3} {p.real} [{p.kind}]: {p.why}");
            foreach (var t in tourDefs)
            {
                t.pickups = stops.Count(s => s.Has(t.id, 'u')); t.drops = stops.Count(s => s.Has(t.id, 'd'));
                report.Add($"tura {t.id}: {t.cargo} ({t.who}, {t.payment}) · utovar {t.pickups} · dostava {t.drops}{(t.pickups == 0 || t.drops == 0 ? " → nije igriva u Kaštelima" : "")}");
            }
            report.Insert(0, $"mag-11-19 → Unity, {DateTime.Now:yyyy-MM-dd HH:mm}: {pk.Count} točaka u paketu, {kept} u igri");
            try { Directory.CreateDirectory("Logs"); File.WriteAllLines(REPORT, report); } catch (Exception e) { Debug.LogWarning(e.Message); }
            Debug.Log($"[igra] delivery points: {kept} of {pk.Count} kept ({REPORT}); {string.Join(", ", tourDefs.Select(t => $"{t.id} {t.pickups}u/{t.drops}d"))}");
        }

        static void FillDistances(List<GStop> stops, List<GEdge> eList, List<Vector3> pts, int nNodes, float[] dist)
        {
            int n = stops.Count;
            for (int i = 0; i < n; i++)
            {
                bool need = false; for (int j = 0; j < n; j++) if (i != j && dist[i * n + j] < 0) { need = true; break; }
                int src = stops[i].padNode >= 0 ? stops[i].padNode : stops[i].accessNode; if (!need || src < 0) continue;
                var d = Dijkstra(eList, pts, nNodes, src, false);
                for (int j = 0; j < n; j++)
                {
                    if (dist[i * n + j] >= 0) continue;
                    int t = stops[j].padNode >= 0 ? stops[j].padNode : stops[j].accessNode;
                    if (i == j) dist[i * n + j] = 0; else if (t >= 0 && d[t] < float.MaxValue) dist[i * n + j] = Mathf.Round(d[t]);
                }
            }
        }

        /// road metres from `src` to every node (reverse: from every node to `src`); closed edges skipped, one-ways respected
        static float[] Dijkstra(List<GEdge> eList, List<Vector3> pts, int n, int src, bool reverse)
        {
            var adj = new List<(int v, float w)>[n];
            for (int i = 0; i < n; i++) adj[i] = new List<(int, float)>();
            foreach (var e in eList)
            {
                if (e.closed) continue;
                float w = e.length > 0 ? e.length : Vector3.Distance(pts[e.p0], pts[e.p0 + e.pn - 1]);
                if (!reverse) { adj[e.a].Add((e.b, w)); if (!e.oneway) adj[e.b].Add((e.a, w)); }
                else { adj[e.b].Add((e.a, w)); if (!e.oneway) adj[e.a].Add((e.b, w)); }
            }
            var d = new float[n]; for (int i = 0; i < n; i++) d[i] = float.MaxValue;
            var open = new SortedSet<(float, int)>(); d[src] = 0; open.Add((0, src));
            while (open.Count > 0)
            {
                var (du, u) = open.Min; open.Remove(open.Min);
                foreach (var (v, w) in adj[u])
                {
                    float nd = du + w;
                    if (nd < d[v]) { if (d[v] < float.MaxValue) open.Remove((d[v], v)); d[v] = nd; open.Add((nd, v)); }
                }
            }
            return d;
        }
    }
}
