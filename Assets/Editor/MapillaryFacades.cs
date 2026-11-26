// Facade colours measured from Mapillary street-level photos (CC BY-SA 4.0, Mapillary contributors) along the old road, the D8 and
// (26. 11.) all the other streets of Kaštela: BalkanDriver tools/world/mapillary/s3_facades.py -> fasade-all.json (a superset of
// fasade.json: same ids, conf never lower), copied here as fasade-mapillary.json by Tools/mapillary/spoji.py (facts
// only: colour, class, type, shutters; docs/mapillary-izvlacenje.md in the BalkanDriver repo). Key 'tx_tz:i' = tile + index in its
// BUILDINGS section, as OrthoRoofs.Get; the centroid must agree within 1.5 m. A record with conf >= MinConf sets the wall's colour
// (rgb: linear albedo), `type` stone -> the stone house family, concrete -> a grey render variant, the shutters' colour; below it
// the house keeps its procedural look. Editor-only input of BuildingsFromTiles / HouseBuilder.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class MapillaryFacades
    {
        public const string PATH = "Assets/Editor/fasade-mapillary.json";
        public const float MinConf = .3f;
        /// false: procedural facades only (A/B switch)
        public static bool Enabled = true;
        public static int nApplied;

        public sealed class Rec { public Color lin; public string type, cls, shutters; public float conf, cx, cz; }
        static Dictionary<string, Rec> recs;

        static void Load()
        {
            if (recs != null) return;
            recs = new Dictionary<string, Rec>();
            if (!File.Exists(PATH)) { Debug.LogWarning("[Magistrala] no " + PATH + ": procedural facades"); return; }
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(PATH)));
            foreach (var kv in MiniJson.O(root["b"]))
            {
                var o = MiniJson.O(kv.Value); var c = MiniJson.L(o["rgb"]);
                recs[kv.Key] = new Rec
                {
                    lin = new Color(MiniJson.F(c[0]), MiniJson.F(c[1]), MiniJson.F(c[2]), 1), type = o["type"] as string, cls = o["class"] as string,
                    shutters = MiniJson.G(o, "shutters") as string, conf = MiniJson.F(o["conf"]), cx = MiniJson.F(o["cx"]), cz = MiniJson.F(o["cz"]),
                };
            }
        }
        public static void Reset() { recs = null; nApplied = 0; }

        /// the usable record of a tile footprint (null: none, below MinConf, or the tiles changed: the centroid must agree)
        public static Rec Get(int tx, int tz, int i, double cx, double cz)
        {
            if (!Enabled) return null;
            Load();
            if (!recs.TryGetValue($"{tx}_{tz}:{i}", out var r) || r.conf < MinConf) return null;
            return Math.Abs(r.cx - cx) < 1.5 && Math.Abs(r.cz - cz) < 1.5 ? r : null;
        }

        static Color H(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

        /// the measured facade on a picked look: wall tint (the plaster layers are grey .71 linear: tint = albedo / .71), stone /
        /// concrete type, shutters; the wall keeps a little of the house's own drift so a row of measured houses is not flat
        public static void Apply(ref Look k, Rec r, bool industrial, bool count = true)
        {
            if (r == null) return;
            if (count) nApplied++;
            if (r.type == "stone" && !industrial)
            {
                k.family = 0; k.derelict = false; k.split = 0; k.unfinished = false; k.bare = 0;
                k.wallCode = BuildingTextures.Stone * 9; k.trimLayer = BuildingTextures.Stone; k.trim = Color.white;
                // the stone layer is untinted: the measured lightness as its darkening / greying
                float l = .3f * r.lin.r + .59f * r.lin.g + .11f * r.lin.b; // linear
                k.stoneDark = Mathf.Clamp01(1 - l / .5f) * .6f; k.stoneGrey = Mathf.Clamp01(1 - (r.lin.r - r.lin.b) * 6);
            }
            else
            {
                // the measured chroma is noisy (white balance from the asphalt, shade, sky light): a blue cast is almost always that
                // (Kaštela has next to no blue houses), so the chroma is halved, a blue one cut to a fifth; the lightness is kept
                var lin = r.lin; float L = .3f * lin.r + .59f * lin.g + .11f * lin.b;
                float keep = lin.b > lin.r ? .2f : .5f;
                lin = new Color(L + (lin.r - L) * keep, L + (lin.g - L) * keep, L + (lin.b - L) * keep);
                var t = new Color(Mathf.Clamp01(lin.r / .71f), Mathf.Clamp01(lin.g / .71f), Mathf.Clamp01(lin.b / .71f)).gamma;
                k.wall = Color.Lerp(t, k.wall, .08f); k.derelict = false;
                if (k.family <= 1) k.trim = Color.white; else k.trim = Color.Lerp(k.wall, new Color(.87f, .85f, .81f), .55f);
                if (r.type == "concrete" && !industrial) k.concrete = true;
            }
            if (r.shutters == "green") { k.shutter = Look.Desat(H("3F5B45"), .2f); k.leaves = true; }
            else if (r.shutters == "brown") { k.shutter = Look.Desat(H("5E4535"), .2f); k.leaves = true; }
            else if (r.shutters == "none" && k.family >= 2) { k.leaves = false; k.rollers = true; }
            k.measured = true;
        }
    }
}
