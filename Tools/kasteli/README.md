# Kašteli — Blender modeli (landmarks)

Proceduralni modeli kaštela iz `BalkanDriver/tools/world/riva-extras/kasteli.json` (OSM ODbL / DGU tlocrti, procjene visina)
i slobodnih referentnih fotografija (samo za pogled, nisu u repou). Izvornik skripti: `BalkanDriver/tools/world/kasteli-blender/`.

    ~/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup -P kastilac.py   # cambi, vitturi, novi (+ zvonik), cipiko_stari, stafileo
    python3 postava.py                                                                      # -> Assets/Editor/kasteli-postava.json

Izlaz: `BalkanDriver/assets-src/kasteli/<ime>/` (`<ime>.glb` s teksturama, `<ime>-unity.glb` bez njih, `izvjestaj.md`),
renderi `~/Desktop/Magistrala-slike/kasteli/model-<ime>-*.png`. U Unity kopirati `<ime>-unity.glb` -> `Assets/Models/Kasteli/<ime>.glb`,
pa `Magistrala/Kašteli: postavi` (`Assets/Editor/Kasteli.cs`). Kuće pod kaštelima izostavlja `Magistrala/Build tile buildings`
(`Kasteli.Excluded`, poligoni i `excludeIds` u kasteli-postava.json). `teksture.py`: Poly Haven CC0 -> `Assets/Textures/Kasteli`.
