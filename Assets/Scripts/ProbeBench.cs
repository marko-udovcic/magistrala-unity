using UnityEngine;

namespace Magistrala
{
    /// FPS log (every 10 s to the log: Player.log in builds) and the command-line switch `-autopilot` (the truck drives the D8
    /// by itself: benchmark / demo). F1 toggles the autopilot? No: P (Autopilot). Esc quits a build.
    public class ProbeBench : MonoBehaviour
    {
        float acc; int frames; float worst; float t0;
        // hitches: frames over 25 / 50 ms in the window, the worst one, the trees' own Update (max ms) and GC collections;
        // every frame over 50 ms after the first 15 s also gets its own [spike] line (at most 20 per window)
        int over25, over50, spikeLines, gc0; float treeMax;
        // where the frame goes: FrameTimingManager (main / render thread / GPU, also in release players) and, in development
        // players, the main profiler markers (averaged over the 10 s window)
        static readonly string[] Markers = { "PlayerLoop", "FixedUpdate.PhysicsFixedUpdate", "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate",
            "Inl_UniversalRenderPipeline.RenderSingleCameraInternal", "UniversalRenderPipeline.RenderSingleCameraInternal", "CullScriptable", "Inl_MainLightShadow", "MainLightShadow", "Inl_DrawOpaqueObjects", "DrawOpaqueObjects",
            "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForGfxCommandsFromMainThread", "Gfx.WaitForRenderThread", "WaitForTargetFPS" };
        Unity.Profiling.ProfilerRecorder[] rec; double[] recSum;
        static readonly string[] Counters = { "Batches Count", "SetPass Calls Count", "Draw Calls Count", "Shadow Casters Count", "Triangles Count" };
        Unity.Profiling.ProfilerRecorder[] cnt; double[] cntSum; int cntN;
        readonly FrameTiming[] ft = new FrameTiming[1]; double ftMain, ftRender, ftGpu; int ftN;
        void OnEnable()
        {
            rec = new Unity.Profiling.ProfilerRecorder[Markers.Length]; recSum = new double[Markers.Length];
            for (int i = 0; i < Markers.Length; i++) rec[i] = Unity.Profiling.ProfilerRecorder.StartNew(new Unity.Profiling.ProfilerCategory("Any"), Markers[i]);
            cnt = new Unity.Profiling.ProfilerRecorder[Counters.Length]; cntSum = new double[Counters.Length];
            for (int i = 0; i < Counters.Length; i++) cnt[i] = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, Counters[i]);
        }
        void OnDisable() { if (rec != null) foreach (var r in rec) r.Dispose(); if (cnt != null) foreach (var r in cnt) r.Dispose(); }
        void Start()
        {
            t0 = Time.realtimeSinceStartup;
            foreach (var a in System.Environment.GetCommandLineArgs())
                {
                    if (a == "-autopilot") { var ap = Magistrala.Game.MagistralaGame.DrivenTruck()?.GetComponent<Autopilot>(); if (ap) ap.drive = true; }
                    if (a == "-mute") AudioListener.volume = 0; // measuring runs: no engine / radio on the speakers
                    if (a == "-hitchdbg" && !GetComponent<HitchDiag>()) gameObject.AddComponent<HitchDiag>(); // stutter frames: what they were made of
                }
            // measuring only: -skip trees,terrain,World/Buildings,shadows,... turns groups off to see what each one costs
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-skip")
                foreach (var w in args[i + 1].Split(','))
                {
                    if (w == "trees") foreach (var t in FindObjectsByType<TreeRenderer>(FindObjectsSortMode.None)) t.enabled = false;
                    else if (w == "terrain") foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None)) t.drawHeightmap = false;
                    else if (w == "grass") { RenderQuality.GrassOff = true; RenderQuality.ApplyGrass(); } // terrain details (grass, stones, maquis) not drawn
                    else if (w == "roadshadows" || w == "roadcast") // road / junction models: roadshadows = flat asphalt + paint out of the shadow maps, roadcast = no road model casts (upper bound)
                    {
                        var g = GameObject.Find("World/Roads"); var done = new System.Collections.Generic.HashSet<Material>();
                        if (g) foreach (var r in g.GetComponentsInChildren<MeshRenderer>())
                        {
                            if (w == "roadcast") { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; continue; }
                            foreach (var m in r.sharedMaterials) if (m && (m.name == "AsphaltPBR" || m.name == "Paint") && done.Add(m)) m.SetShaderPassEnabled("ShadowCaster", false);
                        }
                    }
                    else if (w == "windscreen") WindscreenFx.Off = true; // the cab's transparent windscreen quad
                    // the driven vehicle (applied in Update, the model loads late): cabin = every renderer, glass = its glass / lens
                    // materials, interior = the cab interior materials (v2_ / v3_ / i_ / M_), mv = no per-object motion vectors
                    else if (w == "cabin" || w == "glass" || w == "interior" || w == "mv") skipTruck.Add(w);
                    else if (w == "mblur") CameraFeel.NoBlur = true;
                    else if (w == "shadows") { foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.shadows = LightShadows.None; }
                    else { var g = GameObject.Find(w); if (g) g.SetActive(false); }
                    Debug.Log("[skip] " + w);
                }
        }
        // -kabinaab: GPU time, fps and counters per view (0 chase, 1 cab) since start, skipping 0.7 s after each switch
        // (GPU timings lag a few frames)
        readonly double[] abGpu = new double[2], abDt = new double[2], abTri = new double[2], abDc = new double[2];
        readonly int[] abGN = new int[2], abF = new int[2], abCN = new int[2];
        TruckChaseCamera abChase; bool abLast; float abSince;
        void AbSample(float dt, bool gotFt)
        {
            if (!abChase) abChase = FindFirstObjectByType<TruckChaseCamera>();
            bool cab = abChase && abChase.CabinView;
            if (cab != abLast) { abLast = cab; abSince = Time.realtimeSinceStartup; }
            if (Time.realtimeSinceStartup - abSince < .7f || Time.realtimeSinceStartup - t0 < 15f) return;
            int k = cab ? 1 : 0;
            abF[k]++; abDt[k] += dt;
            if (gotFt) { abGpu[k] += ft[0].gpuFrameTime; abGN[k]++; }
            if (cnt[2].Valid && cnt[2].LastValue > 0) { abDc[k] += cnt[2].LastValue; abTri[k] += cnt[4].LastValue; abCN[k]++; }
        }
        readonly System.Collections.Generic.HashSet<string> skipTruck = new System.Collections.Generic.HashSet<string>();
        float dbgT; readonly bool trafDbg = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-trafdbg") >= 0;
        void Update()
        {
            float dt = Time.unscaledDeltaTime; acc += dt; frames++; worst = Mathf.Max(worst, dt);
            if (skipTruck.Count > 0 && Time.frameCount % 60 == 0 && Magistrala.Game.MagistralaGame.DrivenTruck() is TruckController dv)
                foreach (var r in dv.GetComponentsInChildren<Renderer>(true))
                {
                    if (skipTruck.Contains("mv")) r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                    string mn = r.sharedMaterial ? r.sharedMaterial.name : "";
                    bool interior = mn.StartsWith("v3_") || mn.StartsWith("v2_") || mn.StartsWith("i_") || mn.StartsWith("M_");
                    if (skipTruck.Contains("cabin") || (skipTruck.Contains("glass") && (mn.StartsWith("glass") || mn.StartsWith("lens")))
                        || (skipTruck.Contains("interior") && interior)) r.forceRenderingOff = true;
                }
            if (dt > .025f) over25++;
            if (dt > .05f) { over50++; if (Time.realtimeSinceStartup - t0 > 15 && spikeLines++ < 20) { var tr0 = Magistrala.Game.MagistralaGame.DrivenTruck(); Debug.Log($"[spike] t={Time.realtimeSinceStartup - t0:F1}s {dt * 1000:F0}ms trees={TreeRenderer.LastMs:F1}ms truck x={(tr0 ? tr0.transform.position.x - 4200 : 0):F0} z={(tr0 ? tr0.transform.position.z : 0):F0}"); } }
            treeMax = Mathf.Max(treeMax, TreeRenderer.LastMs);
            for (int i = 0; i < rec.Length; i++) if (rec[i].Valid) recSum[i] += rec[i].LastValue * 1e-6;
            // render counters: LastValue read here is 0 or two frames summed every so often (the 10 / 20 M "alternating"
            // triangles of 22. 11.), so the window average of the frames whose draw-call count is not 0
            if (cnt[2].Valid && cnt[2].LastValue > 0) { for (int i = 0; i < cnt.Length; i++) if (cnt[i].Valid) cntSum[i] += cnt[i].LastValue; cntN++; }
            FrameTimingManager.CaptureFrameTimings();
            bool gotFt = FrameTimingManager.GetLatestTimings(1, ft) > 0;
            if (gotFt) { ftMain += ft[0].cpuMainThreadFrameTime; ftRender += ft[0].cpuRenderThreadFrameTime; ftGpu += ft[0].gpuFrameTime; ftN++; }
            if (TruckChaseCamera.AbSeconds > 0) AbSample(dt, gotFt);
            // -trafdbg: once a second the cars around the truck (TrafficSystem.DebugNear)
            dbgT += dt;
            if (dbgT >= 1f) { dbgT = 0; if (trafDbg) { var tsD = FindFirstObjectByType<Magistrala.Traffic.TrafficSystem>(); var trD = Magistrala.Game.MagistralaGame.DrivenTruck(); if (tsD && trD) Debug.Log($"[traf] t={Time.realtimeSinceStartup - t0:F0} x={trD.transform.position.x - 4200:F0} v={trD.SpeedKmh:F0}" + tsD.DebugNear()); } }
            if (acc >= 10f)
            {
                var tr = Magistrala.Game.MagistralaGame.DrivenTruck();
                Debug.Log($"[bench] t={Time.realtimeSinceStartup - t0:F0}s fps={frames / acc:F1} worst={worst * 1000:F0}ms res={Screen.width}x{Screen.height} q={RenderQuality.Label} w={WeatherSystem.Label} truck x={(tr ? tr.transform.position.x - 4200 : 0):F0} z={(tr ? -tr.transform.position.z - 5300 : 0):F0} y={(tr ? tr.transform.position.y : 0):F1} v={(tr ? tr.SpeedKmh : 0):F0}");
                var sb = new System.Text.StringBuilder("[perf]");
                if (ftN > 0) sb.Append($" main={ftMain / ftN:F1} render={ftRender / ftN:F1} gpu={ftGpu / ftN:F1}");
                for (int i = 0; i < rec.Length; i++) if (rec[i].Valid && recSum[i] > 0) sb.Append($" {Markers[i]}={recSum[i] / frames:F2}");
                for (int i = 0; i < cnt.Length; i++) if (cnt[i].Valid) sb.Append($" [{Counters[i].Replace(" Count", "")}={(cntN > 0 ? cntSum[i] / cntN : 0):F0}]");
                System.Array.Clear(cntSum, 0, cntSum.Length); cntN = 0;
                Debug.Log(sb.ToString());
                if (TruckChaseCamera.AbSeconds > 0)
                    Debug.Log($"[ab] t={Time.realtimeSinceStartup - t0:F0}s (since start) chase gpu={abGpu[0] / Mathf.Max(1, abGN[0]):F2} fps={abF[0] / Mathf.Max(1e-3f, (float)abDt[0]):F1} tri={abTri[0] / Mathf.Max(1, abCN[0]):F0} dc={abDc[0] / Mathf.Max(1, abCN[0]):F0}"
                        + $" | kab gpu={abGpu[1] / Mathf.Max(1, abGN[1]):F2} fps={abF[1] / Mathf.Max(1e-3f, (float)abDt[1]):F1} tri={abTri[1] / Mathf.Max(1, abCN[1]):F0} dc={abDc[1] / Mathf.Max(1, abCN[1]):F0}"
                        + $" | kab-chase gpu={abGpu[1] / Mathf.Max(1, abGN[1]) - abGpu[0] / Mathf.Max(1, abGN[0]):F2} ms");
                int gc = System.GC.CollectionCount(0);
                Debug.Log($"[hitch] t={Time.realtimeSinceStartup - t0:F0}s >25ms={over25} >50ms={over50} worst={worst * 1000:F0}ms trees max={treeMax:F2}ms avg={TreeRenderer.SumMs / Mathf.Max(1, frames):F2}ms gc={gc - gc0}");
                gc0 = gc; over25 = over50 = spikeLines = 0; treeMax = 0; TreeRenderer.SumMs = 0;
                System.Array.Clear(recSum, 0, recSum.Length); ftMain = ftRender = ftGpu = 0; ftN = 0;
                acc = 0; frames = 0; worst = 0;
            }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (!Application.isEditor && kb != null && kb.escapeKey.wasPressedThisFrame && !Magistrala.Game.WorldMap.EscTaken) Application.Quit(); // (Esc closes the map first)
        }
    }
}
