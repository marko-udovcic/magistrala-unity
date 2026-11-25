using UnityEngine;
using Magistrala.Game;

namespace Magistrala.Streaming
{
    /// <summary>Write an ordered world-space polyline, starting near position, into the caller's fixed buffer.
    /// Return count (0 = use velocity fallback). Do not allocate; never exceed output.Length.</summary>
    public interface ILookaheadSource
    {
        int WritePath(Vector3 position, Vector3 velocity, float seconds, Vector3[] output);
    }

    /// <summary>Heading-based graph prediction, not a mission route. Respects closures/one-ways and follows edge polylines.
    /// Built once; nearest-edge search uses the existing allocation-free GPS API. Junctions choose the straightest exit.</summary>
    public sealed class RoadGraphLookaheadSource : ILookaheadSource
    {
        readonly GameWorld world;
        readonly RoadRouter router;
        readonly int[] first, next, edge;
        readonly bool[] forward;

        public RoadGraphLookaheadSource(GameWorld world, RoadRouter router)
        {
            this.world = world;
            this.router = router;
            first = new int[world.nodes.Length];
            for (int i = 0; i < first.Length; i++) first[i] = -1;
            next = new int[world.edges.Length * 2]; edge = new int[next.Length]; forward = new bool[next.Length];
            int n = 0;
            for (int i = 0; i < world.edges.Length; i++)
            {
                var e = world.edges[i];
                if (e.closed || e.pn < 2) continue;
                Add(e.a, i, true, n++);
                if (!e.oneway) Add(e.b, i, false, n++);
            }
        }

        void Add(int node, int e, bool f, int slot)
        { edge[slot] = e; forward[slot] = f; next[slot] = first[node]; first[node] = slot; }

        public int WritePath(Vector3 position, Vector3 velocity, float seconds, Vector3[] output)
        {
            if (output.Length < 2 || velocity.sqrMagnitude < 1f) return 0;
            var spot = router.Nearest(position, velocity.normalized);
            if (spot.edge < 0 || spot.dist > 100f) return 0; // off-road: use actual velocity
            int ei = spot.edge, segment = spot.seg;
            var e = world.edges[ei];
            Vector3 ab = Flat(world.pts[e.p0 + segment + 1] - world.pts[e.p0 + segment]);
            bool fwd = e.oneway || Vector3.Dot(ab, velocity) >= 0;
            Vector3 cursor = spot.p, heading = velocity.normalized;
            float remaining = Flat(velocity).magnitude * Mathf.Max(0, seconds);
            int count = 1;
            output[0] = cursor;
            int point = fwd ? segment + 1 : segment;
            // Bounded even for graphs with zero-length edges or cycles.
            for (int hops = 0; hops < 256 && count < output.Length && remaining > .01f; hops++)
            {
                while (point >= 0 && point < e.pn && count < output.Length)
                {
                    Vector3 target = world.pts[e.p0 + point];
                    Vector3 delta = Flat(target - cursor);
                    float distance = delta.magnitude;
                    if (distance > .01f)
                    {
                        heading = delta / distance;
                        float step = Mathf.Min(distance, remaining);
                        cursor = Vector3.Lerp(cursor, target, step / distance);
                        output[count++] = cursor;
                        remaining -= step;
                        if (remaining <= .01f) return count;
                    }
                    point += fwd ? 1 : -1;
                }
                int node = fwd ? e.b : e.a, best = -1;
                float score = -2;
                for (int a = first[node]; a >= 0; a = next[a])
                {
                    if (edge[a] == ei || world.edges[edge[a]].closed) continue;
                    var candidate = world.edges[edge[a]];
                    int p = candidate.p0 + (forward[a] ? 0 : candidate.pn - 1);
                    Vector3 direction = Flat(world.pts[p + (forward[a] ? 1 : -1)] - world.pts[p]).normalized;
                    float dot = Vector3.Dot(heading, direction);
                    if (dot > score) { score = dot; best = a; }
                }
                if (best < 0) break;
                ei = edge[best]; fwd = forward[best]; e = world.edges[ei];
                point = fwd ? 0 : e.pn - 1;
            }
            return count;
        }

        static Vector3 Flat(Vector3 p) { p.y = 0; return p; }
    }
}
