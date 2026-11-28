# Kašteli (Magistrala): zajednička Blender biblioteka — tlocrti iz kasteli.json, zidovi, krovovi, prozori, mašikule, mostovi,
# UV u metrima po materijalu, tri LOD-a, izvoz GLB-a i EEVEE usporedni renderi.
# Koordinate: igra (x istok, z jug) -> Blender (X = x - cx, Y = -(z - cz), Z gore); Z = 0 je razina mora (kopno: tlo uz kaštel).
import bpy, bmesh, math, json, random, os, sys
from mathutils import Vector

DATA = '/Users/macair/Documents/BalkanDriver/tools/world/riva-extras/kasteli.json'
TEX = '/Users/macair/Documents/BalkanDriver/assets-src/kasteli/_teksture/'
OUT = '/Users/macair/Documents/BalkanDriver/assets-src/kasteli/'
RENDERS = '/Users/macair/Desktop/Magistrala-slike/kasteli/'

# (ime, tekstura, metara po teksturi, boja (množi teksturu), hrapavost)
MATS = [
    ('kamen', 'kamen', 3.0, (1.0, .97, .91), .85),          # 0 klesani kvadri
    ('lomljeni', 'lomljeni', 2.2, (1.0, .96, .89), .9),     # 1 lomljeni kamen (kuće, obod)
    ('zbuka', 'zbuka', 2.0, (.92, .82, .70), .9),           # 2 žbuka
    ('crijep', 'crijep', 2.5, (1.0, 1.0, 1.0), .75),        # 3 kupa kanalica
    ('ploce', 'kamen', 1.6, (.68, .67, .65), .9),           # 4 kameni pokrov (ploče)
    ('okvir', 'kamen', 1.2, (1.08, 1.05, 1.0), .7),        # 5 kameni okviri, vijenci, konzole
    ('otvor', None, 1.0, (.025, .025, .028), .35),          # 6 tamni otvor / staklo
    ('skure_z', None, 1.0, (.10, .22, .13), .6),            # 7 zelene škure
    ('skure_s', None, 1.0, (.22, .12, .06), .6),            # 8 smeđe škure
    ('drvo', None, 1.0, (.20, .12, .07), .7),               # 9 vrata
    ('metal', None, 1.0, (.06, .06, .065), .5),             # 10 ograde, križ, rešetke
    ('kamen_vlazni', 'kamen', 3.0, (.62, .62, .58), .9),    # 11 pojas uz more (vlaga, alge)
]
I = {m[0]: i for i, m in enumerate(MATS)}

def load(cid):
    d = json.load(open(DATA))
    for k in d['kasteli']:
        if k['id'] == cid: return k
    raise KeyError(cid)

def polys(g):
    if not g: return []
    return [g['coordinates']] if g['type'] == 'Polygon' else g['coordinates']

class Frame:
    """game (x, z) <-> local 2D (X east, Y north)"""
    def __init__(self, cx, cz): self.cx, self.cz = cx, cz
    def p(self, x, z): return Vector((x - self.cx, -(z - self.cz)))
    def poly(self, ring):
        P = [self.p(x, z) for x, z in ring]
        if (P[0] - P[-1]).length < 1e-6: P = P[:-1]
        # dedupe
        Q = []
        for v in P:
            if not Q or (v - Q[-1]).length > .05: Q.append(v)
        if len(Q) > 2 and (Q[0] - Q[-1]).length < .05: Q.pop()
        if area(Q) < 0: Q.reverse()      # ccw
        return Q

def area(P):
    return sum(P[i].x * P[(i + 1) % len(P)].y - P[(i + 1) % len(P)].x * P[i].y for i in range(len(P))) / 2

def centroid(P):
    return sum(P, Vector((0, 0))) / len(P)

def offset(P, d):
    """ccw polygon offset outwards by d (miter, clamped)"""
    n = len(P); R = []
    for i in range(n):
        a, b, c = P[i - 1], P[i], P[(i + 1) % n]
        e1 = (b - a).normalized(); e2 = (c - b).normalized()
        n1 = Vector((e1.y, -e1.x)); n2 = Vector((e2.y, -e2.x))
        m = (n1 + n2);
        if m.length < 1e-6: m = n1
        m.normalize(); cosv = max(.35, m.dot(n1))
        R.append(b + m * (d / cosv))
    return R

def min_rect(P):
    """minimum-area oriented rectangle: (centre, ux, L along ux, W)"""
    best = None
    for i in range(len(P)):
        e = P[(i + 1) % len(P)] - P[i]
        if e.length < .3: continue
        u = e.normalized(); v = Vector((-u.y, u.x))
        us = [q.dot(u) for q in P]; vs = [q.dot(v) for q in P]
        L, W = max(us) - min(us), max(vs) - min(vs)
        if best is None or L * W < best[0] - 1e-6:
            c = u * ((max(us) + min(us)) / 2) + v * ((max(vs) + min(vs)) / 2)
            best = (L * W, c, u, L, W)
    _, c, u, L, W = best
    if W > L: u = Vector((-u.y, u.x)); L, W = W, L
    return c, u, L, W

def inside(P, q):
    c = False; n = len(P)
    for i in range(n):
        a, b = P[i], P[(i + 1) % n]
        if (a.y > q.y) != (b.y > q.y) and q.x < a.x + (q.y - a.y) * (b.x - a.x) / (b.y - a.y): c = not c
    return c

def seg_dist(q, a, b):
    ab = b - a; t = max(0, min(1, (q - a).dot(ab) / max(1e-9, ab.dot(ab)))); return (a + ab * t - q).length

def h01(*k):
    v = math.sin(sum((i + 1) * 12.9898 * x for i, x in enumerate(k)) + 78.233) * 43758.5453
    return v - math.floor(v)

V3 = lambda v2, z: Vector((v2.x, v2.y, z))

class Mesh:
    def __init__(self, lod=0):
        self.bm = bmesh.new(); self.lod = lod

    def face(self, pts, mat):
        vs = [self.bm.verts.new(p) for p in pts]
        try:
            f = self.bm.faces.new(vs)
        except ValueError:
            return None
        f.material_index = mat if isinstance(mat, int) else I[mat]
        return f

    def quad(self, a, b, c, d, mat): return self.face([a, b, c, d], mat)

    def prism(self, P, z0, z1, mat, top=True, bottom=False, topmat=None, wet=None):
        """walls of a ccw polygon from z0 to z1 (outward normals); wet: height below which the wall is the wet stone"""
        n = len(P)
        if wet is not None and z0 < wet < z1:
            self.prism(P, z0, wet, 'kamen_vlazni', top=False, bottom=bottom)
            z0 = wet; bottom = False
        for i in range(n):
            a, b = P[i], P[(i + 1) % n]
            self.quad(V3(a, z0), V3(b, z0), V3(b, z1), V3(a, z1), mat)
        if top: self.face([V3(p, z1) for p in P], topmat if topmat is not None else mat)
        if bottom: self.face([V3(p, z0) for p in reversed(P)], mat)

    def obox(self, c, u, w, d, z0, z1, mat, top=True, bottom=False):
        """oriented box: centre c (2D), u = width axis (2D unit), w along u, d across"""
        u = Vector(u).normalized(); v = Vector((-u.y, u.x))
        P = [c - u * w / 2 - v * d / 2, c + u * w / 2 - v * d / 2, c + u * w / 2 + v * d / 2, c - u * w / 2 + v * d / 2]
        self.prism(P, z0, z1, mat, top=top, bottom=bottom)

    def cyl(self, c, r, z0, z1, seg, mat, top=True, r1=None, wet=None):
        r1 = r if r1 is None else r1
        if wet is not None and z0 < wet < z1:
            self.cyl(c, r, z0, wet, seg, 'kamen_vlazni', top=False); z0 = wet
        for i in range(seg):
            a0, a1 = 2 * math.pi * i / seg, 2 * math.pi * (i + 1) / seg
            p0 = Vector((math.cos(a0), math.sin(a0))); p1 = Vector((math.cos(a1), math.sin(a1)))
            self.quad(V3(c + p0 * r, z0), V3(c + p1 * r, z0), V3(c + p1 * r1, z1), V3(c + p0 * r1, z1), mat)
        if top: self.face([V3(c + Vector((math.cos(2 * math.pi * i / seg), math.sin(2 * math.pi * i / seg))) * r1, z1) for i in range(seg)], mat)

    # -------------------------------------------------------------- roofs
    def hip_roof(self, P, z, pitch=24, over=.35, mat='crijep', thick=.18, ridge_frac=.94, chimneys=0, seed=0):
        """hip roof on any (near-convex) polygon: outer ring at the eaves, inset ring raised (a straight-skeleton stand-in)"""
        c, u, L, W = min_rect(P)
        O = offset(P, over)
        t = W / 2 * ridge_frac + over
        rise = t * math.tan(math.radians(pitch))
        # roof skin: bmesh inset on a temporary face
        bm = self.bm
        vs = [bm.verts.new(V3(p, z)) for p in O]
        f = bm.faces.new(vs); f.material_index = I[mat] if isinstance(mat, str) else mat
        f.normal_update()
        res = bmesh.ops.inset_region(bm, faces=[f], thickness=t, depth=rise, use_even_offset=True, use_boundary=True)
        f.material_index = I[mat] if isinstance(mat, str) else mat
        for g in res['faces']: g.material_index = f.material_index
        if self.lod < 2:
            # fascia + soffit
            O2 = offset(P, over)
            for i in range(len(O2)):
                a, b = O2[i], O2[(i + 1) % len(O2)]
                self.quad(V3(a, z - thick), V3(b, z - thick), V3(b, z), V3(a, z), 'okvir')
            self.face([V3(p, z - thick) for p in reversed(O2)], 'okvir')
        top = z + rise
        if chimneys and self.lod < 2:
            for k in range(chimneys):
                s = (h01(seed, k) - .5) * max(0, L - W) * .8
                q = c + u * s + Vector((-u.y, u.x)) * (h01(seed, k, 3) - .5) * W * .3
                self.obox(q, u, .7, .55, z, top + .9, 'lomljeni')
                self.obox(q, u, .9, .75, top + .9, top + 1.05, 'okvir')
        return top

    def gable_roof(self, c, u, L, W, z, pitch=26, over=.3, mat='crijep', gable_mat='lomljeni', thick=.15):
        """gable roof over an oriented rectangle, ridge along u; gable triangles in stone"""
        u = Vector(u).normalized(); v = Vector((-u.y, u.x))
        rise = (W / 2) * math.tan(math.radians(pitch))
        hl, hw = L / 2 + over, W / 2 + over
        zr = z + rise + over * math.tan(math.radians(pitch))
        ze = z - 0  # eaves at wall top minus overhang drop
        zo = z - over * math.tan(math.radians(pitch)) * 0
        A = lambda s, t, h: V3(c + u * s + v * t, h)
        zeave = z - over * math.tan(math.radians(pitch))
        # slopes
        self.quad(A(-hl, -hw, zeave), A(hl, -hw, zeave), A(hl, 0, zr), A(-hl, 0, zr), mat)
        self.quad(A(hl, hw, zeave), A(-hl, hw, zeave), A(-hl, 0, zr), A(hl, 0, zr), mat)
        if self.lod < 2:
            self.quad(A(-hl, -hw, zeave - thick), A(hl, -hw, zeave - thick), A(hl, -hw, zeave), A(-hl, -hw, zeave), 'okvir')
            self.quad(A(hl, hw, zeave - thick), A(-hl, hw, zeave - thick), A(-hl, hw, zeave), A(hl, hw, zeave), 'okvir')
            # undersides
            self.quad(A(-hl, 0, zr - thick), A(hl, 0, zr - thick), A(hl, -hw, zeave - thick), A(-hl, -hw, zeave - thick), mat)
            self.quad(A(hl, 0, zr - thick), A(-hl, 0, zr - thick), A(-hl, hw, zeave - thick), A(hl, hw, zeave - thick), mat)
        # gables (wall material) at the wall plane
        for s, sg in ((-L / 2, -1), (L / 2, 1)):
            pts = [A(s, -W / 2, z), A(s, W / 2, z), A(s, 0, z + rise)]
            if sg < 0: pts.reverse()
            # outward = sg*u ; order so normal points along sg*u
            f = self.face(pts, gable_mat)
            if f is not None:
                f.normal_update()
                if f.normal.to_2d().dot(u * sg) < 0: f.normal_flip()
        return z + rise

    # -------------------------------------------------------------- openings (protruding frames: no holes in the walls)
    def window(self, a, b, s, zs, w, h, style='rect', shutters='z', frame=True, closed=False, depth_out=.0):
        """window on wall a->b (ccw polygon edge: outward = right of a->b), s = centre distance from a"""
        e = (b - a); L = e.length; u = e / L; n = Vector((u.y, -u.x))
        c = a + u * s + n * depth_out
        P = lambda du, dz, dn: Vector((c.x + u.x * du + n.x * dn, c.y + u.y * du + n.y * dn, zs + dz))
        arch = style in ('arch', 'portal', 'loggia')
        hh = h - (w / 2 if arch else 0)   # straight part
        if self.lod >= 2: return
        # opening (dark) slightly in front of the wall
        if arch:
            seg = 6 if self.lod == 0 else 3
            pts = [P(-w / 2, 0, .02), P(w / 2, 0, .02)]
            for k in range(seg + 1):
                ang = math.pi * k / seg
                pts.append(P(math.cos(ang) * w / 2, hh + math.sin(ang) * w / 2, .02))
            pts = pts[:2] + pts[2:]
            self.face(pts, 'drvo' if style == 'portal' and closed else 'otvor')
        else:
            mat = 'otvor'
            if closed and shutters: mat = 'skure_z' if shutters == 'z' else 'skure_s'
            if style == 'door': mat = 'drvo'
            self.quad(P(-w / 2, 0, .02), P(w / 2, 0, .02), P(w / 2, h, .02), P(-w / 2, h, .02), mat)
        if self.lod == 1:
            if shutters and not closed and style in ('rect',):
                sm = 'skure_z' if shutters == 'z' else 'skure_s'
                for sg in (-1, 1):
                    x0 = sg * (w / 2 + .1); x1 = sg * (w / 2 + .1 + w / 2)
                    self.quad(P(min(x0, x1), 0, .03), P(max(x0, x1), 0, .03), P(max(x0, x1), h, .03), P(min(x0, x1), h, .03), sm)
            return
        if frame:
            fw, fo = (.18, .07) if style != 'portal' else (.32, .12)
            def fbox(du, dz, bw, bh, dn=fo):
                cc = c + u * du + n * (dn / 2)
                self.obox(cc, u, bw, dn + .0, zs + dz, zs + dz + bh, 'okvir', top=True)
            fbox(-w / 2 - fw / 2, -fw * .3, fw, hh + fw * .3)
            fbox(w / 2 + fw / 2, -fw * .3, fw, hh + fw * .3)
            if arch:
                self.arch_ring(c, u, n, zs + hh, w / 2, w / 2 + fw, fo, 6)
            else:
                fbox(0, h, w + 2 * fw, fw)                 # lintel
                if style != 'door':
                    cc = c + n * .07
                    self.obox(cc, u, w + 2 * fw + .1, .14, zs - .1, zs, 'okvir')   # sill
        if shutters and style == 'rect' and not closed:
            sm = 'skure_z' if shutters == 'z' else 'skure_s'
            for sg in (-1, 1):
                cc = c + u * (sg * (w / 2 + .2 + w / 4)) + n * .05
                self.obox(cc, u, w / 2, .04, zs, zs + h, sm)

    def arch_ring(self, c, u, n, zc, r0, r1, depth, seg, mat='okvir'):
        """semicircular stone arch ring (front face + outer band) on a wall plane"""
        P = lambda r, ang, dn: Vector((c.x + u.x * math.cos(ang) * r + n.x * dn, c.y + u.y * math.cos(ang) * r + n.y * dn, zc + math.sin(ang) * r))
        for k in range(seg):
            a0, a1 = math.pi * k / seg, math.pi * (k + 1) / seg
            self.quad(P(r0, a0, depth), P(r1, a0, depth), P(r1, a1, depth), P(r0, a1, depth), mat)
            self.quad(P(r1, a0, depth), P(r1, a0, 0), P(r1, a1, 0), P(r1, a1, depth), mat)
            self.quad(P(r0, a1, depth), P(r0, a1, 0), P(r0, a0, 0), P(r0, a0, depth), mat)

    def dormer(self, a, b, s, z_eave, w=1.3, h=1.4, depth=1.6, mat='ploce', wall='lomljeni'):
        """small gabled dormer standing on the eave line of wall a->b (projects into the roof)"""
        if self.lod >= 2: return
        e = (b - a); u = e.normalized(); n = Vector((u.y, -u.x)); c = a + u * s - n * (depth / 2 - .05)
        self.obox(c, u, w, depth, z_eave - .2, z_eave + h, wall, top=False)
        self.gable_roof(c, -n, depth, w, z_eave + h, pitch=38, over=.15, mat=mat, gable_mat=wall)
        self.window(a - n * .05, b - n * .05, s, z_eave + .2, w * .55, h * .65, shutters='z' if h01(s, z_eave) < .5 else 's')

    def quoins(self, P, z0, z1, step=.62, big=.62, small=.4, proud=.035):
        """alternating dressed corner stones on every convex corner of a ccw polygon"""
        if self.lod > 0: return
        n = len(P)
        for i in range(n):
            a, b, c = P[i - 1], P[i], P[(i + 1) % n]
            e1 = (b - a).normalized(); e2 = (c - b).normalized()
            if e1.x * e2.y - e1.y * e2.x < .3: continue          # only convex corners
            n1 = Vector((e1.y, -e1.x)); n2 = Vector((e2.y, -e2.x))
            for k in range(int((z1 - z0) / step)):
                la, lb = (big, small) if k % 2 == 0 else (small, big)
                za, zb = z0 + k * step, z0 + k * step + step - .06
                # block along e1 (ending at the corner) and along e2 (starting at the corner)
                self.obox(b - e1 * (la / 2) + n1 * (proud / 2), e1, la, .2 + proud, za, zb, 'okvir', top=False)
                self.obox(b + e2 * (lb / 2) + n2 * (proud / 2), e2, lb, .2 + proud, za, zb, 'okvir', top=False)

    def slit(self, a, b, s, zs, w=.18, h=.8):
        """loophole / small opening"""
        e = (b - a); u = e.normalized(); n = Vector((u.y, -u.x)); c = a + u * s
        P = lambda du, dz: Vector((c.x + u.x * du + n.x * .02, c.y + u.y * du + n.y * .02, zs + dz))
        if self.lod < 2: self.quad(P(-w / 2, 0), P(w / 2, 0), P(w / 2, h), P(-w / 2, h), 'otvor')

    def balcony(self, a, b, s, z, w, d=1.0, balusters=True):
        """stone balcony on corbels with a balustrade"""
        if self.lod >= 2: return
        e = (b - a); u = e.normalized(); n = Vector((u.y, -u.x)); c = a + u * s + n * (d / 2)
        self.obox(c, u, w, d, z - .18, z, 'okvir')
        nk = max(2, int(w / 1.1) + 1)
        for k in range(nk):
            q = a + u * (s - w / 2 + .3 + (w - .6) * k / (nk - 1)) + n * (d * .45)
            self.obox(q, u, .22, d * .9, z - .6, z - .18, 'okvir')
            if self.lod == 0: self.obox(q - n * (d * .2), u, .22, d * .45, z - .9, z - .6, 'okvir')
        # parapet: posts + rail (balusters as thin boxes at LOD0)
        rail_h = .95
        if balusters and self.lod == 0:
            nb = int(w / .28)
            for k in range(nb + 1):
                q = c - u * (w / 2 - .12) + u * ((w - .24) * k / nb) + n * (d / 2 - .1)
                self.obox(q, u, .11, .11, z, z + rail_h - .12, 'okvir', top=False)
            for sg in (-1, 1):
                for k in range(3):
                    q = c + u * (sg * (w / 2 - .1)) + n * (-d / 2 + .1 + (d - .2) * k / 2)
                    self.obox(q, u, .11, .11, z, z + rail_h - .12, 'okvir', top=False)
        else:
            self.obox(c + n * (d / 2 - .08), u, w, .14, z, z + rail_h - .12, 'okvir')
        self.obox(c + n * (d / 2 - .08), u, w, .2, z + rail_h - .12, z + rail_h, 'okvir')
        for sg in (-1, 1):
            q = c + u * (sg * (w / 2 - .08))
            v = Vector((-u.y, u.x))
            self.obox(q, v, d, .16, z + rail_h - .12, z + rail_h, 'okvir') if False else self.obox(q, u, .16, d, z + rail_h - .12, z + rail_h, 'okvir')
            if not (balusters and self.lod == 0): self.obox(q, u, .14, d, z, z + rail_h - .12, 'okvir')

    def arch_panel(self, c, t, n, w, z_spring, z_top, mat='okvir', seg=5):
        """flat stone panel facing n (2D), width w along t, with a semicircular opening from z_spring (between two corbels)"""
        r = w / 2
        outline = [(-w / 2, z_spring), (-w / 2, z_top), (w / 2, z_top), (w / 2, z_spring)]
        for j in range(seg + 1):
            ang = math.pi * j / seg
            outline.append((math.cos(ang) * r, z_spring + math.sin(ang) * r * .8))
        pts = [Vector((c.x + t.x * s, c.y + t.y * s, z)) for s, z in outline]
        f = self.face(pts, mat)
        if f is not None:
            f.normal_update()
            if f.normal.to_2d().dot(n) < 0: f.normal_flip()

    def corbel_row(self, a, b, z, out=.55, spacing=.95, arches=True, end_pad=.3):
        """machicolation: stepped corbels along a->b under a projecting parapet (caller builds the parapet)"""
        if self.lod >= 2: return
        e = (b - a); L = e.length; u = e / L; n = Vector((u.y, -u.x))
        k = max(1, int((L - 2 * end_pad) / spacing))
        for i in range(k + 1):
            s = end_pad + (L - 2 * end_pad) * i / k
            q = a + u * s
            if self.lod == 0:
                self.obox(q + n * (out * .5 / 2), u, .32, out * .5, z - .9, z - .55, 'okvir')
                self.obox(q + n * (out * .8 / 2), u, .32, out * .8, z - .55, z - .25, 'okvir')
            self.obox(q + n * (out / 2), u, .32, out, z - .25, z, 'okvir')
        if arches and self.lod == 0:
            for i in range(k):
                s = end_pad + (L - 2 * end_pad) * (i + .5) / k
                q = a + u * s + n * (out - .1)
                sp = (L - 2 * end_pad) / k - .32
                self.arch_panel(a + u * s + n * (out + .005), u, n, sp + .02, z - .62, z, seg=4)
                # the dark gap (machicolation hole) behind the arch
                g = a + u * s + n * .015
                P3 = lambda du, dz: Vector((g.x + u.x * du, g.y + u.y * du, z + dz))
                self.quad(P3(-sp / 2, -.9), P3(sp / 2, -.9), P3(sp / 2, -.12), P3(-sp / 2, -.12), 'otvor')

    def corbel_ring(self, c, r, z, out=.6, n=24, arches=True):
        if self.lod >= 2: return
        for i in range(n):
            ang = 2 * math.pi * i / n
            d = Vector((math.cos(ang), math.sin(ang))); t = Vector((-d.y, d.x))
            if self.lod == 0:
                self.obox(c + d * (r + out * .2), t, .42, out * .4, z - 1.3, z - .9, 'okvir')
                self.obox(c + d * (r + out * .35), t, .42, out * .7, z - .9, z - .5, 'okvir')
            self.obox(c + d * (r + out * .5), t, .42, out, z - .5, z, 'okvir')
            if arches and self.lod == 0:
                a2 = 2 * math.pi * (i + .5) / n; d2 = Vector((math.cos(a2), math.sin(a2))); t2 = Vector((-d2.y, d2.x))
                self.arch_panel(c + d2 * (r + out + .005), t2, d2, 2 * math.pi * (r + out) / n - .32, z - .7, z, seg=4)
                sp = 2 * math.pi * r / n - .34; g = c + d2 * (r + .015)
                P3 = lambda du, dz: Vector((g.x + t2.x * du, g.y + t2.y * du, z + dz))
                self.quad(P3(-sp / 2, -1.0), P3(sp / 2, -1.0), P3(sp / 2, -.14), P3(-sp / 2, -.14), 'otvor')

    def stairs_bridge(self): pass

    def bridge(self, A, B, width, z_deck, n_arches, z_water=-.3, parapet=.7, mat='lomljeni', span_frac=.62):
        """stone bridge A->B (2D, local), deck at z_deck, n_arches round arches"""
        e = B - A; L = e.length; u = e / L; v = Vector((-u.y, u.x))
        if self.lod >= 2:
            self.obox((A + B) / 2, u, L, width, z_water - 1.5, z_deck, mat); return
        z_bot = z_water - 1.5
        span = L / n_arches
        aw = span * span_frac; pier = span - aw
        r = aw / 2
        spring = max(z_water + .2, z_deck - .55 - r)
        seg = 8 if self.lod == 0 else 4
        for k in range(n_arches + 1):
            s0 = max(0, k * span - pier / 2); s1 = min(L, k * span + pier / 2)
            if s1 - s0 > .05: self.obox(A + u * ((s0 + s1) / 2), u, s1 - s0, width, z_bot, spring, mat, top=False)
        for k in range(n_arches):
            sc = (k + .5) * span; s0, s1 = sc - aw / 2, sc + aw / 2
            # spandrel walls both sides: polygon from the arch curve up to the deck
            for side in (-1, 1):
                pts = []
                for j in range(seg + 1):
                    ang = math.pi * j / seg
                    pts.append((sc + math.cos(ang) * r, spring + math.sin(ang) * r))
                # pts go from s1 (ang 0) over the top to s0
                poly = [(s1 + pier / 2, spring), (s1 + pier / 2, z_deck)] if False else []
                outline = [(s0 - pier / 2, spring), (s0 - pier / 2, z_deck), (s1 + pier / 2, z_deck), (s1 + pier / 2, spring)] + pts
                # outline: left-bottom, left-top, right-top, right-bottom, then arch from right (ang 0) to left
                P3 = [V3(A + u * s + v * (side * width / 2), z) for s, z in outline]
                f = self.face(P3 if side > 0 else list(reversed(P3)), mat)
                if f is not None:
                    f.normal_update()
                    if f.normal.to_2d().dot(v * side) < 0: f.normal_flip()
            # vault
            for j in range(seg):
                a0, a1 = math.pi * j / seg, math.pi * (j + 1) / seg
                s_0, z_0 = sc + math.cos(a0) * r, spring + math.sin(a0) * r
                s_1, z_1 = sc + math.cos(a1) * r, spring + math.sin(a1) * r
                self.quad(V3(A + u * s_0 + v * (width / 2), z_0), V3(A + u * s_1 + v * (width / 2), z_1),
                          V3(A + u * s_1 - v * (width / 2), z_1), V3(A + u * s_0 - v * (width / 2), z_0), mat)
        # deck + parapets
        self.obox((A + B) / 2, u, L, width, z_deck - .05, z_deck, 'ploce' if False else 'kamen')
        for side in (-1, 1):
            self.obox((A + B) / 2 + v * (side * (width / 2 - .2)), u, L, .4, z_deck, z_deck + parapet, mat)
            if self.lod == 0: self.obox((A + B) / 2 + v * (side * (width / 2 - .2)), u, L, .5, z_deck + parapet, z_deck + parapet + .1, 'okvir')

    def finish(self, name):
        bm = self.bm
        bmesh.ops.recalc_face_normals(bm, faces=[f for f in bm.faces if False])  # keep explicit orientation
        uv = bm.loops.layers.uv.new('UVMap')
        for f in bm.faces:
            f.normal_update()
            nrm = f.normal
            tile = MATS[f.material_index][2]
            if abs(nrm.z) > .97:
                t = Vector((1, 0, 0)); bb = Vector((0, 1, 0))
            else:
                t = Vector((0, 0, 1)).cross(nrm); t.normalize(); bb = nrm.cross(t); bb.normalize()
            for l in f.loops:
                p = l.vert.co
                l[uv].uv = (p.dot(t) / tile, p.dot(bb) / tile)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me); bm.free()
        me.update()
        ob = bpy.data.objects.new(name, me)
        for m in materials(): me.materials.append(m)
        bpy.context.scene.collection.objects.link(ob)
        return ob

_mats = None
def materials():
    global _mats
    if _mats: return _mats
    _mats = []
    for name, tex, tile, col, rough in MATS:
        m = bpy.data.materials.new(name); m.use_nodes = True
        nt = m.node_tree; bsdf = nt.nodes.get('Principled BSDF')
        bsdf.inputs['Roughness'].default_value = rough
        if tex:
            img = bpy.data.images.load(TEX + tex + '_d.jpg', check_existing=True)
            ti = nt.nodes.new('ShaderNodeTexImage'); ti.image = img
            mix = nt.nodes.new('ShaderNodeMix'); mix.data_type = 'RGBA'; mix.blend_type = 'MULTIPLY'
            mix.inputs['Factor'].default_value = 1.0
            nt.links.new(ti.outputs['Color'], mix.inputs[6]); mix.inputs[7].default_value = (*col, 1)
            nt.links.new(mix.outputs[2], bsdf.inputs['Base Color'])
            nimg = bpy.data.images.load(TEX + tex + '_n.jpg', check_existing=True); nimg.colorspace_settings.name = 'Non-Color'
            tn = nt.nodes.new('ShaderNodeTexImage'); tn.image = nimg
            nm = nt.nodes.new('ShaderNodeNormalMap'); nm.inputs['Strength'].default_value = 1.0
            nt.links.new(tn.outputs['Color'], nm.inputs['Color']); nt.links.new(nm.outputs['Normal'], bsdf.inputs['Normal'])
        else:
            bsdf.inputs['Base Color'].default_value = (*col, 1)
        _mats.append(m)
    return _mats

def tris(ob): return sum(len(p.vertices) - 2 for p in ob.data.polygons)

# ------------------------------------------------------------------ export + render
def export(name, objs, folder=None):
    folder = folder or OUT + name + '/'
    os.makedirs(folder, exist_ok=True)
    root = bpy.data.objects.new(name, None); bpy.context.scene.collection.objects.link(root)
    for o in objs: o.parent = root
    bpy.ops.object.select_all(action='DESELECT')
    root.select_set(True)
    for o in objs: o.select_set(True)
    common = dict(use_selection=True, export_format='GLB', export_yup=True, export_apply=True, export_normals=True,
                  export_texcoords=True, export_materials='EXPORT', export_extras=False, export_cameras=False, export_lights=False)
    bpy.ops.export_scene.gltf(filepath=folder + name + '.glb', export_image_format='JPEG', **common)
    # Unity copy: no embedded images (Unity builds its materials from shared textures by material name)
    bpy.ops.export_scene.gltf(filepath=folder + name + '-unity.glb', export_image_format='NONE', **common)
    for o in objs: o.parent = None
    bpy.data.objects.remove(root)

def setup_scene(sea=True, ground=None, ground_z=0.0):
    sc = bpy.context.scene
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x, sc.render.resolution_y = 960, 720
    sc.render.film_transparent = False
    sc.view_settings.view_transform = 'AgX'
    try: sc.view_settings.look = 'AgX - Medium High Contrast'
    except Exception: pass
    try: sc.eevee.taa_render_samples = 32
    except Exception: pass
    try: sc.eevee.use_shadows = True
    except Exception: pass
    w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
    nt = w.node_tree; bg = nt.nodes.get('Background')
    tc = nt.nodes.new('ShaderNodeTexCoord'); sep = nt.nodes.new('ShaderNodeSeparateXYZ'); ramp = nt.nodes.new('ShaderNodeValToRGB')
    nt.links.new(tc.outputs['Generated'], sep.inputs[0]); nt.links.new(sep.outputs['Z'], ramp.inputs['Fac'])
    ramp.color_ramp.elements[0].position = .5; ramp.color_ramp.elements[0].color = (.55, .62, .70, 1)
    ramp.color_ramp.elements[1].position = .85; ramp.color_ramp.elements[1].color = (.18, .34, .62, 1)
    bg.inputs['Color'].default_value = (.32, .48, .78, 1); bg.inputs['Strength'].default_value = .9
    sc.view_settings.exposure = -.35
    sun = bpy.data.lights.new('sun', 'SUN'); sun.energy = 3.2; sun.angle = math.radians(1.0)
    so = bpy.data.objects.new('sun', sun); sc.collection.objects.link(so)
    so.rotation_euler = (math.radians(50), 0, math.radians(200))
    if sea:
        me = bpy.data.meshes.new('sea'); bm = bmesh.new()
        vs = [bm.verts.new((x, y, 0)) for x, y in ((-600, -600), (600, -600), (600, 600), (-600, 600))]; bm.faces.new(vs); bm.to_mesh(me)
        ob = bpy.data.objects.new('sea', me); sc.collection.objects.link(ob)
        m = bpy.data.materials.new('sea'); m.use_nodes = True; b = m.node_tree.nodes.get('Principled BSDF')
        b.inputs['Base Color'].default_value = (.02, .10, .14, 1); b.inputs['Roughness'].default_value = .08
        me.materials.append(m)
    if ground:
        me = bpy.data.meshes.new('ground'); bm = bmesh.new()
        vs = [bm.verts.new((p.x, p.y, ground_z)) for p in ground]; bm.faces.new(vs); bm.to_mesh(me)
        ob = bpy.data.objects.new('ground', me); sc.collection.objects.link(ob)
        m = bpy.data.materials.new('ground'); m.use_nodes = True; b = m.node_tree.nodes.get('Principled BSDF')
        b.inputs['Base Color'].default_value = (.32, .30, .27, 1); b.inputs['Roughness'].default_value = .9
        me.materials.append(m)

def render(path, eye, target, lens=35, only=None, all_objs=None):
    sc = bpy.context.scene
    cam = bpy.data.cameras.new('cam'); cam.lens = lens; cam.clip_end = 3000
    co = bpy.data.objects.new('cam', cam); sc.collection.objects.link(co)
    co.location = eye
    d = Vector(target) - Vector(eye)
    co.rotation_euler = d.to_track_quat('-Z', 'Y').to_euler()
    sc.camera = co
    sun = bpy.data.objects.get('sun')
    if sun:
        hd = Vector((d.x, d.y, 0)).normalized(); ang = math.radians(40)
        hd = Vector((hd.x * math.cos(ang) - hd.y * math.sin(ang), hd.x * math.sin(ang) + hd.y * math.cos(ang), 0))
        L = Vector((hd.x, hd.y, -.85)).normalized()
        sun.rotation_euler = L.to_track_quat('-Z', 'Y').to_euler()
    if all_objs:
        for o in all_objs: o.hide_render = (o is not only)
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(co)

def clear():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    global _mats; _mats = None

# ------------------------------------------------------------------ helpers for building blocks
def exterior_edges(P, others, probe=.6):
    """edges of P whose outside (probe m out) is not inside another part: (a, b, exterior?)"""
    R = []
    for i in range(len(P)):
        a, b = P[i], P[(i + 1) % len(P)]
        e = b - a
        if e.length < .05: continue
        n = Vector((e.y, -e.x)).normalized()
        ext = True
        for t in (.25, .5, .75):
            q = a + e * t + n * probe
            if any(inside(O, q) for O in others): ext = False; break
        R.append((a, b, ext))
    return R

def windows_on_wall(m, a, b, z0, storeys, sh=2.9, spacing=3.4, seed=0, w=.8, h=1.25, shutters=None, door_ground=False,
                    density=1.0, small_ground=True, irregular=.25, max_per=99):
    L = (b - a).length
    if L < 1.6: return 0
    n = max(1, min(max_per, int(L / spacing)))
    cnt = 0
    for st in range(storeys):
        zs = z0 + st * sh + (1.0 if st > 0 else .9)
        for k in range(n):
            if h01(seed, st, k) > density: continue
            s = L * (k + .5) / n + (h01(seed, k, 7) - .5) * irregular * spacing
            s = max(w / 2 + .5, min(L - w / 2 - .5, s))
            if L < w + 1.0: continue
            sht = shutters if shutters is not None else ('z' if h01(seed, st, k, 2) < .6 else 's')
            if st == 0:
                if door_ground and k == n // 2:
                    m.window(a, b, s, z0 + .05, 1.0, 2.1, style='door', shutters=None); cnt += 1; continue
                if small_ground:
                    m.window(a, b, s, zs + .2, w * .7, h * .7, shutters=None); cnt += 1; continue
            closed = h01(seed, st, k, 5) < .18
            m.window(a, b, s, zs, w, h, shutters=sht, closed=closed); cnt += 1
    return cnt

def run(name, build, cams, scene_kw=None, lods=(0, 1, 2), compare=None):
    """build(lod) -> Mesh; exports <name>.glb (LOD0..2 children), renders the cams with LOD0"""
    clear()
    objs = []
    for lod in lods:
        m = build(lod)
        ob = m.finish(f'{name}_LOD{lod}')
        objs.append(ob)
    for mat in materials(): mat.use_backface_culling = True
    stats = {o.name: tris(o) for o in objs}
    print('TRIS', json.dumps(stats))
    export(name, objs)
    setup_scene(**(scene_kw or {}))
    outs = []
    for cname, eye, target, lens, lod in cams:
        p = RENDERS + f'model-{name}-{cname}.png'
        render(p, eye, target, lens, only=objs[lod], all_objs=objs)
        outs.append(p)
    json.dump({'tris': stats, 'renders': outs}, open(OUT + name + '/stats.json', 'w'), indent=1)
    return objs

def gcam(fr, x, z, h):
    v = fr.p(x, z); return (v.x, v.y, h)
