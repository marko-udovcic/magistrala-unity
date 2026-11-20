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
- Zgrade (niz tekstura `Assets/Textures/Buildings/facade_*.jpg`, 1k, boje svedene na tipologiju Kaštela): **White Plaster 02** — https://polyhaven.com/a/white_plaster_02 — Rob Tuytel (žbuka); **Sandstone Blocks 08** — https://polyhaven.com/a/sandstone_blocks_08 — Rob Tuytel (klesani kamen u nepravilnim redovima, od „kuce2”; prije White Sandstone Blocks 02); **Clay Roof Tiles** — https://polyhaven.com/a/clay_roof_tiles — Amal Kumar (kupa kanalica); **Large Red Bricks** — https://polyhaven.com/a/large_red_bricks — Rob Tuytel (opeka nedovršenih katova); **Concrete Wall 008** — https://polyhaven.com/a/concrete_wall_008 — Dario Barresi, Charlotte Baglioni (beton). **Clay Roof Tiles 02** — https://polyhaven.com/a/clay_roof_tiles_02 — Amal Kumar (kupa kanalica, od proba „kuće” umjesto Clay Roof Tiles); **Old Stone Wall** — https://polyhaven.com/a/old_stone_wall (suhozid / zidovi dvorišta). Uz svaki sloj i njegova karta hrapavosti (roughness, plavi kanal `facade_normal.jpg`). Lamele škura, ravna boja i sobe iza prozora (`rooms.png`) su proceduralne (BuildingTextures.cs).
- Prljavština fasada (`Assets/Textures/Buildings/grime.png`, maske curenja kiše ispod streha i prozorskih klupčica): ambientCG **Leaking012A**, **Leaking016A**, **Leaking017A** — https://ambientcg.com (licenca CC0 1.0, https://docs.ambientcg.com/license/)
- Rekviziti dvorišta i terase (`Assets/Models/Props/PolyHaven/*.glb`, glTF 1k pretvoren u GLB s jednim materijalom): **Potted Plant 02** — https://polyhaven.com/a/potted_plant_02 (lonac s biljkom: `lonac-biljka`, `lonac-biljka-lisce`); **Planter Pot Clay** — https://polyhaven.com/a/planter_pot_clay (`teglica`); **Plastic Monobloc Chair 01** — https://polyhaven.com/a/plastic_monobloc_chair_01 (`stolica-plasticna`); **Round Wooden Table 02** — https://polyhaven.com/a/round_wooden_table_02 (`stol-kafic`). Suncobran je proceduralan (PropsBuilder.cs).
- Asfalt: **Asphalt 02** — https://polyhaven.com/a/asphalt_02 — Rob Tuytel (diffuse, normal, roughness; sloj istrošenog / ispucanog asfalta)
- Asfalt: **Asphalt Track** — https://polyhaven.com/a/asphalt_track — Dimitrios Savva (diffuse, normal GL, roughness; sitni agregat, osnovni sloj kolnika)
- Rive (RivaBuilder): **Large Grey Tiles** — https://polyhaven.com/a/large_grey_tiles — Rob Tuytel (kamene ploče šetnice, `riva_ploce_*.jpg`, 1k, posvijetljene i manje zasićene prema dalmatinskom vapnencu); **Castle Wall Slates** — https://polyhaven.com/a/castle_wall_slates — Rob Tuytel (kamena obala / zid rive, `riva_zid_*.jpg`, 1k, posivljeno)

Datoteke: `Assets/Textures/PolyHaven/*` (2k jpg; rive 1k) i `Assets/Textures/Sky/*`.

## Iz igre Magistrala (BalkanDriver repo, vlastiti sadržaj)
- Modeli križanja i dionica (`client/public/assets/models/krizanja/*.glb`, Markov paket), njihov raspored (`model-junctions.json`)
- Visine terena, stabla, tlocrti zgrada (`client/public/assets/world/split-kastela/tiles`, `ground`) — dijelom izvedeno iz podataka DGU
  (Sadrži podatke Državne geodetske uprave; podaci su izmijenjeni)
- Oleander `Assets/Models/Props/Tripo/oleander.glb` (Tripo, Markov model; razdjelni pojas D8, roza i bijela varijanta)
- Kamion `firm_truck.glb`, biljke `plants/*.glb` i njihovi impostori, teksture `tex/pbr/*` (stari teren / beton)

## Državna geodetska uprava (Otvorena dozvola, data.gov.hr/otvorena-dozvola)
- **Sadrži podatke Državne geodetske uprave, Digitalni ortofoto LiDAR 2022./23., preuzeto 19. 11. 2026., podaci su dorađeni.**
  Boja tla oko Kaštel Lukšića (`Assets/Generated/Terrain/Macro/*`, izvedeno: zgrade i ceste maskirane i popunjene, boja
  svedena na naše teksture; docs/dof-tlo.md), od 20. 11. 2026. i Kaštel Novi / Stari / zapadni Lukšić (pločice t_-9_-5, t_-8_-5, t_-7_-5).
  Obris rive, obale, lukobrana i parkirališta te krošnje stabala uz rivu (`Assets/Editor/riva.json`, Tools/riva/riva_build.py) izvedeni su
  s iste ortofotografije. Ne predstavlja službeni geodetski dokument. Isti tekst je u igri (mobitel → O igri).

## Unity Asset Store (Standard Unity Asset Store EULA — korištenje u igri, sirove datoteke se ne dijele, paket nije u gitu)
- **Coast & Dunes Environment – Dynamic Nature** (Beach, Sea, Islands, Dry Desert) — **NatureManufacture**, Unity Asset Store,
  Standard Unity Asset Store EULA. U probi „priroda”: slojevi tla (suho tlo s makijom, smeđa zemlja, kamenito tlo), suha trava i
  biljke za detalje terena, bor (preoblikovan prema alepskom boru), litice (zatamnjene u vapnenac), grm (tamnozeleno, bez cvijeta) u
  razdjelnom pojasu D8, more (shader „Water Sea Offset”), vjetar. Mapa paketa `Assets/NatureManufacture Assets` je u .gitignore.

## Unity paketi
- glTFast (com.unity.cloud.gltfast), URP, Input System, Pipeline — Unity Technologies

## Fontovi (SIL Open Font License 1.1)
- **Inter** (Regular, SemiBold) — The Inter Project Authors, https://github.com/rsms/inter (v4.1) — tekst Matina mobitela.
  Datoteke: `Assets/Resources/Fonts/Inter-*.ttf`, licenca `Assets/Resources/Fonts/Inter-LICENSE.txt`.
