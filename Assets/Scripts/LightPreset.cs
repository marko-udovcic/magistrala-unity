using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Magistrala
{
    /// G: day ↔ golden hour (low warm sun, orange-pink haze, warm grade). Lives on the Sun.
    public class LightPreset : MonoBehaviour
    {
        struct S { public Quaternion rot; public float temp, intensity, fogD, skyExp; public Color fog, sky, eq, ground, filter; public float wb; }
        S day, gold; bool golden; Light sun; ColorAdjustments ca; WhiteBalance wb; Material sky;

        void Start()
        {
            sun = GetComponent<Light>(); sky = RenderSettings.skybox;
            var vol = FindFirstObjectByType<Volume>(); vol.profile.TryGet(out ca); vol.profile.TryGet(out wb);
            day = new S { rot = transform.rotation, temp = sun.colorTemperature, intensity = sun.intensity, fogD = RenderSettings.fogDensity, fog = RenderSettings.fogColor,
                sky = RenderSettings.ambientSkyColor, eq = RenderSettings.ambientEquatorColor, ground = RenderSettings.ambientGroundColor,
                filter = ca ? ca.colorFilter.value : Color.white, wb = wb ? wb.temperature.value : 0, skyExp = sky && sky.HasProperty("_Exposure") ? sky.GetFloat("_Exposure") : 1 };
            var e = transform.rotation.eulerAngles;
            gold = new S { rot = Quaternion.Euler(7f, e.y + 35f, 0), temp = 3300, intensity = 2.2f, fogD = .00016f, fog = new Color(.95f, .72f, .58f),
                sky = new Color(.72f, .62f, .66f), eq = new Color(.8f, .58f, .45f), ground = new Color(.35f, .26f, .2f), filter = new Color(1f, .9f, .82f), wb = 22, skyExp = .75f };
        }

        void Apply(S s)
        {
            transform.rotation = s.rot; sun.colorTemperature = s.temp; sun.intensity = s.intensity;
            RenderSettings.fogDensity = s.fogD; RenderSettings.fogColor = s.fog;
            RenderSettings.ambientSkyColor = s.sky; RenderSettings.ambientEquatorColor = s.eq; RenderSettings.ambientGroundColor = s.ground;
            if (ca) ca.colorFilter.value = s.filter; if (wb) wb.temperature.value = s.wb;
            if (sky && sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", s.skyExp);
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.gKey.wasPressedThisFrame) { golden = !golden; Apply(golden ? gold : day); }
        }
        void OnDisable() { if (golden) Apply(day); }
    }
}
