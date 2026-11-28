"""Cut mask of the eastern riva models (80 Kambelovac, 81 Gomilica, 82 Sućurac) for cut_package.py: their asphalt + sidewalk
(the promenade they model up to the sea) in BalkanDriver split-kastela game metres, same shape as paket-2711/riva-maska.json.
The package mask (PaketData.RIVA_MASKA) covers models 66-71 only; RivaBuilder passes this file as a second mask.
Usage: tools/world/dgu/cache/venv/bin/python -I Tools/riva/maska_istok.py  -> Tools/riva/riva-maska-istok.json
"""
import hashlib, json, struct
from pathlib import Path
import numpy as np
from shapely.geometry import Polygon, mapping
from shapely.ops import unary_union

BD = Path('/Users/macair/Documents/BalkanDriver')
W = BD / 'client/public/assets/world/split-kastela'
OUT = Path(__file__).resolve().parent / 'riva-maska-istok.json'


def mesh_union(p, transform):
    b = p.read_bytes(); jl = struct.unpack_from('<I', b, 12)[0]; g = json.loads(b[20:20 + jl]); bs = 28 + jl
    def acc(i):
        a = g['accessors'][i]; v = g['bufferViews'][a['bufferView']]; k = 3 if a['type'] == 'VEC3' else 1
        dt = {5126: '<f4', 5125: '<u4', 5123: '<u2'}[a['componentType']]
        return np.ndarray((a['count'], k), dtype=dt, buffer=b, offset=bs + v.get('byteOffset', 0) + a.get('byteOffset', 0),
                          strides=(v.get('byteStride', np.dtype(dt).itemsize * k), np.dtype(dt).itemsize))
    tris = []
    for n in g['nodes']:
        if n.get('name') not in ['asphalt', 'sidewalk']: continue
        for pr in g['meshes'][n['mesh']]['primitives']:
            P = acc(pr['attributes']['POSITION'])[:, [0, 2]].astype(float) + np.array(n.get('translation', [0, 0, 0]))[[0, 2]]
            a, b_, c, d, x, z = transform; P = P @ np.array([[a, c], [b_, d]]) + np.array([x, z])
            for ids in acc(pr['indices']).ravel().reshape(-1, 3):
                t = Polygon(P[ids])
                if t.area > 1e-10: tris.append(t)
    return unary_union(tris)


models = json.load(open(W / 'model-junctions.json'))['junctions']
entries = []
for num in (80, 81, 82):
    m = next(m for m in models if m['key'] == f'krizanje-{num}')
    path = BD / 'client/public' / m['glb'].lstrip('/')
    foot = mesh_union(path, [*m['lin'], m['x'], m['z']])
    entries.append({'model': m['key'], 'glb': m['glb'], 'sha256': hashlib.sha256(path.read_bytes()).hexdigest(),
                    'footprint': json.loads(json.dumps(mapping(foot)))})
    print(m['key'], round(foot.area), [round(v) for v in foot.bounds])
OUT.write_text(json.dumps({'version': 1, 'coordinateSystem': 'BalkanDriver split-kastela game metres [x,z], x east, z south',
                           'models': entries}, separators=(',', ':')) + '\n')
print(OUT, OUT.stat().st_size)
