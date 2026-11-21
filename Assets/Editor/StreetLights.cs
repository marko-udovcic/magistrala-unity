// Street lamps (Magistrala/Rekviziti: postavi -> PropsBuilder.Lamps): positions from Mapillary map features where they exist
// (Assets/Editor/rasvjeta-mapillary.json, Tools/rasvjeta/mly_lights.py: object--street-light merged within 6 m; utility poles), the
// gaps filled by rule:
//   D8 (Štafilić -> Sućurac)  double-arm 11 m lamps in the central median (its centre line: MedianHedge.MedianLine, the hedge's own
//                             scan, so the lamp stands in the hedge line); Mapillary lights within the median snapped onto it and merged
//                             within 22 m, gaps over 50 m filled every ~36 m (not where Mapillary shows the stretch lit from the outer
//                             sides); Mapillary lights beside the carriageways (junctions, slip roads) as single 9 m lamps at the edge
//   old road / through roads  single-arm 9 m lamps 0.7 m off the asphalt edge, one side (the side Mapillary shows, else the sea side),
//                             every ~33 m
//   village streets           single-arm 7 m lamps every ~36 m on one side, only where there are houses; a Mapillary utility pole
//                             within 7 m on that side becomes a concrete pole with a bracket lamp
// Every spot: on the ground (not a roof / the sea), off every carriageway and the models' asphalt (0.3 m), not at a junction mouth
// (12 m from a node of 3+ arms), not in a stop's bay, not on a zebra, 14 m from the next lamp (rule) / 6 m (Mapillary).
// The lamps are drawn by Magistrala.StreetLights (instanced, two LODs, short shadows); a capsule collider per pole (one GameObject
// per 128 m cell); the light on the ground at night is a draped additive mesh per cell (Magistrala/LightPool), baked here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static partial class PropsBuilder
    {
        const string LAMP_JSON = "Assets/Editor/rasvjeta-mapillary.json", LAMP_OUT = WorldBuilder.GEN + "/Lights";
        // types: 0 D8 double-arm, 1 single 9 m (old road, through roads, D8 sides, ramps), 2 single 7 m (village), 3 concrete pole + bracket
        static readonly float[] LH = { 11f, 9f, 7f, 8.5f }, LARM = { 1.9f, 1.6f, 1.0f, .75f }, LPOOL = { 10f, 9f, 6.5f, 6.5f }, LSTR = { 1f, .9f, .75f, .7f };
        static readonly float[] LR0 = { .11f, .09f, .075f, .12f }, LR1 = { .06f, .05f, .045f, .075f };
        const float SP_OLD = 33, SP_VILLAGE = 36, SP_D8 = 36;

        static List<(double ax, double az, double bx, double bz, double hw)> lampRoads;
        static bool Asph(double gx, double gz)
        {
            if (roadGrid.TryGetValue(((int)Math.Floor(gx / 16), (int)Math.Floor(gz / 16)), out var l))
                foreach (int i in l)
                {
                    var r = roads[i]; double bx = r.bx - r.ax, bz = r.bz - r.az, l2 = bx * bx + bz * bz;
                    double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((gx - r.ax) * bx + (gz - r.az) * bz) / l2));
                    double dx = gx - r.ax - bx * t, dz = gz - r.az - bz * t;
                    if (dx * dx + dz * dz < r.hw * r.hw) return true;
                }
            foreach (var m in models) if (Math.Abs(gx - m.x) <= m.half + 1 && Math.Abs(gz - m.z) <= m.half + 1 && !double.IsNaN(m.Surface(gx, gz))) return true;
            return false;
        }

        class LEdge { public StreetsBuilder.LampEdge e; public double[] cum; public double L => cum[cum.Length - 1]; }
        static (double x, double z, double tx, double tz) At(LEdge E, double s)
        {
            var p = E.e.pts; s = Math.Max(0, Math.Min(E.L, s));
            int i = 1; while (i < p.Count - 1 && E.cum[i] < s) i++;
            double seg = Math.Max(1e-6, E.cum[i] - E.cum[i - 1]), t = (s - E.cum[i - 1]) / seg;
            double dx = p[i][0] - p[i - 1][0], dz = p[i][2] - p[i - 1][2], l = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
            return (p[i - 1][0] + dx * t, p[i - 1][2] + dz * t, dx / l, dz / l);
        }

        struct Lamp { public double gx, gz; public int type; public float yawU; public bool mly; public Vector2 roadDir; public float y; }

        static string Lamps()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!File.Exists(LAMP_JSON)) return "[rasvjeta] no " + LAMP_JSON;
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(LAMP_JSON)));
            var lights = MiniJson.L(root["lights"]).Select(o => { var q = MiniJson.L(o); return (x: MiniJson.D(q[0]), z: MiniJson.D(q[1])); }).ToList();
            var upoles = MiniJson.L(root["utility_poles"]).Select(o => { var q = MiniJson.L(o); return (x: MiniJson.D(q[0]), z: MiniJson.D(q[1])); }).ToList();

            // ---- the D8 median: columns (1 m) -> runs
            var cols = MedianHedge.MedianLine(models, 1).OrderBy(c => c.x).ToList();
            var runOf = new int[cols.Count]; var runs = new List<(int a, int b)>();
            for (int i = 0; i < cols.Count; i++)
            {
                int best = -1;
                for (int j = i - 1; j >= 0 && cols[i].x - cols[j].x <= 1.6; j--) if (Math.Abs(cols[i].zc - cols[j].zc) < 1.2) { best = j; break; }
                if (best >= 0 && runs[runOf[best]].b == best) { runOf[i] = runOf[best]; runs[runOf[i]] = (runs[runOf[i]].a, i); }
                else { runOf[i] = runs.Count; runs.Add((i, i)); }
            }
            var colAt = new Dictionary<int, List<int>>();
            for (int i = 0; i < cols.Count; i++) { int k = (int)Math.Floor(cols[i].x); if (!colAt.TryGetValue(k, out var l)) colAt[k] = l = new List<int>(); l.Add(i); }
            int MedianCol(double gx, double gz, double pad)
            {
                int best = -1; double bd = 1e9;
                for (int k = (int)Math.Floor(gx) - 1; k <= (int)Math.Floor(gx) + 1; k++)
                    if (colAt.TryGetValue(k, out var l)) foreach (int i in l) { double d = Math.Abs(gz - cols[i].zc); if (d < cols[i].half + pad && d < bd) { bd = d; best = i; } }
                return best;
            }
            int ColNear(int run, double x)
            {
                var (a, b) = runs[run]; int best = a; double bd = 1e9;
                for (int i = a; i <= b; i++) if (runOf[i] == run) { double d = Math.Abs(cols[i].x - x); if (d < bd) { bd = d; best = i; } }
                return best;
            }

            // ---- the streets
            var E = StreetsBuilder.LampEdges().Select(e =>
            {
                var c = new double[e.pts.Count];
                for (int i = 1; i < c.Length; i++) c[i] = c[i - 1] + Math.Sqrt(Math.Pow(e.pts[i][0] - e.pts[i - 1][0], 2) + Math.Pow(e.pts[i][2] - e.pts[i - 1][2], 2));
                return new LEdge { e = e, cum = c };
            }).Where(x => x.L > 1).ToList();
            var eg = new Dictionary<(int, int), List<(int e, int i)>>();
            for (int ei = 0; ei < E.Count; ei++)
                for (int i = 0; i + 1 < E[ei].e.pts.Count; i++)
                {
                    var p = E[ei].e.pts[i]; var q = E[ei].e.pts[i + 1];
                    for (int cx = (int)Math.Floor(Math.Min(p[0], q[0]) / 32); cx <= (int)Math.Floor(Math.Max(p[0], q[0]) / 32); cx++)
                        for (int cz = (int)Math.Floor(Math.Min(p[2], q[2]) / 32); cz <= (int)Math.Floor(Math.Max(p[2], q[2]) / 32); cz++)
                        { if (!eg.TryGetValue((cx, cz), out var l)) eg[(cx, cz)] = l = new List<(int, int)>(); l.Add((ei, i)); }
                }
            // nearest edge: (edge, s, lateral u: + = left of the direction (-tz, tx)), distance
            (int ei, double s, double u, double d) Nearest(double x, double z, double max)
            {
                (int, double, double, double) best = (-1, 0, 0, max);
                for (int cx = (int)Math.Floor((x - max) / 32); cx <= (int)Math.Floor((x + max) / 32); cx++)
                    for (int cz = (int)Math.Floor((z - max) / 32); cz <= (int)Math.Floor((z + max) / 32); cz++)
                        if (eg.TryGetValue((cx, cz), out var l))
                            foreach (var (ei, i) in l)
                            {
                                var p = E[ei].e.pts[i]; var q = E[ei].e.pts[i + 1]; double dx = q[0] - p[0], dz = q[2] - p[2], l2 = dx * dx + dz * dz;
                                double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((x - p[0]) * dx + (z - p[2]) * dz) / l2));
                                double ox = x - p[0] - dx * t, oz = z - p[2] - dz * t, d = Math.Sqrt(ox * ox + oz * oz);
                                if (d < best.Item4) { double L = Math.Sqrt(l2); best = (ei, E[ei].cum[i] + t * L, (ox * -dz + oz * dx) / Math.Max(1e-6, L), d); }
                            }
                return best;
            }
            // the asphalt edge from an edge's centre line at s, on side sg (+1 left / -1 right): lateral distance, NaN if none within reach
            double EdgeU(LEdge L, double s, int sg)
            {
                var p = At(L, s); double nx = -p.tz * sg, nz = p.tx * sg;
                double reach = L.e.hw + 9;
                for (double u = .25; u < reach; u += .25)
                    if (!Asph(p.x + nx * u, p.z + nz * u) && !Asph(p.x + nx * (u + .4), p.z + nz * (u + .4))) return u;
                return double.NaN;
            }

            // houses (built-up land for the village rule): tile footprint centroids on a 20 m grid
            var built = new HashSet<(int, int)>();
            for (int tz = WorldBuilder.TZ0; tz <= WorldBuilder.TZ1; tz++)
                for (int tx = WorldBuilder.TX0; tx <= WorldBuilder.TX1; tx++)
                {
                    var tile = Tiles.Get(tx, tz); if (tile?.buildingsJson == null) continue;
                    foreach (var bo in MiniJson.L(MiniJson.Parse(tile.buildingsJson)))
                    {
                        var pts = MiniJson.L(MiniJson.O(bo)["pts"]); if (pts.Count == 0) continue;
                        foreach (var po in pts) { var q = MiniJson.L(po); built.Add(((int)Math.Floor(MiniJson.D(q[0]) / 20), (int)Math.Floor(MiniJson.D(q[1]) / 20))); }
                    }
                }
            bool Built(double x, double z) { int cx = (int)Math.Floor(x / 20), cz = (int)Math.Floor(z / 20); for (int a = -2; a <= 2; a++) for (int b = -2; b <= 2; b++) if (built.Contains((cx + a, cz + b))) return true; return false; }

            // zebras (signals without models): no lamp on them
            var zebras = new List<Vector2>();
            try { foreach (var j in ExtraSignals.Load()) { foreach (var a in j.approaches) if (a.zebra != null) zebras.Add(a.zebra.p); foreach (var pd in j.ped) zebras.Add(pd.p); } } catch (Exception) { }

            // ---- Mapillary lights -> median / edge sides
            var medC = new Dictionary<int, List<double>>(); var sideC = new Dictionary<(int, int), List<double>>(); var outerX = new List<double>();
            int nMlyMedian = 0, nMlyEdge = 0, nMlyNone = 0;
            foreach (var (x, z) in lights)
            {
                if (x < StreetsBuilder.AX0 || x > StreetsBuilder.AX1 || z < StreetsBuilder.AZ0 || z > StreetsBuilder.AZ1) continue;
                int c = MedianCol(x, z, 3.5);
                if (c >= 0) { int r = runOf[c]; if (!medC.TryGetValue(r, out var l)) medC[r] = l = new List<double>(); l.Add(x); nMlyMedian++; continue; }
                var h = Nearest(x, z, 15);
                if (h.ei < 0 || h.d > E[h.ei].e.hw + 9) { nMlyNone++; continue; }
                if (E[h.ei].e.kind == 0) { if (MedianCol(x, z, 6) >= 0) { nMlyNone++; continue; } outerX.Add(x); }
                var key = (h.ei, h.u >= 0 ? 1 : -1); if (!sideC.TryGetValue(key, out var sl0)) sideC[key] = sl0 = new List<double>(); sl0.Add(h.s); nMlyEdge++;
            }
            List<double> Merge(List<double> v, double r)
            {
                v.Sort(); var o = new List<double>(); int i = 0;
                while (i < v.Count) { int j = i; double sum = 0; while (j < v.Count && v[j] - v[i] < r) { sum += v[j]; j++; } o.Add(sum / (j - i)); i = j; }
                return o;
            }
            outerX.Sort();
            bool OuterNear(double x, double r) { int k = outerX.BinarySearch(x - r); if (k < 0) k = ~k; return k < outerX.Count && outerX[k] <= x + r; }

            var cand = new List<Lamp>();
            Vector2 UDir(double gx, double gz) => new Vector2((float)gx, (float)-gz).normalized; // game direction -> Unity xz
            float Yaw(Vector2 u) => Mathf.Atan2(u.x, u.y) * Mathf.Rad2Deg;

            // ---- D8 median
            int nMedRule = 0, nMedMly = 0;
            for (int r = 0; r < runs.Count; r++)
            {
                var (a, b) = runs[r]; double xa = cols[a].x, xb = cols[b].x; if (xb - xa < 8) continue;
                var anchors = medC.TryGetValue(r, out var ml) ? Merge(ml, 22) : new List<double>();
                var xs = new List<(double x, bool mly)>(); foreach (var x in anchors) xs.Add((x, true));
                double lo = xa + 5, hi = xb - 5;
                void FillBetween(double p, double q, bool pIs, bool qIs)
                {
                    double gap = q - p; int k = (int)Math.Ceiling(gap / SP_D8) - 1;
                    if (!pIs && !qIs) { if (gap < 10) return; k = Math.Max(1, (int)Math.Round(gap / SP_D8)); for (int i = 0; i < k; i++) xs.Add((p + gap * (i + .5) / k, false)); return; }
                    if (!pIs) { for (double x = q - SP_D8; x >= p; x -= SP_D8) xs.Add((x, false)); return; }
                    if (!qIs) { for (double x = p + SP_D8; x <= q; x += SP_D8) xs.Add((x, false)); return; }
                    if (gap > 50) for (int i = 1; i <= k; i++) xs.Add((p + gap * i / (k + 1), false));
                }
                var st = new List<(double x, bool a)> { (lo, false) }; foreach (var x in anchors) if (x > lo && x < hi) st.Add((x, true)); st.Add((hi, false));
                for (int i = 0; i + 1 < st.Count; i++) FillBetween(st[i].x, st[i + 1].x, st[i].a, st[i + 1].a);
                foreach (var (x, mly) in xs)
                {
                    if (!mly && OuterNear(x, 45)) continue; // a stretch lit from its sides
                    int c = ColNear(r, x); var cc = cols[c];
                    if (cc.half < .45) continue;
                    int c0 = Math.Max(a, c - 3), c1 = Math.Min(b, c + 3);
                    var t = new Vector2((float)(cols[c1].x - cols[c0].x), (float)(cols[c1].zc - cols[c0].zc)); if (t.sqrMagnitude < 1e-4f) t = new Vector2(1, 0); t.Normalize();
                    var across = UDir(-t.y, t.x);
                    cand.Add(new Lamp { gx = cc.x, gz = cc.zc, type = 0, yawU = Yaw(across), mly = mly, roadDir = UDir(t.x, t.y) });
                    if (mly) nMedMly++; else nMedRule++;
                }
            }

            // ---- edges: Mapillary sides + rule fill
            int nEdgeMly = 0, nEdgeRule = 0;
            var edgeLamps = new List<(int ei, double s, int sg, bool mly)>();
            for (int ei = 0; ei < E.Count; ei++)
            {
                var L = E[ei]; var e = L.e;
                var left = sideC.TryGetValue((ei, 1), out var lv) ? Merge(lv, 18) : new List<double>();
                var right = sideC.TryGetValue((ei, -1), out var rv) ? Merge(rv, 18) : new List<double>();
                foreach (var s in left) edgeLamps.Add((ei, s, 1, true));
                foreach (var s in right) edgeLamps.Add((ei, s, -1, true));
                if (e.kind != 1 && e.kind != 2) continue;
                double sp = e.kind == 1 ? SP_OLD : SP_VILLAGE;
                if (e.kind == 2 && e.hw < 2.1) continue;
                int main;
                if (left.Count != right.Count) main = left.Count > right.Count ? 1 : -1;
                else if (e.kind == 1) { var m = At(L, L.L / 2); main = m.tx >= 0 ? 1 : -1; } // the left normal (-tz, tx) has +z (south, the sea) when tx > 0
                else main = (Math.Abs(e.id) % 2 == 0) ? 1 : -1;
                var any = left.Concat(right).OrderBy(x => x).ToList();
                double mA = e.degA >= 3 ? 12 : 3, mB = e.degB >= 3 ? 12 : 3;
                double s0 = mA, s1 = L.L - mB; if (s1 - s0 < 4) { if (L.L > 22 && any.Count == 0) { s0 = s1 = L.L / 2; } else continue; }
                double phase = (Math.Abs(e.id * 7919) % 1000) / 1000.0 * sp * .5;
                for (double s = s0 + phase; s <= s1 + 1e-6; s += sp)
                {
                    if (any.Any(x => Math.Abs(x - s) < sp * .6)) continue;
                    var p = At(L, s); if (!Built(p.x, p.z)) continue;
                    edgeLamps.Add((ei, s, main, false));
                }
            }
            foreach (var (ei, s, sg, mly) in edgeLamps)
            {
                var L = E[ei]; var e = L.e; double eu = EdgeU(L, s, sg); if (double.IsNaN(eu)) continue;
                // a D8 lamp on the median side of a carriageway is the median's (above), not an edge lamp
                var p = At(L, s); double nx = -p.tz * sg, nz = p.tx * sg;
                if (e.kind == 0 && MedianCol(p.x + nx * (eu + .5), p.z + nz * (eu + .5), 1.5) >= 0) continue;
                int type = e.kind == 2 ? 2 : 1;
                cand.Add(new Lamp { gx = p.x + nx * (eu + .7), gz = p.z + nz * (eu + .7), type = type, yawU = Yaw(UDir(-nx, -nz)), mly = mly, roadDir = UDir(p.tx, p.tz) });
                if (mly) nEdgeMly++; else nEdgeRule++;
            }

            // ---- validate, dedupe, snap village rule lamps to utility poles
            var upGrid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < upoles.Count; i++) { var k = ((int)Math.Floor(upoles[i].x / 16), (int)Math.Floor(upoles[i].z / 16)); if (!upGrid.TryGetValue(k, out var l)) upGrid[k] = l = new List<int>(); l.Add(i); }
            var usedPole = new HashSet<int>();
            bool Valid(double gx, double gz, float r, out float y)
            {
                y = float.NaN;
                if (gx < StreetsBuilder.AX0 || gx > StreetsBuilder.AX1 || gz < StreetsBuilder.AZ0 || gz > StreetsBuilder.AZ1) return false;
                if (Asph(gx, gz) || Asph(gx + r, gz) || Asph(gx - r, gz) || Asph(gx, gz + r) || Asph(gx, gz - r)) return false;
                var u = W.U(gx, 0, gz);
                y = GroundY(u.x, u.z, out bool bld); if (bld || float.IsNaN(y) || y < .3f) return false;
                if (InBay(new Vector3(u.x, y, u.z), 1f)) return false;
                foreach (var z in zebras) if ((z - new Vector2((float)gx, (float)gz)).sqrMagnitude < 25) return false;
                foreach (var col in Physics.OverlapCapsule(new Vector3(u.x, y + .5f, u.z), new Vector3(u.x, y + 3, u.z), .2f, ~0, QueryTriggerInteraction.Ignore))
                    if (!Ignored(col) && (HasAncestor(col.transform, "Buildings") || col is BoxCollider)) return false;
                return true;
            }
            var acc = new List<Lamp>(); var accGrid = new Dictionary<(int, int), List<int>>();
            bool Crowded(double gx, double gz, double r)
            {
                int cx = (int)Math.Floor(gx / 16), cz = (int)Math.Floor(gz / 16);
                for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++)
                        if (accGrid.TryGetValue((cx + a, cz + b), out var l)) foreach (int i in l) { double dx = acc[i].gx - gx, dz = acc[i].gz - gz; if (dx * dx + dz * dz < r * r) return true; }
                return false;
            }
            int nRejected = 0, nPole = 0;
            foreach (var c0 in cand.OrderBy(c => c.mly ? 0 : 1).ThenBy(c => c.type))
            {
                var c = c0; bool ok = false;
                var dir = new Vector2(Mathf.Sin(c.yawU * Mathf.Deg2Rad), Mathf.Cos(c.yawU * Mathf.Deg2Rad)); // Unity xz, towards the road
                var gdir = new Vector2(dir.x, -dir.y); var gt = new Vector2(c.roadDir.x, -c.roadDir.y);
                double minD = c.mly ? 6 : (c.type == 0 ? 20 : 14);
                // village rule lamps on a Mapillary utility pole nearby (same side of the street)
                if (!c.mly && c.type == 2)
                {
                    int best = -1; double bd = 7;
                    var k = ((int)Math.Floor(c.gx / 16), (int)Math.Floor(c.gz / 16));
                    for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++)
                            if (upGrid.TryGetValue((k.Item1 + a, k.Item2 + b), out var l))
                                foreach (int i in l)
                                {
                                    if (usedPole.Contains(i)) continue;
                                    double dx = upoles[i].x - c.gx, dz = upoles[i].z - c.gz, d = Math.Sqrt(dx * dx + dz * dz);
                                    double toRoad = dx * gdir.x + dz * gdir.y; // + = nearer the road than the planned spot
                                    if (d < bd && toRoad < 1.2 && toRoad > -4) { bd = d; best = i; }
                                }
                    if (best >= 0 && Valid(upoles[best].x, upoles[best].z, .2f, out float py) && !Crowded(upoles[best].x, upoles[best].z, minD))
                    { c.gx = upoles[best].x; c.gz = upoles[best].z; c.y = py; c.type = 3; usedPole.Add(best); ok = true; nPole++; }
                }
                if (!ok)
                {
                    float rr = c.type == 0 ? .2f : .3f;
                    foreach (var (back, along) in c.type == 0 ? new[] { (0.0, 0.0), (0.0, 1.5), (0.0, -1.5), (0.0, 3.0), (0.0, -3.0) }
                        : new[] { (0.0, 0.0), (.5, 0.0), (1.1, 0.0), (0.0, 3.0), (0.0, -3.0), (.5, 3.0), (.5, -3.0), (0.0, 6.0), (0.0, -6.0) })
                    {
                        double gx = c.gx - gdir.x * back + gt.x * along, gz = c.gz - gdir.y * back + gt.y * along;
                        if (Crowded(gx, gz, minD)) break;
                        if (Valid(gx, gz, rr, out float y)) { c.gx = gx; c.gz = gz; c.y = y; ok = true; break; }
                    }
                }
                if (!ok) { nRejected++; continue; }
                int idx = acc.Count; acc.Add(c);
                var kk = ((int)Math.Floor(c.gx / 16), (int)Math.Floor(c.gz / 16)); if (!accGrid.TryGetValue(kk, out var al)) accGrid[kk] = al = new List<int>(); al.Add(idx);
            }

            // ---- scene: renderer, colliders, light pools
            var grp = Group("rasvjeta");
            var sl = grp.gameObject.AddComponent<Magistrala.StreetLights>();
            if (!AssetDatabase.IsValidFolder(LAMP_OUT)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Lights");
            var nearM = new Mesh[4]; var farM = new Mesh[4]; var heads = new List<Vector3>[4];
            for (int t = 0; t < 4; t++) { nearM[t] = LampMesh(t, true, out heads[t]); farM[t] = LampMesh(t, false, out _); }
            var galv = LampMat("LampGalv", new Color(.58f, .6f, .6f), .65f, .42f, false);
            var conc = LampMat("LampConcrete", new Color(.6f, .59f, .56f), 0, .12f, false);
            var lens = LampMat("LampLens", new Color(.78f, .78f, .74f), 0, .85f, true);
            sl.near = nearM; sl.far = farM; sl.mats = new[] { galv, lens, galv, lens, galv, lens, conc, lens };
            sl.lamps = acc.Select(c => { var u = W.U(c.gx, c.y, c.gz); return new Vector4(u.x, c.y, u.z, c.yawU); }).ToArray();
            sl.types = acc.Select(c => (byte)c.type).ToArray();
            var hw = new List<Vector3>();
            var poolCells = new Dictionary<(int, int), (List<Vector3> v, List<Vector2> uv, List<Color> col, List<int> tri)>();
            var colCells = new Dictionary<(int, int), GameObject>();
            var poolMat = AssetDatabase.LoadAssetAtPath<Material>(LAMP_OUT + "/LightPool.mat");
            if (!poolMat) { poolMat = new Material(Shader.Find("Magistrala/LightPool")); AssetDatabase.CreateAsset(poolMat, LAMP_OUT + "/LightPool.mat"); }
            sl.poolMat = poolMat;
            var lampColor = sl.lampColor;
            for (int i = 0; i < acc.Count; i++)
            {
                var c = acc[i]; var u = W.U(c.gx, c.y, c.gz); var rot = Quaternion.Euler(0, c.yawU, 0);
                var cell = ((int)Math.Floor(u.x / 128), (int)Math.Floor(u.z / 128));
                if (!colCells.TryGetValue(cell, out var cg))
                {
                    cg = new GameObject($"stupovi {cell.Item1},{cell.Item2}"); cg.transform.SetParent(grp, false); cg.transform.position = new Vector3(cell.Item1 * 128, 0, cell.Item2 * 128);
                    cg.isStatic = true; colCells[cell] = cg;
                }
                var cap = cg.AddComponent<CapsuleCollider>(); cap.direction = 1; cap.radius = LR0[c.type] + .05f; cap.height = LH[c.type];
                cap.center = new Vector3(u.x, u.y, u.z) - cg.transform.position + Vector3.up * LH[c.type] / 2;
                placed.Add((new Vector2(u.x, u.z), new Vector2(.3f, .3f), 0));
                foreach (var h in heads[c.type])
                {
                    var hp = u + rot * h; hw.Add(hp);
                    // the pool: an ellipse along the road, a little towards the road from under the head
                    var along = new Vector3(c.roadDir.x, 0, c.roadDir.y); var toRoad = rot * Vector3.forward; toRoad.y = 0; toRoad.Normalize();
                    if (c.type == 0) toRoad = (rot * new Vector3(0, 0, Mathf.Sign(h.z))).normalized;
                    var ctr = hp + toRoad * 1.2f; float R = LPOOL[c.type], A = R * 1.25f, B = R * .85f; const int N = 8;
                    if (!poolCells.TryGetValue(cell, out var pc)) poolCells[cell] = pc = (new List<Vector3>(), new List<Vector2>(), new List<Color>(), new List<int>());
                    int v0 = pc.v.Count; var okV = new bool[(N + 1) * (N + 1)];
                    var col = new Color(lampColor.r, lampColor.g, lampColor.b) * LSTR[c.type];
                    for (int a = 0; a <= N; a++)
                        for (int b = 0; b <= N; b++)
                        {
                            float fa = (a / (float)N) * 2 - 1, fb = (b / (float)N) * 2 - 1;
                            var p = ctr + along * fa * A + toRoad * fb * B;
                            float f = Mathf.Clamp01(1 - Mathf.Sqrt(fa * fa + fb * fb));
                            float gy = PoolGround(p.x, p.z, hp.y + 1, out bool okG);
                            okV[a * (N + 1) + b] = okG;
                            pc.v.Add(new Vector3(p.x, okG ? gy + .04f : hp.y, p.z)); pc.uv.Add(new Vector2(f, 0)); pc.col.Add(col);
                        }
                    for (int a = 0; a < N; a++)
                        for (int b = 0; b < N; b++)
                        {
                            int i0 = a * (N + 1) + b, i1 = i0 + 1, i2 = i0 + N + 1, i3 = i2 + 1;
                            if (!(okV[i0] && okV[i1] && okV[i2] && okV[i3])) continue;
                            if (pc.uv[v0 + i0].x + pc.uv[v0 + i1].x + pc.uv[v0 + i2].x + pc.uv[v0 + i3].x <= 0) continue;
                            pc.tri.AddRange(new[] { v0 + i0, v0 + i1, v0 + i3, v0 + i0, v0 + i3, v0 + i2 });
                        }
                }
            }
            sl.heads = hw.ToArray();
            foreach (var f in Directory.GetFiles(LAMP_OUT, "pool_*.asset")) AssetDatabase.DeleteAsset(f);
            var pr = new List<Renderer>();
            foreach (var kv in poolCells)
            {
                if (kv.Value.tri.Count == 0) continue;
                var m = new Mesh { name = $"pool_{kv.Key.Item1}_{kv.Key.Item2}", indexFormat = IndexFormat.UInt32 };
                m.SetVertices(kv.Value.v); m.SetUVs(0, kv.Value.uv); m.SetColors(kv.Value.col); m.SetTriangles(kv.Value.tri, 0); m.RecalculateBounds();
                AssetDatabase.CreateAsset(m, $"{LAMP_OUT}/{m.name}.asset");
                var go = new GameObject($"svjetlo {kv.Key.Item1},{kv.Key.Item2}"); go.transform.SetParent(grp, false); go.isStatic = true;
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = poolMat; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
                mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off; mr.enabled = false; pr.Add(mr);
            }
            sl.pools = pr.ToArray();
            EditorUtility.SetDirty(sl);
            var byType = new int[4]; int nMly = 0; foreach (var c in acc) { byType[c.type]++; if (c.mly) nMly++; }
            return $"[rasvjeta] {acc.Count} lamps: D8 double-arm {byType[0]}, single 9 m {byType[1]}, single 7 m {byType[2]}, concrete pole {byType[3]} (on a Mapillary utility pole {nPole}); " +
                $"Mapillary {nMly} / rule {acc.Count - nMly}; Mapillary lights {lights.Count}: median {nMlyMedian}, beside a street {nMlyEdge}, unmatched {nMlyNone}; " +
                $"candidates median {nMedMly} + {nMedRule} rule, edges {nEdgeMly} + {nEdgeRule} rule, rejected / merged {nRejected}; median {cols.Count} columns in {runs.Count} runs; " +
                $"{hw.Count} heads, {pr.Count} pool meshes; {sw.ElapsedMilliseconds / 1000.0:F1} s";
        }

        /// the ground under a pool vertex: the first hit from above that is not a prop / vehicle; ok = false on a building or nothing
        static float PoolGround(float x, float z, float from, out bool ok)
        {
            ok = false;
            var hits = Physics.RaycastAll(new Vector3(x, from + 2, z), Vector3.down, 40, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (Ignored(h.collider)) continue;
                if (HasAncestor(h.collider.transform, "Buildings") || h.collider is BoxCollider) return float.NaN;
                ok = true; return h.point.y;
            }
            return float.NaN;
        }

        static Material LampMat(string name, Color c, float metal, float smooth, bool lens)
        {
            string p = $"{LAMP_OUT}/{name}.mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth); m.enableInstancing = true;
            if (lens) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            EditorUtility.SetDirty(m); return m;
        }

        // ------------------------------------------------------------------ the lamp meshes (procedural, local: pole at the origin, arm to +z)
        class MB { public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<int>[] t = { new List<int>(), new List<int>() }; }
        static void Tube(MB b, IList<Vector3> path, IList<float> rad, int sides, bool cap)
        {
            int v0 = b.v.Count; Vector3 prevN = Vector3.zero;
            for (int i = 0; i < path.Count; i++)
            {
                var tg = (path[Mathf.Min(i + 1, path.Count - 1)] - path[Mathf.Max(i - 1, 0)]).normalized;
                var nn = i == 0 ? Vector3.Cross(tg, Mathf.Abs(tg.x) < .9f ? Vector3.right : Vector3.forward).normalized : Vector3.Cross(Vector3.Cross(tg, prevN), tg).normalized;
                prevN = nn; var bn = Vector3.Cross(tg, nn);
                for (int k = 0; k < sides; k++)
                {
                    float a = k * Mathf.PI * 2 / sides; var d = nn * Mathf.Cos(a) + bn * Mathf.Sin(a);
                    b.v.Add(path[i] + d * rad[i]); b.n.Add(d);
                }
            }
            for (int i = 0; i + 1 < path.Count; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a0 = v0 + i * sides + k, a1 = v0 + i * sides + (k + 1) % sides, b0 = a0 + sides, b1 = a1 + sides;
                    b.t[0].AddRange(new[] { a0, a1, b0, a1, b1, b0 });
                }
            if (cap)
            {
                int c = b.v.Count; var top = path[path.Count - 1]; var tg = (path[path.Count - 1] - path[path.Count - 2]).normalized;
                b.v.Add(top); b.n.Add(tg); int r0 = v0 + (path.Count - 1) * sides;
                for (int k = 0; k < sides; k++) { b.v.Add(b.v[r0 + k]); b.n.Add(tg); }
                for (int k = 0; k < sides; k++) b.t[0].AddRange(new[] { c, c + 1 + k, c + 1 + (k + 1) % sides });
            }
        }
        static void Box(MB b, int sm, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, Vector3 h, int lensSm = -1)
        {
            ax.Normalize(); ay.Normalize(); az.Normalize();
            var faces = new[] { (ax, ay, az, h.x, h.y, h.z), (-ax, ay, -az, h.x, h.y, h.z), (az, ay, -ax, h.z, h.y, h.x), (-az, ay, ax, h.z, h.y, h.x), (ay, az, ax, h.y, h.z, h.x), (-ay, -az, ax, h.y, h.z, h.x) };
            for (int f = 0; f < 6; f++)
            {
                var (n, u, v, dn, du, dv) = faces[f];
                // (n, u, v): u x v = n for a counter-clockwise front face
                if (Vector3.Dot(Vector3.Cross(u, v), n) < 0) v = -v;
                int s = f == 5 && lensSm >= 0 ? lensSm : sm;
                int i0 = b.v.Count; var o = c + n * dn;
                b.v.Add(o - u * du - v * dv); b.v.Add(o + u * du - v * dv); b.v.Add(o + u * du + v * dv); b.v.Add(o - u * du + v * dv);
                for (int k = 0; k < 4; k++) b.n.Add(n);
                b.t[s].AddRange(new[] { i0, i0 + 1, i0 + 2, i0, i0 + 2, i0 + 3 });
            }
        }
        static Mesh LampMesh(int type, bool full, out List<Vector3> heads)
        {
            var b = new MB(); heads = new List<Vector3>();
            float H = LH[type], L = LARM[type];
            int sides = full ? 12 : 4;
            // the pole: tapered (concrete: thicker), a base sleeve
            var pp = new List<Vector3>(); var pr = new List<float>();
            int segs = full ? 6 : 1;
            for (int i = 0; i <= segs; i++) { float t = i / (float)segs; pp.Add(Vector3.up * (t * H)); pr.Add(Mathf.Lerp(LR0[type], LR1[type], t)); }
            Tube(b, pp, pr, sides, true);
            if (full && type != 3) Tube(b, new[] { Vector3.zero, Vector3.up * .55f }, new[] { LR0[type] + .025f, LR0[type] + .02f }, sides, true);
            foreach (float side in type == 0 ? new[] { 1f, -1f } : new[] { 1f })
            {
                // the arm: from the pole's top out and slightly up (a bracket straight out on the concrete pole)
                var ap = new List<Vector3>(); var ar = new List<float>(); int n = full ? 8 : 3;
                float y0 = type == 3 ? H - 1.2f : H - .35f;
                for (int i = 0; i <= n; i++)
                {
                    float t = i / (float)n;
                    Vector3 p = type == 3 ? new Vector3(0, y0 + .25f * t, side * (LR1[type] + L * t))
                        : new Vector3(0, y0 + .35f * Mathf.Sin(t * Mathf.PI * .5f) + .2f * t, side * L * (1 - Mathf.Cos(t * Mathf.PI * .5f)) * 1f);
                    if (type != 3) p.z = side * L * Mathf.Pow(t, 1.3f);
                    ap.Add(p); ar.Add(Mathf.Lerp(type == 0 ? .05f : .04f, .032f, t));
                }
                Tube(b, ap, ar, full ? 8 : 3, false);
                // the LED head: a flat housing tilted 5° up, the lens its underside (submesh 1)
                var end = ap[ap.Count - 1]; var fwd = new Vector3(0, Mathf.Sin(5 * Mathf.Deg2Rad), side * Mathf.Cos(5 * Mathf.Deg2Rad)).normalized;
                var up = Vector3.Cross(fwd, new Vector3(side, 0, 0)) * side; if (up.y < 0) up = -up;
                float hl = type == 0 ? .36f : type == 1 ? .32f : .26f;
                var hc = end + fwd * (hl - .05f) - up * .01f;
                Box(b, 0, hc, new Vector3(1, 0, 0), up, fwd, new Vector3(type == 0 ? .17f : .14f, .045f, hl), 1);
                heads.Add(hc - up * .06f);
            }
            var m = new Mesh { name = $"lamp_{type}_{(full ? "near" : "far")}" };
            m.SetVertices(b.v); m.SetNormals(b.n); m.subMeshCount = 2; m.SetTriangles(b.t[0], 0); m.SetTriangles(b.t[1], 1);
            m.RecalculateTangents(); m.RecalculateBounds();
            string ap0 = $"{LAMP_OUT}/{m.name}.asset"; if (AssetDatabase.LoadMainAssetAtPath(ap0) != null) AssetDatabase.DeleteAsset(ap0); AssetDatabase.CreateAsset(m, ap0);
            return m;
        }
    }
}
