// Sound bootstrap (BalkanDriver/assets-src/zvuk/PLAN.md): the AudioMixer (Resources/Zvuk/MagistralaMixer: groups Motor / Okolina /
// Promet / UI, made by Magistrala/Zvuk: napravi mixer), the player's volumes (PlayerPrefs "zvuk.*", phone app Postavke), the clip
// cache (Resources/Zvuk/<folder>/<name>: Assets/Audio/Resources/Zvuk, copied from assets-src/zvuk/out, not in git), a small pooled
// set of one-shot voices, and the "[zvuk]" log line every 10 s (voices per group, what plays).
// TruckAudio, AmbienceAudio and TrafficAudio live on the same object and take their sources from here.
//
// Switches: -mute   ProbeBench sets AudioListener.volume = 0; nothing here ever raises it (it is only ever set to 0 here too) and
//                   the mixer master goes to −80 dB, so the sound stays off; the sources still run (the bench keeps their cost)
//           -zvuk 0 no sound system at all (no sources, no mixer): the A/B for the frame cost
// Budget: at most 24 real voices (AudioSettings, set before anything plays), 64 virtual; priorities Motor 32 · sirens 60 ·
// ambience 64 · one-shots 128 · traffic 160 (traffic drops out first). No AudioSource per car / boat.
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Audio;

namespace Magistrala.Audio
{
    public enum Bus { Motor = 0, Okolina = 1, Promet = 2, UI = 3 }

    public sealed class MagistralaAudio : MonoBehaviour
    {
        public static MagistralaAudio I { get; private set; }
        public static bool Muted { get; private set; }
        public static bool Off { get; private set; }
        public const int RealVoices = 24, VirtualVoices = 64;

        const string Root = "Zvuk/", MixerPath = "Zvuk/MagistralaMixer";
        static readonly string[] GroupNames = { "Motor", "Okolina", "Promet", "UI" };
        /// 0 = master, 1..4 = the groups (Bus + 1): the mixer's exposed parameters and the PlayerPrefs keys "zvuk.<name>"
        public static readonly string[] VolParams = { "VolMaster", "VolMotor", "VolOkolina", "VolPromet", "VolUI" };
        static readonly string[] VolKeys = { "zvuk.VolMaster", "zvuk.VolMotor", "zvuk.VolOkolina", "zvuk.VolPromet", "zvuk.VolUI" };
        public static readonly string[] VolLabels = { "Glasnoća", "Motor", "Okolina", "Promet", "Mobitel" };
        static readonly float[] VolDefault = { 0.8f, 1f, 0.9f, 0.9f, 0.8f };

        public AudioMixer Mixer { get; private set; }
        readonly AudioMixerGroup[] groups = new AudioMixerGroup[4];
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly List<AudioSource> all = new List<AudioSource>();
        readonly List<Bus> allBus = new List<Bus>();
        const int Shots3D = 6, Shots2D = 2;
        AudioSource[] shots3D, shots2D; int next3D, next2D;
        AudioSource preview;
        /// outside world in the cabin view: 1 outside, ~0.55 in the cabin (with the mixer's low-pass on top)
        public float Outside { get; private set; } = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I) return;
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-zvuk" && args[i + 1] == "0") Off = true;
            if (Off) { Debug.Log("[zvuk] isključen (-zvuk 0)"); return; }
            Muted = System.Array.IndexOf(args, "-mute") >= 0;
            // voice budget before the first source plays (AudioSettings.Reset restarts the audio output)
            var cfg = AudioSettings.GetConfiguration();
            if (cfg.numRealVoices != RealVoices || cfg.numVirtualVoices != VirtualVoices)
            {
                cfg.numRealVoices = RealVoices; cfg.numVirtualVoices = VirtualVoices;
                AudioSettings.Reset(cfg);
            }
            if (Muted) AudioListener.volume = 0; // (ProbeBench does the same in Start; only ever lowered)
            var go = new GameObject("MagistralaAudio");
            DontDestroyOnLoad(go);
            go.AddComponent<MagistralaAudio>();
            go.AddComponent<TruckAudio>();
            go.AddComponent<AmbienceAudio>();
            go.AddComponent<TrafficAudio>();
        }

        void Awake()
        {
            I = this;
            Mixer = Resources.Load<AudioMixer>(MixerPath);
            if (Mixer)
                for (int i = 0; i < 4; i++)
                {
                    var g = Mixer.FindMatchingGroups(GroupNames[i]);
                    groups[i] = g.Length > 0 ? g[0] : null;
                }
            else Debug.LogWarning("[zvuk] Resources/" + MixerPath + " nema: izvori bez mixera (glasnoće se množe po izvoru)");
            shots3D = new AudioSource[Shots3D];
            for (int i = 0; i < Shots3D; i++) shots3D[i] = NewSource("shot3D_" + i, transform, Bus.Okolina, true, false, 128);
            shots2D = new AudioSource[Shots2D];
            for (int i = 0; i < Shots2D; i++) shots2D[i] = NewSource("shot2D_" + i, transform, Bus.Okolina, false, false, 128);
            preview = NewSource("preview", transform, Bus.UI, false, false, 16);
            preview.ignoreListenerPause = true; // the phone pauses the listener: the settings preview must still sound
        }

        void Start() => ApplyVolumes(); // (AudioMixer.SetFloat in Awake of the first frame is ignored)

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var cam = Camera.main;
            var chase = cam ? cam.GetComponent<TruckChaseCamera>() : null;
            bool cabin = chase && chase.CabinView;
            Outside = Mathf.MoveTowards(Outside, cabin ? 0.55f : 1f, dt * 2f);
            if (Mixer)
            {
                float lp = Mathf.Lerp(1100f, 22000f, (Outside - 0.55f) / 0.45f);
                Mixer.SetFloat("LpOkolina", lp); Mixer.SetFloat("LpPromet", lp);
            }
            if (Muted && AudioListener.volume > 0) AudioListener.volume = 0;
            LogLine(dt);
        }

        // ------------------------------------------------------------------ clips
        /// "motor/atego_engine_idle_on" → Resources/Zvuk/motor/atego_engine_idle_on. Cached; warns once if missing.
        public AudioClip Clip(string path)
        {
            if (clips.TryGetValue(path, out var c)) return c;
            c = Resources.Load<AudioClip>(Root + path);
            if (!c) Debug.LogWarning("[zvuk] nema klipa Resources/" + Root + path + " (Assets/Audio/README.md)");
            clips[path] = c;
            return c;
        }

        // ------------------------------------------------------------------ sources
        public AudioMixerGroup Group(Bus b) => groups[(int)b];

        public AudioSource NewSource(string name, Transform parent, Bus bus, bool spatial, bool loop, int priority, float minDist = 4, float maxDist = 150)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.outputAudioMixerGroup = groups[(int)bus];
            s.spatialBlend = spatial ? 1f : 0f;
            s.rolloffMode = AudioRolloffMode.Logarithmic;
            s.minDistance = minDist;
            s.maxDistance = maxDist;
            s.dopplerLevel = 0f;
            s.priority = priority;
            s.volume = 0;
            all.Add(s); allBus.Add(bus);
            return s;
        }

        /// the volume factor a source of this bus applies itself: 1 with the mixer (the groups do it), else the player's volumes;
        /// the outside world (Okolina, Promet) is quieter in the cabin
        public float Gain(Bus b)
        {
            float g = Mixer ? 1f : Volume(0) * Volume((int)b + 1);
            if (b == Bus.Okolina || b == Bus.Promet) g *= Outside;
            return g;
        }

        public AudioSource PlayAt(AudioClip clip, Vector3 pos, Bus bus, float vol = 1, float pitch = 1, float minDist = 4, float maxDist = 150)
        {
            if (!clip) return null;
            var s = shots3D[next3D]; next3D = (next3D + 1) % shots3D.Length;
            s.transform.position = pos;
            s.outputAudioMixerGroup = groups[(int)bus];
            s.minDistance = minDist; s.maxDistance = maxDist;
            s.clip = clip; s.pitch = pitch; s.volume = vol * Gain(bus);
            s.Play();
            Note(clip);
            return s;
        }

        // one-shots played since the last "[zvuk]" line (for the log: no allocation, clip references in a ring)
        readonly AudioClip[] recent = new AudioClip[12]; int recentN, shotsN;
        void Note(AudioClip c) { recent[recentN++ % recent.Length] = c; shotsN++; }

        public AudioSource Play2D(AudioClip clip, Bus bus, float vol = 1, float pitch = 1)
        {
            if (!clip) return null;
            var s = shots2D[next2D]; next2D = (next2D + 1) % shots2D.Length;
            s.outputAudioMixerGroup = groups[(int)bus];
            s.clip = clip; s.pitch = pitch; s.volume = vol * Gain(bus);
            s.Play();
            Note(clip);
            return s;
        }

        // ------------------------------------------------------------------ volumes (0..1, remembered like karta.tamna)
        public static float Volume(int i)
        {
            try { return PlayerPrefs.GetFloat(VolKeys[i], VolDefault[i]); } catch { return VolDefault[i]; }
        }

        public static void SetVolume(int i, float v)
        {
            v = Mathf.Clamp01(Mathf.Round(v * 10f) / 10f);
            try { PlayerPrefs.SetFloat(VolKeys[i], v); } catch { }
            if (!I) return;
            I.ApplyVolumes();
            // a short tick through the changed group (the phone pauses the listener; this voice ignores the pause)
            var c = I.Clip(i == 2 ? "okolina/seagull_7" : i == 3 ? "promet/car_pass" : "motor/indicator_on");
            if (c && i != 3) { I.preview.outputAudioMixerGroup = i == 0 ? I.groups[(int)Bus.UI] : I.groups[i - 1]; I.preview.clip = c; I.preview.volume = I.Mixer ? 1 : Volume(0) * Volume(i); I.preview.Play(); }
        }

        static float ToDb(float v) => v <= 0.0001f ? -80f : Mathf.Max(-80f, 20f * Mathf.Log10(v));

        public void ApplyVolumes()
        {
            if (!Mixer) return;
            for (int i = 0; i < VolParams.Length; i++)
                Mixer.SetFloat(VolParams[i], i == 0 && Muted ? -80f : ToDb(Volume(i)));
        }

        // ------------------------------------------------------------------ "[zvuk]" log (every 10 s, like ProbeBench's [bench])
        float logT, maxVoices; readonly int[] perBus = new int[4]; readonly StringBuilder sb = new StringBuilder(512);
        public int Playing { get; private set; }

        void LogLine(float dt)
        {
            int n = 0;
            for (int i = 0; i < all.Count; i++) if (all[i].isPlaying) n++;
            Playing = n; maxVoices = Mathf.Max(maxVoices, n);
            logT += dt;
            if (logT < 10f) return;
            logT = 0;
            System.Array.Clear(perBus, 0, 4);
            sb.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (!s.isPlaying) continue;
                perBus[(int)allBus[i]]++;
                if (sb.Length < 400 && s.clip) sb.Append(' ').Append(s.clip.name).Append('@').Append(s.volume.ToString("0.00"));
            }
            var amb = GetComponent<AmbienceAudio>();
            sb.Append(" | jednokratni ").Append(shotsN).Append(':');
            for (int i = Mathf.Max(0, recentN - recent.Length); i < recentN; i++) { var c = recent[i % recent.Length]; if (c) sb.Append(' ').Append(c.name); }
            Debug.Log($"[zvuk] t={Time.realtimeSinceStartup:F0}s glasova={n} (max {maxVoices:0}, limit {RealVoices}) motor={perBus[0]} okolina={perBus[1]} promet={perBus[2]} ui={perBus[3]} izvora={all.Count} mixer={(Mixer ? "da" : "ne")} mute={(Muted ? "da" : "ne")} listener={AudioListener.volume:0.##} more={(amb ? amb.Sea : 0):0.00} grad={(amb ? amb.Town : 0):0.0} |{sb}");
            recentN = 0; shotsN = 0;
            maxVoices = 0;
        }
    }
}
