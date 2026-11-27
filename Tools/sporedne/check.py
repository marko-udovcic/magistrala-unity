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
for path in payload['easternMain']:
 raw=list(map(U,path));pts=[raw[0]];carry=0
 for a,b in zip(raw,raw[1:]):
  L=math.dist(a,b)
  if L<.001:continue
  d=2-carry
  while d<=L:
   pts.append([x+(y-x)*d/L for x,y in zip(a,b)]);d+=2
  carry=L-(d-2)
 cors.append(dict(axis=pts,right=[[0,0,0]]*len(pts),off=[0]*len(pts),y=[p[1] for p in pts],lanes=[1]*len(pts),maxLanes=1))
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
    if d<best and abs(q[1]-p[1])<4:best=d;match=(ci,i,lane)
 if match:matched.append((r,match,best))
 else:fails.append((r['site'],r['source'],r['name'],tuple(round(x,1) for x in p)))
assert len(set(r['site'] for r,m,d in matched)) == 20, fails
assert not fails, fails
assert len(matched) == len(payload['routes'])
assert all(sum(math.dist(a,b) for a,b in zip(p,p[1:])) > 5000 for p in payload['easternMain'])
for r in payload['routes']:
    assert all(math.isfinite(v) for p in r['points'] for v in p)
    length = sum(math.dist(a,b) for a,b in zip(r['points'], r['points'][1:]))
    assert 60 <= length <= 155, (r['source'], length)
    assert not (r['d8'] and r['indicator'] < 0), r['source']
    if r['signal']:
        prefix = r['model'].split('-')[0]
        key = ('dionica-' if prefix.startswith(('s','d')) else 'krizanje-') + prefix
        assert ('  - key: ' + key + '\n') in (root/'Assets/Generated/Traffic/TrafficData.asset').read_text(), key
from extract import extract
import hashlib
source=Path.home()/'Documents/mag-25-11/promet/promet_mreza_sve.json'
raw=source.read_bytes()
assert hashlib.sha256(raw).hexdigest() == payload['sha256'], 'Source changed; review and regenerate routes'
assert extract(json.loads(raw)) == payload['routes'], 'Route extraction is not reproducible'
print(f"PASS: {len(matched)} movements, 20 mouths, both eastern lanes >5 km, all handoffs <6 m, source hash and deterministic extraction")
print('Main handoff maximum correction: %.2f m' % max(d for r,m,d in matched))
print('Runtime, surface raycasts and Unity compilation are NOT tested by this script.')
