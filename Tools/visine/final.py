import os
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'comb.py')).read())
import collections, math, sys
TANE=float(sys.argv[1]) if len(sys.argv)>1 else 1.6
ORT=json.load(open('/Users/macair/Documents/MagistralaUnity-visine/Assets/Editor/zgrade-ortofoto.json'))
DEC={}
for k,l in ORT['b'].items():
    for e in l: DEC[f'{k}:{e[0]}']=e[2]
osm=json.load(open('osmref.json'))
def clamp_for(b):
    k=b['kind']; A=b['area']
    if k=='church_generic': return None
    if k in ('industrial','warehouse'): return (1,2)
    if k=='apartment': return (2,8)
    if b['building'] in ('greenhouse','shed','garage','garages','roof','carport','hut','kiosk'): return (1,1)
    if A<40: return (1,2)
    if A>350: return (1,6)
    return (1,4)
out={}; stats=collections.Counter(); before=collections.Counter(); after=collections.Counter(); applied=collections.Counter()
err=[]
for k,b in B.items():
    r=comb(k)
    cl=clamp_for(b)
    hb=b['h']; fb=int(hb//3)
    before[min(fb,6)]+=1
    if r is None or cl is None:
        after[min(fb,6)]+=1; stats['none']+=1; continue
    L,src,diff=r
    h=TANE*L
    pitched=DEC.get(k,0)==1
    raw=(h-(1.0 if pitched else 0.7)-0.4)/3.0
    n=int(round(raw)); n0=n
    n=max(cl[0],min(cl[1],n))
    if src=='both': conf=0.85 if diff<=0.6 else 0.65 if diff<=1.2 else 0.35
    else: conf=0.4
    if abs(raw-n)>0.9: conf*=0.5      # far outside the plausible range: likely a wrong shadow
    if b['hSrc'] in ('osm-levels','osm-height'): conf=min(conf,0.5)
    # houses: 1 or 4 floors only on strong evidence, else the nearest typical value (2 / 3)
    if b['kind']=='house' and cl==(1,4) and n in (1,4) and conf<0.8: n=2 if n==1 else 3
    out[k]=[n,round(conf,2),'sjena-'+src,round(h,1)]
    good=conf>=0.6
    stats['conf>=0.6' if good else 'low']+=1
    after[min(n if good else fb,6)]+=1
    if good: applied[(fb,n)]+=1
    if k in osm and osm[k]['lv']: err.append((k,osm[k]['lv'],n,conf,round(h,1)))
print('tanE',TANE,dict(stats))
print('before',sorted(before.items())); print('after ',sorted(after.items()))
print('changes (before->after) among applied',sorted(applied.items()))
for e in err: print(' osm',e)
if err:
    print('MAE all measured vs OSM levels: %.2f (n=%d)'%(np.mean([abs(a-b) for _,a,b,_,_ in err]),len(err)))
    g=[(a,b) for _,a,b,c,_ in err if c>=0.6]
    if g: print('MAE conf>=0.6: %.2f (n=%d)'%(np.mean([abs(a-b) for a,b in g]),len(g)))
H=[v[3] for v in out.values() if v[1]>=0.6 and B[v and k]['kind']=='house'] if False else None
hs=np.array([v[3] for k,v in out.items() if B[k]['kind']=='house'])
print('house measured h median %.1f mean %.1f'%(np.median(hs),hs.mean()))
if '--write' in sys.argv:
    json.dump({'v':1,'note':'Tools/visine (docs/visine-zgrada.md): katovi iz sjena na DGU ortofotu LiDAR 2022./23.; b: id -> [katovi, pouzdanost 0..1, izvor, visina m]','tanE':TANE,'sunAz':236,'b':out},open(sys.argv[-1],'w'),ensure_ascii=False,separators=(',',':'))
