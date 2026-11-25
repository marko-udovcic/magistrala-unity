// AI traffic on the D8 and the old road (port of the core of the web game's client/src/world2/traffic/sim.ts, reduced to corridors):
//  - cars live on a corridor (one per direction, TrafficData from Magistrala/Build traffic) at arc length s with a lateral offset;
//    lane k's centre comes from the road models' lane axes;
//  - IDM car following against the nearest vehicle ahead whose body overlaps laterally (a car changing lanes occupies both lanes),
//    the player's truck (its collider footprint projected onto the corridor: it blocks every lane it straddles), red / amber lights
//    at the D8 stop lines of the models' signal plans; a hard clamp keeps a car from ever moving into its obstacle;
//  - lane changes: merge before a lane drop, pass a slow leader on the left, keep right (gap acceptance), ~2.6 s per change;
//  - spawn ring 320–600 m from the player outside the camera view (warm start fills 40–600 m once), despawn beyond 680 m;
//  - drawing: Graphics.RenderMeshInstanced per (kind, part, paint colour); beyond 70 m the merged far mesh (palette, paint, tail:
//    3 draws per kind); brake lights / indicators as lit material variants;
//  - physics: one kinematic Rigidbody + BoxCollider per car (pooled), moved with MovePosition, so the truck collides with them.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.Traffic
{
    [DefaultExecutionOrder(-50)]
    public class TrafficSystem : MonoBehaviour
    {
        public TrafficData data;
        [Range(0, 60)] public int targetCars = 34;
        public float spawnMin = 320, spawnMax = 600, despawnDist = 680;
        public bool signals = true;
        /// headlight lenses lit (follows LightPreset: sunset)
        [System.NonSerialized] public bool headlights;
        LightPreset preset;
        public float lod1Dist = 70, shadowDist = 120;
        /// far cars dissolve (Magistrala/FarFade dither) between these distances and are not drawn beyond: a car spawned in view
        /// (only ever beyond farFadeEnd) or despawned far away never pops — it fades in as it comes closer
        public float farFadeStart = 400, farFadeEnd = 470;
        public Transform player;
        public int seed = 7;

        // ---- stats (read by tests / HUD)
        public int CarCount => cars.Count;
        public float LastUpdateMs { get; private set; }
        public float AvgUpdateMs { get; private set; }
        public float AvgSimMs { get; private set; }
        public float AvgDrawMs { get; private set; }
        public int Spawned { get; private set; }
        public int Despawned { get; private set; }
        /// spawn attempts refused: 0 off the stretch, 1 in view, 2 spacing, 3 truck, 4 red light ahead
        public readonly int[] SpawnFails = new int[5];
        /// stop lines passed by the cars, by the light shown (0 green, 1 amber, 2 red — must stay 0)
        public readonly int[] StopPass = new int[3];

        public class Car
        {
            public int c, kind, paint, lane, laneTo;
            public float s, v, lat, v0, len, hw, h, a, b, T;
            public float decideT, laneT, stopT;
            public int indicator; // −1 left, +1 right (lane change)
            public float wheelAng;  // distance driven (m), for the wheel spin
            public bool braking;
            public bool beacon; public float beaconPh; // emergency lights flashing, phase offset
            public Vector3 pos; public Quaternion rot;
            public Rigidbody rb;
        }

        readonly List<Car> cars = new List<Car>();
        // (one list per corridor: the D8 both ways and, mag-11-19, the old road both ways — sized in OnEnable)
        List<Car>[] byCorridor = { new List<Car>(), new List<Car>() };
        readonly Stack<Rigidbody> pool = new Stack<Rigidbody>();
        System.Random rng;
        Camera cam;
        Plane[] planes = new Plane[6];
        bool warm;
        float clock;

        // player footprint
        Collider[] playerCols; Rigidbody playerRb;
        Vector3[] footLocal; // 4 corners in the player's local frame
        int[] hint = { -1, -1 };
        // per corridor: player's footprint projected (s range, lat range), valid flag, centre s, horizontal distance to the corridor
        float[] pS0 = new float[2], pS1 = new float[2], pL0 = new float[2], pL1 = new float[2], pSc = new float[2], pFar = new float[2];
        bool[] pOn = new bool[2];
        /// a corridor farther than this from the player (the old road while driving the D8, 500 m south) gets no new cars
        public float corridorReach = 300;
        float playerSpeed;

        // drawing
        Matrix4x4[][] mats; int[] matN; // per draw bucket
        Vector3[] bLo, bHi;  // per draw bucket: the instances' positions this frame (tight bounds: per-cascade / view culling)
        readonly List<(Mesh mesh, int sub, Material mat, int lod)> buckets = new List<(Mesh, int, Material, int)>();
        int[][][] kindParts; // [kind][lod][part] -> bucket, paint parts: bucket of paint 0, + paint index
        int[] farB;          // [kind] -> first bucket of the merged far mesh (palette, paints…, tail, tail lit), -1 none
        /// [kind] the far mesh's tail submesh is lamp-sized (0 < height ≤ 0.45 m): only then is it lit. Taller ones carry body
        /// panels from a mixed lod1 tail submesh (golf2 / yugo45 tailgate, sprinter, mazda, coach) — the far mesh has no paint
        /// mask, so those stay unlit rather than glow as a whole red rear (25. 11., lamp audit)
        bool[] farTailLit;

        void OnEnable()
        {
            rng = new System.Random(seed);
            if (data == null) { enabled = false; Debug.LogWarning("[traffic] no TrafficData (run Magistrala/Build traffic)"); return; }
            int nc = data.corridors?.Length ?? 0;
            if (byCorridor.Length != nc)
            {
                byCorridor = new List<Car>[nc]; for (int i = 0; i < nc; i++) byCorridor[i] = new List<Car>();
                hint = new int[nc]; for (int i = 0; i < nc; i++) hint[i] = -1;
                pS0 = new float[nc]; pS1 = new float[nc]; pL0 = new float[nc]; pL1 = new float[nc]; pSc = new float[nc]; pFar = new float[nc]; pOn = new bool[nc];
            }
            BuildBuckets();
        }

        // paint look (25. 11., "auti-izgled"): the builders gave every colour one plain coat (paint .62, coat .9, flake .08). Greys,
        // silvers, blacks and the darker blues / greens / reds become metallic (flake → metallic .21) and every coat a touch
        // glossier, so the scene's reflection probes / sky show in the panels. Material values only: no draw, variant or cost
        // change. Absolute values (idempotent: the same asset materials may be seen twice, by ParkedCars too).
        static bool tuned;
        static readonly int ID_PaintColor = Shader.PropertyToID("_PaintColor"), ID_PaintSmooth = Shader.PropertyToID("_PaintSmooth"),
            ID_CoatSmooth = Shader.PropertyToID("_CoatSmooth"), ID_Flake = Shader.PropertyToID("_Flake");
        public static void TunePaints(TrafficData data)
        {
            if (tuned || VehicleLook.Off || data == null || data.kinds == null) return;
            tuned = true;
            foreach (var kd in data.kinds)
            {
                if (kd.paints != null)
                    foreach (var m in kd.paints)
                    {
                        if (!m || !m.HasProperty(ID_Flake)) continue;
                        Color.RGBToHSV(m.GetColor(ID_PaintColor), out _, out float sat, out float val);
                        bool metallic = (sat < .2f && val < .8f) || (val < .45f);
                        m.SetFloat(ID_Flake, metallic ? .7f : .1f);
                        m.SetFloat(ID_PaintSmooth, metallic ? .7f : .66f);
                        m.SetFloat(ID_CoatSmooth, .95f);
                    }
                if (kd.parkedMat && kd.parkedMat.HasProperty(ID_CoatSmooth)) { kd.parkedMat.SetFloat(ID_CoatSmooth, .94f); kd.parkedMat.SetFloat(ID_PaintSmooth, .66f); }
            }
        }

        void BuildBuckets()
        {
            TunePaints(data);
            buckets.Clear();
            var index = new Dictionary<(Mesh, int, Material), int>();
            int B(Mesh m, int s, Material mt, int lod)
            {
                if (!index.TryGetValue((m, s, mt), out int i)) { i = buckets.Count; index[(m, s, mt)] = i; buckets.Add((m, s, mt, lod)); }
                return i;
            }
            kindParts = new int[data.kinds.Length][][];
            for (int k = 0; k < data.kinds.Length; k++)
            {
                var kd = data.kinds[k];
                kindParts[k] = new int[3][];
                for (int lod = 0; lod < 3; lod++)
                {
                    var parts = Level(kd, lod);
                    kindParts[k][lod] = new int[parts.Length];
                    for (int p = 0; p < parts.Length; p++)
                    {
                        var pt = parts[p];
                        if (pt.paint) { int first = -1; for (int c = 0; c < kd.paints.Length; c++) { int b = B(pt.mesh, pt.sub, kd.paints[c], lod); if (c == 0) first = b; } kindParts[k][lod][p] = first; }
                        else if (pt.lamp > 0 && pt.matOn) { kindParts[k][lod][p] = B(pt.mesh, pt.sub, pt.mat, lod); B(pt.mesh, pt.sub, pt.matOn, lod); }
                        else kindParts[k][lod][p] = B(pt.mesh, pt.sub, pt.mat, lod);
                    }
                }
            }
            farB = new int[data.kinds.Length]; farTailLit = new bool[data.kinds.Length];
            for (int k = 0; k < data.kinds.Length; k++)
            {
                var fm = data.kinds[k].farMesh;
                float h = fm && fm.subMeshCount > 2 ? fm.GetSubMesh(2).bounds.size.y : 0;
                farTailLit[k] = h > 0 && h <= .45f;
            }
            var fadeSh = Resources.Load<Shader>("Shaders/FarFade");
            var fade = new Dictionary<Material, Material>();
            Material F(Material m)
            {
                if (!fadeSh || !m) return m;
                if (!fade.TryGetValue(m, out var f))
                {
                    f = new Material(fadeSh) { name = m.name + " (fade)", enableInstancing = true };
                    f.CopyPropertiesFromMaterial(m);
                    if (m.IsKeywordEnabled("_EMISSION")) f.EnableKeyword("_EMISSION");
                    fade[m] = f;
                }
                return f;
            }
            for (int k = 0; k < data.kinds.Length; k++)
            {
                var kd = data.kinds[k]; farB[k] = -1;
                if (!kd.farMesh || kd.farPaints == null || kd.farPaints.Length != kd.paints.Length) continue;
                farB[k] = B(kd.farMesh, 0, F(data.farPalette), 1);
                for (int c = 0; c < kd.farPaints.Length; c++) B(kd.farMesh, 1, F(kd.farPaints[c]), 1);   // farB + 1 + colour
                B(kd.farMesh, 2, F(kd.farTail), 1); B(kd.farMesh, 2, F(kd.farTailOn), 1);                     // farB + 1 + colours (+1 lit)
            }
            mats = new Matrix4x4[buckets.Count][]; matN = new int[buckets.Count];
            for (int i = 0; i < buckets.Count; i++) mats[i] = new Matrix4x4[16];
            bLo = new Vector3[buckets.Count]; bHi = new Vector3[buckets.Count];
        }

        /// parts of level lod (0 / 1 / 2), falling back to the next finer level the kind has
        static TrafficPart[] Level(TrafficKind kd, int lod)
        {
            if (lod >= 2 && kd.lod2 != null && kd.lod2.Length > 0) return kd.lod2;
            if (lod >= 1 && kd.lod1 != null && kd.lod1.Length > 0) return kd.lod1;
            return kd.lod0;
        }

        void OnDisable()
        {
            foreach (var c in cars) Release(c);
            cars.Clear(); foreach (var l in byCorridor) l.Clear();
        }

        // ------------------------------------------------------------------ corridor geometry
        static float LaneOff(TrafficCorridor c, int i, int k)
        {
            int n = c.lanes[i]; if (k >= n) k = n - 1;
            return c.laneOff[i * c.maxLanes + k];
        }
        float LaneOffAt(TrafficCorridor c, float s, int k)
        {
            float f = Mathf.Clamp(s / c.step, 0, c.axis.Length - 1.001f); int i = (int)f; float t = f - i;
            float a = LaneOff(c, i, k), b = LaneOff(c, i + 1, k);
            return a + (b - a) * t;
        }
        static int LanesAt(TrafficCorridor c, float s) => c.lanes[Mathf.Clamp(Mathf.RoundToInt(s / c.step), 0, c.axis.Length - 1)];

        /// world point at (s, lateral) on the surface between the lane centres
        static Vector3 Point(TrafficCorridor c, float s, float lat)
        {
            float f = Mathf.Clamp(s / c.step, 0, c.axis.Length - 1.001f); int i = (int)f; float t = f - i;
            var ax = Vector3.LerpUnclamped(c.axis[i], c.axis[i + 1], t);
            var r = Vector3.LerpUnclamped(c.right[i], c.right[i + 1], t);
            float y = Mathf.LerpUnclamped(Y(c, i, lat), Y(c, i + 1, lat), t);
            var p = ax + r * lat; p.y = y; return p;
        }
        static float Y(TrafficCorridor c, int i, float lat)
        {
            int n = c.lanes[i], L = c.maxLanes;
            // lane offsets decrease with k (lane 0 rightmost = largest offset)
            float o0 = c.laneOff[i * L], on = c.laneOff[i * L + n - 1];
            if (n == 1 || lat >= o0) return c.laneY[i * L]; // right of the right lane's centre: its height
            if (lat <= on) return c.laneY[i * L + n - 1];
            for (int k = 0; k + 1 < n; k++)
            {
                float a = c.laneOff[i * L + k], b = c.laneOff[i * L + k + 1];
                if (lat <= a && lat >= b) return Mathf.Lerp(c.laneY[i * L + k], c.laneY[i * L + k + 1], (a - lat) / Mathf.Max(a - b, 1e-3f));
            }
            return c.axis[i].y;
        }

        /// nearest sample of corridor ci to world point p (local walk from the hint, global coarse search when lost)
        int Nearest(int ci, Vector3 p)
        {
            var c = data.corridors[ci]; int n = c.axis.Length; int h = hint[ci];
            float D(int i) { var d = c.axis[i] - p; return d.x * d.x + d.z * d.z; }
            if (h < 0 || D(h) > 400 * 400)
            {
                float bd = float.MaxValue; for (int i = 0; i < n; i += 10) { float d = D(i); if (d < bd) { bd = d; h = i; } }
            }
            for (int guard = 0; guard < 4000; guard++)
            {
                float d0 = D(h);
                if (h + 1 < n && D(h + 1) < d0) { h++; continue; }
                if (h > 0 && D(h - 1) < d0) { h--; continue; }
                break;
            }
            hint[ci] = h; return h;
        }

        void Project(int ci, Vector3 p, int i, out float s, out float lat)
        {
            var c = data.corridors[ci];
            var t = c.axis[Mathf.Min(i + 1, c.axis.Length - 1)] - c.axis[Mathf.Max(i - 1, 0)]; t.y = 0; t.Normalize();
            var d = p - c.axis[i];
            s = i * c.step + Vector3.Dot(d, t); lat = Vector3.Dot(d, c.right[i]);
        }

        // ------------------------------------------------------------------ player
        void FindPlayer()
        {
            // (the player's vehicle can change: MagistralaGame.Vehicles parks one and activates the other)
            if (player && !player.gameObject.activeInHierarchy) { player = null; playerCols = null; }
            if (!player)
            {
                var tc = FindFirstObjectByType<TruckController>();
                if (tc) player = tc.transform;
            }
            if (player && playerCols == null)
            {
                var cols = player.GetComponentsInChildren<Collider>();
                if (cols.Length == 0) return; // the truck builds its colliders in its own Start
                playerCols = cols; playerRb = player.GetComponent<Rigidbody>();
                // footprint: the colliders' bounds in the truck's local frame
                Vector3 mn = Vector3.one * 1e9f, mx = -Vector3.one * 1e9f;
                foreach (var col in cols)
                {
                    if (col.isTrigger) continue;
                    var b = col.bounds;
                    for (int k = 0; k < 8; k++)
                    {
                        var w = new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z);
                        var l = player.InverseTransformPoint(w); mn = Vector3.Min(mn, l); mx = Vector3.Max(mx, l);
                    }
                }
                // world AABB of rotated colliders overestimates: shrink the lateral a bit, keep length
                footLocal = new[] { new Vector3(mn.x, 0, mn.z), new Vector3(mx.x, 0, mn.z), new Vector3(mx.x, 0, mx.z), new Vector3(mn.x, 0, mx.z) };
            }
        }

        Vector3 PlayerPos => player ? player.position : (cam ? cam.transform.position : Vector3.zero);

        void ProjectPlayer()
        {
            playerSpeed = playerRb ? playerRb.linearVelocity.magnitude : 0;
            for (int ci = 0; ci < data.corridors.Length; ci++)
            {
                int i = Nearest(ci, PlayerPos);
                Project(ci, PlayerPos, i, out pSc[ci], out _);
                { var d = data.corridors[ci].axis[i] - PlayerPos; pFar[ci] = Mathf.Sqrt(d.x * d.x + d.z * d.z); }
                pOn[ci] = false;
                if (footLocal == null) continue;
                float s0 = 1e9f, s1 = -1e9f, l0 = 1e9f, l1 = -1e9f;
                foreach (var fl in footLocal)
                {
                    var w = player.TransformPoint(fl);
                    Project(ci, w, i, out float s, out float lat);
                    s0 = Mathf.Min(s0, s); s1 = Mathf.Max(s1, s); l0 = Mathf.Min(l0, lat); l1 = Mathf.Max(l1, lat);
                }
                pS0[ci] = s0; pS1[ci] = s1; pL0[ci] = l0; pL1[ci] = l1;
                pOn[ci] = l1 > -15 && l0 < 15 && Mathf.Abs(player.position.y - data.corridors[ci].axis[i].y) < 4;
            }
        }

        /// test runs (ProbeBench -trafdbg): per corridor the player is on, its footprint (s, lat) and the cars within 120 m (s, lane, lat, v km/h)
        public string DebugNear()
        {
            var sb = new System.Text.StringBuilder();
            if (data == null || pOn == null) return "";
            for (int c = 0; c < data.corridors.Length; c++)
            {
                if (!pOn[c]) continue;
                sb.Append($" c{c} truck s {pS0[c]:F0}..{pS1[c]:F0} lat {pL0[c]:F1}..{pL1[c]:F1}:");
                foreach (var o in byCorridor[c]) if (Mathf.Abs(o.s - pSc[c]) < 120) sb.Append($" [{o.s - pSc[c]:F0} L{o.lane}>{o.laneTo} lat {o.lat:F1} v {o.v * 3.6f:F0} stop {o.stopT:F0}]");
            }
            // the stop lines within 150 m of the truck on every corridor: light, cars standing within 40 m before it
            for (int c = 0; c < data.corridors.Length; c++)
            {
                var co = data.corridors[c]; if (co.stops == null || pFar[c] > 60) continue;
                foreach (var st in co.stops)
                {
                    if (Mathf.Abs(st.s - pSc[c]) > 150) continue;
                    int q = 0; float near = 1e9f;
                    foreach (var o in byCorridor[c]) { float d = st.s - (o.s + o.len / 2); if (d > -1 && d < 40 && o.v < .5f) q++; if (d > -1) near = Mathf.Min(near, d); }
                    sb.Append($" {{{co.name} {st.model} {st.group} {(LightAt(st) == 0 ? "zeleno" : LightAt(st) == 1 ? "žuto" : "crveno")} čeka {q} najbliži {(near < 1e8f ? near.ToString("F0") : "-")} m}}");
                }
            }
            sb.Append($" prolazi zel/žu/crv {StopPass[0]}/{StopPass[1]}/{StopPass[2]}");
            return sb.ToString();
        }

        /// for the autopilot: in the corridor the player drives along (heading fwd), the nearest car ahead whose body (or the lane it is
        /// moving to) overlaps the player's footprint laterally — gap from the player's front bumper, its speed — and the next stop
        /// line ahead (distance from the front, light 0 green / 1 amber / 2 red). false: not on a corridor.
        public bool PlayerAhead(Vector3 fwd, out float gap, out float lv, out float stopD, out int light)
        {
            gap = 1e9f; lv = 0; stopD = 1e9f; light = 0;
            if (data == null || data.corridors == null) return false;
            int ci = -1;
            for (int c = 0; c < data.corridors.Length; c++)
            {
                if (!pOn[c] || hint[c] < 0) continue;
                var cor = data.corridors[c]; int i = hint[c];
                var t = cor.axis[Mathf.Min(i + 1, cor.axis.Length - 1)] - cor.axis[Mathf.Max(i - 1, 0)]; t.y = 0;
                if (Vector3.Dot(t.normalized, fwd) > .7f) { ci = c; break; }
            }
            if (ci < 0) return false;
            var co = data.corridors[ci]; float front = pS1[ci];
            foreach (var o in byCorridor[ci])
            {
                float d = o.s - o.len / 2 - front; if (d < -1.5f || d > 250) continue;
                bool hit = o.lat + o.hw > pL0[ci] - .4f && o.lat - o.hw < pL1[ci] + .4f;
                if (!hit && o.laneTo != o.lane) { float lt = LaneOffAt(co, o.s, o.laneTo); hit = lt + o.hw > pL0[ci] - .4f && lt - o.hw < pL1[ci] + .4f; }
                if (hit && d < gap) { gap = Mathf.Max(0, d); lv = o.v; }
            }
            if (signals && co.stops != null)
                foreach (var st in co.stops) { float d = st.s - front; if (d < -1f) continue; stopD = d; light = (int)LightAt(st); break; }
            return true;
        }

        // ------------------------------------------------------------------ signals
        float LightAt(TrafficStop st) => Mathf.Min(2, Light(st.plan, st.group));
        /// 0 green, 1 amber, 2 red, 3 red + amber (just before green: the cars treat it as red)
        int Light(int plan, string group)
        {
            var p = data.plans[plan]; float u = (((clock + p.offset) % p.cycle) + p.cycle) % p.cycle;
            foreach (var ph in p.phases)
            {
                if (ph.group != group) continue;
                if (u >= ph.g0 && u < ph.g1) return 0;
                if (u >= ph.y0 && u < ph.y1) return 1;
                if (u >= ph.r0 && u < ph.r1) return 3;
            }
            return 2;
        }
        /// signal plan index of a junction model (TrafficData.plans key, e.g. "krizanje-01"), −1 = none
        public int PlanIndex(string key) { if (data?.plans != null) for (int i = 0; i < data.plans.Length; i++) if (data.plans[i].key == key) return i; return -1; }
        /// the state a signal head of group `group` of plan `plan` shows (0 green, 1 amber, 2 red, 3 red + amber) — the same clock
        /// the cars' stop lines use
        public int LightFor(int plan, string group) => data == null || plan < 0 || plan >= data.plans.Length ? 2 : Light(plan, group);
        public int LightState(int ci, int si) => (int)LightAt(data.corridors[ci].stops[si]);
        public string LightName(int ci, int si) => LightAt(data.corridors[ci].stops[si]) switch { 0 => "green", 1 => "amber", _ => "red" };

        // ------------------------------------------------------------------ simulation
        static float Idm(Car car, float v0, float gap, float dv)
        {
            float sStar = 2f + Mathf.Max(0, car.v * car.T + car.v * dv / (2 * Mathf.Sqrt(car.a * car.b)));
            float vr = car.v / Mathf.Max(v0, .1f);
            return car.a * (1 - vr * vr * vr * vr - (sStar / Mathf.Max(gap, .1f)) * (sStar / Mathf.Max(gap, .1f)));
        }

        static bool Overlap(float a, float ahw, float b, float bhw, float margin = .25f) => Mathf.Abs(a - b) < ahw + bhw + margin;

        /// nearest obstacle ahead of a car body at lateral lat (± hw): gap (bumper to bumper) and its speed
        void Leader(Car car, List<Car> list, int idx, float lat, float hw, float range, out float gap, out float lv)
        {
            gap = 1e9f; lv = 0;
            for (int j = idx + 1; j < list.Count; j++)
            {
                var o = list[j]; float d = o.s - car.s - (o.len + car.len) / 2;
                if (d > range) break;
                if (o.laneTo != o.lane ? (Overlap(lat, hw, o.lat, o.hw) || Overlap(lat, hw, LaneOffAt(data.corridors[car.c], o.s, o.laneTo), o.hw)) : Overlap(lat, hw, o.lat, o.hw))
                { if (d < gap) { gap = d; lv = o.v; } break; }
            }
            // cars level with us (sorted ties): any body overlapping in s and lateral is a zero gap
            if (pOn[car.c])
            {
                if (pL1[car.c] > lat - hw - .3f && pL0[car.c] < lat + hw + .3f && pS1[car.c] > car.s - car.len / 2)
                {
                    float d = pS0[car.c] - car.s - car.len / 2;
                    if (d < gap) { gap = Mathf.Max(d, 0); lv = d < 0 ? 0 : playerSpeed; }
                }
            }
        }

        /// nearest car behind at lateral lat: gap and its speed
        void Follower(Car car, List<Car> list, int idx, float lat, float hw, out float gap, out float fv)
        {
            gap = 1e9f; fv = 0;
            for (int j = idx - 1; j >= 0; j--)
            {
                var o = list[j]; float d = car.s - o.s - (o.len + car.len) / 2;
                if (d > 120) break;
                if (Overlap(lat, hw, o.lat, o.hw) || (o.laneTo != o.lane && Overlap(lat, hw, LaneOffAt(data.corridors[car.c], o.s, o.laneTo), o.hw))) { gap = d; fv = o.v; break; }
            }
            if (pOn[car.c] && pL1[car.c] > lat - hw - .3f && pL0[car.c] < lat + hw + .3f && pS1[car.c] < car.s + car.len / 2)
            {
                float d = car.s - car.len / 2 - pS1[car.c];
                if (d < gap) { gap = Mathf.Max(d, 0); fv = playerSpeed; }
            }
        }

        void Step(float dt)
        {
            for (int ci = 0; ci < byCorridor.Length; ci++)
            {
                var list = byCorridor[ci]; var cor = data.corridors[ci];
                // insertion sort by s (nearly sorted)
                for (int i = 1; i < list.Count; i++) { var x = list[i]; int j = i - 1; while (j >= 0 && list[j].s > x.s) { list[j + 1] = list[j]; j--; } list[j + 1] = x; }
                for (int i = 0; i < list.Count; i++)
                {
                    var car = list[i];
                    float vmax = Mathf.Min(cor.vmax[Mathf.Clamp((int)((car.s + car.v * 1.5f) / cor.step), 0, cor.axis.Length - 1)], cor.vmax[Mathf.Clamp((int)(car.s / cor.step), 0, cor.axis.Length - 1)]);
                    float v0 = Mathf.Min(car.v0, vmax);
                    // occupy the lane we're in and the one we're moving to
                    float hw = car.hw;
                    Leader(car, list, i, car.lat, hw, 160, out float gap, out float lv);
                    if (car.laneTo != car.lane)
                    {
                        Leader(car, list, i, LaneOffAt(cor, car.s, car.laneTo), hw, 160, out float g2, out float lv2);
                        if (g2 < gap) { gap = g2; lv = lv2; }
                    }
                    // red / amber at the stop line ahead
                    if (signals && cor.stops != null)
                        foreach (var st in cor.stops)
                        {
                            float d = st.s - (car.s + car.len / 2) - 1.0f;
                            if (d < -.5f) continue;
                            if (d > 150) break;
                            float l = LightAt(st);
                            // amber: stop when it can comfortably, or when it could not clear the line before red (≤ 2.5 s left) — no dilemma zone
                            bool stop = l == 2 ? car.v * car.v / (2 * 7f) < d + 1.5f || car.v < 3 : l == 1 && (car.v * car.v / (2 * 3f) < d || d > car.v * 2.5f);
                            if (stop && d < gap) { gap = Mathf.Max(d, 0); lv = 0; }
                            break;
                        }
                    // a closure at the corridor's end: a standing obstacle 1 m before it (the cars queue at the barrier)
                    if (cor.deadEnd) { float d = cor.Length - 1 - (car.s + car.len / 2); if (d < gap) { gap = Mathf.Max(d, 0); lv = 0; } }
                    float acc = gap > 500 ? car.a * (1 - Mathf.Pow(car.v / Mathf.Max(v0, .1f), 4)) : Idm(car, v0, gap, car.v - lv);
                    acc = Mathf.Clamp(acc, -9f, car.a);
                    car.braking = acc < -1.2f || (car.v < .5f && gap < 6);
                    float nv = Mathf.Max(0, car.v + acc * dt);
                    float ds = (car.v + nv) * .5f * dt;
                    // hard clamp: never into the obstacle
                    if (ds > gap - .3f) { ds = Mathf.Max(0, gap - .3f); nv = Mathf.Min(nv, ds / dt); }
                    if (signals && cor.stops != null && ds > 0)
                    {
                        // a stop line passed this step, by the light it showed (tests: a car never crosses on red)
                        float f0 = car.s + car.len / 2 + 1.0f - .5f, f1 = f0 + ds;
                        foreach (var st in cor.stops)
                            if (st.s > f0 && st.s <= f1)
                            {
                                int l = (int)LightAt(st); StopPass[l]++;
                                if (l == 2) Debug.LogWarning($"[traffic] auto prošao kroz crveno: {cor.name} {st.model} {st.group} s {st.s:F0} v {car.v * 3.6f:F0} km/h");
                            }
                    }
                    car.v = nv; car.s += ds; car.wheelAng += ds;
                    car.stopT = car.v < .3f ? car.stopT + dt : 0;
                    // lane keeping / changing
                    float target = LaneOffAt(cor, car.s, car.laneTo);
                    car.indicator = car.laneTo > car.lane ? -1 : car.laneTo < car.lane ? 1 : 0;
                    if (car.laneTo != car.lane)
                    {
                        car.laneT += dt;
                        // indicator ~1 s before the car moves over (the follower in the target lane already sees it as a leader)
                        float rate = car.laneT > 1f ? Mathf.Min(1.45f, .6f + car.v * .1f) * dt : 0;
                        car.lat = Mathf.MoveTowards(car.lat, target, rate);
                        if (Mathf.Abs(car.lat - target) < .05f) { car.lane = car.laneTo; car.laneT = 0; }
                    }
                    else car.lat = Mathf.MoveTowards(car.lat, target, Mathf.Max(.8f * dt, Mathf.Abs(car.lat - target) < .3f ? 1 : 0));
                    // decisions
                    car.decideT -= dt;
                    if (car.decideT <= 0 && car.laneTo == car.lane)
                    {
                        car.decideT = .6f + (float)rng.NextDouble() * .6f;
                        Decide(car, list, i, cor, gap, lv, v0);
                    }
                }
            }
        }

        bool GapOk(Car car, List<Car> list, int idx, float lat, float minAhead)
        {
            Leader(car, list, idx, lat, car.hw, 160, out float ga, out float va);
            Follower(car, list, idx, lat, car.hw, out float gb, out float vb);
            float need = 5 + Mathf.Max(0, vb - car.v) * 2.2f + vb * .45f;
            return ga > Mathf.Max(minAhead, 6 + Mathf.Max(0, car.v - va) * 2f) && gb > need;
        }

        void Decide(Car car, List<Car> list, int idx, TrafficCorridor cor, float gap, float lv, float v0)
        {
            int nHere = LanesAt(cor, car.s);
            // lane drop ahead (lanes vanish on the left): merge right
            int nAhead = Mathf.Min(LanesAt(cor, car.s + 60), Mathf.Min(LanesAt(cor, car.s + 120), LanesAt(cor, car.s + 25)));
            if (car.lane >= nAhead && car.lane > 0)
            {
                float lat = LaneOffAt(cor, car.s, car.lane - 1);
                bool urgent = car.lane >= LanesAt(cor, car.s + 25);
                if (GapOk(car, list, idx, lat, urgent ? 3 : 8)) car.laneTo = car.lane - 1;
                return;
            }
            if (car.lane >= nHere) { car.lane = car.laneTo = nHere - 1; return; }
            // standing: only to get round a standing obstacle (the truck, a car that won't move) when the next lane is clearly emptier
            // — in a queue at a red light the next lane is queued too, so nobody hops
            if (car.v < 4)
            {
                if (car.stopT < 3 || gap > 20 || lv > .5f) return;
                for (int side = 1; side >= -1; side -= 2)
                {
                    int to = car.lane + side; if (to < 0 || to >= nHere || to >= nAhead) continue;
                    float lat = LaneOffAt(cor, car.s, to);
                    Leader(car, list, idx, lat, car.hw, 160, out float ga, out _);
                    if (ga > gap + 25 && GapOk(car, list, idx, lat, 8)) { car.laneTo = to; return; }
                }
                return;
            }
            // pass a slow leader on the left
            if (car.lane + 1 < nHere && car.lane + 1 < nAhead && gap < 45 && lv < v0 - 2.5f)
            {
                float lat = LaneOffAt(cor, car.s, car.lane + 1);
                Leader(car, list, idx, lat, car.hw, 160, out float ga, out _);
                if (ga > gap + 15 && GapOk(car, list, idx, lat, 15)) { car.laneTo = car.lane + 1; return; }
            }
            // keep right
            if (car.lane > 0 && rng.NextDouble() < .5)
            {
                float lat = LaneOffAt(cor, car.s, car.lane - 1);
                Leader(car, list, idx, lat, car.hw, 160, out float ga, out float va);
                if (ga > Mathf.Max(60, car.v * 4) && va >= car.v - 1 || ga > 150)
                    if (GapOk(car, list, idx, lat, 40)) car.laneTo = car.lane - 1;
            }
        }

        // ------------------------------------------------------------------ spawn / despawn
        bool InView(Vector3 p)
        {
            if (!cam) return false;
            var b = new Bounds(p + Vector3.up, new Vector3(5, 3, 5));
            return GeometryUtility.TestPlanesAABB(planes, b);
        }

        /// spawn weights per kind: the data's, or overridden from the command line for measuring (-kindw zastava=0,golub=40;
        /// -kindw only=zastava: nothing else)
        float[] kindW;
        float[] KindW()
        {
            if (kindW != null && kindW.Length == data.kinds.Length) return kindW;
            kindW = System.Array.ConvertAll(data.kinds, k => k.weight);
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-kindw")
                foreach (var kv in a[i + 1].Split(','))
                {
                    var p = kv.Split('='); if (p.Length != 2) continue;
                    if (p[0] == "only") { for (int k = 0; k < kindW.Length; k++) if (data.kinds[k].name != p[1]) kindW[k] = 0; }
                    else if (float.TryParse(p[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w))
                        for (int k = 0; k < kindW.Length; k++) if (data.kinds[k].name == p[0]) kindW[k] = w;
                    Debug.Log("[traffic] -kindw " + kv);
                }
            return kindW;
        }

        int PickKind()
        {
            var W = KindW();
            float tot = 0; foreach (var w in W) tot += w;
            float x = (float)rng.NextDouble() * tot;
            for (int i = 0; i < W.Length; i++) { x -= W[i]; if (W[i] > 0 && x <= 0) return i; }
            return 0;
        }

        bool TrySpawn(bool warmStart)
        {
            // the emptiest quarter (corridor × ahead / behind the player) gets the next car, so every direction stays populated
            int ci = rng.Next(data.corridors.Length); int side = rng.NextDouble() < .5 ? 1 : -1;
            // (warm start: a random corridor near the player; never one out of reach)
            for (int t = 0; t < 8 && pFar[ci] > corridorReach; t++) ci = rng.Next(data.corridors.Length);
            if (pFar[ci] > corridorReach) { SpawnFails[0]++; return false; }
            if (!warmStart)
            {
                int best = int.MaxValue;
                for (int q = 0; q < data.corridors.Length * 2; q++)
                {
                    int qc = q >> 1, qs = (q & 1) == 0 ? 1 : -1, cnt = 0;
                    if (pFar[qc] > corridorReach) continue;
                    float far = pSc[qc] + qs * spawnMax; if (far < 30 || far > data.corridors[qc].Length - 30) continue; // stretch ends
                    foreach (var o in byCorridor[qc]) if ((o.s - pSc[qc]) * qs > 0) cnt++;
                    if (cnt < best || (cnt == best && rng.NextDouble() < .5)) { best = cnt; ci = qc; side = qs; }
                }
            }
            var cor = data.corridors[ci];
            float sp = pSc[ci];
            float d = warmStart ? 40 + (float)rng.NextDouble() * (spawnMax - 40) : spawnMin + (float)rng.NextDouble() * (spawnMax - spawnMin);
            float s = sp + side * d;
            if (s < 30 || s > cor.Length - (cor.deadEnd ? 300 : 30)) { SpawnFails[0]++; return false; }
            int n = LanesAt(cor, s);
            // prefer the right lane
            int lane = n <= 1 ? 0 : rng.NextDouble() < .62 ? 0 : 1 + rng.Next(n - 1);
            // not in a lane that ends soon
            if (lane >= Mathf.Min(LanesAt(cor, s + 100), LanesAt(cor, s + 40))) lane = 0;
            var kind = PickKind(); var kd = data.kinds[kind];
            float lat = LaneOffAt(cor, s, lane);
            var pos = Point(cor, s, lat);
            // never pop in view: out of the frustum, hidden behind terrain / buildings, or beyond farFadeEnd (not drawn there:
            // it dissolves in between farFadeEnd and farFadeStart as it comes closer)
            if (!warmStart && InView(pos))
            {
                var eye = cam.transform.position; var tgt = pos + Vector3.up * 1.2f;
                bool hidden = Physics.Linecast(eye, tgt, out var hit, ~0, QueryTriggerInteraction.Ignore) && (hit.point - tgt).magnitude > 6 && !hit.rigidbody;
                if (!hidden && (pos - eye).magnitude < farFadeEnd + 10) { SpawnFails[1]++; return false; }
            }
            // spacing
            foreach (var o in byCorridor[ci]) if (Mathf.Abs(o.s - s) < 30 + (o.len + kd.len) / 2 && Overlap(lat, kd.halfW, o.lat, o.hw, 1)) { SpawnFails[2]++; return false; }
            // the player's truck
            if (pOn[ci] && pS1[ci] > s - 40 && pS0[ci] < s + 40) { SpawnFails[3]++; return false; }
            // not right before a red light
            if (cor.stops != null) foreach (var st in cor.stops) if (st.s - s > 0 && st.s - s < 90 && LightAt(st) != 0) { SpawnFails[4]++; return false; }
            var car = new Car
            {
                c = ci, kind = kind, lane = lane, laneTo = lane, s = s, lat = lat, len = kd.len, hw = kd.halfW, h = kd.h, a = kd.a, b = kd.b, T = kd.T,
                v0 = 70 / 3.6f * kd.vMul * (.92f + (float)rng.NextDouble() * .2f), decideT = (float)rng.NextDouble(),
            };
            car.v = Mathf.Min(car.v0, cor.vmax[Mathf.Clamp((int)(s / cor.step), 0, cor.axis.Length - 1)]) * .9f;
            // match a close leader
            foreach (var o in byCorridor[ci]) if (o.s > s && o.s - s < 80 && Overlap(lat, kd.halfW, o.lat, o.hw)) car.v = Mathf.Min(car.v, o.v);
            float tot = 0; foreach (var w in kd.paintW) tot += w; float x = (float)rng.NextDouble() * tot;
            for (int i = 0; i < kd.paintW.Length; i++) { x -= kd.paintW[i]; if (x <= 0) { car.paint = i; break; } }
            car.beacon = kd.beaconShare > 0 && rng.NextDouble() < kd.beaconShare; car.beaconPh = (float)rng.NextDouble();
            car.rb = Acquire(kd);
            Pose(car);
            car.rb.position = car.pos; car.rb.rotation = car.rot; car.rb.transform.SetPositionAndRotation(car.pos, car.rot);
            cars.Add(car); byCorridor[ci].Add(car); Spawned++;
            if (HitchDiag.On && !warmStart) HitchDiag.Note($"spawn {kd.name} {(pos - (cam ? cam.transform.position : PlayerPos)).magnitude:F0}m{(InView(pos) ? " view" : "")}");
            return true;
        }

        Rigidbody Acquire(TrafficKind kd)
        {
            Rigidbody rb;
            if (pool.Count > 0) rb = pool.Pop();
            else
            {
                var go = new GameObject("TrafficCar");
                go.transform.SetParent(transform, false);
                rb = go.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.interpolation = RigidbodyInterpolation.None;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                go.AddComponent<BoxCollider>();
            }
            var bc = rb.GetComponent<BoxCollider>();
            bc.size = new Vector3(kd.halfW * 2, kd.h * .92f, kd.len * .97f); bc.center = new Vector3(0, kd.h * .92f / 2 + .12f, 0);
            rb.gameObject.name = "TrafficCar " + kd.name;
            rb.gameObject.SetActive(true);
            return rb;
        }

        void Release(Car c)
        {
            if (c.rb) { c.rb.gameObject.SetActive(false); pool.Push(c.rb); c.rb = null; }
        }

        void Despawn()
        {
            for (int i = cars.Count - 1; i >= 0; i--)
            {
                var c = cars[i]; var cor = data.corridors[c.c];
                float d = Mathf.Abs(c.s - pSc[c.c]); d = Mathf.Sqrt(d * d + pFar[c.c] * pFar[c.c]); // (a corridor away from the player: real distance)
                bool gone = d > despawnDist || c.s < 2 || (!cor.deadEnd && c.s > cor.Length - 8);
                // a closed end: recycled only where nobody sees it — at the barrier, or already on the last 150 m when unseen and
                // not right by the player (the queue never grows in front of a camera that turns away)
                if (!gone && cor.deadEnd && c.s > cor.Length - 150 && !InView(c.pos) && (c.pos - PlayerPos).magnitude > 60) gone = true;
                if (!gone && cor.deadEnd && c.s > cor.Length - 8 && (!InView(c.pos) || (c.pos - (cam ? cam.transform.position : PlayerPos)).magnitude > 350)) gone = true;
                // stuck far away (a jam that never clears behind a parked truck): recycle
                if (!gone && c.stopT > 60 && (c.pos - PlayerPos).magnitude > 150 && !InView(c.pos)) gone = true;
                // boxed in for good behind a standing truck (it blocks every lane it can go round by): recycle when unseen
                if (!gone && c.stopT > 45 && !InView(c.pos) && (c.pos - PlayerPos).magnitude > 25) gone = true;
                if (!gone) continue;
                if (HitchDiag.On) HitchDiag.Note($"despawn {d:F0}m{(InView(c.pos) ? " view" : "")}");
                Release(c); cars.RemoveAt(i); byCorridor[c.c].Remove(c); Despawned++;
            }
        }

        // ------------------------------------------------------------------ pose + drawing
        void Pose(Car car)
        {
            var cor = data.corridors[car.c];
            // body = chord between points 0.3·len ahead and behind (rear tracks inside a curve like a real axle)
            var f = Point(cor, car.s + car.len * .3f, car.lat);
            var r = Point(cor, car.s - car.len * .3f, car.lat);
            var fwd = f - r;
            car.pos = (f + r) * .5f;
            car.rot = fwd.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(fwd, Vector3.up) : Quaternion.identity;
            // a lane change yaws the body a little
            if (car.laneTo != car.lane && car.laneT > 1f)
            {
                float rem = LaneOffAt(cor, car.s, car.laneTo) - car.lat;
                float rate = Mathf.Min(1.45f, .6f + car.v * .1f) * Mathf.Min(1, Mathf.Abs(rem) / 1.2f);
                car.rot = car.rot * Quaternion.Euler(0, Mathf.Sign(rem) * Mathf.Rad2Deg * Mathf.Atan2(rate, Mathf.Max(car.v, 2f)), 0);
            }
        }

        static readonly int ID_FarFade = Shader.PropertyToID("_FarFade"), ID_FarFadeEye = Shader.PropertyToID("_FarFadeEye");

        void Add(int b, Matrix4x4 m)
        {
            if (matN[b] == mats[b].Length) System.Array.Resize(ref mats[b], mats[b].Length * 2);
            var p = new Vector3(m.m03, m.m13, m.m23);
            if (matN[b] == 0) { bLo[b] = p; bHi[b] = p; } else { bLo[b] = Vector3.Min(bLo[b], p); bHi[b] = Vector3.Max(bHi[b], p); }
            mats[b][matN[b]++] = m;
        }

        void Draw()
        {
            for (int i = 0; i < matN.Length; i++) matN[i] = 0;
            var cp = cam ? cam.transform.position : PlayerPos;
            Shader.SetGlobalVector(ID_FarFade, new Vector4(farFadeStart, farFadeEnd, 0, 0));
            Shader.SetGlobalVector(ID_FarFadeEye, cp);
            bool blink = (clock * 1.5f) % 1f < .5f;
            foreach (var car in cars)
            {
                var kd = data.kinds[car.kind];
                float dist = (car.pos - cp).magnitude;
                if (dist > farFadeEnd) continue; // dissolved (FarFade): not drawn
                if (dist > 30 && !GeometryUtility.TestPlanesAABB(planes, new Bounds(car.pos + Vector3.up * car.h * .5f, new Vector3(car.len, car.h, car.len))) && dist > shadowDist) continue;
                int lod = dist > lod1Dist ? 1 : 0;
                bool three = kd.lod2 != null && kd.lod2.Length > 0;
                if (lod == 1 && farB[car.kind] >= 0)
                {
                    var MF = Matrix4x4.TRS(car.pos, car.rot, Vector3.one);
                    int f0 = farB[car.kind], np = kd.farPaints.Length;
                    Add(f0, MF); Add(f0 + 1 + car.paint, MF); Add(f0 + 1 + np + ((car.braking || headlights) && farTailLit[car.kind] ? 1 : 0), MF); // (tail lamps lit at night too: the lit variant is all there is)
                    continue;
                }
                if (three && lod == 0) lod = dist < kd.lodNear ? 0 : dist < kd.lodMid ? 1 : 2;
                var parts = Level(kd, lod);
                var M = Matrix4x4.TRS(car.pos, car.rot, Vector3.one);
                // beacons: A and B alternate, a double flash each (~1.3 Hz)
                float bph = (clock * 1.3f + car.beaconPh) % 1f;
                bool bA = car.beacon && (bph < .1f || (bph > .18f && bph < .28f)), bB = car.beacon && ((bph > .5f && bph < .6f) || (bph > .68f && bph < .78f));
                var ids = kindParts[car.kind][lod];
                for (int p = 0; p < parts.Length; p++)
                {
                    var pt = parts[p];
                    int b = ids[p] + (pt.paint ? car.paint : 0);
                    // tail lamps (1) burn with the headlights at dusk / night as well as when braking (one lit variant)
                    if (pt.lamp > 0 && pt.matOn && (pt.lamp == 4 ? headlights : pt.lamp == 1 ? car.braking || headlights : pt.lamp == 5 ? bA : pt.lamp == 6 ? bB
                        : blink && pt.lamp == (car.indicator < 0 ? 2 : car.indicator > 0 ? 3 : 0))) b++;
                    if (pt.wheel)
                    {
                        // spin about the wheel's own x axis; mirrored (left) wheels turn the other way round their flipped axis
                        float sgn = Mathf.Sign(pt.local.m00);
                        Add(b, M * pt.local * Matrix4x4.Rotate(Quaternion.Euler(sgn * car.wheelAng / pt.radius * Mathf.Rad2Deg, 0, 0)));
                    }
                    else Add(b, M * pt.local);
                }
            }
            for (int b = 0; b < buckets.Count; b++)
            {
                if (matN[b] == 0) continue;
                var bk = buckets[b];
                var rp = new RenderParams(bk.mat)
                {
                    // far cars are merged meshes (3 draws per kind): their shadows are cheap, keep them (no shadow pop at the LOD switch)
                    shadowCastingMode = ShadowCastingMode.On, receiveShadows = true,
                    // bounds of this bucket's instances (+ a coach's half length / height), not 2 km round the player: with ~230
                    // buckets of 1–2 cars each, the 2 km box put every bucket into the view and all 4 shadow cascades (22. 11.)
                    worldBounds = new Bounds((bLo[b] + bHi[b]) * .5f + Vector3.up * 2, bHi[b] - bLo[b] + new Vector3(16, 10, 16)), layer = gameObject.layer,
                };
                Graphics.RenderMeshInstanced(rp, bk.mesh, bk.sub, mats[b], matN[b]);
            }
        }

        void Update()
        {
            if (data == null) return;
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            float dt = Mathf.Min(Time.deltaTime, .05f);
            clock += Time.deltaTime;
            if (!preset) preset = FindFirstObjectByType<LightPreset>();
            headlights = (preset && preset.Golden) || WeatherSystem.Raining; // low-beam lenses lit at sunset (G) and in rain (V)
            if (!cam) cam = Camera.main;
            if (cam) GeometryUtility.CalculateFrustumPlanes(cam, planes);
            FindPlayer();
            ProjectPlayer();
            if (dt > 0) Step(dt);
            Despawn();
            if (!warm && Time.frameCount > 3) { for (int k = 0; k < 400 && cars.Count < targetCars; k++) TrySpawn(true); warm = true; }
            else for (int k = 0; k < 4 && cars.Count < targetCars; k++) if (TrySpawn(false)) break;
            foreach (var c in cars) { Pose(c); c.rb.MovePosition(c.pos); c.rb.MoveRotation(c.rot); }
            var t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            Draw();
            var t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            float simMs = (t1 - t0) * 1000f / System.Diagnostics.Stopwatch.Frequency, drawMs = (t2 - t1) * 1000f / System.Diagnostics.Stopwatch.Frequency;
            AvgSimMs = AvgSimMs <= 0 ? simMs : AvgSimMs * .97f + simMs * .03f; AvgDrawMs = AvgDrawMs <= 0 ? drawMs : AvgDrawMs * .97f + drawMs * .03f;
            float ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000f / System.Diagnostics.Stopwatch.Frequency;
            LastUpdateMs = ms; TrafficDiag.LastMs = ms; AvgUpdateMs = AvgUpdateMs <= 0 ? ms : AvgUpdateMs * .97f + ms * .03f;
        }

        /// teleports / big jumps of the player: start over
        public void Clear() { OnDisable(); warm = false; for (int i = 0; i < hint.Length; i++) hint[i] = -1; }

        public IReadOnlyList<Car> Cars => cars;
    }
}
