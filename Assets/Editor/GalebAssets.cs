// Menu Magistrala/Vozila: Galeb – the Galeb's livery material (Resources/Vehicles/Galeb/galeb_livery.mat: URP Lit, the band
// texture galeb_livery.png with alpha clipping for its rounded corners) and a check of the model (size, wheels, no brand words in
// node / material names). The model itself (galeb.glb) is made outside Unity: Tools/vehicles/galeb/make_galeb.py.
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class GalebAssets
    {
        const string Dir = "Assets/Resources/Vehicles/Galeb/";

        [MenuItem("Magistrala/Vozila: Galeb")]
        public static string Make()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "galeb_livery.png");
            if (!tex) return "galeb_livery.png missing";
            var ti = (TextureImporter)AssetImporter.GetAtPath(Dir + "galeb_livery.png");
            if (ti.wrapMode != TextureWrapMode.Clamp || ti.maxTextureSize != 2048 || !ti.alphaIsTransparency)
            { ti.wrapMode = TextureWrapMode.Clamp; ti.maxTextureSize = 2048; ti.alphaIsTransparency = true; ti.anisoLevel = 4; ti.SaveAndReimport(); }
            var path = Dir + "galeb_livery.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_AlphaClip", 1); mat.SetFloat("_Cutoff", 0.5f); mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Smoothness", 0.55f); mat.SetFloat("_Metallic", 0);
            mat.SetOverrideTag("RenderType", "TransparentCutout"); mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();
            // the model: overall size and the four wheel nodes
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "galeb.glb");
            string info = "galeb.glb missing";
            if (glb)
            {
                var go = Object.Instantiate(glb); var b = new Bounds(); bool any = false; int wheels = 0; var bad = new System.Text.StringBuilder();
                foreach (var r in go.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); foreach (var m in r.sharedMaterials) if (m && Brand(m.name)) bad.Append(" mat:" + m.name); }
                foreach (var t in go.GetComponentsInChildren<Transform>()) { if (t.name.StartsWith("wheel_")) wheels++; if (Brand(t.name)) bad.Append(" node:" + t.name); }
                info = $"size {b.size.x:F2} × {b.size.y:F2} × {b.size.z:F2} m (w × h × l), centre {b.center}, wheels {wheels}, brand words:{(bad.Length == 0 ? " none" : bad.ToString())}";
                Object.DestroyImmediate(go);
            }
            Debug.Log("[vozila] Galeb: livery material ok; " + info);
            return info;
        }

        static bool Brand(string s) { s = s.ToLowerInvariant(); return s.Contains("mercedes") || s.Contains("benz") || s.Contains("sprinter"); }
    }
}
