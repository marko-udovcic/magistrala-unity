using UnityEngine;

namespace Magistrala
{
    /// FPS log (every 10 s to the log: Player.log in builds) and the command-line switch `-autopilot` (the truck drives the D8
    /// by itself: benchmark / demo). F1 toggles the autopilot? No: P (Autopilot). Esc quits a build.
    public class ProbeBench : MonoBehaviour
    {
        float acc; int frames; float worst; float t0;
        void Start()
        {
            t0 = Time.realtimeSinceStartup;
            foreach (var a in System.Environment.GetCommandLineArgs())
                if (a == "-autopilot") { var ap = FindFirstObjectByType<Autopilot>(); if (ap) ap.drive = true; }
        }
        void Update()
        {
            float dt = Time.unscaledDeltaTime; acc += dt; frames++; worst = Mathf.Max(worst, dt);
            if (acc >= 10f)
            {
                var tr = FindFirstObjectByType<TruckController>();
                Debug.Log($"[bench] t={Time.realtimeSinceStartup - t0:F0}s fps={frames / acc:F1} worst={worst * 1000:F0}ms res={Screen.width}x{Screen.height} truck x={(tr ? tr.transform.position.x - 4200 : 0):F0} v={(tr ? tr.SpeedKmh : 0):F0}");
                acc = 0; frames = 0; worst = 0;
            }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (!Application.isEditor && kb != null && kb.escapeKey.wasPressedThisFrame) Application.Quit();
        }
    }
}
