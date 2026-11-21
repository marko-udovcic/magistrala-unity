"""Ground of the Kaštela towns for Unity (Assets/Editor/GroundCover.cs, docs/audit-stari.md): sports pitches, parks, hard ground
surfaces (car parks, pools, courts, playgrounds), parked cars of the car parks and yard hedges, as one derived data file.

  BalkanDriver/tools/world/dgu/cache/venv/bin/python -I Tools/tlo/tlo_build.py        (numpy, Pillow, shapely ≥ 2.1, pyproj)

In:  the cached OSM Overpass answers of BalkanDriver tools/world/ground/build.py (leisure=pitch / park / playground, the
     ground build's own query) and that build's output client/public/assets/world/split-kastela/ground/{tx}_{tz}.json
     (polygons classified from the DGU orthophoto + OSM: car parks with bay lines and ~60 % occupied bays, pools, courts,
     playground rubber, hedges from thin green strips), the DGU LiDAR orthophoto mosaics (pitch surface: grass / artificial turf
     / paved, from the photo colour inside the pitch — only a class is kept, no imagery), the WorldBuilder core (game x
     −10240…0, z −7168…−4096).
Out: Assets/Editor/tlo-kastela.json
     pitches  OSM pitches + PHOTO_PITCHES (measured on the orthophoto where OSM has none): ring, sport, surface, the minimum
              rotated rectangle (cx, cz, ux, uz, A long, B short) for the markings / goals / fence
     parks    OSM leisure=park (+ name): ring
     surf     hard surfaces of the ground build, kind + triangles (vertices x, z; big ones cut on an 8 m grid so the mesh can follow
              the ground), minus everything inside a pitch (the pitch draws itself)
     cars     parked cars of the car parks [x, z, yaw] (yaw = atan2 of the car's long axis, game frame: x east, z south)
     hedges   [ax, az, bx, bz, height, width]
"""
import json, math
from pathlib import Path

import numpy as np
from PIL import Image
import shapely
from shapely.geometry import Polygon, LineString, Point, box
from shapely.ops import unary_union
from shapely.validation import make_valid
from pyproj import Transformer

BD = Path('/Users/macair/Documents/BalkanDriver')
MOS = BD / 'tools/world/dgu/cache/dof/mosaic-lidar'
OSM = [BD / 'tools/world/dgu/cache/ground' / (h + '.response') for h in
       ('ab4d926d6ab811d70716d73efc6a9e70f0b30b0e774328a137f8806ea5966b64', '93d81ade70af62ec009347f97a35d77985a976ecbe72179111cc4eb2f8128a89')]
GROUND = BD / 'client/public/assets/world/split-kastela/ground'
OUT = Path(__file__).resolve().parents[2] / 'Assets/Editor/tlo-kastela.json'
TX0, TX1, TZ0, TZ1 = -10, -1, -7, -5
X0, X1, Z0, Z1 = TX0 * 1024, (TX1 + 1) * 1024, TZ0 * 1024, (TZ1 + 1) * 1024
LL = Transformer.from_crs(4326, '+proj=aeqd +lat_0=43.5083 +lon_0=16.4400 +datum=WGS84 +units=m', always_xy=True)
KINDS = ['dry', 'soil', 'lawn', 'understory', 'concrete', 'asphalt', 'gravel', 'parking', 'turf', 'court', 'clay',
         'pool', 'paint', 'sand', 'rubber', 'tartan']
KEEP = {'parking', 'court', 'clay', 'pool', 'paint', 'rubber', 'tartan', 'turf'}

# pitches the orthophoto shows but OSM lacks (DOF LiDAR 2022./23., measured 2026-11-21 on the 0.5 m mosaic; game x, z corners)
PHOTO_PITCHES = [
    # Valovo (HNK Val, Kaštel Stari): the two small artificial-turf pitches west of the main pitch, by the clubhouse
    {'id': 'foto-valovo-sz', 'name': 'Valovo, mali teren (sjever)', 'sport': 'soccer', 'surface': 'artificial_turf',
     'ring': [(-7616.6, -5050.0), (-7600.8, -5050.0), (-7600.8, -5020.2), (-7616.6, -5020.2)]},
    {'id': 'foto-valovo-jz', 'name': 'Valovo, mali teren (jug)', 'sport': 'soccer', 'surface': 'artificial_turf',
     'ring': [(-7617.0, -4990.0), (-7598.4, -4990.0), (-7598.4, -4952.0), (-7617.0, -4952.0)]},
]


def game(lon, lat):
    x, y = LL.transform(lon, lat)
    return (round(x, 2), round(-y, 2))


def osm_features():
    seen, out = set(), []
    for f in OSM:
        for e in json.loads(f.read_text())['elements']:
            k = (e['type'], e['id'])
            if k in seen:
                continue
            seen.add(k)
            t = e.get('tags', {})
            if t.get('leisure') not in ('pitch', 'park', 'playground'):
                continue
            g = None
            if e['type'] == 'way' and e.get('geometry') and len(e['geometry']) >= 4:
                g = make_valid(Polygon([game(p['lon'], p['lat']) for p in e['geometry']]))
            elif e['type'] == 'relation':
                outer = [make_valid(Polygon([game(p['lon'], p['lat']) for p in m['geometry']])) for m in e.get('members', [])
                         if m.get('role') == 'outer' and m.get('geometry') and len(m['geometry']) >= 4]
                if outer:
                    g = unary_union(outer)
            if g is None or g.is_empty:
                continue
            c = g.centroid
            if X0 <= c.x < X1 and Z0 <= c.y < Z1:
                out.append((f'{e["type"]}/{e["id"]}', t, g))
    return out


def min_rect(g):
    r = g.minimum_rotated_rectangle
    c = list(r.exterior.coords)[:4]
    e = [(c[(i + 1) % 4][0] - c[i][0], c[(i + 1) % 4][1] - c[i][1]) for i in range(4)]
    L = [math.hypot(*v) for v in e]
    i = 0 if L[0] >= L[1] else 1
    ux, uz = e[i][0] / L[i], e[i][1] / L[i]
    if ux < 0 or (abs(ux) < 1e-9 and uz < 0):
        ux, uz = -ux, -uz
    return [round(r.centroid.x, 2), round(r.centroid.y, 2), round(ux, 5), round(uz, 5), round(L[i], 2), round(L[1 - i], 2)]


_tiles = {}


def photo(x, z):
    tx, tz = math.floor(x / 1024), math.floor(z / 1024)
    if (tx, tz) not in _tiles:
        p = MOS / f't_{tx}_{tz}.png'
        _tiles[(tx, tz)] = np.asarray(Image.open(p).convert('RGB')) if p.exists() else None
    a = _tiles[(tx, tz)]
    if a is None:
        return None
    i, j = int((x - tx * 1024) * 2), int((z - tz * 1024) * 2)
    return a[min(j, 2047), min(i, 2047)]


def surface_from_photo(g):
    """grass / artificial_turf / paved from the mean colour of the pitch (inner 70 %)"""
    inner = g.buffer(-0.15 * math.sqrt(g.area) / 2)
    if inner.is_empty:
        inner = g
    x0, z0, x1, z1 = inner.bounds
    px = []
    for z in np.arange(z0, z1, 1.0):
        for x in np.arange(x0, x1, 1.0):
            if inner.contains(Point(x, z)):
                v = photo(x, z)
                if v is not None:
                    px.append(v)
    if not px:
        return 'grass', None
    m = np.mean(np.asarray(px, np.float32), 0)
    R, G, B = m
    if m.mean() > 128:
        kind = 'paved'                            # light: a concrete / asphalt / acrylic court (its colour is kept)
    elif G - R > 6 and G - B > 4:
        kind = 'artificial_turf' if (G - R > 22 or m.mean() < 72) else 'grass'
    elif R - G > 12:
        kind = 'clay'
    else:
        kind = 'paved'
    return kind, [round(float(v)) for v in m]


def tri(g, cell):
    """triangles of a polygon (cut on a cell grid when big): flat vertex list x, z and index list"""
    pieces = []
    if g.area > cell * cell * 4:
        x0, z0, x1, z1 = g.bounds
        for gz in np.arange(math.floor(z0 / cell) * cell, z1, cell):
            for gx in np.arange(math.floor(x0 / cell) * cell, x1, cell):
                q = g.intersection(box(gx, gz, gx + cell, gz + cell))
                if not q.is_empty:
                    pieces.append(q)
    else:
        pieces = [g]
    V, T, idx = [], [], {}
    for q in pieces:
        for p in getattr(q, 'geoms', [q]):
            if p.geom_type != 'Polygon' or p.area < 0.01:
                continue
            for t in shapely.constrained_delaunay_triangles(p).geoms:
                c = list(t.exterior.coords)[:3]
                if Polygon(c).area < 1e-4:
                    continue
                ids = []
                for x, z in c:
                    k = (round(x, 2), round(z, 2))
                    if k not in idx:
                        idx[k] = len(V) // 2
                        V += [k[0], k[1]]
                    ids.append(idx[k])
                T += ids
    return V, T


def main():
    feats = osm_features()
    pitches, parks = [], []
    for fid, t, g in feats:
        if t.get('leisure') == 'park':
            for p in getattr(g, 'geoms', [g]):
                if p.geom_type == 'Polygon' and p.area > 200:
                    parks.append({'id': fid, 'name': t.get('name', ''), 'area': round(p.area),
                                  'ring': [round(v, 2) for xy in list(p.exterior.coords)[:-1] for v in xy]})
        elif t.get('leisure') == 'pitch' and g.geom_type in ('Polygon', 'MultiPolygon') and g.area > 80:
            surf, rgb = surface_from_photo(g)
            tag = t.get('surface', '')
            if tag in ('artificial_turf', 'grass', 'clay', 'tartan', 'acrylic', 'asphalt', 'concrete', 'paved'):
                surf = tag
            pitches.append({'id': fid, 'name': t.get('name', ''), 'sport': t.get('sport', ''), 'surface': surf, 'photo': rgb,
                            'rect': min_rect(g), 'ring': [round(v, 2) for xy in list(g.exterior.coords)[:-1] for v in xy] if g.geom_type == 'Polygon' else []})
    for p in PHOTO_PITCHES:
        g = Polygon(p['ring'])
        pitches.append({'id': p['id'], 'name': p['name'], 'sport': p['sport'], 'surface': p['surface'], 'photo': surface_from_photo(g)[1],
                        'rect': min_rect(g), 'ring': [v for xy in p['ring'] for v in xy]})
    pitch_u = unary_union([Polygon(list(zip(p['ring'][0::2], p['ring'][1::2]))).buffer(2) for p in pitches if len(p['ring']) >= 6])

    surf, cars, hedges, stats = [], [], [], {}
    for tx in range(TX0, TX1 + 1):
        for tz in range(TZ0, TZ1 + 1):
            f = GROUND / f'{tx}_{tz}.json'
            if not f.exists():
                continue
            d = json.loads(f.read_text())
            ox, oz = tx * 1024, tz * 1024
            for p in d['polys']:
                kind = KINDS[p[0]]
                if kind not in KEEP:
                    continue
                rings = [list(zip([v + ox for v in r[0::2]], [v + oz for v in r[1::2]])) for r in p[1:]]
                if len(rings[0]) < 3:
                    continue
                g = make_valid(Polygon(rings[0], [r for r in rings[1:] if len(r) >= 3]))
                if kind in ('turf', 'court', 'clay', 'tartan', 'paint'):
                    g = g.difference(pitch_u)            # the pitches draw their own surface and lines
                if g.is_empty or g.area < (0.03 if kind == 'paint' else 2):
                    continue
                if kind == 'turf':
                    kind = 'turf'                         # (a turf area OSM has no pitch for: grass surface, no furniture)
                for q in getattr(g, 'geoms', [g]):
                    if q.geom_type != 'Polygon' or q.area < (0.03 if kind == 'paint' else 2):
                        continue
                    V, T = tri(q, 8.0)
                    if T:
                        surf.append({'k': kind, 'v': V, 't': T})
                        stats[kind] = stats.get(kind, 0) + 1
            for c in d['cars']:
                cars.append([round(c[0] + ox, 2), round(c[1] + oz, 2), c[2]])
            for h in d['hedges']:
                hedges.append([round(h[0] + ox, 2), round(h[1] + oz, 2), round(h[2] + ox, 2), round(h[3] + oz, 2), h[4], h[5]])
    out = {'version': 1, 'source': 'OSM (ODbL) + DGU DOF LiDAR 2022./23. (ground build of BalkanDriver); derived vectors only',
           'pitches': pitches, 'parks': parks, 'surf': surf, 'cars': cars, 'hedges': hedges}
    OUT.write_text(json.dumps(out, separators=(',', ':')))
    print(f'{len(pitches)} pitches, {len(parks)} parks, surfaces {stats}, {len(cars)} cars, {len(hedges)} hedges; {OUT} {OUT.stat().st_size // 1024} kB')
    for p in pitches:
        print('  pitch', p['id'], p['name'], p['sport'], p['surface'], p['photo'], p['rect'])


if __name__ == '__main__':
    main()
