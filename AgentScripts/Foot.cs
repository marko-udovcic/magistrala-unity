using System.Linq; using System.Text; using Magistrala.EditorTools;
public static class Foot
{
    public static string Run(string key, float x, float z0, float z1)
    {
        var m = WorldData.Models().Find(q => q.key == key); var sb = new StringBuilder();
        var f = m.grid.GetType().GetField("foot"); var foot = f?.GetValue(m.grid) as float[];
        sb.AppendLine($"grid fields: {string.Join(",", m.grid.GetType().GetFields().Select(q => q.Name))}");
        for (float z = z0; z <= z1; z += 2) { int c = m.grid.Cell(x, z); sb.AppendLine($"z {z}: cell {c} surf {m.Surface(x, z):F2} foot {(foot != null && c >= 0 ? foot[c] : float.NaN):F1}"); }
        return sb.ToString();
    }
}
