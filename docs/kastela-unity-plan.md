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

## Tipke i testni prekidači (igra)

- Tab mobitel (igra stoji, sat stoji): ↑ ↓ Enter, 1–9, Backspace natrag, Tab zatvori. Poziv: Y javi se, N odbij.
- U skladištu: 1 / 2 / 3 tura (ili mobitel → Poslovi). U mjestu istovara: E (ručna se povuče, dizalica). W / S prekida istovar.
  J otkaži turu. Enter zatvara karticu isplate. N kraj dana (poslije 17 h ili kad nema ture).
- Prekidači: `-job <id>`, `-jobauto`, `-jobnear M` (0 = u mjestu istovara, unazad), `-jobabort s`, `-jobfresh`, `-savefile put`,
  `-clock HH:MM`, `-clockx N`, `-call [7]`, `-callpick k`, `-phone home|poruke|pozivi|kontakti|poslovi|thread`, `-phonedemo`,
  `-camorbit yaw,pitch`.
