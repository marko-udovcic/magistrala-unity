# Štafilić 41–42: grade discontinuity

The defect is in the package mesh, not Unity's road builder. `WorldBuilder.Roads()` preserves GLB vertex heights, adds a model's `hfix` if present, and flips z. Neither 41 nor 42 has `hfix` or `pad`; their placements are `alt + 1.11 m`. `LaneProfile` supplies lane positions / road shader coordinates, not vertical leveling. The 25-11 import (`f838b4e`) deliberately reused the existing GLBs with `PRELEVELLED=1`; 42's library GLB is byte-identical to its raw mag-25-11 export. The preceding `7f4a3f9` import introduced 41's 5 m `levelEdge` patch, which matched elevations but did not match slopes.

The diagnosis is substantially correct. 42's last 5 m are nearly flat (one sampled track even falls slightly). Beyond the landing, 41 climbs about 7.3%. The qualification: 41's existing terminal slope varies across its triangles, approximately 2.1–5.7%, rather than a uniform 6.5–7.3% at the edge itself. Simply giving 42 a 3.5% grade would leave a slope mismatch there. Comparing actual common-edge positions confirms no metre-scale lateral displacement; the old 1.8 / 3.5 m figures should not be used to translate either model.

Heights below are metres in game / Unity vertical coordinates, including the common +1.11 m package datum offset. Samples follow parallel D409 tracks at package-frame x = −22 and −16 at the seam in **41's** frame (southbound and northbound carriageways). Positive distance runs north toward the bridge; negative distance lies in 42. They use triangle interpolation and the higher of asphalt / deck, not the 2 m height grid. Distances are package metres; the affine fit changes these by less than 0.2%.

| Distance (m) / model | Southbound before | Southbound after | Northbound before | Northbound after |
|---|---:|---:|---:|---:|
| -20.0 / 42 | 32.911 | 32.674 | 32.721 | 32.579 |
| -15.0 / 42 | 33.143 | 32.796 | 33.001 | 32.780 |
| -10.0 / 42 | 33.279 | 32.958 | 33.192 | 32.958 |
| -5.0 / 42 | 33.331 | 33.130 | 33.294 | 33.134 |
| -0.3 / 42 | 33.314 | 33.294 | 33.316 | 33.298 |
| 0.3 / 41 | 33.322 | 33.316 | 33.315 | 33.319 |
| 5.0 / 41 | 33.596 | 33.498 | 33.421 | 33.480 |
| 10.0 / 41 | 33.938 | 33.809 | 33.734 | 33.721 |
| 15.0 / 41 | 34.305 | 34.216 | 34.085 | 34.022 |
| 20.0 / 41 | 34.670 | 34.666 | 34.451 | 34.431 |

The two rows at ±0.3 m are 0.6 m apart: their approximately 21 mm height difference after the fix is the expected rise at 3.5%, not a step. Extrapolated to the same seam, the two tested tracks agree within 5 mm. Triangle-measured terminal slopes are approximately 3.50% on 41 and 3.50% on 42 at these tracks. Coarse triangles between the tracks can differ slightly (around 3.1% on the centre reference line of 41).

`level-4142.ts` regenerates the affected band from the immutable mag-25-11 source. It first reproduces 41's old edge height correction, holds 42 at 3.5% for its final 12 m, and uses a cubic height-and-slope transition back to the original at 30 m. On 41 it holds the first 5 m at the same grade and blends to the original at 20 m. Adjacent ground, paint and deck geometry receive the same vertical displacement; the correction fades laterally beside the road. Vertex x/z, topology and material layout are preserved. Triangle interpolation can extend the influence just beyond those vertex selection distances. The transition into 41's original climb necessarily has a higher grade than the 3.5% landing; inspect its ride in Unity.

The tool writes generated package GLBs using the existing GLB writer; no GLB was edited by hand. It starts from the library layout to retain unrelated old sign geometry, and resets the affected vertices from the immutable source on every run. The package GLB symlinks are unlinked before writing. `PRELEVELLED=1` remains required. `MODEL_UPDATE` recomputes the usual import dependency chain but publishes only the named models, retaining every unrelated JSON entry and GLB.

Verification:

- `node --test tools/world/krizanja/modelJunction.test.ts tools/world/krizanja/modelSquare.test.ts`: **463/463 passed** after import.
- Two new `level-4142.test.ts` checks test actual triangle heights and terminal slopes in both carriageways. The final combined run passed **465/465** after the repeat import.
- A second package generation produced byte-identical 41/42 GLBs; a second scoped import produced byte-identical GLBs and both JSON files.
- The captured baseline contains 92 files: **88 unchanged**, four generated files changed. Both JSON files retain **75/77 entries unchanged**; only 41/42 differ. All other GLBs, 14/47, all five paket-2511 JSON files and Marko's overrides are preserved. Run `verify-unchanged.py` below to reproduce this check.
- The broader `node --test tools/world/krizanja/*.test.ts` command cannot complete cleanly: `import-buildings.test.ts` fails to load the missing historical `/Users/macair/Documents/mag-11-12/krizanja3d/krizanja.json`. The 463 junction tests themselves pass. No historical package was fabricated or modified to bypass it.

Regenerate from the current BalkanDriver checkout (Node 24.21.0 and Python 3.13 were used; raw `~/Documents/mag-25-11` must remain available):

```sh
cd ~/Documents/BalkanDriver
python3 -I tools/world/krizanja/paket-2511.py /private/tmp/spoj4142-package
PRELEVELLED=1 MODEL_UPDATE=krizanje-41,krizanje-42 node tools/world/krizanja/import-model.ts /private/tmp/spoj4142-package
node --test tools/world/krizanja/modelJunction.test.ts tools/world/krizanja/modelSquare.test.ts tools/world/krizanja/level-4142.test.ts
python3 ~/Documents/MagistralaUnity-spoj4142/Tools/spoj4142/verify-unchanged.py ~/Documents/BalkanDriver
```

The package command rewrites `tools/world/krizanja/paket-2511/{krizanja,dionice,ceste_izvan_modela,tocke,drvoredi}.json` with identical bytes. Temporary package files live under `/private/tmp/spoj4142-package`; only 41/42 there are regular generated GLBs, the others remain library symlinks. The import changes the two library GLBs and the two JSON files listed below. No buildings import is needed.

In Unity, reopen **this clone** after generation to clear the static `WorldData.Models()` cache (it has no reload menu). Then run, in order:

1. **Magistrala → Build roads** (regenerates road meshes and colliders).
2. **Magistrala → Build terrain** (fits terrain to the changed surface and ground strip).
3. **Magistrala → Build traffic** (refreshes paths from the changed model data).
4. **Magistrala → Autopilot: osvježi stazu** if using the autopilot route for the drive check.
5. Save the scene locally and build / capture with your normal procedure. Generated Assets and scene changes stay out of the code commit.

Unity reads BalkanDriver's files directly through `W.BD` / `W.KRIZ`; there is no GLB Asset import menu to run. These menus change `Assets/Generated/*` and the scene locally; no ProjectSettings change is needed.

Screenshot pose, northbound toward 41, approximately 12 m before the seam in 42, at truck-driver eye height:

```text
gx,gz,h,yaw,pitch = -9600.900,-5013.751,2.3,329.919,-2
-campose "-9600.900,-5013.751,2.3,329.919,-2"
```

Here `h` is **above the road collider**, as implemented by `GpuSwitches`, not absolute elevation. The corresponding Unity horizontal position is x = −5400.900, z = −286.249. Yaw is Unity yaw; negative pitch looks uphill. The pose is calculated from model geometry and has not been visually verified.

BalkanDriver files changed (left **uncommitted** for your review):

- `tools/world/krizanja/paket-2511.py`
- `tools/world/krizanja/level-4142.ts` (new)
- `tools/world/krizanja/level-4142.test.ts` (new)
- `tools/world/krizanja/square.ts` (exports the existing levelEdge helper; no default behavior change)
- `tools/world/krizanja/import-model.ts` (optional scoped publication)
- `client/public/assets/models/krizanja/41-kastel-stafilic-C.glb` (generated)
- `client/public/assets/models/krizanja/42-kastel-stafilic-C.glb` (generated)
- `client/public/assets/world/split-kastela/model-junctions.json`
- `client/public/assets/world/split-kastela/junction-overrides-modeli.json`

Unity repository files: `Tools/spoj4142/{REPORT.md,before-hashes.json,verify-unchanged.py,commit-code.sh}`. No Unity builder change was needed. Run `Tools/spoj4142/commit-code.sh` to commit these explicit paths on `spoj4142`; `.git` is read-only in this environment. The script checks the branch and an empty index and does not commit BalkanDriver or generated Unity assets.

Untested: Unity compilation, the rebuild menus, visual lighting / normals after deformation, the screenshot pose, truck suspension / ride through the transition, and runtime AI / GPS behavior. No Unity process or player was run. The unavailable historical buildings-test input is the remaining test-environment issue.
