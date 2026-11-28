// Kašteli (landmarks): the Kaštela castles as hand-built models (Blender, BalkanDriver tools/world/kasteli-blender, from the
// OSM / DGU footprints of tools/world/riva-extras/kasteli.json and free reference photos) instead of generated houses.
//   Assets/Models/Kasteli/<name>.glb      LOD0 / LOD1 / LOD2 children, named materials, no embedded textures
//   Assets/Textures/Kasteli/*.jpg          shared tileable textures (Poly Haven CC0, toned to Dalmatian limestone)
//   Assets/Editor/kasteli-postava.json     origin (game x, z of the model's 0), height mode, exclusion footprints
// Menu Magistrala/Kašteli: postavi -> World/Kasteli/<name>: URP Lit materials by material name, LODGroup, one MeshCollider (LOD2
// hull), static. Build tile buildings skips every generated house on an exclusion footprint (Excluded(), BuildingsFromTiles).
// glTFast imports glTF with x negated, so the models (Blender X east, Y north) are turned 180 degrees about y.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class Kasteli
    {
        const string DATA = "Assets/Editor/kasteli-postava.json";
        const string MODELS = "Assets/Models/Kasteli/";
        const string TEX = "Assets/Textures/Kasteli/";
        const string MATS = "Assets/Generated/Kasteli/Materials/";

        // material name -> (texture, colour multiplier, smoothness); UVs are already in texture tiles
        static readonly Dictionary<string, (string tex, Color col, float smooth)> Look = new Dictionary<string, (string, Color, float)>
        {
            { "kamen", ("kamen", new Color(1f, .97f, .91f), .12f) },
            { "lomljeni", ("lomljeni", new Color(1f, .96f, .89f), .08f) },
            { "zbuka", ("zbuka", new Color(.92f, .82f, .70f), .08f) },
            { "crijep", ("crijep", Color.white, .2f) },
            { "ploce", ("kamen", new Color(.68f, .67f, .65f), .08f) },
            { "okvir", ("kamen", new Color(1.06f, 1.03f, .98f), .2f) },
            { "otvor", (null, new Color(.025f, .025f, .028f), .6f) },
            { "skure_z", (null, new Color(.10f, .22f, .13f), .3f) },
            { "skure_s", (null, new Color(.22f, .12f, .06f), .3f) },
            { "drvo", (null, new Color(.20f, .12f, .07f), .25f) },
            { "metal", (null, new Color(.06f, .06f, .065f), .5f) },
            { "kamen_vlazni", ("kamen", new Color(.62f, .62f, .58f), .25f) },
        };

        sealed class Rec { public HashSet<string> ids = new HashSet<string>(); public string name, glb, mode; public double ox, oz, y; public List<List<BuildingsFromTiles.Vector2d>> ex = new List<List<BuildingsFromTiles.Vector2d>>(); }

        static List<Rec> cache;
        static List<Rec> Load()
        {
            if (cache != null) return cache;
            cache = new List<Rec>();
            if (!File.Exists(DATA)) return cache;
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(DATA)));
            foreach (var o in MiniJson.L(root["kasteli"]))
            {
                var d = MiniJson.O(o); var org = MiniJson.L(d["origin"]);
                var r = new Rec { name = (string)d["name"], glb = (string)d["glb"], mode = (string)d["base"], ox = MiniJson.D(org[0]), oz = MiniJson.D(org[1]), y = MiniJson.D(MiniJson.G(d, "y") ?? 0.0) };
                if (MiniJson.G(d, "excludeIds") is List<object> ids) foreach (var x in ids) r.ids.Add((string)x);
                foreach (var ring in MiniJson.L(d["exclude"]))
                {
                    var P = new List<BuildingsFromTiles.Vector2d>();
                    foreach (var p in MiniJson.L(ring)) { var q = MiniJson.L(p); P.Add(new BuildingsFromTiles.Vector2d(MiniJson.D(q[0]), MiniJson.D(q[1]))); }
                    r.ex.Add(P);
                }
                cache.Add(r);
            }
            return cache;
        }

        static bool In(List<BuildingsFromTiles.Vector2d> P, double x, double z)
        {
            bool c = false;
            for (int i = 0, j = P.Count - 1; i < P.Count; j = i++)
                if ((P[i].z > z) != (P[j].z > z) && x < P[i].x + (z - P[i].z) * (P[j].x - P[i].x) / (P[j].z - P[i].z)) c = !c;
            return c;
        }

        /// a generated house (game footprint P, centre cx, cz, tile id) that stands on a castle: listed in excludeIds, or its centre or half its corners on a footprint
        public static bool Excluded(List<BuildingsFromTiles.Vector2d> P, double cx, double cz, string id = null)
        {
            foreach (var r in Load())
            {
                if (id != null && r.ids.Contains(id)) return true;
                foreach (var E in r.ex)
                {
                    if (In(E, cx, cz)) return true;
                    int k = 0; foreach (var p in P) if (In(E, p.x, p.z)) k++;
                    if (k * 2 >= P.Count) return true;
                }
            }
            return false;
        }

        /// Test a replacement's source tile as well as its eventual model footprint.
        public static bool ExcludedTile(string id)
        {
            if (id == null) return false;
            foreach (var r in Load()) if (r.ids.Contains(id)) return true;
            var parts = id.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[1], out int index)) return false;
            var xy = parts[0].Split('_');
            if (xy.Length != 2 || !int.TryParse(xy[0], out int tx) || !int.TryParse(xy[1], out int tz)) return false;
            var tile = Tiles.Get(tx, tz); if (tile?.buildingsJson == null) return false;
            var buildings = MiniJson.L(MiniJson.Parse(tile.buildingsJson));
            if (index < 0 || index >= buildings.Count) return false;
            var points = MiniJson.L(MiniJson.O(buildings[index])["pts"]);
            var foot = new List<BuildingsFromTiles.Vector2d>();
            double x = 0, z = 0;
            foreach (var point in points)
            {
                var q = MiniJson.L(point); double px = MiniJson.D(q[0]), pz = MiniJson.D(q[1]);
                foot.Add(new BuildingsFromTiles.Vector2d(px, pz)); x += px; z += pz;
            }
            if (foot.Count > 1 && foot[0].x == foot[foot.Count - 1].x && foot[0].z == foot[foot.Count - 1].z)
            {
                x -= foot[foot.Count - 1].x; z -= foot[foot.Count - 1].z; foot.RemoveAt(foot.Count - 1);
            }
            return foot.Count >= 3 && Excluded(foot, x / foot.Count, z / foot.Count, id);
        }

        static Material Mat(string name)
        {
            if (!Directory.Exists(MATS)) Directory.CreateDirectory(MATS);
            string path = MATS + "kastel_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            if (!Look.TryGetValue(name, out var l)) l = (null, Color.gray, .1f);
            if (l.tex != null)
            {
                m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TEX + l.tex + "_d.jpg"));
                var nPath = TEX + l.tex + "_n.jpg";
                var imp = AssetImporter.GetAtPath(nPath) as TextureImporter;
                if (imp && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; imp.SaveAndReimport(); }
                m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(nPath)); m.SetFloat("_BumpScale", .9f); m.EnableKeyword("_NORMALMAP");
            }
            m.SetColor("_BaseColor", l.col); m.SetFloat("_Smoothness", l.smooth); m.SetFloat("_Metallic", 0);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        [MenuItem("Magistrala/Kašteli: postavi")]
        public static void PlaceMenu() => Debug.Log(Place());

        public static string Place()
        {
            MainScene.Ensure();
            cache = null;
            var root = WorldBuilder.Root("Kasteli");
            var models = WorldData.Models();
            var sb = new System.Text.StringBuilder("[Magistrala] kašteli:");
            var mats = new Dictionary<string, Material>();
            foreach (var r in Load())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MODELS + r.glb + ".glb");
                if (!prefab) { sb.Append($" {r.name}: no model;"); continue; }
                double y = r.y;
                if (r.mode == "ground")
                {
                    double s = 0; int n = 0;
                    foreach (var E in r.ex) foreach (var p in E) { s += BuildingsFromTiles.Ground(models, p.x, p.z); n++; }
                    y += n > 0 ? s / n : 0;
                }
                var go = new GameObject(r.name); go.transform.SetParent(root.transform, false);
                go.transform.position = W.U(r.ox, y, r.oz);
                go.transform.rotation = Quaternion.Euler(0, 180, 0);
                var lods = new List<LOD>(); Mesh hull = null; int tris = 0;
                for (int k = 0; k < 3; k++)
                {
                    var src = prefab.transform.Find(r.glb + "_LOD" + k);
                    if (!src) continue;
                    var mf0 = src.GetComponent<MeshFilter>(); var mr0 = src.GetComponent<MeshRenderer>();
                    var c = new GameObject("LOD" + k); c.transform.SetParent(go.transform, false);
                    c.AddComponent<MeshFilter>().sharedMesh = mf0.sharedMesh;
                    var mr = c.AddComponent<MeshRenderer>();
                    var ms = new Material[mr0.sharedMaterials.Length];
                    for (int i = 0; i < ms.Length; i++)
                    {
                        string mn = mr0.sharedMaterials[i] ? mr0.sharedMaterials[i].name : "kamen";
                        if (!mats.TryGetValue(mn, out var m)) mats[mn] = m = Mat(mn);
                        ms[i] = m;
                    }
                    mr.sharedMaterials = ms;
                    mr.shadowCastingMode = k == 2 ? ShadowCastingMode.On : ShadowCastingMode.On;
                    mr.receiveShadows = true;
                    lods.Add(new LOD(k == 0 ? .16f : k == 1 ? .05f : .008f, new Renderer[] { mr }));
                    if (k == 0) tris = mf0.sharedMesh.triangles.Length / 3;
                    if (k == 2) hull = mf0.sharedMesh;
                    GameObjectUtility.SetStaticEditorFlags(c, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                }
                var lg = go.AddComponent<LODGroup>(); lg.SetLODs(lods.ToArray()); lg.RecalculateBounds();
                if (hull)
                {
                    var col = new GameObject("col"); col.transform.SetParent(go.transform, false);
                    col.AddComponent<MeshCollider>().sharedMesh = hull;
                    col.isStatic = true;
                }
                sb.Append($" {r.name} y={y:F2} tris={tris};");
            }
            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            return sb.ToString();
        }

        /// which tile footprints (tx_tz:index) each castle's exclusion removes (no rebuild; orthophoto-only houses not counted)
        public static string Audit()
        {
            cache = null;
            var hit = new Dictionary<string, List<string>>();
            for (int tz = WorldBuilder.TZ0; tz <= WorldBuilder.TZ1; tz++)
                for (int tx = WorldBuilder.TX0; tx <= WorldBuilder.TX1; tx++)
                {
                    var tile = Tiles.Get(tx, tz); if (tile?.buildingsJson == null) continue;
                    var list = MiniJson.L(MiniJson.Parse(tile.buildingsJson));
                    for (int bi = 0; bi < list.Count; bi++)
                    {
                        var P = new List<BuildingsFromTiles.Vector2d>(); double cx = 0, cz = 0;
                        foreach (var q0 in MiniJson.L(MiniJson.O(list[bi])["pts"])) { var q = MiniJson.L(q0); P.Add(new BuildingsFromTiles.Vector2d(MiniJson.D(q[0]), MiniJson.D(q[1]))); }
                        if (P.Count < 3) continue;
                        foreach (var p in P) { cx += p.x; cz += p.z; } cx /= P.Count; cz /= P.Count;
                        foreach (var r in Load())
                        {
                            bool ex = false;
                            foreach (var E in r.ex) { if (In(E, cx, cz)) ex = true; else { int k = 0; foreach (var p in P) if (In(E, p.x, p.z)) k++; if (k * 2 >= P.Count) ex = true; } }
                            if (ex) { if (!hit.TryGetValue(r.name, out var l)) hit[r.name] = l = new List<string>(); l.Add($"{tx}_{tz}:{bi}"); }
                        }
                    }
                }
            var sb = new System.Text.StringBuilder();
            foreach (var kv in hit) sb.Append(kv.Key + ": " + string.Join(",", kv.Value) + "; ");
            return sb.ToString();
        }

        /// editor check: a camera at game (x, z), h m above the ground there, looking at game (tx, tz, th above ground) -> PNG
        public static string Shot(string path, double x, double z, double h, double tx, double tz, double th, float fov = 50, int w = 1600, int hgt = 900)
        {
            var models = WorldData.Models();
            double g = Math.Max(0, BuildingsFromTiles.Ground(models, x, z)), tg = Math.Max(0, BuildingsFromTiles.Ground(models, tx, tz));
            var go = new GameObject("kasteli_shot"); var cam = go.AddComponent<Camera>();
            try
            {
                var main = Camera.main;
                if (main) { cam.CopyFrom(main); }
                cam.fieldOfView = fov; cam.nearClipPlane = .3f; cam.farClipPlane = 6000;
                go.transform.position = W.U(x, g + h, z);
                go.transform.LookAt(W.U(tx, tg + th, tz));
                var rt = new RenderTexture(w, hgt, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
                cam.targetTexture = rt; cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, hgt, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, hgt), 0, 0); tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
                RenderTexture.active = null; cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
                return path;
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
