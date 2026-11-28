# Mali kombi (klasa Doblò/Combo/Caddy) – igračevo vozilo (27.11.)

Izvor: CGTrader paket "30 Big collection of commercial vehicles" → `commercial-van/commercial_van_quads.fbx` (43 objekta, 494 835 tri). `Textures.rar` nije raspakiran (solid RAR); sve je preslikano vlastitim PBR materijalima kao na L4H1/Sprinteru.
Skripte (`scripts/`): `s0_inspect.py`, `s0_parts.py`, `dens.py` (gustoća trokuta po dijelu – traženje otpada), `plate_tex.py`, `vlib.py` (zajedničko, isto kao u `sprinter-firma/scripts/`), `m2_build.py` (izvor → `m-lod0.blend`), `export.py` (LOD-ovi), `look.py` + `prod.py` (pregledi).
Ponovno: `Blender -b --factory-startup -P m2_build.py` pa `-P export.py -- <put>/m-lod0.blend mali-kombi 23000 5800`.

## Datoteke i trokuti
| datoteka | trokuti |
|---|---|
| mali-kombi.glb (LOD0) | 269 715 |
| mali-kombi_lod1.glb | 39 000 |
| mali-kombi_lod2.glb | 9 476 |

LOD0 po čvorovima: body 144 284 · wheel_0..3_mesh 4 × ~22 200 (guma 10k s profilom, ukrasni poklopac 10.6k, kapica 1.6k) · door_L/R 8 552 / 8 548 · door_slide_R 4 281 · steering_m 4 236 · door_rear_R/L 3 148 / 2 525 · firm_marking_R/L 3 019 / 1 242 · mglass_L/R 448 · tablice, kazaljke 2.
LOD0 je ~20k iznad gornje granice 250k: izvor je jednoliko gust (karoserija, branici, svjetla), a daljnje smanjenje bi se vidjelo – ostavljeno radi kvalitete.
**Bez globalne decimacije.** Što je smanjeno (samo otpad):
- *planarno spajanje* (Decimate Dissolve ≤ 0.5°, čuva oblik: spaja samo koplanarne pod-plohe) na karoseriji 42.4k → 27.1k, crnoj plastici 71.1k → 54.4k, gumama brtvi 29.6k → 18.3k, kromu, unutarnjim oblogama i dijelu stražnjih svjetala;
- gume 121.5k → 40.1k (0.33; profil i bok kao izvor), ukrasni poklopci 85.2k → 42.6k (0.5; s 0.35 su se vidjele grbe na krakovima – vraćeno), središnje kapice 20.9k → 6.3k, lamele maske 16.6k → 4.1k (bile su 64 tri/cm²), unutrašnjost farova i retrovizorskih žmigavaca ~0.4–0.6, metlice brisača 5.9k → 2.3k, treće stop-svjetlo 3.2k → 1.1k. Obrisani zalutali quadovi na unutarnjem boku guma.
LOD1: body 23k, kotači 4 × 2.3k, vrata 900/700, livreja 900. LOD2: body 5.8k, kotači 4 × 520, bez kazaljki (LOD2 ima grublje sjenčanje, za daljinu).

## Bez marki
Geometrija izvora nema slova ni znakova (Opel/Fiat oznake su bile samo u teksturama koje se ne koriste). Ostaje neutralan kromirani okrugli umetak na maski i okrugla crna brava straga – bez ikakvog znaka. Gume bez natpisa. Provjereno izbliza (maska, stražnja vrata, kotač). Izvorne tablice zamijenjene našima.

## Čvorovi (glTF, +Y gore, +Z naprijed, +X = lijevo/vozačeva strana; origin korijena = tlo)
- `mali_kombi` (korijen) → `body` (statično, uključujući instrumente `gauges` + vizir `gauge_hood`, izvornu ručicu mjenjača na konzoli (dio 272 tri iz izvora, nije posebno provjeren izbliza), kočione diskove, brisače).
- kotači: `wheel_N` → `wheel_N_spin` → `wheel_N_mesh`. glTF položaji: wheel_0 (−0.785, 0.333, −1.480), wheel_1 (0.785, 0.333, −1.480), wheel_2 (−0.785, 0.333, 1.275), wheel_3 (0.785, 0.333, 1.275). Polumjer 0.329 m, međuosovinski razmak 2.755 m, trag 1.57 m.
- `steering` (lokalna Z u glTF = os stupa) → `steering_m` (subdiv ×1). Blender središte (0.397, −0.632, 1.081).
- `needle_speed` / `needle_rpm` → `*_m` (isti brojčanici kao L4H1, 0.26 × 0.12 m, u vizir-kućištu na armaturi iza volana).
- `mglass_L` / `mglass_R`, `door_L` / `door_R` (origin na prednjoj šarki), `door_slide_R` (klizna vrata desno), `door_rear_L` / `door_rear_R` (asimetrična krila, origin na vanjskoj šarki), `plate_front`, `plate_rear`, `firm_marking_L`, `firm_marking_R`.
- sidra: `a_eye` Blender (0.397, −0.12, 1.36) → glTF (0.397, 1.36, 0.12); `a_dash`; `a_headL` / `a_headR` (±0.60, −1.85, 0.90).

## Materijali
Kao Sprinter: Body_Paint (bijela), Firm_Livery, Chrome, Rim (čelični naplatak), **Hubcap** (srebrni plastični poklopac), Black_Plastic, Rubber, Chassis_Black, Glass, Lamp_Lens, Lamp_Reflector, **Lights_Front** (žarulje u farovima), **Lights_Indicator** (narančasti dio fara, retrovizori, srednji okrugli dio stražnjeg svjetla), **Lights_Brake** (crveni dijelovi stražnjih svjetala + treće stop-svjetlo; 0.3), **Lights_Reverse** (donja traka stražnjeg svjetla), Reflector_Red (katadiopteri u branik), Tyre, Brake_Disc, Interior_*, Gauges, Needle, Plate, Mirror_Glass. Lights_Fog ne postoji (izvor nema maglenke, samo slijepe poklopce).
- Livreja: firm-markings.png prilijepljen na bočne plohe (y 0.32…1.95, z 0.70…1.64, 4 mm odmak), logo naprijed, pruge prema straga; desno prelazi preko kliznih vrata.
- Tablice: "ST 276-KM" (izmišljena).

## Ostalo
- Dimenzije: dužina 4.38 m, širina ~1.88 m karoserija / 2.17 m s retrovizorima, visina 1.87 m krov (2.03 m s antenom).
- Masa (prijedlog): prazan ~1 400 kg, nosivost ~700 kg (GVW ~2 100 kg); težište prazan ~0.65 m iznad tla, ~56 % naprijed.
- Pregledi: `~/Desktop/Magistrala-slike/mali-kombi/` — `34front`, `side`, `rear`, `headlight`, `wheel`, `cab` × `-original.png` / `-nas.png`, plus `gauges-nas.png`. Izvorne snimke imaju privremene boje.
- Otvoreno: interijer izvora je jednostavan (armatura ~1.7k + obloge), zadržan; instrumenti su ploča s vizirom na armaturi (izvor nema kućište instrumenata); unutarnje obloge vrata ostale u `body`.

## CREDITS
- "Commercial van" iz paketa "30 Big collection of commercial vehicles" (CGTrader), Royalty Free licenca – modified (izmijenjeno: novi materijali, livreja firme, tablice, instrumenti, razdvojeni kotači/vrata/volan, planarno spajanje i smanjenje gustih dijelova, LOD-ovi).
