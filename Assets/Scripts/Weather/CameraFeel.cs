using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Magistrala
{
    /// B: kamera živa ↔ mirna (remembered). A camera operator's feel on top of TruckChaseCamera, rotation only (the chase camera
    /// sets its rotation from scratch every frame, so nothing feeds back into its smoothing):
    ///  - sway: rolls a little with the truck's lateral g, dips with braking, nods over bumps (spring-damped, lags the truck);
    ///  - speed shake: fine vibration growing with speed, plus a slow wobble;
    ///  - FOV +4° above ~60 km/h (on top of the chase camera's own +7°);
    ///  - motion blur on srednje / oštro (camera + object motion vectors, light);
    ///  - eye adaptation: shaded camera (ray to the sun blocked) opens the exposure up by ~⅓ EV over a second, closes it in the sun.
    /// `-mobitel`: phone-in-hand look for TikTok capture — handheld drift and tremor, film grain, a touch of chromatic aberration
    /// and softness (lower FSR sharpness).
    [DefaultExecutionOrder(150)] // after TruckChaseCamera (100), before WeatherSystem (200)
    public class CameraFeel : MonoBehaviour
    {
        const string PREF = "kamera_ziva";
        public static bool Live { get; private set; } = true;
        public static bool Mobitel { get; private set; }
        public static float ExposureOffset { get; private set; } // eye adaptation currently added to ColorAdjustments.postExposure

        Camera cam; TruckChaseCamera chase; Rigidbody body;
        Vector3 lastV, accel; bool haveV;
        float roll, rollV, pitch, pitchV, fovAdd, expTarget, exposureLastWritten = float.NaN;
        ColorAdjustments ca; MotionBlur mb; FilmGrain grain; ChromaticAberration chroma; bool profReady;
        float toastUntil; GUIStyle toastStyle;
        Light sun; readonly RaycastHit[] hits = new RaycastHit[8]; int rayFrame;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            if (FindFirstObjectByType<CameraFeel>()) return;
            var go = new GameObject("CameraFeel"); DontDestroyOnLoad(go); go.AddComponent<CameraFeel>();
        }

        void Start()
        {
            Live = PlayerPrefs.GetInt(PREF, 1) == 1;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-mobitel") Mobitel = true;
                if (args[i] == "-kamera" && i + 1 < args.Length) Live = args[i + 1] != "0";
            }
            if (Mobitel) Debug.Log("[kamera] mobitel");
        }

        void InitProfile()
        {
            if (profReady) return; profReady = true;
            var vol = FindFirstObjectByType<Volume>(); if (!vol) return;
            var prof = vol.profile; // runtime instance
            prof.TryGet(out ca);
            if (!prof.TryGet(out mb)) mb = prof.Add<MotionBlur>();
            if (!prof.TryGet(out grain)) grain = prof.Add<FilmGrain>();
            if (!prof.TryGet(out chroma)) chroma = prof.Add<ChromaticAberration>();
            mb.mode.Override(MotionBlurMode.CameraAndObjects); mb.quality.Override(MotionBlurQuality.Low);
            mb.intensity.Override(.2f); mb.clamp.Override(.02f);
            grain.type.Override(FilmGrainLookup.Medium2); grain.intensity.Override(.32f); grain.response.Override(.75f);
            chroma.intensity.Override(.12f);
            ApplyStatic();
        }

        void ApplyStatic()
        {
            if (mb) mb.active = (Live || Mobitel) && RenderQuality.Current >= 1;
            if (grain) grain.active = Mobitel;
            if (chroma) chroma.active = Mobitel;
            if (Mobitel)
            {
                var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
                if (rp) { rp.fsrOverrideSharpness = true; rp.fsrSharpness = .35f; } // a phone video is never crisp
            }
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.bKey.wasPressedThisFrame)
            {
                Live = !Live; PlayerPrefs.SetInt(PREF, Live ? 1 : 0); toastUntil = Time.unscaledTime + 2f;
                ApplyStatic();
                Debug.Log("[kamera] " + (Live ? "živa" : "mirna"));
            }
        }

        int lastQuality = -1;
        void LateUpdate()
        {
            if (!cam || !cam.isActiveAndEnabled) { cam = Camera.main; chase = cam ? cam.GetComponent<TruckChaseCamera>() : null; body = null; haveV = false; }
            if (!cam || !chase || !chase.target) return;
            InitProfile();
            if (lastQuality != RenderQuality.Current) { lastQuality = RenderQuality.Current; ApplyStatic(); }
            float dt = Mathf.Clamp(Time.deltaTime, 1e-4f, .1f);
            if (!body) body = chase.target.GetComponent<Rigidbody>();
            Transform truck = chase.target.transform;
            Vector3 v = body ? body.linearVelocity : Vector3.zero;
            if (haveV) accel = Vector3.Lerp(accel, (v - lastV) / dt, 1f - Mathf.Exp(-6f * dt));
            lastV = v; haveV = true;
            float kmh = v.magnitude * 3.6f, t = Time.time;
            bool cabin = chase.CabinView;
            Vector3 euler = Vector3.zero;
            if (Live || Mobitel)
            {
                float lat = Vector3.Dot(accel, truck.right), lon = Vector3.Dot(accel, truck.forward), vert = Vector3.Dot(accel, Vector3.up);
                // sway: spring toward the target lean, so it lags and settles like a hand-held rig on a car
                float rollT = Mathf.Clamp(-lat * .3f, -2.2f, 2.2f) * (cabin ? .5f : 1f);
                float pitchT = Mathf.Clamp(-lon * .1f + vert * .05f, -1.4f, 1.4f) * (cabin ? .6f : 1f);
                Spring(ref roll, ref rollV, rollT, 5f, .55f, dt);
                Spring(ref pitch, ref pitchV, pitchT, 6f, .5f, dt);
                euler += new Vector3(pitch, 0, roll);
                // speed shake: fine (≈11–17 Hz) road vibration and a slow wobble, both growing with speed
                float sk = Mathf.Clamp01(kmh / 90f); sk *= sk;
                float fine = .06f * sk * (cabin ? 1.6f : 1f), slow = .12f * sk;
                euler += new Vector3(
                    (Mathf.PerlinNoise(t * 13f, 1.3f) - .5f) * 2f * fine + (Mathf.PerlinNoise(t * .7f, 4.1f) - .5f) * 2f * slow,
                    (Mathf.PerlinNoise(t * 11f, 7.7f) - .5f) * 2f * fine * .7f + (Mathf.PerlinNoise(t * .5f, 9.2f) - .5f) * 2f * slow,
                    (Mathf.PerlinNoise(t * 17f, 3.3f) - .5f) * 2f * fine * .5f);
                fovAdd = Mathf.Lerp(fovAdd, cabin ? 0f : 4f * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(55f, 100f, kmh)), 1f - Mathf.Exp(-1.5f * dt));
            }
            else fovAdd = Mathf.Lerp(fovAdd, 0, 1f - Mathf.Exp(-3f * dt));
            if (Mobitel)
            {
                // a phone held out of a car window / by a passenger: slow drift, breathing, a little tremor
                euler += new Vector3(
                    (Mathf.PerlinNoise(t * .35f, 11.1f) - .5f) * 1.6f + (Mathf.PerlinNoise(t * 2.3f, 5.5f) - .5f) * .25f + Mathf.Sin(t * 1.6f) * .12f,
                    (Mathf.PerlinNoise(t * .3f, 21.7f) - .5f) * 1.8f + (Mathf.PerlinNoise(t * 2.1f, 8.8f) - .5f) * .25f,
                    (Mathf.PerlinNoise(t * .4f, 31.3f) - .5f) * 1.4f + (Mathf.PerlinNoise(t * 3.1f, 2.2f) - .5f) * .15f);
            }
            if (DroneCamera.Active) { euler = Vector3.zero; fovAdd = 0; } // the drone flies smooth, it is not on the truck
            if (euler.sqrMagnitude > 0) cam.transform.rotation *= Quaternion.Euler(euler);
            cam.fieldOfView += fovAdd;
            EyeAdaptation(dt);
            SunGlare(dt);
        }

        // driving into a low sun: a soft warm glow on the sun and two faint ghosts (SRP lens flare, occluded by the depth buffer,
        // so it disappears behind hills, buildings and trees). Built from procedural shapes, no textures. Off in rain.
        LensFlareComponentSRP flare; float flareK;
        void SunGlare(float dt)
        {
            if (!sun) return;
            if (!flare)
            {
                flare = sun.GetComponent<LensFlareComponentSRP>();
                if (!flare)
                {
                    var data = ScriptableObject.CreateInstance<LensFlareDataSRP>();
                    data.name = "Sunce (runtime)";
                    data.elements = new[]
                    {
                        Flare(SRPLensFlareType.Circle, 0f, 1.0f, 9f, new Color(1f, .92f, .8f), .9f),
                        Flare(SRPLensFlareType.Circle, 0f, .35f, 26f, new Color(1f, .85f, .7f), 1.6f),
                        Flare(SRPLensFlareType.Polygon, .55f, .07f, 2.2f, new Color(.7f, .85f, 1f), .5f),
                        Flare(SRPLensFlareType.Circle, 1.25f, .05f, 3.5f, new Color(1f, .8f, .6f), .6f),
                    };
                    flare = sun.gameObject.AddComponent<LensFlareComponentSRP>();
                    flare.lensFlareData = data;
                    flare.useOcclusion = true; flare.occlusionRadius = .02f; flare.sampleCount = 12; flare.allowOffScreen = false;
                    flare.scale = 1f; flare.attenuationByLightShape = false;
                }
            }
            float want = Live && !WeatherSystem.Raining && sun.intensity > .5f ? .45f : 0f;
            flareK = Mathf.MoveTowards(flareK, want, dt);
            flare.intensity = flareK;
            if (flare.enabled != flareK > 0) flare.enabled = flareK > 0;
        }

        static LensFlareDataElementSRP Flare(SRPLensFlareType type, float pos, float intensity, float size, Color tint, float fall)
        {
            return new LensFlareDataElementSRP
            {
                flareType = type, position = pos, localIntensity = intensity, uniformScale = size, tint = tint, fallOff = fall,
                blendMode = SRPLensFlareBlendMode.Additive, modulateByLightColor = true, sideCount = 6, edgeOffset = .3f,
            };
        }

        // critically-ish damped spring (frequency f Hz, damping ratio z)
        static void Spring(ref float x, ref float v, float target, float f, float z, float dt)
        {
            float w = 2f * Mathf.PI * f * .25f;
            v += (-2f * z * w * v - w * w * (x - target)) * dt;
            x += v * dt;
        }

        void EyeAdaptation(float dt)
        {
            if (!ca) return;
            if (!sun || !sun.isActiveAndEnabled) { sun = RenderSettings.sun; if (!sun) { var lp = FindFirstObjectByType<LightPreset>(); if (lp) sun = lp.GetComponent<Light>(); } }
            // someone else (LightPreset G, WeatherSystem) wrote the exposure: their value is the new base
            float cur = ca.postExposure.value;
            float baseExp = !float.IsNaN(exposureLastWritten) && Mathf.Abs(cur - exposureLastWritten) < 1e-5f ? cur - ExposureOffset : cur;
            if (Live && sun && sun.shadowStrength > .5f && sun.intensity > .5f)
            {
                if (++rayFrame % 4 == 0) // 15 rays a second is plenty for a 1 s adaptation
                {
                    Vector3 o = cam.transform.position, d = -sun.transform.forward;
                    int n = Physics.RaycastNonAlloc(o, d, hits, 80f, ~0, QueryTriggerInteraction.Ignore); // local shade only (bridge, buildings): a low sun behind a far hill is not "shade"
                    bool shade = false;
                    for (int i = 0; i < n; i++) if (!hits[i].rigidbody && !(chase && chase.target && hits[i].transform.IsChildOf(chase.target.transform))) { shade = true; break; }
                    expTarget = shade ? .35f : 0f;
                }
            }
            else expTarget = 0;
            // opening up (into shade) takes ~1.2 s, closing down (into the sun) ~0.5 s — like a camera's auto exposure
            float rate = expTarget > ExposureOffset ? 1.6f : 4f;
            float off = Mathf.Lerp(ExposureOffset, expTarget, 1f - Mathf.Exp(-rate * dt));
            if (Mathf.Abs(off) < 1e-4f && expTarget == 0) off = 0;
            ExposureOffset = off;
            float w = baseExp + off;
            if (Mathf.Abs(w - cur) > 1e-6f) ca.postExposure.value = w;
            exposureLastWritten = ca.postExposure.value;
        }

        void OnGUI()
        {
            if (Time.unscaledTime > toastUntil) return;
            toastStyle ??= new GUIStyle(GUI.skin.box) { fontSize = 22, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            GUI.Box(new Rect(Screen.width * .5f - 200, Screen.height * .32f, 400, 46), "Kamera: " + (Live ? "živa (njihanje, trešnja, zamućenje)" : "mirna") + "  (B)", toastStyle);
        }

        void OnDisable()
        {
            if (ca && ExposureOffset != 0) { ca.postExposure.value -= ExposureOffset; ExposureOffset = 0; }
            if (mb) mb.active = false; if (grain) grain.active = false; if (chroma) chroma.active = false;
        }
    }
}
