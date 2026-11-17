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
        public string key, glb; public double x, y, z, half; public double[] lin; public double padE, padW;
        public List<double[]> hfix = new List<double[]>(); public bool tile;
        public List<List<double[]>> rails = new List<List<double[]>>();
        public ModelGrid grid;
        public List<(List<double[]> p, double w, int n, int lvl)> roads = new List<(List<double[]>, double, int, int)>();

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
        public static List<ModelJ> Models(double minX = -8100, double maxX = -400)
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
                    if (MiniJson.G(j, "pad") is Dictionary<string, object> pad) { m.padE = pad.ContainsKey("e") ? MiniJson.D(pad["e"]) : 0; m.padW = pad.ContainsKey("w") ? MiniJson.D(pad["w"]) : 0; }
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
                    cache.Add(m);
                }
            }
            return cache.FindAll(m => m.x >= minX && m.x <= maxX);
        }

        static float Smooth(double t) => t <= 0 ? 0 : t >= 1 ? 1 : (float)(t * t * (3 - 2 * t));

        /// port of modelJunctions.ts modelTerrain: ground under / around a model (reach = terrain cell span); null = untouched
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
    public class GlbMesh { public string name; public Vector3[] pos, nrm, col; public int[] idx; }

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
                gm.pos = V3(b, bin, acc, bvs, at, "POSITION"); gm.nrm = V3(b, bin, acc, bvs, at, "NORMAL"); gm.col = V3(b, bin, acc, bvs, at, "COLOR_0");
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

        /// baked ground height (game frame), bilinear on the 8 m grid; sea bed (-6) outside the data
        public static double Height(double gx, double gz)
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
