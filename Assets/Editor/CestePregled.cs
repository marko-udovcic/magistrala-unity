// Road joints audit (ceste-pregled, 2026-11-23): -executeMethod Magistrala.EditorTools.CestePregled.Batch -out <dir> [-shots N] [-x0 gx -x1 gx]
// [-dump N: the graph edges near the N worst in rubovi.txt] [-at gx,gz,dx,dz;..: shots of these spots only] [-rebuild: Build streets
// first and save the scene]. Opens Assets/Scenes/Magistrala.unity and walks every carriageway of the built world along its centre line (graph + outside streets:
// StreetsBuilder.Carriageways(); Marko's model / tile roads: model-junctions.json `roads`, lvl 0, each end run 15 m on into whatever
// joins it), 1 m samples, raycasts on the colliders as the truck sees them (World/Roads = models, World/Streets = our ribbons):
//   STEP   the surface jumps > 8 cm between two samples against the local trend (a seam step / lip)
//   HOLE   a short (≤ 4 m) run with no road under the line between road on both sides (a gap at a seam)
//   END    the road stops ≥ 0.4 m above / below the ground under it (an end in the air / in the terrain)
//   DOUBLE two road colliders of different objects within 25 cm at one point (overlapping surfaces, z-fighting, a lip)
//   TERR   the terrain over the road surface (> 3 cm: grass through the asphalt)
//   GRADE  > 14 % over 10 m along, CROSS > 9 % across (± half of the half width): too steep / twisted
//   OBJ    some other collider (building, prop) crossing the road between 0.4 and 4 m over it
// Flags are clustered (12 m), ranked, written to <dir>/problemi.csv + problemi.md, and the worst N clusters rendered (oblique, along the
// road) to <dir>/NNN_<kind>_<gx>_<gz>.png. Nothing is saved: the scene is reopened from disk at the end (stays the last open scene).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class CestePregled
    {
        enum K { Road, Terrain, Other }
        class Flag { public string kind; public double gx, gz, val; public string obj = ""; public Vector3 dir; }
        class Hit { public float y; public K k; public Collider c; }

        static K Kind(Collider c)
        {
            if (c is TerrainCollider) return K.Terrain;
            var p = c.transform.parent; if (p && (p.name == "Roads" || p.name == "Streets")) return K.Road;
            return K.Other;
        }
        static readonly RaycastHit[] buf = new RaycastHit[64];
        static List<Hit> Cast(double gx, double gz)
        {
            var u = W.U(gx, 0, gz); u.y = 400;
            int n = Physics.RaycastNonAlloc(u, Vector3.down, buf, 800, ~0, QueryTriggerInteraction.Ignore);
            var r = new List<Hit>(n);
            for (int i = 0; i < n; i++) r.Add(new Hit { y = buf[i].point.y, k = Kind(buf[i].collider), c = buf[i].collider });
            r.Sort((a, b) => b.y.CompareTo(a.y)); return r;
        }
        static string Path2(Transform t) => t.parent ? t.parent.name + "/" + t.name : t.name;

        static double x0 = -10240, x1 = 0; static int dump; static string at;
        static List<Flag> flags;

        /// one line: samples every 1 m; `yHint` NaN = take the top road
        static void Walk(List<double[]> pts, double hw, string src)
        {
            var S = new List<(double x, double z, Vector3 d)>();
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                double ax = pts[i][0], az = pts[i][1], bx = pts[i + 1][0], bz = pts[i + 1][1], L = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                if (L < 1e-3) continue; int n = Math.Max(1, (int)Math.Ceiling(L));
                var d = new Vector3((float)((bx - ax) / L), 0, (float)(-(bz - az) / L));
                for (int k = 0; k < n; k++) { double t = k / (double)n; S.Add((ax + (bx - ax) * t, az + (bz - az) * t, d)); }
                if (i + 2 == pts.Count) S.Add((bx, bz, d));
            }
            if (S.Count < 3) return;
            int N = S.Count; var y = new float[N]; var ter = new float[N]; var col = new Collider[N]; float prev = float.NaN;
            for (int i = 0; i < N; i++)
            {
                y[i] = float.NaN; ter[i] = float.NaN;
                if (S[i].x < x0 || S[i].x > x1) continue;
                var hs = Cast(S[i].x, S[i].z);
                Hit best = null;
                foreach (var h in hs) if (h.k == K.Road && (best == null || (float.IsNaN(prev) ? h.y > best.y : Math.Abs(h.y - prev) < Math.Abs(best.y - prev)))) best = h;
                if (best != null && !float.IsNaN(prev) && Math.Abs(best.y - prev) > 3) best = null; // another level (bridge / underpass)
                foreach (var h in hs) if (h.k == K.Terrain) { ter[i] = h.y; break; }
                if (best == null) continue;
                y[i] = best.y; col[i] = best.c; prev = best.y;
                // overlapping road surfaces of different objects
                foreach (var h in hs)
                    if (h.k == K.Road && h.c != best.c && Math.Abs(h.y - best.y) < .25f)
                    { flags.Add(new Flag { kind = "DOUBLE", gx = S[i].x, gz = S[i].z, val = Math.Abs(h.y - best.y), obj = Path2(best.c.transform) + " + " + Path2(h.c.transform), dir = S[i].d }); break; }
                if (!float.IsNaN(ter[i]) && ter[i] > best.y + .03f)
                    flags.Add(new Flag { kind = "TERR", gx = S[i].x, gz = S[i].z, val = ter[i] - best.y, obj = Path2(best.c.transform), dir = S[i].d });
                foreach (var h in hs)
                    if (h.k == K.Other && h.y > best.y + .4f && h.y < best.y + 4f && !(h.c is BoxCollider && h.c.transform.parent && h.c.transform.parent.name is "rails" or "barriers"))
                    { flags.Add(new Flag { kind = "OBJ", gx = S[i].x, gz = S[i].z, val = h.y - best.y, obj = Path2(h.c.transform), dir = S[i].d }); break; }
                // cross slope (only away from the line's ends: junction mouths flare)
                if (i > 8 && i < N - 8 && hw > 2)
                {
                    double o = hw * .5; var d = S[i].d; double rx = d.z, rz = d.x; // right in game frame (game z = −Unity z)
                    float a = RoadAt(S[i].x + rx * o, S[i].z + rz * o, best.y), b = RoadAt(S[i].x - rx * o, S[i].z - rz * o, best.y);
                    if (!float.IsNaN(a) && !float.IsNaN(b) && Math.Abs(a - b) / (2 * o) > .09)
                        flags.Add(new Flag { kind = "CROSS", gx = S[i].x, gz = S[i].z, val = Math.Abs(a - b) / (2 * o) * 100, obj = Path2(best.c.transform), dir = S[i].d });
                }
            }
            for (int i = 1; i < N; i++)
            {
                if (float.IsNaN(y[i]) || float.IsNaN(y[i - 1])) continue;
                // the local trend: median of the neighbouring differences (excluding this one)
                var ds = new List<float>();
                for (int k = i - 4; k <= i + 4; k++) if (k != i && k >= 1 && k < N && !float.IsNaN(y[k]) && !float.IsNaN(y[k - 1])) ds.Add(y[k] - y[k - 1]);
                if (ds.Count < 3) continue; ds.Sort(); float med = ds[ds.Count / 2];
                float dev = (y[i] - y[i - 1]) - med;
                if (Math.Abs(dev) > .08f)
                    flags.Add(new Flag { kind = "STEP", gx = S[i].x, gz = S[i].z, val = Math.Abs(dev), obj = col[i - 1] == col[i] ? Path2(col[i].transform) : Path2(col[i - 1].transform) + " | " + Path2(col[i].transform), dir = S[i].d });
            }
            // holes: short no-road runs between road
            for (int i = 1; i < N; i++)
            {
                if (!float.IsNaN(y[i]) || float.IsNaN(y[i - 1])) continue;
                int j = i; while (j < N && float.IsNaN(y[j]) && S[j].x >= x0 && S[j].x <= x1) j++;
                if (j < N && j - i <= 4 && !float.IsNaN(y[j]))
                    flags.Add(new Flag { kind = "HOLE", gx = S[i].x, gz = S[i].z, val = j - i, obj = Path2(col[i - 1].transform) + " | " + Path2(col[j].transform), dir = S[i].d });
                // a road that stops: road then ≥ 6 m of nothing — the last road sample vs the ground just after it
                if (j - i >= 6 && !float.IsNaN(ter[Math.Min(N - 1, i + 1)]))
                {
                    float dz = y[i - 1] - ter[Math.Min(N - 1, i + 1)];
                    if (Math.Abs(dz) > .4f) flags.Add(new Flag { kind = "END", gx = S[i - 1].x, gz = S[i - 1].z, val = dz, obj = Path2(col[i - 1].transform), dir = S[i - 1].d });
                }
                i = j;
            }
            // grade over 10 m
            for (int i = 10; i < N; i += 5)
                if (!float.IsNaN(y[i]) && !float.IsNaN(y[i - 10]) && Math.Abs(y[i] - y[i - 10]) / 10 > .14 && Math.Abs(y[i] - y[i - 10]) < 3)
                    flags.Add(new Flag { kind = "GRADE", gx = S[i].x, gz = S[i].z, val = Math.Abs(y[i] - y[i - 10]) * 10, obj = Path2(col[i].transform), dir = S[i].d });
        }
        static float RoadAt(double gx, double gz, float near)
        {
            float best = float.NaN;
            foreach (var h in Cast(gx, gz)) if (h.k == K.Road && Math.Abs(h.y - near) < 1.5f && (float.IsNaN(best) || Math.Abs(h.y - near) < Math.Abs(best - near))) best = h.y;
            return best;
        }

        static double Score(string k, double v, int n) => k switch
        {
            "HOLE" => 3 + v, "STEP" => v * 20, "END" => Math.Abs(v) * 2, "DOUBLE" => 1.5 + n * .05, "TERR" => v * 15 + n * .05,
            "GRADE" => (v - 14) * .3, "CROSS" => (v - 9) * .3, "OBJ" => 2 + n * .05, _ => 0
        };

        public static void Batch()
        {
            var a = Environment.GetCommandLineArgs();
            string Arg(string k) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }
            string dir = Arg("-out") ?? "Temp/ceste-pregled"; Directory.CreateDirectory(dir);
            int nShots = int.TryParse(Arg("-shots"), out var ns) ? ns : 60;
            dump = int.TryParse(Arg("-dump"), out var du) ? du : 0; at = Arg("-at");
            if (a.Contains("-rebuild")) { EditorSceneManager.OpenScene(MainScene.PATH, OpenSceneMode.Single); Debug.Log("[ceste-pregled] rebuild\n" + StreetsBuilder.Build()); EditorSceneManager.SaveOpenScenes(); }
            if (double.TryParse(Arg("-x0"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v0)) x0 = v0;
            if (double.TryParse(Arg("-x1"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v1)) x1 = v1;
            try { Run(dir, nShots); }
            catch (Exception e) { Debug.LogError("[ceste-pregled] " + e); }
            EditorSceneManager.OpenScene(MainScene.PATH, OpenSceneMode.Single);   // from disk: nothing saved, the main scene stays the last one
        }

        static void Run(string dir, int nShots)
        {
            EditorSceneManager.OpenScene(MainScene.PATH, OpenSceneMode.Single);
            Physics.SyncTransforms();
            flags = new List<Flag>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            // 1) our carriageways (graph + outside streets), chained per edge
            var segs = StreetsBuilder.Carriageways(); int nLines = 0;
            for (int i = 0; i < segs.Count;)
            {
                var line = new List<double[]> { new[] { segs[i].ax, segs[i].az }, new[] { segs[i].bx, segs[i].bz } }; double hw = segs[i].hw; int j = i + 1;
                while (j < segs.Count && Math.Abs(segs[j].ax - segs[j - 1].bx) < 1e-6 && Math.Abs(segs[j].az - segs[j - 1].bz) < 1e-6) { line.Add(new[] { segs[j].bx, segs[j].bz }); j++; }
                if (line.Any(p => p[0] >= x0 && p[0] <= x1 && p[1] > -7168 && p[1] < -4096)) { Walk(line, hw, "graph"); nLines++; }
                i = j;
            }
            int nGraph = flags.Count;
            // 2) the models' roads, both ends run on 15 m
            foreach (var m in WorldData.Models(-1e9, 1e9))
            {
                if (m.x < x0 - 300 || m.x > x1 + 300) continue;
                foreach (var r in m.roads)
                {
                    if (r.lvl != 0 || r.p.Count < 2) continue;
                    var p = r.p.Select(q => new[] { q[0], q[1] }).ToList();
                    double[] Ext(double[] e, double[] f) { double dx = e[0] - f[0], dz = e[1] - f[1], L = Math.Sqrt(dx * dx + dz * dz); return L < 1e-6 ? e : new[] { e[0] + dx / L * 15, e[1] + dz / L * 15 }; }
                    p.Insert(0, Ext(p[0], p[1])); p.Add(Ext(p[p.Count - 1], p[p.Count - 2]));
                    Walk(p, r.w / 2, m.key); nLines++;
                }
            }
            Debug.Log($"[ceste-pregled] {nLines} lines, {flags.Count} flags ({nGraph} on graph lines), {sw.ElapsedMilliseconds / 1000.0:F0} s");

            // cluster (12 m cells, merged with neighbours by a simple pass)
            var models = WorldData.Models(-1e9, 1e9);
            string Near(double gx, double gz)
            {
                ModelJ best = null; double bd = 1e9;
                foreach (var m in models) { double d = Math.Max(Math.Abs(gx - m.x), Math.Abs(gz - m.z)) - m.half; if (d < bd) { bd = d; best = m; } }
                return best == null ? "" : bd <= 0 ? best.key : $"{best.key} (+{bd:F0} m)";
            }
            var cl = new List<(double gx, double gz, List<Flag> f)>();
            foreach (var f in flags.OrderByDescending(f => Score(f.kind, f.val, 1)))
            {
                int k = cl.FindIndex(c => (c.gx - f.gx) * (c.gx - f.gx) + (c.gz - f.gz) * (c.gz - f.gz) < 12 * 12);
                if (k < 0) cl.Add((f.gx, f.gz, new List<Flag> { f })); else cl[k].f.Add(f);
            }
            var rows = cl.Select(c =>
            {
                var byK = c.f.GroupBy(f => f.kind).Select(g => (k: g.Key, n: g.Count(), mx: g.Max(f => Math.Abs(f.val)))).OrderByDescending(g => Score(g.k, g.mx, g.n)).ToList();
                double sc = byK.Sum(g => Score(g.k, g.mx, g.n));
                var top = c.f.OrderByDescending(f => Score(f.kind, f.val, 1)).First();
                return (c.gx, c.gz, sc, byK, top, near: Near(c.gx, c.gz));
            }).OrderByDescending(r => r.sc).ToList();

            var csv = new StringBuilder("rank;gx;gz;unity_x;unity_z;score;kinds;near;objects;shot\n");
            var md = new StringBuilder($"# Ceste – automatski pregled spojeva ({DateTime.Now:yyyy-MM-dd HH:mm})\n\n{nLines} linija, {flags.Count} oznaka, {rows.Count} skupina; x {x0}…{x1}\n\n| # | game x,z | Unity x,z | ocjena | što | model | objekti |\n|---|---|---|---|---|---|---|\n");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i]; var u = W.U(r.gx, 0, r.gz);
                string kinds = string.Join(" ", r.byK.Select(g => $"{g.k}×{g.n}({g.mx:F2})"));
                var objs = string.Join(", ", rows[i].byK.Count > 0 ? new[] { r.top.obj } : new string[0]);
                csv.AppendLine(FormattableString.Invariant($"{i + 1};{r.gx:F1};{r.gz:F1};{u.x:F1};{u.z:F1};{r.sc:F2};{kinds};{r.near};{objs};{r.top.gx:F1},{r.top.gz:F1},{r.top.dir.x:F3},{r.top.dir.z:F3}"));
                if (i < 300) md.AppendLine(FormattableString.Invariant($"| {i + 1} | {r.gx:F0},{r.gz:F0} | {u.x:F0},{u.z:F0} | {r.sc:F1} | {kinds} | {r.near} | {objs} |"));
            }
            File.WriteAllText(Path.Combine(dir, "problemi.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(dir, "problemi.md"), md.ToString());
            Debug.Log($"[ceste-pregled] {rows.Count} clusters -> {dir}");

            if (dump > 0)
            {
                var sbd = new StringBuilder();
                for (int i = 0; i < Math.Min(dump, rows.Count); i++)
                {
                    var r = rows[i]; sbd.AppendLine(FormattableString.Invariant($"## {i + 1} ({r.gx:F0},{r.gz:F0}) {r.near}: {string.Join(" ", r.byK.Select(g => $"{g.k}x{g.n}({g.mx:F2})"))} | {r.top.obj}"));
                    foreach (var l in StreetsBuilder.EdgesNear(r.gx, r.gz, 25)) sbd.AppendLine("  " + l);
                }
                File.WriteAllText(Path.Combine(dir, "rubovi.txt"), sbd.ToString());
            }
            // shots of the worst (or of the given spots: -at gx,gz,dx,dz;..  as the csv's `shot` column)
            var poses = new List<(string name, double gx, double gz, Vector3 dir)>();
            if (at != null)
                foreach (var t in at.Split(';'))
                {
                    var v = t.Split(',').Select(q => double.Parse(q, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                    poses.Add((FormattableString.Invariant($"at_{v[0]:F0}_{v[1]:F0}"), v[0], v[1], new Vector3((float)v[2], 0, (float)v[3])));
                }
            else
                for (int i = 0; i < Math.Min(nShots, rows.Count); i++)
                    poses.Add((FormattableString.Invariant($"{i + 1:000}_{(rows[i].byK.Count > 0 ? rows[i].byK[0].k : "X")}_{rows[i].gx:F0}_{rows[i].gz:F0}"), rows[i].top.gx, rows[i].top.gz, rows[i].top.dir));
            if (poses.Count == 0) return;
            var cam = new GameObject("pregled_cam").AddComponent<Camera>();
            if (Camera.main) cam.CopyFrom(Camera.main);
            cam.nearClipPlane = .2f; cam.farClipPlane = 2500; cam.fieldOfView = 55;
            ShaderUtil.allowAsyncCompilation = false;
            const int Wd = 1280, Ht = 720;
            var rt = new RenderTexture(Wd, Ht, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.aspect = (float)Wd / Ht; cam.targetTexture = rt;
            var tex = new Texture2D(Wd, Ht, TextureFormat.RGB24, false);
            foreach (var ps in poses)
            {
                var p = W.U(ps.gx, 0, ps.gz);
                var hs = Cast(ps.gx, ps.gz); p.y = hs.Count > 0 ? hs.FirstOrDefault(h => h.k == K.Road)?.y ?? hs[0].y : 0;
                // oblique along the road, from the side a little (the seam reads across the line)
                var d = ps.dir.sqrMagnitude > .1f ? ps.dir : Vector3.forward; var side = new Vector3(d.z, 0, -d.x);
                var eye = p - d * 16 + side * 7 + Vector3.up * 11;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(p - eye));
                cam.Render(); cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, Wd, Ht), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, ps.name + ".png"), tex.EncodeToPNG());
            }
            cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(cam.gameObject);
            Debug.Log("[ceste-pregled] shots done");
        }
    }
}
