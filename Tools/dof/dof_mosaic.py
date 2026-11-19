"""DGU orthophoto mosaic on the game grid, one image per Unity terrain tile (the ground colour map, see
Assets/Editor/OrthoGround.cs and docs/dof-tlo.md).

  python3 -I Tools/dof/dof_mosaic.py [--src lidar|dof2526] -- TX,TZ [TX,TZ ..]       (PYTHONPATH: numpy, Pillow, pyproj)
  python3 -I Tools/dof/dof_mosaic.py --src lidar --x0 X0 --x1 X1 --z0 Z0 --z1 Z1  (every tile of a game-x/z box)

Sources:
  lidar    (default) DOF LiDAR 2022./23., 25 cm native, the registered (authKey) WMS — no watermark. The key is read at run time
           from BalkanDriver/tools/world/dgu/cache/authkey.txt (gitignored, 600) and never written anywhere: cache file names
           are the SHA-256 of the URL WITHOUT the key, the .meta.json holds that keyless URL. (The anonymous LiDAR layer is
           watermarked: never used.)
  dof2526  DOF 2025./26., anonymous WMS; reuses the responses tools/world/ground/ortho.py cached (cache/ground/).
Fetching: 256 m EPSG:3765 squares (on a 256 m TM grid, 12 m margin, 0.5 m/px — the colour map needs no more) into
cache/dof/wms-<src>/, serially, >= 2 s apart, never fetched twice.

Out: BalkanDriver/tools/world/dgu/cache/dof/mosaic-<src>/t_TX_TZ.png (2048², 0.5 m/px, north up: pixel (row j, col i) centre =
game (TX*1024 + (i + .5) * .5, TZ*1024 + (j + .5) * .5); alpha 0 = no photo) — raw-derived, stays in the gitignored cache.
Game frame = AEQD (lat 43.5083, lon 16.44, WGS84), x = east, z = -north (tools/world/ground/ortho.py); over one 1 km tile the
AEQD -> HTRS96/TM map is affine to < 1 mm, so the mosaic uses a least-squares affine fit (residual printed).
"""
import glob, hashlib, json, re, subprocess, sys, time
from datetime import datetime, timezone
from io import BytesIO
from pathlib import Path

import numpy as np
from PIL import Image
from pyproj import Transformer

BD = Path('/Users/macair/Documents/BalkanDriver')
CACHE = BD / 'tools/world/dgu/cache'
SRC = {
    'lidar': ('https://geoportal.dgu.hr/services/auth/inspire/orthophoto_lidar_2022_2023/wms', True),
    'dof2526': ('https://geoportal.dgu.hr/services/inspire/orthophoto_2025_2026/wms', False),
}
src = 'lidar'
AEQD = '+proj=aeqd +lat_0=43.5083 +lon_0=16.4400 +datum=WGS84 +units=m'
PX, N = 0.5, 2048
T = Transformer.from_crs(AEQD, 3765, always_xy=True)


def wmsdir():
    return CACHE / f'dof/wms-{src}'


def sources():
    out = []
    old = sorted(glob.glob(str(CACHE / 'ground/*.meta.json'))) if src == 'dof2526' else []
    for f in old + sorted(glob.glob(str(wmsdir() / '*.meta.json'))):
        meta = json.load(open(f))
        m = re.search(r'bbox=([\d.,]+)&width=(\d+)&height=(\d+)', meta['url'])
        if not meta['url'].startswith(SRC[src][0]) or 'authKey' in meta['url'] or not m:
            continue
        b = [float(v) for v in m.group(1).split(',')]
        img = Path(f.replace('.meta.json', '.response' if '/ground/' in f else '.jpg'))
        if img.exists():
            out.append((b, int(m.group(2)), int(m.group(3)), img))
    return out


def fetch(e0, n0, e1, n1):
    w, h = int(round((e1 - e0) / PX)), int(round((n1 - n0) / PX))
    url = (f'{SRC[src][0]}?service=WMS&version=1.3.0&request=GetMap&layers=OI.OrthoimageCoverage&styles='
           f'&crs=EPSG:3765&bbox={e0:.1f},{n0:.1f},{e1:.1f},{n1:.1f}&width={w}&height={h}&format=image/jpeg')
    d = wmsdir(); d.mkdir(parents=True, exist_ok=True)
    p = d / (hashlib.sha256(url.encode()).hexdigest() + '.jpg')
    if p.exists():
        return
    tmp = p.with_suffix('.part')
    full = url
    if SRC[src][1]:
        key = (CACHE / 'authkey.txt').read_text().strip()
        full = url + '&authKey=' + key
    # the URL goes to curl on stdin (a config line), so the key is not on the command line / in ps either
    r = subprocess.run(['curl', '--fail', '--location', '--silent', '--show-error', '--max-time', '120',
                        '--user-agent', 'Magistrala-dof-ground/1.0', '--config', '-', '-o', str(tmp)],
                       input=f'url = "{full}"\n', capture_output=True, text=True)
    time.sleep(2.2)
    if r.returncode:
        raise RuntimeError(r.stderr.replace(full, url))
    if Image.open(tmp).format != 'JPEG':
        raise RuntimeError('not a JPEG: ' + tmp.read_bytes()[:300].decode('latin1'))
    Image.open(tmp).verify()
    tmp.replace(p)
    p.with_suffix('.meta.json').write_text(json.dumps({'url': url, 'fetchedAt': datetime.now(timezone.utc).isoformat(),
                                                       'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}, indent=1))
    print('fetched', url)


def affine(tx, tz):
    g = np.linspace(0, 1024, 9)
    gx, gz = np.meshgrid(tx * 1024 + g, tz * 1024 + g)
    e, n = T.transform(gx.ravel(), -gz.ravel())
    A = np.c_[gx.ravel(), gz.ravel(), np.ones(gx.size)]
    ce, *_ = np.linalg.lstsq(A, e, rcond=None)
    cn, *_ = np.linalg.lstsq(A, n, rcond=None)
    res = max(np.abs(A @ ce - e).max(), np.abs(A @ cn - n).max())
    return ce, cn, res


def mosaic(tx, tz, allow_fetch=True):
    ce, cn, res = affine(tx, tz)
    c = (np.arange(N) + .5) * PX
    gx, gz = np.meshgrid(tx * 1024 + c, tz * 1024 + c)
    E = ce[0] * gx + ce[1] * gz + ce[2]
    Nn = cn[0] * gx + cn[1] * gz + cn[2]
    rgb = np.zeros((N, N, 3), np.float32); have = np.zeros((N, N), bool); best = np.full((N, N), -1.0, np.float32)
    for (e0, n0, e1, n1), w, h, img in sources():
        if e1 < E.min() or e0 > E.max() or n1 < Nn.min() or n0 > Nn.max():
            continue
        dx, dy = (e1 - e0) / w, (n1 - n0) / h
        col = (E - e0) / dx - .5; row = (n1 - Nn) / dy - .5
        inside = (col >= 0) & (row >= 0) & (col <= w - 1.001) & (row <= h - 1.001)
        # prefer the image whose edge is farthest (no seam pixels from the JPEG border)
        edge = np.minimum(np.minimum(col, w - 1 - col), np.minimum(row, h - 1 - row))
        take = inside & (edge > best)
        if not take.any():
            continue
        a = np.asarray(Image.open(img).convert('RGB')).astype(np.float32)
        r, q = row[take], col[take]; r0, q0 = np.floor(r).astype(int), np.floor(q).astype(int); fr, fq = (r - r0)[:, None], (q - q0)[:, None]
        v = (a[r0, q0] * (1 - fq) + a[r0, q0 + 1] * fq) * (1 - fr) + (a[r0 + 1, q0] * (1 - fq) + a[r0 + 1, q0 + 1] * fq) * fr
        rgb[take] = v; have[take] = True; best[take] = edge[take]
    if allow_fetch and not have.all():
        # missing: fetch 256 m EPSG:3765 squares (on a 256 m TM grid, 12 m margin) covering them
        miss = ~have
        cells = set(zip((np.floor(E[miss] / 256)).astype(int).tolist(), (np.floor(Nn[miss] / 256)).astype(int).tolist()))
        print(f'tile {tx},{tz}: {miss.mean() * 100:.1f} % not cached, {len(cells)} requests')
        for i, j in sorted(cells):
            fetch(i * 256 - 12, j * 256 - 12, i * 256 + 268, j * 256 + 268)
        return mosaic(tx, tz, False)
    OUT = CACHE / f'dof/mosaic-{src}'
    OUT.mkdir(parents=True, exist_ok=True)
    out = np.dstack([np.clip(rgb + .5, 0, 255).astype(np.uint8), (have * 255).astype(np.uint8)])
    Image.fromarray(out, 'RGBA').save(OUT / f't_{tx}_{tz}.png')
    print(f'tile {tx},{tz}: affine residual {res * 1000:.3f} mm, covered {have.mean() * 100:.1f} % -> {OUT}/t_{tx}_{tz}.png')


if __name__ == '__main__':
    import argparse
    ap = argparse.ArgumentParser()
    ap.add_argument('--src', default='lidar', choices=sorted(SRC))
    ap.add_argument('--x0', type=float); ap.add_argument('--x1', type=float); ap.add_argument('--z0', type=float); ap.add_argument('--z1', type=float)
    ap.add_argument('tiles', nargs='*')
    a = ap.parse_args()
    src = a.src
    tiles = [tuple(map(int, t.split(','))) for t in a.tiles]
    if a.x0 is not None:
        tiles += [(tx, tz) for tz in range(int(np.floor(a.z0 / 1024)), int(np.floor(a.z1 / 1024)) + 1)
                  for tx in range(int(np.floor(a.x0 / 1024)), int(np.floor(a.x1 / 1024)) + 1)]
    for tx, tz in tiles:
        mosaic(tx, tz)
