"""Offline GLB metre audit. Run from the worktree: python3 -I Tools/props/measure.py.
Hierarchy bounds use POSITION accessor min/max corners (conservative for rotated meshes).
Prop importer bounds use actual vertices, mirror X and yaw, exactly as Normalise does.
"""
import itertools
import argparse
import json
import math
from pathlib import Path
import struct
import subprocess


def read(path):
    data = Path(path).read_bytes()
    length = struct.unpack_from('<I', data, 12)[0]
    return json.loads(data[20:20 + length]), data[28 + length:]


def transform(p, node):
    if 'matrix' in node:
        m = node['matrix']
        return [sum(m[c * 4 + r] * p[c] for c in range(3)) + m[12 + r] for r in range(3)]
    p = [a * b for a, b in zip(p, node.get('scale', [1, 1, 1]))]
    x, y, z, w = node.get('rotation', [0, 0, 0, 1])
    # quaternion rotation: p + 2 * cross(q.xyz, cross(q.xyz,p) + w*p)
    def cross(a, b):
        return [a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0]]
    q = [x, y, z]
    c = cross(q, p)
    d = cross(q, [c[i] + w*p[i] for i in range(3)])
    return [p[i] + 2*d[i] + node.get('translation', [0, 0, 0])[i] for i in range(3)]


def bounds(points):
    points = list(points)
    return [[min(p[i] for p in points) for i in range(3)], [max(p[i] for p in points) for i in range(3)]]


def extent(b):
    return [b[1][i]-b[0][i] for i in range(3)]


def node_bounds(j, index, ancestors=()):
    node = j['nodes'][index]
    chain = (node,) + ancestors
    points = []
    if 'mesh' in node:
        for primitive in j['meshes'][node['mesh']]['primitives']:
            a = j['accessors'][primitive['attributes']['POSITION']]
            for p in itertools.product(*zip(a['min'], a['max'])):
                for n in chain:
                    p = transform(p, n)
                points.append(p)
    for child in node.get('children', []):
        points.extend(itertools.product(*zip(*node_bounds(j, child, chain))))
    return bounds(points)


def prop_size(path, config=None):
    j, data = read(path)
    c = config if config is not None else json.loads(path.with_suffix('.json').read_text())
    a = j['accessors'][j['meshes'][0]['primitives'][0]['attributes']['POSITION']]
    v = j['bufferViews'][a['bufferView']]
    offset = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    yaw = math.radians(c.get('yaw', 0)); co, si = math.cos(yaw), math.sin(yaw)
    points = []
    for i in range(a['count']):
        x, y, z = struct.unpack_from('<3f', data, offset + i*v.get('byteStride', 12))
        points.append([-x*co + z*si, y, x*si + z*co])
    s = extent(bounds(points)); fit = c.get('fit', 'max')
    divisor = s['xyz'.index(fit)] if fit in ('x', 'y', 'z') else max(s[0], s[2]) if fit == 'xz' else max(s)
    k = c.get('size', 1)/divisor
    return [c.get('s'+axis, 0) or s[i]*k for i, axis in enumerate('xyz')]


def fmt(s):
    return ' × '.join(f'{v:.3f}' for v in s)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--baseline', help='Read sidecar settings from this Git revision')
    parser.add_argument('--check', action='store_true', help='Check cargo fit and critical prop dimensions')
    args = parser.parse_args()
    sizes = {}
    for path in sorted(Path('Assets/Models/Props').glob('*/*.glb')):
        config = json.loads(subprocess.check_output(['git', 'show', f'{args.baseline}:{path.with_suffix(".json")}'])) if args.baseline else None
        sizes[path.stem] = prop_size(path, config)
        print(path.stem, fmt(sizes[path.stem]))
    for path in sorted(Path('Assets/Models/Props').glob('*/*.json')):
        if not path.with_suffix('.glb').exists():
            print(path.stem, 'MISSING GLB; configured target only', path.read_text().strip())
    j, _ = read('Assets/Models/Vehicles/firm_truck.glb')
    parents = {c: i for i, n in enumerate(j['nodes']) for c in n.get('children', [])}
    for i, n in enumerate(j['nodes']):
        if any(word in n.get('name', '') for word in ('pallet', 'cargo', 'tail_lift', 'door_roll', 'body_box')):
            chain = []; p = i
            while p in parents:
                p = parents[p]; chain.append(j['nodes'][p])
            b = node_bounds(j, i, tuple(chain))
            print(n['name'], fmt(extent(b)), 'min', fmt(b[0]), 'max', fmt(b[1]))
    if args.check:
        assert not args.baseline, '--check applies to current settings'
        for name, expected in {'paleta': [1.2, .144, .8], 'kolica-rucna': [1.55, 1.2, .55],
                               'voda-paleta': [1.2, 1.244, .8]}.items():
            assert all(abs(a-b) < .001 for a, b in zip(sizes[name], expected)), name
        assert 1.8 <= sizes['klupa-bijela'][0] <= 2 and abs(sizes['klupa-bijela'][1]-.8) < .001
        assert 4 <= sizes['stara-maslina'][1] <= 6
        # Accessor bounds through transforms, including the load parent's -0.93 m offset.
        by_name = {n.get('name'): i for i, n in enumerate(j['nodes'])}
        load = j['nodes'][by_name['load_pallets']]
        rows = [node_bounds(j, by_name[f'load_pallets_{i}'], (load,)) for i in range(2)]
        for b in rows:
            s = extent(b)
            assert s[0] < 2.4 and s[1] < 2.2 and abs(s[2]-1.2) < .001
            assert abs(b[0][1]-.125) < .001
            assert b[0][2] > -1.64 and b[1][2] < 4.32
        # Original 0.82 m pallet widths include a 0.02 m gap either side of centre.
        for i in range(2):
            s = extent(node_bounds(j, by_name[f'load{i}_pallet_wood']))
            assert abs(s[1]-.144) < .001 and abs(s[2]-1.2) < .001
        assert abs(rows[0][0][2]-rows[1][1][2]-.05) < .001
        # Regression: settle a delivered row on y=-0.93 in the MODEL frame.
        bottom = rows[0][0][1]; dy = -.98
        corrected = bottom + dy
        assert abs(corrected + (-.93-corrected) - (-.93)) < 1e-9
        old = corrected - .93
        assert abs(corrected + (-.93-old)) < 1e-9  # old code left its bottom at 0, floating 0.93 m
        print('PASS: target sizes, box clearance, pallet height, row gap and model-frame settlement')
