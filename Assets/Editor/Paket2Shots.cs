// Paket2 review shots: street-level editor renders of fixed spots in the Paket2 areas (before / after), and previews of pack
// prefabs. Edit-mode render, nothing in the scene changes.
// unity command script eval --code 'return Magistrala.EditorTools.Paket2Shots.Spots("<dir>", "prije");'
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class Paket2Shots
    {
        /// Kaštel Lukšić: (name, game x, z near a street, the direction to look along it (game x, z))
        public static readonly (string name, float x, float z, float dx, float dz)[] Luksic =
        {
            ("1-jezgra", -6230, -4731, 1, 0), ("2-sjever-jug", -6181, -4840, 0, 1), ("3-ulica", -6290, -4855, 1, 0),
            ("4-zapad", -6055, -4960, 0, 1), ("5-istok", -6040, -4843, 1, 0), ("6-gornja", -6060, -4702, 1, 0),
        };

        static (Camera cam, RenderTexture rt, Texture2D tex) Rig(int w, int h)
        {
            var cam = new GameObject("paket2_cam").AddComponent<Camera>();
            if (Camera.main) cam.CopyFrom(Camera.main);
            cam.nearClipPlane = .1f; cam.farClipPlane = 2500; cam.fieldOfView = 60;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.aspect = (float)w / h; cam.targetTexture = rt;
            return (cam, rt, new Texture2D(w, h, TextureFormat.RGB24, false));
        }
        static void Save((Camera cam, RenderTexture rt, Texture2D tex) r, string path)
        {
            r.cam.Render(); RenderTexture.active = r.rt; r.tex.ReadPixels(new Rect(0, 0, r.rt.width, r.rt.height), 0, 0); r.tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(path, r.tex.EncodeToPNG());
        }
        static void Free((Camera cam, RenderTexture rt, Texture2D tex) r) { UnityEngine.Object.DestroyImmediate(r.cam.gameObject); r.rt.Release(); UnityEngine.Object.DestroyImmediate(r.tex); }

        public static string Spots(string dir, string tag)
        {
            Directory.CreateDirectory(dir);
            var roads = StreetsBuilder.Carriageways(); var r = Rig(1600, 900); var log = new List<string>();
            // make every LOD level / cell choose as from the camera (edit mode: LODGroups use the rendering camera)
            foreach (var (name, x, z, dx, dz) in Luksic)
            {
                Vector2 g = new Vector2(x, z), best = g, bt = new Vector2(dx, dz).normalized; float bd = 1e9f;
                foreach (var c in roads)
                {
                    var a = new Vector2((float)c.ax, (float)c.az); var d = new Vector2((float)(c.bx - c.ax), (float)(c.bz - c.az)); float L = d.magnitude; if (L < .05f) continue;
                    var u = d / L; var q = a + u * Mathf.Clamp(Vector2.Dot(g - a, u), 0, L); float dd = (g - q).magnitude;
                    if (dd < bd) { bd = dd; best = q; bt = Vector2.Dot(u, new Vector2(dx, dz)) >= 0 ? u : -u; }
                }
                foreach (float h in new[] { 1.7f, 4f })
                {
                    var eye = W.U(best.x, 0, best.y);
                    eye.y = Physics.Raycast(eye + Vector3.up * 200, Vector3.down, out var hit, 400) ? hit.point.y : 0; eye.y += h;
                    var fwd = W.U(bt.x, 0, bt.y) - W.U(0, 0, 0);
                    r.cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(fwd) * Quaternion.Euler(h > 3 ? 6 : 1, 0, 0));
                    string f = Path.Combine(dir, $"{tag}_{name}_{h:F1}m.png"); Save(r, f);
                }
                log.Add($"{name}: road at ({best.x:F0}, {best.y:F0}) {bd:F1} m off");
            }
            Free(r);
            return string.Join("; ", log);
        }

        /// free views: "name:ex,ez,eh,tx,tz,th;..." (game x / z, heights above the ground)
        public static string Views(string dir, string spec)
        {
            Directory.CreateDirectory(dir); var r = Rig(1600, 900); var log = new List<string>();
            float Gnd(Vector3 u) => Physics.Raycast(u + Vector3.up * 200, Vector3.down, out var hit, 400) ? hit.point.y : 0;
            foreach (var v in spec.Split(';'))
            {
                if (v.Trim().Length == 0) continue;
                var parts = v.Split(':'); var n = Array.ConvertAll(parts[1].Split(','), q => float.Parse(q, System.Globalization.CultureInfo.InvariantCulture));
                var e = W.U(n[0], 0, n[1]); e.y = Gnd(e) + n[2]; var t = W.U(n[3], 0, n[4]); t.y = Gnd(t) + n[5];
                r.cam.transform.position = e; r.cam.transform.LookAt(t); Save(r, Path.Combine(dir, parts[0].Trim() + ".png")); log.Add(parts[0]);
            }
            Free(r); return string.Join(", ", log);
        }

        /// pack prefabs side by side at y -2000 (front views), one png each
        public static string Prefabs(string dir, string names)
        {
            Directory.CreateDirectory(dir); var r = Rig(800, 800); var log = new List<string>();
            foreach (var n in names.Split(','))
            {
                var pf = AssetDatabase.LoadAssetAtPath<GameObject>(PaketZgrade.SRC + "Prefabs/" + n.Trim() + ".prefab"); if (!pf) { log.Add(n + " ?"); continue; }
                var go = (GameObject)UnityEngine.Object.Instantiate(pf); go.transform.position = new Vector3(0, -2000, 0);
                var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) { UnityEngine.Object.DestroyImmediate(go); continue; }
                var b = rs[0].bounds; foreach (var x in rs) b.Encapsulate(x.bounds);
                float s = Mathf.Max(b.size.x, b.size.y, b.size.z);
                var light = new GameObject("l").AddComponent<Light>(); light.type = LightType.Directional; light.transform.rotation = Quaternion.Euler(35, 160, 0); light.intensity = 2;
                foreach (var (tagv, dirv) in new[] { ("f", new Vector3(.35f, .25f, 1)), ("b", new Vector3(-.35f, .25f, -1)) })
                {
                    r.cam.transform.position = b.center + dirv.normalized * s * 1.6f; r.cam.transform.LookAt(b.center);
                    r.cam.clearFlags = CameraClearFlags.SolidColor; r.cam.backgroundColor = new Color(.55f, .6f, .65f);
                    Save(r, Path.Combine(dir, n.Trim() + "_" + tagv + ".png"));
                }
                UnityEngine.Object.DestroyImmediate(light.gameObject);
                log.Add($"{n}: {b.size.x:F2} x {b.size.y:F2} x {b.size.z:F2}");
                UnityEngine.Object.DestroyImmediate(go);
            }
            Free(r); return string.Join("; ", log);
        }
    }
}
#endif
