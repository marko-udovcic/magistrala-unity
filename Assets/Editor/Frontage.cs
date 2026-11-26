// Zidići i živice uz staru cestu (25. 11., Marko: "veliki nedostatak na staroj cesti: fale zidići i živice"): the property-front
// walls and hedges along the old coast road (Cesta dr. Franje Tuđmana, Štafilić → Sućurac) and the four link roads.
// Why Yards alone left the old road bare: Yards grows a ring 2.2–4.5 m round each house, so a house set back from the road gets
// its "street wall" in its own front garden, not at the road; on Marko's road tiles (s01–s13, c01–c08, junctions 19–21) every
// ring sample within 2.5 m of a model object (the tiles' asphalt, kerbs, sidewalks, barriers: WorldData.FootDist) is dropped,
// and the houses within 1.5 m of one are not built at all; in the old cores (Kambelovac, Gomilica, Sućurac: BuildingsFromTiles.
// Cores) the houses get no yard. Here the frontage is found from the road instead:
//   stations every 1 m along the road's axis (the tiles' own `roads`; east of Lukšić the street graph); per side, walking out
//   from the asphalt edge on 0.25 m steps over everything paved (the scene's World/Roads, World/Streets and World/Riva colliders:
//   model asphalt and sidewalks, our sidewalks / courts, the carriageways of StreetsBuilder) to the first soft ground: the
//   property line, the wall 0.2 m behind it. No wall where the paving goes on for more than 9 m (a square, a court, a parking,
//   a side street), where a house stands at the line (its facade is the frontage), within 10 m of a stop / delivery point or on
//   its bay, on a slope, or where no house (kind house / apartment) stands behind the line within 30 m.
//   The line is smoothed (median over ±3 m) and cut into plots by the house behind it (its owner); each plot is one property:
//   its type (below), piers at its ends, a driveway gate (3 m, iron, with piers; sometimes two clipped cypresses inside) or a
//   pedestrian gate (1.1 m). Emitted with the owner house (BuildingsFromTiles' house loop) into its cell / chunk meshes by
//   HouseBuilder (its look: a plastered wall takes the house's paint), so it costs no draw call; the hull level is the
//   collider (CellMerge merges it per cell) and the shadow. Yards then leaves the street side of a house to the frontage.
// Plot types (taste: Kaštela's old road as on the Mapillary sequences — plastered walls with a concrete coping and dry-stone /
// rubble walls in about equal parts, many low walls with an iron railing, hedges on or behind a good third of them; a hedge is
// forced where the orthophoto shows green on the line, Mapillary saw a hedge or the ground build has a hedge strip):
//   P plastered wall + coping 1.0–1.5 m, S rubble wall (Poly Haven stone_wall layer) 0.8–1.3 m, R low wall 0.75 m + railing,
//   SH rubble wall 0.6–1.1 m with a clipped hedge growing on it (0.8–1.4 m above, hanging over its face), PH plastered wall with a
//   hedge behind and over it, RH railing wall with a hedge behind, H a hedge alone (1.6–2.3 m).
// Hedge species per plot: pittosporum 35 %, laurel 20 %, privet 12 %, oleander 18 % (pink 2 : white 1), cypress 15 %.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Magistrala.Game;
using UnityEngine;
using V = Magistrala.EditorTools.BuildingsFromTiles.Vector2d;

namespace Magistrala.EditorTools
{
    public static class Frontage
    {
        /// false: the old behaviour (Yards only) — "before" measurements
        public static bool On = true;
        const double STEP = .25, MAX_PAVE = 9, OWNER_R = 30, STOP_R = 10;
        /// no sidewalk: the wall's distance from the asphalt's edge (a kerb is a step of >= KERB m up from the asphalt); a wall line
        /// keeps one offset while it stays within LEVEL m of it, then steps square in a pier (Level)
        const double VERGE = .8, LEVEL = .2; const float KERB = .08f;
        public static int nVerge, nSteps;
        public const int TP = 0, TS = 1, TR = 2, TSH = 3, TPH = 4, TRH = 5, TH = 6;
        public static readonly string[] TypeNames = { "zid + kapa", "kamen", "zid + ograda", "kamen + zivica na njemu", "zid + zivica iza", "ograda + zivica iza", "zivica" };

        public struct House { public List<V> P; public double cx, cz, baseY; public bool house; }
        static List<ModelJ> models; static List<House> H; static Dictionary<(int, int), List<int>> hGrid;
        static (int, int) K16(double x, double z) => ((int)Math.Floor(x / 16), (int)Math.Floor(z / 16));

        // ------------------------------------------------------------------ lines
        /// the roads whose frontage is built: Marko's old-road / link-road tiles and the junctions on the old road, then the
        /// old road east of the tiles (graph); (x, z) polylines in the game frame with the asphalt's half width
        static List<(List<double[]> p, double hw)> Lines()
        {
            var L = new List<(List<double[]>, double)>();
            foreach (var m in models)
            {
                bool want = m.key.StartsWith("dionica-s") || m.key == "krizanje-19" || m.key == "krizanje-20" || m.key == "krizanje-21";
                if (m.key.StartsWith("dionica-c")) { int n; want = int.TryParse(m.key.Substring(9), out n) && n >= 1 && n <= 8; }
                if (!want) continue;
                foreach (var r in m.roads) if (r.p.Count >= 2) L.Add((r.p, r.w / 2));
            }
            foreach (var (p, hw) in OldRoadGraph(-6200, -300)) L.Add((p, hw));
            return L;
        }
        /// the old road in the street graph (Cesta dr. Franje Tuđmana) within game x0..x1
        static List<(List<double[]> p, double hw)> OldRoadGraph(double x0, double x1)
        {
            var L = new List<(List<double[]>, double)>();
            var g = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "graph.json")));
            foreach (var eo in MiniJson.L(g["edges"]))
            {
                var e = MiniJson.O(eo); if (Convert.ToString(MiniJson.G(e, "name")) != "Cesta dr. Franje Tuđmana" || Convert.ToString(e["id"], System.Globalization.CultureInfo.InvariantCulture) == "1088") continue;
                var pts = new List<double[]>(); bool any = false;
                foreach (var q in MiniJson.L(e["points"])) { var qq = MiniJson.L(q); double x = MiniJson.D(qq[0]), z = MiniJson.D(qq[2]); pts.Add(new[] { x, z }); if (x >= x0 && x <= x1) any = true; }
                if (any && pts.Count >= 2) L.Add((pts, MiniJson.D(MiniJson.G(e, "width") ?? 7.0) / 2));
            }
            return L;
        }

        // ------------------------------------------------------------------ paving
        static Dictionary<int, bool> paveCol; static readonly RaycastHit[] hits = new RaycastHit[24];
        static bool IsPave(Collider c)
        {
            if (paveCol.TryGetValue(c.GetInstanceID(), out var b)) return b;
            b = false; for (var t = c.transform; t != null; t = t.parent)
                if (t.parent != null && t.parent.name == "World") { b = t.name == "Roads" || t.name == "Streets" || t.name == "Riva"; break; }
            paveCol[c.GetInstanceID()] = b; return b;
        }
        /// paved at game (x, z): a model / street / riva surface, or a graph carriageway; y = its top (NaN when only the graph says so)
        static bool Paved(double x, double z, out float y)
        {
            y = float.NaN;
            var u = W.U(x, 0, z);
            int n = Physics.RaycastNonAlloc(new Vector3(u.x, 500, u.z), Vector3.down, hits, 1000, ~0, QueryTriggerInteraction.Ignore);
            bool p = false;
            for (int i = 0; i < n; i++) if (IsPave(hits[i].collider)) { p = true; if (float.IsNaN(y) || hits[i].point.y > y) y = hits[i].point.y; }
            if (!p && Yards.Clear(x, z).clear < 0) p = true;
            return p;
        }

        // ------------------------------------------------------------------ owners
        static double PolyDist(List<V> P, double x, double z)
        {
            if (BuildingsFromTiles.InPoly(P, x, z)) return 0;
            double best = 1e9;
            for (int i = 0; i < P.Count; i++) { var a = P[i]; var b = P[(i + 1) % P.Count]; best = Math.Min(best, Seg(x, z, a.x, a.z, b.x, b.z)); }
            return best;
        }
        static double Seg(double px, double pz, double ax, double az, double bx, double bz)
        {
            double dx = bx - ax, dz = bz - az, L = dx * dx + dz * dz, t = L < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * dx + (pz - az) * dz) / L));
            double qx = ax + t * dx - px, qz = az + t * dz - pz; return Math.Sqrt(qx * qx + qz * qz);
        }
        /// the house behind a wall point (x, z; inward n, along u): the nearest footprint overlapping ±4 m along within OWNER_R
        /// behind; atLine: a building reaches the line (within 0.9 m)
        static int Owner(double x, double z, double ux, double uz, double nx, double nz, out bool atLine)
        {
            atLine = false; int best = -1; double bestC = OWNER_R;
            var seen = new HashSet<int>();
            var k0 = K16(x + nx * 15, z + nz * 15);
            for (int i = -2; i <= 2; i++) for (int j = -2; j <= 2; j++)
                {
                    if (!hGrid.TryGetValue((k0.Item1 + i, k0.Item2 + j), out var l)) continue;
                    foreach (int id in l)
                    {
                        if (!seen.Add(id)) continue;
                        var P = H[id].P; double a0 = 1e9, a1 = -1e9, c0 = 1e9, c1 = -1e9;
                        foreach (var p in P) { double dx = p.x - x, dz = p.z - z, a = dx * ux + dz * uz, c = dx * nx + dz * nz; a0 = Math.Min(a0, a); a1 = Math.Max(a1, a); c0 = Math.Min(c0, c); c1 = Math.Max(c1, c); }
                        if (a0 > 4 || a1 < -4 || c1 < -.5) continue;
                        if (c0 < .9 && PolyDist(P, x, z) < 1.5) { atLine = true; continue; }
                        if (c0 < .9) c0 = PolyDist(P, x, z);
                        if (!H[id].house || c0 > bestC) continue;
                        bestC = c0; best = id;
                    }
                }
            return best;
        }

        // ------------------------------------------------------------------ plots
        public class Plot
        {
            public int owner, type, road, side; public HedgeShell.Sp sp; public int seed;
            public float wallH, hedgeH, hedgeW; public bool galv;
            /// sub-runs (game x, z, inward normal, base y), with piers at their ends, and the gates between them
            public List<List<(double x, double z, double nx, double nz, float y)>> runs = new List<List<(double, double, double, double, float)>>();
            public List<(bool a, bool b)> piers = new List<(bool, bool)>();
            public List<(int run, bool drive, bool cypress)> gates = new List<(int, bool, bool)>();
            /// piers where the wall steps to a new line (game x, z, along the road, base y)
            public List<(double x, double z, double ux, double uz, float y)> steps = new List<(double, double, double, double, float)>();
        }
        static Dictionary<int, List<Plot>> byOwner = new Dictionary<int, List<Plot>>();
        public static int nPlots, nGates, nCypress; public static double[] metres = new double[7];
        public static readonly Dictionary<string, int> skip = new Dictionary<string, int>();
        static void Skip(string k) { skip.TryGetValue(k, out var n); skip[k] = n + 1; }

        /// the frontage of every target road, after Yards.Begin (carriageways) and with the houses BuildingsFromTiles will build
        /// (index = its B index); the scene's road / street colliders must be there
        public static void Prepare(List<ModelJ> m, List<House> houses)
        {
            models = m; H = houses; byOwner.Clear(); fGrid.Clear(); segs.Clear(); sGrid.Clear(); skip.Clear(); oldGrid = null;
            nPlots = nGates = nCypress = nVerge = nSteps = 0; metres = new double[7];
            hGrid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < H.Count; i++)
            {
                double x0 = 1e9, x1 = -1e9, z0 = 1e9, z1 = -1e9; foreach (var p in H[i].P) { x0 = Math.Min(x0, p.x); x1 = Math.Max(x1, p.x); z0 = Math.Min(z0, p.z); z1 = Math.Max(z1, p.z); }
                for (int gx = (int)Math.Floor(x0 / 16); gx <= (int)Math.Floor(x1 / 16); gx++) for (int gz = (int)Math.Floor(z0 / 16); gz <= (int)Math.Floor(z1 / 16); gz++)
                    { if (!hGrid.TryGetValue((gx, gz), out var l)) hGrid[(gx, gz)] = l = new List<int>(); l.Add(i); }
            }
            if (!On) return;
            paveCol = new Dictionary<int, bool>(); Physics.SyncTransforms();
            var stops = new List<GStop>(); var game = Resources.Load<GameWorld>(GameWorld.ResourcePath); if (game != null) stops.AddRange(game.stops);
            var lines = Lines();
            // stations (1 m), a station dropped where an earlier line already runs within 3.5 m in about the same direction
            var stGrid = new Dictionary<(int, int), List<(double x, double z, double ux, double uz)>>();
            int road = 0, nSt = 0;
            foreach (var (pl, hw) in lines)
            {
                var S = new List<(double x, double z, double ux, double uz)>();
                double acc = 0, next = 0;
                for (int i = 0; i + 1 < pl.Count; i++)
                {
                    double ax = pl[i][0], az = pl[i][1], bx = pl[i + 1][0], bz = pl[i + 1][1], L = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)); if (L < .01) continue;
                    double ux = (bx - ax) / L, uz = (bz - az) / L;
                    while (next <= acc + L) { double t = next - acc; S.Add((ax + ux * t, az + uz * t, ux, uz)); next += 1; }
                    acc += L;
                }
                var keep = new List<(double x, double z, double ux, double uz)>();
                foreach (var s in S)
                {
                    bool dup = false; var k = ((int)Math.Floor(s.x / 8), (int)Math.Floor(s.z / 8));
                    for (int i = -1; i <= 1 && !dup; i++) for (int j = -1; j <= 1 && !dup; j++)
                            if (stGrid.TryGetValue((k.Item1 + i, k.Item2 + j), out var l))
                                foreach (var q in l) if ((q.x - s.x) * (q.x - s.x) + (q.z - s.z) * (q.z - s.z) < 12.25 && Math.Abs(q.ux * s.ux + q.uz * s.uz) > .8) { dup = true; break; }
                    keep.Add(dup ? (double.NaN, 0, 0, 0) : s);
                }
                foreach (var s in keep) if (!double.IsNaN(s.x)) { var k = ((int)Math.Floor(s.x / 8), (int)Math.Floor(s.z / 8)); if (!stGrid.TryGetValue(k, out var l)) stGrid[k] = l = new List<(double, double, double, double)>(); l.Add(s); nSt++; }
                for (int side = -1; side <= 1; side += 2) Side(keep, hw, side, road, stops);
                road++;
            }
            Debug.Log($"[zivice] frontage: {lines.Count} lines, {nSt} stations, {nPlots} plots, {nGates} gates ({nCypress} with cypresses), {nSteps} square steps, {nVerge} samples set {VERGE} m off a kerbless edge; skipped samples: " + string.Join(", ", skip.Select(kv => $"{kv.Key} {kv.Value}")));
        }

        static void Side(List<(double x, double z, double ux, double uz)> S, double hw, int side, int road, List<GStop> stops)
        {
            int N = S.Count; if (N < 4) return;
            var off = new double[N]; var own = new int[N]; var ys = new float[N]; var ok = new bool[N];
            for (int i = 0; i < N; i++)
            {
                var s = S[i]; if (double.IsNaN(s.x)) continue;
                double nx = -s.uz * side, nz = s.ux * side;          // inward: away from the road on this side
                // the property line: the first 0.75 m of soft ground past the paving
                double t = Math.Max(1, hw - .5), edge = double.NaN; int soft = 0; float topY = float.NaN, roadY = float.NaN, hiY = float.NaN;
                for (; t <= hw + MAX_PAVE; t += STEP)
                {
                    if (Paved(s.x + nx * t, s.z + nz * t, out var py)) { soft = 0; if (!float.IsNaN(py)) { topY = py; if (float.IsNaN(roadY)) roadY = py; hiY = float.IsNaN(hiY) ? py : Math.Max(hiY, py); } }
                    else if (++soft == 3) { edge = t - 2 * STEP; break; }
                }
                if (double.IsNaN(edge)) { Skip("paving > 9 m"); continue; }
                // no kerb on the way out (the asphalt runs straight into the soil: no sidewalk): the wall stands VERGE m off its
                // edge, on a strip of soil, not on the asphalt's ragged rim (26. 11., Štafilić)
                bool verge = !float.IsNaN(roadY) && hiY - roadY < KERB;
                double o = edge + (verge ? VERGE : .2), x = s.x + nx * o, z = s.z + nz * o;
                // (no FootDist test here: the tiles' 2 m grid counts the whole sidewalk cell as an object; the paving scan above has them)
                bool nearStop = false; var u = W.U(x, 0, z);
                foreach (var st in stops) { var d = st.pos - u; d.y = 0; if (d.sqrMagnitude < STOP_R * STOP_R) { nearStop = true; break; } var db = st.bay - u; db.y = 0; if (st.bayHl > 0 && db.sqrMagnitude < (st.bayHl + 4) * (st.bayHl + 4)) { nearStop = true; break; } }
                if (nearStop) { Skip("stop / bay"); continue; }
                int ow = Owner(x, z, s.ux, s.uz, nx, nz, out bool atLine);
                if (atLine) { Skip("house at the line"); continue; }
                if (ow < 0) { Skip("no house behind"); continue; }
                double gy = BuildingsFromTiles.Ground(models, x, z);
                float y = (float)gy; if (!float.IsNaN(topY) && topY > y && topY - y < .6f) y = topY;
                if (Math.Abs(gy - H[ow].baseY) > 3) { Skip("slope"); continue; }
                if (NearWall(x, z, 1.0)) { Skip("other road's wall"); continue; }
                off[i] = o; own[i] = ow; ys[i] = y; ok[i] = true; if (verge) nVerge++;
            }
            // smoothing: the median offset over ±3 m
            var off2 = (double[])off.Clone();
            for (int i = 0; i < N; i++)
            {
                if (!ok[i]) continue; var w = new List<double>();
                for (int k = Math.Max(0, i - 3); k <= Math.Min(N - 1, i + 3); k++) if (ok[k]) w.Add(off[k]);
                w.Sort(); off2[i] = w[w.Count / 2];
            }
            // runs → plots (owner changes; slivers under 3 m join the plot before)
            int a = 0;
            while (a < N)
            {
                if (!ok[a]) { a++; continue; }
                int b = a; while (b + 1 < N && ok[b + 1] && Math.Abs(off2[b + 1] - off2[b]) < .6) b++;
                if (b - a + 1 >= 3)
                {
                    Level(off2, a, b);
                    var cuts = new List<(int s, int e, int owner)>();
                    int c = a;
                    while (c <= b)
                    {
                        int d = c; while (d + 1 <= b && own[d + 1] == own[c]) d++;
                        if (cuts.Count > 0 && d - c + 1 < 3) cuts[cuts.Count - 1] = (cuts[cuts.Count - 1].s, d, cuts[cuts.Count - 1].owner);
                        else cuts.Add((c, d, own[c]));
                        c = d + 1;
                    }
                    if (cuts.Count > 1 && cuts[0].e - cuts[0].s + 1 < 3) { cuts[1] = (cuts[0].s, cuts[1].e, cuts[1].owner); cuts.RemoveAt(0); }
                    for (int q = 0; q < cuts.Count; q++)
                        MakePlot(S, off2, ys, side, road, cuts[q].s, cuts[q].e, cuts[q].owner, q > 0, q + 1 < cuts.Count);
                }
                a = b + 1;
            }
        }

        /// one line per stretch: within a run the wall keeps one offset (parallel to the road) while the property line stays within
        /// ±LEVEL of it, then steps square to the next one (a jog in a pier, MakePlot) – a line that drifts by more than ~0.6 m over
        /// a few metres was a chain of short skewed pieces (26. 11., zivica_closeup_3m.jpg); pieces under 3 m join the neighbour
        /// whose line is nearer, steps under 8 cm are no step
        static void Level(double[] off, int a, int b)
        {
            var seg = new List<(int s, int e, double v)>();
            for (int s0 = a; s0 <= b;)
            {
                int e = s0; double lo = off[s0], hi = off[s0];
                while (e + 1 <= b && Math.Max(hi, off[e + 1]) - Math.Min(lo, off[e + 1]) <= 2 * LEVEL) { e++; lo = Math.Min(lo, off[e]); hi = Math.Max(hi, off[e]); }
                var w = new List<double>(); for (int i = s0; i <= e; i++) w.Add(off[i]); w.Sort();
                seg.Add((s0, e, w[w.Count / 2])); s0 = e + 1;
            }
            for (bool again = true; again && seg.Count > 1;)
            {
                again = false;
                for (int k = 0; k < seg.Count; k++)
                {
                    bool shortK = seg[k].e - seg[k].s + 1 < 3;
                    int j = k == 0 ? 1 : k == seg.Count - 1 ? k - 1 : Math.Abs(seg[k - 1].v - seg[k].v) <= Math.Abs(seg[k + 1].v - seg[k].v) ? k - 1 : k + 1;
                    if (!shortK && Math.Abs(seg[j].v - seg[k].v) >= .08) continue;
                    int lo = Math.Min(j, k), hi = Math.Max(j, k);
                    double v = seg[j].e - seg[j].s >= seg[k].e - seg[k].s ? seg[j].v : seg[k].v;
                    seg[lo] = (seg[lo].s, seg[hi].e, v); seg.RemoveAt(hi); again = true; break;
                }
            }
            foreach (var g in seg) for (int i = g.s; i <= g.e; i++) off[i] = g.v;
        }

        static void MakePlot(List<(double x, double z, double ux, double uz)> S, double[] off, float[] ys, int side, int road, int i0, int i1, int owner, bool joinA, bool joinB)
        {
            var hs = H[owner]; int seed = (int)(Math.Abs(hs.cx * 7.31 + hs.cz * 3.17 + road * 101 + side * 13) % 100000);
            var r = new System.Random(seed * 17 + 3);
            var pts = new List<(double x, double z, double nx, double nz, float y)>();
            var jogs = new List<(int k, double ux, double uz)>();   // pts[k] → pts[k + 1]: a square step to the next line (Level)
            for (int i = i0; i <= i1; i++)
            {
                var s = S[i]; double nx = -s.uz * side, nz = s.ux * side;
                if (i > i0 && Math.Abs(off[i] - off[i - 1]) > 1e-6) { jogs.Add((pts.Count, s.ux, s.uz)); pts.Add((s.x + nx * off[i - 1], s.z + nz * off[i - 1], nx, nz, ys[i])); }
                pts.Add((s.x + nx * off[i], s.z + nz * off[i], nx, nz, ys[i]));
            }
            // plots that touch meet half way (one pier there: the plot before has it)
            if (joinB && i1 + 1 < S.Count) { var p0 = pts[pts.Count - 1]; var s1 = S[i1 + 1]; double nx = -s1.uz * side, nz = s1.ux * side; pts.Add(((p0.x + s1.x + nx * off[i1 + 1]) / 2, (p0.z + s1.z + nz * off[i1 + 1]) / 2, p0.nx, p0.nz, (p0.y + ys[i1 + 1]) / 2)); }
            if (joinA && i0 > 0) { var p0 = pts[0]; var s1 = S[i0 - 1]; double nx = -s1.uz * side, nz = s1.ux * side; pts.Insert(0, ((p0.x + s1.x + nx * off[i0 - 1]) / 2, (p0.z + s1.z + nz * off[i0 - 1]) / 2, p0.nx, p0.nz, (p0.y + ys[i0 - 1]) / 2)); for (int q = 0; q < jogs.Count; q++) jogs[q] = (jogs[q].k + 1, jogs[q].ux, jogs[q].uz); }
            // evidence of a hedge along the plot
            int nMly = 0, nGc = 0, nGreen = 0;
            foreach (var p in pts)
            {
                if (Yards.NearMlyHedgeP(p.x + p.nx * 1.5, p.z + p.nz * 1.5, 4)) nMly++;
                if (Yards.NearGcHedgeP(p.x + p.nx * 1, p.z + p.nz * 1, 2)) nGc++;
                if (Yards.GreenP(p.x + p.nx * .4, p.z + p.nz * .4, -p.nx, -p.nz)) nGreen++;
            }
            bool hedgeEv = nMly * 4 >= pts.Count || nGc * 4 >= pts.Count || nGreen * 2 >= pts.Count;
            var pl = new Plot { owner = owner, road = road, side = side, seed = seed };
            double rt = r.NextDouble();
            (int t, double w)[] W0 = hedgeEv
                ? new[] { (TSH, .32), (TPH, .2), (TRH, .18), (TH, .18), (TP, .05), (TS, .04), (TR, .03) }
                : new[] { (TP, .28), (TS, .19), (TR, .19), (TSH, .14), (TPH, .07), (TRH, .07), (TH, .06) };
            double acc = 0; pl.type = W0[W0.Length - 1].t; foreach (var e in W0) { acc += e.w; if (rt < acc) { pl.type = e.t; break; } }
            double rs = r.NextDouble();
            pl.sp = rs < .35 ? HedgeShell.Sp.Pittosporum : rs < .55 ? HedgeShell.Sp.Laurel : rs < .67 ? HedgeShell.Sp.Privet : rs < .79 ? HedgeShell.Sp.Oleander : rs < .85 ? HedgeShell.Sp.OleanderWhite : HedgeShell.Sp.Cypress;
            pl.wallH = pl.type == TP ? 1.0f + .5f * (float)r.NextDouble() : pl.type == TS ? .8f + .5f * (float)r.NextDouble() : pl.type == TSH ? .6f + .5f * (float)r.NextDouble()
                : pl.type == TPH ? .9f + .3f * (float)r.NextDouble() : .75f;
            pl.hedgeH = pl.type == TSH ? .8f + .6f * (float)r.NextDouble() : pl.type == TPH ? .9f + .5f * (float)r.NextDouble() : pl.type == TRH ? .3f + .5f * (float)r.NextDouble() : 1.6f + .7f * (float)r.NextDouble();
            if (pl.sp == HedgeShell.Sp.Cypress && pl.type != TSH) pl.hedgeH += .4f;
            pl.hedgeW = .7f + .35f * (float)r.NextDouble(); pl.galv = r.NextDouble() < .4;
            // gates: a driveway (3 m) on longer plots, else a pedestrian gate (1.1 m), at the owner's middle where it fits
            int N = pts.Count; double len = N - 1;
            int gw = 0; bool drive = false;
            if (len >= 8 && r.NextDouble() < .6) { gw = 3; drive = true; } else if (len >= 4.5 && r.NextDouble() < .75) gw = 1;
            // gate: a driveway takes 2 stations out (3 m opening), a pedestrian gate 1 (2 m) and its runs reach 0.45 m into it (1.1 m)
            var cut = new bool[N]; int gs = -1, take = drive ? 2 : 1;
            if (gw > 0)
            {
                // at the house's projection on the plot, kept 2 m off the plot's ends
                double best = 1e9; int bi = N / 2;
                for (int i = 0; i < N; i++) { double d = (pts[i].x - hs.cx) * (pts[i].x - hs.cx) + (pts[i].z - hs.cz) * (pts[i].z - hs.cz); if (d < best) { best = d; bi = i; } }
                gs = Math.Max(2, Math.Min(N - 3 - take, bi + r.Next(-2, 3) - take / 2));
                // not in a step: the runs at the gate end along the road (the pedestrian gate's runs reach on along their last metre)
                bool InJog(int g0) => jogs.Any(j => j.k + 1 >= g0 - 1 && j.k <= g0 + take + 2);
                if (gs >= 0 && InJog(gs)) { int alt = -1; for (int dd = 1; dd <= 4 && alt < 0; dd++) foreach (int c in new[] { gs - dd, gs + dd }) if (alt < 0 && c >= 2 && c + take <= N - 3 && !InJog(c)) alt = c; gs = alt; }
                if (gs < 0 || gs + take > N - 3) gs = -1; else for (int k = 1; k <= take; k++) cut[gs + k] = true;
            }
            var cur = new List<(double x, double z, double nx, double nz, float y)>();
            for (int i = 0; i < N; i++)
            {
                if (cut[i]) { if (cur.Count >= 2) pl.runs.Add(cur); cur = new List<(double, double, double, double, float)>(); continue; }
                cur.Add(pts[i]);
            }
            if (cur.Count >= 2) pl.runs.Add(cur);
            if (pl.runs.Count == 0) return;
            if (gs >= 0 && pl.runs.Count == 2)
            {
                if (!drive)
                {
                    // the runs' ends at the gate 0.45 m further (along their own last metre)
                    var A = pl.runs[0]; var a1 = A[A.Count - 1]; var a0 = A[A.Count - 2];
                    A[A.Count - 1] = (a1.x + (a1.x - a0.x) * .45, a1.z + (a1.z - a0.z) * .45, a1.nx, a1.nz, a1.y);
                    var B = pl.runs[1]; var b0 = B[0]; var b1 = B[1];
                    B[0] = (b0.x + (b0.x - b1.x) * .45, b0.z + (b0.z - b1.z) * .45, b0.nx, b0.nz, b0.y);
                }
                bool cy = drive && r.NextDouble() < (pl.type >= TSH ? .3 : .12); pl.gates.Add((0, drive, cy)); nGates++; if (cy) nCypress++;
            }
            for (int k = 0; k < pl.runs.Count; k++) pl.piers.Add((k > 0 || !joinA, true));
            // a pier in every step: one over a short step, one on each corner of a longer one
            foreach (var (k, ux, uz) in jogs)
            {
                if (cut[k] || cut[k + 1]) continue;
                var c1 = pts[k]; var c2 = pts[k + 1]; double d = Math.Sqrt((c2.x - c1.x) * (c2.x - c1.x) + (c2.z - c1.z) * (c2.z - c1.z));
                if (d <= .5) pl.steps.Add(((c1.x + c2.x) / 2, (c1.z + c2.z) / 2, ux, uz, Math.Min(c1.y, c2.y)));
                else { pl.steps.Add((c1.x, c1.z, ux, uz, c1.y)); pl.steps.Add((c2.x, c2.z, ux, uz, c2.y)); }
                nSteps++;
            }
            if (!byOwner.TryGetValue(owner, out var l)) byOwner[owner] = l = new List<Plot>();
            l.Add(pl); nPlots++;
            foreach (var run in pl.runs) foreach (var p in run) AddWallPoint(p.x, p.z);
        }

        // ------------------------------------------------------------------ the frontage's points (Yards keeps its rings off them)
        static readonly Dictionary<(int, int), List<(double x, double z)>> fGrid = new Dictionary<(int, int), List<(double, double)>>();
        static (int, int) K8(double x, double z) => ((int)Math.Floor(x / 8), (int)Math.Floor(z / 8));
        static void AddWallPoint(double x, double z) { var k = K8(x, z); if (!fGrid.TryGetValue(k, out var l)) fGrid[k] = l = new List<(double, double)>(); l.Add((x, z)); }
        static bool NearWall(double x, double z, double r)
        {
            var k = K8(x, z);
            for (int i = -1; i <= 1; i++) for (int j = -1; j <= 1; j++)
                    if (fGrid.TryGetValue((k.Item1 + i, k.Item2 + j), out var l)) foreach (var p in l) if ((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < r * r) return true;
            return false;
        }
        /// a yard-ring sample (game x, z, outward normal) the frontage takes over: on it, or facing it across the front garden
        public static bool Claims(double x, double z, double nx, double nz)
        {
            if (fGrid.Count == 0) return false;
            if (NearWall(x, z, 2.5)) return true;
            for (double t = 1; t <= 14; t += 1) if (NearWall(x + nx * t, z + nz * t, 1.3)) return true;
            return false;
        }

        // ------------------------------------------------------------------ emission (with the owner house)
        static Vector3 Inward(double nx, double nz) => W.U(nx, 0, nz) - W.U(0, 0, 0);
        public static void Emit(int owner, HouseGeo near, HouseGeo mid, HouseGeo hull)
        {
            if (!On || !byOwner.TryGetValue(owner, out var plots)) return;
            foreach (var pl in plots)
            {
                var tint = HedgeTint(pl.sp, pl.seed);
                var R = new List<List<Vector3>>(); var side = new List<float>();
                foreach (var run in pl.runs)
                {
                    var raw = run.Select(p => W.U(p.x, p.y, p.z)).ToList();
                    R.Add(Simplify(raw));
                    Vector3 e = raw[raw.Count - 1] - raw[0]; e.y = 0; var inw = Inward(run[0].nx, run[0].nz);
                    side.Add(Vector3.Dot(new Vector3(-e.z, 0, e.x), inw) >= 0 ? 1 : -1);
                }
                for (int lod = 0; lod < 3; lod++)
                {
                    var g = lod == 0 ? near : lod == 1 ? mid : hull; if (g == null) continue;
                    HouseBuilder.Lod = lod; bool piers = lod < 2;
                    for (int k = 0; k < R.Count; k++)
                    {
                        var pts = R[k]; bool pa = piers && pl.piers[k].a && pl.type != TH, pb = piers && pl.piers[k].b && pl.type != TH;
                        float s = side[k]; int sd = pl.seed * 3 + k;
                        switch (pl.type)
                        {
                            case TP: HouseBuilder.YardWall(g, pts, pl.wallH, 1, pa, pb); break;
                            case TS: HouseBuilder.YardWall(g, pts, pl.wallH, 0, pa, pb); break;
                            case TR: HouseBuilder.YardWall(g, pts, .75f, 2, pa, pb, 1.59f); break;
                            case TSH:
                                HouseBuilder.YardWall(g, pts, pl.wallH, 0, pa, pb, pl.wallH + .2f);
                                HouseBuilder.YardHedge(g, pts, s, -.27f, pl.hedgeW, pl.wallH + pl.hedgeH, tint, pl.wallH - .12f, pl.sp, .3f, sd);
                                break;
                            case TPH:
                                HouseBuilder.YardWall(g, pts, pl.wallH, 1, pa, pb);
                                HouseBuilder.YardHedge(g, pts, s, -.1f, pl.hedgeW, pl.wallH + pl.hedgeH, tint, -.2f, pl.sp, 0, sd);
                                break;
                            case TRH:
                                HouseBuilder.YardWall(g, pts, .75f, 2, pa, pb, 1.59f);
                                HouseBuilder.YardHedge(g, pts, s, .3f, pl.hedgeW, 1.59f + pl.hedgeH, tint, -.2f, pl.sp, 0, sd);
                                break;
                            default:
                                HouseBuilder.YardHedge(g, pts, s, -.05f, pl.hedgeW + .15f, pl.hedgeH, tint, -.2f, pl.sp, 0, sd);
                                break;
                        }
                    }
                    // the piers in the steps of the line
                    if (pl.type != TH)
                        foreach (var st in pl.steps)
                        {
                            int kind = pl.type == TS || pl.type == TSH ? 0 : pl.type == TR || pl.type == TRH ? 2 : 1;
                            float pier = pl.type == TR || pl.type == TRH ? 1.59f : pl.type == TSH ? pl.wallH + .2f : 0;
                            HouseBuilder.YardPier(g, W.U(st.x, st.y, st.z), Inward(st.ux, st.uz), pl.wallH, kind, pier);
                        }
                    // gates (closed, iron) between the runs, and the cypresses inside a driveway
                    foreach (var (run, drive, cy) in pl.gates)
                    {
                        if (run + 1 >= R.Count) continue;
                        var a = R[run][R[run].Count - 1]; var b = R[run + 1][0];
                        float gh = drive ? 1.5f : 1.4f; if (pl.type == TR || pl.type == TRH) gh = 1.55f;
                        HouseBuilder.YardGate(g, a, b, gh);
                        Yards.FrontGates.Add((a + b) / 2);
                        if (cy)
                        {
                            var inw = Inward(pl.runs[run][0].nx, pl.runs[run][0].nz);
                            var e = (b - a); e.y = 0; e.Normalize();
                            var cr = new System.Random(pl.seed + 77); float ch = 4.5f + 3f * (float)cr.NextDouble(), rr = HedgeShell.PILLAR_R * ch / HedgeShell.PILLAR_H;
                            var cyTint = HedgeTint(HedgeShell.Sp.Cypress, pl.seed);
                            HouseBuilder.HedgePillar(g, a - e * .9f + inw * .8f, rr, ch, cyTint, HedgeShell.Sp.Cypress, pl.seed + 1);
                            HouseBuilder.HedgePillar(g, b + e * .9f + inw * .8f, rr, ch * (.92f + .16f * (float)cr.NextDouble()), cyTint, HedgeShell.Sp.Cypress, pl.seed + 2);
                        }
                    }
                }
                HouseBuilder.Lod = 0;
                foreach (var pts in R)
                {
                    float len = 0; for (int k = 0; k + 1 < pts.Count; k++) len += Vector3.Distance(pts[k], pts[k + 1]);
                    metres[pl.type] += len;
                    Record(pts, pl.type != TH, pl.type >= TSH);
                }
            }
        }
        /// the box hedge's tint (Magistrala/Facade's hedge layer, sRGB) per species: matches the leaf shell's colour from afar
        public static Color HedgeTint(HedgeShell.Sp sp, int seed)
        {
            float v = .92f + .16f * (float)(new System.Random(seed).NextDouble());
            switch (sp)
            {
                case HedgeShell.Sp.Cypress: return new Color(.62f, .78f, .68f) * v;
                case HedgeShell.Sp.Privet: return new Color(1.0f, 1.08f, .8f) * v;
                case HedgeShell.Sp.Laurel: return new Color(.78f, .9f, .66f) * v;
                case HedgeShell.Sp.Oleander: case HedgeShell.Sp.OleanderWhite: return new Color(.9f, 1f, .78f) * v;
                default: return new Color(.82f, .95f, .7f) * v;
            }
        }
        static List<Vector3> Simplify(List<Vector3> raw)
        {
            var pts = new List<Vector3> { raw[0] };
            for (int k = 1; k + 1 < raw.Count; k++)
            {
                Vector3 a = pts[pts.Count - 1], c = raw[k + 1], p = raw[k];
                Vector3 ac = c - a; float t = Mathf.Clamp01(Vector3.Dot(p - a, ac) / Mathf.Max(1e-4f, ac.sqrMagnitude));
                Vector3 on = a + ac * t; var dxz = new Vector2(p.x - on.x, p.z - on.z);
                if (dxz.magnitude > .04f || Mathf.Abs(p.y - on.y) > .12f) pts.Add(p);
            }
            pts.Add(raw[raw.Count - 1]); return pts;
        }

        // ------------------------------------------------------------------ measuring (diagnosis: before / after)
        static readonly List<(double ax, double az, double bx, double bz, bool wall, bool hedge)> segs = new List<(double, double, double, double, bool, bool)>();
        static readonly Dictionary<(int, int), List<(double x, double z, double ux, double uz, bool wall, bool hedge)>> sGrid = new Dictionary<(int, int), List<(double, double, double, double, bool, bool)>>();
        /// a built boundary (Unity points along it): Yards and the frontage report every run
        public static void Record(List<Vector3> pts, bool wall, bool hedge)
        {
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                Vector2 a = W.G(pts[k]), b = W.G(pts[k + 1]); double L = Vector2.Distance(a, b); if (L < .05) continue;
                double ux = (b.x - a.x) / L, uz = (b.y - a.y) / L;
                for (double t = 0; t <= L; t += .5)
                {
                    double x = a.x + ux * t, z = a.y + uz * t; var key = K8(x, z);
                    if (!sGrid.TryGetValue(key, out var l)) sGrid[key] = l = new List<(double, double, double, double, bool, bool)>(); l.Add((x, z, ux, uz, wall, hedge));
                }
            }
        }
        static Dictionary<(int, int), bool> oldGrid;
        /// within 16 m of the old road's axis (Yards counts its dropped samples there)
        public static bool OldRoad(double x, double z) { if (oldGrid == null) BuildOld(); return oldGrid.ContainsKey(((int)Math.Floor(x / 4), (int)Math.Floor(z / 4))); }
        static List<(double x, double z, double ux, double uz, double hw)> oldSt;
        static void BuildOld()
        {
            oldGrid = new Dictionary<(int, int), bool>(); oldSt = new List<(double, double, double, double, double)>();
            var near = new Dictionary<(int, int), List<(double, double)>>();
            foreach (var (pl, hw) in OldRoadGraph(-9400, -300))
                for (int i = 0; i + 1 < pl.Count; i++)
                {
                    double ax = pl[i][0], az = pl[i][1], bx = pl[i + 1][0], bz = pl[i + 1][1], L = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)); if (L < .01) continue;
                    double ux = (bx - ax) / L, uz = (bz - az) / L;
                    for (double t = 0; t < L; t += 1)
                    {
                        double x = ax + ux * t, z = az + uz * t; if (x < -9400 || x > -300) continue;
                        var k = K8(x, z); bool dup = false;
                        for (int a = -1; a <= 1 && !dup; a++) for (int b = -1; b <= 1 && !dup; b++) if (near.TryGetValue((k.Item1 + a, k.Item2 + b), out var l)) foreach (var q in l) if ((q.Item1 - x) * (q.Item1 - x) + (q.Item2 - z) * (q.Item2 - z) < 3.9) { dup = true; break; }
                        if (dup) continue;
                        if (!near.TryGetValue(k, out var ll)) near[k] = ll = new List<(double, double)>(); ll.Add((x, z));
                        oldSt.Add((x, z, ux, uz, hw));
                        for (double d = -(hw + 16); d <= hw + 16; d += 2) oldGrid[((int)Math.Floor((x - uz * d) / 4), (int)Math.Floor((z + ux * d) / 4))] = true;
                    }
                }
        }
        static readonly (double x0, double x1, string name)[] Places =
        {
            (-9400, -8900, "Štafilić"), (-8900, -8000, "Novi"), (-8000, -7000, "Stari"), (-7000, -5900, "Lukšić"),
            (-5900, -3300, "Kambelovac"), (-3300, -2000, "Gomilica"), (-2000, -300, "Sućurac"),
        };
        /// metres of old-road edge (both sides) with a wall / hedge within 14 m of the asphalt (roughly parallel), against the
        /// evidence: a house behind (within 30 m), a house with a front yard (not at the line), green on the line (orthophoto),
        /// a Mapillary / ground-build hedge
        public static string Diagnose(string tag)
        {
            if (oldGrid == null) BuildOld();
            var rows = new Dictionary<string, double[]>(); foreach (var p in Places) rows[p.name] = new double[8];
            foreach (var s in oldSt)
            {
                string name = Places.FirstOrDefault(p => s.x >= p.x0 && s.x < p.x1).name ?? Places[Places.Length - 1].name; var row = rows[name];
                for (int side = -1; side <= 1; side += 2)
                {
                    double nx = -s.uz * side, nz = s.ux * side; row[0]++;
                    bool wall = false, hedge = false;
                    var k = K8(s.x + nx * (s.hw + 7), s.z + nz * (s.hw + 7));
                    for (int i = -2; i <= 2; i++) for (int j = -2; j <= 2; j++)
                            if (sGrid.TryGetValue((k.Item1 + i, k.Item2 + j), out var l))
                                foreach (var q in l)
                                {
                                    double dx = q.x - s.x, dz = q.z - s.z, a = dx * s.ux + dz * s.uz, c = dx * nx + dz * nz;
                                    if (Math.Abs(a) > 1.2 || c < s.hw - .5 || c > s.hw + 14 || Math.Abs(q.ux * s.ux + q.uz * s.uz) < .5) continue;
                                    if (q.wall) wall = true; if (q.hedge) hedge = true;
                                }
                    if (wall || hedge) row[1]++; if (wall) row[2]++; if (hedge) row[3]++;
                    // evidence (house: the owner search from the asphalt edge + 2 m)
                    double wx = s.x + nx * (s.hw + 2), wz = s.z + nz * (s.hw + 2);
                    int ow = Owner(wx, wz, s.ux, s.uz, nx, nz, out bool atLine);
                    if (ow >= 0 || atLine) row[4]++; if (ow >= 0 && !atLine) row[5]++;
                    bool green = false; for (double d = 1.5; d <= 4.5 && !green; d += 1) green = Yards.GreenP(s.x + nx * (s.hw + d), s.z + nz * (s.hw + d), -nx, -nz);
                    if (green) row[6]++;
                    bool mh = false; for (double d = 1; d <= 10 && !mh; d += 1.5) mh = Yards.NearMlyHedgeP(s.x + nx * (s.hw + d), s.z + nz * (s.hw + d), 2) || Yards.NearGcHedgeP(s.x + nx * (s.hw + d), s.z + nz * (s.hw + d), 1.5);
                    if (mh) row[7]++;
                }
            }
            var sb = new StringBuilder($"[zivice] {tag}: old road edge metres (both sides) | wall or hedge | wall | hedge || evidence: house behind | house with a front yard | orthophoto green on the line | Mapillary / ground-build hedge\n");
            var tot = new double[8];
            foreach (var p in Places) { var r = rows[p.name]; for (int i = 0; i < 8; i++) tot[i] += r[i]; sb.Append($"  {p.name,-11} {r[0],6:F0} | {r[1],6:F0} ({100 * r[1] / Math.Max(1, r[0]),3:F0} %) | {r[2],6:F0} | {r[3],6:F0} || {r[4],6:F0} | {r[5],6:F0} | {r[6],6:F0} | {r[7],5:F0}\n"); }
            sb.Append($"  {"TOTAL",-11} {tot[0],6:F0} | {tot[1],6:F0} ({100 * tot[1] / Math.Max(1, tot[0]),3:F0} %) | {tot[2],6:F0} | {tot[3],6:F0} || {tot[4],6:F0} | {tot[5],6:F0} | {tot[6],6:F0} | {tot[7],5:F0}\n");
            sb.Append("  frontage plots: " + string.Join(", ", Enumerable.Range(0, 7).Select(i => $"{TypeNames[i]} {metres[i]:F0} m")));
            sb.Append($"\n  Yards samples dropped by the old road: " + string.Join(", ", Yards.Drops.Select(kv => $"{kv.Key} {kv.Value}")));
            Debug.Log(sb.ToString());
            return sb.ToString();
        }
    }
}
