#!/usr/bin/env python3
"""Offline finishing for the three fleet GLBs; never modifies assets-src.
Reuses the L4H1 binary helpers. Preserves each model's eye/steering coordinates,
extracts its actual windscreen and wipers, and enables emissive import variants.
No Blender, Unity or third-party modules required.
"""
import sys
sys.dont_write_bytecode = True

import argparse
import copy
import importlib.util
import math
from pathlib import Path
import struct

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('l4finish', ROOT / 'Tools/kombi-l4h1/finish_glb.py')
l4 = importlib.util.module_from_spec(spec); spec.loader.exec_module(l4)
REVISION = 'magistrala_fleet_cab_v1'


def values(d, b, index):
    a = d['accessors'][index]; v = d['bufferViews'][a['bufferView']]
    size = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
    fmt = '<' + {5126: 'f', 5123: 'H', 5125: 'I', 5121: 'B'}[a['componentType']] * size
    stride = v.get('byteStride', struct.calcsize(fmt)); offset = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    return [struct.unpack_from(fmt, b, offset + i * stride) for i in range(a['count'])]


def sub(a, b): return tuple(x-y for x, y in zip(a, b))
def dot(a, b): return sum(x*y for x, y in zip(a, b))
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])
def unit(v):
    length = math.sqrt(dot(v, v))
    return tuple(x / length for x in v)


def components(positions, triangles):
    # Weld equal positions across UV/normal seams, then label connected triangle islands.
    lookup = {}; parents = []
    ids = []
    for p in positions:
        key = tuple(round(c, 5) for c in p)
        if key not in lookup: lookup[key] = len(parents); parents.append(len(parents))
        ids.append(lookup[key])
    def root(i):
        while parents[i] != i:
            parents[i] = parents[parents[i]]; i = parents[i]
        return i
    for t in triangles:
        a = root(ids[t[0]])
        for i in t[1:]: parents[root(ids[i])] = a
    groups = {}
    for t in triangles: groups.setdefault(root(ids[t[0]]), []).append(t)
    return list(groups.values())


def bounds(p, tris):
    used = {i for t in tris for i in t}
    return tuple(min(p[i][k] for i in used) for k in range(3)), tuple(max(p[i][k] for i in used) for k in range(3))


def scan(d, b):
    body = next(n for n in d['nodes'] if n.get('name') == 'body')
    for prim in d['meshes'][body['mesh']]['primitives']:
        mat = d['materials'][prim['material']]['name']
        if mat not in ('Glass', 'Rubber', 'Black_Plastic'): continue
        p = values(d, b, prim['attributes']['POSITION']); idx = [i[0] for i in values(d, b, prim['indices'])]
        for group in components(p, list(zip(idx[::3], idx[1::3], idx[2::3]))):
            lo, hi = bounds(p, group)
            if mat == 'Glass' or (lo[1] > .9 and lo[2] > .7 and hi[1]-lo[1] < .5):
                print(mat, len(group), tuple(round(x, 3) for x in lo), tuple(round(x, 3) for x in hi))


def add_mesh(d, name, primitives, parent):
    mi = len(d['meshes']); d['meshes'].append(dict(name=name, primitives=primitives))
    ni = len(d['nodes']); d['nodes'].append(dict(name=name, mesh=mi)); parent.setdefault('children', []).append(ni)
    return d['nodes'][ni]


def indices(d, b, triangles):
    return l4.accessor(d, b, [(i,) for t in triangles for i in t], 'I', 'SCALAR', 5125, 34963)


def finish(path):
    d, b = l4.read(path)
    if d.setdefault('extras', {}).get(REVISION): return
    body = next(n for n in d['nodes'] if n.get('name') == 'body')
    parent = next(n for n in d['nodes'] if d['nodes'].index(body) in n.get('children', []))
    # Keep model-specific cab geometry: the L4H1 absolute eye/steering/sill values do not apply to these vans.
    for m in d['materials']:
        name = m.get('name', '')
        if name == 'Gauges': m['emissiveFactor'] = [1, 1, 1]
        elif name == 'Needle': m['emissiveFactor'] = [1, .25, .05]
        elif name.startswith('Lights_') and not any(m.get('emissiveFactor', [])):
            m['emissiveFactor'] = [.001, .001, .001]
    # The widest front glass island is the windscreen. Separate it from the merged body so WindscreenFx
    # can hide only this transparent renderer in cab view. Keep side/rear glass and all body geometry.
    screen = None; wipers = []
    for prim in list(d['meshes'][body['mesh']]['primitives']):
        mat = d['materials'][prim['material']]['name']
        if mat not in ('Glass', 'Rubber', 'Black_Plastic'): continue
        p = values(d, b, prim['attributes']['POSITION']); idx = [i[0] for i in values(d, b, prim['indices'])]
        groups = components(p, list(zip(idx[::3], idx[1::3], idx[2::3])))
        if mat == 'Glass':
            candidates = [g for g in groups if (lambda lo, hi: hi[0]-lo[0] > 1 and lo[2] > .3)(*bounds(p, g))]
            assert candidates, f'{path}: windscreen not found'
            g = max(candidates, key=lambda g: bounds(p, g)[1][0]-bounds(p, g)[0][0])
            allglass = [t for group in candidates for t in group]
            screen = (prim, p, g, allglass)
            keep = [t for group in groups if all(group is not c for c in candidates) for t in group]
            if keep: prim['indices'] = indices(d, b, keep)
            else: d['meshes'][body['mesh']]['primitives'].remove(prim)
        # Wiper geometry is selected below using each model's measured bounding regions.
        else:
            regions = {'sprinter': (1.36, 1.50, 2.42, 2.68), 'mali-kombi': (1.09, 1.19, 1.07, 1.23), 'galeb': (1.29, 1.50, 2.12, 2.39)}
            y0, y1, z0, z1 = regions[path.stem.split('_lod')[0]]
            selected = []
            for g in groups:
                lo, hi = bounds(p, g)
                if lo[1] > y0 and hi[1] < y1 and lo[2] > z0 and hi[2] < z1 and .01 < hi[0]-lo[0] < .8:
                    selected.append(g); wipers.append((prim, p, g))
            if selected:
                prim['indices'] = indices(d, b, [t for g in groups if all(g is not s for s in selected) for t in g])
    assert screen
    prim, p, g, allglass = screen
    original = copy.deepcopy(prim); original['indices'] = indices(d, b, allglass)
    add_mesh(d, 'windscreen_glass', [original], parent)
    # Largest screen triangle defines a stable surface frame, same principle as MirrorCams' optical plane detection.
    tri = max(g, key=lambda t: dot(cross(sub(p[t[1]], p[t[0]]), sub(p[t[2]], p[t[0]])), cross(sub(p[t[1]], p[t[0]]), sub(p[t[2]], p[t[0]]))))
    normal = unit(cross(sub(p[tri[1]], p[tri[0]]), sub(p[tri[2]], p[tri[0]])))
    if normal[2] > 0: normal = tuple(-x for x in normal)
    lo, hi = bounds(p, g)
    fx = copy.deepcopy(original)
    fx['indices'] = indices(d, b, g)
    shifted = [tuple(v[k] + normal[k]*.004 for k in range(3)) for v in p]
    fx['attributes']['POSITION'] = l4.accessor(d, b, shifted, 'fff', 'VEC3', 5126, 34962)
    uv = [((hi[0]-v[0])/(hi[0]-lo[0]), (v[1]-lo[1])/(hi[1]-lo[1])) for v in p]
    fx['attributes']['TEXCOORD_0'] = l4.accessor(d, b, uv, 'ff', 'VEC2', 5126, 34962)
    add_mesh(d, 'windscreen_fx', [fx], parent)
    # Extract original wiper islands, retaining all attributes and materials. Arms share each side's spindle.
    # Local X points along the parked arms; local Z is normal to the glass (Unity mirrors X).
    assert wipers, f'{path}: no wiper islands selected; inspect --scan before changing regions'
    # Rotation about X: local +Z into cab, local +Y up glass. Match L4H1's spindle frame.
    normal = unit((0, normal[1], normal[2]))
    up = (0, -normal[2], normal[1])
    qy = math.sqrt((1 + up[1]) / 2)
    rotation = [0, qy, normal[1] / (2*qy), 0]
    for side, sign in [('L', 1), ('R', -1)]:
        def side_of(pos, group):
            lo, hi = bounds(pos, group)
            if path.stem.startswith('mali-kombi') and hi[0]-lo[0] < .04:
                return 1 if lo[0] > .2 else -1
            return 1 if sum(pos[i][0] for i in {i for t in group for i in t}) > 0 else -1
        chunks = [(pr, pos, group) for pr, pos, group in wipers if side_of(pos, group) == sign]
        if not chunks: continue
        allp = [pos[i] for _, pos, group in chunks for i in {i for t in group for i in t}]
        # The spindle is at the lower/inboard end of the parked arm.
        spindle = min(allp, key=lambda v: v[1])
        node = dict(name='wiper_'+side, translation=list(spindle), rotation=rotation)
        ni = len(d['nodes']); d['nodes'].append(node); parent['children'].append(ni)
        for k, (pr, pos, group) in enumerate(chunks):
            part = copy.deepcopy(pr); part['indices'] = indices(d, b, group)
            def local(v, translate=True):
                x,y,z = sub(v, spindle) if translate else v
                return -x, up[1]*y+up[2]*z, normal[1]*y+normal[2]*z
            part['attributes']['POSITION'] = l4.accessor(d, b, [local(v) for v in pos], 'fff', 'VEC3', 5126, 34962)
            if 'NORMAL' in part['attributes']:
                part['attributes']['NORMAL'] = l4.accessor(d, b, [local(v, False) for v in values(d,b,pr['attributes']['NORMAL'])], 'fff', 'VEC3', 5126, 34962)
            if 'TANGENT' in part['attributes']:
                del part['attributes']['TANGENT'] # no wiper normal map
            add_mesh(d, f'wiper_{side}_m{k}', [part], node)
    assert all(any(n.get('name') == 'wiper_'+side for n in d['nodes']) for side in ('L','R'))
    d['extras'][REVISION] = True
    l4.write(path, d, b)
    print(f'{path.name}: screen + {len(wipers)} wiper islands, emission, {len(d["nodes"])} nodes')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('paths', nargs='+', type=Path); parser.add_argument('--scan', action='store_true'); args = parser.parse_args()
    for path in args.paths:
        if args.scan: print(path); scan(*l4.read(path))
        else: finish(path)
