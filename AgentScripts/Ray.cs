using System.Text; using UnityEngine; using Magistrala.EditorTools;
public static class Ray
{
    // raycasts down every `step` m from game (x0,z0) to (x1,z1): what is hit (object name, y)
    public static string Line(float x0, float z0, float x1, float z1, float step)
    {
        var sb = new StringBuilder(); float L = Mathf.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
        for (float s = 0; s <= L + 1e-3; s += step)
        {
            float t = s / L; var u = W.U(x0 + (x1 - x0) * t, 0, z0 + (z1 - z0) * t);
            var hits = Physics.RaycastAll(new Vector3(u.x, 400, u.z), Vector3.down, 800); System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            sb.Append($"s {s:F1}:"); foreach (var h in hits) sb.Append($" {h.collider.name}@{h.point.y:F2}"); sb.AppendLine();
        }
        return sb.ToString();
    }
}
