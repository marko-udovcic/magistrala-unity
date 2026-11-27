// Priroda: NatureManufacture "Coast & Dunes Environment – Dynamic Nature" (Unity Asset Store, Standard EULA; the package folder
// Assets/NatureManufacture Assets is NOT in git, docs/priroda.md) on the ground and plants of all of Kaštela (NaturePass.Boxes), as hooks in the existing
// builders, so a rebuild reproduces it:
//   Build terrain          tiles touching a box get 4 package-based layers in our order (dry scrub ground, brown soil, our limestone
//                          rock, pale stony gravel) — the splat logic and the orthophoto macro tint (OrthoGround) stay as they are
//   Build trees            pines in a box → the package's beach pines reshaped toward Aleppo pine (mesh flattened: shorter, wider and
//                          flatter crown; bark greyer); extra pines on the Kozjak slope above Lukšić; the D8 median hedge (MedianHedge)
//   Grafika: detalji       dry grass, stonecrop, wormwood and weeds (package terrain-detail prefabs, their wind) in the boxes, weeds
//                          along wall bases (OrthoGround's footprint mask), short draw distance
//   Priroda: postavi       NM wind zone, limestone cliff faces (package tiled cliffs, tinted grey-white) on steep slopes and road cuts
// Not used in Kaštela: beach rose / sea buckthorn as themselves, dunes / dune sand, rope fences.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class NaturePass
    {
        public const string PKG = "Assets/NatureManufacture Assets";
        const string CE = PKG + "/Coast Environment/";
        const string OUT = WorldBuilder.GEN + "/Nature";
        /// the package is present (it is not in git): without it every hook leaves our build unchanged
        public static bool On => AssetDatabase.IsValidFolder(PKG);

        /// boxes, game frame (x0, z0, x1, z1) where the pass applies: all of Kaštela = the playable core tiles (WorldBuilder TX0…TX1,
        /// TZ0…TZ1: Štafilić / D409 west of game x −9600 to Sućurac, the coast up the Kozjak slope); add a box to extend (Split)
        public static readonly (double x0, double z0, double x1, double z1)[] Boxes =
        {
            (WorldBuilder.TX0 * 1024, WorldBuilder.TZ0 * 1024, (WorldBuilder.TX1 + 1) * 1024, (WorldBuilder.TZ1 + 1) * 1024),
        };
        public static bool In(double gx, double gz, double pad = 0)
        {
            foreach (var b in Boxes) if (gx >= b.x0 - pad && gx <= b.x1 + pad && gz >= b.z0 - pad && gz <= b.z1 + pad) return true;
            return false;
        }
        public static bool TileIn(int tx, int tz)
        {
            foreach (var b in Boxes) if (b.x1 > tx * 1024 && b.x0 < tx * 1024 + 1024 && b.z1 > tz * 1024 && b.z0 < tz * 1024 + 1024) return true;
            return false;
        }

        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = Path.GetDirectoryName(p).Replace('\\', '/'); Dir(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(p)); } }
        static T Load<T>(string p) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(p);
        static void Save(UnityEngine.Object o, string path) { if (File.Exists(path)) AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(o, path); }
        static float Perlin(double x, double z, double f) => Mathf.PerlinNoise((float)(x * f + 1000.5), (float)(z * f + 2000.5));
        static uint Hash(double x, double z, int salt) { unchecked { uint h = (uint)((long)Math.Round(x * 10) * 73856093L) ^ (uint)((long)Math.Round(z * 10) * 19349663L) ^ (uint)(salt * 83492791); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return h; } }
        public static float R01(double x, double z, int salt) => (Hash(x, z, salt) & 0xffffff) / 16777216f;

        // ================================================================== terrain layers
        /// the 4 layers (same roles as WorldBuilder's: 0 dry scrub ground, 1 soil, 2 rock, 3 gravel) for a tile in a box
        public static TerrainLayer[] Layers(TerrainLayer[] ours)
        {
            if (!On) return ours;
            Dir(OUT);
            const string G = CE + "Ground/Textures/";
            TerrainLayer L(string name, string bc, string n, float tile, Color remap, int fallback)
            {
                // the package's _BC carries a height / gloss alpha: as a terrain layer without a mask map it made the ground glossy
                // (wet, sky-reflecting sheen); drop the alpha like our Poly Haven jpgs (alpha 1 = matte here)
                if (AssetImporter.GetAtPath(G + bc) is TextureImporter ti && ti.alphaSource != TextureImporterAlphaSource.None) { ti.alphaSource = TextureImporterAlphaSource.None; ti.SaveAndReimport(); }
                var d = Load<Texture2D>(G + bc); var nm = Load<Texture2D>(G + n);
                if (!d) return ours[fallback];
                var l = new TerrainLayer { diffuseTexture = d, normalMapTexture = nm, tileSize = new Vector2(tile, tile), normalScale = 1, smoothness = 0, diffuseRemapMax = remap, diffuseRemapMin = Vector4.zero };
                Save(l, $"{OUT}/{name}.terrainlayer"); return l;
            }
            return new[]
            {
                // patchy olive-green low scrub on pale ground: dry Dalmatian maquis ground (yellowed, a little less green)
                L("NM_DryScrub", "T_ground_sand_07_BC.png", "T_ground_sand_07_N.png", 7, new Color(.62f, .64f, .42f, 1), 0),
                // brown clay soil pushed toward terra rossa
                L("NM_RedSoil", "T_cliff_clay_02_BC.png", "T_cliff_clay_02_N.png", 6, new Color(1.05f, .74f, .56f, 1), 1),
                ours[2],                                                     // our grey-white limestone (Poly Haven rock face)
                // pale ground with pebbles and limestone grit: white macadam tracks, gravel drives
                L("NM_StonyGravel", "T_ground_sand_09_BC.png", "T_ground_sand_09_N.png", 4, new Color(.78f, .77f, .72f, 1), 3),
            };
        }

        // ================================================================== terrain details
        static readonly (string prefab, float min, float max)[] DetailSrc =
        {
            ("Prefab_beach_grass_02_1_dry_Unity_Terrain", .55f, 1.0f),   // dry yellowed grass tufts
            ("Prefab_beach_grass_02_3_dry_Unity_Terrain", .55f, 1.0f),
            ("Prefab_stonecrop_01_Unity_Terrain", .7f, 1.1f),               // Sedum on stony ground
            ("Prefab_sea_wormwood_01_Unity_Terrain", .6f, 1.0f),            // grey Artemisia (Mediterranean garrigue)
            ("Prefab_hedge_bedstraw_01_Unity_Terrain", .6f, 1.0f),          // weeds along wall bases / plots
            ("Prefab_eastern_groundsel_01_Unity_Terrain", .6f, 1.0f),
            // 22. 11. (Marko: "pustinja", ravno tlo uz kamion) driven by the orthophoto (OrthoGround.PhotoCells):
            ("Prefab_beach_grass_02_2_Unity_Terrain", .45f, .85f),         // green grass where the photo is green (gardens, lawns, field edges)
            ("Prefab_beach_grass_02_4_Unity_Terrain", .45f, .8f),
            ("Prefab_kidney_vetch_01_Unity_Terrain", .6f, 1.0f),            // low flowering herbs in the green
            ("Prefab_litter_01_A_Unity_Terrain", .8f, 1.2f),                // leaves / twigs on bare yards and earth
        };
        static GameObject DetailPrefab(string n)
        {
            foreach (var dir in new[] { CE + "Foliage and Grass/Unity Terrain Prefabs/", CE + "Foliage and Grass/Prefabs/" })
            { var g = Load<GameObject>(dir + n + ".prefab"); if (g) return g; }
            var guids = AssetDatabase.FindAssets(n + " t:Prefab", new[] { CE });
            foreach (var gu in guids) { var p = AssetDatabase.GUIDToAssetPath(gu); if (Path.GetFileNameWithoutExtension(p) == n) return Load<GameObject>(p); }
            return null;
        }

        /// a copy of a detail prefab (Generated/Nature/NoShadow) whose renderers cast no shadow: instanced terrain details follow the
        /// prefab's shadow mode, and thousands of tufts in 4 shadow cascades cost more than the tufts themselves (22. 11.)
        static readonly Dictionary<string, GameObject> noShadow = new Dictionary<string, GameObject>();
        public static GameObject NoShadow(GameObject g)
        {
            if (!g) return null;
            if (noShadow.TryGetValue(g.name, out var c) && c) return c;
            Dir(OUT + "/NoShadow");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(g); PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            var pf = PrefabUtility.SaveAsPrefabAsset(go, $"{OUT}/NoShadow/{g.name}.prefab"); UnityEngine.Object.DestroyImmediate(go);
            noShadow[g.name] = pf; return pf;
        }

        /// GraphicsPass.Details hook for one core tile: adds the package detail layers (inside the boxes) and thins our own grass
        /// there. R = detail resolution (4 m cells), A = the tile's alphamap, wall = cells within 1.5 m of a building wall
        public static void Details(int tx, int tz, int R, List<DetailPrototype> protos, List<int[,]> layers, float[,,] A, int ar, List<ModelJ> models)
        {
            if (!On || !TileIn(tx, tz)) return;
            var src = new List<(GameObject g, float min, float max)>();
            foreach (var d in DetailSrc) { var g = NoShadow(DetailPrefab(d.prefab)); if (g) src.Add((g, d.min, d.max)); else Debug.LogWarning("[priroda] detail prefab missing " + d.prefab); }
            if (src.Count == 0) return;
            int k0 = protos.Count;
            foreach (var s in src)
                protos.Add(new DetailPrototype
                {
                    usePrototypeMesh = true, prototype = s.g, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                    minWidth = s.min, maxWidth = s.max, minHeight = s.min, maxHeight = s.max, healthyColor = s.g.name.Contains("beach_grass_02_2") || s.g.name.Contains("beach_grass_02_4") ? new Color(.82f, 1f, .7f) : Color.white, dryColor = new Color(.95f, .9f, .78f), noiseSpread = .4f,
                });
            var L = new int[src.Count][,]; for (int k = 0; k < src.Count; k++) L[k] = new int[R, R];
            var wall = WallMask(tx, tz, R);
            var (pg, pb) = OrthoGround.PhotoCells(tx, tz, R);
            for (int r = 0; r < R; r++)
                for (int c = 0; c < R; c++)
                {
                    double gx = tx * 1024 + (c + .5) * 4, gz = (tz + 1) * 1024 - (r + .5) * 4;
                    if (!In(gx, gz)) continue;
                    double h = Tiles.Height(gx, gz); if (h < .8) continue;
                    bool w = wall != null && wall[r, c];
                    double sd = WorldData.SurfDist(models, gx, gz);
                    if (sd < 2 || StreetsBuilder.Covered(gx, gz, .8) || RivaBuilder.Class(gx, gz) != 0) continue;
                    // the verge: 2–5 m from a road model / graph street (weeds, as at the wall bases)
                    bool edge = sd < 5 || StreetsBuilder.Covered(gx, gz, 4);
                    float g = pg != null ? pg[r, c] : 0, bare = pb != null ? pb[r, c] : 0;
                    if (!w && WorldData.FootDist(models, gx, gz) < 1.5) continue;
                    int ai = Mathf.Min(ar - 1, r * ar / R), aj = Mathf.Min(ar - 1, c * ar / R);
                    float grassW = A[ai, aj, 0], soilW = A[ai, aj, 1], rockW = A[ai, aj, 2];
                    float n = Perlin(gx, gz, .06), n2 = Perlin(gx + 50, gz - 70, .17);
                    // our green grass thinned to a third in the boxes (the package's dry tufts take over)
                    // under the clumps' closed crowns and in the olive groves (VegetationMask): bare soil and litter, a few dry tufts
                    int vm = VegetationMask.At(gx, gz); bool under = (vm & VegetationMask.CLUMP) >= 6 || (vm & VegetationMask.ORCHARD) != 0;
                    if (layers.Count > 0) layers[0][r, c] = under ? 0 : Mathf.RoundToInt(layers[0][r, c] * .35f);
                    float dry = (grassW * (1.1f + 2.4f * n) + soilW * .5f) * (under ? .35f : 1) * (1 - .65f * g) * (1 - .5f * bare); // tufts per 4 m cell
                    int Cnt(float mean, int salt) { float f = mean * (.7f + .6f * R01(gx, gz, salt)); int i = (int)f; return i + (R01(gx, gz, salt + 7) < f - i ? 1 : 0); }
                    L[0][r, c] = Cnt(dry * .55f, 1);
                    if (src.Count > 1) L[1][r, c] = Cnt(dry * .45f, 2);
                    if (src.Count > 2) L[2][r, c] = Cnt((rockW * .9f + soilW * .2f) * (n2 + .2f), 3);
                    if (src.Count > 3) L[3][r, c] = Cnt((grassW * .35f + rockW * .3f) * n2, 4);
                    if (src.Count > 4) L[4][r, c] = Cnt(w ? 1.6f : edge ? .9f * (.4f + n) : grassW * .12f, 5);
                    if (src.Count > 5) L[5][r, c] = Cnt(w ? 1.0f : edge ? .6f * n : grassW * .08f * n, 6);
                    // the orthophoto's green (gardens, lawns, field edges, the green under olives) and bare (yards, tracks)
                    float gg = under ? g * .3f : g;
                    if (src.Count > 6) L[6][r, c] = Cnt(gg * (2.2f + 3.2f * n), 8);
                    if (src.Count > 7) L[7][r, c] = Cnt(gg * (1.0f + 1.8f * n2), 9);
                    if (src.Count > 8) L[8][r, c] = Cnt(gg * .35f * n2, 10);
                    if (src.Count > 9) L[9][r, c] = Cnt(bare * .5f * (.3f + n2) + (under ? .4f : 0), 11);
                    if (layers.Count > 1 && bare > .25f) layers[1][r, c] += Cnt(bare * .9f, 12);   // our pebbles on bare yards / tracks
                }
            for (int k = 0; k < src.Count; k++) layers.Add(L[k]);
        }

        /// cells (4 m) of tile tx, tz within ~1.5 m outside a building footprint (row 0 = north edge, as the detail layers' rows here)
        static bool[,] WallMask(int tx, int tz, int R)
        {
            try
            {
                var inside = OrthoGround.BuildingMask(tx, tz, 0); var grown = OrthoGround.Grow(inside, 1.5);
                int N = OrthoGround.MaskN; var m = new bool[R, R]; int f = N / R;
                for (int r = 0; r < R; r++)
                    for (int c = 0; c < R; c++)
                    {
                        // mask rows: row 0 = south; detail rows here: r counts from the north edge
                        int mr0 = (R - 1 - r) * f; bool any = false;
                        for (int a = 0; a < f && !any; a += 2) for (int b = 0; b < f && !any; b += 2) { int o = (mr0 + a) * N + c * f + b; if (grown[o] && !inside[o]) any = true; }
                        m[r, c] = any;
                    }
                return m;
            }
            catch (Exception e) { Debug.LogWarning("[priroda] wall mask: " + e.Message); return null; }
        }

        // ================================================================== trees (TreeRenderer prototypes appended after ours)
        public class Proto { public string id; public Mesh mesh; public Material[] mats; public Mesh imp; public Material impMat; public float draw, near; }
        static List<Proto> extra;
        static int baseCount;

        static readonly string[] PineSrc = { "Prefab_Pine_beach_01_A", "Prefab_Pine_beach_02_A", "Prefab_Pine_beach_medium_01", "Prefab_Pine_beach_03_A" };

        /// prototypes appended to TreeRenderer (WorldBuilder.Trees calls Begin before placing, Finish after)
        public static void Begin(int ourCount)
        {
            baseCount = ourCount; extra = new List<Proto>();
            if (!On) return;
            Dir(OUT);
            foreach (var p in PineSrc) { var pr = Pine(p); if (pr != null) extra.Add(pr); }
            { var sp = Pine(PineSrc[0], true); if (sp != null) extra.Add(sp); }   // legacy pinija slot: safe existing Aleppo geometry
            MedianHedge.Protos(extra);
            HedgeShell.Protos(extra);   // garden hedges' leaf modules (placed by Build tile buildings: HedgeShell.Commit)
            // crnika (holm oak / closed dark maquis crown): our olive's mesh, leaves a dark evergreen; materials made in Finish from
            // the scene's (leaf-clipped) olive materials (VegetationMask)
            var olive = Load<Mesh>(WorldBuilder.GEN + "/Plants/olive.mesh.asset");
            if (olive && VegetationMask.On) extra.Add(new Proto { id = "crnika", mesh = olive, mats = null, draw = 1000, near = 45 });
        }
        /// the height (m, at scale 1) of a prototype: ours by index or an extra one by id
        public static float Height(string id) { int i = Index(id); return i < 0 ? 0 : HeightOf(i); }
        public static float HeightOf(int proto)
        {
            if (proto >= baseCount && extra != null && proto - baseCount < extra.Count) { var m = extra[proto - baseCount].mesh; return m ? m.bounds.size.y : 0; }
            var pm = Load<Mesh>(WorldBuilder.GEN + "/Plants/" + TreeProtos.IDS[Mathf.Clamp(proto, 0, TreeProtos.IDS.Length - 1)] + ".mesh.asset"); return pm ? pm.bounds.size.y : 0;
        }
        /// one of the package pines (r in 0..1); the medium (bushy, 2.4 m) one only 1 in 8
        public static int PineIndex(float r)
        {
            var ids = extra.Select((p, i) => (p, i)).Where(t => t.p.id.StartsWith("pine:")).ToList();
            var tall = ids.Where(t => !t.p.id.Contains("medium")).ToList(); if (tall.Count == 0) tall = ids;
            if (r < .125f && tall.Count < ids.Count) return baseCount + ids.First(t => t.p.id.Contains("medium")).i;
            return baseCount + tall[Mathf.Min(tall.Count - 1, (int)((r - .125f) / .875f * tall.Count))].i;
        }
        public static int Index(string id) { if (extra == null) return -1; int i = extra.FindIndex(p => p.id == id); return i < 0 ? -1 : baseCount + i; }
        public static int PineCount => extra?.Count(p => p.id.StartsWith("pine:")) ?? 0;

        /// a tile tree's prototype in the boxes: our pines (0 / 1) become the package's Aleppo-shaped pines
        public static int TreeProto(int proto, double gx, double gz, float h, ref float scale)
        {
            if (extra == null || PineCount == 0 || proto > 1 || !In(gx, gz)) return proto;
            int k = Mathf.Min(PineCount - 1, (int)(R01(gx, gz, 11) * PineCount));
            // Aleppo pines in Kaštela: were .62–.87 (low, 5–8 m); 23. 11. Marko: "veliki borovi" — .75–1.35, a fifth of them big
            float r = R01(gx, gz, 12);
            scale *= VegetationMask.On ? (r < .2f ? 1.15f + .2f * r / .2f : .75f + .3f * r) : .8f + .35f * r;
            return baseCount + k;
        }

        /// extra plants: pines on the Kozjak slope above the probe (garrigue / rock above 60 m, not on roads), the D8 median hedge
        public static void Extra(List<byte> rec, List<ModelJ> models, ref int total)
        {
            if (extra == null || extra.Count == 0) return;
            void Put(double gx, double y, double gz, float yaw, float s, int proto) { var u = W.U(gx, y, gz); foreach (var v in new[] { u.x, u.y, u.z, yaw, s, proto }) rec.AddRange(BitConverter.GetBytes(v)); }
            int np = 0;
            if (VegetationMask.On) np = VegetationMask.Place((gx, y, gz, yaw, s, proto) => Put(gx, y, gz, yaw, s, proto), models);   // orthophoto clumps + OSM groves
            else if (PineCount > 0)
                foreach (var b in Boxes)
                    for (double gz = b.z0; gz < b.z1; gz += 7)
                        for (double gx = b.x0; gx < b.x1; gx += 7)
                        {
                            double x = gx + (R01(gx, gz, 21) - .5) * 6, z = gz + (R01(gx, gz, 22) - .5) * 6;
                            double h = Tiles.Height(x, z); if (h < 60) continue;
                            int sc = Tiles.Surface(x, z); if (sc != 10 && sc != 1 && sc != 0) continue;     // garrigue, rock, scrub
                            // clumps: pine stands, not an even sprinkle
                            float stand = Perlin(x, z, .012) * .7f + Perlin(x, z, .05) * .3f;
                            if (stand < .52f || R01(x, z, 23) > (stand - .52f) * 2.2f) continue;
                            if (WorldData.FootDist(models, x, z) < 6 || WorldData.SurfDist(models, x, z) < 5 || StreetsBuilder.Covered(x, z, 4)) continue;
                            int k = Mathf.Min(PineCount - 1, (int)(R01(x, z, 24) * PineCount));
                            Put(x, h - .1, z, R01(x, z, 25) * 360, .75f + .5f * R01(x, z, 26), baseCount + k); np++;
                        }
            total += np;
            int nh = MedianHedge.Place((gx, y, gz, yaw, s, id) => { int pi = Index(id); if (pi >= 0) Put(gx, y, gz, yaw, s, pi); }, models);
            total += nh;
            int nyh = VegetationMask.PlaceHedges((gx, y, gz, yaw, s, proto) => Put(gx, y, gz, yaw, s, proto));   // yard hedges (docs/audit-stari.md)
            total += nyh; Debug.Log($"[priroda] yard hedge plants {nyh}");
            total += GustaVegetacija.Place((gx, y, gz, yaw, s, proto) => Put(gx, y, gz, yaw, s, proto), models, rec);   // Kaštel Stari trial (VEGETACIJA_I_TLO.md)
            Debug.Log($"[priroda] extra pines {np}, median hedge plants {nh}");
        }

        /// append the prototypes to the scene's TreeRenderer arrays
        public static void Finish(Magistrala.TreeRenderer tr)
        {
            if (extra == null || extra.Count == 0) return;
            var meshes = tr.meshes.ToList(); var mats = tr.materials.ToList(); var bm = tr.blendMaterials.ToList(); var st = tr.materialStart.ToList();
            var draw = tr.drawDistance.ToList(); var near = tr.nearDistance.ToList(); var im = (tr.impMeshes ?? new Mesh[0]).ToList(); var imat = (tr.impMaterials ?? new Material[0]).ToList();
            while (im.Count < meshes.Count) { im.Add(null); imat.Add(null); }
            foreach (var p in extra.Where(p => p.mats == null && p.id == "crnika"))
            {
                // the olive (prototype 2) leaf-clipped materials and impostor, tinted dark evergreen (Quercus ilex: dark, dense, a little blue)
                int s0 = tr.materialStart[2], sn = tr.meshes[2].subMeshCount;
                // (25. 11.: the leaves through Foliage's grade, TreeProtos.CRNIKA_GRADE, instead of × .46 .56 .44 of an already dark
                // texture – near black; bark .55 -> .7. Its impostor atlas: "Magistrala/Trees: bake impostors" bakes it from these)
                p.mats = Enumerable.Range(s0, sn).Select(i => Tint(tr.materials[i], "crnika", tr.materials[i].name.Contains("bark") ? new Color(.7f, .68f, .66f, 1) : Color.white)).ToArray();
                foreach (var m in p.mats) if (m && m.shader && m.shader.name == "Magistrala/Foliage") TreeProtos.Grade(m, TreeProtos.CRNIKA_GRADE);
                p.imp = tr.impMeshes[2]; p.impMat = Tint(tr.impMaterials[2], "crnika", Color.white);
            }
            foreach (var p in extra)
            {
                meshes.Add(p.mesh); st.Add(mats.Count); mats.AddRange(p.mats); bm.AddRange(p.mats);
                draw.Add(p.draw); near.Add(p.near); im.Add(p.imp); imat.Add(p.impMat);
            }
            tr.meshes = meshes.ToArray(); tr.materials = mats.ToArray(); tr.blendMaterials = bm.ToArray(); tr.materialStart = st.ToArray();
            tr.drawDistance = draw.ToArray(); tr.nearDistance = near.ToArray(); tr.impMeshes = im.ToArray(); tr.impMaterials = imat.ToArray();
            EditorUtility.SetDirty(tr);
        }

        /// one package pine as a TreeRenderer prototype: LOD1 (bark + needles) as the full mesh, the cross billboard LOD as the
        /// impostor; both reshaped toward Aleppo pine (height × .82, crown spread × 1.22: a shorter trunk, a wider flatter crown) and the bark
        /// material made greyer brown instead of the beach pine's orange
        /// stone keeps the legacy pinija index / asset names, but now uses the same existing Aleppo mesh.
        /// The old per-vertex crown warp jumped from .45*top to .66*top: leaf cards crossing that
        /// boundary became hanging curtains. An umbrella crown requires a proper model, not this warp.
        static Proto Pine(string prefab, bool stone = false)
        {
            var pf = Load<GameObject>(CE + "Pine Trees/Prefabs/" + prefab + ".prefab"); if (!pf) { Debug.LogWarning("[priroda] no " + prefab); return null; }
            var lg = pf.GetComponentInChildren<LODGroup>(); if (!lg) return null;
            var lods = lg.GetLODs(); if (lods.Length < 2) return null;
            MeshRenderer R(int i) => lods[Mathf.Min(i, lods.Length - 1)].renderers.OfType<MeshRenderer>().FirstOrDefault();
            var full = R(2); var cross = R(lods.Length - 1); // LOD2 (~1.6–1.8k tris) near, the cross billboard far
            if (!full) return null;
            var root = pf.transform;
            Mesh Shape(MeshRenderer r, string name)
            {
                var src = r.GetComponent<MeshFilter>().sharedMesh; var m = UnityEngine.Object.Instantiate(src); m.name = name;
                var M = root.worldToLocalMatrix * r.transform.localToWorldMatrix; var v = src.vertices;
                for (int i = 0; i < v.Length; i++)
                {
                    var q = M.MultiplyPoint3x4(v[i]);
                    v[i] = new Vector3(q.x * 1.22f, q.y * .82f, q.z * 1.22f);
                }
                m.vertices = v; m.RecalculateBounds(); Save(m, $"{OUT}/{name}.asset"); return m;
            }
            string id = stone ? "pinija" : prefab.Replace("Prefab_Pine_beach_", "").ToLowerInvariant();
            var mats = full.sharedMaterials.Select(m => Aleppo(m)).ToArray();
            foreach (var m in mats) if (m) m.enableInstancing = true;
            var cm = cross && cross != full ? cross.sharedMaterial : null; if (cm) cm.enableInstancing = true;
            return new Proto
            {
                id = stone ? "pinija" : "pine:" + id, mesh = Shape(full, "pine_" + id), mats = mats,
                imp = cross && cross != full ? Shape(cross, "pine_" + id + "_cross") : null, impMat = cm,
                draw = 1400, near = 60,
            };
        }

        static Material Tint(Material m, string tag, Color c)
        {
            string p = $"{OUT}/{m.name}_{tag}.mat"; var t = Load<Material>(p);
            if (!t) { t = new Material(m); AssetDatabase.CreateAsset(t, p); } else { t.shader = m.shader; t.CopyPropertiesFromMaterial(m); }
            foreach (var prop in new[] { "baseColorFactor", "_BaseColor", "_Color" }) if (t.HasProperty(prop)) { var o = m.GetColor(prop); t.SetColor(prop, new Color(o.r * c.r, o.g * c.g, o.b * c.b, o.a)); }
            t.enableInstancing = true; EditorUtility.SetDirty(t); return t;
        }

        /// bark: greyer, browner (Aleppo pine bark is grey-brown, the beach pine's is orange); needles a touch darker / olive
        static Material Aleppo(Material m)
        {
            if (!m) return m;
            bool bark = m.name.ToLowerInvariant().Contains("bark") || m.name.ToLowerInvariant().Contains("trunk") || (m.shader && m.shader.name.Contains("Bark"));
            string p = $"{OUT}/{m.name}_aleppo.mat"; var c = Load<Material>(p);
            if (!c) { c = new Material(m); AssetDatabase.CreateAsset(c, p); } else { c.shader = m.shader; c.CopyPropertiesFromMaterial(m); }
            foreach (var prop in new[] { "_BaseColor", "_Color", "_TrunkBaseColor", "_BarkBaseColor" })
                if (c.HasProperty(prop)) c.SetColor(prop, bark ? new Color(.62f, .6f, .6f, 1) : new Color(.86f, .9f, .82f, 1));
            c.enableInstancing = true; EditorUtility.SetDirty(c); return c;
        }

        // ================================================================== texture budget
        /// the package ships 4k / 2k textures (499 MB of the build in the first probe): ground sets ≤ 2048, everything else ≤ 1024
        /// (plants, bark, cliffs are seen at a few metres at most from a truck cab); only changes what is above its budget
        [MenuItem("Magistrala/Priroda: teksture (budžet)")]
        public static string TextureBudget()
        {
            if (!On) return "[priroda] no package";
            int n = 0; var paths = new List<string>();
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { PKG }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (!(AssetImporter.GetAtPath(p) is TextureImporter imp)) continue;
                int max = p.Contains("/Ground/") ? 2048 : 1024;
                if (imp.maxTextureSize <= max) continue;
                imp.maxTextureSize = max; paths.Add(p);
            }
            AssetDatabase.StartAssetEditing();
            try { foreach (var p in paths) { ((TextureImporter)AssetImporter.GetAtPath(p)).SaveAndReimport(); n++; } }
            finally { AssetDatabase.StopAssetEditing(); }
            var m = $"[priroda] texture budget: {n} textures capped"; Debug.Log(m); return m;
        }

        // ================================================================== placed objects (Magistrala/Priroda: postavi)
        static readonly string[] CliffSrc = { "Prefab_beach_cliff_01_A", "Prefab_beach_cliff_01_B", "Prefab_beach_cliff_01_C", "Prefab_beach_cliff_02_A" };

        [MenuItem("Magistrala/Priroda: postavi")]
        public static string Place()
        {
            MainScene.Ensure();
            var root = WorldBuilder.Root("Nature");
            if (!On) return "[priroda] no package";
            var log = new System.Text.StringBuilder("[priroda] ");
            log.Append(TextureBudget()).Append("; ");
            // NatureManufacture wind (global shader wind for the foliage / grass / pines)
            var wind = Load<GameObject>(PKG + "/NatureManufacture Wind/Prefab_Wind.prefab");
            if (wind) { var w = (GameObject)PrefabUtility.InstantiatePrefab(wind); w.transform.SetParent(root.transform, false); w.transform.rotation = Quaternion.Euler(0, 200, 0); log.Append("wind; "); }
            log.Append(Cliffs(root.transform));
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            Debug.Log(log); return log.ToString();
        }

        /// grey-white limestone copies of the package cliff materials (their beach cliffs are sandy / clay)
        static Material Limestone(Material m)
        {
            if (!m) return m;
            string p = $"{OUT}/{m.name}_vapnenac.mat"; var c = Load<Material>(p);
            if (!c) { c = new Material(m); AssetDatabase.CreateAsset(c, p); } else { c.shader = m.shader; c.CopyPropertiesFromMaterial(m); }
            foreach (var prop in m.GetTexturePropertyNames()) { }
            foreach (var prop in new[] { "_BaseColor", "_Color", "_Base_Color", "_BaseColorTint" })
                if (c.HasProperty(prop)) c.SetColor(prop, new Color(1.05f, 1.04f, 1.02f, 1) * new Color(.95f, .95f, .97f));
            // a grey-white cover on top instead of grass / sand where the top-cover shaders have one
            foreach (var prop in new[] { "_Cover_Base_Color", "_CoverBaseColor", "_Cover_Color" }) if (c.HasProperty(prop)) c.SetColor(prop, new Color(.75f, .72f, .62f, 1));
            // close-up karst detail: Marko's approved rock_boulder_dry (Poly Haven CC0, 1.8 m; Tools/zbuke/materijali.py) as URP Lit's detail
            // albedo (x2, grey .5 = unchanged) + detail normal over the package's own texture (its UVs: ~12 m per repeat on 01, ~20 m on 02)
            if (c.HasProperty("_DetailAlbedoMap"))
            {
                var da = Detail("Assets/Textures/PolyHaven/rock_boulder_dry_detail.jpg", false); var dn = Detail("Assets/Textures/PolyHaven/rock_boulder_dry_n.jpg", true);
                if (da && dn)
                {
                    float rep = m.name.Contains("_02") ? 11f : 6.5f;
                    c.SetTexture("_DetailAlbedoMap", da); c.SetTexture("_DetailNormalMap", dn); c.SetFloat("_DetailAlbedoMapScale", 1); c.SetFloat("_DetailNormalMapScale", .8f);
                    c.SetTextureScale("_DetailAlbedoMap", new Vector2(rep, rep)); c.EnableKeyword("_DETAIL_MULX2");
                }
            }
            c.enableInstancing = true; EditorUtility.SetDirty(c); return c;
        }

        static Texture2D Detail(string path, bool normal)
        {
            if (AssetImporter.GetAtPath(path) is TextureImporter imp)
            {
                bool want = normal ? imp.textureType == TextureImporterType.NormalMap : !imp.sRGBTexture;
                if (!want || imp.anisoLevel != 4 || imp.maxTextureSize != 1024)
                {
                    if (normal) imp.textureType = TextureImporterType.NormalMap; else imp.sRGBTexture = false; // URP detail albedo is linear
                    imp.anisoLevel = 4; imp.maxTextureSize = 1024; imp.mipmapEnabled = true; imp.SaveAndReimport();
                }
            }
            return Load<Texture2D>(path);
        }

        static string Cliffs(Transform parent)
        {
            var src = CliffSrc.Select(n => Load<GameObject>(CE + "Cliffs/Prefabs/" + n + ".prefab")).Where(g => g).ToList();
            if (src.Count == 0) return "no cliffs; ";
            var terrains = UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
            // terrain by tile (the core of all Kaštela: millions of samples, a linear search over ~70 terrains per sample was minutes)
            var byTile = new Dictionary<(int, int), Terrain>();
            foreach (var t in terrains) { var parts = t.name.Split(' '); if (parts.Length < 2) continue; var q = parts[1].Split(','); if (q.Length == 2 && int.TryParse(q[0], out int ttx) && int.TryParse(q[1], out int ttz)) byTile[(ttx, ttz)] = t; }
            float H(double gx, double gz) { if (!byTile.TryGetValue(((int)Math.Floor(gx / 1024), (int)Math.Floor(gz / 1024)), out var t)) return float.NaN; var u = W.U(gx, 0, gz); return t.SampleHeight(u) + t.transform.position.y; }
            var models = WorldData.Models();
            var grp = new GameObject("cliffs"); grp.transform.SetParent(parent, false); grp.isStatic = true;
            var mats = new Dictionary<Material, Material>();
            var grid = new Dictionary<(int, int), List<Vector2>>(); int n = 0;
            (int, int) Key(Vector2 p) => ((int)Math.Floor(p.x / 16), (int)Math.Floor(p.y / 16));
            void Add(Vector2 p) { var k = Key(p); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<Vector2>(); l.Add(p); }
            bool Near(Vector2 p, float r)
            {
                var k = Key(p);
                for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++)
                        if (grid.TryGetValue((k.Item1 + a, k.Item2 + b), out var l)) foreach (var q in l) if ((q - p).sqrMagnitude < r * r) return true;
                return false;
            }
            foreach (var b in Boxes)
                for (double gz = b.z0; gz < b.z1; gz += 3)
                    for (double gx = b.x0; gx < b.x1; gx += 3)
                    {
                        float a = H(gx - 2, gz), c = H(gx + 2, gz), d = H(gx, gz - 2), e = H(gx, gz + 2);
                        if (float.IsNaN(a + c + d + e)) continue;
                        var grad = new Vector2(c - a, e - d) / 4f; float slope = grad.magnitude;   // rise per metre
                        if (slope < .55f) continue;                                                  // ≥ ~29°: cut faces, steep banks, embankments
                        var g2 = new Vector2((float)gx, (float)gz);
                        // spacing: 5.5 m low down (road cuts, banks seen from the cab), sparser up the Kozjak slope (seen from far: a
                        // face every ~14 m reads the same and keeps the object count down), none above 420 m (out of the playable view)
                        float hy = H(gx, gz); if (hy > 420) continue;
                        float sp = hy < 120 ? 5.5f : 14f;
                        if (Near(g2, sp)) continue;
                        // (3 m off a street's / model's paving: a face sunk 1 m and scaled up to 1.1 sticks out ~2 m downhill)
                        if (WorldData.SurfDist(models, gx, gz) < 3 || StreetsBuilder.Covered(gx, gz, 3)) continue;
                        // not on the shore / riva (the terrain is cut down under the photo sea and by the sea wall there), not on a house plot
                        if (hy < 2 || RivaBuilder.Class(gx, gz) != 0 || RivaBuilder.Class(gx + 4, gz) != 0 || RivaBuilder.Class(gx - 4, gz) != 0 || RivaBuilder.Class(gx, gz + 4) != 0 || RivaBuilder.Class(gx, gz - 4) != 0) continue;
                        if (WorldData.FootDist(models, gx, gz) < 2.5) continue;
                        var pf = src[Mathf.Min(src.Count - 1, (int)(R01(gx, gz, 31) * src.Count))];
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, grp.transform);
                        // face out of the slope (downhill), sunk into it
                        var down = new Vector3(-grad.x, 0, grad.y).normalized; // game z → unity -z
                        float y = hy;
                        go.transform.position = W.U(gx, y - 1.2, gz) - down * 1.0f;
                        go.transform.rotation = Quaternion.LookRotation(down, Vector3.up) * Quaternion.Euler(0, R01(gx, gz, 33) * 20 - 10, 0);
                        float sc = Mathf.Clamp(slope * .6f, .55f, 1.1f) * (.85f + .3f * R01(gx, gz, 34)); go.transform.localScale = Vector3.one * sc;
                        foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
                        {
                            var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) { if (!ms[i]) continue; if (!mats.TryGetValue(ms[i], out var lm)) mats[ms[i]] = lm = Limestone(ms[i]); ms[i] = lm; }
                            r.sharedMaterials = ms; r.shadowCastingMode = ShadowCastingMode.On;
                        }
                        foreach (var col in go.GetComponentsInChildren<Collider>()) UnityEngine.Object.DestroyImmediate(col);
                        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                        Add(g2); n++;
                    }
            return $"cliffs {n}; ";
        }
    }
}
