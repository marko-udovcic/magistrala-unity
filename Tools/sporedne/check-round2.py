#!/usr/bin/env python3
"""Run the actual C# scalar traffic decisions and IDM without starting Unity.
The host/compiler come from the installed Editor; all temporary outputs go to /tmp.
"""
from pathlib import Path
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
MONO = Path('/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge')

def declaration(source, marker):
    start = source.index(marker)
    opening = source.index('{', start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]

side_source = (ROOT / 'Assets/Scripts/Traffic/TrafficSystem.SideStreets.cs').read_text()
rules = declaration(side_source, 'internal static class SideTrafficRules')
has_side_signal = declaration(side_source, 'static bool HasSideSignal(').replace('TrafficSideRoute', 'FakeSideRoute')
side_green = declaration(side_source, 'bool SideGreen(').replace('TrafficSideRoute', 'FakeSideRoute')
runtime = (ROOT / 'Assets/Scripts/Traffic/TrafficSystem.cs').read_text()
idm = declaration(runtime, 'static float Idm(')
light_state = declaration(runtime, 'public int LightState(')
harness = r'''
using System;
using Magistrala.Traffic;
class Mathf {
    public static float Max(float a, float b) => Math.Max(a,b);
    public static float Sqrt(float a) => (float)Math.Sqrt(a);
}
class Regression {
    class Car { public float v = 8, a = 1.7f, b = 3.2f, T = 1.4f; }
    static int checks;
    static void Check(bool condition, string name) {
        if (!condition) throw new Exception(name);
        checks++;
    }
    static float StopAt(float obstacle, out bool oldAccepted, out bool newAccepted) {
        var c = new Car(); float s = obstacle - 40;
        oldAccepted = newAccepted = false;
        for (int i=0; i<3000; i++) {
            float gap = obstacle - s;
            oldAccepted |= c.v < .4f && gap < 2;
            newAccepted |= SideTrafficRules.AtYield(c.v, gap);
            float acc = Math.Max(-9, Math.Min(c.a, Idm(c,4,gap,c.v)));
            float nv = Math.Max(0,c.v + acc * .02f);
            float ds = Math.Min((c.v+nv)*.01f, Math.Max(0,gap-.3f));
            c.v = Math.Min(nv,ds/.02f); s += ds;
        }
        Console.WriteLine("IDM equilibrium gap=" + (obstacle-s).ToString("F6"));
        return s;
    }
    static void Main() {
        bool oldReady, newReady;
        float s = StopAt(96, out oldReady, out newReady);
        Check(newReady, "IDM stop must release at yield");
        Check(SideTrafficRules.AtYield(0,2) && !(0 < .4f && 2 < 2), "exact IDM equilibrium must be accepted, independent of float undershoot");
        float end = StopAt(100-4*.5f-1, out oldReady, out newReady);
        Check(!(end >= 100-4*.5f-2) && SideTrafficRules.AtEnd(100,end,4), "finite-end recycling must be reachable at IDM equilibrium");
        float distance, need;
        Check(SideTrafficRules.GapClear(-16,12,4,25,4,out distance,out need), "departing main car with free merge space must not block");
        Check(!SideTrafficRules.GapClear(60,-64,4,20,4,out distance,out need), "approaching car within four seconds must block");
        Check(SideTrafficRules.GapClear(100,-104,4,20,4,out distance,out need), "clear approaching gap must pass");
        Check(!SideTrafficRules.GapClear(-1,-3,4,0,4,out distance,out need), "occupied crossing must block even at zero speed");
        Check(!SideTrafficRules.HoldsCrossing(false,90,110,4), "waiting inbound must not lock out turn-in plans");
        Check(!SideTrafficRules.HoldsCrossing(true,50,10,4), "deep outbound must release the mouth");
        Check(SideTrafficRules.HoldsCrossing(true,5,10,4), "committed crossing must remain exclusive");
        for (float speed=0; speed<=30; speed+=.5f) {
            float first=-1,last=-1;
            for (float d=0; d<=240; d+=.25f)
                if (SideTrafficRules.TurnWindow(speed,d)) { if(first<0) first=d; last=d; }
            Check(first>=0 && last-first >= Math.Max(5,speed*1.2f), "turn window must allow two decision ticks at speed " + speed);
        }
        var signal = new SignalHarness();
        Check(signal.SideAllows(new FakeSideRoute()), "unsignalled null record must allow entry and turn-in");
        Check(signal.SideAllows(new FakeSideRoute { signal = new FakeStop { plan=0, group="" } }), "Unity's serialized null record must allow entry and turn-in");
        Check(signal.SideAllows(new FakeSideRoute { signal = new FakeStop { plan=0, group=null } }), "null signal group must allow entry and turn-in");
        var realSignal = new FakeSideRoute { signal = new FakeStop { plan=0, group="SG1" } };
        foreach(int light in new int[] {1,2,3}) {
            signal.sideLight=light;
            Check(!signal.SideAllows(realSignal), "real amber/red/pre-green signal must block");
        }
        signal.sideLight=0;
        Check(signal.SideAllows(realSignal), "real green signal must allow entry");
        signal.signals=false; signal.sideLight=2;
        Check(signal.SideAllows(realSignal), "signal A/B override must work");
        Check(signal.LightState(-1,-1)==0, "null data fallback");
        signal.data = new FakeData();
        Check(signal.LightState(0,0)==0, "null corridors fallback");
        signal.data.corridors = new FakeCorridor[] { null, new FakeCorridor() };
        Check(signal.LightState(0,0)==0 && signal.LightState(1,0)==0, "null corridor/stop array fallback");
        signal.data.corridors[1].stops = new FakeStop[] { null, new FakeStop { plan=4 } };
        Check(signal.LightState(1,0)==0 && signal.LightState(1,1)==0, "null stop/null plan array fallback");
        signal.data.plans = new object[] { null };
        Check(signal.LightState(1,1)==0, "plan out of range fallback");
        signal.data.corridors[1].stops[1].plan=0;
        Check(signal.LightState(1,1)==0, "null plan fallback");
        for(int i=0;i<2000;i++) Check(signal.LightState(99,i)==0, "stale head must not throw");
        Check(Debug.warnings==1, "stale heads must warn once per traffic instance");
        signal.data.plans[0]=new object();
        Check(signal.LightState(1,1)==2, "valid head retains actual light state");
        Console.WriteLine("PASS: " + checks + " C# regression checks; no Unity process started");
    }
    /*IDM*/
}
'''.replace('/*IDM*/', idm)
signal_harness = r"""
class Debug { public static int warnings; public static void LogWarning(string s) { warnings++; } }
class FakeStop { public int plan; public string group; }
class FakeSideRoute { public FakeStop signal; }
class FakeCorridor { public FakeStop[] stops; }
class FakeData {
    public FakeCorridor[] corridors; public object[] plans;
    public static implicit operator bool(FakeData d) => d != null;
}
class SignalHarness {
    public FakeData data;
    bool warnedStaleSignal;
    public bool signals=true; public int sideLight=2;
    int LightFor(int plan, string group) => sideLight;
    public bool SideAllows(FakeSideRoute r) => SideGreen(r);
    /*HAS_SIDE_SIGNAL*/
    /*SIDE_GREEN*/
    float LightAt(FakeStop stop) => 2;
    /*LIGHTSTATE*/
}
""".replace('/*LIGHTSTATE*/', light_state).replace('/*HAS_SIDE_SIGNAL*/', has_side_signal).replace('/*SIDE_GREEN*/', side_green)
source = harness + signal_harness + '\nnamespace Magistrala.Traffic {\n' + rules + '\n}\n'
with tempfile.TemporaryDirectory(prefix='sporedne-round2-', dir='/private/tmp') as tmp:
    cs, exe = Path(tmp) / 'Regression.cs', Path(tmp) / 'Regression.exe'
    cs.write_text(source)
    subprocess.run([str(MONO / 'bin/mono'), str(MONO / 'lib/mono/4.5/csc.exe'),
                    '-nologo', '-out:' + str(exe), str(cs)], check=True)
    subprocess.run([str(MONO / 'bin/mono'), str(exe)], check=True)
