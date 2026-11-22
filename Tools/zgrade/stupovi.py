#!/usr/bin/env python3
"""Read-only tile audit. Run from anywhere: ZGRADE_PYLIB=... python3 -I Tools/zgrade/stupovi.py

Requires Shapely >= 2 (normal installation or explicit ZGRADE_PYLIB pip target).
Reads the same type-4 BUILDINGS records/range as Tiles.Get / BuildingsFromTiles.Build.
CSV contains flagged tile footprints, NOT proposed deletions. Heights are eaves before
annex splitting, including BuildingFloors precedence and house jitter. Roof rise and
terrain foundation exposure are not height. Never writes to the source region.

Road-clearance acceptance of orthophoto shifts requires Unity's StreetsBuilder. We
therefore evaluate BOTH accepted/rejected shift positions for affected buildings and
print uncertainty, rather than silently claiming an exact recreation. Added orthophoto
houses have no tile ID: counted separately, not assigned invented tx_tz:i IDs.
"""
import argparse
import collections
import csv
import json
import math
import os
from pathlib import Path
import re
import struct
import sys

if os.environ.get('ZGRADE_PYLIB'):
    sys.path.insert(0, os.environ['ZGRADE_PYLIB'])
try:
    from shapely.geometry import Polygon, Point, shape
    from shapely.ops import transform
    from shapely.strtree import STRtree
    from shapely.validation import explain_validity
except ImportError:
    raise SystemExit('Requires Shapely >= 2; install separately or set ZGRADE_PYLIB.')

ROOT = Path(__file__).resolve().parents[2]
CS = (ROOT / 'Assets/Editor/BuildingsFromTiles.cs').read_text()
W = (ROOT / 'Assets/Editor/WorldData.cs').read_text()
WB = (ROOT / 'Assets/Editor/WorldBuilder.cs').read_text()
BD = Path(re.search(r'const string BD = "([^"]+)"', W)[1])
REGION = BD / 'client/public/assets/world/split-kastela'
C = {k: float(v) for k, v in re.findall(r'(SHAPE_\w+)\s*=\s*([\d.]+)', CS)}
RULES = ('width<2.5', 'area<10', 'height/width>2.5', 'sliver', 'invalid', 'degenerate', 'overlap>=90%')


def read_json(path):
    return json.loads(path.read_text())


def buildings(tx, tz):
    path = REGION / f'tiles/{tx}_{tz}.bin'
    if not path.exists():
        return []
    data = path.read_bytes()
    offset, result = 16, []
    while offset + 8 <= len(data):
        kind, size = struct.unpack_from('<ii', data, offset)
        offset += 8
        if size < 0 or offset + size > len(data):
            raise ValueError(f'Truncated section: {path}')
        if kind == 4:
            result = json.loads(data[offset:offset + size])
        offset += size
    return result


def clean(points):
    p = [tuple(q) for q in points]
    if len(p) > 1 and p[0] == p[-1]:
        p.pop()
    return p


def size_of(poly):
    # Match ShapeSize, including equal-area rectangle ties; global-coordinate GEOS
    # calipers can choose another width for triangles with multiple optimal rectangles.
    points = sorted(set(poly.exterior.coords))
    if len(points) < 3:
        return 0., poly.length
    def cross(a, b, c):
        return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0])
    hull = []
    for p in points:
        while len(hull) >= 2 and cross(hull[-2], hull[-1], p) <= 0:
            hull.pop()
        hull.append(p)
    lower = len(hull)
    for p in points[-2::-1]:
        while len(hull) > lower and cross(hull[-2], hull[-1], p) <= 0:
            hull.pop()
        hull.append(p)
    hull.pop()
    best, width, length = math.inf, 0., 0.
    for a, b in zip(hull, hull[1:] + hull[:1]):
        dx, dz = b[0] - a[0], b[1] - a[1]
        edge = math.sqrt(dx * dx + dz * dz)
        if edge < 1e-9:
            continue
        dx, dz = dx / edge, dz / edge
        us = [(p[0] - a[0]) * dx + (p[1] - a[1]) * dz for p in hull]
        vs = [-(p[0] - a[0]) * dz + (p[1] - a[1]) * dx for p in hull]
        u, v = max(us) - min(us), max(vs) - min(vs)
        area, candidate = u * v, min(u, v)
        if area < best - 1e-8 or abs(area - best) <= 1e-8 and candidate < width:
            best, width, length = area, candidate, max(u, v)
    return width, length


def height_of(id_, b, measured, overrides, cx, cz):
    h, src = b.get('h', 7.), b.get('hSrc') or 'osm'
    if id_ in overrides:
        h, src = 3 * overrides[id_] - .4, 'ispravak'
    elif src not in ('osm-levels', 'osm-height') and id_ in measured and measured[id_][1] >= .6:
        n, _, src, *_ = measured[id_]
        h = 3 * n - .4
    if b.get('kind', 'house') == 'house':
        v = math.sin(cx * 12.9898 + cz * 78.233 + 37.719) * 43758.5453
        h = max(2.6, h + (v - math.floor(v) - .5) * .7)
    return h, src


def action(area, width, h, src, kind):
    if src == 'ispravak':
        return 'keep', h
    if width < C['SHAPE_MIN_WIDTH']:
        return 'skip', 0.
    if kind != 'house':
        return 'keep', h
    if src not in ('osm-levels', 'osm-height') and (
            area < C['SHAPE_TINY_AREA'] and width < C['SHAPE_TINY_WIDTH']):
        return 'skip', 0.
    cap = C['SHAPE_HEIGHT_WIDTH'] * width
    if area <= C['SHAPE_SMALL_AREA'] and width < C['SHAPE_SMALL_WIDTH'] and h > cap:
        return 'cap', cap
    return 'keep', h


def decode_rle(value):
    if isinstance(value, list):
        return [math.nan if v is None else v for v in value]
    out = []
    for token in value.split():
        v, _, count = token.partition('*')
        out.extend([math.nan if v == 'n' else float(v) / 10] * (int(count) if count else 1))
    return out


class Clearances:
    """WorldData.Models / FootDist and BuildingsFromTiles.Covered, no terrain needed."""
    def __init__(self):
        self.models = []
        for m in read_json(REGION / 'model-junctions.json')['junctions']:
            if -10240 <= m['x'] <= -400:
                g = dict(m['grid'])
                g['foot'] = decode_rle(g['foot'])
                self.models.append(g)
        self.rects = [r for r in read_json(REGION / 'model-buildings.json')['objects'] if 'w' in r and 'l' in r]

    def foot(self, x, z):
        best = math.inf
        for g in self.models:
            i, j = round((x - g['x0']) / g['cell']), round((z - g['z0']) / g['cell'])
            if 0 <= i < g['n'] and 0 <= j < g['n']:
                d = g['foot'][j * g['n'] + i]
                if not math.isnan(d):
                    best = min(best, d)
        return best

    def cleared(self, points):
        if len(points) < 3:
            return True
        cx = sum(p[0] for p in points) / len(points)
        cz = sum(p[1] for p in points) / len(points)
        if self.foot(cx, cz) < 3 or any(self.foot(*p) < 1.5 for p in points):
            return True
        poly = Polygon(points)
        for r in self.rects:
            dx, dz = cx - r['x'], cz - r['z']
            if dx * dx + dz * dz > 900:
                continue
            ca, sa = math.cos(r.get('a', 0)), math.sin(r.get('a', 0))
            u, v, m = dx * ca + dz * sa, -dx * sa + dz * ca, max(r['w'], r['l']) / 2 + 2
            if abs(u) < m and abs(v) < m or poly.contains(Point(r['x'], r['z'])):
                return True
        return False


def project(lon, lat, z=None):
    # GameDataBuilder.Places.cs spherical AEQD (same local Gaussian radius).
    p0, p, dl = math.radians(43.5083), math.radians(lat), math.radians(lon - 16.4400)
    e2 = (1 / 298.257223563) * (2 - 1 / 298.257223563)
    q = 1 - e2 * math.sin(p0) ** 2
    radius = math.sqrt((6378137 * (1 - e2) / q ** 1.5) * (6378137 / math.sqrt(q)))
    c = math.acos(max(-1, min(1, math.sin(p0) * math.sin(p) + math.cos(p0) * math.cos(p) * math.cos(dl))))
    k = c / math.sin(c) if c > 1e-12 else 1
    return (radius * k * math.cos(p) * math.sin(dl),
            -radius * k * (math.cos(p0) * math.sin(p) - math.sin(p0) * math.cos(p) * math.cos(dl)))


def villages():
    path = BD / 'tools/world/dgu/calibration-neighbourhoods.geojson'
    return [(f['properties']['name'], transform(project, shape(f['geometry'])))
            for f in read_json(path)['features'] if f['properties']['name'].startswith('Kaštel')]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'Tools/zgrade/stupovi.csv')
    args = parser.parse_args()
    measured = read_json(ROOT / 'Assets/Editor/visine-zgrada.json')['b']
    ov = read_json(ROOT / 'Assets/Editor/katovi-ispravci.json')
    overrides = {k: int(v) for k, v in ov.get('b', ov).items()
                 if isinstance(v, (int, float)) and not isinstance(v, bool) and 1 <= int(v) <= 40}
    ortho = read_json(ROOT / 'Assets/Editor/zgrade-ortofoto.json')
    shifts = {f'{k}:{e[0]}': e for k, es in ortho['b'].items() for e in es}
    places, clear = villages(), Clearances()
    rows, polys, variants = [], [], []
    limits = {k: int(re.search(r'\b' + k + r'\s*=\s*(-?\d+)', WB)[1]) for k in ('TX0', 'TX1', 'TZ0', 'TZ1')}
    for tz in range(limits['TZ0'], limits['TZ1'] + 1):
        for tx in range(limits['TX0'], limits['TX1'] + 1):
            for i, b in enumerate(buildings(tx, tz)):
                id_, p = f'{tx}_{tz}:{i}', clean(b['pts'])
                poly = Polygon(p) if len(p) >= 3 else Polygon()
                polys.append(poly)
                w, length = size_of(poly)
                centroid = poly.centroid if not poly.is_empty else Point(p[0] if p else (0, 0))
                gx, gz = centroid.x, centroid.y
                cx, cz = (sum(q[0] for q in p) / len(p), sum(q[1] for q in p) / len(p)) if p else (gx, gz)
                h, src = height_of(id_, b, measured, overrides, cx, cz)
                village = next((n for n, g in places if g.covers(centroid)), None)
                if village is None:
                    name, g = min(places, key=lambda ng: ng[1].distance(centroid))
                    village = name if g.distance(centroid) <= 250 else 'Izvan Kaštela'
                a, new_h = action(poly.area, w, h, src, b.get('kind', 'house'))
                possibilities = [(p, h, src)]
                e = shifts.get(id_)
                if e and abs(e[10] - cx) < 1.5 and abs(e[11] - cz) < 1.5 and (e[8] or e[9]):
                    shifted = [(x + e[8], z + e[9]) for x, z in p]
                    sh, ss = height_of(id_, b, measured, overrides, cx + e[8], cz + e[9])
                    possibilities.append((shifted, sh, ss))
                outcomes = set()
                for pp, hh, ss in possibilities:
                    outcomes.add('already-cleared' if clear.cleared(pp) else action(poly.area, w, hh, ss, b.get('kind', 'house'))[0])
                variants.append(outcomes)
                rows.append(dict(id=id_, village=village, gx=gx, gz=gz, unity_x=gx + 4200, unity_z=-(gz + 5300),
                                 area=poly.area, width=w, length=length,
                                 compactness=4 * math.pi * poly.area / poly.length ** 2 if poly.length else 0,
                                 height=h, height_source=src, height_width=h / w if w else math.inf,
                                 overlap=0., overlap_id='', validity=explain_validity(poly),
                                 action=a, fixed_height=new_h, builder_outcomes='|'.join(sorted(outcomes))))
    # Do not repair polygons for overlap: repairs could disguise malformed data.
    valid_indices = [i for i, p in enumerate(polys) if p.is_valid and p.area > 0]
    tree = STRtree([polys[i] for i in valid_indices])
    for i in valid_indices:
        p, row = polys[i], rows[i]
        for j0 in tree.query(p):
            j = valid_indices[int(j0)]
            if i == j:
                continue
            overlap = p.intersection(polys[j]).area / p.area
            if overlap > row['overlap']:
                row['overlap'], row['overlap_id'] = overlap, rows[j]['id']
    counts, flagged = collections.Counter(), []
    for row, poly, outcomes in zip(rows, polys, variants):
        tests = (row['width'] < 2.5, row['area'] < 10, row['height_width'] > 2.5,
                 row['width'] < 3 and row['length'] > 15, not poly.is_valid,
                 poly.is_empty or row['area'] < .01 or row['width'] < .01, row['overlap'] >= .9)
        row['rule'] = '|'.join(r for r, hit in zip(RULES, tests) if hit)
        counts.update(r for r, hit in zip(RULES, tests) if hit)
        # Include all proposed edits, even caps below the diagnostic ratio of 2.5.
        if row['rule'] or any(a in outcomes for a in ('skip', 'cap')):
            flagged.append(row)
    flagged.sort(key=lambda r: (-r['height_width'], r['width'], r['id']))
    with args.output.open('w', newline='') as f:
        writer = csv.DictWriter(f, fieldnames=list(rows[0]))
        writer.writeheader()
        for row in flagged:
            writer.writerow({k: round(v, 4) if isinstance(v, float) else v for k, v in row.items()})
    print(f'Tile footprints: {len(rows)}; CSV rows: {len(flagged)}; diagnostic candidates: {sum(bool(r["rule"]) for r in rows)}')
    for rule in RULES:
        print(f'{rule}: {counts[rule]}')
    print('Village | total | skip min..max | cap min..max (after model/package clearances; shift accepted/rejected)')
    for village in sorted({r['village'] for r in rows}) + ['TOTAL']:
        ids = [i for i, r in enumerate(rows) if village == 'TOTAL' or r['village'] == village]
        bounds = [(sum(variants[i] == {a} for i in ids), sum(a in variants[i] for i in ids)) for a in ('skip', 'cap')]
        print(village, len(ids), *[f'{lo}..{hi}' for lo, hi in bounds], sep=' | ')
    print('Before clearances:', dict(collections.Counter(r['action'] for r in rows)))
    added_actions = collections.Counter()
    for b in ortho['add']:
        poly = Polygon(b['pts']); w, _ = size_of(poly)
        # Upper bound: added houses can also be rejected by carriageway clearance < .8 m.
        if not clear.cleared(clean(b['pts'])):
            cx, cz = sum(x for x, z in b['pts']) / len(b['pts']), sum(z for x, z in b['pts']) / len(b['pts'])
            h, src = height_of(None, b, measured, overrides, cx, cz)
            added_actions[action(poly.area, w, h, src, 'house')[0]] += 1
    print('Orthophoto additions (upper bound before road rejection):', dict(added_actions))
    print('Worst 15, ranked by height/width (eaves; before fix, unshifted):')
    for r in flagged[:15]:
        print(f'{r["id"]:14} gx/gz {r["gx"]:.2f} {r["gz"]:.2f} A={r["area"]:.2f} W={r["width"]:.2f} H={r["height"]:.2f} H/W={r["height_width"]:.2f} {r["builder_outcomes"]} {r["rule"]}')
    print(f'CSV: {args.output} ({args.output.stat().st_size} bytes)')


if __name__ == '__main__':
    main()
