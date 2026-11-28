// Mate's phone: the drawing (IMGUI). Our own modern smartphone – thin black bezel, big rounded corners, a pill notch, the status bar
// (game time · signal · wifi · battery), the wallpaper, rounded-square app icons drawn in code (PhoneArt), a dock, a home bar;
// Inter (OFL, Assets/Resources/Fonts) for all text. Owner 2026-11-25: the phone sits at the bottom right (a phone in a holder right
// of the wheel, GTA layout); the apps are light, clean "real app" screens (grouped lists, cards, round avatars), the home screen
// and the calls stay on the wallpaper. Lists scroll (wheel, or ↑ ↓ follows the selection); Backspace / the ‹ button goes back.
// Colours go through HudStyle.Lin (GUI.DrawTexture tints show gamma-encoded once more in this linear project).
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Magistrala.Game.Phone
{
    public partial class MatePhone
    {
        const float W = 330, H = 660;
        Font regular, semi;
        float scroll; bool follow;
        /// tests (-phonescroll): scroll the open app
        public void ScrollTo(float px) { scroll = Mathf.Max(0, px); }

        // ---- the light app palette (our own: no real app's colours)
        static readonly Color LBg = new Color32(0xF2, 0xF2, 0xF5, 0xff), LCard = Color.white, LText = new Color32(0x1C, 0x1C, 0x1E, 0xff),
            LSub = new Color32(0x86, 0x86, 0x8C, 0xff), LSep = new Color32(0xE2, 0xE2, 0xE7, 0xff), LSel = new Color32(0xFF, 0xEB, 0xD2, 0xff),
            Acc = new Color32(0xE0, 0x84, 0x1F, 0xff), MateBlue = new Color(0.04f, 0.52f, 1f), Red = new Color(1f, 0.23f, 0.19f), Green = new Color(0.2f, 0.74f, 0.35f);

        readonly Dictionary<long, GUIStyle> stCache = new Dictionary<long, GUIStyle>();
        GUIStyle St(int size, bool bold, Color c, TextAnchor a = TextAnchor.UpperLeft, bool wrap = true)
        {
            Color32 c32 = c;
            long key = ((long)size << 40) ^ ((long)(bold ? 1 : 0) << 39) ^ ((long)(wrap ? 1 : 0) << 38) ^ ((long)a << 33) ^ ((long)c32.r << 24 | (long)c32.g << 16 | (long)c32.b << 8 | c32.a);
            if (stCache.TryGetValue(key, out var s) && s != null) return s;
            s = new GUIStyle(GUI.skin.label) { font = bold ? semi : regular, fontSize = size, alignment = a, wordWrap = wrap, richText = true, clipping = TextClipping.Clip, padding = new RectOffset(0, 0, 0, 0), normal = { textColor = c } };
            stCache[key] = s; return s;
        }
        GUIStyle txt, small, bubble, item;

        void Styles()
        {
            if (txt != null) return;
            regular = Resources.Load<Font>("Fonts/Inter-Regular"); semi = Resources.Load<Font>("Fonts/Inter-SemiBold") ?? regular;
            txt = St(14, false, Color.white); small = St(11, false, new Color(1, 1, 1, 0.62f));
            bubble = new GUIStyle(St(14, false, Color.white)) { padding = new RectOffset(12, 12, 8, 8) };
            item = new GUIStyle(St(15, true, Color.white)) { padding = new RectOffset(12, 12, 10, 10) };
        }

        static void Fill(Rect r, Color c, float radius) => GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, HudStyle.Lin(c), 0, radius);
        static void Ring(Rect r, Color c, float width, float radius) => GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, HudStyle.Lin(c), width, radius);
        static void Glyph(Rect r, Texture2D t, Color c) => GUI.DrawTexture(r, t, ScaleMode.ScaleToFit, true, 0, HudStyle.Lin(c), 0, 0);
        static void Line(float x, float y, float w, Color c) => GUI.DrawTexture(new Rect(x, y, w, 1), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, HudStyle.Lin(c), 0, 0);
        float TextW(GUIStyle st, string s) => st.CalcSize(new GUIContent(s)).x;
        /// s cut with "…" to fit width w in style st
        string Fit(GUIStyle st, string s, float w)
        {
            if (TextW(st, s) <= w) return s;
            for (int n = s.Length - 1; n > 1; n--) { string t = s.Substring(0, n).TrimEnd() + "…"; if (TextW(st, t) <= w) return t; }
            return "…";
        }

        void OnGUI()
        {
            if (Magistrala.GpuSwitches.NoHud || WorldMap.IsOpen) return; // (the full-screen map covers everything; the clock stands)
            if (slide <= 0.001f) return;
            Styles();
            GUI.depth = -10;
            // the phone ~0.66 of the screen height in its own 660 px frame scaled by GUI.matrix (Screen.height-based, like the
            // HUD), at the bottom right on the HUD margin
            float s = UnityEngine.Screen.height / 1000f, vw = UnityEngine.Screen.width / s, vh = UnityEngine.Screen.height / s;
            var oldM = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float e = 1 - Mathf.Pow(1 - slide, 3), m = Magistrala.HudStyle.Margin / s;
            float x = vw - W - m, y0 = Mathf.Round(vh - H - m);
            float y = y0 + (1 - e) * (vh - y0 + 40);
            bool ringingOnly = Ringing != null && !Open;
            // The incoming call peeks up above the right job/help stack: its top ~345 px (caller, answer / decline).
            bool clip = false;
            if (ringingOnly)
            {
                float stack = Magistrala.HudStyle.StackTop;
                float bottom = stack < UnityEngine.Screen.height ? stack / s - 12 : vh;
                float yr = Mathf.Max(m, Mathf.Min(y0, bottom - 345));
                y = yr + (1 - e) * (vh - yr + 40);
                if (bottom < vh) { clip = true; GUI.BeginClip(new Rect(0, 0, vw, bottom)); }
            }
            // mouse wheel over the phone: scroll the app
            var ev = Event.current;
            if (ev.type == EventType.ScrollWheel && new Rect(x, y, W, H).Contains(ev.mousePosition)) { scroll += ev.delta.y * 22; follow = false; ev.Use(); }
            // device: thin black bezel, rounded corners
            Fill(new Rect(x - 2, y - 2, W + 4, H + 4), new Color(0.32f, 0.33f, 0.36f, 1), 56); // metal edge
            Fill(new Rect(x, y, W, H), Color.black, 54);
            var scr = new Rect(x + 9, y + 9, W - 18, H - 18);
            bool call = ringingOnly || Current == Screen.InCall || Current == Screen.Outgoing;
            bool light = !call && Current != Screen.Home;
            GUI.DrawTexture(scr, call ? PhoneArt.WallBlur : PhoneArt.Wall, ScaleMode.StretchToFill, true, 0, Color.white, 0, 46);
            if (light) Fill(scr, LBg, 46);
            if (call) Fill(scr, new Color(0, 0, 0, 0.35f), 46);
            var area = new Rect(scr.x + 16, scr.y + 48, scr.width - 32, scr.height - 48 - 30);
            items.Clear();
            bool statusDark = light;
            if (ringingOnly) DrawRinging(scr);
            else switch (Current)
            {
                case Screen.Home: DrawHome(scr, area); break;
                case Screen.Messages: DrawMessages(scr); break;
                case Screen.Thread: DrawThread(scr, area); break;
                case Screen.Calls: DrawCalls(scr); break;
                case Screen.Contacts: DrawContacts(scr); break;
                case Screen.Jobs: DrawJobs(scr); break;
                case Screen.Bank: DrawBank(scr); break;
                case Screen.InCall: DrawCall(area); break;
                case Screen.Outgoing: DrawOutgoing(scr, area); break;
                case Screen.Soon: DrawSoon(scr); break;
                case Screen.About: DrawAbout(scr); break;
                case Screen.Settings: DrawSettings(scr); break;
                case Screen.Blicaj: statusDark = DrawBlicaj(scr); break;
                case Screen.BlicajJavi: DrawBlicajJavi(scr); break;
            }
            // the bezel again over the screen's corners (scrolled content stays inside the rounded screen), the pill, the status bar
            Ring(new Rect(x, y, W, H), Color.black, 9, 54);
            Fill(new Rect(x + W / 2 - 46, scr.y + 10, 92, 26), Color.black, 13);
            StatusBar(scr, statusDark ? LText : Color.white);
            Fill(new Rect(x + W / 2 - 62, scr.yMax - 13, 124, 5), statusDark ? new Color(0, 0, 0, 0.8f) : new Color(1, 1, 1, 0.85f), 2.5f); // home bar
            if (Open)
            {
                int nCh = this.call != null ? Mathf.Max(1, this.call.choices.Count) : 4;
                string hint = Current == Screen.InCall && !callDone ? $"↑ ↓ Enter / 1–{nCh} odgovori" : "↑ ↓ Enter · Backspace natrag · Tab zatvori";
                var hs = St(13, true, new Color(1f, 0.86f, 0.35f), TextAnchor.MiddleRight, false);
                Shadow(new Rect(x - 330, y + H - 40, 316, 20), hint, hs);
                Shadow(new Rect(x - 330, y + H - 62, 316, 20), "PAUZA · sat stoji", hs);
            }
            else if (ringingOnly) Shadow(new Rect(x - 330, y + 30, 316, 20), "Y javi se · N odbij", St(14, true, new Color(1f, 0.86f, 0.35f), TextAnchor.MiddleRight, false));
            if (clip) GUI.EndClip();
            GUI.matrix = oldM;
            follow = false;
        }

        void Shadow(Rect r, string t, GUIStyle st)
        {
            var c = st.normal.textColor; st.normal.textColor = new Color(0, 0, 0, 0.75f);
            GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), t, st); st.normal.textColor = c; GUI.Label(r, t, st);
        }

        void StatusBar(Rect scr, Color c)
        {
            GUI.Label(new Rect(scr.x + 30, scr.y + 12, 70, 22), Hm(clock()), St(13, true, c, TextAnchor.MiddleLeft, false));
            float sx = scr.xMax - 92;
            for (int i = 0; i < 4; i++) Fill(new Rect(sx + i * 5, scr.y + 28 - (i + 1) * 3f, 3.4f, (i + 1) * 3f), new Color(c.r, c.g, c.b, i < 3 ? 1 : 0.35f), 1);
            Glyph(new Rect(sx + 24, scr.y + 14, 17, 14), PhoneArt.Wifi, c);
            Ring(new Rect(sx + 46, scr.y + 16, 24, 12), new Color(c.r, c.g, c.b, 0.55f), 1.2f, 3.5f);
            Fill(new Rect(sx + 48, scr.y + 18, 15, 8), c, 2);
            Fill(new Rect(sx + 71, scr.y + 20, 1.8f, 4), new Color(c.r, c.g, c.b, 0.55f), 1);
        }

        // ------------------------------------------------------------------ building blocks
        /// a light app's top: an optional ‹ back (label), the big title, an optional grey line; returns the content top
        float Header(Rect scr, string title, string sub = null, string back = null)
        {
            float y = scr.y + 46;
            if (back != null)
            {
                var br = new Rect(scr.x + 10, y, 120, 26);
                Glyph(new Rect(br.x, br.y + 5, 16, 16), PhoneArt.Back, Acc);
                GUI.Label(new Rect(br.x + 16, br.y, 104, 26), back, St(15, false, Acc, TextAnchor.MiddleLeft, false));
                if (GUI.Button(br, GUIContent.none, GUIStyle.none)) Back();
                y += 26;
            }
            else y += 8;
            GUI.Label(new Rect(scr.x + 18, y, scr.width - 36, 36), title, St(28, true, LText, TextAnchor.MiddleLeft, false));
            y += 38;
            if (sub != null) { GUI.Label(new Rect(scr.x + 18, y, scr.width - 36, 16), sub, St(12, false, LSub, TextAnchor.MiddleLeft, false)); y += 22; }
            return y + 4;
        }

        /// a scrolling view from top to the screen's bottom (minus `bottomGap`): content drawn by `draw(width)` in local coordinates
        /// from y = 0 − scroll; draw returns the content height and reports the selected row (top, bottom) so ↑ ↓ keep it visible
        void Scroll(Rect scr, float top, float bottomGap, Func<float, float> draw)
        {
            var view = new Rect(scr.x, top, scr.width, scr.yMax - bottomGap - top);
            selTop = selBot = -1;
            GUI.BeginClip(view);
            GUI.BeginGroup(new Rect(0, -scroll, view.width, 100000)); // (content coordinates: y down from the content's top)
            float h = draw(view.width);
            GUI.EndGroup();
            GUI.EndClip();
            float max = Mathf.Max(0, h - view.height + 8);
            if (follow && selTop >= 0)
            {
                float yt = selTop, yb = selBot; // content coordinates
                if (yt - 8 < scroll) scroll = yt - 8;
                else if (yb + 8 > scroll + view.height) scroll = yb + 8 - view.height;
            }
            scroll = Mathf.Clamp(scroll, 0, max);
            if (max > 0) // a thin scroll indicator
            {
                float bh = Mathf.Max(30, view.height * view.height / (h + 8)), by = view.y + (view.height - bh) * (scroll / max);
                Fill(new Rect(scr.xMax - 5, by, 3, bh), new Color(0, 0, 0, 0.25f), 1.5f);
            }
        }
        float selTop, selBot;

        /// a round avatar: the contact's colour and initial (or a person glyph when there is no name)
        void Avatar(Rect r, string who)
        {
            var c = Who(who);
            AvatarC(r, c.color, c.name);
        }
        void AvatarC(Rect r, Color col, string name)
        {
            Fill(r, col, r.width / 2);
            string n = name.Replace("Šef ", "").Replace("Šjor ", "");
            string ini = n.Length > 0 && char.IsLetter(n[0]) ? n.Substring(0, 1).ToUpper() : null;
            if (ini != null) GUI.Label(r, ini, St((int)(r.height * 0.44f), true, Color.white, TextAnchor.MiddleCenter, false));
            else Glyph(new Rect(r.x + r.width * 0.2f, r.y + r.height * 0.2f, r.width * 0.6f, r.height * 0.6f), PhoneArt.Person, Color.white);
        }

        /// one list row in a white grouped card: avatar, the name in bold (a heart after it), the grey line under it, a grey note at
        /// the right; returns the row height. `idx` is its item (selection), the click runs it.
        float Row(float x, float y, float w, int idx, string who, Color? avatarCol, string name, string sub, string right, Color? nameCol = null, bool heart = false, bool sep = true, bool dot = false)
        {
            float h = string.IsNullOrEmpty(sub) ? 52 : 58;
            var r = new Rect(x, y, w, h);
            if (idx == sel) { Fill(r, LSel, 0); selTop = y; selBot = y + h; }
            float ax = x + 12, ad = 38;
            if (who != null) Avatar(new Rect(ax, y + (h - ad) / 2, ad, ad), who);
            else AvatarC(new Rect(ax, y + (h - ad) / 2, ad, ad), avatarCol ?? Color.gray, name);
            float tx = ax + ad + 12, rw = right != null ? TextW(St(12, false, LSub, TextAnchor.MiddleRight, false), right) + 8 : 0;
            var ns = St(15, true, nameCol ?? LText, TextAnchor.MiddleLeft, false);
            float nw = x + w - 14 - rw - tx - (heart ? 20 : 0);
            string nm = Fit(ns, name, nw);
            float ny = string.IsNullOrEmpty(sub) ? y + (h - 22) / 2 : y + 9;
            GUI.Label(new Rect(tx, ny, nw, 22), nm, ns);
            if (heart) Glyph(new Rect(tx + TextW(ns, nm) + 5, ny + 4, 14, 14), PhoneArt.Heart, Red);
            if (!string.IsNullOrEmpty(sub)) { var ss = St(12, false, LSub, TextAnchor.MiddleLeft, false); GUI.Label(new Rect(tx, y + 31, x + w - 14 - tx, 18), Fit(ss, sub, x + w - 14 - tx - (dot ? 14 : 0)), ss); }
            if (right != null) GUI.Label(new Rect(x + w - 12 - rw, ny, rw, 22), right, St(12, false, LSub, TextAnchor.MiddleRight, false));
            if (dot) Fill(new Rect(x + w - 22, y + 35, 9, 9), MateBlue, 4.5f);
            if (sep) Line(tx, y + h - 1, x + w - tx, LSep);
            if (idx >= 0 && GUI.Button(r, GUIContent.none, GUIStyle.none)) { sel = idx; items[idx].act?.Invoke(); }
            return h;
        }

        /// a white card with rounded corners (the grouped lists' background)
        static void Card(Rect r, float radius = 14) => Fill(r, LCard, radius);

        /// a simple grouped list from `items` (light: white card, dark text; dark: the call screen's translucent rows)
        float List(Rect a, float y, bool light = true)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                string lab = it.label + (it.sub != null ? $"\n<size=12><color={(light ? "#86868c" : "#ffffff99")}>{it.sub}</color></size>" : "");
                var st = new GUIStyle(item) { font = semi, normal = { textColor = light && i != sel ? LText : Color.white } };
                float h = st.CalcHeight(new GUIContent(lab), a.width);
                if (y + h > a.yMax + 4) break;
                var r = new Rect(a.x, y, a.width, h);
                Color bg = i == sel ? (it.col ?? Acc) : light ? LCard : new Color(1, 1, 1, 0.1f);
                Fill(r, bg, 12);
                if (i == sel) { selTop = r.y; selBot = r.yMax; }
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { sel = i; it.act?.Invoke(); }
                GUI.Label(r, lab, st);
                y += h + 6;
            }
            return y;
        }

        // ------------------------------------------------------------------ calls (on the wallpaper)
        void DrawRinging(Rect scr)
        {
            var c = Ringing; var who = Who(c.caller);
            GUI.Label(new Rect(scr.x, scr.y + 58, scr.width, 18), c.title, St(13, false, new Color(1, 1, 1, 0.75f), TextAnchor.MiddleCenter));
            GUI.Label(new Rect(scr.x, scr.y + 76, scr.width, 40), who.name, St(30, true, Color.white, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(scr.x, scr.y + 114, scr.width, 18), c.sub ?? who.role, St(13, false, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleCenter));
            float pulse = 1 + 0.05f * Mathf.Sin(Time.unscaledTime * 8);
            float s = 70 * pulse; Avatar(new Rect(scr.center.x - s / 2, scr.y + 146 + (70 - s) / 2, s, s), c.caller);
            // round decline (red, handset turned down) / answer (green) buttons
            float by = scr.y + 236, d = 64;
            var no = new Rect(scr.x + 46, by, d, d); var ok = new Rect(scr.xMax - 46 - d, by, d, d);
            Fill(no, Red, d / 2); Fill(ok, Green, d / 2);
            var m = GUI.matrix; GUIUtility.RotateAroundPivot(135, no.center); Glyph(Shrink(no, 18), PhoneArt.Handset, Color.white); GUI.matrix = m;
            Glyph(Shrink(ok, 18), PhoneArt.Handset, Color.white);
            var label = St(11, false, Color.white, TextAnchor.UpperCenter, false);
            GUI.Label(new Rect(no.x - 20, no.yMax + 6, d + 40, 16), "Odbij  (N)", label);
            GUI.Label(new Rect(ok.x - 20, ok.yMax + 6, d + 40, 16), "Javi se  (Y)", label);
            if (GUI.Button(no, GUIContent.none, GUIStyle.none)) Decline();
            if (GUI.Button(ok, GUIContent.none, GUIStyle.none)) Answer();
        }

        static Rect Shrink(Rect r, float p) => new Rect(r.x + p, r.y + p, r.width - 2 * p, r.height - 2 * p);

        // ------------------------------------------------------------------ home
        struct App { public string name; public Texture2D glyph; public Color c; public Screen s; public int badge; public bool map; }

        void DrawHome(Rect scr, Rect a)
        {
            GUI.Label(new Rect(scr.x, scr.y + 56, scr.width, 20), DayText?.Invoke() ?? "", St(15, true, new Color(1, 1, 1, 0.85f), TextAnchor.MiddleCenter));
            GUI.Label(new Rect(scr.x, scr.y + 74, scr.width, 76), Hm(clock()), St(68, true, Color.white, TextAnchor.MiddleCenter, false));
            var grid = new[]
            {
                new App { name = "Poslovi", glyph = PhoneArt.Box, c = new Color(1f, 0.58f, 0.1f), s = Screen.Jobs },
                new App { name = "Karta", glyph = PhoneArt.Map, c = new Color(0.25f, 0.68f, 0.5f), map = true },
                new App { name = "Banka", glyph = PhoneArt.Bank, c = new Color(0.16f, 0.3f, 0.5f), s = Screen.Bank },
                new App { name = BlicajFeed.App, glyph = PhoneArt.Beacon, c = PoliceIconCol, s = Screen.Blicaj, badge = BlicajUnread },
                new App { name = "Postavke", glyph = PhoneArt.Gear, c = new Color(0.42f, 0.44f, 0.5f), s = Screen.Settings },
            };
            var dock = new[]
            {
                new App { name = "Pozivi", glyph = PhoneArt.Handset, c = Green, s = Screen.Calls, badge = MissedCount() },
                new App { name = "Poruke", glyph = PhoneArt.Bubble, c = Green, s = Screen.Messages, badge = Unread },
                new App { name = "Kontakti", glyph = PhoneArt.Person, c = new Color(0.56f, 0.58f, 0.62f), s = Screen.Contacts },
            };
            float icon = 58, cell = (scr.width - 24) / 4, gx = scr.x + 12;
            var label = St(11, false, Color.white, TextAnchor.UpperCenter, false);
            int k = 0;
            void Icon(App ap, Rect r, bool lab)
            {
                int idx = k++; var s2 = ap.s; bool map = ap.map;
                items.Add((ap.name, null, () =>
                {
                    if (map) { Close(); mapAt = Time.frameCount; return; }
                    Go(s2); if (s2 == Screen.Calls) MarkMissedSeen(); if (s2 == Screen.Blicaj) BlicajOpened();
                }, null));
                if (sel == idx) Fill(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), new Color(1, 1, 1, 0.55f), 17);
                Fill(r, ap.c, 14);
                Glyph(Shrink(r, 13), ap.glyph, Color.white);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { sel = idx; items[idx].act(); }
                if (lab) Shadow(new Rect(r.x - 10, r.yMax + 5, r.width + 20, 16), Fit(label, ap.name, r.width + 20), label);
                if (ap.badge > 0) { var b = new Rect(r.xMax - 15, r.y - 7, 22, 22); Fill(b, Red, 11); GUI.Label(b, ap.badge > 99 ? "99" : ap.badge.ToString(), St(12, true, Color.white, TextAnchor.MiddleCenter, false)); }
            }
            float top = scr.y + 176;
            for (int i = 0; i < grid.Length; i++) Icon(grid[i], new Rect(gx + (i % 4) * cell + (cell - icon) / 2, top + (i / 4) * 92, icon, icon), true);
            top += ((grid.Length - 1) / 4) * 92; // (a second row of apps pushes the notification card down)
            // the latest message as a notification card (or the newest unread group post, when it is newer)
            var bl = LatestUnreadBlicaj();
            var nr = new Rect(scr.x + 12, top + 96, scr.width - 24, 78);
            if (nr.yMax < scr.yMax - 112)
            {
                if (bl != null && (data.msgs.Count == 0 || bl.at > data.msgs[data.msgs.Count - 1].at))
                    NoteCard(nr, PoliceIconCol, PhoneArt.Beacon, $"{BlicajFeed.App} · {bl.who}", bl.at, bl.text);
                else if (data.msgs.Count > 0)
                {
                    var mm = data.msgs[data.msgs.Count - 1];
                    NoteCard(nr, Green, PhoneArt.Bubble, Who(mm.from).name, mm.at, mm.text);
                }
            }
            // dock
            var dk = new Rect(scr.x + 12, scr.yMax - 108, scr.width - 24, 84);
            Fill(dk, new Color(1, 1, 1, 0.22f), 26);
            float dc = dk.width / dock.Length;
            for (int i = 0; i < dock.Length; i++) Icon(dock[i], new Rect(dk.x + i * dc + (dc - icon) / 2, dk.y + 13, icon, icon), false);
        }

        void NoteCard(Rect r, Color c, Texture2D glyph, string title, float at, string text)
        {
            Fill(r, new Color(0.12f, 0.13f, 0.16f, 0.72f), 18);
            Fill(new Rect(r.x + 12, r.y + 12, 22, 22), c, 6); Glyph(new Rect(r.x + 15, r.y + 15, 16, 16), glyph, Color.white);
            var ts = St(13, true, Color.white, TextAnchor.MiddleLeft, false);
            GUI.Label(new Rect(r.x + 42, r.y + 12, r.width - 100, 22), Fit(ts, title, r.width - 100), ts);
            GUI.Label(new Rect(r.xMax - 64, r.y + 12, 52, 22), Hm(at), St(11, false, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleRight, false));
            GUI.Label(new Rect(r.x + 12, r.y + 38, r.width - 24, 36), Cut(text, 80), St(13, false, Color.white));
        }

        public Func<string> DayText;
        string soonName;
        void DrawSoon(Rect scr)
        {
            float y = Header(scr, soonName ?? "", null, "Početna");
            GUI.Label(new Rect(scr.x + 18, y, scr.width - 36, 60), "Uskoro.", St(14, false, LSub));
        }

        // ------------------------------------------------------------------ settings and credits
        // sound volumes (Audio/MagistralaAudio: the mixer groups, remembered in PlayerPrefs "zvuk.*"); Enter or → / ← change the
        // selected one by 10 % (Enter wraps 100 % → 0 %), a short tick plays through the changed group although the game is paused
        void DrawSettings(Rect scr)
        {
            float y = Header(scr, "Postavke", "Zvuk · Enter / ← → mijenja", "Početna");
            int n = Magistrala.Audio.MagistralaAudio.VolLabels.Length;
            for (int i = 0; i < n; i++)
            {
                int k = i; float v = Magistrala.Audio.MagistralaAudio.Volume(i);
                items.Add(($"{Magistrala.Audio.MagistralaAudio.VolLabels[i]}  {Mathf.RoundToInt(v * 100)} %", Bar(v), () => Magistrala.Audio.MagistralaAudio.SetVolume(k, Magistrala.Audio.MagistralaAudio.Volume(k) >= 0.95f ? 0 : Magistrala.Audio.MagistralaAudio.Volume(k) + 0.1f), null));
            }
            items.Add(($"Kvaliteta slike: {Magistrala.RenderQuality.Label}", "tipka K mijenja", null, null));
            items.Add(("O igri", "izvori podataka i licence", () => { Go(Screen.About); aboutPage = 0; }, null));
            var kb = Keyboard.current;
            if (kb != null && sel < n && Event.current.type == EventType.Repaint)
            {
                if (kb.rightArrowKey.wasPressedThisFrame) Magistrala.Audio.MagistralaAudio.SetVolume(sel, Magistrala.Audio.MagistralaAudio.Volume(sel) + 0.1f);
                if (kb.leftArrowKey.wasPressedThisFrame) Magistrala.Audio.MagistralaAudio.SetVolume(sel, Magistrala.Audio.MagistralaAudio.Volume(sel) - 0.1f);
            }
            Scroll(scr, y, 30, w => List(new Rect(16, 0, w - 32, 4000), 0) + 4);
        }
        static string Bar(float v) { int f = Mathf.RoundToInt(v * 10); return new string('●', f) + new string('·', 10 - f); } // (● is in Inter, used by Poruke)

        void DrawAbout(Rect scr)
        {
            var pages = AboutText.Split('\f');
            aboutPage = Mathf.Clamp(aboutPage, 0, pages.Length - 1);
            float y = Header(scr, "O igri", pages.Length > 1 ? $"Izvori podataka · {aboutPage + 1}/{pages.Length}" : "Izvori podataka", "Postavke");
            var card = new Rect(scr.x + 16, y, scr.width - 32, scr.yMax - 140 - y);
            Card(card);
            GUI.Label(Shrink(card, 12), pages[aboutPage].Trim('\n'), St(11, false, LText));
            if (aboutPage + 1 < pages.Length) items.Add(("Dalje", null, () => { aboutPage++; sel = 0; }, null));
            items.Add(("Natrag", null, () => { if (aboutPage > 0) { aboutPage--; sel = 0; } else Go(Screen.Settings); }, null));
            List(new Rect(scr.x + 16, 0, scr.width - 32, scr.yMax - 30), scr.yMax - 30 - (items.Count > 1 ? 100 : 50));
        }

        // ------------------------------------------------------------------ Poruke, Pozivi, Kontakti
        int MissedCount() { int n = 0; foreach (var l in data.log) if (l.kind == 1) n++; return n; }
        void MarkMissedSeen() { foreach (var l in data.log) if (l.kind == 1) l.kind = 4; }

        void DrawMessages(Rect scr)
        {
            float top = Header(scr, "Poruke", null, "Početna");
            var rows = new List<(string from, PhoneMsg last, int unread)>();
            var seen = new List<string>();
            for (int i = data.msgs.Count - 1; i >= 0; i--)
            {
                var m = data.msgs[i]; if (seen.Contains(m.from)) continue; seen.Add(m.from);
                int un = 0; foreach (var q in data.msgs) if (q.from == m.from && !q.read) un++;
                rows.Add((m.from, m, un)); string from = m.from;
                items.Add((Who(from).name, null, () => { thread = from; Go(Screen.Thread); }, null));
            }
            if (rows.Count == 0) { GUI.Label(new Rect(scr.x + 18, top, scr.width - 36, 40), "Nema poruka.", St(14, false, LSub)); return; }
            Scroll(scr, top, 30, w =>
            {
                float y = 0, h = 0; foreach (var r in rows) h += 58;
                Card(new Rect(12, 0, w - 24, h));
                for (int i = 0; i < rows.Count; i++)
                {
                    var (from, last, un) = rows[i]; var c = Who(from);
                    y += Row(12, y, w - 24, i, from, null, c.name, Cut(last.text, 60), Hm(last.at), heart: c.heart, sep: i < rows.Count - 1, dot: un > 0);
                }
                return y + 8;
            });
        }

        static string Cut(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        /// chat bubbles from the bottom up: grey for others, blue for Mate (light: a light grey bubble with dark text)
        /// returns how many of the newest bubbles fitted
        int Bubbles(Rect a, float top, float bottom, List<(bool mate, string text, string stamp)> list, bool light = false)
        {
            int drawn = 0;
            var bs = light ? new GUIStyle(bubble) { normal = { textColor = LText } } : bubble;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var (mate, text, stamp) = list[i];
                var st = mate ? bubble : bs;
                float bw = a.width * 0.8f;
                float h = st.CalcHeight(new GUIContent(text), bw);
                var size = st.CalcSize(new GUIContent(text)); if (size.x < bw) bw = Mathf.Max(60, size.x + 2);
                float sh = stamp != null ? 14 : 0;
                if (bottom - h - sh < top) break;
                bottom -= h + sh; drawn++;
                var r = new Rect(mate ? a.xMax - bw : a.x, bottom, bw, h);
                Fill(r, mate ? MateBlue : light ? new Color32(0xE6, 0xE6, 0xEB, 0xff) : new Color(0.23f, 0.23f, 0.25f), 17);
                GUI.Label(r, text, st);
                if (stamp != null) GUI.Label(new Rect(r.x + 6, r.yMax + 1, 120, 13), stamp, St(10, false, light ? LSub : new Color(1, 1, 1, 0.5f), mate ? TextAnchor.UpperRight : TextAnchor.UpperLeft, false));
                bottom -= 8;
            }
            return drawn;
        }

        void DrawThread(Rect scr, Rect a)
        {
            var c = Who(thread);
            // a thread's top bar: ‹ Poruke, the avatar and the name in the middle
            var br = new Rect(scr.x + 10, scr.y + 46, 90, 26);
            Glyph(new Rect(br.x, br.y + 5, 16, 16), PhoneArt.Back, Acc);
            GUI.Label(new Rect(br.x + 16, br.y, 80, 26), "Poruke", St(15, false, Acc, TextAnchor.MiddleLeft, false));
            if (GUI.Button(br, GUIContent.none, GUIStyle.none)) Back();
            Avatar(new Rect(scr.center.x - 20, scr.y + 46, 40, 40), thread);
            var ns = St(12, true, LText, TextAnchor.MiddleCenter, false);
            GUI.Label(new Rect(scr.x, scr.y + 88, scr.width, 16), c.name, ns);
            if (c.heart) Glyph(new Rect(scr.center.x + TextW(ns, c.name) / 2 + 3, scr.y + 90, 12, 12), PhoneArt.Heart, Red);
            Line(scr.x, scr.y + 110, scr.width, LSep);
            var mine = data.msgs.FindAll(m => m.from == thread);
            foreach (var m in mine) m.read = true;
            var list = new List<(bool, string, string)>(); foreach (var m in mine) list.Add((false, m.text, Hm(m.at)));
            var ta = threadAction?.Invoke(thread);
            Bubbles(a, scr.y + 118, a.yMax - (ta != null ? 56 : 6), list, true);
            if (ta.HasValue)
            {
                items.Add((ta.Value.label, null, ta.Value.act, Acc));
                var r = new Rect(a.x, a.yMax - 46, a.width, 42);
                Fill(r, Acc, 21);
                if (sel == 0) Ring(r, LText, 2, 21);
                GUI.Label(r, ta.Value.label, St(15, true, Color.white, TextAnchor.MiddleCenter, false));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) ta.Value.act?.Invoke();
            }
        }

        void DrawCalls(Rect scr)
        {
            float top = Header(scr, "Pozivi", "Zadnji pozivi", "Početna");
            var rows = new List<PhoneLog>();
            for (int i = data.log.Count - 1; i >= 0 && rows.Count < 20; i--) { var l = data.log[i]; rows.Add(l); string id = l.who; items.Add((Who(id).name, null, () => CallOut(id), null)); }
            if (rows.Count == 0) { GUI.Label(new Rect(scr.x + 18, top, scr.width - 36, 30), "Nema poziva.", St(14, false, LSub)); return; }
            Scroll(scr, top, 30, w =>
            {
                float h = 0; foreach (var l in rows) h += 58;
                Card(new Rect(12, 0, w - 24, h));
                float y = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    var l = rows[i]; var c = Who(l.who); bool missed = l.kind == 1 || l.kind == 4;
                    string kind = missed ? "propušten" : l.kind == 2 ? "odlazni" : l.kind == 3 ? "odbijen" : "dolazni";
                    y += Row(12, y, w - 24, i, l.who, null, c.name, kind, Hm(l.at), missed ? Red : (Color?)null, c.heart, i < rows.Count - 1);
                }
                return y + 8;
            });
        }

        /// Kontakti like a real phone list: alphabetical with letter headers, a search field (for the look), round avatars, the name
        /// in bold and a short grey description (family without one)
        void DrawContacts(Rect scr)
        {
            float top = Header(scr, "Kontakti", null, "Početna");
            var sr = new Rect(scr.x + 16, top, scr.width - 32, 32);
            Fill(sr, new Color32(0xE4, 0xE4, 0xE9, 0xff), 10);
            Glyph(new Rect(sr.x + 10, sr.y + 9, 14, 14), PhoneArt.Search, LSub);
            GUI.Label(new Rect(sr.x + 30, sr.y, 200, 32), "Pretraži", St(14, false, LSub, TextAnchor.MiddleLeft, false));
            top += 42;
            var all = new List<Contact>(Contacts); all.AddRange(extra);
            var hr = System.Globalization.CultureInfo.GetCultureInfo("hr-HR").CompareInfo;
            all.Sort((p, q) => hr.Compare(p.name, q.name, System.Globalization.CompareOptions.IgnoreCase));
            foreach (var c in all) { string id = c.id; items.Add((c.name, c.role, () => CallOut(id), null)); }
            Scroll(scr, top, 30, w =>
            {
                float y = 0; string letter = null;
                for (int i = 0; i < all.Count; i++)
                {
                    var c = all[i]; string L = c.name.Substring(0, 1).ToUpper();
                    if (L != letter)
                    {
                        letter = L;
                        if (i > 0) y += 6;
                        GUI.Label(new Rect(18, y, 60, 22), L, St(13, true, LSub, TextAnchor.MiddleLeft, false)); y += 22;
                    }
                    bool last = i + 1 == all.Count || all[i + 1].name.Substring(0, 1).ToUpper() != L;
                    float rh = string.IsNullOrEmpty(c.role) ? 52 : 58;
                    bool first = i == 0 || all[i - 1].name.Substring(0, 1).ToUpper() != L;
                    // the white group card (rounded at its ends)
                    var cr = new Rect(12, y, w - 24, rh);
                    GUI.DrawTexture(cr, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, HudStyle.Lin(LCard), Vector4.zero, new Vector4(first ? 12 : 0, first ? 12 : 0, last ? 12 : 0, last ? 12 : 0));
                    y += Row(12, y, w - 24, i, c.id, null, c.name, c.role, null, null, c.heart, !last);
                }
                return y + 10;
            });
        }

        void CallOut(string id)
        {
            outWho = id; outText = outgoing?.Invoke(id); Log(id, 2);
            Current = Screen.Outgoing; sel = 0;
            Debug.Log($"[phone] calling {id}: {outText ?? "(no answer)"}");
        }

        void DrawOutgoing(Rect scr, Rect a)
        {
            Avatar(new Rect(scr.center.x - 40, scr.y + 70, 80, 80), outWho);
            GUI.Label(new Rect(scr.x, scr.y + 158, scr.width, 36), Who(outWho).name, St(28, true, Color.white, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(scr.x, scr.y + 194, scr.width, 18), outText == null ? "Ne javlja se." : "u pozivu", St(13, false, new Color(1, 1, 1, 0.7f), TextAnchor.MiddleCenter));
            if (outText != null) Bubbles(a, scr.y + 220, a.yMax - 96, new List<(bool, string, string)> { (false, outText, null) });
            var end = new Rect(scr.center.x - 32, a.yMax - 80, 64, 64);
            Fill(end, Red, 32);
            var m = GUI.matrix; GUIUtility.RotateAroundPivot(135, end.center); Glyph(Shrink(end, 18), PhoneArt.Handset, Color.white); GUI.matrix = m;
            items.Add(("Prekini", null, EndCall, null));
            if (GUI.Button(end, GUIContent.none, GUIStyle.none)) EndCall();
        }

        // ------------------------------------------------------------------ Poslovi
        /// the job offers as cards: the label in bold, the details in grey, the orange take arrow; the note (why there is nothing,
        /// or what to do) on a pale card above
        void DrawJobs(Rect scr)
        {
            float top = Header(scr, "Poslovi", jobsTitle?.Invoke() ?? "Ture firme · skladište", "Početna");
            var jobs = jobsProvider?.Invoke();
            string note = jobsNote?.Invoke();
            if (jobs != null) foreach (var j in jobs) { var jj = j; items.Add((j.label, j.sub, () => jj.take(), j.col)); }
            Scroll(scr, top, 30, w =>
            {
                float y = 0, cw = w - 24;
                if (note != null)
                {
                    var ns = St(13, false, new Color32(0x6B, 0x45, 0x10, 0xff));
                    float h = ns.CalcHeight(new GUIContent(note), cw - 28) + 20;
                    var r = new Rect(12, y, cw, h);
                    Fill(r, new Color32(0xFF, 0xF1, 0xDC, 0xff), 12);
                    GUI.Label(new Rect(r.x + 14, r.y + 10, cw - 28, h - 20), note, ns);
                    y += h + 10;
                }
                if (jobs == null || jobs.Count == 0)
                {
                    var r = new Rect(12, y, cw, 64); Card(r);
                    GUI.Label(r, "Danas nema ponuda.", St(14, false, LSub, TextAnchor.MiddleCenter, false));
                    return y + 72;
                }
                var ls = St(15, true, LText); var ss = St(12, false, LSub);
                for (int i = 0; i < jobs.Count; i++)
                {
                    var j = jobs[i];
                    string lab = j.label, num = null;
                    // "1  Pumpa…": the shortcut number as a round badge
                    if (lab.Length > 2 && char.IsDigit(lab[0]) && lab[1] == ' ') { num = lab.Substring(0, 1); lab = lab.Substring(1).TrimStart(); }
                    float tx = num != null ? 52 : 16, tw = cw - tx - 34;
                    float lh = ls.CalcHeight(new GUIContent(lab), tw), sh = string.IsNullOrEmpty(j.sub) ? 0 : ss.CalcHeight(new GUIContent(j.sub), tw);
                    float h = Mathf.Max(60, lh + sh + 26);
                    var r = new Rect(12, y, cw, h);
                    Card(r);
                    if (i == sel) { Ring(r, j.col ?? Acc, 2.5f, 14); selTop = y; selBot = y + h; }
                    if (num != null) { var b = new Rect(r.x + 12, r.y + (h - 28) / 2, 28, 28); Fill(b, j.col ?? Acc, 14); GUI.Label(b, num, St(14, true, Color.white, TextAnchor.MiddleCenter, false)); }
                    GUI.Label(new Rect(r.x + tx, r.y + 12, tw, lh), lab, ls);
                    if (sh > 0) GUI.Label(new Rect(r.x + tx, r.y + 14 + lh, tw, sh), j.sub, ss);
                    Glyph(new Rect(r.xMax - 26, r.y + (h - 14) / 2, 14, 14), PhoneArt.Chevron, j.col ?? Acc);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { sel = i; j.take?.Invoke(); }
                    y += h + 8;
                }
                return y;
            });
        }

        // ------------------------------------------------------------------ Banka
        static readonly System.Globalization.CultureInfo HR = System.Globalization.CultureInfo.GetCultureInfo("hr-HR");
        static string Eur(float v) => v.ToString("#,##0.00", HR) + " €";

        /// the bank app: the trade account's balance on a dark card, the cash, the loan Mate guarantees (what is left, a progress
        /// bar, the next instalment) and the last payments into the account
        void DrawBank(Rect scr)
        {
            var b = bank?.Invoke() ?? new BankInfo();
            float top = Header(scr, "Banka", "Račun obrta · GALEB TRANSPORT", "Početna");
            items.Add(("Stanje", null, null, null));
            Scroll(scr, top, 30, w =>
            {
                float y = 0, cw = w - 24;
                // balance card (deep sea blue, our own)
                var bc = new Rect(12, y, cw, 118);
                Fill(bc, new Color32(0x17, 0x3A, 0x55, 0xff), 16);
                GUI.Label(new Rect(bc.x + 16, bc.y + 14, cw - 32, 16), "Stanje računa", St(12, false, new Color(1, 1, 1, 0.75f), TextAnchor.MiddleLeft, false));
                GUI.Label(new Rect(bc.x + 16, bc.y + 34, cw - 32, 40), Eur(b.account), St(30, true, b.account < 0 ? new Color(1f, 0.6f, 0.55f) : Color.white, TextAnchor.MiddleLeft, false));
                GUI.Label(new Rect(bc.x + 16, bc.y + 84, cw - 32, 18), "HR·· ···· ···· ···· 0811 · obrt", St(11, false, new Color(1, 1, 1, 0.55f), TextAnchor.MiddleLeft, false));
                y += 128;
                // cash
                var cc = new Rect(12, y, cw, 50); Card(cc);
                Glyph(new Rect(cc.x + 14, cc.y + 15, 20, 20), PhoneArt.Wallet, Green);
                GUI.Label(new Rect(cc.x + 44, cc.y, 150, 50), "Gotovina u džepu", St(14, false, LText, TextAnchor.MiddleLeft, false));
                GUI.Label(new Rect(cc.x, cc.y, cw - 14, 50), Eur(b.cash), St(15, true, LText, TextAnchor.MiddleRight, false));
                y += 60;
                // the loan
                if (b.debtTotal > 0)
                {
                    var lc = new Rect(12, y, cw, 122); Card(lc);
                    GUI.Label(new Rect(lc.x + 14, lc.y + 12, cw - 28, 18), "Kredit", St(15, true, LText, TextAnchor.MiddleLeft, false));
                    GUI.Label(new Rect(lc.x + 14, lc.y + 31, cw - 28, 16), b.loanName, St(11, false, LSub, TextAnchor.MiddleLeft, false));
                    GUI.Label(new Rect(lc.x + 14, lc.y + 50, cw - 28, 20), "Preostalo", St(12, false, LSub, TextAnchor.MiddleLeft, false));
                    GUI.Label(new Rect(lc.x, lc.y + 50, cw - 14, 20), Eur(b.debt), St(15, true, LText, TextAnchor.MiddleRight, false));
                    var pb = new Rect(lc.x + 14, lc.y + 76, cw - 28, 6);
                    Fill(pb, LSep, 3);
                    float paid = Mathf.Clamp01(1 - b.debt / b.debtTotal);
                    if (paid > 0) Fill(new Rect(pb.x, pb.y, Mathf.Max(6, pb.width * paid), pb.height), Acc, 3);
                    GUI.Label(new Rect(lc.x + 14, lc.y + 90, cw - 28, 20), "Sljedeća rata", St(12, false, LSub, TextAnchor.MiddleLeft, false));
                    GUI.Label(new Rect(lc.x, lc.y + 90, cw - 14, 20), $"{Eur(b.rate)}{(string.IsNullOrEmpty(b.rateWhen) ? "" : " · " + b.rateWhen)}", St(13, true, Red, TextAnchor.MiddleRight, false));
                    y += 132;
                }
                // last payments
                GUI.Label(new Rect(18, y, cw, 20), "Zadnje uplate", St(13, true, LSub, TextAnchor.MiddleLeft, false)); y += 24;
                if (b.moves.Count == 0)
                {
                    var r = new Rect(12, y, cw, 52); Card(r);
                    GUI.Label(r, "Još nema uplata na račun.", St(13, false, LSub, TextAnchor.MiddleCenter, false));
                    return y + 60;
                }
                float h = b.moves.Count * 52;
                Card(new Rect(12, y, cw, h));
                for (int i = 0; i < b.moves.Count; i++)
                {
                    var (who, when, eur) = b.moves[i];
                    var r = new Rect(12, y, cw, 52);
                    Fill(new Rect(r.x + 12, r.y + 10, 32, 32), eur >= 0 ? new Color32(0xE3, 0xF5, 0xE6, 0xff) : new Color32(0xFD, 0xE6, 0xE4, 0xff), 16);
                    Glyph(new Rect(r.x + 20, r.y + 18, 16, 16), eur >= 0 ? PhoneArt.ArrowIn : PhoneArt.ArrowOut, eur >= 0 ? Green : Red);
                    GUI.Label(new Rect(r.x + 54, r.y + 8, cw - 150, 20), who, St(14, true, LText, TextAnchor.MiddleLeft, false));
                    GUI.Label(new Rect(r.x + 54, r.y + 27, cw - 150, 16), when, St(11, false, LSub, TextAnchor.MiddleLeft, false));
                    GUI.Label(new Rect(r.x, r.y, cw - 14, 52), (eur >= 0 ? "+" : "") + Eur(eur), St(14, true, eur >= 0 ? new Color32(0x1E, 0x8E, 0x3E, 0xff) : (Color)Red, TextAnchor.MiddleRight, false));
                    if (i + 1 < b.moves.Count) Line(r.x + 54, r.yMax - 1, cw - 54, LSep);
                    y += 52;
                }
                return y + 10;
            });
        }

        // ------------------------------------------------------------------ the conversation (on the wallpaper)
        void DrawCall(Rect a)
        {
            var c = call; if (c == null) { Current = Screen.Home; return; }
            Avatar(new Rect(a.x, a.y, 44, 44), c.caller);
            GUI.Label(new Rect(a.x + 54, a.y + 2, a.width - 54, 24), Who(c.caller).name, St(17, true, Color.white, TextAnchor.MiddleLeft, false));
            GUI.Label(new Rect(a.x + 54, a.y + 24, a.width - 54, 16), (c.sub ?? Who(c.caller).role) + (c.chat ? "" : callDone ? " · poziv završen" : " · u pozivu"), small);
            if (!callDone) { int n = 1; foreach (var ch in c.choices) { var cc = ch; items.Add(($"{n++}  {ch.label}", null, () => Choose(cc), MateBlue)); } }
            else items.Add((afterJobs != null ? "Otvori Poslove" : c.chat ? "Zatvori" : "Završi poziv", null, c.chat ? (Action)(() => { call = null; Close(); }) : EndCall, afterJobs != null ? new Color(1f, 0.58f, 0.1f) : Red));
            float ih = 0; var st = new GUIStyle(item) { font = semi }; foreach (var it in items) ih += st.CalcHeight(new GUIContent(it.label), a.width) + 6;
            float listTop = a.yMax - ih;
            var list = new List<(bool, string, string)>(); foreach (var b in bubbles) list.Add((b.mate, b.text, null));
            Bubbles(a, a.y + 56, listTop - 8, list);
            List(a, listTop, false);
        }
    }
}
