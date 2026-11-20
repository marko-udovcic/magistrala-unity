using System.Reflection;
using UnityEngine;

namespace Magistrala
{
    /// F1: Graphy (fps / ms / RAM graphs), F2: In-game Debug Console (log + commands). Both hidden by default (-debug: both shown).
    /// The prefab Resources/DebugOverlays holds the two package prefabs; this attaches itself (no scene change), like
    /// RenderQuality. Graphy is only created on the first F1 (costs nothing until then); the console is created at start,
    /// hidden with its canvas off, so it already holds the log when it is first opened. Their own hotkeys are turned off
    /// (Graphy's G / H would clash with G zalazak), the keys are read here with the Input System.
    public class DebugOverlays : MonoBehaviour
    {
        public GameObject graphyPrefab, consolePrefab;

        GameObject graphy, console;
        Component graphyManager;
        Canvas[] consoleCanvases;
        bool graphyOn, consoleOn;
        /// the F1 graph is shown (TruckHud: the fps readout and the key hints)
        public static bool GraphOn { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            if (FindFirstObjectByType<DebugOverlays>()) return;
            var prefab = Resources.Load<GameObject>("DebugOverlays");
            if (!prefab) { Debug.LogWarning("[debug] Resources/DebugOverlays nema"); return; }
            var go = Instantiate(prefab); go.name = "DebugOverlays"; DontDestroyOnLoad(go);
        }

        // instantiate under an inactive holder so the private serialized fields can be set before Awake / OnEnable run
        static GameObject Spawn(GameObject prefab, System.Type type, params (string field, object value)[] set)
        {
            var holder = new GameObject("hold"); holder.SetActive(false);
            var go = Instantiate(prefab, holder.transform); go.name = prefab.name;
            var c = go.GetComponent(type);
            foreach (var (field, value) in set)
            {
                var f = type.GetField(field, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f == null) { Debug.LogWarning($"[debug] {type.Name}.{field} nema"); continue; }
                f.SetValue(c, f.FieldType.IsEnum ? System.Enum.ToObject(f.FieldType, value) : value);
            }
            go.transform.SetParent(null, false); DontDestroyOnLoad(go); Destroy(holder);
            return go;
        }

        System.Collections.IEnumerator Start()
        {
            if (consolePrefab)
            {
                console = Spawn(consolePrefab, typeof(IngameDebugConsole.DebugLogManager),
                    ("startMinimized", true), ("toggleWithKey", false), ("popupVisibility", (int)IngameDebugConsole.PopupVisibility.Never));
                consoleCanvases = console.GetComponentsInChildren<Canvas>(true);
                SetConsole(false);
            }
            // -debug: both shown from the start (screenshots, checking a build without a keyboard); a frame later, after the
            // console's own Start has hidden its window
            yield return null;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-debug") >= 0) { SetGraphy(true); SetConsole(true); }
        }

        void SetConsole(bool on)
        {
            consoleOn = on;
            var m = IngameDebugConsole.DebugLogManager.Instance;
            if (consoleCanvases != null) foreach (var c in consoleCanvases) if (c) c.enabled = on;
            if (!m) return;
            if (on) m.ShowLogWindow(); else if (m.IsLogWindowVisible) m.HideLogWindow();
        }

        void SetGraphy(bool on)
        {
            graphyOn = on; GraphOn = on;
            if (!graphy && on && graphyPrefab)
            {
                graphy = Spawn(graphyPrefab, typeof(Tayx.Graphy.GraphyManager),
                    ("m_enableHotkeys", false), ("m_toggleModeInputSystemKey", 0), ("m_toggleActiveInputSystemKey", 0), ("m_enableOnStartup", true),
                    ("m_audioModuleState", 4), ("m_advancedModuleState", 4)); // fps + RAM top right; audio / device info off (the info sat over the HUD)
                graphyManager = graphy.GetComponent<Tayx.Graphy.GraphyManager>();
                return; // starts enabled
            }
            if (graphyManager) { var g = (Tayx.Graphy.GraphyManager)graphyManager; if (on) g.Enable(); else g.Disable(); }
        }

        void Update()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return;
            if (kb.f1Key.wasPressedThisFrame) SetGraphy(!graphyOn);
            if (kb.f2Key.wasPressedThisFrame) SetConsole(!consoleOn);
        }
    }
}
