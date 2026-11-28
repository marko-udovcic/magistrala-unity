// Magistrala/Autobusne stanice: postavi — the real bus stops of Kaštela (World/Stanice), run after "Rekviziti: postavi".
// Data: Assets/Editor/stanice-mapillary.json = BalkanDriver tools/world/mapillary/stanice-all.json (s8_stanice.py: OSM bus stops /
// platforms (ODbL, OpenStreetMap contributors) checked against Mapillary map features and street photos (CC BY-SA 4.0, Mapillary
// contributors)): per stop game x / z, the bus's heading (the stop on its right), shelter yes / no, nearby driveways.
//   shelter   the Tripo model "stanica" (Assets/Models/Props/Tripo/stanica.glb + .json: 3.8 x 2.5 x 2.0 m, open side +Z; since 27. 11.
//             its glass-sided version Assets/Models/Props/Stanice/stanica-staklo.glb, StanicaStaklo.cs) on the
//             pavement / verge beside the lane, its open side to the road: the nearest carriageway (StreetsBuilder.Carriageways)
//             running with the bus, the footprint 0.5–3.5 m beyond its edge, slid up to ±20 m along it until it is free
//             (PropsBuilder.Free: ground, not the sea, not on any carriageway / model asphalt, not in a delivery bay, no building /
//             wall / parked car, no prop), ≥ 10 m from a junction, off the Mapillary driveways and not in front of a yard gate /
//             house door (Yards.SpotsPath: gates, doors, frontGates = the street walls' gates, so after Build tile buildings)
//   pole      a stop without a shelter (or with no room for one): a generic stop sign (blue plate, white bus, no brand), 0.4–2 m
//             beyond the edge; one procedural mesh + one material (Assets/Generated/Props/Stanice)
// Cheap: every shelter is the one instanced prefab (LODGroup, 3 LODs, culled at its config's distance), every pole one mesh with one
// instanced material: two kinds of draw, ~50 objects in all.
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
    public static partial class PropsBuilder
    {
        public const string STANICE = "Assets/Editor/stanice-mapillary.json";
        const string STANICE_GEN = WorldBuilder.GEN + "/Props/Stanice";

        [MenuItem("Magistrala/Autobusne stanice: postavi")]
        public static string StaniceMenu()
        {
            MainScene.Ensure();
            var s = Stanice();
            var sc = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc);
            Debug.Log(s); return s;
        }

        static List<Vector3> stGates = new List<Vector3>();
        class Stop { public string id, name, road; public double x, z, heading; public bool shelter; public Vector2? bus; public List<Vector2> drive = new List<Vector2>(); }

        static List<Stop> LoadStops()
        {
            var r = new List<Stop>(); if (!File.Exists(STANICE)) return r;
            var package = MiniJson.O(MiniJson.Parse(File.ReadAllText(PaketData.Need(PaketData.STANICE))));
            var tr = MiniJson.O(MiniJson.O(MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "paket-approaches.json")))["alignment"])["transform"]);
            double A(string k) => MiniJson.D(tr[k]);
            var busByOsm = new Dictionary<string, Vector2>();
            foreach (var po in MiniJson.L(package["stops"]))
            {
                var ps = MiniJson.O(po);
                if (!(MiniJson.G(ps, "lanePoint") is List<object> lp)) continue;
                double x = MiniJson.D(lp[0]), z = MiniJson.D(lp[1]);
                var g = new Vector2((float)(A("a") * x + A("b") * z + A("tx")), (float)(A("c") * x + A("d") * z + A("tz")));
                foreach (var id in MiniJson.L(ps["osm"])) busByOsm[(string)id] = g;
            }
            System.Text.StringBuilder unlinked = null;
            foreach (var o in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(STANICE)))["stops"]))
            {
                var d = MiniJson.O(o);
                var s = new Stop { id = (string)d["id"], name = (string)MiniJson.G(d, "name") ?? "", road = (string)MiniJson.G(d, "road") ?? "", x = MiniJson.D(d["x"]), z = MiniJson.D(d["z"]),
                                   heading = MiniJson.D(d["heading"]), shelter = d["shelter"] is bool b && b };
                if (MiniJson.L(MiniJson.G(d, "driveways")) is List<object> dl) foreach (var q in dl) { var p = MiniJson.L(q); s.drive.Add(new Vector2(MiniJson.F(p[0]), MiniJson.F(p[1]))); }
                foreach (var id in MiniJson.L(d["osm"]))
                {
                    string key = ((string)id).Replace("node/", "n").Replace("way/", "w").Replace("relation/", "r");
                    if (busByOsm.TryGetValue(key, out var bus)) { s.bus = bus; break; }
                }
                // Stops outside the package keep their original placement without a BUS-paint constraint.
                if (!s.bus.HasValue)
                {
                    if (unlinked == null) unlinked = new System.Text.StringBuilder();
                    else unlinked.Append(", ");
                    unlinked.Append(s.id).Append(" (").Append(s.name).Append(')');
                }
                r.Add(s);
            }
            if (unlinked != null) Debug.LogWarning("[stanice] no package BUS reference; keeping original positioning: " + unlinked);
            return r;
        }

        public static string Stanice()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            log = new List<string>(); perProp = new Dictionary<string, int>(); placed = new List<(Vector2, Vector2, float)>(); nPlaced = nRejected = 0;
            info = new Dictionary<string, (Vector3, GameObject)>();
            var stops = LoadStops();
            if (stops.Count == 0) return $"[stanice] nema {STANICE}";
            if (!TripoPropImporter.Prefab("stanica")) log.Add(TripoPropImporter.ImportAll("stanica").Trim());
            var pf = TripoPropImporter.Prefab("stanica");
            // 27. 11.: the shelter with glass sides (StanicaStaklo), same footprint / placement, when its GLB is there
            if (File.Exists(StanicaStaklo.GLB)) { log.Add(StanicaStaklo.Build()); var gp = StanicaStaklo.Prefab(); if (gp) pf = gp; }
            if (pf)
            {
                var b = new Bounds(); bool first = true;
                foreach (var mf in pf.GetComponentsInChildren<MeshFilter>()) { if (!mf.name.StartsWith("lod0")) continue; var mb = mf.sharedMesh.bounds; if (first) b = mb; else b.Encapsulate(mb); first = false; }
                info["stanica"] = (b.size, pf);
            }
            var pole = PolePrefab(); info["stup-stanica"] = (new Vector3(.62f, 2.75f, .1f), pole);

            roads = StreetsBuilder.Carriageways(); roadGrid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < roads.Count; i++)
            {
                var r = roads[i]; double pad = r.hw + 4;
                for (int gx = (int)Math.Floor((Math.Min(r.ax, r.bx) - pad) / 16); gx <= (int)Math.Floor((Math.Max(r.ax, r.bx) + pad) / 16); gx++)
                    for (int gz = (int)Math.Floor((Math.Min(r.az, r.bz) - pad) / 16); gz <= (int)Math.Floor((Math.Max(r.az, r.bz) + pad) / 16); gz++)
                    { if (!roadGrid.TryGetValue((gx, gz), out var l)) roadGrid[(gx, gz)] = l = new List<int>(); l.Add(i); }
            }
            // junctions: carriageway end points shared by >= 3 segment ends (a polyline's inner points have 2)
            var ends = new Dictionary<(long, long), int>();
            foreach (var r in roads) foreach (var p in new[] { (r.ax, r.az), (r.bx, r.bz) })
                { var k = ((long)Math.Round(p.Item1 * 2), (long)Math.Round(p.Item2 * 2)); ends.TryGetValue(k, out int c); ends[k] = c + 1; }
            var junctions = ends.Where(e => e.Value >= 3).Select(e => new Vector2(e.Key.Item1 / 2f, e.Key.Item2 / 2f)).ToList();
            models = WorldData.Models(-1e9, 1e9);
            SignsBuilder.IndexDrawnAsphalt();
            var game = Resources.Load<Magistrala.Game.GameWorld>(Magistrala.Game.GameWorld.ResourcePath);
            bays = game ? game.stops.Select(s => (s.bay, s.bayDir, s.bayHl, s.bayHw)).ToList() : new List<(Vector3, Vector2, float, float)>();
            // the props already there (World/Props: their colliders are ignored by Free, so their footprints count as placed)
            var props = GameObject.Find("World")?.transform.Find("Props");
            if (props)
                foreach (var lg in props.GetComponentsInChildren<LODGroup>(true))
                {
                    var rs = lg.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                    var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds);
                    placed.Add((new Vector2(bb.center.x, bb.center.z), new Vector2(bb.extents.x, bb.extents.z), 0));
                }
            // yard gates / house doors on the street (Yards: the parked cars keep off them too): no shelter / pole in front of one
            stGates = new List<Vector3>();
            if (File.Exists(Yards.SpotsPath))
            {
                var yj = MiniJson.O(MiniJson.Parse(File.ReadAllText(Yards.SpotsPath)));
                foreach (var key in new[] { "gates", "doors", "frontGates" })
                    if (MiniJson.L(MiniJson.G(yj, key)) is List<object> gl)
                        foreach (var o in gl) { var q = MiniJson.L(o); stGates.Add(new Vector3(MiniJson.F(q[0]), MiniJson.F(q[1]), MiniJson.F(q[2]))); }
            }
            Physics.SyncTransforms();
            root = WorldBuilder.Root("Stanice").transform;

            int nSh = 0, nPole = 0, nFallback = 0, nNone = 0;
            var perK = new Dictionary<string, (int sh, int pole)>();
            foreach (var s in stops)
            {
                double h = s.heading * Math.PI / 180;
                var tg = new Vector2((float)Math.Sin(h), (float)-Math.Cos(h));     // travel (game: x east, z south)
                var ng = new Vector2(-tg.y, tg.x);                                   // its right: the stop's side
                bool ok = false; string why = "";
                if (s.shelter && info.ContainsKey("stanica"))
                {
                    ok = Spot(s, tg, ng, "stanica", .5f, s.road == "d8" ? 7f : 3.5f, s.road == "d8" ? 30 : 20, junctions, out why);
                    if (ok) nSh++; else { log.Add($"  {s.id} {s.name}: nadstrešnici nema mjesta ({why}) – stup"); nFallback++; }
                }
                if (!ok)
                {
                    ok = Spot(s, tg, ng, "stup-stanica", .4f, s.road == "d8" ? 8f : 2.0f, s.road == "d8" ? 45 : 12, null, out why);
                    if (ok) nPole++; else { nNone++; log.Add($"  {s.id} {s.name}: ni stup ne stane ({why}) – izostavljeno"); }
                }
            }
            log.Insert(0, $"[stanice] {stops.Count} stanica: {nSh} nadstrešnica, {nPole} stupova ({nFallback} nadstrešnica bez mjesta → stup), {nNone} izostavljeno; " +
                $"{sw.ElapsedMilliseconds / 1000.0:F1} s");
            return string.Join("\n", log);
        }

        /// the nearest carriageway running with the travel direction near a game point: (projection, half width), or null
        static (Vector2 p, float hw, Vector2 dir)? Carriage(Vector2 q, Vector2 tg, float reach)
        {
            (Vector2, float, Vector2)? best = null; float bd = reach;
            for (int gx = (int)Math.Floor((q.x - reach) / 16); gx <= (int)Math.Floor((q.x + reach) / 16); gx++)
                for (int gz = (int)Math.Floor((q.y - reach) / 16); gz <= (int)Math.Floor((q.y + reach) / 16); gz++)
                {
                    if (!roadGrid.TryGetValue((gx, gz), out var l)) continue;
                    foreach (int i in l)
                    {
                        var r = roads[i]; var a = new Vector2((float)r.ax, (float)r.az); var d = new Vector2((float)(r.bx - r.ax), (float)(r.bz - r.az));
                        float L = d.magnitude; if (L < .05f) continue; var u = d / L;
                        if (Mathf.Abs(Vector2.Dot(u, tg)) < .75f) continue;
                        float t = Mathf.Clamp(Vector2.Dot(q - a, u), 0, L); var p = a + u * t;
                        // a dual carriageway: only the one the stop is on the right of (not the opposite direction's across the median)
                        var ua = Vector2.Dot(u, tg) > 0 ? u : -u;
                        if (Vector2.Dot(q - p, new Vector2(-ua.y, ua.x)) < -(float)r.hw - 1) continue;
                        // distance to the edge on the stop's side (a wide road's centreline far away still counts by its edge)
                        float dd = Mathf.Max(0, (q - p).magnitude - (float)r.hw);
                        if (dd < bd) { bd = dd; best = (p, (float)r.hw, Vector2.Dot(u, tg) > 0 ? u : -u); }
                    }
                }
            return best;
        }

        static bool Spot(Stop s, Vector2 tg, Vector2 ng, string prop, float d0, float d1, float slide, List<Vector2> junctions, out string why)
        {
            why = "nema kolnika u 40 m";
            var inf = info[prop];
            float hx = inf.size.x / 2, hz = inf.size.z / 2;   // local x along the road, z across (the open side / plate face +Z)
            var g0 = new Vector2((float)s.x, (float)s.z);
            var c0 = Carriage(g0, tg, 40); if (c0 == null) return false;
            var reasons = new Dictionary<string, int>();
            var order = new List<float> { 0 }; for (float a = 1; a <= slide; a += 1) { order.Add(a); order.Add(-a); }
            foreach (float a in order)
            {
                var c = Carriage(c0.Value.p + c0.Value.dir * a, c0.Value.dir, 12); if (c == null) continue;
                var t = c.Value.dir; var n = new Vector2(-t.y, t.x);               // right of travel
                for (float d = d0; d <= d1 + 1e-3f; d += .25f)
                {
                    var gc = c.Value.p + n * (c.Value.hw + d + hz);
                    string w = null;
                    if (s.bus.HasValue && Vector2.Distance(gc, s.bus.Value) > 8) w = "više od 8 m od BUS natpisa";
                    if (junctions != null && junctions.Any(j => (j - gc).sqrMagnitude < 100)) w = "križanje";
                    else if (s.drive.Any(q => (q - gc).magnitude < hx + .5f)) w = "kolni ulaz (Mapillary)";
                    var u = W.U(gc.x, 0, gc.y);
                    // the open side (+Z) toward the road: forward = -n in Unity
                    var fwd = W.U(gc.x - n.x, 0, gc.y - n.y) - u;
                    var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                    if (prop == "stup-stanica") rot = Quaternion.LookRotation((W.U(gc.x - t.x, 0, gc.y - t.y) - u).normalized, Vector3.up);
                    if (w == null)
                    {
                        float gx0 = prop == "stup-stanica" ? .8f : hx + 1.2f, gz0 = prop == "stup-stanica" ? 2f : hz + 3f;
                        foreach (var gp in stGates)
                        {
                            var l = Quaternion.Inverse(rot) * (gp - u); l.y = 0;
                            if (Mathf.Abs(l.x) < gx0 && Mathf.Abs(l.z) < gz0) { w = "kapija / ulaz"; break; }
                        }
                    }
                    float y = 0;
                    if (w == null && !Free(u, prop == "stup-stanica" ? .2f : hx, prop == "stup-stanica" ? .2f : hz, rot, out y, out w, false, .3f, .5f)) { }
                    if (w != null) { reasons.TryGetValue(w, out int k); reasons[w] = k + 1; continue; }
                    var g = Group($"{s.id} {s.name}".Trim());
                    Put(g, prop, new Vector3(u.x, y, u.z), rot.eulerAngles.y, 1, false, false);
                    if (prop == "stanica") placed[placed.Count - 1] = (new Vector2(u.x, u.z), new Vector2(hx, hz), rot.eulerAngles.y);
                    log.Add($"  {s.id} {s.name} ({s.road}): {prop} {d:F1} m od ruba, {a:+0;-0;0} m uzduž");
                    return true;
                }
            }
            why = string.Join(", ", reasons.OrderByDescending(x => x.Value).Take(3).Select(x => $"{x.Key} {x.Value}"));
            return false;
        }

        // ------------------------------------------------------------------ the generic stop sign on a pole
        static GameObject PolePrefab()
        {
            string dir = STANICE_GEN; TripoDir(dir);
            string pfPath = $"{dir}/stup-stanica.prefab";
            var tex = SignTexture($"{dir}/stup-stanica.png");
            var matPath = $"{dir}/stup-stanica.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", Color.white); mat.SetFloat("_Smoothness", .35f); mat.SetFloat("_Metallic", .2f);
            mat.SetFloat("_Cull", 0); mat.enableInstancing = true; EditorUtility.SetDirty(mat);
            var meshPath = $"{dir}/stup-stanica.asset";
            var mesh = PoleMesh();
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (old) { old.Clear(); EditorUtility.CopySerialized(mesh, old); UnityEngine.Object.DestroyImmediate(mesh); mesh = old; EditorUtility.SetDirty(old); }
            else AssetDatabase.CreateAsset(mesh, meshPath);
            var go = new GameObject("stup-stanica");
            var lod0 = new GameObject("lod0"); lod0.transform.SetParent(go.transform, false);
            lod0.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = lod0.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.On;
            var lg = go.AddComponent<LODGroup>(); lg.SetLODs(new[] { new LOD(.012f, new Renderer[] { mr }) }); lg.RecalculateBounds();
            var cc = go.AddComponent<CapsuleCollider>(); cc.radius = .05f; cc.height = 2.75f; cc.center = new Vector3(0, 1.375f, 0); cc.direction = 1;
            PrefabUtility.SaveAsPrefabAsset(go, pfPath); UnityEngine.Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<GameObject>(pfPath);
        }

        static void TripoDir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = Path.GetDirectoryName(p).Replace('\\', '/'); TripoDir(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(p)); } }

        /// 128 x 160 atlas: the plate (blue, white border, white bus) in the top 128 x 128, the galvanised grey below (pole, plate back)
        static Texture2D SignTexture(string path)
        {
            const int W0 = 128, H0 = 160;
            var t = new Texture2D(W0, H0, TextureFormat.RGBA32, true);
            Color blue = new Color(.0f, .30f, .62f), white = new Color(.95f, .95f, .95f), grey = new Color(.62f, .64f, .66f);
            var px = new Color[W0 * H0];
            for (int y = 0; y < H0; y++)
                for (int x = 0; x < W0; x++)
                {
                    Color c;
                    if (y < 32) c = grey * (0.96f + .04f * Mathf.PerlinNoise(x * .3f, y * .3f));
                    else
                    {
                        int sx = x, sy = y - 32;                  // 0..127 on the plate, y up
                        bool border = sx < 5 || sx > 122 || sy < 5 || sy > 122;
                        bool rim = sx < 8 || sx > 119 || sy < 8 || sy > 119;
                        c = border ? white : rim ? blue : blue;
                        // the bus: body 22..106 x 34..96 (rounded), windows band, wheels
                        bool body = sx >= 22 && sx <= 106 && sy >= 36 && sy <= 96 && !(Corner(sx, sy, 22, 106, 36, 96, 7));
                        bool win = sy >= 64 && sy <= 88 && sx >= 30 && sx <= 98 && ((sx - 30) % 17) > 2;
                        bool door = sx >= 30 && sx <= 40 && sy >= 44 && sy <= 62;
                        bool wheel = (new Vector2(sx - 40, sy - 36).sqrMagnitude < 90) || (new Vector2(sx - 88, sy - 36).sqrMagnitude < 90);
                        bool hub = (new Vector2(sx - 40, sy - 36).sqrMagnitude < 12) || (new Vector2(sx - 88, sy - 36).sqrMagnitude < 12);
                        if (body) c = white;
                        if (body && (win || door)) c = blue;
                        if (wheel) c = hub ? white : blue;
                        if (wheel && !hub && (new Vector2(sx - 40, sy - 36).sqrMagnitude < 132 || new Vector2(sx - 88, sy - 36).sqrMagnitude < 132)) c = blue;
                    }
                    px[y * W0 + x] = c;
                }
            // the wheels sit under the body: a white ring around them so they read on the blue
            for (int y = 32; y < H0; y++)
                for (int x = 0; x < W0; x++)
                {
                    int sx = x, sy = y - 32;
                    foreach (var wc in new[] { new Vector2(40, 36), new Vector2(88, 36) })
                    {
                        float r2 = new Vector2(sx - wc.x, sy - wc.y).sqrMagnitude;
                        if (r2 >= 90 && r2 < 132) px[y * W0 + x] = white;
                    }
                }
            t.SetPixels(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path); ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static bool Corner(int x, int y, int x0, int x1, int y0, int y1, int r)
        {
            int cx = x < x0 + r ? x0 + r : x > x1 - r ? x1 - r : x, cy = y < y0 + r ? y0 + r : y > y1 - r ? y1 - r : y;
            return (x - cx) * (x - cx) + (y - cy) * (y - cy) > r * r;
        }

        /// pole (12 sides, r 3.5 cm, 2.75 m) + plate 0.6 x 0.6 x 2 cm centred 2.35 m up, its face +Z (pivot: the pole's foot)
        static Mesh PoleMesh()
        {
            var V = new List<Vector3>(); var N = new List<Vector3>(); var UV = new List<Vector2>(); var I = new List<int>();
            Vector2 greyUv = new Vector2(.5f, 16f / 160);
            const int S = 12; const float R = .035f, H = 2.75f;
            for (int i = 0; i <= S; i++)
            {
                float a = i * Mathf.PI * 2 / S; var n = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                V.Add(n * R); V.Add(n * R + Vector3.up * H); N.Add(n); N.Add(n); UV.Add(greyUv); UV.Add(greyUv);
            }
            for (int i = 0; i < S; i++) { int k = i * 2; I.AddRange(new[] { k, k + 1, k + 2, k + 1, k + 3, k + 2 }); }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
            {   // a b c d counter-clockwise seen from n's side -> Unity's clockwise front faces: a, c, b / a, d, c
                int k = V.Count; V.AddRange(new[] { a, b, c, d }); for (int j = 0; j < 4; j++) N.Add(n); UV.AddRange(new[] { ua, ub, uc, ud });
                I.AddRange(new[] { k, k + 2, k + 1, k, k + 3, k + 2 });
            }
            const float hw = .3f, y0 = 2.05f, y1 = 2.65f, zf = .045f, zb = .025f;
            float v0 = 32f / 160, v1 = 1;
            // front (+Z): seen from +Z, x grows to the left
            Quad(new Vector3(hw, y0, zf), new Vector3(-hw, y0, zf), new Vector3(-hw, y1, zf), new Vector3(hw, y1, zf), Vector3.forward,
                 new Vector2(0, v0), new Vector2(1, v0), new Vector2(1, v1), new Vector2(0, v1));
            // back (-Z), grey
            Quad(new Vector3(-hw, y0, zb), new Vector3(hw, y0, zb), new Vector3(hw, y1, zb), new Vector3(-hw, y1, zb), Vector3.back, greyUv, greyUv, greyUv, greyUv);
            // rims
            Quad(new Vector3(-hw, y1, zb), new Vector3(hw, y1, zb), new Vector3(hw, y1, zf), new Vector3(-hw, y1, zf), Vector3.up, greyUv, greyUv, greyUv, greyUv);
            Quad(new Vector3(-hw, y0, zf), new Vector3(hw, y0, zf), new Vector3(hw, y0, zb), new Vector3(-hw, y0, zb), Vector3.down, greyUv, greyUv, greyUv, greyUv);
            Quad(new Vector3(hw, y0, zb), new Vector3(hw, y0, zf), new Vector3(hw, y1, zf), new Vector3(hw, y1, zb), Vector3.right, greyUv, greyUv, greyUv, greyUv);
            Quad(new Vector3(-hw, y0, zf), new Vector3(-hw, y0, zb), new Vector3(-hw, y1, zb), new Vector3(-hw, y1, zf), Vector3.left, greyUv, greyUv, greyUv, greyUv);
            var m = new Mesh { name = "stup-stanica" };
            m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetTriangles(I, 0); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }
    }
}
