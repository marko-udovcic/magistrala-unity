# Zgrade s ortofota (audit + tablica za Unity)

Run from this folder with `python3 -I` (`ZGRADE_PYLIB=<pip --target folder>` for numpy, Pillow, shapely, pyproj); outputs go to
the current folder. Inputs: tile footprints (`BalkanDriver/client/public/assets/world/split-kastela/tiles`), the DGU DOF LiDAR
2022/23 mosaic (`BalkanDriver/tools/world/dgu/cache/dof/mosaic-lidar`, Tools/dof/dof_mosaic.py), the cached DGU cadastre WFS
responses and `osm-corridor-original.json` (BalkanDriver/tools/world/dgu).

1. `audit.py` -> audit.json (per footprint: best shift, coverage, pixel classes, roof class, colours, solar)
2. `missing.py` then `missfeat.py` -> missing.json (terracotta roofs with no footprint in the game, OSM, cadastre or package)
3. `export.py ../../Assets/Editor/zgrade-ortofoto.json` -> the table BuildingsFromTiles reads (OrthoRoofs.cs)
4. `sheet.py "<lambda r: ...>" out.png` contact sheets for checking

Results and rules: docs/audit-zgrade.md.
