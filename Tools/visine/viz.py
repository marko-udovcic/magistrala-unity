import os
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'shadow.py')).read())
from PIL import ImageOps
import random
def viz(keys,az,out):
    ims=[]
    for k in keys:
        r=measure(k,az,dbg=True)
        if 'err' in r: continue
        a=analyse2(r)
        rgb=r['rgb'][...,:3]; H,W=rgb.shape[:2]
        im=ImageOps.autocontrast(Image.fromarray(rgb),cutoff=1).convert('RGB')
        o=np.array(im)
        o[r['M']&~np.array(Image.fromarray(r['M'].astype(np.uint8)*255).filter(ImageFilter.MinFilter(3)))>0]=(0,255,255)
        if a['q']=='ok':
            L=a['L']*2; band=(r['t']>L-0.5)&(r['t']<=L+0.5)&~r['B']
            o[band]=(255,0,255)
        im=Image.fromarray(o).resize((W*3,H*3),Image.NEAREST)
        sc=240/max(im.size); im=im.resize((int(im.size[0]*sc),int(im.size[1]*sc)),Image.LANCZOS)
        c=Image.new('RGB',(240,240)); c.paste(im,(0,0)); d=ImageDraw.Draw(c)
        txt=f"{k[2]} {a['q']} " + (f"L{a['L']:.1f} S{a["S"]:.2f} A{a["La"]:.1f} i{a["i0"]}" if a['q']=='ok' else '')
        d.rectangle((0,0,240,12),fill=(0,0,0)); d.text((2,1),txt,fill=(255,255,0)); ims.append(c)
    S=Image.new('RGB',(240*6,240*((len(ims)+5)//6)))
    for j,im in enumerate(ims): S.paste(im,((j%6)*240,(j//6)*240))
    S.save(out)
if __name__=='__main__':
    random.seed(int(sys.argv[2]) if len(sys.argv)>2 else 3)
    keys=[k for k in ALL if ALL[k]['b']['kind']=='house']; random.shuffle(keys)
    viz(keys[:24],float(sys.argv[1]),'viz.png')
