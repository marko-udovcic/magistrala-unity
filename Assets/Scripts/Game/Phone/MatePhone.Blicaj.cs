// Mate's phone: the "Policija Nas Zaustavlja" app (was Blicaj; the group's data and schedule in Blicaj.cs), laid out like a big
// regional group page (owner 2026-11-25, his reference "policija zaustavlja split i okolica"), in OUR neutral look – white page,
// dark text, our orange accent, our own drawn icons; no real network's logo, wordmark, colours or icons:
//   the cover (rendered in the game: Resources/Phone/policija-naslovna, a drawn fallback) with ‹ / search / more over it, the
//   group name in big bold capitals, "Privatna · 51,0 tis. članova · Pozovi", the chips (Istaknuto, Reels, Vi, Informacije),
//   the composer ("Napišite nešto…": Mate's report, the Javi sheet), "Nove objave": the posts newest first – a round avatar,
//   the name, "12 min" and the group glyph, the text large (short posts larger), the likes, comments and share, the first
//   comment – and the bottom tab bar (icons only). The page scrolls (wheel, ↑ ↓ through the posts; Enter likes one).
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
        /// the app icon (our own: a beacon on a deep red-orange, nothing like a real network's icon)
        static readonly Color PoliceIconCol = new Color32(0xC8, 0x3E, 0x2A, 0xff);
        static readonly Color FbBand = new Color32(0xE9, 0xEA, 0xEE, 0xff), FbChip = new Color32(0xEC, 0xED, 0xF1, 0xff), FbBubble = new Color32(0xF0, 0xF1, 0xF4, 0xff);
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

        /// the patrols the group reported in the last 1½ h (for the M map): where, the post, minutes ago
        public List<BlicajLine> KnownPolice() => BlicajFeed.KnownPolice(BlicajLines(), clock());

        static string Ago(float min)
        {
            if (min < 1) return "Upravo";
            if (min < 60) return $"{Mathf.FloorToInt(min)} min";
            if (min < 1440) return $"{Mathf.FloorToInt(min / 60)} h";
            return min < 2880 ? "Jučer" : $"{Mathf.FloorToInt(min / 1440)} d";
        }
        static Color NickCol(string who) { ColorUtility.TryParseHtmlString(NickColor(who), out var c); return Color.Lerp(c, new Color(0.25f, 0.25f, 0.3f), 0.25f); }
        static int Likes(BlicajLine l, int comments)
        {
            int h = 17; foreach (char ch in l.text) h = h * 31 + ch; h = (h ^ Mathf.RoundToInt(l.at * 7)) & 0x7fffffff;
            if (l.mate) return comments;
            return l.alert ? 4 + h % 90 : h % 25;
        }
        readonly HashSet<long> liked = new HashSet<long>();
        static long PostKey(BlicajLine l) => ((long)Mathf.RoundToInt(l.at * 10) << 20) ^ l.who.GetHashCode();

        /// the group page; returns whether the status bar is dark (scrolled past the cover)
        bool DrawBlicaj(Rect scr)
        {
            var feed = BlicajLines();
            B.readUpTo = Mathf.Max(B.readUpTo, clock()); BlicajUnread = 0;
            float now = clock();
            Fill(scr, Color.white, 46);
            // posts newest first, with their comments
            var posts = new List<BlicajLine>(); var com = new Dictionary<BlicajLine, List<BlicajLine>>();
            foreach (var l in feed)
            {
                if (!l.reply) { posts.Add(l); continue; }
                if (l.parent == null) continue;
                if (!com.TryGetValue(l.parent, out var cl)) com[l.parent] = cl = new List<BlicajLine>();
                cl.Add(l);
            }
            posts.Reverse();
            if (posts.Count > 40) posts.RemoveRange(40, posts.Count - 40);
            var sp = HereSpot();
            items.Add(("Napišite nešto…", HereText(sp), () => Go(Screen.BlicajJavi), Acc));
            foreach (var p in posts) { var pp = p; items.Add((p.who, null, () => ToggleLike(pp), null)); }
            Scroll(scr, scr.y, 74, w =>
            {
                float y = 0;
                // the cover photo (rounded top), a soft shade under the status bar
                var cov = new Rect(0, 0, w, 150);
                GUI.DrawTexture(cov, PhoneArt.PoliceCover, ScaleMode.ScaleAndCrop, false, 0, Color.white, Vector4.zero, new Vector4(46, 46, 0, 0));
                GUI.DrawTexture(new Rect(0, 0, w, 60), PhoneArt.ShadeDown, ScaleMode.StretchToFill, true, 0, new Color(0, 0, 0, 0.55f), Vector4.zero, new Vector4(46, 46, 0, 0));
                y = 162;
                var ns = St(23, true, LText, TextAnchor.UpperLeft, true);
                float nh = ns.CalcHeight(new GUIContent(BlicajFeed.Group), w - 32);
                GUI.Label(new Rect(16, y, w - 32, nh), BlicajFeed.Group, ns); y += nh + 6;
                Glyph(new Rect(16, y + 3, 12, 12), PhoneArt.Lock, LSub);
                var ms = St(13, false, LSub, TextAnchor.MiddleLeft, false); string meta = $"Privatna · {BlicajFeed.MembersText} članova · ";
                GUI.Label(new Rect(32, y, w - 48, 18), meta, ms);
                GUI.Label(new Rect(32 + TextW(ms, meta), y, 80, 18), "Pozovi", St(13, true, LText, TextAnchor.MiddleLeft, false));
                y += 28;
                // members' avatars and the joined / invite buttons
                for (int i = 0; i < 6; i++) { var r = new Rect(16 + i * 18, y, 26, 26); Fill(new Rect(r.x - 2, r.y - 2, 30, 30), Color.white, 15); AvatarC(r, NickCol(DupName(i)), DupName(i)); }
                y += 36;
                var jb = new Rect(16, y, (w - 40) / 2, 34); var ib = new Rect(jb.xMax + 8, y, (w - 40) / 2, 34);
                Fill(jb, FbChip, 8); GUI.Label(jb, "Pridružen", St(13, true, LText, TextAnchor.MiddleCenter, false));
                Fill(ib, Acc, 8); GUI.Label(ib, "+ Pozovi", St(13, true, Color.white, TextAnchor.MiddleCenter, false));
                y += 46;
                // chips (wider than the screen, as on the real page: they run off the edge)
                float cx = 16; var cs = St(13, true, LText, TextAnchor.MiddleCenter, false);
                foreach (var chip in new[] { "Istaknuto", "Reels", "Vi", "Informacije" })
                {
                    float cw = TextW(cs, chip) + 26; var r = new Rect(cx, y, cw, 32);
                    Fill(r, FbChip, 16); GUI.Label(r, chip, cs); cx += cw + 8;
                }
                y += 44;
                Fill(new Rect(0, y, w, 8), FbBand, 0); y += 8;
                // the composer (item 0: Mate's report)
                {
                    var r = new Rect(0, y, w, 60);
                    if (sel == 0) { Fill(r, LSel, 0); selTop = y; selBot = y + 60; }
                    AvatarC(new Rect(14, y + 12, 36, 36), Acc, "Mate");
                    var fr = new Rect(58, y + 13, w - 58 - 50, 34);
                    Ring(fr, new Color32(0xCF, 0xD0, 0xD6, 0xff), 1, 17); Fill(Shrink(fr, 1), Color.white, 16);
                    GUI.Label(new Rect(fr.x + 14, fr.y, fr.width - 20, fr.height), "Napišite nešto…", St(14, false, LSub, TextAnchor.MiddleLeft, false));
                    Glyph(new Rect(w - 40, y + 19, 24, 22), PhoneArt.Photo, Green);
                    if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { sel = 0; Go(Screen.BlicajJavi); }
                    y += 60;
                }
                Fill(new Rect(0, y, w, 8), FbBand, 0); y += 8;
                GUI.Label(new Rect(16, y, w - 32, 44), "Nove objave", St(17, true, LText, TextAnchor.MiddleLeft, false)); y += 44;
                if (posts.Count == 0) { GUI.Label(new Rect(16, y, w - 32, 40), "Još niko ništa nije javija.", St(14, false, LSub)); y += 50; }
                for (int i = 0; i < posts.Count; i++)
                    y = Post(posts[i], com.TryGetValue(posts[i], out var cl) ? cl : null, i + 1, y, w, now);
                return y;
            });
            float sc = scroll;
            // the top bar: over the cover round translucent buttons, scrolled past it a white bar with the group's name
            bool bar = sc > 112;
            if (bar)
            {
                var tb = new Rect(scr.x, scr.y, scr.width, 86);
                GUI.DrawTexture(tb, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Color.white, Vector4.zero, new Vector4(46, 46, 0, 0));
                Line(scr.x, tb.yMax - 1, scr.width, LSep);
            }
            Color ic = bar ? LText : Color.white, bg = bar ? new Color(0, 0, 0, 0) : new Color(0, 0, 0, 0.45f);
            var bk = new Rect(scr.x + 12, scr.y + 46, 32, 32);
            Fill(bk, bg, 16); Glyph(Shrink(bk, 8), PhoneArt.Back, ic);
            if (GUI.Button(bk, GUIContent.none, GUIStyle.none)) Back();
            var se = new Rect(scr.xMax - 84, scr.y + 46, 32, 32); Fill(se, bg, 16); Glyph(Shrink(se, 8), PhoneArt.Search, ic);
            var mo = new Rect(scr.xMax - 44, scr.y + 46, 32, 32); Fill(mo, bg, 16); Glyph(Shrink(mo, 7), PhoneArt.Dots, ic);
            if (bar) { var ts = St(14, true, LText, TextAnchor.MiddleLeft, false); GUI.Label(new Rect(bk.xMax + 8, bk.y, se.x - bk.xMax - 12, 32), Fit(ts, BlicajFeed.Group, se.x - bk.xMax - 12), ts); }
            // the bottom tab bar: icons only (home, video, the group – active –, notifications, menu)
            var tbb = new Rect(scr.x, scr.yMax - 74, scr.width, 74);
            GUI.DrawTexture(tbb, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, Color.white, Vector4.zero, new Vector4(0, 0, 46, 46));
            Line(scr.x, tbb.y, scr.width, LSep);
            var tabs = new[] { PhoneArt.House, PhoneArt.Play, PhoneArt.People, PhoneArt.Bell, PhoneArt.Menu };
            float tw = scr.width / tabs.Length;
            for (int i = 0; i < tabs.Length; i++)
            {
                var r = new Rect(scr.x + i * tw + (tw - 24) / 2, tbb.y + 12, 24, 24);
                Glyph(r, tabs[i], i == 2 ? Acc : new Color32(0x65, 0x67, 0x6B, 0xff));
                if (i == 2) Fill(new Rect(scr.x + i * tw + 10, tbb.y, tw - 20, 3), Acc, 1.5f);
                if (i == 0 && GUI.Button(new Rect(scr.x + i * tw, tbb.y, tw, 44), GUIContent.none, GUIStyle.none)) Back();
            }
            if (Event.current.type == EventType.Repaint) blicajShown = posts.Count;
            return bar;
        }

        static string DupName(int i) { string[] n = { "Ante K.", "Mare", "Teta Neda", "Bepo", "Šime B.", "Lucija" }; return n[i % n.Length]; }

        void ToggleLike(BlicajLine l) { long k = PostKey(l); if (!liked.Remove(k)) liked.Add(k); }

        /// one post card; returns the y after it
        float Post(BlicajLine l, List<BlicajLine> comments, int idx, float y, float w, float now)
        {
            float y0 = y;
            int nc = comments?.Count ?? 0;
            bool mine = liked.Contains(PostKey(l));
            int likes = Likes(l, nc) + (mine ? 1 : 0);
            // header: avatar, name, "12 min · group"
            AvatarC(new Rect(12, y + 12, 38, 38), l.mate ? Acc : NickCol(l.who), l.who);
            GUI.Label(new Rect(58, y + 12, w - 100, 20), l.who, St(14, true, LText, TextAnchor.MiddleLeft, false));
            var ms = St(12, false, LSub, TextAnchor.MiddleLeft, false); string ago = Ago(now - l.at) + " · ";
            GUI.Label(new Rect(58, y + 31, 120, 16), ago, ms);
            Glyph(new Rect(58 + TextW(ms, ago), y + 33, 13, 12), PhoneArt.People, LSub);
            Glyph(new Rect(w - 34, y + 18, 18, 18), PhoneArt.Dots, LSub);
            y += 60;
            // the text: large (short posts larger still)
            var ts = St(l.text.Length <= 70 ? 21 : 16, false, LText, TextAnchor.UpperLeft, true);
            float th = ts.CalcHeight(new GUIContent(l.text), w - 28);
            GUI.Label(new Rect(14, y, w - 28, th), l.text, ts); y += th + 12;
            // counts
            if (likes > 0)
            {
                var hr = new Rect(14, y + 4, 18, 18); Fill(hr, Acc, 9); Glyph(Shrink(hr, 4), PhoneArt.Heart, Color.white);
                GUI.Label(new Rect(38, y, 80, 26), likes.ToString(), St(13, false, LSub, TextAnchor.MiddleLeft, false));
            }
            if (nc > 0) GUI.Label(new Rect(w - 160, y, 146, 26), nc % 10 == 1 && nc % 100 != 11 ? $"{nc} komentar" : $"{nc} komentara", St(13, false, LSub, TextAnchor.MiddleRight, false));
            y += 28;
            Line(14, y, w - 28, LSep); y += 2;
            // like / comment / share
            float aw = (w - 16) / 3; var asg = St(13, true, new Color32(0x65, 0x67, 0x6B, 0xff), TextAnchor.MiddleLeft, false);
            var acts = new[] { (PhoneArt.HeartLine, "Sviđa mi se"), (PhoneArt.BubbleLine, "Komentiraj"), (PhoneArt.Share, "Podijeli") };
            for (int i = 0; i < 3; i++)
            {
                var (g, t) = acts[i]; bool on = i == 0 && mine;
                float lw = TextW(asg, t) + 24; float ax = 8 + i * aw + (aw - lw) / 2;
                Glyph(new Rect(ax, y + 11, 18, 18), on ? PhoneArt.Heart : g, on ? Acc : (Color)new Color32(0x65, 0x67, 0x6B, 0xff));
                GUI.Label(new Rect(ax + 24, y, lw, 40), t, on ? St(13, true, Acc, TextAnchor.MiddleLeft, false) : asg);
                if (i == 0 && GUI.Button(new Rect(8 + i * aw, y, aw, 40), GUIContent.none, GUIStyle.none)) { sel = idx; ToggleLike(l); }
            }
            y += 40;
            // the first comment (more: a grey line)
            if (nc > 0)
            {
                var c = comments[0];
                if (nc > 1) { GUI.Label(new Rect(14, y, w - 28, 18), $"Prikaži još {nc - 1}…", St(12, true, LSub, TextAnchor.MiddleLeft, false)); y += 22; }
                AvatarC(new Rect(14, y + 2, 28, 28), c.mate ? Acc : NickCol(c.who), c.who);
                var cs = St(13, false, LText, TextAnchor.UpperLeft, true);
                float bw = w - 64, ch = cs.CalcHeight(new GUIContent(c.text), bw - 20);
                var br = new Rect(48, y, bw, ch + 30);
                Fill(br, FbBubble, 14);
                GUI.Label(new Rect(br.x + 10, br.y + 6, bw - 20, 16), c.who, St(12, true, LText, TextAnchor.MiddleLeft, false));
                GUI.Label(new Rect(br.x + 10, br.y + 23, bw - 20, ch), c.text, cs);
                GUI.Label(new Rect(br.x + 10, br.yMax + 2, 120, 14), Ago(now - c.at), St(11, false, LSub, TextAnchor.MiddleLeft, false));
                y += br.height + 22;
            }
            var card = new Rect(0, y0, w, y - y0);
            if (idx == sel) { Ring(new Rect(2, y0 + 2, w - 4, y - y0 - 4), Acc, 2, 8); selTop = y0; selBot = y; }
            if (Event.current.type == EventType.MouseDown && card.Contains(Event.current.mousePosition)) sel = idx;
            Fill(new Rect(0, y, w, 8), FbBand, 0);
            return y + 8;
        }

        /// the composer sheet: where Mate is, his reports so far, the three kinds of report
        void DrawBlicajJavi(Rect scr)
        {
            var sp = HereSpot(); string where = HereText(sp);
            Fill(scr, Color.white, 46);
            float y = scr.y + 44;
            var x0 = new Rect(scr.x + 12, y, 30, 30);
            Glyph(Shrink(x0, 7), PhoneArt.Close, LText);
            if (GUI.Button(x0, GUIContent.none, GUIStyle.none)) Back();
            GUI.Label(new Rect(scr.x, y, scr.width, 30), "Kreiraj objavu", St(16, true, LText, TextAnchor.MiddleCenter, false));
            y += 38; Line(scr.x, y, scr.width, LSep); y += 12;
            AvatarC(new Rect(scr.x + 16, y, 40, 40), Acc, "Mate");
            GUI.Label(new Rect(scr.x + 66, y + 1, scr.width - 80, 20), BlicajFeed.MateNick, St(15, true, LText, TextAnchor.MiddleLeft, false));
            var gs = St(12, false, LSub, TextAnchor.MiddleLeft, false);
            GUI.Label(new Rect(scr.x + 66, y + 21, scr.width - 80, 16), Fit(gs, "u grupi " + BlicajFeed.App, scr.width - 80), gs);
            y += 52;
            var ws = St(15, false, LText);
            string wt = $"Di si sad: {where}";
            float wh = ws.CalcHeight(new GUIContent(wt), scr.width - 32);
            GUI.Label(new Rect(scr.x + 16, y, scr.width - 32, wh), wt, ws); y += wh + 6;
            GUI.Label(new Rect(scr.x + 16, y, scr.width - 32, 18), $"Tvoje dojave: {B.mine.Count} · zahvale: {BlicajFeed.ThanksCount(BlicajLines())}", St(13, true, Acc, TextAnchor.MiddleLeft, false)); y += 22;
            GUI.Label(new Rect(scr.x + 16, y, scr.width - 32, 34), "Javi samo ono šta vidiš. Grupa pamti ko laže.", St(12, false, LSub)); y += 40;
            Line(scr.x, y, scr.width, LSep); y += 4;
            var icons = new[] { (PhoneArt.Beacon, Acc), (PhoneArt.Beacon, PoliceIconCol), (PhoneArt.Check, Green) };
            for (int k = 0; k < BlicajFeed.ReportLabels.Length; k++)
            {
                int kk = k;
                items.Add((BlicajFeed.ReportLabels[k], null, () => Report(kk, sp, where), null));
                var r = new Rect(scr.x, y, scr.width, 54);
                if (sel == k) Fill(r, LSel, 0);
                var ir = new Rect(r.x + 16, r.y + 9, 36, 36); Fill(ir, icons[k].Item2, 18); Glyph(Shrink(ir, 8), icons[k].Item1, Color.white);
                GUI.Label(new Rect(r.x + 64, r.y, r.width - 80, 54), BlicajFeed.ReportLabels[k], St(15, true, LText, TextAnchor.MiddleLeft, false));
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { sel = k; Report(kk, sp, where); }
                y += 54; Line(scr.x + 64, y - 1, scr.width - 64, LSep);
            }
        }

        void Report(int kind, BlicajSpot sp, string where)
        {
            float now = clock();
            var last = B.mine.Count > 0 ? B.mine[B.mine.Count - 1] : null;
            if (last != null && now - last.at < 15) { toast?.Invoke($"{BlicajFeed.App}: već si javio maloprije. Pusti i druge."); Go(Screen.Blicaj); return; }
            B.mine.Add(new BlicajReport { at = now, spot = sp?.id ?? "", where = where, kind = kind });
            if (B.mine.Count > 30) B.mine.RemoveAt(0);
            blicajFeed = null; blicajScroll = 0; Go(Screen.Blicaj);
            Debug.Log($"[blicaj] Mate reports {BlicajFeed.ReportLabels[kind]} at {sp?.id ?? where}");
            changed?.Invoke();
        }
    }
}
