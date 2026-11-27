// Mapillary street trees (docs/mapillary-izvlacenje.md, "Trees"): Assets/Editor/stabla-mapillary.json is BalkanDriver
// tools/world/mapillary/stabla.json + stabla-all.json (s4_trees.py; Street-level imagery: Mapillary contributors, CC BY-SA 4.0),
// merged by Tools/mapillary/spoji.py: crowns found in the DGU LiDAR orthophoto 1–25 m beyond the old road (6137), the D8 and
// (26. 11.) all the other streets, typed and measured (top height) from the street photos. road: s old road, d D8, u the others.
// Only the crowns the game lacks are added, to the TreeRenderer's trees.bytes (no new prototype, no new draw call kind):
//   * taken      conf ≥ MIN_CONF (seen in ≥ 1 segmented street photo); on the old road and the other streets (village streets,
//                little segmented imagery; OrthoStreets) also the orthophoto-only crowns (conf 0.15–0.25) of 3–12 m that are not
//                hedges; never a `patch` (scrub / grove
//                along the D8, not one crown); palms (27. 11., ExtraTrees "palma") at conf ≥ PALM_CONF (all are 0.3: the classifier
//                is unsure of them) unless within 2.5 m of a Mapillary street light / pole (it also takes lamps for palms)
//   * species    other broadleaf → the three broadleaf street trees (ExtraTrees lisnato_a / b / c by crown shape, else crnika),
//                palm → palma (uniform scale, 4–12 m), olive → olive, pine → the package's Aleppo-shaped pines,
//                cypress → cypress, oleander → oleander-roza, hedge → 1–5 dark bushes (grm-tamni) over the crown; scaled to the
//                measured top height (else from the crown Ø)
//   * skipped    a tree of ours (≥ 2.5 m tall) within crown radius + 2 m; on / near a street, footway or model asphalt (more off
//                it the wider the crown), within 1 m of a building (2 m of a model one), car parks / pitches (GroundCover.Hard),
//                the riva paving (RivaBuilder.NoTree), 0–8 m from the D8 edge (VegetationMask D8CLEAR, docs/vegetacija-d8.md),
//                within 1.5 m of a parked car's box or of a prop / sign / signal; the trunk (0.5 m) or the lower crown touching a
//                building's / wall's collider (World/Buildings: houses, yard and frontage walls – so after Build tile buildings);
//                the trunk at least 0.8 m off a street / footway edge
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
        public const float MIN_CONF = .35f, PALM_CONF = .3f;
        public static bool On = true;
        /// false: the orthophoto-only crowns of the other streets (road u) are not taken (only the seen ones, conf ≥ MIN_CONF)
        public static bool OrthoStreets = true;
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
            if (t.patch) return false;
            if (t.type == "palm") return t.conf >= PALM_CONF;
            if (t.conf >= MIN_CONF) return true;
            return (t.road == "s" || (t.road == "u" && OrthoStreets)) && t.type != "hedge" && t.d >= 3 && t.d <= 12;
        }

        /// prototype indices of the scene's TreeRenderer (ours 0–7 by TreeProtos.IDS, the NaturePass / MedianHedge extras by mesh /
        /// material name)
        class Protos
        {
            public int olive = 2, cypress = 3, crnika = -1, tamni = -1, oleander = -1, palm = -1; public List<int> pines = new List<int>();
            /// the broadleaf street trees (ExtraTrees): a tall narrow, b wide umbrella, c round; −1 none
            public int lisA = -1, lisB = -1, lisC = -1;
            public float[] height;
            public Bounds[] bounds;   // (tr must be World/Vegetation, not the AC units' TreeRenderer)
            public Protos(Magistrala.TreeRenderer tr)
            {
                height = tr.meshes.Select(m => m ? m.bounds.size.y : 0).ToArray();
                bounds = tr.meshes.Select(m => m ? m.bounds : new Bounds()).ToArray();
                for (int i = TreeProtos.IDS.Length; i < tr.meshes.Length; i++)
                {
                    var m = tr.meshes[i]; if (!m) continue; var mat = tr.materials[tr.materialStart[i]];
                    if (m.name.StartsWith("palma")) { palm = i; continue; }
                    if (m.name.StartsWith("lisnato_a")) { lisA = i; continue; }
                    if (m.name.StartsWith("lisnato_b")) { lisB = i; continue; }
                    if (m.name.StartsWith("lisnato_c")) { lisC = i; continue; }
                    if (crnika < 0 && mat && mat.name.Contains("crnika")) crnika = i;
                    else if (m.name.StartsWith("pine_") && !m.name.Contains("medium") && !m.name.Contains("pinija")
                        && TreeProtos.StreetShape(m.bounds, .35f, 1.15f)) pines.Add(i);
                    else if (tamni < 0 && (m.name == "grm_tamni" || (mat && mat.name.EndsWith("rose_tamni")))) tamni = i;   // (Flat keeps the package mesh when no reshape is needed)
                    else if (oleander < 0 && m.name.StartsWith("oleander_uski")) oleander = i;
                }
                if (crnika < 0) crnika = olive;
                if (pines.Count == 0) pines.Add(0); // existing full-size pine_a, not the procedural umbrella
            }

            public float StreetScale(int proto, double want, float minH, float maxH, float maxW,
                float minRatio, float maxRatio)
            {
                if (proto < 0 || proto >= bounds.Length || !TreeProtos.StreetShape(bounds[proto], minRatio, maxRatio))
                    throw new InvalidDataException($"[stabla] unsuitable street prototype {proto}: check native bounds before placing trees");
                return TreeProtos.StreetScale(bounds[proto], (float)want, minH, maxH, maxW);
            }
        }

        /// the broadleaf street tree for a crown of Ø d and top height h (r: 0..1 hash): narrow crowns the tall maple, wide ones the
        /// beech, the rest mostly the round maple (a third of them one of the others, for variety); −1 none of them in the scene
        static int Broadleaf(Protos P, double d, double h, float r)
        {
            var have = new[] { P.lisA, P.lisB, P.lisC }.Where(i => i >= 0).ToArray(); if (have.Length == 0) return -1;
            double ratio = d / Math.Max(1, h);
            int pick = ratio < .55 ? P.lisA : ratio > 1.0 ? P.lisB : P.lisC;
            if (r < .33f || pick < 0) pick = have[(int)(r * 3 * have.Length) % have.Length];
            return pick;
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
            // houses, yard / frontage walls (their hull-level colliders)
            var built = new HashSet<Collider>(); var bRoot = world ? world.transform.Find("Buildings") : null;
            if (bRoot) foreach (var c in bRoot.GetComponentsInChildren<Collider>()) built.Add(c);
            Physics.SyncTransforms(); var ov = new Collider[32];
            bool InBuilt(double x, double y, double z, double crownR, double top)
            {
                if (built.Count == 0) return false;
                var u = W.U(x, y, z);
                int n = Physics.OverlapCapsuleNonAlloc(u + Vector3.up * .2f, u + Vector3.up * 2.5f, .5f, ov, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) if (built.Contains(ov[i])) return true;
                if (crownR <= 0) return false;
                n = Physics.OverlapSphereNonAlloc(u + Vector3.up * (float)Math.Max(2.5, .6 * top), (float)(.4 * crownR), ov, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++) if (built.Contains(ov[i])) return true;
                return false;
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

            // Mapillary street lights / poles (palms: the classifier takes lamps for palms)
            var lampPts = new Dictionary<long, List<Vector2>>();
            if (File.Exists("Assets/Editor/rasvjeta-mapillary.json"))
                foreach (var lo in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText("Assets/Editor/rasvjeta-mapillary.json")))["lights"]))
                { var l = MiniJson.L(lo); long k = Key(MiniJson.D(l[0]), MiniJson.D(l[1])); if (!lampPts.TryGetValue(k, out var ll)) lampPts[k] = ll = new List<Vector2>(); ll.Add(new Vector2(MiniJson.F(l[0]), MiniJson.F(l[1]))); }
            bool NearLamp(double x, double z, double R)
            {
                for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++)
                        if (lampPts.TryGetValue(Key(x + i * 8, z + j * 8), out var l)) foreach (var q in l) if ((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z) < R * R) return true;
                return false;
            }
            var add = new List<byte>(); var why = new Dictionary<string, int>(); var kinds = new Dictionary<string, int>();
            int unchanged = 0;
            void No(string w) { why.TryGetValue(w, out int n); why[w] = n + 1; }
            void Put(double gx, double y, double gz, float yaw, float s, int proto, string kind)
            {
                var u = W.U(gx, y, gz); var one = new List<byte>(REC);
                foreach (var v in new[] { u.x, u.y, u.z, yaw, s, proto }) one.AddRange(BitConverter.GetBytes(v));
                if (old.Contains(Convert.ToBase64String(one.ToArray()))) unchanged++;
                rec.AddRange(one); add.AddRange(one); kinds.TryGetValue(kind, out int n); kinds[kind] = n + 1;
            }
            int trees = 0, cand = 0; var byRoad = new Dictionary<string, int>();
            var all = Load();
            foreach (var t in all)
            {
                if (!Taken(t)) { No("conf / patch / palm"); continue; }
                cand++;
                double x = t.gx, z = t.gz, R = Math.Max(1, t.d / 2);
                if (!NaturePass.In(x, z)) { No("outside"); continue; }
                if (Nearest(x, z, R + 2) < R + 2) { No("ours there"); continue; }
                double clear = Math.Max(.8, .4 * R - 1);   // a wide crown stands further off the street (no low branches over a truck); a trunk ≥ 0.8 m off its edge
                if (StreetsBuilder.Covered(x, z, clear) || WorldData.SurfDist(models, x, z) < 2 + clear) { No("street"); continue; }
                if ((VegetationMask.At(x, z) & VegetationMask.D8CLEAR) != 0) { No("D8 edge 0-8 m"); continue; }
                if (VegetationMask.NearBuilding(x, z) || WorldData.FootDist(models, x, z) < 2) { No("building"); continue; }
                if (GroundCover.Hard(x, z, 1.5)) { No("car park / pitch"); continue; }
                if (RivaBuilder.NoTree(x, z)) { No("riva"); continue; }
                if (NearObstacle(x, z)) { No("parked car / prop"); continue; }
                float h = (float)Tiles.Height(x, z);
                var q = WorldData.ModelTerrain(models, x, z, 2, h); if (q.HasValue) h = (float)q.Value;
                if (h < .4f) { No("sea"); continue; }
                double topH = t.h > 0 ? t.h : t.type == "hedge" ? 1.6 : .8 * t.d + 1;
                if (InBuilt(x, h, z, t.type == "hedge" ? 0 : R, topH)) { No("building / wall"); continue; }
                float r = NaturePass.R01(x, z, 91), yaw = r * 360;
                double top = t.h > 0 ? t.h : 0;
                float Sc(int proto, double want) { float H = proto >= 0 && proto < P.height.Length ? P.height[proto] : 0; return H > 0 ? (float)(want / H) : 1; }
                switch (t.type)
                {
                    case "palm":
                        {
                            if (P.palm < 0) { No("no palm prototype"); continue; }
                            if (NearLamp(x, z, 2.5)) { No("palm = street light?"); continue; }
                            // uniform scale to the measured top height, 4–12 m (no width cap: fronds spread ~ the height)
                            Put(x, h - .08, z, yaw, P.StreetScale(P.palm, top > 0 ? top : Math.Max(5, t.d), 4, 12, 14, .3f, 2f), P.palm, "palm"); break;
                        }
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
                            if (put > 0) { trees++; kinds.TryGetValue("hedge", out int nh); kinds["hedge"] = nh + 1; byRoad.TryGetValue(t.road, out int nh2); byRoad[t.road] = nh2 + 1; } else No("street");
                            continue;
                        }
                    case "oleander":
                        if (P.oleander < 0) { No("no oleander prototype"); continue; }
                        Put(x, h - .05, z, yaw, Sc(P.oleander, Math.Max(1.8, Math.Min(5, top > 0 ? top : 3))), P.oleander, "oleander"); break;
                    case "olive":
                        Put(x, h - .08, z, yaw, P.StreetScale(P.olive, top > 0 ? top : .8 * t.d + 1,
                            2.5f, 6, 8, .65f, 1.6f), P.olive, "olive"); break;
                    case "cypress":
                        Put(x, h - .08, z, yaw, P.StreetScale(P.cypress, top > 0 ? top : 8,
                            5, 14, 5, .12f, .5f), P.cypress, "cypress"); break;
                    case "pine":
                        {
                            int p = P.pines[Math.Min(P.pines.Count - 1, (int)(NaturePass.R01(x, z, 93) * P.pines.Count))];
                            Put(x, h - .1, z, yaw, P.StreetScale(p, top > 0 ? top : 1.1 * t.d,
                                5, 12, 12, .35f, 1.15f), p, "pine"); break;
                        }
                    default:   // other broadleaf: the broadleaf street trees (27. 11.), crnika where none fits
                        {
                            double want = top > 0 ? top : .8 * t.d + 1;
                            int lp = Broadleaf(P, t.d, want, NaturePass.R01(x, z, 94));
                            if (lp >= 0 && want >= 4)
                            {
                                var (mn, mx, mw) = lp == P.lisA ? (6f, 12f, 7f) : lp == P.lisB ? (5f, 10f, 12f) : (5f, 10f, 10f);
                                Put(x, h - .08, z, yaw, P.StreetScale(lp, want, mn, mx, mw, .3f, 1.8f), lp, "broadleaf (street tree)"); break;
                            }
                            Put(x, h - .08, z, yaw, P.StreetScale(P.crnika, want,
                                2.8f, 9, 10, .65f, 1.6f), P.crnika, "broadleaf (crnika)"); break;
                        }
                }
                trees++; byRoad.TryGetValue(t.road, out int nr); byRoad[t.road] = nr + 1;
            }
            File.WriteAllBytes(path, rec.ToArray()); AssetDatabase.ImportAsset(path);
            Directory.CreateDirectory(Path.GetDirectoryName(ADDED)); File.WriteAllBytes(ADDED, add.ToArray());
            tr.data = AssetDatabase.LoadAssetAtPath<TextAsset>(path); EditorUtility.SetDirty(tr);
            return $"[stabla] Mapillary trees: {trees} of {cand} candidates added (of {all.Count} records; by road " + string.Join(", ", byRoad.Select(k => $"{k.Key} {k.Value}")) + $"; {add.Count / REC} plants; " + string.Join(", ", kinds.Select(k => $"{k.Key} {k.Value}")) +
                   $"; {removed} old records replaced; {unchanged} identical, {add.Count / REC - unchanged} new/changed plant records, {old.Count - unchanged} old records superseded); skipped: " + string.Join(", ", why.OrderByDescending(w => w.Value).Select(w => $"{w.Key} {w.Value}"));
        }
    }
}
