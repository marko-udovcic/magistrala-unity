// Paket2: the second use of the UKRAINIAN CITY pack, limited to the towns listed in Assets/Editor/paket2-podrucja.json (game x / z
// boxes; first the Kaštel Lukšić pilot, 28. 11.). Inside an area:
//   krov    the houses' roofs take the pack's barrel tiles RoofTiles_A (BuildingTextures.PRoofPack, Magistrala/Facade roof variant)
//   zbuka   more houses take the pack's light plasters Plaster_H / J and its dressed stone Brick_E (HouseBuilder Look.Finish)
//   vrata   the front doors are the pack's wooden doors (PaketDetalji "vrata_*") instead of our louvred leaf
//   ograde  the yards' street gates get the pack's metal gates, the wire fences on the low walls its picket fence (Yards)
//   plocnik the sidewalks take the pack's concrete slabs Pavement_A (StreetsBuilder MAT_WALK2), the old cores' paved fronts too
// Every feature reads In(gx, gz, feature): off in the json = the houses / streets exactly as before (same random draws).
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class Paket2
    {
        public const string JSON = "Assets/Editor/paket2-podrucja.json";
        sealed class Box { public string name; public double x0, x1, z0, z1; }
        static List<Box> boxes; static Dictionary<string, bool> features;
        static DateTime stamp;

        static void Load()
        {
            var t = File.Exists(JSON) ? File.GetLastWriteTimeUtc(JSON) : DateTime.MinValue;
            if (boxes != null && t == stamp) return;
            stamp = t; boxes = new List<Box>(); features = new Dictionary<string, bool>();
            if (!File.Exists(JSON)) return;
            var o = MiniJson.O(MiniJson.Parse(File.ReadAllText(JSON)));
            if (o == null || !(MiniJson.G(o, "on") is bool on && on)) return;
            foreach (var f in new[] { "krov", "zbuka", "vrata", "ograde", "plocnik" }) features[f] = !(MiniJson.G(o, f) is bool b) || b;
            foreach (var e in MiniJson.L(MiniJson.G(o, "podrucja")) ?? new List<object>())
            {
                var d = MiniJson.O(e); if (d == null || MiniJson.G(d, "on") is bool bo && !bo) continue;
                boxes.Add(new Box { name = MiniJson.G(d, "ime") as string, x0 = MiniJson.D(MiniJson.G(d, "x0")), x1 = MiniJson.D(MiniJson.G(d, "x1")), z0 = MiniJson.D(MiniJson.G(d, "z0")), z1 = MiniJson.D(MiniJson.G(d, "z1")) });
            }
        }

        /// game (x, z) inside an enabled area, with the feature on (null: any feature)
        public static bool In(double gx, double gz, string feature = null)
        {
            Load();
            if (boxes.Count == 0 || feature != null && !(features.TryGetValue(feature, out var on) && on)) return false;
            foreach (var b in boxes) if (gx >= b.x0 && gx <= b.x1 && gz >= b.z0 && gz <= b.z1) return true;
            return false;
        }
        /// the same for a Unity position
        public static bool InU(Vector3 u, string feature = null) { var g = W.G(u); return In(g.x, g.y, feature); }

        public const string TEX = "Assets/Textures/Paket2/";
        /// one-off from the extracted pack: the sidewalk slabs (Pavement_A, 1024², the pack's AO folded in) as committed jpgs
        /// (StreetsBuilder MAT_WALK2), and the roof layer of the plaster strip (BuildingTextures.PackRoof)
        [UnityEditor.MenuItem("Magistrala/Paket2: teksture iz paketa (plocnik, krov)")]
        public static void TexturesMenu() => Debug.Log(Textures());
        public static string Textures()
        {
            const string P = PaketZgrade.SRC + "Textures/Pavement_A/Pavement_A";
            if (!File.Exists(P + "_BaseMap.png")) return P + "_BaseMap.png missing (extract the pack: docs/paket2.md)";
            Directory.CreateDirectory(TEX);
            const int S = 1024;
            var a = PaketZgrade.Read(P + "_BaseMap.png", S, new Color32(128, 128, 128, 255));
            var n = PaketZgrade.Read(P + "_Normal.png", S, new Color32(128, 128, 255, 255));
            var m = PaketZgrade.Read(P + "_MaskMap.png", S, new Color32(0, 255, 0, 64));
            for (int i = 0; i < a.Length; i++) { float ao = Mathf.Lerp(1, m[i].g / 255f, .6f); a[i] = new Color32((byte)(a[i].r * ao), (byte)(a[i].g * ao), (byte)(a[i].b * ao), 255); }
            foreach (var (px, name) in new[] { (a, "plocnik_d.jpg"), (n, "plocnik_n.jpg") })
            {
                var t = new Texture2D(S, S, TextureFormat.RGB24, false); t.SetPixels32(px); t.Apply();
                File.WriteAllBytes(TEX + name, t.EncodeToJPG(92)); UnityEngine.Object.DestroyImmediate(t);
            }
            UnityEditor.AssetDatabase.Refresh();
            if (UnityEditor.AssetImporter.GetAtPath(TEX + "plocnik_n.jpg") is UnityEditor.TextureImporter ti) { ti.textureType = UnityEditor.TextureImporterType.NormalMap; ti.maxTextureSize = 1024; ti.SaveAndReimport(); }
            if (UnityEditor.AssetImporter.GetAtPath(TEX + "plocnik_d.jpg") is UnityEditor.TextureImporter td) { td.maxTextureSize = 1024; td.anisoLevel = 4; td.SaveAndReimport(); }
            return "Paket2 textures: " + TEX + "plocnik_d/n.jpg; " + BuildingTextures.PackRoof();
        }

        /// the road / junction models (World/Roads) whose centre lies in an area ("plocnik"): their sidewalk paving ("Paving",
        /// 5 m per uv) takes the pack's slabs; called at the end of StreetsBuilder.Build (after Build roads). Idempotent.
        public static string Roads()
        {
            var root = GameObject.Find("World/Roads"); if (!root) return "Paket2 roads: no World/Roads";
            var src = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.GEN + "/Materials/Paving.mat");
            var tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(TEX + "plocnik_d.jpg");
            if (!src || !tex) return "Paket2 roads: no Paving.mat / " + TEX + "plocnik_d.jpg";
            string path = WorldBuilder.GEN + "/Materials/PavingPaket.mat";
            var m = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(src); UnityEditor.AssetDatabase.CreateAsset(m, path); } else m.CopyPropertiesFromMaterial(src);
            m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", new Vector2(1.25f, 1.25f));
            m.SetTexture("_BumpMap", UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(TEX + "plocnik_n.jpg")); m.SetTextureScale("_BumpMap", new Vector2(1.25f, 1.25f));
            m.SetFloat("_BumpScale", 1); m.EnableKeyword("_NORMALMAP"); m.SetColor("_BaseColor", new Color(1.12f, 1.11f, 1.09f)); UnityEditor.EditorUtility.SetDirty(m);
            int n = 0; var names = new List<string>();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool inArea = InU(r.bounds.center, "plocnik"); var mats = r.sharedMaterials; bool ch = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (inArea && mats[i] == src) { mats[i] = m; ch = true; }
                    else if (!inArea && mats[i] == m) { mats[i] = src; ch = true; }
                }
                if (ch) { r.sharedMaterials = mats; if (inArea) { n++; names.Add(r.name); } }
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(root.scene);
            return $"Paket2 roads: {n} road models paved with the pack's slabs ({string.Join(", ", names)})";
        }

        public static string Describe()
        {
            Load(); var s = new System.Text.StringBuilder("Paket2 areas: ");
            foreach (var b in boxes) s.Append($"{b.name} x {b.x0}..{b.x1} z {b.z0}..{b.z1}; ");
            foreach (var kv in features) s.Append(kv.Key).Append('=').Append(kv.Value).Append(' ');
            return s.ToString();
        }
    }
}
#endif
