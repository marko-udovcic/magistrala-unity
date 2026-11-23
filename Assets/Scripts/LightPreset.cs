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

        // ---- sunčani dan (runtime, over the scene; sunset / night / rain are still computed from the scene's values).
        // Not serialized: the numbers live here (an open scene would otherwise keep the first values it saw); tuning in a
        // player without a rebuild: -suncano-set "name=value;..." (below), A/B: -suncano 0.
        // The scene's day (GraphicsPass.Sky) read as a postcard: sat +14 and WB +7 under ACES clipped orange roofs, the sea and
        // the sunlit walls into flat colour (no tile / stone texture left), the near-zero haze (6e-5) left Kozjak and the far
        // terrain as a sharp beige cut-out against a cobalt sky. Rain looked better because its fog and flat grade tie it
        // together. The sunny day keeps the sun and the HDRI, but: a summer haze (fog 2.4e-4: the town stays almost clear, the
        // hills and the far sea fade into a pale blue, the sky's horizon ends in the same colour), a calmer, less warm grade
        // (sat −4, WB +2, exposure .06), a touch less sun / more sky light. No measurable GPU cost.
        [System.NonSerialized] public bool sunnyLook = true;
        [System.NonSerialized] public float daySun = 2.6f, dayAmbMul = 1.1f, dayShadow = .75f, dayExposure = .06f, dayContrast = 8f, daySat = -4f, dayWb = 2f;
        // 23. 11. (VEGETACIJA_I_TLO.md: "fog slightly blue, clearly visible from ~600 m", the world's edge hidden): 2.4e-4 → 4e-4
        // (13 % → 21 % at 600 m, 51 % → 70 % at 3 km: Kozjak's top and the far shore go into a pale blue), a touch bluer
        [System.NonSerialized] public float dayFogDensity = .0004f, dayBloomThreshold = 1.3f;
        [System.NonSerialized] public Color dayFog = new Color(.70f, .78f, .89f);
        /// horizon haze in the sky (Resources/Vrijeme/HazySky.shader): the sky ends in the fog colour like the hazed hills
        [System.NonSerialized] public float dayHaze = .8f, dayHazePow = 6f, dayHazeMul = 1.15f;
        [System.NonSerialized] public FogMode dayFogMode = FogMode.Exponential; // (exp² looked unfogged in the player: its variants are probably stripped)

        [Header("zalazak: ugađanje")]
        public float ambMul = 1.45f, exposureOff = -.1f, sunIntensity = 2.3f;
        public Color splitShadows = new Color(.45f, .47f, .57f), splitHighlights = new Color(.7f, .54f, .4f);

        struct S
        {
            public FogMode fogMode; public Quaternion rot; public float temp, intensity, fogD, exposure, contrast, sat, wb, bloomT, bloomI, vig, shadowStr;
            public Color fog, sky, eq, ground, filter, sun, splitS, splitH; public Material skybox; public Texture[] probes;
        }
        S day, scene; bool golden, ready; Light sun;
        /// how much the sunny day's grade differs from the scene's (x exposure, y contrast, z saturation, w white balance):
        /// zero at sunset / night. WeatherSystem takes it off again so the rain looks exactly as it did over the scene's day.
        public Vector4 GradeShift => golden || night || !ready ? Vector4.zero
            : new Vector4(day.exposure - scene.exposure, day.contrast - scene.contrast, day.sat - scene.sat, day.wb - scene.wb);
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
            scene = new S
            {
                fogMode = RenderSettings.fogMode, rot = transform.rotation, temp = sun.colorTemperature, intensity = sun.intensity, sun = sun.color, shadowStr = sun.shadowStrength,
                fogD = RenderSettings.fogDensity, fog = RenderSettings.fogColor,
                sky = RenderSettings.ambientSkyColor, eq = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor,
                filter = ca ? ca.colorFilter.value : Color.white, exposure = ca ? ca.postExposure.value : 0, contrast = ca ? ca.contrast.value : 0, sat = ca ? ca.saturation.value : 0,
                wb = wb ? wb.temperature.value : 0, bloomT = bloom ? bloom.threshold.value : 1, bloomI = bloom ? bloom.intensity.value : 0, vig = vig ? vig.intensity.value : 0,
                splitS = new Color(.5f, .5f, .5f), splitH = new Color(.5f, .5f, .5f), skybox = RenderSettings.skybox, probes = dayProbes,
            };
            day = scene;
            // A/B: -suncano 0 = the scene's day as it was
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-suncano") sunnyLook = args[i + 1] != "0";
            // tuning without a rebuild: -suncano-set "daySat=2;dayFogDensity=0.0002;dayFogMode=Exponential;dayFog=0.7,0.8,0.9"
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-suncano-set")
                foreach (var kv in args[i + 1].Split(';'))
                {
                    var p = kv.Split('='); if (p.Length != 2) continue;
                    var f = GetType().GetField(p[0].Trim()); if (f == null) { Debug.LogWarning("[sunce] nema polja " + p[0]); continue; }
                    var ci = System.Globalization.CultureInfo.InvariantCulture;
                    if (f.FieldType == typeof(float)) f.SetValue(this, float.Parse(p[1], ci));
                    else if (f.FieldType == typeof(bool)) f.SetValue(this, p[1] == "1" || p[1] == "true");
                    else if (f.FieldType == typeof(FogMode)) f.SetValue(this, System.Enum.Parse(typeof(FogMode), p[1]));
                    else if (f.FieldType == typeof(Color)) { var c = System.Array.ConvertAll(p[1].Split(','), x => float.Parse(x, ci)); f.SetValue(this, new Color(c[0], c[1], c[2])); }
                    Debug.Log($"[sunce] {p[0]} = {p[1]}");
                }
            if (sunnyLook)
            {
                day.intensity = daySun; day.shadowStr = dayShadow;
                day.sky = scene.sky * dayAmbMul; day.eq = scene.eq * dayAmbMul; day.ground = scene.ground * dayAmbMul;
                day.fogMode = dayFogMode; day.fogD = dayFogDensity; day.fog = dayFog;
                day.exposure = dayExposure; day.contrast = dayContrast; day.sat = daySat; day.wb = dayWb;
                day.bloomT = dayBloomThreshold;
                var bs = scene.skybox;
                if (dayHaze > 0 && bs && bs.shader.name.Contains("Panoramic") && Shader.Find("Magistrala/HazyPanoramic") is Shader hs)
                {
                    var m = new Material(hs) { name = "SkyHazy (runtime)" };
                    foreach (var n in new[] { "_Exposure", "_Rotation" }) if (bs.HasProperty(n)) m.SetFloat(n, bs.GetFloat(n));
                    if (bs.HasProperty("_Tint")) m.SetColor("_Tint", bs.GetColor("_Tint"));
                    m.SetTexture("_MainTex", bs.GetTexture("_MainTex"));
                    m.SetFloat("_Haze", dayHaze); m.SetFloat("_HazePow", dayHazePow); m.SetFloat("_HazeMul", dayHazeMul);
                    day.skybox = m;
                }
                Apply(day);
                Debug.Log($"[sunce] dan: sunce {daySun}, ambijent ×{dayAmbMul}, magla {dayFogMode} {dayFogDensity}, ekspozicija {dayExposure}, kontrast {dayContrast}, zasićenje {daySat}, WB {dayWb}");
            }
        }

        S Gold()
        {
            return new S
            {
                fogMode = FogMode.Exponential, rot = Quaternion.Euler(sunElev, sunYaw, 0), temp = 4200, intensity = sunIntensity, sun = new Color(1f, .86f, .72f), shadowStr = .85f,
                fogD = .00011f, fog = sunsetFog,
                sky = sunsetSkyAmb * ambMul, eq = Color.Lerp(sunsetHorizon, sunsetSkyAmb, .45f) * .85f * ambMul, ground = new Color(.16f, .13f, .13f) * ambMul,
                filter = Color.white, exposure = scene.exposure + exposureOff, contrast = scene.contrast + 12, sat = scene.sat + 4, wb = scene.wb + 6,
                bloomT = .95f, bloomI = .55f, vig = .22f,
                // cool violet shadows, warm amber highlights (the ETS2 / film sunset look) instead of one sepia tint
                splitS = splitShadows, splitH = splitHighlights,
                skybox = sunsetSky ? sunsetSky : scene.skybox, probes = sunsetProbes,
            };
        }

        S NightS()
        {
            var g = Gold();
            // a cool moon high in the south-east (soft long shadows), the sunset sky almost black, ambient a dark blue
            g.rot = Quaternion.Euler(38, 150, 0); g.temp = 9000; g.intensity = nightSun; g.sun = moon; g.shadowStr = .5f;
            g.sky = new Color(.16f, .2f, .32f) * nightAmb; g.eq = new Color(.12f, .14f, .2f) * nightAmb; g.ground = new Color(.05f, .05f, .06f) * nightAmb;
            g.fog = new Color(.03f, .035f, .05f); g.fogD = .00016f;
            g.exposure = scene.exposure + nightExposure; g.contrast = scene.contrast + 8; g.sat = scene.sat - 10; g.wb = scene.wb - 4;
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
            RenderSettings.fogMode = s.fogMode; RenderSettings.fogDensity = s.fogD; RenderSettings.fogColor = s.fog;
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
