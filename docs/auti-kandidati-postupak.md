# Auti kandidati → prometni auti (postupak)

Preuzeti modeli automobila (Sketchfab i sl., CC BY / CC0) postaju prometni auti alatom
**Magistrala/Auti: uvezi kandidata** (`Assets/Editor/KandidatImporter.cs`). Svaki auto je jedan zapis u
`Assets/Editor/auti-kandidati.json`; sve ostalo je zajedničko. Prvi auto prošao kroz postupak: **Zastava 101** (`zastava`).

## 1. Priprema

1. GLB u `Assets/Models/Vehicles/Kandidati/NN_ime.glb` (mapa je u .gitignore – izvor ostaje samo na disku; nakon spajanja grana
   provjeri da je datoteka i u glavnom projektu).
2. Licenca: CC BY / CC0 i komercijalna uporaba dopuštena. U `CREDITS.md` (odjeljak CC BY 4.0) naslov, autor, URL, licenca i
   „izmijenjeno: …”, te jedan redak u `MatePhone.AboutText` (mobitel → O igri) za svaki CC BY model.
3. Pogledaj model u sceni parkirališta: **Magistrala/Auti: parking kandidata** (`Assets/Scenes/AutiParking.unity`).

## 2. Zapis u `auti-kandidati.json`

```json
{ "kind": "zastava", "glb": "03_zastava_101.glb", "label": "Zastava 101", "length": 3.83,
  "materials": [ { "name": "Karoserija", "role": "body" }, { "name": "SmallStuf", "role": "trim" },
                 { "name": "unutri", "role": "interior", "size": 1024 }, { "name": "Tyre", "role": "wheel", "size": 1024 } ],
  "strip": [ { "mat": "SmallStuf", "px": [1440, 1845, 1480, 1965], "note": "natpis" } ],
  "lamps": [ { "type": "tail", "mat": "SmallStuf", "color": "red", "end": "rear", "depth": 0.15 } ],
  "colors": ["#e6e2d6", "#9c2a22"], "colorWeights": [1, 1], "weight": 8 }
```

| polje | značenje |
|---|---|
| `kind` | ime vrste u prometu (i mapa `Assets/Generated/Vehicles/Kandidati/<kind>/`) |
| `length` | stvarna duljina (m), model se skalira na nju |
| `up`, `front` | `auto` (os: dulja kratka stranica je širina; prednji kraj = suprotno od crvenih stražnjih svjetala) ili ručno: `up` = `y`, `-z`… (os u izvornom glTF-u), `front` = `+` / `-` |
| `parkFront` | samo za slike prije/poslije: kamo je okrenut sirovi model na parkiralištu (`+` = +z) |
| `glassNode` | regex imena čvora čija je geometrija staklo (Sketchfab često stavi staklo na materijal karoserije) |
| `materials[]` | po glTF materijalu (prefiks imena): `role` = `body` (boja se mijenja po primjerku), `trim`, `interior` (samo LOD0, prorijeđen), `wheel` (kotači se vrte), `glass`, `hide`; `size` tekstura (≤ 2048); `smoothMax`, `smoothMul`, `desat`, `bright`, `metalMul` = korekcije izgleda |
| `strip[]` | uklanjanje marke: `px` = pravokutnik u pikselima izvorne teksture (gornji lijevi kut, kao u pregledniku slika) – prebojava se bojom ruba (`fill: auto`) ili zadanom (`#rrggbb`), normal mapa ravna; ili `box` = 6 brojeva (m, okvir auta) – trokuti unutra se brišu |
| `probe` | okviri (6 brojeva svaki): u log se ispiše koji materijal i koji pikselni pravokutnik teksture leži u tom dijelu auta – za traženje amblema |
| `lamps[]` | `type` head / tail / ind (žmigavci, lijevo/desno po x) – trokuti materijala `mat` čija je boja u sredini `red` / `amber` / `light` / `dark` / `any`, u zadnjem `depth` dijelu duljine na kraju `end`, između `yMin` i `yMax`, okrenuti prema van (`facing`) |
| `colors`, `colorWeights` | boje laka i njihove težine (svaka boja = jedan materijal, jedno instancirano crtanje više) |
| `weight`, `vMul`, `a`, `b`, `T` | udio u prometu i vožnja (brzina, ubrzanje, kočenje, razmak) kao kod ostalih vrsta |
| `lod1`, `lod2`, `far`, `interiorLod0` | ciljni broj trokuta; LOD0 = model (unutrašnjost × interiorLod0) |
| `lodNear`, `lodMid` | udaljenosti prelaska LOD0 → LOD1 → LOD2 (m); preko 70 m daleki model (TrafficSystem.lod1Dist) |
| `plateStyle` | 0 = nova tablica s EU trakom, 1 = starija bez nje; `plateFrontY` / `plateRearY` visina sredine tablice (m), `plateW` / `plateH` |
| `paintHueTol`, `paintSatMin` | koji pikseli teksture karoserije su lak (ton blizu najčešćeg zasićenog tona) |
| `paintSmooth`, `coatSmooth`, `flake`, `grime`, `grimeH`, `glassAlpha` | izgled: lak, lakirni sloj, metalne čestice, prljavština (jačina, visina pragova), prozirnost stakla na LOD0 |

## 3. Pokretanje

1. **Magistrala/Auti: uvezi kandidata** (batch: `-executeMethod Magistrala.EditorTools.KandidatImporter.BatchImport -kandidat <kind>`).
   Log `[kandidat]` ispisuje: uspravljanje i mjerilo, kotače (polumjer, trag, međuosovinski razmak, koliko trokuta se vrti),
   brisanja/prebojavanja, svjetla, postotak piksela laka, broj trokuta po LOD-u.
   Izvorne teksture se spremaju u `Builds/kandidati/<kind>/` – tamo u pregledniku slika nađeš pravokutnike za `strip`.
2. **Magistrala/Build traffic** – vrsta ulazi u `TrafficData` (Uvoz sam pokreće Build traffic ako je otvorena scena s objektom Traffic).
3. Parkirani auti: **Magistrala/Rekviziti: postavi** (uzima 8 najčešćih vrsta osobnih auta).
4. Slike prije/poslije u sceni parkirališta: `-executeMethod Magistrala.EditorTools.KandidatImporter.BatchShots -kandidat <kind> -faza prije|poslije -shotdir <mapa> [-paint i]`
   (isti položaji kamere: 3/4 sprijeda, bok, 3/4 straga, detalj sprijeda, detalj straga).
5. U igri: player s `-carshow <kind> -carcam f34,left,rear,d40 -shot x.png -shots …` i `-kindw <kind>=0` / `-kindw only=<kind>` za A/B mjerenje.

## 4. Što alat radi (ukratko)

1. Čita GLB sam (svi čvorovi i matrice, slike) – ne ovisi o glTFast postavkama.
2. Uspravlja, okreće naprijed na +z, skalira na duljinu, ishodište na sredini tlocrta na tlu.
3. Kotači: povezani komadi materijala kotača koji dodiruju tlo = 4 gume; sve unutar njihovih okvira (naplatci, ratkapne) vrti se s
   njima; ostalo (rezervni kotač) ostaje nepomično. Ishodište mreže kotača = središte kotača (TrafficSystem ih vrti).
4. Teksture po materijalu za shader `Magistrala/TripoCar`: albedo (a = glatkoća) + maska (r = lak, b = metal). Pikseli laka postaju
   sivo sjenčanje → lak se boji po primjerku (`_PaintColor`).
5. Svjetla u zasebne podmreže s upaljenom kopijom materijala (`_LampAll`, `matOn`): kočnice (tail), farovi (head, zalazak/kiša), žmigavci.
6. Tablice: naši pravokutnici sprijeda i straga (zraka na karoseriju), u podmreži laka s uv.x 2..3 → atlas `tablice.png`
   (`Tools/vehicles/tablice.sh`, izmišljeni „ST 1234-AB”, logo igre umjesto grba); svaka boja laka ima drugu tablicu.
7. LOD-ovi (UnityMeshSimplifier, po podmreži): LOD0 = model + prozirno zatamnjeno staklo + prorijeđena unutrašnjost; LOD1/LOD2 bez
   unutrašnjosti, neprozirno tamno staklo; daleki model (~600 trokuta, boje po trokutu na zajedničkoj paleti).
8. Shader dodaci (`TripoCar.shader`, za postojeće Tripo aute isključeni): prljavština uz pragove i oko blatobrana (ambientCG CC0
   maska), sitne nepravilnosti laka, tablice, svjetla.

## 5. Zamke

- **Karoserija na prozirnom materijalu** (Zastava: „Karoserija BLEND”): alat sve crta neprozirno; staklo se odvaja po imenu čvora
  (`glassNode`), ne po materijalu.
- **Krivi prednji kraj**: automatika traži crvena stražnja svjetla; ako je unutrašnjost crvena ili svjetla nisu crvena, postavi `front`.
- **Amblemi u teksturi vs. geometriji**: natpis ZASTAVA je naslikan u atlasu sitnih dijelova → `strip.px`; izbočeni amblem je
  zasebna geometrija → `strip.box`. `probe` pomaže naći oboje.
- **Ton laka**: ako je model bijel/siv/crn (nema zasićenja), postotak piksela laka u logu je ~0 → lak se ne može prebojati iz
  teksture; tada smanji `paintSatMin` ili dodaj ručno (nije još podržano: maska iz zasebne slike).
- **Tablica „lebdi”**: visina `plateFrontY` / `plateRearY` mora pogoditi ravnu plohu nosača tablice (zraka ide po osi auta).
- **Kotači**: ako log kaže „only N tyres found”, kotači ostaju nepomični (nije greška u igri, samo se ne vrte).
- **Generated nije u gitu**: nakon spajanja grane uvoz i Build traffic treba ponoviti u glavnom projektu.

## 6. Zastava 101 (`zastava`) – rezultat 22. 11. 2026.

- Izvor: „Zastava 101 (Stojadin)”, Tomislav Tomljenović, Sketchfab, CC BY 4.0; 40 277 trokuta, 4 materijala, 11 tekstura 2K.
- Trokuti: **LOD0 33 091** (unutrašnjost prorijeđena na 40 %), **LOD1 13 964**, **LOD2 4 509**, **daleki 981** (prije: 40 277).
  Prelasci: LOD0 do 25 m, LOD1 do 45 m, LOD2 do 70 m, dalje daleki model.
- Uklonjeno: pločice s natpisom ZASTAVA na rešetki i prtljažniku (10 + 10 trokuta), krilati amblem (84 trokuta) i njegova sjena u
  teksturi karoserije, natpis u atlasu sitnih dijelova. Naplataka s logom nema (čelični kotači).
- Kotači: 4 zasebne mreže (r 0,30 m, trag 1,37 m, međuosovinski 2,58 m), vrte se.
- Boje (težine): bijela 22, bež 16, crvena 18, svijetloplava 16, tamnozelena 14, senf 10; tablice „ST …” starija varijanta bez EU trake.
- Udio u prometu: težina 9 (kao punat 10 / jugić 8); u parkiranim autima je (8 najčešćih vrsta).
- Učinak (player 1600×900, „oštro”, kamion stoji pred 01, `-tpxz -6106,-5595,96`, Mac M2 uz 2 otvorena Unity editora drugih
  pomoćnika – fps nepouzdan): GPU 18–19 ms u svim varijantama; samo Zastave (`-kindw only=zastava`) ~4 200 batcheva / 7,7–8,1 M
  trokuta prema samo Golub ~4 050 / 7,5–7,75 M (+3–4 %); miješani promet sa Zastavom 4 700–5 350 batcheva prema bez nje
  4 840–5 240 (unutar šuma).
