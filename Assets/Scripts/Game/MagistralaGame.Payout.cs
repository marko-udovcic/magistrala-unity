// Delivery payout (web: shared/src/economy.ts computePayout, ui/screens/Delivery.tsx, content/story.ts DELIVERY_LINES):
// base = the firm's pay (10 € + 3 €/road km); lines: late (−15 % + 1 % per 6 min over, at most −50 %), cargo damage (−%),
// reversed into the bay (+10 %), night run (+20 %, more than half of the tour's game minutes between 21 and 5:30). Local region
// jobs: bonuses share a budget of 120 € − base; the total rounded to 0.5 €. Rating 5 stars − late − damage > 10 / 35 % − truck
// damage > 15 %. A multi-stop tour (MagistralaGame.Tours.cs) is paid once, at its end: base = 10 € + 3 €/km of the whole tour
// + StopPay per stop after the first, the empties' return included. Shown as a short card after the unload (Enter closes, gone after 10 s). Firm pay goes to the pocket (cash).
// Damage (engine.ts drainContacts): body contact force over 60 kN → strength = (F − 60 kN) / (mass · 60), debounced 0.6 s;
// truck damage strength · 6 (· 9 with a vehicle), cargo damage × 1.2.
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.Game
{
    public partial class MagistralaGame
    {
        /// Existing customer standing is already normalized, unlike the dispatcher's signed story score.
        public float ReputationPct => Mathf.Clamp01(S.fatRep) * 100;

        SaveData earningsSave;
        int earningsDay = int.MinValue, earningsCount = -1;
        float earningsTotal;
        /// Today and the six preceding game days, cash and invoice payouts together (each delivery once).
        /// Parse legacy locale-dependent history only when a delivery, day or loaded save changes.
        public float SevenDayEarnings
        {
            get
            {
                int count = S.deliveredJobs != null ? S.deliveredJobs.Count : 0;
                if (earningsSave == S && earningsDay == Day && earningsCount == count) return earningsTotal;
                earningsSave = S; earningsDay = Day; earningsCount = count; earningsTotal = 0;
                for (int i = 0; i < count; i++)
                {
                    string row = S.deliveredJobs[i];
                    if (string.IsNullOrEmpty(row)) continue;
                    var fields = row.Split('|');
                    if (fields.Length < 4 || !int.TryParse(fields[0], out int day) || day < Day - 6 || day > Day) continue;
                    if (float.TryParse(fields[3].Replace(',', '.'), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float amount) && !float.IsNaN(amount) && !float.IsInfinity(amount))
                        earningsTotal += Mathf.Max(0, amount);
                }
                return earningsTotal;
            }
        }

        public class Payout
        {
            public Job job; public float base_, total; public int rating; public bool late; public string quip;
            public float minutes; public readonly List<(string label, float amount)> lines = new List<(string, float)>();
        }

        static readonly Dictionary<int, string[]> Quips = new Dictionary<int, string[]>
        {
            { 5, new[] { "Majstore! Ovo je za udžbenik.", "Skladištar je zaplakao od sreće.", "Zdravko kaže: „Takve volim.“" } },
            { 4, new[] { "Dobro je to. Skoro savršeno.", "Šef zadovoljan. Rijetko, ali istina." } },
            { 3, new[] { "Može. Moglo je i bolje, ali može.", "Ni po babu ni po stričevima." } },
            { 2, new[] { "Hmm. Klijent je šutio. To nije dobro.", "Bilo je i boljih dana." } },
            { 1, new[] { "Zdravko je uzdahnuo. Dugo.", "Bit će bolje sutra. Mora biti." } },
        };

        Payout card; float cardT;
        float lastHit = -10;

        public static Payout ComputePayout(Job j, float gameMinutes, bool reversedIn)
        {
            var p = new Payout { job = j, base_ = j.pay, minutes = gameMinutes };
            float b = j.pay;
            p.late = gameMinutes > j.deadlineMin;
            if (j.fatureta)
            {
                // a fatureta pays the agreed price; the customer takes off for broken cargo and for missing his window, no bonuses
                if (p.late) { float over = gameMinutes - j.deadlineMin; p.lines.Add(($"Kašnjenje ({Mathf.Round(over):0} min)", -Mathf.Round(b * Mathf.Min(0.5f, 0.15f + over / 600f)))); }
                if (j.cargoDamage > 0.5f) p.lines.Add(($"Šteta na teretu ({Mathf.Round(j.cargoDamage):0} %)", -Mathf.Round(b * Mathf.Min(0.9f, j.cargoDamage / 100f))));
                float sum = b; foreach (var l in p.lines) sum += l.amount;
                p.total = Mathf.Max(0, Mathf.Round(sum * 2) / 2);
                int rr = 5; if (p.late) rr--; if (j.cargoDamage > 10) rr--; if (j.cargoDamage > 35) rr--; if (j.truckDamage > 15) rr--;
                p.rating = Mathf.Max(1, rr);
                p.quip = I ? I.FaturetaQuip(j, p) : "";
                return p;
            }
            if (p.late)
            {
                float over = gameMinutes - j.deadlineMin, pct = Mathf.Min(0.5f, 0.15f + over / 600f);
                p.lines.Add(($"Kašnjenje ({Mathf.Round(over):0} min)", -Mathf.Round(b * pct)));
            }
            if (j.cargoDamage > 0.5f)
            {
                float pct = Mathf.Min(0.9f, j.cargoDamage / 100f);
                p.lines.Add(($"Šteta na teretu ({Mathf.Round(j.cargoDamage):0} %)", -Mathf.Round(b * pct)));
            }
            if (reversedIn) p.lines.Add(("Parkiranje unazad na rampu", Mathf.Round(b * 0.1f)));
            if (j.nightMin > gameMinutes * 0.5f) p.lines.Add(("Noćna vožnja", Mathf.Round(b * 0.2f)));
            // bonuses of local region jobs share a budget (economy.ts: 120 € below 12 km)
            float budget = Mathf.Max(0, (j.km >= 12 ? 250 : 120) - b);
            for (int i = 0; i < p.lines.Count; i++)
                if (p.lines[i].amount > 0) { float a = Mathf.Min(p.lines[i].amount, budget); budget -= a; p.lines[i] = (p.lines[i].label, a); }
            float raw = b; foreach (var l in p.lines) raw += l.amount;
            p.total = Mathf.Max(0, Mathf.Round(raw * 2) / 2);
            int r = 5;
            if (p.late) r--;
            if (j.cargoDamage > 10) r--;
            if (j.cargoDamage > 35) r--;
            if (j.truckDamage > 15) r--;
            p.rating = Mathf.Max(1, r);
            var q = Quips[p.rating]; p.quip = q[Random.Range(0, q.Length)];
            return p;
        }

        void OnTruckHit(Collision c)
        {
            if (Current == null || State == Phase.Free) return;
            float force = c.impulse.magnitude / Mathf.Max(1e-4f, Time.fixedDeltaTime);
            float mass = Truck.GetComponent<Rigidbody>().mass;
            float strength = Mathf.Min(1, (force - 60000) / (mass * 60));
            if (strength <= 0.02f || Time.time - lastHit < 0.6f) return;
            lastHit = Time.time;
            bool vehicle = c.rigidbody != null;
            float dmg = strength * (vehicle ? 9 : 6);
            Current.truckDamage = Mathf.Min(100, Current.truckDamage + dmg);
            if (Current.loaded) Current.cargoDamage = Mathf.Min(100, Current.cargoDamage + dmg * 1.2f); // (nothing on board yet: no cargo to break)
            if (strength > 0.25f) Banner(vehicle ? "#@%&! Udario si auto!" : "#@%&! Pazi malo!");
            Debug.Log($"[job] hit {c.collider.name}: {force / 1000:0} kN, strength {strength:0.00}, cargo damage {Current.cargoDamage:0.0} %");
        }

        // ------------------------------------------------------------------ the card (Delivery.tsx, short), HUD style (HudStyle)
        /// "Dvor Bašića – Kaštel Lukšić" → "Dvor Bašića"
        static string Short(string n) { if (n == null) return ""; int k = n.IndexOf(" – "); return k > 0 ? n.Substring(0, k) : n; }

        void DrawCard()
        {
            if (card == null) return;
            var p = card; float K = HudStyle.K;
            float w = Mathf.Min(HudStyle.Px(540), Screen.width - 2 * HudStyle.Margin), x = (Screen.width - w) / 2, pad = HudStyle.Px(24);
            int n = p.lines.Count + 1;
            float h = (24 + 22 + 40 + 22 + 30 + 14 + n * 28 + 14 + 50 + 26 + 18) * K;
            float y = Mathf.Max(HudStyle.Margin, Screen.height * 0.42f - h / 2);
            // rows appear one by one (Delivery.tsx count-up, shortened)
            float age = 10 - cardT;
            var box = new Rect(x, y, w, h);
            HudStyle.Fill(box, new Color(HudStyle.Panel.r, HudStyle.Panel.g, HudStyle.Panel.b, HudStyle.Cover(0.92f)), HudStyle.Px(16));
            HudStyle.Fill(new Rect(x, y, w, HudStyle.Px(4)), HudStyle.Accent, HudStyle.Px(2));
            float cx = x + pad, cw = w - 2 * pad, cy = y + HudStyle.Px(22);
            HudStyle.Label(new Rect(cx, cy, cw, 22 * K), p.late ? "ISPORUČENO – S KAŠNJENJEM" : "ISPORUČENO", HudStyle.St(13, true, p.late ? HudStyle.Highlight : HudStyle.Good));
            HudStyle.Label(new Rect(cx, cy - 4 * K, cw, 28 * K), new string('★', p.rating) + new string('☆', 5 - p.rating), HudStyle.St(22, true, HudStyle.Accent, TextAnchor.MiddleRight));
            cy += 22 * K;
            HudStyle.Label(new Rect(cx, cy, cw, 40 * K), p.job.cargo.Substring(0, 1).ToUpper() + p.job.cargo.Substring(1), HudStyle.St(30, true, Color.white)); cy += 40 * K;
            string sub = p.job.fatureta ? $"Fatureta · {Short(World.Stop(p.job.legs[0].stop)?.name)} → {Short(World.Stop(p.job.legs[p.job.legs.Count - 1].stop)?.name)} · {Magistrala.HudStyle.Dec(p.job.km)} km"
                : p.job.tour != "" ? $"{p.job.label} · {Magistrala.HudStyle.Dec(p.job.km)} km · {Mathf.Round(p.minutes):0} min u igri{(TourReturns(p.job.tour) ? " · prazne gajbe vraćene (u cijeni)" : "")}"
                : $"{World.Stop(p.job.from)?.name} → {p.job.label} · {Magistrala.HudStyle.Dec(p.job.km)} km · {Mathf.Round(p.minutes):0} min u igri";
            HudStyle.Label(new Rect(cx, cy, cw, 22 * K), sub, HudStyle.St(14, false, HudStyle.Muted)); cy += 22 * K;
            HudStyle.Label(new Rect(cx, cy, cw, 30 * K), $"„{p.quip}“", HudStyle.St(15, false, new Color(1, 1, 1, 0.82f))); cy += 30 * K + 14 * K;
            var row = HudStyle.St(17, false, Color.white);
            void Row(string l, float a, int i)
            {
                if (age < 0.4f + i * 0.3f) return;
                HudStyle.Label(new Rect(cx, cy, cw, 26 * K), l, row);
                var c = a < 0 ? HudStyle.Bad : a > 0 && i > 0 ? HudStyle.Good : Color.white;
                HudStyle.Label(new Rect(cx, cy, cw, 26 * K), (i > 0 && a > 0 ? "+" : a < 0 ? "−" : "") + Eur(Mathf.Abs(a)), HudStyle.St(17, true, c, TextAnchor.MiddleRight));
            }
            Row(p.job.fatureta ? $"Dogovoreno · {Who(p.job.customer)}" : p.job.tour != "" ? $"Zarada za turu ({p.job.drops} {Stajanja(p.job.drops)})" : "Osnovna zarada", p.base_, 0); cy += 28 * K;
            for (int i = 0; i < p.lines.Count; i++) { Row(p.lines[i].label, p.lines[i].amount, i + 1); cy += 28 * K; }
            cy += 6 * K; HudStyle.Fill(new Rect(cx, cy, cw, Mathf.Max(1, HudStyle.Px(1))), HudStyle.Line); cy += 8 * K;
            float shown = age < 0.6f + n * 0.3f ? 0 : Mathf.Min(1, (age - 0.6f - n * 0.3f) / 0.8f) * p.total;
            HudStyle.Label(new Rect(cx, cy, cw, 50 * K), p.job.fatureta && p.job.payMode == "racun" ? "Ukupno – račun obrta" : "Ukupno – na ruke", HudStyle.St(20, true, Color.white));
            HudStyle.Label(new Rect(cx, cy, cw, 50 * K), Eur(Mathf.Round(shown * 2) / 2), HudStyle.St(34, true, HudStyle.Highlight, TextAnchor.MiddleRight)); cy += 50 * K;
            if (p.job.fatureta) HudStyle.Label(new Rect(cx, cy, cw, 26 * K), (p.job.payMode == "racun" ? "Račun ide u fascikl „Za Ljubicu“." : "Gotovina u džep, bez računa.") + "   Enter – zatvori", HudStyle.St(14, false, HudStyle.Muted));
            else HudStyle.Label(new Rect(cx, cy, cw, 26 * K), p.late ? $"Rok je bio {Hm(p.job.startMin + p.job.deadlineMin)}.   Enter – zatvori" : $"Na vrijeme (rok {Hm(p.job.startMin + p.job.deadlineMin)}).   Enter – zatvori", HudStyle.St(14, false, HudStyle.Muted));
        }
    }
}
