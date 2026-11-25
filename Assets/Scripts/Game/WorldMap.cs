// The full-screen map (M): Kaštela from above in the HUD's style – blue sea, warm land, darker hills and greenery, light building
// footprints, white roads (the D8 wider and warm), place and street names – with the truck, the warehouse, every delivery stop,
// the pumps, the running job's stops, the GPS route, the owner's waypoint and the police the phone's group reported in the last
// 1½ h (Phone/Blicaj.cs KnownPolice: red-orange beacon markers, the post on hover). The game pauses while it is open (like the
// phone): timeScale 0 stops the truck's physics, so no driving input acts. M opens it, M (or Esc) closes it – nothing in between;
// the phone's Karta app opens it too.
//   keys    M / Esc close · WASD / arrows move the view (accelerating; the cursor goes back to the centre) · mouse: the cursor
//           follows it, drag pans · wheel / + − zoom (anchored at the cursor) · Enter / left click: a waypoint at the cursor
//           (snapped to the nearest road; Enter / click on the waypoint removes it). The GPS and the minimap then lead there
//           (MagistralaGame.SetWaypoint → RoadRouter.RouteTo; the HUD says "Oznaka"); reached within 25 m it clears. Tab: the style.
//   look    two styles (owner 2026-11-21): flat printed-map (default, DefaultDark) and dark, Tab switches (remembered);
//           the ground textures and the building mesh are baked (KartaData, Magistrala/Karta: ispeci); roads come live from the
//           game graph (simplified twice: fine below 1.2 m a pixel, coarse above), widths in pixels with a world-width floor.
//   drawing one screen-sized RenderTexture (half float, MSAA 4×, as GpsMap's): the ground texture, the
//           buildings (light palette: a 1 px shadow pass first), road casings and fills, the route and the marker discs,
//           redrawn only when something changed (view, route, waypoint, legs, 1 s); OnGUI shows it, then the text, glyphs, cursor, tooltip, legend and key hints. Nothing runs while it is closed
//           (Update only checks M); the open map culls the 3D camera (cullingMask 0) and frees its target on close.
// Test switches: -karta [m/px]  open at start (metres per 1080 p pixel, default 1.6) · -kartacentar gx,gz  centre of the view
//   (game x, z; default the truck) · -kartastil tamna|svijetla  the style · (MagistralaGame) -kartaoznaka gx,gz  a waypoint at start
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Magistrala.Game
{
    public class WorldMap : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }
        static int escFrame = -1;
        /// Esc belongs to the map this frame (open, or it just closed it): ProbeBench must not quit the player on it
        public static bool EscTaken => IsOpen || escFrame == Time.frameCount;

        const float ZoomMin = 0.15f, ZoomMax = 7f, ZoomDefault = 1.6f, StreetZoom = 1.15f, StopNamesZoom = 0.55f, FineBelow = 1.2f;
        MagistralaGame g; KartaData data; GameWorld w;
        bool init;
        Vector2 centre; float zoom = ZoomDefault; // metres per 1080 p pixel
        Vector2 cur; bool pressed, dragging; Vector2 pressAt; float holdT;
        float startZoom = -1; Vector2? startCentre; bool openAtStart;

        // ---- roads (two levels of detail, sorted by class: 0 street, 1 main road, 2 D8)
        struct Seg { public Vector2 a, b; }
        Seg[] fine, coarse; readonly int[] fineAt = new int[4], coarseAt = new int[4];
        // street labels: one per stretch of a named edge, longest first
        struct Lbl { public Vector2 p; public float ang, len; public string text; public bool d8; }
        Lbl[] streets;
        readonly Rect[] placed = new Rect[320]; int nPlaced;
        // stops
        Vector2[] stopPos; string[] stopTip; byte[] stopKind; // 0 delivery, 1 pump, 2 warehouse
        static readonly string[] Num = new string[32];
        // route
        readonly List<Vector2> routeLine = new List<Vector2>(); List<Vector3> routeOf; int routeN = -1;
        // drawing
        RenderTexture rt; Material mat; bool dirty = true; float redrawT;
        Vector2 drawnCentre; float drawnZoom; Vector3? drawnWp; int drawnLeg = -1; MagistralaGame.Job drawnJob;
        Camera cam; int camMask; bool pausedByUs; float oldScale = 1; bool cursorWas;
        // hover / info
        int hover = -1; string tip = ""; Vector2 tipAt;
        string info = ""; long infoKey = long.MinValue;
        readonly CachedText tWp = new CachedText();
        // the police the group reported (refreshed with the picture)
        readonly List<(Vector2 p, string tip)> police = new List<(Vector2, string)>();
        static readonly Color PoliceC = new Color32(0xC8, 0x3E, 0x2A, 0xff);

        static Color Hex(int v, float a = 1) => new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, a);
        /// GL colours into the target: measured in the player, IMGUI shows this render texture gamma-encoded twice (a stop dot
        /// 74 / 64 / 53 came out 147 / 137 / 127, as GpsMap found for its half-float one), so the colours go in linearised twice;
        /// the baked ground texture and the building colours are stored the same way (KartaBaker)
        /// a material colour: Unity linearises those itself once in linear space
        static Color Lm(Color c) { if (QualitySettings.activeColorSpace != ColorSpace.Linear) return c; var l = c.linear; l.a = c.a; return l; }
        static Color Lc(Color c) { if (QualitySettings.activeColorSpace != ColorSpace.Linear) return c; var l = c.linear.linear; l.a = c.a; return l; }
        /// the style at start (owner 2026-11-21: the flat printed-map look; the dark one with Tab, remembered in PlayerPrefs)
        public const bool DefaultDark = false;
        static bool dark = DefaultDark;
        // the palette of the current style (Palette)
        static Color LandC, Street, StreetFar, StreetCase, MainC, MainCase, D8C, D8Case, RouteC, RouteCase, StopC, StopRing, PumpC, LegC, WpC, WpRing,
            InkText, StreetText, Halo, ArrowFill, ArrowEdge, BldC, BldShade;
        static bool casings; static float roadScale;
        static void Palette(bool dk)
        {
            dark = dk;
            if (dk)
            {
                // dark: light roads on dark ground with dark casings, the D8 brightest; light text on a dark halo
                LandC = Hex(0x3E3628); Street = Hex(0xB8BCBA); StreetFar = Hex(0x8E9290); StreetCase = Hex(0x111513); MainC = Hex(0xD8DAD8); MainCase = Hex(0x0E1210);
                D8C = Hex(0xF6F0E4); D8Case = Hex(0x0B0E0C); RouteC = HudStyle.Accent; RouteCase = Hex(0x2A1A06); StopC = Hex(0xDCD7CD); StopRing = Hex(0x15191A);
                PumpC = Hex(0x3B87DA); LegC = HudStyle.Accent; WpC = HudStyle.Highlight; WpRing = Hex(0x15191A);
                InkText = Hex(0xEEE9E0); StreetText = Hex(0xC2BEB5); Halo = new Color(0.03f, 0.04f, 0.04f, 0.85f);
                ArrowFill = Color.white; ArrowEdge = Hex(0x0B0E0C); BldC = Hex(0x55585C); BldShade = new Color(0, 0, 0, 0);
                casings = true; roadScale = 1;
            }
            else
            {
                // flat: bold dark roads (no casings), white buildings with a 1 px shadow, dark text on a white halo
                LandC = Hex(0xE3E1DA); Street = Hex(0x5E626A); StreetFar = Hex(0x868A91); StreetCase = Street; MainC = Hex(0x40444C); MainCase = MainC;
                D8C = Hex(0x23262C); D8Case = D8C; RouteC = HudStyle.Accent; RouteCase = Hex(0x5A3200); StopC = Hex(0x2A2D33); StopRing = Color.white;
                PumpC = Hex(0x2E78C7); LegC = HudStyle.Accent; WpC = Hex(0x1F2226); WpRing = HudStyle.Highlight;
                InkText = Hex(0x202328); StreetText = Hex(0x33363C); Halo = new Color(1, 1, 1, 0.92f);
                ArrowFill = HudStyle.Ink; ArrowEdge = Color.white; BldC = Hex(0xFDFCF9); BldShade = Hex(0xA3A7AD);
                casings = false; roadScale = 1.15f;
            }
        }

        static string Arg(string k) { var a = System.Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == k) return a[i + 1]; return null; }

        void Start()
        {
            g = GetComponent<MagistralaGame>();
            var a = System.Environment.GetCommandLineArgs();
            int ki = System.Array.IndexOf(a, "-karta");
            if (ki >= 0)
            {
                openAtStart = true;
                if (ki + 1 < a.Length && float.TryParse(a[ki + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z)) startZoom = z;
            }
            if (Arg("-kartacentar") is string kc)
            {
                var v = kc.Split(',');
                if (v.Length == 2 && float.TryParse(v[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gx)
                    && float.TryParse(v[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var gz))
                    startCentre = new Vector2(gx + 4200, -gz - 5300);
            }
        }

        void OnDisable() { if (IsOpen) Close(); }
        void OnDestroy() { if (rt) { rt.Release(); Destroy(rt); } if (mat) Destroy(mat); }

        // ------------------------------------------------------------------ setup (once, at the first opening)
        bool Init()
        {
            if (init) return data != null;
            init = true;
            w = g.World;
            data = Resources.Load<KartaData>(KartaData.ResourcePath);
            if (!data) { Debug.LogWarning("[karta] no Resources/Karta — run Magistrala/Karta: ispeci"); return false; }
            var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
            mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            bool dk = DefaultDark;
            try { dk = PlayerPrefs.GetInt("karta.tamna", DefaultDark ? 1 : 0) == 1; } catch { }
            if (Arg("-kartastil") is string ks) dk = ks == "tamna";
            Palette(dk);
            for (int i = 0; i < Num.Length; i++) Num[i] = i.ToString();
            BuildRoads();
            BuildLabels();
            int n = w.stops.Length;
            stopPos = new Vector2[n]; stopTip = new string[n]; stopKind = new byte[n];
            for (int i = 0; i < n; i++)
            {
                var s = w.stops[i]; stopPos[i] = new Vector2(s.bay.x, s.bay.z);
                stopKind[i] = (byte)(s.id == MagistralaGame.Warehouse ? 2 : s.kind == "fuel" ? 1 : 0);
                stopTip[i] = stopKind[i] == 2 ? s.name : stopKind[i] == 1 ? s.name + " · gorivo" : string.IsNullOrEmpty(s.kind) ? s.name : s.name + " · " + s.kind;
            }
            Debug.Log($"[karta] ready: {fine.Length} / {coarse.Length} road segments (fine / coarse), {streets.Length} street labels, {data.placeNames.Length} places; {data.source}");
            return true;
        }

        static int RoadClass(GEdge e)
        {
            if (e.road == "D8" || (e.name != null && e.name.StartsWith("Cesta pape"))) return 2;
            if (e.lanes >= 2 || (e.road != null && e.road.Length > 0)) return 1;
            return 0;
        }

        void BuildRoads()
        {
            var lf = new List<Seg>[3]; var lc = new List<Seg>[3];
            for (int c = 0; c < 3; c++) { lf[c] = new List<Seg>(); lc[c] = new List<Seg>(); }
            var line = new List<Vector2>();
            foreach (var e in w.edges)
            {
                if (e.closed || e.pn < 2) continue;
                int c = RoadClass(e);
                for (int pass = 0; pass < 2; pass++)
                {
                    line.Clear();
                    for (int k = 0; k < e.pn; k++) { var p = w.pts[e.p0 + k]; line.Add(new Vector2(p.x, p.z)); }
                    Simplify(line, pass == 0 ? 0.8f : 4f);
                    var dst = pass == 0 ? lf[c] : lc[c];
                    for (int k = 0; k + 1 < line.Count; k++) dst.Add(new Seg { a = line[k], b = line[k + 1] });
                }
            }
            fine = Pack(lf, fineAt); coarse = Pack(lc, coarseAt);
        }
        static Seg[] Pack(List<Seg>[] l, int[] at)
        {
            var o = new List<Seg>();
            for (int c = 0; c < 3; c++) { at[c] = o.Count; o.AddRange(l[c]); }
            at[3] = o.Count; return o.ToArray();
        }

        void BuildLabels()
        {
            var l = new List<Lbl>();
            const float Step = 340;
            foreach (var e in w.edges)
            {
                if (e.closed || e.pn < 2 || string.IsNullOrEmpty(e.name)) continue;
                float len = 0; for (int k = 0; k + 1 < e.pn; k++) len += Vector3.Distance(w.pts[e.p0 + k], w.pts[e.p0 + k + 1]);
                if (len < 45) continue;
                bool d8 = RoadClass(e) == 2;
                int n = Mathf.Max(1, Mathf.FloorToInt(len / Step));
                for (int q = 0; q < n; q++)
                {
                    float at = len * (q + 0.5f) / n, s = 0;
                    for (int k = 0; k + 1 < e.pn; k++)
                    {
                        Vector3 a = w.pts[e.p0 + k], b = w.pts[e.p0 + k + 1]; float L = Vector3.Distance(a, b);
                        if (s + L < at && k + 2 < e.pn) { s += L; continue; }
                        // direction over ±12 m around the anchor (steadier than one 3 m segment)
                        Vector3 p = Vector3.Lerp(a, b, L > 1e-3f ? Mathf.Clamp01((at - s) / L) : 0);
                        Vector3 p0 = w.pts[e.p0 + Mathf.Max(0, k - 3)], p1 = w.pts[e.p0 + Mathf.Min(e.pn - 1, k + 4)];
                        float ang = Mathf.Atan2(-(p1.z - p0.z), p1.x - p0.x) * Mathf.Rad2Deg; // screen y down
                        if (ang > 90) ang -= 180; else if (ang < -90) ang += 180;
                        l.Add(new Lbl { p = new Vector2(p.x, p.z), ang = ang, len = len / n, text = e.name, d8 = d8 });
                        break;
                    }
                }
            }
            l.Sort((x, y) => y.len.CompareTo(x.len));
            streets = l.ToArray();
        }

        static readonly Stack<(int, int)> dpStack = new Stack<(int, int)>();
        static readonly List<bool> dpKeep = new List<bool>();
        /// Douglas-Peucker in place (GpsMap's)
        static void Simplify(List<Vector2> p, float tol)
        {
            if (p.Count < 3) return;
            dpKeep.Clear(); for (int i = 0; i < p.Count; i++) dpKeep.Add(false);
            dpKeep[0] = dpKeep[p.Count - 1] = true; dpStack.Clear(); dpStack.Push((0, p.Count - 1));
            while (dpStack.Count > 0)
            {
                var (i0, i1) = dpStack.Pop(); if (i1 - i0 < 2) continue;
                Vector2 a = p[i0], d = p[i1] - a; float L = d.magnitude, bd = tol; int bi = -1;
                for (int i = i0 + 1; i < i1; i++)
                {
                    float dist = L < 1e-4f ? (p[i] - a).magnitude : Mathf.Abs(d.x * (p[i].y - a.y) - d.y * (p[i].x - a.x)) / L;
                    if (dist > bd) { bd = dist; bi = i; }
                }
                if (bi >= 0) { dpKeep[bi] = true; dpStack.Push((i0, bi)); dpStack.Push((bi, i1)); }
            }
            int n = 0; for (int i = 0; i < p.Count; i++) if (dpKeep[i]) p[n++] = p[i];
            p.RemoveRange(n, p.Count - n);
        }

        // ------------------------------------------------------------------ open / close
        public void Open(float z = -1, Vector2? at = null)
        {
            if (IsOpen || !g || !g.Truck || g.Router == null) return;
            if (g.Phone && g.Phone.Open) return;
            if (!Init()) return;
            IsOpen = true;
            var tc = g.TruckCentre();
            centre = at ?? new Vector2(tc.x, tc.z);
            if (z > 0) zoom = z;
            zoom = Mathf.Clamp(zoom, ZoomMin, ZoomMax); ClampView();
            cur = new Vector2(Screen.width * .5f, Screen.height * .5f);
            pressed = dragging = false; holdT = 0; dirty = true;
            if (Time.timeScale > 0) { oldScale = Time.timeScale; Time.timeScale = 0; AudioListener.pause = true; pausedByUs = true; }
            cam = Camera.main; if (cam) { camMask = cam.cullingMask; cam.cullingMask = 0; }
            cursorWas = Cursor.visible; Cursor.visible = false;
            Debug.Log($"[karta] open at game x={centre.x - 4200:0} z={-centre.y - 5300:0}, {zoom:0.00} m/px");
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (pausedByUs) { pausedByUs = false; Time.timeScale = oldScale; AudioListener.pause = false; }
            if (cam) { cam.cullingMask = camMask; cam = null; }
            Cursor.visible = cursorWas;
            if (rt) { rt.Release(); Destroy(rt); rt = null; }
        }

        // ------------------------------------------------------------------ input
        void Update()
        {
            var kb = Keyboard.current;
            if (!IsOpen)
            {
                if (openAtStart && g && g.Truck && g.Router != null && Time.frameCount > 10) { openAtStart = false; Open(startZoom, startCentre); return; }
                if (kb != null && kb.mKey.wasPressedThisFrame && !DroneCamera.Active) Open();
                return;
            }
            if (g && g.Phone && g.Phone.Open) { Close(); return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f), K = HudStyle.K;
            if (kb != null && (kb.mKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
            {
                if (kb.escapeKey.wasPressedThisFrame) escFrame = Time.frameCount;
                Close(); return;
            }
            var mouse = Mouse.current;
            Vector2 mid = new Vector2(Screen.width * .5f, Screen.height * .5f);
            // keyboard pan (accelerating), the cursor back to the centre
            Vector2 dir = Vector2.zero;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) dir.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) dir.y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dir.x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) dir.x -= 1;
            }
            if (dir != Vector2.zero)
            {
                holdT += dt;
                float v = 380 + 1500 * Mathf.Clamp01(holdT / 1.4f) * Mathf.Clamp01(holdT / 1.4f); // 1080 p px / s
                centre += dir.normalized * v * zoom * dt;
                cur = Vector2.MoveTowards(cur, mid, dt * 2500 * K);
            }
            else holdT = 0;
            // mouse: the cursor follows it; drag pans; a click without a drag = Enter
            bool click = false;
            if (mouse != null)
            {
                Vector2 mp = mouse.position.ReadValue(); mp.y = Screen.height - mp.y;
                Vector2 md = mouse.delta.ReadValue();
                if (md.sqrMagnitude > 0.25f && dir == Vector2.zero) cur = mp;
                if (mouse.leftButton.wasPressedThisFrame) { pressed = true; dragging = false; pressAt = mp; }
                if (pressed && mouse.leftButton.isPressed)
                {
                    if (!dragging && (mp - pressAt).magnitude > 5 * K) dragging = true;
                    if (dragging) centre -= new Vector2(md.x, md.y) * (zoom / K);
                }
                if (pressed && mouse.leftButton.wasReleasedThisFrame) { if (!dragging) click = true; pressed = dragging = false; }
                float sc = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(sc) > 0.01f) ZoomAt(Mathf.Exp(-Mathf.Sign(sc) * Mathf.Clamp(Mathf.Abs(sc) / 40f, 0.25f, 3f) * 0.2f));
            }
            if (kb != null && kb.tabKey.wasPressedThisFrame)
            {
                Palette(!dark); dirty = true;
                try { PlayerPrefs.SetInt("karta.tamna", dark ? 1 : 0); } catch { }
            }
            if (kb != null)
            {
                bool zin = kb.equalsKey.isPressed || kb.numpadPlusKey.isPressed, zout = kb.minusKey.isPressed || kb.slashKey.isPressed || kb.numpadMinusKey.isPressed;
                if (zin != zout) ZoomAt(Mathf.Exp((zin ? -1 : 1) * dt * 1.8f));
                if (!KatoviTool.Active && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) click = true;
            }
            ClampView();
            if (click) ToggleWaypoint();
            Hover();
        }

        void ZoomAt(float f)
        {
            Vector2 before = ToWorld(cur);
            zoom = Mathf.Clamp(zoom * f, ZoomMin, ZoomMax);
            Vector2 after = ToWorld(cur);
            centre += before - after;
        }

        void ClampView()
        {
            // never further out than the baked picture fills the screen
            zoom = Mathf.Min(zoom, Mathf.Max(ZoomMin, Mathf.Min(data.size.x / Screen.width, data.size.y / Screen.height) * HudStyle.K));
            float K = HudStyle.K, hw = Screen.width * .5f * zoom / K, hh = Screen.height * .5f * zoom / K;
            Vector2 o = data.origin, s = data.size;
            centre.x = s.x > 2 * hw ? Mathf.Clamp(centre.x, o.x + hw, o.x + s.x - hw) : o.x + s.x * .5f;
            centre.y = s.y > 2 * hh ? Mathf.Clamp(centre.y, o.y + hh, o.y + s.y - hh) : o.y + s.y * .5f;
        }

        float Scale => HudStyle.K / zoom; // screen px per metre
        Vector2 ToScreen(Vector2 p) { float s = Scale; return new Vector2(Screen.width * .5f + (p.x - centre.x) * s, Screen.height * .5f - (p.y - centre.y) * s); }
        Vector2 ToScreen(Vector3 p) => ToScreen(new Vector2(p.x, p.z));
        Vector2 ToWorld(Vector2 q) { float s = Scale; return new Vector2(centre.x + (q.x - Screen.width * .5f) / s, centre.y - (q.y - Screen.height * .5f) / s); }

        void ToggleWaypoint()
        {
            float K = HudStyle.K;
            if (g.Waypoint is Vector3 wp && (ToScreen(wp) - cur).magnitude < 16 * K) { g.ClearWaypoint("removed on the map"); dirty = true; return; }
            var p = ToWorld(cur);
            g.SetWaypoint(new Vector3(p.x, 0, p.y));
            dirty = true;
        }

        /// the marker under the cursor (−1 none, 0..stops−1 a stop, 1000 the truck, 1001 the waypoint, 2000+i a job leg);
        /// else the street and place under it for the info line
        void Hover()
        {
            float K = HudStyle.K, best = 14 * K; int h = -1;
            var tc = g.TruckCentre();
            float d = (ToScreen(tc) - cur).magnitude; if (d < best) { best = d; h = 1000; }
            if (g.Waypoint is Vector3 wp) { d = (ToScreen(wp) - cur).magnitude; if (d < best) { best = d; h = 1001; } }
            for (int i = 0; i < stopPos.Length; i++) { d = (ToScreen(stopPos[i]) - cur).magnitude; if (d < best) { best = d; h = i; } }
            for (int i = 0; i < police.Count; i++) { d = (ToScreen(police[i].p) - cur).magnitude; if (d < best + 4 * K) { best = d; h = 3000 + i; } }
            hover = h;
            if (h == 1000) { tip = "Kamion"; tipAt = ToScreen(tc); }
            else if (h == 1001) { tip = tWp.Get(Mathf.RoundToInt(g.RouteMetres / 10), k => "Oznaka · " + HudStyle.Km(k * 10)); tipAt = ToScreen(g.Waypoint.Value); }
            else if (h >= 3000) { tip = police[h - 3000].tip; tipAt = ToScreen(police[h - 3000].p); }
            else if (h >= 0) { tip = stopTip[h]; tipAt = ToScreen(stopPos[h]); }
            else tip = "";
            // info line: nearest road segment within 14 px, and the place
            Vector2 wpos = ToWorld(cur);
            int cx = Mathf.FloorToInt(wpos.x / 4), cz = Mathf.FloorToInt(wpos.y / 4);
            long key = ((long)cx << 32) ^ (uint)cz ^ ((long)Mathf.RoundToInt(zoom * 20) << 52);
            if (key == infoKey) return;
            infoKey = key;
            float lim = 14 * zoom, bd = lim * lim; int be = -1;
            for (int e = 0; e < w.edges.Length; e++)
            {
                var ge = w.edges[e]; if (ge.closed || string.IsNullOrEmpty(ge.name)) continue;
                Vector3 p0 = w.pts[ge.p0], p1 = w.pts[ge.p0 + ge.pn - 1];
                float r = ge.length * .5f + lim + 5; Vector2 m = new Vector2((p0.x + p1.x) * .5f, (p0.z + p1.z) * .5f);
                if ((m - wpos).sqrMagnitude > r * r) continue;
                for (int k = 0; k + 1 < ge.pn; k++)
                {
                    Vector3 a3 = w.pts[ge.p0 + k], b3 = w.pts[ge.p0 + k + 1];
                    Vector2 a = new Vector2(a3.x, a3.z), ab = new Vector2(b3.x, b3.z) - a;
                    float t = ab.sqrMagnitude < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(wpos - a, ab) / ab.sqrMagnitude);
                    float dd = (a + ab * t - wpos).sqrMagnitude;
                    if (dd < bd) { bd = dd; be = e; }
                }
            }
            string place = w.PlaceAt(new Vector3(wpos.x, 0, wpos.y));
            string street = be >= 0 ? w.edges[be].name : "";
            info = street.Length > 0 && place.Length > 0 ? street + " · " + place : street.Length > 0 ? street : place;
        }

        // ------------------------------------------------------------------ the picture (RenderTexture, when something changed)
        void LateUpdate()
        {
            if (!IsOpen || data == null) return;
            int W = Screen.width, H = Screen.height;
            if (!rt || rt.width != W || rt.height != H)
            {
                if (rt) { rt.Release(); Destroy(rt); }
                // half floats, linear (as GpsMap): the colours go in linearised twice (Lc) and keep the dark palette's tones
                rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 4, name = "karta", filterMode = FilterMode.Point };
                rt.Create(); dirty = true;
            }
            var route = g.Route;
            if (route != routeOf || (route != null && route.Count != routeN))
            {
                routeOf = route; routeN = route?.Count ?? -1; routeLine.Clear();
                if (route != null) { foreach (var q in route) routeLine.Add(new Vector2(q.x, q.z)); Simplify(routeLine, 0.8f); }
                dirty = true;
            }
            var job = g.Current; int leg = job != null ? job.leg : -1;
            if (centre != drawnCentre || zoom != drawnZoom || g.Waypoint != drawnWp || job != drawnJob || leg != drawnLeg) dirty = true;
            redrawT -= Time.unscaledDeltaTime; if (redrawT <= 0) dirty = true;
            if (!dirty) return;
            dirty = false; redrawT = 1; drawnCentre = centre; drawnZoom = zoom; drawnWp = g.Waypoint; drawnJob = job; drawnLeg = leg;
            Police();
            Draw(W, H);
        }

        /// the police posts the phone's group reported lately ("Policija · Bepo, 12 min: radar na D8 iznad Starog")
        void Police()
        {
            police.Clear();
            if (!g.Phone) return;
            float now = g.ClockMin;
            foreach (var l in g.Phone.KnownPolice())
            {
                var s = Magistrala.Game.Phone.BlicajFeed.Spot(l.spot); if (s == null) continue;
                int ago = Mathf.Max(0, Mathf.FloorToInt(now - l.at));
                string t = l.text.Length > 60 ? l.text.Substring(0, 59) + "…" : l.text;
                police.Add((new Vector2(s.pos.x, s.pos.z), $"Policija · {l.who}, {(ago < 1 ? "upravo" : ago < 60 ? ago + " min" : ago / 60 + " h")}: {t}"));
            }
        }

        void Draw(int W, int H)
        {
            float K = HudStyle.K, s = Scale;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.PushMatrix(); GL.LoadPixelMatrix(0, W, H, 0);
            GL.Clear(false, true, Lc(LandC));
            // the ground of the style (stored ×gain, KartaBaker: tinted back down here)
            mat.color = Color.white;
            var gt = dark ? data.groundDark : data.ground;
            if (gt)
            {
                mat.mainTexture = gt; mat.SetPass(0);
                Vector2 a = ToScreen(data.origin), b = ToScreen(data.origin + data.size);
                float gg = 1 / Mathf.Max(1, dark ? data.gainDark : data.gain);
                GL.Begin(GL.QUADS); GL.Color(new Color(gg, gg, gg, 1));
                GL.TexCoord2(0, 0); GL.Vertex3(a.x, a.y, 0); GL.TexCoord2(1, 0); GL.Vertex3(b.x, a.y, 0);
                GL.TexCoord2(1, 1); GL.Vertex3(b.x, b.y, 0); GL.TexCoord2(0, 1); GL.Vertex3(a.x, b.y, 0);
                GL.End();
            }
            // buildings: a soft shadow 1 px down-right, then the fill (vertex colours)
            if (data.buildings)
            {
                mat.mainTexture = Texture2D.whiteTexture;
                float off = Mathf.Max(1, K * (zoom < 0.6f ? 1.5f : 1f));
                var m = Matrix4x4.TRS(new Vector3(W * .5f - centre.x * s + off, H * .5f + centre.y * s + off, 0), Quaternion.identity, new Vector3(s, -s, 1));
                if (BldShade.a > 0) { mat.color = Lm(BldShade); mat.SetPass(0); Graphics.DrawMeshNow(data.buildings, m); }
                m.m03 -= off; m.m13 -= off;
                mat.color = Lm(BldC); mat.SetPass(0); Graphics.DrawMeshNow(data.buildings, m);
            }
            mat.mainTexture = Texture2D.whiteTexture; mat.color = Color.white; mat.SetPass(0);
            // roads: casings, then fills; class widths in 1080 p px with a world-width floor
            bool useFine = zoom < FineBelow; var segs = useFine ? fine : coarse; var at = useFine ? fineAt : coarseAt;
            float pm = 1f / zoom; // 1080 p px per metre
            float rs = roadScale;
            float wMinor = Mathf.Max(zoom > 3.5f ? 1.1f : 1.7f, 6f * pm) * rs, wMain = Mathf.Max(2.6f, 8.5f * pm) * rs, wD8 = Mathf.Max(4f, 11f * pm) * rs * (casings ? 1 : 1.15f);
            float cs = zoom > 3.5f || !casings ? 0 : 1.6f; // casing (total extra width)
            Rect view = new Rect(-40 * K, -40 * K, W + 80 * K, H + 80 * K);
            GL.Begin(GL.QUADS);
            if (cs > 0)
            {
                Segs(segs, at[0], at[1], (wMinor + cs) * K, Lc(StreetCase), view);
                Segs(segs, at[1], at[2], (wMain + cs) * K, Lc(MainCase), view);
            }
            if (casings) Segs(segs, at[2], at[3], (wD8 + 1.8f) * K, Lc(D8Case), view);
            Segs(segs, at[0], at[1], wMinor * K, Lc(zoom > 3.5f ? StreetFar : Street), view);
            Segs(segs, at[1], at[2], wMain * K, Lc(MainC), view);
            Segs(segs, at[2], at[3], wD8 * K, Lc(D8C), view);
            GL.End();
            // the GPS route: the casing (segments + round joins), then the fill
            float wr = Mathf.Max(5f, 7f * pm) * K;
            for (int pass = 0; pass < 2; pass++)
            {
                float rw = pass == 0 ? wr + 3 * K : wr; Color rc = Lc(pass == 0 ? RouteCase : RouteC);
                GL.Begin(GL.QUADS);
                for (int i = 0; i + 1 < routeLine.Count; i++)
                {
                    Vector2 p = ToScreen(routeLine[i]), q = ToScreen(routeLine[i + 1]);
                    if (Out(p, q, view)) continue;
                    Quad(p, q, rw, rc);
                }
                GL.End();
                GL.Begin(GL.TRIANGLES);
                for (int i = 1; i + 1 < routeLine.Count; i++) { var p = ToScreen(routeLine[i]); if (view.Contains(p)) Disc(p, rw * .5f, rc, 8); }
                GL.End();
            }
            GL.Begin(GL.TRIANGLES);
            // stops: small dark dots (white ring), pumps blue; the warehouse; the job's remaining legs; the waypoint; the truck
            float rsz = (zoom < 0.8f ? 5.5f : 4.5f) * K;
            for (int i = 0; i < stopPos.Length; i++)
            {
                if (stopKind[i] == 2) continue;
                var p = ToScreen(stopPos[i]); if (!view.Contains(p)) continue;
                if (stopKind[i] == 1) { Disc(p, 11 * K, Lc(Color.white), 20); Disc(p, 9.5f * K, Lc(PumpC), 20); }
                else { Disc(p, rsz + 1.6f * K, Lc(StopRing), 14); Disc(p, rsz, Lc(StopC), 14); }
            }
            int wh = w.StopIndex(MagistralaGame.Warehouse);
            if (wh >= 0) { var p = ToScreen(stopPos[wh]); Disc(p, 14 * K, Lc(HudStyle.Ink), 24); Disc(p, 12 * K, Lc(HudStyle.Accent), 24); }
            var job = g.Current;
            if (job != null)
                for (int l = job.leg; l < job.legs.Count; l++)
                {
                    var st = w.Stop(job.legs[l].stop); if (st == null) continue;
                    var p = ToScreen(st.bay);
                    Disc(p, 12 * K, Lc(Color.white), 22); Disc(p, 10 * K, Lc(LegC), 22);
                }
            foreach (var (pp, _) in police)
            {
                var p = ToScreen(pp); if (!view.Contains(p)) continue;
                Disc(p, 14 * K, Lc(Color.white), 24); Disc(p, 12 * K, Lc(PoliceC), 24);
            }
            if (g.Waypoint is Vector3 wpt)
            {
                var p = ToScreen(wpt);
                Disc(p, 13 * K, Lc(Color.white), 24); Disc(p, 10.5f * K, Lc(WpC), 24); Disc(p, 4.5f * K, Lc(WpRing), 16);
            }
            {
                var tc = g.TruckCentre(); var f = g.Truck.transform.forward;
                Vector2 hd = new Vector2(f.x, -f.z); if (hd.sqrMagnitude < 1e-6f) hd = Vector2.down; hd.Normalize();
                var p = ToScreen(tc);
                Arrow(p, hd, 15 * K * 1.32f, Lc(ArrowEdge));
                Arrow(p, hd, 15 * K, Lc(ArrowFill));
            }
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        void Segs(Seg[] segs, int i0, int i1, float wpx, Color c, Rect view)
        {
            GL.Color(c);
            float s = Scale, hx = Screen.width * .5f, hy = Screen.height * .5f;
            for (int i = i0; i < i1; i++)
            {
                Vector2 a = segs[i].a, b = segs[i].b;
                Vector2 p = new Vector2(hx + (a.x - centre.x) * s, hy - (a.y - centre.y) * s), q = new Vector2(hx + (b.x - centre.x) * s, hy - (b.y - centre.y) * s);
                if (Out(p, q, view)) continue;
                QuadNoColour(p, q, wpx);
            }
        }
        static bool Out(Vector2 p, Vector2 q, Rect v) => Mathf.Max(p.x, q.x) < v.xMin || Mathf.Min(p.x, q.x) > v.xMax || Mathf.Max(p.y, q.y) < v.yMin || Mathf.Min(p.y, q.y) > v.yMax;
        static void Quad(Vector2 a, Vector2 b, float w, Color c) { GL.Color(c); QuadNoColour(a, b, w); }
        static void QuadNoColour(Vector2 a, Vector2 b, float w)
        {
            Vector2 d = b - a; float L = d.magnitude; if (L < 1e-3f) return;
            d /= L; Vector2 n = new Vector2(-d.y, d.x) * (w * 0.5f); a -= d * (w * 0.45f); b += d * (w * 0.45f);
            GL.Vertex3(a.x + n.x, a.y + n.y, 0); GL.Vertex3(b.x + n.x, b.y + n.y, 0); GL.Vertex3(b.x - n.x, b.y - n.y, 0); GL.Vertex3(a.x - n.x, a.y - n.y, 0);
        }
        static void Disc(Vector2 c, float r, Color col, int n)
        {
            GL.Color(col);
            float step = Mathf.PI * 2 / n;
            for (int i = 0; i < n; i++)
            {
                float a0 = i * step, a1 = a0 + step;
                GL.Vertex3(c.x, c.y, 0); GL.Vertex3(c.x + Mathf.Cos(a0) * r, c.y + Mathf.Sin(a0) * r, 0); GL.Vertex3(c.x + Mathf.Cos(a1) * r, c.y + Mathf.Sin(a1) * r, 0);
            }
        }
        /// navigation arrow along hd (screen, y down): tip, two wings, a notch at the back
        static void Arrow(Vector2 c, Vector2 hd, float s, Color col)
        {
            Vector2 r = new Vector2(-hd.y, hd.x);
            Vector2 tip = c + hd * s, l = c - r * 0.72f * s - hd * 0.75f * s, rr = c + r * 0.72f * s - hd * 0.75f * s, notch = c - hd * 0.38f * s;
            GL.Color(col);
            GL.Vertex3(tip.x, tip.y, 0); GL.Vertex3(rr.x, rr.y, 0); GL.Vertex3(notch.x, notch.y, 0);
            GL.Vertex3(tip.x, tip.y, 0); GL.Vertex3(notch.x, notch.y, 0); GL.Vertex3(l.x, l.y, 0);
        }

        // ------------------------------------------------------------------ the overlay (IMGUI)
        static readonly (float m, string t)[] Bars = { (20, "20 m"), (50, "50 m"), (100, "100 m"), (200, "200 m"), (500, "500 m"), (1000, "1 km"), (2000, "2 km"), (5000, "5 km") };
        const string Hints = "W gore · S dolje · A lijevo · D desno · kotačić zoom · Enter zakači / otkači · Tab stil · M zatvori";

        void OnGUI()
        {
            if (!IsOpen || !rt || Event.current.type != EventType.Repaint) return;
            GUI.depth = -20;
            float K = HudStyle.K, W = Screen.width, H = Screen.height;
            GUI.DrawTexture(new Rect(0, 0, W, H), rt, ScaleMode.StretchToFill, false);
            nPlaced = 0;
            // place names
            var pst = HudStyle.St(zoom > 3 ? 17 : zoom > 1.2f ? 21 : 24, true, InkText, TextAnchor.MiddleCenter);
            var psh = HudStyle.St(zoom > 3 ? 17 : zoom > 1.2f ? 21 : 24, true, Halo, TextAnchor.MiddleCenter);
            for (int i = 0; i < data.placeNames.Length; i++)
            {
                if (zoom > 3 && data.placeCount[i] < 150) continue;
                var p = ToScreen(data.placePos[i]); if (p.x < -200 || p.x > W + 200 || p.y < -50 || p.y > H + 50) continue;
                string t = data.placeNames[i]; float tw = HudStyle.Width(pst, t) + 8 * K, th = 32 * K;
                var r = new Rect(p.x - tw * .5f, p.y - th * .5f, tw, th);
                if (Overlaps(r)) continue;
                Place(r); HaloLabel(r, t, pst, psh, 2f * K);
            }
            // stop names (close in), the D8 shields and the street names
            if (zoom < StopNamesZoom)
            {
                var sst = HudStyle.St(13, true, StreetText, TextAnchor.MiddleLeft); var ssh = HudStyle.St(13, true, Halo, TextAnchor.MiddleLeft);
                for (int i = 0; i < stopPos.Length; i++)
                {
                    var p = ToScreen(stopPos[i]); if (p.x < -50 || p.x > W + 50 || p.y < -20 || p.y > H + 20) continue;
                    string t = w.stops[i].name; float tw = HudStyle.Width(sst, t) + 4 * K;
                    var r = new Rect(p.x + 12 * K, p.y - 10 * K, tw, 20 * K);
                    if (Overlaps(r)) continue;
                    Place(r); HaloLabel(r, t, sst, ssh, 1.5f * K);
                }
            }
            StreetLabels(K, W, H);
            // glyphs on the markers: the pump, the warehouse (truck), the job's leg numbers
            for (int i = 0; i < stopPos.Length; i++)
            {
                if (stopKind[i] == 0) continue;
                var p = ToScreen(stopPos[i]); if (p.x < -30 || p.x > W + 30 || p.y < -30 || p.y > H + 30) continue;
                float gs = (stopKind[i] == 2 ? 16 : 13) * K;
                HudStyle.Glyph(new Rect(p.x - gs * .5f, p.y - gs * .5f, gs, gs), stopKind[i] == 2 ? HudStyle.Truck : HudStyle.Pump, stopKind[i] == 2 ? HudStyle.Ink : Color.white);
            }
            foreach (var (pp, _) in police)
            {
                var p = ToScreen(pp); if (p.x < -30 || p.x > W + 30 || p.y < -30 || p.y > H + 30) continue;
                float gs = 15 * K;
                HudStyle.Glyph(new Rect(p.x - gs * .5f, p.y - gs * .5f - K, gs, gs), Magistrala.Game.Phone.PhoneArt.Beacon, Color.white);
            }
            var job = g.Current;
            if (job != null)
            {
                var nst = HudStyle.St(12, true, HudStyle.Ink, TextAnchor.MiddleCenter);
                for (int l = job.leg, n = 1; l < job.legs.Count; l++, n++)
                {
                    var st = w.Stop(job.legs[l].stop); if (st == null) continue;
                    var p = ToScreen(st.bay);
                    HudStyle.Label(new Rect(p.x - 10 * K, p.y - 10 * K, 20 * K, 20 * K), Num[Mathf.Min(n, Num.Length - 1)], nst);
                }
            }
            // the waypoint's pulse
            if (g.Waypoint is Vector3 wp)
            {
                var p = ToScreen(wp); float ph = Mathf.Repeat(Time.unscaledTime * 0.8f, 1f), r = (12 + 16 * ph) * K;
                HudStyle.Ring(new Rect(p.x - r, p.y - r, 2 * r, 2 * r), new Color(HudStyle.Highlight.r, HudStyle.Highlight.g, HudStyle.Highlight.b, 1 - ph), 2.5f * K, r);
            }
            // the cursor: a ring with a dot (orange over a marker)
            {
                float r = 17 * K; bool over = hover >= 0;
                var rr = new Rect(cur.x - r, cur.y - r, 2 * r, 2 * r);
                HudStyle.Ring(new Rect(rr.x - 1.5f * K, rr.y - 1.5f * K, rr.width + 3 * K, rr.height + 3 * K), new Color(0, 0, 0, 0.55f), 5.5f * K, r + 1.5f * K);
                HudStyle.Ring(rr, over ? HudStyle.Accent : Color.white, 2.5f * K, r);
                float d = 3 * K; HudStyle.Fill(new Rect(cur.x - d, cur.y - d, 2 * d, 2 * d), over ? HudStyle.Accent : Color.white, d);
            }
            // tooltip
            if (tip.Length > 0)
            {
                var st = HudStyle.St(15, true, Color.white);
                float tw = HudStyle.Width(st, tip) + 24 * K, th = 32 * K;
                var r = new Rect(Mathf.Min(W - tw - 8 * K, tipAt.x + 22 * K), Mathf.Max(8 * K, tipAt.y - th - 14 * K), tw, th);
                HudStyle.PanelBg(r, 10);
                HudStyle.Label(new Rect(r.x + 12 * K, r.y, r.width - 24 * K, r.height), tip, st);
            }
            TopPanel(K, W);
            BottomStrip(K, W, H);
        }

        bool Overlaps(Rect r)
        {
            for (int i = 0; i < nPlaced; i++) if (placed[i].Overlaps(r)) return true;
            return false;
        }
        void Place(Rect r) { if (nPlaced < placed.Length) placed[nPlaced++] = r; }

        static void HaloLabel(Rect r, string t, GUIStyle st, GUIStyle halo, float o)
        {
            HudStyle.Label(new Rect(r.x - o, r.y, r.width, r.height), t, halo); HudStyle.Label(new Rect(r.x + o, r.y, r.width, r.height), t, halo);
            HudStyle.Label(new Rect(r.x, r.y - o, r.width, r.height), t, halo); HudStyle.Label(new Rect(r.x, r.y + o, r.width, r.height), t, halo);
            HudStyle.Label(r, t, st);
        }

        void StreetLabels(float K, float W, float H)
        {
            float s = Scale;
            bool names = zoom < StreetZoom;
            var st = HudStyle.St(zoom < 0.5f ? 14 : 12, false, StreetText, TextAnchor.MiddleCenter);
            var sh = HudStyle.St(zoom < 0.5f ? 14 : 12, false, Halo, TextAnchor.MiddleCenter);
            var d8 = HudStyle.St(11, true, Color.white, TextAnchor.MiddleCenter);
            var old = GUI.matrix;
            int shields = 0;
            for (int i = 0; i < streets.Length; i++)
            {
                var l = streets[i];
                var p = ToScreen(l.p); if (p.x < -100 || p.x > W + 100 || p.y < -40 || p.y > H + 40) continue;
                if (l.d8)
                {
                    // a D8 shield every few hundred metres, at any zoom (fewer when far out)
                    if (zoom > 2.5f && (i & 1) == 1) continue;
                    float bw = 30 * K, bh = 19 * K; var r = new Rect(p.x - bw * .5f, p.y - bh * .5f, bw, bh);
                    var big = new Rect(r.x - 30 * K, r.y - 10 * K, r.width + 60 * K, r.height + 20 * K);
                    if (Overlaps(big)) continue;
                    Place(big); shields++;
                    HudStyle.Fill(new Rect(r.x - K, r.y - K, r.width + 2 * K, r.height + 2 * K), Color.white, 5 * K);
                    HudStyle.Fill(r, HudStyle.Accent, 4 * K);
                    HudStyle.Label(r, "D8", d8);
                    continue;
                }
                if (!names) continue;
                float tw = HudStyle.Width(st, l.text);
                if (tw + 30 * K > l.len * s) continue; // longer than its stretch of road on screen
                float th = 18 * K;
                float ca = Mathf.Abs(Mathf.Cos(l.ang * Mathf.Deg2Rad)), sa = Mathf.Abs(Mathf.Sin(l.ang * Mathf.Deg2Rad));
                float bw2 = tw * ca + th * sa, bh2 = tw * sa + th * ca;
                var box = new Rect(p.x - bw2 * .5f, p.y - bh2 * .5f, bw2, bh2);
                if (Overlaps(box)) continue;
                Place(box);
                GUIUtility.RotateAroundPivot(l.ang, p);
                HaloLabel(new Rect(p.x - tw * .5f - 4, p.y - th * .5f, tw + 8, th), l.text, st, sh, 1.3f * K);
                GUI.matrix = old;
            }
        }

        void TopPanel(float K, float W)
        {
            float M = HudStyle.Margin;
            var tst = HudStyle.St(22, true, Color.white); var ist = HudStyle.St(15, false, HudStyle.Muted);
            float s = Scale; int bi = 0;
            for (int i = 0; i < Bars.Length; i++) { bi = i; if (Bars[i].m * s >= 70 * K) break; }
            float bl = Bars[bi].m * s;
            float pw = Mathf.Max(Mathf.Max(300 * K, 250 * K + bl), HudStyle.Width(ist, info) + 40 * K), ph = info.Length > 0 ? 78 * K : 52 * K;
            var r = new Rect(M, M, pw, ph);
            HudStyle.PanelBg(r, 14);
            HudStyle.Label(new Rect(r.x + 20 * K, r.y + 8 * K, 200 * K, 36 * K), "Karta · Kaštela", tst);
            if (info.Length > 0) HudStyle.Label(new Rect(r.x + 20 * K, r.y + 42 * K, pw - 40 * K, 26 * K), info, ist);
            // scale bar, right of the title
            float bx = r.xMax - 20 * K - bl, by = r.y + 30 * K;
            if (bx > r.x + 190 * K)
            {
                HudStyle.Fill(new Rect(bx, by, bl, 3 * K), Color.white, 1);
                HudStyle.Fill(new Rect(bx, by - 5 * K, 2 * K, 8 * K), Color.white); HudStyle.Fill(new Rect(bx + bl - 2 * K, by - 5 * K, 2 * K, 8 * K), Color.white);
                HudStyle.Label(new Rect(bx, by - 24 * K, bl, 18 * K), Bars[bi].t, HudStyle.St(12, false, HudStyle.Muted, TextAnchor.MiddleCenter));
            }
        }

        void BottomStrip(float K, float W, float H)
        {
            float M = HudStyle.Margin, h = 44 * K;
            var r = new Rect(M, H - M - h, W - 2 * M, h);
            HudStyle.PanelBg(r, 14);
            var st = HudStyle.St(14, false, Color.white);
            float x = r.x + 18 * K, cy = r.y + h * .5f;
            void Item(Color c, float rad, string t, bool ring = true, Texture glyph = null, Color gc = default)
            {
                float d = rad * K;
                if (ring) HudStyle.Fill(new Rect(x - K, cy - d - K, 2 * d + 2 * K, 2 * d + 2 * K), Color.white, d + K);
                HudStyle.Fill(new Rect(x, cy - d, 2 * d, 2 * d), c, d);
                if (glyph) HudStyle.Glyph(new Rect(x + d * .2f, cy - d * .8f, d * 1.6f, d * 1.6f), glyph, gc);
                x += 2 * d + 8 * K;
                float tw = HudStyle.Width(st, t);
                HudStyle.Label(new Rect(x, r.y, tw + 4, h), t, st);
                x += tw + 20 * K;
            }
            void Line(Color c, Color edge, float lw, string t)
            {
                HudStyle.Fill(new Rect(x, cy - (lw + 2) * K * .5f, 26 * K, (lw + 2) * K), edge, 2 * K);
                HudStyle.Fill(new Rect(x + K, cy - lw * K * .5f, 24 * K, lw * K), c, 2 * K);
                x += 34 * K;
                float tw = HudStyle.Width(st, t);
                HudStyle.Label(new Rect(x, r.y, tw + 4, h), t, st);
                x += tw + 20 * K;
            }
            Item(ArrowFill, 8, "Kamion");
            Item(HudStyle.Accent, 9, "Skladište", true, HudStyle.Truck, HudStyle.Ink);
            Item(StopC, 5, "Dostava");
            Item(PumpC, 8, "Pumpa", true, HudStyle.Pump, Color.white);
            Item(LegC, 8, "Tura");
            Item(WpC, 8, "Oznaka");
            if (police.Count > 0) Item(PoliceC, 9, "Policija", true, Magistrala.Game.Phone.PhoneArt.Beacon, Color.white);
            Line(RouteC, RouteCase, 5, "GPS");
            Line(D8C, D8Case, 5, "D8");
            var hs = HudStyle.St(13, false, HudStyle.Muted, TextAnchor.MiddleRight);
            float hw = HudStyle.Width(hs, Hints);
            if (x + hw + 20 * K < r.xMax) HudStyle.Label(new Rect(r.xMax - hw - 18 * K, r.y, hw, h), Hints, hs);
            else
            {
                // narrow screens: the hints above the strip
                var hr = new Rect(r.xMax - hw - 36 * K, r.y - 36 * K, hw + 36 * K, 30 * K);
                HudStyle.PanelBg(hr, 12); HudStyle.Label(new Rect(hr.x, hr.y, hr.width - 18 * K, hr.height), Hints, hs);
            }
        }
    }
}
