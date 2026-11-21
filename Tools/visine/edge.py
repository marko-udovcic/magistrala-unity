import os
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'shadow.py')).read())
def AZ(x): return min(70,max(50,66+0.00143*x))
def bil(img,x,y):
    H,W=img.shape[:2]; x=np.clip(x,0,W-1.001); y=np.clip(y,0,H-1.001)
    x0=np.floor(x).astype(int); y0=np.floor(y).astype(int); fx=x-x0; fy=y-y0
    return (img[y0,x0]*(1-fx)*(1-fy)+img[y0,x0+1]*fx*(1-fy)+img[y0+1,x0]*(1-fx)*fy+img[y0+1,x0+1]*fx*fy)
def nearest(img,x,y):
    H,W=img.shape[:2]; return img[np.clip(np.round(y).astype(int),0,H-1),np.clip(np.round(x).astype(int),0,W-1)]
STEP=0.25; SM=12.0
def edges(key,az=None,dbg=False):
    v=ALL[key]; P=v['P']; x0,z0,x1,z1=v['bb']
    cx=sum(p[0] for p in P)/len(P)
    az=AZ(cx) if az is None else az
    ux=math.sin(math.radians(az)); uz=-math.cos(math.radians(az))
    mg=SM+4; gx0=math.floor(x0-mg); gz0=math.floor(z0-mg); gx1=math.ceil(x1+mg); gz1=math.ceil(z1+mg)
    rgb=crop(gx0,gz0,gx1,gz1); H,W=rgb.shape[:2]
    if rgb[...,3].min()<255: return dict(err='nodata')
    M=poly(P,gx0,gz0,W,H)
    others=np.zeros_like(M)
    for k in near(gx0,gz0,gx1,gz1):
        if k!=key: others|=poly(ALL[k]['P'],gx0,gz0,W,H)
    c,_=classes(rgb); a3=rgb[...,:3].astype(np.int16)
    lum=(a3@np.array([30,59,11])/100.).astype(np.float32)
    water=(a3[...,2]>a3[...,0]+12)&(a3[...,1]>a3[...,0]+4)
    bad=(c==VEG)|water|(others&~M)|M
    # orientation of the polygon
    A=sum(P[i][0]*P[(i+1)%len(P)][1]-P[(i+1)%len(P)][0]*P[i][1] for i in range(len(P)))
    sgn=1 if A>0 else -1
    ring=np.array(Image.fromarray(M.astype(np.uint8)*255).filter(ImageFilter.MaxFilter(41)))>0
    gm=ring&~bad
    if gm.sum()<20: return dict(err='noground')
    G=float(np.median(lum[gm]))
    out=[]
    for i in range(len(P)):
        ax,az_=P[i]; bx,bz=P[(i+1)%len(P)]
        ex,ez=bx-ax,bz-az_; Lg=math.hypot(ex,ez)
        if Lg<3: continue
        ex/=Lg; ez/=Lg
        # outward normal: for a polygon with positive signed area (x,z), the right-hand normal (ez,-ex) points ... test
        nx,nz=ez*sgn,-ex*sgn
        mx,mz=(ax+bx)/2+nx*0.6,(az_+bz)/2+nz*0.6
        if 0<=int((mz-gz0)*2)<H and 0<=int((mx-gx0)*2)<W and M[int((mz-gz0)*2),int((mx-gx0)*2)]: nx,nz=-nx,-nz
        cosn=nx*ux+nz*uz
        if cosn<0.5: continue
        # samples along the inner 70 % of the edge, offsets 0..SM along the normal
        ts=np.linspace(0.15,0.85,max(3,int(Lg*0.7*2)))
        ss=np.arange(0,SM+1e-6,STEP)
        X=(ax+ex*Lg*ts[None,:]+nx*ss[:,None]-gx0)*2-0.5; Z=(az_+ez*Lg*ts[None,:]+nz*ss[:,None]-gz0)*2-0.5
        Lm=bil(lum,X,Z); Bd=nearest(bad,X,Z)
        # a sample line ends at its first obstacle beyond 0.5 m
        okm=np.ones_like(Bd)
        for j in range(len(ts)):
            hit=np.nonzero(Bd[:,j]&(ss>=0.75))[0]
            if len(hit): okm[hit[0]:,j]=False
        okm&=~Bd
        out.append(dict(i=i,len=Lg,cos=cosn,prof=[(float(np.mean(Lm[k][okm[k]]))/G if okm[k].sum()>=max(2,0.4*len(ts)) else None) for k in range(len(ss))],n=len(ts)))
    return dict(G=G,edges=out,az=az) if not dbg else dict(G=G,edges=out,az=az,rgb=rgb,M=M,gx0=gx0,gz0=gz0)
def cpfit(prof,s0=0.75):
    """step fit on a profile (list of values / None, STEP m bins) beyond s0: returns (edge m, S, G, sig)"""
    ss=np.arange(len(prof))*STEP
    idx=[k for k,v in enumerate(prof) if v is not None and ss[k]>=s0]
    if len(idx)<10: return None
    x=np.array([prof[k] for k in idx]); best=None
    for j in range(1,len(idx)-6):
        a=x[:j].mean(); b=x[j:].mean(); sse=((x[:j]-a)**2).sum()+((x[j:]-b)**2).sum()
        if best is None or sse<best[0]: best=(sse,j,a,b)
    sse,j,a,b=best
    return ((ss[idx[j-1]]+ss[idx[j]])/2, float(a), float(b), math.sqrt(sse/len(idx)), ss[idx[0]])
def gradfit(prof,smax=10.0):
    """minimum (shadow core) then the steepest climb to the ground level: (edge m, contrast, Smin, Gfar)"""
    ss=np.arange(len(prof))*STEP
    v=np.array([np.nan if p is None else p for p in prof])
    # fill short gaps
    ok=~np.isnan(v)
    if ok.sum()<12: return None
    vi=np.interp(ss,ss[ok],v[ok]); last=ss[ok].max()
    k=np.array([1,2,3,2,1],float); k/=k.sum()
    sm=np.convolve(np.pad(vi,2,mode='edge'),k,mode='valid')
    rng=(ss>=0.5)&(ss<=min(smax,last-1.5))
    if rng.sum()<4: return None
    imin=np.nonzero(rng)[0][np.argmin(sm[rng])]
    d=np.gradient(sm,STEP)
    after=(np.arange(len(ss))>imin)&(ss<=last-0.5)
    if after.sum()<3: return None
    ig=np.nonzero(after)[0][np.argmax(d[after])]
    # sub-bin
    if 0<ig<len(d)-1:
        y0,y1,y2=d[ig-1],d[ig],d[ig+1]; den=y0-2*y1+y2; off=0.5*(y0-y2)/den if abs(den)>1e-9 else 0
    else: off=0
    far=sm[(ss>=ss[ig]+1.0)&(ss<=ss[ig]+3.5)&(ss<=last)]
    if len(far)<3: return None
    G=float(np.median(far))
    return (float(ss[ig]+off*STEP), G-float(sm[imin]), float(sm[imin]), G, float(d[ig]))
