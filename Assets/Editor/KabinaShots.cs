// Cab interior shots without playing: the scene's Atego ("Truck") and the Galeb (Resources/Vehicles/Galeb/galeb.glb) from the
// driver's eye, as TruckChaseCamera places it in the cab view (eye = model.TransformPoint(spec.eyeModel) after the model yaw).
// Batch: -executeMethod Magistrala.EditorTools.KabinaShots.Batch -out <dir>  (reopens Magistrala.unity unchanged at the end)
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class KabinaShots
    {
        /// the cab view's look-down (TruckChaseCamera: headPitch + CabPitch)
        const float PITCH = TruckChaseCamera.CabPitch;
        public static void Batch()
        {
            var a = Environment.GetCommandLineArgs();
            int oi = Array.IndexOf(a, "-out");
            string outDir = oi >= 0 && oi + 1 < a.Length ? a[oi + 1] : "Temp/kabina";
            Directory.CreateDirectory(outDir);
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Magistrala.unity", OpenSceneMode.Single);
            var cam = new GameObject("kabina_cam").AddComponent<Camera>();
            cam.nearClipPlane = 0.05f; cam.farClipPlane = 2000;

            // Atego: the scene's truck model, turned like TruckController.Awake does (modelYawDegrees 180)
            var truck = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Truck");
            if (truck)
            {
                var model = truck.GetComponentsInChildren<Renderer>()[0].transform;
                while (model.parent && model.parent != truck.transform) model = model.parent;
                model.localRotation = Quaternion.AngleAxis(180, Vector3.up) * model.localRotation;
                var eyeA = V(a, "-ategoeye", new Vector3(0.55f, 1.3f, -2.75f));
                // the side mirrors as in the cab view (MirrorCams, rendered once from the eye)
                var tcA = truck.GetComponent<TruckController>();
                if (tcA) { var mc = truck.AddComponent<MirrorCams>(); mc.Bind(tcA, model); mc.RenderNow(model.TransformPoint(eyeA)); }
                Shoot(cam, truck.transform, model, eyeA, outDir, "atego");
            }
            // Galeb: the van as MagistralaGame builds it (glb under an unscaled root, modelYawDegrees 0), next to the truck
            var src = Resources.Load<GameObject>("Vehicles/Galeb/galeb");
            if (src && truck)
            {
                var root = new GameObject("Galeb_shot");
                root.transform.SetPositionAndRotation(truck.transform.position + truck.transform.right * 6f, truck.transform.rotation);
                var m = UnityEngine.Object.Instantiate(src, root.transform, false);
                Shoot(cam, root.transform, m.transform, V(a, "-galebeye", new Vector3(-0.42f, 0.58f, 0.65f)), outDir, "galeb");
            }
            EditorSceneManager.OpenScene("Assets/Scenes/Magistrala.unity", OpenSceneMode.Single); // discard, main scene stays the last one
            Debug.Log("[kabina] shots -> " + outDir);
        }

        /// -ategoeye x,y,z / -galebeye x,y,z: try another eye (model frame, as spec.eyeModel)
        static Vector3 V(string[] a, string key, Vector3 def)
        {
            int i = Array.IndexOf(a, key); if (i < 0 || i + 1 >= a.Length) return def;
            var p = a[i + 1].Split(','); var c = System.Globalization.CultureInfo.InvariantCulture;
            return new Vector3(float.Parse(p[0], c), float.Parse(p[1], c), float.Parse(p[2], c));
        }

        static void Shoot(Camera cam, Transform root, Transform model, Vector3 eyeModel, string dir, string name)
        {
            var eye = model.TransformPoint(eyeModel);
            float aspect = 16f / 9f;
            cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(Mathf.Tan(90f * Mathf.Deg2Rad * 0.5f) / aspect) * Mathf.Rad2Deg, 40f, 85f);
            (string tag, float yaw, float pitch)[] views = { ("naprijed", 0, PITCH), ("ploca", 0, 22f), ("lijevo", -60, 5), ("desno", 55, 12) };
            foreach (var v in views)
            {
                cam.transform.SetPositionAndRotation(eye, root.rotation * Quaternion.Euler(v.pitch, v.yaw, 0));
                Shot(cam, Path.Combine(dir, $"{name}-{v.tag}.png"));
            }
            // the outside mirrors, looked at straight from the eye (narrower view)
            foreach (var g in model.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name == "mglass_L" || r.name == "mglass_R"))
            {
                cam.fieldOfView = 30;
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(g.bounds.center - eye, root.up));
                Shot(cam, Path.Combine(dir, $"{name}-retrovizor-{(g.name.EndsWith("L") ? "lijevo" : "desno")}.png"));
            }
            Debug.Log($"[kabina] {name}: eye {eye}");
        }

        static void Shot(Camera cam, string file)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.aspect = 16f / 9f; cam.targetTexture = rt; cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
