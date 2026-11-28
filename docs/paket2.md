# Paket2 — UKRAINIAN CITY paket u gradovima (pilot: Kaštel Lukšić, 28. 11. 2026)

Područja i prekidači: `Assets/Editor/paket2-podrucja.json` (igra x/z kutije; `"on": false` gasi sve ili jedan grad;
`krov`, `zbuka`, `vrata`, `ograde`, `plocnik` gase pojedine dijelove). Kod: `Assets/Editor/Paket2.cs` (In(x, z, dio)).

U području:
- **krov** — kuće dobivaju crijep paketa RoofTiles_A (kupa kanalica): sloj 15 niza `zbuke_*.jpg`, Facade varijanta 15 na sloju 2.
- **zbuka** — 2/3 kamenih kuća kamen Brick_E, 2/3 zdravih ožbukanih (A/B/C) svijetle žbuke Plaster_H/J (isti `pk` izvlačak).
- **vrata** — ulazna vrata Door_A_1/Door_A_2 (drvo; dio u boji škura) i okviri prozora paketa na svim pročeljima (ne samo uz cestu).
- **ograde** — u ulaznom otvoru dvorišta kapija Gate_B / Gate_A_1 (bojane), grm Bush_A uz stupove; uz ulicu žičana ograda na
  zidiću postaje kovana ograda paketa (Fence_B, bojana po kući), iza niske ograde grmovi svakih ~4,5 m (samo bliska razina).
- **plocnik** — pločnici dobivaju betonske ploče Pavement_A (StreetsBuilder MAT_WALK2, materijal StreetWalkPaket).

## Proširenje na drugi grad
1. Dodati kutiju u `podrucja` (npr. Kaštel Stari, Novi…).
2. Meniji u redoslijedu punog rebuilda: `Magistrala/Build streets` (pločnik), `Magistrala/Zgrade: paket (hale, škole)` (kuće,
   dvorišta). Teksture/detalji se ne ponavljaju (već su u gitu).

## Ponovno izvlačenje paketa (samo ako se mijenjaju teksture / detalji)
Paket se NE uvozi cijeli i nije u gitu (`/Assets/OdesaUkraine/` u .gitignore):
```
python3 -I Tools/paket2/outer.py "<…>/UKRAINIAN CITY Streets Buildings.unitypackage" a730870a63f300840837f8866508e24f <scratch>/urp.unitypackage
python3 -I Tools/paket2/index.py <scratch>/urp.unitypackage <scratch>/pkg
python3 -I Tools/paket2/extract.py <scratch>/urp.unitypackage <scratch>/pkg/index.json Tools/paket2/roots.txt <scratch>/x
python3 -I Tools/paket2/extract.py <scratch>/urp.unitypackage <scratch>/pkg/index.json Tools/paket2/roots_shaders.txt <scratch>/x
cp -R <scratch>/x/Assets/OdesaUkraine <clone>/Assets/
```
(URP varijanta paketa; ~1,2 GB, 297 datoteka; u .meta postaviti maxTextureSize 256 za brži uvoz — bake čita PNG s diska.)
Zatim u editoru: reimport `Assets/OdesaUkraine/Shaders` pa `Materials` (ForceUpdate), `PaketDetalji.Bake()`
(meni "Zgrade: paket – ispeci detalje"), `Paket2.Textures()` (meni "Paket2: teksture iz paketa"). Pregled: `Paket2Shots.Spots(dir, tag)`.
