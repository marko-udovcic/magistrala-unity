using UnityEngine;
public static class VegStat
{
    public static string Set(bool legacy, float dist) { Magistrala.TreeRenderer.Legacy = legacy; Magistrala.TreeRenderer.Distance = dist; return $"legacy {legacy} dist {dist}"; }
    public static string Run()
    {
        var t = Object.FindFirstObjectByType<Magistrala.TreeRenderer>();
        var sb = new System.Text.StringBuilder($"legacy {Magistrala.TreeRenderer.Legacy} dist {Magistrala.TreeRenderer.Distance} drawn {t.DrawnLastFrame} trisMain {t.TrisMain / 1000}k trisShadowCasting {t.TrisShadow / 1000}k cam {Camera.main.transform.position}\n");
        for (int p = 0; p < t.meshes.Length; p++) sb.AppendLine($"  {t.meshes[p].name}: full {t.statFull[p]} imp {t.statImp[p]} fullSh {t.statFullSh[p]} impSh {t.statImpSh[p]}");
        return sb.ToString();
    }
}
