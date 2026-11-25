// Measuring (streaming A/B): a [load] line on the first frames (seconds since the process started) and, with -scenestats,
// a [scene] line 8 s in: GameObjects, MeshRenderers, colliders, LODGroups loaded (all scenes, inactive included).
using UnityEngine;

namespace Magistrala.Streaming
{
    public class SceneStats : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            Debug.Log($"[load] scene loaded at {Time.realtimeSinceStartup:F2} s");
            var go = new GameObject("SceneStats"); DontDestroyOnLoad(go); go.AddComponent<SceneStats>();
        }
        int frame; bool stats;
        void Start() => stats = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-scenestats") >= 0;
        void Update()
        {
            frame++;
            if (frame == 1 || frame == 60) Debug.Log($"[load] frame {frame} at {Time.realtimeSinceStartup:F2} s");
            if (stats && Time.timeSinceLevelLoad > 8) { stats = false; Log(); }
        }
        public static void Log()
        {
            int go = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int mr = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int mra = FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length;
            int col = FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            int lg = FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
            Debug.Log($"[scene] gameObjects={go} meshRenderers={mr} (active {mra}) colliders={col} lodGroups={lg} scenes={UnityEngine.SceneManagement.SceneManager.sceneCount}");
        }
    }
}
