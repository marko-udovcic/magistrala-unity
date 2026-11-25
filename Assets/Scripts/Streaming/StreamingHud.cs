using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Magistrala.Streaming
{
    [DisallowMultipleComponent]
    public sealed class StreamingHud : MonoBehaviour
    {
        public bool Visible { get; set; }
        StreamingScheduler scheduler;
        string text = "";
        double nextRefresh;
        bool warned;

        void Awake()
        {
            scheduler = GetComponent<StreamingScheduler>();
            Visible = Array.IndexOf(Environment.GetCommandLineArgs(), "-streamhud") >= 0;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f8Key.wasPressedThisFrame) Visible = !Visible;
            if (!Visible || !scheduler || Time.realtimeSinceStartupAsDouble < nextRefresh) return;
            nextRefresh = Time.realtimeSinceStartupAsDouble + .25;
            float ahead = scheduler.NearestInactiveAheadMetres;
            warned = scheduler.StreamingEnabled && ahead < 300;
            string distance = float.IsPositiveInfinity(ahead) ? "none on ray" : ahead.ToString("F0") + " m";
            text = $"STREAM  {(scheduler.StreamingEnabled ? scheduler.Status : "idle (-stream 0)")}\n" +
                $"active {scheduler.ActiveCount}   pre {scheduler.PreloadedCount}   loading {scheduler.LoadingCount}\n" +
                $"queue {scheduler.QueueLength}   budget hits {scheduler.BudgetHits}\n" +
                $"activation {scheduler.LastActivationMs:F2} ms   max {scheduler.MaxActivationMs:F2} ms\n" +
                $"scene activation latency {scheduler.LastSceneActivationLatencyMs:F1} ms\n" +
                $"inactive ahead: {distance}   target > 300 m\nF8: streaming HUD";
        }

        void OnGUI()
        {
            if (!Visible || !scheduler) return;
            var panel = new Rect(HudStyle.Margin, HudStyle.Px(100), HudStyle.Px(590), HudStyle.Px(225));
            HudStyle.PanelBg(panel);
            HudStyle.Label(new Rect(panel.x + HudStyle.Px(16), panel.y + HudStyle.Px(10), panel.width - HudStyle.Px(32), panel.height - HudStyle.Px(20)),
                text, HudStyle.St(18, false, warned ? HudStyle.Bad : HudStyle.Highlight, TextAnchor.UpperLeft));
        }
    }
}
