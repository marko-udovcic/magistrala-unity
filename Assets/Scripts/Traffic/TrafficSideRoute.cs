using System;
using UnityEngine;

namespace Magistrala.Traffic
{
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
