// Test switch -streettrace (player / play mode): every 0.5 s the truck's position, speed and what lies under its four wheel corners
// (S = graph street / court of Build streets, R = Marko's road models, T = terrain, ? = nothing), plus vertical jolts (|Δv_y| / dt
// over 6 m/s² at a speed over 10 km/h: a bump at a seam). Self-installing, no scene change.
using UnityEngine;

namespace Magistrala
{
    public class StreetsTrace : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-streettrace") < 0) return;
            DontDestroyOnLoad(new GameObject("StreetsTrace").AddComponent<StreetsTrace>().gameObject);
        }

        Rigidbody rb; float next, vy0 = float.NaN, t0; int off, samples, jolts;
        static char Kind(Collider c)
        {
            if (!c) return '?';
            if (c is TerrainCollider) return 'T';
            for (var t = c.transform; t; t = t.parent) { if (t.name == "Streets") return 'S'; if (t.name == "Roads") return 'R'; }
            return 'o';
        }
        char Under(Transform tr, Vector3 local)
        {
            var p = tr.TransformPoint(local) + Vector3.up * 2f; RaycastHit best = default; float bd = float.MaxValue;
            foreach (var h in Physics.RaycastAll(p, Vector3.down, 6f)) { if (h.collider.attachedRigidbody == rb || h.collider.isTrigger) continue; if (h.distance < bd) { bd = h.distance; best = h; } }
            return bd < float.MaxValue ? Kind(best.collider) : '?';
        }
        void FixedUpdate()
        {
            if (!rb) { var tc = FindFirstObjectByType<TruckController>(); if (tc) { rb = tc.GetComponent<Rigidbody>(); t0 = Time.time; } return; }
            float vy = rb.linearVelocity.y, kmh = rb.linearVelocity.magnitude * 3.6f;
            if (!float.IsNaN(vy0) && kmh > 10 && Time.time - t0 > 3)
            {
                float a = Mathf.Abs(vy - vy0) / Time.fixedDeltaTime;
                if (a > 6 && jolts++ < 40) { var g = rb.position; Debug.Log($"[trace] jolt {a:F1} m/s² at x={g.x - 4200:F1} z={-g.z - 5300:F1} v={kmh:F0}"); }
            }
            vy0 = vy;
            if (Time.time < next) return; next = Time.time + .5f;
            var tr = rb.transform; string s = "" + Under(tr, new Vector3(-1.1f, 0, 2.6f)) + Under(tr, new Vector3(1.1f, 0, 2.6f)) + Under(tr, new Vector3(-1.1f, 0, -2.2f)) + Under(tr, new Vector3(1.1f, 0, -2.2f));
            samples++; if (s.IndexOf('T') >= 0 || s.IndexOf('?') >= 0) off++;
            var q = rb.position;
            Debug.Log($"[trace] t={Time.time - t0:F1} x={q.x - 4200:F1} z={-q.z - 5300:F1} v={kmh:F0} under={s} off={off}/{samples} jolts={jolts}");
        }
    }
}
