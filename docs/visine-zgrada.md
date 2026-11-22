# Visine zgrada (katovi) – procjena s ortofota i ispravci u igri

## Ukratko za Marka

- **LiDAR-a nema**, pa je broj katova procijenjen iz **sjena** na DGU ortofotu (LiDAR let 2022./23., mozaik 0,5 m/px).
  To je gruba procjena: u igru je ušla samo ondje gdje se dvije neovisne metode slažu (**713 zgrada**, 6 %);
  sve ostalo ima visinu kao prije.
- Najsigurniji put do pravih visina je **tvoj ispravak u igri (F7)** – vidi dolje. Ispravak ima prednost pred svime.

## Ispravljanje katova u igri (F7 „Katovi”)

1. U igri pritisni **F7**. Na sredini ekrana je križić; zgrada pod njim dobije obris (narančasto = procjena / zadano,
   zeleno = tvoj spremljeni ispravak, žuto = promijenjeno, još nije spremljeno) s crticama po katovima.
   Gore piše id zgrade (npr. `-8_-6:583`), broj katova (s prizemljem) i odakle je (procjena iz sjene / OSM / zadano).
   Najlakše je s dronom (**F**), ali radi i iz kamione kamere.
2. **PageUp / PageDown** = jedan kat više / manje (obris odmah pokaže novu visinu).
3. **Enter** = spremi sve promjene u `katovi-ispravci.json`
   (Mac: `~/Library/Application Support/Magistrala/<ime igre>/katovi-ispravci.json`; poruka gore pokaže točnu putanju).
   **Delete** = vrati zgradu na procjenu. **F7** = kraj.
4. Zgrade se u igri **ne mijenjaju odmah** (sve su spojene u velike mreže); promjena je vidljiva nakon ponovne gradnje:
   u editoru **Magistrala → Zgrade: uvezi ispravke katova** (spoji datoteku iz igre u `Assets/Editor/katovi-ispravci.json`,
   koja ide u git), zatim **Build tile buildings → Rekviziti: postavi → spremi scenu** i build.

Ispravke možeš upisati i ručno u `Assets/Editor/katovi-ispravci.json` (`"id": katovi`).

## Redoslijed (tko odlučuje o visini)

1. tvoj ispravak (`katovi-ispravci.json`)
2. OSM `building:levels` / `height` (12 zgrada u Kaštelima – ljudski upisano, ostaje)
3. procjena iz sjene (`Assets/Editor/visine-zgrada.json`), samo pouzdanost ≥ 0,6
4. visina iz pločice kao prije (stara igra 7 / 10 m ili zadano)

Katovi → visina strehe: 3 m po katu − 0,4 m (igra crta etaže od 3 m). `BuildingFloors.UseMeasured = false` vraća staro
(A/B), ispravci i dalje vrijede. Igrin pregled `Assets/Generated/Game/Resources/katovi-index.json` (obrisi, visine, izvor) radi Build tile buildings.

## Metoda (Tools/visine)

- **Sunce**: vrijeme snimanja nije u WMS metapodacima. Smjer sjena izmjeren je iz krošanja (korelacija vegetacija ↔ tamno,
  86 kvadranata od 512 m): sjena pada prema **~56° (SI)**, od 52° na zapadu do 65° na istoku (azimut sunca ~236°).
  Visina sunca iz geometrije: za ljetni let (deklinacija 20–23°) to je 56–60°; uzeto **58° (tan 1,6)**, što daje medijan
  izmjerenih kuća 7,7 m – u skladu s očekivanih 7–9 m za Kaštela. Kalibracija na stvarnim visinama nije moguća (premalo OSM-a).
- **Duljina sjene**, dvije metode po zgradi: (a) stupci u smjeru sjene od tlocrta, prosječni profil tame, prijelaz
  sjena → tlo; (b) profili okomito na rubove okrenute od sunca, najstrmiji porast svjetline nakon jezgre sjene. Prepreke
  (vegetacija, druge zgrade, more) režu profil. Pomaknuti krov (ortofoto nije pravi ortofoto) maskiran je pomakom iz audita.
- **Katovi**: visina = sjena × 1,6; katovi = (visina − krov 1,0 / parapet 0,7 − 0,4) / 3. Granice: kuća 1–4 (1 i 4 samo uz
  jaku potvrdu, inače 2 / 3), mala (< 40 m²) 1–2, šupe / staklenici 1, hale 1–2, blokovi 2–8; crkve se ne diraju.
- **Pouzdanost**: 0,85 kad se obje metode slažu unutar 0,6 m sjene, 0,65 unutar 1,2 m, inače 0,35; jedna metoda 0,4.

## Koliko vrijedi (iskreno)

- Ortofoto je 0,5 m/px, s izmaglicom; sjene su blijede (≈ 0,7 svjetline tla) i kratke (sunce visoko: kat ≈ 1,9 m sjene),
  a dvorišni zidovi, auti i tamni asfalt ih oponašaju.
- Ponovljivost: dvije polovice iste zgrade r ≈ 0,4; metoda (a) prema (b) r = 0,55 (medijan razlike 0,6 m sjene).
  Relativni pomak krova (druga, neovisna veličina) slaže se sa sjenom samo slabo (Spearman 0,21), pa se **ne koristi**.
- Prema OSM katovima (jedini stvarni podaci u Kaštelima, n = 9 s mjerenjem): MAE **0,67 kata**; konstanta „2 kata” daje
  0,73 – dakle procjena je na toj maloj provjeri jedva bolja od pogađanja. Zato je u igri samo 6 % zgrada, i zato je F7 alat.
- Po veličini zgrade procjena ima smisla u prosjeku: male kuće (< 80 m²) medijan 6 m, 80–200 m² 8,3 m, 200–500 m² 9 m.

## Brojke (Kaštela, pločice −10…−1 × −7…−5, 12 068 tlocrta)

| katova | prije | poslije |
|---:|---:|---:|
| 1 | 4 | 231 |
| 2 | 6 029 | 5 937 |
| 3 | 5 968 | 5 795 |
| 4+ | 67 | 105 |

(prije = puni katovi iz visine pločice; 7 m = 2, 10 m = 3). Mjerenje postoji za 4 203 zgrade, primijenjeno 713 (13 ih leži pod modelima paketa).

## Što ostaje

1. DGU LiDAR oblak točaka / nDSM (isti let) – jedini pravi izvor visina; s njim cijela ova procjena otpada.
2. Tvoji ispravci (F7) – posebno upadljive zgrade uz D8 i u jezgrama.
3. Izvorni ortofoto 0,25 m (nije u predmemoriji) bi udvostručio razlučivost sjena.
