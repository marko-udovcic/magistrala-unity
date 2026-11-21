import os
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'edge.py')).read())
from multiprocessing import Pool
def job(k):
    try:
        r=edges(k)
        if 'err' in r: return k,None
        res=[]
        for e in r['edges']:
            f=gradfit(e['prof']); g=cpfit(e['prof'])
            res.append(dict(len=round(e['len'],2),cos=round(e['cos'],3),gf=None if f is None else [round(x,3) for x in f],cp=None if g is None else [round(float(x),3) for x in g]))
        return k,res
    except Exception as ex: return k,None
if __name__=='__main__':
    out={}
    with Pool(7) as p:
        for k,res in p.imap_unordered(job,list(ALL),chunksize=20): out['%d_%d:%d'%k]=res
    json.dump(out,open('edges.json','w'))
