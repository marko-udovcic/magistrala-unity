# Priroda: NatureManufacture Coast & Dunes u Kaštelima

Paket **Coast & Dunes Environment – Dynamic Nature** (NatureManufacture, Unity Asset Store, Standard EULA) — u igri smije, sirove
datoteke se ne dijele. Mapa `Assets/NatureManufacture Assets` je u `.gitignore`; bez nje svi kukovi ostavljaju našu gradnju kakva je bila.

## Uvoz (lokalno, jednom po klonu)
`unity assets import --include …` piše filtrirani paket kroz gzip u JS-u (~45 MB/min, sat i pol za 3,5 GB) — umjesto toga
`unpack.py` (izvan repoa; tar stream → `<pathname>` + `.meta`, isto što radi ImportPackage) samo potrebne mape, pa otvoriti editor:
- uzeto: Cliffs, Ground, Pine Trees, Foliage and Grass, Details, Decals, Bushes, Water Materials, R.A.M 3/Water, shaderi
  (Foliage / Object / Water / Particle), NatureManufacture Wind, Spline System — 3,6 GB na disku, import ~10 min;
- izostavljeno: Demo Scene, HD Support Pack, Oak Trees, Wooden Pales / Fence, VFX (treba VFX Graph), R.A.M 3 profili.
- URP 17.3 = verzija paketa: svi shaderi se prevode bez greške, 0 rozih materijala. Prvi build: shader mora (64 varijante po
  prolazu) ~13 min, poslije iz cachea.

## Kukovi u graditeljima (redoslijed gradnje ostaje isti)
- `NaturePass.Boxes` — pravokutnici (igra x/z) gdje vrijedi; od 21. 11. jedan = cijela Kaštela (jezgra pločica WorldBuilder
  TX0…TX1 × TZ0…TZ1, x −10240…0, z −7168…−4096); Split = dodatni pravokutnik.
- Build terrain: pločice koje dodiruju pravokutnik (sve jezgrene) dobiju 4 sloja paketa u našem redu (suho tlo s makijom `ground_sand_07`,
  smeđa→crvenica `cliff_clay_02`, naš vapnenac, kamenito tlo / šljunak `ground_sand_09`). Ortofoto tint (OrthoGround) računa
  prosjek boje sloja iz same teksture, pa radi i s njima (Grafika: ortofoto tlo poslije Build terrain).
- Build trees: borovi u pravokutniku → borovi paketa, mreža spljoštena prema alepskom boru (visina × 0,82, krošnja × 1,22),
  kora sivosmeđa; LOD2 blizu, križni billboard daleko (TreeRenderer). Dodatni borovi u skupinama na padini iznad 60 m — samo bez maske; s maskom (`Assets/Editor/vegetacija-maska.png`, `VegetationMask`) nakupine makije / bora / masline s ortofota i maslinici iz OSM-a, stabla s pločica prorijeđena (docs/vegetacija-d8.md).
  Razdjelni pojas D8 (`MedianHedge`): oleander (Tripo, 70 % roza / 30 % bijeli), tamnozeleni grm, suha trava — cijela D8 od
  čvora 41 (Štafilić) do 05 (Sućurac): rešetka 0,5 m u komadima od 200 m po x, u pojasu ±45 m oko crte D8 (središta modela
  dionica d01–d25 i križanja na D8, `D8Keys`); prekidi na križanjima / prijelazima sami (asfalt).
- Grafika: detalji terena: suha trava, žednjak, pelin, broćika i staračac (terenski prefabi paketa, njihov vjetar) u
  pravokutniku, korov uz podnožje zidova (maska tlocrta iz OrthoGround), naša zelena trava tamo na trećinu.
- Priroda: postavi (novi korak, poslije Rekviziti): vjetar paketa, litice paketa (siva/bijela kopija materijala) na nagibima ≥ 29°:
  razmak 5,5 m do 120 m visine, 14 m iznad, ništa iznad 420 m; ne na obali / rivi (teren je tamo spušten pod more) ni uz kuće
  u modelima.
- More: paket (`NatureSea`, `-more nm`) ostaje samo kao A/B; zadano je naše more (Grafika: more). Od 21. 11. (Marko: „more je
  statično”): svaki sloj valića su dvije kopije koje klize u križnim smjerovima (uzorak se mijenja, ne klizi kao plahta),
  ~0,8–1,4 m/s, lagano mreškanje (3 duga vala ~20 m, ~5,5 m/s), slabiji odsjaj sunca (2,2); dubina-normale ravne (SSAO više ne
  tamni vodu). Mandrači u zonama rive (Stari / Novi, Lukšić zapad): DEM ih ima kao kopno, pa je kroz rupu u bliskoj mreži
  sjalo tamno daleko more — sad su u mreži, boja ~3,5 m vode. Poslije promjene: `Grafika: more` (nije u redoslijedu gradnje).

## Zamke
- `_BC` teksture paketa nose alfu (visina / sjaj): kao terenski sloj bez mask mape URP je čita kao glatkoću → tlo je izgledalo
  mokro, kao more. NaturePass.Layers im gasi alfu (alphaSource None).
- Paket ima 4k teksture (499 MB u buildu): `Magistrala/Priroda: teksture (budžet)` (dio Priroda: postavi) — tlo ≤ 2048, ostalo ≤ 1024.
- d20 nije bio u World/Roads (paket mag-11-20): prije Build terrain treba i **Build roads**, inače Build streets više ne krpa
  rupu (d20 sad postoji u model-junctions.json) i D8 kod skladišta nestane.
- `NaturePass.Hash`: koordinate u long prije množenja (double → uint je davao isti broj za sve, pa su 2 od 4 bora bila prazna).
