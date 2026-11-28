// Traffic signals at the real signalised junctions that have no junction model of the owner (so no signal plan and no props):
// Assets/Editor/semafori-bez-modela.json, written by Tools/semafori/semafori_gen.py from the game graph (GameWorld) and checked
// against the owner's photos (BalkanDriver/references/fotografije/semafori/1–7). Seven junctions on Cesta dr. Franje Tuđmana:
// s06 (school), s13 (Put sv. Lovre), Sućurac Tamunić (school) and Putaljski put, Kambelovac T, Gomilica staggered and T.
// Per junction: a generic plan (SG1 main road, SG2 side road(s), P1 an exclusive pedestrian phase at the school / crossing
// junctions; amber, red + amber, all-red) and its approaches: the stop line (centre of the right half of the carriageway, travel
// direction, length), the mount (cantilever on the right with a head over the lane and one on the pole, or a post), the plates,
// the zebra in front of it and the road markings (ŠKOLA, X).
// Used by TrafficBuilder (plans, AI stop lines) and SignsBuilder (heads, poles, plates, painted lines, the player's red check).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Magistrala.Traffic;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class ExtraSignals
    {
        public const string PATH = "Assets/Editor/semafori-bez-modela.json";

        public class Zebra { public Vector2 p, t; public float l, w; public bool redWhite, paint; }
        public class Mark { public string kind; public Vector2 p, t; }
        public class Approach
        {
            public string name, group, mount; public Vector2 p, axis, t; public float w; public bool paint;
            public HashSet<string> signs = new HashSet<string>(); public Zebra zebra; public List<Mark> marks = new List<Mark>();
        }
        public class Junction
        {
            public string key, title, model; public long node; public Vector2 c; public float y; public TrafficPlan plan;
            public List<Approach> approaches = new List<Approach>();
            /// zebras with pedestrian heads at both ends (centre, road direction, width across)
            public List<(Vector2 p, Vector2 t, float w)> ped = new List<(Vector2, Vector2, float)>();
            public List<Vector2> domes = new List<Vector2>();
        }

        static Vector2 V(object o) { var l = MiniJson.L(o); return new Vector2(MiniJson.F(l[0]), MiniJson.F(l[1])); }

        public static List<Junction> Load()
        {
            var res = new List<Junction>();
            if (!File.Exists(PATH)) { Debug.LogWarning("[semafori] nema " + PATH); return res; }
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(PATH)));
            foreach (var jo in MiniJson.L(root["junctions"]))
            {
                var j = MiniJson.O(jo);
                var J = new Junction { key = (string)j["key"], title = MiniJson.G(j, "title") as string ?? "", c = V(j["c"]), y = MiniJson.G(j, "y") is object yy ? MiniJson.F(yy) : 15 };
                var pl = MiniJson.O(j["plan"]);
                J.plan = new TrafficPlan { key = J.key, cycle = MiniJson.F(pl["cycle"]), offset = MiniJson.G(j, "offset") is object of ? MiniJson.F(of) : 0 };
                var ph = new List<TrafficPhase>();
                foreach (var po in MiniJson.L(pl["phases"]))
                {
                    var p = MiniJson.O(po);
                    var f = new TrafficPhase { group = (string)p["group"], g0 = -1, g1 = -1, y0 = -1, y1 = -1, r0 = -1, r1 = -1 };
                    if (MiniJson.G(p, "green") is List<object> g) { f.g0 = MiniJson.F(g[0]); f.g1 = MiniJson.F(g[1]); }
                    if (MiniJson.G(p, "yellow") is List<object> y) { f.y0 = MiniJson.F(y[0]); f.y1 = MiniJson.F(y[1]); }
                    if (MiniJson.G(p, "redYellow") is List<object> r) { f.r0 = MiniJson.F(r[0]); f.r1 = MiniJson.F(r[1]); }
                    ph.Add(f);
                }
                J.plan.phases = ph.ToArray();
                foreach (var ao in MiniJson.L(j["approaches"]))
                {
                    var a = MiniJson.O(ao);
                    var A = new Approach
                    {
                        name = MiniJson.G(a, "id") as string ?? "", group = (string)a["group"], mount = (string)a["mount"], p = V(a["p"]),
                        axis = MiniJson.G(a, "axis") is object ax ? V(ax) : V(a["p"]), t = V(a["t"]).normalized, w = MiniJson.F(a["w"]),
                        paint = !(MiniJson.G(a, "paint") is bool pb) || pb,
                    };
                    foreach (var s in MiniJson.L(MiniJson.G(a, "signs")) ?? new List<object>()) A.signs.Add((string)s);
                    if (MiniJson.G(a, "zebra") is Dictionary<string, object> z)
                        A.zebra = new Zebra { p = V(z["p"]), t = V(z["t"]).normalized, l = MiniJson.F(z["l"]), w = MiniJson.F(z["w"]), redWhite = MiniJson.G(z, "rw") is bool rw && rw, paint = MiniJson.G(z, "paint") is bool zp && zp };
                    foreach (var mo in MiniJson.L(MiniJson.G(a, "marks")) ?? new List<object>())
                    {
                        var m = MiniJson.O(mo); A.marks.Add(new Mark { kind = (string)m["k"], p = V(m["p"]), t = V(m["t"]).normalized });
                    }
                    J.approaches.Add(A);
                }
                foreach (var po in MiniJson.L(MiniJson.G(j, "ped")) ?? new List<object>())
                {
                    var p = MiniJson.O(po); J.ped.Add((V(p["zebra"]), V(p["t"]).normalized, MiniJson.F(p["w"])));
                }
                foreach (var d in MiniJson.L(MiniJson.G(j, "domes")) ?? new List<object>()) J.domes.Add(V(d));
                res.Add(J);
            }
            BindModels(res);
            return res;
        }

        // IDs in the importer's node table are not stable identities. Restrict to
        // the owning model, then resolve against the surveyed signal position.
        static void BindModels(List<Junction> junctions)
        {
            var owners = new Dictionary<string, string> {
                ["semafor-gomilica-t"] = "krizanje-74", ["semafor-gomilica-pomaknuto"] = "krizanje-74",
                ["semafor-kambelovac"] = "krizanje-75", ["semafor-tamunic"] = "krizanje-78", ["semafor-putaljski"] = "krizanje-79"
            };
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "model-junctions.json")));
            GameDataBuilder.MergedGraph(out var nodes, out _, out _);
            foreach (var j in junctions)
            {
                if (!owners.TryGetValue(j.key, out var owner)) continue;
                var model = MiniJson.L(root["junctions"]).Select(MiniJson.O).Single(m => (string)m["key"] == owner);
                float best = 35; long found = 0;
                foreach (var no in MiniJson.L(model["nodes"]))
                {
                    long id = (long)MiniJson.D(no);
                    if (!nodes.TryGetValue(id, out var n)) continue;
                    float d = Vector2.Distance(j.c, new Vector2((float)n.x, (float)n.z));
                    if (d < best) { best = d; found = id; }
                }
                if (found == 0) throw new InvalidDataException("[semafori] no model node near " + j.key + " / " + owner);
                j.model = owner; j.node = found;
                if (best > 10) Debug.LogWarning($"[semafori] {j.key}: {owner} node {found}, {best:F1} m from survey; keep surveyed poles/stop lines, inspect seam");
            }
        }

        /// the stop lines of every approach (game frame: p, travel direction (x east, z south)), with the junction's key and group
        public static List<(Vector2 p, Vector2 dir, string key, string group)> Stops(List<Junction> js) =>
            js.SelectMany(j => j.approaches.Select(a => (a.p, a.t, j.key, a.group))).ToList();
    }
}
