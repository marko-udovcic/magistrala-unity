// Before / after check of the parked-car bulk render (ParkedBake / ParkedCars): shots of the riva car park in Kaštel Stari
// (game −7900, −4625) from beside a car (~5 m), a 3/4 front view and mid range (~40 m), plus the parked cars' draw counts.
//   -executeMethod Magistrala.EditorTools.ParkedBulkShots.BatchBefore   the scene as it is, far mesh path (legacy) -> prije-*.png
//   -executeMethod Magistrala.EditorTools.ParkedBulkShots.BatchAfter    Build traffic + Rekviziti: postavi + save, then the shots
//                                                                       of the baked path (poslije-*) and the counts of both paths
//   [-out dir]  default ~/Documents/BalkanDriver/references/fotografije/unity-proba/parking-bulk
// The camera poses are stored next to the shots (poze.json) so both runs frame the same spot. Always ends in Magistrala.unity.
using System;
using System.IO;
using System.Linq;
using Magistrala.Traffic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class ParkedBulkShots
    {
        const string SCENE = "Assets/Scenes/Magistrala.unity";
        static readonly Vector3 RIVA = new Vector3(-7900 + 4200, 0, -(-4625 + 5300));   // game (−7900, −4625) in Unity

        [Serializable] class Pose { public string name; public Vector3 eye, look; public float fov; }
        [Serializable] class Poses { public Pose[] p; }

        static string Out()
        {
            var a = Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, "-out");
            string d = i >= 0 && i + 1 < a.Length ? a[i + 1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Documents/BalkanDriver/references/fotografije/unity-proba/parking-bulk");
            Directory.CreateDirectory(d); return d;
        }

        public static void BatchBefore()
        {
            try { EditorSceneManager.OpenScene(SCENE); Shoot("prije", true); }
            catch (Exception e) { Debug.LogError("[parkbulk] " + e); }
            EditorSceneManager.OpenScene(SCENE);
        }

        public static void BatchAfter()
        {
            try
            {
                EditorSceneManager.OpenScene(SCENE);
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-norebuild") < 0)
                {
                    TrafficBuilder.Build();
                    Debug.Log(PropsBuilder.Build());
                    EditorSceneManager.SaveOpenScenes();
                }
                Shoot("poslije", false);
                Shoot("poslije-legacy", true);
            }
            catch (Exception e) { Debug.LogError("[parkbulk] " + e); }
            EditorSceneManager.OpenScene(SCENE);
        }

        /// the AutiParking lot: every parkable kind as the traffic draws it (KandidatImporter.Spawn, lod1 / lod2 parts) next to the
        /// parked bake on the same level (0 / 1) — rows of 8, a shot per row and close pairs; ends in Magistrala.unity
        public static void BatchCompare()
        {
            try
            {
                var dir = Out();
                var scene = EditorSceneManager.OpenScene("Assets/Scenes/AutiParking.unity");
                // (loaded after the scene switch: opening a scene unloads unused assets and the data would be a destroyed object)
                var data = AssetDatabase.LoadAssetAtPath<TrafficData>("Assets/Generated/Traffic/TrafficData.asset");
                foreach (var g in scene.GetRootGameObjects()) if (g.name == "Auti" || g.name == "Linije") g.SetActive(false);
                var ks = Enumerable.Range(0, data.kinds.Length).Where(k => data.kinds[k].parked != null && data.kinds[k].parked.Length == 3).ToArray();
                var cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
                var pcGo = new GameObject("parked-test"); var pc = pcGo.AddComponent<ParkedCars>(); pc.data = data;
                for (int row = 0; row < 2; row++)
                {
                    int lod = row + 1; float z0 = row * 30;
                    var spawned = new System.Collections.Generic.List<GameObject>();
                    var cars = new System.Collections.Generic.List<Vector4>(); var kk = new System.Collections.Generic.List<int>(); var pp = new System.Collections.Generic.List<int>();
                    for (int i = 0; i < ks.Length; i++)
                    {
                        var kd = data.kinds[ks[i]]; int paint = (i * 3 + 1) % kd.paints.Length;
                        spawned.Add(KandidatImporter.Spawn(kd, paint, new Vector3(i * 3.4f, 0, z0), Quaternion.identity, lod, false));
                        cars.Add(new Vector4(i * 3.4f, 0, z0 + 8, 0)); kk.Add(ks[i]); pp.Add(paint);
                    }
                    pc.cars = cars.ToArray(); pc.kind = kk.ToArray(); pc.paint = pp.ToArray(); pc.drawDist = 1000;
                    ParkedCars.ForceLevel = row; ParkedCars.Legacy = false; ParkedCars.Off = false; pc.Init();
                    float cx = (ks.Length - 1) * 3.4f / 2;
                    void Snap(string name, Vector3 eye, Vector3 look, float fov)
                    {
                        cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye)); cam.fieldOfView = fov; cam.farClipPlane = 1000;
                        Render(cam, pc, Path.Combine(dir, $"usporedba-{name}.png"));
                        Debug.Log($"[parkbulk] {name}: parked drawn {pc.DrawnLastFrame}, calls {ParkedCars.CallsMain} + {ParkedCars.CallsShadow} shadow");
                    }
                    // front 3/4 of both rows: the traffic parts in front (z0), the bake behind (z0 + 8)
                    Snap($"lod{lod}-redovi", new Vector3(cx + 6, 6, z0 + 16 + 14), new Vector3(cx, .6f, z0 + 4), 55);
                    for (int i = 0; i < ks.Length; i++)
                    {
                        var kd = data.kinds[ks[i]]; float x = i * 3.4f;
                        Snap($"lod{lod}-{kd.name}-promet", new Vector3(x + 3.2f, 1.5f, z0 + 4.2f), new Vector3(x, .6f, z0 + .6f), 50);
                        Snap($"lod{lod}-{kd.name}-parkirani", new Vector3(x + 3.2f, 1.5f, z0 + 12.2f), new Vector3(x, .6f, z0 + 8.6f), 50);
                        if (row == 1) break;   // lod2: the first kind only (close-ups of the mid level)
                    }
                    foreach (var g in spawned) UnityEngine.Object.DestroyImmediate(g);
                }
                ParkedCars.ForceLevel = -1; UnityEngine.Object.DestroyImmediate(pcGo);
            }
            catch (Exception e) { Debug.LogError("[parkbulk] " + e); }
            ParkedCars.ForceLevel = -1;
            EditorSceneManager.OpenScene(SCENE);   // never leave the lot as the last scene
        }

        static Pose[] MakePoses(ParkedCars pc)
        {
            // the car park: the parked car with the most neighbours within 18 m, 350 m round the riva
            var cs = pc.cars; int best = -1, bestN = -1;
            for (int i = 0; i < cs.Length; i++)
            {
                var p = (Vector3)cs[i]; if (new Vector2(p.x - RIVA.x, p.z - RIVA.z).magnitude > 350) continue;
                int n = 0; for (int j = 0; j < cs.Length; j++) if (new Vector2(cs[j].x - p.x, cs[j].z - p.z).sqrMagnitude < 18 * 18) n++;
                if (n > bestN) { bestN = n; best = i; }
            }
            if (best < 0) throw new Exception("no parked cars near the riva");
            var ctr = Vector3.zero; int cn = 0;
            for (int j = 0; j < cs.Length; j++) if (new Vector2(cs[j].x - cs[best].x, cs[j].z - cs[best].z).sqrMagnitude < 18 * 18) { ctr += (Vector3)cs[j]; cn++; }
            ctr /= cn;
            bool Clear(Vector3 e) { for (int j = 0; j < cs.Length; j++) if (new Vector2(cs[j].x - e.x, cs[j].z - e.z).sqrMagnitude < 3.4f * 3.4f) return false; return !Physics.CheckBox(new Vector3(e.x, e.y - .6f, e.z), new Vector3(1.3f, .55f, 1.3f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore); }   // eye height over the ground: no car / wall / pole round it
            bool Sight(Vector3 e, Vector3 t) => !Physics.Linecast(e, t, ~0, QueryTriggerInteraction.Ignore) || Physics.Linecast(e, t, out var h, ~0, QueryTriggerInteraction.Ignore) && h.collider.transform.parent && h.collider.transform.parent.name == "kolajderi";
            float Ground(Vector3 p) => Physics.Raycast(new Vector3(p.x, p.y + 50, p.z), Vector3.down, out var h, 200, ~0, QueryTriggerInteraction.Ignore) ? h.point.y : p.y;
            // near: a car of the park with a free spot 5 m beside it (side) / 4.5 m off its front corner (3/4), nearest to the centre first
            Pose side = null, q34 = null;
            foreach (int i in Enumerable.Range(0, cs.Length).Where(i => new Vector2(cs[i].x - ctr.x, cs[i].z - ctr.z).sqrMagnitude < 40 * 40).OrderBy(i => new Vector2(cs[i].x - ctr.x, cs[i].z - ctr.z).sqrMagnitude))
            {
                var p = (Vector3)cs[i]; var rot = Quaternion.Euler(0, cs[i].w, 0); var f = rot * Vector3.forward; var r = rot * Vector3.right;
                var look = p + Vector3.up * .75f;
                if (side == null)
                    foreach (float sd in new[] { 1f, -1f })
                    {
                        var e = p + r * 5 * sd; e.y = Ground(e) + 1.6f;
                        if (Clear(e) && Sight(e, look)) { side = new Pose { name = "blizu-bok", eye = e, look = look, fov = 50 }; break; }
                    }
                if (q34 == null)
                    foreach (float sd in new[] { 1f, -1f })
                    {
                        var e = p + f * 3.6f + r * 2.7f * sd; e.y = Ground(e) + 1.3f;
                        if (Clear(e) && Sight(e, look)) { q34 = new Pose { name = "blizu-34", eye = e, look = p + f * .8f + Vector3.up * .6f, fov = 50 }; break; }
                    }
                if (side != null && q34 != null) break;
            }
            // mid: 40 m off the centre, 9 m up, the first of 12 directions with a clear view
            Pose mid = null; var lookC = ctr + Vector3.up * .8f;
            for (int a = 0; a < 12 && mid == null; a++)
            {
                var d = Quaternion.Euler(0, a * 30, 0) * Vector3.forward; var e = ctr + d * 40; e.y = Ground(e) + 9;
                if (Sight(e, lookC)) mid = new Pose { name = "srednje-40m", eye = e, look = lookC, fov = 45 };
            }
            if (mid == null) { var e = ctr + new Vector3(0, 25, -32); mid = new Pose { name = "srednje-40m", eye = e, look = lookC, fov = 45 }; }
            Debug.Log($"[parkbulk] car park at {ctr} ({cn} cars within 18 m of car {best})");
            return new[] { side, q34, mid }.Where(x => x != null).ToArray();
        }

        static void Shoot(string tag, bool legacy)
        {
            var dir = Out();
            var pc = UnityEngine.Object.FindFirstObjectByType<ParkedCars>();
            if (!pc) throw new Exception("no ParkedCars in the scene (Rekviziti: postavi)");
            string pf = Path.Combine(dir, "poze.json");
            Pose[] poses = File.Exists(pf) ? JsonUtility.FromJson<Poses>(File.ReadAllText(pf)).p : null;
            if (poses == null || poses.Length == 0) { poses = MakePoses(pc); File.WriteAllText(pf, JsonUtility.ToJson(new Poses { p = poses }, true)); }
            var cam = Camera.main ? Camera.main : UnityEngine.Object.FindFirstObjectByType<Camera>();
            var sb = new System.Text.StringBuilder($"# {tag} ({(legacy ? "far mesh (staro)" : "bake: jedan mesh + atlas po vrsti")}), {pc.cars.Length} parkiranih auta, vrste: " +
                string.Join(", ", pc.kind.Distinct().Select(k => pc.data.kinds[k].name + (pc.data.kinds[k].parked != null && pc.data.kinds[k].parked.Length == 3 ? "*" : ""))) + "\n");
            sb.AppendLine("| kadar | auta crtano | pozivi (vidljivi) | pozivi (sjene) | materijala | Unity draw calls / SetPass / batches / trokuti (cijeli kadar) | isto bez parkiranih |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var p in poses)
            {
                cam.transform.SetPositionAndRotation(p.eye, Quaternion.LookRotation(p.look - p.eye)); cam.fieldOfView = p.fov; cam.farClipPlane = Mathf.Max(cam.farClipPlane, 1500);
                ParkedCars.Legacy = legacy; ParkedCars.Off = false; pc.Init();
                string with = Render(cam, pc, Path.Combine(dir, $"{tag}-{p.name}.png"));
                int drawn = pc.DrawnLastFrame, cm = ParkedCars.CallsMain, cs = ParkedCars.CallsShadow, mt = ParkedCars.Materials;
                ParkedCars.Off = true;
                string without = Render(cam, pc, null);
                ParkedCars.Off = false;
                sb.AppendLine($"| {p.name} | {drawn} | {cm} | {cs} | {mt} | {with} | {without} |");
            }
            ParkedCars.Legacy = false;
            File.WriteAllText(Path.Combine(dir, $"brojke-{tag}.md"), sb.ToString());
            Debug.Log("[parkbulk]\n" + sb);
        }

        /// one frame (the parked draws submitted for this camera first); returns UnityStats of the frame
        static string Render(Camera cam, ParkedCars pc, string file)
        {
            const int W = 1600, H = 900;
            ShaderUtil.allowAsyncCompilation = false;   // a variant still compiling is skipped by RenderMeshInstanced
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.aspect = (float)W / H; cam.targetTexture = rt;
            pc.Draw(cam, cam); cam.Render();     // first frame warms up shaders / shadow maps
            pc.Draw(cam, cam); cam.Render();
            string stats = $"{UnityStats.drawCalls} / {UnityStats.setPassCalls} / {UnityStats.batches} / {UnityStats.triangles / 1000}k";
            if (file != null)
            {
                RenderTexture.active = rt;
                var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                File.WriteAllBytes(file, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
            }
            cam.targetTexture = null; RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(rt);
            return stats;
        }
    }
}
