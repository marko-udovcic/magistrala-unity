#!/usr/bin/env python3
"""Check the real riva difference and a mask with a hole, without Unity."""
import copy
import json
import sys
from pathlib import Path

# Run with the same DGU Python as cut_package.py (supports -I).
sys.path.insert(0, str(Path(__file__).resolve().parent))
from cut_package import cut
from shapely.geometry import Polygon, LineString, box, shape, mapping
from shapely.ops import unary_union

ROOT = Path(__file__).resolve().parents[2]
MASK = Path('/Users/macair/Documents/BalkanDriver/tools/world/krizanja/paket-2711/riva-maska.json')


def surface(mesh):
    v, t = mesh['v'], mesh['t']
    return unary_union([Polygon([(v[2*i],v[2*i+1]) for i in t[k:k+3]]) for k in range(0,len(t),3)])


def main():
    original = json.loads((ROOT/'Assets/Editor/riva.json').read_text())
    mask = json.loads(MASK.read_text())
    cover = unary_union([shape(m['footprint']) for m in mask['models']])
    result = cut(copy.deepcopy(original),mask)
    for section in ('paving','pontoons','parking'):
        for before, after in zip(original[section],result[section]):
            expected = surface(before).difference(cover)
            actual = surface(after)
            assert actual.symmetric_difference(expected).area < 1e-5, section
            assert actual.intersection(cover).area < 1e-6, section
            for r in after['rings']:
                pts=r['pts'] if r.get('open') else r['pts']+[r['pts'][0]]
                # A coincident boundary is allowed; no wall may cross mask interior.
                assert LineString(pts).intersection(cover.buffer(-1e-6)).length < 1e-5
            for ax,az,bx,bz in after.get('lines',[]):
                assert LineString([(ax,az),(bx,bz)]).buffer(.06).intersection(cover).area < 1e-6
    # A footprint hole must keep our surface; test a hole wholly inside an
    # original triangle (a vertex-only or centroid cut would lose it).
    holemask = Polygon([(1,1),(9,1),(9,9),(1,9)], holes=[[(4,4),(6,4),(6,6),(4,6)]])
    fixture = dict(paving=[dict(v=[0,0,10,0,10,10,0,10],t=[0,1,2,0,2,3],rings=[])],pontoons=[],parking=[],zones=[],trees=[])
    out = cut(fixture,dict(models=[dict(footprint=mapping(holemask))]))
    actual = surface(out['paving'][0])
    assert abs(actual.intersection(box(4,4,6,6)).area-4)<1e-8
    assert abs(actual.area-40)<1e-8
    print('PASS: exact surface difference, holes, surviving walls, parking lines; '+str(result['packageCut']))

if __name__=='__main__': main()
