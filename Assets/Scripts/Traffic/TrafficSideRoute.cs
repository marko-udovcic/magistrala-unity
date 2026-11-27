using System;
using UnityEngine;

namespace Magistrala.Traffic
{
    /// Shared by the side-route baker and runtime. Prefer drivable meshes over terrain underneath
    /// them; never use vehicles, buildings, walls or triggers as a road. The local height window
    /// keeps a bridge on a different level out of the query. Allocate only when the sampler is built.
    public sealed class TrafficGroundSampler
    {
        readonly RaycastHit[] hits = new RaycastHit[64];
        readonly Transform roads, streets;

        public TrafficGroundSampler()
        {
            var world = GameObject.Find("World");
            if (!world) return;
            roads = world.transform.Find("Roads"); streets = world.transform.Find("Streets");
        }

        public bool Sample(Vector3 seed, out Vector3 point, out Vector3 normal)
        {
            point = seed; normal = Vector3.up;
            int count = Physics.RaycastNonAlloc(seed + Vector3.up * 3, Vector3.down,
                hits, 6, ~0, QueryTriggerInteraction.Ignore);
            int rank = 0; float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = hits[i];
                if (hit.rigidbody || hit.normal.y < .6f) continue;
                var t = hit.collider.transform;
                int candidate = (roads && t.parent == roads) || (streets && t.parent == streets)
                    ? 2 : hit.collider is TerrainCollider ? 1 : 0;
                if (candidate == 0 || candidate < rank || (candidate == rank && hit.point.y <= best)) continue;
                rank = candidate; best = hit.point.y; point = hit.point; normal = hit.normal;
            }
            return rank > 0;
        }
    }

    /// A directed, finite lane route. Points already include the package's turn connector.
    [Serializable]
    public class TrafficSideRoute
    {
        public string name, town;
        public int site, source, corridor, lane, indicator;
        public bool inbound;
        public float mainS, yieldS, conflictStart, conflictEnd, gapSeconds = 4;
        public Vector3[] points;
        public float[] arc, speed;
        public TrafficStop signal;
        public float Length => arc[arc.Length - 1];

        public int Sample(float s)
        {
            int lo = 0, hi = arc.Length - 1;
            while (hi - lo > 1) { int m = (lo + hi) / 2; if (arc[m] <= s) lo = m; else hi = m; }
            return lo;
        }
        public Vector3 Point(float s)
        {
            int i = Sample(s);
            return Vector3.Lerp(points[i], points[i + 1], Mathf.Clamp01((s - arc[i]) / Mathf.Max(.001f, arc[i + 1] - arc[i])));
        }
    }
}
