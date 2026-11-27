// Player turn signals. , / . = left / right, backslash = hazards (E is vehicle interaction).
// Bind after VehicleLook: split combined indicator submeshes once, by triangle position in the vehicle frame.
// No mesh/material queries, Resources loads or managed allocations in Update / blink transitions.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Magistrala.Audio;

[DisallowMultipleComponent]
public sealed class TurnSignals : MonoBehaviour
{
    public int Direction { get; private set; } // traffic convention: -1 left, +1 right; independent of blink phase
    public bool Hazards { get; private set; }
    public bool Lit { get; private set; }
    public bool LeftLit => Lit && (Hazards || Direction < 0);
    public bool RightLit => Lit && (Hazards || Direction > 0);
    struct Lamp { public Material material; public int side; public bool dash; }
    readonly List<Lamp> lamps = new List<Lamp>();
    readonly List<Mesh> meshes = new List<Mesh>();
    readonly List<Material> ownedMaterials = new List<Material>();
    static readonly int Emis = Shader.PropertyToID("emissiveFactor"), Emission = Shader.PropertyToID("_EmissionColor");
    TruckController truck;
    TruckChaseCamera chase;
    MagistralaAudio audioSystem;
    AudioClip tickOn, tickOff;
    float phase, turnYaw;
    bool armed;
    Vector3 previousForward;

    public void Bind(TruckController controller)
    {
        if (truck) return;
        truck = controller;
        if (!truck) return;
        truck.Signals = this;
        chase = FindFirstObjectByType<TruckChaseCamera>();
        previousForward = transform.forward;
        foreach (var r in truck.model.GetComponentsInChildren<MeshRenderer>(true)) Split(r);
        // Optional dashboard arrow nodes in other vehicle models. L4H1's current texture has no arrows.
        foreach (var r in truck.model.GetComponentsInChildren<Renderer>(true))
        {
            int side = r.name == "indicator_left" || r.name == "arrow_left" ? -1
                : r.name == "indicator_right" || r.name == "arrow_right" ? 1 : 0;
            if (side != 0) foreach (var m in r.materials) AddLamp(m, side, true);
        }
        WriteLamps();
    }

    void Start()
    {
        audioSystem = MagistralaAudio.I;
        if (!audioSystem) return;
        tickOn = audioSystem.Clip("motor/indicator_on");
        tickOff = audioSystem.Clip("motor/indicator_off");
    }

    void AddLamp(Material m, int side, bool dash)
    {
        if (!m) return;
        m.EnableKeyword(m.HasProperty(Emis) ? "_EMISSIVE" : "_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        lamps.Add(new Lamp { material = m, side = side, dash = dash });
    }

    void Split(MeshRenderer r)
    {
        var mf = r.GetComponent<MeshFilter>();
        if (!mf || !mf.sharedMesh) return;
        var mats = new List<Material>(r.sharedMaterials);
        var source = mf.sharedMesh;
        Mesh mesh = null;
        Vector3[] vertices = null;
        int count = Mathf.Min(source.subMeshCount, mats.Count);
        for (int si = 0; si < count; si++)
        {
            var original = mats[si];
            if (!original) continue;
            string name = original.name.Replace(" (Instance)", "");
            if (name != "Lights_Indicator" && name != "L_blink" && !name.StartsWith("L_blink_")) continue;
            if (!mesh)
            {
                mesh = Instantiate(source); mesh.name = source.name + "_signals";
                meshes.Add(mesh); mf.sharedMesh = mesh; vertices = mesh.vertices;
            }
            var left = new List<int>(); var right = new List<int>();
            var indices = mesh.GetTriangles(si);
            var toTruck = truck.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
            for (int i = 0; i < indices.Length; i += 3)
            {
                var c = (vertices[indices[i]] + vertices[indices[i + 1]] + vertices[indices[i + 2]]) / 3f;
                var dst = toTruck.MultiplyPoint3x4(c).x < 0 ? left : right;
                dst.Add(indices[i]); dst.Add(indices[i + 1]); dst.Add(indices[i + 2]);
            }
            var lm = new Material(original) { name = "L_blink_left" };
            var rm = new Material(original) { name = "L_blink_right" };
            ownedMaterials.Add(lm); ownedMaterials.Add(rm);
            mats[si] = lm;
            int extra = mesh.subMeshCount; mesh.subMeshCount = extra + 1;
            mesh.SetTriangles(left, si); mesh.SetTriangles(right, extra);
            mats.Add(rm); AddLamp(lm, -1, false); AddLamp(rm, 1, false);
        }
        if (mesh) r.sharedMaterials = mats.ToArray();
    }

    public void SetDirection(int direction)
    {
        Direction = direction < 0 ? -1 : direction > 0 ? 1 : 0;
        Hazards = false;
        Restart();
    }

    public void SetHazards(bool on)
    {
        Hazards = on; Direction = 0;
        Restart();
    }

    void Restart()
    {
        phase = turnYaw = 0; armed = false; previousForward = transform.forward;
        SetLit(Hazards || Direction != 0);
        WriteLamps(); // side may change while the phase is already lit
    }

    void Update()
    {
        if (!truck) return;
        var game = Magistrala.Game.MagistralaGame.I;
        bool input = !truck.externalInput && !DroneCamera.Active && !Magistrala.Game.WorldMap.IsOpen
            && !(game && game.Phone && game.Phone.Open) && (!game || game.Truck == truck);
        var k = input ? Keyboard.current : null;
        if (k != null)
        {
            if (k.commaKey.wasPressedThisFrame) SetDirection(Direction < 0 && !Hazards ? 0 : -1);
            if (k.periodKey.wasPressedThisFrame) SetDirection(Direction > 0 && !Hazards ? 0 : 1);
            if (k.backslashKey.wasPressedThisFrame) SetHazards(!Hazards);
        }
        var f = transform.forward;
        float yaw = Vector3.SignedAngle(previousForward, f, transform.up);
        previousForward = f;
        Advance(Time.deltaTime, truck.SpeedKmh, truck.Gear, truck.SteerAngle, yaw);
    }

    // Kept independent of Input/physics/audio for deterministic, muted tests without starting Unity.
    void Advance(float dt, float speed, int gear, float steerAngle, float yawDelta)
    {
        if (!Hazards && Direction != 0 && speed > 3 && gear > 0 && dt > 0)
        {
            turnYaw += yawDelta * Direction;
            float steer = steerAngle * Direction;
            if (steer > .08f || turnYaw > 20) armed = true;
            // A parking wiggle or countersteer alone cannot cancel: require actual heading change in the signalled direction.
            if (armed && turnYaw > 12 && steer < .025f) SetDirection(0);
        }
        if (!Hazards && Direction == 0) return;
        phase = (phase + dt * 1.5f) % 1f;
        SetLit(phase < .5f);
    }

    void SetLit(bool on)
    {
        if (Lit == on) return;
        Lit = on; WriteLamps();
        if (audioSystem && !MagistralaAudio.Muted && chase && chase.target == truck && chase.CabinView)
            audioSystem.Play2D(on ? tickOn : tickOff, Bus.Motor, .3f);
    }

    void WriteLamps()
    {
        foreach (var lamp in lamps)
        {
            bool on = lamp.side < 0 ? LeftLit : RightLit;
            Color color = lamp.dash ? new Color(.1f, 1, .15f) * 2 : new Color(1, .32f, .005f) * 5;
            lamp.material.SetColor(lamp.material.HasProperty(Emis) ? Emis : Emission, on ? color : Color.black);
        }
    }

    void OnDisable()
    {
        Direction = 0; Hazards = false; Lit = false; phase = turnYaw = 0; armed = false;
        WriteLamps();
    }

    void OnDestroy()
    {
        foreach (var mesh in meshes) if (mesh) Destroy(mesh);
        foreach (var material in ownedMaterials) if (material) Destroy(material);
    }
}
