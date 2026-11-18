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
        struct Level { public string name; public float mpix, shadowDist, fsrSharp, veg; public int shadowRes, cascades; public SoftShadowQuality soft; }
        static readonly Level[] Levels =
        {
            new Level { name = "brzo",   mpix = 1.0f, shadowDist = 160, shadowRes = 2048, cascades = 2, soft = SoftShadowQuality.Low,    fsrSharp = .9f, veg = .8f },
            new Level { name = "srednje", mpix = 2.0f, shadowDist = 230, shadowRes = 2048, cascades = 3, soft = SoftShadowQuality.Medium, fsrSharp = .85f, veg = 1f },
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
            TreeRenderer.Distance = L.veg; // vegetation distances (full mesh / impostor / shadows / far cutoff)
            var sun = RenderSettings.sun ? RenderSettings.sun : FindSun();
            if (sun)
            {
                if (!sunData) sunData = sun.GetComponent<UniversalAdditionalLightData>();
                if (sunData) sunData.softShadowQuality = L.soft;
            }
            if (toast) PlayerPrefs.SetInt(PREF, Current); // only the player's own choice (K) is remembered
            Debug.Log($"[kvaliteta] {L.name}: ekran {Screen.width}x{Screen.height}, crta se {Mathf.RoundToInt(Screen.width * Scale)}x{Mathf.RoundToInt(Screen.height * Scale)} (scale {Scale:F2}), sjene {L.shadowDist} m / {L.shadowRes} / {L.cascades}");
            if (toast) toastUntil = Time.unscaledTime + 2.5f;
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
        }
    }
}
