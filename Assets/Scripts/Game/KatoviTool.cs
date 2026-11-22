// F7 "Katovi": the owner's storey corrections in the player (docs/visine-zgrada.md). The building under the screen centre is
// outlined with its id, storeys and where they came from; PageUp / PageDown change its storeys (the outline shows the new
// height), Delete drops the correction, Enter saves every correction of this session to persistentDataPath/katovi-ispravci.json
// (or -katovi <file>) and logs it. In the editor, "Magistrala/Zgrade: uvezi ispravke katova" merges that file into
// Assets/Editor/katovi-ispravci.json, and the next Build tile buildings uses it before anything else.
// The lookup Resources katovi-index.json (Assets/Generated/Game/Resources) comes from the editor's BuildingFloors (Build tile buildings).
// -katovion: the mode on from the start (screenshots); -katovidelta N: N storeys added to the first building selected.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Magistrala
{
    public class KatoviTool : MonoBehaviour
    {
        const float OX = -4200, OZ = -5300, FLOOR = 3f;
        public static bool On { get; private set; }

        sealed class Bld { public string id, src; public int n; public float baseY, h; public Vector2[] P; public Vector2 min, max; }
        List<Bld> all; Dictionary<long, List<int>> grid;
        readonly Dictionary<string, int> saved = new Dictionary<string, int>(), edits = new Dictionary<string, int>();
        Bld sel; string status = ""; float statusT; string file; int demoDelta; bool demoDone;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            if (FindFirstObjectByType<KatoviTool>()) return;
            var go = new GameObject("KatoviTool"); DontDestroyOnLoad(go); go.AddComponent<KatoviTool>();
        }

        static string Arg(string name)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) return a[i + 1];
            return null;
        }

        void Start()
        {
            file = Arg("-katovi") ?? Path.Combine(Application.persistentDataPath, "katovi-ispravci.json");
            ReadSaved();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-katovion") >= 0) SetOn(true);
            if (int.TryParse(Arg("-katovidelta") ?? "", out var d)) demoDelta = d;
        }

        void SetOn(bool on)
        {
            On = on;
            if (on && all == null) Load();
            Say(on ? (all != null && all.Count > 0 ? $"Katovi: {all.Count} zgrada. PgUp / PgDn katovi, Enter spremi, Del poništi, F7 kraj" : "Katovi: nema katovi-index (Build tile buildings)") : "");
        }

        void Say(string s) { status = s; statusT = Time.unscaledTime; if (s.Length > 0) Debug.Log("[katovi] " + s); }

        // ---- the index: one building per line: ["id",n,"src",baseY,h,[x,z,...]]
        void Load()
        {
            all = new List<Bld>(); grid = new Dictionary<long, List<int>>();
            var ta = Resources.Load<TextAsset>("katovi-index");
            if (!ta) return;
            var c = CultureInfo.InvariantCulture;
            foreach (var raw in ta.text.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("[\"")) continue;
                try
                {
                    int q = line.IndexOf('"', 2); string id = line.Substring(2, q - 2);
                    int pb = line.IndexOf('[', q); int pe = line.IndexOf(']', pb);
                    var head = line.Substring(q + 2, pb - q - 2).Split(',');
                    var pts = line.Substring(pb + 1, pe - pb - 1).Split(',');
                    var b = new Bld { id = id, n = int.Parse(head[0], c), src = head[1].Trim('"'), baseY = float.Parse(head[2], c), h = float.Parse(head[3], c), P = new Vector2[pts.Length / 2] };
                    b.min = new Vector2(1e9f, 1e9f); b.max = -b.min;
                    for (int i = 0; i < b.P.Length; i++) { b.P[i] = new Vector2(float.Parse(pts[2 * i], c), float.Parse(pts[2 * i + 1], c)); b.min = Vector2.Min(b.min, b.P[i]); b.max = Vector2.Max(b.max, b.P[i]); }
                    int k = all.Count; all.Add(b);
                    for (int gx = Cell(b.min.x); gx <= Cell(b.max.x); gx++) for (int gz = Cell(b.min.y); gz <= Cell(b.max.y); gz++)
                        { long key = Key(gx, gz); if (!grid.TryGetValue(key, out var l)) grid[key] = l = new List<int>(); l.Add(k); }
                }
                catch (Exception e) { Debug.LogWarning("[katovi] redak indeksa: " + e.Message); }
            }
        }
        static int Cell(float v) => Mathf.FloorToInt(v / 32f);
        static long Key(int gx, int gz) => ((long)gx << 32) ^ (uint)gz;

        /// the building at game (x, z): inside a footprint, else the nearest wall within 2.5 m
        Bld Find(Vector2 g)
        {
            Bld best = null; float bd = 2.5f;
            for (int gx = Cell(g.x - 3); gx <= Cell(g.x + 3); gx++) for (int gz = Cell(g.y - 3); gz <= Cell(g.y + 3); gz++)
                {
                    if (!grid.TryGetValue(Key(gx, gz), out var l)) continue;
                    foreach (var i in l)
                    {
                        var b = all[i];
                        if (Inside(b.P, g)) return b;
                        float d = EdgeDist(b.P, g); if (d < bd) { bd = d; best = b; }
                    }
                }
            return best;
        }
        static bool Inside(Vector2[] P, Vector2 p)
        {
            bool c = false;
            for (int i = 0, j = P.Length - 1; i < P.Length; j = i++)
                if ((P[i].y > p.y) != (P[j].y > p.y) && p.x < (P[j].x - P[i].x) * (p.y - P[i].y) / (P[j].y - P[i].y) + P[i].x) c = !c;
            return c;
        }
        static float EdgeDist(Vector2[] P, Vector2 p)
        {
            float d = 1e9f;
            for (int i = 0; i < P.Length; i++)
            {
                Vector2 a = P[i], b = P[(i + 1) % P.Length], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                d = Mathf.Min(d, Vector2.Distance(p, a + ab * t));
            }
            return d;
        }

        int Storeys(Bld b) => edits.TryGetValue(b.id, out var e) ? e : saved.TryGetValue(b.id, out var s) ? s : b.n;

        void Update()
        {
            var kb = Keyboard.current; if (kb == null) return;
            if (kb.f7Key.wasPressedThisFrame) SetOn(!On);
            if (!On || all == null) return;
            var cam = Camera.main; sel = null;
            if (cam)
            {
                var ray = cam.ViewportPointToRay(new Vector3(.5f, .5f, 0));
                if (Physics.Raycast(ray, out var hit, 1500f)) sel = Find(new Vector2(hit.point.x + OX, -hit.point.z + OZ));
                else if (ray.direction.y < -.01f)
                {
                    // no collider (far chunks): where the ray meets the ground at the camera's height minus its altitude
                    float t = (ray.origin.y - 0) / -ray.direction.y; var p = ray.origin + ray.direction * Mathf.Min(t, 1500f);
                    sel = Find(new Vector2(p.x + OX, -p.z + OZ));
                }
            }
            if (sel != null && demoDelta != 0 && !demoDone) { edits[sel.id] = Mathf.Clamp(Storeys(sel) + demoDelta, 1, 40); demoDone = true; }
            if (sel != null)
            {
                int n = Storeys(sel);
                if (kb.pageUpKey.wasPressedThisFrame) { edits[sel.id] = Mathf.Min(40, n + 1); Say($"{sel.id}: {n + 1} kat. (nije spremljeno, Enter)"); }
                if (kb.pageDownKey.wasPressedThisFrame) { edits[sel.id] = Mathf.Max(1, n - 1); Say($"{sel.id}: {Mathf.Max(1, n - 1)} kat. (nije spremljeno, Enter)"); }
                if (kb.deleteKey.wasPressedThisFrame) { edits.Remove(sel.id); saved.Remove(sel.id); Save(); Say($"{sel.id}: ispravak uklonjen ({sel.n} kat., {sel.src})"); }
            }
            if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) Save();
        }

        void ReadSaved()
        {
            saved.Clear();
            if (!File.Exists(file)) return;
            try
            {
                // {"v":1,"b":{"id":n,...}}: one "id":n per line
                foreach (var raw in File.ReadAllLines(file))
                {
                    var line = raw.Trim().TrimEnd(',');
                    if (!line.StartsWith("\"") || line.StartsWith("\"v\"") || line.StartsWith("\"note\"") || line.StartsWith("\"b\"")) continue;
                    int q = line.IndexOf('"', 1); var id = line.Substring(1, q - 1);
                    var v = line.Substring(line.IndexOf(':', q) + 1).Trim().TrimEnd('}');
                    if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) saved[id] = n;
                }
            }
            catch (Exception e) { Debug.LogWarning("[katovi] " + file + ": " + e.Message); }
        }

        void Save()
        {
            ReadSaved();
            foreach (var kv in edits) { saved[kv.Key] = kv.Value; Debug.Log($"[katovi] ispravak {kv.Key} -> {kv.Value}"); }
            int nNew = edits.Count; edits.Clear();
            var keys = new List<string>(saved.Keys); keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder("{\"v\":1,\"note\":\"ispravci katova iz igre (F7); u editoru: Magistrala/Zgrade: uvezi ispravke katova\",\"b\":{\n");
            for (int i = 0; i < keys.Count; i++) sb.Append('"').Append(keys[i]).Append("\":").Append(saved[keys[i]]).Append(i < keys.Count - 1 ? ",\n" : "\n");
            sb.Append("}}\n");
            try { Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file, sb.ToString()); Say($"spremljeno {nNew} (ukupno {saved.Count}) -> {file}"); }
            catch (Exception e) { Say("nije spremljeno: " + e.Message); }
        }

        // ---- drawing (IMGUI: lines from rotated quads, no material needed)
        static void Line(Vector2 a, Vector2 b, Color c, float w)
        {
            var d = b - a; float len = d.magnitude; if (len < .5f) return;
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg, a);
            GUI.DrawTexture(new Rect(a.x, a.y - w / 2, len, w), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0, HudStyle.Lin(c), 0, 0);
            GUI.matrix = m;
        }
        static bool Screen2(Camera cam, Vector3 w, out Vector2 s)
        {
            var v = cam.WorldToScreenPoint(w); s = new Vector2(v.x, Screen.height - v.y); return v.z > .1f;
        }

        void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            bool recent = status.Length > 0 && Time.unscaledTime - statusT < 6;
            if (!On && !recent) return;
            float K = HudStyle.K;
            var cam = Camera.main;
            // the crosshair
            if (On)
            {
                var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
                Line(c + new Vector2(-12 * K, 0), c + new Vector2(12 * K, 0), Color.white, 2 * K); Line(c + new Vector2(0, -12 * K), c + new Vector2(0, 12 * K), Color.white, 2 * K);
            }
            string info = null;
            if (On && sel != null && cam)
            {
                int n = Storeys(sel); bool edited = edits.ContainsKey(sel.id), corr = saved.ContainsKey(sel.id);
                float top = sel.baseY + (edited || corr ? FLOOR * n - .4f : sel.h);
                var col = edited ? HudStyle.Highlight : corr ? HudStyle.Good : HudStyle.Accent;
                int N = sel.P.Length; var lo = new Vector2[N]; var hi = new Vector2[N]; var okL = new bool[N]; var okH = new bool[N];
                for (int i = 0; i < N; i++)
                {
                    var g = sel.P[i]; var u = new Vector3(g.x - OX, 0, -(g.y - OZ));
                    okL[i] = Screen2(cam, new Vector3(u.x, sel.baseY, u.z), out lo[i]); okH[i] = Screen2(cam, new Vector3(u.x, top + .3f, u.z), out hi[i]);
                }
                for (int i = 0; i < N; i++)
                {
                    int j = (i + 1) % N;
                    if (okL[i] && okL[j]) Line(lo[i], lo[j], col, 2 * K);
                    if (okH[i] && okH[j]) Line(hi[i], hi[j], col, 3 * K);
                    if (okL[i] && okH[i]) Line(lo[i], hi[i], col, 1.5f * K);
                    // storey lines
                    for (int f = 1; f < n && f < 40; f++)
                    {
                        float y = sel.baseY + f * FLOOR;
                        var gi = sel.P[i]; var gj = sel.P[j];
                        if (Screen2(cam, new Vector3(gi.x - OX, y, -(gi.y - OZ)), out var a) && Screen2(cam, new Vector3(gj.x - OX, y, -(gj.y - OZ)), out var b)) Line(a, b, new Color(col.r, col.g, col.b, .55f), 1 * K);
                    }
                }
                string what = edited ? "promijenjeno, Enter = spremi" : corr ? "tvoj ispravak" : sel.src == "osm-levels" || sel.src == "osm-height" ? "OSM" : sel.src.StartsWith("sjena") ? "procjena iz sjene (ortofoto)" : sel.src == "ispravak" ? "ispravak (u igri)" : "zadano / stara igra";
                info = $"{sel.id}   {n} {(n == 1 ? "kat" : "kata")} (s prizemljem)   {what}";
            }
            else if (On) info = all == null || all.Count == 0 ? "nema indeksa zgrada" : "nišani zgradu";
            // the panel: top centre
            var st = HudStyle.St(20, true, Color.white, TextAnchor.MiddleCenter);
            var st2 = HudStyle.St(15, false, HudStyle.Muted, TextAnchor.MiddleCenter);
            string help = "F7 Katovi   PgUp / PgDn  ±1 kat   Enter spremi   Del poništi";
            float w = Mathf.Max(HudStyle.Width(st, info ?? ""), HudStyle.Width(st2, recent ? status : help)) + 48 * K;
            // below the drone help (two lines at the top centre)
            var r = new Rect((Screen.width - w) / 2, 104 * K, w, (On ? 72 : 40) * K);
            HudStyle.PanelBg(r);
            if (On) HudStyle.Label(new Rect(r.x, r.y + 8 * K, r.width, 30 * K), info ?? "", st);
            HudStyle.Label(new Rect(r.x, r.y + (On ? 40 : 6) * K, r.width, 26 * K), recent ? status : help, st2);
        }
    }
}
