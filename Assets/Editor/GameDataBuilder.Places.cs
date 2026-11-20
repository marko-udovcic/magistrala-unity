// Menu Magistrala/Igra: podaci, part 3 — place names for the location label (LocationLabel.cs) and road numbers.
//   source    BalkanDriver tools/world/dgu/calibration-neighbourhoods.geojson (read-only): the OSM boundaries of the seven Kaštel
//             settlements (admin_level 8, with their hinterland), Solin (pooled) and the Split neighbourhoods (all → "Split")
//   frame     lon / lat → game: the world pipeline's azimuthal equidistant projection centred on the Split riva
//             (tools/world/common.py AEQD, split-kastela is 1:1 so game x = east, game z = south) on a sphere of the local
//             Gaussian radius (≪ 1 m off the ellipsoidal one over these 15 km); game → Unity: W.U
//   grid      50 m cells over the whole graph (+ margin): inside a boundary → that place (Kaštel settlements first, then Split,
//             Solin last); else the nearest boundary within 250 m (piers, the shore); else Trogir / Split by distance to their
//             centres (Trogir isn't in the file, Split only partly); else none (the open bay). Note: OSM puts the Novi / Štafilić
//             line west of what the shop names suggest ("Urodica, Kaštel Štafilić" lies in Kaštel Novi): the boundary is kept
//   roads     RoadRef: OSM ref → the number shown with the street name: D roads as they are, 4-digit county roads Ž…, 5-digit
//             local roads (L…) left out
using System;
using System.Collections.Generic;
using System.IO;
using Magistrala.Game;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public partial class GameDataBuilder
    {
        const string PLACES = W.BD + "/tools/world/dgu/calibration-neighbourhoods.geojson";
        const float PlaceCell = 50, PlaceNear = 250;

        public static string RoadRef(string rf)
        {
            if (string.IsNullOrEmpty(rf)) return "";
            foreach (var r0 in rf.Split(';'))
            {
                var r = r0.Trim().Replace(" ", "");
                if (r.Length > 1 && (r[0] == 'D' || r[0] == 'A') && char.IsDigit(r[1])) return r;
            }
            foreach (var r0 in rf.Split(';'))
            {
                var r = r0.Trim();
                if (r.Length == 4 && int.TryParse(r, out _)) return "Ž" + r;
            }
            return "";
        }

        /// lon / lat → game (x east, z south): spherical azimuthal equidistant around tools/world/common.py SPLIT_LAT / SPLIT_LON
        static Vector2 LonLatToGame(double lon, double lat)
        {
            const double LAT0 = 43.5083, LON0 = 16.4400, A = 6378137.0, F = 1 / 298.257223563;
            double e2 = F * (2 - F), p0 = LAT0 * Math.PI / 180, s0 = Math.Sin(p0);
            double M = A * (1 - e2) / Math.Pow(1 - e2 * s0 * s0, 1.5), N = A / Math.Sqrt(1 - e2 * s0 * s0), R = Math.Sqrt(M * N);
            double p = lat * Math.PI / 180, dl = (lon - LON0) * Math.PI / 180;
            double cosc = s0 * Math.Sin(p) + Math.Cos(p0) * Math.Cos(p) * Math.Cos(dl);
            double c = Math.Acos(Math.Max(-1, Math.Min(1, cosc))), k = c < 1e-12 ? 1 : c / Math.Sin(c);
            double x = R * k * Math.Cos(p) * Math.Sin(dl), y = R * k * (Math.Cos(p0) * Math.Sin(p) - s0 * Math.Cos(p) * Math.Cos(dl));
            return new Vector2((float)x, (float)-y);
        }

        class Place { public string name; public int rank; public List<Vector2[]> rings = new List<Vector2[]>(); public Rect box; }

        static void BakePlaces(GameWorld asset)
        {
            if (!File.Exists(PLACES)) { Debug.LogWarning("[igra] places: " + PLACES + " missing, no place names"); return; }
            var places = new List<Place>();
            foreach (var fo in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(PLACES)))["features"]))
            {
                var f = MiniJson.O(fo); var props = MiniJson.O(f["properties"]); var geom = MiniJson.O(f["geometry"]);
                string n = (string)props["name"];
                var pl = new Place();
                if (n.StartsWith("Kaštel")) { pl.name = n; pl.rank = 0; }
                else if (n.StartsWith("Solin")) { pl.name = "Solin"; pl.rank = 2; }
                else { pl.name = "Split"; pl.rank = 1; } // (admin_level 9: Split's neighbourhoods)
                var polys = (string)geom["type"] == "MultiPolygon" ? MiniJson.L(geom["coordinates"]) : new List<object> { geom["coordinates"] };
                float x0 = float.MaxValue, z0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
                foreach (var po in polys)
                    foreach (var ro in MiniJson.L(po))
                    {
                        var ring = MiniJson.L(ro); var r = new Vector2[ring.Count];
                        for (int i = 0; i < ring.Count; i++)
                        {
                            var c = MiniJson.L(ring[i]); var g = LonLatToGame(MiniJson.D(c[0]), MiniJson.D(c[1]));
                            var u = W.U(g.x, 0, g.y); r[i] = new Vector2(u.x, u.z);
                            x0 = Mathf.Min(x0, u.x); x1 = Mathf.Max(x1, u.x); z0 = Mathf.Min(z0, u.z); z1 = Mathf.Max(z1, u.z);
                        }
                        pl.rings.Add(r);
                    }
                pl.box = Rect.MinMaxRect(x0, z0, x1, z1);
                places.Add(pl);
            }
            places.Sort((a, b) => a.rank.CompareTo(b.rank));
            var names = new List<string> { "" };
            foreach (var pl in places) if (!names.Contains(pl.name)) names.Add(pl.name);
            if (!names.Contains("Trogir")) names.Add("Trogir");
            // centres for what the boundaries miss: Trogir (old town), Split (riva); radius in m
            // (Split: only up to 1.5 km north of the riva, the bay towards Kaštela stays unnamed)
            var centres = new (string name, Vector2 u, float r, float north)[]
            {
                ("Trogir", Flat(LonLatToGame(16.2510, 43.5170)), 3500, 1e6f),
                ("Split", Flat(LonLatToGame(16.4400, 43.5083)), 4500, 1500),
            };

            // grid over the graph nodes + 1.5 km
            float gx0 = float.MaxValue, gz0 = float.MaxValue, gx1 = float.MinValue, gz1 = float.MinValue;
            foreach (var n in asset.nodes) { gx0 = Mathf.Min(gx0, n.x); gx1 = Mathf.Max(gx1, n.x); gz0 = Mathf.Min(gz0, n.z); gz1 = Mathf.Max(gz1, n.z); }
            gx0 = Mathf.Floor((gx0 - 1500) / PlaceCell) * PlaceCell; gz0 = Mathf.Floor((gz0 - 1500) / PlaceCell) * PlaceCell;
            int w = Mathf.CeilToInt((gx1 + 1500 - gx0) / PlaceCell), h = Mathf.CeilToInt((gz1 + 1500 - gz0) / PlaceCell);
            var grid = new byte[w * h]; var counts = new int[names.Count];
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    var q = new Vector2(gx0 + (i + .5f) * PlaceCell, gz0 + (j + .5f) * PlaceCell);
                    string hit = null;
                    foreach (var pl in places) if (pl.box.Contains(q) && Inside(pl, q)) { hit = pl.name; break; }
                    if (hit == null)
                    {
                        float bd = PlaceNear;
                        foreach (var pl in places)
                        {
                            var b = pl.box; if (q.x < b.xMin - bd || q.x > b.xMax + bd || q.y < b.yMin - bd || q.y > b.yMax + bd) continue;
                            float d = EdgeDist(pl, q, bd); if (d < bd) { bd = d; hit = pl.name; }
                        }
                    }
                    if (hit == null)
                    {
                        float bd = float.MaxValue;
                        foreach (var c in centres) { float d = Vector2.Distance(q, c.u); if (d < c.r && q.y - c.u.y < c.north && d < bd) { bd = d; hit = c.name; } }
                    }
                    byte k = (byte)(hit == null ? 0 : names.IndexOf(hit));
                    grid[j * w + i] = k; counts[k]++;
                }
            asset.placeNames = names.ToArray(); asset.placeGrid = grid; asset.placeOrigin = new Vector2(gx0, gz0);
            asset.placeCell = PlaceCell; asset.placeW = w; asset.placeH = h;
            var sb = new System.Text.StringBuilder($"[igra] places: {w}×{h} cells of {PlaceCell} m from {places.Count} boundaries:");
            for (int k = 0; k < names.Count; k++) sb.Append($" {(k == 0 ? "(none)" : names[k])} {counts[k]}");
            Debug.Log(sb.ToString());
        }

        static Vector2 Flat(Vector2 game) { var u = W.U(game.x, 0, game.y); return new Vector2(u.x, u.z); }

        /// even-odd over every ring of the feature (outer rings and holes)
        static bool Inside(Place pl, Vector2 q)
        {
            bool inside = false;
            foreach (var r in pl.rings)
                for (int i = 0, k = r.Length - 1; i < r.Length; k = i++)
                    if ((r[i].y > q.y) != (r[k].y > q.y) && q.x < (r[k].x - r[i].x) * (q.y - r[i].y) / (r[k].y - r[i].y) + r[i].x) inside = !inside;
            return inside;
        }

        static float EdgeDist(Place pl, Vector2 q, float max)
        {
            float best = max;
            foreach (var r in pl.rings)
                for (int i = 0, k = r.Length - 1; i < r.Length; k = i++)
                {
                    Vector2 a = r[k], d = r[i] - a; float L = d.sqrMagnitude;
                    float t = L < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(q - a, d) / L);
                    best = Mathf.Min(best, Vector2.Distance(q, a + d * t));
                }
            return best;
        }
    }
}
