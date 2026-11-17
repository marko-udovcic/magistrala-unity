using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using Magistrala.EditorTools;
public static class Drive
{
    public static string Start(float gameX, float cruise)
    {
        var ap = Object.FindFirstObjectByType<Magistrala.Autopilot>();
        ap.cruiseKmh = cruise; ap.Teleport(gameX); ap.drive = true;
        return $"started at {ap.transform.position}";
    }
    // lateral offset (right of travel, m) of the truck from the axis of model road (key, idx) + lane centre expected
    public static async Task<string> Measure(string key, int idx, int samples, int ms)
    {
        var m = WorldData.Models().Find(q => q.key == key); var r = m.roads[idx];
        var truck = Object.FindFirstObjectByType<TruckController>().transform; var sb = new StringBuilder();
        double expect = r.w / 2 * (1 - 1.0 / r.n); sb.AppendLine($"{key} road{idx} w={r.w} n={r.n} lane centre (rightmost) = {expect:F2}; axis start {r.p[0][0]:F0},{r.p[0][1]:F0} end {r.p[r.p.Count-1][0]:F0},{r.p[r.p.Count-1][1]:F0}");
        double sum = 0, sumAbs = 0; int cnt = 0;
        for (int k = 0; k < samples; k++)
        {
            await Task.Delay(ms);
            var g = W.G(truck.position); double bd = 1e9, lat = 0; int bi = -1;
            for (int i = 0; i + 1 < r.p.Count; i++)
            {
                var a = r.p[i]; var b = r.p[i + 1]; double dx = b[0] - a[0], dz = b[1] - a[1], L2 = dx * dx + dz * dz; if (L2 < 1e-6) continue;
                double t = System.Math.Max(0, System.Math.Min(1, ((g.x - a[0]) * dx + (g.y - a[1]) * dz) / L2));
                double px = a[0] + dx * t - g.x, pz = a[1] + dz * t - g.y, d = System.Math.Sqrt(px * px + pz * pz);
                if (d < bd) { bd = d; bi = i; double L = System.Math.Sqrt(L2); lat = ((g.x - a[0]) * (-dz / L) + (g.y - a[1]) * (dx / L)); }
            }
            var rb = truck.GetComponent<Rigidbody>();
            sb.AppendLine($"gx={g.x:F0} spd={rb.linearVelocity.magnitude * 3.6f:F0}km/h lat={lat:F2} (err {lat - expect:+0.00;-0.00}) nearest d={bd:F1}");
            if (bd < r.w / 2 && System.Math.Abs(lat) < r.w) { sum += lat - expect; sumAbs += System.Math.Abs(lat - expect); cnt++; }
        }
        if (cnt > 0) sb.AppendLine($"on-road samples {cnt}: mean err {sum / cnt:+0.00;-0.00}, mean |err| {sumAbs / cnt:F2}");
        return sb.ToString();
    }
    public static string Top(float h, float fwd)
    {
        var cam = Camera.main; var cc = cam.GetComponent<TruckChaseCamera>(); if (cc) cc.enabled = false;
        var t = Object.FindFirstObjectByType<TruckController>().transform;
        cam.transform.position = t.position + t.forward * fwd + Vector3.up * h; cam.transform.rotation = Quaternion.LookRotation(Vector3.down, t.forward);
        return "ok";
    }
    public static string Chase() { var cc = Camera.main.GetComponent<TruckChaseCamera>(); if (cc) cc.enabled = true; return "chase"; }
}
public static class Place
{
    // park the truck on the autopilot path at game x, shifted 'left' metres to the left of travel (0.9 reproduces the old lane offset); top camera above it
    public static string Park(float gameX, float left, float camH)
    {
        var ap = Object.FindFirstObjectByType<Magistrala.Autopilot>(); ap.drive = false; ap.Teleport(gameX);
        var t = ap.transform; var rb = t.GetComponent<Rigidbody>(); var truck = t.GetComponent<TruckController>(); truck.externalInput = false;
        rb.position = t.position - t.right * left; t.position = rb.position;
        var cam = Camera.main; var cc = cam.GetComponent<TruckChaseCamera>(); if (cc) cc.enabled = false;
        cam.fieldOfView = 40; cam.transform.position = t.position + Vector3.up * camH; cam.transform.rotation = Quaternion.LookRotation(Vector3.down, t.forward);
        return $"parked at {t.position}";
    }
}
