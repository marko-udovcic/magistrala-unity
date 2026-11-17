using System.Text;
using UnityEngine;
using Magistrala.EditorTools;
public static class RoadProbe2
{
    // asphalt extent across road (key, idx) at every vertex
    public static string Run(string key, int idx)
    {
        var sb = new StringBuilder(); var m = WorldData.Models().Find(q => q.key == key); var r = m.roads[idx];
        sb.AppendLine($"w={r.w} n={r.n} lvl={r.lvl}");
        for (int i = 0; i < r.p.Count - 1; i++)
        {
            var a = r.p[i]; var b = r.p[i + 1]; double dx = b[0] - a[0], dz = b[1] - a[1], L = System.Math.Sqrt(dx * dx + dz * dz); if (L < .05) continue;
            double rx = -dz / L, rz = dx / L; string s = "";
            for (double o = -8; o <= 8; o += .5) { double y = m.Surface(a[0] + rx * o, a[1] + rz * o); s += double.IsNaN(y) ? "." : "#"; }
            sb.AppendLine($"v{i} ({a[0]:F0},{a[1]:F0}) {s}");
        }
        return sb.ToString();
    }
}
