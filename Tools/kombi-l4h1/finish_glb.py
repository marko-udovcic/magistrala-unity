#!/usr/bin/env python3
"""Idempotent cab/sill finishing pass, standard library only. Run after k3_export.py.
Preserves existing meshes, embedded images, node order and buffer offsets; appends a 12-triangle sill.
No Blender/Unity required. Coordinates here are glTF (+Y up, +Z front); glTFast mirrors X.
"""
import argparse
import json
from pathlib import Path
import struct

REVISION = 'magistrala_cab_v2'
DEFAULT = Path(__file__).resolve().parents[2] / 'Assets/Resources/Vehicles/KombiL4H1'


def read(path):
    raw = Path(path).read_bytes()
    magic, version, total = struct.unpack_from('<III', raw)
    assert magic == 0x46546C67 and version == 2 and total == len(raw)
    n, kind = struct.unpack_from('<II', raw, 12)
    assert kind == 0x4E4F534A
    doc = json.loads(raw[20:20+n])
    size, kind = struct.unpack_from('<II', raw, 20+n)
    assert kind == 0x004E4942
    return doc, bytearray(raw[28+n:28+n+size])


def write(path, doc, binary):
    doc['buffers'][0]['byteLength'] = len(binary)
    binary += b'\0' * (-len(binary) % 4)
    text = json.dumps(doc, separators=(',', ':'), ensure_ascii=True).encode()
    text += b' ' * (-len(text) % 4)
    raw = struct.pack('<III', 0x46546C67, 2, 28+len(text)+len(binary))
    raw += struct.pack('<II', len(text), 0x4E4F534A) + text
    raw += struct.pack('<II', len(binary), 0x004E4942) + binary
    Path(path).write_bytes(raw)


def accessor(doc, binary, values, fmt, shape, component, target):
    binary += b'\0' * (-len(binary) % 4)
    offset = len(binary)
    for value in values:
        binary.extend(struct.pack('<'+fmt, *value))
    view = len(doc['bufferViews'])
    doc['bufferViews'].append(dict(buffer=0, byteOffset=offset, byteLength=len(binary)-offset, target=target))
    a = dict(bufferView=view, componentType=component, count=len(values), type=shape)
    if shape == 'VEC3':
        a['min'] = [min(v[i] for v in values) for i in range(3)]
        a['max'] = [max(v[i] for v in values) for i in range(3)]
    index = len(doc['accessors'])
    doc['accessors'].append(a)
    return index


def sill(doc, binary):
    if any(n.get('name') == 'rear_floor_sill' for n in doc['nodes']):
        return
    # Closed solid, flush with floor y=.818; overlap the existing end at -3.402 and reach behind the door seam.
    lo, hi = (-1.07, .778, -3.47), (1.07, .818, -3.36)
    vertices = [(x, y, z) for x in (lo[0], hi[0]) for y in (lo[1], hi[1]) for z in (lo[2], hi[2])]
    faces = [((0, 1, 3, 2), (-1, 0, 0)), ((4, 6, 7, 5), (1, 0, 0)),
             ((0, 4, 5, 1), (0, -1, 0)), ((2, 3, 7, 6), (0, 1, 0)),
             ((0, 2, 6, 4), (0, 0, -1)), ((1, 5, 7, 3), (0, 0, 1))]
    positions, normals, uv, indices = [], [], [], []
    for corners, normal in faces:
        base = len(positions)
        positions.extend(vertices[i] for i in corners)
        normals.extend([normal]*4)
        uv.extend([(0, 0), (1, 0), (1, 1), (0, 1)])
        indices.extend((base+i,) for i in (0, 1, 2, 0, 2, 3))
    attrs = dict(POSITION=accessor(doc, binary, positions, 'fff', 'VEC3', 5126, 34962),
                 NORMAL=accessor(doc, binary, normals, 'fff', 'VEC3', 5126, 34962),
                 TEXCOORD_0=accessor(doc, binary, uv, 'ff', 'VEC2', 5126, 34962))
    idx = accessor(doc, binary, indices, 'H', 'SCALAR', 5123, 34963)
    material = next(i for i, m in enumerate(doc['materials']) if m['name'] == 'Alu_Satin')
    mesh = len(doc['meshes'])
    doc['meshes'].append(dict(name='rear_floor_sill', primitives=[dict(attributes=attrs, indices=idx, material=material, mode=4)]))
    node = len(doc['nodes'])
    doc['nodes'].append(dict(name='rear_floor_sill', mesh=mesh))
    # Same parent as body; preserves any exported model-root transform.
    body = next(i for i, n in enumerate(doc['nodes']) if n.get('name') == 'body')
    parent = next((n for n in doc['nodes'] if body in n.get('children', [])), None)
    if parent is not None:
        parent['children'].append(node)
    else:
        doc['scenes'][doc.get('scene', 0)]['nodes'].append(node)


def finish(path):
    doc, binary = read(path)
    done = doc.setdefault('extras', {}).get(REVISION, False)
    legacy = doc['extras'].pop('magistrala_cab_v1', False)
    if not done or legacy:
        # Absolute anchors also make this safe after the Blender patch, which applies the same cab adjustment.
        eye = next(n for n in doc['nodes'] if n.get('name') == 'a_eye')
        eye['translation'][1:] = [1.84, 1.21]
        steering = next(n for n in doc['nodes'] if n.get('name') == 'steering')
        steering['translation'][1] = 1.418520851135254
        # 10 degrees flatter about the vehicle X axis; use the absolute source quaternion, so no repeated rotation.
        x, y, z, w = [-.0007406004006043077, .928329586982727, .37175291776657104, .0018494039541110396]
        import math
        a, b = math.sin(math.radians(5)), math.cos(math.radians(5))
        steering['rotation'] = [b*x+a*w, b*y-a*z, b*z+a*y, b*w-a*x]
        # Import emissive shader variants for indicators too (off is set at runtime, never lit permanently).
        for m in doc['materials']:
            if m.get('name') == 'Lights_Indicator':
                m['emissiveFactor'] = [.001, .00032, .000005]
            elif m.get('name') == 'Gauges':
                m['emissiveFactor'] = [1.1]*3
        sill(doc, binary)
        doc['extras'][REVISION] = True
        write(path, doc, binary)
    print(f'{path.name}: cab v2, sill present, {len(doc["nodes"])} nodes')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('paths', nargs='*', type=Path)
    args = parser.parse_args()
    for path in args.paths or sorted(DEFAULT.glob('*.glb')):
        finish(path)
