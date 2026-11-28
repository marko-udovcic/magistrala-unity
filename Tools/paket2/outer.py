import tarfile, sys, shutil
src, guid, out = sys.argv[1:4]
with tarfile.open(src, 'r|*') as t:
    for m in t:
        if m.name.rstrip('/') in (guid + '/asset', './' + guid + '/asset'):
            f = t.extractfile(m)
            with open(out, 'wb') as o: shutil.copyfileobj(f, o, 1 << 22)
            print('ok', m.size); break
