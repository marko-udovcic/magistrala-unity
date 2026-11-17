using System.Collections.Generic;
using UnityEngine;
using Magistrala.EditorTools;
public static class Marks
{
    static void Cube(Transform root, Vector3 p, Color c, float s)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(g.GetComponent<Collider>());
        g.transform.SetParent(root, false); g.transform.position = p; g.transform.localScale = new Vector3(s, s, s);
        var m = new Material(Shader.Find("Unlit/Color")); m.color = c; g.GetComponent<Renderer>().sharedMaterial = m;
    }
    public static string Draw(double gx0, double gx1)
    {
        Clear();
        var root = new GameObject("__marks").transform;
        var ap = Object.FindFirstObjectByType<Magistrala.Autopilot>(); int a = 0, b = 0, c2 = 0;
        foreach (var p in ap.path) { var g = W.G(p); if (g.x >= gx0 && g.x <= gx1) { Cube(root, p + Vector3.up * .3f, Color.red, 1.1f); a++; } }
        var models = WorldData.Models();
        foreach (var q in TruckSetup.EastboundPath()) if (q.x >= gx0 && q.x <= gx1) { double y = double.NaN; foreach (var m in models) { y = m.Surface(q.x, q.y); if (!double.IsNaN(y)) break; } Cube(root, W.U(q.x, double.IsNaN(y) ? 44 : y, q.y) + Vector3.up * .35f, Color.green, .8f); b++; }
        foreach (var m in models) foreach (var r in m.roads) if (r.n >= 2 && r.lvl == 0) foreach (var pt in r.p) if (pt[0] >= gx0 && pt[0] <= gx1) { double y = m.Surface(pt[0], pt[1]); Cube(root, W.U(pt[0], double.IsNaN(y) ? 44 : y, pt[1]) + Vector3.up * .4f, Color.blue, .5f); c2++; }
        return $"red {a} (scene path) green {b} (fresh) blue {c2} (axes)";
    }
    public static string Clear() { var o = GameObject.Find("__marks"); if (o) Object.DestroyImmediate(o); return "cleared"; }
}
