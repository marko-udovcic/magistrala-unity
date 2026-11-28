// Review shots of the petrol stations / retail boxes: edit-mode renders from given GAME points (eye at ground + up m, looking at a
// game point + 1.5 m). Nothing in the scene changes.
// unity command script eval --code 'return Magistrala.EditorTools.BenzinskeShots.Shot("<file.png>", gx, gz, up, tx, tz, fov);'
using System.IO;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class BenzinskeShots
    {
        static float GroundAt(Vector3 p)
        {
            float y = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, 500, p.z), Vector3.down, 1000))
            {
                bool skip = false;
                for (var t = h.collider.transform; t; t = t.parent) if (t.name == "Benzinske" || t.name == "Trgovine" || t.name == "Buildings" || t.name == "Props") { skip = true; break; }
                if (!skip && !h.collider.attachedRigidbody) y = Mathf.Max(y, h.point.y);
            }
            return float.IsInfinity(y) ? (float)Tiles.Height(p.x + W.OX, -p.z + W.OZ) : y;
        }

        public static string Shot(string file, float gx, float gz, float up, float tx, float tz, float fov = 55, float tup = 1.5f)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            var cam = new GameObject("benz_cam").AddComponent<Camera>();
            if (Camera.main) cam.CopyFrom(Camera.main);
            cam.nearClipPlane = .2f; cam.farClipPlane = 3000; cam.fieldOfView = fov;
            const int Wd = 1600, Ht = 900;
            var rt = new RenderTexture(Wd, Ht, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.aspect = (float)Wd / Ht; cam.targetTexture = rt;
            var eye = W.U(gx, 0, gz); eye.y = GroundAt(eye) + up;
            var tgt = W.U(tx, 0, tz); tgt.y = GroundAt(tgt) + tup;
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(tgt - eye));
            cam.Render(); cam.Render();
            var tex = new Texture2D(Wd, Ht, TextureFormat.RGB24, false);
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, Wd, Ht), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(cam.gameObject);
            return $"{file}: eye {eye}, target {tgt}";
        }
    }
}
