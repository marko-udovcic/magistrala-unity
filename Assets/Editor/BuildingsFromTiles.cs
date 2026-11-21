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
        // the far level's walls take the glass's average over their window share (sRGB)
        static readonly Color WIN = new Color(.3f, .32f, .34f);

        static void Tri(HouseGeo g, Vector3 a, Vector3 b, Vector3 c, Slot s, Vector3 want) => HouseBuilder.Emit(g, s, a, b, c, want);
        static void Quad(HouseGeo g, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Slot s, Vector3 want) { Tri(g, a, b, c, s, want); Tri(g, a, c, d, s, want); }

        public static double Ground(List<ModelJ> models, double gx, double gz)
        {
            double h = Tiles.Height(gx, gz);
            var q = WorldData.ModelTerrain(models, gx, gz, 2, h);
            return q ?? h;
        }

        /// the package's roadside buildings only (World/Buildings/package), e.g. after BuildingsBuilder.TripoPumps changed
        public static string RebuildPackage()
        {
            var models = WorldData.Models();
            var root = GameObject.Find("World/Buildings"); if (!root) return "no World/Buildings";
            var old = root.transform.Find("package"); if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
            var pk = BuildingsBuilder.Build(W.REGION + "model-buildings.json", (x, z) => (float)Ground(models, x, z), root.transform, (float)WorldData.MIN_X, -300);
            pk.name = "package"; pk.transform.localPosition = new Vector3((float)-W.OX, 0, (float)W.OZ); pk.transform.SetAsFirstSibling();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            return "package rebuilt: " + pk.transform.childCount + " chunks";
        }

        sealed class Bld
        {
            public List<Vector2d> P, orig; public string kind; public double h, baseY, bottom, top, cx, cz; public int seed, lookSeed, roof = -1;
            public bool core, terrace, annex, yard; public long cell, chunk;
        }

        /// old town cores (game x, z, radius m) round the castles: more bare stone, more shop fronts
        static readonly (double x, double z, double r)[] Cores =
        {
            (-1162, -4163, 230), (-3603, -4341, 200), (-4356, -4458, 210), (-6145, -4708, 230), (-7760, -4625, 240),
            (-8146, -4373, 230), (-8447, -4374, 220), (-8821, -4223, 150),
        };
        static bool InCore(double x, double z) { foreach (var c in Cores) if ((x - c.x) * (x - c.x) + (z - c.z) * (z - c.z) < c.r * c.r) return true; return false; }

        /// the kuce2 showcase (owner's Street View comparison, about 500 houses): Kaštel Novi riva (Obala kralja Tomislava) and
        /// the Novi / Stari old cores, a capsule (game a -> b, radius). Only these get the new near / mid / far / hull pipeline;
        /// the probe and the D8 corridor keep main's three-level chunks, the rest main's boxes (full rollout: later)
        public static readonly (double ax, double az, double bx, double bz, double r) Show = (-8230, -4390, -7690, -4650, SHOW_R);
        const double SHOW_R = 160;
        /// kuce3: the showcase's pipeline and look for every house of Kaštela (false: back to the capsule only, main's
        /// three-level chunks by the D8 / in the probe and boxes elsewhere)
        public static readonly bool ShowEverywhere = true;
        /// AC units on the facades / roofs as the instanced Tripo model (klima.glb) instead of boxes (false: boxes)
        public static readonly bool AcModel = true;
        public static bool InShow(double x, double z) => ShowEverywhere || InShowArea(x, z);
        /// the original showcase capsule (Kaštel Novi riva to the Stari core): extras that cost a draw each (front-door pots)
        public static bool InShowArea(double x, double z)
        {
            double dx = Show.bx - Show.ax, dz = Show.bz - Show.az, t = Math.Max(0, Math.Min(1, ((x - Show.ax) * dx + (z - Show.az) * dz) / (dx * dx + dz * dz)));
            double qx = Show.ax + t * dx - x, qz = Show.az + t * dz - z; return qx * qx + qz * qz < Show.r * Show.r;
        }

        static long Key(double x, double z, double size) => ((long)Math.Floor(x / size) << 32) ^ (uint)(int)Math.Floor(z / size);
        static double Hash01(double x, double z, double k) { double v = Math.Sin(x * 12.9898 + z * 78.233 + k * 37.719) * 43758.5453; return v - Math.Floor(v); }

        /// the polygon on one side of the line (point o, normal n: keeps dot(p - o, n) >= 0), Sutherland-Hodgman
        static List<Vector2d> Clip(List<Vector2d> P, double ox, double oz, double nx, double nz)
        {
            var R = new List<Vector2d>();
            for (int i = 0; i < P.Count; i++)
            {
                var a = P[i]; var b = P[(i + 1) % P.Count];
                double da = (a.x - ox) * nx + (a.z - oz) * nz, db = (b.x - ox) * nx + (b.z - oz) * nz;
                if (da >= 0) R.Add(a);
                if ((da >= 0) != (db >= 0)) { double t = da / (da - db); R.Add(new Vector2d(a.x + (b.x - a.x) * t, a.z + (b.z - a.z) * t)); }
            }
            return R;
        }

        // LOD distances (m from the camera to the reference point; QualitySettings.lodBias 2, fov 55, see Lod()):
        // near (recessed windows, clutter) per 64 m cell to 120 m from its centre (the nearest mid-level house >= ~75 m away:
        // a 20 cm reveal is ~1 px at the "srednje" render scale), mid per cell to 650 m; the 256 m chunk's far level from
        // 500 m of ITS centre (a cell centre is <= 136 m from it: the far level is always on before the cells drop out),
        // its box level (no windows) from 1500 m.
        const float CELL = 64, CHUNK = 256, NEAR_END = 120, MID_END = 650, FAR_START = 500, BOX_START = 1500;

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
                var pk = BuildingsBuilder.Build(W.REGION + "model-buildings.json", (x, z) => (float)Ground(models, x, z), root.transform, (float)WorldData.MIN_X, -300);
                pk.name = "package"; pk.transform.localPosition = new Vector3((float)-W.OX, 0, (float)W.OZ);
            }
            catch (Exception e) { Debug.LogError("[Magistrala] model buildings: " + e); }

            // 1. every footprint (and the annex splits), with its heights
            var B = new List<Bld>(); int n = 0, skipped = 0, nAnnex = 0;
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
                        double baseY = gmax - Math.Min(1.0, (gmax - gmin) * .3), bottom = gmin - .6;
                        bool house = kind == "house";
                        // no row of identical boxes: +-35 cm on the eaves (the DGU heights are per-building medians anyway)
                        if (house) h = Math.Max(2.6, h + (Hash01(cx, cz, 1) - .5) * .7);
                        int seed = (int)(Math.Abs(cx * 7.31 + cz * 3.17) % 100000);
                        var bl = new Bld { P = P, orig = P, kind = kind, h = h, baseY = baseY, bottom = bottom, top = baseY + h, cx = cx, cz = cz, seed = seed, lookSeed = seed,
                            core = InCore(cx, cz), yard = house && h < 13, cell = Key(cx, cz, CELL), chunk = Key(cx, cz, CHUNK) };
                        n++;
                        // annexes: a large near-rectangular house becomes a main block and a lower wing (one storey less, roof
                        // terrace) across its long axis; the footprint stays exactly the cadastre's
                        double area = 0; for (int i = 0; i < P.Count; i++) { var a = P[i]; var c = P[(i + 1) % P.Count]; area += a.x * c.z - c.x * a.z; } area = Math.Abs(area) / 2;
                        if (InShow(cx, cz) && house && h >= 5.6 && area >= 100 && area < 600 && P.Count <= 8 && Hash01(cx, cz, 2) < .42)
                        {
                            // the longest edge's direction is the long axis of a near-rectangle
                            double ux = 1, uz = 0, best = 0;
                            for (int i = 0; i < P.Count; i++) { var a = P[i]; var c = P[(i + 1) % P.Count]; double L = Math.Sqrt((c.x - a.x) * (c.x - a.x) + (c.z - a.z) * (c.z - a.z)); if (L > best) { best = L; ux = (c.x - a.x) / L; uz = (c.z - a.z) / L; } }
                            double lo = 1e9, hj = -1e9, vlo = 1e9, vhi = -1e9;
                            foreach (var p in P) { double t = p.x * ux + p.z * uz, s = -p.x * uz + p.z * ux; lo = Math.Min(lo, t); hj = Math.Max(hj, t); vlo = Math.Min(vlo, s); vhi = Math.Max(vhi, s); }
                            double fill = area / Math.Max(1, (hj - lo) * (vhi - vlo));
                            if (fill > .88 && hj - lo > 9)
                            {
                                double tc = lo + (hj - lo) * (.58 + .14 * Hash01(cx, cz, 3)); bool flip = Hash01(cx, cz, 4) < .5;
                                double ox = ux * tc, oz = uz * tc, nx = flip ? -ux : ux, nz = flip ? -uz : uz;
                                var main = Clip(P, ox, oz, -nx, -nz); var wing = Clip(P, ox, oz, nx, nz);
                                double wh = h >= 8.5 ? h - 3 : Math.Max(2.9, h - 3);
                                if (main.Count >= 3 && wing.Count >= 3 && wh >= 2.8 && wh < h - 1.5)
                                {
                                    bl.P = main; n++; nAnnex++;
                                    B.Add(new Bld { P = wing, orig = P, kind = kind, h = wh, baseY = baseY, bottom = bottom, top = baseY + wh, cx = cx, cz = cz, seed = seed + 1, lookSeed = seed,
                                        roof = 0, terrace = Hash01(cx, cz, 5) < .75, annex = true, core = bl.core, cell = bl.cell, chunk = bl.chunk });
                                }
                            }
                        }
                        B.Add(bl);
                    }
                }

            // 2. party walls: an index of every edge (game coords, 8 m cells)
            var edges = new Dictionary<long, List<(int b, Vector2d a, Vector2d c)>>();
            for (int bi = 0; bi < B.Count; bi++)
            {
                var P = B[bi].P;
                for (int i = 0; i < P.Count; i++)
                {
                    var a = P[i]; var c = P[(i + 1) % P.Count];
                    for (int gx = (int)Math.Floor(Math.Min(a.x, c.x) / 8); gx <= (int)Math.Floor(Math.Max(a.x, c.x) / 8); gx++)
                        for (int gz = (int)Math.Floor(Math.Min(a.z, c.z) / 8); gz <= (int)Math.Floor(Math.Max(a.z, c.z) / 8); gz++)
                        { long k = ((long)gx << 32) ^ (uint)gz; if (!edges.TryGetValue(k, out var l)) edges[k] = l = new List<(int, Vector2d, Vector2d)>(); l.Add((bi, a, c)); }
                }
            }
            int self = -1, nHidden = 0, nBlind = 0;
            HouseBuilder.EdgeQuery = (ua, ub) =>
            {
                Vector2 ga = W.G(ua), gb = W.G(ub);
                double ex = gb.x - ga.x, ez = gb.y - ga.y, L = Math.Sqrt(ex * ex + ez * ez); if (L < .3) return (float.NegativeInfinity, false);
                ex /= L; ez /= L;
                var seen = new HashSet<(int, double, double)>(); var cover = new List<(double t0, double t1, double top)>();
                double mx = (ga.x + gb.x) / 2, mz = (ga.y + gb.y) / 2;
                for (int gx = (int)Math.Floor((Math.Min(ga.x, gb.x) - 1) / 8); gx <= (int)Math.Floor((Math.Max(ga.x, gb.x) + 1) / 8); gx++)
                    for (int gz = (int)Math.Floor((Math.Min(ga.y, gb.y) - 1) / 8); gz <= (int)Math.Floor((Math.Max(ga.y, gb.y) + 1) / 8); gz++)
                    {
                        if (!edges.TryGetValue(((long)gx << 32) ^ (uint)gz, out var l)) continue;
                        foreach (var e in l)
                        {
                            if (e.b == self || !seen.Add((e.b, e.a.x, e.a.z))) continue;
                            double fx = e.c.x - e.a.x, fz = e.c.z - e.a.z, M = Math.Sqrt(fx * fx + fz * fz); if (M < .3) continue;
                            if (Math.Abs((fx * ex + fz * ez) / M) < .985) continue;
                            // both ends on our line (within 35 cm)
                            double da = Math.Abs(-(e.a.x - ga.x) * ez + (e.a.z - ga.y) * ex), dc = Math.Abs(-(e.c.x - ga.x) * ez + (e.c.z - ga.y) * ex);
                            if (da > .35 || dc > .35) continue;
                            double t0 = (e.a.x - ga.x) * ex + (e.a.z - ga.y) * ez, t1 = (e.c.x - ga.x) * ex + (e.c.z - ga.y) * ez;
                            if (t0 > t1) { var t = t0; t0 = t1; t1 = t; }
                            t0 = Math.Max(0, t0); t1 = Math.Min(L, t1); if (t1 - t0 < .2) continue;
                            cover.Add((t0, t1, B[e.b].top));
                        }
                    }
                if (cover.Count == 0) return (float.NegativeInfinity, false);
                cover.Sort((p, q) => p.t0.CompareTo(q.t0));
                double reach = 0, got = 0, minTop = double.MaxValue;
                foreach (var c in cover) { double s0 = Math.Max(reach, c.t0); if (c.t1 > s0) { got += c.t1 - s0; reach = c.t1; } minTop = Math.Min(minTop, c.top); }
                double frac = got / L;
                if (frac > .97) { nHidden++; return ((float)minTop, true); }
                if (frac > .4) { nBlind++; return (float.NegativeInfinity, true); }
                return (float.NegativeInfinity, false);
            };
            // 3. streets (shop fronts): from the facade's midpoint along its outward normal
            Yards.Begin(models, rects);
            HouseBuilder.StreetQuery = (m, ow) =>
            {
                Vector2 g0 = W.G(m + ow * .3f), g1 = W.G(m + ow * 2.3f);
                var c0 = Yards.Clear(g0.x, g0.y); var c1 = Yards.Clear(g1.x, g1.y);
                if (c0.clear < .5 || c1.clear < c0.clear - .5) return ((float)c0.clear, (float)Math.Max(c0.hw, c1.hw));
                return (1e9f, 0f);
            };
            HouseBuilder.Doors.Clear();
            HouseBuilder.AcSpots.Clear(); HouseBuilder.AcModel = AcModel && TripoPropImporter.Prefab("klima");

            // 4. the houses: near / mid per 64 m cell, far / box / hull per 256 m chunk
            var cells = new Dictionary<long, HouseGeo[]>(); var chunks = new Dictionary<long, HouseGeo[]>();
            var legacy = new Dictionary<long, HouseGeo[]>(); var town = new Dictionary<long, HouseGeo[]>();
            const double NEAR = 320; var path = TruckSetup.EastboundPath();
            bool NearD8(double x, double z) { foreach (var q in path) if ((q.x - x) * (q.x - x) + (q.y - z) * (q.y - z) < NEAR * NEAR) return true; return false; }
            int nDetailed = 0, nYards = 0, nBoxes = 0, nShow = 0, nLegacy = 0, nPaved = 0;
            for (int bi = 0; bi < B.Count; bi++)
            {
                var bl = B[bi]; self = bi;
                if (!InShow(bl.cx, bl.cz))
                {
                    // main's look outside the showcase: detailed 3-level chunks by the D8 and in the probe, boxes elsewhere
                    bool probe = InProbe(bl.cx, bl.cz);
                    if (probe || NearD8(bl.cx, bl.cz))
                    {
                        if (!legacy.TryGetValue(bl.chunk, out var lg)) legacy[bl.chunk] = lg = new[] { new HouseGeo(), new HouseGeo(), new HouseGeo() };
                        var lfp = new List<Vector2>(); foreach (var p in bl.P) lfp.Add(new Vector2((float)p.x, (float)p.z));
                        var lopt = new HouseBuilder.Opts { lookSeed = bl.lookSeed, roof = bl.roof, terrace = bl.terrace, core = bl.core, noStairs = bl.annex };
                        try
                        {
                            for (int lod = 0; lod < 3; lod++) { HouseBuilder.Lod = lod; HouseBuilder.AddHouse(lg[lod], lfp, bl.kind, bl.bottom, bl.baseY, bl.top, bl.seed, lopt); }
                            nLegacy++;
                            if (probe && bl.yard && Yards.Build(lg[0], lg[1], null, bl.orig, bl.baseY, bl.seed)) nYards++;
                            continue;
                        }
                        catch (ArgumentException) { }
                        finally { HouseBuilder.Lod = 0; }
                    }
                    if (!town.TryGetValue(bl.chunk, out var tg)) town[bl.chunk] = tg = new[] { new HouseGeo(), new HouseGeo() };
                    Extrude(tg[0], bl.P, bl.kind, bl.bottom, bl.baseY, bl.top, bl.cx, bl.cz, true);
                    Extrude(tg[1], bl.P, bl.kind, bl.bottom, bl.baseY, bl.top, bl.cx, bl.cz, false);
                    nBoxes++;
                    continue;
                }
                nShow++;
                if (!cells.TryGetValue(bl.cell, out var cg)) cells[bl.cell] = cg = new[] { new HouseGeo(), new HouseGeo() };
                if (!chunks.TryGetValue(bl.chunk, out var kg)) chunks[bl.chunk] = kg = new[] { new HouseGeo(), new HouseGeo(), new HouseGeo() };
                var fp = new List<Vector2>(); foreach (var p in bl.P) fp.Add(new Vector2((float)p.x, (float)p.z));
                var opts = new HouseBuilder.Opts { lookSeed = bl.lookSeed, roof = bl.roof, terrace = bl.terrace, core = bl.core, noStairs = bl.annex };
                try
                {
                    // the ring is triangulated before anything is appended: a bad footprint throws in level 0 and adds nothing
                    HouseBuilder.Shrink = 0;
                    HouseBuilder.Lod = 0; HouseBuilder.AddHouse(cg[0], fp, bl.kind, bl.bottom, bl.baseY, bl.top, bl.seed, opts);
                    HouseBuilder.Lod = 1; HouseBuilder.AddHouse(cg[1], fp, bl.kind, bl.bottom, bl.baseY, bl.top, bl.seed, opts);
                    HouseBuilder.Lod = 3; HouseBuilder.AddHouse(kg[2], fp, bl.kind, bl.bottom, bl.baseY, bl.top, bl.seed, opts); // hull
                    HouseBuilder.Shrink = .25f;
                    HouseBuilder.Lod = 2; HouseBuilder.AddHouse(kg[0], fp, bl.kind, bl.bottom, bl.baseY, bl.top, bl.seed, opts);
                    HouseBuilder.Lod = 3; HouseBuilder.AddHouse(kg[1], fp, bl.kind, bl.bottom, bl.baseY, bl.top, bl.seed, opts);
                    nDetailed++;
                    // the old cores' houses stand at the street (no front yard: the paving goes up to the facade)
                    if (bl.yard && !bl.annex && !bl.core && Yards.Build(cg[0], cg[1], kg[2], bl.orig, bl.baseY, bl.seed)) nYards++;
                    nPaved += Pave(cg, bl.P, models, bi);
                }
                catch (ArgumentException)
                {
                    // self-intersecting footprint: a plain box in every level
                    HouseBuilder.Shrink = 0; nBoxes++;
                    Extrude(cg[0], bl.P, bl.kind, bl.bottom, bl.baseY, bl.top, bl.cx, bl.cz, true);
                    Extrude(cg[1], bl.P, bl.kind, bl.bottom, bl.baseY, bl.top, bl.cx, bl.cz, true);
                    Extrude(kg[0], bl.P, bl.kind, bl.bottom, bl.baseY, bl.top - .25, bl.cx, bl.cz, true);
                    Extrude(kg[1], bl.P, bl.kind, bl.bottom, bl.baseY, bl.top - .25, bl.cx, bl.cz, false);
                    Extrude(kg[2], bl.P, bl.kind, bl.bottom, bl.baseY, bl.top, bl.cx, bl.cz, false);
                }
                finally { HouseBuilder.Lod = 0; HouseBuilder.Shrink = 0; }
            }
            HouseBuilder.EdgeQuery = null; HouseBuilder.StreetQuery = null; HouseBuilder.AcModel = false;

            var hm = HouseBuilder.Materials();
            if (!AssetDatabase.IsValidFolder("Assets/Generated/TileBuildings")) AssetDatabase.CreateFolder("Assets/Generated", "TileBuildings");
            foreach (var old in AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/Generated/TileBuildings" })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(old));
            var tr = new GameObject("town"); tr.isStatic = true; tr.transform.SetParent(root.transform, false);
            Mesh Save(HouseGeo g, string file)
            {
                var mesh = HouseBuilder.ToMesh(g, file);
                AssetDatabase.CreateAsset(mesh, $"Assets/Generated/TileBuildings/{file}.asset"); return mesh;
            }
            long[] triL = new long[5]; int ci = 0, hi = 0;
            var cellRoot = new GameObject("cells"); cellRoot.isStatic = true; cellRoot.transform.SetParent(tr.transform, false);
            foreach (var kv in cells)
            {
                var m0 = Save(kv.Value[0], "c" + ci); var m1 = Save(kv.Value[1], $"c{ci}_mid");
                var go = new GameObject("c" + ci); go.isStatic = true; go.transform.SetParent(cellRoot.transform, false);
                var r0 = Part(go, "near", m0, hm, ShadowCastingMode.Off); var r1 = Part(go, "mid", m1, hm, ShadowCastingMode.Off);
                Lod(go, m0.bounds.center, new[] { new[] { r0 }, new[] { r1 } }, new[] { NEAR_END, MID_END }, true);
                triL[0] += Tris(kv.Value[0]); triL[1] += Tris(kv.Value[1]); ci++;
            }
            var chunkRoot = new GameObject("chunks"); chunkRoot.isStatic = true; chunkRoot.transform.SetParent(tr.transform, false);
            foreach (var kv in chunks)
            {
                var mf = Save(kv.Value[0], "k" + hi); var mb2 = Save(kv.Value[1], $"k{hi}_box"); var mh = Save(kv.Value[2], $"k{hi}_hull");
                var go = new GameObject("k" + hi); go.isStatic = true; go.transform.SetParent(chunkRoot.transform, false);
                var rf = Part(go, "far", mf, hm, ShadowCastingMode.Off); var rb = Part(go, "box", mb2, new[] { hm[0] }, ShadowCastingMode.Off);
                // the hull: every house's walls and roofs without openings (and the yard walls): the only shadow caster of the
                // town and its collider
                var hull = Part(go, "hull", mh, new[] { hm[0] }, ShadowCastingMode.ShadowsOnly);
                hull.gameObject.AddComponent<MeshCollider>().sharedMesh = mh;
                Lod(go, mf.bounds.center, new[] { new Renderer[0], new[] { rf }, new[] { rb } }, new[] { FAR_START, BOX_START }, false);
                triL[2] += Tris(kv.Value[0]); triL[3] += Tris(kv.Value[1]); triL[4] += Tris(kv.Value[2]); hi++;
            }
            // main's chunks outside the showcase (as before kuce2: LOD0 casts the shadows, mid from 320 m, far from 1500 m)
            var legRoot = new GameObject("main"); legRoot.isStatic = true; legRoot.transform.SetParent(tr.transform, false);
            int li = 0;
            foreach (var kv in legacy)
            {
                var m0 = Save(kv.Value[0], "h" + li); var m1 = Save(kv.Value[1], $"h{li}_lod1"); var m2 = Save(kv.Value[2], $"h{li}_lod2");
                var go = new GameObject("houses" + li); go.isStatic = true; go.transform.SetParent(legRoot.transform, false);
                var r0 = Part(go, "lod0", m0, hm, ShadowCastingMode.On); r0.gameObject.AddComponent<MeshCollider>().sharedMesh = m0;
                Lod(go, m0.bounds.center, new[] { new[] { r0 }, new[] { Part(go, "lod1", m1, hm, ShadowCastingMode.On) }, new[] { Part(go, "lod2", m2, hm, ShadowCastingMode.On) } }, new[] { 320f, 1500f }, false);
                li++;
            }
            foreach (var kv in town)
            {
                var m0 = Save(kv.Value[0], "t" + li); var m1 = Save(kv.Value[1], $"t{li}_far");
                var go = new GameObject("town" + li); go.isStatic = true; go.transform.SetParent(legRoot.transform, false);
                var r0 = Part(go, "lod0", m0, hm, ShadowCastingMode.On); r0.gameObject.AddComponent<MeshCollider>().sharedMesh = m0;
                Lod(go, m0.bounds.center, new[] { new[] { r0 }, new[] { Part(go, "lod1", m1, hm, ShadowCastingMode.On) } }, new[] { 1500f }, false);
                li++;
            }
            AcUnits(tr.transform);
            Debug.Log($"[Magistrala] building LODs: near {triL[0] / 1000}k (to {NEAR_END} m), mid {triL[1] / 1000}k (to {MID_END} m), far {triL[2] / 1000}k (from {FAR_START} m), box {triL[3] / 1000}k (from {BOX_START} m), shadow hull {triL[4] / 1000}k");
            AssetDatabase.SaveAssets();
            // objects made by script do not dirty the scene: without this, SaveOpenScenes keeps the old buildings (and a player build too)
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[Magistrala] tile buildings: {n} ({skipped} cleared, {nAnnex} annexes, showcase {nShow} ({nDetailed} detailed), main-style {nLegacy} detailed, {nBoxes} boxes, {nYards} yards, {nPaved} paved fronts, facades behind a neighbour {nHidden / 5} hidden, {nBlind / 5} blind), {ci} cells + {hi} chunks");
            Yards.WriteSpots();
        }

        // the AC units: one TreeRenderer prototype (klima LOD1, ~1k tris), drawn instanced to AC_DRAW m, small shadows near
        const float AC_DRAW = 70, AC_SHADOW = 25;
        static void AcUnits(Transform parent)
        {
            var spots = HouseBuilder.AcSpots; if (spots.Count == 0) return;
            var pf = TripoPropImporter.Prefab("klima"); var lg = pf ? pf.GetComponent<LODGroup>() : null; if (!lg) return;
            var lods = lg.GetLODs(); var r = lods[Math.Min(1, lods.Length - 1)].renderers[0];
            var mesh = r.GetComponent<MeshFilter>().sharedMesh; var mat = r.sharedMaterial; mat.enableInstancing = true; EditorUtility.SetDirty(mat);
            float depth = mesh.bounds.size.z;
            var rec = new List<byte>(spots.Count * 24);
            foreach (var (p, n) in spots)
            {
                var o = new Vector3(n.x, 0, n.z).normalized; var q = p + o * (depth / 2 + .02f);
                float yaw = Mathf.Atan2(o.x, o.z) * Mathf.Rad2Deg;
                foreach (var v in new[] { q.x, q.y, q.z, yaw, 1f, 0f }) rec.AddRange(BitConverter.GetBytes(v));
            }
            const string path = "Assets/Generated/TileBuildings/klima.bytes";
            File.WriteAllBytes(path, rec.ToArray()); AssetDatabase.ImportAsset(path);
            var go = new GameObject("klima"); go.transform.SetParent(parent, false);
            var t = go.AddComponent<Magistrala.TreeRenderer>();
            t.data = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            t.meshes = new[] { mesh }; t.materials = new[] { mat }; t.blendMaterials = new[] { mat }; t.materialStart = new[] { 0 };
            t.drawDistance = new[] { AC_DRAW }; t.nearDistance = new[] { 1e5f }; t.impMeshes = new Mesh[1]; t.impMaterials = new Material[1];
            t.shadowDistance = AC_SHADOW; t.impShadowDistance = 0;
            Debug.Log($"[Magistrala] AC units (Tripo klima, instanced to {AC_DRAW} m): {spots.Count}");
        }

        /// the old cores' houses stand at the street: pave the ground between a street facade and the carriageway (concrete
        /// sidewalk, 5 cm over the terrain, in 2 m pieces that follow it), instead of the grass strip the terrain has there
        static int Pave(HouseGeo[] cg, List<Vector2d> P, List<ModelJ> models, int bi)
        {
            int n = 0;
            double area = 0; for (int i = 0; i < P.Count; i++) { var a = P[i]; var c = P[(i + 1) % P.Count]; area += a.x * c.z - c.x * a.z; }
            for (int i = 0; i < P.Count; i++)
            {
                var a = P[i]; var b = P[(i + 1) % P.Count];
                double ex = b.x - a.x, ez = b.z - a.z, L = Math.Sqrt(ex * ex + ez * ez); if (L < 1.5) continue; ex /= L; ez /= L;
                double nx = ez, nz = -ex; if (area > 0) { nx = -nx; nz = -nz; }
                double mx = (a.x + b.x) / 2, mz = (a.z + b.z) / 2;
                if (InPoly(P, mx + nx * .4, mz + nz * .4)) { nx = -nx; nz = -nz; }
                // march out along the normal to the carriageway's edge (at most 10 m: farther is a yard, not a pavement)
                double depth = -1;
                for (double d = .3; d <= 10; d += .25) if (Yards.Clear(mx + nx * d, mz + nz * d).clear < .05) { depth = d + .2; break; }
                if (depth < .6) continue;
                int k = Math.Max(1, (int)Math.Ceiling(L / 2));
                float lift = .05f + .003f * (i % 4); // neighbouring strips at a corner do not z-fight
                int m = Math.Max(1, (int)Math.Ceiling(depth / 1.2)); // across too: the terrain rises to the road (grass banks)
                for (int j = 0; j < k; j++)
                for (int jd = 0; jd < m; jd++)
                {
                    double t0 = L * j / k, t1 = L * (j + 1) / k, d0 = .02 + (depth - .02) * jd / m, d1 = .02 + (depth - .02) * (jd + 1) / m;
                    Vector3 V(double t, double d)
                    {
                        double x = a.x + ex * t + nx * d, z = a.z + ez * t + nz * d;
                        return U(x, Ground(models, x, z) + lift + .04, z);
                    }
                    Vector3 p0 = V(t0, d0), p1 = V(t1, d0), q1 = V(t1, d1), q0 = V(t0, d1);
                    HouseBuilder.Paving(cg[0], p0, p1, q1, q0); HouseBuilder.Paving(cg[1], p0, p1, q1, q0);
                }
                n++;
            }
            return n;
        }

        static Renderer Part(GameObject go, string name, Mesh mesh, Material[] mats, ShadowCastingMode shadows)
        {
            var c = new GameObject(name); c.isStatic = true; c.transform.SetParent(go.transform, false);
            c.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = c.AddComponent<MeshRenderer>(); r.sharedMaterials = mats; r.shadowCastingMode = shadows;
            if (shadows == ShadowCastingMode.Off) r.receiveShadows = true;
            return r;
        }

        // the thresholds assume the chase camera's fov (55 deg at rest) and QualitySettings.lodBias of the PC level (2, SceneSetup)
        const float LOD_FOV = 55, LOD_BIAS = 2;

        static int Tris(HouseGeo g) { int n = 0; foreach (var s in g.sub) n += s.Count / 3; return n; }

        /// LODGroup on `go`: level i holds levels[i] (may be empty: nothing drawn that near) up to dist[i] m from `centre`;
        /// cullLast: the last level ends at the last distance, else it reaches the far clip
        static void Lod(GameObject go, Vector3 centre, Renderer[][] levels, float[] dist, bool cullLast)
        {
            var lg = go.AddComponent<LODGroup>();
            // size only scales the thresholds (a real 256 m chunk would need heights > 1 at 450 m with bias 2): a nominal 16 m
            const float size = 16;
            float H(float d) => Mathf.Clamp01(LOD_BIAS * size / (d * 2 * Mathf.Tan(LOD_FOV * .5f * Mathf.Deg2Rad)));
            var lods = new LOD[levels.Length];
            for (int i = 0; i < levels.Length; i++)
                lods[i] = new LOD(i < dist.Length && (cullLast || i < levels.Length - 1) ? H(dist[i]) : 0f, levels[i]);
            lg.fadeMode = LODFadeMode.None;
            lg.SetLODs(lods);
            lg.localReferencePoint = centre; lg.size = size; // after SetLODs, which recalculates both from the renderers
        }

        /// the probe area (game x, z, radius m): Kaštel Lukšić around the warehouse (stops.json kastela_skladiste)
        public static readonly (double x, double z, double r)[] Probe = { (-6709.6, -5526.8, 420) };
        public static bool InProbe(double x, double z) { foreach (var p in Probe) if ((x - p.x) * (x - p.x) + (z - p.z) * (z - p.z) < p.r * p.r) return true; return false; }

        public struct Vector2d { public double x, z; public Vector2d(double x, double z) { this.x = x; this.z = z; } }

        public static bool Covered(List<double[]> rects, List<Vector2d> P, double cx, double cz)
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
        public static bool InPoly(List<Vector2d> r, double x, double z)
        {
            bool hit = false;
            for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
                if ((r[i].z > z) != (r[j].z > z) && x < (r[j].x - r[i].x) * (z - r[i].z) / (r[j].z - r[i].z) + r[i].x) hit = !hit;
            return hit;
        }

        static Vector3 U(double gx, double y, double gz) => W.U(gx, y, gz);

        static void Extrude(HouseGeo g, List<Vector2d> P, string kind, double bottom, double baseY, double top, double cx, double cz, bool windows)
        {
            int hsh = (int)(Math.Abs(Math.Sin(cx * 12.9898 + cz * 78.233) * 43758.5453) * 1000);
            bool ind = kind == "industrial" || kind == "warehouse";
            var look = Look.Pick(new System.Random(hsh), kind, (float)baseY, (float)(top - baseY), InCore(cx, cz));
            // far level: no window quads; the wall takes their average (1.1 x 1.35 m per 3.2 x 3 m cell = 15 %)
            if (!windows && !ind) { look.wall = Color.Lerp(look.wall, WIN, .15f); look.trim = look.wall; }
            HouseBuilder.Begin(look, (float)baseY, (float)top);
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
                Quad(g, U(a.x, bottom, a.z), U(b.x, bottom, b.z), U(b.x, baseY + .5, b.z), U(a.x, baseY + .5, a.z), Slot.Balcony, nrm);
                Quad(g, U(a.x, baseY + .5, a.z), U(b.x, baseY + .5, b.z), U(b.x, top, b.z), U(a.x, top, a.z), Slot.Wall, nrm);
                // windows: glass with rooms behind, per floor, slightly out of the wall
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
                            float r0 = (float)((t0 * nw) - k), r1 = (float)((t1 * nw) - k);
                            float rnd = Mathf.Repeat(Mathf.Sin((float)(cx * 3.1 + cz * 1.7 + f * 13.1 + k * 7.7 + i * 3.3)) * 43758.55f, 1);
                            float blind = look.rollers ? Mathf.Repeat(rnd * 17.3f, 1) * .9f : 0;
                            HouseBuilder.EmitGlass(g, U(a.x + (b.x - a.x) * t0, y0, a.z + (b.z - a.z) * t0) + off, U(a.x + (b.x - a.x) * t1, y0, a.z + (b.z - a.z) * t1) + off,
                                U(a.x + (b.x - a.x) * t1, y1, a.z + (b.z - a.z) * t1) + off, U(a.x + (b.x - a.x) * t0, y1, a.z + (b.z - a.z) * t0) + off,
                                new Vector2(r0, .3f), new Vector2(r1, .75f), nrm, rnd, blind);
                        }
                }
            }
            // roof
            var pts = new List<Vector3>(); foreach (var p in P) pts.Add(U(p.x, top, p.z));
            if (!hip) { foreach (var t in Ear(pts)) Tri(g, pts[t[0]], pts[t[1]], pts[t[2]], ind ? Slot.Wall : Slot.Balcony, Vector3.up); return; }
            double d = .45 * area / (per / 2);
            var inner = new List<Vector3>();
            for (int i = 0; i < P.Count; i++)
            {
                var c0 = new Vector3((float)cx, 0, (float)cz);
                // inset towards the centroid (fine for the near-rectangular footprints that get a hip roof)
                var p = new Vector3((float)P[i].x, 0, (float)P[i].z); var dir = (c0 - p); float L = dir.magnitude;
                var q = p + dir / Mathf.Max(L, .01f) * Mathf.Min((float)d * 1.35f, L * .8f);
                inner.Add(U(q.x, top + d * .55, q.z));
            }
            for (int i = 0; i < P.Count; i++) { int j = (i + 1) % P.Count; var o0 = pts[i]; var o1 = pts[j]; var e = o1 - o0; var nrm = new Vector3(e.z, 0, -e.x); var mid = (o0 + o1) / 2; if (Vector3.Dot(nrm, mid - new Vector3(C.x, mid.y, C.z)) < 0) nrm = -nrm; Quad(g, o0, o1, inner[j], inner[i], Slot.Roof, nrm + Vector3.up * 1.5f); }
            foreach (var t in Ear(inner)) Tri(g, inner[t[0]], inner[t[1]], inner[t[2]], Slot.Roof, Vector3.up);
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
