# Kaštel Koriolana Cipika (Kaštel Stari): krila P+2 oko dvorišta (DGU dijelovi), uska viša sjeverna ulazna masa s grbom i
# balkončićem, južno pročelje s kamenim balkonom (dva polja s kružnim reljefom) i prozorima s lunetama, crijep, zelene škure.
import sys, math
sys.path.insert(0, '/Users/macair/Documents/BalkanDriver/tools/world/kasteli-blender')
import importlib, klib; importlib.reload(klib)
from klib import *

K = load('kastel_stari_cipiko')
FR = Frame(*K['position'])
parts = [p for p in K['notableParts'] if p.get('footprint')]
PP = [FR.poly(p['footprint']['coordinates'][0]) for p in parts]
# the narrow taller entrance mass: the smallest part on the north side (largest local Y)
ENTRY = max(range(len(PP)), key=lambda i: centroid(PP[i]).y - abs(area(PP[i])) * .02)
SOUTH = min(range(len(PP)), key=lambda i: centroid(PP[i]).y)

def build(lod):
    m = Mesh(lod)
    for i, P in enumerate(PP):
        others = [O for j, O in enumerate(PP) if j != i]
        h = 14.2 if i == ENTRY else 11.0 - .6 * (i % 2)
        wall = 'kamen' if i in (ENTRY, SOUTH) else ('lomljeni' if i % 2 else 'kamen')
        m.prism(P, -1.5, h, wall, top=(lod >= 2))
        if lod == 0: m.prism(offset(P, .1), h - .28, h, 'okvir', top=False)
        if i == ENTRY: m.quoins(P, .3, h - .4)
        if lod < 2:
            for a, b, ext in exterior_edges(P, others):
                if not ext: continue
                L = (b - a).length
                nrm = Vector(((b - a).y, -(b - a).x)).normalized()
                if i == ENTRY and nrm.y > .6:
                    m.window(a, b, L / 2, .02, 1.4, 2.6, style='portal', shutters=None, closed=True)
                    m.balcony(a, b, L / 2, 6.2, 1.8, d=.6)
                    m.window(a, b, L / 2, 6.2, .9, 2.0, shutters='s')
                    m.window(a, b, L / 2, 3.4, .8, 1.3, shutters='s')
                    e = (b - a).normalized()
                    m.obox(a + e * (L / 2) + nrm * .08, e, .8, .16, 9.4, 10.5, 'okvir')        # coat of arms
                    m.window(a, b, L / 2, 11.6, .6, .7, shutters=None)
                elif i == SOUTH and nrm.y < -.6 and L > 5:
                    n5 = max(3, int(L / 2.6)); mid = (n5 - 1) / 2
                    for k in range(n5):
                        s = L * (k + .5) / n5
                        m.window(a, b, s, 3.9, .9, 1.45, shutters='z', closed=k % 2 == 0)
                        if abs(k - mid) < .6:
                            m.balcony(a, b, s, 7.0, 2.6, d=.75, balusters=False)
                            m.window(a, b, s - .55, 7.0, .8, 2.0, shutters=None); m.window(a, b, s + .55, 7.0, .8, 2.0, shutters=None)
                        else:
                            m.window(a, b, s, 7.2, .9, 1.45, shutters='z', closed=k % 2 == 1)
                            e = (b - a).normalized()
                            m.arch_ring(a + e * s + nrm * 0, e, nrm, 7.2 + 1.45 + .3, .5, .66, .08, 6)   # lunette
                    m.window(a, b, L * .3, .02, 1.4, 2.4, style='door', shutters=None)
                else:
                    windows_on_wall(m, a, b, 0, 3, sh=3.3, spacing=3.0, seed=i * 7 + int(a.x * 3), door_ground=True, w=.85, h=1.35,
                                    shutters='z' if i % 3 else None, density=.85)
        if i == ENTRY:
            m.hip_roof(P, h, pitch=28, over=.35, mat='crijep', ridge_frac=.97, chimneys=1, seed=4)
        else:
            c, u, L, W = min_rect(P)
            if abs(area(P)) / (L * W) > .85 and L > 1.25 * W:
                m.gable_roof(c, u, L, W, h, pitch=27, over=.3, gable_mat=wall)
            else:
                m.hip_roof(P, h, pitch=27, over=.3, mat='crijep', chimneys=1 if i % 2 else 0, seed=i)
    return m

if __name__ == '__main__':
    F = FR
    e = centroid(PP[ENTRY]); s = centroid(PP[SOUTH])
    cams = [
        ('sjever', (e.x - 2, e.y + 26, 1.7), (e.x, e.y, 7.5), 40, 0),
        ('jug', (s.x + 4, s.y - 24, 1.7), (s.x, s.y, 6.0), 45, 0),
        ('pregled', (35, -45, 45), (0, 0, 0), 35, 0),
        ('lod1', (s.x + 4, s.y - 24, 1.7), (s.x, s.y, 6.0), 45, 1),
    ]
    run('cipiko-stari', build, cams, scene_kw=dict(sea=False, ground=[Vector((-80, -80)), Vector((80, -80)), Vector((80, 80)), Vector((-80, 80))], ground_z=0))
