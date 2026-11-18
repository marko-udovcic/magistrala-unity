using System.Linq; using UnityEngine;
public static class Lamps
{
    public static string Find()
    {
        var names = new System.Collections.Generic.Dictionary<string,int>();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            foreach (var m in r.sharedMaterials) if (m) { var n = m.name.ToLower(); if (n.Contains("lamp") || n.Contains("light") || n.Contains("svjet") || n.Contains("rasvj") || n.Contains("pole") || n.Contains("stup")) names[m.name] = names.TryGetValue(m.name, out var c) ? c + 1 : 1; }
        var objs = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Where(t => { var n = t.name.ToLower(); return n.Contains("lamp") || n.Contains("rasvj") || n.Contains("streetlight"); }).Take(10).Select(t => t.name);
        return string.Join("; ", names.Select(kv => kv.Key + " x" + kv.Value)) + " | objs: " + string.Join(", ", objs);
    }
}
