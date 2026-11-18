using UnityEngine;

namespace Magistrala
{
    /// FPS log (every 10 s to the log: Player.log in builds) and the command-line switch `-autopilot` (the truck drives the D8
    /// by itself: benchmark / demo). F1 toggles the autopilot? No: P (Autopilot). Esc quits a build.
    public class ProbeBench : MonoBehaviour
    {
        float acc; int frames; float worst; float t0;
        // where the frame goes: FrameTimingManager (main / render thread / GPU, also in release players) and, in development
        // players, the main profiler markers (averaged over the 10 s window)
        static readonly string[] Markers = { "PlayerLoop", "FixedUpdate.PhysicsFixedUpdate", "Update.ScriptRunBehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate",
            "Inl_UniversalRenderPipeline.RenderSingleCameraInternal", "UniversalRenderPipeline.RenderSingleCameraInternal", "CullScriptable", "Inl_MainLightShadow", "MainLightShadow", "Inl_DrawOpaqueObjects", "DrawOpaqueObjects",
            "Gfx.WaitForPresentOnGfxThread", "Gfx.WaitForGfxCommandsFromMainThread", "Gfx.WaitForRenderThread", "WaitForTargetFPS" };
        Unity.Profiling.ProfilerRecorder[] rec; double[] recSum;
        static readonly string[] Counters = { "Batches Count", "SetPass Calls Count", "Draw Calls Count", "Shadow Casters Count", "Triangles Count" };
        Unity.Profiling.ProfilerRecorder[] cnt;
        readonly FrameTiming[] ft = new FrameTiming[1]; double ftMain, ftRender, ftGpu; int ftN;
        void OnEnable()
        {
            rec = new Unity.Profiling.ProfilerRecorder[Markers.Length]; recSum = new double[Markers.Length];
            for (int i = 0; i < Markers.Length; i++) rec[i] = Unity.Profiling.ProfilerRecorder.StartNew(new Unity.Profiling.ProfilerCategory("Any"), Markers[i]);
            cnt = new Unity.Profiling.ProfilerRecorder[Counters.Length];
            for (int i = 0; i < Counters.Length; i++) cnt[i] = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Render, Counters[i]);
        }
        void OnDisable() { if (rec != null) foreach (var r in rec) r.Dispose(); if (cnt != null) foreach (var r in cnt) r.Dispose(); }
        void Start()
        {
            t0 = Time.realtimeSinceStartup;
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a == "-autopilot") { var ap = FindFirstObjectByType<Autopilot>(); if (ap) ap.drive = true; }
            // measuring only: -skip trees,terrain,World/Buildings,shadows,... turns groups off to see what each one costs
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-skip")
                foreach (var w in args[i + 1].Split(','))
                {
                    if (w == "trees") foreach (var t in FindObjectsByType<TreeRenderer>(FindObjectsSortMode.None)) t.enabled = false;
                    else if (w == "terrain") foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None)) t.drawHeightmap = false;
                    else if (w == "shadows") { foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) l.shadows = LightShadows.None; }
                    else { var g = GameObject.Find(w); if (g) g.SetActive(false); }
                    Debug.Log("[skip] " + w);
                }
        }
        void Update()
        {
            float dt = Time.unscaledDeltaTime; acc += dt; frames++; worst = Mathf.Max(worst, dt);
            for (int i = 0; i < rec.Length; i++) if (rec[i].Valid) recSum[i] += rec[i].LastValue * 1e-6;
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, ft) > 0) { ftMain += ft[0].cpuMainThreadFrameTime; ftRender += ft[0].cpuRenderThreadFrameTime; ftGpu += ft[0].gpuFrameTime; ftN++; }
            if (acc >= 10f)
            {
                var tr = FindFirstObjectByType<TruckController>();
                Debug.Log($"[bench] t={Time.realtimeSinceStartup - t0:F0}s fps={frames / acc:F1} worst={worst * 1000:F0}ms res={Screen.width}x{Screen.height} q={RenderQuality.Label} truck x={(tr ? tr.transform.position.x - 4200 : 0):F0} v={(tr ? tr.SpeedKmh : 0):F0}");
                var sb = new System.Text.StringBuilder("[perf]");
                if (ftN > 0) sb.Append($" main={ftMain / ftN:F1} render={ftRender / ftN:F1} gpu={ftGpu / ftN:F1}");
                for (int i = 0; i < rec.Length; i++) if (rec[i].Valid && recSum[i] > 0) sb.Append($" {Markers[i]}={recSum[i] / frames:F2}");
                for (int i = 0; i < cnt.Length; i++) if (cnt[i].Valid) sb.Append($" [{Counters[i].Replace(" Count", "")}={cnt[i].LastValue}]");
                Debug.Log(sb.ToString());
                System.Array.Clear(recSum, 0, recSum.Length); ftMain = ftRender = ftGpu = 0; ftN = 0;
                acc = 0; frames = 0; worst = 0;
            }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (!Application.isEditor && kb != null && kb.escapeKey.wasPressedThisFrame) Application.Quit();
        }
    }
}
