// Minimap with the GPS route (web: "Navigacija po GTA-u" – a marker on the map and the yellow GPS route, no line on the road):
// round map in the top right corner, heading-up, the graph's roads within ~350 m in grey, the route in yellow, the target as a
// yellow dot (on the rim when it is further away), the truck as a white arrow in the middle. Plain IMGUI quads (no shaders).
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
                for (int k = 0; k + 1 < e.pn; k++)
                {
                    Vector3 a = w.pts[e.p0 + k], b = w.pts[e.p0 + k + 1];
                    if ((new Vector2(a.x - at.x, a.z - at.z)).sqrMagnitude > r2 && (new Vector2(b.x - at.x, b.z - at.z)).sqrMagnitude > r2) continue;
                    roads.Add((new Vector2(a.x, a.z), new Vector2(b.x, b.z), major));
                }
            }
        }

        void OnGUI()
        {
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
            if (route != null) for (int i = 0; i + 1 < route.Count; i++) Seg(S(new Vector2(route[i].x, route[i].z)), S(new Vector2(route[i + 1].x, route[i + 1].z)), C, 6f, new Color(1f, 0.8f, 0.1f));
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

        void Disc(Vector2 c, float r, Color col)
        {
            // horizontal strips (a circle of quads)
            var old = GUI.color; GUI.color = col;
            int n = Mathf.Max(8, (int)(r / 2));
            for (int i = 0; i < n; i++)
            {
                float y0 = -r + 2 * r * i / n, y1 = -r + 2 * r * (i + 1) / n, ym = (y0 + y1) / 2, hw = Mathf.Sqrt(Mathf.Max(0, r * r - ym * ym));
                GUI.DrawTexture(new Rect(c.x - hw, c.y + y0, 2 * hw, y1 - y0 + 0.5f), white);
            }
            GUI.color = old;
        }

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
