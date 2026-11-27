// Palme i tamarisi na rivama (28. 11.): the tree rows of the riva promenades — BalkanDriver tools/world/riva-extras/oprema.json
// `treeRowSuggestion` (one polyline per contiguous promenade side of the package sections 66–71 and 80–82; the benches, bins,
// bike racks and lamps of the same file already keep ≥ 1.5 m off these lines) — planted with the ExtraTrees palms / tamarisks:
//   species   Štafilić (model 68): tamarisks (Marko's photo: a row of young narrow tamarisks with benches between them, no palms),
//             tamaris_a, every 4th a younger tamaris_b, every 6–8 m. The riva below the Plavi Jadran car park in Kaštel Stari
//             (FAN box): Washingtonia fan palms (Marko's photos). Elsewhere Canary date palms (33 / 34 / 35, mostly the 7–12 m ones),
//             now and then (1 in 12) a fan palm. Heights 6.5–13 m (tamarisks 2.8–5 m), uniform scale of the real mesh height
//   spacing   palms 8–14 m irregular, ~1 in 7 slots left empty; tamarisks 6–8 m; the first one 2–6 m into the row
//   skipped   on a street / court (trunk ≥ 1 m off; the model footways are the promenades themselves), within 1 m of a building, car park / pitch, the photo sea,
//             cafés' terrace circles and entrance circles (oprema.json exclusionZones), ≤ 2 m from a lamp (oprema / Mapillary),
//             ≤ 1.5 m from a bench / bin / rack / bollard / bitt, a parked car or a prop / sign / signal / bus shelter, ≤ 3.5 m from
//             a tree of ours ≥ 2.5 m tall, the trunk or the crown (¾ of its radius) touching a building / wall collider, ≤ 5 m (tamarisks 4.5 m) from a palm placed before
//   y         dropped onto the ground collider (terrain, model ground, streets, riva paving) as VegetationClean does
// Seeded by position (same plants every build). Build trees runs Apply after Drvoredi; menu Magistrala/Stabla: palme na rivama
// re-applies it to the current trees.bytes (the records it added before, Generated/Nature/stabla-palme-added.bytes, are taken
// out first; VegetationClean and Drvoredi keep them).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class PalmRows
    {
        public const string OPREMA = W.BD + "/tools/world/riva-extras/oprema.json";
        public const string ADDED = WorldBuilder.GEN + "/Nature/stabla-palme-added.bytes";
        public static bool On = true;
        const int REC = 24;
        /// the riva below the Plavi Jadran car park (Kaštel Stari, package 66 x 1395–1490): fan palms (game x / z box)
        static readonly Rect FAN = Rect.MinMaxRect(-7992, -4596, -7895, -4548);
        public static bool InFanZone(double x, double z) => FAN.Contains(new Vector2((float)x, (float)z));

        /// the prototypes by mesh name: Canary 33 / 34 / 35, fan a / b, tamarisk a / b (−1 missing), and their target heights (m)
        public class Kinds
        {
            public int[] canary = { -1, -1, -1 }, fan = { -1, -1 }, tam = { -1, -1 };
            public float[] H;
            public Kinds(Magistrala.TreeRenderer tr)
            {
                H = tr.meshes.Select(m => m ? m.bounds.size.y : 0).ToArray();
                for (int i = 0; i < tr.meshes.Length; i++)
                {
                    var n = tr.meshes[i] ? tr.meshes[i].name : ""; if (n.Contains(".")) n = n.Substring(0, n.IndexOf('.'));
                    switch (n)
                    {
                        case "palma_33": canary[0] = i; break; case "palma_34": canary[1] = i; break; case "palma_35": canary[2] = i; break;
                        case "palma_lepeza_a": fan[0] = i; break; case "palma_lepeza_b": fan[1] = i; break;
                        case "tamaris_a": tam[0] = i; break; case "tamaris_b": tam[1] = i; break;
                    }
                }
            }
            public bool Any => canary.Concat(fan).Any(p => p >= 0);
            static readonly Dictionary<string, (float lo, float hi)> RANGE = new Dictionary<string, (float, float)>
            {
                ["palma_33"] = (6.5f, 8.5f), ["palma_34"] = (9, 12), ["palma_35"] = (11, 13), ["palma_lepeza_a"] = (9.5f, 13),
                ["palma_lepeza_b"] = (6.5f, 8.5f), ["tamaris_a"] = (3.8f, 5), ["tamaris_b"] = (2.8f, 3.6f),
            };
            /// a palm for (x, z): fan palms in the Plavi Jadran box (and 1 in 12 elsewhere), else Canary 33 (45 %) / 34 (40 %) / 35;
            /// −1: none of them in the renderer
            public int Palm(double x, double z)
            {
                float r = NaturePass.R01(x, z, 961), q = NaturePass.R01(x, z, 962);
                int[] fans = fan.Where(p => p >= 0).ToArray(), cans = canary.Where(p => p >= 0).ToArray();
                if (fans.Length > 0 && (InFanZone(x, z) || cans.Length == 0 || r < 1 / 12f)) return fans[(int)(q * fans.Length) % fans.Length];
                if (cans.Length == 0) return -1;
                int k = q < .45f ? 0 : q < .85f ? 1 : 2;
                return canary[k] >= 0 ? canary[k] : cans[0];
            }
            public int Tamarisk(double x, double z)
            {
                int a = tam[0], b = tam[1]; if (a < 0) return b; if (b < 0) return a;
                return NaturePass.R01(x, z, 963) < .25f ? b : a;
            }
            /// uniform scale for the prototype: a height within its range, seeded by position
            public float Scale(Magistrala.TreeRenderer tr, int p, double x, double z)
            {
                var n = tr.meshes[p].name; if (n.Contains(".")) n = n.Substring(0, n.IndexOf('.'));
                var (lo, hi) = RANGE.TryGetValue(n, out var r) ? r : (6f, 10f);
                float h = Mathf.Lerp(lo, hi, NaturePass.R01(x, z, 964));
                return H[p] > .1f ? h / H[p] : 1;
            }
        }

        [MenuItem("Magistrala/Stabla: palme na rivama")]
        public static string Menu()
        {
            MainScene.Ensure();
            var tr = GameObject.Find("World")?.transform.Find("Vegetation")?.GetComponent<Magistrala.TreeRenderer>();
            if (!tr) return "no TreeRenderer (Build trees first)";
            var s = Apply(tr, WorldData.Models());
            EditorSceneManager.MarkSceneDirty(tr.gameObject.scene); EditorSceneManager.SaveScene(tr.gameObject.scene);
            Debug.Log(s); return s;
        }

        /// the records added before (base64 of each 24-byte record), for VegetationClean / Drvoredi to keep them
        public static HashSet<string> Added()
        {
            var r = new HashSet<string>();
            if (File.Exists(ADDED)) { var a = File.ReadAllBytes(ADDED); for (int o = 0; o + REC <= a.Length; o += REC) r.Add(Convert.ToBase64String(a, o, REC)); }
            return r;
        }

        public static string Apply(Magistrala.TreeRenderer tr, List<ModelJ> models)
        {
            string path = WorldBuilder.GEN + "/trees.bytes";
            if (!On || !tr || !File.Exists(path)) return "[palme] off / no trees.bytes";
            if (!File.Exists(OPREMA)) return "[palme] no " + OPREMA;
            var K = new Kinds(tr); if (!K.Any) return "[palme] no palm prototypes (ExtraTrees.Palms)";
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(OPREMA)));

            var old = Added(); var bytes = File.ReadAllBytes(path); var rec = new List<byte>(bytes.Length + 16 * 1024); int removed = 0;
            var grid = new Dictionary<long, List<Vector2>>();
            long Key(double x, double z) => ((long)Math.Floor(x / 8) << 32) ^ (uint)Math.Floor(z / 8);
            void Grid(double x, double z) { long k = Key(x, z); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Vector2>(); l.Add(new Vector2((float)x, (float)z)); }
            for (int o = 0; o + REC <= bytes.Length; o += REC)
            {
                if (old.Count > 0 && old.Contains(Convert.ToBase64String(bytes, o, REC))) { removed++; continue; }
                rec.AddRange(new ArraySegment<byte>(bytes, o, REC));
                int p = (int)BitConverter.ToSingle(bytes, o + 20); float s = BitConverter.ToSingle(bytes, o + 16);
                if (p < 0 || p >= K.H.Length || K.H[p] * s < 2.5f) continue;
                var g = W.G(new Vector3(BitConverter.ToSingle(bytes, o), 0, BitConverter.ToSingle(bytes, o + 8))); Grid(g.x, g.y);
            }
            double Nearest(double x, double z, double R)
            {
                double best = double.PositiveInfinity; int r = (int)Math.Ceiling(R / 8);
                for (int i = -r; i <= r; i++) for (int j = -r; j <= r; j++)
                        if (grid.TryGetValue(Key(x + i * 8, z + j * 8), out var l)) foreach (var q in l) best = Math.Min(best, Math.Sqrt((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z)));
                return best;
            }
            // point obstacles (x, z, clearance): oprema.json furniture, Mapillary lamps
            var pts = new List<(double x, double z, double r)>();
            foreach (var io in MiniJson.L(root["items"]))
            {
                var it = MiniJson.O(io); var p = MiniJson.L(it["position"]); string type = (string)it["type"];
                pts.Add((MiniJson.D(p[0]), MiniJson.D(p[1]), type == "street_lamp" ? 2.0 : 1.5));
            }
            if (File.Exists("Assets/Editor/rasvjeta-mapillary.json"))
                foreach (var lo in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText("Assets/Editor/rasvjeta-mapillary.json")))["lights"]))
                { var l = MiniJson.L(lo); pts.Add((MiniJson.D(l[0]), MiniJson.D(l[1]), 2.0)); }
            var zones = MiniJson.L(root["exclusionZones"]).Select(zo => { var z = MiniJson.O(zo); var p = MiniJson.L(z["position"]); return (x: MiniJson.D(p[0]), z: MiniJson.D(p[1]), r: MiniJson.D(z["radiusM"])); }).ToList();
            // parked cars (oriented boxes), props / signs / signals / stops (renderer bounds)
            var cars = new List<(double x, double z, double fx, double fz)>();
            foreach (var pc in UnityEngine.Object.FindObjectsByType<Magistrala.Traffic.ParkedCars>(FindObjectsSortMode.None))
                if (pc.cars != null) foreach (var c in pc.cars) { var g = W.G(new Vector3(c.x, 0, c.z)); float a = c.w * Mathf.Deg2Rad; cars.Add((g.x, g.y, Math.Sin(a), -Math.Cos(a))); }
            var boxes = new List<(double x0, double z0, double x1, double z1)>();
            var world = GameObject.Find("World");
            if (world)
                foreach (var name in new[] { "Props", "Signs", "Signals", "Stops", "Details" })
                {
                    var t = world.transform.Find(name); if (!t) continue;
                    foreach (var r in t.GetComponentsInChildren<Renderer>())
                    {
                        var b = r.bounds; if (b.size.x > 14 || b.size.z > 14) continue;
                        var a = W.G(b.min); var c = W.G(b.max);
                        boxes.Add((Math.Min(a.x, c.x), Math.Min(a.y, c.y), Math.Max(a.x, c.x), Math.Max(a.y, c.y)));
                    }
                }
            bool Obstacle(double x, double z)
            {
                foreach (var q in pts) { double dx = x - q.x, dz = z - q.z; if (dx * dx + dz * dz < q.r * q.r) return true; }
                foreach (var c in cars)
                {
                    double dx = x - c.x, dz = z - c.z; if (dx * dx + dz * dz > 25) continue;
                    double u = Math.Abs(dx * c.fx + dz * c.fz), v = Math.Abs(-dx * c.fz + dz * c.fx);
                    if (u < 2.6 + 1.5 && v < 1.0 + 1.5) return true;
                }
                foreach (var b in boxes) if (x > b.x0 - 1.5 && x < b.x1 + 1.5 && z > b.z0 - 1.5 && z < b.z1 + 1.5) return true;
                return false;
            }
            // houses, yard / frontage walls (their hull-level colliders): neither the trunk nor the crown may touch one
            var built = new HashSet<Collider>(); var bRoot = world ? world.transform.Find("Buildings") : null;
            if (bRoot) foreach (var c in bRoot.GetComponentsInChildren<Collider>()) built.Add(c);
            var ov = new Collider[32];
            bool InBuilt(Vector3 u, float top, float crownR)
            {
                if (built.Count == 0) return false;
                int n = Physics.OverlapCapsuleNonAlloc(u + Vector3.up * .3f, u + Vector3.up * Mathf.Max(1, top - crownR), .45f, ov, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) if (built.Contains(ov[i])) return true;
                n = Physics.OverlapSphereNonAlloc(u + Vector3.up * Mathf.Max(1.5f, top - crownR * .7f), crownR, ov, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) if (built.Contains(ov[i])) return true;
                return false;
            }
            Physics.SyncTransforms();
            var roots = new[] { "Terrain", "Roads", "Streets", "Riva", "Tlo" }.Select(r => GameObject.Find("World/" + r)).Where(g => g).Select(g => g.transform).ToArray();
            var modelRoot = GameObject.Find("World/Roads")?.transform;

            var add = new List<byte>(); var placed = new List<(double x, double z, bool tam)>();
            var why = new Dictionary<string, int>(); var kinds = new Dictionary<string, int>(); var towns = new Dictionary<string, int>();
            void No(string w) { why.TryGetValue(w, out int n); why[w] = n + 1; }
            int rows = 0, slots = 0;
            foreach (var ro in MiniJson.L(root["treeRowSuggestion"]))
            {
                var row = MiniJson.O(ro); string town = (string)row["town"]; bool tamRow = town == "Štafilić" || ((string)MiniJson.G(row, "speciesSuggestion") ?? "") == "tamarisk";
                var line = MiniJson.L(row["polyline"]).Select(q => { var l = MiniJson.L(q); return new Vector2((float)MiniJson.D(l[0]), (float)MiniJson.D(l[1])); }).ToList();
                if (line.Count < 2) continue; rows++;
                var seg = new List<double> { 0 }; for (int i = 1; i < line.Count; i++) seg.Add(seg[i - 1] + Vector2.Distance(line[i - 1], line[i]));
                double L = seg[seg.Count - 1];
                double s = 2 + 4 * NaturePass.R01(line[0].x, line[0].y, 965);
                while (s < L - 1)
                {
                    int i = 1; while (i < seg.Count - 1 && seg[i] < s) i++;
                    double t = (s - seg[i - 1]) / Math.Max(1e-6, seg[i] - seg[i - 1]); var a = line[i - 1]; var b = line[i];
                    var dir = (b - a).normalized; var nrm = new Vector2(-dir.y, dir.x);
                    float jit = (NaturePass.R01(a.x + s, a.y, 966) - .5f) * .6f;
                    double x = a.x + (b.x - a.x) * t + nrm.x * jit, z = a.y + (b.y - a.y) * t + nrm.y * jit;
                    double step = tamRow ? 6 + 2 * NaturePass.R01(x, z, 967) : 8 + 6 * NaturePass.R01(x, z, 967);
                    s += step; slots++;
                    if (!tamRow && NaturePass.R01(x, z, 968) < 1 / 7f) { No("gap (irregular row)"); continue; }
                    if (!NaturePass.In(x, z)) { No("outside the world"); continue; }
                    if (zones.Any(q => (x - q.x) * (x - q.x) + (z - q.z) * (z - q.z) < q.r * q.r)) { No("café / entrance"); continue; }
                    // (the package models' promenades are model footways: only the streets / courts and the model asphalt outside the
                    // package's riva models count; inside them the rows of oprema.json already keep ≥ 0.5 m off the carriageway)
                    if (StreetsBuilder.CoveredStreet(x, z, 1.0)) { No("street"); continue; }
                    if (VegetationMask.NearBuilding(x, z)) { No("building"); continue; }
                    if (GroundCover.Hard(x, z, 1.5)) { No("car park / pitch"); continue; }
                    int code = RivaBuilder.Code(x, z);
                    if ((code & 3) == 3 || ((code & 3) == 1 && (code & 8) != 0)) { No("riva car park / sea"); continue; }
                    if (Obstacle(x, z)) { No("furniture / lamp / car / prop"); continue; }
                    if (Nearest(x, z, 3.5) < 3.5) { No("a tree there"); continue; }
                    double minD = tamRow ? 4.5 : 5;
                    if (placed.Any(q => (x - q.x) * (x - q.x) + (z - q.z) * (z - q.z) < minD * minD)) { No("palm there"); continue; }
                    float h = (float)Tiles.Height(x, z); var mt = WorldData.ModelTerrain(models, x, z, 2, h); if (mt.HasValue) h = (float)mt.Value;
                    var u = W.U(x, h, z);
                    if (VegetationClean.Surface(u, roots, modelRoot, out float sy, out bool onModel, out Vector3 gn)) { if (onModel && gn.y < .9f) { No("slope"); continue; } h = sy; }
                    if (h < .4f) { No("sea"); continue; }
                    int p = tamRow ? K.Tamarisk(x, z) : K.Palm(x, z);
                    if (p < 0) { No(tamRow ? "no tamarisk prototype" : "no palm prototype"); continue; }
                    float sc = K.Scale(tr, p, x, z);
                    var mb = tr.meshes[p].bounds; float top = mb.max.y * sc, crownR = .75f * Mathf.Max(mb.extents.x, mb.extents.z) * sc;
                    if (InBuilt(W.U(x, h, z), top, crownR)) { No("crown / trunk in a building"); continue; }
                    u = W.U(x, h - .08, z); var one = new List<byte>(REC);
                    foreach (var v in new[] { u.x, u.y, u.z, NaturePass.R01(x, z, 969) * 360, sc, p }) one.AddRange(BitConverter.GetBytes(v));
                    rec.AddRange(one); add.AddRange(one); placed.Add((x, z, tamRow));
                    string kn = tr.meshes[p].name.Split('.')[0]; kinds.TryGetValue(kn, out int nk); kinds[kn] = nk + 1;
                    towns.TryGetValue(town, out int nt); towns[town] = nt + 1;
                }
            }
            File.WriteAllBytes(path, rec.ToArray()); AssetDatabase.ImportAsset(path);
            Directory.CreateDirectory(Path.GetDirectoryName(ADDED)); File.WriteAllBytes(ADDED, add.ToArray());
            tr.data = AssetDatabase.LoadAssetAtPath<TextAsset>(path); EditorUtility.SetDirty(tr);
            return $"[palme] riva rows {rows}, slots {slots}: placed {placed.Count} ({placed.Count(q => q.tam)} tamarisks; " + string.Join(", ", kinds.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}")) +
                   "; by town " + string.Join(", ", towns.Select(k => $"{k.Key} {k.Value}")) + $"); {removed} old records replaced; skipped: " +
                   string.Join(", ", why.OrderByDescending(w => w.Value).Select(w => $"{w.Key} {w.Value}"));
        }
    }
}
