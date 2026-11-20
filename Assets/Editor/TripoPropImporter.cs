// Magistrala/Rekviziti: uvezi Tripo modele — AI-generated props (Tripo, image-to-3D: one mesh, one baked baseColor JPEG, some with a
// normal map) turned into prefabs at real size. Per prop: Assets/Models/Props/Tripo/<name>.glb + <name>.json (Config below).
// Output: Assets/Generated/Props/Tripo/<name>/ (meshes, texture, material, <name>.prefab); PropsBuilder places the prefabs.
//   1. GLB read directly (positions, normals, uv, indices, the textures); glTF -> Unity (x mirrored, winding flipped, v flipped).
//   2. Normalise: Config.yaw about y, then one uniform scale so the Config.fit axis is Config.size metres (Tripo fits every model
//      into ~1 m); optional per-axis targets (sx / sy / sz, a euro pallet's 1.2 x 0.8 footprint); pivot = bottom centre.
//   3. LODs: LOD0 the Tripo mesh (decimated to Config.lod0 if above), LOD1 / LOD2 decimated (weld by position, simplify the
//      geometry with UnityMeshSimplifier, each output triangle takes the affine UV map of its nearest source triangle, as
//      TripoCarImporter: UV-preserving decimation stalls on Tripo's fragmented atlas); culled beyond Config.cull m (LODGroup).
//   4. Texture: downscaled to Config.tex, slightly desaturated / levelled (Tripo bakes light and grime into the albedo).
//   5. Material: URP Lit, GPU instancing on; collider: one box (or a trunk capsule / pedestal box), never a mesh collider.
//   6. Optional pedestal (Config.pedestal "column": the Kaštela horseman's stone column, built from boxes / a cylinder).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class TripoPropImporter
    {
        public const string SRC = "Assets/Models/Props/Tripo";
        /// Poly Haven CC0 models (glTF packed to one-material GLBs, CREDITS.md), imported the same way
        public const string SRC_PH = "Assets/Models/Props/PolyHaven";
        public static IEnumerable<string> Sources(string pattern) => Directory.GetFiles(SRC, pattern).Concat(Directory.Exists(SRC_PH) ? Directory.GetFiles(SRC_PH, pattern) : new string[0]);
        public const string OUT = "Assets/Generated/Props/Tripo";
        /// field of view the LOD distances are worked out for (the chase camera)
        const float FOV = 60;

        [Serializable]
        public class Config
        {
            public string name;
            public float yaw;                     // degrees about y applied to the raw model (front = +Z after it)
            public string fit = "max";            // x | y | z | xz (longest horizontal) | max: the axis that gets `size`
            public float size = 1;                // metres
            public float sx, sy, sz;              // optional absolute size per axis (0 = the uniform scale)
            public float sat = .88f, gain = 1.02f, contrast = 1.04f, lift = 0;
            public float smooth = .18f, metal = 0, bump = .8f;
            public bool twoSided;
            public int tex = 1024, lod0 = 10000, lod1 = 2500, lod2 = 600;
            public float cull = 80;               // m (at the chase camera's fov, lodBias 1)
            public string collider = "box";       // box | trunk | none
            public float colShrink = .92f;        // box footprint share
            public float trunkR = .35f, trunkH = 2.5f;
            public bool shadows = true;
            public string pedestal = "";          // "" | column
        }

        [MenuItem("Magistrala/Rekviziti: uvezi Tripo modele")]
        public static void ImportAllMenu() => Debug.Log(ImportAll());

        public static string ImportAll(string only = null)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var glb in Sources("*.glb").OrderBy(x => x))
            {
                if (only != null && Path.GetFileNameWithoutExtension(glb) != only) continue;
                var cfgPath = Path.ChangeExtension(glb, ".json");
                if (!File.Exists(cfgPath)) { sb.AppendLine($"[rekviziti] {Path.GetFileName(glb)}: no {Path.GetFileName(cfgPath)}, skipped"); continue; }
                try { sb.AppendLine(Import(glb.Replace('\\', '/'), cfgPath)); }
                catch (Exception e) { sb.AppendLine($"[rekviziti] {Path.GetFileName(glb)} FAILED: {e}"); }
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        public static Config LoadConfig(string cfgPath)
        {
            var c = JsonUtility.FromJson<Config>(File.ReadAllText(cfgPath));
            if (string.IsNullOrEmpty(c.name)) c.name = Path.GetFileNameWithoutExtension(cfgPath);
            return c;
        }

        public static string PrefabPath(string name) => $"{OUT}/{name}/{name}.prefab";
        public static GameObject Prefab(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(name));

        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = Path.GetDirectoryName(p).Replace('\\', '/'); Dir(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(p)); } }

        // ------------------------------------------------------------------ GLB
        class Raw { public Vector3[] P, N; public Vector2[] UV; public int[] I; public byte[] albedo, normal; }

        static Raw ReadGlb(string path)
        {
            var b = File.ReadAllBytes(path);
            int jl = BitConverter.ToInt32(b, 12);
            var j = MiniJson.O(MiniJson.Parse(System.Text.Encoding.UTF8.GetString(b, 20, jl)));
            int bin = 20 + jl + 8;
            var accs = MiniJson.L(j["accessors"]); var views = MiniJson.L(j["bufferViews"]);
            int ViewOff(Dictionary<string, object> v) => bin + (MiniJson.G(v, "byteOffset") is object o ? (int)MiniJson.D(o) : 0);
            int Off(Dictionary<string, object> a, out int count)
            {
                var v = MiniJson.O(views[(int)MiniJson.D(a["bufferView"])]);
                count = (int)MiniJson.D(a["count"]);
                return ViewOff(v) + (MiniJson.G(a, "byteOffset") is object o2 ? (int)MiniJson.D(o2) : 0);
            }
            var prim = MiniJson.O(MiniJson.L(MiniJson.O(MiniJson.L(j["meshes"])[0])["primitives"])[0]);
            var attr = MiniJson.O(prim["attributes"]);
            Vector3[] V3(int ai) { var a = MiniJson.O(accs[ai]); int o = Off(a, out int n); var r = new Vector3[n]; for (int i = 0; i < n; i++) r[i] = new Vector3(BitConverter.ToSingle(b, o + 12 * i), BitConverter.ToSingle(b, o + 12 * i + 4), BitConverter.ToSingle(b, o + 12 * i + 8)); return r; }
            Vector2[] V2(int ai) { var a = MiniJson.O(accs[ai]); int o = Off(a, out int n); var r = new Vector2[n]; for (int i = 0; i < n; i++) r[i] = new Vector2(BitConverter.ToSingle(b, o + 8 * i), BitConverter.ToSingle(b, o + 8 * i + 4)); return r; }
            var raw = new Raw { P = V3((int)MiniJson.D(attr["POSITION"])), N = V3((int)MiniJson.D(attr["NORMAL"])), UV = V2((int)MiniJson.D(attr["TEXCOORD_0"])) };
            {
                var a = MiniJson.O(accs[(int)MiniJson.D(prim["indices"])]); int o = Off(a, out int n); int ct = (int)MiniJson.D(a["componentType"]);
                raw.I = new int[n];
                for (int i = 0; i < n; i++) raw.I[i] = ct == 5125 ? (int)BitConverter.ToUInt32(b, o + 4 * i) : ct == 5123 ? BitConverter.ToUInt16(b, o + 2 * i) : b[o + i];
            }
            // the textures of the material: baseColor, normal (texture -> image -> bufferView)
            byte[] Image(int texIndex)
            {
                var tex = MiniJson.O(MiniJson.L(j["textures"])[texIndex]);
                var img = MiniJson.O(MiniJson.L(j["images"])[(int)MiniJson.D(tex["source"])]);
                var iv = MiniJson.O(views[(int)MiniJson.D(img["bufferView"])]);
                int io = ViewOff(iv), il = (int)MiniJson.D(iv["byteLength"]);
                var r = new byte[il]; Array.Copy(b, io, r, 0, il); return r;
            }
            var mat = MiniJson.O(MiniJson.L(j["materials"])[prim.ContainsKey("material") ? (int)MiniJson.D(prim["material"]) : 0]);
            var pbr = MiniJson.O(mat["pbrMetallicRoughness"]);
            raw.albedo = Image((int)MiniJson.D(MiniJson.O(pbr["baseColorTexture"])["index"]));
            if (mat.ContainsKey("normalTexture")) raw.normal = Image((int)MiniJson.D(MiniJson.O(mat["normalTexture"])["index"]));
            // the node's transform (Tripo: translation only, a fraction of a mm)
            for (int i = 0; i < raw.P.Length; i++) { raw.P[i].x = -raw.P[i].x; raw.N[i].x = -raw.N[i].x; raw.UV[i].y = 1 - raw.UV[i].y; }
            for (int t = 0; t < raw.I.Length; t += 3) { int s = raw.I[t + 1]; raw.I[t + 1] = raw.I[t + 2]; raw.I[t + 2] = s; }
            return raw;
        }

        // ------------------------------------------------------------------ import one prop
        public static string Import(string glbPath, string cfgPath)
        {
            var cfg = LoadConfig(cfgPath);
            var raw = ReadGlb(glbPath);
            var log = new System.Text.StringBuilder($"[rekviziti] {cfg.name}: {raw.I.Length / 3} tris");
            var size = Normalise(raw, cfg, log);
            string dir = $"{OUT}/{cfg.name}"; Dir(dir);
            var alb = SaveAlbedo(raw.albedo, cfg, $"{dir}/{cfg.name}_albedo.png");
            Texture2D nrm = raw.normal != null ? SaveNormal(raw.normal, cfg, $"{dir}/{cfg.name}_normal.png") : null;
            var mat = Mat($"{dir}/{cfg.name}.mat", cfg, alb, nrm);

            int src = raw.I.Length / 3;
            var m0 = src <= cfg.lod0 ? Plain(raw, cfg.name + "_lod0") : Decimate(raw, cfg.lod0, cfg.name + "_lod0", .0005f);
            var m1 = Decimate(raw, Mathf.Min(cfg.lod1, Tris(m0) / 2), cfg.name + "_lod1", .002f * Mathf.Max(1, size.magnitude));
            var m2 = Decimate(raw, Mathf.Min(cfg.lod2, Tris(m1) / 2), cfg.name + "_lod2", .008f * Mathf.Max(1, size.magnitude));
            // the simplifier can stall on dense scans (Poly Haven's pot: 4.8k tris for a 150 target): never coarser level heavier
            if (Tris(m2) > Tris(m1)) { UnityEngine.Object.DestroyImmediate(m2); m2 = UnityEngine.Object.Instantiate(m1); m2.name = cfg.name + "_lod2"; }
            m0 = Save(m0, $"{dir}/{cfg.name}_lod0.asset"); m1 = Save(m1, $"{dir}/{cfg.name}_lod1.asset"); m2 = Save(m2, $"{dir}/{cfg.name}_lod2.asset");
            Mesh ped = null; Material pedMat = null;
            float top = 0;
            if (cfg.pedestal == "column") { ped = Save(Column(out top), $"{dir}/{cfg.name}_pedestal.asset"); pedMat = StoneMat(dir); }

            var root = new GameObject(cfg.name);
            var lods = new List<LOD>();
            var meshes = new[] { m0, m1, m2 };
            float maxDim = Mathf.Max(size.x, Mathf.Max(size.y + top, size.z));
            // switch distances: LOD1 from 30 % of the cull distance, LOD2 from 60 % (but LOD0 at least to 10 m)
            float[] dist = { Mathf.Max(10, .3f * cfg.cull), Mathf.Max(18, .6f * cfg.cull), cfg.cull };
            for (int l = 0; l < 3; l++)
            {
                var g = new GameObject("lod" + l); g.transform.SetParent(root.transform, false); g.transform.localPosition = new Vector3(0, top, 0);
                g.AddComponent<MeshFilter>().sharedMesh = meshes[l];
                var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
                mr.shadowCastingMode = cfg.shadows && l < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.lightProbeUsage = LightProbeUsage.BlendProbes; mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                var rs = new List<Renderer> { mr };
                if (ped != null)
                {
                    var pg = new GameObject("pedestal" + l); pg.transform.SetParent(root.transform, false);
                    pg.AddComponent<MeshFilter>().sharedMesh = ped;
                    var pr = pg.AddComponent<MeshRenderer>(); pr.sharedMaterial = pedMat; pr.shadowCastingMode = l < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                    rs.Add(pr);
                }
                lods.Add(new LOD(ScreenH(maxDim, dist[l]), rs.ToArray()));
            }
            var lg = root.AddComponent<LODGroup>(); lg.SetLODs(lods.ToArray()); lg.RecalculateBounds(); lg.fadeMode = LODFadeMode.None;
            // collider: never the mesh
            if (cfg.collider == "box")
            {
                var bc = root.AddComponent<BoxCollider>();
                bc.size = new Vector3(size.x * cfg.colShrink, size.y + top, size.z * cfg.colShrink); bc.center = new Vector3(0, (size.y + top) / 2, 0);
            }
            else if (cfg.collider == "trunk")
            {
                var cc = root.AddComponent<CapsuleCollider>(); cc.radius = cfg.trunkR; cc.height = cfg.trunkH; cc.center = new Vector3(0, cfg.trunkH / 2, 0); cc.direction = 1;
            }
            if (ped != null && cfg.collider != "none")
            {
                // the column: a box over its plinth and shaft
                foreach (var c in root.GetComponents<BoxCollider>()) UnityEngine.Object.DestroyImmediate(c);
                var b1 = root.AddComponent<BoxCollider>(); b1.size = new Vector3(PlinthW, PlinthH, PlinthW); b1.center = new Vector3(0, PlinthH / 2, 0);
                var b2 = root.AddComponent<BoxCollider>(); b2.size = new Vector3(ShaftR * 2, top, ShaftR * 2); b2.center = new Vector3(0, top / 2, 0);
            }
            Dir(dir);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath(cfg.name));
            UnityEngine.Object.DestroyImmediate(root);
            log.Append($"; size {size.x:F2} x {size.y:F2} x {size.z:F2} m{(top > 0 ? $" on a {top:F1} m column" : "")}; lods {Tris(m0)}/{Tris(m1)}/{Tris(m2)}; cull {cfg.cull} m; tex {alb.width}{(nrm ? " + normal" : "")}");
            return log.ToString();
        }

        /// relative screen height of an object `size` m tall at `d` m (LODGroup convention, lodBias 1)
        static float ScreenH(float size, float d) => Mathf.Clamp(size / (2 * d * Mathf.Tan(FOV * .5f * Mathf.Deg2Rad)), .0005f, .99f);

        static Vector3 Normalise(Raw r, Config c, System.Text.StringBuilder log)
        {
            var rot = Quaternion.Euler(0, c.yaw, 0);
            for (int i = 0; i < r.P.Length; i++) { r.P[i] = rot * r.P[i]; r.N[i] = rot * r.N[i]; }
            var b = new Bounds(r.P[0], Vector3.zero); foreach (var p in r.P) b.Encapsulate(p);
            float raw = c.fit == "x" ? b.size.x : c.fit == "y" ? b.size.y : c.fit == "z" ? b.size.z : c.fit == "xz" ? Mathf.Max(b.size.x, b.size.z) : Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            float s = c.size / raw;
            var k = new Vector3(c.sx > 0 ? c.sx / b.size.x : s, c.sy > 0 ? c.sy / b.size.y : s, c.sz > 0 ? c.sz / b.size.z : s);
            for (int i = 0; i < r.P.Length; i++)
            {
                var p = r.P[i];
                r.P[i] = new Vector3((p.x - b.center.x) * k.x, (p.y - b.min.y) * k.y, (p.z - b.center.z) * k.z);
                r.N[i] = new Vector3(r.N[i].x / k.x, r.N[i].y / k.y, r.N[i].z / k.z).normalized;
            }
            var size = Vector3.Scale(b.size, k);
            log.Append($", raw {b.size.x:F3} x {b.size.y:F3} x {b.size.z:F3}, scale {k.x:F2} / {k.y:F2} / {k.z:F2}");
            return size;
        }

        // ------------------------------------------------------------------ textures
        static Texture2D Resized(byte[] jpg, int size, bool linear)
        {
            var src = new Texture2D(2, 2, TextureFormat.RGBA32, false, linear); src.LoadImage(jpg);
            int w = Mathf.Min(size, src.width), h = Mathf.Min(size, src.height);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var dst = new Texture2D(w, h, TextureFormat.RGBA32, false, linear); dst.ReadPixels(new Rect(0, 0, w, h), 0, 0); dst.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); UnityEngine.Object.DestroyImmediate(src);
            return dst;
        }

        static Texture2D SaveAlbedo(byte[] jpg, Config c, string path)
        {
            var t = Resized(jpg, c.tex, false);
            var px = t.GetPixels();
            for (int i = 0; i < px.Length; i++)
            {
                var col = px[i]; float l = .299f * col.r + .587f * col.g + .114f * col.b;
                col = Color.Lerp(new Color(l, l, l), col, c.sat);
                col.r = ((col.r - .5f) * c.contrast + .5f) * c.gain + c.lift; col.g = ((col.g - .5f) * c.contrast + .5f) * c.gain + c.lift; col.b = ((col.b - .5f) * c.contrast + .5f) * c.gain + c.lift;
                col.a = 1; px[i] = col;
            }
            t.SetPixels(px); t.Apply();
            return SavePng(t, path, false, c.tex);
        }

        static Texture2D SaveNormal(byte[] img, Config c, string path) => SavePng(Resized(img, Mathf.Min(1024, c.tex), true), path, true, Mathf.Min(1024, c.tex));

        static Texture2D SavePng(Texture2D t, string path, bool normal, int max)
        {
            File.WriteAllBytes(path, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            imp.sRGBTexture = !normal; imp.mipmapEnabled = true; imp.anisoLevel = 4; imp.maxTextureSize = max;
            imp.textureCompression = TextureImporterCompression.CompressedHQ; imp.alphaSource = TextureImporterAlphaSource.None;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Mat(string path, Config c, Texture2D alb, Texture2D nrm)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.shader = Shader.Find("Universal Render Pipeline/Lit");
            m.SetTexture("_BaseMap", alb); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", c.smooth); m.SetFloat("_Metallic", c.metal);
            if (nrm) { m.SetTexture("_BumpMap", nrm); m.SetFloat("_BumpScale", c.bump); m.EnableKeyword("_NORMALMAP"); }
            else { m.SetTexture("_BumpMap", null); m.DisableKeyword("_NORMALMAP"); }
            m.SetFloat("_Cull", c.twoSided ? 0 : 2);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ meshes
        static int Tris(Mesh m) { int n = 0; for (int s = 0; s < m.subMeshCount; s++) n += (int)m.GetIndexCount(s) / 3; return n; }

        static Mesh Save(Mesh m, string path)
        {
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old) { old.Clear(); EditorUtility.CopySerialized(m, old); EditorUtility.SetDirty(old); UnityEngine.Object.DestroyImmediate(m); return old; }
            AssetDatabase.CreateAsset(m, path); return m;
        }

        static Mesh Finish(Mesh m)
        {
            m.RecalculateBounds(); m.RecalculateTangents(); m.UploadMeshData(false);
            return m;
        }

        static Mesh Plain(Raw r, string name)
        {
            var m = new Mesh { name = name, indexFormat = r.P.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(r.P); m.SetNormals(r.N); m.SetUVs(0, r.UV); m.SetTriangles(r.I, 0);
            return Finish(m);
        }

        static Vector3 Centroid(Raw r, int t) => (r.P[r.I[t * 3]] + r.P[r.I[t * 3 + 1]] + r.P[r.I[t * 3 + 2]]) / 3;

        /// as TripoCarImporter.Decimate: weld by position (cell m), simplify the geometry only, then each output triangle gets the
        /// affine UV map of its nearest source triangle (corners extrapolated in that triangle's plane, pulled in when far)
        static Mesh Decimate(Raw r, int target, string name, float cell)
        {
            int nt = r.I.Length / 3;
            var key = new Dictionary<Vector3Int, int>(); var V = new List<Vector3>(); var Nn = new List<Vector3>(); var T = new List<int>(); var cnt = new List<int>();
            var seen = new HashSet<(int, int, int)>();
            for (int t = 0; t < nt; t++)
            {
                int a0 = -1, a1 = -1, a2 = -1;
                for (int q = 0; q < 3; q++)
                {
                    var p = r.P[r.I[t * 3 + q]];
                    var k = new Vector3Int(Mathf.RoundToInt(p.x / cell), Mathf.RoundToInt(p.y / cell), Mathf.RoundToInt(p.z / cell));
                    if (!key.TryGetValue(k, out int j)) { j = V.Count; key[k] = j; V.Add(Vector3.zero); Nn.Add(Vector3.zero); cnt.Add(0); }
                    Nn[j] += r.N[r.I[t * 3 + q]]; V[j] += p; cnt[j]++;
                    if (q == 0) a0 = j; else if (q == 1) a1 = j; else a2 = j;
                }
                if (a0 == a1 || a1 == a2 || a0 == a2) continue;
                int lo3 = Mathf.Min(a0, Mathf.Min(a1, a2)), hi3 = Mathf.Max(a0, Mathf.Max(a1, a2));
                if (!seen.Add((lo3, a0 + a1 + a2 - lo3 - hi3, hi3))) continue;
                T.Add(a0); T.Add(a1); T.Add(a2);
            }
            for (int i = 0; i < Nn.Count; i++) { Nn[i] = Nn[i].normalized; V[i] /= Mathf.Max(1, cnt[i]); }
            var wm = new Mesh { indexFormat = IndexFormat.UInt32 }; wm.SetVertices(V); wm.SetNormals(Nn); wm.SetTriangles(T, 0);
            var ms = new UnityMeshSimplifier.MeshSimplifier();
            var o = UnityMeshSimplifier.SimplificationOptions.Default;
            o.PreserveUVSeamEdges = false; o.PreserveBorderEdges = false; o.EnableSmartLink = false; o.MaxIterationCount = 300; o.Agressiveness = 8;
            ms.SimplificationOptions = o; ms.Initialize(wm); ms.SimplifyMesh(Mathf.Clamp01((float)target / Mathf.Max(1, T.Count / 3)));
            var lo = ms.ToMesh(); var lv = lo.vertices; var ln = lo.normals; var lt = lo.GetTriangles(0);
            UnityEngine.Object.DestroyImmediate(wm);
            // grid of the source triangles' centroids
            var b = new Bounds(r.P[0], Vector3.zero); foreach (var p in r.P) b.Encapsulate(p);
            float gc = Mathf.Max(.02f, b.size.magnitude / 40);
            var grid = new Dictionary<Vector3Int, List<int>>();
            Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / gc), Mathf.FloorToInt(p.y / gc), Mathf.FloorToInt(p.z / gc));
            var cen = new Vector3[nt]; var fnS = new Vector3[nt];
            for (int t = 0; t < nt; t++)
            {
                cen[t] = Centroid(r, t);
                fnS[t] = Vector3.Cross(r.P[r.I[t * 3 + 1]] - r.P[r.I[t * 3]], r.P[r.I[t * 3 + 2]] - r.P[r.I[t * 3]]).normalized;
                var k = Cell(cen[t]); if (!grid.TryGetValue(k, out var l)) grid[k] = l = new List<int>(); l.Add(t);
            }
            var oV = new List<Vector3>(); var oN = new List<Vector3>(); var oU = new List<Vector2>(); var oT = new List<int>();
            for (int t = 0; t < lt.Length; t += 3)
            {
                Vector3 p0 = lv[lt[t]], p1 = lv[lt[t + 1]], p2 = lv[lt[t + 2]], c = (p0 + p1 + p2) / 3;
                var fn = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                int best = -1; float bd = 1e9f; var k0 = Cell(c);
                for (int rr = 0; rr <= 3 && best < 0; rr++)
                    for (int dx = -rr; dx <= rr; dx++) for (int dy = -rr; dy <= rr; dy++) for (int dz = -rr; dz <= rr; dz++)
                    {
                        if (!grid.TryGetValue(k0 + new Vector3Int(dx, dy, dz), out var l)) continue;
                        foreach (int s in l)
                        {
                            float d = (cen[s] - c).sqrMagnitude * (1.5f - .5f * Vector3.Dot(fnS[s], fn));
                            if (d < bd) { bd = d; best = s; }
                        }
                    }
                if (best < 0) best = 0;
                Vector3 a = r.P[r.I[best * 3]], bb = r.P[r.I[best * 3 + 1]], d3 = r.P[r.I[best * 3 + 2]];
                Vector2 ua = r.UV[r.I[best * 3]], ub = r.UV[r.I[best * 3 + 1]], ud = r.UV[r.I[best * 3 + 2]];
                Vector3 e1 = bb - a, e2 = d3 - a; float d11 = Vector3.Dot(e1, e1), d12 = Vector3.Dot(e1, e2), d22 = Vector3.Dot(e2, e2), den = d11 * d22 - d12 * d12;
                Vector2 UVat(Vector3 p)
                {
                    if (Mathf.Abs(den) < 1e-14f) return ua;
                    var e = p - a; float q1 = Vector3.Dot(e, e1), q2 = Vector3.Dot(e, e2);
                    float v = (d22 * q1 - d12 * q2) / den, w = (d11 * q2 - d12 * q1) / den;
                    float u0 = 1 - v - w; float m = Mathf.Min(u0, Mathf.Min(v, w));
                    if (m < -.6f) { float s = .6f / -m; v = (v - 1f / 3) * s + 1f / 3; w = (w - 1f / 3) * s + 1f / 3; u0 = 1 - v - w; }
                    return ua * u0 + ub * v + ud * w;
                }
                for (int q = 0; q < 3; q++)
                {
                    var p = lv[lt[t + q]];
                    oT.Add(oV.Count); oV.Add(p); oN.Add(ln != null && ln.Length == lv.Length ? ln[lt[t + q]] : fn); oU.Add(UVat(p));
                }
            }
            UnityEngine.Object.DestroyImmediate(lo);
            var mesh = new Mesh { name = name, indexFormat = oV.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(oV); mesh.SetNormals(oN); mesh.SetUVs(0, oU); mesh.SetTriangles(oT, 0);
            return Finish(mesh);
        }

        // ------------------------------------------------------------------ the horseman's column (Kaštela, "Vjesnik slobode")
        // From the photo (Wikimedia Commons, Argo Navis 2022, people for scale): a low two-step plinth, a stone column of ashlar
        // drums ~7 m, a rounded block capital, the rider on top.
        const float PlinthW = 3.0f, PlinthH = .42f, ShaftR = .55f, ShaftH = 7.0f, CapW = 1.35f, CapH = .75f;
        static Mesh Column(out float top)
        {
            var V = new List<Vector3>(); var N = new List<Vector3>(); var U = new List<Vector2>(); var T = new List<int>();
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, float uw, float vh)
            {
                int i = V.Count; V.Add(a); V.Add(b); V.Add(c); V.Add(d); for (int k = 0; k < 4; k++) N.Add(n);
                U.Add(new Vector2(0, 0)); U.Add(new Vector2(uw, 0)); U.Add(new Vector2(uw, vh)); U.Add(new Vector2(0, vh));
                T.Add(i); T.Add(i + 2); T.Add(i + 1); T.Add(i); T.Add(i + 3); T.Add(i + 2);
            }
            void Box(float w, float y0, float y1)
            {
                float h = w / 2;
                var c = new[] { new Vector3(-h, 0, -h), new Vector3(h, 0, -h), new Vector3(h, 0, h), new Vector3(-h, 0, h) };
                for (int s = 0; s < 4; s++)
                {
                    var a = c[s]; var b = c[(s + 1) % 4]; var n = Vector3.Cross(b - a, Vector3.up).normalized * -1;
                    Quad(a + Vector3.up * y0, b + Vector3.up * y0, b + Vector3.up * y1, a + Vector3.up * y1, n, w, y1 - y0);
                }
                Quad(new Vector3(-h, y1, -h), new Vector3(h, y1, -h), new Vector3(h, y1, h), new Vector3(-h, y1, h), Vector3.up, w, w);
            }
            Box(PlinthW, -2.5f, .22f); Box(PlinthW - .7f, .22f, PlinthH);
            const int SEG = 20;
            for (int s = 0; s < SEG; s++)
            {
                float a0 = s * Mathf.PI * 2 / SEG, a1 = (s + 1) * Mathf.PI * 2 / SEG;
                var d0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)); var d1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
                int i = V.Count; float y0 = PlinthH, y1 = PlinthH + ShaftH, u0 = (float)s / SEG * 3.5f, u1 = (float)(s + 1) / SEG * 3.5f;
                V.Add(d0 * ShaftR + Vector3.up * y0); V.Add(d1 * ShaftR + Vector3.up * y0); V.Add(d1 * ShaftR + Vector3.up * y1); V.Add(d0 * ShaftR + Vector3.up * y1);
                N.Add(d0); N.Add(d1); N.Add(d1); N.Add(d0);
                U.Add(new Vector2(u0, 0)); U.Add(new Vector2(u1, 0)); U.Add(new Vector2(u1, ShaftH)); U.Add(new Vector2(u0, ShaftH));
                T.Add(i); T.Add(i + 2); T.Add(i + 1); T.Add(i); T.Add(i + 3); T.Add(i + 2);
            }
            Box(CapW, PlinthH + ShaftH, PlinthH + ShaftH + CapH);
            top = PlinthH + ShaftH + CapH;
            var m = new Mesh { name = "column" }; m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, U); m.SetTriangles(T, 0);
            return Finish(m);
        }

        static Material StoneMat(string dir)
        {
            string p = $"{dir}/stone.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", new Color(.93f, .91f, .86f)); m.SetFloat("_Smoothness", .12f);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/concrete_grey_d.jpg"));
            m.SetTextureScale("_BaseMap", new Vector2(.5f, .5f));
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/concrete_n.jpg");
            if (n) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", .6f); }
            m.enableInstancing = true; EditorUtility.SetDirty(m);
            return m;
        }
    }
}
