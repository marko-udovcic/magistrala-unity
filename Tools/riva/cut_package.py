#!/usr/bin/env python3
"""Build-time riva difference. Input stays untouched; requires the existing DGU Shapely environment."""
import argparse
import base64
import gzip
import json
from pathlib import Path

import numpy as np
import shapely
from shapely.geometry import Polygon, LineString, Point, box, shape
from shapely.ops import unary_union


def polygons(g):
    if g.geom_type == 'Polygon':
        if g.area > 1e-9:
            yield g
    elif hasattr(g, 'geoms'):
        for p in g.geoms:
            yield from polygons(p)


def cut(data, mask):
    cover = unary_union([shape(m['footprint']) for m in mask['models']])
    assert cover.is_valid
    removed = {}
    for section in ('paving', 'pontoons', 'parking'):
        removed[section] = 0
        for mesh in data[section]:
            v, t = mesh['v'], mesh['t']
            outv, outt, ids = [], [], {}
            def vertex(p):
                p = tuple(p[:2])
                if p not in ids:
                    ids[p] = len(ids); outv.extend(p)
                return ids[p]
            # Cut each original triangle: retain the existing fine height mesh,
            # constrained triangulation preserves every hole in the difference.
            for k in range(0, len(t), 3):
                tri = Polygon([(v[2*i], v[2*i+1]) for i in t[k:k+3]])
                clipped = tri.difference(cover)
                removed[section] += tri.area - clipped.area
                for p in polygons(clipped):
                    for q in shapely.constrained_delaunay_triangles(p).geoms:
                        outt.extend(vertex(c) for c in list(q.exterior.coords)[:3])
            mesh['v'], mesh['t'] = outv, outt
            # Keep ONLY surviving portions of the original walls/kerbs. New cut
            # edges meet a model: drawing a fresh wall there would block the seam.
            rings = mesh.get('rings', [{'pts': mesh.get('ring', []), 'sea': [0]*len(mesh.get('ring', []))}])
            outr = []
            for r in rings:
                pts = r['pts']
                boundary = LineString(pts + [pts[0]]) if pts else LineString()
                if not boundary.intersects(cover):
                    outr.append(r); continue
                runs = []
                for i, a in enumerate(pts):
                    b = pts[(i+1) % len(pts)]
                    line = LineString([a,b]).difference(cover)
                    parts = list(line.geoms) if hasattr(line, 'geoms') else [line]
                    for part in parts:
                        if part.is_empty or part.length < 1e-6: continue
                        coords = list(part.coords)
                        if runs and Point(runs[-1]['pts'][-1]).distance(Point(coords[0])) < 1e-7:
                            runs[-1]['pts'].extend(coords[1:]); runs[-1]['sea'].append(r['sea'][i])
                        else:
                            runs.append(dict(pts=coords, sea=[r['sea'][i]]))
                if len(runs)>1 and Point(runs[-1]['pts'][-1]).distance(Point(runs[0]['pts'][0])) < 1e-7:
                    last=runs.pop(); runs[0]=dict(pts=last['pts']+runs[0]['pts'][1:],sea=last['sea']+runs[0]['sea'])
                for run in runs:
                    if Point(run['pts'][0]).distance(Point(run['pts'][-1])) < 1e-7:
                        run['pts'].pop()
                    else:
                        run['open']=True; run['sea'].append(0)
                    outr.append(run)
            mesh['rings'] = outr
            if 'lines' in mesh:
                lines = []
                for ax,az,bx,bz in mesh['lines']:
                    line = LineString([(ax,az),(bx,bz)]).difference(cover.buffer(.075))
                    for part in (line.geoms if hasattr(line,'geoms') else [line]):
                        if not part.is_empty and part.length > .05:
                            a,b = part.coords[0],part.coords[-1]; lines.append([*a,*b])
                mesh['lines'] = lines
    data['trees'] = [t for t in data['trees'] if not box(t[0]-.8,t[1]-.8,t[0]+.8,t[1]+.8).intersects(cover)]
    # Existing classification is a 1 m raster. Clear all bits under the mask,
    # including crown/parking flags, while keeping its holes intact.
    for z in data['zones']:
        raw = bytearray(gzip.decompress(base64.b64decode(z['grid'])))
        xs = z['x0'] + np.arange(z['nx']) + .5
        for j in range(z['nz']):
            covered = shapely.intersects_xy(cover, xs, z['z0']+j+.5)
            for i in np.flatnonzero(covered): raw[j*z['nx']+int(i)] = 0
        z['grid'] = base64.b64encode(gzip.compress(bytes(raw), mtime=0)).decode()
    data['packageCut'] = dict(package='paket-2711', removedArea=removed)
    return data


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('source', type=Path); ap.add_argument('mask', type=Path); ap.add_argument('output',type=Path)
    a = ap.parse_args()
    d = cut(json.loads(a.source.read_text()),json.loads(a.mask.read_text()))
    a.output.write_text(json.dumps(d, ensure_ascii=False,separators=(',',':'))+'\n')
    print(json.dumps(d['packageCut']))

if __name__ == '__main__': main()
