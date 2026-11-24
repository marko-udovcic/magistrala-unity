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
                if (a[i] == "-gsctrace" && i + 1 < a.Length) tracePath = a[i + 1];
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
            if (!System.IO.File.Exists(p)) { Debug.Log($"[warmup] none ({FileName})"); return; }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var gsc = new GraphicsStateCollection();
            if (!gsc.LoadFromFile(p)) { Debug.LogWarning("[warmup] unreadable " + p); return; }
            if (gsc.graphicsDeviceType != SystemInfo.graphicsDeviceType || gsc.runtimePlatform != Application.platform)
            { Debug.Log($"[warmup] {FileName} is for {gsc.runtimePlatform}/{gsc.graphicsDeviceType}: skipped"); return; }
            var job = gsc.WarmUp();
            var holder = new GameObject("ShaderWarmup"); Object.DontDestroyOnLoad(holder);
            holder.AddComponent<Waiter>().Set(gsc, job, sw);
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
