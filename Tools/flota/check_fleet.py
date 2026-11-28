#!/usr/bin/env python3
"""Offline fleet checks: all GLBs, source geometry, id migration, actual C# specs and cargo scaling.
Runs bundled Mono as a compiler/runtime only, never the Unity Editor.
"""
import sys
sys.dont_write_bytecode = True

import hashlib
import json
import math
from pathlib import Path
import re
import subprocess
import tempfile
from finish_glb import ROOT, l4, values, dot, sub, cross, REVISION
from import_models import MODELS


def rotate(p, q):
    a = cross(q[:3], p); b = cross(q[:3], a)
    return tuple(p[k] + 2*(q[3]*a[k]+b[k]) for k in range(3))


def triangles(d):
    return sum(d['accessors'][p['indices']]['count']//3 for m in d['meshes'] for p in m['primitives'])


for folder, dest, stem in MODELS:
    for suffix in ('', '_lod1', '_lod2'):
        path = ROOT / 'Assets/Resources/Vehicles' / dest / (stem+suffix+'.glb')
        d,b = l4.read(path); original, ob = l4.read(Path.home()/'Documents/BalkanDriver/assets-src'/folder/path.name)
        assert d['extras'][REVISION]
        names = {n.get('name'): n for n in d['nodes']}
        for name in ('a_eye','a_dash','a_headL','a_headR','mglass_L','mglass_R','steering','door_rear_L','door_rear_R','windscreen_fx','wiper_L','wiper_R'):
            assert name in names, (path, name)
        assert names['a_eye']['translation'] == next(n for n in original['nodes'] if n.get('name')=='a_eye')['translation']
        expected = {'sprinter':4.315, 'mali-kombi':2.755, 'galeb':4.09}[stem]
        assert abs(names['wheel_2']['translation'][2] - names['wheel_0']['translation'][2] - expected) < .002
        for i in range(4):
            wheel = names[f'wheel_{i}']; spin = d['nodes'][wheel['children'][0]]
            assert spin['name'] == f'wheel_{i}_spin'
            assert d['nodes'][spin['children'][0]]['name'] == f'wheel_{i}_mesh'
        fx = d['meshes'][names['windscreen_fx']['mesh']]
        fxcount = sum(d['accessors'][p['indices']]['count']//3 for p in fx['primitives'])
        assert triangles(d) == triangles(original) + fxcount, (path, 'lost original triangles')
        for mesh in d['meshes']:
            for p in mesh['primitives']:
                count = d['accessors'][p['attributes']['POSITION']]['count']
                idx = values(d,b,p['indices'])
                assert idx and all(0 <= i[0] < count for i in idx)
                for attr in p['attributes'].values(): assert d['accessors'][attr]['count'] == count
        source_body = next(n for n in original['nodes'] if n.get('name')=='body')
        prims = {p['material']:p for p in original['meshes'][source_body['mesh']]['primitives']}
        # Verify extracted arms/blades retain their original world geometry after spindle transforms.
        for side in ('L','R'):
            node = names['wiper_'+side]
            for child in node['children']:
                for p in d['meshes'][d['nodes'][child]['mesh']]['primitives']:
                    oldpos = values(original,ob,prims[p['material']]['attributes']['POSITION'])
                    pos = values(d,b,p['attributes']['POSITION'])
                    for i in {i[0] for i in values(d,b,p['indices'])}:
                        rotated = rotate(pos[i],node['rotation']); restored = tuple(rotated[k]+node['translation'][k] for k in range(3))
                        assert math.sqrt(dot(sub(restored,oldpos[i]),sub(restored,oldpos[i]))) < .00001
        if stem=='galeb' and suffix!='_lod2':
            assert all(n in names for n in ('wear_L','wear_R','wear_rear'))
        gauges = next(m for m in d['materials'] if m['name']=='Gauges')
        assert gauges.get('emissiveTexture') and any(gauges['emissiveFactor'])
        digest = hashlib.sha256(path.read_bytes()).hexdigest(); from finish_glb import finish
        finish(path); assert hashlib.sha256(path.read_bytes()).hexdigest() == digest
        print(path.name, 'geometry/rig/emission/idempotence OK')

jobs = json.loads((ROOT/'Assets/Resources/Game/faturete.json').read_text())['jobs']
assert all(0 < j['kg'] <= 1400 for j in jobs)
assert next(j for j in jobs if j['id']=='jure_drva')['kg'] == 1250
assert next(j for j in jobs if j['id']=='ivica_blokovi')['kg'] == 1400

# Extract actual pure C# methods for executable checks without Unity native code.
def block(source, marker):
    start = source.index(marker); brace = source.index('{', start); depth = 1; i = brace+1
    while depth:
        if source[i]=='{': depth+=1
        elif source[i]=='}': depth-=1
        i+=1
    return source[start:i]

player = (ROOT/'Assets/Scripts/Truck/PlayerTruck.cs').read_text()
truck = (ROOT/'Assets/Scripts/Truck/TruckController.cs').read_text()
fleet = (ROOT/'Assets/Scripts/Game/MagistralaGame.Vehicles.cs').read_text()
tours = (ROOT/'Assets/Scripts/Game/MagistralaGame.Tours.cs').read_text()
spec = block(truck, 'public sealed class Spec')
methods = block(player, 'public static TruckController.Spec FleetSpec') + '\n' + block(player, 'static TruckController.BodyBox[] Shell') + ';\n' + block(player, 'public static TruckController.Spec VanSpec') + ';'
idmap = re.search(r'static string VehicleId\(.*?;', fleet).group()
scale = block(tours, 'float FirmKg').replace('var tc = Truck;', 'var tc = Truck;')
fat = block((ROOT/'Assets/Scripts/Game/MagistralaGame.Faturete.cs').read_text(), 'static void AdaptFaturetaLoad')
migration = block((ROOT/'Assets/Scripts/Game/MagistralaGame.Day.cs').read_text(), 'static void AdaptFirmLoad')
harness = '''using System;
using System.Collections.Generic;
struct Vector3 { public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;} }
static class Mathf {
 public static float Min(float a,float b)=>Math.Min(a,b);
 public static float Round(float x)=>(float)Math.Round(x,MidpointRounding.ToEven);
 public static float InverseLerp(float a,float b,float x)=>Math.Max(0,Math.Min(1,(x-a)/(b-a)));
 public static float Lerp(float a,float b,float t)=>a+(b-a)*Math.Max(0,Math.Min(1,t));
}
class TruckController {
SPEC
 public struct BodyBox {public string name;public Vector3 min,max;}
 public Spec spec;public static implicit operator bool(TruckController t)=>t!=null;
}
class Check {
 static void Assert(bool ok){if(!ok)throw new Exception("Fleet check failed");}
 TruckController Truck;
 class Leg {public string kind="d";public int rows;public char K=>kind[0];}
 class Job {public bool fatureta=true;public string fatId,label;public float kg;public int leg,drops,rowsOff;public List<Leg> legs=new List<Leg>();}
METHODS
IDMAP
SCALE
FAT
MIGRATION
 static Job Tour(int[] rows,int leg=0,int off=0){
  var j=new Job{fatureta=false,kg=2400,drops=rows.Length,leg=leg,rowsOff=off};
  foreach(int r in rows)j.legs.Add(new Leg{rows=r});return j;
 }
 static string Rows(Job j)=>string.Join(",",j.legs.ConvertAll(l=>l.rows.ToString()));
 static void MigrationChecks(){
  // Exact saved structure: pickup, six drops, warehouse return; leg 2, rowsOff 0.
  var j=Tour(new[]{0,1,1,0,1,1});
  j.legs.Insert(0,new Leg{kind="u",rows=-1});j.legs.Add(new Leg{kind="r",rows=-1});j.leg=2;
  AdaptFirmLoad(j,3,1100);
  Assert(Rows(j)=="-1,0,0,1,0,1,1,-1" && j.rowsOff==0 && j.leg==2 && j.drops==6);
  Assert(Math.Abs(j.kg-870.8333f)<.01f);
  Console.WriteLine("Marko save: drop rows ["+string.Join(",",j.legs.GetRange(1,6).ConvertAll(l=>l.rows.ToString()))+"], kg="+j.kg);
  var once=Rows(j);float kg=j.kg;AdaptFirmLoad(j,3,1100);Assert(Rows(j)==once && j.kg==kg);
  // Completed rows stay unchanged; delivered physical rows consume the new capacity.
  var partial=Tour(new[]{1,1,2,1},1,1);AdaptFirmLoad(partial,3,1100);
  Assert(Rows(partial)=="1,0,1,1" && partial.rowsOff==1);
  var proportional=Tour(new[]{6,3,1});AdaptFirmLoad(proportional,6,1100);
  Assert(Rows(proportional)=="3,2,1");
  var full=Tour(new[]{1,1},1,4);AdaptFirmLoad(full,3,1100);
  Assert(Rows(full)=="1,0" && full.rowsOff==3);
  var single=Tour(new[]{-1},0,1);AdaptFirmLoad(single,3,1100);Assert(Rows(single)=="2");
  var pending=Tour(new[]{2,2});pending.legs.Insert(0,new Leg{kind="u",rows=-1});
  AdaptFirmLoad(pending,3,1100);Assert(Rows(pending)=="-1,1,2");
  var hand=Tour(new[]{2,2});AdaptFirmLoad(hand,0,700);Assert(Rows(hand)=="2,2" && hand.kg==700);
  var fat=Tour(new[]{2,2});fat.fatureta=true;AdaptFirmLoad(fat,3,1100);Assert(Rows(fat)=="2,2" && fat.kg==2400);
  Console.WriteLine("Firm migration: finished legs, partial delivery, proportional rows, exhausted box, single drop, pending pickup, hand unload, fatureta and repeat load OK");
 }
 static void Main(){
  MigrationChecks();
  var f=VanSpec(); Assert(f.id=="furgon" && f.payloadKg==1100);
  var spr=FleetSpec("sprinter");var small=FleetSpec("mali");var gal=FleetSpec("galebl3h2");
  Assert(spr.mass==2100 && spr.payloadKg==1300 && spr.ratios.Length==6);
  Assert(small.mass==1400 && small.payloadKg==700 && small.ratios.Length==6);
  Assert(gal.mass==2000 && gal.payloadKg==1400 && gal.torqueNm<spr.torqueNm);
  foreach(var s in new[]{f,spr,small,gal}){
   Assert(s.cargoRefKg==s.payloadKg && s.eyeNode=="a_eye" && s.boxes.Length>=3);
   var c=new Check{Truck=new TruckController{spec=s}};
   foreach(float kg in new[]{25f,600f,1400f,2400f,4000f})Assert(c.FirmKg(kg)<=s.payloadKg);
   foreach(var box in s.boxes)Assert(box.min.x<box.max.x && box.min.y<box.max.y && box.min.z<box.max.z);
  }
  Assert(VehicleId("atego")=="furgon" && VehicleId("kombi")=="sprinter" && VehicleId("galeb")=="galeb" && VehicleId("mali")=="mali");
  var j=new Job{fatId="jure_drva",kg=2500,label="Dva metra drva · Jure"};AdaptFaturetaLoad(j);Assert(j.kg==1250 && j.label.StartsWith("Jedan"));
  var i=new Job{fatId="ivica_blokovi",kg=2800,label="Paleta blokova i deset vrića cementa"};AdaptFaturetaLoad(i);Assert(i.kg==1400 && i.label.StartsWith("Pola"));
  Console.WriteLine("Actual C#: four specs, bounds, five tour loads per van, legacy ids and old faturete OK");
 }
}'''.replace('SPEC', spec).replace('METHODS',methods).replace('IDMAP',idmap).replace('SCALE',scale).replace('FAT',fat).replace('MIGRATION',migration)
# The extracted specs use their original TruckController.Spec type names.
versions = sorted(Path('/Applications/Unity/Hub/Editor').glob('*/Unity.app/Contents/Resources/Scripting/MonoBleedingEdge'))
assert versions, 'Bundled Mono required for C# checks'
mono = versions[-1]/'bin/mono'; csc=versions[-1]/'lib/mono/4.5/csc.exe'
with tempfile.TemporaryDirectory(prefix='mag-flota-') as folder:
    cs=Path(folder)/'Check.cs';exe=Path(folder)/'Check.exe';cs.write_text(harness)
    subprocess.run([str(mono),str(csc),'-nologo','-out:'+str(exe),str(cs)],check=True)
    subprocess.run([str(mono),str(exe)],check=True)
print(f'{len(jobs)} fatureta: all within Galeb payload; all offline fleet checks passed')
