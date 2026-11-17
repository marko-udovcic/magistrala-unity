// Sunset (Marko 2026-11-17: "zlatni sat nije realan, previše cheap", ETS2 sunsets as reference): a real sunset sky
// (Poly Haven CC0 "Belfast Sunset (Pure Sky)") turned so its sun sits low in the west-south-west over Trogir, ambient /
// fog colours measured from that sky, and the D8 reflection probes baked a second time under it. LightPreset (G) swaps.
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class SunsetPass
    {
        const string GEN = WorldBuilder.GEN, HDRI = "Assets/Textures/Sky/belfast_sunset_puresky_4k.hdr";

        static Vector3 Dir(float u, float v)
        {   // Skybox/Panoramic: u = 0.5 - atan2(z, x) / 2π, v = 1 - acos(y) / π
            float lon = (.5f - u) * 2 * Mathf.PI, y = Mathf.Cos((1 - v) * Mathf.PI), r = Mathf.Sqrt(Mathf.Max(0, 1 - y * y));
            return new Vector3(Mathf.Cos(lon) * r, y, Mathf.Sin(lon) * r);
        }
        static Color Norm(Color c, float max) { float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b), 1e-6f); return new Color(c.r / m * max, c.g / m * max, c.b / m * max); }


        // Brightest sky pixel as seen from a camera looking at the light's sun: (deg right, deg up, luminance).
        static Vector3 Measure(Material sky, Vector3 toSun)
        {
            var go = new GameObject("sky align"); go.hideFlags = HideFlags.HideAndDontSave;
            var cam = go.AddComponent<Camera>(); go.AddComponent<Skybox>().material = sky;
            cam.clearFlags = CameraClearFlags.Skybox; cam.cullingMask = 0; cam.fieldOfView = 90; cam.aspect = 1;
            go.transform.SetPositionAndRotation(new Vector3(0, 5000, 0), Quaternion.LookRotation(toSun));
            var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGBFloat); cam.targetTexture = rt; cam.Render();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var t = new Texture2D(256, 256, TextureFormat.RGBAFloat, false); t.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); RenderTexture.active = prev;
            var px = t.GetPixels(); int best = 0; float bl = 0;
            for (int i = 0; i < px.Length; i++) { float l = px[i].r + px[i].g + px[i].b; if (l > bl) { bl = l; best = i; } }
            Object.DestroyImmediate(t); cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
            float x = (best % 256 + .5f) / 128f - 1, y = (best / 256 + .5f) / 128f - 1;
            return new Vector3(Mathf.Atan(x) * Mathf.Rad2Deg, Mathf.Atan(y) * Mathf.Rad2Deg, bl);
        }

        /// Turn a Skybox/Panoramic so its sun disc sits where the directional light comes from. Measured by rendering
        /// (the analytic u/v → direction mapping came out ~100–190° off: the importer / shader flip), so it holds for any HDRI.
        public static float AlignSky(Material sky, Quaternion sunRot)
        {
            var toSun = -(sunRot * Vector3.forward);
            float bestRot = 0, bestLum = -1;
            for (int k = 0; k < 16; k++) { sky.SetFloat("_Rotation", k * 22.5f); var m = Measure(sky, toSun); if (m.z > bestLum) { bestLum = m.z; bestRot = k * 22.5f; } }
            float rot = bestRot;
            for (int it = 0; it < 6; it++)
            {
                sky.SetFloat("_Rotation", (rot % 360 + 360) % 360);
                var m = Measure(sky, toSun); if (Mathf.Abs(m.x) < .4f) break;
                rot += m.x; // +1° rotation moves the sun ~1° to the left
            }
            rot = (rot % 360 + 360) % 360; sky.SetFloat("_Rotation", rot); EditorUtility.SetDirty(sky);
            return rot;
        }

        [MenuItem("Magistrala/Grafika: poravnaj dnevno nebo sa suncem")]
        public static string AlignDay()
        {
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
            var mat = AssetDatabase.LoadAssetAtPath<Material>($"{GEN}/Materials/SkyHDRI.mat");
            float rot = AlignSky(mat, sun.transform.rotation); var m = Measure(mat, -sun.transform.forward);
            RenderSettings.skybox = mat; DynamicGI.UpdateEnvironment();
            // the day probes were baked under the mis-turned sky: bake them again into the same files (Custom probes keep pointing at them)
            int n = 0;
            foreach (var p in Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None).Where(p => p.name.StartsWith("probe ")))
            {
                var tex = p.customBakedTexture; if (p.mode == ReflectionProbeMode.Custom && tex) { Lightmapping.BakeReflectionProbe(p, AssetDatabase.GetAssetPath(tex)); p.customBakedTexture = tex; n++; }
            }
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); EditorSceneManager.SaveOpenScenes(); return $"dnevno nebo rot {rot:F1}°, ostatak {m.x:F1}° / {m.y:F1}°, {n} sondi ponovno pečeno";
        }

        [MenuItem("Magistrala/Grafika: zalazak (nebo + refleksije)")]
        public static string Sunset()
        {
            var sunGo = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
            var lp = sunGo.GetComponent<LightPreset>() ?? sunGo.gameObject.AddComponent<LightPreset>();

            // --- analyse the HDRI: the sun (brightest pixel), the sky above, the horizon band
            var imp = (TextureImporter)AssetImporter.GetAtPath(HDRI);
            imp.textureShape = TextureImporterShape.Texture2D; imp.isReadable = true; imp.mipmapEnabled = false; imp.maxTextureSize = 4096;
            imp.textureCompression = TextureImporterCompression.Uncompressed; imp.wrapModeU = TextureWrapMode.Repeat; imp.wrapModeV = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(HDRI);
            var px = tex.GetPixels(); int W = tex.width, H = tex.height, best = 0; float bl = 0;
            Color sky = default, hor = default; float sw = 0, hw = 0;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i]; float l = c.r + c.g + c.b; if (l > bl) { bl = l; best = i; }
                float v = (i / W + .5f) / H, el = Mathf.Asin(Mathf.Cos((1 - v) * Mathf.PI)) * Mathf.Rad2Deg;
                if (l > 30) continue; // leave the sun disc out of the averages
                if (el > 15) { float w = Mathf.Cos(el * Mathf.Deg2Rad); sky += c * w; sw += w; }
                else if (el >= 0 && el < 8) { hor += c; hw++; }
            }
            sky /= sw; hor /= hw;
            float su = (best % W + .5f) / W, sv = (best / W + .5f) / H;
            var dTex = Dir(su, sv); float hdrElev = Mathf.Asin(dTex.y) * Mathf.Rad2Deg;
            imp.isReadable = false; imp.textureCompression = TextureImporterCompression.CompressedHQ; imp.SaveAndReimport();

            // the light stays a few degrees above the horizon (long shadows but the land is still lit); the sky's sun is turned to the same azimuth
            lp.sunElev = Mathf.Clamp(hdrElev, 3.5f, 9f); lp.sunYaw = 76f;
            var sunRot = Quaternion.Euler(lp.sunElev, lp.sunYaw, 0);
            var dWorld = -(sunRot * Vector3.forward);
            float rot = (Mathf.Atan2(dTex.z, dTex.x) - Mathf.Atan2(dWorld.z, dWorld.x)) * Mathf.Rad2Deg;

            string mp = $"{GEN}/Materials/SkySunset.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!mat) { mat = new Material(Shader.Find("Skybox/Panoramic")); AssetDatabase.CreateAsset(mat, mp); }
            mat.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(HDRI));
            mat.SetFloat("_Mapping", 1); mat.SetFloat("_ImageType", 0); mat.SetFloat("_Rotation", (rot % 360 + 360) % 360);
            mat.SetFloat("_Exposure", 1f); mat.SetColor("_Tint", new Color(.5f, .5f, .5f));
            rot = AlignSky(mat, sunRot);
            EditorUtility.SetDirty(mat);
            lp.sunsetSky = mat;
            lp.sunsetSkyAmb = Norm(sky, .5f); lp.sunsetHorizon = Norm(hor, .9f); lp.sunsetFog = Norm(Color.Lerp(hor, sky, .25f), .78f);

            // --- reflection probes: Custom mode (day cubemap kept), then a second bake under the sunset sky
            var probes = Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None)
                .Where(p => p.name.StartsWith("probe ")).OrderBy(p => int.Parse(p.name.Substring(6))).ToArray();
            var dayTex = probes.Select(p => AssetDatabase.LoadAssetAtPath<Texture>($"{GEN}/Probes/probe{p.name.Substring(6)}.exr")).ToArray();
            for (int i = 0; i < probes.Length; i++) { probes[i].mode = ReflectionProbeMode.Custom; probes[i].customBakedTexture = dayTex[i]; }

            var keep = (RenderSettings.skybox, sunGo.transform.rotation, sunGo.colorTemperature, sunGo.intensity, sunGo.color,
                RenderSettings.ambientSkyColor, RenderSettings.ambientEquatorColor, RenderSettings.ambientGroundColor, RenderSettings.fogColor, RenderSettings.fogDensity);
            RenderSettings.skybox = mat; sunGo.transform.rotation = sunRot; sunGo.colorTemperature = 4200; sunGo.intensity = lp.sunIntensity; sunGo.color = new Color(1f, .86f, .72f);
            RenderSettings.ambientSkyColor = lp.sunsetSkyAmb * lp.ambMul; RenderSettings.ambientEquatorColor = Color.Lerp(lp.sunsetHorizon, lp.sunsetSkyAmb, .45f) * .85f * lp.ambMul;
            RenderSettings.ambientGroundColor = new Color(.16f, .13f, .13f) * lp.ambMul; RenderSettings.fogColor = lp.sunsetFog; RenderSettings.fogDensity = .00011f;
            DynamicGI.UpdateEnvironment();
            var sunsetTex = new Texture[probes.Length];
            try
            {
                for (int i = 0; i < probes.Length; i++)
                {
                    string p = $"{GEN}/Probes/sunset_probe{probes[i].name.Substring(6)}.exr";
                    Lightmapping.BakeReflectionProbe(probes[i], p);
                    sunsetTex[i] = AssetDatabase.LoadAssetAtPath<Texture>(p);
                }
            }
            finally
            {
                RenderSettings.skybox = keep.Item1; sunGo.transform.rotation = keep.Item2; sunGo.colorTemperature = keep.Item3; sunGo.intensity = keep.Item4; sunGo.color = keep.Item5;
                RenderSettings.ambientSkyColor = keep.Item6; RenderSettings.ambientEquatorColor = keep.Item7; RenderSettings.ambientGroundColor = keep.Item8;
                RenderSettings.fogColor = keep.Item9; RenderSettings.fogDensity = keep.Item10; DynamicGI.UpdateEnvironment();
                for (int i = 0; i < probes.Length; i++) probes[i].customBakedTexture = dayTex[i];
            }
            lp.probes = probes; lp.sunsetProbes = sunsetTex;
            EditorUtility.SetDirty(lp); foreach (var p in probes) EditorUtility.SetDirty(p);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); EditorSceneManager.SaveOpenScenes();
            string msg = $"[Magistrala] zalazak: HDRI sunce elev {hdrElev:F1}°, svjetlo {lp.sunElev:F1}°, rot {rot:F0}°, nebo {lp.sunsetSkyAmb}, horizont {lp.sunsetHorizon}, {probes.Length} sondi";
            Debug.Log(msg); return msg;
        }
    }
}
