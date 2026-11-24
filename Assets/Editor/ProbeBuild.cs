// Player builds of the probe: Builds/mac/Magistrala.app and Builds/win/Magistrala.exe (Mono).
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class ProbeBuild
    {
        static string Build(BuildTarget target, BuildTargetGroup group, string path, bool dev = false)
        {
            MainScene.EnsureSaved();
            PlayerSettings.companyName = "Magistrala"; PlayerSettings.productName = "Magistrala Unity proba";
            PlayerSettings.runInBackground = true; PlayerSettings.enableFrameTimingStats = true;
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultScreenWidth = 1920; PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.FromBuildTargetGroup(group), ScriptingImplementation.Mono2x);
            var opts = new BuildPlayerOptions { scenes = new[] { "Assets/Scenes/Magistrala.unity" }, locationPathName = path, target = target, targetGroup = group, options = (dev ? BuildOptions.Development : BuildOptions.None) | BuildOptions.CompressWithLz4HC };
            var r = BuildPipeline.BuildPlayer(opts);
            return $"{target}: {r.summary.result} {r.summary.totalSize / 1048576f:F0} MB, {r.summary.totalTime.TotalSeconds:F0} s, errors {r.summary.totalErrors}";
        }
        [MenuItem("Magistrala/Build macOS")] public static string Mac() => Build(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, "Builds/mac/Magistrala.app");
        // development player with profiler markers in the [perf] log line (Builds/macprof): for measuring only, not for Marko
        public static string MacProfile() => Build(BuildTarget.StandaloneOSX, BuildTargetGroup.Standalone, "Builds/macprof/Magistrala.app", true);
        // Windows: D3D11 for now. There is no D3D12 PSO recording yet (ShaderWarmup's GraphicsStateCollection is per platform + API,
        // only warmup-osxplayer-metal exists); on D3D11 the ShaderVariantCollection fallback (Resources/Shaderi/warmup, menu
        // "Magistrala/Shaderi: SVC iz GSC") removes most first-use shader stalls, on D3D12 nothing would. To go back to D3D12:
        // record with WinTrace() on Windows (snimi-shadere.bat), commit the file as
        // Assets/StreamingAssets/warmup-windowsplayer-direct3d12.graphicsstate, then remove the WinApis(...) line below.
        [MenuItem("Magistrala/Build Windows")] public static string Win()
        {
            WinApis(UnityEngine.Rendering.GraphicsDeviceType.Direct3D11);
            return Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, "Builds/win/Magistrala.exe");
        }
        /// the recording build for Marko's Windows PC: D3D12, Builds/win-snimka/ with snimi-shadere.bat (-gsctrace) and UPUTE.txt.
        /// Leaves the D3D11 setting of Win() behind (each build sets its own APIs).
        [MenuItem("Magistrala/Build Windows (snimka shadera D3D12)")] public static string WinTrace()
        {
            WinApis(UnityEngine.Rendering.GraphicsDeviceType.Direct3D12);
            string r;
            try { r = Build(BuildTarget.StandaloneWindows64, BuildTargetGroup.Standalone, "Builds/win-snimka/Magistrala.exe"); }
            finally { WinApis(UnityEngine.Rendering.GraphicsDeviceType.Direct3D11); }
            const string file = "warmup-windowsplayer-direct3d12.graphicsstate"; // = ShaderWarmup.FileName on WindowsPlayer / Direct3D12
            System.IO.Directory.CreateDirectory("Builds/win-snimka");
            System.IO.File.WriteAllText("Builds/win-snimka/snimi-shadere.bat",
                "@echo off\r\n" +
                "cd /d \"%~dp0\"\r\n" +
                "echo Snimka shadera ide u: %~dp0" + file + "\r\n" +
                "echo Igra koristi vlastiti save (snimka.sav), tvoj pravi save se ne dira.\r\n" +
                "start \"\" /wait \"%~dp0Magistrala.exe\" -gsctrace \"%~dp0" + file + "\" -mute -savefile \"%~dp0snimka.sav\"\r\n" +
                "echo Gotovo. Posalji datoteku: %~dp0" + file + "\r\n" +
                "pause\r\n");
            System.IO.File.WriteAllText("Builds/win-snimka/UPUTE.txt",
                "Snimanje shadera (da igra na Windowsu ne zastajkuje kad prvi put nešto nacrta)\r\n\r\n" +
                "1. Dvaput klikni snimi-shadere.bat (igra ima svoj save, tvoj pravi save se ne dira; zvuk je namjerno ugašen).\r\n" +
                "2. Vozi ~10 minuta i proberi što više toga:\r\n" +
                "   - D8 po danu;\r\n" +
                "   - V uključi kišu i vozi po kiši;\r\n" +
                "   - G zalazak (pa opet G);\r\n" +
                "   - C pogled iz kabine – u svim vozilima (E uzmi drugo vozilo kad staneš kraj njega: Atego u skladištu, Galeb u dvoru Bašića, kombi);\r\n" +
                "   - riva;\r\n" +
                "   - Tab mobitel, M karta.\r\n" +
                "3. Izađi normalno: Esc (ako je karta otvorena, prvo nju zatvori pa opet Esc). Snimka se sprema i svakih 30 s.\r\n" +
                "4. Pošalji dvije datoteke:\r\n" +
                "   - " + file + " (u mapi pokraj Magistrala.exe)\r\n" +
                "   - Player.log iz %USERPROFILE%\\AppData\\LocalLow\\Magistrala\\Magistrala Unity proba\\Player.log\r\n");
            return r;
        }
        static void WinApis(UnityEngine.Rendering.GraphicsDeviceType api)
        {
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { api });
        }
    }
}
