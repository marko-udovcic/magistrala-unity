using System.Linq; using UnityEngine;
public static class TruckInfo
{
    public static string Mats()
    {
        var t = Object.FindFirstObjectByType<TruckController>(); if (!t) return "no truck";
        var sb = new System.Text.StringBuilder(t.name + " " + t.transform.position + "\n");
        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
        {
            var b = r.bounds; var lc = t.transform.InverseTransformPoint(b.center);
            sb.AppendLine($"{r.name} [{string.Join(",", r.sharedMaterials.Select(m => m ? m.name : "-"))}] local {lc} size {b.size}");
        }
        return sb.ToString();
    }
}
