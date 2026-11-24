// "Magistrala/Shaderi: SVC iz GSC": a ShaderVariantCollection (Assets/Resources/Shaderi/warmup.shadervariants) from the
// GraphicsStateCollection recordings in Assets/StreamingAssets (*.graphicsstate, recorded with the player's -gsctrace).
// The GSC is bound to one platform + graphics API (warmup-osxplayer-metal); the SVC is API-independent (shader + pass type +
// keywords), so ShaderWarmup warms it up where no GSC matches (Windows D3D11 until a D3D12 recording exists). Weaker than the
// GSC (no render states / PSOs), but it removes most of the first-use shader compile stalls.
// Batch: -executeMethod Magistrala.EditorTools.ShaderVariantsFromGsc.Build
// Fallback when no GSC gives variants: the editor's currently tracked variants (what Graphics settings › "Save to asset" writes).
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class ShaderVariantsFromGsc
    {
        public const string OutPath = "Assets/Resources/Shaderi/warmup.shadervariants";

        [MenuItem("Magistrala/Shaderi: SVC iz GSC")]
        public static void Build()
        {
            var svc = new ShaderVariantCollection();
            int files = 0, read = 0, added = 0, failed = 0;
            var shaders = new HashSet<Shader>();
            var variants = new List<GraphicsStateCollection.ShaderVariant>();
            string dir = Path.Combine(Application.dataPath, "StreamingAssets");
            if (Directory.Exists(dir))
                foreach (var f in Directory.GetFiles(dir, "*.graphicsstate"))
                {
                    var gsc = new GraphicsStateCollection();
                    if (!gsc.LoadFromFile(f)) { Debug.LogWarning("[svc] unreadable " + f); continue; }
                    files++;
                    variants.Clear(); gsc.GetVariants(variants);
                    foreach (var v in variants)
                    {
                        read++;
                        if (!v.shader) { failed++; continue; }
                        var names = new string[v.keywords != null ? v.keywords.Length : 0];
                        for (int i = 0; i < names.Length; i++) names[i] = v.keywords[i].name;
                        var pt = PassTypeOf(v.shader, v.passId);
                        try
                        {
                            var sv = new ShaderVariantCollection.ShaderVariant(v.shader, pt, names);
                            if (!svc.Contains(sv)) { svc.Add(sv); added++; shaders.Add(v.shader); }
                        }
                        catch (System.ArgumentException) { failed++; } // keywords not valid for that pass type: skipped
                    }
                }
            Directory.CreateDirectory(Path.GetDirectoryName(OutPath));
            if (added > 0)
            {
                AssetDatabase.DeleteAsset(OutPath);
                AssetDatabase.CreateAsset(svc, OutPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[svc] {OutPath}: {svc.shaderCount} shaders, {svc.variantCount} variants from {files} GSC file(s) ({read} read, {failed} skipped)");
                return;
            }
            // fallback: the variants the editor has tracked this session (internal ShaderUtil API, as Graphics settings uses it)
            var m = typeof(ShaderUtil).GetMethod("SaveCurrentShaderVariantCollection", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (m == null) { Debug.LogError($"[svc] no variants from {files} GSC file(s) ({read} read, {failed} skipped) and no ShaderUtil fallback"); return; }
            m.Invoke(null, new object[] { OutPath });
            AssetDatabase.ImportAsset(OutPath);
            var saved = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(OutPath);
            Debug.Log($"[svc] {OutPath} from the editor's tracked variants (GSC gave none): {(saved ? saved.shaderCount : 0)} shaders, {(saved ? saved.variantCount : 0)} variants");
        }

        // SVC wants the pass type, the GSC records the pass index: from the pass's LightMode tag
        static PassType PassTypeOf(Shader s, PassIdentifier id)
        {
            string lm = "";
            try { lm = s.FindPassTagValue((int)id.SubshaderIndex, (int)id.PassIndex, new ShaderTagId("LightMode")).name ?? ""; }
            catch (System.ArgumentException) { }
            switch (lm)
            {
                case "": case "SRPDefaultUnlit": return PassType.ScriptableRenderPipelineDefaultUnlit;
                case "ShadowCaster": return PassType.ShadowCaster;
                case "Meta": return PassType.Meta;
                case "MotionVectors": return PassType.MotionVectors;
                default: return PassType.ScriptableRenderPipeline;
            }
        }
    }
}
