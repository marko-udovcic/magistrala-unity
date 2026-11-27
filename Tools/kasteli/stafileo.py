# Kaštel Štafileo (Kaštel Štafilić): skupina krila različitih visina (DGU dijelovi), visoki zabati, krovne kućice,
# pravokutni kameni okviri, svijetle (smeđe) škure, bez kruništa i bez mosta.
import sys, math
sys.path.insert(0, '/Users/macair/Documents/BalkanDriver/tools/world/kasteli-blender')
import importlib, klib; importlib.reload(klib)
from klib import *

K = load('kastel_stafileo')
FR = Frame(*K['position'])
parts = [p for p in K['notableParts'] if p.get('footprint')]
PP = [(FR.poly(p['footprint']['coordinates'][0]), p['heightM']) for p in parts]

def build(lod):
    m = Mesh(lod)
    allP = [P for P, _ in PP]
    for i, (P, h) in enumerate(PP):
        others = [O for O in allP if O is not P]
        h = h + (1.2 if h >= 11 else 0)
        m.prism(P, -1.5, h, 'kamen', top=(lod >= 2), wet=.4)
        if lod == 0: m.prism(offset(P, .1), h - .25, h, 'okvir', top=False)
        m.quoins(P, .4, h - .4)
        if lod < 2:
            for a, b, ext in exterior_edges(P, others):
                if ext: windows_on_wall(m, a, b, 0, max(2, round((h - .5) / 3.1)), sh=3.1, spacing=4.4, seed=i * 9 + int(a.y),
                                        door_ground=True, w=.8, h=1.2, shutters='s', density=.6, small_ground=True)
        c, u, L, W = min_rect(P)
        if abs(area(P)) / (L * W) > .8 and h < 11:
            m.gable_roof(c, u, L, W, h, pitch=30, over=.3, gable_mat='kamen')
            if lod < 2 and L > 7:
                v = Vector((-u.y, u.x)); a = c - u * (L / 2) - v * (W / 2); b = c + u * (L / 2) - v * (W / 2)
                m.dormer(b, a, L * .5, h, mat='crijep', wall='kamen') if False else m.dormer(a, b, L * .5, h, mat='crijep', wall='kamen')
        else:
            m.hip_roof(P, h, pitch=28, over=.3, mat='crijep', seed=i)
    return m

if __name__ == '__main__':
    F = FR
    cams = [('kopno', gcam(F, -8460, -4420, 1.7), gcam(F, -8447, -4374, 6), 40, 0),
            ('more', gcam(F, -8430, -4330, 2.0), gcam(F, -8447, -4374, 6), 40, 0),
            ('pregled', gcam(F, -8400, -4420, 40), gcam(F, -8447, -4374, 0), 35, 0)]
    run('stafileo', build, cams, scene_kw=dict(sea=True, ground=[Vector((-60, -15)), Vector((60, -15)), Vector((60, 60)), Vector((-60, 60))], ground_z=.01))
