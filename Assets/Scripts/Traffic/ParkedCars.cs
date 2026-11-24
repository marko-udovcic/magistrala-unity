// Parked cars (docs/audit-stari.md): static cars of the traffic kinds (TrafficData) in the car parks and along the village streets,
// placed by Magistrala/Rekviziti: postavi (Assets/Editor/PropsParked.cs). Cars sit in 128 m cells, only the cells near the camera
// are walked; nothing beyond drawDist. Colliders: static BoxColliders made by the builder (children of this object).
// Baked kinds (TrafficKind.parked, Assets/Editor/ParkedBake.cs): the kind merged into one mesh per level on one texture atlas
// (Magistrala/ParkedCar), the paint colour and plate row per instance (instancing buffer _ParkTint) — one instanced draw per kind
// and level whatever the colours: level 0 (lod1) below lod0Dist, level 1 (lod2) below lod1Dist, level 2 (the far mesh) beyond.
// Shadows: a separate shadow-only draw per kind (level 1 mesh within lod1Dist, the far mesh beyond), the visible draws cast none;
// off-screen cars within shadowDist are drawn into the shadow map only.
// Kinds without the bake (and -parkedlegacy, for measuring): the merged far mesh (palette, paint, tail: 3 draws per kind and colour).
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
        public float drawDist = 260, shadowDist = 35;   // shadowDist: off-screen cars still drawn into the shadow map
        /// baked kinds: lod1 mesh below lod0Dist, lod2 mesh below lod1Dist, the far mesh beyond (all one atlas material per kind)
        public float lod0Dist = 45, lod1Dist = 90;   // 45 = the traffic's lodMid (lod2 of some kinds has shards)
        /// measuring only: -parked 0 = not drawn, -parkedlegacy = the old far mesh path for every kind
        public static bool Off, Legacy;
        /// editor comparison shots only: every baked car on this level (0..2), -1 = by distance
        public static int ForceLevel = -1;
        public int DrawnLastFrame { get; private set; }
        /// RenderMeshInstanced calls of the last frame: visible (main) and shadow-only; distinct materials (≈ SetPass per pass)
        public static int CallsMain, CallsShadow, Materials;

        const float CELL = 128;
        // instances per call: within every platform's instancing constant buffer (GLES / WebGL 16 KB, 2 matrices + 1 float4)
        const int BATCH = 125;
        readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        readonly List<(long key, Vector3 c)> cellList = new List<(long, Vector3)>();
        // legacy far mesh buckets (mesh, submesh, material)
        readonly List<(Mesh mesh, int sub, Material mat)> buckets = new List<(Mesh, int, Material)>();
        int[] farB; Matrix4x4[][] mats; int[] matN;
        // baked: per kind × level (0..2) the visible list (+ tints), per kind × 2 the shadow-only list (level 1 mesh / far mesh)
        bool[] baked; Matrix4x4[][] pm, ps; Vector4[][] pt; int[] pmN, psN;
        /// per baked kind: a copy of parkedMat with _FARFADE for the far level (the car dissolves over the last ~45 m before the
        /// draw distance instead of popping in / out of view there)
        Material[] fadeMat;
        static readonly int ID_ParkFade = Shader.PropertyToID("_ParkFade"), ID_FarFadeEye = Shader.PropertyToID("_FarFadeEye");
        readonly Vector4[] tintTmp = new Vector4[BATCH];
        readonly List<MaterialPropertyBlock> mpb = new List<MaterialPropertyBlock>();
        Matrix4x4[] M; Vector4[] carTint;
        Camera cam; readonly Plane[] planes = new Plane[6];
        readonly HashSet<Material> usedMats = new HashSet<Material>();

        void Start()
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length; i++)
            {
                if (i + 1 < a.Length && a[i] == "-parked" && a[i + 1] == "0") Off = true;
                if (a[i] == "-parkedlegacy") Legacy = true;
            }
            Init();
        }

        /// builds the cells and draw lists (Start; editor shot tools call it directly)
        public void Init()
        {
            cells.Clear(); cellList.Clear(); buckets.Clear(); M = null;
            if (!data || cars == null) return;
            M = new Matrix4x4[cars.Length]; carTint = new Vector4[cars.Length];
            for (int i = 0; i < cars.Length; i++)
            {
                var p = (Vector3)cars[i];
                M[i] = Matrix4x4.TRS(p, Quaternion.Euler(0, cars[i].w, 0), Vector3.one);
                long k = ((long)Mathf.FloorToInt(p.x / CELL) << 32) ^ (uint)Mathf.FloorToInt(p.z / CELL);
                if (!cells.TryGetValue(k, out var l)) { cells[k] = l = new List<int>(); cellList.Add((k, new Vector3((Mathf.Floor(p.x / CELL) + .5f) * CELL, p.y, (Mathf.Floor(p.z / CELL) + .5f) * CELL))); }
                l.Add(i);
                // paint colour (linear) and a plate row 0..7 per car (hashed: neighbours of one colour differ)
                var kd = data.kinds[kind[i]];
                Vector4 c = kd.parkedTint != null && kd.parkedTint.Length > 0 ? kd.parkedTint[Mathf.Clamp(paint[i], 0, kd.parkedTint.Length - 1)] : Vector4.one;
                uint h = (uint)i * 2654435761u; c.w = (h >> 29) & 7;
                carTint[i] = c;
            }
            var index = new Dictionary<(Mesh, int, Material), int>();
            int B(Mesh m, int s, Material mt)
            {
                if (!index.TryGetValue((m, s, mt), out int i)) { i = buckets.Count; index[(m, s, mt)] = i; buckets.Add((m, s, mt)); if (mt) mt.enableInstancing = true; }
                return i;
            }
            int nk = data.kinds.Length;
            farB = new int[nk]; baked = new bool[nk]; fadeMat = new Material[nk];
            for (int k = 0; k < nk; k++)
            {
                var kd = data.kinds[k];
                baked[k] = !Legacy && kd.parked != null && kd.parked.Length == 3 && kd.parked[0] && kd.parked[1] && kd.parked[2] && kd.parkedMat;
                if (baked[k])
                {
                    kd.parkedMat.enableInstancing = true;
                    fadeMat[k] = new Material(kd.parkedMat) { name = kd.parkedMat.name + " (fade)", enableInstancing = true };
                    fadeMat[k].EnableKeyword("_FARFADE");
                }
                farB[k] = -1;
                if (!kd.farMesh || kd.farPaints == null || kd.farPaints.Length != kd.paints.Length) continue;
                farB[k] = B(kd.farMesh, 0, data.farPalette);
                for (int c = 0; c < kd.farPaints.Length; c++) B(kd.farMesh, 1, kd.farPaints[c]);
                B(kd.farMesh, 2, kd.farTail);
            }
            mats = new Matrix4x4[buckets.Count][]; matN = new int[buckets.Count];
            for (int i = 0; i < buckets.Count; i++) mats[i] = new Matrix4x4[32];
            pm = new Matrix4x4[nk * 3][]; pt = new Vector4[nk * 3][]; pmN = new int[nk * 3];
            ps = new Matrix4x4[nk * 2][]; psN = new int[nk * 2];
            for (int i = 0; i < nk * 3; i++) { pm[i] = new Matrix4x4[32]; pt[i] = new Vector4[32]; }
            for (int i = 0; i < nk * 2; i++) ps[i] = new Matrix4x4[32];
        }

        void Add(int b, in Matrix4x4 m)
        {
            if (matN[b] == mats[b].Length) System.Array.Resize(ref mats[b], mats[b].Length * 2);
            mats[b][matN[b]++] = m;
        }
        void AddP(int b, in Matrix4x4 m, in Vector4 t)
        {
            if (pmN[b] == pm[b].Length) { System.Array.Resize(ref pm[b], pm[b].Length * 2); System.Array.Resize(ref pt[b], pt[b].Length * 2); }
            pt[b][pmN[b]] = t; pm[b][pmN[b]++] = m;
        }
        void AddS(int b, in Matrix4x4 m)
        {
            if (psN[b] == ps[b].Length) System.Array.Resize(ref ps[b], ps[b].Length * 2);
            ps[b][psN[b]++] = m;
        }

        void LateUpdate()
        {
            if (!cam) cam = Camera.main;
            Draw(cam, null);
        }

        /// collects and submits this frame's draws for camera c (target: only that camera, null = every camera)
        public void Draw(Camera c, Camera target)
        {
            DrawnLastFrame = 0; CallsMain = 0; CallsShadow = 0; Materials = 0;
            if (Off || M == null || !c) return;
            GeometryUtility.CalculateFrustumPlanes(c, planes);
            var cp = c.transform.position;
            for (int i = 0; i < matN.Length; i++) matN[i] = 0;
            for (int i = 0; i < pmN.Length; i++) pmN[i] = 0;
            for (int i = 0; i < psN.Length; i++) psN[i] = 0;
            float k = Mathf.Max(.3f, TreeRenderer.Distance), draw = drawDist * k, sh = shadowDist;
            float draw2 = draw * draw, sh2 = sh * sh, l02 = lod0Dist * lod0Dist, l12 = lod1Dist * lod1Dist;
            foreach (var (key, cc) in cellList)
            {
                float dx = cc.x - cp.x, dz = cc.z - cp.z;
                if (Mathf.Sqrt(dx * dx + dz * dz) - CELL * .71f > draw) continue;
                foreach (int i in cells[key])
                {
                    ref var m = ref M[i];
                    float ex = m.m03 - cp.x, ez = m.m23 - cp.z, dd = ex * ex + ez * ez;
                    if (dd > draw2) continue;
                    int ki = kind[i]; var kd = data.kinds[ki];
                    bool vis = GeometryUtility.TestPlanesAABB(planes, new Bounds(new Vector3(m.m03, m.m13 + kd.h * .5f, m.m23), new Vector3(kd.len, kd.h, kd.len)));
                    if (!vis && dd >= sh2) continue;
                    DrawnLastFrame++;
                    if (baked[ki])
                    {
                        if (vis) AddP(ki * 3 + (ForceLevel >= 0 ? ForceLevel : dd < l02 ? 0 : dd < l12 ? 1 : 2), m, carTint[i]);
                        AddS(ki * 2 + (dd < l12 ? 0 : 1), m);
                        continue;
                    }
                    // far mesh path (one list per bucket, all casting: a second, shadow-less list doubled the draws / SetPass calls)
                    int f0 = farB[ki]; if (f0 < 0) continue;
                    Add(f0, m); Add(f0 + 1 + paint[i], m); Add(f0 + 1 + kd.farPaints.Length, m);
                }
            }
            var wb = new Bounds(cp, Vector3.one * (draw * 2 + 50));
            Shader.SetGlobalVector(ID_FarFadeEye, cp);
            var fadeBand = new Vector4(Mathf.Max(lod1Dist, draw - 45), draw, 0, 0);
            usedMats.Clear(); int mi = 0;
            MaterialPropertyBlock Mpb() { if (mi == mpb.Count) mpb.Add(new MaterialPropertyBlock()); var b = mpb[mi++]; b.Clear(); return b; }
            for (int ki = 0; ki < data.kinds.Length; ki++)
            {
                if (!baked[ki]) continue;
                var kd = data.kinds[ki]; var mat = kd.parkedMat;
                for (int lv = 0; lv < 3; lv++)
                {
                    int b = ki * 3 + lv, n = pmN[b]; if (n == 0) continue;
                    var lm = lv == 2 && fadeMat[ki] ? fadeMat[ki] : mat;
                    if (lm != mat) lm.SetVector(ID_ParkFade, fadeBand);
                    usedMats.Add(lm);
                    for (int s = 0; s < n; s += BATCH)
                    {
                        int cnt = Mathf.Min(BATCH, n - s);
                        System.Array.Copy(pt[b], s, tintTmp, 0, cnt);
                        var props = Mpb(); props.SetVectorArray("_ParkTint", tintTmp);
                        Graphics.RenderMeshInstanced(new RenderParams(lm) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = true, worldBounds = wb, layer = gameObject.layer, matProps = props, camera = target },
                            kd.parked[lv], 0, pm[b], cnt, s);
                        CallsMain++;
                    }
                }
                for (int lv = 0; lv < 2; lv++)
                {
                    int b = ki * 2 + lv, n = psN[b]; if (n == 0) continue;
                    for (int s = 0; s < n; s += BATCH)
                    {
                        Graphics.RenderMeshInstanced(new RenderParams(mat) { shadowCastingMode = ShadowCastingMode.ShadowsOnly, receiveShadows = false, worldBounds = wb, layer = gameObject.layer, camera = target },
                            kd.parked[lv + 1], 0, ps[b], Mathf.Min(BATCH, n - s), s);
                        CallsShadow++;
                    }
                }
            }
            for (int b = 0; b < buckets.Count; b++)
            {
                var bk = buckets[b]; if (matN[b] == 0) continue;
                usedMats.Add(bk.mat);
                for (int s = 0; s < matN[b]; s += 500)
                {
                    Graphics.RenderMeshInstanced(new RenderParams(bk.mat) { shadowCastingMode = ShadowCastingMode.On, receiveShadows = true, worldBounds = wb, layer = gameObject.layer, camera = target }, bk.mesh, bk.sub, mats[b], Mathf.Min(500, matN[b] - s), s);
                    CallsMain++;
                }
            }
            Materials = usedMats.Count;
        }
    }
}
