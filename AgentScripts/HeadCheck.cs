using System.Linq; using UnityEngine;
public static class HeadCheck
{
    public static string Run()
    {
        var t = Object.FindFirstObjectByType<Magistrala.Traffic.TrafficSystem>();
        return string.Join("; ", t.data.kinds.Select(k => $"{k.name}: head {k.lod0.Count(p => p.lamp == 4)} tail {k.lod0.Count(p => p.lamp == 1)} lod1head {(k.lod1 ?? new Magistrala.Traffic.TrafficPart[0]).Count(p => p.lamp == 4)}")) + " | headlights " + t.headlights;
    }
}
