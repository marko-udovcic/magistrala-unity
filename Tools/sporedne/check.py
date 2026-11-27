#!/usr/bin/env python3
"""Static data / lane handoff checks, without Unity. Run from any directory in this clone."""
import json,re,math
from pathlib import Path
root=Path(__file__).resolve().parents[2]
payload=json.loads((root/'Assets/Editor/sporedne-routes.json').read_text());T=json.load(open('/Users/macair/Documents/BalkanDriver/client/public/assets/world/split-kastela/paket-approaches.json'))['alignment']['transform']
def U(p):
 x,y,z=p;return (T['a']*x+T['b']*z+T['tx']+4200,y,-(T['c']*x+T['d']*z+T['tz']+5300))
def sub(a,b):return [a[i]-b[i] for i in range(3)]
def norm(a):
 d=math.sqrt(sum(x*x for x in a));return [x/max(d,.00001) for x in a]
def dot(a,b):return sum(x*y for x,y in zip(a,b))
def dist(a,b):return math.hypot(a[0]-b[0],a[2]-b[2])
text=(root/'Assets/Generated/Traffic/TrafficData.asset').read_text().split('  kinds:')[0]
cors=[]
for chunk in text.split('  - name:')[1:5]:
 def vecs(key):return [tuple(map(float,p)) for p in re.findall(r'\{x: ([^,]+), y: ([^,]+), z: ([^}]+)\}',chunk.split('    '+key+':\n')[1].split('\n    '+('right' if key=='axis' else 'lanes')+':')[0])]
 # read until next field without dropping list lines
 def ns(key):
  lines=chunk.split('    '+key+':\n')[1].splitlines();out=[]
  for l in lines:
   if not l.startswith('    - '):break
   out.append(float(l[6:]))
  return out
 cors.append(dict(axis=vecs('axis'),right=vecs('right'),off=ns('laneOff'),y=ns('laneY'),lanes=bytes.fromhex(re.search(r'    lanes: (\w+)',chunk)[1]),maxLanes=int(re.search(r'    maxLanes: (\d+)',chunk)[1])))
# Independent reference for the new eastern model corridor. This uses the
# reviewed main axes, matching TrafficBuilder's 2 m sampling and +/-10 m smoothing.
models=json.load(open('/Users/macair/Documents/BalkanDriver/client/public/assets/world/split-kastela/model-junctions.json'))['junctions']
bykey={m['key']:m for m in models}
axis=[]
for mid,count in ((72,1),(73,1),(74,3),(75,3),(76,4),(77,4),(78,3),(79,3)):
 for r in bykey['krizanje-'+str(mid)]['roads'][:count]:
  axis += [(p[0],p[1],r['w']/max(2,r['n'])/2) for p in r['p'] if p[0]>=-6052.4]
assert axis[-1][0]>-850 and axis[0][0]<-6040
for path in (axis,list(reversed(axis))):
 raw=[path[0]];carry=0
 for a,b in zip(path,path[1:]):
  L=math.hypot(b[0]-a[0],b[1]-a[1])
  if L<.001:continue
  d=2-carry
  while d<=L:
   raw.append((a[0]+(b[0]-a[0])*d/L,a[1]+(b[1]-a[1])*d/L,a[2]));d+=2
  carry=L-(d-2)
 smooth=[tuple(sum(raw[max(0,min(len(raw)-1,i+k))][j] for k in range(-5,6))/11 for j in range(3)) for i in range(len(raw))]
 pts=[(x+4200,0,-(z+5300)) for x,z,o in smooth]
 right=[]
 for i in range(len(pts)):
  t=sub(pts[min(i+1,len(pts)-1)],pts[max(i-1,0)]);t=norm(t)
  right.append((t[2],0,-t[0]))
 cors.append(dict(axis=pts,right=right,off=[p[2] for p in smooth],y=[0]*len(pts),lanes=[1]*len(pts),maxLanes=1))
assert payload['easternMain']==[], 'Models now own x >= 3260'
matched=[];fails=[]
for r in payload['routes']:
 pts=list(map(U,r['points']));e=len(pts)-1 if r['inbound'] else 0;p=pts[e]
 a,b=(pts[max(0,e-2)],p) if r['inbound'] else (p,pts[min(2,len(pts)-1)])
 heading=norm([b[0]-a[0],0,b[2]-a[2]]);best=6;match=None
 for ci,c in enumerate(cors):
  if (ci<2)!=r['d8']:continue
  for i in range(2,len(c['axis'])-2):
   t=sub(c['axis'][i+1],c['axis'][i-1]);t[1]=0
   if dot(norm(t),heading)<.7:continue
   for lane in range(c['lanes'][i]):
    k=i*c['maxLanes']+lane
    q=[c['axis'][i][j]+c['right'][i][j]*c['off'][k] for j in range(3)];q[1]=c['y'][k]
    d=dist(q,p)
    if d<best and (ci>=4 or abs(q[1]-p[1])<4):best=d;match=(ci,i,lane)
 if match:matched.append((r,match,best))
 else:fails.append((r['site'],r['source'],r['name'],tuple(round(x,1) for x in p)))
assert len(set(r['site'] for r,m,d in matched)) == 20, fails
assert not fails, fails
assert len(matched) == len(payload['routes'])

for r in payload['routes']:
    assert all(math.isfinite(v) for p in r['points'] for v in p)
    length = sum(math.dist(a,b) for a,b in zip(r['points'], r['points'][1:]))
    assert 20 <= length <= 165, (r['source'], length)
    assert not (r['d8'] and r['indicator'] < 0), r['source']
    if r['signal']:
        prefix = r['model'].split('-')[0]
        key = ('dionica-' if prefix.startswith(('s','d')) else 'krizanje-') + prefix
        assert bykey[key].get('signals',{}).get('plan'), key
from extract import extract
import hashlib
source=Path.home()/'Documents/mag-27-11/promet/promet_mreza_sve.json'
raw=source.read_bytes()
assert hashlib.sha256(raw).hexdigest() == payload['sha256'], 'Source changed; review and regenerate routes'
assert extract(json.loads(raw)) == payload['routes'], 'Route extraction is not reproducible'
print(f"PASS: {len(matched)} movements, 20 mouths, eastern MODEL axes >5 km, all handoffs <6 m, source hash and deterministic extraction")
print('Main handoff maximum correction: %.2f m' % max(d for r,m,d in matched))
print('Eastern heights require the lead\'s scene surface bake; runtime and raycasts are NOT tested here.')
