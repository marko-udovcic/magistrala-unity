# Dvorac Vitturi (Kaštel Lukšić): renesansni dvorac oko dvorišta; južno krilo P+2 uz more (morski portal, balkon, trifora
# između dva lučna prozora, četverostrešni krov), dvije kvadratne kule na sjevernim uglovima, sjeverno pročelje s portalom,
# mašikulom iznad njega i kamenim mostom s jednim lukom preko jarka.
import sys, math
sys.path.insert(0, '/Users/macair/Documents/BalkanDriver/tools/world/kasteli-blender')
import importlib, klib; importlib.reload(klib)
from klib import *

K = load('kastel_luksic_vitturi')
FR = Frame(*K['position'])
g = lambda x, z: FR.p(x, z)
SW, SE = g(-6157.74, -4697.16), g(-6134.86, -4695.2)
U = (SE - SW).normalized(); V = Vector((-U.y, U.x))          # U east along the south front, V north
ST = lambda p: ((p - SW).dot(U), (p - SW).dot(V))
P2 = lambda s, t: SW + U * s + V * t
S = (SE - SW).length
# courtyard corners (inner corners of the two L-shaped DGU parts)
cy = [ST(g(x, z)) for x, z in ((-6138.86, -4705.89), (-6137.9, -4714.7), (-6149.91, -4716.02), (-6150.89, -4707.21))]
sc0 = min(c[0] for c in cy); sc1 = max(c[0] for c in cy); tc0 = min(c[1] for c in cy); tc1 = max(c[1] for c in cy)
tN = max(ST(g(-6152.44, -4720.85))[1], ST(g(-6144.23, -4720.01))[1])
print('S', S, 'court s', sc0, sc1, 't', tc0, tc1, 'north', tN)
rect = lambda s0, s1, t0, t1: [P2(s0, t0), P2(s1, t0), P2(s1, t1), P2(s0, t1)]
TW = 5.6
NW_T = rect(-.6, -.6 + TW, tN - 3.0, tN - 3.0 + TW)      # NW tower projects north
NE_T = rect(S - TW + 2.2, S + 2.2, tN - TW + 1.2, tN + 1.2)  # NE tower projects east
H_S, H_W, H_T = 12.6, 12.0, 14.2

def build(lod):
    m = Mesh(lod)
    south = rect(0, S, 0, tc0)
    west = rect(0, sc0, tc0 - .01, tN)
    east = rect(sc1, S, tc0 - .01, tN)
    north = rect(sc0 - .01, sc1 + .01, tc1, tN)
    for P, h in ((south, H_S), (west, H_W), (east, H_W), (north, H_W)):
        m.prism(P, -1.5, h, 'kamen', top=(lod >= 2), wet=None)
        if lod == 0: m.prism(offset(P, .1), h - .3, h, 'okvir', top=False)
    m.quoins(south, .45, H_S - .35)
    if lod == 0:
        m.prism(offset(south, .1), 3.6, 3.82, 'okvir', top=False)     # string course over the ground floor
        m.prism(offset(rect(0, S, 0, tN), .25), -1.5, .45, 'okvir', top=False)  # plinth
    # roofs: hip on the south wing, gables on the side wings, gable on the north wing
    m.hip_roof(south, H_S, pitch=22, over=.45, mat='crijep', chimneys=2, seed=5)
    m.gable_roof(P2(sc0 / 2, (tc0 + tN) / 2), V, tN - tc0, sc0, H_W, pitch=22, over=.35, gable_mat='kamen')
    m.gable_roof(P2((sc1 + S) / 2, (tc0 + tN) / 2), V, tN - tc0, S - sc1, H_W, pitch=22, over=.35, gable_mat='kamen')
    m.gable_roof(P2((sc0 + sc1) / 2, (tc1 + tN) / 2), U, sc1 - sc0, tN - tc1, H_W, pitch=22, over=.3, gable_mat='kamen')
    # towers
    for T in (NW_T, NE_T):
        m.prism(T, -1.5, H_T, 'kamen', top=(lod >= 2))
        if lod == 0: m.prism(offset(T, .12), H_T - .35, H_T, 'okvir', top=False)
        m.hip_roof(T, H_T, pitch=20, over=.4, mat='crijep', ridge_frac=.98)
        m.quoins(T, .45, H_T - .4)
        if lod < 2:
            for a, b, ext in exterior_edges(T, [south, west, east, north]):
                if not ext: continue
                L = (b - a).length
                for zz in (3.5, 7.0): m.window(a, b, L / 2, zz, .45, .6, shutters=None)
                m.window(a, b, L / 2, 11.0, .8, 1.2, shutters=None)
                m.window(a, b, L / 2, 13.2, .5, .6, shutters=None)
    if lod >= 2: return m
    # ---- south (sea) front: portal / balcony door + 2 rect / trifora + 2 arched
    a, b = P2(0, 0), P2(S, 0)          # ccw edge SW -> SE: outward (right) = south
    L = S
    mid = L / 2
    m.window(a, b, mid, .02, 1.7, 3.0, style='portal', shutters=None, closed=True)
    for s in (mid - 5.6, mid + 5.6, mid - 9.0, mid + 9.0): m.window(a, b, s, 1.5, .55, .7, shutters=None)
    m.balcony(a, b, mid, 4.6, 5.0, d=1.0)
    m.window(a, b, mid, 4.6, 1.2, 2.3, shutters=None)
    for s in (mid - 4.8, mid + 4.8): m.window(a, b, s, 5.0, 1.05, 1.7, shutters='z', closed=False)
    for s in (mid - 8.6, mid + 8.6): m.window(a, b, s, 5.0, .9, 1.6, shutters='z', closed=True)
    # trifora: three round-headed lights in one frame, small balcony under it
    m.balcony(a, b, mid, 8.0, 3.2, d=.7)
    for k in (-1, 0, 1): m.window(a, b, mid + k * .95, 8.2, .72, 2.0, style='arch', shutters=None)
    for s in (mid - 4.8, mid + 4.8): m.window(a, b, s, 8.3, .95, 1.9, style='arch', shutters=None)
    for s in (mid - 8.6, mid + 8.6): m.window(a, b, s, 8.3, .9, 1.6, shutters='z', closed=True)
    # coat of arms between the floors
    e = (b - a).normalized(); n = Vector((e.y, -e.x))
    m.obox(a + e * mid + n * .08, e, .8, .16, 10.6, 11.6, 'okvir')
    # ---- east front (to the harbour): two balconies, windows
    a, b = P2(S, 0), P2(S, tN)
    L = (b - a).length
    windows_on_wall(m, a, b, 0, 3, sh=3.6, spacing=3.6, seed=11, w=.95, h=1.6, shutters='z', small_ground=True)
    m.balcony(a, b, L * .72, 4.6, 2.4, d=.8)
    # ---- west front
    a, b = P2(0, tN), P2(0, 0)
    windows_on_wall(m, a, b, 0, 3, sh=3.6, spacing=3.8, seed=12, w=.9, h=1.5, shutters='z', density=.8)
    # ---- north front: portal, machicolation over it, two rows of windows, slits
    a, b = P2(S, tN), P2(0, tN)
    L = S; mid = L / 2
    m.window(a, b, mid, .02, 1.5, 2.8, style='portal', shutters=None, closed=True)
    for k in (-3, -2, -1, 1, 2, 3):
        m.window(a, b, mid + k * 2.6, 5.4, .8, 1.25, shutters=None)
        m.window(a, b, mid + k * 2.6, 8.4, .55, .55, shutters=None)
    for k in (-2, 2): m.slit(a, b, mid + k * 2.6, 1.8)
    # projecting machicolation box above the portal on corbels
    e = (b - a).normalized(); n = Vector((e.y, -e.x))
    c = a + e * mid + n * .35
    m.obox(c, e, 2.6, .7, 8.0, H_W - .2, 'kamen')
    m.corbel_row(a + e * (mid - 1.3), a + e * (mid + 1.3), 8.0, out=.7, spacing=.85, end_pad=.12)
    m.window(a + n * .7, b + n * .7, mid, 8.6, .5, .7, style='arch', shutters=None)
    m.obox(a + e * mid + n * .06, e, .8, .14, 3.3, 4.3, 'okvir')     # coat of arms over the portal
    # ---- courtyard faces (inner walls, facing in): arcade of the gallery on the north side, windows
    court = rect(sc0, sc1, tc0, tc1)
    for i in range(4):
        p, q = court[i], court[(i + 1) % 4]
        # inner face: outward normal points into the courtyard -> edge q->p
        Lc = (q - p).length
        for k in range(max(1, int(Lc / 3.0))):
            s = Lc * (k + .5) / max(1, int(Lc / 3.0))
            m.window(q, p, s, 4.4, .8, 1.3, shutters='z')
            m.window(q, p, s, .02, 1.6, 2.6, style='arch', shutters=None) if i == 2 else None
    m.face([V3(p, .05) for p in court], 'kamen')   # paved courtyard
    # ---- bridge over the ditch (one arch) to the north portal
    A = P2(mid, tN + 6.5); B = P2(mid, tN)
    m.bridge(A, B, 3.2, .35, 1, z_water=-2.2, parapet=.8, mat='kamen', span_frac=.7)
    return m

if __name__ == '__main__':
    F = FR
    cams = [
        ('more-jug', gcam(F, -6110, -4640, 1.8), gcam(F, -6146, -4702, 7.0), 40, 0),
        ('sjever', gcam(F, -6150, -4750, 1.7), gcam(F, -6145, -4718, 7.5), 30, 0),
        ('pregled', gcam(F, -6090, -4650, 50), gcam(F, -6145, -4708, 0), 35, 0),
        ('lod1', gcam(F, -6110, -4640, 1.8), gcam(F, -6146, -4702, 7.0), 40, 1),
        ('lod2', gcam(F, -6110, -4640, 1.8), gcam(F, -6146, -4702, 7.0), 40, 2),
    ]
    run('vitturi', build, cams, scene_kw=dict(sea=True, ground=[Vector((-60, -10)), Vector((60, -10)), Vector((60, 80)), Vector((-60, 80))], ground_z=.01))
