// Lighting / post / camera / truck setup of the probe scene (sunny Mediterranean midday-afternoon, "GTA V daytime" look).
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Magistrala.EditorTools
{
    public static class SceneSetup
    {
        [MenuItem("Magistrala/Setup lighting + post")]
        public static void Lighting()
        {
            // URP asset: 300 m cascaded shadows, 4096 map, soft shadows high, HDR, no MSAA (TAA on the camera)
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/PC_RPAsset.asset");
            var so = new SerializedObject(rp);
            so.FindProperty("m_ShadowDistance").floatValue = 300;
            so.FindProperty("m_ShadowCascadeCount").intValue = 4;
            so.FindProperty("m_Cascade4Split").vector3Value = new Vector3(.05f, .14f, .38f);
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
            so.FindProperty("m_SoftShadowsSupported").boolValue = true;
            so.FindProperty("m_SoftShadowQuality").intValue = 3;
            so.FindProperty("m_MSAA").intValue = 1;
            so.FindProperty("m_SupportsHDR").boolValue = true;
            so.FindProperty("m_SupportsTerrainHoles").boolValue = true;
            var depth = so.FindProperty("m_RequireDepthTexture"); if (depth != null) depth.boolValue = true;
            var cb = so.FindProperty("m_ShadowDepthBias"); if (cb != null) cb.floatValue = 1.0f;
            var nb = so.FindProperty("m_ShadowNormalBias"); if (nb != null) nb.floatValue = 0.6f;
            so.ApplyModifiedProperties();
            for (int q = 0; q < QualitySettings.names.Length; q++) { QualitySettings.SetQualityLevel(q, false); QualitySettings.renderPipeline = rp; }
            QualitySettings.SetQualityLevel(QualitySettings.names.ToList().IndexOf("PC"), true);
            GraphicsSettings.defaultRenderPipeline = rp;
            QualitySettings.vSyncCount = 0;
            QualitySettings.lodBias = 2;

            // SSAO on the PC renderer
            var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/PC_Renderer.asset");
            foreach (var f in rd.rendererFeatures)
                if (f != null && f.GetType().Name == "ScreenSpaceAmbientOcclusion")
                {
                    f.SetActive(true);
                    var fs = new SerializedObject(f); var st = fs.FindProperty("m_Settings");
                    void Set(string n, float v) { var p = st.FindPropertyRelative(n); if (p != null) { if (p.propertyType == SerializedPropertyType.Float) p.floatValue = v; else if (p.propertyType == SerializedPropertyType.Integer || p.propertyType == SerializedPropertyType.Enum) p.intValue = (int)v; else if (p.propertyType == SerializedPropertyType.Boolean) p.boolValue = v > 0; } }
                    Set("Intensity", 1.1f); Set("Radius", .6f); Set("DirectLightingStrength", .2f); Set("Falloff", 120f);
                    fs.ApplyModifiedProperties();
                }

            // sky
            var sky = AssetDatabase.LoadAssetAtPath<Material>(WorldBuilder.GEN + "/Materials/Sky.mat");
            if (sky == null) { sky = new Material(Shader.Find("Skybox/Procedural")); AssetDatabase.CreateAsset(sky, WorldBuilder.GEN + "/Materials/Sky.mat"); }
            sky.SetFloat("_SunDisk", 2); sky.SetFloat("_SunSize", .03f); sky.SetFloat("_SunSizeConvergence", 8);
            sky.SetFloat("_AtmosphereThickness", .8f); sky.SetColor("_SkyTint", new Color(.42f, .54f, .72f));
            sky.SetColor("_GroundColor", new Color(.42f, .45f, .5f)); sky.SetFloat("_Exposure", 1.25f);
            RenderSettings.skybox = sky;

            // sun
            var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
            if (!sun) { sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional; }
            sun.name = "Sun";
            sun.transform.rotation = Quaternion.Euler(52, 28, 0);
            sun.color = new Color(1f, .955f, .88f); sun.intensity = 1.8f;
            sun.shadows = LightShadows.Soft; sun.shadowStrength = .92f;
            var lad = sun.GetComponent<UniversalAdditionalLightData>() ?? sun.gameObject.AddComponent<UniversalAdditionalLightData>();
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Skybox; RenderSettings.ambientIntensity = 1.0f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox; RenderSettings.reflectionIntensity = 1f;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = .00011f;
            RenderSettings.fogColor = new Color(.66f, .76f, .88f);
            DynamicGI.UpdateEnvironment();

            // post volume
            var vol = Object.FindFirstObjectByType<Volume>();
            if (!vol) { vol = new GameObject("Post").AddComponent<Volume>(); }
            vol.isGlobal = true; vol.priority = 1;
            string pp = WorldBuilder.GEN + "/Post.asset";
            var prof = AssetDatabase.LoadAssetAtPath<VolumeProfile>(pp);
            if (prof == null) { prof = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(prof, pp); }
            T Get<T>() where T : VolumeComponent { if (!prof.TryGet<T>(out var c)) { c = prof.Add<T>(true); AssetDatabase.AddObjectToAsset(c, prof); } c.active = true; return c; }
            var tm = Get<Tonemapping>(); tm.mode.Override(TonemappingMode.ACES);
            var bl = Get<Bloom>(); bl.threshold.Override(1.1f); bl.intensity.Override(.18f); bl.scatter.Override(.55f); bl.highQualityFiltering.Override(true);
            var ca = Get<ColorAdjustments>(); ca.postExposure.Override(.15f); ca.contrast.Override(12f); ca.saturation.Override(6f);
            var wb = Get<WhiteBalance>(); wb.temperature.Override(-4f); wb.tint.Override(0f);
            var smh = Get<ShadowsMidtonesHighlights>(); smh.shadows.Override(new Vector4(.97f, .99f, 1.04f, 0)); smh.highlights.Override(new Vector4(1.02f, 1.0f, .97f, 0));
            var vg = Get<Vignette>(); vg.intensity.Override(.16f); vg.smoothness.Override(.4f);
            EditorUtility.SetDirty(prof);
            vol.sharedProfile = prof;

            // camera
            var cam = Camera.main ?? Object.FindFirstObjectByType<Camera>();
            cam.farClipPlane = 9000; cam.nearClipPlane = .25f; cam.fieldOfView = 55;
            var cd = cam.GetUniversalAdditionalCameraData();
            cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.TemporalAntiAliasing; cd.antialiasingQuality = AntialiasingQuality.High;
            cd.renderShadows = true;
            if (!cam.GetComponent<Magistrala.EnvironmentInit>()) cam.gameObject.AddComponent<Magistrala.EnvironmentInit>();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        /// put the scene camera / main camera at a game-frame point looking at another (screenshots)
        public static void Look(double gx, double gy, double gz, double tx, double ty, double tz)
        {
            var cam = Camera.main;
            cam.transform.position = W.U(gx, gy, gz);
            cam.transform.LookAt(W.U(tx, ty, tz));
        }
    }
}

namespace Magistrala.EditorTools
{
    public static class TruckSetup
    {
        /// the eastbound D8 lane path from Lukšić (model 01) on, chained from the models' road axes (right lane centre), game x, z
        public static System.Collections.Generic.List<Vector2> EastboundPath(bool westbound = false)
        {
            var models = WorldData.Models();
            var roads = new System.Collections.Generic.List<(System.Collections.Generic.List<double[]> p, double w, int n)>();
            foreach (var m in models) foreach (var r in m.roads) if (r.n >= 2 && r.lvl == 0 && r.p.Count >= 2) roads.Add((r.p, r.w, r.n));
            var m01 = models.Find(m => m.key == "krizanje-01");
            var r0 = westbound ? m01.roads[2] : m01.roads[0]; (System.Collections.Generic.List<double[]> p, double w, int n) cur = (r0.p, r0.w, r0.n);
            var outL = new System.Collections.Generic.List<Vector2>();
            var used = new System.Collections.Generic.HashSet<object>();
            double prevOff = double.NaN;
            for (int guard = 0; guard < 200; guard++)
            {
                used.Add(cur.p);
                // A model road is one carriageway (its axis = the middle of the asphalt, w = whole width, n = lanes of that carriageway).
                // The middle of the rightmost lane is w/2 - w/(2n) from the axis (n = 2: w/4; n = 3: w/3; n = 4: 3w/8; one-way n = 1: the axis).
                // The old fixed w/4 sat 0.9 m inside the right lane of a 3-lane carriageway (and 1.6 m on 4 lanes).
                double laneOff = cur.w / 2 * (1 - 1.0 / System.Math.Max(1, cur.n)), arc = 0, lastOff = prevOff;
                if (double.IsNaN(prevOff)) prevOff = lastOff = laneOff;
                for (int i = 0; i < cur.p.Count - 1; i++)
                {
                    var a = cur.p[i]; var b = cur.p[i + 1]; double dx = b[0] - a[0], dz = b[1] - a[1], L = System.Math.Sqrt(dx * dx + dz * dz); if (L < .05) continue;
                    // where a lane is added / dropped between two chained roads, slide to the new lane centre over 40 m (smoothstep)
                    double f0 = System.Math.Min(1, arc / 40), f1 = System.Math.Min(1, (arc + L) / 40);
                    double off0 = prevOff + (laneOff - prevOff) * (f0 * f0 * (3 - 2 * f0)), off1 = prevOff + (laneOff - prevOff) * (f1 * f1 * (3 - 2 * f1));
                    double rx = -dz / L, rz = dx / L;
                    outL.Add(new Vector2((float)(a[0] + rx * off0), (float)(a[1] + rz * off0)));
                    if (i == cur.p.Count - 2) outL.Add(new Vector2((float)(b[0] + rx * off1), (float)(b[1] + rz * off1)));
                    arc += L; lastOff = off1;
                }
                prevOff = lastOff;
                var e = cur.p[cur.p.Count - 1]; var e0 = cur.p[cur.p.Count - 2];
                double ddx = e[0] - e0[0], ddz = e[1] - e0[1], dl = System.Math.Sqrt(ddx * ddx + ddz * ddz);
                (System.Collections.Generic.List<double[]> p, double w, int n) next = default; double best = 6; int from = 0;
                foreach (var r in roads)
                {
                    if (used.Contains(r.p)) continue;
                    for (int i = 0; i + 1 < r.p.Count; i++)
                    {
                        var s = r.p[i]; var s1 = r.p[i + 1];
                        double qx = s1[0] - s[0], qz = s1[1] - s[1], ql2 = qx * qx + qz * qz; if (ql2 < 1e-4) continue;
                        double t = System.Math.Max(0, System.Math.Min(1, ((e[0] - s[0]) * qx + (e[1] - s[1]) * qz) / ql2));
                        double px = s[0] + qx * t - e[0], pz = s[1] + qz * t - e[1], d = System.Math.Sqrt(px * px + pz * pz);
                        double cos = (qx * ddx + qz * ddz) / (System.Math.Sqrt(ql2) * dl + 1e-9);
                        if (d < best && cos > .85) { best = d; next = r; from = i + 1; }
                    }
                }
                if (next.p != null) { used.Add(next.p); var rest = next.p.GetRange(from, next.p.Count - from); rest.Insert(0, e); next = (rest, next.w, next.n); }
                if (next.p != null && next.p.Count < 2) { used.Add(next.p); break; }
                if (next.p == null) break;
                cur = next;
            }
            return outL;
        }

        /// the lane path in Unity coordinates, heights from the models' asphalt (where a point falls outside the mesh — a paint gap, a join —
        /// the height is interpolated from the neighbours; it used to be 0, a 44 m cliff in the path the autopilot then tried to steer to)
        public static Vector3[] UnityPath(System.Collections.Generic.List<Vector2> path)
        {
            var models = WorldData.Models();
            var ys = new double[path.Count];
            for (int i = 0; i < path.Count; i++) { ys[i] = double.NaN; foreach (var m in models) { ys[i] = m.Surface(path[i].x, path[i].y); if (!double.IsNaN(ys[i])) break; } }
            for (int i = 0; i < ys.Length; i++)
            {
                if (!double.IsNaN(ys[i])) continue;
                int a = i - 1; while (a >= 0 && double.IsNaN(ys[a])) a--;
                int b = i + 1; while (b < ys.Length && double.IsNaN(ys[b])) b++;
                if (a >= 0 && b < ys.Length) { double t = (double)(i - a) / (b - a); ys[i] = ys[a] + (ys[b] - ys[a]) * t; }
                else ys[i] = a >= 0 ? ys[a] : b < ys.Length ? ys[b] : 0;
            }
            var u = new Vector3[path.Count];
            for (int i = 0; i < path.Count; i++) u[i] = W.U(path[i].x, ys[i], path[i].y);
            return u;
        }

        /// re-derive the autopilot's lane path on the existing truck (after the lane-centre fix), without respawning it
        [MenuItem("Magistrala/Autopilot: osvježi stazu")]
        public static void RefreshAutopilotPath()
        {
            var ap = Object.FindFirstObjectByType<Magistrala.Autopilot>(); if (!ap) { Debug.LogError("[Magistrala] no truck with an Autopilot in the scene (Magistrala/Spawn truck first)"); return; }
            var path = EastboundPath(); ap.path = UnityPath(path);
            EditorUtility.SetDirty(ap); EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Magistrala] autopilot path: {path.Count} pts, game x {path[0].x:F0} .. {path[path.Count - 1].x:F0}");
        }

        [MenuItem("Magistrala/Spawn truck")]
        public static void Spawn()
        {
            var old = GameObject.Find("Truck"); if (old) Object.DestroyImmediate(old);
            var path = EastboundPath();
            var upath = UnityPath(path);
            int si = 2;
            var root = new GameObject("Truck");
            var p0 = upath[si]; var dir = upath[si + 1] - upath[si]; dir.y = 0;
            root.transform.position = p0 + Vector3.up * 1.0f;
            root.transform.rotation = Quaternion.LookRotation(dir.normalized);
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Vehicles/firm_truck.glb");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(src);
            model.name = "Model"; model.transform.SetParent(root.transform, false);
            root.AddComponent<Rigidbody>();
            root.AddComponent<TruckController>();
            var ap = root.AddComponent<Magistrala.Autopilot>(); ap.path = upath;
            var cam = Camera.main;
            var cc = cam.GetComponent<TruckChaseCamera>() ?? cam.gameObject.AddComponent<TruckChaseCamera>(); cc.target = root.GetComponent<TruckController>();
            if (!cam.GetComponent<Magistrala.ProbeBench>()) cam.gameObject.AddComponent<Magistrala.ProbeBench>();
            var hud = cam.GetComponent<TruckHud>() ?? cam.gameObject.AddComponent<TruckHud>(); hud.target = cc.target;
            cam.transform.position = root.transform.TransformPoint(new Vector3(0, 4, -12)); cam.transform.LookAt(root.transform.position + Vector3.up * 2);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Magistrala] truck at {p0}, path {upath.Length} pts, from game x {path[0].x:F0} to {path[path.Count - 1].x:F0}");
        }
    }
}
