// Oleandri (28. 11.): the oleander shrubs of BalkanDriver tools/world/mapillary/vrste/oleander.json (README.md there, section
// "Oleandri"): the whole D8 through Kaštela — the central reservation (~4 000) and the green verges north / south (~3 000) as
// runs of 3–12 m (a shrub every 1.2–1.8 m) with gaps of 0.5–3 m (now and then 5–15 m at junctions, crossings, signs) — plus
// the green islands, the Mapillary oleanders and the hedge detections that are likely oleanders (fromHedge). Only place = true.
//   models    ExtraTrees oleander_a / b / c (variant 0 / 1 / 2: 3.35 / 2.8 / 2.3 m native, crowns narrowed to 72 %), uniform scale
//             height / mesh height; on the median the crown at most 2.4 m wide (the strip is 1.5–2.5 m between the carriageways)
//   skipped   on the model asphalt / a street / footway, within 1 m of a building, car park / pitch, the sea, inside a prop / sign /
//             signal / stop box, ≤ 1 m from a tree of ours ≥ 2.5 m tall, ≤ 0.5 m from an oleander placed before
//   y         dropped onto the ground collider (terrain, model ground, streets, riva paving) as VegetationClean does
//   replaces  the Mapillary oleander / hedge detections that oleander.json carries (detectionId): MapillaryTrees skips them, and
//             MedianHedge drops its Tripo oleanders and the dark block body on the median (only grass and the odd shrub stay)
// Instanced by the TreeRenderer (one prototype per variant, impostor beyond 40 m, drawn to 450 m). Build trees runs Apply after
// PalmRows; menu Magistrala/Stabla: oleander re-applies it to the current trees.bytes (the records it added before,
// Generated/Nature/stabla-oleander-added.bytes, are taken out first; VegetationClean and Drvoredi keep them).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class OleanderRows
    {
        public const string PATH = W.BD + "/tools/world/mapillary/vrste/oleander.json";
        public const string ADDED = WorldBuilder.GEN + "/Nature/stabla-oleander-added.bytes";
        public static bool On = true;
        /// the D8 median's oleanders come from oleander.json (MedianHedge leaves out its own)
        public static bool Median => On && ExtraTrees.HasOleanders && File.Exists(PATH);
        const int REC = 24;
        const float MEDIAN_MAX_W = 2.4f;

        class Item { public double x, z; public int variant; public float height; public string area; public bool place; public bool detection; }
        static List<Item> items, detections;
        static List<Item> Items()
        {
            if (items != null) return items;
            items = new List<Item>();
            if (!File.Exists(PATH)) return items;
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(PATH)));
            foreach (var o in MiniJson.L(root["items"]))
            {
                var d = MiniJson.O(o); var p = MiniJson.L(d["position"]);
                items.Add(new Item
                {
                    x = MiniJson.D(p[0]), z = MiniJson.D(p[1]), variant = (int)MiniJson.D(d["variant"]), height = MiniJson.F(d["height"]),
                    area = (string)d["area"], place = d["place"] is bool b && b, detection = d.ContainsKey("detectionId") && d["detectionId"] != null,
                });
            }
            return items;
        }

        /// a Mapillary detection (gx, gz) that oleander.json carries (kept or blocked): MapillaryTrees must not plant it as well
        public static bool Replaces(double gx, double gz)
        {
            if (!Median) return false;
            if (detections == null) detections = Items().Where(it => it.detection).ToList();
            foreach (var it in detections) if (Math.Abs(it.x - gx) < 1 && Math.Abs(it.z - gz) < 1) return true;
            return false;
        }

        [MenuItem("Magistrala/Stabla: oleander")]
        public static string Menu()
        {
            MainScene.Ensure(); items = null; detections = null;
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
            if (!On || !tr || !File.Exists(path)) return "[oleander] off / no trees.bytes";
            if (!File.Exists(PATH)) return "[oleander] no " + PATH;
            items = null; detections = null; var all = Items();
            // the prototypes: oleander_a / b / c by mesh name
            var P = new int[] { -1, -1, -1 }; var H = new float[3]; var Wd = new float[3];
            for (int i = 0; i < tr.meshes.Length; i++)
            {
                var n = tr.meshes[i] ? tr.meshes[i].name.Split('.')[0] : "";
                int k = n == "oleander_a" ? 0 : n == "oleander_b" ? 1 : n == "oleander_c" ? 2 : -1;
                if (k >= 0) { P[k] = i; H[k] = tr.meshes[i].bounds.size.y; Wd[k] = 2 * Mathf.Max(tr.meshes[i].bounds.extents.x, tr.meshes[i].bounds.extents.z); }
            }
            if (P.All(p => p < 0)) return "[oleander] no oleander prototypes (ExtraTrees.Oleanders)";

            var old = Added(); var bytes = File.ReadAllBytes(path); var rec = new List<byte>(bytes.Length + all.Count * REC); int removed = 0;
            var grid = new Dictionary<long, List<Vector2>>();
            long Key(double x, double z) => ((long)Math.Floor(x / 8) << 32) ^ (uint)Math.Floor(z / 8);
            for (int o = 0; o + REC <= bytes.Length; o += REC)
            {
                if (old.Count > 0 && old.Contains(Convert.ToBase64String(bytes, o, REC))) { removed++; continue; }
                rec.AddRange(new ArraySegment<byte>(bytes, o, REC));
                int p = (int)BitConverter.ToSingle(bytes, o + 20); float s = BitConverter.ToSingle(bytes, o + 16);
                if (p < 0 || p >= tr.meshes.Length || !tr.meshes[p] || tr.meshes[p].bounds.size.y * s < 2.5f) continue;
                var g = W.G(new Vector3(BitConverter.ToSingle(bytes, o), 0, BitConverter.ToSingle(bytes, o + 8)));
                long kk = Key(g.x, g.y); if (!grid.TryGetValue(kk, out var l)) grid[kk] = l = new List<Vector2>(); l.Add(new Vector2((float)g.x, (float)g.y));
            }
            bool Near(Dictionary<long, List<Vector2>> gr, double x, double z, double R)
            {
                for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++)
                        if (gr.TryGetValue(Key(x + i * 8, z + j * 8), out var l)) foreach (var q in l) if ((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z) < R * R) return true;
                return false;
            }
            // props / signs / signals / stops (renderer bounds, game frame) on an 8 m grid
            var boxes = new Dictionary<long, List<(double x0, double z0, double x1, double z1)>>();
            var world = GameObject.Find("World");
            if (world)
                foreach (var name in new[] { "Props", "Signs", "Signals", "Stops", "Details" })
                {
                    var t = world.transform.Find(name); if (!t) continue;
                    foreach (var r in t.GetComponentsInChildren<Renderer>())
                    {
                        var b = r.bounds; if (b.size.x > 14 || b.size.z > 14) continue;
                        var a = W.G(b.min); var c = W.G(b.max);
                        var bx = (Math.Min(a.x, c.x), Math.Min(a.y, c.y), Math.Max(a.x, c.x), Math.Max(a.y, c.y));
                        for (double gx = Math.Floor(bx.Item1 / 8) * 8; gx <= bx.Item3 + 8; gx += 8)
                            for (double gz = Math.Floor(bx.Item2 / 8) * 8; gz <= bx.Item4 + 8; gz += 8)
                            { long kk = Key(gx, gz); if (!boxes.TryGetValue(kk, out var l)) boxes[kk] = l = new List<(double, double, double, double)>(); l.Add(bx); }
                    }
                }
            bool InBox(double x, double z)
            {
                if (!boxes.TryGetValue(Key(x, z), out var l)) return false;
                foreach (var b in l) if (x > b.x0 - .3 && x < b.x1 + .3 && z > b.z0 - .3 && z < b.z1 + .3) return true;
                return false;
            }
            // the models near a point (64 m buckets) for the asphalt test
            var mb = new Dictionary<long, List<ModelJ>>();
            long MK(double x, double z) => ((long)Math.Floor(x / 64) << 32) ^ (uint)Math.Floor(z / 64);
            foreach (var m in models)
                for (double gx = Math.Floor((m.x - m.half - 25) / 64) * 64; gx <= m.x + m.half + 25; gx += 64)
                    for (double gz = Math.Floor((m.z - m.half - 25) / 64) * 64; gz <= m.z + m.half + 25; gz += 64)
                    { long kk = MK(gx, gz); if (!mb.TryGetValue(kk, out var l)) mb[kk] = l = new List<ModelJ>(); l.Add(m); }
            var none = new List<ModelJ>();
            List<ModelJ> Models(double x, double z) => mb.TryGetValue(MK(x, z), out var l) ? l : none;
            Physics.SyncTransforms();
            var roots = new[] { "Terrain", "Roads", "Streets", "Riva", "Tlo" }.Select(r => GameObject.Find("World/" + r)).Where(g => g).Select(g => g.transform).ToArray();
            var modelRoot = GameObject.Find("World/Roads")?.transform;

            var add = new List<byte>(all.Count * REC); var placed = new Dictionary<long, List<Vector2>>();
            var why = new Dictionary<string, int>(); var areas = new Dictionary<string, int>(); var kinds = new int[3];
            void No(string w) { why.TryGetValue(w, out int n); why[w] = n + 1; }
            int cand = 0;
            // Kambelovac → Lukšić: the one bush where the median opens into the westbound left-turn bay (D8Ograda.cs)
            { var b = D8Ograda.Bush(WorldData.Models()); if (D8Ograda.On && !float.IsNaN(b.x)) all = all.Concat(new[] { new Item { x = b.x, z = b.y, variant = 1, height = 1.9f, area = "D8 razdjelni", place = true } }).ToList(); }
            foreach (var it in all)
            {
                if (!it.place) continue; cand++;
                double x = it.x, z = it.z;
                if (it.area == "D8 razdjelni" && D8Ograda.NoHedge(x, z)) { No("Kambelovac – Lukšić: median guardrail, no hedge (D8Ograda)"); continue; }
                if (!NaturePass.In(x, z)) { No("outside the world"); continue; }
                var near = Models(x, z);
                if (LaneProfile.OnAsphalt(near, x, z)) { No("model asphalt"); continue; }
                if (StreetsBuilder.Covered(x, z, .3)) { No("street / footway"); continue; }
                if (VegetationMask.NearBuilding(x, z)) { No("building"); continue; }
                if (GroundCover.Hard(x, z, 0)) { No("car park / pitch"); continue; }
                if (InBox(x, z)) { No("prop / sign / signal"); continue; }
                if (Near(grid, x, z, 1)) { No("a tree there"); continue; }
                if (Near(placed, x, z, .5)) { No("duplicate"); continue; }
                int k = Mathf.Clamp(it.variant, 0, 2); if (P[k] < 0) k = Array.FindIndex(P, p => p >= 0);
                int proto = P[k];
                float h = (float)Tiles.Height(x, z); var mt = WorldData.ModelTerrain(near, x, z, 2, h); if (mt.HasValue) h = (float)mt.Value;
                var u = W.U(x, h, z);
                if (VegetationClean.Surface(u, roots, modelRoot, out float sy, out bool onModel, out Vector3 gn)) { if (onModel && gn.y < .8f) { No("slope"); continue; } h = sy; }
                if (h < .4f) { No("sea"); continue; }
                float hh = Mathf.Clamp(it.height > 0 ? it.height : 2.4f, 1.5f, 3.5f);
                float sc = H[k] > .1f ? hh / H[k] : 1;
                if (it.area == "D8 razdjelni" && Wd[k] * sc > MEDIAN_MAX_W) sc = MEDIAN_MAX_W / Wd[k];
                u = W.U(x, h - .06, z); var one = new List<byte>(REC);
                foreach (var v in new[] { u.x, u.y, u.z, NaturePass.R01(x, z, 1971) * 360, sc, proto }) one.AddRange(BitConverter.GetBytes(v));
                rec.AddRange(one); add.AddRange(one); kinds[k]++;
                long pk = Key(x, z); if (!placed.TryGetValue(pk, out var pl)) placed[pk] = pl = new List<Vector2>(); pl.Add(new Vector2((float)x, (float)z));
                areas.TryGetValue(it.area ?? "?", out int na); areas[it.area ?? "?"] = na + 1;
            }
            File.WriteAllBytes(path, rec.ToArray()); AssetDatabase.ImportAsset(path);
            Directory.CreateDirectory(Path.GetDirectoryName(ADDED)); File.WriteAllBytes(ADDED, add.ToArray());
            tr.data = AssetDatabase.LoadAssetAtPath<TextAsset>(path); EditorUtility.SetDirty(tr);
            int tot = kinds.Sum();
            return $"[oleander] {cand} to place: planted {tot} (a {kinds[0]}, b {kinds[1]}, c {kinds[2]}; " + string.Join(", ", areas.OrderByDescending(a => a.Value).Select(a => $"{a.Key} {a.Value}")) +
                   $"); {removed} old records replaced; skipped: " + string.Join(", ", why.OrderByDescending(w => w.Value).Select(w => $"{w.Key} {w.Value}"));
        }
    }
}
