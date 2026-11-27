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
