// -trafview (28. 11., promet2): what the eye sees of the traffic on the road being driven, for the "oncoming cars approach
// gradually, like ETS" goal. Twice a second: the cars in the frustum and not hidden (HiddenFrom, the -spawnlog test), split
// into same direction / oncoming by heading, and by distance from the eye (< 300, 300–600, 600–draw distance, beyond it:
// existing but not drawn); and the density on the player's road (the corridors within roadR m: the D8's two carriageways)
// against the rest, in cars per km of corridor within the ring. Logged every 10 s as averages ([trafview]) and as a running
// total since the start ([trafview-all]).
using UnityEngine;

namespace Magistrala.Traffic
{
    public partial class TrafficSystem
    {
        bool viewStats; float vsT, vsLogT; int vsN, vsAllN;
        // [0] same direction, [1] oncoming; bins < 300, 300–600, 600–farFadeEnd, beyond (not drawn)
        readonly float[,] vsBin = new float[2, 4], vsAll = new float[2, 4];
        float vsRoadKm, vsElseKm, vsAllRoadKm, vsAllElseKm, vsRoadCars, vsAllRoadCars;
        const float roadR = 40;

        void ViewStats()
        {
            if (!cam || clock - vsT < .5f) return;
            vsT = clock;
            var eye = cam.transform.position; var fw = cam.transform.forward; fw.y = 0;
            foreach (var c in cars)
            {
                if (c.sideRoute >= 0) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, new Bounds(c.pos + Vector3.up * c.h * .5f, new Vector3(c.len, c.h, c.len)))) continue;
                float d = (c.pos - eye).magnitude;
                if (d < farFadeEnd && HiddenFrom(eye, c.pos, c.hw, c.h, true)) continue;
                int dir = Vector3.Dot(c.rot * Vector3.forward, fw) >= 0 ? 0 : 1;
                int bin = d < 300 ? 0 : d < 600 ? 1 : d < farFadeEnd ? 2 : 3;
                vsBin[dir, bin]++; vsAll[dir, bin]++;
            }
            // density: cars per km within ±spawnMax of the player along each corridor in reach
            float roadCars = 0, roadKm = 0, elseCars = 0, elseKm = 0;
            for (int ci = 0; ci < data.corridors.Length; ci++)
            {
                if (pFar[ci] > corridorReach) continue;
                var cor = data.corridors[ci];
                float a = Mathf.Max(0, pSc[ci] - spawnMax), b = Mathf.Min(cor.Length, pSc[ci] + spawnMax);
                if (b <= a) continue;
                int n = 0; foreach (var c in byCorridor[ci]) if (c.s >= a && c.s <= b) n++;
                if (pFar[ci] < roadR) { roadCars += n; roadKm += (b - a) / 1000; } else { elseCars += n; elseKm += (b - a) / 1000; }
            }
            float rd = roadKm > 0 ? roadCars / roadKm : 0, ed = elseKm > 0 ? elseCars / elseKm : 0;
            vsRoadKm += rd; vsElseKm += ed; vsRoadCars += roadCars; vsAllRoadKm += rd; vsAllElseKm += ed; vsAllRoadCars += roadCars;
            vsN++; vsAllN++;
            if (clock - vsLogT < 10) return;
            vsLogT = clock;
            Debug.Log($"[trafview] t={clock:F0} ahead {B(vsBin, 0, vsN)} oncoming {B(vsBin, 1, vsN)} | road {vsRoadKm / vsN:F1}/km ({vsRoadCars / vsN:F0} cars) else {vsElseKm / vsN:F1}/km | cars {cars.Count} target {targetCars}");
            Debug.Log($"[trafview-all] t={clock:F0} ahead {B(vsAll, 0, vsAllN)} oncoming {B(vsAll, 1, vsAllN)} | road {vsAllRoadKm / vsAllN:F1}/km ({vsAllRoadCars / vsAllN:F1} cars) else {vsAllElseKm / vsAllN:F1}/km");
            System.Array.Clear(vsBin, 0, vsBin.Length); vsN = 0; vsRoadKm = vsElseKm = vsRoadCars = 0;
        }
        static string B(float[,] a, int dir, int n)
        {
            n = Mathf.Max(1, n);
            return $"{a[dir, 0] / n:F1}/{a[dir, 1] / n:F1}/{a[dir, 2] / n:F1} far {a[dir, 3] / n:F1}";
        }
    }
}
