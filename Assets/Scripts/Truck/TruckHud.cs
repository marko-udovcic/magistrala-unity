// The in-game HUD (owner's mockup references/grafika/hud-mockup-11-20/hud-voznja.png): one dark rounded panel at the bottom
// right (HudStyle) –
//   top row     speed (big, orange over the limit) km/h · fuel % (pump) · cargo damage % (truck) · the clock and "Utorak · Dan 2"
//   second row  cash (muted, left) · "Račun obrta 200,00 €" (orange, right)
//   map         GpsMap's picture (rounded), the speed limit of the road under the truck in its top-left corner (LocationLabel)
//   bottom row  where the GPS leads (MagistralaGame.DestName) and the road distance
// Above it, right-aligned: the job card (MagistralaGame.JobCard: the running tour, or the warehouse board) and the game's keys
// for now (MagistralaGame.HelpKeys, e.g. "Tab mobitel (3 nova poruka)"). The driving keys show for 15 s at the start and while
// the F1 graph is on (then with the fps readout). Hidden while the phone is open (the phone takes the right side), and with
// -nohud. Only the Repaint event draws (useGUILayout off), strings are rebuilt only when their value changes.
using System.Collections.Generic;
using Magistrala;
using Magistrala.Game;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TruckHud : MonoBehaviour
{
    public TruckController target;
    float smoothedFrameTime = 1f / 60f;
    // 1080 p layout of the panel
    const float PW = 485, PH = 361, Pad = 18, TopH = 62, Row2H = 26, MapY = 96, MapH = 213, MapInset = 10, BottomH = 52, HintsFor = 15;
    readonly CachedText tSpeed = new CachedText(), tFuel = new CachedText(), tDmg = new CachedText(), tClock = new CachedText(), tDay = new CachedText(),
        tAcc = new CachedText(), tCash = new CachedText(), tDist = new CachedText(), tLimit = new CachedText(), tFps = new CachedText();
    readonly List<(string text, int kind)> card = new List<(string, int)>();
    float cardT = -1; // JobCard is rebuilt 4× a second
    static readonly string[] Days = { "Ponedjeljak", "Utorak", "Srijeda", "Četvrtak", "Petak", "Subota", "Nedjelja" };
    const string Keys1 = "WASD vozi · Space ručna · C kabina · F dron · R vrati · miš kamera",
        Keys2 = "G zalazak · V kiša · B kamera · L svjetla · P autopilot · K kvaliteta · F1 pomoć · F2 konzola";

    void Awake()
    {
        useGUILayout = false;
        if (!target) target = FindFirstObjectByType<TruckController>();
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (dt > 0) smoothedFrameTime = Mathf.Lerp(smoothedFrameTime, dt, 1f - Mathf.Exp(-dt * 3f));
        if (!target) target = FindFirstObjectByType<TruckController>();
    }

    void OnGUI()
    {
        if (GpuSwitches.NoHud || Event.current.type != EventType.Repaint) return;
        var g = MagistralaGame.I;
        bool phone = (g && g.Phone && g.Phone.Open) || WorldMap.IsOpen;
        if (phone) { HudStyle.PanelTop = Screen.height; HudStyle.PanelRect = Rect.zero; return; }
        float K = HudStyle.K, M = HudStyle.Margin;
        float pw = Mathf.Round(PW * K), ph = Mathf.Round(PH * K);
        bool map = g && g.World != null;
        if (!map) ph = Mathf.Round(TopH * K);
        var P = new Rect(Screen.width - M - pw, Screen.height - M - ph, pw, ph);
        HudStyle.PanelTop = P.y; HudStyle.PanelRect = P;
        HudStyle.PanelBg(P, 14);
        float X(float v) => P.x + v * K;
        float Y(float v) => P.y + v * K;

        // ---- top row: speed · fuel · damage · clock
        float kmh = target ? target.SpeedKmh : 0;
        int limit = LocationLabel.SpeedLimit;
        bool over = limit > 0 && kmh > limit + 3;
        string sp = tSpeed.Get(Mathf.RoundToInt(kmh), k => k.ToString());
        var spSt = HudStyle.St(38, true, over ? HudStyle.Accent : Color.white);
        float spW = HudStyle.Width(spSt, sp);
        HudStyle.Label(new Rect(X(Pad), Y(4), spW + 4, (TopH - 4) * K), sp, spSt);
        string sub = !target ? null : target.parkLock || target.HandbrakeOn ? "RUČNA" : target.Gear < 0 ? "R" : null;
        float ux = X(Pad) + spW + 6 * K;
        HudStyle.Label(new Rect(ux, Y(sub != null ? 14 : 22), 60 * K, 22 * K), "km/h", HudStyle.St(16, false, HudStyle.Muted));
        if (sub != null) HudStyle.Label(new Rect(ux, Y(36), 70 * K, 16 * K), sub, HudStyle.St(11, true, HudStyle.Accent));
        Divider(X(132), Y(17), 28 * K);
        float fuel = g ? g.FuelPct : -1;
        var fc = fuel >= 0 && fuel < 12 ? HudStyle.Bad : fuel >= 0 && fuel < 20 ? HudStyle.Accent : Color.white;
        HudStyle.Glyph(new Rect(X(147), Y(21), 20 * K, 20 * K), HudStyle.Pump, fc);
        HudStyle.Label(new Rect(X(172), Y(18), 60 * K, 26 * K), fuel < 0 ? "–" : tFuel.Get(Mathf.CeilToInt(fuel), k => k + "%"), HudStyle.St(16, false, fc));
        Divider(X(236), Y(17), 28 * K);
        float dmg = g ? g.CargoDamagePct : 0;
        var dc = dmg >= 35 ? HudStyle.Bad : dmg >= 10 ? HudStyle.Accent : Color.white;
        HudStyle.Glyph(new Rect(X(251), Y(21), 21 * K, 21 * K), HudStyle.Truck, dc);
        HudStyle.Label(new Rect(X(278), Y(18), 60 * K, 26 * K), tDmg.Get(Mathf.RoundToInt(dmg), k => k + "%"), HudStyle.St(16, false, dc));
        if (g)
        {
            float cm = g.ClockMin;
            HudStyle.Label(new Rect(X(PW - Pad - 150), Y(9), 150 * K, 30 * K), tClock.Get((long)(cm % 1440), k => MagistralaGame.Hm(k)), HudStyle.St(26, true, Color.white, TextAnchor.MiddleRight));
            HudStyle.Label(new Rect(X(PW - Pad - 180), Y(38), 180 * K, 18 * K), tDay.Get(g.Day, k => $"{Days[k % 7]} · dan {k + 1}"), HudStyle.St(13, false, new Color(1, 1, 1, 0.85f), TextAnchor.MiddleRight));
        }
        if (!map) { Extras(g, P); return; }

        // ---- second row: wallet
        HudStyle.Label(new Rect(X(PW - Pad - 300), Y(TopH), 300 * K, Row2H * K), tAcc.Get(Mathf.RoundToInt(g.Account * 100), k => "Račun obrta " + MagistralaGame.Eur(k / 100f)), HudStyle.St(16, true, HudStyle.Accent, TextAnchor.MiddleRight));
        if (g.Cash > 0.004f) HudStyle.Label(new Rect(X(Pad), Y(TopH), 200 * K, Row2H * K), tCash.Get(Mathf.RoundToInt(g.Cash * 100), k => "Gotovina " + MagistralaGame.Eur(k / 100f)), HudStyle.St(14, false, HudStyle.Muted));

        // ---- the map
        var mr = new Rect(X(MapInset), Y(MapY), Mathf.Round((PW - 2 * MapInset) * K), Mathf.Round(MapH * K));
        var gps = g.GetComponent<GpsMap>();
        var pic = gps ? gps.Picture((int)mr.width, (int)mr.height) : null;
        if (pic) GUI.DrawTexture(mr, pic, ScaleMode.StretchToFill, false, 0, Color.white, 0, 10 * K);
        else HudStyle.Fill(mr, new Color(0.17f, 0.175f, 0.19f, 1), 10 * K);
        if (limit > 0)
        {
            float r = 21 * K; var sr = new Rect(mr.x + 10 * K, mr.y + 10 * K, 2 * r, 2 * r);
            HudStyle.Fill(new Rect(sr.x - K, sr.y - K, sr.width + 2 * K, sr.height + 2 * K), new Color(0, 0, 0, 0.35f), r + K);
            HudStyle.Fill(sr, Color.white, r);
            HudStyle.Ring(sr, new Color(0.84f, 0.1f, 0.1f), 4.5f * K, r);
            HudStyle.Label(sr, tLimit.Get(limit, k => k.ToString()), HudStyle.St(limit >= 100 ? 14 : 17, true, HudStyle.Ink, TextAnchor.MiddleCenter));
        }

        // ---- bottom row: destination · distance
        string dest = g.DestName; float by = Y(MapY + MapH), bh = BottomH * K;
        if (dest != null)
        {
            string d = tDist.Get(Mathf.RoundToInt(g.RouteMetres / 10), k => HudStyle.Km(k * 10));
            var dst = HudStyle.St(19, true, Color.white, TextAnchor.MiddleRight);
            float dw = HudStyle.Width(dst, d);
            HudStyle.Label(new Rect(X(PW - Pad) - dw - 2, by, dw + 2, bh), d, dst);
            HudStyle.Label(new Rect(X(Pad), by, P.width - 2 * Pad * K - dw - 16 * K, bh), dest, HudStyle.St(19, false, Color.white));
        }
        else
        {
            string place = LocationLabel.Place;
            HudStyle.Label(new Rect(X(Pad), by, P.width - 2 * Pad * K, bh), string.IsNullOrEmpty(place) ? "Bez odredišta" : place, HudStyle.St(19, false, HudStyle.Muted));
        }
        Extras(g, P);
    }

    static void Divider(float x, float y, float h) => HudStyle.Fill(new Rect(x, y, Mathf.Max(1, Mathf.Round(HudStyle.K)), h), HudStyle.Line);

    /// above the panel: the game keys, the job card; bottom left: the driving keys (start / F1) and the fps readout (F1)
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
            w = Mathf.Min(w, P.x - 2 * M);
            if (w > 200 * K)
            {
                var r = new Rect(M, Screen.height - M - h, w, h);
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
