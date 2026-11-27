// Magistrala/Detalji: postavi — the everyday street furniture of Kaštela (World/Props/Detalji), run LAST (after "Rekviziti: postavi",
// which rebuilds World/Props, and after "Autobusne stanice: postavi").
// Data: Assets/Editor/detalji-mapillary.json = BalkanDriver tools/world/mapillary/detalji-all.json (s9_detalji.py: Mapillary map features,
// CC BY-SA 4.0, Mapillary contributors; OpenStreetMap contributors, ODbL); detalji-izvjestaj.md there has the survey and the counts.
//   stupovi    utility poles (object--support--utility-pole merged within 3.5 m): concrete (tapered grey, 9 m) or wooden (8.5 m); a
//              street lamp within 3 m (StreetLights already turned many of these poles into concrete lamp poles) is the pole itself
//   žice       overhead spans between neighbouring poles along the street (12–48 m): the power bundle (7.2 m) and a telecom cable
//              (1.3 m lower); one unit catenary mesh, each span an instance scaled (1, sag, length); no span through a building / hill
//   kontejneri groups of 1100 l waste containers (OSM waste_disposal / recycling, Mapillary trash cans away from benches): grey, a
//              blue + yellow pair where OSM says recycling; litter bins on a post by benches / stops
//   ormarići   electricity / telecom cabinets (object--junction-box), hidranti: red overground fire hydrants (Mapillary + OSM)
// Every item: on the pavement / verge beside the nearest carriageway (pushed off the asphalt when the survey point is on it), slid up
// to ±4 m along / 1.2 m out until PropsBuilder.Free (ground, not the sea, not on a carriageway / model asphalt / drawn asphalt, not
// in a delivery bay, no building / wall, no prop) and off the yard gates / house doors (Yards.SpotsPath) and the lamp poles.
// Cheap: procedural meshes (Assets/Generated/Props/Detalji), ONE material (a 64 px colour atlas, GPU instancing), every item a
// LODGroup root under World/Props -> CellBake turns them into instanced cell props (no GameObjects in the build), culled at
// ~100–220 m; wires cast no shadow.
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
        public const string DETALJI = "Assets/Editor/detalji-mapillary.json";
        const string DET_GEN = WorldBuilder.GEN + "/Props/Detalji";

        [MenuItem("Magistrala/Detalji: postavi")]
        public static string DetaljiMenu()
        {
            MainScene.Ensure();
            var s = Detalji();
            var sc = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc);
            Debug.Log(s); return s;
        }

        // atlas cells (8 x 8 px each in a 64 x 64 texture)
        const int cConcrete = 0, cWood = 1, cCable = 2, cGalv = 3, cLid = 4, cBlue = 5, cYellow = 6, cCabinet = 7, cRed = 8, cSteel = 9, cGreen = 10, cRubber = 11, cSeam = 12;
        static readonly Color[] DetColors = {
            new Color(.56f, .56f, .53f), new Color(.30f, .22f, .15f), new Color(.04f, .04f, .04f), new Color(.52f, .54f, .54f),
            new Color(.25f, .26f, .26f), new Color(.08f, .24f, .58f), new Color(.86f, .70f, .08f), new Color(.74f, .73f, .68f),
            new Color(.66f, .07f, .05f), new Color(.62f, .64f, .64f), new Color(.12f, .26f, .16f), new Color(.06f, .06f, .06f),
            new Color(.42f, .42f, .40f),
        };
        static Vector2 Cell(int c) => new Vector2(((c % 8) + .5f) / 8f, ((c / 8) + .5f) / 8f);

        class DItem { public string id, kind; public double x, z, bearing, roadDist; public int n; public bool rec; }

        public static string Detalji()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            log = new List<string>(); placed = new List<(Vector2, Vector2, float)>(); perProp = new Dictionary<string, int>(); nPlaced = nRejected = 0;
            if (!File.Exists(DETALJI)) return $"[detalji] nema {DETALJI}";
            var J = MiniJson.O(MiniJson.Parse(File.ReadAllText(DETALJI)));
            List<DItem> Items(string key, Func<Dictionary<string, object>, DItem> f) => (MiniJson.L(MiniJson.G(J, key)) ?? new List<object>()).Select(o => f(MiniJson.O(o))).ToList();
            DItem Base(Dictionary<string, object> d) => new DItem { id = (string)d["id"], x = MiniJson.D(d["x"]), z = MiniJson.D(d["z"]), bearing = MiniJson.D(MiniJson.G(d, "roadBearing") ?? 0.0),
                                                                    roadDist = MiniJson.D(MiniJson.G(d, "roadDist") ?? 0.0), kind = (string)MiniJson.G(d, "kind") ?? "", n = 1 };
            var poles = Items("poles", Base);
            var bins = Items("bins", d => { var b = Base(d); b.n = (int)MiniJson.D(d["n"]); b.rec = MiniJson.G(d, "recikliranje") is bool r && r; return b; });
            var boxes = Items("boxes", Base);
            var hyd = Items("hydrants", Base);
            var spans = (MiniJson.L(MiniJson.G(J, "wires")) ?? new List<object>()).Select(o => { var q = MiniJson.L(o); return ((int)MiniJson.D(q[0]), (int)MiniJson.D(q[1])); }).ToList();

            // ---- prefabs
            TripoDir(DET_GEN);
            var mat = DetMaterial();
            var pf = new Dictionary<string, GameObject>
            {
                ["stup-beton"] = DetPrefab("stup-beton", PoleMeshD(false), mat, .035f, ShadowCastingMode.On, new Vector3(.3f, 9f, .3f), true),
                ["stup-drvo"] = DetPrefab("stup-drvo", PoleMeshD(true), mat, .035f, ShadowCastingMode.On, new Vector3(.3f, 8.5f, .3f), true),
                ["zica-struja"] = DetPrefab("zica-struja", WireMesh(.028f), mat, .0144f, ShadowCastingMode.Off, Vector3.zero, false, .1f),
                ["zica-telefon"] = DetPrefab("zica-telefon", WireMesh(.014f), mat, .0144f, ShadowCastingMode.Off, Vector3.zero, false, .1f),
                ["kontejner-sivi"] = DetPrefab("kontejner-sivi", BinMesh(cGalv), mat, .009f, ShadowCastingMode.On, new Vector3(1.37f, 1.3f, 1.07f), false),
                ["kontejner-plavi"] = DetPrefab("kontejner-plavi", BinMesh(cBlue), mat, .009f, ShadowCastingMode.On, new Vector3(1.37f, 1.3f, 1.07f), false),
                ["kontejner-zuti"] = DetPrefab("kontejner-zuti", BinMesh(cYellow), mat, .009f, ShadowCastingMode.On, new Vector3(1.37f, 1.3f, 1.07f), false),
                ["kanta"] = DetPrefab("kanta", LitterBinMesh(), mat, .008f, ShadowCastingMode.Off, new Vector3(.45f, 1f, .45f), true),
                ["ormaric"] = DetPrefab("ormaric", CabinetMesh(), mat, .01f, ShadowCastingMode.On, new Vector3(.84f, 1.4f, .36f), false),
                ["hidrant"] = DetPrefab("hidrant", HydrantMesh(), mat, .008f, ShadowCastingMode.Off, new Vector3(.25f, .9f, .25f), true),
            };

            // ---- scene state (as Stanice)
            roads = StreetsBuilder.Carriageways(); roadGrid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < roads.Count; i++)
            {
                var r = roads[i]; double pad = r.hw + 30;
                for (int gx = (int)Math.Floor((Math.Min(r.ax, r.bx) - pad) / 16); gx <= (int)Math.Floor((Math.Max(r.ax, r.bx) + pad) / 16); gx++)
                    for (int gz = (int)Math.Floor((Math.Min(r.az, r.bz) - pad) / 16); gz <= (int)Math.Floor((Math.Max(r.az, r.bz) + pad) / 16); gz++)
                    { if (!roadGrid.TryGetValue((gx, gz), out var l)) roadGrid[(gx, gz)] = l = new List<int>(); l.Add(i); }
            }
            models = WorldData.Models(-1e9, 1e9);
            var game = Resources.Load<Magistrala.Game.GameWorld>(Magistrala.Game.GameWorld.ResourcePath);
            bays = game ? game.stops.Select(s => (s.bay, s.bayDir, s.bayHl, s.bayHw)).ToList() : new List<(Vector3, Vector2, float, float)>();
            var world = GameObject.Find("World"); if (!world) return "[detalji] nema World";
            var props = world.transform.Find("Props");
            if (!props) { props = new GameObject("Props").transform; props.SetParent(world.transform, false); props.gameObject.isStatic = true; }
            var old = props.Find("Detalji"); if (old) UnityEngine.Object.DestroyImmediate(old.gameObject);
            foreach (var lg in props.GetComponentsInChildren<LODGroup>(true))
            {
                var rs = lg.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds);
                if (bb.size.x > 30 || bb.size.z > 30) continue;
                placed.Add((new Vector2(bb.center.x, bb.center.z), new Vector2(bb.extents.x, bb.extents.z), 0));
            }
            var stanice = world.transform.Find("Stanice");
            if (stanice)
                foreach (var lg in stanice.GetComponentsInChildren<LODGroup>(true))
                {
                    var rs = lg.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                    var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds);
                    placed.Add((new Vector2(bb.center.x, bb.center.z), new Vector2(bb.extents.x + .3f, bb.extents.z + .3f), 0));
                }
            stGates = new List<Vector3>();
            if (File.Exists(Yards.SpotsPath))
            {
                var yj = MiniJson.O(MiniJson.Parse(File.ReadAllText(Yards.SpotsPath)));
                foreach (var key in new[] { "gates", "doors", "frontGates" })
                    if (MiniJson.L(MiniJson.G(yj, key)) is List<object> gl)
                        foreach (var o in gl) { var q = MiniJson.L(o); stGates.Add(new Vector3(MiniJson.F(q[0]), MiniJson.F(q[1]), MiniJson.F(q[2]))); }
            }
            // street lamps (World/Props/rasvjeta): Unity x, ground y, z; type 3 = a concrete pole with a bracket (a utility pole)
            var lamps = new List<(Vector3 p, int type)>();
            var sl = props.Find("rasvjeta")?.GetComponent<Magistrala.StreetLights>();
            if (sl && sl.lamps != null) for (int i = 0; i < sl.lamps.Length; i++) lamps.Add((new Vector3(sl.lamps[i].x, sl.lamps[i].y, sl.lamps[i].z), sl.types != null && i < sl.types.Length ? sl.types[i] : 1));
            var lampGrid = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < lamps.Count; i++) { var k = ((int)Math.Floor(lamps[i].p.x / 16), (int)Math.Floor(lamps[i].p.z / 16)); if (!lampGrid.TryGetValue(k, out var l)) lampGrid[k] = l = new List<int>(); l.Add(i); }
            int NearLamp(Vector3 u, float r)
            {
                int best = -1; float bd = r * r; int cx = (int)Math.Floor(u.x / 16), cz = (int)Math.Floor(u.z / 16);
                for (int a = -1; a <= 1; a++) for (int b = -1; b <= 1; b++)
                        if (lampGrid.TryGetValue((cx + a, cz + b), out var l)) foreach (int i in l) { float d = (new Vector2(lamps[i].p.x - u.x, lamps[i].p.z - u.z)).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
                return best;
            }
            SignsBuilder.IndexDrawnAsphalt();
            Physics.SyncTransforms();
            root = new GameObject("Detalji").transform; root.SetParent(props, false); root.gameObject.isStatic = true;
            var gPoles = Group("stupovi"); var gWires = Group("zice"); var gBins = Group("kontejneri"); var gBoxes = Group("ormarici"); var gHyd = Group("hidranti");
            var reasons = new Dictionary<string, int>();
            void Why(string k, string w) { var key = k + ": " + w; reasons.TryGetValue(key, out int c); reasons[key] = c + 1; }

            // the nearest carriageway (any direction) within reach of a game point: projection, half width, unit tangent, distance from the centre line
            (Vector2 p, float hw, Vector2 t, float d)? Near(Vector2 q, float reach)
            {
                (Vector2, float, Vector2, float)? best = null; float bd = reach;
                if (!roadGrid.TryGetValue(((int)Math.Floor(q.x / 16), (int)Math.Floor(q.y / 16)), out var l)) return null;
                foreach (int i in l)
                {
                    var r = roads[i]; var a = new Vector2((float)r.ax, (float)r.az); var dd = new Vector2((float)(r.bx - r.ax), (float)(r.bz - r.az));
                    float L = dd.magnitude; if (L < .05f) continue; var u = dd / L;
                    float t = Mathf.Clamp(Vector2.Dot(q - a, u), 0, L); var p = a + u * t; float d = (q - p).magnitude;
                    float edge = Mathf.Max(0, d - (float)r.hw);
                    if (edge < bd) { bd = edge; best = (p, (float)r.hw, u, d); }
                }
                return best;
            }

            /// find a free spot for a footprint (hx along the street, hz across, centre) near the survey point; rot: +Z faces the road
            bool Spot(DItem it, string what, float hx, float hz, float clear, bool gates, out Vector3 pos, out Quaternion rot)
            {
                pos = default; rot = Quaternion.identity;
                var g0 = new Vector2((float)it.x, (float)it.z);
                var nr = Near(g0, 30);
                Vector2 t, n, b0;
                if (nr == null)
                {
                    double h = it.bearing * Math.PI / 180; t = new Vector2((float)Math.Sin(h), (float)-Math.Cos(h)); n = new Vector2(-t.y, t.x); b0 = g0;
                }
                else
                {
                    var v = nr.Value; t = v.t; n = g0 - v.p; n = n.sqrMagnitude < 1e-4f ? new Vector2(-t.y, t.x) : n.normalized;
                    float want = v.hw + clear + hz;
                    b0 = v.d < want ? v.p + n * want : (v.d > v.hw + 6 ? g0 : g0);
                }
                var outN = n;  // away from the road
                for (int k = 0; k <= 16; k++)
                {
                    float along = (k + 1) / 2 * .5f * (k % 2 == 0 ? 1 : -1);
                    foreach (float o in new[] { 0f, .4f, .8f, 1.2f })
                    {
                        var gc = b0 + t * along + outN * o;
                        var u = W.U(gc.x, 0, gc.y);
                        var fwd = W.U(gc.x - outN.x, 0, gc.y - outN.y) - u;
                        var r = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                        if (SignsBuilder.DrawnAsphalt(gc.x, gc.y, Mathf.Max(hx, hz) * .7f + .1f)) { Why(what, "asfalt"); continue; }
                        if (NearLamp(u, what.StartsWith("stup") ? 1.2f : Mathf.Max(hx, hz) + .6f) >= 0) { Why(what, "lampa"); continue; }
                        if (gates)
                        {
                            bool bad = false; float gx0 = hx + .9f, gz0 = hz + 2.5f;
                            foreach (var gp in stGates) { var l = Quaternion.Inverse(r) * (gp - u); l.y = 0; if (Mathf.Abs(l.x) < gx0 && Mathf.Abs(l.z) < gz0) { bad = true; break; } }
                            if (bad) { Why(what, "kapija / ulaz"); continue; }
                        }
                        if (!Free(u, hx, hz, r, out float y, out string w, false, .3f, .6f)) { Why(what, w); continue; }
                        pos = new Vector3(u.x, y, u.z); rot = r; return true;
                    }
                }
                return false;
            }

            GameObject Inst(Transform parent, string p, Vector3 at, Quaternion r, Vector3 scale, string name)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(pf[p], parent);
                go.transform.SetPositionAndRotation(at, r); go.transform.localScale = scale; go.name = name; go.isStatic = false;
                perProp.TryGetValue(p, out int c); perProp[p] = c + 1; nPlaced++;
                return go;
            }

            // ---- poles
            var anchor = new Vector3?[poles.Count]; var anchorH = new float[poles.Count];
            int nLampPole = 0;
            for (int i = 0; i < poles.Count; i++)
            {
                var it = poles[i]; var u0 = W.U(it.x, 0, it.z);
                int li = NearLamp(u0, 3f);
                if (li >= 0)
                {   // the lamp stands on this pole (or right beside it): no second pole; a concrete / village lamp carries the wires
                    if (lamps[li].type >= 2) { anchor[i] = lamps[li].p; anchorH[i] = 6.6f; nLampPole++; }
                    continue;
                }
                bool wood = it.kind == "drvo";
                if (!Spot(it, "stup", .16f, .16f, .45f, true, out var pos, out var rot)) { nRejected++; continue; }
                rot = Quaternion.Euler(0, Hash(it.id) % 360, 0);
                Inst(gPoles, wood ? "stup-drvo" : "stup-beton", pos, rot, Vector3.one, it.id);
                placed.Add((new Vector2(pos.x, pos.z), new Vector2(.25f, .25f), 0));
                anchor[i] = pos; anchorH[i] = wood ? 7.0f : 7.3f;
            }
            // ---- wires
            int nSpan = 0, nSpanBlocked = 0; float wireLen = 0;
            foreach (var (i, j) in spans)
            {
                if (i >= poles.Count || j >= poles.Count || anchor[i] == null || anchor[j] == null) continue;
                var A = anchor[i].Value + Vector3.up * anchorH[i]; var B = anchor[j].Value + Vector3.up * anchorH[j];
                var d = B - A; float L = d.magnitude; if (L < 8 || L > 55) continue;
                bool blocked = false;
                foreach (var off in new[] { 0f, -1.3f })
                {
                    var a = A + Vector3.up * off; var b = B + Vector3.up * off;
                    foreach (var h in Physics.RaycastAll(a, (b - a).normalized, L, ~0, QueryTriggerInteraction.Ignore))
                        if (!Ignored(h.collider) && h.distance > .5f && h.distance < L - .5f) { blocked = true; break; }
                    if (blocked) break;
                }
                // the line's lowest point stays 4.5 m above the ground below it (no span through a hill crest)
                if (!blocked)
                {
                    var mid = (A + B) / 2; float gy = GroundY(mid.x, mid.z, out bool bld);
                    if (bld || (!float.IsNaN(gy) && mid.y - (.02f * L + .15f) - 1.3f - gy < 4.5f)) blocked = true;
                }
                if (blocked) { nSpanBlocked++; continue; }
                float sag = .02f * L + .15f;
                var r = Quaternion.LookRotation(d / L, Vector3.up);
                Inst(gWires, "zica-struja", A, r, new Vector3(1, sag, L), $"{poles[i].id}-{poles[j].id}");
                Inst(gWires, "zica-telefon", A + Vector3.up * -1.3f, Quaternion.LookRotation((B - A) / L, Vector3.up), new Vector3(1, sag * .8f, L), $"{poles[i].id}-{poles[j].id} tk");
                nSpan++; wireLen += L;
            }
            // ---- waste
            int nGroups = 0, nCont = 0, nKanta = 0;
            foreach (var it in bins)
            {
                if (it.kind == "kanta")
                {
                    if (!Spot(it, "kanta", .25f, .25f, .4f, false, out var p, out var r)) { nRejected++; continue; }
                    Inst(gBins, "kanta", p, r, Vector3.one, it.id); placed.Add((new Vector2(p.x, p.z), new Vector2(.3f, .3f), 0)); nKanta++;
                    continue;
                }
                int n = Mathf.Clamp(it.n, 1, 4);
                float hx = n * 1.45f / 2, hz = .58f;
                if (!Spot(it, "kontejneri", hx, hz, .35f, true, out var pos, out var rot))
                {   // no room for the whole group: one less
                    if (n < 2 || !Spot(it, "kontejneri", (n - 1) * 1.45f / 2, hz, .35f, true, out pos, out rot)) { nRejected++; continue; }
                    n--; hx = n * 1.45f / 2;
                }
                var kinds = new List<string>();
                if (it.rec) { kinds.Add("kontejner-sivi"); kinds.Add("kontejner-plavi"); kinds.Add("kontejner-zuti"); }
                else { for (int k = 0; k < n; k++) kinds.Add("kontejner-sivi"); if (n >= 2 && Hash(it.id) % 3 == 0) kinds[n - 1] = Hash(it.id) % 2 == 0 ? "kontejner-plavi" : "kontejner-zuti"; }
                var g = Group(it.id); g.SetParent(gBins, true);
                for (int k = 0; k < n; k++)
                {
                    float x = (k - (n - 1) / 2f) * 1.45f;
                    float wob = ((Hash(it.id + k) % 9) - 4) * 1.2f;
                    var p = pos + rot * new Vector3(x, 0, ((Hash(it.id + "z" + k) % 5) - 2) * .03f);
                    float gy = GroundY(p.x, p.z, out bool bld); if (!bld && !float.IsNaN(gy) && Mathf.Abs(gy - pos.y) < .35f) p.y = gy;
                    Inst(g, kinds[k % kinds.Count], p, rot * Quaternion.Euler(0, wob, 0), Vector3.one, kinds[k % kinds.Count]); nCont++;
                }
                placed.Add((new Vector2(pos.x, pos.z), new Vector2(hx, hz), rot.eulerAngles.y)); nGroups++;
            }
            // ---- cabinets, hydrants
            int nBox = 0, nHyd = 0;
            foreach (var it in boxes)
            {
                if (!Spot(it, "ormaric", .42f, .18f, .3f, true, out var p, out var r)) { nRejected++; continue; }
                Inst(gBoxes, "ormaric", p, r, Vector3.one, it.id); placed.Add((new Vector2(p.x, p.z), new Vector2(.45f, .2f), r.eulerAngles.y)); nBox++;
            }
            foreach (var it in hyd)
            {
                if (!Spot(it, "hidrant", .13f, .13f, .5f, true, out var p, out var r)) { nRejected++; continue; }
                Inst(gHyd, "hidrant", p, r, Vector3.one, it.id); placed.Add((new Vector2(p.x, p.z), new Vector2(.15f, .15f), 0)); nHyd++;
            }
            int pCount = perProp.Where(x => x.Key.StartsWith("stup")).Sum(x => x.Value);
            log.Insert(0, $"[detalji] stupovi {pCount}/{poles.Count} (+{nLampPole} na stupu lampe), raspona žica {nSpan}/{spans.Count} ({wireLen:F0} m, {nSpanBlocked} kroz zgradu / brdo), " +
                          $"kontejneri {nCont} u {nGroups} grupa + {nKanta} kanti / {bins.Count} mjesta, ormarići {nBox}/{boxes.Count}, hidranti {nHyd}/{hyd.Count}; " +
                          $"odbijeno {nRejected}; {nPlaced} instanci, {sw.ElapsedMilliseconds / 1000.0:F1} s");
            log.Add("  razlozi pomaka: " + string.Join(", ", reasons.OrderByDescending(x => x.Value).Take(14).Select(x => $"{x.Key} {x.Value}")));
            return string.Join("\n", log);
        }

        // ================================================================== assets
        static Material DetMaterial()
        {
            string dir = DET_GEN, texPath = $"{dir}/detalji-atlas.png", matPath = $"{dir}/detalji.mat";
            var t = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var px = new Color[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { int c = (y / 8) * 8 + x / 8; px[y * 64 + x] = c < DetColors.Length ? DetColors[c] : Color.magenta; }
            t.SetPixels(px); t.Apply(); File.WriteAllBytes(texPath, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(texPath);
            var ti = (TextureImporter)AssetImporter.GetAtPath(texPath); ti.sRGBTexture = true; ti.mipmapEnabled = false; ti.filterMode = FilterMode.Point; ti.wrapMode = TextureWrapMode.Clamp;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", Color.white); mat.SetFloat("_Smoothness", .22f); mat.SetFloat("_Metallic", 0);
            mat.enableInstancing = true; EditorUtility.SetDirty(mat);
            return mat;
        }

        /// prefab: one lod0 renderer, a LODGroup culled below `cull` (screen-relative height); col: capsule (round) / box size, zero = none;
        /// lodSize > 0 overrides the group's size (the wires: scaled by their length)
        static GameObject DetPrefab(string name, Mesh mesh, Material mat, float cull, ShadowCastingMode shadow, Vector3 col, bool capsule, float lodSize = 0)
        {
            string meshPath = $"{DET_GEN}/{name}.asset", pfPath = $"{DET_GEN}/{name}.prefab";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (old) { old.Clear(); EditorUtility.CopySerialized(mesh, old); UnityEngine.Object.DestroyImmediate(mesh); mesh = old; EditorUtility.SetDirty(old); }
            else AssetDatabase.CreateAsset(mesh, meshPath);
            var go = new GameObject(name);
            var lod0 = new GameObject("lod0"); lod0.transform.SetParent(go.transform, false);
            lod0.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = lod0.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = shadow;
            var lg = go.AddComponent<LODGroup>(); lg.SetLODs(new[] { new LOD(cull, new Renderer[] { mr }) }); lg.RecalculateBounds();
            if (lodSize > 0) lg.size = lodSize;
            if (col != Vector3.zero)
            {
                if (capsule) { var cc = go.AddComponent<CapsuleCollider>(); cc.radius = col.x / 2; cc.height = col.y; cc.center = new Vector3(0, col.y / 2, 0); cc.direction = 1; }
                else { var bc = go.AddComponent<BoxCollider>(); bc.size = col; bc.center = new Vector3(0, col.y / 2, 0); }
            }
            PrefabUtility.SaveAsPrefabAsset(go, pfPath); UnityEngine.Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<GameObject>(pfPath);
        }

        // ------------------------------------------------------------------ mesh kit
        sealed class MK
        {
            public List<Vector3> V = new List<Vector3>(), N = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>(); public List<int> I = new List<int>();
            /// quad a b c d (a loop), facing `out` (the winding is fixed to face it)
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, int cell, bool twoSided = false)
            {
                var n = Vector3.Cross(b - a, c - a); if (Vector3.Dot(n, outward) < 0) { var t = b; b = d; d = t; n = -n; }
                n = n.normalized; var uv = Cell(cell); int k = V.Count;
                V.AddRange(new[] { a, b, c, d }); for (int i = 0; i < 4; i++) { N.Add(n); UV.Add(uv); }
                I.AddRange(new[] { k, k + 1, k + 2, k, k + 2, k + 3 });
                if (twoSided) Quad(a, b, c, d, -outward, cell);
            }
            /// a frustum of rectangles: bottom (w0, d0) at y0, top (w1, d1) at y1, centred on (cx, cz); cap: top face
            public void Frustum(float cx, float cz, float y0, float y1, float w0, float d0, float w1, float d1, int cell, bool cap = true, bool bottom = false)
            {
                Vector3 P(float w, float d, float y, int sx, int sz) => new Vector3(cx + sx * w / 2, y, cz + sz * d / 2);
                var b = new[] { P(w0, d0, y0, -1, -1), P(w0, d0, y0, 1, -1), P(w0, d0, y0, 1, 1), P(w0, d0, y0, -1, 1) };
                var t = new[] { P(w1, d1, y1, -1, -1), P(w1, d1, y1, 1, -1), P(w1, d1, y1, 1, 1), P(w1, d1, y1, -1, 1) };
                var c = new Vector3(cx, (y0 + y1) / 2, cz);
                for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; var mid = (b[i] + b[j] + t[i] + t[j]) / 4; var o = mid - c; o.y = 0; Quad(b[i], b[j], t[j], t[i], o, cell); }
                if (cap) Quad(t[0], t[1], t[2], t[3], Vector3.up, cell);
                if (bottom) Quad(b[0], b[1], b[2], b[3], Vector3.down, cell);
            }
            public void Box(Vector3 c, Vector3 s, int cell) => Frustum(c.x, c.z, c.y - s.y / 2, c.y + s.y / 2, s.x, s.z, s.x, s.z, cell, true, true);
            /// a tapered prism of `sides` round the y axis at (cx, cz)
            public void Cyl(float cx, float cz, float y0, float y1, float r0, float r1, int sides, int cell, bool cap = true)
            {
                for (int i = 0; i < sides; i++)
                {
                    float a0 = i * Mathf.PI * 2 / sides, a1 = (i + 1) * Mathf.PI * 2 / sides;
                    Vector3 D(float a) => new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    var o = D((a0 + a1) / 2); var c0 = new Vector3(cx, 0, cz);
                    Quad(c0 + D(a0) * r0 + Vector3.up * y0, c0 + D(a1) * r0 + Vector3.up * y0, c0 + D(a1) * r1 + Vector3.up * y1, c0 + D(a0) * r1 + Vector3.up * y1, o, cell);
                    if (cap)
                    {   // (a fan triangle as a degenerate quad)
                        var top = c0 + Vector3.up * y1;
                        Quad(top, c0 + D(a0) * r1 + Vector3.up * y1, c0 + D(a1) * r1 + Vector3.up * y1, top, Vector3.up, cell);
                    }
                }
            }
            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name }; m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); m.SetTriangles(I, 0); m.RecalculateBounds(); return m;
            }
        }

        /// concrete pole: 9 m, tapered 8-sided; a steel bracket + two insulators at 7.3 m and a clamp at 6 m. wooden: 8.5 m, 6-sided, brown
        static Mesh PoleMeshD(bool wood)
        {
            var k = new MK();
            if (wood)
            {
                k.Cyl(0, 0, 0, 8.5f, .13f, .1f, 6, cWood);
                k.Box(new Vector3(0, 7.0f, -.13f), new Vector3(.08f, .25f, .1f), cSteel);
                k.Box(new Vector3(0, 5.7f, -.12f), new Vector3(.06f, .12f, .06f), cCable);
            }
            else
            {
                k.Cyl(0, 0, 0, 9f, .15f, .085f, 8, cConcrete);
                k.Box(new Vector3(0, 7.3f, -.12f), new Vector3(.5f, .06f, .08f), cSteel);
                k.Cyl(-.2f, -.12f, 7.33f, 7.45f, .03f, .025f, 5, cCabinet); k.Cyl(.2f, -.12f, 7.33f, 7.45f, .03f, .025f, 5, cCabinet);
                k.Box(new Vector3(0, 6.0f, -.1f), new Vector3(.06f, .12f, .06f), cCable);
                k.Box(new Vector3(0, 2.4f, -.12f), new Vector3(.14f, .2f, .04f), cRed);    // the numbered plate / warning sign
            }
            return k.Mesh(wood ? "stup-drvo" : "stup-beton");
        }

        /// unit span along +Z (0..1), sagging 1 m at the middle (the instance scales y by the sag, z by the length): a horizontal and a
        /// vertical ribbon, both two-sided
        static Mesh WireMesh(float w)
        {
            var k = new MK(); const int S = 12;
            for (int i = 0; i < S; i++)
            {
                float t0 = i / (float)S, t1 = (i + 1) / (float)S; float y0 = -4 * t0 * (1 - t0), y1 = -4 * t1 * (1 - t1);
                Vector3 p0 = new Vector3(0, y0, t0), p1 = new Vector3(0, y1, t1);
                k.Quad(p0 + Vector3.left * w / 2, p0 + Vector3.right * w / 2, p1 + Vector3.right * w / 2, p1 + Vector3.left * w / 2, Vector3.up, cCable, true);
                k.Quad(p0 + Vector3.up * w / 2, p0 + Vector3.down * w / 2, p1 + Vector3.down * w / 2, p1 + Vector3.up * w / 2, Vector3.right, cCable, true);
            }
            return k.Mesh("zica");
        }

        /// 1100 l container: tapered body on castors, a domed lid (two steps), side handles; front (+Z) toward the road
        static Mesh BinMesh(int cell)
        {
            var k = new MK();
            k.Frustum(0, 0, .2f, 1.18f, 1.22f, .92f, 1.34f, 1.04f, cell, false, true);
            k.Box(new Vector3(0, 1.2f, 0), new Vector3(1.38f, .05f, 1.08f), cell);
            k.Frustum(0, -.02f, 1.22f, 1.3f, 1.34f, 1.02f, 1.18f, .86f, cLid);
            k.Box(new Vector3(0, 1.08f, .56f), new Vector3(1.0f, .05f, .05f), cLid);               // the front handle bar
            foreach (var sx in new[] { -1, 1 }) foreach (var sz in new[] { -1, 1 })
                    k.Box(new Vector3(sx * .5f, .1f, sz * .36f), new Vector3(.07f, .2f, .16f), cRubber);
            k.Box(new Vector3(-.67f, .95f, 0), new Vector3(.05f, .05f, .5f), cLid); k.Box(new Vector3(.67f, .95f, 0), new Vector3(.05f, .05f, .5f), cLid);
            return k.Mesh("kontejner");
        }

        static Mesh LitterBinMesh()
        {
            var k = new MK();
            k.Cyl(0, -.22f, 0, 1.05f, .03f, .03f, 6, cSteel);
            k.Cyl(0, 0, .4f, .95f, .19f, .2f, 10, cGreen, false);
            k.Cyl(0, 0, .95f, .98f, .21f, .21f, 10, cLid);
            return k.Mesh("kanta");
        }

        /// cabinet 0.8 x 1.25 x 0.32 on a concrete plinth, door seams on the front (+Z)
        static Mesh CabinetMesh()
        {
            var k = new MK();
            k.Box(new Vector3(0, .08f, 0), new Vector3(.84f, .16f, .36f), cConcrete);
            k.Box(new Vector3(0, .16f + .625f, 0), new Vector3(.8f, 1.25f, .32f), cCabinet);
            k.Box(new Vector3(0, 1.43f, 0), new Vector3(.84f, .04f, .36f), cCabinet);
            k.Box(new Vector3(0, .78f, .162f), new Vector3(.012f, 1.1f, .006f), cSeam);
            k.Box(new Vector3(0, .25f, .162f), new Vector3(.74f, .012f, .006f), cSeam); k.Box(new Vector3(0, 1.33f, .162f), new Vector3(.74f, .012f, .006f), cSeam);
            k.Box(new Vector3(.1f, .8f, .166f), new Vector3(.03f, .07f, .01f), cSteel);
            return k.Mesh("ormaric");
        }

        static Mesh HydrantMesh()
        {
            var k = new MK();
            k.Cyl(0, 0, 0, .72f, .095f, .085f, 10, cRed, false);
            k.Cyl(0, 0, .72f, .8f, .11f, .1f, 10, cRed);
            k.Cyl(0, 0, .8f, .86f, .05f, .03f, 6, cRed);
            k.Box(new Vector3(0, .52f, .11f), new Vector3(.08f, .08f, .08f), cRed);
            k.Box(new Vector3(.11f, .5f, 0), new Vector3(.06f, .06f, .06f), cRed); k.Box(new Vector3(-.11f, .5f, 0), new Vector3(.06f, .06f, .06f), cRed);
            return k.Mesh("hidrant");
        }
    }
}
