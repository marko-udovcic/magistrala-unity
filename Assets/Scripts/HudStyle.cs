// Shared look of the in-game HUD (owner's mockup hud-mockup-11-20): dark rounded panels (#1e1e22, 85 %), white Inter text, orange
// accents from the marketing palette (ink #0f0d0b, accent #f2a33a, highlight #ffc46b). Everything scales with Screen.height
// (1080 p = 1): K × a size in 1080 p pixels. Styles are cached per (size, weight, colour, alignment) so OnGUI does not allocate
// a GUIStyle a frame; T() reuses one GUIContent. Glyphs (fuel pump, truck) are drawn in code once.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala
{
    public static class HudStyle
    {
        public static readonly Color Ink = new Color32(0x0f, 0x0d, 0x0b, 0xff);
        public static readonly Color Accent = new Color32(0xf2, 0xa3, 0x3a, 0xff);
        public static readonly Color Highlight = new Color32(0xff, 0xc4, 0x6b, 0xff);
        public static readonly Color Panel = new Color(0x1e / 255f, 0x1e / 255f, 0x22 / 255f, 0.85f);
        public static readonly Color Muted = new Color(1, 1, 1, 0.62f);
        public static readonly Color Line = new Color(1, 1, 1, 0.14f);
        public static readonly Color Bad = new Color(1f, 0.42f, 0.36f);
        public static readonly Color Good = new Color(0.5f, 0.88f, 0.55f);

        /// 1080 p pixels → screen pixels
        public static float K => Screen.height / 1080f;
        public static float Px(float v) => Mathf.Round(v * K);
        /// the margin of every panel from the screen edges (1080 p: 24 px)
        public static float Margin => Px(24);

        /// the HUD panel's top edge on screen (TruckHud; Screen.height when it is hidden), for whatever stacks above it
        public static float PanelTop = 1e6f;
        /// the HUD panel's rect on screen (zero when hidden)
        public static Rect PanelRect;
        /// the top of everything stacked above the panel (job card, game keys)
        public static float StackTop = 1e6f;

        static Font regular, semi;
        public static Font Regular => regular ? regular : regular = Resources.Load<Font>("Fonts/Inter-Regular");
        public static Font Semi => semi ? semi : semi = (Resources.Load<Font>("Fonts/Inter-SemiBold") ?? Regular);

        static readonly Dictionary<long, GUIStyle> styles = new Dictionary<long, GUIStyle>();
        /// a label style: size in 1080 p pixels (scaled here), Inter regular / semibold
        public static GUIStyle St(float size, bool bold, Color c, TextAnchor a = TextAnchor.MiddleLeft, bool wrap = false)
        {
            int px = Mathf.Max(6, Mathf.RoundToInt(size * K));
            Color32 c32 = c;
            long key = ((long)px << 40) ^ ((long)(bold ? 1 : 0) << 39) ^ ((long)(wrap ? 1 : 0) << 38) ^ ((long)a << 33) ^ ((long)c32.r << 24 | (long)c32.g << 16 | (long)c32.b << 8 | c32.a);
            if (styles.TryGetValue(key, out var s) && s != null) return s;
            s = new GUIStyle(GUI.skin.label)
            {
                font = bold ? Semi : Regular, fontSize = px, alignment = a, wordWrap = wrap, richText = false, clipping = TextClipping.Overflow,
                padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0), normal = { textColor = c },
            };
            styles[key] = s; return s;
        }

        // ------------------------------------------------------------------ glyph prewarm
        // IMGUI's dynamic fonts rasterise a size the first time text is drawn in it, and a full atlas is rebuilt: the first rain
        // toast (default font 22) took 1.4 s, a new banner size ~100 ms (HitchDiag: GUI.Repaint / FontEngine). Prewarm() draws the
        // whole Croatian charset off screen in every HUD size once per screen height (sizes scale with it), in the first frames.
        const string Charset = "0123456789 abcčćdđefghijklmnopqrsštuvwxyzžABCČĆDĐEFGHIJKLMNOPQRSŠTUVWXYZŽ.,:;!?–-—·%€()/'\"+*=<>…„“”‘’°&#@";
        static readonly int[] WarmSizes = { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 24, 26, 28, 30, 34, 38 };
        static int warmH = -1; static GUIStyle warmBox;
        /// call from OnGUI (Repaint); cheap when already done for this screen height
        public static void Prewarm()
        {
            if (warmH == Screen.height || Event.current.type != EventType.Repaint) return;
            warmH = Screen.height;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var off = new Rect(-20000, -20000, 4000, 200);
            int n = 0;
            foreach (int sz in WarmSizes)
                for (int b = 0; b < 2; b++) { var st = St(sz, b == 1, Color.white); st.CalcSize(T(Charset)); GUI.Label(off, T(Charset), st); n++; }
            // the toasts' default-font box (WeatherSystem, CameraFeel, RenderQuality)
            warmBox ??= new GUIStyle(GUI.skin.box) { fontSize = 22 };
            GUI.Box(off, T(Charset), warmBox); n++;
            Debug.Log($"[hud] fonts prewarmed: {n} sizes at {Screen.height} px in {sw.ElapsedMilliseconds} ms");
        }

        static readonly GUIContent tmp = new GUIContent();
        public static GUIContent T(string s) { tmp.text = s; tmp.image = null; tmp.tooltip = null; return tmp; }
        public static float Width(GUIStyle st, string s) => st.CalcSize(T(s)).x;
        public static void Label(Rect r, string s, GUIStyle st) => GUI.Label(r, T(s), st);
        /// a label with a soft dark shadow (over the 3D scene)
        public static void Shadowed(Rect r, string s, GUIStyle st, float alpha = 1)
        {
            var c = st.normal.textColor; float o = Mathf.Max(1, Px(2));
            st.normal.textColor = new Color(0, 0, 0, 0.7f * alpha); GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), T(s), st);
            st.normal.textColor = new Color(c.r, c.g, c.b, c.a * alpha); GUI.Label(r, T(s), st);
            st.normal.textColor = c;
        }

        /// IMGUI blends in linear space in this (linear) project: a dark panel at 85 % looks much lighter than the mockup's
        /// (sRGB-blended) 85 %. The coverage that looks like `a` over a mid-bright scene.
        public static float Cover(float a) => QualitySettings.activeColorSpace == ColorSpace.Linear ? 1 - (1 - a) * 0.55f : a;
        /// GUI.DrawTexture's colour shows gamma-encoded once more in this linear project (measured in the player: red 214 / 26 / 26
        /// came out 236 / 90 / 90; label text colours are not affected): fills, rings and glyphs get the linear value
        public static Color Lin(Color c) { if (QualitySettings.activeColorSpace != ColorSpace.Linear) return c; var l = c.linear; l.a = c.a; return l; }
        public static void Fill(Rect r, Color c, float radius = 0) => GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Lin(c), 0, radius);
        public static void Ring(Rect r, Color c, float width, float radius) => GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Lin(c), width, radius);
        public static void Glyph(Rect r, Texture t, Color c) => GUI.DrawTexture(r, t, ScaleMode.ScaleToFit, true, 0, Lin(c), 0, 0);
        /// the standard dark panel, rounded (radius in 1080 p pixels)
        public static void PanelBg(Rect r, float radius = 14, float alpha = 1)
        {
            var c = Panel; c.a = Cover(c.a) * alpha;
            Fill(r, c, Px(radius));
        }

        // ------------------------------------------------------------------ glyphs (white, alpha = shape; v up)
        static Texture2D pump, truck;
        /// fuel pump, outline style (mockup)
        public static Texture2D Pump => pump ? pump : pump = Make(64, (u, v) =>
        {
            float body = Mathf.Clamp01(RRect(u, v, 0.38f, 0.5f, 0.22f, 0.38f, 0.07f) - RRect(u, v, 0.38f, 0.5f, 0.14f, 0.3f, 0.03f));
            float win = RRect(u, v, 0.38f, 0.66f, 0.1f, 0.08f, 0.02f);
            float bas = RRect(u, v, 0.38f, 0.1f, 0.27f, 0.04f, 0.02f);
            float arm = Mathf.Max(RRect(u, v, 0.68f, 0.7f, 0.1f, 0.04f, 0.02f), RRect(u, v, 0.8f, 0.48f, 0.04f, 0.22f, 0.03f));
            float tip = RRect(u, v, 0.72f, 0.28f, 0.1f, 0.04f, 0.02f);
            return Mathf.Max(Mathf.Max(body, win), Mathf.Max(bas, Mathf.Max(arm, tip)));
        });
        /// lorry, front view, outline style (mockup)
        public static Texture2D Truck => truck ? truck : truck = Make(64, (u, v) =>
        {
            float body = Mathf.Clamp01(RRect(u, v, 0.5f, 0.55f, 0.36f, 0.3f, 0.1f) - RRect(u, v, 0.5f, 0.55f, 0.28f, 0.22f, 0.05f));
            float glass = Band(v, 0.6f, 0.035f) * (Mathf.Abs(u - 0.5f) < 0.3f ? 1 : 0);
            float lights = Mathf.Max(Ell(u, v, 0.31f, 0.41f, 0.05f, 0.05f), Ell(u, v, 0.69f, 0.41f, 0.05f, 0.05f));
            float wheels = Mathf.Max(RRect(u, v, 0.27f, 0.2f, 0.07f, 0.08f, 0.03f), RRect(u, v, 0.73f, 0.2f, 0.07f, 0.08f, 0.03f));
            float mirrors = Mathf.Max(RRect(u, v, 0.08f, 0.66f, 0.04f, 0.09f, 0.02f), RRect(u, v, 0.92f, 0.66f, 0.04f, 0.09f, 0.02f));
            return Mathf.Max(Mathf.Max(body, glass), Mathf.Max(lights, Mathf.Max(wheels, mirrors)));
        });

        static float S(float d, float aa = 0.02f) => Mathf.Clamp01(0.5f - d / aa);
        static float Ell(float u, float v, float cx, float cy, float rx, float ry) { float x = (u - cx) / rx, y = (v - cy) / ry; return S((Mathf.Sqrt(x * x + y * y) - 1) * Mathf.Min(rx, ry)); }
        static float Band(float x, float c, float w) => S(Mathf.Abs(x - c) - w);
        static float RRect(float u, float v, float cx, float cy, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(u - cx) - hx + r, qy = Mathf.Abs(v - cy) - hy + r;
            float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
            return S(d);
        }
        static Texture2D Make(int n, Func<float, float, float> a)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a((x + 0.5f) / n, (y + 0.5f) / n)) * 255));
            t.SetPixels32(px); t.Apply(true, true); return t;
        }

        // ------------------------------------------------------------------ number formats (hr-HR), cached per value
        static readonly System.Globalization.CultureInfo HR = System.Globalization.CultureInfo.GetCultureInfo("hr-HR");
        public static string Dec(float v, string f = "0.0") => v.ToString(f, HR);
        public static string Km(float m) => m >= 1000 ? (m / 1000f).ToString("0.0", HR) + " km" : $"{Mathf.Round(m / 10) * 10:0} m";
    }

    /// a string rebuilt only when its integer key changes (OnGUI runs every frame: no new strings while nothing changes)
    public sealed class CachedText
    {
        long key = long.MinValue; string s = "";
        public string Get(long k, Func<long, string> make) { if (k != key) { key = k; s = make(k); } return s; }
    }
}
