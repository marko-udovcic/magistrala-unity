// Magistrala/Brodice: postavi (also run at the end of Magistrala/Rekviziti: postavi) — moored boats of Kaštela (Marko 2026-11-22):
//   models   our own, procedural (no downloads): a lofted hull (stations stern -> bow, a superellipse section keel -> sheer, raked
//            stem), plain vertex colours, a wooden rim, the waterline painted by the shader; six kinds, a near and a far mesh each:
//              pasara    5.0 m transom, thwarts, oars, small outboard          guc       4.6 m double-ended, rowed
//              gajeta    7.0 m double-ended, decks fore / aft, engine box, short mast with the lateen yard tied to it
//              pentaša   5.6 m fibreglass, console with windscreen, outboard   jedrilica 10 m sailing yacht, cabin, mast, sail cover
//              jahta     11 m motor yacht, superstructure, window band, flybridge, swim platform
//            hull colour per instance (palettes per kind), the antifouling one of two per boat; Cull Off: the hull's back faces
//            show its inside colour
//   slots    coast.json (BalkanDriver tools/world/coast_features.py + coast_harbours.py: props [kind, x, z, yaw, variant], bow =
//            model -z towards (-sin yaw, -cos yaw) in the game frame; boat 4.7 m × (1 + 0.07 variant), yacht / sail 10 m + variant),
//            Kaštela only (StreetsBuilder.AX0..AZ1); "boat" slots get pasara / guc / gajeta / pentaša by length and a hash, the
//            marinas' sail / yacht slots sailing / motor yachts (Lučica Kaštel Stari: its 6–8.6 m berths smaller boats); plus a few
//            boats on buoys 50–140 m off each harbour, bows to the bura (NE)
//   checks   the sea surface is y = 0 (GraphicsPass.Sea: the near grid at game height 0); every hull's footprint (9 points) must
//            be over water: the first collider from above (not a prop, car or the truck) below −0.12 m, or none; else the boat
//            is nudged out along its bow (≤ 3 m) and sideways, or dropped; boats keep 0.9 × their beams apart
//   runtime  World/Boats: Magistrala.MooredBoats (instanced, bobbing in Magistrala/MooredBoat); no colliders
// Output: Assets/Generated/Boats/ (meshes, material). Shots: -executeMethod Magistrala.EditorTools.BoatsBuilder.Shots -out <dir>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class BoatsBuilder
    {
        const string DIR = WorldBuilder.GEN + "/Boats";
        const int PASARA = 0, GUC = 1, GAJETA = 2, PENTASA = 3, JEDRILICA = 4, JAHTA = 5;
        static readonly string[] Names = { "pasara", "guc", "gajeta", "pentasa", "jedrilica", "jahta" };

        // ------------------------------------------------------------------ hulls
        class Hull
        {
            public float L, B, transom, s0, sMid, sBow, draft, n, rake;
            public float HB(float t)
            {
                float f;
                if (transom > 0) f = t < .4f ? Mathf.Lerp(transom, 1, Mathf.SmoothStep(0, 1, t / .4f)) : Mathf.Pow(Mathf.Max(0, 1 - Mathf.Pow((t - .4f) / .6f, 2.2f)), .62f);
                else f = Mathf.Pow(Mathf.Clamp01(Mathf.Sin(Mathf.PI * t)), .62f);
                return Mathf.Max(.015f, B * .5f * f);
            }
            public float SH(float t) => sMid + (t < .42f ? (s0 - sMid) * Mathf.Pow((.42f - t) / .42f, 2) : (sBow - sMid) * Mathf.Pow((t - .42f) / .58f, 2));
            public float KL(float t)
            {
                if (transom > 0) return -draft * (t < .15f ? Mathf.Lerp(.75f, 1, t / .15f) : 1 - .9f * Mathf.SmoothStep(0, 1, (t - .62f) / .38f));
                return -draft * (1 - .85f * Mathf.Pow(Mathf.Abs(2 * t - 1), 3));
            }
            /// a point of the hull skin: t stern 0 -> bow 1, u keel 0 -> sheer 1, side +1 starboard / -1 port
            public Vector3 P(float t, float u, int side)
            {
                float th = u * Mathf.PI * .5f, e = 2 / n;
                float hb = HB(t), h = SH(t), k = KL(t);
                float x = hb * Mathf.Pow(Mathf.Clamp01(Mathf.Sin(th)), e), y = k + (h - k) * (1 - Mathf.Pow(Mathf.Clamp01(Mathf.Cos(th)), e));   // (cos 90° is −4e-8: Pow -> NaN)
                float z = (t - .5f) * L + rake * Mathf.Pow(u, 1.5f) * Mathf.SmoothStep(0, 1, (t - .82f) / .18f);
                if (transom <= 0) z -= rake * .6f * Mathf.Pow(u, 1.5f) * Mathf.SmoothStep(0, 1, (.18f - t) / .18f);
                return new Vector3(x * side, y, z);
            }
            /// half width of the inside at height y (station t)
            public float HalfAt(float t, float y)
            {
                Vector3 prev = P(t, 0, 1); if (y <= prev.y) return 0;
                for (int j = 1; j <= 32; j++) { var q = P(t, j / 32f, 1); if (q.y >= y) return Mathf.Lerp(prev.x, q.x, (y - prev.y) / Mathf.Max(1e-4f, q.y - prev.y)); prev = q; }
                return prev.x;
            }
            public float Z(float t) => (t - .5f) * L;
        }

        static readonly Hull[] Hulls = {
            new Hull { L = 5.0f, B = 1.85f, transom = .70f, s0 = .58f, sMid = .52f, sBow = .80f, draft = .30f, n = 2.4f, rake = .25f },   // pasara
            new Hull { L = 4.6f, B = 1.70f, transom = 0,    s0 = .64f, sMid = .48f, sBow = .66f, draft = .28f, n = 2.6f, rake = .20f },   // guc
            new Hull { L = 7.0f, B = 2.50f, transom = 0,    s0 = .88f, sMid = .72f, sBow = 1.05f, draft = .55f, n = 2.2f, rake = .35f },  // gajeta
            new Hull { L = 5.6f, B = 2.15f, transom = .85f, s0 = .70f, sMid = .68f, sBow = .92f, draft = .35f, n = 1.3f, rake = .45f },   // pentaša
            new Hull { L = 10f,  B = 3.40f, transom = .78f, s0 = 1.0f, sMid = 1.05f, sBow = 1.25f, draft = .50f, n = 1.8f, rake = .60f }, // jedrilica
            new Hull { L = 11f,  B = 3.70f, transom = .85f, s0 = 1.15f, sMid = 1.25f, sBow = 1.50f, draft = .60f, n = 1.35f, rake = .90f }, // jahta
        };
        /// bobbing per kind: heave (m), roll / pitch (deg), period (s) — bigger boats slower and smaller
        static readonly (float heave, float roll, float pitch, float period)[] Bobs = {
            (.07f, 3.5f, 2.0f, 5.0f), (.07f, 3.8f, 2.2f, 4.8f), (.06f, 3.0f, 1.6f, 6.0f), (.065f, 3.2f, 1.8f, 5.4f), (.05f, 2.4f, 1.0f, 7.8f), (.045f, 2.0f, .9f, 8.4f) };
        /// hull colours (sRGB, weight) per kind
        static readonly (Color c, float w)[] WoodPal = {
            (new Color(.93f, .93f, .91f), 40), (new Color(.55f, .73f, .86f), 10), (new Color(.18f, .36f, .62f), 11), (new Color(.22f, .48f, .33f), 9),
            (new Color(.30f, .62f, .62f), 7), (new Color(.90f, .85f, .72f), 8), (new Color(.62f, .22f, .16f), 4), (new Color(.72f, .74f, .74f), 6), (new Color(.95f, .80f, .30f), 3) };
        static readonly (Color c, float w)[] GrpPal = {
            (new Color(.94f, .94f, .93f), 62), (new Color(.80f, .82f, .83f), 10), (new Color(.62f, .77f, .88f), 10), (new Color(.90f, .87f, .78f), 9), (new Color(.16f, .24f, .40f), 5), (new Color(.70f, .16f, .14f), 4) };
        static readonly (Color c, float w)[] YachtPal = {
            (new Color(.95f, .95f, .94f), 80), (new Color(.14f, .20f, .32f), 8), (new Color(.55f, .58f, .60f), 6), (new Color(.88f, .86f, .80f), 6) };

        static Color Lin(float r, float g, float b) => new Color(r, g, b).linear;
        static readonly Color Wood = Lin(.46f, .30f, .17f), WoodLight = Lin(.62f, .50f, .36f), WoodDark = Lin(.30f, .19f, .11f), OffWhite = Lin(.88f, .88f, .86f),
            Glass = Lin(.07f, .09f, .11f), Engine = Lin(.13f, .13f, .14f), Alu = Lin(.80f, .81f, .82f), Navy = Lin(.12f, .18f, .34f), Canvas = Lin(.86f, .82f, .70f),
            Teak = Lin(.55f, .40f, .26f), Grey = Lin(.55f, .56f, .57f), WhiteEng = Lin(.90f, .90f, .90f);

        // ------------------------------------------------------------------ mesh builder
        class MB
        {
            readonly List<Vector3> V = new List<Vector3>(), N = new List<Vector3>(), I = new List<Vector3>();
            readonly List<Color> C = new List<Color>(); readonly List<Vector2> U = new List<Vector2>(); readonly List<int> T = new List<int>();

            int Vx(Vector3 p, Vector3 n, Color c, float paint, float sm, bool hull, Color inner)
            {
                V.Add(p); N.Add(n); C.Add(new Color(c.r, c.g, c.b, paint)); U.Add(new Vector2(sm, hull ? 1 : 0)); I.Add(new Vector3(inner.r, inner.g, inner.b));
                return V.Count - 1;
            }

            /// a smooth grid (rows × columns of points); the winding chosen so the normals point along hint on average
            /// (axis: a tube round the line axisA -> axisB, every face's hint radial)
            public void Grid(Vector3[,] P, Color c, float paint, float sm, Vector3 hint, bool hull = false, Color inner = default, Vector3? axisA = null, Vector3? axisB = null)
            {
                int R = P.GetLength(0), Q = P.GetLength(1);
                var nr = new Vector3[R, Q]; float dot = 0;
                for (int r = 0; r + 1 < R; r++) for (int q = 0; q + 1 < Q; q++)
                    {
                        var fn = Vector3.Cross(P[r + 1, q] - P[r, q], P[r + 1, q + 1] - P[r, q]) + Vector3.Cross(P[r + 1, q + 1] - P[r, q], P[r, q + 1] - P[r, q]);
                        nr[r, q] += fn; nr[r + 1, q] += fn; nr[r + 1, q + 1] += fn; nr[r, q + 1] += fn;
                        var hn = hint;
                        if (axisA.HasValue) { var ax = (axisB.Value - axisA.Value).normalized; var fc = (P[r, q] + P[r + 1, q + 1]) * .5f - axisA.Value; hn = fc - ax * Vector3.Dot(fc, ax); }
                        dot += Mathf.Sign(Vector3.Dot(fn, hn));
                    }
                bool flip = dot < 0; int b = V.Count;
                for (int r = 0; r < R; r++) for (int q = 0; q < Q; q++)
                    {
                        var n = nr[r, q].sqrMagnitude > 1e-12f ? nr[r, q].normalized : hint.normalized; if (flip) n = -n;
                        Vx(P[r, q], n, c, paint, sm, hull, inner);
                    }
                for (int r = 0; r + 1 < R; r++) for (int q = 0; q + 1 < Q; q++)
                    {
                        int a = b + r * Q + q, bb = b + (r + 1) * Q + q, cc = b + (r + 1) * Q + q + 1, d = b + r * Q + q + 1;
                        if (!flip) { T.Add(a); T.Add(bb); T.Add(cc); T.Add(a); T.Add(cc); T.Add(d); }
                        else { T.Add(a); T.Add(cc); T.Add(bb); T.Add(a); T.Add(d); T.Add(cc); }
                    }
            }

            /// a flat polygon (convex, as a fan) facing along hint
            public void Poly(IList<Vector3> p, Color c, float paint, float sm, Vector3 hint, bool hull = false, Color inner = default)
            {
                var n = Vector3.zero; for (int i = 1; i + 1 < p.Count; i++) n += Vector3.Cross(p[i] - p[0], p[i + 1] - p[0]);
                bool flip = Vector3.Dot(n, hint) < 0; n = (flip ? -n : n).normalized; if (n == Vector3.zero) n = hint.normalized;
                int b = V.Count; foreach (var q in p) Vx(q, n, c, paint, sm, hull, inner);
                for (int i = 1; i + 1 < p.Count; i++) { T.Add(b); if (flip) { T.Add(b + i + 1); T.Add(b + i); } else { T.Add(b + i); T.Add(b + i + 1); } }
            }

            /// a box or frustum: bottom centre c, bottom (w, d), top (w, d) shifted by topZ along z, height h; yaw-free (boat frame);
            /// skipBottom: sitting on a deck
            public void Frustum(Vector3 c, float w0, float d0, float w1, float d1, float h, Color col, float sm, float topZ = 0, bool skipBottom = true, float paint = 0)
            {
                var b = new[] { c + new Vector3(-w0 / 2, 0, -d0 / 2), c + new Vector3(w0 / 2, 0, -d0 / 2), c + new Vector3(w0 / 2, 0, d0 / 2), c + new Vector3(-w0 / 2, 0, d0 / 2) };
                var t = new[] { c + new Vector3(-w1 / 2, h, -d1 / 2 + topZ), c + new Vector3(w1 / 2, h, -d1 / 2 + topZ), c + new Vector3(w1 / 2, h, d1 / 2 + topZ), c + new Vector3(-w1 / 2, h, d1 / 2 + topZ) };
                var mid = c + Vector3.up * h * .5f;
                for (int i = 0; i < 4; i++) { int j = (i + 1) % 4; var f = new[] { b[i], b[j], t[j], t[i] }; Poly(f, col, paint, sm, (f[0] + f[1] + f[2] + f[3]) / 4 - mid); }
                Poly(t, col, paint, sm, Vector3.up);
                if (!skipBottom) Poly(b, col, paint, sm, Vector3.down);
            }
            public void Box(Vector3 c, Vector3 size, Color col, float sm, bool bottom = true) => Frustum(c - Vector3.up * size.y / 2, size.x, size.z, size.x, size.z, size.y, col, sm, 0, !bottom);

            /// a round bar from a to b (sides faces), capped at b
            public void Bar(Vector3 a, Vector3 b, float r, int sides, Color col, float sm, float rb = -1)
            {
                if (rb < 0) rb = r;
                var ax = (b - a).normalized; var u = Vector3.Cross(ax, Mathf.Abs(ax.y) < .9f ? Vector3.up : Vector3.right).normalized; var v = Vector3.Cross(ax, u);
                var P = new Vector3[sides + 1, 2];
                for (int i = 0; i <= sides; i++) { float an = i * Mathf.PI * 2 / sides; var d = u * Mathf.Cos(an) + v * Mathf.Sin(an); P[i, 0] = a + d * r; P[i, 1] = b + d * rb; }
                Grid(P, col, 0, sm, u, false, default, a, b);
                var cap = new List<Vector3>(); for (int i = 0; i < sides; i++) cap.Add(P[i, 1]);
                Poly(cap, col, 0, sm, ax);
            }

            public int Tris => T.Count / 3;
                public Mesh ToMesh(string name, Mesh m = null)
            {
                if (!m) m = new Mesh(); m.Clear(); m.name = name; m.indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                m.SetVertices(V); m.SetNormals(N); m.SetColors(C); m.SetUVs(0, U); m.SetUVs(1, I); m.SetTriangles(T, 0); m.RecalculateBounds();
                return m;
            }
        }

        // ------------------------------------------------------------------ boat parts
        /// the hull skin (both sides), the transom, the rim (gunwale) along the sheer
        static void HullSkin(MB mb, Hull h, int NS, int NJ, float sm, Color inner, Color rimCol, float rimW, float rimH)
        {
            for (int side = 1; side >= -1; side -= 2)
            {
                var P = new Vector3[NS + 1, NJ + 1];
                for (int i = 0; i <= NS; i++) for (int j = 0; j <= NJ; j++) P[i, j] = h.P(i / (float)NS, j / (float)NJ, side);
                mb.Grid(P, Color.white, 1, sm, new Vector3(side, 0, 0), true, inner);
                if (rimW <= 0) continue;
                // rim: outer face and top, along the sheer
                var O = new Vector3[NS + 1, 2]; var Tp = new Vector3[NS + 1, 2];
                for (int i = 0; i <= NS; i++)
                {
                    var s = P[i, NJ]; var tg = P[Mathf.Min(NS, i + 1), NJ] - P[Mathf.Max(0, i - 1), NJ]; tg.y = 0;
                    var nn = new Vector3(tg.z, 0, -tg.x).normalized * side; if (nn == Vector3.zero) nn = new Vector3(side, 0, 0);
                    O[i, 0] = s + nn * rimW + Vector3.up * .02f; O[i, 1] = s + nn * rimW - Vector3.up * rimH;
                    Tp[i, 0] = s - nn * .04f + Vector3.up * .02f; Tp[i, 1] = s + nn * rimW + Vector3.up * .02f;
                }
                mb.Grid(O, rimCol, 0, .45f, new Vector3(side, 0, 0));
                mb.Grid(Tp, rimCol, 0, .45f, Vector3.up);
            }
            if (h.transom > 0)
            {
                var ring = new List<Vector3>();
                for (int j = NJ; j >= 0; j--) ring.Add(h.P(0, j / (float)NJ, -1));
                for (int j = 1; j <= NJ; j++) ring.Add(h.P(0, j / (float)NJ, 1));
                mb.Poly(ring, Color.white, 1, sm, Vector3.back, true, inner);
            }
        }

        /// a deck between the sheers from t0 to t1, dy above them
        static void Deck(MB mb, Hull h, float t0, float t1, int n, float dy, Color c, float sm)
        {
            var P = new Vector3[n + 1, 2];
            for (int i = 0; i <= n; i++) { float t = Mathf.Lerp(t0, t1, i / (float)n); var a = h.P(t, 1, -1); var b = h.P(t, 1, 1); P[i, 0] = a + Vector3.up * dy; P[i, 1] = b + Vector3.up * dy; }
            mb.Grid(P, c, 0, sm, Vector3.up);
        }

        /// floorboards at height y, from t0 to t1 (inside the hull)
        static void Floor(MB mb, Hull h, float t0, float t1, int n, float y, Color c)
        {
            var P = new Vector3[n + 1, 2];
            for (int i = 0; i <= n; i++) { float t = Mathf.Lerp(t0, t1, i / (float)n); float w = Mathf.Max(.02f, h.HalfAt(t, y) - .03f); P[i, 0] = new Vector3(-w, y, h.Z(t)); P[i, 1] = new Vector3(w, y, h.Z(t)); }
            mb.Grid(P, c, 0, .25f, Vector3.up);
        }

        static void Thwart(MB mb, Hull h, float t, float y, Color c, float depth = .24f)
        {
            float w = h.HalfAt(t, y) * 2 - .02f; mb.Box(new Vector3(0, y, h.Z(t)), new Vector3(w, .045f, depth), c, .3f);
        }

        static void Outboard(MB mb, Hull h, float size, Color cowl)
        {
            float z = -h.L / 2 - .12f * size, top = h.s0 + .28f * size;
            mb.Box(new Vector3(0, h.s0 - .02f, -h.L / 2 - .03f), new Vector3(.22f * size, .18f, .1f), Engine, .3f);                       // clamp bracket
            mb.Box(new Vector3(0, top - .2f * size, z - .05f * size), new Vector3(.34f * size, .42f * size, .5f * size), cowl, .55f);    // cowl
            mb.Box(new Vector3(0, h.s0 - .3f, z), new Vector3(.1f * size, h.s0 + .45f, .16f * size), Engine, .35f);                       // leg
            mb.Box(new Vector3(0, -.28f, z + .02f), new Vector3(.14f * size, .1f, .3f * size), Engine, .35f);                              // cavitation plate
        }

        static MB Make(int kind, int lod)
        {
            var h = Hulls[kind]; var mb = new MB();
            bool near = lod == 0;
            int NS = near ? 20 : 7, NJ = near ? 7 : 3, ND = near ? 6 : 2;
            switch (kind)
            {
                case PASARA:
                    HullSkin(mb, h, NS, NJ, .35f, Lin(.74f, .76f, .74f), Wood, near ? .045f : 0, .09f);
                    Floor(mb, h, .05f, .86f, ND, .14f, WoodLight);
                    Deck(mb, h, .84f, 1, ND / 2 + 1, .01f, OffWhite, .3f);
                    Outboard(mb, h, near ? 1 : 1.1f, Engine);
                    if (near)
                    {
                        Thwart(mb, h, .1f, h.SH(.1f) - .2f, Wood, .4f); Thwart(mb, h, .34f, h.SH(.34f) - .17f, Wood); Thwart(mb, h, .6f, h.SH(.6f) - .17f, Wood);
                        foreach (float x in new[] { -.32f, .38f }) mb.Box(new Vector3(x, h.SH(.45f) - .1f, h.Z(.47f)), new Vector3(.07f, .03f, 2.4f), WoodLight, .3f);   // oars on the thwarts
                    }
                    break;
                case GUC:
                    HullSkin(mb, h, NS, NJ, .35f, Lin(.58f, .47f, .34f), WoodDark, near ? .045f : 0, .09f);
                    Floor(mb, h, .1f, .9f, ND, .13f, WoodLight);
                    if (near)
                    {
                        Thwart(mb, h, .22f, h.SH(.22f) - .17f, Wood, .35f); Thwart(mb, h, .5f, h.SH(.5f) - .17f, Wood); Thwart(mb, h, .76f, h.SH(.76f) - .17f, Wood, .3f);
                        foreach (float x in new[] { -.3f, .34f }) mb.Box(new Vector3(x, h.SH(.5f) - .1f, h.Z(.48f)), new Vector3(.07f, .03f, 2.3f), WoodLight, .3f);
                        foreach (float t in new[] { 0f, 1f }) { var s = h.P(t, 1, 1); mb.Box(new Vector3(0, s.y + .1f, s.z), new Vector3(.07f, .3f, .1f), WoodDark, .3f); }  // stem / stern post heads
                    }
                    else Deck(mb, h, .1f, .9f, 2, -.12f, WoodLight, .2f);
                    break;
                case GAJETA:
                {
                    HullSkin(mb, h, NS, NJ, .35f, Lin(.70f, .72f, .70f), Wood, near ? .06f : 0, .12f);
                    Floor(mb, h, .15f, .75f, ND, .22f, WoodLight);
                    Deck(mb, h, .72f, 1, ND / 2 + 1, .01f, OffWhite, .3f);
                    Deck(mb, h, 0, .17f, ND / 2 + 1, .01f, OffWhite, .3f);
                    mb.Frustum(new Vector3(0, .2f, h.Z(.4f)), .95f, 1.2f, .85f, 1.05f, .62f, Wood, .4f);                   // engine box
                    float mz = h.Z(.67f), deck = h.SH(.67f);
                    mb.Bar(new Vector3(0, deck - .1f, mz), new Vector3(0, deck + 2.1f, mz), .075f, near ? 6 : 3, Wood, .4f, .05f);   // mast
                    // the lateen yard tied to the mast top, its sail furled along it
                    var y0 = new Vector3(0, h.SH(1) + .45f, h.Z(1) + .1f); var y1 = new Vector3(0, deck + 3.4f, h.Z(.1f));
                    mb.Bar(y0, y1, .045f, near ? 5 : 3, Wood, .4f, .03f);
                    if (near)
                    {
                        mb.Bar(Vector3.Lerp(y0, y1, .2f), Vector3.Lerp(y0, y1, .78f), .1f, 6, Canvas, .1f, .07f);
                        var s = h.P(1, 1, 1); mb.Box(new Vector3(0, s.y + .18f, s.z - .03f), new Vector3(.09f, .45f, .12f), WoodDark, .3f);    // stem head
                        Thwart(mb, h, .55f, h.SH(.55f) - .2f, Wood, .3f);
                        mb.Box(new Vector3(0, h.SH(.05f) + .25f, h.Z(.03f)), new Vector3(.06f, .5f, .06f), WoodDark, .3f);                      // tiller post
                    }
                    break;
                }
                case PENTASA:
                    HullSkin(mb, h, NS, NJ, .6f, Lin(.86f, .86f, .84f), Grey, near ? .03f : 0, .06f);
                    Floor(mb, h, .04f, .72f, ND, .22f, Lin(.74f, .74f, .72f));
                    Deck(mb, h, .7f, 1, ND / 2 + 1, .01f, OffWhite, .55f);
                    mb.Frustum(new Vector3(0, .22f, h.Z(.52f)), .72f, .55f, .66f, .45f, .78f, OffWhite, .55f);                // console
                    Outboard(mb, h, 1.35f, near ? WhiteEng : Engine);
                    if (near)
                    {
                        var c = new Vector3(0, 1.0f, h.Z(.52f) + .2f);
                        mb.Poly(new[] { c + new Vector3(-.38f, 0, 0), c + new Vector3(.38f, 0, 0), c + new Vector3(.34f, .32f, -.16f), c + new Vector3(-.34f, .32f, -.16f) }, Glass, 0, .92f, new Vector3(0, .4f, 1));  // windscreen
                        mb.Box(new Vector3(0, .43f, h.Z(.4f)), new Vector3(.9f, .42f, .45f), OffWhite, .5f);                  // seat box
                        mb.Box(new Vector3(0, .66f, h.Z(.4f)), new Vector3(.86f, .06f, .42f), Lin(.30f, .40f, .52f), .2f);   // cushion
                        Thwart(mb, h, .08f, .5f, OffWhite, .45f);                                                             // stern bench
                    }
                    break;
                case JEDRILICA:
                {
                    HullSkin(mb, h, NS, NJ, .65f, OffWhite, Teak, near ? .035f : 0, .05f);
                    Deck(mb, h, 0, 1, near ? 10 : 3, 0, OffWhite, .45f);
                    float cz = h.Z(.47f), dk = h.SH(.47f);
                    mb.Frustum(new Vector3(0, dk, cz), 2.1f, 3.2f, 1.75f, 2.7f, .55f, OffWhite, .55f, -.15f);              // cabin
                    mb.Frustum(new Vector3(0, dk + .22f, cz - .06f), 2.0f, 3.03f, 1.89f, 2.88f, .17f, Glass, .9f, -.047f);          // window band
                    float mz = h.Z(.6f), md = h.SH(.6f);
                    mb.Bar(new Vector3(0, md, mz), new Vector3(0, md + 12.5f, mz), .09f, near ? 6 : 3, Alu, .6f, .06f);    // mast
                    var b0 = new Vector3(0, md + 1.25f, mz - .1f); var b1 = new Vector3(0, md + 1.05f, h.Z(.08f));
                    mb.Bar(b0, b1, .06f, near ? 5 : 3, Alu, .6f);                                                         // boom
                    mb.Bar(b0 + Vector3.up * .12f, Vector3.Lerp(b0, b1, .85f) + Vector3.up * .1f, .2f, near ? 6 : 3, Navy, .2f, .14f);  // sail cover
                    if (near)
                    {
                        var top = new Vector3(0, md + 12.3f, mz);
                        mb.Bar(h.P(1, 1, 1) + Vector3.up * .05f, top, .012f, 3, Grey, .5f);                                // forestay
                        mb.Bar(new Vector3(0, h.s0 + .05f, -h.L / 2 + .1f), top, .012f, 3, Grey, .5f);                     // backstay
                        mb.Box(new Vector3(0, md + 7.5f, mz), new Vector3(2.4f, .04f, .06f), Alu, .5f);                    // spreaders
                        mb.Poly(new[] { new Vector3(-1.15f, h.SH(.15f) + .01f, h.Z(.06f)), new Vector3(1.15f, h.SH(.15f) + .01f, h.Z(.06f)), new Vector3(1.1f, h.SH(.3f) + .01f, h.Z(.31f)), new Vector3(-1.1f, h.SH(.3f) + .01f, h.Z(.31f)) }, Teak, 0, .3f, Vector3.up);   // cockpit sole
                        mb.Frustum(new Vector3(0, dk, cz - 1.85f), 1.7f, .35f, 1.6f, .8f, .9f, Navy, .2f, -.3f);              // sprayhood
                    }
                    break;
                }
                case JAHTA:
                {
                    HullSkin(mb, h, NS, NJ, .7f, OffWhite, Lin(.70f, .72f, .74f), near ? .05f : 0, .1f);
                    Deck(mb, h, 0, 1, near ? 10 : 3, 0, OffWhite, .5f);
                    float sz = h.Z(.46f), dk = h.SH(.46f);
                    mb.Frustum(new Vector3(0, dk, sz), 3.0f, 4.8f, 2.6f, 3.7f, 1.3f, OffWhite, .6f, -.45f);               // superstructure
                    mb.Frustum(new Vector3(0, dk + .55f, sz - .19f), 2.87f, 4.4f, 2.72f, 3.97f, .5f, Glass, .92f, -.174f);         // window band
                    mb.Frustum(new Vector3(0, dk + 1.3f, sz - .6f), 2.2f, 2.2f, 2.1f, 2.0f, .35f, OffWhite, .55f);        // flybridge
                    mb.Frustum(new Vector3(0, dk + 1.65f, sz + .15f), 1.6f, .25f, 1.5f, .1f, .32f, Glass, .92f, -.12f);   // its windscreen
                    mb.Box(new Vector3(0, .32f, -h.L / 2 - .35f), new Vector3(h.B * .78f, .08f, .75f), Teak, .3f);       // swim platform
                    if (near)
                    {
                        mb.Bar(new Vector3(0, dk + 1.65f, sz - 1.6f), new Vector3(0, dk + 2.8f, sz - 1.6f), .05f, 4, Alu, .5f);   // radar mast
                        mb.Box(new Vector3(0, dk + 2.84f, sz - 1.6f), new Vector3(.7f, .1f, .2f), OffWhite, .5f);
                        mb.Box(new Vector3(0, dk + 2.45f, sz - .9f), new Vector3(1.9f, .06f, 1.6f), Lin(.80f, .80f, .78f), .3f);   // bimini top
                        foreach (int sd in new[] { -1, 1 }) mb.Bar(new Vector3(sd * .9f, dk + 1.65f, sz - 1.5f), new Vector3(sd * .9f, dk + 2.43f, sz - .3f), .02f, 3, Alu, .7f);
                        foreach (int sd in new[] { -1, 1 })   // bow rail (stanchions + rail)
                        {
                            var pts = Enumerable.Range(0, 5).Select(i => h.P(.62f + i * .09f, 1, sd) - new Vector3(sd * .12f, 0, 0)).ToArray();
                            for (int i = 0; i < pts.Length; i++) mb.Bar(pts[i], pts[i] + Vector3.up * .6f, .016f, 3, Alu, .7f);
                            for (int i = 0; i + 1 < pts.Length; i++) mb.Bar(pts[i] + Vector3.up * .6f, pts[i + 1] + Vector3.up * .6f, .018f, 3, Alu, .7f);
                        }
                    }
                    break;
                }
            }
            return mb;
        }

        // ------------------------------------------------------------------ assets
        static Mesh SaveMesh(MB mb, string name, string path)
        {
            // a new asset every time (refilling the old one in place did not reach the disk); Place() sets the scene's references anew
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path)) AssetDatabase.DeleteAsset(path);
            var m = mb.ToMesh(name); AssetDatabase.CreateAsset(m, path); return m;
        }
        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = Path.GetDirectoryName(p).Replace('\\', '/'); Dir(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(p)); } }
        static int Tris(Mesh m) => m ? (int)m.GetIndexCount(0) / 3 : 0;

        // ------------------------------------------------------------------ placement
        static bool Ignored(Collider c)
        {
            for (var p = c.transform; p != null; p = p.parent)
                if (p.parent && p.parent.name == "World" && (p.name == "Props" || p.name == "ParkedCars" || p.name == "Boats" || p.name == "Sea" || p.name == "Traffic")) return true;
            return c.attachedRigidbody || c.GetComponentInParent<TruckController>();
        }
        /// land, a quay, a pier or a hull-deep shoal under this point (the first collider from above, not a prop / car)
        static string dryWhy;
        static bool Dry(Vector3 p, float minDepth = -.12f)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, 80, p.z), Vector3.down, 200, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; Collider bc = null;
            foreach (var h in hits) { if (Ignored(h.collider)) continue; if (h.distance < best) { best = h.distance; bc = h.collider; } }
            if (!bc || 80 - best <= minDepth) return false;
            float y = 80 - best;
            dryWhy = !(bc is TerrainCollider) ? $"riva / gat / zgrada ({bc.transform.parent?.name ?? bc.name})" : y < .4f ? "plićina / plaža (teren -0.12…0.4 m)" : "kopno (teren)";
            return true;
        }

        class Boat { public Vector3 p; public float yaw, scale, beam; public int kind; public Color col; public float phase; public string where; }

        static int Pick((Color c, float w)[] pal, double r) { double s = pal.Sum(x => x.w), a = r * s; for (int i = 0; i < pal.Length; i++) { a -= pal[i].w; if (a <= 0) return i; } return pal.Length - 1; }

        static readonly (string name, double x)[] Villages = { ("Štafilić", -8856), ("Novi", -8190), ("Stari", -7750), ("Lukšić", -6127), ("Kambelovac", -4480), ("Gomilica", -3500), ("Sućurac", -1231) };
        static string Village(double gx) => Villages.OrderBy(v => Math.Abs(v.x - gx)).First().name;

        [MenuItem("Magistrala/Brodice: postavi")]
        public static string BuildMenu() { MainScene.Ensure(); var s = Place(); Debug.Log(s); return s; }

        /// batch: places the boats and saves the main scene
        public static void Batch()
        {
            MainScene.Ensure(); Debug.Log(Place());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        }

        public static string Place()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Dir(DIR);
            var near = new Mesh[Names.Length]; var far = new Mesh[Names.Length]; var tris = new List<string>();
            for (int k = 0; k < Names.Length; k++)
            {
                near[k] = SaveMesh(Make(k, 0), Names[k] + "_near", $"{DIR}/{Names[k]}_near.asset"); far[k] = SaveMesh(Make(k, 1), Names[k] + "_far", $"{DIR}/{Names[k]}_far.asset");
                tris.Add($"{Names[k]} {Tris(near[k])}/{Tris(far[k])}");
            }
            string mp = DIR + "/Brodice.mat"; var sh = Shader.Find("Magistrala/MooredBoat");
            if (!sh) return "[brodice] shader Magistrala/MooredBoat missing";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!mat) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, mp); } else mat.shader = sh;
            mat.enableInstancing = true; EditorUtility.SetDirty(mat);

            // ---- slots
            var slots = new List<(double x, double z, double yaw, int variant, string kind)>();
            var cj = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "coast.json")));
            double T = MiniJson.D(cj["tileSize"]);
            foreach (var kv in MiniJson.O(cj["tiles"]))
            {
                var key = kv.Key.Split(','); int tx = int.Parse(key[0]), tz = int.Parse(key[1]);
                foreach (var po in MiniJson.L(MiniJson.O(kv.Value)["props"]))
                {
                    var q = MiniJson.L(po); var kind = (string)q[0]; if (kind != "boat" && kind != "yacht" && kind != "sail") continue;
                    double x = tx * T + MiniJson.D(q[1]), z = tz * T + MiniJson.D(q[2]);
                    if (x < StreetsBuilder.AX0 || x >= StreetsBuilder.AX1 || z < StreetsBuilder.AZ0 || z >= StreetsBuilder.AZ1) continue;
                    slots.Add((x, z, MiniJson.D(q[3]), q.Count > 4 ? (int)MiniJson.D(q[4]) : 0, kind));
                }
            }
            // harbour areas (coast_harbours.py's spec): the name of a slot's place, Lučica Kaštel Stari's smaller berths
            var areas = new List<(string id, string name, double x0, double z0, double x1, double z1)>();
            var hp = W.BD + "/tools/world/overrides/harbours.json";
            if (File.Exists(hp))
                foreach (var ao in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(hp)))["areas"]))
                {
                    var a = MiniJson.O(ao); var bx = MiniJson.L(a["box"]);
                    areas.Add(((string)a["id"], (string)a["name"], MiniJson.D(bx[0]), MiniJson.D(bx[1]), MiniJson.D(bx[2]), MiniJson.D(bx[3])));
                }
            string Area(double x, double z) { foreach (var a in areas) if (x >= a.x0 && x <= a.x1 && z >= a.z0 && z <= a.z1) return a.id; return null; }

            Physics.SyncTransforms();
            var boats = new List<Boat>(); var why = new Dictionary<string, int>(); int nudged = 0;
            void Why(string w) { why[w] = why.TryGetValue(w, out int n) ? n + 1 : 1; }
            float Beam(int k, float s) => Hulls[k].B * s;
            bool Wet(Vector3 c, float yaw, int k, float s, float minDepth)
            {
                var rot = Quaternion.Euler(0, yaw, 0); float hl = Hulls[k].L * s * .47f, hb = Hulls[k].B * s * .47f;
                foreach (var (x, z) in new[] { (0f, 0f), (0f, hl), (0f, -hl), (hb, 0f), (-hb, 0f), (hb * .85f, hl * .6f), (-hb * .85f, hl * .6f), (hb * .85f, -hl * .6f), (-hb * .85f, -hl * .6f) })
                    if (Dry(c + rot * new Vector3(x, 0, z), minDepth)) return false;
                return true;
            }
            bool Apart(Vector3 c, float beam) { foreach (var b in boats) if ((b.p - c).sqrMagnitude < Mathf.Pow(.45f * (b.beam + beam), 2)) return false; return true; }
            bool TryPut(Boat b, float minDepth, bool nudge)
            {
                var bow = Quaternion.Euler(0, b.yaw, 0) * Vector3.forward; var side = Vector3.Cross(Vector3.up, bow);
                var offs = new List<Vector3> { Vector3.zero }; string first = null;
                if (nudge) foreach (float f in new[] { .5f, 1f, 1.5f, 2f, 2.5f, 3f }) foreach (float l in new[] { 0f, .35f, -.35f }) offs.Add(bow * f + side * l);
                for (int i = 0; i < offs.Count; i++)
                {
                    var c = b.p + offs[i];
                    if (!Wet(c, b.yaw, b.kind, b.scale, minDepth)) { if (first == null) first = dryWhy; continue; }
                    if (!Apart(c, b.beam)) { Why("preblizu drugoj brodici"); return false; }
                    if (i > 0) nudged++;
                    b.p = c; boats.Add(b); return true;
                }
                Why(first ?? "suho"); return false;
            }

            int idx = 0;
            foreach (var s in slots.OrderBy(s => s.x).ThenBy(s => s.z))
            {
                var rng = new System.Random(1000 + idx++ * 7919);
                string area = Area(s.x, s.z);
                int k; float len;
                if (s.kind == "boat")
                {
                    len = 4.7f * (1 + .07f * s.variant); double r = rng.NextDouble();
                    if (len < 5.3f) k = r < .42 ? PASARA : r < .66 ? GUC : r < .9 ? PENTASA : GAJETA;
                    else k = r < .4 ? GAJETA : r < .75 ? PENTASA : PASARA;
                    if (k == GAJETA) len = Mathf.Max(len, 5.8f) + (float)rng.NextDouble() * .8f;
                }
                else if (area == "marina-stari") { k = s.kind == "sail" ? JEDRILICA : PENTASA; len = k == JEDRILICA ? 7.6f + (float)rng.NextDouble() : 6.2f + (float)rng.NextDouble() * .5f; }
                else { k = s.kind == "sail" ? JEDRILICA : JAHTA; len = 10 + s.variant; }
                float sc = Mathf.Clamp(len / Hulls[k].L, .72f, 1.4f);
                var pal = k <= GAJETA ? WoodPal : k == PENTASA ? GrpPal : YachtPal;
                var b = new Boat { p = W.U(s.x, 0, s.z), yaw = (float)(-s.yaw * Mathf.Rad2Deg), kind = k, scale = sc, beam = Beam(k, sc), col = pal[Pick(pal, rng.NextDouble())].c, phase = (float)rng.NextDouble(), where = area ?? Village(s.x) };
                TryPut(b, -.12f, true);
            }
            int fromSlots = boats.Count;

            // ---- a few on buoys off every harbour (seaward = Unity −z), bows to the bura (NE) ± 25°
            int buoys = 0;
            foreach (var a in areas)
            {
                var rng = new System.Random(a.id.Aggregate(17, (h, ch) => h * 31 + ch));
                var c = W.U((a.x0 + a.x1) / 2, 0, (a.z0 + a.z1) / 2); float r0 = (float)Math.Max(a.x1 - a.x0, a.z1 - a.z0) * .5f;
                int want = a.id.StartsWith("marina") ? 4 : 3, got = 0;
                for (int tr = 0; tr < 80 && got < want; tr++)
                {
                    float ang = (float)(rng.NextDouble() * 150 - 75) * Mathf.Deg2Rad, dist = r0 + 40 + (float)rng.NextDouble() * 100;
                    var p = c + new Vector3(Mathf.Sin(ang), 0, -Mathf.Cos(ang)) * dist;
                    double rk = rng.NextDouble(); int k = rk < .3 ? GAJETA : rk < .6 ? PENTASA : rk < .85 ? JEDRILICA : PASARA;
                    float sc = k == JEDRILICA ? .8f + (float)rng.NextDouble() * .3f : .9f + (float)rng.NextDouble() * .25f;
                    var pal = k <= GAJETA ? WoodPal : k == PENTASA ? GrpPal : YachtPal;
                    var b = new Boat { p = p, yaw = 45 + (float)(rng.NextDouble() * 50 - 25), kind = k, scale = sc, beam = Beam(k, sc), col = pal[Pick(pal, rng.NextDouble())].c, phase = (float)rng.NextDouble(), where = a.id + " (bova)" };
                    if (boats.Any(o => (o.p - p).sqrMagnitude < 22 * 22)) continue;
                    if (TryPut(b, -1.5f, false)) got++;
                }
                buoys += got;
            }

            // ---- the component
            var go = WorldBuilder.Root("Boats");
            var mbs = go.AddComponent<MooredBoats>();
            mbs.mat = mat; mbs.near = near; mbs.far = far; mbs.kindNames = Names.ToArray();
            mbs.bob = Bobs.Select(x => new Vector4(x.heave, x.roll * Mathf.Deg2Rad, x.pitch * Mathf.Deg2Rad, 2 * Mathf.PI / x.period)).ToArray();
            mbs.halfLen = Hulls.Select(x => x.L * .5f + x.rake).ToArray();
            mbs.boats = boats.Select(b => new Vector4(b.p.x, 0, b.p.z, b.yaw)).ToArray();
            mbs.kind = boats.Select(b => b.kind).ToArray(); mbs.scale = boats.Select(b => b.scale).ToArray();
            mbs.tint = boats.Select(b => { var l = b.col.linear; return new Vector4(l.r, l.g, l.b, b.phase); }).ToArray();
            EditorUtility.SetDirty(mbs);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            var perKind = string.Join(", ", Enumerable.Range(0, Names.Length).Select(k => $"{Names[k]} {boats.Count(b => b.kind == k)}"));
            var perWhere = string.Join(", ", boats.GroupBy(b => b.where).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}"));
            return $"[brodice] {boats.Count} brodica ({fromSlots} na {slots.Count} vezova iz coast.json, {nudged} pomaknuto, {buoys} na bovama); vrste: {perKind}\n" +
                   $"  po mjestima: {perWhere}\n  odbijeno: {string.Join(", ", why.Select(x => $"{x.Key} {x.Value}"))}\n  trokuti blizu/daleko: {string.Join(", ", tris)}; {sw.ElapsedMilliseconds / 1000.0:F1} s";
        }

        // ------------------------------------------------------------------ shots (batch, no play mode)
        /// -executeMethod Magistrala.EditorTools.BoatsBuilder.Shots -out <dir>: Kaštel Stari, Marina Kaštela, Lukšić, a view from the
        /// coast road; draws / instances / triangles with and without the boats (brojke.md); reopens Magistrala.unity at the end
        public static void Shots()
        {
            var a = Environment.GetCommandLineArgs(); int oi = Array.IndexOf(a, "-out");
            string dir = oi >= 0 && oi + 1 < a.Length ? a[oi + 1] : "Temp/brodice"; Directory.CreateDirectory(dir);
            try
            {
                EditorSceneManager.OpenScene(MainScene.PATH, OpenSceneMode.Single);
                ShotsIn(dir);
            }
            catch (Exception e) { Debug.LogError("[brodice] " + e); }
            MooredBoats.Off = false;
            EditorSceneManager.OpenScene(MainScene.PATH, OpenSceneMode.Single);   // discard: the main scene stays the last one
        }

        /// the shots in the open scene (nothing saved)
        public static void ShotsIn(string dir)
        {
            {
                var mb = UnityEngine.Object.FindFirstObjectByType<MooredBoats>();
                if (!mb) throw new Exception("no MooredBoats in the scene (Brodice: postavi)");
                mb.Init();
                var pc = UnityEngine.Object.FindFirstObjectByType<Magistrala.Traffic.ParkedCars>(); if (pc) pc.Init();
                var cam = new GameObject("brodice_cam").AddComponent<Camera>(); cam.nearClipPlane = .1f; cam.farClipPlane = 3000;
                var main = Camera.main; if (main) { cam.CopyFrom(main); cam.nearClipPlane = .1f; cam.farClipPlane = 3000; }
                Physics.SyncTransforms();
                float Ground(Vector3 p) => Physics.Raycast(new Vector3(p.x, p.y + 80, p.z), Vector3.down, out var h, 300, ~0, QueryTriggerInteraction.Ignore) ? h.point.y : 0;
                var poses = new List<(string name, Vector3 eye, Vector3 look, float fov)>();
                // a harbour: the boats within r of its box centre; the eye on land (riva) towards the north, the first clear one
                void Harbour(string name, double gx, double gz, float r, float[] dists, float up, float fov)
                {
                    var c0 = W.U(gx, 0, gz);
                    var ps = mb.boats.Select(b => (Vector3)b).Where(p => (p - c0).sqrMagnitude < r * r).ToList();
                    var c = ps.Count > 0 ? ps.Aggregate(Vector3.zero, (s, p) => s + p) / ps.Count : c0;
                    foreach (float d in dists)
                        for (int k = 0; k < 13; k++)
                        {
                            float an = (k % 2 == 0 ? 1 : -1) * (k + 1) / 2 * 15 * Mathf.Deg2Rad;
                            var e = c + new Vector3(Mathf.Sin(an), 0, Mathf.Cos(an)) * d; float g = Ground(e);
                            if (g < .3f && up < 5) continue;   // a pedestrian on the riva, not in the sea
                            e.y = Mathf.Max(g, 0) + up;
                            if (Physics.CheckSphere(e, .6f, ~0, QueryTriggerInteraction.Ignore)) continue;
                            var look = c + Vector3.up * .8f;
                            if (Physics.Linecast(e, look + (e - look).normalized * 6, out var hh, ~0, QueryTriggerInteraction.Ignore) && hh.point.y > .5f) continue;
                            poses.Add((name, e, look, fov)); return;
                        }
                    poses.Add((name, c + new Vector3(0, 25, 45), c, fov));
                    Debug.Log($"[brodice] {name}: no clear eye, overview instead");
                }
                Harbour("kastel-stari-mandrac", -7750, -4557, 80, new[] { 28f, 40f, 55f }, 1.7f, 55);
                Harbour("kastel-stari-pregled", -7750, -4557, 80, new[] { 70f }, 14f, 50);
                Harbour("marina-kastela", -3083, -4167, 190, new[] { 60f, 90f, 130f }, 1.7f, 55);
                Harbour("marina-kastela-pregled", -3083, -4167, 190, new[] { 170f }, 28f, 50);
                Harbour("luksic-mandrac", -6127, -4667, 70, new[] { 30f, 45f, 60f }, 1.7f, 55);
                Harbour("sucurac-mandrac", -1231, -4141, 70, new[] { 30f, 45f, 60f }, 1.7f, 55);
                // the coast road: a carriageway point 60–220 m from the Lukšić / Kambelovac / Novi boats with a clear sight, truck cab height
                var roads = StreetsBuilder.Carriageways();
                foreach (var (nm, gx, gz) in new[] { ("cesta-luksic", -6127.0, -4667.0), ("cesta-kambelovac", -4480.0, -4476.0), ("cesta-novi", -8190.0, -4365.0) })
                {
                    var c = W.U(gx, 0, gz); bool ok = false;
                    foreach (var rd in roads.OrderBy(r => Math.Abs((W.U(r.ax, 0, r.az) - c).magnitude - 120)))
                    {
                        var e = W.U(rd.ax, 0, rd.az); float d = (e - c).magnitude; if (d < 60 || d > 220) continue;
                        e.y = Ground(e) + 2.3f; var look = c + Vector3.up * .5f;
                        if (Physics.Linecast(e, look + (e - look).normalized * 8, out var hh, ~0, QueryTriggerInteraction.Ignore) && hh.point.y > .3f) continue;
                        poses.Add((nm, e, look, 50)); ok = true; break;
                    }
                    if (!ok) Debug.Log($"[brodice] {nm}: no road point with a clear sight");
                }

                // close-up: a pasara near the Kaštel Stari / Lukšić / Novi riva, 3/4 from the bow side, 9 m off, 3 m up
                {
                    var c0 = W.U(-7000, 0, -4500);
                    var ids = Enumerable.Range(0, mb.boats.Length).Where(i => mb.kind[i] == PASARA).OrderBy(i => ((Vector3)mb.boats[i] - c0).sqrMagnitude).Take(12);
                    foreach (int i in ids)
                    {
                        var p = (Vector3)mb.boats[i]; var rot = Quaternion.Euler(0, mb.boats[i].w, 0);
                        var e = p + rot * new Vector3(4.5f, 0, 7.5f) + Vector3.up * 3f;
                        if (Physics.CheckSphere(e, .5f, ~0, QueryTriggerInteraction.Ignore)) continue;
                        poses.Add(("pasara-izbliza", e, p + Vector3.up * .3f, 40)); break;
                    }
                }
                var sb = new System.Text.StringBuilder($"# Brodice — {mb.boats.Length} brodica, vrste {string.Join(", ", mb.kindNames)}\n\n");
                sb.AppendLine("| kadar | brodica crtano | pozivi (RenderMeshInstanced, vidljivi) | blizu (LOD0, baca sjenu) | daleko (LOD1) | trokuti brodica (vidljivi prolaz) |");
                sb.AppendLine("|---|---|---|---|---|---|");
                foreach (var p in poses)
                {
                    cam.transform.SetPositionAndRotation(p.eye, Quaternion.LookRotation(p.look - p.eye)); cam.fieldOfView = p.fov;
                    MooredBoats.Off = false;
                    Render(cam, mb, pc, Path.Combine(dir, p.name + ".png"));
                    int drawn = mb.DrawnLastFrame, calls = MooredBoats.Calls, tris = MooredBoats.Tris;
                    var cp = cam.transform.position; int nNear = mb.boats.Count(b => ((Vector3)b - cp).sqrMagnitude < mb.lodDist * mb.lodDist);
                    sb.AppendLine($"| {p.name} | {drawn} | {calls} | ≤{nNear} | {drawn - Math.Min(drawn, nNear)}+ | {tris / 1000.0:F1}k |");
                    Debug.Log($"[brodice] {p.name}: eye {p.eye} look {p.look}");
                }
                File.WriteAllText(Path.Combine(dir, "brojke.md"), sb.ToString());
                Debug.Log("[brodice]\n" + sb);
            }
        }

        static readonly Plane[] planes = new Plane[6];
        static string Render(Camera cam, MooredBoats mb, Magistrala.Traffic.ParkedCars pc, string file)
        {
            const int Wd = 1600, Ht = 900;
            ShaderUtil.allowAsyncCompilation = false;
            var rt = new RenderTexture(Wd, Ht, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.aspect = (float)Wd / Ht; cam.targetTexture = rt;
            // the queued RenderMeshInstanced draws are not picked up by cam.Render() in batch mode: the counts come from Draw(), the
            // picture from temporary renderers (same meshes / material / per-boat colour and phase, the same level choice)
            mb.Draw(cam, cam); if (pc) pc.Draw(cam, cam);
            var tmp = new List<GameObject>();
            if (!MooredBoats.Off)
            {
                var cp = cam.transform.position; GeometryUtility.CalculateFrustumPlanes(cam, planes);
                for (int i = 0; i < mb.boats.Length; i++)
                {
                    var p = (Vector3)mb.boats[i]; float d = (p - cp).magnitude, r = mb.halfLen[mb.kind[i]] * mb.scale[i];
                    if (d > Mathf.Min(mb.drawDist, 60 + r * 110) || !GeometryUtility.TestPlanesAABB(planes, new Bounds(p, Vector3.one * r * 2.4f))) continue;
                    var go = new GameObject("boat_tmp"); go.transform.SetPositionAndRotation(p, Quaternion.Euler(0, mb.boats[i].w, 0)); go.transform.localScale = Vector3.one * mb.scale[i];
                    go.AddComponent<MeshFilter>().sharedMesh = d < mb.lodDist ? mb.near[mb.kind[i]] : mb.far[mb.kind[i]];
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mb.mat; mr.shadowCastingMode = d < mb.lodDist ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    var bp = new MaterialPropertyBlock(); bp.SetVector("_BoatTint", mb.tint[i]); bp.SetVector("_BoatBob", mb.bob[mb.kind[i]]); mr.SetPropertyBlock(bp);
                    tmp.Add(go);
                }
            }
            cam.Render(); cam.Render();   // first frame warms up
            foreach (var g in tmp) UnityEngine.Object.DestroyImmediate(g);
            string stats = "";
            if (file != null)
            {
                RenderTexture.active = rt;
                var tex = new Texture2D(Wd, Ht, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, Wd, Ht), 0, 0); tex.Apply();
                File.WriteAllBytes(file, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
            }
            cam.targetTexture = null; RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(rt);
            return stats;
        }
    }
}
