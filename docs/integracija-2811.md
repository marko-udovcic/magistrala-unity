# Integracija 28. 11. — obnova Magistrale (Unity 6 URP)

Provjereno na grani `integracija-2811`, HEAD `7175cee`: baza `2a1b3d3` i mergeovi `ceste2711`, `bus`, `kombi`, `ukrajina`, `kasteli`, `benzinske`.
Ovo je uputa za kasnije pokretanje u Unityju. Pri ovoj provjeri Unity nije pokrenut, scene i ProjectSettings nisu uređivani, commit nije napravljen.

## Priprema i uvozi

Otvoriti glavnu scenu tek pri ručnoj obnovi. Sljedeći koraci stvaraju assete i mijenjaju scenu; neki meniji scenu odmah spremaju.
BalkanDriver mora biti dostupan na putanji koju definira `W.BD` (`/Users/macair/Documents/BalkanDriver`).
`PaketData` koristi **tools/world/krizanja/paket-2711/**, uključujući `riva-maska.json`, `drvoredi.json` i `autobusna_stajalista.json`.

Ako se obnavlja i sadržaj generiranih modela, prije geometrije izvršiti:

1. `Magistrala/Rekviziti: uvezi Tripo modele` — i PolyHaven i RivaOprema prolaze kroz isti importer; klima mora biti uvezena prije gradnje zgrada.
2. `Magistrala/Auti: uvezi Tripo modele` — osnovni modeli za promet/parkiranje.
3. `Magistrala/Auti: uvezi kandidata` — uvozi **sve** konfiguracije iz `Assets/Editor/auti-kandidati.json`, uključujući `bus37`; meni nema odabir samo autobusa. Izlaz je `Assets/Generated/Vehicles/Kandidati`. Ako postoji `Traffic`, meni odmah ponovno gradi promet; završni promet zato se gradi kasnije.
4. `Magistrala/Zgrade: paket – ispeci modele`, zatim `Magistrala/Zgrade: paket – ispeci detalje` — samo kad nedostaju generirani paket asseti ili se mijenja njihov izvor. Za ponovno pečenje potrebni su izvorni modeli/teksture koje koristi kod. Postojeći `zgrade-paket.json` zadržati; `Magistrala/Zgrade: paket – uparivanje (kandidati.json)` služi promjeni uparivanja i nije redovan korak obnove.
5. `Magistrala/Benzinske: uvezi kit` i `Magistrala/Autobusne stanice: uvezi staklenu nadstrešnicu` mogu se izvršiti unaprijed; njihovi meniji za postavljanje već pozivaju odgovarajući uvoz.

## Točan redoslijed geometrije i završnih prolaza

| Red | Unity meni | Ovisnost / učinak potvrđen u kodu |
| --- | --- | --- |
| 1 | `Magistrala/Build roads` | Modeli cesta i njihovi kolideri, podloga za ostale prolaze (`WorldBuilder.Roads`). |
| 2 | `Magistrala/Build terrain` | Teren prema modelima; ovo mora biti prije naknadnog usijecanja ulica i rive (`WorldBuilder.Terrain`). |
| 3 | `Magistrala/Build sea` | More nakon osnovnog terena (`WorldBuilder.Sea`). |
| 4 | `Magistrala/Build streets` | Gradi mrežu, masku pokrivenosti, usijeca teren i na kraju već poziva `RivaBuilder.Build()`. |
| 5 | `Magistrala/Igra: podaci (stajališta, graf cesta)` | Prvi prolaz podataka: aktualni graf i dostavne niše trebaju vegetaciji, benzinskim postajama, rekvizitima, stanicama i detaljima. Meni usput peče kartu; završno pečenje je korak 25. |
| 6 | `Magistrala/Zgrade: paket (hale, škole)` | **Odabrani meni za puni rebuild zgrada.** Poziva `BuildingsFromTiles.Build`: najprije modelske i tile zgrade, zatim `PaketZgrade.Place`. Svi skipovi vrijede zajedno. `Magistrala/Build tile buildings` je ekvivalentna alternativa; ne treba pokretati oba. |
| 7 | `Magistrala/Kašteli: postavi` | Zamjene iz `Assets/Editor/kasteli-postava.json`; postaviti prije drveća. |
| 8 | `Magistrala/Benzinske: postavi` | Kit i vlastite zamjene; ponovno gradi modelski dio `World/Buildings/package` bez preuzetih objekata i uklanja stare pumpe na lokacijama. |
| 9 | `Magistrala/Trgovine: postavi` | Trgovine na svojim tlocrtima; zajedno s benzinskim postajama imaju prednost pred paketom hala. |
| 10 | `Magistrala/Build trees` | Nakon svih zgrada: prototipovi, `trees.bytes`, Mapillary, čišćenje, drvoredi, palme/tamarisi, oleander, završno čišćenje i vrtne živice. |
| 11 | `Magistrala/Vegetacija: drvoredi` | Samostalan prolaz izmjerenih drvoreda (`PaketData.DRVOREDI`). Već je uključen u korak 10; ovdje je izričit redoslijed ako se prolazi izvršavaju pojedinačno. Čuva prethodne zapise palmi i oleandra. |
| 12 | `Magistrala/Stabla: palme na rivama` | `PalmRows`: prijedlozi iz `riva-extras/oprema.json`; treba vegetaciju i gotove zgrade/podlogu. Već uključeno u korak 10. |
| 13 | `Magistrala/Stabla: oleander` | `OleanderRows`: `Assets/Editor/oleander.json`; treba prototipove iz `ExtraTrees` i gotove zgrade/podlogu. Već uključeno u korak 10. |
| 14 | `Magistrala/Vegetacija: očisti` | Završni prolaz poslije svih pojedinačnih sadilica. Precizni asfalt ima prednost pred zaštitom drvoreda; neasfaltna šetnica, zeleni razdjelni pojas i otok čuvaju zaštićene zapise. |
| 15 | `Magistrala/Rekviziti: postavi` | Čita podatke igre i gotove zgrade; stvara `World/Props`, tlo sportskih/parkirnih površina, rasvjetu, parkirana vozila i brodice. Uvozi iz pripreme moraju već postojati. |
| 16 | `Magistrala/Autobusne stanice: postavi` | Mapillary popis + BUS točke iz paketa 2711 i transformacija `paket-approaches.json`; koristi niše igre i gotove kolidere. Stajališta bez BUS veze ostaju na starom položaju uz upozorenje. |
| 17 | `Magistrala/Detalji: postavi` | Nakon rekvizita i stanica radi provjere prepreka; zaseban korijen `World/Detalji`. |
| 18 | `Magistrala/Riva: oprema i brodice` | Najprije lukobrani i korekcija terena, zatim oprema, na kraju `BoatsBuilder.Place`. Dolazi poslije općih rekvizita: zamjenjuje njihove klupe rive i ponovno postavlja brodice uz gotove lukobrane. |
| 19 | `Magistrala/Build traffic` | Nakon uvoza kandidata, cesta i ulica: stvara prometne rute i podatke, uključujući autobus 37. |
| 20 | `Magistrala/Build signals` | Zahtijeva `TrafficSystem` i njegove podatke iz koraka 19; gradi semafore, znakove i stop-linije. |
| 21 | `Magistrala/Spawn truck` | Samo ako u sceni nema kamiona s `Autopilot` komponentom; preduvjet sljedećeg menija. |
| 22 | `Magistrala/Autopilot: osvježi stazu`, zatim `Magistrala/Igra: podaci (stajališta, graf cesta)` | Staza prema aktualnim cestama; završna obnova podataka igre. Drugi meni ponovno usput peče kartu. |
| 23 | `Magistrala/Trees: bake impostors` | Tek nakon konačnog skupa prototipova vegetacije; peče i dodatne prototipove iz `NaturePass`/`ExtraTrees`. |
| 24 | `Magistrala/Ceste: osvježi materijale` | Završno osvježenje asfalta; za promjene geometrije i kamena potrebni su raniji `Build roads` / `Build streets`. |
| 25 | `Magistrala/Karta: ispeci` | Završna karta iz aktualnog `MagistralaGame.asset`, tlocrta i vegetacije. Spremiti konačnu scenu. |

Koraci 11–14 nakon punog `Build trees` ponavljaju ugrađene prolaze; nisu potrebni ako između njih nije bilo izmjena, ali njihov međusobni red ostaje isti.
`Magistrala/Build world` nije zamjena za ovu listu: njegov sadašnji niz je `Roads → Terrain → Trees → TileBuildings → Sea`, bez ulica i novih postavljača, s drvećem prije zgrada.
`Magistrala/Brodice: postavi` također nije dodatni obavezni korak: obuhvaćen je završnim menijem rive.

## Riva: ulazni podaci, zone i zajedničke putanje

`Magistrala/Riva: ulazni podaci` **samo izvozi** `Logs/riva-input.json`; ne gradi rivu i nije obavezan pri obnovi postojećih ulaza.
Kad se mijenja izvorna riva s ortofota, izvozi se nakon aktualnih ulica i podataka igre (i aktualne vegetacije ako se žele njezine pozicije), zatim se iz korijena repozitorija pokreće `python3 -I Tools/riva/riva_build.py` te ponovno `Magistrala/Build streets`.
`Magistrala/Riva: izgradi` alternativa je za obnovu samo rive kad se ulice nisu mijenjale; također traži njihove gotove kolidere i usječeni teren.

- `RivaBuilder`: `Assets/Editor/riva.json`; pri učitavanju `Tools/riva/cut_package.py` oduzima zone `paket-2711/riva-maska.json` i dodatak `Tools/riva/riva-maska-istok.json`. Koristi Shapely Python okruženje iz `BalkanDriver/tools/world/dgu/cache/venv/bin/python`. Izvorni ortofoto JSON ostaje zaseban.
- `RivaOprema`: `BalkanDriver/tools/world/riva-extras/breakwaters.json`, `oprema.json`, `boats.json`; modeli opreme su `Assets/Models/Props/RivaOprema/`.
- `PalmRows.OPREMA`: isti **BalkanDriver/tools/world/riva-extras/oprema.json**, polje `treeRowSuggestion` (na disku provjereno 44 retka). To nije JSON konfiguracije uvoza modela `riva-oprema`. Prepreke i kafićke zone čita iz tog istog ulaza.
- `Drvoredi`: `paket-2711/drvoredi.json`; stanice: `paket-2711/autobusna_stajalista.json`. Nema povratka na stari paket.

Palme i oleander dolaze prije postavljanja namještaja, ali palme već čitaju njegove planirane prepreke iz `oprema.json`. Nakon promjene lukobrana ili namještaja na mjestima drveća ponoviti korake 12–14 nad gotovim koliderima; nakon promjene cesta/terena ponoviti cijeli niz od odgovarajućeg ranijeg koraka.

## Semantička pravila i provjera mergea

`ModelJ.Surface()` sadrži asfalt **i otoke**. Zato nije dovoljan uvjet za brisanje svih biljaka: `VegetationClean`, `Drvoredi` i `PalmRows` koriste precizne asfaltne trokute `LaneProfile.OnAsphalt`, a za ulice `CoveredStreet` bez maske pješačkih staza. Čišćenje provjerava kolnik prije iznimaka za rivu, Mapillary, palme, oleander i izmjerene drvorede. Ostala, nezaštićena vegetacija zadržava šire sigurnosne razmake.

Zamjene zgrada imaju prioritet **kašteli > benzinske/trgovine > paket hala**. Oba menija gradnje zgrada obnavljaju cache zamjena i provode sva tri skupa skipova. Paket koristi isti filtar u `Replaced` i `Place`: odbačena višestruka hala ne uklanja ostale originalne tlocrte koje je namjeravala preuzeti. Kašteli se provjeravaju preko ID-ja, izvornog tile tlocrta i tlocrta postavljenog modela; postavljači benzinskih postaja/trgovina također poštuju kaštele.

Pregled 68 literalnih `MenuItem` putanja nije našao duplikate. Dva menija za zgrade jesu namjerne alternative istog rebuilda. Opći rekviziti i meni rive oboje postavljaju brodice; zato riva ide zadnja. Benzinske postaje i opći rekviziti imaju zaštitu od starih paketnih pumpi (`Benzinske.SkipPackage`).

U diffovima šest grana prema zajedničkoj bazi nema asset putanje koju dvije grane mijenjaju zajedno unutar `Assets/Resources/Vehicles`, `Assets/Models/Plants`, `Assets/Models/Props` ili `Assets/Generated/Props`. `PropsBuilder.Stanice.cs` jest zajednički mijenjan C# izvor (ceste + bus); zadržani su paket 2711, BUS poravnanje i fallback za stanice bez BUS veze. Konfiguracije prop importera iz Tripo/PolyHaven/RivaOprema nemaju sudare izlaznih imena. Kandidati, Tripo vozila i Galeb koriste različite izlazne mape; novi kombi ima zasebni `Resources/Vehicles/GalebL3H2`.

## Shell compile, bez pokretanja Unityja

Korišteni su Unityjev `Resources/Scripting/NetCoreRuntime/dotnet` i `Resources/Scripting/DotNetSdkRoslyn/csc.dll` iz instalacije **6000.3.28f1**, iste reference/defineovi/opcije kao u ranijim `/private/tmp/2711-runtime.rsp` i `2711-editor.rsp`. Popisi izvora obnovljeni su iz svih trenutačnih `Assets/**/*.cs`: **88 runtime + 95 editor** datoteka. Editor je kompiliran prema upravo izgrađenom runtime DLL-u, ne prema starom projektnom DLL-u. Izlazi, response datoteke i logovi isključivo su u `/private/tmp`.

```sh
UNITY_SCRIPTING=/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/Resources/Scripting
"$UNITY_SCRIPTING/NetCoreRuntime/dotnet" "$UNITY_SCRIPTING/DotNetSdkRoslyn/csc.dll" @/private/tmp/integracija-2811-runtime.rsp
"$UNITY_SCRIPTING/NetCoreRuntime/dotnet" "$UNITY_SCRIPTING/DotNetSdkRoslyn/csc.dll" @/private/tmp/integracija-2811-editor.rsp
```

Rezultat: **0 compile grešaka** prije semantičkih izmjena i nakon njih. Nisu nađene duple definicije/članovi ni nedostajući usingi. Preostala upozorenja: `MatePhone.Blicaj.cs` CS0414 (`blicajScroll`), `PaketZgrade.cs` CS8321 (`H`), `Trgovine.cs` CS0219 (`unitBay`), `StreetsBuilder.cs` CS8321 (`Junction`). Logovi: `/private/tmp/integracija-2811-runtime.log` i `/private/tmp/integracija-2811-editor.log`.

Ovo provjerava C# i ovisnosti iz koda; ne potvrđuje uvoz asseta, izvođenje menija, shader kompilaciju ni vizualni rezultat u sceni.
