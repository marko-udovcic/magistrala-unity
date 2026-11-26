# Ceste i riva — treći krug (samo bankina)

Asfalt ostaje jedan zajednički materijal/prolaz. Vraćene su izvorne `asphalt_track` mape, linearni prosjek `.0112`, skala `2.2 m` i boja `(.31,.305,.297)` iz GraphicsPassa. Uklonjen je `_CesteBase`: svijetli `asfalt_d` više ne zamjenjuje bazu. Tragovi kotača posvjetljuju za 3 %, s tvrdim ograničenjem 5 %.

Zakrpe su relativno 9–14 % tamnije, s vlastitim zrnom iz `asfalt_zakrpa_d`; nema zamjene albeda gotovo crnom bojom. Jedna ćelija duljine 60 m po traci, vjerojatnost .9 (prosječno jedna na 67 m), uglavnom uzdužni iskopi širine .7 m i duljine 4–9 m; 20 % manji nepravilni popravci. Rub je razbijen šumom i omekšan preko .3 m. Brtvljenje pukotina sada je proceduralno u cestovnim metrima: širina 1.5 cm, uglavnom rijetki uzdužni spojevi, poneki poprečni spoj. Ne koristi široku mrežu iz `pukotine_d`. Pokrivenost je i bez prorjeđivanja manja od 1 %, neprozirnost .5, lokalno zatamnjenje najviše 11 %, mali porast sjaja. Ulje, rubno zatamnjenje, šahtovi i slivnici zadržani su.

Riva ponovno koristi izvorne sivo-bijele vapnenačke mape `PolyHaven/riva_ploce_{d,n}.jpg`, s ponavljanjem 3 m (otprilike tri velike ploče po teksturi, 0.6–1.2 m). Nove narančaste ploče više se ne koriste za albedo/normalu. Izvorne tamne fuge ostaju; dodatna mreža fuga je isključena, varijacija tona je ±6 %. Morska kapa širine 35 cm ima zaseban mokri pojas širine 20–35 cm, neovisno o visini rive. Na vertikalnom zidu mokrina i alge prate visinu mora y=0. Bitve i postojeća geometrija kape zadržane su.

Rubnjak kod Tuđmanove / Put Banovine je GLB `sidewalk` iz `21-kastel-stari-C.glb`, ne LaneProfile (on samo očitava asfalt). Vertikalne plohe imaju boju klase svijetlog betona, zbog čega su dobivale materijal podzida umjesto `rubnjak`. WorldBuilder sada sve vertikalne plohe GLB pločnika usmjerava na `Ceste_rubnjak`, računa geometrijske normale i dodaje dvodijelno zaobljenje od 2 cm. Isto zaobljenje koristi i GLB otok; kolizijski profil ostaje izvorni. Provjereno je da izvorni winding/normale kod Banovine nisu obrnuti. Materijal ima gotovo siv ton, podignutu difuznu bazu i fuge svakih 1 m. Generirani rubnjaci iz StreetsBuilder i StreetsSidewalks zadržavaju svoje zaobljenje i rampe. Vertikalna normala teksture koristi stabilnu bazu tangenti.

D8 se gradi iz GLB modela u Build roads. U trećem krugu bankina je ograničena na vanjski rub prema otvorenom zemljištu. Cijela zraka do 24 m prema van provjerava drugi kolnik: razdjelnici i prostor između kolnika ne dobivaju bankinu. Nema iznimke kojom D8 zaobilazi provjeru izgrađenog područja. Pločnik, rubnjak, drugi asfalt, prepreka ili postojeća biljka prekidaju pojas. Granica GLB asfalta precizira se binarnim traženjem na izvornim trokutima umjesto na stepenastom rasteru; smjer se zaglađuje preko susjednih uzoraka.

Pojas je širok .8 m. Nema suknje ni vertikalnih ploha: unutarnji rub preklapa asfalt 3 cm, na njegovoj visini +2 mm; prema van se prilagođava terenu (najviše +2 cm). Prevelika razlika terena i ceste (>15 cm) znači da se pojas izostavlja, ne gradi se podignuta ploča. Vanjskih .4 m nestaje ditheringom u boji, dubini i normalama, bez prozirnog dodatnog prolaza. Novi shader `Magistrala/CesteShoulder` koristi isti materijal/podmrežu bankine. Potpuno desaturirano zrno `bankina_d` normalizira se izmjerenim linearnim prosjekom .373 i boji sivo-smeđom bojom `.335,.329,.317` sRGB; srednja difuzna luminancija je oko 16 % iznad izvorne baze asfalta. Prema van prelazi u zemljanu `.35,.335,.30`. Ostale plohe koje koriste postojeći slot šljunka, npr. središte kružnog toka, zadržavaju izvorni uzorak/sjaj; feather se označuje samo na geometriji bankine.

Uklonjen je `gravelCover` i njegov ulaz u zajedničku masku `cover.bytes`. Dodane bankine više ne brišu terenove detalje, `trees.bytes` ni biljke MedianHedge/Drvoredi/TreeRenderer. Pri gradnji samo čitaju postojeće položaje iz `trees.bytes` i preskaču pojas uz njih. Bankina ne mijenja pravila postavljanja biljaka i nema svoju vegetacijsku masku. Build streets prepisuje staru masku bez round-2 bankina; ranije izbrisane biljke treba jednokratno obnoviti s Build trees nakon toga.

Ispuna otoka zadržava kocke, uz zasićenost .65 prema sivom granitu. Stupići i K22 nisu mijenjani. Nema zahvata u križanja 41/42.

## Obnova trećeg kruga

1. Pričekati uvoz novog shadera pa **Magistrala/Build streets** — obnavlja bankine i prepisuje masku bez njihovog proširenja. Postojeći builder usput obnavlja pločnike i rivu istim postavkama drugog kruga.
2. Jednokratno **Magistrala/Build trees** — vraća živicu i druge biljke koje je maska drugog kruga već izbrisala iz `trees.bytes`. Ovaj postojeći builder uključuje MedianHedge i Drvoredi; ne treba mijenjati njihove skripte ili postavke.
3. Jednokratno **Magistrala/Grafika: detalji terena** — obnavlja travu i sitne detalje koje je stara proširena maska već uklonila iz detail mapa.

Ne trebaju Build roads, osvježavanje asfalta, Build signals ni novi `riva.json`. Spremiti scenu nakon obnove u koordinatorskom checkoutu. Ako se koristi prethodno ispečen streaming, uključiti obnovljene biljke i geometriju u uobičajenu obnovu cell podataka prije builda.

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

Bankina: `SHOULDER_WIDTH=.8` u StreetsSidewalks, `_ShoulderWidth=.8` i `_FeatherStart=.4` u CesteMaterials.Shoulder; `_GravelColor=(.335,.329,.317)`, `_DirtColor=(.35,.335,.30)` sRGB; `_GrainMean=.373`, `_GrainContrast=.25`, `_BumpScale=.4`. Usporedbu 10–20 % u konačnoj rasvjeti treba potvrditi istim kamerama. Granične provjere: drugi kolnik do 24 m, razlika visine ≤.15 m, zaštita položaja postojećih biljaka .35 m. Nema nove maske za vegetaciju.

Ostale konstante i površine drugog kruga ostaju nepromijenjene. `CesteLowQuality` ostaje postojeći prekidač dodatnih asfaltnih maski; nema novog rada nad svijetom po kadru i GPU Resident Drawer nije uključen.

## Trošak i provjera

Bankina ostaje u postojećoj podmreži/material slotu šljunka po chunku. Nema dodatnog renderera, materijalnog slota, prozirnog prolaza ili draw calla u odnosu na drugi krug; geometrija je uža i izostavljena na razdjelnicima. Dithering dodaje mali račun u postojeće prolaze. GPU vrijeme i konačni izgled nisu izmjereni; zadržan je ukupni budžet prvog kruga (≤50 dodatnih draw callova / ≤.7 ms pri 1600×900), bez dodatnog budžeta.

Provjereno izvan Unityja: Roslyn kompilacija editor skripti (samo postojeći CS8321), `git diff --check`, izvorne D8 GLB mreže d20/d21/d01/d02: na sva četiri uzorka odbijen rub prema drugom kolniku i prihvaćen otvoreni vanjski rub. Izvorni shaderi asfalta i kamena, WorldBuilder i CesteKerbs nisu mijenjani u ovom krugu. Unity, shader compiler, meniji i igra nisu pokrenuti.

Stvarno korištene Ceste teksture: `asfalt_zakrpa_d`, `ulje_d`, `prasina_d`, `sahta_d`, `resetka_d`, `rubnjak_{d,n,r}`, `otok_{d,n}`, `riva_ploce_r`, `riva_rub_{d,n,r}`, `alge_d`, `bankina_{d,n,r}`. Izvorne PolyHaven mape: `asphalt_track_{diff,nor,rough}.jpg`, `asphalt_02_{diff,nor}.jpg`, `riva_ploce_{d,n}.jpg`. Ceste `riva_ploce_{d,n}` služe samo kao zamjena ako izvorne vapnenačke mape nedostaju. Nedostajuće opcijske teksture zadržavaju postojeći materijal uz upozorenje.

`.git` je u ovoj sesiji samo za čitanje (`index.lock: Operation not permitted`). `sh Tools/ceste/commit-code.sh` iz korijena klona radi samo dva nova commita trećeg kruga s izričitim putanjama, hrvatskim porukama i traženim co-author potpisom. Ne ponavlja commitove drugog kruga i ne dodaje scenu, ProjectSettings, Generated ni slike.
