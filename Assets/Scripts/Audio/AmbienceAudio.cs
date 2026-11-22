// Ambience beds and positioned events, mixed from the world data and the game clock (assets-src/zvuk/PLAN.md §4).
//
// Evaluated 4× a second at the listener (main camera), then faded smoothly:
//   sea    1 − smoothstep(25, 260 m, distance to the nearest sea cell) — GameWorld.seaBits (8 m cells), ring search every 2 cells
//          up to 300 m (≤ ~1100 bit lookups, 4×/s, nothing per frame)
//   town   1 inside an OSM settlement boundary (GameWorld.PlaceAt ≠ ""), eased over ~4 s
//   hour   MagistralaGame.Hour; night = MagistralaGame.IsNight; summer = Summer (the game has no calendar yet: true)
//   rain   WeatherSystem.Wetness; cabin = TruckChaseCamera.CabinView; speed = the driven vehicle's km/h (rushing wind)
// Beds (2D loops, faded, silent ones stopped): sea_riva · cicadas · town_distant · birds_morning · wind · night_crickets ·
// rain (outside) · rain_roof (cabin only, Motor group so the cabin low-pass does not muffle it).
// Events (pooled one-shots): seagulls near the sea by day, dogs in town (a short burst), church bells: the noon peal at 12:00
// and single strokes on the full hours 7–21 h, 2D (as if from the nearest town centre: louder in town).
using UnityEngine;

namespace Magistrala.Audio
{
    public sealed class AmbienceAudio : MonoBehaviour
    {
        public static bool Summer = true;

        enum Bed { Sea, Cicadas, Town, Birds, Wind, Crickets, Rain, RainRoof, Count }
        static readonly string[] BedClip = { "okolina/sea_riva", "okolina/cicadas", "okolina/town_distant", "okolina/birds_morning",
                                             "okolina/wind", "okolina/night_crickets", "okolina/rain", "okolina/rain_roof" };
        static readonly float[] BedMax = { 0.8f, 0.5f, 0.45f, 0.55f, 0.5f, 0.5f, 0.8f, 0.9f };

        MagistralaAudio A; Game.GameWorld world;
        readonly AudioSource[] beds = new AudioSource[(int)Bed.Count];
        readonly float[] want = new float[(int)Bed.Count];
        AudioClip[] gulls, dogs; AudioClip bellsNoon, bellToll;
        float sea, town, townTarget, next, nextGull = 6, nextDog = 20, lastHour = -1, barkT, barkPitch, windPitch = 1;
        int barksLeft; Vector3 barkAt, seaDir = Vector3.forward;
        public float Sea => sea;
        public float Town => town;

        void Start()
        {
            A = MagistralaAudio.I;
            world = Resources.Load<Game.GameWorld>(Game.GameWorld.ResourcePath);
            for (int i = 0; i < beds.Length; i++)
            {
                var bus = i == (int)Bed.RainRoof ? Bus.Motor : Bus.Okolina;
                beds[i] = A.NewSource("bed_" + (Bed)i, transform, bus, false, true, 64);
                beds[i].clip = A.Clip(BedClip[i]);
            }
            gulls = new AudioClip[8];
            for (int i = 0; i < 8; i++) gulls[i] = A.Clip("okolina/seagull_" + (i + 1));
            dogs = new AudioClip[4];
            for (int i = 0; i < 4; i++) dogs[i] = A.Clip("okolina/dog_bark_" + (i + 1));
            bellsNoon = A.Clip("okolina/bells_noon"); bellToll = A.Clip("okolina/bell_toll");
        }

        void Update()
        {
            var cam = Camera.main;
            if (!cam || A == null) return;
            float dt = Time.deltaTime;
            Vector3 p = cam.transform.position;
            if (Time.time >= next) { next = Time.time + 0.25f; Evaluate(p, cam); }
            town = Mathf.MoveTowards(town, townTarget, dt / 4f);
            float gO = A.Gain(Bus.Okolina), gM = A.Gain(Bus.Motor);
            for (int i = 0; i < beds.Length; i++)
            {
                var s = beds[i];
                float target = want[i] * BedMax[i] * (i == (int)Bed.RainRoof ? gM : gO);
                s.volume = Mathf.MoveTowards(s.volume, target, dt * 0.5f);
                if (s.volume > 0.002f) { if (!s.isPlaying && s.clip) { s.timeSamples = Random.Range(0, s.clip.samples); s.Play(); } }
                else if (s.isPlaying) s.Stop();
            }
            beds[(int)Bed.Wind].pitch = windPitch;
            Events(p, dt);
        }

        void Evaluate(Vector3 p, Camera cam)
        {
            var g = Game.MagistralaGame.I;
            float hour = g != null ? g.Hour : 12f;
            bool night = g != null && Game.MagistralaGame.IsNight(g.ClockMin);
            float wet = WeatherSystem.Wetness, dry = 1f - Mathf.Clamp01(wet * 1.5f);
            var chase = cam.GetComponent<TruckChaseCamera>();
            bool cabin = chase && chase.CabinView;
            var truck = TruckAudio.I ? TruckAudio.I.Target : null;
            float speedN = truck ? Mathf.Clamp01(truck.SpeedKmh / 90f) : 0f;
            float d = SeaDistance(p);
            sea = 1f - Smooth(25f, 260f, d);
            if (sea > 0.05f) seaDir = SeaDirection(p);
            townTarget = world != null && !string.IsNullOrEmpty(world.PlaceAt(p)) ? 1f : 0f;
            want[(int)Bed.Sea] = sea;
            want[(int)Bed.Cicadas] = Summer ? dry * (1f - 0.6f * town) * Bell(hour, 8.5f, 14f, 19.5f) : 0f;
            want[(int)Bed.Town] = town * (night ? 0.35f : 1f) * (0.5f + 0.5f * dry);
            want[(int)Bed.Birds] = Bell(hour, 4.8f, 6.5f, 9.5f) * dry * (0.6f + 0.4f * (1f - town));
            // ambient wind (more at the sea) + the rushing air of driving (quieter in the cabin)
            want[(int)Bed.Wind] = Mathf.Clamp01(0.3f + 0.3f * sea + speedN * (cabin ? 0.35f : 0.8f));
            windPitch = 0.9f + 0.35f * speedN;
            want[(int)Bed.Crickets] = night ? dry * (1f - 0.5f * town) : 0f;
            want[(int)Bed.Rain] = wet * (cabin ? 0.35f : 1f);
            want[(int)Bed.RainRoof] = cabin ? wet : 0f;
            if (lastHour >= 0 && Mathf.Floor(hour) != Mathf.Floor(lastHour) && hour > lastHour)
            {
                int h = Mathf.FloorToInt(hour);
                float v = 0.35f + 0.45f * town;
                if (h == 12) A.Play2D(bellsNoon, Bus.Okolina, v);
                else if (h >= 7 && h <= 21) A.Play2D(bellToll, Bus.Okolina, v * 0.8f);
                if (h >= 7 && h <= 21) Debug.Log($"[zvuk] zvona {h}:00 (grad {town:0.0})");
            }
            lastHour = hour;
        }

        void Events(Vector3 p, float dt)
        {
            var g = Game.MagistralaGame.I;
            float hour = g != null ? g.Hour : 12f;
            bool night = g != null && Game.MagistralaGame.IsNight(g.ClockMin);
            if (Time.time > nextGull)
            {
                nextGull = Time.time + Random.Range(7f, 25f);
                if (sea > 0.3f && !night && WeatherSystem.Wetness < 0.3f)
                {
                    Vector3 at = p + seaDir * Random.Range(25f, 70f) + Vector3.up * Random.Range(8f, 20f) + Random.insideUnitSphere * 15f;
                    A.PlayAt(gulls[Random.Range(0, gulls.Length)], at, Bus.Okolina, Random.Range(0.5f, 0.9f), Random.Range(0.93f, 1.07f), 15f, 250f);
                }
            }
            if (Time.time > nextDog)
            {
                bool lively = night || (hour >= 5 && hour < 8);
                nextDog = Time.time + Random.Range(25f, 80f) * (lively ? 0.6f : 1f);
                if (town > 0.5f)
                {
                    barksLeft = Random.Range(2, 5); barkT = 0; barkPitch = Random.Range(0.85f, 1.15f);
                    barkAt = p + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * Random.Range(40f, 140f);
                }
            }
            if (barksLeft > 0)
            {
                barkT -= dt;
                if (barkT <= 0)
                {
                    A.PlayAt(dogs[Random.Range(0, dogs.Length)], barkAt, Bus.Okolina, 0.8f, barkPitch * Random.Range(0.97f, 1.03f), 10f, 300f);
                    barksLeft--; barkT = Random.Range(0.35f, 0.9f);
                }
            }
        }

        // ------------------------------------------------------------------ world queries (4× a second)
        float SeaDistance(Vector3 p)
        {
            if (world == null || world.seaBits == null || world.seaBits.Length == 0) return 1000f;
            float cell = world.seaCell;
            int ci = Mathf.FloorToInt((p.x - world.seaOrigin.x) / cell), cj = Mathf.FloorToInt((p.z - world.seaOrigin.y) / cell);
            if (world.SeaAt(ci, cj)) return 0f;
            int rmax = Mathf.CeilToInt(300f / cell);
            for (int r = 2; r <= rmax; r += 2)
                for (int k = -r; k <= r; k += 2)
                    if (world.SeaAt(ci + k, cj - r) || world.SeaAt(ci + k, cj + r) || world.SeaAt(ci - r, cj + k) || world.SeaAt(ci + r, cj + k))
                        return r * cell;
            return 1000f;
        }

        Vector3 SeaDirection(Vector3 p)
        {
            float cell = world.seaCell; Vector3 sum = Vector3.zero;
            int ci = Mathf.FloorToInt((p.x - world.seaOrigin.x) / cell), cj = Mathf.FloorToInt((p.z - world.seaOrigin.y) / cell);
            for (int a = 0; a < 16; a++)
            {
                Vector3 d = Quaternion.Euler(0, a * 22.5f, 0) * Vector3.forward;
                for (int s = 2; s < 40; s += 4)
                    if (world.SeaAt(ci + Mathf.RoundToInt(d.x * s), cj + Mathf.RoundToInt(d.z * s))) { sum += d / s; break; }
            }
            return sum.sqrMagnitude > 0 ? sum.normalized : Vector3.forward;
        }

        static float Smooth(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }
        static float Bell(float x, float a, float peak, float b) => x <= a || x >= b ? 0 : x < peak ? Smooth(a, peak, x) : 1 - Smooth(peak, b, x);
    }
}
