# extract roots (paths) + their guid closure from a unitypackage into dst/<path with "(URP)" removed>
import tarfile, sys, json, re, os
src, idxf, rootsf, dst = sys.argv[1:5]
idx = json.load(open(idxf)); ydir = os.path.dirname(idxf) + '/yaml/'
bypath = {v['path']: g for g, v in idx.items() if 'path' in v}
roots = [l.strip() for l in open(rootsf) if l.strip()]
need = set(); todo = []
for r in roots:
    g = bypath.get(r)
    if not g: print('MISSING', r); continue
    todo.append(g)
rx = re.compile(rb'guid: ([0-9a-f]{32})')
while todo:
    g = todo.pop()
    if g in need: continue
    need.add(g)
    p = ydir + g
    if os.path.exists(p):
        for m in rx.findall(open(p, 'rb').read()):
            m = m.decode()
            if m in idx and 'path' in idx[m] and m not in need: todo.append(m)
# metas need a pass: read them in the stream
total = 0; n = 0
with tarfile.open(src, 'r|*') as t:
    for m in t:
        parts = m.name.lstrip('./').split('/')
        if len(parts) != 2 or parts[0] not in need: continue
        g, k = parts; path = idx[g]['path'].replace('Assets/OdesaUkraine (URP)', 'Assets/OdesaUkraine')
        if k == 'asset':
            out = dst + '/' + path; os.makedirs(os.path.dirname(out), exist_ok=True)
            with open(out, 'wb') as o: o.write(t.extractfile(m).read())
            total += m.size; n += 1
        elif k == 'asset.meta':
            out = dst + '/' + path + '.meta'; os.makedirs(os.path.dirname(out), exist_ok=True)
            d = t.extractfile(m).read()
            with open(out, 'wb') as o: o.write(d)
            for r in rx.findall(d):
                r = r.decode()
                if r in idx and 'path' in idx[r] and r not in need: print('META-REF (not followed)', idx[r]['path'])
print('files', n, 'MB', total / 1e6)
