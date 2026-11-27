# kasteli-postava.json za Unity (Assets/Editor/): ishodište modela (= position iz kasteli.json, ishodište Blender scene),
# način visine, i poligoni (igra x,z) pod kojima se generirane kuće ne grade.
import json
d = json.load(open('/Users/macair/Documents/BalkanDriver/tools/world/riva-extras/kasteli.json'))
K = {k['id']: k for k in d['kasteli']}
def rings(g):
    if not g: return []
    ps = [g['coordinates']] if g['type'] == 'Polygon' else g['coordinates']
    return [[[round(x, 2), round(z, 2)] for x, z in p[0]] for p in ps]
def part(k, kind):
    return [p for p in k['notableParts'] if p.get('kind') == kind]
out = []
k = K['kastel_gomilica_kastilac']
out.append(dict(name='kastilac', id=k['id'], glb='kastilac', origin=k['position'], base='sea', y=0.0,
                exclude=rings(k['precinct']) + rings(part(k, 'bridge')[0]['footprint'])))
k = K['kastel_kambelovac_cambi']
out.append(dict(name='cambi', id=k['id'], glb='cambi', origin=k['position'], base='ground', y=0.0,
                exclude=[r for p in part(k, 'wing') for r in rings(p['footprint'])],
                excludeIds=['-5_-5:205']))   # 48 % under the tower palace (kasteli.json currentGameBuildings), its rest a sliver
k = K['kastel_luksic_vitturi']
out.append(dict(name='vitturi', id=k['id'], glb='vitturi', origin=k['position'], base='ground', y=0.0,
                exclude=rings(k['precinct']) + rings(k['footprint'])))
k = K['kastel_novi_cipiko']
out.append(dict(name='novi', id=k['id'], glb='novi', origin=k['position'], base='ground', y=0.0,
                exclude=rings(k['footprint'])))
k = K['kastel_stari_cipiko']
out.append(dict(name='cipiko-stari', id=k['id'], glb='cipiko-stari', origin=k['position'], base='ground', y=0.0,
                exclude=rings(k['footprint'])))
k = K['kastel_stafileo']
out.append(dict(name='stafileo', id=k['id'], glb='stafileo', origin=k['position'], base='ground', y=0.0,
                exclude=rings(k['footprint'])))
json.dump({'v': 1, 'note': 'Kašteli (Magistrala/Kašteli: postavi). origin = ishodište modela u igri (x istok, z jug); base sea: y = razina mora + y; ground: prosjek tla ispod tlocrta + y. exclude: tlocrti pod kojima Build tile buildings ne gradi kuće. Zvonik sv. Petra nema potvrđen položaj: nije postavljen.', 'kasteli': out},
          open('/Users/macair/Documents/MagistralaUnity-kasteli/Assets/Editor/kasteli-postava.json', 'w'), ensure_ascii=False, indent=1)
print('ok', len(out))
