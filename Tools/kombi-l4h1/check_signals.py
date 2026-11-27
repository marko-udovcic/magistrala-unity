#!/usr/bin/env python3
"""Exercise the actual signal-state methods, with muted output and no Unity process (bundled Mono only)."""
from pathlib import Path
import re
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'Assets/Scripts/Truck/TurnSignals.cs'


def method(source, name):
    start = re.search(r'^    (?:public )?void ' + name + r'\(', source, re.M).start()
    brace = source.index('{', start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


source = SOURCE.read_text()
methods = '\n'.join(method(source, n) for n in ('SetDirection', 'SetHazards', 'Restart', 'Advance', 'OnDisable'))
harness = '''using System;
class Signals {
 public int Direction { get; private set; }
 public bool Hazards { get; private set; }
 public bool Lit { get; private set; }
 float phase, turnYaw, previousForward; bool armed;
 class Pose { public float forward = 0; }
 Pose transform = new Pose();
 void SetLit(bool on) { Lit = on; }
 void WriteLamps() {} // muted: no Unity graphics/audio
 METHODS
 static void Check(bool ok, string label) { if (!ok) throw new Exception(label); }
 static void Main() {
  var s = new Signals();
  s.SetDirection(-1); Check(s.Lit && s.Direction == -1, "left starts immediately");
  s.Advance(.334f, 0, 1, 0, 0); Check(!s.Lit, "half-cycle off");
  s.Advance(.334f, 0, 1, 0, 0); Check(s.Lit, "1.5 Hz period");
  s.SetDirection(1); Check(s.Direction == 1 && s.Lit, "switch side while lit");
  s.Advance(.02f, 20, 1, -.2f, -30); s.Advance(.02f, 20, 1, 0, 0);
  Check(s.Direction == 1, "opposite turn cannot cancel");
  s.SetDirection(-1); s.Advance(.02f, 20, 1, -.2f, -25);
  Check(s.Direction == -1, "stays on while turning");
  s.Advance(.02f, 20, 1, 0, -1); Check(s.Direction == 0 && !s.Lit, "left auto-cancel");
  s.SetDirection(1); s.Advance(.02f, 20, 1, .2f, 25); s.Advance(.02f, 20, 1, 0, 0);
  Check(s.Direction == 0, "right auto-cancel");
  s.SetDirection(-1); s.Advance(.02f, 1, 1, -.3f, -45); s.Advance(.02f, 1, 1, 0, 0);
  Check(s.Direction == -1, "parking manoeuvre does not cancel");
  s.SetDirection(1); s.Advance(.02f, 10, -1, .3f, 45); s.Advance(.02f, 10, -1, 0, 0);
  Check(s.Direction == 1, "reverse does not cancel");
  s.SetDirection(1); s.Advance(.02f, 20, 1, .2f, 5); s.Advance(.02f, 20, 1, 0, 0);
  Check(s.Direction == 1, "steering twitch does not cancel");
  s.SetHazards(true); s.Advance(.02f, 30, 1, .2f, 90); s.Advance(.02f, 30, 1, 0, 0);
  Check(s.Hazards && s.Direction == 0, "hazards survive a turn");
  bool lit = s.Lit; s.Advance(0, 30, 1, 0, 0); Check(s.Lit == lit, "pause freezes phase");
  s.SetHazards(false); Check(!s.Hazards && !s.Lit, "hazards off");
  s.SetDirection(1); s.SetDirection(0); Check(!s.Lit, "manual cancel");
  s.SetHazards(true); s.OnDisable(); Check(!s.Hazards && s.Direction == 0 && !s.Lit, "switch/disable clears lamps");
  Console.WriteLine("Signals: 16 muted state checks passed (actual C# methods).");
 }
}'''.replace('METHODS', methods)
# Use the installed Editor's compiler/runtime as ordinary executables; never invoke Unity.app.
versions = sorted(Path('/Applications/Unity/Hub/Editor').glob('*/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge'))
if not versions:
    raise SystemExit('No bundled Mono found; run these checks with a C# compiler on this machine.')
mono = versions[-1] / 'bin/mono'
csc = versions[-1] / 'lib/mono/4.5/csc.exe'
with tempfile.TemporaryDirectory(prefix='magistrala-signals-') as folder:
    cs = Path(folder) / 'Signals.cs'
    exe = Path(folder) / 'Signals.exe'
    cs.write_text(harness)
    subprocess.run([str(mono), str(csc), '-nologo', '-out:' + str(exe), str(cs)], check=True)
    subprocess.run([str(mono), str(exe)], check=True)
