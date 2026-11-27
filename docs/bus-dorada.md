# Autobus 37 – dorada u grani `bus`

## Lokalni izvori i pripremljeni LOD-ovi

Pet datoteka kopirano je iz `/Users/macair/Documents/BalkanDriver/assets-src/obrada-2711/` u `Assets/Models/Vehicles/Traffic/bus37/`. Izvornici nisu mijenjani. Konfiguracija kandidata `bus37` sada koristi isključivo projektne putanje za GLB-ove i oba smjera displeja. Dodane su stabilne Unity `.meta` datoteke; odredište nije gitignorirano.

`KandidatImporter.KCar` dobio je opcionalna polja `lod1Glb`, `lod2Glb` i `namedWheels`. Samo `bus37` uključuje novu putanju. Ostalih 28 konfiguracija ostalo je jednako prethodnoj verziji. Bez tih polja importer nastavlja koristiti postojeći simplifier.

| Razina | Izvor | Trokuti u GLB-u |
|---|---|---:|
| LOD0 | `autobus-37.glb` | 121.428 |
| LOD1 | `autobus-37_lod1.glb` | 15.482 |
| LOD2 | `autobus-37_lod2.glb` | 3.523 |

Pripremljene razine prolaze bez dodatnog pojednostavljivanja karoserije, interijera ili kotača. Za njih se preskače i automatsko rezanje poklopaca svjetala. LOD1 zadržava svoj pripremljeni interijer i staklo; LOD2 koristi geometriju iz svojeg GLB-a. Materijali se mapiraju po imenima u stabilne LOD0 podmreže; odsutni materijali daju prazne podmreže. Nova/nepoznata grupa izaziva grešku umjesto pogrešnog materijala. Oba niža LOD-a koriste istu normalizaciju i iste osovinske pivote kao LOD0, pa se ne recentriraju prema decimiranim gumama. Provjereno je da su pivoti i ugrađene base-color teksture zajedničkih materijala jednaki kroz sve tri ulazne razine.

Zadržan je postojeći prometni far proxy i njegov algoritam generiranja. Njegov konačni broj trokuta nije ponovno izmjeren jer Unity nije pokretan.

Prometni sustav koristi udaljenosti, a ne `LODGroup` pragove veličine na ekranu. Zadržani su pragovi kandidata: LOD0 ispod 25 m, LOD1 od 25 do 60 m, LOD2 od 60 m do postojećeg `TrafficSystem.lod1Dist` (zadano 70 m), zatim far proxy. Za referentnih 12 m i vertikalni FOV 60° to približno odgovara relativnim veličinama 0,42 i 0,17 za prva dva prijelaza; stvarna projekcija ovisi o kutu autobusa i kameri. Time autobus koristi isti mehanizam odabira LOD-a kao ostali prometni automobili.

## Naplatci i vrtnja

Važna razlika u odnosu na opis prijavljenog stanja: dostupni izvornici već imaju primitive materijala `Rim` u mrežama čvorova `wheel_FL/FR/RL/RR`. LOD0 sadrži ukupno 11.331 trokut tog materijala. Problem u importeru jest što je zanemarivao hijerarhiju i odlučivao o vrtnji po povezanim komadima geometrije unutar kutije gume. Na LOD2 gume imaju samo 1–2 trokuta po kotaču, pa takva heuristika nije pouzdana.

U projektnim kopijama svaka mreža gume/naplatka premještena je u eksplicitan podčvor `wheel_*_rim_hub` najbližeg kotača. Mreže ostaju cijele, bez dijeljenja ili promjene buffera. Lokalni TRS podčvora računa se kao `inverse(worldWheel) * worldMesh`; za ove izvornike rezultat je identitet. Postojeći čvorovi kotača i njihovi pivoti ostaju isti.

Uz `namedWheels` importer pamti imenovanog pretka i njegov pivot, uz glTF → Unity zrcaljenje X osi. Sve primitive pod kotačem dodjeljuje istom rotirajućem prometnom dijelu. Preostale unutarnje stražnje gume s materijalom `Tyre`, koje se nalaze u mreži karoserije, pridružuje najbližoj osovini unutar ograničenog radijusa. Ostali kandidati koriste dosadašnju geometrijsku heuristiku.

Provjera naplataka u podstablima, broj trokuta:

| LOD | FL | FR | RL | RR |
|---|---:|---:|---:|---:|
| 0 | 3.218 | 3.231 | 2.451 | 2.431 |
| 1 | 378 | 382 | 362 | 351 |
| 2 | 226 | 251 | 284 | 268 |

## Staklena nadstrešnica

U `StanicaStaklo.cs` staklo sada ima RGB `(0.12, 0.17, 0.17)`, uz dosadašnji alpha `0.16`. Smoothness je smanjen s `0.94` na `0.35`, metallic ostaje 0. Isključeni su environment reflections i specular highlights kroz URP Lit svojstva i odgovarajuće keyworde, provjerene u lokalnom URP shaderu. Time se uklanja široki odsjaj neba koji je davao mliječan izgled. Zadržani su prozirni premultiplied blending, obostrano crtanje, isključen depth write i izostanak refrakcije.

## Provedene provjere i ograničenja

Ponovljiva priprema i neovisna provjera pohranjenih kopija koriste isključivo Python standardnu biblioteku:

```sh
python3 -B Tools/vehicles/prepare_bus37.py /Users/macair/Documents/BalkanDriver/assets-src/obrada-2711
python3 -B Tools/vehicles/prepare_bus37.py /Users/macair/Documents/BalkanDriver/assets-src/obrada-2711 --verify-only
```

Obje naredbe uspješno su izvršene. Skripta provjerava:

- identičan BIN i ostale ne-JSON chunkove te nepromijenjene definicije mreža, accessora, slika, tekstura i materijala;
- svjetske položaje svih vrhova i svjetske granice svake od pet mreža u svakom LOD-u: ukupno 15 mreža, najveća razlika **0**;
- ispravnu hijerarhiju bez ciklusa, višestrukih roditelja ili nedostupnih čvorova;
- naplatak u svakom od četiri podstabla i pripadnost svakog `Rim` primitiva najbližem kotaču;
- očuvano ime i uporabu materijala `Display_37`;
- identičan sadržaj obje zasebne PNG teksture displeja.

Dodatno su provjereni zajednički pivoti i teksture između LOD-ova, projektne putanje i njihove `.meta` datoteke, neizmijenjenih 28 drugih kandidata te `git diff --check` za promijenjeni editor kod i konfiguraciju.

Unity nije pokrenut. C# kompilacija, stvarno generiranje `Assets/Generated/...`, vrtnja u igri, prijelazi LOD-ova i konačan izgled stakla nisu izvršno/vizualno provjereni. Izmjene importera primijenit će se pri sljedećem uvozu kandidata iz izbornika **Magistrala/Auti: uvezi kandidata**; materijal nadstrešnice pri sljedećem **Magistrala/Autobusne stanice: uvezi staklenu nadstrešnicu**. Generirani asseti se ne commitaju prema postojećem `.gitignore`.

Nema izmjena runtime hot pathova, dodatnih alokacija po frameu ni GPU Resident Drawera. `Assets/Scenes/*` i `ProjectSettings/*` nisu uređivani ovim zahvatom. Njihove lokalne izmjene postojale su prije početka rada i nisu dio ovog popisa. Commit nije napravljen jer je `.git` samo za čitanje.

## Datoteke za commit

```text
Assets/Editor/KandidatImporter.cs
Assets/Editor/StanicaStaklo.cs
Assets/Editor/auti-kandidati.json
Assets/Models/Vehicles/Traffic/bus37.meta
Assets/Models/Vehicles/Traffic/bus37/autobus-37.glb
Assets/Models/Vehicles/Traffic/bus37/autobus-37.glb.meta
Assets/Models/Vehicles/Traffic/bus37/autobus-37_lod1.glb
Assets/Models/Vehicles/Traffic/bus37/autobus-37_lod1.glb.meta
Assets/Models/Vehicles/Traffic/bus37/autobus-37_lod2.glb
Assets/Models/Vehicles/Traffic/bus37/autobus-37_lod2.glb.meta
Assets/Models/Vehicles/Traffic/bus37/display-37-trogir.png
Assets/Models/Vehicles/Traffic/bus37/display-37-trogir.png.meta
Assets/Models/Vehicles/Traffic/bus37/display-37-split.png
Assets/Models/Vehicles/Traffic/bus37/display-37-split.png.meta
Tools/vehicles/prepare_bus37.py
docs/bus-dorada.md
```

Izvor modela: „Mercedes-Benz Citaro 2 Turen (O530) HQ interior”, Nieve5677, [Sketchfab](https://sketchfab.com/3d-models/51dceb73d719491c9f83ea896a885a80), CC BY 4.0. Kopije zadržavaju postojeće izmjene modela (PROMEX livreja, uklonjene marke, pripremljeni LOD-ovi), uz ovdje opisanu promjenu hijerarhije kotača. Atribucija ostaje i u konfiguraciji kandidata.
