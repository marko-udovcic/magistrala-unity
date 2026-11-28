using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    /// What a face is made of. Wall / Roof / Trim / Balcony / Shutter / Detail all go to submesh 0 (Magistrala/Facade, one
    /// texture array: the layer and tint come from the house's Look), Glass to submesh 1 (Magistrala/WindowInterior), Rail to
    /// submesh 2 (cutout railing panels, URP Lit).
    /// Yard: dry-stone yard walls (rubble layer, untinted). Hedge: trimmed yard hedges (the plaster array's hedge layer,
    /// BuildingTextures.PHedge, submesh 0). Rail also carries the yards' wire fences (House_Rail's atlas, FenceV).
    public enum Slot { Wall, Roof, Glass, Shutter, Trim, Balcony, Rail, Detail, Yard, Hedge }

    public sealed class HouseGeo
    {
        public List<Vector3> v = new List<Vector3>();
        public List<Vector3> n = new List<Vector3>();
        public List<Vector2> uv = new List<Vector2>();
        public List<Vector4> d = new List<Vector4>();
        public List<Color> c = new List<Color>();
        public List<Vector4> o = new List<Vector4>(); // UV2: baked occlusion, sill drip mask, stone darkening / greying (Magistrala/Facade)
        public List<int>[] sub = { new List<int>(), new List<int>(), new List<int>(), new List<int>(), new List<int>() }; // 3 / 4: pack details (PaketDetalji)
    }

    /// Per-building appearance after TIPOLOGIJA.md (references/fotografije/zgrade-predlosci): family A old stone house,
    /// B stone ground floor + plaster above, C new plastered house (apartments). Colours are EFFECTIVE albedos (what the wall
    /// shows: plaster layer x tint), calibrated on the Kaštela photos (riva Kaštel Stari 20211204-33: sunlit plaster #C2B9A6,
    /// stone #A5A191; Sućurac 20220206-16: stone #908A81; Sućurac panoramio: ochre render #AA9872, derelict #817D67), so the
    /// game's bright sun (2.9 + exposure +0.22, ACES) lands near the photos and not on paper white.
    public struct Look
    {
        public int family; // 0 A, 1 B, 2 C, 3 block (apartment), 4 industrial
        public Color wall, trim, shutter, roof, balcony;
        public int wallCode, trimLayer; public float split, dirt;
        public bool leaves, rollers, derelict, unfinished;
        public int bare; // C houses without paint: 1 grey concrete / cement render, 2 bare brick (opeka)
        public float stoneDark, stoneGrey; // per-house tone of the (untinted) stone layer: darker / greyer
        public float flatRoll, gableRoll; // roof draws (HouseBuilder decides with the footprint)
        public float roofFade; // roof tiles: 0 the texture's terracotta x roof, 1 its luminance x roof (a free colour: OrthoRoofs)
        /// plaster variant (BuildingTextures.P*: the Poly Haven plaster array, 0 = the old layer), old = weathered render (peeled
        /// patches, cracks: Magistrala/Facade); concrete / measured: from Mapillary (MapillaryFacades)
        public int variant; public bool old, concrete, measured;
        /// Paket2 areas: the roof takes the pack's barrel tiles (BuildingTextures.PRoofPack)
        public bool roofPack;

        static Color H(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
        /// the plaster / concrete layers are grey ~#DCDCDC (linear .71): the tint that makes the wall show `hex`
        static Color E(string h) { var c = H(h).linear; return new Color(Mathf.Clamp01(c.r / .71f), Mathf.Clamp01(c.g / .71f), Mathf.Clamp01(c.b / .71f)).gamma; }
        // weathered off-white / greyish white (never paper white), cream and beige
        static readonly string[] OffWhite = { "C9C3B5", "C4BEAF", "CCC6B8", "BFB9AB", "C6BFAE", "B9B5AA", "C2BCB0" };
        static readonly string[] Beige = { "C8B99B", "C2B392", "CDBEA0", "BDAF92", "C6B596" };
        // warm ochre, sand, light terracotta / peach
        static readonly string[] Warm = { "C2AC86", "BEA889", "C6B08E", "B9A283", "C0A98A", "BDA08A", "C1A592", "B79D85" };
        // faded pastels (rarer): sage, dusty pink, pale yellow, grey-blue, pale salmon
        static readonly string[] Pastel = { "B4BA9F", "C2AEA2", "C8BC96", "AEB3AE", "C6A797", "A9705A" };
        // grey concrete / unpainted cement render
        static readonly string[] Bare = { "9E9B93", "A6A297", "A9A395", "958F84", "ACA89D" };
        // zgrade (orthophoto pass): pale yellows, ochres and stone-like greiges of the Dalmatian coast (general palette)
        static readonly string[] PaleYellow = { "CFC59E", "D2C8A4", "C9BC93", "D5CCAD", "CCC09A" };
        static readonly string[] Ochre = { "C4A57A", "BE9F74", "C6A980", "B99B70", "C2A682" };
        static readonly string[] Greige = { "B8B2A2", "AFA899", "BDB5A3", "B3AC9B" };
        static readonly string[] Derelict = { "6E6A58", "807A64", "77725E", "817D67" };
        static Color Pick(System.Random r, string[] a) => E(a[r.Next(a.Length)]);
        public static Color Desat(Color c, float k) { float l = .3f * c.r + .59f * c.g + .11f * c.b; return Color.Lerp(c, new Color(l, l, l, c.a), k); }

        /// the plaster variant and weathering by age / type, after Pick (and MapillaryFacades.Apply); own draws (seed), so the
        /// other draws of the house stay as they were; folds them into wallCode (Magistrala/Facade UV1.x)
        public static void Finish(ref Look k, int seed, bool core, bool pack2 = false)
        {
            var r = new System.Random(seed * 97 + 13); double a = r.NextDouble(), b = r.NextDouble();
            int V(params (int v, double p)[] t) { double acc = 0; foreach (var e in t) { acc += e.p; if (a < acc) return e.v; } return t[t.Length - 1].v; }
            float warm = k.wall.r - k.wall.b; // sRGB: yellow / ochre walls are clearly warmer
            if (k.family == 0) { k.variant = 0; k.old = false; }
            else if (k.derelict) { k.variant = V((BuildingTextures.PCracked, .4), (BuildingTextures.PRedWeathered, .3), (BuildingTextures.PWhiteRough, .3)); k.old = true; }
            else if (k.family == 4) { k.variant = V((BuildingTextures.PGrey, .5), (BuildingTextures.PCracked, .25), (0, .25)); k.old = false; }
            else if (k.concrete || k.bare == 1) { k.variant = V((BuildingTextures.PGrey, .6), (BuildingTextures.PCracked, .25), (BuildingTextures.PConcreteWall, .15)); k.old = false; }
            else if (k.family == 1 || (core && k.family == 2))
            {
                k.variant = warm > .12f && a < .5 ? BuildingTextures.PRedWeathered
                    : V((BuildingTextures.PConcreteWall, .4), (BuildingTextures.PWhiteRough, .25), (BuildingTextures.PCracked, .2), (BuildingTextures.PRedWeathered, .15));
                k.old = b < (k.family == 1 ? .6 : .45);
            }
            else if (k.family == 3) { k.variant = V((BuildingTextures.PPainted, .4), (BuildingTextures.PGrey, .3), (BuildingTextures.PPreconcrete, .3)); k.old = false; }
            else
            {
                k.variant = warm > .1f ? V((BuildingTextures.PYellow, .55), (BuildingTextures.PBeige, .45))
                    : V((BuildingTextures.PPainted, .4), (BuildingTextures.PBeige, .35), (BuildingTextures.PPreconcrete, .15), (BuildingTextures.PConcreteWall, .1));
                k.old = k.dirt > .6f && b < .2 && (k.variant == BuildingTextures.PPreconcrete || k.variant == BuildingTextures.PConcreteWall);
            }
            // old peeling render (worn_plaster_wall, Marko's approved pick 25. 11.): on 40 % of the old / derelict plastered houses;
            // its own draw after the others, so every other house keeps its variant
            if (k.old && k.family != 0 && r.NextDouble() < .4) k.variant = BuildingTextures.PWorn;
            // the UKRAINIAN CITY pack's layers (27. 11.): its dressed stone on a third of the all-stone houses, its light plasters on
            // a third of the newer plastered houses (own draw, last: every other house keeps its variant)
            double pk = new System.Random(seed * 131 + 29).NextDouble();
            if (k.family == 0 && !k.derelict && pk < .35) k.variant = BuildingTextures.PStoneE;
            else if (k.family == 2 && !k.old && !k.derelict && !k.concrete && k.bare == 0 && !k.measured
                && (k.variant == BuildingTextures.PPainted || k.variant == BuildingTextures.PBeige || k.variant == BuildingTextures.PYellow) && pk < .34)
                k.variant = pk < .17 ? BuildingTextures.PPackH : BuildingTextures.PPackJ;
            // Paket2 areas (paket2-podrucja.json "zbuka"): the pack's dressed stone on two thirds of the stone houses, its light plasters
            // on two thirds of the sound plastered ones (B houses' upper floors too); the same draw pk, so a house outside keeps its look
            else if (pack2 && k.family == 0 && !k.derelict && pk < .65) k.variant = BuildingTextures.PStoneE;
            else if (pack2 && (k.family == 2 || k.family == 1) && !k.old && !k.derelict && !k.concrete && k.bare == 0 && !k.measured && k.variant != BuildingTextures.PWorn && pk < .68)
                k.variant = pk < .52 ? BuildingTextures.PPackH : BuildingTextures.PPackJ;
            k.wallCode = k.wallCode % 64 + 64 * k.variant + (k.old ? 1024 : 0);
        }

        /// ground = the building's ground height (m above sea): old cores lie by the shore; core = in an old town core
        public static Look Pick(System.Random r, string kind, float ground, float height, bool core = false)
        {
            var k = new Look();
            bool ind = string.Equals(kind, "industrial", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "warehouse", StringComparison.OrdinalIgnoreCase);
            bool apt = string.Equals(kind, "apartment", StringComparison.OrdinalIgnoreCase);
            double pOld = core ? .62 : ground < 10 ? .4 : ground < 25 ? .2 : ground < 60 ? .1 : .04;
            double f = r.NextDouble(), c = r.NextDouble(), s = r.NextDouble(), d = r.NextDouble(), u = r.NextDouble(), q = r.NextDouble();
            double tone = r.NextDouble(), b = r.NextDouble(), st = r.NextDouble();
            k.flatRoll = (float)r.NextDouble(); k.gableRoll = (float)r.NextDouble();
            k.family = ind ? 4 : apt ? 3 : height > 13 ? 2 : f < pOld * .6 ? 0 : f < pOld ? 1 : 2;
            k.derelict = !ind && c > .955;
            // 34 % off-white, 16 % beige, 14 % warm, 10 % pale yellow, 8 % ochre, 6 % pastel, 5 % greige (the rest off-white)
            k.wall = ind ? Color.Lerp(E("9F9D96"), E("BDBAB2"), (float)d) : k.derelict ? Pick(r, Derelict)
                : c < .34 ? Pick(r, OffWhite) : c < .5 ? Pick(r, Beige) : c < .64 ? Pick(r, Warm) : c < .74 ? Pick(r, PaleYellow)
                : c < .82 ? Pick(r, Ochre) : c < .88 ? Pick(r, Pastel) : c < .93 ? Pick(r, Greige) : Pick(r, OffWhite);
            // per-house tone: +-9 % brightness, a little hue drift (repainted / sun-bleached / damp)
            float tb = .91f + .18f * (float)tone; k.wall = new Color(k.wall.r * tb, k.wall.g * tb, k.wall.b * tb * (.98f + .04f * (float)q));
            k.dirt = (float)(k.derelict ? .9 : k.family <= 1 ? .45 + .45 * d : ind ? .35 + .35 * d : .2 + .55 * d);
            int P = BuildingTextures.Plaster, S = BuildingTextures.Stone;
            k.wallCode = k.family == 0 ? S + 8 * S : k.family == 1 ? S + 8 * P : ind ? BuildingTextures.Concrete * 9 : P * 9;
            k.split = k.family == 1 ? (height > 8.5f && q < .45 ? 6.1f : q < .75 ? 3.2f : 2.4f) : 0;
            // the stone: limestone from warm cream to grey, sun-bleached or damp
            k.stoneDark = .05f + .28f * (float)st; k.stoneGrey = (float)(st < .5 ? .35 + .4 * q : .25 + .3 * q); // light beige-grey limestone (owner photos 44 / 48)
            if (k.family == 2 && !k.derelict && height > 2.5f)
            {
                // TIPOLOGIJA 9.8: unfinished top storey (bare brick, concrete columns, rebar), unpainted houses
                k.unfinished = u < .14 && height > 5;
                k.bare = b < .1 ? 1 : b < .15 ? 2 : 0;
                if (k.bare == 1) { k.wallCode = BuildingTextures.Concrete * 9; k.wall = Pick(r, Bare); }
                else if (k.bare == 2) k.wallCode = BuildingTextures.Brick * 9;
                if (k.unfinished) { k.wallCode = (k.bare == 2 ? BuildingTextures.Brick : k.bare == 1 ? BuildingTextures.Concrete : P) + 8 * BuildingTextures.Brick; k.split = Mathf.Max(2.6f, height - 2.9f); }
            }
            k.trim = k.derelict ? E("C9C5B9") : Color.Lerp(k.wall, E("D6D1C5"), .55f);
            k.trimLayer = k.family <= 1 ? S : P;
            if (k.family <= 1) k.trim = Color.white;
            // shutters: faded green 50 % / brown 30 % / grey 15 % / blue 5 %, one colour per house, sun-bleached
            // owner photos 43 / 44 / 50: white louvres on many old stone houses, dark green everywhere, some brown
            k.shutter = k.family <= 1 && s < .38 ? (q < .5 ? E("C9C6BE") : E("BDBAB1"))
                : s < .72 ? (q < .5 ? H("38543F") : H("4A6A52")) : s < .9 ? (q < .5 ? H("5A4232") : H("6B5444")) : s < .97 ? H("5A5C5D") : H("53687A");
            if (k.derelict) k.shutter = H("7C8F6A");
            k.shutter = Desat(k.shutter, .15f + .2f * (float)d);
            k.leaves = !ind && (k.family <= 1 ? d < .92 : k.family == 2 ? u > .5 && u < .85 : u < .3);
            k.rollers = !ind && !k.leaves && (k.family >= 2 ? u > .2 : false);
            // roof: #C8532F +- 12 %, older roofs faded / browner
            double rt = r.NextDouble();
            k.roof = k.family <= 1 && rt < .4 ? Color.Lerp(Color.white, new Color(.86f, .97f, 1.12f), (float)r.NextDouble())
                : new Color(.9f + .24f * (float)rt, .9f + .2f * (float)r.NextDouble(), .92f + .16f * (float)rt);
            k.roof *= .74f; // the texture's mean is #C8532F in sRGB; darker under the bright Dalmatian sun
            k.balcony = Color.Lerp(E("A9A69E"), k.trim, .4f);
            return k;
        }
    }

    /// <summary>Editor-only, deterministic house geometry in W.U world coordinates. No scene/asset writes in AddHouse.</summary>
    public static class HouseBuilder
    {
        const int SlotCount = 5;
        /// Detail level of the next AddHouse calls (LODGroup per chunk, BuildingsFromTiles): 0 = full (recessed windows with
        /// reveals, 3D sills, railing bars, clutter), 1 = mid (one wall quad per facade, glass / sills / frames / shutters as
        /// thin overlays, chimneys, roof tanks), 2 = far (walls, glass, roofs). The same seed gives the same house in every
        /// level: the random draws do not depend on Lod.
        public static int Lod;
        static bool detailRoad; // the facade being built is along a road (PaketDetalji)
        const float FloorHeight = 3f;

        // the house being emitted (paint of the facade slots and the data channel)
        static Look look; static float gY, tY; static Color detailTint = Color.white; static Vector4 glassData;

        static Vector4 Data(Slot s, float y)
        {
            float code = s == Slot.Wall ? look.wallCode : s == Slot.Roof ? BuildingTextures.Roof * 9 + (look.roofPack ? 64 * BuildingTextures.PRoofPack : 0) : s == Slot.Trim ? look.trimLayer * 9
                : s == Slot.Balcony ? BuildingTextures.Concrete * 9 + (metalSlab ? 64 * BuildingTextures.PMetal : 0) : s == Slot.Shutter ? BuildingTextures.Louvre * 9
                : s == Slot.Yard ? BuildingTextures.Rubble * 9 : s == Slot.Hedge ? 64 * BuildingTextures.PHedge : BuildingTextures.Plain * 9;
            if (rustOn && s == Slot.Wall) code += 2048;
            return new Vector4(code, y - gY, tY - y, s == Slot.Detail ? look.dirt * .4f : look.dirt);
        }
        static Color Tint(Slot s)
        {
            Color t = s == Slot.Wall ? look.wall : s == Slot.Roof ? look.roof : s == Slot.Trim ? look.trim : s == Slot.Balcony ? look.balcony
                : s == Slot.Shutter ? look.shutter : s == Slot.Yard ? Color.white : s == Slot.Hedge ? hedgeTint : detailTint;
            // tints are authored in sRGB; vertex colours are read as linear; the roof tint is a multiplier (kept linear)
            if (Lod >= 3 && s == Slot.Wall) t = Color.Lerp(t, new Color(.3f, .32f, .34f), .15f);
            Color lin = s == Slot.Roof ? t : t.linear;
            lin.a = s == Slot.Wall ? look.split / 40f : s == Slot.Roof ? look.roofFade : 0; return lin;
        }

        // UV2 of the next faces: reveal depth (occlusion grows into the opening) and the drip mask under a sill
        static bool revealOn, dripOn; static Vector3 revealO, revealN; static float revealD, dripY0, dripY1;
        static Vector4 Occ(Vector3 p, Slot slot)
        {
            float x = revealOn ? Mathf.Clamp01(.2f + .8f * Vector3.Dot(revealO - p, revealN) / revealD) : 0;
            float y = dripOn ? Mathf.Clamp01((p.y - dripY0) / (dripY1 - dripY0)) : 0;
            // the frames, sills and quoins of dressed stone stay light (kamena erta: lighter than the wall, owner photos 43 / 44)
            return slot == Slot.Trim ? new Vector4(x, y, 0, look.stoneGrey * .5f) : new Vector4(x, y, look.stoneDark, look.stoneGrey);
        }

        // As in BuildingsFromTiles.Tri: orient AFTER the W.U reflection. Swap UVs with the vertices.
        static void Tri(HouseGeo g, Slot slot, Vector3 a, Vector3 b, Vector3 c,
            Vector2 ta, Vector2 tb, Vector2 tc, Vector3 want)
        {
            Vector3 cross = Vector3.Cross(b - a, c - a);
            if (cross.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(cross, want) < 0)
            {
                var p = b; b = c; c = p;
                var t = tb; tb = tc; tc = t;
                cross = -cross;
            }
            Vector3 normal = cross.normalized;
            int index = g.v.Count;
            g.v.Add(a); g.v.Add(b); g.v.Add(c);
            g.n.Add(normal); g.n.Add(normal); g.n.Add(normal);
            g.uv.Add(ta); g.uv.Add(tb); g.uv.Add(tc);
            if (slot == Slot.Glass) { g.d.Add(glassData); g.d.Add(glassData); g.d.Add(glassData); }
            else { g.d.Add(Data(slot, a.y)); g.d.Add(Data(slot, b.y)); g.d.Add(Data(slot, c.y)); }
            var col = Tint(slot); g.c.Add(col); g.c.Add(col); g.c.Add(col);
            g.o.Add(Occ(a, slot)); g.o.Add(Occ(b, slot)); g.o.Add(Occ(c, slot));
            int sm = slot == Slot.Glass ? 1 : slot == Slot.Rail ? 2 : 0;
            g.sub[sm].Add(index); g.sub[sm].Add(index + 1); g.sub[sm].Add(index + 2);
        }

        static void Quad(HouseGeo g, Slot slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td, Vector3 want)
        {
            Tri(g, slot, a, b, c, ta, tb, tc, want);
            Tri(g, slot, a, c, d, ta, tc, td, want);
        }

        // Planar coordinates on the actual face, in metres (the shader tiles per texture layer).
        static void Face(HouseGeo g, Slot slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 want)
        {
            Vector3 u = (b - a).normalized;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            Vector3 v = Vector3.Cross(normal, u).normalized;
            if (Vector3.Dot(d - a, v) < 0) v = -v;
            // anchor on the world position so neighbouring faces of one plane continue the pattern
            Vector2 o = new Vector2(Vector3.Dot(a, u), Vector3.Dot(a, v));
            Quad(g, slot, a, b, c, d, o,
                o + new Vector2(Vector3.Dot(b - a, u), Vector3.Dot(b - a, v)),
                o + new Vector2(Vector3.Dot(c - a, u), Vector3.Dot(c - a, v)),
                o + new Vector2(Vector3.Dot(d - a, u), Vector3.Dot(d - a, v)), want);
        }

        struct Facade
        {
            public Vector3 origin, along, outward;
            public float length, baseY;
            public Vector3 P(float x, float y, float depth = 0) => origin + along * x + Vector3.up * y + outward * depth;
        }

        static void Wall(HouseGeo g, Facade f, Slot slot, float x0, float x1, float y0, float y1)
        {
            if (x1 - x0 < .001f || y1 - y0 < .001f) return;
            Quad(g, slot, f.P(x0, y0), f.P(x1, y0), f.P(x1, y1), f.P(x0, y1),
                new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1), f.outward);
        }

        /// A yard wall along `pts` (Unity world, y = the ground at each point), one open run, for the house AddHouse emitted
        /// last (its look). kind 0 dry stone (rubble), 1 the house's wall (plaster / stone) with a concrete cap, 2 a low plastered
        /// wall with a railing on it, 3 a low concrete base (of a wire fence, YardFence). Gate posts at the run's ends when `posts`.
        public static void YardWall(HouseGeo g, List<Vector3> pts, float h, int kind, bool postA, bool postB, float pier = 0)
        {
            float pierH = pier > 0 ? pier : h;
            if (pts.Count < 2) return;
            float t = kind == 3 ? .1f : .15f; // half thickness
            float g0 = float.MaxValue; foreach (var p in pts) g0 = Mathf.Min(g0, p.y);
            float sg = gY, st = tY; gY = g0; tY = g0 + 100;
            Slot body = kind == 0 ? Slot.Yard : kind == 3 ? Slot.Balcony : Slot.Wall;
            float wh = kind == 2 ? .75f : h;
            Vector3 up = Vector3.up * wh, dn = Vector3.down * .35f;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector3 p = pts[i], q = pts[i + 1], e = q - p; e.y = 0; if (e.sqrMagnitude < 1e-4f) continue; e.Normalize();
                Vector3 sd = new Vector3(-e.z, 0, e.x);
                // overlap the joints by the thickness: no slits at bends
                Vector3 pe = i == 0 ? p : p - e * t, qe = i + 2 == pts.Count ? q : q + e * t;
                Face(g, body, pe + sd * t + dn, qe + sd * t + dn, qe + sd * t + up, pe + sd * t + up, sd);
                Face(g, body, qe - sd * t + dn, pe - sd * t + dn, pe - sd * t + up, qe - sd * t + up, -sd);
                if (kind == 0 || kind == 3) Face(g, body, pe + sd * t + up, pe - sd * t + up, qe - sd * t + up, qe + sd * t + up, Vector3.up);
                else
                {
                    // concrete coping, 3 cm proud each side
                    Vector3 c0 = up, c1 = up + Vector3.up * .07f; float tc = t + .03f;
                    Face(g, Slot.Balcony, pe + sd * tc + c1, pe - sd * tc + c1, qe - sd * tc + c1, qe + sd * tc + c1, Vector3.up);
                    Face(g, Slot.Balcony, pe + sd * tc + c0, qe + sd * tc + c0, qe + sd * tc + c1, pe + sd * tc + c1, sd);
                    Face(g, Slot.Balcony, qe - sd * tc + c0, pe - sd * tc + c0, pe - sd * tc + c1, qe - sd * tc + c1, -sd);
                    Face(g, Slot.Balcony, pe + sd * tc + c0, pe - sd * tc + c0, qe - sd * tc + c0, qe + sd * tc + c0, Vector3.down);
                    if (kind == 2 && Lod < 2) RailPanel(g, p + up + Vector3.up * .07f, q + up + Vector3.up * .07f, sd);
                }
            }
            // end faces
            for (int k = 0; k < 2; k++)
            {
                Vector3 p = k == 0 ? pts[0] : pts[pts.Count - 1], o = k == 0 ? pts[1] : pts[pts.Count - 2], e = p - o; e.y = 0; e.Normalize();
                Vector3 sd = new Vector3(-e.z, 0, e.x);
                Face(g, body, p - sd * t + dn, p + sd * t + dn, p + sd * t + up, p - sd * t + up, e);
                if ((k == 0 ? postA : postB) && Lod < 2)
                {
                    // gate post: a 44 cm pier a little taller than the wall, with a cap
                    float ph = Mathf.Max(wh, pierH) + .25f;
                    Box(g, kind == 3 ? Slot.Wall : body, p - e * .44f - sd * .22f + dn, e, sd, .44f, ph + .35f, .44f, false);
                    Box(g, Slot.Balcony, p - e * .47f - sd * .25f + Vector3.up * ph, e, sd, .5f, .08f, .5f);
                }
            }
            gY = sg; tY = st;
        }

        /// a YardWall pier standing alone, centred on c (Unity world, y = the ground), its faces along e (horizontal unit): where a
        /// frontage wall steps square to a new line (Frontage: the step sits in the pier)
        public static void YardPier(HouseGeo g, Vector3 c, Vector3 e, float h, int kind, float pier = 0)
        {
            if (Lod >= 2) return;
            e.y = 0; if (e.sqrMagnitude < 1e-6f) return; e.Normalize();
            Vector3 sd = new Vector3(-e.z, 0, e.x), dn = Vector3.down * .35f;
            float sg = gY, st = tY; gY = c.y; tY = c.y + 100;
            Slot body = kind == 0 ? Slot.Yard : Slot.Wall;
            float wh = kind == 2 ? .75f : h, ph = Mathf.Max(wh, pier > 0 ? pier : h) + .25f;
            Box(g, body, c - e * .22f - sd * .22f + dn, e, sd, .44f, ph + .35f, .44f, false);
            Box(g, Slot.Balcony, c - e * .25f - sd * .25f + Vector3.up * ph, e, sd, .5f, .08f, .5f);
            gY = sg; tY = st;
        }

        // ---- the railing atlas (Tools/zbuke/ograda.py; House_Rail repeats u 8x per UV unit, so u = metres / 8 is 1 m per repeat):
        // v bands: railing bars below BarV, post patches, green chain-link, galvanised chain-link (1.2 m of wire each)
        const float BarV = .12f, FenceBand = 1.2f;
        static readonly Vector2 BarUV = new Vector2(1f / 2048f, 1f / 2048f);
        static Vector2 FenceUV(float along, float y, bool galv) => new Vector2(along / 8f, ((galv ? 600f : 152f) + y / FenceBand * 416f) / 1024f);
        static Vector2 PostUV(bool galv) => new Vector2((galv ? .75f : .25f) / 8f, 136f / 1024f);
        static Color hedgeTint = Color.white;

        /// A wire fence (žičana ograda) along `pts` (Unity world, y = the ground at each point; one open run): chain-link from
        /// y0 to y1 above the ground (at most 1.2 m of wire), green plastic-coated or galvanised, both faces (House_Rail culls
        /// back faces), 5 cm steel posts every <= 2.5 m (near level only). In level 2 (the hull: shadow caster and collider, drawn
        /// with the facade material only, so its rail submesh is never rendered) the panel reaches the ground: the truck stops at it.
        public static void YardFence(HouseGeo g, List<Vector3> pts, float y0, float y1, bool galv)
        {
            if (pts.Count < 2) return;
            if (Lod >= 2) y0 = -.2f; else y1 = Mathf.Min(y1, y0 + FenceBand);
            float along = 0, wh = y1 - y0;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector3 p = pts[i], q = pts[i + 1], e = q - p; e.y = 0; float L = e.magnitude; if (L < 1e-3f) continue; e /= L;
                Vector3 sd = new Vector3(-e.z, 0, e.x), a = p + Vector3.up * y0, b = q + Vector3.up * y0, c = q + Vector3.up * y1, d = p + Vector3.up * y1;
                Vector2 ta = FenceUV(along, 0, galv), tb = FenceUV(along + L, 0, galv), tc = FenceUV(along + L, wh, galv), td = FenceUV(along, wh, galv);
                Quad(g, Slot.Rail, a, b, c, d, ta, tb, tc, td, sd);
                Quad(g, Slot.Rail, a, b, c, d, ta, tb, tc, td, -sd);
                along += L;
                if (Lod > 0) continue;
                int n = Mathf.Max(1, Mathf.CeilToInt(L / 2.5f));
                railUV = PostUV(galv);
                for (int k = 0; k <= n; k++)
                {
                    if (k == n && i + 2 < pts.Count) break; // the next segment's first post
                    Vector3 m = Vector3.Lerp(p, q, (float)k / n);
                    Box(g, Slot.Rail, m - e * .025f - sd * .025f + Vector3.up * (y0 - .05f), e, sd, .05f, wh + .1f, .05f, false);
                }
                railUV = BarUV;
            }
        }

        /// A trimmed hedge (živica) along `pts` (one open run): its outer face `off` m inside the line (`side` +1: inside is
        /// (-e.z, 0, e.x) of the run's direction e), `w` wide, `h` high, a chamfered top; near level: a station every ~1.3 m with
        /// a few cm of noise in width and height (an uneven trim), mid / hull: one piece per straight segment. Submesh 0 (the
        /// facade material's hedge layer), so no draw call of its own; the hull's copy casts the shadow and collides.
        public static void YardHedge(HouseGeo g, List<Vector3> pts, float side, float off, float w, float h, Color tint, float y0 = -.2f, HedgeShell.Sp sp = HedgeShell.Sp.Pittosporum, float overhang = 0, int seed = 0)
        {
            if (pts.Count < 2) return;
            // the box takes the leaf module's size (HedgeShell.Fit: the nearest module class, scaled), so the leaves sit on its skin
            if (HedgeShell.On) { bool onWall = y0 > 0; var f = HedgeShell.Fit(sp, onWall ? h - y0 : h, onWall); w = f.w; h = (onWall ? y0 : 0) + f.h; }
            float g0 = float.MaxValue; foreach (var p in pts) g0 = Mathf.Min(g0, p.y);
            float sg = gY, st = tY; gY = g0; tY = g0 + 100; hedgeTint = tint;
            // stations: ground point on the line, its inward vector (mitred at bends), jitter allowed
            var S = new List<(Vector3 p, Vector3 s, bool j)>();
            Vector3 prev = Vector3.zero;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector3 p = pts[i], q = pts[i + 1], e = q - p; e.y = 0; float L = e.magnitude; if (L < 1e-3f) continue; e /= L;
                Vector3 sd = new Vector3(-e.z, 0, e.x) * side, sv = sd;
                if (S.Count > 0) { var mi = (prev + sd).normalized; sv = mi / Mathf.Max(.5f, Vector3.Dot(mi, sd)); }
                S.Add((p, sv, S.Count > 0));
                int n = Lod == 0 ? Mathf.Max(1, Mathf.RoundToInt(L / 1.3f)) : 1;
                for (int k = 1; k < n; k++) S.Add((Vector3.Lerp(p, q, (float)k / n), sd, true));
                prev = sd;
            }
            if (S.Count == 0) { gY = sg; tY = st; return; }
            S.Add((pts[pts.Count - 1], prev, false));
            float Hash(Vector3 v, float k) { float x = Mathf.Sin(v.x * 12.9898f + v.z * 78.233f + k) * 43758.5453f; return x - Mathf.Floor(x); }
            (float hw, float hh) Size(int k)
            {
                var (p, s, j) = S[k];
                float hw = w / 2, hh = h;
                if (j && Lod == 0) { hw += (Hash(p, 1) - .5f) * .1f; hh += (Hash(p, 2) - .5f) * .14f; }
                return (hw, hh);
            }
            Vector3[] Profile(int k)
            {
                var (p, s, j) = S[k]; var (hw, hh) = Size(k);
                float c = Mathf.Min(.15f, hw * .4f), mid = off + w / 2;
                Vector3 P(float x, float y) => p + s * (mid + x) + Vector3.up * y;
                return new[] { P(-hw, y0), P(-hw, hh - c), P(-hw + c, hh), P(hw - c, hh), P(hw, hh - c), P(hw, y0) };
            }
            // the leaf shell over the box (near level only: instanced leaf modules, HedgeShell)
            if (Lod == 0 && HedgeShell.On)
                HedgeShell.AddRun(pts, side, off + w / 2, y0 > 0 ? y0 : 0, sp, y0 > 0 ? h - y0 : h, y0 > 0, seed != 0 ? seed : (int)(Mathf.Abs(pts[0].x * 31 + pts[0].z * 17) % 100000));
            var A = Profile(0);
            for (int k = 1; k < S.Count; k++)
            {
                var B = Profile(k); Vector3 s = S[k].s.normalized;
                Vector3[] want = { -s, -s + Vector3.up, Vector3.up, s + Vector3.up, s };
                for (int f = 0; f < 5; f++) Face(g, Slot.Hedge, A[f], B[f], B[f + 1], A[f + 1], want[f]);
                A = B;
            }
            // end caps
            for (int k = 0; k < 2; k++)
            {
                var Q = Profile(k == 0 ? 0 : S.Count - 1);
                Vector3 e = k == 0 ? S[0].p - S[1].p : S[S.Count - 1].p - S[S.Count - 2].p; e.y = 0; e.Normalize();
                Face(g, Slot.Hedge, Q[0], Q[5], Q[4], Q[1], e);
                Face(g, Slot.Hedge, Q[1], Q[4], Q[3], Q[2], e);
            }
            gY = sg; tY = st;
        }

        /// A clipped column of green (a cypress by a gate, Frontage): an octagonal core (the hedge layer) narrowing to a point,
        /// radius r0 at the foot, height h above p (ground); near level: the leaf shell too (HedgeShell.AddPillar)
        public static void HedgePillar(HouseGeo g, Vector3 p, float r0, float h, Color tint, HedgeShell.Sp sp, int seed)
        {
            float sg = gY, st = tY; gY = p.y; tY = p.y + 100; hedgeTint = tint;
            int rings = Lod == 0 ? 7 : 4, n = Lod == 0 ? 8 : 6;
            Vector3[] Ring(float y) { float r = HedgeShell.PillarR(r0, h, y) * .9f; var R = new Vector3[n]; for (int i = 0; i < n; i++) { float a = i * Mathf.PI * 2 / n; R[i] = p + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r); } return R; }
            var A = Ring(-.2f);
            for (int k = 1; k <= rings; k++)
            {
                float y = k == rings ? h * .985f : h * k / rings; var B = Ring(y);
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n; var m = (A[i] + A[j]) / 2 - p; m.y = 0;
                    Face(g, Slot.Hedge, A[i], A[j], B[j], B[i], m.normalized + Vector3.up * .2f);
                }
                A = B;
            }
            var top = p + Vector3.up * h;
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; Tri(g, Slot.Hedge, A[i], A[j], top, Vector2.zero, new Vector2(.3f, 0), new Vector2(.15f, .3f), Vector3.up); }
            if (Lod == 0) HedgeShell.AddPillar(p, r0, h, seed);
            gY = sg; tY = st;
        }

        /// A closed iron gate between two piers (Frontage): the railing atlas' bars from y 0.05 to h over the ground at a / b, a
        /// top and bottom rail; in the hull a plain panel (the truck stops at it)
        public static void YardGate(HouseGeo g, Vector3 a, Vector3 b, float h)
        {
            Vector3 e = b - a; e.y = 0; float L = e.magnitude; if (L < .2f) return; e /= L;
            Vector3 sd = new Vector3(-e.z, 0, e.x), lo = Vector3.up * .05f, hi = Vector3.up * h;
            Quad(g, Slot.Rail, a + lo, b + lo, b + hi, a + hi, new Vector2(0, 0), new Vector2(L / 3, 0), new Vector2(L / 3, BarV), new Vector2(0, BarV), sd);
            Quad(g, Slot.Rail, a + lo, b + lo, b + hi, a + hi, new Vector2(0, 0), new Vector2(L / 3, 0), new Vector2(L / 3, BarV), new Vector2(0, BarV), -sd);
            if (Lod > 0) return;
            Box(g, Slot.Rail, a + hi - sd * .025f, e, sd, L, .05f, .05f);
            Box(g, Slot.Rail, a + lo + Vector3.up * .1f - sd * .025f, e, sd, L, .05f, .05f);
            Box(g, Slot.Rail, a + e * (L / 2 - .02f) + lo - sd * .025f, e, sd, .04f, h - .05f, .05f);
        }

        /// the wall strip between the storey below and this row of windows, cut per column so the piece under each sill
        /// carries the drip mask (UV2.y: 1 under the sill, 0 at the window below); the front door's column stays open
        static void RowBelow(HouseGeo g, Facade f, int columns, float spacing, float width, float yb, float yt, int doorColumn)
        {
            float cursor = 0;
            for (int k = 0; k < columns; k++)
            {
                float centre = (k + .5f) * spacing, x0 = centre - width / 2 - .1f, x1 = centre + width / 2 + .1f;
                Wall(g, f, Slot.Wall, cursor, x0, yb, yt);
                if (k == doorColumn) { Wall(g, f, Slot.Wall, x0, x0 + .1f, yb, yt); Wall(g, f, Slot.Wall, x1 - .1f, x1, yb, yt); }
                else { dripOn = true; dripY0 = yb; dripY1 = yt; Wall(g, f, Slot.Wall, x0, x1, yb, yt); dripOn = false; }
                cursor = x1;
            }
            Wall(g, f, Slot.Wall, cursor, f.length, yb, yt);
        }

        /// front doors of the houses added since the list was cleared (game frame is not needed: Unity world, a point 0.9 m in
        /// front of the door at its threshold, and the facade's outward normal) — PropsBuilder puts flower pots beside them
        public static readonly List<(Vector3 p, Vector3 n)> Doors = new List<(Vector3, Vector3)>();

        /// Options of one AddHouse call (BuildingsFromTiles). lookSeed: the seed of the paint (an annex shares its main
        /// house's look); roof -1 auto, 0 flat, 1 hip, 2 gable; terrace: a flat roof with a railing all round (roof terrace
        /// of an annex); core: the house stands in an old town core (more bare stone).
        public struct Opts { public int lookSeed, roof; public bool terrace, core, noStairs; public OrthoRoofs.Rec ortho; public MapillaryFacades.Rec mly; }

        /// the roof tint (linear, Magistrala/Facade with COLOR.a = 1) that shows sRGB albedo `c`: the tiles' luminance x 2.03 has mean 0.40
        public static Color RoofTint(Color c) { var l = c.linear; return new Color(Mathf.Clamp01(l.r / .40f), Mathf.Clamp01(l.g / .40f), Mathf.Clamp01(l.b / .40f)); }
        /// the Balcony-slot tint (sRGB, made linear by Tint) on the concrete layer (mean 0.605 linear) that shows sRGB albedo `c`
        public static Color ConcreteTint(Color c) { var l = c.linear; return new Color(Mathf.Clamp01(l.r / .605f), Mathf.Clamp01(l.g / .605f), Mathf.Clamp01(l.b / .605f)).gamma; }
        // the flat roof's slab tint of the house being emitted (null: the old dark weathered concrete) and tiles on it
        static Color? flatSlab; static bool flatTiles;
        /// the flat roof slab being emitted is a hall's corrugated metal roof (the plaster array's PMetal layer on the concrete code)
        static bool metalSlab, industrialNow;
        /// counters of the last BuildingsFromTiles.Build (ortho roofs decided / panels)
        public static int nOrthoPitched, nOrthoFlat, nOrthoTiledFlat, nSolar, nSolarPanels;


        /// per facade (Unity world a -> b, on the ground): the height (absolute y) below which a neighbour covers it
        /// (-inf: free) and whether a neighbour stands against part of it (blind wall: no openings). Null: all free.
        public static Func<Vector3, Vector3, (float hide, bool blind)> EdgeQuery;
        /// from a facade's midpoint (Unity world) along its outward normal: metres to the nearest carriageway edge and that
        /// road's half width (main streets get shop fronts). Null: no streets.
        public static Func<Vector3, Vector3, (float gap, float hw)> StreetQuery;
        /// distance (m, up to 20; 1e9 none) from a facade's midpoint along its outward normal to a carriageway: the facades "along a
        /// road" that get the pack's details (PaketDetalji). Null: no details.
        public static Func<Vector3, Vector3, float> RoadQuery;
        const float DETAIL_ROAD = 16;
        static float H01(Vector3 v, float k) { float x = Mathf.Sin(v.x * 12.9898f + v.z * 78.233f + v.y * 3.17f + k) * 43758.5453f; return x - Mathf.Floor(x); }
        /// far levels: the footprint pulled in and the top lowered by this many metres, so where the mid level and the far
        /// level overlap during the switch the nearer one always wins the depth test (no z-fighting)
        public static float Shrink;
        /// shop fronts on facades this close (m) to a carriageway of at least this half width
        const float SHOP_GAP = 4.5f, SHOP_HW = 3.0f;

        /// <param name="footprintGame">Game (x,z), metres; simple ring, either winding, no repeated closing point.</param>
        public static void AddHouse(HouseGeo g, List<Vector2> footprintGame, string kind,
            double bottomY, double baseY, double topY, int seed)
            => AddHouse(g, footprintGame, kind, bottomY, baseY, topY, seed, new Opts { lookSeed = seed, roof = -1 });

        public static void AddHouse(HouseGeo g, List<Vector2> footprintGame, string kind,
            double bottomY, double baseY, double topY, int seed, Opts o)
        {
            if (g == null) throw new ArgumentNullException(nameof(g));
            if (footprintGame == null) throw new ArgumentNullException(nameof(footprintGame));
            if (!Finite(bottomY) || !Finite(baseY) || !Finite(topY) || bottomY > baseY || baseY >= topY)
                throw new ArgumentException("Expected finite heights bottomY <= baseY < topY.");
            var points = CleanRing(footprintGame);
            if (points.Count < 3) return;
            double signedArea = SignedArea(points);
            if (Math.Abs(signedArea) < .01) return;
            // Triangulate before appending anything; malformed rings cannot leave half a house in a chunk.
            List<int> triangles = Ear(points, signedArea);
            int N = points.Count;
            // the neighbours and the streets, asked on the true footprint (before any Shrink)
            var hideBelow = new float[N]; var blindF = new bool[N]; var gap = new float[N]; var roadHw = new float[N]; var roadD = new float[N];
            for (int i = 0; i < N; i++)
            {
                Vector3 a = points[i], b = points[(i + 1) % N], al = (b - a).normalized;
                Vector3 ow = new Vector3(al.z, 0, -al.x) * (signedArea > 0 ? 1 : -1);
                (hideBelow[i], blindF[i]) = EdgeQuery != null ? EdgeQuery(a, b) : (float.NegativeInfinity, false);
                (gap[i], roadHw[i]) = StreetQuery != null ? StreetQuery((a + b) / 2, ow) : (1e9f, 0);
                roadD[i] = RoadQuery != null && Lod <= 1 && PaketDetalji.Ready ? RoadQuery((a + b) / 2, ow) : 1e9f;
            }
            if (Shrink > 0 && topY - Shrink > baseY + .5)
            {
                var inner = Inset(points, signedArea, Shrink);
                try { triangles = Ear(inner, SignedArea(inner)); points = inner; signedArea = SignedArea(inner); topY -= Shrink; }
                catch (ArgumentException) { }
            }
            var random = new System.Random(seed);
            bool industrial = string.Equals(kind, "industrial", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "warehouse", StringComparison.OrdinalIgnoreCase);
            bool apartment = string.Equals(kind, "apartment", StringComparison.OrdinalIgnoreCase);
            float bottom = (float)bottomY, ground = (float)baseY, top = (float)topY;
            look = Look.Pick(new System.Random(o.lookSeed * 31 + 7), kind, ground, top - ground, o.core); gY = ground; tY = top;
            // Paket2 (paket2-podrucja.json): the house's centre in one of the areas
            float pcx = 0, pcz = 0; foreach (var q in footprintGame) { pcx += q.x; pcz += q.y; } pcx /= footprintGame.Count; pcz /= footprintGame.Count;
            bool p2wall = Paket2.In(pcx, pcz, "zbuka"), p2open = Paket2.In(pcx, pcz, "vrata") && PaketDetalji.Ready2;
            MapillaryFacades.Apply(ref look, o.mly, industrial, Lod == 0); Look.Finish(ref look, o.lookSeed, o.core, p2wall);
            look.roofPack = Paket2.In(pcx, pcz, "krov");
            var orr = o.ortho; flatSlab = null; flatTiles = false; industrialNow = industrial;
            if (OrthoRoofs.Enabled)
            {
                // roof colours from the orthophoto (else a draw from the same palette); flat roofs: light membranes / greys
                var pr = new System.Random(o.lookSeed * 13 + 5);
                Color rcol = orr != null && orr.hasRoof ? orr.roof : OrthoRoofs.RoofPalette((float)pr.NextDouble(), (float)pr.NextDouble(), (float)pr.NextDouble());
                look.roof = RoofTint(rcol); look.roofFade = 1;
                double f0 = pr.NextDouble(), f1 = pr.NextDouble();
                flatSlab = ConcreteTint(orr != null && orr.hasFlat ? orr.flat : OrthoRoofs.FlatPalette(f0, f1));
            }
            var clutter = new System.Random(seed * 17 + 3); // the clutter's own draws (identical in every level)
            bool hasAc = !industrial && clutter.NextDouble() < .7, hasDish = !industrial && clutter.NextDouble() < .35;
            bool hasCable = !industrial && clutter.NextDouble() < .9;
            var shape = new System.Random(seed * 53 + 11); // shape draws (identical in every level)
            double rShop = shape.NextDouble(), rStair = shape.NextDouble(), rLong = shape.NextDouble(), rChim = shape.NextDouble(),
                rStairSide = shape.NextDouble(), rSign = shape.NextDouble();
            float plinth = Mathf.Min(ground + .5f, top);
            // Consider partial top storeys too; the opening-height test below rejects those too short for a window.
            int floors = Mathf.Max(1, Mathf.CeilToInt((top - ground) / FloorHeight));
            // a facade is drawn when not wholly behind a neighbour; it has openings when no neighbour stands against it
            var visible = new bool[N]; var open = new bool[N];
            for (int i = 0; i < N; i++) { visible[i] = hideBelow[i] < top - .05f; open[i] = visible[i] && !blindF[i] && hideBelow[i] <= plinth; }
            int longest = -1; float longestLength = 0;
            for (int pass = 0; pass < 2 && longest < 0; pass++)
                for (int i = 0; i < N; i++)
                {
                    if (pass == 0 && !open[i]) continue;
                    float length = Vector3.Distance(points[i], points[(i + 1) % N]);
                    if (length > longestLength) { longestLength = length; longest = i; }
                }
            // roof: most Kaštela houses carry tiles (hip or gable); flat roofs (terraces) on a third of the new houses, the
            // unfinished ones, blocks, halls and the large / irregular footprints
            var rect = MinRect(points); float fill = (float)(Math.Abs(signedArea) / Math.Max(1e-3, rect.area));
            bool canPitch = !industrial && !apartment && N <= 10 && (Math.Abs(signedArea) < 750 || fill > .85f && Math.Abs(signedArea) < 1600);
            int roof = o.roof >= 0 ? o.roof
                : !canPitch || look.unfinished ? 0
                : look.family == 2 && look.flatRoll < .32f && Math.Abs(signedArea) > 45 ? 0
                : fill > .9f && look.gableRoll < (look.family <= 1 ? .5f : .38f) ? 2 : 1;
            if (roof > 0 && !canPitch) roof = 0;
            // the orthophoto's roof where it is clear (tiles: pitched, or tiles on the slab of a footprint a hip roof cannot cover)
            if (o.roof < 0 && orr != null && orr.decide == 1)
            {
                if (canPitch) roof = fill > .9f && look.gableRoll < (look.family <= 1 ? .5f : .38f) ? 2 : 1; else { roof = 0; flatTiles = true; }
                if (Lod == 0) { if (flatTiles) nOrthoTiledFlat++; else nOrthoPitched++; }
            }
            else if (o.roof < 0 && orr != null && orr.decide == 2) { roof = 0; if (Lod == 0) nOrthoFlat++; }
            // shop fronts: the ground floor of the facades along a main street (blocks always, houses often)
            bool shopHouse = !industrial && floors >= 2 && (apartment || rShop < (o.core ? .7 : .5));
            var shopF = new bool[N];
            for (int i = 0; i < N; i++) shopF[i] = shopHouse && open[i] && gap[i] < SHOP_GAP && roadHw[i] >= SHOP_HW && Vector3.Distance(points[i], points[(i + 1) % N]) >= 4;
            // an outside stair to the first floor along a side facade (old and new family houses)
            int stairFacade = -1;
            if (!o.noStairs && !industrial && !apartment && floors >= 2 && look.family != 0 && rStair < .22)
            {
                var cand = new List<int>();
                for (int i = 0; i < N; i++)
                    if (i != longest && open[i] && !shopF[i] && gap[i] > 3 && Vector3.Distance(points[i], points[(i + 1) % N]) >= 7.5f) cand.Add(i);
                if (cand.Count > 0) stairFacade = cand[(int)(rStairSide * cand.Count) % cand.Count];
            }
            bool hip = roof > 0;
            // the pack's window frames on 60 % of the houses along a road (the rest keep ours: PVC / timber vary)
            bool framesHere = H01(points[0], 51) < .6f;
            int dishFacade = hasDish ? clutter.Next(N) : -1;
            float dishAt = (float)clutter.NextDouble();

            for (int i = 0; i < N; i++)
            {
                Vector3 a = points[i], b = points[(i + 1) % N];
                Vector3 along = (b - a).normalized;
                // Polygon winding, rather than a centroid test, also works for concave corners.
                Vector3 outward = new Vector3(along.z, 0, -along.x) * (signedArea > 0 ? 1 : -1);
                var f = new Facade { origin = a, along = along, outward = outward,
                    length = Vector3.Distance(a, b), baseY = ground };
                if (!visible[i]) continue; // party wall wholly behind the neighbour
                if (!open[i])
                {
                    // against a neighbour: blank wall, only above its roof when it covers the lower part
                    float from = Mathf.Max(bottom, hideBelow[i]);
                    if (from < plinth) { Wall(g, f, Slot.Balcony, 0, f.length, from, plinth); from = plinth; }
                    Wall(g, f, Slot.Wall, 0, f.length, from, top);
                    continue;
                }
                bool shopHere = shopF[i];
                // along a road (pack details: frames, balconies, downpipes; awnings in ShopFront)
                bool road = roadD[i] < DETAIL_ROAD && !industrial; detailRoad = road;
                if (!(shopHere && Lod == 0)) Wall(g, f, Slot.Balcony, 0, f.length, bottom, plinth); // concrete plinth
                if (Lod > 0) Wall(g, f, Slot.Wall, 0, f.length, plinth, top);
                int columns = f.length > 3 ? Mathf.Max(1, Mathf.FloorToInt(f.length / (industrial ? 7f : 3.2f))) : 0;
                if (industrial) columns = Mathf.Min(columns, 3);
                float lastY = plinth; int firstFloor = 0;
                if (shopHere && floors >= 2)
                {
                    ShopFront(g, f, bottom, plinth, ground + FloorHeight, seed * 7 + i, rSign);
                    // konoba / café fronts in the old cores: a timber pergola with a dark red canvas over the pavement (owner photo 43)
                    if (o.core && gap[i] > 2.4f && rSign > .45 && Lod < 2) Pergola(g, f, Mathf.Min(2.6f, gap[i] - .6f), rSign);
                    lastY = ground + FloorHeight; firstFloor = 1;
                }
                // the outside stair: a solid stair rising along the facade to a landing in front of column 0 of floor 1
                bool stairHere = i == stairFacade && columns >= 2;
                float stairX0 = 0, stairX1 = 0;
                if (stairHere)
                {
                    float sp = f.length / columns, c0 = sp * .5f;
                    stairX0 = Mathf.Max(.2f, c0 - .75f); stairX1 = c0 + .75f + 4.3f;
                    if (stairX1 > f.length - .3f) stairHere = false;
                    else if (Lod < 2) OutsideStair(g, f, ground, ground + FloorHeight, stairX0, c0 + .75f, stairX1);
                }
                for (int floor = firstFloor; floor < floors && columns > 0; floor++)
                {
                    // Warehouses have one sparse row, regardless of their tall interior volume.
                    if (industrial && floor > 0) break;
                    bool shop = apartment && floor == 0;
                    float y0 = ground + floor * FloorHeight + (shop ? .6f : industrial ? 1.1f : .95f);
                    float y1 = Mathf.Min(y0 + (shop ? 2.05f : industrial ? 1.65f : 1.35f), top - .25f);
                    y0 = Mathf.Max(y0, plinth);
                    if (y1 - y0 < .65f) continue;
                    float spacing = f.length / columns;
                    float width = Mathf.Min(shop ? 2.2f : industrial ? 2.6f : 1.1f, spacing - 1.1f);
                    float cursor = 0;
                    bool balcony = !industrial && floors >= 2 && i == longest && floor >= 1
                        && f.length >= 4 && random.NextDouble() < .5;
                    bool stairDoor = stairHere && floor == 1;
                    int doorCol = balcony ? columns / 2 : stairDoor ? 0 : -1;
                    float floorY = ground + floor * FloorHeight;
                    if (doorCol >= 0 && Lod == 0)
                    {
                        float doorCentre = (doorCol + .5f) * spacing;
                        Wall(g, f, Slot.Wall, 0, doorCentre - width / 2, lastY, y0);
                        Wall(g, f, Slot.Wall, doorCentre + width / 2, f.length, lastY, y0);
                        Wall(g, f, Slot.Wall, doorCentre - width / 2, doorCentre + width / 2, lastY, floorY + .08f);
                    }
                    // the front door: ground floor of the longest facade, a middle column (not on blocks' shop floors)
                    int doorColumn = floor == 0 && i == longest && !industrial && !shop && columns >= 2 && f.length >= 5 ? (columns - 1) / 2 : -1;
                    if (doorColumn >= 0 && stairHere) { float dc = (doorColumn + .5f) * spacing; if (dc > stairX0 - .8f && dc < stairX1 + .8f) doorColumn = -1; }
                    if (doorCol < 0 && Lod == 0) RowBelow(g, f, columns, spacing, width, lastY, y0, doorColumn);
                    for (int k = 0; k < columns; k++)
                    {
                        float centre = (k + .5f) * spacing, x0 = centre - width / 2, x1 = centre + width / 2;
                        if (Lod == 0) Wall(g, f, Slot.Wall, cursor, x0, y0, y1);
                        // A balcony door drops to the slab; cut the lower wall only in this column.
                        bool door = k == doorCol;
                        bool front = k == doorColumn;
                        float openingBottom = door ? floorY + .08f : front ? lastY : y0;
                        // the window's state (same draws in every level): 0 open leaves, 1 closed, 2 one leaf closed;
                        // rollers: the blind's drop; the glass: room / lamp / curtains
                        double st = random.NextDouble(), rr = random.NextDouble();
                        bool leaves = look.leaves && !shop;
                        int state = !leaves ? 0 : look.family <= 1 ? (st < .3 && !door ? 1 : st < .48 ? 2 : 0) : (st < .45 && !door ? 1 : st < .6 ? 2 : 0);
                        if (front) { state = 1; leaves = false; } // a door leaf (louvred, the shutters' colour) instead of glass
                        float blind = look.rollers && !shop ? (rr < .35 ? 0 : rr < .75 ? .2f + .5f * (float)(rr - .35) / .4f : .97f) : 0;
                        glassData = new Vector4((float)random.NextDouble(), blind, 0, 0);
                        var cell = new Rect(k * spacing, floorY, spacing, FloorHeight);
                        // ground-floor windows behind the outside stair are left out (the stair stands in front)
                        bool behindStair = stairHere && floor == 0 && x1 > stairX0 - .1f && x0 < stairX1 + .1f;
                        if (behindStair) { if (Lod == 0) Wall(g, f, Slot.Wall, x0, x1, openingBottom, y1); }
                        else if (Lod == 0)
                        {
                            Window(g, f, x0, x1, openingBottom, y1, leaves, state, cell, look.rollers && !shop && !front, !door && !front);
                            if ((road && framesHere || p2open && look.family != 4) && !front && !shop) PaketDetalji.Frame(g, f.origin, f.along, f.outward, x0, x1, openingBottom, y1, look.family <= 1, ground);
                            if (front && p2open) PaketDetalji.Door(g, f.origin, f.along, f.outward, x0, x1, openingBottom, y1, H01(f.P(centre, lastY), 57), look.shutter, look.family <= 1, ground);
                        }
                        else FlatWindow(g, f, x0, x1, openingBottom, y1, leaves, state, cell, !door && !front);
                        if (front && Lod == 0) { Doors.Add((f.P(centre, lastY, .9f), f.outward)); { detailTint = new Color(.5f, .48f, .45f); { againstWall = true; Box(g, Slot.Detail, f.P(x0 - .25f, lastY - .5f, 0), f.along, f.outward, x1 - x0 + .5f, .5f, .45f, false); againstWall = false; } } } // a step
                        if (door && balcony)
                        {
                            float balconyWidth = Mathf.Min(2f + (float)random.NextDouble() * 2f,
                                2f * Mathf.Min(centre - .3f, f.length - centre - .3f));
                            bool laundry = clutter.NextDouble() < .3; int pots = clutter.Next(4);
                            // newer houses: often one long balcony along the whole facade
                            bool longB = look.family >= 2 && rLong < .4 && f.length >= 6;
                            // along a road: half of them the pack's balcony (slab + railing), the rest ours
                            float pr = H01(f.P(centre, floorY), 41);
                            if (Lod < 2 && road && pr < .55f && (longB ? f.length - .8f : balconyWidth) <= 4.2f) PaketDetalji.Balcony(g, f.origin, f.along, f.outward, longB ? f.length / 2 : centre, floorY, longB ? f.length - .8f : balconyWidth, 1.25f, false, ground);
                            else if (Lod < 2) Balcony(g, f, longB ? f.length / 2 : centre, floorY, longB ? f.length - .8f : balconyWidth, laundry, pots, clutter.Next());
                            // rust runs from the slab's ends where they meet a pier (not over a window of the floor below)
                            if (Lod == 0)
                            {
                                float bw = longB ? f.length - .8f : balconyWidth, bc = longB ? f.length / 2 : centre;
                                foreach (float ex in new[] { bc - bw / 2 + .12f, bc + bw / 2 - .12f })
                                {
                                    bool pier = ex > .3f && ex < f.length - .3f;
                                    for (int kk = 0; kk < columns && pier; kk++) { float cc = (kk + .5f) * spacing; if (Mathf.Abs(ex - cc) < width / 2 + .35f) pier = false; }
                                    if (pier) Streak(g, f, ex, floorY - .16f, .4f, .9f);
                                }
                            }
                        }
                        // an AC unit between this window and the next (70 % of houses, ~1-3 each)
                        bool ac = hasAc && k < columns - 1 && clutter.NextDouble() < .22 && !(doorCol >= 0 && (k == doorCol || k + 1 == doorCol)) && !behindStair;
                        if (ac && Lod == 0) AcUnit(g, f, (k + 1) * spacing, y0 + .25f);
                        cursor = x1;
                    }
                    if (Lod == 0) Wall(g, f, Slot.Wall, cursor, f.length, y0, y1);
                    lastY = y1;
                }
                if (Lod == 0) Wall(g, f, Slot.Wall, 0, f.length, lastY, top);
                if (Lod == 0 && i == longest && !industrial) MeterBox(g, f, Mathf.Min(.7f, f.length * .2f));
                if (Lod == 0 && i == longest && hasCable) Cable(g, f, top - .4f);
                if (Lod == 0 && i == longest && hip && road) PaketDetalji.Pipe(g, f.origin, f.along, f.outward, .15f, ground, top - .1f); // the pack's downpipe + outlet
                else if (Lod == 0 && i == longest && hip) { detailTint = new Color(.47f, .48f, .49f); { againstWall = true; Box(g, Slot.Detail, f.P(.12f, ground, 0), f.along, f.outward, .08f, top - .12f - ground, .08f, false); againstWall = false; } } // downpipe
                else if (Lod == 0 && road && f.length > 4 && top - ground > 3 && H01(f.P(f.length, top), 43) < .45f) PaketDetalji.Pipe(g, f.origin, f.along, f.outward, f.length - .15f, ground, top - .1f);
                // "vječna gradnja" (rare: ~1 in 70 unfinished houses along a road): scaffolding on the longest facade and a site fence
                if (Lod == 0 && road && (look.unfinished || look.bare != 0) && i == longest && f.length > 4 && top - ground > 4 && ++PaketDetalji.nSiteCand > 0 && H01(points[0], 53) < .05f)
                    PaketDetalji.Site(g, f.origin, f.along, f.outward, .3f, f.length - .3f, ground, top, Mathf.Min(3.5f, roadD[i] - .8f));
                if (Lod == 0 && i == dishFacade && f.length > 2.5f && top - ground > 4) Dish(g, f.P(.8f + dishAt * (f.length - 1.6f), top - 1.0f, .02f), f.outward, f.along);
                // the unfinished storey: concrete columns at the corners and ring beams, between them bare brick
                if (look.unfinished && Lod < 2 && f.length > 1.2f)
                {
                    float sy = ground + look.split;
                    { againstWall = true; Box(g, Slot.Balcony, f.P(0, sy - .3f, 0), f.along, f.outward, .32f, top - sy + .3f, .04f, false); againstWall = false; }
                    { againstWall = true; Box(g, Slot.Balcony, f.P(f.length - .32f, sy - .3f, 0), f.along, f.outward, .32f, top - sy + .3f, .04f, false); againstWall = false; }
                    { againstWall = true; Box(g, Slot.Balcony, f.P(0, sy - .3f, 0), f.along, f.outward, f.length, .3f, .045f, false); againstWall = false; }
                    if (f.length > 7) { againstWall = true; Box(g, Slot.Balcony, f.P(f.length / 2 - .16f, sy, 0), f.along, f.outward, .32f, top - sy, .04f, false); againstWall = false; }
                }
            }

            // (no foundation: the underside is never seen)
            bool chim = clutter.NextDouble() < .85, chim2 = clutter.NextDouble() < .3, antenna = clutter.NextDouble() < .45;
            double ra = clutter.NextDouble(), rb = clutter.NextDouble(), rc = clutter.NextDouble(), rd = clutter.NextDouble();
            // solar panels where the orthophoto shows them (the measured spot: game metres from the centroid)
            solarN = 0;
            if (orr != null && orr.solar >= 2f && Lod <= 2 && !industrial)
            {
                solarN = Mathf.Clamp(Mathf.RoundToInt(orr.solar / 1.65f), 1, 24); solarAt = new Vector3(orr.sdx, 0, -orr.sdz);
                if (Lod == 0) { nSolar++; nSolarPanels += solarN; }
            }
            if (hip)
                HipRoof(g, points, top, chim, chim2, antenna, (float)ra, roof == 2, rect);
            else
            {
                hiddenEdge = visible; for (int i = 0; i < N; i++) visible[i] = hideBelow[i] < top + .3f;
                FlatRoof(g, points, triangles, top, signedArea);
                hiddenEdge = null;
                if (solarN > 0) SolarFlat(g, points, top);
                if (!industrial && Lod < 2 && N <= 24)
                    RoofClutter(g, points, triangles, top, signedArea, o.terrace || rb < .5, !o.terrace && rc < .35, !o.terrace && rd < .3, ra < .3, look.unfinished, !o.terrace && !apartment && rChim < .6);
            }
        }

        /// ground floor of a facade on a main street: wide shop windows / doors in deep reveals, roller-shutter boxes, some
        /// shutters down, a plain fascia band above (no names)
        static void ShopFront(HouseGeo g, Facade f, float bottom, float plinth, float yTop, int seed, double rSign)
        {
            var r = new System.Random(seed);
            int n = Mathf.Max(1, Mathf.FloorToInt(f.length / 3.9f)); float sp = f.length / n, w = Mathf.Min(2.9f, sp - .8f);
            float oy0 = gY + .08f, oy1 = gY + 2.6f, recess = -.22f;
            if (w < 1.4f) return;
            float cursor = 0;
            var shutterCol = look.shutter;
            for (int k = 0; k < n; k++)
            {
                float c = (k + .5f) * sp, x0 = c - w / 2, x1 = c + w / 2;
                bool down = r.NextDouble() < .3; glassData = new Vector4((float)r.NextDouble(), 0, 0, 0);
                var cell = new Rect(k * sp, gY, sp, FloorHeight);
                look.shutter = new Color(.62f, .62f, .6f); // aluminium roller shutters
                if (Lod == 0)
                {
                    Wall(g, f, Slot.Balcony, cursor, x0, bottom, plinth);
                    Wall(g, f, Slot.Wall, cursor, x0, plinth, yTop);
                    Wall(g, f, Slot.Wall, x0, x1, oy1, yTop);
                    Wall(g, f, Slot.Balcony, x0, x1, bottom, oy0);
                    Vector3 a = f.P(x0, oy0), b = f.P(x1, oy0), cc = f.P(x1, oy1), d = f.P(x0, oy1);
                    Vector3 ar = f.P(x0, oy0, recess), br = f.P(x1, oy0, recess), cr = f.P(x1, oy1, recess), dr = f.P(x0, oy1, recess);
                    if (down) Shutter(g, f, x0, x1, oy0, oy1, recess + .03f, recess + .03f); else Glass(g, f, x0, x1, oy0, oy1, recess, cell);
                    revealOn = true; revealO = a; revealN = f.outward; revealD = -recess;
                    Face(g, Slot.Wall, a, ar, dr, d, f.along); Face(g, Slot.Wall, br, b, cc, cr, -f.along);
                    Face(g, Slot.Balcony, a, b, br, ar, Vector3.up); Face(g, Slot.Wall, dr, cr, cc, d, Vector3.down);
                    revealOn = false;
                    // the frame mullion of a shop door / window pair
                    if (!down) { detailTint = new Color(.2f, .21f, .22f); Box(g, Slot.Detail, f.P(c - .03f, oy0, recess), f.along, f.outward, .06f, oy1 - oy0, .05f, false); }
                    // the pack's awning over some open shop windows (muted canvas)
                    float ha = H01(f.P(c, oy1), 47);
                    if (!down && detailRoad && ha < .5f)
                    {
                        Color[] canvas = { new Color(.42f, .5f, .44f), new Color(.62f, .32f, .28f), new Color(.86f, .82f, .72f), new Color(.45f, .5f, .58f), new Color(.7f, .62f, .45f) };
                        PaketDetalji.Awning(g, f.origin, f.along, f.outward, x0, x1, oy1 + .3f, 1.2f, canvas[(int)(ha * 10) % canvas.Length], gY);
                    }
                }
                else if (Lod < 3)
                {
                    if (down) Shutter(g, f, x0, x1, oy0, oy1, .03f, .03f); else Glass(g, f, x0, x1, oy0, oy1, .03f, cell);
                }
                look.shutter = shutterCol;
                if (Lod < 2) { againstWall = true; Box(g, Slot.Trim, f.P(x0 - .1f, oy1, -.02f), f.along, f.outward, w + .2f, .26f, .14f); againstWall = false; } // roller box
                cursor = x1;
            }
            if (Lod == 0) { Wall(g, f, Slot.Balcony, cursor, f.length, bottom, plinth); Wall(g, f, Slot.Wall, cursor, f.length, plinth, yTop); }
            if (Lod < 2 && rSign < .75)
            {
                // fascia: a muted painted band over the shop windows
                Color[] fascia = { new Color(.22f, .3f, .25f), new Color(.38f, .16f, .14f), new Color(.17f, .22f, .32f), new Color(.36f, .36f, .35f), new Color(.62f, .58f, .5f), new Color(.45f, .3f, .18f) };
                detailTint = fascia[(int)(rSign * 8) % fascia.Length];
                { againstWall = true; Box(g, Slot.Detail, f.P(.4f, oy1 + .3f, 0), f.along, f.outward, f.length - .8f, .42f, .07f, false); againstWall = false; }
            }
        }

        static void Pergola(HouseGeo g, Facade f, float depth, double rnd)
        {
            float x0 = .3f, x1 = f.length - .3f, h = gY + 2.75f;
            detailTint = new Color(.3f, .2f, .13f); // stained timber
            int posts = Mathf.Max(2, Mathf.RoundToInt((x1 - x0) / 3f) + 1);
            for (int k = 0; k < posts; k++) Box(g, Slot.Detail, f.P(x0 + (x1 - x0 - .12f) * k / (posts - 1), gY - .05f, depth - .14f), f.along, f.outward, .12f, h - gY + .05f, .12f, false);
            Box(g, Slot.Detail, f.P(x0, h, depth - .16f), f.along, f.outward, x1 - x0, .16f, .16f);
            detailTint = rnd < .7 ? new Color(.42f, .13f, .1f) : new Color(.55f, .5f, .42f); // canvas
            Vector3 a = f.P(x0 - .1f, h + .35f, 0), b = f.P(x1 + .1f, h + .35f, 0), c = f.P(x1 + .1f, h + .16f, depth + .1f), d = f.P(x0 - .1f, h + .16f, depth + .1f);
            Face(g, Slot.Detail, a, b, c, d, Vector3.up); Face(g, Slot.Detail, d, c, b, a, Vector3.down);
            Face(g, Slot.Detail, d, c, c + Vector3.down * .22f, d + Vector3.down * .22f, f.outward); // valance
        }

        /// a solid (masonry / concrete) stair along the facade from the ground up to a landing at `land` (x0 .. xl), descending
        /// to x1; railing on its open side
        static void OutsideStair(HouseGeo g, Facade f, float ground, float land, float x0, float xl, float x1)
        {
            Slot s = look.family <= 1 ? Slot.Trim : Slot.Balcony;
            const float depth = 1.05f;
            int n = Mathf.Max(6, Mathf.RoundToInt((land - ground) / .19f)); float rise = (land - ground) / n, tread = (x1 - xl) / n;
            { againstWall = true; Box(g, s, f.P(x0, ground - .3f, 0), f.along, f.outward, xl - x0, land - ground + .3f, depth, false); againstWall = false; }
            for (int k = 0; k < n - 1; k++)
            {
                float xs = xl + (n - 1 - k) * tread; // step k (k = 0 the lowest) from xs to xs + tread
                { againstWall = true; Box(g, s, f.P(xs, ground - .3f, 0), f.along, f.outward, tread + (k == 0 ? 0 : .01f), (k + 1) * rise + .3f, depth, false); againstWall = false; }
            }
            // railing: along the open side of the flight (sloped) and round the landing
            Vector3 h = Vector3.up * .84f;
            Vector3 pa = f.P(x1, ground + rise, depth), pb = f.P(xl, land, depth);
            float L = Vector3.Distance(pa, pb);
            Quad(g, Slot.Rail, pa, pb, pb + h, pa + h, new Vector2(0, 0), new Vector2(L / 3, 0), new Vector2(L / 3, BarV), new Vector2(0, BarV), f.outward);
            Quad(g, Slot.Rail, pa, pb, pb + h, pa + h, new Vector2(0, 0), new Vector2(L / 3, 0), new Vector2(L / 3, BarV), new Vector2(0, BarV), -f.outward);
            RailPanel(g, f.P(xl, land, depth), f.P(x0, land, depth), f.outward);
            RailPanel(g, f.P(x0, land, depth), f.P(x0, land, 0), -f.along);
        }

        /// minimum-area enclosing rectangle (one candidate per footprint edge): origin, axes, extents, area
        struct MRect { public Vector3 origin, u, v; public float minU, maxU, minV, maxV; public double area; }
        static MRect MinRect(List<Vector3> points)
        {
            var m = new MRect { origin = points[0], u = Vector3.right, v = Vector3.forward, area = double.MaxValue };
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 x = (points[(i + 1) % points.Count] - points[i]).normalized;
                Vector3 z = new Vector3(-x.z, 0, x.x);
                float loX = float.MaxValue, hiX = float.MinValue, loZ = float.MaxValue, hiZ = float.MinValue;
                foreach (Vector3 p in points)
                {
                    Vector3 d = p - m.origin;
                    float px = Vector3.Dot(d, x), pz = Vector3.Dot(d, z);
                    loX = Mathf.Min(loX, px); hiX = Mathf.Max(hiX, px);
                    loZ = Mathf.Min(loZ, pz); hiZ = Mathf.Max(hiZ, pz);
                }
                double area = (double)(hiX - loX) * (hiZ - loZ);
                if (area >= m.area) continue;
                m.area = area; m.u = x; m.v = z; m.minU = loX; m.maxU = hiX; m.minV = loZ; m.maxV = hiZ;
            }
            return m;
        }

        static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x) && Math.Abs(x) < float.MaxValue;

        // the glass: UV0 = place in the room cell (one column x one storey), UV1.zw = 0..1 across the glass
        static void Glass(HouseGeo g, Facade f, float x0, float x1, float y0, float y1, float depth, Rect cell)
        {
            Vector2 R(float x, float y) => new Vector2((x - cell.x) / cell.width, (y - cell.y) / cell.height);
            Vector3 a = f.P(x0, y0, depth), b = f.P(x1, y0, depth), c = f.P(x1, y1, depth), d = f.P(x0, y1, depth);
            var gd = glassData; int i0 = g.v.Count;
            Quad(g, Slot.Glass, a, b, c, d, R(x0, y0), R(x1, y0), R(x1, y1), R(x0, y1), f.outward);
            // per-vertex glass coordinates (Tri may swap the winding: match by position)
            for (int i = i0; i < g.v.Count; i++)
            {
                Vector3 p = g.v[i] - a; float gx = Vector3.Dot(p, f.along) / Mathf.Max(.01f, x1 - x0), gy = p.y / Mathf.Max(.01f, y1 - y0);
                g.d[i] = new Vector4(gd.x, gd.y, Mathf.Round(gx), Mathf.Round(gy));
            }
        }

        static void Window(HouseGeo g, Facade f, float x0, float x1, float y0, float y1, bool leaves, int state, Rect cell, bool roller, bool sill)
        {
            // stone walls are thick (old houses ~50 cm, the frame sits in the middle), new plastered houses ~20 cm
            float recess = look.family <= 1 ? -.26f : -.18f;
            Vector3 a = f.P(x0, y0), b = f.P(x1, y0), c = f.P(x1, y1), d = f.P(x0, y1);
            Vector3 ar = f.P(x0, y0, recess), br = f.P(x1, y0, recess), cr = f.P(x1, y1, recess), dr = f.P(x0, y1, recess);
            if (state == 1) Shutter(g, f, x0, x1, y0, y1, recess + .02f, recess + .02f); // closed leaves in the opening
            else Glass(g, f, x0, x1, y0, y1, recess, cell);
            if (state == 2) Shutter(g, f, (x0 + x1) / 2, x1, y0, y1, recess + .03f, recess + .03f);
            revealOn = true; revealO = a; revealN = f.outward; revealD = -recess;
            Face(g, Slot.Wall, a, ar, dr, d, f.along);
            Face(g, Slot.Wall, br, b, c, cr, -f.along);
            Face(g, Slot.Wall, a, b, br, ar, Vector3.up);
            Face(g, Slot.Wall, dr, cr, c, d, Vector3.down);
            revealOn = false;
            Frame(g, f, x0, x1, y0, y1, sill);
            if (sill)
            {
                // Slightly below the bottom reveal, avoiding coplanar plaster/stone surfaces.
                Vector3 s0 = f.P(x0 - .08f, y0 - .085f, .05f), s1 = f.P(x1 + .08f, y0 - .085f, .05f);
                Vector3 s2 = s1 + Vector3.up * .07f, s3 = s0 + Vector3.up * .07f;
                Face(g, Slot.Trim, s0, s1, s2, s3, f.outward);
                Face(g, Slot.Trim, s3, s2, f.P(x1 + .08f, y0 - .015f, -.12f), f.P(x0 - .08f, y0 - .015f, -.12f), Vector3.up);
                Face(g, Slot.Trim, s0, s3, f.P(x0 - .08f, y0 - .015f, -.12f), f.P(x0 - .08f, y0 - .085f, -.12f), -f.along);
                Face(g, Slot.Trim, s1, f.P(x1 + .08f, y0 - .085f, -.12f), f.P(x1 + .08f, y0 - .015f, -.12f), s2, f.along);
            }
            if (roller) { againstWall = true; Box(g, Slot.Trim, f.P(x0 - .12f, y1 + .12f, -.02f), f.along, f.outward, x1 - x0 + .24f, .24f, .1f); againstWall = false; }
            if (!leaves) return;
            // Open leaves lie beside the reveal, slightly angled off the plaster (no coplanar overlay).
            if (state != 1) Shutter(g, f, x0 - .49f, x0 - .04f, y0, y1, .065f, .035f);
            if (state == 0) Shutter(g, f, x1 + .04f, x1 + .49f, y0, y1, .035f, .065f);
        }

        // Lod 1 / 2: the opening as a glass quad 3 cm off the continuous wall (no reveals); Lod 1 adds sill, frame and leaves
        static void FlatWindow(HouseGeo g, Facade f, float x0, float x1, float y0, float y1, bool leaves, int state, Rect cell, bool sill)
        {
            if (Lod >= 3) return; // the box level: the wall takes the windows' average (Tint)
            if (state == 1) Shutter(g, f, x0, x1, y0, y1, .03f, .03f);
            else Glass(g, f, x0, x1, y0, y1, .03f, cell);
            if (Lod > 1) return;
            if (state == 2) Shutter(g, f, (x0 + x1) / 2, x1, y0, y1, .035f, .035f);
            Frame(g, f, x0, x1, y0, y1, sill);
            if (sill)
            {
                Vector3 s0 = f.P(x0 - .08f, y0 - .085f, .05f), s1 = f.P(x1 + .08f, y0 - .085f, .05f);
                Face(g, Slot.Trim, s0, s1, s1 + Vector3.up * .07f, s0 + Vector3.up * .07f, f.outward);
            }
            if (!leaves) return;
            if (state != 1) Shutter(g, f, x0 - .49f, x0 - .04f, y0, y1, .065f, .035f);
            if (state == 0) Shutter(g, f, x1 + .04f, x1 + .49f, y0, y1, .035f, .065f);
        }

        // light frame around the opening, 12 cm, 1.5 cm proud of the wall (stone on old houses)
        static void Frame(HouseGeo g, Facade f, float x0, float x1, float y0, float y1, bool bottom)
        {
            float w = look.family <= 1 ? .17f : .12f, z = look.family <= 1 ? .03f : .015f;
            Face(g, Slot.Trim, f.P(x0 - w, y1, z), f.P(x1 + w, y1, z), f.P(x1 + w, y1 + w, z), f.P(x0 - w, y1 + w, z), f.outward);
            Face(g, Slot.Trim, f.P(x0 - w, y0 - (bottom ? w : 0), z), f.P(x0, y0 - (bottom ? w : 0), z), f.P(x0, y1, z), f.P(x0 - w, y1, z), f.outward);
            Face(g, Slot.Trim, f.P(x1, y0 - (bottom ? w : 0), z), f.P(x1 + w, y0 - (bottom ? w : 0), z), f.P(x1 + w, y1, z), f.P(x1, y1, z), f.outward);
        }

        static void Shutter(HouseGeo g, Facade f, float x0, float x1, float y0, float y1, float d0, float d1)
        {
            Quad(g, Slot.Shutter, f.P(x0, y0, d0), f.P(x1, y0, d1), f.P(x1, y1, d1), f.P(x0, y1, d0),
                new Vector2(0, y0), new Vector2(x1 - x0, y0), new Vector2(x1 - x0, y1), new Vector2(0, y1), f.outward);
        }

        // Oriented closed box. x/z are horizontal unit axes; y is world up.
        /// wall-mounted boxes (their -z side against a wall: AC units, meters, steps, roller boxes...) leave that face out
        static bool againstWall;
        static Vector2 railUV = new Vector2(1f / 2048f, 1f / 2048f);
        static void Box(HouseGeo g, Slot slot, Vector3 origin, Vector3 x, Vector3 z, float width, float height, float depth, bool bottomFace = true)
        {
            int firstVertex = g.v.Count;
            Vector3 a = origin, b = a + x * width, c = b + z * depth, d = a + z * depth;
            Vector3 h = Vector3.up * height;
            if (bottomFace) Face(g, slot, a, b, c, d, Vector3.down);
            Face(g, slot, a + h, d + h, c + h, b + h, Vector3.up);
            if (!againstWall) Face(g, slot, a, a + h, b + h, b, -z);
            Face(g, slot, d, c, c + h, d + h, z);
            Face(g, slot, a, d, d + h, a + h, -x);
            Face(g, slot, b, b + h, c + h, c, x);
            // Bars sample one opaque texel of the railing atlas (a bar, or a fence post's patch); only the panels are perforated.
            if (slot == Slot.Rail)
                for (int i = firstVertex; i < g.uv.Count; i++) g.uv[i] = railUV;
        }
        // a box on an arbitrary frame (x, y, z unit axes): solar panels, dishes
        static void Box3(HouseGeo g, Slot slot, Vector3 o, Vector3 x, Vector3 y, Vector3 z, float w, float h, float d)
        {
            Vector3 X = x * w, Y = y * h, Z = z * d;
            Face(g, slot, o + Y, o + Y + Z, o + X + Y + Z, o + X + Y, y);
            Face(g, slot, o, o + X, o + X + Z, o + Z, -y);
            Face(g, slot, o, o + Y, o + X + Y, o + X, -z);
            Face(g, slot, o + Z, o + X + Z, o + X + Y + Z, o + Y + Z, z);
            Face(g, slot, o, o + Z, o + Y + Z, o + Y, -x);
            Face(g, slot, o + X, o + X + Y, o + X + Y + Z, o + X + Z, x);
        }

        static void Balcony(HouseGeo g, Facade f, float centre, float floorY, float width, bool laundry, int pots, int seed)
        {
            float left = centre - width / 2, right = centre + width / 2;
            Box(g, Slot.Balcony, f.P(left, floorY - .16f, -.08f), f.along, f.outward, width, .16f, 1.08f);
            // Three cutout panels approximate slender vertical bars. Both sides are visible.
            RailPanel(g, f.P(left, floorY + .12f, 1), f.P(right, floorY + .12f, 1), f.outward);
            RailPanel(g, f.P(left, floorY + .12f, 0), f.P(left, floorY + .12f, 1), -f.along);
            RailPanel(g, f.P(right, floorY + .12f, 1), f.P(right, floorY + .12f, 0), f.along);
            if (Lod > 0) return;
            Box(g, Slot.Rail, f.P(left - .025f, floorY + .96f, .975f), f.along, f.outward, width + .05f, .05f, .05f);
            Box(g, Slot.Rail, f.P(left - .025f, floorY + .96f, 0), f.along, f.outward, .05f, .05f, 1);
            Box(g, Slot.Rail, f.P(right - .025f, floorY + .96f, 0), f.along, f.outward, .05f, .05f, 1);
            var r = new System.Random(seed);
            // flower pots (terracotta) with a green crown along the railing
            for (int p = 0; p < pots; p++)
            {
                float x = left + .25f + (width - .6f) * (float)r.NextDouble();
                detailTint = new Color(.72f, .38f, .24f);
                Box(g, Slot.Detail, f.P(x, floorY, .72f), f.along, f.outward, .26f, .24f, .22f, false);
                detailTint = Color.Lerp(new Color(.25f, .42f, .2f), new Color(.45f, .55f, .25f), (float)r.NextDouble());
                if (r.NextDouble() < .3) detailTint = new Color(.75f, .25f, .45f); // bougainvillea / geranium
                Box(g, Slot.Detail, f.P(x - .04f, floorY + .24f, .68f), f.along, f.outward, .34f, .26f, .3f, false);
            }
            if (!laundry) return;
            // a line along the front, 5-7 pieces of washing hanging over the railing
            detailTint = new Color(.85f, .85f, .85f);
            Box(g, Slot.Detail, f.P(left + .05f, floorY + 1.55f, .9f), f.along, f.outward, width - .1f, .015f, .015f, false);
            int n = 5 + r.Next(3); float x0 = left + .15f;
            for (int k = 0; k < n && x0 < right - .3f; k++)
            {
                float w = .25f + .35f * (float)r.NextDouble(), h = .35f + .45f * (float)r.NextDouble();
                w = Mathf.Min(w, right - .1f - x0);
                detailTint = Color.HSVToRGB((float)r.NextDouble(), r.NextDouble() < .4 ? .05f : .5f, .55f + .4f * (float)r.NextDouble());
                Vector3 a = f.P(x0, floorY + 1.55f - h, .9f), b = f.P(x0 + w, floorY + 1.55f - h, .9f);
                Face(g, Slot.Detail, a, b, b + Vector3.up * h, a + Vector3.up * h, f.outward);
                Face(g, Slot.Detail, a, b, b + Vector3.up * h, a + Vector3.up * h, -f.outward);
                x0 += w + .05f + .1f * (float)r.NextDouble();
            }
        }

        static void RailPanel(HouseGeo g, Vector3 a, Vector3 b, Vector3 outward)
        {
            Vector3 h = Vector3.up * .84f;
            float L = Vector3.Distance(a, b);
            Quad(g, Slot.Rail, a, b, b + h, a + h, new Vector2(0, 0), new Vector2(L / 3, 0), new Vector2(L / 3, BarV), new Vector2(0, BarV), outward);
            Quad(g, Slot.Rail, a, b, b + h, a + h, new Vector2(0, 0), new Vector2(L / 3, 0), new Vector2(L / 3, BarV), new Vector2(0, BarV), -outward);
        }

        // ---- clutter (TIPOLOGIJA 6 / 9.7): AC units, meter box, cables, satellite dish, chimneys, antennas, roof terraces
        /// AcModel: the AC units are the Tripo model (klima.glb, 0.80 x 0.56 x 0.30 m, instanced by BuildingsFromTiles through a
        /// TreeRenderer) instead of boxes: AcSpots gets the unit's bottom-centre against the wall (Unity) and its outward normal
        public static bool AcModel;
        public static readonly List<(Vector3 p, Vector3 n)> AcSpots = new List<(Vector3, Vector3)>();
        /// rust / dirt run down the wall from (x, yTop): the wall's own quad again 1 cm off it (same layer, tint, UVs: it matches the
        /// wall), flagged for Magistrala/Facade's rust mask; UV2.x = across profile (0 at the sides, 1 in the middle), UV2.y = 1 at the top
        static bool rustOn;
        static void Streak(HouseGeo g, Facade f, float x, float yTop, float w, float len)
        {
            float y0 = Mathf.Max(gY + .3f, yTop - len); if (yTop - y0 < .2f || Lod > 0) return;
            int start = g.o.Count; rustOn = true;
            var f2 = f; f2.origin += f.outward * .01f;
            Wall(g, f2, Slot.Wall, x - w / 2, x, y0, yTop); Wall(g, f2, Slot.Wall, x, x + w / 2, y0, yTop);
            rustOn = false;
            var c = f2.P(x, 0);
            for (int i = start; i < g.o.Count; i++)
            {
                var p = g.v[i]; var o = g.o[i];
                float across = 1 - Mathf.Clamp01(Mathf.Abs(Vector3.Dot(p - c, f.along)) / (w / 2));
                g.o[i] = new Vector4(across, Mathf.Clamp01((p.y - y0) / (yTop - y0)), o.z, o.w);
            }
        }
        static void AcUnit(HouseGeo g, Facade f, float x, float y)
        {
            // condensate / rust run under the unit (most units have one)
            if (Lod == 0 && ((int)(x * 13.7f + y * 7.1f) & 3) != 0) Streak(g, f, x + .05f, y, .55f, 1.1f + .6f * Mathf.Repeat(x * 3.3f, 1));
            if (AcModel)
            {
                if (Lod == 0) AcSpots.Add((f.P(x, y, 0), f.outward));
                detailTint = new Color(.9f, .9f, .88f); // the pipe duct down the wall
                { againstWall = true; Box(g, Slot.Detail, f.P(x - .36f, y - .9f, 0), f.along, f.outward, .07f, .9f, .06f, false); againstWall = false; }
                return;
            }
            detailTint = new Color(.93f, .93f, .92f);
            { againstWall = true; Box(g, Slot.Detail, f.P(x - .4f, y, 0), f.along, f.outward, .8f, .55f, .28f); againstWall = false; }
            detailTint = new Color(.25f, .26f, .27f); // fan grille
            Vector3 c = f.P(x + .08f, y + .275f, .285f);
            Face(g, Slot.Detail, c - f.along * .2f - Vector3.up * .2f, c + f.along * .2f - Vector3.up * .2f, c + f.along * .2f + Vector3.up * .2f, c - f.along * .2f + Vector3.up * .2f, f.outward);
            detailTint = new Color(.9f, .9f, .88f); // the pipe duct down the wall
            { againstWall = true; Box(g, Slot.Detail, f.P(x - .36f, y - .9f, 0), f.along, f.outward, .07f, .9f, .06f, false); againstWall = false; }
        }
        static void MeterBox(HouseGeo g, Facade f, float x)
        {
            detailTint = new Color(.82f, .82f, .78f);
            { againstWall = true; Box(g, Slot.Detail, f.P(x, gY + .7f, 0), f.along, f.outward, .45f, .6f, .14f, false); againstWall = false; }
        }
        static void Cable(HouseGeo g, Facade f, float y)
        {
            detailTint = new Color(.1f, .1f, .1f);
            { againstWall = true; Box(g, Slot.Detail, f.P(.15f, y, .03f), f.along, f.outward, f.length - .3f, .025f, .025f, false); againstWall = false; }
            { againstWall = true; Box(g, Slot.Detail, f.P(.15f, gY + 1.3f, .03f), f.along, f.outward, .025f, y - gY - 1.3f, .025f, false); againstWall = false; }
        }
        static void Dish(HouseGeo g, Vector3 at, Vector3 outward, Vector3 along)
        {
            detailTint = new Color(.88f, .88f, .86f);
            Box(g, Slot.Detail, at - along * .03f, along, outward, .06f, .06f, .35f, false); // arm
            // dish: a shallow octagon facing out, up and to the side
            Vector3 n = (outward + Vector3.up * .45f + along * .35f).normalized;
            Vector3 c = at + outward * .42f + Vector3.up * .05f;
            Vector3 u = Vector3.Cross(Vector3.up, n).normalized, v = Vector3.Cross(n, u);
            Vector3 back = c - n * .08f;
            for (int i = 0; i < 8; i++)
            {
                float a0 = i * Mathf.PI / 4, a1 = (i + 1) * Mathf.PI / 4;
                Vector3 p0 = c + (u * Mathf.Cos(a0) + v * Mathf.Sin(a0)) * .34f, p1 = c + (u * Mathf.Cos(a1) + v * Mathf.Sin(a1)) * .34f;
                Tri(g, Slot.Detail, back, p0, p1, Vector2.zero, Vector2.right, Vector2.up, n);
                Tri(g, Slot.Detail, back, p0, p1, Vector2.zero, Vector2.right, Vector2.up, -n);
            }
        }

        // ---- solar panels (orthophoto): 1.0 x 1.65 m modules, dark blue glass, 4 cm thick (top face only in the far level)
        static int solarN; static Vector3 solarAt;
        static void Panel(HouseGeo g, Vector3 o, Vector3 x, Vector3 n, Vector3 d)
        {
            detailTint = new Color(.07f, .1f, .17f);
            if (Lod >= 2) { Face(g, Slot.Detail, o + n * .04f, o + n * .04f + x, o + n * .04f + x + d * 1.65f, o + n * .04f + d * 1.65f, n); return; }
            Box3(g, Slot.Detail, o, x, n, d, 1f, .04f, 1.65f);
        }
        /// on the roof slope facing most south (Unity -z), a grid round the measured spot, 8 cm above the tiles
        static void SolarSlope(HouseGeo g, Vector3 centre, Vector3 u, Vector3 v, float halfLength, float halfWidth, float ridgeHalf, float rise, bool gable)
        {
            float s = v.z > 0 ? -1 : 1; Vector3 outw = v * s;
            Vector3 eave = centre + outw * halfWidth, ridge = centre + Vector3.up * rise;
            Vector3 dUp = (ridge - eave).normalized; float L = Vector3.Distance(ridge, eave);
            Vector3 n = Vector3.Cross(dUp, u).normalized; if (n.y < 0) n = -n;
            float usable = 2 * (gable ? halfLength - .6f : ridgeHalf + halfWidth * .45f - .4f);
            int cols = Mathf.FloorToInt(usable / 1.05f), rows = Mathf.FloorToInt((L - .7f) / 1.7f);
            if (cols < 1 || rows < 1) return;
            int k = Mathf.Min(solarN, cols * rows); int cu = Mathf.Min(cols, k), ru = Mathf.CeilToInt(k / (float)cu);
            float gw = cu * 1.05f - .05f, gl = ru * 1.7f - .05f;
            float off = Mathf.Clamp(Vector3.Dot(solarAt, u), -(usable - gw) / 2, (usable - gw) / 2);
            float t0 = (L - gl) / 2;
            for (int i = 0; i < k; i++)
            {
                int r = i / cu, c = i % cu;
                Vector3 o = eave + u * (off - gw / 2 + c * 1.05f) + dUp * (t0 + r * 1.7f) + n * .08f;
                Panel(g, o, u, n, dUp);
            }
        }
        /// flat roofs: rows of modules tilted 25 deg to the south on low frames, round the measured spot, inside the parapet
        static void SolarFlat(HouseGeo g, List<Vector3> points, float top)
        {
            Vector3 c = Vector3.zero; foreach (var p in points) c += p; c /= points.Count; c += solarAt; c.y = top + .25f;
            Vector3 d = new Vector3(0, Mathf.Sin(25 * Mathf.Deg2Rad), Mathf.Cos(25 * Mathf.Deg2Rad)), n = new Vector3(0, d.z, -d.y);
            int cols = Mathf.Min(6, solarN), rows = Mathf.CeilToInt(solarN / (float)cols), placed = 0;
            for (int r = 0; r < rows && placed < solarN; r++)
                for (int k = 0; k < cols && placed < solarN; k++)
                {
                    Vector3 o = c + new Vector3((k - cols / 2f) * 1.05f, 0, (r - rows / 2f) * 2.3f);
                    bool inside = true;
                    foreach (var q in new[] { o, o + Vector3.right, o + d * 1.65f, o + Vector3.right + d * 1.65f })
                        if (!InRing(points, q.x, q.z, .35f)) { inside = false; break; }
                    if (!inside) continue;
                    Panel(g, o, Vector3.right, n, d); placed++;
                }
        }
        /// (x, z) inside the ring (Unity xz) and at least `margin` from its edges
        static bool InRing(List<Vector3> r, float x, float z, float margin)
        {
            bool hit = false;
            for (int i = 0, j = r.Count - 1; i < r.Count; j = i++)
            {
                if ((r[i].z > z) != (r[j].z > z) && x < (r[j].x - r[i].x) * (z - r[i].z) / (r[j].z - r[i].z) + r[i].x) hit = !hit;
                Vector3 a = r[j], b = r[i], e = b - a; float L2 = e.x * e.x + e.z * e.z;
                float t = L2 < 1e-6f ? 0 : Mathf.Clamp01(((x - a.x) * e.x + (z - a.z) * e.z) / L2);
                float qx = a.x + e.x * t - x, qz = a.z + e.z * t - z; if (qx * qx + qz * qz < margin * margin) return false;
            }
            return hit;
        }

        static void HipRoof(HouseGeo g, List<Vector3> points, float top, bool chimney, bool chimney2, bool antenna, float rnd, bool gable, MRect mr)
        {
            Vector3 origin = mr.origin, u = mr.u, v = mr.v;
            float minU = mr.minU, maxU = mr.maxU, minV = mr.minV, maxV = mr.maxV;
            Vector3 centre = origin + u * ((minU + maxU) / 2) + v * ((minV + maxV) / 2) + Vector3.up * top;
            float halfLength = (maxU - minU) / 2 + .5f, halfWidth = (maxV - minV) / 2 + .5f; // 50 cm eaves
            if (halfLength < halfWidth)
            {
                var axis = u; u = v; v = -axis;
                float size = halfLength; halfLength = halfWidth; halfWidth = size;
            }
            float rise = halfWidth * Mathf.Tan(22f * Mathf.Deg2Rad);
            float ridgeHalf = gable ? halfLength : Mathf.Max(0, halfLength - halfWidth);
            Vector3 a = centre - u * halfLength - v * halfWidth;
            Vector3 b = centre + u * halfLength - v * halfWidth;
            Vector3 c = centre + u * halfLength + v * halfWidth;
            Vector3 d0 = centre - u * halfLength + v * halfWidth;
            Vector3 r0 = centre - u * ridgeHalf + Vector3.up * rise;
            Vector3 r1 = centre + u * ridgeHalf + Vector3.up * rise;
            Face(g, Slot.Roof, a, b, r1, r0, Vector3.up - v);
            Face(g, Slot.Roof, c, d0, r0, r1, Vector3.up + v);
            Vector3 down = Vector3.down * .14f;
            if (!gable)
            {
                RoofTriangle(g, b, c, r1, Vector3.up + u);
                RoofTriangle(g, d0, a, r0, Vector3.up - u);
                Face(g, Slot.Trim, a + down, b + down, b, a, -v);
                Face(g, Slot.Trim, b + down, c + down, c, b, u);
                Face(g, Slot.Trim, c + down, d0 + down, d0, c, v);
                Face(g, Slot.Trim, d0 + down, a + down, a, d0, -u);
                // Close the projecting underside: no see-through eaves when driving below a house.
                if (Lod < 2) Face(g, Slot.Trim, a + down, b + down, c + down, d0 + down, Vector3.down);
            }
            else
            {
                // gable ends: the wall rises to the ridge in the facade plane (50 cm inside the roof's edge); the roof's
                // overhang shows its underside there, a barge board follows the slope
                float wl = halfLength - .5f, ww = halfWidth - .5f, eh = rise * .5f / halfWidth;
                Face(g, Slot.Trim, a + down, b + down, b, a, -v);
                Face(g, Slot.Trim, c + down, d0 + down, d0, c, v);
                for (int e = -1; e <= 1; e += 2)
                {
                    Vector3 cu = centre + u * (wl * e), cv = centre + u * (halfLength * e);
                    Vector3 p0 = cu - v * ww, p1 = cu + v * ww;
                    Emit(g, Slot.Wall, p0, p1, p1 + Vector3.up * eh, u * e); Emit(g, Slot.Wall, p0, p1 + Vector3.up * eh, p0 + Vector3.up * eh, u * e);
                    Emit(g, Slot.Wall, p0 + Vector3.up * eh, p1 + Vector3.up * eh, cu + Vector3.up * rise, u * e);
                    Vector3 e0 = cv - v * halfWidth, e1 = cv + v * halfWidth, er = cv + Vector3.up * rise;
                    Vector3 i0 = cu - v * halfWidth, i1 = cu + v * halfWidth, ir = cu + Vector3.up * rise;
                    if (Lod < 2)
                    {
                        Face(g, Slot.Trim, i0, e0, er, ir, -(Vector3.up - v)); // overhang undersides
                        Face(g, Slot.Trim, e1, i1, ir, er, -(Vector3.up + v));
                    }
                    Face(g, Slot.Trim, e0 + down, er + down, er, e0, u * e); // barge boards
                    Face(g, Slot.Trim, er + down, e1 + down, e1, er, u * e);
                }
                if (Lod < 2)
                {
                    Face(g, Slot.Trim, a + down, b + down, b + down + v * .5f, a + down + v * .5f, Vector3.down);
                    Face(g, Slot.Trim, d0 + down - v * .5f, c + down - v * .5f, c + down, d0 + down, Vector3.down);
                }
            }
            if (solarN > 0) SolarSlope(g, centre, u, v, halfLength, halfWidth, ridgeHalf, rise, gable);
            if (Lod > 1) return;
            // chimneys: plastered / stone stacks a little above the ridge, with a concrete cap
            if (chimney) Chimney(g, centre + u * (ridgeHalf * .5f + halfLength * .15f) * (rnd < .5 ? 1 : -1) + v * .7f, u, v, top, rise);
            if (chimney2) Chimney(g, centre - u * (ridgeHalf * .6f + .5f) * (rnd < .5 ? 1 : -1) - v * .6f, u, v, top, rise);
            if (Lod > 0) return;
            // gutters along both long eaves, a downpipe at one corner
            detailTint = new Color(.55f, .56f, .57f);
            Box(g, Slot.Detail, a - v * .1f + down * 1.1f, u, v, halfLength * 2, .1f, .12f);
            Box(g, Slot.Detail, d0 + down * 1.1f - v * .02f, u, v, halfLength * 2, .1f, .12f);
            if (antenna)
            {
                detailTint = new Color(.6f, .6f, .6f);
                Vector3 m = centre + u * ridgeHalf * (rnd - .5f) + Vector3.up * rise;
                Box(g, Slot.Detail, m, u, v, .04f, 2.2f, .04f, false);
                Box(g, Slot.Detail, m + Vector3.up * 1.6f - u * .5f, u, v, 1.0f, .03f, .03f, false);
                Box(g, Slot.Detail, m + Vector3.up * 2.0f - u * .35f, u, v, .7f, .03f, .03f, false);
                Box(g, Slot.Detail, m + Vector3.up * 1.8f - v * .3f, v, u, .6f, .03f, .03f, false);
            }
        }

        static void Chimney(HouseGeo g, Vector3 at, Vector3 u, Vector3 v, float top, float rise)
        {
            float h = rise + .7f;
            Box(g, Slot.Wall, new Vector3(at.x, top, at.z) - u * .28f - v * .28f, u, v, .56f, h, .56f, false);
            Box(g, Slot.Balcony, new Vector3(at.x, top + h, at.z) - u * .36f - v * .36f, u, v, .72f, .08f, .72f);
            Box(g, Slot.Balcony, new Vector3(at.x, top + h + .08f, at.z) - u * .16f - v * .16f, u, v, .32f, .22f, .32f, false);
        }

        static void RoofTriangle(HouseGeo g, Vector3 a, Vector3 b, Vector3 c, Vector3 want)
        {
            Vector3 u = (b - a).normalized;
            Vector3 v = Vector3.Cross(Vector3.Cross(b - a, c - a).normalized, u).normalized;
            Vector2 o = new Vector2(Vector3.Dot(a, u), Vector3.Dot(a, v));
            Tri(g, Slot.Roof, a, b, c, o, o + new Vector2(Vector3.Distance(a, b), 0),
                o + new Vector2(Vector3.Dot(c - a, u), Vector3.Dot(c - a, v)), want);
        }

        static bool[] hiddenEdge; // FlatRoof: per edge, false = against a neighbour reaching the parapet (no parapet there)
        static void FlatRoof(HouseGeo g, List<Vector3> points, List<int> triangles, float top, double area)
        {
            // the roof slab: dark, weathered concrete / bitumen (not the light balcony concrete), the parapet a light coping
            var keep = look.balcony; float keepDirt = look.dirt;
            look.balcony = flatSlab ?? new Color(.55f, .54f, .51f); look.dirt = Mathf.Max(.7f, look.dirt);
            // halls / warehouses: corrugated metal (corrugated_iron_02), still in the orthophoto's roof colour
            metalSlab = industrialNow && !flatTiles; if (metalSlab) look.dirt = keepDirt * .6f;
            Vector3? ridge = null;
            if (metalSlab)
            {
                // the longest footprint edge: the hall's ridge direction (the sheets run from it to the eaves)
                float best = 0;
                for (int i = 0; i < points.Count; i++) { var e = points[(i + 1) % points.Count] - points[i]; e.y = 0; if (e.magnitude > best) { best = e.magnitude; ridge = e.normalized; } }
            }
            Surface(g, points, triangles, top, flatTiles ? Slot.Roof : Slot.Balcony, Vector3.up, ridge);
            metalSlab = false;
            look.balcony = keep; look.dirt = keepDirt;
            var inner = Inset(points, area, .16f);
            Vector3 low = Vector3.up * top, high = Vector3.up * (top + .4f);
            for (int i = 0; i < points.Count; i++)
            {
                int j = (i + 1) % points.Count;
                Vector3 e = (points[j] - points[i]).normalized;
                Vector3 n = new Vector3(e.z, 0, -e.x) * (area > 0 ? 1 : -1);
                if (hiddenEdge != null && !hiddenEdge[i]) continue;
                Face(g, Slot.Wall, points[i] + low, points[j] + low, points[j] + high, points[i] + high, n);
                Face(g, Slot.Wall, inner[i] + low, inner[j] + low, inner[j] + high, inner[i] + high, -n);
                Face(g, Slot.Trim, points[i] + high, points[j] + high, inner[j] + high, inner[i] + high, Vector3.up);
            }
        }

        static List<Vector3> Inset(List<Vector3> points, double area, float by)
        {
            var inner = new List<Vector3>(points.Count);
            // Intersect neighbouring offset edge lines for a joined parapet.
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 prev = (points[i] - points[(i + points.Count - 1) % points.Count]).normalized;
                Vector3 next = (points[(i + 1) % points.Count] - points[i]).normalized;
                float sign = area > 0 ? 1 : -1;
                Vector3 n0 = new Vector3(prev.z, 0, -prev.x) * sign;
                Vector3 n1 = new Vector3(next.z, 0, -next.x) * sign;
                Vector3 bisector = (n0 + n1).normalized;
                float denom = Vector3.Dot(bisector, n1);
                Vector3 inset = denom > .1f ? bisector * Mathf.Min(by / denom, by * 3.75f) : n1 * by;
                inner.Add(points[i] - inset);
            }
            return inner;
        }

        // flat-roof terrace: railing on the parapet, water tank, solar water heater, AC, rebar of the "next storey"
        static void RoofClutter(HouseGeo g, List<Vector3> points, List<int> tris, float top, double area, bool railing, bool tank, bool solar, bool ac, bool rebar, bool chimney)
        {
            // the largest triangle's centroid: inside the footprint
            int bt = 0; float ba = 0;
            for (int i = 0; i < tris.Count; i += 3)
            {
                float A = Mathf.Abs(Vector3.Cross(points[tris[i + 1]] - points[tris[i]], points[tris[i + 2]] - points[tris[i]]).y);
                if (A > ba) { ba = A; bt = i; }
            }
            Vector3 c = (points[tris[bt]] + points[tris[bt + 1]] + points[tris[bt + 2]]) / 3; c.y = top;
            Vector3 u = (points[1] - points[0]).normalized, v = new Vector3(-u.z, 0, u.x);
            float room = Mathf.Sqrt(ba / 2); // ~ the triangle's size
            if (chimney && room > 2.5f) Chimney(g, c - v * 1.1f + u * 1.2f, u, v, top, .2f);
            if (tank && room > 2)
            {
                detailTint = new Color(.12f, .12f, .13f);
                Box(g, Slot.Detail, c + u * .4f, u, v, .9f, 1.1f, .9f, false);
            }
            if (solar && room > 3)
            {
                // collector 2 x 1 m tilted 35 deg, white tank on top
                Vector3 o = c - u * 1.6f - v * .4f + Vector3.up * .3f;
                Vector3 up = (Vector3.up * Mathf.Sin(55 * Mathf.Deg2Rad) - v * Mathf.Cos(55 * Mathf.Deg2Rad)).normalized;
                Vector3 nrm = Vector3.Cross(u, up).normalized; if (nrm.y < 0) nrm = -nrm;
                detailTint = new Color(.1f, .14f, .22f);
                Box3(g, Slot.Detail, o, u, up, -nrm, 1.0f, 1.9f, .06f);
                detailTint = new Color(.86f, .87f, .86f);
                Box(g, Slot.Detail, o + up * 1.9f - v * .15f, u, v, 1.0f, .42f, .42f);
                detailTint = new Color(.5f, .5f, .5f);
                Box(g, Slot.Detail, new Vector3(o.x, top, o.z) - v * .02f, u, v, .05f, .35f, .05f, false);
                Box(g, Slot.Detail, new Vector3(o.x, top, o.z) + u * .95f - v * .02f, u, v, .05f, .35f, .05f, false);
            }
            if (ac && Lod == 0)
            {
                if (AcModel) AcSpots.Add((c + v * 1.2f + u * .4f, v));
                else { detailTint = new Color(.93f, .93f, .92f); Box(g, Slot.Detail, c + v * 1.2f, u, v, .8f, .55f, .3f, false); }
            }
            // large roofs: AC units, tanks and antennas spread over the other big triangles
            var rr = new System.Random((int)(Mathf.Abs(c.x * 13.1f + c.z * 7.7f)) % 100000);
            for (int i = 0; i < tris.Count; i += 3)
            {
                if (i == bt) continue;
                float A = Mathf.Abs(Vector3.Cross(points[tris[i + 1]] - points[tris[i]], points[tris[i + 2]] - points[tris[i]]).y) / 2;
                if (A < 25) continue;
                Vector3 q = (points[tris[i]] + points[tris[i + 1]] + points[tris[i + 2]]) / 3; q.y = top;
                double k = rr.NextDouble();
                if (k < .45) { detailTint = new Color(.88f, .88f, .87f); Box(g, Slot.Detail, q, u, v, .8f, .55f, .3f, false); }
                else if (k < .7) { detailTint = new Color(.13f, .13f, .14f); Box(g, Slot.Detail, q, u, v, .9f, 1.1f, .9f, false); }
                else if (k < .85 && Lod == 0) { detailTint = new Color(.6f, .6f, .6f); Box(g, Slot.Detail, q, u, v, .04f, 2.4f, .04f, false); Box(g, Slot.Detail, q + Vector3.up * 1.9f - u * .5f, u, v, 1f, .03f, .03f, false); }
                else Chimney(g, q, u, v, top, .2f);
            }
            if (Lod > 0) return;
            if (railing)
            {
                var inner = Inset(points, area, .08f);
                detailTint = new Color(.82f, .83f, .82f);
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 p = inner[i], q = inner[(i + 1) % points.Count]; Vector3 e = q - p; float L = e.magnitude; if (L < .3f) continue; e /= L;
                    Vector3 side = new Vector3(-e.z, 0, e.x);
                    Box(g, Slot.Detail, p + Vector3.up * (top + 1.0f) - side * .025f, e, side, L, .045f, .05f, false);
                    Box(g, Slot.Detail, p + Vector3.up * (top + .4f) - side * .02f - e * .02f, e, side, .04f, .6f, .04f, false);
                    int posts = Mathf.FloorToInt(L / 2f);
                    for (int k = 1; k <= posts; k++)
                        Box(g, Slot.Detail, p + e * (L * k / (posts + 1)) + Vector3.up * (top + .4f) - side * .02f, e, side, .03f, .6f, .03f, false);
                }
            }
            if (rebar)
            {
                // starter bars at the corners: four rusty rods each, 0.6-1.2 m above the parapet
                detailTint = new Color(.36f, .2f, .12f);
                var inner = Inset(points, area, .2f);
                for (int i = 0; i < points.Count; i++)
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 p = inner[i] + new Vector3((k & 1) * .14f - .07f, 0, (k >> 1) * .14f - .07f);
                        Box(g, Slot.Detail, new Vector3(p.x, top + .4f, p.z), Vector3.right, Vector3.forward, .022f, .6f + .15f * ((i + k) % 4), .022f, false);
                    }
            }
        }

        /// along: horizontal direction of UV u (null: world x / z); a hall roof's corrugations (texture v) then run across it
        static void Surface(HouseGeo g, List<Vector3> points, List<int> triangles, float y, Slot slot, Vector3 want, Vector3? along = null)
        {
            Vector3 offset = Vector3.up * y;
            Vector3 eu = along ?? Vector3.right, ev = along.HasValue ? new Vector3(-eu.z, 0, eu.x) : Vector3.forward;
            Vector2 UV(Vector3 p) => new Vector2(Vector3.Dot(p, eu), Vector3.Dot(p, ev));
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = points[triangles[i]], b = points[triangles[i + 1]], c = points[triangles[i + 2]];
                Tri(g, slot, a + offset, b + offset, c + offset, UV(a), UV(b), UV(c), want);
            }
        }

        static List<Vector3> CleanRing(List<Vector2> source)
        {
            var points = new List<Vector3>(source.Count);
            foreach (Vector2 p in source)
            {
                if (!Finite(p.x) || !Finite(p.y)) throw new ArgumentException("Footprint contains a non-finite coordinate.");
                Vector3 q = W.U(p.x, 0, p.y);
                if (points.Count == 0 || (q - points[points.Count - 1]).sqrMagnitude > 1e-6f) points.Add(q);
            }
            if (points.Count > 1 && (points[0] - points[points.Count - 1]).sqrMagnitude < 1e-6f) points.RemoveAt(points.Count - 1);
            bool changed = true;
            while (changed && points.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < points.Count; i++)
                {
                    Vector3 a = points[i] - points[(i + points.Count - 1) % points.Count];
                    Vector3 b = points[(i + 1) % points.Count] - points[i];
                    if (Math.Abs(Cross(a, b)) > 1e-5 * Math.Sqrt(a.sqrMagnitude * b.sqrMagnitude) || Vector3.Dot(a, b) < 0) continue;
                    points.RemoveAt(i); changed = true; break;
                }
            }
            return points;
        }

        static double Cross(Vector3 a, Vector3 b) => (double)a.x * b.z - (double)a.z * b.x;

        static double SignedArea(List<Vector3> points)
        {
            double area = 0;
            Vector3 origin = points[0];
            for (int i = 0; i < points.Count; i++) area += Cross(points[i] - origin, points[(i + 1) % points.Count] - origin);
            return area * .5;
        }

        static List<int> Ear(List<Vector3> points, double area)
        {
            var result = new List<int>((points.Count - 2) * 3);
            var ring = new List<int>(points.Count);
            for (int i = 0; i < points.Count; i++) ring.Add(i);
            double sign = area > 0 ? 1 : -1;
            while (ring.Count > 3)
            {
                bool cut = false;
                for (int k = 0; k < ring.Count; k++)
                {
                    int ia = ring[(k + ring.Count - 1) % ring.Count], ib = ring[k], ic = ring[(k + 1) % ring.Count];
                    Vector3 a = points[ia], b = points[ib], c = points[ic];
                    if (Cross(b - a, c - a) * sign <= 1e-8) continue;
                    bool inside = false;
                    foreach (int index in ring)
                    {
                        if (index == ia || index == ib || index == ic) continue;
                        Vector3 p = points[index];
                        if (Cross(b - a, p - a) * sign >= -1e-8 && Cross(c - b, p - b) * sign >= -1e-8
                            && Cross(a - c, p - c) * sign >= -1e-8) { inside = true; break; }
                    }
                    if (inside) continue;
                    result.Add(ia); result.Add(ib); result.Add(ic);
                    ring.RemoveAt(k); cut = true; break;
                }
                if (!cut) throw new ArgumentException("Footprint must be a simple, non-self-intersecting polygon.");
            }
            result.Add(ring[0]); result.Add(ring[1]); result.Add(ring[2]);
            return result;
        }

        public static Mesh ToMesh(HouseGeo g, string name)
        {
            if (g == null) throw new ArgumentNullException(nameof(g));
            Weld(g);
            var mesh = new Mesh { name = name, indexFormat = g.v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(g.v);
            mesh.SetNormals(g.n);
            mesh.SetUVs(0, g.uv);
            mesh.SetUVs(1, g.d);
            mesh.SetColors(g.c);
            mesh.SetUVs(2, g.o);
            mesh.subMeshCount = SlotCount;
            for (int i = 0; i < SlotCount; i++) mesh.SetTriangles(g.sub[i], i, false);
            mesh.RecalculateBounds();
            if (g.v.Count > 0) { mesh.RecalculateTangents(); Compact(mesh); }
            return mesh;
        }

        /// every triangle was emitted with its own three vertices: merge the identical ones (the two triangles of a quad,
        /// neighbouring wall pieces of one plane) - about half the vertices, and half the build size of the town
        static void Weld(HouseGeo g)
        {
            int n = g.v.Count; if (n == 0) return;
            var map = new Dictionary<(Vector3, Vector3, Vector2, Vector4, Color, Vector4), int>(n / 2);
            var remap = new int[n];
            var v = new List<Vector3>(n / 2); var nn = new List<Vector3>(n / 2); var uv = new List<Vector2>(n / 2);
            var d = new List<Vector4>(n / 2); var c = new List<Color>(n / 2); var o = new List<Vector4>(n / 2);
            for (int i = 0; i < n; i++)
            {
                var k = (g.v[i], g.n[i], g.uv[i], g.d[i], g.c[i], g.o[i]);
                if (!map.TryGetValue(k, out int j)) { j = v.Count; map[k] = j; v.Add(g.v[i]); nn.Add(g.n[i]); uv.Add(g.uv[i]); d.Add(g.d[i]); c.Add(g.c[i]); o.Add(g.o[i]); }
                remap[i] = j;
            }
            foreach (var sub in g.sub) for (int i = 0; i < sub.Count; i++) sub[i] = remap[sub[i]];
            g.v = v; g.n = nn; g.uv = uv; g.d = d; g.c = c; g.o = o;
        }

        /// the building vertex in 52 bytes instead of 88 (float everywhere): normal, tangent and the data channel in half floats,
        /// the tint and the occlusion in bytes (kuce3: the whole town is detailed now; SNorm8 normals came out black in the
        /// player beyond the near level, statically batched mid / far meshes); positions and the world-metre UV0 stay 32-bit. Existing data is converted.
        public static void Compact(Mesh mesh)
        {
            mesh.SetVertexBufferParams(mesh.vertexCount,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float16, 4),
                new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float16, 4),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float16, 4),
                new VertexAttributeDescriptor(VertexAttribute.TexCoord2, VertexAttributeFormat.UNorm8, 4));
        }

        /// facade (texture array), glass (rooms), railing (cutout URP Lit): the three materials of a house chunk
        public static Material[] Materials()
        {
            var rail = RailMaterial();
            AssetDatabase.SaveAssets();
            // the UKRAINIAN CITY facade details (submeshes 3 / 4, PaketDetalji) when baked
            if (PaketDetalji.Ready) { var pd = PaketDetalji.Materials(); if (pd[0] && pd[1]) return new[] { BuildingTextures.Facade(), BuildingTextures.Glass(), rail, pd[0], pd[1] }; }
            return new[] { BuildingTextures.Facade(), BuildingTextures.Glass(), rail };
        }

        /// the railing / wire-fence atlas (Tools/zbuke/ograda.py, committed): base colour + alpha, and metallic / smoothness
        const string RailAtlas = "Assets/Textures/Buildings/ograda_atlas.png", RailMetal = "Assets/Textures/Buildings/ograda_metal.png";
        static Texture2D RailTex(string path, bool srgb)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (ti == null) throw new InvalidOperationException(path + " is missing (Tools/zbuke/ograda.py)");
            bool dirty = ti.sRGBTexture != srgb || !ti.mipmapEnabled || ti.wrapModeU != TextureWrapMode.Repeat || ti.wrapModeV != TextureWrapMode.Clamp
                || (srgb && (!ti.alphaIsTransparency || !ti.mipMapsPreserveCoverage || ti.anisoLevel != 4));
            if (dirty)
            {
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = srgb; ti.mipmapEnabled = true;
                ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Trilinear;
                if (srgb)
                {
                    // thin wires: keep their coverage in the mips (alpha-tested at .5), anisotropic along the fence at grazing angles
                    ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
                    ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = .5f; ti.anisoLevel = 4;
                }
                else ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material RailMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Universal Render Pipeline/Lit shader is unavailable.");
            const string path = "Assets/Generated/Materials/House_Rail.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = mat == null;
            if (create) mat = new Material(shader) { name = "House_Rail" };
            else mat.shader = shader;
            mat.enableInstancing = true;
            // the colours, metal and gloss are in the atlas (painted railing bars, green / galvanised wire fences, posts)
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 1f);
            mat.SetFloat("_Metallic", 1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0);
            mat.SetFloat("_Surface", 0);
            mat.SetFloat("_Cull", (float)CullMode.Back);
            if (create) AssetDatabase.CreateAsset(mat, path);
            // the old procedural 16 px bar mask (before the atlas) lived inside the material
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Texture2D texture && texture.name == "RailCoverage") { AssetDatabase.RemoveObjectFromAsset(texture); UnityEngine.Object.DestroyImmediate(texture, true); }
            // Cutout bars retain depth/shadow rendering and avoid transparency sorting across an entire chunk.
            mat.SetTexture("_BaseMap", RailTex(RailAtlas, true));
            mat.SetTextureScale("_BaseMap", new Vector2(8, 1));
            mat.SetTexture("_MetallicGlossMap", RailTex(RailMetal, false)); mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            mat.SetTexture("_BumpMap", null); mat.DisableKeyword("_NORMALMAP");
            mat.SetFloat("_AlphaClip", 1);
            mat.SetFloat("_Cutoff", .5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ---- town boxes (BuildingsFromTiles.Extrude): the same paint, set per building
        public static void Begin(Look k, float ground, float top) { look = k; gY = ground; tY = top; }
        public static void Emit(HouseGeo g, Slot slot, Vector3 a, Vector3 b, Vector3 c, Vector3 want)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(n, want) < 0) n = -n;
            n.Normalize();
            Vector3 u, v;
            if (Mathf.Abs(n.y) > .9f) { u = Vector3.right; v = Vector3.forward; }
            else { u = Vector3.Cross(Vector3.up, n).normalized; v = Vector3.Cross(n, u).normalized; }
            Tri(g, slot, a, b, c, new Vector2(Vector3.Dot(a, u), Vector3.Dot(a, v)), new Vector2(Vector3.Dot(b, u), Vector3.Dot(b, v)),
                new Vector2(Vector3.Dot(c, u), Vector3.Dot(c, v)), want);
        }
        /// sidewalk paving (concrete layer, grey, as the town's pavements): a quad strip a0-a1 (facade foot) to b0-b1 (kerb)
        public static void Paving(HouseGeo g, Vector3 a0, Vector3 a1, Vector3 b1, Vector3 b0)
        {
            var keep = look; look.balcony = new Color(.6f, .59f, .56f); look.dirt = .6f; look.stoneDark = 0; look.stoneGrey = 0;
            gY = Mathf.Min(a0.y, b0.y) - 2; tY = gY + 100;
            Face(g, Slot.Balcony, a0, a1, b1, b0, Vector3.up);
            look = keep;
        }

        /// a window glass quad (a..d counter-clockwise from bottom-left) in its room cell
        public static void EmitGlass(HouseGeo g, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ra, Vector2 rc, Vector3 want, float rnd, float blind)
        {
            glassData = new Vector4(rnd, blind, 0, 0); int i0 = g.v.Count;
            Quad(g, Slot.Glass, a, b, c, d, ra, new Vector2(rc.x, ra.y), rc, new Vector2(ra.x, rc.y), want);
            for (int i = i0; i < g.v.Count; i++)
            {
                Vector2 t = g.uv[i]; g.d[i] = new Vector4(rnd, blind, Mathf.Approximately(t.x, ra.x) ? 0 : 1, Mathf.Approximately(t.y, ra.y) ? 0 : 1);
            }
        }
    }
}
