# Kaštel Stari (i ostala Kaštela) – ortofoto vs igra, tlo između kuća

Analiza 2026-11-21 (grana `stari`). Stvarno: DGU DOF LiDAR 2022./23. (0,5 m/px, mozaik na mreži igre) + OSM; igra: main
`a941bc6` (Mac build 21. 11.), dron ravno dolje (`-dron -dronh 380 -dronp 89 -nohud`, sjever gore).
Slike (interno, NE u gitu): `BalkanDriver/references/fotografije/unity-proba/stari-audit/`
(`usporedba-*.jpg` = ortofoto | igra, `prije-*.jpg`, `poslije-*.jpg`).

## Glavne razlike (prije)

| # | Što | Stvarno | Igra prije |
|---|-----|---------|------------|
| 1 | Krošnje između kuća (dvorišta, vrtovi) | 61–91 krošnji/ha izgrađenog (Stari 66, Novi 82, Lukšić 61, Kambelovac 72, Gomilica 88, Sućurac 91); u izgrađenom Kaštela ~65 000 krošnji na 1 053 ha | 10–14/ha (Stari 706 stabala na 50 ha, Novi 442 / 44 ha); krošnje s fotografije bile su prorijeđene kao „otvoreno tlo” |
| 2 | Bazeni u dvorištima | 383 bazena s fotografije (Stari 13, Novi 24, Kambelovac 24, Lukšić 13, Gomilica 14, Sućurac 10) | 0 |
| 3 | Nogometno igralište Valovo (HNK Val, OSM „stadion Vukovar”, way 296188966) | travnato igralište ~70 × 100 m (s rubom 77 × 108), linije, gol-vratnice, ograda, 2 mala terena s umjetnom travom zapadno, tribina (zgrada) istočno, drvored uz cestu | suha trava, ništa |
| 4 | Ostala igrališta | OSM 12 u Kaštelima (Zrinski Sućurac, Bakotić Gomilica umjetna trava, Sokolana, teniski u Novom i Lukšiću, 4 terena u Biblijskom vrtu …) + 2 s fotografije | 0 |
| 5 | Parkirani auti | ~5–6 auta/ha u dvorištima (uzorak 2,4 ha u Starom: 12–15 auta, uglavnom na popločanim dvorištima), poneki uz ulicu; parkirališta s ~60 % zauzetih mjesta (564 mjesta) | 0 (parkiralište kod crkve u Starom prazno) |
| 6 | Parkirališta | 56 površina s crtama (OSM + fotografija), 31 OSM `amenity=parking` | samo rivino u Starom (prazno) |
| 7 | Živice uz ograde dvorišta | 243 pruge (OSM `barrier=hedge` + uske zelene pruge s fotografije) | 0 (samo oleander u razdjelnom pojasu D8) |
| 8 | Parkovi | 14 OSM `leisure=park` (Park Vitturi, Park K. Gomilica, 3 u Starom/Novom …): klupe, staze, stabla | gola površina, nijedna klupa |
| 9 | Uske ulice / prolazi | gusta mreža uličica i kolnih prolaza između parcela | samo ulice iz grafa (≈ polovica vidljivih) — NIJE dirano (graf, posao za poseban paket) |
| 10 | Boja tla | sivo-zelena: puno betoniranih dvorišta, vrtovi, sjena krošanja | žuto-oker suha trava (ortofoto ton sveden na prosjek naših slojeva) — NIJE dirano |

Ostalo uočeno: dječja igrališta (OSM 11, guma ispod sprava – 5 površina u podacima), školska dvorišta (5 škola: OŠ F. Lukaša,
Bijaći, Ostrog, kneza Trpimira, kneza Mislava) s terenima koje OSM nema, drvoredi uz rivu (već u igri), staklenici.

## Izvor podataka za ugradnju

`Tools/tlo/tlo_build.py` → `Assets/Editor/tlo-kastela.json` (264 kB, samo izvedeni vektori): OSM igrališta / parkovi +
izlaz BalkanDriver `tools/world/ground/build.py` (klasifikacija ortofota: parkirališta s crtama i zauzetim mjestima, bazeni,
tereni, guma, živice), podloga igrališta iz boje ortofota (trava / umjetna / beton / zemlja). Krošnje dvorišta su
`ground/{tx}_{tz}.json` `trees` (vrste 10–13) koje `WorldBuilder.Trees` već čita.
Valovo mali tereni nisu u OSM-u: izmjereni na ortofotu (`PHOTO_PITCHES` u skripti).

## Ugrađeno (2026-11-21, grana `stari`)

- **Igrališta** (`GroundCover.cs`, Rekviziti: postavi → `World/Tlo`): 14 (12 OSM + 2 Valovo s fotografije). Teren poravnat
  (`Tiles.Height` → `GroundCover.Level`: ravno preko igrališta + 2,5 m, prijelaz 8 m; skupina igrališta koja se dodiruju dijeli
  jednu visinu – Valovo 11,8 m), podloga po fotografiji (Valovo trava s prugama košnje, mali tereni i Bakotić umjetna trava,
  Sokolana / škole beton u boji s fotografije, tenis zemlja), oznake po sportu, 20 golova s mrežom, 8 koševa, teniske mreže,
  2 051 m žičane ograde (5 m iza golova velikog igrališta, 4 m oko malih, 3,5 m tenis; panel izostavljen gdje je zgrada / ulica /
  drugo igralište), BoxCollider na ogradi, stupovima, prečkama, mrežama; 12 klupa uz igrališta.
- **Tvrde površine** iz ground builda: 56 parkirališta s crtama, 383 bazena (voda + kameni rub), 5 gumenih podloga igrališta
  (5 747 trokuta, 630 ispušteno pod zgradama / ulicama / rivom). `GroundCover.Hard()`: na njima nema trave ni stabala.
- **Krošnje u dvorištima** (`VegetationMask.YardTree`): krošnje fotografije u izgrađenom ostaju – 54 591 (476 odbačeno: igralište /
  bazen / < 1 m od tlocrta); stabala ukupno 265 913 → 314 867. Trećina maslina → crnika (agrumi, rogač, smokva …), pola velikih
  borova → crnika, pola čempresa → mala maslina.
- **Živice dvorišta**: 243 pruge → 1 857 biljaka (grm-zivica svakih 0,8 m u visini pruge, ~12 % oleander).
- **Parkovi**: 105 klupa u 14 OSM parkova (uz rub, 2,5 m unutra, okrenute prema parku).
- **Parkirani auti** (`PropsParked.cs`, `ParkedCars.cs`): 2 086 – 511 na parkiralištima (zauzeta mjesta ground builda), 1 573 uz
  seoske ulice (rub ili pločnik, paralelno, u nizovima; ne D8 / modeli, ne raskrižja, kapije, stajališta), 2 iza kapija dvorišta.
  7 najčešćih vrsta prometa × 3 boje, instancirano: svuda spojeni daleki model (3 crtanja po vrsti i boji; puni dijelovi
  ~120 SetPass poziva po autu – `-parkednear M` za usporedbu), do 260 m, sve bacaju sjenu; BoxCollider.
  `-parked 0` ih ne crta (mjerenje).

Ponovno: `python -I Tools/tlo/tlo_build.py` (nakon novog ground builda weba), zatim Build terrain → Build streets → Build trees →
Grafika: detalji → (zgrade) → Rekviziti: postavi.

## Mjerenje (Mac, srednje, 1920×1080, naizmjenično main `a941bc6` / `stari`, 3 kruga)

Mac je bio pod jakim opterećenjem (još dva Unity editora + igrači drugih agenata, load 20–97): fps / main ms skaču ±50 % između
istih pokretanja, pa usporedba fps-a NIJE pouzdana. Brojači neovisni o opterećenju (kamion stoji, isti kadar):

| Mjesto | Batches A → B | SetPass A → B | Shadow casters | Trokuti | TreeRenderer CPU |
|--------|---------------|---------------|----------------|---------|------------------|
| Riva Starog (−7900, −4625) | 2 689 → 3 356 (+25 %) | 284 → 326 (+15 %) | 309 → 396 | 5,27 → 5,89 M | 0,8 → 1,6 ms |
| Ulica Ivana Danila (−7820, −4760) | 2 079 → 2 793 (+34 %) | 198 → 240 (+21 %) | 348 → 466 | 2,56 → 3,09 M | 1,2 → 3,0 ms |
| Vukovarska / Valovo (−7622, −4985) | 2 017 → 2 546 (+26 %) | 196 → 226 (+15 %) | 355 → 460 | 2,27 → 2,55 M | 2,0 → 2,9 ms |

GPU ms nije porastao (11–23 ms obje verzije), trošak je na CPU-u (krošnje + crtanja). Treba ponoviti na mirnom računalu;
ako je pad > 10 %: prvo `-skip trees` / `-parked 0` / `-skip World/Tlo` da se vidi čiji je, zatim prorijediti grmlje dvorišta
(vrsta 13, 17 % krošnji) ili skratiti `drawDistance` crnike.

Build (macOS): 1 597 MB, isto kao main (+5 MB na disku).

## Ostalo (po važnosti)

1. Ponoviti fps A/B na mirnom računalu (gore) i po potrebi prorijediti.
2. Uske ulice i kolni prolazi između parcela (≈ pola vidljivih nema u grafu) – paket ulica.
3. Boja tla: ortofoto ton sveden na prosjek naših slojeva daje žuto-oker umjesto sivo-zelenog (betonirana dvorišta, vrtovi).
4. Auti u dvorištima: kapija ima samo 308 (Yards), smjer normale kapije ne daje mjesto – 2 / 122; bolje: auto na popločanom
   dijelu dvorišta uz kuću (paved fronts).
5. Školska dvorišta (5 škola) – tereni s fotografije (OSM ih nema), staze i klupe u parkovima, dječja igrališta (sprave).
6. Tribina Valova je zgrada (paket zgrada); reflektori nisu vidljivi na fotografiji – nisu dodani.
