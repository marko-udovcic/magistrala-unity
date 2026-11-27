#!/usr/bin/env python3
"""Compare generated BalkanDriver data with the pre-task snapshot; permit only 41/42."""
import hashlib, json, pathlib, sys
root = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else '/Users/macair/Documents/BalkanDriver')
baseline = json.loads(pathlib.Path(__file__).with_name('before-hashes.json').read_text())
allowed = {
 'client/public/assets/models/krizanja/41-kastel-stafilic-C.glb',
 'client/public/assets/models/krizanja/42-kastel-stafilic-C.glb',
 'client/public/assets/world/split-kastela/model-junctions.json',
 'client/public/assets/world/split-kastela/junction-overrides-modeli.json',
}
sha = lambda b: hashlib.sha256(b).hexdigest()
changed = [f for f, h in baseline['files'].items() if sha((root/f).read_bytes()) != h]
assert not set(changed)-allowed, f'Unrelated files changed: {set(changed)-allowed}'
print(f"File hashes: {len(baseline['files'])-len(changed)} unchanged, {len(changed)} permitted changes")
for f, hashes in baseline['entries'].items():
 field = 'junctions' if f == 'model-junctions.json' else 'overrides'
 current = json.loads((root/'client/public/assets/world/split-kastela'/f).read_text())[field]
 now = {x.get('key', x.get('id')): sha(json.dumps(x, sort_keys=True, separators=(',', ':')).encode()) for x in current}
 assert now.keys() == hashes.keys(), f'{f}: entries added/removed'
 diff = [key for key in hashes if hashes[key] != now[key]]
 assert all(key.split('#model:')[-1] in ('krizanje-41','krizanje-42') for key in diff), (f, diff)
 print(f'{f}: {len(hashes)-len(diff)} unchanged entries; changed: {", ".join(diff)}')
print('PASS: all unrelated models, 14/47 and Marko overrides preserved')
