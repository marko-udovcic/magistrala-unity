// Terrain tree prototypes from the web game's plant GLBs (Assets/Models/Plants, imported by glTFast): each flattened into one
// mesh (submesh per material) on a prefab root, as Unity's terrain trees require.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class TreeProtos
    {
        public static readonly string[] IDS = { "pine_a", "pine_b", "olive", "cypress", "shrub_a", "shrub_b", "bush_c", "rock_a" };

        public static List<GameObject> Prefabs()
        {
            var outL = new List<GameObject>();
            string dir = WorldBuilder.GEN + "/Plants";
            if (!AssetDatabase.IsValidFolder(dir)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Plants");
            foreach (var id in IDS)
            {
                string pp = $"{dir}/{id}.prefab";
                var src = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Models/Plants/{id}.glb");
                if (src == null) { Debug.LogError("[Magistrala] plant missing " + id); continue; }
                var combine = new List<CombineInstance>(); var mats = new List<Material>();
                foreach (var mf in src.GetComponentsInChildren<MeshFilter>())
                {
                    var mr = mf.GetComponent<MeshRenderer>(); if (!mr || !mf.sharedMesh) continue;
                    var m = mf.sharedMesh;
                    for (int s = 0; s < m.subMeshCount; s++)
                    {
                        combine.Add(new CombineInstance { mesh = m, subMeshIndex = s, transform = src.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix });
                        mats.Add(mr.sharedMaterials[Mathf.Min(s, mr.sharedMaterials.Length - 1)]);
                    }
                }
                var mesh = new Mesh { name = id, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                mesh.CombineMeshes(combine.ToArray(), false, true);
                mesh.RecalculateBounds();
                string mp = $"{dir}/{id}.mesh.asset";
                if (File.Exists(mp)) AssetDatabase.DeleteAsset(mp);
                AssetDatabase.CreateAsset(mesh, mp);
                var go = new GameObject(id);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = mats.ToArray();
                foreach (var mat in mats) if (mat) mat.enableInstancing = true;
                var pf = PrefabUtility.SaveAsPrefabAsset(go, pp);
                Object.DestroyImmediate(go);
                outL.Add(pf);
            }
            return outL;
        }

        static float Hash(double x, double z) { double v = System.Math.Sin(x * 12.9898 + z * 78.233) * 43758.5453; return (float)(v - System.Math.Floor(v)); }

        /// vegetationData.ts speciesOf: tile tree kind → prototype index + scale (-1 = skip)
        public static int Species(int kind, double x, double y, double z, float scale, out float s)
        {
            float h = Hash(x, z); s = scale;
            switch (kind)
            {
                case 0: { float size = (h * 5.3f) % 1; bool a = h < (y < 260 ? .85f : y < 420 ? .45f : .12f); s = a ? (.55f + .5f * size) * scale : (.5f + .45f * size) * scale; return a ? 0 : 1; }
                case 1: if (h < .6f) { s = .8f + .5f * scale * h; return 2; } s = 1.1f + .6f * scale * h; return 4;
                case 2: s = .75f + .45f * scale * h; return 3;
                case 3: s = .75f + .5f * scale * h; return 2;
                case 10: s = scale * (.94f + .12f * h); return h < .8f ? 0 : 1;
                case 11: s = scale * (.94f + .12f * h); return 2;
                case 12: s = scale * (.94f + .12f * h); return 3;
                case 13: if (h < .55f) { s = scale * (.9f + .2f * h); return 4; } s = scale * 1.35f * (.9f + .2f * h); return 5;
                case 4: if (h < .22f) { s = (.45f + .55f * h * 4) * scale; return 4; } if (h < .5f) { s = (.6f + .6f * h) * scale; return 5; } s = (.9f + 1.3f * h) * scale; return 6;
                case 5: if (scale < .5f) return -1; s = (.4f + h * 1.2f) * 1.45f * (scale > 0 ? scale : 1); return 7;
                default: return -1; // 6–8: ground cover tufts (not in the probe)
            }
        }
    }
}
