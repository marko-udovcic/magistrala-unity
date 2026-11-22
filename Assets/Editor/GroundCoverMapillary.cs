// Mapillary car parks (docs/mapillary-izvlacenje.md, "Parking"): Assets/Editor/parking-mapillary.json is the lots part of
// BalkanDriver tools/world/mapillary/parking.json (s5_parking.py; Mapillary contributors, CC BY-SA 4.0): off-road clusters of
// parked cars seen in the street photos (convex hull of the cars + 2 m, positions good to ~2–3 m).
//   * a lot that overlaps an OSM car park of the ground build (≥ 20 % of its area) is that car park: nothing new
//   * the others get the car park asphalt (the material of the OSM car parks, no bay lines) on a 2 m grid along the lot's
//     long axis, a cell only where the terrain is bare (not a building, a street / footway, a model road, the riva paving),
//     flat (≤ 0.5 m over the cell) and not on another surface; cells on our footway / paving are kept as standing room
//     without asphalt (the shop fronts of Štafilić)
//   * PropsBuilder.ParkedCars fills the lots (rows of nose-in bays across the long axis, standing room only)
//   * Hard() covers the lots too (no grass detail, no tree there)
// The kerbside segments of the file are empty (the old road has almost no segmented imagery, the D8 has no kerbside parking).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static partial class GroundCover
    {
        public const string MLY = "Assets/Editor/parking-mapillary.json";
        public const double CELL = 2;

        public class MLot
        {
            public string road; public double[] ring; public double cx, cz, ux, uz, area; public int seen, seqs;
            public bool osm;                                              // an OSM car park is there
            public double u0, u1, v0, v1;                                 // extent in the lot frame (u along, v across)
            public Dictionary<(int, int), bool> cells = new Dictionary<(int, int), bool>(); // cell -> asphalt (false: footway / paving)
            public (double u, double v) Local(double gx, double gz) { double dx = gx - cx, dz = gz - cz; return (dx * ux + dz * uz, -dx * uz + dz * ux); }
            public (double x, double z) World(double u, double v) => (cx + ux * u - uz * v, cz + uz * u + ux * v);
            public static (int, int) Cell(double u, double v) => ((int)Math.Floor(u / CELL), (int)Math.Floor(v / CELL));
        }
        public static List<MLot> MLots = new List<MLot>();
        public static int nMlyCells, nMlyPaved, nMlyLots;
        public static bool UseMly = true;   // false: the ground build and the parked cars without the Mapillary lots (a comparison run)

        static void LoadMly()
        {
            MLots = new List<MLot>();
            if (!UseMly || !File.Exists(MLY)) return;
            var j = MiniJson.O(MiniJson.Parse(File.ReadAllText(MLY)));
            foreach (var o in MiniJson.L(j["lots"]))
            {
                var d = MiniJson.O(o);
                var pts = MiniJson.L(d["poly"]).Select(p => { var q = MiniJson.L(p); return (x: MiniJson.D(q[0]), z: MiniJson.D(q[1])); }).ToList();
                var hull = Hull(pts); if (hull.Count < 3) continue;
                var L = new MLot { road = (string)d["road"], seen = (int)MiniJson.D(d["cars_seen"]), seqs = (int)MiniJson.D(d["seqs"]) };
                L.ring = hull.SelectMany(p => new[] { p.x, p.z }).ToArray();
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
                L.u0 = L.v0 = 1e9; L.u1 = L.v1 = -1e9;
                foreach (var p in hull) { var (u, v) = L.Local(p.x, p.z); L.u0 = Math.Min(L.u0, u); L.u1 = Math.Max(L.u1, u); L.v0 = Math.Min(L.v0, v); L.v1 = Math.Max(L.v1, v); }
                for (int i = 0; i < hull.Count; i++) { var a = hull[i]; var b = hull[(i + 1) % hull.Count]; L.area += a.x * b.z - b.x * a.z; }
                L.area = Math.Abs(L.area) / 2;
                // an OSM car park there? (share of a 1.5 m sample grid inside the car park triangles)
                int nIn = 0, nOsm = 0;
                var park = surf.Where(s => s.k == "parking").ToList();
                for (double u = L.u0; u <= L.u1; u += 1.5)
                    for (double v = L.v0; v <= L.v1; v += 1.5)
                    {
                        var (x, z) = L.World(u, v); if (!InRing(L.ring, x, z)) continue; nIn++;
                        if (InSurf(park, x, z)) nOsm++;
                    }
                L.osm = nIn == 0 || nOsm >= .2 * nIn;
                MLots.Add(L);
            }
        }

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

        static List<(double x, double z)> Hull(List<(double x, double z)> p)
        {
            var s = p.Distinct().OrderBy(q => q.x).ThenBy(q => q.z).ToList(); if (s.Count < 3) return s;
            double Cr((double x, double z) o, (double x, double z) a, (double x, double z) b) => (a.x - o.x) * (b.z - o.z) - (a.z - o.z) * (b.x - o.x);
            var h = new List<(double, double)>();
            foreach (var q in s) { while (h.Count >= 2 && Cr(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            int lo = h.Count + 1;
            for (int i = s.Count - 2; i >= 0; i--) { var q = s[i]; while (h.Count >= lo && Cr(h[h.Count - 2], h[h.Count - 1], q) <= 0) h.RemoveAt(h.Count - 1); h.Add(q); }
            h.RemoveAt(h.Count - 1); return h;
        }

        /// the ground at a point for a lot: 0 bare terrain, 1 our footway / street paving / the riva, 2 anything else (a building,
        /// a wall, a model road, the D8, the sea)
        static int Probe(double gx, double gz, out float y)
        {
            y = float.NaN; var u = W.U(gx, 0, gz);
            var hits = Physics.RaycastAll(new Vector3(u.x, 400, u.z), Vector3.down, 900, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (Under(h.collider.transform, "Tlo") && !(h.collider is BoxCollider) || Under(h.collider.transform, "Props") && !(h.collider is BoxCollider)
                    || Under(h.collider.transform, "ParkedCars") || h.collider.attachedRigidbody) continue;
                y = h.point.y;
                if (h.collider is TerrainCollider) return y < .4f ? 2 : 0;
                if (Under(h.collider.transform, "Streets") || Under(h.collider.transform, "Riva")) return 1;
                return 2;
            }
            return 2;
        }

        static void MlyLots(List<ModelJ> models)
        {
            nMlyCells = nMlyPaved = nMlyLots = 0;
            var other = surf.Where(s => s.k == "parking" || s.k == "pool" || s.k == "court" || s.k == "rubber" || s.k == "tartan" || s.k == "clay" || s.k == "turf").ToList();
            foreach (var L in MLots)
            {
                L.cells.Clear(); if (L.osm) continue;
                for (int i = (int)Math.Floor(L.u0 / CELL); i * CELL < L.u1; i++)
                    for (int k = (int)Math.Floor(L.v0 / CELL); k * CELL < L.v1; k++)
                    {
                        double ua = i * CELL, va = k * CELL;
                        var (cx, cz) = L.World(ua + CELL / 2, va + CELL / 2);
                        if (!InRing(L.ring, cx, cz)) continue;
                        if (pitches.Any(p => cx > p.bx0 && cx < p.bx1 && cz > p.bz0 && cz < p.bz1 && Dist(p, cx, cz) < 1) || InSurf(other, cx, cz)) continue;
                        var c = new[] { (ua, va), (ua + CELL, va), (ua + CELL, va + CELL), (ua, va + CELL), (ua + CELL / 2, va + CELL / 2) };
                        var ys = new float[5]; int worst = 0;
                        for (int q = 0; q < 5; q++) { var (x, z) = L.World(c[q].Item1, c[q].Item2); worst = Math.Max(worst, Probe(x, z, out ys[q])); if (worst == 2) break; }
                        if (worst == 2) continue;
                        if (ys.Max() - ys.Min() > .5f) continue;
                        bool bare = worst == 0 && !StreetsBuilder.Covered(cx, cz, 0) && WorldData.SurfDist(models, cx, cz) >= .5 && RivaBuilder.Class(cx, cz) == 0;
                        if (worst == 0 && !bare) continue;                // a model road / street cover without a collider hit: leave it
                        L.cells[(i, k)] = bare;
                        if (!bare) { nMlyPaved++; continue; }
                        var mb = Get(asphalt, cx, cz);
                        Vector3 P(int q) { var (x, z) = L.World(c[q].Item1, c[q].Item2); return W.U(x, ys[q] + .09f, z); }  // 2 m cells over undulating ground: a little higher than the OSM car parks
                        Vector2 UV(int q) { var (x, z) = L.World(c[q].Item1, c[q].Item2); return new Vector2((float)x / 4f, (float)z / 4f); }
                        mb.Tri(P(0), P(1), P(2), UV(0), UV(1), UV(2)); mb.Tri(P(0), P(2), P(3), UV(0), UV(2), UV(3)); nTris += 2; nMlyCells++;
                    }
                if (L.cells.Count >= 4) nMlyLots++; else L.cells.Clear();
            }
            log.Add($"[tlo] Mapillary parkirališta: {MLots.Count} ({MLots.Count(l => l.osm)} su OSM parkirališta), {nMlyLots} nova: {nMlyCells} ćelija asfalta, {nMlyPaved} na pločniku / popločenju");
        }
    }
}
