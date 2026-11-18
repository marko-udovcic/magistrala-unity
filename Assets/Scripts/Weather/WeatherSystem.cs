using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Magistrala
{
    /// V: vrijeme sunčano ↔ oblačno + kiša (command line `-vrijeme kisa`). Attaches itself (no scene change).
    /// Rain is a modifier over whatever LightPreset shows (day or the G sunset): flat grey overcast sky blended over the HDRI,
    /// sun and its shadows almost gone, flat grey ambient, dense grey fog, brighter / flatter / less saturated grade, reflection
    /// probes swapped for one rendered from the overcast sky, a wet road (global _Wetness in Asphalt / VertexColorLit), GPU rain
    /// streaks around the camera and drops on the lens. Changes fade over ~3 s; G while raining re-applies the rain on the new light.
    [DefaultExecutionOrder(200)] // after TruckChaseCamera (100): camera velocity of this frame
    public class WeatherSystem : MonoBehaviour
    {
        public static WeatherSystem Instance { get; private set; }
        public static bool Raining => Instance && Instance.target > .5f;
        public static float Wetness => Instance ? Instance.wet : 0f;
        public static string Label => Raining ? "kiša" : "sunčano";

        [Header("kiša")]
        public Color rainFog = new Color(.60f, .63f, .66f);
        public float fogDensity = .00055f, sunMul = .1f, shadowStrength = .2f;
        public Color ambSky = new Color(.95f, .99f, 1.05f), ambEq = new Color(.80f, .83f, .86f), ambGround = new Color(.38f, .38f, .37f);
        public float exposureAdd = .35f, contrastAdd = -9f, saturationAdd = -32f, wbAdd = -5f, vignette = .28f;
        public float fadeTime = 3f;
        static readonly int[] RainCount = { 3000, 5000, 7000 }; // brzo / srednje / oštro

        struct B
        {
            public float sunI, shadow, fogD, exposure, contrast, sat, wb, bloomT, bloomI, vig;
            public Color fog, sky, eq, ground; public Material skybox; public Texture[] probes; public Texture defRefl; public DefaultReflectionMode reflMode;
        }
        B b; bool haveBase;
        float target, k, wet; // target 0/1, k = applied blend, wet = road wetness (lags: dries slower than it soaks)
        bool lastGolden; LightPreset lp; Light sun;
        ColorAdjustments ca; WhiteBalance wbc; Bloom bloom; Vignette vig;
        ReflectionProbe[] probes; ReflectionProbe skyProbe; int skyProbeId = -1; bool skyProbeReady, useGradient, rtProbes, rtProbesSaved; float probeT; Texture rainRefl;
        Material skyMat, rainMat, lensMat; GameObject rainGo, lensGo; Mesh rainMesh; int rainN;
        Camera cam; TruckChaseCamera chase; UniversalAdditionalCameraData camData; CameraOverrideOption camColorOpt; bool camColorSaved;
        Vector3 lastCamPos, camVel; bool haveCam;
        float lastHeat = -1, toastUntil, toggleAt = -1; GUIStyle toastStyle;

        static readonly int ID_Wetness = Shader.PropertyToID("_Wetness"), ID_Overcast = Shader.PropertyToID("_Overcast"),
            ID_Box = Shader.PropertyToID("_RainBox"), ID_Centre = Shader.PropertyToID("_RainCentre"), ID_CamVel = Shader.PropertyToID("_RainCamVel"),
            ID_Wind = Shader.PropertyToID("_RainWind"), ID_Speed = Shader.PropertyToID("_RainSpeed"), ID_Shutter = Shader.PropertyToID("_RainShutter"),
            ID_Amount = Shader.PropertyToID("_RainAmount"), ID_RainColor = Shader.PropertyToID("_RainColor"), ID_LensAmount = Shader.PropertyToID("_Amount"),
            ID_LensWind = Shader.PropertyToID("_LensWind"), ID_Alpha = Shader.PropertyToID("_Alpha"), ID_RainI = Shader.PropertyToID("_RainIntensity"), ID_Heat = Shader.PropertyToID("_Heat"), ID_Near = Shader.PropertyToID("_RainNear");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            Shader.SetGlobalFloat(ID_Wetness, 0);
            if (FindFirstObjectByType<WeatherSystem>()) return;
            var go = new GameObject("WeatherSystem"); DontDestroyOnLoad(go); go.AddComponent<WeatherSystem>();
        }

        void Awake() { Instance = this; }

        void Start()
        {
            lp = FindFirstObjectByType<LightPreset>();
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-vrijeme") { string w = args[i + 1].ToLowerInvariant(); if (w.StartsWith("ki")) { target = 1; k = 1; wet = 1; } }
            // comparing: -vrijeme-odsjaj gradijent = rain reflections from the gradient cube instead of the sky probe
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-vrijeme-odsjaj") useGradient = args[i + 1] == "gradijent";
            // testing the fade: -vrijeme-prebaci 15 = press V after 15 s
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-vrijeme-prebaci") float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out toggleAt);
            if (target > 0) { Snapshot(); ApplyAll(); Debug.Log("[vrijeme] kiša (naredbeni redak)"); }
        }

        public void Set(bool rain, bool instant = false)
        {
            if (rain && !haveBase) Snapshot();
            target = rain ? 1 : 0;
            if (instant) { k = target; wet = target; ApplyAll(); }
            toastUntil = Time.unscaledTime + 2f;
            Debug.Log("[vrijeme] " + (rain ? "kiša" : "sunčano"));
        }

        // the light as LightPreset (or the scene) leaves it: rain is blended over this, and it comes back when rain stops
        void Snapshot()
        {
            if (!lp) lp = FindFirstObjectByType<LightPreset>();
            if (lp) { lp.Init(); lastGolden = lp.Golden; }
            sun = lp ? lp.GetComponent<Light>() : RenderSettings.sun;
            if (!sun) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; }
            if (!ca)
            {
                var vol = FindFirstObjectByType<Volume>();
                if (vol) { var prof = vol.profile; prof.TryGet(out ca); prof.TryGet(out wbc); prof.TryGet(out bloom); prof.TryGet(out vig); }
            }
            probes ??= FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None);
            b = new B
            {
                sunI = sun ? sun.intensity : 1, shadow = sun ? sun.shadowStrength : 1,
                fogD = RenderSettings.fogDensity, fog = RenderSettings.fogColor,
                sky = RenderSettings.ambientSkyColor, eq = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor,
                exposure = ca ? ca.postExposure.value - CameraFeel.ExposureOffset : 0, /* without the eye adaptation */ contrast = ca ? ca.contrast.value : 0, sat = ca ? ca.saturation.value : 0,
                wb = wbc ? wbc.temperature.value : 0, bloomT = bloom ? bloom.threshold.value : 1, bloomI = bloom ? bloom.intensity.value : 0, vig = vig ? vig.intensity.value : 0,
                skybox = RenderSettings.skybox == skyMat ? b.skybox : RenderSettings.skybox,
                defRefl = RenderSettings.customReflectionTexture, reflMode = RenderSettings.defaultReflectionMode,
            };
            b.probes = new Texture[probes.Length];
            for (int i = 0; i < probes.Length; i++) b.probes[i] = probes[i] ? probes[i].customBakedTexture : null;
            haveBase = true;
            // overcast sky: the base HDRI's mapping copied so the blend starts from exactly what was on screen
            if (!skyMat) { var sh = Shader.Find("Magistrala/OvercastSky"); if (sh) skyMat = new Material(sh) { name = "OvercastSky (runtime)" }; }
            if (skyMat)
            {
                var bs = b.skybox;
                bool pano = bs && bs.HasProperty("_MainTex") && bs.shader.name.Contains("Panoramic");
                skyMat.SetFloat("_HasBase", pano ? 1 : 0);
                if (pano)
                {
                    skyMat.SetTexture("_MainTex", bs.GetTexture("_MainTex"));
                    if (bs.HasProperty("_Tint")) skyMat.SetColor("_Tint", bs.GetColor("_Tint"));
                    if (bs.HasProperty("_Exposure")) skyMat.SetFloat("_Exposure", bs.GetFloat("_Exposure"));
                    if (bs.HasProperty("_Rotation")) skyMat.SetFloat("_Rotation", bs.GetFloat("_Rotation"));
                }
            }
            skyProbeReady = false; // the rain probe is rendered from the final sky (G changes the base)
        }

        Color RainFogNow => Color.Lerp(rainFog, b.fog, .15f);

        void ApplyAll()
        {
            if (!haveBase) return;
            float s = k;
            if (sun) { sun.intensity = Mathf.Lerp(b.sunI, b.sunI * sunMul, s); sun.shadowStrength = Mathf.Lerp(b.shadow, shadowStrength, s); }
            RenderSettings.fogDensity = Mathf.Lerp(b.fogD, fogDensity, s * s);
            RenderSettings.fogColor = Color.Lerp(b.fog, RainFogNow, s);
            float tintK = .12f; // a trace of the base (sunset) light survives the clouds
            RenderSettings.ambientSkyColor = Color.Lerp(b.sky, Color.Lerp(ambSky, b.sky, tintK), s);
            RenderSettings.ambientEquatorColor = Color.Lerp(b.eq, Color.Lerp(ambEq, b.eq, tintK), s);
            RenderSettings.ambientGroundColor = Color.Lerp(b.ground, Color.Lerp(ambGround, b.ground, tintK), s);
            if (ca) { ca.postExposure.value = b.exposure + exposureAdd * s; ca.contrast.value = b.contrast + contrastAdd * s; ca.saturation.value = b.sat + saturationAdd * s; }
            if (wbc) wbc.temperature.value = b.wb + wbAdd * s;
            if (bloom) { bloom.threshold.value = Mathf.Lerp(b.bloomT, Mathf.Max(b.bloomT, 1.1f), s); bloom.intensity.value = Mathf.Lerp(b.bloomI, b.bloomI * .6f, s); }
            if (vig) vig.intensity.value = Mathf.Lerp(b.vig, Mathf.Max(b.vig, vignette), s);
            if (skyMat && s > 0.001f) { skyMat.SetFloat(ID_Overcast, Mathf.SmoothStep(0, 1, s)); RenderSettings.skybox = skyMat; }
            else if (b.skybox) RenderSettings.skybox = b.skybox;
            bool rainProbes = s > .5f && rainRefl;
            for (int i = 0; i < probes.Length; i++)
                if (probes[i]) probes[i].customBakedTexture = rainProbes ? rainRefl : b.probes[i];
            RenderSettings.defaultReflectionMode = rainProbes ? DefaultReflectionMode.Custom : b.reflMode;
            RenderSettings.customReflectionTexture = rainProbes ? rainRefl : b.defRefl;
            DynamicGI.UpdateEnvironment();
            Shader.SetGlobalFloat(ID_Wetness, wet);
            SetCameraColor(s > .001f);
        }

        // reflections under the overcast: one realtime probe that sees only the sky (no geometry), rendered once
        void RenderSkyProbe()
        {
            if (!skyProbe)
            {
                var go = new GameObject("RainSkyProbe"); go.transform.SetParent(transform, false); go.transform.position = new Vector3(0, -5000, 0);
                skyProbe = go.AddComponent<ReflectionProbe>();
                skyProbe.mode = ReflectionProbeMode.Realtime; skyProbe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                skyProbe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing; skyProbe.cullingMask = 0;
                skyProbe.clearFlags = ReflectionProbeClearFlags.Skybox; skyProbe.resolution = 128; skyProbe.hdr = true;
                skyProbe.size = Vector3.one * .01f; skyProbe.importance = 0; skyProbe.intensity = 1;
            }
            // the quality levels have realtime probes off: on just for this one render (restored when it's done)
            if (!rtProbesSaved) { rtProbes = QualitySettings.realtimeReflectionProbes; rtProbesSaved = true; }
            QualitySettings.realtimeReflectionProbes = true;
            skyProbeId = skyProbe.RenderProbe();
        }

        // the lens drops refract the opaque colour texture; TAA (srednje / oštro) would average the thin, fast rain streaks
        // away (history without them, 1/8 weight for the new frame), so rain uses SMAA — sharper and cheaper anyway
        void SetCameraColor(bool on)
        {
            if (!cam) return;
            if (!camData) camData = cam.GetComponent<UniversalAdditionalCameraData>();
            if (!camData) return;
            if (!camColorSaved) { camColorOpt = camData.requiresColorOption; camColorSaved = true; }
            camData.requiresColorOption = on ? CameraOverrideOption.On : camColorOpt;
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp)
            {
                if (!rpOpaqueSaved) { rpOpaque = rp.supportsCameraOpaqueTexture; rpOpaqueSaved = true; }
                rp.supportsCameraOpaqueTexture = on || rpOpaque;
            }
            if (on && camData.antialiasing == AntialiasingMode.TemporalAntiAliasing) { camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing; tookTaa = true; }
            else if (!on && tookTaa) { if (camData.antialiasing == AntialiasingMode.SubpixelMorphologicalAntiAliasing) camData.antialiasing = AntialiasingMode.TemporalAntiAliasing; tookTaa = false; }
        }
        bool rpOpaque, rpOpaqueSaved, tookTaa;

        // fallback when the realtime sky probe gives nothing: a gradient cube (horizon = fog, brighter zenith, dark ground)
        Texture GradientCube()
        {
            const int N = 32;
            var cube = new Cubemap(N, TextureFormat.RGBAHalf, true) { name = "RainReflection (gradient)" };
            Color hor = RenderSettings.fogColor.linear, zen = hor * 1.3f, gnd = hor * .3f;
            for (int f = 0; f < 6; f++)
            {
                var px = new Color[N * N];
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                {
                    float u = (x + .5f) / N * 2 - 1, v = (y + .5f) / N * 2 - 1; // y = 0: top row of the face (cubemap faces are top-down)
                    Vector3 d = f switch { 0 => new Vector3(1, -v, -u), 1 => new Vector3(-1, -v, u), 2 => new Vector3(u, 1, v), 3 => new Vector3(u, -1, -v), 4 => new Vector3(u, -v, 1), _ => new Vector3(-u, -v, -1) };
                    float dy = d.normalized.y;
                    px[y * N + x] = dy >= 0 ? Color.Lerp(hor, zen, Mathf.Sqrt(dy)) : Color.Lerp(hor, gnd, Mathf.Sqrt(-dy));
                }
                cube.SetPixels(px, (CubemapFace)f);
            }
            cube.Apply(true, false);
            return cube;
        }

        void EnsureRainObjects()
        {
            if (!cam) return;
            int want = RainCount[Mathf.Clamp(RenderQuality.Current, 0, RainCount.Length - 1)];
            if (!rainMat) { var sh = Shader.Find("Magistrala/RainStreaks"); if (sh) rainMat = new Material(sh) { name = "RainStreaks (runtime)" }; }
            if (!lensMat) { var sh = Shader.Find("Magistrala/LensDrops"); if (sh) lensMat = new Material(sh) { name = "LensDrops (runtime)" }; }
            if (rainMat && (!rainGo || rainN != want))
            {
                if (!rainGo)
                {
                    rainGo = new GameObject("Kiša"); rainGo.AddComponent<MeshFilter>();
                    var mr = rainGo.AddComponent<MeshRenderer>(); mr.sharedMaterial = rainMat;
                    mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                }
                if (rainMesh) Destroy(rainMesh);
                rainMesh = BuildRainMesh(want); rainN = want;
                rainGo.GetComponent<MeshFilter>().sharedMesh = rainMesh;
            }
            if (lensMat && !lensGo)
            {
                lensGo = new GameObject("Kapi na leći"); lensGo.AddComponent<MeshFilter>().sharedMesh = BuildQuad();
                var mr = lensGo.AddComponent<MeshRenderer>(); mr.sharedMaterial = lensMat;
                mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
            if (rainGo && rainGo.transform.parent != cam.transform) { rainGo.transform.SetParent(cam.transform, false); rainGo.transform.localPosition = Vector3.zero; }
            if (lensGo && lensGo.transform.parent != cam.transform) { lensGo.transform.SetParent(cam.transform, false); lensGo.transform.localPosition = Vector3.forward; lensGo.transform.localRotation = Quaternion.identity; }
        }

        static Mesh BuildRainMesh(int n)
        {
            var rnd = new System.Random(1234);
            var v = new Vector3[n * 4]; var uv = new Vector2[n * 4]; var r2 = new Vector2[n * 4]; var idx = new int[n * 6];
            for (int i = 0; i < n; i++)
            {
                var p = new Vector3((float)rnd.NextDouble(), (float)rnd.NextDouble(), (float)rnd.NextDouble());
                var r = new Vector2((float)rnd.NextDouble(), (float)i / n); // rnd.y: drawing order for _RainAmount (fade in by count)
                for (int j = 0; j < 4; j++) { v[i * 4 + j] = p; r2[i * 4 + j] = r; }
                uv[i * 4] = new Vector2(-1, 0); uv[i * 4 + 1] = new Vector2(1, 0); uv[i * 4 + 2] = new Vector2(1, 1); uv[i * 4 + 3] = new Vector2(-1, 1);
                idx[i * 6] = i * 4; idx[i * 6 + 1] = i * 4 + 1; idx[i * 6 + 2] = i * 4 + 2; idx[i * 6 + 3] = i * 4; idx[i * 6 + 4] = i * 4 + 2; idx[i * 6 + 5] = i * 4 + 3;
            }
            var m = new Mesh { name = "Kiša", indexFormat = n * 4 > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.vertices = v; m.uv = uv; m.uv2 = r2; m.triangles = idx;
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 20000); // positions are made in the shader
            return m;
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "Kapi na leći" };
            m.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(.5f, .5f, 0), new Vector3(-.5f, .5f, 0) };
            m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            m.bounds = new Bounds(Vector3.zero, Vector3.one);
            return m;
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.vKey.wasPressedThisFrame) Set(target < .5f);
            if (toggleAt > 0 && Time.timeSinceLevelLoad > toggleAt) { toggleAt = -1; Set(target < .5f); }
        }

        void LateUpdate()
        {
            if (!cam || !cam.isActiveAndEnabled) { cam = Camera.main; chase = null; haveCam = false; camData = null; camColorSaved = false; }
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            // G pressed: LightPreset has just applied its full preset (it overwrites every value rain touches) → new base, rain on top
            if (lp && haveBase && lp.Golden != lastGolden) { Snapshot(); ApplyAll(); }
            bool changing = !Mathf.Approximately(k, target);
            if (changing)
            {
                k = Mathf.MoveTowards(k, target, dt / Mathf.Max(.01f, fadeTime));
                ApplyAll();
            }
            float wantWet = target;
            if (!Mathf.Approximately(wet, wantWet))
            {
                wet = Mathf.MoveTowards(wet, wantWet, dt / (wantWet > wet ? fadeTime * 2f : fadeTime * 6f)); // soaks in ~6 s, dries in ~18 s
                Shader.SetGlobalFloat(ID_Wetness, wet);
            }
            // mirage over hot asphalt (Asphalt _Heat): dry day only, not at sunset
            float heat = (1f - wet) * (lp && lp.Golden ? 0f : 1f);
            if (!Mathf.Approximately(heat, lastHeat)) { lastHeat = heat; Shader.SetGlobalFloat(ID_Heat, heat); }
            // reflection probe of the overcast sky once the sky is fully grey
            if (k >= .999f && !skyProbeReady)
            {
                if (skyProbeId < 0) { RenderSkyProbe(); probeT = Time.unscaledTime; }
                else if (skyProbe.IsFinishedRendering(skyProbeId) || Time.unscaledTime - probeT > 2f)
                {
                    var tex = skyProbe.realtimeTexture;
                    bool fin = skyProbe.IsFinishedRendering(skyProbeId), ok = tex && fin && !useGradient;
                    if (ok && tex is RenderTexture src)
                    {
                        // own copy: the probe's texture may go when realtime probes are switched back off
                        try
                        {
                            var copy = new RenderTexture(src.descriptor) { name = "RainReflection (sky)" };
                            copy.Create(); Graphics.CopyTexture(src, copy); tex = copy;
                        }
                        catch (System.Exception e) { Debug.LogWarning("[vrijeme] kopija sonde: " + e.Message); }
                    }
                    var old = rainRefl;
                    rainRefl = ok ? tex : GradientCube(); skyProbeReady = true; skyProbeId = -1;
                    if (old && old != rainRefl) { ApplyAll(); Destroy(old); } // G in rain renders a new one
                    if (rtProbesSaved) QualitySettings.realtimeReflectionProbes = rtProbes;
                    Debug.Log($"[vrijeme] odsjaji: {(ok ? "nebo (realtime sonda)" : "gradijent")} (tekstura {(tex ? tex.width + " px" : "nema")}, gotova {fin}), {probes.Length} sondi");
                    ApplyAll();
                }
            }
            if (k <= 0.001f)
            {
                if (rainGo && rainGo.activeSelf) rainGo.SetActive(false);
                if (lensGo && lensGo.activeSelf) lensGo.SetActive(false);
                Shader.SetGlobalFloat(ID_RainI, 0);
                if (haveBase && target <= 0) { haveBase = false; SetCameraColor(false); } // back to the plain base (the road still dries)
                return;
            }
            if (!cam) return;
            if (camData && camData.antialiasing == AntialiasingMode.TemporalAntiAliasing) SetCameraColor(true); // K put TAA back
            EnsureRainObjects();
            Vector3 cp = cam.transform.position;
            if (haveCam) camVel = Vector3.Lerp(camVel, (cp - lastCamPos) / dt, 1f - Mathf.Exp(-12f * dt));
            if ((cp - lastCamPos).sqrMagnitude > 2500) camVel = Vector3.zero; // teleport
            lastCamPos = cp; haveCam = true;
            if (rainMat && rainGo)
            {
                if (!rainGo.activeSelf) rainGo.SetActive(true);
                var box = new Vector3(36, 18, 36);
                rainMat.SetVector(ID_Box, box);
                // more of the box ahead of the camera (where you look) and ahead of where it's going
                Vector3 fwd = cam.transform.forward; fwd.y = 0; fwd = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
                rainMat.SetVector(ID_Centre, cp + fwd * 8f + Vector3.up * 2f + camVel * .25f);
                rainMat.SetVector(ID_CamVel, camVel);
                rainMat.SetVector(ID_Wind, new Vector3(1.2f, 0, .6f));
                rainMat.SetFloat(ID_Speed, 8.5f);
                rainMat.SetFloat(ID_Shutter, .04f);
                rainMat.SetFloat(ID_Amount, k);
                if (!chase) chase = cam.GetComponent<TruckChaseCamera>();
                rainMat.SetFloat(ID_Near, chase && chase.CabinView ? 2.3f : .7f);
                Shader.SetGlobalFloat(ID_RainI, k);
                rainMat.SetFloat(ID_Alpha, .5f);
                Color fc = RenderSettings.fogColor.linear;
                rainMat.SetColor(ID_RainColor, new Color(fc.r * 1.7f, fc.g * 1.7f, fc.b * 1.75f, 1));
            }
            if (lensMat && lensGo)
            {
                if (!lensGo.activeSelf) lensGo.SetActive(true);
                // under a bridge / dry: no new drops (the existing ones just dry with k)
                lensMat.SetFloat(ID_LensAmount, k * .9f);
                lensMat.SetFloat(ID_LensWind, Mathf.Clamp01(camVel.magnitude / 25f));
            }
        }

        void OnGUI()
        {
            if (Time.unscaledTime > toastUntil) return;
            toastStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Box(new Rect(Screen.width * .5f - 200, Screen.height * .25f, 400, 46), "Vrijeme: " + (target > .5f ? "oblačno, kiša" : "sunčano") + "  (V)", toastStyle);
        }

        void OnDisable()
        {
            Shader.SetGlobalFloat(ID_Wetness, 0);
            if (haveBase) { k = 0; ApplyAll(); }
            SetCameraColor(false);
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp && rpOpaqueSaved) rp.supportsCameraOpaqueTexture = rpOpaque;
        }
    }
}
