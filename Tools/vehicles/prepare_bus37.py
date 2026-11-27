#!/usr/bin/env python3
"""Copy/verify bus37 with explicit wheel mesh children; Python stdlib only.

python3 Tools/vehicles/prepare_bus37.py /path/to/obrada-2711 [--verify-only]
Only the destination GLB JSON is edited; mesh/accessor/image data and BIN stay intact.
"""
import argparse
import copy
import json
import math
from pathlib import Path
import shutil
import struct

DEST = Path(__file__).resolve().parents[2] / 'Assets/Models/Vehicles/Traffic/bus37'
MODELS = ('autobus-37.glb', 'autobus-37_lod1.glb', 'autobus-37_lod2.glb')
DISPLAYS = ('display-37-trogir.png', 'display-37-split.png')
WHEELS = ('wheel_FL', 'wheel_FR', 'wheel_RL', 'wheel_RR')
IDENTITY = [[float(i == k) for k in range(4)] for i in range(4)]


def mul(a, b):
    return [[sum(a[r][k] * b[k][c] for k in range(4)) for c in range(4)] for r in range(4)]


def point(m, v):
    return tuple(sum(m[r][k] * v[k] for k in range(3)) + m[r][3] for r in range(3))


def inverse(m):
    a = [list(row) + list(IDENTITY[i]) for i, row in enumerate(m)]
    for c in range(4):
        pivot = max(range(c, 4), key=lambda r: abs(a[r][c]))
        a[c], a[pivot] = a[pivot], a[c]
        d = a[c][c]
        assert abs(d) > 1e-12, 'singular node transform'
        a[c] = [v / d for v in a[c]]
        for r in range(4):
            if r != c:
                d = a[r][c]
                a[r] = [x - d * y for x, y in zip(a[r], a[c])]
    return [row[4:] for row in a]


def local(n):
    if 'matrix' in n:
        return [[n['matrix'][c * 4 + r] for c in range(4)] for r in range(4)]
    x, y, z, w = n.get('rotation', (0, 0, 0, 1))
    s = n.get('scale', (1, 1, 1))
    t = n.get('translation', (0, 0, 0))
    r = [[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
         [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
         [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]]
    return [[r[i][c] * s[c] for c in range(3)] + [t[i]] for i in range(3)] + [[0, 0, 0, 1]]


def trs(m):
    # These assets only need translation/scale; fail rather than silently lose a rotation or shear.
    assert all(abs(m[r][c]) < 1e-10 for r in range(3) for c in range(3) if r != c), 'unexpected rotated reparent transform'
    return {'translation': [m[r][3] for r in range(3)], 'rotation': [0, 0, 0, 1],
            'scale': [m[r][r] for r in range(3)]}


def read(path):
    raw = path.read_bytes()
    assert struct.unpack_from('<III', raw) == (0x46546c67, 2, len(raw))
    size, kind = struct.unpack_from('<II', raw, 12)
    assert kind == 0x4e4f534a
    return json.loads(raw[20:20+size]), raw[20+size:]


def write(path, j, tail):
    data = json.dumps(j, separators=(',', ':'), ensure_ascii=False).encode()
    data += b' ' * (-len(data) % 4)
    path.write_bytes(struct.pack('<IIIII', 0x46546c67, 2, 20+len(data)+len(tail), len(data), 0x4e4f534a) + data + tail)


def worlds(j):
    result = {}
    def walk(i, parent):
        assert i not in result, 'cycle or multiple parents'
        result[i] = mul(parent, local(j['nodes'][i]))
        for c in j['nodes'][i].get('children', []):
            walk(c, result[i])
    for scene in j['scenes']:
        # Current bus assets contain a single scene. Shared roots across scenes are unsupported.
        for i in scene['nodes']:
            walk(i, IDENTITY)
    assert len(result) == len(j['nodes']), 'unreachable node'
    return result


def values(j, binary, ai):
    a = j['accessors'][ai]
    assert 'sparse' not in a
    v = j['bufferViews'][a['bufferView']]
    fmt = {5126: 'f', 5125: 'I', 5123: 'H', 5121: 'B'}[a['componentType']]
    count = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
    size = struct.calcsize(fmt) * count
    offset = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    return [struct.unpack_from('<' + fmt * count, binary, offset + i * v.get('byteStride', size)) for i in range(a['count'])]


def mesh_points(j, tail):
    size, kind = struct.unpack_from('<II', tail)
    assert kind == 0x004e4942 and size == len(tail)-8
    binary = tail[8:]
    wm = worlds(j)
    result = {}
    for ni, n in enumerate(j['nodes']):
        if 'mesh' not in n:
            continue
        mi = n['mesh']
        assert mi not in result, 'unexpected instanced mesh'
        result[mi] = [point(wm[ni], p) for pr in j['meshes'][mi]['primitives']
                      for p in values(j, binary, pr['attributes']['POSITION'])]
    return result


def bounds(points):
    return tuple(min(v[d] for v in points) for d in range(3)) + tuple(max(v[d] for v in points) for d in range(3))


def repair(j):
    wm = worlds(j)
    wheels = {i: n for i, n in enumerate(j['nodes']) if n.get('name') in WHEELS}
    assert len(wheels) == 4
    # Rim primitives already share the tyre mesh in the supplied revision. Move the entire
    # mesh instance into an explicit child: no splitting/reindexing or buffer edits needed.
    for i, n in list(enumerate(j['nodes'])):
        if 'mesh' not in n:
            continue
        mats = [j['materials'][p['material']]['name'] for p in j['meshes'][n['mesh']]['primitives']]
        if 'Rim' not in mats:
            continue
        origin = point(wm[i], (0, 0, 0))
        nearest = min(wheels, key=lambda w: math.dist(origin, point(wm[w], (0, 0, 0))))
        assert set(mats) <= {'Rim', 'Tyre'}, 'mixed body/rim mesh needs a separate preparation'
        child = {'name': wheels[nearest]['name'] + '_rim_hub', 'mesh': n.pop('mesh'),
                 **trs(mul(inverse(wm[nearest]), wm[i]))}
        wheels[nearest].setdefault('children', []).append(len(j['nodes']))
        j['nodes'].append(child)
    return j


def verify(original, tail, edited, edited_tail):
    assert tail == edited_tail, 'BIN or other chunks changed'
    for key in ('meshes', 'accessors', 'bufferViews', 'buffers', 'images', 'textures', 'materials'):
        assert original.get(key) == edited.get(key), key + ' changed'
    names = [m.get('name') for m in edited['materials']]
    assert 'Display_37' in names
    assert any(p['material'] == names.index('Display_37') for m in edited['meshes'] for p in m['primitives'])
    before, after = mesh_points(original, tail), mesh_points(edited, edited_tail)
    assert before.keys() == after.keys()
    delta = 0
    for mi, vertices in before.items():
        assert len(vertices) == len(after[mi])
        delta = max(delta, max(abs(a-b) for p, q in zip(vertices, after[mi]) for a, b in zip(p, q)))
        assert max(abs(a-b) for a, b in zip(bounds(vertices), bounds(after[mi]))) < 1e-7, f'mesh {mi} bounds changed'
    assert delta < 1e-7, 'world vertex positions changed'
    wheel_tris = {}
    def subtree(i):
        yield i
        for c in edited['nodes'][i].get('children', []):
            yield from subtree(c)
    for i, n in enumerate(edited['nodes']):
        if n.get('name') not in WHEELS:
            continue
        primitives = [p for ni in subtree(i) if 'mesh' in edited['nodes'][ni]
                      for p in edited['meshes'][edited['nodes'][ni]['mesh']]['primitives']]
        rim = [p for p in primitives if names[p['material']] == 'Rim']
        assert rim and any(edited['nodes'][c].get('name') == n['name']+'_rim_hub' for c in n.get('children', []))
        wheel_tris[n['name']] = sum(edited['accessors'][p['indices']]['count']//3 for p in rim)
    assert len(wheel_tris) == 4
    wm = worlds(edited)
    owners = {ni: i for i, n in enumerate(edited['nodes']) if n.get('name') in WHEELS for ni in subtree(i)}
    wheel_nodes = [i for i, n in enumerate(edited['nodes']) if n.get('name') in WHEELS]
    for ni, n in enumerate(edited['nodes']):
        if 'mesh' not in n:
            continue
        for pr in edited['meshes'][n['mesh']]['primitives']:
            if names[pr['material']] != 'Rim':
                continue
            vertices = [point(wm[ni], v) for v in values(edited, edited_tail[8:], pr['attributes']['POSITION'])]
            bb = bounds(vertices)
            center = tuple((bb[d]+bb[d+3])/2 for d in range(3))
            nearest = min(wheel_nodes, key=lambda i: math.dist(center, point(wm[i], (0, 0, 0))))
            assert owners.get(ni) == nearest, 'rim is not under its nearest wheel'
    total = sum(edited['accessors'][p['indices']]['count']//3 for m in edited['meshes'] for p in m['primitives'])
    return {'triangles': total, 'meshes_verified': len(before), 'max_world_vertex_delta': delta,
            'rim_triangles_by_wheel': wheel_tris, 'Display_37': 'intact', 'BIN': 'identical'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path)
    parser.add_argument('--verify-only', action='store_true')
    args = parser.parse_args()
    DEST.mkdir(parents=True, exist_ok=True)
    for name in MODELS:
        j, tail = read(args.source / name)
        if not args.verify_only:
            write(DEST / name, repair(copy.deepcopy(j)), tail)
        out, out_tail = read(DEST / name)
        print(name + ': ' + json.dumps(verify(j, tail, out, out_tail), sort_keys=True))
    for name in DISPLAYS:
        if not args.verify_only:
            shutil.copyfile(args.source / name, DEST / name)
        assert (args.source / name).read_bytes() == (DEST / name).read_bytes()
        print(name + ': identical')


if __name__ == '__main__':
    main()
