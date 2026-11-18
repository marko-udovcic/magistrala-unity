using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    // The numeric order is also the MeshRenderer.sharedMaterials order.
    public enum Slot { Wall0, Wall1, Wall2, Wall3, Roof, Glass, Shutter, ShutterBrown, Trim, Balcony, Rail }

    public sealed class HouseGeo
    {
        public List<Vector3> v = new List<Vector3>();
        public List<Vector3> n = new List<Vector3>();
        public List<Vector2> uv = new List<Vector2>();
        public List<int>[] sub = NewSubmeshes();

        static List<int>[] NewSubmeshes()
        {
            var result = new List<int>[11];
            for (int i = 0; i < result.Length; i++) result[i] = new List<int>();
            return result;
        }
    }

    /// <summary>Editor-only, deterministic house geometry in W.U world coordinates. No scene/asset writes in AddHouse.</summary>
    public static class HouseBuilder
    {
        const int SlotCount = 11;
        /// Detail level of the next AddHouse calls (LODGroup per chunk, BuildingsFromTiles): 0 = full (recessed windows with
        /// reveals, 3D sills, railing bars), 1 = mid (one wall quad per facade, glass / sill fronts / shutters as thin overlays,
        /// balcony slab + panels), 2 = far (walls, glass overlays, roofs). The same seed gives the same house in every level:
        /// the random draws do not depend on Lod.
        public static int Lod;
        const float FloorHeight = 3f;
        const string MaterialFolder = "Assets/Generated/Materials";
        const string TextureFolder = "Assets/Textures/PolyHaven/";

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
            g.sub[(int)slot].Add(index); g.sub[(int)slot].Add(index + 1); g.sub[(int)slot].Add(index + 2);
        }

        static void Quad(HouseGeo g, Slot slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td, Vector3 want)
        {
            Tri(g, slot, a, b, c, ta, tb, tc, want);
            Tri(g, slot, a, c, d, ta, tc, td, want);
        }

        // Planar coordinates on the actual face, rather than horizontal projection (roof slopes keep real scale).
        static void Face(HouseGeo g, Slot slot, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 want, float tile = 3f)
        {
            Vector3 u = (b - a).normalized;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            Vector3 v = Vector3.Cross(normal, u).normalized;
            if (Vector3.Dot(d - a, v) < 0) v = -v;
            Quad(g, slot, a, b, c, d, Vector2.zero,
                new Vector2(Vector3.Dot(b - a, u), Vector3.Dot(b - a, v)) / tile,
                new Vector2(Vector3.Dot(c - a, u), Vector3.Dot(c - a, v)) / tile,
                new Vector2(Vector3.Dot(d - a, u), Vector3.Dot(d - a, v)) / tile, want);
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
                new Vector2(x0, y0 - f.baseY) / 3f, new Vector2(x1, y0 - f.baseY) / 3f,
                new Vector2(x1, y1 - f.baseY) / 3f, new Vector2(x0, y1 - f.baseY) / 3f, f.outward);
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
            Slot wall = industrial ? Slot.Wall0 : (Slot)random.Next(4);
            bool shutters = !industrial && random.NextDouble() < .7;
            Slot shutter = random.NextDouble() < .8 ? Slot.Shutter : Slot.ShutterBrown;
            float bottom = (float)bottomY, ground = (float)baseY, top = (float)topY;
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

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 a = points[i], b = points[(i + 1) % points.Count];
                Vector3 along = (b - a).normalized;
                // Polygon winding, rather than a centroid test, also works for concave corners.
                Vector3 outward = new Vector3(along.z, 0, -along.x) * (signedArea > 0 ? 1 : -1);
                var f = new Facade { origin = a, along = along, outward = outward,
                    length = Vector3.Distance(a, b), baseY = ground };
                Wall(g, f, Slot.Trim, 0, f.length, bottom, plinth);
                if (Lod > 0) Wall(g, f, wall, 0, f.length, plinth, top);
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
                        Wall(g, f, wall, 0, doorCentre - width / 2, lastY, y0);
                        Wall(g, f, wall, doorCentre + width / 2, f.length, lastY, y0);
                        Wall(g, f, wall, doorCentre - width / 2, doorCentre + width / 2,
                            lastY, ground + floor * FloorHeight + .08f);
                    }
                    else if (Lod == 0) Wall(g, f, wall, 0, f.length, lastY, y0);
                    for (int k = 0; k < columns; k++)
                    {
                        float centre = (k + .5f) * spacing, x0 = centre - width / 2, x1 = centre + width / 2;
                        if (Lod == 0) Wall(g, f, wall, cursor, x0, y0, y1);
                        // A balcony door drops to the slab; cut the lower wall only in this column.
                        float openingBottom = balcony && k == balconyColumn ? ground + floor * FloorHeight + .08f : y0;
                        if (Lod == 0) Window(g, f, wall, shutter, x0, x1, openingBottom, y1, shutters && !shop);
                        else FlatWindow(g, f, shutter, x0, x1, openingBottom, y1, shutters && !shop);
                        if (balcony && k == balconyColumn)
                        {
                            float balconyWidth = Mathf.Min(2f + (float)random.NextDouble() * 2f,
                                2f * Mathf.Min(centre - .3f, f.length - centre - .3f));
                            if (Lod < 2) Balcony(g, f, centre, ground + floor * FloorHeight, balconyWidth);
                        }
                        cursor = x1;
                    }
                    if (Lod == 0) Wall(g, f, wall, cursor, f.length, y0, y1);
                    lastY = y1;
                }
                if (Lod == 0) Wall(g, f, wall, 0, f.length, lastY, top);
            }

            // Seal the foundation, including concave footprints.
            if (Lod == 0) Surface(g, points, triangles, bottom, Slot.Trim, Vector3.down);
            if (!industrial && points.Count <= 8 && Math.Abs(signedArea) < 450)
                HipRoof(g, points, top);
            else
                FlatRoof(g, points, triangles, top, wall, signedArea);
        }

        static bool Finite(double x) => !double.IsNaN(x) && !double.IsInfinity(x) && Math.Abs(x) < float.MaxValue;

        static void Window(HouseGeo g, Facade f, Slot wall, Slot shutter, float x0, float x1, float y0, float y1, bool leaves)
        {
            const float recess = -.12f;
            Vector3 a = f.P(x0, y0), b = f.P(x1, y0), c = f.P(x1, y1), d = f.P(x0, y1);
            Vector3 ar = f.P(x0, y0, recess), br = f.P(x1, y0, recess), cr = f.P(x1, y1, recess), dr = f.P(x0, y1, recess);
            Face(g, Slot.Glass, ar, br, cr, dr, f.outward);
            Face(g, wall, a, ar, dr, d, f.along);
            Face(g, wall, br, b, c, cr, -f.along);
            Face(g, wall, a, b, br, ar, Vector3.up);
            Face(g, wall, dr, cr, c, d, Vector3.down);
            // Slightly below the bottom reveal, avoiding coplanar plaster/stone surfaces.
            Vector3 s0 = f.P(x0 - .08f, y0 - .085f, .05f), s1 = f.P(x1 + .08f, y0 - .085f, .05f);
            Vector3 s2 = s1 + Vector3.up * .07f, s3 = s0 + Vector3.up * .07f;
            Face(g, Slot.Trim, s0, s1, s2, s3, f.outward);
            Face(g, Slot.Trim, s3, s2, f.P(x1 + .08f, y0 - .015f, -.12f), f.P(x0 - .08f, y0 - .015f, -.12f), Vector3.up);
            Face(g, Slot.Trim, s0, s3, f.P(x0 - .08f, y0 - .015f, -.12f), f.P(x0 - .08f, y0 - .085f, -.12f), -f.along);
            Face(g, Slot.Trim, s1, f.P(x1 + .08f, y0 - .085f, -.12f), f.P(x1 + .08f, y0 - .015f, -.12f), s2, f.along);
            if (!leaves) return;
            // Open leaves lie beside the reveal, slightly angled off the plaster (no coplanar overlay).
            Shutter(g, f, shutter, x0 - .49f, x0 - .04f, y0, y1, .065f, .035f);
            Shutter(g, f, shutter, x1 + .04f, x1 + .49f, y0, y1, .035f, .065f);
        }

        // Lod 1 / 2: the opening as a glass quad 3 cm off the continuous wall (no reveals); Lod 1 adds the sill front and the leaves
        static void FlatWindow(HouseGeo g, Facade f, Slot shutter, float x0, float x1, float y0, float y1, bool leaves)
        {
            Face(g, Slot.Glass, f.P(x0, y0, .03f), f.P(x1, y0, .03f), f.P(x1, y1, .03f), f.P(x0, y1, .03f), f.outward);
            if (Lod > 1) return;
            Vector3 s0 = f.P(x0 - .08f, y0 - .085f, .05f), s1 = f.P(x1 + .08f, y0 - .085f, .05f);
            Face(g, Slot.Trim, s0, s1, s1 + Vector3.up * .07f, s0 + Vector3.up * .07f, f.outward);
            if (!leaves) return;
            Shutter(g, f, shutter, x0 - .49f, x0 - .04f, y0, y1, .065f, .035f);
            Shutter(g, f, shutter, x1 + .04f, x1 + .49f, y0, y1, .035f, .065f);
        }

        static void Shutter(HouseGeo g, Facade f, Slot slot, float x0, float x1, float y0, float y1, float d0, float d1)
        {
            // Full-leaf UVs: the supplied shutter image contains its horizontal louvres.
            Quad(g, slot, f.P(x0, y0, d0), f.P(x1, y0, d1), f.P(x1, y1, d1), f.P(x0, y1, d0),
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up, f.outward);
        }

        // Oriented closed box. x/z are horizontal unit axes; y is world up.
        static void Box(HouseGeo g, Slot slot, Vector3 origin, Vector3 x, Vector3 z, float width, float height, float depth)
        {
            int firstVertex = g.v.Count;
            Vector3 a = origin, b = a + x * width, c = b + z * depth, d = a + z * depth;
            Vector3 h = Vector3.up * height;
            Face(g, slot, a, b, c, d, Vector3.down);
            Face(g, slot, a + h, d + h, c + h, b + h, Vector3.up);
            Face(g, slot, a, a + h, b + h, b, -z);
            Face(g, slot, d, c, c + h, d + h, z);
            Face(g, slot, a, d, d + h, a + h, -x);
            Face(g, slot, b, b + h, c + h, c, x);
            // Bars sample one opaque texel of the railing mask; only the panel is perforated.
            if (slot == Slot.Rail)
                for (int i = firstVertex; i < g.uv.Count; i++) g.uv[i] = new Vector2(1f / 2048f, 1f / 2048f);
        }

        static void Balcony(HouseGeo g, Facade f, float centre, float floorY, float width)
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
        }

        static void RailPanel(HouseGeo g, Vector3 a, Vector3 b, Vector3 outward)
        {
            Vector3 h = Vector3.up * .84f;
            Face(g, Slot.Rail, a, b, b + h, a + h, outward);
            Face(g, Slot.Rail, a, b, b + h, a + h, -outward);
        }

        static void HipRoof(HouseGeo g, List<Vector3> points, float top)
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
            Face(g, Slot.Roof, a, b, r1, r0, Vector3.up - v, 2.5f);
            Face(g, Slot.Roof, c, d0, r0, r1, Vector3.up + v, 2.5f);
            RoofTriangle(g, b, c, r1, Vector3.up + u);
            RoofTriangle(g, d0, a, r0, Vector3.up - u);
            Vector3 down = Vector3.down * .14f;
            Face(g, Slot.Trim, a + down, b + down, b, a, -v);
            Face(g, Slot.Trim, b + down, c + down, c, b, u);
            Face(g, Slot.Trim, c + down, d0 + down, d0, c, v);
            Face(g, Slot.Trim, d0 + down, a + down, a, d0, -u);
            // Close the projecting underside: no see-through eaves when driving below a house.
            Face(g, Slot.Trim, a + down, b + down, c + down, d0 + down, Vector3.down);
        }

        static void RoofTriangle(HouseGeo g, Vector3 a, Vector3 b, Vector3 c, Vector3 want)
        {
            Vector3 u = (b - a).normalized;
            Vector3 v = Vector3.Cross(Vector3.Cross(b - a, c - a).normalized, u).normalized;
            Tri(g, Slot.Roof, a, b, c, Vector2.zero, new Vector2(Vector3.Distance(a, b), 0) / 2.5f,
                new Vector2(Vector3.Dot(c - a, u), Vector3.Dot(c - a, v)) / 2.5f, want);
        }

        static void FlatRoof(HouseGeo g, List<Vector3> points, List<int> triangles, float top, Slot wall, double area)
        {
            Surface(g, points, triangles, top, Slot.Balcony, Vector3.up);
            var inner = new List<Vector3>(points.Count);
            // Intersect neighbouring offset edge lines for a joined 16 cm parapet.
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 prev = (points[i] - points[(i + points.Count - 1) % points.Count]).normalized;
                Vector3 next = (points[(i + 1) % points.Count] - points[i]).normalized;
                float sign = area > 0 ? 1 : -1;
                Vector3 n0 = new Vector3(prev.z, 0, -prev.x) * sign;
                Vector3 n1 = new Vector3(next.z, 0, -next.x) * sign;
                Vector3 bisector = (n0 + n1).normalized;
                float denom = Vector3.Dot(bisector, n1);
                Vector3 inset = denom > .1f ? bisector * Mathf.Min(.16f / denom, .6f) : n1 * .16f;
                inner.Add(points[i] - inset);
            }
            Vector3 low = Vector3.up * top, high = Vector3.up * (top + .4f);
            for (int i = 0; i < points.Count; i++)
            {
                int j = (i + 1) % points.Count;
                Vector3 e = (points[j] - points[i]).normalized;
                Vector3 n = new Vector3(e.z, 0, -e.x) * (area > 0 ? 1 : -1);
                Face(g, wall, points[i] + low, points[j] + low, points[j] + high, points[i] + high, n);
                Face(g, wall, inner[i] + low, inner[j] + low, inner[j] + high, inner[i] + high, -n);
                Face(g, Slot.Trim, points[i] + high, points[j] + high, inner[j] + high, inner[i] + high, Vector3.up);
            }
        }

        static void Surface(HouseGeo g, List<Vector3> points, List<int> triangles, float y, Slot slot, Vector3 want)
        {
            Vector3 offset = Vector3.up * y, origin = points[0];
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = points[triangles[i]], b = points[triangles[i + 1]], c = points[triangles[i + 2]];
                Tri(g, slot, a + offset, b + offset, c + offset,
                    new Vector2(a.x - origin.x, a.z - origin.z) / 3f,
                    new Vector2(b.x - origin.x, b.z - origin.z) / 3f,
                    new Vector2(c.x - origin.x, c.z - origin.z) / 3f, want);
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
            mesh.subMeshCount = SlotCount;
            for (int i = 0; i < SlotCount; i++) mesh.SetTriangles(g.sub[i], i, false);
            mesh.RecalculateBounds();
            if (g.v.Count > 0) mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Explicit asset-writing entry point; reuses and configures the same eleven assets on every call.</summary>
        public static Material[] Materials()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Universal Render Pipeline/Lit shader is unavailable.");
            EnsureFolder(MaterialFolder);
            Texture2D plaster = Texture("white_plaster_02_soft.jpg", false);
            Texture2D plasterNormal = Texture("white_plaster_02_nor.jpg", true);
            Texture2D roof = Texture("clay_roof_tiles_02_diff.jpg", false);
            Texture2D roofNormal = Texture("clay_roof_tiles_02_nor.jpg", true);
            Texture2D wood = Texture("wood_shutter_diff.jpg", false);
            Texture2D woodNormal = Texture("wood_shutter_nor.jpg", true);
            var result = new Material[SlotCount];
            result[0] = Material(shader, Slot.Wall0, new Color(.95f, .94f, .9f), plaster, plasterNormal);
            result[1] = Material(shader, Slot.Wall1, new Color(.95f, .88f, .74f), plaster, plasterNormal);
            result[2] = Material(shader, Slot.Wall2, new Color(.93f, .8f, .6f), plaster, plasterNormal);
            result[3] = Material(shader, Slot.Wall3, new Color(.94f, .83f, .78f), plaster, plasterNormal);
            // The colour photograph supplies the terracotta hue; white avoids tinting it twice.
            result[4] = Material(shader, Slot.Roof, Color.white, roof, roofNormal);
            result[5] = Material(shader, Slot.Glass, new Color(.08f, .1f, .12f), null, null, .85f, .2f);
            result[6] = Material(shader, Slot.Shutter, new Color(.32f, .5f, .35f), wood, woodNormal);
            result[7] = Material(shader, Slot.ShutterBrown, new Color(.5f, .36f, .25f), wood, woodNormal);
            result[8] = Material(shader, Slot.Trim, new Color(.78f, .77f, .73f));
            result[9] = Material(shader, Slot.Balcony, new Color(.75f, .74f, .7f));
            result[10] = Material(shader, Slot.Rail, new Color(.15f, .15f, .16f), null, null, .4f, .6f);
            // Cutout bars retain depth/shadow rendering and avoid transparency sorting across an entire chunk.
            // Railing UVs are metres / 3; this tiny in-memory mask is persisted inside House_Rail.mat.
            ConfigureRail(result[10]);
            AssetDatabase.SaveAssets();
            return result;
        }

        static Material Material(Shader shader, Slot slot, Color tint, Texture2D diffuse = null,
            Texture2D normal = null, float smoothness = .25f, float metallic = 0)
        {
            string path = MaterialFolder + "/House_" + slot + ".mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool create = mat == null;
            if (create) mat = new Material(shader) { name = "House_" + slot };
            else mat.shader = shader;
            mat.enableInstancing = true;
            // Unity material colour properties are authored in sRGB; URP converts them in the linear shader.
            mat.SetColor("_BaseColor", tint);
            mat.SetTexture("_BaseMap", diffuse);
            mat.SetTextureScale("_BaseMap", Vector2.one);
            mat.SetTextureOffset("_BaseMap", Vector2.zero);
            mat.SetTexture("_BumpMap", normal);
            mat.SetFloat("_BumpScale", .7f);
            if (normal != null) mat.EnableKeyword("_NORMALMAP"); else mat.DisableKeyword("_NORMALMAP");
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Surface", 0);
            mat.SetFloat("_Blend", 0);
            mat.SetFloat("_AlphaClip", 0);
            mat.SetFloat("_Cull", (float)CullMode.Back);
            mat.SetFloat("_SrcBlend", (float)BlendMode.One);
            mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
            mat.SetFloat("_ZWrite", 1);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.SetOverrideTag("RenderType", "Opaque");
            mat.renderQueue = (int)RenderQueue.Geometry;
            if (create) AssetDatabase.CreateAsset(mat, path);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void ConfigureRail(Material mat)
        {
            string path = AssetDatabase.GetAssetPath(mat);
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
            mat.SetFloat("_AlphaClip", 1);
            mat.SetFloat("_Cutoff", .5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetOverrideTag("RenderType", "TransparentCutout");
            mat.renderQueue = (int)RenderQueue.AlphaTest;
            EditorUtility.SetDirty(mat);
        }

        static Texture2D Texture(string file, bool normal)
        {
            string path = TextureFolder + file;
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing house texture: " + path);
            bool changed = false;
            TextureImporterType type = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            if (importer.textureType != type) { importer.textureType = type; changed = true; }
            if (importer.sRGBTexture == normal) { importer.sRGBTexture = !normal; changed = true; }
            if (importer.wrapMode != TextureWrapMode.Repeat) { importer.wrapMode = TextureWrapMode.Repeat; changed = true; }
            if (changed) importer.SaveAndReimport();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) throw new InvalidOperationException("Cannot load house texture: " + path);
            return texture;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            if (slash < 0) throw new ArgumentException("Expected an Assets-relative folder.");
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }
    }
}
