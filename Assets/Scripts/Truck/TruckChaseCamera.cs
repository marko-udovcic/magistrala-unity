using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Close/blizu geometry from chaseView.ts and heading/orbit damping from CameraRig.tsx.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(100)]
public sealed class TruckChaseCamera : MonoBehaviour
{
    public TruckController target;
    [Tooltip("Additional chase zoom. Source back distance is 9.6 m for the 8 m truck.")]
    [Range(0.65f, 2f)] public float zoom = 1f;
    public LayerMask terrainMask = ~0;
    public bool CabinView { get; private set; }

    Camera view;
    TruckController previousTarget;
    bool initialized;
    float yaw, focusHeight, orbitYaw, orbitPitch, headYaw, headPitch;
    float lastMouseTime = float.NegativeInfinity;
    Vector3 lookPoint;
    readonly RaycastHit[] terrainHits = new RaycastHit[64];

    void Awake()
    {
        view = GetComponent<Camera>();
        view.nearClipPlane = 0.08f;
        if (!target) target = FindFirstObjectByType<TruckController>();
    }

    void LateUpdate()
    {
        if (!target)
        {
            target = FindFirstObjectByType<TruckController>();
            if (!target) return;
        }
        if (!target.IsReady) return;
        if (previousTarget != target) { initialized = false; previousTarget = target; }
        float dt = Mathf.Min(Time.deltaTime, 0.1f);
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.cKey.wasPressedThisFrame)
        {
            CabinView = !CabinView;
            initialized = false;
            headYaw = headPitch = 0;
        }
        Mouse mouse = Mouse.current;
        bool dragging = mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed);
        if (dragging)
        {
            Vector2 delta = mouse.delta.ReadValue();
            delta.x = Mathf.Clamp(delta.x, -80, 80);
            delta.y = Mathf.Clamp(delta.y, -80, 80);
            if (delta.sqrMagnitude > 0) lastMouseTime = Time.unscaledTime;
            if (CabinView)
            {
                headYaw = Mathf.Clamp(headYaw + delta.x * 0.0032f * Mathf.Rad2Deg, -126f, 126f);
                headPitch = Mathf.Clamp(headPitch - delta.y * 0.0024f * Mathf.Rad2Deg, -28.65f, 34.38f);
            }
            else
            {
                orbitYaw = Mathf.DeltaAngle(0, orbitYaw + delta.x * 0.0028f * Mathf.Rad2Deg);
                orbitPitch = Mathf.Clamp(orbitPitch - delta.y * 0.0022f * Mathf.Rad2Deg, -28.65f, 34.38f);
            }
        }
        if (mouse != null && !CabinView)
        {
            float scroll = mouse.scroll.ReadValue().y;
            zoom = Mathf.Clamp(zoom * Mathf.Exp(-scroll * 0.0008f), 0.65f, 2f);
        }
        // Preserve the stopped/reversing orbit; source eases back only moving forward, after 1.5 seconds idle.
        if (!dragging && Time.unscaledTime - lastMouseTime > 1.5f && target.SpeedKmh > 5 && target.Gear > 0)
        {
            float k = Ease(2.2f, dt);
            orbitYaw = Mathf.LerpAngle(orbitYaw, 0, k);
            orbitPitch = Mathf.Lerp(orbitPitch, 0, k);
        }
        Transform truck = target.transform; // Rigidbody interpolation is already applied to this render transform.
        Vector3 focus = truck.position;
        if (CabinView)
        {
            Vector3 eye = truck.TransformPoint(target.CabinEyeLocal);
            KeepAboveTerrain(ref eye);
            // Follow the interpolated cab rigidly so the eye cannot drift into the windscreen on corners.
            transform.SetPositionAndRotation(eye, truck.rotation * Quaternion.Euler(headPitch + 2.3f, headYaw, 0));
            float aspect = Mathf.Max(0.1f, view.aspect);
            view.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(Mathf.Tan(90f * Mathf.Deg2Rad * 0.5f) / aspect) * Mathf.Rad2Deg, 40f, 85f);
            initialized = false;
            return;
        }
        float heading = truck.eulerAngles.y;
        float speedK = Mathf.Clamp01(target.SpeedKmh / 90f);
        if (!initialized) { yaw = heading; focusHeight = focus.y; }
        yaw = Mathf.LerpAngle(yaw, heading, Ease(2.2f + Mathf.Clamp01(target.SpeedKmh / 72f) * 1.3f, dt));
        focusHeight = Mathf.Lerp(focusHeight, focus.y, Ease(4f, dt));
        float back = (9.6f + speedK * 1.2f) * zoom;
        float height = 4.16f + speedK * 0.4f;
        float elevation = Mathf.Clamp(Mathf.Atan2(height, back) + orbitPitch * Mathf.Deg2Rad, -0.17f, 0.79f);
        float distance = Mathf.Sqrt(back * back + height * height);
        Vector3 offset = Quaternion.Euler(0, yaw + orbitYaw, 0) * new Vector3(0, Mathf.Sin(elevation) * distance, -Mathf.Cos(elevation) * distance);
        Vector3 desired = new Vector3(focus.x, focusHeight, focus.z) + offset;
        groundRef = focus.y;
        KeepAboveTerrain(ref desired);
        Vector3 position = initialized ? Vector3.Lerp(transform.position, desired, Ease(9f, dt)) : desired;
        // Clamp again after smoothing so a rising slope cannot swallow the camera.
        KeepAboveTerrain(ref position);
        Vector3 ahead = focus + truck.forward * (7f + speedK * 8f) + Vector3.up * 0.2f;
        float orbitAmount = Mathf.Clamp01((Mathf.Abs(orbitYaw) + Mathf.Abs(orbitPitch)) * Mathf.Deg2Rad / 0.25f);
        ahead = Vector3.Lerp(ahead, focus + Vector3.up * 1.5f, orbitAmount);
        lookPoint = initialized ? Vector3.Lerp(lookPoint, ahead, Ease(6f, dt)) : ahead;
        Vector3 direction = lookPoint - position;
        transform.SetPositionAndRotation(position, direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction, Vector3.up) : transform.rotation);
        view.fieldOfView = 55f + speedK * 7f;
        initialized = true;
    }

    void KeepAboveTerrain(ref Vector3 position)
    {
        // High origin also recovers a camera already under a hill. Exclude the whole target, including its box.
        Vector3 origin = position + Vector3.up * 100f;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, terrainHits, 300f, terrainMask, QueryTriggerInteraction.Ignore);
        float highest = float.NegativeInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = terrainHits[i];
            if (hit.transform.IsChildOf(target.transform) || hit.normal.y < 0.3f) continue;
            // overhead structures (the d08 footbridge deck, ~5 m over the road): not ground — the camera went up through them
            if (hit.point.y > groundRef + 3.2f) continue;
            highest = Mathf.Max(highest, hit.point.y);
        }
        if (!float.IsNegativeInfinity(highest)) position.y = Mathf.Max(position.y, highest + 1f);
        // under an overhead structure: stay 0.6 m below its underside (the chase camera clipped through the footbridge)
        Vector3 up0 = new Vector3(position.x, groundRef + 1.2f, position.z);
        float span = position.y + 1.2f - up0.y;
        if (span > 0)
        {
            bool bf = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
            int n = Physics.RaycastNonAlloc(up0, Vector3.up, terrainHits, span, terrainMask, QueryTriggerInteraction.Ignore);
            Physics.queriesHitBackfaces = bf;
            float low = float.PositiveInfinity;
            for (int i = 0; i < n; i++) if (!terrainHits[i].transform.IsChildOf(target.transform) && !terrainHits[i].rigidbody) low = Mathf.Min(low, terrainHits[i].point.y);
            if (!float.IsPositiveInfinity(low)) position.y = Mathf.Max(up0.y, Mathf.Min(position.y, low - .6f));
        }
    }
    float groundRef;

    static float Ease(float rate, float dt) => 1f - Mathf.Exp(-rate * dt);
}
