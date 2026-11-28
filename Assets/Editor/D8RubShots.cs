// Review shots of the D8 between the Lukšić junction (krizanje-01) and the Kambelovac overpass (krizanje-51): the same views before
// and after a change (edit-mode render, nothing in the scene changes). Along both D8 traffic corridors at fixed game x stations:
// "ulica" = eye 1.7 m over the outermost lane, looking 35 m ahead and a little to the right (the verge / sidewalk side);
// "chase" = 4 m up, 9 m behind the station on the outermost lane, looking 25 m ahead. Plus the Lukšić junction approaches.
// unity command script eval --code 'return Magistrala.EditorTools.D8RubShots.Run("<dir>", "prije");'
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class D8RubShots
    {
        static readonly float[] Stations = { -5980, -5700, -5420, -5140, -4860, -4580, -4330 };

        public static string Run(string dir, string tag, string only = null)
        {
            Directory.CreateDirectory(dir);
            var td = AssetDatabase.FindAssets("t:TrafficData").Select(g => AssetDatabase.LoadAssetAtPath<Magistrala.Traffic.TrafficData>(AssetDatabase.GUIDToAssetPath(g))).FirstOrDefault(d => d && d.corridors != null);
            if (!td) return "no TrafficData";
            var cam = new GameObject("d8rub_cam").AddComponent<Camera>();
            if (Camera.main) cam.CopyFrom(Camera.main);
            cam.nearClipPlane = .2f; cam.farClipPlane = 2500; cam.fieldOfView = 60;
            const int Wd = 1600, Ht = 900;
            var rt = new RenderTexture(Wd, Ht, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.aspect = (float)Wd / Ht; cam.targetTexture = rt;
            var tex = new Texture2D(Wd, Ht, TextureFormat.RGB24, false);
            int n = 0;
            void Shot(string name, Vector3 eye, Vector3 at)
            {
                if (only != null && !name.Contains(only)) return;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(at - eye));
                cam.Render(); cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, Wd, Ht), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(dir, $"{tag}_{name}.png"), tex.EncodeToPNG()); n++;
            }
            float Ground(Vector3 p)
            {
                float best = p.y;
                foreach (var h in Physics.RaycastAll(p + Vector3.up * 6, Vector3.down, 12).OrderBy(h => h.distance)) { best = h.point.y; break; }
                return best;
            }
            foreach (var c in td.corridors.Where(c => c.name.StartsWith("D8")))
            {
                string dn = c.name.Contains("istok") ? "istok" : "zapad";
                // station index: nearest axis point at game x
                int At(float gx) { int bi = 0; float bd = 1e9f; for (int i = 0; i < c.axis.Length; i++) { float d = Mathf.Abs(W.G(c.axis[i]).x - gx); if (d < bd) { bd = d; bi = i; } } return bi; }
                (Vector3 p, Vector3 f, Vector3 r) Lane(int i)
                {
                    i = Mathf.Clamp(i, 1, c.axis.Length - 2);
                    var f = c.axis[i + 1] - c.axis[i - 1]; f.y = 0; f.Normalize();
                    float hi = -99; for (int k = 0; k < c.maxLanes; k++) { float o = c.laneOff[i * c.maxLanes + k]; if (!float.IsNaN(o)) hi = Mathf.Max(hi, o); }
                    var p = c.axis[i] + c.right[i] * hi; p.y = Ground(p);
                    return (p, f, c.right[i]);
                }
                foreach (var gx in Stations)
                {
                    int i = At(gx); int stepsPerM = Mathf.Max(1, Mathf.RoundToInt(1f / c.step));
                    var (p, f, r) = Lane(i);
                    Shot($"{dn}_{-gx:F0}_ulica", p + Vector3.up * 1.7f, p + f * 35 + r * 5 + Vector3.up * 1.0f);
                    var (pb, fb, _) = Lane(i - Mathf.RoundToInt(9 / c.step));
                    Shot($"{dn}_{-gx:F0}_chase", pb + Vector3.up * 4f, pb + fb * 34 + Vector3.up * 1.2f);
                }
                // the Lukšić junction (centre game -6040, -5585): 90 m and 45 m before it on this corridor
                int j = 0; float jd = 1e9f; var jc = new Vector2(-6040, -5585);
                for (int k = 0; k < c.axis.Length; k++) { float d = (W.G(c.axis[k]) - jc).magnitude; if (d < jd) { jd = d; j = k; } }
                foreach (var back in new[] { 90, 45 })
                {
                    var (p, f, r) = Lane(j - Mathf.RoundToInt(back / c.step));
                    Shot($"{dn}_luksic_{back}m", p - r * 3 + Vector3.up * 1.9f, W.U(jc.x, p.y + 3.5f, jc.y) + f * 10);
                }
            }
            // the junction from its south arm (the north-west corner and the island), and an overview
            Shot("luksic_jug", Lift(W.U(-6036, 0, -5560), 1.7f), Lift(W.U(-6048, 0, -5612), 3f));
            Shot("luksic_salon", Lift(W.U(-6030, 0, -5572), 1.7f), Lift(W.U(-6068, 0, -5618), 3f));
            Shot("luksic_plocnik_zapad", Lift(W.U(-6047, 0, -5605.5), 1.7f), Lift(W.U(-6110, 0, -5612), 1.2f));
            Shot("luksic_pregled", Lift(W.U(-5990, 0, -5545), 22f), Lift(W.U(-6045, 0, -5595), 0f));
            cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); Object.DestroyImmediate(cam.gameObject);
            return $"{n} shots in {dir}";
            Vector3 Lift(Vector3 u, float up) { u.y = 200; if (Physics.Raycast(u, Vector3.down, out var h, 400)) u.y = h.point.y + up; return u; }
        }
    }
}
