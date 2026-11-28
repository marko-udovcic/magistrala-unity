// Magistrala/Trgovine: postavi — the big retail boxes of Kaštela built on their real footprints (World/Trgovine):
// Trgovački park Sućurac (the retail park: dark cladding, yellow fascia, glass fronts per tenant, solar carports, pavilions),
// Berba market (dark grey upper wall, red canopy band, red portal), Diskont D (light panels, taller entrance block, trolley
// shelter), Veliki trg (light hall, big red parking canopy, welcome banner). All names / signs invented (no real brands).
// Data: Assets/Editor/trgovine.json — per building: the tile building it replaces (footprint polygon, game frame), height,
// style, the point the shop fronts face, tenants, delivery docks (beside the package's delivery points: the van's stop
// stays free), carport rows / canopies / trolley shelters / pavilions (rectangles in the game frame).
// Texture: one atlas (Assets/Models/Benzinske/tex/trgovine_atlas.png, BalkanDriver assets-src/benzinske/scripts/trgovine_tex.py);
// one URP Lit material; every building one mesh per LOD (LOD0 with signs / docks / roof plant, LOD1 the shell), MeshCollider
// on the shell. Deterministic (no random). BuildingsFromTiles skips the replaced tiles (Benzinske.SkipTile -> Trgovine.SkipTile).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class Trgovine
    {
        public const string DATA = "Assets/Editor/trgovine.json";
        const string TEX = "Assets/Models/Benzinske/tex/trgovine_atlas.png", LAYOUT = "Assets/Models/Benzinske/tex/trgovine_layout.json";
        const string GEN = WorldBuilder.GEN + "/Trgovine";

        class Rect { public double x, z, a, l, w; }
        class Bld
        {
            public string id, name, style, tile; public double h = 8, fx, fz; public List<Vector2> poly = new List<Vector2>();
            public List<int> tenants = new List<int>(); public List<Vector2> docks = new List<Vector2>();
            public List<Rect> carports = new List<Rect>(), canopies = new List<Rect>(), trolleys = new List<Rect>(), pavilions = new List<Rect>(), blocks = new List<Rect>();
            public List<int> frontEdges; // explicit front edges (polygon edge index i: i -> i+1), else by facing (fx, fz)
        }

        static List<Bld> data;
        static List<Bld> Data()
        {
            if (data != null) return data;
            data = new List<Bld>(); if (!File.Exists(DATA)) return data;
            Rect R(object o) { var d = MiniJson.O(o); return new Rect { x = MiniJson.D(d["x"]), z = MiniJson.D(d["z"]), a = MiniJson.D(MiniJson.G(d, "a") ?? 0.0), l = MiniJson.D(MiniJson.G(d, "l") ?? 6.0), w = MiniJson.D(MiniJson.G(d, "w") ?? 2.5) }; }
            foreach (var o in MiniJson.L(MiniJson.O(MiniJson.Parse(File.ReadAllText(DATA)))["buildings"]))
            {
                var d = MiniJson.O(o);
                var b = new Bld { id = (string)d["id"], name = (string)d["name"], style = (string)d["style"], tile = (string)MiniJson.G(d, "tile"), h = MiniJson.D(d["h"]) };
                var f = MiniJson.L(d["faceToward"]); b.fx = MiniJson.D(f[0]); b.fz = MiniJson.D(f[1]);
                foreach (var p in MiniJson.L(d["footprint"])) { var q = MiniJson.L(p); b.poly.Add(new Vector2((float)MiniJson.D(q[0]), (float)MiniJson.D(q[1]))); }
                if (b.poly.Count > 2 && (b.poly[0] - b.poly[b.poly.Count - 1]).sqrMagnitude < 1e-4f) b.poly.RemoveAt(b.poly.Count - 1);
                if (MiniJson.L(MiniJson.G(d, "tenants")) is List<object> tl) foreach (var t in tl) b.tenants.Add((int)MiniJson.D(t));
                if (MiniJson.L(MiniJson.G(d, "docks")) is List<object> dl) foreach (var t in dl) { var q = MiniJson.L(t); b.docks.Add(new Vector2((float)MiniJson.D(q[0]), (float)MiniJson.D(q[1]))); }
                if (MiniJson.L(MiniJson.G(d, "frontEdges")) is List<object> fe) b.frontEdges = fe.Select(x => (int)MiniJson.D(x)).ToList();
                foreach (var (key, list) in new[] { ("carports", b.carports), ("canopies", b.canopies), ("trolleys", b.trolleys), ("pavilions", b.pavilions), ("blocks", b.blocks) })
                    if (MiniJson.L(MiniJson.G(d, key)) is List<object> rl) foreach (var r in rl) list.Add(R(r));
                data.Add(b);
            }
            return data;
        }
        public static bool SkipTile(string id) { if (id == null) return false; foreach (var b in Data()) if (b.tile == id) return true; return false; }

        // ------------------------------------------------------------------ atlas
        static Dictionary<string, float[]> reg; static List<string> sws;
        static void Layout()
        {
            if (reg != null) return;
            var j = MiniJson.O(MiniJson.Parse(File.ReadAllText(LAYOUT)));
            reg = new Dictionary<string, float[]>();
            foreach (var kv in MiniJson.O(j["regions"])) { var l = MiniJson.L(kv.Value); reg[kv.Key] = l.Select(x => (float)MiniJson.D(x)).ToArray(); }
            sws = MiniJson.L(j["swatches"]).Select(x => (string)x).ToList();
        }
        /// region rect in uv (u0, u1, v0, v1), sub-range s0..s1 / t0..t1 (t from the region's bottom)
        static Vector4 Reg(string n, float s0 = 0, float s1 = 1, float t0 = 0, float t1 = 1)
        {
            var r = reg[n]; float e = 1.5f / 2048;
            float u0 = r[0] + e, u1 = r[0] + r[2] - e, v0 = 1 - (r[1] + r[3]) + e, v1 = 1 - r[1] - e;
            return new Vector4(u0 + (u1 - u0) * s0, u0 + (u1 - u0) * s1, v0 + (v1 - v0) * t0, v0 + (v1 - v0) * t1);
        }
        static Vector4 Sw(string n, int row = 0)
        {
            var r = reg["swatch"]; int i = sws.IndexOf(n); float px = 64f / 2048;
            float u = r[0] + (i + .5f) * px, v = 1 - (r[1] + (row + .5f) * px);
            return new Vector4(u, u, v, v);
        }

        // ------------------------------------------------------------------ mesh builder (Unity space)
        class MB
        {
            public List<Vector3> v = new List<Vector3>(); public List<Vector2> uv = new List<Vector2>(); public List<int> t = new List<int>();
            /// bl, br, tr, tl as seen from the front side
            public void Quad(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector4 r)
            {
                int k = v.Count; v.Add(bl); v.Add(br); v.Add(tr); v.Add(tl);
                uv.Add(new Vector2(r.x, r.z)); uv.Add(new Vector2(r.y, r.z)); uv.Add(new Vector2(r.y, r.w)); uv.Add(new Vector2(r.x, r.w));
                t.AddRange(new[] { k, k + 3, k + 2, k, k + 2, k + 1 });
            }
            /// a vertical wall rectangle from a (left, seen from outside) to b, y0..y1
            public void Wall(Vector3 a, Vector3 b, float y0, float y1, Vector4 r) =>
                Quad(new Vector3(a.x, y0, a.z), new Vector3(b.x, y0, b.z), new Vector3(b.x, y1, b.z), new Vector3(a.x, y1, a.z), r);
            /// an axis box: centre, half sizes along (right, up, fwd) with a rotation; faces: per-face region or one
            public void Box(Vector3 c, Vector3 he, Quaternion q, Vector4 side, Vector4? front = null, Vector4? top = null, bool bottom = false)
            {
                Vector3 P(float x, float y, float z) => c + q * new Vector3(x * he.x, y * he.y, z * he.z);
                var f = front ?? side; var tp = top ?? side;
                QuadOut(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), side, q * Vector3.forward);
                QuadOut(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), f, q * Vector3.back);
                QuadOut(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), side, q * Vector3.left);
                QuadOut(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), side, q * Vector3.right);
                QuadOut(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1), tp, q * Vector3.up);
                if (bottom) QuadOut(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1), side, q * Vector3.down);
            }
            /// a quad whose front must face `outward`: mirrored when the given order faces the other way
            public void QuadOut(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector4 r, Vector3 outward)
            {
                var n = Vector3.Cross(tl - bl, tr - bl);
                if (Vector3.Dot(n, outward) < 0) Quad(br, bl, tl, tr, r); else Quad(bl, br, tr, tl, r);
            }
            public Mesh Mesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = v.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0);
                m.RecalculateNormals(); m.RecalculateTangents(); m.RecalculateBounds();
                return m;
            }
        }
        // the box faces wind for a left-handed frame: fix by checking after build (normals point away from the centre)

        // ------------------------------------------------------------------ geometry helpers
        static Vector3 U(Vector2 g, float y = 0) => W.U(g.x, y, g.y);
        static bool Inside(List<Vector3> P, Vector3 p)
        {
            bool c = false;
            for (int i = 0, j = P.Count - 1; i < P.Count; j = i++)
                if ((P[i].z > p.z) != (P[j].z > p.z) && p.x < (P[j].x - P[i].x) * (p.z - P[i].z) / (P[j].z - P[i].z) + P[i].x) c = !c;
            return c;
        }
        static List<int> Triangulate(List<Vector3> P)
        {
            // ear clipping in xz (simple polygons)
            var idx = Enumerable.Range(0, P.Count).ToList(); var tri = new List<int>();
            float area = 0; for (int i = 0, j = P.Count - 1; i < P.Count; j = i++) area += (P[j].x * P[i].z - P[i].x * P[j].z);
            bool ccw = area > 0; int guard = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool cut = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int a = idx[(k + idx.Count - 1) % idx.Count], b = idx[k], c = idx[(k + 1) % idx.Count];
                    var A = P[a]; var B = P[b]; var C = P[c];
                    float cr = (B.x - A.x) * (C.z - A.z) - (B.z - A.z) * (C.x - A.x);
                    if (ccw ? cr <= 1e-6f : cr >= -1e-6f) continue;
                    bool any = false;
                    foreach (int o in idx)
                    {
                        if (o == a || o == b || o == c) continue;
                        if (InTri(P[o], A, B, C)) { any = true; break; }
                    }
                    if (any) continue;
                    tri.AddRange(new[] { a, b, c }); idx.RemoveAt(k); cut = true; break;
                }
                if (!cut) break;
            }
            if (idx.Count == 3) tri.AddRange(idx);
            return tri;
        }
        static bool InTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float d1 = (p.x - b.x) * (a.z - b.z) - (a.x - b.x) * (p.z - b.z), d2 = (p.x - c.x) * (b.z - c.z) - (b.x - c.x) * (p.z - c.z), d3 = (p.x - a.x) * (c.z - a.z) - (c.x - a.x) * (p.z - a.z);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0; return !(neg && pos);
        }

        static bool Skip(Collider c)
        {
            for (var t = c.transform; t; t = t.parent)
                if (t.parent && t.parent.name == "World" && (t.name == "Trgovine" || t.name == "Benzinske" || t.name == "Props" || t.name == "Buildings" || t.name == "ParkedCars" || t.name == "Stanice" || t.name == "Detalji")) return true;
            return c.attachedRigidbody;
        }
        static float Ground(Vector3 p)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, 500, p.z), Vector3.down, 1000, ~0, QueryTriggerInteraction.Ignore);
            float y = float.NaN, best = 1e9f;
            foreach (var h in hits) if (!Skip(h.collider) && h.distance < best) { best = h.distance; y = h.point.y; }
            return float.IsNaN(y) ? (float)Tiles.Height(p.x + W.OX, -p.z + W.OZ) : y;
        }

        // ------------------------------------------------------------------ styles
        class Style { public string clad, cladFront, fascia, fasciaSide; public float glassH = 4.2f, fasciaH = 1.4f; public bool band; }
        static Style St(string s)
        {
            switch (s)
            {
                case "park": return new Style { clad = "clad_dark", cladFront = "clad_dark", fascia = "fascia_yellow", fasciaSide = "fascia_yellow", fasciaH = 1.3f };
                case "berba": return new Style { clad = "clad_dark", cladFront = "clad_dark", fascia = "fascia_grey", fasciaSide = "fascia_grey", glassH = 3.6f, fasciaH = .5f, band = true };
                case "diskont": return new Style { clad = "clad_light", cladFront = "clad_light", fascia = "fascia_white", fasciaSide = "fascia_white", glassH = 3.4f, fasciaH = .6f };
                default: return new Style { clad = "clad_light", cladFront = "clad_white", fascia = "fascia_white", fasciaSide = "fascia_grey", glassH = 3.8f, fasciaH = .8f };
            }
        }

        // ------------------------------------------------------------------ build
        [MenuItem("Magistrala/Trgovine: postavi")]
        public static string Menu()
        {
            MainScene.Ensure();
            var s = Place();
            var sc = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(sc); EditorSceneManager.SaveScene(sc);
            Debug.Log(s); return s;
        }

        static Material mat;
        static Material Mat()
        {
            if (mat) return mat;
            var ti = AssetImporter.GetAtPath(TEX) as TextureImporter;
            if (ti && (ti.maxTextureSize != 2048 || ti.anisoLevel != 4)) { ti.maxTextureSize = 2048; ti.anisoLevel = 4; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport(); }
            string p = GEN + "/Trgovine.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { if (!AssetDatabase.IsValidFolder(GEN)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Trgovine"); m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TEX)); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", .35f); m.SetFloat("_Metallic", 0); m.SetFloat("_Cull", 0); m.doubleSidedGI = true; m.enableInstancing = true; EditorUtility.SetDirty(m); // both faces: hand-wound quads of carports / canopies
            return mat = m;
        }

        static Mesh Save(Mesh m, string name)
        {
            if (!AssetDatabase.IsValidFolder(GEN)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Trgovine");
            string p = $"{GEN}/{name}.asset";
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (old) { EditorUtility.CopySerialized(m, old); UnityEngine.Object.DestroyImmediate(m); return old; }
            AssetDatabase.CreateAsset(m, p); return m;
        }

        static readonly List<string> log = new List<string>();
        public static string Place()
        {
            data = null; reg = null; mat = null; log.Clear(); Layout();
            var list = Data(); if (list.Count == 0) return "[trgovine] nema " + DATA;
            Physics.SyncTransforms();
            var root = WorldBuilder.Root("Trgovine").transform;
            int tris = 0;
            foreach (var b in list)
            {
                var footprint = b.poly.Select(q => new BuildingsFromTiles.Vector2d(q.x, q.y)).ToList();
                if (Kasteli.ExcludedTile(b.tile) || Kasteli.Excluded(footprint,
                    b.poly.Average(q => (double)q.x), b.poly.Average(q => (double)q.y), b.tile)) continue;
                var g = new GameObject($"{b.id} {b.name}"); g.transform.SetParent(root, false); g.isStatic = true;
                // the footprint in Unity space, the base at the lowest corner ground (walls sunk 1 m below it)
                var P = b.poly.Select(q => U(q)).ToList();
                float y0 = P.Min(p => Ground(p)), yTop = y0 + (float)b.h;
                var c = new Vector3(P.Average(p => p.x), y0, P.Average(p => p.z));
                var mb0 = new MB(); var mb1 = new MB();
                Shell(b, P, y0, mb0, true); Shell(b, P, y0, mb1, false);
                foreach (var bl in b.blocks) Block(b, bl, y0, mb0, mb1);
                var m0 = Save(mb0.Mesh($"trg-{b.id}-lod0"), $"trg-{b.id}-lod0"); var m1 = Save(mb1.Mesh($"trg-{b.id}-lod1"), $"trg-{b.id}-lod1");
                tris += m0.triangles.Length / 3;
                var lg = g.AddComponent<LODGroup>();
                var r0 = Child(g, "lod0", m0); var r1 = Child(g, "lod1", m1);
                lg.SetLODs(new[] { new LOD(.05f, new Renderer[] { r0 }), new LOD(.004f, new Renderer[] { r1 }) });
                r1.gameObject.AddComponent<MeshCollider>().sharedMesh = m1;
                // parking furniture: carports, canopies, trolley shelters, pavilions (one mesh per building)
                var mp = new MB();
                foreach (var r in b.carports) Carport(r, mp);
                foreach (var r in b.canopies) RedCanopy(r, mp);
                foreach (var r in b.trolleys) Trolley(r, mp);
                foreach (var r in b.pavilions) Pavilion(b, r, mp);
                if (mp.v.Count > 0)
                {
                    var mm = Save(mp.Mesh($"trg-{b.id}-parking"), $"trg-{b.id}-parking"); tris += mm.triangles.Length / 3;
                    var go = new GameObject("parkiraliste"); go.transform.SetParent(g.transform, false); go.isStatic = true;
                    go.AddComponent<MeshFilter>().sharedMesh = mm; go.AddComponent<MeshRenderer>().sharedMaterial = Mat();
                    var lg2 = go.AddComponent<LODGroup>(); lg2.SetLODs(new[] { new LOD(.01f, new Renderer[] { go.GetComponent<MeshRenderer>() }) });
                    foreach (var col in postCols) { var bc = go.AddComponent<BoxCollider>(); bc.center = col.c; bc.size = col.s; }
                    postCols.Clear();
                }
                log.Add($"  {b.id} {b.name} ({b.style}): {b.poly.Count} kutova, {b.h:F1} m, LOD0 {m0.triangles.Length / 3} trokuta, " +
                        $"{b.carports.Count} nadstrešnica parkirališta, {b.canopies.Count} krovova, {b.trolleys.Count} spremišta kolica, {b.docks.Count} rampi");
            }
            log.Insert(0, $"[trgovine] {list.Count} zgrada, {tris} trokuta LOD0 ukupno");
            AssetDatabase.SaveAssets();
            return string.Join("\n", log);
        }

        static MeshRenderer Child(GameObject g, string n, Mesh m)
        {
            var c = new GameObject(n); c.transform.SetParent(g.transform, false); c.isStatic = true;
            c.AddComponent<MeshFilter>().sharedMesh = m; var r = c.AddComponent<MeshRenderer>(); r.sharedMaterial = Mat(); return r;
        }

        static readonly List<(Vector3 c, Vector3 s)> postCols = new List<(Vector3, Vector3)>();

        /// walls, fascia, roof; detail: signs, docks, roof plant, entrance details
        static void Shell(Bld b, List<Vector3> P, float y0, MB mb, bool detail)
        {
            var st = St(b.style); float H = (float)b.h, yb = y0 - 1f, yr = y0 + H, yp = yr + .35f;
            var face = U(new Vector2((float)b.fx, (float)b.fz));
            var cen = new Vector3(P.Average(p => p.x), 0, P.Average(p => p.z));
            int n = P.Count; int tenant = 0; int unitBay = 0;
            for (int i = 0; i < n; i++)
            {
                var A = P[i]; var B = P[(i + 1) % n]; var d = B - A; d.y = 0; float len = d.magnitude; if (len < .3f) continue;
                var dir = d / len; var nrm = new Vector3(dir.z, 0, -dir.x);
                var mid = (A + B) / 2; if (Inside(P, mid + nrm * .3f)) nrm = -nrm;
                // seen from outside: right = cross(up, -nrm)
                var right = Vector3.Cross(Vector3.up, -nrm);
                Vector3 L0 = Vector3.Dot(A, right) < Vector3.Dot(B, right) ? A : B, R0 = L0 == A ? B : A;
                bool front = b.frontEdges != null ? b.frontEdges.Contains(i) : len > 8 && Vector3.Dot(nrm, (face - mid).normalized) > .6f;
                int nb = Mathf.Max(1, Mathf.RoundToInt(len / 4f)); float bw = len / nb;
                float yF0 = yr - st.fasciaH;
                for (int k = 0; k < nb; k++)
                {
                    var a = L0 + right * (bw * k); var bb = L0 + right * (bw * (k + 1));
                    a.y = bb.y = 0;
                    if (front && len > 6)
                    {
                        bool door = (k % 5) == 2 || nb < 3 && k == 0;
                        mb.Wall(a, bb, yb, y0 + .02f, Sw("dgrey", 1));
                        mb.Wall(a, bb, y0, y0 + st.glassH, Reg(door ? "doors" : "shopfront"));
                        // above the glass to the fascia: cladding in one row
                        if (yF0 > y0 + st.glassH + .05f) mb.Wall(a, bb, y0 + st.glassH, yF0, Reg(st.cladFront, 0, 1, 0, Mathf.Clamp01((yF0 - y0 - st.glassH) / 3.5f)));
                        mb.Wall(a, bb, yF0, yp, Reg(st.fascia));
                    }
                    else
                    {
                        mb.Wall(a, bb, yb, y0 + .4f, Sw("dgrey", 2));
                        // cladding rows of up to 4 m
                        int rows = Mathf.Max(1, Mathf.CeilToInt((yF0 - y0 - .4f) / 4f)); float rh = (yF0 - y0 - .4f) / rows;
                        for (int r = 0; r < rows; r++) mb.Wall(a, bb, y0 + .4f + r * rh, y0 + .4f + (r + 1) * rh, Reg(st.clad));
                        mb.Wall(a, bb, yF0, yp, Reg(st.fasciaSide));
                    }
                }
                // parapet cap (top, 0.3 m wide inward)
                var ia = L0 - nrm * .3f; var ib = R0 - nrm * .3f;
                mb.Quad(new Vector3(ia.x, yp, ia.z), new Vector3(ib.x, yp, ib.z), new Vector3(R0.x, yp, R0.z), new Vector3(L0.x, yp, L0.z), Sw("grey", 1));
                if (!detail) continue;
                // the Berba red canopy band along the front, the tenants' signs on the fascia
                if (front && st.band && len > 6)
                {
                    float by = y0 + st.glassH + .25f, bd = 2.2f;
                    var oa = L0 + nrm * bd; var ob = R0 + nrm * bd;
                    mb.Wall(oa, ob, by, by + .7f, Reg("fascia_red"));
                    mb.Quad(new Vector3(ob.x, by, ob.z), new Vector3(oa.x, by, oa.z), new Vector3(L0.x, by, L0.z), new Vector3(R0.x, by, R0.z), Sw("white", 1)); // underside (faces down)
                    mb.Quad(new Vector3(L0.x, by + .7f, L0.z), new Vector3(R0.x, by + .7f, R0.z), new Vector3(ob.x, by + .7f, ob.z), new Vector3(oa.x, by + .7f, oa.z), Sw("red", 2));
                    mb.Wall(L0 + nrm * 0, oa, by, by + .7f, Reg("fascia_red")); mb.Wall(ob, R0, by, by + .7f, Reg("fascia_red"));
                }
                if (front && len > 6)
                {
                    if (b.style == "park")
                    {
                        // a tenant per 3 bays: sign box centred on the unit's fascia
                        for (int k = 1; k + 1 < nb; k += 3)
                        {
                            int si = b.tenants.Count > 0 ? b.tenants[tenant++ % b.tenants.Count] : (tenant++ % 12);
                            var cpos = L0 + right * (bw * (k + .5f)) + nrm * .18f; float sw = Mathf.Min(bw * 2.2f, 7f), sh = sw / 4f;
                            Sign(mb, cpos, right, nrm, yF0 + (yp - yF0) / 2 - sh / 2 + .02f, sw, sh, Reg("sign" + si));
                        }
                        if (len > 60) Sign(mb, L0 + right * (len / 2) + nrm * .2f, right, nrm, yp + .1f, 14, 1.75f, Reg("park_sign"));
                    }
                    else if (b.style == "berba")
                    {
                        Sign(mb, L0 + right * (len * .3f) + nrm * .12f, right, nrm, y0 + st.glassH + 1.4f, 8, 4, Reg("berba"));
                        // the entrance portal: a red frame proud of the band
                        var pc = L0 + right * (len * .7f);
                        foreach (float s in new[] { -2.2f, 2.2f }) mb.Box(pc + right * s + nrm * 2.6f + Vector3.up * (y0 + 2.6f), new Vector3(.25f, 2.6f, .25f), Quaternion.LookRotation(-nrm), Sw("red", 0));
                        mb.Box(pc + nrm * 2.6f + Vector3.up * (y0 + 5.4f), new Vector3(2.5f, .3f, .3f), Quaternion.LookRotation(-nrm), Sw("red", 0));
                    }
                    else if (b.style == "diskont")
                        Sign(mb, L0 + right * (len - 4f) + nrm * .12f, right, nrm, yr - 4.2f, 3.6f, 3.6f, Reg("diskont"));
                    else
                    {
                        Sign(mb, L0 + right * (len * .5f) + nrm * .12f, right, nrm, yr - 5.2f, 5, 5, Reg("velikitrg"));
                        Sign(mb, L0 + right * (len * .5f) + nrm * .12f, right, nrm, y0 + st.glassH + .1f, 10, 1.25f, Reg("welcome"));
                    }
                }
                else if (len > 30 && b.style != "park")
                {
                    // the big name on a long side wall
                    var sc = L0 + right * (len * .5f) + nrm * .12f;
                    if (b.style == "berba") Sign(mb, sc, right, nrm, yr - 4.5f, 8, 4, Reg("berba"));
                    else if (b.style == "diskont") Sign(mb, sc, right, nrm, yr - 4f, 3.4f, 3.4f, Reg("diskont"));
                    else Sign(mb, sc, right, nrm, yr - 5f, 4.5f, 4.5f, Reg("velikitrg"));
                }
            }
            // roof
            var tri = Triangulate(P);
            int k0 = mb.v.Count;
            foreach (var p in P) { mb.v.Add(new Vector3(p.x, yr, p.z)); var s = Sw("lgrey", 2); mb.uv.Add(new Vector2(s.x, s.z)); }
            for (int i = 0; i < tri.Count; i += 3)
            {
                var A = P[tri[i]]; var B = P[tri[i + 1]]; var C = P[tri[i + 2]];
                // up-facing in Unity (clockwise seen from above)
                float cr = (B.x - A.x) * (C.z - A.z) - (B.z - A.z) * (C.x - A.x);
                if (cr < 0) mb.t.AddRange(new[] { k0 + tri[i], k0 + tri[i + 1], k0 + tri[i + 2] }); else mb.t.AddRange(new[] { k0 + tri[i], k0 + tri[i + 2], k0 + tri[i + 1] });
            }
            if (!detail) return;
            // roof plant: HVAC boxes / skylights on a 18 m grid inside, hashed
            float minx = P.Min(p => p.x), maxx = P.Max(p => p.x), minz = P.Min(p => p.z), maxz = P.Max(p => p.z);
            int hcount = 0;
            for (float x = minx + 9; x < maxx - 6; x += 18)
                for (float z = minz + 9; z < maxz - 6; z += 18)
                {
                    var q = new Vector3(x, yr, z); if (!Inside(P, q)) continue;
                    int h = (int)((x * 73856093f + z * 19349663f) % 7); if (h < 0) h = -h;
                    if (h % 3 == 0) { mb.Box(q + Vector3.up * .7f, new Vector3(1.6f, .7f, 1.0f), Quaternion.identity, Sw("steel", 0), null, Sw("steel", 2)); hcount++; }
                    else if (h % 3 == 1) mb.Box(q + Vector3.up * .25f, new Vector3(1.5f, .25f, 3f), Quaternion.identity, Sw("lgrey", 1), null, Sw("glass", 0));
                }
            // delivery docks: the nearest wall to each point, a sectional door + dock shelter + bumpers, a step out of the van's way
            foreach (var dp in b.docks)
            {
                var D = U(dp); float best = 1e9f; Vector3 bp = default, bn = default, bd = default;
                for (int i = 0; i < n; i++)
                {
                    var A = P[i]; var B = P[(i + 1) % n]; var d = B - A; d.y = 0; float len = d.magnitude; if (len < 5) continue;
                    var dir = d / len; float t = Mathf.Clamp(Vector3.Dot(D - A, dir), 2.5f, len - 2.5f); var p = A + dir * t; p.y = 0;
                    float dist = (new Vector3(D.x, 0, D.z) - p).magnitude;
                    if (dist < best) { best = dist; bp = p; bd = dir; bn = new Vector3(dir.z, 0, -dir.x); if (Inside(P, p + bn * .3f)) bn = -bn; }
                }
                var rt = Vector3.Cross(Vector3.up, -bn);
                var dl = bp - rt * 1.9f + bn * .32f; var dr = bp + rt * 1.9f + bn * .32f;
                mb.Wall(dl, dr, y0, y0 + 4.6f, Reg("dock"));
                foreach (float s in new[] { -1, 1 }) mb.Box(bp + rt * (1.95f * s) + bn * .16f + Vector3.up * (y0 + 2.3f), new Vector3(.08f, 2.3f, .16f), Quaternion.LookRotation(-bn), Sw("black", 0));
                mb.Box(bp + bn * .16f + Vector3.up * (y0 + 4.65f), new Vector3(2.0f, .08f, .16f), Quaternion.LookRotation(-bn), Sw("black", 0));
                foreach (float s in new[] { -1.2f, 1.2f }) mb.Box(bp + rt * s + bn * .45f + Vector3.up * (y0 + .5f), new Vector3(.15f, .25f, .12f), Quaternion.LookRotation(-bn), Sw("black", 0));
                // a wall lamp above
                mb.Box(bp + bn * .3f + Vector3.up * (y0 + 5.4f), new Vector3(.3f, .12f, .25f), Quaternion.LookRotation(-bn), Sw("light", 0));
            }
        }

        /// a flat sign panel on a wall: centre point (on the wall plane), its bottom at y
        static void Sign(MB mb, Vector3 c, Vector3 right, Vector3 nrm, float y, float w, float h, Vector4 r)
        {
            var a = c - right * (w / 2); var b = c + right * (w / 2);
            mb.Wall(a, b, y, y + h, r);
            // a thin box edge
            var ia = a - nrm * .12f; var ib = b - nrm * .12f;
            mb.Wall(ia, a, y, y + h, Sw("dgrey", 0)); mb.Wall(b, ib, y, y + h, Sw("dgrey", 0));
            mb.Quad(new Vector3(ia.x, y + h, ia.z), new Vector3(ib.x, y + h, ib.z), new Vector3(b.x, y + h, b.z), new Vector3(a.x, y + h, a.z), Sw("dgrey", 0));
        }

        /// an extra block (the Diskont entrance volume): rect in the game frame, its own height h = w field? (l, w, a; height = b.h + 2.5)
        static void Block(Bld b, Rect r, float y0, MB m0, MB m1)
        {
            var c = W.U(r.x, 0, r.z); var A = new Vector3((float)Math.Cos(r.a), 0, -(float)Math.Sin(r.a)); var N = Vector3.Cross(Vector3.up, A);
            var P = new List<Vector3> { c - A * (float)(r.l / 2) - N * (float)(r.w / 2), c + A * (float)(r.l / 2) - N * (float)(r.w / 2), c + A * (float)(r.l / 2) + N * (float)(r.w / 2), c - A * (float)(r.l / 2) + N * (float)(r.w / 2) };
            var bb = new Bld { id = b.id, style = b.style, h = b.h + 2.5, fx = b.fx, fz = b.fz, tenants = b.tenants };
            Shell(bb, P, y0, m0, true); Shell(bb, P, y0, m1, false);
        }

        static Vector3 RA(Rect r) => new Vector3((float)Math.Cos(r.a), 0, -(float)Math.Sin(r.a));

        /// a solar carport row: posts on the centre line every 7.5 m, a tilted PV deck 2.7 -> 3.3 m
        static void Carport(Rect r, MB mb)
        {
            var c = W.U(r.x, 0, r.z); var A = RA(r); var N = Vector3.Cross(Vector3.up, A);
            float L = (float)r.l, Wd = (float)r.w;
            float y = Ground(c);
            int np = Mathf.Max(2, Mathf.RoundToInt(L / 7.5f) + 1);
            for (int i = 0; i < np; i++)
            {
                var p = c + A * (-L / 2 + .4f + (L - .8f) * i / (np - 1));
                float yp = Ground(p);
                mb.Box(new Vector3(p.x, yp + 1.5f, p.z), new Vector3(.12f, 1.55f, .12f), Quaternion.LookRotation(A), Sw("dgrey", 0));
                mb.Box(new Vector3(p.x, yp + 3.0f, p.z), new Vector3(.1f, .1f, Wd / 2 - .2f), Quaternion.LookRotation(N), Sw("dgrey", 0));
                postCols.Add((new Vector3(p.x, yp + 1.5f, p.z), new Vector3(.3f, 3f, .3f)));
            }
            // deck: segments of 5 m (the PV texture once per segment), tilted across
            int ns = Mathf.Max(1, Mathf.RoundToInt(L / 5f)); float sl = L / ns;
            for (int s = 0; s < ns; s++)
            {
                var a = c + A * (-L / 2 + sl * s); var b = c + A * (-L / 2 + sl * (s + 1));
                Vector3 lo(Vector3 p) => new Vector3((p - N * (Wd / 2)).x, y + 2.75f, (p - N * (Wd / 2)).z);
                Vector3 hi(Vector3 p) => new Vector3((p + N * (Wd / 2)).x, y + 3.25f, (p + N * (Wd / 2)).z);
                mb.Quad(lo(a), lo(b), hi(b), hi(a), Reg("solar"));        // top
                mb.Quad(lo(b), lo(a), hi(a) - Vector3.up * .08f, hi(b) - Vector3.up * .08f, Sw("dgrey", 2)); // underside
            }
            // the deck's edges
            var e0 = c - A * (L / 2) - N * (Wd / 2); var e1 = c + A * (L / 2) - N * (Wd / 2);
            mb.Wall(new Vector3(e0.x, 0, e0.z), new Vector3(e1.x, 0, e1.z), y + 2.6f, y + 2.75f, Sw("dgrey", 1));
        }

        /// the big red parking canopy (Veliki trg): columns on an 8 m grid, a deep red corrugated fascia, a grey underside with light strips
        static void RedCanopy(Rect r, MB mb)
        {
            var c = W.U(r.x, 0, r.z); var A = RA(r); var N = Vector3.Cross(Vector3.up, A);
            float L = (float)r.l, Wd = (float)r.w, y = Ground(c), yU = y + 5.2f, yT = y + 6.6f;
            var q = Quaternion.LookRotation(N);
            int nx = Mathf.Max(2, Mathf.RoundToInt(L / 9f) + 1), nz = Mathf.Max(2, Mathf.RoundToInt(Wd / 9f) + 1);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    var p = c + A * (-L / 2 + 1 + (L - 2) * i / (nx - 1)) + N * (-Wd / 2 + 1 + (Wd - 2) * j / (nz - 1));
                    float yp = Ground(p);
                    mb.Box(new Vector3(p.x, (yp + yU) / 2, p.z), new Vector3(.18f, (yU - yp) / 2, .18f), q, Sw("steel", 1));
                    postCols.Add((new Vector3(p.x, (yp + yU) / 2, p.z), new Vector3(.4f, yU - yp, .4f)));
                }
            Vector3 C(float s, float t, float yy) { var p = c + A * (s * L / 2) + N * (t * Wd / 2); return new Vector3(p.x, yy, p.z); }
            // fascia (outer faces) in segments of ~4 m
            var corners = new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) };
            for (int e = 0; e < 4; e++)
            {
                var (s0, t0) = corners[e]; var (s1, t1) = corners[(e + 1) % 4];
                var a = C(s0, t0, 0); var b = C(s1, t1, 0); var mid = (a + b) / 2; var nrm = (mid - new Vector3(c.x, 0, c.z)); nrm.y = 0; nrm.Normalize();
                var right = Vector3.Cross(Vector3.up, -nrm); if (Vector3.Dot(b - a, right) < 0) { var tmp = a; a = b; b = tmp; }
                float len = (b - a).magnitude; int ns = Mathf.Max(1, Mathf.RoundToInt(len / 4)); var dir = (b - a) / len;
                for (int k = 0; k < ns; k++) mb.Wall(a + dir * (len * k / ns), a + dir * (len * (k + 1) / ns), yU - .1f, yT, Reg("corrugated_red", 0, 1, 0, .6f));
            }
            // top (red) and underside (grey + light strips)
            mb.Quad(C(-1, -1, yT), C(1, -1, yT), C(1, 1, yT), C(-1, 1, yT), Sw("red", 2));
            mb.Quad(C(-1, 1, yU), C(1, 1, yU), C(1, -1, yU), C(-1, -1, yU), Sw("lgrey", 2));
            int strips = Mathf.Max(1, Mathf.RoundToInt(L / 9));
            for (int k = 0; k < strips; k++)
            {
                float s = -1 + (2f * (k + .5f) / strips); float d = 1.2f / L;
                mb.Quad(C(s - d, .9f, yU - .02f), C(s + d, .9f, yU - .02f), C(s + d, -.9f, yU - .02f), C(s - d, -.9f, yU - .02f), Sw("light", 0));
            }
        }

        /// a glazed trolley shelter with two ad panels
        static void Trolley(Rect r, MB mb)
        {
            var c = W.U(r.x, 0, r.z); var A = RA(r); var N = Vector3.Cross(Vector3.up, A);
            float L = (float)r.l, Wd = (float)r.w, y = Ground(c);
            var q = Quaternion.LookRotation(N);
            foreach (float s in new[] { -1f, 1f }) foreach (float t in new[] { -1f, 1f })
                { var p = c + A * (s * L / 2) + N * (t * Wd / 2); mb.Box(new Vector3(p.x, y + 1.15f, p.z), new Vector3(.05f, 1.15f, .05f), q, Sw("steel", 0)); }
            mb.Box(new Vector3(c.x, y + 2.35f, c.z), new Vector3(L / 2 + .1f, .08f, Wd / 2 + .1f), Quaternion.LookRotation(N, Vector3.up) * Quaternion.Euler(0, 90, 0), Sw("steel", 1));
            // the back panel (ads) and glass ends
            var b0 = c - A * (L / 2) + N * (Wd / 2); var b1 = c + A * (L / 2) + N * (Wd / 2);
            var half = (b0 + b1) / 2;
            mb.Wall(b1, half, y + .2f, y + 2.2f, Reg("ad1")); mb.Wall(half, b0, y + .2f, y + 2.2f, Reg("ad2"));
            mb.Wall(b0, b1, y + .2f, y + 2.2f, Sw("glass", 0)); // the back seen from behind
            postCols.Add((new Vector3(c.x, y + 1.1f, c.z), new Vector3(.3f, 2.2f, .3f)));
        }

        /// a small pavilion / kiosk (white box, a tenant sign on its fascia, a glass front)
        static void Pavilion(Bld b, Rect r, MB mb)
        {
            var c = W.U(r.x, 0, r.z); var A = RA(r); var N = Vector3.Cross(Vector3.up, A);
            float L = (float)r.l, Wd = (float)r.w, y = Ground(c);
            var P = new List<Vector3> { c - A * (L / 2) - N * (Wd / 2), c + A * (L / 2) - N * (Wd / 2), c + A * (L / 2) + N * (Wd / 2), c - A * (L / 2) + N * (Wd / 2) };
            var pb = new Bld { id = b.id, style = "park", h = 3.4, fx = b.fx, fz = b.fz, tenants = new List<int> { (int)(Math.Abs(r.x * 7 + r.z * 3)) % 12 } };
            Shell(pb, P, y, mb, true);
        }
    }
}
