"""audit.json + missing.json -> the Unity table (Assets/Editor/zgrade-ortofoto.json): per tile building its roof class,
roof / flat colour (sRGB, already mapped to the game palette), solar panels, an OSM shift; plus the missing buildings."""
import sys, json, math; sys.path.insert(0,'.'); import env
import numpy as np
from collections import defaultdict
from misssel import obvious
OUT=sys.argv[1] if len(sys.argv)>1 else 'zgrade-ortofoto.json'
R=[r for r in json.load(open('audit.json')) if 'dx' in r]

def pick(r):
    """fractions / class from the fitted shift when the fit is clear, else from the footprint as it is"""
    good=r['prom']>=0.1 and math.hypot(r['dx'],r['dz'])<=4
    return (r['fr'],r['cls']) if good else (r['fr0'],r['cls0'])

# --- terracotta: lightness, chroma, hue relative to the neighbourhood (250 m cells, 3x3) -> percentiles
def lch(c):
    r,g,b=[v/255 for v in c]; L=.3*r+.59*g+.11*b
    return L, (r-b)/max(r,1e-3), (g-b)/max(r-b,1e-3)
cell=lambda x,z:(int(math.floor(x/250)),int(math.floor(z/250)))
T=[r for r in R if r.get('terra_rgb')]
grid=defaultdict(list)
for r in T: grid[cell(r['cx'],r['cz'])].append(lch(r['terra_rgb']))
def local(x,z):
    i,j=cell(x,z); L=[]
    for di in (-1,0,1):
        for dj in (-1,0,1): L+=grid.get((i+di,j+dj),[])
    return np.median(np.array(L),0)
dev=[]
for r in T:
    v=np.array(lch(r['terra_rgb'])); r['_d']=v-local(r['cx'],r['cz']); dev.append(r['_d'])
dev=np.array(dev)
def pct(col,v): return float(np.searchsorted(np.sort(dev[:,col]),v)/len(dev))

# the game palette (sRGB albedo of the roof, before the sun): faded salmon .. vivid new tiles; brightness and hue drift
F=np.array([150,108,92.]); V=np.array([152,78,52.])
def roof_colour(tl,tc,th):
    c=F+(V-F)*tc
    c=c*np.array([1,0.94+0.12*th,0.97+0.06*(1-th)])   # th low: redder / pinker, high: more orange-brown
    c=c*(0.8+0.36*tl)
    return np.clip(c,[70,40,28],[172,128,110])

# --- flat roofs: lightness relative to the neighbourhood -> membrane / concrete greys
FL=[r for r in R if r.get('flat_rgb')]
fgrid=defaultdict(list)
for r in FL: fgrid[cell(r['cx'],r['cz'])].append(sum(r['flat_rgb'])/3)
fdev=[]
for r in FL:
    i,j=cell(r['cx'],r['cz']); L=[]
    for di in (-1,0,1):
        for dj in (-1,0,1): L+=fgrid.get((i+di,j+dj),[])
    r['_fd']=sum(r['flat_rgb'])/3-float(np.median(L)); r['_warm']=r['flat_rgb'][0]-r['flat_rgb'][2]; fdev.append(r['_fd'])
fdev=np.sort(np.array(fdev))
def flat_colour(r,dark):
    t=float(np.searchsorted(fdev,r['_fd'])/len(fdev))
    g=78+14*t if dark else 105+65*t
    w=max(-4,min(8,(r['_warm']-6)*0.6))
    return np.array([g+w,g+w*0.3,g-w])

# --- OSM shift per zone: the OSM-minus-cadastre median of the confident pitched fits, where clear
conf=[r for r in R if r['cls']=='pitched' and r['prom']>=0.12 and r['score']>=0.6 and r['fr']['terra']>=0.6]
byz=defaultdict(lambda:{'dgu':[],'osm':[]})
for r in conf:
    if r['src'] in ('dgu','osm'): byz[r['zone']][r['src']].append((r['dx'],r['dz']))
shift={}; rng=np.random.default_rng(1)
for z,d in byz.items():
    o=np.array(d['osm']); g=np.array(d['dgu'])
    if len(o)<40 or len(g)<20: continue
    s=np.median(o,0)-np.median(g,0)
    bs=np.array([np.median(rng.choice(o[:,0],len(o)))-np.median(rng.choice(g[:,0],len(g))) for _ in range(400)])
    frac=np.mean(o[:,0]-np.median(g[:,0])<-0.5) if s[0]<0 else np.mean(o[:,0]-np.median(g[:,0])>0.5)
    lo,hi=np.percentile(bs,[5,95])
    ok=abs(s[0])>=0.75 and (hi<0 or lo>0) and frac>=0.55
    print(f'zone {z}: osm-dgu {s[0]:+.2f},{s[1]:+.2f} ci[{lo:+.2f},{hi:+.2f}] frac {frac:.2f} n {len(o)}/{len(g)} -> {"APPLY" if ok else "-"}',file=sys.stderr)
    if ok: shift[z]=[round(float(s[0]),2),0.0]

CLS={'pitched':1,'flat_light':2,'flat_dark':3,'metal':4,'mixed':5}
hexc=lambda c:'%02x%02x%02x'%tuple(int(round(v)) for v in c)
out=defaultdict(list); stats=defaultdict(int); rng2=np.random.default_rng(7)
for r in R:
    fr,cls=pick(r)
    flat=fr['light']+fr['grey']+fr['dark']
    k=CLS.get(cls,0)
    # confident decisions override the procedural roof rule
    conf_pitch=fr['terra']>=0.55; conf_flat=flat>=0.65 and fr['terra']<=0.12
    decide=1 if conf_pitch else 2 if conf_flat else 0
    stats[('decide',decide)]+=1
    rc=''; fc=''
    if r.get('terra_rgb'):
        d=r['_d']; rc=hexc(roof_colour(pct(0,d[0]),pct(1,d[1]),pct(2,d[2])))
    if r.get('flat_rgb'):
        fc=hexc(flat_colour(r,cls=='flat_dark'))
    if cls=='metal': fc='b4b8bb'
    sol=r.get('solar_m2',0)
    sa=r.get('solar_at',[0,0])
    sh=shift.get(r['zone'],[0,0]) if r['src']=='osm' else [0,0]
    if sh[0]: stats['shifted']+=1
    tx,tz,i=r['id']
    out[f'{tx}_{tz}'].append([i,k,decide,rc,fc,sol,sa[0],sa[1],sh[0],sh[1],round(r['cx'],1),round(r['cz'],1)])
M=json.load(open('missing.json'))
add=[]
for m in M:
    if obvious(m) and m['dgu_cover']:
        # the roof outline less ~0.3 m of eaves
        P=np.array(m['rect']); c=P.mean(0); P=c+(P-c)*np.maximum(0,1-0.3/np.maximum(1e-3,np.linalg.norm(P-c,axis=1)))[:,None]
        add.append({'pts':[[round(float(x),2),round(float(z),2)] for x,z in P],'h':7.0,'roof':hexc(roof_colour(rng2.random(),rng2.random(),rng2.random()))})
doc={'v':1,'note':'Tools/zgrade/export.py from the DGU DOF LiDAR 2022/23 orthophoto (docs/audit-zgrade.md). b: tile -> [index, class 0 ? 1 pitched 2 flat light 3 flat dark 4 metal 5 mixed, decide 0 procedural 1 pitched 2 flat, roof sRGB, flat sRGB, solar m2, solar dx, dz from the centroid, shift dx, dz, cx, cz]',
     'shift':shift,'b':out,'add':add}
s=json.dumps(doc,separators=(',',':'),ensure_ascii=False)
open(OUT,'w').write(s)
print('bytes',len(s),'buildings',sum(len(v) for v in out.values()),'add',len(add),dict(stats),file=sys.stderr)
