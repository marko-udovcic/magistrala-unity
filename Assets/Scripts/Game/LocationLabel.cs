// Location label (ETS2 / GTA style), bottom centre above the HUD panel: the place ("Kaštel Lukšić") and, on a road of the graph,
// its number and name ("D8 · Cesta pape Ivana Pavla II."). The place is a lookup in the baked 50 m grid
// (GameWorld.PlaceAt, OSM settlement boundaries, GameDataBuilder.Places.cs); the street is the nearest segment of the road graph
// (GameWorld edges / pts) through a 40 m bucket grid built once at start, within 22 m horizontally and 7 m in height (an
// underpass is not the D8 above it). Queried 4× a second, at the truck or, while the drone flies, at the ground under it.
// Steadiness: the current street is kept while it is within 3 m of the best one; an unnamed graph piece (junction internals,
// slip lanes) gives way to a named street up to 8 m further; text changes cross-fade.
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.Game
{
    public class LocationLabel : MonoBehaviour
    {
        const float Cell = 40, MaxDist = 22, MaxDy = 7, Every = .25f;
        GameWorld w; TruckController truck;
        readonly Dictionary<long, List<int>> buckets = new Dictionary<long, List<int>>();
        int[] segEdge; // per point index p (segment p → p + 1): its edge, -1 = last point of an edge
        int curEdge = -1; float nextQuery;
        string place = "", road = "", street = "", shownPlace = "", shownRoad = "", shownStreet = "";
        float fade = 1;
        GUIStyle placeStyle, streetStyle, refStyle;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (FindFirstObjectByType<LocationLabel>()) return;
            var gw = Resources.Load<GameWorld>(GameWorld.ResourcePath);
            if (!gw) return;
            var go = new GameObject("LocationLabel"); DontDestroyOnLoad(go); go.AddComponent<LocationLabel>().w = gw;
        }

        static long Key(int i, int j) => ((long)i << 32) ^ (uint)j;

        void Start()
        {
            var t0 = Time.realtimeSinceStartup;
            segEdge = new int[w.pts.Length];
            for (int i = 0; i < segEdge.Length; i++) segEdge[i] = -1;
            for (int e = 0; e < w.edges.Length; e++)
            {
                var ge = w.edges[e];
                for (int k = 0; k + 1 < ge.pn; k++)
                {
                    int p = ge.p0 + k; segEdge[p] = e;
                    Vector3 a = w.pts[p], b = w.pts[p + 1];
                    int i0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / Cell), i1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / Cell);
                    int j0 = Mathf.FloorToInt(Mathf.Min(a.z, b.z) / Cell), j1 = Mathf.FloorToInt(Mathf.Max(a.z, b.z) / Cell);
                    for (int i = i0; i <= i1; i++)
                        for (int j = j0; j <= j1; j++)
                        {
                            long key = Key(i, j);
                            if (!buckets.TryGetValue(key, out var l)) buckets[key] = l = new List<int>(4);
                            l.Add(p);
                        }
                }
            }
            Debug.Log($"[mjesto] {w.edges.Length} edges, {w.pts.Length} points in {buckets.Count} buckets ({(Time.realtimeSinceStartup - t0) * 1000:0} ms), {w.placeNames.Length - 1} places, grid {w.placeW}×{w.placeH}");
        }

        void Update()
        {
            if (Time.unscaledTime < nextQuery) return;
            nextQuery = Time.unscaledTime + Every;
            if (!truck) truck = FindFirstObjectByType<TruckController>();
            Vector3 at;
            bool drone = DroneCamera.Active;
            if (drone) at = DroneCamera.GroundPoint;
            else if (truck) at = truck.transform.position;
            else return;
            place = w.PlaceAt(at);
            curEdge = NearestEdge(at, drone ? 20f : MaxDy);
            var e = curEdge >= 0 ? w.edges[curEdge] : null;
            street = e != null ? e.name ?? "" : ""; road = e != null ? e.road ?? "" : "";
        }

        int NearestEdge(Vector3 q, float maxDy)
        {
            int ci = Mathf.FloorToInt(q.x / Cell), cj = Mathf.FloorToInt(q.z / Cell);
            float best = MaxDist, bestNamed = MaxDist, cur = float.MaxValue; int be = -1, bn = -1;
            for (int i = ci - 1; i <= ci + 1; i++)
                for (int j = cj - 1; j <= cj + 1; j++)
                {
                    if (!buckets.TryGetValue(Key(i, j), out var l)) continue;
                    foreach (int p in l)
                    {
                        Vector3 a = w.pts[p], b = w.pts[p + 1];
                        Vector2 a2 = new Vector2(a.x, a.z), d = new Vector2(b.x - a.x, b.z - a.z); float L = d.sqrMagnitude;
                        float t = L < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(new Vector2(q.x, q.z) - a2, d) / L);
                        float dist = Vector2.Distance(new Vector2(q.x, q.z), a2 + d * t);
                        if (Mathf.Abs(Mathf.Lerp(a.y, b.y, t) - q.y) > maxDy) continue;
                        int e = segEdge[p];
                        if (dist < best) { best = dist; be = e; }
                        if (dist < bestNamed && (w.edges[e].name.Length > 0 || w.edges[e].road.Length > 0)) { bestNamed = dist; bn = e; }
                        if (e == curEdge && dist < cur) cur = dist;
                    }
                }
            if (be < 0) return -1;
            if (bn >= 0 && bn != be && bestNamed < best + 8) { be = bn; best = bestNamed; }
            // keep the current street (or one of the same name) while it is about as near: no flicker at junctions
            if (curEdge >= 0 && cur <= MaxDist && cur < best + 3) return curEdge;
            return be;
        }

        void OnGUI()
        {
            if (GpuSwitches.NoHud) return;
            if (Event.current.type != EventType.Repaint) return;
            // cross-fade: fade out, swap the text, fade in
            bool changed = place != shownPlace || road != shownRoad || street != shownStreet;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .1f);
            if (changed) { fade -= dt * 5f; if (fade <= 0) { fade = 0; shownPlace = place; shownRoad = road; shownStreet = street; } }
            else fade = Mathf.Min(1, fade + dt * 4f);
            if (shownPlace.Length == 0 && shownStreet.Length == 0 && shownRoad.Length == 0) return;
            if (placeStyle == null)
            {
                placeStyle = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white }, padding = new RectOffset(0, 0, 0, 0) };
                streetStyle = new GUIStyle(placeStyle) { fontSize = 14, fontStyle = FontStyle.Normal, normal = { textColor = new Color(.86f, .88f, .9f) } };
                refStyle = new GUIStyle(placeStyle) { fontSize = 12, fontStyle = FontStyle.Bold };
            }
            var tex = Texture2D.whiteTexture;
            string top = shownPlace.Length > 0 ? shownPlace : shownStreet, bottom = shownPlace.Length > 0 ? shownStreet : "";
            bool refLine = shownPlace.Length > 0 && shownRoad.Length > 0;
            Vector2 ts = placeStyle.CalcSize(new GUIContent(top));
            Vector2 ss = bottom.Length > 0 ? streetStyle.CalcSize(new GUIContent(bottom)) : Vector2.zero;
            Vector2 rs = refLine ? refStyle.CalcSize(new GUIContent(shownRoad)) + new Vector2(10, 0) : Vector2.zero;
            float line2 = (refLine ? rs.x + (ss.x > 0 ? 8 : 0) : 0) + ss.x;
            float bw = Mathf.Max(ts.x, line2) + 36, bh = line2 > 0 ? 50 : 30;
            // above the HUD panel (8 px gap, 90 px high) and the game's key line (28 px) when it shows
            var g = MagistralaGame.I;
            float bottomY = Screen.height - 98 - 8 - (g && !string.IsNullOrEmpty(g.HelpKeys) ? 30 + 6 : 0);
            var box = new Rect((Screen.width - bw) / 2, bottomY - bh, bw, bh);
            float a = fade;
            var old = GUI.color;
            GUI.DrawTexture(box, tex, ScaleMode.StretchToFill, true, 0, new Color(.05f, .06f, .07f, .62f * a), 0, 10);
            GUI.DrawTexture(new Rect(box.x + 14, box.y + 1, box.width - 28, 2), tex, ScaleMode.StretchToFill, true, 0, new Color(1f, .8f, .1f, .9f * a), 0, 1);
            GUI.color = new Color(1, 1, 1, a);
            GUI.Label(new Rect(box.x, box.y + 4, box.width, 24), top, placeStyle);
            if (line2 > 0)
            {
                float x = box.x + (box.width - line2) / 2, y = box.y + 29;
                if (refLine)
                {
                    bool d = shownRoad[0] == 'D' || shownRoad[0] == 'A';
                    var rr = new Rect(x, y, rs.x, 17);
                    GUI.DrawTexture(rr, tex, ScaleMode.StretchToFill, true, 0, (d ? new Color(.1f, .35f, .78f) : new Color(.85f, .62f, .1f)) * new Color(1, 1, 1, a), 0, 3);
                    GUI.Label(rr, shownRoad, refStyle);
                    x += rs.x + 8;
                }
                if (ss.x > 0) GUI.Label(new Rect(x, y - 1, ss.x + 2, 19), bottom, streetStyle);
            }
            GUI.color = old;
        }
    }
}
