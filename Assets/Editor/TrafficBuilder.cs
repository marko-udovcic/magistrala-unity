// Magistrala/Build traffic: AI traffic for the D8 between Lukšić and Sućurac and the old road Štafilić – Lukšić (two-way, mag-11-19)
// (runtime: Assets/Scripts/Traffic/TrafficSystem.cs).
//   Corridors – one per direction of the D8, chained from the finished road models' lane axes (model-junctions.json `roads`:
//               carriageway axis in the traffic direction, width w, n lanes; lane k centre at w/2 − (k + ½)·w/n right of it) like
//               TruckSetup.EastboundPath, resampled every 2 m, smoothed, heights by raycasts on the road colliders in the scene.
//               Roads with n ≥ 3: the lanes their mesh has at each point (LaneProfile — 53 / 54: the third lane only near the junction).
//   Signals   – the models' signal plans (krizanje-01 … 05): the D8 approach edges of each signal group (junction-overrides-modeli.json),
//               their painted stop lines (signals.stops) projected onto the corridors.
//   Kinds     – the web game's traffic cars (Assets/Models/Vehicles/Traffic/*.glb, decoded from the web GLBs by Tools/decode_traffic_glb.py),
//               split into (mesh, submesh, material) parts for GPU instancing; instancing copies of the materials and one paint material
//               per body colour in Assets/Generated/Traffic.
// Data: Assets/Generated/Traffic/TrafficData.asset; the scene gets a "Traffic" object with the TrafficSystem.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Magistrala.Game;
using Magistrala.Traffic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class TrafficBuilder
    {
        const string OUT = "Assets/Generated/Traffic";
        const float STEP = 2f;

        // ------------------------------------------------------------------ kinds (client/src/world2/traffic/kinds.ts)
        static readonly (string c, float w)[] STD = { ("#e8e8e6", 24), ("#6f7377", 14), ("#3c4045", 7), ("#151618", 17), ("#b9bcc0", 13), ("#1f3a68", 7), ("#8e1a16", 6), ("#7a6a55", 2), ("#2f4a35", 2) };
        static readonly (string c, float w)[] OLD = { ("#a8332a", 14), ("#e6e2d6", 14), ("#d9cfae", 8), ("#7a8fa3", 8), ("#2f4a35", 6), ("#6b2a22", 5), ("#8a8d8f", 8), ("#2a3f63", 6) };
        static readonly (string c, float w)[] PREMIUM = { ("#131416", 30), ("#3a3f46", 20), ("#252c38", 12), ("#e9e9e8", 12), ("#a9adb2", 12), ("#1b2a4a", 8) };
        static readonly (string c, float w)[] KOMBI = { ("#e6e6e2", 30), ("#8fa1b3", 14), ("#c9bd9a", 10), ("#7b2a24", 8), ("#4b5e46", 6), ("#9a9c9e", 12), ("#2e4b77", 8) };
        static readonly (string c, float w)[] SMALL = { ("#e8e8e6", 20), ("#6f7377", 12), ("#151618", 12), ("#b9bcc0", 13), ("#1f3a68", 8), ("#8e1a16", 9), ("#d0682b", 4), ("#e2c23c", 4) };
        static readonly (string c, float w)[] CROSS = { ("#e8e8e6", 18), ("#6f7377", 14), ("#151618", 12), ("#b9bcc0", 12), ("#c4521e", 12), ("#1f3a68", 10), ("#8e1a16", 8), ("#4b5e46", 5) };
        static readonly (string c, float w)[] VAN = { ("#f2f2f0", 70), ("#b9bcc0", 15), ("#6f7377", 8), ("#1f3a68", 7) };
        static readonly (string c, float w)[] WHITE = { ("#f2f2f0", 1) };
        static readonly (string c, float w)[] COACH = { ("#cfd2d6", 1) };

        struct KindSpec { public string name; public float w, len, hw, h, vMul, a, b, T; public (string c, float w)[] colors; }
        static KindSpec K(string n, float w, float len, float hw, float h, float vMul, float a, float b, float T, (string, float)[] col) =>
            new KindSpec { name = n, w = w, len = len, hw = hw, h = h, vMul = vMul, a = a, b = b, T = T, colors = col };
        static readonly KindSpec[] KINDS =
        {
            K("golub", 20, 4.05f, .88f, 1.46f, 1.0f, 1.7f, 3.2f, 1.4f, STD),
            K("oktan", 18, 4.67f, .9f, 1.46f, 1.03f, 1.9f, 3.3f, 1.3f, STD),
            K("sendo", 14, 4.1f, .88f, 1.57f, 1.0f, 1.6f, 3.2f, 1.4f, CROSS),
            K("jugic", 8, 3.49f, .78f, 1.39f, .88f, 1.0f, 2.8f, 1.6f, OLD),
            K("punat", 10, 4.03f, .85f, 1.48f, .95f, 1.3f, 3.0f, 1.5f, SMALL),
            K("prolaz", 11, 4.77f, .9f, 1.55f, 1.02f, 1.8f, 3.3f, 1.3f, STD),
            K("tusan", 8, 4.5f, .93f, 1.68f, 1.03f, 1.9f, 3.4f, 1.3f, CROSS),
            K("trojka", 5, 4.71f, .92f, 1.43f, 1.1f, 2.4f, 3.7f, 1.0f, PREMIUM),
            K("benzer", 5, 4.94f, .93f, 1.45f, 1.05f, 2.0f, 3.5f, 1.2f, PREMIUM),
            K("x6", 3, 4.95f, .97f, 1.8f, 1.08f, 2.2f, 3.6f, 1.1f, PREMIUM),
            K("tesna", 3, 4.75f, .94f, 1.44f, 1.05f, 2.6f, 3.6f, 1.2f, new[] { ("#e9e9e8", 30f), ("#151618", 20f), ("#6f7377", 18f), ("#8e1a16", 12f), ("#1f3a68", 12f) }),
            K("boxtruck", 4, 6.9f, 1.1f, 3.25f, .92f, 1.0f, 2.8f, 1.6f, WHITE),
            K("van", 10, 5.93f, 1.02f, 2.65f, .95f, 1.2f, 3.0f, 1.5f, VAN),
            K("kombi", 8, 4.78f, .93f, 1.96f, .92f, 1.1f, 2.8f, 1.6f, KOMBI),
            K("coach", 3, 12.2f, 1.28f, 3.55f, .95f, .8f, 2.2f, 1.8f, COACH),
        };

        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = Path.GetDirectoryName(p).Replace('\\', '/'); Dir(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(p)); } }

        static Material InstMat(Material src, string path, Color? paint = null)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(src); AssetDatabase.CreateAsset(m, path); }
            else { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
            m.name = Path.GetFileNameWithoutExtension(path);
            if (paint.HasValue && m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", paint.Value);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// the decoded web GLBs face glTF forward −Z (head lamps at z −1.7 … −6.1, tail lamps at +z, every kind and its lod1), the
        /// TrafficSystem pose is LookRotation(fwd) = +Z forward: turned 180° about y here (parts and far mesh) — the cars drove rear first
        static readonly Matrix4x4 FACE = Matrix4x4.Rotate(Quaternion.Euler(0, 180, 0));

        static TrafficPart[] Parts(GameObject prefab, Func<Material, Material> mat, Func<Material, int, Material> lit)
        {
            var parts = new List<TrafficPart>();
            var root = FACE * prefab.transform.worldToLocalMatrix;
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                var mats = r.sharedMaterials;
                for (int s = 0; s < mf.sharedMesh.subMeshCount && s < mats.Length; s++)
                {
                    if (!mats[s]) continue;
                    var nm = mats[s].name;
                    int lamp = nm == "T_tail" ? 1 : nm == "T_amberL" ? 2 : nm == "T_amberR" ? 3 : nm == "T_head" ? 4 : 0;
                    parts.Add(new TrafficPart
                    {
                        mesh = mf.sharedMesh, sub = s, mat = nm == "paint" ? null : mat(mats[s]), paint = nm == "paint", local = root * r.transform.localToWorldMatrix,
                        lamp = lamp, matOn = lamp > 0 ? lit(mats[s], lamp) : null,
                        wheel = r.name.StartsWith("wheelmesh"), radius = r.name.StartsWith("wheelmesh") ? Mathf.Max(.2f, r.transform.position.y - prefab.transform.position.y) : 0,
                    });
                }
            }
            return parts.ToArray();
        }

        static readonly bool OLD_KINDS = false;
        // old kinds kept while OLD_KINDS is false: the bus (no new model yet; the box truck is replaced by dz_lct3000_07)
        static readonly string[] KEEP_OLD = { "coach" };
        static Palette pal;
        static TrafficKind[] Kinds()
        {
            Dir(OUT + "/Materials");
            pal = new Palette();
            var list = new List<TrafficKind>();
            // Marko 2026-11-22: only the downloaded candidates drive / park ("ove lose izbacit sve"); the web kinds (golub … van, kombi)
            // and the old Tripo police / ambulance stay in the project, back with OLD_KINDS = true (the bus: KEEP_OLD)
            foreach (var k in OLD_KINDS ? KINDS : KINDS.Where(k => KEEP_OLD.Contains(k.name)).ToArray())
            {
                var p0 = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Vehicles/Traffic/{k.name}.glb");
                var p1 = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Vehicles/Traffic/{k.name}_lod1.glb");
                if (!p0) { Debug.LogWarning("[traffic] missing model " + k.name); continue; }
                var cache = new Dictionary<string, Material>();
                Material paintSrc = null;
                Func<Material, Material> M = src =>
                {
                    if (!cache.TryGetValue(src.name, out var m)) cache[src.name] = m = InstMat(src, $"{OUT}/Materials/{k.name}_{src.name}.mat");
                    return m;
                };
                foreach (var r in p0.GetComponentsInChildren<MeshRenderer>()) foreach (var m in r.sharedMaterials) if (m && m.name == "paint") paintSrc = m;
                // lit lamps: brake light red, indicators amber (emissive, HDR so bloom picks them up)
                Func<Material, int, Material> L = (src, lamp) =>
                {
                    string key = src.name + "_on";
                    if (!cache.TryGetValue(key, out var m))
                    {
                        cache[key] = m = InstMat(src, $"{OUT}/Materials/{k.name}_{key}.mat");
                        var e = lamp == 1 ? new Color(6f, .25f, .15f) : lamp == 4 ? new Color(5f, 4.8f, 4.3f) : new Color(6f, 2.6f, .3f);
                        if (m.HasProperty("emissiveFactor")) m.SetColor("emissiveFactor", e);
                        m.EnableKeyword("_EMISSION"); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                        EditorUtility.SetDirty(m);
                    }
                    return m;
                };
                var kd = new TrafficKind
                {
                    name = k.name, len = k.len, halfW = k.hw, h = k.h, vMul = k.vMul, a = k.a, b = k.b, T = k.T, weight = k.w,
                    lod0 = Parts(p0, M, L), lod1 = p1 ? Parts(p1, M, L) : null,
                };
                var paints = new List<Material>(); var pw = new List<float>();
                if (paintSrc)
                    foreach (var (c, w) in k.colors)
                    {
                        ColorUtility.TryParseHtmlString(c, out var col);
                        paints.Add(InstMat(paintSrc, $"{OUT}/Materials/{k.name}_paint_{c.Substring(1)}.mat", col)); pw.Add(w);
                    }
                kd.paints = paints.ToArray(); kd.paintW = pw.ToArray();
                if (p1)
                {
                    kd.farMesh = FarMesh(k.name, p1, pal);
                    kd.farPaints = k.colors.Select(cw => { ColorUtility.TryParseHtmlString(cw.c, out var col); return Urp($"far_paint_{cw.c.Substring(1)}", col, .62f); }).ToArray();
                    kd.farTail = Urp("far_tail", new Color(.55f, .08f, .07f), .5f);
                    kd.farTailOn = Urp("far_tail_on", new Color(.8f, .1f, .08f), .5f, new Color(6f, .25f, .15f));
                }
                list.Add(kd);
            }
            // the Tripo cars (Magistrala/Auti: uvezi Tripo modele): their own parts / LODs, far mesh colours onto the shared palette
            if (OLD_KINDS) list.AddRange(TripoCarImporter.TrafficKinds(c => pal.UV(c), (n, c, sm, e) => Urp(n, c, sm, e)));
            // the downloaded candidates (Magistrala/Auti: uvezi kandidata, Assets/Editor/auti-kandidati.json)
            list.AddRange(KandidatImporter.TrafficKinds(c => pal.UV(c), (n, c, sm, e) => Urp(n, c, sm, e)));
            return list.ToArray();
        }

        // ------------------------------------------------------------------ far LOD (merged lod1, palette material)
        class Palette
        {
            public readonly List<Color> cols = new List<Color>();
            public const int N = 32;
            public Vector2 UV(Color c)
            {
                int i = cols.FindIndex(q => Mathf.Abs(q.r - c.r) + Mathf.Abs(q.g - c.g) + Mathf.Abs(q.b - c.b) < .01f);
                if (i < 0) { i = cols.Count; cols.Add(c); }
                return new Vector2((i % N + .5f) / N, (i / N + .5f) / N);
            }
        }

        static Material Urp(string name, Color c, float smooth, Color? emit = null, Texture2D tex = null)
        {
            string path = $"{OUT}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0);
            if (tex) m.SetTexture("_BaseMap", tex);
            if (emit.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            m.enableInstancing = true; EditorUtility.SetDirty(m);
            return m;
        }

        static Mesh FarMesh(string kind, GameObject prefab, Palette pal)
        {
            var V = new List<Vector3>(); var Nn = new List<Vector3>(); var UV = new List<Vector2>(); var T = new[] { new List<int>(), new List<int>(), new List<int>() };
            var root = FACE * prefab.transform.worldToLocalMatrix;
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>())
            {
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                var mesh = mf.sharedMesh; var mats = r.sharedMaterials; var M = root * r.transform.localToWorldMatrix;
                var v = mesh.vertices; var n = mesh.normals;
                for (int s = 0; s < mesh.subMeshCount && s < mats.Length; s++)
                {
                    if (!mats[s]) continue;
                    string nm = mats[s].name;
                    int k = nm == "paint" ? 1 : nm == "T_tail" ? 2 : 0;
                    Color c = mats[s].HasProperty("baseColorFactor") ? mats[s].GetColor("baseColorFactor") : Color.gray;
                    if (mats[s].HasProperty("baseColorTexture") && mats[s].GetTexture("baseColorTexture")) c = new Color(.32f, .32f, .33f);
                    if (nm == "glass") c = new Color(.1f, .12f, .14f);
                    c.a = 1;
                    var uv = pal.UV(c);
                    var tri = mesh.GetTriangles(s); var map = new Dictionary<int, int>();
                    foreach (int i in tri)
                    {
                        if (!map.TryGetValue(i, out int j))
                        {
                            j = V.Count; map[i] = j;
                            V.Add(M.MultiplyPoint3x4(v[i])); Nn.Add(n != null && n.Length > i ? M.MultiplyVector(n[i]).normalized : Vector3.up); UV.Add(uv);
                        }
                        T[k].Add(j);
                    }
                }
            }
            // mirrored node scales flip the winding (glTFast's left wheels): fix triangles whose face normal opposes the vertex normals
            for (int k = 0; k < 3; k++)
                for (int t = 0; t + 2 < T[k].Count; t += 3)
                {
                    int a = T[k][t], b = T[k][t + 1], c = T[k][t + 2];
                    var fn = Vector3.Cross(V[b] - V[a], V[c] - V[a]);
                    if (Vector3.Dot(fn, Nn[a] + Nn[b] + Nn[c]) < 0) { T[k][t + 1] = c; T[k][t + 2] = b; }
                }
            var m = new Mesh { name = kind + "_far", indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            m.SetVertices(V); m.SetNormals(Nn); m.SetUVs(0, UV); m.subMeshCount = 3;
            for (int k = 0; k < 3; k++) m.SetTriangles(T[k], k);
            m.RecalculateBounds();
            Dir(OUT + "/Meshes");
            string path = $"{OUT}/Meshes/{kind}_far.asset";
            if (AssetDatabase.LoadAssetAtPath<Mesh>(path)) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static Material PaletteMat(Palette pal)
        {
            string path = OUT + "/Materials/far_palette.png";
            var tex = new Texture2D(Palette.N, Palette.N, TextureFormat.RGBA32, false);
            var px = new Color[Palette.N * Palette.N];
            for (int i = 0; i < px.Length; i++) px[i] = i < pal.cols.Count ? pal.cols[i] : Color.magenta;
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.filterMode = FilterMode.Point; imp.mipmapEnabled = false; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.sRGBTexture = true; imp.SaveAndReimport();
            return Urp("far_palette", Color.white, .45f, null, AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }

        // ------------------------------------------------------------------ corridors
        class Piece { public List<double[]> p; public double w; public int n; public LaneProfile.Profile prof; }

        static List<Piece> Roads(List<ModelJ> models)
        {
            var roads = new List<Piece>();
            foreach (var m in models) for (int ri = 0; ri < m.roads.Count; ri++) { var r = m.roads[ri]; if (r.n >= 2 && r.lvl == 0 && r.p.Count >= 2) roads.Add(new Piece { p = r.p, w = r.w, n = r.n, prof = LaneProfile.Get(m, ri) }); }
            return roads;
        }

        /// the road (other than `used`) with a segment within 6 m of (x, z) running along (dx, dz): its index and segment
        static (Piece r, int seg) Near(List<Piece> roads, HashSet<Piece> used, double x, double z, double dx, double dz)
        {
            double dl = Math.Sqrt(dx * dx + dz * dz) + 1e-9, best = 6; Piece br = null; int bs = -1;
            foreach (var r in roads)
            {
                if (used.Contains(r)) continue;
                for (int i = 0; i + 1 < r.p.Count; i++)
                {
                    var s = r.p[i]; var s1 = r.p[i + 1];
                    double qx = s1[0] - s[0], qz = s1[1] - s[1], ql2 = qx * qx + qz * qz; if (ql2 < 1e-4) continue;
                    double t = Math.Max(0, Math.Min(1, ((x - s[0]) * qx + (z - s[1]) * qz) / ql2));
                    double px = s[0] + qx * t - x, pz = s[1] + qz * t - z, d = Math.Sqrt(px * px + pz * pz);
                    double cos = (qx * dx + qz * dz) / (Math.Sqrt(ql2) * dl);
                    if (d < best && cos > .85) { best = d; br = r; bs = i; }
                }
            }
            return (br, bs);
        }

        /// chained road axis (game x, z) with the width / lane count of the piece each vertex belongs to
        static List<(double x, double z, double w, int n, Piece pc)> Chain(List<Piece> roads, Piece seed)
        {
            var used = new HashSet<Piece> { seed };
            var fwd = new List<(double, double, double, int, Piece)>();
            foreach (var q in seed.p) fwd.Add((q[0], q[1], seed.w, seed.n, seed));
            // forward
            var cur = seed; var tail = seed.p;
            for (int guard = 0; guard < 300; guard++)
            {
                var e = tail[tail.Count - 1]; var e0 = tail[tail.Count - 2];
                var (r, seg) = Near(roads, used, e[0], e[1], e[0] - e0[0], e[1] - e0[1]);
                if (r == null) break;
                used.Add(r);
                var rest = r.p.GetRange(seg + 1, r.p.Count - seg - 1);
                if (rest.Count == 0) continue;
                foreach (var q in rest) fwd.Add((q[0], q[1], r.w, r.n, r));
                tail = new List<double[]> { e }; tail.AddRange(rest);
                if (tail.Count < 2) break;
            }
            // backward from the seed's start
            var head = seed.p;
            for (int guard = 0; guard < 300; guard++)
            {
                var s = head[0]; var s1 = head[1];
                var (r, seg) = Near(roads, used, s[0], s[1], s1[0] - s[0], s1[1] - s[1]);
                if (r == null) break;
                used.Add(r);
                var pre = r.p.GetRange(0, seg + 1);
                // drop prefix points that lie ahead of the current start (the matched segment may straddle it)
                var ins = new List<(double, double, double, int, Piece)>();
                foreach (var q in pre) ins.Add((q[0], q[1], r.w, r.n, r));
                fwd.InsertRange(0, ins);
                head = new List<double[]>(pre) { s };
                if (head.Count < 2) break;
            }
            return fwd;
        }

        static float Hit(Vector3 p, float yPrev)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, yPrev + 2.5f, p.z), Vector3.down, 8f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN, bd = 99;
            foreach (var h in hits) { float d = Mathf.Abs(h.point.y - yPrev); if (d < bd) { bd = d; best = h.point.y; } }
            return best;
        }

        /// twoWay: the axis carries both directions (mag-11-19 old road, Cesta dr. F. Tuđmana: w 7, n 2): this corridor drives its one
        /// lane right of the axis, centre (w / n) / 2 — the w/2 − (k + ½)·w/n formula is for one-way carriageways; the other direction is
        /// the reversed chain
        static TrafficCorridor Corridor(string name, List<(double x, double z, double w, int n, Piece pc)> pts, List<ModelJ> models, bool twoWay = false, float yStart = float.NaN)
        {
            // resample every STEP m along the polyline (w, n of the segment's start vertex)
            var P = new List<Vector2>(); var Wd = new List<float>(); var N = new List<int>(); var Pc = new List<Piece>();
            P.Add(new Vector2((float)pts[0].x, (float)pts[0].z)); Wd.Add((float)pts[0].w); N.Add(pts[0].n); Pc.Add(pts[0].pc);
            double carry = 0;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = new Vector2((float)pts[i].x, (float)pts[i].z); var b = new Vector2((float)pts[i + 1].x, (float)pts[i + 1].z);
                double L = Vector2.Distance(a, b); if (L < 1e-3) continue;
                double d = STEP - carry;
                for (; d <= L; d += STEP) { P.Add(Vector2.Lerp(a, b, (float)(d / L))); Wd.Add((float)pts[i].w); N.Add(pts[i].n); Pc.Add(pts[i].pc); }
                carry = L - (d - STEP);
            }
            int cnt = P.Count;
            // smooth the axis (joins of the chained model axes have small kinks), ±10 m
            var S = new Vector2[cnt];
            for (int i = 0; i < cnt; i++) { var acc = Vector2.zero; int c = 0; for (int k = -5; k <= 5; k++) { int j = Mathf.Clamp(i + k, 0, cnt - 1); acc += P[j]; c++; } S[i] = acc / c; }
            // profiled roads (n >= 3, LaneProfile): the lanes the mesh has there, offsets relative to this sample (u: its lateral off the road's axis)
            var Lo = new float[cnt][];
            for (int i = 0; i < cnt; i++)
            {
                var pr = Pc[i].prof; if (pr == null || twoWay) continue;
                LaneProfile.Project(Pc[i].p, P[i].x, P[i].y, out double s, out double u);
                var o = pr.off[pr.Idx(s)]; Lo[i] = new float[o.Length]; for (int k = 0; k < o.Length; k++) Lo[i][k] = o[k] - (float)u;
                N[i] = o.Length;
            }
            if (twoWay) for (int i = 0; i < cnt; i++) N[i] = 1;
            int maxL = N.Max();
            var cor = new TrafficCorridor { name = name, step = STEP, maxLanes = maxL, axis = new Vector3[cnt], right = new Vector3[cnt], lanes = new byte[cnt], laneOff = new float[cnt * maxL], laneY = new float[cnt * maxL], vmax = new float[cnt] };
            var raw = new float[cnt * maxL];
            for (int i = 0; i < cnt; i++)
            {
                var t = S[Mathf.Min(i + 1, cnt - 1)] - S[Mathf.Max(i - 1, 0)]; t.Normalize();
                // game frame right of travel (x east, z south): (−tz, tx); Unity z = −game z
                var rg = new Vector2(-t.y, t.x);
                cor.right[i] = new Vector3(rg.x, 0, -rg.y);
                cor.lanes[i] = (byte)N[i];
                for (int k = 0; k < maxL; k++) raw[i * maxL + k] = k >= N[i] ? float.NaN : twoWay ? Wd[i] / Mathf.Max(2, Pc[i].n) / 2 : Lo[i] != null ? Lo[i][k] : Wd[i] / 2 - (k + .5f) * Wd[i] / N[i];
            }
            // lane offsets smoothed ±10 m where the lane exists (width / lane count changes between pieces)
            for (int i = 0; i < cnt; i++)
                for (int k = 0; k < maxL; k++)
                {
                    if (float.IsNaN(raw[i * maxL + k])) { cor.laneOff[i * maxL + k] = float.NaN; continue; }
                    float acc = 0; int c = 0;
                    for (int q = -5; q <= 5; q++) { int j = Mathf.Clamp(i + q, 0, cnt - 1); float v = raw[j * maxL + k]; if (!float.IsNaN(v)) { acc += v; c++; } }
                    cor.laneOff[i * maxL + k] = acc / c;
                }
            // heights: model surface for the first sample, then raycasts on the road colliders following the previous height
            Physics.SyncTransforms();
            double y0 = double.NaN; foreach (var m in models) { y0 = m.Surface(S[0].x, S[0].y); if (!double.IsNaN(y0)) break; }
            float yPrev = !float.IsNaN(yStart) ? yStart : double.IsNaN(y0) ? 30 : (float)y0; int miss = 0;
            for (int i = 0; i < cnt; i++)
            {
                var u = W.U(S[i].x, 0, S[i].y);
                float ya = Hit(u, yPrev); if (float.IsNaN(ya)) { miss++; ya = yPrev; }
                cor.axis[i] = new Vector3(u.x, ya, u.z); yPrev = ya;
                for (int k = 0; k < maxL; k++)
                {
                    float o = cor.laneOff[i * maxL + k];
                    if (float.IsNaN(o)) { cor.laneY[i * maxL + k] = ya; continue; }
                    float yk = Hit(u + cor.right[i] * o, ya);
                    cor.laneY[i * maxL + k] = float.IsNaN(yk) ? ya : yk;
                }
            }
            // curve speed: v = sqrt(a_lat / κ), κ from the heading change over ±10 m
            for (int i = 0; i < cnt; i++)
            {
                int a = Mathf.Max(0, i - 5), b = Mathf.Min(cnt - 1, i + 5);
                if (b - a < 2) { cor.vmax[i] = 22; continue; }
                var t0 = S[Mathf.Min(a + 1, cnt - 1)] - S[a]; var t1 = S[b] - S[Mathf.Max(b - 1, 0)];
                float ang = Mathf.Abs(Vector2.SignedAngle(t0, t1)) * Mathf.Deg2Rad, len = (b - a) * STEP;
                float kap = ang / len;
                cor.vmax[i] = Mathf.Min(24, Mathf.Sqrt(2.2f / Mathf.Max(kap, 1e-5f)));
            }
            Debug.Log($"[traffic] corridor {name}: {cnt} samples, {(cnt - 1) * STEP:F0} m, game x {S[0].x:F0} → {S[cnt - 1].x:F0}, lanes ≤ {maxL}, height misses {miss}");
            return cor;
        }

        /// a named road of the game graph as a corridor axis: from the node nearest `from` (game x, z), always the edge of that name
        /// that continues straightest, up to game x `xEnd` (eastwards); w 7 / 2 lanes (two-way). yStart: the first point's height.
        static List<(double x, double z, double w, int n, Piece pc)> GraphRoad(GameWorld gw, string name, Vector2 from, float xEnd, out float yStart, out float yEnd)
        {
            yStart = yEnd = float.NaN;
            var inc = new List<int>[gw.nodes.Length]; for (int i = 0; i < inc.Length; i++) inc[i] = new List<int>();
            for (int i = 0; i < gw.edges.Length; i++) { inc[gw.edges[i].a].Add(i); inc[gw.edges[i].b].Add(i); }
            int cur = -1; float bd = 30;
            for (int i = 0; i < gw.nodes.Length; i++) { float d = (W.G(gw.nodes[i]) - from).magnitude; if (d < bd) { bd = d; cur = i; } }
            if (cur < 0) return null;
            List<Vector2> Pts(int ei, int at) { var e = gw.edges[ei]; var l = new List<Vector2>(); for (int k = 0; k < e.pn; k++) l.Add(W.G(gw.pts[e.p0 + k])); if (e.b == at) l.Reverse(); return l; }
            List<float> Ys(int ei, int at) { var e = gw.edges[ei]; var l = new List<float>(); for (int k = 0; k < e.pn; k++) l.Add(gw.pts[e.p0 + k].y); if (e.b == at) l.Reverse(); return l; }
            var piece = new Piece { p = null, w = 7, n = 2, prof = null };
            var outp = new List<Vector2>(); Vector2 dir = Vector2.right; int prev = -1; var seen = new HashSet<int>();
            yStart = gw.nodes[cur].y;
            for (int guard = 0; guard < 400; guard++)
            {
                int best = -1; float bc = prev < 0 ? -2 : .7f;
                foreach (int ei in inc[cur])
                {
                    if (ei == prev || seen.Contains(ei) || gw.edges[ei].closed || !(gw.edges[ei].name ?? "").Contains(name)) continue;
                    var q = Pts(ei, cur); if (q.Count < 2) continue;
                    var t = (q[Mathf.Min(3, q.Count - 1)] - q[0]).normalized; float c = Vector2.Dot(t, dir);
                    if (prev < 0) c = t.x; // the first edge: the eastward one
                    if (c > bc) { bc = c; best = ei; }
                }
                if (best < 0) break;
                var pts = Pts(best, cur); var ys = Ys(best, cur); seen.Add(best);
                for (int k = outp.Count == 0 ? 0 : 1; k < pts.Count; k++) { if (pts[k].x > xEnd) { guard = 9999; break; } outp.Add(pts[k]); yEnd = ys[k]; }
                if (outp.Count >= 2) dir = (outp[outp.Count - 1] - outp[Mathf.Max(0, outp.Count - 4)]).normalized;
                prev = best; var e = gw.edges[best]; cur = e.a == cur ? e.b : e.a;
                if (guard >= 9999) break;
            }
            Debug.Log($"[traffic] graph road {name}: {outp.Count} points, game x {(outp.Count > 0 ? outp[0].x : 0):F0} → {(outp.Count > 0 ? outp[outp.Count - 1].x : 0):F0}");
            return outp.Select(q => ((double)q.x, (double)q.y, 7.0, 2, piece)).ToList();
        }

        // ------------------------------------------------------------------ signals
        /// allArms: every group edge's stop line (SignalsBuilder: which group each painted stop line belongs to), else only the D8's
        /// and the old road's (the AI corridors)
        internal static (List<TrafficPlan>, List<(Vector2 p, Vector2 dir, int plan, string group, string model)>) Signals(List<ModelJ> models, bool allArms = false)
        {
            var plans = new List<TrafficPlan>(); var stops = new List<(Vector2, Vector2, int, string, string)>();
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "model-junctions.json")));
            var ov = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "junction-overrides-modeli.json")));
            var edges = new Dictionary<long, Dictionary<string, object>>();
            foreach (var o in MiniJson.L(ov["overrides"]))
            {
                var oo = MiniJson.O(MiniJson.O(o)["overrides"]);
                if (oo.TryGetValue("addEdges", out var ae)) foreach (var e in MiniJson.L(ae)) { var eo = MiniJson.O(e); edges[(long)MiniJson.D(eo["id"])] = eo; }
            }
            var keys = new HashSet<string>(models.Select(m => m.key));
            foreach (var jo in MiniJson.L(root["junctions"]))
            {
                var j = MiniJson.O(jo); string key = (string)j["key"];
                if (!keys.Contains(key)) continue;
                var sig = MiniJson.G(j, "signals") as Dictionary<string, object>;
                if (sig == null || !(MiniJson.G(sig, "plan") is Dictionary<string, object> plan)) continue;
                var tp = new TrafficPlan { key = key, cycle = (float)MiniJson.D(plan["cycle"]) };
                var ph = new List<TrafficPhase>();
                foreach (var p in MiniJson.L(plan["phases"]))
                {
                    var po = MiniJson.O(p); if (!(MiniJson.G(po, "group") is string g)) continue;
                    var f = new TrafficPhase { group = g, g0 = -1, g1 = -1, y0 = -1, y1 = -1, r0 = -1, r1 = -1 };
                    if (MiniJson.G(po, "redYellow") is List<object> ry) { f.r0 = (float)MiniJson.D(ry[0]); f.r1 = (float)MiniJson.D(ry[1]); }
                    if (MiniJson.G(po, "green") is List<object> gr) { f.g0 = (float)MiniJson.D(gr[0]); f.g1 = (float)MiniJson.D(gr[1]); }
                    if (MiniJson.G(po, "yellow") is List<object> yl) { f.y0 = (float)MiniJson.D(yl[0]); f.y1 = (float)MiniJson.D(yl[1]); }
                    ph.Add(f);
                }
                // a phase whose group has only closed approaches lets nobody go (05: Marko closed both side arms, C and D of SG3):
                // cut from the cycle, the later phases moved up (as the web importer's square.ts, 15. 11.) — else the D8 waits on red
                {
                    var grpEdges = new Dictionary<string, List<long>>();
                    foreach (var kv in MiniJson.O(sig["groups"]) ?? new Dictionary<string, object>()) foreach (var eid in MiniJson.L(kv.Value)) { if (!grpEdges.TryGetValue(kv.Key, out var l)) grpEdges[kv.Key] = l = new List<long>(); l.Add((long)MiniJson.D(eid)); }
                    foreach (var kv in MiniJson.O(MiniJson.G(sig, "left")) ?? new Dictionary<string, object>()) if (long.TryParse(kv.Key, out long le) && kv.Value is string lg) { if (!grpEdges.TryGetValue(lg, out var l)) grpEdges[lg] = l = new List<long>(); l.Add(le); }
                    bool Closed(long id) => edges.TryGetValue(id, out var ce) && MiniJson.G(ce, "closed") is bool cb && cb;
                    float Start(TrafficPhase q) => Mathf.Min(q.r0 >= 0 ? q.r0 : 1e9f, Mathf.Min(q.g0 >= 0 ? q.g0 : 1e9f, q.y0 >= 0 ? q.y0 : 1e9f));
                    foreach (var dead in ph.Where(q => grpEdges.TryGetValue(q.group, out var l) && l.All(Closed)).ToList())
                    {
                        float sp = Start(dead), sn = ph.Where(q => q != dead && Start(q) > sp).Select(Start).DefaultIfEmpty(tp.cycle).Min(), cut = sn - sp;
                        ph.Remove(dead);
                        foreach (var q in ph) if (Start(q) >= sn) { if (q.r0 >= 0) { q.r0 -= cut; q.r1 -= cut; } if (q.g0 >= 0) { q.g0 -= cut; q.g1 -= cut; } if (q.y0 >= 0) { q.y0 -= cut; q.y1 -= cut; } }
                        tp.cycle -= cut;
                        if (!allArms) Debug.Log($"[traffic] {key}: phase {dead.group} cut (all its approaches closed), cycle {tp.cycle + cut:F0} → {tp.cycle:F0} s");
                    }
                }
                tp.phases = ph.ToArray(); int pi = plans.Count; plans.Add(tp);
                var stopLines = MiniJson.L(sig["stops"]).Select(q => MiniJson.O(q)).Select(q => (p: new Vector2((float)MiniJson.D(q["x"]), (float)MiniJson.D(q["z"])), a: (float)MiniJson.D(q["a"]))).ToList();
                foreach (var kv in MiniJson.O(sig["groups"]))
                    foreach (var eid in MiniJson.L(kv.Value))
                    {
                        if (!edges.TryGetValue((long)MiniJson.D(eid), out var e)) continue;
                        // (the D8's approaches, and the old road's — mag-11-19 19 / 20 / 21 / s04 — whose corridors run along Cesta dr. F. Tuđmana)
                        bool d8 = MiniJson.G(e, "ref") is string rf && rf.Contains("D8"), oldRoad = MiniJson.G(e, "name") is string nm && nm.Contains("Tuđmana");
                        if (!d8 && !oldRoad && !allArms) continue;
                        // a closed arm (Marko's models) has no stop line anyone reaches: no red-light line for the player there
                        if (MiniJson.G(e, "closed") is bool ecl && ecl) continue;
                        var pts = MiniJson.L(e["points"]).Select(q => MiniJson.L(q)).Select(q => new Vector2((float)MiniJson.D(q[0]), (float)MiniJson.D(q[2]))).ToList();
                        // the edge runs into the junction at b: its painted stop line is the one nearest to b − stopLine.b along the edge
                        // whose heading (atan2 z, x) runs the edge's way. (A two-way edge of the old road may run into it at a — the
                        // westbound approaches: both ends are tried, the nearer match wins.)
                        int bi = -1; float bd = 25; var dir = Vector2.zero;
                        var sld = MiniJson.G(e, "stopLine") as Dictionary<string, object>;
                        bool twoWayE = !(MiniJson.G(e, "oneway") is bool ow && ow);
                        foreach (bool atB in twoWayE ? new[] { true, false } : new[] { true })
                        {
                            var end = atB ? pts[pts.Count - 1] : pts[0];
                            var dr = atB ? (end - pts[Mathf.Max(0, pts.Count - 12)]).normalized : (end - pts[Mathf.Min(pts.Count - 1, 11)]).normalized;
                            string k = atB ? "b" : "a";
                            float back = sld != null && sld.ContainsKey(k) ? (float)MiniJson.D(sld[k]) : 0;
                            var guess = end - dr * back;
                            for (int q = 0; q < stopLines.Count; q++)
                            {
                                var sd = new Vector2(Mathf.Cos(stopLines[q].a), Mathf.Sin(stopLines[q].a));
                                float d = Vector2.Distance(stopLines[q].p, guess);
                                if (Vector2.Dot(sd, dr) > .5f && d < bd) { bd = d; bi = q; dir = dr; }
                            }
                        }
                        if (bi < 0) { if (!allArms) Debug.LogWarning($"[traffic] {key} {kv.Key}: no stop line for edge {eid}"); continue; }
                        var best = stopLines[bi].p; dir = new Vector2(Mathf.Cos(stopLines[bi].a), Mathf.Sin(stopLines[bi].a));
                        stops.Add((best, dir, pi, kv.Key, key));
                    }
            }
            return (plans, stops);
        }

        static void PlaceStops(TrafficCorridor c, List<(Vector2 p, Vector2 dir, int plan, string group, string model)> stops)
        {
            var list = new List<TrafficStop>();
            foreach (var st in stops)
            {
                var u = W.U(st.p.x, 0, st.p.y); var ud = new Vector3(st.dir.x, 0, -st.dir.y);
                int bi = -1; float bd = 9;
                for (int i = 0; i + 1 < c.axis.Length; i++)
                {
                    var a = c.axis[i]; float d = new Vector2(a.x - u.x, a.z - u.z).magnitude; if (d >= bd) continue;
                    var t = c.axis[i + 1] - c.axis[i]; t.y = 0;
                    if (Vector3.Dot(t.normalized, ud) < .7f) continue;
                    // within the carriageway laterally
                    if (Mathf.Abs(Vector3.Dot(u - a, c.right[i])) > 7) continue;
                    bd = d; bi = i;
                }
                if (bi < 0) continue;
                var tt = (c.axis[Mathf.Min(bi + 1, c.axis.Length - 1)] - c.axis[bi]); tt.y = 0; tt.Normalize();
                float s = bi * c.step + Vector3.Dot(u - c.axis[bi], tt);
                list.Add(new TrafficStop { s = s, plan = st.plan, group = st.group, model = st.model });
                Debug.Log($"[traffic] {c.name}: stop {st.model} {st.group} at s {s:F0} m");
            }
            c.stops = list.OrderBy(q => q.s).ToArray();
        }

        [MenuItem("Magistrala/Build traffic")]
        public static void Build()
        {
            Dir(OUT);
            var models = WorldData.Models(); LaneProfile.Clear();
            Debug.Log("[traffic] lane profiles (n >= 3 roads: lanes where the mesh has them)\n" + LaneProfile.Report(models));
            var roads = Roads(models);
            var m01 = models.Find(m => m.key == "krizanje-01");
            Piece Seed(int idx) => roads.Find(r => r.p == m01.roads[idx].p);
            var eastC = Chain(roads, Seed(0)); var westC = Chain(roads, Seed(2));
            // mag-11-20: the D8 runs unbroken from the Štafilić interchange 41 and is closed towards Trogir in tile c09 (barriers a few metres
            // inside its west edge, model x −48.6): both corridors stop 10 m short of the closure (east starts there, west ends there)
            var c09 = models.Find(m => m.key == "dionica-c09");
            if (c09 != null)
            {
                double cut = c09.x - 38 * (c09.lin?[0] ?? 1);
                int k0 = eastC.FindIndex(q => q.x >= cut); if (k0 > 0) eastC = eastC.GetRange(k0, eastC.Count - k0);
                int k1 = westC.FindLastIndex(q => q.x >= cut); if (k1 >= 0 && k1 < westC.Count - 1) westC = westC.GetRange(0, k1 + 1);
                Debug.Log($"[traffic] D8 cut at the c09 closure (x {cut:F0}): east from {eastC[0].x:F0}, west to {westC[westC.Count - 1].x:F0}");
            }
            var east = Corridor("D8 istok (Split)", eastC, models);
            var west = Corridor("D8 zapad (Trogir)", westC, models);
            west.deadEnd = c09 != null;
            var cors = new List<TrafficCorridor> { east, west };
            // the old road Štafilić → Lukšić (mag-11-19: s01 … s13, junctions 19 / 20 / 21): two-way, one corridor per direction on the same
            // chained axis, cut 4 m before the works closure in s13 (tile x 89, README_DIONICE.md: the road is not built beyond it)
            var s02 = models.Find(m => m.key == "dionica-s02"); var s13 = models.Find(m => m.key == "dionica-s13");
            if (s02 != null && s02.roads.Count > 0)
            {
                var old = Chain(roads, roads.Find(r => r.p == s02.roads[0].p));
                if (s13 != null) { double cut = s13.x + 85 * (s13.lin?[0] ?? 1); int k = old.FindIndex(q => q.x > cut); if (k > 1) old = old.GetRange(0, k); }
                var back = new List<(double x, double z, double w, int n, Piece pc)>(old); back.Reverse();
                cors.Add(Corridor("Stara cesta istok (Lukšić)", old, models, true));
                cors[cors.Count - 1].deadEnd = s13 != null;
                cors.Add(Corridor("Stara cesta zapad (Štafilić)", back, models, true));
            }
            // New package replaces the old assumed easternMain corridor. Chain
            // only 72–79; the direction test follows their through axes to Sućurac.
            var eastModels = models.Where(m => int.TryParse(m.key.Replace("krizanje-", ""), out int id) && id >= 72 && id <= 79).ToList();
            var easternRoads = Roads(eastModels);
            var m72 = eastModels.Find(m => m.key == "krizanje-72");
            if (m72 == null) throw new InvalidDataException("[traffic] missing model 72");
            var easternAxis = Chain(easternRoads, easternRoads.Find(r => r.p == m72.roads[0].p));
            if (easternAxis[easternAxis.Count - 1].x < -850)
                throw new InvalidDataException("[traffic] model chain 72–79 stops before Sućurac");
            // The s13 works remain closed: start on the eastern side of the gap.
            easternAxis.RemoveAll(q => q.x < -6052.4);
            var easternBack = new List<(double x, double z, double w, int n, Piece pc)>(easternAxis); easternBack.Reverse();
            cors.Add(Corridor("Tuđmana istok (Sućurac)", easternAxis, models, true));
            var easternWest = Corridor("Tuđmana zapad (Lukšić)", easternBack, models, true);
            easternWest.deadEnd = true; cors.Add(easternWest);
            var (plans, stops) = Signals(models);
            // the real signalised junctions without a model: generic plans, their stop lines
            var extra = ExtraSignals.Load();
            foreach (var j in extra)
            {
                int pi = plans.Count; plans.Add(j.plan);
                foreach (var a in j.approaches) stops.Add((a.p, a.t, pi, a.group, j.key));
            }
            foreach (var c in cors) PlaceStops(c, stops);

            var sideRoutes = TrafficSideBuilder.Build(cors.ToArray(), plans.ToArray(), stops);
            string path = OUT + "/TrafficData.asset";
            var data = AssetDatabase.LoadAssetAtPath<TrafficData>(path);
            if (data == null) { data = ScriptableObject.CreateInstance<TrafficData>(); AssetDatabase.CreateAsset(data, path); }
            data.corridors = cors.ToArray();
            data.plans = plans.ToArray();
            data.sideRoutes = sideRoutes;
            data.kinds = Kinds();
            data.farPalette = PaletteMat(pal);
            // parked cars: one mesh per level + one atlas material per parkable kind (ParkedBake)
            foreach (var kd in data.kinds) if (ParkedBake.Parkable(kd)) ParkedBake.Bake(kd);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();

            var go = GameObject.Find("Traffic") ?? new GameObject("Traffic");
            var ts = go.GetComponent<TrafficSystem>() ?? go.AddComponent<TrafficSystem>();
            ts.data = data;
            // view range (paket25): the scene keeps its serialized values, so the build writes the defaults onto the component
            ts.targetCars = TrafficSystem.VIEW_CARS; ts.spawnMin = TrafficSystem.VIEW_SPAWN_MIN; ts.spawnMax = TrafficSystem.VIEW_SPAWN_MAX; ts.despawnDist = TrafficSystem.VIEW_DESPAWN;
            ts.farFadeStart = TrafficSystem.VIEW_FADE0; ts.farFadeEnd = TrafficSystem.VIEW_FADE1; ts.corridorReach = TrafficSystem.VIEW_REACH; ts.viewVersion = TrafficSystem.VIEW_VERSION;
            EditorUtility.SetDirty(ts);
            EditorSceneManager.MarkSceneDirty(go.scene);
            EditorSceneManager.SaveScene(go.scene);
            Debug.Log($"[traffic] built: {data.kinds.Length} kinds, {plans.Count} signal plans, corridors " + string.Join(", ", cors.Select(c => $"{c.name} ({c.stops.Length} stops)")));
        }
    }
}
