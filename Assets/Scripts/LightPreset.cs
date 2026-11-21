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
        [Header("noć (Shift+G, -night 1): the sunset sky darkened, a faint cool moonlight, the street lamps on")]
        public float nightSky = .12f, nightSun = .1f, nightAmb = .35f, nightExposure = 0f, nightReflect = .15f;
        public Color moon = new Color(.62f, .72f, 1f);
        bool night; Material nightSky0; float dayRefl = 1;
        /// the street lamps' level (StreetLights): sunset .55 (they come on at dusk), night 1, day 0
        public float LampLevel => night ? 1 : golden ? .55f : 0;
        public bool Night => night;
        [Header("zalazak (GraphicsPass.Sunset)")]
        public Material sunsetSky;
        public ReflectionProbe[] probes;
        public Texture[] sunsetProbes;
        public float sunElev = 4f, sunYaw = 76f;
        public Color sunsetSkyAmb = new Color(.42f, .45f, .62f), sunsetHorizon = new Color(.85f, .62f, .5f), sunsetFog = new Color(.78f, .6f, .55f);

        [Header("zalazak: ugađanje")]
        public float ambMul = 1.45f, exposureOff = -.1f, sunIntensity = 2.3f;
        public Color splitShadows = new Color(.45f, .47f, .57f), splitHighlights = new Color(.7f, .54f, .4f);

        struct S
        {
            public Quaternion rot; public float temp, intensity, fogD, exposure, contrast, sat, wb, bloomT, bloomI, vig, shadowStr;
            public Color fog, sky, eq, ground, filter, sun, splitS, splitH; public Material skybox; public Texture[] probes;
        }
        S day; bool golden, ready; Light sun;
        ColorAdjustments ca; WhiteBalance wb; Bloom bloom; SplitToning split; Vignette vig; ShadowsMidtonesHighlights smh; Vector4 smhShadows;

        void Start() => Init();

        public void Init()
        {
            if (ready) return; ready = true;
            sun = GetComponent<Light>(); dayRefl = RenderSettings.reflectionIntensity;
            var vol = FindFirstObjectByType<Volume>();
            var prof = vol.profile; // runtime instance (never the asset)
            prof.TryGet(out ca); prof.TryGet(out wb); prof.TryGet(out bloom); prof.TryGet(out vig);
            if (prof.TryGet(out smh)) smhShadows = smh.shadows.value;
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

        S NightS()
        {
            var g = Gold();
            // a cool moon high in the south-east (soft long shadows), the sunset sky almost black, ambient a dark blue
            g.rot = Quaternion.Euler(38, 150, 0); g.temp = 9000; g.intensity = nightSun; g.sun = moon; g.shadowStr = .5f;
            g.sky = new Color(.16f, .2f, .32f) * nightAmb; g.eq = new Color(.12f, .14f, .2f) * nightAmb; g.ground = new Color(.05f, .05f, .06f) * nightAmb;
            g.fog = new Color(.03f, .035f, .05f); g.fogD = .00016f;
            g.exposure = day.exposure + nightExposure; g.contrast = day.contrast + 8; g.sat = day.sat - 10; g.wb = day.wb - 4;
            g.bloomT = .9f; g.bloomI = .9f; g.vig = .28f;
            g.splitS = new Color(.42f, .46f, .6f); g.splitH = new Color(.62f, .55f, .45f);
            if (g.skybox)
            {
                if (!nightSky0) { nightSky0 = new Material(g.skybox) { name = "SkyNight (runtime)" }; }
                if (nightSky0.HasProperty("_Exposure")) nightSky0.SetFloat("_Exposure", nightSky);
                if (nightSky0.HasProperty("_Tint")) nightSky0.SetColor("_Tint", new Color(.35f, .4f, .55f));
                g.skybox = nightSky0;
            }
            return g;
        }

        void Apply(S s)
        {
            RenderSettings.reflectionIntensity = night ? nightReflect * dayRefl : dayRefl;
            // the grade's shadow lift (+0.04) turns a night into a grey veil: blacks stay black at night
            if (smh) smh.shadows.value = night ? new Vector4(smhShadows.x, smhShadows.y, smhShadows.z, -.02f) : smhShadows;
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

        public void SetGolden(bool on) { Init(); golden = on; night = false; Apply(golden ? Gold() : day); }
        /// night: also Golden (truck / traffic headlights follow Golden)
        public void SetNight(bool on) { Init(); night = on; golden = on; Apply(on ? NightS() : day); }
        public bool Golden => golden;

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.gKey.wasPressedThisFrame)
            {
                if (kb.shiftKey.isPressed) SetNight(!night);
                else SetGolden(!golden);
            }
        }
        void OnDisable() { if (golden && ready) Apply(day); }
    }
}
