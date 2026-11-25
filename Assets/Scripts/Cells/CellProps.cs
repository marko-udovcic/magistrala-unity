// Props and nature from the cell blobs (streaming phase 2): the prefab instances "Rekviziti: postavi" places in World/Props (Tripo /
// Poly Haven props: pallets, crates, benches, pots, pumps, the horseman, the old olive …) and "Priroda: postavi" in World/Nature
// (NatureManufacture limestone cliffs) drawn instanced (Graphics.RenderMeshInstanced) per prototype part, no GameObject per
// instance. CellBake turns every instance into (prototype, matrix): a prototype = the instance's renderers (mesh, submesh,
// material, matrix relative to the root, shadow mode, layer) by LOD level of its LODGroup.
// LOD as Unity's LODGroup with fade mode none: the level whose screen-relative transition height the group's screen height
// (size / (2 d tan(fov / 2)) × lodBias, from the camera to the group's reference point) reaches; culled below the last.
// (The cliffs' LODGroups cross-faded over 0.5 s; here the switch is immediate - the only visual difference.)
// Per frame: the instances of the pages within reach, a sphere test against the view; off-screen instances within the shadow
// distance whose shadow may fall into the view are drawn into the shadow map only. Colliders: CellColliders.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Magistrala.Cells
{
    [DefaultExecutionOrder(10001)]
    public class CellProps : MonoBehaviour, ICellLayer
    {
        /// measuring only: -props 0 = not drawn
        public static bool Off;
        public int Count { get; private set; }
        public int DrawnLastFrame { get; private set; }
        public static int Calls;

        PropProto[] protos;
        /// decode snapshot per prototype: LOD reference point / size, bounds centre / radius (root frame)
        Vector3[] pRef, pBc; float[] pSize, pBr;
        /// per prototype: its parts' slot index base; per part: its slot (main list) — a slot = one (mesh, submesh, material) draw
        int[] slot0; int nSlots;
        sealed class Page { public int n; public int[] proto; public Matrix4x4[] M; public Vector3[] refW, bc; public float[] sizeW, br; public Bounds b; public float reach, maxDist; }
        /// per prototype: 1 / its last LOD height (0 = no LODGroup: drawn to maxDist)
        float[] pInvLast, pMax;
        readonly List<Page> pages = new List<Page>();
        struct List3 { public Matrix4x4[] a; public int n; public Vector3 lo, hi; }
        List3[] main, shadow;
        readonly Plane[] planes = new Plane[6];
        Light sun; Vector3 sunFwd = Vector3.down;
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        const int CHUNK = 511;

        public void Prepare(CellDataManifest m)
        {
            foreach (var a in System.Environment.GetCommandLineArgs()) if (a == "-props0") Off = true;
            protos = m.props; int np = protos.Length;
            pRef = new Vector3[np]; pBc = new Vector3[np]; pSize = new float[np]; pBr = new float[np]; slot0 = new int[np + 1]; pInvLast = new float[np]; pMax = new float[np];
            for (int p = 0; p < np; p++)
            {
                var pr = protos[p]; pInvLast[p] = pr.lodHeights.Length > 0 && pr.lodHeights[pr.lodHeights.Length - 1] > 0 ? 1 / pr.lodHeights[pr.lodHeights.Length - 1] : 0; pMax[p] = pr.maxDist; pRef[p] = pr.lodRef; pSize[p] = pr.lodSize; pBc[p] = pr.bounds.center; pBr[p] = pr.bounds.extents.magnitude;
                slot0[p] = nSlots; nSlots += pr.parts.Length;
                foreach (var part in pr.parts) if (part.mat) part.mat.enableInstancing = true;
            }
            slot0[np] = nSlots;
            main = new List3[nSlots]; shadow = new List3[nSlots];
            for (int i = 0; i < nSlots; i++) { main[i].a = new Matrix4x4[16]; shadow[i].a = new Matrix4x4[16]; }
        }

        static float MaxScale(in Matrix4x4 m)
        {
            float a = m.m00 * m.m00 + m.m10 * m.m10 + m.m20 * m.m20, b = m.m01 * m.m01 + m.m11 * m.m11 + m.m21 * m.m21, c = m.m02 * m.m02 + m.m12 * m.m12 + m.m22 * m.m22;
            return Mathf.Sqrt(Mathf.Max(a, Mathf.Max(b, c)));
        }

        public object Decode(CellSections s)
        {
            int o = s.Find(CellBlob.PROP, out int n); if (o < 0 || n == 0) return null;
            var pg = new Page { n = n, proto = new int[n], M = new Matrix4x4[n], refW = new Vector3[n], bc = new Vector3[n], sizeW = new float[n], br = new float[n] };
            bool first = true; int w = 0;
            for (int k = 0; k < n; k++)
            {
                int r = o + k * CellBlob.PROP_B, p = CellBlob.I(s.b, r); if (p < 0 || p >= pRef.Length) continue;
                var m = CellBlob.M34(s.b, r + 4); float sc = MaxScale(m);
                pg.proto[w] = p; pg.M[w] = m; pg.refW[w] = m.MultiplyPoint3x4(pRef[p]); pg.sizeW[w] = pSize[p] * sc;
                pg.bc[w] = m.MultiplyPoint3x4(pBc[p]); pg.br[w] = pBr[p] * sc;
                // the farthest this instance can be drawn: size / last LOD height (× the per-frame k), or maxDist without LODs
                if (pInvLast[p] > 0) pg.reach = Mathf.Max(pg.reach, pg.sizeW[w] * pInvLast[p]); else pg.maxDist = Mathf.Max(pg.maxDist, pMax[p]);
                var bb = new Bounds(pg.bc[w], Vector3.one * pg.br[w] * 2);
                if (first) { pg.b = bb; first = false; } else pg.b.Encapsulate(bb);
                w++;
            }
            pg.n = w; return w == 0 ? null : pg;
        }
        public void Attach(Vector2Int cell, object page) { var pg = (Page)page; pages.Add(pg); Count += pg.n; }
        public void Detach(Vector2Int cell, object page) { var pg = (Page)page; if (pages.Remove(pg)) Count -= pg.n; }
        public void Release(object page) { }
        // phase-3 scheduler (ICellDataSystem names): this system's part of a cell (CellData decodes the cell once for all systems)
        bool Mine => CellData.I && CellData.I.props == this;
        public void LoadCell(Vector2Int c) { if (Mine) CellData.Load(c, 1 << CellBlob.PROPS); }
        public void UnloadCell(Vector2Int c) { if (Mine) CellData.Unload(c, 1 << CellBlob.PROPS); }
        public bool IsLoaded(Vector2Int c) => !Mine || CellData.IsLoaded(c, 1 << CellBlob.PROPS);


        bool InView(Vector3 c, float r) { for (int q = 0; q < 6; q++) if (planes[q].GetDistanceToPoint(c) < -r) return false; return true; }
        bool ShadowInView(Vector3 c, float r)
        {
            if (sunFwd.y > -.05f) return true;
            var end = c + sunFwd * (r * 2 / -sunFwd.y);
            return InView(end, r) || InView((c + end) * .5f, r + (end - c).magnitude * .25f);
        }

        void LateUpdate()
        {
            sw.Restart(); Draw(); sw.Stop(); CellStats.Add(CellStats.PROPS, (float)sw.Elapsed.TotalMilliseconds);
        }

        void Add(ref List3 l, in Matrix4x4 m, Vector3 c, float r)
        {
            if (l.n == l.a.Length) System.Array.Resize(ref l.a, l.a.Length * 2);
            var lo = c - new Vector3(r, r, r); var hi = c + new Vector3(r, r, r);
            if (l.n == 0) { l.lo = lo; l.hi = hi; } else { l.lo = Vector3.Min(l.lo, lo); l.hi = Vector3.Max(l.hi, hi); }
            l.a[l.n++] = m;
        }

        void Draw()
        {
            DrawnLastFrame = 0; Calls = 0;
            if (Off || protos == null || pages.Count == 0) return;
            var cam = Camera.main; if (!cam) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            if (!sun || !sun.isActiveAndEnabled) sun = RenderSettings.sun;
            sunFwd = sun ? sun.transform.forward : Vector3.down;
            var cp = cam.transform.position;
            float tanHalf = Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad), bias = QualitySettings.lodBias; int maxLod = QualitySettings.maximumLODLevel;
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            float shD = urp ? urp.shadowDistance : 150, far = cam.farClipPlane;
            // relative screen height per metre of size at distance d: 1 / (2 d tan(fov / 2)) × bias
            float k = bias / (2 * tanHalf);
            for (int i = 0; i < nSlots; i++) { main[i].n = 0; shadow[i].n = 0; }
            for (int pi = 0; pi < pages.Count; pi++)
            {
                var pg = pages[pi];
                float pd = Mathf.Sqrt(pg.b.SqrDistance(cp));
                if (pd > far || pd > Mathf.Max(pg.reach * k, pg.maxDist)) continue; // nothing of this page is drawn that far
                for (int i = 0; i < pg.n; i++)
                {
                    int p = pg.proto[i]; var pr = protos[p];
                    float d = Vector3.Distance(cp, pg.refW[i]);
                    int level = -1; var h = pr.lodHeights;
                    if (h.Length > 0)
                    {
                        float rel = d > 1e-4f ? pg.sizeW[i] * k / d : 1e9f;
                        for (int l = 0; l < h.Length; l++) if (rel >= h[l]) { level = l; break; }
                        if (level < 0) continue; // culled
                        if (level < maxLod) level = Mathf.Min(maxLod, h.Length - 1);
                    }
                    else if (d > pr.maxDist) continue;
                    var c = pg.bc[i]; float r = pg.br[i];
                    bool vis = InView(c, r);
                    bool sh = !vis && d - r < shD && ShadowInView(c, r);
                    if (!vis && !sh) continue;
                    if (vis) DrawnLastFrame++;
                    var parts = pr.parts; int s0 = slot0[p];
                    for (int q = 0; q < parts.Length; q++)
                    {
                        var part = parts[q];
                        if (part.level >= 0 && part.level != level) continue;
                        if (!vis && part.shadow == ShadowCastingMode.Off) continue;
                        var m = pg.M[i] * part.local;
                        if (vis) Add(ref main[s0 + q], m, c, r); else Add(ref shadow[s0 + q], m, c, r);
                    }
                }
            }
            for (int p = 0; p < protos.Length; p++)
            {
                var parts = protos[p].parts;
                for (int q = 0; q < parts.Length; q++)
                {
                    var part = parts[q]; if (!part.mat || !part.mesh) continue;
                    int si = slot0[p] + q;
                    Submit(ref main[si], part, part.shadow);
                    Submit(ref shadow[si], part, ShadowCastingMode.ShadowsOnly);
                }
            }
        }

        void Submit(ref List3 l, PropPart part, ShadowCastingMode mode)
        {
            if (l.n == 0) return;
            var rp = new RenderParams(part.mat)
            {
                shadowCastingMode = mode, receiveShadows = part.receive, layer = part.layer, worldBounds = new Bounds((l.lo + l.hi) * .5f, l.hi - l.lo),
                lightProbeUsage = part.lightProbes == LightProbeUsage.Off ? LightProbeUsage.Off : LightProbeUsage.BlendProbes,
                reflectionProbeUsage = part.reflProbes,
            };
            for (int i = 0; i < l.n; i += CHUNK) { Graphics.RenderMeshInstanced(rp, part.mesh, part.submesh, l.a, Mathf.Min(CHUNK, l.n - i), i); Calls++; }
        }
    }
}
