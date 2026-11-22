#!/usr/bin/env python3
"""RoadRouter reproduction against the baked runtime graph; standard library only."""
import heapq
import json
import math
from pathlib import Path
import random
import re

ROOT = Path(__file__).resolve().parents[2]
ASSET = ROOT / 'Assets/Generated/Game/Resources/MagistralaGame.asset'


def vector(s):
    return tuple(float(v) for v in re.findall(r'[xyz]: ([^,}]+)', s))


def load():
    sections = {}
    section = None
    for line in ASSET.read_text().splitlines():
        if line in ('  stops:', '  nodes:', '  edges:', '  pts:'):
            section = line.strip()[:-1]
            sections[section] = []
        elif re.match(r'^  [a-zA-Z]', line):
            section = None
        elif section in ('nodes', 'pts') and line.startswith('  - {'):
            sections[section].append(vector(line))
        elif section in ('stops', 'edges'):
            if line.startswith('  - '):
                sections[section].append({})
                line = '    ' + line[4:]
            if line.startswith('    ') and ': ' in line:
                k, v = line.strip().split(':', 1)
                v = v.strip()
                if v.startswith('{'):
                    v = vector(v)
                elif v.startswith('"'):
                    v = json.loads(re.sub(r'\\x([0-9a-fA-F]{2})', r'\\u00\1', v))
                else:
                    try:
                        v = float(v) if '.' in v else int(v)
                    except ValueError:
                        pass
                sections[section][-1][k] = v
    for e in sections['edges']:
        e['p'] = sections['pts'][e['p0']:e['p0'] + e['pn']]
    return sections


def flat(a, b):
    return math.hypot(a[0]-b[0], a[2]-b[2])


class Router:
    def __init__(self, world):
        self.w = world
        self.edges = world['edges']
        self.adj = [[] for _ in world['nodes']]
        for i, e in enumerate(self.edges):
            if e['closed']:
                continue
            self.adj[e['a']].append((i, True))
            if not e['oneway']:
                self.adj[e['b']].append((i, False))

    def nearest(self, pos, fwd):
        best = None
        norm = math.hypot(fwd[0], fwd[2]) or 1
        for i, e in enumerate(self.edges):
            if e['closed']:
                continue
            s = 0
            for k, (a, b) in enumerate(zip(e['p'], e['p'][1:])):
                dx, dz = b[0]-a[0], b[2]-a[2]
                length = math.hypot(dx, dz)
                t = max(0, min(1, ((pos[0]-a[0])*dx+(pos[2]-a[2])*dz)/(length*length))) if length > .001 else 0
                p = tuple(x+(y-x)*t for x, y in zip(a,b))
                d = flat(pos, p)
                if e['oneway'] and length > .001 and (dx*fwd[0]+dz*fwd[2])/length/norm < -.3:
                    d += 8
                if best is None or d < best['dist']:
                    best = dict(edge=i, seg=k, t=t, s=s+length*t, dist=d, p=p)
                s += length
        return best

    def length(self, e):
        return e['length'] if e['length'] > 0 else math.dist(e['p'][0], e['p'][-1])

    def search(self, sp, fwd, ta, tb=-1):
        e = self.edges[sp['edge']]
        a, b = e['p'][sp['seg']:sp['seg']+2]
        heading = (b[0]-a[0])*fwd[0]+(b[2]-a[2])*fwd[2] >= 0
        d = [math.inf]*len(self.adj)
        pe, pf = [-1]*len(d), [False]*len(d)
        q = []
        def seed(u, cost):
            if cost < d[u]:
                d[u] = cost
                heapq.heappush(q, (cost,u))
        seed(e['b'], self.length(e)-sp['s']+(0 if heading else 240))
        if not e['oneway']:
            seed(e['a'], sp['s']+(240 if heading else 0))
        done = set()
        while q:
            cost,u = heapq.heappop(q)
            if u in done:
                continue
            done.add(u)
            if (u == ta and (tb < 0 or tb in done)) or (u == tb and ta in done):
                break
            for i,f in self.adj[u]:
                x = self.edges[i]
                v = x['b'] if f else x['a']
                nd = cost+self.length(x)+(240 if pe[u] == i else 0)
                if nd < d[v]:
                    d[v],pe[v],pf[v] = nd,i,f
                    heapq.heappush(q, (nd,v))
        return d,pe,pf

    def assemble(self, sp, target, pe, pf):
        chain = []
        v = target
        while pe[v] >= 0:
            i,f = pe[v],pf[v]
            chain.append((i,f))
            v = self.edges[i]['a' if f else 'b']
            assert len(chain) <= 4000
        chain.reverse()
        e = self.edges[sp['edge']]
        line = [sp['p']]
        line += e['p'][sp['seg']+1:] if v == e['b'] else e['p'][:sp['seg']+1][::-1]
        for i,f in chain:
            p = self.edges[i]['p']
            line += p[1:] if f else p[:-1][::-1]
        return line,chain

    def route(self, start, goal, waypoint=False):
        pos = start['bay']
        fwd = (-start['bayDir'][0], 0, -start['bayDir'][1])
        sp = self.nearest(pos,fwd)
        if waypoint:
            gp = self.nearest(goal['bay'], (0,0,0))
            ge = self.edges[gp['edge']]
            if sp['edge'] == gp['edge']:
                a,b = ge['p'][sp['seg']:sp['seg']+2]
                heading = (b[0]-a[0])*fwd[0]+(b[2]-a[2])*fwd[2] >= 0
                fw = gp['s'] >= sp['s'] and (heading or ge['oneway'])
                bw = not ge['oneway'] and gp['s'] <= sp['s'] and not heading
                if fw or bw:
                    line = [sp['p']] + (ge['p'][sp['seg']+1:gp['seg']+1] if fw else ge['p'][gp['seg']+1:sp['seg']+1][::-1]) + [gp['p']]
                    return sum(math.dist(a,b) for a,b in zip(line,line[1:])),sp,[]
            d,pe,pf = self.search(sp,fwd,ge['a'], -1 if ge['oneway'] else ge['b'])
            glen = sum(flat(a,b) for a,b in zip(ge['p'],ge['p'][1:]))
            via_a,via_b = d[ge['a']]+gp['s'], math.inf if ge['oneway'] else d[ge['b']]+glen-gp['s']
            from_a = via_a <= via_b
            target = ge['a'] if from_a else ge['b']
            if math.isinf(min(via_a,via_b)):
                return math.inf,sp,[]
            line,chain = self.assemble(sp,target,pe,pf)
            line += ge['p'][1:gp['seg']+1] if from_a else ge['p'][gp['seg']+1:-1][::-1]
            line.append(gp['p'])
        else:
            target = goal['padNode'] if goal['padNode'] >= 0 else goal['accessNode']
            d,pe,pf = self.search(sp,fwd,target)
            if math.isinf(d[target]):
                return math.inf,sp,[]
            line,chain = self.assemble(sp,target,pe,pf)
            line.append(goal['bay'])
        return sum(math.dist(a,b) for a,b in zip(line,line[1:])),sp,chain


# These are producer IDs, never baked array indices.
ARM = -7000000096509
CONTINUATION = -7000000096516


def source_graph(region):
    """GameDataBuilder's atomic override application and closure precedence."""
    graph = json.loads((region / 'graph.json').read_text())
    nodes = {n['id']: n for n in graph['nodes']}
    edges = {e['id']: e for e in graph['edges']}
    entries = {}
    files = ['junction-overrides.json']
    index = region / 'junction-overrides.index.json'
    if index.exists():
        files += json.loads(index.read_text())
    for filename in files:
        path = region / filename
        if path.exists():
            for entry in json.loads(path.read_text())['overrides']:
                entries[entry['id']] = entry
    applied = skipped = 0
    for key, entry in entries.items():
        override = entry.get('overrides')
        anchor = None
        if key.startswith('graph-node-'):
            anchor = nodes.get(int(key[11:].split('#')[0]))
        if anchor is None and entry.get('at'):
            x,z = entry['at']
            bd = 25
            for node in nodes.values():
                d = (node['x']-x)**2+(node['z']-z)**2
                if d < bd:
                    bd,anchor = d,node
        valid = override is not None and anchor is not None
        if valid and 'arms' in entry.get('signature', {}):
            valid = sum(e['a'] == anchor['id'] or e['b'] == anchor['id'] for e in edges.values()) == entry['signature']['arms']
        if valid:
            valid = (all(n['id'] in nodes for n in override.get('nodes', []))
                     and all(e['id'] in edges for e in override.get('edges', []))
                     and all(e in edges for e in override.get('removeEdges', []))
                     and all(n['id'] not in nodes for n in override.get('addNodes', []))
                     and all(e['id'] not in edges for e in override.get('addEdges', [])))
        if not valid:
            skipped += 1
            continue
        applied += 1
        for n in override.get('nodes', []):
            nodes[n['id']].update(n)
        for e in override.get('edges', []):
            edges[e['id']].update(e)
        for n in override.get('addNodes', []):
            nodes[n['id']] = n
        for e in override.get('addEdges', []):
            edges[e['id']] = e
        for eid in override.get('removeEdges', []):
            del edges[eid]
    file_closed = set(json.loads((region/'road-closures.json').read_text())['closedEdgeIds'])
    print(f'Source overrides: {applied} applied, {skipped} skipped; {len(file_closed)} file closures')
    return edges, file_closed


def repair(world, source):
    arm, continuation = source[ARM], source[CONTINUATION]
    assert arm['b'] == continuation['a']
    assert arm['name'] == continuation['name'] == 'Lindlarska ulica'
    assert not arm['oneway'] and not continuation['oneway'] and continuation.get('closed') is not True
    # Match baked geometry to the original ID; fitting heights does not change x/z.
    def same_geometry(e, original):
        def unity(p):
            return p[0]+4200, p[1], -p[2]-5300
        return (e['name'] == original['name']
                and flat(e['p'][0], unity(original['points'][0])) < .02
                and flat(e['p'][-1], unity(original['points'][-1])) < .02)
    arms = [i for i,e in enumerate(world['edges']) if same_geometry(e, arm)]
    continuations = [i for i,e in enumerate(world['edges']) if same_geometry(e, continuation)]
    assert len(arms) == len(continuations) == 1, (arms, continuations)
    i,j = arms[0],continuations[0]
    assert world['edges'][i]['b'] == world['edges'][j]['a']
    assert not world['edges'][j]['closed']
    world['edges'][i]['closed'] = 0
    print(f'Repair: source {ARM} (baked edge {i}), {arm["length"]:.2f} m, continued by {CONTINUATION} (baked {j})')


def main():
    import argparse
    import copy
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--region', type=Path, default=Path('/Users/macair/Documents/BalkanDriver/client/public/assets/world/split-kastela'))
    args = parser.parse_args()
    source, file_closed = source_graph(args.region)
    assert ARM not in file_closed, 'explicit road closure must not be reopened'
    print(f'Arm closed={source[ARM].get("closed")}; in road-closures.json={ARM in file_closed}')
    world = load()
    before = Router(world)
    fixed = copy.deepcopy(world)
    repair(fixed, source)
    after = Router(fixed)
    stops = {s['id']:s for s in world['stops']}
    pairs = [('kastela_skladiste', 'dvor_basica'), ('dvor_basica', 'kastela_skladiste')]
    rng = random.Random(20261122)
    ordinary = [s['id'] for s in world['stops'] if s['pid'] == -1 and s['kind'] != 'radar']
    # Sample reachable pairs; some source stops are intentionally cut off by closures.
    attempts = 0
    while len(pairs) < 5:
        attempts += 1
        assert attempts < 100
        pair = tuple(rng.sample(ordinary, 2))
        if pair not in pairs and all(math.isfinite(before.route(stops[pair[0]],stops[pair[1]],wp)[0]) for wp in (True,False)):
            pairs.append(pair)
    print('Stops start at bay centres, facing away from bayDir (PlaceAtStop). Goal: bay snapped with zero heading for RouteTo; pad/access + bay tail for Route.')
    print('No explicit turn restrictions exist in GEdge/RoadRouter: only oneway, closed and 240 m U-turn costs. Reported metres are assembled 3D polyline lengths, not penalized search costs.')
    print('pair | RouteTo before -> after (m) | Route before -> after (m)')
    for a,b in pairs:
        values = []
        for wp in (True,False):
            old,sp,_ = before.route(stops[a],stops[b],wp)
            new,sp2,_ = after.route(stops[a],stops[b],wp)
            assert sp == sp2, 'repair unexpectedly changed start snapping'
            assert math.isfinite(old) and math.isfinite(new)
            assert new <= old + .1, (a,b,old,new)
            values.append(f'{old:.1f} -> {new:.1f}')
        print(f'{a} -> {b} | ' + ' | '.join(values))
    # This is a generation repair: the checked-in asset is deliberately unchanged.
    assert sum(a['closed'] != b['closed'] for a,b in zip(world['edges'],fixed['edges'])) <= 1


if __name__ == '__main__':
    main()
