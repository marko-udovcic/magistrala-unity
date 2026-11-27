# Flota – dorada (27. 11. 2026.)

| Vozilo / save ID | Nosivost | Motor, 6 brzina |
|---|---:|---|
| Furgon L4H1 / `furgon` | 1100 kg | 2,3 l, ~150 KS |
| Sprinter firme / `sprinter` | 1300 kg | 2,1–2,2 l, ~140 KS |
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
| Sprinter | Palete ≤1300 kg; ručni istovar | ☐ |
| Mali kombi | Palete ≤700 kg; slobodan parking u Lukšiću | ☐ |
| Galeb | Juretova drva 1250 kg; maglenke/habanje | ☐ |

Učitati stare saveove `atego`, `kombi`, `galeb`, s parkiranim vozilima i prihvaćenim/čekajućim drvima ili blokovima; ponovno spremiti/učitati. `-kamion atego` mora dati Furgon. Provjeriti Console. Vizualna i PhysX provjera ostaju leadu.
