// Review shots of the riva palms in play mode (28. 11.): the truck put on the riva street, the camera either the chase camera
// (truck height) or fixed at street level; ScreenCapture at the end of a frame, the frame's render stats logged.
//   unity command script eval 'Magistrala.EditorTools.PalmShots.Prep(); return Magistrala.EditorTools.PalmShots.Truck(gx, gz, yaw);'
//   … PalmShots.Eye(gx, gz, h, yaw, pitch)   … PalmShots.Shot("<file.png>")   … PalmShots.Stats()
// Play mode only; never touches the save (MAGISTRALA_SAVEFILE must point to a scratch file — Prep refuses otherwise).
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class PalmShots
    {
        public static string Prep()
        {
            if (string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("MAGISTRALA_SAVEFILE"))) return "NO MAGISTRALA_SAVEFILE: refusing";
            EditorUtility.audioMasterMute = true; AudioListener.volume = 0;
            var p = typeof(Magistrala.Audio.MagistralaAudio).GetProperty("Muted", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            p?.SetValue(null, true);
            return "muted";
        }

        public static string Truck(float gx, float gz, float yaw)
        {
            if (!Application.isPlaying) return "not playing";
            var tr = Magistrala.Game.MagistralaGame.DrivenTruck(); if (!tr) return "no truck";
            var u = new Vector3(gx + 4200, 300, -(gz + 5300));
            if (Physics.Raycast(u, Vector3.down, out var hit, 600, ~0, QueryTriggerInteraction.Ignore)) u.y = hit.point.y + 1.1f;
            var rb = tr.GetComponent<Rigidbody>(); if (rb) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            tr.transform.SetPositionAndRotation(u, Quaternion.Euler(0, yaw, 0)); if (rb) { rb.position = u; rb.rotation = tr.transform.rotation; }
            var ch = Camera.main ? Camera.main.GetComponent<TruckChaseCamera>() : null; if (ch) ch.enabled = true;
            return $"truck at {u}";
        }

        public static string Eye(float gx, float gz, float h, float yaw, float pitch)
        {
            if (!Camera.main) return "no camera";
            var ch = Camera.main.GetComponent<TruckChaseCamera>(); if (ch) ch.enabled = false;
            var u = new Vector3(gx + 4200, 300, -(gz + 5300)); float y = 0;
            if (Physics.Raycast(u, Vector3.down, out var hit, 600, ~0, QueryTriggerInteraction.Ignore)) y = hit.point.y;
            Camera.main.transform.SetPositionAndRotation(new Vector3(u.x, y + h, u.z), Quaternion.Euler(pitch, yaw, 0));
            return $"eye {Camera.main.transform.position}";
        }

        public static string Shot(string path) { ScreenCapture.CaptureScreenshot(path, 1); return "queued " + path; }

        public static string Stats()
        {
            var tr = System.Array.Find(Object.FindObjectsByType<Magistrala.TreeRenderer>(FindObjectsSortMode.None), t => t.name == "Vegetation");
            return $"batches {UnityStats.batches} setpass {UnityStats.setPassCalls} tris {UnityStats.triangles} verts {UnityStats.vertices}" +
                   (tr ? $" | vegetation tris main {tr.TrisMain} shadow {tr.TrisShadow} drawn {tr.DrawnLastFrame}" : "");
        }
    }
}
