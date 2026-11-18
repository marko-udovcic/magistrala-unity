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
    public enum Slot { Wall, Roof, Glass, Shutter, Trim, Balcony, Rail, Detail }

    public sealed class HouseGeo
    {
        public List<Vector3> v = new List<Vector3>();
        public List<Vector3> n = new List<Vector3>();
        public List<Vector2> uv = new List<Vector2>();
        public List<Vector4> d = new List<Vector4>();
        public List<Color> c = new List<Color>();
        public List<int>[] sub = { new List<int>(), new List<int>(), new List<int>() };
    }

    /// Per-building appearance after TIPOLOGIJA.md (references/fotografije/zgrade-predlosci): family A old stone house,
    /// B stone ground floor + plaster above, C new plastered house (apartments); colours 55 % neutral / 25 % warm pastel /
    /// 15 % bare stone / 5 % derelict; shutters one colour per house (green 50 / brown 30 / grey 15 / blue 5 %); light frames.
    public struct Look
    {
        public int family; // 0 A, 1 B, 2 C, 3 block (apartment), 4 industrial
        public Color wall, trim, shutter, roof, balcony;
        public int wallCode, trimLayer; public float split, dirt;
        public bool leaves, rollers, derelict, unfinished;

        static Color H(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }
        static readonly string[] Neutral = { "EDE8DC", "E6D9B8", "F4F1E8", "EAE4D4", "E8E1CF", "C9C6BE" };
        static readonly string[] Warm = { "E9D79A", "D8B26A", "DDB08A", "E3C3B0", "E6CFA0", "BFCBB0" };
        static readonly string[] Derelict = { "6E6A58", "807A64", "77725E" };
        static Color Pick(System.Random r, string[] a) => H(a[r.Next(a.Length)]);

        /// ground = the building's ground height (m above sea): old cores lie by the shore
        public static Look Pick(System.Random r, string kind, float ground, float height)
        {
            var k = new Look();
            bool ind = string.Equals(kind, "industrial", StringComparison.OrdinalIgnoreCase) || string.Equals(kind, "warehouse", StringComparison.OrdinalIgnoreCase);
            bool apt = string.Equals(kind, "apartment", StringComparison.OrdinalIgnoreCase);
            double pOld = ground < 10 ? .5 : ground < 25 ? .25 : ground < 60 ? .12 : .05;
            double f = r.NextDouble(), c = r.NextDouble(), s = r.NextDouble(), d = r.NextDouble(), u = r.NextDouble(), q = r.NextDouble();
            k.family = ind ? 4 : apt ? 3 : height > 13 ? 2 : f < pOld * .65 ? 0 : f < pOld ? 1 : 2;
            k.derelict = !ind && c > .95;
            k.wall = ind ? Color.Lerp(H("C9C6BE"), H("E4E2DC"), (float)d) : k.derelict ? Pick(r, Derelict) : c < .66 ? Pick(r, Neutral) : Pick(r, Warm);
            k.dirt = (float)(k.derelict ? .85 : k.family <= 1 ? .35 + .4 * d : ind ? .3 + .3 * d : .1 + .4 * d);
            int P = BuildingTextures.Plaster, S = BuildingTextures.Stone;
            k.wallCode = k.family == 0 ? S + 8 * S : k.family == 1 ? S + 8 * P : ind ? BuildingTextures.Concrete * 9 : P * 9;
            k.split = k.family == 1 ? (q < .6 ? 3.2f : 2.4f) : 0;
            // 12 % of new houses: top storey in bare brick (and rebar on a flat roof)
            k.unfinished = k.family == 2 && u < .08 && height > 5;
            if (k.unfinished) { k.wallCode = P + 8 * BuildingTextures.Brick; k.split = Mathf.Max(2.6f, height - 2.9f); }
            k.trim = k.derelict ? H("E8E4D8") : Color.Lerp(k.wall, H("F2EEE4"), .65f);
            k.trimLayer = k.family <= 1 ? S : P;
            if (k.family <= 1) k.trim = Color.white;
            k.shutter = s < .5 ? (q < .5 ? H("3F6F55") : H("4E8266")) : s < .8 ? H("5B3A29") : s < .95 ? H("3B3E42") : H("3F5E7E");
            if (k.derelict) k.shutter = H("7C8F6A");
            k.leaves = !ind && (k.family <= 1 ? d < .92 : k.family == 2 ? u > .5 && u < .85 : u < .3);
            k.rollers = !ind && !k.leaves && (k.family >= 2 ? u > .2 : false);
            // roof: #C8532F +- 12 %, older roofs faded / browner
            double rt = r.NextDouble();
            k.roof = k.family <= 1 && rt < .4 ? Color.Lerp(Color.white, new Color(.86f, .97f, 1.12f), (float)r.NextDouble())
                : new Color(.9f + .24f * (float)rt, .9f + .2f * (float)r.NextDouble(), .92f + .16f * (float)rt);
            k.roof *= .8f; // the texture's mean is #C8532F in sRGB; a little darker under the bright Dalmatian sun
            k.balcony = Color.Lerp(H("CFCBC2"), k.trim, .5f);
            return k;
        }
    }

    /// <summary>Editor-only, deterministic house geometry in W.U world coordinates. No scene/asset writes in AddHouse.</summary>
    public static class HouseBuilder
    {
        const int SlotCount = 3;
        /// Detail level of the next AddHouse calls (LODGroup per chunk, BuildingsFromTiles): 0 = full (recessed windows with
        /// reveals, 3D sills, railing bars, clutter), 1 = mid (one wall quad per facade, glass / sills / frames / shutters as
        /// thin overlays, chimneys, roof tanks), 2 = far (walls, glass, roofs). The same seed gives the same house in every
        /// level: the random draws do not depend on Lod.
        public static int Lod;
        const float FloorHeight = 3f;

        // the house being emitted (paint of the facade slots and the data channel)
        static Look look; static float gY, tY; static Color detailTint = Color.white; static Vector4 glassData;

        static Vector4 Data(Slot s, float y)
        {
            float code = s == Slot.Wall ? look.wallCode : s == Slot.Roof ? BuildingTextures.Roof * 9 : s == Slot.Trim ? look.trimLayer * 9
                : s == Slot.Balcony ? BuildingTextures.Concrete * 9 : s == Slot.Shutter ? BuildingTextures.Louvre * 9 : BuildingTextures.Plain * 9;
            return new Vector4(code, y - gY, tY - y, s == Slot.Detail ? look.dirt * .4f : look.dirt);
        }
        static Color Tint(Slot s)
        {
            Color t = s == Slot.Wall ? look.wall : s == Slot.Roof ? look.roof : s == Slot.Trim ? look.trim : s == Slot.Balcony ? look.balcony
                : s == Slot.Shutter ? look.shutter : detailTint;
            // tints are authored in sRGB; vertex colours are read as linear; the roof tint is a multiplier (kept linear)
            Color lin = s == Slot.Roof ? t : t.linear;
            lin.a = s == Slot.Wall ? look.split / 40f : 0; return lin;
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

        /// <param name="footprintGame">Game (x,z), metres; simple ring, either winding, no repeated closing point.</param>
        public static void AddHouse(HouseGeo g, List<Vector2> footprintGame, string kind,
            double bottomY, double baseY, double topY, int seed)
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
            var random = new System.Random(seed);
            bool industrial = string.Equals(kind, "industrial", StringComparison.OrdinalIgnoreCase)
                || string.Equals(kind, "warehouse", StringComparison.OrdinalIgnoreCase);
            bool apartment = string.Equals(kind, "apartment", StringComparison.OrdinalIgnoreCase);
            float bottom = (float)bottomY, ground = (float)baseY, top = (float)topY;
            look = Look.Pick(new System.Random(seed * 31 + 7), kind, ground, top - ground); gY = ground; tY = top;
            var clutter = new System.Random(seed * 17 + 3); // the clutter's own draws (identical in every level)
            bool hasAc = !industrial && clutter.NextDouble() < .7, hasDish = !industrial && clutter.NextDouble() < .35;
            bool hasCable = !industrial && clutter.NextDouble() < .9;
            float plinth = Mathf.Min(ground + .5f, top);
            // Consider partial top storeys too; the opening-height test below rejects those too short for a window.
            int floors = Mathf.Max(1, Mathf.CeilToInt((top - ground) / FloorHeight));
            int longest = 0;
            float longestLength = 0;
            for (int i = 0; i < points.Count; i++)
            {
                float length = Vector3.Distance(points[i], points[(i + 1) % points.Count]);
                if (length > longestLength) { longestLength = length; longest = i; }
            }
            bool hip = !industrial && points.Count <= 8 && Math.Abs(signedArea) < 450;
            int dishFacade = hasDish ? clutter.Next(points.Count) : -1;
            float dishAt = (float)clutter.NextDouble();

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 a = points[i], b = points[(i + 1) % points.Count];
                Vector3 along = (b - a).normalized;
                // Polygon winding, rather than a centroid test, also works for concave corners.
                Vector3 outward = new Vector3(along.z, 0, -along.x) * (signedArea > 0 ? 1 : -1);
                var f = new Facade { origin = a, along = along, outward = outward,
                    length = Vector3.Distance(a, b), baseY = ground };
                Wall(g, f, Slot.Balcony, 0, f.length, bottom, plinth); // concrete plinth
                if (Lod > 0) Wall(g, f, Slot.Wall, 0, f.length, plinth, top);
                int columns = f.length > 3 ? Mathf.Max(1, Mathf.FloorToInt(f.length / (industrial ? 7f : 3.2f))) : 0;
                if (industrial) columns = Mathf.Min(columns, 3);
                float lastY = plinth;
                for (int floor = 0; floor < floors && columns > 0; floor++)
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
                    int balconyColumn = columns / 2;
                    if (balcony && Lod == 0)
                    {
                        float doorCentre = (balconyColumn + .5f) * spacing;
                        Wall(g, f, Slot.Wall, 0, doorCentre - width / 2, lastY, y0);
                        Wall(g, f, Slot.Wall, doorCentre + width / 2, f.length, lastY, y0);
                        Wall(g, f, Slot.Wall, doorCentre - width / 2, doorCentre + width / 2,
                            lastY, ground + floor * FloorHeight + .08f);
                    }
                    else if (Lod == 0) Wall(g, f, Slot.Wall, 0, f.length, lastY, y0);
                    float floorY = ground + floor * FloorHeight;
                    for (int k = 0; k < columns; k++)
                    {
                        float centre = (k + .5f) * spacing, x0 = centre - width / 2, x1 = centre + width / 2;
                        if (Lod == 0) Wall(g, f, Slot.Wall, cursor, x0, y0, y1);
                        // A balcony door drops to the slab; cut the lower wall only in this column.
                        bool door = balcony && k == balconyColumn;
                        float openingBottom = door ? floorY + .08f : y0;
                        // the window's state (same draws in every level): 0 open leaves, 1 closed, 2 one leaf closed;
                        // rollers: the blind's drop; the glass: room / lamp / curtains
                        double st = random.NextDouble(), rr = random.NextDouble();
                        bool leaves = look.leaves && !shop;
                        int state = !leaves ? 0 : st < .5 && !door ? 1 : st < .65 ? 2 : 0;
                        float blind = look.rollers && !shop ? (rr < .35 ? 0 : rr < .75 ? .2f + .5f * (float)(rr - .35) / .4f : .97f) : 0;
                        glassData = new Vector4((float)random.NextDouble(), blind, 0, 0);
                        var cell = new Rect(k * spacing, floorY, spacing, FloorHeight);
                        if (Lod == 0) Window(g, f, x0, x1, openingBottom, y1, leaves, state, cell, look.rollers && !shop, !door);
                        else FlatWindow(g, f, x0, x1, openingBottom, y1, leaves, state, cell, !door);
                        if (door)
                        {
                            float balconyWidth = Mathf.Min(2f + (float)random.NextDouble() * 2f,
                                2f * Mathf.Min(centre - .3f, f.length - centre - .3f));
                            bool laundry = clutter.NextDouble() < .3; int pots = clutter.Next(4);
                            if (Lod < 2) Balcony(g, f, centre, floorY, balconyWidth, laundry, pots, clutter.Next());
                        }
                        // an AC unit between this window and the next (70 % of houses, ~1-3 each)
                        bool ac = hasAc && k < columns - 1 && clutter.NextDouble() < .22 && !(balcony && (k == balconyColumn || k + 1 == balconyColumn));
                        if (ac && Lod == 0) AcUnit(g, f, (k + 1) * spacing, y0 + .25f);
                        cursor = x1;
                    }
                    if (Lod == 0) Wall(g, f, Slot.Wall, cursor, f.length, y0, y1);
                    lastY = y1;
                }
                if (Lod == 0) Wall(g, f, Slot.Wall, 0, f.length, lastY, top);
                if (Lod == 0 && i == longest && !industrial) MeterBox(g, f, Mathf.Min(.7f, f.length * .2f));
                if (Lod == 0 && i == longest && hasCable) Cable(g, f, top - .4f);
                if (Lod == 0 && i == longest && hip) { detailTint = new Color(.55f, .56f, .57f); Box(g, Slot.Detail, f.P(.12f, ground, 0), f.along, f.outward, .08f, top - .12f - ground, .08f, false); } // downpipe
                if (Lod == 0 && i == dishFacade && f.length > 2.5f && top - ground > 4) Dish(g, f.P(.8f + dishAt * (f.length - 1.6f), top - 1.0f, .02f), f.outward, f.along);
            }

            // Seal the foundation, including concave footprints.
            if (Lod == 0) Surface(g, points, triangles, bottom, Slot.Balcony, Vector3.down);
            bool chim = clutter.NextDouble() < .85, chim2 = clutter.NextDouble() < .3, antenna = clutter.NextDouble() < .45;
            double ra = clutter.NextDouble(), rb = clutter.NextDouble(), rc = clutter.NextDouble(), rd = clutter.NextDouble();
            if (hip)
                HipRoof(g, points, top, chim, chim2, antenna, (float)ra);
            else
            {
                FlatRoof(g, points, triangles, top, signedArea);
                if (!industrial && Lod < 2 && points.Count <= 12)
                    RoofClutter(g, points, triangles, top, signedArea, rb < .5, rc < .35, rd < .3, ra < .3, look.unfinished);
            }
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
            const float recess = -.12f;
            Vector3 a = f.P(x0, y0), b = f.P(x1, y0), c = f.P(x1, y1), d = f.P(x0, y1);
            Vector3 ar = f.P(x0, y0, recess), br = f.P(x1, y0, recess), cr = f.P(x1, y1, recess), dr = f.P(x0, y1, recess);
            if (state == 1) Shutter(g, f, x0, x1, y0, y1, recess + .02f, recess + .02f); // closed leaves in the opening
            else Glass(g, f, x0, x1, y0, y1, recess, cell);
            if (state == 2) Shutter(g, f, (x0 + x1) / 2, x1, y0, y1, recess + .03f, recess + .03f);
            Face(g, Slot.Wall, a, ar, dr, d, f.along);
            Face(g, Slot.Wall, br, b, c, cr, -f.along);
            Face(g, Slot.Wall, a, b, br, ar, Vector3.up);
            Face(g, Slot.Wall, dr, cr, c, d, Vector3.down);
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
            if (roller) Box(g, Slot.Trim, f.P(x0 - .12f, y1 + .12f, -.02f), f.along, f.outward, x1 - x0 + .24f, .24f, .1f);
            if (!leaves) return;
            // Open leaves lie beside the reveal, slightly angled off the plaster (no coplanar overlay).
            if (state != 1) Shutter(g, f, x0 - .49f, x0 - .04f, y0, y1, .065f, .035f);
            if (state == 0) Shutter(g, f, x1 + .04f, x1 + .49f, y0, y1, .035f, .065f);
        }

        // Lod 1 / 2: the opening as a glass quad 3 cm off the continuous wall (no reveals); Lod 1 adds sill, frame and leaves
        static void FlatWindow(HouseGeo g, Facade f, float x0, float x1, float y0, float y1, bool leaves, int state, Rect cell, bool sill)
        {
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
            const float w = .12f, z = .015f;
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
        static void Box(HouseGeo g, Slot slot, Vector3 origin, Vector3 x, Vector3 z, float width, float height, float depth, bool bottomFace = true)
        {
            int firstVertex = g.v.Count;
            Vector3 a = origin, b = a + x * width, c = b + z * depth, d = a + z * depth;
            Vector3 h = Vector3.up * height;
            if (bottomFace) Face(g, slot, a, b, c, d, Vector3.down);
            Face(g, slot, a + h, d + h, c + h, b + h, Vector3.up);
            Face(g, slot, a, a + h, b + h, b, -z);
            Face(g, slot, d, c, c + h, d + h, z);
            Face(g, slot, a, d, d + h, a + h, -x);
            Face(g, slot, b, b + h, c + h, c, x);
            // Bars sample one opaque texel of the railing mask; only the panel is perforated.
            if (slot == Slot.Rail)
                for (int i = firstVertex; i < g.uv.Count; i++) g.uv[i] = new Vector2(1f / 2048f, 1f / 2048f);
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
            Quad(g, Slot.Rail, a, b, b + h, a + h, new Vector2(0, 0), new Vector2(L / 3, 0), new Vector2(L / 3, .28f), new Vector2(0, .28f), outward);
            Quad(g, Slot.Rail, a, b, b + h, a + h, new Vector2(0, 0), new Vector2(L / 3, 0), new Vector2(L / 3, .28f), new Vector2(0, .28f), -outward);
        }

        // ---- clutter (TIPOLOGIJA 6 / 9.7): AC units, meter box, cables, satellite dish, chimneys, antennas, roof terraces
        static void AcUnit(HouseGeo g, Facade f, float x, float y)
        {
            detailTint = new Color(.93f, .93f, .92f);
            Box(g, Slot.Detail, f.P(x - .4f, y, 0), f.along, f.outward, .8f, .55f, .28f);
            detailTint = new Color(.25f, .26f, .27f); // fan grille
            Vector3 c = f.P(x + .08f, y + .275f, .285f);
            Face(g, Slot.Detail, c - f.along * .2f - Vector3.up * .2f, c + f.along * .2f - Vector3.up * .2f, c + f.along * .2f + Vector3.up * .2f, c - f.along * .2f + Vector3.up * .2f, f.outward);
            detailTint = new Color(.9f, .9f, .88f); // the pipe duct down the wall
            Box(g, Slot.Detail, f.P(x - .36f, y - .9f, 0), f.along, f.outward, .07f, .9f, .06f, false);
        }
        static void MeterBox(HouseGeo g, Facade f, float x)
        {
            detailTint = new Color(.82f, .82f, .78f);
            Box(g, Slot.Detail, f.P(x, gY + .7f, 0), f.along, f.outward, .45f, .6f, .14f, false);
        }
        static void Cable(HouseGeo g, Facade f, float y)
        {
            detailTint = new Color(.1f, .1f, .1f);
            Box(g, Slot.Detail, f.P(.15f, y, .03f), f.along, f.outward, f.length - .3f, .025f, .025f, false);
            Box(g, Slot.Detail, f.P(.15f, gY + 1.3f, .03f), f.along, f.outward, .025f, y - gY - 1.3f, .025f, false);
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

        static void HipRoof(HouseGeo g, List<Vector3> points, float top, bool chimney, bool chimney2, bool antenna, float rnd)
        {
            Vector3 origin = points[0], u = Vector3.right, v = Vector3.forward;
            float minU = 0, maxU = 0, minV = 0, maxV = 0;
            double best = double.MaxValue;
            // Minimum-area enclosing rectangle: one candidate per footprint edge (small rings only).
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 x = (points[(i + 1) % points.Count] - points[i]).normalized;
                Vector3 z = new Vector3(-x.z, 0, x.x);
                float loX = float.MaxValue, hiX = float.MinValue, loZ = float.MaxValue, hiZ = float.MinValue;
                foreach (Vector3 p in points)
                {
                    Vector3 d = p - origin;
                    float px = Vector3.Dot(d, x), pz = Vector3.Dot(d, z);
                    loX = Mathf.Min(loX, px); hiX = Mathf.Max(hiX, px);
                    loZ = Mathf.Min(loZ, pz); hiZ = Mathf.Max(hiZ, pz);
                }
                double area = (double)(hiX - loX) * (hiZ - loZ);
                if (area >= best) continue;
                best = area; u = x; v = z; minU = loX; maxU = hiX; minV = loZ; maxV = hiZ;
            }
            Vector3 centre = origin + u * ((minU + maxU) / 2) + v * ((minV + maxV) / 2) + Vector3.up * top;
            float halfLength = (maxU - minU) / 2 + .35f, halfWidth = (maxV - minV) / 2 + .35f;
            if (halfLength < halfWidth)
            {
                var axis = u; u = v; v = -axis;
                float size = halfLength; halfLength = halfWidth; halfWidth = size;
            }
            float rise = halfWidth * Mathf.Tan(22f * Mathf.Deg2Rad);
            float ridgeHalf = Mathf.Max(0, halfLength - halfWidth);
            Vector3 a = centre - u * halfLength - v * halfWidth;
            Vector3 b = centre + u * halfLength - v * halfWidth;
            Vector3 c = centre + u * halfLength + v * halfWidth;
            Vector3 d0 = centre - u * halfLength + v * halfWidth;
            Vector3 r0 = centre - u * ridgeHalf + Vector3.up * rise;
            Vector3 r1 = centre + u * ridgeHalf + Vector3.up * rise;
            Face(g, Slot.Roof, a, b, r1, r0, Vector3.up - v);
            Face(g, Slot.Roof, c, d0, r0, r1, Vector3.up + v);
            RoofTriangle(g, b, c, r1, Vector3.up + u);
            RoofTriangle(g, d0, a, r0, Vector3.up - u);
            Vector3 down = Vector3.down * .14f;
            Face(g, Slot.Trim, a + down, b + down, b, a, -v);
            Face(g, Slot.Trim, b + down, c + down, c, b, u);
            Face(g, Slot.Trim, c + down, d0 + down, d0, c, v);
            Face(g, Slot.Trim, d0 + down, a + down, a, d0, -u);
            // Close the projecting underside: no see-through eaves when driving below a house.
            Face(g, Slot.Trim, a + down, b + down, c + down, d0 + down, Vector3.down);
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

        static void FlatRoof(HouseGeo g, List<Vector3> points, List<int> triangles, float top, double area)
        {
            Surface(g, points, triangles, top, Slot.Balcony, Vector3.up);
            var inner = Inset(points, area, .16f);
            Vector3 low = Vector3.up * top, high = Vector3.up * (top + .4f);
            for (int i = 0; i < points.Count; i++)
            {
                int j = (i + 1) % points.Count;
                Vector3 e = (points[j] - points[i]).normalized;
                Vector3 n = new Vector3(e.z, 0, -e.x) * (area > 0 ? 1 : -1);
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
        static void RoofClutter(HouseGeo g, List<Vector3> points, List<int> tris, float top, double area, bool railing, bool tank, bool solar, bool ac, bool rebar)
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
                detailTint = new Color(.93f, .93f, .92f);
                Box(g, Slot.Detail, c + v * 1.2f, u, v, .8f, .55f, .3f, false);
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

        static void Surface(HouseGeo g, List<Vector3> points, List<int> triangles, float y, Slot slot, Vector3 want)
        {
            Vector3 offset = Vector3.up * y;
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = points[triangles[i]], b = points[triangles[i + 1]], c = points[triangles[i + 2]];
                Tri(g, slot, a + offset, b + offset, c + offset,
                    new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z), want);
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
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(g.v);
            mesh.SetNormals(g.n);
            mesh.SetUVs(0, g.uv);
            mesh.SetUVs(1, g.d);
            mesh.SetColors(g.c);
            mesh.subMeshCount = SlotCount;
            for (int i = 0; i < SlotCount; i++) mesh.SetTriangles(g.sub[i], i, false);
            mesh.RecalculateBounds();
            if (g.v.Count > 0) mesh.RecalculateTangents();
            return mesh;
        }

        /// facade (texture array), glass (rooms), railing (cutout URP Lit): the three materials of a house chunk
        public static Material[] Materials()
        {
            var rail = RailMaterial();
            AssetDatabase.SaveAssets();
            return new[] { BuildingTextures.Facade(), BuildingTextures.Glass(), rail };
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
            mat.SetColor("_BaseColor", new Color(.15f, .15f, .16f));
            mat.SetFloat("_Smoothness", .4f);
            mat.SetFloat("_Metallic", .6f);
            mat.SetFloat("_Surface", 0);
            mat.SetFloat("_Cull", (float)CullMode.Back);
            if (create) AssetDatabase.CreateAsset(mat, path);
            // Cutout bars retain depth/shadow rendering and avoid transparency sorting across an entire chunk.
            Texture2D mask = null;
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                if (asset is Texture2D texture && texture.name == "RailCoverage") { mask = texture; break; }
            if (mask == null)
            {
                mask = new Texture2D(16, 16, TextureFormat.RGBA32, false) { name = "RailCoverage",
                    filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                var pixels = new Color[256];
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++) pixels[y * 16 + x] = new Color(1, 1, 1, x < 2 ? 1 : 0);
                mask.SetPixels(pixels); mask.Apply();
                AssetDatabase.AddObjectToAsset(mask, mat);
            }
            mat.SetTexture("_BaseMap", mask);
            mat.SetTextureScale("_BaseMap", new Vector2(8, 1));
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
