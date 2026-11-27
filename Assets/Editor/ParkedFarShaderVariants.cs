// Build-time regression guard for the runtime-only parked-car proxy renderer.
// Resources/ParkedFar.mat marks this shader as instanced before built-in stripping.
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public sealed class ParkedFarShaderVariants : IPreprocessShaders
    {
        // Observe the variants after URP's shader preprocessor has run.
        public int callbackOrder => int.MaxValue;

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (shader.name != "Magistrala/ParkedFar") return;
            var instanced = shader.keywordSpace.FindKeyword("INSTANCING_ON");
            var debug = shader.keywordSpace.FindKeyword("_PARKEDFARDEBUG");
            bool normalFound = false, debugFound = false;
            foreach (var variant in data)
            {
                if (!variant.shaderKeywordSet.IsEnabled(instanced)) continue;
                if (variant.shaderKeywordSet.IsEnabled(debug)) debugFound = true;
                else normalFound = true;
            }
            if (!normalFound || !debugFound)
                throw new BuildFailedException($"[parked] {snippet.passName}/{snippet.shaderType}: missing INSTANCING_ON variants (normal={normalFound}, debug={debugFound}). Resources/ParkedFar.mat must be included with Enable Instancing enabled; do not ship a proxy shader without instance transforms.");
            Debug.Log($"[parked] shader build check {snippet.passName}/{snippet.shaderType}: instanced normal and debug variants retained ({data.Count} variants)");
        }
    }
}
