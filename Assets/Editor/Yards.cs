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
            models = m; rects = packageRects; gates.Clear(); green.Clear(); mly = null; gch = null;
            nRuns = new int[7]; metres = new double[7]; nEvidOrtho = nEvidMly = nEvidGc = nNoGreen = 0;
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

        /// a footprint that is not in the tiles (BuildingsFromTiles: the orthophoto's added houses), after Begin
        public static void AddFoot(List<V> P)
        {
            int id = foot.Count; foot.Add(P);
            double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
            foreach (var v in P) { x0 = Math.Min(x0, v.x); x1 = Math.Max(x1, v.x); z0 = Math.Min(z0, v.z); z1 = Math.Max(z1, v.z); }
            for (int gx = (int)Math.Floor((x0 - 8) / CELL); gx <= (int)Math.Floor((x1 + 8) / CELL); gx++)
                for (int gz = (int)Math.Floor((z0 - 8) / CELL); gz <= (int)Math.Floor((z1 + 8) / CELL); gz++)
                { if (!footGrid.TryGetValue((gx, gz), out var l)) footGrid[(gx, gz)] = l = new List<int>(); l.Add(id); }
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
            // runs split where the street side ends: the street front and the rest each get their own boundary
            var parts = new List<List<int>>();
            foreach (var run in runs)
            {
                List<int> cur = null;
                for (int k = 0; k < run.Count; k++)
                {
                    if (cur == null || (k > 0 && S[run[k]].street != S[run[k - 1]].street && cur.Count >= 3 && run.Count - k >= 3)) { if (cur != null) cur.Add(run[k]); parts.Add(cur = new List<int>()); }
                    cur.Add(run[k]);
                }
            }
            // the house's two boundary types (street / back), then each run's evidence: a green strip on the orthophoto along it,
            // a Mapillary hedge by it, the ground build's hedge strip (bushes there already: a wire fence in front of them)
            var rt = new System.Random(seed * 31 + 7);
            bool coast = baseY < 10;
            int streetType = Pick(rt.NextDouble(), coast, true), backType = Pick(rt.NextDouble(), coast, false);
            bool galv = rt.NextDouble() < .4; float baseH = .3f + .3f * (float)rt.NextDouble(), lowH = .6f + .3f * (float)rt.NextDouble();
            float hedgeH = 1.2f + 1f * (float)rt.NextDouble(), hedgeW = .6f + .35f * (float)rt.NextDouble();
            var tint = Color.HSVToRGB(.22f + .06f * (float)rt.NextDouble(), .12f * (float)rt.NextDouble(), .85f + .25f * (float)rt.NextDouble());
            double evid = rt.NextDouble();
            int emitted = 0;
            foreach (var run in parts)
            {
                if (run.Count < 3) continue;
                bool street = 0 < run.FindAll(q => S[q].street).Count * 2 / run.Count;
                int type = street ? streetType : backType;
                // evidence along the run
                // (a hedge: green along the line on the photo, clearly more than 3 m deeper in the yard: a strip, not a lawn or a
                // garden; 22 % of the house sides; elsewhere no hedge where the line has hardly any green)
                int nMly = 0, nGc = 0; var ev = new List<(double, double, double, double)>();
                foreach (int q in run)
                {
                    ev.Add((S[q].x, S[q].z, S[q].nx, S[q].nz));
                    if (NearMlyHedge(S[q].x, S[q].z, 3.5)) nMly++;
                    if (NearGcHedge(S[q].x, S[q].z, 1.5)) nGc++;
                }
                var (f, fin, _) = Evidence(ev);
                bool hedgeType = type == THedge || type == TFenceHedge;
                if (nGc * 3 >= run.Count) { type = TFence; nEvidGc++; }
                else if ((f >= .5 && f - fin >= .25) || nMly * 4 >= run.Count)
                {
                    if (!hedgeType) type = street ? (evid < .5 ? THedge : TFenceHedge) : (evid < .7 ? THedge : TFenceHedge);
                    if (nMly * 4 >= run.Count) nEvidMly++; else nEvidOrtho++;
                }
                else if (f < .2 && hedgeType) { type = type == THedge ? TFence : (street ? TLowFence : TFence); nNoGreen++; }
                // one segment per straight run (the samples are 1 m apart): drop the points on the line within 4 cm and 12 cm
                // of the ground (~8 x fewer faces)
                var raw = new List<Vector3>(); foreach (int q in run) raw.Add(W.U(S[q].x, ground[q], S[q].z));
                var pts = new List<Vector3> { raw[0] };
                for (int k = 1; k + 1 < raw.Count; k++)
                {
                    Vector3 a = pts[pts.Count - 1], c = raw[k + 1], p = raw[k];
                    Vector3 ac = c - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ac) / Mathf.Max(1e-4f, ac.sqrMagnitude));
                    Vector3 on = a + ac * t; var dxz = new Vector2(p.x - on.x, p.z - on.z);
                    if (dxz.magnitude > .04f || Mathf.Abs(p.y - on.y) > .12f) pts.Add(p);
                }
                pts.Add(raw[raw.Count - 1]);
                bool pa = post[run[0]], pb = post[run[run.Count - 1]];
                // the inside of the yard: the side (-e.z, 0, e.x) of the run's direction, or the other one
                float side = 0;
                for (int k = 0; k + 1 < run.Count; k++)
                {
                    Vector3 e = raw[k + 1] - raw[k]; var o = W.U(S[run[k]].nx, 0, S[run[k]].nz) - W.U(0, 0, 0);
                    side -= -e.z * o.x + e.x * o.z;
                }
                side = side >= 0 ? 1 : -1;
                for (int lod = 0; lod < 3; lod++)
                {
                    var g = lod == 0 ? near : lod == 1 ? mid : hull; if (g == null) continue;
                    HouseBuilder.Lod = lod; bool piers = lod < 2;
                    switch (type)
                    {
                        case THedge:
                            HouseBuilder.YardHedge(g, pts, side, -.1f, hedgeW, hedgeH, tint);
                            break;
                        case TFence:
                        case TFenceHedge:
                            HouseBuilder.YardWall(g, pts, baseH, 3, piers && pa, piers && pb, baseH + 1.2f);
                            HouseBuilder.YardFence(g, pts, baseH, baseH + (type == TFence ? 1.2f : 1.1f), galv);
                            if (type == TFenceHedge) HouseBuilder.YardHedge(g, pts, side, .25f, hedgeW, Mathf.Max(hedgeH, baseH + 1.3f), tint);
                            break;
                        case TLowFence:
                            HouseBuilder.YardWall(g, pts, lowH, 1, piers && pa, piers && pb, lowH + 1f);
                            HouseBuilder.YardFence(g, pts, lowH + .07f, lowH + 1.07f, galv);
                            break;
                        default:
                            HouseBuilder.YardWall(g, pts, h, kind, piers && pa, piers && pb);
                            break;
                    }
                }
                HouseBuilder.Lod = 0; emitted++;
                float len = 0; for (int k = 0; k + 1 < pts.Count; k++) len += Vector3.Distance(pts[k], pts[k + 1]);
                int ti = type == TWall ? kind : type; nRuns[ti]++; metres[ti] += len;
            }
            return emitted > 0;
        }

        // ------------------------------------------------------------------ boundary types and their evidence
        public const int TWall = -1, THedge = 3, TFence = 4, TLowFence = 5, TFenceHedge = 6;
        public static readonly string[] TypeNames = { "kamen (suhozid)", "zid + kapa", "nizak zid + ograda", "zivica", "zica na podnozju", "nizak zid + zica", "zica + zivica" };
        public static int[] nRuns = new int[7]; public static double[] metres = new double[7];
        public static int nEvidOrtho, nEvidMly, nEvidGc, nNoGreen;
        /// a house's boundary type before the evidence (which turns ~22 % of the runs into hedges and takes hedges off runs without
        /// green): together Kaštela's mix (walls ~35 %, wire fences ~30 %, hedges ~20 %, fence + hedge ~15 %), more walls along the
        /// street, more stone walls by the sea (the old cores' belt), more wire and hedge behind
        static int Pick(double r, bool coast, bool street)
        {
            (int t, double p)[] w = street
                ? (coast ? new[] { (TWall, .66), (TLowFence, .12), (TFence, .08), (THedge, .07), (TFenceHedge, .07) }
                         : new[] { (TWall, .48), (TLowFence, .18), (TFence, .12), (THedge, .11), (TFenceHedge, .11) })
                : (coast ? new[] { (TWall, .5), (TFence, .26), (THedge, .12), (TFenceHedge, .08), (TLowFence, .04) }
                         : new[] { (TWall, .36), (TFence, .32), (THedge, .16), (TFenceHedge, .12), (TLowFence, .04) });
            double acc = 0; foreach (var e in w) { acc += e.p; if (r < acc) return e.t; }
            return w[w.Length - 1].t;
        }

        // the orthophoto's green per 0.5 m pixel (OrthoGround's mosaics; excess green, not deep shadow), one byte per pixel per tile
        static readonly Dictionary<(int, int), byte[]> green = new Dictionary<(int, int), byte[]>();
        const int GN = 2048;
        static byte[] GreenTile(int tx, int tz)
        {
            if (green.TryGetValue((tx, tz), out var b)) return b;
            string src = OrthoGround.MosaicPath(tx, tz); b = null;
            if (File.Exists(src))
            {
                var img = new Texture2D(2, 2); img.LoadImage(File.ReadAllBytes(src));
                if (img.width == GN && img.height == GN)
                {
                    var px = img.GetPixels32(); b = new byte[GN * GN];
                    for (int i = 0; i < px.Length; i++)
                    {
                        float R0 = px[i].r / 255f, G0 = px[i].g / 255f, B0 = px[i].b / 255f;
                        float exg = (2 * G0 - R0 - B0) / (R0 + G0 + B0 + 1e-3f), lum = .2126f * R0 + .7152f * G0 + .0722f * B0;
                        b[i] = (byte)(255 * Mathf.Clamp01((exg - .01f) / .09f) * Mathf.Clamp01((lum - .06f) / .1f));
                    }
                }
                UnityEngine.Object.DestroyImmediate(img);
            }
            green[(tx, tz)] = b; return b;
        }
        static float GreenAt(double gx, double gz)
        {
            int tx = (int)Math.Floor(gx / 1024), tz = (int)Math.Floor(gz / 1024); var b = GreenTile(tx, tz); if (b == null) return 0;
            int i = (int)Math.Floor((gx - tx * 1024) / .5), r = (int)Math.Floor(((tz + 1) * 1024 - gz) / .5);
            return i >= 0 && r >= 0 && i < GN && r < GN ? b[r * GN + i] / 255f : 0;
        }
        /// a hedge-like green on the photo at a boundary sample: on the line or up to 1 m inside it (the yard side), not only one pixel
        static bool Green(double x, double z, double nx, double nz)
        {
            int n = 0;
            foreach (double o in new[] { .3, 0, -.5, -1.0 }) if (GreenAt(x + nx * o, z + nz * o) > .5f) n++;
            return n >= 2;
        }

        /// a side's green evidence: fraction of samples green on the line (f), deeper in the yard at 3 m (fin) and outside it at 1.5 m (fout)
        static (double f, double fin, double fout) Evidence(List<(double x, double z, double nx, double nz)> s)
        {
            int a = 0, b = 0, c = 0;
            foreach (var q in s)
            {
                if (Green(q.x, q.z, q.nx, q.nz)) a++;
                if (GreenAt(q.x - q.nx * 3, q.z - q.nz * 3) > .5f) b++;
                if (GreenAt(q.x + q.nx * 1.5, q.z + q.nz * 1.5) > .5f) c++;
            }
            double n = Math.Max(1, s.Count); return (a / n, b / n, c / n);
        }
        /// debug: the evidence over every tile footprint's ring (rectangle + 3 m, one sample per metre, per side): histogram lines
        public static string EvidenceStats(double fMin, double stripMin)
        {
            int sides = 0, strong = 0, f45 = 0; var hist = new int[11];
            for (int tz = WorldBuilder.TZ0; tz <= WorldBuilder.TZ1; tz++)
                for (int tx = WorldBuilder.TX0; tx <= WorldBuilder.TX1; tx++)
                {
                    var tile = Tiles.Get(tx, tz); if (tile?.buildingsJson == null) continue;
                    int k = 0;
                    foreach (var bo in MiniJson.L(MiniJson.Parse(tile.buildingsJson)))
                    {
                        if (k++ % 4 != 0) continue;
                        var pts = MiniJson.L(MiniJson.O(bo)["pts"]); if (pts.Count < 3) continue;
                        double x0 = 1e9, x1 = -1e9, z0 = 1e9, z1 = -1e9;
                        foreach (var p in pts) { var q = MiniJson.L(p); double x = MiniJson.D(q[0]), z = MiniJson.D(q[1]); x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z); }
                        if ((x1 - x0) * (z1 - z0) > 600) continue;
                        x0 -= 3; x1 += 3; z0 -= 3; z1 += 3;
                        for (int sd = 0; sd < 4; sd++)
                        {
                            var l = new List<(double, double, double, double)>();
                            for (double t = 0; t < 1; t += 1.0 / Math.Max(3, (sd % 2 == 0 ? x1 - x0 : z1 - z0)))
                            {
                                if (sd == 0) l.Add((x0 + (x1 - x0) * t, z0, 0, -1)); else if (sd == 1) l.Add((x1, z0 + (z1 - z0) * t, 1, 0));
                                else if (sd == 2) l.Add((x0 + (x1 - x0) * t, z1, 0, 1)); else l.Add((x0, z0 + (z1 - z0) * t, -1, 0));
                            }
                            var e = Evidence(l); sides++; hist[(int)Math.Round(e.f * 10)]++;
                            if (e.f >= .45) f45++;
                            if (e.f >= fMin && e.f - e.fin >= stripMin) strong++;
                        }
                    }
                }
            return $"sides {sides}: f>=.45 {100.0 * f45 / sides:F0} %, strong (f>={fMin}, strip>={stripMin}) {100.0 * strong / sides:F0} %; f hist {string.Join(",", hist)}";
        }

        // the Mapillary hedges (MapillaryTrees' data: type hedge) and the ground build's hedge strips (GroundCover.Hedges, drawn as
        // bushes by VegetationMask.PlaceHedges), on a 32 m grid
        static Dictionary<(int, int), List<(double x, double z)>> mly;
        static Dictionary<(int, int), List<double[]>> gch;
        static void LoadEvidence()
        {
            mly = new Dictionary<(int, int), List<(double, double)>>(); gch = new Dictionary<(int, int), List<double[]>>();
            if (File.Exists(MapillaryTrees.PATH))
                foreach (var o in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(MapillaryTrees.PATH)))["trees"]))
                {
                    var d = MiniJson.O(o); if ((string)d["type"] != "hedge") continue;
                    double x = MiniJson.D(d["gx"]), z = MiniJson.D(d["gz"]);
                    if (!mly.TryGetValue(Key(x, z), out var l)) mly[Key(x, z)] = l = new List<(double, double)>(); l.Add((x, z));
                }
            GroundCover.Pitches.GetType(); // loads tlo-kastela.json (its hedges)
            foreach (var h in GroundCover.Hedges)
            {
                if (h.Length < 4) continue;
                for (int gx = (int)Math.Floor((Math.Min(h[0], h[2]) - 2) / CELL); gx <= (int)Math.Floor((Math.Max(h[0], h[2]) + 2) / CELL); gx++)
                    for (int gz = (int)Math.Floor((Math.Min(h[1], h[3]) - 2) / CELL); gz <= (int)Math.Floor((Math.Max(h[1], h[3]) + 2) / CELL); gz++)
                    { if (!gch.TryGetValue((gx, gz), out var l)) gch[(gx, gz)] = l = new List<double[]>(); l.Add(h); }
            }
        }
        static bool NearMlyHedge(double x, double z, double r)
        {
            if (mly == null) LoadEvidence();
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                    if (mly.TryGetValue((Key(x, z).Item1 + dx, Key(x, z).Item2 + dz), out var l))
                        foreach (var p in l) if ((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < r * r) return true;
            return false;
        }
        static bool NearGcHedge(double x, double z, double r)
        {
            if (gch == null) LoadEvidence();
            if (gch.TryGetValue(Key(x, z), out var l)) foreach (var h in l) if (SegDist(x, z, h[0], h[1], h[2], h[3]) < r) return true;
            return false;
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
            var tb = new StringBuilder("[Magistrala] yard boundaries (runs / m):");
            double all = 0; foreach (var m in metres) all += m;
            for (int i = 0; i < 7; i++) tb.Append($" {TypeNames[i]} {nRuns[i]} / {metres[i]:F0} m ({100 * metres[i] / Math.Max(1, all):F0} %);");
            tb.Append($" evidence: orthophoto green {nEvidOrtho} runs, Mapillary hedge {nEvidMly}, ground-build hedge strip (fence) {nEvidGc}, hedge dropped (no green) {nNoGreen}");
            Debug.Log(tb.ToString());
        }
    }
}
