// Magistrala/Ćelije: ispeci — streaming phase 2 (runtime: Assets/Scripts/Cells/CellData.cs). Reads the data-only systems from the
// open scene exactly as the builders left them and writes one blob per CellGrid cell (Assets/Generated/Cells/Data/c_<x>_<z>.bytes) plus
// the manifest (Assets/Generated/Cells/Resources/MagistralaCells.asset):
//   trees      World/Vegetation TreeRenderer: the records of its trees.bytes, by cell (file order kept)
//   props      every LODGroup root under World/Props and World/Nature whose objects are only renderers / LOD group / box, capsule,
//              sphere colliders (Tripo / Poly Haven props, the umbrella, the NatureManufacture cliffs): prototype + matrix
//   parked     World/ParkedCars ParkedCars: cars (+ the paint / plate tint the runtime computes) and the kolajderi boxes
//   lamps      World/Props/rasvjeta StreetLights: lamps, heads, the night light-pool meshes, the pole capsules
//   boats      World/Boats MooredBoats
// Nothing in the scene changes. The bake runs by itself before a player build (IPreprocessBuildWithReport) and before play mode
// when the scene's data differ from the last bake (fingerprints). When a scene is built / play mode starts (IProcessSceneWithReport)
// every system whose fingerprint still matches is taken out of that scene copy and a "Cells" object (CellData, CellProps,
// CellColliders) is added that loads it from the blobs; a system that does not match stays in the scene as before (a warning).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Magistrala.Cells;
using Magistrala.Streaming;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Magistrala.EditorTools
{
    public static class CellBake
    {
        public const string DIR = CellGrid.GeneratedRoot + "/Data", RES = CellGrid.GeneratedRoot + "/Resources", MANIFEST = RES + "/" + CellDataManifest.ResourceName + ".asset";
        public const string SCENE = "Assets/Scenes/Magistrala.unity";
        /// off: no strip / no bake (the game as before phase 2) — Magistrala/Ćelije: isključi / uključi
        public static bool Enabled { get => !EditorPrefs.GetBool("Magistrala.CellsOff", false); set => EditorPrefs.SetBool("Magistrala.CellsOff", !value); }

        [MenuItem("Magistrala/Ćelije: ispeci")]
        public static string Menu() { MainScene.Ensure(); var s = Bake(SceneManager.GetActiveScene(), true); Debug.Log(s); return s; }
        [MenuItem("Magistrala/Ćelije: isključi ili uključi")]
        public static string Toggle() { Enabled = !Enabled; var s = "[cells] " + (Enabled ? "on: builds / play mode load the systems from the cell blobs" : "OFF: builds / play mode as before phase 2"); Debug.Log(s); return s; }

        // ================================================================== collection (the same for the bake and the strip)
        sealed class Shape { public int sys, type, layer, flags, mat, name; public Vector3 pos, scale, size; public Quaternion rot; }
        sealed class Inst { public GameObject root; public int proto; public Matrix4x4 m; }
        sealed class Collected
        {
            public GameObject world;
            public TreeRenderer trees; public byte[] treeBytes;
            public List<GameObject> propRoots = new List<GameObject>();
            public Magistrala.Traffic.ParkedCars parked; public GameObject parkedCols;
            public StreetLights lamps; public List<GameObject> lampObjs = new List<GameObject>();
            public MooredBoats boats;
            public string[] fp = new string[CellBlob.SYSTEMS];
            public string[] why = new string[CellBlob.SYSTEMS];
        }

        static Transform Child(GameObject world, string name) => world ? world.transform.Find(name) : null;
        static GameObject World(Scene s) => s.GetRootGameObjects().FirstOrDefault(g => g.name == "World");

        static readonly HashSet<Type> OkTypes = new HashSet<Type> { typeof(Transform), typeof(MeshFilter), typeof(MeshRenderer), typeof(LODGroup), typeof(BoxCollider), typeof(CapsuleCollider), typeof(SphereCollider) };
        /// a prop instance we can draw instanced: a LODGroup root whose objects carry only renderers / LOD group / simple colliders
        static bool Convertible(GameObject g, out string why)
        {
            why = null;
            foreach (var c in g.GetComponentsInChildren<Component>(true))
            {
                if (!c) { why = "missing script"; return false; }
                if (!OkTypes.Contains(c.GetType())) { why = c.GetType().Name; return false; }
            }
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                foreach (var m in r.sharedMaterials) if (m && m.shader && !m.shader.isSupported) { why = "shader " + m.shader.name; return false; }
            }
            return true;
        }

        static Collected Collect(Scene s)
        {
            var c = new Collected { world = World(s) };
            if (!c.world) return c;
            // trees
            var veg = Child(c.world, "Vegetation"); c.trees = veg ? veg.GetComponent<TreeRenderer>() : null;
            if (c.trees && c.trees.data) { c.treeBytes = c.trees.data.bytes; c.fp[CellBlob.TREES] = Hash(h => { h.Bytes(c.treeBytes); h.I(c.trees.meshes.Length); }); }
            else c.why[CellBlob.TREES] = "no World/Vegetation TreeRenderer with data";
            // props + nature
            var skipped = new Dictionary<string, int>();
            foreach (var rootName in new[] { "Props", "Nature" })
            {
                var r = Child(c.world, rootName); if (!r) continue;
                foreach (var lg in r.GetComponentsInChildren<LODGroup>(true))
                {
                    var g = lg.gameObject;
                    if (!g.activeInHierarchy) continue;
                    if (lg.GetComponentsInParent<LODGroup>(true).Length > 1) continue; // (a nested group goes with its outer one)
                    if (!Convertible(g, out var why)) { skipped.TryGetValue(why, out int n); skipped[why] = n + 1; continue; }
                    c.propRoots.Add(g);
                }
            }
            c.fp[CellBlob.PROPS] = Hash(h =>
            {
                foreach (var g in c.propRoots)
                {
                    h.S(g.name); h.M(g.transform.localToWorldMatrix);
                    foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
                    {
                        // (no mesh names: static batching renames them to "Combined Mesh" in play mode / builds before the strip)
                        h.I(r.enabled && r.gameObject.activeInHierarchy ? 1 : 0); h.I((int)r.shadowCastingMode);
                        foreach (var m in r.sharedMaterials) h.S(m ? m.name : "");
                    }
                    foreach (var col in g.GetComponentsInChildren<Collider>(true)) { h.S(col.GetType().Name); h.V3(col.transform.position); h.I(col.enabled ? 1 : 0); }
                }
            });
            if (skipped.Count > 0) c.why[CellBlob.PROPS] = "kept as GameObjects: " + string.Join(", ", skipped.Select(x => $"{x.Key} {x.Value}"));
            // parked cars
            var pr = Child(c.world, "ParkedCars"); c.parked = pr ? pr.GetComponent<Magistrala.Traffic.ParkedCars>() : null;
            if (c.parked && c.parked.data && c.parked.cars != null)
            {
                var col = pr.Find("kolajderi"); c.parkedCols = col ? col.gameObject : null;
                c.fp[CellBlob.PARKED] = Hash(h =>
                {
                    for (int i = 0; i < c.parked.cars.Length; i++) { h.V4(c.parked.cars[i]); h.I(c.parked.kind[i]); h.I(c.parked.paint[i]); h.V4(Magistrala.Traffic.ParkedCars.Tint(c.parked.data, i, c.parked.kind[i], c.parked.paint[i])); }
                    if (c.parkedCols) foreach (var b in c.parkedCols.GetComponentsInChildren<BoxCollider>(true)) { h.V3(b.transform.position); h.Q(b.transform.rotation); h.V3(b.size); h.V3(b.center); }
                });
            }
            else c.why[CellBlob.PARKED] = "no World/ParkedCars";
            // street lamps
            var props = Child(c.world, "Props"); var rv = props ? props.Find("rasvjeta") : null; c.lamps = rv ? rv.GetComponent<StreetLights>() : null;
            if (c.lamps && c.lamps.lamps != null)
            {
                foreach (Transform t in rv)
                {
                    bool pool = c.lamps.pools != null && c.lamps.pools.Any(p => p && p.transform == t);
                    bool poles = t.GetComponents<Component>().All(x => x is Transform || x is CapsuleCollider) && t.childCount == 0 && t.GetComponent<CapsuleCollider>();
                    if (pool || poles) c.lampObjs.Add(t.gameObject);
                }
                c.fp[CellBlob.LAMPS] = Hash(h =>
                {
                    foreach (var l in c.lamps.lamps) h.V4(l); foreach (var t in c.lamps.types) h.I(t); if (c.lamps.heads != null) foreach (var x in c.lamps.heads) h.V3(x);
                    foreach (var g in c.lampObjs) { h.S(g.name); h.M(g.transform.localToWorldMatrix); h.I(g.GetComponents<Collider>().Length); var mf = g.GetComponent<MeshFilter>(); h.S(mf && mf.sharedMesh ? mf.sharedMesh.name : ""); }
                });
            }
            else c.why[CellBlob.LAMPS] = "no World/Props/rasvjeta StreetLights";
            // boats
            var bt = Child(c.world, "Boats"); c.boats = bt ? bt.GetComponent<MooredBoats>() : null;
            if (c.boats && c.boats.boats != null) c.fp[CellBlob.BOATS] = Hash(h => { for (int i = 0; i < c.boats.boats.Length; i++) { h.V4(c.boats.boats[i]); h.I(c.boats.kind[i]); h.F(c.boats.scale[i]); h.V4(c.boats.tint[i]); } });
            else c.why[CellBlob.BOATS] = "no World/Boats MooredBoats";
            // the grid and the format belong to every fingerprint
            string g0 = $"{CellBlob.VERSION}|{CellGrid.L0}|0|0|";
            for (int i = 0; i < CellBlob.SYSTEMS; i++) if (c.fp[i] != null) c.fp[i] = g0 + c.fp[i];
            return c;
        }

        // ================================================================== bake
        sealed class CellOut { public CellBlob.Writer w = new CellBlob.Writer(); public List<byte[]> trees = new List<byte[]>(); public List<(int p, Matrix4x4 m)> props = new List<(int, Matrix4x4)>();
            public List<(Vector4 car, int kind, int paint, Vector4 tint)> parked = new List<(Vector4, int, int, Vector4)>(); public List<(Vector4 l, int t)> lamps = new List<(Vector4, int)>();
            public List<Vector3> heads = new List<Vector3>(); public List<(int mesh, int layer, Matrix4x4 m, Bounds b)> pools = new List<(int, int, Matrix4x4, Bounds)>();
            public List<(Vector4 b, int kind, float scale, Vector4 tint)> boats = new List<(Vector4, int, float, Vector4)>(); public List<Shape> cols = new List<Shape>(); }

        public static string Bake(Scene scene, bool force)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var c = Collect(scene);
            if (!c.world) return "[cells] no World in " + scene.path;
            var man = AssetDatabase.LoadAssetAtPath<CellDataManifest>(MANIFEST);
            if (!force && man && Fresh(man, c)) return "[cells] bake is up to date";
            Dir(RES); Dir(DIR);
            var cells = new Dictionary<Vector2Int, CellOut>();
            CellOut At(Vector3 p) { var k = CellGrid.Cell(p); if (!cells.TryGetValue(k, out var o)) cells[k] = o = new CellOut(); return o; }
            var names = new List<string>(); var nameIx = new Dictionary<string, int>();
            int Name(string n) { if (!nameIx.TryGetValue(n, out int i)) { i = names.Count; names.Add(n); nameIx[n] = i; } return i; }
            var pmats = new List<PhysicsMaterial>();
            int PMat(PhysicsMaterial m) { if (!m) return -1; int i = pmats.IndexOf(m); if (i < 0) { i = pmats.Count; pmats.Add(m); } return i; }
            void Col(int sys, Collider col, string name)
            {
                if (!col || !col.enabled || !col.gameObject.activeInHierarchy) return;
                var t = col.transform; var sh = new Shape { sys = sys, layer = col.gameObject.layer, flags = (col.isTrigger ? 1 : 0) | (sys << 8), mat = PMat(col.sharedMaterial), name = Name(name), rot = t.rotation, scale = t.lossyScale };
                Vector3 ctr;
                if (col is BoxCollider b) { sh.type = 0; ctr = b.center; sh.size = b.size; }
                else if (col is CapsuleCollider cc) { sh.type = 1; ctr = cc.center; sh.size = new Vector3(cc.radius, cc.height, cc.direction); }
                else if (col is SphereCollider sc) { sh.type = 2; ctr = sc.center; sh.size = new Vector3(sc.radius, 0, 0); }
                else return;
                sh.pos = t.position + t.rotation * Vector3.Scale(t.lossyScale, ctr);   // centre moved into the pose: the same world shape
                At(sh.pos).cols.Add(sh);
            }
            var log = new List<string>();
            int[] counts = new int[CellBlob.SYSTEMS];
            // trees
            double[] mean = new double[0];
            if (c.fp[CellBlob.TREES] != null)
            {
                var b = c.treeBytes; int n = b.Length / 24, np = c.trees.meshes.Length; var sum = new double[np]; var cnt = new int[np];
                for (int i = 0; i < n; i++)
                {
                    var rec = new byte[24]; Buffer.BlockCopy(b, i * 24, rec, 0, 24);
                    At(new Vector3(BitConverter.ToSingle(rec, 0), 0, BitConverter.ToSingle(rec, 8))).trees.Add(rec);
                    int p = (int)BitConverter.ToSingle(rec, 20); if (p >= 0 && p < np && c.trees.meshes[p]) { sum[p] += BitConverter.ToSingle(rec, 16); cnt[p]++; }
                }
                mean = new double[np]; for (int p = 0; p < np; p++) mean[p] = cnt[p] > 0 ? sum[p] / cnt[p] : 0;
                counts[CellBlob.TREES] = n;
            }
            // props
            var protos = new List<PropProto>(); var protoIx = new Dictionary<string, int>(); var shaders = new HashSet<string>();
            foreach (var g in c.propRoots)
            {
                var key = ProtoKey(g, out var proto); if (!protoIx.TryGetValue(key, out int pi)) { pi = protos.Count; protoIx[key] = pi; protos.Add(proto); foreach (var part in proto.parts) if (part.mat) shaders.Add(part.mat.shader.name); }
                At(g.transform.position).props.Add((pi, g.transform.localToWorldMatrix));
                foreach (var col in g.GetComponentsInChildren<Collider>(true)) Col(CellBlob.PROPS, col, g.name);
            }
            counts[CellBlob.PROPS] = c.propRoots.Count;
            // parked
            if (c.fp[CellBlob.PARKED] != null)
            {
                var pc = c.parked;
                for (int i = 0; i < pc.cars.Length; i++) At(pc.cars[i]).parked.Add((pc.cars[i], pc.kind[i], pc.paint[i], Magistrala.Traffic.ParkedCars.Tint(pc.data, i, pc.kind[i], pc.paint[i])));
                if (c.parkedCols) foreach (var col in c.parkedCols.GetComponentsInChildren<Collider>(true)) Col(CellBlob.PARKED, col, col.name);
                counts[CellBlob.PARKED] = pc.cars.Length;
            }
            // lamps
            var poolMeshes = new List<Mesh>();
            if (c.fp[CellBlob.LAMPS] != null)
            {
                var sl = c.lamps;
                for (int i = 0; i < sl.lamps.Length; i++) At(sl.lamps[i]).lamps.Add((sl.lamps[i], sl.types[i]));
                if (sl.heads != null) foreach (var h in sl.heads) At(h).heads.Add(h);
                foreach (var g in c.lampObjs)
                {
                    foreach (var col in g.GetComponents<Collider>()) Col(CellBlob.LAMPS, col, "stup");
                    var mf = g.GetComponent<MeshFilter>(); var mr = g.GetComponent<MeshRenderer>();
                    if (mf && mf.sharedMesh && mr)
                    {
                        int mi = poolMeshes.IndexOf(mf.sharedMesh); if (mi < 0) { mi = poolMeshes.Count; poolMeshes.Add(mf.sharedMesh); }
                        var m = g.transform.localToWorldMatrix; var wb = TransformBounds(mf.sharedMesh.bounds, m);
                        At(wb.center).pools.Add((mi, g.layer, m, wb));
                    }
                }
                counts[CellBlob.LAMPS] = sl.lamps.Length;
            }
            // boats
            if (c.fp[CellBlob.BOATS] != null)
            {
                var mb = c.boats;
                for (int i = 0; i < mb.boats.Length; i++) At(mb.boats[i]).boats.Add((mb.boats[i], mb.kind[i], mb.scale[i], mb.tint[i]));
                counts[CellBlob.BOATS] = mb.boats.Length;
            }
            // write the blobs (only those whose bytes changed), drop the cells no longer used
            var written = new List<(Vector2Int k, string path)>(); int changed = 0; long bytes = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var kv in cells.OrderBy(x => x.Key.x).ThenBy(x => x.Key.y))
                {
                    var o = kv.Value; var w = o.w;
                    if (o.trees.Count > 0) { w.Begin(CellBlob.TREE); foreach (var r in o.trees) { w.Raw(r, 0, 24); w.Rec(); } }
                    if (o.props.Count > 0) { w.Begin(CellBlob.PROP); foreach (var (p, m) in o.props) { w.I(p); w.M34(m); w.Rec(); } }
                    if (o.parked.Count > 0) { w.Begin(CellBlob.PARK); foreach (var x in o.parked) { w.V4(x.car); w.I(x.kind); w.I(x.paint); w.V4(x.tint); w.Rec(); } }
                    if (o.lamps.Count > 0) { w.Begin(CellBlob.LAMP); foreach (var x in o.lamps) { w.V4(x.l); w.I(x.t); w.Rec(); } }
                    if (o.heads.Count > 0) { w.Begin(CellBlob.HEAD); foreach (var x in o.heads) { w.V3(x); w.Rec(); } }
                    if (o.pools.Count > 0) { w.Begin(CellBlob.POOL); foreach (var x in o.pools) { w.I(x.mesh); w.I(x.layer); w.M34(x.m); w.V3(x.b.center); w.V3(x.b.extents); w.Rec(); } }
                    if (o.boats.Count > 0) { w.Begin(CellBlob.BOAT); foreach (var x in o.boats) { w.V4(x.b); w.I(x.kind); w.F(x.scale); w.V4(x.tint); w.Rec(); } }
                    if (o.cols.Count > 0) { w.Begin(CellBlob.COLL); foreach (var s in o.cols) { w.I(s.type); w.I(s.layer); w.I(s.flags); w.I(s.mat); w.I(s.name); w.V3(s.pos); w.Q(s.rot); w.V3(s.scale); w.V3(Vector3.zero); w.V3(s.size); w.Rec(); } }
                    if (w.Empty) continue;
                    var data = w.Finish(kv.Key.x, kv.Key.y); bytes += data.Length;
                    string path = $"{DIR}/{CellGrid.Key(kv.Key)}.bytes";
                    if (!File.Exists(path) || !File.ReadAllBytes(path).SequenceEqual(data)) { File.WriteAllBytes(path, data); AssetDatabase.ImportAsset(path); changed++; }
                    written.Add((kv.Key, path));
                }
                var keep = new HashSet<string>(written.Select(x => x.path));
                foreach (var f in Directory.GetFiles(DIR, "c_*.bytes")) { var p = f.Replace('\\', '/'); if (!keep.Contains(p)) AssetDatabase.DeleteAsset(p); }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            // the manifest (updated in place: its GUID stays)
            bool isNew = !man; if (isNew) man = ScriptableObject.CreateInstance<CellDataManifest>();
            man.version = CellBlob.VERSION; man.cellSize = CellGrid.L0; man.gridOrigin = Vector2.zero;
            man.cells = written.Select(x => x.k).ToArray(); man.blobs = written.Select(x => AssetDatabase.LoadAssetAtPath<TextAsset>(x.path)).ToArray();
            man.fingerprints = (string[])c.fp.Clone(); man.systems = 0;
            for (int i = 0; i < CellBlob.SYSTEMS; i++) if (c.fp[i] != null) man.systems |= 1 << i;
            man.treeMeanScale = mean; man.treeCount = counts[CellBlob.TREES]; man.props = protos.ToArray();
            man.colliderNames = names.ToArray(); man.colliderMats = pmats.ToArray(); man.poolMeshes = poolMeshes.ToArray(); man.counts = counts;
            if (isNew) AssetDatabase.CreateAsset(man, MANIFEST); else EditorUtility.SetDirty(man);
            AssetDatabase.SaveAssets();
            int ncol = cells.Values.Sum(x => x.cols.Count);
            log.Add($"[cells] baked {written.Count} cells ({changed} changed, {bytes / 1024} KB) in {sw.ElapsedMilliseconds} ms: trees {counts[0]}, props {counts[1]} ({protos.Count} prototypes), parked {counts[2]}, lamps {counts[3]} ({poolMeshes.Count} pools), boats {counts[4]}, colliders {ncol}");
            log.Add("  prop shaders: " + string.Join(", ", shaders.OrderBy(x => x)));
            log.Add("  prototypes: " + string.Join(", ", protos.GroupBy(p => p.name).OrderByDescending(gp => gp.Count()).Take(12).Select(gp => $"{gp.Key} {gp.Count()}")));
            for (int i = 0; i < CellBlob.SYSTEMS; i++) if (c.why[i] != null) log.Add($"  {CellBlob.SysNames[i]}: {c.why[i]}");
            return string.Join("\n", log);
        }

        static bool Fresh(CellDataManifest man, Collected c)
        {
            if (man.version != CellBlob.VERSION || man.fingerprints == null || man.fingerprints.Length != CellBlob.SYSTEMS) return false;
            for (int i = 0; i < CellBlob.SYSTEMS; i++) if (man.fingerprints[i] != c.fp[i]) return false;
            return man.blobs.All(b => b);
        }

        static Bounds TransformBounds(Bounds b, Matrix4x4 m)
        {
            var r = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
            for (int i = 0; i < 8; i++) r.Encapsulate(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return r;
        }

        /// the prototype of one instance root and a key that is equal for instances that draw the same
        static string ProtoKey(GameObject g, out PropProto proto)
        {
            var root = g.transform; var inv = root.worldToLocalMatrix;
            var lg = g.GetComponent<LODGroup>(); var lods = lg ? lg.GetLODs() : new LOD[0];
            proto = new PropProto { name = g.name, lodHeights = lods.Select(l => l.screenRelativeTransitionHeight).ToArray() };
            if (lg)
            {
                proto.lodRef = root.InverseTransformPoint(lg.transform.TransformPoint(lg.localReferencePoint));
                float S(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));
                proto.lodSize = lg.size * S(lg.transform.lossyScale) / Mathf.Max(1e-6f, S(root.lossyScale));
            }
            var parts = new List<PropPart>(); bool first = true; var bb = new Bounds();
            var key = new System.Text.StringBuilder();
            proto.lodSize = (float)Math.Round(proto.lodSize, 4);
            key.Append(string.Join(",", proto.lodHeights.Select(x => x.ToString("R")))).Append('|').Append(proto.lodSize.ToString("F4")).Append('|').Append(proto.lodRef.ToString("F2")).Append('|');
            foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                var mesh = mf.sharedMesh; int level = -1;
                for (int l = 0; l < lods.Length; l++) if (lods[l].renderers != null && lods[l].renderers.Contains(r)) { level = l; break; }
                var local = inv * r.transform.localToWorldMatrix;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i]) continue;
                    var part = new PropPart { level = level, mesh = mesh, submesh = Mathf.Min(i, mesh.subMeshCount - 1), mat = mats[i], local = local, shadow = r.shadowCastingMode, receive = r.receiveShadows, layer = r.gameObject.layer, lightProbes = r.lightProbeUsage, reflProbes = r.reflectionProbeUsage };
                    parts.Add(part);
                    key.Append(mesh.GetInstanceID()).Append(':').Append(part.submesh).Append(':').Append(mats[i].GetInstanceID()).Append(':').Append(level).Append(':').Append((int)part.shadow).Append(part.receive ? 1 : 0).Append(':').Append(part.layer).Append(':');
                    for (int e = 0; e < 12; e++) { float v = (float)Math.Round(local[e / 4, e % 4], 3); key.Append(v == 0 ? "0" : v.ToString("F3")).Append(','); }
                    key.Append(';');
                }
                var lb = TransformBounds(mesh.bounds, local);
                if (first) { bb = lb; first = false; } else bb.Encapsulate(lb);
            }
            proto.parts = parts.ToArray(); proto.bounds = bb;
            return key.ToString();
        }

        static void Dir(string p) { if (!AssetDatabase.IsValidFolder(p)) { var parent = Path.GetDirectoryName(p).Replace('\\', '/'); Dir(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(p)); } }

        // ================================================================== strip (a scene copy being built / entering play mode)
        public static string Strip(Scene scene)
        {
            var man = AssetDatabase.LoadAssetAtPath<CellDataManifest>(MANIFEST);
            if (!man) return "[cells] no bake (Magistrala/Ćelije: ispeci): the scene stays as it is";
            var c = Collect(scene); if (!c.world) return "[cells] no World";
            int mask = 0; var log = new List<string>();
            for (int i = 0; i < CellBlob.SYSTEMS - 1; i++)
            {
                if (c.fp[i] == null) continue;
                if (man.fingerprints == null || i >= man.fingerprints.Length || man.fingerprints[i] != c.fp[i]) { log.Add($"{CellBlob.SysNames[i]} STALE (kept in the scene: Magistrala/Ćelije: ispeci)"); continue; }
                mask |= 1 << i;
            }
            if (mask == 0) return "[cells] nothing taken out: " + string.Join("; ", log);
            int nGo = 0;
            void Kill(GameObject g) { if (!g) return; nGo += g.GetComponentsInChildren<Transform>(true).Length; UnityEngine.Object.DestroyImmediate(g); }
            if ((mask & 1 << CellBlob.TREES) != 0) c.trees.data = null;
            if ((mask & 1 << CellBlob.PROPS) != 0)
            {
                foreach (var g in c.propRoots) Kill(g);
                // the groups left empty (only a Transform, no children)
                foreach (var rootName in new[] { "Props", "Nature" })
                {
                    var r = Child(c.world, rootName); if (!r) continue;
                    bool any = true;
                    while (any)
                    {
                        any = false;
                        foreach (var t in r.GetComponentsInChildren<Transform>(true))
                            if (t != r && t.childCount == 0 && t.GetComponents<Component>().Length == 1) { Kill(t.gameObject); any = true; break; }
                    }
                }
            }
            if ((mask & 1 << CellBlob.PARKED) != 0) { Kill(c.parkedCols); c.parked.cars = new Vector4[0]; c.parked.kind = new int[0]; c.parked.paint = new int[0]; }
            if ((mask & 1 << CellBlob.LAMPS) != 0) { foreach (var g in c.lampObjs) Kill(g); c.lamps.lamps = new Vector4[0]; c.lamps.types = new byte[0]; c.lamps.heads = new Vector3[0]; c.lamps.pools = new Renderer[0]; }
            if ((mask & 1 << CellBlob.BOATS) != 0) { c.boats.boats = new Vector4[0]; c.boats.kind = new int[0]; c.boats.scale = new float[0]; c.boats.tint = new Vector4[0]; }
            mask |= 1 << CellBlob.COLLIDERS;
            var go = new GameObject("Cells"); SceneManager.MoveGameObjectToScene(go, scene); go.isStatic = false;
            var cd = go.AddComponent<CellData>(); cd.manifest = man; cd.mask = mask;
            cd.trees = c.trees; cd.parked = c.parked; cd.lamps = c.lamps; cd.boats = c.boats;
            cd.props = go.AddComponent<CellProps>(); cd.colliders = go.AddComponent<CellColliders>();
            var on = Enumerable.Range(0, CellBlob.SYSTEMS - 1).Where(i => (mask & 1 << i) != 0).Select(i => CellBlob.SysNames[i]);
            return $"[cells] {scene.name}: from the cell blobs: {string.Join(", ", on)}; {nGo} GameObjects taken out" + (log.Count > 0 ? "; " + string.Join("; ", log) : "");
        }

        // ================================================================== hooks
        sealed class Hooks : IPreprocessBuildWithReport, IProcessSceneWithReport
        {
            public int callbackOrder => 100;
            public void OnPreprocessBuild(BuildReport report)
            {
                if (!Enabled) return;
                var s = SceneManager.GetActiveScene();
                if (s.path != SCENE) { Debug.LogWarning("[cells] the open scene is not " + SCENE + ": not baked before the build"); return; }
                Debug.Log(Bake(s, false));
            }
            public void OnProcessScene(Scene scene, BuildReport report)
            {
                if (!Enabled || scene.path != SCENE && scene.path != CellMerge.SCENE && scene.path != CellMerge.STREAMSCENE) return; // (phase 1/3: ProbeBuild builds CellMerge's copies of the main scene)
                Debug.Log(Strip(scene));
            }
        }

        [InitializeOnLoadMethod]
        static void PlayHook()
        {
            EditorApplication.playModeStateChanged += st =>
            {
                if (st != PlayModeStateChange.ExitingEditMode || !Enabled) return;
                var s = SceneManager.GetActiveScene(); if (s.path != SCENE) return;
                var r = Bake(s, false); if (!r.EndsWith("up to date")) Debug.Log(r);
            };
        }

        // ================================================================== fingerprint hash (FNV-1a 64 over the values' bits)
        sealed class H
        {
            ulong h = 1469598103934665603UL;
            public void B(byte b) { h ^= b; h *= 1099511628211UL; }
            public void I(int v) { for (int i = 0; i < 4; i++) B((byte)(v >> (i * 8))); }
            public void F(float v) => I(BitConverter.SingleToInt32Bits(v));
            public void V3(Vector3 v) { F(v.x); F(v.y); F(v.z); }
            public void V4(Vector4 v) { F(v.x); F(v.y); F(v.z); F(v.w); }
            public void Q(Quaternion q) { F(q.x); F(q.y); F(q.z); F(q.w); }
            public void M(Matrix4x4 m) { for (int i = 0; i < 16; i++) F(m[i]); }
            public void S(string s) { foreach (char ch in s) { B((byte)ch); B((byte)(ch >> 8)); } B(0); }
            public void Bytes(byte[] b) { foreach (var x in b) B(x); }
            public override string ToString() => h.ToString("x16");
        }
        static string Hash(Action<H> f) { var h = new H(); f(h); return h.ToString(); }
    }
}
