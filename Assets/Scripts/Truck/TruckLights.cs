using System.Collections.Generic;
using UnityEngine;

namespace Magistrala
{
    /// Truck lamps: two headlight spots + glowing lenses (head, tail, box markers), brake and reverse lamps.
    /// On automatically at sunset (LightPreset / G), L toggles by hand. Attaches itself to the player truck.
    public class TruckLights : MonoBehaviour
    {
        TruckController truck; LightPreset preset;
        readonly List<Light> beams = new List<Light>();
        readonly Dictionary<string, List<Material>> lens = new Dictionary<string, List<Material>>();
        bool manual, manualOn, lastOn, lastAutoSeen; float lastBrake = -1, lastRev = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            var t = FindFirstObjectByType<TruckController>();
            if (t && !t.GetComponent<TruckLights>()) t.gameObject.AddComponent<TruckLights>();
        }

        void Start()
        {
            truck = GetComponent<TruckController>(); preset = FindFirstObjectByType<LightPreset>();
            Renderer head = null;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "body_L_head") head = r;
                var mats = r.materials; // instances: only this truck glows
                foreach (var m in mats)
                {
                    if (!m) continue; string n = m.name.Replace(" (Instance)", "");
                    if (!n.StartsWith("L_")) continue;
                    if (!lens.TryGetValue(n, out var l)) lens[n] = l = new List<Material>();
                    l.Add(m); m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                }
            }
            if (head)
            {
                var b = head.bounds; var right = transform.right; var c = b.center;
                float half = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(right.x), Mathf.Abs(right.y), Mathf.Abs(right.z))));
                var fwd = Vector3.ProjectOnPlane(c - transform.position, transform.up).normalized;
                foreach (float s in new[] { -1f, 1f })
                {
                    var go = new GameObject("headlight"); go.transform.SetParent(transform, false);
                    go.transform.position = c + right * s * Mathf.Max(0, half - .18f) + fwd * .15f;
                    go.transform.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(4f, Vector3.Cross(fwd, transform.up) * -1f) * fwd, transform.up);
                    var L = go.AddComponent<Light>(); L.type = LightType.Spot; L.range = 70; L.spotAngle = 62; L.innerSpotAngle = 30;
                    L.intensity = 60; L.color = new Color(1f, .95f, .85f); L.shadows = LightShadows.None; L.renderMode = LightRenderMode.ForcePixel;
                    L.enabled = false; beams.Add(L);
                }
            }
            Set(false, 0, false);
        }

        void Glow(string mat, Color c)
        {
            if (!lens.TryGetValue(mat, out var l)) return;
            foreach (var m in l) m.SetColor("_EmissionColor", c);
        }

        void Set(bool on, float brake, bool rev)
        {
            foreach (var b in beams) b.enabled = on;
            Glow("L_head", on ? new Color(1f, .96f, .88f) * 6f : Color.black);
            Glow("L_marker", on ? new Color(1f, .55f, .1f) * 3f : Color.black);
            Glow("L_tail", on ? new Color(1f, .05f, .02f) * 3f : Color.black);
            float br = Mathf.Max(on ? .25f : 0f, brake);
            Glow("L_brake", new Color(1f, .03f, .02f) * (br * 8f));
            Glow("L_reverse", rev ? Color.white * 5f : Color.black);
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.lKey.wasPressedThisFrame) { manual = true; manualOn = !lastOn; }
            bool auto = preset && preset.Golden;
            bool on = manual ? manualOn : auto;
            if (manual && auto != lastAutoSeen) { manual = false; on = auto; } // changing the time of day hands control back
            lastAutoSeen = auto;
            float brake = truck ? (truck.BrakeLevel > .05f ? 1f : 0f) : 0f; float rev = truck && truck.Gear < 0 ? 1 : 0;
            if (on != lastOn || brake != lastBrake || rev != lastRev) { Set(on, brake, rev > 0); lastOn = on; lastBrake = brake; lastRev = rev; }
        }
    }
}
