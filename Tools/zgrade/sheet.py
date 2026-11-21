import sys, json, random; sys.path.insert(0,'.'); import env
from PIL import Image, ImageDraw
from ortho import crop
from tiles import buildings
_bc={}
def pts(id_):
    tx,tz,i=id_
    if (tx,tz) not in _bc: _bc[(tx,tz)]=buildings(tx,tz)
    P=_bc[(tx,tz)][i]['pts']; return P[:-1] if P[0]==P[-1] else P
def tile_img(r, R=22, Z=4, label=None, extra=None):
    cx,cz=r['cx'],r['cz']
    im=Image.fromarray(crop(cx-R,cz-R,cx+R,cz+R)).convert('RGB').resize((int(4*R*Z/2),int(4*R*Z/2)),Image.LANCZOS)
    d=ImageDraw.Draw(im); k=im.width/(2*R)
    P=pts(r['id'])
    d.line([((x-cx+R)*k,(z-cz+R)*k) for x,z in P+[P[0]]],fill=(0,255,255),width=2)
    if 'dx' in r and (r['dx'] or r['dz']):
        d.line([((x+r['dx']-cx+R)*k,(z+r['dz']-cz+R)*k) for x,z in P+[P[0]]],fill=(255,255,0),width=1)
    if r.get('solar_at'):
        sx,sz=r['solar_at']; d.ellipse([(sx+R)*k-6,(sz+R)*k-6,(sx+R)*k+6,(sz+R)*k+6],outline=(0,80,255),width=2)
    t=label or f"{r.get('cls')} {r.get('src')} cov{r.get('cov0')}"
    d.rectangle([0,0,im.width,14],fill=(0,0,0)); d.text((2,1),t,fill=(255,255,255))
    return im
def sheet(rs,out,cols=6,**kw):
    ims=[tile_img(r,**kw) for r in rs]
    if not ims: return
    w,h=ims[0].size; S=Image.new('RGB',(w*cols,h*((len(ims)+cols-1)//cols)),(0,0,0))
    for i,im in enumerate(ims): S.paste(im,((i%cols)*w,(i//cols)*h))
    S.save(out)
if __name__=='__main__':
    R=json.load(open('audit.json')); random.seed(int(sys.argv[3]) if len(sys.argv)>3 else 1)
    sel=eval(sys.argv[1]); L=[r for r in R if 'dx' in r and sel(r)]; print(len(L))
    sheet(random.sample(L,min(24,len(L))),sys.argv[2])
