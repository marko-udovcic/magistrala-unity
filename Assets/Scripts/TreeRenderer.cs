using System.Collections.Generic;
using Magistrala.Cells;
using Magistrala.Streaming;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Magistrala
{
    /// Instanced vegetation: the importer writes all plants as binary records (x, y, z, yaw, scale, proto) into `data`;
    /// they are bucketed per 128 m cell and prototype; each frame the cells near the camera are drawn with
    /// Graphics.RenderMeshInstanced. Far cutoff per prototype (shrubs / rocks short).
    /// Pages (streaming phase 2, Assets/Scripts/Cells/CellData.cs): the plants of one CellGrid cell are one page (one matrix array:
    /// its 128 m buckets and 256 m blocks are ranges of it, plus the per-plant hysteresis state); a page is attached / detached
    /// without touching the others. `data` set (the AC units, an editor scene): split into pages at start, all attached. In a
    /// player / play mode where CellBake took trees.bytes out, the pages come from the cell blobs (CellData, ICellLayer).
    /// Cells that straddle a LOD / shadow / draw distance are split per plant (a 128 m cell is too coarse: its nearest
    /// corner decided for the whole cell, so full meshes and their shadows reached ~200 m): full mesh within nearDistance,
    /// impostor (crossed quads) beyond; full-mesh shadows only within shadowDistance; near full meshes outside the view
    /// are drawn into the shadow map only (or not at all). Each split list gets its own tight bounds, so it touches fewer
    /// shadow cascades. `Distance` scales all vegetation distances (quality level).
    /// Smoothness: runs after the chase camera (LateUpdate, late execution order), so culling uses this frame's camera, not
    /// the last one; full / impostor and full-mesh shadow switch with hysteresis (per-plant state); between the full-mesh
    /// shadow distance and the impostor distance the plant casts its impostor's shadow (no shadow-less ring); cells just
    /// outside the view still cast shadows that fall into it; plants grow in over the last part of their draw distance.
    /// Fades (25. 11., Marko: "stabla se čudno učitavaju"): where the materials are Magistrala/Foliage (our plants, the crnika, all
    /// their impostors) the full mesh and the impostor cross-fade with a dither over a band before the switch distance (both
    /// drawn there), impostors dither in / out over the last part of the draw distance instead of growing out of the ground, and
    /// the leaves' shadows dither out before the full-mesh shadow distance (the small plants' 30 m). The shader takes the bands
    /// per draw from a property block and the distance from the instance's origin, so no per-instance data. Plants on other
    /// shaders (package pines, oleanders, hedges) keep the switch with hysteresis and the grow-in.
    [DefaultExecutionOrder(10000)]
    public class TreeRenderer : MonoBehaviour, ICellLayer
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

        /// a 128 m bucket: per prototype a range [off, off + cnt) of the page's matrices (and of its state)
        class Bucket { public Bounds bounds; public Vector3 centre; public Matrix4x4[] m; public byte[] st; public int[] off, cnt; public Block block; public Page page; }
        /// 256 m blocks of 2 x 2 cells: where a whole block is impostor-only and past the impostor shadow distance, one draw per
        /// prototype for the block instead of one per cell (22. 11.: ~2 k impostor draws a frame at oštro, -15 ms GPU without them).
        /// Its matrices: a second range of the page's array (impostor prototypes only); done[p]: drawn as a block this frame
        class Block { public Bounds bounds; public Vector3 centre; public Matrix4x4[] m; public int[] off, cnt; public bool[] done; public Page page; }
        /// one CellGrid cell's plants
        class Page { public Vector2Int cell; public Matrix4x4[] m; public byte[] st; public Bucket[] buckets; public Block[] blocks; public int count; }
        const float BLOCK = 256;
        readonly List<Block> blocks = new List<Block>();
        readonly List<Page> pages = new List<Page>();
        const float HYST = 4, FADE = .12f, FADE_MAX = 30; // m either side of a switch distance; plants grow in over the last 12 % of their draw distance, at most 30 m
        public static bool NoFix; // measuring only (-trees-nofix): no hysteresis / impostor shadow fill / shadow cells / grow-in
        /// measuring only (-trees-nofade): no dither fades (the switches and grow-in as before 25. 11.)
        public static bool NoFade;
        /// per prototype (Start): xfade = full mesh ↔ impostor cross-fade (leaves and impostor on Foliage); impDither = the impostor
        /// fades out by dither (else it shrinks); fullDither = every full-mesh submesh on Foliage (fades out by dither, else shrinks)
        bool[] xfade, impDither, fullDither;
        MaterialPropertyBlock[] mpbFull, mpbImp;
        const float XFADE = .10f, XFADE_MIN = 3, SHFADE = .15f, SHFADE_MIN = 3; // band widths: share of the switch distance, at least m (cross-fade band 15 % -> 10 %: the plants in it draw twice)
        static readonly int ID_FadeIn = Shader.PropertyToID("_FadeIn"), ID_FadeSh = Shader.PropertyToID("_FadeSh"), ID_FadeFrame = Shader.PropertyToID("_VegFadeFrame");
        /// per prototype (Start): shadow distance and far cutoff caps of the small plants; NoCaps (-trees-nocaps, measuring only) = off
        float[] shadowCap, drawCap; const float SMALL_SHADOW = 30;
        public static bool NoCaps, ImpOwnShadow;
        /// measuring only (-trees-near k): full mesh / impostor switch distances × k (100 = all full meshes, .01 = all impostors:
        /// screenshots of both from one camera pose show whether the impostor matches the mesh)
        public static float NearScale = 1;
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

        static bool argsRead;
        static void ReadArgs(TreeRenderer t)
        {
            var args = System.Environment.GetCommandLineArgs(); // measuring only
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-trees-blend" && t.blendMaterials != null && t.blendMaterials.Length == t.materials.Length) t.materials = t.blendMaterials;
                if (argsRead) continue;
                if (args[i] == "-trees-legacy") Legacy = true;
                if (args[i] == "-trees-nofix") NoFix = true;
                if (args[i] == "-trees-nofade") NoFade = true;
                if (args[i] == "-trees-stat") Stat10 = true;
                if (args[i] == "-trees-nocaps") NoCaps = true;
                if (args[i] == "-trees-impshadow") ImpOwnShadow = true; // measuring only: the old impostor-cast shadow inside the full-mesh shadow distance
                if (args[i] == "-trees-near" && i + 1 < args.Length) float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out NearScale);
                if (args[i] == "-trees-skip" && i + 1 < args.Length) foreach (var w in args[i + 1].Split(',')) Skip |= w == "imp" ? 1 : w == "impsh" ? 2 : w == "full" ? 4 : w == "fullsh" ? 8 : 0;
            }
            argsRead = true;
        }

        void Start()
        {
            if (!data || inited) return;
            // the whole file: mean scale per prototype, then one page per CellGrid cell, all attached
            var b = data.bytes; int n = b.Length / 24, np = meshes.Length;
            var sumS = new double[np]; var cnt = new int[np];
            for (int i = 0; i < n; i++)
            {
                int p = (int)System.BitConverter.ToSingle(b, i * 24 + 20);
                if (p < 0 || p >= np || !meshes[p]) continue;
                sumS[p] += System.BitConverter.ToSingle(b, i * 24 + 16); cnt[p]++;
            }
            var mean = new double[np]; for (int p = 0; p < np; p++) mean[p] = cnt[p] > 0 ? sumS[p] / cnt[p] : 0;
            Init(mean);
            var byCell = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                var c = CellGrid.Cell(new Vector3(System.BitConverter.ToSingle(b, i * 24), 0, System.BitConverter.ToSingle(b, i * 24 + 8)));
                if (!byCell.TryGetValue(c, out var l)) byCell[c] = l = new List<int>(); l.Add(i);
            }
            var tmp = new byte[0];
            foreach (var kv in byCell)
            {
                if (tmp.Length < kv.Value.Count * 24) tmp = new byte[kv.Value.Count * 24];
                for (int k = 0; k < kv.Value.Count; k++) System.Buffer.BlockCopy(b, kv.Value[k] * 24, tmp, k * 24, 24);
                var pg = Build(ctx, kv.Key, tmp, 0, kv.Value.Count);
                if (pg != null) Attach(kv.Key, pg);
            }
        }

        // ------------------------------------------------------------------ pages
        /// what a decode needs, snapshotted on the main thread (Init)
        sealed class Ctx { public int np; public bool[] valid, imp; public Vector3[] bc, bs; }
        Ctx ctx; bool inited;

        /// prototypes' per-plant constants (once; the mean scale per prototype over the whole map)
        void Init(double[] meanScale)
        {
            if (inited) return; inited = true;
            ReadArgs(this);
            int np = meshes.Length;
            ctx = new Ctx { np = np, valid = new bool[np], imp = new bool[np], bc = new Vector3[np], bs = new Vector3[np] };
            for (int p = 0; p < np; p++)
            {
                ctx.valid[p] = meshes[p]; if (!meshes[p]) continue;
                ctx.bc[p] = meshes[p].bounds.center; ctx.bs[p] = meshes[p].bounds.size;
                ctx.imp[p] = impMeshes != null && p < impMeshes.Length && impMeshes[p];
            }
            // a checkout without the generated clip materials (Assets/Generated is not in git): make them here from the originals
            if (blendMaterials != null && blendMaterials.Length == materials.Length)
                for (int i = 0; i < materials.Length; i++) if (!materials[i] && blendMaterials[i]) materials[i] = Clip(blendMaterials[i]);
            foreach (var m in materials) if (m) m.enableInstancing = true;
            // small plants (bushes, hedges, oleanders: under 3 m as placed): shadows only near the camera and a shorter far cutoff
            // (a 1.5 m bush is ~5 px tall at 160 m); yard-sized trees (olive, crnika: under 8 m) end at 650 m, where they are
            // 2–3 px and only overdraw (the Kaštel Stari trial drew ~5.5 k of their impostors a frame); tall trees keep theirs
            shadowCap = new float[np]; drawCap = new float[np];
            for (int p = 0; p < np; p++)
            {
                float h = meshes[p] && p < meanScale.Length && meanScale[p] > 0 ? meshes[p].bounds.size.y * (float)meanScale[p] : 10;
                bool small = h < 3 && !NoCaps;
                shadowCap[p] = small ? SMALL_SHADOW : 1e9f; drawCap[p] = NoCaps ? 1e9f : small ? 100 + 40 * h : h < 8 ? 650 : 1e9f;
            }
            statFull = new int[np]; statImp = new int[np]; statFullSh = new int[np]; statImpSh = new int[np];
            xfade = new bool[np]; impDither = new bool[np]; fullDither = new bool[np]; mpbFull = new MaterialPropertyBlock[np]; mpbImp = new MaterialPropertyBlock[np];
            for (int p = 0; p < np; p++)
            {
                mpbFull[p] = new MaterialPropertyBlock(); mpbImp[p] = new MaterialPropertyBlock();
                if (NoFade || !meshes[p]) continue;
                bool any = false, all = true;
                for (int sm = 0; sm < meshes[p].subMeshCount; sm++) { bool f = IsFoliage(materialStart[p] + sm < materials.Length ? materials[materialStart[p] + sm] : null); any |= f; all &= f; }
                impDither[p] = impMeshes != null && p < impMeshes.Length && impMeshes[p] && p < impMaterials.Length && IsFoliage(impMaterials[p]);
                xfade[p] = impDither[p] && any; fullDither[p] = all;
            }
        }

        // ICellLayer (CellData): the TREE section of a cell blob
        public void Prepare(CellManifest m) => Init(m.treeMeanScale);
        public object Decode(CellSections s) { int o = s.Find(CellBlob.TREE, out int n); return o < 0 || n == 0 ? null : Build(ctx, s.cell, s.b, o, n); }
        public void Attach(Vector2Int cell, object page)
        {
            var pg = (Page)page; pages.Add(pg);
            buckets.AddRange(pg.buckets); blocks.AddRange(pg.blocks); Count += pg.count;
        }
        public void Detach(Vector2Int cell, object page)
        {
            var pg = (Page)page; if (!pages.Remove(pg)) return;
            int w = 0; for (int i = 0; i < buckets.Count; i++) if (buckets[i].page != pg) buckets[w++] = buckets[i]; buckets.RemoveRange(w, buckets.Count - w);
            w = 0; for (int i = 0; i < blocks.Count; i++) if (blocks[i].page != pg) blocks[w++] = blocks[i]; blocks.RemoveRange(w, blocks.Count - w);
            Count -= pg.count;
        }
        // phase-3 scheduler (ICellDataSystem names): this system's part of a cell (CellData decodes the cell once for all systems)
        bool Mine => CellData.I && CellData.I.trees == this;
        public void LoadCell(Vector2Int c) { if (Mine) CellData.Load(c, 1 << CellBlob.TREES); }
        public void UnloadCell(Vector2Int c) { if (Mine) CellData.Unload(c, 1 << CellBlob.TREES); }
        public bool IsLoaded(Vector2Int c) => !Mine || CellData.IsLoaded(c, 1 << CellBlob.TREES);

        public void Release(object page) { var pg = (Page)page; ArrayPool<Matrix4x4>.Return(pg.m); ArrayPool<byte>.Return(pg.st); pg.m = null; pg.st = null; }

        /// one page from n plant records at b[o..] (any thread: plain math only). Buckets 128 m on the world grid (a bucket at a cell
        /// border is cut in two: one per page), blocks 256 m of buckets; within a bucket the plants by prototype, in file order.
        static Page Build(Ctx c, Vector2Int cell, byte[] b, int o, int n)
        {
            int np = c.np;
            var bIdx = new Dictionary<long, int>(); var bk = new List<(int bx, int bz, float y)>();
            var pb = ArrayPool<int>.Rent(n);  // per plant its bucket (-1 = invalid prototype)
            for (int i = 0; i < n; i++)
            {
                int r = o + i * 24; int p = (int)CellBlob.F(b, r + 20);
                if (p < 0 || p >= np || !c.valid[p]) { pb[i] = -1; continue; }
                float x = CellBlob.F(b, r), z = CellBlob.F(b, r + 8);
                int bx = Mathf.FloorToInt(x / CELL), bz = Mathf.FloorToInt(z / CELL); long key = ((long)bx << 32) ^ (uint)bz;
                if (!bIdx.TryGetValue(key, out int k)) { k = bk.Count; bIdx[key] = k; bk.Add((bx, bz, CellBlob.F(b, r + 4))); }
                pb[i] = k;
            }
            int nb = bk.Count; if (nb == 0) { ArrayPool<int>.Return(pb); return null; }
            // counts per bucket × prototype -> ranges (bucket-major)
            var cnt = new int[nb * np];
            for (int i = 0; i < n; i++) if (pb[i] >= 0) cnt[pb[i] * np + (int)CellBlob.F(b, o + i * 24 + 20)]++;
            int total = 0; var off = new int[nb * np];
            for (int i = 0; i < cnt.Length; i++) { off[i] = total; total += cnt[i]; }
            // blocks: 2 x 2 buckets; their ranges after the buckets', impostor prototypes only
            var blIdx = new Dictionary<long, int>(); var blOf = new int[nb]; var blKids = new List<List<int>>();
            for (int k = 0; k < nb; k++)
            {
                long key = ((long)(bk[k].bx >> 1) << 32) ^ (uint)(bk[k].bz >> 1);
                if (!blIdx.TryGetValue(key, out int q)) { q = blKids.Count; blIdx[key] = q; blKids.Add(new List<int>()); }
                blOf[k] = q; blKids[q].Add(k);
            }
            int nbl = blKids.Count, totalB = 0; var blOff = new int[nbl * np]; var blCnt = new int[nbl * np];
            for (int q = 0; q < nbl; q++)
                for (int p = 0; p < np; p++)
                {
                    blOff[q * np + p] = total + totalB; if (!c.imp[p]) continue;
                    int s = 0; foreach (int k in blKids[q]) s += cnt[k * np + p];
                    blCnt[q * np + p] = s; totalB += s;
                }
            var pg = new Page { cell = cell, count = total, m = ArrayPool<Matrix4x4>.Rent(total + totalB), st = ArrayPool<byte>.Rent(total) };
            System.Array.Clear(pg.st, 0, total);
            pg.buckets = new Bucket[nb]; pg.blocks = new Block[nbl];
            for (int k = 0; k < nb; k++)
            {
                var ctr = new Vector3((bk[k].bx + .5f) * CELL, bk[k].y, (bk[k].bz + .5f) * CELL);
                var u = new Bucket { m = pg.m, st = pg.st, page = pg, off = new int[np], cnt = new int[np], bounds = new Bounds(ctr, new Vector3(CELL, 1, CELL)) };
                for (int p = 0; p < np; p++) { u.off[p] = off[k * np + p]; u.cnt[p] = 0; }
                pg.buckets[k] = u;
            }
            for (int i = 0; i < n; i++)
            {
                int k = pb[i]; if (k < 0) continue;
                int r = o + i * 24; var u = pg.buckets[k];
                var pos = new Vector3(CellBlob.F(b, r), CellBlob.F(b, r + 4), CellBlob.F(b, r + 8));
                float yaw = CellBlob.F(b, r + 12), s = CellBlob.F(b, r + 16); int p = (int)CellBlob.F(b, r + 20);
                pg.m[u.off[p] + u.cnt[p]++] = CellMath.TRSYaw(pos, yaw, s);
                u.bounds.Encapsulate(new Bounds(pos + Vector3.up * c.bc[p].y * s, c.bs[p] * s));
            }
            for (int k = 0; k < nb; k++) pg.buckets[k].centre = pg.buckets[k].bounds.center;
            for (int q = 0; q < nbl; q++)
            {
                var kids = blKids[q];
                var bl = new Block { m = pg.m, page = pg, off = new int[np], cnt = new int[np], done = new bool[np], bounds = pg.buckets[kids[0]].bounds };
                foreach (int k in kids) bl.bounds.Encapsulate(pg.buckets[k].bounds);
                for (int p = 0; p < np; p++)
                {
                    bl.off[p] = blOff[q * np + p]; bl.cnt[p] = blCnt[q * np + p];
                    int w = bl.off[p];
                    if (bl.cnt[p] > 0) foreach (int k in kids) { var u = pg.buckets[k]; System.Array.Copy(pg.m, u.off[p], pg.m, w, u.cnt[p]); w += u.cnt[p]; }
                }
                bl.centre = bl.bounds.center; pg.blocks[q] = bl;
                foreach (int k in kids) pg.buckets[k].block = bl;
            }
            ArrayPool<int>.Return(pb);
            return pg;
        }

        static bool IsFoliage(Material m) => m && m.shader && m.shader.name == "Magistrala/Foliage";

        /// this frame's fade bands into the property blocks (the quality level's distance scale may change)
        void SetFades(float k, float shD, bool on)
        {
            for (int p = 0; p < meshes.Length; p++)
            {
                bool hasImp = impMeshes != null && p < impMeshes.Length && impMeshes[p];
                float draw = Mathf.Min(drawDistance[p], drawCap[p]) * k, fade = draw - Mathf.Min(draw * FADE, FADE_MAX);
                float nOut = (hasImp ? nearDistance[p] * k * NearScale : 1e9f) + HYST, nA = nOut - Mathf.Max(XFADE_MIN, (nOut - HYST) * XFADE);
                float sIn = Mathf.Max(0, Mathf.Min(shD, shadowCap[p] * k) - HYST), sA = sIn - Mathf.Max(SHFADE_MIN, sIn * SHFADE);
                var off = new Vector2(0, 0);
                if (!on) { mpbFull[p].SetVector(ID_FadeIn, Vector4.zero); mpbFull[p].SetVector(ID_FadeSh, Vector4.zero); mpbImp[p].SetVector(ID_FadeIn, Vector4.zero); mpbImp[p].SetVector(ID_FadeSh, Vector4.zero); continue; }
                Vector2 fullOut = xfade[p] ? new Vector2(nA, nOut) : fullDither[p] && !hasImp ? new Vector2(fade, draw) : off;
                Vector2 impIn = xfade[p] ? new Vector2(nA, nOut) : off, impOut = impDither[p] ? new Vector2(fade, draw) : off;
                Vector2 sh = sA > 0 ? new Vector2(sA, sIn) : off;
                mpbFull[p].SetVector(ID_FadeIn, new Vector4(0, 0, fullOut.x, fullOut.y)); mpbFull[p].SetVector(ID_FadeSh, new Vector4(sh.x, sh.y, 0, 0));
                mpbImp[p].SetVector(ID_FadeIn, new Vector4(impIn.x, impIn.y, impOut.x, impOut.y)); mpbImp[p].SetVector(ID_FadeSh, Vector4.zero);
            }
            // TAA: a pattern that moves every frame resolves into a smooth fade; otherwise a still one (no crawling noise)
            var cd = cam ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
            Shader.SetGlobalFloat(ID_FadeFrame, cd && cd.antialiasing == AntialiasingMode.TemporalAntiAliasing ? Time.frameCount % 8 : 0);
        }
        Camera cam;

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
            CellStats.Add(CellStats.TREES, LastMs);
            if (Stat10) LogStats();
        }

        /// measuring only (-trees-stat): every 10 s per prototype the average instances a frame (full / impostor, and into the
        /// shadow maps) and the triangles, to see which plants cost
        public static bool Stat10;
        long[] sF, sI, sFS, sIS; int sN; float sT;
        void LogStats()
        {
            if (statFull == null) return;
            if (sF == null) { sF = new long[meshes.Length]; sI = new long[meshes.Length]; sFS = new long[meshes.Length]; sIS = new long[meshes.Length]; sT = Time.realtimeSinceStartup; }
            for (int p = 0; p < meshes.Length; p++) { sF[p] += statFull[p]; sI[p] += statImp[p]; sFS[p] += statFullSh[p]; sIS[p] += statImpSh[p]; }
            sN++; if (Time.realtimeSinceStartup - sT < 10 || trisFull == null) return;
            var sb = new System.Text.StringBuilder($"[trees] {name} t={Time.realtimeSinceStartup:F0}s per frame (full/imp/fullSh/impSh, ktris main+shadow):");
            for (int p = 0; p < meshes.Length; p++)
            {
                if (sF[p] + sI[p] + sFS[p] + sIS[p] == 0) continue;
                long kt = (sF[p] * trisFull[p] + sI[p] * trisImp[p] + sFS[p] * trisFull[p] + sIS[p] * trisImp[p]) / sN / 1000;
                sb.Append($" {(meshes[p] ? meshes[p].name : "?")} {sF[p] / sN}/{sI[p] / sN}/{sFS[p] / sN}/{sIS[p] / sN} {kt}k;");
            }
            Debug.Log(sb.ToString());
            System.Array.Clear(sF, 0, sF.Length); System.Array.Clear(sI, 0, sI.Length); System.Array.Clear(sFS, 0, sFS.Length); System.Array.Clear(sIS, 0, sIS.Length); sN = 0; sT = Time.realtimeSinceStartup;
        }

        void UpdateDraw()
        {
            cam = Camera.main; if (!cam || buckets.Count == 0 || !inited) return;
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
            // no impostor shadows past the full-mesh shadow distance (24. 11.: shadow-only screenshots with and without them are
            // the same – the crossed cards leave next to nothing in the shadow map – yet they cost ~60 shadow draws a frame and
            // kept the 256 m impostor blocks out to 260 m); -trees-impshadow (measuring only) brings them back
            if (!ImpOwnShadow) impShD = shD;
            float shMax = Mathf.Max(shD, impShD); bool fix = !NoFix && !Legacy;
            SetFades(k, shD, fix && !NoFade);
            for (int bi = 0; bi < blocks.Count; bi++) System.Array.Clear(blocks[bi].done, 0, blocks[bi].done.Length);
            if (fix && impMeshes != null)
                for (int bi = 0; bi < blocks.Count; bi++)
                {
                    var bl = blocks[bi];
                    float dc = Vector3.Distance(new Vector3(cp.x, 0, cp.z), new Vector3(bl.centre.x, 0, bl.centre.z));
                    float d = dc - BLOCK * .71f, dmax = dc + BLOCK * .71f;
                    if (d <= impShD || d > 1400 * k) continue; // shadows / per-plant switches near the camera: per cell below
                    bool view = GeometryUtility.TestPlanesAABB(planes, bl.bounds);
                    for (int p = 0; p < meshes.Length; p++)
                    {
                        if (p >= impMeshes.Length || !impMeshes[p]) continue;
                        float draw = Mathf.Min(drawDistance[p], drawCap[p]) * k;
                        if (d <= nearDistance[p] * k * NearScale + HYST || dmax > draw - Mathf.Min(draw * FADE, FADE_MAX)) continue; // straddles a switch: cells
                        bl.done[p] = true;
                        int n = bl.cnt[p]; if (!view || n == 0) continue; // no shadows this far: off screen = nothing
                        Submit(impMaterials[p], impMeshes[p], 0, bl.m, bl.off[p], n, bl.bounds, ShadowCastingMode.Off, mpbImp[p]); Stat(p, false, n, false); drawn += n;
                    }
                }
            for (int bi = 0; bi < buckets.Count; bi++)
            {
                var bk = buckets[bi];
                float dc = Vector3.Distance(new Vector3(cp.x, 0, cp.z), new Vector3(bk.centre.x, 0, bk.centre.z));
                float d = dc - CELL * .71f, dmax = dc + CELL * .71f;
                if (d > 1400 * k) continue;
                bool view = GeometryUtility.TestPlanesAABB(planes, bk.bounds);
                if (!view && !(fix && d < shMax && ShadowCellInView(bk.bounds))) continue; // off screen: only if its shadows may fall into view
                for (int p = 0; p < meshes.Length; p++)
                {
                    int n = bk.cnt[p], o = bk.off[p]; if (n == 0) continue;
                    if (fix && bk.block.done[p]) continue;
                    bool hasImp = impMeshes != null && p < impMeshes.Length && impMeshes[p];
                    if (Legacy) { if (d > drawDistance[p]) continue; DrawLegacy(bk, p, d, hasImp); drawn += n; continue; }
                    float draw = Mathf.Min(drawDistance[p], drawCap[p]) * k; if (d > draw) continue;
                    float near = hasImp ? nearDistance[p] * k * NearScale : 1e9f;
                    float shP = Mathf.Min(shD, shadowCap[p] * k); bool impSh = d < impShD && shadowCap[p] > 1e8f;
                    if (d > near + (fix ? HYST : 0) && dmax <= draw - (fix ? Mathf.Min(draw * FADE, FADE_MAX) : 0) && (!fix || ImpOwnShadow || d > shP + HYST))
                    {
                        if (!view) { if (impSh) Submit(impMaterials[p], impMeshes[p], 0, bk.m, o, n, bk.bounds, ShadowCastingMode.ShadowsOnly, mpbImp[p]); continue; }
                        Submit(impMaterials[p], impMeshes[p], 0, bk.m, o, n, bk.bounds, impSh ? ShadowCastingMode.On : ShadowCastingMode.Off, mpbImp[p]); Stat(p, false, n, impSh); drawn += n;
                        continue;
                    }
                    drawn += fix ? Split2(p, bk, cp, draw, near, shP, impSh, view, hasImp) : Split(p, bk.m, o, n, cp, draw, near, shD, impSh);
                }
            }
            DrawnLastFrame = drawn;
            long tm = 0, ts = 0;
            for (int p = 0; p < meshes.Length; p++) { tm += (long)statFull[p] * trisFull[p] + (long)statImp[p] * trisImp[p]; ts += (long)statFullSh[p] * trisFull[p] + (long)statImpSh[p] * trisImp[p]; }
            TrisMain = tm; TrisShadow = ts;
        }

        void Stat(int p, bool full, int n, bool sh) { if (full) { statFull[p] += n; if (sh) statFullSh[p] += n; } else { statImp[p] += n; if (sh) statImpSh[p] += n; } }

        // lists: 0 full + shadow, 1 full (no shadow), 2 full shadow only (outside the view), 3 impostor
        readonly List3[] L = new List3[6]; // 5 = impostor without its own shadow (Split2)

        int Split(int p, Matrix4x4[] arr, int o, int cnt, Vector3 cp, float draw, float near, float shD, bool impShadow)
        {
            var mb = meshes[p].bounds; float rad = mb.extents.magnitude, cy = mb.center.y;
            var eb = mb.extents; float ex = Mathf.Max(eb.x, eb.z);
            float draw2 = draw * draw, near2 = near * near, sh2 = shD * shD; int drawn = 0;
            for (int i = 0; i < 4; i++) L[i].n = 0;
            for (int i = o; i < o + cnt; i++)
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
        int Split2(int p, Bucket bk, Vector3 cp, float draw, float near, float shD, bool impShadow, bool view, bool hasImp)
        {
            var mb = meshes[p].bounds; float rad = mb.extents.magnitude, cy = mb.center.y;
            var eb = mb.extents; float ex = Mathf.Max(eb.x, eb.z);
            float draw2 = draw * draw, fade = draw - Mathf.Min(draw * FADE, FADE_MAX), fade2 = fade * fade;
            float nIn = Mathf.Max(0, near - HYST), nOut = near + HYST, sIn = Mathf.Max(0, shD - HYST), sOut = shD + HYST;
            float nIn2 = nIn * nIn, nOut2 = nOut * nOut, sIn2 = sIn * sIn, sOut2 = sOut * sOut;
            // cross-fade: full mesh within nOut, impostor from nA on, both in between (the shader dithers them, no hysteresis needed)
            bool xf = xfade[p] && hasImp; float nA = nOut - Mathf.Max(XFADE_MIN, near * XFADE), nA2 = nA * nA;
            // the far fade by dither (no shrinking) for the impostor / a full mesh fully on Foliage
            bool ditherImp = impDither[p], ditherFull = fullDither[p] && !hasImp;
            int drawn = 0; var arr = bk.m; var state = bk.st; int o = bk.off[p], end = o + bk.cnt[p];
            for (int i = 0; i < 6; i++) L[i].n = 0;
            for (int i = o; i < end; i++)
            {
                ref var m = ref arr[i];
                float dx = m.m03 - cp.x, dz = m.m23 - cp.z, dd = dx * dx + dz * dz;
                if (dd > draw2) continue;
                ref byte st = ref state[i];
                bool full = !hasImp || dd < nIn2 || (dd < nOut2 && (st & 1) != 0), both = false;
                if (xf) { full = dd < nOut2; both = full && dd > nA2; }
                bool fsh = dd < sIn2 || (dd < sOut2 && (st & 2) != 0);
                st = (byte)((full ? 1 : 0) | (fsh ? 2 : 0));
                float s = Mathf.Sqrt(m.m00 * m.m00 + m.m20 * m.m20), g = 1;
                if (dd > fade2 && !(full ? ditherFull : ditherImp)) { g = Mathf.Clamp01((draw - Mathf.Sqrt(dd)) / (draw - fade)); g = g * g * (3 - 2 * g); if (g < .02f) continue; }
                var c = new Vector3(m.m03, m.m13 + cy * s * g, m.m23); float r = rad * s * g;
                int li, li2 = -1, li3 = -1;
                if (!full)
                {
                    // the impostor's shadow: inside the full-mesh shadow distance the full mesh's (23. 11.: the crossed quads cast
                    // next to nothing, so a crnika / pine / oleander's shadow vanished at its impostor switch, 25–60 m in front of
                    // the truck, and came back on approach), past it the impostor's
                    int sl = ImpOwnShadow ? (impShadow ? 4 : -1) : fsh ? 2 : impShadow ? 4 : -1;
                    if (sl >= 0 && !ShadowInView(c, r, (cy + eb.y) * s * g)) sl = -1;
                    if (view) { li = ImpOwnShadow ? 3 : 5; li2 = ImpOwnShadow ? -1 : sl; }
                    else if (sl >= 0) li = sl;
                    else continue;
                }
                else
                {
                    bool vis = view && InView(c, r);
                    if (vis) { li = fsh ? 0 : 1; if (!fsh && impShadow && hasImp) li2 = 4; if (both) li3 = 5; }
                    else if ((fsh || (impShadow && hasImp)) && ShadowInView(c, r, (cy + eb.y) * s * g)) li = fsh ? 2 : 4; // off screen, but its shadow may fall into view
                    else continue;
                }
                var mm = m;
                if (g < 1) { mm.m00 *= g; mm.m01 *= g; mm.m02 *= g; mm.m10 *= g; mm.m11 *= g; mm.m12 *= g; mm.m20 *= g; mm.m21 *= g; mm.m22 *= g; }
                float gs = s * g;
                var lo = new Vector3(m.m03 - ex * gs, m.m13 + (cy - eb.y) * gs, m.m23 - ex * gs); var hi = new Vector3(m.m03 + ex * gs, m.m13 + (cy + eb.y) * gs, m.m23 + ex * gs);
                for (int pass = 0; pass < 3; pass++)
                {
                    int lj = pass == 0 ? li : pass == 1 ? li2 : li3; if (lj < 0) continue;
                    ref var l = ref L[lj];
                    if (l.n == 0) { l.a = Take(); l.lo = lo; l.hi = hi; } else { l.lo = Vector3.Min(l.lo, lo); l.hi = Vector3.Max(l.hi, hi); }
                    l.a[l.n++] = mm;
                    if (l.n == CHUNK) drawn += Flush(lj, p, impShadow);
                }
            }
            for (int li = 0; li < 6; li++) if (L[li].n > 0) drawn += Flush(li, p, impShadow);
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
            var pi = mpbImp?[p];
            if (li == 4) { if (impShadow) Submit(impMaterials[p], impMeshes[p], 0, l.a, 0, n, b, ShadowCastingMode.ShadowsOnly, pi); statImpSh[p] += n; return 0; }
            if (li == 5) { Submit(impMaterials[p], impMeshes[p], 0, l.a, 0, n, b, ShadowCastingMode.Off, pi); Stat(p, false, n, false); return n; }
            if (li == 3) { Submit(impMaterials[p], impMeshes[p], 0, l.a, 0, n, b, impShadow ? ShadowCastingMode.On : ShadowCastingMode.Off, pi); Stat(p, false, n, impShadow); return n; }
            var mode = li == 0 ? ShadowCastingMode.On : li == 1 ? ShadowCastingMode.Off : ShadowCastingMode.ShadowsOnly;
            var mesh = meshes[p];
            for (int sm = 0; sm < mesh.subMeshCount; sm++) Submit(materials[materialStart[p] + sm], mesh, sm, l.a, 0, n, b, mode, mpbFull?[p]);
            if (li == 2) { statFullSh[p] += n; return 0; }
            Stat(p, true, n, li == 0); return n;
        }

        /// n instances from a[start..]
        void Submit(Material mat, Mesh mesh, int sm, Matrix4x4[] a, int start, int n, Bounds b, ShadowCastingMode mode, MaterialPropertyBlock mpb = null)
        {
            if (Skip != 0)
            {
                bool imp = impMeshes != null && System.Array.IndexOf(impMeshes, mesh) >= 0;
                bool sh = mode != ShadowCastingMode.Off, main = mode != ShadowCastingMode.ShadowsOnly;
                if (sh && (Skip & (imp ? 2 : 8)) != 0) { if (!main) return; mode = ShadowCastingMode.Off; }
                if (main && (Skip & (imp ? 1 : 4)) != 0) { if (mode == ShadowCastingMode.Off) return; mode = ShadowCastingMode.ShadowsOnly; }
            }
            var rp = new RenderParams(mat) { shadowCastingMode = mode, receiveShadows = true, worldBounds = b, layer = gameObject.layer, matProps = mpb };
            for (int i = 0; i < n; i += CHUNK) Graphics.RenderMeshInstanced(rp, mesh, sm, a, Mathf.Min(CHUNK, n - i), start + i);
        }

        void DrawLegacy(Bucket bk, int p, float d, bool hasImp)
        {
            int o = bk.off[p], n = bk.cnt[p];
            if (d > nearDistance[p] && hasImp)
            {
                bool sh = d < impShadowDistance;
                Submit(impMaterials[p], impMeshes[p], 0, bk.m, o, n, bk.bounds, sh ? ShadowCastingMode.On : ShadowCastingMode.Off); Stat(p, false, n, sh); return;
            }
            bool shadows = d < shadowDistance;
            var mesh = meshes[p];
            for (int sm = 0; sm < mesh.subMeshCount; sm++) Submit(materials[materialStart[p] + sm], mesh, sm, bk.m, o, n, bk.bounds, shadows ? ShadowCastingMode.On : ShadowCastingMode.Off);
            Stat(p, true, n, shadows);
        }
    }
}
