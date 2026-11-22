// Rekviziti: postavi, the town ground (docs/audit-stari.md) — benches by the pitches and in the parks, parked cars:
//   pitch benches  two team benches on a long side of every goal pitch ≥ 40 m, between the touchline and the fence, facing it
//   park benches   OSM leisure=park: every ~22 m of the outline, 2.5 m inside it, facing in (at most 8 a park)
//   parked cars    the traffic kinds (TrafficData: passenger cars and vans, no buses / trucks / beacons), static, one BoxCollider each:
//                  * car parks: the ground build's occupied bays (~60 %, OSM amenity=parking with bay lines)
//                  * Mapillary car parks (GroundCoverMapillary.cs, CC BY-SA 4.0): the lots the street photos saw cars in and
//                    OSM has no car park for, nose-in bays packed on their standing room, 45–80 % taken; a lot with
//                    fewer than 3 bays or 3 cars is left out (GroundCover.MlyFinish builds the asphalt of the rest)
//                  * the village streets: on the verge beside our streets (not the D8, no model road) in built-up land (OSM
//                    landuse=residential), parallel 1.45 m off the asphalt edge, in runs (noise along the street) — a spot only
//                    on the verge or up on our footway (not paving / the riva / a wall), flat, clear of gates (3.2 m), stop bays, junctions
//                    (another street within its half width + 5.5 m), props, pitches / pools; never on a carriageway
//                  * driveways: behind ~40 % of the yard gates (Yards: yard-spots.json), 3–5 m in, along the gate's axis
//                  runtime: Magistrala.Traffic.ParkedCars (instanced like the traffic)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Magistrala.Traffic;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static partial class PropsBuilder
    {
        static void PitchBenches()
        {
            if (!info.TryGetValue("klupa-bijela", out var inf)) return;
            var g = Group("teren-klupe"); int n = 0;
            foreach (var p in GroundCover.Pitches)
            {
                if (!p.Goals || p.A < 40) continue;
                foreach (int sb in new[] { -1, 1 })
                {
                    bool ok = false;
                    foreach (double a in new[] { 6.0, -6.0 })
                    {
                        // between the touchline (B/2 - 1) and the fence (B/2 + 1.5)
                        double b = sb * (p.B / 2 + .35);
                        var pos = W.U(p.cx + p.ux * a - p.uz * b, 0, p.cz + p.uz * a + p.ux * b);
                        var c = W.U(p.cx + p.ux * a, 0, p.cz + p.uz * a);
                        var face = Quaternion.LookRotation(new Vector3(c.x - pos.x, 0, c.z - pos.z).normalized);
                        if (!Free(pos, inf.size.x / 2, inf.size.z / 2, face, out float y, out _)) continue;
                        Put(g, "klupa-bijela", new Vector3(pos.x, y, pos.z), face.eulerAngles.y, 1, false, false); n++; ok = true;
                    }
                    if (ok) break; // one side is enough (the other may be the stand)
                }
            }
            log.Add($"  igrališta: {n} klupa");
        }

        static void ParkBenches()
        {
            if (!info.TryGetValue("klupa-bijela", out var inf)) return;
            var g = Group("park-klupe"); int n = 0, parks = 0;
            foreach (var pk in GroundCover.Parks)
            {
                var r = pk.ring; int m = r.Length / 2; if (m < 3) continue;
                double area = 0; for (int i = 0; i < m; i++) { int k = (i + 1) % m; area += r[2 * i] * r[2 * k + 1] - r[2 * k] * r[2 * i + 1]; }
                double sgn = Math.Sign(area); // inward normal side
                double per = 0; for (int i = 0; i < m; i++) { int k = (i + 1) % m; per += Math.Sqrt(Math.Pow(r[2 * k] - r[2 * i], 2) + Math.Pow(r[2 * k + 1] - r[2 * i + 1], 2)); }
                int want = Math.Min(8, Math.Max(1, (int)(per / 22))), got = 0; double step = per / want, acc = step / 2;
                for (int i = 0; i < m && got < want; i++)
                {
                    int k = (i + 1) % m; double ax = r[2 * i], az = r[2 * i + 1], bx = r[2 * k], bz = r[2 * k + 1], L = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                    if (L < 1e-3) continue;
                    double nx = -(bz - az) / L * sgn, nz = (bx - ax) / L * sgn; // inward for a counter-clockwise (area > 0) ring in x / z
                    while (acc <= L && got < want)
                    {
                        double x = ax + (bx - ax) * acc / L, z = az + (bz - az) * acc / L; acc += step;
                        bool ok = false;
                        foreach (double d in new[] { 2.5, 3.5, 1.8, 5 })
                        {
                            var pos = W.U(x + nx * d, 0, z + nz * d); var into = W.U(x + nx * (d + 1), 0, z + nz * (d + 1));
                            var face = Quaternion.LookRotation(new Vector3(into.x - pos.x, 0, into.z - pos.z).normalized);
                            if (GroundCover.Hard(x + nx * d, z + nz * d)) continue;
                            if (!Free(pos, inf.size.x / 2 + .1f, inf.size.z / 2 + .2f, face, out float y, out _)) continue;
                            Put(g, "klupa-bijela", new Vector3(pos.x, y, pos.z), face.eulerAngles.y, 1, false, false); n++; got++; ok = true; break;
                        }
                        if (!ok) nRejected++;
                    }
                    acc -= L;
                }
                if (got > 0) parks++;
            }
            log.Add($"  parkovi: {n} klupa u {parks} parkova (OSM leisure=park: {GroundCover.Parks.Count})");
        }

        // ------------------------------------------------------------------ parked cars
        public static float StreetShare = .22f; // mean share of the verge spots taken (Kaštela photo: ~1 car per 15–25 m of village street;
                                                // .26 before the Mapillary lots: the total stays ~1800, moved towards the measured lots)
        public static float YardShare = .4f;
        static List<Vector3> gates; static Dictionary<Vector3, Vector2> gateN;

        static void ParkedCars()
        {
            var data = AssetDatabase.LoadAssetAtPath<TrafficData>("Assets/Generated/Traffic/TrafficData.asset");
            var proot = WorldBuilder.Root("ParkedCars");
            if (!data) { log.Add("  parkirani auti: nema TrafficData (Build traffic)"); return; }
            // the parking filter (ParkedBake.Parkable: 3.3–5.4 m long, ≤ 2.1 m high, no beacons / bus / truck), the 32 commonest kinds:
            // a baked kind (ParkedBake) costs one instanced draw per distance level whatever its colours (paint per instance);
            // a kind without the bake falls back to the merged far mesh (palette + paint + tail per kind and colour)
            var ks = Enumerable.Range(0, data.kinds.Length).Where(k => ParkedBake.Parkable(data.kinds[k]) && data.kinds[k].farMesh)
                .OrderByDescending(k => data.kinds[k].weight).Take(32).ToArray();
            if (ks.Length == 0) { log.Add("  parkirani auti: nema vrsta"); return; }
            float wsum = ks.Sum(k => data.kinds[k].weight);
            gates = new List<Vector3>(); gateN = new Dictionary<Vector3, Vector2>();
            if (File.Exists(Yards.SpotsPath))
                foreach (var o in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(Yards.SpotsPath)))["gates"]))
                { var q = MiniJson.L(o); var gp = new Vector3(MiniJson.F(q[0]), MiniJson.F(q[1]), MiniJson.F(q[2])); gates.Add(gp); gateN[gp] = new Vector2(MiniJson.F(q[3]), MiniJson.F(q[4])); }
            var cars = new List<Vector4>(); var kinds = new List<int>(); var paints = new List<int>();
            var colRoot = new GameObject("kolajderi").transform; colRoot.SetParent(proot.transform, false); colRoot.gameObject.isStatic = true;
            int nLot = 0, nStreet = 0, triedLot = 0, triedStreet = 0;
            var why = new Dictionary<string, int>();
            void Why(string w) { why.TryGetValue(w, out int c); why[w] = c + 1; }

            bool Place(double gx, double gz, float yawU, uint h, bool street, bool yard = false, bool lot = false)
            {
                float r = (h & 0xffff) / 65536f * wsum; int kd = ks[0];
                foreach (int k in ks) { r -= data.kinds[k].weight; if (r <= 0) { kd = k; break; } }
                var K = data.kinds[kd];
                var c = W.U(gx, 0, gz); var rot = Quaternion.Euler(0, yawU, 0);
                if (!CarFree(c, K.halfW + .12f, K.len / 2 + .2f, rot, street, out float y, out string w, yard, lot)) { Why(w); return false; }
                // baked kinds: every colour by its weight (the paint is per instance); far mesh kinds: the three commonest only
                // (every colour is one more instanced draw per kind there)
                bool baked = K.parked != null && K.parked.Length == 3 && K.parkedMat;
                var top = Enumerable.Range(0, K.paintW.Length).OrderByDescending(i => K.paintW[i]).Take(baked ? K.paintW.Length : 3).ToArray();
                float pr = ((h >> 16) & 0xffff) / 65536f * top.Sum(i => K.paintW[i]); int pi = top[0];
                foreach (int i in top) { pr -= K.paintW[i]; if (pr <= 0) { pi = i; break; } }
                cars.Add(new Vector4(c.x, y, c.z, yawU)); kinds.Add(kd); paints.Add(Mathf.Min(pi, K.paints.Length - 1));
                placed.Add((new Vector2(c.x, c.z), new Vector2(K.halfW + .1f, K.len / 2 + .15f), yawU));
                var go = new GameObject("auto"); go.isStatic = true; go.transform.SetParent(colRoot, false);
                go.transform.SetPositionAndRotation(new Vector3(c.x, y + K.h / 2, c.z), rot); go.AddComponent<BoxCollider>().size = new Vector3(K.halfW * 2, K.h, K.len);
                return true;
            }
            uint Hs(double x, double z, int salt) { unchecked { uint q = (uint)((long)Math.Round(x * 10) * 73856093L) ^ (uint)((long)Math.Round(z * 10) * 19349663L) ^ (uint)(salt * 83492791); q ^= q >> 13; q *= 0x5bd1e995; q ^= q >> 15; return q; } }

            // 1. car parks (the ground build's occupied bays)
            foreach (var c in GroundCover.Cars)
            {
                triedLot++;
                double yaw = c[2]; // atan2 of the long axis in the game frame (x east, z south)
                float yawU = (float)(Math.Atan2(Math.Sin(yaw), -Math.Cos(yaw)) * 180 / Math.PI) + ((Hs(c[0], c[1], 3) & 1) == 0 ? 180 : 0);
                if (Place(c[0], c[1], yawU, Hs(c[0], c[1], 1), false)) nLot++;
            }
            // 1b. the Mapillary car parks without an OSM car park (GroundCoverMapillary.cs): nose-in bays across the lot's long
            //     axis (2.7 m wide, 5.2 m deep), packed on the lot's standing room by GroundCover.MlyPrepare; the share
            //     taken follows what the photos saw (cars seen per pass over the bays), 45–80 %
            int nMly = 0, triedMly = 0;
            foreach (var L in GroundCover.MLots)
            {
                if (!L.keep || L.bays.Count == 0) continue;
                var bays = L.bays; bool along = L.along;
                int c0 = cars.Count, p0 = placed.Count, k0 = colRoot.childCount;
                double share = Math.Max(.45, Math.Min(.8, L.seen / (double)Math.Max(1, L.seqs) / bays.Count));
                foreach (var (u, v) in bays)
                {
                    var (x, z) = L.World(u, v);
                    if ((Hs(x, z, 21) & 0xff) / 256f > share) continue;
                    triedMly++;
                    double ax = along ? L.ux : -L.uz, az = along ? L.uz : L.ux; // the car's long axis
                    float yawU = (float)(Math.Atan2(ax, -az) * 180 / Math.PI) + ((Hs(x, z, 22) & 3) == 0 ? 180 : 0); // most nose-in
                    if (Place(x, z, yawU, Hs(x, z, 23), false, false, true)) { nMly++; L.nCars++; }
                }
                // under 3 cars: no car park (no asphalt in an empty field), its cars out again
                if (L.nCars > 0 && L.nCars < 3)
                {
                    cars.RemoveRange(c0, cars.Count - c0); kinds.RemoveRange(c0, kinds.Count - c0); paints.RemoveRange(c0, paints.Count - c0);
                    placed.RemoveRange(p0, placed.Count - p0);
                    while (colRoot.childCount > k0) UnityEngine.Object.DestroyImmediate(colRoot.GetChild(colRoot.childCount - 1).gameObject);
                    nMly -= L.nCars; L.nCars = 0;
                }
            }
            log.Add(GroundCover.MlyFinish());   // the asphalt of the lots that got a car
            // 2. the village streets: verge spots in runs
            // (the carriageway list is each street's polyline in 4 m pieces: walk it as one line, a spot every 5.8 m)
            double sNext = 3, s0 = 0, lx = double.NaN, lz = double.NaN;
            foreach (var rd in roads)
            {
                double dx = rd.bx - rd.ax, dz = rd.bz - rd.az, L = Math.Sqrt(dx * dx + dz * dz);
                bool cont = Math.Abs(rd.ax - lx) < .05 && Math.Abs(rd.az - lz) < .05; lx = rd.bx; lz = rd.bz;
                if (!cont) { s0 = 0; sNext = 3; }
                if (L < 1e-3) continue;
                double fx = dx / L, fz = dz / L;
                for (; sNext < s0 + L; sNext += 5.8)
                {
                    double t = sNext - s0, px = rd.ax + fx * t, pz = rd.az + fz * t;
                    if (rd.hw > 4.6) continue;                                  // the wide main roads: no
                    if ((VegetationMask.At(px, pz) & VegetationMask.BUILT) == 0) continue;
                    foreach (int sd in new[] { -1, 1 })
                    {
                        // runs: a slow noise along the streets decides where cars stand, a hash thins them inside a run
                        float run = Mathf.PerlinNoise((float)(px * .03 + sd * 17.3 + 400), (float)(pz * .03 + 900));
                        if (run < .62f - StreetShare * .35f || (Hs(px, pz, 7 + sd) & 0xff) / 256f > .72f) continue;
                        double off = rd.hw + 1.45;
                        double cx = px - fz * off * sd, cz = pz + fx * off * sd;
                        if (NearJunction(cx, cz, fx, fz)) { Why("raskrižje"); continue; }
                        triedStreet++;
                        // parallel to the street, either way round
                        float yawU = (float)(Math.Atan2(fx, -fz) * 180 / Math.PI) + ((Hs(cx, cz, 9) & 1) == 0 ? 0 : 180);
                        if (Place(cx, cz, yawU, Hs(cx, cz, 2), true)) nStreet++;
                    }
                }
                s0 += L;
            }
            // 3. driveways: most cars of Kaštela stand inside the plots (the photo: ~5 a ha in the yards, few on the street);
            //    a car behind ~40 % of the yard gates, along the gate's axis, 3–5 m in
            int nYard = 0, triedYard = 0;
            foreach (var gt in gates)
            {
                var gg = W.G(gt); if ((Hs(gg.x, gg.y, 11) & 0xff) / 256f > YardShare) continue;
                triedYard++;
                if (!gateN.TryGetValue(gt, out var n2) || n2.sqrMagnitude < .5f) continue;
                bool ok = false;
                foreach (float sgn in new[] { 1f, -1f })
                {
                    foreach (float d in new[] { 4.2f, 5.2f, 3.4f })
                    {
                        var c = gt + new Vector3(n2.x, 0, n2.y) * sgn * d; var g2 = W.G(c);
                        if (OnRoad(c, 1.5f)) break;                     // that is the street side
                        float yawU = Mathf.Atan2(n2.x * sgn, n2.y * sgn) * Mathf.Rad2Deg + ((Hs(g2.x, g2.y, 12) & 1) == 0 ? 0 : 180);
                        if (Place(g2.x, g2.y, yawU, Hs(g2.x, g2.y, 13), true, true)) { nYard++; ok = true; break; }
                    }
                    if (ok) break;
                }
            }
            var pc = proot.AddComponent<ParkedCars>();
            pc.data = data; pc.cars = cars.ToArray(); pc.kind = kinds.ToArray(); pc.paint = paints.ToArray();
            EditorUtility.SetDirty(pc);
            log.Add($"  parkirani auti: {cars.Count} ({nLot} / {triedLot} na parkiralištima, {nMly} / {triedMly} na Mapillary parkiralištima, {nStreet} / {triedStreet} uz ulice, {nYard} / {triedYard} u dvorištima iza kapija; vrste {string.Join(",", ks.Select(k => data.kinds[k].name))}); odbijeno: " +
                    string.Join(", ", why.OrderByDescending(x => x.Value).Select(x => $"{x.Key} {x.Value}")));
        }

        /// another carriageway (not running along this one) within its half width + 5.5 m: a junction / a side street mouth
        static bool NearJunction(double x, double z, double fx, double fz)
        {
            for (int gx = (int)Math.Floor((x - 12) / 16); gx <= (int)Math.Floor((x + 12) / 16); gx++)
                for (int gz = (int)Math.Floor((z - 12) / 16); gz <= (int)Math.Floor((z + 12) / 16); gz++)
                {
                    if (!roadGrid.TryGetValue((gx, gz), out var l)) continue;
                    foreach (int i in l)
                    {
                        var r = roads[i]; double bx = r.bx - r.ax, bz = r.bz - r.az, l2 = bx * bx + bz * bz; if (l2 < 1e-6) continue;
                        double cosA = Math.Abs((bx * fx + bz * fz) / Math.Sqrt(l2)); if (cosA > .9) continue; // runs along (this street, a bend of it)
                        double t = Math.Max(0, Math.Min(1, ((x - r.ax) * bx + (z - r.az) * bz) / l2));
                        double ddx = x - r.ax - bx * t, ddz = z - r.az - bz * t;
                        if (ddx * ddx + ddz * ddz < (r.hw + 5.5) * (r.hw + 5.5)) return true;
                    }
                }
            return false;
        }

        /// can a car stand here: every probe on the ground (street cars: bare terrain only), flat, off carriageways / model roads,
        /// stop bays, gates, pitches / pools, props and other cars, walls / fences / buildings in its volume
        static bool CarFree(Vector3 c, float hx, float hz, Quaternion rot, bool street, out float y, out string why, bool yard = false, bool lot = false)
        {
            y = float.NaN; why = null;
            var ys = new List<float>(9);
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    var p = c + rot * new Vector3(i * hx, 0, j * hz);
                    var hits = Physics.RaycastAll(new Vector3(p.x, 400, p.z), Vector3.down, 900, ~0, QueryTriggerInteraction.Ignore);
                    Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                    bool got = false;
                    var gp = W.G(p); double terr = Tiles.Height(gp.x, gp.y);
                    foreach (var hh in hits)
                    {
                        if (Ignored(hh.collider) || HasAncestor(hh.collider.transform, "ParkedCars") || HasAncestor(hh.collider.transform, "Tlo") && !(hh.collider is BoxCollider)) continue;
                        // a Mapillary lot reaches to ~1.5 m off the road edge: the D8 / model road meshes are carriageway there
                        if (lot && HasAncestor(hh.collider.transform, "Roads")) { why = "kolnik (D8)"; return false; }
                        // a building's flat yard paving / paved front (≤ 0.3 m over the ground) is ground for a car in a yard
                        bool paving = HasAncestor(hh.collider.transform, "Buildings") && hh.point.y < terr + .3 && yard;
                        if (!paving && (HasAncestor(hh.collider.transform, "Buildings") || hh.collider is BoxCollider)) { why = "zgrada / zid / ograda"; return false; }
                        // street cars: on the verge or up on the footway of our streets (two wheels on the kerb, as in Kaštela), not on paving / the riva
                        if (street && !yard && !(hh.collider is TerrainCollider) && !HasAncestor(hh.collider.transform, "Streets")) { why = "nije tlo ni pločnik (popločenje / riva)"; return false; }
                        ys.Add(hh.point.y); got = true; break;
                    }
                    if (!got) { why = "nema tla"; return false; }
                    if (OnRoad(p, lot ? 1f : .25f)) { why = "kolnik"; return false; }   // a lot car: a metre clear of the road edge
                }
            ys.Sort(); if (ys[8] - ys[0] > .45f) { why = "nagib"; return false; }
            y = ys[4]; if (y < .4f) { why = "more"; return false; }
            if (InBay(c, 1.0f)) { why = "mjesto istovara"; return false; }
            var g = W.G(c);
            if (street)
            {
                if (GroundCover.Hard(g.x, g.y, .5)) { why = "igralište / bazen"; return false; }
                if (!yard) foreach (var gt in gates) if ((new Vector2(gt.x - c.x, gt.z - c.z)).sqrMagnitude < 3.2f * 3.2f) { why = "kapija"; return false; }
            }
            var c2 = new Vector2(c.x, c.z); float yw = rot.eulerAngles.y;
            foreach (var q in placed) if (Overlap(c2, new Vector2(hx, hz), yw, q.c, q.h, q.yaw, .1f)) { why = "preklapanje"; return false; }
            foreach (var col in Physics.OverlapBox(new Vector3(c.x, y + .9f, c.z), new Vector3(hx * .95f, .65f, hz * .95f), rot, ~0, QueryTriggerInteraction.Ignore))
                if (!Ignored(col) && !(col is TerrainCollider) && !HasAncestor(col.transform, "ParkedCars") && (HasAncestor(col.transform, "Buildings") || col is BoxCollider || col is MeshCollider))
                { why = "zgrada / zid (volumen)"; return false; }
            return true;
        }
    }
}
