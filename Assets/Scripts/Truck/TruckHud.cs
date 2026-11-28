// In-game HUD: one tall left column (owner's 119/120 references, 2026-11-28).
// Cash / seven-day payouts, cargo damage, customer reputation / deliveries, job deadline / clock,
// speed / road limit, GpsMap picture, estimated arrival / duration / road distance, lamps / diesel.
// The job card and help keys stay at the right; the phone keeps the bottom right. Opening the phone
// hides that stack but keeps the column. WorldMap and -nohud hide everything. Repaint only,
// useGUILayout off; CachedText and value-keyed caches rebuild strings only when inputs change.
using System.Collections.Generic;
using Magistrala;
using Magistrala.Game;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TruckHud : MonoBehaviour
{
    public TruckController target;
    float smoothedFrameTime = 1f / 60f;
    const float CW = 360, CH = 744, Pad = 14, PW = 485, HintsFor = 15;
    readonly CachedText tSpeed = new CachedText(), tFuel = new CachedText(), tDmg = new CachedText(),
        tClock = new CachedText(), tCash = new CachedText(), tWeek = new CachedText(),
        tRep = new CachedText(), tJobs = new CachedText(), tLimit = new CachedText(), tFps = new CachedText(),
        tDue = new CachedText(), tEta = new CachedText(), tDuration = new CachedText(), tDist = new CachedText();
    readonly List<(string text, int kind)> card = new List<(string, int)>();
    float cardT = -1;
    static readonly string[] Days = { "Pon", "Uto", "Sri", "Čet", "Pet", "Sub", "Ned" };
    static readonly Color Off = new Color(1, 1, 1, .23f);
    const string Keys1 = "WASD vozi · Space ručna · C kabina · F dron · R vrati · miš kamera",
        Keys2 = "G zalazak · V kiša · B kamera · L svjetla · P autopilot · K kvaliteta · F1 pomoć · F2 konzola";
    TruckController lampsTruck;
    TruckLights lights;
    GpsMap gps;
    MagistralaGame mapGame;
    Rect column;

    void Awake()
    {
        useGUILayout = false;
        if (!target) target = MagistralaGame.DrivenTruck();
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt > 0) smoothedFrameTime = Mathf.Lerp(smoothedFrameTime, dt, 1f - Mathf.Exp(-dt * 3f));
        var driven = MagistralaGame.I ? MagistralaGame.I.Truck : target;
        if (driven) target = driven;
        if (!target) target = MagistralaGame.DrivenTruck();
        if (lampsTruck != target) { lampsTruck = target; lights = target ? target.GetComponent<TruckLights>() : null; }
        // TruckLights attaches at scene startup; also recover if its Start runs after this HUD.
        if (target && !lights) lights = target.GetComponent<TruckLights>();
    }

    void OnGUI()
    {
        if (Event.current.type != EventType.Repaint) return;
        if (GpuSwitches.NoHud || WorldMap.IsOpen)
        {
            HudStyle.PanelTop = HudStyle.StackTop = Screen.height;
            HudStyle.PanelRect = HudStyle.MapRect = Rect.zero;
            return;
        }
        HudStyle.Prewarm();
        var g = MagistralaGame.I;
        float K = HudStyle.K, M = HudStyle.Margin;
        column = new Rect(M, Screen.height - M - CH * K, CW * K, CH * K);
        HudStyle.PanelRect = column; HudStyle.PanelTop = column.y;
        HudStyle.MapRect = Rect.zero; HudStyle.StackTop = Screen.height;
        HudStyle.PanelBg(column, 14);
        DrawColumn(g, K);
        bool phone = g && g.Phone && g.Phone.Open;
        if (!phone) Extras(g, new Rect(Screen.width - M - PW * K, Screen.height - M, PW * K, 0));
    }

    Rect Row(float x, float y, float w, float h) => new Rect(column.x + x * HudStyle.K, column.y + y * HudStyle.K, w * HudStyle.K, h * HudStyle.K);
    void Text(float x, float y, float w, float h, string s, float size, Color c, bool bold = false, TextAnchor a = TextAnchor.MiddleLeft)
        => HudStyle.Label(Row(x, y, w, h), s, HudStyle.St(size, bold, c, a));
    void Icon(float x, float y, Texture t, Color c, float size = 23) => HudStyle.Glyph(Row(x, y, size, size), t, c);
    void Rule(float y) => HudStyle.Fill(Row(Pad, y, CW - 2 * Pad, 1), HudStyle.Line);
    void Bar(float x, float y, float w, float pct, Color c)
    {
        HudStyle.Fill(Row(x, y, w, 4), Off, 2 * HudStyle.K);
        if (pct > 0) HudStyle.Fill(Row(x, y, w * Mathf.Clamp01(pct / 100), 4), c, 2 * HudStyle.K);
    }
    static string DayTime(long min) => Days[(int)((min / 1440 % 7 + 7) % 7)] + " " + MagistralaGame.Hm(min);

    void DrawColumn(MagistralaGame g, float K)
    {
        Icon(Pad, 16, HudStyle.Wallet, HudStyle.Muted);
        Text(44, 10, 135, 34, g ? tCash.Get(Mathf.RoundToInt(g.Cash * 100), k => MagistralaGame.Eur(k / 100f)) : "– €", 18, Color.white, true);
        Text(181, 10, 165, 34, g ? tWeek.Get(Mathf.RoundToInt(g.SevenDayEarnings * 100), k => "7d: " + MagistralaGame.Eur(k / 100f)) : "7d: – €", 14, HudStyle.Muted, false, TextAnchor.MiddleRight);
        Rule(47);
        float damage = g ? g.CargoDamagePct : 0;
        Color dc = damage >= 35 ? HudStyle.Bad : damage >= 10 ? HudStyle.Accent : Color.white;
        Icon(Pad, 59, HudStyle.Truck, dc);
        Text(44, 52, 66, 31, tDmg.Get(Mathf.RoundToInt(damage), k => k + "%"), 18, dc);
        Bar(112, 67, 234, damage, dc);
        Rule(92);
        float rep = g ? g.ReputationPct : 0;
        Icon(Pad, 106, HudStyle.Thumb, HudStyle.Good);
        Text(44, 99, 66, 31, g ? tRep.Get(Mathf.RoundToInt(rep), k => k + "%") : "–", 18, Color.white);
        Bar(112, 114, 153, rep, HudStyle.Good);
        Icon(278, 105, HudStyle.Person, HudStyle.Muted);
        Text(303, 99, 43, 31, g ? tJobs.Get(g.Delivered, k => k.ToString()) : "–", 16, Color.white, false, TextAnchor.MiddleRight);
        Rule(138);

        var job = g ? g.Current : null;
        string dest = g && g.World != null ? g.DestName : null;
        string due = job != null ? tDue.Get(Mathf.FloorToInt(job.startMin + job.deadlineMin), k => DayTime(k)) : "";
        var ds = HudStyle.St(17, true, Color.white);
        float dw = HudStyle.Width(HudStyle.St(14, false, HudStyle.Muted), due) / K;
        HudStyle.Label(Row(Pad, 149, CW - 2 * Pad - (job != null ? dw + 12 : 0), 28),
            FitText(ds, dest ?? "Bez odredišta", (CW - 2 * Pad - (job != null ? dw + 12 : 0)) * K), ds);
        Text(CW - Pad - dw, 149, dw, 28, due, 14, HudStyle.Muted);
        bool late = job != null && g.ClockMin > job.startMin + job.deadlineMin;
        Text(Pad, 178, 140, 24, job == null ? "Slobodna vožnja" : late ? "kasniš" : "na vrijeme", 14, job == null ? HudStyle.Muted : late ? HudStyle.Accent : HudStyle.Good);
        Text(164, 178, 182, 24, g ? tClock.Get(Mathf.FloorToInt(g.ClockMin), k => DayTime(k)) : "–", 17, Color.white, false, TextAnchor.MiddleRight);
        Rule(216);

        float kmh = target ? target.SpeedKmh : 0;
        int limit = LocationLabel.SpeedLimit;
        bool over = limit > 0 && kmh > limit + 3;
        Text(Pad, 224, 150, 62, tSpeed.Get(Mathf.RoundToInt(kmh), k => k.ToString()), 48, over ? HudStyle.Accent : Color.white, true);
        Text(151, 239, 70, 30, "km/h", 18, HudStyle.Muted);
        string gear = !target ? null : target.parkLock || target.HandbrakeOn ? "RUČNA" : target.Gear < 0 ? "R" : null;
        if (gear != null) Text(152, 267, 80, 18, gear, 11, HudStyle.Accent, true);
        if (limit > 0)
        {
            var sign = Row(286, 231, 56, 56);
            HudStyle.Fill(sign, Color.white, 28 * K);
            HudStyle.Ring(sign, new Color(.84f, .1f, .1f), 5 * K, 28 * K);
            HudStyle.Label(sign, tLimit.Get(limit, k => k.ToString()), HudStyle.St(limit >= 100 ? 20 : 24, true, HudStyle.Ink, TextAnchor.MiddleCenter));
        }
        var mr = Row(10, 302, 340, 354);
        if (g && g.World != null)
        {
            if (mapGame != g) { mapGame = g; gps = g.GetComponent<GpsMap>(); }
            HudStyle.MapRect = mr;
        }
        var pic = g && g.World != null && gps ? gps.Picture(Mathf.RoundToInt(mr.width), Mathf.RoundToInt(mr.height)) : null;
        if (pic) GUI.DrawTexture(mr, pic, ScaleMode.StretchToFill, false, 0, Color.white, 0, 8 * K);
        else HudStyle.Fill(mr, new Color(.17f, .175f, .19f, 1), 8 * K);
        DrawArrival(g);
        Rule(695);
        var signals = target ? target.Signals : null;
        Icon(14, 709, HudStyle.ArrowLeft, signals && signals.LeftLit ? HudStyle.Good : Off, 25);
        Icon(52, 709, HudStyle.Headlight, lights && lights.IsOn ? HudStyle.Good : Off, 25);
        float fuel = g ? Mathf.Clamp(g.FuelPct, 0, 100) : -1;
        Color fc = fuel < 0 ? Off : fuel < 12 ? HudStyle.Bad : fuel < 20 ? HudStyle.Accent : Color.white;
        Icon(91, 709, HudStyle.Pump, fc, 25);
        int segments = fuel > 0 ? Mathf.CeilToInt(fuel * .12f) : 0;
        for (int i = 0; i < 12; i++) HudStyle.Fill(Row(122 + i * 11, 714, 8, 16), i < segments ? fc : Off, K);
        Text(258, 706, 54, 30, fuel < 0 ? "–" : tFuel.Get(Mathf.CeilToInt(fuel), k => k + "%"), 13, fc, false, TextAnchor.MiddleRight);
        Icon(321, 709, HudStyle.ArrowRight, signals && signals.RightLit ? HudStyle.Good : Off, 25);
    }

    void DrawArrival(MagistralaGame g)
    {
        if (!g || g.Target == null) { Text(Pad, 660, 332, 30, "Bez rute", 13, HudStyle.Muted); return; }
        // Stable planning estimate at 40 km/h, converted with the same clock rate as the simulation.
        // It remains useful while stopped; this is arrival at the GPS target, not the whole multi-stop tour.
        int minutes = Mathf.CeilToInt(Mathf.Max(0, g.RouteMetres) / (40f / 3.6f) * MagistralaGame.ClockRate);
        Text(Pad, 660, 111, 30, tEta.Get(Mathf.FloorToInt(g.ClockMin) + minutes, k => "≈ " + DayTime(k)), 13, Color.white);
        Text(125, 660, 111, 30, tDuration.Get(minutes, k => k >= 60 ? $"{k / 60} č {k % 60} min" : k + " min"), 13, HudStyle.Muted, false, TextAnchor.MiddleCenter);
        Text(236, 660, 110, 30, tDist.Get(Mathf.RoundToInt(g.RouteMetres / 10), k => HudStyle.Km(k * 10)), 13, Color.white, false, TextAnchor.MiddleRight);
    }

    string fitIn, fitOut; GUIStyle fitSt; float fitW;
    string FitText(GUIStyle st, string s, float w)
    {
        if (s == fitIn && st == fitSt && Mathf.Abs(w - fitW) < .5f) return fitOut;
        fitIn = s; fitSt = st; fitW = w; fitOut = s;
        if (HudStyle.Width(st, s) > w)
            for (int n = s.Length - 1; n > 1; n--) { string t = s.Substring(0, n).TrimEnd() + "…"; if (HudStyle.Width(st, t) <= w) { fitOut = t; break; } }
        return fitOut;
    }

    /// Right-side job/help stack; driving keys and F1 readout above the left column.
    void Extras(MagistralaGame g, Rect P)
    {
        float K = HudStyle.K, M = HudStyle.Margin, gap = 10 * K, y = P.y;
        if (g && !string.IsNullOrEmpty(g.HelpKeys))
        {
            var st = HudStyle.St(14, true, HudStyle.Highlight, TextAnchor.MiddleCenter);
            float w = Mathf.Min(Screen.width - 2 * M, HudStyle.Width(st, g.HelpKeys) + 28 * K), h = 30 * K;
            y -= gap + h;
            var r = new Rect(P.xMax - w, y, w, h);
            HudStyle.PanelBg(r, 15);
            HudStyle.Label(r, g.HelpKeys, st);
        }
        if (g && g.World != null)
        {
            if (Time.unscaledTime > cardT) { cardT = Time.unscaledTime + 0.25f; g.JobCard(card); }
            if (card.Count > 0)
            {
                float lh = 24 * K, w = P.width;
                for (int i = 0; i < card.Count; i++) w = Mathf.Max(w, HudStyle.Width(LineSt(card[i].kind), card[i].text) + 2 * Pad * K);
                w = Mathf.Min(w, Screen.width - 2 * M);
                float h = card.Count * lh + 22 * K;
                y -= gap + h;
                var r = new Rect(P.xMax - w, y, w, h);
                HudStyle.PanelBg(r, 14);
                float cy = r.y + 11 * K;
                for (int i = 0; i < card.Count; i++) { HudStyle.Label(new Rect(r.x + Pad * K, cy, w - 2 * Pad * K, lh), card[i].text, LineSt(card[i].kind)); cy += lh; }
            }
        }
        HudStyle.StackTop = y;
        // driving keys for the first seconds and with F1; the fps line with F1
        bool f1 = DebugOverlays.GraphOn;
        float t = Time.timeSinceLevelLoad, a = f1 ? 1 : Mathf.Clamp01((HintsFor - t) / 1.5f);
        if (a > 0)
        {
            var st = HudStyle.St(14, false, Color.white);
            float w = Mathf.Max(HudStyle.Width(st, Keys1), HudStyle.Width(st, Keys2)) + 2 * Pad * K, lh = 22 * K;
            int lines = f1 ? 3 : 2;
            float h = lines * lh + 18 * K;
            w = Mathf.Min(w, Screen.width - 2 * M);
            if (w > 200 * K)
            {
                float bottom = column.y - gap;
                var r = new Rect(M, bottom - h, w, h);
                HudStyle.PanelBg(r, 14, a);
                var old = GUI.color; GUI.color = new Color(1, 1, 1, a);
                float cy = r.y + 9 * K;
                if (f1)
                {
                    float ms = smoothedFrameTime * 1000f;
                    string fps = tFps.Get(Mathf.RoundToInt(ms * 10), k => $"FPS {1000f / Mathf.Max(k / 10f, 0.01f):0} ({k / 10f:0.0} ms) · kvaliteta {RenderQuality.Label}");
                    HudStyle.Label(new Rect(r.x + Pad * K, cy, w - 2 * Pad * K, lh), fps, HudStyle.St(14, true, HudStyle.Highlight)); cy += lh;
                }
                HudStyle.Label(new Rect(r.x + Pad * K, cy, w - 2 * Pad * K, lh), Keys1, st); cy += lh;
                HudStyle.Label(new Rect(r.x + Pad * K, cy, w - 2 * Pad * K, lh), Keys2, st);
                GUI.color = old;
            }
        }
    }

    static GUIStyle LineSt(int kind) => kind switch
    {
        0 => HudStyle.St(17, true, Color.white),
        2 => HudStyle.St(14, false, HudStyle.Muted),
        3 => HudStyle.St(14, true, HudStyle.Accent),
        _ => HudStyle.St(15, false, Color.white),
    };
}
