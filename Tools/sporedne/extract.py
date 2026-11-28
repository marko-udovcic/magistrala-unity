#!/usr/bin/env python3
"""Extract twenty reviewed street mouths from our assumed lane package; no Unity required.
Run from the clone; source paths may be overridden. Output stays in package coordinates.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path

# Stable survey anchors in package metres; piece IDs are resolved from each source.
SITES = [
    ('Štafilić', 'Škabrnjska', 'Škabrnjska', [42.81, 91.57]),
    ('Štafilić', 'Put Zagoraca', 'Put Zagoraca', [230.19, -61.36]),
    ('Novi', 'Put Groblja (D8 ramps)', 'Put Groblja', [353.75, -828.6]),
    ('Novi', 'Nehajski put', 'Nehajski put', [389.32, -140.08]),
    ('Novi', 'Put sv. Josipa', 'Put sv. Josipa', [889.67, -280.76]),
    ('Stari', 'Vukovarska', 'Vukovarska', [1754.1, -572.39]),
    ('Stari', 'Ulica bana Jelačića', 'Ulica bana Jelačića', [1690.44, -549.1]),
    ('Stari', 'Put Štalija', 'Put Štalija', [2201.04, -536.87]),
    ('Lukšić', 'Ulica Frane Ćipina', 'Ulica Frane Ćipina', [2555.57, -562.74]),
    ('Lukšić', 'Uz sv. Ivana', 'Ulica uz Svetog Ivana', [2804.35, -586.07]),
    ('Lukšić', 'Put sv. Lovre (D8)', 'Put sv. Lovre', [3337.7, -1089.5]),
    ('Kambelovac', 'Ulica kralja Krešimira', 'Ulica kralja Krešimira', [4963.68, -147.59]),
    ('Kambelovac', 'Ulica don Klementa Tadina', 'Ulica don Klementa Tadina', [5048.46, -130.6]),
    ('Gomilica', 'Kupališni prilaz', 'Kupališni prilaz', [5973.8, -1.86]),
    ('Gomilica', 'Ulica fra Fulgencija Careva', 'Ulica fra Fulgencija Careva', [6126.9, 36.99]),
    ('Gomilica', 'Put Kozjaka', 'Put Kozjaka', [6771.91, 155.22]),
    ('Sućurac', 'Ulica satnije Putalj', 'Ulica satnije Putalj', [7119.54, 143.69]),
    ('Sućurac', 'Tamunić', 'Tamunić', [8041.05, 118.17]),
    ('Gomilica', 'Južni prilaz (D8)', 'Južni krak', [6355.63, -569.63]),
    ('Sućurac', 'Putaljski put', 'Putaljski put', [8308.8, 172.25]),
]


def length(pts):
    return sum(math.dist(a, b) for a, b in zip(pts, pts[1:]))

def clip(pts, distance):
    out = [pts[0]]
    for a, b in zip(pts, pts[1:]):
        d = math.dist(a, b)
        if d < .001:
            continue
        if d >= distance:
            out.append([round(x + (y-x)*distance/d, 3) for x, y in zip(a, b)])
            break
        out.append(b)
        distance -= d
    return out

def main_lane(p):
    n = p.get('name', '')
    return p['kind'] == 'lane' and (n.startswith('D8 prema') or 'Tuđmana' in n or n == 'Cesta pape Ivana Pavla II.')

def extract(net):
    D = {p['id']: p for p in net['pieces']}
    def depth(p, inbound):
        pts = list(reversed(p['points'])) if inbound else list(p['points'])
        ids = [p['id']]
        for _ in range(12):
            if length(pts) >= 100:
                break
            choices = [D[i] for i in p['prev' if inbound else 'next'] if i not in ids
                       and not main_lane(D[i]) and D[i].get('move', 'through') == 'through'
                       and D[i].get('end') != 'closed']
            if not choices:
                break
            p = max(choices, key=lambda q: q['length'])
            q = list(reversed(p['points'])) if inbound else p['points']
            if math.dist(pts[-1], q[0]) > 5:
                # Outside lanes can still overlap the new short arm (Tamunić).
                # Trim only the explicitly linked continuation to the arm end.
                best = None
                for k, (a, b) in enumerate(zip(q, q[1:])):
                    v = [b[j]-a[j] for j in range(3)]
                    t = max(0, min(1, sum((pts[-1][j]-a[j])*v[j] for j in range(3))/max(1e-9,sum(x*x for x in v))))
                    hit = [a[j]+t*v[j] for j in range(3)]
                    d = math.dist(pts[-1], hit)
                    if best is None or d < best[0]: best = (d,k,hit)
                if best is None or best[0] > 5:
                    break
                q = [best[2]] + q[best[1]+1:]
            ids.append(p['id'])
            pts += q
        if length(pts) < 20:
            return None
        return clip(pts, 100), ids

    candidates = []
    for p in D.values():
        if p['kind'] != 'conn' or not p['prev'] or not p['next']:
            continue
        a, b = D[p['prev'][0]], D[p['next'][0]]
        if main_lane(a) == main_lane(b):
            continue
        inbound = main_lane(b)
        side, road = (a, b) if inbound else (b, a)
        if side['kind'] != 'lane' or side.get('end') == 'closed':
            continue
        # D8 medians: only supplied right/slip movements, never invent a crossing.
        if 'D8' in road.get('name', '') and p.get('move') == 'left':
            continue
        dep = depth(side, inbound)
        if dep is None:
            continue
        pts, ids = dep
        conn = p['points']
        pts = list(reversed(pts)) + conn if inbound else conn + pts
        candidates.append((p, side, road, inbound, pts, ids))

    out = []
    for site, (town, label, base, mouth) in enumerate(SITES):
        anchor = [mouth[0], 0, mouth[1]]
        selected = []
        for c, side, road, inbound, pts, ids in candidates:
            if (not side.get('name', '').startswith(base)) or math.hypot(anchor[0]-c['points'][0][0], anchor[2]-c['points'][0][2]) > 125:
                continue
            # Model 01's dedicated right slip has no connector at its start; handled below.
            selected.append(dict(site=site, town=town, name=label, source=c['id'], pieces=ids,
                                 inbound=inbound, d8='D8' in road.get('name', ''),
                                 indicator=-1 if c.get('move') == 'left' else 1,
                                 connectorLength=round(length(c['points']), 3),
                                 signal=c.get('signal', {}).get('group', ''), model=c['model'], points=pts))
        # Dedicated D8 slips have no connector at their handoff. Resolve by name,
        # direction and position, never by the changing network piece IDs.
        def lane(name, mouth, inbound):
            choices = [p for p in D.values() if p['kind'] == 'lane' and p.get('name', '').startswith(name)
                       and math.hypot(p['points'][-1 if inbound else 0][0]-mouth[0],
                                      p['points'][-1 if inbound else 0][2]-mouth[1]) < 8]
            assert len(choices) == 1, (name, mouth, [p['id'] for p in choices])
            return choices[0]
        if site in (2, 10, 18):
            if site == 2:
                side = lane('Put Groblja, prema sjeveru', (392.3, -832.3), True)
                # Follow its explicit supplied slip through to the D8 lane.
                chain = []
                q = side
                for _ in range(5):
                    choices = [D[i] for i in q['next']]
                    assert len(choices) == 1
                    q = choices[0]; chain.append(q)
                    if q['kind'] == 'lane': break
                pts, ids = depth(side, True)
                tail = [point for q in chain for point in q['points']]
                selected.append(dict(site=site, town=town, name=label, source=chain[0]['id'],
                    pieces=ids+[q['id'] for q in chain], inbound=True, d8=True, indicator=1,
                    connectorLength=round(length(tail),3), signal='', model=side['model'],
                    points=list(reversed(pts))+tail))
            else:
                name, mouth = ('Odvojak desno: D8 → jug', (3209.21, -1109.86)) if site == 10 else ('Odvojak desno: D8 → jug', (6224.48, -621.75))
                side = lane(name, mouth, False)
                pts, ids = depth(side, False)
                selected.append(dict(site=site, town=town, name=label, source=side['id'], pieces=ids,
                    inbound=False, d8=True, indicator=1, connectorLength=0, signal='', model=side['model'], points=pts))
        assert selected, (town, label)
        out += selected
    return out

def run():
    ap = argparse.ArgumentParser()
    ap.add_argument('--source', type=Path, default=Path.home()/'Documents/mag-27-11/promet/promet_mreza_sve.json')
    ap.add_argument('--output', type=Path, default=Path('Assets/Editor/sporedne-routes.json'))
    args = ap.parse_args()
    raw = args.source.read_bytes()
    net = json.loads(raw)
    routes = extract(net)
    # 72–79 provide the eastern old-road lanes; no overlapping assumed corridor.
    mains = []
    args.output.write_text(json.dumps(dict(note='Assumed lanes, not surveyed. Package frame; transform with paket-approaches.json. OSM contributors, ODbL.',
                                          sha256=hashlib.sha256(raw).hexdigest(), easternMain=mains, routes=routes), ensure_ascii=False, separators=(',', ':'))+'\n')
    print(f'{len(SITES)} street mouths, {len(routes)} movements -> {args.output}')
    for i, (town, name, _, _) in enumerate(SITES):
        r = [r for r in routes if r['site'] == i]
        print(i, town, name, [(q['source'], 'in' if q['inbound'] else 'out', q['signal']) for q in r])
if __name__ == '__main__':
    run()
