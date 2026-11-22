# Pregled hrvatskih tekstova

Pregledano: svih 45 datoteka `Assets/Scripts/**/*.cs` i jedina JSON datoteka u `Assets/Resources/**/*.json` (trenutačno `Game/faturete.json`). Obuhvaćeni su HUD, obavijesti, mobitel i pozivi, karta, kartice poslova, isplata, postavke kvalitete, vrijeme, kamera, dron i pomoćni prikazi.

Pripovjedač i sučelje koriste standardni hrvatski. Dijalekt u razgovorima ostaje sačuvan; jedina izmjena replike je očiti tipfeler „nastrešnicon” → „nadstrešnicon”. Nazivi likova poput „Šjora Anka” i „Ružica, cvićarka” ostaju lokalni nadimci. Izvorni naslovi 3D modela i imena autora u zaslugama ostaju neprevedeni radi točne atribucije. Nazivi tipki Tab, Enter, Backspace, Shift, PgUp/PgDn i Del ostaju oznake na tipkovnici; Space je razmaknica. Pojmovi „fatureta/faturete” i „račun obrta” zadržani su u svojim gramatičkim oblicima.

`customers[id=jure]` i `jobs[id=jure_drva]` ostali su nepromijenjeni, uključujući izvorno oblikovanje. Nisu mijenjani komentari, logika, identifikatori, ključevi, placeholderi ni interpolacijski izrazi.

Provjera: JSON se uspješno parsira; usporedba s HEAD potvrđuje iste ključeve, strukturu, brojčane vrijednosti i placeholdere. Statička leksička usporedba C# potvrđuje da su kod izvan tekstualnih literala, komentari, navodnici, escapeovi i cijeli interpolacijski izrazi identični izvorniku. `git diff --check` prolazi. Unity i C# prevoditelj nisu pokretani; provjera kompilacije ograničena je na očuvanje izvorne sintakse. Izgled i stane li svaki dinamički tekst u prikaz nisu provjereni u igri.

## Sve izmjene (66)

Brojevi redaka odnose se na izmijenjene datoteke. Svaka stavka navodi cijeli izmijenjeni literal; `\n` je izvorni escape u C# kodu.

- `Assets/Resources/Game/faturete.json:34`: `"Susida, Kaštel Lukšić"` → `"Susjeda, Kaštel Lukšić"`
- `Assets/Resources/Game/faturete.json:160`: `"Penzioner, Kaštel Štafilić"` → `"Umirovljenik, Kaštel Štafilić"`
- `Assets/Resources/Game/faturete.json:430`: `"Penzionerka, Kaštel Gomilica"` → `"Umirovljenica, Kaštel Gomilica"`
- `Assets/Resources/Game/faturete.json:430`: `"Vesna, penzionerka"` → `"Vesna, umirovljenica"`
- `Assets/Resources/Game/faturete.json:489`: `"Dogovoreno, sve je pod nastrešnicon."` → `"Dogovoreno, sve je pod nadstrešnicon."`
- `Assets/Resources/Game/faturete.json:502`: `"Nikola, voditelj radione"` → `"Nikola, voditelj radionice"`
- `Assets/Resources/Game/faturete.json:559`: `"Ormar i dvi komode"` → `"Ormar i dvije komode"`
- `Assets/Resources/Game/faturete.json:561`: `"Paleta blokova i deset vrića cementa"` → `"Paleta blokova i deset vreća cementa"`
- `Assets/Resources/Game/faturete.json:565`: `"Frižider iz stare kuće"` → `"Hladnjak iz stare kuće"`
- `Assets/Resources/Game/faturete.json:565`: `"frižider"` → `"hladnjak"`
- `Assets/Resources/Game/faturete.json:571`: `"Dvi stolice i ogledalo za salon"` → `"Dvije stolice i ogledalo za salon"`
- `Assets/Resources/Game/faturete.json:575`: `"Suve cjepanice u vrićama"` → `"Suhe cjepanice u vrećama"`
- `Assets/Resources/Game/faturete.json:577`: `"Osam vrića ubranih maslina"` → `"Osam vreća ubranih maslina"`
- `Assets/Resources/Game/faturete.json:583`: `"Dvanaest vrića cementa"` → `"Dvanaest vreća cementa"`
- `Assets/Resources/Game/faturete.json:589`: `"Rabljeni frižider za prvi stan"` → `"Rabljeni hladnjak za prvi stan"`
- `Assets/Resources/Game/faturete.json:589`: `"frižider"` → `"hladnjak"`
- `Assets/Resources/Game/faturete.json:593`: `"Suve mreže i prazne kašete"` → `"Suhe mreže i prazne sanduci"`
- `Assets/Resources/Game/faturete.json:597`: `"Dvi zatvorene bačvice vina po 50 litara"` → `"Dvije zatvorene bačvice vina po 50 litara"`
- `Assets/Resources/Game/faturete.json:605`: `"Mali ugostiteljski frižider"` → `"Mali ugostiteljski hladnjak"`
- `Assets/Resources/Game/faturete.json:605`: `"frižider"` → `"hladnjak"`
- `Assets/Resources/Game/faturete.json:609`: `"Snopovi suvih mreža i prazne vrše"` → `"Snopovi suhih mreža i prazne vrše"`
- `Assets/Resources/Game/faturete.json:613`: `"Ukrasne sadnice i vriće zemlje"` → `"Ukrasne sadnice i vreće zemlje"`
- `Assets/Resources/Game/faturete.json:615`: `"Zatvoren big-bag s 500 kg pijeska"` → `"Zatvorena velika vreća s 500 kg pijeska"`
- `Assets/Resources/Game/faturete.json:621`: `"Obnovljeni dvosjed i dvi fotelje"` → `"Obnovljeni dvosjed i dvije fotelje"`
- `Assets/Resources/Game/faturete.json:625`: `"Velike tegle sa sadnicama"` → `"Velike posude sa sadnicama"`
- `Assets/Resources/Game/faturete.json:627`: `"Metalne police za radionu"` → `"Metalne police za radionicu"`
- `Assets/Scripts/Game/KatoviTool.cs:55`: `"Katovi: nema katovi-index (Build tile buildings)"` → `"Katovi: nedostaje indeks zgrada"`
- `Assets/Scripts/Game/KatoviTool.cs:249`: `"F7 Katovi   PgUp / PgDn  ±1 kat   Enter spremi   Del poništi"` → `"F7 katovi · PgUp/PgDn ±1 kat · Enter spremi · Del poništi"`
- `Assets/Scripts/Game/MagistralaGame.Faturete.cs:105`: `$"Dogovoreno i čeka: {S.fatQueue[0].label}."` → `$"Na čekanju: {S.fatQueue[0].label}."`
- `Assets/Scripts/Game/MagistralaGame.Faturete.cs:186`: `$"Fatureta: {c.name} pita za {j.what.ToLower()}. Tab → Poslovi"` → `$"Fatureta: {c.name} · {j.what.ToLower()} · Tab → Poslovi"`
- `Assets/Scripts/Game/MagistralaGame.Payout.cs:42`: `$"Kasno ({Mathf.Round(over):0} min iza dogovora)"` → `$"Kašnjenje ({Mathf.Round(over):0} min)"`
- `Assets/Scripts/Game/MagistralaGame.Tours.cs:130`: `"Natrag u skladište s praznim gajbama"` → `"Povrat praznih gajbi u skladište"`
- `Assets/Scripts/Game/MagistralaGame.Vehicles.cs:222`: `"Atego firme. Ture se uzimaju u skladištu."` → `"Službeni Atego. Ture se preuzimaju u skladištu."`
- `Assets/Scripts/Game/MagistralaGame.Vehicles.cs:222`: `"Galeb. Ćaćin kamion. Pazi, koči kasnije nego Atego."` → `"Galeb. Očev kamion. Pazi, koči kasnije nego Atego."`
- `Assets/Scripts/Game/MagistralaGame.cs:247`: `"Ture firme voziš Ategom firme, ne Galebom."` → `"Ture firme voziš službenim Ategom."`
- `Assets/Scripts/Game/MagistralaGame.cs:334`: `$"Tab mobitel ({Phone.Unread} nova poruka) · M karta"` → `$"Tab mobitel (poruke: {Phone.Unread}) · M karta"`
- `Assets/Scripts/Game/MagistralaGame.cs:374`: `"J otkaži turu"` → `"J otkaži posao"`
- `Assets/Scripts/Game/MagistralaGame.cs:387`: `"Faturete se voze Galebom – GPS te vodi do njega"` → `"Faturete voziš Galebom · slijedi GPS"`
- `Assets/Scripts/Game/MagistralaGame.cs:391`: `"E – Istovar"` → `"E – istovar"`
- `Assets/Scripts/Game/MagistralaGame.cs:391`: `$"E – Utovar ({Current.cargo})"` → `$"E – utovar ({Current.cargo})"`
- `Assets/Scripts/Game/MagistralaGame.cs:398`: `"E – Istovari dizalicom (unazad +10 %)"` → `"E – istovari dizalicom (unazad +10 %)"`
- `Assets/Scripts/Game/MagistralaGame.cs:398`: `"E – Istovari dizalicom"` → `"E – istovari dizalicom"`
- `Assets/Scripts/Game/MagistralaGame.cs:398`: `"E – Predaj prazne gajbe"` → `"E – predaj prazne gajbe"`
- `Assets/Scripts/Game/MagistralaGame.cs:398`: `"E – Utovar"` → `"E – utovar"`
- `Assets/Scripts/Game/MagistralaGame.cs:403`: `"Parkiraj u označeno mjesto za istovar"` → `"Parkiraj na označeno mjesto za istovar"`
- `Assets/Scripts/Game/MagistralaGame.cs:403`: `"Parkiraj u označeno mjesto za utovar"` → `"Parkiraj na označeno mjesto za utovar"`
- `Assets/Scripts/Game/MagistralaGame.cs:419`: `"W / S prekini istovar · J otkaži turu"` → `"W/S prekini istovar · J otkaži posao"`
- `Assets/Scripts/Game/MagistralaGame.cs:426`: `"Istovar prekinut – stani u mjesto pa ponovno E."` → `"Istovar prekinut · parkiraj pa pritisni E."`
- `Assets/Scripts/Game/MagistralaGame.cs:592`: `$"{Magistrala.HudStyle.Dec(Current.km)} km · plaća {Eur(Current.pay)} · rok {Hm(Current.startMin + Current.deadlineMin)}"` → `$"{Magistrala.HudStyle.Dec(Current.km)} km · zarada {Eur(Current.pay)} · rok {Hm(Current.startMin + Current.deadlineMin)}"`
- `Assets/Scripts/Game/MagistralaGame.cs:603`: `"Smjena je gotova. Firma radi radnim danom 6–17 h."` → `"Smjena gotova · radno vrijeme: pon–pet 6–17 h."`
- `Assets/Scripts/Game/Phone/MatePhone.cs:58`: `"Ćaćin stari kolega"` → `"Očev stari kolega"`
- `Assets/Scripts/Game/Phone/MatePhone.cs:455`: `"Street-level imagery: Mapillary contributors (CC BY-SA 4.0) — boje fasada, položaji svjetiljki, parkirališta i stabala uz cestu.\n\n"` → `"Snimke s razine ulice: suradnici Mapillaryja (CC BY-SA 4.0) — boje fasada, položaji svjetiljki, parkirališta i stabala uz cestu.\n\n"`
- `Assets/Scripts/Game/Phone/MatePhone.cs:456`: `"Teksture i nebo: Poly Haven (CC0). Font Inter (SIL OFL 1.1).\n\n"` → `"Teksture i nebo: Poly Haven (CC0). Pismo Inter (SIL OFL 1.1).\n\n"`
- `Assets/Scripts/Game/Phone/MatePhone.cs:459`: `"Prometni auti — 3D modeli sa Sketchfaba, licenca CC BY 4.0, izmijenjeni (uklonjene oznake marke, naše tablice, boje, LOD):\n"` → `"Prometni automobili — 3D modeli sa Sketchfaba, licenca CC BY 4.0, izmijenjeni (uklonjene oznake marke, naše tablice, boje, LOD):\n"`
- `Assets/Scripts/Game/Phone/MatePhone.cs:471`: `"Prometni auti (nastavak), Sketchfab, CC BY 4.0, izmijenjeni:\n"` → `"Prometni automobili (nastavak), Sketchfab, CC BY 4.0, izmijenjeni:\n"`
- `Assets/Scripts/Game/Phone/MatePhone.cs:484`: `"Prometni auti (nastavak), Sketchfab, CC BY 4.0, izmijenjeni:\n"` → `"Prometni automobili (nastavak), Sketchfab, CC BY 4.0, izmijenjeni:\n"`
- `Assets/Scripts/Game/WorldMap.cs:582`: `"W gore · S dolje · A lijevo · D desno · kotačić zoom · Enter zakači / otkači · Tab stil · M zatvori"` → `"WASD pomak · kotačić povećanje · Enter postavi/ukloni oznaku · Tab stil · M zatvori"`
- `Assets/Scripts/RenderQuality.cs:15`: `"brzo"` → `"niska"`
- `Assets/Scripts/RenderQuality.cs:16`: `"srednje"` → `"srednja"`
- `Assets/Scripts/RenderQuality.cs:17`: `"oštro"` → `"visoka"`
- `Assets/Scripts/RenderQuality.cs:157`: `$"Kvaliteta: {L.name}  ·  crta se {Mathf.RoundToInt(Screen.width * Scale)}×{Mathf.RoundToInt(Screen.height * Scale)}"` → `$"Kvaliteta: {L.name} · razlučivost {Mathf.RoundToInt(Screen.width * Scale)}×{Mathf.RoundToInt(Screen.height * Scale)}"`
- `Assets/Scripts/Truck/DroneCamera.cs:152`: `$"DRON {agl:0} m  ·  WASD let · E/Q gore/dolje · desni klik + miš ili Z/X R/T okret · Shift brže · F kamion"` → `$"DRON {agl:0} m · WASD let · E/Q visina · desni klik + miš ili Z/X R/T pogled · Shift brže · F kamion"`
- `Assets/Scripts/Truck/TruckHud.cs:28`: `"WASD vozi · Space ručna · C kabina · F dron · R vrati · miš kamera"` → `"WASD vozi · razmaknica ručna · C kabina · F dron · R vrati · miš pogled"`
- `Assets/Scripts/Truck/TruckHud.cs:29`: `"G zalazak · V kiša · B kamera · L svjetla · P autopilot · K kvaliteta · F1 graf i pomoć · F2 konzola"` → `"G zalazak · V kiša · B kamera · L svjetla · P autopilot · K kvaliteta · F1 pomoć · F2 konzola"`
- `Assets/Scripts/Truck/TruckHud.cs:86`: `$"{Days[k % 7]} · Dan {k + 1}"` → `$"{Days[k % 7]} · dan {k + 1}"`
- `Assets/Scripts/Weather/CameraFeel.cs:221`: `"živa (njihanje, trešnja, blur)"` → `"živa (njihanje, trešnja, zamućenje)"`
