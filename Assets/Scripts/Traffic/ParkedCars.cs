// Parked cars (docs/audit-stari.md): static cars of the traffic kinds (TrafficData) in the car parks and along the village streets,
// placed by Magistrala/Rekviziti: postavi (Assets/Editor/PropsParked.cs). Drawn like the traffic (Graphics.RenderMeshInstanced):
// the kind's parts (lod0 / lod1 / lod2 by distance) only very near the camera, the merged far mesh (palette, paint, tail: 3 draws
// per kind) beyond nearDist, nothing beyond drawDist; every drawn car casts (one draw list per bucket), off-screen ones within shadowDist. Cars sit in 128 m cells, only the cells near the camera are
// walked. Colliders: static BoxColliders made by the builder (children of this object).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.Traffic
{
    [DefaultExecutionOrder(10001)]
    public class ParkedCars : MonoBehaviour
    {
        public TrafficData data;
        /// per car: position (Unity), yaw (deg) in w; kind index into data.kinds; paint index into the kind's paints
        public Vector4[] cars;
        public int[] kind, paint;
        public float nearDist = 12, drawDist = 260, shadowDist = 35;   // (full parts to 45 m: ~800 SetPass calls at the Stari riva lot); shadowDist: off-screen cars still drawn into the shadow map
        /// measuring only (-parked 0): not drawn
        public static bool Off;
        public int DrawnLastFrame { get; private set; }

        const float CELL = 128;
        readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        readonly List<(long key, Vector3 c)> cellList = new List<(long, Vector3)>();
        readonly List<(Mesh mesh, int sub, Material mat)> buckets = new List<(Mesh, int, Material)>();
        int[][][] kindParts; int[] farB;
        Matrix4x4[][] mats, matsSh; int[] matN, matShN;
        Matrix4x4[] M;
        Camera cam; readonly Plane[] planes = new Plane[6];

        void Start()
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++)
            {
                if (a[i] == "-parked" && a[i + 1] == "0") Off = true;
                if (a[i] == "-parkednear" && float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var nd)) nearDist = nd; // measuring: full-parts distance
            }
            if (!data || cars == null) return;
            M = new Matrix4x4[cars.Length];
            for (int i = 0; i < cars.Length; i++)
            {
                var p = (Vector3)cars[i];
                M[i] = Matrix4x4.TRS(p, Quaternion.Euler(0, cars[i].w, 0), Vector3.one);
                long k = ((long)Mathf.FloorToInt(p.x / CELL) << 32) ^ (uint)Mathf.FloorToInt(p.z / CELL);
                if (!cells.TryGetValue(k, out var l)) { cells[k] = l = new List<int>(); cellList.Add((k, new Vector3((Mathf.Floor(p.x / CELL) + .5f) * CELL, p.y, (Mathf.Floor(p.z / CELL) + .5f) * CELL))); }
                l.Add(i);
            }
            var index = new Dictionary<(Mesh, int, Material), int>();
            int B(Mesh m, int s, Material mt)
            {
                if (!index.TryGetValue((m, s, mt), out int i)) { i = buckets.Count; index[(m, s, mt)] = i; buckets.Add((m, s, mt)); if (mt) mt.enableInstancing = true; }
                return i;
            }
            kindParts = new int[data.kinds.Length][][]; farB = new int[data.kinds.Length];
            for (int k = 0; k < data.kinds.Length; k++)
            {
                var kd = data.kinds[k]; kindParts[k] = new int[3][];
                for (int lod = 0; lod < 3; lod++)
                {
                    var parts = Level(kd, lod); kindParts[k][lod] = new int[parts.Length];
                    for (int p = 0; p < parts.Length; p++)
                    {
                        var pt = parts[p];
                        if (pt.paint) { int first = -1; for (int c = 0; c < kd.paints.Length; c++) { int b = B(pt.mesh, pt.sub, kd.paints[c]); if (c == 0) first = b; } kindParts[k][lod][p] = first; }
                        else kindParts[k][lod][p] = B(pt.mesh, pt.sub, pt.mat);
                    }
                }
                farB[k] = -1;
                if (!kd.farMesh || kd.farPaints == null || kd.farPaints.Length != kd.paints.Length) continue;
                farB[k] = B(kd.farMesh, 0, data.farPalette);
                for (int c = 0; c < kd.farPaints.Length; c++) B(kd.farMesh, 1, kd.farPaints[c]);
                B(kd.farMesh, 2, kd.farTail);
            }
            mats = new Matrix4x4[buckets.Count][]; matsSh = new Matrix4x4[buckets.Count][]; matN = new int[buckets.Count]; matShN = new int[buckets.Count];
            for (int i = 0; i < buckets.Count; i++) { mats[i] = new Matrix4x4[32]; matsSh[i] = new Matrix4x4[32]; }
        }

        static TrafficPart[] Level(TrafficKind kd, int lod)
        {
            if (lod >= 2 && kd.lod2 != null && kd.lod2.Length > 0) return kd.lod2;
            if (lod >= 1 && kd.lod1 != null && kd.lod1.Length > 0) return kd.lod1;
            return kd.lod0;
        }

        void Add(int b, in Matrix4x4 m, bool sh)
        {
            if (sh) { if (matShN[b] == matsSh[b].Length) System.Array.Resize(ref matsSh[b], matsSh[b].Length * 2); matsSh[b][matShN[b]++] = m; }
            else { if (matN[b] == mats[b].Length) System.Array.Resize(ref mats[b], mats[b].Length * 2); mats[b][matN[b]++] = m; }
        }

        readonly List<(float dd, int i)> nearC = new List<(float, int)>();
        public int maxNear = 6;
        void Far(int i, in Matrix4x4 m, TrafficKind kd)
        {
            int f0 = farB[kind[i]];
            Add(f0, m, true); Add(f0 + 1 + paint[i], m, true); Add(f0 + 1 + kd.farPaints.Length, m, true);
        }

        void LateUpdate()
        {
            DrawnLastFrame = 0;
            if (Off || M == null || buckets.Count == 0) return;
            if (!cam) cam = Camera.main; if (!cam) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            var cp = cam.transform.position;
            for (int i = 0; i < matN.Length; i++) { matN[i] = 0; matShN[i] = 0; }
            float k = Mathf.Max(.3f, TreeRenderer.Distance), draw = drawDist * k, near = nearDist, sh = shadowDist;
            float draw2 = draw * draw, near2 = near * near, sh2 = sh * sh;
            foreach (var (key, c) in cellList)
            {
                float dx = c.x - cp.x, dz = c.z - cp.z;
                if (Mathf.Sqrt(dx * dx + dz * dz) - CELL * .71f > draw) continue;
                foreach (int i in cells[key])
                {
                    ref var m = ref M[i];
                    float ex = m.m03 - cp.x, ez = m.m23 - cp.z, dd = ex * ex + ez * ez;
                    if (dd > draw2) continue;
                    var kd = data.kinds[kind[i]];
                    bool vis = GeometryUtility.TestPlanesAABB(planes, new Bounds(new Vector3(m.m03, m.m13 + kd.h * .5f, m.m23), new Vector3(kd.len, kd.h, kd.len)));
                    bool near0 = dd < sh2;
                    if (!vis && !near0) continue;
                    // (one draw list per bucket, all casting: a second, shadow-less list doubled the draws / SetPass calls)
                    DrawnLastFrame++;
                    if (dd > near2 || farB[kind[i]] < 0) { if (farB[kind[i]] >= 0) Far(i, m, kd); else nearC.Add((dd, i)); continue; }
                    nearC.Add((dd, i));
                }
            }
            // full parts for the few nearest cars only (each part is an instanced draw of its own material; a car park beside the
            // truck put ~250 SetPass calls on the main thread), the rest of the near ones as the merged far mesh
            nearC.Sort((x, y) => x.dd.CompareTo(y.dd));
            for (int q = 0; q < nearC.Count; q++)
            {
                int i = nearC[q].i; var kd = data.kinds[kind[i]]; ref var m = ref M[i];
                if (q >= maxNear && farB[kind[i]] >= 0) { Far(i, m, kd); continue; }
                float d = Mathf.Sqrt(nearC[q].dd);
                bool three = kd.lod2 != null && kd.lod2.Length > 0;
                int lod = three ? (d < kd.lodNear ? 0 : d < kd.lodMid ? 1 : 2) : 0;
                var parts = Level(kd, lod); var ids = kindParts[kind[i]][lod];
                for (int p = 0; p < parts.Length; p++) Add(ids[p] + (parts[p].paint ? paint[i] : 0), m * parts[p].local, true);
            }
            nearC.Clear();
            var wb = new Bounds(cp, Vector3.one * (draw * 2 + 50));
            for (int b = 0; b < buckets.Count; b++)
            {
                var bk = buckets[b];
                for (int s = 0; s < matShN[b]; s += 500) Graphics.RenderMeshInstanced(new RenderParams(bk.mat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, worldBounds = wb, layer = gameObject.layer }, bk.mesh, bk.sub, matsSh[b], Mathf.Min(500, matShN[b] - s), s);
                for (int s = 0; s < matN[b]; s += 500) Graphics.RenderMeshInstanced(new RenderParams(bk.mat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = wb, layer = gameObject.layer }, bk.mesh, bk.sub, mats[b], Mathf.Min(500, matN[b] - s), s);
            }
        }
    }
}
