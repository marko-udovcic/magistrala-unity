import os
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'shadow.py')).read())
from multiprocessing import Pool
def AZ(x): return min(70,max(50,66+0.00143*x))
def job(k):
    v=ALL[k]; P=v['P']; cx=sum(p[0] for p in P)/len(P)
    try:
        r=measure(k,AZ(cx))
        if 'err' in r: return k,dict(q=r['err'])
        a=analyse2(r); a['d']=r['d']; a['az']=round(AZ(cx),1); return k,a
    except Exception as e: return k,dict(q='err',e=str(e))
if __name__=='__main__':
    keys=list(ALL); t0=time.time(); out={}
    with Pool(7) as p:
        for i,(k,a) in enumerate(p.imap_unordered(job,keys,chunksize=20)):
            out['%d_%d:%d'%k]={kk:(round(vv,3) if isinstance(vv,float) else vv) for kk,vv in a.items()}
            if i%1000==0: print(i,round(time.time()-t0),file=sys.stderr,flush=True)
    json.dump(out,open('shadows.json','w'))
