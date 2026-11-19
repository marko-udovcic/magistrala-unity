# Ortofoto kao boja tla (proba: Kaštel Lukšić)

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

## Cijela Kaštela (kad Marko odobri; ključ vrijedi do 22. 11. 2026. 21:25)

    PYTHONPATH=<numpy, Pillow, pyproj> python3 -I Tools/dof/dof_mosaic.py --src lidar --x0 -10240 --x1 -1 --z0 -7168 --z1 -4097

(30 jezgrenih pločica, ≈ 25 zahtjeva po pločici, ≈ 3 s svaki → ≈ 40 min; već preuzeto se ne traži ponovno), zatim
`OrthoGround.TILES` na te pločice i izbornik 2.
