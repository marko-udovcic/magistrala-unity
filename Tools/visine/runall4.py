import os
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'runall.py')).read().split("if __name__")[0])
def job3(k):
    v=ALL[k]; P=v['P']; cx=sum(p[0] for p in P)/len(P)
    try:
        r=measure(k,AZ(cx))
        if 'err' in r: return k,dict(q=r['err'])
        a=analyse4(r); a['d']=r['d']; a['az']=round(AZ(cx),1)
        ws=sorted(r['cols']); h=len(ws)//2
        if h>=4:
            a1=analyse4(r,cols={w:r['cols'][w] for w in ws[:h]}); a2=analyse4(r,cols={w:r['cols'][w] for w in ws[h:]})
            if a1.get('q')=='ok' and a2.get('q')=='ok': a['h1']=a1['L']; a['h2']=a2['L']
        return k,a
    except Exception as e: return k,dict(q='err',e=str(e))
if __name__=='__main__':
    keys=list(ALL); t0=time.time(); out={}
    with Pool(7) as p:
        for i,(k,a) in enumerate(p.imap_unordered(job3,keys,chunksize=20)):
            out['%d_%d:%d'%k]={kk:(round(vv,3) if isinstance(vv,float) else vv) for kk,vv in a.items()}
    json.dump(out,open('shadows4.json','w'))
    print(round(time.time()-t0))
