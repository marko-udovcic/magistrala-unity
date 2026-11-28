#!/usr/bin/env python3
"""Idempotent cab/sill finishing pass, standard library only. Run after k3_export.py.
Preserves existing meshes, embedded images, node order and buffer offsets; appends a 12-triangle sill, the rear swing
doors' white skin (outer + inner quad per door, a third primitive) and an inward-facing box liner (box_liner).
No Blender/Unity required. Coordinates here are glTF (+Y up, +Z front); glTFast mirrors X.
"""
import argparse
import json
from pathlib import Path
import struct

REVISION = 'magistrala_cab_v2'
SKIN = 'magistrala_door_skin_v1'
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


def values(doc, binary, index):
    a = doc['accessors'][index]
    view = doc['bufferViews'][a['bufferView']]
    fmt = {5126: 'f', 5123: 'H', 5125: 'I', 5121: 'B'}[a['componentType']]
    k = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
    size = struct.calcsize(fmt) * k
    stride, offset = view.get('byteStride', size), view.get('byteOffset', 0) + a.get('byteOffset', 0)
    return [struct.unpack_from('<' + fmt * k, binary, offset + stride * i) for i in range(a['count'])]


def primitive(doc, binary, quads_or_tris, material):
    """quads_or_tris: list of (corner positions, normal); 4 corners are a CCW quad seen from the normal side"""
    positions, normals, uv, indices = [], [], [], []
    for corners, normal in quads_or_tris:
        base = len(positions)
        positions.extend(corners)
        normals.extend([normal] * len(corners))
        uv.extend([(0, 0), (1, 0), (1, 1), (0, 1)][:len(corners)])
        tri = (0, 1, 2, 0, 2, 3) if len(corners) == 4 else (0, 1, 2)
        indices.extend((base + i,) for i in tri)
    attrs = dict(POSITION=accessor(doc, binary, positions, 'fff', 'VEC3', 5126, 34962),
                 NORMAL=accessor(doc, binary, normals, 'fff', 'VEC3', 5126, 34962),
                 TEXCOORD_0=accessor(doc, binary, uv, 'ff', 'VEC2', 5126, 34962))
    return dict(attributes=attrs, indices=accessor(doc, binary, indices, 'H', 'SCALAR', 5123, 34963), material=material, mode=4)


def door_skins(doc, binary):
    """The source swing doors (furgon003/004) are only rims; their skin was the isoBox rear wall, which k2_build.diff removes
    so the box can open. Add the white outer and inner skin to each door mesh (it swings with the door, in every LOD)."""
    white = next(i for i, m in enumerate(doc['materials']) if m['name'] == 'Box_White')
    for node in doc['nodes']:
        if node.get('name') not in ('door_rear_L', 'door_rear_R'):
            continue
        mesh = doc['meshes'][node['mesh']]
        if mesh.setdefault('extras', {}).get('magistrala_door_skin'):
            continue
        rim = next(p for p in mesh['primitives'] if p.get('material') == white)
        a = doc['accessors'][rim['attributes']['POSITION']]
        # the skin fills the rim's opening, 3 cm under its 3.4 cm lip, just behind the outer lip / in front of the inner edge
        (x0, y0, z0), (x1, y1, z1), d = a['min'], a['max'], .03
        x0, x1, y0, y1 = x0 + d, x1 - d, y0 + d, y1 - d
        zo, zi = z0 + .0025, z1 - .0025
        outer = [(x0, y0, zo), (x0, y1, zo), (x1, y1, zo), (x1, y0, zo)]   # faces -Z (glTF rear, out of the box)
        inner = [(x0, y0, zi), (x1, y0, zi), (x1, y1, zi), (x0, y1, zi)]   # faces +Z (into the box)
        mesh['primitives'].append(primitive(doc, binary, [(outer, (0, 0, -1)), (inner, (0, 0, 1))], white))
        mesh['extras']['magistrala_door_skin'] = True


def box_liner(doc, binary):
    """The isoBox walls face outwards only (floor down, roof up): from behind with the doors open the box looked hollow.
    A flipped copy of the body's Box_White faces lines the inside (floor, side walls, roof, front)."""
    if any(n.get('name') == 'box_liner' for n in doc['nodes']):
        return
    white = next(i for i, m in enumerate(doc['materials']) if m['name'] == 'Box_White')
    body = next(i for i, n in enumerate(doc['nodes']) if n.get('name') == 'body')
    src = next(p for p in doc['meshes'][doc['nodes'][body]['mesh']]['primitives'] if p.get('material') == white)
    ps = values(doc, binary, src['attributes']['POSITION'])
    ns = values(doc, binary, src['attributes']['NORMAL'])
    ix = [v[0] for v in values(doc, binary, src['indices'])]
    tris = [([ps[ix[i]], ps[ix[i + 2]], ps[ix[i + 1]]], tuple(-c for c in ns[ix[i]])) for i in range(0, len(ix), 3)]
    mesh = len(doc['meshes'])
    doc['meshes'].append(dict(name='box_liner', primitives=[primitive(doc, binary, tris, white)]))
    node = len(doc['nodes'])
    doc['nodes'].append(dict(name='box_liner', mesh=mesh))
    parent = next((n for n in doc['nodes'] if body in n.get('children', [])), None)
    if parent is not None:
        parent['children'].append(node)
    else:
        doc['scenes'][doc.get('scene', 0)]['nodes'].append(node)


def finish(path):
    doc, binary = read(path)
    done = doc.setdefault('extras', {}).get(REVISION, False)
    legacy = doc['extras'].pop('magistrala_cab_v1', False)
    if not doc['extras'].get(SKIN):
        door_skins(doc, binary)
        box_liner(doc, binary)
        doc['extras'][SKIN] = True
        if done and not legacy:
            write(path, doc, binary)
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
    print(f'{path.name}: cab v2, sill, door skins, box liner present, {len(doc["nodes"])} nodes')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('paths', nargs='*', type=Path)
    args = parser.parse_args()
    for path in args.paths or sorted(DEFAULT.glob('*.glb')):
        finish(path)
