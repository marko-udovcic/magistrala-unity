// World streaming, phase 1 (2026-11-24): the static world grouped per L0 cell (CellGrid, 512 m) and merged per cell per material.
// Build-time pass on a COPY of the main scene (Assets/Generated/Cells/Magistrala_cells.unity): Assets/Scenes/Magistrala.unity and
// every world menu stay as they are (the builders keep probing the unmerged colliders by name / type), ProbeBuild bakes and
// builds the copy. Menu Magistrala/Bake cells (also run by ProbeBuild.Mac / Win unless ProbeBuild.Plain).
//
// Per root World/{Buildings, Roads, Tlo, Streets, Signs, Riva}:
//  - renderers: merged per (L0 cell, 256 m block inside it, material, renderer settings, vertex layout) into World/<root>/c_x_z/r…;
//    the 256 m block keeps frustum / shadow culling about as fine as the builders' own 256 m chunks. NOT merged (kept as they are,
//    only moved under their cell): renderers of a LODGroup (the buildings' near / mid / far levels switch per 64 / 256 m cell),
//    ad_* faces (Billboards finds them by name), objects with scripts, transparent / object-space shaders (Foliage, ParkedCar, …),
//    inactive or disabled objects, mirrored transforms.
//  - colliders: every enabled non-trigger BoxCollider / non-convex MeshCollider becomes part of ONE MeshCollider per
//    (L0 cell, physics material, layer, cooking options) at World/<root>/c_x_z/col…: the 7,200 guard-rail boxes, 570 sign posts,
//    street barriers, court fences, road / street / riva surfaces, building hulls. Same geometry, same physics material.
//  - a LODGroup whose LOD0 is one child renderer at identity (Build tile buildings' c#/near, k#/far) gets that renderer on itself:
//    one GameObject less per building cell, identical drawing (LOD distances, reference point and size kept).
//  - objects left with nothing but a Transform are deleted (unless a script field references them).
// Assets/Generated/Cells/Meshes/<root>_<x>_<z>.asset (one file per root and cell), manifest.json (cells, bounds, objects, counts).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Magistrala.Streaming;

namespace Magistrala.EditorTools
{
    public static class CellMerge
    {
        public const string DIR = CellGrid.GeneratedRoot;
        public const string MESHES = DIR + "/Meshes";
        public const string SCENE = DIR + "/Magistrala_cells.unity";
        public const string MANIFEST = DIR + "/manifest.json";
        public static readonly string[] ROOTS = { "Buildings", "Roads", "Tlo", "Streets", "Signs", "Riva" };
        const float SUB = 256;
        /// shaders that only use object space to get to world space (merging moves the vertices into the cell's space)
        static readonly HashSet<string> MergeShaders = new HashSet<string> {
            "Magistrala/Asphalt", "Magistrala/Facade", "Magistrala/VertexColorLit", "Magistrala/WindowInterior",
            "Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit", "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Baked Lit" };

        [MenuItem("Magistrala/Bake cells (merge + cell scenes)")]
        public static void BakeMenu() => Debug.Log(Bake());

        /// the main scene (saved) -> its merged copy SCENE + manifest; the main scene is open again afterwards
        public static string Bake()
        {
            MainScene.Ensure();
            MainScene.EnsureSaved();
            var t0 = DateTime.Now;
            Dir(DIR);
            if (AssetDatabase.IsValidFolder(MESHES)) AssetDatabase.DeleteAsset(MESHES);
            Dir(MESHES);
            var main = SceneManager.GetActiveScene();
            if (!EditorSceneManager.SaveScene(main, SCENE, true)) throw new Exception("[cells] could not copy the scene to " + SCENE);
            var scene = EditorSceneManager.OpenScene(SCENE, OpenSceneMode.Single);
            string res;
            try
            {
                var st = Merge(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                res = st;
                if (Split)
                {
                    var paths = SplitScenes(scene);
                    res += $" | split: {paths.Count} cell scenes + {STREAMSCENE}";
                }
                else WriteRuntimeManifest(null);
                res += $" | {(DateTime.Now - t0).TotalSeconds:F0} s";
            }
            finally { EditorSceneManager.OpenScene(MainScene.PATH, OpenSceneMode.Single); }
            AssetDatabase.SaveAssets();
            return "[cells] " + res;
        }

        /// the scenes of a player build after Bake(): the merged copy (phase 3: the streaming scene first, then every cell scene)
        public static string[] BuildScenes()
        {
            if (!Split) return new[] { SCENE };
            var l = new List<string> { STREAMSCENE };
            l.AddRange(Directory.GetFiles(CELLSCENES, "*.unity").Select(f => f.Replace('\\', '/')).OrderBy(f => f));
            return l.ToArray();
        }

        /// phase 3: also split the merged copy into the streaming scene + one additive scene per L0 cell (ProbeBuild.MacStream)
        public static bool Split = false;
        public const string CELLSCENES = DIR + "/Scenes";
        public const string STREAMSCENE = DIR + "/Magistrala_stream.unity";
        public const string RUNTIME_MANIFEST = DIR + "/Resources/Cells/manifest.json";
        static Dictionary<Vector2Int, CellStat> lastCells;

        /// what of a cell may leave the always-loaded scene: drawn only near (LOD groups culled at their last level, signs, ground
        /// details, every collider); far-visible things (road / street / riva surfaces, ad boards, buildings' far / box levels) stay
        static bool Streamable(string root, Transform t)
        {
            if (root == "Signs" || root == "Tlo") return true;
            if (t.GetComponent<MeshCollider>() && !t.GetComponent<Renderer>()) return true;
            if (root != "Buildings") return false;
            var lg = t.GetComponent<LODGroup>();
            if (lg) { var lods = lg.GetLODs(); return lods.Length > 0 && lods[lods.Length - 1].screenRelativeTransitionHeight > 0; }
            var r = t.GetComponent<Renderer>();
            return r && r.shadowCastingMode == ShadowCastingMode.ShadowsOnly; // the shadow hulls (shadow distance 230 m)
        }

        /// World/<root>/c_x_z children -> Scenes/c_x_z.unity (one root "c_x_z", saved inactive, children <root>/c_x_z at the same
        /// world place), the rest -> STREAMSCENE; Resources/Cells/manifest.json for StreamingScheduler
        static List<string> SplitScenes(Scene merged)
        {
            Dir(CELLSCENES); Dir(DIR + "/Resources/Cells");
            foreach (var f in Directory.GetFiles(CELLSCENES, "*.unity")) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            var world = merged.GetRootGameObjects().First(g => g.name == "World");
            var byCell = new Dictionary<Vector2Int, List<(string root, Transform t)>>();
            foreach (var rootName in ROOTS)
            {
                var R = world.transform.Find(rootName); if (!R) continue;
                foreach (Transform cellT in R)
                {
                    var p = cellT.name.Split('_'); if (p.Length != 3 || p[0] != "c" || !int.TryParse(p[1], out int cx) || !int.TryParse(p[2], out int cz)) continue;
                    var c = new Vector2Int(cx, cz);
                    foreach (Transform ch in cellT)
                        if (Streamable(rootName, ch))
                        { if (!byCell.TryGetValue(c, out var l)) byCell[c] = l = new List<(string, Transform)>(); l.Add((rootName, ch)); }
                }
            }
            var paths = new List<string>(); var sceneOf = new Dictionary<Vector2Int, string>();
            var streamed = new Dictionary<Vector2Int, int[]>(); // renderers, triangles
            foreach (var kv in byCell.OrderBy(k => k.Key.x).ThenBy(k => k.Key.y))
            {
                var s = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var top = new GameObject(CellGrid.Key(kv.Key)); top.isStatic = true; SceneManager.MoveGameObjectToScene(top, s);
                var st = streamed[kv.Key] = new int[2];
                foreach (var (rootName, t) in kv.Value)
                {
                    var rt = top.transform.Find(rootName);
                    if (!rt) { var g = new GameObject(rootName); g.isStatic = true; g.transform.SetParent(top.transform, false); rt = g.transform; }
                    t.SetParent(null, true); SceneManager.MoveGameObjectToScene(t.gameObject, s); t.SetParent(rt, true);
                    st[0] += t.GetComponentsInChildren<MeshRenderer>(true).Length;
                    foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) st[1] += Tris(mf.sharedMesh);
                }
                top.SetActive(false); // StreamingScheduler shows it (roots must be saved inactive)
                string path = $"{CELLSCENES}/{CellGrid.Key(kv.Key)}.unity";
                EditorSceneManager.SaveScene(s, path); EditorSceneManager.CloseScene(s, true);
                paths.Add(path); sceneOf[kv.Key] = path;
            }
            EditorSceneManager.SaveScene(merged, STREAMSCENE, true);
            WriteRuntimeManifest(sceneOf, streamed);
            return paths;
        }

        /// the scheduler's manifest (Magistrala.Streaming.CellManifest); without the split: no manifest (scheduler idle)
        static void WriteRuntimeManifest(Dictionary<Vector2Int, string> sceneOf, Dictionary<Vector2Int, int[]> streamed = null)
        {
            if (sceneOf == null) { if (File.Exists(RUNTIME_MANIFEST)) AssetDatabase.DeleteAsset(RUNTIME_MANIFEST); return; }
            var m = new CellManifest { l1 = Array.Empty<L1Cell>() };
            var l0 = new List<L0Cell>(); bool any = false; var wb = new Bounds();
            foreach (var kv in sceneOf.OrderBy(k => k.Key.x).ThenBy(k => k.Key.y))
            {
                var b = CellGrid.CellBounds(kv.Key);
                if (lastCells != null && lastCells.TryGetValue(kv.Key, out var cs) && cs.hasB) { b.SetMinMax(new Vector3(b.min.x, Mathf.Min(b.min.y, cs.b.min.y), b.min.z), new Vector3(b.max.x, Mathf.Max(b.max.y, cs.b.max.y), b.max.z)); }
                var st = streamed != null && streamed.TryGetValue(kv.Key, out var a) ? a : new int[2];
                l0.Add(new L0Cell { key = CellGrid.Key(kv.Key), coordinate = kv.Key, bounds = b, sceneName = kv.Value, rendererCount = st[0], triangleCount = st[1] });
                if (!any) { wb = b; any = true; } else wb.Encapsulate(b);
            }
            m.l0 = l0.ToArray(); m.worldBounds = wb;
            if (!m.Validate(out var err)) throw new Exception("[cells] runtime manifest invalid: " + err);
            File.WriteAllText(RUNTIME_MANIFEST, JsonUtility.ToJson(m, true));
            AssetDatabase.ImportAsset(RUNTIME_MANIFEST);
        }

        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = Path.GetDirectoryName(p).Replace('\\', '/'); Dir(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(p)); } }

        // ------------------------------------------------------------------ grid
        static Vector2Int CellOf(Vector3 world) => CellGrid.Cell(world);
        static Vector3 CellOrigin(Vector2Int c) => new Vector3(c.x * CellGrid.L0, 0, c.y * CellGrid.L0);
        static Vector2Int BlockOf(Vector3 world) => new Vector2Int(Mathf.FloorToInt(world.x / SUB), Mathf.FloorToInt(world.z / SUB));

        // ------------------------------------------------------------------ stats / manifest
        sealed class CellStat
        {
            public Vector2Int c; public Bounds b; public bool hasB;
            public readonly Dictionary<string, int[]> perRoot = new Dictionary<string, int[]>(); // root -> {gameObjects, renderers, triangles, colliders}
            public readonly List<string> objects = new List<string>();
            public void Grow(Bounds x) { if (!hasB) { b = x; hasB = true; } else b.Encapsulate(x); }
            public int[] R(string root) { if (!perRoot.TryGetValue(root, out var a)) perRoot[root] = a = new int[4]; return a; }
        }

        struct RKey : IEquatable<RKey>
        {
            public Vector2Int cell, block; public Material mat; public string settings, layout;
            public bool Equals(RKey o) => cell == o.cell && block == o.block && mat == o.mat && settings == o.settings && layout == o.layout;
            public override bool Equals(object o) => o is RKey k && Equals(k);
            public override int GetHashCode() => (cell.GetHashCode() * 31 + block.GetHashCode()) * 31 + (mat ? mat.GetInstanceID() : 0) ^ settings.GetHashCode() * 7 ^ layout.GetHashCode();
        }
        struct CKey : IEquatable<CKey>
        {
            public Vector2Int cell; public PhysicsMaterial pm; public int layer; public MeshColliderCookingOptions cook; public string masks;
            public bool Equals(CKey o) => cell == o.cell && pm == o.pm && layer == o.layer && cook == o.cook && masks == o.masks;
            public override bool Equals(object o) => o is CKey k && Equals(k);
            public override int GetHashCode() => cell.GetHashCode() * 31 + (pm ? pm.GetInstanceID() : 0) * 7 + layer * 3 + (int)cook ^ masks.GetHashCode();
        }
        sealed class RGroup { public MeshRenderer proto; public StaticEditorFlags flags; public int layer; public readonly List<Material> mats = new List<Material>(); public readonly Dictionary<Material, List<CombineInstance>> parts = new Dictionary<Material, List<CombineInstance>>(); public int verts; }
        sealed class CGroup { public MeshCollider proto; public BoxCollider protoBox; public readonly List<Vector3> v = new List<Vector3>(); public readonly List<int> t = new List<int>(); }

        static string Settings(MeshRenderer r, StaticEditorFlags flags)
            => $"{(int)r.shadowCastingMode}|{r.receiveShadows}|{r.gameObject.layer}|{r.renderingLayerMask}|{(int)r.lightProbeUsage}|{(int)r.reflectionProbeUsage}|{(int)r.motionVectorGenerationMode}|{r.allowOcclusionWhenDynamic}|{r.staticShadowCaster}|{r.rendererPriority}|{(int)flags}|{r.sortingLayerID}|{r.sortingOrder}";

        static string Layout(Mesh m)
        {
            var sb = new StringBuilder();
            foreach (var a in m.GetVertexAttributes()) sb.Append((int)a.attribute).Append(':').Append((int)a.format).Append('x').Append(a.dimension).Append(' ');
            return sb.ToString();
        }

        static bool Mergeable(Material m) => m && m.shader && MergeShaders.Contains(m.shader.name) && m.renderQueue <= 2500 && m.GetTag("RenderType", false, "") != "Transparent"; // (opaque and alpha-tested: no sorting)

        // ------------------------------------------------------------------ the pass
        public static string Merge(Scene scene)
        {
            var world = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "World");
            if (!world) throw new Exception("[cells] no World in " + scene.path);
            var cells = new Dictionary<Vector2Int, CellStat>();
            CellStat Cell(Vector2Int c) { if (!cells.TryGetValue(c, out var s)) cells[c] = s = new CellStat { c = c }; return s; }
            var referenced = Referenced(scene);
            var sb = new StringBuilder();
            int goBefore = 0, goAfter = 0, rBefore = 0, rAfter = 0, cBefore = 0, cAfter = 0;
            AssetDatabase.StartAssetEditing();
            var meshFiles = new Dictionary<string, List<Mesh>>();
            try
            {
                foreach (var rootName in ROOTS)
                {
                    var R = world.transform.Find(rootName); if (!R) continue;
                    int g0 = R.GetComponentsInChildren<Transform>(true).Length, r0 = R.GetComponentsInChildren<MeshRenderer>(true).Length, c0 = R.GetComponentsInChildren<Collider>(true).Length;
                    goBefore += g0; rBefore += r0; cBefore += c0;

                    // 1. who stays as it is
                    var lodR = new HashSet<Renderer>();
                    foreach (var lg in R.GetComponentsInChildren<LODGroup>(true)) foreach (var l in lg.GetLODs()) foreach (var r in l.renderers) if (r) lodR.Add(r);
                    var rg = new Dictionary<RKey, RGroup>();
                    var mergedR = new List<MeshRenderer>();
                    int nKeptR = 0;
                    foreach (var mr in R.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        var mf = mr.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null;
                        var mats = mr.sharedMaterials;
                        bool keep = !mesh || !mr.enabled || !mr.gameObject.activeInHierarchy || lodR.Contains(mr) || Billboards.FaceName(mr.transform) != null
                            || mr.GetComponent<MonoBehaviour>() || mr.probeAnchor || mr.lightmapIndex >= 0 && mr.lightmapIndex < 65534 || mr.additionalVertexStreams
                            || mr.transform.localToWorldMatrix.determinant <= 0 || mats.Length == 0 || mats.Any(m => !Mergeable(m)) || referenced.Contains(mr.gameObject) || referenced.Contains(mr.transform);
                        if (keep) { nKeptR++; continue; }
                        var flags = GameObjectUtility.GetStaticEditorFlags(mr.gameObject);
                        var centre = mr.bounds.center; var cell = CellOf(centre);
                        var toCell = Matrix4x4.Translate(-CellOrigin(cell)) * mr.transform.localToWorldMatrix;
                        string set = Settings(mr, flags), lay = Layout(mesh);
                        for (int i = 0; i < mats.Length; i++)
                        {
                            int sub = Mathf.Min(i, mesh.subMeshCount - 1);
                            // one renderer per (cell, block, settings), one submesh per material (as many draws as before, fewer renderers)
                            var key = new RKey { cell = cell, block = BlockOf(centre), mat = null, settings = set, layout = lay };
                            if (!rg.TryGetValue(key, out var grp)) rg[key] = grp = new RGroup { proto = mr, flags = flags, layer = mr.gameObject.layer };
                            if (!grp.parts.TryGetValue(mats[i], out var pl)) { grp.parts[mats[i]] = pl = new List<CombineInstance>(); grp.mats.Add(mats[i]); }
                            pl.Add(new CombineInstance { mesh = mesh, subMeshIndex = sub, transform = toCell });
                            grp.verts += mesh.vertexCount; // (conservative: the index format must hold whatever CombineMeshes copies)
                        }
                        mergedR.Add(mr);
                    }

                    // 2. colliders
                    var cg = new Dictionary<CKey, CGroup>();
                    var mergedC = new List<Collider>();
                    foreach (var c in R.GetComponentsInChildren<Collider>(true))
                    {
                        if (!c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger || c.attachedRigidbody || c.GetComponent<MonoBehaviour>() || referenced.Contains(c.gameObject)) continue;
                        var mc = c as MeshCollider; var bc = c as BoxCollider;
                        if (mc && (mc.convex || !mc.sharedMesh)) continue;
                        if (!mc && !bc) continue;
                        var cell = CellOf(c.bounds.center);
                        var key = new CKey { cell = cell, pm = c.sharedMaterial, layer = c.gameObject.layer, cook = mc ? mc.cookingOptions : (MeshColliderCookingOptions)(-1), masks = $"{c.includeLayers.value}|{c.excludeLayers.value}|{c.layerOverridePriority}|{c.providesContacts}|{c.contactOffset:F4}" };
                        // boxes and meshes of one material share a collider (cooking options: the meshes' own, boxes take the default)
                        if (bc) { key.cook = DefaultCook; }
                        if (!cg.TryGetValue(key, out var grp)) cg[key] = grp = new CGroup();
                        if (mc && !grp.proto) grp.proto = mc; if (bc && !grp.protoBox) grp.protoBox = bc;
                        var m = Matrix4x4.Translate(-CellOrigin(cell)) * c.transform.localToWorldMatrix;
                        bool flip = m.determinant < 0;
                        if (bc) AddBox(grp, m, bc.center, bc.size);
                        else
                        {
                            var src = mc.sharedMesh; var sv = src.vertices; int b0 = grp.v.Count;
                            foreach (var p in sv) grp.v.Add(m.MultiplyPoint3x4(p));
                            for (int s = 0; s < src.subMeshCount; s++)
                            {
                                if (src.GetTopology(s) != MeshTopology.Triangles) continue;
                                var tri = src.GetTriangles(s);
                                for (int i = 0; i + 2 < tri.Length; i += 3)
                                {
                                    grp.t.Add(b0 + tri[i]);
                                    if (flip) { grp.t.Add(b0 + tri[i + 2]); grp.t.Add(b0 + tri[i + 1]); } else { grp.t.Add(b0 + tri[i + 1]); grp.t.Add(b0 + tri[i + 2]); }
                                }
                            }
                        }
                        mergedC.Add(c);
                    }

                    // 3. the merged objects
                    var cellGos = new Dictionary<Vector2Int, Transform>();
                    Transform CellGo(Vector2Int c)
                    {
                        if (cellGos.TryGetValue(c, out var t)) return t;
                        var go = new GameObject(CellGrid.Key(c)); go.isStatic = true;
                        go.transform.SetParent(R, false); go.transform.position = CellOrigin(c);
                        Cell(c).objects.Add($"World/{rootName}/{go.name}");
                        return cellGos[c] = go.transform;
                    }
                    var files = new Dictionary<Vector2Int, List<Mesh>>();
                    int ri = 0;
                    int mergedVerts = 0, srcVerts = 0; foreach (var x in mergedR) srcVerts += x.GetComponent<MeshFilter>().sharedMesh.vertexCount;
                    foreach (var kv in rg.OrderBy(k => k.Key.cell.x).ThenBy(k => k.Key.cell.y).ThenBy(k => k.Key.block.x).ThenBy(k => k.Key.block.y).ThenBy(k => k.Key.settings))
                    {
                        var k = kv.Key; var grp = kv.Value;
                        var subs = new List<CombineInstance>(); var tmp = new List<Mesh>();
                        foreach (var mat in grp.mats)
                        {
                            var pl = grp.parts[mat]; int pv = 0; foreach (var ci0 in pl) pv += ci0.mesh.vertexCount;
                            var sm = new Mesh { indexFormat = pv > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                            sm.CombineMeshes(pl.ToArray(), true, true, false); tmp.Add(sm);
                            subs.Add(new CombineInstance { mesh = sm, subMeshIndex = 0, transform = Matrix4x4.identity });
                        }
                        int tv = 0; foreach (var m0 in tmp) tv += m0.vertexCount;
                        var mesh = new Mesh { name = $"{rootName}_{k.cell.x}_{k.cell.y}_{ri++}", indexFormat = tv > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                        mesh.CombineMeshes(subs.ToArray(), false, false, false);
                        foreach (var m0 in tmp) UnityEngine.Object.DestroyImmediate(m0);
                        mesh.RecalculateBounds(); mergedVerts += mesh.vertexCount;
                        if (!files.TryGetValue(k.cell, out var fl)) files[k.cell] = fl = new List<Mesh>(); fl.Add(mesh);
                        var go = new GameObject($"r_{k.block.x}_{k.block.y}_{grp.mats[0].name}{(grp.mats.Count > 1 ? "+" + (grp.mats.Count - 1) : "")}");
                        go.layer = grp.layer;
                        go.transform.SetParent(CellGo(k.cell), false);
                        GameObjectUtility.SetStaticEditorFlags(go, grp.flags);
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var mr = go.AddComponent<MeshRenderer>(); CopyRenderer(grp.proto, mr); mr.sharedMaterials = grp.mats.ToArray();
                        var st = Cell(k.cell); st.Grow(mr.bounds); var a = st.R(rootName); a[1]++; a[2] += Tris(mesh);
                    }
                    int ci = 0;
                    foreach (var kv in cg.OrderBy(k => k.Key.cell.x).ThenBy(k => k.Key.cell.y).ThenBy(k => k.Key.pm ? k.Key.pm.name : "").ThenBy(k => k.Key.layer))
                    {
                        var k = kv.Key; var grp = kv.Value;
                        var mesh = new Mesh { name = $"{rootName}_{k.cell.x}_{k.cell.y}_col{ci++}", indexFormat = grp.v.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                        mesh.SetVertices(grp.v); mesh.SetTriangles(grp.t, 0); mesh.RecalculateBounds();
                        if (!files.TryGetValue(k.cell, out var fl)) files[k.cell] = fl = new List<Mesh>(); fl.Add(mesh);
                        var go = new GameObject($"col_{(k.pm ? k.pm.name : "default")}"); go.layer = k.layer; go.isStatic = true;
                        go.transform.SetParent(CellGo(k.cell), false);
                        var mc = go.AddComponent<MeshCollider>();
                        if (grp.proto) { mc.cookingOptions = grp.proto.cookingOptions; mc.includeLayers = grp.proto.includeLayers; mc.excludeLayers = grp.proto.excludeLayers; mc.layerOverridePriority = grp.proto.layerOverridePriority; mc.providesContacts = grp.proto.providesContacts; mc.contactOffset = grp.proto.contactOffset; }
                        else if (grp.protoBox) { mc.cookingOptions = DefaultCook; mc.includeLayers = grp.protoBox.includeLayers; mc.excludeLayers = grp.protoBox.excludeLayers; mc.layerOverridePriority = grp.protoBox.layerOverridePriority; mc.providesContacts = grp.protoBox.providesContacts; mc.contactOffset = grp.protoBox.contactOffset; }
                        mc.sharedMaterial = k.pm; mc.sharedMesh = mesh;
                        var st = Cell(k.cell); st.Grow(mc.bounds); st.R(rootName)[3]++;
                    }
                    foreach (var kv in files)
                    {
                        string path = $"{MESHES}/{rootName}_{kv.Key.x}_{kv.Key.y}.asset";
                        meshFiles[path] = kv.Value;
                    }

                    // 4. the sources go (components first, then the empty objects)
                    foreach (var c in mergedC) UnityEngine.Object.DestroyImmediate(c);
                    foreach (var mr in mergedR) { var mf = mr.GetComponent<MeshFilter>(); UnityEngine.Object.DestroyImmediate(mr); if (mf) UnityEngine.Object.DestroyImmediate(mf); }

                    // 5. LODGroup: LOD0's single child renderer onto the group's own object
                    int collapsed = 0;
                    foreach (var lg in R.GetComponentsInChildren<LODGroup>(true)) if (Collapse(lg, referenced)) collapsed++;

                    // 6. what is left: whole subtrees with something on them move under their cell
                    var moved = new List<Transform>();
                    void Walk(Transform t)
                    {
                        foreach (Transform ch in t.Cast<Transform>().ToList())
                        {
                            if (cellGos.ContainsValue(ch)) continue;
                            if (Atomic(ch)) moved.Add(ch); else Walk(ch);
                        }
                    }
                    Walk(R);
                    int globals = 0;
                    foreach (var t in moved)
                    {
                        if (t.GetComponentInChildren<MonoBehaviour>(true)) { globals++; continue; } // scripts (klima TreeRenderer …): stay where they are
                        var b = SubtreeBounds(t, out bool any); var cell = CellOf(any ? b.center : t.position);
                        t.SetParent(CellGo(cell), true);
                        var st = Cell(cell); if (any) st.Grow(b);
                        var a = st.R(rootName);
                        a[1] += t.GetComponentsInChildren<MeshRenderer>(true).Length;
                        foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh) a[2] += LodTris(mf);
                        a[3] += t.GetComponentsInChildren<Collider>(true).Length;
                    }
                    int removed = RemoveEmpty(R, referenced, cellGos);

                    foreach (var kv in cellGos) Cell(kv.Key).R(rootName)[0] = kv.Value.GetComponentsInChildren<Transform>(true).Length;
                    int g1 = R.GetComponentsInChildren<Transform>(true).Length, r1 = R.GetComponentsInChildren<MeshRenderer>(true).Length, c1 = R.GetComponentsInChildren<Collider>(true).Length;
                    goAfter += g1; rAfter += r1; cAfter += c1;
                    sb.Append($"{rootName}: verts {srcVerts}->{mergedVerts}, GO {g0}->{g1}, renderers {r0}->{r1} ({mergedR.Count} merged into {rg.Count}, {nKeptR} kept), colliders {c0}->{c1} ({mergedC.Count} into {cg.Count}), LOD collapsed {collapsed}, {globals} script objects stay, {removed} empty removed, {cellGos.Count} cells; ");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            // mesh files (one per root and cell)
            foreach (var kv in meshFiles)
            {
                AssetDatabase.CreateAsset(kv.Value[0], kv.Key);
                for (int i = 1; i < kv.Value.Count; i++) AssetDatabase.AddObjectToAsset(kv.Value[i], kv.Key);
            }
            AssetDatabase.SaveAssets();
            WriteManifest(cells, scene.path); lastCells = cells;
            return $"GameObjects {goBefore}->{goAfter}, renderers {rBefore}->{rAfter}, colliders {cBefore}->{cAfter}, {cells.Count} cells | " + sb;
        }

        /// a new MeshCollider's options (m_CookingOptions: 30, every collider of the scene has them)
        const MeshColliderCookingOptions DefaultCook = MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.EnableMeshCleaning | MeshColliderCookingOptions.WeldColocatedVertices | MeshColliderCookingOptions.UseFastMidphase;

        static int Tris(Mesh m) { long n = 0; for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3; return (int)n; }
        static int LodTris(MeshFilter mf) => Tris(mf.sharedMesh);

        static readonly int[] BoxTri = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7, 0, 1, 5, 0, 5, 4, 2, 3, 7, 2, 7, 6, 1, 2, 6, 1, 6, 5, 3, 0, 4, 3, 4, 7 };
        static void AddBox(CGroup g, Matrix4x4 m, Vector3 c, Vector3 s)
        {
            var h = s * .5f; int b0 = g.v.Count;
            var P = new[] { new Vector3(-h.x, -h.y, -h.z), new Vector3(h.x, -h.y, -h.z), new Vector3(h.x, -h.y, h.z), new Vector3(-h.x, -h.y, h.z),
                            new Vector3(-h.x, h.y, -h.z), new Vector3(h.x, h.y, -h.z), new Vector3(h.x, h.y, h.z), new Vector3(-h.x, h.y, h.z) };
            foreach (var p in P) g.v.Add(m.MultiplyPoint3x4(c + p));
            bool flip = m.determinant < 0;
            for (int i = 0; i < BoxTri.Length; i += 3)
            {
                g.t.Add(b0 + BoxTri[i]);
                if (flip) { g.t.Add(b0 + BoxTri[i + 2]); g.t.Add(b0 + BoxTri[i + 1]); } else { g.t.Add(b0 + BoxTri[i + 1]); g.t.Add(b0 + BoxTri[i + 2]); }
            }
        }

        static void CopyRenderer(MeshRenderer s, MeshRenderer d)
        {
            d.shadowCastingMode = s.shadowCastingMode; d.receiveShadows = s.receiveShadows; d.renderingLayerMask = s.renderingLayerMask;
            d.lightProbeUsage = s.lightProbeUsage; d.reflectionProbeUsage = s.reflectionProbeUsage; d.motionVectorGenerationMode = s.motionVectorGenerationMode;
            d.allowOcclusionWhenDynamic = s.allowOcclusionWhenDynamic; d.staticShadowCaster = s.staticShadowCaster; d.rendererPriority = s.rendererPriority;
            d.sortingLayerID = s.sortingLayerID; d.sortingOrder = s.sortingOrder; d.lightmapIndex = s.lightmapIndex; d.realtimeLightmapIndex = s.realtimeLightmapIndex;
            d.receiveGI = s.receiveGI;
        }

        /// a LODGroup without a renderer of its own whose LOD0 is exactly one plain child renderer at identity: that renderer moves onto it
        static bool Collapse(LODGroup lg, HashSet<UnityEngine.Object> referenced)
        {
            if (lg.GetComponent<Renderer>() || lg.GetComponent<Collider>() || lg.GetComponent<MonoBehaviour>()) return false;
            var lods = lg.GetLODs(); if (lods.Length == 0 || lods[0].renderers.Length != 1) return false;
            var r = lods[0].renderers[0] as MeshRenderer; if (!r) return false;
            var t = r.transform;
            if (t.parent != lg.transform || t.childCount > 0 || referenced.Contains(t.gameObject) || referenced.Contains(t)) return false;
            if (t.localPosition != Vector3.zero || t.localRotation != Quaternion.identity || t.localScale != Vector3.one) return false;
            if (t.gameObject.layer != lg.gameObject.layer || !t.gameObject.activeSelf || !r.enabled) return false;
            if (t.GetComponents<Component>().Length != 3) return false; // Transform, MeshFilter, MeshRenderer only
            var mf = t.GetComponent<MeshFilter>(); if (!mf) return false;
            if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != GameObjectUtility.GetStaticEditorFlags(lg.gameObject)) return false;
            var go = lg.gameObject;
            var refPt = lg.localReferencePoint; var size = lg.size; var fade = lg.fadeMode; var anim = lg.animateCrossFading;
            var nmf = go.AddComponent<MeshFilter>(); nmf.sharedMesh = mf.sharedMesh;
            var nmr = go.AddComponent<MeshRenderer>(); CopyRenderer(r, nmr); nmr.sharedMaterials = r.sharedMaterials;
            for (int i = 0; i < lods.Length; i++) lods[i].renderers = lods[i].renderers.Select(x => x == r ? (Renderer)nmr : x).ToArray();
            UnityEngine.Object.DestroyImmediate(t.gameObject);
            lg.SetLODs(lods);
            lg.localReferencePoint = refPt; lg.size = size; lg.fadeMode = fade; lg.animateCrossFading = anim;
            return true;
        }

        static bool Atomic(Transform t)
        {
            foreach (var c in t.GetComponents<Component>()) if (!(c is Transform)) return true;
            return false;
        }

        static Bounds SubtreeBounds(Transform t, out bool any)
        {
            any = false; var b = new Bounds();
            foreach (var r in t.GetComponentsInChildren<Renderer>(true)) { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            foreach (var c in t.GetComponentsInChildren<Collider>(true)) { if (!any) { b = c.bounds; any = true; } else b.Encapsulate(c.bounds); }
            return b;
        }

        static int RemoveEmpty(Transform R, HashSet<UnityEngine.Object> referenced, Dictionary<Vector2Int, Transform> keep)
        {
            int n = 0;
            bool Rec(Transform t) // true: t was destroyed
            {
                foreach (Transform ch in t.Cast<Transform>().ToList()) if (Rec(ch)) n++;
                if (t == R || keep.ContainsValue(t)) return false;
                if (t.childCount > 0 || Atomic(t) || referenced.Contains(t) || referenced.Contains(t.gameObject)) return false;
                UnityEngine.Object.DestroyImmediate(t.gameObject); return true;
            }
            Rec(R);
            return n;
        }

        /// every GameObject / Transform / Component a script of the scene (or a LODGroup elsewhere) points at
        static HashSet<UnityEngine.Object> Referenced(Scene scene)
        {
            var set = new HashSet<UnityEngine.Object>();
            void Add(UnityEngine.Object o)
            {
                if (!o) return;
                if (o is Component c) { set.Add(c); set.Add(c.gameObject); set.Add(c.transform); }
                else if (o is GameObject g) { set.Add(g); set.Add(g.transform); }
            }
            foreach (var root in scene.GetRootGameObjects())
                foreach (var mb in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (!mb) continue;
                    for (var ty = mb.GetType(); ty != null && ty != typeof(MonoBehaviour); ty = ty.BaseType)
                        foreach (var f in ty.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        {
                            if (!f.IsPublic && f.GetCustomAttribute<SerializeField>() == null) continue;
                            var ft = f.FieldType;
                            if (typeof(UnityEngine.Object).IsAssignableFrom(ft)) Add(f.GetValue(mb) as UnityEngine.Object);
                            else if (ft.IsArray && typeof(UnityEngine.Object).IsAssignableFrom(ft.GetElementType())) { if (f.GetValue(mb) is Array a) foreach (var o in a) Add(o as UnityEngine.Object); }
                            else if (ft.IsGenericType && ft.GetGenericTypeDefinition() == typeof(List<>) && typeof(UnityEngine.Object).IsAssignableFrom(ft.GetGenericArguments()[0]))
                            { if (f.GetValue(mb) is System.Collections.IList l) foreach (var o in l) Add(o as UnityEngine.Object); }
                        }
                }
            // only scene objects matter
            set.RemoveWhere(o => !o || EditorUtility.IsPersistent(o));
            return set;
        }

        // ------------------------------------------------------------------ manifest
        static string F(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        static void WriteManifest(Dictionary<Vector2Int, CellStat> cells, string scenePath)
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"version\": 1,\n  \"cellSize\": ").Append(F(CellGrid.L0)).Append(",\n  \"scene\": \"").Append(scenePath).Append("\",\n  \"baked\": \"").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("\",\n  \"cells\": [\n");
            bool first = true;
            foreach (var s in cells.Values.OrderBy(c => c.c.x).ThenBy(c => c.c.y))
            {
                if (!first) sb.Append(",\n"); first = false;
                var b = s.hasB ? s.b : new Bounds(CellOrigin(s.c), Vector3.zero);
                sb.Append($"    {{ \"x\": {s.c.x}, \"z\": {s.c.y}, \"min\": [{F(b.min.x)}, {F(b.min.y)}, {F(b.min.z)}], \"max\": [{F(b.max.x)}, {F(b.max.y)}, {F(b.max.z)}],\n      \"roots\": {{");
                bool f2 = true;
                foreach (var kv in s.perRoot.OrderBy(k => k.Key))
                {
                    if (!f2) sb.Append(", "); f2 = false;
                    sb.Append($"\"{kv.Key}\": {{ \"gameObjects\": {kv.Value[0]}, \"renderers\": {kv.Value[1]}, \"triangles\": {kv.Value[2]}, \"colliders\": {kv.Value[3]} }}");
                }
                sb.Append("},\n      \"objects\": [").Append(string.Join(", ", s.objects.Select(o => "\"" + o + "\""))).Append("] }");
            }
            sb.Append("\n  ]\n}\n");
            File.WriteAllText(MANIFEST, sb.ToString());
            AssetDatabase.ImportAsset(MANIFEST);
        }
    }
}
