// Drvoredi (Marko 23. 11.: the surveyed street trees may be used): drvoredi.json of mag-24-11 (PaketData.DRVOREDI: the corrected
// Grgura Ninskog, + the park at the bottom of Vukovarska, Park Đardin, the park by Ulica Frane Ćipina), positions and sizes he
// read from aerial and street imagery (VEGETACIJA_I_TLO.md "Surveyed vegetation", STABLA.md); nothing of the imagery is in the game.
// Package frame (x east, z south) → game through paket-approaches' affine, as the other package data. Per street:
//   band     the street's axis (centroid across of the surveyed plants per 20 m along the street's main direction) ± 40 m: every
//            plant the rules / tile data / Mapillary put there is removed, so the survey wins and nothing is doubled
//   trees    on the existing prototypes, scaled to heightM, dropped onto the ground colliders (VegetationClean.Surface):
//            broadleaf → olive mesh, evergreen → crnika on Bana Jelačića (checked from the street: dark evergreen broadleaves),
//            pine (crown ≥ 4 m) or cypress on Grgura Ninskog, pine → the package's tall-trunk pines, small → crnika, bush →
//            grm-tamni; palms are left out (no palm prototype yet; the riva's own kerb-row trees stay in their pits). A tree
//            inside a building footprint (Grgura Ninskog could not be checked against them) moves out by the shortest way
//   rows     a plant every spacingM along the line (small 3 m crnika, shrub 1.6 m grm-tamni, evergreen 5 m crnika)
//   hedges   the yard hedge body (zivica-blok) every 0.8 m at heightM
//   areas    by the matching rule: olive-grove / orchard = olives on a 7.5 m grid, dense = a tree per 30 m² and bushes, scrub /
//            bush = a bush per 25 m² and a tree per 150 m²; grass / dry-grass / vineyard / bare: nothing planted. Area plants keep
//            2.5 m (trees) / 1.2 m (bushes) off asphalt and 1 m off buildings
// Seeded by position (same plants every build). Build trees runs it after VegetationClean; menu Magistrala/Vegetacija: drvoredi
// runs it on the current trees.bytes (a second run gives the same result: the band is cleared first).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class Drvoredi
    {
        public const string SRC = PaketData.DRVOREDI;
        public static bool On = true;
        const double BAND = 40, BIN = 20;

        [MenuItem("Magistrala/Vegetacija: drvoredi")]
        public static string Menu()
        {
            MainScene.Ensure();
            var tr = GameObject.Find("World")?.transform.Find("Vegetation")?.GetComponent<Magistrala.TreeRenderer>();   // (not the AC units' TreeRenderer)
            if (!tr) return "no TreeRenderer (Build trees first)";
            var s = Apply(tr, WorldData.Models()); Debug.Log(s); return s;
        }

        struct P2 { public double x, z; public P2(double x, double z) { this.x = x; this.z = z; } }
        class Band { public string name; public double ox, oz, ax, az; public List<(double s, double c)> axis = new List<(double, double)>(); public double s0, s1; }

        public static string Apply(Magistrala.TreeRenderer tr, List<ModelJ> models)
        {
            string path = WorldBuilder.GEN + "/trees.bytes"; if (!On || !File.Exists(path)) return "[drvoredi] off / no trees.bytes"; PaketData.Need(SRC);
            var pa = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "paket-approaches.json")));
            var T = MiniJson.O(MiniJson.G(MiniJson.O(pa["alignment"]), "transform") ?? pa["transform"]);
            double Ta = MiniJson.D(T["a"]), Tb = MiniJson.D(T["b"]), Tc = MiniJson.D(T["c"]), Td = MiniJson.D(T["d"]), Ttx = MiniJson.D(T["tx"]), Ttz = MiniJson.D(T["tz"]);
            P2 G(double x, double z) => new P2(Ta * x + Tb * z + Ttx, Tc * x + Td * z + Ttz);
            List<P2> Line(object o) => MiniJson.L(o).Select(q => { var l = MiniJson.L(q); return G(MiniJson.D(l[0]), MiniJson.D(l[1])); }).ToList();

            // prototypes by name (the tinted copies carry their kind in the material name)
            int np = tr.meshes.Length;
            int Find(Func<string, bool> f) { for (int p = 0; p < np; p++) if (tr.meshes[p] && f(tr.meshes[p].name.ToLowerInvariant() + " " + VegetationClean.Mat(tr, p))) return p; return -1; }
            int olive = Find(n => n.StartsWith("olive") && !n.Contains("crnika")), crn = Find(n => n.Contains("crnika")), cyp = Find(n => n.StartsWith("cypress"));
            int bush = Find(n => n.Contains("tamni")), blok = Find(n => n.StartsWith("zivica_blok"));
            var pines = Enumerable.Range(0, np).Where(p => tr.meshes[p] && tr.meshes[p].name.StartsWith("pine_0")).ToList();   // package Aleppo pines (pine_01_a …, tall trunks)
            if (pines.Count == 0) pines = Enumerable.Range(0, Math.Min(2, np)).ToList();
            if (crn < 0) crn = olive; if (bush < 0) bush = 4;
            float H(int p) => tr.meshes[p] ? Mathf.Max(.3f, tr.meshes[p].bounds.size.y) : 3;

            Physics.SyncTransforms();
            var roots = new[] { "Terrain", "Roads", "Streets", "Riva", "Tlo" }.Select(r => GameObject.Find("World/" + r)).Where(g => g).Select(g => g.transform).ToArray();
            var modelRoot = GameObject.Find("World/Roads")?.transform;

            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(SRC)));
            var bands = new List<Band>(); var add = new List<(P2 g, float h, int proto, bool tree)>(); var report = new System.Text.StringBuilder();
            int si = 0;
            foreach (var so in MiniJson.L(root["streets"]))
            {
                var st = MiniJson.O(so); string name = (string)st["name"]; bool second = si++ > 0;
                int nT = 0, nR = 0, nH = 0, nA = 0, nPalm = 0, nMoved = 0;
                var pts = new List<P2>();
                foreach (var to in MiniJson.L(st["trees"]))
                {
                    var t = MiniJson.O(to); var g = G(MiniJson.D(t["x"]), MiniJson.D(t["z"])); pts.Add(g);
                    string kind = (string)t["kind"]; float h = MiniJson.F(t["heightM"]), crown = MiniJson.F(t["crownM"]);
                    int p = kind switch
                    {
                        "broadleaf" => olive, "olive" => olive, "small" => crn, "bush" => bush,
                        "pine" => pines[(int)(NaturePass.R01(g.x, g.z, 931) * pines.Count) % pines.Count],
                        "evergreen" => second ? (crown >= 4 ? pines[(int)(NaturePass.R01(g.x, g.z, 932) * pines.Count) % pines.Count] : cyp) : crn,
                        _ => -1,
                    };
                    if (kind == "palm" || p < 0) { nPalm++; continue; }
                    if (kind != "pine" && VegetationMask.NearBuilding(g.x, g.z)) { if (OutOfHouse(ref g)) nMoved++; }
                    add.Add((g, h, p, kind != "bush")); nT++;
                }
                foreach (var ro in MiniJson.L(MiniJson.G(st, "rows") ?? new List<object>()))
                {
                    var r = MiniJson.O(ro); var line = Line(r["points"]); pts.AddRange(line);
                    string kind = (string)r["kind"]; double sp = Math.Max(1, MiniJson.D(r["spacingM"]));
                    var (p, h) = kind == "shrub" ? (bush, 1.6f) : kind == "evergreen" ? (crn, 5f) : (crn, 3f);
                    foreach (var g in Along(line, sp, sp * .5)) { add.Add((g, h * (.9f + .2f * NaturePass.R01(g.x, g.z, 933)), p, kind != "shrub")); nR++; }
                }
                foreach (var ho in MiniJson.L(MiniJson.G(st, "hedges") ?? new List<object>()))
                {
                    var hd = MiniJson.O(ho); var line = Line(hd["points"]); pts.AddRange(line);
                    float h = Mathf.Clamp(MiniJson.F(hd["heightM"]), 1.1f, 2.6f); int p = blok >= 0 ? blok : bush;
                    foreach (var g in Along(line, .8, .4)) { add.Add((g, h, p, false)); nH++; }
                }
                foreach (var ao in MiniJson.L(MiniJson.G(st, "areas") ?? new List<object>()))
                {
                    var a = MiniJson.O(ao); var poly = Line(a["outline"]); pts.AddRange(poly); nA += Fill((string)a["kind"], poly, add, olive, crn, bush, models);
                }
                bands.Add(MakeBand(name, pts));
                report.Append($" {name}: trees {nT} (palms left out {nPalm}, moved out of houses {nMoved}), row plants {nR}, hedge plants {nH}, area plants {nA};");
            }

            // clear the bands, then add the survey
            // the riva's kerb-row trees stay (RivaBuilder tree pits; the survey has only palms there, which we lack: without them
            // the pits stood empty)
            var riva = new HashSet<long>(RivaBuilder.Trees().Select(t => VegetationClean.Key(t.x, t.z)));
            var b = File.ReadAllBytes(path); var outB = new List<byte>(b.Length); int removed = 0;
            for (int o = 0; o + 24 <= b.Length; o += 24)
            {
                var g = W.G(new Vector3(BitConverter.ToSingle(b, o), 0, BitConverter.ToSingle(b, o + 8)));
                if (!riva.Contains(VegetationClean.Key(g.x, g.y)) && bands.Any(bd => InBand(bd, g.x, g.y))) { removed++; continue; }
                for (int k = 0; k < 24; k++) outB.Add(b[o + k]);
            }
            int added = 0;
            var packageModels = models.Where(m => m.key.StartsWith("krizanje-") && int.TryParse(m.key.Substring(9), out int id) && id >= 66 && id <= 82).ToList();
            foreach (var (g, h, p, tree) in add)
            {
                if (p < 0 || p >= np) continue;
                bool onRoad = false;
                foreach (var model in packageModels) if (!double.IsNaN(model.Surface(g.x, g.z))) { onRoad = true; break; }
                if (onRoad) continue;
                double y0 = Tiles.Height(g.x, g.z); var q = WorldData.ModelTerrain(models, g.x, g.z, 2, y0); if (q.HasValue) y0 = q.Value;
                var u = W.U(g.x, y0, g.z);
                if (VegetationClean.Surface(u, roots, modelRoot, out float sy, out _, out _)) u.y = sy;
                u.y -= tree ? .12f : .05f;
                float s = h / H(p), yaw = NaturePass.R01(g.x, g.z, 934) * 360;
                foreach (var v in new[] { u.x, u.y, u.z, yaw, s, p }) outB.AddRange(BitConverter.GetBytes(v));
                added++;
            }
            File.WriteAllBytes(path, outB.ToArray()); AssetDatabase.ImportAsset(path);
            return $"[drvoredi] removed {removed} rule / tile plants in the bands, added {added}:{report}";
        }

        /// the survey's bands as a test on game x / z (null = no survey file): VegetationClean leaves these plants alone
        public static Func<double, double, bool> BandTest()
        {
            if (!On || !File.Exists(SRC)) return null;
            var pa = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "paket-approaches.json")));
            var T = MiniJson.O(MiniJson.G(MiniJson.O(pa["alignment"]), "transform") ?? pa["transform"]);
            double Ta = MiniJson.D(T["a"]), Tb = MiniJson.D(T["b"]), Tc = MiniJson.D(T["c"]), Td = MiniJson.D(T["d"]), Ttx = MiniJson.D(T["tx"]), Ttz = MiniJson.D(T["tz"]);
            P2 G(List<object> l) { double x = MiniJson.D(l[0]), z = MiniJson.D(l[1]); return new P2(Ta * x + Tb * z + Ttx, Tc * x + Td * z + Ttz); }
            var bands = new List<Band>();
            foreach (var so in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(SRC)))["streets"]))
            {
                var st = MiniJson.O(so); var pts = new List<P2>();
                foreach (var to in MiniJson.L(st["trees"])) { var t = MiniJson.O(to); pts.Add(G(new List<object> { t["x"], t["z"] })); }
                foreach (var k in new[] { "rows", "hedges" }) foreach (var o in MiniJson.L(MiniJson.G(st, k) ?? new List<object>())) foreach (var q in MiniJson.L(MiniJson.O(o)["points"])) pts.Add(G(MiniJson.L(q)));
                foreach (var o in MiniJson.L(MiniJson.G(st, "areas") ?? new List<object>())) foreach (var q in MiniJson.L(MiniJson.O(o)["outline"])) pts.Add(G(MiniJson.L(q)));
                bands.Add(MakeBand((string)st["name"], pts));
            }
            return (x, z) => bands.Any(b => InBand(b, x, z));
        }

        /// points every `step` m along a polyline, the first `first` m in
        static IEnumerable<P2> Along(List<P2> line, double step, double first)
        {
            double carry = first;
            for (int i = 0; i + 1 < line.Count; i++)
            {
                var a = line[i]; double dx = line[i + 1].x - a.x, dz = line[i + 1].z - a.z, L = Math.Sqrt(dx * dx + dz * dz); if (L < 1e-6) continue;
                double t = carry; for (; t <= L; t += step) yield return new P2(a.x + dx * t / L, a.z + dz * t / L);
                carry = t - L;
            }
        }

        /// an area filled by the rule of its kind (jittered grid, seeded by position)
        static int Fill(string kind, List<P2> poly, List<(P2, float, int, bool)> add, int olive, int crn, int bush, List<ModelJ> models)
        {
            double step; float pTree, pBush;
            switch (kind)
            {
                case "olive-grove": case "orchard": step = 7.5; pTree = .92f; pBush = 0; break;
                case "dense": step = 3; pTree = 9f / 30; pBush = 9f / 20; break;
                case "park": step = 3; pTree = 9f / 120; pBush = 9f / 200; break;   // (mag-24-11 Park Đardin: about fifteen large trees, not placed one by one)
                case "scrub": case "bush": step = 3; pTree = 9f / 150; pBush = 9f / 25; break;
                default: return 0;   // grass, dry-grass, vineyard, bare: the ground layer only
            }
            double x0 = poly.Min(p => p.x), x1 = poly.Max(p => p.x), z0 = poly.Min(p => p.z), z1 = poly.Max(p => p.z); int n = 0;
            bool grove = step > 5;
            for (double z = Math.Floor(z0 / step) * step; z <= z1; z += step)
                for (double x = Math.Floor(x0 / step) * step; x <= x1; x += step)
                {
                    double jx = grove ? .3 : step * .8, gx = x + (NaturePass.R01(x, z, 941) - .5) * jx, gz = z + (NaturePass.R01(x, z, 942) - .5) * jx;
                    if (!Inside(poly, gx, gz)) continue;
                    float r = NaturePass.R01(gx, gz, 943); bool tree = r < pTree; if (!tree && r >= pTree + pBush) continue;
                    double sd = WorldData.SurfDist(models, gx, gz), clear = tree ? 2.5 : 1.2;
                    if (GustaVegetacija.NearAsphalt(models, gx, gz, sd, clear) || StreetsBuilder.Covered(gx, gz, clear) || VegetationMask.NearBuilding(gx, gz) || GroundCover.Hard(gx, gz, tree ? 2 : .5)) continue;
                    float r2 = NaturePass.R01(gx, gz, 944);
                    if (tree) add.Add((new P2(gx, gz), grove ? 3.8f + 1.4f * r2 : 3.5f + 3f * r2, grove || r2 < .5f ? olive : crn, true));
                    else add.Add((new P2(gx, gz), 1.1f + 1.2f * r2, bush, false));
                    n++;
                }
            return n;
        }

        static bool Inside(List<P2> poly, double x, double z)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].z > z) != (poly[j].z > z) && x < (poly[j].x - poly[i].x) * (z - poly[i].z) / (poly[j].z - poly[i].z) + poly[i].x) c = !c;
            return c;
        }

        /// a tree inside a building footprint: the nearest free point within 6 m (16 directions)
        static bool OutOfHouse(ref P2 g)
        {
            for (double r = .5; r <= 6; r += .5)
                for (int k = 0; k < 16; k++)
                {
                    double a = k * Math.PI / 8; var q = new P2(g.x + Math.Cos(a) * r, g.z + Math.Sin(a) * r);
                    if (!VegetationMask.NearBuilding(q.x, q.z)) { g = q; return true; }
                }
            return false;
        }

        /// the street's main direction (principal axis of its plants), the centroid across per BIN m along it
        static Band MakeBand(string name, List<P2> pts)
        {
            var bd = new Band { name = name };
            double mx = pts.Average(p => p.x), mz = pts.Average(p => p.z), sxx = 0, szz = 0, sxz = 0;
            foreach (var p in pts) { double dx = p.x - mx, dz = p.z - mz; sxx += dx * dx; szz += dz * dz; sxz += dx * dz; }
            double ang = .5 * Math.Atan2(2 * sxz, sxx - szz); bd.ox = mx; bd.oz = mz; bd.ax = Math.Cos(ang); bd.az = Math.Sin(ang);
            var proj = pts.Select(p => (s: (p.x - mx) * bd.ax + (p.z - mz) * bd.az, c: -(p.x - mx) * bd.az + (p.z - mz) * bd.ax)).ToList();
            bd.s0 = proj.Min(p => p.s) - 10; bd.s1 = proj.Max(p => p.s) + 10;
            foreach (var grp in proj.GroupBy(p => Math.Floor(p.s / BIN)).OrderBy(g => g.Key)) bd.axis.Add(((grp.Key + .5) * BIN, grp.Average(p => p.c)));
            return bd;
        }

        static bool InBand(Band bd, double x, double z)
        {
            double dx = x - bd.ox, dz = z - bd.oz, s = dx * bd.ax + dz * bd.az, c = -dx * bd.az + dz * bd.ax;
            if (s < bd.s0 || s > bd.s1 || bd.axis.Count == 0) return false;
            double ac = bd.axis[0].c;
            if (s >= bd.axis[bd.axis.Count - 1].s) ac = bd.axis[bd.axis.Count - 1].c;
            else for (int i = 0; i + 1 < bd.axis.Count; i++) if (s >= bd.axis[i].s && s < bd.axis[i + 1].s) { double t = (s - bd.axis[i].s) / (bd.axis[i + 1].s - bd.axis[i].s); ac = bd.axis[i].c + (bd.axis[i + 1].c - bd.axis[i].c) * t; break; }
            return Math.Abs(c - ac) <= BAND;
        }
    }
}
