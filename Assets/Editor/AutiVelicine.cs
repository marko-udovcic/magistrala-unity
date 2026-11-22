// Size check of the traffic cars against our own vehicles (Marko 2026-11-22: "usporedi velicine sa nasim kamionom"):
// measures every traffic kind (lod0, as TrafficSystem draws it: +Z forward), the scene's Atego ("Truck") and the Galeb
// (Resources/Vehicles/Galeb/galeb.glb), compares them with the real cars' dimensions and lines them up side by side on the
// AutiParking lot for two shots (3/4 front + top). Batch: -executeMethod Magistrala.EditorTools.AutiVelicine.Batch -out <dir>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Magistrala.Traffic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Magistrala.EditorTools
{
    public static class AutiVelicine
    {
        // real length × width (without mirrors) × height, m; null = invented car (class reference only)
        static readonly Dictionary<string, (float l, float w, float h, string src)> REAL = new Dictionary<string, (float, float, float, string)>
        {
            { "zastava", (3.83f, 1.59f, 1.38f, "Zastava 101") },
            { "yugo45", (3.49f, 1.55f, 1.39f, "Yugo 45") },
            { "golf2", (3.99f, 1.67f, 1.42f, "Golf II 3-vrata") },
            { "e46", (4.47f, 1.74f, 1.42f, "E46 limuzina") },
            { "w124", (4.74f, 1.74f, 1.45f, "W124 limuzina") },
            { "passat", (4.70f, 1.74f, 1.46f, "Passat B5.5 limuzina") },
            { "rcc_hatch", (4.00f, 1.75f, 1.45f, "klasa kompakt (Golf/Astra)") },
            { "rcc_sedan", (4.74f, 1.80f, 1.45f, "klasa srednja limuzina") },
            { "kombi_tn", (5.00f, 1.90f, 2.00f, "klasa stari dostavni kombi") },
            { "sprinter", (5.91f, 1.99f, 2.43f, "Sprinter W906 srednji (2,43 normalni / 2,70 visoki krov)") },
            { "sprinter_policija", (5.91f, 1.99f, 2.43f, "Sprinter W906 srednji") },
            { "sprinter_hitna", (5.91f, 1.99f, 2.43f, "Sprinter W906 srednji") },
            { "coach", (12.2f, 2.55f, 3.6f, "turistički autobus") },
            { "boxtruck", (6.9f, 2.2f, 3.25f, "kamion 7,5 t s sandukom") },
            { "Galeb", (5.91f, 1.99f, 2.43f, "Sprinter W906 srednji") },
            { "Atego", (7.30f, 2.45f, 3.30f, "Atego 818 s sandukom (~7,3 × 2,45 × 3,3)") },
        };

        static Bounds RB(GameObject g)
        {
            var rs = g.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(g.transform.position, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static void Strip(GameObject g)
        {
            // MonoBehaviours first (RequireComponent), then physics, keep transforms + mesh renderers
            foreach (var c in g.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in g.GetComponentsInChildren<Joint>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in g.GetComponentsInChildren<WheelCollider>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in g.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in g.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in g.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.DestroyImmediate(c);
            foreach (var c in g.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(c);
        }

        /// Build traffic + parked cars (props), save the scene, then the size check
        public static void BatchAll()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Magistrala.unity");
            TrafficBuilder.Build();
            Debug.Log(PropsBuilder.Build());
            EditorSceneManager.SaveOpenScenes();
            Batch();
        }

        public static void Batch()
        {
            var a = Environment.GetCommandLineArgs();
            int oi = Array.IndexOf(a, "-out");
            string outDir = oi >= 0 && oi + 1 < a.Length ? a[oi + 1] : "Temp/auti-velicine";
            Directory.CreateDirectory(outDir);
            var rows = new List<(string name, Vector3 size)>();
            var objs = new List<(string name, GameObject go)>();

            // the Atego: the scene's Truck, stripped to its renderers and kept as a temporary prefab (the lot is opened on its own:
            // a second open scene leaves URP's reflection-probe data of the big scene behind and the shots come out black)
            const string TMP = "Assets/Generated/velicine_atego.prefab";
            var main = EditorSceneManager.OpenScene("Assets/Scenes/Magistrala.unity", OpenSceneMode.Single);
            var truck = main.GetRootGameObjects().FirstOrDefault(g => g.name == "Truck");
            if (truck)
            {
                var cl = UnityEngine.Object.Instantiate(truck); cl.name = "Atego";
                Strip(cl);
                foreach (var c in cl.GetComponentsInChildren<ReflectionProbe>(true)) UnityEngine.Object.DestroyImmediate(c);
                cl.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                PrefabUtility.SaveAsPrefabAsset(cl, TMP); UnityEngine.Object.DestroyImmediate(cl);
            }
            else Debug.LogWarning("[velicine] no Truck in Magistrala.unity");
            var park = EditorSceneManager.OpenScene("Assets/Scenes/AutiParking.unity", OpenSceneMode.Single);
            GameObject atego = null;
            var ap = AssetDatabase.LoadAssetAtPath<GameObject>(TMP);
            if (ap) { atego = (GameObject)PrefabUtility.InstantiatePrefab(ap); PrefabUtility.UnpackPrefabInstance(atego, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction); atego.name = "Atego"; }

            // the Galeb as MagistralaGame builds it (glb under an unscaled root)
            var gsrc = Resources.Load<GameObject>("Vehicles/Galeb/galeb");
            GameObject galeb = null;
            if (gsrc) { galeb = new GameObject("Galeb"); UnityEngine.Object.Instantiate(gsrc, galeb.transform, false); }

            // hide the parked candidates and bay labels on the lot
            foreach (var g in park.GetRootGameObjects()) if (g.name == "Auti" || g.name == "Linije") g.SetActive(false);

            var data = AssetDatabase.LoadAssetAtPath<TrafficData>("Assets/Generated/Traffic/TrafficData.asset");
            if (atego) objs.Add(("Atego", atego));
            if (galeb) objs.Add(("Galeb", galeb));
            if (data)
                foreach (var kd in data.kinds.OrderByDescending(k => k.len))
                {
                    if (kd.name == "sprinter_hitna") continue; // same body as policija / civil
                    int paint = 0;
                    objs.Add((kd.name, KandidatImporter.Spawn(kd, paint, Vector3.zero, Quaternion.identity, 0, false)));
                }

            // measure (axis aligned: everything +Z forward at the origin, ground = bounds min y)
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("| vozilo | u igri D × Š × V (m) | stvarno D × Š × V (m) | razlika D / Š / V | referenca |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var (n, go) in objs)
            {
                var b = RB(go); var s = b.size;
                // Atego / Galeb models may face ±X: length = the longer horizontal axis
                bool alongX = s.x > s.z; float L = alongX ? s.x : s.z, W = alongX ? s.z : s.x;
                if (alongX) go.transform.rotation = Quaternion.Euler(0, 90, 0);
                rows.Add((n, new Vector3(W, s.y, L)));
                string real = "", diff = "", src = "";
                if (REAL.TryGetValue(n, out var r))
                {
                    real = $"{r.l:F2} × {r.w:F2} × {r.h:F2}";
                    diff = $"{(L / r.l - 1) * 100:+0;-0} % / {(W / r.w - 1) * 100:+0;-0} % / {(s.y / r.h - 1) * 100:+0;-0} %";
                    src = r.src;
                }
                sb.AppendLine($"| {n} | {L:F2} × {W:F2} × {s.y:F2} | {real} | {diff} | {src} |");
            }
            File.WriteAllText(Path.Combine(outDir, "mjere.md"), sb.ToString());
            Debug.Log("[velicine]\n" + sb);

            // line-up: side by side (gap 0.9 m), rears on one line z = 0, standing on the asphalt
            var asf = park.GetRootGameObjects().FirstOrDefault(g => g.name == "Asfalt");
            float x = asf ? RB(asf).min.x + 3f : 0;
            var labels = new List<GameObject>();
            foreach (var (n, go) in objs)
            {
                var b = RB(go);
                go.transform.position += new Vector3(x - b.min.x, -b.min.y + 0.0f, -b.min.z);
                b = RB(go);
                var t = new GameObject("Natpis_" + n).AddComponent<TextMesh>();
                t.text = $"{n}\n{b.size.z:F2} m"; t.fontSize = 64; t.characterSize = 0.07f; t.anchor = TextAnchor.UpperCenter; t.alignment = TextAlignment.Center;
                t.color = Color.white;
                t.transform.SetPositionAndRotation(new Vector3(b.center.x, 0.02f, -0.3f), Quaternion.Euler(90, 0, 0));
                labels.Add(t.gameObject);
                x = b.max.x + 0.9f;
            }
            var all = new Bounds(Vector3.zero, Vector3.zero); foreach (var (_, go) in objs) all.Encapsulate(RB(go));
            foreach (var l in labels) all.Encapsulate(l.transform.position);

            var cam = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).FirstOrDefault(c => c.gameObject.scene == park);
            if (!cam) cam = new GameObject("cam").AddComponent<Camera>();
            // 1) front 3/4, perspective, the whole row filling the frame
            cam.orthographic = false; cam.fieldOfView = 24; cam.farClipPlane = 500;
            var c0 = all.center;
            float hf = 2 * Mathf.Atan(Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad / 2) * 2.4f);
            float dist = all.size.x / 2 / Mathf.Tan(hf / 2) * 1.08f;
            var eye = new Vector3(c0.x, 3.2f, all.max.z + dist);
            cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(new Vector3(c0.x, 1.4f, all.max.z - 2) - eye));
            Shot(cam, Path.Combine(outDir, "velicine-sprijeda.png"), 2400, 1000);
            // 2) top, orthographic: lengths (rears aligned at z 0, labels at the rear)
            cam.orthographicSize = Mathf.Max(all.size.z, all.size.x / 2.4f) * 0.56f + 0.5f;
            eye = new Vector3(c0.x, 60f, c0.z); cam.orthographic = true;
            cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(90, 0, 0));
            cam.farClipPlane = 200;
            Shot(cam, Path.Combine(outDir, "velicine-odozgo.png"), 2400, 1000);
            AssetDatabase.DeleteAsset(TMP);
            // never leave the lot as the editor's last scene: the next rebuild / build would go to the wrong scene (main 675c6be)
            EditorSceneManager.OpenScene("Assets/Scenes/Magistrala.unity", OpenSceneMode.Single);
            Debug.Log("[velicine] -> " + outDir);
        }

        static void Shot(Camera cam, string file, int w, int h)
        {
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.aspect = (float)w / h; cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
