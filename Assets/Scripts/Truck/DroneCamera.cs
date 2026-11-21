using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>F: free-flying drone camera (toggles with the chase camera). Starts above / behind the truck; WASD / arrows fly
/// horizontally relative to the view, E / Space up, Q / Ctrl down, right (or left) mouse button + mouse or Z / X (turn) and
/// R / T (look up / down) rotate, Shift is fast. Smoothed velocity, at least MinClear m above the ground (terrain, buildings),
/// at most MaxAgl m above it. While it flies the truck gets no driving input: service brake + parking brake (unless the
/// autopilot is driving it). Player switches: `-dron` starts in drone mode; `-dronh H` starts H m above the truck (default 45);
/// `-dronlet S` (tests / clips) flies forward at S × the normal speed without keys (S > 1: that many times faster).</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(100)]
public sealed class DroneCamera : MonoBehaviour
{
    public static bool Active { get; private set; }
    /// ground under the drone (for the location label), valid while Active
    public static Vector3 GroundPoint { get; private set; }

    const float Speed = 22f, FastK = 4f, Climb = 10f, MinClear = 3f, MaxAgl = 400f, Accel = 2.5f;
    TruckChaseCamera chase; Camera view; TruckController truck;
    Vector3 vel; float yaw, pitch, agl; bool startDron, weBrake; float autoFwd, startH = 45, startP = -1;
    readonly RaycastHit[] hits = new RaycastHit[32];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach()
    {
        var c = FindFirstObjectByType<TruckChaseCamera>();
        if (c && !c.GetComponent<DroneCamera>()) c.gameObject.AddComponent<DroneCamera>();
    }

    void Awake()
    {
        chase = GetComponent<TruckChaseCamera>(); view = GetComponent<Camera>();
        var a = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] == "-dron") startDron = true;
            if (a[i] == "-dronh" && i + 1 < a.Length) float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out startH);
            if (a[i] == "-dronp" && i + 1 < a.Length) float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out startP);
            if (a[i] == "-dronlet" && i + 1 < a.Length) float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out autoFwd);
        }
    }

    void OnDisable() { if (Active) Exit(); }

    void Update()
    {
        if (!truck || !truck.isActiveAndEnabled) truck = chase && chase.target && chase.target.isActiveAndEnabled ? chase.target : FindFirstObjectByType<TruckController>();
        if (!truck || !truck.IsReady) return;
        // (-dron waits 2.5 s: the test switch -tpxz moves the truck after 1 s)
        if (startDron && Time.timeSinceLevelLoad > 2.5f) { startDron = false; Enter(); Debug.Log("[dron] -dron: start in drone mode"); }
        var kb = Keyboard.current;
        if (kb != null && kb.fKey.wasPressedThisFrame && !PhoneOpen()) { if (Active) Exit(); else Enter(); }
        if (!Active) return;
        // the truck stands: brakes on, no player input (the autopilot, if it is driving, keeps its own input)
        var ap = truck.GetComponent<Magistrala.Autopilot>();
        if (ap && ap.drive) weBrake = false;
        else
        {
            weBrake = true; truck.externalInput = true;
            truck.extThrottle = 0; truck.extSteer = 0; truck.extBrake = 1; truck.extHandbrake = true;
        }
    }

    static bool PhoneOpen() { var g = Magistrala.Game.MagistralaGame.I; return (g && g.Phone && g.Phone.Open) || Magistrala.Game.WorldMap.IsOpen; }

    void Enter()
    {
        Active = true; vel = Vector3.zero;
        if (chase) chase.enabled = false;
        Transform t = truck.transform;
        Vector3 f = t.forward; f.y = 0; if (f.sqrMagnitude < 1e-4f) f = Vector3.forward; f.Normalize();
        yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg; pitch = startP >= 0 ? startP : 32f;
        // -dronp P: start pitch; at >= 80 deg the drone starts straight above the truck (top view for map comparisons)
        Vector3 back = startP >= 80 ? Vector3.zero : f * startH * 1.2f;
        transform.SetPositionAndRotation(t.position + Vector3.up * startH - back, Quaternion.Euler(pitch, yaw, 0));
        view.fieldOfView = 60f;
        Debug.Log("[dron] on");
    }

    void Exit()
    {
        Active = false;
        if (truck && weBrake) { truck.externalInput = false; truck.extBrake = 0; truck.extHandbrake = false; }
        weBrake = false;
        if (chase) chase.enabled = true; // re-initialises itself behind the truck (target unchanged → smooth from its last state)
        Debug.Log("[dron] off");
    }

    void LateUpdate()
    {
        if (!Active) return;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        var kb = Keyboard.current; var mouse = Mouse.current;
        bool input = !PhoneOpen();
        float ix = 0, iz = Mathf.Min(autoFwd, 1), iy = 0, turn = 0, look = 0; bool fast = false;
        if (input && kb != null)
        {
            iz += (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1 : 0) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1 : 0);
            ix += (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1 : 0) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1 : 0);
            iy += (kb.eKey.isPressed || kb.spaceKey.isPressed ? 1 : 0) - (kb.qKey.isPressed || kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed ? 1 : 0);
            turn = (kb.xKey.isPressed ? 1 : 0) - (kb.zKey.isPressed ? 1 : 0);
            look = (kb.tKey.isPressed ? 1 : 0) - (kb.rKey.isPressed ? 1 : 0);
            fast |= kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
        }
        if (input && mouse != null && (mouse.rightButton.isPressed || mouse.leftButton.isPressed))
        {
            Vector2 d = mouse.delta.ReadValue();
            yaw += Mathf.Clamp(d.x, -120, 120) * 0.15f;
            pitch -= Mathf.Clamp(d.y, -120, 120) * 0.12f;
        }
        yaw += turn * 75f * dt; pitch += look * 55f * dt;
        pitch = Mathf.Clamp(pitch, -60f, 89f); yaw = Mathf.Repeat(yaw, 360f);
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
        // horizontal speed grows with the height above the ground (fine moves low down, crossing Kaštela from up high)
        float hk = 1f + Mathf.Clamp(agl, 0, MaxAgl) / 120f, k = (fast ? FastK : 1f) * Mathf.Max(1f, autoFwd);
        Vector3 fwd = Quaternion.Euler(0, yaw, 0) * Vector3.forward, right = Quaternion.Euler(0, yaw, 0) * Vector3.right;
        Vector3 hor = fwd * iz + right * ix; if (hor.sqrMagnitude > 1) hor.Normalize();
        Vector3 wish = hor * Speed * hk * k + Vector3.up * Mathf.Clamp(iy, -1, 1) * Climb * k * Mathf.Sqrt(hk);
        vel = Vector3.Lerp(vel, wish, 1f - Mathf.Exp(-Accel * dt));
        Vector3 p = transform.position + vel * dt;
        // ground: highest hit under the drone (terrain, roofs, the truck itself counts as well)
        // and 0.8 s ahead along the flight: a slope or a roof coming up lifts the drone early and smoothly
        float ground = Ground(p);
        Vector3 ahead = p + new Vector3(vel.x, 0, vel.z) * 0.8f;
        float groundAhead = new Vector2(vel.x, vel.z).sqrMagnitude > 1f ? Ground(ahead) : float.NegativeInfinity;
        if (!float.IsNegativeInfinity(ground))
        {
            float lo = ground + MinClear, hi = ground + MaxAgl, soft = Mathf.Max(lo, groundAhead + MinClear);
            if (p.y < soft) { p.y = Mathf.Lerp(p.y, soft, 1f - Mathf.Exp(-4f * dt)); if (vel.y < 0) vel.y = 0; }
            if (p.y < lo) { p.y = Mathf.Lerp(p.y, lo, 1f - Mathf.Exp(-12f * dt)); if (p.y < lo - 0.5f) p.y = lo - 0.5f; }
            if (p.y > hi) { p.y = hi; if (vel.y > 0) vel.y = 0; }
            agl = p.y - ground; GroundPoint = new Vector3(p.x, ground, p.z);
        }
        else { agl = p.y; GroundPoint = new Vector3(p.x, 0, p.z); }
        transform.SetPositionAndRotation(p, rot);
        view.fieldOfView = 60f;
    }

    float Ground(Vector3 p)
    {
        int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 600f, p.z), Vector3.down, hits, 2000f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        for (int i = 0; i < n; i++) best = Mathf.Max(best, hits[i].point.y);
        return best;
    }

    void OnGUI()
    {
        if (!Active || Magistrala.GpuSwitches.NoHud || Event.current.type != EventType.Repaint) return;
        var st = Magistrala.HudStyle.St(14, false, Color.white, TextAnchor.MiddleCenter);
        string s = $"DRON {agl:0} m  ·  WASD let · E/Q gore/dolje · desni klik + miš ili Z/X R/T okret · Shift brže · F kamion";
        float K = Magistrala.HudStyle.K, w = Mathf.Min(Screen.width - 2 * Magistrala.HudStyle.Margin, Magistrala.HudStyle.Width(st, s) + 36 * K);
        // top centre, on the HUD margin (the HUD panel is at the bottom right)
        var r = new Rect(Mathf.Round((Screen.width - w) / 2), Magistrala.HudStyle.Margin, w, 34 * K);
        Magistrala.HudStyle.PanelBg(r, 12);
        Magistrala.HudStyle.Fill(new Rect(r.x + 16 * K, r.yMax - Mathf.Max(2, 2 * K), r.width - 32 * K, Mathf.Max(2, 2 * K)), Magistrala.HudStyle.Accent, K);
        Magistrala.HudStyle.Label(r, s, st);
    }
}
