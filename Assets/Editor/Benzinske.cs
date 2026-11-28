// Magistrala/Benzinske: postavi — the petrol stations of Kaštela as modular, branded stations (World/Benzinske).
// Kit: Assets/Models/Benzinske/*.glb (BalkanDriver assets-src/benzinske/scripts/build.py, Blender; from "Gas station" and
// "Gas station Props" by Elbolilloduro, CC BY 4.0, modified – CREDITS.md) + per-brand atlases tex/<brand>_atlas.png. Three
// invented brands (no real names / logos): jn "Jadranska nafta" (blue), sunce "Sunce gorivo" (yellow / orange), benzol
// "Benzol" (red / dark blue). Each part GLB holds <part>_LOD0 / <part>_LOD1; its materials are named brand / src / glass / emit
// and are swapped here for the brand's own (one URP Lit material per brand atlas, instanced).
// Data: Assets/Editor/benzinske.json — per site (OSM amenity=fuel): brand, canopy centre / axis / islands (game frame: x east,
// z south; along = (cos a, sin a), as model-buildings.json canopies), shop / totem / hut / LPG tank spots, the package objects
// (model-buildings.json ids) and tile buildings (tx_tz:i) it replaces. BuildingsBuilder / BuildingsFromTiles skip those ids
// (Skip*), so after editing the lists run "Build tile buildings" (or at least this menu, which rebuilds the package part).
// Rules: a job's bay (GameWorld stops: the refuelling spot of Pumpa Kaštela, deliveries) never gets an island / pump / prop
// (the canopy stays over it), so the stops keep working; everything deterministic (no random, hashes of the site id).
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
    public static class Benzinske
    {
        public const string DATA = "Assets/Editor/benzinske.json";
        const string KIT = "Assets/Models/Benzinske", GEN = WorldBuilder.GEN + "/Benzinske";
        static readonly string[] Brands = { "jn", "sunce", "benzol" };
        static readonly string[] Parts = { "canopy2", "canopy3", "canopy4", "island", "pump", "pump_lpg", "shopS", "shopL", "totem", "hut",
            "cage", "bollard", "airwater", "bin", "lpgtank", "washer", "freezer", "pallet", "crates", "dumpster" };
        /// parts whose look does not depend on the brand (one prefab)
        static readonly HashSet<string> Neutral = new HashSet<string> { "freezer", "pallet", "crates", "dumpster", "bollard", "bin" };
        /// LOD1 / cull distances (screen height share) per part size class
        static float[] Lods(string p) => p.StartsWith("canopy") || p.StartsWith("shop") || p == "totem" ? new[] { .12f, .015f } :
                                          p == "hut" || p == "lpgtank" || p.StartsWith("pump") || p == "island" ? new[] { .2f, .03f } : new[] { .25f, .05f };

        // ------------------------------------------------------------------ data
        public class Site
        {
            public string id, name, brand, note;
            public double cx, cz, ca; public int n; public List<int> skipIslands = new List<int>(); public bool lpg;
            public double sx = double.NaN, sz, sfx, sfz; public string shop = "S";
            public double tx = double.NaN, tz, tfx, tfz;
            public double hx = double.NaN, hz, hfx, hfz;
            public double lx = double.NaN, lz, la;
            public double px = double.NaN, pz, pa, pl, pw;   // an asphalt forecourt pad (where the world has none)
            public double bayHw = double.NaN;               // a stop's bay narrowed to this half width here (a pad-sized delivery bay)
            public List<string> package = new List<string>(), tiles = new List<string>();
        }

        static List<Site> sites;
        public static List<Site> Sites()
        {
            if (sites != null) return sites;
            sites = new List<Site>();
            if (!File.Exists(DATA)) return sites;
            foreach (var o in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(DATA)))["sites"]))
            {
                var d = MiniJson.O(o); var s = new Site { id = (string)d["id"], name = (string)MiniJson.G(d, "name") ?? "", brand = (string)d["brand"], note = (string)MiniJson.G(d, "note") ?? "" };
                var c = MiniJson.O(d["canopy"]); s.cx = MiniJson.D(c["x"]); s.cz = MiniJson.D(c["z"]); s.ca = MiniJson.D(c["a"]); s.n = (int)MiniJson.D(c["n"]);
                if (MiniJson.L(MiniJson.G(c, "skipIslands")) is List<object> sk) foreach (var q in sk) s.skipIslands.Add((int)MiniJson.D(q));
                s.lpg = MiniJson.G(d, "lpg") is bool lb && lb;
                if (MiniJson.G(d, "shop") is Dictionary<string, object> sh)
                { s.sx = MiniJson.D(sh["x"]); s.sz = MiniJson.D(sh["z"]); s.sfx = MiniJson.D(sh["faceX"]); s.sfz = MiniJson.D(sh["faceZ"]); s.shop = (string)MiniJson.G(sh, "size") ?? "S"; }
                if (MiniJson.G(d, "totem") is Dictionary<string, object> to)
                { s.tx = MiniJson.D(to["x"]); s.tz = MiniJson.D(to["z"]); s.tfx = MiniJson.D(to["faceX"]); s.tfz = MiniJson.D(to["faceZ"]); }
                if (MiniJson.G(d, "hut") is Dictionary<string, object> hu)
                { s.hx = MiniJson.D(hu["x"]); s.hz = MiniJson.D(hu["z"]); s.hfx = MiniJson.D(hu["faceX"]); s.hfz = MiniJson.D(hu["faceZ"]); }
                if (MiniJson.G(d, "lpgTank") is Dictionary<string, object> lt)
                { s.lx = MiniJson.D(lt["x"]); s.lz = MiniJson.D(lt["z"]); s.la = MiniJson.D(lt["a"]); }
                if (MiniJson.G(d, "pad") is Dictionary<string, object> pd)
                { s.px = MiniJson.D(pd["x"]); s.pz = MiniJson.D(pd["z"]); s.pa = MiniJson.D(pd["a"]); s.pl = MiniJson.D(pd["l"]); s.pw = MiniJson.D(pd["w"]); }
                if (MiniJson.G(d, "bayHw") is object bh) s.bayHw = MiniJson.D(bh);
                if (MiniJson.L(MiniJson.G(d, "replacePackage")) is List<object> rp) foreach (var q in rp) s.package.Add((string)q);
                if (MiniJson.L(MiniJson.G(d, "replaceTiles")) is List<object> rt) foreach (var q in rt) s.tiles.Add((string)q);
                sites.Add(s);
            }
            return sites;
        }
        /// model-buildings.json objects replaced by a station (BuildingsBuilder.Build skips them: no box, no pump spots)
        public static bool SkipPackage(string id) { foreach (var s in Sites()) if (s.package.Contains(id)) return true; return false; }
        /// tile buildings (tx_tz:i) replaced by a station (BuildingsFromTiles skips them)
        public static bool SkipTile(string id) { if (id == null) return false; foreach (var s in Sites()) if (s.tiles.Contains(id)) return true; return Trgovine.SkipTile(id); }

        // ------------------------------------------------------------------ kit -> prefabs
        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = Path.GetDirectoryName(p).Replace('\\', '/'); Dir(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(p)); } }

        static Material Mat(string name, Func<Material> make)
        {
            if (mats.TryGetValue(name, out var m) && m) return m;
            Dir(GEN);
            string p = $"{GEN}/{name}.mat";
            var fresh = make();
            var old = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (old) { old.shader = fresh.shader; old.CopyPropertiesFromMaterial(fresh); old.enableInstancing = true; EditorUtility.SetDirty(old); UnityEngine.Object.DestroyImmediate(fresh); m = old; }
            else { fresh.enableInstancing = true; AssetDatabase.CreateAsset(fresh, p); m = fresh; }
            mats[name] = m; return m;
        }

        static Texture2D Tex(string file)
        {
            string p = $"{KIT}/tex/{file}";
            var ti = AssetImporter.GetAtPath(p) as TextureImporter;
            if (ti && (ti.maxTextureSize != 2048 || ti.anisoLevel != 4 || !ti.mipmapEnabled))
            { ti.maxTextureSize = 2048; ti.anisoLevel = 4; ti.mipmapEnabled = true; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport(); }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }

        static Material Lit(Texture2D t, float smooth, Color emit = default, bool glass = false)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.SetTexture("_BaseMap", t); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0);
            if (emit != default)
            {
                m.EnableKeyword("_EMISSION"); m.SetTexture("_EmissionMap", t); m.SetColor("_EmissionColor", emit);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (glass)
            {
                m.SetColor("_BaseColor", new Color(.10f, .14f, .16f, .38f)); m.SetFloat("_Smoothness", .93f); m.SetFloat("_Metallic", .2f);
                m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_ZWrite", 0); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); m.renderQueue = (int)RenderQueue.Transparent;
                m.SetOverrideTag("RenderType", "Transparent");
            }
            return m;
        }

        static Material MatFor(string slot, string brand)
        {
            switch (slot)
            {
                case "src": return Mat("src", () => Lit(Tex("src_atlas.png"), .25f));
                case "glass": return Mat("glass", () => Lit(null, .9f, default, true));
                case "emit": return Mat("emit_" + brand, () => Lit(Tex(brand + "_atlas.png"), .3f, new Color(1.6f, 1.6f, 1.55f)));
                default: return Mat("brand_" + brand, () => Lit(Tex(brand + "_atlas.png"), .32f));
            }
        }

        static readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
        static readonly Dictionary<string, Bounds> sizes = new Dictionary<string, Bounds>();

        /// "<part>/<brand>" prefab: LODGroup over the GLB's LOD meshes with the brand's materials and simple colliders
        static GameObject Prefab(string part, string brand)
        {
            if (Neutral.Contains(part)) brand = "jn";
            string key = part + "/" + brand;
            if (prefabs.TryGetValue(key, out var pf) && pf) return pf;
            string glb = $"{KIT}/{part}.glb";
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(glb);
            if (!src) throw new Exception("Benzinske: nema " + glb);
            var go = new GameObject(Neutral.Contains(part) ? part : $"{part}-{brand}");
            var lods = new List<LOD>(); var lv = Lods(part);
            Bounds b0 = default; bool has0 = false;
            for (int i = 0; i < 2; i++)
            {
                var t = src.GetComponentsInChildren<MeshFilter>(true).FirstOrDefault(f => f.name.EndsWith($"_LOD{i}") || f.transform.parent && f.transform.parent.name.EndsWith($"_LOD{i}"));
                if (!t) continue;
                var mr = t.GetComponent<MeshRenderer>();
                var c = new GameObject($"lod{i}"); c.transform.SetParent(go.transform, false);
                // the GLB node's transform (glTFast: the node, mirrored to Unity) relative to the import root
                c.transform.localPosition = src.transform.InverseTransformPoint(t.transform.position);
                c.transform.localRotation = Quaternion.Inverse(src.transform.rotation) * t.transform.rotation;
                c.transform.localScale = t.transform.lossyScale;
                c.AddComponent<MeshFilter>().sharedMesh = t.sharedMesh;
                var r = c.AddComponent<MeshRenderer>();
                r.sharedMaterials = mr.sharedMaterials.Select(m => MatFor(SlotName(m), brand)).ToArray();
                r.shadowCastingMode = i == 0 || part.StartsWith("canopy") || part.StartsWith("shop") ? ShadowCastingMode.On : ShadowCastingMode.Off;
                lods.Add(new LOD(lv[i], new Renderer[] { r }));
                if (i == 0) { b0 = r.bounds; has0 = true; }
            }
            var lg = go.AddComponent<LODGroup>(); lg.SetLODs(lods.ToArray()); lg.RecalculateBounds();
            if (has0) sizes[part] = b0;
            Colliders(part, go, b0);
            Dir(GEN);
            string p = $"{GEN}/{go.name}.prefab";
            pf = PrefabUtility.SaveAsPrefabAsset(go, p);
            UnityEngine.Object.DestroyImmediate(go);
            prefabs[key] = pf; return pf;
        }

        static string SlotName(Material m)
        {
            if (!m) return "brand";
            string n = m.name.ToLowerInvariant();
            return n.StartsWith("src") ? "src" : n.StartsWith("glass") ? "glass" : n.StartsWith("emit") ? "emit" : "brand";
        }

        static void Colliders(string part, GameObject go, Bounds b)
        {
            if (part.StartsWith("canopy"))
            {
                // pillars only (the roof is 5 m up); found as the canopy's island spacing (10 m)
                int n = part[part.Length - 1] - '0';
                var along = Mathf.Abs(b.size.x) > Mathf.Abs(b.size.z) ? Vector3.right : Vector3.forward;
                for (int i = 0; i < n; i++)
                {
                    var cap = go.AddComponent<CapsuleCollider>(); cap.radius = .3f; cap.height = 5f; cap.direction = 1;
                    cap.center = along * ((i - (n - 1) / 2f) * 10f) + Vector3.up * 2.6f;
                }
                return;
            }
            if (part == "washer" || part == "bollard") { var c = go.AddComponent<CapsuleCollider>(); c.radius = .1f; c.height = b.size.y; c.center = new Vector3(0, b.size.y / 2, 0); return; }
            var bc = go.AddComponent<BoxCollider>();
            bc.center = go.transform.InverseTransformPoint(b.center); bc.size = b.size * .96f;
        }

        public static string ImportKit()
        {
            prefabs.Clear(); mats.Clear(); sizes.Clear();
            AssetDatabase.Refresh();
            int n = 0;
            foreach (var part in Parts)
                foreach (var br in Neutral.Contains(part) ? new[] { "jn" } : Brands) { Prefab(part, br); n++; }
            AssetDatabase.SaveAssets();
            return $"[benzinske] kit: {n} prefaba ({GEN})";
        }

        // ------------------------------------------------------------------ placement
        static Transform root;
        static List<(Vector3 c, Vector2 dir, float hl, float hw)> bays;
        static readonly List<string> log = new List<string>();

        [MenuItem("Magistrala/Benzinske: postavi")]
        public static string Menu()
        {
            MainScene.Ensure();
            var s = Place();
            var sc = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc);
            Debug.Log(s); return s;
        }

        [MenuItem("Magistrala/Benzinske: uvezi kit")]
        public static void KitMenu() => Debug.Log(ImportKit());

        static Vector3 GDir(double dx, double dz) => new Vector3((float)dx, 0, (float)-dz).normalized; // game direction -> Unity
        static float Yaw(Vector3 fwd) => Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;

        static bool Skip(Collider c)
        {
            for (var t = c.transform; t; t = t.parent)
                if (t.parent && t.parent.name == "World" && (t.name == "Benzinske" || t.name == "Props" || t.name == "Buildings" || t.name == "ParkedCars" || t.name == "Stanice" || t.name == "Detalji")) return true;
            return c.attachedRigidbody;
        }
        /// the ground (terrain / road / forecourt) under a Unity point: the highest non-building, non-prop hit
        static float Ground(Vector3 p)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, 500, p.z), Vector3.down, 1000, ~0, QueryTriggerInteraction.Ignore);
            float y = float.NaN, best = 1e9f;
            foreach (var h in hits) if (!Skip(h.collider) && h.distance < best) { best = h.distance; y = h.point.y; }
            return float.IsNaN(y) ? (float)Tiles.Height(p.x + W.OX, -p.z + W.OZ) : y;
        }

        static Site cur;
        static bool InBay(Vector3 p, float r)
        {
            foreach (var b0 in bays)
            {
                var b = b0;
                if (cur != null && !double.IsNaN(cur.bayHw) && (new Vector2(b.c.x, b.c.z) - new Vector2(p.x, p.z)).magnitude < 60) b.hw = Mathf.Min(b.hw, (float)cur.bayHw);
                var d = new Vector2(p.x - b.c.x, p.z - b.c.z); var f = b.dir.sqrMagnitude > 0 ? b.dir.normalized : Vector2.up;
                float along = Mathf.Abs(Vector2.Dot(d, f)), side = Mathf.Abs(d.x * f.y - d.y * f.x);
                if (along < b.hl + r && side < b.hw + r) return true;
            }
            return false;
        }

        static (Vector3 c, Vector2 dir, float hl, float hw) BayAt(Vector3 p, float r)
        {
            foreach (var b in bays)
            {
                var d = new Vector2(p.x - b.c.x, p.z - b.c.z); var f = b.dir.sqrMagnitude > 0 ? b.dir.normalized : Vector2.up;
                if (Mathf.Abs(Vector2.Dot(d, f)) < b.hl + r && Mathf.Abs(d.x * f.y - d.y * f.x) < b.hw + r) return b;
            }
            return bays[0];
        }

        static GameObject Put(Transform parent, string part, string brand, Vector3 p, float yaw, string name = null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Prefab(part, brand), parent);
            go.transform.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
            if (name != null) go.name = name;
            go.isStatic = true; foreach (Transform c in go.transform) c.gameObject.isStatic = true;
            return go;
        }

        public static string Place()
        {
            log.Clear(); prefabs.Clear(); mats.Clear(); sites = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var list = Sites(); if (list.Count == 0) return "[benzinske] nema " + DATA;
            ImportKit();
            // the package part without the replaced canopies / shops / totems (and so without their pump spots)
            log.Add("  " + BuildingsFromTiles.RebuildPackage());
            Physics.SyncTransforms();
            var game = Resources.Load<Magistrala.Game.GameWorld>(Magistrala.Game.GameWorld.ResourcePath);
            bays = game ? game.stops.Select(s => (s.bay, s.bayDir, s.bayHl, s.bayHw)).ToList() : new List<(Vector3, Vector2, float, float)>();
            root = WorldBuilder.Root("Benzinske").transform;
            int nParts = 0;
            foreach (var s in list)
            {
                if (s.tiles.Any(Kasteli.ExcludedTile) || Kasteli.Excluded(
                    new List<BuildingsFromTiles.Vector2d> { new BuildingsFromTiles.Vector2d(s.cx, s.cz) }, s.cx, s.cz)) continue;
                var g = new GameObject($"{s.id} {s.name} ({s.brand})").transform; g.SetParent(root, false); g.gameObject.isStatic = true;
                cur = s;
                if (!double.IsNaN(s.px)) Pad(g, s);
                var C = W.U(s.cx, 0, s.cz); var A = GDir(Math.Cos(s.ca), Math.Sin(s.ca)); var N = Vector3.Cross(Vector3.up, A);
                // the old Tripo dispensers of the package canopy (World/Props/benzinske) within the canopy
                var tp = GameObject.Find("World/Props/benzinske");
                if (tp) for (int i = tp.transform.childCount - 1; i >= 0; i--)
                    { var c = tp.transform.GetChild(i); var d = c.position - C; d.y = 0; if (Mathf.Abs(Vector3.Dot(d, A)) < 5 * s.n + 4 && Mathf.Abs(Vector3.Dot(d, N)) < 8) UnityEngine.Object.DestroyImmediate(c.gameObject); }
                // ground: the islands' mean (the forecourt is near flat); the canopy stands on it
                var ys = new List<float>();
                for (int i = 0; i < s.n; i++) ys.Add(Ground(C + A * ((i - (s.n - 1) / 2f) * 10)));
                float y0 = ys.Average();
                string cp = "canopy" + Mathf.Clamp(s.n, 2, 4);
                // the canopy part is built along its local x (Blender X); glTFast mirrors x, which a symmetric canopy does not mind
                Put(g, cp, s.brand, new Vector3(C.x, y0, C.z), Yaw(A) - 90, "nadstresnica"); nParts++;
                var pumpB = sizes.TryGetValue("pump", out var pbnd) ? pbnd : new Bounds(Vector3.zero, Vector3.one);
                // the brand fin is at the pump's +x (Blender) end; after the import it is where the bounds reach further
                float fin = pumpB.max.x > -pumpB.min.x ? 1 : -1;
                int nIsl = 0, nPump = 0;
                for (int i = 0; i < s.n; i++)
                {
                    if (s.skipIslands.Contains(i)) continue;
                    var ic = C + A * ((i - (s.n - 1) / 2f) * 10);
                    if (InBay(ic, 3.2f - 1.4f))
                    {
                        // the job's bay crosses this island: a diesel pump on a short island beside the bay instead (the truck refuels there)
                        var tb = BayAt(ic, 3.2f - 1.4f);
                        var bd = new Vector3(tb.dir.x, 0, tb.dir.y).normalized; var bn = Vector3.Cross(Vector3.up, bd);
                        if (Vector3.Dot(C - tb.c, bn) < 0) bn = -bn;           // the side toward the canopy centre
                        var tc = tb.c + bn * (tb.hw + 1.25f); tc.y = 0;
                        float yt = Ground(tc);
                        Put(g, "island", s.brand, new Vector3(tc.x, yt, tc.z), Yaw(bd) - 90, "otok kamioni");
                        var rf = bd * fin; Put(g, "pump", s.brand, new Vector3(tc.x, yt + .2f, tc.z) + bd * 1.8f, Yaw(Vector3.Cross(rf, Vector3.up)), "aparat kamioni");
                        log.Add($"  {s.id}: otok {i} zamijenjen otokom uz mjesto točenja posla"); nIsl++; nPump++;
                        continue;
                    }
                    float yi = Ground(ic);
                    Put(g, "island", s.brand, new Vector3(ic.x, yi, ic.z), Yaw(A) - 90, $"otok {i}"); nIsl++;
                    // two dispensers, the brand fins outward (away from the pillar); the LPG one on the last island's far end
                    for (int e = -1; e <= 1; e += 2)
                    {
                        var pc = ic + A * (1.8f * e);
                        bool lpg = s.lpg && i == s.n - 1 && e == 1;
                        // local +x of the part (after the mirror: fin side) must point along e * A
                        var right = A * e * fin; var fwd = Vector3.Cross(right, Vector3.up);
                        Put(g, lpg ? "pump_lpg" : "pump", s.brand, new Vector3(pc.x, yi + .2f, pc.z), Yaw(fwd), lpg ? "autoplin" : "aparat"); nPump++;
                    }
                    // a washer bucket at one island end
                    var wp = ic + A * 3.4f + N * .2f;
                    if (!InBay(wp, .3f)) Put(g, "washer", s.brand, new Vector3(wp.x, Ground(wp), wp.z), Yaw(A), "brisaci");
                }
                nParts += nIsl + nPump;
                // shop: its front (glass) to the canopy
                if (!double.IsNaN(s.sx))
                {
                    var sc = W.U(s.sx, 0, s.sz); var f = GDir(s.sfx - s.sx, s.sfz - s.sz);
                    string part = "shop" + s.shop;
                    float ys0 = Ground(sc);
                    var front = ShopFront(part);
                    var shopGo = Put(g, part, s.brand, new Vector3(sc.x, ys0, sc.z), Yaw(f * front), "trgovina"); nParts++;
                    nParts += ShopProps(g, s, sc, f, part);
                }
                if (!double.IsNaN(s.tx))
                {
                    var tc = W.U(s.tx, 0, s.tz); var f = GDir(s.tfx - s.tx, s.tfz - s.tz);
                    // the totem's faces are its ±y (Blender): turned so a face looks along f
                    Put(g, "totem", s.brand, new Vector3(tc.x, Ground(tc), tc.z), Yaw(f), "totem-cijene"); nParts++;
                }
                if (!double.IsNaN(s.hx))
                {
                    var hc = W.U(s.hx, 0, s.hz); var f = GDir(s.hfx - s.hx, s.hfz - s.hz);
                    Put(g, "hut", s.brand, new Vector3(hc.x, Ground(hc), hc.z), Yaw(f * ShopFront("shopS")), "pomocna-zgrada"); nParts++;
                    var aw = hc + Vector3.Cross(Vector3.up, f) * 2.9f + f * .6f;
                    Put(g, "airwater", s.brand, new Vector3(aw.x, Ground(aw), aw.z), Yaw(f * ShopFront("shopS")), "zrak-voda"); nParts++;
                }
                if (!double.IsNaN(s.lx))
                {
                    var lc = W.U(s.lx, 0, s.lz); var a = GDir(Math.Cos(s.la), Math.Sin(s.la));
                    Put(g, "lpgtank", s.brand, new Vector3(lc.x, Ground(lc), lc.z), Yaw(a) - 90, "spremnik-plina"); nParts++;
                }
                log.Add($"  {s.id} {s.name}: {s.brand}, {cp}, {nIsl} otoka, {nPump} aparata" + (s.note.Length > 0 ? $" – {s.note}" : ""));
            }
            int tris = 0; foreach (var lg in root.GetComponentsInChildren<LODGroup>()) { var l = lg.GetLODs(); if (l.Length > 0) foreach (var r in l[0].renderers) if (r && r.GetComponent<MeshFilter>()) tris += r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3; }
            cur = null;
            log.Insert(0, $"[benzinske] {list.Count} benzinskih, {nParts} dijelova, LOD0 {tris} trokuta ukupno; {sw.ElapsedMilliseconds / 1000.0:F1} s");
            return string.Join("\n", log);
        }

        /// a forecourt where the world has bare ground: one asphalt slab (corners on the ground + 4 cm, edges a little sunk), AsphaltPBR
        static void Pad(Transform g, Site s)
        {
            var C = W.U(s.px, 0, s.pz); var A = GDir(Math.Cos(s.pa), Math.Sin(s.pa)); var N = Vector3.Cross(Vector3.up, A);
            int nu = Mathf.Max(2, (int)(s.pl / 2)), nv = Mathf.Max(2, (int)(s.pw / 2));
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var ix = new List<int>();
            for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    float a = ((float)i / nu - .5f) * (float)s.pl, b = ((float)j / nv - .5f) * (float)s.pw;
                    var p = C + A * a + N * b; bool edge = i == 0 || j == 0 || i == nu || j == nv;
                    p.y = Ground(p) + (edge ? -.02f : .04f);
                    v.Add(p - C); uv.Add(new Vector2(a, b) / 4f);
                }
            for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int k = j * (nu + 1) + i;
                    ix.AddRange(new[] { k, k + nu + 1, k + 1, k + 1, k + nu + 1, k + nu + 2 });
                }
            var m = new Mesh { name = "plato-" + s.id }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(ix, 0);
            // winding: up-facing
            m.RecalculateNormals(); if (m.normals[0].y < 0) { ix.Reverse(); m.SetTriangles(ix, 0); m.RecalculateNormals(); }
            m.RecalculateTangents(); m.RecalculateBounds();
            Dir(GEN); string path = $"{GEN}/plato-{s.id}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old) { EditorUtility.CopySerialized(m, old); UnityEngine.Object.DestroyImmediate(m); m = old; } else AssetDatabase.CreateAsset(m, path);
            var go = new GameObject("plato"); go.transform.SetParent(g, false); go.transform.position = C; go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.GEN + "/Materials/AsphaltPBR.mat");
            go.AddComponent<MeshCollider>().sharedMesh = m;
        }

        /// the shop's glass front is at Blender -Y; after the import it is +z or -z of the prefab: the sign that turns f into the yaw
        static float ShopFront(string part)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{KIT}/{part}.glb");
            foreach (var mf in src.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.name.EndsWith("_LOD0") && !(mf.transform.parent && mf.transform.parent.name.EndsWith("_LOD0"))) continue;
                var mr = mf.GetComponent<MeshRenderer>(); var mesh = mf.sharedMesh;
                for (int k = 0; k < mesh.subMeshCount && k < mr.sharedMaterials.Length; k++)
                {
                    if (SlotName(mr.sharedMaterials[k]) != "glass") continue;
                    var idx = mesh.GetTriangles(k); var v = mesh.vertices; float z = 0;
                    foreach (int i in idx) z += mf.transform.TransformPoint(v[i]).z - src.transform.position.z;
                    // most glass is the front: its mean z sign is the front
                    return z >= 0 ? 1 : -1;
                }
            }
            return 1;
        }

        /// in front of the shop: freezer, gas cage, a pallet of water, crates, bin, yellow bollards; a dumpster behind it
        static int ShopProps(Transform g, Site s, Vector3 sc, Vector3 f, string part)
        {
            var b = sizes.TryGetValue(part, out var bb) ? bb : new Bounds(Vector3.zero, new Vector3(8, 4, 7));
            float hw = Mathf.Max(b.size.x, b.size.z) / 2, hd = Mathf.Min(b.size.x, b.size.z) / 2;
            if (part == "shopS") { hw = 4; hd = 3.6f; } else { hw = 6.5f; hd = 5f; }
            var t = Vector3.Cross(Vector3.up, f); int n = 0;
            int h = 0; foreach (char ch in s.id) h = h * 31 + ch; h &= 0x7fffffff;
            float side = (h & 1) == 0 ? 1 : -1;
            var items = new (string p, float u, float d, float yaw)[]
            {
                ("freezer", -hw + 1.3f, .9f, 0), ("cage", -hw + 3.0f, .8f, 0), ("bin", -hw + 4.1f, .7f, 0),
                ("pallet", hw - 1.6f, 1.0f, 0), ("crates", hw - 3.3f, .9f, 0),
            };
            foreach (var it in items)
            {
                var p = sc + f * (hd + it.d) + t * it.u * side;
                if (InBay(p, .8f)) continue;
                float fs = ShopFront("shopS"); // every part's front is Blender -Y: the same sign
                Put(g, it.p, s.brand, new Vector3(p.x, Ground(p), p.z), Yaw((it.p == "pallet" || it.p == "crates" ? t : f) * fs), it.p); n++;
            }
            // bollards along the front kerb (protecting the glass)
            for (float u = -hw + .8f; u <= hw - .8f; u += 2.3f)
            {
                var p = sc + f * (hd + 2.1f) + t * u;
                if (InBay(p, .3f)) continue;
                Put(g, "bollard", s.brand, new Vector3(p.x, Ground(p), p.z), 0, "stupic"); n++;
            }
            var dp = sc - f * (hd + 1.4f) + t * (hw - 1.5f) * -side;
            if (!InBay(dp, .5f)) { Put(g, "dumpster", s.brand, new Vector3(dp.x, Ground(dp), dp.z), Yaw(t), "kontejner"); n++; }
            return n;
        }
    }
}
