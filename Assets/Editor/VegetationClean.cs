// Vegetacija bez glupih mjesta (23. 11., Marko kod konjanika u Kaštel Novom: "tamo stvarno nema toliko stabala, a stabla su na
// glupim mjestima"): one last pass over trees.bytes after every placer (tile trees, clumps, groves, yard hedges, the Kaštel Stari
// trial, Mapillary), inside NaturePass.Boxes:
//   keep clear  trunks ≥ 2.5 m from model asphalt / graph streets (bushes 1.2 m, hedge bodies 0.6 m) and off sidewalks / islands;
//               no low-crowned tree within 6 m of a junction model's asphalt near its centre (the sight lines at the junction
//               mouths; cypresses and tall-trunk pines stay); off car parks / pitches (GroundCover.Hard) and the delivery bays
//               of MagistralaGame.asset (+4 m along, +3 m across)
//   surface     y = the collider surface under the plant (terrain, model ground, streets, riva) nearest to the placer's height:
//               the placers used the 60 m DEM / ModelTerrain, which is off the built ground at the model strips (floating /
//               sunk plants); on model ground steeper than 35° (junction embankments) nothing grows
//   konjanik    the town around the monument is a street with scattered yard trees, not a wood: within 180 m of stop p40
//               (fading out to 300 m) 3 trees in 10 and 2 bushes in 5 are kept
// Decided by position only (no running counter): a second run changes nothing. Build trees runs it at its end; menu
// Magistrala/Vegetacija: očisti runs it on the current trees.bytes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class VegetationClean
    {
        public static bool On = true;
        static readonly Vector2 KONJANIK = new Vector2(-7911.9f, -4596.2f); // game x / z of stop p40 "Tonći, Kaštel Novi (kod konjanika)"
        const float K_IN = 180, K_OUT = 300, K_TREE = .3f, K_BUSH = .4f;

        [MenuItem("Magistrala/Vegetacija: očisti")]
        public static string Menu()
        {
            MainScene.Ensure();
            var tr = GameObject.Find("World")?.transform.Find("Vegetation")?.GetComponent<Magistrala.TreeRenderer>();   // (not the AC units' TreeRenderer)
            if (!tr) return "no TreeRenderer (Build trees first)";
            var s = Apply(tr, WorldData.Models()); Debug.Log(s); return s;
        }

        public static string Apply(Magistrala.TreeRenderer tr, List<ModelJ> models)
        {
            string path = WorldBuilder.GEN + "/trees.bytes"; if (!On || !File.Exists(path)) return "[vegetacija] clean: off";
            int np = tr.meshes.Length; var h1 = new float[np]; var hedge = new bool[np]; var tall = new bool[np]; var rock = new bool[np];
            for (int p = 0; p < np; p++)
            {
                var m = tr.meshes[p]; if (!m) continue; string n = m.name.ToLowerInvariant() + " " + Mat(tr, p);
                h1[p] = m.bounds.size.y; hedge[p] = n.Contains("zivica"); rock[p] = n.Contains("rock");
                tall[p] = n.Contains("cypress") || n.Contains("pine");   // crown high above a road user's eye (cypress, our / package pines, pinija)
            }
            var bays = new List<(Vector2 c, Vector2 d, float hl, float hw)>();
            var gw = AssetDatabase.LoadAssetAtPath<Magistrala.Game.GameWorld>("Assets/Generated/Game/Resources/MagistralaGame.asset");
            if (gw && gw.stops != null) foreach (var s in gw.stops) if (s.bayHl > 0) bays.Add((new Vector2(s.bay.x, s.bay.z), s.bayDir.normalized, s.bayHl + 4, s.bayHw + 3));
            var junctions = models.Where(m => m.roads.Count >= 3).ToList();
            Physics.SyncTransforms();
            var roots = new[] { "Terrain", "Roads", "Streets", "Riva", "Tlo" }.Select(r => GameObject.Find("World/" + r)).Where(g => g).Select(g => g.transform).ToArray();
            var modelRoot = GameObject.Find("World/Roads")?.transform;

            // kept as they are: the riva's kerb-row trees (RivaBuilder: tree pits in the paving, by design next to the road) and the
            // Mapillary street trees (seen in street photos; MapillaryTrees re-applies by their exact records, so not even moved)
            var riva = new HashSet<long>(RivaBuilder.Trees().Select(t => Key(t.x, t.z)));
            var mapillary = new HashSet<string>(); string added = WorldBuilder.GEN + "/Nature/stabla-mapillary-added.bytes";
            if (File.Exists(added)) { var a = File.ReadAllBytes(added); for (int o = 0; o + 24 <= a.Length; o += 24) mapillary.Add(Convert.ToBase64String(a, o, 24)); }
            var b = File.ReadAllBytes(path); var outB = new List<byte>(b.Length);
            int road = 0, mouth = 0, bay = 0, hard = 0, steep = 0, thin = 0, moved = 0, kept = 0;
            for (int o = 0; o + 24 <= b.Length; o += 24)
            {
                var u = new Vector3(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8));
                float s = BitConverter.ToSingle(b, o + 16); int p = (int)BitConverter.ToSingle(b, o + 20);
                var g = W.G(u); double x = g.x, z = g.y;
                if (p < 0 || p >= np || !NaturePass.In(x, z) || riva.Contains(Key(x, z)) || mapillary.Contains(Convert.ToBase64String(b, o, 24))) { Keep(b, o, outB, u.y); continue; }
                bool tree = !hedge[p] && !rock[p] && h1[p] * s >= 3;
                double sd = WorldData.SurfDist(models, x, z), r = hedge[p] ? .6 : tree ? 2.5 : 1.2;
                if (GustaVegetacija.NearAsphalt(models, x, z, sd, r) || StreetsBuilder.Covered(x, z, r)) { road++; continue; }
                if (tree && !tall[p] && sd < 6 && junctions.Any(m => Math.Abs(x - m.x) < 30 && Math.Abs(z - m.z) < 30) && GustaVegetacija.NearAsphalt(models, x, z, sd, 6)) { mouth++; continue; }
                if (GroundCover.Hard(x, z, tree ? 2 : .5)) { hard++; continue; }
                if (InBay(bays, new Vector2(u.x, u.z))) { bay++; continue; }
                float dk = Vector2.Distance(g, KONJANIK);
                if (dk < K_OUT && !hedge[p])
                {
                    float keep = Mathf.Lerp(1, tree ? K_TREE : K_BUSH, Mathf.Clamp01((K_OUT - dk) / (K_OUT - K_IN)));
                    if (NaturePass.R01(x, z, 901) > keep) { thin++; continue; }
                }
                float y = u.y;
                if (Surface(u, roots, modelRoot, out float sy, out bool onModel, out Vector3 nrm))
                {
                    if (onModel && nrm.y < .82f) { steep++; continue; }   // 35°
                    float ny = sy - (tree ? .12f : .05f); if (Mathf.Abs(ny - y) > .05f) { moved++; y = ny; }
                }
                Keep(b, o, outB, y); kept++;
            }
            File.WriteAllBytes(path, outB.ToArray()); AssetDatabase.ImportAsset(path);
            return $"[vegetacija] clean: kept {kept} in the boxes; dropped road {road}, junction mouth {mouth}, car park {hard}, bay {bay}, " +
                   $"embankment {steep}, konjanik thinning {thin}; moved onto the ground {moved}";
        }

        /// the prototype's first material name (the tinted copies carry their kind: _crnika, _tamni, _zivica)
        internal static string Mat(Magistrala.TreeRenderer tr, int p)
        {
            int i = p < tr.materialStart.Length ? tr.materialStart[p] : -1;
            return i >= 0 && i < tr.materials.Length && tr.materials[i] ? tr.materials[i].name.ToLowerInvariant() : "";
        }

        /// 0.5 m cells (the riva's trees: game x / z as in RivaBuilder, the records through W.U / W.G as float)
        static long Key(double x, double z) => ((long)Math.Round(x * 2) << 32) ^ (uint)Math.Round(z * 2);

        static void Keep(byte[] b, int o, List<byte> outB, float y)
        {
            for (int k = 0; k < 24; k++) outB.Add(b[o + k]);
            var yb = BitConverter.GetBytes(y); for (int k = 0; k < 4; k++) outB[outB.Count - 20 + k] = yb[k];
        }

        static bool InBay(List<(Vector2 c, Vector2 d, float hl, float hw)> bays, Vector2 u)
        {
            foreach (var (c, d, hl, hw) in bays)
            {
                var v = u - c; float a = Vector2.Dot(v, d), q = v.x * d.y - v.y * d.x;
                if (Mathf.Abs(a) < hl && Mathf.Abs(q) < hw) return true;
            }
            return false;
        }

        /// the ground collider (terrain or under one of roots) right under u whose height is nearest to u.y (not a bridge deck
        /// above, nor a tunnel / culvert below)
        internal static bool Surface(Vector3 u, Transform[] roots, Transform modelRoot, out float y, out bool onModel, out Vector3 nrm)
        {
            y = 0; onModel = false; nrm = Vector3.up; float best = float.MaxValue;
            foreach (var h in Physics.RaycastAll(new Vector3(u.x, u.y + 30, u.z), Vector3.down, 60, ~0, QueryTriggerInteraction.Ignore))
            {
                bool ground = h.collider is TerrainCollider || roots.Any(r => h.collider.transform.IsChildOf(r));
                if (!ground || Mathf.Abs(h.point.y - u.y) >= best) continue;
                best = Mathf.Abs(h.point.y - u.y); y = h.point.y; nrm = h.normal; onModel = modelRoot && h.collider.transform.IsChildOf(modelRoot);
            }
            return best < 8;
        }
    }
}
