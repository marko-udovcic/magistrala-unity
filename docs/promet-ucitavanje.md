# Promet: kako auti ulaze u scenu bez iskakanja

Datum: 2026-11-26. Kratko istraživanje (≈30 min) + dijagnoza i popravak u `Assets/Scripts/Traffic/TrafficSystem.cs`.
Izvori su uglavnom forumi i intervjui, a ne službena dokumentacija; što je iz općeg znanja, tako i piše.

## 1. Što rade druge igre

| Igra | Kako se auti pojave | Što preuzeti |
|---|---|---|
| GTA III / VC / SA (Obbe Vermeij, bivši Rockstar) | U smjeru pogleda auto se rodi ~70 m ispred, izvan pogleda samo ~15 m; briše se na ~90 m ako ga igrač vidi, ~25 m ako ne vidi. GTA IV isti algoritam s većim udaljenostima. Puna fizika samo do ~20 m. | Rađanje ovisi o pogledu; izvan pogleda smije blizu, u pogledu samo daleko. Brisanje također ovisi o pogledu. |
| GTA V (modovi, općenito znanje) | Gustoća po zoni i dobu dana (popcycle), auti se rađaju na čvorovima ceste izvan pogleda ili iza zaklona; modovi povećavaju "render distance" da se ne vidi rađanje. | Gustoća po vremenu dana; rađanje na čvorovima mreže. |
| Midtown Madness (Gamasutra "AI Madness") | Promet živi u "mjehuru" oko igrača; za svaku "cull room" unaprijed je izračunato koje su ceste vidljive. Kad igrač prijeđe u drugu sobu, ceste koje su ispale iz mjehura vraćaju aute u bazen (pool), nove se pune do gustoće (duljina auta / duljina ceste). Na rubu mjehura auto s dvosmjerne ceste prebacuje se na početak suprotnog smjera. Daleki auti voze po "šini" (spline) bez fizike. | Bazen auta, punjenje po gustoći, recikliranje na rubu, jeftina simulacija na šini — to već imamo. |
| Euro Truck Simulator 2 / ATS | `g_traffic` je množitelj gustoće; `g_lod_factor_traffic` određuje do koje udaljenosti se auti crtaju i kada mijenjaju LOD. Auti se rađaju daleko ispred i iza, na prilazima čvorovima, i iz čvorova (raskrižja, odvojci) uz voznu traku. Igrači se žale na auti koji "iskoče" na raskrižjima kad je gustoća visoka. | Crtati aute daleko (nisko-poligonski LOD), rađati ih na ulazima u mrežu (odvojci), ne na otvorenoj cesti u pogledu. |
| BeamNG | "Pooling": više vozila nego aktivnih, neaktivna skrivena; respawn je "sight based" (iza igrača / izvan pogleda), "respawn factor" mijenja minimalnu udaljenost. Igrači se žale da okretanje kamere premješta promet. | Test pogleda + minimalna udaljenost; okretanje kamere ne smije pokretati rađanje odmah uz rub pogleda. |
| Forza Horizon | Prometni auti dugo se smatraju slabom točkom (iskakanje na ~100–200 m po forumima). | Primjer kako ne. |
| Need for Speed (forum, konfiguracija) | Min/Max spawn distance; veći minimum znači i veći maksimum. | Prsten rađanja daleko + tanka gustoća u prstenu. |
| City Car Driving (općenito znanje) | Gustoća po postavkama, auti izlaze iz bočnih ulica i parkinga, svjetla daleko. | Bočne ulice kao izvori. |
| Cities: Skylines | Vozila se stvaraju samo u zgradama (parkiralište, garaža) i na vanjskim vezama (rub karte), nikad na sredini ceste. | Najčišće rješenje: izvori i ponori na rubu mreže i u zgradama. |

Zajedničke ideje:
1. **Rub mreže i čvorovi kao izvori.** Auto ulazi iz sporedne ulice, garaže ili s ruba karte, a ne iz zraka na sredini ceste.
2. **Rađanje izvan pogleda ili iza zaklona,** u pogledu samo jako daleko, uz rezervu kuta (guard band), da okret kamere ne otkrije svježe rođen auto.
3. **Postupno pojavljivanje na granici crtanja** (dither ili alpha), i za sjenu.
4. **Daleki LOD/impostor** da granica crtanja može biti daleko (500–1000 m), a cijena mala.
5. **Bazen i jeftina simulacija daleko**: auto "postoji" i napreduje i kad se ne crta; kad uđe u doseg, pojavi se tamo gdje bi stvarno bio.
6. **Noću**: svjetla (emisivni točkići) vide se daleko prije tijela.

## 2. Dijagnoza kod nas (pravi build, `-spawnlog`)

Novo logiranje u `TrafficSystem` (samo uz `-spawnlog`):
- `[firstview]` za svaki auto u prvom trenutku kad ga oko stvarno vidi (u kadru, unutar granice crtanja, barem jedna od 4 zrake
  slobodna; stupovi uz kameru se ne broje): udaljenost, starost, kako je rođen (`out` izvan pogleda, `view-hidden` iza zaklona,
  `view-far` iza granice crtanja, `entry` na početku koridora, `warm`), koliko je bio neproziran (fade) i vrsta:
  - `POP-born`: viđen odmah nakon rođenja (< 1 s) s vidljivim dijelom tijela (fade > 0,15) – pravo iskakanje;
  - `POP-turn`: mlad (< 8 s), a otkriven okretom kamere, bez da je prije bio iza zaklona ili iza granice crtanja;
  - `emerged` (izašao iza brijega / zgrade) i `faded` (rastopio se na granici crtanja) – tako ulaze i pravi auti.
- `[vanish]` auto maknut dok ga se vidi; `[popstat]` zbroj svakih 30 s; `[trafcost]` auti, pozivi crtanja, instance, ms.
- `-trafpk25` vraća stari (paket25) prsten i stari način rađanja u istom buildu, za A/B; `-trafring a,b,c,d,e,n,r` bilo koji prsten.
- `-campose …,spin` (6. vrijednost) okreće kameru u mjestu (°/s); `-shotat` je uz `-capfps` u vremenu igre (ponovljivi nizovi slika).

Uzroci koje je log pokazao (vožnja D8 80 km/h, stara cesta 60 km/h, stajanje u Lukšiću, rub D8):
1. **Lažno "skriven" auto u pogledu** (glavni uzrok). Auto se smio roditi u kadru na 650–760 m ako su sve 4 zrake prema njemu
   bile blokirane. Na 700 m se te 4 zrake blizu kamere razilaze tek nekoliko centimetara, pa ih **jedan stup, znak, rasvjetni stup
   ili kutija parkiranog auta uz kameru** blokira sve odjednom; isto rub mosta ili samo tjeme brijega. 0,2 s kasnije auto je
   vidljiv s 20–100 % tijela: `POP-born` na 621–714 m (D8: 5 u 150 s, stara cesta 1). Daleko je, ali na praznoj ravnoj cesti oko
   to primijeti, osobito dok se čeka.
2. **Okret kamere**: rađanje "izvan pogleda" testiralo je samo trenutni frustum, bez rezerve; i prsten se mjerio po luku koridora,
   pa je na zavoju auto mogao biti bliže zračnom linijom.
3. **Kraj koridora**: na otvorenom kraju (D8 prema Splitu, Sućurac, Štafilić) auto je nestajao u pogledu; na zatvorenom kraju
   (radovi na staroj cesti s13, D8 prema Trogiru) red auta brisao se u pogledu već s 350 m.
4. **Početak koridora**: uz početak (Sućurac, Štafilić, D8 istok) taj dio nije dobivao aute (prsten nije stao), cesta je bila prazna.
5. **Noć**: daleki auti nemaju upaljena prednja svjetla (daleki LOD), pa se noću ništa ne vidi dok auto nije blizu.
6. Nije nađeno: teleport (nitko ne zove `Clear`), warm start (samo pri učitavanju), skok LOD-a na 70 m (spojena daleka mreža
   iste boje), parkirani auti (rastapaju se 215–260 m, nisu rađanje). Brda: auti izlaze iza tjemena D8 na ~520–550 m (`emerged`),
   to je prirodno.

## 3. Popravak

- **Zaklon mora stvarno zaklanjati**: zapreke bliže od 25 m od oka se ne broje; za rađanje vrh zrake je 2 m iznad krova i 1 m
  šire, i to mora vrijediti i iz točke gdje će oko biti za 1,5 s (brzina igrača + 1 m gore).
- **Pogled s rezervom 25°** na sve strane za rađanje; izvan pogleda auto se ne rađa bliže od 0,85 × prstena zračnom linijom
  (osim iza pravog zaklona).
- **Veći prsten (Markov prijedlog)**: rađanje 700–1200 m, rastapanje 850–1050 m, brisanje 1300 m, 64 auta (ista gustoća po km
  kao 46 na starom prstenu), stara cesta se puni i kad se vozi D8 (600 m). Daleko je auto spojena daleka mreža (3 instancirana
  crtanja po vrsti, broj auta ne mijenja broj poziva), pa je trošak isti (vidi brojke).
- **Ulazi na rubu mreže**: koridor koji počinje gdje se cesta nastavlja (Štafilić, Sućurac, D8 kod Splita) dobiva aute na
  početku, izvan pogleda (≥ 160 m); nikad pokraj zatvorene ceste.
- **Izlazi**: auto na otvorenom kraju nastavlja ravno do 150 m dok ga se vidi, nestaje tek izvan pogleda; red na zatvorenom kraju
  briše se u pogledu tek iza granice crtanja.
- **Noć**: dalekim autima (od 70 m) dvije male emisivne točke sprijeda (bijele) i straga (crvene), veličine ~2 px, nestaju
  prema rubu prstena. 2 instancirana crtanja, bez sjena.

## 4. Brojke

Isti build, `-trafpk25` (stari način) prema novom; "svjež" = auto mlađi od 8 s; "POP" = `POP-born` + `POP-turn`.

| Scenarij | paket25: POP (najbliži) | paket25: najbliži svjež | novo: POP | novo: najbliži svjež |
|---|---|---|---|---|
| D8, autopilot 80 km/h, 150 s | 5 (644 m) | 412 m | 0 | 613 m |
| stara cesta, 60 km/h | 1 (621 m) | 440 m | 0 | 822 m |
| kamera se okreće 30°/s uz D8 | 23 (351 m, 2 ispod 400 m) | 351 m | 0 | 641 m |
| Tuđmanova do Sućurca | 0, ali 4× `[vanish]` na kraju (485–706 m) | 408 m | 0, 0× vanish | 1044 m |

Najbliži auto koji se uopće prvi put vidi (i stari, izašao iza zgrade) i dalje može biti blizu (stajanje u Lukšiću: 78 m
između kuća), to je prirodno.

Veći prsten (isti build, D8 150 s): paket25 prsten 480–850 / 46 auta: ~92 poziva crtanja, ~110 instanci, 0,65 ms;
novi 700–1200 / 64 auta: ~72 poziva, ~100 instanci, 0,30 ms (broj poziva ovisi o broju različitih vrsta i boja u kadru, ne o
broju auta: daleki auto je 3 instancirana crtanja po vrsti, a iza ruba fadea se ne crta; razlika je šum vožnje). FPS A/B: FPSAB.

Je li veća udaljenost pomogla: da, ali uglavnom zato što je rub crtanja (850–1050 m) sada dalje od onoga što oko na 1600×900
razlikuje (auto ~4–5 px), a rađanje "izvan pogleda" je sad ≥ 600 m zračnom linijom. Sam po sebi veći prsten ne bi riješio
glavni uzrok (lažni zaklon uz kameru); taj je riješen testom zaklona. Na brdima i u mjestu prvi pogled i dalje određuju
zapreke (tjeme D8 ~520 m, kuće), što je ispravno.

## 5. Promet po sporednim ulicama

Mreža `mag-25-11/promet/promet_mreza_sve.json`: 3009 komada (1841 iz modela, 788 traka vanjskih cesta, 380 generiranih
skretanja), 243,5 km, 135 ulaza, 98 izlaza, 78 slijepih komada; prednost na raskrižjima bez modela je pretpostavljena.

Bi li pomogla ulasku auta: **da, i to najviše u mjestima**. Danas promet postoji samo na 6 koridora, pa se auti mogu pojaviti
samo na rubu prstena ili na kraju koridora. Na mreži bi auti ulazili i izlazili kroz sporedne ulice, dvorišta i parkirališta
(kao Cities: Skylines i City Car Driving), skretali na D8 i s nje, a "izvor" bi gotovo uvijek bio iza kuće, dakle prirodno
skriven. Ovdašnja logika (prsten, zaklon, fade, točke noću) bi se zadržala.

Procjena posla (jedan agent):
- uvoz mreže i graf (sljedeći / prethodni komadi, trake), praćenje auta po komadima, IDM preko granica komada: 2–3 dana;
- raskrižja: prednost, rezervacija konfliktnih komada, semafori modela, skretanje s/na D8 koridor: 3–4 dana;
- izvori i ponori (ulazi, izlazi, parkirališta), gustoća po vrsti ceste, rubni slučajevi (slijepi komadi, uske ulice, kamion
  igrača u ulici): 2 dana;
- testovi (zastoji, prolasci kroz crveno, fps), podešavanje: 2–3 dana.

Ukupno ~**1,5–2 tjedna** za kvalitetnu verziju. Brži međukorak (2–3 dana): samo "ulazi i izlazi" — na ~20 spojeva sporednih
ulica s D8 i starom cestom auti izlaze iz ulice (stanu, žmigavac, uključe se u koridor) i skreću u nju, bez prometa u samim
ulicama.
