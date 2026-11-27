# Dorada kabine L4H1

Pregledano svih pet snimaka kabine i zrcala.

- **Instrumenti:** emisija postojećih tekstura i kazaljki 1,1 danju / 2 noću. Oko +10 cm gore i 6 cm natrag; volan −8 cm i 10° položenije. Sve 144 provjerene zrake oboda brojčanika i četiri kuta ploče prolaze mimo mreže volana u početnom položaju.
- **Zrcala:** stvarna ploha nagnuta je 32,3°; stari prikaz kroz središte AABB-a presijecao je staklo. Oba prikaza sada prate plohu, kamera je 25 mm ispred, near 80 mm. Jednaki sRGB ARGB32, bez post-processinga; maska isključuje prikazne plohe. Nije pronađena razlika ekspozicije kamera.
- **Žmigavci:** `,` lijevi, `.` desni, `\` sva četiri; ista tipka gasi smjer. `E` ostaje za vozilo. Amber podmreže razdvojene po strani, treptanje 1,5 Hz, automatsko gašenje nakon skretanja i vraćanja volana. Parkiranje/unatrag ne gase signal; sva četiri ne gase se automatski. Postojeći klipovi `motor/indicator_on/off`, samo u kabini, uz mute. L4H1 nema zasebne strelice; imenovane strelice podržane su. Za AI: `TruckController.Indicator` (−1/0/+1) i `HazardsOn`; promet ih zasad ne čita.
- **Pogon:** 350 Nm, vrh 109,1 kW (~148 KS), pod opterećenjem šaltanje oko 2.900 o/min; šesta na 90 km/h = 2.001 o/min. Puna masa 3.500 kg. Uzdužni proračun 0–50: **7,92 s pri 80 %**, 7,34 s pri punoj papučici. Proračun ne uključuje PhysX proklizavanje, ovjes ni prigušenje kotača. Stari Atego zadržava izvorne parametre i mapu pogona.
- **Pod:** zatvoren otvor ~38 mm do vrata punim aluminijskim pragom, debljine 40 mm, u sva tri GLB-a. `finish_glb.py` obavezno nakon izvoza; detalji u `Tools/kombi-l4h1/README.md`. Blender nije potreban za ovu doradu.

Provjere prolaze: runtime C# kompilacija Unityjevim Roslynom (samo postojeće upozorenje `blicajScroll`), 16 tihih C# provjera signala, GLB granice buffera, zatvorenost/normale praga, idempotentnost, vidljivost skala i proračun pogona. Priprema materijala/podmreža jednokratna je; vruće putanje koriste spremljene reference, audio ključeve i liste terena.

Unity nije pokrenut; nema commita ni GPU Resident Drawera. Scene i ProjectSettings nisu dirani; postojeće korisničke izmjene ostavljene su kakve jesu. U igri ostaje provjeriti dan/noć, oba zrcala, žmigavce i vožnju s teretom.

Datoteke za commit (samo ovaj zahvat):

```text
Assets/Scripts/Truck/CabinRig.cs
Assets/Scripts/Truck/MirrorCams.cs
Assets/Scripts/Truck/PlayerTruck.cs
Assets/Scripts/Truck/TruckController.cs
Assets/Scripts/Truck/TruckLights.cs
Assets/Scripts/Truck/TurnSignals.cs
Assets/Scripts/Truck/TurnSignals.cs.meta
Assets/Scripts/Audio/MagistralaAudio.cs
Assets/Resources/Vehicles/KombiL4H1/kombi-l4h1.glb
Assets/Resources/Vehicles/KombiL4H1/kombi-l4h1_lod1.glb
Assets/Resources/Vehicles/KombiL4H1/kombi-l4h1_lod2.glb
Tools/kombi-l4h1/README.md
Tools/kombi-l4h1/patch_unity.py
Tools/kombi-l4h1/finish_glb.py
Tools/kombi-l4h1/check_model_drive.py
Tools/kombi-l4h1/check_signals.py
docs/kabina-dorada.md
```
