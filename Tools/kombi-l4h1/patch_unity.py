# Inserted before the body join of k2_build.py: wipers as wiper_L / wiper_R (spindle empties, glTF local X = parked arm
# direction, Y = up the glass, Z = into the cab), windscreen_fx (inner skin of the windscreen, 4 mm into the cab, UV per
# kabina-v3 contract)
def _loose(o):
    bm = bmesh.new(); bm.from_mesh(o.data); bm.verts.ensure_lookup_table(); bm.faces.ensure_lookup_table()
    lab = {}; k = 0
    for v in bm.verts:
        if v.index in lab: continue
        st = [v]; lab[v.index] = k
        while st:
            a = st.pop()
            for e in a.link_edges:
                b = e.other_vert(a)
                if b.index not in lab: lab[b.index] = k; st.append(b)
        k += 1
    fl = [lab[f.verts[0].index] for f in bm.faces]
    cen = {}
    for f, l in zip(bm.faces, fl): cen.setdefault(l, []).append(f.calc_center_median())
    bm.free()
    return fl, {l: sum(c, Vector()) / len(c) for l, c in cen.items()}
ws = OB['Windshield01']
fl, pc = _loose(ws)
co = np.array([v.co for v in ws.data.vertices]); ctr = co.mean(0); ev, evec = np.linalg.eigh(np.cov((co - ctr).T))
NIN = Vector(evec[:, 0].tolist()).normalized()
if NIN.y < 0: NIN = -NIN                                # into the cab (+Y = backwards), down the reclined glass
inner = max(pc, key=lambda l: pc[l].dot(NIN))
log('windscreen normal in', tuple(round(x, 3) for x in NIN), 'parts', {l: tuple(round(x, 3) for x in c) for l, c in pc.items()}, 'inner', inner)
fx = ws.copy(); fx.data = ws.data.copy(); link(fx); fx.name = 'windscreen_fx'; fx.data.name = 'windscreen_fx'
bm = bmesh.new(); bm.from_mesh(fx.data); bm.faces.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[f for f, l in zip(bm.faces, fl) if l != inner], context='FACES')
for v in bm.verts: v.co += NIN * 0.004
bm.to_mesh(fx.data); bm.free()
me = fx.data
for uvl in list(me.uv_layers): me.uv_layers.remove(uvl)
uv = me.uv_layers.new(name='UVMap')
xs = [v.co.x for v in me.vertices]; zs = [v.co.z for v in me.vertices]; xm = max(abs(x) for x in xs); z0, z1 = min(zs), max(zs)
for p in me.polygons:
    for li in p.loop_indices:
        c = me.vertices[me.loops[li].vertex_index].co; uv.data[li].uv = (0.5 - c.x / (2 * xm), (c.z - z0) / (z1 - z0))
me.materials.clear(); me.materials.append(M['Glass'])
log('windscreen_fx faces', len(me.polygons))
# wipers
wp = join([OB.pop('Brushs0102'), OB.pop('BrushRuber01')], 'wipers')
fl, pc = _loose(wp)
SP = {'L': Vector((0.6185, -2.512, 1.3395)), 'R': Vector((0.0455, -2.5515, 1.326))}
side = {l: ('L' if c.x > 0.07 else 'R') for l, c in pc.items()}
R_ = Vector((-1, 0, 0)); U_ = NIN.cross(R_).normalized(); B_ = Matrix((R_, -NIN, U_)).transposed()   # Blender local X, Y, Z
WIP = []
for tag in ('L', 'R'):
    o = wp.copy(); o.data = wp.data.copy(); link(o); o.name = 'wiper_' + tag + '_m'; o.data.name = o.name
    bm = bmesh.new(); bm.from_mesh(o.data); bm.faces.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[f for f, l in zip(bm.faces, fl) if side[l] != tag], context='FACES'); bm.to_mesh(o.data); bm.free()
    set_origin(o, SP[tag], B_)
    e = empty('wiper_' + tag, SP[tag], B_); parent_keep(o, e); WIP.append(e)
    log('wiper', tag, 'faces', len(o.data.polygons))
bpy.data.objects.remove(wp)
