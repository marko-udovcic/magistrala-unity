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
| Isporuka | rampa / mjesto za istovar, E, bonus unazad | **novo:** provjera mjesta kao engine.ts inBay, E, istovar 4 s |
| Novčanici | gotovina + račun obrta (200 €) | **novo:** oba, plaća firme u gotovinu, pamti se (PlayerPrefs) |
| Rampa / dizalica (tail lift) | firmUnload.ts, animacija | nema (samo odbrojavanje) |
| Bonusi / kazne | unazad +10 %, noć +20 %, kašnjenje, oštećenje | nema |
| Dnevni sat i dan | 6:00 poziv, 0,5 min igre / s, mrak, spavanje | nema (samo G zalazak) |
| Mobitel, pozivi, poruke | MatePhone, calls.ts (Zdravko, Vesna…) | nema |
| Priča: prolog, 1. poglavlje | chapter1.json, prologue.ts, bilježnica, banka | nema |
| Matin glas | mateLines.json (69 replika), titlovi | nema |
| Radio | stanice, radio uživo (proxy) | nema |
| Galeb (TN kombi, CC-BY) | galeb.glb, faturete | nema |
| Faturete (Jure, drva) | 17–22 h, dogovorena cijena | nema |
| Pumpa, kafana, policija Stipe, banka | stops/*.tsx | nema (samo stajališta kao točke) |
| Radovi na cesti (zapornice) | Roadworks2, road-closures.json | GPS ih zaobilazi; zapornica se ne crta |
| Vraćanje na cestu | Backspace na cestu | R uspravi na mjestu |
| Karta (M) | velika karta | nema |
| Spremanje igre | server + lokalno | samo novčanik |

## Najveće rupe (vidljivo u probnoj vožnji)

- **Ulice grafa i dvorišta stajališta ne postoje u Unityju.** Unity ima samo ceste iz Markovih modela (D8 i krakovi).
  Bardejovska ulica, prilaz skladištu (ogranak `spur`) i dvorište skladišta su trava; kamion do D8 vozi po livadi.
  Web crta naše ulice (vrpce grafa) i tlo dvorišta (DeliverySite). Ovo je prvo za Kaštela (posao helpera za ceste ili sljedeći korak).
- Skladište u Lukšiću nema svoju zgradu ni rampu, samo opću zgradu iz pločica.
- Autopilot (test) ne gleda promet: na križanju s D8 stane u autobus. GPS je dobar, testna vožnja kreće 220 m prije cilja (`-jobnear`).

## Redoslijed (mali koraci)

0. **Ulice grafa i dvorišta stajališta** u Unityju (vrpce iz graph.json + overrides, tlo dvorišta, prilazi).
1. **Osnovna petlja** (gotovo ovdje): skladište → tura → GPS → mjesto istovara → plaća.
2. Istovar dizalicom: rampa (`tail_lift`) i rolo vrata (`door_roll`) iz modela, red paleta po red (kao web).
3. Bonus za parkiranje unazad, noćna vožnja; kartica isplate (stavke kao economy.ts).
4. Dnevni sat 6:00–22:00 (isti tempo kao web), Zdravkov poziv u 6:00, skladište radi danju.
5. Mobitel: poruke i pozivi (tekst), prvo Zdravko i Vesna (kruh i mliko na Marijinoj pumpi).
6. Spremanje igre (novac, sat, dan, završene ture) u datoteku.
7. Vraćanje na najbližu cestu (graf) umjesto samo uspravljanja; zapornice radova na cesti.
8. Galeb i prva fatureta (Jure, dvor Bašića → Sućurac), 8 € + 4 €/km, najmanje 45 €.
9. Prolog lite i 1. poglavlje (kartice + Matin glas), bilježnica, banka.
10. Radio, pumpa (gorivo), kafana, Stipe.

## Kako se gradi (Unity)

- Podaci: izbornik **Magistrala/Igra: podaci (stajališta, graf cesta)** → `Assets/Generated/Game/Resources/MagistralaGame.asset`
  (iz stops.json, graph.json + junction-overrides*.json, road-closures.json, distances.json). Ako asset nedostaje, napravi se
  sam prije builda igrača.
- Igra: `Assets/Scripts/Game/*` se stvara pri učitavanju scene (scena se ne mijenja).
