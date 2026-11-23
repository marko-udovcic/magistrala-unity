# Magistrala (Unity): semafori na stvarnim semaforiziranim križanjima bez Markova modela križanja.
# Čita GameWorld (Assets/Generated/Game/Resources/MagistralaGame.asset, Igra: podaci) i piše Assets/Editor/semafori-bez-modela.json:
# po križanju prilazi (stop-crta, smjer, skupina, nosač, znakovi, zebra, oznake na kolniku) i plan signala.
# Pokretanje (iz korijena projekta): python3 -I Tools/semafori/semafori_gen.py [izlaz.json]
# Izvori položaja: graf igre (osi cesta), zebre Markovih pločica dionica-s06 / s13 (dionice.json objs, preko model-junctions.json),
# OSM traffic_signals / ortofoto za ulice kojih u igri nema (6, 7). Fotografije: BalkanDriver/references/fotografije/semafori/1–7.
import json, math, sys, re
L=open('Assets/Generated/Game/Resources/MagistralaGame.asset',encoding='utf-8').read().split('\n')
def sect(name):
    for i,l in enumerate(L):
        if l=='  '+name+':': return i
vre=re.compile(r'\{x: ([-\d.e]+), y: ([-\d.e]+), z: ([-\d.e]+)\}')
def vecs(start):
    out=[]; i=start+1
    while L[i].startswith('  - {'):
        m=vre.search(L[i]); out.append(tuple(float(v) for v in m.groups())); i+=1
    return out
nodes=vecs(sect('nodes')); pts=vecs(sect('pts'))
edges=[]; i=sect('edges')+1
cur=None
while i<len(L) and (L[i].startswith('  - ') or L[i].startswith('    ')):
    l=L[i]
    if l.startswith('  - '): cur={}; edges.append(cur); l='    '+l[4:]
    k,_,v=l.strip().partition(': ')
    if k.endswith(':'): k=k[:-1]; v=''
    cur[k]=v; i+=1
for e in edges:
    for k in ('a','b','p0','pn','oneway','closed','lanes','limit'): e[k]=int(e[k])
    try: e['name']=json.loads(e['name']) if e['name'].startswith('"') else e['name']
    except: pass
    e['pts']=[(p[0]-4200,p[1],-p[2]-5300) for p in pts[e['p0']:e['p0']+e['pn']]]
G={'nodes':[(n[0]-4200,n[1],-n[2]-5300) for n in nodes],'edges':edges}
E=G['edges']; N=G['nodes']
inc={}
for i,e in enumerate(E): inc.setdefault(e['a'],[]).append(i); inc.setdefault(e['b'],[]).append(i)
def other(ei,n): e=E[ei]; return e['b'] if e['a']==n else e['a']
def pts_from(ei,n):
    e=E[ei]; p=[(q[0],q[2]) for q in e['pts']]; return p if e['a']==n else p[::-1]
def arm(node, ei, L=120):
    """polyline from node outwards along edge ei, continued straight through further nodes"""
    out=pts_from(ei,node); cur=other(ei,node); prev=ei
    while poly_len(out)<L:
        best=None; bd=.85
        dx,dz=unit(out[-1][0]-out[-3][0], out[-1][1]-out[-3][1]) if len(out)>2 else unit(out[-1][0]-out[0][0],out[-1][1]-out[0][1])
        for ej in inc.get(cur,[]):
            if ej==prev: continue
            q=pts_from(ej,cur); k=min(2,len(q)-1); tx,tz=unit(q[k][0]-q[0][0],q[k][1]-q[0][1])
            c=tx*dx+tz*dz
            if c>bd: bd=c; best=ej
        if best is None: break
        out+=pts_from(best,cur)[1:]; prev=best; cur=other(best,cur)
    return out
def unit(x,z): l=math.hypot(x,z) or 1; return x/l,z/l
def poly_len(p): return sum(math.hypot(p[i+1][0]-p[i][0],p[i+1][1]-p[i][1]) for i in range(len(p)-1))
def at(p,d):
    for i in range(len(p)-1):
        a,b=p[i],p[i+1]; L=math.hypot(b[0]-a[0],b[1]-a[1])
        if d<=L or i==len(p)-2:
            f=min(1,d/L) if L>1e-6 else 0; t=unit(b[0]-a[0],b[1]-a[1])
            return (a[0]+(b[0]-a[0])*f, a[1]+(b[1]-a[1])*f), t
        d-=L
def straight(o,t,L=60): return [o,(o[0]+t[0]*L,o[1]+t[1]*L)]
def right(t): return (-t[1],t[0])
def R(v,n=2): return [round(v[0],n),round(v[1],n)]
def edge_into(node, compass):
    """the edge at node leaving closest to compass heading (deg, 0 north, 90 east)"""
    best=None; bd=-2
    tx,tz=math.sin(math.radians(compass)),-math.cos(math.radians(compass))
    for ei in inc[node]:
        q=pts_from(ei,node); k=min(3,len(q)-1); x,z=unit(q[k][0]-q[0][0],q[k][1]-q[0][1])
        c=x*tx+z*tz
        if c>bd: bd=c; best=ei
    return best
def tile(key):
    for j in json.load(open('/Users/macair/Documents/BalkanDriver/client/public/assets/world/split-kastela/model-junctions.json'))['junctions']:
        if j['key']==key: return j
def tl(j,lx,lz): l=j['lin']; return (j['x']+l[0]*lx+l[1]*lz, j['z']+l[2]*lx+l[3]*lz)

STOPGAP=3.0   # stop line this far before the zebra
def approach(poly, d, hw, group, mount, signs=(), paint=True, zebra=None, marks=(), name='', ped=False):
    """poly: road axis from the junction outwards; d: stop line distance from the junction along it. Travel = inwards."""
    (x,z),t_out=at(poly,d); t=(-t_out[0],-t_out[1]); r=right(t)
    a={'id':name,'p':R((x+r[0]*hw/2,z+r[1]*hw/2)),'axis':R((x,z)),'t':R(t,4),'w':round(hw,2),'group':group,'mount':mount,'signs':list(signs),'paint':paint}
    if zebra:
        zd,l,w,rw,zpaint=zebra
        (zx,zz),zt=at(poly,zd)
        a['zebra']={'p':R((zx,zz)),'t':R((-zt[0],-zt[1]),4),'l':l,'w':w,'rw':rw,'paint':zpaint}
    if marks:
        a['marks']=[]
        for k,dd in marks:
            (mx,mz),mt=at(poly,d+dd); a['marks'].append({'k':k,'p':R((mx+r[0]*hw/2,mz+r[1]*hw/2)),'t':R((-mt[0],-mt[1]),4)})
    return a

def plan(ped=False, cycle=None):
    if not ped:
        # main 36 s green, side 21 s; amber 3 s, all-red 2 s, red+amber 2 s
        return {'cycle':70,'phases':[
            {'group':'SG1','green':[0,36],'yellow':[36,39],'redYellow':[68,70]},
            {'group':'SG2','redYellow':[39,41],'green':[41,63],'yellow':[63,66]}]}
    # school / pedestrian crossing: an exclusive pedestrian phase (all vehicles red)
    return {'cycle':80,'phases':[
        {'group':'SG1','green':[0,32],'yellow':[32,35],'redYellow':[78,80]},
        {'group':'SG2','redYellow':[35,37],'green':[37,52],'yellow':[52,55]},
        {'group':'P1','green':[57,67]}]}

J=[]
MAIN=3.5
# ---------------------------------------------------------------- 1 s06 (tile dionica-s06: zebras and arms the owner's)
t6=tile('dionica-s06'); c=1507
W=arm(c,edge_into(c,265)); Ea=arm(c,edge_into(c,85)); S=arm(c,edge_into(c,180)); Nn=arm(c,edge_into(c,0))
zW=math.hypot(tl(t6,-34.4,1.79)[0]-N[c][0],tl(t6,-34.4,1.79)[1]-N[c][2]); zE=math.hypot(tl(t6,-9.3,-.58)[0]-N[c][0],tl(t6,-9.3,-.58)[1]-N[c][2])
zS=math.hypot(tl(t6,-24,8.5)[0]-N[c][0],tl(t6,-24,8.5)[1]-N[c][2])
print('s06 zebras',zW,zE,zS)
J.append({'key':'semafor-s06','title':'Kaštel Štafilić/Stari, Tuđmana × Polinovac / Put blata (škola)','c':R((N[c][0],N[c][2])),'plan':plan(),'offset':0,'approaches':[
  approach(W,zW+2.25+STOPGAP,3.8,'SG1','konzola',('prednost','pjesacki'),zebra=(zW,4.5,7.6,False,False),marks=(('x',5),('skola',11)),name='Tuđmana od zapada'),
  approach(Ea,zE+2.25+STOPGAP,3.8,'SG1','konzola',('prednost','pjesacki'),zebra=(zE,4.5,7.6,False,False),marks=(('x',5),('skola',11)),name='Tuđmana od istoka'),
  approach(S,zS+1.75+STOPGAP,3.0,'SG2','stup',('pjesacki',),zebra=(zS,3.5,7.4,False,False),name='Polinovac'),
  approach(Nn,7.0,3.0,'SG2','stup',(),name='Put blata'),
]})
# ---------------------------------------------------------------- 2 s13 (tile dionica-s13)
t13=tile('dionica-s13'); c=1594
W=arm(c,edge_into(c,265)); Ea=arm(c,edge_into(c,95)); Nn=arm(c,edge_into(c,20))
zE=tl(t13,76.1,3.82); zEd=math.hypot(zE[0]-N[c][0],zE[1]-N[c][2])
st=tl(t13,41.33,-10.04); stD=math.hypot(st[0]-N[c][0],st[1]-N[c][2])
print('s13 east zebra',zEd,'owner stop',stD)
apps=[
  approach(W,12,3.5,'SG1','konzola',('prednost',),name='Tuđmana od zapada'),
  approach(Ea,zEd+2.25+STOPGAP,3.5,'SG1','konzola',('prednost','pjesacki'),zebra=(zEd,4.5,7,False,False),name='Tuđmana od istoka (prije zebre)'),
  approach(Ea,zEd-2.25-STOPGAP,3.5,'SG1','stup',('pjesacki',),name='Tuđmana prema istoku (zebra iza križanja)'),
  approach(Nn,stD,3.0,'SG2','stup',(),paint=False,name='Put sv. Lovre od Ostroga'),
]
# the eastbound one at the zebra travels east: re-derive (approach() points inwards)
(x,z),tt=at(Ea,zEd-2.25-STOPGAP); r=right(tt); apps[2].update({'p':R((x+r[0]*1.75,z+r[1]*1.75)),'axis':R((x,z)),'t':R(tt,4)})
# owner's painted stop line (signals.stops of dionica-s13): exact position, no paint
apps[3]['p']=R(st)
zc=tl(t13,76.1,3.82)
J.append({'key':'semafor-s13','title':'Kaštel Lukšić, Tuđmana × Put sv. Lovre od Ostroga','c':R((N[c][0],N[c][2])),'plan':plan(True),'offset':23,'approaches':apps,
  'ped':[{'zebra':R(zc),'t':R(at(Ea,zEd)[1],4),'w':7}]})
# ---------------------------------------------------------------- 3 Sućurac Tuđmana × Tamunić / don A. Rubignonia (school)
c=119
W=arm(c,edge_into(c,274)); Ea=arm(c,edge_into(c,96)); Nn=arm(c,edge_into(c,358)); S=arm(c,edge_into(c,180))
zm=3.0+1.5+2.0; zs=MAIN+1.5+1.5
apps=[
  approach(W,zm+2+STOPGAP,MAIN,'SG1','konzola',('prednost','pjesacki','skola_ploca'),zebra=(zm,4,7,False,True),marks=(('x',4),('skola',10)),name='Tuđmana od zapada'),
  approach(Ea,zm+2+STOPGAP,MAIN,'SG1','konzola',('prednost','pjesacki','skola_ploca'),zebra=(zm,4,7,False,True),marks=(('x',4),('skola',10)),name='Tuđmana od istoka'),
  approach(Nn,zs+1.5+STOPGAP,3.0,'SG2','stup',('pjesacki',),zebra=(zs,3,6,False,True),name='Tamunić'),
  approach(S,zs+1.5+STOPGAP,3.0,'SG2','stup',('pjesacki',),zebra=(zs,3,6,False,True),name='Ulica don Ante Rubignonia'),
]
J.append({'key':'semafor-tamunic','title':'Kaštel Sućurac, Tuđmana × Tamunić / Ulica don Ante Rubignonia (škola)','c':R((N[c][0],N[c][2])),'plan':plan(True),'offset':41,'approaches':apps,
  'ped':[{'zebra':a['zebra']['p'],'t':a['zebra']['t'],'w':a['zebra']['w']} for a in apps]})
# ---------------------------------------------------------------- 4 Sućurac Tuđmana × Putaljski put
c=468
W=arm(c,edge_into(c,287)); Ea=arm(c,edge_into(c,107)); Nn=arm(c,edge_into(c,16))
apps=[
  approach(W,zm+2+STOPGAP,MAIN,'SG1','konzola',('prednost','pjesacki'),zebra=(zm,4,7,False,True),name='Tuđmana od zapada'),
  approach(Ea,zm+2+STOPGAP,MAIN,'SG1','konzola',('prednost','pjesacki'),zebra=(zm,4,7,False,True),name='Tuđmana od istoka'),
  approach(Nn,zs+1.5+STOPGAP,3.0,'SG2','stup',('pjesacki',),zebra=(zs,3,6,False,True),name='Putaljski put'),
]
# concrete half-sphere bollards on the corner island between the east arm and Putaljski put (photo 4, westbound view)
(ex,ez),et=at(Ea,7.5); (nx,nz),nt=at(Nn,7.5)
# corner point: east arm's north kerb / side street's east kerb
rE=right((-et[0],-et[1]))  # right of westbound = north
kE=(ex+rE[0]*(MAIN+1.0), ez+rE[1]*(MAIN+1.0))
rN=right((-nt[0],-nt[1]))  # right of southbound = west -> we want east kerb: minus
kN=(nx-rN[0]*(3.0+1.0), nz-rN[1]*(3.0+1.0))
domes=[]
for i in range(6):
    f=i/5; domes.append(R((kE[0]+(kN[0]-kE[0])*f, kE[1]+(kN[1]-kE[1])*f)))
J.append({'key':'semafor-putaljski','title':'Kaštel Sućurac, Tuđmana × Putaljski put','c':R((N[c][0],N[c][2])),'plan':plan(),'offset':12,'approaches':apps,'domes':domes})
# ---------------------------------------------------------------- 5 Kambelovac T (side street: OutsideStreets 'Ulica fra Fulgencija Careva')
c=1703
W=arm(c,edge_into(c,285)); Ea=arm(c,edge_into(c,105)); Nn=arm(c,edge_into(c,49))
apps=[
  approach(W,zm+2+STOPGAP,MAIN,'SG1','konzola',('prednost','pjesacki'),zebra=(zm,4,7,False,True),name='Tuđmana od zapada'),
  approach(Ea,zm+2+STOPGAP,MAIN,'SG1','konzola',('prednost','pjesacki'),zebra=(zm,4,7,False,True),name='Tuđmana od istoka'),
  approach(Nn,zs+1.5+STOPGAP,3.0,'SG2','stup',('pjesacki',),zebra=(zs,3,6,False,True),name='Ulica fra Fulgencija Careva'),
]
J.append({'key':'semafor-kambelovac','title':'Kaštel Kambelovac, Tuđmana × Ulica fra Fulgencija Careva','c':R((N[c][0],N[c][2])),'plan':plan(),'offset':52,'approaches':apps})
# ---------------------------------------------------------------- main road polyline through a point (edge 1723 for 6 and 7)
def main_split(x0, zg=-4600):
    # the Tuđmana edge crossing x0 nearest to zg (edge indices change with every Igra: podaci)
    best=None
    for e in E:
        if 'Tuđmana' not in str(e['name']): continue
        q=[(v[0],v[2]) for v in e['pts']]
        for i in range(len(q)-1):
            if (q[i][0]-x0)*(q[i+1][0]-x0)<=0 and q[i][0]!=q[i+1][0]:
                z=q[i][1]+(q[i+1][1]-q[i][1])*(x0-q[i][0])/(q[i+1][0]-q[i][0])
                if best is None or abs(z-zg)<best[0]: best=(abs(z-zg),e)
    e=best[1]; p=[(q[0],q[2]) for q in e['pts']]
    if p[0][0]>p[-1][0]: p=p[::-1]   # west -> east
    # find the param where x crosses x0
    for i in range(len(p)-1):
        if (p[i][0]-x0)*(p[i+1][0]-x0)<=0:
            f=(x0-p[i][0])/(p[i+1][0]-p[i][0]); o=(p[i][0]+(p[i+1][0]-p[i][0])*f, p[i][1]+(p[i+1][1]-p[i][1])*f)
            east=[o]+p[i+1:]; west=[o]+p[i::-1]
            return o, west, east
# ---------------------------------------------------------------- 6 Gomilica staggered (side streets: ulice-dodatne.json)
# side streets: Assets/Editor/ulice-dodatne.json (the streets the game lacked, built by OutsideStreets), from the junction outwards
UD={r['id']:[(q[0],q[2]) for q in r['points']] for r in json.load(open('Assets/Editor/ulice-dodatne.json',encoding='utf-8'))['roads']}
def side(key, o):
    """the street's axis from where it leaves the main road's centre o"""
    q=UD[key]; return [o]+q[1:]
oN,W6,_=main_split(UD['gomilica-sjever'][0][0]); oS,_,E6=main_split(UD['gomilica-jug'][0][0])
tE=unit(E6[1][0]-E6[0][0],E6[1][1]-E6[0][1])
northN=right((-tE[0],-tE[1]))  # right of westbound = north
nstreet=side('gomilica-sjever',oN); sstreet=side('gomilica-jug',oS)
zr=-3851.0
oZ,_,_=main_split(zr)
apps=[
  approach(W6,(oN[0]-zr)+2+STOPGAP,MAIN,'SG1','konzola',('prednost','pjesacki'),zebra=((oN[0]-zr),4,7,True,True),name='Tuđmana od zapada'),
  approach(E6,7.0,MAIN,'SG1','konzola',('prednost',),name='Tuđmana od istoka'),
  approach(nstreet,MAIN+1.5,3.0,'SG2','stup',(),paint=False,name='sporedna ulica sjever'),
  approach(sstreet,MAIN+1.5,3.0,'SG2','stup',(),paint=False,name='sporedna ulica jug'),
]
J.append({'key':'semafor-gomilica-pomaknuto','title':'Kaštel Gomilica, Tuđmana – pomaknuto križanje (lijevo pa desno)','c':R(((oN[0]+oS[0])/2,(oN[1]+oS[1])/2)),'plan':plan(),'offset':30,'approaches':apps})
# ---------------------------------------------------------------- 7 Gomilica T (Ulica kralja Krešimira: ulice-dodatne.json)
o7,W7,E7=main_split(UD['gomilica-t-sjever'][0][0])
kstreet=side('gomilica-t-sjever',o7)
apps=[
  approach(W7,zm+2+STOPGAP,MAIN,'SG1','konzola',('prednost','pjesacki'),zebra=(zm,4,7,False,True),name='Tuđmana od zapada'),
  approach(E7,zm+2+STOPGAP,MAIN,'SG1','konzola',('prednost','pjesacki'),zebra=(zm,4,7,False,True),name='Tuđmana od istoka'),
  approach(kstreet,MAIN+1.5,3.0,'SG2','stup',(),paint=False,name='Ulica kralja Krešimira'),
]
J.append({'key':'semafor-gomilica-t','title':'Kaštel Gomilica, Tuđmana × Ulica kralja Krešimira','c':R(o7),'plan':plan(),'offset':61,'approaches':apps})

# height hint (Unity y of the nearest graph point) for the ground probes
for j in J:
    best=1e18; y=15
    for e in E:
        for q in e['pts']:
            d=(q[0]-j['c'][0])**2+(q[2]-j['c'][1])**2
            if d<best: best=d; y=q[1]
    j['y']=round(y,2)
doc={'note':'Semafori na stvarnim semaforiziranim križanjima bez Markova modela križanja (potvrđeno njegovim fotografijama, references/fotografije/semafori/1–7). '
 'Generirano iz GameWorld grafa (scratchpad gen.py); koordinate igre (x istok, z jug), p = sredina stop-crte (desna polovica kolnika), t = smjer vožnje, '
 'w = duljina stop-crte; plan: SG1 glavna cesta, SG2 sporedna, P1 pješaci (isključiva faza); offset pomiče sat plana (s).','junctions':J}
out=sys.argv[1] if len(sys.argv)>1 else 'Assets/Editor/semafori-bez-modela.json'
json.dump(doc,open(out,'w'),ensure_ascii=False,indent=1)
print('wrote',out)
for j in J:
    print(j['key'],j['c'])
    for a in j['approaches']: print('   ',a['id'],a['group'],a['mount'],'p',a['p'],'t',a['t'],'z' if 'zebra' in a else '')
