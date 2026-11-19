# Izvori (credits)

## CC0 (Poly Haven, https://polyhaven.com — licenca CC0, atribucija nije obavezna, ali je navodimo)
- HDRI nebo: **Kloofendal 43d Clear (Pure Sky)** — Greg Zaal / Poly Haven — https://polyhaven.com/a/kloofendal_43d_clear_puresky (4k .hdr)
- HDRI nebo za zalazak (G): **Belfast Sunset (Pure Sky)** — Poly Haven — https://polyhaven.com/a/belfast_sunset_puresky (4k .hdr)
- Teren: **Aerial Grass Rock** — https://polyhaven.com/a/aerial_grass_rock — Rob Tuytel (suha trava s kamenjem)
- Teren: **Dry Ground Rocks** — https://polyhaven.com/a/dry_ground_rocks — Rob Tuytel (oker tlo među maslinama)
- Teren: **Red Dirt Mud 01** — https://polyhaven.com/a/red_dirt_mud_01 — Rob Tuytel (crvenica / terra rossa)
- Teren: **Rock Face 03** — https://polyhaven.com/a/rock_face_03 — Dario Barresi, Rico Cilliers (vapnenac, krš)
- Teren / bankine: **Gravel Ground 01** — https://polyhaven.com/a/gravel_ground_01 — Rob Tuytel
- Kuće: **Clay Roof Tiles 02** — https://polyhaven.com/a/clay_roof_tiles_02 — Amal Kumar; **White Plaster 02** — https://polyhaven.com/a/white_plaster_02 — Rob Tuytel; **Wood Shutter** — https://polyhaven.com/a/wood_shutter — Dimitrios Savva
- Zgrade (niz tekstura `Assets/Textures/Buildings/facade_*.jpg`, 1k, boje svedene na tipologiju Kaštela): **White Plaster 02** — https://polyhaven.com/a/white_plaster_02 — Rob Tuytel (žbuka); **White Sandstone Blocks 02** — https://polyhaven.com/a/white_sandstone_blocks_02 — Rob Tuytel (klesani kamen); **Clay Roof Tiles** — https://polyhaven.com/a/clay_roof_tiles — Amal Kumar (kupa kanalica); **Large Red Bricks** — https://polyhaven.com/a/large_red_bricks — Rob Tuytel (opeka nedovršenih katova); **Concrete Wall 008** — https://polyhaven.com/a/concrete_wall_008 — Dario Barresi, Charlotte Baglioni (beton). Lamele škura, ravna boja i sobe iza prozora (`rooms.png`) su proceduralne (BuildingTextures.cs).
- Asfalt: **Asphalt 02** — https://polyhaven.com/a/asphalt_02 — Rob Tuytel (diffuse, normal, roughness; sloj istrošenog / ispucanog asfalta)
- Asfalt: **Asphalt Track** — https://polyhaven.com/a/asphalt_track — Dimitrios Savva (diffuse, normal GL, roughness; sitni agregat, osnovni sloj kolnika)

Datoteke: `Assets/Textures/PolyHaven/*` (2k jpg) i `Assets/Textures/Sky/*`.

## Iz igre Magistrala (BalkanDriver repo, vlastiti sadržaj)
- Modeli križanja i dionica (`client/public/assets/models/krizanja/*.glb`, Markov paket), njihov raspored (`model-junctions.json`)
- Visine terena, stabla, tlocrti zgrada (`client/public/assets/world/split-kastela/tiles`, `ground`) — dijelom izvedeno iz podataka DGU
  (Sadrži podatke Državne geodetske uprave; podaci su izmijenjeni)
- Kamion `firm_truck.glb`, biljke `plants/*.glb` i njihovi impostori, teksture `tex/pbr/*` (stari teren / beton)

## Državna geodetska uprava (Otvorena dozvola, data.gov.hr/otvorena-dozvola)
- **Sadrži podatke Državne geodetske uprave, Digitalni ortofoto LiDAR 2022./23., preuzeto 19. 11. 2026., podaci su dorađeni.**
  Boja tla oko Kaštel Lukšića (`Assets/Generated/Terrain/Macro/*`, izvedeno: zgrade i ceste maskirane i popunjene, boja
  svedena na naše teksture; docs/dof-tlo.md). Ne predstavlja službeni geodetski dokument. Isti tekst je u igri (mobitel → O igri).

## Unity paketi
- glTFast (com.unity.cloud.gltfast), URP, Input System, Pipeline — Unity Technologies

## Fontovi (SIL Open Font License 1.1)
- **Inter** (Regular, SemiBold) — The Inter Project Authors, https://github.com/rsms/inter (v4.1) — tekst Matina mobitela.
  Datoteke: `Assets/Resources/Fonts/Inter-*.ttf`, licenca `Assets/Resources/Fonts/Inter-LICENSE.txt`.
