// Gusta vegetacija – the Kaštel Stari trial of ~/Documents/mag-23-11-v2/VEGETACIJA_I_TLO.md (23. 11.): Marko wants the town to
// read like a lived-in Mediterranean place seen from a truck (many trees of several kinds and shades close to the road and
// between the houses, hiding plain house sides and empty patches). The existing build keeps ~10 trees / ha in built-up land;
// this pass adds, inside Boxes only, on top of the tile / orthophoto / Mapillary trees (same TreeRenderer, no new prototype,
// no GameObjects):
//   yard      land within 30 m of a building (OrthoGround footprints + the models'), 2 m off the walls: a tree per ~80 m² of
//             free ground (olive, dark evergreen broadleaf = crnika as fig / citrus / holm oak, cypress, now and then a pine),
//             a bush per ~45 m², and along the street side of about half of the yards an oleander / dark hedge plant
//   empty     open land (not built up, no orthophoto clump / grove): scrub bushes per ~35 m², a tree per ~180 m², in patches
//   verge     the D8 belt (VegetationMask D8NEAR, outside its 0–8 m clear strip): bushes, a pine or cypress every ~20 m
// Keep clear (brief "Where nothing may be planted"): trunk ≥ 2.5 m from model asphalt and graph streets (bushes ≥ 1.5 m),
// within 6 m of a road only cypresses and bushes (no low crown over the carriageway), 2 m from walls, not on car parks /
// pitches / pools (GroundCover.Hard), riva paving (RivaBuilder.NoTree), nothing below 0.8 m above sea; 2.5 m from any other
// plant (smaller for bushes). Sign test: plants within 1.5 m of the sight line from 10–100 m in front of (and behind) a sign
// / signal face to the face are dropped (RemoveSigns, counts in the log).
// Seeded by position (NaturePass.R01): the same ground always gets the same plants in every build (Marko records videos).
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class GustaVegetacija
    {
        /// game frame boxes (x0, z0, x1, z1): Kaštel Stari from the D8 hillside to the sea (brief: package x 1200…2100,
        /// z −1350…80 through paket-approaches' affine). Add a box to extend once Marko agrees.
        public static readonly (double x0, double z0, double x1, double z1)[] Boxes =
        {
            (-8180, -5832, -7275, -4404),
        };
        public static bool On = true;
        public static int[] Kinds = new int[16];
        public static int Yard, Empty, Verge, SignsRemoved;

        static bool InBox(double x, double z) { foreach (var b in Boxes) if (x >= b.x0 && x < b.x1 && z >= b.z0 && z < b.z1) return true; return false; }

        // ---- 2 m and 30 m building masks per 1 km tile (OrthoGround's 0.5 m footprint mask)
        static readonly Dictionary<(int, int), (bool[] wall2, bool[] wall1, bool[] yard)> masks = new Dictionary<(int, int), (bool[], bool[], bool[])>();
        static (bool[] wall2, bool[] wall1, bool[] yard) Mask(int tx, int tz)
        {
            if (masks.TryGetValue((tx, tz), out var m)) return m;
            try
            {
                var inside = OrthoGround.BuildingMask(tx, tz, 0);
                m = (OrthoGround.Grow(inside, 2.0), OrthoGround.Grow(inside, 1.0), Coarse(inside, 30));
            }
            catch (Exception e) { Debug.LogWarning("[gusto] footprints " + e.Message); m = (null, null, null); }
            masks[(tx, tz)] = m; return m;
        }
        /// "within r m of a footprint" on a 4 m grid (cheap: max over blocks then a square dilation)
        static bool[] Coarse(bool[] inside, double r)
        {
            int N = OrthoGround.MaskN, f = 8, C = N / f; var c = new bool[C * C];
            for (int j = 0; j < N; j++) for (int i = 0; i < N; i++) if (inside[j * N + i]) c[(j / f) * C + i / f] = true;
            int k = (int)Math.Ceiling(r / 4.0); var o = new bool[C * C];
            for (int j = 0; j < C; j++) for (int i = 0; i < C; i++)
                {
                    if (!c[j * C + i]) continue;
                    for (int b = Math.Max(0, j - k); b <= Math.Min(C - 1, j + k); b++) for (int a = Math.Max(0, i - k); a <= Math.Min(C - 1, i + k); a++) o[b * C + a] = true;
                }
            return o;
        }
        static bool At(bool[] m, int tx, int tz, double gx, double gz, int f)
        {
            if (m == null) return false;
            int N = OrthoGround.MaskN / f; int r = (int)Math.Floor(((tz + 1) * 1024 - gz) / (.5 * f)), i = (int)Math.Floor((gx - tx * 1024) / (.5 * f));
            return r >= 0 && i >= 0 && r < N && i < N && m[r * N + i];
        }

        // ---- spacing grid of what this pass placed (2 m cells)
        static readonly Dictionary<long, List<(double x, double z, float r)>> placed = new Dictionary<long, List<(double, double, float)>>();
        static long K(double x, double z) => ((long)Math.Floor(x / 4) << 32) ^ (uint)Math.Floor(z / 4);
        static bool Crowded(double x, double z, float r)
        {
            long cx = (long)Math.Floor(x / 4), cz = (long)Math.Floor(z / 4);
            for (long a = -2; a <= 2; a++) for (long b = -2; b <= 2; b++)
                    if (placed.TryGetValue(((cx + a) << 32) ^ (uint)(cz + b), out var l))
                        foreach (var p in l) { double d = Math.Sqrt((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z)); if (d < Math.Max(r, p.r)) return true; }
            return false;
        }
        static void Mark(double x, double z, float r) { long k = K(x, z); if (!placed.TryGetValue(k, out var l)) placed[k] = l = new List<(double, double, float)>(); l.Add((x, z, r)); }

        /// model asphalt within r m (8 directions at r and r / 2 + the point); sd = WorldData.SurfDist (≥ r: off every model)
        internal static bool NearAsphalt(List<ModelJ> models, double x, double z, double sd, double r)
        {
            if (sd >= r) return false;
            if (LaneProfile.OnAsphalt(models, x, z)) return true;
            // nor on (or within 1 m of) a model's sidewalk / island
            foreach (var m in models)
            {
                if (Math.Abs(x - m.x) > m.half + 25 || Math.Abs(z - m.z) > m.half + 25) continue;
                foreach (var nm in new[] { "sidewalk", "island" })
                    for (int k = 0; k < 5; k++) { double ox = k == 1 ? 1 : k == 2 ? -1 : 0, oz = k == 3 ? 1 : k == 4 ? -1 : 0; if (LaneProfile.Hit(m, nm, x + ox, z + oz)) return true; }
            }
            for (int k = 0; k < 8; k++)
            {
                double a = k * Math.PI / 4, c = Math.Cos(a), s = Math.Sin(a);
                if (LaneProfile.OnAsphalt(models, x + c * r, z + s * r) || LaneProfile.OnAsphalt(models, x + c * r * .5, z + s * r * .5)) return true;
            }
            return false;
        }

        struct Plant { public double x, y, z; public float yaw, s; public int proto; public float h, r; }

        /// place the trial's plants through put (NaturePass.Extra); existing = the records already in trees.bytes (x, y, z unity)
        public static int Place(VegetationMask.PutFn put, List<ModelJ> models, List<byte> existing)
        {
            Kinds = new int[16]; Yard = Empty = Verge = SignsRemoved = 0; placed.Clear(); masks.Clear();
            if (!On || !VegetationMask.On) return 0;
            int crn = NaturePass.Index("crnika"), ol = NaturePass.Index("oleander-roza"), ow = NaturePass.Index("oleander-bijeli"), gt = NaturePass.Index("grm-tamni");
            int np = NaturePass.PineCount, pin = NaturePass.Index("pinija");
            float hCrn = NaturePass.Height("crnika"); if (hCrn <= 0) hCrn = 2;
            float hOl = ol >= 0 ? NaturePass.Height("oleander-roza") : 2; if (hOl <= 0) hOl = 2;
            float hGt = gt >= 0 ? NaturePass.Height("grm-tamni") : 1.5f; if (hGt <= 0) hGt = 1.5f;
            float H(int p) { float h = NaturePass.HeightOf(p); return h > 0 ? h : 3; }
            float hOlive = H(2), hCyp = H(3), hSa = H(4), hSb = H(5), hBc = H(6);
            // the trees already there count for the spacing
            for (int o = 0; o + 24 <= existing.Count; o += 24)
            {
                float ux = BitConverter.ToSingle(existing.ToArray(o, 4), 0), uz = BitConverter.ToSingle(existing.ToArray(o + 8, 4), 0);
                var g = W.G(new Vector3(ux, 0, uz)); if (InBox(g.x, g.y)) Mark(g.x, g.y, 2.5f);
            }
            var outL = new List<Plant>();
            void Add(double x, double y, double z, float yaw, float s, int proto, float hgt, float rad) { outL.Add(new Plant { x = x, y = y, z = z, yaw = yaw, s = s, proto = proto, h = hgt, r = rad }); Mark(x, z, rad); }
            const double G = 3.0;
            foreach (var b in Boxes)
                for (double gz = b.z0; gz < b.z1; gz += G)
                    for (double gx = b.x0; gx < b.x1; gx += G)
                    {
                        double x = gx + (NaturePass.R01(gx, gz, 301) - .5) * G, z = gz + (NaturePass.R01(gx, gz, 302) - .5) * G;
                        int tx = (int)Math.Floor(x / 1024), tz = (int)Math.Floor(z / 1024);
                        double h = Tiles.Height(x, z); if (h < .8) continue;
                        int v = VegetationMask.At(x, z);
                        if ((v & VegetationMask.D8CLEAR) != 0) continue;
                        if ((v & VegetationMask.CLUMP) >= 4 || (v & VegetationMask.ORCHARD) != 0) continue; // the photo's clumps / groves are planted already
                        var mk = Mask(tx, tz);
                        bool yard = At(mk.yard, tx, tz, x, z, 8);   // (not the models' FootDist: that is the junction / section models' area, and made the whole D8 corridor a yard)
                        bool verge = (v & VegetationMask.D8NEAR) != 0;
                        // patches: slow noise so the plants clump and leave open ground (not an even sprinkle)
                        float patch = Mathf.PerlinNoise((float)(x * .035 + 311.7), (float)(z * .035 + 97.3));
                        float r = NaturePass.R01(x, z, 303), r2 = NaturePass.R01(x, z, 304), r3 = NaturePass.R01(x, z, 305);
                        // cell area 9 m²: probability = 9 / (m² per plant), scaled by the patch noise (0.4…1.6)
                        float pk = .4f + 1.2f * Mathf.Clamp01((patch - .25f) * 2);
                        float pTree, pBush;
                        if (yard) { pTree = 9f / 80 * pk; pBush = 9f / 45 * pk; }
                        else if (verge) { pTree = 9f / 300 * pk; pBush = 9f / 40 * pk; }
                        else { pTree = 9f / 260 * pk; pBush = 9f / 35 * pk; }   // open land stays open: trees in patches
                        bool tree = r < pTree, bush = !tree && r < pTree + pBush;
                        if (!tree && !bush) continue;
                        // keep clear
                        // model asphalt by the mesh (SurfDist is 0 on the models' whole ground strip, ~15 m beside the roads: just
                        // where plants matter most)
                        double sd = WorldData.SurfDist(models, x, z);
                        if (NearAsphalt(models, x, z, sd, tree ? 2.5 : 1.5) || StreetsBuilder.Covered(x, z, tree ? 2.5 : 1.5)) continue;
                        bool nearRoad = NearAsphalt(models, x, z, sd, 6) || StreetsBuilder.Covered(x, z, 6);
                        if (tree ? (At(mk.wall2, tx, tz, x, z, 1) || WorldData.FootDist(models, x, z) < 2) : (At(mk.wall1, tx, tz, x, z, 1) || WorldData.FootDist(models, x, z) < 1)) continue;
                        if (GroundCover.Hard(x, z, tree ? 3 : 1) || RivaBuilder.NoTree(x, z)) continue;
                        var q = WorldData.ModelTerrain(models, x, z, 2, h); if (q.HasValue) h = q.Value;
                        if (h < .8) continue;
                        float yaw = r3 * 360;
                        if (tree)
                        {
                            int proto; float s, tall, rad;
                            // big pines (Marko 23. 11.: "veliki borovi"): Aleppo 11–15 m, stone pines (umbrella) 10–14 m near the sea
                            // and now and then on the verge; both tall-trunked, so also within 6 m of a road (crown base ~6 m)
                            bool sea = h < 8;
                            if ((nearRoad && r2 < .5f) || (verge && r2 < .3f)) { proto = 3; tall = 7 + 6 * r3; s = tall / hCyp; rad = 2.5f; }   // cypress: no low crown over the road
                            else if (nearRoad || verge || (!yard && r2 < .5f))
                            {
                                bool umbrella = pin >= 0 && (sea ? r3 < .6f : r3 < .2f);
                                if (umbrella) { proto = pin; tall = 10 + 4 * r2; s = tall / H(pin); rad = 8; }
                                else if (np > 0) { proto = NaturePass.PineIndex(r3 < .5f ? .2f : .99f); tall = 11 + 4 * r2; s = tall / H(proto); rad = 6; }
                                else { proto = 0; tall = 12; s = 1; rad = 6; }
                            }
                            else
                            {
                                // yard: olive 35 %, crnika (fig / citrus / holm oak) 35 %, cypress 18 %, pine 12 %
                                if (r2 < .35f) { proto = 2; tall = 3.8f + 2.2f * r3; s = tall / hOlive; rad = 4; }
                                else if (r2 < .66f && crn >= 0) { proto = crn; tall = 3.5f + 3.5f * r3; s = tall / hCrn; rad = 3.5f; }
                                else if (r2 < .82f) { proto = 3; tall = 7 + 6 * r3; s = tall / hCyp; rad = 2.5f; }
                                else if (pin >= 0 && (h < 8 || r3 < .3f)) { proto = pin; tall = 10 + 4 * r3; s = tall / H(pin); rad = 8; }
                                else if (np > 0) { proto = NaturePass.PineIndex(r3 < .5f ? .2f : .99f); tall = 10 + 5 * r3; s = tall / H(proto); rad = 6; }
                                else { proto = 2; tall = 5; s = tall / hOlive; rad = 4; }
                            }
                            if (Crowded(x, z, rad)) continue;
                            Add(x, h - .1, z, yaw, s, proto, tall, rad);
                            if (yard) Yard++; else if (verge) Verge++; else Empty++;
                        }
                        else
                        {
                            int proto; float s, tall;
                            // yard street side: oleanders / dark hedge plants on about half of the yards (decided per 40 m patch)
                            bool hedgeYard = yard && Mathf.PerlinNoise((float)(x * .025 + 51.1), (float)(z * .025 + 7.7)) > .5f && (nearRoad || StreetsBuilder.Covered(x, z, 8));
                            if (hedgeYard && ol >= 0 && r2 < .55f) { proto = r2 < .35f ? ol : ow; tall = 1.8f + 1.2f * r3; s = tall / hOl; }
                            else if (gt >= 0 && r2 < .55f) { proto = gt; tall = 1.1f + 1.3f * r3; s = tall / hGt; }
                            // (two bush kinds, not four: every kind is a draw call per 128 m cell — the trial's first cut cost ~400
                            // draw calls and ~3 ms of main thread)
                            else { proto = 4; tall = 1.0f + 1.4f * r3; s = tall / hSa; }
                            if (Crowded(x, z, 1.4f)) continue;
                            Add(x, h - .06, z, yaw, s, proto, tall, 1.4f);
                        }
                    }
            int n = 0;
            var keep = RemoveSigns(outL);
            foreach (var p in keep) { put(p.x, p.y, p.z, p.yaw, p.s, p.proto); Kinds[Math.Min(15, p.proto < 8 ? p.proto : p.proto == crn ? 8 : p.proto == ol || p.proto == ow ? 9 : p.proto == gt ? 10 : p.proto == pin ? 12 : 11)]++; n++; }
            Debug.Log($"[gusto] Kaštel Stari: {n} plants (trees yard {Yard}, empty {Empty}, verge {Verge}; removed for signs {SignsRemoved}); " +
                      $"pine_a {Kinds[0]} pine_b {Kinds[1]} olive {Kinds[2]} cypress {Kinds[3]} shrub_a {Kinds[4]} shrub_b {Kinds[5]} bush_c {Kinds[6]} crnika {Kinds[8]} oleander {Kinds[9]} grm-tamni {Kinds[10]} pines(NM) {Kinds[11]} pinija {Kinds[12]}");
            placed.Clear(); masks.Clear();
            return n;
        }

        /// the sign test: signs / signals of the scene (SignsBuilder "Signs", signals) in the boxes; sight lines from 10–100 m
        /// along the face normal (both ways: the face's side is not known here) at 2.5 m above the ground to the face's middle
        static List<Plant> RemoveSigns(List<Plant> plants)
        {
            var faces = new List<(Vector3 c, Vector3 n)>();
            foreach (var rootName in new[] { "Signs", "Signals", "Signali", "Znakovi" })
            {
                var root = GameObject.Find("World/" + rootName) ?? GameObject.Find(rootName);
                if (!root) continue;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    var b = r.bounds; if (b.size.y > 8 || b.size.magnitude < .3f) continue;
                    var g = W.G(b.center); if (!InBox(g.x, g.y)) continue;
                    var f = r.transform.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
                    faces.Add((b.center, f));
                }
            }
            if (faces.Count == 0) { Debug.Log("[gusto] sign test: no sign / signal objects in the scene"); return plants; }
            var keep = new List<Plant>();
            foreach (var p in plants)
            {
                var u = W.U(p.x, p.y, p.z); bool hit = false;
                foreach (var (c, n) in faces)
                {
                    if ((new Vector2(u.x - c.x, u.z - c.z)).sqrMagnitude > 110 * 110) continue;
                    for (int sgn = -1; sgn <= 1 && !hit; sgn += 2)
                        for (int d = 10; d <= 100 && !hit; d += 10)
                        {
                            var eye = c + n * (sgn * d); eye.y = c.y + 1;   // ~2.5 m above the road
                            // distance from the plant's axis (at the sight line's height) to the segment eye → face, in plan
                            Vector2 a = new Vector2(eye.x, eye.z), bb = new Vector2(c.x, c.z), pp = new Vector2(u.x, u.z);
                            var ab = bb - a; float t = Mathf.Clamp01(Vector2.Dot(pp - a, ab) / ab.sqrMagnitude); if (t > .97f) continue;
                            float yLine = Mathf.Lerp(eye.y, c.y, t);
                            if (yLine < u.y || yLine > u.y + p.h + .5f) continue;
                            if ((a + ab * t - pp).magnitude < p.r * .5f + 1.5f) hit = true;
                        }
                    if (hit) break;
                }
                if (hit) SignsRemoved++; else keep.Add(p);
            }
            Debug.Log($"[gusto] sign test: {faces.Count} sign / signal faces, {SignsRemoved} plants removed");
            return keep;
        }

        static byte[] ToArray(this List<byte> l, int o, int n) { var a = new byte[n]; l.CopyTo(o, a, 0, n); return a; }
    }
}
