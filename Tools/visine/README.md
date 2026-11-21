# Visine zgrada iz sjena na ortofotu (Tools/visine)

Run from a scratch folder with `ZGRADE_PYLIB=<pip --target folder> python3 -I <this folder>/<script>.py`; outputs (json) go to the
current folder. Inputs as Tools/zgrade (tiles, DGU DOF LiDAR 2022/23 mosaic) plus `ZGRADE_AUDIT` = Tools/zgrade's audit.json
(roof offsets) and the cached Overpass responses in BalkanDriver/tools/world/dgu/cache (OSM building:levels for the check).

1. `treeaz.py` – shadow direction from trees (vegetation ↔ dark cross-correlation per 512 m quadrant) → az(x) used below
2. `runall4.py` → shadows4.json (columns along the shadow direction: change-point fit of the darkness profile)
3. `edgeall.py` → edges.json (profiles perpendicular to each sun-facing-away edge: steepest climb after the shadow core)
4. `osmref.py` → osmref.json (tile footprints matched to OSM ways with building:levels / height, IoU ≥ 0.5)
5. `final.py 1.6 --write ../../Assets/Editor/visine-zgrada.json` (tan of the sun elevation; storeys, confidence, source)
6. `viz2.py` / `viz.py` contact sheets (footprint cyan, measured shadow end magenta)

Method, numbers and limits: docs/visine-zgrada.md.
