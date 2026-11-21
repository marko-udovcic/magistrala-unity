import env
import numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS=None
MOS='/Users/macair/Documents/BalkanDriver/tools/world/dgu/cache/dof/mosaic-lidar/'
_c={}
def tile(tx,tz):
    k=(tx,tz)
    if k not in _c:
        try: _c[k]=np.asarray(Image.open(MOS+f't_{tx}_{tz}.png').convert('RGBA'))
        except FileNotFoundError: _c[k]=None
    return _c[k]
def crop(gx0,gz0,gx1,gz1):
    """RGBA crop, 0.5 m/px, rows = z ascending; origin pixel = (gx0,gz0)"""
    W=int(round((gx1-gx0)*2)); H=int(round((gz1-gz0)*2))
    out=np.zeros((H,W,4),np.uint8)
    px0=int(np.floor(gx0*2)); pz0=int(np.floor(gz0*2))
    for tz in range(int(np.floor(gz0/1024)), int(np.floor((gz1-1e-6)/1024))+1):
        for tx in range(int(np.floor(gx0/1024)), int(np.floor((gx1-1e-6)/1024))+1):
            t=tile(tx,tz)
            if t is None: continue
            ax0=max(px0, tx*2048); ax1=min(px0+W, tx*2048+2048)
            az0=max(pz0, tz*2048); az1=min(pz0+H, tz*2048+2048)
            if ax0>=ax1 or az0>=az1: continue
            out[az0-pz0:az1-pz0, ax0-px0:ax1-px0]=t[az0-tz*2048:az1-tz*2048, ax0-tx*2048:ax1-tx*2048]
    return out
