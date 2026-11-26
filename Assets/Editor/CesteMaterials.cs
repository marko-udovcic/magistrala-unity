using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    /// <summary>Optional CC0 surfaces. No scene scan or runtime texture loading.</summary>
    public static class CesteMaterials
    {
        const string DIR = "Assets/Textures/Ceste/";
        static readonly HashSet<string> warned = new HashSet<string>();

        public static Texture2D Texture(string name, bool normal = false, bool linear = false)
        {
            string path = null;
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg" })
                if (File.Exists(DIR + name + ext)) { path = DIR + name + ext; break; }
            if (path == null)
            {
                if (warned.Add(name)) Debug.LogWarning("[Ceste] Nedostaje " + DIR + name + " — zadržan postojeći izgled.");
                return null;
            }
            AssetDatabase.ImportAsset(path);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                var kind = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                bool srgb = !normal && !linear;
                if (imp.textureType != kind || imp.sRGBTexture != srgb || !imp.mipmapEnabled || imp.anisoLevel < 4)
                {
                    imp.textureType = kind; imp.sRGBTexture = srgb; imp.mipmapEnabled = true; imp.anisoLevel = 4;
                    imp.SaveAndReimport();
                }
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // Supports the CC0 manifest sizeMeters field, plus size_m for older manifests.
        // Unknown manifest fields are harmless; built-in metre defaults remain usable.
        public static Vector2 Size(string name, float fallback)
        {
            string path = DIR + "manifest.json";
            if (!File.Exists(path)) return Vector2.one * fallback;
            try
            {
                var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(path)));
                object entry = null;
                if (root.TryGetValue(name, out var direct)) entry = direct;
                else if (root.TryGetValue("textures", out var textures))
                {
                    if (textures is Dictionary<string, object> dict) dict.TryGetValue(name, out entry);
                    else foreach (var item in MiniJson.L(textures))
                    {
                        var row = MiniJson.O(item);
                        if (row.TryGetValue("name", out var key) && (string)key == name) { entry = row; break; }
                    }
                }
                if (entry is Dictionary<string, object> e && (e.TryGetValue("sizeMeters", out var size) || e.TryGetValue("size_m", out size)))
                {
                    if (size is double n && n > 0) return Vector2.one * (float)n;
                    var v = MiniJson.L(size);
                    if (v.Count >= 2 && MiniJson.F(v[0]) > 0 && MiniJson.F(v[1]) > 0) return new Vector2(MiniJson.F(v[0]), MiniJson.F(v[1]));
                }
            }
            catch (Exception ex) { if (warned.Add("manifest")) Debug.LogWarning("[Ceste] manifest: " + ex.Message); }
            return Vector2.one * fallback;
        }

        static bool Bind(Material m, string property, string name, bool normal = false, bool linear = false)
        {
            var t = Texture(name, normal, linear); if (!t) return false;
            m.SetTexture(property, t); return true;
        }

        public static void Asphalt(Material m)
        {
            bool hasBase = Bind(m, "_BaseMap", "asfalt_d");
            m.SetFloat("_CesteBase", hasBase ? 1 : 0);
            if (hasBase) { Bind(m,"_BumpMap","asfalt_n",true); Bind(m,"_RoughMap","asfalt_r",false,true); m.SetFloat("_Tiling",Size("asfalt",2.2f).x); }
            m.SetVector("_CesteMaps", new Vector4(Bind(m,"_PatchMap","asfalt_zakrpa_d") ? 1 : 0,
                Bind(m,"_TarMap","pukotine_d") ? 1 : 0, Bind(m,"_OilMap","ulje_d") ? 1 : 0, Bind(m,"_DustMap","prasina_d") ? 1 : 0));
            m.SetVector("_CesteCovers",new Vector4(Bind(m,"_CoverMap","sahta_d") ? 1 : 0,Bind(m,"_DrainMap","resetka_d") ? 1 : 0,0,0));
            m.SetVector("_DetailSize",new Vector4(Size("asfalt_zakrpa",2).x,Size("pukotine",6).x,0,0));
            m.SetVector("_OverlaySize",new Vector4(Size("ulje",1.5f).x,Size("prasina",4).x,Size("sahta",.8f).x,Size("resetka",.5f).x));
            m.SetFloat("_TrackDarken",.10f); m.SetFloat("_TrackSmooth",.12f);
            EditorUtility.SetDirty(m);
        }

        public static Material Stone(Material fallback, string name, bool joints = true, bool waterline = false)
        {
            var d = Texture(name + "_d"); if (!d) return fallback;
            var shader = Shader.Find("Magistrala/CesteStone"); if (!shader) return fallback;
            string path = WorldBuilder.GEN + "/Materials/Ceste_" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m,path); }
            m.shader = shader; m.enableInstancing = true; m.SetTexture("_BaseMap",d);
            m.SetColor("_BaseColor",Color.white); m.SetFloat("_BumpScale",.65f);
            m.SetTexture("_BumpMap",Texture(name+"_n",true));
            // Island fill has no roughness map in the agreed texture contract.
            if (name != "otok") Bind(m,"_RoughMap",name+"_r",false,true);
            m.SetFloat("_Tiling",Size(name,name == "rubnjak" ? 1 : 2).x);
            m.SetFloat("_VerticalScale",name == "riva_rub" ? 2.5f : 1);
            m.SetFloat("_Slabs",joints ? 1 : 0); m.SetVector("_CellSize",name == "rubnjak" ? new Vector4(1,.3f,0,0) : new Vector4(.6f,.9f,0,0));
            m.SetFloat("_AlgaeTiling",waterline ? Size("alge",3).x : 3);
            m.SetFloat("_Waterline",waterline ? 1 : 0);
            m.SetFloat("_AlgaeAvailable",waterline && Bind(m,"_AlgaeMap","alge_d") ? 1 : 0);
            EditorUtility.SetDirty(m); return m;
        }

        [MenuItem("Magistrala/Ceste: osvježi materijale")]
        public static void Refresh()
        {
            GraphicsPass.AsphaltMaterial();
            var src = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.GEN + "/Materials/AsphaltPBR.mat");
            var flat = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.GEN + "/Materials/AsphaltFlat.mat");
            if (src && flat) { flat.CopyPropertiesFromMaterial(src); flat.SetShaderPassEnabled("ShadowCaster",false); EditorUtility.SetDirty(flat); }
            AssetDatabase.SaveAssets();
            Debug.Log("[Ceste] Asfalt osvježen. Za kamen i geometriju pokrenuti Build roads pa Build streets pa Build signals.");
        }
    }
}
