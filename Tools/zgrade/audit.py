import sys, json, math, time; sys.path.insert(0,'.'); import env
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from ortho import crop
from tiles import buildings
from classify import *

S_M=8.0; S=int(S_M*2); RING=4; MARG=S+RING+3
CORES=[('Štafilić',-8821),('Novi',-8300),('Stari',-7760),('Lukšić',-6145),('Kambelovac',-4356),('Gomilica',-3603),('Sućurac',-1162)]
def zone(cx,cz):
    n=min(CORES,key=lambda c:abs(c[1]-cx))[0]
    return n+(' gornji' if cz<-5000 else '')

def area_of(P):
    a=0
    for i in range(len(P)):
        x0,z0=P[i]; x1,z1=P[(i+1)%len(P)]; a+=x0*z1-x1*z0
    return a/2

def components(mask):
    """4-connected labels of a small bool array -> list of (n, cy, cx)"""
    H,W=mask.shape; seen=np.zeros_like(mask); out=[]
    ys,xs=np.nonzero(mask)
    for y0,x0 in zip(ys,xs):
        if seen[y0,x0]: continue
        st=[(y0,x0)]; seen[y0,x0]=True; pts=[]
        while st:
            y,x=st.pop(); pts.append((y,x))
            for yy,xx in ((y+1,x),(y-1,x),(y,x+1),(y,x-1)):
                if 0<=yy<H and 0<=xx<W and mask[yy,xx] and not seen[yy,xx]: seen[yy,xx]=True; st.append((yy,xx))
        a=np.array(pts); out.append((len(pts), a[:,0].mean(), a[:,1].mean(), np.ptp(a[:,0])+1, np.ptp(a[:,1])+1))
    return out

def analyse(b):
    P=b['pts']
    if P[0]==P[-1]: P=P[:-1]
    A=abs(area_of(P))
    cx=sum(p[0] for p in P)/len(P); cz=sum(p[1] for p in P)/len(P)
    xs=[p[0] for p in P]; zs=[p[1] for p in P]
    gx0=math.floor(min(xs)*2)/2-MARG/2; gz0=math.floor(min(zs)*2)/2-MARG/2
    gx1=gx0+math.ceil((max(xs)-gx0)*2+MARG)/2; gz1=gz0+math.ceil((max(zs)-gz0)*2+MARG)/2
    rgb=crop(gx0,gz0,gx1,gz1)
    H,W=rgb.shape[:2]
    rec=dict(cx=round(cx,2),cz=round(cz,2),area=round(A,1),kind=b.get('kind'),src=b.get('src') or 'osm',h=b.get('h'),zone=zone(cx,cz))
    if rgb[...,3].mean()<200: rec['nodata']=True; return rec
    m=Image.new('L',(W,H),0); ImageDraw.Draw(m).polygon([((x-gx0)*2,(z-gz0)*2) for x,z in P],fill=255)
    M=np.array(m)>0
    if M.sum()<8: rec['tiny']=True; return rec
    dil=np.array(m.filter(ImageFilter.MaxFilter(2*RING+1)))>0
    ero=np.array(m.filter(ImageFilter.MinFilter(3)))>0
    ring=dil&~M; bd=M&~ero
    c,panel=classes(rgb); R=ROOFNESS[c]
    lum=rgb[...,:3].astype(np.float32)@np.array([.3,.59,.11],np.float32)
    gy=np.zeros_like(lum); gxx=np.zeros_like(lum)
    gy[1:-1]=lum[2:]-lum[:-2]; gxx[:,1:-1]=lum[:,2:]-lum[:,:-2]
    G=np.hypot(gy,gxx); G/= (G.mean()+1e-3)
    FR=np.fft.rfft2(R); FG=np.fft.rfft2(G)
    def corr(F,mask): return np.fft.irfft2(F*np.conj(np.fft.rfft2(mask.astype(np.float32))),s=R.shape)
    cin=corr(FR,M)/M.sum(); cring=corr(FR,ring)/max(1,ring.sum()); cedge=corr(FG,bd)/max(1,bd.sum())
    sc=np.full((2*S+1,2*S+1),-9.0); best=(-9,0,0)
    for dy in range(-S,S+1):
        for dx in range(-S,S+1):
            v=cin[dy%H,dx%W]-cring[dy%H,dx%W]+0.12*cedge[dy%H,dx%W]-0.012*math.hypot(dx,dy)/2
            sc[dy+S,dx+S]=v
            if v>best[0]: best=(v,dy,dx)
    v,dy,dx=best
    rec['dx']=dx/2; rec['dz']=dy/2; rec['score']=round(float(v),3); rec['score0']=round(float(sc[S,S]),3)
    # prominence: best vs the best score >= 3 m away
    yy,xx=np.mgrid[-S:S+1,-S:S+1]; far=np.hypot(yy-dy,xx-dx)>=6
    rec['prom']=round(float(v-sc[far].max()),3)
    Ms=np.roll(np.roll(M,dy,0),dx,1); Es=np.roll(np.roll(ero,dy,0),dx,1)
    roofish=np.isin(c,[TERRA,LIGHT,GREY,DARK])
    rec['edge']=round(float(cedge[dy%H,dx%W]),2); rec['edge0']=round(float(cedge[0,0]),2)
    rec['lstd']=round(float(lum[Es if Es.sum()>=6 else Ms].std()),1)
    rec['cov0']=round(float(roofish[M].mean()),3); rec['cov']=round(float(roofish[Ms].mean()),3)
    sel=Es if Es.sum()>=6 else Ms
    cs=c[sel]; n=len(cs)
    fr={NAMES[i]:round(float((cs==i).mean()),3) for i in range(7)}
    rec['fr']=fr
    px=rgb[...,:3][sel].astype(int)
    t=px[cs==TERRA]
    if len(t)>=4: rec['terra_rgb']=[int(v) for v in np.median(t,0)]
    g=px[np.isin(cs,[LIGHT,GREY,DARK])]
    if len(g)>=4: rec['flat_rgb']=[int(v) for v in np.median(g,0)]
    rec['all_rgb']=[int(v) for v in np.median(px,0)]
    # panels: blue-dark clusters inside the roof, not touching the footprint edge, contrast to the roof
    rgbi=rgb[...,:3].astype(np.int16)
    Vv=rgbi.max(-1); medV=float(np.median(Vv[sel]))
    E2=np.array(Image.fromarray((Ms*255).astype(np.uint8)).filter(ImageFilter.MinFilter(5)))>0
    E0=np.array(m.filter(ImageFilter.MinFilter(5)))>0
    pin=E2&E0&(rgbi[...,2]>=rgbi[...,0]+8)&(Vv<=medV-35)&(Vv>=22)&(c!=VEG)
    pan=[]
    if pin.sum()>=6:
        for k,y,x,hh,ww in components(pin):
            if k>=8 and k<=0.5*sel.sum() and k>=0.5*hh*ww: pan.append((k,y,x,hh,ww))
    if pan:
        tot=sum(p[0] for p in pan)
        rec['solar_m2']=tot/4
        k,y,x,hh,ww=max(pan)
        rec['solar_at']=[round(gx0+x/2-cx,1), round(gz0+y/2-cz,1)]
    sel0=ero if ero.sum()>=6 else M
    fr0={NAMES[i]:round(float((c[sel0]==i).mean()),3) for i in range(7)}
    rec['fr0']=fr0
    rec['cls0']=klass(fr0,b,A)
    rec['cls']=klass(fr,b,A)
    return rec

def klass(fr,b,A):
    terra=fr['terra']; flat=fr['light']+fr['grey']+fr['dark']; veg=fr['veg']+fr['other']
    if terra>=0.45: cls='pitched'
    elif terra>=0.2 and flat>=0.3: cls='mixed'
    elif flat>=0.55 and fr['dark']>0.5*flat: cls='flat_dark'
    elif flat>=0.55: cls='flat_light'
    elif veg>=0.6: cls='none'
    else: cls='unknown'
    if cls in('flat_light','flat_dark') and (b.get('kind') in ('industrial','warehouse') or A>700) and fr['light']+fr['grey']>0.5: cls='metal'
    return cls

if __name__=='__main__':
    out=[]; t0=time.time()
    tiles=[(tx,tz) for tz in range(-7,-4) for tx in range(-10,0)]
    for tx,tz in tiles:
        for i,b in enumerate(buildings(tx,tz)):
            try: r=analyse(b)
            except Exception as e: r={'err':str(e)}
            r['id']=[tx,tz,i]; out.append(r)
        print(tx,tz,len(out),round(time.time()-t0),file=sys.stderr)
    json.dump(out,open('audit.json','w'))
