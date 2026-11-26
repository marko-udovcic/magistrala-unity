# Ceste i riva

Asfalt ostaje jedan prolaz i jedan zajednički materijal: svjetliji zaglađeni tragovi kotača, tamnije pravokutne zakrpe, bitumenske pukotine, ulje, prljavi rub, pijesak, šahtovi i slivnici. UV0 su metri svijeta, UV1 bočni odmak/uzdužna udaljenost, UV2 pola širine/širina trake. Na parkiralištima bez profila ulje koristi rijetke ćelije svijeta. Postojeći `PaintMaterial` zadržava izblijedjele, istrošene oznake. Nema dodatnih projektora ni objekata za detalje asfalta.

`CesteStone` daje kamenu normalu, hrapavost, razlike tona po ploči, fuge i istrošene rubove. Generirani pločnici imaju rubnjake 15 cm s dvodijelnim zaobljenjem radijusa 2 cm; na krajevima i poznatim zebrama spuštaju se na 2 cm. `CesteCrossings` čita postojeće podatke paketa. Izvorna topologija/visina GLB pločnika i otoka ostaje očuvana; rampe se dodaju našim generiranim pločnicima. Nema zahvata u rad na križanjima 41/42.

GLB `island` dobiva kamenu ispunu na gornjim plohama i rubnjak na bokovima, uz postojeći podignuti kolizijski oblik. `Build signals` već gradi stupiće `bollard_keep_right` i koristi `SignAtlas.KeepRight()` (plavi krug, bijela strelica dolje desno); ponovno se koristi postojeća signalizacija i njezin atlas, bez dupliciranja znakova.

Riva dobiva ploče, morski rub s kamenom kapom širine 35 cm, mokar pojas i alge prema stvarnoj visini mora y=0. Bitve su svakih 22 m uz morske segmente, 65 cm unutar ruba; geometrija i kolizija spojene su s postojećim chunkovima. Bankina koristi šljunak na postojećoj traci širine 1,6 m koja pada 40 cm u teren: taj preklop skriva rez ruba. Teren i trava iza trake zadržavaju postojeću obradu buildera.

## Nakon spajanja

1. Pričekati uvoz `Assets/Textures/Ceste` pa pokrenuti **Magistrala/Ceste: osvježi materijale**.
2. **Magistrala/Build roads**.
3. **Magistrala/Build streets** — uključuje obnovu rive i prilagodbu terena. Ako se mijenja samo riva: **Magistrala/Riva: izgradi**.
4. **Magistrala/Build signals** — nakon postojećih preduvjeta `Igra: podaci` / `Build traffic`.
5. Provjeriti rampe, otoke, bitve i rubove u editoru; zatim spremiti scenu u koordinatorskom checkoutu.

Nije potrebno ponovno generirati ulazni `riva.json` ako se obuhvat nije mijenjao. Builderi pišu materijale/mreže u Generated tek kada ih koordinator pokrene. Nedostajuća tekstura daje upozorenje i postojeći materijal ili proceduralnu zamjenu. `manifest.json` / `sizeMeters` određuje veličinu teksture u metrima; postoji zadana veličina bez manifesta.

## Podešavanje i trošak

Asphalt: `_PatchAmount`, `_TrackDarken` (pozitivan broj sada posvjetljuje), `_TrackSmooth`, `_EdgeDust`, `_DetailSize`, `_OverlaySize`. Šahtovi su svakih 47 m, slivnici svakih 29 m (konstante u shaderu). Kamen: `_CellSize`, `_Slabs`, `_BumpScale`, `_Waterline`. Razmak bitvi i dimenzije kape su u `RivaBuilder.Edges`. Generirani materijali ponovno se postavljaju pri obnovi.

Postojeća kvaliteta **brzo** (`-kvaliteta 0`, najniža / nisko) isključuje dodatne teksturne maske preko globalnog `_CesteLowQuality`; K vraća detalje na srednje/oštro. Nema novog rada po kadru nad svijetom i ne uključuje se GPU Resident Drawer.

Asfalt +0 draw callova; rubnjaci/bankina koriste postojeće podmreže. Dodatno najviše jedna podmreža ispune po vidljivom modelu i jedna podmreža bitvi po vidljivom chunku rive, uz njihove sjene. Očekivani prirast u uobičajenom kadru: oko 2–20 draw callova; cilj ≤50 i ≤0,7 ms GPU na 1600×900 nije izmjeren. Potvrditi Frame Debuggerom/GPU profilerom na M2 i GTX 1060, osobito panoramu rive. C# provjeren zasebnim Roslynom iz instalacije Unityja; Unity/editor, shader compiler, izgradnja menija, kolizije i izgled nisu pokrenuti/provjereni.

Korištene nove teksture (baza + nastavci; `.jpg`/`.png`): `asfalt_{d,n,r}`, `asfalt_zakrpa_d`, `pukotine_d`, `ulje_d`, `sahta_d`, `resetka_d`, `rubnjak_{d,n,r}`, `otok_{d,n}`, `riva_ploce_{d,n,r}`, `riva_rub_{d,n,r}`, `alge_d`, `bankina_{d,n,r}`, `prasina_d`. Ostale isporučene normale/hrapavosti nisu potrebne za ovaj prolaz; postojeće Poly Haven teksture ostaju zamjena.

Sesija implementacije nije mogla zapisivati `.git/index.lock` (sandbox: `.git` samo za čitanje), pa commitovi nisu napravljeni. Nakon pregleda pokrenuti `sh Tools/ceste/commit-code.sh` iz korijena klona: tri logička commita s izričitim putanjama i traženim potpisom; skripta odbija rad na drugoj grani ili s već pripremljenim promjenama u indeksu. Ne dodaje slike, scenu, ProjectSettings ni Generated.
