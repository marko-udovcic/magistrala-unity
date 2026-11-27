// No post on the asphalt (26. 11., mag-25-11 točka 5, znakovi_provjera.json): the carriageway as drawn – the AsphaltFlat submeshes
// of World/Roads (junction models, road tiles) and World/Streets (our streets with their junction fillets) – not the graph's
// straight half widths, which leave the rounded corners of a junction mouth out (street-name plates, crossing signs and posts of
// the side streets stood on the asphalt there). Indexed lazily per renderer (only the meshes a post is tested against).
//   * Clear (every generated roadside post: approach / crossing / limit / place / street-name / road signs, the no-model signals)
//     goes on out until the post is also 0.3 m off the drawn asphalt
//   * the owner's props (junction-overrides-modeli.json, his positions): a post stack on the asphalt moves to the nearest spot off
//     it (≤ 3 m, heads and plates with it); a gantry leg on the asphalt goes out along the truss (the span grows, the heads stay
//     over their lanes); a cantilever mast goes back behind its arm (the arm grows, the heads stay) – s04, 04 Sućurac, 19
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static partial class SignsBuilder
    {
        sealed class AsphMesh { public Bounds b; public float[] t; public Dictionary<Vector2Int, List<int>> g; }
        static List<(Renderer r, AsphMesh m)> asph;
        public static int nOffAsphalt;
        const float AG = 4;

        static void IndexAsphalt()
        {
            asph = new List<(Renderer, AsphMesh)>(); nOffAsphalt = 0;
            foreach (var name in new[] { "Roads", "Streets" })
            {
                var root = GameObject.Find("World/" + name); if (!root) continue;
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                {
                    bool any = false; foreach (var m in r.sharedMaterials) if (m && m.name.StartsWith("Asphalt")) any = true;
                    if (any) asph.Add((r, null));
                }
            }
        }
        static AsphMesh Load(Renderer r)
        {
            var am = new AsphMesh { b = r.bounds, g = new Dictionary<Vector2Int, List<int>>() };
            var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null; var t = new List<float>();
            if (mesh)
            {
                var v = mesh.vertices; var M = r.transform.localToWorldMatrix; var mats = r.sharedMaterials;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    if (!mats[s] || !mats[s].name.StartsWith("Asphalt")) continue;
                    var tri = mesh.GetTriangles(s);
                    for (int i = 0; i + 2 < tri.Length; i += 3)
                    {
                        Vector3 a = M.MultiplyPoint3x4(v[tri[i]]), b = M.MultiplyPoint3x4(v[tri[i + 1]]), c = M.MultiplyPoint3x4(v[tri[i + 2]]);
                        int id = t.Count / 6; t.Add(a.x); t.Add(a.z); t.Add(b.x); t.Add(b.z); t.Add(c.x); t.Add(c.z);
                        float x0 = Mathf.Min(a.x, Mathf.Min(b.x, c.x)), x1 = Mathf.Max(a.x, Mathf.Max(b.x, c.x)), z0 = Mathf.Min(a.z, Mathf.Min(b.z, c.z)), z1 = Mathf.Max(a.z, Mathf.Max(b.z, c.z));
                        for (int gx = Mathf.FloorToInt(x0 / AG); gx <= Mathf.FloorToInt(x1 / AG); gx++)
                            for (int gz = Mathf.FloorToInt(z0 / AG); gz <= Mathf.FloorToInt(z1 / AG); gz++)
                            { var k = new Vector2Int(gx, gz); if (!am.g.TryGetValue(k, out var l)) am.g[k] = l = new List<int>(); l.Add(id); }
                    }
                }
            }
            am.t = t.ToArray(); return am;
        }
        static bool InTri(float[] t, int i, float x, float z)
        {
            int o = i * 6; float ax = t[o], az = t[o + 1], bx = t[o + 2], bz = t[o + 3], cx = t[o + 4], cz = t[o + 5];
            float d1 = (x - bx) * (az - bz) - (ax - bx) * (z - bz), d2 = (x - cx) * (bz - cz) - (bx - cx) * (z - cz), d3 = (x - ax) * (cz - az) - (cx - ax) * (z - az);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
        static bool AsphaltAt(float x, float z)
        {
            if (asph == null) return false;
            for (int i = 0; i < asph.Count; i++)
            {
                var (r, m) = asph[i];
                var b = m != null ? m.b : r.bounds;
                if (x < b.min.x || x > b.max.x || z < b.min.z || z > b.max.z) continue;
                if (m == null) { m = Load(r); asph[i] = (r, m); }
                if (m.g.TryGetValue(new Vector2Int(Mathf.FloorToInt(x / AG), Mathf.FloorToInt(z / AG)), out var l))
                    foreach (int k in l) if (InTri(m.t, k, x, z)) return true;
            }
            return false;
        }
        /// p (Unity) or a point within r of it on the drawn asphalt
        static bool OnAsphalt(Vector3 p, float r = .3f)
        {
            if (AsphaltAt(p.x, p.z)) return true;
            if (r <= 0) return false;
            for (int k = 0; k < 8; k++) { float a = k * Mathf.PI / 4; if (AsphaltAt(p.x + Mathf.Cos(a) * r, p.z + Mathf.Sin(a) * r)) return true; }
            return false;
        }
        /// for other builders (PropsBuilder.Lamps): index World/Roads + World/Streets as they are now, then DrawnAsphalt
        public static void IndexDrawnAsphalt() => IndexAsphalt();
        /// game (gx, gz) or a point within r of it on the drawn asphalt (IndexDrawnAsphalt first)
        public static bool DrawnAsphalt(double gx, double gz, float r) => OnAsphalt(W.U(gx, 0, gz), r);
        /// the nearest spot within maxD of p that is off the asphalt (rings of 0.25 m, 16 directions); null: none
        static Vector3? OffAsphalt(Vector3 p, float maxD = 3f, float r = .3f)
        {
            if (!OnAsphalt(p, r)) return p;
            for (float d = .25f; d <= maxD + 1e-3f; d += .25f)
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI / 8; var q = p + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * d;
                    if (!OnAsphalt(q, r)) return q;
                }
            return null;
        }
        // Exact disc/triangle overlap for the emergency signal placement search. The older
        // eight-point OnAsphalt probe stays unchanged so existing clear placements remain identical.
        static float AsphaltEdgeDistance2(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float u = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude));
            return (p - (a + ab * u)).sqrMagnitude;
        }
        static bool AsphaltDiscHitsTriangle(float[] triangles, int id, Vector2 p, float radius2)
        {
            int o = id * 6;
            var a = new Vector2(triangles[o], triangles[o + 1]);
            var b = new Vector2(triangles[o + 2], triangles[o + 3]);
            var c = new Vector2(triangles[o + 4], triangles[o + 5]);
            float area = (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
            if (Mathf.Abs(area) > 1e-8f && InTri(triangles, id, p.x, p.y)) return true;
            return AsphaltEdgeDistance2(p, a, b) <= radius2 || AsphaltEdgeDistance2(p, b, c) <= radius2 ||
                AsphaltEdgeDistance2(p, c, a) <= radius2;
        }
        static bool AsphaltDiscBlocked(Vector3 p, float clearance)
        {
            // All Roads/Streets asphalt submeshes, including narrow corners between the old probe rays.
            if (asph == null) return true; // fail closed when no index was prepared
            var q = new Vector2(p.x, p.z);
            for (int i = 0; i < asph.Count; i++)
            {
                var (r, m) = asph[i]; var bounds = m != null ? m.b : r.bounds;
                if (p.x + clearance < bounds.min.x || p.x - clearance > bounds.max.x ||
                    p.z + clearance < bounds.min.z || p.z - clearance > bounds.max.z) continue;
                if (m == null) { m = Load(r); asph[i] = (r, m); }
                for (int x = Mathf.FloorToInt((p.x - clearance) / AG); x <= Mathf.FloorToInt((p.x + clearance) / AG); x++)
                    for (int z = Mathf.FloorToInt((p.z - clearance) / AG); z <= Mathf.FloorToInt((p.z + clearance) / AG); z++)
                        if (m.g.TryGetValue(new Vector2Int(x, z), out var ids))
                            foreach (int id in ids)
                                if (AsphaltDiscHitsTriangle(m.t, id, q, clearance * clearance)) return true;
            }
            return false;
        }
        // Only after the old Clear/OffAsphalt chain failed. Search outward up to 4 m past
        // the nominal kerb and +/- 8 m along the road, on expanding quarter-metre rings.
        static Vector3? SignalPoleFallback(Vector3 kerb, Vector3 right, Vector3 travel)
        {
            for (int ring = 0; ring <= 32; ring++)
                for (int outward = 0; outward <= Mathf.Min(16, ring); outward++)
                    for (int along = 0; along <= ring; along++)
                    {
                        if (Mathf.Max(outward, along) != ring) continue;
                        var q = kerb + right * (outward * .25f) + travel * (along * .25f);
                        if (!AsphaltDiscBlocked(q, .4f)) return q;
                        if (along == 0) continue;
                        q = kerb + right * (outward * .25f) - travel * (along * .25f);
                        if (!AsphaltDiscBlocked(q, .4f)) return q;
                    }
            return null;
        }
        /// how far p must go along dir (unit, horizontal) to be off the asphalt (≤ maxD); -1: not within maxD
        static float OutAlong(Vector3 p, Vector3 dir, float maxD = 4f, float r = .3f)
        {
            dir.y = 0; dir.Normalize();
            for (float s = 0; s <= maxD + 1e-3f; s += .25f) if (!OnAsphalt(p + dir * s, r)) return s;
            return -1;
        }
    }
}
