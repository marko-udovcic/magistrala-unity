// Finite side streets from our lane package. Never invent a straight line through a junction.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Magistrala.Traffic;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class TrafficSideBuilder
    {
        // GraphRoad currently ends at the unnamed through edge of the Krešimira crossroads (~game x -4419).
        // Continue the two eastern old-road lanes using the same package that supplies the side streets.
        // These are lane centres already, so lane offset is zero. The s13 works gap is retained.
        public static TrafficCorridor[] EasternMain()
        {
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText("Assets/Editor/sporedne-routes.json")));
            var pa = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "paket-approaches.json")));
            var tr = MiniJson.O(MiniJson.O(pa["alignment"])["transform"]);
            double A(string k) => MiniJson.D(tr[k]);
            var result = new List<TrafficCorridor>();
            foreach (var path in MiniJson.L(root["easternMain"]))
            {
                var raw = new List<Vector3>();
                foreach (var point in MiniJson.L(path))
                {
                    var p = MiniJson.L(point); double x = MiniJson.D(p[0]), z = MiniJson.D(p[2]);
                    raw.Add(W.U(A("a") * x + A("b") * z + A("tx"), MiniJson.D(p[1]), A("c") * x + A("d") * z + A("tz")));
                }
                var pts = new List<Vector3> { raw[0] }; float carry = 0;
                for (int i = 1; i < raw.Count; i++)
                {
                    float length = Vector3.Distance(raw[i - 1], raw[i]);
                    if (length < .001f) continue;
                    float d = 2 - carry;
                    for (; d <= length; d += 2) pts.Add(Vector3.Lerp(raw[i - 1], raw[i], d / length));
                    carry = length - (d - 2);
                }
                int n = pts.Count;
                var c = new TrafficCorridor { name = result.Count == 0 ? "Tuđmana istok (Sućurac)" : "Tuđmana zapad (Lukšić)",
                    step = 2, maxLanes = 1, axis = pts.ToArray(), right = new Vector3[n], lanes = new byte[n],
                    laneOff = new float[n], laneY = new float[n], vmax = new float[n], deadEnd = result.Count == 1 };
                for (int i = 0; i < n; i++)
                {
                    var p = c.axis[i]; float closest = 3;
                    foreach (var hit in Physics.RaycastAll(p + Vector3.up * 3, Vector3.down, 6, ~0, QueryTriggerInteraction.Ignore))
                    {
                        float d = Mathf.Abs(hit.point.y - p.y);
                        if (!hit.rigidbody && d < closest) { closest = d; c.axis[i].y = hit.point.y; }
                    }
                    c.lanes[i] = 1; c.laneY[i] = c.axis[i].y;
                    var tangent = pts[Mathf.Min(i + 2, n - 1)] - pts[Mathf.Max(0, i - 2)]; tangent.y = 0;
                    c.right[i] = Vector3.Cross(Vector3.up, tangent.normalized);
                    var a = pts[i] - pts[Mathf.Max(0, i - 5)]; var b = pts[Mathf.Min(n - 1, i + 5)] - pts[i];
                    c.vmax[i] = Mathf.Min(50 / 3.6f, Mathf.Sqrt(2.2f / Mathf.Max(.001f, Vector3.Angle(a, b) * Mathf.Deg2Rad / 10)));
                }
                result.Add(c);
            }
            return result.ToArray();
        }

        public static TrafficSideRoute[] Build(TrafficCorridor[] corridors, TrafficPlan[] plans,
            List<(Vector2 p, Vector2 dir, int plan, string group, string model)> stops)
        {
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText("Assets/Editor/sporedne-routes.json")));
            var pa = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "paket-approaches.json")));
            var tr = MiniJson.O(MiniJson.O(pa["alignment"])["transform"]);
            double A(string k) => MiniJson.D(tr[k]);
            Vector3 U(List<object> p)
            {
                double x = MiniJson.D(p[0]), z = MiniJson.D(p[2]);
                return W.U(A("a") * x + A("b") * z + A("tx"), MiniJson.D(p[1]), A("c") * x + A("d") * z + A("tz"));
            }
            var result = new List<TrafficSideRoute>();
            foreach (var obj in MiniJson.L(root["routes"]))
            {
                var o = MiniJson.O(obj);
                var pts = MiniJson.L(o["points"]).Select(p => U(MiniJson.L(p))).ToList();
                for (int i = pts.Count - 1; i > 0; i--) if ((pts[i] - pts[i - 1]).sqrMagnitude < .0001f) pts.RemoveAt(i);
                bool inbound = (bool)o["inbound"], d8 = (bool)o["d8"];
                int end = inbound ? pts.Count - 1 : 0;
                var heading = inbound ? pts[end] - pts[Mathf.Max(0, end - 2)] : pts[Mathf.Min(2, pts.Count - 1)] - pts[0];
                // A turn's tangent at the main-road end must agree with the target corridor.
                heading.y = 0; heading.Normalize();
                int bc = -1, bl = 0; float bs = 0, best = 6.0f;
                Vector3 bp = Vector3.zero;
                for (int ci = 0; ci < corridors.Length; ci++)
                {
                    var c = corridors[ci]; if (c.name.StartsWith("D8") != d8) continue;
                    for (int i = 2; i < c.axis.Length - 2; i++)
                    {
                        var t = c.axis[i + 1] - c.axis[i - 1]; t.y = 0;
                        if (Vector3.Dot(t.normalized, heading) < .7f) continue;
                        for (int lane = 0; lane < c.lanes[i]; lane++)
                        {
                            var q = c.axis[i] + c.right[i] * c.laneOff[i * c.maxLanes + lane];
                            q.y = c.laneY[i * c.maxLanes + lane];
                            float dist = Vector2.Distance(new Vector2(q.x, q.z), new Vector2(pts[end].x, pts[end].z));
                            if (dist >= best || Mathf.Abs(q.y - pts[end].y) > 4) continue;
                            best = dist; bc = ci; bl = lane; bs = i * c.step; bp = q;
                        }
                    }
                }
                if (bc < 0) { Debug.LogWarning($"[sporedne] rejected source {o["source"]}: main lane >6 m away / wrong heading or level"); continue; }
                // Spread the small alignment correction over the connector, with exactly matching handoff position.
                Vector3 delta = bp - pts[end];
                var original = pts.ToArray();
                float walked = 0;
                for (int n = 0; n < pts.Count; n++)
                {
                    int i = inbound ? end - n : n;
                    if (n > 0) walked += Vector3.Distance(original[i], original[inbound ? i + 1 : i - 1]);
                    pts[i] += delta * Mathf.Clamp01(1 - walked / 15);
                }
                pts[end] = bp;
                // Follow the imported road surface, not an elevated roof or a bridge on another level.
                for (int i = 0; i < pts.Count; i++)
                {
                    if (i == end) continue;
                    var p = pts[i]; float dy = 3, y = p.y;
                    foreach (var hit in Physics.RaycastAll(p + Vector3.up * 3, Vector3.down, 6, ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.rigidbody) continue;
                        float d = Mathf.Abs(hit.point.y - p.y);
                        if (d < dy) { dy = d; y = hit.point.y; }
                    }
                    p.y = y; pts[i] = p;
                }
                var r = new TrafficSideRoute {
                    name = (string)o["name"], town = (string)o["town"], source = (int)MiniJson.D(o["source"]),
                    site = (int)MiniJson.D(o["site"]), inbound = inbound, corridor = bc, lane = bl,
                    indicator = (int)MiniJson.D(o["indicator"]), mainS = bs, points = pts.ToArray(),
                    arc = new float[pts.Count], speed = new float[pts.Count]
                };
                for (int i = 1; i < pts.Count; i++) r.arc[i] = r.arc[i - 1] + Vector3.Distance(pts[i - 1], pts[i]);
                float connector = (float)MiniJson.D(o["connectorLength"]);
                r.conflictStart = inbound ? Mathf.Max(0, r.Length - connector) : 0;
                r.conflictEnd = inbound ? r.Length : connector;
                r.yieldS = inbound ? Mathf.Max(0, r.conflictStart - 2) : 0;
                for (int i = 0; i < pts.Count; i++)
                {
                    float s = r.arc[i]; var a = r.Point(s) - r.Point(s - 4); var b = r.Point(s + 4) - r.Point(s);
                    float curvature = Vector3.Angle(a, b) * Mathf.Deg2Rad / 4;
                    r.speed[i] = Mathf.Min(8.3f, Mathf.Sqrt(1.7f / Mathf.Max(.001f, curvature)));
                    if (s >= r.conflictStart - 8 && s <= r.conflictEnd + 8) r.speed[i] = Mathf.Min(r.speed[i], 4);
                }
                string group = (string)o["signal"], model = (string)o["model"];
                // Model connectors carry their own signal group; match by imported model filename prefix.
                if (group.Length > 0)
                {
                    string prefix = model.Split('-')[0];
                    string planKey = prefix.StartsWith("s") || prefix.StartsWith("d") ? "dionica-" + prefix : "krizanje-" + prefix;
                    int plan = Array.FindIndex(plans, p => p.key == planKey);
                    if (plan < 0) throw new InvalidDataException("[sporedne] missing signal plan for " + model);
                    r.signal = new TrafficStop { plan = plan, group = group, model = model };
                }
                // Generic signals without models: match the side approach's actual stop line and heading.
                if (inbound && r.signal == null)
                {
                    float nearest = 10;
                    foreach (var st in stops)
                        for (int i = 1; i < pts.Count; i++)
                        {
                            if (r.arc[i] > r.conflictStart + 1) continue;
                            var g = W.G(pts[i]); var tg = W.G(pts[i]) - W.G(pts[i - 1]);
                            float dist = Vector2.Distance(g, st.p);
                            if (dist >= nearest || Vector2.Dot(tg.normalized, st.dir) < .7f) continue;
                            nearest = dist;
                            r.signal = new TrafficStop { plan = st.plan, group = st.group, model = st.model };
                            r.yieldS = Mathf.Min(r.yieldS, Mathf.Max(0, r.arc[i] - 1));
                        }
                }
                result.Add(r);
            }
            int sites = result.Select(r => r.site).Distinct().Count();
            if (sites != 20) throw new InvalidDataException($"[sporedne] only {sites}/20 mouths match the built corridors. Review rejected source IDs before saving.");
            Debug.Log($"[sporedne] built {sites} mouths, {result.Count(r => r.inbound)} entry routes, {result.Count(r => !r.inbound)} exit routes; assumed lanes, not surveyed");
            return result.ToArray();
        }
    }
}
