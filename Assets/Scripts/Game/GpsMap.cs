// Minimap with the GPS route (web: "Navigacija po GTA-u" – a marker on the map and the yellow GPS route, no line on the road):
// round map in the top right corner, heading-up, the graph's roads within ~350 m in grey, the route in yellow, the target as a
// yellow dot (on the rim when it is further away), the truck as a white arrow in the middle. Plain IMGUI quads (no shaders).
// Every quad is its own draw call (and SetPass) on the render thread, so the polylines are simplified first (Douglas-Peucker,
// SIMPLIFY m ≈ 0.6 px at the map scale): the graph's points are 3–4 m apart (mag-11-19 outside streets 3 m), ~2200 segments
// within the cache radius at Lukšić drawn one by one took the render thread ~7 → ~35 ms; simplified ~110.
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.Game
{
    public class GpsMap : MonoBehaviour
    {
        public float radiusPx = 125, metres = 320;
        MagistralaGame g;
        Texture2D white;
        readonly List<(Vector2 a, Vector2 b, bool major)> roads = new List<(Vector2, Vector2, bool)>();
        Vector3 cachedAt = new Vector3(1e9f, 0, 0);
        const float SIMPLIFY = 1.5f;
        readonly List<Vector2> line = new List<Vector2>(), routeLine = new List<Vector2>();
        List<Vector3> routeOf; int routeN = -1;

        void Start()
        {
            g = GetComponent<MagistralaGame>();
            white = new Texture2D(1, 1); white.SetPixel(0, 0, Color.white); white.Apply();
        }

        void CacheRoads(Vector3 at)
        {
            cachedAt = at; roads.Clear();
            var w = g.World; float r2 = (metres * 1.6f) * (metres * 1.6f);
            foreach (var e in w.edges)
            {
                if (e.closed) continue;
                bool major = e.lanes >= 2 || e.name.StartsWith("D8") || e.name.Contains("Cesta pape");
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
        }

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

        void OnGUI()
        {
            if (Magistrala.GpuSwitches.NoHud) return;
            if (Event.current.type != EventType.Repaint || g == null || !g.Truck || g.World == null) return;
            Vector3 tp = g.TruckCentre();
            if ((new Vector2(tp.x - cachedAt.x, tp.z - cachedAt.z)).magnitude > metres * 0.4f) CacheRoads(tp);
            Vector3 f = g.Truck.transform.forward; float yaw = Mathf.Atan2(f.x, f.z);
            float cs = Mathf.Cos(yaw), sn = Mathf.Sin(yaw), sc = radiusPx / metres;
            Vector2 C = new Vector2(Screen.width - radiusPx - 18, radiusPx + 18);
            // world (x, z) → screen, heading up
            Vector2 S(Vector2 p) { float dx = p.x - tp.x, dz = p.y - tp.z; float rx = dx * cs - dz * sn, rz = dx * sn + dz * cs; return C + new Vector2(rx, -rz) * sc; }

            Disc(C, radiusPx + 4, new Color(0, 0, 0, 0.55f));
            Disc(C, radiusPx, new Color(0.16f, 0.19f, 0.17f, 0.85f));
            foreach (var (a, b, major) in roads) Seg(S(a), S(b), C, major ? 5f : 3f, major ? new Color(0.78f, 0.78f, 0.75f) : new Color(0.55f, 0.56f, 0.53f));
            var route = g.Route;
            if (route != routeOf || (route != null && route.Count != routeN))
            {
                routeOf = route; routeN = route?.Count ?? -1; routeLine.Clear();
                if (route != null) { foreach (var q in route) routeLine.Add(new Vector2(q.x, q.z)); Simplify(routeLine); }
            }
            for (int i = 0; i + 1 < routeLine.Count; i++) Seg(S(routeLine[i]), S(routeLine[i + 1]), C, 6f, new Color(1f, 0.8f, 0.1f));
            if (g.Target is Vector3 t)
            {
                Vector2 p = S(new Vector2(t.x, t.z)), d = p - C;
                if (d.magnitude > radiusPx - 8) p = C + d.normalized * (radiusPx - 8);
                Disc(p, 9, Color.black); Disc(p, 7, new Color(1f, 0.8f, 0.1f));
            }
            // the truck: a white arrow pointing up
            Seg(C + new Vector2(0, -11), C + new Vector2(-7, 8), C, 4, Color.white);
            Seg(C + new Vector2(0, -11), C + new Vector2(7, 8), C, 4, Color.white);
            Seg(C + new Vector2(-7, 8), C + new Vector2(7, 8), C, 4, Color.white);
        }

        // one rounded quad (border radius = r), not ~r/2 horizontal strips (each its own draw call)
        void Disc(Vector2 c, float r, Color col) => GUI.DrawTexture(new Rect(c.x - r, c.y - r, 2 * r, 2 * r), white, ScaleMode.StretchToFill, true, 0, col, 0, r);

        /// segment clipped to the map disc, drawn as a rotated quad
        void Seg(Vector2 a, Vector2 b, Vector2 c, float width, Color col)
        {
            float R = radiusPx - 1;
            Vector2 d = b - a; float A = Vector2.Dot(d, d); if (A < 1e-4f) return;
            Vector2 m = a - c; float B = 2 * Vector2.Dot(m, d), Cc = Vector2.Dot(m, m) - R * R, disc = B * B - 4 * A * Cc;
            if (disc <= 0) return;
            float sq = Mathf.Sqrt(disc), t0 = Mathf.Max(0, (-B - sq) / (2 * A)), t1 = Mathf.Min(1, (-B + sq) / (2 * A));
            if (t1 <= t0) return;
            Vector2 p = a + d * t0, q = a + d * t1;
            float len = Vector2.Distance(p, q), ang = Mathf.Atan2(q.y - p.y, q.x - p.x) * Mathf.Rad2Deg;
            var oldM = GUI.matrix; var oldC = GUI.color; GUI.color = col;
            GUIUtility.RotateAroundPivot(ang, p);
            GUI.DrawTexture(new Rect(p.x, p.y - width / 2, len + width * 0.3f, width), white);
            GUI.matrix = oldM; GUI.color = oldC;
        }
    }
}
