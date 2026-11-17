using UnityEngine;
using Magistrala.EditorTools;
public static class Cam
{
    // top-down camera above game point (gx,gz) at height h above ground y; rotation so +game-x points right on screen
    public static string Top(double gx, double gy, double gz, double h, float fov)
    {
        var cam = Camera.main; var cc = cam.GetComponent<TruckChaseCamera>(); if (cc) cc.enabled = false;
        cam.fieldOfView = fov;
        cam.transform.position = W.U(gx, gy + h, gz);
        var east = W.U(gx + 1, gy, gz) - W.U(gx, gy, gz); // screen-up = world dir of game +x ... make east point up-screen? use east as 'up' so +x is up
        cam.transform.rotation = Quaternion.LookRotation(Vector3.down, east);
        return $"cam {cam.transform.position}";
    }
}
public static class CabinCam
{
    public static string Set(bool on)
    {
        var cc = Object.FindFirstObjectByType<TruckChaseCamera>(); cc.enabled = true;
        typeof(TruckChaseCamera).GetField("<CabinView>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(cc, on);
        return $"cabin {cc.CabinView} near {Camera.main.nearClipPlane} fov {Camera.main.fieldOfView}";
    }
}
public static class Pick
{
    // list truck renderers whose mesh the camera ray through viewport (vx,vy) hits (triangle test on the mesh)
    public static string At(float vx, float vy)
    {
        var cam = Camera.main; var ray = cam.ViewportPointToRay(new Vector3(vx, vy, 0)); var sb = new System.Text.StringBuilder();
        var truck = Object.FindFirstObjectByType<TruckController>();
        foreach (var r in truck.GetComponentsInChildren<Renderer>(true))
        {
            var mf = r.GetComponent<MeshFilter>(); Mesh m = mf ? mf.sharedMesh : (r is SkinnedMeshRenderer s ? s.sharedMesh : null); if (!m) continue;
            var inv = r.transform.worldToLocalMatrix; var lr = new Ray(inv.MultiplyPoint3x4(ray.origin), inv.MultiplyVector(ray.direction).normalized);
            var vs = m.vertices; float best = float.MaxValue;
            for (int sm = 0; sm < m.subMeshCount; sm++) { var t = m.GetTriangles(sm); for (int i = 0; i < t.Length; i += 3) { if (RayTri(lr, vs[t[i]], vs[t[i + 1]], vs[t[i + 2]], out float d) && d < best) best = d; } }
            if (best < float.MaxValue) { var mats = r.sharedMaterials; sb.AppendLine($"{Path(r.transform)}  dist {best * r.transform.lossyScale.x:F2}  mats {string.Join(",", System.Array.ConvertAll(mats, q => q ? q.name + "/" + q.shader.name : "null"))} bounds {r.bounds.center} {r.bounds.size} near-cam {Vector3.Distance(cam.transform.position, r.bounds.center):F2}"); }
        }
        return sb.ToString();
    }
    static string Path(Transform t) { var s = t.name; while (t.parent && t.parent.name != "Truck") { t = t.parent; s = t.name + "/" + s; } return s; }
    static bool RayTri(Ray r, Vector3 a, Vector3 b, Vector3 c, out float t)
    {
        t = 0; var e1 = b - a; var e2 = c - a; var p = Vector3.Cross(r.direction, e2); float det = Vector3.Dot(e1, p); if (Mathf.Abs(det) < 1e-9f) return false;
        float inv = 1 / det; var s = r.origin - a; float u = Vector3.Dot(s, p) * inv; if (u < 0 || u > 1) return false;
        var q = Vector3.Cross(s, e1); float v = Vector3.Dot(r.direction, q) * inv; if (v < 0 || u + v > 1) return false; t = Vector3.Dot(e2, q) * inv; return t > 0;
    }
}
public static class Tree
{
    public static string Dump(string filter)
    {
        var truck = Object.FindFirstObjectByType<TruckController>(); var sb = new System.Text.StringBuilder();
        foreach (var t in truck.transform.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.ToLower().Contains(filter)) continue;
            var r = t.GetComponent<Renderer>(); var cam = Camera.main.transform;
            sb.AppendLine($"{t.name} parent {t.parent.name} localPos {t.localPosition} localEuler {t.localEulerAngles} localScale {t.localScale} {(r ? "bounds " + r.bounds.center + " size " + r.bounds.size : "")}");
        }
        sb.AppendLine("eye " + Camera.main.transform.position + " truck " + truck.transform.position);
        return sb.ToString();
    }
}
public static class Front
{
    // camera 'dist' m in front of wiper_0 (along truck forward), slightly right and above, looking at the wiper
    public static string View(float dist, float side, float up)
    {
        var cam = Camera.main; var truck = Object.FindFirstObjectByType<TruckController>(); var t = truck.transform;
        var cc = cam.GetComponent<TruckChaseCamera>(); if (cc) cc.enabled = false;
        var w = t.Find("Model/wiper_0").position; var f = t.forward; f.y = 0; f.Normalize();
        cam.transform.position = w + f * dist + t.right * side + Vector3.up * up; cam.transform.LookAt(w + Vector3.up * .2f); cam.fieldOfView = 45;
        return $"ok wiper {w} cam {cam.transform.position}";
    }
}
