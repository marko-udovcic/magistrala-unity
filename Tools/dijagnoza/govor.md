# Govor i uski tlocrti — 22. 11. 2026.

- Pregledane su sve Matine i kupčeve replike te tekstovi poslova, osim odobrenih Jurinih. Dorađena je jedna Matina replika, 95 kupčevih replika i 29 opisa poslova. Sačuvani su prisnost starijih, žurba majstora, studentski govor i Sanjino obraćanje na vi.
- Ujednačeni su prirodni lokalni oblici: `iman`, `neman`, `platin`, `čekan`, `naša san`, `doli`, `na mistu`, `rišila`, `ćer`, `pisak`. Zamijenjene su knjiške ili nezgrapne formulacije poput „skupljan troškove“, „uredna svita“ i „za prihvat“.
- Skraćene su ponude poslova i razlomljene duge rečenice. Najduža kupčeva replika ima 55 znakova, najduži opis posla 129 (bez Jure).
- Zorkina uloga sada je `Kaštel Lukšić`, prema vlasnikovoj odluci i njezinu tekstu.
- JSON je provjeren s `python3 -I`. Ručni raspored datoteke je očuvan; ključevi, ID-jevi, brojevi, stajališta i svi placeholderi ostali su isti. Cijeli objekti `jure` i `jure_drva` uspoređeni su bajt po bajt s HEAD-om i nisu mijenjani; README također nije mijenjan.

## Tlocrti

- `-6_-5:407`: širina 1,4203 m, duljina 35,4208 m, površina 50,3005 m², visina 13 m.
- `-6_-5:409`: širina 1,4101 m, duljina 34,8494 m, površina 48,9623 m², visina 13 m.
- Oba DGU zapisa imaju `kind=apartment`, `building=school`, `hSrc=game`. Oba dodiruju stvarni školski tlocrt `-6_-5:408` (1902,26 m²). Usporedba lokalnog DOF ortofota s obrisima pokazuje uske trake uz isti južni rub škole, bez zasebnih zgrada: artefakti, a ne dva dodatna bloka.
- `BuildingsFromTiles.cs` i zrcalni izračun u `stupovi.py` sada odbacuju širinu strogo manju od `SHAPE_MIN_WIDTH` (1,5 m) bez obzira na vrstu i OSM visinu. Vlasnikovi ispravci (`ispravak`) imaju prednost. Ostala pravila za male kuće nisu proširena na apartmane; škola `408` ostaje.
- Ponovljena analiza: 12 068 tlocrta, 976 CSV redaka, 975 dijagnostičkih kandidata. Prije čišćenja modelima: `keep` 11 118 → 11 116, `skip` 51 → 53, `cap` 899 → 899. Nakon čišćenja: `skip` 50 → 52, `cap` ostaje 889–890 (neizvjesnost prihvaćanja ortofoto pomaka bez Unityja). Lukšić: `skip` 15 → 17. Ortofoto dodaci: 182 zadržana, gornja granica prije cestovnog odbacivanja.
- U CSV-u su se promijenila samo dva navedena zapisa, oba `keep` → `skip`. Provjereni su prag 1,5 m, različite vrste zgrada, OSM visine, vlasnikovi ispravci i postojeće pravilo sićušnih kuća. Unity nije pokretan.
