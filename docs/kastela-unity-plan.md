# Kaštela u Unityju – plan (2026-11-18)

Markov nalog: prvo Kaštela potpuno dobra u Unityju, tek onda Split. Web igra (BalkanDriver) je referenca i izvor podataka.
Scenarij: Mate Bašić (35), firma i početak u Lukšiću, bez mita, bez stvarnih imena firmi. Plaća po km ceste našeg grafa:
firma 10 € + 3 €/km, fatureta 8 € + 4 €/km (najmanje 45 €), zaokruženo na 0,5 €.

## Što ima web igra, a što Unity

| Sustav | Web igra | Unity (grana kastela-igra) |
| --- | --- | --- |
| Svijet Lukšić → Sućurac | teren, modeli križanja/dionica, zgrade, drveće, more | **ima** (WorldBuilder i drugi) |
| Promet, semafori | graf + kutije križanja | **ima** D8 u oba smjera (TrafficBuilder) |
| Firmin kamion Benzer (Atego), Kormoran livreja | firm_truck.glb, palete u sanduku | **ima** model i fiziku; **novo:** palete samo dok vozi turu, teret 2,4 t na ovjesu |
| Stajališta (stops.json) | 36 u regiji, u Kaštelima 11 | **novo:** 11 stajališta s mjestom za istovar (bay) |
| Posao firme (Zdravko) | ture iz skladišta, kartica ture | **novo:** 3 ture iz skladišta u Lukšiću |
| GPS | ruta po grafu, žuta linija na minikarti, oznaka cilja | **novo:** Dijkstra po grafu (+ slojevi junction-overrides, zatvorene ceste), minikarta s rutom, oznaka na mjestu istovara |
| Isporuka | rampa / mjesto za istovar, E, bonus unazad | **ima:** provjera mjesta kao engine.ts inBay, E, ručna se sama povuče |
| Novčanici | gotovina + račun obrta (200 €) | **ima:** oba, plaća firme u gotovinu, pamti se (spremanje) |
| Rampa / dizalica (tail lift) | firmUnload.ts, animacija | **novo (FirmUnload.cs):** rolo vrata gore, rampa dolje, red po red paleta na tlo iza kamiona (12 s), rampa gore, vrata dolje; W/S ili pomak prekida (palete se vrate) |
| Bonusi / kazne | unazad +10 %, noć +20 %, kašnjenje, oštećenje | **novo:** iste stavke kao computePayout (rok kao regions.ts, šteta od udaraca kao engine.ts), kartica isplate sa zvjezdicama |
| Dnevni sat i dan | 6:00 poziv, 1/6 min igre / s (REGION_CLOCK), mrak, spavanje | **novo:** sat i dan u HUD-u (1/6 min / s), firma radnim danom 6–17 h, N kraj dana; mrak i spavanje još nema |
| Mobitel, pozivi, poruke | MatePhone, calls.ts (Zdravko, Vesna…) | **novo (Phone/MatePhone.cs):** Tab, igra stoji; Poruke, Pozivi, Kontakti, Poslovi (Karta / Banka uskoro); Zdravko 6:00 (4 odgovora Q3) i ljuti u 7:00; Vesnina poruka u 14 h, najviše 2 nasumične dnevno |
| Priča: prolog, 1. poglavlje | chapter1.json, prologue.ts, bilježnica, banka | nema |
| Matin glas | mateLines.json (69 replika), titlovi | nema |
| Radio | stanice, radio uživo (proxy) | nema |
| Galeb (TN kombi, CC-BY) | galeb.glb, faturete | nema |
| Faturete (Jure, drva) | 17–22 h, dogovorena cijena | nema |
| Pumpa, kafana, policija Stipe, banka | stops/*.tsx | nema (samo stajališta kao točke) |
| Radovi na cesti (zapornice) | Roadworks2, road-closures.json | GPS ih zaobilazi; zapornica se ne crta |
| Vraćanje na cestu | Backspace na cestu | R uspravi na mjestu |
| Karta (M) | velika karta | nema |
| Spremanje igre | server + lokalno | **novo:** magistrala-save.json u persistentDataPath (dan, sat, novčanik, ugled, pozivi, poruke, isporuke, tura u tijeku, stajalište) |

## Najveće rupe (vidljivo u probnoj vožnji)

- **Ulice grafa i dvorišta stajališta ne postoje u Unityju.** Unity ima samo ceste iz Markovih modela (D8 i krakovi).
  Bardejovska ulica, prilaz skladištu (ogranak `spur`) i dvorište skladišta su trava; kamion do D8 vozi po livadi.
  Web crta naše ulice (vrpce grafa) i tlo dvorišta (DeliverySite). Ovo je prvo za Kaštela (posao helpera za ceste ili sljedeći korak).
- Skladište u Lukšiću nema svoju zgradu ni rampu, samo opću zgradu iz pločica.
- Autopilot (test) ne gleda promet: na križanju s D8 stane u autobus. GPS je dobar, testna vožnja kreće 220 m prije cilja (`-jobnear`).

## Redoslijed (mali koraci)

0. **Ulice grafa i dvorišta stajališta** u Unityju (vrpce iz graph.json + overrides, tlo dvorišta, prilazi).
1. **Osnovna petlja** (gotovo ovdje): skladište → tura → GPS → mjesto istovara → plaća.
2. ~~Istovar dizalicom~~ (gotovo, FirmUnload.cs).
3. ~~Bonusi i kartica isplate~~ (gotovo).
4. ~~Dnevni sat, Zdravkov poziv u 6:00 / 7:00~~ (gotovo); dalje: mrak po satu, spavanje doma, Galeb navečer.
5. ~~Mobitel~~ (gotovo, Phone/MatePhone.cs); dalje: Vesnina kupovina, cjenkanje fatureta, banka u mobitelu.
6. ~~Spremanje~~ (gotovo).
7. Vraćanje na najbližu cestu (graf) umjesto samo uspravljanja; zapornice radova na cesti.
8. Galeb i prva fatureta (Jure, dvor Bašića → Sućurac), 8 € + 4 €/km, najmanje 45 €.
9. Prolog lite i 1. poglavlje (kartice + Matin glas), bilježnica, banka.
10. Radio, pumpa (gorivo), kafana, Stipe.

## Kako se gradi (Unity)

- Podaci: izbornik **Magistrala/Igra: podaci (stajališta, graf cesta)** → `Assets/Generated/Game/Resources/MagistralaGame.asset`
  (iz stops.json, graph.json + junction-overrides*.json, road-closures.json, distances.json). Ako asset nedostaje, napravi se
  sam prije builda igrača.
- Igra: `Assets/Scripts/Game/*` se stvara pri učitavanju scene (scena se ne mijenja).

## Točke dostave i ture (paket mag-11-19, grana ture-11-19)

- Uvoz u istom izborniku (`Assets/Editor/GameDataBuilder.Tours.cs`): `~/Documents/mag-11-19/world_1to1.json` (ne 1to3), točke iz
  okvira paketa afinom transformacijom iz `paket-approaches.json` (kao OutsideStreets) u naš, pa u Unity. Zadrže se samo točke s
  turama u području ulica (Štafilić → Sućurac) koje GPS dosiže iz skladišta u Lukšiću i natrag; točka se prikvači na najbližu
  ulicu grafa (D8 se računa 40 m dalje), brid se tu podijeli (novi čvor = cilj GPS-a), mjesto istovara leži uz ulicu.
  Izvještaj: `Logs/ture-uvoz.txt` (koje su ušle, koje nisu i zašto). 2026-11-19: 55 od 85; van su Split / Solin (25),
  baze i odmorišta bez tura (8) i Caffe bar Verona na rivi Starog (Vukovarska: otok u grafu, nema puta).
- Imena: `Assets/Editor/imena-firmi.json` – jedna tablica stvarno → izmišljeno (Lidl → Cjenko, Interspar → Interšpajz, Plodine →
  Urodica, Tommy → Tonći, Ribola → Škarpina, JYSK → Jastuk namještaj, Roto dinamic → Burin, Stop Shop → Stani-Kupi centar, kafići i
  konobe izmišljena imena). Točka firme bez ključa dobije opće ime po vrsti i upozorenje u logu. Kuće: ulica (+ broj kad se ponavlja).
- Ture (`MagistralaGame.Tours.cs`): noge – utovar (E) → dostave po redu najbližeg susjeda (E, dizalica spusti svoj dio redova paleta;
  stajanje bez reda: gajbe rukom) → kod veleprodaje pića natrag u skladište s praznim gajbama (E; u cijeni, ne plaća se posebno).
  GPS vodi na sljedeću nogu. Jedna ukupna isplata na kraju: 10 € + 3 €/km cijele ture (skladište → utovar → dostave → natrag)
  + 5 € po stajanju iza prvog (`StopPay`, za Marka), rok po satu igre (1/6 min igre u sekundi): km pri ~40 km/h × 1,4 + 6 min po stajanju + 30 min (Deadline(km) iz weba je za
  ture od 20+ km davao više od dana). Duga tura pića (24 km) tako traje ~10 h igre – za Marka: manje stajanja ili brži sat.
- Ponuda dana (sjeme = dan): 2 ture pića (Burin u zoni Sućurca, 4–8 kafića / konoba: jedna oko veleprodaje, jedna oko nasumične rive),
  1 peciva (pekara → 3–6 kafića / dućana), 1 namještaj (Jastuk → 1–3 kuće), pa Zdravkove 3 stare jednostavne ture (pumpa, Marijina
  pumpa, dućan) kao kratke ture iz skladišta. Odvožena tura nestane s popisa za taj dan.
- Nisu igrive u Kaštelima: brašno, riba, voće (utovar u Splitu), bicikli, šuta (dostava u Splitu). Faturete (drva, ormar) su u
  podacima, ali se ne nude (Galeb i večer još nisu u Unityju).
- Proba (2026-11-19): `-job pice -jobauto -jobnear 0` (skok u mjesto svake noge) odvozi cijelu turu (utovar, 8 dostava, gajbe,
  isplata). `-jobnear 250` s autopilotom: utovar i jedna dostava stvarno odvožene; autopilot ne zna unazad i u uskim ulicama reže
  kutove u zgrade (tada nakon 20 s skoči u mjesto, zapisano u logu); kod Konobe Mandrač (riva Starog, pločica terena -8,-5)
  kamion je više puta propao kroz teren – za provjeru terena / ulice tamo.
- Vozilo: `maxVehicle = "mali"` (kombi ili mali kamion) na točkama rive; samo upisano i prikazano u opisu ture, ne provjerava se
  (klasa Atega još nije odlučena).

## Tipke i testni prekidači (igra)

- Tab mobitel (igra stoji, sat stoji): ↑ ↓ Enter, 1–9, Backspace natrag, Tab zatvori. Poziv: Y javi se, N odbij.
- U skladištu: 1–9 tura (ili mobitel → Poslovi). U mjestu istovara: E (ručna se povuče, dizalica). W / S prekida istovar.
  J otkaži turu. Enter zatvara karticu isplate. N kraj dana (poslije 17 h ili kad nema ture).
- Prekidači: `-job <id>` (ili vrsta ture, npr. `-job pice`: prva današnja tura te vrste), `-jobauto`, `-jobnear M` (0 = u mjestu istovara, unazad), `-jobabort s`, `-jobfresh`, `-savefile put`,
  `-clock HH:MM`, `-clockx N`, `-call [7]`, `-callpick k`, `-phone home|poruke|pozivi|kontakti|poslovi|thread`, `-phonedemo`,
  `-camorbit yaw,pitch`.
