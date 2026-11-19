using System.Linq; using System.Text; using Magistrala.EditorTools;
public static class OutEnds
{
    // outside streets with an end within r m of any of the points "x,z;x,z": end kind, model, model half width, end height
    public static string Run(string pts, float r)
    {
        var net = StreetsBuilder.OutsideNet(); var sb = new StringBuilder();
        var P = pts.Split(';').Select(s => s.Split(',').Select(double.Parse).ToArray()).ToList();
        foreach (var st in net.streets)
            foreach (var atB in new[] { false, true })
            {
                var q = atB ? st.pts[st.pts.Count - 1] : st.pts[0]; var q2 = atB ? st.pts[st.pts.Count - 2] : st.pts[1];
                foreach (var p in P) if ((q[0] - p[0]) * (q[0] - p[0]) + (q[2] - p[1]) * (q[2] - p[1]) < r * r)
                {
                    var m = atB ? st.modelB : st.modelA; double mhw = atB ? st.mhwB : st.mhwA;
                    sb.AppendLine($"{st.name} [{st.id}] {(atB ? "B" : "A")} at {q[0]:F1},{q[2]:F1} y {q[1]:F2} model {(m?.key ?? "-")} mhw {mhw:F2} w {st.width} node {(atB ? st.b : st.a)} surf {(m != null ? m.Surface(q[0], q[2]) : double.NaN):F2}");
                }
            }
        return sb.ToString();
    }
}
