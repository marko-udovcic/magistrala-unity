using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.Traffic
{
    public partial class TrafficSystem
    {
        bool sideEnabled = true;
        readonly List<int> nearSides = new List<int>();
        readonly List<Car> sideCars = new List<Car>();
        readonly Dictionary<long, List<int>> sideGrid = new Dictionary<long, List<int>>();
        float sideRefresh, sideSpawnAt, sideLogAt;
        int sideEntries, sideExits, sideBlocked;
        struct SideConflict { public int corridor, lane; public float s, routeS; }
        List<SideConflict>[] sideConflicts;
        float[] sideNextEvent;
        float[] sideSpawnRetryAt;
        float sideDebugAt;
        string sideBlockReason;
        int sideBlockSource, sideBlockCorridor, sideBlockLane;
        float sideBlockDistance, sideBlockNeed, sideBlockSpeed, sideBlockStop;
        int sideTurnChoices, sideTurnStarts;
        const float SideCell = 512;
        static long SideKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        void InitSides()
        {
            var args = System.Environment.GetCommandLineArgs();
            sideEnabled = true;
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-sporedne") sideEnabled = args[i + 1] != "0";
            sideCars.Clear(); nearSides.Clear(); sideGrid.Clear(); sideRefresh = sideSpawnAt = 0; sideLogAt = clock + 60;
            sideEntries = sideExits = sideBlocked = 0;
            sideDebugAt = clock + 10; sideBlockReason = null;
            sideBlockSource = sideBlockCorridor = sideBlockLane = -1;
            sideTurnChoices = sideTurnStarts = 0;
            sideNextEvent = new float[32];
            sideConflicts = new List<SideConflict>[data.sideRoutes?.Length ?? 0];
            sideSpawnRetryAt = new float[sideConflicts.Length];
            for (int i = 0; i < sideConflicts.Length; i++) sideConflicts[i] = BuildSideConflicts(data.sideRoutes[i]);
            if (sideEnabled && data.sideRoutes != null)
                for (int i = 0; i < data.sideRoutes.Length; i++)
                {
                    var r = data.sideRoutes[i]; var p = r.Point(r.inbound ? r.Length : 0);
                    long key = SideKey(Mathf.FloorToInt(p.x / SideCell), Mathf.FloorToInt(p.z / SideCell));
                    if (!sideGrid.TryGetValue(key, out var list)) sideGrid[key] = list = new List<int>();
                    list.Add(i);
                }
            Debug.Log($"[sporedne] enabled={(sideEnabled ? 1 : 0)} routes={data.sideRoutes?.Length ?? 0} shared budget={targetCars}");
        }

        void RefreshSides()
        {
            if (!sideEnabled || clock < sideRefresh) return;
            sideRefresh = clock + 1;
            nearSides.Clear();
            int x = Mathf.FloorToInt(PlayerPos.x / SideCell), z = Mathf.FloorToInt(PlayerPos.z / SideCell);
            // Only the nine local spatial buckets; no scan over all mouths each frame.
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++)
                if (sideGrid.TryGetValue(SideKey(x + dx, z + dz), out var list))
                    foreach (int id in list)
                    {
                        var r = data.sideRoutes[id];
                        if ((r.Point(r.inbound ? r.Length : 0) - PlayerPos).sqrMagnitude < 650 * 650) nearSides.Add(id);
                    }
        }

        bool SideBusy(TrafficSideRoute route, Car except = null)
        {
            foreach (var c in sideCars)
            {
                if (c == except) continue;
                var r = data.sideRoutes[c.sideRoute];
                // Waiting on the side street and driving deep into it do not reserve the intersection.
                // Only a car that actually accepted a gap/started turning holds it until its rear clears.
                if (!SideTrafficRules.HoldsCrossing(c.sideCommitted, c.sideS, r.conflictEnd, c.len)) continue;
                if (r.site == route.site || (r.Point(r.inbound ? r.Length : 0) - route.Point(route.inbound ? route.Length : 0)).sqrMagnitude < 65 * 65) return true;
            }
            return false;
        }

        // Geometry is resolved once on enable. Runtime checks only the lanes crossed by this connector,
        // with signed arc distances: departing cars and the opposite side of a median are not approaching threats.
        List<SideConflict> BuildSideConflicts(TrafficSideRoute r)
        {
            var result = new List<SideConflict>();
            var centre = r.Point((r.conflictStart + r.conflictEnd) * .5f);
            for (int ci = 0; ci < data.corridors.Length; ci++)
            {
                var cor = data.corridors[ci]; int nearest = Nearest(ci, centre);
                int radius = Mathf.CeilToInt((r.conflictEnd - r.conflictStart + 15) / cor.step);
                for (int lane = 0; lane < cor.maxLanes; lane++)
                {
                    float best = 2.6f * 2.6f; SideConflict hit = default; bool found = false;
                    for (int i = Mathf.Max(1, nearest - radius); i < Mathf.Min(cor.axis.Length - 1, nearest + radius + 1); i++)
                    {
                        if (lane >= cor.lanes[i]) continue;
                        var p = Point(cor, i * cor.step, LaneOff(cor, i, lane));
                        for (float s = r.conflictStart; s <= r.conflictEnd + 1; s += 1)
                        {
                            float distance = (p - r.Point(s)).sqrMagnitude;
                            if (distance >= best) continue;
                            best = distance; found = true;
                            hit = new SideConflict { corridor = ci, lane = lane, s = i * cor.step, routeS = Mathf.Min(s, r.Length) };
                        }
                    }
                    if (found) result.Add(hit);
                }
            }
            // The endpoint is an exact, editor-validated handoff even if coarse conflict sampling missed it.
            if (!result.Exists(h => h.corridor == r.corridor && h.lane == r.lane))
                result.Add(new SideConflict { corridor = r.corridor, lane = r.lane, s = r.mainS, routeS = r.inbound ? r.Length : 0 });
            return result;
        }

        void SideBlock(string reason, Car car, TrafficSideRoute r, int ci = -1, int lane = -1,
            float distance = 0, float need = 0, float speed = 0, float stop = 0)
        {
            if (sideBlockReason != null) return; // first refused check in this ten-second interval, no formatting here
            sideBlockReason = reason; sideBlockSource = r.source;
            sideBlockCorridor = ci < 0 ? r.corridor : ci; sideBlockLane = lane < 0 ? r.lane : lane;
            sideBlockDistance = distance; sideBlockNeed = need; sideBlockSpeed = speed;
            sideBlockStop = stop != 0 ? stop : r.inbound ? r.yieldS - car.sideS - car.len * .5f : r.mainS - car.s;
        }

        float SideGapSeconds(Car car, TrafficSideRoute r, SideConflict hit)
        {
            // Four seconds at the merge, or enough time to clear a crossed lane. Do not add four seconds
            // to traversal time and then two more seconds as the old radial test did (often 12–18 s).
            float progress = car.sideRoute >= 0 ? car.sideS : 0;
            float remaining = Mathf.Max(0, hit.routeS - Mathf.Max(progress, r.conflictStart));
            float clear = (remaining + car.len) / 4 + 1;
            return car.sideCommitted ? Mathf.Max(1, clear) : Mathf.Max(r.gapSeconds, clear);
        }

        bool SideGap(Car car, TrafficSideRoute r)
        {
            if (SideBusy(r, car)) { SideBlock("reserved-crossing", car, r); return false; }
            int routeId = car.sideRoute >= 0 ? car.sideRoute : car.turnRoute;
            foreach (var hit in sideConflicts[routeId])
            {
                var cor = data.corridors[hit.corridor];
                float lat = LaneOffAt(cor, hit.s, hit.lane), seconds = SideGapSeconds(car, r, hit);
                foreach (var o in byCorridor[hit.corridor])
                {
                    if (o == car || (!r.inbound && o.c == car.c && o.s < car.s)) continue;
                    if (!Overlap(lat, 1.25f, o.lat, o.hw) &&
                        !(o.laneTo != o.lane && Overlap(lat, 1.25f, LaneOffAt(cor, o.s, o.laneTo), o.hw))) continue;
                    float front = hit.s - (o.s + o.len * .5f), rear = o.s - o.len * .5f - hit.s;
                    // Already downstream: only the physical space into which we merge matters, not its speed.
                    bool departing = rear > 0;
                    if (SideTrafficRules.GapClear(front, rear, car.len, o.v, seconds, out float distance, out float need)) continue;
                    SideBlock(departing ? "merge-space" : "approaching-car", car, r, hit.corridor, hit.lane, distance, need, o.v);
                    return false;
                }
                if (!player || footLocal == null || !pOn[hit.corridor] || pL1[hit.corridor] < lat - 2 || pL0[hit.corridor] > lat + 2) continue;
                bool following = !r.inbound && hit.corridor == car.c && pS1[hit.corridor] < car.s - car.len * .5f - 1;
                if (following) continue;
                float truckFront = hit.s - pS1[hit.corridor], truckRear = pS0[hit.corridor] - hit.s;
                bool truckDeparting = truckRear > 0;
                float truckDistance = truckDeparting ? truckRear : truckFront;
                // Wrong-way and straddling trucks remain obstacles; use velocity along this corridor,
                // rather than letting a truck on the other carriageway block a legal right turn.
                int k = Mathf.Clamp(Mathf.RoundToInt(hit.s / cor.step), 1, cor.axis.Length - 2);
                var tangent = (cor.axis[k + 1] - cor.axis[k - 1]).normalized;
                float along = playerRb ? Vector3.Dot(playerRb.linearVelocity, tangent) : 0;
                float truckNeed = truckDeparting ? 7 + car.len * .5f + Mathf.Max(0, -along) * seconds
                    : 6 + car.len * .5f + Mathf.Max(0, along) * seconds;
                if (truckDistance < truckNeed)
                {
                    SideBlock("truck", car, r, hit.corridor, hit.lane, truckDistance, truckNeed, along);
                    return false;
                }
            }
            return true;
        }

        float TurnLight(Car car, TrafficStop stop)
        {
            if (car.turnRoute >= 0)
            {
                var r = data.sideRoutes[car.turnRoute];
                if (HasSideSignal(r) && r.signal.plan == stop.plan && Mathf.Abs(stop.s - r.mainS) < 25) return LightAt(r.signal);
            }
            return LightAt(stop);
        }

        static bool HasSideSignal(TrafficSideRoute r)
        {
            // Unity inline serialization turns null TrafficStop into { plan:0, group:"" }.
            // A record is an actual signal only when it names a group.
            return r.signal != null && SideTrafficRules.HasSignalGroup(r.signal.group);
        }

        bool SideGreen(TrafficSideRoute r)
        {
            return !signals || !HasSideSignal(r) || LightFor(r.signal.plan, r.signal.group) == 0;
        }

        void PickSideTurn(Car car)
        {
            if (!sideEnabled || car.turnRoute >= 0 || car.len > 6 || car.lane != car.laneTo) return;
            foreach (int id in nearSides)
            {
                var r = data.sideRoutes[id];
                float d = r.mainS - car.s;
                if (r.inbound || r.corridor != car.c || r.lane != car.lane || !SideTurnWindow(car.v, d)) continue;
                if (car.lastSideSite == r.site && clock < car.sideRetryAt) continue;
                bool otherIntent = false;
                foreach (var o in cars)
                    if (o != car && o.turnRoute >= 0 && data.sideRoutes[o.turnRoute].site == r.site) { otherIntent = true; break; }
                // A plan does not reserve the intersection. A waiting inbound car must not suppress
                // the only turn-in chance; resolve the actual conflict at the stop/turn point instead.
                if (otherIntent) { car.sideRetryAt = clock + 1; car.lastSideSite = r.site; continue; }
                bool due = clock >= sideNextEvent[r.site] && (r.Point(0) - PlayerPos).sqrMagnitude < 300 * 300;
                car.lastSideSite = r.site; car.sideRetryAt = clock + 2;
                if (due || rng.NextDouble() < .28)
                { car.turnRoute = id; sideTurnChoices++; break; }
            }
        }

        static bool SideTurnWindow(float speed, float distance) => SideTrafficRules.TurnWindow(speed, distance);

        void SideEvent(TrafficSideRoute r) => sideNextEvent[r.site] = clock + 20 + (float)rng.NextDouble() * 20;

        void TurnApproach(Car car, ref float v0, ref float gap, ref float lv)
        {
            if (car.turnRoute < 0) return;
            var r = data.sideRoutes[car.turnRoute];
            float d = r.mainS - car.s;
            car.indicator = r.indicator;
            v0 = Mathf.Min(v0, Mathf.Sqrt(4 * Mathf.Max(0, d) + 9));
            bool green = SideGreen(r), busy = SideBusy(r, car);
            bool gapOk = green && !busy && (r.indicator >= 0 || SideGap(car, r));
            car.sideTurnWait = !green ? 1 : busy ? 2 : !gapOk ? 3 : 0;
            if (!gapOk)
            {
                float stop = d - car.len * .5f - 2;
                if (stop < gap) { gap = Mathf.Max(0, stop); lv = 0; }
            }
        }

        // Run after the main-corridor iteration so removing / adding cars cannot invalidate its sorted lists.
        void StepSides(float dt)
        {
            if (!sideEnabled) return;
            foreach (var c in cars)
            {
                if (c.turnRoute < 0 || c.sideRoute >= 0) continue;
                var r = data.sideRoutes[c.turnRoute];
                if (c.s < r.mainS) continue;
                byCorridor[c.c].Remove(c);
                c.sideRoute = c.turnRoute; c.turnRoute = -1; c.sideS = 0; c.sideCommitted = true;
                c.laneTo = c.lane; c.laneT = 0; sideCars.Add(c);
                sideTurnStarts++;
                SideEvent(r);
            }
            for (int i = sideCars.Count - 1; i >= 0; i--)
            {
                var c = sideCars[i]; var r = data.sideRoutes[c.sideRoute];
                float gap = 1e9f, lv = 0, v0 = 8.3f;
                // A changed truck speed / late main-road arrival can revoke the reservation while still behind the mouth.
                if (r.inbound && c.sideCommitted && c.sideS + c.len * .5f < r.conflictStart && clock >= c.sideCheckAt)
                {
                    c.sideCheckAt = clock + .3f;
                    if (!SideGap(c, r) || !SideGreen(r)) c.sideCommitted = false;
                }
                // Look ahead for bends: braking starts before the connector, not inside it.
                for (float s = c.sideS; s < Mathf.Min(r.Length, c.sideS + 25); s += 3)
                    v0 = Mathf.Min(v0, Mathf.Sqrt(r.speed[r.Sample(s)] * r.speed[r.Sample(s)] + 4 * (s - c.sideS)));
                if (r.inbound && !c.sideCommitted)
                {
                    float stop = r.yieldS - c.sideS - c.len * .5f;
                    if (stop < 15 && clock >= c.sideCheckAt)
                    {
                        c.sideCheckAt = clock + .3f;
                        // IDM's equilibrium is TWO metres from its virtual obstacle. Requiring <2 m
                        // could leave a stopped car on an unaccepted numerical boundary before SideGap even ran.
                        if (c.v < .4f)
                        {
                            bool accepted = false;
                            if (!SideTrafficRules.AtYield(c.v, stop)) SideBlock("not-at-yield", c, r, stop: stop);
                            else if (!SideGreen(r)) SideBlock("signal", c, r, stop: stop);
                            else accepted = SideGap(c, r);
                            if (accepted) c.sideCommitted = true;
                            else sideBlocked++; // one refused decision per 0.3 s, never per frame
                        }
                    }
                    if (!c.sideCommitted) gap = Mathf.Max(0, stop);
                }
                // Do not drive into the truck on a stub. Use its actual local footprint (not its corridor projection).
                if (player && footLocal != null)
                {
                    Vector3 lo = footLocal[0], hi = footLocal[2];
                    for (float s = c.sideS; s <= Mathf.Min(r.Length, c.sideS + c.len * .5f + 15); s += .5f)
                    {
                        var world = r.Point(s); var p = player.InverseTransformPoint(world);
                        if (Mathf.Abs(world.y - PlayerPos.y) > 4 || p.x < lo.x - c.hw - .5f || p.x > hi.x + c.hw + .5f || p.z < lo.z - 1 || p.z > hi.z + 1) continue;
                        gap = Mathf.Min(gap, Mathf.Max(0, s - c.sideS - c.len * .5f - 1)); break;
                    }
                }
                // A car whose finite street end remains visible waits there; it never extrapolates off the mapped street.
                if (!r.inbound) gap = Mathf.Min(gap, Mathf.Max(0, r.Length - c.sideS - c.len * .5f - 1));
                // Deep outbound cars no longer reserve the mouth, but remain leaders on their shared stub.
                // The two main-road approaches can have different connector lengths and the same finite endpoint.
                if (!r.inbound)
                    foreach (var o in sideCars)
                    {
                        if (o == c) continue;
                        var other = data.sideRoutes[o.sideRoute];
                        if (other.inbound || other.site != r.site || (other.Point(other.Length) - r.Point(r.Length)).sqrMagnitude > 36) continue;
                        float ahead = (r.Length - c.sideS) - (other.Length - o.sideS);
                        float distance = ahead - (o.len + c.len) * .5f;
                        if (ahead > 0 && distance < gap) { gap = Mathf.Max(0, distance); lv = o.v; }
                    }
                float acc = Mathf.Clamp(Idm(c, v0, gap, c.v - lv), -9, c.a);
                float nv = Mathf.Max(0, c.v + acc * dt), ds = (c.v + nv) * .5f * dt;
                ds = Mathf.Min(ds, Mathf.Max(0, gap - .3f));
                c.v = Mathf.Min(nv, ds / Mathf.Max(.001f, dt)); c.sideS += ds; c.wheelAng += ds;
                c.braking = acc < -1.2f || c.v < .5f;
                c.indicator = c.sideS > r.conflictEnd + c.len ? 0 : r.indicator;
                c.stopT = c.v < .3f ? c.stopT + dt : 0;
                if (r.inbound && c.sideS >= r.Length)
                {
                    // Reservation has kept main-road followers behind our handoff point throughout the connector.
                    c.s = r.mainS; c.lane = c.laneTo = r.lane; c.lat = LaneOffAt(data.corridors[c.c], c.s, c.lane);
                    c.sideRoute = -1; c.sideCommitted = false; c.indicator = 0; c.lastSideSite = r.site;
                    byCorridor[c.c].Add(c); sideCars.RemoveAt(i); sideEntries++;
                    SideEvent(r);
                }
            }
        }

        // Main-road IDM also sees side cars while their body or reserved merge occupies this lane.
        void SideLeader(Car car, float lat, float hw, ref float gap, ref float lv)
        {
            foreach (var c in sideCars)
            {
                var r = data.sideRoutes[c.sideRoute]; if (r.corridor != car.c) continue;
                var cor = data.corridors[car.c];
                int k = Mathf.Clamp(Mathf.RoundToInt(r.mainS / cor.step), 1, cor.axis.Length - 2);
                float s, lateral;
                if (r.inbound && c.sideCommitted)
                {
                    s = r.mainS; lateral = LaneOffAt(cor, s, r.lane);
                }
                else
                {
                    var delta = c.pos - cor.axis[k]; var t = (cor.axis[k + 1] - cor.axis[k - 1]).normalized;
                    s = r.mainS + Vector3.Dot(delta, t); lateral = Vector3.Dot(delta, cor.right[k]);
                }
                float d = s - car.s - (c.len + car.len) * .5f;
                if (s > car.s - car.len * .5f && Overlap(lat, hw, lateral, c.hw, .5f) && d < gap)
                { gap = Mathf.Max(0, d); lv = r.inbound ? 0 : c.v; }
            }
        }

        void SidePose(Car c)
        {
            var r = data.sideRoutes[c.sideRoute];
            var p = r.Point(c.sideS); var tangent = r.Point(c.sideS + 1) - r.Point(c.sideS - 1);
            c.pos = p;
            if (tangent.sqrMagnitude > .00001f) c.rot = Quaternion.LookRotation(tangent, Vector3.up);
        }

        int MainSpawnBudget => sideEnabled && nearSides.Count > 0 ? Mathf.Max(0, targetCars - Mathf.Min(4, targetCars / 4)) : targetCars;

        bool TrySideSpawn()
        {
            if (!sideEnabled || clock < sideSpawnAt || nearSides.Count == 0 || sideCars.Count >= 6 || cars.Count >= targetCars) return false;
            sideSpawnAt = clock + 3;
            int id = -1; float nearest = float.MaxValue;
            // Prefer an overdue, close mouth instead of wasting half the attempts on outbound-only paths.
            int start = rng.Next(nearSides.Count);
            for (int n = 0; n < nearSides.Count; n++)
            {
                int candidate = nearSides[(start + n) % nearSides.Count]; var route = data.sideRoutes[candidate];
                if (!route.inbound || clock < sideSpawnRetryAt[candidate] || SideBusy(route)) continue;
                bool occupied = false;
                foreach (var c in sideCars) if (data.sideRoutes[c.sideRoute].site == route.site) { occupied = true; break; }
                if (occupied) continue; // one inbound per mouth; stub following is not a full network simulation
                float score = (route.Point(route.Length) - PlayerPos).sqrMagnitude;
                if (clock < sideNextEvent[route.site]) score += 650 * 650;
                if (score < nearest) { nearest = score; id = candidate; }
            }
            if (id < 0) return false;
            var r = data.sideRoutes[id];
            sideSpawnRetryAt[id] = clock + 12; // a visible/too-close endpoint must not starve all the other mouths
            int kind = PickKind(); var kd = data.kinds[kind]; if (kd.len > 6) return false;
            var pos = r.Point(kd.len * .5f + 1);
            var eye = cam ? cam.transform.position : PlayerPos;
            float distance = Vector3.Distance(pos, eye);
            // Exact corridor-entry policy, including guard band, roof +2 m / future eye and the 25 m near-eye exclusion.
            bool inView = cam && InGuardView(pos);
            bool hidden = cam && HiddenSpawn(eye, pos, kd.halfW, kd.h);
            if (distance < nearEntry || (inView && !hidden)) return false;
            foreach (var c in cars) if ((c.pos - pos).sqrMagnitude < 35 * 35) return false;
            if ((PlayerPos - pos).sqrMagnitude < 40 * 40) return false;
            var car = new Car { c = r.corridor, kind = kind, len = kd.len, hw = kd.halfW, h = kd.h,
                a = kd.a, b = kd.b, T = kd.T, v0 = 70 / 3.6f * kd.vMul, sideRoute = id,
                sideS = kd.len * .5f + 1, lane = r.lane, laneTo = r.lane, s = r.mainS,
                born = clock, bornD = distance, how = 4, wasHid = hidden, indicator = r.indicator };
            float total = 0; foreach (var w in kd.paintW) total += w;
            float pick = (float)rng.NextDouble() * total;
            for (int i = 0; i < kd.paintW.Length; i++) { pick -= kd.paintW[i]; if (pick <= 0) { car.paint = i; break; } }
            car.beacon = kd.beaconShare > 0 && rng.NextDouble() < kd.beaconShare; car.beaconPh = (float)rng.NextDouble();
            car.rb = Acquire(kd); SidePose(car);
            car.rb.position = car.pos; car.rb.rotation = car.rot; car.rb.transform.SetPositionAndRotation(car.pos, car.rot);
            cars.Add(car); sideCars.Add(car); Spawned++;
            return true;
        }

        bool SideGone(Car c)
        {
            var r = data.sideRoutes[c.sideRoute];
            // A finite endpoint can recycle only off-view/behind cover, using the conservative spawn test.
            bool atEnd = !r.inbound && SideTrafficRules.AtEnd(r.Length, c.sideS, c.len);
            bool far = (c.pos - PlayerPos).sqrMagnitude > despawnDist * despawnDist;
            if (!atEnd && !far) return false;
            var eye = cam ? cam.transform.position : PlayerPos;
            if (cam && InGuardView(c.pos) && !HiddenSpawn(eye, c.pos, c.hw, c.h)) return false;
            if (atEnd) sideExits++;
            return true;
        }

        void SideStats()
        {
            if (sideEnabled && clock >= sideDebugAt)
            {
                sideDebugAt = clock + 10;
                string turn = "no-nearby-exit", detail = ""; float nearest = float.MaxValue;
                foreach (int id in nearSides)
                {
                    var r = data.sideRoutes[id]; if (r.inbound) continue;
                    float mouthDistance = (r.Point(0) - PlayerPos).sqrMagnitude;
                    foreach (var c in byCorridor[r.corridor])
                    {
                        float d = r.mainS - c.s;
                        float score = mouthDistance + d * d + (d < -c.len ? 1000000 : 0);
                        if (score >= nearest) continue;
                        nearest = score;
                        turn = c.turnRoute >= 0 ? c.sideTurnWait == 1 ? "planned-signal" : c.sideTurnWait == 2 ? "planned-reservation"
                            : c.sideTurnWait == 3 ? "planned-opposing-gap" : "planned-approaching" : c.len > 6 ? "vehicle-too-long"
                            : c.lane != r.lane ? "lane-mismatch" : c.lane != c.laneTo ? "lane-change"
                            : !SideTurnWindow(c.v, d) ? "outside-window" : c.lastSideSite == r.site && clock < c.sideRetryAt ? "retry-delay" : "eligible";
                        detail = $" source={r.source} c{c.c}/L{c.lane}->L{r.lane} d={d:F1}m window={Mathf.Max(12, c.v * c.v / 6 + 8):F0}..{Mathf.Max(120, c.v * 6 + 30):F0}m v={c.v:F1}m/s s={c.s:F1} mouthS={r.mainS:F1} plan-source={(c.turnRoute >= 0 ? data.sideRoutes[c.turnRoute].source : -1)}";
                    }
                }
                Debug.Log($"[sporedne] debug first-block={sideBlockReason ?? "none"} source={sideBlockSource} c{sideBlockCorridor}/L{sideBlockLane} distance={sideBlockDistance:F1}m need={sideBlockNeed:F1}m v={sideBlockSpeed:F1}m/s stop={sideBlockStop:F2}m; nearest-turn={turn}{detail} choices/10s={sideTurnChoices} starts/10s={sideTurnStarts}");
                sideBlockReason = null; sideTurnChoices = sideTurnStarts = 0;
                sideBlockSource = sideBlockCorridor = sideBlockLane = -1;
                sideBlockDistance = sideBlockNeed = sideBlockSpeed = sideBlockStop = 0;
            }
            if (clock < sideLogAt) return;
            Debug.Log($"[sporedne] enabled={(sideEnabled ? 1 : 0)} entries/min={sideEntries} exits/min={sideExits} blocked-merges/min={sideBlocked} active={sideCars.Count} nearby-routes={nearSides.Count} cars={cars.Count}/{targetCars}");
            sideEntries = sideExits = sideBlocked = 0; sideLogAt = clock + 60;
        }
    }

    // Pure decision boundaries, shared with the standalone regression harness (no Unity process needed).
    internal static class SideTrafficRules
    {
        public static bool HasSignalGroup(string group) => !string.IsNullOrEmpty(group);
        public static bool AtYield(float speed, float bumperGap) => speed < .4f && bumperGap <= 3;
        public static bool AtEnd(float length, float s, float carLength) => length - s - carLength * .5f - 1 <= 3;
        public static bool HoldsCrossing(bool committed, float s, float conflictEnd, float carLength) =>
            committed && s <= conflictEnd + carLength + 3;
        public static bool TurnWindow(float speed, float distance) => distance >= System.Math.Max(12, speed * speed / 6 + 8)
            && distance <= System.Math.Max(120, speed * 6 + 30);
        public static bool GapClear(float front, float rear, float carLength, float speed, float seconds,
            out float distance, out float need)
        {
            bool departing = rear > 0;
            distance = departing ? rear : front;
            need = departing ? 6 + carLength * .5f : 5 + carLength * .5f + speed * seconds;
            return distance >= need;
        }
    }
}
