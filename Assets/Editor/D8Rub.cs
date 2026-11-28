// D8 rub (28. 11., promet2; Marko with ETS2 screenshots: "rubovi ceste gušći i raznolikiji"): the D8's outer verges through
// Kaštela get an edge of their own, walked along the two D8 traffic corridors (TrafficData: the carriageway's outer lane edge,
// right of travel = the outer verge; the median is left of travel and never planted here):
//   grass   1.2–5 m past the carriageway edge: tall dry grass clumps (two tints, straw and grey-green) and grey wormwood, in long
//           runs with gaps (noise along the road), 0.7–1.6 m tall: the kerb / ditch edge of a Dalmatian road in summer
//   shrubs  3.5–13 m: maquis runs (lentisk-like dark shrub, the dark evergreen grm-tamni, our shrub, low crnika), 0.8–2.6 m
//   trees   9–32 m: groups of Aleppo pine, holm oak (crnika), olive, now and then a cypress pair or a stone pine, 3.5–13 m,
//           heights varied ±30 %; fewer in built-up land (VegetationMask BUILT), none in yards / on buildings
// Kept clear: asphalt / footways / graph streets (trees 4 m, shrubs 2 m, grass 0.8 m), car parks / pitches, the riva, the sea,
// within 2 m of a building footprint, anything with a collider (lamp / sign / signal posts, guardrail posts, buildings, bridge
// piers: an overlap capsule, the ground colliders aside), within 55 m of a junction model's centre only grass (the sight lines),
// slip roads by the asphalt test, and the signs' sight lines (GustaVegetacija.RemoveSigns). VegetationClean then drops every
// plant onto the ground collider and re-checks the asphalt like all others.
// The plants: existing TreeRenderer prototypes plus three of the Coast Environment package (rub-trava-a / -b: beach grass 01
// tinted dry, rub-pelin: sea wormwood, rub-grm: sea buckthorn tinted to a dark lentisk green), registered by Protos in
// NaturePass.Begin. Seeded by position: every build plants the same verge. Off: D8Rub.On = false.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class D8Rub
    {
        public static bool On = true;
        /// density factors (1 = the values below) for A/B
        public static float GrassK = 1, ShrubK = 1, TreeK = 1;
        public static int Grass, Shrubs, Trees, SkipAsphalt, SkipObstacle, SkipJunction, SkipBuilt, SkipSign;

        const string OUT = WorldBuilder.GEN + "/Nature";
        const string CE = NaturePass.PKG + "/Coast Environment/";
        static T Load<T>(string p) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(p);

        // ------------------------------------------------------------------ prototypes
        public static void Protos(List<NaturePass.Proto> list)
        {
            if (!On) return;
            if (!AssetDatabase.IsValidFolder(OUT)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Nature");
            void Grass(string prefab, string id, Color tint, float draw)
            {
                var g = Load<GameObject>(CE + "Foliage and Grass/Prefabs/" + prefab + ".prefab"); if (!g) { Debug.LogWarning("[d8rub] missing " + prefab); return; }
                var r = g.GetComponentInChildren<MeshRenderer>(); if (!r) return;
                list.Add(new NaturePass.Proto { id = id, mesh = Flat(r, g.transform, id.Replace('-', '_')), mats = r.sharedMaterials.Select(m => Tint(m, id, tint, draw)).ToArray(), draw = draw, near = 1e5f });
            }
            Grass("Prefab_beach_grass_01_2", "rub-trava-a", new Color(1.22f, 1.02f, .58f, 0), 95);   // straw
            Grass("Prefab_beach_grass_01_3", "rub-trava-b", new Color(1.0f, .97f, .66f, 0), 95);     // half dry, grey-green
            Grass("Prefab_sea_wormwood_02", "rub-pelin", new Color(.9f, .95f, .86f, 0), 70);         // grey Artemisia
            var bush = Load<GameObject>(CE + "Bushes/Prefabs/Prefab_sea_buckthorn_02.prefab");
            if (bush)
            {
                var lg = bush.GetComponentInChildren<LODGroup>(); var lods = lg ? lg.GetLODs() : null;
                MeshRenderer R(int i) => lods == null ? bush.GetComponentInChildren<MeshRenderer>() : lods[Mathf.Min(i, lods.Length - 1)].renderers.OfType<MeshRenderer>().FirstOrDefault();
                var near = R(1) ?? R(0); var far = lods != null && lods.Length > 2 ? R(lods.Length - 1) : null;
                var tint = new Color(.62f, .72f, .52f, 0);   // lentisk / myrtle: dark olive green
                if (near)
                    list.Add(new NaturePass.Proto
                    {
                        id = "rub-grm", mesh = Flat(near, bush.transform, "rub_grm"), mats = near.sharedMaterials.Select(m => Tint(m, "rubgrm", tint, 170)).ToArray(),
                        imp = far && far != near ? Flat(far, bush.transform, "rub_grm_far") : null, impMat = far && far != near ? Tint(far.sharedMaterial, "rubgrm", tint, 170) : null, draw = 170, near = 28,
                    });
            }
            else Debug.LogWarning("[d8rub] missing Prefab_sea_buckthorn_02");
        }
        static Mesh Flat(MeshRenderer r, Transform root, string name)
        {
            var src = r.GetComponent<MeshFilter>().sharedMesh; var M = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
            if (M == Matrix4x4.identity) return src;
            var m = UnityEngine.Object.Instantiate(src); m.name = name; var v = src.vertices; for (int i = 0; i < v.Length; i++) v[i] = M.MultiplyPoint3x4(v[i]);
            m.vertices = v; m.RecalculateBounds();
            string p = $"{OUT}/{name}.asset"; if (File.Exists(p)) AssetDatabase.DeleteAsset(p); AssetDatabase.CreateAsset(m, p); return m;
        }
        static Material Tint(Material m, string tag, Color c, float cull = 100)
        {
            if (!m) return m;
            string p = $"{OUT}/{m.name}_{tag}.mat"; var t = Load<Material>(p);
            if (!t) { t = new Material(m); AssetDatabase.CreateAsset(t, p); } else { t.shader = m.shader; t.CopyPropertiesFromMaterial(m); }
            foreach (var prop in new[] { "_BaseColor", "_Color" }) if (t.HasProperty(prop)) t.SetColor(prop, c);
            // the package's foliage shader (NatureManufacture Foliage 2) has no base colour: its healthy / dry colours tint it,
            // and it dithers itself out at _CullFarStart + _CullFarDistance (80 + 20 m on the package's materials)
            if (t.HasProperty("_HealthyColor")) { t.SetColor("_HealthyColor", c); t.SetColor("_DryColor", new Color(c.r * .95f, c.g * .9f, c.b * .85f, 0)); }
            if (t.HasProperty("_CullFarStart")) { t.SetFloat("_CullFarStart", cull - 25); t.SetFloat("_CullFarDistance", 25); }
            t.enableInstancing = true; EditorUtility.SetDirty(t); return t;
        }

        // ------------------------------------------------------------------ placement
        static readonly Dictionary<long, List<(double x, double z, float r)>> placed = new Dictionary<long, List<(double, double, float)>>();
        static long K(long cx, long cz) => (cx << 32) ^ (uint)cz;
        static bool Crowded(double x, double z, float r)
        {
            long cx = (long)Math.Floor(x / 4), cz = (long)Math.Floor(z / 4);
            for (long a = -2; a <= 2; a++) for (long b = -2; b <= 2; b++)
                    if (placed.TryGetValue(K(cx + a, cz + b), out var l))
                        foreach (var p in l) { double dx = p.x - x, dz = p.z - z; float m = Mathf.Min(r, p.r); if (dx * dx + dz * dz < m * m) return true; }
            return false;
        }
        static void Mark(double x, double z, float r) { long k = K((long)Math.Floor(x / 4), (long)Math.Floor(z / 4)); if (!placed.TryGetValue(k, out var l)) placed[k] = l = new List<(double, double, float)>(); l.Add((x, z, r)); }

        static readonly Dictionary<(int, int), bool[]> walls = new Dictionary<(int, int), bool[]>();
        static bool NearBuilding(double x, double z)
        {
            int tx = (int)Math.Floor(x / 1024), tz = (int)Math.Floor(z / 1024);
            if (!walls.TryGetValue((tx, tz), out var m))
            {
                try { m = OrthoGround.Grow(OrthoGround.BuildingMask(tx, tz, 0), 2.0); } catch (Exception e) { Debug.LogWarning("[d8rub] footprints " + e.Message); m = null; }
                walls[(tx, tz)] = m;
            }
            if (m == null) return false;
            int N = OrthoGround.MaskN; int r = (int)Math.Floor(((tz + 1) * 1024 - z) / .5), i = (int)Math.Floor((x - tx * 1024) / .5);
            return r >= 0 && i >= 0 && r < N && i < N && m[r * N + i];
        }

        static readonly Collider[] hits = new Collider[16];
        /// a collider that is not ground (terrain, the road / ground models' big mesh colliders) in the plant's space
        static bool Obstacle(Vector3 u, float r, float h)
        {
            int n = Physics.OverlapCapsuleNonAlloc(u + Vector3.up * (r + .5f), u + Vector3.up * Mathf.Max(r + .6f, h), r, hits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = hits[i]; if (c is TerrainCollider) continue;
                var b = c.bounds.size; if (b.x > 60 || b.z > 60) continue;   // ground / road model tiles, long embankments
                return true;
            }
            return false;
        }

        /// place the verge plants through put (NaturePass.Extra), after the other placers (existing = trees.bytes records so far)
        public static int Place(VegetationMask.PutFn put, List<ModelJ> models, List<byte> existing)
        {
            Grass = Shrubs = Trees = SkipAsphalt = SkipObstacle = SkipJunction = SkipBuilt = SkipSign = 0;
            placed.Clear(); walls.Clear();
            if (!On) return 0;
            var td = AssetDatabase.FindAssets("t:TrafficData").Select(g => Load<Magistrala.Traffic.TrafficData>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(d => d && d.corridors != null);
            if (!td) { Debug.LogWarning("[d8rub] no TrafficData (Build traffic first)"); return 0; }
            var cors = td.corridors.Where(c => c.name.StartsWith("D8")).ToList();
            int ta = NaturePass.Index("rub-trava-a"), tb = NaturePass.Index("rub-trava-b"), tp = NaturePass.Index("rub-pelin"), tg = NaturePass.Index("rub-grm");
            int gt = NaturePass.Index("grm-tamni"), crn = NaturePass.Index("crnika"), pin = NaturePass.Index("pinija"), np = NaturePass.PineCount;
            float H(int p) { float h = p >= 0 ? NaturePass.HeightOf(p) : 0; return h > .05f ? h : 1; }
            // the plants already there count for the spacing (within 60 m of the D8)
            var d8box = cors.SelectMany(c => c.axis).Select(a => W.G(a)).ToList();
            double bx0 = d8box.Min(g => g.x) - 60, bx1 = d8box.Max(g => g.x) + 60, bz0 = d8box.Min(g => g.y) - 400, bz1 = d8box.Max(g => g.y) + 400;
            var arr = existing.ToArray();
            for (int o = 0; o + 24 <= arr.Length; o += 24)
            {
                var g = W.G(new Vector3(BitConverter.ToSingle(arr, o), 0, BitConverter.ToSingle(arr, o + 8)));
                if (g.x < bx0 || g.x > bx1 || g.y < bz0 || g.y > bz1) continue;
                float s = BitConverter.ToSingle(arr, o + 16); int p = (int)BitConverter.ToSingle(arr, o + 20);
                Mark(g.x, g.y, H(p) * s >= 3 ? 2.5f : 1.0f);
            }
            var junctions = models.Where(m => m.roads.Count >= 3).ToList();
            Physics.SyncTransforms();
            var outL = new List<GustaVegetacija.Plant>();
            foreach (var cor in cors)
            {
                int n = cor.axis.Length; float L = cor.Length;
                for (float s = 20; s < L - 20; s += 1f)
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt(s / cor.step), 0, n - 1);
                    // the carriageway's outer edge: the outermost lane centre + half a lane (+ = right of travel = the outer verge)
                    float edge = float.NegativeInfinity;
                    for (int k = 0; k < cor.maxLanes; k++) { float o = cor.laneOff[i * cor.maxLanes + k]; if (!float.IsNaN(o)) edge = Mathf.Max(edge, o); }
                    if (float.IsNegativeInfinity(edge)) continue;
                    edge += 1.85f;
                    var a = cor.axis[i]; var rt = cor.right[i];
                    var g0 = W.G(a);
                    // noise along the road (by world position, so both corridors / every build agree)
                    float run = Mathf.PerlinNoise(g0.x * .022f + 17.3f, g0.y * .022f + 3.1f), grp = Mathf.PerlinNoise(g0.x * .0085f + 71.9f, g0.y * .0085f + 13.7f) * .7f + Mathf.PerlinNoise(g0.x * .03f + 5.5f, g0.y * .03f + 41.2f) * .3f;
                    // grass: every metre up to five clumps, most within 3 m of the edge, a few out to 9 m
                    for (int q = 0; q < 5; q++)
                    {
                        double sx = g0.x, sz = g0.y; float r0 = NaturePass.R01(sx, sz, 711 + q * 10);
                        if (r0 > (.4f + .6f * Mathf.Clamp01((run - .2f) * 2.5f)) * (q < 3 ? .95f : .6f) * GrassK) continue;
                        float e = q < 3 ? 1.1f + 4.2f * NaturePass.R01(sx, sz, 713 + q * 10) * NaturePass.R01(sx, sz, 715 + q * 10) : 3f + 6f * NaturePass.R01(sx, sz, 713 + q * 10);   // dense at the edge
                        float along = (NaturePass.R01(sx, sz, 717 + q * 10) - .5f) * .9f;
                        Try(cor, i, a, rt, edge + e, along, 0, NaturePass.R01(sx, sz, 719 + q * 10));
                    }
                    // shrubs: every metre in runs
                    {
                        double sx = g0.x, sz = g0.y; float r0 = NaturePass.R01(sx, sz, 821);
                        float pS = Mathf.Clamp01((run - .32f) * 2.4f) * .8f + Mathf.Clamp01((grp - .45f) * 3f) * .4f;
                        if (r0 < pS * ShrubK)
                            Try(cor, i, a, rt, edge + 3.5f + 9.5f * NaturePass.R01(sx, sz, 823) * (.4f + .6f * NaturePass.R01(sx, sz, 824)), (NaturePass.R01(sx, sz, 825) - .5f) * 1.6f, 1, NaturePass.R01(sx, sz, 827));
                    }
                    // trees: every 3 m in groups
                    if (((int)s % 3) == 0)
                    {
                        double sx = g0.x, sz = g0.y; float r0 = NaturePass.R01(sx, sz, 831);
                        float pT = Mathf.Clamp01((grp - .4f) * 2.6f) * .85f + .07f;
                        for (int q = 0; q < 2; q++)
                            if (NaturePass.R01(sx, sz, 832 + q) < pT * TreeK * (q == 0 ? 1 : .55f))
                                Try(cor, i, a, rt, edge + 9 + (q == 0 ? 11 : 23) * NaturePass.R01(sx, sz, 834 + q), (NaturePass.R01(sx, sz, 836 + q) - .5f) * 4, 2, NaturePass.R01(sx, sz, 838 + q));
                    }
                }
            }
            void Try(Magistrala.Traffic.TrafficCorridor cor, int i, Vector3 a, Vector3 rt, float off, float along, int cls, float rk)
            {
                var t = cor.axis[Mathf.Min(i + 1, cor.axis.Length - 1)] - cor.axis[Mathf.Max(i - 1, 0)]; t.y = 0; t.Normalize();
                var u = a + rt * off + t * along; var g = W.G(u); double x = g.x, z = g.y;
                double h = Tiles.Height(x, z); if (h < .8) return;
                int vm = VegetationMask.At(x, z);
                bool built = (vm & VegetationMask.BUILT) != 0;
                if (cls == 2 && built && NaturePass.R01(x, z, 841) > .4f) { SkipBuilt++; return; }
                if (cls == 1 && built && NaturePass.R01(x, z, 842) > .7f) { SkipBuilt++; return; }
                double sd = WorldData.SurfDist(models, x, z), clr = cls == 2 ? 4 : cls == 1 ? 2 : .8;
                if (GustaVegetacija.NearAsphalt(models, x, z, sd, clr) || StreetsBuilder.Covered(x, z, clr + (cls == 2 ? 2 : 0))) { SkipAsphalt++; return; }
                if (GroundCover.Hard(x, z, cls == 2 ? 2 : .5) || RivaBuilder.NoTree(x, z)) { SkipAsphalt++; return; }
                if (cls > 0 && NearBuilding(x, z)) { SkipBuilt++; return; }
                if (cls > 0 && junctions.Any(m => (m.x - x) * (m.x - x) + (m.z - z) * (m.z - z) < 55 * 55)) { SkipJunction++; return; }
                var q = WorldData.ModelTerrain(models, x, z, 2, h); if (q.HasValue) h = q.Value;
                if (h < .8) return;
                int proto; float tall, rad;
                if (cls == 0)
                {
                    proto = rk < .42f ? ta : rk < .8f ? tb : tp; if (proto < 0) return;
                    tall = (proto == tp ? .6f : .8f) + (proto == tp ? .5f : 1.0f) * NaturePass.R01(x, z, 851); rad = .45f;
                }
                else if (cls == 1)
                {
                    if (rk < .34f && tg >= 0) { proto = tg; tall = .9f + 1.4f * NaturePass.R01(x, z, 852); }
                    else if (rk < .62f && gt >= 0) { proto = gt; tall = .8f + 1.2f * NaturePass.R01(x, z, 852); }
                    else if (rk < .82f) { proto = 4; tall = .9f + 1.1f * NaturePass.R01(x, z, 852); }
                    else if (crn >= 0) { proto = crn; tall = 1.8f + 1.4f * NaturePass.R01(x, z, 852); }
                    else return;
                    rad = 1.3f;
                }
                else
                {
                    float r2 = NaturePass.R01(x, z, 853);
                    if (rk < .32f && np > 0) { proto = NaturePass.PineIndex(r2 < .5f ? .2f : .99f); tall = 8 + 5 * r2; rad = 4.5f; }
                    else if (rk < .64f && crn >= 0) { proto = crn; tall = 3.8f + 3.2f * r2; rad = 3.2f; }
                    else if (rk < .84f) { proto = 2; tall = 3.4f + 2.2f * r2; rad = 3.4f; }
                    else if (rk < .93f) { proto = 3; tall = 7 + 5 * r2; rad = 2.2f; }
                    else if (pin >= 0) { proto = pin; tall = 9 + 4 * r2; rad = 6; }
                    else { proto = 2; tall = 4.5f; rad = 3.4f; }
                }
                if (Crowded(x, z, rad)) return;
                if (cls > 0 && Obstacle(W.U(x, h, z), cls == 2 ? 1.2f : .8f, Mathf.Min(tall, 4))) { SkipObstacle++; return; }
                outL.Add(new GustaVegetacija.Plant { x = x, y = h - .06, z = z, yaw = NaturePass.R01(x, z, 857) * 360, s = tall / H(proto), proto = proto, h = tall, r = rad });
                Mark(x, z, rad);
            }
            int before = outL.Count;
            var keep = GustaVegetacija.RemoveSigns(outL); SkipSign = before - keep.Count;
            foreach (var p in keep)
            {
                put(p.x, p.y, p.z, p.yaw, p.s, p.proto);
                if (p.proto == ta || p.proto == tb || p.proto == tp) Grass++; else if (p.h < 3.4f) Shrubs++; else Trees++;
            }
            Debug.Log($"[d8rub] D8 verges: grass {Grass}, shrubs {Shrubs}, trees {Trees}; skipped asphalt {SkipAsphalt}, obstacle {SkipObstacle}, junction {SkipJunction}, built {SkipBuilt}, signs {SkipSign}");
            placed.Clear(); walls.Clear();
            return keep.Count;
        }
    }
}
