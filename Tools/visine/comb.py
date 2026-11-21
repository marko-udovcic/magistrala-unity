import os
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'env0.py')).read())
import json,numpy as np
from tiles import buildings
E=json.load(open('edges.json')); C=json.load(open('shadows4.json'))
def e2(res,mincos=0.7,minc=0.12):
  if not res: return None
  xs=[];ws=[]
  for e in res:
    f=e['gf']
    if e['cos']>=mincos and e['len']>=4 and f and f[1]>=minc and f[2]<0.85: xs.append(f[0]/e['cos']); ws.append(e['len']*f[1])
  return (float(np.average(xs,weights=ws)),float(np.sum(ws))) if xs else None
B={}
for tz in range(-7,-4):
  for tx in range(-10,0):
    for i,b in enumerate(buildings(tx,tz)):
      P=b['pts']; A=abs(sum(P[j][0]*P[(j+1)%len(P)][1]-P[(j+1)%len(P)][0]*P[j][1] for j in range(len(P))))/2
      B[f'{tx}_{tz}:{i}']=dict(kind=b['kind'],area=A,h=b['h'],hSrc=b.get('hSrc'),building=b.get('building'))
def comb(k):
  x=e2(E.get(k)); c=C.get(k,{}); y=c['L'] if c.get('q')=='ok' and c.get('c',0)>=0.15 else None
  if x and y is not None: return (x[0]+y)/2, 'both', abs(x[0]-y)
  if x: return x[0],'edge',None
  if y is not None: return y,'col',None
  return None
