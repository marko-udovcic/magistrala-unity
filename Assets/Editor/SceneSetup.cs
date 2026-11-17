// Lighting / post / camera / truck setup of the probe scene (sunny Mediterranean midday-afternoon, "GTA V daytime" look).
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Magistrala.EditorTools
{
    public static class SceneSetup
    {
        [MenuItem("Magistrala/Setup lighting + post")]
        public static void Lighting()
        {
            // URP asset: 300 m cascaded shadows, 4096 map, soft shadows high, HDR, no MSAA (TAA on the camera)
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            var so = new SerializedObject(rp);
            so.FindProperty("m_ShadowDistance").floatValue = 300;
            so.FindProperty("m_ShadowCascadeCount").intValue = 4;
            so.FindProperty("m_Cascade4Split").vector3Value = new Vector3(.05f, .14f, .38f);
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
            so.FindProperty("m_SoftShadowsSupported").boolValue = true;
            so.FindProperty("m_SoftShadowQuality").intValue = 3;
            so.FindProperty("m_MSAA").intValue = 1;
            so.FindProperty("m_SupportsHDR").boolValue = true;
            so.FindProperty("m_SupportsTerrainHoles").boolValue = true;
            var depth = so.FindProperty("m_RequireDepthTexture"); if (depth != null) depth.boolValue = true;
            var cb = so.FindProperty("m_ShadowDepthBias"); if (cb != null) cb.floatValue = 1.0f;
            var nb = so.FindProperty("m_ShadowNormalBias"); if (nb != null) nb.floatValue = 0.6f;
            so.ApplyModifiedProperties();
            for (int q = 0; q < QualitySettings.names.Length; q++) { QualitySettings.SetQualityLevel(q, false); QualitySettings.renderPipeline = rp; }
            QualitySettings.SetQualityLevel(QualitySettings.names.ToList().IndexOf("PC"), true);
            GraphicsSettings.defaultRenderPipeline = rp;
            QualitySettings.vSyncCount = 0;
            QualitySettings.lodBias = 2;

            // SSAO on the PC renderer
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            foreach (var f in rd.rendererFeatures)
                if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion")
                {
                    f.SetActive(true);
                    var fs = new SerializedObject(f); var st = fs.FindProperty("m_Settings");
                    void Set(string n, float v) { var p = st.FindPropertyRelative(n); if (p != null) { if (p.propertyType == SerializedPropertyType.Float) p.floatValue = v; else if (p.propertyType == SerializedPropertyType.Integer || p.propertyType == SerializedPropertyType.Enum) p.intValue = (int)v; else if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v > 0; } }
                    Set("Intensity", 1.1f); Set("Radius", .6f); Set("DirectLightingStrength", .2f); Set("Falloff", 120f);
                    fs.ApplyModifiedProperties();
                }

            // sky
            var sky = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.GEN + "/Materials/Sky.mat");
            if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, WorldBuilder.GEN + "/Materials/Sky.mat"); }
            sky.SetFloat("_SunDisk", 2); sky.SetFloat("_SunSize", .03f); sky.SetFloat("_SunSizeConvergence", 8);
            sky.SetFloat("_AtmosphereThickness", .62f); sky.SetColor("_SkyTint", new Color(.36f, .5f, .74f));
            sky.SetColor("_GroundColor", new Color(.42f, .45f, .5f)); sky.SetFloat("_Exposure", 1.25f);
            RenderSettings.skybox = sky;

            // sun
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (!sun) { sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; }
            sun.name = "Sun";
            sun.transform.rotation = Quaternion.Euler(52, 28, 0);
            sun.color = new Color(1f, .955f, .88f); sun.intensity = 2.1f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = .92f;
            var lad = sun.GetComponent<UniversalAdditionalLightData>() ?? sun.gameObject.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox; RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox; RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = .00011f;
            RenderSettings.fogColor = new Color(.66f, .76f, .88f);
            DynamicGI.UpdateEnvironment();

            // post volume
            var vol = Object.FindFirstObjectByType<Volume>();
            if (!vol) { vol = new GameObject("Post").AddComponent<Volume>(); }
            vol.isGlobal = true; vol.priority = 1;
            string pp = WorldBuilder.GEN + "/Post.asset";
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(pp);
            if (prof == null) { prof = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(prof, pp); }
            T Get<T>() where T : VolumeComponent { if (!prof.TryGet<T>(out var c)) { c = prof.Add<T>(true); AssetDatabase.AddObjectToAsset(c, prof); } c.active = true; return c; }
            var tm = Get<Tonemapping>(); tm.mode.Override(TonemappingMode.ACES);
            var bl = Get<Bloom>(); bl.threshold.Override(1.1f); bl.intensity.Override(.18f); bl.scatter.Override(.55f); bl.highQualityFiltering.Override(true);
            var ca = Get<ColorAdjustments>(); ca.postExposure.Override(.15f); ca.contrast.Override(14f); ca.saturation.Override(16f);
            var wb = Get<WhiteBalance>(); wb.temperature.Override(-4f); wb.tint.Override(0f);
            var smh = Get<ShadowsMidtonesHighlights>(); smh.shadows.Override(new Vector4(.97f, .99f, 1.04f, 0)); smh.highlights.Override(new Vector4(1.02f, 1.0f, .97f, 0));
            var vg = Get<Vignette>(); vg.intensity.Override(.16f); vg.smoothness.Override(.4f);
            EditorUtility.SetDirty(prof);
            vol.sharedProfile = prof;

            // camera
            var cam = Camera.main ?? Object.FindFirstObjectByType<Camera>();
            cam.farClipPlane = 9000; cam.nearClipPlane = .25f; cam.fieldOfView = 55;
            var cd = cam.GetUniversalAdditionalCameraData();
            cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.TemporalAntiAliasing; cd.antialiasingQuality = AntialiasingQuality.High;
            cd.renderShadows = true;
            if (!cam.GetComponent<Magistrala.EnvironmentInit>()) cam.gameObject.AddComponent<Magistrala.EnvironmentInit>();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        /// put the scene camera / main camera at a game-frame point looking at another (screenshots)
        public static void Look(double gx, double gy, double gz, double tx, double ty, double tz)
        {
            var cam = Camera.main;
            cam.transform.position = W.U(gx, gy, gz);
            cam.transform.LookAt(W.U(tx, ty, tz));
        }
    }
}
