// Import settings for the billboard pictures in Assets/Resources/Reklame (the PNGs and their .meta are not in git, so the settings
// live here): sRGB colour, mipmaps, max 2048, normal-quality compression, no power-of-two rescale (sizes are multiples of 4),
// clamp (the zid8_ tiles repeat along u), anisotropic filtering for the walls seen at a grazing angle.
// Menu Magistrala/Reklame: pregled lists the ad_ faces of the open scene and the ad each one would get (changes nothing).
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public class ReklameImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').StartsWith("Assets/Resources/Reklame/")) return;
            var ti = (TextureImporter)assetImporter;
            string file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool wall = file.StartsWith("zid");
            ti.textureType = TextureImporterType.Default;
            ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.None;
            ti.mipmapEnabled = true;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.crunchedCompression = false;
            ti.wrapModeU = file.StartsWith("zid8_") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.filterMode = FilterMode.Trilinear;
            ti.anisoLevel = wall ? 8 : 4;
            ti.isReadable = false;
        }

        [MenuItem("Magistrala/Reklame: pregled")]
        static void Preview()
        {
            var sb = new System.Text.StringBuilder();
            int n = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                string s = Billboards.Describe(r);
                if (s == null) continue;
                sb.AppendLine(s); n++;
            }
            Debug.Log(n == 0 ? "[reklame] u sceni nema ploha ad_* (modeli mag-23-11-v2 još nisu uvezeni?)" : $"[reklame] {n} ploha:\n{sb}");
        }
    }
}
