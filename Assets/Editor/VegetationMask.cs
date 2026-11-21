// Vegetacija Kaštela kakva stvarno jest (docs/vegetacija-d8.md): the mask Tools/vegetacija/maska.py derives from the DGU LiDAR
// orthophoto + OSM (Assets/Editor/vegetacija-maska.png, 4 m cells over the WorldBuilder core) drives Build trees in NaturePass.Boxes:
//   clumps    dense dark woody patches of the orthophoto (closed maquis / holm oak, low Aleppo pine, olive): 300–500 plants/ha low
//             down, ~110–200/ha up the Kozjak slope (seen only from afar), crowns closed; replaces the even sprinkle and the
//             Perlin slope pines
//   groves    OSM landuse=orchard: olives on a 7.5 m grid at ~0.77 of our olive
//   elsewhere the tile trees thinned to ~25–30 / ha (open land along the D8), ~10 / ha in built-up land, nothing 0–8 m from the
//             D8 edge; pines ~15 % and shorter, cypresses only a few beside houses (the rest become olives / holm oaks)
// Without the mask (or without the package) every hook leaves the old build unchanged.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class VegetationMask
    {
        public const string PATH = "Assets/Editor/vegetacija-maska.png";
        const double X0 = -10240, Z0 = -7168, CELL = 4;
        const int NX = 2560, NZ = 768;
        public const int CLUMP = 15, ORCHARD = 16, BUILT = 32, D8CLEAR = 64, D8NEAR = 128;
        static byte[] m;

        public static bool On => NaturePass.On && Load();
        static bool Load()
        {
            if (m != null) return true;
            if (!File.Exists(PATH)) return false;
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!t.LoadImage(File.ReadAllBytes(PATH)) || t.width != NX || t.height != NZ) { Debug.LogWarning("[vegetacija] bad mask " + PATH); UnityEngine.Object.DestroyImmediate(t); return false; }
            var px = t.GetPixels32(); m = new byte[NX * NZ];
            // the texture's row 0 is the PNG's last row; mask row 0 (PNG first row) = smallest game z
            for (int j = 0; j < NZ; j++) for (int i = 0; i < NX; i++) m[j * NX + i] = px[(NZ - 1 - j) * NX + i].r;
            UnityEngine.Object.DestroyImmediate(t);
            return true;
        }
        public static void Reset() { m = null; }

        /// the mask byte at game x, z (0 outside the core)
        public static int At(double gx, double gz)
        {
            if (m == null && !Load()) return 0;
            int i = (int)Math.Floor((gx - X0) / CELL), j = (int)Math.Floor((gz - Z0) / CELL);
            return i < 0 || j < 0 || i >= NX || j >= NZ ? 0 : m[j * NX + i];
        }

        // ------------------------------------------------------------------ tile trees
        /// local density of the tile trees (trees per ha in 32 m cells) of one tile's raw list (x, y, z, kind, scale)
        public static Dictionary<long, int> Density(List<float[]> raw)
        {
            var d = new Dictionary<long, int>();
            foreach (var f in raw) { if (f[3] == 4 || f[3] == 5 || f[3] == 13) continue; long k = Key(f[0], f[2]); d.TryGetValue(k, out int n); d[k] = n + 1; }
            return d;
        }
        static long Key(double x, double z) => ((long)Math.Floor(x / 32) << 32) ^ (uint)Math.Floor(z / 32);

        /// a tile tree after TreeProtos.Species (our prototypes 0 pine_a, 1 pine_b, 2 olive, 3 cypress, 4–6 shrubs, 7 rock): false =
        /// drop it; pines / cypresses may become an olive or a holm oak (crnika)
        public static bool TileTree(ref int proto, ref float s, double gx, double gz, Dictionary<long, int> dens, List<ModelJ> models, bool crown = false)
        {
            if (!On || !NaturePass.In(gx, gz)) return true;
            int v = At(gx, gz);
            if ((v & D8CLEAR) != 0) return false;                                     // 0–8 m from the D8 edge: kerb, dry weeds
            if (proto == 7) return true;                                              // rocks stay
            if ((v & CLUMP) != 0 || (v & ORCHARD) != 0) return false;                 // the clump / grove plants replace them
            float r = NaturePass.R01(gx, gz, 61);
            bool built = (v & BUILT) != 0, near = (v & D8NEAR) != 0;
            if (crown && built && YardTrees) return YardTree(ref proto, ref s, gx, gz);
            if (proto >= 4) return r < (built ? .3f : .6f);                           // shrubs: fewer
            dens.TryGetValue(Key(gx, gz), out int n);
            float perHa = Math.Max(1, n) / .1024f;
            float target = built ? (near ? 4 : 10) : (near ? 30 : 25);
            if (r > target / perHa) return false;
            float r2 = NaturePass.R01(gx, gz, 62);
            if (proto == 3 && !(WorldData.FootDist(models, gx, gz) < 18 && r2 < .3f)) { proto = 2; s = .75f + .2f * r2; }      // cypress → olive
            else if (proto <= 1 && r2 > .25f)                                                                                // pines → ~15 %
            {
                int c = NaturePass.Index("crnika");
                if (r2 < .62f && c >= 0) { proto = c; s = CrnikaScale(gx, gz, 1); } else { proto = 2; s = .75f + .2f * r2; }
            }
            return true;
        }

        // ------------------------------------------------------------------ yards (docs/audit-stari.md)
        /// the photo's single crowns between the houses (the ground build's trees, kinds 10–13) stay in built-up land: 60–90 crowns
        /// per ha in the towns, not ~10 (they were thinned with the open land's tile trees); off the pitches / car parks / pools and
        /// 1 m off the tile buildings' footprints; a third of the olive-sized crowns become dark evergreen broadleaves (citrus,
        /// carob, fig, holm oak read as crnika), half of the big pine-sized ones too, half of the cypress-sized ones become small olives
        public static bool YardTrees = true;
        public static int YardKept, YardDropped;
        static bool YardTree(ref int proto, ref float s, double gx, double gz)
        {
            if (GroundCover.Hard(gx, gz, 1.5) || NearBuilding(gx, gz)) { YardDropped++; return false; }
            float r2 = NaturePass.R01(gx, gz, 64); int c = NaturePass.Index("crnika");
            if (c >= 0 && ((proto == 2 && r2 < .33f) || (proto <= 1 && r2 < .5f))) { proto = c; s = CrnikaScale(gx, gz, proto <= 1 ? 1.15f : .9f); }
            else if (proto == 3 && r2 < .55f) { proto = 2; s = .7f + .25f * r2; }   // the photo's narrow crowns: about half are cypresses (the rest small olives / fruit trees)
            YardKept++;
            return true;
        }

        static readonly Dictionary<(int, int), bool[]> footMask = new Dictionary<(int, int), bool[]>();
        /// within 1 m of a building footprint (the tiles' buildings + the package's, OrthoGround's 0.5 m mask)
        public static bool NearBuilding(double gx, double gz)
        {
            int tx = (int)Math.Floor(gx / 1024), tz = (int)Math.Floor(gz / 1024);
            if (!footMask.TryGetValue((tx, tz), out var m))
            {
                try { m = OrthoGround.Grow(OrthoGround.BuildingMask(tx, tz, 0), 1.0); } catch (Exception e) { Debug.LogWarning("[vegetacija] footprints " + e.Message); m = null; }
                footMask[(tx, tz)] = m;
            }
            if (m == null) return false;
            int N = OrthoGround.MaskN; int r = (int)Math.Floor(((tz + 1) * 1024 - gz) / .5), i = (int)Math.Floor((gx - tx * 1024) / .5);
            return r >= 0 && i >= 0 && r < N && i < N && m[r * N + i];
        }

        /// yard hedges (the ground build's thin green strips along plot edges + OSM barrier=hedge): the dark hedge body (grm-zivica)
        /// every 0.8 m at the strip's height, an oleander now and then
        public static int PlaceHedges(PutFn put)
        {
            int z = NaturePass.Index("grm-zivica"), ol = NaturePass.Index("oleander-roza"), ow = NaturePass.Index("oleander-bijeli"); if (z < 0 || !On) return 0;
            float hz = NaturePass.Height("grm-zivica"), ho = ol >= 0 ? NaturePass.Height("oleander-roza") : 0; if (hz <= 0) hz = 1.8f;
            int n = 0;
            foreach (var h in GroundCover.Hedges)
            {
                double ax = h[0], az = h[1], bx = h[2], bz = h[3]; float H = (float)Math.Max(1.1, Math.Min(2.4, h[4]));
                double L = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)); if (L < 1) continue;
                float yaw = (float)(Math.Atan2(bx - ax, -(bz - az)) * 180 / Math.PI);
                for (double t = .4; t < L; t += .8)
                {
                    double x = ax + (bx - ax) * t / L, zz = az + (bz - az) * t / L;
                    if (StreetsBuilder.Covered(x, zz, .5) || GroundCover.Hard(x, zz, .3) || NearBuilding(x, zz) || RivaBuilder.NoTree(x, zz)) continue;
                    double y = Tiles.Height(x, zz); if (y < .6) continue;
                    float r = NaturePass.R01(x, zz, 81);
                    if (ol >= 0 && ho > 0 && r < .12f) put(x, y - .05, zz, yaw + r * 300, H * 1.05f / ho, r < .07f ? ol : ow);
                    else put(x, y - .05, zz, yaw + (r - .5f) * 30, H / hz * (.95f + .1f * r), z);
                    n++;
                }
            }
            return n;
        }

        static float CrnikaScale(double gx, double gz, float k)
        {
            float h = NaturePass.Height("crnika"); if (h <= 0) h = 2;
            return (3.2f + 2.6f * NaturePass.R01(gx, gz, 71)) * k / h;                // holm oak / maquis crowns 3–6 m tall
        }
        static float PineScale(int proto, double gx, double gz, float k)
        {
            float h = NaturePass.HeightOf(proto); if (h <= 0) h = 10;
            return (5.5f + 4f * NaturePass.R01(gx, gz, 72)) * k / h;                  // low Aleppo pines 5.5–9.5 m
        }

        // ------------------------------------------------------------------ clumps and groves
        public delegate void PutFn(double gx, double y, double gz, float yaw, float s, int proto);
        public static int[] Counts = new int[4]; // crnika, pine, olive (clumps), olive (groves)

        /// clump plants and grove olives over all boxes
        public static int Place(PutFn put, List<ModelJ> models)
        {
            Counts = new int[4];
            if (!On) return 0;
            int crn = NaturePass.Index("crnika"), np = NaturePass.PineCount; if (crn < 0 && np == 0) return 0;
            bool Free(double x, double z, out double h)
            {
                h = Tiles.Height(x, z);
                if (h < .6 || (At(x, z) & D8CLEAR) != 0) return false;
                if (WorldData.FootDist(models, x, z) < 3 || WorldData.SurfDist(models, x, z) < 1.5 || StreetsBuilder.Covered(x, z, 1.5) || RivaBuilder.NoTree(x, z) || GroundCover.Hard(x, z, 1)) return false;
                var q = WorldData.ModelTerrain(models, x, z, 2, h); if (q.HasValue) h = q.Value;
                return h >= .6;
            }
            int n = 0;
            foreach (var b in NaturePass.Boxes)
            {
                // clumps: a jittered grid, finer low down (seen from the road) than up the slope (seen from afar)
                const double G = 4.4;
                for (double gz = b.z0; gz < b.z1; gz += G)
                    for (double gx = b.x0; gx < b.x1; gx += G)
                    {
                        int v = At(gx, gz); int c = v & CLUMP; if (c == 0 || (v & ORCHARD) != 0) continue;
                        double x = gx + (NaturePass.R01(gx, gz, 41) - .5) * G * .8, z = gz + (NaturePass.R01(gx, gz, 42) - .5) * G * .8;
                        if ((At(x, z) & CLUMP) == 0) continue;
                        double h0 = Tiles.Height(x, z);
                        float keep = .55f + .45f * c / 15f;                                  // 285–516 / ha
                        bool high = h0 > 110; float k = 1;
                        if (high) { keep *= h0 > 300 ? .18f : .3f; k = 1.3f; }             // ~50–155 / ha, bigger crowns
                        if (NaturePass.R01(x, z, 43) > keep) continue;
                        if (!Free(x, z, out double h)) continue;
                        float r = NaturePass.R01(x, z, 44); float yaw = NaturePass.R01(x, z, 45) * 360;
                        if (r < (high ? .72f : .55f) && crn >= 0) { put(x, h - .15, z, yaw, CrnikaScale(x, z, k), crn); Counts[0]++; }
                        else if (r < (high ? 1f : .77f) && np > 0)
                        {
                            int p = NaturePass.PineIndex(NaturePass.R01(x, z, 46));
                            put(x, h - .1, z, yaw, PineScale(p, x, z, high ? 1.1f : 1), p); Counts[1]++;
                        }
                        else { put(x, h - .08, z, yaw, .75f + .2f * NaturePass.R01(x, z, 47), 2); Counts[2]++; }
                        n++;
                    }
                // olive groves: a regular 7.5 m grid, a gap here and there
                const double O = 7.5;
                for (double gz = Math.Ceiling(b.z0 / O) * O; gz < b.z1; gz += O)
                    for (double gx = Math.Ceiling(b.x0 / O) * O; gx < b.x1; gx += O)
                    {
                        if ((At(gx, gz) & ORCHARD) == 0 || NaturePass.R01(gx, gz, 51) < .08f) continue;
                        double x = gx + (NaturePass.R01(gx, gz, 52) - .5) * .6, z = gz + (NaturePass.R01(gx, gz, 53) - .5) * .6;
                        if (!Free(x, z, out double h)) continue;
                        put(x, h - .08, z, NaturePass.R01(gx, gz, 54) * 360, .72f + .1f * NaturePass.R01(gx, gz, 55), 2); Counts[3]++; n++;
                    }
            }
            Debug.Log($"[vegetacija] clumps: crnika {Counts[0]}, pines {Counts[1]}, olives {Counts[2]}; grove olives {Counts[3]}");
            return n;
        }
    }
}
