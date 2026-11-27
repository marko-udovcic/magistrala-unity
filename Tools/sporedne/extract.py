#!/usr/bin/env python3
"""Extract twenty reviewed street mouths from our assumed lane package; no Unity required.
Run from the clone; source paths may be overridden. Output stays in package coordinates.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path

SITES = [
    ('Štafilić', 'Škabrnjska', 125),
    ('Štafilić', 'Put Zagoraca', 139),
    ('Novi', 'Put Groblja (D8 ramps)', 168),
    ('Novi', 'Nehajski put', 195),
    ('Novi', 'Put sv. Josipa', 313),
    ('Stari', 'Ulica Ivana Danila', 374),
    ('Stari', 'Ulica bana Jelačića', 467),
    ('Stari', 'Put Štalija', 524),
    ('Lukšić', 'Ulica Frane Ćipina', 551),
    ('Lukšić', 'Janka Rodina', 590),
    ('Lukšić', 'Put sv. Lovre (D8)', 697),
    ('Kambelovac', 'Ulica kralja Krešimira', 31105),
    ('Kambelovac', 'Ulica don Klementa Tadina', 31115),
    ('Gomilica', 'Kupališni prilaz', 31122),
    ('Gomilica', 'Ulica fra Fulgencija Careva', 30939),
    ('Gomilica', 'Put Kozjaka', 30933),
    ('Sućurac', 'Ulica satnije Putalj', 30962),
    ('Sućurac', 'Tamunić', 30981),
    ('Gomilica', 'Južni prilaz (D8)', 859),
    ('Sućurac', 'Putaljski put', 31131),
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
                break
            ids.append(p['id'])
            pts += q
        if length(pts) < 60:
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
    for site, (town, label, seed) in enumerate(SITES):
        p = D[seed]
        seed_side = D[p['prev'][0]] if main_lane(D[p['next'][0]]) else D[p['next'][0]]
        base = seed_side.get('name', '').split(',')[0]
        anchor = p['points'][0]
        selected = []
        for c, side, road, inbound, pts, ids in candidates:
            if (not side.get('name', '').startswith(base)) or math.dist(anchor, c['points'][0]) > 125:
                continue
            # Model 01's dedicated right slip has no connector at its start; handled below.
            selected.append(dict(site=site, town=town, name=label, source=c['id'], pieces=ids,
                                 inbound=inbound, d8='D8' in road.get('name', ''),
                                 indicator=-1 if c.get('move') == 'left' else 1,
                                 connectorLength=round(length(c['points']), 3),
                                 signal=c.get('signal', {}).get('group', ''), model=c['model'], points=pts))
        if seed == 168:
            pts, ids = depth(D[164], True)
            selected.append(dict(site=site, town=town, name=label, source=171, pieces=ids + [171, 165],
                                 inbound=True, d8=True, indicator=1, connectorLength=24.6, signal='',
                                 model=D[171]['model'], points=list(reversed(pts)) + D[171]['points'] + D[165]['points']))
        if seed in (697, 859):
            c, side = (D[660], D[685]) if seed == 697 else (D[828], D[849])
            pts, ids = depth(side, False)
            selected.append(dict(site=site, town=town, name=label, source=side['id'], pieces=ids,
                                 inbound=False, d8=True, indicator=1, connectorLength=0,
                                 signal='', model=side['model'], points=pts))
        assert selected, (town, label)
        out += selected
    return out

def run():
    ap = argparse.ArgumentParser()
    ap.add_argument('--source', type=Path, default=Path.home()/'Documents/mag-25-11/promet/promet_mreza_sve.json')
    ap.add_argument('--output', type=Path, default=Path('Assets/Editor/sporedne-routes.json'))
    args = ap.parse_args()
    raw = args.source.read_bytes()
    net = json.loads(raw)
    routes = extract(net)
    D = {p['id']: p for p in net['pieces']}
    mains = []
    for seed in (30696, 30760):
        p, points, seen = D[seed], [], set()
        for _ in range(200):
            if p['id'] in seen:
                break
            seen.add(p['id'])
            points += p['points']
            choices = [D[i] for i in p['next'] if main_lane(D[i]) or
                       (D[i].get('move') == 'through' and any(main_lane(D[j]) for j in D[i]['next']))]
            if not choices:
                break
            p = max(choices, key=lambda q: q['length'])
        # Keep the existing s13 works gap, and end before the eastern world edge.
        points = [p for p in points if 3322 <= p[0] <= 8900]
        assert length(points) > 5000
        mains.append(points)
    args.output.write_text(json.dumps(dict(note='Assumed lanes, not surveyed. Package frame; transform with paket-approaches.json. OSM contributors, ODbL.',
                                          sha256=hashlib.sha256(raw).hexdigest(), easternMain=mains, routes=routes), ensure_ascii=False, separators=(',', ':'))+'\n')
    print(f'{len(SITES)} street mouths, {len(routes)} movements -> {args.output}')
    for i, (town, name, _) in enumerate(SITES):
        r = [r for r in routes if r['site'] == i]
        print(i, town, name, [(q['source'], 'in' if q['inbound'] else 'out', q['signal']) for q in r])
if __name__ == '__main__':
    run()
