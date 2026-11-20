#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Magistrala.EditorTools
{
    /// <summary>Editor import of world2/content/modelBuildings.ts. All geometry is authored in game space,
    /// then reflected across Z with reversed indices. Call Build from an editor tool after importing the shader.</summary>
    public static class BuildingsBuilder
    {
        const string MeshFolder = "Assets/Generated/Buildings";
        const string MaterialPath = "Assets/Generated/Materials/Buildings.mat";
        const float CellSize = 256;
        static readonly Color BuildingColor = Srgb(.78f, .74f, .66f);
        static readonly Color Steel = Srgb(.55f, .58f, .6f);
        static readonly Color Kerb = Srgb(.8f, .8f, .77f);

        /// <summary>groundAtGame must sample terrain / road / asphalt in GAME x,z coordinates and return absolute y.
        /// Parent with worldPositionStays=true preserves absolute Unity placement, including under a transformed parent.
        /// Rebuilding updates mesh assets in place (preserves GUIDs). Existing scene roots are left to the caller.</summary>
        /// pump islands without the box dispensers: PropsBuilder stands Tripo's aparat-gorivo on them (PumpSpotsPath, game frame:
        /// canopy id, island top centre, unit axis along the island)
        public static bool TripoPumps = true;
        public const string PumpSpotsPath = "Assets/Generated/Props/pump-spots.json";
        static readonly List<(string id, Vector3 p, Vector3 a)> PumpSpots = new List<(string, Vector3, Vector3)>();

        public static GameObject Build(string jsonPath, Func<float, float, float> groundAtGame,
            Transform parent, float minX = float.NegativeInfinity, float maxX = float.PositiveInfinity)
        {
            PumpSpots.Clear();
            if (groundAtGame == null) throw new ArgumentNullException(nameof(groundAtGame));
            if (float.IsNaN(minX) || float.IsNaN(maxX) || minX > maxX)
                throw new ArgumentException("Invalid game x interval.");
            var file = MiniJson.Parse(File.ReadAllText(jsonPath)) as Dictionary<string, object>;
            if (file == null || !(Get(file, "objects") is List<object> objects))
                throw new FormatException("Expected a JSON object containing an objects array.");
            var chunks = new SortedDictionary<int, Geometry>();
            int count = 0;
            // Pumpa Kaštela by geometry around the job's fuel bay (as the web's findPumpaKastela; ids change with every package import)
            var bays = PumpaBays(Path.Combine(Path.GetDirectoryName(jsonPath), "stops.json"));
            var pumpa = FindPumpaKastela(objects, bays);
            if (pumpa.canopy == null || pumpa.shop == null || pumpa.sign == null)
                Debug.LogError($"Buildings: Pumpa Kaštela not found by geometry around the kastela_pumpa bay (canopy {pumpa.canopy}, shop {pumpa.shop}, sign {pumpa.sign}).");
            else Debug.Log($"Buildings: Pumpa Kaštela = {pumpa.canopy}, {pumpa.shop}, {pumpa.sign}.");
            foreach (object value in objects)
            {
                var o = value as Dictionary<string, object>;
                if (o == null) continue;
                float x = Num(o, "x"), z = Num(o, "z");
                if (!Finite(x) || !Finite(z)) throw new FormatException("Non-finite building position.");
                if (x < minX || x > maxX) continue;
                int cell = Mathf.FloorToInt(x / CellSize);
                if (!chunks.TryGetValue(cell, out Geometry geo))
                {
                    geo = new Geometry(new Vector3(cell * CellSize, 0, z));
                    chunks.Add(cell, geo);
                }
                ApplyBranding(o, pumpa, bays); // ModelBuildings2 calls withBranding before drawing.
                new Recipe(o, groundAtGame, geo).Build();
                count++;
            }
            EnsureFolder(MeshFolder);
            EnsureFolder("Assets/Generated/Materials");
            Shader shader = Shader.Find("Magistrala/VertexColorLit");
            if (shader == null) throw new InvalidOperationException("Import Assets/Shaders/VertexColorLit.shader first.");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "Buildings" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", Color.white);
            material.SetTexture("_BaseMap", null);
            material.SetFloat("_Smoothness", .15f);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Cull", (float)CullMode.Back);
            EditorUtility.SetDirty(material);
            var root = new GameObject("Buildings");
            root.transform.SetParent(parent, true);
            try
            {
                foreach (var pair in chunks)
                {
                    Geometry g = pair.Value;
                    if (g.Positions.Count == 0) continue;
                    var mesh = new Mesh { name = "Buildings_" + pair.Key, indexFormat = IndexFormat.UInt32 };
                    mesh.SetVertices(g.Positions);
                    mesh.SetNormals(g.Normals);
                    mesh.SetColors(g.Colors); // Color, not Color32: retain linear precision.
                    mesh.SetUVs(0, g.UVs);
                    mesh.SetUVs(1, g.Data);
                    mesh.SetUVs(2, new Vector2[g.Positions.Count]); // no baked occlusion (a missing stream is not zero on Metal)
                    mesh.subMeshCount = 2;
                    mesh.SetTriangles(g.Indices, 0);
                    mesh.SetTriangles(g.GlassIndices, 1);
                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();
                    HouseBuilder.Compact(mesh);
                    string path = MeshFolder + "/Buildings_" + pair.Key + ".asset";
                    Mesh saved = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                    if (saved == null) { AssetDatabase.CreateAsset(mesh, path); saved = mesh; }
                    else
                    {
                        EditorUtility.CopySerialized(mesh, saved);
                        Object.DestroyImmediate(mesh);
                        EditorUtility.SetDirty(saved);
                    }
                    var chunk = new GameObject(saved.name);
                    chunk.transform.SetParent(root.transform, false);
                    chunk.transform.localPosition = Reflect(g.Anchor);
                    chunk.AddComponent<MeshFilter>().sharedMesh = saved;
                    var renderer = chunk.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = new[] { BuildingTextures.Facade(), BuildingTextures.Glass() };
                    renderer.shadowCastingMode = ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                    var collider = chunk.AddComponent<MeshCollider>();
                    collider.convex = false;
                    collider.sharedMesh = saved;
                }
                AssetDatabase.SaveAssets();
                EnsureFolder(Path.GetDirectoryName(PumpSpotsPath).Replace('\\', '/'));
                var js = new StringBuilder("{\"spots\":[");
                for (int i = 0; i < PumpSpots.Count; i++)
                {
                    var q = PumpSpots[i];
                    js.Append(i > 0 ? "," : "").Append(string.Format(CultureInfo.InvariantCulture, "{{\"id\":\"{0}\",\"x\":{1:F3},\"y\":{2:F3},\"z\":{3:F3},\"ax\":{4:F4},\"az\":{5:F4}", q.id, q.p.x, q.p.y, q.p.z, q.a.x, q.a.z)).Append("}");
                }
                File.WriteAllText(PumpSpotsPath, js.Append("]}").ToString());
                Debug.Log($"Buildings: imported {count} objects into {root.transform.childCount} chunks, {PumpSpots.Count} pump islands ({PumpSpotsPath}).", root);
                return root;
            }
            catch { Object.DestroyImmediate(root); throw; }
        }

        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
        static Vector3 Reflect(Vector3 v) => new Vector3(v.x, v.y, -v.z);
        static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
        static object Get(Dictionary<string, object> d, string k) => d.TryGetValue(k, out object v) ? v : null;
        static string Str(Dictionary<string, object> d, string k, string fallback = "") => Get(d, k)?.ToString() ?? fallback;
        static float Num(Dictionary<string, object> d, string k, float fallback = 0) =>
            Get(d, k) is double v ? (float)v : fallback;
        // JS +(value ?? fallback) || fallback; explicit zero is meaningful only for the plain Num fields.
        static float NonZero(Dictionary<string, object> d, string k, float fallback)
        { float v = Num(d, k, fallback); return v == 0 ? fallback : v; }
        static IEnumerable<Dictionary<string, object>> Records(Dictionary<string, object> d, string k)
        {
            if (Get(d, k) is List<object> list)
                foreach (object v in list) if (v is Dictionary<string, object> q) yield return q;
        }
        static Color Srgb(float r, float g, float b)
        {
            // Match TS S(): round channels to bytes before decoding.
            return new Color(Linear(Mathf.Floor(r * 255 + .5f) / 255),
                Linear(Mathf.Floor(g * 255 + .5f) / 255), Linear(Mathf.Floor(b * 255 + .5f) / 255), 1);
        }
        static float Linear(float v) => v < .04045f ? v / 12.92f : Mathf.Pow((v + .055f) / 1.055f, 2.4f);
        static Color Hex(string s, Color fallback)
        {
            if (s.StartsWith("#", StringComparison.Ordinal)) s = s.Substring(1);
            if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint n)) return fallback;
            return new Color(Linear(((n >> 16) & 255) / 255f), Linear(((n >> 8) & 255) / 255f), Linear((n & 255) / 255f), 1);
        }
        static Color Tint(Dictionary<string, object> o, string key, Color fallback) => Hex(Str(o, key), fallback);
        static Color Mix(Color c, float k) => new Color(c.r * k + (1-k) * .55f, c.g * k + (1-k) * .55f, c.b * k + (1-k) * .53f, 1);
        static string Kind(Dictionary<string, object> o) => Str(o, "kind", Str(o, "k", "bld"));

        /// a job's fuel bay in GAME x / z (stops.json): centre, yaw (along = (sin, cos)), half length / width
        public struct FuelBay { public double x, z, yaw, hl, hw; }
        /// a canopy column / pump island centre closer than this to the bay goes (island 2.6 m half length + 0.6 m clearance)
        public const double BayClear = 3.3;
        public const string PumpaStop = "kastela_pumpa";

        public static List<FuelBay> PumpaBays(string stopsPath)
        {
            var r = new List<FuelBay>();
            if (!File.Exists(stopsPath)) return r;
            var f = MiniJson.Parse(File.ReadAllText(stopsPath)) as Dictionary<string, object>;
            if (f == null || !(Get(f, "stops") is List<object> stops)) return r;
            foreach (object v in stops)
            {
                if (!(v is Dictionary<string, object> st) || Str(st, "id") != PumpaStop) continue;
                var list = Get(st, "bays") is List<object> bl && bl.Count > 0 ? bl : new List<object> { st };
                foreach (object q in list) if (q is Dictionary<string, object> b)
                    r.Add(new FuelBay { x = Convert.ToDouble(Get(b, "x")), z = Convert.ToDouble(Get(b, "z")), yaw = Convert.ToDouble(Get(b, "yaw")),
                        hl = Convert.ToDouble(Get(b, "hl")), hw = Convert.ToDouble(Get(b, "hw")) });
            }
            return r;
        }
        /// how far a GAME point lies outside a bay rectangle (0 inside)
        public static double BayGap(FuelBay b, double x, double z)
        {
            double fx = Math.Sin(b.yaw), fz = Math.Cos(b.yaw), dx = x - b.x, dz = z - b.z;
            return Math.Max(0, Math.Max(Math.Abs(dx * fx + dz * fz) - b.hl, Math.Abs(-dx * fz + dz * fx) - b.hw));
        }
        /// the canopy over (≤ 5 m from) the bay, the shop = nearest building of the same model with a sign (≤ 25 m from the canopy),
        /// the totem = nearest sign of the same model (≤ 60 m from the bay); as content/modelBuildings.ts findPumpaKastela
        public static (string canopy, string shop, string sign) FindPumpaKastela(List<object> objects, List<FuelBay> bays)
        {
            if (bays == null || bays.Count == 0) return (null, null, null);
            double cx = 0, cz = 0; foreach (var b in bays) { cx += b.x / bays.Count; cz += b.z / bays.Count; }
            Dictionary<string, object> canopy = null; double best = 5;
            foreach (object v in objects)
            {
                if (!(v is Dictionary<string, object> o) || Kind(o) != "canopy") continue;
                double a = Num(o, "a"), tx = Math.Cos(a), tz = Math.Sin(a);
                foreach (var b in bays)
                {
                    double dx = b.x - Num(o, "x"), dz = b.z - Num(o, "z");
                    double g = Math.Max(0, Math.Max(Math.Abs(dx * tx + dz * tz) - Num(o, "l", 2) / 2, Math.Abs(-dx * tz + dz * tx) - Num(o, "w", 2) / 2));
                    if (g <= best) { best = g; canopy = o; }
                }
            }
            if (canopy == null) return (null, null, null);
            string model = Str(canopy, "model");
            Dictionary<string, object> shop = null, sign = null; double bs = 25, bg = 60;
            foreach (object v in objects)
            {
                if (!(v is Dictionary<string, object> o) || Str(o, "model") != model) continue;
                if (Kind(o) == "bld" && Get(o, "signs") is List<object> sl && sl.Count > 0)
                {
                    double d = Math.Sqrt(Math.Pow(Num(o, "x") - Num(canopy, "x"), 2) + Math.Pow(Num(o, "z") - Num(canopy, "z"), 2));
                    if (d <= bs) { bs = d; shop = o; }
                }
                else if (Kind(o) == "sign")
                {
                    double d = Math.Sqrt(Math.Pow(Num(o, "x") - cx, 2) + Math.Pow(Num(o, "z") - cz, 2));
                    if (d <= bg) { bg = d; sign = o; }
                }
            }
            return (Str(canopy, "id"), shop == null ? null : Str(shop, "id"), sign == null ? null : Str(sign, "id"));
        }

        static void ApplyBranding(Dictionary<string, object> o, (string canopy, string shop, string sign) pumpa, List<FuelBay> bays)
        {
            string id = Str(o, "id");
            if (id.Length == 0 || (id != pumpa.canopy && id != pumpa.shop && id != pumpa.sign)) return;
            if (Kind(o) == "canopy")
            {
                o["col"] = "#124eaa"; o["ucol"] = "#f4f5f4"; o["ccol"] = "#e6e8ea"; o["pcol"] = "#2896e1";
                o["s"] = "PUMPA KAŠTELA"; o["tcol"] = "#ffffff";
                // every island / column within BayClear of the job's fuel bay goes (the canopy end stays cantilevered over it)
                double a = Num(o, "a"), tx = Math.Cos(a), tz = Math.Sin(a), nx = -tz, nz = tx, ox = Num(o, "x"), oz = Num(o, "z");
                foreach (string key in new[] { "cols", "pumps" })
                    if (Get(o, key) is List<object> list)
                        list.RemoveAll(v => v is List<object> p && p.Count >= 2 && bays.Exists(b =>
                            BayGap(b, ox + nx * Convert.ToDouble(p[0]) + tx * Convert.ToDouble(p[1]), oz + nz * Convert.ToDouble(p[0]) + tz * Convert.ToDouble(p[1])) < BayClear));
            }
            else if (Kind(o) == "sign") { o["col"] = "#124eaa"; o["s"] = "PUMPA|KAŠTELA|DIZEL · PLIN"; o["tcol"] = "#ffffff"; }
            else
            {
                float sy = -1;
                foreach (var s in Records(o, "signs")) { sy = Num(s, "y", -1); break; }
                foreach (var b in Records(o, "band"))
                {
                    float y = Num(b, "y"), h = Num(b, "h", .6f);
                    if (y == 0) continue;
                    bool fascia = sy >= y && sy <= y + h;
                    b["col"] = fascia ? "#124eaa" : "#2896e1";
                    if (!fascia) b["h"] = (double)Mathf.Max(h, .2f);
                }
                foreach (var s in Records(o, "signs")) { s["s"] = "PUMPA KAŠTELA"; s["tcol"] = "#ffffff"; }
                o["col"] = "#f4f5f4";
            }
        }
        sealed class Geometry
        {
            public readonly Vector3 Anchor;
            public readonly List<Vector3> Positions = new List<Vector3>();
            public readonly List<Vector3> Normals = new List<Vector3>();
            public readonly List<Color> Colors = new List<Color>();
            public readonly List<Vector2> UVs = new List<Vector2>();
            public readonly List<int> Indices = new List<int>();
            // Magistrala/Facade (submesh 0) and Magistrala/WindowInterior (submesh 1, GlassIndices), as the town's houses:
            // UV0 planar metres, UV1 = (layer code, height above the ground, below the top, dirt) or the glass's data
            public readonly List<Vector4> Data = new List<Vector4>();
            public readonly List<int> GlassIndices = new List<int>();
            // the building being emitted: faces in its wall colour are plaster, in its roof colour tiles / concrete, the rest plain
            public bool Bld; public Color WallCol, RoofCol; public bool Pitched; public float GroundY, TopY = 1e4f, Dirt = .2f;
            // set while a glass quad is emitted: its corners (game frame) and the window's random number
            public bool GlassOn; Vector3 ga, gb, gd; float grnd;
            static readonly Color RoofMean = new Color(.392f, .0976f, .0497f); // #A9573F linear: the tile layer's mean
            public Geometry(Vector3 anchor) { Anchor = anchor; }
            public void Tri(Vector3 a, Vector3 b, Vector3 c, Color color, Vector3 hint)
            {
                Vector3 n = Vector3.Cross(b-a, c-a);
                if (n.magnitude < 1e-9f) return;
                n.Normalize();
                if (Vector3.Dot(n, hint) < 0) { Vector3 tmp = b; b = c; c = tmp; n = -n; }
                // First finish the GAME-frame triangle, then reflect positions/normals and reverse winding.
                int start = Positions.Count;
                Vector3 u, v;
                if (Mathf.Abs(n.y) > .9f) { u = Vector3.right; v = Vector3.forward; }
                else { u = Vector3.Cross(Vector3.up, n).normalized; v = Vector3.Cross(n, u).normalized; }
                int layer = BuildingTextures.Plain; Color tint = color;
                if (Bld && color == WallCol) layer = BuildingTextures.Plaster;
                else if (Bld && color == RoofCol)
                {
                    bool tiles = Pitched && color.r > 2.5f * color.g;
                    layer = tiles ? BuildingTextures.Roof : BuildingTextures.Concrete;
                    if (tiles) tint = new Color(color.r / RoofMean.r, color.g / RoofMean.g, color.b / RoofMean.b) * .8f;
                }
                if (layer == BuildingTextures.Plain) tint = color / .915f; // the plain layer's grey
                else if (layer != BuildingTextures.Roof) tint = Look.Desat(color, .35f) / .86f * .92f; // plaster / concrete: faded, a little darker
                foreach (Vector3 p in new[] { a, b, c })
                {
                    Positions.Add(Reflect(p - Anchor)); Normals.Add(Reflect(n)); Colors.Add(new Color(tint.r, tint.g, tint.b, 0));
                    Vector3 q = p - Anchor;
                    if (GlassOn)
                    {
                        float gx = Mathf.Round(Vector3.Dot(p - ga, (gb - ga).normalized) / Mathf.Max(.01f, (gb - ga).magnitude)), gy = Mathf.Round((p.y - ga.y) / Mathf.Max(.01f, gd.y - ga.y));
                        UVs.Add(new Vector2(Mathf.Lerp(.2f, .8f, gx), Mathf.Lerp(.28f, .75f, gy))); Data.Add(new Vector4(grnd, 0, gx, gy));
                    }
                    else { UVs.Add(new Vector2(Vector3.Dot(q, u), Vector3.Dot(q, v))); Data.Add(new Vector4(layer * 9, p.y - GroundY, TopY - p.y, Dirt)); }
                }
                var list = GlassOn ? GlassIndices : Indices;
                list.Add(start); list.Add(start+2); list.Add(start+1);
            }
            public void Glass(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 hint, float rnd)
            { GlassOn = true; ga = a; gb = b; gd = d; grnd = rnd; Quad(a, b, c, d, Color.white, hint); GlassOn = false; }
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color, Vector3 hint)
            { Tri(a,b,c,color,hint); Tri(a,c,d,color,hint); }
            public void Box(Vector3 c, Vector3 x, Vector3 y, Vector3 z, Color color)
            {
                Vector3[] axes = { x,y,z };
                for (int i=0; i<3; i++) foreach (int sign in new[] {1,-1})
                {
                    Vector3 a = axes[i]*sign, b = axes[(i+1)%3], q = axes[(i+2)%3], f = c+a;
                    Quad(f+b+q, f+b-q, f-b-q, f-b+q, color, a);
                }
            }
        }

        sealed class Side
        {
            public Vector3 Center, Normal, U;
            public float Width;
            public Vector3 At(float u, float y) => Center + U*u + Vector3.up*y;
        }
        sealed class Recipe
        {
            readonly Dictionary<string, object> o;
            readonly Geometry g;
            readonly string kind;
            readonly Vector3 t, n;
            readonly float x, z, w, l, y0, low, offset;
            readonly Color col;
            public Recipe(Dictionary<string, object> obj, Func<float,float,float> ground, Geometry geometry)
            {
                o=obj; g=geometry; kind=Kind(o);
                x=Num(o,"x"); z=Num(o,"z"); float a=Num(o,"a");
                t=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)); n=new Vector3(-t.z,0,t.x);
                w=NonZero(o,"w",2); l=NonZero(o,"l",2); offset=Num(o,"y0");
                var heights = new List<float>();
                Vector2[] points = kind == "sign" ? new[] { Vector2.zero } : new[] {
                    Vector2.zero, new Vector2(-w/2,-l/2), new Vector2(w/2,-l/2), new Vector2(w/2,l/2), new Vector2(-w/2,l/2) };
                foreach (var p in points)
                {
                    Vector3 q=new Vector3(x,0,z)+n*p.x+t*p.y;
                    float h=ground(q.x,q.z);
                    if (!Finite(h)) throw new InvalidOperationException("Ground sampler returned a non-finite height.");
                    heights.Add(h);
                }
                float center=heights[0]; heights.Sort();
                float min=heights[0], max=heights[heights.Count-1];
                float baseY=kind=="canopy" ? center : max-min < 1.2f ? max : heights[heights.Count/2];
                y0=baseY+offset;
                low=offset>0 ? 0 : min-baseY-.35f; // Exact TS rule, including suspended y0 objects.
                col=Tint(o,"col",BuildingColor);
            }
            Vector3 At(float u,float v,float y) => new Vector3(x,y0+y,z)+n*u+t*v;
            public void Build()
            {
                g.GroundY=y0; g.TopY=1e4f;
                if (kind=="sign") { Sign(); return; }
                if (kind=="canopy") { Canopy(); return; }
                // TS treats every remaining kind as a box building (there are only bld/canopy/sign in this file).
                Building();
            }
            void Sign()
            {
                float bh=NonZero(o,"h",1.5f), ph=Num(o,"ph",4);
                Color bc=Tint(o,"col",Srgb(.9f,.9f,.9f)); int count=w>2.4f?2:1;
                for (int i=0;i<count;i++)
                {
                    float u=count==1?0:(i==0?-1:1)*(w/2-.2f);
                    if (ph>.05f) g.Box(At(u,0,(ph+low)/2),n*.09f,Vector3.up*((ph-low)/2),t*.09f,Steel);
                }
                float bottom=ph>.05f?ph:low;
                g.Box(At(0,0,(bottom+ph+bh)/2),n*(w/2),Vector3.up*((ph+bh-bottom)/2),t*.12f,bc);
                // TextQuad glyphs use a browser canvas atlas in the source; this mesh import keeps the sign boards.
            }
            static List<Vector2> Points(object value)
            {
                var result=new List<Vector2>();
                if (value is List<object> list)
                    foreach (object item in list)
                        if (item is List<object> p && p.Count>=2)
                            result.Add(new Vector2(Convert.ToSingle(p[0],CultureInfo.InvariantCulture),Convert.ToSingle(p[1],CultureInfo.InvariantCulture)));
                return result;
            }
            void Canopy()
            {
                float h=NonZero(o,"h",4.6f), th=NonZero(o,"th",.8f);
                Color uc=Tint(o,"ucol",Srgb(.95f,.95f,.95f)), cc=Tint(o,"ccol",Srgb(.85f,.85f,.85f));
                g.Box(At(0,0,h+th/2),n*(w/2),Vector3.up*(th/2),t*(l/2),Tint(o,"col",Srgb(.92f,.92f,.92f)));
                g.Quad(At(-w/2,-l/2,h-.01f),At(w/2,-l/2,h-.01f),At(w/2,l/2,h-.01f),At(-w/2,l/2,h-.01f),uc,Vector3.down);
                bool along=l>=w;
                List<Vector2> columns=Points(Get(o,"cols"));
                if (columns.Count==0)
                {
                    int count=(int)Num(o,"nc");
                    if (count==0) count=Mathf.FloorToInt(Mathf.Max(w,l)/9+.5f);
                    count=Mathf.Max(1,count);
                    for (int i=0;i<count;i++)
                    {
                        float q=count==1?0:-.5f+(float)i/(count-1);
                        columns.Add(along?new Vector2(0,q*(l-5)):new Vector2(q*(w-5),0));
                    }
                }
                foreach (var p in columns) g.Box(At(p.x,p.y,(h+low)/2),n*.2f,Vector3.up*((h-low)/2),t*.2f,cc);
                List<Vector2> pumps;
                if (Get(o,"pumps") is List<object>) pumps=Points(Get(o,"pumps"));
                else
                {
                    int count=(int)Num(o,"pumps",columns.Count);
                    if (count<0) count=Mathf.Max(0,columns.Count+count); // JS slice(0, negative).
                    pumps=columns.GetRange(0,Mathf.Min(count,columns.Count));
                }
                Color pc=Tint(o,"pcol",Srgb(.8f,.8f,.82f)); Vector3 a=along?t:n, b=along?n:t;
                foreach (var p in pumps)
                {
                    g.Box(At(p.x,p.y,.08f+low/2),b*.55f,Vector3.up*(.08f-low/2),a*2.6f,Kerb);
                    // the dispensers: Tripo's aparat-gorivo (PropsBuilder) on the island instead of two boxes
                    if (TripoPumps) { PumpSpots.Add((Str(o,"id"),At(p.x,p.y,.16f),a)); continue; }
                    foreach (float d in new[] {-1.4f,1.4f})
                        g.Box(At(p.x,p.y,1)+a*d,b*.28f,Vector3.up*.85f,a*.45f,pc);
                }
            }
            Dictionary<string,Side> Sides()
            {
                var sides=new Dictionary<string,Side> {
                    {"F",new Side {Center=At(0,l/2,0),Normal=t,Width=w}},
                    {"B",new Side {Center=At(0,-l/2,0),Normal=-t,Width=w}},
                    {"P",new Side {Center=At(w/2,0,0),Normal=n,Width=l}},
                    {"M",new Side {Center=At(-w/2,0,0),Normal=-n,Width=l}} };
                foreach (var s in sides.Values) s.U=Vector3.Cross(-s.Normal,Vector3.up);
                return sides;
            }
            void Rect(Side s,float u0,float u1,float ya,float yb,Color c,float off=.03f)
            {
                Vector3 d=s.Normal*off;
                g.Quad(s.At(u0,ya)+d,s.At(u1,ya)+d,s.At(u1,yb)+d,s.At(u0,yb)+d,c,s.Normal);
            }
            void Building()
            {
                float h=NonZero(o,"h",3.2f); string roof=Str(o,"roof","flat");
                float rh=roof=="flat"?0:NonZero(o,"rh",1.6f);
                Color rc=Tint(o,"rcol",Srgb(.62f,.3f,.22f)); var sides=Sides();
                g.Bld=true; g.WallCol=col; g.RoofCol=rc; g.Pitched=roof!="flat"; g.GroundY=y0; g.TopY=y0+h; g.Dirt=.3f;
                try { BuildingBody(h,roof,rh,rc,sides); } finally { g.Bld=false; g.GroundY=y0; g.TopY=1e4f; g.Dirt=.2f; }
            }
            void BuildingBody(float h,string roof,float rh,Color rc,Dictionary<string,Side> sides)
            {
                foreach (Side s in sides.Values) Rect(s,-s.Width/2,s.Width/2,low,h,col,0);
                foreach (var b in Records(o,"band")) foreach (var pair in sides)
                {
                    string selection=Str(b,"side"); if (selection.Length>0 && !selection.Contains(pair.Key)) continue;
                    Side s=pair.Value; float y=Num(b,"y");
                    Rect(s,-s.Width/2,s.Width/2,y==0 && offset==0?low:y,y+NonZero(b,"h",.6f),Tint(b,"col",Srgb(.3f,.3f,.3f)),.02f);
                }
                foreach (var wn in Records(o,"win")) foreach (char key in Str(wn,"side","F"))
                {
                    if (!sides.TryGetValue(key.ToString(),out Side s)) continue;
                    int count=Mathf.Max(1,(int)Num(wn,"n",1)), rows=Mathf.Max(1,(int)Num(wn,"rows",1));
                    float ww=NonZero(wn,"w",1.2f), wh=NonZero(wn,"h",1.3f), yb=Num(wn,"y",1), dy=NonZero(wn,"dy",2.9f);
                    float u0=Num(wn,"u0",-s.Width/2), u1=Num(wn,"u1",s.Width/2);
                    Color cl=Tint(wn,"col",Srgb(.2f,.26f,.32f));
                    for (int r=0;r<rows;r++) for (int i=0;i<count;i++)
                    {
                        float u=u0+(i+.5f)*(u1-u0)/count, y=yb+r*dy;
                        Rect(s,u-ww/2-.06f,u+ww/2+.06f,Mathf.Max(low,y-.06f),y+wh+.06f,Mix(col,.72f),.025f);
                        // glass with a room behind it (the package's colour only told light / dark glass)
                        Vector3 gd=s.Normal*.035f;
                        g.Glass(s.At(u-ww/2,y)+gd,s.At(u+ww/2,y)+gd,s.At(u+ww/2,y+wh)+gd,s.At(u-ww/2,y+wh)+gd,s.Normal,Mathf.Repeat(Mathf.Sin(u*12.9f+y*78.2f+x*.37f+z*.71f)*43758.5f,1));
                    }
                }
                foreach (var sg in Records(o,"signs"))
                {
                    if (!sides.TryGetValue(Str(sg,"side","F"),out Side s)) continue;
                    float lh=NonZero(sg,"h",.6f), tw=Mathf.Min(NonZero(sg,"w",Str(sg,"s").Length*lh*.75f),s.Width*.95f);
                    float y=Num(sg,"y",h-lh), u=Num(sg,"u");
                    if (Str(sg,"col").Length>0) Rect(s,u-tw/2-.25f,u+tw/2+.25f,y-lh*.85f,y+lh*.85f,Tint(sg,"col",Color.white),.04f);
                }
                float ov=roof=="flat"?0:.35f;
                Vector3[] e={At(-w/2-ov,-l/2-ov,h),At(w/2+ov,-l/2-ov,h),At(w/2+ov,l/2+ov,h),At(-w/2-ov,l/2+ov,h)};
                if (roof=="flat")
                {
                    g.Quad(e[0],e[1],e[2],e[3],rc,Vector3.up);
                    if (offset==0) foreach (var s in sides.Values)
                        g.Box(s.At(0,h+.2f)-s.Normal*.1f,s.U*(s.Width/2),Vector3.up*.2f,s.Normal*.1f,col);
                    return;
                }
                bool along=Str(o,"ridge").Length>0?Str(o,"ridge")=="l":l>=w;
                float half=along?w/2:l/2, cut=roof=="hip"?Mathf.Min(half,(along?l:w)/2-.2f):-ov;
                Vector3 r0=along?At(0,-l/2+cut,h+rh):At(-w/2+cut,0,h+rh);
                Vector3 r1=along?At(0,l/2-cut,h+rh):At(w/2-cut,0,h+rh);
                if (roof=="shed")
                {
                    if (along)
                    {
                        g.Quad(e[0],At(w/2+ov,-l/2-ov,h+rh),At(w/2+ov,l/2+ov,h+rh),e[3],rc,Vector3.up);
                        g.Quad(At(w/2,-l/2,h),At(w/2,l/2,h),At(w/2,l/2,h+rh),At(w/2,-l/2,h+rh),col,n);
                        g.Tri(At(-w/2,-l/2,h),At(w/2,-l/2,h),At(w/2,-l/2,h+rh),col,-t);
                        g.Tri(At(-w/2,l/2,h),At(w/2,l/2,h),At(w/2,l/2,h+rh),col,t);
                    }
                    else
                    {
                        g.Quad(e[0],e[1],At(w/2+ov,l/2+ov,h+rh),At(-w/2-ov,l/2+ov,h+rh),rc,Vector3.up);
                        g.Quad(At(-w/2,l/2,h),At(w/2,l/2,h),At(w/2,l/2,h+rh),At(-w/2,l/2,h+rh),col,t);
                        g.Tri(At(w/2,-l/2,h),At(w/2,l/2,h),At(w/2,l/2,h+rh),col,n);
                        g.Tri(At(-w/2,-l/2,h),At(-w/2,l/2,h),At(-w/2,l/2,h+rh),col,-n);
                    }
                }
                else if (along)
                {
                    g.Quad(e[0],r0,r1,e[3],rc,Vector3.up); g.Quad(e[1],e[2],r1,r0,rc,Vector3.up);
                    if (roof=="hip") {g.Tri(e[0],e[1],r0,rc,Vector3.up);g.Tri(e[3],e[2],r1,rc,Vector3.up);}
                    else
                    {
                        g.Tri(At(-w/2,-l/2,h),At(w/2,-l/2,h),At(0,-l/2,h+rh),col,-t);
                        g.Tri(At(-w/2,l/2,h),At(w/2,l/2,h),At(0,l/2,h+rh),col,t);
                    }
                }
                else
                {
                    g.Quad(e[0],e[1],r1,r0,rc,Vector3.up);g.Quad(e[3],r0,r1,e[2],rc,Vector3.up);
                    if (roof=="hip") {g.Tri(e[0],e[3],r0,rc,Vector3.up);g.Tri(e[1],e[2],r1,rc,Vector3.up);}
                    else
                    {
                        g.Tri(At(-w/2,-l/2,h),At(-w/2,l/2,h),At(-w/2,0,h+rh),col,-n);
                        g.Tri(At(w/2,-l/2,h),At(w/2,l/2,h),At(w/2,0,h+rh),col,n);
                    }
                }
                g.Quad(e[0],e[1],e[2],e[3],Mix(col,.8f),Vector3.down);
            }
        }

        /// <summary>Small strict JSON reader: dictionaries, lists, strings, doubles, booleans and null.</summary>
        static class MiniJson
        {
            public static object Parse(string json)
            {
                var p=new Parser(json); object value=p.Value(0); p.White();
                if (!p.End) throw p.Error("Trailing content");
                return value;
            }
            sealed class Parser
            {
                readonly string text; int pos;
                public Parser(string s) {text=s??throw new ArgumentNullException(nameof(s));}
                public bool End => pos>=text.Length;
                public FormatException Error(string message) => new FormatException(message+" at JSON offset "+pos+".");
                public void White() {while (!End && (text[pos]==' ' || text[pos]=='\t' || text[pos]=='\r' || text[pos]=='\n')) pos++;}
                bool Take(char c) {White(); if (!End && text[pos]==c) {pos++;return true;} return false;}
                void Need(char c) {if (!Take(c)) throw Error("Expected '"+c+"'");}
                public object Value(int depth)
                {
                    if (depth>128) throw Error("JSON nesting too deep");
                    White(); if (End) throw Error("Unexpected end"); char c=text[pos];
                    if (c=='{')
                    {
                        pos++;var d=new Dictionary<string,object>(); if (Take('}')) return d;
                        do {White();if (End || text[pos]!='"') throw Error("Expected object key");string k=String();Need(':');d[k]=Value(depth+1);if (Take('}')) return d;Need(',');} while (true);
                    }
                    if (c=='[')
                    {
                        pos++;var list=new List<object>();if (Take(']')) return list;
                        do {list.Add(Value(depth+1));if (Take(']')) return list;Need(',');} while (true);
                    }
                    if (c=='"') return String();
                    if (c=='t') {Literal("true");return true;}
                    if (c=='f') {Literal("false");return false;}
                    if (c=='n') {Literal("null");return null;}
                    return Number();
                }
                void Literal(string s)
                {if (pos+s.Length>text.Length || string.CompareOrdinal(text,pos,s,0,s.Length)!=0) throw Error("Invalid literal");pos+=s.Length;}
                string String()
                {
                    Need('"');var b=new StringBuilder();
                    while (!End)
                    {
                        char c=text[pos++];if (c=='"') return b.ToString();
                        if (c<32) throw Error("Control character in string");
                        if (c!='\\') {b.Append(c);continue;}
                        if (End) throw Error("Unfinished escape");c=text[pos++];
                        switch (c)
                        {
                            case '"':case '\\':case '/':b.Append(c);break;
                            case 'b':b.Append('\b');break;case 'f':b.Append('\f');break;
                            case 'n':b.Append('\n');break;case 'r':b.Append('\r');break;case 't':b.Append('\t');break;
                            case 'u':
                                if (pos+4>text.Length || !ushort.TryParse(text.Substring(pos,4),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out ushort u)) throw Error("Invalid Unicode escape");
                                b.Append((char)u);pos+=4;break;
                            default:throw Error("Invalid escape");
                        }
                    }
                    throw Error("Unterminated string");
                }
                bool Digit => !End && text[pos]>='0' && text[pos]<='9';
                void Digits() {if (!Digit) throw Error("Expected digit");while (Digit) pos++;}
                double Number()
                {
                    int start=pos;
                    if (!End && text[pos]=='-') pos++;
                    if (!End && text[pos]=='0') pos++;else Digits();
                    if (!End && text[pos]=='.') {pos++;Digits();}
                    if (!End && (text[pos]=='e' || text[pos]=='E'))
                    {pos++;if (!End && (text[pos]=='+' || text[pos]=='-')) pos++;Digits();}
                    if (!double.TryParse(text.Substring(start,pos-start),NumberStyles.Float,CultureInfo.InvariantCulture,out double v) || double.IsInfinity(v) || double.IsNaN(v)) throw Error("Invalid number");
                    return v;
                }
            }
        }
    }
}
#endif
