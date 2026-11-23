// Mate's phone: the Blicaj app (the "Di stoje plavi – Kaštela" group, data and schedule in Blicaj.cs). The feed is chat bubbles
// (name in colour on top, grey for others, blue for Mate), newest at the bottom; "Starije" / "Novije" page through it.
// "Javi" reports at the spot nearest to the truck (radar / police stopping / all clear); replies come while the clock runs
// (the phone pauses it). The home icon carries the unread badge, the notification card shows the newest unread post, and a
// new alert (a report, or a reply to Mate) raises the same toast as a message. State: PhoneSave.blicaj.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.Game.Phone
{
    public partial class MatePhone
    {
        static readonly Color BlicajBlue = new Color(0.1f, 0.36f, 0.9f);
        static readonly string[] NickColors = { "#ffd60a", "#64d2ff", "#ff9f0a", "#30d158", "#bf5af2", "#ff6482", "#ac8e68", "#5e9eff" };

        /// the truck's position (nearest spot for Mate's report), the place under it (fallback text), and the app being opened
        /// (MagistralaGame.Blicaj.cs: reading the phone while driving fast)
        public Func<Vector3?> truckPos;
        public Func<string> placeName;
        public Action blicajOpened;

        public int BlicajUnread { get; private set; }
        BlicajSave B => data.blicaj ??= new BlicajSave();
        List<BlicajLine> blicajFeed;
        float blicajT, blicajSeen = -1;
        int blicajScroll, blicajShown;

        List<BlicajLine> BlicajLines() => blicajFeed ??= BlicajFeed.Build(B, clock());

        /// twice a second (unscaled): rebuild the feed, log and announce what arrived since the last look, count the unread
        void TickBlicaj()
        {
            if (Time.unscaledTime < blicajT) return;
            blicajT = Time.unscaledTime + 0.5f;
            float now = clock();
            if (B.readUpTo < 0) B.readUpTo = now - 45; // a new save: the last ¾ h waits as unread, so the app gets noticed
            blicajFeed = BlicajFeed.Build(B, now);
            BlicajLine note = null;
            if (blicajSeen >= 0 && now > blicajSeen)
                foreach (var l in blicajFeed)
                    if (!l.mate && l.at > blicajSeen && l.at <= now)
                    {
                        Debug.Log($"[blicaj] {Hm(l.at)} {l.who}: {l.text}");
                        if (now - l.at < 15 && (l.alert || l.toMate)) note = l; // (not after a clock jump: end of day, -clock)
                    }
            blicajSeen = now;
            bool reading = Open && Current == Screen.Blicaj;
            if (note != null && !reading) toast?.Invoke($"{BlicajFeed.App} · {note.who}: {Cut(note.text, 60)}");
            if (reading) B.readUpTo = Mathf.Max(B.readUpTo, now);
            int n = 0; foreach (var l in blicajFeed) if (!l.mate && l.at > B.readUpTo) n++;
            BlicajUnread = n;
        }

        BlicajLine LatestUnreadBlicaj()
        {
            var f = blicajFeed; if (f == null) return null;
            for (int i = f.Count - 1; i >= 0; i--) if (!f[i].mate && f[i].at > B.readUpTo) return f[i];
            return null;
        }

        void BlicajOpened() { blicajScroll = 0; blicajOpened?.Invoke(); }

        /// tests (-blicajdemo): eight posts from the last hour and one report of Mate's a quarter of an hour ago, all unread
        public void BlicajDemo()
        {
            float now = clock();
            BlicajFeed.Demo(B, now, 8);
            var sp = HereSpot();
            B.mine.Add(new BlicajReport { at = now - 14, spot = sp?.id ?? "", where = HereText(sp), kind = 0 });
            B.readUpTo = now - 61; blicajFeed = null; blicajT = 0;
            Debug.Log($"[blicaj] demo: {B.forced.Count} test posts, Mate's report {HereText(sp)}");
        }

        BlicajSpot HereSpot() => truckPos?.Invoke() is Vector3 p ? BlicajFeed.Nearest(p, 1200) : null;
        string HereText(BlicajSpot sp)
        {
            if (sp != null) return sp.where;
            string pl = placeName?.Invoke();
            return string.IsNullOrEmpty(pl) ? "kod mene" : $"kod mene ({pl})";
        }

        static string NickColor(string who) { int h = 0; foreach (char ch in who) h = h * 31 + ch; return NickColors[(h & 0x7fffffff) % NickColors.Length]; }

        string Stamp(float at) => Mathf.FloorToInt(at / 1440) < Mathf.FloorToInt(clock() / 1440) ? $"jučer {Hm(at)}" : Hm(at);

        void DrawBlicaj(Rect a)
        {
            var feed = BlicajLines();
            B.readUpTo = Mathf.Max(B.readUpTo, clock()); BlicajUnread = 0;
            float y = Title(a, BlicajFeed.App, $"{BlicajFeed.Group} · {BlicajFeed.Members} članova");
            var sp = HereSpot();
            items.Add(("Javi plave ili radar", $"di si sad: {HereText(sp)}", () => { Current = Screen.BlicajJavi; sel = 0; }, BlicajBlue));
            blicajScroll = Mathf.Clamp(blicajScroll, 0, Mathf.Max(0, feed.Count - 1));
            if (blicajScroll + blicajShown < feed.Count) items.Add(("Starije", null, () => blicajScroll += Mathf.Max(1, blicajShown - 1), null));
            if (blicajScroll > 0) items.Add(("Novije", null, () => blicajScroll = Mathf.Max(0, blicajScroll - Mathf.Max(1, blicajShown - 1)), null));
            items.Add(("Natrag", null, () => { Current = Screen.Home; sel = 0; }, null));
            float ih = 0; var st = new GUIStyle(item) { font = semi };
            foreach (var it in items) ih += st.CalcHeight(new GUIContent(it.label + (it.sub != null ? $"\n<size=12>{it.sub}</size>" : "")), a.width) + 6;
            var list = new List<(bool, string, string)>();
            for (int i = 0; i < feed.Count - blicajScroll; i++)
            {
                var l = feed[i];
                list.Add((l.mate, l.mate ? l.text : $"<b><color={NickColor(l.who)}>{l.who}</color></b>\n{l.text}", Stamp(l.at)));
            }
            if (list.Count == 0) GUI.Label(new Rect(a.x, y, a.width, 40), "Još niko ništa nije javija.", small);
            int drawn = Bubbles(a, y, a.yMax - ih - 8, list);
            if (Event.current.type == EventType.Repaint) blicajShown = drawn;
            List(a, a.yMax - ih);
        }

        void DrawBlicajJavi(Rect a)
        {
            var sp = HereSpot(); string where = HereText(sp);
            float y = Title(a, "Javi grupi", $"di si sad: {where}");
            GUI.Label(new Rect(a.x, y, a.width, 18), $"Tvoje dojave: {B.mine.Count} · zahvale: {BlicajFeed.ThanksCount(BlicajLines())}", St(13, true, new Color(1f, 0.82f, 0.45f)));
            GUI.Label(new Rect(a.x, y + 22, a.width, 34), "Javi samo ono šta vidiš. Grupa pamti ko laže.", small);
            y += 60;
            for (int k = 0; k < BlicajFeed.ReportLabels.Length; k++)
            {
                int kk = k;
                items.Add((BlicajFeed.ReportLabels[k], null, () => Report(kk, sp, where), k == 2 ? new Color(0.2f, 0.7f, 0.35f) : BlicajBlue));
            }
            items.Add(("Natrag", null, () => { Current = Screen.Blicaj; sel = 0; }, null));
            List(a, y);
        }

        void Report(int kind, BlicajSpot sp, string where)
        {
            float now = clock();
            var last = B.mine.Count > 0 ? B.mine[B.mine.Count - 1] : null;
            if (last != null && now - last.at < 15) { toast?.Invoke($"{BlicajFeed.App}: već si javio maloprije. Pusti i druge."); Current = Screen.Blicaj; sel = 0; return; }
            B.mine.Add(new BlicajReport { at = now, spot = sp?.id ?? "", where = where, kind = kind });
            if (B.mine.Count > 30) B.mine.RemoveAt(0);
            blicajFeed = null; blicajScroll = 0; Current = Screen.Blicaj; sel = 0;
            Debug.Log($"[blicaj] Mate reports {BlicajFeed.ReportLabels[kind]} at {sp?.id ?? where}");
            changed?.Invoke();
        }
    }
}
