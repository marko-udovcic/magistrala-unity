// Free camera for the AutiParking scene (car candidates on a parking lot).
// Right mouse drag = look, WASD = move, Q/E = down/up, Shift = faster, scroll = speed,
// 1–9 = jump to that car, 0 = overview, Esc = quit (in a build).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Magistrala.Kandidati
{
    public class ParkingCam : MonoBehaviour
    {
        public Transform[] cars;
        public string[] names;
        float yaw, pitch, speed = 6f;
        Vector3 homePos; Quaternion homeRot;
        GUIStyle style;

        // the game's AfterSceneLoad bootstraps (MagistralaGame, HUD, weather, drone…) start in every scene; this lot only needs its own objects
        static readonly string[] Keep = { "Sunce", "Volume", "Asfalt", "Linije", "Auti", "Main Camera" };
        int cleanFrames = 3;
        void Clean()
        {
            var roots = new List<GameObject>(gameObject.scene.GetRootGameObjects());
            var probe = new GameObject("probe"); DontDestroyOnLoad(probe); roots.AddRange(probe.scene.GetRootGameObjects()); Destroy(probe);
            foreach (var g in roots) if (g && g != probe && System.Array.IndexOf(Keep, g.name) < 0) Destroy(g);
        }

        void LateUpdate() { if (cleanFrames-- > 0) Clean(); }

        void Start()
        {
            Clean();
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
            homePos = transform.position; homeRot = transform.rotation;
            var e = transform.eulerAngles; yaw = e.y; pitch = e.x;
        }

        void Update()
        {
            var kb = Keyboard.current; var ms = Mouse.current;
            if (kb == null || ms == null) return;
            if (ms.rightButton.isPressed)
            {
                var d = ms.delta.ReadValue() * 0.15f;
                yaw += d.x; pitch = Mathf.Clamp(pitch - d.y, -89f, 89f);
                transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            }
            float sc = ms.scroll.ReadValue().y;
            if (sc != 0) speed = Mathf.Clamp(speed * (sc > 0 ? 1.2f : 1 / 1.2f), 0.5f, 60f);
            var mv = Vector3.zero;
            if (kb.wKey.isPressed) mv += transform.forward;
            if (kb.sKey.isPressed) mv -= transform.forward;
            if (kb.dKey.isPressed) mv += transform.right;
            if (kb.aKey.isPressed) mv -= transform.right;
            if (kb.eKey.isPressed) mv += Vector3.up;
            if (kb.qKey.isPressed) mv -= Vector3.up;
            transform.position += mv * speed * (kb.leftShiftKey.isPressed ? 3f : 1f) * Time.deltaTime;

            if (kb.digit0Key.wasPressedThisFrame) Look(homePos, homeRot);
            Key[] keys = { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9 };
            for (int i = 0; i < keys.Length && cars != null && i < cars.Length; i++)
                if (kb[keys[i]].wasPressedThisFrame) Focus(cars[i]);
            if (kb.escapeKey.wasPressedThisFrame && !Application.isEditor) Application.Quit();
        }

        void Focus(Transform car)
        {
            var b = new Bounds(car.position, Vector3.zero);
            foreach (var r in car.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
            var dir = (car.right * 1.0f + car.forward * 1.2f + Vector3.up * 0.55f).normalized;
            var pos = b.center + dir * (b.extents.magnitude * 2.1f);
            Look(pos, Quaternion.LookRotation(b.center - pos));
        }

        void Look(Vector3 p, Quaternion r)
        {
            transform.SetPositionAndRotation(p, r);
            var e = r.eulerAngles; yaw = e.y; pitch = e.x > 180 ? e.x - 360 : e.x;
        }

        void OnGUI()
        {
            if (style == null) style = new GUIStyle(GUI.skin.label) { fontSize = 15, richText = true };
            var s = "<b>Auti – kandidati</b>   desni klik + miš = gledaj, WASD = kreći se, Q/E = dolje/gore, Shift = brže, kotačić = brzina\n" +
                    "1–" + (cars != null ? cars.Length : 0) + " = skoči na auto, 0 = pregled";
            if (names != null) for (int i = 0; i < names.Length; i++) s += (i % 4 == 0 ? "\n" : "     ") + (i + 1) + ". " + names[i];
            GUI.color = new Color(0, 0, 0, 0.55f); GUI.DrawTexture(new Rect(8, 8, 900, 96), Texture2D.whiteTexture); GUI.color = Color.white;
            GUI.Label(new Rect(16, 12, 890, 96), s, style);
        }
    }
}
