# Petrol station island orientation

The builder now separates the canopy's pillar row from the drive-through direction. `canopy.a` describes the row `(cos a, sin a)` in game coordinates; islands run perpendicular to it, along `(-sin a, cos a)`. Island centres retain the 10 m spacing under the canopy pillars. The canopy's long axis follows that pillar row.

Islands, both ordinary and LPG dispensers, and end buckets turn together. Dispensers sit toward the two island ends, with brand fins outward and their fuel faces looking sideways into the lanes on both long sides. Shops retain their authored `faceX`/`faceZ` orientation. Existing fictional branding is retained.

## Site angles

| Site | `canopy.a` (radians) | Reason |
| --- | --- | --- |
| `ks-sjever` | 1.4556617916328947, unchanged | Corrected lanes run about 173.4°, alongside D8 (about 174.6°) and perpendicular to the shop front. |
| `ks-jug` | 1.4516480608226348, unchanged | Corrected lanes run about 173.2°, alongside D8 (about 174.2°) and perpendicular to the shop front. |
| `sucurac-d8` | 0.19969240836916427 → 1.7704887351640608 | Turn the canopy/pillar row 90° so the lanes run about 11.4°, alongside D8 (about 10.5°). |
| `kastela-2` | 1.5707963, unchanged | Corrected lanes run east–west, matching the job bay and nearby main road (about 177.6°). The short access connector turns into the forecourt. |
| `stafilic-d8` | -0.4887 → 1.0820963267948966 | Turn the canopy/pillar row 90° so the lanes run about 152°, alongside the access road/D8 (about 151–152°). The asphalt pad and LPG tank retain their road-aligned angles. |

Road directions above are approximate, modulo 180°, checked against nearby segments in the local source `graph.json`.

## Job bays and verification

The complete rotated island footprint, plus 0.4 m clearance, is checked against each job bay. Conflicting islands are replaced by a bay-aligned `otok kamioni` with a sideways-facing diesel dispenser. Its original canopy column and collider are omitted in both LODs, leaving the roof cantilevered. Mesh variants are generated deterministically; source GLBs remain intact. Each nearby fuel job also gets an island alongside its bay if one was not already added; this covers `kastela_pumpa_2`, whose bay is beyond the regular pillar row. Existing bay-width limits remain in effect.

C# 9 compilation passed using the standalone compiler and project assembly references. Static checks against the current GameWorld bays and GLB geometry passed for all five sites, including island clearance, truck-island separation, and column removal in both canopy LODs. Unity and Blender were not run.

In Unity, run **Magistrala/Benzinske: postavi** and check:

- Cars can enter, stop beside either fuel face, and pull through along each island; fins point toward island ends.
- Remaining pillars stand on islands. Sućurac and Štafilić have road-parallel lanes; inspect canopy edges, asphalt coverage, shop approaches, and LPG access.
- Both fuel jobs remain unobstructed, with a dispenser beside the truck. At the northern job, check the omitted column at both LOD distances and confirm there is no invisible collider.
- Run the menu twice and confirm identical placement and no duplicate truck islands.
