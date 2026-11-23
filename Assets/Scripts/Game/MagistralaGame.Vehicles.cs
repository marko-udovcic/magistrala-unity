// Two vehicles (story phase 3): the firm's Atego (the scene's "Truck", firm tours) and the family's Galeb (GALEB TRANSPORT,
// faturete). Galeb is built at runtime from Resources/Vehicles/Galeb/galeb.glb — a brand-free copy of "Mercedes Benz Sprinter
// 2006" by Max (CC BY 4.0; badges painted out, new paint, wheels split per hub: Tools/vehicles/galeb/make_galeb.py) — with a side
// band "GALEB TRANSPORT" (galeb_livery.png + galeb_livery.mat, Inter OFL) and its own TruckController parameter set (GalebSpec:
// 2.3 t empty, up to 4 t payload per Marko, lighter and quicker than the Atego, softer springs: more roll, same forgiving grip).
//   parked   the vehicle the player is not driving is "parked": its model is lifted out under a static GameObject with box colliders
//            and the vehicle root (rigidbody, wheels, scripts) is deactivated — every system that looks for "the" TruckController
//            (camera, HUD, traffic, lights, weather) finds only the driven one.
//   switch   stopped within SwitchReach m of the parked vehicle, no cargo on board: "E – uzmi Galeba / Atego" (a short fade). Firm
//            tours need the Atego (taken at the warehouse), faturete the Galeb (GPS leads to it first).
//   homes    a new game: Atego in the Lukšić warehouse bay, Galeb in the Bašić yard (dvor_basica); later both stay where parked.
//   save     SaveData.vehicle, the driven vehicle's exact pose (pos*) and the parked one's (park*), the Galeb's own diesel (fuelG).
// Test switches: -vozilo galeb  start in the Galeb (at the yard, unless the save puts it elsewhere)   -zamjena  after 4 s the
//   driven vehicle is put 9 m beside the parked one and they are swapped (as E does)
using System.Collections;
using UnityEngine;

namespace Magistrala.Game
{
    public partial class MagistralaGame
    {
        public const string Yard = "dvor_basica";
        public const float SwitchReach = 22f;
        public class Vehicle
        {
            public string id, label, acc; // "atego" / "galeb", "Atego" / "Galeb", the accusative ("Atego" / "Galeba")
            public TruckController tc; public Transform model; public Vector3 modelPos; public Quaternion modelRot;
            public GameObject parked; public FirmUnload unload; public GameObject pallets;
            public bool IsParked => parked != null;
        }
        public Vehicle Atego { get; private set; }
        public Vehicle Galeb { get; private set; }
        public Vehicle Driven => Truck && Galeb != null && Truck == Galeb.tc ? Galeb : Atego;
        public Vehicle Other => Driven == Galeb ? Atego : Galeb;
        public bool InGaleb => Galeb != null && Truck == Galeb.tc;
        float fade; bool switching;

        /// the Galeb's physics (TruckController.Spec): a 3.5 t-class panel van, 2.2 l diesel ~330 Nm, 6 gears
        public static TruckController.Spec GalebSpec() => new TruckController.Spec
        {
            id = "galeb", mass = 2300, radius = 0.37f, finalDrive = 4.0f, torqueNm = 330, topKmh = 135, reverseKmh = 18,
            idle = 750, redline = 4200, rpmScale = 1.7f, shiftTime = 0.45f,
            travel = 0.22f, sag = 0.10f, sagLoaded = 0.135f, springTarget = 0.3f, damping = 0.5f,
            ratios = new[] { 4.6f, 2.6f, 1.6f, 1.1f, 0.84f, 0.67f }, reverseRatio = 4.2f, dragArea = 1.9f, steerRate = 1.9f,
            wheelMassFront = 25, wheelMassRear = 30, comAboveAxle = 0.45f, comFront = 0.56f, cargoAboveAxle = 0.55f, cargoRefKg = 4000,
            brakeEmpty = 6.5f, brakeLoaded = 4.8f, engineBrake = 0.25f, cabFloor = 0.45f, boxFloor = 0.4f,
            eyeModel = new Vector3(-0.42f, 0.68f, 0.58f), antiRollFront = 5200, antiRollRear = 3800,
        };

        // ------------------------------------------------------------------ setup (MagistralaGame.Start)
        void SetupVehicles()
        {
            Atego = new Vehicle { id = "atego", label = "Atego", acc = "Atego", tc = Truck };
            Atego.unload = Unload; Atego.pallets = pallets;
            var src = Resources.Load<GameObject>("Vehicles/Galeb/galeb");
            if (!src) { Debug.LogWarning("[vozila] Resources/Vehicles/Galeb/galeb.glb missing: no Galeb"); return; }
            var root = new GameObject("Galeb"); root.SetActive(false);
            root.layer = Truck.gameObject.layer;
            var m = Instantiate(src, root.transform, false); m.name = "Model";
            AddLivery(m.transform);
            root.AddComponent<Rigidbody>();
            var tc = root.AddComponent<TruckController>(); tc.spec = GalebSpec(); tc.modelYawDegrees = 0;
            var ap = root.AddComponent<Autopilot>(); ap.path = new[] { Vector3.zero, Vector3.forward * 10 };
            root.AddComponent<TruckLights>();
            Galeb = new Vehicle { id = "galeb", label = "Galeb", acc = "Galeba", tc = tc };
            tc.Hit += OnTruckHit;
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

        /// at start (after LoadSave, before RestoreFromSave): the driven vehicle as saved (or -vozilo), the other one at its saved
        /// pose, else its home bay; both settle on their wheels for a moment, then the other one is frozen as a parked model
        void ChooseVehicle(bool placed)
        {
            if (Galeb == null) return;
            bool galebDriven = (Arg("-vozilo") ?? S.vehicle) == "galeb";
            var gtc = Galeb.tc;
            gtc.gameObject.SetActive(true); // Awake: wheels and colliders from GalebSpec
            if (galebDriven)
            {
                Truck = gtc; OnDrivenChanged();
                if (!(placed && S.posOn) && World.Stop(Yard) is GStop y) PlaceAtStop(y); // (RestoreFromSave puts it at its saved pose)
                if (placed && S.parkOn && Arg("-vozilo") == null) PlacePose(Atego.tc, new Vector3(S.parkX, S.parkY, S.parkZ), S.parkYaw);
                else PlaceAtStopFor(Atego.tc, World.Stop(Warehouse));
            }
            else if (placed && S.parkOn && Arg("-vozilo") == null) PlacePose(gtc, new Vector3(S.parkX, S.parkY, S.parkZ), S.parkYaw);
            else PlaceAtStopFor(gtc, World.Stop(Yard));
            S.vehicle = galebDriven ? "galeb" : "atego";
            SetCamTarget(Truck);
            StartCoroutine(ParkOtherLater());
        }

        IEnumerator ParkOtherLater()
        {
            yield return new WaitForSeconds(1.2f);
            if (Other == null || Other.IsParked) yield break;
            Park(Other);
            Debug.Log($"[vozila] driving {Driven.label}; {Other.label} parked at x={Other.parked.transform.position.x - 4200:0} z={-Other.parked.transform.position.z - 5300:0}");
            if (Has("-zamjena"))
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
                Debug.Log($"[vozila] -zamjena: near {NearOther}, can switch {CanSwitch}");
                SwitchVehicle();
            }
        }

        /// put any vehicle's root so its (bounds) centre stands on p facing fwd (as Place does for the driven one)
        void PlaceAtStopFor(TruckController tc, GStop s)
        {
            if (s == null || !tc) return;
            var keep = Truck; Truck = tc; var keepC = centreLocal; centreLocal = CentreOf(tc);
            PlaceAtStop(s);
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
        bool CanSwitch => !switching && Galeb != null && Other != null && Other.IsParked && Truck.SpeedKmh < 2 && NearOther
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
            Banner(to == Galeb ? "Galeb. Ćaćin kamion. Pazi, koči kasnije nego Atego." : "Atego firme. Ture se uzimaju u skladištu.");
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
            bool atego = Truck == Atego.tc;
            Unload = atego ? Atego.unload : null;
            pallets = atego ? Atego.pallets : null;
        }

        static void SetCamTarget(TruckController tc)
        {
            var cam = FindFirstObjectByType<TruckChaseCamera>(); if (cam) cam.target = tc;
            var hud = FindFirstObjectByType<TruckHud>(); if (hud) hud.target = tc;
        }

        /// saved with the game: the driven vehicle's pose and the parked one's
        void SaveVehicles()
        {
            if (Galeb == null || !Truck) return;
            S.vehicle = Driven.id;
            var t = Truck.transform; S.posOn = true; S.posX = t.position.x; S.posY = t.position.y; S.posZ = t.position.z; S.posYaw = t.eulerAngles.y;
            if (Other.IsParked) { var p = Other.parked.transform; S.parkOn = true; S.parkX = p.position.x; S.parkY = p.position.y; S.parkZ = p.position.z; S.parkYaw = p.eulerAngles.y; }
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
