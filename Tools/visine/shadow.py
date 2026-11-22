import os
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'env0.py')).read())
import numpy as np, math, json, sys, time
from PIL import Image, ImageDraw, ImageFilter
from ortho import crop
from tiles import buildings
from classify import classes, VEG
TILES=[(tx,tz) for tz in range(-7,-4) for tx in range(-10,0)]
AUD={tuple(r['id']):r for r in json.load(open(os.environ.get('ZGRADE_AUDIT','../zgrade/audit.json'))) if 'id' in r}
ORT=json.load(open('/Users/macair/Documents/MagistralaUnity-visine/Assets/Editor/zgrade-ortofoto.json'))
SH={}
for k,l in ORT['b'].items():
    tx,tz=map(int,k.split('_'))
    for e in l: SH[(tx,tz,e[0])]=(e[8],e[9])
ALL={}   # id -> P (shifted)
for tx,tz in TILES:
    for i,b in enumerate(buildings(tx,tz)):
        P=b['pts'][:-1] if b['pts'][0]==b['pts'][-1] else b['pts']
        sx,sz=SH.get((tx,tz,i),(0,0))
        ALL[(tx,tz,i)]=dict(P=[(x+sx,z+sz) for x,z in P],b=b)
GRID={}
for k,v in ALL.items():
    xs=[p[0] for p in v['P']]; zs=[p[1] for p in v['P']]
    v['bb']=(min(xs),min(zs),max(xs),max(zs))
    for gx in range(int(min(xs)//32),int(max(xs)//32)+1):
        for gz in range(int(min(zs)//32),int(max(zs)//32)+1): GRID.setdefault((gx,gz),[]).append(k)
def near(x0,z0,x1,z1):
    s=set()
    for gx in range(int(x0//32),int(x1//32)+1):
        for gz in range(int(z0//32),int(z1//32)+1): s.update(GRID.get((gx,gz),[]))
    return s
def shift(M,dy,dx):
    out=np.zeros_like(M); H,W=M.shape
    if abs(dy)>=H or abs(dx)>=W: return out
    out[max(dy,0):H+min(dy,0),max(dx,0):W+min(dx,0)]=M[max(-dy,0):H+min(-dy,0),max(-dx,0):W+min(-dx,0)]
    return out
def poly(P,gx0,gz0,W,H):
    m=Image.new('L',(W,H),0); ImageDraw.Draw(m).polygon([((x-gx0)*2,(z-gz0)*2) for x,z in P],fill=255); return np.array(m)>0
SMAX=48  # px (24 m)
def measure(key,az,dbg=False):
    v=ALL[key]; P=v['P']; x0,z0,x1,z1=v['bb']
    ux=math.sin(math.radians(az)); uz=-math.cos(math.radians(az))
    mg=6; gx0=math.floor(x0)-mg-max(0,-ux)*26; gz0=math.floor(z0)-mg-max(0,-uz)*26
    gx1=math.ceil(x1)+mg+max(0,ux)*26; gz1=math.ceil(z1)+mg+max(0,uz)*26
    gx0=math.floor(gx0); gz0=math.floor(gz0); gx1=math.ceil(gx1); gz1=math.ceil(gz1)
    rgb=crop(gx0,gz0,gx1,gz1); H,W=rgb.shape[:2]
    if rgb[...,3].min()<255: return dict(err='nodata')
    M=poly(P,gx0,gz0,W,H)
    if M.sum()<8: return dict(err='tiny')
    others=np.zeros_like(M)
    for k in near(gx0,gz0,gx1,gz1):
        if k!=key: others|=poly(ALL[k]['P'],gx0,gz0,W,H)
    a=AUD.get(key,{}); dx=dz=0.0
    if a.get('prom',0)>=0.1 and math.hypot(a.get('dx',0),a.get('dz',0))<=4: dx,dz=a['dx'],a['dz']
    # the building as imaged: footprint swept to the displaced roof, 1 px margin
    B=M.copy(); n=int(math.ceil(max(abs(dx),abs(dz))*2))
    for j in range(1,n+1): B|=shift(M,int(round(dz*2*j/n)),int(round(dx*2*j/n)))
    B=np.array(Image.fromarray(B.astype(np.uint8)*255).filter(ImageFilter.MaxFilter(3)))>0
    c,_=classes(rgb); a3=rgb[...,:3].astype(np.int16)
    lum=a3@np.array([30,59,11])/100.
    water=(a3[...,2]>a3[...,0]+12)&(a3[...,1]>a3[...,0]+4)
    bad=(c==VEG)|water|(others&~M)
    # t: steps along u from the footprint
    t=np.full(M.shape,-1.0,np.float32); t[M]=0
    for s2 in range(1,2*SMAX+1):
        s=s2/2
        sm=shift(M,int(round(uz*s)),int(round(ux*s)))
        new=sm&(t<0); t[new]=s
    # columns perpendicular to u
    yy,xx=np.mgrid[0:H,0:W]; wcol=np.round(-uz*xx+ux*yy).astype(int)   # perpendicular coordinate (px)
    # ground reference: open, non-building, non-veg pixels 6..20 m around, not in the shadow swath near the building
    ring=np.array(Image.fromarray(M.astype(np.uint8)*255).filter(ImageFilter.MaxFilter(41)))>0
    gmask=ring&~B&~bad&~((t>0)&(t<=SMAX))
    if gmask.sum()<30: gmask=ring&~B&~bad
    if gmask.sum()<20: return dict(err='noground')
    G=float(np.median(lum[gmask]))
    sel=(t>0)&~B
    cols={}
    for w in np.unique(wcol[sel]):
        cm=(wcol==w)&(t>0)
        # this column's sequence along t (first pixel per step)
        ts=t[cm]; ls=lum[cm]; bs=bad[cm]|B[cm]; inB=B[cm]
        order=np.argsort(ts,kind='stable'); ts=ts[order]; ls=ls[order]; bs=bs[order]; inB=inB[order]
        cols[w]=(ts,ls,bs,inB)
    return dict(G=G,cols=cols,lum=lum,t=t,B=B,bad=bad,M=M,rgb=rgb,d=(dx,dz),gx0=gx0,gz0=gz0) if dbg else dict(G=G,cols=cols,d=(dx,dz))

def analyse(r):
    """per column: the dark run from the building out along u -> length (px); building: robust median"""
    G=r['G']; near=[]
    for w,(ts,ls,bs,inB) in r['cols'].items():
        ok=~bs&(ts<=4)
        near+=list(ls[ok])
    if len(near)<6: return dict(q='nonear')
    S=float(np.percentile(near,25))
    if S>0.86*G: return dict(q='noshadow',S=S/G)
    thr=0.5*(S+G*0.98)
    Ls=[];blocked=0;hidden=0;ncol=0
    for w,(ts,ls,bs,inB) in r['cols'].items():
        if len(ts)==0 or ts[0]>1.5: continue   # column must start at the footprint
        ncol+=1
        started=False; gap=0; last=None; end=None; prevB=True
        for t_,l_,b_,ib in zip(ts,ls,bs,inB):
            if ib and not started: last=t_; continue    # the leaning building itself
            if b_:
                if started or last is None or t_-(last or 0)<1.5: end='blocked'; break
                continue
            if l_<thr:
                started=True; last=t_; gap=0
            else:
                if not started:
                    # the first open pixel is lit: no shadow here (roof displaced over it, or no shadow)
                    end=('lit',t_); break
                gap+=1
                if gap>=2: end=('ok',last); break
        if end=='blocked': blocked+=1; continue
        if end is None: continue
        if end[0]=='lit':
            if last is not None and last>=1: hidden+=1   # shadow under the leaning roof
            Ls.append(0.0); continue
        Ls.append(end[1]+0.5)
    if ncol==0: return dict(q='nocol')
    if len(Ls)<3: return dict(q='blocked',blocked=blocked,ncol=ncol)
    Ls=np.array(Ls)
    return dict(q='ok',L=float(np.median(Ls))/2, L70=float(np.percentile(Ls,70))/2, iqr=float(np.subtract(*np.percentile(Ls,[75,25])))/2,
                n=len(Ls),ncol=ncol,blocked=blocked,hidden=hidden,S=S/G,G=G,zero=float((Ls==0).mean()))

def analyse2(r):
    """median profile along u over the open columns; shadow end = where it climbs back to the ground level"""
    G=r['G']; cols=r['cols']
    NB=2*SMAX+2
    prof=[[] for _ in range(NB)]; ncol=0; nblk=0
    for w,(ts,ls,bs,inB) in cols.items():
        if len(ts)==0 or ts[0]>1.5: continue
        ncol+=1
        # cut the column at the first obstacle (vegetation / other building / water) after the building image
        seen_open=False; blk=False
        for t_,l_,b_,ib in zip(ts,ls,bs,inB):
            if ib and not seen_open: continue
            if b_ and not ib: blk=True; break
            seen_open=True
            prof[int(round(t_*2))].append(l_)
        if blk: nblk+=1
    if ncol==0: return dict(q='nocol')
    tt=np.arange(NB)/2; cnt=np.array([len(p) for p in prof])
    g=np.array([np.median(p) if len(p)>=max(2,0.25*ncol) else np.nan for p in prof])
    # 1 px bins (pairs of half-px bins)
    g1=np.array([np.nanmean(g[2*i:2*i+2]) if not np.all(np.isnan(g[2*i:2*i+2])) else np.nan for i in range(SMAX)])
    ok=~np.isnan(g1)
    if ok.sum()<6: return dict(q='blocked',ncol=ncol,nblk=nblk)
    i0=int(np.argmax(ok))           # first open px
    far=g1[min(SMAX-1,i0+20):]; far=far[~np.isnan(far)]
    Gref=float(np.median(far)) if len(far)>=4 else G
    near=g1[i0:i0+3]; near=near[~np.isnan(near)]
    S=float(np.min(near)) if len(near) else np.nan
    if not(S<0.85*Gref): return dict(q='noshadow',S=S/Gref,i0=i0,ncol=ncol,nblk=nblk)
    thr=0.5*(S+Gref)
    L=None
    for i in range(i0+1,SMAX):
        if np.isnan(g1[i]): 
            continue
        if g1[i]>=thr:
            # interpolate between the previous open bin and this one
            j=i-1
            while j>=i0 and np.isnan(g1[j]): j-=1
            if j<i0: L=i; break
            f=(thr-g1[j])/max(1e-3,g1[i]-g1[j]); L=j+f*(i-j); break
    if L is None: return dict(q='open',ncol=ncol,nblk=nblk)
    # area estimate (blur-robust): sum of the shadow fraction up to the edge + 4 px
    w=np.clip((Gref-g1)/max(1e-3,Gref-S),0,1); w[np.isnan(w)]=0
    La=float(w[i0:int(L)+4].sum()+i0)
    return dict(q='ok',L=(L+0.5)/2,La=La/2,S=S/Gref,Gref=Gref,i0=i0/2,ncol=ncol,nblk=nblk,contrast=(Gref-S)/Gref)

def analyse3(r, tau=0.80, cols=None):
    """cumulative evidence along u: L = argmax_t sum_{t'<=t} (tau*G - lum) over the open pixels of the swath"""
    cols=r['cols'] if cols is None else cols
    NB=2*SMAX+2
    sums=np.zeros(NB); cnt=np.zeros(NB); vals=[[] for _ in range(NB)]
    ncol=0; nblk=0; first=[]
    for w,(ts,ls,bs,inB) in cols.items():
        if len(ts)==0 or ts[0]>1.5: continue
        ncol+=1; seen=False
        for t_,l_,b_,ib in zip(ts,ls,bs,inB):
            if ib and not seen: continue
            if b_ and not ib: nblk+=1; break
            if not seen: first.append(t_)
            seen=True; i=int(round(t_*2)); vals[i].append(l_)
    if ncol<3: return dict(q='nocol')
    cnt=np.array([len(v) for v in vals],float)
    i0=int(np.percentile(first,50)*2) if first else 0
    G=r['G']
    for it in range(2):
        mean=np.array([np.mean(v) if len(v) else np.nan for v in vals])
        good=cnt>=max(2,0.3*ncol)
        ev=np.where(good,(tau*G-mean)/G,0.0)
        cs=np.cumsum(ev); iL=int(np.argmax(cs[i0:]))+i0
        far=good&(np.arange(NB)>iL+3)&(np.arange(NB)<=iL+14)
        if far.sum()>=4: G=float(np.median(mean[far]))
    if good[i0:iL+1].sum()<2: return dict(q='noshadow',ncol=ncol,nblk=nblk,i0=i0/4)
    near=mean[i0:i0+4][good[i0:i0+4]]
    S=float(np.min(near))/G if len(near) else 1
    peak=float(cs[iL]-cs[i0-1] if i0>0 else cs[iL])
    return dict(q='ok' if S<0.85 else 'noshadow',L=(iL+0.5)/4,ev=peak/4,S=S,G=G,i0=i0/4,ncol=ncol,nblk=nblk,cov=float(good[i0:iL+1].mean()))

def analyse4(r, cols=None, TMAX=28):
    """change point: profile (0.5 m bins along u, open pixels only) = S up to L, ground after"""
    cols=r['cols'] if cols is None else cols
    vals=[[] for _ in range(TMAX+2)]; ncol=0; nblk=0; first=[]
    for w,(ts,ls,bs,inB) in cols.items():
        if len(ts)==0 or ts[0]>1.5: continue
        ncol+=1; seen=False
        for t_,l_,b_,ib in zip(ts,ls,bs,inB):
            if ib and not seen: continue
            if b_ and not ib: nblk+=1; break
            i=int(t_)          # 1 px = 0.5 m bins
            if i>TMAX: break
            if not seen: first.append(t_)
            seen=True; vals[i].append(l_)
    if ncol<3 or not first: return dict(q='nocol')
    G=r['G']
    n=np.array([len(v) for v in vals],float); m=np.array([np.mean(v)/G if len(v) else 0 for v in vals])
    i0=int(np.median(first))
    ok=n>=max(2,0.3*ncol); idx=np.nonzero(ok[i0:])[0]+i0
    if len(idx)<8: return dict(q='short',ncol=ncol,nblk=nblk)
    x=m[idx]; w=n[idx]
    best=None
    for j in range(1,len(idx)-3):   # shadow = idx[:j], ground = idx[j:]
        w1=w[:j]; w2=w[j:]; a=np.average(x[:j],weights=w1); b=np.average(x[j:],weights=w2)
        sse=(w1*(x[:j]-a)**2).sum()+(w2*(x[j:]-b)**2).sum()
        if best is None or sse<best[0]: best=(sse,j,a,b)
    sse,j,a,b=best
    sig=math.sqrt(sse/w.sum())
    # the edge between the last shadow bin and the first ground bin
    Lpx=(idx[j-1]+idx[j])/2+0.5
    q='ok' if b-a>=0.12 and a<0.85 else 'noshadow'
    return dict(q=q,L=Lpx/2,S=float(a),Gf=float(b),c=float(b-a),sig=float(sig),snr=float((b-a)/max(sig,1e-3)),i0=i0/2,ncol=ncol,nblk=nblk,nb=int(j))
