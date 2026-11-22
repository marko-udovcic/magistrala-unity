# Ortofoto kao boja tla (cijela Kaštela)

Izvor: DGU **Digitalni ortofoto LiDAR 2022./23.** (25 cm), registrirani WMS
(`services/auth/inspire/orthophoto_lidar_2022_2023/wms`, sloj `OI.OrthoimageCoverage`, EPSG:3765), Otvorena dozvola.
Atribucija (u igri: mobitel → "O igri"; `CREDITS.md`): *Sadrži podatke Državne geodetske uprave, Digitalni ortofoto
LiDAR 2022./23., preuzeto 19. 11. 2026., podaci su dorađeni.*
Anonimni LiDAR sloj ima vodeni žig – ne koristi se. Ni Google / Esri.

## Cjevovod

1. `Tools/dof/dof_mosaic.py --src lidar -- TX,TZ ..` (ili `--x0 .. --x1 .. --z0 .. --z1 ..` za kutiju igre):
   pločice 256 m u EPSG:3765, 0,5 m/px, serijski, ≥ 2 s razmaka, u gitignorirani
   `BalkanDriver/tools/world/dgu/cache/dof/wms-lidar/` (uz svaku `.meta.json`: URL **bez** ključa, vrijeme). Ključ se čita
   samo pri pokretanju iz `cache/authkey.txt` i ide curlu preko stdin-a – nigdje se ne zapisuje.
   Izlaz: `cache/dof/mosaic-lidar/t_TX_TZ.png` (2048², 0,5 m/px, sjever gore, na mreži igre).
   Okvir igre = AEQD (43.5083, 16.44), x = istok, z = −sjever (`tools/world/ground/ortho.py`); preko jedne pločice 1 km
   AEQD → HTRS96/TM je afina s ostatkom < 0,1 mm.
2. Unity: **Magistrala/Grafika: ortofoto tlo** (`Assets/Editor/OrthoGround.cs`, pločice u `OrthoGround.TILES`):
   maska (tlocrti zgrada + 3 m, naše ceste + 2 m, crveni / bijeli krovovi i bazeni s fotografije + 1 m) → popuna okolnim
   tlom (pull-push) → blagi blur → omjer *fotografija ÷ srednja boja naših slojeva* (boja fotografije jednom po kanalu
   svedena na prosjek naših slojeva za cijelo područje, kontrast 1 (puni omjer)), 1 = bez promjene na moru i 64 m uz rub prema
   pločici bez fotografije. `Assets/Generated/Terrain/Macro/macro_TX_TZ.png` (linearno, BC1 + mipovi, 2048²) i materijal
   na `Assets/Shaders/TerrainMacro.shader` (kopija URP TerrainLit, jedna linija: albedo × makro; i basemap gen).
3. Igrač: `-dof 0` vraća standardni materijal (A/B), `-camhigh H` kamera H m iznad kamiona.

Ponovno pokrenuti 2. nakon promjene splat mape (Build terrain), zgrada ili ulica.

Scena se ne commita s ovim materijalima (oni su u gitignoriranom `Assets/Generated`): na novoj kopiji pokrenuti 1. i 2.
pa spremiti scenu. Bez toga teren ostaje na standardnom materijalu.

## Proba 2026-11-19 (pločice −8,−6 / −7,−6 / −6,−6, ≈ 3,1 km² oko skladišta u Lukšiću)

- Preuzeto 65 zahtjeva LiDAR WMS-a (+ 16 ranijih DOF 2025/26, koji se više ne koriste).
- Poravnanje: tlocrti zgrada ↔ krovovi na LiDAR ortofotu najbolji pomak ≤ 1,1 m (sve tri pločice); D8 pojas ↔ asfalt
  medijan 0,5 m (šumovito, parkirališta). Ranije mjerenje osi grafa prema DOF-u (BalkanDriver
  docs/ceste-ortofoto-mjerenje.md): primarne 0,4 m, sekundarne 0,6 m; D8 iz Markovih modela 1,5–3 m sjevernije od ortofota.
- Memorija: 3 × 2048² BC1 s mipovima = 3 × 2,7 MB. Build (macOS) 1767 → 1777 MB (+9,7 MB: teksture ≈ 8,4 MB + varijante kopije terenskog shadera). Cijela Kaštela (~30 pločica) ≈ +85 MB memorije i builda; opcija 1024² (1 m/px) = četvrtina.
- GPU (srednje, parkiran na D8 kod skladišta, 4–5 prozora po 10 s): prije 37–43 ms, poslije 37–47 ms, `-dof 0` 39–46 ms, ponovljeni „prije” 39–52 ms — razlika je ispod šuma (zagrijavanje Maca ±5 ms); shader dodaje jedno uzorkovanje teksture po pikselu terena.

## Cijela Kaštela (22. 11. 2026.)

Marko: „osim Lukšića sva mjesta su žuta pustinja, a uz kamion ravno tlo”. Napravljeno na grani `tlo`:

**Preuzimanje.** Svih 30 jezgrenih pločica (TX −10…−1 × TZ −7…−5) preuzeto je već 20. 11. (533 odgovora LiDAR WMS-a u
`cache/dof/wms-lidar/`); 22. 11. `dof_mosaic.py` ponovno složen samo iz cachea: 0 novih zahtjeva, pokrivenost 100 % na
svakoj pločici, ostatak afine ≤ 0,08 mm. Ključ (istekao 22. 11. 21:25) više nije potreban dok se područje ne širi.
(`python3 -I` ne čita PYTHONPATH: pokretati kroz omotač koji doda put do numpy / Pillow / pyproj u `sys.path` pa `runpy`.)

**OrthoGround** (`Magistrala/Grafika: ortofoto tlo`, ~7,5 min za 30 pločica):
- `TILES` = sve jezgrene pločice (iz `WorldBuilder.TX0…TZ1`).
- `OutN = 1024`: makro tekstura 1 m/px, BC1 + mipovi = 0,7 MB po pločici, 30 × = 21 MB (2048² bi bilo 85 MB). Omjer boje je
  blag i mutan, detalj blizu daju slojevi i detalji terena, razlika 0,5 → 1 m/px se s 2 m ne vidi. Pass-1 podaci čuvaju se
  na izlaznoj veličini (30 × 2048² floatova bilo bi ~3,4 GB u editoru).
- Boja: uzrok „pustinje” bio je usklađivanje boje – prosjek fotografije po kanalu sveden na prosjek NAŠIH slojeva (žuti
  `NM_DryScrub`), pa je zelena fotografija (sRGB prosjek 103/113/105) postajala žuta. Sad: svjetlina se usklađuje u
  potpunosti, nijansa samo 30 % (`HueMatch`); pojačanje 0,74 / 0,66 / 0,62. Zatim po pikselu (`Grade`): vegetacija
  (excess green > 0) malo zelenija (`Green` 0,22), narančasta / oker gola zemlja 40 % prema sivoj (`Earth`), svjetlina
  ostaje. `Strength` 0,7 → 0,85 (manje žutog sloja ispod), `FADE` 64 → 128 m prema pločicama bez fotografije.
- Šavova među pločicama nema (jedno usklađivanje za cijelo područje); prijelaz prema moru i rubu kao prije.

**Detalji terena** (`Magistrala/Grafika: detalji terena`):
- Glavni uzrok „ravnog tla”: Unity od 2022.2 terenske detalje po zadanom raspoređuje u *coverage* načinu (vrijednost
  0–255 = pokrivenost), a naši slojevi su brojevi primjeraka po ćeliji 4 m (1–3) → crtalo se ~1 % trave. Sad
  `SetDetailScatterMode(InstanceCountMode)`.
- `NaturePass.Details` čita ortofoto po ćeliji (`OrthoGround.PhotoCells`: zeleno = excess green, golo = svijetlo i nezeleno):
  zelena trava (`beach_grass_02_2 / 02_4`, lagano zeleno tonirana) i nisko bilje (`kidney_vetch`) gdje je fotografija
  zelena, manje suhih busena tamo; lišće / grančice (`litter_01_A`) i naši kamenčići na golim dvorištima i putevima;
  korov 2–5 m uz ceste (kao uz zidove).
- Bez detalja na popločenju (`GraphicsPass.Paved`): ćelija ispod koje zraka odozgo pogodi ne-terenski kolajder
  5 cm – 3 m iznad terena. Terase / ploče uz zgrade postoje samo u render mrežama (`town/**/near`), pa im prolaz dodaje
  privremeni MeshCollider i na kraju ga briše – u coverage načinu se to nije vidjelo, sad je trava rasla kroz pločnik.
- Sitni detalji (trava, kamenčići, sve iz paketa) bez bacanja sjene (`NaturePass.NoShadow` kopije u
  `Generated/Nature/NoShadow`): tisuće busena u 4 kaskade sjena koštale su više od samih busena. Makija baca sjenu.
- Detalja ukupno: 4,0 M → 7,9 M (u 30 pločica; crta se samo do 140 m od kamere). „Grafika: detalji terena” ~1 min.

**Mjerenja** (Mac, „oštro”, 1600×900, kamera fiksna `-campose`, GPU iz FrameTimingManagera, prozori od 10 s nakon 30 s):
- Build (macOS): 1629 → 1638 MB (+9 MB, +0,6 %). Makro teksture u memoriji: prije 6 × 2,7 = 16 MB (2048²), sad 30 × 0,7 =
  21 MB (1024²) → +5 MB GPU memorije za cijela Kaštela.
- GPU, s ulice na 2 m u Kaštel Starom (najgušći detalji), 4 izmjenična para po ~5 × 10 s, editor zatvoren:
  prije 28,5 / 28,4 / 28,1 / 28,5 ms, poslije 26,9 / 26,9 / 26,7 / 26,5 ms → **28,4 → 26,8 ms (−6 %)**; glavna nit
  4,6 → 4,9 ms. Brže jer sitni detalji više ne bacaju sjenu (i oni stari koji su se crtali).
- Usput izmjereno: prva verzija (detalji stvarno crtani, ali sa sjenama) bila je +20–38 % GPU (u rasponu šuma s otvorenim
  editorom); bez trave (`-skip grass`) prije = poslije (27,8 / 27,1 ms) – makro tekstura sama ne košta ništa mjerljivo;
  kraća udaljenost trave (`-trava 80` / `50`) nije bitno pomogla – trošak su bile sjene, ne udaljenost.
- GPU, dron 120 m koso iznad Kaštel Starog: prije 25,2 ms, poslije 25,2 ms (glavna nit 3,9 → 4,0 ms).

Slike prije / poslije (dron 120 m koso i 2 m s ulice: Kaštel Stari, Kambelovac, Gomilica, Sućurac, Lukšić (stara proba
i istok), D8): `BalkanDriver/references/fotografije/unity-proba/tlo/`.

**Otvoreno:** zasjenjeno tlo uz zidove ponegdje plavkasto-zeleno (sjena na fotografiji × zelena korekcija; `Green` /
`ShadowLift` za doradu); nagib Kozjaka (pločice TZ −7) dosta taman (omjer p50 0,54–0,8: makija i sjene na fotografiji); detalji su i dalje rijetki (prirodno, ali ne „vrt”); na glavnoj kopiji nakon spajanja (scena nije u gitu) pokrenuti
„Grafika: ortofoto tlo” pa „Grafika: detalji terena” i spremiti scenu.
