// The driven vehicle's sound (Atego or Galeb), read from TruckController state only — no physics changes.
//
// Engine: 4 rpm steps × 2 load layers (…_on full load, …_off coasting) per vehicle (Atego 600/1200/1800/2400, Galeb
// 750/1800/2800/3800 rpm, seamless loops), crossfaded by EngineRpm with equal power; each layer is pitched by rpm / its step,
// so neighbours meet at the same pitch. Only the two bracketing steps play (≤ 4 voices), silent ones are stopped.
// Load = smoothed TruckController.Throttle; on / off layers crossfaded with equal power.
// One set of sources follows whichever vehicle is driven (MagistralaGame.Truck), so the parked one costs nothing.
//
// One-shots from state changes:  Gear change → gear_shift · Gear −1 → reverse_beep loop (Atego) · brake held ≥ 0.6 s then
// released → air_brake_short (stopped: air_brake; Atego, at most every 2.5 s) · handbrake on / off → handbrake_atego (+ release
// hiss) / handbrake_galeb · H → horn loop · vehicle switch → old: stop + door, new: door + start · unloading: FirmUnload (roll-up
// door, tail lift, pallet jack) or by hand (rear door, boxes) · TruckController.Hit → crash / metal / thud by impact speed.
using UnityEngine;
using UnityEngine.InputSystem;

namespace Magistrala.Audio
{
    [DisallowMultipleComponent]
    public sealed class TruckAudio : MonoBehaviour
    {
        sealed class Set
        {
            public string veh; public float[] rpm; public string[] tag; public Vector3 engine, rear;
            public string shift, handbrake, horn, start, stop, doorOpen, doorClose; public bool airBrakes, beeper;
            public AudioClip[] on, off; public AudioClip cShift, cHand, cHorn, cStart, cStop, cDoorOpen, cDoorClose;
        }
        static readonly Set Galeb = new Set
        {
            veh = "galeb", rpm = new[] { 750f, 1800f, 2800f, 3800f }, tag = new[] { "idle", "1800", "2800", "3800" },
            engine = new Vector3(0, 0.8f, 2.2f), rear = new Vector3(0, 1.2f, -3.0f),
            shift = "motor/gear_shift_galeb", handbrake = "motor/handbrake_galeb", horn = "motor/horn_galeb_loop",
            start = "motor/galeb_start", stop = "motor/galeb_stop", doorOpen = "motor/door_galeb_open", doorClose = "motor/door_galeb_close",
            airBrakes = false, beeper = false,
        };

        MagistralaAudio A;
        TruckController tc; Set set;
        Transform engineAt, rearAt;
        readonly AudioSource[] on = new AudioSource[4], off = new AudioSource[4];
        AudioSource horn, beep, lift, roll;
        AudioClip cBrake, cBrakeShort, cRollOpen, cRollClose, cPump, cCrash; AudioClip[] cThud, cMetal;
        float load, startT = -1, engineLevel, brakeHeld, lastHiss = -10, findT, crateT, rollT, lastUnloadT = -1;
        int lastGear = 1, lastRowsOff; bool lastHand, wasUnloading, hornOn;
        public static TruckAudio I { get; private set; }
        public TruckController Target => tc;

        void Start()
        {
            I = this;
            A = MagistralaAudio.I;
            foreach (var s in new[] { Galeb })
            {
                s.on = new AudioClip[4]; s.off = new AudioClip[4];
                for (int i = 0; i < 4; i++) { s.on[i] = A.Clip($"motor/{s.veh}_engine_{s.tag[i]}_on"); s.off[i] = A.Clip($"motor/{s.veh}_engine_{s.tag[i]}_off"); }
                s.cShift = A.Clip(s.shift); s.cHand = A.Clip(s.handbrake); s.cHorn = A.Clip(s.horn); s.cStart = A.Clip(s.start); s.cStop = A.Clip(s.stop);
                s.cDoorOpen = A.Clip(s.doorOpen); s.cDoorClose = A.Clip(s.doorClose);
            }
            cBrake = A.Clip("motor/air_brake"); cBrakeShort = A.Clip("motor/air_brake_short");
            cRollOpen = A.Clip("istovar/rollup_door_open"); cRollClose = A.Clip("istovar/rollup_door_close"); cPump = A.Clip("istovar/pallet_jack_pump");
            cCrash = A.Clip("udarci/crash_big");
            cThud = new[] { A.Clip("udarci/hit_thud1"), A.Clip("udarci/hit_thud2") };
            cMetal = new[] { A.Clip("udarci/hit_metal1"), A.Clip("udarci/hit_metal2"), A.Clip("udarci/hit_metal3") };
            engineAt = new GameObject("zvuk_motor").transform; engineAt.SetParent(transform, false);
            rearAt = new GameObject("zvuk_straga").transform; rearAt.SetParent(transform, false);
            for (int i = 0; i < 4; i++)
            {
                on[i] = A.NewSource("eng_on_" + i, engineAt, Bus.Motor, true, true, 32, 5, 180);
                off[i] = A.NewSource("eng_off_" + i, engineAt, Bus.Motor, true, true, 32, 5, 180);
            }
            horn = A.NewSource("horn", engineAt, Bus.Motor, true, true, 30, 8, 400);
            beep = A.NewSource("reverse_beep", rearAt, Bus.Motor, true, true, 40, 4, 120); beep.clip = A.Clip("motor/reverse_beep");
            lift = A.NewSource("tail_lift", rearAt, Bus.Motor, true, true, 50, 3, 80); lift.clip = A.Clip("istovar/tail_lift_loop");
            roll = A.NewSource("pallet_roll", rearAt, Bus.Motor, true, true, 50, 3, 60); roll.clip = A.Clip("istovar/pallet_jack_roll");
        }

        TruckController FindDriven()
        {
            var g = Game.MagistralaGame.I;
            if (g != null && g.Truck) return g.Truck;
            return FindFirstObjectByType<TruckController>();
        }

        void Update()
        {
            if (A == null) return;
            float dt = Time.deltaTime;
            findT -= Time.unscaledDeltaTime;
            if (findT <= 0 || !tc)
            {
                findT = 0.5f;
                var d = FindDriven();
                if (d != tc) Switch(d);
            }
            if (!tc || !tc.isActiveAndEnabled) { Silence(); return; }
            // follow the vehicle (the sources are not children of it: the parked vehicle's root is switched off)
            engineAt.SetPositionAndRotation(tc.transform.TransformPoint(set.engine), tc.transform.rotation);
            rearAt.SetPositionAndRotation(tc.transform.TransformPoint(set.rear), tc.transform.rotation);
            if (startT >= 0) startT += dt;
            float want = startT < 0 ? 0 : Mathf.Clamp01((startT - 2.75f) / 0.4f); // in under the end of the 3.2 s start one-shot
            engineLevel = Mathf.MoveTowards(engineLevel, want, dt * 2.5f);
            Engine(dt);
            Events(dt);
            Unload(dt);
        }

        void Switch(TruckController d)
        {
            if (tc)
            {
                tc.Hit -= OnHit;
                // getting out of the old one: engine off, door
                A.PlayAt(set.cStop, engineAt.position, Bus.Motor, 1f, 1f, 5, 180);
                A.PlayAt(set.cDoorOpen, tc.transform.TransformPoint(-1.1f, 1.2f, set.engine.z), Bus.Motor, 0.8f, 1f, 2, 40);
                Silence();
            }
            tc = d;
            if (!tc) return;
            set = Galeb; // Shared van diesel; no legacy truck sound set is loaded.
            for (int i = 0; i < 4; i++) { on[i].clip = set.on[i]; off[i].clip = set.off[i]; }
            horn.clip = set.cHorn;
            tc.Hit += OnHit;
            lastGear = tc.Gear; lastHand = tc.HandbrakeOn; engineLevel = 0;
            startT = 0;
            engineAt.SetPositionAndRotation(tc.transform.TransformPoint(set.engine), tc.transform.rotation);
            A.PlayAt(set.cDoorClose, tc.transform.TransformPoint(-1.1f, 1.2f, set.engine.z), Bus.Motor, 0.8f, 1f, 2, 40);
            A.PlayAt(set.cStart, engineAt.position, Bus.Motor, 1f, 1f, 5, 180);
            Debug.Log($"[zvuk] vozilo {set.veh}: start");
        }

        void Silence()
        {
            for (int i = 0; i < 4; i++) { on[i].Stop(); off[i].Stop(); on[i].volume = off[i].volume = 0; }
            horn.Stop(); beep.Stop(); lift.Stop(); roll.Stop();
            startT = -1; engineLevel = 0;
        }

        void Engine(float dt)
        {
            float rpm = tc.EngineRpm;
            // reverse drives with the brake key (TruckController: gas = brakeInput in reverse) → treat moving in reverse as half load
            float thr = tc.Gear < 0 ? (tc.SpeedKmh > 0.5f && tc.BrakeLevel < 0.05f ? 0.6f : 0f) : tc.Throttle;
            load = Mathf.MoveTowards(load, thr, dt * (thr > load ? 4f : 2.5f));
            var r = set.rpm;
            int i0 = 0;
            while (i0 < 2 && rpm > r[i0 + 1]) i0++;
            float x = Mathf.Clamp01(Mathf.InverseLerp(r[i0], r[i0 + 1], rpm));
            float rpmN = Mathf.InverseLerp(r[0], r[3], rpm);
            float master = engineLevel * Mathf.Lerp(0.6f, 1f, rpmN) * Mathf.Lerp(0.85f, 1f, load) * A.Gain(Bus.Motor);
            float lOn = Mathf.Sin(load * Mathf.PI / 2), lOff = Mathf.Cos(load * Mathf.PI / 2);
            for (int i = 0; i < 4; i++)
            {
                float g = i == i0 ? Mathf.Cos(x * Mathf.PI / 2) : i == i0 + 1 ? Mathf.Sin(x * Mathf.PI / 2) : 0f;
                float pitch = Mathf.Clamp(rpm / r[i], 0.6f, 1.6f);
                Drive(on[i], g * lOn * master, pitch);
                Drive(off[i], g * lOff * master, pitch);
            }
        }

        static void Drive(AudioSource s, float vol, float pitch)
        {
            s.volume = vol;
            s.pitch = pitch;
            if (vol > 0.002f) { if (!s.isPlaying && s.clip) { s.timeSamples = Random.Range(0, s.clip.samples); s.Play(); } }
            else if (s.isPlaying) s.Stop();
        }

        static void Fade(AudioSource s, float want, float rate, float dt)
        {
            s.volume = Mathf.MoveTowards(s.volume, want, dt * rate);
            if (want > 0) { if (!s.isPlaying && s.clip) s.Play(); }
            else if (s.volume <= 0 && s.isPlaying) s.Stop();
        }

        void Events(float dt)
        {
            var k = Keyboard.current;
            Vector3 p = engineAt.position;
            float gm = A.Gain(Bus.Motor);
            int g = tc.Gear;
            if (g != lastGear)
            {
                A.PlayAt(set.cShift, p, Bus.Motor, 0.8f, Random.Range(0.96f, 1.04f), 3, 60);
                lastGear = g;
            }
            Fade(beep, g < 0 && set.beeper && engineLevel > 0.5f ? 0.7f * gm : 0f, 20f, dt);
            if (set.airBrakes)
            {
                float b = tc.BrakeLevel;
                if (b > 0.3f) brakeHeld += dt;
                else if (b < 0.05f)
                {
                    if (brakeHeld > 0.6f && Time.time - lastHiss > 2.5f)
                    {
                        bool stopped = tc.SpeedKmh < 1f;
                        A.PlayAt(stopped ? cBrake : cBrakeShort, tc.transform.TransformPoint(0, 0.5f, -1f), Bus.Motor, stopped ? 0.8f : 0.5f, Random.Range(0.95f, 1.05f), 3, 80);
                        lastHiss = Time.time;
                    }
                    brakeHeld = 0;
                }
            }
            bool hb = tc.HandbrakeOn;
            if (hb != lastHand)
            {
                if (hb) A.PlayAt(set.cHand, p, Bus.Motor, 0.8f, 1f, 3, 60);
                else if (set.airBrakes) A.PlayAt(cBrakeShort, p, Bus.Motor, 0.45f, 1.15f, 3, 60);
                lastHand = hb;
            }
            hornOn = k != null && k.hKey.isPressed && !Game.WorldMap.IsOpen;
            Fade(horn, hornOn ? gm : 0f, hornOn ? 50f : 16f, dt);
        }

        void Unload(float dt)
        {
            var game = Game.MagistralaGame.I;
            bool unloading = game != null && game.Truck == tc && game.State == Game.MagistralaGame.Phase.Unloading;
            var fu = game != null && game.Truck == tc ? game.Unload : null;
            float liftWant = 0;
            float gm = A.Gain(Bus.Motor);
            if (fu && fu.Running)
            {
                const float TDoor = 1.25f, TFold = 1.25f; // FirmUnload's (private) phase times
                float t = fu.T, dur = fu.Duration;
                if (lastUnloadT < 0) A.PlayAt(cRollOpen, rearAt.position, Bus.Motor, 0.9f, 1f, 3, 60);
                if (lastUnloadT >= 0 && lastUnloadT < dur - TDoor && t >= dur - TDoor) A.PlayAt(cRollClose, rearAt.position, Bus.Motor, 0.9f, 1f, 3, 60);
                bool folding = (t > TDoor && t < TDoor + TFold) || (t > dur - TDoor - TFold && t < dur - TDoor);
                liftWant = folding ? 0.8f : (t > TDoor + TFold && t < dur - TDoor - TFold ? 0.4f : 0f);
                if (lastUnloadT >= 0 && fu.RowsOff != lastRowsOff) { A.PlayAt(cPump, rearAt.position, Bus.Motor, 0.7f, 1f, 2, 40); rollT = 1.6f; }
                lastRowsOff = fu.RowsOff;
                lastUnloadT = t;
            }
            else
            {
                if (lastUnloadT >= 0) lastUnloadT = -1;
                lastRowsOff = fu ? fu.RowsOff : 0;
                // by hand (Galeb faturete, crates): the rear door, then boxes set down now and then
                if (unloading && !wasUnloading) A.PlayAt(set.cDoorOpen, rearAt.position, Bus.Motor, 0.8f, 0.8f, 2, 40);
                if (!unloading && wasUnloading && !(fu && fu.Running)) A.PlayAt(set.cDoorClose, rearAt.position, Bus.Motor, 0.8f, 0.8f, 2, 40);
                if (unloading)
                {
                    crateT -= dt;
                    if (crateT <= 0) { crateT = Random.Range(1.2f, 2.4f); A.PlayAt(cThud[Random.Range(0, cThud.Length)], rearAt.position + rearAt.forward * -1.2f, Bus.Motor, 0.5f, Random.Range(0.85f, 1.1f), 2, 40); }
                }
            }
            wasUnloading = unloading;
            rollT -= dt;
            Fade(lift, liftWant * gm, 4f, dt);
            Fade(roll, rollT > 0 ? 0.6f * gm : 0f, 4f, dt);
        }

        void OnHit(Collision c)
        {
            float v = c.relativeVelocity.magnitude;
            if (v < 1.5f) return;
            var clip = v > 8f ? cCrash : v > 4f ? cMetal[Random.Range(0, cMetal.Length)] : cThud[Random.Range(0, cThud.Length)];
            A.PlayAt(clip, c.contactCount > 0 ? c.GetContact(0).point : tc.transform.position, Bus.Motor, Mathf.Clamp01(0.3f + v / 12f), Random.Range(0.9f, 1.1f), 4, 120);
        }

        public bool HornOn => hornOn;
    }
}
