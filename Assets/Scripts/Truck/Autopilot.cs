using UnityEngine;

namespace Magistrala
{
    /// Test driver (verification / demo): follows a lane path (built by the importer from the models' D8 road axes) with pure
    /// pursuit and a curve-aware speed target. Off by default; P toggles it.
    public class Autopilot : MonoBehaviour
    {
        public Vector3[] path;
        public bool drive;
        public float cruiseKmh = 70;
        TruckController truck;
        Magistrala.Traffic.TrafficSystem traffic;
        int seg; bool tipped; float holdT;
        /// last traffic reading (HUD / logs): gap to the car ahead, stop line distance, its light
        public float Gap { get; private set; } = 1e9f;
        public int Segment => seg;
        public float CrossTrack { get; private set; }

        void Start() { truck = GetComponent<TruckController>(); traffic = FindFirstObjectByType<Magistrala.Traffic.TrafficSystem>(); path = Smooth(path); seg = Nearest(); }

        // [hit] / [tip] lines (autopilot runs: what the truck ran into, when it rolled over)
        void OnCollisionEnter(Collision c)
        {
            if (!drive || c.impulse.magnitude < 3000) return;
            var g = transform.position;
            Debug.Log($"[hit] {c.collider.name} impulse {c.impulse.magnitude:F0} N·s rel {c.relativeVelocity.magnitude * 3.6f:F0} km/h at x={g.x - 4200:F0} z={-g.z - 5300:F0} v={truck.SpeedKmh:F0} up={transform.up.y:F2} gap={Gap:F0}");
        }

        /// resample to 4 m and smooth (the chained model axes have small kinks at the joins)
        static Vector3[] Smooth(Vector3[] src)
        {
            var r = new System.Collections.Generic.List<Vector3> { src[0] }; float carry = 0;
            for (int i = 0; i + 1 < src.Length; i++)
            {
                Vector3 a = src[i], b = src[i + 1]; float L = Vector3.Distance(a, b);
                for (float d = 4 - carry; d <= L; d += 4) r.Add(Vector3.Lerp(a, b, d / L));
                carry = (carry + L) % 4;
            }
            var o = new Vector3[r.Count];
            for (int i = 0; i < r.Count; i++) { Vector3 acc = Vector3.zero; int n = 0; for (int k = -3; k <= 3; k++) { int j = Mathf.Clamp(i + k, 0, r.Count - 1); acc += r[j]; n++; } o[i] = acc / n; }
            var keep = new System.Collections.Generic.List<Vector3> { o[0] };
            for (int i = 1; i < o.Length; i++) if (Vector3.Distance(o[i], keep[keep.Count - 1]) > 2f) keep.Add(o[i]);
            return keep.ToArray();
        }

        int Nearest()
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < path.Length; i++) { float d = (path[i] - transform.position).sqrMagnitude; if (d < bd) { bd = d; best = i; } }
            return best;
        }

        /// put the truck on the path at the point nearest to game x (tests: start the drive further along the D8)
        public void Teleport(float gameX)
        {
            int best = 0; for (int i = 0; i < path.Length; i++) if (Mathf.Abs(path[i].x - 4200 - gameX) < Mathf.Abs(path[best].x - 4200 - gameX)) best = i;
            var rb = GetComponent<Rigidbody>(); var dir = path[Mathf.Min(best + 1, path.Length - 1)] - path[best]; dir.y = 0;
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            transform.SetPositionAndRotation(path[best] + Vector3.up * 1.1f, Quaternion.LookRotation(dir)); rb.position = transform.position; rb.rotation = transform.rotation;
            seg = best;
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && kb.pKey.wasPressedThisFrame) { drive = !drive; seg = Nearest(); if (!drive) truck.externalInput = false; }
        }

        void FixedUpdate()
        {
            if (!drive || path == null || path.Length < 2 || !truck) return;
            truck.externalInput = true;
            Vector3 p = transform.position;
            while (seg < path.Length - 2 && Vector3.Dot(p - path[seg + 1], path[seg + 1] - path[seg]) > 0) seg++;
            // (recover if we fell behind: re-seek the nearest point in a window ahead)
            for (int i = seg + 1; i < Mathf.Min(path.Length - 1, seg + 30); i++) if ((path[i] - p).sqrMagnitude < (path[seg] - p).sqrMagnitude) seg = i;
            Vector3 a = path[seg], b = path[seg + 1], ab = b - a; ab.y = 0;
            Vector3 ap = p - a; ap.y = 0;
            CrossTrack = Vector3.Cross(ab.normalized, ap).y;
            float v = truck.SpeedKmh / 3.6f;
            float look = Mathf.Clamp(6 + v * 0.9f, 8, 30);
            // look-ahead point along the path
            Vector3 target = b; float left = look;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Mathf.Max(ab.sqrMagnitude, 1e-3f));
            Vector3 cur = a + (b - a) * t; int k = seg;
            while (k < path.Length - 1)
            {
                float d = Vector3.Distance(cur, path[k + 1]);
                if (d >= left) { target = cur + (path[k + 1] - cur).normalized * left; break; }
                left -= d; cur = path[k + 1]; k++; target = cur;
            }
            Vector3 local = transform.InverseTransformPoint(new Vector3(target.x, p.y, target.z)); // flat: a height step in the path must not shrink the curvature
            float curvature = 2 * local.x / Mathf.Max(local.sqrMagnitude, 1);
            float steerAngle = Mathf.Atan(curvature * 4.3f) * Mathf.Rad2Deg;
            // TruckController scales the input by its speed-sensitive lock: invert that so the wanted angle is what we get
            float v2 = v * v, lockRad = v2 < 1e-4f ? .72f : Mathf.Min(.72f, Mathf.Atan(4.3f * (4f + 1.5f / (1f + v2 / 36f)) / v2));
            truck.extSteer = Mathf.Clamp(steerAngle * Mathf.Deg2Rad / lockRad, -1, 1);
            // speed target from the path's curvature ahead
            float acc = 0;
            float vCurve = 99;
            for (int i = seg; i + 6 < path.Length && acc < 140; i += 2)
            {
                Vector3 d0 = path[i + 3] - path[i], d1 = path[i + 6] - path[i + 3]; d0.y = d1.y = 0;
                float kc = Vector3.Angle(d0, d1) * Mathf.Deg2Rad / 12f; acc += 8;
                // speed allowed there, and what we can brake to by then (1.5 m/s²)
                float vk = kc > 1e-4f ? Mathf.Sqrt(2.0f / kc) : 99;
                vCurve = Mathf.Min(vCurve, Mathf.Sqrt(vk * vk + 2 * 1.5f * Mathf.Max(0, acc - 10)));
            }
            float vt = Mathf.Min(cruiseKmh / 3.6f, vCurve);
            if (seg >= path.Length - 3) vt = 0;
            // traffic: it used to drive blind — into the queue at a red light (the AI cars are kinematic: an immovable wall that threw
            // the truck back and over, x ≈ −3090 at 02 / −1536 at 03). Follow the car ahead (keep 7 m standing, ~1.6 s moving, brake
            // ≤ 3 m/s²) and stop at red / amber stop lines (amber only when it can stop at ≤ 3 m/s²).
            if (traffic && traffic.PlayerAhead(transform.forward, out float gap, out float lv, out float stopD, out int light))
            {
                Gap = gap;
                float room = gap - 7 - v * 1.6f;
                vt = Mathf.Min(vt, Mathf.Sqrt(Mathf.Max(0, lv * lv + 2 * 3f * Mathf.Max(0, gap - 7))), lv + Mathf.Max(0, room) * .5f);
                if (light == 2 || (light == 1 && v * v / (2 * 3f) < stopD - 2))
                    vt = Mathf.Min(vt, stopD < 2.5f ? 0 : Mathf.Sqrt(2 * 2.2f * Mathf.Max(0, stopD - 2.5f)));
            }
            else Gap = 1e9f;
            float e = vt - v;
            truck.extThrottle = Mathf.Clamp01(e * 0.5f);
            truck.extBrake = Mathf.Clamp01(-e * 0.35f);
            // standing: hold it with the brakes (no creeping into the car ahead / over the line, no rolling back on a slope)
            holdT = vt < .3f && v < 1.5f ? holdT + Time.fixedDeltaTime : 0;
            if (holdT > 0) { truck.extThrottle = 0; truck.extBrake = 1; }
            truck.extHandbrake = holdT > 1;
            if (!tipped && transform.up.y < .6f) { tipped = true; var g = transform.position; Debug.Log($"[tip] truck rolled over at x={g.x - 4200:F0} z={-g.z - 5300:F0} v={truck.SpeedKmh:F0} gap={Gap:F0}"); }
        }
    }
}
