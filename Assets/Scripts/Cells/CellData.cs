// World streaming, phase 2: the data-only world systems (trees, parked cars, street lamps, moored boats, props + nature, their
// colliders) as per-cell blobs (CellGrid cells, Assets/Generated/Cells, written by Magistrala/Ćelije: ispeci = CellBake), loaded
// per cell into the systems' own pages: a cell's page is added to / taken out of the instanced renderer without rebuilding
// anything else; no GameObject per instance (colliders: a pool around the player, CellColliders).
//
// In the editor scene nothing changes: the builders keep making their GameObjects / arrays (their own checks use them). When the
// scene is built or play mode starts, CellBake (IProcessSceneWithReport) takes the baked systems out of the scene copy
// (prop / cliff prefab instances, parked-car / lamp colliders, the systems' arrays, trees.bytes) and adds this component, which
// feeds the same systems from the blobs. A system whose bake is stale stays as it was (GameObjects / arrays) and is not loaded here.
//
// API for the phase-3 scheduler (main thread):
//   CellData.LoadEverything (default true): Start loads every cell (blocking, decoded in parallel) - the game as before phase 3.
//     Set it false before the scene starts (or run with -cells stream) and drive Load / Unload yourself.
//   Per system (ICellDataSystem names: LoadCell / UnloadCell / IsLoaded) on TreeRenderer, ParkedCars, StreetLights, MooredBoats,
//   CellProps, and on CellData for all of them at once; they forward to:
//   CellData.Load(cell, systems)    queue a cell: the blob is copied (memcpy) and decoded once, on a worker thread, for every system;
//                                   the wanted systems' pages are attached in Update within BudgetMs (default 0.5 ms) per frame.
//                                   false = no data there for those systems.
//   CellData.Unload(cell, systems)  detach those systems' pages now (cheap: list removal); when no system wants the cell any more its
//                                   arrays go back to the pools. A cell still decoding is dropped when it finishes.
//   CellData.IsLoaded(cell, systems) attached for them (true when there is nothing to load); State(cell) None / Loading / Loaded;
//   CellData.Pending = cells queued or decoding;  CellData.Cells = cells with data;  CellLoaded / CellUnloaded events.
//   The collider pool (CellColliders) follows the props / parked cars / lamps of a cell.
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Magistrala.Cells
{
    /// one system fed from the blobs. Decode runs on a worker thread (no Unity API, only data snapshotted in Prepare);
    /// Attach / Detach / Release on the main thread.
    public interface ICellLayer
    {
        void Prepare(CellManifest m);
        object Decode(CellSections s);
        void Attach(Vector2Int cell, object page);
        void Detach(Vector2Int cell, object page);
        /// a decoded page that will not be attached (unloaded while loading): give its arrays back
        void Release(object page);
    }

    /// the sections of one blob (parsed header)
    public sealed class CellSections
    {
        public byte[] b; public int len; public Vector2Int cell;
        public readonly uint[] id = new uint[16]; public readonly int[] n = new int[16], off = new int[16]; public int count;
        public bool Parse()
        {
            count = 0;
            if (!CellBlob.Header(b, len, out int cx, out int cz, out int ns)) return false;
            cell = new Vector2Int(cx, cz); int o = 20;
            for (int i = 0; i < ns && count < id.Length; i++)
            {
                if (o + 12 > len) return false;
                id[count] = CellBlob.U(b, o); n[count] = CellBlob.I(b, o + 4); int bytes = CellBlob.I(b, o + 8); off[count] = o + 12;
                if (off[count] + bytes > len || bytes != n[count] * CellBlob.RecordSize(id[count])) return false;
                count++; o += 12 + bytes;
            }
            return true;
        }
        public int Find(uint sid, out int records) { for (int i = 0; i < count; i++) if (id[i] == sid) { records = n[i]; return off[i]; } records = 0; return -1; }
    }

    public enum CellState { None, Loading, Loaded }

    [DefaultExecutionOrder(-300)]
    public class CellData : MonoBehaviour
    {
        public CellManifest manifest;
        /// systems taken out of the scene by the bake and fed from here (CellBlob bits)
        public int mask;
        public TreeRenderer trees;
        public Magistrala.Traffic.ParkedCars parked;
        public StreetLights lamps;
        public MooredBoats boats;
        public CellProps props;
        public CellColliders colliders;

        public static CellData I { get; private set; }
        public static bool LoadEverything = true;
        public static float BudgetMs = .5f;
        public static event Action<Vector2Int> CellLoaded, CellUnloaded;

        readonly List<ICellLayer> layers = new List<ICellLayer>();
        /// per layer: the system bits that keep it attached (the collider pool: props, parked cars, lamps)
        readonly List<int> layerWant = new List<int>();
        readonly Dictionary<Vector2Int, int> blobIndex = new Dictionary<Vector2Int, int>();
        /// a cell: the systems that want it, its decoded pages (per layer) and which of them are attached (layer bits)
        sealed class Slot { public Vector2Int cell; public CellState state; public Job job; public object[] pages; public int want, attached; }
        readonly Dictionary<Vector2Int, Slot> slots = new Dictionary<Vector2Int, Slot>();
        readonly Queue<Job> running = new Queue<Job>();
        readonly Stack<Job> freeJobs = new Stack<Job>();
        readonly List<Vector2Int> cellList = new List<Vector2Int>();
        bool prepared;
        public int LoadedCells { get; private set; }
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
        public const int ALL = (1 << CellBlob.SYSTEMS) - 1;

        sealed class Job
        {
            public Vector2Int cell; public readonly CellSections sec = new CellSections(); public object[] pages;
            public List<ICellLayer> layers; public volatile bool done; public Exception error;
            public readonly WaitCallback run; public ManualResetEventSlim signal;
            public Job() { run = Run; }
            void Run(object _)
            {
                try { if (!sec.Parse()) throw new Exception("bad blob"); for (int i = 0; i < layers.Count; i++) pages[i] = layers[i].Decode(sec); }
                catch (Exception e) { error = e; }
                done = true; signal?.Set();
            }
        }

        // ------------------------------------------------------------------ API (main thread). systems: CellBlob bits (TREES, PROPS,
        // PARKED, LAMPS, BOATS); a cell is decoded once for all of them, each system's page is attached while that system wants it.
        public static IReadOnlyList<Vector2Int> Cells => I ? I.cellList : (IReadOnlyList<Vector2Int>)Array.Empty<Vector2Int>();
        public static int Pending => I ? I.running.Count : 0;
        public static CellState State(Vector2Int c) => I && I.slots.TryGetValue(c, out var s) ? s.state : CellState.None;
        /// queue / keep a cell for these systems; false = no data in that cell (nothing to wait for)
        public static bool Load(Vector2Int c, int systems = ALL) => I && I.LoadCell(c, systems);
        public static void Unload(Vector2Int c, int systems = ALL) { if (I) I.UnloadCell(c, systems); }
        /// the cell is in for these systems (also true when there is nothing to load: no data there, or the system is not from cells)
        public static bool IsLoaded(Vector2Int c, int systems)
        {
            if (!I) return true;
            I.SetUp(); systems &= I.mask; if (systems == 0 || !I.blobIndex.ContainsKey(c)) return true;
            return I.slots.TryGetValue(c, out var s) && s.state == CellState.Loaded && (s.want & systems) == systems;
        }
        public static bool Has(int system) => I && (I.mask & (1 << system)) != 0;

        // ICellDataSystem-style names (phase-3 scheduler): every system at once
        public void LoadCell(Vector2Int c) => LoadCell(c, ALL);
        public void UnloadCell(Vector2Int c) => UnloadCell(c, ALL);
        public bool IsLoaded(Vector2Int c) => IsLoaded(c, ALL);

        void Awake() { I = this; }
        void OnEnable() { if (!I) I = this; }

        /// once, before the first load (not in Awake: CellBake adds this component in play mode before it fills its fields)
        bool setUp;
        void SetUp()
        {
            if (setUp) return; setUp = true; I = this;
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-cells") { if (a[i + 1] == "stream") LoadEverything = false; if (a[i + 1] == "off") { LoadEverything = false; noLoad = true; } if (a[i + 1] == "ring") { LoadEverything = false; ring = true; } }
            CellStats.Extra = Summary;
            if (!manifest) manifest = Resources.Load<CellManifest>(CellManifest.ResourceName);
            if (!manifest) { Debug.LogWarning("[cells] no manifest: nothing loaded"); return; }
            for (int i = 0; i < manifest.cells.Length; i++) if (manifest.blobs[i]) { blobIndex[manifest.cells[i]] = i; cellList.Add(manifest.cells[i]); }
            void L(int sys, ICellLayer l, int want) { if (Has(sys) && l != null) { layers.Add(l); layerWant.Add(want); } }
            L(CellBlob.TREES, trees, 1 << CellBlob.TREES);
            L(CellBlob.PARKED, parked, 1 << CellBlob.PARKED);
            L(CellBlob.LAMPS, lamps, 1 << CellBlob.LAMPS);
            L(CellBlob.BOATS, boats, 1 << CellBlob.BOATS);
            L(CellBlob.PROPS, props, 1 << CellBlob.PROPS);
            L(CellBlob.COLLIDERS, colliders, 1 << CellBlob.PROPS | 1 << CellBlob.PARKED | 1 << CellBlob.LAMPS);
        }
        bool noLoad;

        void OnDestroy() { if (I == this) { I = null; CellStats.Extra = null; } }

        void Prepare()
        {
            SetUp();
            if (prepared || !manifest) return; prepared = true;
            foreach (var l in layers) l.Prepare(manifest);
        }

        void Start()
        {
            SetUp();
            if (LoadEverything && !noLoad && manifest) LoadAllNow();
        }

        /// every cell for every system, now: decoded on the thread pool in parallel, attached in this frame (start-up / loading screen)
        public void LoadAllNow()
        {
            Prepare();
            var t = Time.realtimeSinceStartup;
            var jobs = new List<Job>();
            foreach (var c in cellList)
            {
                if (slots.TryGetValue(c, out var s)) { s.want = ALL; if (s.state == CellState.Loaded) Apply(s); continue; }
                var j = Queue(c, ALL, true); if (j != null) jobs.Add(j);
            }
            foreach (var j in jobs) { j.signal.Wait(); j.signal.Dispose(); j.signal = null; }
            float dec = (Time.realtimeSinceStartup - t) * 1000;
            while (running.Count > 0) { var j = running.Dequeue(); var spin = new SpinWait(); while (!j.done) spin.SpinOnce(); Finish(j); }
            Debug.Log($"[cells] all {jobs.Count} cells loaded: decode {dec:F0} ms, attach {(Time.realtimeSinceStartup - t) * 1000 - dec:F0} ms; {Summary()}");
        }

        public void UnloadAll() { foreach (var c in cellList) UnloadCell(c, ALL); }

        bool LoadCell(Vector2Int c, int systems)
        {
            SetUp(); systems &= mask; if (systems == 0 || !manifest || !blobIndex.ContainsKey(c)) return false;
            if (slots.TryGetValue(c, out var s))
            {
                s.want |= systems;
                if (s.state == CellState.Loaded) { sw.Restart(); Apply(s); sw.Stop(); CellStats.Add(CellStats.LOADER, (float)sw.Elapsed.TotalMilliseconds); }
                return true;
            }
            Prepare();
            return Queue(c, systems, false) != null;
        }

        Job Queue(Vector2Int c, int systems, bool signal)
        {
            sw.Restart();
            var ta = manifest.blobs[blobIndex[c]];
            var na = ta.GetData<byte>();
            var j = freeJobs.Count > 0 ? freeJobs.Pop() : new Job();
            j.cell = c; j.done = false; j.error = null; j.layers = layers; j.signal = signal ? new ManualResetEventSlim(false) : null;
            if (j.pages == null || j.pages.Length != layers.Count) j.pages = new object[layers.Count];
            j.sec.b = BytePool.Rent(na.Length); j.sec.len = na.Length;
            Unity.Collections.NativeArray<byte>.Copy(na, j.sec.b, na.Length);
            slots[c] = new Slot { cell = c, state = CellState.Loading, job = j, want = systems & mask };
            running.Enqueue(j);
            ThreadPool.UnsafeQueueUserWorkItem(j.run, null);
            sw.Stop(); CellStats.Add(CellStats.LOADER, (float)sw.Elapsed.TotalMilliseconds);
            return j;
        }

        void UnloadCell(Vector2Int c, int systems)
        {
            if (!slots.TryGetValue(c, out var s)) return;
            s.want &= ~systems;
            if (s.state == CellState.Loading) return; // (Finish drops it if nobody wants it by then)
            sw.Restart();
            Apply(s);
            if (s.want == 0) { Drop(s); CellUnloaded?.Invoke(c); }
            sw.Stop(); CellStats.Add(CellStats.LOADER, (float)sw.Elapsed.TotalMilliseconds);
        }

        /// attach the pages of the layers the cell's systems want, detach the others (cheap: list add / removal)
        void Apply(Slot s)
        {
            for (int i = 0; i < layers.Count; i++)
            {
                if (s.pages[i] == null) continue;
                bool want = (s.want & layerWant[i]) != 0, on = (s.attached & 1 << i) != 0;
                if (want == on) continue;
                if (want) layers[i].Attach(s.cell, s.pages[i]); else layers[i].Detach(s.cell, s.pages[i]);
                s.attached ^= 1 << i;
            }
        }

        void Drop(Slot s)
        {
            for (int i = 0; i < layers.Count; i++) if (s.pages != null && s.pages[i] != null) layers[i].Release(s.pages[i]);
            if (s.state == CellState.Loaded) LoadedCells--;
            slots.Remove(s.cell);
        }

        /// testing only (-cells ring): a stand-in for the phase-3 scheduler - every 0.5 s the cells within RingIn of the camera are
        /// loaded, those beyond RingOut unloaded ([cells] ring lines: loads / unloads and the worst main-thread ms)
        bool ring; float nextRing; public static float RingIn = 1000, RingOut = 1400; int ringLoads, ringUnloads;
        readonly List<Vector2Int> ringTmp = new List<Vector2Int>();
        void Ring()
        {
            if (!ring || Time.unscaledTime < nextRing || !Camera.main) return;
            nextRing = Time.unscaledTime + .5f; SetUp();
            var cp = Camera.main.transform.position; float in2 = RingIn * RingIn, out2 = RingOut * RingOut;
            foreach (var c in cellList)
            {
                var b = Magistrala.Streaming.CellGrid.CellBounds(c); float d2 = b.SqrDistance(new Vector3(cp.x, b.center.y, cp.z));
                var st = State(c);
                if (d2 < in2 && (st == CellState.None || !IsLoaded(c, ALL))) { if (LoadCell(c, ALL)) ringLoads++; }
                else if (d2 > out2 && st != CellState.None && slots[c].want != 0) ringTmp.Add(c);
            }
            foreach (var c in ringTmp) { UnloadCell(c, ALL); ringUnloads++; }
            ringTmp.Clear();
        }
        public string RingSummary() => ring ? $" ring loads {ringLoads} unloads {ringUnloads}" : "";

        void Update()
        {
            Ring();
            if (running.Count == 0) return;
            sw.Restart();
            // in order (the queue head first); several per frame while under the budget
            while (running.Count > 0 && running.Peek().done && sw.Elapsed.TotalMilliseconds < BudgetMs) Finish(running.Dequeue());
            sw.Stop(); CellStats.Add(CellStats.LOADER, (float)sw.Elapsed.TotalMilliseconds);
        }

        void Finish(Job j)
        {
            BytePool.Return(j.sec.b); j.sec.b = null;
            var s = slots[j.cell];
            s.pages = (object[])j.pages.Clone(); s.job = null;
            if (j.error != null || s.want == 0)
            {
                if (j.error != null) Debug.LogError($"[cells] cell {j.cell}: {j.error}");
                Drop(s);
            }
            else
            {
                s.state = CellState.Loaded; LoadedCells++;
                Apply(s);
                CellLoaded?.Invoke(j.cell);
            }
            Array.Clear(j.pages, 0, j.pages.Length); freeJobs.Push(j);
        }

        public string Summary()
        {
            var sb = new System.Text.StringBuilder($"cells {LoadedCells}/{cellList.Count} loaded, {running.Count} pending{RingSummary()}, systems");
            for (int i = 0; i < CellBlob.SYSTEMS; i++) if ((mask & (1 << i)) != 0) sb.Append(' ').Append(CellBlob.SysNames[i]);
            if (trees && Has(CellBlob.TREES)) sb.Append($"; trees {trees.Count}");
            if (props && Has(CellBlob.PROPS)) sb.Append($"; props {props.Count} (drawn {props.DrawnLastFrame})");
            if (parked && Has(CellBlob.PARKED)) sb.Append($"; parked {parked.Count}");
            if (lamps && Has(CellBlob.LAMPS)) sb.Append($"; lamps {lamps.Count}");
            if (boats && Has(CellBlob.BOATS)) sb.Append($"; boats {boats.Count}");
            if (colliders && Has(CellBlob.COLLIDERS)) sb.Append($"; colliders {colliders.Count} shapes, {colliders.Active} active / {colliders.Pooled} pooled");
            return sb.ToString();
        }
    }

    /// pools of arrays by size class (8 classes per octave: at most 12.5 % unused), shared by the systems' pages; thread-safe
    /// (decodes rent on worker threads). A page given back is reused by the next cell of about its size: no garbage per load.
    public static class ArrayPool<T>
    {
        static readonly Dictionary<int, Stack<T[]>> free = new Dictionary<int, Stack<T[]>>();
        public static int Class(int n)
        {
            if (n <= 64) return 64;
            int top = 1; while (top <= n) top <<= 1; int step = Math.Max(8, top >> 4);
            return (n + step - 1) / step * step;
        }
        public static T[] Rent(int n)
        {
            if (n <= 0) return Array.Empty<T>();
            int c = Class(n);
            lock (free) { if (free.TryGetValue(c, out var s) && s.Count > 0) return s.Pop(); }
            return new T[c];
        }
        public static void Return(T[] a, bool clear = false)
        {
            if (a == null || a.Length == 0 || Class(a.Length) != a.Length) return;
            if (clear) Array.Clear(a, 0, a.Length);
            lock (free) { if (!free.TryGetValue(a.Length, out var s)) free[a.Length] = s = new Stack<T[]>(); s.Push(a); }
        }
    }
    public static class BytePool
    {
        public static byte[] Rent(int n) => ArrayPool<byte>.Rent(n);
        public static void Return(byte[] a) => ArrayPool<byte>.Return(a);
    }

    /// helpers the systems share
    public static class CellMath
    {
        /// yaw (deg about y) + uniform scale + position, as Matrix4x4.TRS(p, Euler(0, yaw, 0), s) without Unity API (worker threads)
        public static Matrix4x4 TRSYaw(Vector3 p, float yawDeg, float s)
        {
            float a = yawDeg * Mathf.Deg2Rad, c = (float)Math.Cos(a) * s, n = (float)Math.Sin(a) * s;
            var m = new Matrix4x4();
            m.m00 = c; m.m01 = 0; m.m02 = n; m.m03 = p.x;
            m.m10 = 0; m.m11 = s; m.m12 = 0; m.m13 = p.y;
            m.m20 = -n; m.m21 = 0; m.m22 = c; m.m23 = p.z;
            m.m33 = 1; return m;
        }
        public static long Key(float x, float z, float size) => ((long)Mathf.FloorToInt(x / size) << 32) ^ (uint)Mathf.FloorToInt(z / size);
    }
}
