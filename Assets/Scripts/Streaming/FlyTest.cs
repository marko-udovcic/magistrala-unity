// Test only (streaming drive tests): -flykmh K moves the driven truck kinematically along the autopilot's D8 path at K km/h, so the
// streaming scheduler's rings and lookahead see real motorway speed (the Atego itself tops out at ~75 km/h on the D8). Starts once
// scaled time runs (after the streaming start rings), wraps at the path's end. The chase camera follows the truck as usual.
using UnityEngine;

namespace Magistrala.Streaming
{
    public class FlyTest : MonoBehaviour
    {
        float speed, along; int seg; Vector3[] path; Rigidbody rb; TruckController tc; float t0 = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var a = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(a, "-flykmh");
            if (i < 0 || i + 1 >= a.Length || !float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var k)) return;
            var go = new GameObject("FlyTest"); DontDestroyOnLoad(go); go.AddComponent<FlyTest>().speed = k / 3.6f;
        }

        void FixedUpdate()
        {
            if (!tc)
            {
                tc = Magistrala.Game.MagistralaGame.DrivenTruck(); if (!tc) return;
                var ap = tc.GetComponent<Autopilot>(); if (!ap || ap.path == null || ap.path.Length < 2) { tc = null; return; }
                if (t0 < 0) { t0 = Time.time; tc = null; return; }
                if (Time.time - t0 < 1f) { tc = null; return; } // (Autopilot.Start smooths its path first)
                ap.drive = false; path = ap.path; rb = tc.GetComponent<Rigidbody>(); rb.isKinematic = true;
                float bd = float.MaxValue; for (int i = 0; i < path.Length; i++) { float d = (path[i] - rb.position).sqrMagnitude; if (d < bd) { bd = d; seg = i; } }
                seg = Mathf.Min(seg, path.Length - 2); along = 0;
                Debug.Log($"[fly] {speed * 3.6f:F0} km/h along the autopilot path ({path.Length} points) from point {seg}");
            }
            along += speed * Time.fixedDeltaTime;
            while (true)
            {
                float L = Vector3.Distance(path[seg], path[seg + 1]);
                if (along <= L) break;
                along -= L; seg++;
                if (seg >= path.Length - 1) { seg = 0; along = 0; Debug.Log("[fly] path end, back to the start"); break; }
            }
            Vector3 a = path[seg], b = path[seg + 1], dir = b - a; float len = dir.magnitude;
            var p = Vector3.Lerp(a, b, len > 0 ? along / len : 0) + Vector3.up * 1.2f; dir.y = 0;
            rb.MovePosition(p); if (dir.sqrMagnitude > 1e-4f) rb.MoveRotation(Quaternion.LookRotation(dir));
        }
    }
}
