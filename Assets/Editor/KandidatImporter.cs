// Magistrala/Auti: uvezi kandidata — downloaded car models (Sketchfab etc., Assets/Models/Vehicles/Kandidati/NN_ime.glb, gitignored)
// turned into traffic kinds (TrafficKind: lod0 / lod1 / lod2 parts + far mesh) drawn with the Magistrala/TripoCar shader.
// One entry per car in Assets/Editor/auti-kandidati.json (KCar below; docs/auti-kandidati-postupak.md describes every field).
//   1. GLB read directly (every node / mesh / primitive, node matrices composed, the PNG / JPEG images) -> glTF to Unity (x mirrored).
//   2. Upright: long axis -> z, up = the smaller short axis (or cfg.up), up sign from the wheels, front from the red tail-lamp texels
//      (or cfg.front); scaled to cfg.length; origin = centre of the footprint on the ground, +z forward, +x right.
//   3. Wheels: connected pieces of the wheel materials that touch the ground = the 4 tyres (quadrants); every wheel piece inside a
//      tyre's box joins it (hubcap, rim); the rest (spare wheel) stays static. Wheel meshes have their origin at the wheel centre.
//   4. Brand removal: cfg.strip — pixel rects of a material's texture painted over with the rect's border colour (albedo, roughness,
//      normal flat) and/or 3D boxes whose triangles are deleted.
//   5. Per material a TripoCar texture pair (<= 2K): albedo (a = smoothness) + mask (r paint, b metal). Paint texels of the body
//      material (hue near the model's own paint) become grey shading, recoloured per instance (one material per cfg.colors entry).
//   6. Lamps: triangles by material + texel colour + end of the car (cfg.lamps) -> own submeshes with lit copies (_LampAll).
//   7. Plates: our quads front / rear (ray-cast onto the body) in the paint submesh, uv.x in 2..3 -> Tools/vehicles/tablice.sh atlas,
//      a different plate per paint colour.
//   8. LODs with UnityMeshSimplifier per submesh; LOD0 = the model (interior decimated, transparent tinted glass), LOD1 / LOD2 no
//      interior, opaque glass; far mesh ~600 triangles with per-triangle colours (TrafficBuilder's shared palette).
//      Optional lod1Glb/lod2Glb: authored geometry, shared LOD0 normalisation/pivots/material slots; no further simplification.
//      Optional namedWheels: wheel_FL/FR/RL/RR subtrees rotate together, independent of tyre topology.
// TrafficBuilder.Kinds() adds them (TrafficKinds below). Output: Assets/Generated/Vehicles/Kandidati/<kind>/.
// Also: before / after screenshots in the AutiParking scene (Shots), source textures dumped for finding badge rects (Builds/kandidati).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Magistrala.Traffic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class KandidatImporter
    {
        public const string SRC = "Assets/Models/Vehicles/Kandidati";
        public const string CFG = "Assets/Editor/auti-kandidati.json";
        public const string OUT = "Assets/Generated/Vehicles/Kandidati";
        const string PLATES = "Assets/Textures/Vehicles/Plates/tablice.png";
        const string GRIME = "Assets/Textures/Vehicles/auto_grime.png";

        // ------------------------------------------------------------------ config
        [Serializable] public class KMat
        {
            public string name;                 // glTF material name (prefix match)
            public string role = "trim";        // body (paint) | trim | interior | wheel | glass | hide | display (city bus destination sign: lamp 7)
            public string tex, texAlt;          // display: its texture (default: the GLB's own) and the other direction's (paths from the project root)
            public float emit = 3f;             // display: brightness (URP Unlit colour × texture, HDR)
            public int size = 2048;             // texture size (<= 2048)
            public float smoothMax = 1, smoothMul = 1, desat = 0, bright = 1;   // look tweaks of the non-paint texels
            public float metalMul = 1;
            public float metal = -1, smooth = -1;  // >= 0: fixed metal / smoothness (untextured chrome, glossy plastic)
            public string texFrom;
            public string alphaFill;            // #rrggbb: transparent texels (lamp lenses of a BLEND atlas) blended towards this colour (what showed through)              // use another material's textures (a plain uniform panel material joins the body's paint set)
        }
        [Serializable] public class KStrip
        {
            public string mat;                  // material name (prefix); empty: any
            public int[] px;                    // x0, y0, x1, y1 in the source image's pixels (top-left origin, as in an image viewer)
            public string fill = "auto";        // auto (median of the rect border) | #rrggbb
            public float[] box;                 // or: x0, y0, z0, x1, y1, z1 (car frame, m): triangles deleted
            public int pieceMax;                // with box: delete only whole connected pieces of at most this many triangles lying in the box (badges)
            public float[] at;                  // or: x, y, end (+1 front / -1 rear), w, h (m): the texture under that rect of the car's end is painted over
            public string note;
        }
        [Serializable] public class KLamp
        {
            public string type;                 // head | tail | indL | indR (indL / indR: amber, split by x < 0 / > 0 automatically if "ind") | beaconA | beaconB (flashing, TrafficKind.beaconShare)
            public string mat;                  // material name (prefix)
            public string color = "any";        // red | amber | light | dark | any — the texel at the triangle centre
            public string end = "front";        // front | rear: only the last `depth` of the length; any: anywhere (no facing test)
            public float depth = .2f, yMin = 0, yMax = 9, facing = .2f;   // facing: min |n.z| towards that end
            public float axMin = 0, axMax = 9;  // |x| range (m): lamps sit at the corners, the grille / badge in the middle
        }
        [Serializable] public class KCar
        {
            public string kind, glb, label, source;
            public float length = 4.3f;
            /// real height (m) when the model is taller than the car at its real length (Marko 22.11.: Yugo / Zastava); 0 = keep
            /// the model's proportions. Only the body above the axles is squashed (wheels stay round)
            public float height;
            public string up = "auto", front = "auto";   // up: auto | x | -x | y | -y | z | -z (raw glTF axis); front: auto | + | - (along the long axis)
            public string glassNode = "glass|staklo|window";
            public KMat[] materials;
            public KStrip[] strip;
            public KLamp[] lamps;
            public float[] probe;               // log the texture rects of the triangles in these boxes (6 floats per box)
            public string[] colors; public float[] colorWeights;
            public float weight = 5, vMul = .95f, a = 1.3f, b = 3f, T = 1.4f;
            // Optional authored LODs in the same glTF frame/material/UV layout as glb. Empty = legacy simplifier.
            public string lod1Glb, lod2Glb;
            public bool namedWheels;            // wheel_FL/FR/RL/RR hierarchy is authoritative (including rims/hubs)
            public int lod1 = 13000, lod2 = 4000, far = 600; public float interiorLod0 = .4f;
            public float lodNear = 25, lodMid = 45;
            public int plateStyle = 1;          // atlas column: 0 EU strip, 1 older without
            public float plateW = .52f, plateH = .11f, plateFrontY = .45f, plateRearY = .5f; public bool plateFront = true, plateRear = true;
            public float paintHueTol = 28, paintSatMin = .22f, paintSmooth = .62f, coatSmooth = .9f, flake = .08f;
            public float grime = .55f, grimeH = .42f;
            public float glassAlpha = .62f;
            public string parkFront = "+";      // AutiParking scene: the raw model's front along +z or -z (for the before / after views)
            public string parkLabel;            // AutiParking bay label of the raw model (default: label) — variants of one model share it
            // Codex preparation folder (relative to the project root, e.g. ../BalkanDriver/assets-src/auti-kandidati/01_bmw_e46):
            // config.json texture_map (clean textures, alpha files), drop_meshes (whole glTF nodes), geometry_patches (triangle ranges)
            public string codex;
            public int[] keepNodes;             // drop_meshes nodes kept anyway (their texture is already cleaned)
            public string dropNodes;            // regex: glTF node names dropped (ground tiles etc.)
            public string paintMode = "hue";    // hue: texels near the paint's hue | light: bright unsaturated texels (white cars) | all: the whole body material
            public int[] paintRects;            // body texture px rects (x0, y0, x1, y1, ...; top-left origin): paint only inside them
            public float paintSatMax = .14f, paintVMin = .55f;   // paintMode light
            public bool noPaint;                // livery (police, ambulance): the body texture as is, one white "paint"
            public float beaconShare;           // share of the cars driving with beacons on (lamps beaconA / beaconB)
            public KExtra[] extra;              // extra GLBs merged in before uprighting (police light bar)
            public int lod0;                    // LOD0 triangle cap (body + wheels, interior aside); 0 = the model as is
            public KNodeMat[] nodeMats;
            public bool glassOpaque;
            public float headMetal = .6f;       // headlamp submesh metal (chrome reflector look); clear lenses: lower            // no interior behind the windows: opaque dark glass on LOD0 too         // split a material by node path: matching primitives get a copy "<material>_<suffix>" (own role)
        }
        [Serializable] public class KNodeMat { public string path; public string suffix; }
        [Serializable] public class KExtra
        {
            public string glb;                  // path relative to the project root
            public float[] offset;              // glTF metres in the car's glTF space (after its node transforms)
        }
        [Serializable] class KFile { public KCar[] cars; }

        public static KCar[] Configs() => File.Exists(CFG) ? JsonUtility.FromJson<KFile>(File.ReadAllText(CFG)).cars ?? new KCar[0] : new KCar[0];

        [Serializable] public class Info
        {
            public string kind; public float len, halfW, h, wheelR, wheelZf, wheelZr;
            public Vector3[] wheels;
            public int tris0, tris1, tris2, trisFar;
            public string[] subMat;     // per body submesh: material asset name ("paint" = the paint set)
            public int[] subLamp;       // per body submesh: lamp code (TrafficPart.lamp)
            public bool[] subLod0Only;  // interior / transparent glass: LOD0 only
            public string[] subLodMat;  // replacement material for LOD1 / LOD2 (glass -> opaque), "" = same
        }

        // ------------------------------------------------------------------ menu
        [MenuItem("Magistrala/Auti: uvezi kandidata")]
        public static void ImportAllMenu()
        {
            var log = ImportAll(null);
            Debug.Log(log);
            if (GameObject.Find("Traffic")) TrafficBuilder.Build();
        }

        /// batch: -executeMethod Magistrala.EditorTools.KandidatImporter.BatchImport [-kandidat zastava]
        public static void BatchImport()
        {
            string only = null; var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-kandidat") only = a[i + 1];
            Debug.Log(ImportAll(only));
        }

        /// batch: import one candidate, then its after shots (-kandidat k -shotdir d [-paint i])
        public static void BatchImportShots()
        {
            BatchImport();
            string kind = null, outDir = "Builds/kandidati/slike"; int paint = 0;
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) { if (a[i] == "-kandidat") kind = a[i + 1]; if (a[i] == "-shotdir") outDir = a[i + 1]; if (a[i] == "-paint") paint = int.Parse(a[i + 1]); }
            Shots(Configs().First(c => c.kind == kind), outDir, "poslije", paint);

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Magistrala.unity"); // never leave the lot as the last scene (main 675c6be)
        }

        /// batch: the main scene, import (-kandidat k), Build traffic, Rekviziti: postavi (parked cars), save, [-player: macOS player]
        public static void BatchGame()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Magistrala.unity");
            BatchImport();
            TrafficBuilder.Build();
            Debug.Log(PropsBuilder.Build());
            EditorSceneManager.SaveOpenScenes();
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-player") >= 0) Debug.Log("[kandidat] " + ProbeBuild.Mac());
        }

        public static string ImportAll(string only)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var c in Configs())
            {
                if (only != null && c.kind != only) continue;
                try { sb.AppendLine(Import(c)); }
                catch (Exception e) { sb.AppendLine($"[kandidat] {c.kind} FAILED: {e}"); }
            }
            AssetDatabase.SaveAssets();
            return sb.ToString();
        }

        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = Path.GetDirectoryName(p).Replace('\\', '/'); Dir(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(p)); } }

        // ------------------------------------------------------------------ GLB (all nodes / meshes / primitives)
        class GMat { public string name; public int bc = -1, mr = -1, nrm = -1; public Color factor = Color.white; public bool blend; public bool sg; public float gloss = 1; }
        class Prim { public string node, path; public int nodeIdx, primIdx; public int wheelNode = -1; public Vector3 wheelOrigin; public int mat; public Vector3[] P, N; public Vector2[] UV; public int[] I; }
        class Glb { public List<Prim> prims = new List<Prim>(); public List<GMat> mats = new List<GMat>(); public List<byte[]> images = new List<byte[]>(); }

        static Glb ReadGlb(string path)
        {
            var b = File.ReadAllBytes(path);
            int jl = BitConverter.ToInt32(b, 12);
            var j = MiniJson.O(MiniJson.Parse(System.Text.Encoding.UTF8.GetString(b, 20, jl)));
            int bin = 20 + jl + 8;
            var accs = MiniJson.L(j["accessors"]); var views = MiniJson.L(j["bufferViews"]);
            float F(object o) => (float)MiniJson.D(o);
            int I(object o) => (int)MiniJson.D(o);
            int ViewOff(Dictionary<string, object> v) => bin + (MiniJson.G(v, "byteOffset") is object o ? I(o) : 0);
            float[][] Floats(int ai, int comps)
            {
                var a = MiniJson.O(accs[ai]); var v = MiniJson.O(views[I(a["bufferView"])]);
                if (I(a["componentType"]) != 5126) throw new Exception("accessor not float");
                int n = I(a["count"]), off = ViewOff(v) + (MiniJson.G(a, "byteOffset") is object o ? I(o) : 0);
                int stride = MiniJson.G(v, "byteStride") is object s ? I(s) : comps * 4;
                var r = new float[n][];
                for (int i = 0; i < n; i++) { r[i] = new float[comps]; for (int c = 0; c < comps; c++) r[i][c] = BitConverter.ToSingle(b, off + i * stride + 4 * c); }
                return r;
            }
            var g = new Glb();
            // images, materials
            if (MiniJson.G(j, "images") is object imgsO)
                foreach (var io in MiniJson.L(imgsO))
                {
                    var im = MiniJson.O(io); var v = MiniJson.O(views[I(im["bufferView"])]);
                    int o = ViewOff(v), l = I(v["byteLength"]); var bytes = new byte[l]; Array.Copy(b, o, bytes, 0, l); g.images.Add(bytes);
                }
            var texs = MiniJson.G(j, "textures") is object to ? MiniJson.L(to) : new List<object>();
            int Img(object texRef) => texRef == null ? -1 : I(MiniJson.O(texs[I(MiniJson.O(texRef)["index"])])["source"]);
            foreach (var mo in MiniJson.L(j["materials"]))
            {
                var m = MiniJson.O(mo); var gm = new GMat { name = MiniJson.G(m, "name") as string ?? "mat" };
                if (MiniJson.G(m, "pbrMetallicRoughness") is object pbrO)
                {
                    var pbr = MiniJson.O(pbrO);
                    gm.bc = Img(MiniJson.G(pbr, "baseColorTexture")); gm.mr = Img(MiniJson.G(pbr, "metallicRoughnessTexture"));
                    if (MiniJson.G(pbr, "baseColorFactor") is object bf) { var l = MiniJson.L(bf); gm.factor = new Color(F(l[0]), F(l[1]), F(l[2]), F(l[3])); }
                }
                // older exports: KHR_materials_pbrSpecularGlossiness (diffuse + specular / glossiness in alpha)
                if (MiniJson.G(m, "extensions") is object exO && MiniJson.G(MiniJson.O(exO), "KHR_materials_pbrSpecularGlossiness") is object sgO)
                {
                    var sg = MiniJson.O(sgO); gm.sg = true;
                    if (MiniJson.G(sg, "diffuseTexture") is object dt) gm.bc = Img(dt);
                    if (MiniJson.G(sg, "specularGlossinessTexture") is object st) gm.mr = Img(st);
                    if (MiniJson.G(sg, "diffuseFactor") is object df) { var l = MiniJson.L(df); gm.factor = new Color(F(l[0]), F(l[1]), F(l[2]), F(l[3])); }
                    gm.gloss = MiniJson.G(sg, "glossinessFactor") is object gf ? F(gf) : 1;
                }
                gm.nrm = Img(MiniJson.G(m, "normalTexture"));
                gm.blend = (MiniJson.G(m, "alphaMode") as string) == "BLEND";
                g.mats.Add(gm);
            }
            var nodes = MiniJson.L(j["nodes"]); var meshes = MiniJson.L(j["meshes"]);
            Matrix4x4 Local(Dictionary<string, object> n)
            {
                if (MiniJson.G(n, "matrix") is object mo)
                {
                    var l = MiniJson.L(mo); var m = new Matrix4x4();
                    for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) m[r, c] = F(l[c * 4 + r]);
                    return m;
                }
                var t = Vector3.zero; var q = Quaternion.identity; var s = Vector3.one;
                if (MiniJson.G(n, "translation") is object tt) { var l = MiniJson.L(tt); t = new Vector3(F(l[0]), F(l[1]), F(l[2])); }
                if (MiniJson.G(n, "rotation") is object rr) { var l = MiniJson.L(rr); q = new Quaternion(F(l[0]), F(l[1]), F(l[2]), F(l[3])); }
                if (MiniJson.G(n, "scale") is object ss) { var l = MiniJson.L(ss); s = new Vector3(F(l[0]), F(l[1]), F(l[2])); }
                return Matrix4x4.TRS(t, q, s);
            }
            void Walk(int ni, Matrix4x4 parent, string ppath, int wheel = -1, Vector3 wheelOrigin = default)
            {
                var n = MiniJson.O(nodes[ni]); var M = parent * Local(n);
                string name = MiniJson.G(n, "name") as string ?? "", path = ppath + "/" + name;
                int namedWheel = name == "wheel_FL" ? 0 : name == "wheel_FR" ? 1 : name == "wheel_RL" ? 2 : name == "wheel_RR" ? 3 : -1;
                if (namedWheel >= 0) { wheel = namedWheel; wheelOrigin = M.MultiplyPoint3x4(Vector3.zero); }
                if (MiniJson.G(n, "mesh") is object meshI)
                {
                    var nm = M.inverse.transpose; bool flip = M.determinant < 0;
                    var primList = MiniJson.L(MiniJson.O(meshes[I(meshI)])["primitives"]);
                    for (int pIdx = 0; pIdx < primList.Count; pIdx++)
                    {
                        var p = MiniJson.O(primList[pIdx]); var at = MiniJson.O(p["attributes"]);
                        if (MiniJson.G(p, "mode") is object md && I(md) != 4) continue;
                        var pos = Floats(I(at["POSITION"]), 3);
                        var nor = MiniJson.G(at, "NORMAL") is object na ? Floats(I(na), 3) : null;
                        var uv = MiniJson.G(at, "TEXCOORD_0") is object ua ? Floats(I(ua), 2) : null;
                        var pr = new Prim { wheelNode = wheel, wheelOrigin = new Vector3(-wheelOrigin.x, wheelOrigin.y, wheelOrigin.z), node = name, path = path, nodeIdx = ni, primIdx = pIdx, mat = MiniJson.G(p, "material") is object mi ? I(mi) : -1, P = new Vector3[pos.Length], N = new Vector3[pos.Length], UV = new Vector2[pos.Length] };
                        for (int i = 0; i < pos.Length; i++)
                        {
                            var w = M.MultiplyPoint3x4(new Vector3(pos[i][0], pos[i][1], pos[i][2]));
                            var nn = nor != null ? nm.MultiplyVector(new Vector3(nor[i][0], nor[i][1], nor[i][2])).normalized : Vector3.up;
                            pr.P[i] = new Vector3(-w.x, w.y, w.z); pr.N[i] = new Vector3(-nn.x, nn.y, nn.z);   // glTF -> Unity: x mirrored
                            pr.UV[i] = uv != null ? new Vector2(uv[i][0], 1 - uv[i][1]) : Vector2.zero;        // v up
                        }
                        if (MiniJson.G(p, "indices") is object ii)
                        {
                            var a = MiniJson.O(accs[I(ii)]); var v = MiniJson.O(views[I(a["bufferView"])]);
                            int n2 = I(a["count"]), ct = I(a["componentType"]), off = ViewOff(v) + (MiniJson.G(a, "byteOffset") is object o ? I(o) : 0);
                            pr.I = new int[n2];
                            for (int i = 0; i < n2; i++) pr.I[i] = ct == 5125 ? (int)BitConverter.ToUInt32(b, off + 4 * i) : ct == 5123 ? BitConverter.ToUInt16(b, off + 2 * i) : b[off + i];
                        }
                        else pr.I = Enumerable.Range(0, pos.Length).ToArray();
                        // winding: the x mirror flips it, a mirrored node matrix flips it back
                        if (!flip) for (int t = 0; t < pr.I.Length; t += 3) { int s = pr.I[t + 1]; pr.I[t + 1] = pr.I[t + 2]; pr.I[t + 2] = s; }
                        g.prims.Add(pr);
                    }
                }
                if (MiniJson.G(n, "children") is object ch) foreach (var c in MiniJson.L(ch)) Walk(I(c), M, path, wheel, wheelOrigin);
            }
            var scene = MiniJson.O(MiniJson.L(j["scenes"])[MiniJson.G(j, "scene") is object sc ? I(sc) : 0]);
            foreach (var r in MiniJson.L(scene["nodes"])) Walk(I(r), Matrix4x4.identity, "");
            return g;
        }

        // ------------------------------------------------------------------ texture sampling
        class Tex
        {
            public int w, h; public Color32[] px;
            public static Tex Load(byte[] bytes)
            {
                var t2 = new Texture2D(2, 2, TextureFormat.RGBA32, false, true); t2.LoadImage(bytes);
                var t = new Tex { w = t2.width, h = t2.height, px = t2.GetPixels32() };
                UnityEngine.Object.DestroyImmediate(t2); return t;
            }
            public Color At(Vector2 uv)
            {
                float u = uv.x - Mathf.Floor(uv.x), v = uv.y - Mathf.Floor(uv.y);
                int x = Mathf.Clamp((int)(u * w), 0, w - 1), y = Mathf.Clamp((int)(v * h), 0, h - 1);
                return px[y * w + x];
            }
        }
        static float Lum(Color c) => .299f * c.r + .587f * c.g + .114f * c.b;
        static bool IsRed(Color c) => c.r > .3f && c.r > 1.7f * c.g && c.r > 1.5f * c.b;
        static bool IsAmber(Color c) => c.r > .45f && c.g > .2f && c.g < .8f * c.r && c.b < .55f * c.g;
        static bool IsLight(Color c) { Color.RGBToHSV(c, out _, out float s, out float v); return v > .55f && s < .3f; }

        // ------------------------------------------------------------------ the car
        const int C_BASE = 0, C_HEAD = 1, C_TAIL = 2, C_INDL = 3, C_INDR = 4, C_GLASS = 5, C_BCNA = 6, C_BCNB = 7;
        class Tri { public int prim, t; public int mat; public int cls; public int wheel = -1; public bool cut; }
        class Car
        {
            public KCar cfg; public Glb g; public Tex[] bc, mr, nrm; public KMat[] km;   // per glTF material
            public List<Tri> tris = new List<Tri>();
            public float L, hw, H; public Matrix4x4 modelToCar;
            public Vector3[] wc = new Vector3[4]; public float[] wr = new float[4];
        }
        static Vector3 P(Car c, Tri t, int q) { var p = c.g.prims[t.prim]; return p.P[p.I[t.t + q]]; }
        static Vector3 Ctr(Car c, Tri t) => (P(c, t, 0) + P(c, t, 1) + P(c, t, 2)) / 3;
        static Vector2 CtrUV(Car c, Tri t) { var p = c.g.prims[t.prim]; return (p.UV[p.I[t.t]] + p.UV[p.I[t.t + 1]] + p.UV[p.I[t.t + 2]]) / 3; }
        static Vector3 FaceN(Car c, Tri t) => Vector3.Cross(P(c, t, 1) - P(c, t, 0), P(c, t, 2) - P(c, t, 0));

        static KMat MatCfg(KCar cfg, string name)
        {
            var m = cfg.materials?.FirstOrDefault(k => name.StartsWith(k.name, StringComparison.OrdinalIgnoreCase));
            return m ?? new KMat { name = name, role = System.Text.RegularExpressions.Regex.IsMatch(name, "tyre|tire|wheel|rim", System.Text.RegularExpressions.RegexOptions.IgnoreCase) ? "wheel" : "trim" };
        }

        public static string Import(KCar cfg)
        {
            var log = new System.Text.StringBuilder($"[kandidat] {cfg.kind} ({cfg.glb})");
            var c = new Car { cfg = cfg, g = ReadGlb(GlbPath(cfg.glb)) };
            // extra GLBs (light bar): own materials / images appended, placed at the offset in the car's glTF space
            if (cfg.extra != null)
                foreach (var ex in cfg.extra)
                {
                    var e = ReadGlb(ex.glb); int mo = c.g.mats.Count, io = c.g.images.Count;
                    var off = ex.offset != null && ex.offset.Length == 3 ? new Vector3(-ex.offset[0], ex.offset[1], ex.offset[2]) : Vector3.zero;   // glTF -> Unity: x mirrored
                    c.g.images.AddRange(e.images);
                    foreach (var m in e.mats) { if (m.bc >= 0) m.bc += io; if (m.mr >= 0) m.mr += io; if (m.nrm >= 0) m.nrm += io; c.g.mats.Add(m); }
                    foreach (var pr in e.prims) { pr.nodeIdx = -1; if (pr.mat >= 0) pr.mat += mo; for (int i = 0; i < pr.P.Length; i++) pr.P[i] += off; c.g.prims.Add(pr); }
                    log.Append($"; extra {Path.GetFileName(ex.glb)}: {e.prims.Sum(q => q.I.Length / 3)} tris");
                }
            // Codex preparation: clean textures (+ alpha), whole nodes dropped, triangle ranges removed
            var cut = new HashSet<(int node, int prim, int tri)>(); var dropN = new HashSet<int>(); var alphaImg = new Dictionary<int, byte[]>();
            if (!string.IsNullOrEmpty(cfg.codex)) CodexPrep(c, cfg, dropN, cut, alphaImg, log);
            if (cfg.nodeMats != null)
                foreach (var nmr in cfg.nodeMats)
                {
                    var rx = new System.Text.RegularExpressions.Regex(nmr.path, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    var copies = new Dictionary<int, int>(); int nP = 0;
                    foreach (var pr in c.g.prims)
                    {
                        if (pr.mat < 0 || !rx.IsMatch(pr.path ?? pr.node)) continue;
                        if (!copies.TryGetValue(pr.mat, out int cm))
                        {
                            var o = c.g.mats[pr.mat];
                            c.g.mats.Add(new GMat { name = o.name + "_" + nmr.suffix, bc = o.bc, mr = o.mr, nrm = o.nrm, factor = o.factor, blend = o.blend, sg = o.sg, gloss = o.gloss });
                            copies[pr.mat] = cm = c.g.mats.Count - 1;
                        }
                        pr.mat = cm; nP++;
                    }
                    log.Append($"; nodeMats {nmr.suffix}: {nP} primitives");
                }
            int nm = c.g.mats.Count;
            c.bc = new Tex[nm]; c.mr = new Tex[nm]; c.nrm = new Tex[nm]; c.km = new KMat[nm];
            var texCache = new Dictionary<int, Tex>();
            Tex T(int i)
            {
                if (i < 0) return null;
                if (!texCache.TryGetValue(i, out var t))
                {
                    texCache[i] = t = Tex.Load(c.g.images[i]);
                    if (alphaImg.TryGetValue(i, out var ab)) { var a = Tex.Load(ab); for (int k = 0; k < t.px.Length; k++) { int x = k % t.w * a.w / t.w, y = k / t.w * a.h / t.h; t.px[k].a = a.px[y * a.w + x].r; } }
                }
                return t;
            }
            for (int m = 0; m < nm; m++)
            {
                c.km[m] = MatCfg(cfg, c.g.mats[m].name);
                if (!string.IsNullOrEmpty(c.km[m].texFrom))
                {
                    var src = c.g.mats.FirstOrDefault(q => q.name.StartsWith(c.km[m].texFrom, StringComparison.OrdinalIgnoreCase) && q != c.g.mats[m]);
                    if (src != null) { c.g.mats[m].bc = src.bc; c.g.mats[m].mr = src.mr; c.g.mats[m].nrm = src.nrm; c.g.mats[m].factor = src.factor; }
                }
                c.bc[m] = T(c.g.mats[m].bc); c.mr[m] = T(c.g.mats[m].mr); c.nrm[m] = T(c.g.mats[m].nrm);
            }
            int nTri = 0, nCut = 0, nDrop = 0;
            var dropRx = string.IsNullOrEmpty(cfg.dropNodes) ? null : new System.Text.RegularExpressions.Regex(cfg.dropNodes, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            for (int pi = 0; pi < c.g.prims.Count; pi++)
            {
                var p = c.g.prims[pi]; bool glass = System.Text.RegularExpressions.Regex.IsMatch(p.node, cfg.glassNode, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                nTri += p.I.Length / 3;
                if (dropN.Contains(p.nodeIdx) || (dropRx != null && dropRx.IsMatch(p.node))) { nDrop += p.I.Length / 3; continue; }
                for (int t = 0; t < p.I.Length; t += 3)
                {
                    if (cut.Count > 0 && cut.Contains((p.nodeIdx, p.primIdx, t / 3))) { nCut++; continue; }
                    c.tris.Add(new Tri { prim = pi, t = t, mat = Mathf.Max(0, p.mat), cls = glass || (p.mat >= 0 && c.km[p.mat].role == "glass") ? C_GLASS : C_BASE });
                }
            }
            log.Append($": {nTri} tris ({nDrop} dropped nodes, {nCut} cut by patches), {nm} materials ({string.Join(", ", c.g.mats.Select((m, i) => $"{m.name}={c.km[i].role}"))})");
            DumpTextures(c);
            Upright(c, log);
            Wheels(c, log);
            Strip(c, log);
            // Prepared levels already contain the finished geometry, including lamp covers.
            if (string.IsNullOrEmpty(cfg.lod1Glb) && string.IsNullOrEmpty(cfg.lod2Glb)) Covers(c, log);
            Probe(c);
            Lamps(c, log);
            PlateProfile(c);
            string dir = $"{OUT}/{cfg.kind}"; Dir(dir);
            var info = new Info { kind = cfg.kind, len = c.L, halfW = c.hw, h = c.H, wheelR = (c.wr[0] + c.wr[1] + c.wr[2] + c.wr[3]) / 4, wheels = c.wc, wheelZf = (c.wc[0].z + c.wc[1].z) / 2, wheelZr = (c.wc[2].z + c.wc[3].z) / 2 };
            var texSets = BakeTextures(c, dir, log);
            BuildMeshesAndMaterials(c, dir, texSets, info, log);
            File.WriteAllText($"{dir}/{cfg.kind}_info.json", JsonUtility.ToJson(info, true));
            AssetDatabase.ImportAsset($"{dir}/{cfg.kind}_info.json");
            log.Append($"; LOD tris {info.tris0} / {info.tris1} / {info.tris2} / far {info.trisFar}");
            return log.ToString();
        }

        /// a glb path: a bare file name lives in SRC, a path is relative to the project root
        static string GlbPath(string g) => g.Contains("/") ? g : $"{SRC}/{g}";

        /// Codex's config.json (assets-src/auti-kandidati/NN_ime): texture_map -> clean images (+ alpha files), drop_meshes -> nodes,
        /// geometry_patches -> (node, primitive, triangle) to remove (unless marked already applied)
        static void CodexPrep(Car c, KCar cfg, HashSet<int> dropN, HashSet<(int, int, int)> cut, Dictionary<int, byte[]> alphaImg, System.Text.StringBuilder log)
        {
            string dir = cfg.codex.TrimEnd('/');
            var j = MiniJson.O(MiniJson.Parse(File.ReadAllText($"{dir}/config.json")));
            int nTex = 0;
            if (MiniJson.G(j, "texture_map") is object tmO)
                foreach (var o in MiniJson.L(tmO))
                {
                    var e = MiniJson.O(o); int img = (int)MiniJson.D(e["image"]);
                    if (MiniJson.G(e, "clean_file") is string cf && File.Exists($"{dir}/textures-clean/{cf}") && img < c.g.images.Count)
                    {
                        c.g.images[img] = File.ReadAllBytes($"{dir}/textures-clean/{cf}"); nTex++;
                        if (MiniJson.G(e, "alpha_file") is string af && File.Exists($"{dir}/textures-clean/{af}")) alphaImg[img] = File.ReadAllBytes($"{dir}/textures-clean/{af}");
                    }
                }
            var keep = new HashSet<int>(cfg.keepNodes ?? new int[0]);
            if (MiniJson.G(j, "drop_meshes") is object dmO)
                foreach (var o in MiniJson.L(dmO)) { int n = (int)MiniJson.D(MiniJson.O(o)["node"]); if (!keep.Contains(n)) dropN.Add(n); }
            int nPatch = 0;
            if (MiniJson.G(j, "geometry_patches") is object gpO)
                foreach (var o in MiniJson.L(gpO))
                {
                    var e = MiniJson.O(o);
                    if (MiniJson.G(e, "already_applied_in_model_policija") is bool ap && ap && cfg.glb.Contains("policija")) continue;
                    int node = (int)MiniJson.D(e["node"]), prim = MiniJson.G(e, "primitive") is object po ? (int)MiniJson.D(po) : 0;
                    foreach (var r in MiniJson.L(e["triangle_ranges"]))
                    {
                        var rl = MiniJson.L(r); int a = (int)MiniJson.D(rl[0]), n = (int)MiniJson.D(rl[1]);
                        for (int t = a; t < a + n; t++) { cut.Add((node, prim, t)); nPatch++; }
                    }
                }
            log.Append($"; codex {Path.GetFileName(dir)}: {nTex} clean textures ({alphaImg.Count} with alpha), {dropN.Count} nodes dropped, {nPatch} patch triangles");
        }

        /// the colour at a triangle's centre: texture × factor (untextured materials: the factor)
        static Color TexCol(Car c, Tri t) { var f = c.g.mats[t.mat].factor; var tx = c.bc[t.mat]; return tx != null ? tx.At(CtrUV(c, t)) * f : f; }

        /// the source textures (base colour) to Builds/kandidati/<kind>/ for finding badge rects in an image viewer
        static void DumpTextures(Car c)
        {
            string d = $"Builds/kandidati/{c.cfg.kind}"; Directory.CreateDirectory(d);
            for (int m = 0; m < c.g.mats.Count; m++)
                if (c.g.mats[m].bc >= 0) File.WriteAllBytes($"{d}/{m}_{c.g.mats[m].name}_basecolor.png", c.g.images[c.g.mats[m].bc]);
        }

        // ------------------------------------------------------------------ upright, scale, centre
        static void Upright(Car c, System.Text.StringBuilder log)
        {
            // bounds of the kept triangles (dropped nodes / cut triangles / hidden materials leave their vertices behind)
            var kept = c.tris.Where(t => c.km[t.mat].role != "hide").ToList();
            Bounds B() { bool first = true; var b = new Bounds(); foreach (var t in kept) for (int q = 0; q < 3; q++) { var v = P(c, t, q); if (first) { b = new Bounds(v, Vector3.zero); first = false; } else b.Encapsulate(v); } return b; }
            var b0 = B();
            Vector3[] ax = { Vector3.right, Vector3.up, Vector3.forward };
            float[] ext = { b0.size.x, b0.size.y, b0.size.z };
            int L = ext[0] >= ext[1] && ext[0] >= ext[2] ? 0 : ext[1] >= ext[2] ? 1 : 2;
            Vector3 up;
            if (c.cfg.up != "auto")
            {
                // the raw glTF axis named in the config, in Unity's mirrored frame (glTF x -> -x)
                string u = c.cfg.up.TrimStart('-'); float s = c.cfg.up.StartsWith("-") ? -1 : 1;
                up = (u == "x" ? -Vector3.right : u == "y" ? Vector3.up : Vector3.forward) * s;
            }
            else
            {
                int a = (L + 1) % 3, cc = (L + 2) % 3; int H = ext[a] <= ext[cc] ? a : cc; up = ax[H];
                // up sign: the body's centre is above the wheels
                var wsum = Vector3.zero; int wn = 0;
                foreach (var t in c.tris) if (c.km[t.mat].role == "wheel") { wsum += Ctr(c, t); wn++; }
                if (wn > 0 && Vector3.Dot(b0.center - wsum / wn, up) < 0) up = -up;
            }
            var fwd = ax[L];
            // front: the red (tail lamp) texels sit at the rear
            float sum = 0, wsum2 = 0;
            foreach (var t in c.tris)
            {
                if (c.km[t.mat].role == "interior" || c.km[t.mat].role == "wheel" || c.km[t.mat].role == "hide") continue;
                var col = TexCol(c, t); if (!IsRed(col)) continue;
                float ar = FaceN(c, t).magnitude; sum += Vector3.Dot(Ctr(c, t) - b0.center, fwd) * ar; wsum2 += ar;
            }
            if (c.cfg.front == "+" ) { } else if (c.cfg.front == "-") fwd = -fwd;
            else if (wsum2 > 0 && sum / wsum2 > 0) fwd = -fwd;
            var rot = Quaternion.Inverse(Quaternion.LookRotation(fwd, up));
            foreach (var p in c.g.prims) for (int i = 0; i < p.P.Length; i++) { p.P[i] = rot * p.P[i]; p.N[i] = rot * p.N[i]; }
            // a pure rotation keeps the winding
            var b = B(); float s2 = c.cfg.length / b.size.z;
            foreach (var p in c.g.prims) for (int i = 0; i < p.P.Length; i++) p.P[i] = new Vector3((p.P[i].x - b.center.x) * s2, (p.P[i].y - b.min.y) * s2, (p.P[i].z - b.center.z) * s2);
            c.modelToCar = Matrix4x4.Scale(Vector3.one * s2) * Matrix4x4.Translate(new Vector3(-b.center.x, -b.min.y, -b.center.z)) * Matrix4x4.Rotate(rot);
            b = B();
            if (c.cfg.height > 0 && b.size.y > c.cfg.height)
            {
                // pivot = the axle height (mean centre of the wheel material), wheel vertices untouched
                var wv = new HashSet<(int, int)>(); var wsum3 = Vector3.zero; int wn3 = 0;
                foreach (var t in c.tris) if (c.km[t.mat].role == "wheel") { var pr = c.g.prims[t.prim]; for (int q = 0; q < 3; q++) wv.Add((t.prim, pr.I[t.t + q])); wsum3 += Ctr(c, t); wn3++; }
                float yc = wn3 > 0 ? wsum3.y / wn3 : 0, k = (c.cfg.height - yc) / (b.max.y - yc);
                for (int pi = 0; pi < c.g.prims.Count; pi++)
                {
                    var p = c.g.prims[pi];
                    for (int i = 0; i < p.P.Length; i++)
                    {
                        if (wv.Contains((pi, i))) continue;
                        p.P[i].y = yc + (p.P[i].y - yc) * k;
                        p.N[i] = new Vector3(p.N[i].x, p.N[i].y / k, p.N[i].z).normalized;
                    }
                }
                log.Append($"; height {b.size.y:F2} -> {c.cfg.height:F2} m (body ×{k:F3} above the axle at {yc:F2} m)");
                b = B();
            }
            c.L = b.size.z; c.H = b.size.y;
            // body half width below the mirrors (lower 55 %)
            float hw = 0; foreach (var t in kept) for (int q = 0; q < 3; q++) { var v = P(c, t, q); if (v.y < .55f * c.H) hw = Mathf.Max(hw, Mathf.Abs(v.x)); }
            c.hw = hw;
            log.Append($"; upright: long axis {"xyz"[L]}, up {up}, red centroid {(wsum2 > 0 ? sum / wsum2 : 0):F2} -> front {fwd}; scale {s2:F4}, size {b.size.x:F2} x {b.size.y:F2} x {b.size.z:F2} m");
        }

        // ------------------------------------------------------------------ wheels
        static void Wheels(Car c, System.Text.StringBuilder log)
        {
            if (c.cfg.namedWheels) { NamedWheels(c, log); return; }
            // connected pieces of the wheel materials (welded at 1 mm)
            var wt = c.tris.Where(t => c.km[t.mat].role == "wheel").ToList();
            if (wt.Count == 0) { log.Append("; no wheel material"); return; }
            var parent = new Dictionary<Vector3Int, Vector3Int>();
            Vector3Int K(Vector3 p) => Vector3Int.RoundToInt(p * 1000);
            Vector3Int Find(Vector3Int k) { while (parent.TryGetValue(k, out var q) && q != k) { if (parent.TryGetValue(q, out var qq)) parent[k] = qq; k = q; } return k; }
            void Union(Vector3Int a, Vector3Int b) { if (!parent.ContainsKey(a)) parent[a] = a; if (!parent.ContainsKey(b)) parent[b] = b; var ra = Find(a); var rb = Find(b); if (ra != rb) parent[ra] = rb; }
            foreach (var t in wt) { var a = K(P(c, t, 0)); Union(a, K(P(c, t, 1))); Union(a, K(P(c, t, 2))); }
            var comp = new Dictionary<Vector3Int, (Bounds b, List<Tri> t)>();
            foreach (var t in wt)
            {
                var r = Find(K(P(c, t, 0)));
                if (!comp.TryGetValue(r, out var e)) e = (new Bounds(P(c, t, 0), Vector3.zero), new List<Tri>());
                for (int q = 0; q < 3; q++) e.b.Encapsulate(P(c, t, q)); e.t.Add(t); comp[r] = e;
            }
            // tyres: pieces on the ground, one box per quadrant (front-left, front-right, rear-left, rear-right)
            var box = new Bounds[4]; var has = new bool[4];
            foreach (var e in comp.Values)
            {
                if (e.b.min.y > .04f || e.b.size.y < .3f) continue;
                int w = (e.b.center.z > 0 ? 0 : 2) + (e.b.center.x < 0 ? 0 : 1);
                if (!has[w]) { box[w] = e.b; has[w] = true; } else box[w].Encapsulate(e.b);
            }
            if (has.Any(h => !h)) { log.Append($"; wheels: only {has.Count(h => h)} tyres found, wheels left static"); foreach (var t in wt) t.wheel = -1; return; }
            int moved = 0, stat = 0;
            foreach (var e in comp.Values)
            {
                int w = -1;
                for (int k = 0; k < 4; k++) { var bb = box[k]; bb.Expand(.03f); if (bb.Contains(e.b.center)) { w = k; break; } }
                foreach (var t in e.t) t.wheel = w;
                if (w >= 0) moved += e.t.Count; else stat += e.t.Count;
            }
            for (int k = 0; k < 4; k++) { c.wc[k] = box[k].center; c.wr[k] = box[k].size.y / 2; }
            log.Append($"; wheels r {c.wr.Average():F3} track {c.wc[1].x - c.wc[0].x:F2} base {c.wc[0].z - c.wc[2].z:F2}, {moved} tris spin, {stat} static ({comp.Count} pieces)");
        }

        // Authored wheel pivots survive even extremely sparse LOD tyres. Editor-only work.
        static void NamedWheels(Car c, System.Text.StringBuilder log)
        {
            for (int w = 0; w < 4; w++)
            {
                var pr = c.g.prims.FirstOrDefault(p => p.wheelNode == w);
                if (pr == null) throw new Exception($"{c.cfg.kind}: missing named wheel {w}");
                c.wc[w] = c.modelToCar.MultiplyPoint3x4(pr.wheelOrigin);
                var ts = c.tris.Where(t => c.g.prims[t.prim].wheelNode == w).ToList();
                float r = 0;
                foreach (var t in ts) for (int q = 0; q < 3; q++)
                {
                    var v = P(c, t, q) - c.wc[w];
                    r = Mathf.Max(r, new Vector2(v.y, v.z).magnitude);
                }
                c.wr[w] = r;
                foreach (var t in ts) t.wheel = w;
            }
            // Some inner rear tyres are still in the body mesh. Only wheel-role geometry near an axle joins it.
            foreach (var t in c.tris.Where(t => t.wheel < 0 && c.km[t.mat].role == "wheel"))
            {
                var v = Ctr(c, t); int w = 0;
                for (int k = 1; k < 4; k++) if ((v - c.wc[k]).sqrMagnitude < (v - c.wc[w]).sqrMagnitude) w = k;
                var d = v - c.wc[w];
                if (Mathf.Abs(d.x) < .6f && new Vector2(d.y, d.z).magnitude < c.wr[w] * 1.1f) t.wheel = w;
            }
            log.Append($"; named wheels: {c.tris.Count(t => t.wheel >= 0)} tris spin");
        }

        static Car AuthoredLod(Car source, string path, System.Text.StringBuilder log)
        {
            if (!source.cfg.namedWheels || source.cfg.height > 0 || source.cfg.extra?.Length > 0 ||
                !string.IsNullOrEmpty(source.cfg.codex) || source.cfg.nodeMats?.Length > 0 || source.cfg.strip?.Length > 0)
                throw new Exception("authored LODs require namedWheels and a shared, unpatched glTF frame/material layout");
            var g = ReadGlb(GlbPath(path));
            var map = g.mats.Select(m => source.g.mats.FindIndex(x => x.name == m.name)).ToArray();
            if (map.Any(m => m < 0)) throw new Exception($"{path}: material absent from LOD0");
            var c = new Car { cfg = source.cfg, g = g, km = source.km, bc = source.bc, mr = source.mr, nrm = source.nrm,
                modelToCar = source.modelToCar, L = source.L, H = source.H, hw = source.hw };
            foreach (var p in g.prims)
            {
                if (p.mat < 0) throw new Exception($"{path}: primitive without a material");
                p.mat = map[p.mat];
                for (int i = 0; i < p.P.Length; i++)
                {
                    p.P[i] = c.modelToCar.MultiplyPoint3x4(p.P[i]);
                    p.N[i] = c.modelToCar.MultiplyVector(p.N[i]).normalized;
                }
            }
            g.mats = source.g.mats; // remapped IDs and the same baked textures/material assets
            for (int pi = 0; pi < g.prims.Count; pi++)
            {
                var p = g.prims[pi];
                bool glass = c.km[p.mat].role == "glass" || System.Text.RegularExpressions.Regex.IsMatch(p.node, c.cfg.glassNode, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                for (int t = 0; t < p.I.Length; t += 3) c.tris.Add(new Tri { prim = pi, t = t, mat = p.mat, cls = glass ? C_GLASS : C_BASE });
            }
            Wheels(c, log);
            // One pivot per runtime wheel for all levels; do not recenter a decimated tyre's bounds.
            c.wc = source.wc; c.wr = source.wr;
            Lamps(c, log);
            log.Append($"; authored {path}: {c.tris.Count} tris");
            return c;
        }

        // ------------------------------------------------------------------ brand removal
        static void Strip(Car c, System.Text.StringBuilder log)
        {
            if (c.cfg.strip == null) return;
            foreach (var s in c.cfg.strip)
            {
                bool MatOk(Tri t) => string.IsNullOrEmpty(s.mat) || c.g.mats[t.mat].name.StartsWith(s.mat, StringComparison.OrdinalIgnoreCase);
                if (s.at != null && s.at.Length == 5)
                {
                    // the 3D rect -> a pixel rect: rays along the car's axis at the corners and the centre onto the material's triangles
                    var cand = c.tris.Where(MatOk).ToList(); float zs = s.at[2] >= 0 ? 1 : -1;
                    int m = -1; float x0 = 9, y0 = 9, x1 = -9, y1 = -9;
                    for (int q = 0; q < 9; q++)
                    {
                        float ox = s.at[0] + (q % 3 - 1) * s.at[3] / 2, oy = s.at[1] + (q / 3 - 1) * s.at[4] / 2;
                        if (!Ray(c, cand, new Vector3(ox, oy, zs * (c.L / 2 + 1)), new Vector3(0, 0, -zs), out var t, out var uv)) continue;
                        m = t.mat; x0 = Mathf.Min(x0, uv.x); x1 = Mathf.Max(x1, uv.x); y0 = Mathf.Min(y0, uv.y); y1 = Mathf.Max(y1, uv.y);
                    }
                    if (m < 0 || c.bc[m] == null) { log.Append($"; strip at {s.note}: nothing hit"); }
                    else
                    {
                        int W = c.bc[m].w, Hh = c.bc[m].h;
                        var px = new[] { (int)(x0 * W) - 2, (int)((1 - y1) * Hh) - 2, (int)(x1 * W) + 2, (int)((1 - y0) * Hh) + 2 };
                        if (px[2] - px[0] > W / 4 || px[3] - px[1] > Hh / 4) log.Append($"; strip at {s.note}: rect crosses UV charts ({string.Join(",", px)}), skipped");
                        else { PaintRect(c, m, px, s.fill); log.Append($"; strip at {s.note}: {c.g.mats[m].name} px {string.Join(",", px)}"); }
                    }
                }
                if (s.box != null && s.box.Length == 6)
                {
                    var b = new Bounds(); b.SetMinMax(new Vector3(s.box[0], s.box[1], s.box[2]), new Vector3(s.box[3], s.box[4], s.box[5]));
                    int n = 0;
                    if (s.pieceMax > 0)
                    {
                        foreach (var pc in Pieces(c, c.tris.Where(MatOk).ToList()))
                        {
                            if (pc.Count > s.pieceMax) continue;
                            bool inside = pc.All(t => b.Contains(P(c, t, 0)) && b.Contains(P(c, t, 1)) && b.Contains(P(c, t, 2)));
                            if (inside) foreach (var t in pc) { t.cut = true; n++; }
                        }
                    }
                    else foreach (var t in c.tris) if (MatOk(t) && b.Contains(Ctr(c, t))) { t.cut = true; n++; }
                    log.Append($"; strip box {s.note}: {n} tris deleted");
                }
                if (s.px != null && s.px.Length == 4)
                    for (int m = 0; m < c.g.mats.Count; m++)
                        if (c.g.mats[m].name.StartsWith(s.mat ?? "", StringComparison.OrdinalIgnoreCase)) { PaintRect(c, m, s.px, s.fill); log.Append($"; strip {c.g.mats[m].name} px {string.Join(",", s.px)} {s.note}"); }
            }
            c.tris.RemoveAll(t => t.cut);
        }

        /// role "cover" (transparent lamp covers): the cover's triangles lying over another surface (a lamp behind, within 8 cm) are
        /// dropped – the lamp shows; the rest (cover over nothing, e.g. smoked segments over the dark body) stays as opaque trim
        static void Covers(Car c, System.Text.StringBuilder log)
        {
            var cov = c.tris.Where(t => c.km[t.mat].role == "cover").ToList(); if (cov.Count == 0) return;
            // lamp internals only: a cover over the body paint (smoked segments) stays
            var under = c.tris.Where(t => c.km[t.mat].role != "cover" && c.km[t.mat].role != "body" && t.cls != C_GLASS && c.km[t.mat].role != "hide").ToList();
            int cut = 0;
            foreach (var t in cov)
            {
                var n = FaceN(c, t); if (n.sqrMagnitude < 1e-14f) { t.cut = true; continue; }
                n.Normalize(); var o = Ctr(c, t);
                // both ways (the cover's winding is not reliable): a surface within 8 cm behind or in front
                bool hit = false;
                foreach (var d in new[] { -n, n })
                    if (Ray(c, under, o + d * .001f, d, out var ht, out _))
                    {
                        var a = P(c, ht, 0); var hn = FaceN(c, ht).normalized;
                        float dist = Mathf.Abs(Vector3.Dot(a - o, hn) / Mathf.Max(1e-4f, Mathf.Abs(Vector3.Dot(d, hn))));
                        if (dist < .08f) { hit = true; break; }
                    }
                if (hit) { t.cut = true; cut++; }
            }
            c.tris.RemoveAll(t => t.cut);
            log.Append($"; covers: {cut} of {cov.Count} tris over a lamp dropped, {cov.Count - cut} kept opaque");
        }

        /// paint a pixel rect (source image, top-left origin) of material m over: base colour + roughness with the rect border's median
        /// (or fill), the normal map flat
        static void PaintRect(Car c, int m, int[] px, string fillHex)
        {
            foreach (var tx in new[] { c.bc[m], c.mr[m], c.nrm[m] })
            {
                if (tx == null) continue;
                bool isN = tx == c.nrm[m];
                float sx = tx.w / (float)(c.bc[m]?.w ?? tx.w), sy = tx.h / (float)(c.bc[m]?.h ?? tx.h);
                int x0 = Mathf.Clamp((int)(px[0] * sx), 0, tx.w - 1), x1 = Mathf.Clamp((int)(px[2] * sx), 0, tx.w - 1);
                // image rows top-down -> texture rows bottom-up
                int y0 = Mathf.Clamp(tx.h - 1 - (int)(px[3] * sy), 0, tx.h - 1), y1 = Mathf.Clamp(tx.h - 1 - (int)(px[1] * sy), 0, tx.h - 1);
                Color32 fill;
                if (isN) fill = new Color32(128, 128, 255, 255);
                else if (fillHex != "auto" && tx == c.bc[m] && ColorUtility.TryParseHtmlString(fillHex, out var fc)) fill = fc;
                else
                {
                    var border = new List<Color32>();
                    for (int x = x0; x <= x1; x++) { border.Add(tx.px[Mathf.Max(0, y0 - 2) * tx.w + x]); border.Add(tx.px[Mathf.Min(tx.h - 1, y1 + 2) * tx.w + x]); }
                    for (int y = y0; y <= y1; y++) { border.Add(tx.px[y * tx.w + Mathf.Max(0, x0 - 2)]); border.Add(tx.px[y * tx.w + Mathf.Min(tx.w - 1, x1 + 2)]); }
                    var srt = border.OrderBy(q => q.r + q.g + q.b).ToList(); fill = srt[srt.Count / 2];
                }
                // soft edge (3 px): blend into the surrounding texels so the patch does not show as a hard rectangle
                for (int y = y0 - 3; y <= y1 + 3; y++) for (int x = x0 - 3; x <= x1 + 3; x++)
                {
                    if (x < 0 || y < 0 || x >= tx.w || y >= tx.h) continue;
                    float d = Mathf.Max(Mathf.Max(x0 - x, x - x1), Mathf.Max(y0 - y, y - y1));
                    float a = d <= 0 ? 1 : 1 - d / 4f;
                    int i = y * tx.w + x; tx.px[i] = Color32.Lerp(tx.px[i], fill, a);
                }
            }
        }

        /// nearest hit of a ray on the triangles: the triangle and the UV there
        static bool Ray(Car c, List<Tri> tris, Vector3 o, Vector3 d, out Tri hit, out Vector2 uv)
        {
            hit = null; uv = default; float best = float.MaxValue;
            foreach (var t in tris)
            {
                var a = P(c, t, 0); var e1 = P(c, t, 1) - a; var e2 = P(c, t, 2) - a;
                var pv = Vector3.Cross(d, e2); float det = Vector3.Dot(e1, pv); if (Mathf.Abs(det) < 1e-9f) continue;
                float inv = 1 / det; var tv = o - a; float u = Vector3.Dot(tv, pv) * inv; if (u < 0 || u > 1) continue;
                var qv = Vector3.Cross(tv, e1); float v = Vector3.Dot(d, qv) * inv; if (v < 0 || u + v > 1) continue;
                float dist = Vector3.Dot(e2, qv) * inv;
                if (dist > 0 && dist < best)
                {
                    best = dist; hit = t; var p = c.g.prims[t.prim];
                    uv = p.UV[p.I[t.t]] * (1 - u - v) + p.UV[p.I[t.t + 1]] * u + p.UV[p.I[t.t + 2]] * v;
                }
            }
            return hit != null;
        }

        /// connected pieces (welded at 1 mm)
        static List<List<Tri>> Pieces(Car c, List<Tri> ts)
        {
            var par = new Dictionary<Vector3Int, Vector3Int>();
            Vector3Int K(Vector3 v) => Vector3Int.RoundToInt(v * 1000);
            Vector3Int F(Vector3Int x) { while (par.TryGetValue(x, out var y) && y != x) x = y; return x; }
            void U(Vector3Int a, Vector3Int b) { if (!par.ContainsKey(a)) par[a] = a; if (!par.ContainsKey(b)) par[b] = b; var ra = F(a); var rb = F(b); if (ra != rb) par[ra] = rb; }
            foreach (var t in ts) { var a0 = K(P(c, t, 0)); U(a0, K(P(c, t, 1))); U(a0, K(P(c, t, 2))); }
            return ts.GroupBy(t => F(K(P(c, t, 0)))).Select(g => g.ToList()).ToList();
        }

        static void Probe(Car c)
        {
            if (c.cfg.probe == null) return;
            for (int k = 0; k + 5 < c.cfg.probe.Length; k += 6)
            {
                var pb = c.cfg.probe; var b = new Bounds(); b.SetMinMax(new Vector3(pb[k], pb[k + 1], pb[k + 2]), new Vector3(pb[k + 3], pb[k + 4], pb[k + 5]));
                foreach (var g in c.tris.Where(t => b.Contains(Ctr(c, t))).GroupBy(t => t.mat))
                {
                    var tx = c.bc[g.Key]; int W = tx?.w ?? 1, Hh = tx?.h ?? 1;
                    float u0 = 9, v0 = 9, u1 = -9, v1 = -9;
                    foreach (var t in g) { var p = c.g.prims[t.prim]; for (int q = 0; q < 3; q++) { var uv = p.UV[p.I[t.t + q]]; u0 = Mathf.Min(u0, uv.x); u1 = Mathf.Max(u1, uv.x); v0 = Mathf.Min(v0, uv.y); v1 = Mathf.Max(v1, uv.y); } }
                    var bbs = new Bounds(); bool f = true; foreach (var t in g) { if (f) { bbs = new Bounds(Ctr(c, t), Vector3.zero); f = false; } bbs.Encapsulate(Ctr(c, t)); }
                    Debug.Log($"[kandidat] probe box {k / 6} {c.g.mats[g.Key].name}: {g.Count()} tris, px x {u0 * W:F0}..{u1 * W:F0} y {(1 - v1) * Hh:F0}..{(1 - v0) * Hh:F0} (image, top-left), 3D {bbs.min} .. {bbs.max}");
                    // the separate pieces in the box (a badge is usually its own piece): size, 3D box, texture rect
                    var par = new Dictionary<Vector3Int, Vector3Int>();
                    Vector3Int Kk(Vector3 v) => Vector3Int.RoundToInt(v * 1000);
                    Vector3Int Fd(Vector3Int x) { while (par.TryGetValue(x, out var y) && y != x) x = y; return x; }
                    void Un(Vector3Int a2, Vector3Int b2) { if (!par.ContainsKey(a2)) par[a2] = a2; if (!par.ContainsKey(b2)) par[b2] = b2; var ra = Fd(a2); var rb = Fd(b2); if (ra != rb) par[ra] = rb; }
                    foreach (var t in g) { var a0 = Kk(P(c, t, 0)); Un(a0, Kk(P(c, t, 1))); Un(a0, Kk(P(c, t, 2))); }
                    foreach (var pc in g.GroupBy(t => Fd(Kk(P(c, t, 0)))).OrderByDescending(x => x.Count()).Take(12))
                    {
                        var pbx = new Bounds(Ctr(c, pc.First()), Vector3.zero); float a0 = 9, b0 = 9, a1 = -9, b1 = -9;
                        foreach (var t in pc) { var pr = c.g.prims[t.prim]; for (int q = 0; q < 3; q++) { pbx.Encapsulate(P(c, t, q)); var uv = pr.UV[pr.I[t.t + q]]; a0 = Mathf.Min(a0, uv.x); a1 = Mathf.Max(a1, uv.x); b0 = Mathf.Min(b0, uv.y); b1 = Mathf.Max(b1, uv.y); } }
                        Debug.Log($"[kandidat]   piece {pc.Count()} tris, 3D ({pbx.min.x:F3},{pbx.min.y:F3},{pbx.min.z:F3})..({pbx.max.x:F3},{pbx.max.y:F3},{pbx.max.z:F3}), px x {a0 * W:F0}..{a1 * W:F0} y {(1 - b1) * Hh:F0}..{(1 - b0) * Hh:F0}");
                    }
                }
            }
        }

        /// the outermost body surface along the car's axis at x = 0 (front / rear), every 3 cm of height: where a plate can sit flat
        static void PlateProfile(Car c)
        {
            var solid = c.tris.Where(t => t.cls != C_GLASS && t.wheel < 0 && c.km[t.mat].role != "interior" && c.km[t.mat].role != "hide").ToList();
            foreach (float zs in new[] { 1f, -1f })
            {
                var sb = new System.Text.StringBuilder($"[kandidat] {c.cfg.kind} profile {(zs > 0 ? "front" : "rear")} (y: |z| material):");
                for (float y = .2f; y < 1.3f; y += .03f)
                {
                    if (!Ray(c, solid, new Vector3(0, y, zs * (c.L / 2 + 1)), new Vector3(0, 0, -zs), out var ht, out _)) continue;
                    var a = P(c, ht, 0); var nrm = Vector3.Cross(P(c, ht, 1) - a, P(c, ht, 2) - a);
                    float tz = a.z - (nrm.x * (0 - a.x) + nrm.y * (y - a.y)) / (Mathf.Abs(nrm.z) < 1e-6f ? 1e-6f : nrm.z);
                    sb.Append($" {y:F2}:{Mathf.Abs(tz):F3} {c.g.mats[ht.mat].name.Substring(0, Mathf.Min(10, c.g.mats[ht.mat].name.Length))};");
                }
                Debug.Log(sb.ToString());
            }
        }

        // ------------------------------------------------------------------ lamps
        static void Lamps(Car c, System.Text.StringBuilder log)
        {
            if (c.cfg.lamps == null) return;
            var cnt = new int[8];
            foreach (var lp in c.cfg.lamps)
            {
                int cls = lp.type == "head" ? C_HEAD : lp.type == "tail" ? C_TAIL : lp.type == "indL" ? C_INDL : lp.type == "indR" ? C_INDR : lp.type == "ind" ? -1
                        : lp.type == "beaconA" ? C_BCNA : lp.type == "beaconB" ? C_BCNB : -2;
                bool any = lp.end == "any";
                if (cls == -2) continue;
                float zs = lp.end == "rear" ? -1 : 1, zLim = c.L / 2 - lp.depth * c.L;
                foreach (var t in c.tris)
                {
                    if (t.cls != C_BASE || t.wheel >= 0) continue;
                    if (!string.IsNullOrEmpty(lp.mat) && !c.g.mats[t.mat].name.StartsWith(lp.mat, StringComparison.OrdinalIgnoreCase)) continue;
                    var p = Ctr(c, t); if ((!any && p.z * zs < zLim) || p.y < lp.yMin || p.y > lp.yMax || Mathf.Abs(p.x) < lp.axMin || Mathf.Abs(p.x) > lp.axMax) continue;
                    var pr = c.g.prims[t.prim]; var n = (pr.N[pr.I[t.t]] + pr.N[pr.I[t.t + 1]] + pr.N[pr.I[t.t + 2]]).normalized; if (!any && n.z * zs < lp.facing) continue;
                    var col = TexCol(c, t);
                    bool ok = lp.color == "red" ? IsRed(col) : lp.color == "amber" ? IsAmber(col) : lp.color == "light" ? IsLight(col) : lp.color == "dark" ? Lum(col) < .25f : true;
                    if (!ok) continue;
                    t.cls = cls >= 0 ? cls : p.x < 0 ? C_INDL : C_INDR; cnt[t.cls]++;
                }
            }
            log.Append($"; lamps head {cnt[C_HEAD]} tail {cnt[C_TAIL]} ind {cnt[C_INDL]}/{cnt[C_INDR]} beacons {cnt[C_BCNA]}/{cnt[C_BCNB]} tris");
        }

        // ------------------------------------------------------------------ textures
        class TexSet { public Texture2D alb, mask; public float paintRef; public bool paint; public float hue0; }

        static TexSet[] BakeTextures(Car c, string dir, System.Text.StringBuilder log)
        {
            var sets = new TexSet[c.g.mats.Count];
            for (int m = 0; m < c.g.mats.Count; m++)
            {
                var km = c.km[m]; if (km.role == "hide" || km.role == "glass") continue;
                if (!c.tris.Any(t => t.mat == m)) continue;
                var bc = c.bc[m]; var mr = c.mr[m]; var gm = c.g.mats[m];
                int S = Mathf.Min(km.size, Mathf.Max(bc?.w ?? 64, bc?.h ?? 64), 2048); if (bc == null) S = 16;   // (a narrow strip texture, 8 x 1024 livery bands: its long side)
                var alb = new Color[S * S]; var mask = new Color[S * S];
                bool body = km.role == "body" && !c.cfg.noPaint;
                bool allPaint = body && (c.cfg.paintMode == "all" || bc == null);
                if (body && bc != null) { rectW = bc.w; rectH = bc.h; rectsFor = null; }
                // the paint's hue: peak of the saturated texels' hue histogram (texels the triangles use: the whole atlas is close enough)
                float hue0 = 0, vRef = 1;
                if (body && bc != null && !allPaint)
                {
                    var hist = new float[36];
                    for (int i = 0; i < bc.px.Length; i += 7) { Color.RGBToHSV(bc.px[i], out float h, out float s, out float v); if (s > c.cfg.paintSatMin && v > .25f) hist[Mathf.Min(35, (int)(h * 36))]++; }
                    int pk = Array.IndexOf(hist, hist.Max()); hue0 = (pk + .5f) / 36f;
                    var vs = new List<float>();
                    for (int i = 0; i < bc.px.Length; i += 7)
                    {
                        Color.RGBToHSV(bc.px[i], out float h, out float s, out float v);
                        if (PaintTexel(h, s, v, hue0, c.cfg) && InPaintRect(c.cfg, (i % bc.w + .5f) / bc.w, (i / bc.w + .5f) / bc.h)) vs.Add(v);
                    }
                    vs.Sort(); vRef = vs.Count > 0 ? vs[(int)(vs.Count * .7f)] : 1;
                }
                if (allPaint && bc != null)
                {
                    var vs = new List<float>(); for (int i = 0; i < bc.px.Length; i += 7) { Color.RGBToHSV(bc.px[i], out _, out _, out float v); vs.Add(v); }
                    vs.Sort(); vRef = Mathf.Max(.05f, vs[(int)(vs.Count * .7f)]);
                }
                const float PaintRef = .82f; int nPaint = 0;
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        var uv = new Vector2((x + .5f) / S, (y + .5f) / S);
                        Color col = bc != null ? Box(bc, uv, S) : Color.white; col *= gm.factor;
                        Color mrc = mr != null ? Box(mr, uv, S) : new Color(0, .6f, 0, 1);
                        float smooth = Mathf.Clamp01((1 - mrc.g) * km.smoothMul), metal = Mathf.Clamp01(mrc.b * km.metalMul);
                        if (gm.sg)
                        {
                            // specular / glossiness: smoothness = glossiness (alpha; not when the "specular" map is the diffuse atlas itself),
                            // no metal (a white diffuse / specular atlas would turn the whole car into chrome; chrome parts: KMat.metal)
                            smooth = Mathf.Clamp01((mr != null && gm.mr != gm.bc ? mrc.a : .55f) * gm.gloss * km.smoothMul);
                            metal = 0;
                        }
                        if (km.metal >= 0) metal = km.metal;
                        if (km.smooth >= 0) smooth = km.smooth;
                        Color.RGBToHSV(col, out float h, out float s, out float v);
                        int i = y * S + x;
                        if (body && (allPaint || (PaintTexel(h, s, v, hue0, c.cfg) && InPaintRect(c.cfg, uv.x, uv.y))))
                        {
                            // plain paint: grey shading (its brightness against the paint's), recoloured per instance by _PaintColor
                            float g = (bc == null ? 1 : Mathf.Clamp(v / vRef, .3f, 1.25f)) * PaintRef;
                            alb[i] = new Color(g, g, g, c.cfg.paintSmooth); mask[i] = new Color(1, 0, 0, 0); nPaint++;
                            continue;
                        }
                        if (!string.IsNullOrEmpty(km.alphaFill) && col.a < .98f) { var af = Hex(km.alphaFill); col = new Color(Mathf.Lerp(af.r, col.r, col.a), Mathf.Lerp(af.g, col.g, col.a), Mathf.Lerp(af.b, col.b, col.a), 1); }
                        if (km.desat > 0) { float l = Lum(col); col = Color.Lerp(col, new Color(l, l, l), km.desat); }
                        col *= km.bright;
                        alb[i] = new Color(col.r, col.g, col.b, Mathf.Min(km.smoothMax, smooth));
                        mask[i] = new Color(0, 0, metal, 0);
                    }
                string k = c.cfg.kind, nmS = San(gm.name);
                var set = new TexSet { paintRef = PaintRef, paint = body, hue0 = allPaint ? -1 : hue0 };
                set.alb = Save(alb, S, $"{dir}/{k}_{nmS}_albedo.png", true);
                set.mask = Save(mask, S, $"{dir}/{k}_{nmS}_mask.png", false);
                sets[m] = set;
                log.Append($"; tex {gm.name} {S}px" + (body ? $" paint hue {hue0 * 360:F0}°, {100f * nPaint / (S * S):F0} % paint texels" : ""));
            }
            return sets;
        }

        /// texture uv (bottom-up) inside one of cfg.paintRects (px, top-left origin of the body texture)? no rects: everywhere
        static bool InPaintRect(KCar cfg, float u, float v)
        {
            if (cfg.paintRects == null || cfg.paintRects.Length < 4) return true;
            return PaintRectsUV(cfg).Any(r => u >= r.x && u <= r.z && v >= r.y && v <= r.w);
        }
        static Vector4[] rectsUV; static KCar rectsFor;
        static Vector4[] PaintRectsUV(KCar cfg)
        {
            if (rectsFor == cfg && rectsUV != null) return rectsUV;
            // px rects are given against a nominal 1.0 = cfg.paintRects texture size: the first body texture's (set in BakeTextures)
            var l = new List<Vector4>();
            for (int k = 0; k + 3 < cfg.paintRects.Length; k += 4)
                l.Add(new Vector4(cfg.paintRects[k] / rectW, 1 - cfg.paintRects[k + 3] / rectH, cfg.paintRects[k + 2] / rectW, 1 - cfg.paintRects[k + 1] / rectH));
            rectsFor = cfg; rectsUV = l.ToArray(); return rectsUV;
        }
        static float rectW = 2048, rectH = 2048;

        static bool PaintTexel(float h, float s, float v, float hue0, KCar cfg)
        {
            if (cfg.paintMode == "light") return s < cfg.paintSatMax && v > cfg.paintVMin;
            float dh = Mathf.Abs(h - hue0); dh = Mathf.Min(dh, 1 - dh);
            return s > cfg.paintSatMin && v > .2f && dh * 360 < cfg.paintHueTol;
        }

        static Color Box(Tex t, Vector2 uv, int S)
        {
            int f = Mathf.Max(1, t.w / S);
            int x0 = Mathf.Clamp((int)(uv.x * t.w) - f / 2, 0, t.w - f), y0 = Mathf.Clamp((int)(uv.y * t.h) - f / 2, 0, t.h - f);
            Color sum = Color.clear;
            for (int y = 0; y < f; y++) for (int x = 0; x < f; x++) sum += (Color)t.px[(y0 + y) * t.w + x0 + x];
            return sum / (f * f);
        }

        static string San(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

        static Texture2D Save(Color[] px, int S, string path, bool srgb)
        {
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false, !srgb);
            t.SetPixels(px); t.Apply();
            File.WriteAllBytes(path, t.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.sRGBTexture = srgb; imp.mipmapEnabled = true; imp.alphaIsTransparency = false; imp.anisoLevel = 4;
            imp.maxTextureSize = 2048; imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ------------------------------------------------------------------ meshes
        class MB
        {
            public List<Vector3> V = new List<Vector3>(); public List<Vector3> N = new List<Vector3>(); public List<Vector2> UV = new List<Vector2>(); public List<Color> C = new List<Color>();
            public List<int>[] T;
            public MB(int subs) { T = new List<int>[subs]; for (int i = 0; i < subs; i++) T[i] = new List<int>(); }
            public int Add(Vector3 p, Vector3 n, Vector2 uv) { V.Add(p); N.Add(n); UV.Add(uv); return V.Count - 1; }
            public Mesh ToMesh(string name)
            {
                var m = new Mesh { name = name, indexFormat = V.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                m.SetVertices(V); m.SetNormals(N); m.SetUVs(0, UV); if (C.Count == V.Count) m.SetColors(C);
                m.subMeshCount = T.Length; for (int s = 0; s < T.Length; s++) m.SetTriangles(T[s], s);
                m.RecalculateBounds(); return m;
            }
        }

        static Mesh SaveMesh(Mesh m, string path)
        {
            var old = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (old) { old.Clear(); EditorUtility.CopySerialized(m, old); EditorUtility.SetDirty(old); return old; }
            AssetDatabase.CreateAsset(m, path); return m;
        }

        static int Tris(Mesh m) { int n = 0; if (!m) return 0; for (int s = 0; s < m.subMeshCount; s++) n += (int)m.GetIndexCount(s) / 3; return n; }

        /// one submesh group's triangles as a single-submesh mesh (vertices welded per source primitive), offset subtracted
        static Mesh GroupMesh(Car c, List<Tri> ts, Vector3 off)
        {
            var mb = new MB(1); var map = new Dictionary<(int, int), int>();
            foreach (var t in ts)
            {
                var p = c.g.prims[t.prim];
                for (int q = 0; q < 3; q++)
                {
                    int vi = p.I[t.t + q];
                    if (!map.TryGetValue((t.prim, vi), out int j)) { j = mb.Add(p.P[vi] - off, p.N[vi], p.UV[vi]); map[(t.prim, vi)] = j; }
                    mb.T[0].Add(j);
                }
            }
            return mb.ToMesh("g");
        }

        /// UV-preserving decimation; when the UV seams stop it short of the target (fragmented atlases), again without them
        /// (the UVs then stretch a little across the seams — only used for LOD1 / LOD2 / far)
        static Mesh Simplify(Mesh src, float quality, bool seams = true, bool careful = false)
        {
            if (quality >= .999f) return src;
            int target = Mathf.Max(4, Mathf.RoundToInt(Tris(src) * quality));
            Mesh Run(Mesh m, float q, bool keepSeams, double link)
            {
                var ms = new UnityMeshSimplifier.MeshSimplifier();
                var o = UnityMeshSimplifier.SimplificationOptions.Default;
                o.PreserveUVSeamEdges = keepSeams; o.PreserveBorderEdges = careful; o.EnableSmartLink = true; o.VertexLinkDistance = link;
                o.PreserveSurfaceCurvature = false; o.MaxIterationCount = 300; o.Agressiveness = 7;
                ms.SimplificationOptions = o; ms.Initialize(m); ms.SimplifyMesh(q);
                return ms.ToMesh();
            }
            var r = Run(src, quality, seams, .0005);
            if (careful) return r;   // interior: open shells (seats, dashboard) collapse into shards without their borders
            // fragmented models stop short of the target: again without seams, linking ever farther vertices
            foreach (double link in new[] { .002, .01, .025 })
                if (Tris(r) > target * 1.2f) r = Run(r, Mathf.Clamp01(target / (float)Mathf.Max(1, Tris(r))), false, link);
            return r;
        }

        /// meshes into one mesh with one submesh each
        static Mesh Combine(List<Mesh> parts, string name)
        {
            var mb = new MB(parts.Count);
            for (int s = 0; s < parts.Count; s++)
            {
                var m = parts[s]; if (!m) continue;
                int b = mb.V.Count; var v = m.vertices; var n = m.normals; var uv = m.uv;
                for (int i = 0; i < v.Length; i++) mb.Add(v[i], n != null && n.Length == v.Length ? n[i] : Vector3.up, uv != null && uv.Length == v.Length ? uv[i] : Vector2.zero);
                for (int sm = 0; sm < m.subMeshCount; sm++) foreach (var i in m.GetTriangles(sm)) mb.T[s].Add(b + i);
            }
            return mb.ToMesh(name);
        }

        static Mesh PlateQuads(Car c, List<Tri> solid)
        {
            var mb = new MB(1);
            void Quad(float zs, float y)
            {
                // rays from outside along -zs*z over the plate's rect onto the body: the plate sits on the outermost hit (a slanted
                // panel would swallow half of it otherwise)
                float outer = float.NegativeInfinity;
                for (int q = 0; q < 9; q++)
                {
                    var o = new Vector3((q % 3 - 1) * c.cfg.plateW * .45f, y + (q / 3 - 1) * c.cfg.plateH * .45f, zs * (c.L / 2 + 1));
                    if (Ray(c, solid, o, new Vector3(0, 0, -zs), out var ht, out _))
                    {
                        // the hit point: back along the ray by the distance (recomputed from the triangle plane)
                        var a = P(c, ht, 0); var nrm = Vector3.Cross(P(c, ht, 1) - a, P(c, ht, 2) - a);
                        float tz = a.z - (nrm.x * (o.x - a.x) + nrm.y * (o.y - a.y)) / (Mathf.Abs(nrm.z) < 1e-6f ? 1e-6f : nrm.z);
                        outer = Mathf.Max(outer, tz * zs);
                    }
                }
                if (float.IsNegativeInfinity(outer)) { Debug.LogWarning($"[kandidat] {c.cfg.kind}: no body behind the plate at y {y}"); return; }
                float z = zs * (outer + .006f), w = c.cfg.plateW / 2, h = c.cfg.plateH / 2;
                var n = new Vector3(0, 0, zs);
                // u runs left -> right as seen from outside: from the front the viewer's left is +x
                int i0 = mb.Add(new Vector3(zs * w, y - h, z), n, new Vector2(2, 0)), i1 = mb.Add(new Vector3(-zs * w, y - h, z), n, new Vector2(2.999f, 0));
                int i2 = mb.Add(new Vector3(-zs * w, y + h, z), n, new Vector2(2.999f, .999f)), i3 = mb.Add(new Vector3(zs * w, y + h, z), n, new Vector2(2, .999f));
                // facing +zs (clockwise seen from outside = Unity front face)
                var fn = Vector3.Cross(mb.V[i1] - mb.V[i0], mb.V[i2] - mb.V[i0]);
                // Unity: a triangle faces cross(b - a, c - a)
                if (Vector3.Dot(fn, n) > 0) mb.T[0].AddRange(new[] { i0, i1, i2, i0, i2, i3 }); else mb.T[0].AddRange(new[] { i0, i2, i1, i0, i3, i2 });
            }
            if (c.cfg.plateFront) Quad(1, c.cfg.plateFrontY);
            if (c.cfg.plateRear) Quad(-1, c.cfg.plateRearY);
            return mb.ToMesh("plates");
        }

        class Group { public int mat, cls; public List<Tri> t = new List<Tri>(); public string role; }

        static void BuildMeshesAndMaterials(Car c, string dir, TexSet[] sets, Info info, System.Text.StringBuilder log)
        {
            string k = c.cfg.kind; var cfg = c.cfg;
            bool authored = !string.IsNullOrEmpty(cfg.lod1Glb) || !string.IsNullOrEmpty(cfg.lod2Glb);
            if (authored && (string.IsNullOrEmpty(cfg.lod1Glb) || string.IsNullOrEmpty(cfg.lod2Glb)))
                throw new Exception("set both lod1Glb and lod2Glb");
            var levels = authored ? new[] { c, AuthoredLod(c, cfg.lod1Glb, log), AuthoredLod(c, cfg.lod2Glb, log) } : null;
            var groups = c.tris.Where(t => t.wheel < 0 && c.km[t.mat].role != "hide").GroupBy(t => (t.cls == C_GLASS ? -1 : t.mat, t.cls))
                .Select(g => new Group { mat = g.Key.Item1, cls = g.Key.cls, t = g.ToList(), role = g.Key.Item1 < 0 ? "glass" : c.km[g.Key.Item1].role })
                .OrderBy(g => g.role == "body" && g.cls == C_BASE ? 0 : g.role == "glass" ? 3 : g.role == "interior" ? 2 : 1).ThenBy(g => g.mat).ThenBy(g => g.cls).ToList();
            if (groups.Count == 0 || !(groups[0].role == "body" && groups[0].cls == C_BASE)) throw new Exception("no body (paint) material — set role \"body\" in the config");
            // several body materials: they share the first one's paint set, so they must use the same texture (one atlas)
            foreach (var g in groups.Where(g => g.role == "body" && g.cls == C_BASE && g.mat != groups[0].mat))
                if (c.g.mats[g.mat].bc != c.g.mats[groups[0].mat].bc) Debug.LogWarning($"[kandidat] {k}: body material {c.g.mats[g.mat].name} has another texture than {c.g.mats[groups[0].mat].name} — its paint uses the wrong UVs");
            var solid = c.tris.Where(t => t.cls != C_GLASS && t.wheel < 0 && c.km[t.mat].role != "interior" && c.km[t.mat].role != "hide").ToList();
            var plates = PlateQuads(c, solid);

            // materials
            var sh = Shader.Find("Magistrala/TripoCar");
            var grime = AssetDatabase.LoadAssetAtPath<Texture2D>(GRIME); var plateTex = AssetDatabase.LoadAssetAtPath<Texture2D>(PLATES);
            Material Mat(string name, Shader s)
            {
                string path = $"{dir}/{k}_{name}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) { m = new Material(s); AssetDatabase.CreateAsset(m, path); } else m.shader = s;
                m.enableInstancing = true; m.name = $"{k}_{name}"; EditorUtility.SetDirty(m); return m;
            }
            void Setup(Material m, TexSet ts, Color paint)
            {
                m.SetTexture("_BaseMap", ts.alb); m.SetTexture("_MaskMap", ts.mask); m.SetFloat("_PaintRef", ts.paintRef);
                m.SetColor("_PaintColor", paint); m.SetColor("_LampColor", Color.black); m.SetFloat("_LampAll", 0); m.SetFloat("_LampMetal", 0); m.SetFloat("_Livery", 0);
                m.SetFloat("_PaintSmooth", cfg.paintSmooth); m.SetFloat("_CoatSmooth", cfg.coatSmooth); m.SetFloat("_Flake", cfg.flake);
                m.SetTexture("_GrimeTex", grime); m.SetFloat("_Grime", grime ? cfg.grime : 0); m.SetFloat("_GrimeH", cfg.grimeH);
                m.SetVector("_WheelZ", new Vector4(info.wheelZf, info.wheelZr, info.wheelR, 0));
                m.SetTexture("_PlateTex", plateTex);
            }
            var colors = cfg.colors != null && cfg.colors.Length > 0 ? cfg.colors : new[] { "#e6e2d6" };
            var bodySet = sets[groups[0].mat];
            for (int i = 0; i < colors.Length; i++)
            {
                var m = Mat("paint_" + colors[i].TrimStart('#'), sh); Setup(m, bodySet, Hex(colors[i]));
                // a different fictional plate per colour (atlas rows 0..7), cfg.plateStyle = column
                int row = i % 8;
                m.SetVector("_PlateRect", new Vector4(cfg.plateStyle * .5f + 6 / 1024f, 1 - (row * 128 + 10 + 108) / 1024f, 500 / 1024f, 108 / 1024f));
            }
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var glassT = Mat("glass", lit);
            {
                // tinted, slightly transparent (LOD0: the interior shows through), premultiplied so the reflection stays
                glassT.SetFloat("_Surface", 1); glassT.SetFloat("_Blend", 0); glassT.SetFloat("_BlendModePreserveSpecular", 1);
                glassT.SetFloat("_SrcBlend", (float)BlendMode.One); glassT.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                glassT.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); glassT.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                glassT.SetFloat("_ZWrite", 0); glassT.SetFloat("_Cull", 2); glassT.SetFloat("_ReceiveShadows", 1);
                glassT.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); glassT.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                glassT.SetOverrideTag("RenderType", "Transparent"); glassT.renderQueue = (int)RenderQueue.Transparent;
                glassT.SetShaderPassEnabled("ShadowCaster", false); glassT.SetShaderPassEnabled("DepthOnly", false);
                glassT.SetColor("_BaseColor", new Color(.05f, .06f, .065f, cfg.glassAlpha)); glassT.SetFloat("_Smoothness", .96f); glassT.SetFloat("_Metallic", 0);
            }
            var glassO = Mat("glass_opaque", lit);
            glassO.SetColor("_BaseColor", new Color(.035f, .04f, .045f, 1)); glassO.SetFloat("_Smoothness", .93f); glassO.SetFloat("_Metallic", 0);

            // per group: material (off / on)
            var subMat = new List<Material>(); var subOn = new List<Material>(); var subLamp = new List<int>(); var lod0Only = new List<bool>(); var lodMat = new List<Material>();
            foreach (var g in groups)
            {
                Material m, on = null; int lamp = 0;
                if (g.role == "glass") { m = cfg.glassOpaque ? glassO : glassT; lodMat.Add(authored ? m : glassO); }
                else if (g.role == "display")
                {
                    // destination display (TrafficPart lamp 7): URP Unlit with its own texture; "_on" = the other direction's
                    var km = c.km[g.mat]; string mn = San(c.g.mats[g.mat].name);
                    Texture2D T(string file, string nm)
                    {
                        byte[] bytes = !string.IsNullOrEmpty(file) && File.Exists(file) ? File.ReadAllBytes(file) : c.g.mats[g.mat].bc >= 0 ? c.g.images[c.g.mats[g.mat].bc] : null;
                        if (bytes == null) return null;
                        string tp = $"{dir}/{k}_{nm}.png"; var t2 = new Texture2D(2, 2); t2.LoadImage(bytes); File.WriteAllBytes(tp, t2.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t2);
                        AssetDatabase.ImportAsset(tp); var ti = (TextureImporter)AssetImporter.GetAtPath(tp);
                        ti.sRGBTexture = true; ti.mipmapEnabled = true; ti.maxTextureSize = 1024; ti.anisoLevel = 4; ti.SaveAndReimport();
                        return AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
                    }
                    Material D(string nm, Texture2D t)
                    {
                        // URP Unlit, the LED texture × an HDR white (Lit's _EMISSION keyword did not stay on the saved asset: dark sign);
                        // it sits behind the tinted windscreen, so a bit over 1
                        var dm = Mat(nm, Shader.Find("Universal Render Pipeline/Unlit")); dm.SetTexture("_BaseMap", t); dm.SetColor("_BaseColor", Color.white * km.emit);
                        EditorUtility.SetDirty(dm); return dm;
                    }
                    var t0 = T(km.tex, mn + "_display"); var t1 = T(string.IsNullOrEmpty(km.texAlt) ? km.tex : km.texAlt, mn + "_display_alt");
                    m = D(mn + "_display", t0); on = D(mn + "_display_on", t1); lamp = 7; lodMat.Add(null);
                }
                else
                {
                    var ts = sets[g.mat]; string mn = San(c.g.mats[g.mat].name);
                    if (g.role == "body" && g.cls == C_BASE) m = null;   // paint set
                    else
                    {
                        string suf = g.cls == C_HEAD ? "_head" : g.cls == C_TAIL ? "_tail" : g.cls == C_INDL || g.cls == C_INDR ? "_ind" : g.cls == C_BCNA ? "_bcnA" : g.cls == C_BCNB ? "_bcnB" : "";
                        m = Mat(mn + suf, sh); Setup(m, ts, Hex(colors[0]));
                        if (g.cls != C_BASE)
                        {
                            lamp = g.cls == C_HEAD ? 4 : g.cls == C_TAIL ? 1 : g.cls == C_INDL ? 2 : g.cls == C_INDR ? 3 : g.cls == C_BCNA ? 5 : 6;
                            m.SetFloat("_LampAll", 1); m.SetColor("_LampColor", g.cls == C_TAIL ? new Color(.22f, .01f, .005f) : Color.black);
                            m.SetFloat("_LampMetal", g.cls == C_HEAD ? cfg.headMetal : .05f);
                            on = Mat(mn + suf + "_on", sh); Setup(on, ts, Hex(colors[0])); on.SetFloat("_LampAll", 1); on.SetFloat("_LampMetal", m.GetFloat("_LampMetal"));
                            on.SetColor("_LampColor", g.cls == C_HEAD ? new Color(4f, 3.9f, 3.6f) : g.cls == C_TAIL ? new Color(7f, .3f, .15f)
                                : g.cls == C_BCNA || g.cls == C_BCNB ? new Color(.35f, 1.1f, 9f) : new Color(6f, 2.6f, .3f));
                        }
                    }
                    lodMat.Add(null);
                }
                subMat.Add(m); subOn.Add(on); subLamp.Add(lamp); lod0Only.Add(!authored && g.role == "interior");
            }

            // LOD meshes: per group a mesh, simplified per level
            var gm0 = groups.Select(g => GroupMesh(c, g.t, Vector3.zero)).ToList();
            gm0[0] = CombineSame(gm0[0], plates);
            int nonInt = groups.Select((g, i) => g.role == "interior" ? 0 : Tris(gm0[i])).Sum();
            // LOD0 cap (dense models): body share of cfg.lod0 after the wheels' share (4 × 4 %)
            int wheelAll = c.tris.Count(t => t.wheel >= 0);
            // the paint panels are what one sees close up: kept whole, the rest takes the cut
            int paintTris = groups.Select((g, i) => g.role == "body" && g.cls == C_BASE ? Tris(gm0[i]) : 0).Sum();
            bool keepPaint = paintTris < cfg.lod0 * .45f;   // a dense paint mesh (Yugo: 21k) is cut like the rest
            float q0 = cfg.lod0 <= 0 ? 1 : keepPaint ? Mathf.Clamp((cfg.lod0 - Mathf.Min(wheelAll, cfg.lod0 * .16f) - paintTris) / (float)Mathf.Max(1, nonInt - paintTris), .2f, 1)
                : Mathf.Clamp01((cfg.lod0 - Mathf.Min(wheelAll, cfg.lod0 * .16f)) / (float)Mathf.Max(1, nonInt));
            Mesh Level(int lod, int target)
            {
                if (authored)
                {
                    var lc = levels[lod];
                    var body = lc.tris.Where(t => t.wheel < 0 && lc.km[t.mat].role != "hide").ToList();
                    int GroupIndex(Tri t) => groups.FindIndex(g => g.mat == (t.cls == C_GLASS ? -1 : t.mat) && g.cls == t.cls);
                    if (body.Any(t => GroupIndex(t) < 0)) throw new Exception($"{k} LOD{lod}: unmatched material/lamp group");
                    return Combine(groups.Select((g, i) => GroupMesh(lc, body.Where(t => GroupIndex(t) == i).ToList(), Vector3.zero)).ToList(), $"{k}_lod{lod}");
                }
                float q = Mathf.Clamp01(target / (float)Mathf.Max(1, nonInt));
                var ms = new List<Mesh>();
                for (int i = 0; i < groups.Count; i++)
                {
                    var g = groups[i]; int n = Tris(gm0[i]);
                    if (g.role == "interior") { ms.Add(lod == 0 ? Simplify(gm0[i], cfg.interiorLod0, true, true) : null); continue; }
                    if ((lod == 0 && q0 >= .999f) || n < 120 || (g.cls != C_BASE && lod <= 1)) { ms.Add(gm0[i]); continue; }   // lamps / small pieces kept whole (LOD2: lamps simplified too)
                    if (lod == 0) { ms.Add(keepPaint && g.role == "body" && g.cls == C_BASE ? gm0[i] : Simplify(gm0[i], Mathf.Max(q0, 60f / n), true, true)); continue; }
                    ms.Add(Simplify(gm0[i], Mathf.Max(q, 60f / n)));
                }
                // plates survive the simplifier? keep them whole: re-add to the paint submesh
                if (lod > 0 || q0 < .999f) ms[0] = CombineSame(Strip2(ms[0]), plates);
                return Combine(ms.Select(m => m ?? new Mesh()).ToList(), $"{k}_lod{lod}");
            }
            var b0 = Level(0, int.MaxValue);
            var b1 = Level(1, Mathf.RoundToInt(cfg.lod1 * .82f));
            var b2 = Level(2, Mathf.RoundToInt(cfg.lod2 * .82f));
            SaveMesh(b0, $"{dir}/{k}_lod0.asset"); SaveMesh(b1, $"{dir}/{k}_lod1.asset"); SaveMesh(b2, $"{dir}/{k}_lod2.asset");

            // wheels
            var wheelMat = new Dictionary<int, Material>();
            int wTris0 = 0, wTris1 = 0, wTris2 = 0;
            var wheelSubs = new List<string>();
            for (int w = 0; w < 4; w++)
            {
                var wt = c.tris.Where(t => t.wheel == w).GroupBy(t => t.mat).OrderBy(g => g.Key).ToList();
                var parts = wt.Select(g => GroupMesh(c, g.ToList(), c.wc[w])).ToList();
                int n = parts.Sum(Tris);
                for (int lod = 0; lod < 3; lod++)
                {
                    float q = lod == 0 ? (cfg.lod0 > 0 ? Mathf.Clamp01(cfg.lod0 * .04f / Mathf.Max(1, n)) : 1) : lod == 1 ? Mathf.Clamp01(cfg.lod1 * .045f / Mathf.Max(1, n)) : Mathf.Clamp01(cfg.lod2 * .04f / Mathf.Max(1, n));
                    Mesh m;
                    if (authored)
                    {
                        var lc = levels[lod];
                        var keys = wt.Select(g => g.Key).ToArray();
                        if (lc.tris.Any(t => t.wheel == w && !keys.Contains(t.mat))) throw new Exception($"{k} LOD{lod}: unmatched wheel material");
                        m = Combine(keys.Select(mat => GroupMesh(lc, lc.tris.Where(t => t.wheel == w && t.mat == mat).ToList(), c.wc[w])).ToList(), $"{k}_wheel{w}_lod{lod}");
                    }
                    else m = Combine(parts.Select(p => Simplify(p, q)).ToList(), $"{k}_wheel{w}_lod{lod}");
                    SaveMesh(m, $"{dir}/{k}_wheel{w}_lod{lod}.asset");
                    if (lod == 0) wTris0 += Tris(m); else if (lod == 1) wTris1 += Tris(m); else wTris2 += Tris(m);
                }
                if (w == 0) wheelSubs = wt.Select(g => g.Key.ToString()).ToList();
                foreach (var g in wt) if (!wheelMat.ContainsKey(g.Key)) { var m = Mat(San(c.g.mats[g.Key].name) + "_wheel", sh); Setup(m, sets[g.Key], Hex(colors[0])); m.SetFloat("_Grime", 0); wheelMat[g.Key] = m; }
            }
            info.tris0 = Tris(b0) + wTris0; info.tris1 = Tris(b1) - SubTris(b1, lod0Only) + wTris1; info.tris2 = Tris(b2) - SubTris(b2, lod0Only) + wTris2;

            // far mesh: lod2 decimated further, per-triangle colours (submesh 0 palette, 1 paint, 2 tail)
            var far = FarMesh(c, groups, gm0, sets, cfg.far, k);
            SaveMesh(far, $"{dir}/{k}_far.asset"); info.trisFar = Tris(far);

            info.subMat = subMat.Select(m => m ? m.name : "paint").ToArray();
            info.subLamp = subLamp.ToArray(); info.subLod0Only = lod0Only.ToArray();
            info.subLodMat = lodMat.Select(m => m ? m.name : "").ToArray();
            // wheel materials: stored as "<wheel submesh index>=<material>" in the lamp-free list (one wheel mesh has one submesh per material)
            var wl = new List<string>(); for (int i = 0; i < wheelSubs.Count; i++) wl.Add(wheelMat[int.Parse(wheelSubs[i])].name);
            info.subMat = info.subMat.Concat(new[] { "|wheels|" }).Concat(wl).ToArray();
            foreach (var mo in subOn.Where(x => x)) EditorUtility.SetDirty(mo);

            log.Append($"; {groups.Count} body submeshes ({string.Join(", ", groups.Select(g => $"{(g.mat >= 0 ? c.g.mats[g.mat].name : "glass")}/{g.cls}:{g.t.Count}"))})");
        }

        static int SubTris(Mesh m, List<bool> which) { int n = 0; for (int s = 0; s < which.Count && s < m.subMeshCount; s++) if (which[s]) n += (int)m.GetIndexCount(s) / 3; return n; }

        /// a + b in one submesh
        static Mesh CombineSame(Mesh a, Mesh b)
        {
            var m = Combine(new List<Mesh> { a, b }, a.name);
            var tri = m.GetTriangles(0).Concat(m.GetTriangles(1)).ToArray(); m.subMeshCount = 1; m.SetTriangles(tri, 0); return m;
        }
        /// drop the plate quads (uv.x > 1.5) from a simplified paint mesh
        static Mesh Strip2(Mesh m)
        {
            m = UnityEngine.Object.Instantiate(m);
            var uv = m.uv; var t = m.GetTriangles(0).ToList(); var keep = new List<int>();
            for (int i = 0; i < t.Count; i += 3) if (uv[t[i]].x < 1.5f && uv[t[i + 1]].x < 1.5f && uv[t[i + 2]].x < 1.5f) keep.AddRange(new[] { t[i], t[i + 1], t[i + 2] });
            m.SetTriangles(keep, 0); return m;
        }

        /// far mesh: everything but the interior in one mesh, 3 submeshes (0 palette = vertex colour sampled from the textures, 1 paint,
        /// 2 tail lamps), welded and decimated as a whole (pieces merge) to ~target triangles; then one colour per triangle
        static Mesh FarMesh(Car c, List<Group> groups, List<Mesh> gm0, TexSet[] sets, int target, string k)
        {
            var mb = new MB(3);
            void AddMesh(Mesh m, Group g, int mat)
            {
                var v = m.vertices; var nn = m.normals; var uv = m.uv; var t = m.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    var cuv = (uv[t[i]] + uv[t[i + 1]] + uv[t[i + 2]]) / 3;
                    int sub = 0; Color col;
                    if (g != null && g.role == "glass") col = new Color(.06f, .07f, .08f);
                    else if (cuv.x > 1.5f) col = new Color(.9f, .9f, .88f);
                    else
                    {
                        col = (c.bc[mat] != null ? c.bc[mat].At(cuv) : Color.white) * c.g.mats[mat].factor;
                        if (g != null && g.cls == C_TAIL) sub = 2;
                        else if (g != null && g.role == "body" && g.cls == C_BASE && sets[mat] != null && sets[mat].paint)
                        {
                            Color.RGBToHSV(col, out float h, out float s, out float vv);
                            if (sets[mat].hue0 < 0 || (PaintTexel(h, s, vv, sets[mat].hue0, c.cfg) && InPaintRect(c.cfg, cuv.x - Mathf.Floor(cuv.x), cuv.y - Mathf.Floor(cuv.y)))) sub = 1;
                        }
                    }
                    for (int q = 0; q < 3; q++) { int j = mb.Add(v[t[i + q]], nn != null && nn.Length == v.Length ? nn[t[i + q]] : Vector3.up, Vector2.zero); mb.C.Add(col); mb.T[sub].Add(j); }
                }
            }
            for (int i = 0; i < groups.Count; i++) if (groups[i].role != "interior") AddMesh(gm0[i], groups[i], groups[i].mat);
            for (int w = 0; w < 4; w++) foreach (var g in c.tris.Where(t => t.wheel == w).GroupBy(t => t.mat)) AddMesh(GroupMesh(c, g.ToList(), Vector3.zero), null, g.Key);
            // vertex clustering (UnityMeshSimplifier stalls on ~2000 loose pieces): grid cells grow until the target is met;
            // a triangle survives when its corners fall in 3 different cells; flat normals, one (quantised) colour per triangle
            MB far = null;
            foreach (float cell in new[] { .05f, .065f, .08f, .1f, .125f, .155f, .19f, .24f })
            {
                far = new MB(3);
                var cid = new Dictionary<Vector3Int, int>(); var sum = new List<Vector3>(); var cnt = new List<int>();
                int Id(Vector3 p) { var k3 = Vector3Int.FloorToInt(p / cell); if (!cid.TryGetValue(k3, out int i)) { i = sum.Count; cid[k3] = i; sum.Add(Vector3.zero); cnt.Add(0); } sum[i] += p; cnt[i]++; return i; }
                var tris = new List<(int a, int b, int c, int sub, Color col)>(); var seen = new HashSet<(int, int, int)>();
                for (int s2 = 0; s2 < 3; s2++)
                    for (int i = 0; i < mb.T[s2].Count; i += 3)
                    {
                        int ia = mb.T[s2][i], ib = mb.T[s2][i + 1], ic = mb.T[s2][i + 2];
                        int a = Id(mb.V[ia]), b = Id(mb.V[ib]), cc = Id(mb.V[ic]);
                        if (a == b || b == cc || a == cc) continue;
                        var key = (Mathf.Min(a, Mathf.Min(b, cc)), Mathf.Max(a, Mathf.Max(b, cc)), a + b + cc);
                        if (!seen.Add(key)) continue;
                        tris.Add((a, b, cc, s2, (mb.C[ia] + mb.C[ib] + mb.C[ic]) / 3));
                    }
                if (tris.Count > target * 1.25f && cell < .24f) continue;
                foreach (var (a, b, cc, sub, col) in tris)
                {
                    Vector3 pa = sum[a] / cnt[a], pb = sum[b] / cnt[b], pc = sum[cc] / cnt[cc];
                    var n = Vector3.Cross(pb - pa, pc - pa); if (n.sqrMagnitude < 1e-10f) continue; n.Normalize();
                    var q = new Color(Mathf.Round(col.r * 10) / 10, Mathf.Round(col.g * 10) / 10, Mathf.Round(col.b * 10) / 10, 1);
                    far.T[sub].Add(far.Add(pa, n, Vector2.zero)); far.T[sub].Add(far.Add(pb, n, Vector2.zero)); far.T[sub].Add(far.Add(pc, n, Vector2.zero));
                    far.C.Add(q); far.C.Add(q); far.C.Add(q);
                }
                break;
            }
            return far.ToMesh(k + "_far");
        }

        static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        // ------------------------------------------------------------------ traffic kinds (TrafficBuilder)
        public static List<TrafficKind> TrafficKinds(Func<Color, Vector2> farColor, Func<string, Color, float, Color?, Material> urp)
        {
            var list = new List<TrafficKind>();
            foreach (var cfg in Configs())
            {
                var kd = Kind(cfg, farColor, urp);
                if (kd != null) list.Add(kd); else Debug.LogWarning($"[kandidat] {cfg.kind}: not imported (Magistrala/Auti: uvezi kandidata)");
            }
            return list;
        }

        public static TrafficKind Kind(KCar cfg, Func<Color, Vector2> farColor = null, Func<string, Color, float, Color?, Material> urp = null)
        {
            string dir = $"{OUT}/{cfg.kind}", k = cfg.kind;
            var infoAsset = AssetDatabase.LoadAssetAtPath<TextAsset>($"{dir}/{k}_info.json");
            if (!infoAsset) return null;
            var info = JsonUtility.FromJson<Info>(infoAsset.text);
            Mesh M(string n) => AssetDatabase.LoadAssetAtPath<Mesh>($"{dir}/{k}_{n}.asset");
            Material Mt(string n) => AssetDatabase.LoadAssetAtPath<Material>($"{dir}/{n}.mat");
            int sep = Array.IndexOf(info.subMat, "|wheels|");
            var bodyMats = info.subMat.Take(sep).ToArray(); var wheelMats = info.subMat.Skip(sep + 1).ToArray();
            TrafficPart[] Parts(int lod)
            {
                var body = M("lod" + lod); var ps = new List<TrafficPart>();
                for (int s = 0; s < bodyMats.Length; s++)
                {
                    if (lod > 0 && info.subLod0Only[s]) continue;
                    if (bodyMats[s] == "paint") { ps.Add(new TrafficPart { mesh = body, sub = s, paint = true, local = Matrix4x4.identity }); continue; }
                    var mat = lod > 0 && !string.IsNullOrEmpty(info.subLodMat[s]) ? Mt(info.subLodMat[s]) : Mt(bodyMats[s]);
                    int lamp = info.subLamp[s];
                    ps.Add(new TrafficPart { mesh = body, sub = s, mat = mat, lamp = lamp, matOn = lamp > 0 ? Mt(bodyMats[s] + "_on") : null, local = Matrix4x4.identity });
                }
                for (int w = 0; w < 4; w++)
                {
                    var wm = M($"wheel{w}_lod{lod}");
                    for (int s = 0; s < wheelMats.Length && s < wm.subMeshCount; s++)
                        ps.Add(new TrafficPart { mesh = wm, sub = s, mat = Mt(wheelMats[s]), local = Matrix4x4.Translate(info.wheels[w]), wheel = true, radius = info.wheelR });
                }
                return ps.ToArray();
            }
            var colors = cfg.colors != null && cfg.colors.Length > 0 ? cfg.colors : new[] { "#e6e2d6" };
            var kd = new TrafficKind
            {
                name = k, len = info.len, halfW = info.halfW, h = info.h, vMul = cfg.vMul, a = cfg.a, b = cfg.b, T = cfg.T, weight = cfg.weight,
                lod0 = Parts(0), lod1 = Parts(1), lod2 = Parts(2), lodNear = cfg.lodNear, lodMid = cfg.lodMid, beaconShare = cfg.beaconShare,
                paints = colors.Select(cc => Mt($"{k}_paint_{cc.TrimStart('#')}")).ToArray(),
                paintW = cfg.colorWeights != null && cfg.colorWeights.Length == colors.Length ? cfg.colorWeights.ToArray() : colors.Select(_ => 1f).ToArray(),
            };
            var far = M("far");
            if (far && farColor != null && urp != null)
            {
                var cols = far.colors; var uv = new Vector2[cols.Length];
                for (int i = 0; i < cols.Length; i++) uv[i] = farColor(cols[i]);
                far.uv = uv; EditorUtility.SetDirty(far);
                kd.farMesh = far;
                kd.farPaints = colors.Select(cc => urp($"far_paint_{cc.TrimStart('#')}", Hex(cc), .62f, null)).ToArray();
                kd.farTail = urp("far_tail", new Color(.55f, .08f, .07f), .5f, null);
                kd.farTailOn = urp("far_tail_on", new Color(.8f, .1f, .08f), .5f, new Color(6f, .25f, .15f));
            }
            return kd;
        }

        // ------------------------------------------------------------------ shots (AutiParking scene): before / after, identical views
        static readonly string[] Views = { "34-sprijeda", "bok", "34-straga", "sprijeda-detalj", "straga-detalj" };

        /// car frame (ground centre, forward, right) -> camera pose for view v; frame from the parked model's bounds
        static (Vector3 eye, Vector3 look) Pose(string v, Vector3 g, Vector3 f, Vector3 r, float L)
        {
            var u = Vector3.up; var ctr = g + u * .7f;
            switch (v)
            {
                case "34-sprijeda": return (ctr + f * 4.4f + r * 3.2f + u * .6f, ctr);
                case "bok": return (ctr - r * 6.2f + u * .25f, ctr);
                case "34-straga": return (ctr - f * 4.4f + r * 3.2f + u * .8f, ctr);
                case "sprijeda-detalj": { var p = g + f * (L / 2) + u * .62f; return (p + f * 1.55f + r * .55f + u * .2f, p - f * .05f); }
                default: { var p = g + f * (-L / 2) + u * .72f; return (p - f * 1.6f + r * .55f + u * .25f, p + f * .05f); }
            }
        }

        static Transform ParkedCandidate(string label)
        {
            var root = GameObject.Find("Auti"); if (!root) return null;
            foreach (Transform t in root.transform) if (t.name.EndsWith(label)) return t;
            return null;
        }

        static Bounds RB(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy && !(r is MeshRenderer mr && mr.GetComponent<TextMesh>())).ToArray();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }

        /// batch: several candidates in one editor run: import + before / after shots each
        /// -executeMethod Magistrala.EditorTools.KandidatImporter.BatchMany -kandidati bmw,w124 -shotroot <dir> [-faze prije,poslije] [-noimport]
        public static void BatchMany()
        {
            string list = "", root = "Builds/kandidati/slike", faze = "prije,poslije"; bool noImport = false;
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length; i++)
            {
                if (i + 1 < a.Length && a[i] == "-kandidati") list = a[i + 1];
                if (i + 1 < a.Length && a[i] == "-shotroot") root = a[i + 1];
                if (i + 1 < a.Length && a[i] == "-faze") faze = a[i + 1];
                if (a[i] == "-noimport") noImport = true;
            }
            if (Array.IndexOf(a, "-parkscene") >= 0 || !File.Exists("Assets/Scenes/AutiParking.unity")) AutiParking.BuildScene();
            foreach (var k in list.Split(',').Where(x => x.Length > 0))
            {
                var cfg = Configs().FirstOrDefault(c => c.kind == k); if (cfg == null) { Debug.LogError("[kandidat] no config " + k); continue; }
                try
                {
                    if (!noImport) { Debug.Log(Import(cfg)); AssetDatabase.SaveAssets(); }
                    foreach (var f in faze.Split(',').Where(x => x.Length > 0)) Shots(cfg, $"{root}/{k}", f, 0);
                }
                catch (Exception e) { Debug.LogError($"[kandidat] {k} FAILED: {e}"); }
            }

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Magistrala.unity"); // never leave the lot as the last scene (main 675c6be)
        }

        /// batch: -executeMethod Magistrala.EditorTools.KandidatImporter.BatchShots -kandidat zastava -shotdir <dir> -faza prije|poslije [-paint i]
        public static void BatchShots()
        {
            string kind = null, outDir = "Builds/kandidati/slike", faza = "prije"; int paint = 0;
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) { if (a[i] == "-kandidat") kind = a[i + 1]; if (a[i] == "-shotdir") outDir = a[i + 1]; if (a[i] == "-faza") faza = a[i + 1]; if (a[i] == "-paint") paint = int.Parse(a[i + 1]); }
            var cfg = Configs().First(c => c.kind == kind);
            Shots(cfg, outDir, faza, paint);

            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Magistrala.unity"); // never leave the lot as the last scene (main 675c6be)
        }

        public static void Shots(KCar cfg, string outDir, string faza, int paint)
        {
            if (!File.Exists("Assets/Scenes/AutiParking.unity")) AutiParking.BuildScene();
            EditorSceneManager.OpenScene("Assets/Scenes/AutiParking.unity");
            string pl = string.IsNullOrEmpty(cfg.parkLabel) ? cfg.label : cfg.parkLabel;
            var holder = ParkedCandidate(pl); if (!holder) throw new Exception("no parked " + pl);
            var model = holder.GetComponentsInChildren<Transform>().First(t => t.parent == holder && !t.GetComponent<TextMesh>()).gameObject;
            // only this car: the neighbours (3 m bays) would block the side view, the painted bay labels draw over everything
            foreach (Transform t in holder.parent) if (t != holder) t.gameObject.SetActive(false);
            foreach (var tm in holder.GetComponentsInChildren<TextMesh>()) tm.gameObject.SetActive(false);
            var b = RB(model);
            // the frame: ground centre of the parked model, forward = the model's front (same rule as the importer: red tail lamps at the rear)
            var g = new Vector3(b.center.x, b.min.y, b.center.z);
            var f = ParkFront(cfg);
            var r = Vector3.Cross(Vector3.up, f);
            GameObject after = null;
            if (faza == "poslije")
            {
                model.SetActive(false);
                var kd = Kind(cfg);
                after = Spawn(kd, paint, g, Quaternion.LookRotation(f), 0, false);
            }
            Directory.CreateDirectory(outDir);
            var cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
            cam.fieldOfView = 40;
            foreach (var v in Views)
            {
                var (eye, look) = Pose(v, g, f, r, cfg.length);
                cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                Shot(cam, Path.Combine(outDir, $"{faza}-{v}.png"));
            }
            if (after) UnityEngine.Object.DestroyImmediate(after);
            // after: the 3/4 front view in every paint colour (boje-N.png)
            if (faza == "poslije")
            {
                var kd = Kind(cfg);
                for (int i = 0; i < kd.paints.Length; i++)
                {
                    var go = Spawn(kd, i, g, Quaternion.LookRotation(f), 0, false);
                    var (eye, look) = Pose("34-sprijeda", g, f, r, cfg.length);
                    cam.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye));
                    Shot(cam, Path.Combine(outDir, $"{faza}-boja-{i}.png"));
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }
            model.SetActive(true);
            Debug.Log($"[kandidat] shots {faza} -> {outDir}");
        }

        /// the parked (raw) model's front in the AutiParking scene: +z or -z (AutiParking.Upright does not decide the front)
        static Vector3 ParkFront(KCar cfg) => cfg.parkFront == "-" ? Vector3.back : Vector3.forward;

        /// the kind's parts as GameObjects (lod, lamps lit), for still shots
        public static GameObject Spawn(TrafficKind kd, int paint, Vector3 pos, Quaternion rot, int lod, bool lampsOn)
        {
            var root = new GameObject(kd.name + "_preview"); root.transform.SetPositionAndRotation(pos, rot);
            var parts = lod == 2 ? kd.lod2 : lod == 1 ? kd.lod1 : kd.lod0;
            foreach (var pt in parts)
            {
                var go = new GameObject("part"); go.transform.SetParent(root.transform, false);
                var m = pt.local; go.transform.localPosition = m.GetColumn(3); go.transform.localRotation = m.rotation; go.transform.localScale = m.lossyScale;
                // only this part's submesh
                var mesh = UnityEngine.Object.Instantiate(pt.mesh); var tri = mesh.GetTriangles(pt.sub); mesh.subMeshCount = 1; mesh.SetTriangles(tri, 0);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mat = pt.paint ? kd.paints[Mathf.Clamp(paint, 0, kd.paints.Length - 1)] : (lampsOn && pt.matOn ? pt.matOn : pt.mat);
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            return root;
        }

        static void Shot(Camera cam, string file)
        {
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt; cam.Render(); cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            File.WriteAllBytes(file, tex.EncodeToPNG());
            cam.targetTexture = null; RenderTexture.active = null; UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
        }
    }
}
