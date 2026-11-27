// Magistrala/Riva: oprema i brodice (2026-11-27) — the Kaštela waterfronts from BalkanDriver tools/world/riva-extras (README.md there):
//   1. lukobrani   breakwaters.json (51 moles / piers / quays outside the riva paving): a flat weathered-concrete top at
//                  topRelativeToSeaM (0.65 m), a limestone coping (35 cm) and a stone wall down to bottomRelativeToSeaM on every
//                  wallSides segment (the face away from the polygon's inside), one mesh + collider per structure (World/RivaLukobrani);
//                  the terrain under each polygon goes 0.3 m below its top and, 6 m round it, under the sea (−1.2 m): no more
//                  brown earth tongues
//   2. oprema      oprema.json (benches facing the sea, bins, bike racks, bollards, two-globe lamps, mooring bitts) with our own
//                  Blender props (Assets/Models/Props/RivaOprema, BalkanDriver assets-src/riva-oprema/riva_oprema.py, imported by
//                  TripoPropImporter: LODs, instanced material, box colliders), each on the first collider below it (riva paving,
//                  mole, street, terrain); bitts on the riva paving are skipped (RivaBuilder puts its own every 22 m)
//   3. brodice     BoatsBuilder.Place (coast.json + riva-extras/boats.json berths; the moles' colliders keep hulls off them)
// Deterministic (data order, no randomness except hashed variants). Trees are not touched (palms / tamarisks: other branch).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class RivaOprema
    {
        const string SRC = W.BD + "/tools/world/riva-extras/";
        const string OUT = WorldBuilder.GEN + "/RivaLukobrani";

        [MenuItem("Magistrala/Riva: oprema i brodice")]
        public static string BuildMenu()
        {
            MainScene.Ensure();
            var s = Build(); Debug.Log(s); return s;
        }

        /// batch: -executeMethod Magistrala.EditorTools.RivaOprema.Batch
        public static void Batch() { MainScene.Ensure(); Debug.Log(Build()); EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene()); }

        public static string Build()
        {
            var log = new List<string>();
            log.Add(Lukobrani());
            Physics.SyncTransforms();
            foreach (var f in Directory.GetFiles(TripoPropImporter.SRC_RO, "*.glb").OrderBy(x => x))
                log.Add(TripoPropImporter.ImportAll(Path.GetFileNameWithoutExtension(f)).Trim());
            // PropsBuilder's white riva benches give way to oprema.json's (PropsBuilder.Benches skips them from now on)
            foreach (var t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => t.name == "riva-klupe").ToList()) UnityEngine.Object.DestroyImmediate(t.gameObject);
            log.Add(Oprema());
            log.Add(BoatsBuilder.Place());
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            return "[riva-oprema]\n  " + string.Join("\n  ", log);
        }

        // ------------------------------------------------------------------ geometry helpers
        static bool Inside(List<Vector2> poly, Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) && p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
            return c;
        }
        static float SegDist(Vector2 p, Vector2 a, Vector2 b) { var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude)); return (a + ab * t - p).magnitude; }
        static float Area(List<Vector2> p) { float s = 0; for (int i = 0, j = p.Count - 1; i < p.Count; j = i++) s += (p[j].x * p[i].y - p[i].x * p[j].y); return s * .5f; }

        /// ear clipping of a simple polygon (no holes); returns index triples
        static List<int> EarClip(List<Vector2> p)
        {
            var idx = Enumerable.Range(0, p.Count).ToList(); var tris = new List<int>();
            if (Area(p) < 0) idx.Reverse();
            bool Convex(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x) > 1e-7f;
            bool InTri(Vector2 q, Vector2 a, Vector2 b, Vector2 c)
            {
                float d1 = (q.x - b.x) * (a.y - b.y) - (a.x - b.x) * (q.y - b.y), d2 = (q.x - c.x) * (b.y - c.y) - (b.x - c.x) * (q.y - c.y), d3 = (q.x - a.x) * (c.y - a.y) - (c.x - a.x) * (q.y - a.y);
                return !((d1 < 0 || d2 < 0 || d3 < 0) && (d1 > 0 || d2 > 0 || d3 > 0));
            }
            int guard = 0;
            while (idx.Count > 3 && guard++ < 100000)
            {
                bool cut = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    if (!Convex(p[ia], p[ib], p[ic])) continue;
                    bool ear = true;
                    foreach (int k in idx) if (k != ia && k != ib && k != ic && InTri(p[k], p[ia], p[ib], p[ic])) { ear = false; break; }
                    if (!ear) continue;
                    tris.Add(ia); tris.Add(ib); tris.Add(ic); idx.RemoveAt(i); cut = true; break;
                }
                if (!cut) { // degenerate rest: fan it
                    for (int i = 1; i + 1 < idx.Count; i++) { tris.Add(idx[0]); tris.Add(idx[i]); tris.Add(idx[i + 1]); }
                    idx.Clear(); break;
                }
            }
            if (idx.Count == 3) tris.AddRange(idx);
            return tris;
        }

        // ------------------------------------------------------------------ 1. breakwaters / moles
        static string Lukobrani()
        {
            var path = SRC + "breakwaters.json"; if (!File.Exists(path)) return "lukobrani: nema " + path;
            if (!AssetDatabase.IsValidFolder(OUT)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "RivaLukobrani");
            foreach (var f in Directory.GetFiles(OUT, "*.asset")) AssetDatabase.DeleteAsset(f);
            var root = WorldBuilder.Root("RivaLukobrani");
            var top = WorldBuilder.Lit("LukobranBeton", new Color(.86f, .84f, .80f), .12f, 0, "PolyHaven/concrete_wall_007_d.jpg", "PolyHaven/concrete_wall_007_n.jpg", .8f);
            var wall = CesteMaterials.Stone(WorldBuilder.Lit("LukobranZid", new Color(.95f, .95f, .93f), .1f, 0, "PolyHaven/riva_zid_d.jpg", "PolyHaven/riva_zid_n.jpg", 1f), "riva_rub", true, true);
            var phys = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(WorldBuilder.GEN + "/Roads/Asphalt.physicMaterial");
            var shapes = new List<(List<Vector2> ring, float top)>();
            int n = 0, tris = 0, walls = 0;
            foreach (var bo in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(path)))["breakwaters"]))
            {
                var b = MiniJson.O(bo); string id = (string)b["id"];
                var ringL = MiniJson.L(MiniJson.L(MiniJson.O(b["polygon"])["coordinates"])[0]);
                var ring = new List<Vector2>();
                foreach (var q in ringL) { var l = MiniJson.L(q); var v = new Vector2(MiniJson.F(l[0]), MiniJson.F(l[1])); if (ring.Count == 0 || (ring[ring.Count - 1] - v).sqrMagnitude > 1e-4f) ring.Add(v); }
                if (ring.Count > 3 && (ring[0] - ring[ring.Count - 1]).sqrMagnitude < 1e-4f) ring.RemoveAt(ring.Count - 1);
                if (ring.Count < 3) continue;
                float yTop = MiniJson.G(b, "topRelativeToSeaM") is object t0 ? MiniJson.F(t0) : .65f;
                shapes.Add((ring, yTop));
                var V = new List<Vector3>(); var U = new List<Vector2>(); var Tt = new List<int>(); var Tw = new List<int>();
                // top
                foreach (var g in ring) { V.Add(W.U(g.x, yTop, g.y)); U.Add(g / 3f); }
                var et = EarClip(ring);
                for (int k = 0; k < et.Count; k += 3)
                {
                    int a = et[k], bb = et[k + 1], c = et[k + 2];
                    if (Vector3.Cross(V[bb] - V[a], V[c] - V[a]).y < 0) { var s = bb; bb = c; c = s; }
                    Tt.Add(a); Tt.Add(bb); Tt.Add(c);
                }
                // walls + coping on the water sides
                float along = 0;
                foreach (var wo in MiniJson.L(b["wallSides"]))
                {
                    var w = MiniJson.O(wo); var line = MiniJson.L(w["line"]);
                    var l0 = MiniJson.L(line[0]); var l1 = MiniJson.L(line[1]);
                    var ga = new Vector2(MiniJson.F(l0[0]), MiniJson.F(l0[1])); var gb = new Vector2(MiniJson.F(l1[0]), MiniJson.F(l1[1]));
                    float L = (gb - ga).magnitude; if (L < .05f) continue;
                    float bot = MiniJson.G(w, "bottomRelativeToSeaM") is object bt ? MiniJson.F(bt) : -1.2f;
                    var dir = (gb - ga) / L; var nrm = new Vector2(-dir.y, dir.x);
                    if (Inside(ring, (ga + gb) * .5f + nrm * .3f)) nrm = -nrm;               // outward = away from the inside
                    var ua = W.U(ga.x, yTop, ga.y); var ub = W.U(gb.x, yTop, gb.y);
                    var face = new Vector3(nrm.x, 0, -nrm.y);
                    void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3, Vector3 f)
                    {
                        int o = V.Count; V.Add(p0); V.Add(p1); V.Add(p2); V.Add(p3); U.Add(u0); U.Add(u1); U.Add(u2); U.Add(u3);
                        bool flip = Vector3.Dot(Vector3.Cross(p1 - p0, p2 - p0), f) < 0;
                        if (!flip) Tw.AddRange(new[] { o, o + 1, o + 2, o, o + 2, o + 3 }); else Tw.AddRange(new[] { o, o + 2, o + 1, o, o + 3, o + 2 });
                    }
                    const float s = 2.5f; float cap = .05f;
                    // face from the coping's top edge down to the bottom
                    var ca = ua + face * .04f + Vector3.up * cap; var cb = ub + face * .04f + Vector3.up * cap;
                    Quad(ca, cb, new Vector3(cb.x, bot, cb.z), new Vector3(ca.x, bot, ca.z), new Vector2(along / s, (yTop + cap) / s), new Vector2((along + L) / s, (yTop + cap) / s), new Vector2((along + L) / s, bot / s), new Vector2(along / s, bot / s), face);
                    // coping top (35 cm wide, 5 cm proud) and its inner lip
                    var ia2 = ca - face * .39f; var ib2 = cb - face * .39f;
                    Quad(ca, cb, ib2, ia2, new Vector2(along / s, 0), new Vector2((along + L) / s, 0), new Vector2((along + L) / s, .14f), new Vector2(along / s, .14f), Vector3.up);
                    Quad(ia2, ib2, ib2 - Vector3.up * cap, ia2 - Vector3.up * cap, new Vector2(along / s, 0), new Vector2((along + L) / s, 0), new Vector2((along + L) / s, .02f), new Vector2(along / s, .02f), -face);
                    along += L; walls++;
                }
                var mesh = new Mesh { name = "lukobran-" + n, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(V); mesh.SetUVs(0, U); mesh.subMeshCount = 2; mesh.SetTriangles(Tt, 0); mesh.SetTriangles(Tw, 1);
                mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, $"{OUT}/lukobran-{n}.asset");
                var go = new GameObject("lukobran " + id); go.isStatic = true; go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = new[] { top, wall };
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = mesh; mc.sharedMaterial = phys;
                tris += (Tt.Count + Tw.Count) / 3; n++;
            }
            return $"lukobrani: {n} konstrukcija, {walls} segmenata zida, {tris} trokuta; " + LowerTerrain(shapes);
        }

        /// terrain under a structure 0.3 m below its top, within 6 m outside it under the sea (−1.2 m); min() only
        static string LowerTerrain(List<(List<Vector2> ring, float top)> shapes)
        {
            int nIn = 0, nOut = 0, tiles = 0;
            foreach (var t in UnityEngine.Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                var parts = t.name.Split(' '); if (parts.Length < 2) continue; var xz = parts[1].Split(','); if (xz.Length < 2) continue;
                if (!int.TryParse(xz[0], out int tx) || !int.TryParse(xz[1], out int tz)) continue;
                var td = t.terrainData; int res = td.heightmapResolution; double step = 1024.0 / (res - 1);
                float[,] H = null; bool ch = false;
                foreach (var (ring, top) in shapes)
                {
                    float x0 = ring.Min(p => p.x) - 6, x1 = ring.Max(p => p.x) + 6, z0 = ring.Min(p => p.y) - 6, z1 = ring.Max(p => p.y) + 6;
                    if (x1 < tx * 1024 || x0 > tx * 1024 + 1024 || z1 < tz * 1024 || z0 > tz * 1024 + 1024) continue;
                    if (H == null) H = td.GetHeights(0, 0, res, res);
                    int c0 = Mathf.Max(0, (int)Math.Floor((x0 - tx * 1024) / step)), c1 = Mathf.Min(res - 1, (int)Math.Ceiling((x1 - tx * 1024) / step));
                    int r0 = Mathf.Max(0, (int)Math.Floor(((tz + 1) * 1024 - z1) / step)), r1 = Mathf.Min(res - 1, (int)Math.Ceiling(((tz + 1) * 1024 - z0) / step));
                    for (int r = r0; r <= r1; r++)
                        for (int c = c0; c <= c1; c++)
                        {
                            var g = new Vector2((float)(tx * 1024 + c * step), (float)((tz + 1) * 1024 - r * step));
                            float target;
                            if (Inside(ring, g)) { target = top - .3f; nIn++; }
                            else
                            {
                                float d = float.MaxValue; for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++) d = Mathf.Min(d, SegDist(g, ring[j], ring[i]));
                                if (d > 6) continue;
                                if (RivaBuilder.Class(g.x, g.y) == 1 || RivaBuilder.Class(g.x, g.y) == 2) continue;    // riva paving / its wall foot: RivaBuilder's
                                if (Tiles.Height(g.x, g.y) > 1.5f) continue;                                               // real land next to the mole stays
                                target = -1.2f; nOut++;
                            }
                            float h = WorldBuilder.YMIN + H[r, c] * WorldBuilder.YSIZE;
                            if (target < h) { H[r, c] = Mathf.Clamp01((target - WorldBuilder.YMIN) / WorldBuilder.YSIZE); ch = true; }
                        }
                }
                if (ch) { td.SetHeights(0, 0, H); EditorUtility.SetDirty(td); tiles++; }
            }
            return $"teren: {nIn} uzoraka ispod lukobrana, {nOut} uz njih pod morem, {tiles} pločica";
        }

        // ------------------------------------------------------------------ 2. furniture
        static string PropFor(string type, string id)
        {
            type = type.ToLowerInvariant();
            if (type.Contains("bitt")) return "bitva";
            if (type.Contains("bench")) return "klupa-crvena";
            if (type.Contains("bike")) return "stalak-bicikli";
            if (type.Contains("bin") || type.Contains("litter")) return "kanta-riva";
            if (type.Contains("lamp")) return "lampa-dvije-kugle";
            if (type.Contains("bollard")) return "stupic-beton";
            return null;
        }

        static bool Ignored(Collider c)
        {
            for (var p = c.transform; p; p = p.parent) if (p.name == "Props" || p.name == "RivaOprema" || p.name == "Boats") return true;
            return c.attachedRigidbody || c.GetComponentInParent<TruckController>();
        }

        static float GroundY(Vector3 u)
        {
            var hits = Physics.RaycastAll(new Vector3(u.x, 200, u.z), Vector3.down, 400, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN;
            foreach (var h in hits) { if (Ignored(h.collider)) continue; if (float.IsNaN(best) || h.point.y > best) best = h.point.y; }
            return best;
        }

        /// café terraces: at each mapped café (oprema.json exclusionZones cafe_terrace_allowance) up to three parasol sets (3 × 3 m) on the
        /// riva paving next to it, the ones nearest the café first (by the house front, the walking lane by the sea stays free): level
        /// ground (±12 cm), no building / road over or under it, the sea ≥ 4.5 m away all round, clear of the other furniture
        static int Terraces(Dictionary<string, object> data, Transform root, List<Vector3> placed, Action<string> why)
        {
            if (!data.ContainsKey("exclusionZones")) return 0;
            var vars = new[] { "kafic-set-krem", "kafic-set-bijela", "kafic-set-crvena" }.Select(TripoPropImporter.Prefab).ToArray();
            if (vars.Any(v => !v)) { why("nema prefaba kafic-set"); return 0; }
            var g = new GameObject("kafic-terase").transform; g.SetParent(root, false);
            var sets = new List<Vector3>(); int n = 0, ci = 0;
            bool Street(Vector3 p)
            {
                if (!Physics.Raycast(new Vector3(p.x, 200, p.z), Vector3.down, out var h, 400, ~0, QueryTriggerInteraction.Ignore)) return true;
                for (var t = h.collider.transform; t; t = t.parent) if (t.name == "Roads" || t.name == "Streets" || t.name == "Buildings" || t.name == "RivaLukobrani") return true;
                return false;
            }
            foreach (var zo in MiniJson.L(data["exclusionZones"]))
            {
                var z = MiniJson.O(zo); if ((string)z["kind"] != "cafe_terrace_allowance") continue;
                var pos = MiniJson.L(z["position"]); double gx = MiniJson.D(pos[0]), gz = MiniJson.D(pos[1]); ci++;
                if (gx < StreetsBuilder.AX0 || gx >= StreetsBuilder.AX1 || gz < StreetsBuilder.AZ0 || gz >= StreetsBuilder.AZ1) continue;
                var c = W.U(gx, 0, gz); var pf = vars[ci % 3]; int got = 0;
                var cands = new List<(Vector3 p, float d)>();
                for (float r = 3.5f; r <= 12; r += 1.5f)
                    for (int a = 0; a < 24; a++) { float an = a * 15 * Mathf.Deg2Rad; var p = c + new Vector3(Mathf.Sin(an), 0, Mathf.Cos(an)) * r; cands.Add((p, r)); }
                foreach (var (p0, d) in cands.OrderBy(q => q.d))
                {
                    if (got >= 3) break;
                    var gp = W.G(p0); if (RivaBuilder.Class(gp.x, gp.y) != 1) continue;
                    if (sets.Any(s2 => (s2 - p0).sqrMagnitude < 3.3f * 3.3f) || placed.Any(q => (new Vector2(q.x - p0.x, q.z - p0.z)).sqrMagnitude < 2.4f * 2.4f)) continue;
                    float y = GroundY(p0); if (float.IsNaN(y) || y < .3f || Street(p0)) continue;
                    bool ok = true;
                    for (int k = 0; k < 8 && ok; k++)
                    {
                        var dir = Quaternion.Euler(0, k * 45, 0) * Vector3.forward;
                        var e = p0 + dir * 1.5f; float ye = GroundY(e); if (float.IsNaN(ye) || Mathf.Abs(ye - y) > .12f || Street(e)) ok = false;
                        var s4 = p0 + dir * 4.5f; float ys = GroundY(s4); if (float.IsNaN(ys) || ys < .2f) ok = false;
                    }
                    if (!ok) continue;
                    // nothing solid in the set's space (house walls, signs, trees with colliders, lamps)
                    var hitsBox = Physics.OverlapBox(new Vector3(p0.x, y + 1.4f, p0.z), new Vector3(1.4f, 1.1f, 1.4f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                    if (hitsBox.Any(hc => !hc.transform.IsChildOf(g))) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, g);
                    float yaw = Quaternion.LookRotation(new Vector3(p0.x - c.x, 0, p0.z - c.z)).eulerAngles.y;
                    go.transform.SetPositionAndRotation(new Vector3(p0.x, y, p0.z), Quaternion.Euler(0, Mathf.Round(yaw / 90) * 90 + (ci % 7) - 3, 0));
                    go.name = "kafic-set"; go.isStatic = false; Physics.SyncTransforms();
                    sets.Add(p0); got++; n++;
                }
            }
            return n;
        }

        static string Oprema()
        {
            var path = SRC + "oprema.json"; if (!File.Exists(path)) return "oprema: nema " + path + " (preskočeno)";
            var root = WorldBuilder.Root("RivaOprema");
            var groups = new Dictionary<string, Transform>(); var count = new Dictionary<string, int>(); var why = new Dictionary<string, int>();
            void Why(string w) { why[w] = why.TryGetValue(w, out int k) ? k + 1 : 1; }
            var prefabs = new Dictionary<string, GameObject>();
            var placed = new List<Vector3>();
            // our street lamps (Magistrala.StreetLights.lamps, Mapillary-placed): a proposed riva lamp within 10 m of one is left out
            var lamps = new List<Vector3>();
            foreach (var sl in UnityEngine.Object.FindObjectsByType<Magistrala.StreetLights>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (sl.lamps != null) foreach (var l in sl.lamps) lamps.Add(new Vector3(l.x, l.y, l.z));
            var data = MiniJson.O(MiniJson.Parse(File.ReadAllText(path)));
            foreach (var it in MiniJson.L(data["items"]))
            {
                var o = MiniJson.O(it); string type = (string)o["type"]; string prop = PropFor(type, (string)o["id"]);
                if (prop == null) { Why("nepoznat tip " + type); continue; }
                if (!prefabs.TryGetValue(prop, out var pf)) prefabs[prop] = pf = TripoPropImporter.Prefab(prop);
                if (!pf) { Why("nema prefaba " + prop); continue; }
                var pos = MiniJson.L(o["position"]); double gx = MiniJson.D(pos[0]), gz = MiniJson.D(pos[1]);
                if (gx < StreetsBuilder.AX0 || gx >= StreetsBuilder.AX1 || gz < StreetsBuilder.AZ0 || gz >= StreetsBuilder.AZ1) { Why("izvan Kaštela"); continue; }
                if (prop == "bitva" && RivaBuilder.Class(gx, gz) != 0) { Why("bitva na rivi (RivaBuilder ih ima)"); continue; }
                var u = W.U(gx, 0, gz); float y = GroundY(u);
                if (prop == "lampa-dvije-kugle" && lamps.Any(l => (new Vector2(l.x - u.x, l.z - u.z)).sqrMagnitude < 100)) { Why("lampa: naša rasvjeta blizu"); continue; }
                if (float.IsNaN(y) || y < .15f) { Why("nema tla / u moru"); continue; }
                if (!groups.TryGetValue(prop, out var g)) { g = new GameObject(prop).transform; g.SetParent(root.transform, false); groups[prop] = g; }
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pf, g);
                float yaw = 180 - MiniJson.F(o["headingDeg"]);
                go.transform.SetPositionAndRotation(new Vector3(u.x, y, u.z), Quaternion.Euler(0, yaw, 0));
                go.name = prop; go.isStatic = false;
                count[prop] = count.TryGetValue(prop, out int c) ? c + 1 : 1; placed.Add(go.transform.position);
            }
            int terr = Terraces(data, root.transform, placed, Why);
            count["kafic-set"] = terr;
            return $"oprema: {count.Values.Sum()} komada ({string.Join(", ", count.Select(x => $"{x.Key} {x.Value}"))}); preskočeno: {string.Join(", ", why.Select(x => $"{x.Key} {x.Value}"))}";
        }
    }
}
