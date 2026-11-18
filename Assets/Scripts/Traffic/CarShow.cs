// -carshow policija[,oktan,golub…]: parks traffic kinds in a row beside the player's truck at the start (for screenshots of the
// traffic cars; drawn like TrafficSystem draws them: lod parts, lamps, beacons). -carcam f34,left,right,rear,r34,row,d40,d60 then
// moves the main camera through those views of the first car, one every -carcamdt seconds (default 2.5) from -carcamt0 (default 7),
// so -shots can capture them. -carpaint i: paint colour index of every car (default 0). -carlod 0|1|2: force a parts level.
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.Traffic
{
    [DefaultExecutionOrder(1000)]
    public class CarShow : MonoBehaviour
    {
        class Shown { public TrafficKind kd; public int paint; public Vector3 pos; public Quaternion rot; }
        readonly List<Shown> shown = new List<Shown>();
        string[] views; float dt = 2.5f, t0 = 7f; int forceLod = -1;
        TrafficSystem ts;
        bool placed;

        static string Arg(string k)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == k) return a[i + 1];
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (Arg("-carshow") != null) new GameObject("CarShow").AddComponent<CarShow>(); }

        void Start()
        {
            if (Arg("-carcam") is string v) views = v.Split(',');
            if (Arg("-carcamdt") is string d) dt = float.Parse(d, CultureInfo.InvariantCulture);
            if (Arg("-carcamt0") is string s0) t0 = float.Parse(s0, CultureInfo.InvariantCulture);
            if (Arg("-carlod") is string l) forceLod = int.Parse(l);
        }

        void Place()
        {
            ts = FindFirstObjectByType<TrafficSystem>();
            var truck = FindFirstObjectByType<TruckController>();
            if (!ts || !ts.data || !truck) return;
            int paint = Arg("-carpaint") is string p ? int.Parse(p) : 0;
            var tr = truck.transform; var fwd = tr.forward; fwd.y = 0; fwd.Normalize(); var right = Vector3.Cross(Vector3.up, fwd);
            var names = Arg("-carshow").Split(',');
            for (int i = 0; i < names.Length; i++)
            {
                var kd = System.Array.Find(ts.data.kinds, k => k.name == names[i]);
                if (kd == null) { Debug.LogWarning("[carshow] no kind " + names[i]); continue; }
                var at = tr.position + right * 4.2f + fwd * (4f - i * 6.5f);
                float y = at.y;
                foreach (var h in Physics.RaycastAll(at + Vector3.up * 6, Vector3.down, 15, ~0, QueryTriggerInteraction.Ignore))
                    if (!h.rigidbody && (h.point.y > y - 3) && (y == at.y || h.point.y > y)) y = h.point.y;
                shown.Add(new Shown { kd = kd, paint = Mathf.Clamp(paint, 0, kd.paints.Length - 1), pos = new Vector3(at.x, y, at.z), rot = Quaternion.LookRotation(fwd) });
                Debug.Log($"[carshow] {kd.name} at {at}");
            }
            placed = true;
        }

        void Update()
        {
            if (!placed) { if (Time.frameCount > 5) Place(); return; }
            float clock = Time.time;
            foreach (var c in shown)
            {
                var M = Matrix4x4.TRS(c.pos, c.rot, Vector3.one);
                int lod = forceLod;
                if (lod < 0)
                {
                    float dist = Camera.main ? (Camera.main.transform.position - c.pos).magnitude : 0;
                    lod = c.kd.lod2 != null && c.kd.lod2.Length > 0 ? (dist < c.kd.lodNear ? 0 : dist < c.kd.lodMid ? 1 : 2) : 0;
                }
                var parts = lod == 2 && c.kd.lod2 != null && c.kd.lod2.Length > 0 ? c.kd.lod2 : lod >= 1 && c.kd.lod1 != null && c.kd.lod1.Length > 0 ? c.kd.lod1 : c.kd.lod0;
                float bph = (clock * 1.3f) % 1f;
                bool bA = bph < .1f || (bph > .18f && bph < .28f), bB = (bph > .5f && bph < .6f) || (bph > .68f && bph < .78f);
                foreach (var pt in parts)
                {
                    var mat = pt.paint ? c.kd.paints[c.paint] : pt.mat;
                    if (pt.lamp > 0 && pt.matOn && (pt.lamp == 4 ? ts.headlights : pt.lamp == 5 ? bA : pt.lamp == 6 ? bB : false)) mat = pt.matOn;
                    if (!mat || !pt.mesh) continue;
                    var rp = new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, layer = ts.gameObject.layer, worldBounds = new Bounds(c.pos, Vector3.one * 20) };
                    Graphics.RenderMesh(rp, pt.mesh, pt.sub, M * pt.local);
                }
            }
        }

        void LateUpdate()
        {
            if (!placed || views == null || shown.Count == 0 || !Camera.main) return;
            float t = Time.realtimeSinceStartup - t0; if (t < 0) return;
            int vi = Mathf.Min((int)(t / dt), views.Length - 1);
            var c = shown[0]; var cam = Camera.main.transform;
            var f = c.rot * Vector3.forward; var r = c.rot * Vector3.right; var u = Vector3.up; var ctr = c.pos + u * .75f;
            Vector3 eye; Vector3 look = ctr;
            switch (views[vi])
            {
                case "f34": eye = ctr + f * 4.2f + r * 3.0f + u * .55f; break;
                case "r34": eye = ctr - f * 4.2f + r * 3.0f + u * .7f; break;
                case "left": eye = ctr - r * 6.0f + u * .2f; break;
                case "right": eye = ctr + r * 6.0f + u * .2f; break;
                case "rear": eye = ctr - f * 6.0f + u * .5f; break;
                case "front": eye = ctr + f * 6.0f + u * .5f; break;
                case "row": look = ctr - f * 6.5f * (shown.Count - 1) * .5f; eye = look + r * (6f + 3.2f * shown.Count) + f * 2f + u * 1.2f; break;
                case "d40": eye = ctr - f * 34f + r * 9f + u * 3.5f; break;
                case "d60": eye = ctr - f * 55f + r * 12f + u * 4.5f; break;
                default: eye = ctr + f * 4.5f + r * 3f + u * .6f; break;
            }
            cam.position = eye; cam.rotation = Quaternion.LookRotation(look - eye);
        }
    }
}
