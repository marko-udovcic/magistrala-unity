using System.Collections.Generic;
using Magistrala.Cells;
using Magistrala.Streaming;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala
{
    /// Street lamps (PropsBuilder.Lamps, Assets/Editor/StreetLights.cs): every lamp instanced (Graphics.RenderMeshInstanced) per 128 m
    /// cell: the full mesh within nearDistance (shadows within shadowDistance), a few-sided far mesh beyond, nothing past
    /// drawDistance. Per type: submesh 0 the pole / arm / housing (galvanised or concrete), submesh 1 the LED lens.
    /// Night (LightPreset: the sunset G and the night preset, else the sun under ~6°): the lenses emit (bloom), the baked light
    /// pools on the ground under each head (World/Props/rasvjeta/pools: draped additive meshes, Magistrala/LightPool) switch on, and
    /// the `realLights` heads nearest the camera get a pooled real point light (no shadows), faded in / out by distance — no
    /// thousands of real-time lights.
    /// Pages (streaming phase 2, Assets/Scripts/Cells/CellData.cs): lamps, heads and light pools of one CellGrid cell are one page.
    /// From the scene's arrays at start (editor scene), or from the cell blobs when the bake took the arrays, the pool renderers
    /// and the pole colliders out (player / play mode; the pools are then drawn here, the colliders come from CellColliders).
    [DefaultExecutionOrder(10001)]
    public class StreetLights : MonoBehaviour, ICellLayer
    {
        public Vector4[] lamps;            // Unity x, ground y, z, yaw (deg; local +z = the arm, towards the road)
        public byte[] types;
        public Mesh[] near, far;           // per type
        public Material[] mats;            // per type: [type * 2] pole, [type * 2 + 1] lens
        public Vector3[] heads;            // world position of every lamp head (double-arm lamps: two)
        public Renderer[] pools;
        public Material poolMat;
        public float nearDistance = 70, drawDistance = 650, shadowDistance = 45;
        public int realLights = 6; public float realRange = 22, realIntensity = 3.5f, realReach = 75;
        public Color lampColor = new Color(1f, .84f, .64f);
        public float lensEmission = 7f;
        /// 0 off (day) .. 1 full night; set every frame (others may read it)
        public static float Level;
        /// measuring only (-lamps 0): no lamps drawn, no lights
        public static bool Off;
        public int Count { get; private set; }

        const float CELL = 128;
        /// a 128 m bucket of a page: bounds and a range of the page's lamps
        struct Sub { public Bounds b; public int o, n; }
        /// one CellGrid cell: lamps (+ matrices, types) by bucket, lamp heads, light pools (mesh, matrix, layer, world bounds)
        sealed class Page { public Vector4[] lamps; public byte[] types; public Matrix4x4[] mtx; public int n; public Sub[] subs; public Vector3[] heads; public int nh;
            public Mesh[] poolMesh; public Matrix4x4[] poolM; public int[] poolLayer; public Bounds[] poolB; }
        readonly List<Page> pages = new List<Page>();
        Material[] lensRun; Light[] pool; LightPreset preset; Light sun;
        readonly Plane[] planes = new Plane[6];
        float lastLevel = -1;
        const int CHUNK = 500;
        // per type: near + shadow, near no shadow, near shadow only, far
        List<Matrix4x4>[,] lists; Vector3[,] lo, hi;
        public int DrawnLastFrame { get; private set; }
        Mesh[] cellPoolMeshes; bool inited, fromCells;
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        void Start()
        {
            InitOnce();
            if (fromCells || lamps == null || lamps.Length == 0) return;
            // the scene's arrays: one page per CellGrid cell (the pools stay the scene's renderers)
            var byCell = new Dictionary<Vector2Int, (List<int> l, List<Vector3> h)>();
            (List<int> l, List<Vector3> h) At(Vector3 p) { var c = CellGrid.Cell(p); if (!byCell.TryGetValue(c, out var e)) byCell[c] = e = (new List<int>(), new List<Vector3>()); return e; }
            for (int i = 0; i < lamps.Length; i++) At(lamps[i]).l.Add(i);
            if (heads != null) foreach (var h in heads) At(h).h.Add(h);
            foreach (var kv in byCell)
            {
                var pg = new Page { n = kv.Value.l.Count, lamps = new Vector4[kv.Value.l.Count], types = new byte[kv.Value.l.Count], heads = kv.Value.h.ToArray(), nh = kv.Value.h.Count };
                for (int k = 0; k < pg.n; k++) { pg.lamps[k] = lamps[kv.Value.l[k]]; pg.types[k] = types[kv.Value.l[k]]; }
                Finish(pg); Attach(kv.Key, pg);
            }
        }

        void InitOnce()
        {
            if (inited) return; inited = true;
            foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-lamps0") Off = true;
            if (near == null) return;
            lists = new List<Matrix4x4>[near.Length, 4];
            for (int t = 0; t < near.Length; t++) for (int k = 0; k < 4; k++) lists[t, k] = new List<Matrix4x4>(256);
            lo = new Vector3[near.Length, 4]; hi = new Vector3[near.Length, 4];
            // the lens materials as runtime copies (the emission changes at night; never the assets)
            lensRun = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++) { if (!mats[i]) continue; if (i % 2 == 1) { lensRun[i] = new Material(mats[i]); lensRun[i].EnableKeyword("_EMISSION"); } else lensRun[i] = mats[i]; lensRun[i].enableInstancing = true; }
            if (poolMat) { poolMat = new Material(poolMat); if (pools != null) foreach (var r in pools) if (r) { r.sharedMaterial = poolMat; r.enabled = false; } }
            pool = new Light[Mathf.Max(0, realLights)];
            for (int i = 0; i < pool.Length; i++)
            {
                var go = new GameObject("lamp light " + i); go.transform.SetParent(transform, false);
                var L = go.AddComponent<Light>(); L.type = LightType.Point; L.range = realRange; L.color = lampColor; L.shadows = LightShadows.None;
                L.renderMode = LightRenderMode.ForcePixel; L.enabled = false; pool[i] = L;
            }
            chosen = new int[pool.Length]; for (int i = 0; i < chosen.Length; i++) chosen[i] = -1;
        }

        /// matrices and 128 m buckets (the lamps sorted by bucket, stable) of a page
        static void Finish(Page pg)
        {
            var idx = new Dictionary<long, int>(); var lists = new List<List<int>>();
            for (int i = 0; i < pg.n; i++) { long k = CellMath.Key(pg.lamps[i].x, pg.lamps[i].z, CELL); if (!idx.TryGetValue(k, out int b)) { b = lists.Count; idx[k] = b; lists.Add(new List<int>()); } lists[b].Add(i); }
            var L = new Vector4[pg.n]; var T = new byte[pg.n]; pg.mtx = new Matrix4x4[pg.n]; pg.subs = new Sub[lists.Count]; int w = 0;
            for (int b = 0; b < lists.Count; b++)
            {
                var l0 = pg.lamps[lists[b][0]]; var bb = new Bounds(new Vector3(l0.x, l0.y, l0.z), Vector3.one);
                pg.subs[b].o = w; pg.subs[b].n = lists[b].Count;
                foreach (int i in lists[b])
                {
                    var l = pg.lamps[i]; L[w] = l; T[w] = pg.types[i]; pg.mtx[w] = CellMath.TRSYaw(new Vector3(l.x, l.y, l.z), l.w, 1); w++;
                    bb.Encapsulate(new Bounds(new Vector3(l.x, l.y, l.z) + Vector3.up * 6, new Vector3(5, 13, 5)));
                }
                pg.subs[b].b = bb;
            }
            pg.lamps = L; pg.types = T;
        }

        // ICellLayer (CellData): LAMP, HEAD and POOL sections
        public void Prepare(CellManifest m) { fromCells = true; cellPoolMeshes = m.poolMeshes; InitOnce(); }
        public object Decode(CellSections s)
        {
            int o = s.Find(CellBlob.LAMP, out int n), oh = s.Find(CellBlob.HEAD, out int nh), op = s.Find(CellBlob.POOL, out int np);
            if (n == 0 && nh == 0 && np == 0) return null;
            var pg = new Page { n = n, lamps = new Vector4[n], types = new byte[n], heads = new Vector3[nh], nh = nh, poolMesh = new Mesh[np], poolM = new Matrix4x4[np], poolLayer = new int[np], poolB = new Bounds[np] };
            for (int k = 0; k < n; k++) { int r = o + k * CellBlob.LAMP_B; pg.lamps[k] = CellBlob.V4(s.b, r); pg.types[k] = (byte)CellBlob.I(s.b, r + 16); }
            for (int k = 0; k < nh; k++) pg.heads[k] = CellBlob.V3(s.b, oh + k * CellBlob.HEAD_B);
            for (int k = 0; k < np; k++)
            {
                int r = op + k * CellBlob.POOL_B, mi = CellBlob.I(s.b, r);
                pg.poolMesh[k] = cellPoolMeshes != null && mi >= 0 && mi < cellPoolMeshes.Length ? cellPoolMeshes[mi] : null; // (reading the array of references: no Unity API)
                pg.poolLayer[k] = CellBlob.I(s.b, r + 4); pg.poolM[k] = CellBlob.M34(s.b, r + 8);
                pg.poolB[k] = new Bounds(CellBlob.V3(s.b, r + 56), CellBlob.V3(s.b, r + 68) * 2);
            }
            Finish(pg); return pg;
        }
        public void Attach(Vector2Int cell, object page) { var pg = (Page)page; pages.Add(pg); Count += pg.n; nextSearch = 0; }
        public void Detach(Vector2Int cell, object page) { var pg = (Page)page; if (pages.Remove(pg)) { Count -= pg.n; nextSearch = 0; } }
        public void Release(object page) { }
        // phase-3 scheduler (ICellDataSystem names): this system's part of a cell (CellData decodes the cell once for all systems)
        bool Mine => CellData.I && CellData.I.lamps == this;
        public void LoadCell(Vector2Int c) { if (Mine) CellData.Load(c, 1 << CellBlob.LAMPS); }
        public void UnloadCell(Vector2Int c) { if (Mine) CellData.Unload(c, 1 << CellBlob.LAMPS); }
        public bool IsLoaded(Vector2Int c) => !Mine || CellData.IsLoaded(c, 1 << CellBlob.LAMPS);


        float NightLevel()
        {
            if (!preset) preset = FindFirstObjectByType<LightPreset>();
            if (preset) return preset.LampLevel;
            if (!sun) sun = RenderSettings.sun;
            if (!sun) return 0;
            float elev = Mathf.Asin(Mathf.Clamp(-sun.transform.forward.y, -1, 1)) * Mathf.Rad2Deg;
            return Mathf.Clamp01((6 - elev) / 10);
        }

        void LateUpdate()
        {
            sw.Restart(); DrawAll(); sw.Stop(); CellStats.Add(CellStats.LAMPS, (float)sw.Elapsed.TotalMilliseconds);
        }

        void DrawAll()
        {
            if (lists == null || pages.Count == 0) return;
            var cam = Camera.main; if (!cam) return;
            float lv = Off ? 0 : NightLevel(); Level = lv;
            if (Mathf.Abs(lv - lastLevel) > .001f)
            {
                lastLevel = lv;
                for (int i = 1; i < lensRun.Length; i += 2) if (lensRun[i]) lensRun[i].SetColor("_EmissionColor", lampColor * (lv > 0 ? lensEmission * (.4f + .6f * lv) : 0));
                if (poolMat) poolMat.SetFloat("_Level", lv);
                if (pools != null) foreach (var r in pools) if (r) r.enabled = lv > 0;
            }
            if (Off) { foreach (var L in pool) L.enabled = false; return; }
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 cp = cam.transform.position;
            float near2 = nearDistance * nearDistance, sh2 = shadowDistance * shadowDistance, draw2 = drawDistance * drawDistance;
            for (int t = 0; t < near.Length; t++) for (int k = 0; k < 4; k++) lists[t, k].Clear();
            int drawn = 0;
            for (int pi = 0; pi < pages.Count; pi++)
            {
                var pg = pages[pi];
                for (int si = 0; si < pg.subs.Length; si++)
                {
                    ref var c = ref pg.subs[si];
                    float d = Mathf.Sqrt(c.b.SqrDistance(cp));
                    if (d > drawDistance) continue;
                    bool view = GeometryUtility.TestPlanesAABB(planes, c.b);
                    if (!view && d > shadowDistance) continue;
                    for (int i = c.o, end = c.o + c.n; i < end; i++)
                    {
                        var l = pg.lamps[i]; float dx = l.x - cp.x, dz = l.z - cp.z, dd = dx * dx + dz * dz;
                        if (dd > draw2) continue;
                        int t = pg.types[i];
                        if (dd < near2)
                        {
                            bool vis = view && InView(new Vector3(l.x, l.y + 6, l.z), 7);
                            int k = vis ? (dd < sh2 ? 0 : 1) : (dd < sh2 ? 2 : -1);
                            if (k < 0) continue;
                            Add(t, k, l, pg.mtx[i]); if (vis) drawn++;
                        }
                        else if (view) { Add(t, 3, l, pg.mtx[i]); drawn++; }
                    }
                }
                // the light pools of the cells (scene renderers when not from the cells: those are switched above)
                if (lv > 0 && pg.poolMesh != null && poolMat)
                    for (int k = 0; k < pg.poolMesh.Length; k++)
                    {
                        if (!pg.poolMesh[k] || !GeometryUtility.TestPlanesAABB(planes, pg.poolB[k])) continue;
                        var rp = new RenderParams(poolMat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = pg.poolLayer[k], worldBounds = pg.poolB[k], lightProbeUsage = LightProbeUsage.Off, reflectionProbeUsage = ReflectionProbeUsage.Off };
                        Graphics.RenderMesh(rp, pg.poolMesh[k], 0, pg.poolM[k]);
                    }
            }
            DrawnLastFrame = drawn;
            for (int t = 0; t < near.Length; t++)
                for (int k = 0; k < 4; k++)
                {
                    var L = lists[t, k]; if (L.Count == 0) continue;
                    var mesh = k == 3 ? far[t] : near[t];
                    var mode = k == 0 ? ShadowCastingMode.On : k == 2 ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.Off;
                    for (int sm = 0; sm < Mathf.Min(2, mesh.subMeshCount); sm++)
                    {
                        var mat = lensRun[t * 2 + sm]; if (!mat) continue;
                        if (sm == 1 && k == 2) continue; // (the lens casts nothing of its own)
                        var rp = new RenderParams(mat) { shadowCastingMode = mode, receiveShadows = true, worldBounds = new Bounds((lo[t, k] + hi[t, k]) * .5f, hi[t, k] - lo[t, k]), layer = gameObject.layer };
                        for (int i = 0; i < L.Count; i += CHUNK) Graphics.RenderMeshInstanced(rp, mesh, sm, L, Mathf.Min(CHUNK, L.Count - i), i);
                    }
                }
            RealLights(cp, lv);
        }

        void Add(int t, int k, Vector4 l, in Matrix4x4 m)
        {
            var a = new Vector3(l.x - 3, l.y, l.z - 3); var b = new Vector3(l.x + 3, l.y + 13, l.z + 3);
            if (lists[t, k].Count == 0) { lo[t, k] = a; hi[t, k] = b; } else { lo[t, k] = Vector3.Min(lo[t, k], a); hi[t, k] = Vector3.Max(hi[t, k], b); }
            lists[t, k].Add(m);
        }

        // the nearest heads get the pooled point lights (a full search ~4 times a second, the intensity faded every frame)
        readonly List<(float d, Vector3 h)> best = new List<(float, Vector3)>();
        float nextSearch; int[] chosen = new int[0]; Vector3[] chosenP = new Vector3[0];
        static readonly System.Comparison<(float d, Vector3 h)> ByD = (a, b) => a.d.CompareTo(b.d);
        void RealLights(Vector3 cp, float lv)
        {
            if (pool.Length == 0) return;
            if (lv <= 0) { foreach (var L in pool) L.enabled = false; return; }
            if (chosenP.Length != pool.Length) { chosenP = new Vector3[pool.Length]; chosen = new int[pool.Length]; nextSearch = 0; }
            if (Time.unscaledTime >= nextSearch)
            {
                nextSearch = Time.unscaledTime + .25f; best.Clear(); float r2 = realReach * realReach;
                for (int pi = 0; pi < pages.Count; pi++) { var pg = pages[pi]; for (int h = 0; h < pg.nh; h++) { float d = (pg.heads[h] - cp).sqrMagnitude; if (d < r2) best.Add((d, pg.heads[h])); } }
                best.Sort(ByD);
                for (int i = 0; i < chosen.Length; i++) { chosen[i] = i < best.Count ? 1 : -1; if (i < best.Count) chosenP[i] = best[i].h; }
            }
            for (int i = 0; i < pool.Length; i++)
            {
                var L = pool[i];
                if (chosen[i] < 0) { L.enabled = false; continue; }
                float d = Vector3.Distance(chosenP[i], cp);
                L.transform.position = chosenP[i] - Vector3.up * .25f;
                L.intensity = realIntensity * lv * Mathf.Clamp01((realReach - d) / 20f);
                L.enabled = L.intensity > .01f;
            }
        }

        bool InView(Vector3 c, float r) { for (int q = 0; q < 6; q++) if (planes[q].GetDistanceToPoint(c) < -r) return false; return true; }
    }
}
