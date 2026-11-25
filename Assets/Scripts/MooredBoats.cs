// Moored boats along the rivas, in the mandrači and the marinas of Kaštela (Marko 2026-11-22), placed by Magistrala/Brodice: postavi
// (Assets/Editor/BoatsBuilder.cs, also run by Magistrala/Rekviziti: postavi). Drawn like the parked cars (ParkedCars): boats sit
// in 128 m cells, only the cells near the camera are walked; one mesh per kind and level, one material (Magistrala/MooredBoat,
// vertex colours, no textures), the hull colour and bobbing phase per instance (instancing buffer _BoatTint) — one instanced
// draw per kind and level: level 0 (near mesh) below lodDist, level 1 (far mesh) beyond, nothing beyond drawDist.
// The bobbing is in the shader (vertex stage, _Time + the instance phase); this one component is the only script for all boats.
// Shadows: the near level casts (the visible near boats only), the far level casts none. No colliders.
// Pages (streaming phase 2, Assets/Scripts/Cells/CellData.cs): the boats of one CellGrid cell are one page, from the scene's arrays
// at start (editor scene) or from the cell blobs when the bake took the arrays out (player / play mode).
using System.Collections.Generic;
using Magistrala.Cells;
using Magistrala.Streaming;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala
{
    [DefaultExecutionOrder(10002)]
    public class MooredBoats : MonoBehaviour, ICellLayer
    {
        public Material mat;
        /// per kind: near / far mesh, bobbing (x heave m, y roll rad, z pitch rad, w rad/s), half length (m, at scale 1) for culling
        public Mesh[] near, far;
        public Vector4[] bob;
        public float[] halfLen;
        public string[] kindNames;
        /// per boat: position (Unity, y = waterline), yaw (deg) in w; kind; uniform scale; linear hull colour rgb + phase a
        public Vector4[] boats;
        public int[] kind;
        public float[] scale;
        public Vector4[] tint;
        public float drawDist = 900, lodDist = 70;
        /// measuring only: -boats 0 = not drawn
        public static bool Off;
        public int DrawnLastFrame { get; private set; }
        /// RenderMeshInstanced calls and instances of the last frame
        public static int Calls, Instances, Tris;   // Tris: triangles submitted (visible pass; the near level adds its shadow pass)

        const float CELL = 128;
        // instances per call: 2 matrices + 1 float4 per instance within a 16 KB instancing constant buffer
        const int BATCH = 100;
        /// one CellGrid cell's boats, sorted into 128 m buckets (ranges)
        sealed class Page { public Matrix4x4[] M; public Vector4[] tint; public int[] kind; public float[] scale; public int n; public Vector3[] bc; public int[] bo, bn; }
        readonly List<Page> pages = new List<Page>();
        Matrix4x4[][] lm; Vector4[][] lt; int[] ln;
        public int Count { get; private set; }
        bool fromCells; readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        readonly Vector4[] tintTmp = new Vector4[BATCH];
        readonly List<MaterialPropertyBlock> mpb = new List<MaterialPropertyBlock>();
        Camera cam; readonly Plane[] planes = new Plane[6];

        void Start()
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-boats" && a[i + 1] == "0") Off = true;
            if (!fromCells) Init();
        }

        /// builds the cells and draw lists (Start; editor shot tools call it directly)
        public void Init()
        {
            pages.Clear(); Count = 0; lm = null;
            if (!InitLists() || boats == null) return;
            var byCell = new Dictionary<Vector2Int, List<int>>();
            for (int i = 0; i < boats.Length; i++) { var c = CellGrid.Cell((Vector3)boats[i]); if (!byCell.TryGetValue(c, out var l)) byCell[c] = l = new List<int>(); l.Add(i); }
            foreach (var kv in byCell)
            {
                var l = kv.Value; var pg = new Page { n = l.Count, M = new Matrix4x4[l.Count], tint = new Vector4[l.Count], kind = new int[l.Count], scale = new float[l.Count] };
                for (int k = 0; k < l.Count; k++) { int i = l[k]; Put(pg, k, boats[i], kind[i], scale[i], tint[i]); }
                Bucketize(pg); Attach(kv.Key, pg);
            }
        }

        bool InitLists()
        {
            if (lm != null) return true;
            if (!mat || near == null || far == null) return false;
            mat.enableInstancing = true;
            int nb = near.Length * 2;
            lm = new Matrix4x4[nb][]; lt = new Vector4[nb][]; ln = new int[nb];
            for (int i = 0; i < nb; i++) { lm[i] = new Matrix4x4[32]; lt[i] = new Vector4[32]; }
            return true;
        }

        static void Put(Page pg, int k, Vector4 b, int kd, float s, Vector4 t) { pg.M[k] = CellMath.TRSYaw(b, b.w, s); pg.kind[k] = kd; pg.scale[k] = s; pg.tint[k] = t; }
        static void Bucketize(Page pg)
        {
            var idx = new Dictionary<long, int>(); var lists = new List<List<int>>(); var ctr = new List<Vector3>();
            for (int i = 0; i < pg.n; i++)
            {
                float x = pg.M[i].m03, z = pg.M[i].m23; long key = CellMath.Key(x, z, CELL);
                if (!idx.TryGetValue(key, out int b)) { b = lists.Count; idx[key] = b; lists.Add(new List<int>()); ctr.Add(new Vector3((Mathf.Floor(x / CELL) + .5f) * CELL, pg.M[i].m13, (Mathf.Floor(z / CELL) + .5f) * CELL)); }
                lists[b].Add(i);
            }
            var M = new Matrix4x4[pg.n]; var T = new Vector4[pg.n]; var K = new int[pg.n]; var S = new float[pg.n];
            pg.bc = ctr.ToArray(); pg.bo = new int[lists.Count]; pg.bn = new int[lists.Count]; int w = 0;
            for (int b = 0; b < lists.Count; b++) { pg.bo[b] = w; pg.bn[b] = lists[b].Count; foreach (int i in lists[b]) { M[w] = pg.M[i]; T[w] = pg.tint[i]; K[w] = pg.kind[i]; S[w] = pg.scale[i]; w++; } }
            pg.M = M; pg.tint = T; pg.kind = K; pg.scale = S;
        }

        // ICellLayer (CellData): the BOAT section
        public void Prepare(CellDataManifest m) { fromCells = true; InitLists(); }
        public object Decode(CellSections s)
        {
            int o = s.Find(CellBlob.BOAT, out int n); if (o < 0 || n == 0) return null;
            var pg = new Page { n = n, M = new Matrix4x4[n], tint = new Vector4[n], kind = new int[n], scale = new float[n] };
            for (int k = 0; k < n; k++) { int r = o + k * CellBlob.BOAT_B; Put(pg, k, CellBlob.V4(s.b, r), CellBlob.I(s.b, r + 16), CellBlob.F(s.b, r + 20), CellBlob.V4(s.b, r + 24)); }
            Bucketize(pg); return pg;
        }
        public void Attach(Vector2Int cell, object page) { var pg = (Page)page; pages.Add(pg); Count += pg.n; }
        public void Detach(Vector2Int cell, object page) { var pg = (Page)page; if (pages.Remove(pg)) Count -= pg.n; }
        public void Release(object page) { }
        // phase-3 scheduler (ICellDataSystem names): this system's part of a cell (CellData decodes the cell once for all systems)
        bool Mine => CellData.I && CellData.I.boats == this;
        public void LoadCell(Vector2Int c) { if (Mine) CellData.Load(c, 1 << CellBlob.BOATS); }
        public void UnloadCell(Vector2Int c) { if (Mine) CellData.Unload(c, 1 << CellBlob.BOATS); }
        public bool IsLoaded(Vector2Int c) => !Mine || CellData.IsLoaded(c, 1 << CellBlob.BOATS);


        void Add(int b, in Matrix4x4 m, in Vector4 t)
        {
            if (ln[b] == lm[b].Length) { System.Array.Resize(ref lm[b], lm[b].Length * 2); System.Array.Resize(ref lt[b], lt[b].Length * 2); }
            lt[b][ln[b]] = t; lm[b][ln[b]++] = m;
        }

        void LateUpdate()
        {
            if (!cam) cam = Camera.main;
            sw.Restart(); Draw(cam, null); sw.Stop(); CellStats.Add(CellStats.BOATS, (float)sw.Elapsed.TotalMilliseconds);
        }

        /// collects and submits this frame's draws for camera c (target: only that camera, null = every camera)
        public void Draw(Camera c, Camera target)
        {
            DrawnLastFrame = 0; Calls = 0; Instances = 0; Tris = 0;
            if (Off || lm == null || pages.Count == 0 || !c) return;
            GeometryUtility.CalculateFrustumPlanes(c, planes);
            var cp = c.transform.position;
            for (int i = 0; i < ln.Length; i++) ln[i] = 0;
            float draw = drawDist * Mathf.Max(.3f, TreeRenderer.Distance), draw2 = draw * draw, l2 = lodDist * lodDist;
            for (int pi = 0; pi < pages.Count; pi++)
            for (int bi = 0, nbk = pages[pi].bc.Length; bi < nbk; bi++)
            {
                var pg = pages[pi]; var cc = pg.bc[bi];
                float dx = cc.x - cp.x, dz = cc.z - cp.z;
                if (Mathf.Sqrt(dx * dx + dz * dz) - CELL * .71f > draw) continue;
                for (int i = pg.bo[bi], end = pg.bo[bi] + pg.bn[bi]; i < end; i++)
                {
                    ref var m = ref pg.M[i];
                    float ex = m.m03 - cp.x, ez = m.m23 - cp.z, dd = ex * ex + ez * ez;
                    int k = pg.kind[i];
                    float r = halfLen[k] * pg.scale[i];
                    // small boats fade out sooner (a 5 m pasara is a few pixels at 600 m)
                    float lim = Mathf.Min(draw, 60 + r * 110);
                    if (dd > lim * lim) continue;
                    if (!GeometryUtility.TestPlanesAABB(planes, new Bounds(new Vector3(m.m03, m.m13 + r * .4f, m.m23), new Vector3(r * 2, r * 2.4f, r * 2)))) continue;
                    DrawnLastFrame++;
                    Add(k * 2 + (dd < l2 ? 0 : 1), m, pg.tint[i]);
                }
            }
            var wb = new Bounds(cp, Vector3.one * (draw * 2 + 50));
            int mi = 0;
            MaterialPropertyBlock Mpb() { if (mi == mpb.Count) mpb.Add(new MaterialPropertyBlock()); var b = mpb[mi++]; b.Clear(); return b; }
            for (int b = 0; b < ln.Length; b++)
            {
                int n = ln[b]; if (n == 0) continue;
                int k = b / 2, lv = b % 2; var mesh = lv == 0 ? near[k] : far[k]; if (!mesh) continue;
                for (int s = 0; s < n; s += BATCH)
                {
                    int cnt = Mathf.Min(BATCH, n - s);
                    System.Array.Copy(lt[b], s, tintTmp, 0, cnt);
                    var props = Mpb(); props.SetVectorArray("_BoatTint", tintTmp); props.SetVector("_BoatBob", bob[k]);
                    Graphics.RenderMeshInstanced(new RenderParams(mat)
                    {
                        shadowCastingMode = lv == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true,
                        worldBounds = wb, layer = gameObject.layer, matProps = props, camera = target
                    }, mesh, 0, lm[b], cnt, s);
                    Calls++; Instances += cnt; Tris += cnt * (int)(mesh.GetIndexCount(0) / 3);
                }
            }
        }
    }
}
