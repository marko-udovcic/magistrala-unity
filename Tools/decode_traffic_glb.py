#!/usr/bin/env python3
"""Web traffic car GLBs (EXT_meshopt_compression + EXT_texture_webp) -> plain GLBs that glTFast imports without extra packages.
Meshopt is decoded with Blender 5.2's bundled libmeshoptimizer (ctypes), WebP images are converted to PNG with macOS `sips`.
usage: decode_traffic_glb.py <src dir> <dst dir> name [name ...]"""
import ctypes, json, os, struct, subprocess, sys, tempfile

LIB = '/Users/macair/Applications/Blender.app/Contents/Resources/lib/libmeshoptimizer.dylib'
lib = ctypes.CDLL(LIB)
for f in (lib.meshopt_decodeVertexBuffer, lib.meshopt_decodeIndexBuffer, lib.meshopt_decodeIndexSequence):
    f.restype = ctypes.c_int
    f.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_size_t, ctypes.POINTER(ctypes.c_ubyte), ctypes.c_size_t]
for f in (lib.meshopt_decodeFilterOct, lib.meshopt_decodeFilterQuat, lib.meshopt_decodeFilterExp):
    f.restype = None
    f.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_size_t]


def decode(src, dst):
    b = open(src, 'rb').read()
    jl = struct.unpack('<I', b[12:16])[0]
    j = json.loads(b[20:20 + jl])
    o = 20 + jl
    bl = struct.unpack('<I', b[o:o + 4])[0]
    binb = b[o + 8:o + 8 + bl]
    out = bytearray()
    for bv in j['bufferViews']:
        ext = (bv.get('extensions') or {}).get('EXT_meshopt_compression')
        if ext:
            data = binb[ext.get('byteOffset', 0):ext.get('byteOffset', 0) + ext['byteLength']]
            count, stride = ext['count'], ext['byteStride']
            dstb = bytearray(count * stride)
            dp = (ctypes.c_ubyte * len(dstb)).from_buffer(dstb)
            sp = (ctypes.c_ubyte * len(data)).from_buffer_copy(data)
            fn = {'ATTRIBUTES': lib.meshopt_decodeVertexBuffer, 'TRIANGLES': lib.meshopt_decodeIndexBuffer, 'INDICES': lib.meshopt_decodeIndexSequence}[ext['mode']]
            if fn(dp, count, stride, sp, len(data)) != 0: raise RuntimeError('meshopt decode failed ' + src)
            flt = ext.get('filter')
            if ext['mode'] == 'ATTRIBUTES' and flt and flt != 'NONE':
                {'OCTAHEDRAL': lib.meshopt_decodeFilterOct, 'QUATERNION': lib.meshopt_decodeFilterQuat, 'EXPONENTIAL': lib.meshopt_decodeFilterExp}[flt](dp, count, stride)
            data = bytes(dstb)
            del bv['extensions']['EXT_meshopt_compression']
            if not bv['extensions']: del bv['extensions']
        else:
            data = binb[bv.get('byteOffset', 0):bv.get('byteOffset', 0) + bv['byteLength']]
        while len(out) % 4: out.append(0)
        bv['buffer'] = 0; bv['byteOffset'] = len(out); bv['byteLength'] = len(data)
        out += data
    # webp images -> png
    for im in j.get('images', []):
        if im.get('mimeType') == 'image/webp':
            bv = j['bufferViews'][im['bufferView']]
            with tempfile.TemporaryDirectory() as td:
                wp, pp = os.path.join(td, 'a.webp'), os.path.join(td, 'a.png')
                open(wp, 'wb').write(out[bv['byteOffset']:bv['byteOffset'] + bv['byteLength']])
                subprocess.run(['sips', '-s', 'format', 'png', wp, '--out', pp], check=True, capture_output=True)
                png = open(pp, 'rb').read()
            while len(out) % 4: out.append(0)
            bv['byteOffset'] = len(out); bv['byteLength'] = len(png); out += png
            im['mimeType'] = 'image/png'
    for t in j.get('textures', []):
        e = (t.get('extensions') or {}).pop('EXT_texture_webp', None)
        if e is not None: t['source'] = e['source']
        if 'extensions' in t and not t['extensions']: del t['extensions']
    while len(out) % 4: out.append(0)
    j['buffers'] = [{'byteLength': len(out)}]
    for k in ('extensionsUsed', 'extensionsRequired'):
        if k in j:
            j[k] = [e for e in j[k] if e not in ('EXT_meshopt_compression', 'EXT_texture_webp')]
            if not j[k]: del j[k]
    js = json.dumps(j, separators=(',', ':')).encode()
    while len(js) % 4: js += b' '
    total = 12 + 8 + len(js) + 8 + len(out)
    with open(dst, 'wb') as f:
        f.write(struct.pack('<III', 0x46546C67, 2, total))
        f.write(struct.pack('<II', len(js), 0x4E4F534A)); f.write(js)
        f.write(struct.pack('<II', len(out), 0x004E4942)); f.write(out)


if __name__ == '__main__':
    s, d = sys.argv[1], sys.argv[2]
    os.makedirs(d, exist_ok=True)
    for n in sys.argv[3:]:
        decode(os.path.join(s, n + '.glb'), os.path.join(d, n + '.glb'))
        print(n, os.path.getsize(os.path.join(d, n + '.glb')))
