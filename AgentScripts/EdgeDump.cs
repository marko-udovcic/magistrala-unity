using System.Linq; using System.Text; using System.Reflection; using System.Collections;
public static class EdgeDump
{
    public static string Run(long id)
    {
        var T = typeof(Magistrala.EditorTools.StreetsBuilder); var BF = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public;
        T.GetMethod("LoadGraph", BF).Invoke(null, null); T.GetMethod("LoadModels", BF).Invoke(null, null);
        var edges = (IList)T.GetField("edges", BF).GetValue(null); var sb = new StringBuilder();
        foreach (var e in edges)
        {
            var et = e.GetType(); long eid = (long)et.GetField("id").GetValue(e); if (eid != id) continue;
            var pts = (System.Collections.Generic.List<double[]>)et.GetField("pts").GetValue(e);
            sb.AppendLine($"edge {eid} a {et.GetField("a").GetValue(e)} b {et.GetField("b").GetValue(e)} closed {et.GetField("closed").GetValue(e)} n {pts.Count}: " + string.Join(" ", pts.Take(8).Select(p => $"({p[0]:F1},{p[1]:F2},{p[2]:F1})")));
        }
        var hidden = T.GetField("hidden", BF)?.GetValue(null);
        return sb.ToString() + " hidden field " + (hidden != null);
    }
}
