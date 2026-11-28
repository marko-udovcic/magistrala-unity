# Paket 2711 — izvještaj

Traženi `.codex/izvjestaj.md` nije moguće zapisati: sandbox označava `.codex` samo za čitanje (`operation not permitted`). Izvještaj je zato ovdje; lead ga može premjestiti na traženo mjesto.

## Izmjene

1. **Paket:** `PaketData` prebačen na 2711 za sve ulaze; provjereno sedam datoteka i 265 vanjskih komada. OutsideStreets se ponovno računa pri `Build streets` i `Igra: podaci`; zamjena korijena Streets uklanja stare komade. U Assets/Tools kodu više nema putanje 2511.
2. **Semafori:** ostaje pet fotografiranih semafora i s06/s13. Veza na čvor traži model + položaj; poze/planovi ostaju. Spriječeno dupliciranje modelskih signalnih rekvizita kod tih poza. Putalj 77 koristi postojeći modelski put (74 s, SG1 A/B, SG2 C), uključujući skupine novih sporednih.
3. **Sporedne:** ponovno izvučeno iz mag-27-11, **20 ušća / 65 putanja**, SITES po imenima i položaju. Provjereni kraci 3→68/4, 5→66/13, 7→69/2, 8→70/3, 9→70/4, 13→81/0. Kupališni je skraćen; Tamunićev preklop vanjske trake obrezan. `easternMain=[]`; TrafficBuilder daje po jedan koridor iz modelskih osi 72–79 po smjeru, uz postojeći jaz radova s13.
4. **Riva:** RivaBuilder pri čitanju pokreće `Tools/riva/cut_package.py` postojećim DGU Pythonom. Maska se čita pri buildu, izvorni `riva.json` ostaje. Režu se trokuti, parking, stari rubovi/zidovi, crte, stabla i klasifikacija, uz očuvanje rupa; novi šav nema zid. Uklonjeno **9.544,19 m²** pavinga i **45,36 m²** parkinga, pontoni ostaju. Rezultat je privremen; podatkovni repo se ne zapisuje.
5. **Props:** Free provjerava i stvarne asfaltne trokute. Detalji i lampe već indeksiraju svu novu cestovnu geometriju. Vegetacija više ne čuva Mapillary/drvored/riva iznimke na novom kolniku; Drvoredi ga ne sade. Naših **48/48 stanica** upareno je OSM identitetom na BUS reference; postavljanje ograničeno na 8 m od natpisa uz provjeru otiska.
6. **Start/autopilot:** provjeren spojeni graf svih override slojeva: spur **3513**, pad **795**, novi ulaz **-7000000098011**, ruta do/od skladišta. Autopilot zadržava D8 ulaz modela 01 i postojeći meni obnove. Scene/postavke nisu uređivane, GPU Resident Drawer nije uključen.

## Provjere bez Unityja

- `python3 Tools/sporedne/check.py`: PASS; svi spojevi <6 m, najviše **3,82 m**, SHA izvora i ponovljiva ekstrakcija. Istočne visine čekaju scene raycast bake.
- `python3 Tools/sporedne/check-round2.py`: **2087 PASS**.
- `/Users/macair/Documents/BalkanDriver/tools/world/dgu/cache/venv/bin/python -I -B Tools/riva/check_package_cut.py`: PASS, stvarna razlika površina, rupe, zidovi i crte.
- Runtime i svi editor C# izvori kompilirani Unityjevim Roslynom iz shell-a, izlazi samo u `/private/tmp`; postojeća upozorenja `MatePhone.blicajScroll` i lokalni `StreetsBuilder.Junction`. `git diff --check` prolazi. Unity nije pokrenut.

## Točan redoslijed menija za leada

U glavnoj sceni, nakon kompilacije, sve pod `Magistrala/`:

1. `Build roads`
2. `Build streets` (uključuje izrezanu rivu i OutsideStreets)
3. `Build terrain`
4. `Grafika: detalji terena`
5. `Igra: podaci (stajališta, graf cesta)`
6. `Build traffic`
7. `Build signals` (zahtijeva već izgrađen TrafficSystem/data)
8. `Autopilot: osvježi stazu`
9. `Rekviziti: postavi`
10. `Autobusne stanice: postavi`
11. `Detalji: postavi`
12. `Vegetacija: očisti` → `Stabla: Mapillary` → `Vegetacija: drvoredi` → ponovno `Vegetacija: očisti`
13. `Ceste: osvježi materijale`
14. `Karta: ispeci`
15. `Bake cells (merge + cell scenes)`

`Riva: ulazni podaci` i regeneracija ortofoto izvora nisu potrebni za rez. Ako se izvor regenerira: nakon cesta izvesti `Riva: ulazni podaci`, pokrenuti postojeći `Tools/riva/riva_build.py` DGU Pythonom, zatim `Riva: izgradi` i nastaviti od terena. Rez se ponovno primjenjuje automatski.

## Rizici / otvoreno

**Pomaknuti semafor:** tablični `74/-7000000145003` udaljen je 485 m od fotografirane poze. Prostorno najbliži čvor istog modela je **-7000000145004, 28,85 m**, na kraju modela uz šav; builder upozorava. Poze i AI stop-crte ostaju fotografirane. Ostala četiri odgovaraju tablici (0,7–5,7 m). Dva postojeća Markova overridea za graph-node-12 ne prolaze potpise; nisu mijenjana.

Lead treba provjeriti taj šav, sve semaforske prilaze i Putalj SG1/SG2, svih 20 `[sporedne]` ušća i staru cestu u oba smjera, start/izlaz Bašića, Stop Shop, poznatih ~0,28 m na spoju 79/82, rubove rive, rasvjetu 68 i log eventualno izostavljenih stanica zbog BUS 8 m. Klasifikacija rive ostaje mreža 1 m; model 73 ostaje velik (256k trokuta). Vizualna provjera i build ostaju leadu.

## Datoteke za commit

```text
Assets/Editor/Drvoredi.cs
Assets/Editor/ExtraSignals.cs
Assets/Editor/OutsideStreets.cs
Assets/Editor/PaketData.cs
Assets/Editor/PropsBuilder.cs
Assets/Editor/PropsBuilder.Stanice.cs
Assets/Editor/RivaBuilder.cs
Assets/Editor/SignsBuilder.cs
Assets/Editor/TrafficBuilder.cs
Assets/Editor/TrafficSideBuilder.cs
Assets/Editor/VegetationClean.cs
Assets/Editor/sporedne-routes.json
Tools/riva/cut_package.py
Tools/riva/check_package_cut.py
Tools/sporedne/extract.py
Tools/sporedne/check.py
docs/izvjestaj-2711.md
```

Bez commita i pisanja u `.git`. Zatečene izmjene scene, ProjectSettings, `.codex/zadatak.md`, recovery/Tripo/vozila i ostale tuđe datoteke nisu dio ovog popisa.

## C1 popravak

Ispravak navoda 48/48 iz točke 5: paketnu BUS referencu ima **45/48 stanica**. `LoadStops()` za st41 (Strinje), st47 (Kaštel Sućurac Luvica 2) i st48 (Kaštel Sućurac Luvica 1), izvan modeliranog područja, zadržava staro pozicioniranje i ispisuje jedno upozorenje s popisom nepovezanih stanica. Nedostajuća veza više ne baca iznimku; obrada neispravnih podataka ostaje nepromijenjena. Povezane stanice zadržavaju postojeće ograničenje ≤8 m od BUS natpisa, bez novih alokacija u petlji pozicioniranja.

Provjereno uparivanje svih 48 stanica sa stvarnim paketom 2711; kod pozicioniranja ostao je nepromijenjen. `git diff --check` prolazi za ove dvije datoteke (zatečene izmjene scene imaju razmake na kraju redaka). Unity nije pokrenut; bez commita. Izmijenjene datoteke: `Assets/Editor/PropsBuilder.Stanice.cs` i `docs/izvjestaj-2711.md`.
