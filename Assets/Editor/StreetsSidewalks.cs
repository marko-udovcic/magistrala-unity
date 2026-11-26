// Sidewalks (pločnici) beyond the OSM tags, for StreetsBuilder (Build streets):
//   * built-up land: a building footprint (the tiles' BUILDINGS that BuildingsFromTiles builds, the package's buildings) within
//     ~6–40 m of the street on that side, ±30 m along; rural stretches get none
//   * walls at the asphalt: the footway narrows to the nearest footprint / model guardrail (0.1 m off it), dropped under 1 m
//   * Marko's road models (World/Roads) whose roads carry no sidewalk of their own on a side (the old road through Štafilić …
//     Lukšić, the link roads, the D8 tiles' side streets — never the D8): our own kerb + footway strip along the model asphalt's
//     edge, found on a 0.2 m raster of the model meshes and of our own streets (no overlap with model paving, model / our
//     asphalt, courts), at the asphalt edge's height; lowered kerbs (ramps to 2 cm) at every run end that meets a street /
//     driveway, the kerb 0.15 m mountable as on our streets (the same collider: the footway's top)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static partial class StreetsBuilder
    {
        // ------------------------------------------------------------------ footprints (2D polygons, game x / z) on a 16 m grid
        static List<double[]> feet; // x0 z0 x1 z1 … (closed ring implied)
        static Dictionary<(int, int), List<int>> footGrid; const double FG = 16;
        static List<List<double[]>> railsAll; static Dictionary<(int, int), List<(int r, int i)>> railGrid;
        static (int, int) FKey(double x, double z) => ((int)Math.Floor(x / FG), (int)Math.Floor(z / FG));
        static void AddFoot(double[] P)
        {
            int id = feet.Count; feet.Add(P);
            double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
            for (int i = 0; i < P.Length; i += 2) { x0 = Math.Min(x0, P[i]); x1 = Math.Max(x1, P[i]); z0 = Math.Min(z0, P[i + 1]); z1 = Math.Max(z1, P[i + 1]); }
            for (int gx = (int)Math.Floor(x0 / FG); gx <= (int)Math.Floor(x1 / FG); gx++)
                for (int gz = (int)Math.Floor(z0 / FG); gz <= (int)Math.Floor(z1 / FG); gz++)
                { if (!footGrid.TryGetValue((gx, gz), out var l)) footGrid[(gx, gz)] = l = new List<int>(); l.Add(id); }
        }
        static string LoadFeet()
        {
            feet = new List<double[]>(); footGrid = new Dictionary<(int, int), List<int>>();
            var wm = WorldData.Models(-1e9, 1e9);
            int nT = 0, nSkip = 0, nP = 0;
            for (int tz = WorldBuilder.TZ0; tz <= WorldBuilder.TZ1; tz++)
                for (int tx = WorldBuilder.TX0; tx <= WorldBuilder.TX1; tx++)
                {
                    var tile = Tiles.Get(tx, tz); if (tile?.buildingsJson == null) continue;
                    foreach (var bo in MiniJson.L(MiniJson.Parse(tile.buildingsJson)))
                    {
                        var pts = MiniJson.L(MiniJson.O(bo)["pts"]); if (pts.Count < 3) continue;
                        var P = new double[pts.Count * 2]; double cx = 0, cz = 0;
                        for (int i = 0; i < pts.Count; i++) { var q = MiniJson.L(pts[i]); P[2 * i] = MiniJson.D(q[0]); P[2 * i + 1] = MiniJson.D(q[1]); cx += P[2 * i]; cz += P[2 * i + 1]; }
                        cx /= pts.Count; cz /= pts.Count;
                        // (BuildingsFromTiles: a footprint on / next to a model's paving is not built)
                        bool onFoot = WorldData.FootDist(wm, cx, cz) < 3;
                        for (int i = 0; i < P.Length && !onFoot; i += 2) if (WorldData.FootDist(wm, P[i], P[i + 1]) < 1.5) onFoot = true;
                        if (onFoot) { nSkip++; continue; }
                        AddFoot(P); nT++;
                    }
                }
            var mb = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "model-buildings.json")));
            foreach (var oo in MiniJson.L(mb["objects"]))
            {
                var o = MiniJson.O(oo); if (MiniJson.G(o, "k") as string != "bld") continue;
                double x = MiniJson.D(o["x"]), z = MiniJson.D(o["z"]), a = MiniJson.D(MiniJson.G(o, "a") ?? 0.0), hl = MiniJson.D(MiniJson.G(o, "l") ?? 2.0) / 2, hw = MiniJson.D(MiniJson.G(o, "w") ?? 2.0) / 2;
                double tx = Math.Cos(a), tz = Math.Sin(a), nx = -tz, nz = tx;
                AddFoot(new[] { x - tx * hl - nx * hw, z - tz * hl - nz * hw, x + tx * hl - nx * hw, z + tz * hl - nz * hw, x + tx * hl + nx * hw, z + tz * hl + nz * hw, x - tx * hl + nx * hw, z - tz * hl + nz * hw });
                nP++;
            }
            // the models' guardrails (rails: x y z …): no footway behind a guardrail at the asphalt
            railsAll = new List<List<double[]>>(); railGrid = new Dictionary<(int, int), List<(int, int)>>();
            foreach (var m in wm)
                foreach (var r in m.rails)
                {
                    int id = railsAll.Count; railsAll.Add(r);
                    for (int i = 0; i + 1 < r.Count; i++)
                    {
                        var p = r[i]; var q = r[i + 1];
                        for (int gx = (int)Math.Floor(Math.Min(p[0], q[0]) / FG); gx <= (int)Math.Floor(Math.Max(p[0], q[0]) / FG); gx++)
                            for (int gz = (int)Math.Floor(Math.Min(p[2], q[2]) / FG); gz <= (int)Math.Floor(Math.Max(p[2], q[2]) / FG); gz++)
                            { if (!railGrid.TryGetValue((gx, gz), out var l)) railGrid[(gx, gz)] = l = new List<(int, int)>(); l.Add((id, i)); }
                    }
                }
            return $"sidewalk footprints: {nT} tile buildings ({nSkip} on model paving, not built), {nP} package buildings, {railsAll.Count} model guardrails";
        }
        static bool InFoot(double[] P, double x, double z)
        {
            bool hit = false; int n = P.Length / 2;
            for (int i = 0, k = n - 1; i < n; k = i++)
            {
                double ax = P[2 * i], az = P[2 * i + 1], bx = P[2 * k], bz = P[2 * k + 1];
                if ((az > z) != (bz > z) && x < (bx - ax) * (z - az) / (bz - az) + ax) hit = !hit;
            }
            return hit;
        }
        /// inside a building footprint or within 0.3 m of a model guardrail
        static bool Obstacle(double x, double z)
        {
            if (footGrid.TryGetValue(FKey(x, z), out var l)) foreach (int i in l) if (InFoot(feet[i], x, z)) return true;
            if (railGrid.TryGetValue(FKey(x, z), out var rl))
                foreach (var (r, i) in rl)
                {
                    var p = railsAll[r][i]; var q = railsAll[r][i + 1]; double bx = q[0] - p[0], bz = q[2] - p[2], l2 = bx * bx + bz * bz;
                    double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((x - p[0]) * bx + (z - p[2]) * bz) / l2));
                    if (Sq(x - p[0] - bx * t) + Sq(z - p[2] - bz * t) < .09) return true;
                }
            return false;
        }
        /// how far from the node the sidewalk on `side` of edge `ei` keeps off at its a / b end: 0 when no arm turns off on that side
        /// (the far side of a T, a bend, a degree-2 node: the footway runs on), else past the kerb fillet of the neighbouring arm on
        /// that side (its half width / sin θ + fillet radius / tan θ/2 + 1 m, at most the old both-sides clearance NodeR)
        static double SideClear(int ei, bool atA, int side)
        {
            var e = edges[ei]; long nid = atA ? e.a : e.b; int ni = nIdx[nid];
            if (RbR(nid) > 0) return NodeR(nid);
            if (adj[ni].Count < 3) return 0;
            double Ang(int k, bool fromA)
            {
                var q = edges[k]; var p = PointAt(k, fromA ? Math.Min(6, q.L / 2) : Math.Max(0, q.L - Math.Min(6, q.L / 2)));
                var nd = nodes[ni]; return Math.Atan2(p.z - nd.z, p.x - nd.x);
            }
            double me = Ang(ei, atA); int sg = atA ? side : -side;
            double best = double.MaxValue; int bk = -1;
            foreach (int k in adj[ni])
            {
                if (k == ei) continue; var q = edges[k];
                foreach (bool fa in new[] { true, false })
                {
                    if (nIdx[fa ? q.a : q.b] != ni) continue;
                    double d = sg > 0 ? Ang(k, fa) - me : me - Ang(k, fa);
                    while (d <= 0) d += Math.PI * 2; while (d > Math.PI * 2) d -= Math.PI * 2;
                    if (d < best) { best = d; bk = k; }
                }
            }
            if (bk < 0 || best >= 2.6) return 0;
            if (best < .8) return NodeR(nid);
            var o = edges[bk]; bool site = o.spur != null && Math.Abs(o.sp.laneW - 2.75) < 1e-6 && o.sp.shoulder == 0;
            double r = o.spur != null && !site ? 1.5 : Math.Max(Math.Max(KerbRadius(e.type), KerbRadius(o.type)), site ? 9 : 0);
            return Math.Min(Math.Max(2, NodeR(nid)), o.sp.halfWidth / Math.Sin(best) + r / Math.Tan(best / 2) + 1);
        }
        const double WALK_W = 1.6, WALK_MIN = 1.0;
        /// footway width at an asphalt edge point (outward rx, rz): up to the first footprint / guardrail, 0 = none fits
        static double FitWidth(double ex, double ez, double rx, double rz)
        {
            for (double d = .2; d <= WALK_W + .1; d += .1)
                if (Obstacle(ex + rx * d, ez + rz * d)) { double w = d - .15; return w >= WALK_MIN ? Math.Min(WALK_W, w) : 0; }
            return WALK_W;
        }
        /// built-up on this side: a footprint vertex within 26 m of the point 20 m out from the asphalt edge (≈ 0–46 m off the road)
        static bool BuiltUp(double ex, double ez, double rx, double rz)
        {
            double px = ex + rx * 20, pz = ez + rz * 20, R2 = 26 * 26;
            for (int gx = (int)Math.Floor((px - 26) / FG); gx <= (int)Math.Floor((px + 26) / FG); gx++)
                for (int gz = (int)Math.Floor((pz - 26) / FG); gz <= (int)Math.Floor((pz + 26) / FG); gz++)
                {
                    if (!footGrid.TryGetValue((gx, gz), out var l)) continue;
                    foreach (int i in l) { var P = feet[i]; for (int k = 0; k < P.Length; k += 2) if (Sq(P[k] - px) + Sq(P[k + 1] - pz) < R2) return true; }
                }
            return false;
        }
        /// dilate a per-sample flag by `r` samples both ways
        static bool[] Dilate(bool[] a, int r)
        {
            var o = new bool[a.Length]; int last = -100000;
            for (int i = 0; i < a.Length; i++) { if (a[i]) last = i; o[i] = i - last <= r; }
            last = 100000; for (int i = a.Length - 1; i >= 0; i--) { if (a[i]) last = i; if (last - i <= r) o[i] = true; }
            return o;
        }

        // ------------------------------------------------------------------ village statistics (game x bands along the old road)
        static readonly (string name, double x1)[] VILLAGES = { ("Štafilić", -9050), ("Novi", -8350), ("Stari", -7400), ("Lukšić", -5900), ("Kambelovac", -4100), ("Gomilica", -2650), ("Sućurac", 1e9) };
        static Dictionary<string, double> walkOurs, walkModel;
        static void CountWalk(Dictionary<string, double> d, double x, double z, double len)
        {
            if (z < -6144 || z > -4096) return;
            string v = VILLAGES.First(q => x < q.x1).name; d.TryGetValue(v, out double c); d[v] = c + len;
        }
        static string WalkStats() => "sidewalk km by village (ours | model strips): " + string.Join(", ", VILLAGES.Select(v =>
            $"{v.name} {(walkOurs.TryGetValue(v.name, out var a) ? a : 0) / 1000:F2} | {(walkModel.TryGetValue(v.name, out var b) ? b : 0) / 1000:F2}"));

        // ------------------------------------------------------------------ strips along the models' roads
        static float TerrainY(double gx, double gz)
        {
            var u = W.U(gx, 0, gz);
            foreach (var t in Terrain.activeTerrains)
            {
                var p = t.transform.position; var s = t.terrainData.size;
                if (u.x >= p.x && u.x <= p.x + s.x && u.z >= p.z && u.z <= p.z + s.z) return t.SampleHeight(u) + p.y;
            }
            return float.NaN;
        }
        class MRaster
        {
            public double x0, z0; public int nx, nz; public const double C = .2;
            public byte[] c; public float[] y; // c: 0 none, 1 model asphalt, 2 model paving / concrete, 3 ours (asphalt, court, footway)
            public int Idx(double x, double z) { int i = (int)Math.Floor((x - x0) / C), j = (int)Math.Floor((z - z0) / C); return i < 0 || j < 0 || i >= nx || j >= nz ? -1 : j * nx + i; }
            public byte At(double x, double z) { int k = Idx(x, z); return k < 0 ? (byte)255 : c[k]; }
            public void Fill(Vector3 a, Vector3 b, Vector3 d, byte cls)
            {
                var n = Vector3.Cross(b - a, d - a); if (n.sqrMagnitude < 1e-10f || Mathf.Abs(n.normalized.y) < .5f) return;
                // game coords
                double ax = a.x + W.OX, az = -a.z + W.OZ, bx = b.x + W.OX, bz = -b.z + W.OZ, dx = d.x + W.OX, dz = -d.z + W.OZ;
                double minx = Math.Min(ax, Math.Min(bx, dx)), maxx = Math.Max(ax, Math.Max(bx, dx)), minz = Math.Min(az, Math.Min(bz, dz)), maxz = Math.Max(az, Math.Max(bz, dz));
                int i0 = Math.Max(0, (int)Math.Floor((minx - x0) / C)), i1 = Math.Min(nx - 1, (int)Math.Floor((maxx - x0) / C));
                int j0 = Math.Max(0, (int)Math.Floor((minz - z0) / C)), j1 = Math.Min(nz - 1, (int)Math.Floor((maxz - z0) / C));
                if (i0 > i1 || j0 > j1) return;
                double den = (bz - dz) * (ax - dx) + (dx - bx) * (az - dz); if (Math.Abs(den) < 1e-12) return;
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        double px = x0 + (i + .5) * C, pz = z0 + (j + .5) * C;
                        double w1 = ((bz - dz) * (px - dx) + (dx - bx) * (pz - dz)) / den, w2 = ((dz - az) * (px - dx) + (ax - dx) * (pz - dz)) / den, w3 = 1 - w1 - w2;
                        if (w1 < -1e-6 || w2 < -1e-6 || w3 < -1e-6) continue;
                        int k = j * nx + i; float yy = (float)(w1 * a.y + w2 * b.y + w3 * d.y);
                        if (cls == 1) { if (c[k] == 0 || c[k] == 1) { c[k] = 1; y[k] = Math.Max(y[k], yy); } else if (c[k] == 2 && yy > y[k] + .05f) { c[k] = 1; y[k] = yy; } }
                        else if (cls == 2) { if (c[k] != 3 && (c[k] != 1 || yy > y[k] + .05f)) { c[k] = 2; y[k] = yy; } }
                        else c[k] = 3;
                    }
            }
        }
        static List<double[]> walkLow = new List<double[]>(), walkRaise = new List<double[]>();
        static readonly List<double[]> gravelCover = new List<double[]>();
        static double modelShoulderMetres;
        static int stripRuns; static double stripLen; static int stripSkipModel, stripSkipConflict, stripSkipRural, stripSkipWall, stripSkipGround;
        static string BuildModelWalks()
        {
            modelShoulderMetres=0;
            walkModel = new Dictionary<string, double>(); walkLow.Clear(); walkRaise.Clear(); stripRuns = 0; stripLen = 0; stripSkipModel = stripSkipConflict = stripSkipRural = stripSkipWall = stripSkipGround = 0;
            var roadsT = GameObject.Find("World")?.transform.Find("Roads"); if (!roadsT) return "model sidewalks: no World/Roads";
            var meshes = new List<(MeshFilter mf, MeshRenderer mr, Bounds b)>();
            foreach (var mr in roadsT.GetComponentsInChildren<MeshRenderer>()) { var mf = mr.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh) meshes.Add((mf, mr, mr.bounds)); }
            foreach (var m in models)
            {
                var j = m.j; if (!InArea(j.x, j.z) || j.roads.Count == 0) continue;
                double pad = 6; var R = new MRaster { x0 = j.x - j.half - pad, z0 = j.z - j.half - pad };
                R.nx = R.nz = (int)Math.Ceiling((2 * j.half + 2 * pad) / MRaster.C); R.c = new byte[R.nx * R.nz]; R.y = new float[R.nx * R.nz];
                for (int k = 0; k < R.y.Length; k++) R.y[k] = -1e6f;
                Vector3 ua = W.U(R.x0, 0, R.z0 + R.nz * MRaster.C), ub = W.U(R.x0 + R.nx * MRaster.C, 0, R.z0);
                var bb = new Bounds(); bb.SetMinMax(new Vector3(ua.x, -1e4f, ua.z), new Vector3(ub.x, 1e4f, ub.z));
                foreach (var (mf, mr, b) in meshes)
                {
                    if (!b.Intersects(bb)) continue;
                    var mesh = mf.sharedMesh; var v = mesh.vertices; var M = mr.transform.localToWorldMatrix; var wv = new Vector3[v.Length];
                    for (int i = 0; i < v.Length; i++) wv[i] = M.MultiplyPoint3x4(v[i]);
                    var mats = mr.sharedMaterials;
                    for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                    {
                        string nm = mats[s] ? mats[s].name : ""; byte cls = nm.StartsWith("Asphalt") || nm.StartsWith("Paint") ? (byte)1 : nm.StartsWith("Concrete") || nm.StartsWith("Paving") || nm == "Ceste_rubnjak" || nm == "Ceste_otok" ? (byte)2 : (byte)0;
                        if (cls == 0) continue;
                        var t = mesh.GetTriangles(s);
                        for (int i = 0; i < t.Length; i += 3) R.Fill(wv[t[i]], wv[t[i + 1]], wv[t[i + 2]], cls);
                    }
                }
                // our streets, junction discs, courts, footways (all drawn by now)
                foreach (var kv in chunks)
                {
                    float cx0 = kv.Key.Item1 * CHUNK, cz0 = kv.Key.Item2 * CHUNK;
                    if (cx0 > ub.x || cx0 + CHUNK < ua.x || cz0 > ub.z || cz0 + CHUNK < ua.z) continue;
                    foreach (int mat in new[] { MAT_ASPHALT, MAT_COURT, MAT_WALK, MAT_KERB })
                    {
                        var vl = kv.Value.v[mat];
                        for (int i = 0; i + 2 < vl.Count; i += 3) R.Fill(vl[i], vl[i + 1], vl[i + 2], 3);
                    }
                }
                foreach (var road in j.roads)
                {
                    StripsAlong(m, R, road.p, road.w);
                    if (road.lvl==0) ShouldersAlong(R,road.p,road.w);
                }
            }
            return $"model gravel shoulders: {modelShoulderMetres:F0} m; model sidewalks: {stripRuns} runs, {stripLen / 1000:F2} km (samples skipped: {stripSkipModel} the model has one, {stripSkipConflict} paving / street in the way, {stripSkipRural} not built-up, {stripSkipWall} wall / guardrail at the asphalt, {stripSkipGround} ground off the kerb)";
        }

        // Same verge material/submesh as generated streets. Follow the real model asphalt, never json width alone.
        static G GravelPoint(G edge,double rx,double rz,double u)
        {
            double x=edge.x+rx*u,z=edge.z+rz*u;
            float ground=TerrainY(x,z);
            double y=edge.y-.04*u;
            if (!float.IsNaN(ground)) y=u>=1.6 ? ground+.02 : Math.Max(y,ground+.025);
            if(u<=0) y=edge.y+.012;
            return new G(x,y,z);
        }
        static void GravelStrip(G a,G b,double arx,double arz,double brx,double brz,Drop drop)
        {
            foreach(var range in new[] {(0.0,.8),(.8,1.6)})
                Quad(MAT_VERGE,GravelPoint(a,arx,arz,range.Item1),GravelPoint(b,brx,brz,range.Item1),
                    GravelPoint(b,brx,brz,range.Item2),GravelPoint(a,arx,arz,range.Item2),Vector3.up,drop,false);
            // Bury the last 40 cm: terrain carving happens after mesh generation and can lower the outer edge.
            G Skirt(G e,double rx,double rz)
            {
                var p=GravelPoint(e,rx,rz,2); float ground=TerrainY(p.x,p.z);
                return new G(p.x,float.IsNaN(ground) ? e.y-.4 : ground-.4,p.z);
            }
            Quad(MAT_VERGE,GravelPoint(a,arx,arz,1.6),GravelPoint(b,brx,brz,1.6),
                Skirt(b,brx,brz),Skirt(a,arx,arz),Vector3.up,drop,false);
            // Only the footprint/vegetation mask uses this capsule; it must not change terrain heights.
            gravelCover.Add(new[]{a.x+arx*.8,a.z+arz*.8,a.y,b.x+brx*.8,b.z+brz*.8,b.y,.8});
        }
        static void ShouldersAlong(MRaster raster,List<double[]> poly,double width)
        {
            foreach(int side in new[]{-1,1})
            {
                G? previous=null; double oldRx=0,oldRz=0;
                for(int seg=0;seg+1<poly.Count;seg++)
                {
                    var a=poly[seg];var b=poly[seg+1];double dx=b[0]-a[0],dz=b[1]-a[1],length=Math.Sqrt(dx*dx+dz*dz);
                    if(length<.01) continue;
                    double rx=-dz/length*side,rz=dx/length*side;
                    int count=Math.Max(1,(int)Math.Ceiling(length));
                    for(int k=0;k<count;k++)
                    {
                        double f=(double)k/count,cx=a[0]+dx*f,cz=a[1]+dz*f;
                        if(raster.At(cx,cz)!=1) {previous=null;continue;}
                        double last=-1;int miss=0;
                        for(double u=0;u<=width/2+3;u+=.1)
                        {
                            if(raster.At(cx+rx*u,cz+rz*u)==1) {last=u;miss=0;}
                            else if(++miss>=3) break;
                        }
                        double edge=last+.05;
                        if(last<0 || edge<width/2-1.5 || edge>width/2+1.2) {previous=null;continue;}
                        double ex=cx+rx*edge,ez=cz+rz*edge;
                        var hit=Nearest(cx,cz,8);
                        bool d8=hit.ei>=0 && (IsD8(edges[hit.ei]) || Regex.IsMatch(edges[hit.ei].rf,@"^D\s?8$"));
                        bool clear=d8 || !BuiltUp(ex,ez,rx,rz);
                        for(double u=.25;u<=1.7 && clear;u+=.2)
                            if(raster.At(ex+rx*u,ez+rz*u)!=0 || Obstacle(ex+rx*u,ez+rz*u)) clear=false;
                        int id=raster.Idx(cx+rx*(edge-.2),cz+rz*(edge-.2));
                        if(!clear || id<0 || raster.c[id]!=1) {previous=null;continue;}
                        double y=raster.y[id];float ground=TerrainY(ex+rx,ez+rz);
                        if(!float.IsNaN(ground) && Math.Abs(ground-y)>.65) {previous=null;continue;}
                        var pt=new G(ex,y,ez);
                        if(previous.HasValue)
                        {
                            var old=previous.Value;double distance=Math.Sqrt(Sq(pt.x-old.x)+Sq(pt.z-old.z));
                            if(distance<2.5 && Math.Abs(pt.y-old.y)<.4)
                            {
                                GravelStrip(old,pt,oldRx,oldRz,rx,rz,Drop.None);
                                modelShoulderMetres+=distance;
                            }
                        }
                        previous=pt;oldRx=rx;oldRz=rz;
                    }
                }
            }
        }

        static void StripsAlong(MX m, MRaster R, List<double[]> poly, double w)
        {
            // resample every metre
            var P = new List<double[]>();
            for (int i = 0; i + 1 < poly.Count; i++)
            {
                double ax = poly[i][0], az = poly[i][1], bx = poly[i + 1][0], bz = poly[i + 1][1], l = Math.Sqrt(Sq(bx - ax) + Sq(bz - az));
                int n = Math.Max(1, (int)Math.Ceiling(l)); for (int k = 0; k < n; k++) P.Add(new[] { ax + (bx - ax) * k / n, az + (bz - az) * k / n });
            }
            P.Add(new[] { poly[poly.Count - 1][0], poly[poly.Count - 1][1] });
            int N = P.Count; if (N < 8) return;
            // which road is it: the D8 / ramps / driveways never get one
            int good = 0, bad = 0;
            for (int i = 0; i < N; i += 5)
            {
                var h = Nearest(P[i][0], P[i][1], 8);
                if (h.ei < 0) { good++; continue; }
                var e = edges[h.ei]; bool d8 = IsD8(e) || Regex.IsMatch(e.rf, @"^D\s?8$");
                if (d8 || e.type == "motorway" || e.type == "trunk" || e.type == "ramp" || e.spur != null || e.roundabout) bad++; else good++;
            }
            if (bad >= good) return;
            var tx = new double[N]; var tz = new double[N];
            for (int i = 0; i < N; i++)
            {
                var a = P[Math.Max(0, i - 2)]; var b = P[Math.Min(N - 1, i + 2)]; double l = Math.Sqrt(Sq(b[0] - a[0]) + Sq(b[1] - a[1])); if (l < 1e-9) l = 1;
                tx[i] = (b[0] - a[0]) / l; tz[i] = (b[1] - a[1]) / l;
            }
            // built-up: a building on either side, ±30 m
            var builtAny = new bool[N];
            for (int i = 0; i < N; i++)
                foreach (int sd in new[] { -1, 1 }) if (!builtAny[i] && BuiltUp(P[i][0] - tz[i] * sd * w / 2, P[i][1] + tx[i] * sd * w / 2, -tz[i] * sd, tx[i] * sd)) builtAny[i] = true;
            var bu = Dilate(builtAny, 30);
            foreach (int side in new[] { -1, 1 })
            {
                var ok = new bool[N]; var soft = new bool[N]; var E = new double[N]; var Y = new double[N]; var Wd = new double[N]; var Gy = new double[N];
                for (int i = 0; i < N; i++)
                {
                    double cx = P[i][0], cz = P[i][1], rx = -tz[i] * side, rz = tx[i] * side;
                    if (R.At(cx, cz) != 1) { soft[i] = true; continue; } // (centre off the model's asphalt: another tile's / an intersection's)
                    double lastA = -1; int miss = 0;
                    for (double u = 0; u <= w / 2 + 3; u += .1)
                    {
                        var c = R.At(cx + rx * u, cz + rz * u);
                        if (c == 1) { lastA = u; miss = 0; } else if (++miss >= 3) break;
                    }
                    if (lastA < 0) { soft[i] = true; continue; }
                    double e = lastA + .1;
                    if (e > w / 2 + 1.2 || e < w / 2 - 1.5) { stripSkipConflict++; continue; } // a mouth / fillet / median
                    // the model's own sidewalk / island beside the asphalt
                    bool model = false;
                    for (double u = e; u <= e + 2.5 && !model; u += .1) if (R.At(cx + rx * u, cz + rz * u) == 2) model = true;
                    if (model) { stripSkipModel++; continue; }
                    double ex = cx + rx * e, ez = cz + rz * e;
                    double wd = FitWidth(ex, ez, rx, rz); if (wd <= 0) { stripSkipWall++; continue; }
                    bool clash = false;
                    for (double u = .15; u <= wd + .15 && !clash; u += .1)
                        foreach (double al in new[] { -.6, 0, .6 })
                        {
                            byte c = R.At(ex + rx * u + tx[i] * al, ez + rz * u + tz[i] * al);
                            if (c == 1 || c == 2 || c == 3 || c == 255) { clash = true; break; }
                        }
                    if (clash) { stripSkipConflict++; continue; }
                    int ki = R.Idx(cx + rx * (e - .15), cz + rz * (e - .15)); if (ki < 0 || R.c[ki] != 1) { soft[i] = true; continue; }
                    double y = R.y[ki]; float g = TerrainY(ex + rx * (wd + .3), ez + rz * (wd + .3));
                    if (!float.IsNaN(g) && (g < y - 1.2 || g > y + .6)) { stripSkipGround++; soft[i] = true; continue; }
                    ok[i] = true; E[i] = e; Y[i] = y; Wd[i] = wd; Gy[i] = float.IsNaN(g) ? y - .4 : g;
                }
                for (int i = 0; i < N; i++) if (ok[i] && !bu[i]) { ok[i] = false; soft[i] = true; stripSkipRural++; }
                // smoothing (median edge / height over ±2, the narrowest width over ±1), runs under 6 m dropped
                var E2 = (double[])E.Clone(); var Y2 = (double[])Y.Clone(); var W2a = (double[])Wd.Clone();
                for (int i = 0; i < N; i++)
                {
                    if (!ok[i]) continue;
                    var le = new List<double>(); var ly = new List<double>(); double mw = Wd[i];
                    for (int k = Math.Max(0, i - 2); k <= Math.Min(N - 1, i + 2); k++) if (ok[k]) { le.Add(E[k]); ly.Add(Y[k]); if (Math.Abs(k - i) <= 1) mw = Math.Min(mw, Wd[k]); }
                    le.Sort(); ly.Sort(); E2[i] = le[le.Count / 2]; Y2[i] = ly[ly.Count / 2]; W2a[i] = mw;
                }
                // runs joined over gaps under 6 m that nothing hard interrupts (no mouth / paving / street / wall in the strip: a
                // seam, a rural / ground sample), the edge / height / width interpolated; runs under 15 m dropped
                for (int i = 1; i < N - 1; i++)
                {
                    if (ok[i] || !ok[i - 1]) continue;
                    int g1 = i; while (g1 < N && !ok[g1]) g1++;
                    if (g1 >= N || g1 - i >= 6) { i = g1; continue; }
                    bool allSoft = true; for (int k = i; k < g1; k++) if (!soft[k]) allSoft = false;
                    if (allSoft)
                        for (int k = i; k < g1; k++)
                        {
                            double f = (k - (i - 1)) / (double)(g1 - (i - 1));
                            ok[k] = true; E2[k] = E2[i - 1] + (E2[g1] - E2[i - 1]) * f; Y2[k] = Y2[i - 1] + (Y2[g1] - Y2[i - 1]) * f; W2a[k] = Math.Min(W2a[i - 1], W2a[g1]); Gy[k] = Math.Min(Gy[i - 1], Gy[g1]);
                        }
                    i = g1;
                }
                for (int i = 0; i < N;)
                {
                    if (!ok[i]) { i++; continue; }
                    int j0 = i; while (i < N && ok[i]) i++; int j1 = i - 1;
                    if (j1 - j0 < 15) continue;
                    // lowered kerb at a run end inside the road (the polyline's own ends continue into the next tile / street)
                    bool rampA = j0 > 0, rampB = j1 < N - 1;
                    double Kh(int k) { double d = Math.Min(rampA ? k - j0 : 1e9, rampB ? j1 - k : 1e9); return CesteCrossings.KerbHeight(P[k][0],P[k][1],.02 + .13 * Smooth(d / 1.5)); }
                    G Pt(int k, double du, double dy) { double rx = -tz[k] * side, rz = tx[k] * side, u = E2[k] - .15 + du; return new G(P[k][0] + rx * u, Y2[k] + dy, P[k][1] + rz * u); }
                    for (int k = j0; k < j1; k++)
                    {
                        int q = k + 1; double ka = Kh(k), kb = Kh(q), wa = W2a[k] + .15, wb = W2a[q] + .15;
                        var outA = new Vector3((float)(-tz[k] * side), 0, (float)(-tx[k] * side));
                        double ga = Math.Max(-2, Math.Min(-.4, Gy[k] - Y2[k] - .05)), gb = Math.Max(-2, Math.Min(-.4, Gy[q] - Y2[q] - .05));
                        double ba = Math.Min(.02,ka*.25), bb = Math.Min(.02,kb*.25);
                        Quad(MAT_KERB, Pt(k, 0, -.25), Pt(q, 0, -.25), Pt(q, 0, kb-bb), Pt(k, 0, ka-ba), -outA, Drop.None, false);
                        const double arc = .2928932188;
                        Quad(MAT_KERB, Pt(k, 0, ka-ba), Pt(q, 0, kb-bb), Pt(q, bb*arc, kb-bb*arc), Pt(k, ba*arc, ka-ba*arc), (Vector3.up-outA*2.4142f).normalized, Drop.None, false);
                        Quad(MAT_KERB, Pt(k, ba*arc, ka-ba*arc), Pt(q, bb*arc, kb-bb*arc), Pt(q, bb, kb), Pt(k, ba, ka), (Vector3.up*2.4142f-outA).normalized, Drop.None, false);
                        Quad(MAT_KERB, Pt(k, ba, ka), Pt(q, bb, kb), Pt(q, .18, kb), Pt(k, .18, ka), Vector3.up, Drop.None, false);
                        Quad(MAT_WALK, Pt(k, .18, ka), Pt(q, .18, kb), Pt(q, wb, kb), Pt(k, wa, ka), Vector3.up, Drop.None, false);
                        Quad(MAT_KERB, Pt(k, wa, ka), Pt(q, wb, kb), Pt(q, wb, gb), Pt(k, wa, ga), outA, Drop.None, false);
                        Quad(-1, Pt(k, 0, ka), Pt(q, 0, kb), Pt(q, wb, kb), Pt(k, wa, ka), Vector3.up, Drop.None, true);
                        var ca = Pt(k, wa / 2, 0); var cb = Pt(q, wb / 2, 0);
                        double len = Math.Sqrt(Sq(cb.x - ca.x) + Sq(cb.z - ca.z)); stripLen += len; CountWalk(walkModel, ca.x, ca.z, len);
                        // the ground: lowered along the asphalt edge (no terrain over the model's asphalt in front of the kerb), raised
                        // under the footway's back and behind it to its top (no floating slab); the strip's own footprint mask
                        var ia = Pt(k, 0, 0); var ib = Pt(q, 0, 0);
                        walkLow.Add(new[] { ia.x, ia.z, ia.y, ib.x, ib.z, ib.y, 1.1 });
                        walkRaise.Add(new[] { ia.x, ia.z, ib.x, ib.z, -tz[k] * side, tx[k] * side, Y2[k] + ka - .03, Y2[q] + kb - .03, Math.Min(wa, wb) });
                    }
                    // end caps
                    foreach (int k in new[] { j0, j1 })
                    {
                        var tv = new Vector3((float)tx[k], 0, (float)-tz[k]) * (k == j0 ? -1 : 1); double kk = Kh(k), wk = W2a[k] + .15, gk = Math.Max(-2, Math.Min(-.4, Gy[k] - Y2[k] - .05));
                        Quad(MAT_KERB, Pt(k, 0, -.25), Pt(k, wk, gk), Pt(k, wk, kk), Pt(k, 0, kk), tv, Drop.None, false);
                    }
                    stripRuns++;
                }
            }
        }
    }
}
namespace Magistrala.EditorTools
{
    public static partial class StreetsBuilder
    {
        // ------------------------------------------------------------------ ground at the model-road strips (CarveTerrain, last)
        /// the terrain lowered within 1.1 m of the model asphalt's edge (asphalt y − 0.18: nothing over the asphalt in front of the
        /// kerb), then raised (only) from 1.2 m behind the kerb face to the footway's top − 3 cm and blended back over ≥ 3 m (1:5);
        /// not on a model's paving or our streets
        static void WalkTerrain(double ox, double oz1, double step, int res, Func<int, int, float> get, Action<int, int, double> set)
        {
            foreach (var q in walkLow)
            {
                double reach = q[6], x0 = Math.Min(q[0], q[3]) - reach, x1 = Math.Max(q[0], q[3]) + reach, z0 = Math.Min(q[1], q[4]) - reach, z1 = Math.Max(q[1], q[4]) + reach;
                if (x1 < ox || x0 > ox + 1024 || z1 < oz1 - 1024 || z0 > oz1) continue;
                double bx = q[3] - q[0], bz = q[4] - q[1], l2 = bx * bx + bz * bz;
                for (int r = Math.Max(0, (int)Math.Floor((oz1 - z1) / step)); r <= Math.Min(res - 1, (int)Math.Ceiling((oz1 - z0) / step)); r++)
                    for (int c = Math.Max(0, (int)Math.Floor((x0 - ox) / step)); c <= Math.Min(res - 1, (int)Math.Ceiling((x1 - ox) / step)); c++)
                    {
                        double gx = ox + c * step, gz = oz1 - r * step;
                        double tt = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((gx - q[0]) * bx + (gz - q[1]) * bz) / l2));
                        if (Math.Sqrt(Sq(gx - q[0] - bx * tt) + Sq(gz - q[1] - bz * tt)) > reach) continue;
                        double ry = q[2] + (q[5] - q[2]) * tt - .18;
                        if (get(r, c) > ry) set(r, c, ry);
                    }
            }
            foreach (var q in walkRaise)
            {
                double W = q[8], pad = W + 12, x0 = Math.Min(q[0], q[2]) - pad, x1 = Math.Max(q[0], q[2]) + pad, z0 = Math.Min(q[1], q[3]) - pad, z1 = Math.Max(q[1], q[3]) + pad;
                if (x1 < ox || x0 > ox + 1024 || z1 < oz1 - 1024 || z0 > oz1) continue;
                double bx = q[2] - q[0], bz = q[3] - q[1], l2 = bx * bx + bz * bz; if (l2 < 1e-6) continue;
                for (int r = Math.Max(0, (int)Math.Floor((oz1 - z1) / step)); r <= Math.Min(res - 1, (int)Math.Ceiling((oz1 - z0) / step)); r++)
                    for (int c = Math.Max(0, (int)Math.Floor((x0 - ox) / step)); c <= Math.Min(res - 1, (int)Math.Ceiling((x1 - ox) / step)); c++)
                    {
                        double gx = ox + c * step, gz = oz1 - r * step;
                        double tt = ((gx - q[0]) * bx + (gz - q[1]) * bz) / l2; if (tt < -.02 || tt > 1.02) continue; tt = Math.Max(0, Math.Min(1, tt));
                        double d = (gx - q[0] - bx * tt) * q[4] + (gz - q[1] - bz * tt) * q[5]; if (d < 1.2) continue;
                        double top = q[6] + (q[7] - q[6]) * tt; float cur = get(r, c); double rise = top - cur; if (rise <= .01) continue;
                        double k = 1;
                        if (d > W + .3) { double B = Math.Max(3, Math.Min(10, rise * 5)); if (d > W + .3 + B) continue; k = Smooth(1 - (d - W - .3) / B); }
                        if (WorldData.SurfDist(wbModels, gx, gz) < .3 || CoveredMain(gx, gz, 0)) continue;
                        double target = cur + rise * k; if (target > cur + .005) set(r, c, target);
                    }
            }
        }

        // ------------------------------------------------------------------ the strips' own footprint (1 m cells): vegetation off the strip only
        const double WMC = 1; static readonly int WNX = (int)((AX1 - AX0) / WMC), WNZ = (int)((AZ1 - AZ0) / WMC);
        const string WALKCOVER = OUT + "/cover-walk.bytes";
        static byte[] walkCover;
        static string WriteWalkCover()
        {
            var m = new byte[(WNX * WNZ + 7) / 8]; int n = 0;
            foreach (var q in walkRaise)
            {
                double W = q[8], bx = q[2] - q[0], bz = q[3] - q[1], l2 = bx * bx + bz * bz; if (l2 < 1e-6) continue;
                for (double gz = Math.Floor((Math.Min(q[1], q[3]) - W - 1) / WMC) * WMC; gz <= Math.Max(q[1], q[3]) + W + 1; gz += WMC)
                    for (double gx = Math.Floor((Math.Min(q[0], q[2]) - W - 1) / WMC) * WMC; gx <= Math.Max(q[0], q[2]) + W + 1; gx += WMC)
                    {
                        double cx = gx + WMC / 2, cz = gz + WMC / 2, tt = ((cx - q[0]) * bx + (cz - q[1]) * bz) / l2; if (tt < -.05 || tt > 1.05) continue;
                        double d = (cx - q[0] - bx * tt) * q[4] + (cz - q[1] - bz * tt) * q[5]; if (d < -.3 || d > W + .4) continue;
                        int i = (int)Math.Floor((cx - AX0) / WMC), j = (int)Math.Floor((cz - AZ0) / WMC); if (i < 0 || j < 0 || i >= WNX || j >= WNZ) continue;
                        int b = j * WNX + i; if ((m[b >> 3] & (1 << (b & 7))) == 0) { m[b >> 3] |= (byte)(1 << (b & 7)); n++; }
                    }
            }
            File.WriteAllBytes(WALKCOVER, m); UnityEditor.AssetDatabase.ImportAsset(WALKCOVER); walkCover = m;
            return $"model-road footways: {n} m² footprint mask";
        }
        static bool WalkCovered(double gx, double gz)
        {
            if (walkCover == null) { if (!File.Exists(WALKCOVER)) return false; walkCover = File.ReadAllBytes(WALKCOVER); }
            int i = (int)Math.Floor((gx - AX0) / WMC), j = (int)Math.Floor((gz - AZ0) / WMC); if (i < 0 || j < 0 || i >= WNX || j >= WNZ) return false;
            int b = j * WNX + i; return (walkCover[b >> 3] & (1 << (b & 7))) != 0;
        }
    }
}
