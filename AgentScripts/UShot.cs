using UnityEngine;
public static class UShot
{
    // views "ux,uz,dist,yaw,pitch[,fov[,lookUp]]" in Unity coords: camera orbits the target point (ground there) at dist m, yaw deg
    // (0 = camera south of the target looking north, 90 = west of it looking east), pitch deg above the horizon → file per view
    public static string Run(string views, string files, int w, int h)
    {
        var cam = Camera.main; var sb = new System.Text.StringBuilder();
        var cc = cam.GetComponent<TruckChaseCamera>(); bool ce = cc && cc.enabled; if (cc) cc.enabled = false;
        var p0 = cam.transform.position; var r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
        var fl = files.Split(';'); int i = 0;
        foreach (var v in views.Split(';'))
        {
            if (v.Length == 0) continue; var a = System.Array.ConvertAll(v.Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
            float y = 0; foreach (var hh in Physics.RaycastAll(new Vector3(a[0], 600, a[1]), Vector3.down, 1200)) if (!hh.collider.GetComponentInParent<LODGroup>() || true) y = Mathf.Max(y, -999);
            if (Physics.Raycast(new Vector3(a[0], 600, a[1]), Vector3.down, out var hit, 1200)) y = hit.point.y;
            var tgt = new Vector3(a[0], y + (a.Length > 6 ? a[6] : 0), a[1]);
            var q = Quaternion.Euler(a[4], a[3], 0);
            cam.transform.position = tgt - q * Vector3.forward * a[2]; cam.transform.rotation = q;
            cam.fieldOfView = a.Length > 5 ? a[5] : 50;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render(); cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt; var t = new Texture2D(w, h, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply(); RenderTexture.active = null;
            string f = fl[i++]; System.IO.File.WriteAllBytes(f, f.EndsWith(".png") ? t.EncodeToPNG() : t.EncodeToJPG(88)); Object.DestroyImmediate(t); rt.Release(); sb.AppendLine(f + " y " + y.ToString("F1"));
        }
        cam.transform.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0; if (cc) cc.enabled = ce;
        return sb.ToString();
    }
}
