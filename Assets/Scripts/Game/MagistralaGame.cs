// The first gameplay loop of Kaštela (web game: game/scenario.ts, engine.ts delivery bays, shared/src/driverPay.ts):
// start at the firm's warehouse in Lukšić → take one of Zdravko's firm jobs → drive with the GPS → park in the stop's bay →
// unload → paid in cash (firm pay: 10 € + 3 €/road km, rounded to 0.5 €). Created at scene load (no scene edits); data from
// Resources/MagistralaGame (menu Magistrala/Igra: podaci).
//
// mag-11-19: multi-stop tours from Marko's delivery points (MagistralaGame.Tours.cs) – a tour is legs (load at the pickup, drops,
// for the drinks wholesaler the empties back to the warehouse), the GPS leads to the next leg, one total payout at the end.
//
// Keys: 1–9 take a job (at the warehouse, stopped, after Zdravko's call) · E unload (in the bay, stopped: parking brake on, tail
// lift, FirmUnload.cs) · W/S during the unload stops it · J cancel the job · Enter closes the payout card · N ends the day after
// 17 h at the warehouse · Tab the phone, Y / N answer a call (Phone/MatePhone.cs, MagistralaGame.Day.cs). The help line (TruckHud) lists what applies now.
// Test switches (built player): -nojob  game off (old probe: truck on the D8)   -job <stopId> | <tour>  take that job (or the
//   day's first tour of that type, e.g. -job pice) at start
//   -jobauto  the autopilot drives the GPS route and unloads   -jobnear M  start M metres before the target on the route
//   (-jobnear 0: backed into the target bay; with -jobauto the unload starts at once, and every next leg of a tour is a jump
//   into its bay, the whole tour in a minute)
//   -jobfresh  wallet from zero, nothing saved (implied by -job)   -savefile path  save / load this file instead (tests)
//   -jobabort s  break the first unload off after s seconds   -camorbit yaw,pitch  chase camera orbit at start
//   -kartaoznaka gx,gz  the owner's map waypoint at game x, z at start (WorldMap.cs: -karta [m/px] opens the map at start)
// The owner's waypoint (M map, WorldMap.cs: Enter / click): snapped to the nearest open graph edge; while it is set the GPS
// leads there instead (RoadRouter.RouteTo, the same yellow route on the minimap), the HUD shows "Oznaka"; cleared within
// WaypointReach m, kept in the save (SaveData.wp*).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Magistrala.Game.Phone;

namespace Magistrala.Game
{
    public partial class MagistralaGame : MonoBehaviour
    {
        public const string Warehouse = "kastela_skladiste";
        /// Zdravko's firm jobs from the warehouse (cargo as the web's regionContent / scenario: drinks for the pumps, groceries for the shop)
        static readonly (string to, string label, string cargo)[] FirmJobs =
        {
            ("kastela_pumpa", "Pumpa Kaštela", "napitci i grickalice"),
            ("kastela_pumpa_2", "Marijina pumpa", "napitci i kava"),
            ("ducan_stari", "Dućan u Kaštel Starom", "namirnice"),
        };

        public enum Phase { Free, Loading, Driving, Unloading }
        [System.Serializable]
        public class Job
        {
            public string from, to, label, cargo; public float km, pay, deadlineMin, startMin, nightMin, cargoDamage, truckDamage;
            /// mag-11-19 tours: the tour type ("" = a single drop from the warehouse), its legs, the current one, the cargo mass,
            /// pallets in the box, rows already delivered, drops reversed into
            public string tour = "", key = ""; public List<Leg> legs = new List<Leg>(); public int leg, drops, rowsOff, reversed; public float kg = 2400; public bool loaded;
            /// a fatureta (MagistralaGame.Faturete.cs): its id, the customer, the agreed price is `pay`, "ruke" (cash) or "racun"
            /// (invoice, the trade account), the deadline is the end of the customer's window
            public bool fatureta; public string fatId = "", customer = "", payMode = "ruke"; public float loadSec = 15, unloadSec = 15;
            public Leg Leg => leg < legs.Count ? legs[leg] : null;
        }
        /// one stop of a tour: 'u' load, 'd' drop (`rows` pallet rows off, −1 all), 'r' back to the warehouse with the empties
        [System.Serializable] public class Leg { public string stop, kind = "d"; public int rows = -1; public char K => string.IsNullOrEmpty(kind) ? 'd' : kind[0]; }

        public static MagistralaGame I { get; private set; }
        public GameWorld World { get; private set; }
        public RoadRouter Router { get; private set; }
        public TruckController Truck { get; private set; }
        public Phase State { get; private set; }
        public Job Current { get; private set; }
        public List<Vector3> Route { get; private set; }
        public float RouteMetres { get; private set; }
        public Vector3? Target { get; private set; }
        public float Cash => S.cash;
        public float Account => S.account; // web b140c9c: 200 € on the trade account, 0 € in the pocket
        public int Delivered => S.delivered;
        /// the game keys that apply right now (TruckHud's help line)
        public string HelpKeys { get; private set; }
        public FirmUnload Unload { get; private set; }

        string prompt, banner; float bannerT, phaseT, routeT;
        bool inBay, atWarehouse, save = true, auto, bayReversed, unloadReversed, handCrates;
        char loadingKind = 'a'; float stuckT; // Loading: 'a' the job taken at the warehouse, 'u' loading at a pickup, 'r' handing in the empties
        float nearStart = -1, abortAt = -1;
        string autoJob;
        Vector3 centreLocal; float truckLen = 8.4f;
        GameObject pallets, marker;

        static string Arg(string k) { var a = System.Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == k) return a[i + 1]; return null; }
        static bool Has(string k) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), k) >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Has("-nojob") || I) return;
            var w = Resources.Load<GameWorld>(GameWorld.ResourcePath);
            if (!w) { Debug.LogWarning("[job] no Resources/MagistralaGame — run Magistrala/Igra: podaci"); return; }
            var go = new GameObject("MagistralaGame"); I = go.AddComponent<MagistralaGame>(); I.World = w; go.AddComponent<GpsMap>(); go.AddComponent<WorldMap>();
        }

        void Start()
        {
            Router = new RoadRouter(World);
            Truck = FindFirstObjectByType<TruckController>();
            if (!Truck) { Debug.LogWarning("[job] no truck"); enabled = false; return; }
            autoJob = Arg("-job"); auto = Has("-jobauto");
            if (Arg("-jobnear") is string jn && float.TryParse(jn, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var m)) nearStart = m;
            save = !(Has("-jobfresh") || autoJob != null) || Arg("-savefile") != null;
            if (Arg("-jobabort") is string ja) float.TryParse(ja, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out abortAt);
            LoadSave();
            SetupPhone();
            Magistrala.Traffic.TrafficSignals.RanRed += OnRanRed;
            // the truck's box centre in its own frame (delivery-bay check) and length
            var b = new Bounds(); bool any = false;
            foreach (var r in Truck.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (any) { centreLocal = Truck.transform.InverseTransformPoint(b.center); centreLocal.y = 0; truckLen = Mathf.Clamp(Vector3.Dot(b.size, Abs(Truck.transform.forward)), 6, 12); }
            foreach (var t in Truck.GetComponentsInChildren<Transform>(true)) if (t.name == "load_pallets") pallets = t.gameObject;
            Unload = FirmUnload.Attach(Truck.transform);
            if (Unload && !Unload.Ok) Unload = null;
            SetPallets(false);
            Truck.Hit += OnTruckHit;
            SetupVehicles(); // the Galeb (MagistralaGame.Vehicles.cs)
            // start of the day: in the warehouse bay in Lukšić, backed in, facing out (unless a measuring run drives the D8)
            bool placed = !Has("-autopilot") && Arg("-tp") == null && !Has("-bench");
            if (placed) PlaceAtWarehouse();
            ChooseVehicle(placed);
            if (placed) RestoreFromSave();
            if (S.wpOn) SetWaypoint(new Vector3(S.wpX, 0, S.wpZ), false);
            if (Arg("-kartaoznaka") is string ko)
            {
                var v = ko.Split(',');
                if (v.Length == 2 && float.TryParse(v[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gx)
                    && float.TryParse(v[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gz))
                    SetWaypoint(new Vector3(gx + 4200, 0, -gz - 5300));
            }
            StartDay();
            StartFaturete();
            if (autoJob != null) StartCoroutine(AutoStart());
            // test: -camorbit yaw,pitch  the chase camera's mouse orbit (degrees) at start, e.g. 30,-18 to watch the tail lift
            if (Arg("-camorbit") is string co && FindFirstObjectByType<TruckChaseCamera>() is TruckChaseCamera cam)
            {
                var v = co.Split(','); var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                float.TryParse(v[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var oy);
                float op = 0; if (v.Length > 1) float.TryParse(v[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out op);
                typeof(TruckChaseCamera).GetField("orbitYaw", bf)?.SetValue(cam, oy); typeof(TruckChaseCamera).GetField("orbitPitch", bf)?.SetValue(cam, op);
            }
            Debug.Log($"[job] game on: {World.stops.Length} stops, {World.edges.Length} edges; tail lift {(Unload ? $"{Unload.Rows} pallet rows, {Unload.Duration:0.0} s" : "none")}; {World.source}");
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        System.Collections.IEnumerator AutoStart()
        {
            yield return new WaitForSeconds(1.5f);
            var offers = Offers();
            int k = offers.FindIndex(j => j.tour == autoJob);
            if (k < 0) k = offers.FindIndex(j => j.tour == "" && j.to == autoJob);
            if (k < 0) { Debug.LogWarning($"[job] -job {autoJob}: not among today's jobs ({string.Join(", ", offers.ConvertAll(j => j.tour != "" ? j.tour : j.to))})"); yield break; }
            Accept(k, true);
            yield return new WaitForSeconds(0.5f);
            UpdateRoute();
            if (nearStart == 0) { PlaceAtStop(World.Stop(Current.to)); yield return new WaitForSeconds(1.5f); } // backed into the bay: unload test
            else if (nearStart > 0 && Route != null) PlaceOnRoute(nearStart);
            if (auto && nearStart != 0) DriveRoute();
        }

        // ------------------------------------------------------------------ truck placement
        public void PlaceAtWarehouse() { var s = World.Stop(Warehouse); if (s != null) PlaceAtStop(s); }

        /// backed into the stop's bay, facing out
        void PlaceAtStop(GStop s)
        {
            Vector3 fwd = -new Vector3(s.bayDir.x, 0, s.bayDir.y);
            Place(s.bay - fwd * 0.5f, fwd);
        }

        void Place(Vector3 p, Vector3 fwd)
        {
            var rb = Truck.GetComponent<Rigidbody>();
            p.y = GroundY(p);
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            Vector3 pos = p + Vector3.up * 1.1f - rot * centreLocal;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            Truck.transform.SetPositionAndRotation(pos, rot); rb.position = pos; rb.rotation = rot;
        }

        /// the drivable surface at p: the highest hit no more than 1.5 m above the data height (not a canopy roof over the bay)
        float GroundY(Vector3 p)
        {
            float best = float.NegativeInfinity;
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 30, Vector3.down, 80, ~0, QueryTriggerInteraction.Ignore))
                if (!h.transform.IsChildOf(Truck.transform) && h.point.y <= p.y + 1.5f && h.point.y > best) best = h.point.y;
            return float.IsNegativeInfinity(best) ? p.y : best;
        }

        /// test: put the truck on the route `back` metres before its end
        void PlaceOnRoute(float back)
        {
            float acc = 0;
            for (int i = Route.Count - 1; i > 0; i--)
            {
                float d = Vector3.Distance(Route[i], Route[i - 1]); acc += d;
                if (acc >= back) { Vector3 dir = Route[i] - Route[i - 1]; dir.y = 0; Place(Route[i - 1] + Lane(Route[i - 1], Route[i]), dir); break; }
            }
            UpdateRoute();
            Debug.Log($"[job] placed {back:0} m before the target, route {RouteMetres:0} m");
        }

        // a point's lateral offset to the right-hand lane of the GPS line (test driving only)
        static Vector3 Lane(Vector3 a, Vector3 b) { Vector3 d = b - a; d.y = 0; return Vector3.Cross(Vector3.up, d.normalized) * 1.6f; }

        /// test: the autopilot follows the GPS route (resampled to 4 m, smoothed, right-hand lane)
        void DriveRoute()
        {
            var ap = Truck.GetComponent<Autopilot>(); if (!ap || Route == null) return;
            var raw = new List<Vector3>();
            for (int i = 0; i + 1 < Route.Count; i++)
            {
                Vector3 a = Route[i], b = Route[i + 1]; float L = Vector3.Distance(a, b); if (L < 0.01f) continue;
                for (float d = 0; d < L; d += 4) raw.Add(Vector3.Lerp(a, b, d / L) + Lane(a, b));
            }
            raw.Add(Route[Route.Count - 1]);
            var o = new Vector3[raw.Count];
            for (int i = 0; i < raw.Count; i++) { Vector3 acc = Vector3.zero; int n = 0; for (int k = -2; k <= 2; k++) { int j = Mathf.Clamp(i + k, 0, raw.Count - 1); acc += raw[j]; n++; } o[i] = acc / n; }
            o[o.Length - 1] = Route[Route.Count - 1];
            ap.path = o; ap.cruiseKmh = Current != null && Current.tour != "" ? 30 : 45; ap.drive = true;
            // (Autopilot keeps its segment index from the D8 path it started on: back to the start of the new path)
            typeof(Autopilot).GetField("seg", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(ap, 0);
            Debug.Log($"[job] autopilot on the GPS route: {o.Length} points");
        }

        // ------------------------------------------------------------------ jobs
        /// one of Zdravko's single-drop jobs (FirmJobs), loaded at the warehouse
        public Job Single(int k)
        {
            var (to, label, cargo) = FirmJobs[k];
            if (World.Stop(to) == null) return null;
            float m = World.RoadMetres(Warehouse, to);
            float km = m > 0 ? m / 1000f : 0;
            var j = new Job { from = Warehouse, to = to, label = label, cargo = cargo, km = km, pay = FirmPay(km), key = to, drops = 1, loaded = true };
            j.legs.Add(new Leg { stop = to, kind = "d" });
            return j;
        }
        public static float FirmPay(float km) => Mathf.Round((10 + 3 * Mathf.Max(0, km)) * 2) / 2; // driverPay.ts firmBasePay

        void Accept(int k, bool force = false)
        {
            var list = Offers();
            if (k < 0 || k >= list.Count) return;
            var j = Clone(list[k]);
            if (!force && Current != null) return;
            if (!force && !atWarehouse) { Banner("Ture se preuzimaju u skladištu u Lukšiću."); return; }
            if (!force && Truck.SpeedKmh > 3) { Banner("Stani u skladištu pa biraj turu."); return; }
            if (!force && !ToursOpen) { Banner(FirmHours ? "Zdravko još nije dao ture." : "Firma radi radnim danom 6–17 h."); return; }
            if (!force && InGaleb) { Banner(NearOther ? "Ture firme voziš Ategom. E – uzmi Atego." : "Ture firme voziš Ategom firme, ne Galebom."); return; }
            j.startMin = S.clockMin; j.deadlineMin = TourDeadline(j);
            Current = j; State = Phase.Loading; phaseT = force ? 0.1f : 3f; loadingKind = 'a';
            if (j.loaded) { Truck.SetCargo(j.kg); SetPallets(true); } // four euro pallets of drinks ≈ 2.4 t
            else { Truck.SetCargo(0); SetPallets(false); phaseT = force ? 0.1f : 1.5f; } // a tour: empty to the pickup
            Debug.Log($"[job] accepted {(j.tour != "" ? j.tour + " " : "")}{j.from} → {string.Join(" → ", j.legs.ConvertAll(l => l.kind + ":" + l.stop))}: {j.km:0.00} km, {j.pay:0.0} €, deadline {j.deadlineMin:0} min ({Hm(j.startMin + j.deadlineMin)})");
            SaveNow("job accepted");
        }

        void Cancel()
        {
            if (Current == null) return;
            Debug.Log($"[job] cancelled {Current.to}");
            if (Current.fatureta) FaturetaCancelled(Current);
            if (State == Phase.Unloading) StopUnload(false);
            Truck.parkLock = false;
            bool fat = Current.fatureta;
            Current = null; State = Phase.Free; Truck.SetCargo(0); SetPallets(false); Banner(fat ? "Fatureta otkazana" : "Tura otkazana");
            SaveNow("job cancelled");
        }

        void StartUnload()
        {
            State = Phase.Unloading; unloadReversed = bayReversed;
            Truck.parkLock = true; // parking brake on while the tail lift works (web: airBrake)
            int rows = Current.Leg?.rows ?? -1;
            handCrates = Unload == null || rows == 0;
            if (Current.fatureta) handCrates = true;
            if (!handCrates) { Unload.Begin(rows); phaseT = Unload.Duration; } else phaseT = Current.fatureta ? (auto && nearStart == 0 ? 2 : Current.unloadSec) : Unload == null ? 4f : 5f;
            Banner(Current.fatureta ? $"Ručna povučena. Istovar: {Current.cargo}…" : handCrates && Unload ? "Ručna povučena. Gajbe rukom, potpis na otpremnici…" : "Ručna povučena. Rampa dolje, transpaleta van…");
            Debug.Log($"[job] unload started at {Current.to} ({(unloadReversed ? "reversed in" : "nose in")}), {phaseT:0.0} s");
        }

        void StopUnload(bool done)
        {
            Truck.parkLock = false;
            if (!done && Unload) Unload.Abort();
        }

        void Deliver()
        {
            var j = Current; StopUnload(true);
            bool rev = j.tour == "" ? unloadReversed : j.drops > 0 && j.reversed >= j.drops;
            var p = ComputePayout(j, S.clockMin - j.startMin, rev);
            if (j.fatureta) FaturetaPaid(j, p); else S.cash += p.total;
            S.delivered++; S.deliveredJobs.Add($"{Day}|{Hm(S.clockMin)}|{(j.tour != "" ? j.tour + ":" + j.legs.Count : j.to)}|{p.total:0.0}");
            if (j.tour != "" && !j.fatureta) S.toursDone.Add(Day + ":" + j.key);
            card = p; cardT = 10;
            Debug.Log($"[job] delivered {(j.tour != "" ? $"tour {j.tour} ({j.drops} drops)" : j.to)}: base {j.pay:0.0} € {string.Join(" ", p.lines.ConvertAll(l => $"[{l.label} {l.amount:+0.0;-0.0}]"))} = {p.total:0.0} €, rating {p.rating}, {p.minutes:0} game min (deadline {j.deadlineMin:0}), cash {Cash:0.0} €, delivered {Delivered}");
            Current = null; State = Phase.Free; Truck.SetCargo(0);
            if (pallets) pallets.SetActive(false); // (the rows stay at the stop: FirmUnload dropped them)
            NextQueuedFatureta();
            SaveNow("delivered");
            if (auto) { var ap = Truck.GetComponent<Autopilot>(); if (ap) ap.drive = false; Truck.externalInput = false; }
        }

        /// Zdravko's line for the cargo (regionContent.ts SPLIT_KASTELA_CARGO namirnice)
        static string CargoLine(Job j) => j.tour != "" ? TourLine(j) : j.to.StartsWith("ducan") ? "Namirnice za dućan. Rampa dolje, transpaleta van, potpis i dalje." : "Piće za pumpu. Rampa dolje, transpaleta van, potpis i dalje.";

        void SetPallets(bool on) { if (pallets) pallets.SetActive(on); if (Unload) Unload.SetLoaded(on); }
        void Banner(string s) { banner = s; bannerT = 6; }
        /// running a red light: fine from the trade account (račun obrta) and −1 reputation; tune RedFine here
        public const float RedFine = 100f;
        void OnRanRed(string s)
        {
            S.account -= RedFine; S.rep -= 1;
            Banner($"{s} Kazna {RedFine:0} € (račun obrta)");
            Debug.Log($"[kazna] crveno: −{RedFine:0} €, račun {S.account:0.0} €, ugled {S.rep}");
        }
        void OnDestroy() { Magistrala.Traffic.TrafficSignals.RanRed -= OnRanRed; }
        public static string Eur(float v) => v.ToString("0.00", System.Globalization.CultureInfo.GetCultureInfo("hr-HR")) + " €";

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (!Truck) return;
            var kb = Keyboard.current;
            float dt = Time.deltaTime; bannerT -= dt;
            var wh = World.Stop(Warehouse);
            atWarehouse = wh != null && Vector3.Distance(Flat(TruckCentre()), Flat(wh.bay)) < 30;
            prompt = null;
            TickDay(dt, kb);
            var help = new List<string>();
            // the phone takes the keys while it is open (the game is paused) or ringing (Y / N); the map (M) too
            if (WorldMap.IsOpen) kb = null;
            else if (Phone.Open) { kb = null; help.Add("Tab zatvori mobitel"); }
            else if (Phone.Ringing != null) help.Add($"Y javi se ({MatePhone.Who(Phone.Ringing.caller).name}) · N odbij");
            else help.Add(Phone.Unread > 0 ? $"Tab mobitel (poruke: {Phone.Unread}) · M karta" : "Tab mobitel · M karta");
            bool ringing = Phone.Ringing != null;
            if (card != null)
            {
                cardT -= dt;
                if (cardT <= 0 || (!KatoviTool.Active && kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))) card = null;
                else help.Add("Enter zatvori");
            }
            // the other vehicle close by: E takes it (MagistralaGame.Vehicles.cs)
            // (with a fatureta waiting to be loaded only towards the Galeb: at the yard both stand close and E there means "load")
            bool swap = CanSwitch && card == null && (State == Phase.Free || (Current != null && Current.fatureta && !InGaleb));
            if (swap)
            {
                prompt = $"E – uzmi {Other.acc}"; help.Add($"E uzmi {Other.acc}");
                if (kb != null && kb.eKey.wasPressedThisFrame && !DroneCamera.Active) { SwitchVehicle(); kb = null; }
            }
            switch (State)
            {
                case Phase.Free:
                    if (atWarehouse && InGaleb) { if (!swap) prompt = FirmHours && ToursOpen ? "Ture firme voziš Ategom" : null; HomeEndDay(kb, help, ringing); break; }
                    if (atWarehouse)
                    {
                        if (ToursOpen)
                        {
                            int no = Mathf.Min(9, Offers().Count);
                            prompt = Truck.SpeedKmh > 3 ? "Stani u skladištu za ture" : $"Ture: 1–{no} (ili mobitel → Poslovi)";
                            help.Add($"1–{no} uzmi turu");
                            if (kb != null) for (int k = 0; k < no; k++) if (kb[(Key)((int)Key.Digit1 + k)].wasPressedThisFrame) Accept(k);
                        }
                        else if (!FirmHours || S.closedDay == Day)
                        {
                            prompt = S.closedDay == Day && FirmHours ? "Danas nema firmine ture · N – kraj dana" : Hour >= 17 || Day % 7 >= 5 ? "Smjena gotova · N – kraj dana" : null;
                            if (prompt != null) { help.Add("N kraj dana"); if (kb != null && !ringing && kb.nKey.wasPressedThisFrame && Truck.SpeedKmh < 3) EndDay(); }
                        }
                    }
                    else HomeEndDay(kb, help, ringing);
                    break;
                case Phase.Loading:
                    phaseT -= dt;
                    prompt = loadingKind == 'u' ? $"Utovar{(Current.fatureta ? $" ({Current.cargo})" : $" u {World.Stop(Current.to)?.name}")}… {Mathf.CeilToInt(Mathf.Max(0, phaseT))} s" : loadingKind == 'r' ? $"Predaja praznih gajbi i otpremnica… {Mathf.CeilToInt(Mathf.Max(0, phaseT))} s" : Current.loaded ? "Utovar paleta…" : "Preuzimanje naloga…";
                    if (loadingKind != 'a') { help.Add("J otkaži posao"); if (kb != null && kb.jKey.wasPressedThisFrame) { Cancel(); break; } }
                    if (phaseT <= 0)
                    {
                        if (loadingKind == 'a') { State = Phase.Driving; routeT = 0; Banner($"Zdravko: „{CargoLine(Current)} Javi kad istovariš.“"); }
                        else LegDone();
                    }
                    break;
                case Phase.Driving:
                {
                    var s = World.Stop(Current.to);
                    inBay = InBay(s, out bayReversed);
                    float d = Vector3.Distance(Flat(TruckCentre()), Flat(s.bay));
                    if (swap) { }
                    else if (Current.fatureta && !InGaleb && d < 40) prompt = "Faturete voziš Galebom · slijedi GPS";
                    else if (inBay && Truck.SpeedKmh < 1.5f && Current.fatureta)
                    {
                        char lk = Current.Leg?.K ?? 'd';
                        prompt = lk == 'u' ? $"E – utovar ({Current.cargo})" : "E – istovar";
                        help.Add(lk == 'u' ? "E utovar" : "E istovar");
                        if ((kb != null && kb.eKey.wasPressedThisFrame && !DroneCamera.Active) || auto) StartLeg();
                    }
                    else if (inBay && Truck.SpeedKmh < 1.5f)
                    {
                        char lk = Current.Leg?.K ?? 'd';
                        prompt = lk == 'u' ? "E – utovar" : lk == 'r' ? "E – predaj prazne gajbe" : bayReversed ? "E – istovari dizalicom (unazad +10 %)" : "E – istovari dizalicom";
                        help.Add(lk == 'u' ? "E utovar" : lk == 'r' ? "E predaj" : "E istovar");
                        if ((kb != null && kb.eKey.wasPressedThisFrame && !DroneCamera.Active) || auto) StartLeg(); // (E is "up" for the drone)
                    }
                    else if (inBay) prompt = Current.Leg?.K == 'u' ? "Stani za utovar" : "Stani za istovar";
                    else if (d < 35) prompt = Current.Leg?.K == 'u' ? "Parkiraj na označeno mjesto za utovar" : "Parkiraj na označeno mjesto za istovar";
                    help.Add(Current.fatureta ? "J otkaži faturetu" : "J otkaži turu");
                    if (kb != null && kb.jKey.wasPressedThisFrame) Cancel();
                    // test: the autopilot stuck on a tight town corner (it cannot reverse) for 20 s → jump into the bay, logged
                    if (auto && Current != null && Current.tour != "" && nearStart != 0)
                    {
                        stuckT = Truck.SpeedKmh < 1 && !inBay ? stuckT + dt : 0;
                        if (stuckT > 20) { stuckT = 0; Debug.Log($"[job] autopilot stuck {d:0} m before {Current.to}: jump into the bay"); PlaceAtStop(s); }
                    }
                    break;
                }
                case Phase.Unloading:
                {
                    phaseT -= dt;
                    int off = Unload && !handCrates ? Unload.RowsOff : 0, rows = Unload && !handCrates ? Unload.SeqRows : 0;
                    prompt = Current.fatureta ? $"Istovar… {Mathf.CeilToInt(Mathf.Max(0, phaseT))} s" : handCrates ? $"Gajbe rukom… {Mathf.CeilToInt(Mathf.Max(0, phaseT))} s" : rows > 0 ? $"Istovar dizalicom… paleta {Mathf.Min(rows, off + 1)} / {rows} · {Mathf.CeilToInt(Mathf.Max(0, phaseT))} s" : $"Istovar dizalicom… {Mathf.CeilToInt(phaseT)} s";
                    help.Add("W/S prekini istovar · J otkaži posao");
                    bool drive = kb != null && !DroneCamera.Active && (kb.wKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame);
                    // test: -jobabort s  the first unload is broken off after s seconds (as if the player drove off), the autopilot restarts it
                    if (abortAt > 0 && Unload && Unload.T >= abortAt) { abortAt = -1; drive = true; }
                    if (drive || Truck.SpeedKmh > 3)
                    {
                        StopUnload(false); State = Phase.Driving;
                        Banner("Istovar prekinut · parkiraj pa pritisni E.");
                        Debug.Log("[job] unload aborted");
                    }
                    else if (kb != null && kb.jKey.wasPressedThisFrame) Cancel();
                    else if (phaseT <= 0 && (!Unload || !Unload.Running)) LegDone();
                    break;
                }
            }
            if (swap && prompt == null && Other != null) prompt = $"E – uzmi {Other.acc}";
            HelpKeys = help.Count > 0 ? string.Join(" · ", help) : null;
            if (Waypoint is Vector3 wpt && Vector3.Distance(Flat(TruckCentre()), Flat(wpt)) < WaypointReach)
            {
                ClearWaypoint("reached"); Banner("Stigao si do oznake.");
            }
            TickFaturete();
            TickFuel(dt);
            routeT -= dt;
            if (routeT <= 0) { routeT = 1f; UpdateRoute(); }
            UpdateMarker();
        }

        public Vector3 TruckCentre() => Truck.transform.TransformPoint(centreLocal);
        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0, v.z);

        /// engine.ts inBay: box centre within the bay (along: half length − ¼ truck + 3 m, across: half width + 0.8 m), aligned to 21°
        bool InBay(GStop s, out bool reversed)
        {
            Vector3 c = TruckCentre(), f = Truck.transform.forward; f.y = 0; f.Normalize();
            Vector2 r = s.bayDir, d = new Vector2(c.x - s.bay.x, c.z - s.bay.z);
            float along = Vector2.Dot(d, r), across = d.x * -r.y + d.y * r.x, align = f.x * r.x + f.z * r.y;
            reversed = align < 0;
            return Mathf.Abs(along) < s.bayHl - truckLen * 0.25f + 3 && Mathf.Abs(across) < s.bayHw + 0.8f && Mathf.Abs(align) > 0.93f;
        }

        // ------------------------------------------------------------------ the owner's waypoint (WorldMap)
        public const float WaypointReach = 25;
        /// the waypoint on the road (snapped), null = none
        public Vector3? Waypoint { get; private set; }
        RoadRouter.Spot wpSpot;

        /// a waypoint at the road point nearest to u (Unity x / z); the GPS leads there from now on
        public void SetWaypoint(Vector3 u, bool log = true)
        {
            if (Router == null) return;
            var sp = Router.Nearest(new Vector3(u.x, 0, u.z), Vector3.zero);
            if (sp.edge < 0) return;
            wpSpot = sp; Waypoint = sp.p; S.wpOn = true; S.wpX = sp.p.x; S.wpZ = sp.p.z;
            routeT = 0;
            if (log) Debug.Log($"[karta] waypoint at game x={sp.p.x - 4200:0} z={-sp.p.z - 5300:0} ({sp.dist:0} m from the click), edge {sp.edge} '{World.edges[sp.edge].name}'");
        }
        public void ClearWaypoint(string why)
        {
            if (Waypoint == null) return;
            Waypoint = null; S.wpOn = false; routeT = 0;
            Debug.Log($"[karta] waypoint cleared ({why})");
        }

        void UpdateRoute()
        {
            if (Waypoint is Vector3 wp)
            {
                Target = wp;
                Route = Router.RouteTo(TruckCentre(), Truck.transform.forward, wpSpot, out float wm);
                RouteMetres = Route != null ? wm : Vector3.Distance(TruckCentre(), wp);
                return;
            }
            // a fatureta not yet loaded, driving the Atego: first to the parked Galeb
            if (Current != null && Current.fatureta && !Current.loaded && !InGaleb && OtherAt is Vector3 gp)
            {
                var sp = Router.Nearest(gp, Vector3.zero);
                if (sp.edge >= 0) { Target = gp; Route = Router.RouteTo(TruckCentre(), Truck.transform.forward, sp, out float gm); RouteMetres = Route != null ? gm : Vector3.Distance(TruckCentre(), gp); return; }
            }
            // free in the Galeb after work: home to the yard (the warehouse only on a working day before 17 h)
            if (State == Phase.Free && InGaleb && !(FirmHours && S.closedDay != Day))
            {
                var yd = World.Stop(Yard);
                if (yd == null || Vector3.Distance(Flat(TruckCentre()), Flat(yd.bay)) < 30) { Route = null; Target = null; RouteMetres = 0; return; }
                Target = yd.bay; Route = Router.Route(TruckCentre(), Truck.transform.forward, yd.padNode >= 0 ? yd.padNode : yd.accessNode, yd.bay, out float ym);
                RouteMetres = Route != null ? ym : Vector3.Distance(TruckCentre(), yd.bay); return;
            }
            GStop goal = State == Phase.Free ? (atWarehouse ? null : World.Stop(Warehouse)) : Current != null ? World.Stop(Current.to) : null;
            if (goal == null) { Route = null; Target = null; RouteMetres = 0; return; }
            Target = goal.bay;
            var fwd = Truck.transform.forward;
            Route = Router.Route(TruckCentre(), fwd, goal.padNode >= 0 ? goal.padNode : goal.accessNode, goal.bay, out float m);
            RouteMetres = Route != null ? m : Vector3.Distance(TruckCentre(), goal.bay);
        }

        // ------------------------------------------------------------------ target marker (the bay outline + a beam, GTA-like)
        void UpdateMarker()
        {
            GStop s = State == Phase.Driving || State == Phase.Unloading ? World.Stop(Current.to) : null;
            if (s == null) { if (marker) marker.SetActive(false); return; }
            if (!marker) marker = BuildMarker();
            if (marker.name != s.id)
            {
                marker.name = s.id;
                Vector3 p = s.bay;
                p.y = GroundY(p);
                marker.transform.SetPositionAndRotation(p, Quaternion.LookRotation(new Vector3(s.bayDir.x, 0, s.bayDir.y)));
                var frame = marker.transform.Find("frame");
                frame.localScale = new Vector3(s.bayHw * 2, 1, s.bayHl * 2 + 2);
            }
            marker.SetActive(true);
        }

        GameObject BuildMarker()
        {
            var root = new GameObject("marker");
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(sh ? sh : Shader.Find("Sprites/Default")) { color = new Color(1f, 0.78f, 0.1f) };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(1f, 0.78f, 0.1f));
            var frame = new GameObject("frame").transform; frame.SetParent(root.transform, false);
            void Bar(Vector3 c, Vector3 size)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(frame, false); g.transform.localPosition = c; g.transform.localScale = size;
                var r = g.GetComponent<Renderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // unit frame (scaled to the bay): 0.25 m wide painted bars, 4 cm above the ground
            const float wb = 0.25f;
            Bar(new Vector3(-0.5f, 0.04f, 0), new Vector3(wb / 5f, 0.04f, 1)); Bar(new Vector3(0.5f, 0.04f, 0), new Vector3(wb / 5f, 0.04f, 1));
            Bar(new Vector3(0, 0.04f, 0.5f), new Vector3(1, 0.04f, wb / 20f)); Bar(new Vector3(0, 0.04f, -0.5f), new Vector3(1, 0.04f, wb / 20f));
            var beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Destroy(beam.GetComponent<Collider>());
            beam.name = "beam"; beam.transform.SetParent(root.transform, false); beam.transform.localPosition = new Vector3(0, 12, 0); beam.transform.localScale = new Vector3(0.35f, 12, 0.35f);
            var br = beam.GetComponent<Renderer>(); br.sharedMaterial = mat; br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return root;
        }

        // ------------------------------------------------------------------ HUD data (TruckHud draws the panel), prompt and banner (centre)
        /// where the GPS leads: the current leg's stop, or the warehouse when free and away from it (null = nowhere)
        public string DestName
        {
            get
            {
                if (Waypoint != null) return "Oznaka";
                if (Current != null && Current.fatureta && !Current.loaded && !InGaleb && OtherAt != null) return "Galeb";
                if (Current == null && State == Phase.Free && InGaleb && !(FirmHours && S.closedDay != Day)) return Target != null ? "Dvor Bašića" : null;
                string id = Current != null ? Current.to : State == Phase.Free && !atWarehouse ? Warehouse : null;
                if (id == null) return null;
                return id == Warehouse ? "Skladište u Lukšiću" : World.Stop(id)?.name;
            }
        }
        public bool AtWarehouse => atWarehouse;
        /// damage of the running job's cargo, % (MagistralaGame.Payout.cs; 0 without a job)
        public float CargoDamagePct => Current != null ? Current.cargoDamage : 0;

        /// the lines of the job card above the HUD panel: (text, kind) kind 0 title, 1 text, 2 muted, 3 accent
        public void JobCard(List<(string text, int kind)> l)
        {
            l.Clear();
            if (Current != null)
            {
                if (Current.fatureta)
                {
                    l.Add(($"Fatureta: {Current.label}", 0));
                    l.Add(($"{(Current.loaded ? "Istovar: " + World.Stop(Current.legs[Current.legs.Count - 1].stop)?.name : InGaleb ? "Utovar: " + World.Stop(Current.legs[0].stop)?.name : "Prvo po Galeba")} · {Eur(Current.pay)} {(Current.payMode == "racun" ? "(račun)" : Current.payMode == "ruke" ? "(na ruke)" : "")} · rok {Hm(Current.startMin + Current.deadlineMin)}", 2));
                }
                else if (Current.tour != "")
                {
                    l.Add(($"Tura: {Current.label}", 0));
                    l.Add(($"{LegVerb(Current.Leg)} · {Magistrala.HudStyle.Dec(Current.km)} km · {Eur(Current.pay)} · rok {Hm(Current.startMin + Current.deadlineMin)}", 2));
                }
                else
                {
                    l.Add(($"Tura: {Current.label} – {Current.cargo}", 0));
                    l.Add(($"{Magistrala.HudStyle.Dec(Current.km)} km · zarada {Eur(Current.pay)} · rok {Hm(Current.startMin + Current.deadlineMin)}", 2));
                }
                if (Current.cargoDamage > 0.5f) l.Add(($"Šteta na teretu {Current.cargoDamage:0} %", 3));
            }
            else if (atWarehouse && ToursOpen)
            {
                l.Add(("Zdravkove ture iz skladišta", 0));
                var of = Offers();
                for (int k = 0; k < of.Count && k < 9; k++) l.Add(($"{k + 1}   {OfferLine(of[k])}", 1));
            }
            else if (atWarehouse)
                l.Add((S.closedDay == Day && FirmHours ? "Danas nema firmine ture." : !FirmHours ? (Hour < 6 && Day % 7 < 5 ? "Skladište otvara u 6:00. Zdravko zove." : "Smjena gotova · radno vrijeme: pon–pet 6–17 h.") : "Čeka se Zdravkov poziv.", 1));
            // slobodan i daleko od skladišta: bez kartice (odredište "Skladište u Lukšiću" je već na HUD-u)
        }

        void OnGUI()
        {
            if (Magistrala.GpuSwitches.NoHud || !Truck || Event.current.type != EventType.Repaint || WorldMap.IsOpen) return;
            if (prompt != null) Pill(Screen.height * 0.62f, prompt, 1);
            if (bannerT > 0 && banner != null) Pill(Screen.height * 0.16f, banner, Mathf.Clamp01(bannerT * 2));
            DrawCard();
            DrawFade();
        }

        /// a centred message on a dark pill (prompt / banner)
        static void Pill(float y, string text, float alpha)
        {
            var st = HudStyle.St(21, true, Color.white, TextAnchor.MiddleCenter);
            float w = Mathf.Min(Screen.width - 2 * HudStyle.Margin, HudStyle.Width(st, text) + HudStyle.Px(44)), h = HudStyle.Px(44);
            var r = new Rect((Screen.width - w) / 2, y, w, h);
            HudStyle.PanelBg(r, 22, alpha);
            HudStyle.Fill(new Rect(r.x + HudStyle.Px(18), r.yMax - HudStyle.Px(3), r.width - HudStyle.Px(36), HudStyle.Px(2)), new Color(HudStyle.Accent.r, HudStyle.Accent.g, HudStyle.Accent.b, 0.9f * alpha), 1);
            var old = GUI.color; GUI.color = new Color(1, 1, 1, alpha);
            HudStyle.Label(r, text, st);
            GUI.color = old;
        }

        static string Dist(float m) => HudStyle.Km(m);
    }
}
