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
        /// Actual applied dipped-beam state, including automatic sunset/rain and manual overrides.
        public bool IsOn => lastOn;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            var t = FindFirstObjectByType<TruckController>();
            if (t && !t.GetComponent<TruckLights>()) t.gameObject.AddComponent<TruckLights>();
        }

        void Start()
        {
            truck = GetComponent<TruckController>(); preset = FindFirstObjectByType<LightPreset>();
            VehicleLook.Apply(transform, truck && truck.spec != null ? truck.spec.model : "l4h1"); // paint, glass, lamp submeshes (before the lenses are collected)
            Transform headL = null, headR = null;
            foreach (var t in truck.model.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "a_headL") headL = t;
                else if (t.name == "a_headR") headR = t;
            }
            Renderer head = null; Bounds headB = default; bool headSub = false;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "body_L_head" || (!head && VehicleLook.Off && r.name.StartsWith("Phare"))) head = r; // (Kombi: both lamps in one "Phare" mesh, VehicleLook splits it)
                var mats = r.materials; // instances: only this truck glows
                foreach (var m in mats)
                {
                    if (!m) continue; string n = m.name.Replace(" (Instance)", "");
                    if (!n.StartsWith("L_")) continue;
                    if (!lens.TryGetValue(n, out var l)) lens[n] = l = new List<Material>();
                    l.Add(m); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                    // glTFast graphs: emissiveFactor (× emissiveTexture, white when none) under _EMISSIVE; URP Lit: _EmissionColor
                    if (m.HasProperty(ID_Emis)) m.EnableKeyword("_EMISSIVE"); else m.EnableKeyword("_EMISSION");
                    // head lamps that are a submesh of a bigger part (VehicleLook: the Kombi's "Phare", the Galeb's body atlas)
                    if (n == "L_head" && !head && r.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh)
                    {
                        int si = System.Array.IndexOf(mats, m);
                        if (si >= 0 && si < mf.sharedMesh.subMeshCount)
                        {
                            var sb = mf.sharedMesh.GetSubMesh(si).bounds; var M = r.transform.localToWorldMatrix;
                            var wb = new Bounds(M.MultiplyPoint3x4(sb.center), Vector3.zero);
                            for (int i = 0; i < 8; i++) wb.Encapsulate(M.MultiplyPoint3x4(sb.center + Vector3.Scale(sb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
                            headB = wb; headSub = true;
                        }
                    }
                }
            }
            if ((headL && headR) || head || headSub)
            {
                var b = head ? head.bounds : headB; var right = transform.right; var c = b.center;
                float half = Mathf.Abs(Vector3.Dot(b.extents, new Vector3(Mathf.Abs(right.x), Mathf.Abs(right.y), Mathf.Abs(right.z))));
                var fwd = Vector3.ProjectOnPlane(c - transform.position, transform.up).normalized;
                foreach (float s in new[] { -1f, 1f })
                {
                    var go = new GameObject("headlight"); go.transform.SetParent(transform, false);
                    var anchor = s < 0 ? headL : headR;
                    go.transform.position = anchor ? anchor.position + transform.forward * .05f : c + right * s * Mathf.Max(0, half - .18f) + fwd * .15f;
                    if (anchor) fwd = transform.forward;
                    go.transform.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(4f, Vector3.Cross(fwd, transform.up) * -1f) * fwd, transform.up);
                    var L = go.AddComponent<Light>(); L.type = LightType.Spot; L.range = 70; L.spotAngle = 62; L.innerSpotAngle = 30;
                    L.intensity = 60; L.color = new Color(1f, .95f, .85f); L.shadows = LightShadows.None; L.renderMode = LightRenderMode.ForcePixel;
                    L.enabled = false; beams.Add(L);
                }
            }
            Set(false, 0, false);
            (GetComponent<TurnSignals>() ?? gameObject.AddComponent<TurnSignals>()).Bind(truck);
        }

        static readonly int ID_Emis = Shader.PropertyToID("emissiveFactor"), ID_EmissionColor = Shader.PropertyToID("_EmissionColor");
        void Glow(string mat, Color c)
        {
            if (!lens.TryGetValue(mat, out var l)) return;
            foreach (var m in l) m.SetColor(m.HasProperty(ID_Emis) ? ID_Emis : ID_EmissionColor, c);
        }

        void Set(bool on, float brake, bool rev)
        {
            foreach (var b in beams) b.enabled = on;
            Glow("L_head", on ? new Color(1f, .96f, .88f) * 6f : Color.black);
            Glow("L_headrefl", on ? new Color(1f, .95f, .85f) * .3f : Color.black); // the vans' chrome bowls (VehicleLook)
            Glow("L_fog", on ? new Color(1f, .92f, .75f) * 4f : Color.black);
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
            bool auto = (preset && preset.Golden) || WeatherSystem.Raining; // dipped beams at sunset and in rain
            bool on = manual ? manualOn : auto;
            if (manual && auto != lastAutoSeen) { manual = false; on = auto; } // changing the time of day hands control back
            lastAutoSeen = auto;
            float brake = truck ? (truck.BrakeLevel > .05f ? 1f : 0f) : 0f; float rev = truck && truck.Gear < 0 ? 1 : 0;
            if (on != lastOn || brake != lastBrake || rev != lastRev) { Set(on, brake, rev > 0); lastOn = on; lastBrake = brake; lastRev = rev; }
        }
    }
}
