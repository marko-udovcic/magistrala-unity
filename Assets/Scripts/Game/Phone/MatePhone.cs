// Mate's phone (web: ui/hud/MatePhone.tsx, PhoneCall.tsx, game/calls.ts; design: magistrala-tehnicka-strana.md "Dok je mobitel
// otvoren, igra je pauzirana i sat stoji"). GTA-like: Tab raises it in the bottom-right corner; while it is open the game is
// paused (timeScale 0, the clock stops). Apps: Poruke (messages), Pozivi (recent calls + contacts, calling a contact), Poslovi
// (the job offers: a provider from the game, 1/2/3 stay shortcuts at the warehouse), Kontakti; Karta and Banka are placeholders.
// Incoming calls ring with the caller's name without pausing (Y answer · N decline); answering opens the phone on the call:
// the caller's lines as bubbles and 2–4 choices; a choice shows Mate's line and the caller's reply.
// Keys while open: ↑ / ↓ select · Enter open / choose · 1–9 pick directly · Backspace back · Tab close. Mouse works too.
// Plug-ins (faturete haggling, Vesna's errands, the bank): Ring(PhoneCall) with choices, Message(from, text, story),
// jobsProvider for Poslovi; the save round-trips through Export / Import.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Magistrala.Game.Phone
{
    public class PhoneChoice
    {
        public string label;       // what Mate says
        public string reply;       // what the caller answers (null = none)
        public Action act;         // consequence
        public bool openJobs;      // after the call: the Poslovi app
    }

    public class PhoneCall
    {
        public string caller;      // contact id
        public string title = "Dolazni poziv";
        public string sub;         // e.g. "Dispečer · ljut"
        public string[] lines;
        public List<PhoneChoice> choices = new List<PhoneChoice>();
        public float timeoutMin = 60;          // game minutes of ringing before it is missed
        public Action onMissed, onDecline, onAnswer;
        public string declineToast;            // banner after N
        public float ringAt;
    }

    public class PhoneJob { public string label, sub; public Action take; }

    [Serializable] public class PhoneMsg { public string from, text; public float at; public bool read, story; }
    [Serializable] public class PhoneLog { public string who; public float at; public int kind; } // 0 in, 1 missed, 2 out, 3 declined
    [Serializable] public class PhoneSave { public List<PhoneMsg> msgs = new List<PhoneMsg>(); public List<PhoneLog> log = new List<PhoneLog>(); public int randDay = -1, randCount; }

    public class MatePhone : MonoBehaviour
    {
        public struct Contact { public string id, name, role; public Color color; }
        public static readonly Contact[] Contacts =
        {
            new Contact { id = "zdravko", name = "Šef Zdravko", role = "Dispečer, skladište u Lukšiću", color = new Color(0.95f, 0.62f, 0.15f) },
            new Contact { id = "vesna", name = "Vesna", role = "Žena", color = new Color(0.86f, 0.36f, 0.48f) },
            new Contact { id = "zare", name = "Žare", role = "Ćaćin stari kolega", color = new Color(0.35f, 0.62f, 0.9f) },
            new Contact { id = "marija", name = "Marija", role = "Pumpa u Kaštelima", color = new Color(0.4f, 0.75f, 0.45f) },
            new Contact { id = "punica", name = "Dragica", role = "Punica", color = new Color(0.62f, 0.5f, 0.85f) },
        };
        public static Contact Who(string id) { foreach (var c in Contacts) if (c.id == id) return c; return new Contact { id = id, name = id, role = "", color = Color.gray }; }

        public enum Screen { Home, Messages, Thread, Calls, Contacts, Jobs, InCall, Outgoing, Soon, About }
        public bool Open { get; private set; }
        public Screen Current { get; private set; } = Screen.Home;
        public PhoneCall Ringing { get; private set; }
        public bool Paused => Open;
        public int Unread { get { int n = 0; foreach (var m in data.msgs) if (!m.read) n++; return n; } }

        /// game clock (minutes) for the status bar, ringing time-outs and the message stamps
        public Func<float> clock = () => 0;
        public Func<List<PhoneJob>> jobsProvider;
        public Func<string> jobsNote;
        /// outgoing call: contact id → what they say (null = no answer)
        public Func<string, string> outgoing;
        public Action<string> toast;
        public Action changed;

        PhoneSave data = new PhoneSave();
        PhoneCall call; List<(bool mate, string text)> bubbles = new List<(bool, string)>(); bool callDone; PhoneCall afterJobs;
        string thread, outWho, outText;
        int sel; float slide; // 0 hidden .. 1 raised
        readonly List<(string label, string sub, Action act, Color? col)> items = new List<(string, string, Action, Color?)>();

        // ------------------------------------------------------------------ API
        public void Ring(PhoneCall c)
        {
            c.ringAt = clock(); Ringing = c;
            Debug.Log($"[phone] {Hm(c.ringAt)} {Who(c.caller).name} rings");
        }

        public void Message(string from, string text, bool story)
        {
            data.msgs.Add(new PhoneMsg { from = from, text = text, at = clock(), story = story });
            if (data.msgs.Count > 40) data.msgs.RemoveAt(0);
            toast?.Invoke($"Poruka: {Who(from).name}");
            Debug.Log($"[phone] message from {from}: {text}");
            changed?.Invoke();
        }

        /// at most 2 random messages a day (faza1-odgovori Q21); story messages go through Message(.., story: true) and do not count
        public bool TryRandom(int day, string from, string text)
        {
            if (data.randDay != day) { data.randDay = day; data.randCount = 0; }
            if (data.randCount >= 2) return false;
            data.randCount++; Message(from, text, false); return true;
        }
        public int RandomToday(int day) => data.randDay == day ? data.randCount : 0;

        public PhoneSave Export() => data;
        public void Import(PhoneSave s) { data = s ?? new PhoneSave(); }

        public void Show(Screen s) { Open = true; Current = s; sel = 0; }
        public void OpenThread(string from) { thread = from; Show(Screen.Thread); }
        public void Close() { Open = false; Current = Screen.Home; afterJobs = null; ApplyPause(); }

        static string Hm(float m) { m = ((m % 1440) + 1440) % 1440; return $"{(int)(m / 60):00}:{(int)(m % 60):00}"; }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            var kb = Keyboard.current;
            float dt = Time.unscaledDeltaTime;
            bool show = Open || Ringing != null;
            slide = Mathf.MoveTowards(slide, show ? 1 : 0, dt * 5);
            if (Ringing != null && !Open && clock() - Ringing.ringAt >= Ringing.timeoutMin)
            {
                var r = Ringing; Ringing = null; Log(r.caller, 1);
                Debug.Log($"[phone] missed call {r.caller}"); r.onMissed?.Invoke(); changed?.Invoke();
            }
            if (kb == null || WorldMap.IsOpen) { ApplyPause(); return; } // (the full-screen map takes the keys)
            if (Ringing != null && !Open)
            {
                if (kb.yKey.wasPressedThisFrame) Answer();
                else if (kb.nKey.wasPressedThisFrame) Decline();
            }
            else if (kb.tabKey.wasPressedThisFrame) { if (Open) { if (Current != Screen.InCall || callDone) Close(); } else Show(Screen.Home); }
            else if (Open)
            {
                if (kb.upArrowKey.wasPressedThisFrame) sel--;
                if (kb.downArrowKey.wasPressedThisFrame) sel++;
                if (items.Count > 0) sel = (sel % items.Count + items.Count) % items.Count;
                if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) { if (sel < items.Count) items[sel].act?.Invoke(); }
                else if (kb.backspaceKey.wasPressedThisFrame) Back();
                else for (int i = 0; i < 9 && i < items.Count; i++)
                    if (kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) { items[i].act?.Invoke(); break; }
            }
            ApplyPause();
        }

        bool pausedByUs;
        void ApplyPause()
        {
            if (Open && !pausedByUs) { pausedByUs = true; Time.timeScale = 0; AudioListener.pause = true; }
            else if (!Open && pausedByUs) { pausedByUs = false; Time.timeScale = 1; AudioListener.pause = false; }
        }
        void OnDisable() { if (pausedByUs) { Time.timeScale = 1; AudioListener.pause = false; pausedByUs = false; } }

        void Back()
        {
            if (Current == Screen.InCall && !callDone) return; // choose first
            if (Current == Screen.InCall || Current == Screen.Outgoing) { EndCall(); return; }
            if (Current == Screen.Thread) { Current = Screen.Messages; sel = 0; return; }
            if (Current == Screen.Home) Close(); else { Current = Screen.Home; sel = 0; }
        }

        public void Answer()
        {
            var c = Ringing; if (c == null) return;
            Ringing = null; call = c; callDone = c.choices.Count == 0; bubbles.Clear();
            foreach (var l in c.lines) bubbles.Add((false, l));
            Log(c.caller, 0); c.onAnswer?.Invoke();
            Show(Screen.InCall);
            Debug.Log($"[phone] answered {c.caller}");
        }

        public void Decline()
        {
            var c = Ringing; if (c == null) return;
            Ringing = null; Log(c.caller, 3);
            if (c.declineToast != null) toast?.Invoke(c.declineToast);
            c.onDecline?.Invoke(); changed?.Invoke();
            Debug.Log($"[phone] declined {c.caller}");
        }

        /// tests: choose the i-th answer of the call in progress
        public void ChooseIndex(int i) { if (call != null && !callDone && i >= 0 && i < call.choices.Count) Choose(call.choices[i]); }

        void Choose(PhoneChoice ch)
        {
            bubbles.Add((true, ch.label));
            if (ch.reply != null) bubbles.Add((false, ch.reply));
            callDone = true; ch.act?.Invoke(); changed?.Invoke();
            if (ch.openJobs) afterJobs = call;
            sel = 0;
            Debug.Log($"[phone] chose \"{ch.label}\"");
        }

        public void EndCallPublic() => EndCall();

        void EndCall()
        {
            bool jobs = afterJobs != null; afterJobs = null; call = null;
            if (jobs) { Current = Screen.Jobs; sel = 0; } else { Current = Screen.Home; sel = 0; }
        }

        void Log(string who, int kind) { data.log.Add(new PhoneLog { who = who, at = clock(), kind = kind }); if (data.log.Count > 30) data.log.RemoveAt(0); }

        // ------------------------------------------------------------------ drawing (IMGUI): our own modern smartphone – thin black bezel,
        // big rounded corners, a pill notch, status bar (game time · signal · wifi · battery), wallpaper, rounded-square app icons
        // drawn in code (PhoneArt), a dock, a home bar; Inter (OFL, Assets/Resources/Fonts) for all text.
        const float W = 330, H = 660;
        GUIStyle txt, small, head, big, status, label, bubble, item;
        Font regular, semi;

        GUIStyle St(int size, bool bold, Color c, TextAnchor a = TextAnchor.UpperLeft, bool wrap = true) =>
            new GUIStyle(GUI.skin.label) { font = bold ? semi : regular, fontSize = size, alignment = a, wordWrap = wrap, richText = true, padding = new RectOffset(0, 0, 0, 0), normal = { textColor = c } };

        void Styles()
        {
            if (txt != null) return;
            regular = Resources.Load<Font>("Fonts/Inter-Regular"); semi = Resources.Load<Font>("Fonts/Inter-SemiBold") ?? regular;
            txt = St(14, false, Color.white); small = St(11, false, new Color(1, 1, 1, 0.62f)); head = St(26, true, Color.white);
            big = St(60, false, Color.white, TextAnchor.MiddleCenter); status = St(13, true, Color.white, TextAnchor.MiddleLeft, false);
            label = St(11, false, Color.white, TextAnchor.UpperCenter, false);
            bubble = St(14, false, Color.white); bubble.padding = new RectOffset(12, 12, 8, 8);
            item = St(15, true, Color.white); item.padding = new RectOffset(12, 12, 10, 10);
        }

        void Fill(Rect r, Color c, float radius) => GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c, 0, radius);
        void Ring(Rect r, Color c, float width, float radius) => GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c, width, radius);
        void Glyph(Rect r, Texture2D t, Color c) => GUI.DrawTexture(r, t, ScaleMode.ScaleToFit, true, 0, c, 0, 0);

        void OnGUI()
        {
            if (Magistrala.GpuSwitches.NoHud) return;
            if (slide <= 0.001f) return;
            Styles();
            GUI.depth = -10;
            // owner's mockup hud-mobitel.png: the phone ~0.66 of the screen height, at the right edge (HUD margin), vertically
            // centred; drawn in its own 660 px frame scaled by GUI.matrix (Screen.height-based, like the HUD)
            float s = UnityEngine.Screen.height / 1000f, vw = UnityEngine.Screen.width / s, vh = UnityEngine.Screen.height / s;
            var oldM = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
            float e = 1 - Mathf.Pow(1 - slide, 3);
            float x = vw - W - Magistrala.HudStyle.Margin / s, y0 = Mathf.Round((vh - H) / 2);
            float y = y0 + (1 - e) * (vh - y0 + 40);
            bool ringingOnly = Ringing != null && !Open;
            // the incoming call peeks up above the HUD panel (and what is stacked on it): its top ~345 px (caller, answer / decline)
            bool clip = false;
            if (ringingOnly)
            {
                float stack = Mathf.Min(Magistrala.HudStyle.StackTop, Magistrala.HudStyle.PanelTop);
                float bottom = stack < UnityEngine.Screen.height ? stack / s - 12 : vh;
                float yr = Mathf.Max(Magistrala.HudStyle.Margin / s, Mathf.Min(y0, bottom - 345));
                y = yr + (1 - e) * (vh - yr + 40);
                if (bottom < vh) { clip = true; GUI.BeginClip(new Rect(0, 0, vw, bottom)); }
            }
            // device: thin black bezel, rounded corners, a pill notch
            Fill(new Rect(x - 2, y - 2, W + 4, H + 4), new Color(0.32f, 0.33f, 0.36f, 1), 56); // metal edge
            Fill(new Rect(x, y, W, H), Color.black, 54);
            var scr = new Rect(x + 9, y + 9, W - 18, H - 18);
            bool call = ringingOnly || Current == Screen.InCall || Current == Screen.Outgoing;
            GUI.DrawTexture(scr, call ? PhoneArt.WallBlur : PhoneArt.Wall, ScaleMode.StretchToFill, true, 0, Color.white, 0, 46);
            if (Current != Screen.Home && !call) Fill(scr, new Color(0.02f, 0.025f, 0.035f, 0.97f), 46);
            if (call) Fill(scr, new Color(0, 0, 0, 0.35f), 46);
            Fill(new Rect(x + W / 2 - 46, scr.y + 10, 92, 26), Color.black, 13); // the pill
            // status bar
            GUI.Label(new Rect(scr.x + 30, scr.y + 12, 70, 22), Hm(clock()), status);
            float sx = scr.xMax - 92;
            for (int i = 0; i < 4; i++) Fill(new Rect(sx + i * 5, scr.y + 28 - (i + 1) * 3f, 3.4f, (i + 1) * 3f), new Color(1, 1, 1, i < 3 ? 1 : 0.35f), 1);
            Glyph(new Rect(sx + 24, scr.y + 14, 17, 14), PhoneArt.Wifi, Color.white);
            Ring(new Rect(sx + 46, scr.y + 16, 24, 12), new Color(1, 1, 1, 0.55f), 1.2f, 3.5f);
            Fill(new Rect(sx + 48, scr.y + 18, 15, 8), Color.white, 2);
            Fill(new Rect(sx + 71, scr.y + 20, 1.8f, 4), new Color(1, 1, 1, 0.55f), 1);
            var area = new Rect(scr.x + 18, scr.y + 48, scr.width - 36, scr.height - 48 - 30);
            items.Clear();
            if (ringingOnly) DrawRinging(scr);
            else switch (Current)
            {
                case Screen.Home: DrawHome(scr, area); break;
                case Screen.Messages: DrawMessages(area); break;
                case Screen.Thread: DrawThread(area); break;
                case Screen.Calls: DrawCalls(area); break;
                case Screen.Contacts: DrawContacts(area); break;
                case Screen.Jobs: DrawJobs(area); break;
                case Screen.InCall: DrawCall(area); break;
                case Screen.Outgoing: DrawOutgoing(scr, area); break;
                case Screen.Soon: DrawSoon(area); break;
                case Screen.About: DrawAbout(area); break;
            }
            Fill(new Rect(x + W / 2 - 62, scr.yMax - 13, 124, 5), new Color(1, 1, 1, 0.85f), 2.5f); // home bar
            if (Open)
            {
                string hint = Current == Screen.InCall && !callDone ? "↑ ↓ Enter / 1–4 odgovori" : "↑ ↓ Enter · Backspace natrag · Tab zatvori";
                var hs = St(13, true, new Color(1f, 0.86f, 0.35f), TextAnchor.MiddleRight, false);
                GUI.Label(new Rect(x - 330, y + H - 40, 316, 20), hint, hs);
                GUI.Label(new Rect(x - 330, y + H - 62, 316, 20), "PAUZA · sat stoji", hs);
            }
            else if (ringingOnly) GUI.Label(new Rect(x - 330, y + 30, 316, 20), "Y javi se · N odbij", St(14, true, new Color(1f, 0.86f, 0.35f), TextAnchor.MiddleRight, false));
            if (clip) GUI.EndClip();
            GUI.matrix = oldM;
        }

        float Title(Rect a, string t, string sub = null)
        {
            GUI.Label(new Rect(a.x, a.y + 2, a.width, 34), t, head);
            if (sub != null) { GUI.Label(new Rect(a.x, a.y + 36, a.width, 16), sub, small); return a.y + 60; }
            return a.y + 46;
        }

        void Avatar(Rect r, string who)
        {
            var c = Who(who);
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, c.color, 0, r.width / 2);
            string ini = c.name.Replace("Šef ", ""); ini = ini.Length > 0 ? ini.Substring(0, 1) : "?";
            GUI.Label(r, ini, St((int)(r.height * 0.46f), true, Color.white, TextAnchor.MiddleCenter, false));
        }

        /// grouped rows (inset list) from `items`, starting at y
        float List(Rect a, float y, bool grouped = true)
        {
            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                string lab = it.label + (it.sub != null ? $"\n<size=12><color=#ffffff99>{it.sub}</color></size>" : "");
                var st = new GUIStyle(item) { font = semi };
                float h = st.CalcHeight(new GUIContent(lab), a.width);
                if (y + h > a.yMax + 4) break;
                var r = new Rect(a.x, y, a.width, h);
                Color bg = i == sel ? (it.col ?? new Color(0.04f, 0.52f, 1f)) : new Color(1, 1, 1, 0.1f);
                Fill(r, bg, 12);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { sel = i; it.act?.Invoke(); }
                GUI.Label(r, lab, st);
                y += h + 6;
            }
            return y;
        }

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
            Fill(no, new Color(1f, 0.23f, 0.19f), d / 2); Fill(ok, new Color(0.2f, 0.78f, 0.35f), d / 2);
            var m = GUI.matrix; GUIUtility.RotateAroundPivot(135, no.center); Glyph(Shrink(no, 18), PhoneArt.Handset, Color.white); GUI.matrix = m;
            Glyph(Shrink(ok, 18), PhoneArt.Handset, Color.white);
            GUI.Label(new Rect(no.x - 20, no.yMax + 6, d + 40, 16), "Odbij  (N)", label);
            GUI.Label(new Rect(ok.x - 20, ok.yMax + 6, d + 40, 16), "Javi se  (Y)", label);
            if (GUI.Button(no, GUIContent.none, GUIStyle.none)) Decline();
            if (GUI.Button(ok, GUIContent.none, GUIStyle.none)) Answer();
        }

        static Rect Shrink(Rect r, float p) => new Rect(r.x + p, r.y + p, r.width - 2 * p, r.height - 2 * p);

        struct App { public string name; public Texture2D glyph; public Color c; public Screen s; public int badge; }

        void DrawHome(Rect scr, Rect a)
        {
            GUI.Label(new Rect(scr.x, scr.y + 56, scr.width, 20), DayText?.Invoke() ?? "", St(15, true, new Color(1, 1, 1, 0.85f), TextAnchor.MiddleCenter));
            GUI.Label(new Rect(scr.x, scr.y + 74, scr.width, 76), Hm(clock()), St(68, true, Color.white, TextAnchor.MiddleCenter, false));
            var grid = new[]
            {
                new App { name = "Poslovi", glyph = PhoneArt.Box, c = new Color(1f, 0.58f, 0.1f), s = Screen.Jobs },
                new App { name = "Karta", glyph = PhoneArt.Map, c = new Color(0.3f, 0.75f, 0.55f), s = Screen.Soon },
                new App { name = "Banka", glyph = PhoneArt.Bank, c = new Color(0.2f, 0.32f, 0.62f), s = Screen.Soon },
                new App { name = "O igri", glyph = PhoneArt.Person, c = new Color(0.45f, 0.47f, 0.52f), s = Screen.About },
            };
            var dock = new[]
            {
                new App { name = "Pozivi", glyph = PhoneArt.Handset, c = new Color(0.2f, 0.78f, 0.35f), s = Screen.Calls, badge = MissedCount() },
                new App { name = "Poruke", glyph = PhoneArt.Bubble, c = new Color(0.2f, 0.78f, 0.35f), s = Screen.Messages, badge = Unread },
                new App { name = "Kontakti", glyph = PhoneArt.Person, c = new Color(0.56f, 0.58f, 0.62f), s = Screen.Contacts },
            };
            float icon = 58, cell = (scr.width - 24) / 4, gx = scr.x + 12;
            int k = 0;
            void Icon(App ap, Rect r, bool lab)
            {
                int idx = k++; var s2 = ap.s; var nm = ap.name;
                items.Add((ap.name, null, () => { if (s2 == Screen.Soon) soonName = nm; Current = s2; sel = 0; if (s2 == Screen.Calls) MarkMissedSeen(); }, null));
                if (sel == idx) Fill(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), new Color(1, 1, 1, 0.55f), 17);
                Fill(r, ap.c, 14);
                Glyph(Shrink(r, 13), ap.glyph, Color.white);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { sel = idx; items[idx].act(); }
                if (lab) GUI.Label(new Rect(r.x - 12, r.yMax + 5, r.width + 24, 16), ap.name, label);
                if (ap.badge > 0) { var b = new Rect(r.xMax - 15, r.y - 7, 22, 22); Fill(b, new Color(1f, 0.23f, 0.19f), 11); GUI.Label(b, ap.badge.ToString(), St(12, true, Color.white, TextAnchor.MiddleCenter, false)); }
            }
            float top = scr.y + 176;
            for (int i = 0; i < grid.Length; i++) Icon(grid[i], new Rect(gx + i * cell + (cell - icon) / 2, top, icon, icon), true);
            // the latest message as a notification card
            if (data.msgs.Count > 0)
            {
                var m = data.msgs[data.msgs.Count - 1];
                var r = new Rect(scr.x + 12, top + 96, scr.width - 24, 78);
                Fill(r, new Color(0.12f, 0.13f, 0.16f, 0.72f), 18);
                Fill(new Rect(r.x + 12, r.y + 12, 22, 22), new Color(0.2f, 0.78f, 0.35f), 6); Glyph(new Rect(r.x + 15, r.y + 15, 16, 16), PhoneArt.Bubble, Color.white);
                GUI.Label(new Rect(r.x + 42, r.y + 12, r.width - 100, 22), $"<b>{Who(m.from).name}</b>", St(13, true, Color.white, TextAnchor.MiddleLeft, false));
                GUI.Label(new Rect(r.xMax - 64, r.y + 12, 52, 22), Hm(m.at), St(11, false, new Color(1, 1, 1, 0.6f), TextAnchor.MiddleRight, false));
                GUI.Label(new Rect(r.x + 12, r.y + 38, r.width - 24, 36), Cut(m.text, 80), St(13, false, Color.white));
            }
            // dock
            var dk = new Rect(scr.x + 12, scr.yMax - 108, scr.width - 24, 84);
            Fill(dk, new Color(1, 1, 1, 0.22f), 26);
            float dc = dk.width / dock.Length;
            for (int i = 0; i < dock.Length; i++) Icon(dock[i], new Rect(dk.x + i * dc + (dc - icon) / 2, dk.y + 13, icon, icon), false);
        }

        public Func<string> DayText;
        string soonName;
        void DrawSoon(Rect a)
        {
            float y = Title(a, soonName ?? "");
            GUI.Label(new Rect(a.x, y, a.width, 60), "Uskoro.", txt);
            items.Add(("Natrag", null, () => { Current = Screen.Home; sel = 0; }, null));
            List(a, a.yMax - 50);
        }

        // credits / sources (the attribution the data licences ask for; full list in CREDITS.md)
        public const string AboutText =
            "Magistrala — Kaštela, Split.\n\n" +
            "Sadrži podatke Državne geodetske uprave, Digitalni ortofoto LiDAR 2022./23., preuzeto 19. 11. 2026., podaci su dorađeni " +
            "(boja tla). Visine terena i tlocrti zgrada dijelom iz podataka DGU, izmijenjeni.\n\n" +
            "Teksture i nebo: Poly Haven (CC0). Font Inter (SIL OFL 1.1).";
        void DrawAbout(Rect a)
        {
            float y = Title(a, "O igri", "Izvori podataka");
            GUI.Label(new Rect(a.x, y, a.width, a.yMax - 60 - y), AboutText, small);
            items.Add(("Natrag", null, () => { Current = Screen.Home; sel = 0; }, null));
            List(a, a.yMax - 50);
        }

        int MissedCount() { int n = 0; foreach (var l in data.log) if (l.kind == 1) n++; return n; }
        void MarkMissedSeen() { foreach (var l in data.log) if (l.kind == 1) l.kind = 4; }

        void DrawMessages(Rect a)
        {
            float y = Title(a, "Poruke");
            var seen = new List<string>();
            for (int i = data.msgs.Count - 1; i >= 0; i--)
            {
                var m = data.msgs[i]; if (seen.Contains(m.from)) continue; seen.Add(m.from);
                int un = 0; foreach (var q in data.msgs) if (q.from == m.from && !q.read) un++;
                string from = m.from;
                items.Add(($"{Who(from).name}{(un > 0 ? $"  <color=#0a84ff>●</color>" : "")}", $"{Hm(m.at)} · {Cut(m.text, 46)}", () => { thread = from; Current = Screen.Thread; sel = 0; }, null));
            }
            if (items.Count == 0) GUI.Label(new Rect(a.x, y, a.width, 40), "Nema poruka.", small);
            List(a, y);
        }

        static string Cut(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        /// chat bubbles from the bottom up: grey for others, blue for Mate
        void Bubbles(Rect a, float top, float bottom, List<(bool mate, string text, string stamp)> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var (mate, text, stamp) = list[i];
                float bw = a.width * 0.8f;
                float h = bubble.CalcHeight(new GUIContent(text), bw);
                var size = bubble.CalcSize(new GUIContent(text)); if (size.x < bw) bw = Mathf.Max(60, size.x + 2);
                float sh = stamp != null ? 14 : 0;
                if (bottom - h - sh < top) break;
                bottom -= h + sh;
                var r = new Rect(mate ? a.xMax - bw : a.x, bottom, bw, h);
                Fill(r, mate ? new Color(0.04f, 0.52f, 1f) : new Color(0.23f, 0.23f, 0.25f), 17);
                GUI.Label(r, text, bubble);
                if (stamp != null) GUI.Label(new Rect(r.x + 6, r.yMax + 1, 120, 13), stamp, St(10, false, new Color(1, 1, 1, 0.5f), mate ? TextAnchor.UpperRight : TextAnchor.UpperLeft, false));
                bottom -= 8;
            }
        }

        void DrawThread(Rect a)
        {
            var c = Who(thread);
            Avatar(new Rect(a.center.x - 22, a.y, 44, 44), thread);
            GUI.Label(new Rect(a.x, a.y + 46, a.width, 18), c.name, St(13, true, Color.white, TextAnchor.MiddleCenter, false));
            var mine = data.msgs.FindAll(m => m.from == thread);
            foreach (var m in mine) m.read = true;
            var list = new List<(bool, string, string)>(); foreach (var m in mine) list.Add((false, m.text, Hm(m.at)));
            Bubbles(a, a.y + 74, a.yMax - 50, list);
            items.Add(("Natrag", null, () => { Current = Screen.Messages; sel = 0; }, null));
            List(a, a.yMax - 44);
        }

        void DrawCalls(Rect a)
        {
            float y = Title(a, "Pozivi", "Zadnji pozivi");
            for (int i = data.log.Count - 1; i >= 0 && items.Count < 8; i--)
            {
                var l = data.log[i]; string id = l.who;
                string kind = l.kind == 1 || l.kind == 4 ? "<color=#ff453a>propušten</color>" : l.kind == 2 ? "odlazni" : l.kind == 3 ? "odbijen" : "dolazni";
                items.Add((Who(id).name, $"{kind} · {Hm(l.at)}", () => CallOut(id), null));
            }
            if (items.Count == 0) GUI.Label(new Rect(a.x, y, a.width, 30), "Nema poziva.", small);
            List(a, y);
        }

        void DrawContacts(Rect a)
        {
            float y = Title(a, "Kontakti");
            foreach (var c in Contacts) { string id = c.id; items.Add((c.name, c.role, () => CallOut(id), null)); }
            List(a, y);
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
            Fill(end, new Color(1f, 0.23f, 0.19f), 32);
            var m = GUI.matrix; GUIUtility.RotateAroundPivot(135, end.center); Glyph(Shrink(end, 18), PhoneArt.Handset, Color.white); GUI.matrix = m;
            items.Add(("Prekini", null, EndCall, null));
            if (GUI.Button(end, GUIContent.none, GUIStyle.none)) EndCall();
        }

        void DrawJobs(Rect a)
        {
            float y = Title(a, "Poslovi", "Ture firme · skladište u Lukšiću");
            var jobs = jobsProvider?.Invoke();
            string note = jobsNote?.Invoke();
            if (note != null)
            {
                var ns = St(13, false, new Color(1f, 0.82f, 0.45f));
                float h = ns.CalcHeight(new GUIContent(note), a.width);
                GUI.Label(new Rect(a.x, y, a.width, h), note, ns); y += h + 10;
            }
            if (jobs != null) foreach (var j in jobs) { var jj = j; items.Add((j.label, j.sub, () => jj.take(), new Color(1f, 0.58f, 0.1f))); }
            if (jobs == null || jobs.Count == 0) GUI.Label(new Rect(a.x, y, a.width, 20), "Danas nema ponuda.", small);
            List(a, y);
        }

        void DrawCall(Rect a)
        {
            var c = call; if (c == null) { Current = Screen.Home; return; }
            Avatar(new Rect(a.x, a.y, 44, 44), c.caller);
            GUI.Label(new Rect(a.x + 54, a.y + 2, a.width - 54, 24), Who(c.caller).name, St(17, true, Color.white, TextAnchor.MiddleLeft, false));
            GUI.Label(new Rect(a.x + 54, a.y + 24, a.width - 54, 16), (c.sub ?? Who(c.caller).role) + (callDone ? " · poziv završen" : " · u pozivu"), small);
            if (!callDone) { int n = 1; foreach (var ch in c.choices) { var cc = ch; items.Add(($"{n++}  {ch.label}", null, () => Choose(cc), new Color(0.04f, 0.52f, 1f))); } }
            else items.Add((afterJobs != null ? "Otvori Poslove" : "Završi poziv", null, EndCall, afterJobs != null ? new Color(1f, 0.58f, 0.1f) : new Color(1f, 0.23f, 0.19f)));
            float ih = 0; var st = new GUIStyle(item) { font = semi }; foreach (var it in items) ih += st.CalcHeight(new GUIContent(it.label), a.width) + 6;
            float listTop = a.yMax - ih;
            var list = new List<(bool, string, string)>(); foreach (var b in bubbles) list.Add((b.mate, b.text, null));
            Bubbles(a, a.y + 56, listTop - 8, list);
            List(a, listTop);
        }
    }

    /// self-made phone art (no real brand artwork): the wallpaper and white app glyphs as small generated textures
    public static class PhoneArt
    {
        static Texture2D wall, wallBlur, handset, bubble, person, box, map, bank, wifi;
        public static Texture2D Wall => wall ??= Wallpaper(false);
        public static Texture2D WallBlur => wallBlur ??= Wallpaper(true);
        public static Texture2D Handset => handset ??= Make(96, HandsetA);
        public static Texture2D Bubble => bubble ??= Make(96, (u, v) => Mathf.Max(Ell(u, v, 0.5f, 0.54f, 0.42f, 0.33f), Tri(u, v, new Vector2(0.24f, 0.3f), new Vector2(0.4f, 0.3f), new Vector2(0.16f, 0.12f))));
        public static Texture2D Person => person ??= Make(96, (u, v) => Mathf.Max(Ell(u, v, 0.5f, 0.66f, 0.17f, 0.17f), Mathf.Min(Ell(u, v, 0.5f, 0.12f, 0.34f, 0.32f), v > 0.08f ? 1 : 0)));
        public static Texture2D Box => box ??= Make(96, (u, v) => Mathf.Max(RRect(u, v, 0.5f, 0.42f, 0.4f, 0.27f, 0.07f) * (1 - Band(v, 0.5f, 0.025f) * (u > 0.12f && u < 0.88f ? 1 : 0)), RRect(u, v, 0.5f, 0.73f, 0.14f, 0.06f, 0.03f) * (1 - RRect(u, v, 0.5f, 0.72f, 0.09f, 0.03f, 0.01f))));
        public static Texture2D Map => map ??= Make(96, (u, v) => Mathf.Max(Mathf.Clamp01(Ring(u, v, 0.5f, 0.62f, 0.2f, 0.08f) + Tri(u, v, new Vector2(0.32f, 0.56f), new Vector2(0.68f, 0.56f), new Vector2(0.5f, 0.14f)) - Ell(u, v, 0.5f, 0.62f, 0.08f, 0.08f)), 0));
        public static Texture2D Bank => bank ??= Make(96, (u, v) =>
        {
            float a = Tri(u, v, new Vector2(0.1f, 0.68f), new Vector2(0.9f, 0.68f), new Vector2(0.5f, 0.9f));
            a = Mathf.Max(a, RRect(u, v, 0.5f, 0.14f, 0.4f, 0.04f, 0.01f));
            for (int i = 0; i < 4; i++) a = Mathf.Max(a, RRect(u, v, 0.23f + i * 0.18f, 0.41f, 0.045f, 0.22f, 0.01f));
            return a;
        });
        public static Texture2D Wifi => wifi ??= Make(64, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.08f, r = Mathf.Sqrt(dx * dx + dy * dy), ang = Mathf.Abs(Mathf.Atan2(dx, dy));
            if (ang > 0.78f) return 0;
            if (r < 0.13f) return 1;
            return Band(r, 0.38f, 0.07f) + Band(r, 0.66f, 0.07f);
        }, true);

        static float S(float d, float aa = 0.012f) => Mathf.Clamp01(0.5f - d / aa);
        static float Ell(float u, float v, float cx, float cy, float rx, float ry) { float x = (u - cx) / rx, y = (v - cy) / ry; return S((Mathf.Sqrt(x * x + y * y) - 1) * Mathf.Min(rx, ry)); }
        static float Ring(float u, float v, float cx, float cy, float r, float w) { float d = Vector2.Distance(new Vector2(u, v), new Vector2(cx, cy)); return S(Mathf.Abs(d - r) - w); }
        static float Band(float x, float c, float w) => S(Mathf.Abs(x - c) - w);
        static float RRect(float u, float v, float cx, float cy, float hx, float hy, float r)
        {
            float qx = Mathf.Abs(u - cx) - hx + r, qy = Mathf.Abs(v - cy) - hy + r;
            float d = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - r;
            return S(d);
        }
        static float Tri(float u, float v, Vector2 a, Vector2 b, Vector2 c)
        {
            var p = new Vector2(u, v);
            float E(Vector2 p0, Vector2 p1) { var e = p1 - p0; var n = new Vector2(-e.y, e.x).normalized; return Vector2.Dot(p - p0, n); }
            float s = Mathf.Sign(E(a, b) + E(b, c) + E(c, a));
            float d = Mathf.Min(E(a, b) * s, Mathf.Min(E(b, c) * s, E(c, a) * s));
            return S(-d);
        }
        /// a classic handset: a thick arc with two round ends (ear / mouth pieces)
        static float HandsetA(float u, float v)
        {
            float cx = 0.62f, cy = 0.62f, r = 0.4f;
            float dx = u - cx, dy = v - cy, d = Mathf.Sqrt(dx * dx + dy * dy), ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
            float arc = (ang < -75 && ang > -195) || ang > 165 ? S(Mathf.Abs(d - r) - 0.07f) : 0;
            var e1 = new Vector2(cx + r * Mathf.Cos(-80 * Mathf.Deg2Rad), cy + r * Mathf.Sin(-80 * Mathf.Deg2Rad));
            var e2 = new Vector2(cx + r * Mathf.Cos(170 * Mathf.Deg2Rad), cy + r * Mathf.Sin(170 * Mathf.Deg2Rad));
            float p1 = RRectRot(u, v, e1, -80, 0.14f, 0.09f), p2 = RRectRot(u, v, e2, 170, 0.14f, 0.09f);
            return Mathf.Max(arc, Mathf.Max(p1, p2));
        }
        static float RRectRot(float u, float v, Vector2 c, float deg, float hx, float hy)
        {
            float a = -deg * Mathf.Deg2Rad, x = u - c.x, y = v - c.y;
            float rx = x * Mathf.Cos(a) - y * Mathf.Sin(a), ry = x * Mathf.Sin(a) + y * Mathf.Cos(a);
            return RRect(rx + 0.5f, ry + 0.5f, 0.5f, 0.5f, hy, hx, 0.05f);
        }

        static Texture2D Make(int n, Func<float, float, float> a, bool point = false)
        {
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a((x + 0.5f) / n, (y + 0.5f) / n)) * 255));
            t.SetPixels32(px); t.Apply(); return t;
        }

        /// dusk over the sea: deep blue on top to warm orange, soft hills and a sun glow (blurred: wider glow, no hills edge)
        static Texture2D Wallpaper(bool blur)
        {
            int w = 90, h = 180; var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[w * h];
            Color top = new Color(0.08f, 0.12f, 0.32f), mid = new Color(0.55f, 0.32f, 0.5f), low = new Color(0.98f, 0.55f, 0.32f), sea = new Color(0.1f, 0.2f, 0.38f);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float v = y / (float)h, u = x / (float)w;
                Color c = v > 0.55f ? Color.Lerp(mid, top, (v - 0.55f) / 0.45f) : Color.Lerp(low, mid, Mathf.Clamp01((v - 0.3f) / 0.25f));
                float hill = 0.3f + 0.05f * Mathf.Sin(u * 5.2f + 1.1f) + 0.03f * Mathf.Sin(u * 13f);
                float edge = blur ? 0.05f : 0.006f;
                c = Color.Lerp(c, new Color(0.16f, 0.1f, 0.2f), Mathf.Clamp01((hill - v) / edge + 0.5f) * (v > 0.22f ? 1 : 0));
                if (v <= 0.22f) c = Color.Lerp(sea, low * 0.8f, Mathf.Pow(v / 0.22f, 2) * 0.6f);
                float g = Mathf.Exp(-((u - 0.62f) * (u - 0.62f) + (v - 0.3f) * (v - 0.3f) * 2) / (blur ? 0.05f : 0.012f));
                c += new Color(1f, 0.75f, 0.45f) * g * 0.6f;
                if (blur) c *= 0.8f;
                c.a = 1; px[y * w + x] = c;
            }
            t.SetPixels(px); t.Apply(); return t;
        }
    }
}
