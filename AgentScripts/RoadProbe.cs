using System.Text;
using UnityEngine;
using Magistrala.EditorTools;
public static class RoadProbe
{
    // for each model road with n>=2 print perpendicular asphalt extent (right of direction) at the road's middle segment
    public static string Run(string key)
    {
        var sb = new StringBuilder();
        foreach (var m in WorldData.Models())
        {
            if (m.key != key) continue;
            int ri = 0;
            foreach (var r in m.roads)
            {
                int i = r.p.Count / 2 - 1; if (i < 0) i = 0;
                var a = r.p[i]; var b = r.p[i + 1]; double dx = b[0] - a[0], dz = b[1] - a[1], L = System.Math.Sqrt(dx * dx + dz * dz);
                double rx = -dz / L, rz = dx / L; double mx = (a[0] + b[0]) / 2, mz = (a[1] + b[1]) / 2;
                string s = ""; double lo = 99, hi = -99;
                for (double o = -9; o <= 9; o += .5)
                {
                    double y = m.Surface(mx + rx * o, mz + rz * o);
                    if (!double.IsNaN(y)) { s += "#"; lo = System.Math.Min(lo, o); hi = System.Math.Max(hi, o); } else s += ".";
                }
                sb.AppendLine($"{key} road{ri} w={r.w} n={r.n} dir=({dx / L:F2},{dz / L:F2}) at ({mx:F0},{mz:F0}) asphalt[{lo}..{hi}] right>0: {s}");
                ri++;
            }
        }
        return sb.ToString();
    }
}
