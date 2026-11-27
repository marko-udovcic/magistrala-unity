// Proba paketa "UKRAINIAN CITY" (Assets/OdesaUkraine, NIJE u gitu) na našim kućama: ulica se zada dvjema točkama na cesti
// (Unity), s ceste se bacaju zrake bočno na kuće (hull collider ćelije), za svako pročelje se iz near mesha ćelije čitaju
// prozori (submesh 1 = staklo) i zid (submesh 0, sloj iz UV1: kamen / žbuka / opeka / beton; boja kuće iz vertex boje).
// Na pročelje ide: "koža" zida (kopija trokuta zida 1.2 cm ispred, materijal paketa po sloju, boja kuće), okviri prozora,
// ponegdje balkon s ogradom, klima, odvodna cijev + oluk, tenda nad izlogom (široko staklo u prizemlju). Sve pod
// GameObjectom "UkrajinaProba" (neaktivan dok se ne uključi) — ništa se ne sprema; isti kod može kasnije ići po svim kućama.
//   Dress(ax, az, bx, bz, maxFacades)  — edit mode, gradi korijen (neaktivan)
//   Play-mode: Hold(view) / Shot(file) / Stats() — kamera na pozu, snimka (s GPU stablima/živicama) i UnityStats Game viewa
#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Magistrala.EditorTools
{
    public static class UkrajinaProba
    {
        public const string PF = "Assets/OdesaUkraine/Prefabs/", MT = "Assets/OdesaUkraine/Materials/", ROOT = "UkrajinaProba";

        public struct Fac { public Vector3 p0, n, along; public float u0, u1, y0, y1, ground; public List<Rect> win; public int code; public Color tint; public float split; }

        static GameObject Root(bool make)
        {
            var r = GameObject.Find(ROOT) ?? Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(g => g.name == ROOT && g.scene.IsValid());
            if (!r && make) r = new GameObject(ROOT);
            return r;
        }

        public static string Clear() { var r = Root(false); if (r) Object.DestroyImmediate(r); return "ok"; }
        public static string Show(bool on) { var r = Root(false); if (!r) return "nema"; r.SetActive(on); return r.name + " " + on; }

        static float Ground(Vector3 p)
        {
            foreach (var h in Physics.RaycastAll(p + Vector3.up * 60, Vector3.down, 200).OrderBy(h => h.distance))
                if (h.collider.name != "hull") return h.point.y;
            return p.y;
        }

        /// the facades seen from the road a -> b (both sides), nearest to `near` first
        public static List<Fac> Facades(Vector3 a, Vector3 b, Vector3 near, int max)
        {
            var res = new List<Fac>(); var seen = new HashSet<string>();
            var ab = b - a; ab.y = 0; float L = ab.magnitude; var t = ab / L; var side = new Vector3(-t.z, 0, t.x);
            var cand = new List<(float d, Fac f)>();
            for (float s = 0; s <= L; s += 1.5f)
                foreach (int sg in new[] { 1, -1 })
                {
                    var p = a + t * s; p.y = Ground(p) + 2.2f; var dir = side * sg;
                    var hits = Physics.RaycastAll(p, dir, 30).Where(h => h.collider.name == "hull" || h.collider is MeshCollider).OrderBy(h => h.distance).ToArray();
                    if (hits.Length == 0 || hits[0].collider.name != "hull") continue;
                    var h0 = hits[0]; if (Vector3.Dot(h0.normal, -dir) < .55f) continue;
                    var f = FromHull((MeshCollider)h0.collider, h0); if (f.u1 - f.u0 < 3) continue;
                    var fc = f.p0 + f.along * (f.u0 + f.u1) * .5f;
                    string key = Mathf.Round(fc.x) + "_" + Mathf.Round(fc.z) + "_" + Mathf.Round(f.n.x * 10) + "_" + Mathf.Round(f.n.z * 10);
                    if (!seen.Add(key)) continue;
                    f.ground = Ground(f.p0 + f.along * (f.u0 + f.u1) * .5f + f.n * 1.5f);
                    var c = f.p0 + f.along * (f.u0 + f.u1) * .5f;
                    cand.Add(((c - near).magnitude, f));
                }
            foreach (var c in cand.OrderBy(c => c.d)) { if (res.Count >= max) break; res.Add(c.f); }
            return res;
        }

        static Fac FromHull(MeshCollider mc, RaycastHit h)
        {
            var m = mc.sharedMesh; var v = m.vertices; var tr = m.triangles; var M = mc.transform.localToWorldMatrix;
            var n = h.normal; n.y = 0; n.Normalize(); var along = Vector3.Cross(Vector3.up, n).normalized; var p0 = h.point;
            float u0 = 1e9f, u1 = -1e9f, y0 = 1e9f, y1 = -1e9f;
            void Add(Vector3 w) { float u = Vector3.Dot(w - p0, along); u0 = Mathf.Min(u0, u); u1 = Mathf.Max(u1, u); y0 = Mathf.Min(y0, w.y); y1 = Mathf.Max(y1, w.y); }
            int ti = h.triangleIndex * 3;
            for (int k = 0; k < 3; k++) Add(M.MultiplyPoint3x4(v[tr[ti + k]]));
            for (int it = 0; it < 4; it++)
                for (int i = 0; i < tr.Length; i += 3)
                {
                    var A = M.MultiplyPoint3x4(v[tr[i]]); var B = M.MultiplyPoint3x4(v[tr[i + 1]]); var C = M.MultiplyPoint3x4(v[tr[i + 2]]);
                    var nn = Vector3.Cross(B - A, C - A); if (nn.sqrMagnitude < 1e-8f) continue; nn.Normalize();
                    if (Vector3.Dot(nn, n) < .99f && Vector3.Dot(-nn, n) < .99f) continue;
                    if (Mathf.Abs(Vector3.Dot(A - p0, n)) > .08f) continue;
                    float a0 = Mathf.Min(Vector3.Dot(A - p0, along), Mathf.Min(Vector3.Dot(B - p0, along), Vector3.Dot(C - p0, along)));
                    float a1 = Mathf.Max(Vector3.Dot(A - p0, along), Mathf.Max(Vector3.Dot(B - p0, along), Vector3.Dot(C - p0, along)));
                    if (a1 < u0 - .05f || a0 > u1 + .05f || a1 - a0 > 60) continue;
                    Add(A); Add(B); Add(C);
                }
            return new Fac { p0 = p0, n = n, along = along, u0 = u0, u1 = u1, y0 = y0, y1 = y1 };
        }

        static readonly StringBuilder dbg = new StringBuilder();
        struct Tri { public Vector3 a, b, c; public Vector4 d; public Color col; }

        /// near-level triangles of the facade: glass (window openings) and the wall itself
        static void Read(ref Fac f, List<Tri> wall)
        {
            var win = new List<Rect>(); var codes = new Dictionary<int, float>(); var tmp = new List<Tri>();
            var fb = new Bounds(f.p0 + f.along * (f.u0 + f.u1) * .5f + Vector3.up * ((f.y0 + f.y1) * .5f - f.p0.y), new Vector3(Mathf.Abs(f.along.x) * (f.u1 - f.u0) + 2, f.y1 - f.y0 + 2, Mathf.Abs(f.along.z) * (f.u1 - f.u0) + 2));
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (r.name != "near" || !r.bounds.Intersects(fb)) continue;
                var m = r.GetComponent<MeshFilter>().sharedMesh; if (!m) continue;
                var M = r.localToWorldMatrix; var v = m.vertices; var d = new List<Vector4>(); m.GetUVs(1, d); var col = m.colors;
                for (int sm = 0; sm < 2; sm++)
                {
                    var tr = m.GetTriangles(sm);
                    for (int i = 0; i < tr.Length; i += 3)
                    {
                        var A = M.MultiplyPoint3x4(v[tr[i]]); var B = M.MultiplyPoint3x4(v[tr[i + 1]]); var C = M.MultiplyPoint3x4(v[tr[i + 2]]);
                        var nn = Vector3.Cross(B - A, C - A); if (nn.sqrMagnitude < 1e-10f) continue; nn.Normalize();
                        if (Vector3.Dot(nn, f.n) < .97f) continue;
                        var cc = (A + B + C) / 3; float pd = Vector3.Dot(cc - f.p0, f.n), u = Vector3.Dot(cc - f.p0, f.along);
                        if (u < f.u0 - .05f || u > f.u1 + .05f || cc.y < f.y0 - .5f || cc.y > f.y1 + .5f) continue;
                        if (sm == 1)
                        {
                            if (pd < -.8f || pd > .1f) continue;
                            float x0 = Mathf.Min(Vector3.Dot(A - f.p0, f.along), Mathf.Min(Vector3.Dot(B - f.p0, f.along), Vector3.Dot(C - f.p0, f.along)));
                            float x1 = Mathf.Max(Vector3.Dot(A - f.p0, f.along), Mathf.Max(Vector3.Dot(B - f.p0, f.along), Vector3.Dot(C - f.p0, f.along)));
                            win.Add(Rect.MinMaxRect(x0, Mathf.Min(A.y, Mathf.Min(B.y, C.y)), x1, Mathf.Max(A.y, Mathf.Max(B.y, C.y))));
                        }
                        else
                        {
                            if (Mathf.Abs(pd) > .05f) continue;
                            int code = (int)Mathf.Round(d[tr[i]].x);
                            float area = Vector3.Cross(B - A, C - A).magnitude * .5f;
                            codes[code] = (codes.TryGetValue(code, out var q) ? q : 0) + area;
                            tmp.Add(new Tri { a = A, b = B, c = C, d = d[tr[i]], col = col.Length > 0 ? col[tr[i]] : Color.white });
                        }
                    }
                }
            }
            // merge the glass triangles into openings
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < win.Count && !merged; i++)
                    for (int j = i + 1; j < win.Count; j++)
                    {
                        var A = win[i]; var B = win[j];
                        if (A.xMin <= B.xMax + .04f && B.xMin <= A.xMax + .04f && A.yMin <= B.yMax + .04f && B.yMin <= A.yMax + .04f)
                        { win[i] = Rect.MinMaxRect(Mathf.Min(A.xMin, B.xMin), Mathf.Min(A.yMin, B.yMin), Mathf.Max(A.xMax, B.xMax), Mathf.Max(A.yMax, B.yMax)); win.RemoveAt(j); merged = true; break; }
                    }
            }
            dbg.Append("  staklo: " + string.Join(" ", win.Select(r => $"({r.xMin:F1},{r.yMin:F1} {r.width:F1}x{r.height:F1})")) + "\n");
            f.win = win.Where(r => r.width > .35f && r.height > .5f && r.width < 6 && r.height < 4).ToList();
            if (codes.Count == 0) return;
            f.code = codes.OrderByDescending(k => k.Value).First().Key;
            foreach (var t in tmp) if ((int)Mathf.Round(t.d.x) == f.code) { wall.Add(t); f.tint = t.col; f.split = t.col.a * 40; }
        }

        static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        static Material Mat(string name, Color tint)
        {
            string key = name + ColorUtility.ToHtmlStringRGB(tint);
            if (mats.TryGetValue(key, out var m) && m) return m;
            var src = AssetDatabase.LoadAssetAtPath<Material>(MT + name + ".mat");
            m = new Material(src) { name = "uk_" + key };
            if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
            // Kaštela limestone blocks are larger and greyer than the pack's ashlar
            if (name.StartsWith("Brick_E") && m.HasProperty("_World_Scale")) m.SetFloat("_World_Scale", .55f);
            return mats[key] = m;
        }

        static Material Tinted(Material src, Color c)
        {
            if (!src || !src.HasProperty("_Color")) return src;
            string key = "t_" + src.name + ColorUtility.ToHtmlStringRGB(c);
            if (mats.TryGetValue(key, out var m) && m) return m;
            m = new Material(src) { name = key }; m.SetColor("_Color", c); return mats[key] = m;
        }

        /// pack material for one of our wall layers (BuildingTextures: 0 plaster, 1 stone, 3 brick, 4 concrete)
        public static string[] LayerMat = { "Plaster_H", "Brick_E", "Plaster_H", "Brick_A", "Concrete_B", "Plaster_H", "Plaster_H", "Brick_B" };

        static GameObject Put(string prefab, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(PF + prefab + ".prefab"); if (!src) { Debug.LogWarning("[uk] nema " + prefab); return null; }
            var g = (GameObject)PrefabUtility.InstantiatePrefab(src, parent);
            g.transform.SetPositionAndRotation(pos, rot); g.transform.localScale = Vector3.Scale(g.transform.localScale, scale);
            return g;
        }

        /// local bounds of a prefab (identity pose)
        static Bounds PB(string prefab)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(PF + prefab + ".prefab");
            var g = (GameObject)Object.Instantiate(src); g.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var rs = g.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            Object.DestroyImmediate(g); return b;
        }

        /// prefab fitted into a facade rect (u, y): x/y scaled to the rect, its front face `front` m out of the wall
        static GameObject Fit(string prefab, Transform parent, Fac f, Rect r, float front, float pad = 0)
        {
            var b = PB(prefab);
            var sc = new Vector3((r.width + pad) / b.size.x, (r.height + pad) / b.size.y, 1);
            var rot = Quaternion.LookRotation(f.n, Vector3.up);
            var g = Put(prefab, parent, Vector3.zero, rot, sc); if (!g) return null;
            var c = f.p0 + f.along * r.center.x; c.y = r.center.y;
            // bounds centre (x, y) on the rect centre, the front (max z) at the wall + front
            var off = rot * Vector3.Scale(new Vector3(b.center.x, b.center.y, b.max.z), sc);
            g.transform.position = c - off + f.n * front;
            return g;
        }

        public static string Dress(float ax, float az, float bx, float bz, float nx, float nz, int maxFacades, int seed = 7)
        {
            dbg.Clear(); Clear(); var root = Root(true); var rnd = new System.Random(seed);
            var a = new Vector3(ax, 0, az); var b = new Vector3(bx, 0, bz);
            var fs = Facades(a, b, new Vector3(nx, 0, nz), maxFacades);
            var sb = new StringBuilder(); int nWin = 0, nBal = 0, nAc = 0, nPipe = 0, nAw = 0;
            int idx = 0;
            foreach (var f0 in fs)
            {
                var f = f0; if (f.y1 - f.ground < 3) continue; // a yard wall / hedge box, not a house
                var wall = new List<Tri>(); Read(ref f, wall);
                var house = new GameObject("kuca" + (idx++)).transform; house.SetParent(root.transform, false);
                // wall skin
                int lower = (f.code % 64) % 8, upper = ((f.code % 64) / 8) % 8;
                Color tint = new Color(f.tint.r, f.tint.g, f.tint.b).gamma;
                var V = new List<Vector3>(); var T0 = new List<int>(); var T1 = new List<int>();
                // Kaštela rule (lead review 27. 11.): stone / brick / concrete faces keep our own texture; only plastered faces get the
                // pack's plaster, always light and warm (the house's paint, lifted; never darker than #C8BCA8 in luminance)
                float lum = .3f * tint.r + .59f * tint.g + .11f * tint.b;
                if (lum < .80f) tint = Color.Lerp(tint, new Color(.92f, .87f, .78f), Mathf.Clamp01((.80f - lum) / .25f));
                tint = new Color(Mathf.Min(1, tint.r * 1.06f), Mathf.Min(1, tint.g * 1.04f), Mathf.Min(1, tint.b * 1.0f));
                foreach (var t in wall)
                {
                    bool up = ((t.a.y + t.b.y + t.c.y) / 3 - f.ground) > f.split && upper != lower;
                    if ((up ? upper : lower) != 0) continue;
                    int k = V.Count; V.Add(t.a + f.n * .012f); V.Add(t.b + f.n * .012f); V.Add(t.c + f.n * .012f);
                    (up ? T1 : T0).AddRange(new[] { k, k + 1, k + 2 });
                }
                if (V.Count > 0)
                {
                    var mesh = new Mesh { name = "uk_skin_" + idx, indexFormat = V.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                    mesh.SetVertices(V); mesh.subMeshCount = 2; mesh.SetTriangles(T0, 0); mesh.SetTriangles(T1, 1);
                    // facing: emitted triangles keep the source winding
                    mesh.RecalculateNormals(); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                    var go = new GameObject("koza"); go.transform.SetParent(house, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    Material ML(int layer) => Mat(LayerMat[Mathf.Clamp(layer, 0, 7)], layer == 1 ? new Color(.80f, .83f, .86f) : layer == 3 || layer == 7 ? Color.white : tint);
                    go.AddComponent<MeshRenderer>().sharedMaterials = new[] { ML(0), ML(0) };
                }
                // windows: frame + sill in the opening (white PVC on plastered houses, brown wood on stone)
                bool stone = lower == 1 && upper == 1;
                var floors = f.win.GroupBy(r => Mathf.Round((r.yMin - f.ground) / 2.8f)).ToList();
                foreach (var r in f.win)
                {
                    bool groundFloor = r.yMin - f.ground < 1.2f;
                    if (groundFloor && r.width > 1.8f && r.yMin - f.ground < .7f)
                    {   // shop window: awning over it
                        var aw = Fit("Awning_B", house, f, Rect.MinMaxRect(r.xMin - .1f, r.yMax + .15f, r.xMax + .1f, r.yMax + .45f), .9f); if (aw) nAw++;
                        continue;
                    }
                    Fit(stone ? "Window_G" : "Window_F", house, f, r, .02f, .04f); nWin++;
                }
                // one balcony per upper floor on wide enough facades, a couple of AC units, a downpipe at a corner
                foreach (var fl in floors)
                {
                    if (fl.Key < 1 || f.u1 - f.u0 < 6) continue;
                    var list = fl.OrderBy(r => r.center.x).ToList(); if (list.Count < 2 || rnd.NextDouble() < .35) continue;
                    var r = list[rnd.Next(list.Count)];
                    var bb = PB("Balcony_F"); float w = Mathf.Max(2.2f, r.width + 1.2f);
                    var rot = Quaternion.LookRotation(f.n, Vector3.up); var c = f.p0 + f.along * r.center.x; c.y = r.yMin - .95f;
                    // slab tucked 5 cm into the wall, concrete-coloured (the pack's slab reads grey-blue under our sky)
                    var g = Put("Balcony_F", house, c - f.n * .05f, rot, new Vector3(w / bb.size.x, 1, 1.3f / bb.size.z));
                    if (g) { nBal++; foreach (var br in g.GetComponentsInChildren<Renderer>()) br.sharedMaterials = br.sharedMaterials.Select(m => Tinted(m, new Color(1f, .95f, .86f))).ToArray(); }
                }
                foreach (var r in f.win.Where(r => r.yMin - f.ground > 1.5f).OrderBy(_ => rnd.Next()).Take(rnd.Next(1, 3)))
                {
                    float u = r.xMax + .55f; if (u > f.u1 - .4f) u = r.xMin - .55f; if (u < f.u0 + .4f) continue;
                    var c = f.p0 + f.along * u; c.y = r.yMin + .1f;
                    if (Put(rnd.NextDouble() < .5 ? "ACUnit_B" : "ACUnit_D", house, c, Quaternion.LookRotation(f.n), Vector3.one)) nAc++;
                }
                {
                    float u = f.u1 - .25f, top = f.y1 - .15f, h = top - f.ground;
                    var c = f.p0 + f.along * u + f.n * .12f; c.y = f.ground;
                    if (h > 2 && Put("Pipe_B_1 Variant", house, c, Quaternion.LookRotation(f.n), new Vector3(1, h / 2f, 1))) nPipe++;
                    var gc = c; gc.y = top; Put("GutterDrain_A", house, gc + f.n * .05f, Quaternion.LookRotation(f.n), Vector3.one * .8f);
                }
                var fcen = f.p0 + f.along * (f.u0 + f.u1) * .5f; fcen.y = f.ground;
                sb.Append($"{house.name} @({fcen.x:F1},{fcen.y:F1},{fcen.z:F1}) n({f.n.x:F2},{f.n.z:F2}): pročelje {f.u1 - f.u0:F1} x {f.y1 - f.ground:F1} m, prozora {f.win.Count}, zid sloj {lower}/{upper} split {f.split:F1}, trokuta zida {wall.Count}, boja #{ColorUtility.ToHtmlStringRGB(tint)}\n");
            }
            root.SetActive(false);
            return dbg + $"pročelja {fs.Count}, okvira {nWin}, balkona {nBal}, klima {nAc}, cijevi {nPipe}, tendi {nAw}\n" + sb;
        }

        // ---------------------------------------------------------------- vegetation / roads trial (any mode)
        public static string Veg(string spec)
        {   // spec: "prefab@x,z,rotY,scale;..." (Unity x,z; y from the ground)
            var root = Root(true); var vg = root.transform.Find("vegetacija") ?? new GameObject("vegetacija").transform; vg.SetParent(root.transform, false);
            int n = 0;
            foreach (var it in spec.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = it.Split('@'); var q = p[1].Split(',').Select(float.Parse).ToArray();
                var pos = new Vector3(q[0], 0, q[1]); pos.y = Ground(pos);
                if (Put(p[0], vg, pos, Quaternion.Euler(0, q.Length > 2 ? q[2] : 0, 0), Vector3.one * (q.Length > 3 ? q[3] : 1))) n++;
            }
            return "vegetacija +" + n;
        }

        static readonly Dictionary<Renderer, Material[]> keep = new Dictionary<Renderer, Material[]>();
        /// swap the street chunk materials near c (radius r): asphalt / sidewalk / kerb by our material name -> pack material
        public static string Roads(float cx, float cz, float r, string asphalt, string walk, string kerb)
        {
            var c = new Vector3(cx, 0, cz); int n = 0; var sb = new StringBuilder();
            foreach (var rd in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var path = rd.transform.parent ? rd.transform.parent.name : ""; if (path != "Streets" && path != "Roads") continue;
                var b = rd.bounds; b.Expand(new Vector3(0, 1000, 0)); if (b.SqrDistance(c) > r * r) continue;
                if (!keep.ContainsKey(rd)) keep[rd] = rd.sharedMaterials;
                var ms = keep[rd].ToArray();
                for (int i = 0; i < ms.Length; i++)
                {
                    if (!ms[i]) continue; var nm = ms[i].name;
                    string to = nm.StartsWith("Asphalt") ? asphalt : nm.StartsWith("StreetWalk") || nm.Contains("plocnik") || nm.Contains("Walk") ? walk : nm.Contains("rubnjak") ? kerb : null;
                    if (string.IsNullOrEmpty(to)) continue;
                    var m = AssetDatabase.LoadAssetAtPath<Material>(MT + to + ".mat"); if (!m) { sb.Append("? " + to + " "); continue; }
                    ms[i] = m; n++;
                }
                rd.sharedMaterials = ms; sb.Append(rd.name + " ");
            }
            return "zamijenjeno " + n + ": " + sb;
        }
        public static string RoadsBack() { foreach (var k in keep) if (k.Key) k.Key.sharedMaterials = k.Value; int n = keep.Count; keep.Clear(); return "vraćeno " + n; }

        // ---------------------------------------------------------------- play mode: hold the camera, shots, stats
        public static string Hold(float ex, float ey, float ez, float lx, float ly, float lz, float fov)
        {
            var cam = Camera.main; if (!cam) return "nema Camera.main";
            foreach (var mb in cam.GetComponents<MonoBehaviour>()) if (!(mb is Holder) && mb.GetType().Namespace != "UnityEngine.Rendering.Universal") mb.enabled = false;
            foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None)) if (rb.transform.root.name == "Truck") rb.isKinematic = true;
            var h = cam.GetComponent<Holder>() ?? cam.gameObject.AddComponent<Holder>();
            h.eye = new Vector3(ex, ey, ez); h.look = new Vector3(lx, ly, lz); h.fov = fov;
            cam.transform.SetPositionAndRotation(h.eye, Quaternion.LookRotation(h.look - h.eye)); cam.fieldOfView = fov;
            return "ok";
        }
        public static string Shot(string file) { var h = Camera.main ? Camera.main.GetComponent<Holder>() : null; if (!h) return "nema Holder"; h.file = file; return "zatraženo"; }
        public static string Stats() => $"batches {UnityStats.batches}, draw calls {UnityStats.drawCalls}, setpass {UnityStats.setPassCalls}, tris {UnityStats.triangles / 1000}k, verts {UnityStats.vertices / 1000}k, shadow casters {UnityStats.shadowCasters}, frame {UnityStats.frameTime * 1000:F1} ms, render {UnityStats.renderTime * 1000:F1} ms";

        /// edit-mode render (no GPU trees / hedges: those stream in play mode only)
        public static string EditShot(string file, float ex, float ey, float ez, float lx, float ly, float lz, float fov)
        {
            var c = new GameObject("uk_cam").AddComponent<Camera>(); c.CopyFrom(Camera.main); c.fieldOfView = fov; c.nearClipPlane = .1f; c.farClipPlane = 2500;
            c.transform.SetPositionAndRotation(new Vector3(ex, ey, ez), Quaternion.LookRotation(new Vector3(lx, ly, lz) - new Vector3(ex, ey, ez)));
            var rt = new RenderTexture(1600, 900, 24) { antiAliasing = 4 }; c.targetTexture = rt; c.Render(); c.Render();
            RenderTexture.active = rt; var t = new Texture2D(1600, 900, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0); RenderTexture.active = null;
            System.IO.File.WriteAllBytes(file, t.EncodeToPNG()); Object.DestroyImmediate(c.gameObject); Object.DestroyImmediate(rt); Object.DestroyImmediate(t);
            return file;
        }

        [DefaultExecutionOrder(32000)]
        public sealed class Holder : MonoBehaviour
        {
            public Vector3 eye, look; public float fov = 60; public string file;
            void LateUpdate()
            {
                transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye)); var cam = GetComponent<Camera>(); cam.fieldOfView = fov;
                if (string.IsNullOrEmpty(file)) return;
                const int W = 1600, H = 900;
                var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32); var old = cam.targetTexture;
                cam.targetTexture = rt; cam.Render(); cam.targetTexture = old;
                RenderTexture.active = rt; var tex = new Texture2D(W, H, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply(); RenderTexture.active = null;
                System.IO.File.WriteAllBytes(file, tex.EncodeToPNG()); Destroy(tex); RenderTexture.ReleaseTemporary(rt); file = null;
            }
        }
    }
}
#endif
