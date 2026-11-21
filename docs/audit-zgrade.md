# Zgrade prema ortofotu – audit i integracija (2026-11-21)

Izvor: DGU **Digitalni ortofoto LiDAR 2022./23.** (mozaik 0,5 m/px na mreži igre, `BalkanDriver/tools/world/dgu/cache/dof/mosaic-lidar`),
tlocrti iz pločica (`split-kastela/tiles`, sekcija 4: DGU katastar u koridoru ±150 m, drugdje OSM), predmemorirani DGU WFS
odgovori (katastar) i `osm-corridor-original.json`. Alati: `Tools/zgrade/` (README). Ni Google / Esri / Bing ni Street View.
Isječci (nisu u gitu): `BalkanDriver/references/fotografije/unity-proba/zgrade-audit/audit-*.png`.

## Metoda

- **Klase piksela** (`classify.py`): vegetacija (ExG), sjena (V < 50), crijep (crveni ton: r ≥ g, r − b ≥ 22, (g − b)/(r − b) < 0,56),
  svijetlo sivo / bijelo (zasićenje < 0,17, V ≥ 178), sivo, tamno, ostalo (tlo). Provjera: `audit-klase-piksela-stari.png`.
- **Pomak** po tlocrtu: unakrsna korelacija (FFT) maske tlocrta s „krovnošću” (crijep 1, bijelo 0,8, sivo 0,25, tamno 0,
  tlo −0,3, sjena −0,6, vegetacija −1): unutra minus prsten 2 m, + 0,12 × gradijent na rubu, − 0,012 / m pomaka; ±8 m.
  Pouzdan pomak: istaknutost vrha ≥ 0,1 (za statistiku pomaka samo kose krovove s ≥ 60 % crijepa, istaknutost ≥ 0,12).
  Ortofoto nije pravi ortofoto: krov je pomaknut od podnožja za visinu × udaljenost od nadira / visina leta, pa je
  pomak krova ≈ 1–2 m normalan i nije greška tlocrta. Zato se izvori uspoređuju **međusobno u istoj zoni** (DGU − OSM).
- **Pokrivenost**: udio piksela pod tlocrtom koji nisu vegetacija / tlo / sjena (na mjestu i na najboljem pomaku).
- **Klasa krova** (unutrašnjost tlocrta skupljena 0,5 m, na najboljem pomaku ako je pouzdan i ≤ 4 m, inače na mjestu):
  kosi (crijep ≥ 45 %), mješoviti (crijep 20–45 % + ravno ≥ 30 %), ravni svijetli (ravno ≥ 55 %), ravni tamni (tamno > ½ ravnog),
  lim / hala (ravni, industrijski ili > 700 m²), ništa (vegetacija ≥ 60 %).
- **Solari**: plavkasti (b ≥ r + 8), ≥ 35 razina tamniji od medijana krova, kompaktni grozdovi ≥ 2 m², ≥ 1 m od ruba tlocrta.
- **Nedostajuće zgrade** (`missing.py`): crijep (otvaranje 3 px) izvan svih tlocrta igre + 3 m, komponente ≥ 40 m²; zatim
  provjera prema DGU katastru (predmemorija), OSM originalima i zgradama paketa; „očite” = popunjenost ≥ 0,6, 40–300 m²,
  pravokutnik ≤ 1,6 × površine, r − b ≥ 30, ≥ 60 % crijepa, prsten ≤ 25 % crijepa, tekstura krova (`misssel.py`).

## Brojke (12 068 tlocrta u pločicama −10…−1 × −7…−5, 12 039 analizirano, 29 bez ortofota)

### Pomaci

| | n | medijan dx / dz (m) | medijan \|d\| | napomena |
|---|---:|---|---:|---|
| svi pouzdani kosi | 3 186 | −0,5 / +0,5 | 1,58 | |
| DGU katastar | 2 338 | 0,0 / +0,5 | 1,50 | bez sustavnog pomaka |
| OSM | 846 | −1,5 / +0,5 | 2,50 | 50 % ima dx < −1 m |

OSM − DGU u istoj zoni (bootstrap 90 % CI, udio OSM zgrada pomaknutih na istu stranu):

| zona | OSM − DGU dx | CI | udio | n OSM / DGU | primijenjeno |
|---|---:|---|---:|---|---|
| Štafilić (obala) | −1,75 | [−2,0, −1,0] | 0,67 | 98 / 271 | **da** |
| Štafilić gornji | −1,00 | [−1,5, −1,0] | 0,59 | 164 / 74 | **da** |
| Kambelovac (obala) | −1,00 | [−2,0, −0,5] | 0,55 | 94 / 193 | **da** |
| Kambelovac gornji | −0,75 | [−1,0, 0,0] | 0,53 | 81 / 28 | ne (CI dodiruje 0) |
| Stari gornji | −0,50 | [−1,0, −0,5] | 0,48 | 87 / 267 | ne (< 0,75 m) |
| Lukšić gornji, Novi gornji, Gomilica | −0,5 … 0 | | ≤ 0,46 | | ne |
| Lukšić / Stari / Sućurac obala | −2,25 / −2,0 / −1,0 | | | 22 / 13 / 19 OSM | ne (premalo OSM-a) |

Ništa nije > 3 m. DGU nema sustavnog pomaka nigdje (medijani ≤ 1 m, isti predznak kao OSM unutar zone = relief).
Pojedinačni pomaci (p90 \|d\| ≈ 6–7 m) su uglavnom krive korelacije u gustim jezgrama i kod sivih ravnih krovova (asfalt i
krov iste boje) – ne primjenjuju se po zgradi.

### Pokrivenost i višak

- Pokrivenost na mjestu: medijan 0,99, prosjek 0,95, p10 0,88 (najbolji pomak: prosjek 0,97).
- Tlocrti bez krova (pokrivenost < 0,35): **168** (DGU 143, OSM 25); od toga 96 na čistoj vegetaciji / tlu. Pregled
  (`audit-niska-pokrivenost.png`, `audit-bez-krova.png`): oko pola su prazne parcele / vrtovi, ostalo kućice pod krošnjama,
  staklenici i krivo pomaknute korelacije. **Ne brišu se**: katastar je iz 2026., ortofoto iz 2022./23. – prazna parcela na
  fotografiji je često kuća izgrađena poslije snimanja.

### Nedostajuće zgrade

- Crveni krovovi bez tlocrta u igri (≥ 40 m²): **517**; od toga **409** nema ni u katastru, ni u OSM-u, ni u paketu
  (108 ih katastar ima – izbačene namjerno: ceste, raskrižja, stanice).
- „Očite” kuće: **263**; od toga **182** u području gdje je katastar predmemoriran (dakle nema ih ni u OSM-u ni u katastru)
  → **dodane** u igru kao pravokutnici (min. pravokutnik krova − 0,3 m strehe, h 7 m, kosi krov, boja iz palete);
  81 izvan predmemoriranog katastra samo popisano (`missing.json`). Provjera: `audit-nedostaju-dodani.png` (~90 % pravih kuća;
  lažni: crvena oranica, šupe), `audit-nedostaju-odbaceni.png`.
- Ravni sivi krovovi koji nedostaju nisu traženi (siva = asfalt / beton dvorišta) – vjerojatno ih ima još desetak posto.

### Krovovi (klasa po zgradi; konačni odabir)

| klasa | zgrada | udio površine |
|---|---:|---:|
| kosi crijep | 5 536 (46 %) | 46,9 % |
| ravni svijetli / sivi (terase, membrane) | 5 204 (43 %) | 31,3 % |
| mješoviti (dio crijepa, dio terase) | 796 (7 %) | 6,4 % |
| lim / hala | 144 (1,2 %) | 12,6 % |
| ravni tamni | 58 (0,5 %) | 0,3 % |
| ništa / nepoznato | 301 (2,5 %) | |

Po zonama (n, udio OSM-a, kosi / ravni svijetli, solari, medijan boje crijepa na fotografiji, sRGB):

| zona | n | OSM % | kosi | ravni | solari | crijep |
|---|---:|---:|---:|---:|---:|---|
| Štafilić | 1093 | 24 | 54 % | 37 % | 22 | 152 126 115 |
| Štafilić gornji | 782 | 64 | 45 % | 43 % | 25 | 153 126 115 |
| Novi | 846 | 4 | 45 % | 43 % | 12 | 155 131 120 |
| Novi gornji | 661 | 22 | 47 % | 43 % | 10 | 152 126 115 |
| Stari (jezgra) | 648 | 8 | 45 % | 43 % | 8 | 153 128 116 |
| Stari gornji | 1340 | 27 | 46 % | 46 % | 13 | 154 128 115 |
| Lukšić | 747 | 14 | 49 % | 37 % | 9 | 164 137 124 |
| Lukšić gornji | 1209 | 16 | 54 % | 37 % | 9 | 159 133 120 |
| Kambelovac | 885 | 33 | 53 % | 37 % | 7 | 150 124 111 |
| Kambelovac gornji | 414 | 73 | 50 % | 43 % | 0 | 145 118 105 |
| Gomilica | 1177 | 14 | 43 % | 46 % | 12 | 141 117 104 |
| Gomilica gornji | 415 | 31 | 42 % | 48 % | 5 | 135 111 99 |
| Sućurac | 1520 | 11 | 33 % | 53 % | 17 | 176 149 132 |
| Sućurac gornji | 302 | 100 | 44 % | 49 % | 2 | 186 153 135 |

- Crijep na fotografiji: medijan sRGB (155, 128, 116), p10 (137, 112, 101), p90 (181, 151, 135) – izmaglica snimke ga čini
  blijedim; razlike između zona (Sućurac svjetliji, Gomilica tamnija) su dijelom ekspozicija snimaka, zato se boja po kući
  računa **relativno prema susjedstvu** (ćelije 250 m, 3 × 3).
- Ravni svijetli: medijan (158, 161, 153), p10 (116, 117, 111), p90 (205, 205, 197).
- Solari: 151 krov (1 318 m²) – pri 0,5 m/px i izmaglici preciznost je oko pola (`audit-solari.png`: dio su crni spremnici
  vode / sjene uz parapet); stvarnih ima sigurno više (mali solarni bojleri se ne vide).
- Igra prije: gotovo svi krovovi isti narančasti crijep, ravni krovovi samo kod ~⅓ novih kuća i blokova; fotografija: 43 %
  zgrada ravno, a crijep je od blijedog lososa do tamne cigle.

## Integracija (Unity)

- `Tools/zgrade/export.py` → `Assets/Editor/zgrade-ortofoto.json` (640 kB, samo editor, nije u buildu):
  po tlocrtu (pločica, indeks; provjera težišta ±1,5 m) klasa, odluka (1 kosi: crijep ≥ 55 %, 2 ravni: ravno ≥ 65 % i
  crijep ≤ 12 %, 0 proceduralno), boja crijepa, boja ravnog krova, solari (m², mjesto), pomak OSM-a; i dodane kuće.
- `OrthoRoofs.cs` čita tablicu (`OrthoRoofs.Enabled = false` vraća staro).
- `BuildingsFromTiles`: pomak OSM tlocrta po zoni (ne ako bi kut došao bliže od 0,5 m kolniku), dodane kuće (ne bliže od
  0,8 m kolniku, ne na modelima / paketu), aneks (niže krilo s terasom) ne na kući koju fotografija pokazuje kosom.
- `HouseBuilder`: odluka s fotografije ima prednost pred pravilom (kosi → hip / zabat po starom pravilu; tlocrt koji kosi krov
  ne može pokriti → ravna ploča s crijepom); boja crijepa po kući: paleta igre (blijedi losos #9E705E … novi crijep #A04A2E,
  svjetlina ×0,8–1,16, ton crvenije / smeđe) po percentilu svjetline, zasićenosti i tona **u odnosu na susjedstvo** (izmaglica
  i ekspozicija snimke se ponište); kuće bez mjerenja dobiju izvlačenje iz iste palete. Shader `Magistrala/Facade`: krovni sloj
  s COLOR.a = 1 uzima luminanciju crijepa × 2,03 × boja (slobodna boja, tekstura ostaje); paket (a = 0) kao prije.
  Ravni krovovi: membrana / beton po izmjerenoj svjetlini (sRGB 105–170, tamni 78–92, lim #B4B8BB) umjesto jedne tamne sive;
  parapet i ograde kao prije. Solari: moduli 1 × 1,65 m (tamno plavo staklo) na južnoj kosini (mreža oko izmjerenog mjesta) ili
  u redovima pod 25° na ravnom krovu; u mreži zgrade (near / mid, gornja ploha u far), bez novih draw callova.
  Paleta crijepa: blijedi losos #966C5C … crijep #984E34 (prva runda #A04A2E je u igri pod suncem bila prenarančasta).
- Fasade (opća dalmatinska paleta, determinirano po kući, ne iz fotografija ulica): + blijedo žute, oker i „kamene” sivo-bež,
  ±9 % svjetline (prije ±7 %).

## Rezultat (Build tile buildings, 2026-11-21)

`[Magistrala] orthophoto roofs: 12 061 tlocrta sa zapisom, kosi 4 479 + crijep na ravnoj ploči 750, ravni 4 998,
solari na 139 krovova (587 modula), OSM pomaknuto 1 048 (3 ostavljena: kolnik), dodano 180` (2 dodane uz kolnik izbačene).
Zgrada (s aneksima) ukupno 12 830, od toga 740 aneksa (aneks se više ne radi na kući koju fotografija pokazuje kosom).

- Build macOS: 1 592 → 1 600 MB (tablica je samo u editoru; razlika = novi tlocrti i solari u mrežama).
- Trokuti u kadru (isti kadar, [perf]): +0,7 … +1,3 %; batchevi ±10. FPS A/B (1600 × 1000, srednje, 2 × 4 prozora po 10 s,
  izmjenično main / zgrade, uz još dva otvorena Unity editora drugih agenata – šum ±15 fps): Novi riva 39,5 → 49,5,
  skladište Lukšić 47,1 → 45,5, Kambelovac 36,7 → 40,8 (medijani) – nema mjerljivog pada.
- Slike prije / poslije: `references/fotografije/unity-proba/zgrade-audit/{prije,poslije}-{odozgo,koso,ulica}-*.png`.

## Što ostaje (po važnosti)

1. **Visine** (LiDAR / nDSM): 3 758 zgrada ima zadanu visinu, 5 269 visinu stare igre – ortofoto to ne može dati. DGU LiDAR
   oblak točaka (isti let 2022./23.) dao bi visinu strehe i sljemena po zgradi, a usput i pravi oblik krova (smjer sljemena,
   kosi / ravni bez nagađanja po boji, terase na dijelu krova) i siguran pomak krov ↔ podnožje. To je najveći preostali
   dobitak; ortofoto je potrošen.
2. Spojeni katastarski blokovi u starim jezgrama (750 „crijep na ravnoj ploči”): stvarno su nizovi malih kosih krovova –
   dijeljenje bloka po crijepu / sjenama sljemena s fotografije ili LiDAR-om.
3. Ravni krovovi koji nedostaju (siva boja = asfalt; nisu traženi) i 81 kuća izvan predmemoriranog katastra (popis u
   missing.json, dodati kad se katastar provjeri).
4. Solari: preciznost ~50 % pri 0,5 m/px; izvorni DOF 0,25 m/px (WMS) bi ih pouzdano razlikovao od crnih spremnika.
5. 168 tlocrta bez krova na fotografiji ostavljeno (novogradnja poslije 2022.); provjera na DOF 2025./26.
