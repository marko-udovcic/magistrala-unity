// Rive Kaštela (Kaštel Novi / Stari, Lukšić west): the stone-paved promenade between the coastal street and the sea, its sea wall,
// the harbour works (moles, breakwaters, pontoons), the car parks by the waterfront and the riva's row of trees — from
// Assets/Editor/riva.json, which Tools/riva/riva_build.py derives from the DGU orthophoto (DOF LiDAR 2022./23., see its header):
//   1. Magistrala/Riva: ulazni podaci (ExportInputs) -> Logs/riva-input.json (the streets' carriageways, our trees, the stop bays)
//   2. python -I Tools/riva/riva_build.py -> Assets/Editor/riva.json
//   3. Magistrala/Build streets runs Build() at its end (the riva needs the streets' colliders and their carved ground):
//        paving   one mesh per 128 m chunk: the top (riva_ploce: Poly Haven Large Grey Tiles, lightened to Dalmatian limestone),
//                 the sea wall (riva_zid: Castle Wall Slates, greyed) down to −2.6 m on every ring segment with the photo sea
//                 outside, a 0.35 m kerb face elsewhere; height = the nearest street's surface + 0.10, falling 1.2 % towards the sea,
//                 at least 0.7 m above it (pontoons: flat 0.45 m); under a street surface the paving stays 4 cm below it
//        parking  asphalt (the streets' AsphaltFlat) on the ground + 6 cm, a kerb round it, white bay lines
//        pits     dark soil squares round the trees standing on the paving
//        terrain  lowered 0.25 m under the paving / car parks, to −1.2 m under the paving's last 3 m before the wall, and under the
//                 photo sea to −(0.35 + 0.4 × distance to land) (≤ −1.8 m): no grass cape or bank in front of the wall
//   WorldBuilder.Trees drops trees on paving / car parks without a photo crown, adds riva.json's trees; GraphicsPass.Details keeps
//   the grass off it; PropsBuilder puts the horseman on its landmark spot (riva.json landmarks).
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class RivaBuilder
    {
        public const string DATA = "Assets/Editor/riva.json", INPUT = "Logs/riva-input.json";
        /// models 80-82 (Kambelovac, Gomilica, Sućurac) model their promenade: cut too (Tools/riva/maska_istok.py)
        const string MASK_ISTOK = "Tools/riva/riva-maska-istok.json";
        const string OUT = WorldBuilder.GEN + "/Riva";
        const float WALL_BOTTOM = -2.6f, KERB = .35f, FLOOR = .7f, PONTOON = .45f, LIFT = .10f, FALL = .012f;

        // ------------------------------------------------------------------ data
        class Zone { public string id; public int x0, z0, nx, nz; public byte[] g; }
        static List<Zone> zones; static Dictionary<string, object> data;
        static Dictionary<string, object> Data()
        {
            if (data == null && File.Exists(DATA))
            {
                // Editor-only boolean geometry, same Shapely environment as riva_build.py.
                // Never rewrite the orthophoto source. Stop the build if cutting fails.
                string mask = PaketData.Need(PaketData.RIVA_MASKA);
                string python = PaketData.Need(W.BD + "/tools/world/dgu/cache/venv/bin/python");
                string output = Path.GetTempFileName();
                try
                {
                    var start = new System.Diagnostics.ProcessStartInfo(python,
                        $"-I -B \"{Path.GetFullPath("Tools/riva/cut_package.py")}\" \"{Path.GetFullPath(DATA)}\" \"{mask}\" \"{output}\""
                        + (File.Exists(MASK_ISTOK) ? $" --extra \"{Path.GetFullPath(MASK_ISTOK)}\"" : ""))
                    { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                    using (var process = System.Diagnostics.Process.Start(start))
                    {
                        string report = process.StandardOutput.ReadToEnd(), error = process.StandardError.ReadToEnd();
                        process.WaitForExit();
                        if (process.ExitCode != 0) throw new InvalidDataException("[riva] package cut failed: " + error);
                        Debug.Log("[riva] " + report.Trim());
                    }
                    data = MiniJson.O(MiniJson.Parse(File.ReadAllText(output)));
                }
                finally { File.Delete(output); }
            }
            return data;
        }
        public static void Reset() { data = null; zones = null; }

        static List<Zone> Zones()
        {
            if (zones != null) return zones;
            zones = new List<Zone>(); var d = Data(); if (d == null) return zones;
            foreach (var zo in MiniJson.L(d["zones"]))
            {
                var z = MiniJson.O(zo);
                var raw = Convert.FromBase64String((string)z["grid"]);
                using (var gs = new GZipStream(new MemoryStream(raw), CompressionMode.Decompress)) using (var ms = new MemoryStream()) { gs.CopyTo(ms); raw = ms.ToArray(); }
                zones.Add(new Zone { id = (string)z["id"], x0 = (int)MiniJson.D(z["x0"]), z0 = (int)MiniJson.D(z["z0"]), nx = (int)MiniJson.D(z["nx"]), nz = (int)MiniJson.D(z["nz"]), g = raw });
            }
            return zones;
        }

        /// riva grid code at game (x, z): bits 0-1 class (0 none, 1 paving / car park, 2 paving by the sea wall, 3 photo sea), bit 2 photo
        /// tree crown, bits 3-7 a sea cell's distance to land (0.5 m); on a class 1 cell bit 3 = car park
        public static int Code(double gx, double gz)
        {
            foreach (var z in Zones())
            {
                int i = (int)Math.Floor(gx - z.x0), j = (int)Math.Floor(gz - z.z0);
                if (i >= 0 && j >= 0 && i < z.nx && j < z.nz) return z.g[j * z.nx + i];
            }
            return 0;
        }
        public static int Class(double gx, double gz) => Code(gx, gz) & 3;
        /// a tree may not stand here (paving without a crown on the photo, a car park, the photo sea)
        public static bool NoTree(double gx, double gz) { int c = Code(gx, gz); return (c & 3) == 3 || ((c & 3) == 1 && (c & 8) != 0) || ((c & 3) != 0 && (c & 4) == 0); }

        /// riva.json's trees: game x, z, kind (TreeProtos.Species), scale, on paving (1: a pit round it)
        public static List<(double x, double z, int kind, float scale, bool pit)> Trees()
        {
            var r = new List<(double, double, int, float, bool)>(); var d = Data(); if (d == null) return r;
            foreach (var to in MiniJson.L(d["trees"])) { var t = MiniJson.L(to); r.Add((MiniJson.D(t[0]), MiniJson.D(t[1]), (int)MiniJson.D(t[2]), MiniJson.F(t[3]), MiniJson.D(t[4]) > .5)); }
            return r;
        }

        public static Vector2? Landmark(string name)
        {
            var d = Data(); if (d == null || !d.ContainsKey("landmarks")) return null;
            var l = MiniJson.O(d["landmarks"]); if (!l.ContainsKey(name)) return null;
            var p = MiniJson.L(l[name]); return new Vector2(MiniJson.F(p[0]), MiniJson.F(p[1]));
        }

        /// surface height of the riva meshes at game (x, z) (NaN: none)
        public static float SurfaceY(double gx, double gz)
        {
            var u = W.U(gx, 0, gz);
            foreach (var h in Physics.RaycastAll(new Vector3(u.x, 200, u.z), Vector3.down, 400))
                if (Under(h.collider.transform, "Riva")) return h.point.y;
            return float.NaN;
        }

        static bool Under(Transform t, string root) { for (; t; t = t.parent) if (t.name == root && t.parent && t.parent.name == "World") return true; return false; }

        // ------------------------------------------------------------------ inputs for riva_build.py
        [MenuItem("Magistrala/Riva: ulazni podaci")]
        public static string ExportInputs()
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new System.Text.StringBuilder("{\"cw\":[");
            var cw = StreetsBuilder.Carriageways();
            for (int i = 0; i < cw.Count; i++) { var c = cw[i]; if (i > 0) sb.Append(','); sb.Append(string.Format(ci, "[{0:F2},{1:F2},{2:F2},{3:F2},{4:F2}]", c.ax, c.az, c.bx, c.bz, c.hw)); }
            sb.Append("],\"trees\":[");
            string tp = WorldBuilder.GEN + "/trees.bytes"; int nt = 0;
            // (the trees riva.json added last time are not "ours": the export stays the same after a Build trees)
            Reset(); var mine = new HashSet<(long, long)>(Trees().Select(t => ((long)Math.Round(t.x * 2), (long)Math.Round(t.z * 2))));
            if (File.Exists(tp))
            {
                var b = File.ReadAllBytes(tp);
                for (int o = 0; o + 24 <= b.Length; o += 24)
                {
                    var g = W.G(new Vector3(BitConverter.ToSingle(b, o), 0, BitConverter.ToSingle(b, o + 8)));
                    if (mine.Contains(((long)Math.Round(g.x * 2), (long)Math.Round(g.y * 2)))) continue;
                    if (nt++ > 0) sb.Append(','); sb.Append(string.Format(ci, "[{0:F2},{1:F2}]", g.x, g.y));
                }
            }
            sb.Append("],\"bays\":[");
            var game = Resources.Load<Magistrala.Game.GameWorld>(Magistrala.Game.GameWorld.ResourcePath); int nb = 0;
            if (game) foreach (var s in game.stops)
                {
                    var g = W.G(s.bay); var dir = new Vector2(s.bayDir.x, -s.bayDir.y).normalized; // (Unity x / z -> game x / z)
                    if (nb++ > 0) sb.Append(','); sb.Append(string.Format(ci, "[{0:F2},{1:F2},{2:F3},{3:F3},{4:F2},{5:F2}]", g.x, g.y, dir.x, dir.y, s.bayHl, s.bayHw));
                }
            sb.Append("]}");
            Directory.CreateDirectory("Logs"); File.WriteAllText(INPUT, sb.ToString());
            return $"{INPUT}: {cw.Count} carriageway segments, {nt} trees, {nb} bays";
        }

        // ------------------------------------------------------------------ heights
        static List<(double ax, double az, double bx, double bz, double hw)> cw;
        static Dictionary<(int, int), List<int>> cwGrid; const double CG = 32;
        static void IndexRoads()
        {
            cw = StreetsBuilder.Carriageways(); cwGrid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < cw.Count; i++)
            {
                var r = cw[i];
                for (int gx = (int)Math.Floor(Math.Min(r.ax, r.bx) / CG); gx <= (int)Math.Floor(Math.Max(r.ax, r.bx) / CG); gx++)
                    for (int gz = (int)Math.Floor(Math.Min(r.az, r.bz) / CG); gz <= (int)Math.Floor(Math.Max(r.az, r.bz) / CG); gz++)
                    { if (!cwGrid.TryGetValue((gx, gz), out var l)) cwGrid[(gx, gz)] = l = new List<int>(); l.Add(i); }
            }
        }
        /// nearest carriageway point within `reach` m: its asphalt edge point (towards x, z), distance from the centre line, half width
        static bool NearestRoad(double x, double z, double reach, out double ex, out double ez, out double d, out double hw)
        {
            ex = ez = d = hw = 0; double best = double.MaxValue; int R = (int)Math.Ceiling(reach / CG); int cx = (int)Math.Floor(x / CG), cz = (int)Math.Floor(z / CG);
            var seen = new HashSet<int>();
            for (int a = cx - R; a <= cx + R; a++)
                for (int b = cz - R; b <= cz + R; b++)
                {
                    if (!cwGrid.TryGetValue((a, b), out var l)) continue;
                    foreach (int i in l)
                    {
                        if (!seen.Add(i)) continue;
                        var r = cw[i]; double bx = r.bx - r.ax, bz = r.bz - r.az, l2 = bx * bx + bz * bz;
                        double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((x - r.ax) * bx + (z - r.az) * bz) / l2));
                        double px = r.ax + bx * t, pz = r.az + bz * t, dd = Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
                        if (dd - r.hw < best)
                        {
                            best = dd - r.hw; d = dd; hw = r.hw;
                            double k = dd > 1e-6 ? Math.Min(dd, Math.Max(0, r.hw - .3)) / dd : 0; ex = px + (x - px) * k; ez = pz + (z - pz) * k;
                        }
                    }
                }
            return best < reach;
        }
        /// the street / road model surface at game (x, z) (NaN: none), from the colliders of World/Streets and World/Roads
        static float StreetY(double gx, double gz, float near = float.NaN)
        {
            var u = W.U(gx, 0, gz); float best = float.NaN;
            foreach (var h in Physics.RaycastAll(new Vector3(u.x, 300, u.z), Vector3.down, 600))
            {
                if (!(Under(h.collider.transform, "Streets") || Under(h.collider.transform, "Roads"))) continue;
                if (float.IsNaN(near)) { if (float.IsNaN(best) || h.point.y > best) best = h.point.y; }
                else if (Mathf.Abs(h.point.y - near) < .6f && (float.IsNaN(best) || Mathf.Abs(h.point.y - near) < Mathf.Abs(best - near))) best = h.point.y;
            }
            return best;
        }
        static UnityEngine.Terrain[] terrains;
        static float TerrainY(double gx, double gz)
        {
            var u = W.U(gx, 0, gz);
            foreach (var t in terrains)
            {
                var p = t.transform.position; var s = t.terrainData.size;
                if (u.x >= p.x && u.x <= p.x + s.x && u.z >= p.z && u.z <= p.z + s.z) return t.SampleHeight(new Vector3(u.x, 0, u.z)) + p.y;
            }
            return (float)Tiles.Height(gx, gz);
        }
        static float PaveY(double x, double z)
        {
            float y;
            if (NearestRoad(x, z, 220, out double ex, out double ez, out double d, out double hw))
            {
                float sy = StreetY(ex, ez);
                if (float.IsNaN(sy)) sy = TerrainY(ex, ez);
                y = sy + LIFT - FALL * (float)Math.Max(0, d - hw - 2);
            }
            else y = (float)Tiles.Height(x, z) + .05f;
            y = Mathf.Max(y, FLOOR);
            float on = StreetY(x, z, y); // a street / sidewalk surface right at this height: the paving goes under it
            if (!float.IsNaN(on) && on - .04f < y) y = on - .04f;
            return y;
        }

        /// the paving heights over every mesh vertex: fixed by the street along the kerb (its surface + LIFT within 3 m of the asphalt
        /// edge), harmonic in between (Jacobi over the mesh edges: the nearest-street formula alone jumped where the nearest street
        /// changed — a 1.8 m step across the square west of the marina), then ≥ FLOOR and under any street surface at the vertex
        static Dictionary<(long, long), float> SmoothHeights(List<object> pavings)
        {
            var id = new Dictionary<(long, long), int>(); var X = new List<double>(); var Z = new List<double>(); var nb = new List<HashSet<int>>();
            var local = new HashSet<int>(); bool loc = false;
            int Id(double x, double z) { var k = ((long)Math.Round(x * 100), (long)Math.Round(z * 100)); if (!id.TryGetValue(k, out int i)) { i = X.Count; id[k] = i; X.Add(x); Z.Add(z); nb.Add(new HashSet<int>()); } if (loc) local.Add(i); return i; }
            foreach (var po in pavings)
            {
                var m = MiniJson.O(po); var V = MiniJson.L(m["v"]); var T = MiniJson.L(m["t"]); int n = V.Count / 2; var map = new int[n];
                loc = MiniJson.G(m, "heights") as string == "local";
                for (int i = 0; i < n; i++) map[i] = Id(MiniJson.D(V[2 * i]), MiniJson.D(V[2 * i + 1]));
                for (int k = 0; k + 2 < T.Count; k += 3)
                {
                    int a = map[(int)MiniJson.D(T[k])], b = map[(int)MiniJson.D(T[k + 1])], c = map[(int)MiniJson.D(T[k + 2])];
                    nb[a].Add(b); nb[a].Add(c); nb[b].Add(a); nb[b].Add(c); nb[c].Add(a); nb[c].Add(b);
                }
                foreach (var ro in MiniJson.L(m["rings"]))
                {
                    var pts = MiniJson.L(MiniJson.O(ro)["pts"]); int prev = -1, first = -1;
                    foreach (var q in pts) { var l = MiniJson.L(q); int i = Id(MiniJson.D(l[0]), MiniJson.D(l[1])); if (prev >= 0) { nb[prev].Add(i); nb[i].Add(prev); } else first = i; prev = i; }
                    if (prev >= 0 && first >= 0) { nb[prev].Add(first); nb[first].Add(prev); }
                }
            }
            int N = X.Count; var y = new float[N]; var fix = new bool[N];
            for (int i = 0; i < N; i++)
            {
                if (local.Contains(i))
                {
                    // heights 'local' (the eastern rivas: their streets are road models 80 / 81, not in the street list, and the
                    // nearest listed street is the main road up the hill): the ground of the source tiles, fixed to a road model's
                    // or a street's surface where one lies within 3 m
                    y[i] = (float)Tiles.Height(X[i], Z[i]) + .05f;
                    float sy = StreetY(X[i], Z[i]); float bd = 0;
                    if (float.IsNaN(sy))
                        for (int ri = 1; ri <= 2 && float.IsNaN(sy); ri++)
                            for (int a = 0; a < 8; a++)
                            {
                                double ang = a * Math.PI / 4; float q = StreetY(X[i] + Math.Cos(ang) * 1.5 * ri, Z[i] + Math.Sin(ang) * 1.5 * ri);
                                if (!float.IsNaN(q) && (float.IsNaN(sy) || q > sy)) { sy = q; bd = 1.5f * ri; }
                            }
                    if (!float.IsNaN(sy)) { y[i] = sy + LIFT - FALL * bd; fix[i] = true; }
                    continue;
                }
                y[i] = PaveY(X[i], Z[i]);
                if (NearestRoad(X[i], Z[i], 40, out double ex, out double ez, out double d, out double hw) && d - hw < 3)
                {
                    float sy = StreetY(ex, ez); if (!float.IsNaN(sy)) { y[i] = sy + LIFT; fix[i] = true; }
                }
            }
            var y2 = new float[N];
            for (int it = 0; it < 900; it++)
            {
                for (int i = 0; i < N; i++)
                {
                    if (fix[i] || nb[i].Count == 0) { y2[i] = y[i]; continue; }
                    float s = 0; foreach (int j in nb[i]) s += y[j]; y2[i] = s / nb[i].Count;
                }
                var t = y; y = y2; y2 = t;
            }
            var r = new Dictionary<(long, long), float>();
            foreach (var kv in id)
            {
                int i = kv.Value; float v = Mathf.Max(y[i], FLOOR);
                float on = StreetY(X[i], Z[i], v); if (!float.IsNaN(on) && on - .04f < v) v = on - .04f;
                r[kv.Key] = v;
            }
            return r;
        }

        // ------------------------------------------------------------------ meshes
        class Chunk { public List<Vector3> v = new List<Vector3>(); public List<Vector2> u = new List<Vector2>(); public List<int>[] t = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() }; }
        static Dictionary<(int, int), Chunk> chunks;
        const float CH = 128;
        static Chunk At(Vector3 p) { var k = ((int)Math.Floor(p.x / CH), (int)Math.Floor(p.z / CH)); if (!chunks.TryGetValue(k, out var c)) chunks[k] = c = new Chunk(); return c; }
        /// triangle in material m (0 paving, 1 wall, 2 parking asphalt, 3 soil, 4 bitts, 5 concrete, 6 gravel), facing `face` (wound to it)
        static void Tri(int m, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 face)
        {
            var ch = At((a + b + c) / 3); int o = ch.v.Count;
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), face) < 0) { var t = b; b = c; c = t; var tu = ub; ub = uc; uc = tu; }
            ch.v.Add(a); ch.v.Add(b); ch.v.Add(c); ch.u.Add(ua); ch.u.Add(ub); ch.u.Add(uc);
            ch.t[m].Add(o); ch.t[m].Add(o + 1); ch.t[m].Add(o + 2);
        }
        static Vector2 TopUv(Vector3 u, float s) { var g = W.G(u); return new Vector2(g.x, g.y) / s; }

        static int Surface(Dictionary<string, object> m, int mat, Func<double, double, float> height, float uvScale)
        {
            var V = MiniJson.L(m["v"]); var T = MiniJson.L(m["t"]); int n = V.Count / 2;
            var P = new Vector3[n];
            for (int i = 0; i < n; i++) { double x = MiniJson.D(V[2 * i]), z = MiniJson.D(V[2 * i + 1]); P[i] = W.U(x, height(x, z), z); }
            for (int k = 0; k + 2 < T.Count; k += 3)
            {
                var a = P[(int)MiniJson.D(T[k])]; var b = P[(int)MiniJson.D(T[k + 1])]; var c = P[(int)MiniJson.D(T[k + 2])];
                Tri(mat, a, b, c, TopUv(a, uvScale), TopUv(b, uvScale), TopUv(c, uvScale), Vector3.up);
            }
            return T.Count / 3;
        }

        /// sea walls (to WALL_BOTTOM) and kerb faces along the rings; outward = right of an exterior (counter-clockwise in game x / z)
        static (int, int) Edges(Dictionary<string, object> m, Func<double, double, float> height, int kerbMat, int wallMat = 1)
        {
            int nw = 0, nk = 0;
            foreach (var ro in MiniJson.L(m["rings"]))
            {
                var r = MiniJson.O(ro); var pts = MiniJson.L(r["pts"]); var sea = MiniJson.L(r["sea"]); int n = pts.Count;
                var G = new Vector2[n]; var Y = new float[n];
                for (int i = 0; i < n; i++) { var q = MiniJson.L(pts[i]); G[i] = new Vector2(MiniJson.F(q[0]), MiniJson.F(q[1])); Y[i] = height(G[i].x, G[i].y); }
                float along = 0;
                for (int i = 0; i < n; i++)
                {
                    if (i == n - 1 && MiniJson.G(r, "open") is bool open && open) break;
                    int j = (i + 1) % n; var a = G[i]; var b = G[j]; float L = (b - a).magnitude; if (L < 1e-3) continue;
                    var dir = (b - a) / L; var outG = new Vector2(dir.y, -dir.x); // right of a -> b in game x / z
                    var ua = W.U(a.x, Y[i], a.y); var ub = W.U(b.x, Y[j], b.y);
                    var face = new Vector3(outG.x, 0, -outG.y);
                    bool wall = MiniJson.D(sea[i]) > .5;
                    float bot = wall ? WALL_BOTTOM : 0, s = wall ? 2.5f : 3f, mat = wall ? wallMat : kerbMat;
                    var la = new Vector3(ua.x, wall ? WALL_BOTTOM : ua.y - KERB, ua.z); var lb = new Vector3(ub.x, wall ? WALL_BOTTOM : ub.y - KERB, ub.z);
                    Vector2 Uv(float t, float y) => new Vector2((along + t) / s, y / s);
                    Tri((int)mat, ua, ub, lb, Uv(0, ua.y), Uv(L, ub.y), Uv(L, lb.y), face);
                    Tri((int)mat, ua, lb, la, Uv(0, ua.y), Uv(L, lb.y), Uv(0, la.y), face);
                    if (wall)
                    {
                        // 35 cm stone coping facing the sea, 14 mm above the paving.
                        var inset = -face * .35f;
                        var ca = ua + Vector3.up * .014f; var cb = ub + Vector3.up * .014f;
                        Tri(1,ca,cb,cb+inset,Uv(0,0),Uv(L,0),Uv(L,.35f),Vector3.up);
                        Tri(1,ca,cb+inset,ca+inset,Uv(0,0),Uv(L,.35f),Uv(0,.35f),Vector3.up);
                        // Fixed arc-length spacing continues over short ring segments.
                        for (float t = Mathf.Ceil((along + .01f) / 22f) * 22f - along; t < L; t += 22f)
                        {
                            if (along+t < 3) continue;
                            var b0 = Vector3.Lerp(ua,ub,t/L) - face*.65f;
                            b0.y = height(W.G(b0).x,W.G(b0).y) + .015f;
                            MooringBollard(b0, (ub-ua).normalized);
                        }
                    }
                    along += L; if (wall) nw++; else nk++;
                }
            }
            return (nw, nk);
        }

        // Low-poly cast iron bitva; combined with its chunk, including the collider mesh.
        static void MooringBollard(Vector3 b, Vector3 along)
        {
            void Cylinder(Vector3 centre, Vector3 axis, float length, float radius)
            {
                axis.Normalize(); var u = Vector3.Cross(axis,Vector3.up);
                if (u.sqrMagnitude < .01f) u = Vector3.right;
                u.Normalize(); var v = Vector3.Cross(axis,u).normalized;
                var lo = centre-axis*length*.5f; var hi = centre+axis*length*.5f;
                for (int i=0;i<10;i++)
                {
                    float a=i*Mathf.PI*.2f, c=(i+1)*Mathf.PI*.2f;
                    var n0=(u*Mathf.Cos(a)+v*Mathf.Sin(a)); var n1=(u*Mathf.Cos(c)+v*Mathf.Sin(c));
                    var p0=lo+n0*radius; var p1=lo+n1*radius; var p2=hi+n1*radius; var p3=hi+n0*radius;
                    Tri(4,p0,p1,p2,Vector2.zero,Vector2.right,Vector2.one,(n0+n1).normalized);
                    Tri(4,p0,p2,p3,Vector2.zero,Vector2.one,Vector2.up,(n0+n1).normalized);
                    Tri(4,lo,p1,p0,Vector2.zero,Vector2.zero,Vector2.zero,-axis);
                    Tri(4,hi,p3,p2,Vector2.zero,Vector2.zero,Vector2.zero,axis);
                }
            }
            Cylinder(b+Vector3.up*.035f,Vector3.up,.07f,.21f);
            Cylinder(b+Vector3.up*.24f,Vector3.up,.42f,.105f);
            Cylinder(b+Vector3.up*.44f,along,.48f,.115f);
        }

        static Material Mat(string name, Color c, float smooth, string tex, string nrm, float nrmScale)
        {
            var m = WorldBuilder.Lit(name, c, smooth, 0, tex, nrm, nrmScale);
            return m;
        }

        // ------------------------------------------------------------------ build
        [MenuItem("Magistrala/Riva: izgradi")]
        public static string BuildMenu() { var s = Build(); Debug.Log("[Magistrala] " + s); return s; }

        public static string Build()
        {
            Reset(); var d = Data(); if (d == null) return "riva: no " + DATA;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var log = new List<string>();
            terrains = UnityEngine.Object.FindObjectsByType<UnityEngine.Terrain>(FindObjectsSortMode.None);
            IndexRoads(); Physics.SyncTransforms();
            chunks = new Dictionary<(int, int), Chunk>();
            var root = WorldBuilder.Root("Riva");
            if (!AssetDatabase.IsValidFolder(OUT)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Riva");
            foreach (var f in Directory.GetFiles(OUT, "*.asset")) AssetDatabase.DeleteAsset(f);
            var paveMat = Mat("RivaPloce", new Color(1f, 1f, .98f), .18f, "PolyHaven/riva_ploce_d.jpg", "PolyHaven/riva_ploce_n.jpg", .9f);
            var wallMat = Mat("RivaZid", new Color(.95f, .95f, .93f), .1f, "PolyHaven/riva_zid_d.jpg", "PolyHaven/riva_zid_n.jpg", 1f);
            var asph = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.GEN + "/Materials/AsphaltFlat.mat") ?? GraphicsPass.AsphaltMaterial();
            var soil = Mat("RivaZemlja", new Color(.42f, .36f, .3f), .05f, "soil_ochre_d.jpg", null, 1);
            var paint = WorldBuilder.Lit("RivaCrte", new Color(.93f, .93f, .9f), .3f);
            paveMat = CesteMaterials.Stone(paveMat, "riva_ploce");
            wallMat = CesteMaterials.Stone(wallMat, "riva_rub", true, true);
            var iron = WorldBuilder.Lit("RivaBitve", new Color(.12f,.13f,.13f), .28f, .7f);
            // per real use (riva.json paving 'surface'): ploce = limestone slabs, beton = concrete quay / approaches (as the breakwaters),
            // sljunak = gravel beach promenade (the streets' pale gravel verge)
            var beton = WorldBuilder.Lit("RivaBeton", new Color(.86f, .84f, .80f), .12f, 0, "PolyHaven/concrete_wall_007_d.jpg", "PolyHaven/concrete_wall_007_n.jpg", .8f);
            var gravel = WorldBuilder.Lit("RivaSljunak", new Color(.95f, .93f, .89f), .05f, 0, "PolyHaven/gravel_ground_01_diff.jpg", "PolyHaven/gravel_ground_01_nor.jpg", .7f);
            var mats = new[] { paveMat, wallMat, asph, soil, iron, beton, gravel };

            // the paving heights: computed once per vertex (cache by position: the rings share the mesh's boundary vertices)
            var cache = SmoothHeights(MiniJson.L(d["paving"]));
            float H(double x, double z) { var k = ((long)Math.Round(x * 100), (long)Math.Round(z * 100)); if (!cache.TryGetValue(k, out var y)) cache[k] = y = PaveY(x, z); return y; }
            int nt = 0, nw = 0, nk = 0;
            foreach (var po in MiniJson.L(d["paving"]))
            {
                var m = MiniJson.O(po); string surf = MiniJson.G(m, "surface") as string;
                int sm = surf == "beton" ? 5 : surf == "sljunak" ? 6 : 0;
                nt += Surface(m, sm, H, 3f); var e = Edges(m, H, sm == 6 ? 5 : sm, sm == 0 ? 1 : 5); nw += e.Item1; nk += e.Item2; // (concrete quays: concrete wall, the stone coping stays)
            }
            float Pon(double x, double z) => PONTOON;
            foreach (var po in MiniJson.L(d["pontoons"])) { var m = MiniJson.O(po); nt += Surface(m, 0, Pon, 3f); Edges(m, Pon, 0); }
            log.Add($"paving {nt} triangles, sea wall {nw} segments, kerb {nk}, {cache.Count} heights");
            // car parks: on the ground as the streets left it (+ 6 cm), kerb round
            var parks = MiniJson.L(d["parking"]);
            float Gnd(double x, double z) => (float)Tiles.Height(x, z) + .06f; // (the baked ground: a second build gives the same car park)
            foreach (var po in parks)
            {
                var m = MiniJson.O(po); Surface(m, 2, Gnd, 1f);
                Edges(m, Gnd, 0);
            }
            var phys = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(WorldBuilder.GEN + "/Roads/Asphalt.physicMaterial");
            int nv = 0;
            foreach (var kv in chunks)
            {
                var ch = kv.Value; string name = $"riva_{kv.Key.Item1}_{kv.Key.Item2}";
                // welded per material (identical position + uv)
                var V = new List<Vector3>(); var U = new List<Vector2>(); var subs = new List<int[]>(); var sm = new List<Material>();
                var col = new List<int>();
                for (int k = 0; k < mats.Length; k++)
                {
                    if (ch.t[k].Count == 0) continue;
                    var map = new Dictionary<(Vector3, Vector2), int>(); var ix = new int[ch.t[k].Count];
                    for (int i = 0; i < ix.Length; i++)
                    {
                        int o = ch.t[k][i]; var key = (ch.v[o], ch.u[o]);
                        if (!map.TryGetValue(key, out int id)) { id = V.Count; map[key] = id; V.Add(key.Item1); U.Add(key.Item2); }
                        ix[i] = id;
                    }
                    subs.Add(ix); sm.Add(mats[k]); if (k != 3) col.AddRange(ix);
                }
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(V); mesh.SetUVs(0, U); mesh.subMeshCount = subs.Count;
                for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, $"{OUT}/{name}.asset"); nv += V.Count;
                var cm = new Mesh { name = name + "-col", indexFormat = IndexFormat.UInt32 }; cm.SetVertices(V); cm.SetTriangles(col, 0); cm.RecalculateBounds();
                AssetDatabase.CreateAsset(cm, $"{OUT}/{name}-col.asset");
                var go = new GameObject(name); go.isStatic = true; go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = sm.ToArray(); mr.shadowCastingMode = ShadowCastingMode.On;
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = cm; mc.sharedMaterial = phys;
            }
            Physics.SyncTransforms();
            // bay lines on the car parks (on their surface: rays onto the riva colliders)
            var lv = new List<Vector3>(); var li = new List<int>(); var lu = new List<Vector2>(); int nl = 0;
            foreach (var po in parks)
                foreach (var lo in MiniJson.L(MiniJson.O(po)["lines"]))
                {
                    var l = MiniJson.L(lo); var a = new Vector2(MiniJson.F(l[0]), MiniJson.F(l[1])); var b = new Vector2(MiniJson.F(l[2]), MiniJson.F(l[3]));
                    float L = (b - a).magnitude; var dir = (b - a) / L; var n = new Vector2(-dir.y, dir.x) * .06f; int steps = Mathf.Max(1, Mathf.CeilToInt(L / 2));
                    for (int s = 0; s < steps; s++)
                    {
                        var p = a + dir * (L * s / steps); var q = a + dir * (L * (s + 1) / steps);
                        float yp = SurfaceY(p.x, p.y), yq = SurfaceY(q.x, q.y); if (float.IsNaN(yp) || float.IsNaN(yq)) continue;
                        int o = lv.Count;
                        lv.Add(W.U(p.x + n.x, yp + .01f, p.y + n.y)); lv.Add(W.U(q.x + n.x, yq + .01f, q.y + n.y)); lv.Add(W.U(q.x - n.x, yq + .01f, q.y - n.y)); lv.Add(W.U(p.x - n.x, yp + .01f, p.y - n.y));
                        for (int k = 0; k < 4; k++) lu.Add(Vector2.zero);
                        var f = Vector3.Cross(lv[o + 1] - lv[o], lv[o + 2] - lv[o]).y > 0 ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
                        foreach (int k in f) li.Add(o + k);
                    }
                    nl++;
                }
            if (lv.Count > 0)
            {
                var mesh = new Mesh { name = "riva-parking-lines", indexFormat = IndexFormat.UInt32 }; mesh.SetVertices(lv); mesh.SetUVs(0, lu); mesh.SetTriangles(li, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, $"{OUT}/parking-lines.asset");
                var go = new GameObject("parking-lines"); go.isStatic = true; go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = paint; mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            // tree pits on the paving (on its surface: rays onto the riva colliders), dark soil 1.6 m square
            var pv = new List<Vector3>(); var pu = new List<Vector2>(); var pi = new List<int>(); int npit = 0;
            foreach (var t in Trees())
            {
                if (!t.pit) continue;
                const float r = .8f; var c4 = new[] { (-r, -r), (r, -r), (r, r), (-r, r) }; var ys = new float[4]; bool ok = true;
                for (int k = 0; k < 4; k++) { ys[k] = SurfaceY(t.x + c4[k].Item1, t.z + c4[k].Item2); if (float.IsNaN(ys[k])) ok = false; }
                if (!ok) continue;
                int o = pv.Count;
                for (int k = 0; k < 4; k++) { pv.Add(W.U(t.x + c4[k].Item1, ys[k] + .012f, t.z + c4[k].Item2)); pu.Add(new Vector2(k == 1 || k == 2 ? 1 : 0, k >= 2 ? 1 : 0)); }
                var f = Vector3.Cross(pv[o + 1] - pv[o], pv[o + 2] - pv[o]).y > 0 ? new[] { 0, 1, 2, 0, 2, 3 } : new[] { 0, 2, 1, 0, 3, 2 };
                foreach (int k in f) pi.Add(o + k);
                npit++;
            }
            if (pv.Count > 0)
            {
                var mesh = new Mesh { name = "riva-pits", indexFormat = IndexFormat.UInt32 }; mesh.SetVertices(pv); mesh.SetUVs(0, pu); mesh.SetTriangles(pi, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, $"{OUT}/pits.asset");
                var go = new GameObject("tree-pits"); go.isStatic = true; go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = soil; mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            log.Add($"{parks.Count} car parks ({nl} bay lines), {npit} tree pits, {chunks.Count} chunks, {nv} vertices");
            log.Add(LowerTerrain());
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            log.Add($"{sw.ElapsedMilliseconds / 1000.0:F1} s");
            return "riva: " + string.Join("; ", log);
        }

        /// terrain under the paving / car parks below their surface, the photo sea below the water (WorldBuilder's heights are the base:
        /// a rebuild of the streets + riva gives the same ground — min() only)
        static string LowerTerrain()
        {
            int n1 = 0, n2 = 0, n3 = 0, tiles = 0;
            foreach (var t in terrains)
            {
                var parts = t.name.Split(' ')[1].Split(','); int tx = int.Parse(parts[0]), tz = int.Parse(parts[1]);
                bool any = Zones().Any(z => z.x0 < tx * 1024 + 1024 && z.x0 + z.nx > tx * 1024 && z.z0 < tz * 1024 + 1024 && z.z0 + z.nz > tz * 1024);
                if (!any) continue;
                var td = t.terrainData; int res = td.heightmapResolution; double step = 1024.0 / (res - 1);
                var H = td.GetHeights(0, 0, res, res); bool ch = false;
                for (int r = 0; r < res; r++)
                    for (int c = 0; c < res; c++)
                    {
                        double gx = tx * 1024 + c * step, gz = (tz + 1) * 1024 - r * step;
                        int code = Code(gx, gz), cls = code & 3; if (cls == 0) continue;
                        float h = WorldBuilder.YMIN + H[r, c] * WorldBuilder.YSIZE, target = h;
                        if (cls == 1) { float sy = SurfaceY(gx, gz); if (!float.IsNaN(sy)) { target = Mathf.Min(h, sy - .25f); n1++; } }
                        else if (cls == 2) { target = Mathf.Min(h, -1.2f); n2++; }
                        else
                        {
                            float dl = (code >> 3) * .5f;
                            if (!float.IsNaN(StreetY(gx, gz))) continue; // (a street over the photo sea: its ground stays)
                            target = Mathf.Min(h, -Mathf.Min(1.8f, .35f + .4f * dl)); n3++;
                        }
                        if (target < h) { H[r, c] = Mathf.Clamp01((target - WorldBuilder.YMIN) / WorldBuilder.YSIZE); ch = true; }
                    }
                if (ch) { td.SetHeights(0, 0, H); EditorUtility.SetDirty(td); tiles++; }
            }
            return $"terrain: {n1} samples under paving / car parks, {n2} by the sea wall, {n3} under the photo sea, {tiles} tiles";
        }
    }
}
