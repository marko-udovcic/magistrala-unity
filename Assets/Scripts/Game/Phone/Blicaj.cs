// "Blicaj" – a phone app with the local chat group "Di stoje plavi – Kaštela" (IDEJE_IGRA.md §7, accepted instead of a radio):
// people report where the police / a radar stand, bicker, post false alarms, yesterday's radar, cats and figs. Text only.
// The app and the group are invented (no real network, name or logo). Posts are in Dalmatian ikavica on purpose: do NOT
// "correct" them to standard Croatian.
//   schedule: per game day, seeded by the day (deterministic, so nothing but the read cursor and Mate's own reports is saved):
//   12–18 posts picked without repeats from Posts, each at a random time in its hour window, replies a few minutes after it.
//   places: {m} in a text is a spot phrase ("na D8 iznad Starog"); the spots sit on the real D8 / old road (Ž6137) through each
//   Kaštel, or at existing game stops (pump, cement works, marina), positions resolved from the GameWorld at start
//   (MagistralaGame.Blicaj.cs). True reports ('r') name the day's patrol spot (one in the morning, one after noon: PatrolSpot,
//   the hook for the moving tripod radar of §6); false alarms ('l') and traffic posts ('g') pick any spot.
//   Mate reports from the app at the spot nearest to the truck; replies (thanks, and a doubter when no patrol is there) follow.
// Drawing and the badge: MatePhone.Blicaj.cs; the save: PhoneSave.blicaj (BlicajSave).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.Game.Phone
{
    [Serializable] public class BlicajReport { public float at; public string spot = "", where = ""; public int kind; }
    [Serializable] public class BlicajForced { public int post; public float at; public string spot = ""; }
    [Serializable]
    public class BlicajSave
    {
        /// everything up to this game minute counts as read (−1 = not yet initialised: the first look leaves the last ¾ h unread)
        public float readUpTo = -1;
        /// Mate's own reports (the last 30)
        public List<BlicajReport> mine = new List<BlicajReport>();
        /// test posts (-blicajdemo) outside the day's schedule
        public List<BlicajForced> forced = new List<BlicajForced>();
    }

    public class BlicajSpot
    {
        public string id, where;    // where: the phrase used in the posts
        public char road;           // 'd' D8, 's' the old road (Ž6137), 'p' an existing game stop
        public string key;          // the place name (GameWorld.placeNames) for 'd' / 's', the stop id for 'p'
        public Vector3 pos; public bool ok;
    }

    /// one line of the feed (a post, a reply, or Mate's report)
    public class BlicajLine { public string who, text; public float at; public bool mate, reply, toMate, doubt, alert; }

    public static class BlicajFeed
    {
        public const string App = "Blicaj", Group = "Di stoje plavi – Kaštela", MateNick = "Mate B.";
        public const int Members = 1287;

        // ------------------------------------------------------------------ spots
        public static readonly BlicajSpot[] Spots =
        {
            S("d8-stafilic", "na D8 iznad Štafilića", 'd', "Kaštel Štafilić"),
            S("d8-novi", "na D8 iznad Novog", 'd', "Kaštel Novi"),
            S("d8-stari", "na D8 iznad Starog", 'd', "Kaštel Stari"),
            S("d8-luksic", "na D8 iznad Lukšića", 'd', "Kaštel Lukšić"),
            S("d8-kambelovac", "na D8 iznad Kambelovca", 'd', "Kaštel Kambelovac"),
            S("d8-gomilica", "na D8 kod Gomilice", 'd', "Kaštel Gomilica"),
            S("d8-sucurac", "na D8 kod Sućurca", 'd', "Kaštel Sućurac"),
            S("st-stafilic", "na staroj cesti u Štafiliću", 's', "Kaštel Štafilić"),
            S("st-novi", "na staroj cesti u Novom", 's', "Kaštel Novi"),
            S("st-stari", "na staroj cesti u Starom", 's', "Kaštel Stari"),
            S("st-luksic", "na staroj cesti u Lukšiću", 's', "Kaštel Lukšić"),
            S("st-kambelovac", "na staroj cesti u Kambelovcu", 's', "Kaštel Kambelovac"),
            S("st-gomilica", "na staroj cesti u Gomilici", 's', "Kaštel Gomilica"),
            S("st-sucurac", "na staroj cesti u Sućurcu", 's', "Kaštel Sućurac"),
            S("pumpa-luksic", "kod pumpe u Lukšiću", 'p', "kastela_pumpa"),
            S("pumpa-marija", "kod Marijine pumpe", 'p', "kastela_pumpa_2"),
            S("cementara", "kod cementare", 'p', "cementara_sucurac"),
            S("marina", "kod marine", 'p', "p44"),
            S("konjanik", "kod konjanika u Starom", 'p', "p40"),
            S("riva-stari", "na rivi u Starom", 'p', "p42"),
        };
        static BlicajSpot S(string id, string where, char road, string key) => new BlicajSpot { id = id, where = where, road = road, key = key };

        public static BlicajSpot Spot(string id) { foreach (var s in Spots) if (s.id == id) return s; return null; }
        static List<BlicajSpot> Ok(bool roadsOnly) { var l = new List<BlicajSpot>(); foreach (var s in Spots) if (s.ok && (!roadsOnly || s.road != 'p')) l.Add(s); return l; }

        /// the spot nearest to p within maxM metres (horizontal), null = none
        public static BlicajSpot Nearest(Vector3 p, float maxM)
        {
            BlicajSpot best = null; float bd = maxM * maxM;
            foreach (var s in Spots)
            {
                if (!s.ok) continue;
                float dx = s.pos.x - p.x, dz = s.pos.z - p.z, d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = s; }
            }
            return best;
        }

        /// where the patrol of the day stands (morning before 12 h / afternoon), null = no spot resolved. The future tripod radar
        /// (§6) can read this so the group's true reports and the real police agree.
        public static BlicajSpot PatrolSpot(int day, float hour)
        {
            var roads = Ok(true); if (roads.Count == 0) return null;
            var rnd = new System.Random(day * 3571 + 7);
            int a = rnd.Next(roads.Count), b = rnd.Next(roads.Count);
            if (b == a && roads.Count > 1) b = (b + 1 + rnd.Next(roads.Count - 1)) % roads.Count;
            return roads[hour < 12 ? a : b];
        }

        // ------------------------------------------------------------------ the day's schedule
        struct Sched { public int post; public float at; public string spot; }
        static readonly Dictionary<int, List<Sched>> cache = new Dictionary<int, List<Sched>>();
        /// spots resolve after the first schedule may have been built (tests): drop the cache then
        public static void Reset() => cache.Clear();

        static List<Sched> Schedule(int day)
        {
            if (cache.TryGetValue(day, out var l)) return l;
            if (cache.Count > 6) cache.Clear();
            l = new List<Sched>();
            var rnd = new System.Random(day * 7349 + 101);
            int n = (day % 7 == 5 ? 12 : 14) + rnd.Next(5);
            var order = new int[Posts.Length]; for (int i = 0; i < order.Length; i++) order[i] = i;
            for (int i = order.Length - 1; i > 0; i--) { int j = rnd.Next(i + 1); (order[i], order[j]) = (order[j], order[i]); }
            var all = Ok(false);
            for (int k = 0; k < n && k < order.Length; k++)
            {
                var p = Posts[order[k]];
                float at = day * 1440 + (p.h0 + (float)rnd.NextDouble() * (p.h1 - p.h0)) * 60;
                double r = rnd.NextDouble(); // (drawn for every post, so the times do not depend on which spots resolved)
                string spot = "";
                if (p.kind == 'r') spot = PatrolSpot(day, (at % 1440) / 60f)?.id ?? "";
                else if ((p.kind == 'l' || p.kind == 'g') && all.Count > 0)
                {
                    var pick = all[(int)(r * all.Count) % all.Count];
                    var pat = PatrolSpot(day, (at % 1440) / 60f);
                    if (p.kind == 'l' && pat != null && pick == pat && all.Count > 1) pick = all[((int)(r * all.Count) + 1) % all.Count]; // a false alarm is false
                    spot = pick.id;
                }
                l.Add(new Sched { post = order[k], at = at, spot = spot });
            }
            l.Sort((x, y) => x.at.CompareTo(y.at));
            cache[day] = l;
            return l;
        }

        static string Fill(string t, string spot)
        {
            if (t.IndexOf("{m}", StringComparison.Ordinal) < 0) return t;
            var s = Spot(spot);
            return t.Replace("{m}", s != null ? s.where : "na D8");
        }

        static void AddPost(List<BlicajLine> o, Post p, float at, string spot)
        {
            o.Add(new BlicajLine { who = p.who, text = Fill(p.text, spot), at = at, alert = p.kind == 'r' || p.kind == 'l' });
            float t = at;
            for (int i = 0; i < p.re.Length; i++)
            {
                int bar = p.re[i].IndexOf('|');
                t += 1.5f + ((p.text.Length + i * 7) % 5) * 1.2f; // a few game minutes apart, fixed per post
                o.Add(new BlicajLine { who = p.re[i].Substring(0, bar), text = Fill(p.re[i].Substring(bar + 1), spot), at = t, reply = true });
            }
        }

        /// the feed visible at `now`: yesterday's last posts as history, today's posts so far, test posts and Mate's reports with
        /// their replies, oldest first
        public static List<BlicajLine> Build(BlicajSave b, float now)
        {
            var o = new List<BlicajLine>();
            int day = Mathf.FloorToInt(now / 1440), prev = day - 1; if (prev % 7 == 6) prev--; // (Sunday is skipped by the game)
            var y = Schedule(prev);
            for (int i = Mathf.Max(0, y.Count - 4); i < y.Count; i++) AddPost(o, Posts[y[i].post], y[i].at, y[i].spot);
            foreach (var s in Schedule(day)) if (s.at <= now) AddPost(o, Posts[s.post], s.at, s.spot);
            float from = prev * 1440;
            if (b != null)
            {
                foreach (var f in b.forced) if (f.at >= from && f.post >= 0 && f.post < Posts.Length) AddPost(o, Posts[f.post], f.at, f.spot);
                for (int i = 0; i < b.mine.Count; i++) if (b.mine[i].at >= from) AddMine(o, b.mine[i], i);
            }
            o.RemoveAll(l => l.at > now);
            o.Sort((p, q) => p.at.CompareTo(q.at)); // (List.Sort is not stable: replies are always later than their post anyway)
            return o;
        }

        // ------------------------------------------------------------------ Mate's reports
        public static readonly string[] ReportLabels = { "Radar na tronošcu", "Plavi zaustavljaju", "Čisto je, nema nikog" };
        static readonly string[] MateTexts =
        {
            "Radar {w}, na tronošcu. Polako ljudi.",
            "Plavi {w}, zaustavljaju. Pazite.",
            "Proša san {w}, nema nikog. Čisto.",
        };
        public static string MateText(BlicajReport r) => MateTexts[Mathf.Clamp(r.kind, 0, 2)].Replace("{w}", r.where);

        static readonly string[] Thanks =
        {
            "Teta Neda|fala Mate, zlato", "Bepo|fala kume", "Duje_93|Mate legenda", "Mirela K.|fala, baš san iša tuda",
            "Krešo Dizel|ajme fala, ja tu uvik vozin 70", "Šime Kambelovac|potvrđujem, i ja ih vidija", "Zoran (kombi)|bravo kamiondžija",
            "Frane|e fala, sad ću okolo", "Stipe|Mate a ti si onaj s kamionon? fala", "Tonka|fala Mate, vozi pomalo i ti",
            "Nediljka|Hvala Mate. Pozdravite ženu.", "Mladen Gomilica|zahvala od cile Gomilice", "Admin Joško|potvrđena dojava, fala",
            "Lucija|fala ti puno!!", "Marinko Taksi|fala, ja baš iman vožnju tamo",
        };
        static readonly string[] ClearThanks =
        {
            "Bepo|fala Mate, onda gas", "Admin Joško|ne gas nego 50!!", "Teta Neda|fala sine", "Duje_93|super, fala",
            "Krešo Dizel|e to volin čut", "Lucija|fala, sad ću mirno",
        };
        static readonly string[] Doubt =
        {
            "Jure Lukšić|Mate ja sad proša, nema nikog?", "Ante iz Starog|di Mate? ja ne vidin ništa", "Frane|to je od jučer Mate",
        };

        static void AddMine(List<BlicajLine> o, BlicajReport r, int idx)
        {
            o.Add(new BlicajLine { who = MateNick, text = MateText(r), at = r.at, mate = true });
            var rnd = new System.Random(Mathf.RoundToInt(r.at * 10) + idx * 131);
            var pool = r.kind == 2 ? ClearThanks : Thanks;
            int n = 2 + rnd.Next(3); float t = r.at;
            int day = Mathf.FloorToInt(r.at / 1440);
            var pat = PatrolSpot(day, (r.at % 1440) / 60f);
            // a radar / police report where today's patrol is not: sometimes someone says there is nothing there
            bool doubt = r.kind != 2 && (pat == null || pat.id != r.spot) && rnd.NextDouble() < 0.4;
            int start = rnd.Next(pool.Length);
            for (int i = 0; i < n; i++)
            {
                bool d = doubt && i == 1;
                string s = d ? Doubt[rnd.Next(Doubt.Length)] : pool[(start + i * 3) % pool.Length];
                int bar = s.IndexOf('|'); t += 1 + (float)rnd.NextDouble() * 4;
                o.Add(new BlicajLine { who = s.Substring(0, bar), text = s.Substring(bar + 1), at = t, reply = true, toMate = true, doubt = d });
            }
        }

        /// thanks Mate has received so far (replies to his reports that are not doubts)
        public static int ThanksCount(List<BlicajLine> feed) { int n = 0; foreach (var l in feed) if (l.toMate && !l.doubt) n++; return n; }

        /// test: n random posts in the last hour (forced, saved like the rest) – -blicajdemo
        public static void Demo(BlicajSave b, float now, int n)
        {
            var rnd = new System.Random(Mathf.RoundToInt(now));
            var all = Ok(false);
            for (int i = 0; i < n; i++)
            {
                int k = rnd.Next(Posts.Length);
                b.forced.Add(new BlicajForced { post = k, at = now - 60 + i * (55f / n), spot = all.Count > 0 ? all[rnd.Next(all.Count)].id : "" });
            }
            if (b.forced.Count > 40) b.forced.RemoveRange(0, b.forced.Count - 40);
        }

        // ------------------------------------------------------------------ the posts (kind r: true police / radar at the patrol
        // spot, l: false or stale alarm at any spot, g: traffic or something on the road at any spot, x: chat without a place).
        // Hours are the window the post may appear in (game time). Replies: "Nick|text", {m} works in replies too.
        public class Post { public char kind; public string who, text; public float h0, h1; public string[] re; }
        static Post R(string who, float h0, float h1, string text, params string[] re) => new Post { kind = 'r', who = who, text = text, h0 = h0, h1 = h1, re = re };
        static Post L(string who, float h0, float h1, string text, params string[] re) => new Post { kind = 'l', who = who, text = text, h0 = h0, h1 = h1, re = re };
        static Post G(string who, float h0, float h1, string text, params string[] re) => new Post { kind = 'g', who = who, text = text, h0 = h0, h1 = h1, re = re };
        static Post X(string who, float h0, float h1, string text, params string[] re) => new Post { kind = 'x', who = who, text = text, h0 = h0, h1 = h1, re = re };

        public static readonly Post[] Posts =
        {
            // --- true reports
            R("Bepo", 7, 11, "Plavi stoje {m}, tronožac i dvojica. Vozite lipo", "Teta Neda|fala Bepo zlato", "Duje_93|potvrđujem, sad proša"),
            R("Mirela K.", 7, 17, "radar {m}!!! u bilom autu, nisu u plavom", "Jure Lukšić|a u bilom?? ajme meni", "Mirela K.|u bilom kažen ti"),
            R("Šime Kambelovac", 6.5f, 12, "{m} zaustavljaju sve kamione, gledaju papire", "Zoran (kombi)|i kombije gledaju, ja jedva proša"),
            R("Ante iz Starog", 7, 18, "pazite {m} mjere brzinu, vidija san laser"),
            R("Duje_93", 7, 19, "plavi {m} ljudi pazite", "Frane|di točno", "Duje_93|pa {m} san reka", "Frane|ma znan, ali di TOČNO"),
            R("Tonka", 8, 16, "stoje {m}, jedan maše palicon, triba stat ko ga vidi"),
            R("Krešo Dizel", 7, 10, "{m} kontrola, alkotest u 8 ujutro. ko pije u 8 ujutro ljudi", "Bepo|ima ih Krešo, viruj mi"),
            R("Nediljka", 8, 13, "Djeco polako, {m} je policija. Unuk mi reka da javim.", "Admin Joško|fala Nediljka, svaka čast"),
            R("Vinko Novi", 7, 18, "{m} radar, okrenut prema Splitu"),
            R("Stipe", 9, 18, "e ljudi {m} su plavi, i to oni mladi šta pišu sve"),
            R("Marinko Taksi", 7, 19, "{m} stoje. Ja tri puta proša, tri puta me gledali ka da san ja kriv"),
            R("Lucija", 8, 17, "jel to radar {m} ili mi se čini? nešto stoji na tronošcu", "Šime Kambelovac|radar je, ja ga vidija", "Lucija|fala, a mislila san da je fotograf"),
            R("Mladen Gomilica", 7, 15, "Pazite {m}, kontrola pojaseva. Vežite se ljudi lipo"),
            R("Teta Neda", 8, 13, "plavi su {m}, a ja baš nosila smokve u Split", "Stipe|jesu ti uzeli smokve", "Teta Neda|samo dvi"),
            R("Zoran (kombi)", 7, 17, "{m} zaustavljaju, meni napisali za svitlo. Lipo kažen od jutra svitlo ne radi", "Bepo|jesi platija?", "Zoran (kombi)|a šta ću"),
            R("Jure Lukšić", 9, 18, "evo ih opet {m}. ka da im je tu kuća", "Ante iz Starog|plaćaju i oni kiriju tu sigurno"),
            R("Ivana S.", 8, 18, "radar {m}, tu je 50, nemojte ka ja", "Krešo Dizel|koliko?", "Ivana S.|neću reć"),
            R("Duje_93", 7, 9, "{m} plavi, svi u koloni ka na sprovodu"),
            R("Ćaća od Ive", 9, 14, "Sin mi reka da napišem. Policija {m}.", "Tonka|pozdravi Ivu"),
            R("Šime Kambelovac", 7, 11, "Stoje {m} od 7 ujutro, ka da su tu i spavali"),
            R("Bepo", 10, 18, "{m} skidaju tablice nekom stojadinu", "Stipe|ajme stojadin, a šta je on kriv"),
            R("Mirela K.", 8, 17, "pazite {m} radar u grmu, vidi mu se samo nogica od tronošca"),
            R("Admin Joško", 9, 17, "POTVRĐENO od više članova: radar {m}. Vozite po propisu."),
            R("Krešo Dizel", 8, 18, "{m} kontrola. ja proša s pojason, svi ponosni na mene", "Teta Neda|bravo Krešo"),
            R("Mladen Gomilica", 6.5f, 9, "jutarnja kontrola {m}, ima ih četvero, ćakulaju"),
            R("Zoran (kombi)", 9, 17, "{m} plavi s tronošcen, slikaju sve šta se miče"),
            R("Teta Neda", 19, 22, "Policija {m}, gledaju svima u auto", "Bepo|ko zna šta traže", "Teta Neda|valjda mene"),
            R("Frane", 19, 22, "{m} alkotest. kasno je ljudi, pazite"),
            R("Ivana S.", 9, 17, "{m} stoje, jedan je zgodan ali ipak vozite polako", "Mirela K.|koji", "Ivana S.|onaj s brkon", "Admin Joško|samo informacije molim"),
            R("Lucija", 8, 16, "Pazite plavi {m}, gledaju kamione jesu li pretovareni", "Zoran (kombi)|a ja vozin cement u kombiju, ajme"),
            R("Marinko Taksi", 8, 18, "{m} radar. Ja platija prošli put 60 eura, ne ponovilo se"),
            R("Mladen Gomilica", 10, 18, "{m} su opet. ko god prolazi blicnite ka ljudi"),
            R("Nediljka", 9, 16, "Policija {m}. Pitali me jesan li vidila nekoga. Nisan."),
            R("Stipe", 13, 18, "posli marende su se vratili {m}"),
            // --- false and stale alarms
            L("Frane", 7, 17, "radar {m}!!", "Jure Lukšić|nema tamo ništa, ja sad proša", "Frane|bija je jutros", "Jure Lukšić|jutros je bija i u ponediljak"),
            L("Stipe", 8, 18, "plavi {m}?", "Mirela K.|to je auto od struje, miću stup", "Stipe|isto ima rotirku"),
            L("Nediljka", 8, 14, "Policija {m}.", "Duje_93|to je od jučer Nediljka", "Nediljka|A ja san sad vidila tu u grupi"),
            L("Marinko Taksi", 9, 18, "radar {m}", "Ante iz Starog|to je kanta za smeće Marinko", "Marinko Taksi|isto izgleda sumnjivo"),
            L("Lucija", 7, 18, "jel ima ko {m}? stoje li još?", "Bepo|otišli su prije pola ure", "Šime Kambelovac|nisu, evo ih", "Bepo|onda su se vratili"),
            L("Krešo Dizel", 9, 17, "{m} su plavi!!! a ne, to je bija poštar"),
            L("Teta Neda", 8, 12, "Ljudi stoje {m}. Jučer.", "Admin Joško|Neda molim samo današnje", "Teta Neda|a šta ja znan kad je danas"),
            L("Vinko Novi", 9, 17, "na D8 radar", "Tonka|di na D8, D8 je dug 600 kilometri", "Vinko Novi|na ovoj našoj"),
            L("Jure Lukšić", 8, 18, "e jel istina da su {m}?", "Zoran (kombi)|ne", "Jure Lukšić|fala", "Zoran (kombi)|nema na čemu"),
            L("Duje_93", 10, 18, "LAŽNA UZBUNA {m}, nema nikog, to je onaj čovik šta prodaje lubenice"),
            L("Mladen Gomilica", 9, 17, "{m} stoji neki auto s rotirkon", "Stipe|to je pauk", "Mladen Gomilica|a vidiš, sad nosi moj auto"),
            L("Ivana S.", 9, 18, "netko javija radar {m}, a tamo samo jedan stari s kišobranon"),
            L("Šime Kambelovac", 8, 18, "{m} nema nikog, slobodno. Ali vozite lipo ipak"),
            L("Bepo", 10, 16, "jutros su bili {m}, sad nema nikog"),
            L("Ante iz Starog", 9, 18, "Ko je javija radar {m}? Nema ničega, a ja se vuka 40 iza traktora za ništa", "Frane|nije iza traktora za ništa, uvik je za nešto"),
            L("Vinko Novi", 9, 17, "radar {m}, al stoji prema moru, valjda mjeri barke"),
            L("Stipe", 8, 18, "e jel ima radar {m}, sad ću proć", "Bepo|ne pitaj nego vozi 50 i nema problema", "Stipe|a di je tu gušt"),
            L("Šime Kambelovac", 11, 14, "dojava od jutros {m} više ne vridi, otišli na marendu", "Krešo Dizel|na marendu ka ljudi"),
            L("Mirela K.", 9, 18, "ljudi radar {m}! a ne, sorry, to je bila moja svekrva s kišobranon"),
            L("Krešo Dizel", 7, 10, "evo jučerašnji radar {m}, za svaki slučaj", "Admin Joško|Krešo jučerašnji radar nije radar", "Krešo Dizel|ali mogu se vratit"),
            // --- the road
            G("Zoran (kombi)", 7, 16, "kolona {m}, neki kamion se okreće u pet poteza", "Mladen Gomilica|ma to je onaj iz Lukšića sigurno", "Zoran (kombi)|ma neka ga, mora čovik radit"),
            G("Tonka", 7, 18, "{m} gužva, semafor opet ne radi kako triba"),
            G("Stipe", 8, 17, "{m} prosuli lubenice po cesti, ima za sve"),
            G("Krešo Dizel", 8, 19, "{m} se sudarili dva auta, nikom ništa, samo se svađaju", "Teta Neda|neka ih, nek se ispušu"),
            G("Mirela K.", 7, 12, "Pazite {m} ovce na cesti!!!", "Jure Lukšić|koje ovce", "Mirela K.|bile su, sad nisu"),
            G("Lucija", 8, 16, "{m} radovi, jedna traka, čeka se 10 minuti"),
            G("Bepo", 7, 18, "{m} pokvarija se autobus, obilazite kroz mista"),
            G("Frane", 7, 18, "{m} lokva ka jezero, ne vidi se di je rupa"),
            G("Marinko Taksi", 15, 19, "{m} gužva ka u Zagrebu, a nije ni sezona"),
            G("Duje_93", 9, 18, "{m} neki u kamperu vozi 30 i maše svima"),
            G("Vinko Novi", 8, 14, "{m} traktor s grožđem, strpite se ljudi"),
            G("Nediljka", 8, 18, "Na cesti {m} pas, crni, ima ogrlicu. Ko ga je izgubija?", "Ivana S.|to je od mog susida! zovem ga", "Nediljka|Fala Bogu"),
            G("Jure Lukšić", 9, 19, "{m} prošla hitna, pustite ih ljudi"),
            G("Bepo", 10, 18, "{m} neko stao nasred ceste i kupuje bresve"),
            G("Tonka", 8, 18, "{m} prosuto ulje, kliže ka led"),
            // --- chat without a place
            X("Teta Neda", 8, 13, "Prodajen smokve, domaće, 2 eura kila. Javite se u poruke.", "Admin Joško|Neda ovo nije oglasnik!!", "Teta Neda|a di ću onda", "Bepo|ja uzimam kilu"),
            X("Ivana S.", 10, 20, "evo moja Mica, nema veze s policijon ali lipa je", "Admin Joško|Ivana MOLIM", "Teta Neda|lipa je", "Krešo Dizel|lipa je", "Admin Joško|ok lipa je"),
            X("Admin Joško", 6, 9, "PRAVILA GRUPE: 1. samo policija i radari 2. bez mačaka 3. bez smokava 4. ne svađajte se", "Teta Neda|a grožđe?", "Admin Joško|NI GROŽĐE"),
            X("Šime Kambelovac", 7, 12, "ko je ovo ostavija auto na pločniku kod škole, plavi karavan, maknite ga ljudi", "Duje_93|plavi karavan ili plavi plavi", "Šime Kambelovac|karavan karavan"),
            X("Stipe", 7, 11, "ko je ovo ostavija auto na pločniku ispred pekare, ne mogu s kolicima proć", "Lucija|ja, evo idem", "Stipe|aj fala"),
            X("Mladen Gomilica", 6, 8, "dobro jutro svima, kava je gotova, plavih nema na vidiku"),
            X("Bepo", 9, 18, "jel ko vidija moju kapu? izgubija san je na pumpi", "Marinko Taksi|ima na pumpi kapa al je ženska"),
            X("Krešo Dizel", 11, 17, "kad je radar zadnji put bija kod vas? ja ga nisan vidija tjedan dan", "Frane|ne zazivaj", "Krešo Dizel|ups"),
            X("Lucija", 10, 18, "Prodajen dječja kolica, malo korištena. Može zamjena za drva", "Admin Joško|Lucija ovo nije oglasnik", "Lucija|svi prodaju, zašto ja ne"),
            X("Frane", 9, 13, "ko zna radi li pošta subotom", "Nediljka|radi do podne", "Bepo|ne radi", "Nediljka|radi", "Admin Joško|zaključavam komentare"),
            X("Vinko Novi", 12, 19, "ljudi kupija san auto, jel triba javit u grupu", "Tonka|čestitam", "Vinko Novi|fala, plavi je", "Jure Lukšić|ajme opet plavi"),
            X("Teta Neda", 20, 22, "Laku noć grupa, pazite se sutra", "Bepo|laku noć Neda"),
            X("Duje_93", 7, 18, "ima li ko kabel za akumulator, stoji mi auto kod crkve", "Šime Kambelovac|evo dolazin", "Duje_93|ti si legenda"),
            X("Ivana S.", 12, 18, "zašto ova grupa ima 1300 članova a niko ne javlja ništa danas", "Krešo Dizel|jer nema plavih", "Ivana S.|ili ste svi lini"),
            X("Ćaća od Ive", 10, 18, "Kako se ovdi piše poruka", "Tonka|tako", "Ćaća od Ive|Fala"),
            X("Admin Joško", 9, 12, "Pozdrav novim članovima! Ovdi javljamo di su plavi. Mačke i smokve NE.", "Teta Neda|a mendule?"),
            X("Mirela K.", 7, 18, "posli kiše je sve puno lokvi, vozite polako i bez plavih"),
            X("Marinko Taksi", 8, 18, "Taksi Kaštela–Split, povoljno, i noću", "Admin Joško|MARINKO", "Marinko Taksi|dobro dobro"),
            X("Stipe", 9, 16, "di je onaj šta je proda smokve jučer, bile su kisele", "Teta Neda|moje nisu kisele!!!", "Stipe|nisan reka da su tvoje", "Teta Neda|ma znan ja"),
            X("Jure Lukšić", 16, 21, "jel ko zna radi li ona pumpa u Lukšiću do ponoći", "Bepo|radi", "Jure Lukšić|fala Bepo"),
            X("Tonka", 11, 19, "evo moj pas Roki, on isto pazi da ne bude radara", "Admin Joško|evo sad i psi", "Mirela K.|joj kako je sladak"),
            X("Šime Kambelovac", 6, 10, "Bura jutros, kamioni i kombiji pazite na D8", "Zoran (kombi)|meni kombi skoro uletija u more"),
            X("Lucija", 15, 17, "jel se ide u 5 popodne u Split ili je bolje ostat doma", "Frane|ostat doma", "Lucija|fala"),
            X("Nediljka", 6, 8, "Dobro jutro svima. Vozite pažljivo, nema žurbe."),
            X("Duje_93", 14, 20, "prvi put proša cili dan bez da me zaustavu, sritan dan"),
            X("Krešo Dizel", 10, 18, "Ko ima garažu za iznajmit u Lukšiću, triba mi za staru krntiju"),
            X("Ante iz Starog", 15, 20, "ovo mi je 50. dojava ove godine, šta dobijen", "Admin Joško|zahvalu", "Ante iz Starog|može i jaja"),
            X("Krešo Dizel", 10, 18, "jel radar slika i suvozača? pitan za prijatelja", "Duje_93|prijatelj se zove Krešo", "Krešo Dizel|ne zove se"),
            X("Admin Joško", 12, 14, "Podsjetnik: ova grupa nije za vriđanje. Ni za smokve. Pozdrav.", "Teta Neda|ajme"),
            X("Teta Neda", 9, 15, "Ima li ko jaja, domaća, triba mi za kolač", "Bepo|ja iman", "Admin Joško|..."),
            X("Duje_93", 10, 17, "šta se ovo puši iznad cementare", "Šime Kambelovac|to je normalno", "Duje_93|aha onda dobro"),
            X("Jure Lukšić", 9, 17, "jel ko zna kad će asfaltirat staru cestu", "Frane|kad prestane kiša", "Jure Lukšić|ne pada kiša", "Frane|eto vidiš"),
            X("Ante iz Starog", 19, 22, "Fala svima danas, ni jedne kazne. Lipo je imat ovakvu grupu.", "Teta Neda|grlim te Ante"),
            X("Stipe", 8, 17, "ko zadnji vidi radar, časti kavu", "Bepo|onda ja ne gledam"),
            X("Mladen Gomilica", 17, 21, "ko je ovo opet ostavija auto na pločniku kod rive, ne mogu proć s ćaćon", "Vinko Novi|nije moj", "Tonka|nije ni moj", "Mladen Gomilica|onda je ničiji, zvat ću pauka"),
            X("Frane", 18, 22, "sad mi Susid govori da je radar bija kod njega cili dan a niko nije javija", "Ante iz Starog|a di je Susid", "Frane|ne smin reć, ljutit će se"),
            X("Tonka", 6, 9, "ko ide jutros za Split, triba mi nešto odnit na poštu", "Marinko Taksi|ja, ali naplaćujen", "Tonka|onda niko"),
        };
    }
}
