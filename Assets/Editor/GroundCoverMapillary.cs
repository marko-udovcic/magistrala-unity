// Mapillary car parks (docs/mapillary-izvlacenje.md, "Parking"): Assets/Editor/parking-mapillary.json is the lots part of
// BalkanDriver tools/world/mapillary/parking.json (s5_parking.py; Mapillary contributors, CC BY-SA 4.0): off-road clusters of
// parked cars seen in the street photos (convex hull of the cars + 2 m, positions good to ~2–3 m).
//   * a lot that overlaps an OSM car park of the ground build (≥ 20 % of its area) is that car park: nothing new
//   * the outline: the hull 0.5 m in, its corners rounded (2 m)
//   * the ground on a 0.5 m grid (MlyPrepare, in GroundCover.Build): asphalt where the terrain is bare (not a building, a wall,
//     a street / footway, a model road, the riva, another surface, a pitch), on any slope, in pieces ≥ 8 m²; our footway /
//     paving is standing room without asphalt (the shop fronts of Štafilić); the bays (flat ground only; nose-in across the
//     long axis, along it in a strip under 4.6 m) packed on the standing room
//   * a lot with fewer than 3 bays is dropped; PropsBuilder.ParkedCars fills the bays, then MlyFinish drops the lots that got
//     under 3 cars (ParkedCars takes them out again) and builds the asphalt of the rest: clipped by the outline (the mesh edge follows it), the edge at a wall / a
//     building by marching squares, draped on the terrain (2 m pieces where they lie on it, else 0.5 m cells), plus a 4 m
//     connector to the nearest of our streets (not the D8) when the gap is under 15 m
//   * the kept lots and connectors go to Assets/Generated/Tlo/mapillary-lots.json: Hard() covers them (no grass detail, no
//     tree there)
// The kerbside segments of the file are empty (the old road has almost no segmented imagery, the D8 has no kerbside parking).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static partial class GroundCover
    {
        public const string MLY = "Assets/Editor/parking-mapillary.json";
        const string MLY_KEEP = OUT + "/mapillary-lots.json";
        public const double SUB = .5;                                    // the ground grid
        public static bool UseMly = true;   // false: the ground build and the parked cars without the Mapillary lots (a comparison run)

        public class MLot
        {
            public int id; public string road; public double[] ring; public double cx, cz, ux, uz, area; public int seen, seqs;
            public bool osm;                                              // an OSM car park is there
            public bool keep;                                             // built (≥ 3 bays, a car parked)
            public double u0, u1, v0, v1;                                 // extent of the outline in the lot frame (u along, v across)
            // the ground grid: nodes every SUB m from (gu, gv); kind 0 asphalt ground, 1 standing room without asphalt, 2 no
            public double gu, gv; public int nu, nv; public byte[] kind; public float[] y;
            public bool[] cut, asph, stand, wall;                         // cells (nu - 1) x (nv - 1): touched by the outline, asphalt, standing room, a wall / fence across
            public List<(double u, double v)> bays = new List<(double, double)>(); public bool along; public int nCars;
            public (double u, double v) Local(double gx, double gz) { double dx = gx - cx, dz = gz - cz; return (dx * ux + dz * uz, -dx * uz + dz * ux); }
            public (double x, double z) World(double u, double v) => (cx + ux * u - uz * v, cz + uz * u + ux * v);
            public bool Stand(double u, double v)
            {
                int i = (int)Math.Floor((u - gu) / SUB), k = (int)Math.Floor((v - gv) / SUB);
                return i >= 0 && k >= 0 && i < nu - 1 && k < nv - 1 && stand[k * (nu - 1) + i];
            }
            public float Y(double u, double v)
            {
                double fu = (u - gu) / SUB, fv = (v - gv) / SUB; int i = Math.Max(0, Math.Min(nu - 2, (int)Math.Floor(fu))), k = Math.Max(0, Math.Min(nv - 2, (int)Math.Floor(fv)));
                float a = (float)Math.Max(0, Math.Min(1, fu - i)), b = (float)Math.Max(0, Math.Min(1, fv - k));
                float y00 = y[k * nu + i], y10 = y[k * nu + i + 1], y01 = y[(k + 1) * nu + i], y11 = y[(k + 1) * nu + i + 1];
                return Mathf.Lerp(Mathf.Lerp(y00, y10, a), Mathf.Lerp(y01, y11, a), b);
            }
        }
        public static List<MLot> MLots = new List<MLot>();
        static List<double[]> mlyLinks = new List<double[]>();
        public static int nMlyLots, nMlyAsph, nMlyLinks, nMlyFew, nMlyEmpty;

        static void LoadMly()
        {
            MLots = new List<MLot>(); mlyLinks = new List<double[]>();
            if (!UseMly || !File.Exists(MLY)) return;
            var j = MiniJson.O(MiniJson.Parse(File.ReadAllText(MLY)));
            var keep = new HashSet<int>();
            if (File.Exists(MLY_KEEP))
            {
                var kj = MiniJson.O(MiniJson.Parse(File.ReadAllText(MLY_KEEP)));
                foreach (var o in MiniJson.L(kj["keep"])) keep.Add((int)MiniJson.D(o));
                foreach (var o in MiniJson.L(kj["links"])) mlyLinks.Add(MiniJson.L(o).Select(MiniJson.D).ToArray());
            }
            var park = surf.Where(s => s.k == "parking").ToList();
            int id = -1;
            foreach (var o in MiniJson.L(j["lots"]))
            {
                id++;
                var d = MiniJson.O(o);
                var pts = MiniJson.L(d["poly"]).Select(p => { var q = MiniJson.L(p); return (x: MiniJson.D(q[0]), z: MiniJson.D(q[1])); }).ToList();
                var hull = Hull(pts); if (hull.Count < 3) continue;
                var L = new MLot { id = id, road = (string)d["road"], seen = (int)MiniJson.D(d["cars_seen"]), seqs = (int)MiniJson.D(d["seqs"]), keep = keep.Contains(id) };
                L.cx = hull.Average(p => p.x); L.cz = hull.Average(p => p.z);
                // the long axis: the hull edge with the smallest bounding rectangle, its longer side
                double best = double.MaxValue;
                for (int i = 0; i < hull.Count; i++)
                {
                    var a = hull[i]; var b = hull[(i + 1) % hull.Count]; double ex = b.x - a.x, ez = b.z - a.z, el = Math.Sqrt(ex * ex + ez * ez); if (el < .3) continue;
                    ex /= el; ez /= el;
                    double a0 = 1e9, a1 = -1e9, b0 = 1e9, b1 = -1e9;
                    foreach (var p in hull) { double u = p.x * ex + p.z * ez, v = -p.x * ez + p.z * ex; a0 = Math.Min(a0, u); a1 = Math.Max(a1, u); b0 = Math.Min(b0, v); b1 = Math.Max(b1, v); }
                    double ar = (a1 - a0) * (b1 - b0); if (ar >= best) continue; best = ar;
                    if (a1 - a0 >= b1 - b0) { L.ux = ex; L.uz = ez; } else { L.ux = -ez; L.uz = ex; }
                }
                if (best == double.MaxValue) continue;
                // the outline: 0.5 m in, corners rounded with 2 m (inset by 2.5, then out by 2 along arcs)
                var ins = Inset(hull, 2.5);
                var ring = ins.Count >= 3 && Area(ins) > 1 ? Rounded(ins, 2.0) : Inset(hull, .5);
                if (ring.Count < 3) continue;
                L.ring = ring.SelectMany(p => new[] { p.x, p.z }).ToArray();
                L.u0 = L.v0 = 1e9; L.u1 = L.v1 = -1e9;
                foreach (var p in ring) { var (u, v) = L.Local(p.x, p.z); L.u0 = Math.Min(L.u0, u); L.u1 = Math.Max(L.u1, u); L.v0 = Math.Min(L.v0, v); L.v1 = Math.Max(L.v1, v); }
                L.area = Area(ring);
                // an OSM car park there? (share of a 1.5 m sample grid inside the car park triangles)
                int nIn = 0, nOsm = 0;
                for (double u = L.u0; u <= L.u1; u += 1.5)
                    for (double v = L.v0; v <= L.v1; v += 1.5)
                    {
                        var (x, z) = L.World(u, v); if (!InRing(L.ring, x, z)) continue; nIn++;
                        if (InSurf(park, x, z)) nOsm++;
                    }
                L.osm = nIn == 0 || nOsm >= .2 * nIn;
                if (L.osm) L.keep = false;
                MLots.Add(L);
            }
        }

        // ------------------------------------------------------------------ convex geometry
        static double Cross((double x, double z) o, (double x, double z) a, (double x, double z) b) => (a.x - o.x) * (b.z - o.z) - (a.z - o.z) * (b.x - o.x);
        static double Area(List<(double x, double z)> P) { double a = 0; for (int i = 0; i < P.Count; i++) { var p = P[i]; var q = P[(i + 1) % P.Count]; a += p.x * q.z - q.x * p.z; } return Math.Abs(a) / 2; }

        static List<(double x, double z)> Hull(List<(double x, double z)> p)
        {
            var s = p.Distinct().OrderBy(q => q.x).ThenBy(q => q.z).ToList(); if (s.Count < 3) return s;
            var h = new List<(double, double)>();
            foreach (var q in s) { while (h.Count >= 2 && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            int lo = h.Count + 1;
            for (int i = s.Count - 2; i >= 0; i--) { var q = s[i]; while (h.Count >= lo && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            h.RemoveAt(h.Count - 1); return h;                       // counter-clockwise (inside on the left of every edge)
        }

        /// a convex polygon clipped by the half plane left of a -> b (Sutherland–Hodgman)
        static List<(double x, double z)> ClipHalf(List<(double x, double z)> P, (double x, double z) a, (double x, double z) b)
        {
            var R = new List<(double, double)>(); int n = P.Count; if (n == 0) return R;
            for (int i = 0; i < n; i++)
            {
                var c = P[i]; var d = P[(i + 1) % n]; double sc = Cross(a, b, c), sd = Cross(a, b, d);
                if (sc >= 0) R.Add(c);
                if ((sc >= 0) != (sd >= 0)) { double t = sc / (sc - sd); R.Add((c.x + (d.x - c.x) * t, c.z + (d.z - c.z) * t)); }
            }
            return R;
        }
        static List<(double x, double z)> ClipConvex(List<(double x, double z)> P, List<(double x, double z)> by)
        {
            for (int i = 0; i < by.Count && P.Count > 0; i++) P = ClipHalf(P, by[i], by[(i + 1) % by.Count]);
            return P;
        }
        /// a counter-clockwise convex polygon moved d m in (every edge's half plane shifted)
        static List<(double x, double z)> Inset(List<(double x, double z)> H, double d)
        {
            double x0 = H.Min(p => p.x) - 1, x1 = H.Max(p => p.x) + 1, z0 = H.Min(p => p.z) - 1, z1 = H.Max(p => p.z) + 1;
            var P = new List<(double x, double z)> { (x0, z0), (x1, z0), (x1, z1), (x0, z1) };
            for (int i = 0; i < H.Count && P.Count > 0; i++)
            {
                var a = H[i]; var b = H[(i + 1) % H.Count]; double ex = b.x - a.x, ez = b.z - a.z, l = Math.Sqrt(ex * ex + ez * ez); if (l < 1e-6) continue;
                double nx = -ez / l * d, nz = ex / l * d;              // the left normal: inside
                P = ClipHalf(P, (a.x + nx, a.z + nz), (b.x + nx, b.z + nz));
            }
            return P;
        }
        /// a counter-clockwise convex polygon grown by r with round corners
        static List<(double x, double z)> Rounded(List<(double x, double z)> H, double r)
        {
            var R = new List<(double, double)>(); int n = H.Count;
            for (int i = 0; i < n; i++)
            {
                var p = H[i]; var a = H[(i + n - 1) % n]; var b = H[(i + 1) % n];
                double a0 = Math.Atan2(-(p.x - a.x), p.z - a.z), a1 = Math.Atan2(-(b.x - p.x), b.z - p.z); // the outward (right) normals
                double da = a1 - a0; while (da < 0) da += 2 * Math.PI; while (da > 2 * Math.PI) da -= 2 * Math.PI;
                int steps = Math.Max(1, (int)Math.Ceiling(da / (Math.PI / 10)));
                for (int k = 0; k <= steps; k++) { double t = a0 + da * k / steps; R.Add((p.x + Math.Cos(t) * r, p.z + Math.Sin(t) * r)); }
            }
            return Hull(R);
        }
        static List<(double x, double z)> Ring(double[] r) { var l = new List<(double, double)>(); for (int i = 0; i + 1 < r.Length; i += 2) l.Add((r[i], r[i + 1])); return l; }

        static bool InSurf(List<Surf> ss, double x, double z)
        {
            foreach (var s in ss)
                for (int t = 0; t + 2 < s.t.Length; t += 3)
                {
                    double ax = s.v[s.t[t] * 2], az = s.v[s.t[t] * 2 + 1], bx = s.v[s.t[t + 1] * 2], bz = s.v[s.t[t + 1] * 2 + 1], cx = s.v[s.t[t + 2] * 2], cz = s.v[s.t[t + 2] * 2 + 1];
                    if (x < Math.Min(ax, Math.Min(bx, cx)) || x > Math.Max(ax, Math.Max(bx, cx)) || z < Math.Min(az, Math.Min(bz, cz)) || z > Math.Max(az, Math.Max(bz, cz))) continue;
                    if (InTri(x, z, ax, az, bx, bz, cx, cz, 0)) return true;
                }
            return false;
        }

        public static bool InRing(double[] r, double x, double z)
        {
            bool c = false; int n = r.Length / 2;
            for (int i = 0, k = n - 1; i < n; k = i++)
            {
                double xi = r[2 * i], zi = r[2 * i + 1], xk = r[2 * k], zk = r[2 * k + 1];
                if ((zi > z) != (zk > z) && x < (xk - xi) * (z - zi) / (zk - zi) + xi) c = !c;
            }
            return c;
        }

        /// the ground at a point for a lot: 0 bare terrain, 1 our footway / street paving / the riva, 2 anything else (a building,
        /// a wall, a model road, the D8, the sea); root: "Streets", "Riva" or "Roads" for those
        static int Probe(double gx, double gz, out float y) => Probe(gx, gz, out y, out _);
        static int Probe(double gx, double gz, out float y, out string rootName)
        {
            y = float.NaN; rootName = null; var u = W.U(gx, 0, gz);
            var hits = Physics.RaycastAll(new Vector3(u.x, 400, u.z), Vector3.down, 900, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (Under(h.collider.transform, "Tlo") && !(h.collider is BoxCollider) || Under(h.collider.transform, "Props") && !(h.collider is BoxCollider)
                    || Under(h.collider.transform, "ParkedCars") || h.collider.attachedRigidbody) continue;
                y = h.point.y;
                if (h.collider is TerrainCollider) return y < .4f ? 2 : 0;
                if (Under(h.collider.transform, "Streets")) { rootName = "Streets"; return 1; }
                if (Under(h.collider.transform, "Riva")) { rootName = "Riva"; return 1; }
                if (Under(h.collider.transform, "Roads")) rootName = "Roads";
                return 2;
            }
            return 2;
        }

        // ------------------------------------------------------------------ 1. the ground grid and the bays (GroundCover.Build)
        static void MlyPrepare(List<ModelJ> models)
        {
            nMlyFew = 0;
            var other = surf.Where(s => s.k == "parking" || s.k == "pool" || s.k == "court" || s.k == "rubber" || s.k == "tartan" || s.k == "clay" || s.k == "turf").ToList();
            foreach (var L in MLots)
            {
                L.bays.Clear(); L.nCars = 0; L.keep = false; if (L.osm) continue;
                var ringL = LocalRing(L);
                double bx0 = L.ring.Where((_, i) => i % 2 == 0).Min() - 2, bx1 = L.ring.Where((_, i) => i % 2 == 0).Max() + 2, bz0 = L.ring.Where((_, i) => i % 2 == 1).Min() - 2, bz1 = L.ring.Where((_, i) => i % 2 == 1).Max() + 2;
                var near = other.Where(s => { for (int i = 0; i + 1 < s.v.Length; i += 2) if (s.v[i] > bx0 - 60 && s.v[i] < bx1 + 60 && s.v[i + 1] > bz0 - 60 && s.v[i + 1] < bz1 + 60) return true; return false; }).ToList();
                var pit = pitches.Where(p => p.bx0 < bx1 && p.bx1 > bx0 && p.bz0 < bz1 && p.bz1 > bz0).ToList();
                L.gu = Math.Floor(L.u0 / SUB) * SUB - SUB; L.gv = Math.Floor(L.v0 / SUB) * SUB - SUB;
                L.nu = (int)Math.Ceiling((L.u1 - L.gu) / SUB) + 2; L.nv = (int)Math.Ceiling((L.v1 - L.gv) / SUB) + 2;
                L.kind = new byte[L.nu * L.nv]; L.y = new float[L.nu * L.nv];
                for (int k = 0; k < L.nv; k++)
                    for (int i = 0; i < L.nu; i++)
                    {
                        var (x, z) = L.World(L.gu + i * SUB, L.gv + k * SUB);
                        int kd = Probe(x, z, out float yy);
                        if (kd == 0 && (StreetsBuilder.Covered(x, z, 0) || WorldData.SurfDist(models, x, z) < .5 || RivaBuilder.Class(x, z) != 0)) kd = 2;
                        if (kd == 0 && (InSurf(near, x, z) || pit.Any(p => Dist(p, x, z) < 1))) kd = 2;
                        L.kind[k * L.nu + i] = (byte)kd; L.y[k * L.nu + i] = kd == 0 ? yy : (float)Tiles.Height(x, z); // a roof / wall top is no ground height
                    }
                int cu = L.nu - 1, cv = L.nv - 1;
                L.cut = new bool[cu * cv]; L.asph = new bool[cu * cv]; L.stand = new bool[cu * cv]; L.wall = new bool[cu * cv];
                var rotU = Quaternion.Euler(0, (float)(Math.Atan2(L.ux, -L.uz) * 180 / Math.PI), 0);
                for (int k = 0; k < cv; k++)
                    for (int i = 0; i < cu; i++)
                    {
                        var (x, z) = L.World(L.gu + (i + .5) * SUB, L.gv + (k + .5) * SUB);
                        bool inside = InRing(L.ring, x, z);
                        bool cut = inside || ClipConvex(Sq(L.gu + i * SUB, L.gv + k * SUB, SUB), ringL).Count >= 3;
                        if (!cut) continue;
                        L.cut[k * cu + i] = true;
                        int n0 = k * L.nu + i, n1 = n0 + 1, n2 = n0 + L.nu, n3 = n2 + 1;
                        int w = Math.Max(Math.Max(L.kind[n0], L.kind[n1]), Math.Max(L.kind[n2], L.kind[n3])); if (w == 2) continue;
                        float lo = Math.Min(Math.Min(L.y[n0], L.y[n1]), Math.Min(L.y[n2], L.y[n3])), hi = Math.Max(Math.Max(L.y[n0], L.y[n1]), Math.Max(L.y[n2], L.y[n3]));
                        // the asphalt follows the ground whatever its slope (only buildings, walls, streets shape its edge);
                        // a car stands only where it is flat (≤ 24 %)
                        L.asph[k * cu + i] = w == 0; L.stand[k * cu + i] = inside && hi - lo <= .12f;
                        // a thin wall / fence the 0.5 m probes step over: anything standing in the cell 0.35–1.6 m over the ground
                        var cU = W.U(x, (lo + hi) / 2 + .975, z);
                        foreach (var col in Physics.OverlapBox(cU, new Vector3(.25f, .625f, .25f), rotU, ~0, QueryTriggerInteraction.Ignore))
                        {
                            if (col is TerrainCollider || col.attachedRigidbody || Under(col.transform, "ParkedCars") || Under(col.transform, "Props")
                                || Under(col.transform, "Tlo") && !(col is BoxCollider) || Under(col.transform, "Streets")) continue;
                            L.wall[k * cu + i] = true; L.asph[k * cu + i] = false; L.stand[k * cu + i] = false; break;
                        }
                    }
                // asphalt in pieces ≥ 8 m² and ≥ a quarter of the biggest only (no crumbs between walls, no yard behind a wall)
                var seen = new bool[cu * cv]; var q = new Queue<int>(); var comp = new List<int>(); var comps = new List<List<int>>();
                for (int s = 0; s < cu * cv; s++)
                {
                    if (!L.asph[s] || seen[s]) continue;
                    comp.Clear(); q.Enqueue(s); seen[s] = true;
                    while (q.Count > 0)
                    {
                        int c = q.Dequeue(); comp.Add(c); int ci = c % cu, ck = c / cu;
                        foreach (var (di, dk) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        {
                            int ni = ci + di, nk = ck + dk; if (ni < 0 || nk < 0 || ni >= cu || nk >= cv) continue;
                            int nn = nk * cu + ni; if (L.asph[nn] && !seen[nn]) { seen[nn] = true; q.Enqueue(nn); }
                        }
                    }
                    comps.Add(new List<int>(comp));
                }
                int big = comps.Count > 0 ? comps.Max(c => c.Count) : 0;
                foreach (var cc in comps)
                    if (cc.Count * SUB * SUB < 8 || cc.Count < big / 4) foreach (int c in cc) { L.asph[c] = false; if (L.kind[c / cu * L.nu + c % cu] == 0) L.stand[c] = false; }
                // the bays: packed greedily, rows 1 m apart, the whole bay on standing room
                L.along = L.v1 - L.v0 < 4.6;
                double du = L.along ? 5.8 : 2.7, dv = L.along ? 2.4 : 5.2, hu = L.along ? 2.2 : .8, hv = L.along ? .8 : 2.2;
                for (double v = L.v0 + hv + .3; v <= L.v1 - hv; v += 1)
                    for (double u = L.u0 + hu + .3; u <= L.u1 - hu; u += .9)
                    {
                        bool ok = true;
                        for (int a = -2; a <= 2 && ok; a++)
                            for (int b = -2; b <= 2 && ok; b++)
                                if (!L.Stand(u + hu * a / 2, v + hv * b / 2)) ok = false;
                        if (!ok) continue;
                        foreach (var bb in L.bays) if (Math.Abs(bb.u - u) < du - .05 && Math.Abs(bb.v - v) < dv - .05) { ok = false; break; }
                        if (ok) L.bays.Add((u, v));
                    }
                if (L.bays.Count < 3) { L.bays.Clear(); nMlyFew++; continue; }
                L.keep = true;                                        // until MlyFinish (no car: dropped)
            }
            hard = null;
        }
        static List<(double x, double z)> Sq(double u0, double v0, double s) => new List<(double x, double z)> { (u0, v0), (u0 + s, v0), (u0 + s, v0 + s), (u0, v0 + s) };
        /// the outline in the lot frame, counter-clockwise
        static List<(double x, double z)> LocalRing(MLot L)
        {
            var r = Ring(L.ring).Select(p => L.Local(p.x, p.z)).Select(p => (x: p.u, z: p.v)).ToList();
            double a = 0; for (int i = 0; i < r.Count; i++) { var p = r[i]; var q = r[(i + 1) % r.Count]; a += p.x * q.z - q.x * p.z; }
            if (a < 0) r.Reverse();
            return r;
        }

        // ------------------------------------------------------------------ 2. the asphalt of the lots that got cars (after ParkedCars)
        public static string MlyFinish()
        {
            nMlyLots = nMlyAsph = nMlyLinks = nMlyEmpty = 0; mlyLinks = new List<double[]>();
            if (root == null || asphalt == null) return "[tlo] Mapillary: no ground build in this session";
            foreach (var f in Directory.GetFiles(OUT, "tlo-mly-*.asset")) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            var old = root.Find("mapillary"); if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var mroot = new GameObject("mapillary"); mroot.isStatic = true; mroot.transform.SetParent(root, false);
            var mbs = new Dictionary<int, MB>();
            MB Chunk(double gx, double gz) { int key = ((int)Math.Floor(gx / 256) + 100) * 1000 + (int)Math.Floor(gz / 256) + 100; if (!mbs.TryGetValue(key, out var m)) mbs[key] = m = new MB(); return m; }
            const float LIFT = .09f;
            var roadsC = StreetsBuilder.Carriageways();
            foreach (var L in MLots)
            {
                if (!L.keep) continue;
                if (L.nCars == 0) { L.keep = false; nMlyEmpty++; continue; }
                nMlyLots++;
                var ringL = LocalRing(L);
                int cu = L.nu - 1, cv = L.nv - 1;
                // a good node: bare ground
                var ok = new bool[L.nu * L.nv];
                for (int n = 0; n < ok.Length; n++) ok[n] = L.kind[n] == 0;
                void Emit(List<(double x, double z)> P)
                {
                    if (P.Count < 3) return;
                    var c0 = L.World(P[0].x, P[0].z); var mb = Chunk(c0.x, c0.z);
                    Vector3 V((double x, double z) p) { var w = L.World(p.x, p.z); return W.U(w.x, L.Y(p.x, p.z) + LIFT, w.z); }
                    Vector2 UV((double x, double z) p) { var w = L.World(p.x, p.z); return new Vector2((float)w.x / 4f, (float)w.z / 4f); }
                    for (int t = 1; t + 1 < P.Count; t++) mb.Tri(V(P[0]), V(P[t]), V(P[t + 1]), UV(P[0]), UV(P[t]), UV(P[t + 1]));
                }
                // 2 m blocks where every cell the outline touches is asphalt: one piece clipped by the outline; else cell by cell
                for (int bk = 0; bk < cv; bk += 4)
                    for (int bi = 0; bi < cu; bi += 4)
                    {
                        bool whole = bi + 4 <= cu && bk + 4 <= cv, any = false;
                        for (int k = bk; k < Math.Min(cv, bk + 4); k++)
                            for (int i = bi; i < Math.Min(cu, bi + 4); i++)
                            {
                                int c = k * cu + i;
                                if (L.asph[c]) any = true; else if (L.cut[c]) whole = false;
                            }
                        if (!any) continue;
                        // one 2 m piece only where it lies on the ground (every node within 4 cm of the piece's plane), else 0.5 m cells
                        if (whole)
                        {
                            float y00 = L.y[bk * L.nu + bi], y10 = L.y[bk * L.nu + bi + 4], y01 = L.y[(bk + 4) * L.nu + bi], y11 = L.y[(bk + 4) * L.nu + bi + 4];
                            for (int k = 0; k <= 4 && whole; k++)
                                for (int i = 0; i <= 4 && whole; i++)
                                    if (Math.Abs(Mathf.Lerp(Mathf.Lerp(y00, y10, i / 4f), Mathf.Lerp(y01, y11, i / 4f), k / 4f) - L.y[(bk + k) * L.nu + bi + i]) > .04f) whole = false;
                        }
                        if (whole) { Emit(ClipConvex(Sq(L.gu + bi * SUB, L.gv + bk * SUB, 4 * SUB), ringL)); nMlyAsph += 16; continue; }
                        for (int k = bk; k < Math.Min(cv, bk + 4); k++)
                            for (int i = bi; i < Math.Min(cu, bi + 4); i++)
                            {
                                int c = k * cu + i;
                                if (L.asph[c]) { Emit(ClipConvex(Sq(L.gu + i * SUB, L.gv + k * SUB, SUB), ringL)); nMlyAsph++; continue; }
                                // a cell on the edge of the asphalt (a wall, a bank): marching squares over the good nodes, the edge cut
                                // half way between a good and a bad node (a chamfer, not a step)
                                if (!L.cut[c] || L.wall[c]) continue;
                                bool nb = (i > 0 && L.asph[c - 1]) || (i + 1 < cu && L.asph[c + 1]) || (k > 0 && L.asph[c - cu]) || (k + 1 < cv && L.asph[c + cu]);
                                if (!nb) continue;
                                var corner = new[] { (i, k), (i + 1, k), (i + 1, k + 1), (i, k + 1) };
                                var P = new List<(double x, double z)>();
                                for (int q = 0; q < 4; q++)
                                {
                                    var (ai, ak) = corner[q]; var (bi2, bk2) = corner[(q + 1) % 4];
                                    bool oa = ok[ak * L.nu + ai], ob = ok[bk2 * L.nu + bi2];
                                    if (oa) P.Add((L.gu + ai * SUB, L.gv + ak * SUB));
                                    if (oa != ob) P.Add((L.gu + (ai + bi2) * SUB / 2, L.gv + (ak + bk2) * SUB / 2));
                                }
                                if (P.Count >= 3) Emit(ClipConvex(P, ringL));
                            }
                    }
                // the connector: the nearest of our streets (not the D8) under 15 m from the outline
                double best = 15, pbx = 0, pbz = 0, qx = 0, qz = 0, hwb = 0;
                var rl = Ring(L.ring);
                for (int e = 0; e < rl.Count; e++)
                {
                    var a = rl[e]; var b = rl[(e + 1) % rl.Count]; double el = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                    for (double s = 0; s < el; s += 1)
                    {
                        double px = a.x + (b.x - a.x) * s / el, pz = a.z + (b.z - a.z) * s / el;
                        foreach (var r in roadsC)
                        {
                            if (px < Math.Min(r.ax, r.bx) - 25 || px > Math.Max(r.ax, r.bx) + 25 || pz < Math.Min(r.az, r.bz) - 25 || pz > Math.Max(r.az, r.bz) + 25) continue;
                            double vx = r.bx - r.ax, vz = r.bz - r.az, l2 = vx * vx + vz * vz, t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((px - r.ax) * vx + (pz - r.az) * vz) / l2));
                            double cx = r.ax + vx * t, cz = r.az + vz * t, gap = Math.Sqrt((px - cx) * (px - cx) + (pz - cz) * (pz - cz)) - r.hw;
                            if (gap < best && Probe(cx, cz, out _, out string rn) == 1 && rn == "Streets") { best = gap; pbx = px; pbz = pz; qx = cx; qz = cz; hwb = r.hw; }
                        }
                    }
                }
                if (best < 15 && best > .3)
                {
                    double dx = qx - pbx, dz = qz - pbz, dl = Math.Sqrt(dx * dx + dz * dz); dx /= dl; dz /= dl;
                    double nx = -dz, nz = dx, start = -1.5, end = dl - hwb + .3; // from 1.5 m inside the lot to just onto the street
                    // along the strip: a building / wall / model road / the D8 on it: no connector; the footway or the street: it ends there
                    double reach = start; bool bad = false;
                    for (double s = start; s <= end + 1e-6 && !bad; s += .5)
                    {
                        bool street = false;
                        foreach (double w in new[] { -2.0, 0, 2.0 })
                        {
                            int kd = Probe(pbx + dx * s + nx * w, pbz + dz * s + nz * w, out _);
                            if (kd == 2) { bad = true; break; }
                            if (kd == 1) street = true;
                        }
                        if (bad) break;
                        reach = s; if (street) break;
                    }
                    if (!bad && reach - start > 2)
                    {
                        var mb = Chunk(pbx, pbz);
                        Vector3 P(double ss, double w) { double x = pbx + dx * ss + nx * w, z = pbz + dz * ss + nz * w; return W.U(x, Tiles.Height(x, z) + LIFT + .01, z); }
                        Vector2 U(double ss, double w) { double x = pbx + dx * ss + nx * w, z = pbz + dz * ss + nz * w; return new Vector2((float)x / 4f, (float)z / 4f); }
                        for (double s = start; s < reach - 1e-6; s += .5)
                        {
                            double s1 = Math.Min(reach, s + .5);
                            mb.Tri(P(s, -2), P(s1, -2), P(s1, 2), U(s, -2), U(s1, -2), U(s1, 2)); mb.Tri(P(s, -2), P(s1, 2), P(s, 2), U(s, -2), U(s1, 2), U(s, 2));
                        }
                        mlyLinks.Add(new[] { pbx + dx * start - nx * 2, pbz + dz * start - nz * 2, pbx + dx * reach - nx * 2, pbz + dz * reach - nz * 2, pbx + dx * reach + nx * 2, pbz + dz * reach + nz * 2, pbx + dx * start + nx * 2, pbz + dz * start + nz * 2 });
                        nMlyLinks++;
                    }
                }
            }
            int nm = 0, tris = 0;
            foreach (var kv in mbs)
            {
                var mb = kv.Value; if (mb.v.Count == 0) continue;
                var mesh = new Mesh { name = $"tlo-mly-{kv.Key}", indexFormat = mb.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                mesh.SetVertices(mb.v); mesh.SetUVs(0, mb.uv); mesh.SetTriangles(mb.t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, $"{OUT}/{mesh.name}.asset");
                var go = new GameObject(mesh.name); go.isStatic = true; go.transform.SetParent(mroot.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = asphalt; mr.shadowCastingMode = ShadowCastingMode.Off;
                nm++; tris += mb.t.Count / 3;
            }
            AssetDatabase.SaveAssets();
            // the kept lots and connectors for Hard() (grass detail, trees) in later sessions
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder("{\"keep\":[");
            sb.Append(string.Join(",", MLots.Where(l => l.keep).Select(l => l.id)));
            sb.Append("],\"links\":[");
            sb.Append(string.Join(",", mlyLinks.Select(a => "[" + string.Join(",", a.Select(v => v.ToString("F2", inv))) + "]")));
            sb.Append("]}");
            File.WriteAllText(MLY_KEEP, sb.ToString());
            hard = null;
            return $"[tlo] Mapillary parkirališta: {MLots.Count} ({MLots.Count(l => l.osm)} su OSM parkirališta), {nMlyFew} s manje od 3 mjesta i {nMlyEmpty} s manje od 3 auta izbačeno, " +
                   $"{nMlyLots} izgrađeno: {nMlyAsph} ćelija asfalta (0,5 m), {nMlyLinks} priključaka na ulicu; {nm} meshova, {tris} trokuta";
        }

        /// Hard(): the kept lots and their connectors
        static void MarkMly(Action<double, double, double, double, Func<double, double, bool>> Mark)
        {
            foreach (var l in MLots)
                if (l.keep) Mark(l.ring.Where((_, i) => i % 2 == 0).Min(), l.ring.Where((_, i) => i % 2 == 1).Min(), l.ring.Where((_, i) => i % 2 == 0).Max(), l.ring.Where((_, i) => i % 2 == 1).Max(), (x, z) => InRing(l.ring, x, z));
            foreach (var a in mlyLinks)
                Mark(new[] { a[0], a[2], a[4], a[6] }.Min(), new[] { a[1], a[3], a[5], a[7] }.Min(), new[] { a[0], a[2], a[4], a[6] }.Max(), new[] { a[1], a[3], a[5], a[7] }.Max(), (x, z) => InRing(a, x, z));
        }
    }
}
