using UnityEngine;
public static class Tune
{
    // copy the code defaults of LightPreset's sunset tunables onto the scene component (serialized values win over code defaults)
    public static string Defaults()
    {
        var lp = Object.FindFirstObjectByType<Magistrala.LightPreset>();
        var go = new GameObject("tmp"); go.SetActive(false); var d = go.AddComponent<Magistrala.LightPreset>();
        lp.ambMul = d.ambMul; lp.exposureOff = d.exposureOff; lp.sunIntensity = d.sunIntensity; lp.splitShadows = d.splitShadows; lp.splitHighlights = d.splitHighlights;
        Object.DestroyImmediate(go);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(lp);
#endif
        return $"amb {lp.ambMul} exp {lp.exposureOff} sun {lp.sunIntensity}";
    }
}
public static class TuneAmb
{
    public static string Set(float amb, float exp)
    {
        var lp = Object.FindFirstObjectByType<Magistrala.LightPreset>(); lp.ambMul = amb; lp.exposureOff = exp; lp.SetGolden(true);
        return $"amb {amb} exp {exp}";
    }
}
