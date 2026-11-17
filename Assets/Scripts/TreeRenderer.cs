using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala
{
    /// Instanced vegetation: the importer writes all plants as binary records (x, y, z, yaw, scale, proto) into `data`;
    /// at start they are bucketed per 128 m cell and prototype; each frame the cells near the camera are drawn with
    /// Graphics.RenderMeshInstanced (shadows only within shadowDistance). Far cutoff per prototype (shrubs / rocks short).
    public class TreeRenderer : MonoBehaviour
    {
        public TextAsset data;
        public Mesh[] meshes;
        public Material[] materials;          // flattened: per prototype its submesh materials, in order
        public int[] materialStart;           // index into materials per prototype
        public float[] drawDistance;          // per prototype
        public float[] nearDistance;          // full mesh within, impostor (crossed quads) beyond
        public Mesh[] impMeshes;
        public Material[] impMaterials;
        public float shadowDistance = 90, impShadowDistance = 260;
        const float CELL = 128;

        class Bucket { public Bounds bounds; public Vector3 centre; public List<Matrix4x4>[] perProto; public Matrix4x4[][] arr; }
        readonly List<Bucket> buckets = new List<Bucket>();
        readonly Plane[] planes = new Plane[6];
        public int Count { get; private set; }
        public int DrawnLastFrame { get; private set; }

        void Start()
        {
            if (!data) return;
            var b = data.bytes; int n = b.Length / 24;
            var map = new Dictionary<long, Bucket>();
            for (int i = 0; i < n; i++)
            {
                int o = i * 24;
                float x = System.BitConverter.ToSingle(b, o), y = System.BitConverter.ToSingle(b, o + 4), z = System.BitConverter.ToSingle(b, o + 8);
                float yaw = System.BitConverter.ToSingle(b, o + 12), s = System.BitConverter.ToSingle(b, o + 16); int p = (int)System.BitConverter.ToSingle(b, o + 20);
                if (p < 0 || p >= meshes.Length || !meshes[p]) continue;
                long key = ((long)Mathf.FloorToInt(x / CELL) << 32) ^ (uint)Mathf.FloorToInt(z / CELL);
                if (!map.TryGetValue(key, out var bk))
                {
                    bk = new Bucket { perProto = new List<Matrix4x4>[meshes.Length] };
                    for (int k = 0; k < meshes.Length; k++) bk.perProto[k] = new List<Matrix4x4>();
                    var c = new Vector3((Mathf.Floor(x / CELL) + .5f) * CELL, y, (Mathf.Floor(z / CELL) + .5f) * CELL);
                    bk.bounds = new Bounds(c, new Vector3(CELL, 1, CELL)); map[key] = bk;
                }
                var m = Matrix4x4.TRS(new Vector3(x, y, z), Quaternion.Euler(0, yaw, 0), Vector3.one * s);
                bk.perProto[p].Add(m);
                bk.bounds.Encapsulate(new Bounds(new Vector3(x, y, z) + Vector3.up * meshes[p].bounds.center.y * s, meshes[p].bounds.size * s));
                Count++;
            }
            foreach (var bk in map.Values)
            {
                bk.arr = new Matrix4x4[meshes.Length][];
                for (int k = 0; k < meshes.Length; k++) bk.arr[k] = bk.perProto[k].ToArray();
                bk.perProto = null; bk.centre = bk.bounds.center; buckets.Add(bk);
            }
            foreach (var m in materials) if (m) m.enableInstancing = true;
        }

        void Update()
        {
            var cam = Camera.main; if (!cam || buckets.Count == 0) return;
            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            Vector3 cp = cam.transform.position; int drawn = 0;
            foreach (var bk in buckets)
            {
                float d = Vector3.Distance(new Vector3(cp.x, 0, cp.z), new Vector3(bk.centre.x, 0, bk.centre.z)) - CELL * .71f;
                if (d > 1400 || !GeometryUtility.TestPlanesAABB(planes, bk.bounds)) continue;
                bool shadows = d < shadowDistance;
                for (int p = 0; p < meshes.Length; p++)
                {
                    var arr = bk.arr[p]; if (arr.Length == 0 || d > drawDistance[p]) continue;
                    if (d > nearDistance[p] && impMeshes != null && p < impMeshes.Length && impMeshes[p])
                    {
                        var ip = new RenderParams(impMaterials[p]) { shadowCastingMode = d < impShadowDistance ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true, worldBounds = bk.bounds, layer = gameObject.layer };
                        for (int i = 0; i < arr.Length; i += 500) Graphics.RenderMeshInstanced(ip, impMeshes[p], 0, arr, Mathf.Min(500, arr.Length - i), i);
                        drawn += arr.Length; continue;
                    }
                    var mesh = meshes[p];
                    for (int sm = 0; sm < mesh.subMeshCount; sm++)
                    {
                        var rp = new RenderParams(materials[materialStart[p] + sm]) { shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off, receiveShadows = true, worldBounds = bk.bounds, layer = gameObject.layer };
                        for (int i = 0; i < arr.Length; i += 500) Graphics.RenderMeshInstanced(rp, mesh, sm, arr, Mathf.Min(500, arr.Length - i), i);
                    }
                    drawn += arr.Length;
                }
            }
            DrawnLastFrame = drawn;
        }
    }
}
