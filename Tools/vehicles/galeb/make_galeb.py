# Galeb: brand-free copy of "Mercedes Benz Sprinter 2006" by Max (CC BY 4.0) -> galeb.glb
#  - textures: grille / rear emblem disc painted plain, licence plate fictional, tyre sidewall lettering blurred, body near white
#  - wheels: the two axle meshes (both wheels of an axle in one mesh) split into four nodes wheel_0..3 with the pivot at the hub
#  - names / metadata: no brand words (author + licence kept; full attribution in CREDITS.md)
import json,struct,sys,io
sys.path.insert(0,'/private/tmp/claude-501/-Users-macair-Documents-BalkanDriver/eff135b3-602f-4849-9129-cf1d21309610/scratchpad/pylib')
import numpy as np
from PIL import Image, ImageDraw, ImageFont, ImageFilter
SRC,DST,FONT=sys.argv[1],sys.argv[2],sys.argv[3]
f=open(SRC,'rb').read()
cl,_=struct.unpack('<II',f[12:20]); j=json.loads(f[20:20+cl]); off=20+cl; bl,_=struct.unpack('<II',f[off:off+8]); B=f[off+8:off+8+bl]
views=[B[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']] for v in j['bufferViews']]
def img(i): return Image.open(io.BytesIO(views[j['images'][i]['bufferView']]))
def setimg(i,im):
    o=io.BytesIO(); im.save(o,'PNG',optimize=True); views[j['images'][i]['bufferView']]=o.getvalue()

# --- textures
a=img(12).convert('RGB'); d=ImageDraw.Draw(a)
d.ellipse((948-19,203-19,948+19,203+19),fill=(104,104,104))           # emblem disc: plain, no badge
# licence plate (the UV is mirrored): white plate, blue strip, fictional number
plate=Image.new('RGB',(114,24),(242,242,238)); pd=ImageDraw.Draw(plate)
pd.rectangle((0,0,10,23),fill=(30,70,170)); pd.text((1,7),'HR',fill=(255,255,255),font=ImageFont.truetype(FONT,7))
pd.text((16,1),'ST 418-GB',fill=(20,20,20),font=ImageFont.truetype(FONT,18))
pd.rectangle((0,0,113,23),outline=(30,30,30))
a.paste(plate.transpose(Image.FLIP_TOP_BOTTOM),(83,85))                  # (this UV island is upside down, like the original plate)
clu=a.crop((832,636,868,668)).filter(ImageFilter.GaussianBlur(4)); a.paste(clu,(832,636))  # instrument cluster centre: no maker mark
setimg(12,a)
t=img(18).convert('RGB'); arr=np.array(t); bl_=np.array(t.filter(ImageFilter.GaussianBlur(9)))
yy,xx=np.mgrid[0:1024,0:1024]
for cx,cy in ((240,272),(240,752)):
    r=np.hypot(xx-cx,yy-cy); m=(r>168)&(r<262)&(xx<445)
    arr[m]=bl_[m]
setimg(18,Image.fromarray(arr))
for i in (6,20):                                                        # body paint: near white
    b=np.array(img(i)).astype(np.float32); setimg(i,Image.fromarray(np.clip(b*1.12,0,255).astype(np.uint8)))

# --- wheels
def accarr(ai):
    a=j['accessors'][ai]; v=j['bufferViews'][a['bufferView']]; n={'VEC3':3,'VEC2':2,'SCALAR':1,'VEC4':4}[a['type']]
    dt={5126:np.float32,5123:np.uint16,5125:np.uint32}[a['componentType']]
    raw=views[a['bufferView']]; s=a.get('byteOffset',0); st=v.get('byteStride')
    if st and st!=n*np.dtype(dt).itemsize:
        r=np.frombuffer(raw,np.uint8,count=st*a['count'],offset=s).reshape(a['count'],st); return r[:,:n*np.dtype(dt).itemsize].copy().view(dt).reshape(a['count'],n)
    return np.frombuffer(raw,dt,count=a['count']*n,offset=s).reshape(a['count'],n).copy()
def addacc(arr,typ,target=None,minmax=False):
    arr=np.ascontiguousarray(arr); comp={np.dtype(np.float32):5126,np.dtype(np.uint32):5125,np.dtype(np.uint16):5123}[arr.dtype]
    views.append(arr.tobytes()); bv={'buffer':0,'byteLength':len(views[-1])}
    if target: bv['target']=target
    j['bufferViews'].append(bv)
    acc={'bufferView':len(j['bufferViews'])-1,'componentType':comp,'count':int(arr.shape[0]),'type':typ}
    if minmax: acc['min']=arr.min(0).tolist(); acc['max']=arr.max(0).tolist()
    j['accessors'].append(acc); return len(j['accessors'])-1
k=0
for ni in (31,33):                      # Cylinder_1_Mat_0 (rear axle), Cylinder_Mat_0 (front axle)
    node=j['nodes'][ni]; mesh=j['meshes'][node.pop('mesh')]; pr=mesh['primitives'][0]
    at={kk:accarr(v) for kk,v in pr['attributes'].items()}; idx=accarr(pr['indices']).reshape(-1,3).astype(np.int64)
    P=at['POSITION']; side=P[:,1]>-112
    node['children']=[]
    for s in (False,True):
        tri=idx[side[idx[:,0]]==s]; used=np.unique(tri); remap=-np.ones(len(P),np.int64); remap[used]=np.arange(len(used))
        c=(P[used].min(0)+P[used].max(0))/2
        attrs={}
        for kk,v in at.items():
            vv=v[used]
            if kk=='POSITION': vv=vv-c
            attrs[kk]=addacc(vv.astype(np.float32),{2:'VEC2',3:'VEC3',4:'VEC4'}[vv.shape[1]],34962,kk=='POSITION')
        ia=addacc(remap[tri].reshape(-1).astype(np.uint32),'SCALAR',34963)
        j['meshes'].append({'name':f'wheel_{k}','primitives':[{'attributes':attrs,'indices':ia,'material':pr['material']}]})
        j['nodes'].append({'name':f'wheel_{k}','mesh':len(j['meshes'])-1,'translation':c.astype(float).tolist()})
        node['children'].append(len(j['nodes'])-1); k+=1
    j['nodes'][ni]['name']=f'axle_{ni}'
# --- 3 stray vertices of 'Autre' 1 m behind the van (degenerate triangles): out of the bounds
pr=j['meshes'][25]['primitives'][0]; P=accarr(pr['attributes']['POSITION']); idx=accarr(pr['indices']).reshape(-1,3)
keep=~(P[idx][:,:,2]<-590).any(1); print('Autre: dropped',(~keep).sum(),'triangles')
pr['indices']=addacc(idx[keep].reshape(-1).astype(np.uint32),'SCALAR',34963)
far=P[:,2]<-590; P[far]=P[~far][0]; pr['attributes']['POSITION']=addacc(P.astype(np.float32),'VEC3',34962,True)
# --- emblems: the grille star with its ring, the bonnet badge, the rear-door star (both door halves) are separate small
#     components of the 'Autre' meshes: their triangles are dropped (what is behind them is grille / door skin)
def q2m(q):
    x,y,z,w=q
    return np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
def local(n):
    if 'matrix' in n: return np.array(n['matrix']).reshape(4,4).T
    M=np.eye(4)
    if 'scale' in n: M=np.diag(list(n['scale'])+[1])@M
    if 'rotation' in n: R=np.eye(4); R[:3,:3]=q2m(n['rotation']); M=R@M
    if 'translation' in n: T=np.eye(4); T[:3,3]=n['translation']; M=T@M
    return M
W={}
def walk(i,P):
    M=P@local(j['nodes'][i]); W[i]=M
    for c in j['nodes'][i].get('children',[]): walk(c,M)
walk(0,np.eye(4))
def comps(idx,P):
    par=list(range(len(P)))
    def f(a):
        while par[a]!=a: par[a]=par[par[a]]; a=par[a]
        return a
    for t in idx:
        a=f(t[0])
        for b in t[1:]:
            b=f(b)
            if a!=b: par[b]=a
    key={}
    for v in range(len(P)):
        k=tuple(np.round(P[v],4))
        if k in key:
            a,b=f(key[k]),f(v)
            if a!=b: par[b]=a
        else: key[k]=v
    return np.array([f(v) for v in range(len(P))])
dropped=0
for ni,n in enumerate(j['nodes']):
    if n.get('name') not in ('Autre_Autre_0','Porte_1_Autre_0','Porte_2_Autre_0'): continue
    pr=j['meshes'][n['mesh']]['primitives'][0]; P=accarr(pr['attributes']['POSITION']); idx=accarr(pr['indices']).reshape(-1,3).astype(np.int64)
    h=(np.c_[P,np.ones(len(P))]@W[ni].T)[:,:3]; lab=comps(idx,P); tl=lab[idx[:,0]]; keep=np.ones(len(idx),bool)
    for L in np.unique(tl):
        v=np.unique(idx[tl==L]); a=h[v].min(0); b=h[v].max(0); sz=b-a; cx=(a[0]+b[0])/2; cy=(a[1]+b[1])/2
        front=a[2]>2.74 and -0.46<cy<-0.17; rear=b[2]<-2.84 and 0<cy<0.16
        if sz[0]<0.17 and sz[1]<0.17 and abs(cx)<0.09 and (front or rear):
            keep[tl==L]=False; print('emblem part dropped:',n['name'],(tl==L).sum(),'tris',a.round(2),b.round(2))
    if (~keep).any(): pr['indices']=addacc(idx[keep].reshape(-1).astype(np.uint32),'SCALAR',34963); dropped+=(~keep).sum()
print('emblem triangles dropped:',dropped)
# --- names / metadata
j['nodes'][0]['name']='Galeb'; j['nodes'][1]['name']='galeb_model'
j['asset']['extras']={'author':'Max (https://sketchfab.com/Max-5532)','license':'CC-BY-4.0 (http://creativecommons.org/licenses/by/4.0/)','note':'Modified for Magistrala (Galeb): badges removed, new paint. Full attribution in CREDITS.md.'}
j['asset']['generator']='Magistrala make_galeb.py'
j['scenes'][0]['name']='Galeb'
# --- repack
out=bytearray()
for i,v in enumerate(views):
    while len(out)%4: out+=b'\0'
    j['bufferViews'][i]['byteOffset']=len(out); j['bufferViews'][i]['byteLength']=len(v); out+=v
while len(out)%4: out+=b'\0'
j['buffers']=[{'byteLength':len(out)}]
js=json.dumps(j,separators=(',',':')).encode()
while len(js)%4: js+=b' '
glb=struct.pack('<III',0x46546C67,2,12+8+len(js)+8+len(out))+struct.pack('<II',len(js),0x4E4F534A)+js+struct.pack('<II',len(out),0x004E4942)+bytes(out)
open(DST,'wb').write(glb); print('wrote',DST,len(glb)//1024,'KB')
s=json.dumps(j).lower()
for w in ('mercedes','benz','sprinter'): print(w, w in s)
