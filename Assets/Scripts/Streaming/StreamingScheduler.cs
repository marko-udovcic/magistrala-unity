using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Magistrala.Game;
using LoadingPriority = UnityEngine.ThreadPriority;

namespace Magistrala.Streaming
{
    /// <summary>LoadCell enqueues bounded work; IsLoaded becomes true when usable. UnloadCell must also cancel a pending load.
    /// Implementations own their I/O, buffers and further time slicing. Calls run on Unity's main thread.</summary>
    public interface ICellDataSystem
    {
        void LoadCell(Vector2Int c);
        void UnloadCell(Vector2Int c);
        bool IsLoaded(Vector2Int c);
    }

    [DisallowMultipleComponent, DefaultExecutionOrder(31000)]
    public sealed class StreamingScheduler : MonoBehaviour
    {
        public static StreamingScheduler Instance { get; private set; }
        [SerializeField, Min(0)] float activeRadius = 700f;
        [SerializeField, Min(0)] float preloadRadius = 1300f;
        [SerializeField, Min(0)] float unloadRadius = 1600f;
        [SerializeField, Min(0)] float lookaheadSeconds = 30f;
        [SerializeField, Min(0)] float lookaheadCorridorRadius = 256f;
        [SerializeField, Min(.1f)] float lookaheadRefreshSeconds = .5f;
        [SerializeField, Min(.1f)] float frameBudgetMs = 1.5f;
        [SerializeField, Min(1)] int maxL0LoadsInFlight = 1;
        [SerializeField, Min(1)] int maxScenesWaitingForActivation = 1;
        [SerializeField, Min(1)] float slowFrameThresholdMs = 20f;
        [SerializeField] bool showInitialFade = true;
        [SerializeField] MonoBehaviour lookaheadComponent = null;
        [SerializeField] MonoBehaviour[] dataSystems = Array.Empty<MonoBehaviour>();

        public bool StreamingEnabled { get; private set; }
        public bool InitialLoading { get; private set; }
        public bool IsReady => StreamingEnabled && started && !InitialLoading && !HasFailedCells;
        public bool HasFailedCells { get; private set; }
        public string Status { get; private set; } = "idle (-stream 0)";
        public CellManifest Manifest { get; private set; }
        public ILookaheadSource LookaheadSource { get; set; }
        public int ActiveCount { get; private set; }
        public int PreloadedCount { get; private set; }
        public int LoadingCount { get; private set; }
        public int QueueLength { get; private set; }
        public int BudgetHits { get; private set; }
        public int LoadsStarted { get; private set; }
        /// <summary>Synchronous content-root activation (including Awake/OnEnable), not async scene integration.</summary>
        public double LastActivationMs { get; private set; }
        public double MaxActivationMs { get; private set; }
        /// <summary>Wall latency from allowSceneActivation=true until completion; includes frame scheduling.</summary>
        public double LastSceneActivationLatencyMs { get; private set; }
        public float NearestInactiveAheadMetres { get; private set; } = float.PositiveInfinity;

        enum Residency { Absent, Loading, Resident, Unloading, Failed }
        sealed class Cell
        {
            public L0Cell definition;
            public Residency state;
            public Scene scene;
            public AsyncOperation unload;
            public readonly List<GameObject> roots = new List<GameObject>(4);
            /// sliced activation: the objects under root/<group>/ that were saved active, hidden while their root is switched on and
            /// then shown a few per frame (units[0..shown) are active); never touched: objects saved inactive
            public readonly List<GameObject> units = new List<GameObject>(64);
            public int shown; public bool prepared;
            public bool wantActive, wantPreload, keep, ready;
            public float distance, priority;
        }
        sealed class DataSlot
        {
            public ICellDataSystem system;
            public CellDataKind kind;
            public bool[] requested;
            public int cursor;
        }

        Cell[] cells = Array.Empty<Cell>();
        readonly Dictionary<Vector2Int, int> index = new Dictionary<Vector2Int, int>();
        readonly List<DataSlot> systems = new List<DataSlot>(8);
        readonly Vector3[] path = new Vector3[256];
        readonly StreamingLog log = new StreamingLog();
        int pathCount, loadingIndex = -1, activationFrame = -1, budgetHitFrame = -1, rootCursor;
        AsyncOperation load;
        bool gateReleased, started, ownsPause, ownsPriority, graphLookupAttempted;
        double frameStart, gateReleasedAt, nextPathRefresh, nextPlayerSearch;
        float savedTimeScale;
        LoadingPriority savedPriority;
        TruckController truck;
        Rigidbody body;
        Vector3 position, velocity, previousPathPosition, previousPathVelocity;
        GameWorld graphWorld;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatic() { Instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance || FindFirstObjectByType<StreamingScheduler>()) return;
            // An idle component also supports F8 without requiring any scene modifications.
            var go = new GameObject("WorldStreaming");
            DontDestroyOnLoad(go);
            go.AddComponent<StreamingScheduler>();
        }

        void Awake()
        {
            if (Instance && Instance != this) { Destroy(this); return; }
            Instance = this;
            if (!GetComponent<StreamingHud>()) gameObject.AddComponent<StreamingHud>();
        }

        void Start()
        {
            if (Instance != this) return;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-stream") StreamingEnabled = args[i + 1] == "1";
            Dbg = Array.IndexOf(args, "-streamdbg") >= 0;
            // -streamold: the first phase-3 policy (one whole root per frame, start waits for the preload ring too), for A/B
            if (Array.IndexOf(args, "-streamold") >= 0) { sliced = false; startOnActiveRing = false; }
            // measuring: -streambudget MS (admission budget while driving), -streamupload MB,MS (QualitySettings async upload
            // buffer / time slice)
            for (int i = 0; i + 1 < args.Length; i++)
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                if (args[i] == "-streambudget" && float.TryParse(args[i + 1], System.Globalization.NumberStyles.Float, inv, out float ms)) frameBudgetMs = Mathf.Max(.1f, ms);
                if (args[i] == "-streamupload")
                {
                    var p = args[i + 1].Split(',');
                    if (p.Length > 0 && int.TryParse(p[0], out int mb)) QualitySettings.asyncUploadBufferSize = Mathf.Clamp(mb, 2, 2047);
                    if (p.Length > 1 && int.TryParse(p[1], out int sl)) QualitySettings.asyncUploadTimeSlice = Mathf.Clamp(sl, 1, 33);
                }
            }
            if (StreamingEnabled) Debug.Log($"[stream] policy sliced={sliced} startOnActiveRing={startOnActiveRing} budget={frameBudgetMs:F1}ms upload={QualitySettings.asyncUploadBufferSize}MB/{QualitySettings.asyncUploadTimeSlice}ms");
            if (!StreamingEnabled) { LoadEverything(); return; }
            if (!CellManifest.TryLoad(out var manifest, out var error)) { FailStartup(error); return; }
            Manifest = manifest;
            cells = new Cell[manifest.l0.Length];
            for (int i = 0; i < cells.Length; i++)
            {
                var definition = manifest.l0[i];
                if (!string.IsNullOrEmpty(definition.sceneName))
                {
                    if (!Application.CanStreamedLevelBeLoaded(definition.sceneName))
                    { FailStartup("scene missing from build: " + definition.sceneName); return; }
                    var existing = FindScene(definition.sceneName);
                    if (existing.IsValid())
                    { FailStartup("cell scene already resident; scheduler must own its scenes: " + definition.sceneName); return; }
                }
                cells[i] = new Cell { definition = definition };
                index.Add(definition.coordinate, i);
            }
            // Slots registered by other components' Start/Awake may predate manifest initialization.
            for (int i = 0; i < systems.Count; i++) systems[i].requested = new bool[cells.Length];
            for (int i = 0; i < dataSystems.Length; i++)
                if (dataSystems[i] is ICellDataSystem system) RegisterDataSystem(system);
            if (lookaheadComponent is ILookaheadSource source) LookaheadSource = source;
            savedPriority = Application.backgroundLoadingPriority;
            ownsPriority = true;
            InitialLoading = true;
            Application.backgroundLoadingPriority = LoadingPriority.High;
            // paused from the start, not only once the truck is found: it stood over a road whose cell was not loaded yet and fell
            // through. (Not Physics.simulationMode = Script: forces added by FixedUpdate then pile up and launch the truck.)
            savedTimeScale = Time.timeScale; Time.timeScale = 0; ownsPause = true;
            Status = "waiting for player";
            if (maxL0LoadsInFlight > 1 || maxScenesWaitingForActivation > 1)
                Debug.Log("[stream] serialized scene gate caps effective concurrency/waiters at 1 (Unity async queue)");
        }

        /// -stream 0 in a build whose world was split into cell scenes (ProbeBuild.MacStream): every cell is loaded at the start
        /// and shown, nothing is ever unloaded (A/B against streaming). A build of the unsplit scene has no manifest: nothing happens.
        void LoadEverything()
        {
            if (!CellManifest.TryLoad(out var manifest, out _)) return;
            int n = 0; double t0 = Time.realtimeSinceStartupAsDouble;
            SceneManager.sceneLoaded += ShowAll;
            foreach (var c in manifest.l0)
                if (!string.IsNullOrEmpty(c.sceneName) && Application.CanStreamedLevelBeLoaded(c.sceneName) && !FindScene(c.sceneName).IsValid())
                { SceneManager.LoadScene(c.sceneName, LoadSceneMode.Additive); n++; }
            if (n > 0) { allPending = n; allTimeScale = Time.timeScale; Time.timeScale = 0; } // (no physics step before the roads are there)
            Status = $"-stream 0: all {n} cell scenes loaded at the start";
            Debug.Log($"[stream] {Status} (queued in {(Time.realtimeSinceStartupAsDouble - t0) * 1000:F0} ms)");
        }
        static void ShowAll(Scene s, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Additive || !s.path.StartsWith(CellGrid.GeneratedRoot)) return;
            foreach (var g in s.GetRootGameObjects()) g.SetActive(true);
            if (allPending > 0 && --allPending == 0) { if (Time.timeScale == 0) Time.timeScale = allTimeScale; Debug.Log($"[stream] -stream 0: every cell shown at {Time.realtimeSinceStartup:F1} s"); }
        }
        static int allPending; static float allTimeScale = 1;

        void FailStartup(string reason)
        {
            StreamingEnabled = false;
            Status = reason;
            Debug.LogWarning("[stream] idle: " + reason);
        }

        void OnValidate()
        {
            activeRadius = Mathf.Max(0, activeRadius);
            preloadRadius = Mathf.Max(activeRadius, preloadRadius);
            unloadRadius = Mathf.Max(preloadRadius + 1, unloadRadius);
            maxL0LoadsInFlight = Mathf.Max(1, maxL0LoadsInFlight);
            maxScenesWaitingForActivation = Mathf.Clamp(maxScenesWaitingForActivation, 1, 1);
        }

        /// <summary>Register during startup/OnEnable, with the matching blob kind. Default All receives every cell.
        /// Returns false for duplicate registration. Buffers allocate only at registration.</summary>
        public bool RegisterDataSystem(ICellDataSystem system, CellDataKind kind = CellDataKind.All)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            for (int i = 0; i < systems.Count; i++) if (ReferenceEquals(systems[i].system, system)) return false;
            systems.Add(new DataSlot { system = system, kind = kind, requested = new bool[cells.Length] });
            return true;
        }

        /// <summary>The system must release/cancel its own cells before unregistering (e.g. its OnDisable).</summary>
        public void UnregisterDataSystem(ICellDataSystem system)
        {
            for (int i = 0; i < systems.Count; i++)
                if (ReferenceEquals(systems[i].system, system)) { systems.RemoveAt(i); return; }
        }

        public bool IsCellActive(Vector2Int coordinate) => StreamingEnabled &&
            index.TryGetValue(coordinate, out int i) && cells[i].ready;

        public bool TryGetCell(Vector2Int coordinate, out L0Cell definition)
        {
            if (index.TryGetValue(coordinate, out int i)) { definition = cells[i].definition; return true; }
            definition = null; return false;
        }

        // Run late enough to see gameplay's initial placement and vehicle swaps, independently of the chase camera.
        void LateUpdate()
        {
            if (!StreamingEnabled) return;
            frameStart = Time.realtimeSinceStartupAsDouble;
            if (Dbg) DumpDt();
            if (!RefreshPlayer()) { log.Tick(this); return; }
            if (!started)
            {
                if (!truck.IsReady) { log.Tick(this); return; }
                started = true;
                if (!ownsPause) { savedTimeScale = Time.timeScale; Time.timeScale = 0; ownsPause = true; }
                Status = "loading start rings";
            }
            RefreshPath();
            UpdateWants(); // Also drops stale, not-yet-started requests on reversals/teleports.
            // the admission budget is for the work that follows (activations, gate, loads, unloads), not this bookkeeping:
            // measured from here (drive test: bookkeeping alone used the 1.5 ms on a busy M2, nothing loaded, the ring ran out)
            frameStart = Time.realtimeSinceStartupAsDouble;
            PollOperations();
            ReleaseSceneGate();
            StepRoots();
            StepDataSystems();
            StartOneUnload();
            StartOneLoad();
            UpdateMetrics();
            if (InitialLoading && StartRingsReady()) FinishInitialLoad();
            else if (!InitialLoading && UnderTruckMissing()) CatchUp();
            if (ElapsedMs >= frameBudgetMs) HitBudget();
            log.Tick(this);
        }

        bool RefreshPlayer()
        {
            var game = MagistralaGame.I;
            var current = game && game.Truck ? game.Truck : truck;
            double now = Time.realtimeSinceStartupAsDouble;
            if (!current && now >= nextPlayerSearch)
            {
                nextPlayerSearch = now + 1;
                var chase = FindFirstObjectByType<TruckChaseCamera>();
                current = chase && chase.target ? chase.target : MagistralaGame.DrivenTruck();
            }
            if (!current) return false;
            if (current != truck) { truck = current; body = truck.GetComponent<Rigidbody>(); nextPathRefresh = 0; }
            position = truck.transform.position;
            velocity = body && !body.isKinematic ? body.linearVelocity : truck.transform.forward * (truck.SpeedKmh / 3.6f);
            // a kinematic truck (FlyTest -flykmh) reports no velocity: from its motion instead
            if (body && body.isKinematic && Time.deltaTime > 0) { var v = (position - kinPrev) / Time.deltaTime; if (v.sqrMagnitude < 100 * 100) kinVel = Vector3.Lerp(kinVel, v, .2f); velocity = kinVel; }
            kinPrev = position;
            velocity.y = 0;
            return true;
        }

        Vector3 kinPrev, kinVel;
        /// -streamdbg (measuring): every scheduler operation and every frame over 33 ms as a [sev] line with the frame number, so
        /// hitches can be matched to what streaming did in that frame or the one before (release players too)
        public static bool Dbg;
        /// sliced activation (see Cell.units) and a start that waits only for the active ring (the preload ring follows while driving)
        bool sliced = true, startOnActiveRing = true;
        static void Ev(string s) { Debug.Log($"[sev] f={Time.frameCount} t={Time.realtimeSinceStartup:F2} {s}"); }
        /// every frame's unscaled dt (ms, rounded) as one [sevdt] line per 10 s, first frame number in front: event-locked averages
        /// offline without a log line per frame
        static readonly System.Text.StringBuilder dtLine = new System.Text.StringBuilder(8192);
        static int dtFirst = -1; static double dtFlushAt;
        static void DumpDt()
        {
            if (Time.unscaledDeltaTime > .033f) Ev($"dt={Time.unscaledDeltaTime * 1000:F0}");
            if (dtFirst < 0) { dtFirst = Time.frameCount; dtLine.Clear(); dtFlushAt = Time.realtimeSinceStartupAsDouble + 10; }
            dtLine.Append(' ').Append(Mathf.RoundToInt(Time.unscaledDeltaTime * 1000));
            if (Time.realtimeSinceStartupAsDouble < dtFlushAt) return;
            Debug.Log($"[sevdt] f0={dtFirst}{dtLine}");
            dtFirst = -1;
        }
        void RefreshPath()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if ((position - previousPathPosition).sqrMagnitude > CellGrid.L0 * CellGrid.L0 ||
                Vector3.Dot(velocity, previousPathVelocity) < 0) nextPathRefresh = 0;
            if (now < nextPathRefresh) return;
            previousPathPosition = position; previousPathVelocity = velocity;
            nextPathRefresh = now + Mathf.Max(.1f, lookaheadRefreshSeconds);
            if (LookaheadSource == null && !graphLookupAttempted)
            {
                graphLookupAttempted = true;
                var game = MagistralaGame.I;
                graphWorld = game && game.World ? game.World : Resources.Load<GameWorld>(GameWorld.ResourcePath);
                if (graphWorld && graphWorld.nodes != null && graphWorld.edges != null && graphWorld.pts != null)
                    LookaheadSource = new RoadGraphLookaheadSource(graphWorld, game && game.Router != null ? game.Router : new RoadRouter(graphWorld));
            }
            pathCount = LookaheadSource != null ? Mathf.Clamp(LookaheadSource.WritePath(position, velocity, lookaheadSeconds, path), 0, path.Length) : 0;
            if (pathCount < 2)
            {
                path[0] = position;
                path[1] = position + velocity * lookaheadSeconds;
                pathCount = 2;
            }
        }

        void UpdateWants()
        {
            // the path's box (+ corridor): cells outside it skip the per-segment tests (117 cells × up to 255 segments every frame)
            float px0 = float.MaxValue, pz0 = float.MaxValue, px1 = float.MinValue, pz1 = float.MinValue;
            for (int p = 0; p < pathCount; p++) { var q = path[p]; px0 = Mathf.Min(px0, q.x); px1 = Mathf.Max(px1, q.x); pz0 = Mathf.Min(pz0, q.z); pz1 = Mathf.Max(pz1, q.z); }
            px0 -= lookaheadCorridorRadius; pz0 -= lookaheadCorridorRadius; px1 += lookaheadCorridorRadius; pz1 += lookaheadCorridorRadius;
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                c.distance = DistanceXZ(position, c.definition.bounds);
                c.wantActive = c.distance <= activeRadius;
                var bmin = c.definition.bounds.min; var bmax = c.definition.bounds.max;
                bool ahead = false, outside = bmax.x < px0 || bmin.x > px1 || bmax.z < pz0 || bmin.z > pz1;
                // A segment/AABB test, not point sampling: no skipped cells on a long straight edge.
                if (!outside) for (int p = 1; p < pathCount && !ahead; p++)
                    ahead = SegmentIntersects(path[p - 1], path[p], c.definition.bounds, lookaheadCorridorRadius);
                c.wantPreload = c.distance <= preloadRadius || ahead;
                c.keep = c.distance < unloadRadius || ahead;
                c.priority = c.distance - (ahead ? CellGrid.L0 : 0) - (c.wantActive ? 100000f : 0);
            }
        }

        double ElapsedMs => (Time.realtimeSinceStartupAsDouble - frameStart) * 1000;
        bool HasBudget(double estimate = .05)
        {
            bool fits = InitialLoading || ElapsedMs + estimate <= frameBudgetMs;
            if (!fits) HitBudget();
            return fits;
        }

        void HitBudget()
        {
            if (budgetHitFrame == Time.frameCount) return;
            budgetHitFrame = Time.frameCount; BudgetHits++;
        }
        // slow frames defer an activation, but never for long: on a 30 fps machine every frame is "slow" (drive test: at 40–60 fps
        // with spikes the gate stayed shut, the active ring emptied and the catch-up pause had to step in)
        int slowDeferrals;
        bool CanActivate
        {
            get
            {
                if (activationFrame == Time.frameCount || !HasBudget(Math.Min(Math.Max(LastActivationMs, .1), frameBudgetMs * .75))) return false;
                if (InitialLoading || Time.unscaledDeltaTime * 1000 <= slowFrameThresholdMs || slowDeferrals >= 6) { slowDeferrals = 0; return true; }
                slowDeferrals++; return false;
            }
        }

        void PollOperations()
        {
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                if (c.state == Residency.Unloading && c.unload.isDone)
                { c.unload = null; c.scene = default; c.roots.Clear(); c.units.Clear(); c.shown = 0; c.prepared = false; c.state = Residency.Absent; if (Dbg) Ev("unloaded " + c.definition.key); }
            }
            if (load == null || !load.isDone) return;
            activationFrame = Time.frameCount; // Scene integration completed this frame; do not enable another root.
            var loaded = cells[loadingIndex];
            LastSceneActivationLatencyMs = (Time.realtimeSinceStartupAsDouble - gateReleasedAt) * 1000;
            loaded.scene = FindScene(loaded.definition.sceneName);
            if (!loaded.scene.IsValid() || !loaded.scene.isLoaded)
            { loaded.state = Residency.Failed; CellFailed("scene load failed: " + loaded.definition.sceneName); }
            else
            {
                loaded.state = Residency.Resident; loaded.units.Clear(); loaded.shown = 0; loaded.prepared = false;
                if (HitchDiag.On) HitchDiag.Note($"scene+ {loaded.definition.key} {LastSceneActivationLatencyMs:F0}ms");
                if (Dbg) Ev($"scene+ {loaded.definition.key} latency {LastSceneActivationLatencyMs:F0}ms");
                loaded.roots.Capacity = Mathf.Max(loaded.roots.Capacity, loaded.scene.rootCount + 1);
                loaded.scene.GetRootGameObjects(loaded.roots);
                for (int i = 0; i < loaded.roots.Count; i++)
                    if (loaded.roots[i].activeSelf)
                    {
                        // Invalid bake: prevent a visible preload flash on the next render. Awake has already run.
                        loaded.roots[i].SetActive(false);
                        CellFailed("scene roots must be saved inactive: " + loaded.definition.sceneName);
                    }
            }
            load = null; loadingIndex = -1; gateReleased = false;
        }

        void ReleaseSceneGate()
        {
            if (load == null || gateReleased || load.progress < .9f) return;
            // Release even if the player has moved away: Unity cannot cancel an in-flight scene load.
            if (!CanActivate) return;
            activationFrame = Time.frameCount;
            gateReleasedAt = Time.realtimeSinceStartupAsDouble;
            gateReleased = true;
            load.allowSceneActivation = true;
            if (HitchDiag.On) HitchDiag.Note("gate " + cells[loadingIndex].definition.key);
            if (Dbg) Ev("gate " + cells[loadingIndex].definition.key);
        }

        void StepRoots()
        {
            if (sliced) { StepUnits(); return; }
            if (!CanActivate) return;
            for (int n = 0; n < cells.Length; n++)
            {
                int i = (rootCursor + n) % cells.Length;
                var c = cells[i];
                if (c.state != Residency.Resident) continue;
                for (int r = 0; r < c.roots.Count; r++)
                {
                    var root = c.roots[r];
                    if (!root || root.activeSelf == c.wantActive) continue;
                    double before = Time.realtimeSinceStartupAsDouble;
                    root.SetActive(c.wantActive); // A root is the indivisible bake unit. Keep it small.
                    LastActivationMs = (Time.realtimeSinceStartupAsDouble - before) * 1000;
                    MaxActivationMs = Math.Max(MaxActivationMs, LastActivationMs);
                    if (HitchDiag.On) HitchDiag.Note($"root {c.definition.key} {(c.wantActive ? "on" : "off")} {LastActivationMs:F1}ms");
                    if (Dbg) Ev($"root {c.definition.key} {(c.wantActive ? "on" : "off")} {LastActivationMs:F2}ms");
                    activationFrame = Time.frameCount;
                    rootCursor = (i + 1) % cells.Length;
                    return;
                }
            }
        }

        /// sliced activation: the nearest cell that wants to be shown first; units are switched on (or off, for cells leaving the
        /// active ring, only when nothing waits to be shown) one after another until the frame's budget is used. A unit is one
        /// object under root/<group>/ (a merged 256 m block, a building's LOD group, a sign, a cell's collider mesh): its
        /// SetActive (renderer / collider registration, OnEnable) is the indivisible step instead of the whole cell's.
        void StepUnits()
        {
            if (activationFrame == Time.frameCount) return; // a scene integrated this frame
            if (!InitialLoading && Time.unscaledDeltaTime * 1000 > slowFrameThresholdMs && slowDeferrals < 6) { slowDeferrals++; return; }
            slowDeferrals = 0;
            int steps = 0;
            while (true)
            {
                int best = -1; float bp = float.PositiveInfinity; bool show = false;
                for (int i = 0; i < cells.Length; i++)
                {
                    var c = cells[i];
                    if (c.state != Residency.Resident || c.roots.Count == 0) continue;
                    bool need = c.wantActive ? (!c.prepared || c.shown < c.units.Count) : c.shown > 0;
                    if (!need) continue;
                    // showing beats hiding; nearer first
                    float p = c.wantActive ? c.priority - 1e7f : c.priority;
                    if (p < bp) { bp = p; best = i; show = c.wantActive; }
                }
                if (best < 0) return;
                double estimate = Math.Min(Math.Max(LastActivationMs, .02), frameBudgetMs * .75);
                if (steps > 0 && !HasBudget(estimate)) return;
                var cell = cells[best];
                double before = Time.realtimeSinceStartupAsDouble;
                string what;
                if (show && !cell.prepared) { Prepare(cell); what = "prep"; }
                else if (show) { cell.units[cell.shown++].SetActive(true); what = "on"; }
                else { cell.units[--cell.shown].SetActive(false); what = "off"; }
                LastActivationMs = (Time.realtimeSinceStartupAsDouble - before) * 1000;
                MaxActivationMs = Math.Max(MaxActivationMs, LastActivationMs);
                if (Dbg && LastActivationMs > .5) Ev($"unit {cell.definition.key} {what} {(what == "on" ? cell.units[cell.shown - 1].name : what == "off" ? cell.units[cell.shown].name : "")} {LastActivationMs:F2}ms");
                steps++;
                if (!InitialLoading && steps >= 64) return;
            }
        }

        /// the root's children-of-groups that were saved active are hidden while the root is still inactive (no callbacks), then the
        /// root goes on: nothing of the cell is drawn or collides until its units are shown
        static void Prepare(Cell c)
        {
            c.units.Clear(); c.shown = 0;
            for (int r = 0; r < c.roots.Count; r++)
            {
                var root = c.roots[r]; if (!root) continue;
                var rt = root.transform;
                if (root.activeSelf) root.SetActive(false);
                for (int g = 0; g < rt.childCount; g++)
                {
                    var group = rt.GetChild(g);
                    if (!group.gameObject.activeSelf) continue;
                    for (int u = 0; u < group.childCount; u++)
                    {
                        var unit = group.GetChild(u).gameObject;
                        if (!unit.activeSelf) continue;
                        unit.SetActive(false); c.units.Add(unit);
                    }
                }
            }
            for (int r = 0; r < c.roots.Count; r++) if (c.roots[r]) c.roots[r].SetActive(true);
            c.prepared = true;
        }

        void StepDataSystems()
        {
            for (int s = 0; s < systems.Count; s++)
            {
                var slot = systems[s];
                if (!SystemAlive(slot.system)) continue;
                for (int n = 0; n < cells.Length; n++)
                {
                    int i = (slot.cursor + n) % cells.Length;
                    var c = cells[i];
                    bool wanted = c.wantActive && c.state == Residency.Resident && Receives(slot, c);
                    if (slot.requested[i] == wanted) continue;
                    if (!HasBudget()) return;
                    // Exactly one mutation per system/frame, even if its implementation is synchronous.
                    try
                    {
                        if (wanted) slot.system.LoadCell(c.definition.coordinate);
                        else slot.system.UnloadCell(c.definition.coordinate);
                        slot.requested[i] = wanted;
                    }
                    catch (Exception e)
                    {
                        CellFailed("data callback failed: " + e.Message);
                        // A throwing implementation must be repaired/re-registered; avoid per-frame log/exception churn.
                        systems.RemoveAt(s--);
                    }
                    slot.cursor = (i + 1) % cells.Length;
                    break;
                }
            }
        }

        static bool SystemAlive(ICellDataSystem system) => !(system is UnityEngine.Object obj) || obj;
        static bool Receives(DataSlot slot, Cell c) => slot.kind == CellDataKind.All || (slot.kind & c.definition.blobs.Available) != 0;

        bool DataReady(int i)
        {
            var c = cells[i];
            for (int s = 0; s < systems.Count; s++)
            {
                var slot = systems[s];
                if (SystemAlive(slot.system) && Receives(slot, c) &&
                    (!slot.requested[i] || !slot.system.IsLoaded(c.definition.coordinate))) return false;
            }
            return true;
        }

        bool DataReleased(int i)
        {
            for (int s = 0; s < systems.Count; s++) if (systems[s].requested[i]) return false;
            return true;
        }

        bool RootsVisible(Cell c)
        {
            if (sliced) return c.prepared && c.shown == c.units.Count;
            for (int i = 0; i < c.roots.Count; i++) if (c.roots[i] && !c.roots[i].activeSelf) return false;
            return true;
        }

        void StartOneUnload()
        {
            // Any held scene stalls unloads. Drain the gate first; never enqueue work behind it.
            if (load != null || !HasBudget()) return;
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                if (c.state != Residency.Resident || c.keep || !DataReleased(i) || c.shown > 0) continue;
                if (!c.scene.IsValid()) { c.state = Residency.Absent; return; } // data-only
                c.unload = SceneManager.UnloadSceneAsync(c.scene);
                if (HitchDiag.On) HitchDiag.Note("unload " + c.definition.key);
                if (Dbg) Ev("unload " + c.definition.key);
                if (c.unload == null) { c.state = Residency.Failed; CellFailed("unload rejected: " + c.definition.key); }
                else c.state = Residency.Unloading;
                return;
            }
        }

        void StartOneLoad()
        {
            if (load != null || !HasBudget()) return;
            // Avoid starting a gate while an unload is still pending in Unity's shared async queue.
            for (int i = 0; i < cells.Length; i++) if (cells[i].state == Residency.Unloading) return;
            int best = -1;
            float priority = float.PositiveInfinity;
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                if (c.state == Residency.Absent && c.wantPreload && c.priority < priority)
                { priority = c.priority; best = i; }
            }
            if (best < 0) return;
            var cell = cells[best];
            if (string.IsNullOrEmpty(cell.definition.sceneName)) { cell.state = Residency.Resident; return; }
            try
            {
                load = SceneManager.LoadSceneAsync(cell.definition.sceneName, LoadSceneMode.Additive);
                if (load == null) throw new InvalidOperationException("LoadSceneAsync returned null");
                load.allowSceneActivation = false;
                load.priority = cell.wantActive ? 100 : cell.priority < cell.distance ? 10 : 0;
                loadingIndex = best; gateReleased = false;
                cell.state = Residency.Loading; LoadsStarted++;
                if (Dbg) Ev($"load {cell.definition.key} d={cell.distance:F0} prio={load.priority}");
            }
            catch (Exception e)
            { load = null; cell.state = Residency.Failed; CellFailed("load rejected: " + e.Message); }
        }

        void UpdateMetrics()
        {
            ActiveCount = PreloadedCount = LoadingCount = QueueLength = 0;
            NearestInactiveAheadMetres = float.PositiveInfinity;
            Vector3 direction = velocity.sqrMagnitude > 1 ? velocity.normalized : truck.transform.forward;
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                c.ready = c.wantActive && c.state == Residency.Resident && RootsVisible(c) && DataReady(i);
                if (c.ready) ActiveCount++;
                else if (c.state == Residency.Resident) PreloadedCount++;
                if (c.state == Residency.Loading) LoadingCount++;
                if (c.state == Residency.Absent && c.wantPreload) QueueLength++;
                // Ray/AABB entry distance in the driving direction (not distance to an arbitrary cell centre).
                if (!c.ready && RayEntry(position, direction, c.definition.bounds, out float distance))
                    NearestInactiveAheadMetres = Mathf.Min(NearestInactiveAheadMetres, distance);
            }
        }

        bool StartRingsReady()
        {
            if (HasFailedCells) return false;
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                if (c.wantPreload && !startOnActiveRing && c.state != Residency.Resident) return false;
                if (c.wantActive && !c.ready) return false;
            }
            return true;
        }

        /// a teleport (job start, -tpxz, vehicle swap) or a stall: the ground under the truck is not shown yet
        bool UnderTruckMissing()
        {
            for (int i = 0; i < cells.Length; i++)
            {
                var c = cells[i];
                if (c.distance <= 60 && !string.IsNullOrEmpty(c.definition.sceneName) && !c.ready && !HasFailedCells) return true;
            }
            return false;
        }

        /// the start's pause again (black overlay, scaled time 0, High priority) until the rings are back: never a fall through the road
        void CatchUp()
        {
            InitialLoading = true;
            if (!ownsPause) { savedTimeScale = Time.timeScale; Time.timeScale = 0; ownsPause = true; }
            Application.backgroundLoadingPriority = LoadingPriority.High;
            Status = "catching up (teleport)";
            Debug.Log($"[stream] catching up at x={position.x - 4200:F0} z={-position.z - 5300:F0}");
        }

        /// vehicles placed (MagistralaGame.Place / PlaceAtStopFor: a ground raycast) before their cell's colliders existed stand on
        /// the terrain under a yard / road surface: lifted onto the highest surface at most 3 m above their wheels' ground
        static readonly RaycastHit[] groundHits = new RaycastHit[32];
        void Reground()
        {
            foreach (var tc in FindObjectsByType<TruckController>(FindObjectsSortMode.None))
            {
                var rb = tc.GetComponent<Rigidbody>(); var p = tc.transform.position; float ground = p.y - 1.1f, best = float.NegativeInfinity;
                int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 30, p.z), Vector3.down, groundHits, 80, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    var h = groundHits[i];
                    if (h.collider.attachedRigidbody || h.transform.IsChildOf(tc.transform)) continue;
                    if (h.point.y <= ground + 3f && h.point.y > best) best = h.point.y;
                }
                if (float.IsNegativeInfinity(best) || best <= ground + .05f) continue;
                var q = p + Vector3.up * (best - ground);
                tc.transform.position = q; if (rb) { rb.position = q; rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                Debug.Log($"[stream] {tc.name} lifted {best - ground:F2} m onto its cell's ground at x={q.x - 4200:F0} z={-q.z - 5300:F0}");
            }
        }

        void FinishInitialLoad()
        {
            Physics.SyncTransforms(); Reground();
            InitialLoading = false;
            RestorePause();
            Application.backgroundLoadingPriority = LoadingPriority.Low;
            Status = "driving";
            Debug.Log("[stream] start rings ready");
        }

        void CellFailed(string reason)
        {
            HasFailedCells = true; Status = reason;
            Debug.LogError("[stream] " + reason);
            // A bad bake must not trap the player permanently behind a black loading overlay.
            if (InitialLoading) { InitialLoading = false; RestorePause(); Application.backgroundLoadingPriority = LoadingPriority.Low; }
        }

        void RestorePause()
        {
            if (!ownsPause) return;
            if (Time.timeScale == 0) Time.timeScale = savedTimeScale;
            ownsPause = false;
        }

        void OnDisable()
        {
            if (Instance != this) return;
            // Never leave Unity's global async queue locked if this component is disabled/destroyed.
            if (load != null) load.allowSceneActivation = true;
            RestorePause();
            if (ownsPriority) Application.backgroundLoadingPriority = savedPriority;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void OnGUI()
        {
            if (!StreamingEnabled || !InitialLoading || !showInitialFade) return;
            int oldDepth = GUI.depth; Color oldColor = GUI.color;
            GUI.depth = -10000; GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            HudStyle.Label(new Rect(0, Screen.height * .45f, Screen.width, HudStyle.Px(40)), "Učitavanje svijeta…",
                HudStyle.St(24, true, Color.white, TextAnchor.MiddleCenter));
            GUI.color = oldColor; GUI.depth = oldDepth;
        }

        static Scene FindScene(string sceneName)
        {
            var scene = SceneManager.GetSceneByPath(sceneName);
            return scene.IsValid() ? scene : SceneManager.GetSceneByName(sceneName);
        }

        static float DistanceXZ(Vector3 p, Bounds b)
        {
            float x = Mathf.Max(Mathf.Max(b.min.x - p.x, 0), p.x - b.max.x);
            float z = Mathf.Max(Mathf.Max(b.min.z - p.z, 0), p.z - b.max.z);
            return Mathf.Sqrt(x * x + z * z);
        }

        static bool SegmentIntersects(Vector3 a, Vector3 b, Bounds bounds, float padding)
        {
            Vector3 min = bounds.min, max = bounds.max;
            float lo = 0, hi = 1;
            return Slab(a.x, b.x - a.x, min.x - padding, max.x + padding, ref lo, ref hi) &&
                Slab(a.z, b.z - a.z, min.z - padding, max.z + padding, ref lo, ref hi);
        }

        static bool RayEntry(Vector3 p, Vector3 dir, Bounds bounds, out float distance)
        {
            float lo = 0, hi = float.PositiveInfinity;
            bool hit = Slab(p.x, dir.x, bounds.min.x, bounds.max.x, ref lo, ref hi) &&
                Slab(p.z, dir.z, bounds.min.z, bounds.max.z, ref lo, ref hi);
            distance = lo; return hit;
        }

        static bool Slab(float p, float d, float min, float max, ref float lo, ref float hi)
        {
            if (Mathf.Abs(d) < .00001f) return p >= min && p <= max;
            float a = (min - p) / d, b = (max - p) / d;
            if (a > b) { float t = a; a = b; b = t; }
            lo = Mathf.Max(lo, a); hi = Mathf.Min(hi, b);
            return hi >= lo;
        }
    }
}
