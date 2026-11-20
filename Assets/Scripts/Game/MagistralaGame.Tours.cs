// Multi-stop tours from Marko's delivery points (package mag-11-19, imported by GameDataBuilder.Tours.cs into GameWorld.stops with
// pid ≥ 0 and their tours "pice:u" / "pice:d"). BILJESKE 2026-11-19 "Kako firma radi": the firm is a carrier; a wholesaler tour is one
// load and many small drops with the empties back, a chain's / producer's tour one (or two) loads and a few drops.
//   a tour  legs: 'u' load at the pickup (E, a forklift loads the pallets) → 'd' drops in nearest-neighbour order by our road metres
//           (E, the tail lift puts off that stop's share of the pallet rows; a stop with no row left: crates by hand) → for the drinks
//           wholesaler 'r' back to the Lukšić warehouse with the empty crates (E, handed in; part of the price, not paid apart)
//   offers  made each day (seeded by the day) from the firm tours whose pickup and drops are in the game: two drinks tours (one
//           round the wholesaler, one round a random waterfront), one pastry tour, one furniture tour; plus Zdravko's three
//           single drops from the warehouse (FirmJobs, the first loop – kept: short jobs, loaded at the warehouse)
//   pay     one total at the end: 10 € + 3 €/road km of the whole tour (warehouse → pickup → drops → back) + StopPay per stop after
//           the first, rounded to 0.5 €; the deadline on the game clock (TourDeadline); then the usual payout lines
// Fatureta tours (drva, ormar: Galeb, evening, cash) are in the data but not offered: the Galeb is not in Unity yet. Tours whose
// pickup is in Split (brasno, riba, voce) or whose drop is (bicikli, suta) are not playable in Kaštela.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.Game
{
    public partial class MagistralaGame
    {
        public const float StopPay = 5f;
        /// one shift (Marko 2026-11-20: "smjena do 8 sati rada"): a tour is cut to the drops that fit ShiftMin game minutes of
        /// driving (~40 km/h on the game clock) + StopWorkMin per stop (reverse in, unload, paperwork)
        public const float ShiftMin = 8 * 60, StopWorkMin = 15;
        public static float WorkMin(Job j) => j.km * 1000 / 11f * ClockRate + Mathf.Max(0, j.legs.Count - 1) * StopWorkMin;
        /// the tours the game puts together: drops min..max, cargo mass, empties back to the warehouse
        static readonly (string id, int min, int max, float kg, bool ret, int count)[] TourKinds =
        {
            ("pice", 4, 6, 2400, true, 2),
            ("peciva", 3, 6, 600, false, 1),
            ("namjestaj", 1, 3, 1400, false, 1),
        };
        public static bool TourReturns(string tour) { foreach (var k in TourKinds) if (k.id == tour) return k.ret; return false; }

        List<Job> offers; int offersDay = int.MinValue, offersDone = -1;

        /// today's jobs (tours first, then the single drops), without the tours already driven today
        public List<Job> Offers()
        {
            if (offers != null && offersDay == Day && offersDone == S.toursDone.Count) return offers;
            offersDay = Day; offersDone = S.toursDone.Count;
            var all = new List<Job>(); var rnd = new System.Random(Day * 104729 + 31); var used = new HashSet<string>();
            foreach (var k in TourKinds)
            {
                var def = World.Tour(k.id); if (def == null || def.who != "firma") continue;
                for (int c = 0; c < k.count; c++) { var j = MakeTour(def, k, c, rnd, used); if (j != null) all.Add(j); }
            }
            offers = all.FindAll(j => !S.toursDone.Contains(Day + ":" + j.key));
            for (int k = 0; k < FirmJobs.Length; k++) { var j = Single(k); if (j != null) offers.Add(j); }
            return offers;
        }

        float D(string a, string b) => a == b ? 0 : World.RoadMetres(a, b);

        Job MakeTour(GTour def, (string id, int min, int max, float kg, bool ret, int count) k, int c, System.Random rnd, HashSet<string> used)
        {
            var picks = new List<GStop>(); var drops = new List<GStop>();
            foreach (var s in World.stops)
            {
                if (s.Has(k.id, 'u') && D(Warehouse, s.id) >= 0) picks.Add(s);
                if (s.Has(k.id, 'd') && !used.Contains(s.id)) drops.Add(s);
            }
            if (picks.Count == 0 || drops.Count == 0) return null;
            var pick = picks[rnd.Next(picks.Count)];
            drops.RemoveAll(s => D(pick.id, s.id) < 0 || D(s.id, pick.id) < 0 || (k.ret && D(s.id, Warehouse) < 0));
            if (drops.Count == 0) return null;
            int n = Mathf.Min(drops.Count, rnd.Next(k.min, k.max + 1));
            // a cluster: the 2n nearest to the seed (the pickup for the first tour of a kind, a random drop for the next), n of them at random
            var seed = c == 0 ? pick : drops[rnd.Next(drops.Count)];
            float Sym(GStop s) => s == seed ? 0 : D(seed.id, s.id) + D(s.id, seed.id);
            drops.Sort((a, b) => Sym(a).CompareTo(Sym(b)));
            var pool = drops.GetRange(0, Mathf.Min(drops.Count, 2 * n));
            var chosen = new List<GStop>();
            while (chosen.Count < n) { int i = rnd.Next(pool.Count); chosen.Add(pool[i]); pool.RemoveAt(i); }
            // order: nearest neighbour from the pickup
            var order = new List<GStop>(); string cur = pick.id;
            while (chosen.Count > 0)
            {
                int bi = 0; float bd = float.MaxValue;
                for (int i = 0; i < chosen.Count; i++) { float d = D(cur, chosen[i].id); if (d >= 0 && d < bd) { bd = d; bi = i; } }
                order.Add(chosen[bi]); cur = chosen[bi].id; chosen.RemoveAt(bi);
            }
            Job j = null;
            for (; n >= 1; n--)
            {
                j = new Job { tour = k.id, from = pick.id, to = pick.id, cargo = def.cargo, kg = k.kg, drops = n, loaded = false };
                j.legs.Add(new Leg { stop = pick.id, kind = "u" });
                int R = Unload ? Unload.Rows : 4;
                for (int i = 0; i < n; i++) j.legs.Add(new Leg { stop = order[i].id, kind = "d", rows = (i + 1) * R / n - i * R / n });
                if (k.ret) j.legs.Add(new Leg { stop = Warehouse, kind = "r" });
                float m = D(Warehouse, pick.id); string prev = pick.id;
                foreach (var l in j.legs) { if (l.stop == pick.id && l.K == 'u') continue; float d = D(prev, l.stop); if (d < 0) return null; m += d; prev = l.stop; }
                j.km = m / 1000f;
                if (WorkMin(j) <= ShiftMin) break; // the last drops (farthest in the nearest-neighbour order) wait for another day
            }
            if (n < 1) return null;
            order.RemoveRange(n, order.Count - n);
            j.pay = Mathf.Round((10 + 3 * j.km + StopPay * (n - 1)) * 2) / 2;
            j.label = $"{def.cargo} · {n} {Stajanja(n)}";
            j.key = $"{k.id}:{pick.id}:{order[0].id}";
            foreach (var s in order) used.Add(s.id);
            return j;
        }

        public static string Stajanja(int n) => n % 10 == 1 && n % 100 != 11 ? "stajanje" : "stajanja";
        static Job Clone(Job j) => JsonUtility.FromJson<Job>(JsonUtility.ToJson(j));
        /// a single drop: Deadline(km) as before (web regions.ts); a tour: on this game's clock (ClockRate, 1/6 game min per real
        /// second) at ~40 km/h × 1.4 + 6 min per stop + 30 (Deadline's web clock gave a 24 km tour a whole day and more)
        static float TourDeadline(Job j) => j.tour == "" ? Deadline(j.km) : Mathf.Round(j.km * 1000 / 11f * ClockRate * 1.4f + Mathf.Max(0, j.legs.Count - 1) * StopWorkMin + 30);

        public string OfferLine(Job j) => j.tour != "" ? $"{j.label} · {Eur(j.pay)} · {j.km:0.0} km · oko {Mathf.Max(1, Mathf.Round(WorkMin(j) / 30) / 2):0.#} h" : $"{j.label} – {j.cargo} · {j.km:0.0} km · {Eur(j.pay)}";

        /// the phone's second line of a tour: pickup → first … last drop (→ the warehouse), km, deadline, the vehicle note
        public string OfferSub(Job j)
        {
            if (j.tour == "") return $"{j.cargo} · {j.km:0.0} km · {Eur(j.pay)} · rok {Hm(S.clockMin + TourDeadline(j))}";
            var ds = j.legs.FindAll(l => l.K == 'd');
            string first = World.Stop(ds[0].stop)?.name, last = World.Stop(ds[ds.Count - 1].stop)?.name;
            // riva points (maxVehicle "mali"): the Atego is allowed there (Marko 2026-11-20: "smije, ide i u stvarnosti"), so no warning
            return $"{World.Stop(j.from)?.name} → {first}{(ds.Count > 1 ? $" … {last}" : "")}{(TourReturns(j.tour) ? " → skladište (prazne gajbe, u cijeni)" : "")} · {j.km:0.0} km · rok {Hm(S.clockMin + TourDeadline(j))}";
        }

        string LegVerb(Leg l)
        {
            if (l == null) return "";
            if (l.K == 'u') return "Utovar";
            if (l.K == 'r') return "Natrag u skladište s praznim gajbama";
            int n = 0; foreach (var x in Current.legs) { if (x == l) break; if (x.K == 'd') n++; }
            return $"Dostava {n + 1} / {Current.drops}";
        }

        static string TourLine(Job j)
        {
            var w = I.World; string p = w.Stop(j.from)?.name;
            return j.tour switch
            {
                "pice" => $"Piće iz {p}: {j.drops} {Stajanja(j.drops)} po kafićima i konobama. Prazne gajbe vrati u skladište.",
                "peciva" => $"Peciva iz {p}, {j.drops} {Stajanja(j.drops)}. Toplo je, ne vozi okolo.",
                "namjestaj" => $"Namještaj iz {p}, {j.drops} {(j.drops == 1 ? "adresa" : "adrese")}. Pazi na kutove.",
                _ => $"{j.cargo}: {j.drops} {Stajanja(j.drops)}.",
            };
        }

        /// E in the bay of the current leg
        void StartLeg()
        {
            char k = Current.Leg?.K ?? 'd';
            if (k == 'd') { StartUnload(); return; }
            State = Phase.Loading; loadingKind = k; Truck.parkLock = true;
            phaseT = auto && nearStart == 0 ? 2 : k == 'u' ? 8 : 4;
            Banner(k == 'u' ? "Ručna povučena. Viličar utovaruje palete…" : "Prazne gajbe na rampu, otpremnice u ured.");
            Debug.Log($"[job] {(k == 'u' ? "loading" : "empties")} at {Current.to}");
        }

        /// the current leg done: the next one (the GPS leads there), or the tour's end and its payout
        void LegDone()
        {
            var j = Current; char k = j.Leg?.K ?? 'd';
            Truck.parkLock = false;
            if (k == 'u' && !j.loaded) { j.loaded = true; SetPallets(true); }
            if (k == 'd' && unloadReversed) j.reversed++;
            j.leg++;
            if (j.leg >= j.legs.Count) { Deliver(); return; }
            int left = 0; for (int i = j.leg; i < j.legs.Count; i++) if (j.legs[i].K == 'd') left++;
            Truck.SetCargo(j.loaded && j.drops > 0 ? j.kg * left / j.drops : 0);
            j.to = j.Leg.stop; State = Phase.Driving; routeT = 0;
            var next = World.Stop(j.to);
            Banner(j.Leg.K == 'r' ? "Sve isporučeno. Prazne gajbe natrag u skladište u Lukšiću." : $"{LegVerb(j.Leg)}: {next?.name}");
            Debug.Log($"[job] leg {j.leg}/{j.legs.Count} → {j.Leg.kind}:{j.to} ({next?.name}), cargo {Truck.CargoKg:0} kg");
            SaveNow("leg");
            if (auto) StartCoroutine(AutoNext());
        }

        IEnumerator AutoNext()
        {
            yield return new WaitForSeconds(1f);
            if (Current == null) yield break;
            if (nearStart == 0) { PlaceAtStop(World.Stop(Current.to)); yield return new WaitForSeconds(1.5f); UpdateRoute(); }
            else
            {
                // (the test driver cannot reverse: where the route starts behind it, out of a dead-end stop, it is turned round in place)
                UpdateRoute();
                if (Route != null && Route.Count > 3)
                {
                    Vector3 dir = Route[3] - Route[0]; dir.y = 0;
                    if (Vector3.Dot(dir, Truck.transform.forward) < 0) { Place(Route[0], dir); yield return new WaitForSeconds(1f); UpdateRoute(); Debug.Log("[job] autopilot: turned round for the next leg"); }
                }
                DriveRoute();
            }
        }
    }
}
