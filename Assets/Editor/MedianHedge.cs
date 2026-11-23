// The D8 central median as in Kaštela: a dense oleander hedge (pink and white, 1.5–2 m) with shrubs and dry grass at its base.
// The median of Marko's D8 tiles is not a mesh of its own (the tiles' "ground" between the two carriageways), so it is found on a
// 0.5 m grid: a cell off the asphalt with the D8's asphalt both north and south of it within MAXW (two carriageways, a strip
// between). Junction openings and median crossings are asphalt, so the hedge stops there by itself. The whole D8 in Kaštela
// (Štafilić → Sućurac) is scanned in 200 m chunks along x, each in a narrow z window around the D8 line.
// Plants are TreeRenderer prototypes (instanced, a cheap far mesh, short draw distance): Plants is the list of (prototype, weight);
// a new plant (e.g. another Tripo shrub) is one more entry here + one Proto in Protos().
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class MedianHedge
    {
        /// the D8 through Kaštela (game x): Štafilić (čvor 41) to Sućurac (05); the scan runs in CHUNK m pieces along x, each in a
        /// z window of ±HALF m around the D8 line (its section / junction models' centres)
        public static double X0 = -9600, X1 = -420;
        const double CHUNK = 200, HALF = 45;
        /// the D8's models (dionice d01–d25, the junctions on it): their centres give the D8 line
        static readonly string[] D8Keys =
        {
            "krizanje-41", "dionica-d22", "krizanje-43", "dionica-d23", "krizanje-45", "dionica-d24", "dionica-d25", "krizanje-47",
            "dionica-d16", "dionica-d17", "dionica-d18", "dionica-d19", "dionica-d20", "dionica-d21", "krizanje-01",
            "dionica-d01", "dionica-d02", "dionica-d03", "dionica-d04", "dionica-d05", "dionica-d06", "krizanje-51",
            "dionica-d07", "dionica-d08", "dionica-d09", "krizanje-02", "dionica-d10", "krizanje-53", "dionica-d11", "krizanje-54",
            "dionica-d12", "krizanje-03", "dionica-d13", "dionica-d14", "krizanje-04", "dionica-d15", "krizanje-05",
        };
        const double STEP = .5, MAXW = 9, KERB = .3;
        /// the hedge's plants: weight = share of the hedge's main slots (oleanders) / of the fill slots (shrubs) / base (grass)
        public static readonly (string id, string slot, float weight)[] Plants =
        {
            ("oleander-roza", "main", .85f), ("oleander-bijeli", "main", .15f),
            ("grm-tamni", "fill", 1f),
            ("zivica-blok", "core", 1f),
            ("trava-suha", "base", 1f),
        };
        const string OUT = WorldBuilder.GEN + "/Nature";
        const string CE = NaturePass.PKG + "/Coast Environment/";

        static T Load<T>(string p) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(p);
        static void Save(UnityEngine.Object o, string path) { if (File.Exists(path)) AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(o, path); }

        // ------------------------------------------------------------------ prototypes
        public static void Protos(List<NaturePass.Proto> list)
        {
            narrow.Clear();
            // oleander (Tripo, TripoPropImporter: oleander.json, LOD0 6k / LOD1 1.5k / LOD2 400 tris): LOD1 near, LOD2 far
            var ol = TripoPropImporter.Prefab("oleander");
            if (!ol) { TripoPropImporter.ImportAll("oleander"); ol = TripoPropImporter.Prefab("oleander"); }
            if (ol)
            {
                var lg = ol.GetComponentInChildren<LODGroup>(); var lods = lg ? lg.GetLODs() : null;
                MeshRenderer R(int i) => lods == null ? ol.GetComponentInChildren<MeshRenderer>() : lods[Mathf.Min(i, lods.Length - 1)].renderers.OfType<MeshRenderer>().FirstOrDefault();
                var near = R(1); var far = R(2);
                // the real D8 median: a solid ~2 m dark-green hedge. Leaves of the albedo darkened (flowers kept), the crown narrowed to
                // 60 % so a 2 m tall plant is ~1.2 m wide (fits the median, rows overlap at 0.85 m); LOD1 (1.5k tris) to 30 m,
                // LOD2 (565 tris, still a full bush, no card) to 300 m; the dark body (grm-zivica, 153 tris far) carries on to 380 m so from the cab it reads as one hedge far ahead
                var pink = DarkLeaves(near.sharedMaterial);
                var white = WhiteFlowers(pink);
                list.Add(new NaturePass.Proto { id = "oleander-roza", mesh = Narrow(near, "oleander_uski", .75f), mats = new[] { pink }, imp = Narrow(far, "oleander_uski_far", .75f), impMat = pink, draw = 300, near = 30 });
                list.Add(new NaturePass.Proto { id = "oleander-bijeli", mesh = Narrow(near, "oleander_uski", .75f), mats = new[] { white }, imp = Narrow(far, "oleander_uski_far", .75f), impMat = white, draw = 300, near = 30 });
            }
            else Debug.LogWarning("[priroda] no oleander prefab (Assets/Models/Props/Tripo/oleander.glb + .json)");
            // dense dark-green filler: the package's rose bush without flowers, tinted to a dark Mediterranean evergreen
            var bush = Load<GameObject>(CE + "Bushes/Prefabs/Prefab_beach_rose_02.prefab");
            if (bush)
            {
                var lg = bush.GetComponentInChildren<LODGroup>(); var lods = lg ? lg.GetLODs() : null;
                MeshRenderer R(int i) => lods == null ? bush.GetComponentInChildren<MeshRenderer>() : lods[Mathf.Min(i, lods.Length - 1)].renderers.OfType<MeshRenderer>().FirstOrDefault();
                var near = R(1); var far = lods != null && lods.Length > 2 ? R(lods.Length - 1) : null;
                var mats = near.sharedMaterials.Select(m => Tint(m, "tamni", new Color(.5f, .62f, .45f, 1))).ToArray();
                // the hedge's body: the same bush narrowed and darker, ~1.7–1.9 m, one every 0.85 m under the oleanders — no see-through
                var dark = new Color(.36f, .48f, .33f, 1);
                list.Add(new NaturePass.Proto { id = "grm-zivica", mesh = Narrow(near, "grm_zivica", .72f), mats = near.sharedMaterials.Select(m => Tint(m, "zivica", dark)).ToArray(), imp = far && far != near ? Narrow(far, "grm_zivica_far", .72f) : null, impMat = far && far != near ? Tint(far.sharedMaterial, "zivica", dark) : null, draw = 380, near = 30 });
                list.Add(new NaturePass.Proto { id = "grm-tamni", mesh = Flat(near, bush.transform, "grm_tamni"), mats = mats, imp = far && far != near ? Flat(far, bush.transform, "grm_tamni_far") : null, impMat = far && far != near ? Tint(far.sharedMaterial, "tamni", new Color(.5f, .62f, .45f, 1)) : null, draw = 200, near = 25 });
            }
            var blok = HedgeBlock(); if (blok != null) list.Add(blok);
            var grass = Load<GameObject>(CE + "Foliage and Grass/Prefabs/Prefab_beach_grass_02_2.prefab");
            if (grass)
            {
                var r = grass.GetComponentInChildren<MeshRenderer>();
                list.Add(new NaturePass.Proto { id = "trava-suha", mesh = Flat(r, grass.transform, "trava_suha"), mats = r.sharedMaterials.Select(m => Tint(m, "suha", new Color(1.05f, .95f, .75f, 1))).ToArray(), draw = 35, near = 1e5f });
            }
        }

        /// 23. 11. (Marko: "di živica??" — the oleanders alone read as low scattered shrubs with gaps and paper-like flecks): the
        /// hedge's body is a block of dense leaf cards, 1.0 m long × 1.1 m wide × 1.9 m tall with a rounded top (leaf sprigs of the
        /// package's rose atlas, dark evergreen, Magistrala/Foliage: lit as one volume), one every 0.9 m so the blocks overlap into a
        /// continuous hedge; the oleanders stand in it as the flowering accents. Seeded, the same mesh every build.
        static NaturePass.Proto HedgeBlock()
        {
            const string tp = CE + "Bushes/Textures/T_beach_rose_BC_O.png";
            var tex = Load<Texture2D>(tp); var sh = Shader.Find("Magistrala/Foliage"); if (!tex || !sh) return null;
            // leaf-sprig rects of the atlas (u0, v0, u1, v1; v from the bottom), square in pixels (the atlas is 1:2)
            var R = new[] { new Vector4(.12f, .63f, .52f, .83f), new Vector4(.52f, .58f, .94f, .79f), new Vector4(.10f, .48f, .50f, .68f), new Vector4(.55f, .40f, .95f, .60f), new Vector4(.30f, .70f, .70f, .90f) };
            var rnd = new System.Random(2311);
            float F() => (float)rnd.NextDouble();
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            const float L = 1.0f, W = 1.1f, H = 1.9f;
            for (int i = 0; i < 80; i++)
            {
                // a point in the block, most of them pushed out to its skin (a full surface from outside), the top rounded
                var p = new Vector3((F() - .5f) * L, F() * H, (F() - .5f) * W);
                if (F() < .7f)
                {
                    int ax = rnd.Next(3);
                    if (ax == 0) p.z = (F() < .5f ? -1 : 1) * W * (.38f + .12f * F());
                    else if (ax == 1) p.y = H * (.85f + .15f * F());
                    else p.x = (F() < .5f ? -1 : 1) * L * (.4f + .1f * F());
                }
                float top = H - .35f * Mathf.Pow(Mathf.Abs(p.z) / (W * .5f), 2); p.y = Mathf.Clamp(p.y, .15f, top);
                float sz = .5f + .22f * F();          // (80 bigger cards: 120 cost ~ms of alpha-tested overdraw along the whole D8)
                var q = Quaternion.Euler(F() * 360, F() * 360, F() * 360);
                Vector3 a = q * Vector3.right * sz * .5f, b = q * Vector3.up * sz * .5f, nn = q * Vector3.forward;
                var r = R[rnd.Next(R.Length)]; int b0 = v.Count;
                v.Add(p - a - b); v.Add(p + a - b); v.Add(p + a + b); v.Add(p - a + b);
                for (int k = 0; k < 4; k++) n.Add(nn);
                uv.Add(new Vector2(r.x, r.y)); uv.Add(new Vector2(r.z, r.y)); uv.Add(new Vector2(r.z, r.w)); uv.Add(new Vector2(r.x, r.w));
                t.AddRange(new[] { b0, b0 + 1, b0 + 2, b0, b0 + 2, b0 + 3 });
            }
            var m = new Mesh { name = "zivica_blok" }; m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
            Save(m, $"{OUT}/zivica_blok.asset");
            string mp = $"{OUT}/zivica_blok.mat"; var mat = Load<Material>(mp);
            if (!mat) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, mp); } else mat.shader = sh;
            mat.SetTexture("baseColorTexture", tex); mat.SetColor("baseColorFactor", new Color(.74f, .86f, .64f, 1)); mat.SetFloat("alphaCutoff", .45f);
            mat.SetFloat("_CrownY", H * .55f); mat.SetFloat("_CrownR", 1.1f); mat.SetFloat("_Bend", .75f); mat.SetFloat("_Inner", .45f); mat.SetFloat("_Vary", .06f);
            mat.renderQueue = 2450; mat.enableInstancing = true; EditorUtility.SetDirty(mat);
            return new NaturePass.Proto { id = "zivica-blok", mesh = Load<Mesh>($"{OUT}/zivica_blok.asset"), mats = new[] { mat }, draw = 260, near = 1e5f };
        }

        static readonly Dictionary<string, Mesh> narrow = new Dictionary<string, Mesh>();
        /// a crown narrowed (x, z × k, in the prefab root's frame), height kept
        static Mesh Narrow(MeshRenderer r, string name, float k)
        {
            if (narrow.TryGetValue(name, out var c) && c) return c;
            var src = r.GetComponent<MeshFilter>().sharedMesh; var m = UnityEngine.Object.Instantiate(src); m.name = name; var v = src.vertices;
            var M = r.transform.root.worldToLocalMatrix * r.transform.localToWorldMatrix;
            for (int i = 0; i < v.Length; i++) { var q = M.MultiplyPoint3x4(v[i]); v[i] = new Vector3(q.x * k, q.y, q.z * k); }
            m.vertices = v; m.RecalculateBounds(); Save(m, $"{OUT}/{name}.asset"); return narrow[name] = m;
        }

        /// oleander leaves darker (the D8 hedge reads dark green), flower pixels unchanged
        static Material DarkLeaves(Material src)
        {
            var tex = src.GetTexture("_BaseMap") as Texture2D; if (!tex) return src;
            if (!AssetDatabase.IsValidFolder(OUT)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Nature");
            string tp = $"{OUT}/oleander_tamni_albedo.png";
            var t = new Texture2D(2, 2); t.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(tex)));
            var px = t.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i]; int r = c.r, g = c.g, b = c.b;
                float pinkness = Mathf.Clamp01(((r - g) - 22) / 50f) * Mathf.Clamp01(((b - g) + 8) / 30f), k = 1 - pinkness;
                px[i] = new Color32((byte)(r * (1 - .55f * k)), (byte)(g * (1 - .4f * k)), (byte)(b * (1 - .55f * k)), c.a);
            }
            t.SetPixels32(px); File.WriteAllBytes(tp, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(tp);
            var imp = (TextureImporter)AssetImporter.GetAtPath(tp); imp.sRGBTexture = true; imp.mipmapEnabled = true; imp.SaveAndReimport();
            string mp = $"{OUT}/oleander_tamni.mat"; var m = Load<Material>(mp);
            if (!m) { m = new Material(src); AssetDatabase.CreateAsset(m, mp); } else { m.shader = src.shader; m.CopyPropertiesFromMaterial(src); }
            m.SetTexture("_BaseMap", Load<Texture2D>(tp)); m.enableInstancing = true; EditorUtility.SetDirty(m);
            return m;
        }

        /// a mesh in its prefab root's frame (TreeRenderer draws at the instance's TRS only)
        static Mesh Flat(MeshRenderer r, Transform root, string name)
        {
            var src = r.GetComponent<MeshFilter>().sharedMesh; var M = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
            if (M == Matrix4x4.identity) return src;
            var m = UnityEngine.Object.Instantiate(src); m.name = name; var v = src.vertices; for (int i = 0; i < v.Length; i++) v[i] = M.MultiplyPoint3x4(v[i]);
            m.vertices = v; m.RecalculateBounds(); Save(m, $"{OUT}/{name}.asset"); return m;
        }

        static Material Tint(Material m, string tag, Color c)
        {
            if (!m) return m;
            if (!AssetDatabase.IsValidFolder(OUT)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Nature");
            string p = $"{OUT}/{m.name}_{tag}.mat"; var t = Load<Material>(p);
            if (!t) { t = new Material(m); AssetDatabase.CreateAsset(t, p); } else { t.shader = m.shader; t.CopyPropertiesFromMaterial(m); }
            foreach (var prop in new[] { "_BaseColor", "_Color" }) if (t.HasProperty(prop)) t.SetColor(prop, c);
            t.enableInstancing = true; EditorUtility.SetDirty(t); return t;
        }

        /// white-flowered oleander: the pink flower pixels of the albedo turned to warm white, the leaves unchanged
        static Material WhiteFlowers(Material pink)
        {
            var tex = pink.GetTexture("_BaseMap") as Texture2D; if (!tex) return pink;
            string tp = $"{OUT}/oleander_bijeli_albedo.png";
            if (!AssetDatabase.IsValidFolder(OUT)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Nature");
            var src = new Texture2D(2, 2); src.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(tex)));
            var px = src.GetPixels32(); int n = 0;
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i]; int r = c.r, g = c.g, b = c.b;
                // pink: red clearly over green, blue over green (magenta side), not a brown / orange stem
                float pinkness = Mathf.Clamp01(((r - g) - 22) / 50f) * Mathf.Clamp01(((b - g) + 8) / 30f);
                if (pinkness <= 0) continue;
                float l = Mathf.Max(r, Mathf.Max(g, b)); var w = new Color32((byte)Mathf.Min(255, l * .86f), (byte)Mathf.Min(255, l * .83f), (byte)Mathf.Min(255, l * .76f), c.a); // soft warm white (a full white read as paper flecks)
                px[i] = Color32.Lerp(c, w, pinkness); n++;
            }
            src.SetPixels32(px); File.WriteAllBytes(tp, src.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(src);
            AssetDatabase.ImportAsset(tp);
            var imp = (TextureImporter)AssetImporter.GetAtPath(tp); imp.sRGBTexture = true; imp.mipmapEnabled = true; imp.SaveAndReimport();
            string mp = $"{OUT}/oleander_bijeli.mat"; var m = Load<Material>(mp);
            if (!m) { m = new Material(pink); AssetDatabase.CreateAsset(m, mp); } else { m.shader = pink.shader; m.CopyPropertiesFromMaterial(pink); }
            m.SetTexture("_BaseMap", Load<Texture2D>(tp)); m.enableInstancing = true; EditorUtility.SetDirty(m);
            Debug.Log($"[priroda] oleander white: {n} flower pixels recoloured");
            return m;
        }

        // ------------------------------------------------------------------ placement
        public delegate void PutFn(double gx, double y, double gz, float yaw, float s, string id);

        public static int Place(PutFn put, List<ModelJ> all)
        {
            var line = all.Where(m => D8Keys.Contains(m.key)).Select(m => (x: m.x, z: m.z)).OrderBy(p => p.x).ToList();
            if (line.Count < 2) { Debug.LogWarning("[priroda] median: no D8 models"); return 0; }
            double Zc(double x)
            {
                if (x <= line[0].x) return line[0].z; if (x >= line[line.Count - 1].x) return line[line.Count - 1].z;
                int k = 1; while (line[k].x < x) k++;
                double t = (x - line[k - 1].x) / Math.Max(1e-6, line[k].x - line[k - 1].x); return line[k - 1].z + (line[k].z - line[k - 1].z) * t;
            }
            var rnd = new System.Random(4711); int n = 0, cells = 0; double lastMain = -1e9; lastBlock = -1e9;
            for (double cx = X0; cx < X1; cx += CHUNK)
            {
                double cx1 = Math.Min(X1, cx + CHUNK);
                double za = Math.Min(Math.Min(Zc(cx), Zc(cx1)), Zc((cx + cx1) / 2)) - HALF, zb = Math.Max(Math.Max(Zc(cx), Zc(cx1)), Zc((cx + cx1) / 2)) + HALF;
                n += Chunk(put, all, cx, cx1, za, zb, rnd, ref lastMain, ref cells);
            }
            Debug.Log($"[priroda] median: {cells} cells of 0.5 m, {n} plants (x {X0}…{X1}, D8 line from {line.Count} models)");
            return n;
        }

        /// the D8 median's centre line (street lamps, PropsBuilder.Lamps): the same scan as the hedge (a strip off the asphalt with
        /// a carriageway north and south of it within MAXW), one column every `step` m along x: (x, centre z, half width)
        public static List<(double x, double zc, double half)> MedianLine(List<ModelJ> all, double step = 1)
        {
            var res = new List<(double, double, double)>();
            var line = all.Where(m => D8Keys.Contains(m.key)).Select(m => (x: m.x, z: m.z)).OrderBy(p => p.x).ToList();
            if (line.Count < 2) return res;
            double Zc(double x)
            {
                if (x <= line[0].x) return line[0].z; if (x >= line[line.Count - 1].x) return line[line.Count - 1].z;
                int k = 1; while (line[k].x < x) k++;
                double t = (x - line[k - 1].x) / Math.Max(1e-6, line[k].x - line[k - 1].x); return line[k - 1].z + (line[k].z - line[k - 1].z) * t;
            }
            for (double cx = X0; cx < X1; cx += CHUNK)
            {
                double cx1 = Math.Min(X1, cx + CHUNK);
                double Z0 = Math.Min(Math.Min(Zc(cx), Zc(cx1)), Zc((cx + cx1) / 2)) - HALF, Z1 = Math.Max(Math.Max(Zc(cx), Zc(cx1)), Zc((cx + cx1) / 2)) + HALF;
                var models = all.FindAll(m => m.x + m.half + 25 > cx && m.x - m.half - 25 < cx1 && m.z + m.half + 25 > Z0 && m.z - m.half - 25 < Z1);
                int nz = (int)((Z1 - Z0) / STEP); 
                for (double x = cx + step / 2; x < cx1; x += step)
                {
                    var asph = new bool[nz];
                    for (int j = 0; j < nz; j++) asph[j] = LaneProfile.OnAsphalt(models, x, Z0 + (j + .5) * STEP);
                    for (int j = 0; j < nz; j++)
                    {
                        if (asph[j]) continue;
                        int j1 = j; while (j1 + 1 < nz && !asph[j1 + 1]) j1++;
                        int a = j - 1, b = j1 + 1;
                        if (a >= 0 && b < nz && (b - a) * STEP <= MAXW && (b - a) * STEP >= 1.2)
                        {
                            int ka = 0, kb = 0; while (a - ka > 0 && asph[a - ka] && ka < 12) ka++; while (b + kb < nz - 1 && asph[b + kb] && kb < 12) kb++;
                            if (ka >= 10 && kb >= 10) res.Add((x, Z0 + ((j + j1) / 2.0 + .5) * STEP, (j1 - j + 1) * STEP / 2));
                        }
                        j = j1;
                    }
                }
            }
            return res;
        }

        static double lastBlock;
        static int Chunk(PutFn put, List<ModelJ> all, double X0, double X1, double Z0, double Z1, System.Random rnd, ref double lastMain, ref int cells)
        {
            var models = all.FindAll(m => m.x + m.half + 25 > X0 && m.x - m.half - 25 < X1 && m.z + m.half + 25 > Z0 && m.z - m.half - 25 < Z1);
            int nx = (int)Math.Round((X1 - X0) / STEP), nz = (int)((Z1 - Z0) / STEP);
            bool Asph(double x, double z) => LaneProfile.OnAsphalt(models, x, z);
            // column scan (x fixed): asphalt runs along z; a median cell has asphalt north and south within MAXW total
            var asph = new bool[nx, nz];
            for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) asph[i, j] = Asph(X0 + (i + .5) * STEP, Z0 + (j + .5) * STEP);
            var edge = new float[nx, nz]; // distance to the nearest asphalt along the column (m), 0 off the median
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (asph[i, j]) continue;
                    int a = j, b = j; int lim = (int)(MAXW / STEP);
                    while (a > 0 && !asph[i, a] && j - a < lim) a--;
                    while (b < nz - 1 && !asph[i, b] && b - j < lim) b++;
                    if (!asph[i, a] || !asph[i, b] || (b - a) * STEP > MAXW || (b - a) * STEP < 1.2) continue;
                    // both sides must be a carriageway (wide asphalt), not a kerb island or a lane line gap
                    int ka = 0, kb = 0; while (a - ka > 0 && asph[i, a - ka] && ka < 12) ka++; while (b + kb < nz - 1 && asph[i, b + kb] && kb < 12) kb++;
                    if (ka < 10 || kb < 10) continue;
                    edge[i, j] = (float)Math.Min((j - a) * STEP, (b - j) * STEP); cells++;
                }
            int n = 0;
            float Pick(string slot, out string id)
            {
                var opts = Plants.Where(p => p.slot == slot).ToArray(); float tot = opts.Sum(p => p.weight), r = (float)rnd.NextDouble() * tot;
                foreach (var p in opts) { if ((r -= p.weight) <= 0) { id = p.id; return p.weight; } }
                id = opts.Length > 0 ? opts[opts.Length - 1].id : null; return 0;
            }
            double Y(double x, double z) { double h = Tiles.Height(x, z); var q = WorldData.ModelTerrain(models, x, z, 2, h); return q ?? h; }
            // 23. 11. (Marko: "di živica??" — the strip read as ~0.5 m shrubs with gaps): scales are now metres of height ÷ the
            // prototype mesh's height (they were raw multipliers of the meshes, whose heights are not 2 m): oleanders 1.8–2.4 m,
            // the dark body 1.6–2.0 m, fill shrubs 1.0–1.4 m
            float HS(string id, float metres, float fallback) { float h = NaturePass.Height(id); return h > .05f ? metres / h : fallback; }
            // along each median column: the centre cell (max edge distance) gets the hedge; walk x, a main plant every ~1.3 m
            for (int i = 0; i < nx; i++)
            {
                double x = X0 + (i + .5) * STEP;
                // the median cell(s) of this column: centres of runs
                for (int j = 0; j < nz; j++)
                {
                    if (edge[i, j] <= 0) continue;
                    int j1 = j; while (j1 + 1 < nz && edge[i, j1 + 1] > 0) j1++;
                    double zc = Z0 + ((j + j1) / 2.0 + .5) * STEP, half = (j1 - j + 1) * STEP / 2;
                    double room = half - KERB;                              // half-width a plant may take
                    if (room > .35)
                    {
                        if (x - lastBlock >= .9)
                        {
                            Pick("core", out var cid);
                            if (cid != null) { put(x, Y(x, zc) - .08, zc + (rnd.NextDouble() - .5) * .1, (float)((rnd.NextDouble() - .5) * 16), HS(cid, (float)(2.0 + .4 * rnd.NextDouble()), 1f), cid); n++; }
                            lastBlock = x;
                        }
                        if (x - lastMain >= 4.5 + 3 * rnd.NextDouble())   // flowering accents every ~4.5–7.5 m (more read as pale flecks)
                        {
                            // oleander (narrowed crown): 1.9–2.2 m tall × ~1.2 m wide, one every 0.85 m so the crowns overlap — a solid hedge;
                            // a median with room for it gets two staggered rows (~1.2–2 m planted band), a narrow one a single row
                            int rows = 1; bool odd = ((int)Math.Round(x / .85)) % 2 == 1;
                            for (int q = 0; q < rows; q++)
                            {
                                Pick("main", out var id);
                                double off = rows == 1 ? 0 : (q == 0 ? -1 : 1) * Math.Min(.5, room - .55) + (odd ? .1 : -.1);
                                float s = HS(id ?? "", (float)(2.0 + .5 * rnd.NextDouble()), (float)(1.0 + .16 * rnd.NextDouble()));
                                if (id != null) { put(x + (rnd.NextDouble() - .5) * .25 + (q == 1 ? .42 : 0), Y(x, zc) - .08, zc + off + (rnd.NextDouble() - .5) * .15, (float)(rnd.NextDouble() * 360), s, id); n++; }
                            }
                            lastMain = x;
                        }
                        // fill between and under: shrubs where the oleanders leave gaps, grass at the base on both edges
                        if (rnd.NextDouble() < .04) { Pick("fill", out var id); if (id != null) { put(x + (rnd.NextDouble() - .5) * .5, Y(x, zc) - .05, zc + (rnd.NextDouble() - .5) * Math.Min(room, 2.4), (float)(rnd.NextDouble() * 360), HS(id, (float)(1.0 + .4 * rnd.NextDouble()), (float)(1.1 + .3 * rnd.NextDouble())), id); n++; } }
                        if (rnd.NextDouble() < .4) { Pick("base", out var id); if (id != null) { double side = rnd.NextDouble() < .5 ? -1 : 1; put(x, Y(x, zc), zc + side * Math.Min(Math.Max(0, room - .15), 1.9), (float)(rnd.NextDouble() * 360), (float)(.7 + .4 * rnd.NextDouble()), id); n++; } }
                    }
                    j = j1;
                }
            }
            return n;
        }
    }
}
