# Sprinter – firmin kombi (dugi, visoki krov) – igračevo vozilo (27.11.)

Izvor: CGTrader paket "30 Big collection of commercial vehicles" → `van-sprinter/van-vray.FBX` (39 objekata, 296 114 tri). `Textures.rar` nije raspakiran (solid RAR); sve je preslikano vlastitim PBR materijalima kao na L4H1 (u izvoru su teksture samo sitnice: BrakeDisc, red/orange/white.png, HeadlightBump).
Skripte (`scripts/`): `s0_inspect.py` (FBX → `s-src.blend` + popis), `s0_parts.py` (dijelovi po objektu), `plate_tex.py` (tablica), `vlib.py` (zajedničke funkcije, iste konvencije kao `kombi-l4h1/k2_build.py`), `s2_build.py` (izvor → `s-lod0.blend`), `export.py` (LOD0/1/2 + izvještaj trokuta), `look.py` + `prod.py` (EEVEE pregledi).
Ponovno: `Blender -b --factory-startup -P s2_build.py` pa `-P export.py -- <put>/s-lod0.blend sprinter 24000 5900`.

## Datoteke i trokuti
| datoteka | trokuti |
|---|---|
| sprinter.glb (LOD0) | 250 080 |
| sprinter_lod1.glb | 39 809 |
| sprinter_lod2.glb | 9 576 |

LOD0 po čvorovima: body 147 348 (karoserija, farovi, maska, branici, retrovizori, šasija, interijer, instrumenti, ručica mjenjača) · wheel_0..3_mesh 4 × ~17 100 (guma 11.2k s punim profilom, naplatak 4.4k, poklopac 1.5k) · door_L/R 5 941 / 5 911 · steering_m 4 236 · firm_marking_R/L 4 007 / 2 927 · door_rear_R/L 3 956 / 3 723 · door_slide_R 3 183 · mglass_L/R 204 · tablice, kazaljke 2.
**Bez globalne decimacije.** Smanjeno samo: gume 64 000 → 44 800 (0.7; profil i bok izgledaju isto kao izvor, vidi `wheel-*`), kućišta retrovizora 19 375 → 11 625, žmigavci na retrovizorima 5 856 → 2 928, reflektorske posude stražnjih svjetala 19 680 → 9 840. Uklonjeni znakovi (≈15.7k tri). Karoserija, prednja svjetla, maska, interijer = izvorna geometrija; volan subdiv ×1.
LOD1: body 24k, kotači 4 × 2.3k, vrata 900/700, livreja 900, sitno ≤300. LOD2: body 5.9k, kotači 4 × 520, bez kazaljki.

## Bez marki
Uklonjeno: Mercedesova zvijezda na maski (zvijezda + krakovi unutar prstena – ostaje neutralni crni okrugli umetak), zvijezda na haubi, zvijezda na stražnjim vratima (ostaje samo plitko udubljenje u limu), natpis "Sprinter" straga, zvijezde na poklopcima kotača (ostaje glatka kapica). Izvorne tablice zamijenjene našima. Provjereno na EEVEE snimkama izbliza (maska, stražnja vrata, kotač).

## Čvorovi (glTF, +Y gore, +Z naprijed, +X = lijevo/vozačeva strana; origin korijena = tlo)
- `sprinter` (korijen) → `body` (sve statično, uključujući instrumente `gauges`, ručicu `gear_lever`, kočione diskove, brisače).
- kotači: `wheel_N` → `wheel_N_spin` → `wheel_N_mesh` (origin u središtu). wheel_0 (x−, straga), wheel_1 (x+, straga), wheel_2 (x−, naprijed), wheel_3 (x+, naprijed). glTF položaji: wheel_0 (−0.859, 0.365, −1.867), wheel_1 (0.859, 0.365, −1.867), wheel_2 (−0.855, 0.369, 2.448), wheel_3 (0.855, 0.369, 2.448). Polumjer 0.365 m, međuosovinski razmak 4.315 m, trag ~1.71 m (sredine guma). Jednostruki stražnji kotači.
- `steering` (prazan u središtu volana, lokalna Z u glTF = os stupa) → `steering_m`. Blender središte (0.509, −1.783, 1.552).
- `needle_speed` / `needle_rpm` (lokalna Z = normala brojčanika prema oku, kazaljka na 0) → `*_m`. Brojčanici isti kao L4H1 (0–180 km/h, 0–50 ×100/min, 260°). Instrumenti 0.28 × 0.13 m u izvornom kućištu iza volana.
- `mglass_L` / `mglass_R`, `door_L` / `door_R` (prednja vrata sa staklom, okvirom i kvakom; origin na prednjoj šarki), **`door_slide_R`** (bočna klizna vrata desno, origin na prednjem rubu – dodatni čvor, nema ga na L4H1), `door_rear_L` / `door_rear_R` (stražnja krila, origin na vanjskoj šarki), `plate_front`, `plate_rear`, `firm_marking_L`, `firm_marking_R`.
- sidra: `a_eye` Blender (0.509, −1.40, 1.90) → glTF (0.509, 1.90, 1.40); `a_dash`; `a_headL` / `a_headR` (±0.73, −3.14, 0.975).

## Materijali
Isti nazivi kao L4H1: Body_Paint (bijela + clearcoat), Firm_Livery (firm-markings.png, alpha MASK), Chrome, Rim, Black_Plastic, Rubber, Chassis_Black, Glass, Glass_Dark, Lamp_Lens, Lamp_Reflector, **Lights_Front** (žarulje/projektori unutar fara, emisija 1.0), **Lights_Indicator** (prednji, na retrovizorima, bočni ponavljač, stražnji; emisija 0), **Lights_Brake** (crvene stražnje leće + žarulje, 0.3 = poziciono), **Lights_Reverse** (bijeli dio stražnjeg svjetla, 0), Lights_Marker (bočna narančasta poziciona svjetla, 0.3), Reflector_Red (katadiopteri u branik), Tyre, Brake_Disc, Interior_Dash/Trim/Seat, Gauges, Needle, Plate, Mirror_Glass. **Lights_Fog** ne postoji – izvor nema prednja maglenka (na branik su slijepi poklopci).
- Livreja: isti firm-markings.png kao L4H1, ali **prilijepljena na lim** (kopija bočnih ploha karoserije, odrezana na prozor y −1.0…3.12, z 0.84…2.48, odmaknuta 4 mm) – prati udubljenja bočnih ploča. Logo naprijed, pruge se šire prema straga, na obje strane. Na desnoj strani prelazi i preko kliznih vrata (dio je u `firm_marking_R`, ne pomiče se s vratima).
- Tablice: tex/plate.png "ST 531-KM" (izmišljena), prednja i stražnja na izvornim mjestima.

## Ostalo
- Dimenzije: dužina 6.95 m, širina 2.00 m karoserija / 2.43 m s retrovizorima, visina 2.76 m (s antenom; krov ~2.72 m).
- Masa (prijedlog, klasa 3.5 t): prazan ~2 100 kg, nosivost ~1 300 kg (GVW 3 500 kg); težište prazan ~0.85 m iznad tla, ~52 % naprijed.
- Pregledi: `~/Desktop/Magistrala-slike/sprinter/` — `34front`, `side`, `rear`, `headlight`, `wheel`, `cab` × `-original.png` / `-nas.png`, plus `gauges-nas.png`. Izvorne snimke imaju privremene boje (teksture nedostupne), geometrija ista.
- Nije rađeno / otvoreno: interijer izvora je jednostavan (armatura 3.9k, sjedala 2.2k) – zadržan, dodani su instrumenti, kazaljke i ručica mjenjača; nema tovarnog prostora iznutra; unutarnje obloge vrata ostale u `body`; brisači nisu zasebni čvorovi. Smjer vrtnje kazaljki provjeriti u Unityju (glTFast zrcali X).

## CREDITS
- "Van Sprinter" iz paketa "30 Big collection of commercial vehicles" (CGTrader), Royalty Free licenca – modified (izmijenjeno: uklonjeni znakovi proizvođača, novi materijali, livreja firme, tablice, instrumenti, ručica mjenjača, razdvojeni kotači/vrata/volan, smanjene gume i sitni dijelovi, LOD-ovi).
