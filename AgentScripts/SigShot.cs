using UnityEngine;
public static class SigShot
{
    // free camera looking at signal head k from `back` m in front of it (approaching driver's view)
    public static string At(int k, float back, float up)
    {
        var s = Object.FindFirstObjectByType<Magistrala.Traffic.TrafficSignals>(); var h = s.heads[k];
        var head = h.amber.transform; var face = -head.forward; // lamps face +forward of head rotation
        var cc = Object.FindFirstObjectByType<TruckChaseCamera>(); if (cc) cc.enabled = false;
        var cam = Camera.main; var p = head.position + head.forward * back; p.y = head.position.y - 3 + up;
        cam.transform.position = p; cam.transform.LookAt(head.position - Vector3.up * 1.2f);
        return $"head {k} {s.heads.Length} state {s.traffic.LightState(h.corridor, h.stop)} at {head.position}";
    }
}
