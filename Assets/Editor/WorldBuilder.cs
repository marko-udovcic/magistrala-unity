// Builds the Magistrala probe world from the web game's data (see WorldData.cs for the coordinate mapping):
//   Roads()    – the owner's junction / road-tile GLBs (01 … 05 + d01–d21 + 47) placed like ui/ModelJunctions2.tsx
//   Terrain()  – Unity terrains from tiles/*.bin heights, lowered under / blended around the models like modelTerrain()
//   Trees(), TileBuildings(), Sea()
// Run: Magistrala.EditorTools.WorldBuilder.BuildAll() (menu Magistrala/Build world).
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
    public static class WorldBuilder
    {
        public const string GEN = "Assets/Generated";
        // core terrain tiles (2 m heightmap) and the backdrop ring (8 m)
        public const int TX0 = -8, TX1 = -1, TZ0 = -7, TZ1 = -5;
        public const int BX0 = -11, BX1 = 2, BZ0 = -9, BZ1 = -3;
        public const float YMIN = -40, YSIZE = 900;

        [MenuItem("Magistrala/Build world")]
        public static void BuildAll()
        {
            Roads(); Terrain(); Trees(); TileBuildings(); Sea();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }

        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = Path.GetDirectoryName(p).Replace('\\', '/'); Dir(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(p)); } }

        public static GameObject Root(string name)
        {
            var world = GameObject.Find("World") ?? new GameObject("World");
            var t = world.transform.Find(name);
            if (t) UnityEngine.Object.DestroyImmediate(t.gameObject);
            var go = new GameObject(name); go.transform.SetParent(world.transform, false); go.isStatic = true;
            return go;
        }

        static void Save(UnityEngine.Object o, string path)
        {
            var old = AssetDatabase.LoadMainAssetAtPath(path);
            if (old != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(o, path);
        }

        // ------------------------------------------------------------------ materials
        public static Texture2D Tex(string name, bool normal = false)
        {
            string p = "Assets/Textures/" + name;
            var imp = AssetImporter.GetAtPath(p) as TextureImporter;
            if (imp != null)
            {
                bool dirty = false;
                if (normal && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; dirty = true; }
                if (imp.anisoLevel < 8) { imp.anisoLevel = 8; dirty = true; }
                if (dirty) imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
        }

        public static Material Lit(string name, Color c, float smooth, float metal = 0, string tex = null, string nrm = null, float nrmScale = 1)
        {
            Dir(GEN + "/Materials");
            string p = $"{GEN}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            if (tex != null) m.SetTexture("_BaseMap", Tex(tex));
            if (nrm != null) { m.SetTexture("_BumpMap", Tex(nrm, true)); m.SetFloat("_BumpScale", nrmScale); m.EnableKeyword("_NORMALMAP"); }
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // classes of the package's vertex colours (all GLBs use only these): asphalt, paint, steel, kerb concrete, light concrete, paving
        static readonly Vector3[] CLS = { new Vector3(.33f, .35f, .37f), new Vector3(.94f, .94f, .92f), new Vector3(.55f, .58f, .6f), new Vector3(.72f, .71f, .66f), new Vector3(.8f, .8f, .77f), new Vector3(.63f, .63f, .61f) };
        static Material[] RoadMats()
        {
            return new[]
            {
                GraphicsPass.AsphaltMaterial() ?? Lit("Asphalt", new Color(.52f, .52f, .53f), .22f, 0, "asphalt_d.jpg", "asphalt_n.jpg", .8f),
                GraphicsPass.PaintMaterial(),
                Lit("Steel", new Color(.62f, .65f, .67f), .55f, .65f),
                Lit("Kerb", new Color(.66f, .65f, .61f), .12f, 0, "concrete_d.jpg", "concrete_n.jpg", .6f),
                Lit("Concrete", new Color(.62f, .62f, .6f), .12f, 0, "concrete_d.jpg", "concrete_n.jpg", .6f),
                Lit("Paving", new Color(.58f, .57f, .55f), .15f, 0, "concrete_d.jpg", "concrete_n.jpg", .8f),
                DeckMat(),
            };
        }

        static Material DeckMat()
        {
            var m = Lit("DeckConcrete", new Color(.7f, .69f, .66f), .12f, 0, "concrete_d.jpg", "concrete_n.jpg", .6f);
            m.SetFloat("_Cull", 0); m.doubleSidedGI = true; EditorUtility.SetDirty(m); return m;
        }

        // ------------------------------------------------------------------ road models
        static readonly string[] DRAW = { "asphalt", "island", "markings", "sidewalk", "guardrail", "deck" };
        static readonly string[] COLL = { "asphalt", "island", "deck", "sidewalk", "guardrail" };

        public struct RoadFrame { public int road; public float u, s, hw, lane; }
        /// modelJunctions.ts roadFrameAt (tol .45): the one road whose band holds (x, z), else road -1
        public static RoadFrame FrameAt(List<(List<double[]> p, double w, int n, int lvl)> roads, double x, double z, double tol = .45)
        {
            var hit = new RoadFrame { road = -1 };
            for (int r = 0; r < roads.Count; r++)
            {
                var P = roads[r].p; double hw = roads[r].w / 2 + tol, s0 = 0; bool found = false; double bd = 0, bu = 0, bs = 0;
                for (int i = 0; i + 1 < P.Count; i++)
                {
                    double dx = P[i + 1][0] - P[i][0], dz = P[i + 1][1] - P[i][1], L = Math.Sqrt(dx * dx + dz * dz); if (L < 1e-9) continue;
                    double tx = dx / L, tz = dz / L, t = (x - P[i][0]) * tx + (z - P[i][1]) * tz;
                    if (t >= -.01 && t <= L + .01) { double u = (x - P[i][0]) * -tz + (z - P[i][1]) * tx; if (!found || Math.Abs(u) < bd) { found = true; bd = Math.Abs(u); bu = u; bs = s0 + t; } }
                    s0 += L;
                }
                if (found && bd <= hw)
                {
                    if (hit.road >= 0) return new RoadFrame { road = -1 };
                    double rw = roads[r].w; int n = Math.Max(1, roads[r].n);
                    hit = new RoadFrame { road = r, u = (float)Math.Max(-rw / 2, Math.Min(rw / 2, bu)), s = (float)bs, hw = (float)(rw / 2), lane = (float)(n >= 2 ? Math.Max(rw / n, 3.1) : rw) };
                }
            }
            return hit;
        }

        [MenuItem("Magistrala/Build roads")]
        public static void Roads()
        {
            var models = WorldData.Models();
            var root = Root("Roads");
            Dir(GEN + "/Roads");
            var mats = RoadMats();
            var phys = new PhysicsMaterial("Asphalt") { dynamicFriction = 1f, staticFriction = 1f, frictionCombine = PhysicsMaterialCombine.Average };
            Save(phys, GEN + "/Roads/Asphalt.physicMaterial");
            var railPhys = new PhysicsMaterial("Rail") { dynamicFriction = .1f, staticFriction = .1f, frictionCombine = PhysicsMaterialCombine.Minimum };
            Save(railPhys, GEN + "/Roads/Rail.physicMaterial");
            int tris = 0;
            foreach (var m in models)
            {
                var glb = Glb.Read(W.KRIZ + m.glb);
                var v = new List<Vector3>[7]; var nr = new List<Vector3>[7]; var uv = new List<Vector2>[7]; var uv1 = new List<Vector2>[7]; var uv2 = new List<Vector2>[7];
                for (int k = 0; k < 7; k++) { v[k] = new List<Vector3>(); nr[k] = new List<Vector3>(); uv[k] = new List<Vector2>(); uv1[k] = new List<Vector2>(); uv2[k] = new List<Vector2>(); }
                var groundRoads = m.roads.FindAll(r => r.lvl == 0);
                var cv = new List<Vector3>(); var ci = new List<int>();
                double L0 = m.lin?[0] ?? 1;
                foreach (var gm in glb)
                {
                    if (Array.IndexOf(DRAW, gm.name) < 0) continue;
                    bool coll = Array.IndexOf(COLL, gm.name) >= 0 && !(gm.name == "guardrail" && m.rails.Count > 0);
                    var P = new Vector3[gm.pos.Length]; var N = new Vector3[gm.pos.Length];
                    float lift = gm.name == "markings" ? .012f : 0;
                    for (int i = 0; i < P.Length; i++)
                    {
                        double x = gm.pos[i].x, y = gm.pos[i].y, z = gm.pos[i].z;
                        if (m.hfix.Count > 0) y += m.HfixAt(x, z);
                        if (m.padE > 0 && x > m.half - 0.02) x += m.padE / L0;
                        if (m.padW > 0 && x < -m.half + 0.02) x -= m.padW / L0;
                        if (m.lin != null) { double nx = m.lin[0] * x + m.lin[1] * z, nz = m.lin[2] * x + m.lin[3] * z; x = nx; z = nz; }
                        P[i] = new Vector3((float)x, (float)y + lift, (float)-z);
                        var n = gm.nrm != null ? gm.nrm[i] : Vector3.up; N[i] = new Vector3(n.x, n.y, -n.z);
                    }
                    int cbase = cv.Count;
                    if (coll) cv.AddRange(P);
                    // road frame of every asphalt vertex (ModelJunctions2 roadAsphalt: lateral u, arc s of the model road whose band holds it)
                    RoadFrame[] fr = null;
                    if (gm.name == "asphalt") { fr = new RoadFrame[P.Length]; for (int i = 0; i < P.Length; i++) fr[i] = FrameAt(groundRoads, m.x + P[i].x, m.z - P[i].z); }
                    for (int t = 0; t + 2 < gm.idx.Length; t += 3)
                    {
                        int a = gm.idx[t], b = gm.idx[t + 2], c = gm.idx[t + 1]; // reversed winding (z flip)
                        int k = 0;
                        if (gm.name == "markings") k = 1;
                        else if (gm.name != "asphalt" && gm.col != null)
                        {
                            var col = gm.col[a]; float best = 9;
                            for (int q = 0; q < CLS.Length; q++) { float d = (CLS[q] - col).sqrMagnitude; if (d < best) { best = d; k = q; } }
                            if (k == 1) k = 4;
                            // the bridge structure (deck, not its asphalt): double-sided — its faces were wound inwards (black footbridge on d08)
                            if (gm.name == "deck" && k != 0) k = 6;
                        }
                        var fn = Vector3.Cross(P[b] - P[a], P[c] - P[a]).normalized;
                        if (gm.name == "deck" && Mathf.Abs(fn.y) < .6f) k = 6; // deck sides / girders (asphalt-coloured in the GLB: the black footbridge)
                        bool sameRoad = fr != null && fr[a].road >= 0 && fr[a].road == fr[b].road && fr[b].road == fr[c].road;
                        foreach (int id in new[] { a, b, c })
                        {
                            var p = P[id]; v[k].Add(p); nr[k].Add(k == 6 ? Vector3.up : Vector3.Dot(N[id], fn) < 0 ? fn : N[id]); // (deck structure: wound inwards in the GLB → lit as from above; other inverted normals flipped)
                            float gx = (float)(m.x - W.OX) + p.x, gz = -(float)(m.z - W.OZ) + p.z;
                            if (k == 0) uv[k].Add(new Vector2(gx, gz)); // asphalt shader: world metres
                            else uv[k].Add(Mathf.Abs(fn.y) > .5f ? new Vector2(gx, gz) * .2f : new Vector2((gx + gz) * .5f, p.y + (float)m.y) * .5f);
                            if (sameRoad) { var f = fr[id]; uv1[k].Add(new Vector2(f.u, f.s)); uv2[k].Add(new Vector2(f.hw, f.lane)); }
                            else { uv1[k].Add(Vector2.zero); uv2[k].Add(Vector2.zero); }
                        }
                        if (coll) { ci.Add(cbase + a); ci.Add(cbase + b); ci.Add(cbase + c); }
                        tris++;
                    }
                }
                var mesh = new Mesh { name = m.key, indexFormat = IndexFormat.UInt32 };
                var allU1 = new List<Vector2>(); var allU2 = new List<Vector2>();
                var allV = new List<Vector3>(); var allN = new List<Vector3>(); var allU = new List<Vector2>(); var subs = new List<int[]>(); var subMats = new List<Material>();
                for (int k = 0; k < 7; k++)
                {
                    if (v[k].Count == 0) continue;
                    int b0 = allV.Count; allV.AddRange(v[k]); allN.AddRange(nr[k]); allU.AddRange(uv[k]); allU1.AddRange(uv1[k]); allU2.AddRange(uv2[k]);
                    subs.Add(Enumerable.Range(b0, v[k].Count).ToArray()); subMats.Add(mats[k]);
                }
                mesh.SetVertices(allV); mesh.SetNormals(allN); mesh.SetUVs(0, allU); mesh.SetUVs(1, allU1); mesh.SetUVs(2, allU2);
                mesh.subMeshCount = subs.Count;
                for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
                mesh.RecalculateTangents(); mesh.RecalculateBounds();
                Save(mesh, $"{GEN}/Roads/{m.key}.asset");
                var cm = new Mesh { name = m.key + "-col", indexFormat = IndexFormat.UInt32 };
                cm.SetVertices(cv); cm.SetTriangles(ci, 0); cm.RecalculateBounds();
                Save(cm, $"{GEN}/Roads/{m.key}-col.asset");

                var go = new GameObject(m.key); go.isStatic = true;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = W.U(m.x, m.y, m.z);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = subMats.ToArray(); mr.shadowCastingMode = ShadowCastingMode.On;
                var mc = go.AddComponent<MeshCollider>(); mc.sharedMesh = cm; mc.sharedMaterial = phys;
                if (m.rails.Count > 0)
                {
                    var rg = new GameObject("rails"); rg.isStatic = true; rg.transform.SetParent(go.transform, false);
                    foreach (var r in m.rails)
                        for (int i = 0; i + 1 < r.Count; i++)
                        {
                            var a = r[i]; var b = r[i + 1];
                            double dx = b[0] - a[0], dz = b[1] - a[1], L = Math.Sqrt(dx * dx + dz * dz);
                            if (L < .05) continue;
                            double y0 = Math.Min(a[2], b[2]), y1 = Math.Max(Math.Max(a[3], b[3]), y0 + 1.4);
                            var seg = new GameObject("r"); seg.isStatic = true; seg.transform.SetParent(rg.transform, false);
                            seg.transform.position = W.U((a[0] + b[0]) / 2, (y0 + y1) / 2, (a[1] + b[1]) / 2);
                            seg.transform.rotation = Quaternion.LookRotation(new Vector3((float)dx, 0, (float)-dz));
                            var bc = seg.AddComponent<BoxCollider>(); bc.size = new Vector3(.24f, (float)(y1 - y0), (float)L + .2f); bc.sharedMaterial = railPhys;
                        }
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Magistrala] roads: {models.Count} models, {tris} triangles");
        }

        // ------------------------------------------------------------------ terrain
        static Texture2D Tinted(string src, string dst, Color mul, Color add, float sat = 1)
        {
            string p = "Assets/Textures/" + dst;
            if (!File.Exists(p))
            {
                var bytes = File.ReadAllBytes("Assets/Textures/" + src);
                var t = new Texture2D(2, 2); t.LoadImage(bytes);
                var px = t.GetPixels();
                for (int i = 0; i < px.Length; i++) { var c = px[i]; float gy = c.r * .3f + c.g * .59f + c.b * .11f; c = Color.Lerp(new Color(gy, gy, gy), c, sat); px[i] = new Color(c.r * mul.r + add.r, c.g * mul.g + add.g, c.b * mul.b + add.b, 1); }
                t.SetPixels(px); File.WriteAllBytes(p, t.EncodeToJPG(92));
                AssetDatabase.ImportAsset(p);
            }
            return Tex(dst);
        }

        static TerrainLayer Layer(string name, Texture2D d, Texture2D n, float tile, float smooth = 0)
        {
            Dir(GEN + "/Terrain");
            var l = new TerrainLayer { diffuseTexture = d, normalMapTexture = n, tileSize = new Vector2(tile, tile), smoothness = smooth, normalScale = 1 };
            Save(l, $"{GEN}/Terrain/{name}.terrainlayer");
            return l;
        }

        static float Perlin(double x, double z, double f) => Mathf.PerlinNoise((float)(x * f + 1000.5), (float)(z * f + 2000.5));

        [MenuItem("Magistrala/Build terrain")]
        public static void Terrain()
        {
            var models = WorldData.Models();
            var root = Root("Terrain");
            // CC0 Poly Haven sets (CREDITS.md): olive dry grass with stones, terra rossa, limestone rock face, pale gravel
            const string PH = "PolyHaven/";
            var layers = new[]
            {
                Layer("DryGrass", Tinted("PolyHaven/aerial_grass_rock_diff.jpg", "grass_maquis_d.jpg", new Color(.56f, .8f, .72f), new Color(0, .006f, .012f), .9f), Tex(PH + "aerial_grass_rock_nor.jpg", true), 9), // olive-green maquis ground
                Layer("RedSoil", Tinted("PolyHaven/dry_ground_rocks_diff.jpg", "soil_ochre_d3.jpg", new Color(.86f, .78f, .64f), new Color(.015f, .012f, .008f), .72f), Tex(PH + "dry_ground_rocks_nor.jpg", true), 6), // ochre-brown soil between olive rows, dry patches
                Layer("Rock", Tinted("PolyHaven/rock_face_03_diff.jpg", "rock_grey_d.jpg", new Color(.9f, .91f, .93f), new Color(.06f, .06f, .07f), 0f), Tex(PH + "rock_face_03_nor.jpg", true), 9),
                Layer("Gravel", Tex(PH + "gravel_ground_01_diff.jpg"), Tex(PH + "gravel_ground_01_nor.jpg", true), 4),
            };
            var terrMat = AssetDatabase.LoadAssetAtPath<Material>("Packages/com.unity.render-pipelines.universal/Runtime/Materials/TerrainLit.mat");
            Dir(GEN + "/Terrain");
            for (int tz = BZ0; tz <= BZ1; tz++)
                for (int tx = BX0; tx <= BX1; tx++)
                {
                    bool core = tx >= TX0 && tx <= TX1 && tz >= TZ0 && tz <= TZ1;
                    if (Tiles.Get(tx, tz)?.heights == null) continue;
                    int res = core ? 513 : 129; double step = 1024.0 / (res - 1);
                    var near = core ? models.FindAll(m => m.x + m.half + 14 > tx * 1024 && m.x - m.half - 14 < tx * 1024 + 1024 && m.z + m.half + 14 > tz * 1024 && m.z - m.half - 14 < tz * 1024 + 1024) : new List<ModelJ>();
                    var H = new float[res, res];
                    for (int r = 0; r < res; r++)
                        for (int c = 0; c < res; c++)
                        {
                            double gx = tx * 1024 + c * step, gz = (tz + 1) * 1024 - r * step;
                            double h = Tiles.Height(gx, gz);
                            if (near.Count > 0) { var q = WorldData.ModelTerrain(near, gx, gz, step, h); if (q.HasValue) h = q.Value; }
                            H[r, c] = Mathf.Clamp01((float)((h - YMIN) / YSIZE));
                        }
                    var td = new TerrainData { heightmapResolution = res };
                    td.size = new Vector3(1024, YSIZE, 1024);
                    // asset first: alphamaps set before CreateAsset were lost (every terrain read back as 100 % layer 0 — the flat tan)
                    Save(td, $"{GEN}/Terrain/t_{tx}_{tz}.asset");
                    td.SetHeights(0, 0, H);
                    if (core && near.Count > 0)
                    {
                        // holes under the model asphalt (the road mesh is the ground there: no terrain poking through)
                        int hr = res - 1; var holes = new bool[hr, hr]; int nh = 0;
                        for (int r = 0; r < hr; r++)
                            for (int c = 0; c < hr; c++)
                            {
                                double gx = tx * 1024 + (c + .5) * step, gz = (tz + 1) * 1024 - (r + .5) * step;
                                bool solid = false;
                                for (int k = 0; k < 9 && !solid; k++)
                                {
                                    double px = gx + (k % 3 - 1) * 2.2, pz = gz + (k / 3 - 1) * 2.2; bool on = false;
                                    foreach (var m in near) if (!double.IsNaN(m.Surface(px, pz))) { on = true; break; }
                                    if (!on) solid = true;
                                }
                                holes[r, c] = solid; if (!solid) nh++;
                            }
                        td.SetHoles(0, 0, holes);
                    }
                    // splat
                    int ar = core ? 512 : 128; td.alphamapResolution = ar; td.terrainLayers = layers;
                    var A = new float[ar, ar, 4];
                    double as_ = 1024.0 / ar;
                    for (int r = 0; r < ar; r++)
                        for (int c = 0; c < ar; c++)
                        {
                            double gx = tx * 1024 + (c + .5) * as_, gz = (tz + 1) * 1024 - (r + .5) * as_;
                            var w = new float[4];
                            // bilinear blend of the 8 m ground classes
                            // (sample position jittered by noise: the 8 m class grid read as square blocks from the air)
                            double jx = (Perlin(gx, gz, .09f) - .5) * 14 + (Perlin(gx + 77, gz, .023f) - .5) * 16, jz = (Perlin(gx, gz + 51, .09f) - .5) * 14 + (Perlin(gx, gz - 33, .023f) - .5) * 16;
                            double fx = (gx + jx) / 8 - .5, fz = (gz + jz) / 8 - .5; int ix = (int)Math.Floor(fx), iz = (int)Math.Floor(fz);
                            for (int k = 0; k < 4; k++)
                            {
                                double sx = (ix + (k & 1)) * 8 + 4, sz = (iz + (k >> 1)) * 8 + 4;
                                float bw = (float)((k & 1) == 1 ? fx - ix : 1 - (fx - ix)) * (float)((k >> 1) == 1 ? fz - iz : 1 - (fz - iz));
                                switch (Tiles.Surface(sx, sz))
                                {
                                    case 0: w[0] += .72f * bw; w[1] += .16f * bw; w[2] += .12f * bw; break;
                                    case 1: w[2] += .7f * bw; w[0] += .3f * bw; break;
                                    case 2: w[0] += .68f * bw; w[1] += .32f * bw; break;
                                    case 3: w[1] += .36f * bw; w[0] += .64f * bw; break;
                                    case 4: w[3] += .22f * bw; w[0] += .6f * bw; w[1] += .18f * bw; break;
                                    case 5: w[3] += bw; break;
                                    // manifest surfaceClasses 7–11 (were all falling through to pale gravel: the cream Kozjak slopes)
                                    case 7: w[3] += bw; break;                                                  // mapped paving
                                    case 8: w[1] += .3f * bw; w[0] += .7f * bw; break;                          // olive rows
                                    case 9: w[1] += .36f * bw; w[0] += .64f * bw; break;                        // vineyard rows
                                    case 10: w[0] += .62f * bw; w[2] += .3f * bw; w[1] += .08f * bw; break;     // garrigue
                                    case 11: w[0] += .8f * bw; w[1] += .2f * bw; break;                         // gardens
                                    default: w[0] += .68f * bw; w[1] += .32f * bw; break;
                                }
                            }
                            float n1 = Perlin(gx, gz, .013f) * .6f + Perlin(gx, gz, .0045f) * .5f - .05f, n2 = Perlin(gx, gz, .07f);
                            float n3 = Perlin(gx, gz, .0032f), n4 = Perlin(gx, gz, .021f), n5 = Perlin(gx, gz, .11f); // ochre soil: smaller share than before (.5 + .25 made 100 m orange blobs from the air) and grained at ~6 m, so a patch is
                            // a mix of soil and scrub, not a uniform fill (the near-road and aerial read stay the same in kind: patches of bare soil)
                            float soil = (Mathf.Clamp01((n1 * .45f + n3 * .3f + n4 * .25f - .6f) * 3f) * .44f + Mathf.Clamp01((n5 - .72f) * 3f) * .18f) * (.45f + .55f * Perlin(gx + 311, gz - 97, .16f));
                            w[1] += soil * (w[0] + .1f); w[0] *= 1 - soil * .7f;
                            float rock = Mathf.Clamp01((Perlin(gx, gz, .045f) * .6f + Perlin(gx, gz, .008f) * .5f - .55f) * 4f) * .55f;
                            w[2] += rock * w[0]; w[0] *= 1 - rock * .5f;
                            // slope → rock
                            double dh = Math.Abs(Tiles.Height(gx + 4, gz) - Tiles.Height(gx - 4, gz)) + Math.Abs(Tiles.Height(gx, gz + 4) - Tiles.Height(gx, gz - 4));
                            float slope = Mathf.Clamp01((float)(dh / 8 - .45) * 2.5f);
                            for (int k = 0; k < 4; k++) w[k] *= 1 - slope; w[2] += slope;
                            // Kozjak: bare karst grows with altitude (limestone above ~150 m, mixed with the scrub)
                            float hk = Mathf.Clamp01((float)(Tiles.Height(gx, gz) - 180) / 380f) * (.3f + .45f * Perlin(gx, gz, .02f) + .25f * Perlin(gx, gz, .1f));
                            w[2] += hk * (w[0] + w[1]); w[0] *= 1 - hk; w[1] *= 1 - hk;
                            if (core && near.Count > 0)
                            {
                                double sd = WorldData.SurfDist(near, gx, gz);
                                if (sd < 3.5) { float g = (float)(1 - sd / 3.5) * .55f; for (int k = 0; k < 4; k++) w[k] *= 1 - g; w[3] += g * .7f; w[1] += g * .3f; }
                            }
                            // decisive patches (a soft mix of every layer read as one flat tan from afar)
                            for (int k = 0; k < 4; k++) w[k] = Mathf.Pow(w[k], 1.25f); // (squared gave big hard blotches from the air)
                            float sum = w[0] + w[1] + w[2] + w[3]; if (sum <= 0) { w[0] = 1; sum = 1; }
                            for (int k = 0; k < 4; k++) A[r, c, k] = w[k] / sum;
                        }
                    td.SetAlphamaps(0, 0, A);
                    EditorUtility.SetDirty(td);
                    var go = UnityEngine.Terrain.CreateTerrainGameObject(td);
                    go.name = $"terrain {tx},{tz}"; go.isStatic = true;
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = W.U(tx * 1024, YMIN, (tz + 1) * 1024);
                    var t = go.GetComponent<UnityEngine.Terrain>();
                    if (terrMat) t.materialTemplate = terrMat;
                    t.heightmapPixelError = core ? 3 : 8; t.basemapDistance = 600; t.drawInstanced = true;
                    t.groupingID = core ? 1 : 2; t.allowAutoConnect = true;
                    t.treeDistance = 900; t.treeBillboardDistance = 900; t.detailObjectDistance = 0;
                    t.shadowCastingMode = ShadowCastingMode.On;
                }
            AssetDatabase.SaveAssets();
            Debug.Log("[Magistrala] terrain built");
        }

        // ------------------------------------------------------------------ trees (tile TREES: x, y, z, kind, scale; kind 0 pine, 1 deciduous, 2 cypress, 3 olive, 4 shrub, 5 rock)
        [MenuItem("Magistrala/Build trees")]
        public static void Trees()
        {
            var protos = TreeProtos.Prefabs();
            var models = WorldData.Models();
            int total = 0;
            var rec = new List<byte>();
            foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.Terrain>(FindObjectsSortMode.None))
            {
                var parts = t.name.Split(' ')[1].Split(','); int tx = int.Parse(parts[0]), tz = int.Parse(parts[1]);
                var td = t.terrainData;
                td.SetTreeInstances(new TreeInstance[0], false); td.treePrototypes = new TreePrototype[0];
                var tile = Tiles.Get(tx, tz);
                var list = new List<TreeInstance>();
                bool core = tx >= TX0 && tx <= TX1 && tz >= TZ0 && tz <= TZ1;
                if (core)
                {
                    // tile trees, minus those inside the classified ground-cover area (groundCover.ts), plus the ground-cover trees
                    var raw = new List<float[]>();
                    byte[] cover = null; List<object> gtrees = null;
                    string gp = W.REGION + $"ground/{tx}_{tz}.json";
                    if (File.Exists(gp))
                    {
                        var g = MiniJson.O(MiniJson.Parse(File.ReadAllText(gp)));
                        var rle = MiniJson.L(g["cover"]); cover = new byte[256 * 256]; int o = 0;
                        for (int i = 0; i + 1 < rle.Count; i += 2) { int v = (int)MiniJson.D(rle[i]), n = (int)MiniJson.D(rle[i + 1]); for (int k = 0; k < n && o < cover.Length; k++) cover[o++] = (byte)v; }
                        gtrees = MiniJson.L(g["trees"]);
                    }
                    if (tile?.trees != null)
                        for (int i = 0; i + 4 < tile.trees.Length; i += 5)
                        {
                            var f = tile.trees; int kind = (int)f[i + 3];
                            if (cover != null)
                            {
                                int ix = (int)Math.Floor((f[i] - tx * 1024) / 4.0), iz = (int)Math.Floor((f[i + 2] - tz * 1024) / 4.0);
                                int c = ix < 0 || iz < 0 || ix > 255 || iz > 255 ? 0 : cover[iz * 256 + ix];
                                if (c == 2 || (c == 1 && kind != 5)) continue;
                            }
                            raw.Add(new[] { f[i], f[i + 1], f[i + 2], f[i + 3], f[i + 4] });
                        }
                    if (gtrees != null)
                        for (int i = 0; i + 3 < gtrees.Count; i += 4)
                            raw.Add(new[] { MiniJson.F(gtrees[i]) + tx * 1024, 0f, MiniJson.F(gtrees[i + 1]) + tz * 1024, MiniJson.F(gtrees[i + 2]), MiniJson.F(gtrees[i + 3]) });
                    var rnd = new System.Random(tx * 7919 + tz);
                    foreach (var f in raw)
                    {
                        double gx = f[0], gz = f[2];
                        float h = (float)Tiles.Height(gx, gz);
                        int proto = TreeProtos.Species((int)f[3], gx, h, gz, f[4], out float s);
                        if (proto < 0 || proto >= protos.Count) continue;
                        if (WorldData.FootDist(models, gx, gz) < (proto >= 4 ? 3 : 5)) continue;
                        var q = WorldData.ModelTerrain(models, gx, gz, 2, h); if (q.HasValue) h = (float)q.Value;
                        if (h < .4f) continue;
                        var u = W.U(gx, h - .08, gz);
                        foreach (var v in new[] { u.x, u.y, u.z, (float)(rnd.NextDouble() * 360), s, proto }) rec.AddRange(BitConverter.GetBytes(v));
                        total++;
                    }
                }
            }
            File.WriteAllBytes(GEN + "/trees.bytes", rec.ToArray()); AssetDatabase.ImportAsset(GEN + "/trees.bytes");
            var root = Root("Vegetation");
            var tr = root.AddComponent<Magistrala.TreeRenderer>();
            tr.data = AssetDatabase.LoadAssetAtPath<TextAsset>(GEN + "/trees.bytes");
            AssetDatabase.SaveAssets(); tr.meshes = protos.Select(p => AssetDatabase.LoadAssetAtPath<Mesh>(GEN + "/Plants/" + p.name + ".mesh.asset")).ToArray();
            var mats = new List<Material>(); var starts = new List<int>();
            foreach (var p in protos) { starts.Add(mats.Count); mats.AddRange(p.GetComponent<MeshRenderer>().sharedMaterials); }
            tr.materials = mats.ToArray(); tr.materialStart = starts.ToArray();
            // pine_a, pine_b, olive, cypress, shrub_a, shrub_b, bush_c, rock_a
            tr.drawDistance = new[] { 1400f, 1400f, 1000f, 1400f, 300f, 300f, 260f, 120f };
            tr.nearDistance = new[] { 110f, 110f, 90f, 110f, 45f, 45f, 40f, 120f };
            TreeProtos.Impostors(out var im, out var imat); tr.impMeshes = im; tr.impMaterials = imat;
            Debug.Log($"[Magistrala] trees: {total}");
        }

        // ------------------------------------------------------------------ sea
        [MenuItem("Magistrala/Build sea")]
        public static void Sea()
        {
            var root = Root("Sea");
            var mat = Lit("Sea", new Color(.025f, .13f, .2f), .86f, 0, null, null);
            var nrm = WaterNormal();
            mat.SetTexture("_BumpMap", nrm); mat.SetFloat("_BumpScale", .35f); mat.EnableKeyword("_NORMALMAP");
            mat.SetTextureScale("_BumpMap", new Vector2(1, 1));
            mat.SetFloat("_EnvironmentReflections", 1); mat.SetFloat("_SpecularHighlights", 1);
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "sea"; go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0, 0, 0); go.transform.localScale = new Vector3(2400, 1, 2400); // 24 km
            var mesh = new Mesh { name = "SeaPlane" };
            // plane with uvs in 40 m units so the normal map tiles at world scale
            int n = 64; float size = 24000; var vs = new List<Vector3>(); var us = new List<Vector2>(); var ts = new List<int>();
            for (int j = 0; j <= n; j++) for (int i = 0; i <= n; i++) { float x = -size / 2 + size * i / n, z = -size / 2 + size * j / n; vs.Add(new Vector3(x, 0, z)); us.Add(new Vector2(x, z) / 40f); }
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) { int a = j * (n + 1) + i; ts.AddRange(new[] { a, a + n + 1, a + 1, a + 1, a + n + 1, a + n + 2 }); }
            mesh.SetVertices(vs); mesh.SetUVs(0, us); mesh.SetTriangles(ts, 0); mesh.RecalculateNormals(); mesh.RecalculateTangents();
            Dir(GEN + "/Sea"); Save(mesh, GEN + "/Sea/SeaPlane.asset");
            go.GetComponent<MeshFilter>().sharedMesh = mesh; go.transform.localScale = Vector3.one;
            var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
            if (!go.GetComponent<Magistrala.SeaScroll>()) go.AddComponent<Magistrala.SeaScroll>();
        }

        static Texture2D WaterNormal()
        {
            string p = "Assets/Textures/water_n.png";
            if (!File.Exists(p))
            {
                int N = 512; var h = new float[N, N];
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                    {
                        float v = 0, a = 1, f = 4;
                        for (int o = 0; o < 5; o++) { v += a * Mathf.Sin(2 * Mathf.PI * (x * f / N + Mathf.PerlinNoise(x * f / N * .7f + o * 13, y * f / N * .7f) * 1.5f)) * Mathf.Sin(2 * Mathf.PI * (y * f / N + Mathf.PerlinNoise(y * f / N * .7f + o * 7, x * f / N * .7f + 3) * 1.5f)); a *= .55f; f *= 2; }
                        h[x, y] = v;
                    }
                var t = new Texture2D(N, N, TextureFormat.RGB24, false);
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                    {
                        float dx = h[(x + 1) % N, y] - h[(x + N - 1) % N, y], dy = h[x, (y + 1) % N] - h[x, (y + N - 1) % N];
                        var n = new Vector3(-dx * 2, -dy * 2, 1).normalized;
                        t.SetPixel(x, y, new Color(n.x * .5f + .5f, n.y * .5f + .5f, n.z * .5f + .5f));
                    }
                File.WriteAllBytes(p, t.EncodeToPNG()); AssetDatabase.ImportAsset(p);
            }
            return Tex("water_n.png", true);
        }

        // ------------------------------------------------------------------ tile buildings (footprints, extruded)
        [MenuItem("Magistrala/Build tile buildings")]
        public static void TileBuildings() { BuildingsFromTiles.Build(); }
    }
}
