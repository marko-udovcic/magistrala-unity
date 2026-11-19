// Lanes of a model road where its mesh really has them. model-junctions.json gives every road one width w and lane count n for
// its whole length, but on the D8 arms of junctions 53 / 54 (n = 3, w = 10.8 / 10.6 / 12.6 m) the asphalt is 7.2 m / 2 lanes over
// most of the arm: the third lane is only a pocket near the junction. Built on n = 3 (as everything that read w / n did) the right
// lane lay 1–2 m off the asphalt (traffic, autopilot, the asphalt shader's tyre tracks).
// Here: cross-sections of the model's own asphalt triangles every metre along the road. Lane k of the json's split (centre
// w/2 − (k + ½)·w/n right of the axis, width w/n) exists where at least LANE_MIN of its band is asphalt (at most the json's n,
// never fewer than 2); a lane on asphalt only in part sits in the middle of that part. Only roads with n ≥ 3 are profiled
// (n ≤ 2 roads keep w / n).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class LaneProfile
    {
        public const double LANE_MIN = 2.9;    // m of a lane band on asphalt for the lane to exist
        public class Profile
        {
            public double len;
            public float[][] off;              // per metre of arc: centres of the existing lanes, right first (m right of the axis)
            public float[] uL, uR;             // per metre: the existing lanes' asphalt from / to (m right of the axis)
            public int Idx(double s) => Math.Max(0, Math.Min(off.Length - 1, (int)Math.Round(s)));
        }

        // ---------------------------------------------------------------- asphalt triangles of a model (game x, z), bucketed
        class Tris
        {
            public readonly List<Vector2[]> t = new List<Vector2[]>();
            public readonly Dictionary<long, List<int>> g = new Dictionary<long, List<int>>();
            const double C = 3;
            static long K(int i, int j) => ((long)i << 32) ^ (uint)j;
            public void Add(Vector2 a, Vector2 b, Vector2 c)
            {
                int id = t.Count; t.Add(new[] { a, b, c });
                int i0 = (int)Math.Floor(Math.Min(a.x, Math.Min(b.x, c.x)) / C), i1 = (int)Math.Floor(Math.Max(a.x, Math.Max(b.x, c.x)) / C);
                int j0 = (int)Math.Floor(Math.Min(a.y, Math.Min(b.y, c.y)) / C), j1 = (int)Math.Floor(Math.Max(a.y, Math.Max(b.y, c.y)) / C);
                for (int i = i0; i <= i1; i++) for (int j = j0; j <= j1; j++) { if (!g.TryGetValue(K(i, j), out var l)) g[K(i, j)] = l = new List<int>(); l.Add(id); }
            }
            public bool Hit(double x, double z)
            {
                if (!g.TryGetValue(K((int)Math.Floor(x / C), (int)Math.Floor(z / C)), out var l)) return false;
                foreach (int id in l)
                {
                    var q = t[id];
                    double d1 = (x - q[1].x) * (q[0].y - q[1].y) - (q[0].x - q[1].x) * (z - q[1].y);
                    double d2 = (x - q[2].x) * (q[1].y - q[2].y) - (q[1].x - q[2].x) * (z - q[2].y);
                    double d3 = (x - q[0].x) * (q[2].y - q[0].y) - (q[2].x - q[0].x) * (z - q[0].y);
                    if (!((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0))) return true;
                }
                return false;
            }
        }

        static readonly Dictionary<string, Tris> asphalt = new Dictionary<string, Tris>();
        static readonly Dictionary<List<double[]>, Profile> cache = new Dictionary<List<double[]>, Profile>();
        public static void Clear() { asphalt.Clear(); cache.Clear(); }

        /// game-frame (x, z) of a GLB vertex of model m (WorldBuilder.Roads: pads, lin, model origin)
        public static Vector2 GamePos(ModelJ m, Vector3 p)
        {
            double x = p.x, z = p.z;
            m.Pad(ref x, ref z);
            if (m.lin != null) { double nx = m.lin[0] * x + m.lin[1] * z, nz = m.lin[2] * x + m.lin[3] * z; x = nx; z = nz; }
            return new Vector2((float)(m.x + x), (float)(m.z + z));
        }

        static Tris Asphalt(ModelJ m) => Mesh2D(m, "asphalt");
        /// a GLB mesh of model m (asphalt, island, sidewalk, markings…) as 2D triangles in the game frame
        static Tris Mesh2D(ModelJ m, string name)
        {
            string key = m.key + "/" + name;
            if (asphalt.TryGetValue(key, out var tr)) return tr;
            tr = new Tris();
            foreach (var gm in Glb.Read(W.KRIZ + m.glb))
            {
                if (gm.name != name) continue;
                var P = new Vector2[gm.pos.Length]; for (int i = 0; i < P.Length; i++) P[i] = GamePos(m, gm.pos[i]);
                for (int t = 0; t + 2 < gm.idx.Length; t += 3) tr.Add(P[gm.idx[t]], P[gm.idx[t + 1]], P[gm.idx[t + 2]]);
            }
            return asphalt[key] = tr;
        }

        /// game point (x, z) on mesh `name` of model m
        public static bool Hit(ModelJ m, string name, double x, double z) => Mesh2D(m, name).Hit(x, z);

        /// game point (x, z) on any model's asphalt (checks)
        public static bool OnAsphalt(List<ModelJ> models, double x, double z)
        {
            foreach (var m in models) if (Math.Abs(x - m.x) < m.half + 25 && Math.Abs(z - m.z) < m.half + 25 && Asphalt(m).Hit(x, z)) return true;
            return false;
        }

        /// the model that owns road polyline p
        public static ModelJ Owner(List<ModelJ> models, List<double[]> p, out int road)
        {
            foreach (var m in models) for (int r = 0; r < m.roads.Count; r++) if (m.roads[r].p == p) { road = r; return m; }
            road = -1; return null;
        }

        /// lane profile of road r of model m (null: n ≤ 2, the json's w / n stand)
        public static Profile Get(ModelJ m, int r)
        {
            var rd = m.roads[r];
            if (rd.n < 3 || rd.lvl != 0) return null;
            if (cache.TryGetValue(rd.p, out var pr)) return pr;
            var tr = Asphalt(m);
            var P = rd.p; double w = rd.w; int n = rd.n; double lw = w / n;
            var seg = new List<(double s0, double L, double tx, double tz)>(); double len = 0;
            for (int i = 0; i + 1 < P.Count; i++)
            {
                double dx = P[i + 1][0] - P[i][0], dz = P[i + 1][1] - P[i][1], L = Math.Sqrt(dx * dx + dz * dz);
                seg.Add((len, L, L > 1e-9 ? dx / L : 0, L > 1e-9 ? dz / L : 0)); len += L;
            }
            int cnt = (int)Math.Floor(len) + 1;
            var cov = new double[cnt, n]; var lo = new double[cnt, n]; var hi = new double[cnt, n];
            const double STEP = .1;
            for (int si = 0; si < cnt; si++)
            {
                double s = Math.Min(si, len); int q = 0; while (q + 1 < seg.Count && seg[q].s0 + seg[q].L < s) q++;
                var sg = seg[q]; if (sg.L < 1e-9) continue;
                double t = s - sg.s0, cx = P[q][0] + sg.tx * t, cz = P[q][1] + sg.tz * t, rx = -sg.tz, rz = sg.tx; // game frame: right of travel
                for (int k = 0; k < n; k++)
                {
                    double c = w / 2 - (k + .5) * lw, a = c - lw / 2;
                    // longest asphalt run inside the band
                    int steps = (int)Math.Round(lw / STEP); double runA = 0, best = 0, bA = c, bB = c; bool inRun = false;
                    for (int j = 0; j <= steps; j++)
                    {
                        double u = a + j * STEP; bool hit = tr.Hit(cx + rx * u, cz + rz * u);
                        if (hit && !inRun) { inRun = true; runA = u; }
                        if ((!hit || j == steps) && inRun) { inRun = false; double b = hit ? u : u - STEP; if (b - runA > best) { best = b - runA; bA = runA; bB = b; } }
                    }
                    cov[si, k] = best; lo[si, k] = bA; hi[si, k] = bB;
                }
            }
            // a lane exists where its band is asphalt over LANE_MIN — median over ±2 m (a sliver / seam in the mesh is no lane end)
            var ex = new bool[cnt, n];
            for (int si = 0; si < cnt; si++)
                for (int k = 0; k < n; k++)
                {
                    var v = new List<double>(); for (int d = -2; d <= 2; d++) v.Add(cov[Math.Max(0, Math.Min(cnt - 1, si + d)), k]);
                    v.Sort(); ex[si, k] = v[2] >= Math.Min(LANE_MIN, lw * .8);
                }
            pr = new Profile { len = len, off = new float[cnt][], uL = new float[cnt], uR = new float[cnt] };
            // the road's ends sit on the model's edge (the next model's asphalt is not in this one): sections with fewer than 2 lanes
            // take the nearest section that has them
            var good = new bool[cnt];
            for (int si = 0; si < cnt; si++) { int e = 0; for (int k = 0; k < n; k++) if (ex[si, k]) e++; good[si] = e >= 2; }
            for (int si = 0; si < cnt; si++)
            {
                int src = si;
                if (!good[si]) { src = -1; for (int d = 1; d < cnt && src < 0; d++) { if (si - d >= 0 && good[si - d]) src = si - d; else if (si + d < cnt && good[si + d]) src = si + d; } }
                var o = new List<float>(); float l0 = 1e9f, r0 = -1e9f;
                for (int k = 0; k < n; k++)
                {
                    double c = w / 2 - (k + .5) * lw;
                    bool e = src >= 0 ? ex[src, k] : true;
                    if (!e) continue;
                    // in part on asphalt: the middle of that part (full lanes keep the json's centre)
                    double cc = src >= 0 && cov[src, k] < lw - .25 ? (lo[src, k] + hi[src, k]) / 2 : c;
                    double ha = src >= 0 && cov[src, k] < lw - .25 ? cov[src, k] / 2 : lw / 2;
                    o.Add((float)cc); l0 = Mathf.Min(l0, (float)(cc - ha)); r0 = Mathf.Max(r0, (float)(cc + ha));
                }
                pr.off[si] = o.ToArray(); pr.uL[si] = l0; pr.uR[si] = r0;
            }
            return cache[rd.p] = pr;
        }

        /// arc length s and lateral u (right of travel) of game point (x, z) on polyline p
        public static void Project(List<double[]> p, double x, double z, out double s, out double u)
        {
            double s0 = 0, bd = double.MaxValue; s = 0; u = 0;
            for (int i = 0; i + 1 < p.Count; i++)
            {
                double dx = p[i + 1][0] - p[i][0], dz = p[i + 1][1] - p[i][1], L = Math.Sqrt(dx * dx + dz * dz); if (L < 1e-9) continue;
                double tx = dx / L, tz = dz / L, t = Math.Max(0, Math.Min(L, (x - p[i][0]) * tx + (z - p[i][1]) * tz));
                double px = p[i][0] + tx * t - x, pz = p[i][1] + tz * t - z, d = px * px + pz * pz;
                if (d < bd) { bd = d; s = s0 + t; u = (x - p[i][0]) * -tz + (z - p[i][1]) * tx; }
                s0 += L;
            }
        }

        /// one line per profiled road: where its lane count differs from the json's n (checks / the report)
        public static string Report(List<ModelJ> models)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var m in models)
                for (int r = 0; r < m.roads.Count; r++)
                {
                    var pr = Get(m, r); if (pr == null) continue;
                    int n = m.roads[r].n; sb.Append($"{m.key} road{r} w={m.roads[r].w:F1} n={n} L={pr.len:F0}:");
                    int prev = -1, from = 0;
                    for (int si = 0; si <= pr.off.Length; si++)
                    {
                        int c = si < pr.off.Length ? pr.off[si].Length : -2;
                        if (c != prev) { if (prev >= 0) sb.Append($" [{from}–{si - 1} m: {prev} lanes, {pr.uL[from]:F1}…{pr.uR[from]:F1}]"); prev = c; from = si; }
                    }
                    sb.AppendLine();
                }
            return sb.ToString();
        }
    }
}
