# Flota – dorada (27. 11. 2026.)

| Vozilo / save ID | Nosivost | Motor, 6 brzina |
|---|---:|---|
| Furgon L4H1 / `furgon` | 1100 kg | 2,3 l, ~150 KS |
| Dugi kombi firme / `sprinter` | 1300 kg | 2,1–2,2 l, ~140 KS |
| Mali kombi / `mali` | 700 kg | 1,6 l, ~105 KS |
| Očev Galeb L3H2 / `galeb` | 1400 kg | 2,3 l, ~120 KS; tromiji turbo/mjenjač |

Save migracija: `atego` → `furgon`, `kombi` → `sprinter`; položaji i gorivo ostaju. Mali ima zasebno gorivo i slobodan parking uz ostale u skladištu u Lukšiću. E bira najbliže vozilo. Ture prilagođavaju teret aktivnom vozilu. Juretova drva smanjena su na jedan metar / **1250 kg**, Ivičin materijal na pola palete i pet vrića / **1400 kg**; usklađeni su tekstovi i nastavci starih poslova.

Novi GLB-ovi kopirani su s LOD-ovima i izvještajima; izvori nisu mijenjani. Zajednički L4H1 put pokriva fiziku iz čvorova kotača, dimenzije, `a_eye`, retrovizore, kazaljke/rasvjetu, izdvojene brisače/vjetrobran, žmigavce i svjetla iz `a_headL/R`. Galeb zadržava habanje i maglenke. Panelni kombiji imaju ručni istovar; izvori nemaju dovršen tovarni interijer.

Ukinuti su Atego fallback, varijabla okoline te stari odabiri modela/kabine/zvuka. **Granica bez izmjene scene:** stari meshovi ostaju serijalizirana ovisnost scene (učitaju se sa scenom, zatim ih `Prepare` odbaci). Njihovo potpuno izbacivanje iz učitavanja traži naknadnu izmjenu scene. Stari asseti ostaju na disku.

Datoteke:

- `Assets/Resources/Vehicles/{SprinterFirma,MaliKombi,GalebL3H2}/`: GLB + LOD1/2, izvještaji i `.meta`; pripadajuće mape `.meta`.
- `Assets/Scripts/Truck/{PlayerTruck,TruckController,CabinRig,VehicleLook,TruckLights,WindscreenFx}.cs`.
- `Assets/Scripts/Game/MagistralaGame{,.Vehicles,.Day,.Fuel,.Tours,.Faturete}.cs`.
- `Assets/Scripts/Audio/TruckAudio.cs`, `Assets/Scripts/CliArgs.cs`, `Assets/Resources/Game/faturete.json`, `CREDITS.md`.
- `Tools/flota/{import_models,finish_glb,check_fleet}.py`, ovaj izvještaj. `MirrorCams` i `TurnSignals` koriste postojeći zajednički kod.

Offline prolaze: runtime C# kompilacija (postojeće upozorenje `blicajScroll`), devet GLB-ova/geometrija/rig/emisija/idempotentnost, stvarne C# specifikacije/migracije, svih 39 fatureta i 16 provjera žmigavaca. Ponoviti: `python3 Tools/flota/check_fleet.py`. Unity nije pokrenut; nema commita ni naših izmjena scene, ProjectSettingsa ili GPU Resident Drawera. Zatečene izmjene ostavljene su netaknute.

## Lead – editor checklista

Za **svaki red**: prazna/puna vožnja, C kabina, oba retrovizora, kazaljke/noćno osvjetljenje, brisači na kiši, L/stop/rikverc/žmigavci, E, save/load položaja i goriva te prijelazi LOD-a:

| Vozilo | Isporuka / posebna provjera | Gotovo |
|---|---|---|
| Furgon | Palete ≤1100 kg; stražnja vrata/istovar | ☐ |
| Dugi kombi firme | Palete ≤1300 kg; ručni istovar | ☐ |
| Mali kombi | Palete ≤700 kg; slobodan parking u Lukšiću | ☐ |
| Galeb | Juretova drva 1250 kg; maglenke/habanje | ☐ |

Učitati stare saveove `atego`, `kombi`, `galeb`, s parkiranim vozilima i prihvaćenim/čekajućim drvima ili blokovima; ponovno spremiti/učitati. `-kamion atego` mora dati Furgon. Provjeriti Console. Vizualna i PhysX provjera ostaju leadu.


## Review K1/K2 – 28. 11. 2026.

- **K1:** vidljivi naziv vozila, akuzativ za E i banner koriste **Dugi kombi firme**. Ostaju Furgon, Mali kombi i Galeb. Pretraženi su svi `Assets/Scripts` te JSON/TXT u `Assets/Resources` za Sprinter, Ducato, Crafter, Mercedes, Fiat, Doblo i Atego. Telefonske zasluge također koriste neutralne opise; autori, licence i poveznice izvora ostaju. Interni `sprinter`, putanje resursa i komentari nisu migrirani.
- **K2:** `AdaptFirmLoad` pri obnovi firmine ture ograničava kg i samo nedovršene dostavne etape prilagođava na `Unload.Rows - rowsOff`. Završene etape ostaju netaknute; `rowsOff` se ograničava na fizički kapacitet novog sanduka. Svaki pozitivan istovar zadržava barem jedan red ako ima mjesta. Inače se prvo spajaju najmanji istovari (kod jednakih raniji), a preostali redovi dijele se proporcionalno izvornim veličinama uz minimum jedan. Nulti istovar koristi postojeće ručne gajbe, umjesto praznog `FirmUnload.Begin()` slijeda. Negativni broj redova starog pojedinačnog posla znači sve preostale redove. Panelni kombiji ostaju na ručnom istovaru.
- Kg se prvo ograničava nosivošću. Zatim se smanjuje samo preostali udio ture razmjerno smanjenju redova; već završeni udio ostaje. To prati postojeći obračun mase prema broju preostalih dostavnih etapa. Ponovno učitavanje već prilagođenog posla ne smanjuje ga opet.
- Markov stvarni save pročitan je **samo za čitanje**. C# provjera simulira njegovu strukturu: utovar, šest istovara `[0,1,1,0,1,1]`, povratak, `leg=2`, `rowsOff=0`, `kg=2400`. Novi istovari: **`[0,0,1,0,1,1]`**, `rowsOff=0`, ukupni parametar mase ture **870,8334 kg** (trenutno u vozilu prema postojećem obračunu 725,6945 kg). Etape, odredišta i broj dostava nisu uklonjeni.
- `python3 Tools/flota/check_fleet.py` prolazi: izvršava stvarnu izdvojenu C# migraciju preko Mono kompajlera/runtimea, ispisuje nove redove te pokriva ponovno učitavanje, djelomičnu isporuku, proporcionalnu raspodjelu, potrošen kapacitet, pojedinačnu dostavu, turu prije utovara, ručni istovar i izuzimanje fatureta. Prolaze i postojeće provjere devet GLB-ova, četiri specifikacije i 39 fatureta. Unity nije pokrenut i commit nije napravljen. Zatečene izmjene scene i ProjectSettingsa nisu dirane.

Datoteke ove review dorade:

- `Assets/Scripts/Game/MagistralaGame.Vehicles.cs`
- `Assets/Scripts/Game/Phone/MatePhone.cs`
- `Assets/Scripts/Game/MagistralaGame.Day.cs`
- `Tools/flota/check_fleet.py`
- `docs/flota-dorada.md`
