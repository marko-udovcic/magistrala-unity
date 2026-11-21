// The orthophoto audit's per-building table (docs/audit-zgrade.md; made by Tools/zgrade/export.py from the DGU DOF LiDAR
// 2022/23 orthophoto, BalkanDriver/tools/world/dgu/cache/dof/mosaic-lidar): per tile footprint (tile, index in its
// BUILDINGS section) the roof seen from the air (pitched tiles / flat light / flat dark / metal), its colour already mapped
// to the game's palette, solar panels, and a shift for the OSM footprints of the zones where they sit consistently off the
// roofs; plus the houses the photo shows and neither OSM nor the cadastre has (simple rectangles). Editor-only input of
// BuildingsFromTiles (Assets/Editor: not in the player).
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class OrthoRoofs
    {
        public const string PATH = "Assets/Editor/zgrade-ortofoto.json";
        /// false: the procedural roofs / colours as before (A/B switch)
        public static bool Enabled = true;

        public sealed class Rec
        {
            public int cls, decide; // cls: 0 unknown, 1 pitched, 2 flat light, 3 flat dark, 4 metal, 5 mixed; decide: 0 procedural, 1 pitched, 2 flat
            public bool hasRoof, hasFlat; public Color roof, flat; // sRGB albedo
            public float solar, sdx, sdz, shx, shz, cx, cz; // m², solar spot from the centroid (game m), shift (game m), centroid
        }
        public sealed class Added { public List<BuildingsFromTiles.Vector2d> P; public double h; public Color roof; }

        static Dictionary<long, Rec> recs; static List<Added> added;
        static long Key(int tx, int tz, int i) => ((long)(tx + 512) << 40) | ((long)(tz + 512) << 24) | (uint)i;

        static void Load()
        {
            if (recs != null) return;
            recs = new Dictionary<long, Rec>(); added = new List<Added>();
            if (!File.Exists(PATH)) { Debug.LogWarning("[Magistrala] no " + PATH + ": procedural roofs"); return; }
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(PATH)));
            foreach (var kv in MiniJson.O(root["b"]))
            {
                var t = kv.Key.Split('_'); int tx = int.Parse(t[0]), tz = int.Parse(t[1]);
                foreach (var eo in MiniJson.L(kv.Value))
                {
                    var e = MiniJson.L(eo);
                    var r = new Rec { cls = (int)MiniJson.D(e[1]), decide = (int)MiniJson.D(e[2]), solar = MiniJson.F(e[5]), sdx = MiniJson.F(e[6]), sdz = MiniJson.F(e[7]),
                        shx = MiniJson.F(e[8]), shz = MiniJson.F(e[9]), cx = MiniJson.F(e[10]), cz = MiniJson.F(e[11]) };
                    r.hasRoof = Hex((string)e[3], out r.roof); r.hasFlat = Hex((string)e[4], out r.flat);
                    recs[Key(tx, tz, (int)MiniJson.D(e[0]))] = r;
                }
            }
            foreach (var ao in MiniJson.L(root["add"]))
            {
                var a = MiniJson.O(ao); var P = new List<BuildingsFromTiles.Vector2d>();
                foreach (var p in MiniJson.L(a["pts"])) { var q = MiniJson.L(p); P.Add(new BuildingsFromTiles.Vector2d(MiniJson.D(q[0]), MiniJson.D(q[1]))); }
                Hex((string)a["roof"], out var rc);
                added.Add(new Added { P = P, h = MiniJson.D(a["h"]), roof = rc });
            }
        }
        static bool Hex(string h, out Color c) { c = default; return !string.IsNullOrEmpty(h) && ColorUtility.TryParseHtmlString("#" + h, out c); }

        /// the record of a tile footprint (null: none, or the tiles changed since the table was made: the centroid must agree)
        public static Rec Get(int tx, int tz, int i, double cx, double cz)
        {
            if (!Enabled) return null;
            Load();
            if (!recs.TryGetValue(Key(tx, tz, i), out var r)) return null;
            return Math.Abs(r.cx - cx) < 1.5 && Math.Abs(r.cz - cz) < 1.5 ? r : null;
        }
        public static List<Added> Missing() { if (!Enabled) return new List<Added>(); Load(); return added; }
        public static void Reset() { recs = null; added = null; }

        // ---- the game's roof palette (export.py roof_colour): faded salmon .. vivid new tiles, brightness and hue drift
        /// sRGB albedo of a tiled roof for draws tl (lightness), tc (vividness), th (hue: low redder, high more orange-brown)
        public static Color RoofPalette(float tl, float tc, float th)
        {
            Vector3 F = new Vector3(150, 108, 92), V = new Vector3(152, 78, 52);
            Vector3 c = F + (V - F) * tc;
            c = Vector3.Scale(c, new Vector3(1, .94f + .12f * th, .97f + .06f * (1 - th))) * (.8f + .36f * tl);
            c = new Vector3(Mathf.Clamp(c.x, 70, 172), Mathf.Clamp(c.y, 40, 128), Mathf.Clamp(c.z, 28, 110));
            return new Color(c.x / 255, c.y / 255, c.z / 255);
        }
        /// flat roofs without a measurement: light membrane / concrete greys, some darker
        public static Color FlatPalette(double r, double t)
        {
            float g = r < .62 ? 132 + 38 * (float)t : r < .92 ? 112 + 24 * (float)t : 82 + 18 * (float)t;
            float w = -3 + 9 * (float)Math.Abs(Math.Sin(t * 91.7)); // a little warm (beige gravel / tiles) or cool
            return new Color((g + w) / 255, (g + w * .3f) / 255, (g - w) / 255);
        }
    }
}
