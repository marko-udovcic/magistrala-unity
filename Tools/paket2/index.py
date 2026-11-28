import tarfile, sys, json, os
src, out = sys.argv[1:3]
idx = {}  # guid -> {path, size, yaml(bool)}
os.makedirs(out + '/yaml', exist_ok=True)
with tarfile.open(src, 'r|*') as t:
    for m in t:
        n = m.name.lstrip('./')
        parts = n.split('/')
        if len(parts) != 2: continue
        g, k = parts
        e = idx.setdefault(g, {})
        f = None
        if k == 'pathname':
            e['path'] = t.extractfile(m).read().decode('utf8').split('\n')[0]
        elif k == 'asset':
            e['size'] = m.size
            if m.size < 3_000_000:
                d = t.extractfile(m).read()
                if d[:5] == b'%YAML': open(out + '/yaml/' + g, 'wb').write(d)
json.dump(idx, open(out + '/index.json', 'w'), indent=0)
print(len(idx))
