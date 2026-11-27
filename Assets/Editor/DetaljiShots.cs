// Review shots of the street details (PropsBuilder.Detalji): a driver's-eye view (2.4 m up, from the road beside the item, 20 m back
// along the street) of chosen items of World/Props/Detalji. Edit-mode render, nothing in the scene changes.
// unity command script eval --code 'return Magistrala.EditorTools.DetaljiShots.Run("<dir>", "sm001,up0012");'
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class DetaljiShots
    {
        public static string Run(string dir, string names, float back = 20, float side = 3.5f, float up = 2.4f)
        {
            Directory.CreateDirectory(dir);
            var root = GameObject.Find("World")?.transform.Find("Props/Detalji"); if (!root) return "nema World/Props/Detalji";
            var all = root.GetComponentsInChildren<Transform>(true);
            var cam = new GameObject("detalji_cam").AddComponent<Camera>();
            if (Camera.main) cam.CopyFrom(Camera.main);
            cam.nearClipPlane = .2f; cam.farClipPlane = 2500; cam.fieldOfView = 55;
            const int Wd = 1600, Ht = 900;
            var rt = new RenderTexture(Wd, Ht, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.aspect = (float)Wd / Ht; cam.targetTexture = rt;
            var tex = new Texture2D(Wd, Ht, TextureFormat.RGB24, false);
            var done = new System.Collections.Generic.List<string>();
            var roads = StreetsBuilder.Carriageways();
            foreach (var n in names.Split(','))
            {
                var t = all.FirstOrDefault(x => x.name == n.Trim()); if (!t) { done.Add(n + ": ?"); continue; }
                var p = t.childCount > 0 && !t.GetComponent<LODGroup>() ? t.GetChild(0) : t;
                // the nearest carriageway: the eye on it (its lane on the item's side), `back` m before the item, looking at it
                var g = W.G(p.position); Vector2 best = g, bt = Vector2.right; float bd = 1e9f;
                foreach (var r in roads)
                {
                    var a = new Vector2((float)r.ax, (float)r.az); var dd = new Vector2((float)(r.bx - r.ax), (float)(r.bz - r.az)); float L = dd.magnitude; if (L < .05f) continue;
                    var u = dd / L; var q = a + u * Mathf.Clamp(Vector2.Dot(g - a, u), 0, L); float d = (g - q).magnitude;
                    if (d < bd) { bd = d; best = q + (g - q).normalized * Mathf.Min(d * .5f, (float)r.hw * .5f); bt = u; }
                }
                var e2 = best - bt * back; var eye = W.U(e2.x, 0, e2.y); eye.y = p.position.y + up;
                if (Physics.Raycast(eye + Vector3.up * 50, Vector3.down, out var hit, 200)) eye.y = hit.point.y + up;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(p.position + Vector3.up * 1.2f - eye));
                cam.Render(); cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, Wd, Ht), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, n.Trim() + ".png"), tex.EncodeToPNG()); done.Add(n);
            }
            cam.targetTexture = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex); UnityEngine.Object.DestroyImmediate(cam.gameObject);
            return string.Join(", ", done);
        }

        /// names of the first k items of a group (stupovi, kontejneri, ormarici, hidranti) of World/Props/Detalji, for picking shots
        public static string List(string group, int k)
        {
            var g = GameObject.Find("World")?.transform.Find("Props/Detalji/" + group); if (!g) return "?";
            return string.Join(",", Enumerable.Range(0, Math.Min(k, g.childCount)).Select(i => g.GetChild(i).name + "@" + g.GetChild(i).position.ToString("F0")));
        }
    }
}
