using System.Collections;
using System.Text;
using UnityEngine;

namespace Magistrala
{
    /// Handling bench like the web game's tools/pw-scripts/handling_bench.mjs: on a private flat plate far below the world
    /// (and a 14 % ramp): step-steer roll at 0.4 g / 50 km/h, brake 50→0, 0–50 launch, 14 % pull-away; empty and +6 t.
    /// Start: Run() from script eval (play mode); results in Result and the log.
    public class PhysicsBench : MonoBehaviour
    {
        public string Result = "";
        public bool Done;
        TruckController t; Rigidbody rb;
        Vector3 lastV; Vector3 acc;
        const float FLAT_Y = -2000f, RAMP_Y = -3000f;

        public static PhysicsBench Run()
        {
            var tr = FindFirstObjectByType<TruckController>();
            var b = tr.GetComponent<PhysicsBench>() ?? tr.gameObject.AddComponent<PhysicsBench>();
            b.StopAllCoroutines(); b.Done = false; b.Result = ""; b.StartCoroutine(b.All());
            return b;
        }

        void FixedUpdate() { if (!rb) return; var v = rb.linearVelocity; acc = Vector3.Lerp(acc, (v - lastV) / Time.fixedDeltaTime, .2f); lastV = v; }

        void Plate()
        {
            if (GameObject.Find("BenchFlat")) return;
            var f = GameObject.CreatePrimitive(PrimitiveType.Cube); f.name = "BenchFlat";
            f.transform.position = new Vector3(0, FLAT_Y - 0.5f, 0); f.transform.localScale = new Vector3(3000, 1, 3000);
            var r = GameObject.CreatePrimitive(PrimitiveType.Cube); r.name = "BenchRamp";
            float a = Mathf.Atan(0.14f) * Mathf.Rad2Deg;
            r.transform.position = new Vector3(0, RAMP_Y, 0); r.transform.rotation = Quaternion.Euler(-a, 0, 0); r.transform.localScale = new Vector3(60, 1, 3000);
            foreach (var c in new[] { f, r }) { c.GetComponent<Renderer>().enabled = false; c.GetComponent<Collider>().material = new PhysicsMaterial { dynamicFriction = 1, staticFriction = 1, frictionCombine = PhysicsMaterialCombine.Average }; }
        }

        void Place(Vector3 p, Quaternion q)
        {
            rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
            rb.position = p; rb.rotation = q; transform.SetPositionAndRotation(p, q); lastV = Vector3.zero; acc = Vector3.zero;
        }
        void In(float th, float br, float st) { t.extThrottle = th; t.extBrake = br; t.extSteer = st; t.extHandbrake = false; }
        float Kmh => Vector3.Dot(rb.linearVelocity, transform.forward) * 3.6f;
        float Roll => Mathf.Asin(Mathf.Clamp(transform.right.y, -1, 1)) * Mathf.Rad2Deg;
        float Pitch => Mathf.Asin(Mathf.Clamp(transform.forward.y, -1, 1)) * Mathf.Rad2Deg;

        IEnumerator Settle(float s) { In(0, 0, 0); t.extHandbrake = true; float e = Time.time + s; while (Time.time < e) yield return new WaitForFixedUpdate(); t.extHandbrake = false; }

        IEnumerator ToSpeed(float kmh)
        {
            float e = Time.time + 60;
            while (Kmh < kmh - 0.5f && Time.time < e) { In(1, 0, 0); yield return new WaitForFixedUpdate(); }
            float h = Time.time + 2;
            while (Time.time < h) { In(Mathf.Clamp01((kmh - Kmh) * .5f), 0, 0); yield return new WaitForFixedUpdate(); }
        }

        IEnumerator All()
        {
            t = GetComponent<TruckController>(); rb = GetComponent<Rigidbody>();
            var ap = GetComponent<Autopilot>(); if (ap) ap.drive = false;
            t.externalInput = true; Plate();
            var sb = new StringBuilder("| test | prazan (6,3 t) | natovaren (+6 t) | web cilj (prazan / natovaren) |\n|---|---|---|---|\n");
            var rows = new string[6, 2];
            for (int load = 0; load < 2; load++)
            {
                t.SetCargo(load == 0 ? 0 : 6000);
                // 0–50 launch
                Place(new Vector3(-1000, FLAT_Y + 1.1f, -1200), Quaternion.identity); yield return Settle(2);
                float t0 = Time.time; while (Kmh < 50 && Time.time - t0 < 60) { In(1, 0, 0); yield return new WaitForFixedUpdate(); }
                rows[0, load] = $"{Time.time - t0:F1} s";
                // brake 50 → 0
                yield return ToSpeed(50);
                Vector3 p0 = rb.position; float b0 = Time.time, dive = 0, pitch0 = Pitch;
                while (Kmh > 0.3f && Time.time - b0 < 20) { In(0, 1, 0); dive = Mathf.Min(dive, Pitch - pitch0); yield return new WaitForFixedUpdate(); }
                var d = rb.position - p0; d.y = 0; rows[1, load] = $"{d.magnitude:F1} m ({Time.time - b0:F1} s, zaron {-dive:F1}°)";
                // step steer to 0.4 g at 50 km/h: closed loop on the measured lateral acceleration
                Place(new Vector3(0, FLAT_Y + 1.1f, -1200), Quaternion.identity); yield return Settle(1.5f);
                yield return ToSpeed(50);
                t.benchFullLock = true; float s0 = Time.time, steer = 0, rollSum = 0, peak = 0, minUp = 1; int n = 0; float latSum = 0;
                while (Time.time - s0 < 6)
                {
                    float lat = Vector3.Dot(acc, transform.right) / 9.81f;
                    steer = Mathf.Clamp(steer + (0.4f - lat) * 0.25f * Time.fixedDeltaTime, 0, 1);
                    In(Mathf.Clamp01((50 - Kmh) * .5f), 0, steer);
                    float r = Mathf.Abs(Roll); peak = Mathf.Max(peak, r); minUp = Mathf.Min(minUp, transform.up.y);
                    if (Time.time - s0 > 4.5f) { rollSum += r; latSum += lat; n++; }
                    yield return new WaitForFixedUpdate();
                }
                t.benchFullLock = false; rows[2, load] = $"{rollSum / Mathf.Max(1, n):F1}° pri {latSum / Mathf.Max(1, n):F2} g (vrh {peak:F1}°)";
                rows[3, load] = minUp < .5f ? "PREVRNUO SE" : $"ne (min up.y {minUp:F2})";
                // 14 % pull-away (loaded is the reference)
                float a = Mathf.Atan(0.14f);
                var q = Quaternion.Euler(-a * Mathf.Rad2Deg, 0, 0);
                Place(new Vector3(0, RAMP_Y + .5f / Mathf.Cos(a) - 800 * Mathf.Tan(a) + 1.2f, -800), q);
                // (snap onto the ramp surface)
                if (Physics.Raycast(rb.position + Vector3.up * 30, Vector3.down, out var hit, 80, ~0, QueryTriggerInteraction.Ignore)) Place(hit.point + hit.normal * 1.05f, q);
                yield return Settle(2);
                float g0 = Time.time; float v10 = 0, v20 = 0, vmin = 0;
                while (Time.time - g0 < 30) { In(1, 0, 0); float k = Kmh; vmin = Mathf.Min(vmin, k); if (v10 == 0 && Time.time - g0 >= 10) v10 = k; if (v20 == 0 && Time.time - g0 >= 20) v20 = k; yield return new WaitForFixedUpdate(); }
                rows[4, load] = $"{v10:F0} / {v20:F0} / {Kmh:F0} km/h (stupanj {t.Gear}{(vmin < -0.5f ? ", vraća se " + vmin.ToString("F1") : "")})";
                rows[5, load] = $"{rb.mass:F0} kg, CoM y {rb.centerOfMass.y:F2}";
                Place(new Vector3(0, FLAT_Y + 1.1f, -1200), Quaternion.identity); yield return Settle(1);
            }
            string[] names = { "0–50 km/h", "kočenje 50→0", "nagib pri 0,4 g, 50 km/h", "prevrtanje", "14 % uzbrdo s mjesta: 10 s / 20 s / 30 s", "masa, težište" };
            string[] web = { "≈ 11 s / ≈ 14 s", "≈ 18 m / ≈ 24 m", "≈ 2,5° / ≈ 4,0°", "ne", "— / ≈ 14 km/h", "6300 / 12300" };
            for (int i = 0; i < names.Length; i++) sb.Append($"| {names[i]} | {rows[i, 0]} | {rows[i, 1]} | {web[i]} |\n");
            Result = sb.ToString(); Done = true; t.SetCargo(0);
            Debug.Log("[bench-fizika]\n" + Result);
        }
    }
}
