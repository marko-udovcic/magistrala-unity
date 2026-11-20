// The D8 central median as in Kaštela: a dense oleander hedge (pink and white, 1.5–2 m) with shrubs and dry grass at its base.
// The median of Marko's D8 tiles is not a mesh of its own (the tiles' "ground" between the two carriageways), so it is found on a
// 0.5 m grid: a cell off the asphalt with the D8's asphalt both north and south of it within MAXW (two carriageways, a strip
// between). Junction openings and median crossings are asphalt, so the hedge stops there by itself.
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
        /// probe stretch of the D8 (game x), Lukšić warehouse ± 500 m; widen to extend
        public static double X0 = -7250, X1 = -6250, Z0 = -5700, Z1 = -5500;
        const double STEP = .5, MAXW = 9, KERB = .3;
        /// the hedge's plants: weight = share of the hedge's main slots (oleanders) / of the fill slots (shrubs) / base (grass)
        public static readonly (string id, string slot, float weight)[] Plants =
        {
            ("oleander-roza", "main", .7f), ("oleander-bijeli", "main", .3f),
            ("grm-tamni", "fill", 1f),
            ("trava-suha", "base", 1f),
        };
        const string OUT = WorldBuilder.GEN + "/Nature";
        const string CE = NaturePass.PKG + "/Coast Environment/";

        static T Load<T>(string p) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(p);
        static void Save(UnityEngine.Object o, string path) { if (File.Exists(path)) AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(o, path); }

        // ------------------------------------------------------------------ prototypes
        public static void Protos(List<NaturePass.Proto> list)
        {
            // oleander (Tripo, TripoPropImporter: oleander.json, LOD0 6k / LOD1 1.5k / LOD2 400 tris): LOD1 near, LOD2 far
            var ol = TripoPropImporter.Prefab("oleander");
            if (!ol) { TripoPropImporter.ImportAll("oleander"); ol = TripoPropImporter.Prefab("oleander"); }
            if (ol)
            {
                var lg = ol.GetComponentInChildren<LODGroup>(); var lods = lg ? lg.GetLODs() : null;
                MeshRenderer R(int i) => lods == null ? ol.GetComponentInChildren<MeshRenderer>() : lods[Mathf.Min(i, lods.Length - 1)].renderers.OfType<MeshRenderer>().FirstOrDefault();
                var near = R(1); var far = R(2);
                var pink = near.sharedMaterial; pink.enableInstancing = true;
                var white = WhiteFlowers(pink);
                Mesh M(MeshRenderer r) => r.GetComponent<MeshFilter>().sharedMesh;
                list.Add(new NaturePass.Proto { id = "oleander-roza", mesh = M(near), mats = new[] { pink }, imp = M(far), impMat = pink, draw = 160, near = 14 });
                list.Add(new NaturePass.Proto { id = "oleander-bijeli", mesh = M(near), mats = new[] { white }, imp = M(far), impMat = white, draw = 160, near = 14 });
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
                list.Add(new NaturePass.Proto { id = "grm-tamni", mesh = Flat(near, bush.transform, "grm_tamni"), mats = mats, imp = far && far != near ? Flat(far, bush.transform, "grm_tamni_far") : null, impMat = far && far != near ? Tint(far.sharedMaterial, "tamni", new Color(.5f, .62f, .45f, 1)) : null, draw = 90, near = 12 });
            }
            var grass = Load<GameObject>(CE + "Foliage and Grass/Prefabs/Prefab_beach_grass_02_2.prefab");
            if (grass)
            {
                var r = grass.GetComponentInChildren<MeshRenderer>();
                list.Add(new NaturePass.Proto { id = "trava-suha", mesh = Flat(r, grass.transform, "trava_suha"), mats = r.sharedMaterials.Select(m => Tint(m, "suha", new Color(1.05f, .95f, .75f, 1))).ToArray(), draw = 35, near = 1e5f });
            }
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
                float l = Mathf.Max(r, Mathf.Max(g, b)); var w = new Color32((byte)Mathf.Min(255, l * 1.0f), (byte)Mathf.Min(255, l * .98f), (byte)Mathf.Min(255, l * .93f), c.a);
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
            var models = all.FindAll(m => m.x + m.half > X0 && m.x - m.half < X1);
            int nx = (int)((X1 - X0) / STEP), nz = (int)((Z1 - Z0) / STEP);
            bool Asph(double x, double z) => LaneProfile.OnAsphalt(models, x, z);
            // column scan (x fixed): asphalt runs along z; a median cell has asphalt north and south within MAXW total
            var asph = new bool[nx, nz];
            for (int i = 0; i < nx; i++) for (int j = 0; j < nz; j++) asph[i, j] = Asph(X0 + (i + .5) * STEP, Z0 + (j + .5) * STEP);
            var edge = new float[nx, nz]; // distance to the nearest asphalt along the column (m), 0 off the median
            int cells = 0;
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
            var rnd = new System.Random(4711); int n = 0;
            float Pick(string slot, out string id)
            {
                var opts = Plants.Where(p => p.slot == slot).ToArray(); float tot = opts.Sum(p => p.weight), r = (float)rnd.NextDouble() * tot;
                foreach (var p in opts) { if ((r -= p.weight) <= 0) { id = p.id; return p.weight; } }
                id = opts.Length > 0 ? opts[opts.Length - 1].id : null; return 0;
            }
            double Y(double x, double z) { double h = Tiles.Height(x, z); var q = WorldData.ModelTerrain(models, x, z, 2, h); return q ?? h; }
            // along each median column: the centre cell (max edge distance) gets the hedge; walk x, a main plant every ~1.3 m
            double lastMain = -1e9;
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
                        if (x - lastMain >= 1.2)
                        {
                            // oleander: ~1.9 m tall × 2 m wide at s = 1; fit the median (half-width ≤ room), at least 70 %; a wide
                            // median gets two staggered rows (a ~3 m planted band, as the D8 in Kaštela), a narrow one a single row
                            int rows = room > 2.2 ? 2 : 1; bool odd = ((int)Math.Round(x / 1.2)) % 2 == 1;
                            for (int q = 0; q < rows; q++)
                            {
                                Pick("main", out var id);
                                double off = rows == 1 ? 0 : (q == 0 ? -.75 : .75) + (odd ? .2 : -.2);
                                float s = (float)Math.Min(.95 + .12 * rnd.NextDouble(), Math.Max(.7, (room - Math.Abs(off)) / 1.0));
                                if (id != null) { put(x + (rnd.NextDouble() - .5) * .35 + (q == 1 ? .6 : 0), Y(x, zc) - .05, zc + off + (rnd.NextDouble() - .5) * .25, (float)(rnd.NextDouble() * 360), s, id); n++; }
                            }
                            lastMain = x;
                        }
                        // fill between and under: shrubs where the oleanders leave gaps, grass at the base on both edges
                        if (rnd.NextDouble() < .55) { Pick("fill", out var id); if (id != null) { put(x + (rnd.NextDouble() - .5) * .5, Y(x, zc) - .05, zc + (rnd.NextDouble() - .5) * Math.Min(room, 2.4), (float)(rnd.NextDouble() * 360), (float)(.9 + .3 * rnd.NextDouble()), id); n++; } }
                        if (rnd.NextDouble() < .4) { Pick("base", out var id); if (id != null) { double side = rnd.NextDouble() < .5 ? -1 : 1; put(x, Y(x, zc), zc + side * Math.Min(Math.Max(0, room - .15), 1.9), (float)(rnd.NextDouble() * 360), (float)(.7 + .4 * rnd.NextDouble()), id); n++; } }
                    }
                    j = j1;
                }
            }
            Debug.Log($"[priroda] median: {cells} cells of 0.5 m, {n} plants (x {X0}…{X1})");
            return n;
        }
    }
}
