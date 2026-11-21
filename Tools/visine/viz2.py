import os
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'viz.py')).read().split("if __name__")[0])
def AZ(x): return min(70,max(50,66+0.00143*x))
def viz2(keys,out,labels=None,size=300):
    ims=[]
    for n,k in enumerate(keys):
        P=ALL[k]['P']; cx=sum(p[0] for p in P)/len(P); az=AZ(cx)
        r=measure(k,az,dbg=True)
        if 'err' in r: continue
        a=analyse2(r)
        rgb=r['rgb'][...,:3]; H,W=rgb.shape[:2]
        im=ImageOps.autocontrast(Image.fromarray(rgb),cutoff=1).convert('RGB')
        o=np.array(im)
        edge=r['M']&~(np.array(Image.fromarray(r['M'].astype(np.uint8)*255).filter(ImageFilter.MinFilter(3)))>0)
        o2=o.copy()
        if a['q']=='ok':
            L=a['L']*2; band=(r['t']>L-0.5)&(r['t']<=L+0.5)&~r['B']
            o2[band]=(255,0,255)
        o2[edge]=(0,255,255)
        big=np.concatenate([o,o2],1)
        im=Image.fromarray(big).resize((W*2*4,H*4),Image.BICUBIC)
        sc=min(2*size/im.size[0],size/im.size[1]); im=im.resize((int(im.size[0]*sc),int(im.size[1]*sc)),Image.LANCZOS)
        c=Image.new('RGB',(2*size,size)); c.paste(im,(0,0)); d=ImageDraw.Draw(c)
        txt=f"{k} {a['q']} " + (f"L{a['L']:.1f} S{a['S']:.2f} i{a['i0']}" if a['q']=='ok' else '')+(' '+labels[n] if labels else '')
        d.rectangle((0,0,2*size,12),fill=(0,0,0)); d.text((2,1),txt,fill=(255,255,0)); ims.append(c)
    S=Image.new('RGB',(2*size*3,size*((len(ims)+2)//3)))
    for j,im in enumerate(ims): S.paste(im,((j%3)*2*size,(j//3)*size))
    S.save(out)
if __name__=='__main__':
    o=json.load(open('osmref.json'))
    ks=[tuple(map(int,k.replace(':','_').split('_'))) for k in o]
    viz2(ks,'osm.png',[f"lv{o[k]['lv']}" for k in o],size=240)
