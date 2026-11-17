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
