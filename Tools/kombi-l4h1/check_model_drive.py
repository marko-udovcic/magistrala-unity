#!/usr/bin/env python3
"""GLB structure/geometry and longitudinal drivetrain checks. No Unity, Blender or third-party modules.
The acceleration estimate excludes PhysX tyre slip/suspension/wheel damping and is not a road-test result.
"""
import hashlib
import math
from pathlib import Path
import re
import tempfile

import sys
sys.dont_write_bytecode = True

from finish_glb import DEFAULT, finish, read

ROOT = Path(__file__).resolve().parents[2]


def values(doc, binary, index):
    a = doc['accessors'][index]
    v = doc['bufferViews'][a['bufferView']]
    count = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}[a['type']]
    fmt = {5126: 'f', 5123: 'H', 5125: 'I', 5121: 'B'}[a['componentType']] * count
    import struct
    stride = v.get('byteStride', struct.calcsize('<'+fmt))
    offset = v.get('byteOffset', 0) + a.get('byteOffset', 0)
    return [struct.unpack_from('<'+fmt, binary, offset+i*stride) for i in range(a['count'])]


def sub(a, b): return tuple(x-y for x, y in zip(a, b))
def dot(a, b): return sum(x*y for x, y in zip(a, b))
def cross(a, b): return (a[1]*b[2]-a[2]*b[1], a[2]*b[0]-a[0]*b[2], a[0]*b[1]-a[1]*b[0])


def rotate(p, q):
    u, w = q[:3], q[3]
    a, b = cross(u, p), cross(u, cross(u, p))
    return tuple(p[i]+2*(w*a[i]+b[i]) for i in range(3))


def check_sightlines(doc, binary):
    nodes = doc['nodes']
    names = {n['name']: i for i, n in enumerate(nodes)}
    parents = {child: i for i, n in enumerate(nodes) for child in n.get('children', [])}

    def point(i, p):
        n = nodes[i]
        p = tuple(p[j]*n.get('scale', [1]*3)[j] for j in range(3))
        p = rotate(p, n.get('rotation', [0, 0, 0, 1]))
        p = tuple(p[j]+n.get('translation', [0]*3)[j] for j in range(3))
        return point(parents[i], p) if i in parents else p

    p = doc['meshes'][nodes[names['steering_m']]['mesh']]['primitives'][0]
    wheel = [point(names['steering_m'], p) for p in values(doc, binary, p['attributes']['POSITION'])]
    indices = [v[0] for v in values(doc, binary, p['indices'])]
    eye = point(names['a_eye'], (0, 0, 0))
    p = next(p for p in doc['meshes'][nodes[names['body']]['mesh']]['primitives'] if doc['materials'][p['material']]['name'] == 'Gauges')
    gauge = [point(names['body'], p) for p in values(doc, binary, p['attributes']['POSITION'])]

    def blocked(target):
        direction = sub(target, eye)
        for j in range(0, len(indices), 3):
            a, b, c = (wheel[k] for k in indices[j:j+3])
            e1, e2 = sub(b, a), sub(c, a)
            h = cross(direction, e2)
            det = dot(e1, h)
            if abs(det) < 1e-9: continue
            offset = sub(eye, a)
            u = dot(offset, h)/det
            if not 0 <= u <= 1: continue
            q = cross(offset, e1)
            v = dot(direction, q)/det
            if v < 0 or u+v > 1: continue
            if 0 < dot(e2, q)/det < 1: return True
        return False

    low, high = min(gauge, key=lambda p: p[1]), max(gauge, key=lambda p: p[1])
    xmin, xmax = min(p[0] for p in gauge), max(p[0] for p in gauge)
    # Sample the two printed dial circumferences every 5 degrees, plus the four panel corners.
    for center in (.2625, .7375):
        for angle in range(0, 360, 5):
            a = math.radians(angle)
            u, v = center+.22*math.cos(a), .5+.44*math.sin(a)
            target = (xmin+(xmax-xmin)*u, low[1]+(high[1]-low[1])*v, low[2]+(high[2]-low[2])*v)
            assert not blocked(target), ('wheel hides dial', center, angle)
    assert all(not blocked(p) for p in gauge)
    print('  cab: 144 dial-perimeter rays + 4 panel corners clear of steering mesh (default view)')


def check_model(path):
    doc, binary = read(path)
    for view in doc['bufferViews']:
        assert view.get('byteOffset', 0) + view['byteLength'] <= len(binary)
    nodes = {n['name']: n for n in doc['nodes']}
    eye = nodes['a_eye']['translation']
    assert eye[1:] == [1.84, 1.21]
    assert abs(nodes['steering']['translation'][1]-1.418520851135254) < 1e-6
    sill = doc['meshes'][nodes['rear_floor_sill']['mesh']]['primitives'][0]
    ps = values(doc, binary, sill['attributes']['POSITION'])
    ix = [v[0] for v in values(doc, binary, sill['indices'])]
    ns = values(doc, binary, sill['attributes']['NORMAL'])
    assert len(ix) == 36 and len(ps) == 24
    assert abs(max(v[1] for v in ps)-.818) < 1e-6
    assert min(v[2] for v in ps) < -3.44 and max(v[2] for v in ps) > -3.402
    for i in range(0, len(ix), 3):
        a, b, c = (ps[j] for j in ix[i:i+3])
        assert dot(cross(sub(b, a), sub(c, a)), ns[ix[i]]) > 0, 'outward winding'
    # Every physical edge appears twice: closed sill, including its rear vertical face (no open black seam).
    edges = {}
    for i in range(0, len(ix), 3):
        tri = [ps[j] for j in ix[i:i+3]]
        for a, b in zip(tri, tri[1:]+tri[:1]):
            edge = tuple(sorted((a, b)))
            edges[edge] = edges.get(edge, 0)+1
    assert set(edges.values()) == {2}
    angles = []
    for name in ('mglass_L', 'mglass_R'):
        p = doc['meshes'][nodes[name]['mesh']]['primitives'][0]
        v = values(doc, binary, p['attributes']['POSITION'])
        ids = [x[0] for x in values(doc, binary, p['indices'])]
        normals = [cross(sub(v[ids[i+1]], v[ids[i]]), sub(v[ids[i+2]], v[ids[i]])) for i in range(0, len(ids), 3)]
        n = max(normals, key=lambda x: dot(x, x))
        angles.append(math.degrees(math.acos(abs(n[2])/math.sqrt(dot(n, n)))))
    if path.name == 'kombi-l4h1.glb':
        assert all(31 < angle < 34 for angle in angles), angles
        check_sightlines(doc, binary)
    # Finishing a finished GLB must change no bytes, geometry, images or IDs.
    with tempfile.TemporaryDirectory(prefix='magistrala-glb-') as folder:
        copy = Path(folder)/path.name
        copy.write_bytes(path.read_bytes())
        before = hashlib.sha256(copy.read_bytes()).digest()
        finish(copy)
        assert hashlib.sha256(copy.read_bytes()).digest() == before
    print(f'  geometry: closed sill, eye/column, mirror-face angles {angles[0]:.1f}/{angles[1]:.1f} deg; idempotent')


source = (ROOT/'Assets/Scripts/Truck/PlayerTruck.cs').read_text().split('VanSpec()')[1].split('/// TruckController')[0]


def scalar(name):
    return float(re.search(r'\b'+name+r'\s*=\s*([\d.]+)f?\b', source)[1])


def array(name):
    return [float(x.strip().rstrip('f')) for x in re.search(r'\b'+name+r'\s*=\s*new\[\]\s*\{([^}]+)', source)[1].split(',')]


points, torques, ratios = array('torqueRpm'), array('torqueFraction'), array('ratios')
assert len(points) == len(torques) and all(a < b for a, b in zip(points, points[1:]))


def torque(rpm):
    for i in range(1, len(points)):
        if rpm <= points[i]:
            t = max(0, min(1, (rpm-points[i-1])/(points[i]-points[i-1])))
            return torques[i-1] + t*(torques[i]-torques[i-1])
    return torques[-1]


def longitudinal(pedal):
    # Same shift, boost, force, rotating-inertia and drag equations as TruckController, on level ground at 50 Hz.
    dt = .02
    mass = scalar('mass')+scalar('payloadKg')
    radius, final, shift = scalar('radius'), scalar('finalDrive'), scalar('shiftTime')
    speed, rpm, boost, timer, since, gear = 0., 600., 0., 0., 10., 0
    acceleration = filtered = 0.
    shifts = []
    gas = pedal**scalar('pedalExponent')
    for step in range(5000):
        timer, since = max(0, timer-dt), since+dt
        ratio = ratios[gear]
        road = speed/radius*ratio*final*60/(2*math.pi)
        desired = max(scalar('idle'), road, scalar('idle')+gas*600 if gear == 0 else 0)
        rpm += (min(scalar('redline')+100, desired)-rpm)*dt*10
        if timer <= 0 and speed > 0:
            up = scalar('upLoad' if pedal > .6 else 'upCruise')
            down = scalar('downLoad' if pedal > .6 else 'downCruise')
            next_speed = max(0, speed-9.81*.0065*shift)
            next_rpm = next_speed/radius*ratios[min(gear+1, 5)]*final*60/(2*math.pi)
            if gear < 5 and ((road > up and next_rpm > down+60) or (road > scalar('redline')-40 and next_rpm > scalar('idle')+250)):
                shifts.append((gear+2, round(speed*3.6, 1), round(road)))
                gear, timer, since = gear+1, shift, 0
            elif gear > 0 and rpm < down and speed*3.6 > 1 and (since > shift+1.5 or rpm < scalar('idle')+120) and (gas < .6 or filtered < .05):
                if road*ratios[gear-1]/ratio < scalar('downLimit'):
                    gear, timer = gear-1, shift*.8
            ratio = ratios[gear]
        want = 0 if timer > 0 else min(1, gas*scalar('boostGain'))*min(1, rpm/1100)
        tau = scalar('turboRise') if want > boost else 1.2 if timer > 0 and gas > .05 else .35
        boost += (want-boost)*min(1, dt/tau)
        force = 0 if timer > 0 else scalar('torqueNm')*torque(rpm)*gas*(.5+.5*boost)*ratio*final*.85/radius
        rolling = mass*9.81*.0065
        if force > rolling:
            force = rolling+(force-rolling)/(1+scalar('rotatingInertia')*(ratio*final)**2)
        if rpm > scalar('redline'): force *= .1
        acceleration = (force-rolling-.5*1.225*scalar('dragArea')*speed**2)/mass
        filtered += (acceleration-filtered)*dt*2
        speed = max(0, speed+acceleration*dt)
        if speed*3.6 >= 50:
            return (step+1)*dt, shifts
    raise AssertionError('did not reach 50')


if __name__ == '__main__':
    for path in sorted(DEFAULT.glob('*.glb')): check_model(path)
    mass = scalar('mass')+scalar('payloadKg')
    assert mass == 3500
    peak = max(scalar('torqueNm')*torque(r)*r*2*math.pi/60/1000 for r in range(600, 4301))
    cruise = 25/scalar('radius')*ratios[-1]*scalar('finalDrive')*60/(2*math.pi)
    assert 100 < peak < 112 and 1950 < cruise < 2050
    for pedal in (.8, 1):
        time, shifts = longitudinal(pedal)
        assert 7 < time < 8.2, (pedal, time)
        assert all(2800 < rpm < 3000 for _, _, rpm in shifts)
        print(f'Longitudinal estimate, {mass:.0f} kg, pedal {pedal:.0%}: 0-50 {time:.2f}s; shifts {shifts}')
    print(f'Peak {peak:.1f} kW; 6th at 90 km/h: {cruise:.0f} rpm. PhysX road test still required.')
