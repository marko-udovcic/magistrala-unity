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
        return $"t {Time.time:F0} fails {string.Join(",", t.SpawnFails)} cars {cars.Count} overlaps {pairs} truck contacts {truckHits} truck v {truck.SpeedKmh:F0} km/h upd {t.AvgUpdateMs:F3} ms (sim {t.AvgSimMs:F3}, draw {t.AvgDrawMs:F3}) fps {1f / Time.smoothDeltaTime:F0}" + sb;
    }
}
public static class TP3
{
    /// truck to game x (stopped, autopilot off), traffic restarted around it
    public static string Teleport(float gx)
    {
        var ap = Object.FindFirstObjectByType<Magistrala.Autopilot>(); ap.drive = false;
        var tc = Object.FindFirstObjectByType<TruckController>(); tc.externalInput = true; tc.extThrottle = 0; tc.extBrake = 1; tc.extHandbrake = true;
        ap.Teleport(gx);
        Object.FindFirstObjectByType<TrafficSystem>().Clear();
        return tc.transform.position.ToString();
    }
    /// free camera at game (x, y, z) looking at game (tx, ty, tz)
    public static string Look(float gx, float gy, float gz, float tx, float ty, float tz)
    {
        var cc = Object.FindFirstObjectByType<TruckChaseCamera>(); if (cc) cc.enabled = false;
        var cam = Camera.main; cam.transform.position = new Vector3(gx + 4200, gy, -gz - 5300); cam.transform.LookAt(new Vector3(tx + 4200, ty, -tz - 5300));
        return "ok";
    }
    public static string Lights()
    {
        var t = Object.FindFirstObjectByType<TrafficSystem>(); var sb = new System.Text.StringBuilder();
        for (int c = 0; c < t.data.corridors.Length; c++) for (int i = 0; i < t.data.corridors[c].stops.Length; i++) sb.Append($"{t.data.corridors[c].stops[i].model}/{c}: {t.LightName(c, i)}  ");
        return sb.ToString();
    }
}
public static class TP4
{
    public static string Truck()
    {
        var t = Object.FindFirstObjectByType<TrafficSystem>();
        var f = typeof(TrafficSystem).GetField("pS0", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        float[] G(string n) => (float[])typeof(TrafficSystem).GetField(n, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(t);
        var on = (bool[])typeof(TrafficSystem).GetField("pOn", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(t);
        var c = t.data.corridors[0]; float s = G("pS0")[0]; int i = Mathf.RoundToInt(s / c.step);
        var sb = new System.Text.StringBuilder($"truck c0 on {on[0]} s {G("pS0")[0]:F1}..{G("pS1")[0]:F1} lat {G("pL0")[0]:F2}..{G("pL1")[0]:F2}; lanes {c.lanes[i]} offs ");
        for (int k = 0; k < c.lanes[i]; k++) sb.Append($"{c.laneOff[i * c.maxLanes + k]:F2} ");
        foreach (var car in t.Cars) if (car.c == 0 && Mathf.Abs(car.s - s) < 60) sb.Append($"\n {t.data.kinds[car.kind].name} s {car.s:F1} lat {car.lat:F2} lane {car.lane}->{car.laneTo} v {car.v:F1} stop {car.stopT:F0}");
        return sb.ToString();
    }
}

public static class TP5
{
    public static string Toggle(bool on) { var t = Object.FindFirstObjectByType<TrafficSystem>(); t.enabled = on; return "traffic " + on; }
    public static string Fps() => $"fps {1f / Time.smoothDeltaTime:F1} frame {Time.frameCount}";
}
