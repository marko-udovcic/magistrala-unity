using System.Reflection;
using UnityEngine;
public static class Shot
{
    public static string Gold(bool on, float orbit)
    {
        var lp = Object.FindFirstObjectByType<Magistrala.LightPreset>(); lp.SetGolden(on);
        var cam = Object.FindFirstObjectByType<TruckChaseCamera>();
        if (cam) typeof(TruckChaseCamera).GetField("orbitYaw", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(cam, orbit);
        return $"golden {lp.Golden}, orbit {orbit}";
    }
}
public static class ShotSky
{
    public static string Rot(float add)
    {
        var m = RenderSettings.skybox; float r = m.GetFloat("_Rotation"); m.SetFloat("_Rotation", (r + add) % 360f); DynamicGI.UpdateEnvironment();
        return $"{m.name} rot {r} -> {m.GetFloat("_Rotation")}";
    }
}
