// Reading the web game's data (BalkanDriver repo, read-only): model-junctions.json (+ RLE grids), GLB layouts, tile .bin heights.
//
// COORDINATES. The web game (three.js, right-handed): x = east, y = up, z = south (z = -north), metres.
// Unity (left-handed): x = east, y = up, z = north. Mapping: unity = (gx - OX, gy, -(gz - OZ)) — z is flipped (a reflection, so
// triangle winding is reversed on import) and the origin is moved to the middle of the Lukšić–Sućurac stretch (OX, OZ) so the
// physics / rendering work within ±4 km of the origin.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class W
    {
        public const double OX = -4200, OZ = -5300;
        public const string BD = "/Users/macair/Documents/BalkanDriver";
        public const string REGION = BD + "/client/public/assets/world/split-kastela/";
        public const string KRIZ = BD + "/client/public/assets/models/krizanja/";
        public static Vector3 U(double gx, double gy, double gz) => new Vector3((float)(gx - OX), (float)gy, (float)(-(gz - OZ)));
        public static Vector2 G(Vector3 u) => new Vector2((float)(u.x + OX), (float)(-u.z + OZ));
    }

    public class ModelGrid
    {
        public double x0, z0, cell; public int n;
        public float[] surf, dist, near, foot; // NaN = null
        public int Cell(double x, double z)
        {
            int i = (int)Math.Round((x - x0) / cell), j = (int)Math.Round((z - z0) / cell);
            return i < 0 || j < 0 || i >= n || j >= n ? -1 : j * n + i;
        }
    }

    public class ModelJ
    {
        public string key, glb; public double x, y, z, half; public double[] lin; public double padE, padW, padN, padS;
        public List<double[]> hfix = new List<double[]>(); public bool tile;
        public List<List<double[]>> rails = new List<List<double[]>>();
        public ModelGrid grid;
        /// mag-23-11-v2: width of the GLB `ground` strip beside the paving (0 = the old full-square plate, Split / Solin). gnd = the strip's
        /// world y on the model grid (NaN off it), gndIn = how far inside the strips a cell lies (m, to the outer edge of all strips together) — LoadGround, StripEdges
        public double strip; public float[] gnd, gndIn;
        public List<(List<double[]> p, double w, int n, int lvl)> roads = new List<(List<double[]>, double, int, int)>();

        /// a tile edge short of / overlapping its neighbour (data `pad`, mag-11-19: also n / s, negative = 10 cm overlap of the old-road
        /// tiles, pulled back): the GLB vertex on that edge moves by it (model frame, before `lin`; ui/ModelJunctions2.tsx)
        public void Pad(ref double x, ref double z)
        {
            double L0 = lin?[0] ?? 1, L3 = lin?[3] ?? 1;
            if (padE != 0 && x > half - 0.02) x += padE / L0;
            if (padW != 0 && x < -half + 0.02) x -= padW / L0;
            if (padS != 0 && z > half - 0.02) z += padS / L3;
            if (padN != 0 && z < -half + 0.02) z -= padN / L3;
        }
        public double HfixAt(double px, double pz)
        {
            double sw = 0, sv = 0;
            foreach (var p in hfix)
            {
                double d2 = (px - p[0]) * (px - p[0]) + (pz - p[1]) * (pz - p[1]);
                if (d2 < 1e-6) return p[2];
                double w = 1 / d2; sw += w; sv += w * p[2];
            }
            return sw > 0 ? sv / sw : 0;
        }
        /// model surface world y at game (x, z), NaN off asphalt / islands
        public double Surface(double gx, double gz)
        {
            int c = grid.Cell(gx, gz); if (c < 0) return double.NaN;
            float h = grid.surf[c]; return float.IsNaN(h) ? double.NaN : y + h;
        }
    }

    public static class WorldData
    {
        static float[] DecodeRle(object o, float scale)
        {
            if (o is List<object> l) { var a = new float[l.Count]; for (int i = 0; i < l.Count; i++) a[i] = l[i] == null ? float.NaN : MiniJson.F(l[i]); return a; }
            var s = (string)o; var outL = new List<float>(s.Length / 2);
            foreach (var tok in s.Split(' '))
            {
                if (tok.Length == 0) continue;
                int k = tok.IndexOf('*'); string v = k < 0 ? tok : tok.Substring(0, k); int n = k < 0 ? 1 : int.Parse(tok.Substring(k + 1));
                float x = v == "n" ? float.NaN : float.Parse(v, System.Globalization.CultureInfo.InvariantCulture) / scale;
                for (int i = 0; i < n; i++) outL.Add(x);
            }
            return outL.ToArray();
        }

        static List<ModelJ> cache;
        // (west edge −10240: mag-11-19 s01 / c01 / 42 at game x −9340 … −9590 — the Štafilić end of the old road and of Škabrnjska)
        public const double MIN_X = -10240;
        public static List<ModelJ> Models(double minX = MIN_X, double maxX = -400)
        {
            if (cache == null)
            {
                cache = new List<ModelJ>();
                var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "model-junctions.json")));
                foreach (var jo in MiniJson.L(root["junctions"]))
                {
                    var j = MiniJson.O(jo);
                    var m = new ModelJ
                    {
                        key = (string)j["key"], glb = Path.GetFileName((string)j["glb"]),
                        x = MiniJson.D(j["x"]), y = MiniJson.D(j["y"]), z = MiniJson.D(j["z"]), half = MiniJson.D(j["half"]),
                        tile = MiniJson.G(j, "tile") is bool b && b,
                    };
                    if (MiniJson.G(j, "lin") is List<object> lin) { m.lin = new double[4]; for (int i = 0; i < 4; i++) m.lin[i] = MiniJson.D(lin[i]); }
                    if (MiniJson.G(j, "pad") is Dictionary<string, object> pad) { m.padE = pad.ContainsKey("e") ? MiniJson.D(pad["e"]) : 0; m.padW = pad.ContainsKey("w") ? MiniJson.D(pad["w"]) : 0; m.padN = pad.ContainsKey("n") ? MiniJson.D(pad["n"]) : 0; m.padS = pad.ContainsKey("s") ? MiniJson.D(pad["s"]) : 0; }
                    if (MiniJson.G(j, "hfix") is List<object> hf) foreach (var p in hf) { var q = MiniJson.L(p); m.hfix.Add(new[] { MiniJson.D(q[0]), MiniJson.D(q[1]), MiniJson.D(q[2]) }); }
                    if (MiniJson.G(j, "rails") is List<object> rl)
                        foreach (var r in rl) { var pts = new List<double[]>(); foreach (var p in MiniJson.L(MiniJson.O(r)["p"])) { var q = MiniJson.L(p); pts.Add(new[] { MiniJson.D(q[0]), MiniJson.D(q[1]), MiniJson.D(q[2]), MiniJson.D(q[3]) }); } m.rails.Add(pts); }
                    if (MiniJson.G(j, "roads") is List<object> rds)
                        foreach (var r in rds)
                        {
                            var ro = MiniJson.O(r); var pts = new List<double[]>();
                            foreach (var q in MiniJson.L(ro["p"])) { var qq = MiniJson.L(q); pts.Add(new[] { MiniJson.D(qq[0]), MiniJson.D(qq[1]) }); }
                            m.roads.Add((pts, MiniJson.D(ro["w"]), (int)MiniJson.D(ro["n"]), ro.ContainsKey("lvl") ? (int)MiniJson.D(ro["lvl"]) : 0));
                        }
                    var g = MiniJson.O(j["grid"]);
                    m.grid = new ModelGrid
                    {
                        x0 = MiniJson.D(g["x0"]), z0 = MiniJson.D(g["z0"]), cell = MiniJson.D(g["cell"]), n = (int)MiniJson.D(g["n"]),
                        surf = DecodeRle(g["surf"], 100), near = DecodeRle(g["near"], 100), dist = DecodeRle(g["dist"], 10), foot = DecodeRle(g["foot"], 10),
                    };
                    if (MiniJson.G(j, "strip") is object st) { m.strip = MiniJson.D(st); LoadGround(m); }
                    cache.Add(m);
                }
                StripEdges(cache);
            }
            return cache.FindAll(m => m.x >= minX && m.x <= maxX);
        }

        /// the model's `ground` strip rasterised onto its grid (world-aligned 2 m points, as `surf`): world y of the strip and the distance
        /// from each strip point to the strip's outer edge (chamfer, m). README_11-23-v2.md §1: the strip's outer edge lies on the terrain
        static void LoadGround(ModelJ m)
        {
            var g = m.grid; int n = g.n; m.gnd = new float[n * n]; for (int i = 0; i < m.gnd.Length; i++) m.gnd[i] = float.NaN;
            m.gndIn = new float[n * n];
            GlbMesh gm = null; try { gm = Glb.Read(W.KRIZ + m.glb).Find(q => q.name == "ground"); } catch (Exception e) { Debug.LogWarning($"[strip] {m.key}: {e.Message}"); }
            if (gm == null) { m.strip = 0; return; }
            // vertices in world x / z (pad, lin as Build roads places the mesh), y world
            var P = new Vector3[gm.pos.Length];
            for (int i = 0; i < P.Length; i++)
            {
                double x = gm.pos[i].x, y = gm.pos[i].y, z = gm.pos[i].z;
                if (m.hfix.Count > 0) y += m.HfixAt(x, z);
                m.Pad(ref x, ref z);
                if (m.lin != null) { double nx = m.lin[0] * x + m.lin[1] * z, nz = m.lin[2] * x + m.lin[3] * z; x = nx; z = nz; }
                P[i] = new Vector3((float)(m.x + x - g.x0), (float)(m.y + y), (float)(m.z + z - g.z0)); // (x, z relative to the grid origin)
            }
            for (int t = 0; t + 2 < gm.idx.Length; t += 3)
            {
                Vector3 a = P[gm.idx[t]], b = P[gm.idx[t + 1]], c = P[gm.idx[t + 2]];
                double d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z); if (Math.Abs(d) < 1e-9) continue;
                int i0 = Math.Max(0, (int)Math.Floor(Math.Min(a.x, Math.Min(b.x, c.x)) / g.cell)), i1 = Math.Min(n - 1, (int)Math.Ceiling(Math.Max(a.x, Math.Max(b.x, c.x)) / g.cell));
                int j0 = Math.Max(0, (int)Math.Floor(Math.Min(a.z, Math.Min(b.z, c.z)) / g.cell)), j1 = Math.Min(n - 1, (int)Math.Ceiling(Math.Max(a.z, Math.Max(b.z, c.z)) / g.cell));
                for (int jj = j0; jj <= j1; jj++)
                    for (int ii = i0; ii <= i1; ii++)
                    {
                        double x = ii * g.cell, z = jj * g.cell;
                        double l0 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d, l1 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d, l2 = 1 - l0 - l1;
                        if (l0 < -1e-4 || l1 < -1e-4 || l2 < -1e-4) continue;
                        float h = (float)(l0 * a.y + l1 * b.y + l2 * c.y); int k = jj * n + ii;
                        if (float.IsNaN(m.gnd[k]) || h > m.gnd[k]) m.gnd[k] = h;
                    }
            }
        }

        /// gndIn: distance (m) from each strip point to the outer edge of ALL strips together — a point off this model's strip that a
        /// neighbour's strip covers (they overlap 2 m along shared square edges) is no edge, so the seams get no crease
        static void StripEdges(List<ModelJ> all)
        {
            var strips = all.FindAll(q => q.gnd != null);
            foreach (var m in strips)
            {
                var g = m.grid; int n = g.n; var D = m.gndIn; const float BIG = 1e9f;
                for (int k = 0; k < D.Length; k++)
                {
                    D[k] = BIG;
                    if (!float.IsNaN(m.gnd[k])) continue;
                    double x = g.x0 + (k % n) * g.cell, z = g.z0 + (k / n) * g.cell; bool other = false;
                    foreach (var o in strips) { if (o == m) continue; int c = o.grid.Cell(x, z); if (c >= 0 && !float.IsNaN(o.gnd[c])) { other = true; break; } }
                    if (!other) D[k] = 0;
                }
                int[,] fw = { { -1, 0 }, { 0, -1 }, { -1, -1 }, { 1, -1 } }, bw = { { 1, 0 }, { 0, 1 }, { 1, 1 }, { -1, 1 } };
                for (int pass = 0; pass < 2; pass++)
                {
                    var nb = pass == 0 ? fw : bw;
                    for (int q = 0; q < n * n; q++)
                    {
                        int k = pass == 0 ? q : n * n - 1 - q; if (D[k] == 0) continue;
                        int x = k % n, z = k / n;
                        for (int r = 0; r < 4; r++)
                        {
                            int X = x + nb[r, 0], Z = z + nb[r, 1]; if (X < 0 || Z < 0 || X >= n || Z >= n) continue;
                            float w = (nb[r, 0] != 0 && nb[r, 1] != 0 ? 1.4142f : 1) * (float)g.cell;
                            if (D[Z * n + X] + w < D[k]) D[k] = D[Z * n + X] + w;
                        }
                    }
                }
            }
        }

        static float Smooth(double t) => t <= 0 ? 0 : t >= 1 ? 1 : (float)(t * t * (3 - 2 * t));

        /// port of modelJunctions.ts modelTerrain: ground under / around a model (reach = terrain cell span); null = untouched
        public const double STRIP_UNDER = 0.3, STRIP_BLEND = 8;
        public static double? ModelTerrain(List<ModelJ> list, double x, double z, double reach, double ours, double under = 0.25)
        {
            // (unlike the TS loop that returns the first model in reach: squares overlap at the tile seams, so first look for the
            // asphalt under the point in ANY model, then blend towards the nearest one)
            double lo = double.PositiveInfinity, r = reach, h2 = reach / 2;
            double[,] off = { { 0, 0 }, { r, 0 }, { -r, 0 }, { 0, r }, { 0, -r }, { r, r }, { -r, r }, { r, -r }, { -r, -r }, { h2, 0 }, { -h2, 0 }, { 0, h2 }, { 0, -h2 } };
            ModelJ best = null; double bestD = double.PositiveInfinity; int bestC = -1;
            foreach (var m in list)
            {
                if (Math.Abs(x - m.x) > m.half + 12 || Math.Abs(z - m.z) > m.half + 12) continue;
                for (int k = 0; k < 13; k++) { double h = m.Surface(x + off[k, 0], z + off[k, 1]); if (!double.IsNaN(h)) lo = Math.Min(lo, h); }
                int c = m.grid.Cell(x, z);
                if (c >= 0 && !float.IsNaN(m.grid.near[c]) && m.grid.dist[c] < bestD) { bestD = m.grid.dist[c]; best = m; bestC = c; }
            }
            if (!double.IsInfinity(lo)) return lo - under;
            // mag-23-11-v2 strip models: under the `ground` strip the terrain is the strip − 0.3 m (README_11-23-v2.md §1), blended into
            // ours over the strip's last STRIP_BLEND m (its outer edge is Marko's 60 m DEM, ours is 0.9 m higher on average, up to 7 m — so 8 m, not the README's 5);
            // where neighbouring strips overlap, the one the point lies deeper inside
            double gy = double.NaN, gin = -1;
            foreach (var m in list)
            {
                if (m.gnd == null || Math.Abs(x - m.x) > m.half + 12 || Math.Abs(z - m.z) > m.half + 12) continue;
                int c = m.grid.Cell(x, z); if (c < 0 || float.IsNaN(m.gnd[c])) continue;
                if (m.gndIn[c] > gin) { gin = m.gndIn[c]; gy = m.gnd[c] - STRIP_UNDER; }
            }
            if (!double.IsNaN(gy)) return ours + (gy - ours) * Smooth((gin - 1) / STRIP_BLEND);
            // (off the strip of a strip model: the world terrain as everywhere — no pull towards its roads / square)
            if (best != null && best.gnd != null) return null;
            if (best == null || bestD > reach + 12) return null;
            double w = 1 - Smooth((bestD - reach) / 12);
            return ours + (best.y + best.grid.near[bestC] - 0.18 - ours) * w;
        }

        public static double FootDist(List<ModelJ> list, double x, double z)
        {
            double best = double.PositiveInfinity;
            foreach (var m in list) { int c = m.grid.Cell(x, z); if (c >= 0) best = Math.Min(best, m.grid.foot[c]); }
            return best;
        }
        public static double SurfDist(List<ModelJ> list, double x, double z)
        {
            double best = double.PositiveInfinity;
            foreach (var m in list) { int c = m.grid.Cell(x, z); if (c >= 0) best = Math.Min(best, float.IsNaN(m.grid.surf[c]) ? m.grid.dist[c] : 0); }
            return best;
        }
    }

    // ---------------------------------------------------------------- GLB (the package's layouts: flat nodes, float VEC3, uint32 indices)
    public class GlbMesh { public string name; public Vector3[] pos, nrm, col; public Vector2[] uv; public int[] idx; public Dictionary<string, object> mat; }

    public static class Glb
    {
        public static List<GlbMesh> Read(string path)
        {
            var b = File.ReadAllBytes(path);
            int jl = BitConverter.ToInt32(b, 12);
            var j = MiniJson.O(MiniJson.Parse(System.Text.Encoding.UTF8.GetString(b, 20, jl)));
            int bin = 20 + jl + 8;
            var acc = MiniJson.L(j["accessors"]); var bvs = MiniJson.L(j["bufferViews"]); var meshes = MiniJson.L(j["meshes"]);
            var outL = new List<GlbMesh>();
            foreach (var no in MiniJson.L(j["nodes"]))
            {
                var n = MiniJson.O(no); if (!n.ContainsKey("mesh")) continue;
                var prim = MiniJson.O(MiniJson.L(MiniJson.O(meshes[(int)MiniJson.D(n["mesh"])])["primitives"])[0]);
                var at = MiniJson.O(prim["attributes"]);
                var gm = new GlbMesh { name = (string)n["name"] };
                gm.pos = V3(b, bin, acc, bvs, at, "POSITION"); gm.nrm = V3(b, bin, acc, bvs, at, "NORMAL"); gm.col = V3(b, bin, acc, bvs, at, "COLOR_0"); gm.uv = V2(b, bin, acc, bvs, at, "TEXCOORD_0");
                if (prim.ContainsKey("material") && j.ContainsKey("materials")) gm.mat = MiniJson.O(MiniJson.L(j["materials"])[(int)MiniJson.D(prim["material"])]);
                if (prim.ContainsKey("indices"))
                {
                    var a = MiniJson.O(acc[(int)MiniJson.D(prim["indices"])]); var bv = MiniJson.O(bvs[(int)MiniJson.D(a["bufferView"])]);
                    int off = bin + (int)MiniJson.D(MiniJson.G(bv, "byteOffset") ?? 0.0) + (int)MiniJson.D(MiniJson.G(a, "byteOffset") ?? 0.0), cnt = (int)MiniJson.D(a["count"]);
                    int ct = (int)MiniJson.D(a["componentType"]);
                    gm.idx = new int[cnt];
                    for (int i = 0; i < cnt; i++) gm.idx[i] = ct == 5125 ? (int)BitConverter.ToUInt32(b, off + i * 4) : ct == 5123 ? BitConverter.ToUInt16(b, off + i * 2) : b[off + i];
                }
                else { gm.idx = new int[gm.pos.Length]; for (int i = 0; i < gm.idx.Length; i++) gm.idx[i] = i; }
                outL.Add(gm);
            }
            return outL;
        }

        static Vector2[] V2(byte[] b, int bin, List<object> acc, List<object> bvs, Dictionary<string, object> at, string key)
        {
            if (!at.ContainsKey(key)) return null;
            var a = MiniJson.O(acc[(int)MiniJson.D(at[key])]);
            if ((string)a["type"] != "VEC2" || (int)MiniJson.D(a["componentType"]) != 5126) return null;
            var bv = MiniJson.O(bvs[(int)MiniJson.D(a["bufferView"])]);
            int off = bin + (int)MiniJson.D(MiniJson.G(bv, "byteOffset") ?? 0.0) + (int)MiniJson.D(MiniJson.G(a, "byteOffset") ?? 0.0), cnt = (int)MiniJson.D(a["count"]);
            int stride = MiniJson.G(bv, "byteStride") is object s ? (int)MiniJson.D(s) : 8;
            var v = new Vector2[cnt];
            for (int i = 0; i < cnt; i++) { int o = off + i * stride; v[i] = new Vector2(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4)); }
            return v;
        }

        static Vector3[] V3(byte[] b, int bin, List<object> acc, List<object> bvs, Dictionary<string, object> at, string key)
        {
            if (!at.ContainsKey(key)) return null;
            var a = MiniJson.O(acc[(int)MiniJson.D(at[key])]);
            if ((string)a["type"] != "VEC3" || (int)MiniJson.D(a["componentType"]) != 5126) return null;
            var bv = MiniJson.O(bvs[(int)MiniJson.D(a["bufferView"])]);
            int off = bin + (int)MiniJson.D(MiniJson.G(bv, "byteOffset") ?? 0.0) + (int)MiniJson.D(MiniJson.G(a, "byteOffset") ?? 0.0), cnt = (int)MiniJson.D(a["count"]);
            int stride = MiniJson.G(bv, "byteStride") is object s ? (int)MiniJson.D(s) : 12;
            var v = new Vector3[cnt];
            for (int i = 0; i < cnt; i++) { int o = off + i * stride; v[i] = new Vector3(BitConverter.ToSingle(b, o), BitConverter.ToSingle(b, o + 4), BitConverter.ToSingle(b, o + 8)); }
            return v;
        }
    }

    // ---------------------------------------------------------------- terrain tiles (docs/world-format.md: 1024 m, 129² heights, sections)
    public class TileData { public int tx, tz, res; public float[] heights; public byte[] surface; public float[] trees; public string buildingsJson; }

    public static class Tiles
    {
        static readonly Dictionary<long, TileData> cache = new Dictionary<long, TileData>();
        public static TileData Get(int tx, int tz)
        {
            long key = ((long)tx << 32) ^ (uint)tz;
            if (cache.TryGetValue(key, out var t)) return t;
            string p = W.REGION + $"tiles/{tx}_{tz}.bin";
            t = null;
            if (File.Exists(p))
            {
                var b = File.ReadAllBytes(p);
                t = new TileData { tx = tx, tz = tz, res = BitConverter.ToUInt16(b, 6) };
                int o = 16;
                while (o + 8 <= b.Length)
                {
                    int type = BitConverter.ToInt32(b, o), len = BitConverter.ToInt32(b, o + 4); o += 8;
                    if (type == 1 || type == 5) { var f = new float[len / 4]; Buffer.BlockCopy(b, o, f, 0, (len / 4) * 4); if (type == 1) t.heights = f; else t.trees = f; }
                    else if (type == 2) { t.surface = new byte[len]; Buffer.BlockCopy(b, o, t.surface, 0, len); }
                    else if (type == 4) t.buildingsJson = System.Text.Encoding.UTF8.GetString(b, o, len);
                    o += len;
                }
            }
            cache[key] = t; return t;
        }

        /// ground height (game frame): the baked tiles' height, levelled under the sports pitches (GroundCover.Level)
        public static double Height(double gx, double gz) => GroundCover.Level(gx, gz, Raw(gx, gz));
        /// baked ground height (game frame), bilinear on the 8 m grid; sea bed (-6) outside the data
        public static double Raw(double gx, double gz)
        {
            int tx = (int)Math.Floor(gx / 1024), tz = (int)Math.Floor(gz / 1024);
            var t = Get(tx, tz); if (t?.heights == null) return -6;
            int r = t.res; double s = 1024.0 / (r - 1);
            double fx = (gx - tx * 1024) / s, fz = (gz - tz * 1024) / s;
            int ix = Math.Min(r - 2, (int)fx), iz = Math.Min(r - 2, (int)fz); double ax = fx - ix, az = fz - iz;
            float h00 = t.heights[iz * r + ix], h10 = t.heights[iz * r + ix + 1], h01 = t.heights[(iz + 1) * r + ix], h11 = t.heights[(iz + 1) * r + ix + 1];
            return (h00 * (1 - ax) + h10 * ax) * (1 - az) + (h01 * (1 - ax) + h11 * ax) * az;
        }
        public static int Surface(double gx, double gz)
        {
            int tx = (int)Math.Floor(gx / 1024), tz = (int)Math.Floor(gz / 1024);
            var t = Get(tx, tz); if (t?.surface == null) return 6;
            int r = t.res; double s = 1024.0 / (r - 1);
            int ix = (int)Math.Round((gx - tx * 1024) / s), iz = (int)Math.Round((gz - tz * 1024) / s);
            return t.surface[Math.Min(r - 1, iz) * r + Math.Min(r - 1, ix)];
        }
    }
}
