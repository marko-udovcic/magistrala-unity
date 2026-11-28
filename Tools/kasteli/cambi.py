# Kaštel Cambi (Kaštel Kambelovac): cilindrična kula s izbačenim prstenom mašikula (lukovi na konzolama, parapet s malim
# otvorima, bez stožastog krova), prislonjena palača s dugim kamenim balkonom na konzolama, i četvrtasti kaštel uz mul.
import sys, math
sys.path.insert(0, '/Users/macair/Documents/BalkanDriver/tools/world/kasteli-blender')
import importlib, klib; importlib.reload(klib)
from klib import *

K = load('kastel_kambelovac_cambi')
FR = Frame(*K['position'])
parts = [p for p in K['notableParts'] if p.get('kind') == 'wing' and p.get('footprint')]
SQ = FR.poly(parts[0]['footprint']['coordinates'][0])      # square castle at the root of the mole
PAL = FR.poly(parts[1]['footprint']['coordinates'][0])     # palace block with the round tower

# the tower: the palace outline's curved run (its first six points) is the tower's outer face; radius 4.4 m (the 8-9 m ring
# on the orthophoto, kasteli.json roundTower.r), centre 4.4 m in from the middle of that arc
ring = parts[1]['footprint']['coordinates'][0][:6]
pts = [FR.p(x, z) for x, z in ring]
import numpy as np
A = np.array([[2 * p.x, 2 * p.y, 1] for p in pts]); bb = np.array([p.x ** 2 + p.y ** 2 for p in pts])
cx, cy, k = np.linalg.lstsq(A, bb, rcond=None)[0]
fc = Vector((cx, cy)); amid = pts[2] * .5 + pts[3] * .5
TR = 4.4
TC = amid - (amid - fc).normalized() * TR
print('TOWER c', TC, 'game', TC.x + FR.cx, -TC.y + FR.cz)
Z0 = 0.0

def build(lod):
    m = Mesh(lod)
    seg = 40 if lod == 0 else (20 if lod == 1 else 12)
    # ---------------- round tower
    z_ring = 12.0            # underside of the machicolation crown
    z_top = 14.8
    m.cyl(TC, TR, Z0 - 1.5, z_ring, seg, 'kamen', top=False)
    if lod < 2:
        # slight batter at the base
        m.cyl(TC, TR + .25, Z0 - 1.5, Z0 + 1.2, seg, 'kamen', top=False, r1=TR + .02)
    m.corbel_ring(TC, TR, z_ring + .0, out=.55, n=22)
    R2 = TR + .5
    m.cyl(TC, R2, z_ring, z_top, seg, 'zbuka' if lod < 2 else 'kamen', top=(lod >= 2))
    if lod < 2:
        # underside of the crown + inner parapet face + walk (flat, no conical roof)
        m.face([V3(TC + Vector((math.cos(2 * math.pi * i / seg), math.sin(2 * math.pi * i / seg))) * R2, z_ring) for i in reversed(range(seg))], 'kamen')
        inner = [TC + Vector((math.cos(2 * math.pi * i / seg), math.sin(2 * math.pi * i / seg))) * (R2 - .45) for i in range(seg)]
        for i in range(seg):
            a, b = inner[i], inner[(i + 1) % seg]
            m.quad(V3(b, z_top - 1.1), V3(a, z_top - 1.1), V3(a, z_top), V3(b, z_top), 'kamen')
        m.face([V3(p, z_top - 1.1) for p in inner], 'ploce')
        # top ring (coping, the brick-and-tile edge seen from below)
        outer = [TC + Vector((math.cos(2 * math.pi * i / seg), math.sin(2 * math.pi * i / seg))) * (R2 + .08) for i in range(seg)]
        for i in range(seg):
            a, b = outer[i], outer[(i + 1) % seg]; ai, bi = inner[i], inner[(i + 1) % seg]
            m.quad(V3(a, z_top), V3(b, z_top), V3(b, z_top + .18), V3(a, z_top + .18), 'crijep')
            m.quad(V3(a, z_top + .18), V3(b, z_top + .18), V3(bi, z_top + .1), V3(ai, z_top + .1), 'crijep')
        # small square openings in the crown + three windows stacked on the street side (east-south-east)
        for j in range(6):
            ang = math.radians(-60 + j * 60 + 15)
            d = Vector((math.cos(ang), math.sin(ang))); t = Vector((-d.y, d.x))
            a = TC + d * (R2 + .01) - t * .5; b = TC + d * (R2 + .01) + t * .5
            # edge a->b must have the outside on its right: right of (b - a) = right of t = -d ... use reversed
            m.window(a, b, .5, z_ring + 1.0, .55, .6, shutters=None, frame=False)
        ang = math.radians(-20)
        d = Vector((math.cos(ang), math.sin(ang))); t = Vector((-d.y, d.x))
        a = TC + d * (TR + .01) - t * .6; b = TC + d * (TR + .01) + t * .6
        for zz in (2.6, 5.4, 8.0):
            m.window(a, b, .6, Z0 + zz, .65, .95, shutters=None)
        m.window(a, b, .6, Z0 + .02, 1.2, 2.2, style='door', shutters=None)
    # ---------------- palace (P+2, long stone balcony on the east front, tile roof)
    others = [SQ]
    h_pal = 8.0
    m.prism(PAL, Z0 - 1.5, h_pal, 'kamen', top=(lod >= 2))
    if lod == 0: m.prism(offset(PAL, .08), h_pal - .25, h_pal, 'okvir', top=False)
    edges = exterior_edges(PAL, [])
    for a, b, ext in edges:
        L = (b - a).length
        nrm = Vector(((b - a).y, -(b - a).x)).normalized()
        mid = (a + b) / 2
        if (mid - TC).length < TR + .3 or L < 2.2: continue
        if nrm.x > .8 and L > 5:          # east front: the balcony
            m.balcony(a, b, L * .45, Z0 + 5.6, min(L - 1.5, 7.0), d=1.1)
            m.window(a, b, L * .45, Z0 + 5.6, 1.2, 2.0, shutters=None)
            m.window(a, b, L * .25, Z0 + 2.9, 1.0, 1.4, shutters=None)
            m.window(a, b, L * .75, Z0 + 2.9, 1.0, 1.4, shutters=None)
            for s in (L * .3, L * .7):
                m.window(a, b, s, Z0 + .02, 1.6, 2.4, style='door', shutters=None)
        elif lod < 2:
            windows_on_wall(m, a, b, Z0, 3, sh=3.0, spacing=3.2, seed=int(a.x * 13 + a.y), door_ground=True, w=.9, h=1.35, density=.85)
    c, u, L, W = min_rect(PAL)
    m.hip_roof(PAL, h_pal, pitch=27, over=.3, mat='crijep', chimneys=1, seed=7)
    # ---------------- the square castle by the mole (P+2, hip roof, corner quoins, sea portal)
    h_sq = 10.5
    m.prism(SQ, Z0 - 1.5, h_sq, 'kamen', top=(lod >= 2), wet=Z0 + .5)
    if lod == 0:
        m.prism(offset(SQ, .12), Z0 + 3.55, Z0 + 3.8, 'okvir', top=False)        # string course
        m.prism(offset(SQ, .12), h_sq - .3, h_sq, 'okvir', top=False)            # cornice
        for q in SQ:                                                               # corner quoins
            for k in range(int(h_sq / .7)):
                m.obox(q, Vector((1, 0)), .5 if k % 2 else .35, .5 if k % 2 == 0 else .35, Z0 + k * .7, Z0 + k * .7 + .62, 'okvir')
    for j, (a, b, ext) in enumerate(exterior_edges(SQ, [])):
        L = (b - a).length
        if lod >= 2: break
        nw = max(2, int(L / 3.6))
        for st in range(3):
            for k in range(nw):
                s = L * (k + .5) / nw
                if st == 0:
                    if k == nw // 2 and j % 2 == 0: m.window(a, b, s, Z0 + .02, 1.6, 2.8, style='portal', shutters=None, closed=True)
                    else: m.window(a, b, s, Z0 + 1.4, .7, .9, shutters=None)
                else:
                    m.window(a, b, s, Z0 + 4.4 + (st - 1) * 3.2, .95, 1.5, shutters='z' if (j + k) % 3 else 's', closed=(k + st) % 4 == 0)
        if j == 0 and lod == 0:
            # coat-of-arms panel over the portal side
            e = (b - a).normalized(); n = Vector((e.y, -e.x))
            m.obox(a + e * (L / 2) + n * .06, e, .9, .12, Z0 + 3.9, Z0 + 4.2, 'okvir')
            m.obox(a + e * (L / 2) + n * .08, e, .6, .16, Z0 + 4.2, Z0 + 5.0, 'okvir')
    m.hip_roof(SQ, h_sq, pitch=26, over=.4, mat='crijep', chimneys=2, seed=3)
    # ground pads (base courses visible above the riva)
    return m

if __name__ == '__main__':
    F = FR
    tg = lambda h: (TC.x, TC.y, h)
    cams = [
        ('ulica-kula', gcam(F, -4338, -4482, 1.7), tg(8.5), 24, 0),
        ('kula-blizu', gcam(F, -4344, -4474, 1.7), tg(10.5), 28, 0),
        ('riva', gcam(F, -4300, -4430, 2.0), gcam(F, -4356, -4458, 6), 35, 0),
        ('pregled', gcam(F, -4300, -4410, 45), gcam(F, -4356, -4460, 0), 35, 0),
        ('lod1', gcam(F, -4300, -4430, 2.0), gcam(F, -4356, -4458, 6), 35, 1),
        ('lod2', gcam(F, -4300, -4430, 2.0), gcam(F, -4356, -4458, 6), 35, 2),
    ]
    run('cambi', build, cams, scene_kw=dict(sea=True, ground=[Vector((-80, -80)), Vector((80, -80)), Vector((80, 80)), Vector((-80, 80))], ground_z=.01))
