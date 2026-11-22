// AutiParking: separate scene (Assets/Scenes/AutiParking.unity) with the Sketchfab car candidates
// (Assets/Models/Vehicles/Kandidati/NN_ime.glb, CC BY 4.0, see BalkanDriver ~/Downloads/vozila-kandidati/popis.csv)
// parked on a flat lot, for looking at them before deciding what goes into traffic. Does not touch Magistrala.unity.
// Menu: Magistrala/Auti: parking kandidata. Batch: -executeMethod Magistrala.EditorTools.AutiParking.BatchAll
// (scene + screenshots + Mac/Win players in Builds/auti-parking).
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Magistrala.Kandidati;

namespace Magistrala.EditorTools
{
    public static class AutiParking
    {
        const string ModelDir = "Assets/Models/Vehicles/Kandidati";
        const string ScenePath = "Assets/Scenes/AutiParking.unity";
        const float BayW = 3.0f, BayD = 6.0f, Aisle = 8f;

        // real-world length (m) per file prefix, used to fix the scale of each model
        static readonly Dictionary<string, (string label, float len)> Info = new Dictionary<string, (string, float)>
        {
            { "01", ("BMW E46", 4.47f) }, { "02", ("Mercedes W124", 4.74f) }, { "03", ("Zastava 101", 3.83f) },
            { "04", ("Yugo 45", 3.49f) }, { "05", ("Golf 2", 3.99f) }, { "06", ("Compact Hatchback (RCC)", 4.0f) },
            { "07", ("Executive Sedan (RCC)", 4.74f) }, { "08", ("Dostavni kombi TN", 5.0f) },
            { "09", ("Mercedes Sprinter", 5.9f) }, { "10", ("VW Passat B5.5", 4.70f) },
        };

        [MenuItem("Magistrala/Auti: parking kandidata")]
        public static void BuildScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // light + sky like the game
            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Generated/Materials/SkyHDRI.mat");
            if (sky) RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            var sun = new GameObject("Sunce").AddComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 2.2f; sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft; sun.transform.rotation = Quaternion.Euler(48f, -35f, 0);
            RenderSettings.sun = sun;
            var vol = new GameObject("Volume").AddComponent<Volume>(); vol.isGlobal = true;
            vol.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/DefaultVolumeProfile.asset");

            // lot: asphalt + white bay lines
            var files = Directory.GetFiles(ModelDir, "*.glb").OrderBy(f => f).ToArray();
            int perRow = Mathf.CeilToInt(files.Length / 2f);
            float lotW = perRow * BayW + 8f, lotD = 2 * BayD + Aisle + 8f;
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.name = "Asfalt";
            ground.transform.localScale = new Vector3(lotW / 10f + 4, 1, lotD / 10f + 4);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var asphalt = new Material(lit) { color = new Color(0.62f, 0.62f, 0.62f) };
            var tile = new Vector2(ground.transform.localScale.x * 10f / 4f, ground.transform.localScale.z * 10f / 4f);
            var diff = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/PolyHaven/asphalt_02_diff.jpg");
            var nor = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/PolyHaven/asphalt_02_nor.jpg");
            if (diff) { asphalt.SetTexture("_BaseMap", diff); asphalt.SetTextureScale("_BaseMap", tile); }
            if (nor) { asphalt.SetTexture("_BumpMap", nor); asphalt.EnableKeyword("_NORMALMAP"); asphalt.SetFloat("_BumpScale", 0.6f); }
            asphalt.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(asphalt, "Assets/Models/Vehicles/Kandidati/ParkingAsfalt.mat");
            ground.GetComponent<Renderer>().sharedMaterial = asphalt;
            var paint = new Material(lit) { color = new Color(0.92f, 0.92f, 0.88f) };
            paint.SetFloat("_Smoothness", 0.2f);
            AssetDatabase.CreateAsset(paint, "Assets/Models/Vehicles/Kandidati/ParkingLinija.mat");
            var lines = new GameObject("Linije");
            float x0 = -perRow * BayW / 2f;
            for (int row = 0; row < 2; row++)
            {
                float zFront = row == 0 ? -Aisle / 2f : Aisle / 2f, zBack = row == 0 ? zFront - BayD : zFront + BayD;
                for (int i = 0; i <= perRow; i++) Line(lines.transform, paint, new Vector3(x0 + i * BayW, 0, (zFront + zBack) / 2f), new Vector3(0.12f, 0.01f, BayD));
                Line(lines.transform, paint, new Vector3(0, 0, zBack), new Vector3(perRow * BayW + 0.12f, 0.01f, 0.12f));
            }

            // cars
            var root = new GameObject("Auti");
            var cars = new List<Transform>(); var names = new List<string>();
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            for (int n = 0; n < files.Length; n++)
            {
                var path = files[n].Replace('\\', '/');
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                string key = Path.GetFileName(path).Substring(0, 2);
                var (label, len) = Info.TryGetValue(key, out var v) ? v : (Path.GetFileNameWithoutExtension(path), 4.3f);
                if (!prefab) { Debug.LogWarning("[parking] no prefab " + path); continue; }
                int row = n < perRow ? 0 : 1, col = row == 0 ? n : n - perRow;
                float cx = x0 + (col + 0.5f) * BayW, cz = row == 0 ? -Aisle / 2f - BayD / 2f : Aisle / 2f + BayD / 2f;

                var holder = new GameObject($"{key} {label}").transform; holder.SetParent(root.transform);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab); inst.transform.SetParent(holder, false);
                // presentation extras baked into some files (RCC: stone tiles under the car)
                foreach (var tr in inst.GetComponentsInChildren<Transform>(true))
                    if (System.Text.RegularExpressions.Regex.IsMatch(tr.name, "TILES|_ground|groundplane|Floor_", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) tr.gameObject.SetActive(false);
                OpaqueBody(inst, key);
                Upright(inst, key == "08" || key == "09");
                var b = Bounds(inst);
                float s = len / Mathf.Max(0.01f, b.size.z);
                inst.transform.localScale *= s; b = Bounds(inst);
                inst.transform.position += new Vector3(cx - b.center.x, -b.min.y, cz - b.center.z);
                b = Bounds(inst);
                int tris = inst.GetComponentsInChildren<MeshFilter>().Where(m => m.sharedMesh).Sum(m => (int)Enumerable.Range(0, m.sharedMesh.subMeshCount).Sum(i => (long)m.sharedMesh.GetIndexCount(i)) / 3)
                         + inst.GetComponentsInChildren<SkinnedMeshRenderer>().Where(m => m.sharedMesh).Sum(m => m.sharedMesh.triangles.Length / 3);
                Debug.Log($"[parking] {key} {label}: scale {s:F3}, size {b.size.x:F2}x{b.size.y:F2}x{b.size.z:F2} m, {tris} tris");

                // number + name painted on the asphalt in front of the bay
                var t = new GameObject("Natpis").AddComponent<TextMesh>(); t.transform.SetParent(holder);
                t.text = $"{n + 1}. {label}"; t.font = font; t.fontSize = 64; t.characterSize = 0.045f;
                t.anchor = TextAnchor.MiddleCenter; t.color = new Color(1f, 0.85f, 0.2f);
                t.GetComponent<MeshRenderer>().sharedMaterial = font.material;
                float zt = row == 0 ? -Aisle / 2f + 0.8f : Aisle / 2f - 0.8f;
                t.transform.SetPositionAndRotation(new Vector3(cx, 0.02f, zt), Quaternion.Euler(90, row == 0 ? 0 : 180, 0));
                cars.Add(inst.transform); names.Add(label);
            }

            var cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
            cam.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 2000f; cam.fieldOfView = 50;
            cam.transform.position = new Vector3(0, 9f, -lotD / 2f - 6f);
            cam.transform.LookAt(new Vector3(0, 0.5f, 0));
            var pc = cam.gameObject.AddComponent<ParkingCam>(); pc.cars = cars.ToArray(); pc.names = names.ToArray();

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[parking] scene saved {ScenePath}, {cars.Count} cars");
        }

        static void Line(Transform p, Material m, Vector3 c, Vector3 size)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(p); g.transform.position = c; g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = m; g.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // some files put the body on the glass's alpha-blended material (Zastava: Karoserija BLEND) → sorting holes in the paint.
        // Body parts (not named glass/staklo/window) get an opaque URP Lit copy with the same colour + normal maps.
        static void OpaqueBody(GameObject inst, string key)
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(r.name, "glass|staklo|window|prozor|lamp|light|svjetl", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m || m.renderQueue < 2900) continue;
                    if (!System.Text.RegularExpressions.Regex.IsMatch(m.name, "karoserija|body|paint", System.Text.RegularExpressions.RegexOptions.IgnoreCase)) continue;
                    var o = new Material(lit) { name = m.name + "_opaque" };
                    var bc = m.HasProperty("baseColorTexture") ? m.GetTexture("baseColorTexture") : m.mainTexture;
                    if (bc) o.SetTexture("_BaseMap", bc);
                    if (m.HasProperty("normalTexture") && m.GetTexture("normalTexture") is Texture n) { o.SetTexture("_BumpMap", n); o.EnableKeyword("_NORMALMAP"); }
                    o.SetFloat("_Metallic", 0.2f); o.SetFloat("_Smoothness", 0.55f);
                    AssetDatabase.CreateAsset(o, $"{ModelDir}/{key}_{m.name.Replace('/', '_')}_opaque.mat");
                    mats[i] = o; changed = true;
                    Debug.Log($"[parking] {key}: {r.name} material {m.name} → opaque");
                }
                if (changed) r.sharedMaterials = mats;
            }
        }

        // turn the model so its longest side runs along Z and its height is Y; height = the smaller of the two short sides
        // (cars are wider than tall), except tall vans (keepUp: only the length is fixed). Up sign from the wheels if named.
        static void Upright(GameObject inst, bool keepUp)
        {
            var b = Bounds(inst);
            Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };
            float[] ext = { b.size.x, b.size.y, b.size.z };
            int L = ext[0] >= ext[1] && ext[0] >= ext[2] ? 0 : ext[1] >= ext[2] ? 1 : 2;
            int H;
            if (keepUp) H = L == 1 ? 0 : 1;
            else { int a = (L + 1) % 3, c = (L + 2) % 3; H = ext[a] <= ext[c] ? a : c; }
            var up = ax[H];
            var wheels = inst.GetComponentsInChildren<Renderer>().Where(r => System.Text.RegularExpressions.Regex.IsMatch(
                r.name + " " + (r.transform.parent ? r.transform.parent.name : ""), "wheel|tyre|tire|rim", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                && !System.Text.RegularExpressions.Regex.IsMatch(r.name + " " + (r.transform.parent ? r.transform.parent.name : ""), "steer|trim", System.Text.RegularExpressions.RegexOptions.IgnoreCase)).ToArray();
            if (wheels.Length > 0)
            {
                var wc = Vector3.zero; foreach (var w in wheels) wc += w.bounds.center; wc /= wheels.Length;
                if (Vector3.Dot(b.center - wc, up) < 0) up = -up;
            }
            var rot = Quaternion.Inverse(Quaternion.LookRotation(ax[L], up));
            inst.transform.rotation = rot * inst.transform.rotation;
            Debug.Log($"[parking] {inst.name}: L={L} H={H} wheels={wheels.Length} up={up}");
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        // screenshots of the open AutiParking scene: overview + each car 3/4 front-ish
        public static void Shots(string outDir)
        {
            Directory.CreateDirectory(outDir);
            var cam = Object.FindFirstObjectByType<Camera>(); var pc = cam.GetComponent<ParkingCam>();
            var home = (cam.transform.position, cam.transform.rotation);
            Shot(cam, Path.Combine(outDir, "00-pregled.png"));
            cam.transform.SetPositionAndRotation(new Vector3(0, 1.7f, 0), Quaternion.Euler(8, 90, 0)); // eye height down the aisle
            cam.transform.position = new Vector3(-pc.cars.Length / 4f * BayW - 4f, 1.7f, 0);
            Shot(cam, Path.Combine(outDir, "00-prolaz.png"));
            for (int i = 0; i < pc.cars.Length; i++)
            {
                var car = pc.cars[i]; var b = Bounds(car.gameObject);
                bool row0 = b.center.z < 0;
                var dir = new Vector3(0.75f, 0.42f, row0 ? 1f : -1f).normalized;
                var pos = b.center + dir * (b.extents.magnitude * 2.3f);
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(b.center - pos));
                Shot(cam, Path.Combine(outDir, $"{i + 1:00}-{pc.names[i].Replace(' ', '-').Replace("(", "").Replace(")", "")}.png"));
            }
            cam.transform.SetPositionAndRotation(home.Item1, home.Item2);
        }

        static void Shot(Camera cam, string file)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }

        static string BuildPlayer(BuildTarget target, string path)
        {
            string prod = PlayerSettings.productName;
            var fs = PlayerSettings.fullScreenMode;
            try
            {
                PlayerSettings.productName = "Magistrala auti parking";
                PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
                var r = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath }, locationPathName = path, target = target, targetGroup = BuildTargetGroup.Standalone, options = BuildOptions.CompressWithLz4HC });
                return $"{target}: {r.summary.result} {r.summary.totalSize / 1048576f:F0} MB, errors {r.summary.totalErrors}";
            }
            finally { PlayerSettings.productName = prod; PlayerSettings.fullScreenMode = fs; AssetDatabase.SaveAssets(); }
        }

        // Unity -projectPath … -executeMethod Magistrala.EditorTools.AutiParking.OpenAndPlay  (GUI editor: open the lot, press Play)
        public static void OpenAndPlay()
        {
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.delayCall += () => EditorApplication.EnterPlaymode();
        }

        public static void BatchShots()
        {
            BuildScene();
            Shots(System.Environment.GetEnvironmentVariable("PARKING_SHOTS") ?? "Builds/auti-parking/slike");

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Magistrala.unity"); // never leave the lot as the last scene (main 675c6be)
        }

        public static void BatchPlayers()
        {
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log("[parking] " + BuildPlayer(BuildTarget.StandaloneOSX, "Builds/auti-parking/mac/AutiParking.app"));
            Debug.Log("[parking] " + BuildPlayer(BuildTarget.StandaloneWindows64, "Builds/auti-parking/win/AutiParking.exe"));

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Magistrala.unity"); // never leave the lot as the last scene (main 675c6be)
        }

        public static void BatchAll()
        {
            BuildScene();
            Shots(System.Environment.GetEnvironmentVariable("PARKING_SHOTS") ?? "Builds/auti-parking/slike");
            Debug.Log("[parking] " + BuildPlayer(BuildTarget.StandaloneOSX, "Builds/auti-parking/mac/AutiParking.app"));
            Debug.Log("[parking] " + BuildPlayer(BuildTarget.StandaloneWindows64, "Builds/auti-parking/win/AutiParking.exe"));

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Magistrala.unity"); // never leave the lot as the last scene (main 675c6be)
        }
    }
}
