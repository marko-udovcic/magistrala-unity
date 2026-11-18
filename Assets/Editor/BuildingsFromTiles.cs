// Town buildings: the tiles' footprints (BUILDINGS section: pts, h, kind, y) extruded with a flat or hipped roof, Dalmatian
// colours as vertex colours (Magistrala/VertexColorLit), chunked per 256 m. Plus the package's roadside buildings
// (model-buildings.json, BuildingsBuilder by Codex). Buildings on a model footprint or under a model building are skipped.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class BuildingsFromTiles
    {
        static readonly Color[] WALL = { C(.93f, .91f, .86f), C(.92f, .87f, .76f), C(.89f, .80f, .64f), C(.80f, .78f, .73f), C(.93f, .85f, .80f), C(.95f, .94f, .90f), C(.85f, .82f, .72f) };
        static readonly Color[] ROOF = { C(.66f, .33f, .22f), C(.72f, .40f, .27f), C(.58f, .30f, .22f), C(.70f, .46f, .33f) };
        static readonly Color FLAT = C(.62f, .61f, .58f), IND = C(.76f, .78f, .80f), BASE = C(.55f, .53f, .5f), WIN = C(.16f, .18f, .2f);
        static Color C(float r, float g, float b) => new Color(r, g, b).linear;

        class Geo { public List<Vector3> v = new List<Vector3>(); public List<Color> c = new List<Color>(); public List<int> t = new List<int>(); }

        static void Tri(Geo g, Vector3 a, Vector3 b, Vector3 c, Color col, Vector3 want)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), want) < 0) { var x = b; b = c; c = x; }
            int i = g.v.Count; g.v.Add(a); g.v.Add(b); g.v.Add(c); g.c.Add(col); g.c.Add(col); g.c.Add(col); g.t.Add(i); g.t.Add(i + 1); g.t.Add(i + 2);
        }
        static void Quad(Geo g, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col, Vector3 want) { Tri(g, a, b, c, col, want); Tri(g, a, c, d, col, want); }

        public static double Ground(List<ModelJ> models, double gx, double gz)
        {
            double h = Tiles.Height(gx, gz);
            var q = WorldData.ModelTerrain(models, gx, gz, 2, h);
            return q ?? h;
        }

        public static void Build()
        {
            var models = WorldData.Models();
            var root = WorldBuilder.Root("Buildings");
            // the package's roadside buildings (rects to clear ours under them)
            var rects = new List<double[]>();
            var mb = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "model-buildings.json")));
            foreach (var o in MiniJson.L(mb["objects"]))
            {
                var d = MiniJson.O(o); if (!d.ContainsKey("w") || !d.ContainsKey("l")) continue;
                rects.Add(new[] { MiniJson.D(d["x"]), MiniJson.D(d["z"]), MiniJson.D(MiniJson.G(d, "a") ?? 0.0), MiniJson.D(d["w"]), MiniJson.D(d["l"]) });
            }
            try
            {
                var pk = BuildingsBuilder.Build(W.REGION + "model-buildings.json", (x, z) => (float)Ground(models, x, z), root.transform, -8100, -300);
                pk.name = "package"; pk.transform.localPosition = new Vector3((float)-W.OX, 0, (float)W.OZ);
            }
            catch (Exception e) { Debug.LogError("[Magistrala] model buildings: " + e); }

            // [0] = with window quads (LOD0), [1] = without, walls darkened by the window share (LOD1, far)
            var chunks = new Dictionary<long, Geo[]>();
            // detailed Dalmatian houses (HouseBuilder: windows, škure, balconies, tile roofs) within NEAR m of the D8, boxes beyond
            const double NEAR = 320;
            var path = TruckSetup.EastboundPath(); var houses = new Dictionary<long, HouseGeo[]>(); int nDetailed = 0;
            bool NearD8(double x, double z) { foreach (var q in path) if ((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z) < NEAR * NEAR) return true; return false; }
            int n = 0, skipped = 0;
            for (int tz = WorldBuilder.TZ0; tz <= WorldBuilder.TZ1; tz++)
                for (int tx = WorldBuilder.TX0; tx <= WorldBuilder.TX1; tx++)
                {
                    var tile = Tiles.Get(tx, tz); if (tile?.buildingsJson == null) continue;
                    foreach (var bo in MiniJson.L(MiniJson.Parse(tile.buildingsJson)))
                    {
                        var b = MiniJson.O(bo);
                        var pts = MiniJson.L(b["pts"]); if (pts.Count < 3) continue;
                        var P = new List<Vector2d>();
                        foreach (var p in pts) { var q = MiniJson.L(p); P.Add(new Vector2d(MiniJson.D(q[0]), MiniJson.D(q[1]))); }
                        if (P[0].x == P[P.Count - 1].x && P[0].z == P[P.Count - 1].z) P.RemoveAt(P.Count - 1);
                        if (P.Count < 3) continue;
                        double cx = 0, cz = 0; foreach (var p in P) { cx += p.x; cz += p.z; } cx /= P.Count; cz /= P.Count;
                        if (WorldData.FootDist(models, cx, cz) < 3 || Covered(rects, P, cx, cz)) { skipped++; continue; }
                        bool onFoot = false; foreach (var p in P) if (WorldData.FootDist(models, p.x, p.z) < 1.5) { onFoot = true; break; }
                        if (onFoot) { skipped++; continue; }
                        string kind = (string)MiniJson.G(b, "kind") ?? "house";
                        double h = MiniJson.D(MiniJson.G(b, "h") ?? 7.0);
                        double gmin = double.MaxValue, gmax = double.MinValue;
                        foreach (var p in P) { double g = Ground(models, p.x, p.z); gmin = Math.Min(gmin, g); gmax = Math.Max(gmax, g); }
                        double baseY = gmax - Math.Min(1.0, (gmax - gmin) * .3), bottom = gmin - .6, top = baseY + h;
                        // (uint)(int): a negative double cast straight to uint is 0 on ARM, which had merged every cell south of z = 0 into one 2 km strip
                        long key = ((long)Math.Floor(cx / 256) << 32) ^ (uint)(int)Math.Floor(cz / 256);
                        bool done = false;
                        if (NearD8(cx, cz))
                        {
                            if (!houses.TryGetValue(key, out var hg)) houses[key] = hg = new[] { new HouseGeo(), new HouseGeo(), new HouseGeo() };
                            var fp = new List<Vector2>(); foreach (var p in P) fp.Add(new Vector2((float)p.x, (float)p.z));
                            int seed = (int)(Math.Abs(cx * 7.31 + cz * 3.17) % 100000);
                            try
                            {
                                // the ring is triangulated before anything is appended: a bad footprint throws in level 0 and adds nothing
                                for (int lod = 0; lod < 3; lod++) { HouseBuilder.Lod = lod; HouseBuilder.AddHouse(hg[lod], fp, kind, bottom, baseY, top, seed); }
                                nDetailed++; done = true;
                            }
                            catch (ArgumentException) { } // self-intersecting footprint: plain box
                            finally { HouseBuilder.Lod = 0; }
                        }
                        if (!done)
                        {
                            if (!chunks.TryGetValue(key, out var geo)) chunks[key] = geo = new[] { new Geo(), new Geo() };
                            Extrude(geo[0], P, kind, bottom, baseY, top, cx, cz, true);
                            Extrude(geo[1], P, kind, bottom, baseY, top, cx, cz, false);
                        }
                        n++;
                    }
                }
            var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/Materials/Buildings.mat");
            if (!AssetDatabase.IsValidFolder("Assets/Generated/TileBuildings")) AssetDatabase.CreateFolder("Assets/Generated", "TileBuildings");
            var tr = new GameObject("town"); tr.isStatic = true; tr.transform.SetParent(root.transform, false);
            int ci = 0;
            Mesh Save(Mesh mesh, string file)
            {
                string p = $"Assets/Generated/TileBuildings/{file}.asset";
                if (File.Exists(p)) AssetDatabase.DeleteAsset(p);
                AssetDatabase.CreateAsset(mesh, p); return mesh;
            }
            Mesh TownMesh(Geo g, string name)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(g.v); mesh.SetColors(g.c); mesh.SetTriangles(g.t, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                return Save(mesh, name);
            }
            long[] triL = new long[5];
            foreach (var kv in chunks)
            {
                var mesh = TownMesh(kv.Value[0], "town" + ci);
                var go = new GameObject("town" + ci); go.isStatic = true; go.transform.SetParent(tr.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                go.AddComponent<MeshCollider>().sharedMesh = mesh;
                var far = TownMesh(kv.Value[1], $"town{ci}_lod1");
                Lods(go, mesh.bounds, new[] { mesh, far }, new[] { TOWN_FAR }, new[] { mat });
                triL[0] += kv.Value[0].t.Count / 3; triL[1] += kv.Value[1].t.Count / 3;
                ci++;
            }
            var hm = HouseBuilder.Materials(); int hi = 0;
            foreach (var kv in houses)
            {
                var mesh = Save(HouseBuilder.ToMesh(kv.Value[0], "house" + hi), "house" + hi);
                var go = new GameObject("houses" + hi); go.isStatic = true; go.transform.SetParent(tr.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = hm;
                go.AddComponent<MeshCollider>().sharedMesh = mesh; // collider: always the full house
                var m1 = Save(HouseBuilder.ToMesh(kv.Value[1], $"house{hi}_lod1"), $"house{hi}_lod1");
                var m2 = Save(HouseBuilder.ToMesh(kv.Value[2], $"house{hi}_lod2"), $"house{hi}_lod2");
                Lods(go, mesh.bounds, new[] { mesh, m1, m2 }, new[] { HOUSE_MID, HOUSE_FAR }, hm);
                for (int l = 0; l < 3; l++) triL[2 + l] += (long)Tris(kv.Value[l]);
                hi++;
            }
            Debug.Log($"[Magistrala] building LODs: town {triL[0] / 1000}k -> far {triL[1] / 1000}k (from {TOWN_FAR} m), houses {triL[2] / 1000}k -> mid {triL[3] / 1000}k (from {HOUSE_MID} m) -> far {triL[4] / 1000}k (from {HOUSE_FAR} m)");
            AssetDatabase.SaveAssets();
            Debug.Log($"[Magistrala] tile buildings: {n} ({skipped} cleared, {nDetailed} detailed), {ci} + {hi} chunks");
        }

        // LOD switch distances, metres from the camera to the CHUNK CENTRE (a 256 m chunk reaches ~180 m closer than that):
        // houses mid from 450 m (nearest house >= ~270 m: the 12 cm window reveals / sill ends / railing bars are < 1 px),
        // houses and town far from 1500 m (windows ~1 px, replaced by the wall colour darkened by their share).
        const float HOUSE_MID = 450, HOUSE_FAR = 1500, TOWN_FAR = 1500;
        // the thresholds assume the chase camera's fov (55 deg at rest) and QualitySettings.lodBias of the PC level (2, SceneSetup)
        const float LOD_FOV = 55, LOD_BIAS = 2;

        static int Tris(HouseGeo g) { int n = 0; foreach (var s in g.sub) n += s.Count / 3; return n; }

        /// LODGroup on the chunk object: level 0 is its own renderer, the others children "lod1", "lod2" (no colliders).
        static void Lods(GameObject go, Bounds b, Mesh[] meshes, float[] dist, Material[] mats)
        {
            var lg = go.AddComponent<LODGroup>();
            // size only scales the thresholds (a real 256 m chunk would need heights > 1 at 450 m with bias 2): a nominal 16 m
            const float size = 16;
            float H(float d) => Mathf.Clamp01(LOD_BIAS * size / (d * 2 * Mathf.Tan(LOD_FOV * .5f * Mathf.Deg2Rad)));
            var lods = new LOD[meshes.Length];
            lods[0] = new LOD(H(dist[0]), new Renderer[] { go.GetComponent<MeshRenderer>() });
            for (int i = 1; i < meshes.Length; i++)
            {
                var c = new GameObject("lod" + i); c.isStatic = true; c.transform.SetParent(go.transform, false);
                c.AddComponent<MeshFilter>().sharedMesh = meshes[i];
                var r = c.AddComponent<MeshRenderer>(); r.sharedMaterials = mats;
                // last level: never culled by size (the town stays visible to the far clip)
                lods[i] = new LOD(i < meshes.Length - 1 ? H(dist[i]) : 0f, new Renderer[] { r });
            }
            lg.fadeMode = LODFadeMode.None;
            lg.SetLODs(lods);
            lg.localReferencePoint = b.center; lg.size = size; // after SetLODs, which recalculates both from the renderers
        }

        struct Vector2d { public double x, z; public Vector2d(double x, double z) { this.x = x; this.z = z; } }

        static bool Covered(List<double[]> rects, List<Vector2d> P, double cx, double cz)
        {
            foreach (var r in rects)
            {
                double dx = cx - r[0], dz = cz - r[1];
                if (dx * dx + dz * dz > 900) continue;
                double ca = Math.Cos(r[2]), sa = Math.Sin(r[2]);
                // test the footprint centre in the rect frame (either axis convention: generous)
                double u = dx * ca + dz * sa, v = -dx * sa + dz * ca, m = Math.Max(r[3], r[4]) / 2 + 2;
                if (Math.Abs(u) < m && Math.Abs(v) < m) return true;
                if (InPoly(P, r[0], r[1])) return true;
            }
            return false;
        }
        static bool InPoly(List<Vector2d> r, double x, double z)
        {
            bool hit = false;
            for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
                if ((r[i].z > z) != (r[j].z > z) && x < (r[j].x - r[i].x) * (z - r[i].z) / (r[j].z - r[i].z) + r[i].x) hit = !hit;
            return hit;
        }

        static Vector3 U(double gx, double y, double gz) => W.U(gx, y, gz);

        static void Extrude(Geo g, List<Vector2d> P, string kind, double bottom, double baseY, double top, double cx, double cz, bool windows)
        {
            int hsh = (int)(Math.Abs(Math.Sin(cx * 12.9898 + cz * 78.233) * 43758.5453) * 1000);
            bool ind = kind == "industrial" || kind == "warehouse";
            Color wall = ind ? IND : WALL[hsh % WALL.Length];
            // far level: no window quads; the wall takes their average (1.1 x 1.35 m per 3.2 x 3 m cell = 15 %, linear colours)
            if (!windows && !ind) wall = Color.Lerp(wall, WIN, .15f);
            double area = 0, per = 0;
            for (int i = 0; i < P.Count; i++) { var a = P[i]; var b = P[(i + 1) % P.Count]; area += a.x * b.z - b.x * a.z; per += Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z)); }
            area = Math.Abs(area) / 2;
            bool hip = kind == "house" && P.Count <= 8 && area < 400 && hsh % 10 < 8;
            var C = U(cx, (bottom + top) / 2, cz);
            for (int i = 0; i < P.Count; i++)
            {
                var a = P[i]; var b = P[(i + 1) % P.Count];
                var mid = U((a.x + b.x) / 2, 0, (a.z + b.z) / 2); var outw = mid - new Vector3(C.x, 0, C.z);
                var e = U(b.x, 0, b.z) - U(a.x, 0, a.z); var nrm = new Vector3(e.z, 0, -e.x); if (Vector3.Dot(nrm, outw) < 0) nrm = -nrm;
                Quad(g, U(a.x, bottom, a.z), U(b.x, bottom, b.z), U(b.x, baseY + .5, b.z), U(a.x, baseY + .5, a.z), BASE, nrm);
                Quad(g, U(a.x, baseY + .5, a.z), U(b.x, baseY + .5, b.z), U(b.x, top, b.z), U(a.x, top, a.z), wall, nrm);
                // windows: dark rectangles per floor, slightly out of the wall
                double len = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.z - a.z) * (b.z - a.z));
                int nw = (int)Math.Floor(len / (ind ? 6 : 3.2)); int floors = (int)Math.Floor((top - baseY - .5) / 3.0);
                if (windows && len > 3 && nw > 0 && !ind)
                {
                    var off = nrm.normalized * .03f;
                    for (int f = 0; f < floors; f++)
                        for (int k = 0; k < nw; k++)
                        {
                            double t0 = (k + .5) / nw - .55 / len, t1 = (k + .5) / nw + .55 / len, y0 = baseY + .5 + f * 3.0 + .9, y1 = y0 + 1.35;
                            if (y1 > top - .3) continue;
                            Quad(g, U(a.x + (b.x - a.x) * t0, y0, a.z + (b.z - a.z) * t0) + off, U(a.x + (b.x - a.x) * t1, y0, a.z + (b.z - a.z) * t1) + off,
                                U(a.x + (b.x - a.x) * t1, y1, a.z + (b.z - a.z) * t1) + off, U(a.x + (b.x - a.x) * t0, y1, a.z + (b.z - a.z) * t0) + off, WIN, nrm);
                        }
                }
            }
            // roof
            var pts = new List<Vector3>(); foreach (var p in P) pts.Add(U(p.x, top, p.z));
            if (!hip) { foreach (var t in Ear(pts)) Tri(g, pts[t[0]], pts[t[1]], pts[t[2]], ind ? IND * .95f : FLAT, Vector3.up); return; }
            double d = .45 * area / (per / 2);
            Color roof = ROOF[hsh / 7 % ROOF.Length];
            var inner = new List<Vector3>();
            for (int i = 0; i < P.Count; i++)
            {
                var c0 = new Vector3((float)cx, 0, (float)cz);
                // inset towards the centroid (fine for the near-rectangular footprints that get a hip roof)
                var p = new Vector3((float)P[i].x, 0, (float)P[i].z); var dir = (c0 - p); float L = dir.magnitude;
                var q = p + dir / Mathf.Max(L, .01f) * Mathf.Min((float)d * 1.35f, L * .8f);
                inner.Add(U(q.x, top + d * .55, q.z));
            }
            for (int i = 0; i < P.Count; i++) { int j = (i + 1) % P.Count; var o0 = pts[i]; var o1 = pts[j]; var e = o1 - o0; var nrm = new Vector3(e.z, 0, -e.x); var mid = (o0 + o1) / 2; if (Vector3.Dot(nrm, mid - new Vector3(C.x, mid.y, C.z)) < 0) nrm = -nrm; Quad(g, o0, o1, inner[j], inner[i], roof, nrm + Vector3.up * 1.5f); }
            foreach (var t in Ear(inner)) Tri(g, inner[t[0]], inner[t[1]], inner[t[2]], roof, Vector3.up);
            // eaves overhang skipped (probe)
        }

        // ear clipping in the xz plane
        static List<int[]> Ear(List<Vector3> pts)
        {
            var res = new List<int[]>(); var idx = new List<int>(); for (int i = 0; i < pts.Count; i++) idx.Add(i);
            double area = 0; for (int i = 0; i < pts.Count; i++) { var a = pts[i]; var b = pts[(i + 1) % pts.Count]; area += a.x * b.z - b.x * a.z; }
            float sgn = area > 0 ? 1 : -1; int guard = 0;
            while (idx.Count > 3 && guard++ < 1000)
            {
                bool cut = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int i0 = idx[(k + idx.Count - 1) % idx.Count], i1 = idx[k], i2 = idx[(k + 1) % idx.Count];
                    Vector3 a = pts[i0], b = pts[i1], c = pts[i2];
                    float cr = ((b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x)) * sgn;
                    if (cr <= 1e-6f) continue;
                    bool inside = false;
                    foreach (int o in idx) { if (o == i0 || o == i1 || o == i2) continue; if (InTri(pts[o], a, b, c, sgn)) { inside = true; break; } }
                    if (inside) continue;
                    res.Add(new[] { i0, i1, i2 }); idx.RemoveAt(k); cut = true; break;
                }
                if (!cut) break;
            }
            if (idx.Count == 3) res.Add(new[] { idx[0], idx[1], idx[2] });
            return res;
        }
        static bool InTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c, float s)
        {
            float d1 = ((b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x)) * s, d2 = ((c.x - b.x) * (p.z - b.z) - (c.z - b.z) * (p.x - b.x)) * s, d3 = ((a.x - c.x) * (p.z - c.z) - (a.z - c.z) * (p.x - c.x)) * s;
            return d1 > 0 && d2 > 0 && d3 > 0;
        }
    }
}
