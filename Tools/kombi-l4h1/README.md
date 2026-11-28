# L4H1 box van for Unity (27. 11. 2026)

The GLBs in `Assets/Resources/Vehicles/KombiL4H1/` were built from `BalkanDriver/assets-src/kombi-l4h1/` (k2_build.py + k3_export.py)
with these additions for the game (patches against those scripts):
- `patch_unity.py` (run from k2_build.py before the body join): wipers split out as `wiper_L` / `wiper_R` (spindle empties; glTF
  local X = parked arm direction, Y = up the glass, Z = into the cab; WindscreenFx sweeps them), `windscreen_fx` (inner skin of
  the windscreen 4 mm into the cab, UV left→right / bottom→top: the rain FX surface of the cab view)
- `k2_build.diff`: the box's closed rear wall (isoBox faces behind the swing doors) removed; glass split out of `body` / `door_L` /
  `door_R` into `cab_glass`, `door_L_glass`, `door_R_glass` (all-transparent renderers: hidden in the cab view by WindscreenFx)
- `k3_export.diff`: LOD targets for the new glass / windscreen nodes

Rebuild: copy k-src.blend, tex/ and scripts/ of assets-src/kombi-l4h1 to a work folder, apply the diffs, put patch_unity.py into
scripts/, run `Blender -b --python scripts/k2_build.py` then `Blender -b --python scripts/k3_export.py`, copy the three GLBs.

Cab finishing pass (required after each export, **before copying the GLBs**):

```sh
python3 /path/to/MagistralaUnity-kombi/Tools/kombi-l4h1/finish_glb.py /work/kombi-l4h1.glb /work/kombi-l4h1_lod1.glb /work/kombi-l4h1_lod2.glb
```

Without arguments it updates the three GLBs in this checkout. Uses only Python's standard library; no Blender needed for this
pass. It sets the absolute eye (10 cm higher, 6 cm back), lowers the steering node 8 cm and tilts it 10 degrees flatter,
retains emissive import variants, and appends a closed aluminium rear threshold (x ±1.07, y .778–.818, z −3.47…−3.36 m).
The threshold overlaps the old floor end at −3.402 and bridges the door seam at −3.44.
It also skins the rear swing doors: the source doors (`furgon003/004`) are only rims, their skin was the isoBox rear wall that
`k2_build.diff` removes, so without this pass the doors are see-through frames. Each `door_rear_L/R` mesh gets a Box_White
primitive (outer quad facing the rear, inner quad facing the box, inside the rim's 3.4 cm lip), and `box_liner` (a flipped
copy of the body's Box_White faces) lines the inside of the box (floor, walls, roof) for the open-door view. Embedded textures and existing meshes,
node order and buffer offsets stay intact. Repeated runs are byte-identical. The threshold is appended **after** LOD decimation,
so it survives all three LODs; `patch_unity.py` also keeps the source cab anchors consistent.

Offline checks (never launch Unity):

```sh
python3 Tools/kombi-l4h1/check_model_drive.py
python3 Tools/kombi-l4h1/check_signals.py
```

The first checks GLB buffer bounds, closed threshold faces/winding, default-view sightlines through the actual steering mesh,
finishing-pass idempotence, engine power and a scalar longitudinal estimate from VanSpec. That estimate excludes PhysX tyre
slip, wheel damping and suspension. The second extracts the actual C# signal-state methods and runs 16 muted checks with the
installed Editor's bundled Mono compiler/runtime (plain executables, not the Unity Editor). Driving/rendering still need a
manual in-game check. Signals: comma/period left/right, backslash hazards; Q/E remain drone/interaction controls.
