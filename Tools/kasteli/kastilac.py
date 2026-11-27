# Kaštilac (Kaštel Gomilica): utvrđeni otočić — obodne kamene kuće (DGU dijelovi iz kasteli.json), benediktinski blok,
# sjeverna ulazna kula s mašikulama i kameni most s lukovima prema rivi.
import sys, math
sys.path.insert(0, '/Users/macair/Documents/BalkanDriver/tools/world/kasteli-blender')
import importlib, klib; importlib.reload(klib)
from klib import *

K = load('kastel_gomilica_kastilac')
FR = Frame(*K['position'])
PRE = FR.poly(polys(K['precinct'])[0][0])
parts = [p for p in K['notableParts'] if p.get('kind') == 'wing' and p.get('footprint')]
bridge = [p for p in K['notableParts'] if p.get('kind') == 'bridge'][0]
BR = FR.poly(bridge['footprint']['coordinates'][0])
# bridge axis: land end (north) -> island end (south)
bg = bridge['footprint']['coordinates'][0]
LAND = FR.p((bg[0][0] + bg[1][0]) / 2, (bg[0][1] + bg[1][1]) / 2)
ISL = FR.p((bg[2][0] + bg[3][0]) / 2, (bg[2][1] + bg[3][1]) / 2)
DIR = (ISL - LAND).normalized()               # pointing south into the island
TU = Vector((DIR.y, -DIR.x))                  # tower width axis
TW = 6.4
TC = ISL + DIR * (TW / 2 - .1)
TOWER = [TC - TU * TW / 2 - DIR * TW / 2, TC + TU * TW / 2 - DIR * TW / 2, TC + TU * TW / 2 + DIR * TW / 2, TC - TU * TW / 2 + DIR * TW / 2]
if area(TOWER) < 0: TOWER.reverse()

Z_PLAT = 1.0          # island walking level (sea = 0)
houses = []
for i, p in enumerate(parts):
    P = FR.poly(p['footprint']['coordinates'][0])
    if abs(area(P)) < 4: continue
    if inside(TOWER, centroid(P)) or any(inside(TOWER, q) for q in P if True) and abs(area(P)) < 40: continue
    h = p['heightM']
    if h >= 12: h = 11.2
    else: h = 6.4 + 3.2 * h01(i, 1.7)
    houses.append((i, P, h))

def near_pre(q, d=1.6):
    return any(seg_dist(q, PRE[j], PRE[(j + 1) % len(PRE)]) < d for j in range(len(PRE)))

def build(lod):
    m = Mesh(lod)
    # the islet: stone platform from the sea floor to the walking level, wet band at the waterline
    plat = offset(PRE, .55)
    m.prism(plat, -2.0, Z_PLAT, 'kamen', top=True, topmat='kamen', wet=.55)
    allP = [P for _, P, _ in houses] + [TOWER]
    for i, P, h in houses:
        others = [O for O in allP if O is not P]
        top = Z_PLAT + h
        wall = 'lomljeni' if h01(i, 2.2) < .35 else 'kamen'
        m.prism(P, Z_PLAT - .3, top, wall, top=(lod >= 2), wet=None)
        # cornice (stone eave course)
        if lod == 0: m.prism(offset(P, .08), top - .22, top, 'okvir', top=False)
        storeys = max(2, round((h - .6) / 2.9))
        if lod < 2:
            for a, b, ext in exterior_edges(P, others):
                if not ext: continue
                nrm = Vector(((b - a).y, -(b - a).x)).normalized()
                outer = near_pre((a + b) / 2) and nrm.dot(-DIR) < .5      # the sea sides: fortress walls, few windows
                windows_on_wall(m, a, b, Z_PLAT, storeys, seed=i * 31 + int(a.x * 7),
                                spacing=4.4 if outer else 3.3, density=.62 if outer else .9,
                                door_ground=not outer and (b - a).length > 3, small_ground=True,
                                w=.75 if outer else .85, h=1.1 if outer else 1.3)
        roofmat = 'ploce' if h01(i, 4.2) < .6 else 'crijep'
        if i == 3: roofmat = 'ploce'
        c, u, L, W = min_rect(P)
        fill = abs(area(P)) / (L * W)
        if fill > .8 and h01(i, 9) < .5:
            gu = u if h01(i, 11) < .7 else Vector((-u.y, u.x))
            GL, GW = (L, W) if gu is u else (W, L)
            m.gable_roof(c, gu, GL, GW, top, pitch=36, over=.25, mat=roofmat, gable_mat=wall)
        else:
            m.hip_roof(P, top, pitch=35 if roofmat == 'ploce' else 33, over=.28, mat=roofmat,
                       chimneys=1 if h01(i, 6) < .5 else 0, seed=i)
        # dormers on the long sea-facing walls of the bigger houses
        if lod < 2 and L > 7.5 and h01(i, 13) < .6:
            for a, b, ext in exterior_edges(P, others):
                if ext and (b - a).length > 6.5 and near_pre((a + b) / 2):
                    m.dormer(a, b, (b - a).length * (.35 + .3 * h01(i, 14)), top, mat=roofmat, wall=wall); break
    # ---- the entrance tower (6.4 m square, mašikule, open upper storey, hip roof, bell)
    z_body = Z_PLAT + 12.4
    m.prism(TOWER, -1.8, z_body, 'kamen', top=False, wet=.6)
    m.quoins(TOWER, Z_PLAT, z_body - 1.3)
    up = offset(TOWER, .5 if lod < 2 else .3)
    z_up = z_body + 3.4
    m.prism(up, z_body, z_up, 'kamen', top=(lod >= 2))
    if lod < 2:
        m.face([V3(p, z_body) for p in reversed(up)], 'kamen')   # underside of the projection
        for j in range(4):
            a, b = TOWER[j], TOWER[(j + 1) % 4]
            m.corbel_row(a, b, z_body, out=.5, spacing=.82, end_pad=.25)
            ua, ub = up[j], up[(j + 1) % 4]
            n_open = 3 if j in (0, 2) else 2
            for k in range(n_open):
                s = (ub - ua).length * (k + .5) / n_open
                m.window(ua, ub, s, z_body + 1.0, .72, 1.25, style='arch', shutters=None, frame=lod == 0)
            # loopholes down the body
            for zz in (Z_PLAT + 4.5, Z_PLAT + 8.2):
                m.slit(a, b, (b - a).length * .5, zz)
        # string course at the old drawbridge level
        if lod == 0: m.prism(offset(TOWER, .1), Z_PLAT + 5.2, Z_PLAT + 5.45, 'okvir', top=False)
    # face toward the bridge: the edge whose outward normal is -DIR
    fj = min(range(4), key=lambda j: (lambda a, b: Vector(((b - a).y, -(b - a).x)).normalized().dot(DIR))(TOWER[j], TOWER[(j + 1) % 4]))
    a, b = TOWER[fj], TOWER[(fj + 1) % 4]
    if lod < 2:
        m.window(a, b, (b - a).length / 2, Z_PLAT + .5, 1.5, 3.0, style='portal', shutters=None, closed=False)
        m.window(a, b, (b - a).length / 2, Z_PLAT + 6.2, .55, .8, shutters=None)
        if lod == 0:
            # anchor-tie cross ("X") and the inscription slab
            e = (b - a).normalized(); n = Vector((e.y, -e.x)); cpos = a + e * ((b - a).length / 2) + n * .05
            for sg in (-1, 1):
                for t in range(5):
                    q = cpos + e * (sg * (t - 2) * .22)
                    m.obox(q, e, .12, .06, Z_PLAT + 9.2 + t * .22, Z_PLAT + 9.42 + t * .22, 'metal')
            m.obox(a + e * ((b - a).length / 2) + n * .04, e, 1.2, .08, Z_PLAT + 3.8, Z_PLAT + 4.4, 'okvir')
    rtop = m.hip_roof(up, z_up, pitch=28, over=.35, mat='crijep', ridge_frac=.97)
    if lod == 0:
        # small bell frame on the ridge
        m.obox(TC, TU, .9, .12, rtop - .2, rtop + .9, 'metal')
        m.obox(TC, TU, .12, .12, rtop + .9, rtop + 1.5, 'metal')
    # ---- the bridge (deck slightly above the riva, 4 arches) + the wooden section at the tower
    deck = 1.45
    wood = 2.6
    m.bridge(LAND, ISL - DIR * wood, 3.6, deck, 4, z_water=-.2, parapet=.75, mat='lomljeni')
    if lod < 2:
        A2 = ISL - DIR * wood
        m.obox((A2 + ISL) / 2 + DIR * .05, DIR, wood + .1, 3.0, deck - .25, deck, 'drvo')
        for sg in (-1, 1):
            for t in range(3):
                q = A2 + DIR * (t * wood / 2) + TU * (sg * 1.4)
                m.obox(q, DIR, .12, .12, deck, deck + 1.0, 'drvo')
            m.obox((A2 + ISL) / 2 + TU * (sg * 1.4), DIR, wood, .1, deck + .9, deck + 1.0, 'drvo')
        # landing on the riva side
        m.obox(LAND - DIR * 1.0, DIR, 2.0, 4.4, -1.0, deck, 'kamen')
    return m

if __name__ == '__main__':
    F = FR
    cams = [
        ('more-istok', gcam(F, -3528, -4328, 1.8), gcam(F, -3600, -4338, 6.5), 50, 0),
        ('riva-zapad', gcam(F, -3730, -4352, 2.2), gcam(F, -3600, -4342, 6.5), 50, 0),
        ('sjever-most', gcam(F, -3614, -4404, 1.8), gcam(F, -3597, -4360, 8.0), 35, 0),
        ('pregled', gcam(F, -3520, -4440, 55), gcam(F, -3603, -4342, 0), 35, 0),
        ('lod1', gcam(F, -3528, -4328, 1.8), gcam(F, -3600, -4338, 6.5), 50, 1),
        ('lod2', gcam(F, -3528, -4328, 1.8), gcam(F, -3600, -4338, 6.5), 50, 2),
    ]
    run('kastilac', build, cams)
