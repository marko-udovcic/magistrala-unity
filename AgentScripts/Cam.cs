using UnityEngine;
public static class Cam
{
    /// free camera: offset from the truck (world), looking at truck + look offset; chase camera off
    public static string At(float ox, float oy, float oz, float lx, float ly, float lz)
    {
        var cc = Object.FindFirstObjectByType<TruckChaseCamera>(); if (cc) cc.enabled = false;
        var t = Object.FindFirstObjectByType<TruckController>().transform;
        var cam = Camera.main; cam.transform.position = t.position + new Vector3(ox, oy, oz); cam.transform.LookAt(t.position + new Vector3(lx, ly, lz));
        return cam.transform.position.ToString();
    }
    public static string Chase() { var cc = Object.FindFirstObjectByType<TruckChaseCamera>(); if (cc) cc.enabled = true; return "chase"; }
}
