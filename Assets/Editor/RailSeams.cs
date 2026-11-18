// Guardrails across the seams between a road section (d01–d21, its rails in model-junctions.json `rails`) and a junction model:
// the junction GLBs have no guardrails, so the section's edge / median rail stopped dead on the seam (e.g. the median at x ≈ −2030
// between d11 and 54). Each section rail end that lies at a junction model is continued along the junction's road it runs beside
// (same lateral offset from that road's axis), metre by metre, until an opening really starts:
//   asphalt / island / sidewalk under the rail (a side road, a crossover or turn gap, a kerbed island), the carriageway beside it ends
//   (no asphalt within its clearance + 1.2 m), or a pedestrian crossing beside it (markings over 2 of 3 points across the first lane);
// then 1.5 m short of it. The extension ends in a sloped terminal (the beam ramps down into the ground over the last ≤ 6 m, as on
// Croatian roads) instead of a cut. Colliders: the same 0.24 m boxes as the section rails (Rail physics material).
// Drawn as a 0.3 m steel beam (like the package's guardrail shell) on posts every 4 m. Marko's geometry is not touched.
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class RailSeams
    {
        const double STEP = 1, MAXLEN = 400, BACK = 1.5, RAMP = 6, BEAM = .3;

        static bool AnyAsphalt(List<ModelJ> ms, double x, double z) => LaneProfile.OnAsphalt(ms, x, z);
        static bool Solid(List<ModelJ> near, double x, double z, out string what)
        {
            foreach (var m in near)
            {
                if (LaneProfile.Hit(m, "asphalt", x, z)) { what = "asphalt under the rail (" + m.key + ")"; return true; }
                if (LaneProfile.Hit(m, "island", x, z)) { what = "island / kerb (" + m.key + ")"; return true; }
                if (LaneProfile.Hit(m, "sidewalk", x, z)) { what = "sidewalk / kerb (" + m.key + ")"; return true; }
            }
            what = null; return false;
        }
        static double Ground(List<ModelJ> near, double x, double z)
        {
            foreach (var m in near) { double h = m.Surface(x, z); if (!double.IsNaN(h)) return h; }
            return double.NaN;
        }

        /// point / right-of-travel normal at arc s of polyline p (game frame)
        static void At(List<double[]> p, double s, out double x, out double z, out double tx, out double tz)
        {
            double acc = 0; x = p[0][0]; z = p[0][1]; tx = 1; tz = 0;
            for (int i = 0; i + 1 < p.Count; i++)
            {
                double dx = p[i + 1][0] - p[i][0], dz = p[i + 1][1] - p[i][1], L = Math.Sqrt(dx * dx + dz * dz); if (L < 1e-9) continue;
                tx = dx / L; tz = dz / L;
                if (acc + L >= s || i + 2 == p.Count) { double t = Math.Max(0, Math.Min(L, s - acc)); x = p[i][0] + tx * t; z = p[i][1] + tz * t; return; }
                acc += L;
            }
        }
        static double Len(List<double[]> p) { double L = 0; for (int i = 0; i + 1 < p.Count; i++) L += Math.Sqrt(Math.Pow(p[i + 1][0] - p[i][0], 2) + Math.Pow(p[i + 1][1] - p[i][1], 2)); return L; }

        public static string Build(List<ModelJ> models, Dictionary<string, GameObject> gos, Material steel, PhysicsMaterial railPhys)
        {
            var sb = new StringBuilder(); int nExt = 0; double total = 0;
            foreach (var T in models)
            {
                if (!T.tile || T.rails.Count == 0 || !gos.TryGetValue(T.key, out var tgo)) continue;
                for (int ri = 0; ri < T.rails.Count; ri++)
                    for (int end = 0; end < 2; end++)
                    {
                        var R = T.rails[ri]; if (R.Count < 2) continue;
                        var q = end == 0 ? R[0] : R[R.Count - 1];
                        // outward direction: from a point ≥ 4 m back along the rail
                        double[] qb = null; double acc = 0;
                        for (int k = 1; k < R.Count; k++)
                        {
                            var c = end == 0 ? R[k] : R[R.Count - 1 - k]; var c0 = end == 0 ? R[k - 1] : R[R.Count - k];
                            acc += Math.Sqrt(Math.Pow(c[0] - c0[0], 2) + Math.Pow(c[1] - c0[1], 2)); qb = c; if (acc >= 4) break;
                        }
                        if (acc < 1.5) continue; // stubs (d11 rail0: 2 m)
                        double ux = q[0] - qb[0], uz = q[1] - qb[1], ul = Math.Sqrt(ux * ux + uz * uz); ux /= ul; uz /= ul;
                        var J = models.Find(m => !m.tile && Math.Abs(q[0] - m.x) < m.half + 6 && Math.Abs(q[1] - m.z) < m.half + 6);
                        if (J == null) continue;
                        var near = new List<ModelJ> { J, T };
                        string tag = $"{T.key} rail{ri} {(end == 0 ? "a" : "b")} x={q[0]:F0} → {J.key}";
                        // the junction road the rail runs beside: nearest ground road with a parallel segment within 25 m
                        List<double[]> rd = null; double bestD = 25, s0 = 0, u0 = 0; int sign = 1;
                        int rdi = -1;
                        for (int jr = 0; jr < J.roads.Count; jr++)
                        {
                            var r = J.roads[jr];
                            if (r.lvl != 0 || r.p.Count < 2 || r.n < 2 || r.w < 6) continue; // the D8 carriageways (not turn lanes / side roads)
                            LaneProfile.Project(r.p, q[0], q[1], out double s, out double u);
                            At(r.p, s, out _, out _, out double tx, out double tz);
                            double cos = tx * ux + tz * uz;
                            if (Math.Abs(cos) < .93 || Math.Abs(u) >= bestD) continue;
                            if (s < .5 && cos < 0 || s > Len(r.p) - .5 && cos > 0) continue; // the rail leaves this road's end
                            // the rail end must lie beside the road, not before its start / past its end (Project clamps there)
                            var e0 = s < .5 ? r.p[0] : s > Len(r.p) - .5 ? r.p[r.p.Count - 1] : null;
                            if (e0 != null && Math.Abs((q[0] - e0[0]) * tx + (q[1] - e0[1]) * tz) > 3) continue;
                            bestD = Math.Abs(u); rd = r.p; s0 = s; u0 = u; sign = cos > 0 ? 1 : -1; rdi = jr;
                        }
                        if (rd == null) { sb.AppendLine(tag + ": no junction road along it"); continue; }
                        tag += $" (along road{rdi} s {s0:F0} u {u0:F1})";
                        // road sides beside the rail and their clearance (any model's asphalt)
                        double nx = -uz, nz = ux; // right of the outward direction
                        var sides = new List<(int sd, double c)>();
                        foreach (int sd in new[] { 1, -1 })
                            for (double t = .1; t <= 2.5; t += .1) if (AnyAsphalt(near, q[0] + nx * sd * t, q[1] + nz * sd * t)) { sides.Add((sd, t)); break; }
                        if (sides.Count == 0) { sb.AppendLine(tag + ": no carriageway beside it"); continue; }
                        if (Solid(near, q[0], q[1], out _)) { sb.AppendLine(tag + ": rail end on asphalt already"); continue; }
                        // the rail's top above the asphalt beside it, at the seam
                        double topQ = Math.Max(q[3], (end == 0 ? R[Math.Min(2, R.Count - 1)] : R[Math.Max(0, R.Count - 3)])[3]);
                        double g0 = Ground(near, q[0] + nx * sides[0].sd * (sides[0].c + .5), q[1] + nz * sides[0].sd * (sides[0].c + .5));
                        double hTop = double.IsNaN(g0) ? .95 : Math.Max(.6, Math.Min(1.1, topQ - g0));
                        var pts = new List<(double x, double z, double g)>(); string why = "400 m";
                        double gPrev = double.IsNaN(g0) ? topQ - .95 : g0;
                        pts.Add((q[0], q[1], gPrev));
                        double L = Len(rd);
                        for (int k = 1; k <= MAXLEN / STEP; k++)
                        {
                            double s = s0 + sign * k * STEP;
                            if (s < 0 || s > L) { why = "road end"; break; }
                            At(rd, s, out double ax, out double az, out double tx, out double tz);
                            double rx = -tz, rz = tx; // right of the junction road's travel
                            double px = ax + rx * u0, pz = az + rz * u0;
                            double lnx = -tz * sign, lnz = tx * sign; // right of the outward direction
                            if (Solid(near, px, pz, out var w)) { why = $"{w} at x={px:F0}"; break; }
                            bool ok = true;
                            foreach (var (sd, c) in sides)
                            {
                                bool any = false;
                                for (double t = .1; t <= c + 1.2 && !any; t += .1) any = AnyAsphalt(near, px + lnx * sd * t, pz + lnz * sd * t);
                                if (!any) { why = "carriageway beside it ends"; ok = false; break; }
                                int mk = 0;
                                foreach (double t in new[] { c + .8, c + 1.8, c + 2.8 }) if (LaneProfile.Hit(J, "markings", px + lnx * sd * t, pz + lnz * sd * t)) mk++;
                                if (mk >= 2) { why = "pedestrian crossing / stop markings beside it"; ok = false; break; }
                            }
                            if (!ok) break;
                            double gi = Ground(near, px + lnx * sides[0].sd * (sides[0].c + .5), pz + lnz * sides[0].sd * (sides[0].c + .5));
                            if (!double.IsNaN(gi) && Math.Abs(gi - gPrev) < .5) gPrev = gi;
                            pts.Add((px, pz, gPrev));
                        }
                        // stay BACK short of the opening
                        int drop = (int)Math.Ceiling(BACK / STEP); if (why == "400 m" || why == "road end") drop = 0;
                        if (pts.Count - drop < 3) { sb.AppendLine($"{tag}: none ({why} right at the seam)"); continue; }
                        pts.RemoveRange(pts.Count - drop, drop);
                        double ext = (pts.Count - 1) * STEP;
                        var go = Geometry(tgo, pts, hTop, steel, railPhys, ri, end);
                        nExt++; total += ext;
                        sb.AppendLine($"{tag}: +{ext:F0} m to x={pts[pts.Count - 1].x:F0} z={pts[pts.Count - 1].z:F0} (stops: {why}), top {hTop:F2} m");
                    }
            }
            sb.Insert(0, $"[Magistrala] rail seams: {nExt} extensions, {total:F0} m\n");
            return sb.ToString();
        }

        static GameObject Geometry(GameObject parent, List<(double x, double z, double g)> pts, double hTop, Material steel, PhysicsMaterial phys, int ri, int end)
        {
            var go = new GameObject($"rail-ext {ri}{(end == 0 ? "a" : "b")}"); go.isStatic = true; go.transform.SetParent(parent.transform, false);
            var o = W.U(pts[0].x, pts[0].g, pts[0].z); go.transform.position = o; go.transform.rotation = Quaternion.identity;
            int n = pts.Count; double ext = (n - 1) * STEP, ramp = Math.Min(RAMP, ext * .6);
            var P = new Vector3[n]; var top = new float[n]; var gnd = new float[n];
            for (int i = 0; i < n; i++)
            {
                double s = i * STEP, f = ramp > 0 ? Math.Max(0, Math.Min(1, (ext - s) / ramp)) : 1;
                P[i] = W.U(pts[i].x, pts[i].g, pts[i].z) - o; gnd[i] = P[i].y;
                top[i] = (float)(pts[i].g + (hTop + .03) * (f * f * (3 - 2 * f)) - .03) - o.y; // smooth slope, its end 3 cm in the ground
            }
            var V = new List<Vector3>(); var N = new List<Vector3>(); var UV = new List<Vector2>(); var I = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 nn)
            {
                int k = V.Count; V.Add(a); V.Add(b); V.Add(c); V.Add(d); for (int q = 0; q < 4; q++) N.Add(nn);
                UV.Add(new Vector2(0, 0)); UV.Add(new Vector2(1, 0)); UV.Add(new Vector2(1, 1)); UV.Add(new Vector2(0, 1));
                // both windings are not needed: faces are emitted with their outward normal, wound for it
                var fn = Vector3.Cross(b - a, c - a); if (Vector3.Dot(fn, nn) >= 0) { I.Add(k); I.Add(k + 1); I.Add(k + 2); I.Add(k); I.Add(k + 2); I.Add(k + 3); }
                else { I.Add(k); I.Add(k + 2); I.Add(k + 1); I.Add(k); I.Add(k + 3); I.Add(k + 2); }
            }
            const float th = .03f;
            for (int i = 0; i + 1 < n; i++)
            {
                var d = P[i + 1] - P[i]; d.y = 0; var t = d.normalized; var r = new Vector3(t.z, 0, -t.x) * th;
                Vector3 a0 = P[i], a1 = P[i + 1];
                float b0 = Mathf.Max(gnd[i] - .05f, top[i] - (float)BEAM), b1 = Mathf.Max(gnd[i + 1] - .05f, top[i + 1] - (float)BEAM);
                Vector3 T0 = new Vector3(a0.x, top[i], a0.z), T1 = new Vector3(a1.x, top[i + 1], a1.z), B0 = new Vector3(a0.x, b0, a0.z), B1 = new Vector3(a1.x, b1, a1.z);
                Quad(B0 + r, B1 + r, T1 + r, T0 + r, r.normalized);
                Quad(B0 - r, T0 - r, T1 - r, B1 - r, -r.normalized);
                Quad(T0 - r, T0 + r, T1 + r, T1 - r, Vector3.up);
                Quad(B0 - r, B1 - r, B1 + r, B0 + r, Vector3.down);
                if (i == 0) Quad(B0 - r, B0 + r, T0 + r, T0 - r, -t);
            }
            // posts every 4 m where the beam is at (nearly) full height, the first 2 m from the seam
            for (double s = 2; s < ext; s += 4)
            {
                int i = (int)Math.Round(s / STEP); if (i >= n - 1) break;
                if (top[i] - gnd[i] < hTop * .7) break;
                var d = P[i + 1] - P[i]; d.y = 0; var t = d.normalized; var r = new Vector3(t.z, 0, -t.x);
                var c = P[i]; float y0 = gnd[i] - .2f, y1 = top[i] - .02f; Vector3 ex = t * .05f, ey = r * .06f;
                Vector3 Pp(Vector3 e1, Vector3 e2, float y) => new Vector3(c.x, y, c.z) + e1 + e2;
                Quad(Pp(-ex, ey, y0), Pp(ex, ey, y0), Pp(ex, ey, y1), Pp(-ex, ey, y1), r);
                Quad(Pp(-ex, -ey, y0), Pp(-ex, -ey, y1), Pp(ex, -ey, y1), Pp(ex, -ey, y0), -r);
                Quad(Pp(ex, -ey, y0), Pp(ex, -ey, y1), Pp(ex, ey, y1), Pp(ex, ey, y0), t);
                Quad(Pp(-ex, -ey, y0), Pp(-ex, ey, y0), Pp(-ex, ey, y1), Pp(-ex, -ey, y1), -t);
            }
            var mesh = new Mesh { name = go.name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(V); mesh.SetNormals(N); mesh.SetUVs(0, UV); mesh.SetTriangles(I, 0); mesh.RecalculateBounds();
            string path = $"{WorldBuilder.GEN}/Roads/{parent.name}-railext-{ri}{(end == 0 ? "a" : "b")}.asset";
            if (UnityEditor.AssetDatabase.LoadMainAssetAtPath(path) != null) UnityEditor.AssetDatabase.DeleteAsset(path);
            UnityEditor.AssetDatabase.CreateAsset(mesh, path);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = steel; mr.shadowCastingMode = ShadowCastingMode.On;
            // colliders: as the section rails (WorldBuilder.Roads), lower on the sloped end
            for (int i = 0; i + 1 < n; i++)
            {
                var a = P[i] + o; var b = P[i + 1] + o; var d = b - a; d.y = 0; float L = d.magnitude; if (L < .05f) continue;
                float y0 = Mathf.Min(gnd[i], gnd[i + 1]) + o.y, tp = Mathf.Max(top[i], top[i + 1]) + o.y;
                bool full = Mathf.Min(top[i], top[i + 1]) - Mathf.Max(gnd[i], gnd[i + 1]) > hTop * .9f;
                float y1 = full ? Mathf.Max(tp, y0 + 1.4f) : Mathf.Max(y0 + .3f, tp + .5f);
                var seg = new GameObject("r"); seg.isStatic = true; seg.transform.SetParent(go.transform, false);
                seg.transform.position = new Vector3((a.x + b.x) / 2, (y0 + y1) / 2, (a.z + b.z) / 2);
                seg.transform.rotation = Quaternion.LookRotation(d);
                var bc = seg.AddComponent<BoxCollider>(); bc.size = new Vector3(.24f, y1 - y0, L + .2f); bc.sharedMaterial = phys;
            }
            return go;
        }
    }
}
