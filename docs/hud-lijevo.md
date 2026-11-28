# Left driving HUD

Replaced the separate minimap and bottom-right gauges with one rounded, translucent left column, inspired by the supplied 119/120 references. Its 360 × 744 layout scales with screen height (1080p baseline), with a taller heading-up minimap. All icons are original procedural textures; there is no smiley. Rendering remains Repaint-only IMGUI with cached styles/text and no new steady-frame string construction.

| Row | Data |
| --- | --- |
| Wallet / `7d:` | `Cash`, Croatian euro formatting; `SevenDayEarnings` sums saved `deliveredJobs` payouts for today and the previous six game days, including cash and invoiced deliveries once each. Cached until the day, history count or save object changes; no save migration. |
| Truck / damage bar | `CargoDamagePct`, with orange/red damage thresholds. |
| Thumbs-up / deliveries | `ReputationPct` uses existing customer standing (`S.fatRep`, 0–1), displayed as 0–100%; `Delivered` supplies the person count. The dispatcher's signed story score (`S.rep`) has no percentage scale. |
| Destination / due / status / clock | GPS `DestName`; the same `Current.startMin + deadlineMin` used by `JobCard`; `ClockMin` and Monday-based day numbering. Deadline overrun turns `kasniš` orange; otherwise `na vrijeme` is green. Free driving has no deadline. |
| Speed / limit | Active driven truck's `SpeedKmh`, gear/handbrake; `LocationLabel.SpeedLimit`, red-rim sign and orange overspeed. |
| Map | Existing `GpsMap.Picture`, roads, route and target behavior. |
| Arrival / duration / distance | Approximate arrival at the current GPS target, assuming 40 km/h along `RouteMetres`, converted through `ClockRate`; duration in game minutes (hours when needed), distance in metres/km. Day rollover is included. No route shows `Bez rute`. This estimates the current leg/waypoint, excluding later stops and unloading. |
| Vehicle strip | `TurnSignals.LeftLit/RightLit` synchronize green blinks, including hazards; `TruckLights.IsOn` exposes actual automatic/manual dipped beams; `FuelPct` drives 12 segments and a percentage, orange below 20%, red below 12%. No high-beam mode exists. |

Job/help cards remain on the right. The phone keeps its bottom-right position; opening it hides that stack and leaves the column visible. Incoming calls avoid the right stack; location labels avoid the column. Full-screen map and `-nohud` suppression remain. Scenes and ProjectSettings were not edited.

Validation: standalone Unity 6000.3.28f1 Roslyn C# 9 compilation of runtime and editor sources against project references succeeded (four existing unused-member/local warnings). Temporary response files/logs: `/private/tmp/hud-left-{runtime,editor}.{rsp,log}`. Extracted getter checks cover seven-day boundaries, locale variants, malformed/non-finite history, delivery/day/save cache invalidation and reputation clamping; 10,000 cached reads allocate zero bytes. Unity was not launched.

Visual checks in Unity: 1080p and scaled resolutions; long destination names and large balances; no job, multi-stop and fatureta jobs; late status and midnight rollover; minimap framing/route, waypoint ETA and speed-limit changes; both indicators/hazards; L toggle, sunset/rain lights; empty/low/full fuel; vehicle switches; phone open/incoming call; F1/startup hints; full-screen map and `-nohud`.
