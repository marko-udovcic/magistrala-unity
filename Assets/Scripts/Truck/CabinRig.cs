// Cab interior animation for the driven vans: the steering wheel turns with the front wheels and the
// speed / rpm needles follow the truck:
//   steering     node "steering", spins about its local Z (along the column), ±540° at full lock
//   needle_*     nodes "needle_speed" / "needle_rpm", local Z (normal of the dial), modelled at 0; the 260° sweep clockwise
//                from the driver is +260° about local Z in Unity's mirrored-X frame.
// Added by TruckController once the model is found; nodes it does not find are simply skipped.
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CabinRig : MonoBehaviour
{
    public float speedMax = 120f, rpmMax = 3000f, sweepDeg = 240f;   // the Atego v3 dials (0…120 km/h, 0…30 ×100 r/min, 240°)
    /// the speedo's first printed mark (the needle's rest; the Galeb's T1-style dial starts at 10) and its own sweep
    public float speedMin = 0f, speedSweepDeg = 240f;
    /// road-wheel angle (rad) → steering-wheel angle (rad); ~0.6 rad full lock → ~9.4 rad (540°)
    public float steerRatio = 15f;

    TruckController tc;
    Transform steering, needleSpeed, needleRpm;
    Quaternion steering0, speed0, rpm0;
    struct Backlight { public Material material; public Color color; }
    readonly System.Collections.Generic.List<Backlight> backlights = new System.Collections.Generic.List<Backlight>();
    Magistrala.LightPreset preset;
    float lastLevel = -1;
    static readonly int Emis = Shader.PropertyToID("emissiveFactor"), Emission = Shader.PropertyToID("_EmissionColor");

    public void Bind(TruckController controller, Transform model)
    {
        tc = controller;
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "steering" && !steering) { steering = t; steering0 = t.localRotation; }
            else if (t.name == "needle_speed" && !needleSpeed) { needleSpeed = t; speed0 = t.localRotation; }
            else if (t.name == "needle_rpm" && !needleRpm) { needleRpm = t; rpm0 = t.localRotation; }
        }
    }

    void Start()
    {
        // Use the existing glTF emissive texture: only the markings glow, the cluster stays dark.
        // VehicleLook leaves these interior materials intact. Cache instances once, never query materials in LateUpdate.
        if (!tc || !PlayerTruck.Modern(tc.spec.model)) return;
        preset = FindFirstObjectByType<Magistrala.LightPreset>();
        foreach (var r in tc.model.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.materials)
            {
                if (!m) continue;
                string name = m.name.Replace(" (Instance)", "");
                if (name != "Gauges" && name != "Needle") continue;
                m.EnableKeyword(m.HasProperty(Emis) ? "_EMISSIVE" : "_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                backlights.Add(new Backlight { material = m, color = name == "Needle" ? new Color(1, .25f, .05f) : Color.white });
            }
    }

    void LateUpdate()
    {
        if (!tc) return;
        float level = preset ? preset.LampLevel : 0;
        if (level != lastLevel)
        {
            lastLevel = level;
            foreach (var light in backlights)
                light.material.SetColor(light.material.HasProperty(Emis) ? Emis : Emission, light.color * Mathf.Lerp(1.1f, 2f, level));
        }
        if (steering) steering.localRotation = steering0 * Quaternion.AngleAxis(Mathf.Clamp(tc.SteerAngle * steerRatio * Mathf.Rad2Deg, -560f, 560f), Vector3.forward);
        if (needleSpeed) needleSpeed.localRotation = speed0 * Quaternion.AngleAxis(speedSweepDeg * Mathf.Clamp((tc.SpeedKmh - speedMin) / (speedMax - speedMin), 0f, 1.01f), Vector3.forward);
        if (needleRpm) needleRpm.localRotation = rpm0 * Quaternion.AngleAxis(sweepDeg * Mathf.Clamp(tc.EngineRpm / rpmMax, 0f, 1.01f), Vector3.forward);
    }
}
