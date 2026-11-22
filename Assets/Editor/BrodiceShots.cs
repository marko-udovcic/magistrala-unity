// Boats check in the real world (coordinator 2026-11-22): -executeMethod Magistrala.EditorTools.BrodiceShots.Batch -out <dir>
// opens Assets/Scenes/Magistrala.unity, places the boats (Brodice: postavi: counts per place, dropped slots and why, in the log and
// <dir>/postavljanje.txt), renders the shots (Kaštel Stari mandrač / pregled, Marina Kaštela (Bonaca), Lukšić, Sućurac, the coast
// road, a pasara close up) with <dir>/brojke.md, then reopens the scene from disk WITHOUT saving (the placement is thrown away).
using System;
using System.IO;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class BrodiceShots
    {
        public static void Batch()
        {
            var a = Environment.GetCommandLineArgs(); int oi = Array.IndexOf(a, "-out");
            string dir = oi >= 0 && oi + 1 < a.Length ? a[oi + 1] : "Temp/brodice"; Directory.CreateDirectory(dir);
            try
            {
                EditorSceneManager.OpenScene(MainScene.PATH, OpenSceneMode.Single);
                var log = BoatsBuilder.Place();
                Debug.Log(log); File.WriteAllText(Path.Combine(dir, "postavljanje.txt"), log + "\n");
                BoatsBuilder.ShotsIn(dir);
            }
            catch (Exception e) { Debug.LogError("[brodice] " + e); }
            MooredBoats.Off = false;
            EditorSceneManager.OpenScene(MainScene.PATH, OpenSceneMode.Single);   // from disk, unsaved changes dropped
            Debug.Log("[brodice] shots -> " + dir);
        }
    }
}
