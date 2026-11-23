// Cab interior animation for the driven vehicles (Atego / Galeb): the steering wheel turns with the front wheels and the
// speed / rpm needles follow the truck:
//   steering     node "steering", spins about its local Z (along the column), ±540° at full lock
//   needle_*     nodes "needle_speed" / "needle_rpm", local Z (normal of the dial), modelled at 0; the 260° sweep clockwise
//                from the driver is +260° about local Z in Unity's mirrored-X frame (cab interiors v3 (Atego wheel flattened; needle pivots unchanged):
//                BalkanDriver/assets-src/vehicles/kabina-v3/README.md)
// Added by TruckController once the model is found; nodes it does not find are simply skipped.
using UnityEngine;

[DisallowMultipleComponent]
public sealed class CabinRig : MonoBehaviour
{
    public float speedMax = 140f, rpmMax = 3500f, sweepDeg = 260f;
    /// road-wheel angle (rad) → steering-wheel angle (rad); ~0.6 rad full lock → ~9.4 rad (540°)
    public float steerRatio = 15f;

    TruckController tc;
    Transform steering, needleSpeed, needleRpm;
    Quaternion steering0, speed0, rpm0;

    public void Bind(TruckController controller, Transform model)
    {
        tc = controller;
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "steering" && !steering) { steering = t; steering0 = t.localRotation; }
            else if (t.name == "needle_speed" && !needleSpeed) { needleSpeed = t; speed0 = t.localRotation; }
            else if (t.name == "needle_rpm" && !needleRpm) { needleRpm = t; rpm0 = t.localRotation; }
        }
    }

    void LateUpdate()
    {
        if (!tc) return;
        if (steering) steering.localRotation = steering0 * Quaternion.AngleAxis(Mathf.Clamp(tc.SteerAngle * steerRatio * Mathf.Rad2Deg, -560f, 560f), Vector3.forward);
        if (needleSpeed) needleSpeed.localRotation = speed0 * Quaternion.AngleAxis(sweepDeg * Mathf.Clamp(tc.SpeedKmh / speedMax, 0f, 1.01f), Vector3.forward);
        if (needleRpm) needleRpm.localRotation = rpm0 * Quaternion.AngleAxis(sweepDeg * Mathf.Clamp(tc.EngineRpm / rpmMax, 0f, 1.01f), Vector3.forward);
    }
}
