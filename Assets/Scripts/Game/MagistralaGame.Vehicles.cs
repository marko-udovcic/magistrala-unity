// Furgon, Sprinter firme, mali kombi i očev Galeb L3H2. Park/save ids migrate once at startup.
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.Game
{
    public partial class MagistralaGame
    {
        public const string Yard = "dvor_basica";
        public const float SwitchReach = 22f;
        public class Vehicle
        {
            public string id, label, acc; // stable id, display label, accusative
            public bool firm; // firm tours may be driven with it
            public string home; public string hello; // home stop, the banner when taken
            public TruckController tc; public Transform model; public Vector3 modelPos; public Quaternion modelRot;
            public GameObject parked; public FirmUnload unload; public GameObject pallets;
            public bool IsParked => parked != null;
            public Vector3 At => parked ? parked.transform.position : tc ? tc.transform.position : Vector3.zero;
        }
        public Vehicle Furgon { get; private set; }
        public Vehicle Galeb { get; private set; }
        public Vehicle Kombi { get; private set; }
        public Vehicle Mali { get; private set; }
        readonly List<Vehicle> fleet = new List<Vehicle>();
        public Vehicle Driven
        {
            get { for (int i = 0; i < fleet.Count; i++) if (Truck && fleet[i].tc == Truck) return fleet[i]; return Furgon; }
        }
        /// E always selects the nearest parked vehicle
        /// within SwitchReach (else the nearest parked one at all; null = none)
        public Vehicle Other
        {
            get
            {
                if (fleet.Count < 2) return null;
                Vector3 c = Truck ? Flat(TruckCentre()) : Vector3.zero;
                Vehicle best = null; float bd = float.PositiveInfinity;
                var d = Driven;
                foreach (var v in fleet)
                {
                    if (v == d || !v.IsParked) continue;
                    float k = Vector3.Distance(c, Flat(v.At));
                    if (k < bd) { bd = k; best = v; }
                }
                return best;
            }
        }
        public bool InGaleb => Galeb != null && Truck == Galeb.tc;
        /// Firm vehicles: Furgon, Sprinter and small van.
        public bool InFirm => Driven.firm;
        /// where the parked Galeb stands (the GPS leads there with a fatureta waiting), null = driven / none
        public Vector3? GalebAt => Galeb != null && Galeb.IsParked ? Galeb.parked.transform.position : (Vector3?)null;
        float fade; bool switching;

        public static TruckController.Spec GalebSpec() => PlayerTruck.FleetSpec("galebl3h2");
        public static TruckController.Spec KombiSpec() => PlayerTruck.FleetSpec("sprinter");
        static string VehicleId(string id) => id == "atego" ? "furgon" : id == "kombi" ? "sprinter" : id;

        void SetupVehicles()
        {
            fleet.Clear();
            Furgon = new Vehicle { id = "furgon", label = "Furgon", acc = "furgon", firm = true, home = Warehouse, tc = Truck,
                hello = "Furgon firme. Nosivost 1100 kg. Ture se uzimaju u skladištu.", unload = Unload, pallets = pallets };
            fleet.Add(Furgon);
            var tc = MakeVehicle(PlayerTruck.GalebResource, "Galeb", GalebSpec());
            if (tc)
            {
                Galeb = new Vehicle { id = "galeb", label = "Galeb", acc = "Galeba", home = Yard, tc = tc,
                    hello = "Galeb. Ćaćin stari kombi. Do 1400 kg, pomalo uzbrdo." };
                fleet.Add(Galeb);
            }
            tc = MakeVehicle(PlayerTruck.SprinterResource, "Dugi kombi firme", KombiSpec());
            if (tc)
            {
                Kombi = new Vehicle { id = "sprinter", label = "Dugi kombi firme", acc = "dugi kombi firme", firm = true, home = Warehouse, tc = tc,
                    hello = "Dugi kombi firme. Nosivost 1300 kg, istovar ručno." };
                fleet.Add(Kombi);
            }
            tc = MakeVehicle(PlayerTruck.SmallResource, "Mali kombi", PlayerTruck.FleetSpec("mali"));
            if (tc)
            {
                Mali = new Vehicle { id = "mali", label = "Mali kombi", acc = "mali kombi", firm = true, home = Warehouse, tc = tc,
                    hello = "Mali kombi firme. Nosivost 700 kg, za manje dostave." };
                fleet.Add(Mali);
            }
        }

        TruckController MakeVehicle(string resource, string name, TruckController.Spec spec)
        {
            var src = Resources.Load<GameObject>(resource);
            if (!src) { Debug.LogWarning($"[vozila] Resources/{resource}.glb missing: no {name}"); return null; }
            var root = new GameObject(name); root.SetActive(false);
            root.layer = Truck.gameObject.layer;
            var m = Instantiate(src, root.transform, false); m.name = "Model";
            PlayerTruck.AddLods(m.transform, resource);
            root.AddComponent<Rigidbody>();
            var tc = root.AddComponent<TruckController>(); tc.spec = spec; tc.modelYawDegrees = 0;
            var ap = root.AddComponent<Autopilot>(); ap.path = new[] { Vector3.zero, Vector3.forward * 10 };
            root.AddComponent<TruckLights>();
            tc.Hit += OnTruckHit;
            return tc;
        }

        /// at start (after LoadSave, before RestoreFromSave): the driven vehicle as saved (or -vozilo), the others at their saved
        /// poses, else their homes; all settle on their wheels for a moment, then the others are frozen as parked models
        void ChooseVehicle(bool placed)
        {
            if (fleet.Count < 2) return;
            string want = VehicleId(Arg("-vozilo") ?? S.vehicle);
            var drive = fleet.Find(v => v.id == want) ?? Furgon;
            foreach (var v in fleet) if (v != Furgon) v.tc.gameObject.SetActive(true); // Awake: wheels and colliders from the spec
            if (drive != Furgon)
            {
                Truck = drive.tc; OnDrivenChanged();
                if (!(placed && S.posOn) && World.Stop(drive.home) is GStop h) PlaceAtStop(h); // (RestoreFromSave puts it at its saved pose)
            }
            bool fromSave = placed && Arg("-vozilo") == null;
            // the vehicle at its home stop first, the second one at the same stop (Kombi beside the Furgon) then finds a free spot
            var homeTaken = new HashSet<string> { drive.home };
            foreach (var v in fleet)
            {
                if (v == drive) continue;
                if (fromSave && SavedPark(v, out var pos, out float yaw)) { PlacePose(v.tc, pos, yaw); continue; }
                PlaceAtStopFor(v.tc, World.Stop(v.home), homeTaken.Contains(v.home));
                homeTaken.Add(v.home);
            }
            S.vehicle = drive.id;
            SetCamTarget(Truck);
            StartCoroutine(ParkOtherLater());
        }

        /// the saved pose of a parked vehicle: SaveData.parks, else (an older save) the single park* pose of the Furgon / Galeb pair
        bool SavedPark(Vehicle v, out Vector3 pos, out float yaw)
        {
            foreach (var p in S.parks) if (VehicleId(p.id) == v.id) { pos = new Vector3(p.x, p.y, p.z); yaw = p.yaw; return true; }
            pos = default; yaw = 0;
            if (S.parks.Count > 0 || !S.parkOn) return false;
            string legacyOther = VehicleId(S.vehicle) == "galeb" ? "furgon" : "galeb";
            if (v.id != legacyOther) return false;
            pos = new Vector3(S.parkX, S.parkY, S.parkZ); yaw = S.parkYaw; return true;
        }

        IEnumerator ParkOtherLater()
        {
            yield return new WaitForSeconds(1.2f);
            var d = Driven;
            foreach (var v in fleet)
            {
                if (v == d || v.IsParked) continue;
                Park(v);
                Debug.Log($"[vozila] {v.label} parked at x={v.parked.transform.position.x - 4200:0} z={-v.parked.transform.position.z - 5300:0}");
            }
            Debug.Log($"[vozila] driving {d.label}; fleet {fleet.Count}");
            if (Has("-zamjena") && Other != null)
            {
                yield return new WaitForSeconds(3f);
                // a free spot 8–12 m around the parked one (no collider in a truck-sized box above the ground)
                var o = Other.parked.transform; Vector3 spot = o.position + o.forward * 10f; Vector3 dir = o.forward;
                foreach (var c in new[] { o.forward * 10, o.right * 8, -o.right * 8, o.forward * 12 + o.right * 4, -o.forward * 10, o.right * 12, -o.right * 12 })
                {
                    var p = o.position + c; p.y = GroundY(p);
                    if (!Physics.CheckBox(p + Vector3.up * 1.9f, new Vector3(1.4f, 1.1f, 4.5f), Quaternion.LookRotation(o.forward), ~0, QueryTriggerInteraction.Ignore)) { spot = p; break; }
                }
                Place(spot, dir);
                yield return new WaitForSeconds(2f);
                Debug.Log($"[vozila] -zamjena: near {NearOther}, can switch {CanSwitch}, takes {Other?.label}");
                SwitchVehicle();
            }
        }

        /// put any vehicle's root so its (bounds) centre stands on p facing fwd (as Place does for the driven one)
        /// beside: the bay is taken (the Kombi at the warehouse next to the Furgon): the first free spot beside / behind it
        void PlaceAtStopFor(TruckController tc, GStop s, bool beside = false)
        {
            if (s == null || !tc) return;
            var keep = Truck; Truck = tc; var keepC = centreLocal; centreLocal = CentreOf(tc);
            if (!beside) PlaceAtStop(s);
            else
            {
                Physics.SyncTransforms();
                Vector3 fwd = -new Vector3(s.bayDir.x, 0, s.bayDir.y).normalized, right = Vector3.Cross(Vector3.up, fwd);
                Vector3 bay = s.bay - fwd * 0.5f, spot = bay + right * 4.5f;
                foreach (var c in new[] { right * 4.5f, -right * 4.5f, right * 9f, -right * 9f, fwd * 11f, -fwd * 11f, right * 4.5f + fwd * 11f, -right * 4.5f + fwd * 11f })
                {
                    var p = bay + c; p.y = GroundY(p);
                    if (!Physics.CheckBox(p + Vector3.up * 1.9f, new Vector3(1.3f, 1.1f, 3.6f), Quaternion.LookRotation(fwd), ~0, QueryTriggerInteraction.Ignore)) { spot = p; break; }
                }
                Place(spot, fwd);
            }
            Truck = keep; centreLocal = keepC;
        }

        void PlacePose(TruckController tc, Vector3 pos, float yaw)
        {
            var rb = tc.GetComponent<Rigidbody>(); var rot = Quaternion.Euler(0, yaw, 0);
            pos.y += 0.15f;
            tc.transform.SetPositionAndRotation(pos, rot);
            if (rb) { rb.position = pos; rb.rotation = rot; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }

        static Vector3 CentreOf(TruckController tc)
        {
            var b = new Bounds(); bool any = false;
            foreach (var r in tc.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            var c = any ? tc.transform.InverseTransformPoint(b.center) : Vector3.zero; c.y = 0; return c;
        }

        // ------------------------------------------------------------------ park / unpark
        void Park(Vehicle v)
        {
            if (v == null || v.IsParked || !v.tc) return;
            var root = v.tc.transform;
            v.model = v.tc.model ? v.tc.model : root.Find("Model");
            if (!v.model) return;
            v.modelPos = v.model.localPosition; v.modelRot = v.model.localRotation;
            var p = new GameObject("Parkiran_" + v.id); p.layer = root.gameObject.layer;
            p.transform.SetPositionAndRotation(root.position, root.rotation);
            // the root's body colliders, copied as static boxes
            foreach (var bc in root.GetComponentsInChildren<BoxCollider>())
            {
                if (bc.isTrigger || !bc.enabled) continue;
                var c = new GameObject(bc.name).AddComponent<BoxCollider>(); c.gameObject.layer = p.layer;
                c.transform.SetParent(p.transform, false);
                c.transform.localPosition = root.InverseTransformPoint(bc.transform.position); c.transform.localRotation = Quaternion.Inverse(root.rotation) * bc.transform.rotation;
                c.center = bc.center; c.size = bc.size;
            }
            v.model.SetParent(p.transform, true);
            v.parked = p;
            root.gameObject.SetActive(false);
        }

        void Unpark(Vehicle v)
        {
            if (v == null || !v.IsParked) return;
            var root = v.tc.transform; var p = v.parked.transform;
            root.SetPositionAndRotation(p.position + Vector3.up * 0.05f, p.rotation);
            v.model.SetParent(root, false); v.model.localPosition = v.modelPos; v.model.localRotation = v.modelRot;
            Destroy(v.parked); v.parked = null;
            root.gameObject.SetActive(true);
            var rb = v.tc.GetComponent<Rigidbody>(); if (rb) { rb.position = root.position; rb.rotation = root.rotation; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
        }

        /// where the parked vehicle stands (Unity), null = no other vehicle
        public Vector3? OtherAt => Other != null && Other.IsParked ? Other.parked.transform.position : (Vector3?)null;
        public bool NearOther => OtherAt is Vector3 o && Vector3.Distance(Flat(TruckCentre()), Flat(o)) < SwitchReach;
        /// may the player change vehicles now: stopped, nothing loaded (a fatureta not yet loaded is fine), no tail lift running
        bool CanSwitch => !switching && fleet.Count > 1 && Other != null && Other.IsParked && Truck.SpeedKmh < 2 && NearOther
                          && (Current == null || (Current.fatureta && !Current.loaded && State == Phase.Driving));

        void SwitchVehicle()
        {
            if (!CanSwitch) return;
            StartCoroutine(SwitchRoutine());
        }

        IEnumerator SwitchRoutine()
        {
            switching = true;
            for (float t = 0; t < 0.35f; t += Time.unscaledDeltaTime) { fade = t / 0.35f; yield return null; }
            fade = 1;
            var from = Driven; var to = Other;
            Truck.parkLock = false;
            Park(from);
            Unpark(to);
            Truck = to.tc; OnDrivenChanged();
            SetCamTarget(Truck);
            S.vehicle = to.id;
            Banner(to.hello);
            Debug.Log($"[vozila] switched to {to.label}");
            yield return null; yield return null;
            routeT = 0;
            SaveNow("vehicle switch");
            for (float t = 0; t < 0.35f; t += Time.unscaledDeltaTime) { fade = 1 - t / 0.35f; yield return null; }
            fade = 0; switching = false;
        }

        /// The driven vehicle changed: dimensions, offers, Furgon pallets, camera and HUD.
        void OnDrivenChanged()
        {
            offers = null; // capacities and pallet rows change with the driven van
            centreLocal = CentreOf(Truck);
            var b = new Bounds(); bool any = false;
            foreach (var r in Truck.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (any) truckLen = Mathf.Clamp(Vector3.Dot(b.size, Abs(Truck.transform.forward)), 5, 12);
            bool main = Truck == Furgon.tc; // Furgon pallet rows; panel vans unload by hand.
            Unload = main ? Furgon.unload : null;
            pallets = main ? Furgon.pallets : null;
        }

        static void SetCamTarget(TruckController tc)
        {
            var cam = FindFirstObjectByType<TruckChaseCamera>(); if (cam) cam.target = tc;
            var hud = FindFirstObjectByType<TruckHud>(); if (hud) hud.target = tc;
        }

        /// saved with the game: the driven vehicle's pose and every parked one's
        void SaveVehicles()
        {
            if (fleet.Count < 2 || !Truck) return;
            var d = Driven;
            S.vehicle = d.id;
            var t = Truck.transform; S.posOn = true; S.posX = t.position.x; S.posY = t.position.y; S.posZ = t.position.z; S.posYaw = t.eulerAngles.y;
            bool all = true; foreach (var v in fleet) if (v != d && !v.IsParked) all = false;
            if (!all) return; // (the first 1.2 s: not parked yet, keep the saved poses)
            S.parks.Clear();
            foreach (var v in fleet)
            {
                if (v == d) continue;
                var p = v.parked.transform;
                S.parks.Add(new ParkPose { id = v.id, x = p.position.x, y = p.position.y, z = p.position.z, yaw = p.eulerAngles.y });
            }
            S.parkOn = false; // superseded by parks
        }

        void DrawFade()
        {
            if (fade <= 0.001f) return;
            var old = GUI.color; GUI.color = new Color(0, 0, 0, fade);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }
}
