using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Magistrala
{
    /// Measuring only: command-line switches that turn GPU costs on / off in a built player, so one build can A/B many
    /// variants. Pipeline-asset switches are applied after every RenderQuality.Set (ApplyPipeline), scene switches once
    /// at start. Unknown / absent switches change nothing.
    ///   -post 0                 camera post-processing off        -bloom 0 / -vig 0 / -split 0 / -wb 0 / -ca 0 / -smh 0 / -tonemap 0
    ///   -aa 0|1|2|3 [-aaq 0..2] antialiasing none/FXAA/SMAA/TAA   -hdr 0      -opaque 0|1  -depth 0    -lut 16|32
    ///   -ssao 0 | half | full | depth | normals | after | before | blur0..2 | samples0..2 (comma list)
    ///   -soft 0..3 (0 = hard shadows)   -casc N   -shd metres   -shres N   -scale F
    ///   -shsmall M  renderers whose bounds are smaller than M metres cast no shadow   -tshadow 0  terrain casts no shadow
    ///   -sea 0      -probes 0 (reflection probe blending / box projection off)   -tbase M (terrain basemap distance)
    ///   -golden 1   start in sunset    -shot path.png [-shotat s]  screenshot after s seconds (default 25)
    ///   -burst N    with -shot: N consecutive frames path_00.png.. (shimmer / temporal look while driving)
    ///   -near M / -far M  camera clip planes (after TruckChaseCamera.Awake)
    ///   -ssao ...,nq0..2  normals-from-depth reconstruction quality (Low / Medium / High; only with depth)
    public class GpuSwitches : MonoBehaviour
    {
        static string[] args;
        static string Arg(string k)
        {
            args ??= System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == k) return args[i + 1];
            return null;
        }
        static bool Off(string k) => Arg(k) == "0";
        static bool TryF(string k, out float f) { f = 0; var s = Arg(k); return s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            if (Application.isEditor) return;
            var go = new GameObject("GpuSwitches"); DontDestroyOnLoad(go); go.AddComponent<GpuSwitches>();
        }

        static void SetField(object o, string name, object v)
        {
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(o, v); else Debug.Log($"[gpu] no field {name} on {o.GetType().Name}");
        }

        /// pipeline asset overrides; RenderQuality.Set calls this after it writes its level
        public static void ApplyPipeline(UniversalRenderPipelineAsset rp)
        {
            if (Application.isEditor || !rp) return;
            var sb = new System.Text.StringBuilder("[gpu] pipeline:");
            var cd0 = Camera.main ? Camera.main.GetComponent<UniversalAdditionalCameraData>() : null; // AA is per level too (RenderQuality), so the switch wins here
            if (cd0 && TryF("-aa", out var aa0)) { cd0.antialiasing = (AntialiasingMode)(int)aa0; sb.Append($" aa {aa0}"); }
            if (cd0 && TryF("-aaq", out var aq0)) { cd0.antialiasingQuality = (AntialiasingQuality)(int)aq0; sb.Append($" aaq {aq0}"); }
            if (Off("-hdr")) { rp.supportsHDR = false; sb.Append(" hdr 0"); }
            if (Arg("-opaque") is string op) { rp.supportsCameraOpaqueTexture = op != "0"; sb.Append($" opaque {op}"); }
            if (Off("-depth")) { rp.supportsCameraDepthTexture = false; sb.Append(" depth 0"); }
            if (TryF("-lut", out var lut)) { rp.colorGradingLutSize = (int)lut; sb.Append($" lut {lut}"); }
            if (TryF("-casc", out var c)) { rp.shadowCascadeCount = (int)c; sb.Append($" casc {c}"); }
            if (TryF("-shd", out var d)) { rp.shadowDistance = d; sb.Append($" shd {d}"); }
            if (TryF("-shres", out var r)) { rp.mainLightShadowmapResolution = (int)r; sb.Append($" shres {r}"); }
            if (TryF("-scale", out var s)) { rp.renderScale = s; rp.upscalingFilter = s < 1 ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Auto; sb.Append($" scale {s}"); }
            if (Off("-probes")) { SetField(rp, "m_ReflectionProbeBlending", false); SetField(rp, "m_ReflectionProbeBoxProjection", false); sb.Append(" probes 0"); }
            if (TryF("-soft", out var soft))
            {
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l.type != LightType.Directional) continue;
                    if (soft == 0) l.shadows = LightShadows.Hard;
                    else { var ad = l.GetComponent<UniversalAdditionalLightData>(); if (ad) ad.softShadowQuality = (SoftShadowQuality)(int)soft; }
                }
                sb.Append($" soft {soft}");
            }
            var ssao = Arg("-ssao");
            if (ssao != null)
            {
                var list = GetRendererFeatures(rp);
                if (list != null) foreach (var f in list)
                {
                    if (!(f is ScreenSpaceAmbientOcclusion)) continue;
                    if (ssao == "0") { f.SetActive(false); continue; }
                    f.SetActive(true); // any other value: SSAO on with those settings
                    var st = f.GetType().GetField("m_Settings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(f);
                    if (st == null) continue;
                    foreach (var w in ssao.Split(','))
                    {
                        if (w == "half") SetField(st, "Downsample", true);
                        else if (w == "after") SetField(st, "AfterOpaque", true);
                        else if (w == "before") SetField(st, "AfterOpaque", false);
                        else if (w == "depth") SetEnum(st, "Source", 0);
                        else if (w == "normals") SetEnum(st, "Source", 1);
                        else if (w == "full") SetField(st, "Downsample", false);
                        else if (w.StartsWith("blur")) SetEnum(st, "BlurQuality", int.Parse(w.Substring(4)));
                        else if (w.StartsWith("samples")) SetEnum(st, "Samples", int.Parse(w.Substring(7)));
                        else if (w == "interleaved") SetEnum(st, "AOMethod", 1);
                        else if (w.StartsWith("nq")) SetEnum(st, "NormalSamples", int.Parse(w.Substring(2)));
                    }
                }
                sb.Append($" ssao {ssao}");
            }
            Debug.Log(sb.ToString());
        }

        static void SetEnum(object o, string name, int v)
        {
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(o, System.Enum.ToObject(f.FieldType, v)); else Debug.Log($"[gpu] no field {name}");
        }

        static System.Collections.Generic.List<ScriptableRendererFeature> GetRendererFeatures(UniversalRenderPipelineAsset rp)
        {
            var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
            var arr = f?.GetValue(rp) as ScriptableRendererData[];
            if (arr == null || arr.Length == 0 || !arr[0]) { Debug.Log("[gpu] no renderer data"); return null; }
            return arr[0].rendererFeatures;
        }

        float shotAt = 25; string shot; bool shotDone; int burst, burstI;

        void Start()
        {
            var sb = new System.Text.StringBuilder("[gpu] scene:");
            var cam = Camera.main;
            Debug.Log($"[gpu] depth {SystemInfo.GetGraphicsFormat(UnityEngine.Experimental.Rendering.DefaultFormat.DepthStencil)}, reversed Z {SystemInfo.usesReversedZBuffer}, near {(cam ? cam.nearClipPlane : 0)} far {(cam ? cam.farClipPlane : 0)}");
            var cd = cam ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
            if (cd)
            {
                if (Off("-post")) { cd.renderPostProcessing = false; sb.Append(" post 0"); }
                if (TryF("-aa", out var aa)) { cd.antialiasing = (AntialiasingMode)(int)aa; sb.Append($" aa {aa}"); }
                if (TryF("-aaq", out var aq)) { cd.antialiasingQuality = (AntialiasingQuality)(int)aq; sb.Append($" aaq {aq}"); }
            }
            if (cam && TryF("-near", out var nr)) { cam.nearClipPlane = nr; sb.Append($" near {nr}"); }
            if (cam && TryF("-far", out var fr)) { cam.farClipPlane = fr; sb.Append($" far {fr}"); }
            var vol = FindFirstObjectByType<Volume>();
            if (vol)
            {
                var prof = vol.profile;
                foreach (var comp in prof.components)
                {
                    string n = comp is Bloom ? "-bloom" : comp is Vignette ? "-vig" : comp is SplitToning ? "-split" : comp is WhiteBalance ? "-wb"
                        : comp is ColorAdjustments ? "-ca" : comp is ShadowsMidtonesHighlights ? "-smh" : comp is Tonemapping ? "-tonemap" : null;
                    if (n != null && Off(n)) { comp.active = false; sb.Append($" {n} 0"); }
                }
            }
            if (Off("-sea")) foreach (var s in FindObjectsByType<SeaScroll>(FindObjectsSortMode.None)) { s.gameObject.SetActive(false); sb.Append(" sea 0"); }
            if (TryF("-shsmall", out var small))
            {
                int n = 0;
                foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    if (r.shadowCastingMode == ShadowCastingMode.On && r.bounds.size.magnitude < small) { r.shadowCastingMode = ShadowCastingMode.Off; n++; }
                sb.Append($" shsmall {small}: {n} renderers");
            }
            if (Off("-tshadow")) foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None)) { t.shadowCastingMode = ShadowCastingMode.Off; sb.Append(" tshadow 0"); }
            if (TryF("-tbase", out var tb)) foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None)) { t.basemapDistance = tb; sb.Append($" tbase {tb}"); }
            if (Arg("-golden") == "1") { var lp = FindFirstObjectByType<LightPreset>(); if (lp) { lp.SetGolden(true); sb.Append(" golden"); } }
            shot = Arg("-shot"); if (TryF("-shotat", out var sa)) shotAt = sa; if (TryF("-burst", out var bu)) burst = (int)bu;
            Debug.Log(sb.ToString());
        }

        void Update()
        {
            if (shot == null || shotDone || Time.realtimeSinceStartup <= shotAt) return;
            if (burst <= 1) { shotDone = true; ScreenCapture.CaptureScreenshot(shot); Debug.Log("[gpu] shot " + shot); return; }
            ScreenCapture.CaptureScreenshot(shot.Replace(".png", $"_{burstI:D2}.png"));
            if (++burstI >= burst) { shotDone = true; Debug.Log($"[gpu] burst {burst} " + shot); }
        }
    }
}
