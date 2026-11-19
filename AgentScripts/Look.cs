using UnityEngine;
public static class Look
{
    // views "px,py,pz,tx,ty,tz[,fov]" absolute Unity coords (py / ty relative to the ground below when prefixed with g: not supported, absolute)
    public static string Run(string views, string files, int w, int h)
    {
        var cam = Camera.main; var cc = cam.GetComponent<TruckChaseCamera>(); bool ce = cc && cc.enabled; if (cc) cc.enabled = false;
        var p0 = cam.transform.position; var r0 = cam.transform.rotation; float f0 = cam.fieldOfView; var fl = files.Split(';'); int i = 0; var sb = new System.Text.StringBuilder();
        foreach (var v in views.Split(';'))
        {
            if (v.Length == 0) continue; var a = System.Array.ConvertAll(v.Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
            cam.transform.position = new Vector3(a[0], a[1], a[2]); cam.transform.LookAt(new Vector3(a[3], a[4], a[5])); cam.fieldOfView = a.Length > 6 ? a[6] : 50;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render(); cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt; var t = new Texture2D(w, h, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply(); RenderTexture.active = null;
            string f = fl[i++]; System.IO.File.WriteAllBytes(f, f.EndsWith(".png") ? t.EncodeToPNG() : t.EncodeToJPG(90)); Object.DestroyImmediate(t); rt.Release(); sb.AppendLine(f);
        }
        cam.transform.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0; if (cc) cc.enabled = ce;
        return sb.ToString();
    }
}
