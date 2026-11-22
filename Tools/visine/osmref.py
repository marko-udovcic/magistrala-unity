import os
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'env0.py')).read())
import json, glob, collections, re
from pyproj import Transformer
from shapely.geometry import Polygon
from shapely import STRtree, make_valid
from tiles import buildings
LL=Transformer.from_crs(4326,'+proj=aeqd +lat_0=43.5083 +lon_0=16.4400 +datum=WGS84 +units=m',always_xy=True)
C='/Users/macair/Documents/BalkanDriver/tools/world/dgu/cache/'
els={}
for f in ['2157f49145b9fb949d03eb1f061fed6a0670b08378bd5594b8da3cd353b61104','385a84c04f794f3c3285d69ec731ebcff18277cdb5b1174dc97218d929f53769','3f77dce5dafc79bc17a6a6ead60bcf6c32d7a7a260360aa6b0c5e0440519445f','d1c2597275b0f8c091277cd65470b4cb8b231e3c740f65859c5508524bf68885']:
    for e in json.load(open(C+f+'.response'))['elements']: els[(e['type'],e['id'])]=e
def num(s):
    m=re.match(r'^\s*([0-9]+(?:[.,][0-9]+)?)',s or '');
    return float(m.group(1).replace(',','.')) if m else None
osm=[]
for e in els.values():
    t=e.get('tags',{})
    if e['type']!='way' or 'geometry' not in e: continue
    lv=num(t.get('building:levels')); h=num(t.get('height'))
    if lv is None and h is None: continue
    pts=[LL.transform(p['lon'],p['lat']) for p in e['geometry']]
    pts=[(x,-y) for x,y in pts]
    if len(pts)<4: continue
    g=make_valid(Polygon(pts))
    if g.area<10: continue
    c=g.centroid
    if not(-10240<=c.x<0 and -7168<=c.y<-4096): continue
    osm.append(dict(g=g,lv=lv,h=h,rl=num(t.get('roof:levels')),part='building:part' in t,b=t.get('building'),id=e['id']))
print('osm with levels/height in Kaštela tiles:',len(osm),file=sys.stderr)
tree=STRtree([o['g'] for o in osm])
out={}
for tz in range(-7,-4):
    for tx in range(-10,0):
        for i,b in enumerate(buildings(tx,tz)):
            P=b['pts']; g=make_valid(Polygon(P))
            if g.area<5: continue
            best=None
            for j in tree.query(g):
                o=osm[j]; inter=g.intersection(o['g']).area; iou=inter/(g.area+o['g'].area-inter)
                if best is None or iou>best[0]: best=(iou,o)
            if best and best[0]>=0.5:
                o=best[1]; out[f'{tx}_{tz}:{i}']=dict(iou=round(best[0],2),lv=o['lv'],h=o['h'],rl=o['rl'],b=o['b'],osm=o['id'])
print('matched',len(out),collections.Counter(v['lv'] for v in out.values()),file=sys.stderr)
json.dump(out,open('osmref.json','w'))
