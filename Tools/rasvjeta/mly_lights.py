"""Street lights (and utility poles) of Kaštela from Mapillary map features (CC BY-SA 4.0, Mapillary contributors): the Graph API
`map_features` (object--street-light, object--support--utility-pole, object--support--pole) in 0.01° bbox chunks through BalkanDriver's
tools/world/mapillary/mly.py (token read in-process from token.txt, never logged; every response cached in its gitignored cache/).
Detections of one lamp (several sequences, both heads of a double-arm lamp) are merged within MERGE m. Output: game x / z (the region's
AEQD frame, as mly.ll2g), the number of merged detections -> Assets/Editor/rasvjeta-mapillary.json (facts only: positions).
PropsBuilder.Lamps (Assets/Editor/StreetLights.cs) snaps them to the D8 median / the kerbs and fills the gaps by rule.
Run: PYLIB=<folder with numpy, pyproj> python3 -I Tools/rasvjeta/mly_lights.py"""
import json, os, sys
if os.environ.get('PYLIB'): sys.path.insert(0, os.environ['PYLIB'])
sys.path.insert(0, '/Users/macair/Documents/BalkanDriver/tools/world/mapillary')
import mly
import numpy as np
HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, '..', '..', 'Assets', 'Editor', 'rasvjeta-mapillary.json')
VALS = 'object--street-light,object--support--pole,object--support--utility-pole'
LON0, LON1, LAT0, LAT1, ST = 16.315, 16.445, 43.530, 43.575, 0.01
X0, X1, Z0, Z1 = -10240, 0, -7168, -3500        # the playable core (StreetsBuilder AX0..AX1, AZ0..)
MERGE = {'object--street-light': 6.0, 'object--support--utility-pole': 3.0, 'object--support--pole': 3.0}

feats = {}
lo = LON0
while lo < LON1 - 1e-9:
    la = LAT0
    while la < LAT1 - 1e-9:
        d = mly.graph('map_features', fields='id,object_value,geometry,first_seen_at,last_seen_at', bbox=f'{lo:.4f},{la:.4f},{lo + ST:.4f},{la + ST:.4f}', object_values=VALS, limit=2000)
        for f in d.get('data', []): feats[f['id']] = f
        if len(d.get('data', [])) >= 2000: print('chunk full', lo, la)
        la += ST
    lo += ST

def cluster(P, r):
    """greedy merge: points within r of a cluster's running centre join it"""
    out = []
    for p in P[np.argsort(P[:, 0])]:
        best = None
        for c in out[-60:]:
            if abs(c[0] / c[2] - p[0]) < r and np.hypot(c[0] / c[2] - p[0], c[1] / c[2] - p[1]) < r: best = c; break
        if best is None: out.append([p[0], p[1], 1])
        else: best[0] += p[0]; best[1] += p[1]; best[2] += 1
    return [[round(c[0] / c[2], 2), round(c[1] / c[2], 2), c[2]] for c in out]

res = {}
for v in MERGE:
    P = []
    for f in feats.values():
        if f['object_value'] != v: continue
        gx, gz = mly.ll2g(*f['geometry']['coordinates'])
        if X0 <= gx <= X1 and Z0 <= gz <= Z1: P.append((gx, gz))
    P = np.array(P) if P else np.zeros((0, 2))
    res[v] = cluster(P, MERGE[v]) if len(P) else []
    print(v, len(P), 'detections ->', len(res[v]))
meta = {'source': 'Mapillary map features (CC BY-SA 4.0), Mapillary contributors; Tools/rasvjeta/mly_lights.py',
        'fields': '[game x, game z, merged detections]', 'merge_m': MERGE}
json.dump({'meta': meta, 'lights': res['object--street-light'], 'utility_poles': res['object--support--utility-pole'], 'poles': res['object--support--pole']},
          open(OUT, 'w'), separators=(',', ':'))
print('->', os.path.abspath(OUT), os.path.getsize(OUT) // 1024, 'KB')
