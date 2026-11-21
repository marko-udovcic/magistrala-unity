"""Vegetation mask of all Kaštela (the WorldBuilder core tiles) for Build trees (Assets/Editor/VegetationMask.cs, docs/vegetacija-d8.md).

  BalkanDriver/tools/world/dgu/cache/venv/bin/python -I Tools/vegetacija/maska.py      (numpy, Pillow, scipy, shapely, pyproj, rasterio)

In:  the DGU LiDAR orthophoto mosaics (BalkanDriver/tools/world/dgu/cache/dof/mosaic-lidar/t_TX_TZ.png, 0.5 m/px, Tools/dof/dof_mosaic.py),
     the cached OSM Overpass answers of tools/world/ground/build.py (landuse=orchard / residential), the D8 from graph.json.
Out: Assets/Editor/vegetacija-maska.png — 2560 × 768 grey, 4 m cells, game x −10240…0, z −7168…−4096, row 0 = smallest z (north).
     A byte per cell:  bits 0–3 dense woody cover 0…15 inside a clump (0 = not a clump)
                       bit 4 olive grove (OSM landuse=orchard)   bit 5 built-up (OSM landuse=residential)
                       bit 6 0–8 m from the D8 asphalt edge      bit 7 0–100 m from the D8 edge
Clumps: per 0.5 m pixel "dark woody" = green-dominant (G−R ≥ 2, G−B ≥ 2) and 2.5 m-blurred brightness < 84 (closed maquis, holm oak,
pine, dense olive; grass / fields / gardens are brighter), shadowed pixels between crowns filled; woody share per 2 m cell, 10 m
smoothed share ≥ .42 = clump, clumps smaller than 800 m² dropped (single garden trees are not clumps). Only a derived mask, no imagery.
"""
import json, math, sys
from pathlib import Path

import numpy as np
from PIL import Image
from scipy import ndimage as ndi
from shapely.geometry import LineString, Polygon, MultiPolygon
from shapely.ops import unary_union
from shapely.validation import make_valid
from pyproj import Transformer
from rasterio import features as rf
from rasterio.transform import Affine

BD = Path('/Users/macair/Documents/BalkanDriver')
MOS = BD / 'tools/world/dgu/cache/dof/mosaic-lidar'
OSM = [BD / 'tools/world/dgu/cache/ground' / (h + '.response') for h in
       ('ab4d926d6ab811d70716d73efc6a9e70f0b30b0e774328a137f8806ea5966b64', '93d81ade70af62ec009347f97a35d77985a976ecbe72179111cc4eb2f8128a89')]
GRAPH = BD / 'client/public/assets/world/split-kastela/graph.json'
OUT = Path(__file__).resolve().parents[2] / 'Assets/Editor/vegetacija-maska.png'
TX0, TX1, TZ0, TZ1 = -10, -1, -7, -5
X0, Z0 = TX0 * 1024, TZ0 * 1024
CELL = 4.0
NX, NZ = int((TX1 - TX0 + 1) * 1024 / CELL), int((TZ1 - TZ0 + 1) * 1024 / CELL)
AEQD = '+proj=aeqd +lat_0=43.5083 +lon_0=16.4400 +datum=WGS84 +units=m'
LL = Transformer.from_crs(4326, AEQD, always_xy=True)


def boxmean(m, k):
    m = m.astype(np.float32); p = k // 2
    pad = np.pad(m, p, mode='edge'); I = np.cumsum(np.cumsum(pad, 0), 1); I = np.pad(I, ((1, 0), (1, 0)))
    h, w = m.shape
    return (I[k:k + h, k:k + w] - I[:h, k:k + w] - I[k:k + h, :w] + I[:h, :w]) / (k * k)


def woody(rgba):
    a = rgba[..., :3].astype(np.float32); R, G, B = a[..., 0], a[..., 1], a[..., 2]
    V = a.mean(2); Vb = boxmean(V, 5)
    w = (G - R >= 2) & (G - B >= 2) & (Vb < 84)
    w |= (V < 70) & (boxmean(w, 9) > .4)         # shadows between crowns inside a canopy
    w |= (Vb < 58) & (B < G + 10) & (B < R + 14)  # deep forest shade (too dark to read green; the sea is blue, not this)
    w &= rgba[..., 3] > 0
    return w


def main():
    # woody share per 2 m cell over the whole core
    n2x, n2z = NX * 2, NZ * 2
    share = np.zeros((n2z, n2x), np.float32)
    for tx in range(TX0, TX1 + 1):
        for tz in range(TZ0, TZ1 + 1):
            p = MOS / f't_{tx}_{tz}.png'
            if not p.exists(): print('missing', p); continue
            w = woody(np.asarray(Image.open(p).convert('RGBA'))).astype(np.float32)
            s = w.reshape(512, 4, 512, 4).mean((1, 3))
            i0, j0 = (tx - TX0) * 512, (tz - TZ0) * 512
            share[j0:j0 + 512, i0:i0 + 512] = s
    sm = boxmean(share, 5)
    clump = sm >= .42
    lab, n = ndi.label(clump)
    size = ndi.sum(clump, lab, np.arange(1, n + 1)) * 4.0     # m²
    keep = np.zeros(n + 1, bool); keep[1:] = size >= 800
    clump = keep[lab]
    clump = ndi.binary_closing(clump, iterations=1) & (sm >= .3)
    cov4 = (share * clump).reshape(NZ, 2, NX, 2).mean((1, 3))
    c4 = clump.reshape(NZ, 2, NX, 2).mean((1, 3)) >= .5
    q = np.where(c4, np.clip(np.round(cov4 * 15), 1, 15), 0).astype(np.uint8)

    # OSM landuse
    T = Affine(CELL, 0, X0, 0, CELL, Z0)
    orch, resi = [], []
    seen = set()
    for f in OSM:
        if not f.exists(): print('no OSM cache', f); continue
        for e in json.loads(f.read_text())['elements']:
            t = e.get('tags', {}); lu = t.get('landuse')
            if lu not in ('orchard', 'residential') or (e['type'], e['id']) in seen: continue
            seen.add((e['type'], e['id']))
            def ring(geom): return [(lambda x, y: (x, -y))(*LL.transform(p['lon'], p['lat'])) for p in geom]
            g = None
            if e['type'] == 'way' and e.get('geometry') and len(e['geometry']) >= 4:
                g = make_valid(Polygon(ring(e['geometry'])))
            elif e['type'] == 'relation':
                outer = [Polygon(ring(m['geometry'])) for m in e.get('members', []) if m.get('role') == 'outer' and m.get('geometry') and len(m['geometry']) >= 4]
                inner = [Polygon(ring(m['geometry'])) for m in e.get('members', []) if m.get('role') == 'inner' and m.get('geometry') and len(m['geometry']) >= 4]
                if outer:
                    g = make_valid(unary_union([make_valid(o) for o in outer]))
                    if inner: g = g.difference(unary_union([make_valid(i) for i in inner]))
            if g is None or g.is_empty: continue
            (orch if lu == 'orchard' else resi).append(g)
    def rast(gs): return rf.rasterize([(g, 1) for g in gs], out_shape=(NZ, NX), transform=T, fill=0, dtype='uint8').astype(bool) if gs else np.zeros((NZ, NX), bool)
    orchard, built = rast(orch), rast(resi)

    # D8: distance from the asphalt edge
    g = json.loads(GRAPH.read_text())
    lines = []
    for e in g['edges']:
        if e.get('ref') != 'D8' or not any(X0 - 300 < p[0] < 300 and Z0 - 300 < p[2] < Z0 + NZ * CELL + 300 for p in e['points']): continue
        lines.append(LineString([(p[0], p[2]) for p in e['points']]).buffer(e.get('width', 10) / 2, cap_style=2))
    road = rast(lines)
    d = ndi.distance_transform_edt(~road) * CELL - CELL / 2
    clear, corr = (d <= 8) & ~road, d <= 100

    m = q | (orchard.astype(np.uint8) << 4) | (built.astype(np.uint8) << 5) | ((clear | road).astype(np.uint8) << 6) | (corr.astype(np.uint8) << 7)
    Image.fromarray(m, 'L').save(OUT, optimize=True)
    ha = CELL * CELL / 1e4
    print(f'clumps {c4.sum() * ha:.0f} ha ({n} raw components), orchards {orchard.sum() * ha:.0f} ha ({len(orch)}), built {built.sum() * ha:.0f} ha ({len(resi)}), '
          f'D8 corridor {corr.sum() * ha:.0f} ha, clumps in corridor {(c4 & corr).sum() * ha:.0f} ha; {OUT} {OUT.stat().st_size // 1024} kB')


if __name__ == '__main__':
    main()
