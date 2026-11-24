using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Magistrala
{
    /// First-use shader / pipeline-state compiles are stutters (Metal builds a PSO the first time a shader variant meets a new
    /// render state: the first rain, the first car of a kind, the map, a new junction model…). This warms them up while the game
    /// loads from a GraphicsStateCollection recorded in earlier runs:
    ///   -gsctrace <file>   record every variant + state the run meets (saved every 30 s and on quit; adds to <file> if it exists)
    ///   at startup         StreamingAssets/warmup-<platform>-<api>.graphicsstate, if present, is warmed up (a job over the
    ///                      first frames, under the loading screen)
    ///   -nowarmup          skip it (measuring)
    /// Recording: a long run with rain, the map, both vehicles and the cab view, then copy the file into Assets/StreamingAssets.
    public static class ShaderWarmup
    {
        static GraphicsStateCollection trace; static string tracePath;

        public static string FileName => $"warmup-{Application.platform}-{SystemInfo.graphicsDeviceType}.graphicsstate".ToLowerInvariant();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            var a = System.Environment.GetCommandLineArgs();
            bool skip = false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == "-gsctrace" && i + 1 < a.Length) tracePath = System.IO.Path.GetFullPath(a[i + 1]); // (relative: the working directory)
                if (a[i] == "-nowarmup") skip = true;
            }
            if (tracePath != null)
            {
                trace = new GraphicsStateCollection();
                if (System.IO.File.Exists(tracePath)) trace.LoadFromFile(tracePath);
                bool ok = trace.BeginTrace();
                Debug.Log($"[warmup] trace {(ok ? "on" : "FAILED")} → {tracePath} ({trace.variantCount} variants so far)");
                var go = new GameObject("ShaderWarmupTrace"); Object.DontDestroyOnLoad(go); go.AddComponent<Saver>();
                return; // (no warm-up while recording: it would record nothing new)
            }
            if (skip) { Debug.Log("[warmup] off (-nowarmup)"); return; }
            string p = System.IO.Path.Combine(Application.streamingAssetsPath, FileName);
            if (!System.IO.File.Exists(p)) { Debug.Log($"[warmup] no {FileName}"); SvcFallback(); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var gsc = new GraphicsStateCollection();
            if (!gsc.LoadFromFile(p)) { Debug.LogWarning("[warmup] unreadable " + p); SvcFallback(); return; }
            if (gsc.graphicsDeviceType != SystemInfo.graphicsDeviceType || gsc.runtimePlatform != Application.platform)
            { Debug.Log($"[warmup] {FileName} is for {gsc.runtimePlatform}/{gsc.graphicsDeviceType}: skipped"); SvcFallback(); return; }
            var job = gsc.WarmUp();
            var holder = new GameObject("ShaderWarmup"); Object.DontDestroyOnLoad(holder);
            holder.AddComponent<Waiter>().Set(gsc, job, sw);
        }

        /// no PSO recording for this platform / API (Windows D3D11 today): the API-independent ShaderVariantCollection made by
        /// the editor menu "Magistrala/Shaderi: SVC iz GSC" (Resources/Shaderi/warmup). Warmed up in one go here, before the
        /// scene loads: the scene load itself is one long blocking frame, so there are no loading frames to spread it over, and
        /// spreading it into gameplay frames would bring the stutter back. Variants only, no render states: weaker than a GSC.
        static void SvcFallback()
        {
            var svc = Resources.Load<ShaderVariantCollection>("Shaderi/warmup");
            if (!svc) { Debug.Log("[warmup] no Resources/Shaderi/warmup either: no warm-up"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            svc.WarmUp();
            Debug.Log($"[warmup] SVC: {svc.warmedUpVariantCount}/{svc.variantCount} variants of {svc.shaderCount} shaders in {sw.ElapsedMilliseconds} ms");
        }

        class Waiter : MonoBehaviour
        {
            GraphicsStateCollection g; Unity.Jobs.JobHandle job; System.Diagnostics.Stopwatch sw;
            public void Set(GraphicsStateCollection gsc, Unity.Jobs.JobHandle j, System.Diagnostics.Stopwatch s) { g = gsc; job = j; sw = s; }
            void Update()
            {
                if (!job.IsCompleted) return;
                job.Complete();
                Debug.Log($"[warmup] {g.completedWarmupCount}/{g.totalGraphicsStateCount} states of {g.variantCount} variants warmed in {sw.ElapsedMilliseconds} ms");
                Destroy(gameObject);
            }
        }

        class Saver : MonoBehaviour
        {
            float t;
            void Update() { t += Time.unscaledDeltaTime; if (t > 30) { t = 0; Save(false); } }
            void OnApplicationQuit() => Save(true);
            static void Save(bool end)
            {
                if (trace == null) return;
                // SaveToFile writes what the trace has so far; tracing continues
                bool ok = trace.SaveToFile(tracePath);
                Debug.Log($"[warmup] trace saved{(end ? " (quit)" : "")}: {trace.variantCount} variants, {trace.totalGraphicsStateCount} states, ok {ok}");
                if (end) trace.EndTrace();
            }
        }
    }
}
