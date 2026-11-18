using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala
{
    /// Instanced vegetation: the importer writes all plants as binary records (x, y, z, yaw, scale, proto) into `data`;
    /// at start they are bucketed per 128 m cell and prototype; each frame the cells near the camera are drawn with
    /// Graphics.RenderMeshInstanced. Far cutoff per prototype (shrubs / rocks short).
    /// Cells that straddle a LOD / shadow / draw distance are split per plant (a 128 m cell is too coarse: its nearest
    /// corner decided for the whole cell, so full meshes and their shadows reached ~200 m): full mesh within nearDistance,
    /// impostor (crossed quads) beyond; full-mesh shadows only within shadowDistance; near full meshes outside the view
    /// are drawn into the shadow map only (or not at all). Each split list gets its own tight bounds, so it touches fewer
    /// shadow cascades. `Distance` scales all vegetation distances (quality level).
    /// Smoothness: runs after the chase camera (LateUpdate, late execution order), so culling uses this frame's camera, not
    /// the last one; full / impostor and full-mesh shadow switch with hysteresis (per-plant state); between the full-mesh
    /// shadow distance and the impostor distance the plant casts its impostor's shadow (no shadow-less ring); cells just
    /// outside the view still cast shadows that fall into it; plants grow in over the last part of their draw distance.
    [DefaultExecutionOrder(10000)]
    public class TreeRenderer : MonoBehaviour
    {
        public TextAsset data;
        public Mesh[] meshes;
        public Material[] materials;          // flattened: per prototype its submesh materials, in order (leaves alpha-clipped)
        public Material[] blendMaterials;     // measuring only (-trees-blend): the glTF originals, leaves alpha-blended
        public int[] materialStart;           // index into materials per prototype
        public float[] drawDistance;          // per prototype
        public float[] nearDistance;          // full mesh within, impostor (crossed quads) beyond
        public Mesh[] impMeshes;
        public Material[] impMaterials;
        public float shadowDistance = 90, impShadowDistance = 260;
        /// quality level multiplier of all distances (RenderQuality); measuring only: Legacy = the old per-cell path
        public static float Distance = 1;
        public static bool Legacy;
        /// measuring only (-trees-skip imp,impsh,full,fullsh): 1 impostors, 2 impostor shadows, 4 full meshes, 8 full-mesh shadows
        public static int Skip;
        /// measuring only (ProbeBench [hitch]): this component's Update in ms, last frame and summed over the window
        public static float LastMs, SumMs;
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        const float CELL = 128;

        class Bucket { public Bounds bounds; public Vector3 centre; public List<Matrix4x4>[] perProto; public Matrix4x4[][] arr; public int[] start; }
        /// per plant: bit 0 full mesh, bit 1 full-mesh shadow (the hysteresis state)
        byte[] state;
        const float HYST = 4, FADE = .12f, FADE_MAX = 30; // m either side of a switch distance; plants grow in over the last 12 % of their draw distance, at most 30 m
        public static bool NoFix; // measuring only (-trees-nofix): no hysteresis / impostor shadow fill / shadow cells / grow-in
        readonly List<Bucket> buckets = new List<Bucket>();
        readonly Plane[] planes = new Plane[6];
        public int Count { get; private set; }
        public int DrawnLastFrame { get; private set; }
        /// per prototype, last frame: instances in the main pass (full / impostor) and drawn into shadows (full / impostor)
        public int[] statFull, statImp, statFullSh, statImpSh;
        public long TrisMain, TrisShadow;

        // per-frame pool of instance arrays for the split lists (each submit gets its own array)
        const int CHUNK = 500;
        readonly List<Matrix4x4[]> pool = new List<Matrix4x4[]>(); int poolUsed;
        Matrix4x4[] Take() { if (poolUsed == pool.Count) pool.Add(new Matrix4x4[CHUNK]); return pool[poolUsed++]; }
        struct List3 { public Matrix4x4[] a; public int n; public Vector3 lo, hi; }

        void Start()
        {
            if (!data) return;
            var args = System.Environment.GetCommandLineArgs(); // measuring only
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-trees-legacy") Legacy = true;
                if (args[i] == "-trees-nofix") NoFix = true;
                if (args[i] == "-trees-blend" && blendMaterials != null && blendMaterials.Length == materials.Length) materials = blendMaterials;
                if (args[i] == "-trees-skip" && i + 1 < args.Length) foreach (var w in args[i + 1].Split(',')) Skip |= w == "imp" ? 1 : w == "impsh" ? 2 : w == "full" ? 4 : w == "fullsh" ? 8 : 0;
            }
            var b = data.bytes; int n = b.Length / 24;
            var map = new Dictionary<long, Bucket>();
            for (int i = 0; i < n; i++)
            {
                int o = i * 24;
                float x = System.BitConverter.ToSingle(b, o), y = System.BitConverter.ToSingle(b, o + 4), z = System.BitConverter.ToSingle(b, o + 8);
                float yaw = System.BitConverter.ToSingle(b, o + 12), s = System.BitConverter.ToSingle(b, o + 16); int p = (int)System.BitConverter.ToSingle(b, o + 20);
                if (p < 0 || p >= meshes.Length || !meshes[p]) continue;
                long key = ((long)Mathf.FloorToInt(x / CELL) << 32) ^ (uint)Mathf.FloorToInt(z / CELL);
                if (!map.TryGetValue(key, out var bk))
                {
                    bk = new Bucket { perProto = new List<Matrix4x4>[meshes.Length] };
                    for (int k = 0; k < meshes.Length; k++) bk.perProto[k] = new List<Matrix4x4>();
                    var c = new Vector3((Mathf.Floor(x / CELL) + .5f) * CELL, y, (Mathf.Floor(z / CELL) + .5f) * CELL);
                    bk.bounds = new Bounds(c, new Vector3(CELL, 1, CELL)); map[key] = bk;
                }
                var m = Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(0, yaw, 0), Vector3.one * s);
                bk.perProto[p].Add(m);
                bk.bounds.Encapsulate(new Bounds(new Vector3(x, y, z) + Vector3.up * meshes[p].bounds.center.y * s, meshes[p].bounds.size * s));
                Count++;
            }
            foreach (var bk in map.Values)
            {
                bk.arr = new Matrix4x4[meshes.Length][];
                for (int k = 0; k < meshes.Length; k++) bk.arr[k] = bk.perProto[k].ToArray();
                bk.perProto = null; bk.centre = bk.bounds.center; buckets.Add(bk);
            }
            int total = 0;
            foreach (var bk in buckets) { bk.start = new int[meshes.Length]; for (int k = 0; k < meshes.Length; k++) { bk.start[k] = total; total += bk.arr[k].Length; } }
            state = new byte[total];
            // a checkout without the generated clip materials (Assets/Generated is not in git): make them here from the originals
            if (blendMaterials != null && blendMaterials.Length == materials.Length)
                for (int i = 0; i < materials.Length; i++) if (!materials[i] && blendMaterials[i]) materials[i] = Clip(blendMaterials[i]);
            foreach (var m in materials) if (m) m.enableInstancing = true;
            int np = meshes.Length; statFull = new int[np]; statImp = new int[np]; statFullSh = new int[np]; statImpSh = new int[np];
        }

        /// alpha-blended glTF leaves -> alpha-clipped copy (same as TreeProtos.LeafClip, glTF mask mode)
        static Material Clip(Material m)
        {
            if (!(m.renderQueue >= 3000 || m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))) return m;
            var c = new Material(m) { name = m.name + "_clip" };
            c.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); c.EnableKeyword("_ALPHATEST_ON");
            c.SetFloat("_Surface", 0); c.SetFloat("_AlphaClip", 1); c.SetFloat("_ZWrite", 1); c.SetFloat("_SrcBlend", 1); c.SetFloat("_DstBlend", 0);
            c.SetFloat("_SrcBlendAlpha", 1); c.SetFloat("_DstBlendAlpha", 0); c.SetFloat("alphaCutoff", .5f); c.SetFloat("_AlphaToMask", 1);
            c.SetOverrideTag("RenderType", "TransparentCutout"); c.renderQueue = 2450; c.SetShaderPassEnabled("DepthOnly", true);
            return c;
        }

        static int Tris(Mesh m) { long t = 0; for (int s = 0; s < m.subMeshCount; s++) t += m.GetIndexCount(s); return (int)(t / 3); }
        int[] trisFull, trisImp;

        void LateUpdate()
        {
            sw.Restart(); UpdateDraw(); sw.Stop(); LastMs = (float)sw.Elapsed.TotalMilliseconds; SumMs += LastMs;
        }

        void UpdateDraw()
        {
            var cam = Camera.main; if (!cam || buckets.Count == 0) return;
            if (trisFull == null)
            {
                trisFull = new int[meshes.Length]; trisImp = new int[meshes.Length];
                for (int p = 0; p < meshes.Length; p++) { trisFull[p] = meshes[p] ? Tris(meshes[p]) : 0; trisImp[p] = impMeshes != null && p < impMeshes.Length && impMeshes[p] ? Tris(impMeshes[p]) : 0; }
            }
            System.Array.Clear(statFull, 0, statFull.Length); System.Array.Clear(statImp, 0, statImp.Length);
            System.Array.Clear(statFullSh, 0, statFullSh.Length); System.Array.Clear(statImpSh, 0, statImpSh.Length);
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 cp = cam.transform.position; int drawn = 0; poolUsed = 0;
            if (!sun || !sun.isActiveAndEnabled) { sun = RenderSettings.sun; if (!sun) foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional) { sun = l; break; } }
            sunFwd = sun ? sun.transform.forward : Vector3.down;
            float k = Mathf.Max(.2f, Distance), shD = shadowDistance * k, impShD = impShadowDistance * k;
            float shMax = Mathf.Max(shD, impShD); bool fix = !NoFix && !Legacy;
            foreach (var bk in buckets)
            {
                float dc = Vector3.Distance(new Vector3(cp.x, 0, cp.z), new Vector3(bk.centre.x, 0, bk.centre.z));
                float d = dc - CELL * .71f, dmax = dc + CELL * .71f;
                if (d > 1400 * k) continue;
                bool view = GeometryUtility.TestPlanesAABB(planes, bk.bounds);
                if (!view && !(fix && d < shMax && ShadowCellInView(bk.bounds))) continue; // off screen: only if its shadows may fall into view
                for (int p = 0; p < meshes.Length; p++)
                {
                    var arr = bk.arr[p]; if (arr.Length == 0) continue;
                    bool hasImp = impMeshes != null && p < impMeshes.Length && impMeshes[p];
                    if (Legacy) { if (d > drawDistance[p]) continue; DrawLegacy(bk, p, arr, d, hasImp); drawn += arr.Length; continue; }
                    float draw = drawDistance[p] * k; if (d > draw) continue;
                    float near = hasImp ? nearDistance[p] * k : 1e9f;
                    bool impSh = d < impShD;
                    if (d > near + (fix ? HYST : 0) && dmax <= draw - (fix ? Mathf.Min(draw * FADE, FADE_MAX) : 0))
                    {
                        if (!view) { if (impSh) Submit(impMaterials[p], impMeshes[p], 0, arr, arr.Length, bk.bounds, ShadowCastingMode.ShadowsOnly); continue; }
                        Submit(impMaterials[p], impMeshes[p], 0, arr, arr.Length, bk.bounds, impSh ? ShadowCastingMode.On : ShadowCastingMode.Off); Stat(p, false, arr.Length, impSh); drawn += arr.Length;
                        continue;
                    }
                    drawn += fix ? Split2(p, bk, arr, cp, draw, near, shD, impSh, view, hasImp) : Split(p, arr, cp, draw, near, shD, impSh);
                }
            }
            DrawnLastFrame = drawn;
            long tm = 0, ts = 0;
            for (int p = 0; p < meshes.Length; p++) { tm += (long)statFull[p] * trisFull[p] + (long)statImp[p] * trisImp[p]; ts += (long)statFullSh[p] * trisFull[p] + (long)statImpSh[p] * trisImp[p]; }
            TrisMain = tm; TrisShadow = ts;
        }

        void Stat(int p, bool full, int n, bool sh) { if (full) { statFull[p] += n; if (sh) statFullSh[p] += n; } else { statImp[p] += n; if (sh) statImpSh[p] += n; } }

        // lists: 0 full + shadow, 1 full (no shadow), 2 full shadow only (outside the view), 3 impostor
        readonly List3[] L = new List3[5];

        int Split(int p, Matrix4x4[] arr, Vector3 cp, float draw, float near, float shD, bool impShadow)
        {
            var mb = meshes[p].bounds; float rad = mb.extents.magnitude, cy = mb.center.y;
            var eb = mb.extents; float ex = Mathf.Max(eb.x, eb.z);
            float draw2 = draw * draw, near2 = near * near, sh2 = shD * shD; int drawn = 0;
            for (int i = 0; i < 4; i++) L[i].n = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                ref var m = ref arr[i];
                float dx = m.m03 - cp.x, dz = m.m23 - cp.z, dd = dx * dx + dz * dz;
                if (dd > draw2) continue;
                float s = Mathf.Sqrt(m.m00 * m.m00 + m.m20 * m.m20);
                int li;
                if (dd > near2) li = 3;
                else
                {
                    var c = new Vector3(m.m03, m.m13 + cy * s, m.m23); float r = rad * s; bool vis = InView(c, r);
                    bool sh = dd < sh2;
                    if (vis) li = sh ? 0 : 1;
                    else if (sh && ShadowInView(c, r, (cy + eb.y) * s)) li = 2; // off screen, but its shadow may fall into view
                    else continue;
                }
                ref var l = ref L[li];
                var lo = new Vector3(m.m03 - ex * s, m.m13 + (cy - eb.y) * s, m.m23 - ex * s); var hi = new Vector3(m.m03 + ex * s, m.m13 + (cy + eb.y) * s, m.m23 + ex * s);
                if (l.n == 0) { l.a = Take(); l.lo = lo; l.hi = hi; } else { l.lo = Vector3.Min(l.lo, lo); l.hi = Vector3.Max(l.hi, hi); }
                l.a[l.n++] = m;
                if (l.n == CHUNK) drawn += Flush(li, p, impShadow);
            }
            for (int li = 0; li < 4; li++) if (L[li].n > 0) drawn += Flush(li, p, impShadow);
            return drawn;
        }

        /// per plant with hysteresis and grow-in; lists 0..3 as in Split, 4 = impostor into the shadow map only
        /// (off-screen impostors, and full meshes past the full-mesh shadow distance so their shadow does not vanish)
        int Split2(int p, Bucket bk, Matrix4x4[] arr, Vector3 cp, float draw, float near, float shD, bool impShadow, bool view, bool hasImp)
        {
            var mb = meshes[p].bounds; float rad = mb.extents.magnitude, cy = mb.center.y;
            var eb = mb.extents; float ex = Mathf.Max(eb.x, eb.z);
            float draw2 = draw * draw, fade = draw - Mathf.Min(draw * FADE, FADE_MAX), fade2 = fade * fade;
            float nIn = Mathf.Max(0, near - HYST), nOut = near + HYST, sIn = Mathf.Max(0, shD - HYST), sOut = shD + HYST;
            float nIn2 = nIn * nIn, nOut2 = nOut * nOut, sIn2 = sIn * sIn, sOut2 = sOut * sOut;
            int drawn = 0, b0 = bk.start[p];
            for (int i = 0; i < 5; i++) L[i].n = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                ref var m = ref arr[i];
                float dx = m.m03 - cp.x, dz = m.m23 - cp.z, dd = dx * dx + dz * dz;
                if (dd > draw2) continue;
                ref byte st = ref state[b0 + i];
                bool full = !hasImp || dd < nIn2 || (dd < nOut2 && (st & 1) != 0);
                bool fsh = dd < sIn2 || (dd < sOut2 && (st & 2) != 0);
                st = (byte)((full ? 1 : 0) | (fsh ? 2 : 0));
                float s = Mathf.Sqrt(m.m00 * m.m00 + m.m20 * m.m20), g = 1;
                if (dd > fade2) { g = Mathf.Clamp01((draw - Mathf.Sqrt(dd)) / (draw - fade)); g = g * g * (3 - 2 * g); if (g < .02f) continue; }
                var c = new Vector3(m.m03, m.m13 + cy * s * g, m.m23); float r = rad * s * g;
                int li, li2 = -1;
                if (!full)
                {
                    if (view) li = 3;
                    else if (impShadow && ShadowInView(c, r, (cy + eb.y) * s * g)) li = 4;
                    else continue;
                }
                else
                {
                    bool vis = view && InView(c, r);
                    if (vis) { li = fsh ? 0 : 1; if (!fsh && impShadow && hasImp) li2 = 4; }
                    else if ((fsh || (impShadow && hasImp)) && ShadowInView(c, r, (cy + eb.y) * s * g)) li = fsh ? 2 : 4; // off screen, but its shadow may fall into view
                    else continue;
                }
                var mm = m;
                if (g < 1) { mm.m00 *= g; mm.m01 *= g; mm.m02 *= g; mm.m10 *= g; mm.m11 *= g; mm.m12 *= g; mm.m20 *= g; mm.m21 *= g; mm.m22 *= g; }
                float gs = s * g;
                var lo = new Vector3(m.m03 - ex * gs, m.m13 + (cy - eb.y) * gs, m.m23 - ex * gs); var hi = new Vector3(m.m03 + ex * gs, m.m13 + (cy + eb.y) * gs, m.m23 + ex * gs);
                for (int pass = 0; pass < 2; pass++)
                {
                    int lj = pass == 0 ? li : li2; if (lj < 0) break;
                    ref var l = ref L[lj];
                    if (l.n == 0) { l.a = Take(); l.lo = lo; l.hi = hi; } else { l.lo = Vector3.Min(l.lo, lo); l.hi = Vector3.Max(l.hi, hi); }
                    l.a[l.n++] = mm;
                    if (l.n == CHUNK) drawn += Flush(lj, p, impShadow);
                }
            }
            for (int li = 0; li < 5; li++) if (L[li].n > 0) drawn += Flush(li, p, impShadow);
            return drawn;
        }

        /// the cell's shadow volume (its box swept along the sun down to the ground) touches the view
        bool ShadowCellInView(Bounds b)
        {
            if (sunFwd.y > -.05f) return true;
            var off = sunFwd * (b.size.y / -sunFwd.y); if (off.sqrMagnitude > 120 * 120) off = off.normalized * 120;
            var sb = b; sb.Encapsulate(new Bounds(b.center + off, b.size));
            return GeometryUtility.TestPlanesAABB(planes, sb);
        }

        Light sun; Vector3 sunFwd = Vector3.down;
        bool InView(Vector3 c, float r) { for (int q = 0; q < 6; q++) if (planes[q].GetDistanceToPoint(c) < -r) return false; return true; }
        /// the plant's shadow (from its centre down to where its top's shadow lands) touches the view
        bool ShadowInView(Vector3 c, float r, float top)
        {
            if (sunFwd.y > -.05f) return true;
            var end = c + sunFwd * (top / -sunFwd.y);
            return InView(end, r) || InView((c + end) * .5f, r + (end - c).magnitude * .25f);
        }

        int Flush(int li, int p, bool impShadow)
        {
            ref var l = ref L[li]; int n = l.n; l.n = 0;
            var b = new Bounds((l.lo + l.hi) * .5f, l.hi - l.lo);
            if (li == 4) { if (impShadow) Submit(impMaterials[p], impMeshes[p], 0, l.a, n, b, ShadowCastingMode.ShadowsOnly); statImpSh[p] += n; return 0; }
            if (li == 3) { Submit(impMaterials[p], impMeshes[p], 0, l.a, n, b, impShadow ? ShadowCastingMode.On : ShadowCastingMode.Off); Stat(p, false, n, impShadow); return n; }
            var mode = li == 0 ? ShadowCastingMode.On : li == 1 ? ShadowCastingMode.Off : ShadowCastingMode.ShadowsOnly;
            var mesh = meshes[p];
            for (int sm = 0; sm < mesh.subMeshCount; sm++) Submit(materials[materialStart[p] + sm], mesh, sm, l.a, n, b, mode);
            if (li == 2) { statFullSh[p] += n; return 0; }
            Stat(p, true, n, li == 0); return n;
        }

        void Submit(Material mat, Mesh mesh, int sm, Matrix4x4[] a, int n, Bounds b, ShadowCastingMode mode)
        {
            if (Skip != 0)
            {
                bool imp = impMeshes != null && System.Array.IndexOf(impMeshes, mesh) >= 0;
                bool sh = mode != ShadowCastingMode.Off, main = mode != ShadowCastingMode.ShadowsOnly;
                if (sh && (Skip & (imp ? 2 : 8)) != 0) { if (!main) return; mode = ShadowCastingMode.Off; }
                if (main && (Skip & (imp ? 1 : 4)) != 0) { if (mode == ShadowCastingMode.Off) return; mode = ShadowCastingMode.ShadowsOnly; }
            }
            var rp = new RenderParams(mat) { shadowCastingMode = mode, receiveShadows = true, worldBounds = b, layer = gameObject.layer };
            for (int i = 0; i < n; i += CHUNK) Graphics.RenderMeshInstanced(rp, mesh, sm, a, Mathf.Min(CHUNK, n - i), i);
        }

        void DrawLegacy(Bucket bk, int p, Matrix4x4[] arr, float d, bool hasImp)
        {
            if (d > nearDistance[p] && hasImp)
            {
                bool sh = d < impShadowDistance;
                Submit(impMaterials[p], impMeshes[p], 0, arr, arr.Length, bk.bounds, sh ? ShadowCastingMode.On : ShadowCastingMode.Off); Stat(p, false, arr.Length, sh); return;
            }
            bool shadows = d < shadowDistance;
            var mesh = meshes[p];
            for (int sm = 0; sm < mesh.subMeshCount; sm++) Submit(materials[materialStart[p] + sm], mesh, sm, arr, arr.Length, bk.bounds, shadows ? ShadowCastingMode.On : ShadowCastingMode.Off);
            Stat(p, true, arr.Length, shadows);
        }
    }
}
