// The firm's main truck (vehicle id "atego": saves, fuel, parking poses and the fleet keep that id). Since 27. 11. it is the
// L4H1 isothermal box van (Resources/Vehicles/KombiL4H1/kombi-l4h1.glb, BalkanDriver assets-src/kombi-l4h1: izvjestaj.md),
// a 3.5 t-class van with a 2.3 l diesel and a 6-speed box, ~1.1 t payload, three rows of two euro pallets in the box, rear
// swing doors (FirmUnload doors mode, no tail lift).
// The scene's "Truck" still holds the old 7.5 t box truck model; TruckController.Awake calls Prepare before anything reads the
// model: the old model is replaced by the van (its pallet rows are reused as the van's load), the spec swapped.
//   -kamion atego   keep the old box truck (also env MAGISTRALA_KAMION=atego; editor: CliArgs SessionState)
// Van model frame (glTFast): +Z forward, +X right, origin on the ground. Box inside: floor y 0.818, roof 2.83, walls |x| 1.07,
// z −3.44 (rear doors) … 0.76 (bulkhead). Wheels r 0.365, wheelbase 4.09 m, track 1.81 m. LOD1 / LOD2 (39k / 9k) in a
// LODGroup; their wheel meshes ride on the LOD0 wheel spin nodes, every other LOD node is renamed l1_ / l2_ (the rigs bind
// by node name).
using System.Collections.Generic;
using UnityEngine;

public static class PlayerTruck
{
    public const string Resource = "Vehicles/KombiL4H1/kombi-l4h1";

    /// the old box truck instead of the van (-kamion atego)
    public static bool UseAtego
    {
        get
        {
            string k = Magistrala.CliArgs.Arg("-kamion") ?? System.Environment.GetEnvironmentVariable("MAGISTRALA_KAMION");
            return k != null && k.ToLowerInvariant() == "atego";
        }
    }

    /// the van's physics: 2450 kg empty, 3.5 t GVW (~1.1 t payload), 2.3 l diesel 380 Nm, 6 gears, rear-wheel drive
    public static TruckController.Spec VanSpec() => new TruckController.Spec
    {
        id = "atego", model = "l4h1", mass = 2450, radius = 0.365f, finalDrive = 4.35f, torqueNm = 380, topKmh = 140, reverseKmh = 18,
        idle = 800, redline = 4200, rpmScale = 1.7f, shiftTime = 0.4f,
        travel = 0.22f, sag = 0.10f, sagLoaded = 0.14f, springTarget = 0.3f, damping = 0.5f,
        ratios = new[] { 4.5f, 2.5f, 1.55f, 1.08f, 0.84f, 0.68f }, reverseRatio = 4.1f, dragArea = 3.1f, steerRate = 1.8f, maxSteer = 0.68f,
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

    /// TruckController.Awake of the scene truck (spec still the default "atego" box truck): put the van in
    public static void Prepare(TruckController tc)
    {
        if (tc.spec == null || tc.spec.id != "atego" || tc.spec.model != "atego" || UseAtego) return;
        var src = Resources.Load<GameObject>(Resource);
        if (!src) { Debug.LogWarning($"[kamion] Resources/{Resource}.glb missing: the old box truck stays"); return; }
        var old = tc.model ? tc.model : tc.transform.Find("Model");
        Transform oldLoad = null;
        if (old) foreach (var t in old.GetComponentsInChildren<Transform>(true)) if (t.name == "load_pallets") { oldLoad = t; break; }
        var m = Object.Instantiate(src, tc.transform, false); m.name = "Model"; m.layer = tc.gameObject.layer;
        AddLods(m.transform);
        int rows = AddPallets(m.transform, oldLoad);
        if (old) { old.SetParent(null, false); old.gameObject.SetActive(false); Object.Destroy(old.gameObject); }
        tc.model = m.transform; tc.modelYawDegrees = 0; tc.spec = VanSpec();
        Debug.Log($"[kamion] L4H1 izotermni sanduk ({rows} reda paleta); -kamion atego = stari kamion");
    }

    static Transform Find(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }

    /// LOD1 / LOD2 of the van in a LODGroup on the model
    static void AddLods(Transform model)
    {
        var r0 = new List<Renderer>(model.GetComponentsInChildren<Renderer>(true));
        var lods = new List<LOD> { };
        var lists = new List<Renderer>[2];
        for (int L = 1; L <= 2; L++)
        {
            var src = Resources.Load<GameObject>(Resource + "_lod" + L);
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
