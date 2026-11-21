import sys, json, glob, math; sys.path.insert(0,'.'); import env
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from ortho import tile
from tiles import buildings
from classify import classes, TERRA, LIGHT
from pyproj import Transformer
BD='/Users/macair/Documents/BalkanDriver/'
AEQD='+proj=aeqd +lat_0=43.5083 +lon_0=16.4400 +datum=WGS84 +units=m'
T=Transformer.from_crs(3765,AEQD,always_xy=True)
COVER=[]
def dgu_polys():
    seen=set(); out=[]
    for f in glob.glob(BD+'tools/world/dgu/cache/*.meta.json'):
        m=json.load(open(f))
        if 'inspire/bu/wfs' not in m.get('url','') or 'bbox=' not in m['url']: continue
        import urllib.parse as up
        q=up.parse_qs(up.urlparse(m['url']).query); bb=[float(v) for v in q['bbox'][0].split(',')[:4]]
        xs,ys=T.transform([bb[0],bb[2],bb[0],bb[2]],[bb[1],bb[1],bb[3],bb[3]]); COVER.append((min(xs),-max(ys),max(xs),-min(ys)))
        try: d=json.load(open(f.replace('.meta.json','.response')))
        except Exception: continue
        for ft in d.get('features',[]):
            fid=ft.get('id');
            if fid in seen: continue
            seen.add(fid); g=ft.get('geometry')
            if not g: continue
            polys=g['coordinates'] if g['type']=='MultiPolygon' else [g['coordinates']] if g['type']=='Polygon' else []
            for p in polys:
                ring=np.array(p[0])[:,:2]; x,y=T.transform(ring[:,0],ring[:,1])
                out.append((fid,list(zip(x,-np.asarray(y)))))
    return out
def osm_orig():
    d=json.load(open(BD+'tools/world/dgu/osm-corridor-original.json'))['buildings']
    return [b['pts'] for k,L in d.items() for b in L]
def model_rects():
    d=json.load(open(BD+'client/public/assets/world/split-kastela/model-buildings.json'))
    out=[]
    for o in d['objects']:
        if 'w' not in o or 'l' not in o: continue
        a=o.get('a',0); ca,sa=math.cos(a),math.sin(a); w,l=o['w']/2,o['l']/2
        out.append([(o['x']+ca*u-sa*v, o['z']+sa*u+ca*v) for u,v in ((-w,-l),(w,-l),(w,l),(-w,l))])
    return out
def label(mask):
    H,W=mask.shape; lab=np.zeros((H,W),np.int32); comps=[]; n=0
    ys,xs=np.nonzero(mask)
    for y0,x0 in zip(ys,xs):
        if lab[y0,x0]: continue
        n+=1; st=[(y0,x0)]; lab[y0,x0]=n; P=[]
        while st:
            y,x=st.pop(); P.append((y,x))
            for yy,xx in ((y+1,x),(y-1,x),(y,x+1),(y,x-1)):
                if 0<=yy<H and 0<=xx<W and mask[yy,xx] and not lab[yy,xx]: lab[yy,xx]=n; st.append((yy,xx))
        comps.append(np.array(P))
    return comps
if __name__=='__main__':
    game=[(b['pts'],'game') for tz in range(-7,-4) for tx in range(-10,0) for b in buildings(tx,tz)]
    dg=dgu_polys(); print('dgu polys',len(dg),file=sys.stderr)
    others=[(p,'dgu') for _,p in dg]+[(p,'osm') for p in osm_orig()]+[(p,'model') for p in model_rects()]
    res=[]
    for tz in range(-7,-4):
        for tx in range(-10,0):
            t=tile(tx,tz)
            if t is None: continue
            c,_=classes(t); valid=t[...,3]>200
            terra=(c==TERRA)&valid
            # opening: remove thin noise
            im=Image.fromarray((terra*255).astype(np.uint8)).filter(ImageFilter.MinFilter(3)).filter(ImageFilter.MaxFilter(3))
            terra=np.array(im)>0
            def raster(polys,grow):
                m=Image.new('L',(2048,2048),0); d=ImageDraw.Draw(m)
                for P in polys:
                    xs=[p[0] for p in P]
                    if max(xs)<tx*1024-20 or min(xs)>tx*1024+1044: continue
                    zs=[p[1] for p in P]
                    if max(zs)<tz*1024-20 or min(zs)>tz*1024+1044: continue
                    d.polygon([((x-tx*1024)*2,(z-tz*1024)*2) for x,z in P],fill=255)
                if grow: m=m.filter(ImageFilter.MaxFilter(2*grow+1))
                return np.array(m)>0
            mg=raster([p for p,_ in game],6)
            res_mask=terra&~mg
            mo=raster([p for p,_ in others],2)
            for P in label(res_mask):
                n=len(P)
                if n<160: continue
                hh=np.ptp(P[:,0])+1; ww=np.ptp(P[:,1])+1
                fill=n/(hh*ww)
                y,x=P[:,0].mean(),P[:,1].mean()
                gx=tx*1024+x/2; gz=tz*1024+y/2
                inother=mo[P[:,0],P[:,1]].mean()
                from shapely.geometry import MultiPoint
                hull=MultiPoint([(tx*1024+(xx+.5)/2, tz*1024+(yy+.5)/2) for yy,xx in P]).convex_hull
                rr=hull.minimum_rotated_rectangle
                res.append(dict(hull=[list(map(lambda v:round(v,2),q)) for q in hull.exterior.coords[:-1]], rect=[list(map(lambda v:round(v,2),q)) for q in rr.exterior.coords[:-1]], rect_area=round(rr.area,1),gx=round(gx,1),gz=round(gz,1),area=n/4,fill=round(float(fill),2),w=ww/2,h=hh/2,other=round(float(inother),2),
                                pix=P[::max(1,n//400)].tolist(),tile=[tx,tz],dgu_cover=any(c[0]<=gx<=c[2] and c[1]<=gz<=c[3] for c in COVER)))
            print(tx,tz,len(res),file=sys.stderr)
    json.dump(res,open('missing.json','w'))
