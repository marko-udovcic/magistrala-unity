# Ceste i riva — drugi krug

Asfalt ostaje jedan zajednički materijal/prolaz. Vraćene su izvorne `asphalt_track` mape, linearni prosjek `.0112`, skala `2.2 m` i boja `(.31,.305,.297)` iz GraphicsPassa. Uklonjen je `_CesteBase`: svijetli `asfalt_d` više ne zamjenjuje bazu. Tragovi kotača posvjetljuju za 3 %, s tvrdim ograničenjem 5 %.

Zakrpe su relativno 9–14 % tamnije, s vlastitim zrnom iz `asfalt_zakrpa_d`; nema zamjene albeda gotovo crnom bojom. Jedna ćelija duljine 60 m po traci, vjerojatnost .9 (prosječno jedna na 67 m), uglavnom uzdužni iskopi širine .7 m i duljine 4–9 m; 20 % manji nepravilni popravci. Rub je razbijen šumom i omekšan preko .3 m. Brtvljenje pukotina sada je proceduralno u cestovnim metrima: širina 1.5 cm, uglavnom rijetki uzdužni spojevi, poneki poprečni spoj. Ne koristi široku mrežu iz `pukotine_d`. Pokrivenost je i bez prorjeđivanja manja od 1 %, neprozirnost .5, lokalno zatamnjenje najviše 11 %, mali porast sjaja. Ulje, rubno zatamnjenje, šahtovi i slivnici zadržani su.

Riva ponovno koristi izvorne sivo-bijele vapnenačke mape `PolyHaven/riva_ploce_{d,n}.jpg`, s ponavljanjem 3 m (otprilike tri velike ploče po teksturi, 0.6–1.2 m). Nove narančaste ploče više se ne koriste za albedo/normalu. Izvorne tamne fuge ostaju; dodatna mreža fuga je isključena, varijacija tona je ±6 %. Morska kapa širine 35 cm ima zaseban mokri pojas širine 20–35 cm, neovisno o visini rive. Na vertikalnom zidu mokrina i alge prate visinu mora y=0. Bitve i postojeća geometrija kape zadržane su.

Rubnjak kod Tuđmanove / Put Banovine je GLB `sidewalk` iz `21-kastel-stari-C.glb`, ne LaneProfile (on samo očitava asfalt). Vertikalne plohe imaju boju klase svijetlog betona, zbog čega su dobivale materijal podzida umjesto `rubnjak`. WorldBuilder sada sve vertikalne plohe GLB pločnika usmjerava na `Ceste_rubnjak`, računa geometrijske normale i dodaje dvodijelno zaobljenje od 2 cm. Isto zaobljenje koristi i GLB otok; kolizijski profil ostaje izvorni. Provjereno je da izvorni winding/normale kod Banovine nisu obrnuti. Materijal ima gotovo siv ton, podignutu difuznu bazu i fuge svakih 1 m. Generirani rubnjaci iz StreetsBuilder i StreetsSidewalks zadržavaju svoje zaobljenje i rampe. Vertikalna normala teksture koristi stabilnu bazu tangenti.

D8 se gradi iz GLB modela u Build roads; stara bankina postojala je samo uz generirane ulice. Build streets sada uz stvarni rub asfalta modela gradi šljunčanu traku od 1.6 m, s provjerom pločnika, drugih kolnika, prepreka i visine terena. D8 je dopušten, ostale modelske ceste dobivaju je izvan izgrađenih poteza. Visina šljunka prati teren (+2–2.5 cm), umjesto pada 40 cm ispod njega. Isti profil koristi bankina generiranih ulica; završnih .4 m dodatne suknje ulazi u teren i skriva rez i nakon prilagodbe visina. Maska uklanja vegetaciju samo s pojasa; dodatne kapsule ne mijenjaju visinu terena. Log Build streets ispisuje metre modelske bankine. Raspoznavanje postojećih GLB pločnika uključuje nove materijale `Ceste_rubnjak`/`Ceste_otok`, da se ne dodaje preklopljena geometrija.

Ispuna otoka zadržava kocke, uz zasićenost .65 prema sivom granitu. Stupići i K22 nisu mijenjani. Nema zahvata u križanja 41/42.

## Obnova

1. **Magistrala/Ceste: osvježi materijale**.
2. **Magistrala/Build roads** — obavezno zbog materijala i zaobljenja GLB rubnjaka.
3. **Magistrala/Build streets** — bankine, pločnici, maska vegetacije i riva.

Build streets već poziva obnovu rive. Samostalno **Magistrala/Riva: izgradi** treba samo pri zasebnoj promjeni rive. **Build signals** nije potreban za ovaj krug ako već postoje stupići i znakovi. Nije potreban novi `riva.json`. Nakon obnove spremiti scenu u koordinatorskom checkoutu.

## Podešavanje

Materijali se ponovno postavljaju pri obnovi:

| Parametar | Vrijednost / namjena |
|---|---|
| `_TrackDarken`, `_TrackSmooth` | .03, .06 |
| `_PatchAmount`, `_PatchFeather` | .9 po ćeliji 60 m, .3 m |
| `_DetailSize.x` | 2.1 m iz manifesta, zrno zakrpe |
| `_SealWidth`, `_SealOpacity` | .015 m, .5 |
| `_Saturation` | rubnjak .15, otok .65, ostalo 1 |
| `_AlbedoLift` | rubnjak .12 linearno, ostalo 0 |
| `_SlabVariation` | .06 |
| `_JointStrength` | riva ploče 0 (fuge iz teksture), ostalo 1 |
| `_CellSize` | rubnjak (1,.3), varijacija rive (1,1) m |
| `_Waterline`, `_VerticalScale` | morski rub 1, 2.5 |

Konstante: raspored zakrpa i pukotina u Asphalt.shader; bankina 1.6 m / srednja točka .8 m u GravelStrip; radijus GLB rubnjaka .02 m u CesteKerbs; kapa .35 m i bitve svakih 22 m u RivaBuilder.Edges. `CesteLowQuality` ostaje postojeći prekidač dodatnih maski na niskoj kvaliteti; nema rada nad svijetom po kadru i GPU Resident Drawer nije uključen.

## Trošak i provjera

Nema novog materijala/prolaza asfalta, renderera za zakrpe ni projektora. Zaobljenja se spajaju u postojeće podmreže rubnjaka; bankine u postojeći tip podmreže šljunka po chunku. Novi modelski pojasevi mogu aktivirati dosad praznu podmrežu šljunka u chunku; sve ostaje unutar istog budžeta prvog kruga (ukupno ≤50 dodatnih draw callova / ≤.7 ms pri 1600×900), bez dodatnog budžeta. Stvarni broj i GPU vrijeme treba izmjeriti u izgrađenoj igri.

Provjereno izvan Unityja: Roslyn kompilacija svih editor skripti (samo postojeći CS8321), `git diff --check`, geometrija stvarnog GLB pločnika kod Banovine: 1494 → 5478 trokuta, bez degeneriranih/obrnuto okrenutih trokuta, najveći pomak .02 m. Unity, shader compiler, meniji, kolizije, kadrovi i GPU profiler nisu pokrenuti. Usporedbu svjetline ±8 % treba potvrditi istim kamerama i ekspozicijom nakon obnove.

Stvarno korištene Ceste teksture: `asfalt_zakrpa_d`, `ulje_d`, `prasina_d`, `sahta_d`, `resetka_d`, `rubnjak_{d,n,r}`, `otok_{d,n}`, `riva_ploce_r`, `riva_rub_{d,n,r}`, `alge_d`, `bankina_{d,n,r}`. Izvorne PolyHaven mape: `asphalt_track_{diff,nor,rough}.jpg`, `asphalt_02_{diff,nor}.jpg`, `riva_ploce_{d,n}.jpg`. Ceste `riva_ploce_{d,n}` služe samo kao zamjena ako izvorne vapnenačke mape nedostaju. Nedostajuće opcijske teksture zadržavaju postojeći materijal uz upozorenje.

`.git` je u ovoj sesiji samo za čitanje (`index.lock: Operation not permitted`). `sh Tools/ceste/commit-code.sh` iz korijena klona radi tri commita drugog kruga s izričitim putanjama, hrvatskim porukama i traženim co-author potpisom. Ne dodaje scenu, ProjectSettings, Generated ni slike.
