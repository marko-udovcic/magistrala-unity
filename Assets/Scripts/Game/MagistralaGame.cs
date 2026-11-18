// The first gameplay loop of Kaštela (web game: game/scenario.ts, engine.ts delivery bays, shared/src/driverPay.ts):
// start at the firm's warehouse in Lukšić → take one of Zdravko's firm jobs → drive with the GPS → park in the stop's bay →
// unload → paid in cash (firm pay: 10 € + 3 €/road km, rounded to 0.5 €). Created at scene load (no scene edits); data from
// Resources/MagistralaGame (menu Magistrala/Igra: podaci).
//
// Keys: 1–3 take a job (at the warehouse, stopped, after Zdravko's call) · E unload (in the bay, stopped: parking brake on, tail
// lift, FirmUnload.cs) · W/S during the unload stops it · J cancel the job · Enter closes the payout card · N ends the day after
// 17 h at the warehouse · Tab the phone, Y / N answer a call (Phone/MatePhone.cs, MagistralaGame.Day.cs). The help line (TruckHud) lists what applies now.
// Test switches (built player): -nojob  game off (old probe: truck on the D8)   -job <stopId>  take that job at start
//   -jobauto  the autopilot drives the GPS route and unloads   -jobnear M  start M metres before the target on the route
//   (-jobnear 0: backed into the target bay; with -jobauto the unload starts at once)
//   -jobfresh  wallet from zero, nothing saved (implied by -job)   -savefile path  save / load this file instead (tests)
//   -jobabort s  break the first unload off after s seconds   -camorbit yaw,pitch  chase camera orbit at start
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
        public class Job { public string from, to, label, cargo; public float km, pay, deadlineMin, startMin, nightMin, cargoDamage, truckDamage; }

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
        bool inBay, atWarehouse, save = true, auto, bayReversed, unloadReversed;
        float nearStart = -1, abortAt = -1;
        string autoJob;
        Vector3 centreLocal; float truckLen = 8.4f;
        GameObject pallets, marker;
        GUIStyle big, small, bold, bold2;

        static string Arg(string k) { var a = System.Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == k) return a[i + 1]; return null; }
        static bool Has(string k) => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), k) >= 0;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Has("-nojob") || I) return;
            var w = Resources.Load<GameWorld>(GameWorld.ResourcePath);
            if (!w) { Debug.LogWarning("[job] no Resources/MagistralaGame — run Magistrala/Igra: podaci"); return; }
            var go = new GameObject("MagistralaGame"); I = go.AddComponent<MagistralaGame>(); I.World = w; go.AddComponent<GpsMap>();
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
            // the truck's box centre in its own frame (delivery-bay check) and length
            var b = new Bounds(); bool any = false;
            foreach (var r in Truck.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (any) { centreLocal = Truck.transform.InverseTransformPoint(b.center); centreLocal.y = 0; truckLen = Mathf.Clamp(Vector3.Dot(b.size, Abs(Truck.transform.forward)), 6, 12); }
            foreach (var t in Truck.GetComponentsInChildren<Transform>(true)) if (t.name == "load_pallets") pallets = t.gameObject;
            Unload = FirmUnload.Attach(Truck.transform);
            if (Unload && !Unload.Ok) Unload = null;
            SetPallets(false);
            Truck.Hit += OnTruckHit;
            // start of the day: in the warehouse bay in Lukšić, backed in, facing out (unless a measuring run drives the D8)
            if (!Has("-autopilot") && Arg("-tp") == null && !Has("-bench")) { PlaceAtWarehouse(); RestoreFromSave(); }
            StartDay();
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
            int k = System.Array.FindIndex(FirmJobs, j => j.to == autoJob);
            if (k < 0) { Debug.LogWarning($"[job] -job {autoJob}: not a firm job ({string.Join(", ", System.Array.ConvertAll(FirmJobs, j => j.to))})"); yield break; }
            Accept(k, true);
            yield return new WaitForSeconds(0.5f);
            UpdateRoute();
            if (nearStart == 0) { PlaceAtStop(World.Stop(autoJob)); yield return new WaitForSeconds(1.5f); } // backed into the bay: unload test
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
            ap.path = o; ap.cruiseKmh = 45; ap.drive = true;
            // (Autopilot keeps its segment index from the D8 path it started on: back to the start of the new path)
            typeof(Autopilot).GetField("seg", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(ap, 0);
            Debug.Log($"[job] autopilot on the GPS route: {o.Length} points");
        }

        // ------------------------------------------------------------------ jobs
        public Job Offer(int k)
        {
            var (to, label, cargo) = FirmJobs[k];
            if (World.Stop(to) == null) return null;
            float m = World.RoadMetres(Warehouse, to);
            float km = m > 0 ? m / 1000f : 0;
            return new Job { from = Warehouse, to = to, label = label, cargo = cargo, km = km, pay = FirmPay(km) };
        }
        public static float FirmPay(float km) => Mathf.Round((10 + 3 * Mathf.Max(0, km)) * 2) / 2; // driverPay.ts firmBasePay

        void Accept(int k, bool force = false)
        {
            var j = Offer(k); if (j == null) return;
            if (!force && Current != null) return;
            if (!force && !atWarehouse) { Banner("Ture se preuzimaju u skladištu u Lukšiću."); return; }
            if (!force && Truck.SpeedKmh > 3) { Banner("Stani u skladištu pa biraj turu."); return; }
            if (!force && !ToursOpen) { Banner(FirmHours ? "Zdravko još nije dao ture." : "Firma radi radnim danom 6–17 h."); return; }
            j.startMin = S.clockMin; j.deadlineMin = Deadline(j.km);
            Current = j; State = Phase.Loading; phaseT = force ? 0.1f : 3f;
            Truck.SetCargo(2400); SetPallets(true); // four euro pallets of drinks ≈ 2.4 t
            Debug.Log($"[job] accepted {j.from} → {j.to}: {j.km:0.00} km, {j.pay:0.0} €, deadline {j.deadlineMin:0} min ({Hm(j.startMin + j.deadlineMin)})");
            SaveNow("job accepted");
        }

        void Cancel()
        {
            if (Current == null) return;
            Debug.Log($"[job] cancelled {Current.to}");
            if (State == Phase.Unloading) StopUnload(false);
            Current = null; State = Phase.Free; Truck.SetCargo(0); SetPallets(false); Banner("Tura otkazana");
            SaveNow("job cancelled");
        }

        void StartUnload()
        {
            State = Phase.Unloading; unloadReversed = bayReversed;
            Truck.parkLock = true; // parking brake on while the tail lift works (web: airBrake)
            if (Unload) { Unload.Begin(); phaseT = Unload.Duration; } else phaseT = 4f;
            Banner("Ručna povučena. Rampa dolje, transpaleta van…");
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
            var p = ComputePayout(j, S.clockMin - j.startMin, unloadReversed);
            S.cash += p.total; S.delivered++; S.deliveredJobs.Add($"{Day}|{Hm(S.clockMin)}|{j.to}|{p.total:0.0}");
            card = p; cardT = 10;
            Debug.Log($"[job] delivered {j.to}: base {j.pay:0.0} € {string.Join(" ", p.lines.ConvertAll(l => $"[{l.label} {l.amount:+0.0;-0.0}]"))} = {p.total:0.0} €, rating {p.rating}, {p.minutes:0} game min (deadline {j.deadlineMin:0}), cash {Cash:0.0} €, delivered {Delivered}");
            Current = null; State = Phase.Free; Truck.SetCargo(0);
            if (pallets) pallets.SetActive(false); // (the rows stay at the stop: FirmUnload dropped them)
            SaveNow("delivered");
            if (auto) { var ap = Truck.GetComponent<Autopilot>(); if (ap) ap.drive = false; Truck.externalInput = false; }
        }

        /// Zdravko's line for the cargo (regionContent.ts SPLIT_KASTELA_CARGO namirnice)
        static string CargoLine(Job j) => j.to.StartsWith("ducan") ? "Namirnice za dućan. Rampa dolje, transpaleta van, potpis i dalje." : "Piće za pumpu. Rampa dolje, transpaleta van, potpis i dalje.";

        void SetPallets(bool on) { if (pallets) pallets.SetActive(on); if (Unload) Unload.SetLoaded(on); }
        void Banner(string s) { banner = s; bannerT = 6; }
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
            // the phone takes the keys while it is open (the game is paused) or ringing (Y / N)
            if (Phone.Open) { kb = null; help.Add("Tab zatvori mobitel"); }
            else if (Phone.Ringing != null) help.Add($"Y javi se ({MatePhone.Who(Phone.Ringing.caller).name}) · N odbij");
            else help.Add(Phone.Unread > 0 ? $"Tab mobitel ({Phone.Unread} nova poruka)" : "Tab mobitel");
            bool ringing = Phone.Ringing != null;
            if (card != null)
            {
                cardT -= dt;
                if (cardT <= 0 || (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))) card = null;
                else help.Add("Enter zatvori");
            }
            switch (State)
            {
                case Phase.Free:
                    if (atWarehouse)
                    {
                        if (ToursOpen)
                        {
                            prompt = Truck.SpeedKmh > 3 ? "Stani u skladištu za ture" : "Ture: 1 / 2 / 3 (ili mobitel → Poslovi)";
                            help.Add("1 / 2 / 3 uzmi turu");
                            if (kb != null) { if (kb.digit1Key.wasPressedThisFrame) Accept(0); if (kb.digit2Key.wasPressedThisFrame) Accept(1); if (kb.digit3Key.wasPressedThisFrame) Accept(2); }
                        }
                        else if (!FirmHours || S.closedDay == Day)
                        {
                            prompt = S.closedDay == Day && FirmHours ? "Danas nema firmine ture · N – kraj dana" : Hour >= 17 || Day % 7 >= 5 ? "Smjena gotova · N – kraj dana" : null;
                            if (prompt != null) { help.Add("N kraj dana"); if (kb != null && !ringing && kb.nKey.wasPressedThisFrame && Truck.SpeedKmh < 3) EndDay(); }
                        }
                    }
                    break;
                case Phase.Loading:
                    prompt = "Utovar paleta…"; phaseT -= dt;
                    if (phaseT <= 0) { State = Phase.Driving; routeT = 0; Banner($"Zdravko: „{CargoLine(Current)} Javi kad istovariš.“"); }
                    break;
                case Phase.Driving:
                {
                    var s = World.Stop(Current.to);
                    inBay = InBay(s, out bayReversed);
                    float d = Vector3.Distance(Flat(TruckCentre()), Flat(s.bay));
                    if (inBay && Truck.SpeedKmh < 1.5f)
                    {
                        prompt = bayReversed ? "E – Istovari dizalicom (unazad +10 %)" : "E – Istovari dizalicom";
                        help.Add("E istovar");
                        if ((kb != null && kb.eKey.wasPressedThisFrame) || auto) StartUnload();
                    }
                    else if (inBay) prompt = "Stani za istovar";
                    else if (d < 35) prompt = "Parkiraj u označeno mjesto za istovar";
                    help.Add("J otkaži turu");
                    if (kb != null && kb.jKey.wasPressedThisFrame) Cancel();
                    break;
                }
                case Phase.Unloading:
                {
                    phaseT -= dt;
                    int off = Unload ? Unload.RowsOff : 0, rows = Unload ? Unload.Rows : 0;
                    prompt = rows > 0 ? $"Istovar dizalicom… paleta {Mathf.Min(rows, off + 1)} / {rows} · {Mathf.CeilToInt(Mathf.Max(0, phaseT))} s" : $"Istovar dizalicom… {Mathf.CeilToInt(phaseT)} s";
                    help.Add("W / S prekini istovar · J otkaži turu");
                    bool drive = kb != null && (kb.wKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame || kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame);
                    // test: -jobabort s  the first unload is broken off after s seconds (as if the player drove off), the autopilot restarts it
                    if (abortAt > 0 && Unload && Unload.T >= abortAt) { abortAt = -1; drive = true; }
                    if (drive || Truck.SpeedKmh > 3)
                    {
                        StopUnload(false); State = Phase.Driving;
                        Banner("Istovar prekinut – stani u mjesto pa ponovno E.");
                        Debug.Log("[job] unload aborted");
                    }
                    else if (kb != null && kb.jKey.wasPressedThisFrame) Cancel();
                    else if (phaseT <= 0 && (!Unload || !Unload.Running)) Deliver();
                    break;
                }
            }
            HelpKeys = help.Count > 0 ? string.Join(" · ", help) : null;
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

        void UpdateRoute()
        {
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

        // ------------------------------------------------------------------ HUD: job card + wallet (top left), prompt (centre)
        void OnGUI()
        {
            if (!Truck) return;
            if (big == null)
            {
                big = new GUIStyle(GUI.skin.label) { fontSize = 19, normal = { textColor = Color.white }, wordWrap = true };
                small = new GUIStyle(big) { fontSize = 15 };
                bold = new GUIStyle(big) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 0.85f, 0.3f) } };
            }
            float x = 12, y = 12, w = 470;
            var lines = new List<(string, GUIStyle)>();
            lines.Add((DayLabel + (IsNight(S.clockMin) ? " · noć" : ""), bold2 ??= new GUIStyle(big) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.85f, 0.3f) } }));
            lines.Add(($"Gotovina {Eur(Cash)}   ·   Račun obrta {Eur(Account)}   ·   ugled {S.rep:+0;-0;0}", small));
            if (Current != null)
            {
                lines.Add(($"Tura: {World.Stop(Current.from)?.name} → {Current.label}", big));
                lines.Add(($"{Current.cargo} · {Current.km:0.0} km · plaća {Eur(Current.pay)} · rok {Hm(Current.startMin + Current.deadlineMin)}", small));
                if (Current.cargoDamage > 0.5f) lines.Add(($"Šteta na teretu {Current.cargoDamage:0} %", small));
                if (State == Phase.Driving) lines.Add(($"Do cilja {Dist(RouteMetres)}", big));
            }
            else if (atWarehouse && ToursOpen)
            {
                lines.Add(("Zdravkove ture iz skladišta:", big));
                for (int k = 0; k < FirmJobs.Length; k++) { var j = Offer(k); if (j != null) lines.Add(($"{k + 1}  {j.label} – {j.cargo} · {j.km:0.0} km · {Eur(j.pay)}", small)); }
            }
            else if (atWarehouse)
                lines.Add((S.closedDay == Day && FirmHours ? "Danas nema firmine ture." : !FirmHours ? (Hour < 6 && Day % 7 < 5 ? "Skladište otvara u 6:00. Zdravko zove." : "Smjena je gotova. Firma radi radnim danom 6–17 h.") : "Čeka se Zdravkov poziv.", big));
            else lines.Add(($"Vrati se u skladište u Lukšiću · {Dist(RouteMetres)}", big));
            float h = 10; foreach (var (t, st) in lines) h += st.CalcHeight(new GUIContent(t), w - 20) + 2;
            GUI.Box(new Rect(x, y, w, h), GUIContent.none); GUI.Box(new Rect(x, y, w, h), GUIContent.none);
            float cy = y + 5;
            foreach (var (t, st) in lines) { float lh = st.CalcHeight(new GUIContent(t), w - 20); GUI.Label(new Rect(x + 10, cy, w - 20, lh), t, st); cy += lh + 2; }
            if (prompt != null) Shadowed(new Rect(0, Screen.height * 0.62f, Screen.width, 40), prompt, bold);
            if (bannerT > 0 && banner != null) Shadowed(new Rect(0, Screen.height * 0.18f, Screen.width, 40), banner, bold);
            DrawCard();
        }

        static string Dist(float m) => m >= 1000 ? (m / 1000f).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("hr-HR")) + " km" : $"{Mathf.Round(m / 10) * 10:0} m";
        static void Shadowed(Rect r, string t, GUIStyle st)
        {
            var c = st.normal.textColor; st.normal.textColor = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), t, st); st.normal.textColor = c; GUI.Label(r, t, st);
        }
    }
}
