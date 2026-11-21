// Yard walls of the detailed houses (all of Kaštela since kuce2; the probe area BuildingsFromTiles.Probe first had them): the house's oriented footprint rectangle grown
// by 2-4.5 m, sampled every metre; a sample is pulled back towards the house to keep 1.6 m off a carriageway (that run is the
// street wall and gets a 3 m gate with two piers), and dropped when it is nearer another building than its own house, inside /
// next to another building, on a model's ground, a package building or a stop's bay, or on a slope. Walls go into the house's
// chunk mesh (Magistrala/Facade: rubble layer, the house's own wall with a concrete coping, or a low wall with a railing), so
// they add no draw calls. Gates and front doors go to yard-spots.json for PropsBuilder (flower pots).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Magistrala.Game;
using UnityEngine;
using V = Magistrala.EditorTools.BuildingsFromTiles.Vector2d;

namespace Magistrala.EditorTools
{
    public static class Yards
    {
        public const string SpotsPath = "Assets/Generated/Props/yard-spots.json";
        const double ROAD_CLEAR = 1.6, CELL = 32;

        static List<ModelJ> models; static List<double[]> rects;
        static List<(double ax, double az, double bx, double bz, double hw)> roads;
        static Dictionary<(int, int), List<int>> roadGrid;
        static List<List<V>> foot; static Dictionary<(int, int), List<int>> footGrid;
        static List<(Vector3 c, Vector2 dir, float hl, float hw)> bays;
        static readonly List<(Vector3 p, Vector3 n)> gates = new List<(Vector3, Vector3)>();

        static (int, int) Key(double x, double z) => ((int)Math.Floor(x / CELL), (int)Math.Floor(z / CELL));

        /// all footprints (all tiles), carriageways and bays; call once before the houses
        public static void Begin(List<ModelJ> m, List<double[]> packageRects)
        {
            models = m; rects = packageRects; gates.Clear();
            roads = StreetsBuilder.Carriageways(); roadGrid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < roads.Count; i++)
            {
                var r = roads[i]; double pad = r.hw + 6;
                for (int gx = (int)Math.Floor((Math.Min(r.ax, r.bx) - pad) / CELL); gx <= (int)Math.Floor((Math.Max(r.ax, r.bx) + pad) / CELL); gx++)
                    for (int gz = (int)Math.Floor((Math.Min(r.az, r.bz) - pad) / CELL); gz <= (int)Math.Floor((Math.Max(r.az, r.bz) + pad) / CELL); gz++)
                    { if (!roadGrid.TryGetValue((gx, gz), out var l)) roadGrid[(gx, gz)] = l = new List<int>(); l.Add(i); }
            }
            foot = new List<List<V>>(); footGrid = new Dictionary<(int, int), List<int>>();
            for (int tz = WorldBuilder.TZ0; tz <= WorldBuilder.TZ1; tz++)
                for (int tx = WorldBuilder.TX0; tx <= WorldBuilder.TX1; tx++)
                {
                    var tile = Tiles.Get(tx, tz); if (tile?.buildingsJson == null) continue;
                    foreach (var bo in MiniJson.L(MiniJson.Parse(tile.buildingsJson)))
                    {
                        var pts = MiniJson.L(MiniJson.O(bo)["pts"]); if (pts.Count < 3) continue;
                        var P = new List<V>(); double cx = 0, cz = 0;
                        foreach (var p in pts) { var q = MiniJson.L(p); var v = new V(MiniJson.D(q[0]), MiniJson.D(q[1])); P.Add(v); cx += v.x; cz += v.z; }
                        cx /= P.Count; cz /= P.Count;
                        int id = foot.Count; foot.Add(P);
                        double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
                        foreach (var v in P) { x0 = Math.Min(x0, v.x); x1 = Math.Max(x1, v.x); z0 = Math.Min(z0, v.z); z1 = Math.Max(z1, v.z); }
                        for (int gx = (int)Math.Floor((x0 - 8) / CELL); gx <= (int)Math.Floor((x1 + 8) / CELL); gx++)
                            for (int gz = (int)Math.Floor((z0 - 8) / CELL); gz <= (int)Math.Floor((z1 + 8) / CELL); gz++)
                            { if (!footGrid.TryGetValue((gx, gz), out var l)) footGrid[(gx, gz)] = l = new List<int>(); l.Add(id); }
                    }
                }
            bays = new List<(Vector3, Vector2, float, float)>();
            var game = Resources.Load<GameWorld>(GameWorld.ResourcePath);
            if (game != null) foreach (var s in game.stops) bays.Add((s.bay, s.bayDir, s.bayHl, s.bayHw));
        }

        static double SegDist(double px, double pz, double ax, double az, double bx, double bz)
        {
            double dx = bx - ax, dz = bz - az, L = dx * dx + dz * dz;
            double t = L < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * dx + (pz - az) * dz) / L));
            double qx = ax + t * dx - px, qz = az + t * dz - pz; return Math.Sqrt(qx * qx + qz * qz);
        }
        static double RoadClear(double x, double z)
        {
            double best = 1e9;
            if (roadGrid.TryGetValue(Key(x, z), out var l)) foreach (int i in l) { var r = roads[i]; best = Math.Min(best, SegDist(x, z, r.ax, r.az, r.bx, r.bz) - r.hw); }
            return best;
        }
        /// metres from (x, z) to the nearest carriageway edge (negative: on it) and that carriageway's half width
        public static (double clear, double hw) Clear(double x, double z)
        {
            double best = 1e9, hw = 0;
            if (roadGrid != null && roadGrid.TryGetValue(Key(x, z), out var l))
                foreach (int i in l) { var r = roads[i]; double d = SegDist(x, z, r.ax, r.az, r.bx, r.bz) - r.hw; if (d < best) { best = d; hw = r.hw; } }
            return (best, hw);
        }
        static double PolyDist(List<V> P, double x, double z)
        {
            if (BuildingsFromTiles.InPoly(P, x, z)) return 0;
            double best = 1e9;
            for (int i = 0; i < P.Count; i++) { var a = P[i]; var b = P[(i + 1) % P.Count]; best = Math.Min(best, SegDist(x, z, a.x, a.z, b.x, b.z)); }
            return best;
        }
        static bool OnBay(double x, double z)
        {
            var u = W.U(x, 0, z);
            foreach (var b in bays)
            {
                float dx = u.x - b.c.x, dz = u.z - b.c.z; if (dx * dx + dz * dz > 2500) continue;
                float along = dx * b.dir.x + dz * b.dir.y, across = -dx * b.dir.y + dz * b.dir.x;
                if (Math.Abs(along) < b.hl + 4 && Math.Abs(across) < b.hw + 3) return true;
            }
            return false;
        }

        /// yard wall of one house (P: its game footprint, already emitted by HouseBuilder.AddHouse): near / mid levels and the
        /// shadow / collision hull (level 2: no railings, no piers)
        public static bool Build(HouseGeo near, HouseGeo mid, HouseGeo hull, List<V> P, double baseY, int seed)
        {
            var rnd = new System.Random(seed * 13 + 5);
            double d = 2.2 + 2.3 * rnd.NextDouble(); int kind = rnd.NextDouble() < .35 ? 0 : rnd.NextDouble() < .7 ? 1 : 2;
            float h = 1.1f + .5f * (float)rnd.NextDouble();
            // minimum-area rectangle (one candidate per edge)
            double best = double.MaxValue, ux = 1, uz = 0, u0 = 0, u1 = 0, v0 = 0, v1 = 0;
            for (int i = 0; i < P.Count; i++)
            {
                double ex = P[(i + 1) % P.Count].x - P[i].x, ez = P[(i + 1) % P.Count].z - P[i].z, L = Math.Sqrt(ex * ex + ez * ez); if (L < .5) continue;
                ex /= L; ez /= L;
                double a0 = 1e9, a1 = -1e9, b0 = 1e9, b1 = -1e9;
                foreach (var p in P) { double a = p.x * ex + p.z * ez, b = -p.x * ez + p.z * ex; a0 = Math.Min(a0, a); a1 = Math.Max(a1, a); b0 = Math.Min(b0, b); b1 = Math.Max(b1, b); }
                double ar = (a1 - a0) * (b1 - b0); if (ar >= best) continue;
                best = ar; ux = ex; uz = ez; u0 = a0; u1 = a1; v0 = b0; v1 = b1;
            }
            if (best > 600) return false;
            (double x, double z) At(double a, double b) => (a * ux - b * uz, a * uz + b * ux);
            var corners = new[] { (u0 - d, v0 - d), (u1 + d, v0 - d), (u1 + d, v1 + d), (u0 - d, v1 + d) };
            var own = P;
            // the ring of samples: (game x, z, outward normal, valid, street)
            var S = new List<(double x, double z, double nx, double nz, bool ok, bool street)>();
            for (int sd = 0; sd < 4; sd++)
            {
                var A = corners[sd]; var B = corners[(sd + 1) % 4];
                var pa = At(A.Item1, A.Item2); var pb = At(B.Item1, B.Item2);
                double len = Math.Sqrt((pb.x - pa.x) * (pb.x - pa.x) + (pb.z - pa.z) * (pb.z - pa.z));
                int n = Math.Max(1, (int)Math.Round(len));
                // outward: away from the rectangle's centre
                var c = At((u0 + u1) / 2, (v0 + v1) / 2);
                double mx = (pa.x + pb.x) / 2 - c.x, mz = (pa.z + pb.z) / 2 - c.z, ml = Math.Sqrt(mx * mx + mz * mz); mx /= ml; mz /= ml;
                for (int k = 0; k < n; k++)
                {
                    double t = (double)k / n, x = pa.x + (pb.x - pa.x) * t, z = pa.z + (pb.z - pa.z) * t;
                    bool ok = true, street = false;
                    double r = RoadClear(x, z);
                    if (r < ROAD_CLEAR)
                    {
                        double shift = ROAD_CLEAR - r + .1;
                        if (shift > d - 1.2) ok = false;
                        else { x -= mx * shift; z -= mz * shift; street = true; if (RoadClear(x, z) < ROAD_CLEAR - .1) ok = false; }
                    }
                    if (ok)
                    {
                        double mine = PolyDist(own, x, z);
                        if (footGrid.TryGetValue(Key(x, z), out var l))
                            foreach (int id in l)
                            {
                                var F = foot[id]; if (F.Count == own.Count && Math.Abs(F[0].x - own[0].x) < 1e-6 && Math.Abs(F[0].z - own[0].z) < 1e-6) continue;
                                double o = PolyDist(F, x, z); if (o < 1.2 || o < mine) { ok = false; break; }
                            }
                    }
                    if (ok && (WorldData.FootDist(models, x, z) < 2.5 || OnBay(x, z))) ok = false;
                    if (ok) foreach (var rc in rects) { double dx = x - rc[0], dz = z - rc[1]; if (dx * dx + dz * dz > 2500) continue; double ca = Math.Cos(rc[2]), sa = Math.Sin(rc[2]); double uu = dx * ca + dz * sa, vv = -dx * sa + dz * ca, m = Math.Max(rc[3], rc[4]) / 2 + 2; if (Math.Abs(uu) < m && Math.Abs(vv) < m) { ok = false; break; } }
                    S.Add((x, z, mx, mz, ok, street));
                }
            }
            int N = S.Count; if (N < 8) return false;
            var okA = new bool[N]; for (int i = 0; i < N; i++) okA[i] = S[i].ok;
            var ground = new float[N];
            for (int i = 0; i < N; i++) if (okA[i]) { ground[i] = (float)BuildingsFromTiles.Ground(models, S[i].x, S[i].z); if (Math.Abs(ground[i] - baseY) > 3) okA[i] = false; }
            // the gate: the middle of the longest street run (>= 6 samples), 3 samples taken out
            var post = new bool[N]; int bs = -1, bl = 0;
            for (int i = 0; i < N; i++)
            {
                if (!okA[i] || !S[i].street || (i > 0 && okA[i - 1] && S[i - 1].street)) continue;
                int j = i; while (j + 1 < N && okA[j + 1] && S[j + 1].street) j++;
                if (j - i + 1 > bl) { bl = j - i + 1; bs = i; }
            }
            if (bl >= 6)
            {
                int g0 = bs + bl / 2 - 1;
                for (int k = 0; k < 3; k++) okA[g0 + k] = false;
                post[g0 - 1] = post[g0 + 3] = true;
                var a = S[g0 - 1]; var b = S[g0 + 3];
                gates.Add((W.U((a.x + b.x) / 2, (ground[g0 - 1] + ground[g0 + 3]) / 2, (a.z + b.z) / 2), W.U(a.nx, 0, a.nz) - W.U(0, 0, 0)));
            }
            // runs: start at an invalid sample so no run wraps; a whole ring (no gate, no gap) is one closed run
            int start = Array.IndexOf(okA, false); bool whole = start < 0;
            var runs = new List<List<int>>();
            if (whole) { var all = new List<int>(); for (int i = 0; i <= N; i++) all.Add(i % N); runs.Add(all); }
            else
            {
                List<int> cur = null;
                for (int j = 0; j < N; j++)
                {
                    int i = (start + j) % N;
                    if (okA[i]) { if (cur == null) runs.Add(cur = new List<int>()); cur.Add(i); } else cur = null;
                }
            }
            int emitted = 0;
            foreach (var run in runs)
            {
                if (run.Count < 3) continue;
                var raw = new List<Vector3>(); foreach (int q in run) raw.Add(W.U(S[q].x, ground[q], S[q].z));
                // one segment per straight run (the samples are 1 m apart): drop the points on the line within 4 cm and 12 cm
                // of the ground (~8 x fewer faces)
                var pts = new List<Vector3> { raw[0] };
                for (int k = 1; k + 1 < raw.Count; k++)
                {
                    Vector3 a = pts[pts.Count - 1], c = raw[k + 1], p = raw[k];
                    Vector3 ac = c - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ac) / Mathf.Max(1e-4f, ac.sqrMagnitude));
                    Vector3 on = a + ac * t; var dxz = new Vector2(p.x - on.x, p.z - on.z);
                    if (dxz.magnitude > .04f || Mathf.Abs(p.y - on.y) > .12f) pts.Add(p);
                }
                pts.Add(raw[raw.Count - 1]);
                HouseBuilder.Lod = 0; HouseBuilder.YardWall(near, pts, h, kind, post[run[0]], post[run[run.Count - 1]]);
                HouseBuilder.Lod = 1; HouseBuilder.YardWall(mid, pts, h, kind, post[run[0]], post[run[run.Count - 1]]);
                if (hull != null) { HouseBuilder.Lod = 2; HouseBuilder.YardWall(hull, pts, h, kind, false, false); }
                HouseBuilder.Lod = 0; emitted++;
            }
            return emitted > 0;
        }

        /// gates (and the probe's front doors) for PropsBuilder: Unity position at the threshold, outward (street) normal
        public static void WriteSpots()
        {
            var sb = new StringBuilder("{\"gates\":[");
            string F(float v) => v.ToString("F3", CultureInfo.InvariantCulture);
            for (int i = 0; i < gates.Count; i++) sb.Append(i > 0 ? "," : "").Append($"[{F(gates[i].p.x)},{F(gates[i].p.y)},{F(gates[i].p.z)},{F(gates[i].n.x)},{F(gates[i].n.z)}]");
            sb.Append("],\"doors\":["); int nd = 0;
            foreach (var d in HouseBuilder.Doors)
            {
                if (!BuildingsFromTiles.InProbe(d.p.x + W.OX, -d.p.z + W.OZ) && !BuildingsFromTiles.InShowArea(d.p.x + W.OX, -d.p.z + W.OZ)) continue;
                sb.Append(nd++ > 0 ? "," : "").Append($"[{F(d.p.x)},{F(d.p.y)},{F(d.p.z)},{F(d.n.x)},{F(d.n.z)}]");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(SpotsPath));
            File.WriteAllText(SpotsPath, sb.Append("]}").ToString());
            Debug.Log($"[Magistrala] yards: {gates.Count} gates, {nd} front doors in the probe ({SpotsPath})");
        }
    }
}
