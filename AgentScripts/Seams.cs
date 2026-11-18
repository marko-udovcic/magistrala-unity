public static class Seams
{
    // RailSeams log without rebuilding the roads (dry: geometry goes under a throwaway object)
    public static string Run()
    {
        var models = Magistrala.EditorTools.WorldData.Models(); var tmp = new UnityEngine.GameObject("tmp-seams");
        var gos = new System.Collections.Generic.Dictionary<string, UnityEngine.GameObject>();
        foreach (var m in models) { var g = new UnityEngine.GameObject(m.key); g.transform.SetParent(tmp.transform); gos[m.key] = g; }
        var s = Magistrala.EditorTools.RailSeams.Build(models, gos, null, null);
        UnityEngine.Object.DestroyImmediate(tmp); return s;
    }
}
