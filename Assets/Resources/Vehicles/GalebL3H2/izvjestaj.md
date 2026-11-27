# GALEB – očev stari kombi (2007., ~19 god.) – priča (27.11.)

Izvor: CGTrader paket "30 Big collection of commercial vehicles" → `l3h3-truck/L3H2_truck_quads.fbx` (96 objekata, 237 258 tri; isti autor/struktura kao L4H1). Teksture (`textures.rar`, `07.rar`) nisu raspakirane (solid RAR) – vlastiti materijali. Izvor je bio crven → prebojen u bijelo.
Skripte (`scripts/`): `g_tex.py` (natpis + istrošenost, Blender tekst + numpy), `plate_tex.py`, `vlib.py`, `g2_build.py` (izvor → `g-lod0.blend`), `export.py` (LOD-ovi; `wear_*` u LOD2 izbačeni), `look.py` + `prod.py`.
Ponovno: `-P g_tex.py -- ../tex`, `-P plate_tex.py -- ../tex 207-GB`, `-P g2_build.py`, `-P export.py -- <put>/g-lod0.blend galeb 21500 6000`.

## Datoteke i trokuti
| datoteka | trokuti |
|---|---|
| galeb.glb (LOD0) | 247 256 |
| galeb_lod1.glb | 40 107 |
| galeb_lod2.glb | 9 673 |

LOD0 po čvorovima: body 115 896 · wheel_0..3_mesh 4 × ~21 840 (guma 12.8k, naplatak 7.8k, vijci 1.2k) · wear_L/R 7 506 / 7 488 · door_L/R 5 441 / 5 357 · steering_m 4 236 · door_slide_R 3 861 · door_rear_R/L 3 181 / 2 048 · wear_rear 2 436 · firm_marking_L/R ~850 · mglass_L/R 369.
Bez decimacije osim guma 64 000 → 51 200 (0.8, kao L4H1).

## Izgled (priča)
- **Boja**: bijela, malo potamnjela/zagasita (Body_Paint 0.80/0.80/0.77, roughness 0.42, gotovo bez laka). Plastika branika i letvi: stara tamnosiva, mat (Black_Plastic 0.055, rough 0.72). Čelični naplatci (Rim sivi, poluma). Leće farova blago mutne.
- **Natpis** (`firm_marking_L/R`, tex/galeb-lettering.png 2048×512, BLEND): plavi "GALEB TRANSPORT" + znak galeba (dva luka) + "obrt za prijevoz · Kaštel Lukšić" (stil postojećeg galeb_livery.png, ali plava slova na bijelom limu, bez trake). Izblijedjelo (mrljasto izbljeđivanje, svjetlija plava), ispucalo (tanke pukotine) i s otpalim komadićima folije. **Slovo R u TRANSPORT nedostaje** – ostao samo blijedi sivi trag ljepila. Natpis prilijepljen na lim (y −0.55…2.65, z 1.44…2.24), čita se ispravno s obje strane (desno zrcaljen UV).
- **Prljavština/hrđa** (`wear_L/R`, `wear_rear`, tex/galeb-wear-side.png 2048×1024, galeb-wear-rear.png, BLEND, 3 mm iznad lima): prašina/blato na pragovima i donjem dijelu, jače straga; točkice hrđe oko oba blatobrana i na donjim rubovima prednjih i kliznih vrata te stražnjih krila; nekoliko svijetlih ogrebotina na bočnim pločama; masnoća oko stražnje kvake.
- **Interijer**: sjedala izlizana (svjetlija, mat tkanina), armatura izblijedjela; instrumenti (isti kao L4H1, s vizirom) i ručica mjenjača na armaturi.
- Bez marki: izvor nema znakova (provjereno na snimkama); tablice "ST 207-GB" (izmišljene) naprijed na braniku i straga na lijevom krilu.
- **Nije napravljeno**: udubljenja (nema normal mape – ogrebotine su samo u boji), puni ukrasni poklopci kotača (izvor ima čelični naplatak s plosnatom središnjom kapom; nije dodavan poklopac), tekstura habanja sjedala (samo materijal).

## Čvorovi (glTF, +Y gore, +Z naprijed, +X = lijevo/vozač)
- `galeb` → `body`, `wheel_N` → `wheel_N_spin` → `wheel_N_mesh`: wheel_0 (−0.896, 0.367, −2.031), wheel_1 (0.896, 0.367, −2.031), wheel_2 (−0.892, 0.367, 2.059), wheel_3 (0.892, 0.367, 2.059). Polumjer 0.365 m, međuosovinski 4.09 m, trag ~1.79 m.
- `steering` → `steering_m` (Blender središte 0.509, −1.503, 1.499), `needle_speed` / `needle_rpm`, `mglass_L/R`, `door_L/R`, `door_slide_R`, `door_rear_L/R`, `plate_front`, `plate_rear`, `firm_marking_L/R`, **`wear_L`, `wear_R`, `wear_rear`** (novi, prozirni sloj prljavštine – može se isključiti za "oprani" Galeb).
- sidra: `a_eye` Blender (0.509, −1.09, 1.74) → glTF (0.509, 1.74, 1.09); `a_dash`; `a_headL/R` (±0.72, −2.55, 1.17).
- Materijali kao L4H1 (Lights_Front/Fog/Indicator/Brake/Reverse – ovdje postoje i maglenke) + Galeb_Lettering, Galeb_Wear_Side, Galeb_Wear_Rear.

## Ostalo
- Dimenzije: dužina 6.04 m, širina 2.06 m karoserija / 2.65 m s retrovizorima, visina 2.52 m krov (2.59 m s antenom).
- Masa (prijedlog, 3.3–3.5 t klasa): prazan ~2 000 kg, nosivost ~1 300 kg; težište ~0.85 m, ~53 % naprijed.
- Postojeći Unity Galeb (`MagistralaUnity/Assets/Resources/Vehicles/Galeb/galeb.glb`) i `client/public/assets/models/vehicles/galeb.glb` **nisu dirani** – zamjena je na Marku/Unity agentu (GalebAssets.cs provjerava nazive bez marki – nazivi ovdje su čisti).
- Pregledi: `~/Desktop/Magistrala-slike/galeb/` — `34front`, `side`, `rear`, `headlight`, `wheel`, `cab` × `-original.png` / `-nas.png` + `gauges-nas.png` (izvorne snimke s privremenim bojama).

## CREDITS
- "L3H2 truck" iz paketa "30 Big collection of commercial vehicles" (CGTrader), Royalty Free licenca – modified (izmijenjeno: nova boja i materijali, natpis GALEB TRANSPORT, slojevi istrošenosti, tablice, instrumenti, ručica mjenjača, razdvojeni kotači/vrata/volan, LOD-ovi).
