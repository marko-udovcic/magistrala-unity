# Phase 3 runtime skeleton

Default `-stream 0` leaves today's world loading unchanged. Runtime boot adds an idle scheduler/HUD without a scene edit. There is no Addressables dependency; this backend uses SceneManager additive scenes.

## Manifest / phase 1 contract

Produce `Assets/Generated/Cells/manifest.json`, then **ship a copy as `Assets/Resources/Cells/manifest.json`** (TextAsset). Runtime loads `Cells/manifest`; there is no filesystem fallback. No generated content is written by these runtime scripts.

Version 1 public fields, serialized with `JsonUtility.ToJson(manifest, true)`:

- Manifest: `version`, `exampleOnly`, `l0Size=512`, `l1Size=2048`, `worldBounds`, `l0[]`, `l1[]`.
- L0: `key=c_i_j`, `coordinate` (Vector2Int x/y = i/j), `bounds` (Bounds), `sceneName`, `triangleCount` (long), `rendererCount`, `blobs`.
- Blobs: `trees`, `parked`, `lamps`, `props`, `boats`: relative paths, empty when absent. Producers own their payload formats and loading.
- L1: `key=h_I_J`, `coordinate`, `bounds`, `sceneName`, `meshName`. Metadata only until phase 5; L2 stays outside this scheduler.

Use CellGrid for all coordinates/bounds/keys, including negative indices. L0 bounds cover a full 512 m square. JsonUtility writes Bounds as `m_Center`/`m_Extent`.

**Save every baked cell scene root inactive.** The scheduler owns all roots and enables/disables them individually; intentionally inactive children remain inactive. This prevents preload rendering/physics/Awake before the active ring. Keep roots few and small, with merged geometry and pre-cooked colliders. No player/camera/global bootstrap roots, persistent objects or roots moved between scenes. Invalid active roots are hidden and reported, but their Awake cannot be undone.

Prefer unique full scene asset paths in `sceneName`; include them in Build Settings. The integrator must remove their original always-loaded duplicates. Already resident manifest scenes are rejected. Empty sceneName supports data-only cells. Invalid/example manifests and missing build scenes leave streaming idle.

`Magistrala/Streaming: Bake cells (stub)` measures World renderer/terrain bounds (fallback: GameWorld nodes) and prompts for an example JSON destination, initially OS temp. No scene splitting, generated assets or build changes. `exampleOnly=true` prevents runtime use; scene paths are placeholders, blob paths/counts remain empty/zero. The real baker can call `BakeCellsMenu.CreateExample(Bounds)`.

## Public integration API / phase 2

`StreamingScheduler.Instance` exposes `Manifest`, `TryGetCell`, `IsCellActive`, `IsReady`, `InitialLoading`, `HasFailedCells`, `Status` and diagnostic properties. IsCellActive is updated after scheduling: every root enabled and applicable systems ready.

Implement `ICellDataSystem.LoadCell(Vector2Int)`, `UnloadCell(Vector2Int)`, `IsLoaded(Vector2Int)`. Register on the main thread with `RegisterDataSystem(this, CellDataKind.Trees)` (Parked/Lamps/Props/Boats or flags). Matching nonempty blob paths select cells. Default `All` receives every cell, including empty ones; inspector `dataSystems` also uses All. Existing systems are not modified here. Retry registration if Instance does not exist yet; runtime initialization order is unspecified. Registration before manifest initialization works.

Each system receives at most one mutation call per frame. LoadCell must enqueue bounded work; IsLoaded stays false until usable and must be cheap/allocation-free/nonthrowing. UnloadCell must cancel a pending load too. Calls are once per transition, without repeated LoadCell polling. Before UnregisterDataSystem, release/cancel the system's resources. Do not change registrations from callbacks. Throwing mutation callbacks report failure and remove that registration; the faulty implementation owns cleanup. Scene unload waits until unload callbacks have been issued, not until external GPU cleanup finishes.

Override `LookaheadSource`, or supply a component implementing `ILookaheadSource.WritePath(position, velocity, seconds, callerBuffer)`. Write an ordered polyline without allocating; return zero for velocity fallback. The built-in GameWorld/RoadRouter adapter predicts the straightest permitted exit, respecting closures/one-ways. It does not predict branches or a mission destination. Off-road/missing graph uses position + velocity. Fixed 256-point/256-hop limits bound traversal. Refresh: 0.5 s, sooner after reversal/swap/large displacement. Segment tests cover long edges. Player selection follows the driven truck, then chase target/DrivenTruck; camera heading does not affect requests.

## Policy and budgets

Serialized defaults: active 700 m, preload 1300 m, unload 1600 m, lookahead 30 s × actual velocity, corridor half-width 256 m, admission budget 1.5 ms, slow-frame threshold 20 ms. Distances are horizontal distance to cell bounds. Leaving active hides roots/releases data; scenes retain hysteresis. Lookahead adds scene preload/retention. Queued requests refresh each pass; in-flight Unity loads cannot be cancelled.

One scene load is gated at 0.9. Effective concurrency is capped at one even when serialized `maxL0LoadsInFlight` requests more; `maxScenesWaitingForActivation` is hard-capped at one. Holding activation stalls Unity's global async queue, so preload scenes complete activation through this gate, then remain hidden through their inactive roots. No unload is queued behind a gate; new loads wait for outstanding unloads. At most one scene unload starts per frame. Other loaders must coordinate with the same Unity queue.

At most one gate release or content root toggle per frame; no root activation on the frame scene integration completes. Admission uses elapsed realtime and the last unscaled frame interval. Each SetActive/callback and Unity's native scene integration are indivisible: the budget is an admission target, not a hard CPU cap. Sustained frames above 20 ms defer activation; tune the threshold for 30 Hz targets. Full-manifest scans and RoadRouter.Nearest can exceed the budget on larger maps; replace with spatial indexing/incremental prediction then.

After the truck is ready/placed, startup pauses scaled simulation behind a black overlay until requested scene rings and active data are ready. High backgroundLoadingPriority during startup; Low while driving. Startup bypasses the driving budget but preserves operation limits. No reusable world-loading-screen readiness API was found: an integrator can use InitialLoading/IsReady and disable `showInitialFade`. Errors release the pause and leave IsReady false. Coordinate unscaled gameplay/teleports and other timescale owners. Before a player exists, the overlay waits without pausing. Keep the scheduler alive for the session; disabling drains its gate/restores globals but is not a graceful scene/data shutdown API.

## Switches, metrics and limits

- `-stream 0|1`: default 0; 1 validates the packaged manifest and runs.
- `-streamhud`: show HUD; F8 toggles it in either mode.
- `[stream]` every 10 s: active/resident non-active (`pre`, including transitions), loads started in the window, loading/queue counts, last/lifetime-max root activation ms, budget hits, inactive-ahead metres, scene activation wall latency, initial/failure flags.

Root activation measures synchronous SetActive including Awake/OnEnable. Scene activation latency includes frame scheduling and is **not CPU time**. Budget hits count frames that exhaust/reject admission. The warning is ray entry into the nearest not-fully-active manifest cell in driving direction: target **>300 m at 130 km/h**. Infinity means no matching cell, not proof of world coverage. HUD text (4 Hz when visible), logs (10 s), registration, startup and scene transitions intentionally allocate; the steady scheduler uses fixed buffers/indexed collections. External systems must uphold the allocation contract.

No UnloadUnusedAssets calls. Scene unload does not guarantee immediate shared-asset reclamation. Later phases supply GPU memory caps, collider pools, PSO warmup, HLOD/dither, floating origin and teleport loading screens. There is no memory cap or automatic retry here.

Validation: standalone C# compiler against local Unity 6000.3 assemblies and Input System; real GameWorld/RoadRouter/HudStyle, minimal stand-ins for unimported vehicle/game manager types. Fifteen standalone geometry/manifest checks passed. No Editor/Player launched. Integrator still needs full project compilation and Player tests for activation cost, async queue progress, allocations, reversals/teleports and the speed warning.

## Integration in Magistrala (stream1, 2026-11-25)

- Phase 1 (default build): `ProbeBuild.Mac()` / `Win()` run `CellMerge.Bake()` (menu *Magistrala/Bake cells (merge + cell scenes)*,
  ~40 s) and build `Assets/Generated/Cells/Magistrala_cells.unity`: a copy of the main scene with Buildings / Roads / Tlo / Streets /
  Signs / Riva grouped per L0 cell, renderers merged per (cell, 256 m block, settings) with one submesh per material, all box /
  non-convex mesh colliders merged per (cell, physics material), LOD0 children collapsed onto their LODGroup, render-only meshes as
  non-readable copies. No runtime manifest: the scheduler stays idle. `MacPlain()` / `MacProfilePlain()` build the plain scene (A/B).
- Phase 3 (behind the switch): `ProbeBuild.MacStream()` / `MacProfileStream()` also split the copy into `Cells/Scenes/c_i_j.unity`
  (one inactive root per cell: what is drawn only near + every collider) and `Magistrala_stream.unity` (far-visible surfaces, ad
  boards, building far / box chunks stay), and write `Resources/Cells/manifest.json`. `-stream 1` streams; `-stream 0` (default)
  loads every cell at the start. Changes to the skeleton: time paused from the scheduler's Start (the truck fell through a road whose
  cell was not loaded yet), vehicles lifted onto their cell's ground once the rings are ready, catch-up pause after a teleport,
  budget measured from the mutation work, slow-frame deferral capped at 6 frames, path-AABB prefilter, HitchDiag notes.
- Tests: `-flykmh 130` (FlyTest: the truck kinematic along the autopilot path), `-apkmh K`, `-scenestats`, `-hitchdbg`.
