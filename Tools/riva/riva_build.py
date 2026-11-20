"""Rive Kaštela: the paved waterfront (šetnica) between the coastal street and the sea, from the DGU orthophoto (Assets/Editor/RivaBuilder.cs).

  1. Unity: Magistrala/Riva: ulazni podaci (RivaBuilder.ExportInputs) -> Logs/riva-input.json (carriageways of the built streets, trees)
  2. tools/world/dgu/cache/venv/bin/python -I Tools/riva/riva_build.py   (numpy, scipy, Pillow, shapely >= 2.1)
  3. Unity: Magistrala/Build streets (ends with RivaBuilder.Build), Build trees, ...

Reads (never writes) the BalkanDriver repo: the DOF LiDAR 2022/23 mosaic (tools/world/dgu/cache/dof/mosaic-lidar/t_TX_TZ.png, 0.5 m/px,
north up, game frame), the tiles' building footprints, coast.json (harbour works, beaches). Config: Tools/riva/riva-config.json.
Per zone (riva-config.json zones):
  water   photo sea (teal: G, B well above R), cleaned (specks, boats out in the open), harbour basins forced to water (moored boats)
  riva    land within 35 m of the photo sea, not our asphalt (carriageways + 0.15 m), not a building, beach or car park, connected to
          the sea edge (what lies between the coastal street and the sea), + the harbour works (coast.json breakwater / quay / mole,
          config works); pontoons (coast.json piers < 3.5 m wide) kept apart (low floating decks)
  trees   tree crowns of the photo (dark green blobs): one tree per crown (7 m grid in big crowns); new ones where we have none within
          4 m (not on asphalt / roofs / car park bays), ours on paving / car parks without a crown there are dropped (grid bit)
Out: Assets/Editor/riva.json — meshes (vertices + triangles, cut on a 4 m grid so the heights can follow the street), boundary rings
     with a sea flag per segment (sea wall vs kerb), car parks (+ bay lines), trees to add, and a 1 m grid (gzip + base64):
     bits 0-1 class (1 paving / car park, 2 paving within 3 m of the sea wall, 3 photo sea), bit 2 photo crown, bits 3-7 distance of
     a sea cell to land in 0.5 m (0..31); on a class 1 cell bit 3 = car park.
"""
import base64, gzip, json, math, struct, sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
import shapely
from shapely.geometry import Polygon, LineString, MultiPolygon, box, Point
from shapely.ops import unary_union

BD = Path('/Users/macair/Documents/BalkanDriver')
REG = BD / 'client/public/assets/world/split-kastela'
MOS = BD / 'tools/world/dgu/cache/dof/mosaic-lidar'
UP = Path(__file__).resolve().parents[2]
CFG = json.loads((UP / 'Tools/riva/riva-config.json').read_text())
INP = json.loads((UP / 'Logs/riva-input.json').read_text())
OUT = UP / 'Assets/Editor/riva.json'
PX = 0.5
DEBUG = '--debug' in sys.argv


def tile_heights_bld(tx, tz):
    p = REG / f'tiles/{tx}_{tz}.bin'
    if not p.exists(): return None
    b = p.read_bytes(); o = 16; bj = None
    while o + 8 <= len(b):
        t, l = struct.unpack_from('<ii', b, o); o += 8
        if t == 4: bj = b[o:o + l].decode('utf-8')
        o += l
    return json.loads(bj) if bj else []


class Grid:
    """0.5 m raster over a zone box (row 0 = north = smallest game z, as the mosaic)"""
    def __init__(self, x0, z0, x1, z1):
        self.x0, self.z0 = math.floor(x0), math.floor(z0)
        self.nx, self.nz = int(math.ceil((x1 - self.x0) / PX)), int(math.ceil((z1 - self.z0) / PX))

    def img(self): return Image.new('1', (self.nx, self.nz), 0)
    def pix(self, x, z): return ((x - self.x0) / PX, (z - self.z0) / PX)
    def poly(self, dr, pts, v=1):
        if len(pts) >= 3: dr.polygon([self.pix(x, z) for x, z in pts], fill=v)
    def mask(self, im): return np.asarray(im, dtype=bool)
    def gx(self, i): return self.x0 + (i + .5) * PX
    def gz(self, j): return self.z0 + (j + .5) * PX


def photo(g):
    """RGBA of the zone from the mosaic tiles (alpha 0 = no photo)"""
    a = np.zeros((g.nz, g.nx, 4), np.uint8)
    for tz in range(math.floor(g.z0 / 1024), math.floor((g.z0 + g.nz * PX - 1e-6) / 1024) + 1):
        for tx in range(math.floor(g.x0 / 1024), math.floor((g.x0 + g.nx * PX - 1e-6) / 1024) + 1):
            p = MOS / f't_{tx}_{tz}.png'
            if not p.exists(): continue
            im = np.asarray(Image.open(p).convert('RGBA'))
            # tile pixel (row j, col i) centre = game (tx*1024 + (i+.5)*.5, tz*1024 + (j+.5)*.5)
            oi, oj = int(round((tx * 1024 - g.x0) / PX)), int(round((tz * 1024 - g.z0) / PX))
            i0, j0 = max(0, oi), max(0, oj); i1, j1 = min(g.nx, oi + 2048), min(g.nz, oj + 2048)
            if i1 > i0 and j1 > j0: a[j0:j1, i0:i1] = im[j0 - oj:j1 - oj, i0 - oi:i1 - oi]
    return a


def coast_surfaces():
    c = json.loads((REG / 'coast.json').read_text()); T = c['tileSize']; out = []
    for k, t in c['tiles'].items():
        tx, tz = map(int, k.split(','))
        for s in t['surfaces']:
            for r in s.get('rings', []):
                out.append((s['kind'], s.get('src', 'osm'), [(tx * T + p[0], tz * T + p[1]) for p in r]))
    return out


def width_of(poly):
    """mean width of a strip polygon: 2 area / perimeter"""
    return 2 * poly.area / max(poly.length, 1e-6)


def to_polys(mask, g, simplify=.4, min_area=6):
    """raster -> shapely polygons (game frame), simplified, small bits dropped"""
    import rasterio.features
    from rasterio.transform import Affine
    tr = Affine(PX, 0, g.x0, 0, PX, g.z0)
    ps = []
    for geom, v in rasterio.features.shapes(mask.astype(np.uint8), mask=mask, transform=tr):
        if v != 1: continue
        p = shapely.geometry.shape(geom).buffer(0)
        if p.area < min_area: continue
        ps.append(p)
    # the raster's 0.5 m steps and the photo's ragged water line: closed 0.8 m, opened 0.6 m, then simplified
    u = unary_union(ps).buffer(.8, quad_segs=4).buffer(-1.4, quad_segs=4).buffer(.6, quad_segs=4)
    u = u.simplify(simplify, preserve_topology=True).buffer(0)
    return [shapely.geometry.polygon.orient(p, 1.0) for p in (u.geoms if hasattr(u, 'geoms') else [u]) if p.area >= min_area]


def mesh(polys, cell=4.0):
    """polygons cut on a cell grid, each piece triangulated (constrained Delaunay): indexed vertices + triangles"""
    V, I, key = [], [], {}
    def vid(x, z):
        k = (round(x * 100), round(z * 100))
        if k not in key: key[k] = len(V); V.append((round(x, 2), round(z, 2)))
        return key[k]
    for P in polys:
        P = shapely.segmentize(P, 2.0)
        x0, z0, x1, z1 = P.bounds
        for cz in np.arange(math.floor(z0 / cell) * cell, z1, cell):
            for cx in np.arange(math.floor(x0 / cell) * cell, x1, cell):
                piece = P.intersection(box(cx, cz, cx + cell, cz + cell))
                if piece.is_empty or piece.area < 1e-3: continue
                for part in (piece.geoms if hasattr(piece, 'geoms') else [piece]):
                    if part.geom_type != 'Polygon' or part.area < 1e-3: continue
                    for t in shapely.constrained_delaunay_triangles(part).geoms:
                        c = list(t.exterior.coords)[:3]
                        a = (c[1][0] - c[0][0]) * (c[2][1] - c[0][1]) - (c[1][1] - c[0][1]) * (c[2][0] - c[0][0])
                        if abs(a) < 1e-4: continue
                        ids = [vid(*p) for p in c]
                        if a < 0: ids = [ids[0], ids[2], ids[1]]  # counter-clockwise in (x, z)
                        I.extend(ids)
    return V, I


def rings(polys, sea, g):
    """boundary rings (segmentized 2 m) with a flag per segment: 1 = sea wall (the photo sea 0.75 m outside), 0 = kerb"""
    out = []
    for P in polys:
        P = shapely.segmentize(P, 2.0)
        for r in [P.exterior] + list(P.interiors):
            c = list(r.coords)[:-1]; n = len(c); fl = []
            for k in range(n):
                (ax, az), (bx, bz) = c[k], c[(k + 1) % n]
                L = math.hypot(bx - ax, bz - az) or 1
                mx, mz = (ax + bx) / 2, (az + bz) / 2; nx, nz = -(bz - az) / L, (bx - ax) / L
                f = 0
                for s in (1, -1):
                    qx, qz = mx + nx * .75 * s, mz + nz * .75 * s
                    if P.contains(Point(qx, qz)): continue
                    i, j = int((qx - g.x0) / PX), int((qz - g.z0) / PX)
                    if 0 <= i < g.nx and 0 <= j < g.nz and sea[j, i]: f = 1
                fl.append(f)
            out.append({'pts': [[round(x, 2), round(z, 2)] for x, z in c], 'sea': fl})
    return out


def main():
    cw = INP['cw']; trees_have = INP['trees']
    surfs = coast_surfaces()
    res = {'version': 1, 'source': 'DGU DOF LiDAR 2022./23. (ortofoto, Otvorena dozvola), OSM (coast.json, ODbL), Tools/riva/riva_build.py',
           'zones': [], 'paving': [], 'pontoons': [], 'parking': [], 'trees': [], 'landmarks': CFG.get('landmarks', {})}
    for zone in CFG['zones']:
        x0, z0, x1, z1 = zone['box']; g = Grid(x0, z0, x1, z1)
        a = photo(g); r, gg, b, al = [a[..., k].astype(np.int16) for k in range(4)]
        lum = (r * 3 + gg * 6 + b) / 10
        water = (((gg - r) > 18) & ((b - r) > 15) & (b >= gg - 14) & (lum < 175)) | (al < 128)
        water = ndimage.binary_opening(water, iterations=1)
        # the sea only: big connected water (shadows, pools and dark roofs read teal too)
        lab, n = ndimage.label(water); sz = ndimage.sum(water, lab, range(1, n + 1))
        water = np.isin(lab, np.nonzero(sz >= 5000 / (PX * PX))[0] + 1)
        # boats / specks out in the water: small land bits not attached to the shore
        land = ~water; lab, n = ndimage.label(land); sz = ndimage.sum(land, lab, range(1, n + 1))
        land &= np.isin(lab, np.nonzero(sz >= 200 / (PX * PX))[0] + 1)
        im = g.img(); dr = ImageDraw.Draw(im)
        for bs in CFG['basins']: g.poly(dr, bs['pts'])
        basin = g.mask(im)
        # harbour works (coast.json breakwater / quay / mole riva, + config works) and pontoons
        works, pontoons, beach = [], [], []
        zb = box(x0, z0, x1, z1)
        for kind, src, pts in surfs:
            if len(pts) < 3: continue
            P = Polygon(pts).buffer(0)
            if not P.intersects(zb): continue
            if kind in ('beach', 'pebble'): beach.append(P)
            elif kind == 'pier' and width_of(P) < 3.5: pontoons.append(P)
            elif kind in ('pier', 'quay', 'breakwater') or (kind == 'riva' and src == 'harbours'): works.append(P)
        for w in CFG['works']:
            P = LineString(w['line']).buffer(w['w'] / 2, cap_style='flat', join_style='round')
            if P.intersects(zb): works.append(P)
        im = g.img(); dr = ImageDraw.Draw(im)
        for P in works:
            for p in (P.geoms if hasattr(P, 'geoms') else [P]): g.poly(dr, list(p.exterior.coords))
        workm = g.mask(im)
        land &= ~basin
        land |= workm
        water = ~land
        # our asphalt, buildings, beaches, car parks
        im = g.img(); dr = ImageDraw.Draw(im)
        for ax, az, bx, bz, hw in cw:
            if max(ax, bx) < x0 - 10 or min(ax, bx) > x1 + 10 or max(az, bz) < z0 - 10 or min(az, bz) > z1 + 10: continue
            L = math.hypot(bx - ax, bz - az)
            if L < 1e-6: continue
            ux, uz = (bx - ax) / L * .3, (bz - az) / L * .3; nx, nz = -(bz - az) / L * (hw + .15), (bx - ax) / L * (hw + .15)
            g.poly(dr, [(ax - ux + nx, az - uz + nz), (bx + ux + nx, bz + uz + nz), (bx + ux - nx, bz + uz - nz), (ax - ux - nx, az - uz - nz)])
        road = g.mask(im)
        im = g.img(); dr = ImageDraw.Draw(im)
        for tz in range(math.floor(z0 / 1024) - 1, math.floor(z1 / 1024) + 2):
            for tx in range(math.floor(x0 / 1024) - 1, math.floor(x1 / 1024) + 2):
                for bo in tile_heights_bld(tx, tz) or []:
                    pts = bo.get('pts') or []
                    if len(pts) >= 3 and any(x0 - 20 < p[0] < x1 + 20 and z0 - 20 < p[1] < z1 + 20 for p in pts): g.poly(dr, [tuple(p[:2]) for p in pts])
        bld = g.mask(im)
        im = g.img(); dr = ImageDraw.Draw(im)
        for P in beach:
            for p in (P.geoms if hasattr(P, 'geoms') else [P]): g.poly(dr, list(p.exterior.coords))
        beachm = g.mask(im)
        im = g.img(); dr = ImageDraw.Draw(im)
        parks = [p for p in CFG['parking'] if Polygon(p['pts']).intersects(zb)]
        for p in parks: g.poly(dr, p['pts'])
        parkm = g.mask(im)
        # the riva: land by the sea, between the street and the water
        dw = ndimage.distance_transform_edt(~water) * PX
        cand = land & ~road & ~bld & ~(beachm & ~workm) & ~parkm & (dw <= 35)
        cand = ndimage.binary_opening(cand, iterations=1)
        lab, n = ndimage.label(cand)
        touch = np.unique(lab[ndimage.binary_dilation(water, iterations=2) & cand])
        riva = np.isin(lab, touch[touch > 0])
        riva = ndimage.binary_closing(riva, iterations=1) & ~road & ~bld
        quay = land & road & (dw <= 2.5) & ndimage.binary_dilation(riva | (water & ~basin), iterations=int(3 / PX))
        riva |= quay
        pav = to_polys(riva, g)
        # pontoons: apart, minus the paving they start from
        pu = unary_union(pav) if pav else Polygon()
        pon = []
        for P in pontoons:
            q = P.difference(pu).buffer(0)
            for p in (q.geoms if hasattr(q, 'geoms') else [q]):
                if p.geom_type == 'Polygon' and p.area > 4 and p.intersects(zb): pon.append(shapely.geometry.polygon.orient(p, 1.0))
        # trees: the photo's crowns
        crown = ((gg - r) > 6) & ((gg - b) > 4) & (lum < 125) & land & ~bld & (al > 128)
        crown = ndimage.binary_opening(crown, iterations=2)
        lab, n = ndimage.label(crown)
        have = np.array([(t[0], t[1]) for t in trees_have if x0 - 5 < t[0] < x1 + 5 and z0 - 5 < t[1] < z1 + 5]) if trees_have else np.zeros((0, 2))
        add = []
        objs = ndimage.find_objects(lab)
        for k, sl in enumerate(objs, 1):
            m = lab[sl] == k; A = m.sum() * PX * PX
            if A < 5: continue
            jj, ii = np.nonzero(m); jj = jj + sl[0].start; ii = ii + sl[1].start
            if A < 70: pts = [(ii.mean(), jj.mean(), math.sqrt(A / math.pi))]
            else:
                pts = []; st = int(7 / PX)
                for j in range(sl[0].start + st // 2, sl[0].stop, st):
                    for i in range(sl[1].start + st // 2, sl[1].stop, st):
                        if lab[j, i] == k: pts.append((i, j, 3.5))
            for i, j, rad in pts:
                xx, zz = g.gx(int(i)), g.gz(int(j)); ci, cj = int(i), int(j)
                if road[cj, ci] or bld[cj, ci] or parkm[cj, ci] or water[cj, ci] or dw[cj, ci] < 3: continue
                rr = int(1.2 / PX)
                if road[max(0, cj - rr):cj + rr + 1, max(0, ci - rr):ci + rr + 1].any(): continue
                if len(have) and np.min(np.hypot(have[:, 0] - xx, have[:, 1] - zz)) < 4: continue
                if any(math.hypot(t[0] - xx, t[1] - zz) < 4 for t in add): continue
                kind = 10 if dw[cj, ci] < 40 else 1
                add.append((round(xx, 2), round(zz, 2), kind, round(min(1.3, max(.6, rad / 3.5)), 2), int(riva[cj, ci])))
        # the row of trees along the kerb (Obala kralja Tomislava: kerb, tree pits, then the promenade to the sea wall; ~10 m apart)
        pavu = unary_union(pav) if pav else Polygon()
        bays = [b for b in INP.get('bays', []) if x0 - 20 < b[0] < x1 + 20 and z0 - 20 < b[1] < z1 + 20]
        def inside(xx, zz):
            i, j = int((xx - g.x0) / PX), int((zz - g.z0) / PX)
            return 0 <= i < g.nx and 0 <= j < g.nz
        def at(m, xx, zz):
            i, j = int((xx - g.x0) / PX), int((zz - g.z0) / PX)
            return inside(xx, zz) and bool(m[j, i])
        nrow = 0
        for P in pav:
            ring = P.exterior; L = ring.length; s = 5.0
            while s < L:
                pa = ring.interpolate(s); pb = ring.interpolate(min(L, s + .5)); s += 10.0
                dx, dz = pb.x - pa.x, pb.y - pa.y; l = math.hypot(dx, dz) or 1; ox, oz = dz / l, -dx / l  # outward (CCW exterior: right)
                if not at(road, pa.x + ox * 1.0, pa.y + oz * 1.0): continue                  # the kerb of a street
                tx_, tz_ = pa.x - ox * 1.3, pa.y - oz * 1.3
                if not all(at(riva, pa.x - ox * d, pa.y - oz * d) for d in (1.3, 3.0, 5.0)): continue  # promenade wide enough
                if at(road, tx_ - ox * 3.5, tz_ - oz * 3.5) or not inside(tx_, tz_) or dw[int((tz_ - g.z0) / PX), int((tx_ - g.x0) / PX)] < 4.5: continue
                if any(abs((tx_ - bx) * bdx + (tz_ - bz) * bdz) < bhl + 2 and abs(-(tx_ - bx) * bdz + (tz_ - bz) * bdx) < bhw + 2.5 for bx, bz, bdx, bdz, bhl, bhw in bays): continue
                if any(math.hypot(t[0] - tx_, t[1] - tz_) < 7 for t in add): continue
                if len(have) and np.min(np.hypot(have[:, 0] - tx_, have[:, 1] - tz_)) < 5: continue
                lm = CFG.get('landmarks', {}).get('konjanik')
                if lm and math.hypot(lm[0] - tx_, lm[1] - tz_) < 12: continue
                add.append((round(tx_, 2), round(tz_, 2), 10, .85, 1)); nrow += 1
        print(f"  kerb row: {nrow} trees")
        # 1 m grid
        n1x, n1z = g.nx // 2, g.nz // 2
        def down(m): return m[:n1z * 2, :n1x * 2].reshape(n1z, 2, n1x, 2).mean(axis=(1, 3))
        im = g.img(); dr = ImageDraw.Draw(im)
        for P in pav + pon:
            g.poly(dr, list(P.exterior.coords))
            for h in P.interiors: g.poly(dr, list(h.coords), 0)
        pavm = g.mask(im)
        pv = down(pavm | parkm) >= .5
        sea1 = (down(water & ~pavm) >= .5) & ~pv
        dsea = ndimage.distance_transform_edt(~sea1)        # paving cell -> sea, m
        dland = ndimage.distance_transform_edt(sea1)        # sea cell -> land / paving, m
        cls = np.zeros((n1z, n1x), np.uint8)
        cls[pv] = 1; cls[pv & (dsea <= 3) & (down(pavm) >= .5)] = 2; cls[sea1] = 3
        cr = down(ndimage.binary_dilation(crown, iterations=int(2 / PX))) > 0
        code = cls | (cr.astype(np.uint8) << 2) | (np.clip(np.round(dland * 2), 0, 31).astype(np.uint8) << 3) * sea1
        code |= ((down(parkm) >= .5) & pv).astype(np.uint8) << 3   # (paving / car park cells: bit 3 = car park)
        res['zones'].append({'id': zone['id'], 'x0': g.x0, 'z0': g.z0, 'nx': int(n1x), 'nz': int(n1z), 'cell': 1,
                             'grid': base64.b64encode(gzip.compress(code.tobytes(), 9)).decode()})
        V, I = mesh(pav)
        res['paving'].append({'zone': zone['id'], 'v': [c for p in V for c in p], 't': I, 'rings': rings(pav, water, g)})
        V, I = mesh(pon)
        res['pontoons'].append({'zone': zone['id'], 'v': [c for p in V for c in p], 't': I, 'rings': rings(pon, water, g)})
        for p in parks:
            V, I = mesh([Polygon(p['pts'])])
            lines = []
            for row in p['rows']:
                (ax, az), (bx, bz) = row['a'], row['b']; L = math.hypot(bx - ax, bz - az); ux, uz = (bx - ax) / L, (bz - az) / L
                sx, sz = -uz * row['side'], ux * row['side']
                nb = int(L // 2.5)
                for k in range(nb + 1):
                    px, pz = ax + ux * k * 2.5, az + uz * k * 2.5
                    lines.append([round(px, 2), round(pz, 2), round(px + sx * 5, 2), round(pz + sz * 5, 2)])
                lines.append([round(ax + sx * 5, 2), round(az + sz * 5, 2), round(ax + sx * 5 + ux * nb * 2.5, 2), round(az + sz * 5 + uz * nb * 2.5, 2)])
            res['parking'].append({'id': p['id'], 'v': [c for q in V for c in q], 't': I, 'ring': p['pts'], 'lines': lines})
        res['trees'] += [list(t) for t in add]
        area = sum(P.area for P in pav)
        print(f"{zone['id']}: paving {len(pav)} polygons {area:.0f} m2, {len(I)} pontoon idx, works {len(works)}, pontoons {len(pon)}, parks {len(parks)}, "
              f"trees +{len(add)} (photo crowns {n}), grid {n1x}x{n1z}")
        if DEBUG:
            dbg = a[..., :3].copy()
            dbg[pavm] = (dbg[pavm] * .4 + np.array([255, 0, 255]) * .6).astype(np.uint8)
            dbg[road] = (dbg[road] * .5 + np.array([255, 255, 0]) * .5).astype(np.uint8)
            dbg[parkm] = (dbg[parkm] * .5 + np.array([0, 128, 255]) * .5).astype(np.uint8)
            D = ImageDraw.Draw(im2 := Image.fromarray(dbg))
            for P in pon: D.polygon([g.pix(x, z) for x, z in P.exterior.coords], outline=(0, 255, 255))
            for rg in res['paving'][-1]['rings']:
                c = rg['pts']
                for k in range(len(c)):
                    D.line([g.pix(*c[k]), g.pix(*c[(k + 1) % len(c)])], fill=(255, 0, 0) if rg['sea'][k] else (0, 255, 0), width=2)
            for t in add: D.ellipse([g.pix(t[0] - 1.5, t[1] - 1.5), g.pix(t[0] + 1.5, t[1] + 1.5)], outline=(255, 128, 0), width=2)
            for t in have: D.ellipse([g.pix(t[0] - 1, t[1] - 1), g.pix(t[0] + 1, t[1] + 1)], outline=(255, 255, 255), width=1)
            im2.save(UP / f"Logs/riva-debug-{zone['id']}.jpg", quality=85)
    OUT.write_text(json.dumps(res, separators=(',', ':')))
    print(f'{OUT}: {OUT.stat().st_size / 1e6:.2f} MB')


if __name__ == '__main__':
    main()
