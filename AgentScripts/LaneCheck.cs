using System.Text;
using UnityEngine;
using Magistrala.EditorTools;
public static class LaneCheck
{
    // every 2 m of the autopilot path and of each traffic lane: is the body (±hw) on asphalt? runs of off-asphalt (game x)
    public static string Run(float hw, float x0, float x1)
    {
        var models = WorldData.Models(); var sb = new StringBuilder();
        var ap = Object.FindFirstObjectByType<Magistrala.Autopilot>();
        Check(sb, "autopilot", ap.path, null, hw, x0, x1, models);
        var ts = Object.FindFirstObjectByType<Magistrala.Traffic.TrafficSystem>();
        foreach (var c in ts.data.corridors)
            for (int k = 0; k < c.maxLanes; k++)
            {
                var pts = new Vector3[c.axis.Length]; var ok = new bool[c.axis.Length];
                for (int i = 0; i < c.axis.Length; i++) { ok[i] = k < c.lanes[i]; pts[i] = c.axis[i] + c.right[i] * (ok[i] ? c.laneOff[i * c.maxLanes + k] : 0); }
                Check(sb, c.name + " lane " + k, pts, ok, hw, x0, x1, models);
            }
        return sb.ToString();
    }
    static void Check(StringBuilder sb, string name, Vector3[] p, bool[] ok, float hw, float x0, float x1, System.Collections.Generic.List<ModelJ> models)
    {
        int bad = 0; float runStart = float.NaN, worst = 0; var runs = new StringBuilder();
        for (int i = 0; i + 1 < p.Length; i++)
        {
            var g = W.G(p[i]); bool on = true; float off = 0;
            if (g.x >= x0 && g.x <= x1 && (ok == null || ok[i]))
            {
                var t = p[i + 1] - p[i]; t.y = 0; t.Normalize(); var r = new Vector3(t.z, 0, -t.x);
                foreach (float side in new[] { -1f, 1f })
                {
                    var q = W.G(p[i] + r * side * hw);
                    if (!LaneProfile.OnAsphalt(models, q.x, q.y))
                    {
                        on = false;
                        for (float d = 0; d <= hw; d += .1f) { var q2 = W.G(p[i] + r * side * (hw - d)); if (LaneProfile.OnAsphalt(models, q2.x, q2.y)) { off = Mathf.Max(off, d); break; } }
                    }
                }
            }
            if (!on) { bad++; worst = Mathf.Max(worst, off); if (float.IsNaN(runStart)) runStart = g.x; }
            else if (!float.IsNaN(runStart)) { runs.Append($" {runStart:F0}…{g.x:F0}"); runStart = float.NaN; }
        }
        sb.AppendLine($"{name}: {bad} samples off asphalt, worst {worst:F1} m;{runs}");
    }
}
public static class LaneDbg
{
    // autopilot path points in [x0, x1]: lateral vs model road (key, idx) and the asphalt run across the path there
    public static string Run(string key, int idx, float x0, float x1)
    {
        var models = WorldData.Models(); var m = models.Find(q => q.key == key); var rd = m.roads[idx]; var sb = new StringBuilder();
        var ap = Object.FindFirstObjectByType<Magistrala.Autopilot>(); var p = ap.path;
        for (int i = 0; i + 1 < p.Length; i++)
        {
            var g = W.G(p[i]); if (g.x < x0 || g.x > x1) continue;
            LaneProfile.Project(rd.p, g.x, g.y, out double s, out double u);
            var t = p[i + 1] - p[i]; t.y = 0; t.Normalize(); var r = new Vector3(t.z, 0, -t.x);
            float a = float.NaN, b = float.NaN;
            for (float d = -8; d <= 8; d += .1f) { var q = W.G(p[i] + r * d); bool on = LaneProfile.OnAsphalt(models, q.x, q.y); if (on && float.IsNaN(a)) a = d; if (on) b = d; }
            sb.AppendLine($"gx={g.x:F1} s={s:F0} u={u:F2} asphalt {a:F1}…{b:F1} (right of path)");
        }
        return sb.ToString();
    }
}
