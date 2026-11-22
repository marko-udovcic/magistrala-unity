// Storeys per building (docs/visine-zgrada.md). Order, highest first:
//   1. the owner's corrections  Assets/Editor/katovi-ispravci.json  (id -> storeys; made in the player with F7, merged here by
//      the menu "Magistrala/Zgrade: uvezi ispravke katova")
//   2. OSM building:levels / height (hSrc osm-levels / osm-height in the tiles): human-tagged, kept
//   3. the shadow estimate from the DGU orthophoto  Assets/Editor/visine-zgrada.json  (Tools/visine), only where its
//      confidence >= MIN_CONF (both shadow estimators agree)
//   4. the tile's h as before (old game height / default)
// A storey count becomes the eave height FLOOR × n − 0.4 (HouseBuilder: storeys = ceil(h / 3), the ±0.35 m jitter stays
// inside the same count). Also writes the player's lookup Assets/Generated/Game/Resources/katovi-index.json (git-ignored, rebuilt) (footprint, base, height,
// storeys, source per building) for the F7 tool (Magistrala.KatoviTool).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class BuildingFloors
    {
        public const string MEASURED = "Assets/Editor/visine-zgrada.json";
        public const string OVERRIDES = "Assets/Editor/katovi-ispravci.json";
        public const string INDEX = "Assets/Generated/Game/Resources/katovi-index.json";
        public const string PLAYER_FILE = "katovi-ispravci.json";
        public const float MIN_CONF = .6f;
        /// false: the tiles' heights as before (A/B switch); the owner's corrections still apply
        public static bool UseMeasured = true;
        const double FLOOR = 3.0;

        static Dictionary<string, (int n, float conf, string src)> measured;
        static Dictionary<string, int> overrides;
        static readonly StringBuilder index = new StringBuilder();
        static int nIndex;
        public static int nOverride, nOsm, nMeasured, nKept;

        public static void Begin()
        {
            measured = new Dictionary<string, (int, float, string)>(); overrides = new Dictionary<string, int>();
            index.Clear(); nIndex = 0; nOverride = nOsm = nMeasured = nKept = 0;
            if (File.Exists(MEASURED))
            {
                var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(MEASURED)));
                foreach (var kv in MiniJson.O(root["b"]))
                {
                    var e = MiniJson.L(kv.Value);
                    measured[kv.Key] = ((int)MiniJson.D(e[0]), MiniJson.F(e[1]), (string)e[2]);
                }
            }
            else Debug.LogWarning("[Magistrala] no " + MEASURED + ": tile heights");
            overrides = ReadOverrides(OVERRIDES);
        }

        /// the eave height of building `id` (tile `b`, tile h `h`); src: what decided it
        /// true when the shadow measurement (visine-zgrada.json) decides this building's height: real data, not to be capped
        public static bool IsMeasured(string id) => UseMeasured && id != null && measured != null && measured.TryGetValue(id, out var m) && m.conf >= MIN_CONF;

        public static double Height(string id, Dictionary<string, object> b, double h, out string src, out int storeys)
        {
            src = (string)MiniJson.G(b, "hSrc") ?? "osm";
            if (id != null && overrides != null && overrides.TryGetValue(id, out var o)) { nOverride++; src = "ispravak"; storeys = o; return FLOOR * o - .4; }
            if (src == "osm-levels" || src == "osm-height") { nOsm++; storeys = Math.Max(1, (int)Math.Ceiling(h / FLOOR - .05)); return h; }
            if (UseMeasured && id != null && measured != null && measured.TryGetValue(id, out var m) && m.conf >= MIN_CONF)
            { nMeasured++; src = m.src; storeys = m.n; return FLOOR * m.n - .4; }
            nKept++; storeys = Math.Max(1, (int)Math.Ceiling(h / FLOOR - .05)); return h;
        }

        /// one building for the player's F7 lookup (game coords; top = eave)
        public static void Index(string id, List<BuildingsFromTiles.Vector2d> P, double baseY, double h, int storeys, string src)
        {
            if (id == null) return;
            var c = CultureInfo.InvariantCulture;
            if (nIndex > 0) index.Append(",\n");
            index.Append('[').Append('"').Append(id).Append("\",").Append(storeys).Append(",\"").Append(src).Append("\",")
                .Append(baseY.ToString("0.0", c)).Append(',').Append(h.ToString("0.0", c)).Append(",[");
            for (int i = 0; i < P.Count; i++) { if (i > 0) index.Append(','); index.Append(P[i].x.ToString("0.0", c)).Append(',').Append(P[i].z.ToString("0.0", c)); }
            index.Append("]]"); nIndex++;
        }

        public static void End()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(INDEX));
            File.WriteAllText(INDEX, "{\"v\":1,\"note\":\"BuildingFloors.cs: [id, katovi, izvor, baseY, h, [x,z,...]] u koordinatama igre\",\"b\":[\n" + index + "\n]}\n");
            AssetDatabase.ImportAsset(INDEX);
            Debug.Log($"[Magistrala] storeys: owner's corrections {nOverride}, OSM {nOsm}, shadow estimate {nMeasured} (conf >= {MIN_CONF}), tile height kept {nKept}; index {nIndex} -> {INDEX}");
        }

        // ---- the owner's corrections
        static Dictionary<string, int> ReadOverrides(string path)
        {
            var d = new Dictionary<string, int>();
            if (!File.Exists(path)) return d;
            try
            {
                var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(path)));
                var b = MiniJson.O(MiniJson.G(root, "b")) ?? root;
                foreach (var kv in b) if (kv.Value is double || kv.Value is long || kv.Value is int) { int n = (int)MiniJson.D(kv.Value); if (n >= 1 && n <= 40) d[kv.Key] = n; }
            }
            catch (Exception e) { Debug.LogError("[Magistrala] " + path + ": " + e.Message); }
            return d;
        }
        static void WriteOverrides(string path, Dictionary<string, int> d)
        {
            var keys = new List<string>(d.Keys); keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder("{\"v\":1,\"note\":\"ispravci katova (vlasnik, F7 u igri; docs/visine-zgrada.md): id zgrade -> broj katova (s prizemljem)\",\"b\":{\n");
            for (int i = 0; i < keys.Count; i++) sb.Append("\"").Append(keys[i]).Append("\":").Append(d[keys[i]]).Append(i < keys.Count - 1 ? ",\n" : "\n");
            sb.Append("}}\n");
            File.WriteAllText(path, sb.ToString());
        }

        /// the player's corrections files (persistentDataPath of any Magistrala build on this Mac), newest last
        public static List<string> PlayerFiles()
        {
            var l = new List<string>();
            string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            foreach (var dir in new[] { Path.Combine(home, "Library/Application Support/Magistrala"), Path.Combine(home, "Library/Application Support/DefaultCompany") })
                if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, PLAYER_FILE, SearchOption.AllDirectories)) l.Add(f);
            l.Sort((a, b) => File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
            return l;
        }

        [MenuItem("Magistrala/Zgrade: uvezi ispravke katova")]
        public static void ImportMenu() => Import(null);

        /// merges the player's corrections (or `file`) into Assets/Editor/katovi-ispravci.json; returns a report
        public static string Import(string file)
        {
            var files = file != null ? new List<string> { file } : PlayerFiles();
            var d = ReadOverrides(OVERRIDES); int added = 0, changed = 0;
            foreach (var f in files)
                foreach (var kv in ReadOverrides(f))
                {
                    if (!d.TryGetValue(kv.Key, out var old)) added++; else if (old != kv.Value) changed++; else continue;
                    d[kv.Key] = kv.Value;
                }
            WriteOverrides(OVERRIDES, d); AssetDatabase.ImportAsset(OVERRIDES);
            string msg = $"[Magistrala] ispravci katova: {files.Count} datoteka ({string.Join(", ", files)}), novih {added}, promijenjenih {changed}, ukupno {d.Count} -> {OVERRIDES}. Zatim: Build tile buildings.";
            Debug.Log(msg); return msg;
        }
    }
}
