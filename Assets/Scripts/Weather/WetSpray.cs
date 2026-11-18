using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Magistrala.Traffic;

namespace Magistrala
{
    /// Spray mist behind the wheels on a wet road (WeatherSystem.Wetness): two emitters on the truck's rear wheels and a small
    /// pool handed to the nearest fast AI cars (TrafficSystem draws them instanced, so the emitters follow their data). World
    /// space, soft round puffs that grow and fade, part of the vehicle's speed inherited (the wake drags the mist along).
    public class WetSpray : MonoBehaviour
    {
        const int CarEmitters = 6;
        const float CarRange = 80f;
        Material mat;
        ParticleSystem[] truckPs = new ParticleSystem[2], carPs = new ParticleSystem[CarEmitters];
        TrafficSystem.Car[] carOf = new TrafficSystem.Car[CarEmitters];
        TruckController truck; Rigidbody body; WheelCollider[] rear;
        TrafficSystem traffic; float pickT; bool built;
        readonly List<TrafficSystem.Car> near = new List<TrafficSystem.Car>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            if (FindFirstObjectByType<WetSpray>()) return;
            var go = new GameObject("WetSpray"); DontDestroyOnLoad(go); go.AddComponent<WetSpray>();
        }

        ParticleSystem Make(string name, float size)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true; main.playOnAwake = false; main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.7f, 1.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.5f * size, 1.1f * size);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = new Color(1, 1, 1, .32f);
            main.maxParticles = 220; main.gravityModifier = .03f;
            main.emitterVelocityMode = ParticleSystemEmitterVelocityMode.Custom;
            var em = ps.emission; em.rateOverTime = 0;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35; sh.radius = .25f;
            var inh = ps.inheritVelocity; inh.enabled = true; inh.mode = ParticleSystemInheritVelocityMode.Initial; inh.curveMultiplier = .45f;
            var drag = ps.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 1.6f; drag.multiplyDragByParticleSize = false; drag.limit = 100f;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, .5f), new Keyframe(1, 2.4f)));
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .12f), new GradientAlphaKey(.5f, .5f), new GradientAlphaKey(0, 1) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.maxParticleSize = .35f; // never a screen-filling puff
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            ps.Play();
            return ps;
        }

        void Build()
        {
            built = true;
            var shd = Shader.Find("Magistrala/Spray"); if (!shd) { enabled = false; return; }
            mat = new Material(shd) { name = "Spray (runtime)" };
            for (int i = 0; i < 2; i++) truckPs[i] = Make("Prskanje kamion " + i, 1.4f);
            for (int i = 0; i < CarEmitters; i++) carPs[i] = Make("Prskanje auto " + i, .8f);
        }

        static void Rate(ParticleSystem ps, float r) { var em = ps.emission; em.rateOverTime = r; }

        void LateUpdate()
        {
            float wet = WeatherSystem.Wetness;
            if (wet < .25f)
            {
                if (built) { foreach (var p in truckPs) Rate(p, 0); foreach (var p in carPs) Rate(p, 0); }
                return;
            }
            if (!built) Build();
            if (!mat) return;
            float k = Mathf.InverseLerp(.25f, 1f, wet);
            mat.SetColor("_Color", RenderSettings.fogColor.linear * 1.25f);
            // truck: the two outermost rear wheels
            if (!truck) { truck = FindFirstObjectByType<TruckController>(); rear = null; }
            if (truck && truck.IsReady && rear == null)
            {
                var ws = truck.GetComponentsInChildren<WheelCollider>();
                if (ws.Length >= 2)
                {
                    System.Array.Sort(ws, (a, b) => truck.transform.InverseTransformPoint(a.transform.position).z.CompareTo(truck.transform.InverseTransformPoint(b.transform.position).z));
                    rear = new[] { ws[0], ws[1] };
                    body = truck.GetComponent<Rigidbody>();
                }
            }
            if (rear != null && body)
            {
                Vector3 v = body.linearVelocity; float spd = v.magnitude;
                for (int i = 0; i < 2; i++)
                {
                    var w = rear[i]; if (!w) continue;
                    Transform t = truck.transform;
                    Vector3 p = w.transform.position - t.up * (w.radius * .75f) - t.forward * (w.radius * .7f);
                    truckPs[i].transform.SetPositionAndRotation(p, Quaternion.LookRotation(-t.forward * .8f + t.up * .6f, t.up));
                    var main = truckPs[i].main; main.emitterVelocity = v;
                    Rate(truckPs[i], k * Mathf.Clamp01((spd - 4f) / 18f) * 70f);
                }
            }
            // AI cars: re-pick the nearest fast ones 4× a second
            if (!traffic) traffic = FindFirstObjectByType<TrafficSystem>();
            Camera cam = Camera.main;
            if (traffic && cam && Time.time > pickT)
            {
                pickT = Time.time + .25f;
                Vector3 cp = cam.transform.position;
                near.Clear();
                foreach (var c in traffic.Cars) if (c.v > 6f && (c.pos - cp).sqrMagnitude < CarRange * CarRange) near.Add(c);
                near.Sort((a, b) => (a.pos - cp).sqrMagnitude.CompareTo((b.pos - cp).sqrMagnitude));
                if (near.Count > CarEmitters) near.RemoveRange(CarEmitters, near.Count - CarEmitters);
                // keep an emitter on its car while that car is still wanted: no teleport streaks
                for (int i = 0; i < CarEmitters; i++) if (carOf[i] != null && !near.Contains(carOf[i])) carOf[i] = null;
                foreach (var c in near)
                {
                    if (System.Array.IndexOf(carOf, c) >= 0) continue;
                    int free = System.Array.IndexOf(carOf, null); if (free < 0) break;
                    carOf[free] = c; carPs[free].transform.position = c.pos; // fresh car: move first, emit from the next frame
                }
            }
            for (int i = 0; i < CarEmitters; i++)
            {
                var c = carOf[i];
                if (c == null || !traffic || (cam && (c.pos - cam.transform.position).sqrMagnitude > CarRange * CarRange * 1.3f)) { Rate(carPs[i], 0); continue; }
                Vector3 f = c.rot * Vector3.forward, up = c.rot * Vector3.up;
                carPs[i].transform.SetPositionAndRotation(c.pos - f * (c.len * .42f) + up * .15f, Quaternion.LookRotation(-f * .8f + up * .6f, up));
                var main = carPs[i].main; main.emitterVelocity = f * c.v;
                var sh = carPs[i].shape; sh.radius = Mathf.Max(.25f, c.hw * .8f);
                Rate(carPs[i], k * Mathf.Clamp01((c.v - 5f) / 18f) * 45f);
            }
        }
    }
}
