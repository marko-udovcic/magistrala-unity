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
public static class CamCar
{
    /// camera beside the n-th nearest AI car (to the truck), looking at it; freezes traffic time so both LOD shots match
    public static string Near(int n, float side, float back, float up)
    {
        var t = Object.FindFirstObjectByType<Magistrala.Traffic.TrafficSystem>();
        var tr = Object.FindFirstObjectByType<TruckController>().transform;
        var cars = new System.Collections.Generic.List<Magistrala.Traffic.TrafficSystem.Car>(t.Cars);
        cars.Sort((a, b) => (a.pos - tr.position).sqrMagnitude.CompareTo((b.pos - tr.position).sqrMagnitude));
        var c = cars[Mathf.Min(n, cars.Count - 1)];
        var cc = Object.FindFirstObjectByType<TruckChaseCamera>(); if (cc) cc.enabled = false;
        var cam = Camera.main; cam.transform.position = c.pos + c.rot * new Vector3(side, up, -back); cam.transform.LookAt(c.pos + Vector3.up * .8f);
        return t.data.kinds[c.kind].name + " v " + (c.v * 3.6f).ToString("F0");
    }
}
