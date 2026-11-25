using UnityEngine;

namespace Magistrala.Streaming
{
    /// <summary>Low-frequency diagnostics. Formatting/logging intentionally allocate once per ten seconds.</summary>
    public sealed class StreamingLog
    {
        double nextAt;
        int previousLoads;

        public void Tick(StreamingScheduler scheduler)
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (nextAt == 0) { nextAt = now + 10; return; }
            if (now < nextAt) return;
            nextAt = now + 10;
            int loads = scheduler.LoadsStarted - previousLoads;
            previousLoads = scheduler.LoadsStarted;
            Debug.Log($"[stream] active {scheduler.ActiveCount} pre {scheduler.PreloadedCount} loads {loads} " +
                $"act {scheduler.LastActivationMs:F1}ms max {scheduler.MaxActivationMs:F1}ms " +
                $"loading {scheduler.LoadingCount} queue {scheduler.QueueLength} hits {scheduler.BudgetHits} " +
                $"ahead {scheduler.NearestInactiveAheadMetres:F0}m sceneLatency {scheduler.LastSceneActivationLatencyMs:F1}ms " +
                $"initial {scheduler.InitialLoading} failed {scheduler.HasFailedCells}");
        }
    }
}
