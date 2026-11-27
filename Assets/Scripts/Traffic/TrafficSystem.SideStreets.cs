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
        const float SideCell = 512;
        static long SideKey(int x, int z) => ((long)x << 32) ^ (uint)z;

        void InitSides()
        {
            var args = System.Environment.GetCommandLineArgs();
            sideEnabled = true;
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-sporedne") sideEnabled = args[i + 1] != "0";
            sideCars.Clear(); nearSides.Clear(); sideGrid.Clear(); sideRefresh = sideSpawnAt = 0; sideLogAt = clock + 60;
            sideEntries = sideExits = sideBlocked = 0;
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
            foreach (var c in cars)
            {
                if (c == except) continue;
                int id = c.sideRoute >= 0 ? c.sideRoute : c.turnRoute;
                if (id < 0) continue;
                var r = data.sideRoutes[id];
                // Adjacent mouths at one crossroads share the same conflict space as well.
                if (r.site == route.site || (r.Point(r.inbound ? r.Length : 0) - route.Point(route.inbound ? route.Length : 0)).sqrMagnitude < 65 * 65) return true;
            }
            return false;
        }

        // A conservative reservation: four seconds AFTER the connector has been cleared. Checks both main-road directions
        // (and every lane), the truck even when it is not projected onto a corridor, and other active side routes.
        bool SideGap(Car car, TrafficSideRoute r)
        {
            if (SideBusy(r, car)) return false;
            float horizon = r.gapSeconds + (r.conflictEnd - r.conflictStart + car.len) / 3 + 2;
            bool Threat(Vector3 pos, float speed, float len)
            {
                for (float s = r.conflictStart; s < r.conflictEnd + 4; s += 4)
                {
                    var p = r.Point(s);
                    if (Mathf.Abs(pos.y - p.y) < 4 && Vector2.Distance(new Vector2(pos.x, pos.z), new Vector2(p.x, p.z)) < 10 + len * .5f + speed * horizon) return true;
                }
                return false;
            }
            foreach (var o in cars)
            {
                if (o == car) continue;
                // The following cars in our own lane already see this turn-in as their leader.
                if (!r.inbound && o.sideRoute < 0 && o.c == car.c && o.s < car.s) continue;
                if (Threat(o.pos, o.v, o.len)) return false;
            }
            float truckLen = 14;
            if (footLocal != null) truckLen = Mathf.Max(truckLen, Vector3.Distance(footLocal[0], footLocal[2]));
            bool truckFollowing = player && !r.inbound && pOn[car.c] && pS1[car.c] < car.s - car.len * .5f - 1
                && Vector3.Dot(player.forward, car.rot * Vector3.forward) > .5f;
            if (player && !truckFollowing && Threat(PlayerPos, playerSpeed, truckLen)) return false;
            return true;
        }

        float TurnLight(Car car, TrafficStop stop)
        {
            if (car.turnRoute >= 0)
            {
                var r = data.sideRoutes[car.turnRoute];
                if (r.signal != null && r.signal.plan == stop.plan && Mathf.Abs(stop.s - r.mainS) < 25) return LightAt(r.signal);
            }
            return LightAt(stop);
        }

        bool SideGreen(TrafficSideRoute r) => !signals || r.signal == null || LightAt(r.signal) == 0;

        void PickSideTurn(Car car)
        {
            if (!sideEnabled || car.turnRoute >= 0 || car.len > 6 || car.lane != car.laneTo) return;
            foreach (int id in nearSides)
            {
                var r = data.sideRoutes[id];
                float d = r.mainS - car.s;
                if (r.inbound || r.corridor != car.c || r.lane != car.lane || d < Mathf.Max(25, car.v * 3) || d > 130) continue;
                // One decision for this mouth; do not roll again at every 0.6 s decision tick.
                if (car.lastSideSite == r.site) continue;
                car.lastSideSite = r.site;
                if (rng.NextDouble() < .28 && !SideBusy(r)) { car.turnRoute = id; break; }
            }
        }

        void TurnApproach(Car car, ref float v0, ref float gap, ref float lv)
        {
            if (car.turnRoute < 0) return;
            var r = data.sideRoutes[car.turnRoute];
            float d = r.mainS - car.s;
            car.indicator = r.indicator;
            v0 = Mathf.Min(v0, Mathf.Sqrt(4 * Mathf.Max(0, d) + 9));
            if (!SideGreen(r) || (r.indicator < 0 ? !SideGap(car, r) : SideBusy(r, car)))
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
            }
            for (int i = sideCars.Count - 1; i >= 0; i--)
            {
                var c = sideCars[i]; var r = data.sideRoutes[c.sideRoute];
                float gap = 1e9f, v0 = 8.3f;
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
                        // Always come to rest at a side-road stop/yield before accepting a gap.
                        if (c.v < .4f && stop < 2 && SideGreen(r) && SideGap(c, r)) c.sideCommitted = true;
                        else if (c.v < .4f) sideBlocked++;
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
                float acc = Mathf.Clamp(Idm(c, v0, gap, c.v), -9, c.a);
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
            int id = nearSides[rng.Next(nearSides.Count)]; var r = data.sideRoutes[id];
            if (!r.inbound || SideBusy(r)) return false;
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
            bool atEnd = !r.inbound && c.sideS >= r.Length - c.len * .5f - 2;
            bool far = (c.pos - PlayerPos).sqrMagnitude > despawnDist * despawnDist;
            if (!atEnd && !far) return false;
            var eye = cam ? cam.transform.position : PlayerPos;
            if (cam && InGuardView(c.pos) && !HiddenSpawn(eye, c.pos, c.hw, c.h)) return false;
            if (atEnd) sideExits++;
            return true;
        }

        void SideStats()
        {
            if (clock < sideLogAt) return;
            Debug.Log($"[sporedne] enabled={(sideEnabled ? 1 : 0)} entries/min={sideEntries} exits/min={sideExits} blocked-merges/min={sideBlocked} active={sideCars.Count} nearby-routes={nearSides.Count} cars={cars.Count}/{targetCars}");
            sideEntries = sideExits = sideBlocked = 0; sideLogAt = clock + 60;
        }
    }
}
