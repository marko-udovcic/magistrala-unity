GPS detour reproduction
=======================

Run from any directory, without Unity or Python dependencies:

    python3 Tools/gps/repro.py

Use `--region /path/to/split-kastela` if the read-only BalkanDriver data lives
elsewhere. The script reads the checked-in runtime asset, not an approximation
of the graph before outside streets, package splits and reattached stops. It
also replays the source graph's override ordering, entry replacement, anchor
signature checks and atomic patches (95 applied, 2 skipped), and reads the
213 IDs in road-closures.json. Baked `closed` flags already include those
closures and edge-own overrides. No generated JSON or asset is edited.

Cause and correction
--------------------

`junction-overrides-modeli.json`, entry `graph-node-487#model:dionica-s11`,
adds the north Lindlarska arm **-7000000096509**, length 19.95 m, as
`closed: true`. The same entry adds open continuation **-7000000096516**
from that arm's terminal node **-7000000096006** to node **486**, towards
Branimirova/Bardejovska and the warehouse. These are already shared nodes;
the connection is blocked by the arm's own closure. The arm is absent from
road-closures.json. Its baked indices are 1629 and 1636 in this checkout.
Start snapping is unchanged by the repair (warehouse spur 1018, yard spur
1794); neither a direction error nor snapping causes this detour.

GameDataBuilder corrects only those producer IDs, after validating the shared
node, street names and two-way flags and the open continuation. An explicit
file closure prevents the correction. The correction applies in both Build
and MergedGraph. StreetsBuilder uses the same check so it no longer generates
a works barrier on this continued arm. Other closed model arms remain closed.

The Python router ports Nearest, Search, Assemble, Route and RouteTo, including
8 m wrong-heading snapping penalty, directed adjacency, edge.length costs
(fallback to endpoint 3D distance), 240 m U-turn costs, both seeded endpoints,
and RouteTo's same-edge shortcut and partial goal-edge traversal. RoadRouter
has no additional turn-restriction table or A*: it runs node-based Dijkstra.
Distances below are the resulting 3D polylines; turn penalties only influence
path choice. Starts are bay centres facing away from bayDir, as PlaceAtStop
does. RouteTo goals snap bay centres with a zero heading. Route instead uses
the stop's pad/access node and bay tail.

Measured metres (original asset -> simulated corrected build):

| Stop pair | RouteTo | Route |
|---|---:|---:|
| kastela_skladiste -> dvor_basica | 1384.4 -> 1113.1 | 1390.2 -> 1118.9 |
| dvor_basica -> kastela_skladiste | 3501.5 -> 1111.8 | 3512.0 -> 1122.3 |
| kastela_kafana_2 -> kastela_kafana | 1040.0 -> 1040.0 | 1045.8 -> 1045.8 |
| ducan_stari -> kastela_pumpa | 2983.7 -> 2983.7 | 2984.0 -> 2984.0 |
| kastela_pumpa -> kastela_pumpa_2 | 8069.6 -> 8069.6 | 8075.8 -> 8075.8 |

The three additional reachable pairs are sampled with seed 20261122. Assertions
check finite routes, unchanged start snapping and no distance regression for
both APIs. The reported ~5.2 km does **not** reproduce from these bay poses in
this checkout; a saved truck position/heading, map click or different built
asset can give different numbers.

Applying the builder fix
------------------------

Unity was not opened, and the generated runtime asset remains unchanged.
When next using the Editor, run **Magistrala/Igra: podaci (stajališta, graf
cesta)**. It will regenerate `Assets/Generated/Game/Resources/MagistralaGame.asset`
with the Lindlarska arm open, restoring the roughly 1.11 km link in both
directions; Build also rebakes the map. Merely building a player with an
existing asset does not regenerate it. Re-run **Magistrala/Build streets**
if existing street geometry contains the old works barrier; that rebuild is
left to the owner because this task must not touch scenes or open Unity.
