using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Empty firm_truck, Unity +Z forward. Root and model must use metre units and unit scale.</summary>
[DisallowMultipleComponent]
public sealed class TruckController : MonoBehaviour
{
    [Tooltip("Optional imported model container; otherwise Model or the first renderer's top-level child.")]
    public Transform model;
    [Tooltip("Additional yaw applied once at setup. Raw firm_truck GLB faces -Z; glTFast preserves Z.")]
    public float modelYawDegrees = 180f;
    public bool externalInput;
    /// payload (kg): mass, centre of mass, spring rates (load-levelling like the leaf packs firming up) and brake force follow
    public float CargoKg { get; private set; }
    Vector3 comEmpty, cargoCentre; float axleYc;
    [Range(0, 1)] public float extThrottle, extBrake;
    [Range(-1, 1)] public float extSteer;
    public bool extHandbrake;

    public float SpeedKmh => body ? body.linearVelocity.magnitude * 3.6f : 0f;
    public int Gear { get; private set; } = 1;
    public float EngineRpm { get; private set; } = 600f;
    public int Grounded { get; private set; }
    public bool IsReady => ready;
    public Vector3 CabinEyeLocal { get; private set; } = new Vector3(-0.55f, 1.3f, 2.75f);

    // truckPhysics.ts firm_truck: 6300 kg already agrees with the real empty box + tail-lift mass.
    const float Mass = 6300f, Radius = 0.46f, FinalDrive = 4.4f;
    const float Idle = 600f, Redline = 2450f, ShiftTime = 0.6f;
    const float Travel = 0.30f, Sag = 0.13f, SpringTarget = 0.3f; // (bench vs web: 0.09 m sag rolled only 1.6° at 0.4 g, web 2.5°)
    static readonly float[] Ratios = { 6.75f, 3.6f, 2.13f, 1.39f, 1f, 0.78f };
    static readonly float[] CurveRpm = { 500, 800, 1200, 1600, 1900, 2200, 2500 };
    static readonly float[] CurveTorque = { 0.42f, 0.68f, 1, 1, 0.95f, 0.87f, 0.3f };
    readonly WheelCollider[] wheels = new WheelCollider[4]; // FL, FR, RL, RR
    readonly Transform[] visuals = new Transform[4];
    readonly Quaternion[] visualOffsets = new Quaternion[4];
    readonly Vector3[] pivotOffsets = new Vector3[4];
    readonly float[] loadShare = new float[4];
    Rigidbody body;
    bool ready, resetRequested, handbrake;
    float throttleInput, brakeInput, steerInput, steerAngle, shiftTimer, sinceUp = 10f;
    float reverseHold, boost, brakePressure, previousSpeed, acceleration, wheelbase = 4.3f;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (!body) body = gameObject.AddComponent<Rigidbody>();
        body.mass = Mass;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearDamping = 0f;
        body.angularDamping = 0.5f;
        body.solverIterations = 12;
        body.sleepThreshold = 0f; // WheelCollider torque does not wake a sleeping body
        body.solverVelocityIterations = 6;
        body.isKinematic = false; // (WheelColliders created on a kinematic body never simulated: rpm 0, no ground force)
        Setup();
    }

    /// <summary>Idempotent; retries in Update if glTFast has not finished instantiating the child.</summary>
    public void Setup()
    {
        if (ready || !body) return;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        if (!model) model = transform.Find("Model");
        if (!model)
        {
            model = renderers[0].transform;
            while (model.parent && model.parent != transform) model = model.parent;
        }
        if (model == transform)
        {
            Debug.LogError("TruckController requires the imported visual model to be a child of the truck root.", this);
            enabled = false;
            return;
        }
        model.localRotation = Quaternion.AngleAxis(modelYawDegrees, Vector3.up) * model.localRotation;
        // glTFast mirrors X. Before yaw: eye (+.55, 1.3, -2.75); after 180 yaw it is the left seat.
        CabinEyeLocal = transform.InverseTransformPoint(model.TransformPoint(new Vector3(0.55f, 1.3f, -2.75f)));
        Transform[] nodes = model.GetComponentsInChildren<Transform>(true);
        var candidates = new List<Transform>();
        foreach (Transform node in nodes)
        {
            string n = node.name.ToLowerInvariant();
            if (!(n.Contains("wheel") || n.Contains("tyre") || n.Contains("tire"))) continue;
            bool nested = false;
            for (Transform p = node.parent; p && p != model; p = p.parent)
            {
                string pn = p.name.ToLowerInvariant();
                if (pn.Contains("wheel") || pn.Contains("tyre") || pn.Contains("tire")) { nested = true; break; }
            }
            if (!nested && node.GetComponentsInChildren<Renderer>(true).Length > 0) candidates.Add(node);
        }
        // Prefer the four GLB axle groups; classify geometrically after conversion, never assume mirrored L/R names.
        var named = candidates.FindAll(t => t.name == "wheel_0" || t.name == "wheel_1" || t.name == "wheel_2" || t.name == "wheel_3");
        if (named.Count == 4) candidates = named;
        Vector3[] centres = new Vector3[4];
        float[] distances = { float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue };
        foreach (Transform node in candidates)
        {
            Bounds b = VisualBounds(node);
            bool axleGroup = node.name == "wheel_0" || node.name == "wheel_1" || node.name == "wheel_2" || node.name == "wheel_3";
            Vector3 c = axleGroup ? transform.InverseTransformPoint(node.position) : b.center;
            int slot = (c.z > 0 ? 0 : 2) + (c.x > 0 ? 1 : 0);
            Vector3 expected = new Vector3(slot % 2 == 0 ? -1f : 1f, -0.47f, slot < 2 ? 2.2f : -2.1f);
            float distance = (c - expected).sqrMagnitude;
            if (distance < distances[slot]) { distances[slot] = distance; visuals[slot] = node; centres[slot] = c; }
        }
        for (int i = 0; i < 4; i++)
        {
            if (!visuals[i])
            {
                centres[i] = new Vector3((i % 2 == 0 ? -1f : 1f) * (i < 2 ? 1f : 0.93f), -0.47f, i < 2 ? 2.2f : -2.1f);
                Debug.LogWarning("Wheel visual missing in slot " + i + "; using firm_truck physics centre.", this);
            }
        }
        float front = (centres[0].z + centres[1].z) * 0.5f;
        float rear = (centres[2].z + centres[3].z) * 0.5f;
        wheelbase = Mathf.Max(1f, front - rear);
        float axleY = (centres[0].y + centres[1].y + centres[2].y + centres[3].y) * 0.25f;
        // Web CoM is y=.12 (1.05 above ground). Lower the Unity probe to .25 above axle for stability.
        body.centerOfMass = new Vector3(0, axleY + 0.6f, Mathf.Lerp(rear, front, 2.6f / 4.3f)); // ≈ 1.05 m above ground like the web spec (comEmpty y .12 on a 0.98 m body origin)
        comEmpty = body.centerOfMass; axleYc = axleY;
        float frontShare = Mathf.Clamp((body.centerOfMass.z - rear) / wheelbase, 0.3f, 0.7f);
        CreateBodyColliders(renderers, axleY - Radius);
        for (int i = 0; i < 4; i++)
        {
            var child = new GameObject("TruckWheelCollider_" + i);
            child.layer = gameObject.layer;
            child.transform.SetParent(transform, false);
            // Gravity compresses the spring by Sag from its unloaded target; retain 12 cm bump travel.
            child.transform.localPosition = centres[i] + Vector3.up * (Travel * (1f - SpringTarget) - Sag);
            WheelCollider w = child.AddComponent<WheelCollider>();
            wheels[i] = w;
            w.radius = Radius;
            w.mass = i < 2 ? 65f : 100f;
            w.suspensionDistance = Travel;
            loadShare[i] = (i < 2 ? frontShare : 1f - frontShare) * 0.5f;
            w.sprungMass = Mass * loadShare[i];
            float spring = w.sprungMass * 9.81f / Sag;
            w.suspensionSpring = new JointSpring { spring = spring, damper = 2f * 0.65f * Mathf.Sqrt(spring * w.sprungMass), targetPosition = SpringTarget };
            w.forceAppPointDistance = 0.05f; // tyre forces near the contact patch: realistic roll moment (0.32 m hid most of the body roll)
            w.wheelDampingRate = 1f;
            w.forwardFriction = Friction(0.35f, 1.1f, 0.8f, 0.8f);
            w.sidewaysFriction = Friction(0.22f, 1f, 0.5f, 0.78f);
            w.ConfigureVehicleSubsteps(10f, 5, 3);
            if (visuals[i])
            {
                visualOffsets[i] = Quaternion.Inverse(transform.rotation) * visuals[i].rotation;
                pivotOffsets[i] = Quaternion.Inverse(visuals[i].rotation) * (visuals[i].position - transform.TransformPoint(centres[i]));
            }
        }
        // the GLB's windscreen (alpha .25, mirror-smooth) read as dark blue tinted glass from the cabin
        foreach (Renderer r in renderers)
            if (r.name == "body_glass") foreach (Material m in r.materials) { if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", new Color(.75f, .8f, .83f, .1f)); }
        body.ResetInertiaTensor();
        body.isKinematic = false;
        ready = true;
    }

    static WheelFrictionCurve Friction(float slip, float value, float endSlip, float endValue)
    {
        return new WheelFrictionCurve { extremumSlip = slip, extremumValue = value, asymptoteSlip = endSlip, asymptoteValue = endValue, stiffness = 1f };
    }

    // Transform local renderer bounds corners directly: avoids oversized world-AABB boxes on a rotated truck.
    Bounds RendererBounds(Renderer renderer)
    {
        Bounds b = renderer.localBounds;
        Bounds result = new Bounds();
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            corner = transform.InverseTransformPoint(renderer.transform.TransformPoint(corner));
            if (i == 0) result = new Bounds(corner, Vector3.zero); else result.Encapsulate(corner);
        }
        return result;
    }

    Bounds VisualBounds(Transform node)
    {
        Bounds b = new Bounds(transform.InverseTransformPoint(node.position), Vector3.zero);
        bool first = true;
        foreach (Renderer r in node.GetComponentsInChildren<Renderer>(true))
        {
            Bounds rb = RendererBounds(r);
            if (first) { b = rb; first = false; } else b.Encapsulate(rb);
        }
        return b;
    }

    void CreateBodyColliders(Renderer[] renderers, float ground)
    {
        Bounds cab = new Bounds(), box = new Bounds(), chassis = new Bounds();
        bool hasCab = false, hasBox = false, hasChassis = false;
        foreach (Renderer r in renderers)
        {
            string n = r.name.ToLowerInvariant();
            Bounds b = RendererBounds(r);
            if (n.StartsWith("body_box")) Include(ref box, ref hasBox, b);
            else if (n == "body_chassis") Include(ref chassis, ref hasChassis, b);
            else if (n == "body_paint" || n == "body_glass" || n == "body_grille_dark" || n == "body_black_trim") Include(ref cab, ref hasCab, b);
        }
        // Generic model fallback: split renderer bounds at the front of the cargo body, omit wheel geometry.
        if (!hasCab || !hasBox || !hasChassis)
        {
            Bounds all = new Bounds(); bool any = false;
            foreach (Renderer r in renderers)
            {
                bool wheel = false;
                for (Transform p = r.transform; p && p != transform; p = p.parent)
                    if (p.name.ToLowerInvariant().Contains("wheel")) { wheel = true; break; }
                if (!wheel) Include(ref all, ref any, RendererBounds(r));
            }
            float split = Mathf.Lerp(all.min.z, all.max.z, 0.75f);
            if (!hasCab) cab.SetMinMax(new Vector3(all.min.x, ground + 0.8f, split), all.max);
            if (!hasBox) box.SetMinMax(new Vector3(all.min.x, ground + 1f, all.min.z), new Vector3(all.max.x, all.max.y, split));
            if (!hasChassis) chassis = new Bounds(new Vector3(0, ground + 0.65f, all.center.z), new Vector3(1.6f, 0.35f, all.size.z * 0.9f));
        }
        // Replace imported solid colliders to avoid hidden low geometry grounding the body.
        foreach (Collider c in GetComponentsInChildren<Collider>(true))
            if (c.attachedRigidbody == body && !c.isTrigger) c.enabled = false;
        AddBox("TruckCabCollider", cab, ground + 0.65f);
        AddBox("TruckCargoCollider", box, ground + 0.85f);
        cargoCentre = box.center;
        AddBox("TruckChassisCollider", chassis, ground + 0.4f);
    }

    static void Include(ref Bounds bounds, ref bool hasBounds, Bounds value)
    {
        if (!hasBounds) { bounds = value; hasBounds = true; } else bounds.Encapsulate(value);
    }

    void AddBox(string name, Bounds bounds, float floor)
    {
        Vector3 min = bounds.min, max = bounds.max;
        min.y = Mathf.Max(min.y, floor);
        max.y = Mathf.Max(max.y, min.y + 0.15f);
        var child = new GameObject(name);
        child.layer = gameObject.layer;
        child.transform.SetParent(transform, false);
        BoxCollider c = child.AddComponent<BoxCollider>();
        c.center = (min + max) * 0.5f;
        c.size = max - min;
    }

    void Update()
    {
        if (!ready) Setup();
        if (externalInput) return;
        Keyboard k = Keyboard.current;
        if (k != null && k.rKey.wasPressedThisFrame) resetRequested = true;
    }

    void ReadInput()
    {
        if (externalInput)
        {
            throttleInput = Mathf.Clamp01(extThrottle); brakeInput = Mathf.Clamp01(extBrake);
            steerInput = Mathf.Clamp(extSteer, -1f, 1f); handbrake = extHandbrake;
            return;
        }
        Keyboard k = Keyboard.current;
        throttleInput = k != null && (k.wKey.isPressed || k.upArrowKey.isPressed) ? 1f : 0f;
        brakeInput = k != null && (k.sKey.isPressed || k.downArrowKey.isPressed) ? 1f : 0f;
        steerInput = k == null ? 0f : (k.dKey.isPressed || k.rightArrowKey.isPressed ? 1f : 0f) - (k.aKey.isPressed || k.leftArrowKey.isPressed ? 1f : 0f);
        handbrake = k != null && k.spaceKey.isPressed;
    }

    void FixedUpdate()
    {
        if (!ready) return;
        ReadInput();
        if (resetRequested) { ResetUpright(); resetRequested = false; }
        float dt = Time.fixedDeltaTime;
        Vector3 forward = body.rotation * Vector3.forward;
        float speed = Vector3.Dot(body.linearVelocity, forward), kmh = Mathf.Abs(speed) * 3.6f;
        acceleration = Mathf.Lerp(acceleration, (speed - previousSpeed) / dt, Mathf.Min(1f, dt * 2f));
        previousSpeed = speed;
        Grounded = 0;
        foreach (WheelCollider w in wheels) if (w.isGrounded) Grounded++;
        if (Gear > 1 && kmh < 1f && shiftTimer <= 0) Gear = 1;
        float gas = throttleInput, brake = brakeInput;
        if (Gear > 0)
        {
            if (brakeInput > 0.1f && Mathf.Abs(speed) < 0.6f && throttleInput < 0.1f)
            {
                reverseHold += dt;
                if (reverseHold > 0.45f) { Gear = -1; reverseHold = 0; gas = brakeInput; brake = 0; }
                else brake = 1;
            }
            else reverseHold = 0;
        }
        else
        {
            gas = brakeInput; brake = 0;
            if (throttleInput > 0.1f)
            {
                gas = 0; brake = throttleInput;
                if (Mathf.Abs(speed) < 0.6f) { Gear = 1; gas = throttleInput; brake = brakeInput; }
            }
        }
        // Opposite momentum must be arrested before applying propulsion in the selected direction.
        if ((Gear > 0 && speed < -0.6f && gas > 0) || (Gear < 0 && speed > 0.6f && gas > 0)) { brake = Mathf.Max(brake, gas); gas = 0; }
        shiftTimer = Mathf.Max(0, shiftTimer - dt);
        sinceUp += dt;
        float ratio = Gear < 0 ? 6.06f : Ratios[Gear - 1];
        float roadRpm = Mathf.Abs(speed) / Radius * ratio * FinalDrive * 60f / (2f * Mathf.PI);
        float desiredRpm = Mathf.Max(Idle, roadRpm, Gear <= 1 ? Idle + gas * 600f : 0);
        EngineRpm = Mathf.Lerp(EngineRpm, Mathf.Min(Redline + 100f, desiredRpm), Mathf.Min(1, dt * 10f));
        if (Gear > 0 && shiftTimer <= 0 && speed > 0)
        {
            float up = gas > 0.6f ? 2100 : 1600, down = gas > 0.6f ? 1150 : 850;
            float afterSpeed = Mathf.Max(0, speed - Mathf.Max(0, 9.81f * (forward.y + 0.0065f)) * ShiftTime);
            float nextRpm = Gear < 6 ? afterSpeed / Radius * Ratios[Gear] * FinalDrive * 60f / (2f * Mathf.PI) : 0;
            float nextRatio = Gear < 6 ? Ratios[Gear] * FinalDrive : 0;
            float nextForce = 800f * TorqueFraction(nextRpm) * (0.5f + 0.375f * boost) * nextRatio * 0.85f / Radius;
            float resistance = body.mass * 9.81f * (forward.y + 0.0065f);
            bool pulls = forward.y <= 0.02f || nextForce > resistance || roadRpm > Redline + 50;
            if (Gear < 6 && pulls && ((roadRpm > up && nextRpm > down + 60) || (roadRpm > Redline - 40 && nextRpm > Idle + 250)))
            { Gear++; shiftTimer = ShiftTime; sinceUp = 0; }
            else if (Gear > 1 && EngineRpm < down && kmh > 1 && (sinceUp > ShiftTime + 1.5f || EngineRpm < Idle + 120) && (gas < 0.6f || acceleration < 0.05f))
            {
                float lowerRpm = roadRpm * Ratios[Gear - 2] / ratio;
                if (lowerRpm < 1950) { Gear--; shiftTimer = ShiftTime * 0.8f; }
            }
            ratio = Ratios[Gear - 1];
        }
        float boostWant = shiftTimer > 0 ? 0 : gas * Mathf.Min(1f, EngineRpm / 1100f);
        float tau = boostWant > boost ? 1.6f : (shiftTimer > 0 && gas > 0.05f ? 1.2f : 0.35f);
        boost = Mathf.Lerp(boost, boostWant, Mathf.Min(1, dt / tau));
        float force = shiftTimer > 0 ? 0 : 800f * TorqueFraction(EngineRpm) * gas * (0.5f + 0.5f * boost) * ratio * FinalDrive * 0.85f / Radius;
        float gradeForce = body.mass * 9.81f * Mathf.Max(0, (Gear < 0 ? -forward.y : forward.y) + 0.0065f);
        if (force > gradeForce) force = gradeForce + (force - gradeForce) / (1f + 0.0022f * Mathf.Pow(ratio * FinalDrive, 2));
        if (EngineRpm > Redline) force *= 0.1f;
        float top = Gear < 0 ? 14f : 95f;
        force *= Mathf.Clamp01((top + 3f - kmh) / 3f);
        brakePressure = Mathf.Lerp(brakePressure, brake, Mathf.Min(1, dt / 0.25f));
        float engineBrake = gas < 0.05f && kmh > 3 && shiftTimer <= 0 ? Mass * 0.32f * Mathf.Min(1, EngineRpm / Redline) * 1.4f * Radius / 2f : 0;
        float v2 = speed * speed;
        float limit = v2 < 0.0001f || benchFullLock ? 0.72f : Mathf.Min(0.72f, Mathf.Atan(wheelbase * (4f + 1.5f / (1f + v2 / 36f)) / v2));
        steerAngle = Mathf.MoveTowards(steerAngle, steerInput * limit, 1.3f * dt);
        for (int i = 0; i < 4; i++)
        {
            WheelCollider w = wheels[i];
            // Ackermann: inside front wheel takes a tighter radius.
            float steer = steerAngle;
            if (i < 2 && Mathf.Abs(steer) > 0.0001f)
            {
                float turnRadius = wheelbase / Mathf.Tan(Mathf.Abs(steer));
                float x = w.transform.localPosition.x;
                steer = Mathf.Sign(steer) * Mathf.Atan(wheelbase / Mathf.Max(0.3f, turnRadius - Mathf.Sign(steer) * x));
            }
            w.steerAngle = i < 2 ? steer * Mathf.Rad2Deg : 0;
            w.motorTorque = i >= 2 ? force * Radius * 0.5f * (Gear < 0 ? -1 : 1) : 0;
            w.brakeTorque = brakePressure * body.mass * Mathf.Lerp(5.8f, 4.5f, CargoKg / 6000f) * Radius * loadShare[i] + (i >= 2 ? engineBrake : 0);
            if (handbrake && i >= 2) w.brakeTorque += Mass * 6f * Radius * 0.5f;
            // PhysX locks a slow wheel under ANY brake torque: the asymptotic brakePressure lerp (1e-41 Nm) held the truck still
            if (w.brakeTorque < 1f) w.brakeTorque = 0f;
        }
        AntiRoll(0, 1, AntiRollFront * antiRollScale);
        AntiRoll(2, 3, AntiRollRear * antiRollScale);
        Vector3 velocity = body.linearVelocity;
        body.AddForce(-0.5f * 1.225f * 5.2f * velocity.magnitude * velocity);
        // Rolling resistance only at tyre contacts; cap it so it cannot reverse a near-stationary body.
        if (Grounded > 0 && Mathf.Abs(speed) > 0.001f)
            body.AddForce(-forward * Mathf.Sign(speed) * Mathf.Min(body.mass * 9.81f * 0.0065f, body.mass * Mathf.Abs(speed) / dt));
    }

    static float TorqueFraction(float rpm)
    {
        for (int i = 1; i < CurveRpm.Length; i++)
            if (rpm <= CurveRpm[i]) return Mathf.Lerp(CurveTorque[i - 1], CurveTorque[i], Mathf.InverseLerp(CurveRpm[i - 1], CurveRpm[i], rpm));
        return CurveTorque[CurveTorque.Length - 1];
    }

    void AntiRoll(int left, int right, float stiffness)
    {
        WheelCollider l = wheels[left], r = wheels[right];
        float lt = 1, rt = 1;
        bool lg = l.GetGroundHit(out WheelHit lh), rg = r.GetGroundHit(out WheelHit rh);
        if (lg) lt = (-l.transform.InverseTransformPoint(lh.point).y - l.radius) / l.suspensionDistance;
        if (rg) rt = (-r.transform.InverseTransformPoint(rh.point).y - r.radius) / r.suspensionDistance;
        float force = (lt - rt) * stiffness;
        if (lg) body.AddForceAtPosition(-l.transform.up * force, l.transform.position);
        if (rg) body.AddForceAtPosition(r.transform.up * force, r.transform.position);
    }

    void LateUpdate()
    {
        if (!ready) return;
        for (int i = 0; i < 4; i++)
        {
            if (!visuals[i]) continue;
            wheels[i].GetWorldPose(out Vector3 p, out Quaternion q);
            // Wheel poses come from physics; move them into the interpolated root frame for rendering.
            Quaternion renderDelta = transform.rotation * Quaternion.Inverse(body.rotation);
            p = transform.position + renderDelta * (p - body.position);
            q = renderDelta * q;
            Quaternion rotation = q * visualOffsets[i];
            visuals[i].SetPositionAndRotation(p + rotation * pivotOffsets[i], rotation);
        }
    }

    public void SetCargo(float kg)
    {
        if (!ready) Setup();
        CargoKg = Mathf.Max(0, kg);
        body.mass = Mass + CargoKg;
        // payload on the box floor, its centre ~0.55 m above the floor
        Vector3 cc = new Vector3(0, axleYc + 1.55f, cargoCentre.z);
        body.centerOfMass = (comEmpty * Mass + cc * CargoKg) / body.mass;
        float front = (wheels[0].transform.localPosition.z + wheels[1].transform.localPosition.z) * .5f, rear = (wheels[2].transform.localPosition.z + wheels[3].transform.localPosition.z) * .5f;
        float frontShare = Mathf.Clamp((body.centerOfMass.z - rear) / Mathf.Max(1, front - rear), 0.2f, 0.7f);
        for (int i = 0; i < 4; i++)
        {
            var w = wheels[i];
            loadShare[i] = (i < 2 ? frontShare : 1f - frontShare) * 0.5f;
            w.sprungMass = body.mass * loadShare[i];
            // progressive springs: loaded sag 0.12 m instead of the empty 0.09 m (no bottoming on the 0.28 m travel)
            float sag = Mathf.Lerp(Sag, 0.16f, CargoKg / 6000f);
            float spring = w.sprungMass * 9.81f / sag;
            w.suspensionSpring = new JointSpring { spring = spring, damper = 2f * 0.65f * Mathf.Sqrt(spring * w.sprungMass), targetPosition = SpringTarget };
        }
        antiRollScale = body.mass / Mass;
        body.ResetInertiaTensor();
        body.WakeUp();
    }
    float antiRollScale = 1f;
    public float AntiRollFront = 2000f, AntiRollRear = 1500f;
    /// bench only: steer to the full lock at any speed (the web bench sets the wheel angle directly)
    public bool benchFullLock;

    public void ResetUpright()
    {
        if (!body) return;
        Vector3 heading = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, Vector3.up);
        if (heading.sqrMagnitude < 0.001f) heading = Vector3.ProjectOnPlane(body.rotation * Vector3.up, Vector3.up);
        if (heading.sqrMagnitude < 0.001f) heading = Vector3.forward;
        body.position += Vector3.up;
        body.rotation = Quaternion.LookRotation(heading, Vector3.up);
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        Gear = 1; EngineRpm = Idle; Grounded = 0;
        shiftTimer = reverseHold = boost = brakePressure = steerAngle = previousSpeed = acceleration = 0;
        sinceUp = 10;
        foreach (WheelCollider w in wheels) if (w) { w.motorTorque = 0; w.brakeTorque = 0; w.steerAngle = 0; }
        body.WakeUp();
    }
}
