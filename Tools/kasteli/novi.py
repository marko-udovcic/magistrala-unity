# Kaštel Pavla Cipika (Kaštel Novi): visoka četverokutna kula uz rivu (18,5 m, nizak četverostrešni krov, prozori sa
# smeđim škurama u dvije osi, kameni pojas) s nižim krilima oko dvorišta. + zasebni zvonik sv. Petra (osmerokutni trup,
# dvije otvorene lođe, nizak krov s križem) — položaj zvonika nije potvrđen, model se ne postavlja automatski.
import sys, math
sys.path.insert(0, '/Users/macair/Documents/BalkanDriver/tools/world/kasteli-blender')
import importlib, klib; importlib.reload(klib)
from klib import *

K = load('kastel_novi_cipiko')
FR = Frame(*K['position'])
parts = [p for p in K['notableParts'] if p.get('footprint')]
PP = [(FR.poly(p['footprint']['coordinates'][0]), p['heightM'], p['kind']) for p in parts]

def build(lod):
    m = Mesh(lod)
    allP = [P for P, _, _ in PP]
    for i, (P, h, kind) in enumerate(PP):
        others = [O for O in allP if O is not P]
        tower = kind == 'tower'
        m.prism(P, -1.5, h, 'kamen' if tower else ('lomljeni' if i % 2 else 'kamen'), top=(lod >= 2))
        if lod == 0: m.prism(offset(P, .1), h - .3, h, 'okvir', top=False)
        if tower:
            m.quoins(P, .3, h - .5)
            if lod == 0: m.prism(offset(P, .1), 3.0, 3.3, 'okvir', top=False)     # the horizontal band low down
            if lod < 2:
                for j, (a, b, ext) in enumerate(exterior_edges(P, others)):
                    L = (b - a).length
                    if not ext or L < 4: continue
                    nrm = Vector(((b - a).y, -(b - a).x)).normalized()
                    rich = nrm.y < -.5 or nrm.x > .5            # sea (south) and east faces: the two-axis rows
                    axes = (L * .32, L * .68) if rich else (L * .5,)
                    for s in axes:
                        m.window(a, b, s, 1.2, .55, .7, shutters=None)
                        for zz in (6.0, 9.6, 13.2):
                            m.window(a, b, s, zz, 1.0, 1.55, shutters='s', closed=True)
                        m.window(a, b, s, 16.0, .6, .7, shutters=None)
            m.hip_roof(P, h, pitch=20, over=.5, mat='crijep', ridge_frac=.97, chimneys=1, seed=2)
        else:
            storeys = max(1, round((h - .5) / 3.0))
            if lod < 2:
                for a, b, ext in exterior_edges(P, others):
                    if ext: windows_on_wall(m, a, b, 0, storeys, sh=3.0, spacing=3.3, seed=i * 17 + int(a.x), door_ground=True, w=.9, h=1.4, density=.85)
            c, u, L, W = min_rect(P)
            if abs(area(P)) / (L * W) > .85 and L > 1.3 * W:
                m.gable_roof(c, u, L, W, h, pitch=24, over=.3, gable_mat='kamen')
            else:
                m.hip_roof(P, h, pitch=24, over=.3, mat='crijep', seed=i)
    return m

def zvonik(lod):
    m = Mesh(lod)
    c = Vector((0, 0)); seg = 8
    R = 2.3
    off = math.pi / 8
    ring = lambda r: [c + Vector((math.cos(2 * math.pi * i / seg + off), math.sin(2 * math.pi * i / seg + off))) * r for i in range(seg)]
    m.prism(ring(R + .35), -1.0, 1.4, 'kamen', top=False)            # plinth
    m.prism(ring(R), 1.4, 16.8, 'kamen', top=False)
    if lod == 0:
        for zz in (6.0, 11.5): m.prism(ring(R + .12), zz, zz + .3, 'okvir', top=False)
    # first loggia: corner piers + round arches, cornice
    z1, z2 = 16.8, 20.6
    m.prism(ring(R + .3), z1 - .3, z1 + .1, 'okvir', top=(lod >= 1))
    pts = ring(R)
    if lod < 2:
        for i in range(seg):
            a, b = pts[i], pts[(i + 1) % seg]
            m.obox(a, (b - a).normalized(), .45, .45, z1 + .1, z2, 'kamen', top=False)
            e = (b - a); L = e.length; u = e / L; n = Vector((u.y, -u.x))
            m.arch_panel(a + u * (L / 2) + n * .02, u, n, L - .45, z2 - 1.3, z2 + .4, mat='kamen', seg=5)
            m.window(a, b, L / 2, z1 + 1.2, .6, 1.1, shutters=None, frame=False)     # bell shadow behind
        m.prism(ring(R - .35), z1 + .1, z2 + .4, 'otvor', top=False)   # dark core seen through the arches
    else:
        m.prism(ring(R), z1, z2 + .4, 'kamen', top=False)
    m.prism(ring(R + .3), z2 + .4, z2 + .8, 'okvir', top=True)
    # second, smaller loggia
    R2 = 1.75; z3, z4 = z2 + .8, z2 + 3.6
    pts = ring(R2)
    if lod < 2:
        for i in range(seg):
            a, b = pts[i], pts[(i + 1) % seg]
            m.obox(a, (b - a).normalized(), .36, .36, z3, z4, 'kamen', top=False)
            e = (b - a); L = e.length; u = e / L; n = Vector((u.y, -u.x))
            m.arch_panel(a + u * (L / 2) + n * .02, u, n, L - .36, z4 - 1.0, z4 + .3, mat='kamen', seg=4)
        m.prism(ring(R2 - .3), z3, z4 + .3, 'otvor', top=False)
    else:
        m.prism(ring(R2), z3, z4 + .3, 'kamen', top=False)
    m.prism(ring(R2 + .25), z4 + .3, z4 + .6, 'okvir', top=False)
    # low octagonal roof + cross
    zr = z4 + .6
    top = Vector((0, 0, zr + 1.3)); P = ring(R2 + .25)
    for i in range(seg):
        a, b = P[i], P[(i + 1) % seg]
        m.face([V3(a, zr), V3(b, zr), top], 'kamen')
    if lod < 2:
        m.obox(c, Vector((1, 0)), .12, .12, zr + 1.2, zr + 2.6, 'metal')
        m.obox(c + Vector((0, 0)), Vector((1, 0)), .8, .1, zr + 2.0, zr + 2.12, 'metal')
    return m

if __name__ == '__main__':
    F = FR
    tw = [P for P, h, k in PP if k == 'tower'][0]; tc = centroid(tw)
    cams = [
        ('riva', gcam(F, -8105, -4325, 1.7), (tc.x, tc.y, 9.0), 28, 0),
        ('more', gcam(F, -8150, -4290, 3.0), gcam(F, -8148, -4372, 8.0), 35, 0),
        ('pregled', gcam(F, -8095, -4310, 50), gcam(F, -8148, -4372, 0), 35, 0),
        ('lod1', gcam(F, -8105, -4325, 1.7), (tc.x, tc.y, 9.0), 28, 1),
    ]
    run('novi', build, cams, scene_kw=dict(sea=True, ground=[Vector((-60, -12)), Vector((60, -12)), Vector((60, 80)), Vector((-60, 80))], ground_z=.01))
    run('zvonik-sv-petar', zvonik, [('trg', (14, -18, 1.7), (0, 0, 14), 30, 0), ('lod1', (14, -18, 1.7), (0, 0, 14), 30, 1)],
        scene_kw=dict(sea=False, ground=[Vector((-60, -60)), Vector((60, -60)), Vector((60, 60)), Vector((-60, 60))], ground_z=0))
