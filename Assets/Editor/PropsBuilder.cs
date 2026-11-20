// Magistrala/Rekviziti: postavi — Tripo props (TripoPropImporter prefabs) placed in the scene, World/Props, deterministic: the same
// data gives the same props (no random numbers, variants by a hash of the stop id).
//   delivery points  (Resources/MagistralaGame: stops) against the business's wall nearest to the truck's bay: rays from the bay
//                    find the walls of World/Buildings; the layout of the stop's kind (cafés / konobe: crates + kegs by the door;
//                    shops: pallets with goods; bakeries: pallets + boxes) runs along that wall, the door itself (u 0) kept free
//   special sites    the firm's yard in Lukšić (pallets, boxes, trolley, pallet truck in its north-west corner; the bay and the
//                    entrance from the east stay free), Burin drinks wholesale (stacks of crates / water / pallets / kegs along the
//                    hall's south wall, the doorway free)
//   rivas            white benches facing the sea on the riva slots of coast.json (the web game's riva furniture, OSM rivas), every
//                    ~30 m, Kaštela only
//   landmarks        the horseman "Vjesnik slobode" on its column (OSM tourism=artwork, 43.54934 N 16.34159 E, next to Tommy "kod
//                    konjanika"), the 1500-year-old olive of Kaštel Štafilić (OSM natural=tree "Stara Maslina", 43.55056 N 16.33565 E,
//                    height 10 m): OSM lon / lat -> game by the region's 1:1 azimuthal-equidistant frame (centre Split 43.5083 / 16.44)
//   petrol stations  Tripo dispensers on the package canopies' islands (BuildingsBuilder.TripoPumps: pump-spots.json)
// Every prop is checked: ground below (not a roof, not the sea, flat), not on a carriageway (StreetsBuilder.Carriageways + the models'
// asphalt), not in any stop's bay (+ margin), not in a building, not overlapping another prop. Rejected items are counted in the log.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Magistrala.Game;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class PropsBuilder
    {
        // ------------------------------------------------------------------ layouts
        /// u: along the wall (m, right of the wall's normal), d: the item's back from the wall (m), yaw: deg (0 = front away from the
        /// wall), n: stacked copies
        struct It { public string p; public float u, d, yaw; public int n; public It(string p, float u, float d = .25f, float yaw = 0, int n = 1) { this.p = p; this.u = u; this.d = d; this.yaw = yaw; this.n = n; } }

        static readonly It[][] Cafe = {
            new[] { new It("gajbe", 1.3f), new It("gajbe", 1.75f), new It("bacva-pivo", 2.3f), new It("bacva-pivo", 2.8f) },
            new[] { new It("gajbe-hrpa", 2.1f), new It("bacva-pivo", 3.3f), new It("bacva-pivo", 3.3f, .8f) },
            new[] { new It("gajbe", 1.3f), new It("gajbe", 1.3f, .7f, 10), new It("bacva-pivo", 1.95f), new It("gajbe", 2.5f, .25f, -6) },
        };
        static readonly It[][] Shop = {
            new[] { new It("paleta-kutije", 2.2f), new It("voda-paleta", 3.6f), new It("paleta", 4.9f, .25f, 0, 4) },
            new[] { new It("voda-paleta", 2.1f), new It("paleta-kutije-puna", 3.4f, .25f, 90), new It("paleta-folija", 4.8f) },
            new[] { new It("paleta-kutije-puna", 2.0f, .25f, 90), new It("paleta-kutije", 3.4f), new It("paleta", 4.7f, .25f, 0, 3) },
        };
        static readonly It[][] Bakery = {
            new[] { new It("paleta", 1.8f, .25f, 0, 3), new It("kutija", 2.9f, .3f, 8, 2), new It("kutija", 3.5f, .3f, -5) },
        };
        static readonly It[] Wholesale = {
            new It("gajbe-hrpa", -9.4f), new It("gajbe-hrpa", -7.7f), new It("gajbe-hrpa", -6.0f), new It("gajbe-hrpa", -8.5f, 1.45f, 4),
            new It("voda-paleta", -4.1f), new It("voda-paleta", -2.7f), new It("voda-paleta", -3.4f, 1.4f),
            new It("paleta", 2.4f, .25f, 0, 6), new It("paleta", 3.8f, .25f, 0, 4), new It("paleta", 3.1f, 1.2f, 15, 2),
            new It("bacva-pivo", 5.0f), new It("bacva-pivo", 5.5f), new It("bacva-pivo", 6.0f), new It("bacva-pivo", 5.25f, .75f), new It("bacva-pivo", 5.75f, .75f),
            new It("gajbe", 7.0f), new It("gajbe", 7.45f), new It("gajbe", 7.9f), new It("gajbe", 7.2f, .7f, 12),
            new It("romobil", .2f, 3.2f, 100), new It("kolica-rucna", 9.3f, .3f, 90),
            new It("gajbe-hrpa", -11.3f), new It("gajbe-hrpa", -13.0f), new It("gajbe-hrpa", -12.1f, 1.45f, -5),
            new It("voda-paleta", -14.7f), new It("voda-paleta", -16.1f), new It("paleta-kutije", -17.6f),
            new It("gajbe", 10.8f), new It("gajbe", 11.25f), new It("gajbe", 11.7f), new It("gajbe", 12.15f), new It("gajbe", 11.0f, .7f, -8),
            new It("bacva-pivo", 12.9f), new It("bacva-pivo", 13.4f), new It("bacva-pivo", 13.15f, .75f),
            new It("paleta", 15.0f, .25f, 0, 7), new It("paleta", 16.4f, .25f, 0, 5),
        };
        /// the firm's yard (kastela_skladiste): Unity x / z, yaw (deg, 0 = front north), n
        static readonly (string p, float x, float z, float yaw, int n)[] Yard = {
            ("paleta", -2519.0f, 239.6f, 0, 5), ("paleta", -2517.6f, 239.6f, 3, 3), ("paleta-kutije", -2515.9f, 239.4f, 180, 1),
            ("paleta-kutije-puna", -2514.1f, 239.3f, 90, 1), ("voda-paleta", -2512.4f, 239.4f, 180, 1), ("kutija", -2510.7f, 240.2f, 10, 2),
            ("kutija", -2510.4f, 239.4f, -12, 1), ("paleta-folija", -2519.6f, 236.9f, 90, 1), ("kolica-rucna", -2519.5f, 234.8f, 0, 1),
            ("romobil", -2518.4f, 232.8f, 70, 1), ("kutija", -2519.8f, 231.5f, 30, 1),
        };
        // OSM -> game (see the header)
        static readonly (string p, double lat, double lon, float yaw, string what)[] Landmarks = {
            ("konjanik", 43.5493418, 16.3415904, 90, "Vjesnik slobode (konjanik), Kaštel Novi / Stari"),
            ("stara-maslina", 43.5505621, 16.3356451, 20, "Stara maslina, Kaštel Štafilić"),
        };

        // ------------------------------------------------------------------ state
        static List<(double ax, double az, double bx, double bz, double hw)> roads;
        static Dictionary<(int, int), List<int>> roadGrid;
        static List<ModelJ> models;
        static List<(Vector3 c, Vector2 dir, float hl, float hw)> bays;
        static List<(Vector2 c, Vector2 h, float yaw)> placed;
        static Transform root;
        static int nPlaced, nRejected; static Dictionary<string, int> perProp; static List<string> log;
        static Dictionary<string, (Vector3 size, GameObject pf)> info;

        [MenuItem("Magistrala/Rekviziti: postavi")]
        public static string BuildMenu() { var s = Build(); Debug.Log(s); return s; }

        public static string Build()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            log = new List<string>(); perProp = new Dictionary<string, int>(); placed = new List<(Vector2, Vector2, float)>(); nPlaced = nRejected = 0;
            info = new Dictionary<string, (Vector3, GameObject)>();
            foreach (var f in Directory.GetFiles(TripoPropImporter.SRC, "*.json"))
            {
                var n = Path.GetFileNameWithoutExtension(f); var pf = TripoPropImporter.Prefab(n); if (!pf) continue;
                var b = new Bounds(); bool first = true;
                foreach (var mf in pf.GetComponentsInChildren<MeshFilter>()) { if (!mf.name.StartsWith("lod0")) continue; var mb = mf.sharedMesh.bounds; if (first) b = mb; else b.Encapsulate(mb); first = false; }
                info[n] = (b.size, pf);
            }
            roads = StreetsBuilder.Carriageways(); roadGrid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < roads.Count; i++)
            {
                var r = roads[i]; double pad = r.hw + 4;
                for (int gx = (int)Math.Floor((Math.Min(r.ax, r.bx) - pad) / 16); gx <= (int)Math.Floor((Math.Max(r.ax, r.bx) + pad) / 16); gx++)
                    for (int gz = (int)Math.Floor((Math.Min(r.az, r.bz) - pad) / 16); gz <= (int)Math.Floor((Math.Max(r.az, r.bz) + pad) / 16); gz++)
                    { if (!roadGrid.TryGetValue((gx, gz), out var l)) roadGrid[(gx, gz)] = l = new List<int>(); l.Add(i); }
            }
            models = WorldData.Models(-1e9, 1e9);
            var game = Resources.Load<GameWorld>(GameWorld.ResourcePath);
            bays = game.stops.Select(s => (s.bay, s.bayDir, s.bayHl, s.bayHw)).ToList();
            root = WorldBuilder.Root("Props").transform;

            // special sites first (they own their space), then the landmarks, pumps, delivery points, benches
            var yard = game.Stop(MagistralaGame.Warehouse);
            if (yard != null) { var g = Group("skladiste-luksic"); foreach (var y in Yard) Put(g, y.p, new Vector3(y.x, 0, y.z), y.yaw, y.n, true); }
            foreach (var l in Landmarks) Landmark(l.p, l.lat, l.lon, l.yaw, l.what);
            Pumps();
            foreach (var s in game.stops)
            {
                It[] lay = null; string k = s.kind ?? "";
                int h = Hash(s.id);
                if (s.id == "p84") { Wall(s, Wholesale, "veleprodaja " + s.name, 60, true); continue; }
                if (k == "kafić" || k == "restoran" || k == "kafana") lay = Cafe[h % Cafe.Length];
                else if (k == "trgovina" || k == "trgovački centar" || k == "shop") lay = Shop[h % Shop.Length];
                else if (k == "pekara") lay = Bakery[h % Bakery.Length];
                if (lay == null) continue;
                // the door's other side on every other stop
                if ((h / 7) % 2 == 1) lay = lay.Select(x => { x.u = -x.u; return x; }).ToArray();
                Wall(s, lay, s.name, 32, false);
            }
            Benches();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            int rend = root.GetComponentsInChildren<MeshRenderer>(true).Length, lg = root.GetComponentsInChildren<LODGroup>(true).Length, cols = root.GetComponentsInChildren<Collider>(true).Length;
            log.Insert(0, $"[rekviziti] {nPlaced} props placed ({lg} LOD groups, {rend} renderers, {cols} colliders), {nRejected} rejected; " +
                string.Join(", ", perProp.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}")) + $"; {sw.ElapsedMilliseconds / 1000.0:F1} s");
            return string.Join("\n", log);
        }

        static int Hash(string s) { unchecked { int h = 23; foreach (char c in s) h = h * 31 + c; return h & 0x7fffffff; } }
        static Transform Group(string name) { var g = new GameObject(name); g.transform.SetParent(root, false); g.isStatic = true; return g.transform; }

        // ------------------------------------------------------------------ checks
        static bool HasAncestor(Transform t, string name) { for (var p = t; p != null; p = p.parent) if (p.name == name && p.parent && p.parent.name == "World") return true; return false; }
        static bool Ignored(Collider c) => HasAncestor(c.transform, "Props") || c.GetComponentInParent<TruckController>() || c.attachedRigidbody;

        /// ground under a point: the first hit from above that isn't a prop / vehicle; NaN when that is a building (a roof) or nothing
        static float GroundY(float x, float z, out bool building)
        {
            building = false;
            var hits = Physics.RaycastAll(new Vector3(x, 400, z), Vector3.down, 900, ~0, QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (Ignored(h.collider)) continue;
                if (HasAncestor(h.collider.transform, "Buildings")) { building = true; return float.NaN; }
                // the guardrails / barriers / package furniture boxes are not ground
                if (h.collider is BoxCollider) { building = true; return float.NaN; }
                return h.point.y;
            }
            return float.NaN;
        }

        static bool OnRoad(Vector3 u, float margin)
        {
            var g = W.G(u); double gx = g.x, gz = g.y;
            if (roadGrid.TryGetValue(((int)Math.Floor(gx / 16), (int)Math.Floor(gz / 16)), out var l))
                foreach (int i in l)
                {
                    var r = roads[i]; double bx = r.bx - r.ax, bz = r.bz - r.az, l2 = bx * bx + bz * bz;
                    double t = l2 < 1e-9 ? 0 : Math.Max(0, Math.Min(1, ((gx - r.ax) * bx + (gz - r.az) * bz) / l2));
                    double dx = gx - r.ax - bx * t, dz = gz - r.az - bz * t;
                    if (dx * dx + dz * dz < (r.hw + margin) * (r.hw + margin)) return true;
                }
            foreach (var m in models) if (!double.IsNaN(m.Surface(gx, gz))) return true;
            return false;
        }

        static bool InBay(Vector3 p, float margin)
        {
            foreach (var b in bays)
            {
                var d = new Vector2(p.x - b.c.x, p.z - b.c.z); var f = b.dir.sqrMagnitude > 0 ? b.dir.normalized : Vector2.up;
                float along = Mathf.Abs(Vector2.Dot(d, f)), side = Mathf.Abs(d.x * f.y - d.y * f.x);
                if (along < b.hl + margin + 2 && side < b.hw + margin) return true;
            }
            return false;
        }

        /// can a footprint (half extents hx / hz, rotation) stand here; y = its ground height
        static bool Free(Vector3 c, float hx, float hz, Quaternion rot, out float y, out string why, bool yard = false, float minY = .3f, float maxSlope = .35f)
        {
            y = float.NaN; why = null;
            var ys = new List<float>();
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    var p = c + rot * new Vector3(i * hx, 0, j * hz);
                    float gy = GroundY(p.x, p.z, out bool bld);
                    if (bld) { why = "zgrada / ograda"; return false; }
                    if (float.IsNaN(gy)) { why = "nema tla"; return false; }
                    if (gy < minY) { why = "more"; return false; }
                    if (OnRoad(p, .35f)) { why = "kolnik"; return false; }
                    ys.Add(gy);
                }
            ys.Sort(); if (ys[ys.Count - 1] - ys[0] > maxSlope) { why = "nagib"; return false; }
            y = maxSlope > .5f ? ys[ys.Count - 1] : ys[ys.Count / 2]; // (a plinth on rough ground: its top above the highest point)
            if (!yard && InBay(c, 1.2f)) { why = "mjesto istovara"; return false; }
            var c2 = new Vector2(c.x, c.z); float yw = rot.eulerAngles.y;
            foreach (var q in placed) if (Overlap(c2, new Vector2(hx, hz), yw, q.c, q.h, q.yaw, .03f)) { why = "preklapanje"; return false; }
            // walls / fences / furniture boxes in the volume
            foreach (var col in Physics.OverlapBox(new Vector3(c.x, y + .9f, c.z), new Vector3(hx * .9f, .7f, hz * .9f), rot, ~0, QueryTriggerInteraction.Ignore))
                if (!Ignored(col) && (HasAncestor(col.transform, "Buildings") || col is BoxCollider)) { why = "zgrada (volumen)"; return false; }
            return true;
        }

        /// two footprints (centre, half extents, yaw deg) closer than gap (separating axes)
        static bool Overlap(Vector2 c1, Vector2 h1, float y1, Vector2 c2, Vector2 h2, float y2, float gap)
        {
            Vector2 Ax(float yaw, bool z) { float a = yaw * Mathf.Deg2Rad; return z ? new Vector2(Mathf.Sin(a), Mathf.Cos(a)) : new Vector2(Mathf.Cos(a), -Mathf.Sin(a)); }
            Vector2 u1 = Ax(y1, false), v1 = Ax(y1, true), u2 = Ax(y2, false), v2 = Ax(y2, true), d = c2 - c1;
            foreach (var a in new[] { u1, v1, u2, v2 })
            {
                float r1 = h1.x * Mathf.Abs(Vector2.Dot(a, u1)) + h1.y * Mathf.Abs(Vector2.Dot(a, v1));
                float r2 = h2.x * Mathf.Abs(Vector2.Dot(a, u2)) + h2.y * Mathf.Abs(Vector2.Dot(a, v2));
                if (Mathf.Abs(Vector2.Dot(d, a)) > r1 + r2 + gap) return false;
            }
            return true;
        }

        /// place one prop (n stacked); p = position (y from the ground), yaw = deg about y; check = run Free
        static bool Put(Transform parent, string prop, Vector3 p, float yaw, int n, bool yard, bool check = true)
        {
            if (!info.TryGetValue(prop, out var inf)) { log.Add($"  nema prefaba {prop}"); return false; }
            var rot = Quaternion.Euler(0, yaw, 0);
            float y = p.y;
            if (check)
            {
                if (!Free(p, inf.size.x / 2, inf.size.z / 2, rot, out y, out string why, yard)) { nRejected++; log.Add($"  odbijeno {prop} @ {p.x:F1},{p.z:F1} ({parent.name}): {why}"); return false; }
            }
            for (int k = 0; k < n; k++)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(inf.pf, parent);
                // a stack: each copy a little turned (hash), never perfectly aligned
                float wob = n > 1 ? ((Hash(parent.name + p.x.ToString("F1") + k) % 9) - 4) * 1.1f : 0;
                go.transform.SetPositionAndRotation(new Vector3(p.x, y + k * inf.size.y * .985f, p.z), rot * Quaternion.Euler(0, wob, 0));
                go.name = prop; go.isStatic = false;
                nPlaced++; perProp.TryGetValue(prop, out int c); perProp[prop] = c + 1;
            }
            placed.Add((new Vector2(p.x, p.z), new Vector2(inf.size.x / 2, inf.size.z / 2), yaw));
            return true;
        }

        // ------------------------------------------------------------------ against a wall
        static void Wall(GStop s, It[] lay, string name, float reach, bool all)
        {
            // walls seen from the bay (rays at 1.2 m), facing it; the business's own building preferred (near s.pos)
            var cands = new List<(Vector3 p, Vector3 n, float score)>();
            var o = s.bay + Vector3.up * 1.2f;
            for (int a = 0; a < 120; a++)
            {
                float ang = a * 3 * Mathf.Deg2Rad; var dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                var hits = Physics.RaycastAll(o, dir, reach, ~0, QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (x, y) => x.distance.CompareTo(y.distance));
                foreach (var h in hits)
                {
                    if (Ignored(h.collider)) continue;
                    if (!HasAncestor(h.collider.transform, "Buildings")) break;
                    var n = new Vector3(h.normal.x, 0, h.normal.z); if (n.sqrMagnitude < .5f) break; n.Normalize();
                    if (Vector3.Dot(n, -dir) < .35f) break;
                    float dPos = new Vector2(h.point.x - s.pos.x, h.point.z - s.pos.z).magnitude;
                    cands.Add((h.point, n, h.distance + .8f * dPos));
                    break;
                }
            }
            if (cands.Count == 0) { log.Add($"  {name}: nema zida u {reach:0} m od mjesta istovara"); return; }
            cands.Sort((a, b) => a.score.CompareTo(b.score));
            Transform g = null; int best = -1, tried = 0;
            foreach (var c in cands)
            {
                if (tried++ > 12) break;
                // dry run: how many items fit here
                int ok = 0;
                var t = Vector3.Cross(Vector3.up, c.n);
                foreach (var it in lay)
                {
                    if (!info.TryGetValue(it.p, out var inf)) continue;
                    var rot = Quaternion.LookRotation(c.n) * Quaternion.Euler(0, it.yaw, 0);
                    float depth = Mathf.Abs(Mathf.Cos(it.yaw * Mathf.Deg2Rad)) * inf.size.z + Mathf.Abs(Mathf.Sin(it.yaw * Mathf.Deg2Rad)) * inf.size.x;
                    var p = c.p + t * it.u + c.n * (it.d + depth / 2);
                    if (Free(p, inf.size.x / 2, inf.size.z / 2, rot, out _, out _)) ok++;
                }
                if (ok >= Mathf.CeilToInt(lay.Length * (all ? .75f : .6f))) { best = cands.IndexOf(c); break; }
            }
            if (best < 0) { log.Add($"  {name}: uz zid nema mjesta (pokušano {Mathf.Min(tried, cands.Count)} zidova)"); return; }
            var w = cands[best]; var tt = Vector3.Cross(Vector3.up, w.n);
            g = Group(name);
            int put = 0;
            foreach (var it in lay)
            {
                if (!info.TryGetValue(it.p, out var inf)) continue;
                float depth = Mathf.Abs(Mathf.Cos(it.yaw * Mathf.Deg2Rad)) * inf.size.z + Mathf.Abs(Mathf.Sin(it.yaw * Mathf.Deg2Rad)) * inf.size.x;
                var p = w.p + tt * it.u + w.n * (it.d + depth / 2);
                float yaw = Quaternion.LookRotation(w.n).eulerAngles.y + it.yaw;
                if (Put(g, it.p, p, yaw, it.n, false)) put++;
            }
            log.Add($"  {name}: {put}/{lay.Length} uz zid {new Vector2(w.p.x, w.p.z)} ({Vector3.Distance(w.p, s.bay):F0} m od mjesta istovara)");
        }

        // ------------------------------------------------------------------ landmarks
        public static Vector2 GameFromLatLon(double lat, double lon)
        {
            const double a = 6378137.0, f = 1 / 298.257223563; double e2 = f * (2 - f);
            double lat0 = 43.5083 * Math.PI / 180, lon0 = 16.44 * Math.PI / 180, la = lat * Math.PI / 180, lo = lon * Math.PI / 180;
            double phim = (la + lat0) / 2, s2 = Math.Sin(phim) * Math.Sin(phim);
            double M = a * (1 - e2) / Math.Pow(1 - e2 * s2, 1.5), N = a / Math.Sqrt(1 - e2 * s2);
            return new Vector2((float)((lo - lon0) * N * Math.Cos(phim)), (float)(-(la - lat0) * M));
        }

        static void Landmark(string prop, double lat, double lon, float yaw, string what)
        {
            if (!info.TryGetValue(prop, out var inf)) return;
            var g2 = GameFromLatLon(lat, lon);
            // (the riva's survey of the spot on the orthophoto, when it has one: the horseman's plinth on the little mole at the sea end of Grgura Ninskog)
            if (RivaBuilder.Landmark(prop) is Vector2 lm) g2 = lm;
            var u = W.U(g2.x, 0, g2.y);
            // the nearest spot within 12 m where its base stands on land, off the road and out of buildings (the OSM point may be in
            // our sea where the coast differs)
            float hx = prop == "konjanik" ? 1.5f : .9f, hz = hx;
            Vector3 best = Vector3.zero; float bd = 1e9f, by = 0;
            for (float dx = -12; dx <= 12; dx += .5f)
                for (float dz = -12; dz <= 12; dz += .5f)
                {
                    float d = dx * dx + dz * dz; if (d >= bd || d > 144) continue;
                    var p = new Vector3(u.x + dx, 0, u.z + dz);
                    // (the horseman's plinth reaches 2.5 m down: it may stand on the rocks at the water like the real one)
                    bool col = prop == "konjanik";
                    if (Free(p, hx, hz, Quaternion.identity, out float y, out _, false, col ? .05f : .3f, col ? 1.2f : .35f)) { bd = d; best = p; by = y; }
                }
            if (bd > 1e8f) { log.Add($"  {what}: nema slobodnog tla u 12 m od OSM točke (game {g2.x:F1}, {g2.y:F1}) — nije postavljeno"); return; }
            var g = Group(prop);
            Put(g, prop, new Vector3(best.x, by, best.z), yaw, 1, false, false);
            log.Add($"  {what}: game {g2.x:F1}, {g2.y:F1} → Unity {best.x:F1}, {by:F1}, {best.z:F1} ({Mathf.Sqrt(bd):F1} m od OSM točke)");
        }

        // ------------------------------------------------------------------ petrol stations
        static void Pumps()
        {
            if (!BuildingsBuilder.TripoPumps || !File.Exists(BuildingsBuilder.PumpSpotsPath)) { log.Add("  benzinske: nema pump-spots.json (Magistrala/Build tile buildings)"); return; }
            var j = MiniJson.O(MiniJson.Parse(File.ReadAllText(BuildingsBuilder.PumpSpotsPath)));
            Transform g = null; int n = 0;
            foreach (var so in MiniJson.L(j["spots"]))
            {
                var s = MiniJson.O(so);
                var p = W.U(MiniJson.D(s["x"]), MiniJson.D(s["y"]), MiniJson.D(s["z"]));
                var a = new Vector3((float)MiniJson.D(s["ax"]), 0, -(float)MiniJson.D(s["az"])).normalized;
                if (g == null) g = Group("benzinske");
                // the dispenser's long side (local x) along the island, its faces to the lanes either side
                var rot = Quaternion.LookRotation(Vector3.Cross(a, Vector3.up), Vector3.up);
                // two per island either side of the canopy column (as the boxes BuildingsBuilder drew: ±1.4 m along it)
                foreach (float d in new[] { -1.4f, 1.4f })
                {
                    // a job's fuelling spot stays free (as the web game's 1d26e6a: no dispenser in the truck's place)
                    if (InBay(p + a * d, .6f)) { log.Add($"  benzinske: aparat {MiniJson.G(s, "id")} @ {(p + a * d).x:F1},{(p + a * d).z:F1} izostavljen – u mjestu posla"); continue; }
                    Put(g, "aparat-gorivo", p + a * d, rot.eulerAngles.y, 1, true, false); n++;
                }
            }
            log.Add($"  benzinske: {n} aparata na otocima nadstrešnica paketa");
        }

        // ------------------------------------------------------------------ riva benches
        static void Benches()
        {
            var path = W.REGION + "coast.json"; if (!File.Exists(path)) return;
            var j = MiniJson.O(MiniJson.Parse(File.ReadAllText(path)));
            double T = MiniJson.D(j["tileSize"]);
            var slots = new List<(double x, double z, double yaw)>();
            foreach (var kv in MiniJson.O(j["tiles"]))
            {
                var k = kv.Key.Split(','); int tx = int.Parse(k[0]), tz = int.Parse(k[1]);
                var t = MiniJson.O(kv.Value);
                foreach (var po in MiniJson.L(t["props"]))
                {
                    var q = MiniJson.L(po); var kind = (string)q[0]; if (kind == "boat" || kind == "yacht" || kind == "sail") continue;
                    slots.Add((tx * T + MiniJson.D(q[1]), tz * T + MiniJson.D(q[2]), MiniJson.D(q[3])));
                }
            }
            // Kaštela only (the streets' area), west to east
            slots = slots.Where(s => s.x >= StreetsBuilder.AX0 && s.x < StreetsBuilder.AX1 && s.z >= StreetsBuilder.AZ0 && s.z < StreetsBuilder.AZ1).OrderBy(s => s.x).ToList();
            var g = Group("riva-klupe"); var benches = new List<Vector2>(); int tried = 0;
            if (!info.TryGetValue("klupa-bijela", out var inf)) return;
            foreach (var s in slots)
            {
                var u = W.U(s.x, 0, s.z); var c2 = new Vector2(u.x, u.z);
                if (benches.Any(b => (b - c2).magnitude < 30)) continue;
                tried++;
                // along the riva (coast_features.py: yaw = atan2(dx, dz) in the game frame) and the sea side of it
                var along = new Vector3((float)Math.Sin(s.yaw), 0, -(float)Math.Cos(s.yaw));
                var nrm = new Vector3(-along.z, 0, along.x);
                float Side(float sg) { float m = 99; foreach (float d in new[] { 4f, 7f, 10f }) { var p = u + nrm * sg * d; float y = GroundY(p.x, p.z, out bool b); m = Mathf.Min(m, float.IsNaN(y) ? 99 : y); } return m; }
                var sea = Side(1) < Side(-1) ? nrm : -nrm;
                var rot = Quaternion.LookRotation(sea);
                bool ok = false;
                foreach (float off in new[] { 0f, .6f, 1.2f, -.6f, 1.8f, 2.6f, 3.4f })
                {
                    var p = u - sea * off;
                    var front = p + sea * 1.6f; float fy = GroundY(front.x, front.z, out bool fb);
                    if (fb || float.IsNaN(fy) || fy < .4f) continue;   // not at the water's edge
                    if (OnRoad(p + sea * -1.2f, .2f)) continue;         // its back not on the kerb
                    if (!Free(p, inf.size.x / 2 + .15f, inf.size.z / 2 + .3f, rot, out float y, out _)) continue;
                    Put(g, "klupa-bijela", new Vector3(p.x, y, p.z), rot.eulerAngles.y, 1, false, false);
                    benches.Add(new Vector2(p.x, p.z)); ok = true; break;
                }
                if (!ok) nRejected++;
            }
            log.Add($"  riva: {benches.Count} klupa ({tried} mjesta pokušano, {slots.Count} mjesta opreme rive u Kaštelima)");
        }
    }
}
