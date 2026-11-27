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
- Ceste (`Assets/Textures/Ceste/`, detalji u `IZVOR.md` i `manifest.json`): **Asphalt 04** — https://polyhaven.com/a/asphalt_04 (asfalt); **Clean Asphalt** — https://polyhaven.com/a/clean_asphalt (zakrpe); **Gravel Concrete 04** — https://polyhaven.com/a/gravel_concrete_04 (rubnjaci); **Cobblestone Floor 08** — https://polyhaven.com/a/cobblestone_floor_08 (otoci); **Sandstone Blocks 08** — https://polyhaven.com/a/sandstone_blocks_08 (ploče rive); **Seaworn Stone Tiles** — https://polyhaven.com/a/seaworn_stone_tiles (rubni kamen rive); **Mossy Rock** — https://polyhaven.com/a/mossy_rock (alge, zatamnjeno); **Gravel Floor 02** — https://polyhaven.com/a/gravel_floor_02 (bankina). Šahte, slivnici, pukotine, mrlje ulja i prašina: vlastito, proceduralno (`Tools/ceste/gen_decals.py`), CC0.
- Ograde dvorišta (grana `ograde`): **Periwinkle Plant** — https://polyhaven.com/a/periwinkle_plant (CC0; listovi iz atlasa `periwinkle_plant_diff_1k.png` + `_opacity_1k.png` složeni u sloj živice, `Tools/zbuke/zivica.py` → 10. sloj `Assets/Textures/PolyHaven/zbuke/zbuke_*.jpg`); **Modular Chainlink Fence** — https://polyhaven.com/a/modular_chainlink_fence (CC0; `modular_chainlink_fence_wire_alpha_1k.png` → pletivo žičane ograde u `Assets/Textures/Buildings/ograda_atlas.png`, `Tools/zbuke/ograda.py`). Izvorne datoteke (ne u gitu) leže u `BalkanDriver/assets-src/polyhaven-zbuke/periwinkle_plant/` i `.../modular_chainlink_fence/`; ponovno preuzimanje: https://dl.polyhaven.org/file/ph-assets/Models/png/1k/periwinkle_plant/periwinkle_plant_{diff,opacity}_1k.png i https://dl.polyhaven.org/file/ph-assets/Models/png/1k/modular_chainlink_fence/modular_chainlink_fence_wire_alpha_1k.png
- Prljavština fasada (`Assets/Textures/Buildings/grime.png`, maske curenja kiše ispod streha i prozorskih klupčica): ambientCG **Leaking012A**, **Leaking016A**, **Leaking017A** — https://ambientcg.com (licenca CC0 1.0, https://docs.ambientcg.com/license/)
- Rekviziti dvorišta i terase (`Assets/Models/Props/PolyHaven/*.glb`, glTF 1k pretvoren u GLB s jednim materijalom): **Potted Plant 02** — https://polyhaven.com/a/potted_plant_02 (lonac s biljkom: `lonac-biljka`, `lonac-biljka-lisce`); **Planter Pot Clay** — https://polyhaven.com/a/planter_pot_clay (`teglica`); **Plastic Monobloc Chair 01** — https://polyhaven.com/a/plastic_monobloc_chair_01 (`stolica-plasticna`); **Round Wooden Table 02** — https://polyhaven.com/a/round_wooden_table_02 (`stol-kafic`). Suncobran je proceduralan (PropsBuilder.cs).
- Žbuke fasada izbliza (`Assets/Textures/PolyHaven/zbuke/`, niz od 9 slojeva 1k iz 2k izvora, posivljeno — boju daje kuća / Mapillary; Tools/zbuke/zbuke.py; `mikro.png` = sitno zrno iz Grey Plaster 02): **Concrete Wall 003** — https://polyhaven.com/a/concrete_wall_003; **White Rough Plaster** — https://polyhaven.com/a/white_rough_plaster; **Cracked Concrete Wall** — https://polyhaven.com/a/cracked_concrete_wall; **Preconcrete Wall 001** — https://polyhaven.com/a/preconcrete_wall_001; **Beige Wall 002** — https://polyhaven.com/a/beige_wall_002; **Painted Plaster Wall** — https://polyhaven.com/a/painted_plaster_wall; **Yellow Plaster** — https://polyhaven.com/a/yellow_plaster; **Red Plaster Weathered** — https://polyhaven.com/a/red_plaster_weathered; **Grey Plaster 02** — https://polyhaven.com/a/grey_plaster_02
- Odobreni materijali (Marko, 25. 11. 2026; izvori 2K u `BalkanDriver/assets-src/polyhaven-materijali/`, README + kontakt-list.jpg; u igri 1k): **White Sandstone Blocks 02** — https://polyhaven.com/a/white_sandstone_blocks_02 (klesani kamen starih kuća, sloj 1 `facade_*.jpg`, zamijenio Sandstone Blocks 08); **Stone Wall** — https://polyhaven.com/a/stone_wall (suhozid / zidovi dvorišta, sloj 7, zamijenio Old Stone Wall); **Concrete Wall 007** — https://polyhaven.com/a/concrete_wall_007 (beton, sloj 4, zamijenio Concrete Wall 008; i beton podzida / parapeta modela cesta, `concrete_wall_007_*.jpg`); **Painted Metal Shutter** — https://polyhaven.com/a/painted_metal_shutter (lamele škura, sloj 5, nijansirano po kući; zamijenio proceduralne lamele); **Large Red Bricks** i **Clay Roof Tiles 02** (kao gore); **Worn Plaster Wall** — https://polyhaven.com/a/worn_plaster_wall (stara oljuštena žbuka, 11. sloj `zbuke_*.jpg`); **Corrugated Iron 02** — https://polyhaven.com/a/corrugated_iron_02 (limeni krovovi hala, 12. sloj `zbuke_*.jpg`); **Concrete Tiles 02** — https://polyhaven.com/a/concrete_tiles_02 (betonske ploče nogostupa, `concrete_tiles_02_*.jpg`); **Rock Boulder Dry** — https://polyhaven.com/a/rock_boulder_dry (krški vapnenac: detaljna tekstura stijena / usjeka, `rock_boulder_dry_*.jpg`). Sve CC0; obrada `Tools/zbuke/materijali.py` i `Tools/zbuke/zbuke.py`.
- Asfalt: **Asphalt 02** — https://polyhaven.com/a/asphalt_02 — Rob Tuytel (diffuse, normal, roughness; sloj istrošenog / ispucanog asfalta)
- Asfalt: **Asphalt Track** — https://polyhaven.com/a/asphalt_track — Dimitrios Savva (diffuse, normal GL, roughness; sitni agregat, osnovni sloj kolnika)
- Rive (RivaBuilder): **Large Grey Tiles** — https://polyhaven.com/a/large_grey_tiles — Rob Tuytel (kamene ploče šetnice, `riva_ploce_*.jpg`, 1k, posvijetljene i manje zasićene prema dalmatinskom vapnencu); **Castle Wall Slates** — https://polyhaven.com/a/castle_wall_slates — Rob Tuytel (kamena obala / zid rive, `riva_zid_*.jpg`, 1k, posivljeno)

Datoteke: `Assets/Textures/PolyHaven/*` (2k jpg; rive 1k) i `Assets/Textures/Sky/*`.

- Prljavština i sitne ogrebotine na prometnim autima (`Assets/Textures/Vehicles/auto_grime.png`, r = mikro nepravilnosti, g = mrlje):
  ambientCG **SurfaceImperfections014** i **Smear007** — https://ambientcg.com (licenca CC0 1.0, https://docs.ambientcg.com/license/)

## CC BY 4.0 (Creative Commons Attribution 4.0, https://creativecommons.org/licenses/by/4.0/)
- **"Zastava 101 (Stojadin)"** — Tomislav Tomljenović — https://sketchfab.com/3d-models/x-52c885cfc52a4ac1acb14f84a3feacfa — CC BY 4.0.
  **Izmijenjeno:** uklonjeni natpisi i oznake proizvođača, materijali pretvoreni u naš shader za aute (boja po primjerku, prljavština,
  zatamnjena stakla), dodane izmišljene registarske tablice, razine detalja (LOD) i pojednostavljeni daleki model.
  U igri kao prometni auto „zastava” (`Assets/Editor/auti-kandidati.json`, `Assets/Editor/KandidatImporter.cs`).
- **"Yugo Koral 45"** — Tomislav Tomljenović — https://sketchfab.com/3d-models/yugo-koral-45-f5d14e34c9064b49b943e0c4a3add6ca — CC BY 4.0. U igri „yugo45”.
- **"BMW E46 MSport"** — Ricy — https://sketchfab.com/3d-models/bmw-e46-msport-614c2f1660474178a1d4717b7492d1c7 — CC BY 4.0. U igri „e46”.
- **"Mercedes-Benz E-class W124 (unfinished)"** — OverLord — https://sketchfab.com/3d-models/mercedes-benz-e-class-w124-unfinished-4b468dbe4b6d4ca994bc7066b90781e2 — CC BY 4.0. U igri „w124”.
- **"Volkswagen Golf II"** — ARDmation — https://sketchfab.com/3d-models/volkswagen-golf-ii-740500f10bc94e7a91c2290e445fcb39 — CC BY 4.0. U igri „golf2”.
- **"Volkswagen Passat (2004 V6 4Motion)"** — I3D99 — https://sketchfab.com/3d-models/volkswagen-passat-480c0793f02b43d78b0d0c4a0eb6acb6 — CC BY 4.0. U igri „passat” (uklonjeni i američki bočni markeri).
- **"Compact Hatchback"** — RCC Design — https://sketchfab.com/3d-models/compact-hatchback-abbd4e65bbf1475b866d84735088b8ff — CC BY 4.0. U igri „rcc_hatch”.
- **"Executive Sedan"** — RCC Design — https://sketchfab.com/3d-models/executive-sedan-eadf0e3171d74e4999e37898f95578c8 — CC BY 4.0. U igri „rcc_sedan”.
- **"European Delivery Van"** — Evan Hiltz — https://sketchfab.com/3d-models/european-delivery-van-0b2f1ad95a79419f9a092420024d329c — CC BY 4.0. U igri „kombi_tn”.
- **"Asti Stradale '89 - Low poly model"** — Daniel Zhabotinsky (DanielZhabotinsky) — https://sketchfab.com/3d-models/asti-stradale-89-low-poly-model-97b15b067111443ebe05840137b49c65 — CC BY 4.0. U igri „dz_asti_89”.
- **"Milano '95 - Low poly model"** — Daniel Zhabotinsky (DanielZhabotinsky) — https://sketchfab.com/3d-models/milano-95-low-poly-model-122a22d210234596ad23623588d0d166 — CC BY 4.0. U igri „dz_milano_95”.
- **"Olympic '95 - Low poly model"** — Daniel Zhabotinsky (DanielZhabotinsky) — https://sketchfab.com/3d-models/olympic-95-low-poly-model-4809a093a0f341409757298a9e7e0003 — CC BY 4.0. U igri „dz_olympic_95”.
- **"Tozzo '98 - Low poly model"** — Daniel Zhabotinsky (DanielZhabotinsky) — https://sketchfab.com/3d-models/tozzo-98-low-poly-model-d43bb748da364af2b3f0a9348365f0fb — CC BY 4.0. U igri „dz_tozzo_98”.
- **"Sigil '07 - Low poly model"** — Daniel Zhabotinsky (DanielZhabotinsky) — https://sketchfab.com/3d-models/sigil-07-low-poly-model-22abe5284d4c4b55920b8462eb24a8c1 — CC BY 4.0. U igri „dz_sigil_07”.
- **"Compact '07 - Low poly model"** — Daniel Zhabotinsky (DanielZhabotinsky) — https://sketchfab.com/3d-models/compact-07-low-poly-model-25c6874d173a4f6ca63e150bbd505686 — CC BY 4.0. U igri „dz_compact_07”.
- **"Kiri '10 - Low poly model"** — Daniel Zhabotinsky (DanielZhabotinsky) — https://sketchfab.com/3d-models/kiri-10-low-poly-model-7fd6e15785fa4aa9bfd6e31eb7c97ba6 — CC BY 4.0. U igri „dz_kiri_10”.
- **"Urban '10 - Low poly model"** — Daniel Zhabotinsky (DanielZhabotinsky) — https://sketchfab.com/3d-models/urban-10-low-poly-model-2866efdfa943484391ef8313768e074d — CC BY 4.0. U igri „dz_urban_10”.
- **"Ace '11 - Low Poly model"** — Daniel Zhabotinsky (DanielZhabotinsky) — https://sketchfab.com/3d-models/ace-11-low-poly-model-055ff8a21b8d4d279debca089e2fafcd — CC BY 4.0. U igri „dz_ace_11”.
- **"LCT 3000 '07- Low poly model"** — Daniel Zhabotinsky (DanielZhabotinsky) — https://sketchfab.com/3d-models/lct-3000-07-low-poly-model-3be03b6a43aa41898c9ca806b8787052 — CC BY 4.0. U igri „dz_lct3000_07”.
- **"Volkswagen Golf IV"** — Fratzica (vasilebetivu62) — https://sketchfab.com/3d-models/volkswagen-golf-iv-3e40ac80ec044394999715a18e2dbe33 — CC BY 4.0. U igri „vw_golf_4”.
- **"low-poly Škoda Octavia RS 2020"** — D_U (DU1701) — https://sketchfab.com/3d-models/low-poly-skoda-octavia-rs-2020-d567807ce57e415ba9a1ffdf07e0cf5b — CC BY 4.0. U igri „octavia_2020”.
- **"2015 Mazda 6"** — Dave Bored (Tyler_Dave) — https://sketchfab.com/3d-models/2015-mazda-6-24fb449e5f4243f49666ccf22d10c834 — CC BY 4.0. U igri „mazda6_2015”.
- **"Dacia Duster Low Poly"** — Danut_Stanciu (solid3ddd / solidmodelsproject) — https://sketchfab.com/3d-models/dacia-duster-low-poly-ea275644cd634593b6a97eacf0ede405 — CC BY 4.0. U igri „dacia_duster”.
- **"Jeep Renegade 2016"** — Jose Bronze (pinceladas3d) — https://sketchfab.com/3d-models/jeep-renegade-2016-192f7e81dce84c6780434f692a0f96c5 — CC BY 4.0. U igri „jeep_renegade”.
- **"Lowpoly Generic SUV"** — mk2design — https://sketchfab.com/3d-models/lowpoly-generic-suv-edc994ad28ed438cb365c0e0389ac177 — CC BY 4.0. U igri „generic_suv”.
- **"Mercedes Benz Sprinter 2006"** — Max (Max-7215; u metapodacima Max-5532) — https://sketchfab.com/3d-models/mercedes-benz-sprinter-2006-f69de1315bb049c8946d57f6006acd73 — CC BY 4.0.
  U igri „sprinter” (civilni) i „sprinter_policija” (naša policijska grafika POLICIJA s logom igre, svjetla na krovu naša; razdvojen UV).
  **Izmijenjeno (svi gore):** uklonjeni amblemi, natpisi i oznake proizvođača (geometrija i teksture: priprema
  `BalkanDriver/assets-src/auti-kandidati/`, README.md), natpisi s guma, stvarne / strane registracije; materijali pretvoreni u naš
  shader za aute (boja po primjerku, prljavština, zatamnjena stakla), dodane izmišljene tablice „ST …” s logom igre, razine detalja (LOD)
  i pojednostavljeni daleki model.

## Iz igre Magistrala (BalkanDriver repo; vlastiti sadržaj i preneseni izvori)
- Modeli križanja i dionica (`client/public/assets/models/krizanja/*.glb`, Markov paket), njihov raspored (`model-junctions.json`)
- Visine terena, stabla, tlocrti zgrada (`client/public/assets/world/split-kastela/tiles`, `ground`) — dijelom izvedeno iz podataka DGU
  (Sadrži podatke Državne geodetske uprave; podaci su izmijenjeni)
- Tripo rekviziti i službena vozila (`Assets/Models/Props/Tripo/`, `Assets/Models/Vehicles/Tripo/`), Markovi generirani modeli. U ovom worktreeju oleander i klima imaju samo JSON konfiguraciju, bez GLB-a. Licenca generiranja / plan računa nije dokumentiran; vidi upozorenja u `Tools/dijagnoza/licence.md`.
- Kamion `firm_truck.glb` i proceduralna prometna flota iz BalkanDrivera (iznimka: Punat, niže). Biljke, impostori i stare PBR teksture imaju vanjske CC0 izvore, popisane niže; nisu u cijelosti vlastiti sadržaj.
- Brodice (pasara, guc, gajeta, pentaša, jedrilica, jahta): vlastiti proceduralni modeli bez tekstura (`Assets/Editor/BoatsBuilder.cs`,
  izbornik Magistrala/Brodice: postavi), ništa preuzeto. Položaji vezova iz `coast.json` (rive, mandrači, marine; izvedeno iz
  OpenStreetMap)

## Mapillary (CC BY-SA 4.0)
- **Street-level imagery: Mapillary contributors (CC BY-SA 4.0)** — https://www.mapillary.com. Iz fotografija su izvedene samo
  činjenice: boja, vrsta zida i škure fasada uz staru cestu i D8 (`Assets/Editor/fasade-mapillary.json`, iz BalkanDriver
  tools/world/mapillary/s3_facades.py) i položaji uličnih svjetiljki / stupova (Mapillary map features,
  `Assets/Editor/rasvjeta-mapillary.json`, Tools/rasvjeta/mly_lights.py) te parkirališta (`Assets/Editor/parking-mapillary.json`, s5_parking.py) i vrsta i visina stabala uz staru cestu i D8
  (`Assets/Editor/stabla-mapillary.json`, iz BalkanDriver tools/world/mapillary/s4_trees.py; Assets/Editor/MapillaryTrees.cs).
  Fotografije nisu u igri ni u gitu. Podaci su izmjereni, filtrirani i prilagođeni koordinatama igre.
  Licenca: https://creativecommons.org/licenses/by-sa/4.0/. Za eventualne zaštićene prerade vrijedi ista licenca.

## Državna geodetska uprava (Otvorena dozvola, data.gov.hr/otvorena-dozvola)
- **Sadrži podatke Državne geodetske uprave, Digitalni ortofoto LiDAR 2022./23., preuzeto 19. 11. 2026., podaci su dorađeni.**
  Boja tla oko Kaštel Lukšića (`Assets/Generated/Terrain/Macro/*`, izvedeno: zgrade i ceste maskirane i popunjene, boja
  svedena na naše teksture; docs/dof-tlo.md), od 20. 11. 2026. i Kaštel Novi / Stari / zapadni Lukšić (pločice t_-9_-5, t_-8_-5, t_-7_-5).
  Procijenjene visine / katovi zgrada (`Assets/Editor/visine-zgrada.json`) izvedeni su iz sjena na istom ortofotu.
  Izvor i uvjeti: https://dgu.gov.hr/otvoreni-podaci/6596, https://data.gov.hr/otvorena-dozvola/.
  Obris rive, obale, lukobrana i parkirališta te krošnje stabala uz rivu (`Assets/Editor/riva.json`, Tools/riva/riva_build.py) izvedeni su
  s iste ortofotografije. Ne predstavlja službeni geodetski dokument. Isti tekst je u igri (mobitel → O igri).

## Creative Commons Attribution 4.0 (CC BY 4.0, https://creativecommons.org/licenses/by/4.0/)
- **Galeb (kombi GALEB TRANSPORT)**: "Mercedes Benz Sprinter 2006" by Max (https://sketchfab.com/Max-5532), CC BY 4.0 – izmijenjeno (uklonjene oznake, nova boja).
  Izvor: https://sketchfab.com/3d-models/mercedes-benz-sprinter-2006-f69de1315bb049c8946d57f6006acd73.
  Izmjene (`Tools/vehicles/galeb/make_galeb.py`): znak na maski i vratima, natpisi na bočnicama guma i registarska pločica
  preslikani u neutralno (izmišljena pločica), karoserija posvijetljena u bijelo, kotači razdvojeni po glavčinama, imena čvorova bez
  marke; bočna traka „GALEB TRANSPORT“ je naša (`galeb_livery.png`, `Tools/vehicles/galeb/livery.py`, font Inter).
  Datoteke: `Assets/Resources/Vehicles/Galeb/galeb.glb`, `galeb_livery.png`. U igri: mobitel → O igri (autor, licenca, izmijenjeno).

## Unity Asset Store (prijavljena Standard Unity Asset Store EULA; paket nije u gitu)
- **Coast & Dunes Environment – Dynamic Nature** (Beach, Sea, Islands, Dry Desert) — **NatureManufacture**, Unity Asset Store,
  Standard Unity Asset Store EULA. U probi „priroda”: slojevi tla (suho tlo s makijom, smeđa zemlja, kamenito tlo), suha trava i
  biljke za detalje terena, bor (preoblikovan prema alepskom boru), litice (zatamnjene u vapnenac), grm (tamnozeleno, bez cvijeta) u
  razdjelnom pojasu D8, more (shader „Water Sea Offset”), vjetar. Mapa paketa `Assets/NatureManufacture Assets` je u .gitignore
  i nije prisutna u ovom worktreeju; licenca paketa i dokaz kupnje ovdje nisu provjerljivi. Vidi `Tools/dijagnoza/licence.md`.

## Unity paketi
- glTFast (com.unity.cloud.gltfast), URP, Input System, Pipeline — Unity Technologies

## Fontovi (SIL Open Font License 1.1)
- **D-DIN Condensed Bold** — Datto Inc., https://www.datto.com/fonts/d-din (SIL OFL 1.1) — slova izmišljenih registarskih tablica
  (`Tools/vehicles/tablice.sh` → `Assets/Textures/Vehicles/Plates/tablice.png`; font i licenca u `Tools/vehicles/fonts/`).
  Na tablici je umjesto grba logo igre Magistrala (vlastiti, prebojan).
- **Inter** (Regular, SemiBold) — The Inter Project Authors, https://github.com/rsms/inter (v4.1) — tekst Matina mobitela i natpisi na prometnim znakovima
  (ploče portala, putokazi, nazivi mjesta i ulica: Inter SemiBold rasteriziran u `Assets/Generated/Signs/SignAtlas.png` pri
  gradnji, `Assets/Editor/SignArt.cs`).
  Datoteke: `Assets/Resources/Fonts/Inter-*.ttf`, licenca `Assets/Resources/Fonts/Inter-LICENSE.txt`.

- **Liberation Sans** — Digitized data copyright (c) 2010 Google Corporation (Reserved Font Names: Arimo, Tinos, Cousine); Copyright (c) 2012 Red Hat, Inc. (Reserved Font Name: Liberation). SIL OFL 1.1.
  Font i puni tekst licence: `Assets/TextMesh Pro/Fonts/LiberationSans.ttf`, `Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt`.
- Inter: Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter).
- D-DIN: Copyright (C) 2017 Datto Inc.; Reserved Font Names: D-DIN, D-DIN Condensed, D-DIN Expanded.
  OFL: https://openfontlicense.org/. Pri distribuciji fontova sačuvati cijeli tekst licence i copyright; rasterizirani natpisi nisu font softver.

## OpenStreetMap (ODbL 1.0)
- **© OpenStreetMap contributors** — https://www.openstreetmap.org/copyright.
  Podaci pod Open Database License 1.0: https://opendatacommons.org/licenses/odbl/1-0/.
  Cestovni graf, nazivi, ograničenja brzine, tlocrti / visine zgrada, namjena tla, parkirališta, parkovi i nasadi
  iz BalkanDriver podataka, filtrirani i prilagođeni igri. Prikaz u igri: mobitel → O igri → Karta i prostor.
  Atribucija ne zamjenjuje ODbL obveze pri javnoj distribuciji izvedene baze; vidi audit.

## Preneseni CC0 izvori iz BalkanDrivera
- Biljke i impostori (`Assets/Models/Plants`, `Assets/Textures/Imp`): Poly Haven
  [island_tree_02](https://polyhaven.com/a/island_tree_02) (maslina),
  [searsia_lucida](https://polyhaven.com/a/searsia_lucida) (grmovi),
  [wild_rooibos_bush](https://polyhaven.com/a/wild_rooibos_bush) (niski grm),
  [grass_medium_02](https://polyhaven.com/a/grass_medium_02) (trava),
  [pine_sapling_small](https://polyhaven.com/a/pine_sapling_small) (iglice proceduralnih borova / čempresa),
  [rock_07](https://polyhaven.com/a/rock_07) (kamen); kora ambientCG
  [Bark014](https://ambientcg.com/a/Bark014), [Bark007](https://ambientcg.com/a/Bark007), Lennart Demes.
  Izmjene: pojednostavljenje, zamjena lišća karticama, prebojavanje, atlasni impostori.
- Stare PBR teksture (`Assets/Textures`): Poly Haven
  [asphalt_02](https://polyhaven.com/a/asphalt_02), [rocky_trail](https://polyhaven.com/a/rocky_trail),
  [grass_ground](https://polyhaven.com/a/grass_ground), [rocks_ground_02](https://polyhaven.com/a/rocks_ground_02),
  [rock_boulder_dry](https://polyhaven.com/a/rock_boulder_dry),
  [concrete_floor_damaged_01](https://polyhaven.com/a/concrete_floor_damaged_01).
  Albedo s AO, zapakirane normale / hrapavost; dio boja dodatno prilagođen u Unityju.
- Materijali proceduralnih kamiona iz BalkanDrivera: ambientCG Fabric061, Fabric030, Metal021, Planks021,
  Plastic010, Leather037, Rubber004, DiamondPlate008C, Leather026, Plastic018B, Plastic012B, Leather018 (CC0).
  Popis izvora: `BalkanDriver/client/public/assets/CREDITS-vehicles.md`; ne predstavlja potvrdu da svaki materijal ulazi u ovaj build.

## Punat (treća strana; potvrditi licencu)
- **„2005 Fiat Grande Punto” — ActsOfDoge, Sketchfab**, osnova za
  `Assets/Models/Vehicles/Traffic/punat.glb` i `punat_lod1.glb`.
  BalkanDriver `CREDITS-traffic.md` navodi „Free Standard” i komercijalnu uporabu; izvorni URL i kopija licence
  nisu priloženi ovom worktreeju pa to nije potvrđena CC BY licenca. Izmjene: uklonjene oznake / tablice,
  izmišljeni identitet Fiyat Punat, nova maska, stakla, atlas, kotači i LOD. Dobrovoljna zasluga u mobitelu → O igri.

## Zvuk (Assets/Audio, izvor: BalkanDriver assets-src/zvuk — puni popis po datoteci u ATRIBUCIJA.md)

Obavezna atribucija (prikazano i u mobitelu, "O igri"):
- Motor (granule), truba, zrak kočnica / ručne, petrolej-pumpa, automobili u prometu: **Work With Sounds / Technical Museum of
  Slovenia** ("WWS City Bus MAN SG 220 – engine / horn / door opening-closing"), **Werstas** ("WWS Petrol Tanker", "WWS Siren of
  the fire chief's van"), **Konrad Gutkowski** ("WWS Fire engine siren"), **Museum of Municipal Engineering** ("WWS FSR Tarpan
  239D van starting") — CC BY 4.0, izmijenjeno (granularna resinteza, rezanje, petlje, filtriranje). commons.wikimedia.org
- Cvrčci noću: **Glaneur de sons**, "Nature sounds ambience in a Dordogne pond", CC BY 3.0, izmijenjeno.
- Kiša po krovu: **Beeld en Geluid**, "Regen op het dak van een stilstaande auto en onweer", CC BY-SA 3.0, izmijenjeno;
  izvedena datoteka `rain_roof.ogg` je pod CC BY-SA 3.0 (zamjena bez BY-SA: `rain_roof_pd.ogg`).

CC0 / javno dobro: Kenney (Impact Sounds), GGBotNet (Car Sound Effects Pack), Rango Mango (seagulls), HaelDB (dog barking, CC0
od dvojne licence), isaiah658 (ambient birds), Thimras (park ambiences), qubodup, rubberduck, bmaczero (udarci), Helge Klaus
Rieder (zvona Schruns), Nostrifikator (zvono), Tvabutzku1234 (vjetar), earthcalling (more), DavidDelon (cvrčci), cori (kiša,
grad), stephan (kafana), ezwa (jutro), Extemporalist (prolaz auta). Ostalo (žmigavac, pištalica, truba Galeba, dvotonac rezerva,
roletna, rampa): vlastita sinteza, CC0.
