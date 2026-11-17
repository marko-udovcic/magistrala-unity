// Player builds of the probe: Builds/mac/Magistrala.app and Builds/win/Magistrala.exe (Mono).
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class ProbeBuild
    {
        static string Build(BuildTarget target, BuildTargetGroup group, string path)
        {
            PlayerSettings.companyName = "Magistrala"; PlayerSettings.productName = "Magistrala Unity proba";
            PlayerSettings.runInBackground = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group), ScriptingImplementation.Mono2x);
            var opts = new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/Magistrala.unity" }, locationPathName = path, target = target, targetGroup = group, options = BuildOptions.None };
            var r = BuildPipeline.BuildPlayer(opts);
            return $"{target}: {r.summary.result} {r.summary.totalSize / 1048576f:F0} MB, {r.summary.totalTime.TotalSeconds:F0} s, errors {r.summary.totalErrors}";
        }
        [MenuItem("Magistrala/Build macOS")] public static string Mac() => Build(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, "Builds/mac/Magistrala.app");
        [MenuItem("Magistrala/Build Windows")] public static string Win() => Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, "Builds/win/Magistrala.exe");
    }
}
