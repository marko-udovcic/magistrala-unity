using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Magistrala
{
    /// K: quality brzo / srednje / oštro (remembered). On a big screen (Retina, 4K) the game draws fewer pixels and FSR
    /// upscales them to the full screen: the render resolution follows a pixel budget, not the screen. Shadows get cheaper
    /// in the lower levels too. Command line: `-kvaliteta 0|1|2`. Attaches itself (no scene change).
    public class RenderQuality : MonoBehaviour
    {
        struct Level { public string name; public float mpix, shadowDist, fsrSharp, veg; public int shadowRes, cascades; public SoftShadowQuality soft; public bool aoDepth, aoHalf, aoAfter, aoOff; }
        static readonly Level[] Levels =
        {
            new Level { name = "brzo",   mpix = 1.0f, shadowDist = 160, shadowRes = 2048, cascades = 2, soft = SoftShadowQuality.Low,    fsrSharp = .9f, veg = .8f, aoOff = true },
            new Level { name = "srednje", mpix = 2.0f, shadowDist = 230, shadowRes = 2048, cascades = 3, soft = SoftShadowQuality.Medium, fsrSharp = .85f, veg = 1f, aoHalf = true },
            new Level { name = "oštro",  mpix = 99f,  shadowDist = 300, shadowRes = 4096, cascades = 4, soft = SoftShadowQuality.High,   fsrSharp = .8f, veg = 1.2f },
        };
        const string PREF = "kvaliteta";

        public static int Current { get; private set; } = 1;
        public static float Scale { get; private set; } = 1;

        UniversalRenderPipelineAsset rp;
        UniversalAdditionalLightData sunData;
        int lastW, lastH;
        float toastUntil; GUIStyle toastStyle;
        // editor only: changing the asset in Play Mode would stick, so the originals come back on exit
        float o_scale, o_dist; int o_res, o_casc; UpscalingFilterSelection o_up; bool saved, o_fsrO; float o_fsr;
        // SSAO settings (internal class on the renderer feature, set by reflection): aoDepth = normals from depth (Depth prepass
        // instead of DepthNormals), aoHalf = AO at half resolution, aoAfter = from the depth copy after the opaque pass (no prepass
        // at all; AO then darkens the lit colour, not only ambient), aoOff = no SSAO. Full SSAO cost ~5.5 ms GPU on the M2 at
        // 1710x1074, half resolution saves ~3 ms. Depth-source AO is grainy here (far plane 9 km), so no level uses it.
        ScriptableRendererFeature aoFeature; bool o_aoActive; object ao; System.Reflection.FieldInfo aoSource, aoDown, aoAfter; object o_aoSource; bool o_aoDown, o_aoAfter;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            if (FindFirstObjectByType<RenderQuality>()) return;
            var go = new GameObject("RenderQuality"); DontDestroyOnLoad(go); go.AddComponent<RenderQuality>();
        }

        void Start()
        {
            rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!rp) { enabled = false; return; }
            if (Application.isEditor) { o_scale = rp.renderScale; o_dist = rp.shadowDistance; o_res = rp.mainLightShadowmapResolution; o_casc = rp.shadowCascadeCount; o_up = rp.upscalingFilter; o_fsrO = rp.fsrOverrideSharpness; o_fsr = rp.fsrSharpness; saved = true; }
            int lvl = PlayerPrefs.GetInt(PREF, 1);
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-kvaliteta" && int.TryParse(args[i + 1], out int v)) lvl = v;
            // measuring only: -grd 0 turns the GPU Resident Drawer off, -occl 0 only its GPU occlusion culling
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-grd" && args[i + 1] == "0") rp.gpuResidentDrawerMode = GPUResidentDrawerMode.Disabled;
                if (args[i] == "-occl" && args[i + 1] == "0") rp.gpuResidentDrawerEnableOcclusionCullingInCameras = false;
            }
            FindAO();
            if (saved && ao != null) { o_aoSource = aoSource.GetValue(ao); o_aoDown = (bool)aoDown.GetValue(ao); o_aoAfter = (bool)aoAfter.GetValue(ao); o_aoActive = aoFeature.isActive; }
            Debug.Log($"[kvaliteta] GPU Resident Drawer {rp.gpuResidentDrawerMode}, occlusion {rp.gpuResidentDrawerEnableOcclusionCullingInCameras}");
            Set(lvl, false);
        }

        void Set(int lvl, bool toast)
        {
            Current = Mathf.Clamp(lvl, 0, Levels.Length - 1);
            var L = Levels[Current];
            lastW = Screen.width; lastH = Screen.height;
            float px = Mathf.Max(1, (float)Screen.width * Screen.height) / 1e6f;
            Scale = Mathf.Clamp(Mathf.Sqrt(L.mpix / px), .5f, 1f);
            if (Scale > .97f) Scale = 1f;
            rp.renderScale = Scale;
            rp.upscalingFilter = Scale < 1f ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Auto;
            rp.fsrOverrideSharpness = true; rp.fsrSharpness = L.fsrSharp;
            rp.shadowDistance = L.shadowDist;
            rp.mainLightShadowmapResolution = L.shadowRes;
            rp.shadowCascadeCount = L.cascades;
            if (ao != null) { aoSource.SetValue(ao, System.Enum.ToObject(aoSource.FieldType, L.aoDepth ? 0 : 1)); aoAfter.SetValue(ao, L.aoAfter); aoDown.SetValue(ao, L.aoHalf); aoFeature.SetActive(!L.aoOff); }
            TreeRenderer.Distance = L.veg; // vegetation distances (full mesh / impostor / shadows / far cutoff)
            var sun = RenderSettings.sun ? RenderSettings.sun : FindSun();
            if (sun)
            {
                if (!sunData) sunData = sun.GetComponent<UniversalAdditionalLightData>();
                if (sunData) sunData.softShadowQuality = L.soft;
            }
            GpuSwitches.ApplyPipeline(rp); // measuring only: command-line overrides
            if (toast) PlayerPrefs.SetInt(PREF, Current); // only the player's own choice (K) is remembered
            Debug.Log($"[kvaliteta] {L.name}: ekran {Screen.width}x{Screen.height}, crta se {Mathf.RoundToInt(Screen.width * Scale)}x{Mathf.RoundToInt(Screen.height * Scale)} (scale {Scale:F2}), sjene {L.shadowDist} m / {L.shadowRes} / {L.cascades}, SSAO {(ao == null ? "?" : L.aoOff ? "isključen" : L.aoDepth ? "Depth" : "DepthNormals")}{(L.aoHalf ? " pola rez." : "")}{(L.aoAfter ? " nakon neprozirnih" : "")}");
            if (toast) toastUntil = Time.unscaledTime + 2.5f;
        }

        void FindAO()
        {
            const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var list = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", F)?.GetValue(rp) as ScriptableRendererData[];
            if (list == null || list.Length == 0 || !list[0]) return;
            foreach (var f in list[0].rendererFeatures)
                if (f is ScreenSpaceAmbientOcclusion)
                {
                    aoFeature = f; ao = f.GetType().GetField("m_Settings", F)?.GetValue(f); if (ao == null) return;
                    aoSource = ao.GetType().GetField("Source", F); aoDown = ao.GetType().GetField("Downsample", F); aoAfter = ao.GetType().GetField("AfterOpaque", F);
                    if (aoSource == null || aoDown == null || aoAfter == null) ao = null;
                    return;
                }
        }

        static Light FindSun()
        {
            foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) return l;
            return null;
        }

        public static string Label => $"{Levels[Current].name}" + (Scale < 1f ? $" {Mathf.RoundToInt(Scale * 100)} %" : "");

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.kKey.wasPressedThisFrame) Set((Current + 1) % Levels.Length, true);
            else if (Screen.width != lastW || Screen.height != lastH) Set(Current, false); // window resized / fullscreen toggled
        }

        void OnGUI()
        {
            if (Time.unscaledTime > toastUntil) return;
            toastStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            var L = Levels[Current];
            string s = $"Kvaliteta: {L.name}  ·  crta se {Mathf.RoundToInt(Screen.width * Scale)}×{Mathf.RoundToInt(Screen.height * Scale)}";
            GUI.Box(new Rect(Screen.width * .5f - 300, Screen.height * .18f, 600, 46), s, toastStyle);
        }

        void OnDestroy()
        {
            if (!saved || !rp) return;
            rp.renderScale = o_scale; rp.shadowDistance = o_dist; rp.mainLightShadowmapResolution = o_res; rp.shadowCascadeCount = o_casc; rp.upscalingFilter = o_up; rp.fsrOverrideSharpness = o_fsrO; rp.fsrSharpness = o_fsr;
            if (ao != null && o_aoSource != null) { aoSource.SetValue(ao, o_aoSource); aoDown.SetValue(ao, o_aoDown); aoAfter.SetValue(ao, o_aoAfter); aoFeature.SetActive(o_aoActive); }
        }
    }
}
