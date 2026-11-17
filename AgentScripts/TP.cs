using System.Linq;
using UnityEngine;
using Magistrala.Traffic;
public static class TP
{
    public static string Stats()
    {
        var t = Object.FindFirstObjectByType<TrafficSystem>();
        if (!t) return "no traffic";
        var truck = Object.FindFirstObjectByType<TruckController>();
        var sb = new System.Text.StringBuilder($"frame {Time.frameCount} t {Time.time:F1} fps {1f / Time.smoothDeltaTime:F0} cars {t.CarCount} upd {t.LastUpdateMs:F3} avg {t.AvgUpdateMs:F3} ms spawned {t.Spawned} despawned {t.Despawned} truck {(truck ? truck.transform.position.ToString() : "-")}\n");
        foreach (var c in t.Cars.OrderBy(c => truck ? (c.pos - truck.transform.position).magnitude : 0).Take(12))
            sb.Append($"  {t.data.kinds[c.kind].name} c{c.c} s {c.s:F0} lane {c.lane}->{c.laneTo} v {c.v * 3.6f:F0} km/h d {(truck ? (c.pos - truck.transform.position).magnitude : 0):F0} stop {c.stopT:F0}\n");
        return sb.ToString();
    }
}
public static class TP2
{
    public static string Drive(bool on)
    {
        var ap = Object.FindFirstObjectByType<Magistrala.Autopilot>(); ap.drive = on; if (!on) Object.FindFirstObjectByType<TruckController>().externalInput = false;
        return "drive " + on;
    }
    /// overlapping car pairs (same corridor, bodies shrunk 10 %) and cars touching the truck's colliders
    public static string Check()
    {
        var t = Object.FindFirstObjectByType<TrafficSystem>();
        var cars = t.Cars; int pairs = 0, truckHits = 0; var sb = new System.Text.StringBuilder();
        for (int i = 0; i < cars.Count; i++)
            for (int j = i + 1; j < cars.Count; j++)
            {
                var a = cars[i]; var b = cars[j]; if (a.c != b.c) continue;
                if (Mathf.Abs(a.s - b.s) < (a.len + b.len) / 2 * .9f && Mathf.Abs(a.lat - b.lat) < (a.hw + b.hw) * .9f) { pairs++; sb.Append($" pair s {a.s:F0}/{b.s:F0} lat {a.lat:F1}/{b.lat:F1};"); }
            }
        var truck = Object.FindFirstObjectByType<TruckController>();
        var cols = truck.GetComponentsInChildren<Collider>();
        foreach (var c in cars)
        {
            var bc = c.rb.GetComponent<BoxCollider>();
            foreach (var tc in cols)
                if (Physics.ComputePenetration(bc, c.rb.position, c.rb.rotation, tc, tc.transform.position, tc.transform.rotation, out var dir, out var dist) && dist > .05f) { truckHits++; sb.Append($" truck-hit {c.s:F0} {dist:F2};"); break; }
        }
        return $"t {Time.time:F0} cars {cars.Count} overlaps {pairs} truck contacts {truckHits} truck v {truck.SpeedKmh:F0} km/h upd {t.AvgUpdateMs:F3} ms" + sb;
    }
}
