# Karta (M) — full-screen map

**In the game:** press **M**. The map of Kaštela opens and the game pauses, as it does for the phone (Tab). Press **M** or **Esc** to close it.

| Key / mouse | What it does |
|---|---|
| WASD / arrow keys | move the view (it speeds up the longer you hold the key; the cursor goes back to the centre) |
| mouse | the circular cursor follows the mouse; drag to pan |
| wheel, + / − (also numpad; the Croatian `-` key works) | zoom, anchored at the cursor |
| Enter / left click | set a waypoint at the cursor. It snaps to the nearest open road of the game graph. Enter or a click on an existing waypoint removes it |
| Tab | switch the style: flat printed-map look (default, `WorldMap.DefaultDark = false`) ↔ dark. The choice is remembered (PlayerPrefs `karta.tamna`) |
| hover | the marker's name (stops, pumps, warehouse, truck, waypoint); the top panel shows the street and place under the cursor |

The waypoint works like a job target. The GPS (minimap) draws the same yellow route there: `MagistralaGame.SetWaypoint` calls `RoadRouter.RouteTo`, which routes to a point on an edge. The HUD shows **Oznaka** and the road distance. The waypoint takes priority over the job's route while it is set. It clears when the truck gets within 25 m of it, and it is kept in the save (`SaveData.wpOn / wpX / wpZ`).

## How it is drawn

- **Baked** (`Magistrala/Karta: ispeci`, `Assets/Editor/KartaBaker.cs`; it also runs at the end of `Magistrala/Igra: podaci`, and before a player build if it is missing) into `Assets/Generated/Map/` (ignored by git):
  - `karta-podloga.png` (flat style) and `karta-podloga-tamna.png` (dark style): each is 6144 × 2560 px at 2 m/px over game x −10752…1536, z −8192…−3072. The build stores each as DXT1 with mips, about 10.5 MB, so about 21 MB for both.
    - **flat**: solid fills with no hillshade: a medium blue sea, a sand strip along the shore, two greens (woods and maquis clumps; fields, groves and garrigue), and light grey land.
    - **dark**: a blue sea with teal near the shore, a green that stays green, warm dark-tan dry ground, warmer high ground, and a soft hillshade.
    - Both are built from our terrain heights, the ground classes and `vegetacija-maska.png`.
  - `KartaZgrade.asset`: the town's building footprints as one flat mesh. These are the tile footprints with the orthophoto shift and the orthophoto-only houses, minus those under models, plus the package buildings and canopies.
  - `Resources/Karta.asset` (`KartaData`): the references above, the extent, and the place-name anchors.
- **Live** (`Assets/Scripts/Game/WorldMap.cs`):
  - Roads come from the game graph, simplified at two levels, in three classes (street / main road / D8).
  - The GPS route, the markers, and the place, street and stop names (the names appear when you zoom in), plus D8 shields.
  - Everything goes into one screen-sized half-float MSAA render texture, redrawn only when something changes. In this project IMGUI shows that texture gamma-encoded twice (GpsMap ran into the same thing). So every colour goes in linearised twice, and the ground textures are stored ×gain to keep the dark tones in 8 bits. Text and the UI are IMGUI in the HUD style (`HudStyle`).
  - While the map is closed, nothing is drawn. While it is open, the 3D camera's culling mask is 0.

## Tests

`-karta [m/px]` opens the map at start (metres per 1080p pixel, default 1.6). `-kartacentar gx,gz` sets the view centre (game frame). `-kartaoznaka gx,gz` sets a waypoint at start. `-kartastil tamna|svijetla` picks the style. Example:

    Magistrala -mute -savefile /tmp/x.json -karta 0.4 -kartacentar -7800,-4800 -shot /abs/karta.png -shotat 32
