using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;

namespace Magistrala
{
    /// Measuring only (`-hitchdbg`, added by ProbeBench): what a stutter frame was made of.
    ///  - every frame: GC Allocated In Frame (ProfilerRecorder), GC collections, the traffic's / trees' own ms;
    ///  - every 10 s an [alloc] line: KB allocated per frame (avg / max), GC count, managed heap;
    ///  - every frame over 40 ms a [hitchdbg] line: dt, alloc, GC, FrameTiming (main / render / GPU), the 12 heaviest profiler markers
    ///    of that frame (every timing marker the player has registered, rescanned every 20 s: development players also have the
    ///    per-script "X.Update() [Invoke]" ones) and the events other systems noted (HitchDiag.Note: spawns, loads, weather …).
    [DefaultExecutionOrder(32000)]
    public class HitchDiag : MonoBehaviour
    {
        public static bool On;
        /// systems call this when they do something possibly heavy this frame (guard the string building with `if (HitchDiag.On)`)
        public static void Note(string s) { if (On && cur.Length < 400) cur.Append(' ').Append(s); }
        static System.Text.StringBuilder cur = new System.Text.StringBuilder(), prev = new System.Text.StringBuilder();

        ProfilerRecorder gcAlloc, gcUsed;
        readonly List<ProfilerRecorder> recs = new List<ProfilerRecorder>();
        readonly List<string> names = new List<string>();
        readonly HashSet<string> seen = new HashSet<string>();
        readonly List<ProfilerRecorderHandle> handles = new List<ProfilerRecorderHandle>();
        float t0, scanT = 15, winT; int gcLast, lines;
        long winAlloc, winMax; int winFrames, winGc0;
        readonly FrameTiming[] ft = new FrameTiming[4];
        readonly long[] top = new long[12]; readonly int[] topI = new int[12];

        void OnEnable()
        {
            On = true; t0 = Time.realtimeSinceStartup;
            gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            gcUsed = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Used Memory");
            gcLast = winGc0 = System.GC.CollectionCount(0);
            Application.logMessageReceived += OnLog;
        }
        // every log line of the frame is an event too (weather, saves, phone, map …): its first 48 characters
        void OnLog(string msg, string st, LogType t) { if (!msg.StartsWith("[hitchdbg]") && !msg.StartsWith("[alloc]")) Note(msg.Length > 48 ? msg.Substring(0, 48) : msg); }
        void OnDisable()
        {
            Application.logMessageReceived -= OnLog;
            On = false; gcAlloc.Dispose(); gcUsed.Dispose();
            foreach (var r in recs) r.Dispose(); recs.Clear(); names.Clear(); seen.Clear();
        }

        void Scan()
        {
            handles.Clear(); ProfilerRecorderHandle.GetAvailable(handles);
            int added = 0;
            foreach (var h in handles)
            {
                if (recs.Count >= 3000) break;
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (!seen.Add(d.Name)) continue;
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                string n = d.Name;
                // containers that are always the biggest: skip, they say nothing
                if (n == "PlayerLoop" || n == "Main Thread" || n == "Render Thread" || n.StartsWith("Gfx.Wait") || n == "WaitForTargetFPS"
                    || n == "Semaphore.WaitForSignal" || n.StartsWith("Idle") || n == "CPU Total Frame Time" || n == "CPU Main Thread Frame Time"
                    || n == "CPU Render Thread Frame Time" || n == "GPU Frame Time" || n.Contains("Job.") || n.StartsWith("WaitFor")) continue;
                recs.Add(ProfilerRecorder.StartNew(d.Category, n, 1)); names.Add(n); added++;
            }
            if (added > 0) Debug.Log($"[hitchdbg] markers +{added} = {recs.Count}");
        }

        void LateUpdate()
        {
            float now = Time.realtimeSinceStartup - t0, dt = Time.unscaledDeltaTime;
            if (now > scanT) { scanT = now + 20; Scan(); }
            long a = gcAlloc.Valid ? gcAlloc.LastValue : 0;
            int gc = System.GC.CollectionCount(0), dGc = gc - gcLast; gcLast = gc;
            if (now > 15) { winAlloc += a; winMax = System.Math.Max(winMax, a); winFrames++; }
            if (dt > .04f && now > 15 && lines++ < 30)
            {
                var sb = new System.Text.StringBuilder();
                var tr = Magistrala.Game.MagistralaGame.DrivenTruck();
                sb.Append($"[hitchdbg] t={now:F1}s {dt * 1000:F0}ms alloc={a / 1024}KB gc+{dGc} trees={TreeRenderer.LastMs:F1} traffic={TrafficDiag.LastMs:F1}ms v={(tr ? tr.SpeedKmh : 0):F0}");
                FrameTimingManager.CaptureFrameTimings();
                int nft = (int)FrameTimingManager.GetLatestTimings(4, ft);
                sb.Append(" ft[");
                for (int i = 0; i < nft; i++) sb.Append($"{ft[i].cpuMainThreadFrameTime:F0}/{ft[i].cpuRenderThreadFrameTime:F0}/{ft[i].gpuFrameTime:F0} ");
                sb.Append("]");
                for (int k = 0; k < top.Length; k++) { top[k] = 0; topI[k] = -1; }
                for (int i = 0; i < recs.Count; i++)
                {
                    if (!recs[i].Valid) continue;
                    long v = recs[i].LastValue; if (v < 1_000_000 || v <= top[top.Length - 1]) continue;
                    int k = top.Length - 1; while (k > 0 && top[k - 1] < v) { top[k] = top[k - 1]; topI[k] = topI[k - 1]; k--; }
                    top[k] = v; topI[k] = i;
                }
                for (int k = 0; k < top.Length; k++) if (topI[k] >= 0) sb.Append($" | {names[topI[k]]}={top[k] * 1e-6:F1}");
                if (prev.Length > 0) sb.Append(" || prev:").Append(prev);
                if (cur.Length > 0) sb.Append(" || now:").Append(cur);
                Debug.Log(sb.ToString());
            }
            winT += dt;
            if (winT >= 10)
            {
                int g = System.GC.CollectionCount(0);
                Debug.Log($"[alloc] t={now:F0}s {(winFrames > 0 ? winAlloc / winFrames / 1024f : 0):F1} KB/frame avg, max {winMax / 1024} KB, gc={g - winGc0}, heap {(gcUsed.Valid ? gcUsed.LastValue / 1048576f : 0):F0} MB");
                winT = 0; winAlloc = winMax = 0; winFrames = 0; winGc0 = g; lines = 0;
            }
            (prev, cur) = (cur, prev); cur.Clear();
        }
    }

    /// the traffic's last Update in ms (TrafficSystem sets it; static so HitchDiag needs no reference)
    public static class TrafficDiag { public static float LastMs; }
}
