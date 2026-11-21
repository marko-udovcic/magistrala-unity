import numpy as np
VEG, SHADOW, TERRA, LIGHT, GREY, DARK, OTHER, PANEL = range(8)
NAMES=['veg','shadow','terra','light','grey','dark','other','panel']
def classes(rgb):
    a=rgb[...,:3].astype(np.int16); r,g,b=a[...,0],a[...,1],a[...,2]
    mx=a.max(-1); mn=a.min(-1); V=mx; S=(mx-mn)/np.maximum(mx,1)
    c=np.full(r.shape, OTHER, np.uint8)
    lowsat=S<0.17
    c[lowsat & (V>=95)]=GREY
    c[lowsat & (V>=178)]=LIGHT
    c[(V<95)&(V>=50)&(S<0.25)]=DARK
    rb=r-b; hue=(g-b)/np.maximum(rb,1)
    terra=(r>=g)&(r>=b)&(rb>=22)&(hue<0.56)&(V>=75)
    c[terra]=TERRA
    exg=2*g-r-b
    c[((exg>20)&(g>=r))|(exg>32)]=VEG
    c[V<50]=SHADOW
    panel=(V>=25)&(V<105)&(b>=r-3)&(b>=g-8)&(exg<10)
    return c, panel
LUT=np.array([[60,140,60],[20,20,40],[220,90,50],[250,250,250],[160,160,160],[80,80,90],[200,180,120],[30,60,200]],np.uint8)
ROOFNESS=np.array([-1,-0.6,1,0.8,0.25,0.0,-0.3,0],np.float32)
