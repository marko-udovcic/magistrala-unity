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

        // plantAssets.ts: impostor square side and base offset (m), tint
        static readonly float[] IMP_SIZE = { 12.138f, 11.422f, 5.908f, 11.643f, 2.074f, 1.481f, .877f };
        static readonly float[] IMP_BASE = { -.243f, -.228f, -.118f, -.233f, -.041f, -.030f, -.018f };
        static readonly string[] IMP_COL = { "#dfe3cf", "#d2d8c4", "#e4e7da", "#cfd6c6", "#aab69c", "#a4b096", "#e3dccb" };

        /// far LOD: 4 crossed quads (views 0..3 of the game's 8-view impostor atlas), alpha-clipped two-sided URP Lit
        public static void Impostors(out Mesh[] meshes, out Material[] mats)
        {
            meshes = new Mesh[IDS.Length]; mats = new Material[IDS.Length];
            string dir = WorldBuilder.GEN + "/Plants";
            for (int p = 0; p < IMP_SIZE.Length; p++)
            {
                string tp = $"Assets/Textures/Imp/{IDS[p]}_imp.png";
                var imp = (TextureImporter)AssetImporter.GetAtPath(tp);
                if (imp.alphaIsTransparency == false || imp.mipMapsPreserveCoverage == false) { imp.alphaIsTransparency = true; imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = .45f; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport(); }
                float S = IMP_SIZE[p], B = IMP_BASE[p];
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
                for (int q = 0; q < 4; q++)
                {
                    float a = q * Mathf.PI / 4; var r = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (S / 2);
                    var f = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
                    var nrm = (f * .35f + Vector3.up).normalized; int b0 = v.Count;
                    v.Add(-r + Vector3.up * B); v.Add(r + Vector3.up * B); v.Add(r + Vector3.up * (B + S)); v.Add(-r + Vector3.up * (B + S));
                    for (int k = 0; k < 4; k++) n.Add(nrm);
                    float u0 = q / 8f, u1 = (q + 1) / 8f;
                    uv.Add(new Vector2(u0, 0)); uv.Add(new Vector2(u1, 0)); uv.Add(new Vector2(u1, 1)); uv.Add(new Vector2(u0, 1));
                    t.AddRange(new[] { b0, b0 + 2, b0 + 1, b0, b0 + 3, b0 + 2 });
                }
                var m = new Mesh { name = IDS[p] + "_imp" }; m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateBounds();
                string mp = $"{dir}/{IDS[p]}_imp.mesh.asset"; if (File.Exists(mp)) AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(m, mp);
                string matp = $"{dir}/{IDS[p]}_imp.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matp);
                if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matp); }
                ColorUtility.TryParseHtmlString(IMP_COL[p], out var col);
                mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tp)); mat.SetColor("_BaseColor", col);
                mat.SetFloat("_AlphaClip", 1); mat.SetFloat("_Cutoff", .45f); mat.EnableKeyword("_ALPHATEST_ON");
                mat.SetFloat("_Cull", 0); mat.doubleSidedGI = true; mat.SetFloat("_Smoothness", .05f); mat.SetFloat("_SpecularHighlights", 0); mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                mat.renderQueue = 2450; mat.enableInstancing = true; EditorUtility.SetDirty(mat);
                meshes[p] = AssetDatabase.LoadAssetAtPath<Mesh>(mp); mats[p] = mat;
            }
            AssetDatabase.SaveAssets();
        }

        /// leaves: the glTF leaf materials are alpha-blended (queue 3000, no depth write, shadow caster without clipping): under
        /// TAA they smear (no depth, so they reproject like the sky behind them), instanced batches sort wrongly and the cards
        /// cast square shadows. Alpha-clipped copies (glTF mask mode, as the other glTF cut-out materials: cutoff .5, queue
        /// 2450, depth write) in Generated/Plants; the originals stay in blendMaterials for -trees-blend.
        public static Material[] LeafClip(Material[] src, int[] start)
        {
            var outM = new Material[src.Length];
            string dir = WorldBuilder.GEN + "/Plants";
            for (int i = 0; i < src.Length; i++)
            {
                var m = src[i]; outM[i] = m;
                if (!m || !(m.renderQueue >= 3000 || m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))) continue;
                int p = 0; for (int k = 0; k < start.Length; k++) if (start[k] <= i) p = k;
                string mp = $"{dir}/{IDS[p]}_{m.name}_clip.mat";
                var c = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (!c) { c = new Material(m); AssetDatabase.CreateAsset(c, mp); } else c.CopyPropertiesFromMaterial(m);
                c.shader = m.shader; c.name = $"{IDS[p]}_{m.name}_clip";
                c.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); c.EnableKeyword("_ALPHATEST_ON");
                c.SetFloat("_Surface", 0); c.SetFloat("_Blend", 0); c.SetFloat("_AlphaClip", 1); c.SetFloat("_ZWrite", 1);
                c.SetFloat("_SrcBlend", 1); c.SetFloat("_DstBlend", 0); c.SetFloat("_SrcBlendAlpha", 1); c.SetFloat("_DstBlendAlpha", 0);
                c.SetFloat("alphaCutoff", .5f); c.SetFloat("_AlphaToMask", 1);
                c.SetOverrideTag("RenderType", "TransparentCutout"); c.renderQueue = 2450;
                c.SetShaderPassEnabled("DepthOnly", true); c.SetShaderPassEnabled("SHADOWCASTER", true); c.SetShaderPassEnabled("DepthNormals", true);
                c.enableInstancing = true; EditorUtility.SetDirty(c);
                outM[i] = c;
            }
            AssetDatabase.SaveAssets();
            return outM;
        }

        /// the scene's TreeRenderer: leaves alpha-clipped (LeafClip), originals kept in blendMaterials
        [MenuItem("Magistrala/Trees: alpha-clipped leaves")]
        public static string ApplyLeafClip()
        {
            var tr = Object.FindFirstObjectByType<Magistrala.TreeRenderer>(); if (!tr) return "no TreeRenderer";
            if (tr.blendMaterials == null || tr.blendMaterials.Length != tr.materials.Length) tr.blendMaterials = tr.materials;
            tr.materials = LeafClip(tr.blendMaterials, tr.materialStart);
            EditorUtility.SetDirty(tr); UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tr.gameObject.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(tr.gameObject.scene);
            int n = 0; for (int i = 0; i < tr.materials.Length; i++) if (tr.materials[i] != tr.blendMaterials[i]) n++;
            return $"leaf materials clipped: {n} of {tr.materials.Length}";
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
