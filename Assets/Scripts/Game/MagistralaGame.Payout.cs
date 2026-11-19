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
            Current.cargoDamage = Mathf.Min(100, Current.cargoDamage + dmg * 1.2f);
            if (strength > 0.25f) Banner(vehicle ? "#@%&! Udario si auto!" : "#@%&! Pazi malo!");
            Debug.Log($"[job] hit {c.collider.name}: {force / 1000:0} kN, strength {strength:0.00}, cargo damage {Current.cargoDamage:0.0} %");
        }

        // ------------------------------------------------------------------ the card (Delivery.tsx, short)
        GUIStyle cdLabel, cdTitle, cdSub, cdRow, cdTotal, cdQuip;
        Texture2D cdBg, cdLine;

        void DrawCard()
        {
            if (card == null) return;
            if (cdBg == null)
            {
                cdBg = Tex(new Color(0.05f, 0.06f, 0.07f, 0.93f)); cdLine = Tex(new Color(1, 1, 1, 0.18f));
                cdLabel = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.45f, 0.85f, 0.5f) } };
                cdTitle = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
                cdSub = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, normal = { textColor = new Color(0.72f, 0.74f, 0.77f) } };
                cdRow = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = Color.white } };
                cdTotal = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(1f, 0.83f, 0.3f) } };
                cdQuip = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Italic, wordWrap = true, normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
            }
            var p = card; float w = Mathf.Min(520, Screen.width - 24), x = (Screen.width - w) / 2;
            int n = p.lines.Count + 1;
            float h = 20 + 18 + 38 + 20 + 26 + 12 + n * 26 + 12 + 46 + 24;
            float y = Mathf.Max(12, Screen.height * 0.42f - h / 2);
            // rows appear one by one (Delivery.tsx count-up, shortened)
            float age = 10 - cardT;
            GUI.DrawTexture(new Rect(x, y, w, h), cdBg);
            float cy = y + 16;
            GUI.Label(new Rect(x + 20, cy, w - 40, 18), p.late ? "ISPORUČENO – S KAŠNJENJEM" : "ISPORUČENO", cdLabel);
            GUI.Label(new Rect(x + 20, cy - 6, w - 40, 30), new string('★', p.rating) + new string('☆', 5 - p.rating), new GUIStyle(cdLabel) { alignment = TextAnchor.UpperRight, fontSize = 24, normal = { textColor = new Color(1f, 0.83f, 0.3f) } });
            cy += 18;
            GUI.Label(new Rect(x + 20, cy, w - 40, 38), p.job.cargo.Substring(0, 1).ToUpper() + p.job.cargo.Substring(1), cdTitle); cy += 38;
            string sub = p.job.tour != "" ? $"{p.job.label} · {p.job.km:0.0} km · {Mathf.Round(p.minutes):0} min u igri{(TourReturns(p.job.tour) ? " · prazne gajbe vraćene (u cijeni)" : "")}"
                : $"{World.Stop(p.job.from)?.name} → {p.job.label} · {p.job.km:0.0} km · {Mathf.Round(p.minutes):0} min u igri";
            GUI.Label(new Rect(x + 20, cy, w - 40, 20), sub, cdSub); cy += 20;
            GUI.Label(new Rect(x + 20, cy, w - 40, 26), $"„{p.quip}“", cdQuip); cy += 26 + 12;
            void Row(string l, float a, int i, bool money = true)
            {
                if (age < 0.4f + i * 0.3f) return;
                GUI.Label(new Rect(x + 20, cy, w - 40, 24), l, cdRow);
                var st = new GUIStyle(cdRow) { alignment = TextAnchor.UpperRight, normal = { textColor = a < 0 ? new Color(1f, 0.5f, 0.45f) : a > 0 && i > 0 ? new Color(0.5f, 0.9f, 0.55f) : Color.white } };
                GUI.Label(new Rect(x + 20, cy, w - 40, 24), (i > 0 && a > 0 ? "+" : a < 0 ? "−" : "") + Eur(Mathf.Abs(a)), st);
            }
            Row(p.job.tour != "" && p.job.legs.Count > 1 ? $"Osnovna zarada (10 € + 3 €/km + {StopPay:0} € po stajanju)" : "Osnovna zarada (10 € + 3 €/km)", p.base_, 0); cy += 26;
            for (int i = 0; i < p.lines.Count; i++) { Row(p.lines[i].label, p.lines[i].amount, i + 1); cy += 26; }
            cy += 4; GUI.DrawTexture(new Rect(x + 20, cy, w - 40, 1), cdLine); cy += 8;
            float shown = age < 0.6f + n * 0.3f ? 0 : Mathf.Min(1, (age - 0.6f - n * 0.3f) / 0.8f) * p.total;
            GUI.Label(new Rect(x + 20, cy + 8, w - 40, 30), "Ukupno – na ruke", new GUIStyle(cdRow) { fontSize = 20, fontStyle = FontStyle.Bold });
            GUI.Label(new Rect(x + 20, cy, w - 40, 46), Eur(Mathf.Round(shown * 2) / 2), cdTotal); cy += 46;
            GUI.Label(new Rect(x + 20, cy, w - 40, 20), p.late ? $"Rok je bio {Hm(p.job.startMin + p.job.deadlineMin)}." : $"Na vrijeme (rok {Hm(p.job.startMin + p.job.deadlineMin)}).   Enter – zatvori", cdSub);
        }
    }
}
