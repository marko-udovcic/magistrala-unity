// Probni katalog paketa "UKRAINIAN CITY: Streets & Buildings" (Assets/OdesaUkraine, NIJE u gitu): svaki prefab snimljen
// zasebno (3/4 pogled, okvir po bounds) u privremenoj praznoj sceni + statistika (trokuti, rendereri, materijali, najveća
// tekstura, sumnjivi shaderi) u katalog.csv; materijali (fasade, tlo) kao kocka 2 m. Slike se slažu u listove izvan Unityja.
// unity command script eval --code 'return Magistrala.EditorTools.UkrajinaKatalog.Run("<dir>");'
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class UkrajinaKatalog
    {
        public const string ROOT = "Assets/OdesaUkraine";
        const int PX = 384;

        public static string Run(string dir, bool prefabs = true, bool materials = true)
        {
            Directory.CreateDirectory(dir);
            var back = EditorSceneManager.GetActiveScene().path;
            if (EditorSceneManager.GetActiveScene().isDirty) return "scena je izmijenjena (nije spremljena) — prekid";
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var cam = Camera.main; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.62f, .68f, .74f);
            cam.fieldOfView = 30; cam.nearClipPlane = .05f; cam.farClipPlane = 2000;
            var sun = Object.FindFirstObjectByType<Light>(); sun.intensity = 1.6f; sun.transform.rotation = Quaternion.Euler(40, -35, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat; RenderSettings.ambientLight = new Color(.55f, .57f, .6f);
            var rt = new RenderTexture(PX, PX, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.aspect = 1;
            var tex = new Texture2D(PX, PX, TextureFormat.RGB24, false);
            var csv = new StringBuilder("vrsta;ime;trokuti;rendereri;slotovi;materijali;maxTex;shaderi;greske\n");
            int n = 0;
            if (prefabs)
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { ROOT + "/Prefabs" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var pf = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!pf) continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(pf);
                    go.transform.position = Vector3.zero;
                    csv.Append("prefab;" + Rel(path) + ";" + Stats(go) + "\n");
                    Shoot(cam, go, rt, tex, Path.Combine(dir, "p_" + Safe(Rel(path)) + ".png"));
                    Object.DestroyImmediate(go); n++;
                }
            if (materials)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.localScale = Vector3.one * 2;
                foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { ROOT + "/Materials" }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (!m) continue;
                    cube.GetComponent<MeshRenderer>().sharedMaterial = m;
                    csv.Append("material;" + Rel(path) + ";12;1;1;1;" + MaxTex(new[] { m }) + ";" + m.shader.name + ";" + (Bad(m) ? "SHADER" : "") + "\n");
                    Shoot(cam, cube, rt, tex, Path.Combine(dir, "m_" + Safe(Rel(path)) + ".png")); n++;
                }
                Object.DestroyImmediate(cube);
            }
            cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            File.WriteAllText(Path.Combine(dir, "katalog.csv"), csv.ToString());
            EditorSceneManager.OpenScene(back);
            return "snimljeno " + n;
        }

        static string Rel(string p) => p.Substring(ROOT.Length + 1).Replace(".prefab", "").Replace(".mat", "");
        static string Safe(string s) => s.Replace('/', '~').Replace(' ', '_');
        static bool Bad(Material m) => m.shader == null || !m.shader.isSupported || m.shader.name.StartsWith("HDRP") || m.shader.name.Contains("InternalError");

        public static string Stats(GameObject go)
        {
            long tris = 0; int slots = 0; var mats = new HashSet<Material>(); var shaders = new HashSet<string>(); int bad = 0;
            var rs = go.GetComponentsInChildren<Renderer>(true);
            // LOD groups: count LOD0 only
            var lodSkip = new HashSet<Renderer>();
            foreach (var lg in go.GetComponentsInChildren<LODGroup>(true)) { var l = lg.GetLODs(); for (int i = 1; i < l.Length; i++) foreach (var r in l[i].renderers) if (r) lodSkip.Add(r); }
            int count = 0;
            foreach (var r in rs)
            {
                if (lodSkip.Contains(r)) continue; count++;
                Mesh mesh = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh) for (int i = 0; i < mesh.subMeshCount; i++) tris += mesh.GetIndexCount(i) / 3;
                foreach (var m in r.sharedMaterials) { slots++; if (!m) continue; mats.Add(m); shaders.Add(m.shader.name); if (Bad(m)) bad++; }
            }
            return tris + ";" + count + ";" + slots + ";" + mats.Count + ";" + MaxTex(mats) + ";" + string.Join("|", shaders) + ";" + (bad > 0 ? "SHADER x" + bad : "");
        }

        static int MaxTex(IEnumerable<Material> mats)
        {
            int mx = 0;
            foreach (var m in mats) foreach (var id in m.GetTexturePropertyNameIDs()) { var t = m.GetTexture(id); if (t) mx = Mathf.Max(mx, t.width); }
            return mx;
        }

        static void Shoot(Camera cam, GameObject go, RenderTexture rt, Texture2D tex, string file)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            float rad = Mathf.Max(b.extents.magnitude, .05f);
            cam.transform.position = b.center + new Vector3(.6f, .4f, -1f).normalized * (rad / Mathf.Sin(cam.fieldOfView * .5f * Mathf.Deg2Rad));
            cam.transform.LookAt(b.center);
            cam.Render(); cam.Render();
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, PX, PX), 0, 0); tex.Apply(); RenderTexture.active = null;
            File.WriteAllBytes(file, tex.EncodeToPNG());
        }
    }
}
#endif
