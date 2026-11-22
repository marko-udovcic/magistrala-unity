// Traffic voices: only the nearest cars sound, through a fixed pool (assets-src/zvuk/PLAN.md §5).
//
//   * TrafficSystem's cars have no AudioSource (and never get one). 6 pooled car voices (promet/car_loop) + 2 siren voices.
//   * 5× a second: the nearest cars within 120 m (4 on quality "brzo", else 6). A car keeps its voice while it stays in the set
//     (no restart); a released voice fades out over 0.3 s before it is reused; despawned cars lose theirs.
//   * every frame a voice follows its car; Unity derives the doppler shift from the source's movement (dopplerLevel 0.7).
//     Pitch from speed (0.75 standing → 1.3 at 90 km/h) × a per-car factor (0.85–1.2), volume 0.25 idling → 1 at speed.
//   * sirens: the 2 nearest cars with beacon on (TrafficSystem flashes them) within 500 m; siren_ambulance when the kind's name
//     says "hitna" / "ambul" / "sanit", else siren_police — both real European two-tones.
using System.Collections.Generic;
using UnityEngine;
using Magistrala.Traffic;

namespace Magistrala.Audio
{
    public sealed class TrafficAudio : MonoBehaviour
    {
        public float Range = 120f, SirenRange = 500f;
        const int MaxVoices = 6, SirenVoices = 2;

        sealed class Voice { public AudioSource src; public TrafficSystem.Car car; public float pitchMul; }
        readonly Voice[] voices = new Voice[MaxVoices];
        readonly Voice[] sirens = new Voice[SirenVoices];
        readonly List<TrafficSystem.Car> pick = new List<TrafficSystem.Car>(64);
        readonly HashSet<TrafficSystem.Car> alive = new HashSet<TrafficSystem.Car>();
        TrafficSystem ts; MagistralaAudio A; float next, findT; Vector3 L;
        AudioClip carLoop, sirenPolice, sirenAmb;
        System.Comparison<TrafficSystem.Car> byDist;

        void Start()
        {
            A = MagistralaAudio.I;
            carLoop = A.Clip("promet/car_loop"); sirenPolice = A.Clip("promet/siren_police"); sirenAmb = A.Clip("promet/siren_ambulance");
            byDist = (a, b) => (a.pos - L).sqrMagnitude.CompareTo((b.pos - L).sqrMagnitude);
            for (int i = 0; i < MaxVoices; i++)
            {
                var s = A.NewSource("car_" + i, transform, Bus.Promet, true, true, 160, 6f, Range * 1.3f);
                s.clip = carLoop; s.dopplerLevel = 0.7f;
                voices[i] = new Voice { src = s };
            }
            for (int i = 0; i < SirenVoices; i++)
            {
                var s = A.NewSource("siren_" + i, transform, Bus.Promet, true, true, 60, 20f, SirenRange * 1.5f);
                s.dopplerLevel = 0.7f;
                sirens[i] = new Voice { src = s };
            }
        }

        void Update()
        {
            if (A == null) return;
            if (!ts) { findT -= Time.unscaledDeltaTime; if (findT > 0) return; findT = 2f; ts = FindFirstObjectByType<TrafficSystem>(); if (!ts) return; }
            var cam = Camera.main; if (!cam) return;
            float dt = Time.deltaTime;
            if (Time.time >= next) { next = Time.time + 0.2f; L = cam.transform.position; Assign(); }
            float g = A.Gain(Bus.Promet);
            for (int i = 0; i < voices.Length; i++) Follow(voices[i], dt, false, g);
            for (int i = 0; i < sirens.Length; i++) Follow(sirens[i], dt, true, g);
        }

        void Assign()
        {
            var cars = ts.Cars;
            alive.Clear();
            for (int i = 0; i < cars.Count; i++) alive.Add(cars[i]);
            int n = RenderQuality.Current <= 0 ? 4 : MaxVoices;
            Nearest(cars, Range, n, false);
            Bind(voices, n, false);
            Nearest(cars, SirenRange, SirenVoices, true);
            Bind(sirens, SirenVoices, true);
        }

        void Nearest(IReadOnlyList<TrafficSystem.Car> cars, float range, int n, bool beaconOnly)
        {
            pick.Clear();
            float r2 = range * range;
            for (int i = 0; i < cars.Count; i++)
            {
                var c = cars[i];
                if (beaconOnly && !c.beacon) continue;
                if ((c.pos - L).sqrMagnitude > r2) continue;
                pick.Add(c);
            }
            pick.Sort(byDist);
            if (pick.Count > n) pick.RemoveRange(n, pick.Count - n);
        }

        void Bind(Voice[] pool, int n, bool siren)
        {
            for (int i = 0; i < pool.Length; i++)
            {
                var v = pool[i];
                if (v.car != null && (i >= n || !pick.Contains(v.car) || !alive.Contains(v.car))) v.car = null;
                if (v.car != null) pick.Remove(v.car);
            }
            for (int p = 0; p < pick.Count; p++)
            {
                var c = pick[p];
                for (int i = 0; i < n; i++)
                {
                    var v = pool[i];
                    if (v.car != null || v.src.volume > 0.01f) continue;
                    v.car = c;
                    v.pitchMul = 0.85f + 0.35f * Hash01(c);
                    if (siren)
                    {
                        bool amb = false;
                        if (ts.data != null && c.kind >= 0 && c.kind < ts.data.kinds.Length)
                        {
                            string k = ts.data.kinds[c.kind].name;
                            amb = k != null && (k.IndexOf("hitna", System.StringComparison.OrdinalIgnoreCase) >= 0 || k.IndexOf("ambul", System.StringComparison.OrdinalIgnoreCase) >= 0
                                                || k.IndexOf("sanit", System.StringComparison.OrdinalIgnoreCase) >= 0);
                        }
                        v.src.clip = amb ? sirenAmb : sirenPolice;
                        v.pitchMul = 1f;
                    }
                    v.src.transform.position = c.pos + Vector3.up * 0.6f;
                    if (v.src.clip) { v.src.timeSamples = Random.Range(0, v.src.clip.samples); v.src.Play(); }
                    break;
                }
            }
        }

        static void Follow(Voice v, float dt, bool siren, float gain)
        {
            float want = 0;
            if (v.car != null)
            {
                v.src.transform.position = v.car.pos + Vector3.up * 0.6f;
                float kmh = v.car.v * 3.6f;
                if (siren) { want = 0.9f; v.src.pitch = 1f; }
                else
                {
                    want = Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(kmh / 70f));
                    v.src.pitch = Mathf.Lerp(0.75f, 1.3f, Mathf.Clamp01(kmh / 90f)) * v.pitchMul;
                }
            }
            v.src.volume = Mathf.MoveTowards(v.src.volume, want * gain, dt / 0.3f);
            if (v.car == null && v.src.volume <= 0.001f && v.src.isPlaying) v.src.Stop();
        }

        static float Hash01(TrafficSystem.Car c) => unchecked((uint)System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(c) * 2654435761u) % 1000u / 1000f;

        public int Bound { get { int n = 0; foreach (var v in voices) if (v.car != null) n++; return n; } }
    }
}
