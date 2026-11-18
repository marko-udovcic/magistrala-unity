using System.Text;
using UnityEngine;
using Magistrala.EditorTools;
public static class RailDbg
{
    // cross-sections (across dir dx,dz) at points along from (x,z): A asphalt, I island, S sidewalk, M markings, G ground(any), . nothing; per 0.25 m over ±w
    public static string Run(string key, float x, float z, float dx, float dz, float w, int steps, float step)
    {
        var models = WorldData.Models(); var m = models.Find(q => q.key == key); var sb = new StringBuilder();
        float l = Mathf.Sqrt(dx * dx + dz * dz); dx /= l; dz /= l; float nx = -dz, nz = dx;
        for (int k = 0; k < steps; k++)
        {
            float px = x + dx * k * step, pz = z + dz * k * step; var line = new StringBuilder();
            for (float u = -w; u <= w + 1e-3; u += .25f)
            {
                float qx = px + nx * u, qz = pz + nz * u; char c = '.';
                if (LaneProfile.Hit(m, "markings", qx, qz)) c = 'M'; else if (LaneProfile.Hit(m, "island", qx, qz)) c = 'I'; else if (LaneProfile.Hit(m, "sidewalk", qx, qz)) c = 'S'; else if (LaneProfile.Hit(m, "asphalt", qx, qz)) c = 'A';
                else if (LaneProfile.OnAsphalt(models, qx, qz)) c = 'a';
                if (Mathf.Abs(u) < .13f) c = c == '.' ? '|' : char.ToLower(c) == c ? c : c;
                line.Append(c);
            }
            sb.AppendLine($"{px:F0},{pz:F0} {line}");
        }
        return sb.ToString();
    }
}
