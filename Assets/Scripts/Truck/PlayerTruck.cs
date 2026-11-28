// Four player vans share the L4H1 node contract. Legacy ids are migrated by the fleet.
using System.Collections.Generic;
using UnityEngine;

public static class PlayerTruck
{
    public const string Resource = "Vehicles/KombiL4H1/kombi-l4h1";

    public const string SprinterResource = "Vehicles/SprinterFirma/sprinter";
    public const string SmallResource = "Vehicles/MaliKombi/mali-kombi";
    public const string GalebResource = "Vehicles/GalebL3H2/galeb";
    public static bool Modern(string model) => model == "l4h1" || model == "sprinter" || model == "mali" || model == "galebl3h2";

    // Shared diesel response and node rig; dimensions and load limits belong to each model.
    public static TruckController.Spec FleetSpec(string model)
    {
        var s = VanSpec(); s.model = model; s.boxes = null;
        s.cargoAboveAxle = .55f; s.comAboveAxle = .48f;
        if (model == "sprinter")
        {
            s.id = "sprinter"; s.mass = 2100; s.radius = .365f; s.payloadKg = 1300;
            s.torqueNm = 330; s.dragArea = 2.5f; s.comFront = .52f;
            s.boxes = Shell(1.0f, .40f, 2.70f, -3.45f, 1.0f, 3.47f, 1.45f);
        }
        else if (model == "mali")
        {
            s.id = "mali"; s.mass = 1400; s.radius = .329f; s.payloadKg = 700;
            s.torqueNm = 250; s.finalDrive = 3.65f; s.topKmh = 150; s.dragArea = 1.65f;
            s.ratios = new[] { 3.77f, 2.05f, 1.32f, .98f, .76f, .62f };
            s.comAboveAxle = .32f; s.comFront = .56f; s.cargoAboveAxle = .4f;
            s.antiRollFront = 4300; s.antiRollRear = 3300;
            s.boxes = Shell(.92f, .32f, 1.85f, -2.18f, .45f, 2.19f, 1.05f);
        }
        else if (model == "galebl3h2")
        {
            s.id = "galeb"; s.mass = 2000; s.radius = .365f; s.payloadKg = 1400;
            s.torqueNm = 285; s.topKmh = 130; s.dragArea = 2.4f; s.comFront = .53f;
            s.turboRise = .65f; s.shiftTime = .48f; s.brakeEmpty = 6.5f; s.brakeLoaded = 5.3f;
            s.antiRollFront = 5800; s.antiRollRear = 4500;
            s.boxes = Shell(1.02f, .38f, 2.50f, -3.01f, .75f, 3.02f, 1.38f);
        }
        s.cargoRefKg = s.payloadKg;
        return s;
    }

    static TruckController.BodyBox[] Shell(float halfWidth, float floor, float roof, float rear, float cab, float nose, float bonnet)
        => new[] {
            new TruckController.BodyBox { name = "TruckCargoCollider", min = new Vector3(-halfWidth, floor, rear), max = new Vector3(halfWidth, roof, cab) },
            new TruckController.BodyBox { name = "TruckCabCollider", min = new Vector3(-halfWidth, floor, cab), max = new Vector3(halfWidth, roof * .85f, nose - .75f) },
            new TruckController.BodyBox { name = "TruckNoseCollider", min = new Vector3(-halfWidth, floor, nose - .75f), max = new Vector3(halfWidth, bonnet, nose) },
        };

    /// the van's physics: 2450 kg empty, 3.5 t GVW (~1.1 t payload), 2.3 l diesel 350 Nm, 6 gears, rear-wheel drive
    public static TruckController.Spec VanSpec() => new TruckController.Spec
    {
        id = "furgon", model = "l4h1", mass = 2450, radius = 0.365f, finalDrive = 4.25f, torqueNm = 350, topKmh = 140, reverseKmh = 18,
        idle = 800, redline = 4200, rpmScale = 1f, shiftTime = 0.35f,
        travel = 0.22f, sag = 0.10f, sagLoaded = 0.14f, springTarget = 0.3f, damping = 0.5f,
        // ~110 kW / 150 PS peak; 350 Nm plateau, six-speed diesel gearing (2001 rpm at 90).
        torqueRpm = new[] { 600f, 800f, 1100f, 1500f, 2700f, 3200f, 3600f, 4000f, 4300f },
        torqueFraction = new[] { .4f, .55f, .8f, 1f, 1f, .93f, .81f, .65f, .3f },
        upLoad = 2900, upCruise = 2000, downLoad = 1400, downCruise = 1050, downLimit = 2600,
        turboRise = .35f, rotatingInertia = .00045f, pedalExponent = .35f, boostGain = 1.3f,
        ratios = new[] { 4f, 2.24f, 1.44f, 1.06f, .85f, .72f }, reverseRatio = 4.1f, dragArea = 3.1f, steerRate = 1.8f, maxSteer = 0.68f,
        wheelMassFront = 25, wheelMassRear = 30, comAboveAxle = 0.55f, comFront = 0.5f, cargoAboveAxle = 1.0f, cargoRefKg = 1100, payloadKg = 1100,
        brakeEmpty = 7.0f, brakeLoaded = 6.0f, engineBrake = 0.25f, antiRollFront = 7000, antiRollRear = 6000, eyeNode = "a_eye",
        boxes = new[]
        {
            new TruckController.BodyBox { name = "TruckCargoCollider", min = new Vector3(-1.08f, 0.80f, -3.46f), max = new Vector3(1.08f, 2.83f, 0.76f) },
            new TruckController.BodyBox { name = "TruckCabCollider", min = new Vector3(-1.03f, 0.55f, 0.76f), max = new Vector3(1.03f, 2.17f, 2.45f) },
            new TruckController.BodyBox { name = "TruckNoseCollider", min = new Vector3(-1.0f, 0.35f, 2.45f), max = new Vector3(1.0f, 1.36f, 3.21f) },
            new TruckController.BodyBox { name = "TruckChassisCollider", min = new Vector3(-0.8f, 0.30f, -3.30f), max = new Vector3(0.8f, 0.80f, 0.76f) },
        },
    };

    /// TruckController.Awake of the scene root: replace the serialized legacy visual before setup.
    public static void Prepare(TruckController tc)
    {
        if (tc.spec == null || tc.spec.id != "furgon" || tc.spec.model != "l4h1") return;
        var src = Resources.Load<GameObject>(Resource);
        if (!src)
        {
            var legacy = tc.model ? tc.model : tc.transform.Find("Model");
            if (legacy) { legacy.gameObject.SetActive(false); Object.Destroy(legacy.gameObject); }
            tc.spec = VanSpec(); tc.enabled = false;
            throw new System.InvalidOperationException("Missing required player van: " + Resource);
        }
        var old = tc.model ? tc.model : tc.transform.Find("Model");
        Transform oldLoad = null;
        if (old) foreach (var t in old.GetComponentsInChildren<Transform>(true)) if (t.name == "load_pallets") { oldLoad = t; break; }
        var m = Object.Instantiate(src, tc.transform, false); m.name = "Model"; m.layer = tc.gameObject.layer;
        AddLods(m.transform, Resource);
        int rows = AddPallets(m.transform, oldLoad);
        if (old) { old.SetParent(null, false); old.gameObject.SetActive(false); Object.Destroy(old.gameObject); }
        tc.model = m.transform; tc.modelYawDegrees = 0; tc.spec = VanSpec();
        Debug.Log($"[kamion] Furgon L4H1 ({rows} reda paleta)");
    }

    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    /// LOD1 / LOD2 of the van in a LODGroup on the model
    public static void AddLods(Transform model, string resource)
    {
        var r0 = new List<Renderer>(model.GetComponentsInChildren<Renderer>(true));
        var lists = new List<Renderer>[2];
        for (int L = 1; L <= 2; L++)
        {
            var src = Resources.Load<GameObject>(resource + "_lod" + L);
            if (!src) return;
            var inst = Object.Instantiate(src, model, false); inst.name = "lod" + L;
            var list = new List<Renderer>();
            for (int i = 0; i < 4; i++)
            {
                var wm = Find(inst.transform, $"wheel_{i}_mesh"); var spin = Find(model, $"wheel_{i}_spin");
                if (!wm || !spin) continue;
                wm.SetParent(spin, false); wm.name = $"l{L}_wheelmesh_{i}";
                list.AddRange(wm.GetComponentsInChildren<Renderer>(true));
            }
            // Cab and door rigs in every LOD follow the animated LOD0 pivot. Same authored local frame.
            foreach (string name in new[] { "steering", "needle_speed", "needle_rpm", "door_L", "door_R", "door_slide_R", "door_rear_L", "door_rear_R", "wiper_L", "wiper_R" })
            {
                var part = Find(inst.transform, name); var pivot = Find(model, name);
                if (!part || !pivot || pivot == part) continue;
                // Preserve the source pivot offset before nesting under its identical LOD0 pivot.
                part.SetParent(pivot, true);
                foreach (var r in part.GetComponentsInChildren<Renderer>(true)) list.Add(r);
                foreach (var t in part.GetComponentsInChildren<Transform>(true)) t.name = $"l{L}_" + t.name;
            }
            foreach (var t in inst.GetComponentsInChildren<Transform>(true))
            {
                if (t == inst.transform) continue;
                // (the cab-view windscreen surface: LOD0 only)
                if (t.name == "windscreen_fx") { t.gameObject.SetActive(false); }
                t.name = $"l{L}_" + t.name;
            }
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) if (r.gameObject.activeSelf) list.Add(r);
            lists[L - 1] = list;
        }
        var lg = model.gameObject.AddComponent<LODGroup>();
        lg.SetLODs(new[] { new LOD(0.22f, r0.ToArray()), new LOD(0.07f, lists[0].ToArray()), new LOD(0.012f, lists[1].ToArray()) });
        lg.RecalculateBounds();
    }

    /// three rows (rear first) of two euro pallets across the box, made from the old truck's load (its rows _0 / _1 alternate);
    /// each row's pieces are moved (the rows stay at local zero: FirmUnload slides the rows)
    static int AddPallets(Transform model, Transform template)
    {
        if (!template) return 0;
        var src = new List<Transform>();
        foreach (Transform c in template) if (c.name.StartsWith("load_pallets_")) src.Add(c);
        src.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        if (src.Count == 0) return 0;
        var load = new GameObject("load_pallets").transform; load.SetParent(model, false);
        const float floorY = 0.818f, rearZ = -3.38f, rowLen = 1.2f, gap = 0.05f;
        const int rows = 3;
        for (int k = 0; k < rows; k++)
        {
            var row = Object.Instantiate(src[k % src.Count].gameObject, load, false);
            row.name = "load_pallets_" + k; row.transform.localPosition = Vector3.zero; row.transform.localRotation = Quaternion.identity;
            row.SetActive(true);
            // bounds of the row's meshes in the model frame → centred across, bottom on the floor, at its slot along the box
            var b = new Bounds(); bool any = false; var inv = model.worldToLocalMatrix;
            foreach (var mf in row.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds; var M = inv * mf.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    var p = M.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (!any) continue;
            var target = new Vector3(0, floorY + 0.005f, rearZ + rowLen * 0.5f + k * (rowLen + gap));
            var d = new Vector3(target.x - b.center.x, target.y - b.min.y, target.z - b.center.z);
            var dl = row.transform.InverseTransformVector(model.TransformVector(d));
            foreach (Transform c in row.transform) c.localPosition += dl;
        }
        load.gameObject.SetActive(false);
        return rows;
    }
}
