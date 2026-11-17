using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Magistrala
{
    /// G: day ↔ sunset. Lives on the Sun. The sunset is a real sunset sky (CC0 HDRI, its sun turned to the west over
    /// Trogir), a low sun matched to it, sky-coloured ambient (cool shadows, warm light — no orange filter over
    /// everything), reflection probes baked under that sky, darker exposure, stronger bloom. Data filled by
    /// Magistrala ▸ Grafika: zalazak (GraphicsPass.Sunset).
    public class LightPreset : MonoBehaviour
    {
        [Header("zalazak (GraphicsPass.Sunset)")]
        public Material sunsetSky;
        public ReflectionProbe[] probes;
        public Texture[] sunsetProbes;
        public float sunElev = 4f, sunYaw = 76f;
        public Color sunsetSkyAmb = new Color(.42f, .45f, .62f), sunsetHorizon = new Color(.85f, .62f, .5f), sunsetFog = new Color(.78f, .6f, .55f);

        [Header("zalazak: ugađanje")]
        public float ambMul = 1.9f, exposureOff = -.2f, sunIntensity = 2.3f;
        public Color splitShadows = new Color(.45f, .47f, .57f), splitHighlights = new Color(.7f, .54f, .4f);

        struct S
        {
            public Quaternion rot; public float temp, intensity, fogD, exposure, contrast, sat, wb, bloomT, bloomI, vig, shadowStr;
            public Color fog, sky, eq, ground, filter, sun, splitS, splitH; public Material skybox; public Texture[] probes;
        }
        S day; bool golden, ready; Light sun;
        ColorAdjustments ca; WhiteBalance wb; Bloom bloom; SplitToning split; Vignette vig;

        void Start() => Init();

        void Init()
        {
            if (ready) return; ready = true;
            sun = GetComponent<Light>();
            var vol = FindFirstObjectByType<Volume>();
            var prof = vol.profile; // runtime instance (never the asset)
            prof.TryGet(out ca); prof.TryGet(out wb); prof.TryGet(out bloom); prof.TryGet(out vig);
            if (!prof.TryGet(out split)) { split = prof.Add<SplitToning>(); }
            split.active = true;
            Texture[] dayProbes = null;
            if (probes != null) { dayProbes = new Texture[probes.Length]; for (int i = 0; i < probes.Length; i++) dayProbes[i] = probes[i] ? probes[i].customBakedTexture : null; }
            day = new S
            {
                rot = transform.rotation, temp = sun.colorTemperature, intensity = sun.intensity, sun = sun.color, shadowStr = sun.shadowStrength,
                fogD = RenderSettings.fogDensity, fog = RenderSettings.fogColor,
                sky = RenderSettings.ambientSkyColor, eq = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor,
                filter = ca ? ca.colorFilter.value : Color.white, exposure = ca ? ca.postExposure.value : 0, contrast = ca ? ca.contrast.value : 0, sat = ca ? ca.saturation.value : 0,
                wb = wb ? wb.temperature.value : 0, bloomT = bloom ? bloom.threshold.value : 1, bloomI = bloom ? bloom.intensity.value : 0, vig = vig ? vig.intensity.value : 0,
                splitS = new Color(.5f, .5f, .5f), splitH = new Color(.5f, .5f, .5f), skybox = RenderSettings.skybox, probes = dayProbes,
            };
        }

        S Gold()
        {
            return new S
            {
                rot = Quaternion.Euler(sunElev, sunYaw, 0), temp = 4200, intensity = sunIntensity, sun = new Color(1f, .86f, .72f), shadowStr = .85f,
                fogD = .00011f, fog = sunsetFog,
                sky = sunsetSkyAmb * ambMul, eq = Color.Lerp(sunsetHorizon, sunsetSkyAmb, .45f) * .85f * ambMul, ground = new Color(.16f, .13f, .13f) * ambMul,
                filter = Color.white, exposure = day.exposure + exposureOff, contrast = day.contrast + 12, sat = day.sat + 4, wb = day.wb + 6,
                bloomT = .95f, bloomI = .55f, vig = .22f,
                // cool violet shadows, warm amber highlights (the ETS2 / film sunset look) instead of one sepia tint
                splitS = splitShadows, splitH = splitHighlights,
                skybox = sunsetSky ? sunsetSky : day.skybox, probes = sunsetProbes,
            };
        }

        void Apply(S s)
        {
            transform.rotation = s.rot; sun.colorTemperature = s.temp; sun.intensity = s.intensity; sun.color = s.sun; sun.shadowStrength = s.shadowStr;
            RenderSettings.fogDensity = s.fogD; RenderSettings.fogColor = s.fog;
            RenderSettings.ambientSkyColor = s.sky; RenderSettings.ambientEquatorColor = s.eq; RenderSettings.ambientGroundColor = s.ground;
            if (s.skybox) RenderSettings.skybox = s.skybox;
            if (ca) { ca.colorFilter.value = s.filter; ca.postExposure.value = s.exposure; ca.contrast.value = s.contrast; ca.saturation.value = s.sat; }
            if (wb) wb.temperature.value = s.wb;
            if (bloom) { bloom.threshold.value = s.bloomT; bloom.intensity.value = s.bloomI; }
            if (vig) vig.intensity.value = s.vig;
            if (split) { split.shadows.Override(s.splitS); split.highlights.Override(s.splitH); split.balance.Override(-15f); }
            if (probes != null && s.probes != null)
                for (int i = 0; i < probes.Length && i < s.probes.Length; i++) if (probes[i] && s.probes[i]) probes[i].customBakedTexture = s.probes[i];
            DynamicGI.UpdateEnvironment();
        }

        public void SetGolden(bool on) { Init(); golden = on; Apply(golden ? Gold() : day); }
        public bool Golden => golden;

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.gKey.wasPressedThisFrame) SetGolden(!golden);
        }
        void OnDisable() { if (golden && ready) Apply(day); }
    }
}
