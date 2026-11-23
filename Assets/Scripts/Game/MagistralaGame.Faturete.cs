// Faturete (story phase 3; kostur priče "Faturete s Galebom", "Cjenkanje", "Na ruke ili na račun"; faza1-odgovori Q9/Q10/Q18;
// dijalozi-prijedlog Q11): small jobs for neighbours and customers, driven with the family's Galeb after work.
//   data     Resources/Game/faturete.json — customers (name, character, their lines) and jobs (cargo, real Kaštela stops from / to,
//            days); Marko edits the lines there. Mate's lines are short and Dalmatian; Jure's are the approved Q11 ones.
//   when     weekdays: one offer between 17:00 and 17:45, sometimes a second at 18:30–19:30, the job to be done by 22:00;
//            Saturday: three offers (morning, noon, afternoon), done by 20:00; Sunday nothing. The first weekday evening it is
//            always susjed Jure's firewood (Q9). An unanswered offer is gone after an hour (the customer finds someone else).
//   offer    a message from the customer (the phone toasts it); Poslovi lists it, the customer's thread has "Odgovori".
//   price    fair = 8 € + 4 €/road km of our graph (pickup → drop, distances.json), rounded to 0.5 €, at least 45 € (web
//            driverPay.ts faturetaBasePay); a job may fix its own (Jure: 60 €, opens at 50, his limit 65 — Q11).
//   haggle   the web's shared/src/haggle.ts, same rules: the customer opens at his character's share of fair, his hidden maximum
//            = fair × (character + 5 %·urgency + 5 %·Mate's standing) × 0.95–1.05, never above 130 % of fair; at most three of
//            Mate's rounds, all prices end in 0 or 5; asking more than 150 % of his maximum insults him. Choices (≤ 4): accept his
//            price · ask more (Mate's suggestions: fair, +10 %, +20 %) · "držim se svoga" (bluff on the last ask) · "Danas ne mogu".
//   payment  (Marko 22.11.) the price stands → Mate sets off; on delivery the customer pays the agreed price (less damage /
//            lateness) and Mate asks "Triba li ti račun?": the customer's answer (FatCustomer.invoice = his chance of wanting one)
//            decides — yes: the trade account + an invoice into the folder "Za Ljubicu"; no: cash straight into Mate's pocket.
//   drive    accepted → the GPS leads to the Galeb if Mate is in the Atego, then to the pickup (E – load, a timer), then to the
//            drop (E – unload) → the payout card (MagistralaGame.Payout.cs): the agreed price, less cargo damage and missing the
//            customer's window; the customer's thanks (or complaint) arrives as a message.
//   standing S.fatRep 0..1 (hidden): on time and whole +0.05, damaged / late −0.05, overcharging (> 110 % of fair) −0.03 and the
//            customer waits twice as long before calling again, cancelling −0.1.
// Test switches: -fatureta <id>  that offer now (and the phone opens on it)   -fatpick 2,3,1  answer the conversation's choices
//   one by one (1-based, every 2.5 s)   -fatdeal ruke|racun|pitaj  agree at his price and pay that way (no haggling); with -jobauto
//   -jobnear 0 the whole job runs by itself (bays jumped into)
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Magistrala.Game.Phone;

namespace Magistrala.Game
{
    public partial class MagistralaGame
    {
        [Serializable] class FatData { public FatMate mate; public FatCustomer[] customers; public FatJob[] jobs; }
        [Serializable] class FatMate { public string acceptOffer, ask, firm, acceptCounter, decline, cash, invoice, go, invoiceAsk; }
        [Serializable] class FatCustomer { public string id, name, role, color, personality; public int urgency; public float invoice = 0.5f; public FatLines lines; }
        [Serializable] class FatLines { public string counter, giveIn, agree, walkAway, insulted, final, payment, expired, cancel, thanks, thanksBad, phone, invoiceYes, invoiceNo; }
        [Serializable] class FatJob { public string id, customer, cargo, what, from, to, days, text; public float kg, fair, open, max, loadSec = 15, unloadSec = 15; public int cooldown = 7; }

        /// one offer of the day and its haggling state (saved)
        [Serializable]
        public class FatOffer
        {
            public string id, slot, state = "new"; // new (message only) · talk · deal (price agreed, payment not) · accepted · gone
            public int day, seed, round, patience;
            public float at, expires, windowEnd, fair, max, offer, step, lastAsk, km, price;
        }

        public const int HaggleRounds = 3;
        public const float FatMinPay = 45;
        static readonly Dictionary<string, (float open, float max, int patience, float step)> Profiles = new Dictionary<string, (float, float, int, float)>
        {
            { "posten", (0.9f, 1.1f, 2, 0.5f) }, { "skrtica", (0.6f, 1.0f, 2, 0.3f) }, { "zuri_se", (0.8f, 1.2f, 1, 0.6f) },
            { "turist", (1.0f, 1.2f, 0, 0f) }, { "rodbina", (0.5f, 0.8f, 2, 0.4f) },
        };

        FatData fat;
        static float Round5(float x) => Mathf.Round(x / 5) * 5;
        static float Floor5(float x) => Mathf.Floor(x / 5) * 5;
        public static float FaturetaBasePay(float km) => Mathf.Max(FatMinPay, Mathf.Round((8 + 4 * Mathf.Max(0, km)) * 2) / 2);
        static string Who(string id) => MatePhone.Who(id).name;
        static int StableHash(string s) { int h = 17; foreach (char ch in s) h = (h * 31 + ch) % 100003; return h; }
        static string E0(float v) => $"{v:0} €";
        FatJob FJob(string id) => fat?.jobs == null ? null : Array.Find(fat.jobs, j => j.id == id);
        FatCustomer FCust(string id) => fat?.customers == null ? null : Array.Find(fat.customers, c => c.id == id);
        FatOffer LiveOffer(string customer) => S.fatOffers.Find(o => (o.state == "new" || o.state == "talk" || o.state == "deal") && FJob(o.id)?.customer == customer);
        static string Fill(string t, float offer = 0, float price = 0, float cash = 0, float ask = 0) =>
            (t ?? "").Replace("{offer}", offer.ToString("0")).Replace("{price}", price.ToString("0")).Replace("{cash}", cash.ToString("0")).Replace("{ask}", ask.ToString("0"));

        // ------------------------------------------------------------------ setup
        void StartFaturete()
        {
            var ta = Resources.Load<TextAsset>("Game/faturete");
            if (!ta) { Debug.LogWarning("[fatureta] Resources/Game/faturete.json missing"); return; }
            try { fat = JsonUtility.FromJson<FatData>(ta.text); } catch (Exception e) { Debug.LogWarning($"[fatureta] faturete.json: {e.Message}"); return; }
            foreach (var c in fat.customers)
            {
                ColorUtility.TryParseHtmlString(c.color, out var col);
                MatePhone.AddContact(new MatePhone.Contact { id = c.id, name = c.name, role = c.role, color = col });
            }
            var bad = new List<string>();
            foreach (var j in fat.jobs) if (World.Stop(j.from) == null || World.Stop(j.to) == null || World.RoadMetres(j.from, j.to) < 0 || FCust(j.customer) == null) bad.Add(j.id);
            foreach (var j in fat.jobs) if (!bad.Contains(j.id)) { float km = World.RoadMetres(j.from, j.to) / 1000f; Debug.Log($"[fatureta] {j.id}: {World.Stop(j.from).name} → {World.Stop(j.to).name} {km:0.00} km, fair {(j.fair > 0 ? j.fair : FaturetaBasePay(km)):0.0} €"); }
            if (bad.Count > 0) Debug.LogWarning($"[fatureta] not playable (stop / road / customer missing): {string.Join(", ", bad)}");
            fat.jobs = Array.FindAll(fat.jobs, j => !bad.Contains(j.id));
            // the phone: offers in Poslovi, "Odgovori" in the customer's thread, customers answer calls
            var jp = Phone.jobsProvider; var jn = Phone.jobsNote; var og = Phone.outgoing;
            Phone.jobsTitle = () => "Ture firme i faturete";
            Phone.jobsProvider = () =>
            {
                var l = new List<PhoneJob>();
                foreach (var o in S.fatOffers)
                {
                    if (o.state != "new" && o.state != "talk" && o.state != "deal") continue;
                    var fj = FJob(o.id); if (fj == null) continue; var oo = o;
                    l.Add(new PhoneJob { label = $"Fatureta · {Who(fj.customer)}", sub = $"{fj.what} · {World.Stop(fj.from)?.name} → {World.Stop(fj.to)?.name} · {HudStyle.Dec(o.km)} km · procjena {E0(Round5(o.fair * 0.9f))}–{E0(Round5(o.fair * 1.1f))} · odgovori do {Hm(o.expires)}", take = () => OpenFatureta(oo), col = new Color(0.2f, 0.55f, 0.95f) });
                }
                var firm = jp?.Invoke(); if (firm != null) l.AddRange(firm);
                return l;
            };
            Phone.jobsNote = () =>
                Current != null && Current.fatureta ? $"Fatureta u tijeku: {Current.label}." :
                S.fatQueue.Count > 0 ? $"Na čekanju: {S.fatQueue[0].label}." :
                Day % 7 == 6 ? "Nedjelja je za obitelj." :
                Day % 7 == 5 ? (S.fatOffers.Exists(o => o.state == "new" || o.state == "talk") ? "Subota: faturete s Galebom do 20 h." : "Subota: faturete stižu porukama, 8–20 h.") :
                Hour >= 17 && !S.fatOffers.Exists(o => o.state == "new" || o.state == "talk") ? "Faturete stižu porukama, radnim danom 17–22 h." :
                jn?.Invoke();
            Phone.threadAction = who => LiveOffer(who) is FatOffer o ? ("Odgovori na ponudu", (Action)(() => OpenFatureta(o))) : ((string, Action)?)null;
            Phone.outgoing = who => FCust(who) is FatCustomer c ? (Current != null && Current.customer == who ? c.lines.phone : LiveOffer(who) != null ? "Pa javi se na poruku, Mate!" : "Ništa zasad, Mate. Javim se.") : og?.Invoke(who);
            Debug.Log($"[fatureta] {fat.jobs.Length} jobs, {fat.customers.Length} customers; standing {S.fatRep:0.00}, offers today {S.fatOffers.Count}");
            if (Arg("-fatureta") is string fid) StartCoroutine(TestOffer(fid));
        }

        // ------------------------------------------------------------------ the day's offers
        /// slot times (clock minutes) of a day: weekdays 17:00–17:45 (+ 35 % a second at 18:30–19:30), Saturday three
        List<float> Slots(int day)
        {
            var r = new System.Random(day * 7919 + 101); var l = new List<float>();
            int dow = day % 7; float d0 = day * 1440;
            if (dow < 5) { l.Add(d0 + 17 * 60 + (float)r.NextDouble() * 45); if (r.NextDouble() < 0.35) l.Add(d0 + 18 * 60 + 30 + (float)r.NextDouble() * 60); }
            else if (dow == 5) foreach (float h in new[] { 8.5f, 11.5f, 14.5f }) l.Add(d0 + h * 60 + (float)r.NextDouble() * 60);
            return l;
        }
        float WindowEnd(int day) => day * 1440 + (day % 7 == 5 ? 20 : 22) * 60;

        void TickFaturete()
        {
            if (fat == null || fat.jobs.Length == 0) return;
            float now = S.clockMin;
            var slots = Slots(Day);
            for (int i = 0; i < slots.Count; i++)
            {
                string key = Day + ":" + i;
                if (now < slots[i] || now > slots[i] + 60 || S.fatOffers.Exists(o => o.slot == key)) continue;
                var j = PickJob(i);
                if (j == null) { S.fatOffers.Add(new FatOffer { slot = key, state = "gone", id = "" }); continue; }
                if (MakeOffer(j, key) == null) S.fatOffers.Add(new FatOffer { slot = key, state = "gone", id = "" });
            }
            foreach (var o in S.fatOffers)
                if ((o.state == "new" || o.state == "talk" || o.state == "deal") && now > o.expires && !(Phone.InChat))
                {
                    o.state = "gone";
                    var fj = FJob(o.id); if (fj != null) Phone.Message(fj.customer, FCust(fj.customer).lines.expired, true);
                    Debug.Log($"[fatureta] offer {o.id} expired");
                }
            if (S.fatOffers.Count > 12) S.fatOffers.RemoveAll(o => o.day < Day - 1);
        }

        FatJob PickJob(int slot)
        {
            var r = new System.Random(Day * 31337 + slot * 7 + 5);
            string dk = Day % 7 == 5 ? "sat" : "wd";
            bool Free(FatJob j)
            {
                if (!j.days.Contains(dk) || S.fatOffers.Exists(o => o.id == j.id && o.day == Day)) return false;
                if (Current != null && Current.fatId == j.id) return false;
                foreach (var n in S.fatNext) { var p = n.Split(':'); if (p[0] == j.id && int.TryParse(p[1], out int d) && Day < d) return false; }
                return true;
            }
            // the first fatureta of the game is Jure's firewood (Q9)
            if (S.fatDone == 0 && slot == 0 && FJob("jure_drva") is FatJob jure && Free(jure)) return jure;
            var ok = Array.FindAll(fat.jobs, Free);
            return ok.Length == 0 ? null : ok[r.Next(ok.Length)];
        }

        FatOffer MakeOffer(FatJob j, string slot, bool test = false)
        {
            var c = FCust(j.customer);
            float km = World.RoadMetres(j.from, j.to) / 1000f;
            float fair = j.fair > 0 ? j.fair : FaturetaBasePay(km);
            var pr = Profiles.TryGetValue(c.personality ?? "", out var pp) ? pp : Profiles["posten"];
            var o = new FatOffer { id = j.id, slot = slot, day = Day, seed = Day * 977 + StableHash(j.id), at = S.clockMin, fair = fair, km = km };
            var rnd = new System.Random(o.seed);
            o.max = Mathf.Min(Floor5(fair * 1.3f), fair * (pr.max + 0.05f * c.urgency + 0.05f * S.fatRep) * (0.95f + 0.1f * (float)rnd.NextDouble()));
            if (j.max > 0) o.max = Mathf.Min(o.max, j.max);
            o.offer = j.open > 0 ? j.open : Round5(fair * pr.open);
            // Marko 22.11.: nijedna fatureta ispod 45 € – ni ponuda ni dogovor
            o.offer = Mathf.Max(o.offer, FatMinPay); o.max = Mathf.Max(o.max, o.offer);
            o.patience = pr.patience; o.step = pr.step;
            o.windowEnd = test ? Mathf.Max(WindowEnd(Day), S.clockMin + 240) : WindowEnd(Day);
            o.expires = Mathf.Min(S.clockMin + 60, o.windowEnd - 60);
            if (o.expires <= S.clockMin && !test) return null;
            if (test) o.expires = Mathf.Max(o.expires, S.clockMin + 60);
            S.fatOffers.Add(o);
            Phone.Message(c.id, Fill(j.text, o.offer), true);
            Banner($"Fatureta: {c.name} · {j.what.ToLower()} · Tab → Poslovi");
            Debug.Log($"[fatureta] offer {j.id} from {c.name}: {World.Stop(j.from).name} → {World.Stop(j.to).name} {km:0.00} km, fair {fair:0.0} €, opens {o.offer:0} €, max {o.max:0.0} € (hidden), answer by {Hm(o.expires)}, done by {Hm(o.windowEnd)}");
            SaveNow("fatureta offer");
            return o;
        }

        void DropOffers(string why)
        {
            foreach (var o in S.fatOffers) if (o.state == "new" || o.state == "talk" || o.state == "deal") o.state = "gone";
            if (S.fatQueue.Count > 0) { Debug.Log($"[fatureta] {why}: {S.fatQueue.Count} accepted faturete not driven"); S.fatRep = Mathf.Max(0, S.fatRep - 0.1f * S.fatQueue.Count); S.fatQueue.Clear(); }
        }

        // ------------------------------------------------------------------ the conversation (haggling)
        void OpenFatureta(FatOffer o)
        {
            var j = FJob(o.id); var c = FCust(j?.customer);
            if (j == null || c == null) return;
            var call = new PhoneCall { caller = c.id, chat = true, title = "Poruka · fatureta", sub = Sub(o, j) };
            if (o.state == "deal")
            {
                var st = PayStep(o, j, c, null);
                call.lines = st.lines; call.choices = st.choices;
            }
            else
            {
                call.lines = o.round == 0 ? new[] { Fill(j.text, o.offer) } : new[] { $"Zadnje što nudi: {o.offer:0} €." };
                call.choices = HaggleChoices(o, j, c);
                o.state = "talk";
            }
            Phone.OpenChat(call);
        }

        string Sub(FatOffer o, FatJob j) => $"{j.what} · {HudStyle.Dec(o.km)} km · procjena {E0(Round5(o.fair * 0.9f))}–{E0(Round5(o.fair * 1.1f))} · krug {Mathf.Min(o.round + 1, HaggleRounds)}/{HaggleRounds}";

        static List<float> AskOptions(float fair) { var l = new List<float>(); foreach (var k in new[] { 1f, 1.1f, 1.2f }) { float v = Round5(fair * k); if (!l.Contains(v)) l.Add(v); } return l; }

        List<PhoneChoice> HaggleChoices(FatOffer o, FatJob j, FatCustomer c)
        {
            var m = fat.mate; var l = new List<PhoneChoice>();
            l.Add(new PhoneChoice { label = Fill(o.round == 0 ? m.acceptOffer : m.acceptCounter, o.offer, o.offer), next = () => Agreed(o, j, c, o.offer, c.lines.agree) });
            if (o.round < HaggleRounds)
            {
                var asks = AskOptions(o.fair).FindAll(x => x > o.offer);
                if (o.lastAsk <= 0) { if (asks.Count > 0) l.Add(AskChoice(o, j, c, asks[0], false)); if (asks.Count > 1) l.Add(AskChoice(o, j, c, asks[asks.Count - 1], false)); }
                else
                {
                    var a = asks.Find(x => x != o.lastAsk); if (a > 0) l.Add(AskChoice(o, j, c, a, false));
                    if (o.lastAsk > o.offer) l.Add(AskChoice(o, j, c, o.lastAsk, true));
                }
            }
            l.Add(new PhoneChoice { label = m.decline, next = () => Gone(o, j, c, c.lines.walkAway, 0, "declined") });
            return l;
        }

        PhoneChoice AskChoice(FatOffer o, FatJob j, FatCustomer c, float ask, bool firm) =>
            new PhoneChoice { label = Fill(firm ? fat.mate.firm : fat.mate.ask, o.offer, ask: ask), next = () => Respond(o, j, c, ask, firm) };

        /// haggle.ts respond(): Mate asks `ask` (firm = "držim se svoga")
        PhoneStep Respond(FatOffer o, FatJob j, FatCustomer c, float ask, bool firm)
        {
            var rnd = new System.Random(o.seed + 17 * (o.round + 1) + (firm ? 5 : 0));
            float R() => (float)rnd.NextDouble();
            ask = Round5(ask); o.lastAsk = ask;
            if (o.round >= HaggleRounds) return Gone(o, j, c, c.lines.walkAway, 0.02f, "walk");
            o.round++;
            string res; float price = 0;
            if (ask <= o.offer) { res = "accept"; price = Mathf.Max(ask, 5); }
            else if (ask > o.max * 1.5f) res = "insulted";
            else if (firm)
            {
                if (ask <= o.max) { res = "accept"; price = ask; }
                else if (ask <= Mathf.Min(o.max * 1.1f, Floor5(o.fair * 1.3f)) && R() < 0.4f) { res = "accept"; price = ask; }
                else res = "walk";
            }
            else if (ask <= o.max && R() < 0.3f) { res = "accept"; price = ask; }
            else if (--o.patience < 0) res = "walk";
            else
            {
                float next = Mathf.Max(Round5(o.offer + (ask - o.offer) * o.step), o.offer + 5);
                next = Mathf.Min(next, Mathf.Min(Floor5(o.max), ask));
                if (next <= o.offer) res = "walk"; else { res = "counter"; o.offer = next; }
            }
            Debug.Log($"[fatureta] {o.id} round {o.round}: Mate asks {ask:0} €{(firm ? " (firm)" : "")} → {res} {(res == "counter" ? o.offer : price):0} (max {o.max:0.0}, patience {o.patience})");
            if (res == "accept") return Agreed(o, j, c, price, ask > o.offer ? c.lines.giveIn : c.lines.agree);
            if (res == "insulted") return Gone(o, j, c, c.lines.insulted, 0.05f, "insulted");
            if (res == "walk") return Gone(o, j, c, c.lines.walkAway, 0.02f, "walk");
            var st = new PhoneStep { lines = new[] { Fill(o.round >= HaggleRounds ? c.lines.final : c.lines.counter, o.offer, o.offer) }, sub = Sub(o, j) };
            st.choices = HaggleChoices(o, j, c);
            SaveNow("haggle");
            return st;
        }

        PhoneStep Gone(FatOffer o, FatJob j, FatCustomer c, string line, float repLoss, string why)
        {
            o.state = "gone"; S.fatRep = Mathf.Clamp01(S.fatRep - repLoss);
            Debug.Log($"[fatureta] {o.id}: no deal ({why}), standing {S.fatRep:0.00}");
            SaveNow("no deal");
            return new PhoneStep { lines = new[] { line } };
        }

        /// the price stands: the customer's line and how he pays
        PhoneStep Agreed(FatOffer o, FatJob j, FatCustomer c, float price, string line)
        {
            price = Mathf.Max(price, FatMinPay);
            o.state = "deal"; o.price = price;
            Debug.Log($"[fatureta] {o.id}: agreed {price:0} € (fair {o.fair:0.0})");
            return PayStep(o, j, c, line);
        }

        PhoneStep PayStep(FatOffer o, FatJob j, FatCustomer c, string line)
        {
            // how he pays is asked on delivery (FaturetaPayMode), not here
            float price = o.price;
            var st = new PhoneStep { sub = $"{j.what} · dogovoreno {E0(price)}" };
            var lines = new List<string>(); if (!string.IsNullOrEmpty(line)) lines.Add(line); lines.Add(Fill(c.lines.payment, o.offer, price)); st.lines = lines.ToArray();
            st.choices.Add(new PhoneChoice { label = Fill(fat.mate.go, price: price), act = () => Accepted(o, j, c, price, "pitaj") });
            return st;
        }

        void Accepted(FatOffer o, FatJob j, FatCustomer c, float price, string mode)
        {
            o.state = "accepted"; o.price = price;
            bool over = price > o.fair * 1.1f;
            if (over) S.fatRep = Mathf.Clamp01(S.fatRep - 0.03f); else if (price < o.fair * 0.9f) S.fatRep = Mathf.Clamp01(S.fatRep + 0.02f);
            SetCooldown(j, over ? j.cooldown * 2 : j.cooldown);
            var job = new Job
            {
                fatureta = true, tour = "fatureta", fatId = j.id, customer = c.id, cargo = j.cargo, label = $"{j.what} · {c.name}", key = j.id,
                from = j.from, to = j.from, kg = j.kg, drops = 1, loaded = false, km = o.km, pay = price, payMode = mode,
                loadSec = j.loadSec, unloadSec = j.unloadSec, startMin = S.clockMin, deadlineMin = Mathf.Max(60, o.windowEnd - S.clockMin),
            };
            job.legs.Add(new Leg { stop = j.from, kind = "u" }); job.legs.Add(new Leg { stop = j.to, kind = "d" });
            Debug.Log($"[fatureta] accepted {j.id}: {price:0} € {(mode == "ruke" ? "na ruke" : mode == "racun" ? "preko obrta (račun)" : "račun se pita na isporuci")}, {World.Stop(j.from).name} → {World.Stop(j.to).name}, by {Hm(o.windowEnd)}; standing {S.fatRep:0.00}");
            if (Current == null) StartFatureta(job);
            else { S.fatQueue.Add(job); Banner($"Fatureta dogovorena: {j.what}. Kad završiš ovo, GPS vodi dalje."); SaveNow("fatureta queued"); }
        }

        void SetCooldown(FatJob j, int days) { S.fatNext.RemoveAll(n => n.StartsWith(j.id + ":")); S.fatNext.Add($"{j.id}:{Day + Mathf.Max(1, days)}"); }

        void StartFatureta(Job j)
        {
            Current = j; State = Phase.Driving; Truck.SetCargo(0); SetPallets(false); routeT = 0;
            j.to = j.Leg.stop;
            var from = World.Stop(j.from)?.name;
            Banner(InGaleb ? $"Fatureta: {j.cargo}. Utovar: {from}. GPS vodi." : $"Fatureta: {j.cargo}. Prvo po Galeba, pa utovar: {from}.");
            SaveNow("fatureta accepted");
        }

        void NextQueuedFatureta()
        {
            if (Current != null || S.fatQueue.Count == 0) return;
            var j = S.fatQueue[0]; S.fatQueue.RemoveAt(0);
            StartFatureta(j);
        }

        // ------------------------------------------------------------------ delivery, cancel
        /// Deliver(): the agreed price (less damage / lateness) into the cash wallet or the trade account (+ an invoice)
        /// on delivery Mate asks "Triba li ti račun?" — the customer's answer (his FatCustomer.invoice chance) sets j.payMode once
        void FaturetaPayMode(Job j)
        {
            if (j.payMode == "racun" || j.payMode == "ruke") return; // decided (or an old save's choice at the deal)
            var c = FCust(j.customer);
            j.payMode = UnityEngine.Random.value < (c != null ? c.invoice : 0.5f) ? "racun" : "ruke";
            Debug.Log($"[fatureta] {j.fatId}: račun? → {(j.payMode == "racun" ? "triba" : "ne triba")}");
        }

        void FaturetaPaid(Job j, Payout p)
        {
            FaturetaPayMode(j);
            if (j.payMode == "racun") { S.account += p.total; S.invoices.Add($"{Day}|{Hm(S.clockMin)}|{Who(j.customer)}|{p.total:0.0}"); }
            else S.cash += p.total;
            S.fatDone++;
            bool good = !p.late && j.cargoDamage <= 10;
            S.fatRep = Mathf.Clamp01(S.fatRep + (good ? 0.05f : -0.05f));
            var c = FCust(j.customer);
            if (c != null) Phone.Message(c.id, good ? c.lines.thanks : c.lines.thanksBad, true);
            Debug.Log($"[fatureta] paid {j.fatId}: {p.total:0.0} € {(j.payMode == "racun" ? "→ račun obrta (račun za Ljubicu)" : "→ gotovina")}, cash {S.cash:0.0} €, account {S.account:0.0} €, invoices {S.invoices.Count}, standing {S.fatRep:0.00}");
        }

        public string FaturetaQuip(Job j, Payout p)
        {
            var c = FCust(j.customer); if (c == null) return "";
            FaturetaPayMode(j);
            string ans = j.payMode == "racun" ? c.lines.invoiceYes : c.lines.invoiceNo;
            if (string.IsNullOrEmpty(ans)) return !p.late && j.cargoDamage <= 10 ? c.lines.thanks : c.lines.thanksBad;
            return $"{fat.mate.invoiceAsk}“ – „{ans}";
        }

        void FaturetaCancelled(Job j)
        {
            S.fatRep = Mathf.Clamp01(S.fatRep - 0.1f);
            var c = FCust(j.customer); if (c != null) Phone.Message(c.id, c.lines.cancel, true);
            Debug.Log($"[fatureta] cancelled {j.fatId}, standing {S.fatRep:0.00}");
        }

        // ------------------------------------------------------------------ tests
        IEnumerator TestOffer(string id)
        {
            yield return new WaitForSeconds(2.2f);
            var j = FJob(id);
            if (j == null) { Debug.LogWarning($"[fatureta] -fatureta {id}: no such playable job ({string.Join(", ", Array.ConvertAll(fat.jobs, x => x.id))})"); yield break; }
            S.fatOffers.RemoveAll(o => o.id == id && o.day == Day);
            var o = MakeOffer(j, "test:" + Day, true);
            if (o == null) yield break;
            yield return new WaitForSeconds(0.8f);
            if (Arg("-fatdeal") is string mode)
            {
                Agreed(o, j, FCust(j.customer), o.offer, null);
                Accepted(o, j, FCust(j.customer), mode == "ruke" ? Mathf.Max(5, Round5(o.offer * 0.9f)) : o.offer, mode == "ruke" ? "ruke" : mode == "racun" ? "racun" : "pitaj");
                if (auto) StartCoroutine(AutoFatureta());
                yield break;
            }
            OpenFatureta(o);
            if (Arg("-fatpick") is string picks)
                foreach (var k in picks.Split(','))
                {
                    yield return new WaitForSecondsRealtime(2.5f);
                    if (int.TryParse(k, out int n)) Phone.ChooseIndex(n - 1);
                }
        }

        /// -fatdeal with -jobauto: the vehicle into the pickup bay (or on the route), loaded and driven by the test driver
        IEnumerator AutoFatureta()
        {
            yield return new WaitForSeconds(1f);
            if (Current == null || !Current.fatureta) yield break;
            if (!InGaleb) { Debug.LogWarning("[fatureta] -jobauto: not in the Galeb (use -vozilo galeb)"); yield break; }
            UpdateRoute();
            if (nearStart == 0) { PlaceAtStop(World.Stop(Current.to)); yield return new WaitForSeconds(1.5f); }
            else if (nearStart > 0 && Route != null) { PlaceOnRoute(nearStart); DriveRoute(); }
            else DriveRoute();
        }
    }
}
