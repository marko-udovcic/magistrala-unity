// Mate's phone (web: ui/hud/MatePhone.tsx, PhoneCall.tsx, game/calls.ts; design: magistrala-tehnicka-strana.md "Dok je mobitel
// otvoren, igra je pauzirana i sat stoji"). GTA-like: Tab raises it in the bottom-right corner (a phone in a holder right of the
// wheel, owner 2026-11-25; the minimap moved to the bottom left); while it is open the game is paused (timeScale 0, the clock
// stops). Apps: Poslovi (the job offers: a provider from the game, 1/2/3 stay shortcuts at the warehouse), Karta (closes the
// phone and opens the full-screen M map), Banka (the trade account, cash, the loan Mate guarantees, the last payments: bank
// provider), Policija Nas Zaustavlja (the local group page, was "Blicaj": MatePhone.Blicaj.cs, Blicaj.cs), Postavke (sound;
// "O igri" – the licence credits – inside it); the dock: Pozivi, Poruke, Kontakti. Drawing: MatePhone.Draw.cs.
// Incoming calls ring with the caller's name without pausing (Y answer · N decline); answering opens the phone on the call:
// the caller's lines as bubbles and 2–4 choices; a choice shows Mate's line and the caller's reply.
// Keys while open: ↑ / ↓ select · Enter open / choose · 1–9 pick directly · Backspace back · Tab close. Mouse works too.
// Plug-ins (faturete haggling, Vesna's errands, the bank): Ring(PhoneCall) with choices, Message(from, text, story),
// jobsProvider for Poslovi; the save round-trips through Export / Import.
// Conversations (faturete haggling, MagistralaGame.Faturete.cs): OpenChat(PhoneCall { chat = true }) shows a message conversation
// on the call screen without ringing; a choice with `next` continues it (the other side's lines + new choices), so a haggle can
// run several rounds. AddContact adds people who are not in the fixed list (customers).
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
        public Func<PhoneStep> next; // the conversation goes on: the other side's lines and new choices (null / no choices = done)
    }

    public class PhoneStep { public string[] lines; public List<PhoneChoice> choices = new List<PhoneChoice>(); public string sub; }

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
        public bool chat;                      // a message conversation (OpenChat), not a ringing call
    }

    public class PhoneJob { public string label, sub; public Action take; public Color? col; }

    /// the Banka app: the trade account, the cash in the pocket, the loan Mate guarantees (left to pay, the next instalment)
    /// and the last payments (who, when, € – positive in)
    public class BankInfo
    {
        public float account, cash, debt, debtTotal, rate; public string rateWhen = "", loanName = "";
        public List<(string who, string when, float eur)> moves = new List<(string, string, float)>();
    }

    [Serializable] public class PhoneMsg { public string from, text; public float at; public bool read, story; }
    [Serializable] public class PhoneLog { public string who; public float at; public int kind; } // 0 in, 1 missed, 2 out, 3 declined
    [Serializable] public class PhoneSave { public List<PhoneMsg> msgs = new List<PhoneMsg>(); public List<PhoneLog> log = new List<PhoneLog>(); public int randDay = -1, randCount; public BlicajSave blicaj = new BlicajSave(); }

    public partial class MatePhone : MonoBehaviour
    {
        /// a phone contact: as Mate saved it (family without a description, "Vesna" with a heart), the grey line under the name
        public struct Contact { public string id, name, role; public Color color; public bool heart; }
        /// the game's people (calls, messages) and šjor Duje (no calls yet: "Ne javlja se."); Lucija and šogor Ante left out
        /// (Marko, 26. 11. 2026)
        public static readonly Contact[] Contacts =
        {
            new Contact { id = "zdravko", name = "Šef Zdravko", role = "Dispečer · firma", color = new Color(0.95f, 0.62f, 0.15f) },
            new Contact { id = "vesna", name = "Vesna", role = "", color = new Color(0.86f, 0.36f, 0.48f), heart = true },
            new Contact { id = "zare", name = "Žare", role = "Ćaćin stari kolega", color = new Color(0.35f, 0.62f, 0.9f) },
            new Contact { id = "marija", name = "Marija", role = "Pumpa Kaštela", color = new Color(0.4f, 0.75f, 0.45f) },
            new Contact { id = "punica", name = "Punica", role = "", color = new Color(0.62f, 0.5f, 0.85f) },
            new Contact { id = "duje", name = "Šjor Duje", role = "Taksist", color = new Color(0.9f, 0.75f, 0.2f) },
        };
        static readonly List<Contact> extra = new List<Contact>();
        /// people outside the fixed list (fatureta customers); replaces one with the same id
        public static void AddContact(Contact c) { extra.RemoveAll(x => x.id == c.id); extra.Add(c); }
        public static Contact Who(string id) { foreach (var c in Contacts) if (c.id == id) return c; foreach (var c in extra) if (c.id == id) return c; return new Contact { id = id, name = id, role = "", color = Color.gray }; }

        public enum Screen { Home, Messages, Thread, Calls, Contacts, Jobs, InCall, Outgoing, Soon, About, Settings, Blicaj, BlicajJavi, Bank }
        public bool Open { get; private set; }
        public Screen Current { get; private set; } = Screen.Home;
        public PhoneCall Ringing { get; private set; }
        public bool Paused => Open;
        public int Unread { get { int n = 0; foreach (var m in data.msgs) if (!m.read) n++; return n; } }

        /// game clock (minutes) for the status bar, ringing time-outs and the message stamps
        public Func<float> clock = () => 0;
        public Func<List<PhoneJob>> jobsProvider;
        public Func<string> jobsNote;
        /// the Poslovi app's subtitle
        public Func<string> jobsTitle;
        /// an extra action in a message thread (e.g. "Odgovori na ponudu"), null = none
        public Func<string, (string label, Action act)?> threadAction;
        /// outgoing call: contact id → what they say (null = no answer)
        public Func<string, string> outgoing;
        public Action<string> toast;
        public Action changed;
        /// the Karta app: the full-screen map (WorldMap), opened the frame after the phone closes (the Enter that chose the app
        /// must not also set a waypoint there)
        public Action openMap;
        /// the Banka app's figures (MagistralaGame.Day.cs)
        public Func<BankInfo> bank;
        int mapAt = -1;

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
        public void Import(PhoneSave s) { data = s ?? new PhoneSave(); if (data.blicaj == null) data.blicaj = new BlicajSave(); blicajFeed = null; }

        public void Show(Screen s) { Open = true; Current = s; sel = 0; scroll = 0; }
        /// go to an app screen (the selection and the scroll start at the top)
        void Go(Screen s) { Current = s; sel = 0; scroll = 0; }
        /// a message conversation with choices (faturete): no ringing, no call log
        public void OpenChat(PhoneCall c)
        {
            call = c; callDone = c.choices.Count == 0; bubbles.Clear(); afterJobs = null;
            foreach (var l in c.lines) bubbles.Add((false, l));
            Show(Screen.InCall);
            Debug.Log($"[phone] conversation with {c.caller}");
        }
        public bool InChat => Open && Current == Screen.InCall && call != null && call.chat;
        public void OpenThread(string from) { thread = from; Show(Screen.Thread); }
        public void Close() { Open = false; Current = Screen.Home; afterJobs = null; ApplyPause(); }

        static string Hm(float m) { m = ((m % 1440) + 1440) % 1440; return $"{(int)(m / 60):00}:{(int)(m % 60):00}"; }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            var kb = Keyboard.current;
            float dt = Time.unscaledDeltaTime;
            if (Current != Screen.About) aboutPage = 0;
            TickBlicaj();
            if (mapAt >= 0 && Time.frameCount > mapAt) { mapAt = -1; openMap?.Invoke(); }
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
            else if (kb.tabKey.wasPressedThisFrame) { if (Open) { if (Current != Screen.InCall || callDone || (call != null && call.chat)) { if (call != null && call.chat) call = null; Close(); } } else Show(Screen.Home); }
            else if (Open)
            {
                if (kb.upArrowKey.wasPressedThisFrame) { sel--; follow = true; }
                if (kb.downArrowKey.wasPressedThisFrame) { sel++; follow = true; }
                if (items.Count > 0) sel = (sel % items.Count + items.Count) % items.Count;
                if (!KatoviTool.Active && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) { if (sel < items.Count) items[sel].act?.Invoke(); }
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
            if (Current == Screen.InCall && !callDone && !(call != null && call.chat)) return; // choose first (a conversation can wait)
            if (Current == Screen.InCall || Current == Screen.Outgoing) { EndCall(); return; }
            if (Current == Screen.Thread) { Go(Screen.Messages); return; }
            if (Current == Screen.BlicajJavi) { Go(Screen.Blicaj); return; }
            if (Current == Screen.About) { Go(Screen.Settings); return; }
            if (Current == Screen.Home) Close(); else Go(Screen.Home);
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
            callDone = true; ch.act?.Invoke();
            var st = ch.next?.Invoke();
            if (st != null && call != null)
            {
                if (st.lines != null) foreach (var l in st.lines) bubbles.Add((false, l));
                if (st.sub != null) call.sub = st.sub;
                call.choices = st.choices ?? new List<PhoneChoice>(); callDone = call.choices.Count == 0;
            }
            changed?.Invoke();
            if (ch.openJobs) afterJobs = call;
            sel = 0;
            Debug.Log($"[phone] chose \"{ch.label}\"");
        }

        public void EndCallPublic() => EndCall();

        void EndCall()
        {
            bool jobs = afterJobs != null; afterJobs = null; call = null;
            Go(jobs ? Screen.Jobs : Screen.Home);
        }

        void Log(string who, int kind) { data.log.Add(new PhoneLog { who = who, at = clock(), kind = kind }); if (data.log.Count > 30) data.log.RemoveAt(0); }

        // credits / sources (the attribution the data licences ask for; full list in CREDITS.md)
        public const string AboutText =
            "Magistrala — Kaštela, Split.\n" +
            "\n" +
            "Karta i prostor\n" +
            "© OpenStreetMap contributors\n" +
            "openstreetmap.org/copyright\n" +
            "Podaci: ODbL 1.0\n" +
            "opendatacommons.org/licenses/odbl/1-0/\n" +
            "\n" +
            "Ceste, nazivi, zgrade i namjena tla; podaci prilagođeni igri." +
            AboutPage +
            "Državna geodetska uprava\n" +
            "\n" +
            "Sadrži podatke Državne geodetske uprave, Digitalni ortofoto LiDAR 2022./23., preuzeto 19. 11. 2026., podaci su dorađeni.\n" +
            "\n" +
            "Tlo, obala, krošnje i procjene katova iz sjena; teren i tlocrti dijelom iz DGU podataka.\n" +
            "Ne predstavlja službeni geodetski dokument.\n" +
            "\n" +
            "Otvorena dozvola\n" +
            "data.gov.hr/otvorena-dozvola/\n" +
            "dgu.gov.hr/otvoreni-podaci/6596" +
            AboutPage +
            "Fotografije i opažanja\n" +
            "\n" +
            "Street-level imagery: Mapillary contributors (CC BY-SA 4.0)\n" +
            "mapillary.com\n" +
            "creativecommons.org/licenses/by-sa/4.0/\n" +
            "\n" +
            "Boje i vrsta fasada, škure, položaji svjetiljki, stupova, parkirališta i stabala; vrsta i visina stabala.\n" +
            "Izmjereno, filtrirano i prilagođeno igri. Fotografije nisu isporučene. Za zaštićene prerade vrijedi ista licenca." +
            AboutPage +
            "Teksture, nebo i biljke\n" +
            "\n" +
            "Poly Haven — CC0\n" +
            "polyhaven.com\n" +
            "\n" +
            "ambientCG — Lennart Demes, CC0\n" +
            "ambientcg.com\n" +
            "\n" +
            "NatureManufacture — Coast & Dunes Environment – Dynamic Nature, Unity Asset Store.\n" +
            "\n" +
            "Punat: „2005 Fiat Grande Punto” — ActsOfDoge / Sketchfab, izmijenjeno. Izvor navodi „Free Standard”; uvjeti su u provjeri." +
            AboutPage +
            "Fontovi — SIL OFL 1.1\n" +
            "\n" +
            "Inter — Copyright (c) 2016 The Inter Project Authors\n" +
            "github.com/rsms/inter\n" +
            "\n" +
            "D-DIN Condensed Bold — Copyright (C) 2017 Datto Inc.\n" +
            "datto.com/fonts/d-din\n" +
            "\n" +
            "Liberation Sans — Copyright (c) 2010 Google Corporation; (c) 2012 Red Hat, Inc.\n" +
            "\n" +
            "Licenca: openfontlicense.org\n" +
            "Puni tekstovi licenci priloženi su fontovima." +
            AboutPage +
            "3D modeli — Sketchfab\n" +
            "\n" +
            "Sljedeći modeli: CC BY 4.0\n" +
            "creativecommons.org/licenses/by/4.0/\n" +
            "\n" +
            "Izmijenjeno: uklonjene oznake, naše tablice i boje, materijali i razine detalja (LOD). Sprinter ima i našu policijsku izvedbu.\n" +
            "\n" +
            "Kombi firme koristi „Mercedes Benz Sprinter 2006” autora Max (Max-7215 / Max-5532), produljen i povišen, s našom unutrašnjosti. Izvor je naveden uz Sprinter.\n" +
            "\n" +
            "Galeb koristi „European Delivery Van” autora Evan Hiltz — CC BY 4.0\n" +
            "sketchfab.com/3d-models/european-delivery-van-0b2f1ad95a79419f9a092420024d329c\n" +
            "creativecommons.org/licenses/by/4.0/\n" +
            "Izmijenjeno: uklonjene oznake i tablice, naša grafika GALEB TRANSPORT, naša unutrašnjost kabine." +
            AboutPage +
            "Benzinske koriste „Gas station” i „Gas station Props” autora Elbolilloduro — CC BY 4.0\n" +
            "sketchfab.com (Elbolilloduro)\n" +
            "creativecommons.org/licenses/by/4.0/\n" +
            "Izmijenjeno: dio rekvizita i tekstura preuzet, ostalo novo modelirano; marke Jadranska nafta, Sunce gorivo i Benzol su izmišljene." +
            AboutPage +
            "Prometni auti — CC BY 4.0\n" +
            "Izmijenjeni; licenca na stranici „3D modeli”.\n" +
            "\n" +
            "„Zastava 101 (Stojadin)”\n" +
            "Tomislav Tomljenović\n" +
            "sketchfab.com/models/52c885cfc52a4ac1acb14f84a3feacfa\n" +
            "\n" +
            "„Yugo Koral 45”\n" +
            "Tomislav Tomljenović\n" +
            "sketchfab.com/models/f5d14e34c9064b49b943e0c4a3add6ca\n" +
            "\n" +
            "„BMW E46 MSport”\n" +
            "Ricy\n" +
            "sketchfab.com/models/614c2f1660474178a1d4717b7492d1c7" +
            AboutPage +
            "Prometni auti — CC BY 4.0\n" +
            "Izmijenjeni; licenca na stranici „3D modeli”.\n" +
            "\n" +
            "„Mercedes-Benz E-class W124 (unfinished)”\n" +
            "OverLord\n" +
            "sketchfab.com/models/4b468dbe4b6d4ca994bc7066b90781e2\n" +
            "\n" +
            "„Volkswagen Golf II”\n" +
            "ARDmation\n" +
            "sketchfab.com/models/740500f10bc94e7a91c2290e445fcb39\n" +
            "\n" +
            "„Volkswagen Passat (2004 V6 4Motion)”\n" +
            "I3D99\n" +
            "sketchfab.com/models/480c0793f02b43d78b0d0c4a0eb6acb6" +
            AboutPage +
            "Prometni auti — CC BY 4.0\n" +
            "Izmijenjeni; licenca na stranici „3D modeli”.\n" +
            "\n" +
            "„Compact Hatchback”\n" +
            "RCC Design\n" +
            "sketchfab.com/models/abbd4e65bbf1475b866d84735088b8ff\n" +
            "\n" +
            "„Executive Sedan”\n" +
            "RCC Design\n" +
            "sketchfab.com/models/eadf0e3171d74e4999e37898f95578c8\n" +
            "\n" +
            "„European Delivery Van”\n" +
            "Evan Hiltz\n" +
            "sketchfab.com/models/0b2f1ad95a79419f9a092420024d329c" +
            AboutPage +
            "Prometni auti — CC BY 4.0\n" +
            "Izmijenjeni; licenca na stranici „3D modeli”.\n" +
            "\n" +
            "„Asti Stradale '89 - Low poly model”\n" +
            "Daniel Zhabotinsky (DanielZhabotinsky)\n" +
            "sketchfab.com/models/97b15b067111443ebe05840137b49c65\n" +
            "\n" +
            "„Milano '95 - Low poly model”\n" +
            "Daniel Zhabotinsky (DanielZhabotinsky)\n" +
            "sketchfab.com/models/122a22d210234596ad23623588d0d166\n" +
            "\n" +
            "„Olympic '95 - Low poly model”\n" +
            "Daniel Zhabotinsky (DanielZhabotinsky)\n" +
            "sketchfab.com/models/4809a093a0f341409757298a9e7e0003" +
            AboutPage +
            "Prometni auti — CC BY 4.0\n" +
            "Izmijenjeni; licenca na stranici „3D modeli”.\n" +
            "\n" +
            "„Tozzo '98 - Low poly model”\n" +
            "Daniel Zhabotinsky (DanielZhabotinsky)\n" +
            "sketchfab.com/models/d43bb748da364af2b3f0a9348365f0fb\n" +
            "\n" +
            "„Sigil '07 - Low poly model”\n" +
            "Daniel Zhabotinsky (DanielZhabotinsky)\n" +
            "sketchfab.com/models/22abe5284d4c4b55920b8462eb24a8c1\n" +
            "\n" +
            "„Compact '07 - Low poly model”\n" +
            "Daniel Zhabotinsky (DanielZhabotinsky)\n" +
            "sketchfab.com/models/25c6874d173a4f6ca63e150bbd505686" +
            AboutPage +
            "Prometni auti — CC BY 4.0\n" +
            "Izmijenjeni; licenca na stranici „3D modeli”.\n" +
            "\n" +
            "„Kiri '10 - Low poly model”\n" +
            "Daniel Zhabotinsky (DanielZhabotinsky)\n" +
            "sketchfab.com/models/7fd6e15785fa4aa9bfd6e31eb7c97ba6\n" +
            "\n" +
            "„Urban '10 - Low poly model”\n" +
            "Daniel Zhabotinsky (DanielZhabotinsky)\n" +
            "sketchfab.com/models/2866efdfa943484391ef8313768e074d\n" +
            "\n" +
            "„Ace '11 - Low Poly model”\n" +
            "Daniel Zhabotinsky (DanielZhabotinsky)\n" +
            "sketchfab.com/models/055ff8a21b8d4d279debca089e2fafcd" +
            AboutPage +
            "Prometni auti — CC BY 4.0\n" +
            "Izmijenjeni; licenca na stranici „3D modeli”.\n" +
            "\n" +
            "„LCT 3000 '07- Low poly model”\n" +
            "Daniel Zhabotinsky (DanielZhabotinsky)\n" +
            "sketchfab.com/models/3be03b6a43aa41898c9ca806b8787052\n" +
            "\n" +
            "„Volkswagen Golf IV”\n" +
            "Fratzica (vasilebetivu62)\n" +
            "sketchfab.com/models/3e40ac80ec044394999715a18e2dbe33\n" +
            "\n" +
            "„low-poly Škoda Octavia RS 2020”\n" +
            "D_U (DU1701)\n" +
            "sketchfab.com/models/d567807ce57e415ba9a1ffdf07e0cf5b" +
            AboutPage +
            "Prometni auti — CC BY 4.0\n" +
            "Izmijenjeni; licenca na stranici „3D modeli”.\n" +
            "\n" +
            "„2015 Mazda 6”\n" +
            "Dave Bored (Tyler_Dave)\n" +
            "sketchfab.com/models/24fb449e5f4243f49666ccf22d10c834\n" +
            "\n" +
            "„Dacia Duster Low Poly”\n" +
            "Danut_Stanciu (solid3ddd / solidmodelsproject)\n" +
            "sketchfab.com/models/ea275644cd634593b6a97eacf0ede405\n" +
            "\n" +
            "„Jeep Renegade 2016”\n" +
            "Jose Bronze (pinceladas3d)\n" +
            "sketchfab.com/models/192f7e81dce84c6780434f692a0f96c5" +
            AboutPage +
            "Prometni auti — CC BY 4.0\n" +
            "Izmijenjeni; licenca na stranici „3D modeli”.\n" +
            "\n" +
            "„Lowpoly Generic SUV”\n" +
            "mk2design\n" +
            "sketchfab.com/models/edc994ad28ed438cb365c0e0389ac177\n" +
            "\n" +
            "„Mercedes Benz Sprinter 2006”\n" +
            "Max (Max-7215; u metapodacima Max-5532)\n" +
            "sketchfab.com/models/f69de1315bb049c8946d57f6006acd73" +
            AboutPage +
            "Zvuk\n" +
            "\n" +
            "Motor, sirene, truba i zrak: Work With Sounds — Technical Museum of Slovenia, Werstas, Museum of Municipal Engineering (Kraków), Konrad Gutkowski. CC BY 4.0, izmijenjeno.\n" +
            "\n" +
            "Cvrčci noću: Glaneur de sons, CC BY 3.0, izmijenjeno.\n" +
            "Kiša po krovu: Beeld en Geluid, CC BY-SA 3.0, izmijenjeno (izvedenica pod istom licencom).\n" +
            "creativecommons.org/licenses/by/4.0/ · /by/3.0/ · /by-sa/3.0/" +
            AboutPage +
            "Zvuk — CC0 i javno dobro\n" +
            "\n" +
            "Kenney (kenney.nl), GGBotNet, Rango Mango, HaelDB, isaiah658, Thimras, qubodup, rubberduck, bmaczero, Helge Klaus Rieder, Nostrifikator, Tvabutzku1234, earthcalling, DavidDelon, cori, stephan, ezwa, Extemporalist.\n" +
            "\n" +
            "Wikimedia Commons, OpenGameArt. Ostalo: vlastita sinteza.";
        /// AboutText is shown in pages (the phone screen is short): the marker splits them
        const string AboutPage = "\f";
        int aboutPage;
    }

    /// self-made phone art (no real brand artwork): the wallpaper and white app glyphs as small generated textures
    public static class PhoneArt
    {
        static Texture2D wall, wallBlur, handset, bubble, person, box, map, bank, wifi;
        static Texture2D gear, beacon;
        /// Blicaj: a rotating-light dome on its base with three short flashes (no real police emblem)
        public static Texture2D Beacon => beacon ??= Make(96, (u, v) =>
        {
            float a = RRect(u, v, 0.5f, 0.2f, 0.34f, 0.07f, 0.03f);
            a = Mathf.Max(a, Ell(u, v, 0.5f, 0.27f, 0.22f, 0.36f) * (v > 0.27f ? 1 : 0));
            a *= 1 - Ell(u, v, 0.42f, 0.44f, 0.045f, 0.09f); // a glint
            a = Mathf.Max(a, Ell(u, v, 0.5f, 0.84f, 0.04f, 0.08f));
            a = Mathf.Max(a, Mathf.Max(Ell(u, v, 0.16f, 0.66f, 0.05f, 0.05f), Ell(u, v, 0.84f, 0.66f, 0.05f, 0.05f)));
            return a;
        });
        /// settings: an 8-tooth cog with a hole
        public static Texture2D Gear => gear ??= Make(96, (u, v) =>
        {
            float dx = u - 0.5f, dy = v - 0.5f, r = Mathf.Sqrt(dx * dx + dy * dy), a = Mathf.Atan2(dy, dx);
            float outer = Mathf.Cos(a * 8) > 0.25f ? 0.42f : 0.33f;
            return Mathf.Clamp01((outer - r) * 60f) * Mathf.Clamp01((r - 0.13f) * 60f);
        });
        // ---- the light apps' and the group page's glyphs (our own simple shapes)
        static Texture2D back, chevron, search, dots, lockT, photo, heart, heartLine, bubbleLine, share, house, play, people, bell, menu, close, check, walletT, arrowIn, arrowOut, shadeDown, cover;
        static float Seg(float u, float v, float ax, float ay, float bx, float by, float w)
        {
            var p = new Vector2(u, v); var a = new Vector2(ax, ay); var ab = new Vector2(bx, by) - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return S((p - (a + ab * t)).magnitude - w);
        }
        static float HeartF(float u, float v, float k)
        {
            float x = (u - 0.5f) * 2.5f * k, y = (v - 0.47f) * 2.5f * k;
            float q = x * x + y * y - 1, f = q * q * q - x * x * y * y * y;
            return Mathf.Clamp01(0.5f - f * 6);
        }
        public static Texture2D Back => back ??= Make(96, (u, v) => Mathf.Max(Seg(u, v, 0.64f, 0.88f, 0.3f, 0.5f, 0.075f), Seg(u, v, 0.3f, 0.5f, 0.64f, 0.12f, 0.075f)));
        public static Texture2D Chevron => chevron ??= Make(96, (u, v) => Mathf.Max(Seg(u, v, 0.36f, 0.88f, 0.7f, 0.5f, 0.075f), Seg(u, v, 0.7f, 0.5f, 0.36f, 0.12f, 0.075f)));
        public static Texture2D Search => search ??= Make(96, (u, v) => Mathf.Max(Ring(u, v, 0.43f, 0.57f, 0.26f, 0.065f), Seg(u, v, 0.62f, 0.38f, 0.86f, 0.14f, 0.08f)));
        public static Texture2D Dots => dots ??= Make(96, (u, v) => Mathf.Max(Ell(u, v, 0.18f, 0.5f, 0.09f, 0.09f), Mathf.Max(Ell(u, v, 0.5f, 0.5f, 0.09f, 0.09f), Ell(u, v, 0.82f, 0.5f, 0.09f, 0.09f))));
        public static Texture2D Lock => lockT ??= Make(96, (u, v) => Mathf.Max(RRect(u, v, 0.5f, 0.3f, 0.33f, 0.26f, 0.07f), Ring(u, v, 0.5f, 0.6f, 0.2f, 0.065f) * (v > 0.55f ? 1 : 0)));
        public static Texture2D Photo => photo ??= Make(96, (u, v) =>
        {
            float frame = Mathf.Clamp01(RRect(u, v, 0.5f, 0.5f, 0.44f, 0.36f, 0.09f) - RRect(u, v, 0.5f, 0.5f, 0.36f, 0.28f, 0.04f));
            float mtn = Mathf.Max(Tri(u, v, new Vector2(0.16f, 0.24f), new Vector2(0.58f, 0.24f), new Vector2(0.37f, 0.56f)), Tri(u, v, new Vector2(0.44f, 0.24f), new Vector2(0.84f, 0.24f), new Vector2(0.64f, 0.46f)));
            return Mathf.Max(frame, Mathf.Max(mtn, Ell(u, v, 0.68f, 0.64f, 0.07f, 0.07f)));
        });
        public static Texture2D Heart => heart ??= Make(96, (u, v) => HeartF(u, v, 1));
        public static Texture2D HeartLine => heartLine ??= Make(96, (u, v) => Mathf.Clamp01(HeartF(u, v, 1) - HeartF(u, v, 1.45f)));
        public static Texture2D BubbleLine => bubbleLine ??= Make(96, (u, v) => Mathf.Clamp01(Mathf.Max(Ell(u, v, 0.5f, 0.56f, 0.42f, 0.33f), Tri(u, v, new Vector2(0.22f, 0.32f), new Vector2(0.4f, 0.3f), new Vector2(0.14f, 0.12f))) - Ell(u, v, 0.5f, 0.56f, 0.32f, 0.23f)));
        public static Texture2D Share => share ??= Make(96, (u, v) => Mathf.Max(Seg(u, v, 0.14f, 0.3f, 0.55f, 0.55f, 0.07f), Tri(u, v, new Vector2(0.48f, 0.8f), new Vector2(0.62f, 0.3f), new Vector2(0.9f, 0.62f))));
        public static Texture2D House => house ??= Make(96, (u, v) => Mathf.Max(Tri(u, v, new Vector2(0.08f, 0.52f), new Vector2(0.92f, 0.52f), new Vector2(0.5f, 0.92f)), Mathf.Clamp01(RRect(u, v, 0.5f, 0.3f, 0.3f, 0.24f, 0.04f) - RRect(u, v, 0.5f, 0.17f, 0.08f, 0.13f, 0.02f))));
        public static Texture2D Play => play ??= Make(96, (u, v) => Mathf.Max(Mathf.Clamp01(RRect(u, v, 0.5f, 0.5f, 0.44f, 0.34f, 0.09f) - RRect(u, v, 0.5f, 0.5f, 0.36f, 0.26f, 0.05f)), Tri(u, v, new Vector2(0.42f, 0.66f), new Vector2(0.42f, 0.34f), new Vector2(0.66f, 0.5f))));
        public static Texture2D People => people ??= Make(96, (u, v) =>
        {
            float a = Mathf.Max(Ell(u, v, 0.36f, 0.66f, 0.14f, 0.14f), Mathf.Min(Ell(u, v, 0.36f, 0.16f, 0.27f, 0.27f), v > 0.1f ? 1 : 0));
            float b = Mathf.Max(Ell(u, v, 0.7f, 0.7f, 0.11f, 0.11f), Mathf.Min(Ell(u, v, 0.72f, 0.26f, 0.21f, 0.22f), v > 0.1f ? 1 : 0)) * (1 - Ell(u, v, 0.36f, 0.4f, 0.33f, 0.5f));
            return Mathf.Max(a, b);
        });
        public static Texture2D Bell => bell ??= Make(96, (u, v) => Mathf.Max(Mathf.Max(RRect(u, v, 0.5f, 0.5f, 0.27f, 0.27f, 0.22f), RRect(u, v, 0.5f, 0.26f, 0.38f, 0.05f, 0.03f)), Mathf.Max(Ell(u, v, 0.5f, 0.13f, 0.09f, 0.07f), Ell(u, v, 0.5f, 0.82f, 0.05f, 0.05f))));
        public static Texture2D Menu => menu ??= Make(96, (u, v) => (u > 0.14f && u < 0.86f ? 1 : 0) * Mathf.Max(Band(v, 0.25f, 0.045f), Mathf.Max(Band(v, 0.5f, 0.045f), Band(v, 0.75f, 0.045f))));
        public static Texture2D Close => close ??= Make(96, (u, v) => Mathf.Max(Seg(u, v, 0.18f, 0.18f, 0.82f, 0.82f, 0.07f), Seg(u, v, 0.18f, 0.82f, 0.82f, 0.18f, 0.07f)));
        public static Texture2D Check => check ??= Make(96, (u, v) => Mathf.Max(Seg(u, v, 0.14f, 0.5f, 0.4f, 0.24f, 0.08f), Seg(u, v, 0.4f, 0.24f, 0.86f, 0.76f, 0.08f)));
        public static Texture2D Wallet => walletT ??= Make(96, (u, v) => Mathf.Clamp01(RRect(u, v, 0.5f, 0.45f, 0.42f, 0.3f, 0.08f) - RRect(u, v, 0.8f, 0.45f, 0.14f, 0.09f, 0.04f) + Ell(u, v, 0.78f, 0.45f, 0.04f, 0.04f)));
        public static Texture2D ArrowIn => arrowIn ??= Make(96, (u, v) => Mathf.Max(Seg(u, v, 0.5f, 0.88f, 0.5f, 0.36f, 0.08f), Tri(u, v, new Vector2(0.22f, 0.44f), new Vector2(0.78f, 0.44f), new Vector2(0.5f, 0.12f))));
        public static Texture2D ArrowOut => arrowOut ??= Make(96, (u, v) => Mathf.Max(Seg(u, v, 0.5f, 0.12f, 0.5f, 0.64f, 0.08f), Tri(u, v, new Vector2(0.22f, 0.56f), new Vector2(0.78f, 0.56f), new Vector2(0.5f, 0.88f))));
        /// alpha 1 at the top to 0 at the bottom (a shade under the status bar)
        public static Texture2D ShadeDown => shadeDown ??= Make(32, (u, v) => v * v);
        /// the group page's cover: the picture rendered in the game (Resources/Phone/policija-naslovna.png: the police car parked by
        /// the D8 with cones, a player shot with "-carshow policija -carshowcones 4 -carshowhide -carcam …", cropped), or a drawn
        /// stand-in (to be redone when pedestrians / an officer exist)
        public static Texture2D PoliceCover => cover ? cover : cover = (Resources.Load<Texture2D>("Phone/policija-naslovna") ?? CoverFallback());

        /// a simple drawn cover: sky, the Kozjak ridge, the sea, the road with a white police car with a blue band and two cones
        static Texture2D CoverFallback()
        {
            int w = 312, h = 150; var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "policija cover (drawn)" };
            var px = new Color[w * h];
            Color sky0 = new Color(0.55f, 0.72f, 0.9f), sky1 = new Color(0.86f, 0.9f, 0.93f), hill = new Color(0.5f, 0.55f, 0.5f), road = new Color(0.33f, 0.33f, 0.35f), verge = new Color(0.62f, 0.6f, 0.52f);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float v = y / (float)h, u = x / (float)w; Color c = Color.Lerp(sky1, sky0, (v - 0.4f) / 0.6f);
                float ridge = 0.55f + 0.08f * Mathf.Sin(u * 7f + 0.6f) + 0.04f * Mathf.Sin(u * 19f);
                if (v < ridge) c = Color.Lerp(hill, new Color(0.42f, 0.47f, 0.42f), (ridge - v) * 3);
                if (v < 0.4f) c = verge;
                if (v < 0.34f) c = road;
                if (v < 0.34f && v > 0.18f && Mathf.Abs(v - 0.26f) < 0.008f && (x / 18) % 2 == 0) c = Color.white;
                if (v < 0.06f) c = verge * 0.9f;
                // the car: body, the blue band, windows, wheels, the light bar
                float cx = 150, cy = 50;
                if (x > cx - 70 && x < cx + 70 && y > cy - 6 && y < cy + 24) c = new Color(0.95f, 0.96f, 0.97f);
                if (x > cx - 70 && x < cx + 70 && y > cy + 2 && y < cy + 10) c = new Color(0.12f, 0.3f, 0.7f);
                if (x > cx - 44 && x < cx + 40 && y >= cy + 24 && y < cy + 44 - Mathf.Max(0, (x - (cx + 20)) * 0.9f) - Mathf.Max(0, ((cx - 30) - x) * 0.9f)) c = new Color(0.93f, 0.94f, 0.95f);
                if (x > cx - 36 && x < cx + 32 && y > cy + 27 && y < cy + 40 - Mathf.Max(0, (x - (cx + 16)) * 0.9f) - Mathf.Max(0, ((cx - 24) - x) * 0.9f) && Mathf.Abs(x - cx) > 2) c = new Color(0.15f, 0.2f, 0.26f);
                if (x > cx - 12 && x < cx + 12 && y >= cy + 44 && y < cy + 49) c = (x < cx) ? new Color(0.2f, 0.45f, 1f) : new Color(0.9f, 0.2f, 0.2f);
                float w1 = (x - (cx - 42)) * (x - (cx - 42)) + (y - (cy - 6)) * (y - (cy - 6)), w2 = (x - (cx + 44)) * (x - (cx + 44)) + (y - (cy - 6)) * (y - (cy - 6));
                if (w1 < 150 || w2 < 150) c = new Color(0.08f, 0.08f, 0.09f);
                if (w1 < 40 || w2 < 40) c = new Color(0.6f, 0.6f, 0.62f);
                // two cones
                for (int k = 0; k < 2; k++)
                {
                    float kx = 248 + k * 30, ky = 30 + k * 4, hh = 30, half = 10 * (1 - (y - ky) / hh);
                    if (y >= ky && y < ky + hh && Mathf.Abs(x - kx) < half) c = Mathf.Abs((y - ky) - 14) < 4 ? Color.white : new Color(1f, 0.45f, 0.1f);
                    if (y >= ky - 3 && y < ky && Mathf.Abs(x - kx) < 13) c = new Color(1f, 0.45f, 0.1f) * 0.8f;
                }
                c.a = 1; px[y * w + x] = c;
            }
            t.SetPixels(px); t.Apply(); return t;
        }

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
