using UnityEngine;
public static class PropView
{
    // views "group|dist|yaw|pitch|fov|dx|dz" (group under World/Props; target = its renderers' bounds centre (+dx/dz)); yaw 0 = camera south looking north
    public static string Run(string views, string files, int w, int h)
    {
        var cam = Camera.main; var sb = new System.Text.StringBuilder();
        var cc = cam.GetComponent<TruckChaseCamera>(); bool ce = cc && cc.enabled; if (cc) cc.enabled = false;
        var p0 = cam.transform.position; var r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
        var fl = files.Split(';'); int i = 0;
        foreach (var v in views.Split(';'))
        {
            if (v.Length == 0) continue; var a = v.Split('|');
            var nm = a[0].Split('#'); var g = GameObject.Find("World/Props/" + nm[0]); if (g && nm.Length > 1) g = g.transform.GetChild(int.Parse(nm[1]) % g.transform.childCount).gameObject; if (!g) { sb.AppendLine("no " + a[0]); i++; continue; }
            var b = new Bounds(); bool first = true;
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>()) { if (!r.name.StartsWith("lod0")) continue; if (first) b = r.bounds; else b.Encapsulate(r.bounds); first = false; }
            float F(int k, float d) => a.Length > k ? float.Parse(a[k], System.Globalization.CultureInfo.InvariantCulture) : d;
            var tgt = b.center + new Vector3(F(5, 0), 0, F(6, 0));
            float yaw;
            if (a.Length > 2 && a[2].StartsWith("a"))
            {
                // look at the nearest building wall (the props stand against it), turned by the number after "a"
                float bd = 1e9f; Vector3 bdir = Vector3.forward;
                for (int k = 0; k < 72; k++)
                {
                    var d = Quaternion.Euler(0, k * 5, 0) * Vector3.forward;
                    foreach (var hh in Physics.RaycastAll(b.center + Vector3.up * .3f, d, 15))
                    { bool bl = false; for (var t2 = hh.collider.transform; t2; t2 = t2.parent) if (t2.name == "Buildings") bl = true; if (bl && hh.distance < bd) { bd = hh.distance; bdir = d; } }
                }
                yaw = Mathf.Atan2(bdir.x, bdir.z) * Mathf.Rad2Deg + (a[2].Length > 1 ? float.Parse(a[2].Substring(1)) : 0);
            }
            else yaw = F(2, 0);
            var q = Quaternion.Euler(F(3, 20), yaw, 0);
            cam.transform.position = tgt - q * Vector3.forward * F(1, 10); cam.transform.rotation = q; cam.fieldOfView = F(4, 50);
            foreach (var lg in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None)) lg.ForceLOD(-1);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render(); cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt; var t = new Texture2D(w, h, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, w, h), 0, 0); t.Apply(); RenderTexture.active = null;
            string f = fl[i++]; System.IO.File.WriteAllBytes(f, f.EndsWith(".png") ? t.EncodeToPNG() : t.EncodeToJPG(90)); Object.DestroyImmediate(t); rt.Release();
            sb.AppendLine($"{f} centre {b.center} size {b.size}");
        }
        cam.transform.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0; if (cc) cc.enabled = ce;
        return sb.ToString();
    }
}
