// Measuring only (no effect on the game): a [cells] line every 10 s in the log — main-thread ms per frame of the instanced world
// systems (trees, parked cars, street lights, boats, props, collider pool, cell loading), the scene's GameObject / renderer /
// collider counts (once, 5 s after start) and memory. Made by itself after the scene loads; nothing in the scene.
using UnityEngine;
using UnityEngine.Profiling;

namespace Magistrala.Cells
{
    public class CellStats : MonoBehaviour
    {
        public const int TREES = 0, PARKED = 1, LAMPS = 2, BOATS = 3, PROPS = 4, COLLIDERS = 5, LOADER = 6, N = 7;
        static readonly string[] Names = { "trees", "parked", "lamps", "boats", "props", "colliders", "loader" };
        static readonly double[] sum = new double[N];
        static readonly float[] max = new float[N];
        /// CellData: loaded cells / instances
        public static System.Func<string> Extra;
        public static void Add(int sys, float ms) { sum[sys] += ms; if (ms > max[sys]) max[sys] = ms; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<CellStats>()) return;
            var go = new GameObject("CellStats") { hideFlags = HideFlags.HideInHierarchy }; DontDestroyOnLoad(go); go.AddComponent<CellStats>();
        }

        float t0, acc; int frames; bool counted;
        void Start() { t0 = Time.realtimeSinceStartup; }

        void Update()
        {
            acc += Time.unscaledDeltaTime; frames++;
            if (!counted && Time.realtimeSinceStartup - t0 > 5) { counted = true; Count(); }
            if (acc < 10) return;
            var sb = new System.Text.StringBuilder($"[cells] t={Time.realtimeSinceStartup - t0:F0}s ms/frame");
            double tot = 0;
            for (int i = 0; i < N; i++) { tot += sum[i]; if (sum[i] > 0) sb.Append($" {Names[i]}={sum[i] / frames:F3}(max {max[i]:F2})"); sum[i] = 0; max[i] = 0; }
            sb.Append($" total={tot / frames:F3}");
            sb.Append($" | mem alloc={Profiler.GetTotalAllocatedMemoryLong() / 1048576}MB reserved={Profiler.GetTotalReservedMemoryLong() / 1048576}MB mono={Profiler.GetMonoUsedSizeLong() / 1048576}MB gfx={Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576}MB");
            if (Extra != null) sb.Append(" | ").Append(Extra());
            Debug.Log(sb.ToString());
            acc = 0; frames = 0;
        }

        static void Count()
        {
            var t = Time.realtimeSinceStartup;
            int go = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int act = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            int mr = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            int lg = FindObjectsByType<LODGroup>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            int col = FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            Debug.Log($"[cells] scene: {go} GameObjects ({act} active), {mr} MeshRenderers, {lg} LODGroups, {col} colliders (counted in {(Time.realtimeSinceStartup - t) * 1000:F0} ms)");
        }
    }
}
