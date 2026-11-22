// The world menus and the player build work on Assets/Scenes/Magistrala.unity only.
// 22.11.: the editor reopened AutiParking.unity (last scene) and the whole rebuild went into it and was saved there, while
// ProbeBuild built the stale Magistrala.unity from disk - its cells pointed at the regenerated Generated/TileBuildings meshes
// (same paths, other houses): houses invisible, only their shadow hulls and AC units left.
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Magistrala.EditorTools
{
    public static class MainScene
    {
        public const string PATH = "Assets/Scenes/Magistrala.unity";

        /// before a world menu: the main scene is open (opened here when the open scene has nothing unsaved), else an error
        public static void Ensure()
        {
            var s = SceneManager.GetActiveScene();
            if (s.path == PATH) return;
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException($"[Magistrala] open scene is {s.path}, not {PATH}, and {SceneManager.GetSceneAt(i).path} has unsaved changes: save or discard it and open {PATH} first");
            EditorSceneManager.OpenScene(PATH, OpenSceneMode.Single);
            UnityEngine.Debug.Log($"[Magistrala] opened {PATH} (was {s.path})");
        }

        /// before a player build: the build takes the scene from disk, so it must not differ from what is open
        public static void EnsureSaved()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var s = SceneManager.GetSceneAt(i);
                if (s.isDirty && s.path == PATH) throw new InvalidOperationException($"[Magistrala] {PATH} has unsaved changes: save it before the build (the build reads it from disk)");
                if (s.isDirty && s.path != PATH && s.GetRootGameObjects().Length > 0 && Array.Exists(s.GetRootGameObjects(), g => g.name == "World"))
                    throw new InvalidOperationException($"[Magistrala] the world was built in {s.path} (unsaved), not in {PATH}: the build would take the old {PATH}");
            }
            // generated building meshes newer than the scene: they were rebuilt into another scene (or not saved), and the
            // scene's cells would show other houses' meshes
            const string GEN = "Assets/Generated/TileBuildings";
            if (System.IO.Directory.Exists(GEN))
            {
                var scene = System.IO.File.GetLastWriteTimeUtc(PATH); var newest = DateTime.MinValue;
                foreach (var f in System.IO.Directory.GetFiles(GEN, "*.asset")) { var t = System.IO.File.GetLastWriteTimeUtc(f); if (t > newest) newest = t; }
                if (newest > scene.AddMinutes(1))
                    throw new InvalidOperationException($"[Magistrala] {GEN} ({newest.ToLocalTime():HH:mm}) is newer than {PATH} ({scene.ToLocalTime():HH:mm}): Build tile buildings ran in another scene or was not saved - rebuild in {PATH} and save before the build");
            }
        }
    }
}
