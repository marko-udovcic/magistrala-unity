// The working day of Kaštela (web: game/scenario.ts scenarioTick, ui/hud/MatePhone.tsx, shared economy.ts REGION_CLOCK) and the save.
//   clock: 660 game minutes (6–17 h) in 66 real minutes → 1/6 game minute per real second; day 0 is a Monday; a new game starts
//   in the Lukšić warehouse at 5:59 and Zdravko rings at 6:00 with the day's tours.
//   calls come through Mate's phone (Phone/MatePhone.cs): 6:00 weekdays (until 15 h, once a day, not during a tour), answered → the
//   approved Q3 choices (Poslovi opens after "Može"), declined → −1 ugled, the tours stay on the warehouse board; unanswered for an
//   hour → the 7:00 angry re-call (−2 once): "Vozim" / "Zaspao sam" (+1) / "Danas ne mogu" (−1, no firm tour today); unanswered for
//   half an hour → −2, no firm tour today (faza1-odgovori Q19, dijalozi Q3). Messages: story ones (Vesna's list at 14 h, once) plus
//   at most 2 random a day (Q21) at two seeded times between 8 and 19 h (regionContent SPLIT_KASTELA_MESSAGES).
//   firm tours only on weekdays 6–17 h after the call; after 17 h (or a closed day) N at the warehouse ends the day — or at
//   home in the Bašić yard (Galeb, faturete: MagistralaGame.Faturete.cs). The next day: a weekday at 5:59, Saturday at 7:59
//   (faturete 8–20 h, no firm), Sunday is skipped (family, nothing to drive: faza1-odgovori Q10).
//   save: Application.persistentDataPath/magistrala-save.json (JsonUtility) – day / time, wallet, reputation, call flags,
//   delivered jobs, the running tour, the stop the truck stands at; loaded on start (old PlayerPrefs wallet migrated once).
// Test switches: -clock HH:MM  start time · -clockx N  clock N× faster · -call [7]  ring the 6:00 (or the 7:00) call at once
//   -blicajdemo  test posts in the Blicaj group (MagistralaGame.Blicaj.cs) · -phone blicaj  open the phone in that app
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using Magistrala.Game.Phone;

namespace Magistrala.Game
{
    /// a parked vehicle's pose in the save (MagistralaGame.Vehicles.cs)
    [Serializable]
    public class ParkPose { public string id; public float x, y, z, yaw; }

    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public float clockMin = 5 * 60 + 59;
        public float cash, account = 200;
        public int delivered, rep;
        public int zdravkoDay = -1, missedDay = -1, recallDay = -1, answeredDay = -1, closedDay = -1;
        public List<string> deliveredJobs = new List<string>();
        public string stop = MagistralaGame.Warehouse;
        // the running tour (pallets in the box), empty = none
        public string jobTo = "";
        public float jobStartMin, jobNightMin, jobCargoDamage, jobTruckDamage;
        public bool jobLoaded;
        public int vesnaDay = -1;
        /// mag-11-19: the running tour whole (legs, the current leg, rows delivered; no legs = none) and the tours driven ("day:key")
        public MagistralaGame.Job job;
        public List<string> toursDone = new List<string>();
        public PhoneSave phone = new PhoneSave();
        /// diesel in the tank, % (MagistralaGame.Fuel.cs); a new game starts at 72 %
        public float fuel = 72;
        /// the owner's map waypoint (WorldMap, Unity x / z on the road), wpOn = set
        public bool wpOn; public float wpX, wpZ;
        /// story phase 3 (MagistralaGame.Vehicles.cs / Faturete.cs): the driven vehicle ("atego" / "galeb" / "kombi"), its exact
        /// pose, every parked one's pose (parks; park* = the single parked pose of older saves), the Galeb's and the Kombi's diesel %
        public string vehicle = "atego";
        public bool posOn, parkOn; public float posX, posY, posZ, posYaw, parkX, parkY, parkZ, parkYaw;
        public List<ParkPose> parks = new List<ParkPose>();
        public float fuelG = 64, fuelK = 80;
        /// faturete: today's offers and their haggling state, accepted ones waiting their turn, when each job may come again
        /// ("id:day"), the customers' opinion of Mate (0..1, hidden), invoices for Ljubica ("day|hh:mm|customer|€")
        public List<MagistralaGame.FatOffer> fatOffers = new List<MagistralaGame.FatOffer>();
        public List<MagistralaGame.Job> fatQueue = new List<MagistralaGame.Job>();
        public List<string> fatNext = new List<string>();
        public float fatRep = 0.5f;
        public int fatPlanDay = -1, fatDone;
        public List<string> invoices = new List<string>();
        /// Blicaj (MagistralaGame.Blicaj.cs): seen reading the phone while driving – the day of it and how many times that day
        public int phoneWarnDay = -1, phoneWarns;
    }

    public partial class MagistralaGame
    {
        public const float ClockRate = 660f / (66 * 60); // game minutes per real second (REGION_CLOCK split-kastela)
        static readonly string[] DayNames = { "ponedjeljak", "utorak", "srijeda", "četvrtak", "petak", "subota", "nedjelja" };

        public SaveData S { get; private set; } = new SaveData();
        public float ClockMin => S.clockMin;
        public int Day => Mathf.FloorToInt(S.clockMin / 1440);
        public float Hour => (S.clockMin % 1440) / 60f;
        public static bool IsNight(float clockMin) { float h = (clockMin % 1440) / 60f; return h >= 21 || h < 5.5f; }
        public bool FirmHours => Day % 7 < 5 && Hour >= 6 && Hour < 17;
        /// tours on the warehouse board today: firm hours, the morning call dealt with, the day not closed
        public bool ToursOpen => FirmHours && S.answeredDay == Day && S.closedDay != Day;
        public static string Hm(float clockMin) { float m = ((clockMin % 1440) + 1440) % 1440; return $"{(int)(m / 60):00}:{(int)(m % 60):00}"; }
        public string DayLabel => $"Dan {Day + 1} · {DayNames[Day % 7]} · {Hm(S.clockMin)}";

        public MatePhone Phone { get; private set; }
        float clockX = 1; float saveT = 30;
        string savePath;

        // ------------------------------------------------------------------ save
        void LoadSave()
        {
            savePath = Arg("-savefile") ?? System.IO.Path.Combine(Application.persistentDataPath, "magistrala-save.json");
            if (!save) { S = new SaveData(); return; }
            try
            {
                if (System.IO.File.Exists(savePath)) { S = JsonUtility.FromJson<SaveData>(System.IO.File.ReadAllText(savePath)) ?? new SaveData(); Debug.Log($"[save] loaded {savePath}: {DayLabel}, cash {S.cash:0.0} €, delivered {S.delivered}, stop {S.stop}, job '{S.jobTo}'"); }
                else
                {
                    S = new SaveData();
                    // the first step kept only the wallet in PlayerPrefs
                    if (PlayerPrefs.HasKey("mag.cash")) { S.cash = PlayerPrefs.GetFloat("mag.cash", 0); S.account = PlayerPrefs.GetFloat("mag.account", 200); S.delivered = PlayerPrefs.GetInt("mag.delivered", 0); }
                    Debug.Log($"[save] new game ({savePath})");
                }
            }
            catch (Exception e) { Debug.LogWarning($"[save] unreadable, new game: {e.Message}"); S = new SaveData(); }
        }

        public void SaveNow(string why)
        {
            if (!save || savePath == null || !Truck) return;
            // where the truck stands: a stop within 40 m of its bay (else the last one it stood at)
            var c = Flat(TruckCentre()); GStop best = null; float bd = 40;
            foreach (var s in World.stops) { float d = Vector3.Distance(c, Flat(s.bay)); if (d < bd) { bd = d; best = s; } }
            if (best != null) S.stop = best.id;
            if (Current != null)
            {
                S.job = Clone(Current); S.job.rowsOff = Unload ? Unload.Rows - Unload.RowsIn : 0;
                if (State == Phase.Loading && loadingKind == 'a') S.job.leg = 0;
                S.jobTo = Current.to; S.jobStartMin = Current.startMin; S.jobNightMin = Current.nightMin;
                S.jobCargoDamage = Current.cargoDamage; S.jobTruckDamage = Current.truckDamage; S.jobLoaded = State != Phase.Loading;
            }
            else { S.jobTo = ""; S.job = null; }
            if (Phone) S.phone = Phone.Export();
            SaveVehicles();
            try { System.IO.File.WriteAllText(savePath, JsonUtility.ToJson(S, true)); Debug.Log($"[save] {why}: {DayLabel}, stop {S.stop}, cash {S.cash:0.0} €"); }
            catch (Exception e) { Debug.LogWarning($"[save] failed: {e.Message}"); }
        }

        void OnApplicationQuit() => SaveNow("quit");

        /// after loading: the truck at the saved stop (backed into its bay), the running tour back in the box
        void RestoreFromSave()
        {
            var s = World.Stop(S.stop);
            if (S.posOn && fleet.Count > 1) PlacePose(Truck, new Vector3(S.posX, S.posY, S.posZ), S.posYaw); // exact (two vehicles may share a stop)
            else if (s != null && s.id != Warehouse) PlaceAtStop(s);
            if (S.job != null && S.job.legs != null && S.job.legs.Count > 0 && S.job.leg < S.job.legs.Count && S.job.legs.TrueForAll(l => World.Stop(l.stop) != null))
            {
                var j = Clone(S.job); j.to = j.Leg.stop; j.deadlineMin = TourDeadline(j);
                if (j.fatureta && j.loaded && !InGaleb) { Debug.LogWarning("[save] loaded fatureta but not in the Galeb: dropped"); S.job = null; return; }
                Current = j; State = Phase.Driving;
                SetPallets(j.loaded); if (j.loaded && Unload) Unload.SetDelivered(j.rowsOff);
                int left = 0; for (int i = j.leg; i < j.legs.Count; i++) if (j.legs[i].K == 'd') left++;
                Truck.SetCargo(j.loaded && j.drops > 0 ? j.kg * left / j.drops : 0);
                Banner($"Tura se nastavlja: {j.label} · {LegVerb(j.Leg)}");
            }
            else if (!string.IsNullOrEmpty(S.jobTo))
            {
                int k = Array.FindIndex(FirmJobs, j => j.to == S.jobTo);
                var j = k >= 0 ? Single(k) : null;
                if (j != null)
                {
                    j.startMin = S.jobStartMin; j.nightMin = S.jobNightMin; j.cargoDamage = S.jobCargoDamage; j.truckDamage = S.jobTruckDamage;
                    j.deadlineMin = Deadline(j.km);
                    Current = j; State = Phase.Driving; Truck.SetCargo(2400); SetPallets(true);
                    Banner($"Tura se nastavlja: {j.label}");
                }
            }
        }

        // ------------------------------------------------------------------ clock and calls
        void StartDay()
        {
            clockX = 1;
            if (Arg("-clockx") is string cx && float.TryParse(cx, NumberStyles.Float, CultureInfo.InvariantCulture, out var x)) clockX = Mathf.Max(0.1f, x);
            if (Arg("-clock") is string ck)
            {
                var p = ck.Split(':');
                if (p.Length == 2 && int.TryParse(p[0], out int hh) && int.TryParse(p[1], out int mm)) S.clockMin = Day * 1440 + hh * 60 + mm;
            }
            if (Has("-call"))
            {
                bool angry = Arg("-call") == "7";
                S.zdravkoDay = Day; if (angry) { S.missedDay = Day; S.recallDay = Day; S.rep -= 2; }
                Phone.Ring(angry ? Recall7() : Call6());
            }
            // test: -phonedemo  a story and a random message · -phone home|poruke|pozivi|kontakti|poslovi|banka|policija (blicaj)|javi|
            //   postavke|thread  open the phone there · -phonescroll px  scroll that app (screenshots of the lower part)
            if (Has("-phonedemo")) { Phone.Message("vesna", "Kupi kruv i mliko, i ono šta san ti rekla.", true); Phone.TryRandom(Day, "zare", "Bura na D8. Prazan kamion je jedrilica, drži volan."); }
            if (Has("-blicajdemo")) Phone.BlicajDemo();
            if (Arg("-phone") is string ph) StartCoroutine(OpenPhoneLater(ph));
            // test: -callpick k  answer the next ringing call after 3 s, pick answer k (1-based) 3 s later, end it 3 s later
            if (Arg("-callpick") is string cp && int.TryParse(cp, out int pick)) StartCoroutine(CallPick(pick - 1));
            Debug.Log($"[day] {DayLabel}, clock ×{clockX:0.#}");
        }

        System.Collections.IEnumerator OpenPhoneLater(string screen)
        {
            yield return new WaitForSeconds(2.5f);
            var sc = screen switch
            {
                "poruke" => MatePhone.Screen.Messages, "pozivi" => MatePhone.Screen.Calls, "kontakti" => MatePhone.Screen.Contacts, "poslovi" => MatePhone.Screen.Jobs,
                "blicaj" or "policija" => MatePhone.Screen.Blicaj, "javi" => MatePhone.Screen.BlicajJavi, "banka" => MatePhone.Screen.Bank, "postavke" => MatePhone.Screen.Settings,
                "thread" => MatePhone.Screen.Thread, _ => MatePhone.Screen.Home,
            };
            Phone.Show(sc);
            if (sc == MatePhone.Screen.Thread) Phone.OpenThread("vesna");
            if (Arg("-phonescroll") is string ps && float.TryParse(ps, NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
            {
                yield return new WaitForSecondsRealtime(2); // (the app has laid out its content by then)
                Phone.ScrollTo(px);
            }
        }

        System.Collections.IEnumerator CallPick(int k)
        {
            while (Phone.Ringing == null) yield return null;
            yield return new WaitForSecondsRealtime(3); Phone.Answer();
            yield return new WaitForSecondsRealtime(3); Phone.ChooseIndex(k);
            yield return new WaitForSecondsRealtime(3); Phone.EndCallPublic();
        }

        void SetupPhone()
        {
            Phone = gameObject.AddComponent<MatePhone>();
            Phone.Import(S.phone);
            Phone.clock = () => S.clockMin;
            Phone.DayText = () => $"{char.ToUpper(DayNames[Day % 7][0])}{DayNames[Day % 7].Substring(1)}, dan {Day + 1}";
            Phone.toast = Banner;
            Phone.changed = () => SaveNow("phone");
            Phone.openMap = () => { if (GetComponent<WorldMap>() is WorldMap wm) wm.Open(); };
            Phone.bank = () =>
            {
                var b = new BankInfo { account = S.account, cash = S.cash, debt = LoanLeft, debtTotal = LoanTotal, rate = LoanRate, loanName = "GALEB TRANSPORT · Mate jamac-platac" };
                // the last payments into the account: the invoiced faturete (S.invoices "day|hh:mm|customer|€"), newest first
                for (int i = S.invoices.Count - 1; i >= 0 && b.moves.Count < 8; i--)
                {
                    var p = S.invoices[i].Split('|');
                    if (p.Length < 4 || !int.TryParse(p[0], out int d) || !float.TryParse(p[3].Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var eur)) continue;
                    b.moves.Add((p[2], $"{DayNames[d % 7]} {p[1]} · fatureta, račun", eur));
                }
                return b;
            };
            Phone.jobsProvider = () =>
            {
                var l = new List<PhoneJob>();
                if (!ToursOpen || Current != null) return l;
                var of = Offers();
                for (int k = 0; k < of.Count && k < 9; k++)
                {
                    var j = of[k]; int kk = k;
                    l.Add(new PhoneJob { label = $"{k + 1}  {(j.tour != "" ? OfferLine(j) : j.label)}", sub = OfferSub(j), take = () => { Accept(kk); if (Current != null) Phone.Close(); } });
                }
                return l;
            };
            Phone.jobsNote = () =>
                Current != null ? $"Tura u tijeku: {Current.label}. Javi kad istovariš." :
                !FirmHours ? "Firma radi radnim danom od 6 do 17 h." :
                S.closedDay == Day ? "Danas nema firmine ture." :
                S.answeredDay != Day ? "Čeka se Zdravkov jutarnji poziv." :
                !atWarehouse ? "Ture se preuzimaju u skladištu u Lukšiću (GPS te vodi)." :
                Truck.SpeedKmh > 3 ? "Stani u skladištu pa biraj turu." : $"Biraj turu (1–{Mathf.Min(9, Offers().Count)}).";
            Phone.outgoing = who => who switch
            {
                "zdravko" => Current != null && Current.tour != "" ? $"Još {Current.legs.Count - Current.leg} do kraja ture. Javi kad završiš." : Current != null ? "Javi kad istovariš. I ne zovi me dok voziš." : ToursOpen ? "Ture su u Poslovima. Biraj, Bašiću." : "Bašiću, sad nemam ništa za tebe.",
                "vesna" => "Vozi polako. Vidimo se navečer.",
                "zare" => "Polako, Mate. Cesta neće pobić, a ćevapi oće.",
                "marija" => "Mate! Burek je baš sad izašao. Svrati kad stigneš.",
                "punica" => "Mate, sine, samo jedna sitnica: ormar bi trebalo prenijeti, nije težak, puna je samo jedna ladica.",
                _ => null,
            };
            SetupBlicaj();
        }

        /// the loan Mate guarantees (scenario + faza1-odgovori: 20.000 € (Marko, 26. 11.) on ćaća's obrt, the first instalment 300 €): shown in the
        /// Banka app; nothing is paid off yet – the debt system (instalments, the bank's letter) comes with the story
        public const float LoanTotal = 20000, LoanRate = 300;
        public float LoanLeft => LoanTotal;

        /// 6:00 (MatePhone.tsx ZdravkoCall + the approved Q3 choices)
        PhoneCall Call6()
        {
            var c = new PhoneCall
            {
                caller = "zdravko", title = $"Dolazni poziv · {Hm(S.clockMin)}", sub = "Dispečer",
                lines = new[] { "Bašiću, dobro jutro. Ture za danas su u aplikaciji, biraj jednu." }, timeoutMin = 60,
                declineToast = "Propušteni poziv: Zdravko. „Dobro, Bašiću. Zapamtit ću.“",
                onDecline = () => { S.rep -= 1; S.answeredDay = Day; },
                onMissed = () => { S.missedDay = Day; Debug.Log("[day] 6:00 call missed"); },
            };
            c.choices.Add(new PhoneChoice { label = "„Može. Krećem u skladište.“", reply = "„Nalog ti je u Poslovima.“", openJobs = true, act = () => { S.rep += 1; S.answeredDay = Day; } });
            c.choices.Add(new PhoneChoice { label = "„Može, ali ako zapnem, javim. Neću obećati praznu cestu.“", reply = "„Javi prije deset, ne poslije.“", openJobs = true, act = () => S.answeredDay = Day });
            c.choices.Add(new PhoneChoice { label = "„Je li tih deset i za slušanje tebe do deset?“", reply = "„Za to nema dovoljno novca.“", openJobs = true, act = () => { S.rep -= 1; S.answeredDay = Day; } });
            c.choices.Add(new PhoneChoice { label = "„Danas ne mogu, Zdravko. Treba mi dan doma.“", reply = "„Dobro. Ali drugi put reci ranije.“", act = () => { S.rep -= 3; S.closedDay = Day; } });
            return c;
        }

        /// 7:00, angry (MatePhone.tsx ZdravkoRecall)
        PhoneCall Recall7()
        {
            var c = new PhoneCall
            {
                caller = "zdravko", title = $"Dolazni poziv · {Hm(S.clockMin)}", sub = "Dispečer · ljut",
                lines = new[] { "Bašiću, sedam je. Ja od šest tražim vozača, a kamion stoji. Novi i Stari do deset — voziš ili tražim drugoga?" }, timeoutMin = 30,
                declineToast = "Zdravko: „Više ni ne čekam.“",
                onDecline = () => { S.rep -= 1; S.closedDay = Day; },
                onMissed = () => { S.rep -= 2; S.closedDay = Day; Banner("Propušteni poziv: Zdravko. Danas nema firmine ture."); Debug.Log("[day] 7:00 call missed: day closed"); },
            };
            c.choices.Add(new PhoneChoice { label = "„Vozim. Krećem odmah.“", reply = "„Onda se vidimo na rampi.“", openJobs = true, act = () => S.answeredDay = Day });
            c.choices.Add(new PhoneChoice { label = "„Zaspao sam. Moja greška.“", reply = "„Onda danas preskoči kavu.“", openJobs = true, act = () => { S.rep += 1; S.answeredDay = Day; } });
            c.choices.Add(new PhoneChoice { label = "„Danas ipak ne mogu. Nemoj me čekati.“", reply = "„Više ni ne čekam.“", act = () => { S.rep -= 1; S.closedDay = Day; } });
            return c;
        }

        // garage messages (regionContent.ts SPLIT_KASTELA_MESSAGES) with a sender
        static readonly (string from, string text)[] RandomMsgs =
        {
            ("zdravko", "Bašiću, luka, rampa tri, u 10. Ne u 10 i petnaest."),
            ("zdravko", "Mate, gazda je ljut. Ovaj put stvarno."),
            ("zare", "Gužva na D8 od sedam do osam. Idi kroz mjesta, ima pekara."),
            ("zare", "Kamper na D8 vozi 40. Ja ne vozim, ja zovem."),
            ("zare", "Bura na D8. Prazan kamion je jedrilica, drži volan."),
            ("zdravko", "Bašiću, aerodrom, dostavni ulaz, do jedanaest. Ne u jedanaest i pet."),
            ("zdravko", "Cement iz Sućurca za gradilište. Težak je, koči na vrime."),
            ("zare", "Pozive vodi preko handsfreeja. Poruke pročitaj kad parkiraš."),
        };

        void TickDay(float dt, Keyboard kb)
        {
            float before = S.clockMin;
            S.clockMin += dt * ClockRate * clockX; // dt = 0 while the phone is open (timeScale 0): the clock stops
            if (Current != null && IsNight(S.clockMin)) Current.nightMin += S.clockMin - before;
            int day = Day;
            if (Phone.Ringing == null && Current == null && State == Phase.Free && card == null)
            {
                if (FirmHours && Hour < 15 && S.zdravkoDay != day) { S.zdravkoDay = day; Phone.Ring(Call6()); }
                else if (FirmHours && S.missedDay == day && S.recallDay != day && S.answeredDay != day && S.closedDay != day) { S.recallDay = day; S.rep -= 2; Phone.Ring(Recall7()); }
            }
            // story: Vesna's list at 14 h (once); random: two seeded times a day between 8 and 19 h, at most 2 (MatePhone counts)
            if (Hour >= 14 && Hour < 20 && S.vesnaDay < 0) { S.vesnaDay = day; Phone.Message("vesna", "Kupi kruv i mliko, i ono šta san ti rekla.", true); }
            var rnd = new System.Random(day * 7919 + 17);
            for (int i = 0; i < 2; i++)
            {
                float at = day * 1440 + (8 + (float)rnd.NextDouble() * 11) * 60; int pick = rnd.Next(RandomMsgs.Length);
                if (before < at && S.clockMin >= at && Phone.RandomToday(day) <= i) Phone.TryRandom(day, RandomMsgs[pick].from, RandomMsgs[pick].text);
            }
            saveT -= dt;
            if (saveT <= 0) { saveT = 60; if (Truck.SpeedKmh < 1) SaveNow("periodic"); }
        }

        /// N at the warehouse after the shift (or on a closed day), or at home in the yard: the next day — a weekday at 5:59,
        /// Saturday at 7:59 (faturete only), Sunday skipped (family)
        void EndDay()
        {
            int d = Day + 1; bool sunday = d % 7 == 6; if (sunday) d++;
            bool sat = d % 7 == 5;
            S.clockMin = d * 1440 + (sat ? 7 * 60 + 59 : 5 * 60 + 59);
            DropOffers("end of day");
            Banner(sat ? "Subota. Nema firme – faturete s Galebom od 8 do 20." : sunday ? $"Nedjelja je bila za obitelj. Novi tjedan: {DayNames[d % 7]}." : $"Novi radni dan: {DayNames[d % 7]}. {Atego?.label ?? "Atego"} čeka u Lukšiću.");
            Debug.Log($"[day] end of day → {DayLabel}");
            SaveNow("end of day");
        }

        /// N – end of the day at home: in the Bašić yard, free and stopped, outside the firm's hours (or a closed day / weekend)
        void HomeEndDay(Keyboard kb, List<string> help, bool ringing)
        {
            var y = World.Stop(Yard);
            if (y == null || Vector3.Distance(Flat(TruckCentre()), Flat(y.bay)) > 45 || Current != null) return;
            if (FirmHours && S.closedDay != Day) return;
            if (prompt == null) prompt = "Doma si · N – kraj dana";
            help.Add("N kraj dana");
            if (kb != null && !ringing && kb.nKey.wasPressedThisFrame && Truck.SpeedKmh < 3) EndDay();
        }

        /// web regions.ts: expected game minutes at ~55 km/h (realSec = m / 15.3, 0.5 game min / s) × 1.8 + 45
        public static float Deadline(float km) => Mathf.Round(km * 1000 / 15.3f * 0.5f * 1.8f + 45);

        static Texture2D Tex(Color c) { var t = new Texture2D(1, 1); t.SetPixel(0, 0, c); t.Apply(); return t; }
    }
}
