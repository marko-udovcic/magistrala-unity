import os
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'env0.py')).read())
import numpy as np, math
from ortho import tile
from classify import classes, VEG
from PIL import Image, ImageFilter
res={}
for tz in range(-7,-4):
  for tx in range(-10,0):
    t=tile(tx,tz)
    if t is None: continue
    for qy in range(2):
      for qx in range(2):
        a=t[qy*1024:(qy+1)*1024, qx*1024:(qx+1)*1024]
        if a[...,3].min()<255: continue
        c,_=classes(a); lum=a[...,:3].astype(np.float32)@np.array([.3,.59,.11],np.float32)
        loc=np.array(Image.fromarray(lum.astype(np.uint8)).filter(ImageFilter.BoxBlur(15))).astype(np.float32)
        V=(c==VEG).astype(np.float32)
        D=((lum<0.72*loc)&(c!=VEG)).astype(np.float32)
        V-=V.mean(); D-=D.mean()
        C=np.fft.irfft2(np.fft.rfft2(D)*np.conj(np.fft.rfft2(V)),s=V.shape)
        C=np.fft.fftshift(C); cy,cx=512,512
        W=C[cy-16:cy+17,cx-16:cx+17]
        # ignore the centre (overlap) and radius > 12 px
        yy,xx=np.mgrid[-16:17,-16:17]; r=np.hypot(yy,xx)
        Wm=np.where((r>=2)&(r<=12),W,-1e18)
        iy,ix=np.unravel_index(np.argmax(Wm),Wm.shape); dy,dx=yy[iy,ix],xx[iy,ix]
        # direction-weighted centroid in the ring 2..10 of positive values
        m=(r>=2)&(r<=10); w=np.clip(W-np.median(W[m]),0,None)*m
        mx=(w*xx).sum()/w.sum(); my=(w*yy).sum()/w.sum()
        az=math.degrees(math.atan2(mx,-my))%360; az2=math.degrees(math.atan2(dx,-dy))%360
        print(tx,tz,qx,qy,'peak',dx,dy,'az %.0f'%az2,'centroid az %.0f'%az,flush=True)
