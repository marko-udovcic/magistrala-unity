// Three vehicles (story phase 3, Marko 24.11.: "firma ima atego i taj sprinter, galeb je u vlasništvu Mate"):
//   Atego  the firm's 7.5 t box truck (the scene's "Truck"), firm tours, tail lift
//   Kombi  the firm's big panel van (Resources/Vehicles/Kombi/kombi.glb, the brand-free long high-roof van), firm tours, pallets
//          unloaded by hand (no tail lift)
//   Galeb  Mate's own old TN-type van, GALEB TRANSPORT (Resources/Vehicles/Galeb/galeb.glb; the livery is in the GLB as
//          "galeb_decal" — older models without it get the side band galeb_livery.mat), faturete only, up to 4 t payload
// Galeb and Kombi are built at runtime with their own TruckController parameter sets (GalebSpec / KombiSpec).
//   parked   the vehicle the player is not driving is "parked": its model is lifted out under a static GameObject with box colliders
//            and the vehicle root (rigidbody, wheels, scripts) is deactivated — every system that looks for "the" TruckController
//            (camera, HUD, traffic, lights, weather) finds only the driven one.
//   switch   stopped within SwitchReach m of a parked vehicle, no cargo on board: "E – uzmi Galeba / Atego / kombi" (a short fade;
//            the nearest parked one, the Galeb first while a fatureta waits). Firm tours need the Atego or the Kombi (taken at the
//            warehouse), faturete the Galeb (GPS leads to it first).
//   homes    a new game: Atego in the Lukšić warehouse bay, Kombi on a free spot beside it, Galeb in the Bašić yard (dvor_basica);
//            later all stay where parked.
//   save     SaveData.vehicle, the driven vehicle's exact pose (pos*), every parked one's (parks; the old single park* pose is
//            still read), each vehicle's own diesel (fuel / fuelG / fuelK).
// Test switches: -vozilo galeb  start in the Galeb (at the yard, unless the save puts it elsewhere)   -zamjena  after 4 s the
//   driven vehicle is put 9 m beside the parked one and they are swapped (as E does)
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
            public string id, label, acc; // "atego" / "galeb" / "kombi", "Atego" / "Galeb" / "Kombi", the accusative ("Galeba")
            public bool firm; // firm tours may be driven with it
            public string home; public string hello; // home stop, the banner when taken
            public TruckController tc; public Transform model; public Vector3 modelPos; public Quaternion modelRot;
            public GameObject parked; public FirmUnload unload; public GameObject pallets;
            public bool IsParked => parked != null;
            public Vector3 At => parked ? parked.transform.position : tc ? tc.transform.position : Vector3.zero;
        }
        public Vehicle Atego { get; private set; }
        public Vehicle Galeb { get; private set; }
        public Vehicle Kombi { get; private set; }
        readonly List<Vehicle> fleet = new List<Vehicle>();
        public Vehicle Driven
        {
            get { for (int i = 0; i < fleet.Count; i++) if (Truck && fleet[i].tc == Truck) return fleet[i]; return Atego; }
        }
        /// the parked vehicle E takes: the Galeb while a fatureta waits to be loaded (and it is close), else the nearest parked one
        /// within SwitchReach (else the nearest parked one at all; null = none)
        public Vehicle Other
        {
            get
            {
                if (fleet.Count < 2) return null;
                Vector3 c = Truck ? Flat(TruckCentre()) : Vector3.zero;
                if (Galeb != null && Galeb.IsParked && Current != null && Current.fatureta && !Current.loaded
                    && Vector3.Distance(c, Flat(Galeb.At)) < SwitchReach) return Galeb;
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
        /// driving a firm vehicle (Atego or Kombi): firm tours allowed
        public bool InFirm => Driven.firm;
        /// where the parked Galeb stands (the GPS leads there with a fatureta waiting), null = driven / none
        public Vector3? GalebAt => Galeb != null && Galeb.IsParked ? Galeb.parked.transform.position : (Vector3?)null;
        float fade; bool switching;

        /// the Galeb's physics (TruckController.Spec): Mate's old TN-type panel van, up to 4 t payload, ~2.4 l diesel, 5 gears
        public static TruckController.Spec GalebSpec() => new TruckController.Spec
        {
            id = "galeb", model = "galeb", mass = 2300, radius = 0.405f, finalDrive = 4.0f, torqueNm = 330, topKmh = 135, reverseKmh = 18,
            idle = 750, redline = 4200, rpmScale = 1.7f, shiftTime = 0.45f,
            travel = 0.22f, sag = 0.10f, sagLoaded = 0.135f, springTarget = 0.3f, damping = 0.5f,
            ratios = new[] { 4.6f, 2.6f, 1.6f, 1.1f, 0.84f, 0.67f }, reverseRatio = 4.2f, dragArea = 1.9f, steerRate = 1.9f,
            wheelMassFront = 25, wheelMassRear = 30, comAboveAxle = 0.45f, comFront = 0.56f, cargoAboveAxle = 0.55f, cargoRefKg = 4000,
            brakeEmpty = 6.5f, brakeLoaded = 4.8f, engineBrake = 0.25f, cabFloor = 0.45f, boxFloor = 0.4f,
            eyeModel = new Vector3(-0.42f, 1.04f, 1.02f), antiRollFront = 5200, antiRollRear = 3800, // galeb-tn.glb (kabina-v3)
        };

        /// the firm's van: a long high-roof 3.5 t-class panel van, heavier and slower to stop than the Galeb, ~2.5 t payload
        public static TruckController.Spec KombiSpec() => new TruckController.Spec
        {
            id = "kombi", model = "kombi", mass = 2600, radius = 0.367f, finalDrive = 4.2f, torqueNm = 360, topKmh = 140, reverseKmh = 18,
            idle = 750, redline = 4200, rpmScale = 1.7f, shiftTime = 0.45f,
            travel = 0.22f, sag = 0.10f, sagLoaded = 0.14f, springTarget = 0.3f, damping = 0.52f,
            ratios = new[] { 4.6f, 2.6f, 1.6f, 1.1f, 0.84f, 0.67f }, reverseRatio = 4.2f, dragArea = 2.4f, steerRate = 1.8f,
            wheelMassFront = 26, wheelMassRear = 32, comAboveAxle = 0.5f, comFront = 0.52f, cargoAboveAxle = 0.6f, cargoRefKg = 2500,
            brakeEmpty = 6.3f, brakeLoaded = 4.9f, engineBrake = 0.25f, cabFloor = 0.45f, boxFloor = 0.4f,
            eyeModel = new Vector3(-0.42f, 0.72f, 0.58f), antiRollFront = 5600, antiRollRear = 4400, // kombi-firma.glb (kabina-v3)
        };

        // ------------------------------------------------------------------ setup (MagistralaGame.Start)
        void SetupVehicles()
        {
            fleet.Clear();
            // (id "atego" for the saves; since 27. 11. the firm's main truck is the L4H1 box van, PlayerTruck.cs)
            bool van = Truck.spec.model == "l4h1";
            Atego = new Vehicle { id = "atego", label = van ? "Furgon" : "Atego", acc = van ? "furgon" : "Atego", firm = true, home = Warehouse, tc = Truck,
                hello = van ? "Furgon firme, izotermni sanduk. Ture se uzimaju u skladištu." : "Atego firme. Ture se uzimaju u skladištu." };
            Atego.unload = Unload; Atego.pallets = pallets;
            fleet.Add(Atego);
            var tc = MakeVehicle("Vehicles/Galeb/galeb", "Galeb", GalebSpec());
            if (tc)
            {
                Galeb = new Vehicle { id = "galeb", label = "Galeb", acc = "Galeba", home = Yard, tc = tc,
                    hello = Truck.spec.model == "l4h1" ? "Galeb. Ćaćin kamion. Pazi, koči kasnije nego furgon." : "Galeb. Ćaćin kamion. Pazi, koči kasnije nego Atego." };
                if (!HasNode(tc.transform, "galeb_decal")) AddLivery(tc.transform.Find("Model"));
                fleet.Add(Galeb);
            }
            tc = MakeVehicle("Vehicles/Kombi/kombi", "Kombi", KombiSpec());
            if (tc)
            {
                Kombi = new Vehicle { id = "kombi", label = "Kombi", acc = "kombi", firm = true, home = Warehouse, tc = tc,
                    hello = "Kombi firme. Ture se uzimaju u skladištu, paleta se istovaruje ručno." };
                fleet.Add(Kombi);
            }
        }

        TruckController MakeVehicle(string resource, string name, TruckController.Spec spec)
        {
            var src = Resources.Load<GameObject>(resource);
            if (!src) { Debug.LogWarning($"[vozila] Resources/{resource}.glb missing: no {name}"); return null; }
            var root = new GameObject(name); root.SetActive(false);
            root.layer = Truck.gameObject.layer;
            var m = Instantiate(src, root.transform, false); m.name = "Model";
            root.AddComponent<Rigidbody>();
            var tc = root.AddComponent<TruckController>(); tc.spec = spec; tc.modelYawDegrees = 0;
            var ap = root.AddComponent<Autopilot>(); ap.path = new[] { Vector3.zero, Vector3.forward * 10 };
            root.AddComponent<TruckLights>();
            tc.Hit += OnTruckHit;
            return tc;
        }

        static bool HasNode(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return true;
            return false;
        }

        /// the GALEB TRANSPORT band on both sides of the cargo area (model frame of galeb.glb: +Z forward, sides at |x| 0.948 between
        /// y −0.33 and −0.03, measured from the body mesh; the rear wheel arch tops out at y −0.4, the sliding-door rail runs at y 0.07)
        static void AddLivery(Transform model)
        {
            var mat = Resources.Load<Material>("Vehicles/Galeb/galeb_livery");
            if (!mat) { Debug.LogWarning("[vozila] galeb_livery material missing"); return; }
            const float h = 0.33f, w = h * 2048f / 384f, x = 0.951f, y = -0.19f, z = -0.58f;
            foreach (float s in new[] { -1f, 1f })
            {
                var q = new GameObject(s < 0 ? "livery_L" : "livery_R");
                q.transform.SetParent(model, false);
                q.transform.localPosition = new Vector3(s * x, y, z);
                // the quad faces outwards; the text reads left to right seen from that side
                q.transform.localRotation = Quaternion.LookRotation(new Vector3(-s, 0, 0), Vector3.up);
                var mesh = new Mesh { name = "livery" };
                mesh.vertices = new[] { new Vector3(-w / 2, -h / 2, 0), new Vector3(w / 2, -h / 2, 0), new Vector3(-w / 2, h / 2, 0), new Vector3(w / 2, h / 2, 0) };
                mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
                mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                q.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = q.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        /// at start (after LoadSave, before RestoreFromSave): the driven vehicle as saved (or -vozilo), the others at their saved
        /// poses, else their homes; all settle on their wheels for a moment, then the others are frozen as parked models
        void ChooseVehicle(bool placed)
        {
            if (fleet.Count < 2) return;
            string want = Arg("-vozilo") ?? S.vehicle;
            var drive = fleet.Find(v => v.id == want) ?? Atego;
            foreach (var v in fleet) if (v != Atego) v.tc.gameObject.SetActive(true); // Awake: wheels and colliders from the spec
            if (drive != Atego)
            {
                Truck = drive.tc; OnDrivenChanged();
                if (!(placed && S.posOn) && World.Stop(drive.home) is GStop h) PlaceAtStop(h); // (RestoreFromSave puts it at its saved pose)
            }
            bool fromSave = placed && Arg("-vozilo") == null;
            // the vehicle at its home stop first, the second one at the same stop (Kombi beside the Atego) then finds a free spot
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

        /// the saved pose of a parked vehicle: SaveData.parks, else (an older save) the single park* pose of the Atego / Galeb pair
        bool SavedPark(Vehicle v, out Vector3 pos, out float yaw)
        {
            foreach (var p in S.parks) if (p.id == v.id) { pos = new Vector3(p.x, p.y, p.z); yaw = p.yaw; return true; }
            pos = default; yaw = 0;
            if (S.parks.Count > 0 || !S.parkOn) return false;
            string legacyOther = S.vehicle == "galeb" ? "atego" : "galeb";
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
        /// beside: the bay is taken (the Kombi at the warehouse next to the Atego): the first free spot beside / behind it
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

        /// the driven vehicle changed: its box centre and length, pallets / tail lift (Atego only), the camera and the HUD
        void OnDrivenChanged()
        {
            centreLocal = CentreOf(Truck);
            var b = new Bounds(); bool any = false;
            foreach (var r in Truck.GetComponentsInChildren<Renderer>()) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            if (any) truckLen = Mathf.Clamp(Vector3.Dot(b.size, Abs(Truck.transform.forward)), 5, 12);
            bool atego = Truck == Atego.tc; // tail lift and the pallet rows: the Atego only (the Kombi: by hand)
            Unload = atego ? Atego.unload : null;
            pallets = atego ? Atego.pallets : null;
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
