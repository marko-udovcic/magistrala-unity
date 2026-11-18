using UnityEngine;
using Magistrala.EditorTools;
public static class TopShot
{
    // views "gx,gz,h,ang[,tilt];…" (ang = heading deg from east, ccw in the game frame; tilt 90 = straight down) → dir/name_i.jpg
    public static string Run(string views, string dir, string name)
    {
        var cam = Camera.main; var sb = new System.Text.StringBuilder(); int i = 0;
        var cc = cam.GetComponent<TruckChaseCamera>(); bool ce = cc && cc.enabled; if (cc) cc.enabled = false;
        var p0 = cam.transform.position; var r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
        foreach (var v in views.Split(';'))
        {
            if (v.Length == 0) continue; var a = System.Array.ConvertAll(v.Split(','), float.Parse);
            var u = W.U(a[0], 0, a[1]); float y = 40; if (Physics.Raycast(new Vector3(u.x, 400, u.z), Vector3.down, out var hit, 800)) y = hit.point.y;
            float ang = a[3] * Mathf.Deg2Rad; var fwd = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)); // game z south = unity -z: ccw heading → (cos, -sin) in game = (cos, +sin) unity
            float tilt = a.Length > 4 ? a[4] : 90;
            cam.transform.position = new Vector3(u.x, y + a[2], u.z) - fwd * (tilt < 89 ? a[2] / Mathf.Tan(tilt * Mathf.Deg2Rad) : 0);
            cam.transform.rotation = Quaternion.LookRotation(Quaternion.AngleAxis(tilt, Vector3.Cross(Vector3.up, fwd)) * fwd, tilt > 89 ? fwd : Vector3.up);
            cam.fieldOfView = 50;
            int w = 1280, h = 800; var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render(); cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt; var t = new Texture2D(w, h, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply(); RenderTexture.active = null;
            string f = $"{dir}/{name}_{i++}.jpg"; System.IO.File.WriteAllBytes(f, t.EncodeToJPG(85)); Object.DestroyImmediate(t); rt.Release(); sb.AppendLine(f);
        }
        cam.transform.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0; if (cc) cc.enabled = ce;
        return sb.ToString();
    }
}
