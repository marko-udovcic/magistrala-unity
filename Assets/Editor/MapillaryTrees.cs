// Mapillary street trees (docs/mapillary-izvlacenje.md, "Trees"): Assets/Editor/stabla-mapillary.json is BalkanDriver
// tools/world/mapillary/stabla.json (s4_trees.py; Street-level imagery: Mapillary contributors, CC BY-SA 4.0): crowns found in the
// DGU LiDAR orthophoto 1–25 m beyond the old road (6137) and the D8, typed and measured (top height) from the street photos.
// Only the crowns the game lacks are added, to the TreeRenderer's trees.bytes (no new prototype, no new draw call kind):
//   * taken      conf ≥ MIN_CONF (seen in ≥ 1 segmented street photo); on the old road (village streets, little segmented imagery)
//                also the orthophoto-only crowns (conf 0.15–0.25) of 3–12 m that are not hedges; never a `patch` (scrub / grove
//                along the D8, not one crown); no palms (no palm prototype; the classifier also takes lamps for palms)
//   * species    other broadleaf → crnika (dark evergreen broadleaf), olive → olive, pine → the package's Aleppo-shaped pines,
//                cypress → cypress, oleander → oleander-roza, hedge → 1–5 dark bushes (grm-tamni) over the crown; scaled to the
//                measured top height (else from the crown Ø)
//   * skipped    a tree of ours (≥ 2.5 m tall) within crown radius + 2 m; on / near a street, footway or model asphalt (more off
//                it the wider the crown), within 1 m of a building (2 m of a model one), car parks / pitches (GroundCover.Hard),
//                the riva paving (RivaBuilder.NoTree), 0–8 m from the D8 edge (VegetationMask D8CLEAR, docs/vegetacija-d8.md),
//                within 1.5 m of a parked car's box or of a prop / sign / signal
// Build trees calls Apply at its end; menu Magistrala/Stabla: Mapillary re-applies it to the current trees.bytes (the records it
// added before are listed in Generated/Nature/stabla-mapillary-added.bytes and taken out first).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class MapillaryTrees
    {
        public const string PATH = "Assets/Editor/stabla-mapillary.json";
        const string ADDED = WorldBuilder.GEN + "/Nature/stabla-mapillary-added.bytes";
        public const float MIN_CONF = .35f;
        public static bool On = true;
        const int REC = 24;

        [MenuItem("Magistrala/Stabla: Mapillary")]
        public static string Menu()
        {
            MainScene.Ensure();
            var tr = GameObject.Find("World")?.transform.Find("Vegetation")?.GetComponent<Magistrala.TreeRenderer>();   // (not the AC units' TreeRenderer)
            if (!tr) return "no TreeRenderer (Build trees first)";
            var s = Apply(tr, WorldData.Models());
            EditorSceneManager.MarkSceneDirty(tr.gameObject.scene); EditorSceneManager.SaveScene(tr.gameObject.scene);
            Debug.Log(s); return s;
        }

        class T { public double gx, gz, d, h, edge; public string type, road; public float conf; public int n; public bool patch; }

        static List<T> Load()
        {
            var r = new List<T>(); if (!File.Exists(PATH)) return r;
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(PATH)));
            foreach (var o in MiniJson.L(root["trees"]))
            {
                var d = MiniJson.O(o);
                r.Add(new T
                {
                    gx = MiniJson.D(d["gx"]), gz = MiniJson.D(d["gz"]), d = MiniJson.D(d["d"]), h = MiniJson.D(MiniJson.G(d, "h") ?? 0.0),
                    type = (string)d["type"], conf = MiniJson.F(d["conf"]), n = (int)MiniJson.D(d["n"]), patch = MiniJson.D(MiniJson.G(d, "patch") ?? 0.0) > .5,
                    road = (string)MiniJson.G(d, "road") ?? "d", edge = MiniJson.D(MiniJson.G(d, "edge") ?? 0.0),
                });
            }
            return r;
        }

        static bool Taken(T t)
        {
            if (t.patch || t.type == "palm") return false;
            if (t.conf >= MIN_CONF) return true;
            return t.road == "s" && t.type != "hedge" && t.d >= 3 && t.d <= 12;
        }

        /// prototype indices of the scene's TreeRenderer (ours 0–7 by TreeProtos.IDS, the NaturePass / MedianHedge extras by mesh /
        /// material name)
        class Protos
        {
            public int olive = 2, cypress = 3, crnika = -1, tamni = -1, oleander = -1; public List<int> pines = new List<int>();
            public float[] height;   // (tr must be World/Vegetation, not the AC units' TreeRenderer)
            public Protos(Magistrala.TreeRenderer tr)
            {
                height = tr.meshes.Select(m => m ? m.bounds.size.y : 0).ToArray();
                for (int i = TreeProtos.IDS.Length; i < tr.meshes.Length; i++)
                {
                    var m = tr.meshes[i]; if (!m) continue; var mat = tr.materials[tr.materialStart[i]];
                    if (crnika < 0 && mat && mat.name.Contains("crnika")) crnika = i;
                    else if (m.name.StartsWith("pine_") && !m.name.Contains("medium")) pines.Add(i);
                    else if (tamni < 0 && (m.name == "grm_tamni" || (mat && mat.name.EndsWith("rose_tamni")))) tamni = i;   // (Flat keeps the package mesh when no reshape is needed)
                    else if (oleander < 0 && m.name.StartsWith("oleander_uski")) oleander = i;
                }
                if (crnika < 0) crnika = olive;
                if (pines.Count == 0) pines.Add(0);
            }
        }

        public static string Apply(Magistrala.TreeRenderer tr, List<ModelJ> models)
        {
            string path = WorldBuilder.GEN + "/trees.bytes";
            if (!On || !tr || !File.Exists(path) || !File.Exists(PATH)) return "[stabla] Mapillary trees: off / no data";
            var bytes = File.ReadAllBytes(path);
            // the records added before
            var old = new HashSet<string>();
            if (File.Exists(ADDED)) { var a = File.ReadAllBytes(ADDED); for (int o = 0; o + REC <= a.Length; o += REC) old.Add(Convert.ToBase64String(a, o, REC)); }
            var rec = new List<byte>(bytes.Length + 64 * 1024); int removed = 0;
            var P = new Protos(tr);
            // our trees (≥ 2.5 m tall) on an 8 m grid
            var grid = new Dictionary<long, List<Vector2>>();
            long Key(double x, double z) => ((long)Math.Floor(x / 8) << 32) ^ (uint)Math.Floor(z / 8);
            for (int o = 0; o + REC <= bytes.Length; o += REC)
            {
                if (old.Count > 0 && old.Contains(Convert.ToBase64String(bytes, o, REC))) { removed++; continue; }
                rec.AddRange(new ArraySegment<byte>(bytes, o, REC));
                int p = (int)BitConverter.ToSingle(bytes, o + 20); float s = BitConverter.ToSingle(bytes, o + 16);
                if (p < 0 || p >= P.height.Length || P.height[p] * s < 2.5f) continue;
                var g = W.G(new Vector3(BitConverter.ToSingle(bytes, o), 0, BitConverter.ToSingle(bytes, o + 8)));
                long k = Key(g.x, g.y); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Vector2>(); l.Add(g);
            }
            double Nearest(double x, double z, double R)
            {
                double best = double.PositiveInfinity; int r = (int)Math.Ceiling(R / 8);
                for (int i = -r; i <= r; i++) for (int j = -r; j <= r; j++)
                        if (grid.TryGetValue(Key(x + i * 8, z + j * 8), out var l)) foreach (var q in l) best = Math.Min(best, Math.Sqrt((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z)));
                return best;
            }
            // parked cars (oriented boxes) and props / signs / signals (renderer bounds), game frame
            var cars = new List<(double x, double z, double fx, double fz)>();
            foreach (var pc in UnityEngine.Object.FindObjectsByType<Magistrala.Traffic.ParkedCars>(FindObjectsSortMode.None))
                if (pc.cars != null) foreach (var c in pc.cars) { var g = W.G(new Vector3(c.x, 0, c.z)); float a = c.w * Mathf.Deg2Rad; cars.Add((g.x, g.y, Math.Sin(a), -Math.Cos(a))); }
            var boxes = new List<(double x0, double z0, double x1, double z1)>();
            var world = GameObject.Find("World");
            if (world)
                foreach (var name in new[] { "Props", "Signs", "Signals" })
                {
                    var t = world.transform.Find(name); if (!t) continue;
                    foreach (var r in t.GetComponentsInChildren<Renderer>())
                    {
                        var b = r.bounds; if (b.size.x > 14 || b.size.z > 14) continue;
                        var a = W.G(b.min); var c = W.G(b.max);
                        boxes.Add((Math.Min(a.x, c.x), Math.Min(a.y, c.y), Math.Max(a.x, c.x), Math.Max(a.y, c.y)));
                    }
                }
            bool NearObstacle(double x, double z)
            {
                foreach (var c in cars)
                {
                    double dx = x - c.x, dz = z - c.z; if (dx * dx + dz * dz > 25) continue;
                    double u = Math.Abs(dx * c.fx + dz * c.fz), v = Math.Abs(-dx * c.fz + dz * c.fx);   // along / across the car
                    if (u < 2.6 + 1.5 && v < 1.0 + 1.5) return true;
                }
                foreach (var b in boxes) if (x > b.x0 - 1.5 && x < b.x1 + 1.5 && z > b.z0 - 1.5 && z < b.z1 + 1.5) return true;
                return false;
            }

            var add = new List<byte>(); var why = new Dictionary<string, int>(); var kinds = new Dictionary<string, int>();
            void No(string w) { why.TryGetValue(w, out int n); why[w] = n + 1; }
            void Put(double gx, double y, double gz, float yaw, float s, int proto, string kind)
            {
                var u = W.U(gx, y, gz); var one = new List<byte>(REC);
                foreach (var v in new[] { u.x, u.y, u.z, yaw, s, proto }) one.AddRange(BitConverter.GetBytes(v));
                rec.AddRange(one); add.AddRange(one); kinds.TryGetValue(kind, out int n); kinds[kind] = n + 1;
            }
            int trees = 0, cand = 0;
            foreach (var t in Load())
            {
                if (!Taken(t)) { No("conf / patch / palm"); continue; }
                cand++;
                double x = t.gx, z = t.gz, R = Math.Max(1, t.d / 2);
                if (!NaturePass.In(x, z)) { No("outside"); continue; }
                if (Nearest(x, z, R + 2) < R + 2) { No("ours there"); continue; }
                double clear = Math.Max(0, .4 * R - 1);   // a wide crown stands further off the street (no low branches over a truck)
                if (StreetsBuilder.Covered(x, z, clear) || WorldData.SurfDist(models, x, z) < 2 + clear) { No("street"); continue; }
                if ((VegetationMask.At(x, z) & VegetationMask.D8CLEAR) != 0) { No("D8 edge 0-8 m"); continue; }
                if (VegetationMask.NearBuilding(x, z) || WorldData.FootDist(models, x, z) < 2) { No("building"); continue; }
                if (GroundCover.Hard(x, z, 1.5)) { No("car park / pitch"); continue; }
                if (RivaBuilder.NoTree(x, z)) { No("riva"); continue; }
                if (NearObstacle(x, z)) { No("parked car / prop"); continue; }
                float h = (float)Tiles.Height(x, z);
                var q = WorldData.ModelTerrain(models, x, z, 2, h); if (q.HasValue) h = (float)q.Value;
                if (h < .4f) { No("sea"); continue; }
                float r = NaturePass.R01(x, z, 91), yaw = r * 360;
                double top = t.h > 0 ? t.h : 0;
                float Sc(int proto, double want) { float H = proto >= 0 && proto < P.height.Length ? P.height[proto] : 0; return H > 0 ? (float)(want / H) : 1; }
                switch (t.type)
                {
                    case "hedge":
                        {
                            if (P.tamni < 0) { No("no bush prototype"); continue; }
                            double hh = Math.Max(1, Math.Min(2.3, top > 0 ? top : 1.6));
                            int k = Math.Max(1, Math.Min(5, (int)Math.Round(t.d * t.d / 8)));
                            int put = 0;
                            for (int i = 0; i < k; i++)
                            {
                                double a = (i + r) * 2.39996, rr = k == 1 ? 0 : (R - .7) * Math.Sqrt((i + .5) / k);
                                double bx = x + Math.Cos(a) * rr, bz = z + Math.Sin(a) * rr;
                                if (StreetsBuilder.Covered(bx, bz, 0) || VegetationMask.NearBuilding(bx, bz) || NearObstacle(bx, bz)) continue;
                                Put(bx, Tiles.Height(bx, bz) - .05, bz, yaw + i * 77, Sc(P.tamni, hh * (.9 + .2 * NaturePass.R01(bx, bz, 92))), P.tamni, "hedge bushes"); put++;
                            }
                            if (put > 0) { trees++; kinds.TryGetValue("hedge", out int nh); kinds["hedge"] = nh + 1; } else No("street");
                            continue;
                        }
                    case "oleander":
                        if (P.oleander < 0) { No("no oleander prototype"); continue; }
                        Put(x, h - .05, z, yaw, Sc(P.oleander, Math.Max(1.8, Math.Min(5, top > 0 ? top : 3))), P.oleander, "oleander"); break;
                    case "olive":
                        Put(x, h - .08, z, yaw, Sc(P.olive, Math.Max(2.5, Math.Min(7, top > 0 ? top : .8 * t.d + 1))), P.olive, "olive"); break;
                    case "cypress":
                        Put(x, h - .08, z, yaw, Sc(P.cypress, Math.Max(5, Math.Min(16, top > 0 ? top : 8))), P.cypress, "cypress"); break;
                    case "pine":
                        {
                            int p = P.pines[Math.Min(P.pines.Count - 1, (int)(NaturePass.R01(x, z, 93) * P.pines.Count))];
                            Put(x, h - .1, z, yaw, Sc(p, Math.Max(5, Math.Min(14, top > 0 ? top : 1.1 * t.d))), p, "pine"); break;
                        }
                    default:   // other broadleaf
                        Put(x, h - .08, z, yaw, Sc(P.crnika, Math.Max(2.8, Math.Min(11, top > 0 ? top : .8 * t.d + 1))), P.crnika, "broadleaf (crnika)"); break;
                }
                trees++;
            }
            File.WriteAllBytes(path, rec.ToArray()); AssetDatabase.ImportAsset(path);
            Directory.CreateDirectory(Path.GetDirectoryName(ADDED)); File.WriteAllBytes(ADDED, add.ToArray());
            tr.data = AssetDatabase.LoadAssetAtPath<TextAsset>(path); EditorUtility.SetDirty(tr);
            return $"[stabla] Mapillary trees: {trees} of {cand} candidates added ({add.Count / REC} plants; " + string.Join(", ", kinds.Select(k => $"{k.Key} {k.Value}")) +
                   $"; {removed} old records replaced); skipped: " + string.Join(", ", why.OrderByDescending(w => w.Value).Select(w => $"{w.Key} {w.Value}"));
        }
    }
}
