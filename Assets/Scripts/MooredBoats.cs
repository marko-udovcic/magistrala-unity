// Moored boats along the rivas, in the mandrači and the marinas of Kaštela (Marko 2026-11-22), placed by Magistrala/Brodice: postavi
// (Assets/Editor/BoatsBuilder.cs, also run by Magistrala/Rekviziti: postavi). Drawn like the parked cars (ParkedCars): boats sit
// in 128 m cells, only the cells near the camera are walked; one mesh per kind and level, one material (Magistrala/MooredBoat,
// vertex colours, no textures), the hull colour and bobbing phase per instance (instancing buffer _BoatTint) — one instanced
// draw per kind and level: level 0 (near mesh) below lodDist, level 1 (far mesh) beyond, nothing beyond drawDist.
// The bobbing is in the shader (vertex stage, _Time + the instance phase); this one component is the only script for all boats.
// Shadows: the near level casts (the visible near boats only), the far level casts none. No colliders.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala
{
    [DefaultExecutionOrder(10002)]
    public class MooredBoats : MonoBehaviour
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
        readonly Dictionary<long, List<int>> cells = new Dictionary<long, List<int>>();
        readonly List<(long key, Vector3 c)> cellList = new List<(long, Vector3)>();
        Matrix4x4[] M; Matrix4x4[][] lm; Vector4[][] lt; int[] ln;
        readonly Vector4[] tintTmp = new Vector4[BATCH];
        readonly List<MaterialPropertyBlock> mpb = new List<MaterialPropertyBlock>();
        Camera cam; readonly Plane[] planes = new Plane[6];

        void Start()
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-boats" && a[i + 1] == "0") Off = true;
            Init();
        }

        /// builds the cells and draw lists (Start; editor shot tools call it directly)
        public void Init()
        {
            cells.Clear(); cellList.Clear(); M = null;
            if (!mat || boats == null || near == null || far == null) return;
            mat.enableInstancing = true;
            M = new Matrix4x4[boats.Length];
            for (int i = 0; i < boats.Length; i++)
            {
                var p = (Vector3)boats[i];
                M[i] = Matrix4x4.TRS(p, Quaternion.Euler(0, boats[i].w, 0), Vector3.one * scale[i]);
                long k = ((long)Mathf.FloorToInt(p.x / CELL) << 32) ^ (uint)Mathf.FloorToInt(p.z / CELL);
                if (!cells.TryGetValue(k, out var l)) { cells[k] = l = new List<int>(); cellList.Add((k, new Vector3((Mathf.Floor(p.x / CELL) + .5f) * CELL, p.y, (Mathf.Floor(p.z / CELL) + .5f) * CELL))); }
                l.Add(i);
            }
            int nb = near.Length * 2;
            lm = new Matrix4x4[nb][]; lt = new Vector4[nb][]; ln = new int[nb];
            for (int i = 0; i < nb; i++) { lm[i] = new Matrix4x4[32]; lt[i] = new Vector4[32]; }
        }

        void Add(int b, in Matrix4x4 m, in Vector4 t)
        {
            if (ln[b] == lm[b].Length) { System.Array.Resize(ref lm[b], lm[b].Length * 2); System.Array.Resize(ref lt[b], lt[b].Length * 2); }
            lt[b][ln[b]] = t; lm[b][ln[b]++] = m;
        }

        static readonly System.Diagnostics.Stopwatch swc = new System.Diagnostics.Stopwatch();
        void LateUpdate()
        {
            if (!cam) cam = Camera.main;
            swc.Restart(); Draw(cam, null); Magistrala.Cells.CellStats.Add(Magistrala.Cells.CellStats.BOATS, (float)swc.Elapsed.TotalMilliseconds);
        }

        /// collects and submits this frame's draws for camera c (target: only that camera, null = every camera)
        public void Draw(Camera c, Camera target)
        {
            DrawnLastFrame = 0; Calls = 0; Instances = 0; Tris = 0;
            if (Off || M == null || !c) return;
            GeometryUtility.CalculateFrustumPlanes(c, planes);
            var cp = c.transform.position;
            for (int i = 0; i < ln.Length; i++) ln[i] = 0;
            float draw = drawDist * Mathf.Max(.3f, TreeRenderer.Distance), draw2 = draw * draw, l2 = lodDist * lodDist;
            foreach (var (key, cc) in cellList)
            {
                float dx = cc.x - cp.x, dz = cc.z - cp.z;
                if (Mathf.Sqrt(dx * dx + dz * dz) - CELL * .71f > draw) continue;
                foreach (int i in cells[key])
                {
                    ref var m = ref M[i];
                    float ex = m.m03 - cp.x, ez = m.m23 - cp.z, dd = ex * ex + ez * ez;
                    int k = kind[i];
                    float r = halfLen[k] * scale[i];
                    // small boats fade out sooner (a 5 m pasara is a few pixels at 600 m)
                    float lim = Mathf.Min(draw, 60 + r * 110);
                    if (dd > lim * lim) continue;
                    if (!GeometryUtility.TestPlanesAABB(planes, new Bounds(new Vector3(m.m03, m.m13 + r * .4f, m.m23), new Vector3(r * 2, r * 2.4f, r * 2)))) continue;
                    DrawnLastFrame++;
                    Add(k * 2 + (dd < l2 ? 0 : 1), m, tint[i]);
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
