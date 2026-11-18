// GPS on the drivable graph (GameWorld): from the truck's place on the nearest edge to a node (a stop's pad end), Dijkstra on
// road metres, one-ways respected, closed edges skipped. Like the web GPS (world2/nav/navigator.ts) a turn-around costs extra
// (the truck avoids it where a sensible route exists).
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.Game
{
    public class RoadRouter
    {
        readonly GameWorld w;
        readonly List<(int e, bool fwd)>[] adj;
        const float UTurnCost = 240f;

        public RoadRouter(GameWorld world)
        {
            w = world;
            adj = new List<(int, bool)>[w.nodes.Length];
            for (int i = 0; i < adj.Length; i++) adj[i] = new List<(int, bool)>();
            for (int e = 0; e < w.edges.Length; e++)
            {
                var ge = w.edges[e]; if (ge.closed) continue;
                adj[ge.a].Add((e, true));
                if (!ge.oneway) adj[ge.b].Add((e, false));
            }
        }

        public struct Spot { public int edge, seg; public float t, s, dist; public Vector3 p; }

        /// nearest point on an open edge (horizontal distance); a one-way edge against the heading counts 8 m further
        public Spot Nearest(Vector3 pos, Vector3 fwd)
        {
            var best = new Spot { edge = -1, dist = float.MaxValue };
            Vector2 P = new Vector2(pos.x, pos.z), F = new Vector2(fwd.x, fwd.z).normalized;
            for (int e = 0; e < w.edges.Length; e++)
            {
                var ge = w.edges[e]; if (ge.closed) continue;
                float s = 0;
                for (int k = 0; k + 1 < ge.pn; k++)
                {
                    Vector3 a3 = w.pts[ge.p0 + k], b3 = w.pts[ge.p0 + k + 1];
                    Vector2 a = new Vector2(a3.x, a3.z), b = new Vector2(b3.x, b3.z), ab = b - a;
                    float L2 = ab.sqrMagnitude, L = Mathf.Sqrt(L2);
                    float t = L2 < 1e-6f ? 0 : Mathf.Clamp01(Vector2.Dot(P - a, ab) / L2);
                    float d = Vector2.Distance(P, a + ab * t);
                    if (ge.oneway && L > 1e-3f && Vector2.Dot(ab / L, F) < -0.3f) d += 8;
                    if (d < best.dist) best = new Spot { edge = e, seg = k, t = t, s = s + L * t, dist = d, p = Vector3.Lerp(a3, b3, t) };
                    s += L;
                }
            }
            return best;
        }

        /// route polyline from pos to node `target` (then on to `tail` when given); null = no route
        public List<Vector3> Route(Vector3 pos, Vector3 fwd, int target, Vector3? tail, out float metres)
        {
            metres = 0;
            var sp = Nearest(pos, fwd);
            if (sp.edge < 0 || target < 0) return null;
            var ge = w.edges[sp.edge];
            float elen = EdgeLen(ge);
            Vector3 dirAB = w.pts[ge.p0 + Mathf.Min(sp.seg + 1, ge.pn - 1)] - w.pts[ge.p0 + sp.seg]; dirAB.y = 0;
            bool headingAB = Vector3.Dot(dirAB, fwd) >= 0;
            int n = w.nodes.Length;
            var dist = new float[n]; var prevE = new int[n]; var prevFwd = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prevE[i] = -1; }
            var open = new SortedSet<(float d, int i)>();
            void Seed(int node, float d) { if (d < dist[node]) { dist[node] = d; open.Add((d, node)); } }
            Seed(ge.b, elen - sp.s + (headingAB ? 0 : UTurnCost));
            if (!ge.oneway) Seed(ge.a, sp.s + (headingAB ? UTurnCost : 0));
            var done = new bool[n];
            while (open.Count > 0)
            {
                var top = open.Min; open.Remove(top); int u = top.i;
                if (done[u]) continue; done[u] = true;
                if (u == target) break;
                foreach (var (e, f) in adj[u])
                {
                    var x = w.edges[e]; int v = f ? x.b : x.a;
                    // turning straight back onto the edge we came from = a turn-around
                    float extra = prevE[u] == e ? UTurnCost : 0;
                    float nd = dist[u] + EdgeLen(x) + extra;
                    if (nd < dist[v]) { if (dist[v] < float.MaxValue) open.Remove((dist[v], v)); dist[v] = nd; prevE[v] = e; prevFwd[v] = f; open.Add((nd, v)); }
                }
            }
            if (dist[target] == float.MaxValue) return null;
            // walk back the chain of edges
            var chain = new List<(int e, bool f)>();
            for (int v = target; prevE[v] >= 0;) { var e = prevE[v]; bool f = prevFwd[v]; chain.Add((e, f)); v = f ? w.edges[e].a : w.edges[e].b; if (chain.Count > 4000) break; }
            chain.Reverse();
            int startNode = chain.Count > 0 ? (chain[0].f ? w.edges[chain[0].e].a : w.edges[chain[0].e].b) : target;
            var line = new List<Vector3> { sp.p };
            // along the start edge to the first node
            if (startNode == ge.b) for (int k = sp.seg + 1; k < ge.pn; k++) line.Add(w.pts[ge.p0 + k]);
            else for (int k = sp.seg; k >= 0; k--) line.Add(w.pts[ge.p0 + k]);
            foreach (var (e, f) in chain)
            {
                var x = w.edges[e];
                if (f) for (int k = 1; k < x.pn; k++) line.Add(w.pts[x.p0 + k]);
                else for (int k = x.pn - 2; k >= 0; k--) line.Add(w.pts[x.p0 + k]);
            }
            if (tail.HasValue) line.Add(tail.Value);
            for (int i = 1; i < line.Count; i++) metres += Vector3.Distance(line[i - 1], line[i]);
            return line;
        }

        float EdgeLen(GEdge e) => e.length > 0 ? e.length : Vector3.Distance(w.pts[e.p0], w.pts[e.p0 + e.pn - 1]);
    }
}
