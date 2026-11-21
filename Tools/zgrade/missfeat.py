import sys; sys.path.insert(0,'.'); import env, json
import numpy as np
from PIL import Image, ImageDraw, ImageFilter
from ortho import crop
from classify import classes, TERRA
def feats(m):
    cx,cz=m['gx'],m['gz']; R=30
    a=crop(cx-R,cz-R,cx+R,cz+R); c,_=classes(a)
    im=Image.new('L',(a.shape[1],a.shape[0]),0); ImageDraw.Draw(im).polygon([((x-cx+R)*2,(z-cz+R)*2) for x,z in m['rect']],fill=255)
    M=np.array(im)>0; D=np.array(im.filter(ImageFilter.MaxFilter(9)))>0; ring=D&~M
    rgb=a[...,:3].astype(int)
    t=rgb[M&(c==TERRA)]
    rb=float(np.median(t[:,0]-t[:,2])) if len(t) else 0
    lum=rgb@[.3,.59,.11]
    return dict(rb=rb, ring_terra=round(float((c[ring]==TERRA).mean()),2), in_terra=round(float((c[M]==TERRA).mean()),2),
                contrast=round(float(abs(lum[M].mean()-lum[ring].mean())),1), lstd=round(float(lum[M].std()),1))
if __name__=='__main__':
    M=json.load(open('missing.json'))
    for m in M:
        if m['other']<0.3: m.update(feats(m))
    json.dump(M,open('missing.json','w'))
