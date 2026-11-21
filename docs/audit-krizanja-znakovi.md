# Križanja, semafori i znakovi – pregled (Kaštela, Unity proba)

Stanje 21. 11. 2026., grana `znakovi`. Uspoređeno: igra (Unity) ↔ Markovi modeli križanja (mag-11-12 `-C`, mag-11-19 / -20
pločice) i njegovi rekviziti (`junction-overrides-modeli.json`, čvorovi `props`) ↔ stvarnost: istraživački paket
(`paket-approaches.json`, slike s ulice 2019.–2025.), OSM (`references/magistrala-paket/izvor/kastela-split-lanes.json`:
`highway=traffic_signals / stop / give_way / crossing`) i DGU ortofoto LiDAR 2022./23. (`tools/world/dgu/cache/dof/mosaic-lidar`).
Google Maps / Street View nisu korišteni.

Snimke: `BalkanDriver/references/fotografije/unity-proba/znakovi/` — `prije-*` (stara igra), `poslije-*` (nova),
`orto-*.jpg` (ortofoto križanja s ucrtanim: bijelo = obojene stop-crte modela, crveno = semaforski rekviziti, žuto = znakovi
na stupu / stupići, kvadratići = naši znakovi uz cestu: ljubičasto STOP / trokut, cijan pješački prijelaz, bijelo ograničenje,
plavo ostalo).

## 1. Što je bilo (prije)

| | prije | izvor problema |
|---|---|---|
| Semafori | 18 jednostavnih stupova s krakom (36 glava), **samo na stop-crtama AI koridora** (D8 i stara cesta, skupina SG1) | `SignalsBuilder` je gradio iz `TrafficData`, ne iz modela |
| Bočni prilazi (SG3, lijevi skretači SG2) | **bez ijedne glave** – igrač iz Lukšića (skladište) izlazi na D8 kroz 01 B bez semafora | – |
| Portali s pločama na D8 | nema (modeli ih imaju u `structure` / `boards`, Unity ih ne crta; web ih crta iz rekvizita) | `WorldBuilder.DRAW` = samo asfalt, otoci, oznake, pločnik, ograda, most |
| Prometni znakovi | **nijedan** (ni STOP, ni trokut, ni ograničenja, ni pješački, ni nazivi mjesta, ni ulice) | – |
| Natpisi | nigdje teksta (Unity nema web-ov canvas atlas) | – |
| Crveno za igrača | ne postoji (samo autopilot staje) | – |
| AI kod c09 | nestaju ~18 m prije zapreke (koridor završava 10 m prije, auti nestaju 8 m prije kraja) | `TrafficSystem.Despawn` |

## 2. Križanja u Unity svijetu (x igre −10 240 … −400)

16 modela križanja + 47 pločica dionica (c01–c09, d01–d25, s01–s13). **9 semaforiziranih** (plan signala u modelu):

| model | mjesto | prilazi (rekviziti vlasnika) | ploče (tekst) | OSM semafori u krugu 80 m | stvarnost (paket) | u igri sad |
|---|---|---|---|---|---|---|
| krizanje-01 | Lukšić, D8 × Put sv. Lovre | A portal; B, D konzola + stup + prednost; C portal (bez glava) + stup lijevog skretača | „↑ Split / Solin“, „↑ Split / K. Kambelovac“, „↑“ ×2 | 4 | A/C portal s pločama, desno stup s glavom ✔ | sve; C: dodan zamjenski desni stup s glavom SG1 (paket: `rightPost` signal_head_vertical) |
| krizanje-02 | Gomilica, D8 × fra F. Careva | A, B portal (3 ploče); C, D konzola | „←“, „↑ Split / Solin“, „↑ Split / K. Sućurac“, „↑ Šibenik / Trogir“ | 4 | isto ✔ | sve |
| krizanje-03 | Sućurac, D8 × Put Dragočeva | A, B portal; C, D konzola | „↑ Zagreb / Dubrovnik“, „↑ Split / Solin“, „↑ Šibenik / Trogir“, „↑ Trogir / K. Gomilica“ | 4 | isto ✔ (paket: *review*) | sve; prilazni rubovi preimenovani u mag-11-19 → skupina nađena po stop-crti |
| krizanje-04 | Sućurac, D8 × Putaljski put | A, B portal (3 ploče); C, D konzola | „← Groblje“, „← Kaštel Sućurac“ … | 4 | isto ✔ | sve |
| krizanje-05 | Sućurac, D8 × Suvača | A, B portal; C, D konzola | kao 03 | 4 | isto ✔ (paket: *review*) | sve |
| krizanje-19 | Štafilić, Tuđmana × Put poljoprivrednika | A konzola lijevo; B konzola desno; pješački | – | 1 | isto ✔ | sve |
| krizanje-20 | Stari, Tuđmana × bana Jelačića | A stupovi + STOP + pješački; B, C konzola | – | 3 | isto ✔ | sve |
| krizanje-21 | Stari, Tuđmana × Put Banovine | A, B konzola + pješački; C konzola | – | 1 | isto ✔ | sve |
| dionica-s04 | stara cesta Štafilić → Stari | A, B konzola (SG1 + all-red za poprečni) | – | **0** | nema ga u paketu | sve – **provjeriti**: OSM nema semafor kod s04 |

Nesemaforizirana: 41 (čvor D409 × D8, most), 42, 43, 45, 47, 51, 53, 54 + pločice. U modelima nemaju rekvizita; imaju
obojene stop-crte (41 ×3, 42, 45, d11, s01 ×2, s13) i zebre (41 ×4, 42 ×6, 47 ×3, 51 ×4, pločice ~60).

Stvarni semafori **bez modela** (OSM + graf `control: signals`, nema plana ni glava u igri):
stara cesta – s06 (Uska ulica / Polinovac / Put blata, jedan sklop), s13 (Put sv. Lovre × Tuđmana), Sućurac
(Tuđmana × Tamunić, Tuđmana × Putaljski put), Kambelovac (~−3250, −4450; −3840, −4580) i Gomilica (−4409, −4637) na
cestama uz more.

## 3. Što je sad u igri (Magistrala ▸ Build signals)

Sve iz podataka (`Assets/Editor/SignsBuilder.cs`, `SignArt.cs`); izvještaj gradnje: `Logs/znakovi-izvjestaj.txt`,
položaji svih stupova: `Logs/znakovi-polozaji.tsv`.

**Semafori** (iz Markovih rekvizita, mjere kao web `gantrySpec.ts`):
10 portala (rešetka, 24 ploče s tekstom), 19 konzola, 26 stupova kod stop-crte (s pločama prednost / pješački / STOP),
20 stupića za obilaženje (plavi znak), **82 glave** (81 iz rekvizita + 1 zamjenska). Svaka glava pokazuje svoju skupinu
iz plana modela (SG1 / SG3; lijevi skretači SG2 preko `laneMv` / `mv: left` / `signals.left`) na istom satu po kojem AI
staje; crveno-žuto prije zelenog (`redYellow` iz plana). Provjera pri gradnji: **18 / 18 AI stop-crta ima vidljivu glavu
svoje skupine** (prije: 18 / 18 ali samo D8 i stara cesta; bočni prilazi nisu imali ništa).

**Crveno za igrača**: 30 obojenih stop-crta semaforiziranih prilaza (sve skupine); kamion koji je prijeđe naprijed na
crveno → natpis „Prošao si kroz crveno!“ (bez kazne – to je odluka za Marka) i `[semafor]` u logu.

**Znakovi uz cestu**:

| vrsta | broj | pravilo / izvor |
|---|---|---|
| STOP | 17 (+1 u rekvizitu) | graf `approaches: stop`; obojene stop-crte modela bez plana |
| trokut (prednost prolaska) | 59 | graf `approaches: give_way` (OSM + Markovi modeli) |
| prednost (žuti romb) | 18 portal + 18 stup | rekviziti |
| pješački prijelaz (C02) | 115 (+26 rekviziti/portali) | zebre nesemaforiziranih modela i pločica, oba ruba, oba smjera |
| ograničenje brzine | 47 (30 / 40 / 50 / 70 / 80) | gdje se mijenja ograničenje koje pokazuje HUD (`GameWorld.limit`), + ponovljeno na D8 iza semafora |
| putokazi (paket) | 15 stupova | `road-signs.json` (npr. „Zračna luka "Split" / K. Štafilić“, „Lećevica, Radun, K. Stari“) |
| ograničenje mase 7 t | 4 | `road-signs.json` |
| naziv mjesta (ulaz + „kraj“ na poleđini) | 27 | granice naselja (OSM, `GameWorld.placeNames`) na D8, Ž-cestama i staroj cesti |
| ploča s imenom ulice | 155 | svaka imenovana ulica koja se spaja s drugom (ne D8) |

Natpisi: Inter SemiBold (SIL OFL 1.1, već u projektu za mobitel) rasterizira se pri gradnji u atlas
`Assets/Generated/Signs/SignAtlas.png` (4096 × 4096, BC7, 289 lica, čćžšđ ispravno). Jedan materijal, mreže po 256 m
(86 komada, 26 k trokuta), 510 box-sudarača na stupovima / stupovima portala / stupićima. Ugašene leće su dio te mreže;
upaljene crta `TrafficSignals` instancirano – tri poziva crtanja za sve semafore svijeta.

Preskočeno (u izvještaju gradnje): stupovi koji bi pali na kolnik a ne mogu se odmaknuti ≤ 3 m (ulice 124, trokuti 10,
ograničenja 17, pješački 23), duplikati, sve izvan svijeta.

## 4. Promet

- **c09 (D8 prema Trogiru zatvoren)**: koridor `D8 zapad` je sada *slijepi* (`deadEnd`): auti staju 1 m prije kraja kao
  na crvenom i nestaju samo kad ih kamera ne vidi (ili na zadnjih 150 m izvan pogleda i > 60 m od igrača); na zadnjih
  300 m se ne stvaraju. Isto za kraj stare ceste kod zatvaranja u s13. Prije: nestajali na ~18 m od zapreke pred igračem.
- **Čvor 41 bez AI-ja**: AI vozi samo uzdužne koridore (D8 oba smjera kroz 41, stara cesta); rampe 41 (D409 ↔ D8) nisu
  koridori – trebalo bi spajanje / razdvajanje koridora (prelazak auta s rampe u traku D8). **Nije riješeno** (vidi 5).
- AI poštuje samo stop-crte na svojim koridorima (SG1 kroz D8 / staru cestu); bočne prilaze nitko od AI-ja ne vozi.

## 4a. Učinak (fps)

Mjereno u playeru (1600×900, srednja kvaliteta, kamion stoji pred 01 A / 20 A) – **nepouzdano**: istodobno su radila
tri Unity editora i build drugih agenata (isti player je između dva pokretanja skakao 15 ↔ 40 fps). Brojači crtanja
(ProbeBench `[perf]`) kod 01: prije ~4000 batcheva / ~4800 draw calla, poslije ~3550 / ~4400 (promet varira); kod 20
prije ~2000 / 2140, poslije ~2080 / 2225 (+4 %). Trokuta +0,5 %. Znakovi dakle ne bi trebali prijeći −5 %; treba
ponoviti mjerenje na mirnom stroju (`-nojob -tpxz -6106,-5595,96 -nohud`, 75 s).

Snimke: `prije-01-A-ulica`, `prije-01-B-ulica`, `prije-20-A-ulica`, `prije-01-dron`, `prije-42-ulica`;
`poslije-*` iste pozicije; `poslije-01-B-semafor-faze.jpg` (crveno → žuto → zeleno), `poslije2-01-B-semafor_t0*.png`
(instancirane leće), `poslije-c09-zapreka_t0*.png` (c09), `orto-<model>.jpg` (17 križanja na ortofotu).

## 5. Što je ostalo (po važnosti)

1. **Rampe čvora 41 i bočne ceste bez AI-ja** – potreban model spajanja koridora (merge / diverge) u `TrafficSystem`.
2. **Stvarni semafori bez modela** (s06, s13, Sućurac Tamunić / Putaljski put na staroj cesti, Kambelovac, Gomilica uz
   more): treba Markov plan ili odluka da se ondje stave semafori s generičkim planom.
3. **Natpisi na tvrtkama / totemima** (`model-buildings.json` `sign`, npr. GRAĐEVINSKI MATERIJAL, PEKARA, GORIVO) – i dalje
   prazni; `SignArt` (atlas + Inter) se može iskoristiti u `BuildingsBuilder` (područje agenta za zgrade).
4. Pješački semafori na semaforiziranim zebrama (nema ih u rekvizitima ni u paketu).
5. Izgled ploča s imenima ulica (plave, bijela slova) i naziva mjesta (bijele, crni tekst) je pretpostavka po
   Pravilniku; provjeriti s fotografijama na terenu. Romb „cesta s prednošću“ uz glavne ceste izvan križanja nije postavljen.
6. s04: model ima semafor, OSM ne – provjeriti na terenu.
7. Kazna / posljedica za crveno (sad samo natpis).

## 6. Semafori bez Markova modela (grana `semafori`)

Sedam stvarnih semaforiziranih križanja koja nemaju model križanja (Marko ih je potvrdio fotografijama,
`references/fotografije/semafori/1–7`; #6 je Street View – samo raspored). Podaci: `Assets/Editor/semafori-bez-modela.json`
(generira `Tools/semafori/semafori_gen.py` iz GameWorld grafa, zebri pločica s06 / s13 i OSM `traffic_signals`), gradnja
`SignsBuilder.Extra.cs` (Build signals), planovi i AI stop-crte `TrafficBuilder` (Build traffic).

| # | ključ | prilazi (SG1 glavna, SG2 sporedna) | plan |
|---|---|---|---|
| 1 | semafor-s06 | Tuđmana Z / I (konzola, romb, pješački), Polinovac / Put blata (stup); ŠKOLA + X na kolniku oba glavna prilaza | 70 s, 2 faze |
| 2 | semafor-s13 | Tuđmana Z (konzola), I prije zebre (konzola), prema I na zebri (stup), Put sv. Lovre (stup, Markova stop-crta) + pješački semafori na zebri | 80 s, + pješačka faza |
| 3 | semafor-tamunic | Tuđmana Z / I (konzola s pločom ŠKOLA), Tamunić / Rubignonia (stup); 4 zebre, ŠKOLA + X; pješački semafori | 80 s, + pješačka faza |
| 4 | semafor-putaljski | Tuđmana Z / I (konzola), Putaljski put (stup); 3 zebre; betonske polukugle na otoku | 70 s |
| 5 | semafor-kambelovac | Tuđmana Z / I (konzola), fra F. Careva (stup); 3 zebre | 70 s |
| 6 | semafor-gomilica-pomaknuto | Tuđmana Z (konzola, crveno-bijela zebra) / I (konzola), sjeverna i južna sporedna (stupovi, zajednička SG2) | 70 s |
| 7 | semafor-gomilica-t | Tuđmana Z / I (konzola), Ulica kralja Krešimira (stup) | 70 s |

Faze: zeleno, žuto 3 s, sve crveno 2 s, crveno-žuto 2 s; svaki plan ima pomak (`offset`) na zajedničkom satu.
AI: novi koridori „Tuđmana istok / zapad“ (graf igre, od Lukšića x −6052 do x −430) voze glavnu cestu kroz 3–7; 1 i 2 su na
staroj cesti. Sporedne ulice nemaju AI (kao i drugdje). Ulice 6 i 7 (sporedne) nisu u igri: samo stupovi sa semaforima, bez boje.
