# Assets/Audio — zvukovi igre (nisu u gitu)

Klipovi su u `Assets/Audio/Resources/Zvuk/<mapa>/` (`motor`, `okolina`, `promet`, `istovar`, `udarci`); kod ih učitava s
`Resources.Load<AudioClip>("Zvuk/motor/atego_engine_idle_on")` (Assets/Scripts/Audio/MagistralaAudio.cs). U gitu je samo ovaj
README; mixer (`Assets/Resources/Zvuk/MagistralaMixer.mixer`) i import postavke (`Assets/Editor/ZvukSetup.cs`) jesu.

## Kako ih napraviti i kopirati

1. U BalkanDriver repou (izvori u `~/Downloads/zvuk-kandidati/`, licence u `assets-src/zvuk/ATRIBUCIJA.md`):
   ```
   python3 -m venv /tmp/zvukvenv && /tmp/zvukvenv/bin/pip install numpy scipy
   PY=/tmp/zvukvenv/bin/python ~/Documents/BalkanDriver/assets-src/zvuk/scripts/build.sh
   ```
   → `~/Documents/BalkanDriver/assets-src/zvuk/out/` (87 datoteka, ~12 MB, 44,1 kHz; OGG petlje, WAV kratki).
2. Kopirati u Unity projekt (zatvoren Editor ili neka ih uveze):
   ```
   for d in motor okolina promet istovar udarci; do
     mkdir -p Assets/Audio/Resources/Zvuk/$d
     find ~/Documents/BalkanDriver/assets-src/zvuk/out/$d \( -name '*.ogg' -o -name '*.wav' \) -exec cp {} Assets/Audio/Resources/Zvuk/$d/ \;
   done
   ```
3. Import postavke se primijene same (`ZvukImport`: mono za 3D, ADPCM za kratke, Vorbis za petlje, streaming za zvona); ako su
   klipovi uvezeni prije te skripte: izbornik **Magistrala → Zvuk: ponovno uvezi zvukove**.
4. Mixer je već u gitu; samo ako ga treba iznova: **Magistrala → Zvuk: napravi mixer**.

Bez klipova igra radi normalno (bez zvuka; u logu `[zvuk] nema klipa …`). Prekidači: `-mute` (sve tiho, izvori rade),
`-zvuk 0` (sustav zvuka isključen — A/B za mjerenje). Svakih 10 s log `[zvuk] … glasova=…`.
