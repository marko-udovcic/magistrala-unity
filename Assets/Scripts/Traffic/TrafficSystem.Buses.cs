// City bus line 37 (Trogir – Airport – Split) on the D8 (27. 11.): a few AI buses among the traffic cars, the same simulation
// (IDM following, signals, lane changes, the spawn ring / recycling), plus:
//   - spawned on their own budget (targetBuses, `-busevi N`, 0 = none) on the corridors with bus stops (TrafficData.busStops: the
//     D8 stops of stanice-mapillary.json, TrafficBuilder.BusStops), in the right lane, alternating the direction;
//   - each stop ahead: get into the right lane ≥ 25 m before it (or skip it), indicate right, pull in towards the kerb (BUS_PULL),
//     stop with the front bumper BUS_AHEAD past the stop's pole, wait 15–25 s, indicate left, pull out, on to the next one;
//     a stop it could not brake for comfortably is skipped. Cars behind it go round it (Decide: a standing leader, the next
//     lane clearly emptier) or wait;
//   - no passing on the left, never a side street turn (len > 6), never parked (ParkedBake.Parkable: name "bus");
//   - destination display (TrafficPart lamp 7): towards Split / towards Trogir by the corridor's name.
// No per-frame allocations: stops per corridor as sorted float arrays built once.
using UnityEngine;

namespace Magistrala.Traffic
{
    public partial class TrafficSystem
    {
        /// buses alive at once (spread over the spawn ring round the player); `-busevi N` overrides, 0 disables
        public int targetBuses = 3;
        const float BUS_AHEAD = 5f, BUS_PULL = .45f, BUS_SEE = 260;
        float[][] busS;     // per corridor: its stops' arc lengths, sorted (null: no stops)
        int busCount, busDir; float busNextTry;
        bool BusOn => busS != null && data.busKind >= 0 && data.busKind < data.kinds.Length && targetBuses > 0;
        public int BusCount => busCount;

        void InitBuses()
        {
            busS = null; busCount = 0;
            var a = System.Environment.GetCommandLineArgs();
            busDbg = System.Array.IndexOf(a, "-busdbg") >= 0;
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-busevi" && int.TryParse(a[i + 1], out int n)) targetBuses = Mathf.Max(0, n);
            if (targetBuses <= 0 || data.busKind < 0 || data.busStops == null || data.busStops.Length == 0) return;
            busS = new float[data.corridors.Length][];
            for (int c = 0; c < busS.Length; c++)
            {
                int n = 0; foreach (var st in data.busStops) if (st.corridor == c) n++;
                if (n == 0) continue;
                var s = new float[n]; int k = 0; foreach (var st in data.busStops) if (st.corridor == c) s[k++] = st.s;
                System.Array.Sort(s); busS[c] = s;
            }
            Debug.Log($"[traffic] buses: {targetBuses} ({data.kinds[data.busKind].name}), {data.busStops.Length} stops");
        }

        bool BusCorridor(int ci) => busS != null && ci < busS.Length && busS[ci] != null;

        /// spawn budget of the buses (after the cars' spawn, at most one try every 1.5 s)
        void SpawnBuses(bool warmStart)
        {
            if (!BusOn || busCount >= targetBuses || (!warmStart && clock < busNextTry)) return;
            busNextTry = clock + 1.5f;
            for (int k = 0; k < (warmStart ? 40 : 2) && busCount < targetBuses; k++) TrySpawn(warmStart, data.busKind);
        }

        /// a new bus: the first stop it can still brake for, the display by direction
        void BusBorn(Car car)
        {
            car.bus = true; busCount++; busDir++;
            car.displayAlt = data.corridors[car.c].name.Contains("Trogir");
            car.busStop = 0; var S = busS[car.c];
            while (car.busStop < S.Length && S[car.busStop] + BUS_AHEAD - (car.s + car.len / 2) < 40) car.busStop++;
            car.busPhase = 0; car.pull = 0;
        }

        /// in Step, before the IDM: the next stop as a standing obstacle (approach / dwell), pull-in, indicators
        void BusStep(Car car, TrafficCorridor cor, float dt, ref float gap, ref float lv)
        {
            car.busInd = 0;
            var S = busS != null && car.c < busS.Length ? busS[car.c] : null;
            float pullTo = 0;
            if (car.busLeaveT > 0) { car.busLeaveT -= dt; car.busInd = -1; }
            if (S != null && car.busStop < S.Length)
            {
                float d = S[car.busStop] + BUS_AHEAD - (car.s + car.len / 2);
                if (car.busPhase == 2)
                {
                    // dwelling: held at the stop
                    gap = 0; lv = 0; pullTo = BUS_PULL;
                    car.dwell -= dt;
                    if (car.dwell < 2.5f) car.busInd = -1; else car.busInd = 1;
                    if (car.dwell <= 0) { car.busPhase = 0; car.busStop++; car.busLeaveT = 3; }
                }
                else if (d < -1.5f) { car.busStop++; car.busPhase = 0; }
                else if (d < BUS_SEE)
                {
                    // could it still stop comfortably (2.2 m/s²)? else skip this stop
                    bool can = car.v * car.v / (2 * 2.2f) < d + 3;
                    if (car.busPhase == 0) car.busPhase = (byte)(can ? 1 : 0);
                    if (car.busPhase == 1)
                    {
                        if (car.lane != 0 && car.laneTo != 0 && d < 25) { car.busPhase = 0; car.busStop++; }
                        else if (car.lane == 0 && car.laneTo == 0)
                        {
                            // a standing obstacle 2 m past the stop point (IDM's minimum gap): it stops with its front on the point
                            float g = Mathf.Max(0, d + 2);
                            if (g < gap) { gap = g; lv = 0; }
                            if (d < 70) { car.busInd = 1; pullTo = BUS_PULL * Mathf.Clamp01(1 - (d - 8) / 50); }
                            if (d < 1.2f && car.v < .6f) { car.busPhase = 2; car.dwell = 15 + (float)rng.NextDouble() * 10; }
                        }
                    }
                    else if (!can && d < 60) { car.busStop++; }
                }
            }
            car.pull = Mathf.MoveTowards(car.pull, pullTo, .35f * dt);
        }

        // -busdbg: every 5 s each bus (corridor, phase, next stop and its distance, speed, game x / z) — tests find a bus at a stop
        bool busDbg; float busDbgT;
        void BusDebug()
        {
            if (!busDbg || busS == null || clock < busDbgT) return;
            busDbgT = clock + 5;
            var sb = new System.Text.StringBuilder($"[bus] t={clock:F0}s buses {busCount}:");
            foreach (var c in cars)
            {
                if (!c.bus) continue;
                var S = busS[c.c]; float d = c.busStop < S.Length ? S[c.busStop] + BUS_AHEAD - (c.s + c.len / 2) : -1;
                sb.Append($" [c{c.c} ph{c.busPhase} stop{c.busStop} d {d:F0} v {c.v * 3.6f:F0} dwell {c.dwell:F0} lane {c.lane} pull {c.pull:F2} x={c.pos.x - 4200:F0} z={-c.pos.z - 5300:F0}]");
            }
            Debug.Log(sb.ToString());
        }

        void BusGone(Car c) { if (c.bus) { c.bus = false; busCount = Mathf.Max(0, busCount - 1); } }
    }
}
