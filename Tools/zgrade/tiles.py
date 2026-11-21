import struct, json, os
REG='/Users/macair/Documents/BalkanDriver/client/public/assets/world/split-kastela/tiles/'
def buildings(tx,tz):
    p=REG+f'{tx}_{tz}.bin'
    if not os.path.exists(p): return []
    b=open(p,'rb').read(); o=16; out=[]
    while o+8<=len(b):
        t,l=struct.unpack_from('<ii',b,o); o+=8
        if t==4: out=json.loads(b[o:o+l].decode())
        o+=l
    return out
def heights(tx,tz):
    import numpy as np
    p=REG+f'{tx}_{tz}.bin'; b=open(p,'rb').read(); res=struct.unpack_from('<H',b,6)[0]; o=16
    while o+8<=len(b):
        t,l=struct.unpack_from('<ii',b,o); o+=8
        if t==1: return np.frombuffer(b[o:o+l],dtype='<f4').reshape(res,res)
        o+=l
