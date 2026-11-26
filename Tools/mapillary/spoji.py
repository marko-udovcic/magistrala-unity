"""Mapillary podaci za sve ulice (BalkanDriver tools/world/mapillary, izlazi *-all.json od 24. 11. 2026.) -> Assets/Editor/*-mapillary.json.
Street-level imagery: Mapillary contributors (CC BY-SA 4.0); samo činjenice (boja / vrsta fasade, krošnje, parkirališta).
Ništa staro i dobro ne nestaje:
  * fasade   fasade-all.json je nadskup fasade.json (isti building id, conf nikad niži, isti centroid): uzima se cijeli; provjera
  * stabla   stara datoteka igre (stara cesta + D8, s poljima road / edge) ostaje; krošnja iz stabla-all.json unutar 1.5 m od
             stare zamjenjuje je samo s većim conf (pravilo merge_all.py), ostale se dodaju. road = bliža od stare ceste (s) i
             D8 (d) ako je rub te ceste ≤ 25 m (pojas iz s4_trees.py), inače u (ostale ulice); edge = m od ruba bliže od te dvije
             ceste (graph.json: udaljenost od osi − širina / 2), kao u staroj datoteci
  * parking  stara parkirališta ostaju istim redom (MapillaryLots: id = indeks, Generated/Tlo/mapillary-lots.json čuva keep po
             id-ju); novo parkiralište iz parking-all.json se dodaje na kraj kad ne pokriva staro (središte u staroj ljusci + 3 m,
             staro središte u njegovoj, ili središta < 10 m); rubni segmenti (9, sve ulice) se prepisuju (igra ih još ne koristi)
Pokretanje (bez mreže, bez tokena): python3 -I Tools/mapillary/spoji.py
"""
import json, math, os
HERE = os.path.dirname(os.path.abspath(__file__))
ED = os.path.join(HERE, '..', '..', 'Assets', 'Editor')
SRC = '/Users/macair/Documents/BalkanDriver/tools/world/mapillary'
GRAPH = '/Users/macair/Documents/BalkanDriver/client/public/assets/world/split-kastela/graph.json'
BAND = 25.0


def load(p): return json.load(open(p, encoding='utf-8'))


def facades():
    old = load(os.path.join(ED, 'fasade-mapillary.json'))['b']
    new = load(os.path.join(SRC, 'fasade-all.json'))
    for k, o in old.items():
        n = new['b'].get(k)
        assert n is not None, k
        assert n['conf'] >= o['conf'] and abs(n['cx'] - o['cx']) < .01 and abs(n['cz'] - o['cz']) < .01, k
    json.dump(new, open(os.path.join(ED, 'fasade-mapillary.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
    b = new['b'].values()
    print(f'fasade: {len(old)} -> {len(new["b"])} (conf >= 0.3: {sum(o["conf"] >= .3 for o in old.values())} -> {sum(o["conf"] >= .3 for o in b)})')


def roads():
    S = {'s': [], 'd': []}
    for e in load(GRAPH)['edges']:
        k = 's' if e.get('ref') == '6137' else 'd' if e.get('ref') == 'D8' else None
        if not k: continue
        P = [(p[0], p[2]) for p in e['points']]
        for a, b in zip(P, P[1:]): S[k].append((a, b, e.get('width', 8)))
    grid = {}
    for k, L in S.items():
        for (a, b, w) in L:
            for gx in range(math.floor(min(a[0], b[0]) / 32) - 1, math.floor(max(a[0], b[0]) / 32) + 2):
                for gz in range(math.floor(min(a[1], b[1]) / 32) - 1, math.floor(max(a[1], b[1]) / 32) + 2):
                    grid.setdefault((gx, gz), []).append((k, a, b, w))
    return S, grid


def classify(x, z, S, grid):
    """(road, edge): the nearer of the old road / D8 and the distance from its edge"""
    def near(cands):
        best = (1e9, None)
        for k, (ax, az), (bx, bz), w in cands:
            dx, dz = bx - ax, bz - az; L = dx * dx + dz * dz
            t = 0 if L < 1e-9 else max(0, min(1, ((x - ax) * dx + (z - az) * dz) / L))
            d = math.hypot(ax + t * dx - x, az + t * dz - z) - w / 2
            if d < best[0]: best = (d, k)
        return best
    c = []
    for i in (-1, 0, 1):
        for j in (-1, 0, 1): c += grid.get((math.floor(x / 32) + i, math.floor(z / 32) + j), [])
    d, k = near(c)   # (the grid holds every segment within >= 32 m of the cell)
    if k is None or d > BAND: return 'u', round(min(d, 99), 1)
    return k, round(d, 1)


def trees():
    gp = os.path.join(ED, 'stabla-mapillary.json')
    game = load(gp)
    old = game['trees']
    new = load(os.path.join(SRC, 'stabla-all.json'))
    S, grid = roads()
    cell = {}
    def key(x, z): return (math.floor(x / 2), math.floor(z / 2))
    for i, o in enumerate(old): cell.setdefault(key(o['gx'], o['gz']), []).append(i)
    out = list(old); replaced = added = 0; byroad = {}
    for o in new:
        x, z = o['gx'], o['gz']; kx, kz = key(x, z)
        near = [i for dx in (-1, 0, 1) for dz in (-1, 0, 1) for i in cell.get((kx + dx, kz + dz), []) if i < len(old)
                and math.hypot(old[i]['gx'] - x, old[i]['gz'] - z) <= 1.5]
        rec = {'gx': x, 'gz': z, 'type': o['type'], 'conf': o['conf'], 'd': o['d'], 'h': o.get('h', 0), 'n': o['n'],
               'patch': 1 if o.get('patch') else 0}
        if near:
            i = min(near, key=lambda i: math.hypot(old[i]['gx'] - x, old[i]['gz'] - z))
            if o['conf'] > out[i]['conf']:
                rec['road'], rec['edge'] = out[i]['road'], out[i]['edge']; out[i] = rec; replaced += 1
            continue
        rec['road'], rec['edge'] = classify(x, z, S, grid)
        out.append(rec); added += 1; byroad[rec['road']] = byroad.get(rec['road'], 0) + 1
    src = ('BalkanDriver tools/world/mapillary/stabla.json + stabla-all.json (s4_trees.py, sve ulice 24. 11. 2026., docs/mapillary-izvlacenje.md; '
           'Tools/mapillary/spoji.py); Street-level imagery: Mapillary contributors (CC BY-SA 4.0); road: s = old road 6137, d = D8 (nearer one, '
           'edge <= 25 m), u = other streets; edge = m from the edge of the nearer of 6137 / D8')
    with open(gp, 'w', encoding='utf-8') as f:
        f.write('{"source":' + json.dumps(src, ensure_ascii=False) + ',"trees":[\n')
        f.write(',\n'.join(json.dumps(t, ensure_ascii=False, separators=(',', ':')) for t in out))
        f.write('\n]}\n')
    print(f'stabla: {len(old)} -> {len(out)} ({added} novih: {byroad}; {replaced} starih zamijenjeno većim conf)')


def hull_has(poly, x, z, grow=0.0):
    """point in polygon (ray cast), or within grow m of its edge"""
    inside = False; n = len(poly)
    for i in range(n):
        (ax, az), (bx, bz) = poly[i], poly[(i + 1) % n]
        if (az > z) != (bz > z) and x < ax + (z - az) * (bx - ax) / (bz - az): inside = not inside
    if inside or grow <= 0: return inside
    for i in range(n):
        (ax, az), (bx, bz) = poly[i], poly[(i + 1) % n]; dx, dz = bx - ax, bz - az; L = dx * dx + dz * dz
        t = 0 if L < 1e-9 else max(0, min(1, ((x - ax) * dx + (z - az) * dz) / L))
        if math.hypot(ax + t * dx - x, az + t * dz - z) <= grow: return True
    return False


def parking():
    gp = os.path.join(ED, 'parking-mapillary.json')
    old = load(gp); new = load(os.path.join(SRC, 'parking-all.json'))
    lots = list(old['lots']); add = 0
    for n in new['lots']:
        dup = any(math.dist(n['c'], o['c']) < 10 or hull_has(o['poly'], *n['c'], 3) or hull_has(n['poly'], *o['c']) for o in old['lots'])
        if not dup: lots.append(n); add += 1
    out = dict(new); out['lots'] = lots; out['segments'] = new['segments']
    out['meta'] = dict(new['meta']); out['meta']['n_lots'] = len(lots)
    out['meta']['merge'] = 'Tools/mapillary/spoji.py: the old lots first in their order (ids), then the new ones of parking-all.json that cover none of them'
    json.dump(out, open(gp, 'w', encoding='utf-8'), ensure_ascii=False, indent=0)
    print(f'parking: {len(old["lots"])} -> {len(lots)} parkirališta ({add} novih), {len(new["segments"])} rubnih segmenata')


if __name__ == '__main__':
    facades(); trees(); parking()
