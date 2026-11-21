// Minimap with the GPS route (web: "Navigacija po GTA-u" – a marker on the map and the yellow GPS route, no line on the road),
// drawn inside the HUD panel (TruckHud, owner's mockup hud-mockup-11-20): a rounded rectangle, heading-up, the truck as a white
// arrow low in the middle, the graph's roads in grey (main roads wider), the sea in dark blue (GameWorld.seaBits, baked by
// GameDataBuilder.BakeSea), the route in yellow, the target as an orange dot (on the edge when it is further away).
// Drawn with GL into a small RenderTexture (MSAA 4×) in LateUpdate: two SetPass calls (sea texture, then every road / route
// quad in one GL batch), the panel then shows the texture as one rounded quad. The polylines are still simplified first
// (Douglas-Peucker, SIMPLIFY m) and cached around the truck: the graph's points are 3–4 m apart, ~2200 segments within the
// cache radius at Lukšić; simplified a few hundred. The zoom eases out with speed (more road ahead on the D8).
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.Game
{
    public class GpsMap : MonoBehaviour
    {
        /// map scale in 1080 p pixels per metre: standing / at 70 km/h
        public float pxPerMetreSlow = 0.95f, pxPerMetreFast = 0.62f;
        /// the truck's place in the map, fraction of its height from the top
        public float truckAt = 0.68f;
        MagistralaGame g;
        readonly List<(Vector2 a, Vector2 b, bool major)> roads = new List<(Vector2, Vector2, bool)>();
        Vector3 cachedAt = new Vector3(1e9f, 0, 0);
        const float SIMPLIFY = 1.2f, CACHE_R = 650, RECACHE = 180;
        readonly List<Vector2> line = new List<Vector2>(), routeLine = new List<Vector2>();
        List<Vector3> routeOf; int routeN = -1;
        RenderTexture rt; Material mat; Texture2D sea;
        int wantW, wantH; float zoom = -1;
        public int Segments => roads.Count;

        // measured in the player: IMGUI shows this half-float texture's values gamma-encoded twice (land written as Color.linear
        // came out 133 instead of 60 / 255), so the colours go in linearised twice
        static Color L(Color c) { if (QualitySettings.activeColorSpace != ColorSpace.Linear) return c; var l = c.linear.linear; l.a = c.a; return l; }
        static readonly Color Land = L(new Color(0.36f, 0.355f, 0.345f, 1)), SeaC = L(new Color(0.3f, 0.4f, 0.45f, 1));
        static readonly Color Minor = L(new Color(0.58f, 0.575f, 0.57f, 1)), Major = L(new Color(0.75f, 0.745f, 0.74f, 1));
        static readonly Color Route = L(new Color(1f, 0.77f, 0.1f, 1)), Ink = L(HudStyle.Ink), Accent = L(HudStyle.Accent), Shade = new Color(0, 0, 0, 0.55f);

        void Start() { g = GetComponent<MagistralaGame>(); }

        /// the panel asks for a map of w × h screen pixels; returns last frame's picture (null before the first)
        public Texture Picture(int w, int h) { wantW = w; wantH = h; return rt; }

        void OnDestroy() { if (rt) { rt.Release(); Destroy(rt); } if (sea) Destroy(sea); if (mat) Destroy(mat); }

        void CacheRoads(Vector3 at)
        {
            cachedAt = at; roads.Clear();
            var w = g.World; float r2 = CACHE_R * CACHE_R;
            foreach (var e in w.edges)
            {
                if (e.closed) continue;
                // quick reject: both ends and the middle far away
                Vector3 p0 = w.pts[e.p0], p1 = w.pts[e.p0 + e.pn - 1], pm = w.pts[e.p0 + e.pn / 2];
                float lim = CACHE_R + e.length * 0.5f;
                if (Flat2(p0, at) > lim * lim && Flat2(p1, at) > lim * lim && Flat2(pm, at) > lim * lim) continue;
                bool major = e.lanes >= 2 || e.road == "D8" || e.name.StartsWith("Cesta pape");
                line.Clear();
                for (int k = 0; k < e.pn; k++) { Vector3 a = w.pts[e.p0 + k]; line.Add(new Vector2(a.x, a.z)); }
                Simplify(line);
                for (int k = 0; k + 1 < line.Count; k++)
                {
                    Vector2 a = line[k], b = line[k + 1];
                    if ((new Vector2(a.x - at.x, a.y - at.z)).sqrMagnitude > r2 && (new Vector2(b.x - at.x, b.y - at.z)).sqrMagnitude > r2) continue;
                    roads.Add((a, b, major));
                }
            }
            // the main roads last: drawn over the side streets
            roads.Sort((x, y) => x.major.CompareTo(y.major));
        }
        static float Flat2(Vector3 p, Vector3 q) { float dx = p.x - q.x, dz = p.z - q.z; return dx * dx + dz * dz; }

        /// Douglas-Peucker in place: the points within SIMPLIFY m of the kept line are dropped
        static readonly Stack<(int, int)> dpStack = new Stack<(int, int)>();
        static readonly List<bool> dpKeep = new List<bool>();
        static void Simplify(List<Vector2> p)
        {
            if (p.Count < 3) return;
            dpKeep.Clear(); for (int i = 0; i < p.Count; i++) dpKeep.Add(false);
            dpKeep[0] = dpKeep[p.Count - 1] = true; dpStack.Clear(); dpStack.Push((0, p.Count - 1));
            while (dpStack.Count > 0)
            {
                var (i0, i1) = dpStack.Pop(); if (i1 - i0 < 2) continue;
                Vector2 a = p[i0], d = p[i1] - a; float L = d.magnitude, bd = SIMPLIFY; int bi = -1;
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

        /// the sea mask as a texture (white, alpha = sea), bilinear: soft coast at any zoom
        void BuildSea()
        {
            var w = g.World;
            if (w.seaW <= 0 || w.seaH <= 0 || w.seaBits == null || w.seaBits.Length == 0) return;
            sea = new Texture2D(w.seaW, w.seaH, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "minimap sea" };
            var px = new Color32[w.seaW * w.seaH];
            for (int j = 0; j < w.seaH; j++) for (int i = 0; i < w.seaW; i++) px[j * w.seaW + i] = new Color32(255, 255, 255, w.SeaAt(i, j) ? (byte)255 : (byte)0);
            sea.SetPixels32(px); sea.Apply(false, true);
        }

        void LateUpdate()
        {
            if (GpuSwitches.NoHud || WorldMap.IsOpen || wantW < 8 || wantH < 8 || g == null || !g.Truck || g.World == null) return;
            if (!mat)
            {
                var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
                mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                BuildSea();
            }
            if (!rt || rt.width != wantW || rt.height != wantH)
            {
                if (rt) { rt.Release(); Destroy(rt); }
                // half floats, linear: the colours go in linearised (L) and keep their precision in the dark tones
                rt = new RenderTexture(wantW, wantH, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear) { antiAliasing = 4, name = "minimap", filterMode = FilterMode.Bilinear };
                rt.Create();
            }
            Vector3 tp = g.TruckCentre();
            if ((new Vector2(tp.x - cachedAt.x, tp.z - cachedAt.z)).magnitude > RECACHE) CacheRoads(tp);
            var route = g.Route;
            if (route != routeOf || (route != null && route.Count != routeN))
            {
                routeOf = route; routeN = route?.Count ?? -1; routeLine.Clear();
                if (route != null) { foreach (var q in route) routeLine.Add(new Vector2(q.x, q.z)); Simplify(routeLine); }
            }
            float k = wantH / 213f; // the panel's map is 213 px high at 1080 p: pixel sizes below are 1080 p pixels
            float kmh = g.Truck.SpeedKmh, target = Mathf.Lerp(pxPerMetreSlow, pxPerMetreFast, Mathf.InverseLerp(20, 70, kmh));
            zoom = zoom < 0 ? target : Mathf.Lerp(zoom, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 0.8f));
            float sc = zoom * k;
            Vector3 f = g.Truck.transform.forward; float yaw = Mathf.Atan2(f.x, f.z);
            float cs = Mathf.Cos(yaw), sn = Mathf.Sin(yaw);
            Vector2 C = new Vector2(wantW * 0.5f, wantH * truckAt);
            // world (x, z) → map pixels (y down), heading up
            Vector2 S(Vector2 p) { float dx = p.x - tp.x, dz = p.y - tp.z; float rx = dx * cs - dz * sn, rz = dx * sn + dz * cs; return C + new Vector2(rx, -rz) * sc; }
            Vector2 Wd(Vector2 m) { Vector2 d = (m - C) / sc; float rx = d.x, rz = -d.y; return new Vector2(tp.x + rx * cs + rz * sn, tp.z - rx * sn + rz * cs); }

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.PushMatrix(); GL.LoadPixelMatrix(0, wantW, wantH, 0);
            GL.Clear(false, true, Land);
            // the sea: one quad, the texture coordinates of the map's corners
            if (sea)
            {
                var w = g.World;
                mat.mainTexture = sea; mat.SetPass(0);
                GL.Begin(GL.QUADS); GL.Color(SeaC);
                void V(float x, float y) { var p = Wd(new Vector2(x, y)); GL.TexCoord2((p.x - w.seaOrigin.x) / (w.seaW * w.seaCell), (p.y - w.seaOrigin.y) / (w.seaH * w.seaCell)); GL.Vertex3(x, y, 0); }
                V(0, 0); V(wantW, 0); V(wantW, wantH); V(0, wantH);
                GL.End();
            }
            mat.mainTexture = Texture2D.whiteTexture; mat.SetPass(0);
            GL.Begin(GL.QUADS);
            float pad = 12 * k; Rect view = new Rect(-pad, -pad, wantW + 2 * pad, wantH + 2 * pad);
            foreach (var (a, b, major) in roads)
            {
                Vector2 p = S(a), q = S(b);
                if (Mathf.Max(p.x, q.x) < view.xMin || Mathf.Min(p.x, q.x) > view.xMax || Mathf.Max(p.y, q.y) < view.yMin || Mathf.Min(p.y, q.y) > view.yMax) continue;
                Seg(p, q, (major ? 3.4f : 1.8f) * k, major ? Major : Minor);
            }
            for (int i = 0; i + 1 < routeLine.Count; i++)
            {
                Vector2 p = S(routeLine[i]), q = S(routeLine[i + 1]);
                if (Mathf.Max(p.x, q.x) < view.xMin || Mathf.Min(p.x, q.x) > view.xMax || Mathf.Max(p.y, q.y) < view.yMin || Mathf.Min(p.y, q.y) > view.yMax) continue;
                Seg(p, q, 5f * k, Route);
            }
            GL.End();
            GL.Begin(GL.TRIANGLES);
            for (int i = 1; i + 1 < routeLine.Count; i++) { var p = S(routeLine[i]); if (view.Contains(p)) Disc(p, 2.5f * k, Route, 8); } // round joins
            if (g.Target is Vector3 t)
            {
                Vector2 p = S(new Vector2(t.x, t.z)), d = p - C;
                // further away: on the map's edge, towards the target
                float m = 10 * k, f1 = 1;
                if (d.x > 1e-3f) f1 = Mathf.Min(f1, (wantW - m - C.x) / d.x); else if (d.x < -1e-3f) f1 = Mathf.Min(f1, (m - C.x) / d.x);
                if (d.y > 1e-3f) f1 = Mathf.Min(f1, (wantH - m - C.y) / d.y); else if (d.y < -1e-3f) f1 = Mathf.Min(f1, (m - C.y) / d.y);
                p = C + d * Mathf.Max(0, f1);
                Disc(p, 7.5f * k, Ink, 18); Disc(p, 5.5f * k, Accent, 18);
            }
            // the truck: a white arrow (notched) on a dark outline
            Arrow(C, 13f * k, Shade, 1.3f);
            Arrow(C, 13f * k, Color.white, 1f);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        static void Seg(Vector2 a, Vector2 b, float w, Color c)
        {
            Vector2 d = b - a; float L = d.magnitude; if (L < 1e-3f) return;
            d /= L; Vector2 n = new Vector2(-d.y, d.x) * (w * 0.5f); a -= d * (w * 0.3f); b += d * (w * 0.3f);
            GL.Color(c);
            GL.Vertex3(a.x + n.x, a.y + n.y, 0); GL.Vertex3(b.x + n.x, b.y + n.y, 0); GL.Vertex3(b.x - n.x, b.y - n.y, 0); GL.Vertex3(a.x - n.x, a.y - n.y, 0);
        }
        static void Disc(Vector2 c, float r, Color col, int n)
        {
            GL.Color(col);
            for (int i = 0; i < n; i++)
            {
                float a0 = i * Mathf.PI * 2 / n, a1 = (i + 1) * Mathf.PI * 2 / n;
                GL.Vertex3(c.x, c.y, 0); GL.Vertex3(c.x + Mathf.Cos(a0) * r, c.y + Mathf.Sin(a0) * r, 0); GL.Vertex3(c.x + Mathf.Cos(a1) * r, c.y + Mathf.Sin(a1) * r, 0);
            }
        }
        /// navigation arrow pointing up: tip, two wings, a notch at the back
        static void Arrow(Vector2 c, float s, Color col, float grow)
        {
            s *= grow;
            Vector2 tip = c + new Vector2(0, -s), l = c + new Vector2(-0.72f * s, 0.75f * s), r = c + new Vector2(0.72f * s, 0.75f * s), notch = c + new Vector2(0, 0.38f * s);
            GL.Color(col);
            GL.Vertex3(tip.x, tip.y, 0); GL.Vertex3(r.x, r.y, 0); GL.Vertex3(notch.x, notch.y, 0);
            GL.Vertex3(tip.x, tip.y, 0); GL.Vertex3(notch.x, notch.y, 0); GL.Vertex3(l.x, l.y, 0);
        }
    }
}
