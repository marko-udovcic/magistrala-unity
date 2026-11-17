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
        int seg;
        public int Segment => seg;
        public float CrossTrack { get; private set; }

        void Start() { truck = GetComponent<TruckController>(); path = Smooth(path); seg = Nearest(); }

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
            Vector3 local = transform.InverseTransformPoint(target);
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
            float e = vt - v;
            truck.extThrottle = Mathf.Clamp01(e * 0.5f);
            truck.extBrake = Mathf.Clamp01(-e * 0.35f);
            truck.extHandbrake = false;
        }
    }
}
