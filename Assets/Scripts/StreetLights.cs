using System.Collections.Generic;
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
    [DefaultExecutionOrder(10001)]
    public class StreetLights : MonoBehaviour
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

        const float CELL = 128;
        class Cell { public Bounds b; public int[] idx; }
        readonly List<Cell> cells = new List<Cell>();
        Matrix4x4[] mtx; Material[] lensRun; Light[] pool; LightPreset preset; Light sun;
        readonly Plane[] planes = new Plane[6];
        float lastLevel = -1;
        const int CHUNK = 500;
        // per type: near + shadow, near no shadow, near shadow only, far
        List<Matrix4x4>[,] lists; Vector3[,] lo, hi;
        public int DrawnLastFrame { get; private set; }

        void Start()
        {
            foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-lamps0") Off = true;
            if (lamps == null || lamps.Length == 0 || near == null) return;
            mtx = new Matrix4x4[lamps.Length];
            var map = new Dictionary<long, List<int>>();
            for (int i = 0; i < lamps.Length; i++)
            {
                var l = lamps[i]; mtx[i] = Matrix4x4.TRS(new Vector3(l.x, l.y, l.z), Quaternion.Euler(0, l.w, 0), Vector3.one);
                long k = ((long)Mathf.FloorToInt(l.x / CELL) << 32) ^ (uint)Mathf.FloorToInt(l.z / CELL);
                if (!map.TryGetValue(k, out var li)) map[k] = li = new List<int>(); li.Add(i);
            }
            foreach (var li in map.Values)
            {
                var b = new Bounds(new Vector3(lamps[li[0]].x, lamps[li[0]].y, lamps[li[0]].z), Vector3.one);
                foreach (int i in li) { var p = new Vector3(lamps[i].x, lamps[i].y, lamps[i].z); b.Encapsulate(new Bounds(p + Vector3.up * 6, new Vector3(5, 13, 5))); }
                cells.Add(new Cell { b = b, idx = li.ToArray() });
            }
            lists = new List<Matrix4x4>[near.Length, 4];
            for (int t = 0; t < near.Length; t++) for (int k = 0; k < 4; k++) lists[t, k] = new List<Matrix4x4>(256);
            lo = new Vector3[near.Length, 4]; hi = new Vector3[near.Length, 4];
            // the lens materials as runtime copies (the emission changes at night; never the assets)
            lensRun = new Material[mats.Length];
            for (int i = 0; i < mats.Length; i++) { if (!mats[i]) continue; if (i % 2 == 1) { lensRun[i] = new Material(mats[i]); lensRun[i].EnableKeyword("_EMISSION"); } else lensRun[i] = mats[i]; lensRun[i].enableInstancing = true; }
            if (poolMat) { poolMat = new Material(poolMat); foreach (var r in pools) if (r) { r.sharedMaterial = poolMat; r.enabled = false; } }
            pool = new Light[Mathf.Max(0, realLights)];
            for (int i = 0; i < pool.Length; i++)
            {
                var go = new GameObject("lamp light " + i); go.transform.SetParent(transform, false);
                var L = go.AddComponent<Light>(); L.type = LightType.Point; L.range = realRange; L.color = lampColor; L.shadows = LightShadows.None;
                L.renderMode = LightRenderMode.ForcePixel; L.enabled = false; pool[i] = L;
            }
        }

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
            if (mtx == null) return;
            var cam = Camera.main; if (!cam) return;
            float lv = Off ? 0 : NightLevel(); Level = lv;
            if (Mathf.Abs(lv - lastLevel) > .001f)
            {
                lastLevel = lv;
                for (int i = 1; i < lensRun.Length; i += 2) if (lensRun[i]) lensRun[i].SetColor("_EmissionColor", lampColor * (lv > 0 ? lensEmission * (.4f + .6f * lv) : 0));
                if (poolMat) poolMat.SetFloat("_Level", lv);
                foreach (var r in pools) if (r) r.enabled = lv > 0;
            }
            if (Off) { foreach (var L in pool) L.enabled = false; return; }
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 cp = cam.transform.position;
            float near2 = nearDistance * nearDistance, sh2 = shadowDistance * shadowDistance, draw2 = drawDistance * drawDistance;
            for (int t = 0; t < near.Length; t++) for (int k = 0; k < 4; k++) lists[t, k].Clear();
            int drawn = 0;
            foreach (var c in cells)
            {
                float d = Mathf.Sqrt(c.b.SqrDistance(cp));
                if (d > drawDistance) continue;
                bool view = GeometryUtility.TestPlanesAABB(planes, c.b);
                if (!view && d > shadowDistance) continue;
                foreach (int i in c.idx)
                {
                    var l = lamps[i]; float dx = l.x - cp.x, dz = l.z - cp.z, dd = dx * dx + dz * dz;
                    if (dd > draw2) continue;
                    int t = types[i];
                    if (dd < near2)
                    {
                        bool vis = view && InView(new Vector3(l.x, l.y + 6, l.z), 7);
                        int k = vis ? (dd < sh2 ? 0 : 1) : (dd < sh2 ? 2 : -1);
                        if (k < 0) continue;
                        Add(t, k, i); if (vis) drawn++;
                    }
                    else if (view) { Add(t, 3, i); drawn++; }
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

        void Add(int t, int k, int i)
        {
            var l = lamps[i]; var a = new Vector3(l.x - 3, l.y, l.z - 3); var b = new Vector3(l.x + 3, l.y + 13, l.z + 3);
            if (lists[t, k].Count == 0) { lo[t, k] = a; hi[t, k] = b; } else { lo[t, k] = Vector3.Min(lo[t, k], a); hi[t, k] = Vector3.Max(hi[t, k], b); }
            lists[t, k].Add(mtx[i]);
        }

        // the nearest heads get the pooled point lights (a full search ~4 times a second, the intensity faded every frame)
        readonly List<(float d, int h)> best = new List<(float, int)>();
        float nextSearch; int[] chosen = new int[0];
        void RealLights(Vector3 cp, float lv)
        {
            if (pool.Length == 0) return;
            if (lv <= 0 || heads == null) { foreach (var L in pool) L.enabled = false; return; }
            if (Time.unscaledTime >= nextSearch || chosen.Length != pool.Length)
            {
                nextSearch = Time.unscaledTime + .25f; best.Clear(); float r2 = realReach * realReach;
                for (int h = 0; h < heads.Length; h++) { float d = (heads[h] - cp).sqrMagnitude; if (d < r2) best.Add((d, h)); }
                best.Sort((a, b) => a.d.CompareTo(b.d));
                chosen = new int[pool.Length]; for (int i = 0; i < chosen.Length; i++) chosen[i] = i < best.Count ? best[i].h : -1;
            }
            for (int i = 0; i < pool.Length; i++)
            {
                var L = pool[i]; int h = chosen[i];
                if (h < 0) { L.enabled = false; continue; }
                float d = Vector3.Distance(heads[h], cp);
                L.transform.position = heads[h] - Vector3.up * .25f;
                L.intensity = realIntensity * lv * Mathf.Clamp01((realReach - d) / 20f);
                L.enabled = L.intensity > .01f;
            }
        }

        bool InView(Vector3 c, float r) { for (int q = 0; q < 6; q++) if (planes[q].GetDistanceToPoint(c) < -r) return false; return true; }
    }
}
