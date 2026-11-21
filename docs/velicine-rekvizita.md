# Veličine rekvizita — Magistrala, grana velicine

Sve trojke su **X × Y × Z u metrima** u lokalnom okviru gotovog rekvizita, nakon `yaw`; Y je visina. „Prije” su postavke početnog commita ovog zadatka. „Stvarno” su ciljne dimenzije iz zadatka odnosno razumna veličina nestandardiziranog predmeta, ne izmjera konkretnog proizvođača. Tablica opisuje **rezultat sljedećeg uvoza**, a ne izmjeren prikaz postojeće scene.

Izračun: `python3 -I Tools/props/measure.py --check`. Skripta radi bez Unityja i bez dodatnih paketa. `--baseline <git-revizija>` računa stare sidecare nad istim GLB-ovima (GLB-ovi nisu promijenjeni). Hijerarhiju kamiona računa preko osam kutova POSITION accessor min/max, node TRS/matrica i svih roditelja. Za Tripo/Poly Haven koristi stvarne POSITION vrhove prvog primitivea prvog mesha, jer upravo to radi `TripoPropImporter.ReadGlb`: ignorira node transformacije, zrcali X, primjenjuje yaw, zatim normalizira.

`fit=x/y/z` bira tu os; `xz` najveću vodoravnu; ostalo najveću od svih triju. Uniformni faktor je `size / duljina_odabrane_osi`. Pozitivni `sx/sy/sz` zamjenjuju samo svoju os apsolutnom mjerom. Pivot postaje sredina dna. `cull`, LOD i položaj ne mijenjaju veličinu. Standardne osnove paleta, četiri gajbe, visina klupe i deformirana AI transpaleta/romobil zahtijevaju ciljane korekcije pojedinih osi; ostale osi zadržavaju izvorne omjere. Uniformno povećanje svih predmeta povećalo bi i već prevelike predmete.

| Rekvizit | Prije (m) | Stvarno / cilj (m) | Nakon (m) | Promijenjena datoteka |
|---|---|---|---|---|
| lonac-biljka-lisce | 0.726 × 0.655 × 0.666 | krošnja ~0.7 široka, ~0.65 visoka | 0.726 × 0.655 × 0.666 | — (provjereno, zadržano) |
| lonac-biljka | 0.494 × 0.355 × 0.494 | lonac Ø ~0.5, H ~0.35 | 0.494 × 0.355 × 0.494 | — (provjereno, zadržano) |
| stol-kafic | 0.750 × 0.750 × 0.720 | ploha 0.6–0.8; H 0.72–0.75 | 0.750 × 0.750 × 0.720 | — (provjereno, zadržano) |
| stolica-plasticna | 0.627 × 0.860 × 0.613 | H ~0.80–0.86; sjedalo ~0.45 | 0.627 × 0.860 × 0.613 | — (provjereno, zadržano) |
| teglica | 0.246 × 0.205 × 0.243 | Ø ~0.25; H ~0.20 | 0.246 × 0.205 × 0.243 | — (provjereno, zadržano) |
| aparat-gorivo | 1.769 × 1.800 × 0.968 | H 1.9–2.2 (dvostruki aparat) | 1.966 × 2.000 × 1.076 | `Assets/Models/Props/Tripo/aparat-gorivo.json` |
| bacva-pivo | 0.441 × 0.600 × 0.439 | H 0.53–0.60; Ø ~0.40–0.44 | 0.441 × 0.600 × 0.439 | — (provjereno, zadržano) |
| gajbe-hrpa | 1.537 × 1.400 × 1.032 | više gajbi: hrpa ~1.5 × 1.4 × 1.0 | 1.537 × 1.400 × 1.032 | — (provjereno, zadržano) |
| gajbe | 0.400 × 1.042 × 0.401 | 4 gajbe po 0.40 × 0.30 × 0.30; stup H 1.20 | 0.400 × 1.200 × 0.300 | `Assets/Models/Props/Tripo/gajbe.json` |
| klupa-bijela | 1.800 × 0.962 × 0.742 | L 1.8–2.0; H ~0.8; sjedalo ~0.45 | 1.900 × 0.800 × 0.784 | `Assets/Models/Props/Tripo/klupa-bijela.json` |
| kolica-rucna | 1.200 × 0.724 × 0.553 | transpaleta L 1.55; H 1.20; W 0.55 | 1.550 × 1.200 × 0.550 | `Assets/Models/Props/Tripo/kolica-rucna.json` |
| konjanik | 2.536 × 2.600 × 0.758 | odobreno: H 2.6 + postojeći stup | 2.536 × 2.600 × 0.758 | — (provjereno, zadržano) |
| kutija | 0.500 × 0.294 × 0.359 | kartonska kutija ~0.50 × 0.30 × 0.36 | 0.500 × 0.294 × 0.359 | — (provjereno, zadržano) |
| paleta-folija | 1.159 × 1.097 × 1.200 | osnova 0.8 × 1.2; H 0.144 + teret 1.10 | 0.800 × 1.244 × 1.200 | `Assets/Models/Props/Tripo/paleta-folija.json` |
| paleta-kutije-puna | 0.782 × 0.559 × 1.200 | osnova 0.8 × 1.2; s kutijama H ~1.3 | 0.800 × 1.300 × 1.200 | `Assets/Models/Props/Tripo/paleta-kutije-puna.json` |
| paleta-kutije | 1.210 × 1.300 × 1.193 | osnova 1.2 × 0.8; s kutijama H ~1.3 | 1.200 × 1.289 × 0.800 | `Assets/Models/Props/Tripo/paleta-kutije.json` |
| paleta | 1.200 × 0.160 × 0.800 | 1.20 × 0.144 × 0.80 | 1.200 × 0.144 × 0.800 | `Assets/Models/Props/Tripo/paleta.json` |
| romobil | 0.843 × 1.300 × 1.300 | L ~1.1–1.15; H 1.15; upravljač W ~0.5 | 0.500 × 1.150 × 1.150 | `Assets/Models/Props/Tripo/romobil.json` |
| stara-maslina | 13.759 × 9.000 × 12.347 | H 4–6; široka stara krošnja | 8.408 × 5.500 × 7.546 | `Assets/Models/Props/Tripo/stara-maslina.json` |
| voda-paleta | 1.200 × 0.776 × 1.104 | osnova 1.2 × 0.8; H 0.144 + teret 1.0–1.2 | 1.200 × 1.244 × 0.800 | `Assets/Models/Props/Tripo/voda-paleta.json` |
| zardinjera | 1.000 × 0.941 × 0.498 | ukrasni visoki planter L 1.0; H ~0.94; D ~0.5 | 1.000 × 0.941 × 0.498 | — (provjereno, zadržano) |
| klima | nema GLB-a; cilj 0.800 × 0.560 × 0.300 | 0.80 × 0.56 × 0.30 | konfiguracija zadržana; geometrija nije mjerljiva | — (`Tripo/klima.json`) |
| oleander | nema GLB-a; cilj H 1.900 | H 1.9–2.2 | konfiguracija zadržana; geometrija nije mjerljiva | — (`Tripo/oleander.json`) |

`gajbe.glb` nije jedna gajba: projekcija vrhova pokazuje stup četiri gajbe. Nije smanjen na ukupno 0.30 m. `gajbe-hrpa` je složena hrpa; ukupna omotnica nije mjera pojedine gajbe. Bez razdvajanja njezine spojene geometrije pojedinačne gajbe nisu certificirane standardnim mjerama.

`kolica-rucna.glb` je transpaleta (niska platforma/vilice, kotači i ručka), unatoč nazivu. U ovom worktreeju nema zasebnog modela uspravnih ručnih kolica; cilj H 1.15–1.20 / W 0.5 nije primijenjen na pogrešnu vrstu predmeta. Ukupne mjere transpalete popravljene su u sidecaru; to nije zasebno remodeliranje vilica na točno 1.15 m.

Poly Haven stol i stolica već imaju realne ukupne mjere. Stol je približno kvadratan, ne okrugao. Lonac i lišće su dva odvojena modela; `PropsBuilder.LeavesOnPot = (0.07, 0.3135, -0.148) * 0.69` dodaje (0.0483, 0.216315, -0.10212) m. Sastavljena biljka ima H 0.871 m, omotnicu približno 0.726 × 0.871 × 0.682 m. Taj relativni položaj je zadržan. Visina sjedala i detalji spojene AI geometrije zahtijevaju vizualnu provjeru nakon uvoza; ukupni bounds nisu dokaz ergonomije svih dijelova.

## Popis ostalih mjesta koja određuju mjerilo

| Kod / predmet | Prije (m ili faktor) | Stvarno / kriterij | Nakon | Promijenjena datoteka |
|---|---|---|---|---|
| PropsBuilder.Put: svi rekviziti dvorišta, firmi, benzinskih, terasa i znamenitosti | prefab × 1; broj kopija `n`; vertikalni razmak slaganja 0.985 × H | veličina iz sidecara; `n` nije faktor mjerila | isto, s novim uvoznim mjerama | sidecari iz gornje tablice |
| PropsParked: klupe uz terene, parkove i rivu | isti `klupa-bijela` prefab × 1 | L 1.8–2.0 / H ~0.8 | 1.900 × 0.800 × 0.784 | `Tripo/klupa-bijela.json` |
| PropsBuilder.Umbrella | Ø 2.9, rub H 2.2, vrh H 2.62 | veliki terasni suncobran | isto | — |
| Yards: ograde i kapije | H 1.1–1.6; otvor kapije ~3.0; debljina prema HouseGeo | stvarne okućnice; bez globalnog umanjenja | isto | — |
| GroundCover: nogometni / mali gol | otvor 7.32 × 2.44 / 5 × 2 / 3 × 2; profil 0.12; dubina 0.8 × H | odgovarajuća veličina gola po terenu | isto (vanjska omotnica uključuje profil) | — |
| GroundCover: koš | obruč H 3.05, Ø 0.46; ploča 1.80 × 1.06; stup H 3.95 | standardna visina koša | isto | — |
| GroundCover: tenis, ograde | stup mreže H 1.07, mreža H 1.02; ograde 2.2–5.0 | rekreativni teren; širina iz geometrije terena | isto | — |
| PropsParked: parkirani automobili | TrafficData dimenzije, L 3.3–5.4, H ≤2.1; bez dodatnog scalea | osobni automobili | isto; kolajder W=2×halfW, H=h, L=len | — |
| MedianHedge: oleander | izvor H 1.9; crown XZ ×0.75; instanca ×1.00–1.16; ukop 0.08 | ukupni H 1.90–2.204, vidljivi H 1.82–2.124 | isto (nedostaje GLB za bounds širine) | — |
| MedianHedge: ostalo raslinje | core ×1.15–1.30; fill ×1.1–1.4; base ×0.7–1.1 | promjenjivo prirodno raslinje | isto; ovisi o package prefabu | — |
| NaturePass: borovi i crnika | borovi XZ ×1.22 / Y ×0.82; dodatne instance i rasponi po vrsti | prirodne varijacije; nema zajedničkog „prop” umanjenja | isto; package nije prisutan za točan bounds | — |
| NaturePass: stijene | instanca ×0.4675–1.265; ukop 1.2 | prirodne stijene promjenjive veličine | isto; package nije prisutan | — |
| NaturePass: sitno raslinje | visine/širine kroz DetailPrototype min/max iz DetailSrc | varijacije po vrsti; nije namještaj | isto; package nije prisutan | — |
| StreetsBuilder | ceste/pločnici/ograde iz metričkih presjeka; nema dodatnog scalea namještaja | 1 Unity jedinica = 1 m | isto | — |
| SignsBuilder: ploče znakova | prednost 0.8; pješački 0.6; STOP 0.7; trokut 0.9; krug 0.6; putokaz H 1.2 | uobičajene veličine prometne signalizacije iz projekta | isto | — |
| SignsBuilder: semafori / portali | leća Ø0.20 ili 0.28; glava prema leći; portal H 7.35, raspon iz podataka (zadano 12) | cestovna signalizacija u metrima | isto | — |
| SignsBuilder.Extra: pješački semafor / konzola | glava 0.32 × 0.63 × 0.20; konzola vrh ~5.93 | uobičajena cestovna oprema | isto | — |
| RivaBuilder | površine, zidovi, pontoni i stabla iz podataka; nema posebnog scalea klupe | geometrija obale u metrima | isto; klupe postavlja PropsBuilder | — |
| autobusna nadstrešnica / ulična lampa | nema zasebnog modela ni placement funkcije u navedenim builderima | nadstrešnica H~2.5 / L3–4; lampa H8–12 | nema dostupne geometrije za mjerenje; nisu izmišljene izmjere | — |

Mjere package raslinja i objekata u vanjskim regionalnim paketima nisu provjerljive iz ovog worktreeja. Vanjske mape nisu otvarane. Ovaj popis razlikuje metriku koda od nedostajuće geometrije; ne tvrdi da je izmjeren svaki predmet zapečen u nedostupnom regionalnom GLB-u.

## Kamion i istovar

`TruckController` zahtijeva metre i jedinični scale korijena/modela. `FirmUnload` ne smanjuje modele. GLB čvorovi paleta već su metrički ispravni; njihove mesh scale vrijednosti (npr. 0.82) dio su normalizirane GLB geometrije, ne dodatno umanjenje rekvizita.

| Čvor / predmet | Prije (X × Y × Z m) | Stvarno / kriterij | Nakon | Promijenjena datoteka |
|---|---|---|---|---|
| load0_pallet_wood, load1_pallet_wood | red 1.640 × 0.144 × 1.200 | dvije palete 0.800 × 0.144 × 1.200, razmak 0.040 | isto | — |
| load_pallets_0 | 1.640 × 1.544 × 1.200 | palete s teretom ispod H 2.2 | isto | — |
| load_pallets_1 | 1.640 × 1.744 × 1.200 | palete s teretom ispod H 2.2 | isto | — |
| ukupni teret | 1.640 × 1.744 × 2.450 | sanduk približno 2.4 × 2.2 × 6.1 | isto | — |
| door_roll, zatvoreno | 2.230 × 2.240 × 0.045 | vrata stvarnog sanduka; ne povećavati zasebno | isto | — |
| tail_lift_plate, horizontalno | 2.300 × 0.050 × 1.350 | red dubine 1.2 stane s 0.15 ukupne rezerve | isto | — |
| odložena paleta: visina nad tlom | pogrešan račun dna, +0.930 m na ravnom tlu | dno na tlu | +0.000 m pri uspješnom raycastu | `Assets/Scripts/Game/FirmUnload.cs` |
| transpaleta u istovaru | nema GLB čvora, instance ili scalea u FirmUnload | vilice pod paletom, uređaj L1.55 / H1.2 / W0.55 | odsutna i dalje; popravljena postojeća dvorišna transpaleta | `Tripo/kolica-rucna.json` |

Sanduk izvana ima 2.430 × 2.380 × 5.980 m. Stražnji unutarnji mesh pokriva samo dio Z: 1.730–4.330, dok krov pokriva −1.600–4.300. Teret je unutar X ±0.820, Y 0.125–1.869 i Z 1.760–4.210. Između redova ostaje 0.050 m. Nisu povećavane već ispravne palete niti kamion.

Pri povlačenju postojeći kod pomiče dno reda 0.125 na vrh rampe 0.045 (−0.080 m), zatim spušta rampu 0.900 m. Odložena kopija se poravna s najbližim tlom. `rowBottom` je izračunat u okviru modela; dosad ga je `Drop` ponovno pretvarao kroz `load_pallets` s Y=−0.930. Ispravak koristi `lift.parent` (isti model frame kao mjerenje) i sortira pogotke prije biranja tla. To sprječava da isporučena paleta lebdi i izgleda premalo / odvojeno od podloge.

**Ograničenje animacije:** ovaj checkout nema transpaletu povezanu s istovarom. Ne može se potvrditi ulazak njezinih vilica pod palete niti popravljati njezin nepostojeći animirani scale. Povezivanje transpalete s teretom i koreografija dvije palete po redu bili bi dodatna funkcionalnost. Ovdje nije dodavan zamjenski model niti izmišljana potvrda kontakta vilica.

## Provjera i primjena

Offline provjera prolazi: kritične mjere, visina palete 0.144, teret unutar sanduka, razmak redova 0.05 i račun poravnanja u okviru modela. GLB datoteke ostale su iste. Unity Editor, Play Mode, kompilacija C# u Unityju i vizualna provjera nisu pokretani po zahtjevu vlasnika. Scena nije mijenjana.

`Assets/Generated` nije prisutan u ovom worktreeju i ignoriran je u Gitu. Kad Editor bude slobodan, promjene sidecara treba primijeniti postojećim **Magistrala/Rekviziti: uvezi Tripo modele**, zatim **Magistrala/Rekviziti: postavi**. Tek tada scena dobiva nove veličine i obnovljene kolajdere / provjere slobodnog prostora. Konjanik i njegov stup su zadržani.
