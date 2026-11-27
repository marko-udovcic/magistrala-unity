// Signals of the real signalised junctions without a junction model (ExtraSignals.cs, Assets/Editor/semafori-bez-modela.json),
// built like the owner's props (same heads, poles, plates and atlas) after his photos (references/fotografije/semafori/1–7):
//   main road   a cantilever on the right kerb at the stop line: one horizontal head over the lane, a vertical head on the pole,
//               the priority diamond over the pedestrian-crossing plate on the pole; at a school (Tamunić) a "ŠKOLA" plate on the arm
//   side road   a post on the right kerb with a vertical head (and the crossing plate where a zebra is)
//   pedestrians two-lens heads (red / green) on posts at both ends of the zebras of the pedestrian-phase junctions
//   markings    stop line (0.5 m, the right half of the carriageway), zebra (white, Gomilica red-white), "ŠKOLA" and the X before
//               the school crossings — flat quads on the asphalt (probed per vertex, 2 cm up), only where the game has the road
//               (the owner's tiles s06 / s13 paint their own zebras; the side streets of 6 / 7 are not in the game: no paint)
//   island      concrete half-sphere bollards (Putaljski put, photo 4)
using System.Collections.Generic;
using Magistrala.Traffic;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static partial class SignsBuilder
    {
        static Dictionary<Vector2Int, Chunk> decals;
        static List<ExtraSignals.Junction> extra;

        static Chunk DecalChunk(Vector3 p)
        {
            var k = new Vector2Int(Mathf.FloorToInt(p.x / CH), Mathf.FloorToInt(p.z / CH));
            if (!decals.TryGetValue(k, out var c)) decals[k] = c = new Chunk();
            return c;
        }

        static Vector3 UDir(Vector2 g) => new Vector3(g.x, 0, -g.y).normalized; // game direction (x east, z south) → Unity

        /// the top walkable surface at p (road / ground), not the signs' own posts
        static float Surface(Vector3 p, float yHint, out bool hit)
        {
            hit = false; float best = yHint, bd = float.MaxValue;
            foreach (var h in Physics.RaycastAll(new Vector3(p.x, yHint + 6f, p.z), Vector3.down, 30f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.normal.y < .6f || h.collider.name.StartsWith("TrafficCar") || h.collider.name == "post") continue;
                if (h.collider.attachedRigidbody && !h.collider.attachedRigidbody.isKinematic) continue;
                if (h.distance < bd) { bd = h.distance; best = h.point.y; hit = true; }
            }
            return best;
        }

        /// a flat marking: centre c, travel direction t (its "up"), across × along metres, atlas rect r; a vertex grid ≤ 1 m probed
        /// onto the surface so it follows camber and grade
        static void Decal(Vector3 c, Vector3 t, float across, float along, Rect r, float yHint)
        {
            t.y = 0; t.Normalize(); var right = Vector3.Cross(Vector3.up, t);
            int nx = Mathf.Max(1, Mathf.CeilToInt(across / 1f)), nz = Mathf.Max(1, Mathf.CeilToInt(along / 1f));
            var k = DecalChunk(c); int b0 = k.v.Count;
            for (int j = 0; j <= nz; j++)
                for (int i = 0; i <= nx; i++)
                {
                    float u = i / (float)nx, v = j / (float)nz;
                    var p = c + right * (u - .5f) * across + t * (v - .5f) * along;
                    p.y = Surface(p, yHint, out _) + .02f;
                    k.v.Add(p); k.n.Add(Vector3.up); k.uv.Add(UvAt(r, u, v));
                }
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    int a = b0 + j * (nx + 1) + i, b = a + 1, d = a + nx + 1, e = d + 1;
                    Tri(k, a, d, e, Vector3.up); Tri(k, a, e, b, Vector3.up);
                }
            Count("oznaka na kolniku (dio)");
        }

        /// a two-lens pedestrian head (red man over green man; lit lenses as the vehicle heads') facing people walking along t
        static void PedHead(Vector3 c, Vector3 t, string plan, string group)
        {
            t.y = 0; t.Normalize(); var q = Quaternion.LookRotation(t, Vector3.up);
            const float d = .2f, pitch = .3f, depth = .2f;
            Box(c, q, new Vector3(d * 1.6f, pitch * 2.1f, depth), rBlack);
            var front = c - t * (depth / 2 + .006f);
            var h = new TrafficSignals.Head
            {
                corridor = -1, planKey = plan, group = group,
                mRed = LensAt(front + Vector3.up * pitch / 2, t, d), mGreen = LensAt(front - Vector3.up * pitch / 2, t, d),
                mAmber = Matrix4x4.TRS(front, Quaternion.identity, Vector3.zero),
            };
            for (int i = 0; i < 2; i++) Box(front + Vector3.up * (i == 0 ? pitch / 2 : -pitch / 2) + Vector3.up * d * .55f - t * .08f, q, new Vector3(d * 1.05f, .02f, .16f), rBlack);
            heads.Add(h); Count("glave pješačkog semafora");
        }

        /// concrete half-sphere bollard
        static void Dome(Vector3 b, float rad, Rect r)
        {
            var k = At(b); int b0 = k.v.Count; const int RINGS = 5, SEG = 12;
            for (int j = 0; j <= RINGS; j++)
            {
                float phi = j / (float)RINGS * Mathf.PI / 2; // 0 = equator … π/2 = top
                for (int i = 0; i <= SEG; i++)
                {
                    float a = i * Mathf.PI * 2 / SEG; var n = new Vector3(Mathf.Cos(a) * Mathf.Cos(phi), Mathf.Sin(phi), Mathf.Sin(a) * Mathf.Cos(phi));
                    k.v.Add(b + n * rad); k.n.Add(n); k.uv.Add(r.center);
                }
            }
            for (int j = 0; j < RINGS; j++)
                for (int i = 0; i < SEG; i++)
                {
                    int a = b0 + j * (SEG + 1) + i, c = a + SEG + 1;
                    var n = (k.n[a] + k.n[c + 1]).normalized;
                    Tri(k, a, c, c + 1, n); Tri(k, a, c + 1, a + 1, n);
                }
            cols.Add((b + Vector3.up * rad / 2, Quaternion.identity, new Vector3(rad * 1.6f, rad, rad * 1.6f)));
            Count("betonska polukugla");
        }

        static bool NearExtraJunction(Vector3 u, float r)
        {
            extra ??= ExtraSignals.Load();
            var g = W.G(u);
            foreach (var j in extra) if ((j.c - g).magnitude < r) return true;
            return false;
        }
        static bool NearExtraStop(Vector3 u, float r)
        {
            extra ??= ExtraSignals.Load();
            var g = W.G(u);
            foreach (var j in extra) foreach (var a in j.approaches) if ((a.p - g).magnitude < r) return true;
            return false;
        }

        static readonly List<(string message, bool warning)> signalPoleLog = new List<(string, bool)>();

        static Vector3 ExtraPolePosition(string id, Vector3 kerb, Vector3 right, Vector3 travel)
        {
            // Preserve the old successful result bit-for-bit. Only the final "?? kerb"
            // branch gets the wider search; do not perturb other junctions' furniture.
            var candidate = Clear(kerb, right, .3f) ?? OffAsphalt(kerb, 6f);
            bool fallback = !candidate.HasValue;
            if (fallback) candidate = SignalPoleFallback(kerb, right, travel);
            var original = W.G(kerb);
            if (!candidate.HasValue)
            {
                signalPoleLog.Add(($"[signals] WARNING pole {id} at game ({original.x:F2}, {original.y:F2}): no asphalt-free position with 0.4 m clearance within right 0..4 m / travel +/-8 m; retained original position for inspection", true));
                return kerb;
            }
            var result = candidate.Value;
            if ((result - kerb).sqrMagnitude > 0)
            {
                var moved = W.G(result);
                signalPoleLog.Add(($"[signals] pole {id} moved {Vector3.Distance(kerb, result):F2} m: game ({original.x:F2}, {original.y:F2}) -> ({moved.x:F2}, {moved.y:F2}); {(fallback ? "extended fallback" : "existing clearance rule")}", false));
                if (fallback) { nOffAsphalt++; Count("stup semafora maknut s asfalta (proširena pretraga)"); }
            }
            return result;
        }

        static void BuildExtraSignals()
        {
            signalPoleLog.Clear(); // Build runs twice for atlas packing; publish only the final pass.
            extra = ExtraSignals.Load();
            var rPaint = R(A.Solid("paint", SignAtlas.PaintWhite)); var rRed = R(A.Solid("paintred", SignAtlas.PaintRed));
            var rConcrete = R(A.Solid("concrete", SignAtlas.Concrete));
            string school = A.SchoolPlate(), road = A.RoadText("ŠKOLA");
            foreach (var j in extra)
            {
                float yh = j.y;
                foreach (var a in j.approaches)
                {
                    var t = UDir(a.t); var right = Vector3.Cross(Vector3.up, t);
                    var p = W.U(a.p.x, 0, a.p.y); p.y = Surface(p, yh, out bool ok);
                    if (!ok) log.Add($"{j.key} {a.name}: nema tla na stop-crti");
                    // the pole: on the right kerb, half a metre past the stop line (towards the zebra, as in the photos)
                    var b0 = p + right * (a.w / 2 + .75f) + t * .5f;
                    var bq = ExtraPolePosition($"{j.key}/{a.group}/{a.name}", b0, right, t); var b = bq; b.y = Surface(b, p.y, out _);
                    float reachLane = Vector3.Dot(b - p, right); // lateral pole → lane centre
                    bool zebra = a.signs.Contains("pjesacki");
                    if (a.mount == "konzola")
                    {
                        var q = Quaternion.LookRotation(t, Vector3.up);
                        Cyl(b, 6.1f, .12f, rSteel, false, 10); cols.Add((b + Vector3.up * 3.05f, q, new Vector3(.28f, 6.1f, .28f)));
                        Box(b + Vector3.up * .05f, q, new Vector3(.5f, .1f, .5f), rSteel);
                        float reach = reachLane + .9f;
                        Box(b - right * reach / 2 + Vector3.up * 5.86f, q, new Vector3(reach, .14f, .14f), rSteel);
                        Box(b - right * reach / 2 + Vector3.up * 5.3f, q, new Vector3(reach * .8f, .08f, .08f), rSteel); // the lower chord (photos 2, 7)
                        var e1 = b + Vector3.up * 4.85f; var e2 = b - right * Mathf.Min(2.4f, reach) + Vector3.up * 5.8f;
                        Box((e1 + e2) / 2, Quaternion.LookRotation(e2 - e1, t) * Quaternion.Euler(90, 0, 0), new Vector3(.07f, (e2 - e1).magnitude, .07f), rSteel);
                        // the head over the lane (horizontal, red on the driver's left), the repeater on the pole
                        Head(b - right * reachLane + Vector3.up * 5.55f - t * .1f, t, false, .28f, j.key, a.group);
                        Head(b + Vector3.up * 2.75f - t * .2f, t, true, .2f, j.key, a.group);
                        if (a.signs.Contains("skola_ploca")) { Plate(b - right * (reachLane - 1.45f) + Vector3.up * 5.55f - t * .12f, t, school, A.Back("rect"), 1.15f, .4f); Count("ploča ŠKOLA"); }
                        if (a.signs.Contains("prednost")) { Plate(b + Vector3.up * 4.35f - t * .2f, t, A.Priority(), A.Back("diamond"), .75f, .75f); Count("znak prednost (semafor bez modela)"); }
                        if (zebra) Plate(b + Vector3.up * 3.6f - t * .2f, t, A.Pedestrian(), A.Back("square"), .6f, .6f);
                        Count("konzola (semafor bez modela)");
                    }
                    else
                    {
                        float top = zebra ? 4.1f : 3.45f;
                        Pole(b, top, .07f);
                        Head(b + Vector3.up * 2.85f - t * .14f, t, true, .2f, j.key, a.group);
                        if (zebra) Plate(b + Vector3.up * 3.75f - t * .1f, t, A.Pedestrian(), A.Back("square"), .6f, .6f);
                        Count("stup sa semaforom (bez modela)");
                    }
                    if (zebra) { placed.Add((b, t, "pješački prijelaz")); Count("znak pješački prijelaz (semafor bez modela)"); }
                    var gb = W.G(b); where.Add($"semafor {j.key} {a.group}\t{gb.x:F1}\t{gb.y:F1}\t{Mathf.Atan2(t.x, t.z) * Mathf.Rad2Deg:F0}");

                    // markings on the asphalt
                    if (a.paint) Decal(p, t, a.w - .3f, .5f, rPaint, p.y);
                    if (a.zebra != null && a.zebra.paint)
                    {
                        var z = a.zebra; var zt = UDir(z.t); var zr = Vector3.Cross(Vector3.up, zt);
                        var zc = W.U(z.p.x, 0, z.p.y); zc.y = Surface(zc, p.y, out _);
                        int n = Mathf.Max(2, Mathf.FloorToInt((z.w - .3f) / 1f));
                        float span = n * 1f - .5f; // n bars of 0.5 m with 0.5 m gaps
                        for (int i = 0; i < n; i++)
                        {
                            float o = -span / 2 + .25f + i;
                            Decal(zc + zr * o, zt, .5f, z.l, rPaint, zc.y);
                            if (z.redWhite && i + 1 < n) Decal(zc + zr * (o + .5f), zt, .5f, z.l, rRed, zc.y);
                        }
                        if (z.redWhite) foreach (int s in new[] { -1, 1 }) Decal(zc + zt * s * (z.l / 2 + .15f), zt, span, .3f, rRed, zc.y);
                        Count("zebra (semafor bez modela)");
                    }
                    foreach (var m in a.marks)
                    {
                        var mt = UDir(m.t); var mc = W.U(m.p.x, 0, m.p.y); mc.y = Surface(mc, p.y, out _);
                        if (m.kind == "skola") { Decal(mc, mt, Mathf.Min(2.8f, a.w - .5f), 5.2f, R(road), mc.y); Count("natpis ŠKOLA na kolniku"); }
                        else if (m.kind == "x")
                        {
                            foreach (float ang in new[] { 38f, -38f }) Decal(mc, Quaternion.Euler(0, ang, 0) * mt, .3f, 3.6f, rPaint, mc.y);
                            Count("oznaka X na kolniku");
                        }
                    }
                }
                // pedestrian heads at both ends of the zebras (pedestrian phase P1)
                foreach (var (zp, zt2, zw) in j.ped)
                {
                    var zt = UDir(zt2); var zr = Vector3.Cross(Vector3.up, zt); var zc = W.U(zp.x, 0, zp.y); zc.y = Surface(zc, yh, out _);
                    foreach (int s in new[] { -1, 1 })
                    {
                        var e0 = zc + zr * s * (zw / 2 + .7f) + zt * 1.6f;
                        var e = ExtraPolePosition($"{j.key}/P1/zebra({zp.x:F2},{zp.y:F2})/side{s}", e0, zr * s, zt); e.y = Surface(e, zc.y, out _);
                        Pole(e, 2.75f, .06f);
                        PedHead(e + Vector3.up * 2.35f - zr * s * .12f, zr * s, j.key, "P1");
                    }
                }
                foreach (var d in j.domes)
                {
                    var u = W.U(d.x, 0, d.y); var cen = W.U(j.c.x, 0, j.c.y); var outw = u - cen; outw.y = 0;
                    var q = Clear(u, outw.normalized, .4f); if (q == null) { Count("preskočeno (na cesti) polukugla"); continue; }
                    var bb = q.Value; bb.y = Surface(bb, yh, out _);
                    Dome(bb, .32f, rConcrete);
                }
            }
        }

        /// the player's red-light lines of the no-model junctions
        static List<TrafficSignals.Line> ExtraLines()
        {
            var res = new List<TrafficSignals.Line>();
            foreach (var j in ExtraSignals.Load())
                foreach (var a in j.approaches)
                {
                    var p = W.U(a.p.x, j.y, a.p.y); p.y = Surface(p, j.y, out _);
                    res.Add(new TrafficSignals.Line { p = p, dir = UDir(a.t), hw = a.w / 2, planKey = j.key, group = a.group });
                }
            return res;
        }
    }
}
