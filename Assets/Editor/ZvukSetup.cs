// Sound setup (assets-src/zvuk/PLAN.md):
//   * Magistrala/Zvuk: napravi mixer — builds Assets/Resources/Zvuk/MagistralaMixer.mixer: Master → Motor / Okolina / Promet / UI,
//     a Lowpass Simple on Okolina and Promet (the cabin view muffles the outside), exposed parameters VolMaster, VolMotor,
//     VolOkolina, VolPromet, VolUI (dB) and LpOkolina, LpPromet (Hz). AudioMixer has no public creation API: this goes through
//     UnityEditor.Audio.AudioMixerController by reflection (the same calls the Audio Mixer window makes). The asset is committed;
//     rerun only to rebuild it. Batch: -executeMethod Magistrala.EditorTools.ZvukSetup.BuildMixer
//   * ZvukImport (AssetPostprocessor): import settings for the clips under Assets/Audio/ (gitignored, Assets/Audio/README.md):
//     3D sources forced to mono, short one-shots ADPCM decompressed on load, engine/horn/traffic loops Vorbis decompressed on
//     load, ambience Vorbis compressed in memory, the long bells / pass-by streamed. Sample rate preserved (44.1 kHz).
//     After copying the clips in: Magistrala/Zvuk: ponovno uvezi zvukove (or just let Unity import them).
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Magistrala.EditorTools
{
    public static class ZvukSetup
    {
        public const string MixerPath = "Assets/Resources/Zvuk/MagistralaMixer.mixer";
        const BindingFlags F = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [MenuItem("Magistrala/Zvuk: napravi mixer")]
        public static void BuildMixer()
        {
            var asm = typeof(AudioImporter).Assembly; // UnityEditor.CoreModule
            Type tCtrl = asm.GetType("UnityEditor.Audio.AudioMixerController"), tGroup = asm.GetType("UnityEditor.Audio.AudioMixerGroupController"),
                 tEffect = asm.GetType("UnityEditor.Audio.AudioMixerEffectController"), tPath = asm.GetType("UnityEditor.Audio.AudioParameterPath"),
                 tGPath = asm.GetType("UnityEditor.Audio.AudioGroupParameterPath"), tEPath = asm.GetType("UnityEditor.Audio.AudioEffectParameterPath"),
                 tExp = typeof(AudioMixer).Assembly.GetType("UnityEditor.Audio.ExposedAudioParameter") ?? asm.GetType("UnityEditor.Audio.ExposedAudioParameter");
            if (tCtrl == null || tGroup == null || tEffect == null || tGPath == null || tEPath == null || tExp == null)
            { Debug.LogError($"[zvuk] mixer API not found: ctrl {tCtrl} group {tGroup} effect {tEffect} gpath {tGPath} epath {tEPath} exp {tExp}"); return; }

            Directory.CreateDirectory(Path.GetDirectoryName(MixerPath));
            if (File.Exists(MixerPath)) AssetDatabase.DeleteAsset(MixerPath);
            var ctrl = tCtrl.GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { MixerPath });
            var master = tCtrl.GetProperty("masterGroup", F).GetValue(ctrl);
            var createGroup = tCtrl.GetMethod("CreateNewGroup", F);
            var addChild = tCtrl.GetMethod("AddChildToParent", F);
            var addSub = tCtrl.GetMethod("AddNewSubAsset", F);
            var addExposed = tCtrl.GetMethod("AddExposedParameter", F);
            var guidVol = tGroup.GetMethod("GetGUIDForVolume", F);
            var insertEffect = tGroup.GetMethod("InsertEffect", F);
            var exposedProp = tCtrl.GetProperty("exposedParameters", F);

            void Expose(object path, string name)
            {
                addExposed.Invoke(ctrl, new[] { path });
                // the new entry gets a default name ("MyExposedParam …"): rename the one whose guid matches
                var guid = (GUID)tPath.GetField("parameter", F).GetValue(path);
                var arr = (Array)exposedProp.GetValue(ctrl);
                for (int i = 0; i < arr.Length; i++)
                {
                    var e = arr.GetValue(i);
                    if ((GUID)tExp.GetField("guid", F).GetValue(e) != guid) continue;
                    tExp.GetField("name", F).SetValue(e, name);
                    arr.SetValue(e, i);
                }
                exposedProp.SetValue(ctrl, arr);
            }

            Expose(Activator.CreateInstance(tGPath, master, guidVol.Invoke(master, null)), "VolMaster");
            string[] names = { "Motor", "Okolina", "Promet", "UI" };
            foreach (var n in names)
            {
                var g = createGroup.Invoke(ctrl, new object[] { n, false });
                addChild.Invoke(ctrl, new[] { g, master });
                Expose(Activator.CreateInstance(tGPath, g, guidVol.Invoke(g, null)), "Vol" + n);
                if (n == "Okolina" || n == "Promet")
                {
                    var fx = Activator.CreateInstance(tEffect, "Lowpass Simple");
                    addSub.Invoke(ctrl, new[] { fx, (object)false });
                    tEffect.GetMethod("PreallocateGUIDs", F).Invoke(fx, null);
                    insertEffect.Invoke(g, new[] { fx, (object)1 });
                    var pg = (GUID)tEffect.GetMethod("GetGUIDForParameter", F).Invoke(fx, new object[] { "Cutoff freq" });
                    Expose(Activator.CreateInstance(tEPath, g, fx, pg), "Lp" + n);
                    // default 22 kHz (open) in the start snapshot
                    var snap = tCtrl.GetProperty("TargetSnapshot", F)?.GetValue(ctrl) ?? tCtrl.GetProperty("startSnapshot", F)?.GetValue(ctrl);
                    var setVal = tEffect.GetMethod("SetValueForParameter", F);
                    try { if (snap != null && setVal != null) setVal.Invoke(fx, new[] { ctrl, snap, "Cutoff freq", (object)22000f }); }
                    catch (Exception e) { Debug.LogWarning("[zvuk] default cutoff not set (runtime sets LpOkolina / LpPromet anyway): " + e.Message); }
                }
            }
            tCtrl.GetMethod("OnSubAssetChanged", F)?.Invoke(ctrl, null);
            EditorUtility.SetDirty((UnityEngine.Object)ctrl);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MixerPath, ImportAssetOptions.ForceUpdate);
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            var groups = mixer ? mixer.FindMatchingGroups("") : new AudioMixerGroup[0];
            var exposed = ((Array)exposedProp.GetValue(ctrl)).Cast<object>().Select(e => (string)tExp.GetField("name", F).GetValue(e));
            Debug.Log($"[zvuk] mixer {MixerPath}: groups {string.Join(", ", groups.Select(g => g.name))}; exposed {string.Join(", ", exposed)}");
        }

        [MenuItem("Magistrala/Zvuk: ponovno uvezi zvukove")]
        public static void Reimport()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio" }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);
            Debug.Log("[zvuk] reimported Assets/Audio");
        }
    }

    public class ZvukImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith("Assets/Audio/")) return;
            var imp = (AudioImporter)assetImporter;
            string name = Path.GetFileNameWithoutExtension(assetPath), dir = Path.GetFileName(Path.GetDirectoryName(assetPath));
            bool wav = assetPath.EndsWith(".wav");
            bool stereoBed = name.StartsWith("birds_") || name == "wind";
            bool longOne = name == "bells_noon" || name == "bell_toll" || name == "car_pass";
            bool ambBed = dir == "okolina" && !wav && !longOne;
            imp.forceToMono = !stereoBed;
            imp.loadInBackground = ambBed || longOne;
            var s = imp.defaultSampleSettings;
            s.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            s.preloadAudioData = !longOne;
            if (wav) { s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.ADPCM; }
            else if (longOne) { s.loadType = AudioClipLoadType.Streaming; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.5f; }
            else if (ambBed) { s.loadType = AudioClipLoadType.CompressedInMemory; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.5f; }
            else { s.loadType = AudioClipLoadType.DecompressOnLoad; s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.7f; }
            imp.defaultSampleSettings = s;
        }
    }
}
