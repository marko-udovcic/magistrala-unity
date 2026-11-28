// Terrain tree prototypes from the web game's plant GLBs (Assets/Models/Plants, imported by glTFast): each flattened into one
// mesh (submesh per material) on a prefab root, as Unity's terrain trees require.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class TreeProtos
    {
        public static readonly string[] IDS = { "pine_a", "pine_b", "olive", "cypress", "shrub_a", "shrub_b", "bush_c", "rock_a" };

        /// A street tree keeps its native proportions. Reject unsuitable prototypes instead of
        /// squeezing them to match a noisy Mapillary height/crown measurement. Width is the larger
        /// horizontal mesh extent; all instance axes receive the returned scalar (trees.bytes format).
        public static bool StreetShape(Bounds b, float minRatio, float maxRatio)
        {
            float h = b.size.y, w = Mathf.Max(b.size.x, b.size.z);
            return h > .1f && w / h >= minRatio && w / h <= maxRatio;
        }

        public static float StreetScale(Bounds b, float wantedHeight, float minHeight, float maxHeight, float maxWidth)
        {
            float h = b.size.y, w = Mathf.Max(b.size.x, b.size.z);
            if (h <= .1f || w <= .1f) return 0;
            return Mathf.Min(Mathf.Clamp(wantedHeight, minHeight, maxHeight) / h, maxWidth / w);
        }

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

        /// far LOD: 4 crossed quads at 45° steps, each showing our own full mesh seen square-on from that quad's normal, alpha-
        /// clipped, on Magistrala/Foliage. (Until 23. 11. the atlases were the web game's 8-view renders with its square sizes: other
        /// crown shapes, views mirrored against the quads and the trunk off the pivot, so at the full / impostor switch a tree
        /// changed shape and jumped sideways by up to ~2 m – Marko: "kad se približajen kao da minja poziciju vegetacija".)
        /// full / mats / start: the TreeRenderer's prototypes (the first IDS.Length - 1: rocks have no impostor)
        public static void Impostors(Mesh[] full, Material[] mats, int[] start, out Mesh[] meshes, out Material[] impMats)
        {
            meshes = new Mesh[IDS.Length]; impMats = new Material[IDS.Length];
            string dir = WorldBuilder.GEN + "/Plants";
            for (int p = 0; p < IDS.Length - 1; p++)
            {
                if (p >= full.Length || !full[p]) continue;
                string mp = $"{dir}/{IDS[p]}_imp.mesh.asset", matp = $"{dir}/{IDS[p]}_imp.mat";
                var m = AssetDatabase.LoadAssetAtPath<Mesh>(mp); if (!m) { m = new Mesh { name = IDS[p] + "_imp" }; AssetDatabase.CreateAsset(m, mp); }
                var mat = AssetDatabase.LoadAssetAtPath<Material>(matp); if (!mat) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, matp); }
                int n = full[p].subMeshCount, s0 = start[p];
                var tex = BakeImpostor(full[p], Enumerable.Range(s0, n).Select(i => i < mats.Length ? mats[i] : null).ToArray(), m, $"{dir}/{IDS[p]}_imp.png");
                ImpostorMat(mat, tex, m);
                EditorUtility.SetDirty(m);
                meshes[p] = m; impMats[p] = mat;
            }
            AssetDatabase.SaveAssets();
        }

        /// the impostor material of a baked atlas `tex` on the crossed quads `m` (also ExtraTrees)
        public static void ImpostorMat(Material mat, Texture2D tex, Mesh m)
        {
            mat.shader = Shader.Find("Universal Render Pipeline/Lit");
            mat.SetTexture("_BaseMap", tex); mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_AlphaClip", 1); mat.SetFloat("_Cutoff", .5f); mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", 0); mat.doubleSidedGI = true; mat.SetFloat("_Smoothness", .05f); mat.SetFloat("_SpecularHighlights", 0); mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            mat.renderQueue = 2450; mat.enableInstancing = true;
            // impostor: the crossed quads' normals bent to a sphere around the crown's middle (as the near mesh's leaves)
            var b = m.bounds; Foliage(mat, null, -1); mat.SetFloat("_CrownY", b.center.y); mat.SetFloat("_CrownR", Mathf.Max(b.extents.x, b.extents.y)); mat.SetFloat("_Bend", .8f); mat.SetFloat("_Inner", .2f);
            mat.SetColor("baseColorFactor", new Color(IMP_ALB, IMP_ALB, IMP_ALB, 1)); mat.SetFloat("_DirShade", IMP_SHADE); mat.SetFloat("_Grade", 0);
            EditorUtility.SetDirty(mat);
        }

        const int TILE = 512;
        /// the albedo of a whole crown on 4 cards is lit as if every leaf faced out: darker, to match the full mesh's own shading
        /// (screenshots of one pose with all full meshes / all impostors, 23. 11.). 25. 11.: on the direct light only (Foliage
        /// _DirShade) – it stands for the crown's self-shadow, which the cards lack; on the albedo it also darkened the ambient, and
        /// in the rain (all ambient) the impostors went far darker than the meshes (the black olives in front of the truck).
        /// Now .9 on the albedo and .65 more on the direct light (sun: ~ the old .74 overall; rain: .9, as the meshes)
        const float IMP_SHADE = .65f, IMP_ALB = .9f;

        /// 25. 11. (Marko: "masline crne po kiši"): olive and crnika share the web game's broadleaf texture, a dark yellow green
        /// (sRGB ~.32 .35 .15); the crnika was that × (.46 .56 .44) and its impostor × .74 more – near black, trunk included, worst
        /// in the rain. Foliage's grade recolours the leaves from their luminance: olive a silvery grey-green, crnika a dark
        /// evergreen that still reads as green. Leaf materials only; baseColorFactor back to white (the grade carries the colour)
        public static readonly Vector4 OLIVE_GRADE = new Vector4(2.2f, 2.5f, 1.85f, .9f), CRNIKA_GRADE = new Vector4(.95f, 1.3f, .7f, .9f);
        public static void Grade(Material m, Vector4 g)
        {
            if (!m || !m.HasProperty("_Grade")) return;
            m.SetFloat("_Grade", g.w); m.SetVector("_GradeTint", new Vector4(g.x, g.y, g.z, 0)); m.SetColor("baseColorFactor", Color.white);
            EditorUtility.SetDirty(m);
        }
        /// renders `mesh` (its submeshes with the albedo of `mats`) orthographically from the 4 quad normals into a 4-tile atlas
        /// (png, imported alpha-clip friendly) and writes the crossed quads into `quads`: centred on the pivot, as wide as the
        /// plant's largest horizontal reach from it, from its lowest to its highest point, so the impostor stands where the mesh does
        public static Texture2D BakeImpostor(Mesh mesh, Material[] mats, Mesh quads, string png)
        {
            var v = mesh.vertices; float R = .01f, y0 = float.MaxValue, y1 = float.MinValue;
            foreach (var q in v) { R = Mathf.Max(R, new Vector2(q.x, q.z).magnitude); y0 = Mathf.Min(y0, q.y); y1 = Mathf.Max(y1, q.y); }
            y0 = Mathf.Min(y0, 0); R *= 1.01f; y1 += (y1 - y0) * .01f;
            var sh = Shader.Find("Hidden/Magistrala/ImpostorBake");
            var bm = new Material[mesh.subMeshCount];
            for (int s = 0; s < bm.Length; s++)
            {
                var src = s < mats.Length ? mats[s] : null; bm[s] = new Material(sh);
                if (!src) continue;
                Texture t = src.HasProperty("baseColorTexture") ? src.GetTexture("baseColorTexture") : src.HasProperty("_BaseMap") ? src.GetTexture("_BaseMap") : src.HasProperty("_MainTex") ? src.GetTexture("_MainTex") : null;
                var c = src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor") : src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : Color.white;
                float cut = src.HasProperty("alphaCutoff") ? src.GetFloat("alphaCutoff") : src.HasProperty("_Cutoff") ? src.GetFloat("_Cutoff") : .5f;
                bool clip = src.renderQueue >= 2450 || src.IsKeywordEnabled("_ALPHATEST_ON");
                bm[s].SetTexture("_MainTex", t ? t : Texture2D.whiteTexture); bm[s].SetColor("_Color", c); bm[s].SetFloat("_Cutoff", clip ? cut : -1);
                if (src.HasProperty("_Grade")) { bm[s].SetFloat("_Grade", src.GetFloat("_Grade")); bm[s].SetVector("_GradeTint", src.GetVector("_GradeTint")); }
                if (src.HasProperty("baseColorTexture_ST")) { var st = src.GetVector("baseColorTexture_ST"); if (st.x != 0 || st.y != 0) bm[s].SetVector("_MainTex_ST", st); }
            }
            var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(TILE * 4, TILE, RenderTextureFormat.ARGB32, 24) { sRGB = true });
            var cb = new UnityEngine.Rendering.CommandBuffer();
            cb.SetRenderTarget(rt); cb.ClearRenderTarget(true, true, Color.clear);
            for (int q = 0; q < 4; q++)
            {
                // quad q spans r = (cos a, 0, sin a); seen from -f looking along f = (-sin a, 0, cos a), the screen's right is +r
                float a = q * Mathf.PI / 4; var f = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
                var view = Matrix4x4.TRS(-f * (R + 10), Quaternion.LookRotation(f, Vector3.up), new Vector3(1, 1, -1)).inverse;
                cb.SetViewport(new Rect(q * TILE, 0, TILE, TILE));
                cb.SetViewProjectionMatrices(view, Matrix4x4.Ortho(-R, R, y0, y1, .1f, 2 * R + 20));
                for (int s = 0; s < bm.Length; s++) cb.DrawMesh(mesh, Matrix4x4.identity, bm[s], s, 0);
            }
            Graphics.ExecuteCommandBuffer(cb); cb.Release();
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var img = new Texture2D(TILE * 4, TILE, TextureFormat.RGBA32, false); img.ReadPixels(new Rect(0, 0, TILE * 4, TILE), 0, 0); img.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
            foreach (var m in bm) Object.DestroyImmediate(m);
            var px = img.GetPixels32(); Bleed(px, TILE * 4, TILE); img.SetPixels32(px);
            File.WriteAllBytes(png, img.EncodeToPNG()); Object.DestroyImmediate(img);
            AssetDatabase.ImportAsset(png);
            var ti = (TextureImporter)AssetImporter.GetAtPath(png);
            ti.alphaIsTransparency = true; ti.mipMapsPreserveCoverage = true; ti.alphaTestReferenceValue = .5f; ti.wrapMode = TextureWrapMode.Clamp;
            ti.sRGBTexture = true; ti.maxTextureSize = 2048; ti.SaveAndReimport();
            // the quads: same layout as the bake, u of tile q from -r to +r, v from y0 to y1
            var qv = new List<Vector3>(); var qn = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int q = 0; q < 4; q++)
            {
                float a = q * Mathf.PI / 4; var r = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * R; var f = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
                var nrm = (f * .35f + Vector3.up).normalized; int b0 = qv.Count;
                qv.Add(-r + Vector3.up * y0); qv.Add(r + Vector3.up * y0); qv.Add(r + Vector3.up * y1); qv.Add(-r + Vector3.up * y1);
                for (int k = 0; k < 4; k++) qn.Add(nrm);
                float u0 = q / 4f, u1 = (q + 1) / 4f;
                uv.Add(new Vector2(u0, 0)); uv.Add(new Vector2(u1, 0)); uv.Add(new Vector2(u1, 1)); uv.Add(new Vector2(u0, 1));
                tri.AddRange(new[] { b0, b0 + 2, b0 + 1, b0, b0 + 3, b0 + 2 });
            }
            quads.Clear(); quads.SetVertices(qv); quads.SetNormals(qn); quads.SetUVs(0, uv); quads.SetTriangles(tri, 0); quads.RecalculateBounds();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(png);
        }

        /// colour of the clipped-away texels from their opaque neighbours (a few rings), so mips / filtering do not pull in black
        static void Bleed(Color32[] px, int w, int h)
        {
            var o = (Color32[])px.Clone();
            for (int it = 0; it < 8; it++)
            {
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int i = y * w + x; if (px[i].a > 0) continue;
                        int r = 0, g = 0, b = 0, n = 0;
                        for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                            {
                                int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= w || yy >= h) continue;
                                var c = px[yy * w + xx];
                                if (c.a == 0 && c.r == 0 && c.g == 0 && c.b == 0) continue;
                                r += c.r; g += c.g; b += c.b; n++;
                            }
                        if (n > 0) o[i] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), 0);
                    }
                System.Array.Copy(o, px, px.Length);
            }
        }

        /// the scene's TreeRenderer: the impostors of our prototypes (and the crnika's, which shares the olive's quads) rebaked
        /// from the full meshes in place – same mesh / material assets, so the scene and trees.bytes stay as they are
        [MenuItem("Magistrala/Trees: bake impostors")]
        public static string BakeSceneImpostors()
        {
            MainScene.Ensure();
            // (props, parked cars and boats have TreeRenderers too: the plants' one is World/Vegetation)
            var tr = System.Array.Find(Object.FindObjectsByType<Magistrala.TreeRenderer>(FindObjectsSortMode.None), t => t.name == "Vegetation");
            if (!tr || tr.impMeshes == null) { Debug.LogWarning("[stabla] no Vegetation TreeRenderer"); return "no TreeRenderer"; }
            // the olive / crnika leaf grade on the scene's materials first (the bake takes it into the atlases)
            int olive = System.Array.IndexOf(IDS, "olive");
            for (int p = 0; p < tr.meshes.Length; p++)
                if (tr.meshes[p] && tr.meshes[p] == tr.meshes[olive])
                {
                    Grade(LeafMat(tr, p), p == olive ? OLIVE_GRADE : CRNIKA_GRADE);
                    if (p == olive) continue;
                    // crnika bark: the olive's × .7 (was × .55, NaturePass.Finish)
                    for (int k = 0; k < tr.meshes[p].subMeshCount; k++)
                    {
                        Material bm = tr.materials[tr.materialStart[p] + k], ob = tr.materials[tr.materialStart[olive] + k];
                        if (bm && ob && bm != ob && bm.name.Contains("bark") && bm.HasProperty("baseColorFactor") && ob.HasProperty("baseColorFactor"))
                        { var c = ob.GetColor("baseColorFactor"); bm.SetColor("baseColorFactor", new Color(c.r * .7f, c.g * .68f, c.b * .66f, c.a)); EditorUtility.SetDirty(bm); }
                    }
                }
            Impostors(tr.meshes, tr.materials, tr.materialStart, out var im, out var imat);
            int n = 0;
            for (int p = 0; p < IDS.Length - 1 && p < tr.impMeshes.Length; p++) if (im[p]) n++;
            // prototypes that reuse one of ours' impostor quads (crnika = the olive's mesh, own leaf / bark colours): their own atlas
            // baked from their own materials (until 25. 11. the olive's atlas × the leaf colour ratio – the trunk went as dark as the leaves)
            for (int p = IDS.Length; p < tr.impMeshes.Length; p++)
            {
                int src = System.Array.IndexOf(im, tr.impMeshes[p]); if (src < 0 || !tr.impMaterials[p] || !imat[src]) continue;
                var mt = tr.impMaterials[p];
                mt.shader = imat[src].shader; mt.CopyPropertiesFromMaterial(imat[src]);
                if (tr.meshes[p] == tr.meshes[src])
                {
                    int sn = tr.meshes[p].subMeshCount, s0 = tr.materialStart[p];
                    var tmp = new Mesh();
                    var tex = BakeImpostor(tr.meshes[p], Enumerable.Range(s0, sn).Select(i => i < tr.materials.Length ? tr.materials[i] : null).ToArray(), tmp,
                        Path.ChangeExtension(AssetDatabase.GetAssetPath(mt), ".png"));
                    Object.DestroyImmediate(tmp);
                    mt.SetTexture("baseColorTexture", tex);
                }
                mt.SetColor("baseColorFactor", new Color(IMP_ALB, IMP_ALB, IMP_ALB, 1)); EditorUtility.SetDirty(mt); n++;
            }
            // the downloaded prototypes (ExtraTrees: palm, broadleaf street trees): their own quads, rebaked from their own materials
            for (int p = IDS.Length; p < tr.meshes.Length && p < tr.impMeshes.Length; p++)
            {
                var me = tr.meshes[p]; if (!me || !tr.impMeshes[p] || !tr.impMaterials[p] || !ExtraTrees.DEFS.Any(d => me.name == d.id || me.name.StartsWith(d.id + "."))) continue;
                int sn = me.subMeshCount, s0 = tr.materialStart[p];
                var tex = BakeImpostor(me, Enumerable.Range(s0, sn).Select(i => i < tr.materials.Length ? tr.materials[i] : null).ToArray(), tr.impMeshes[p], $"{WorldBuilder.GEN}/Plants/{me.name.Split('.')[0]}_imp.png");
                ImpostorMat(tr.impMaterials[p], tex, tr.impMeshes[p]); EditorUtility.SetDirty(tr.impMeshes[p]); n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[stabla] impostors baked: {n}");
            return $"impostors baked: {n}";
        }
        /// a prototype's first leaf material (Magistrala/Foliage)
        static Material LeafMat(Magistrala.TreeRenderer tr, int p)
        {
            int n = tr.meshes[p].subMeshCount;
            for (int i = tr.materialStart[p]; i < tr.materialStart[p] + n && i < tr.materials.Length; i++)
            {
                var m = tr.materials[i]; if (m && m.shader && m.shader.name == "Magistrala/Foliage") return m;
            }
            return null;
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
                c.enableInstancing = true;
                Foliage(c, AssetDatabase.LoadAssetAtPath<Mesh>($"{dir}/{IDS[p]}.mesh.asset"), i - start[p]);
                if (IDS[p] == "olive") Grade(c, OLIVE_GRADE);
                EditorUtility.SetDirty(c);
                outM[i] = c;
            }
            AssetDatabase.SaveAssets();
            return outM;
        }

        /// the leaves (and impostors) on Magistrala/Foliage (Assets/Shaders/Foliage.shader: crown-bent normals, wrap light,
        /// translucency, per-plant tint; 23. 11.: in the sun the shaded half of every crown was almost black). The crown centre
        /// and radius come from the leaf submesh's bounds.
        public static bool UseFoliage = true;
        public static void Foliage(Material c, Mesh mesh, int sub)
        {
            var sh = Shader.Find("Magistrala/Foliage"); if (!UseFoliage || !sh || !c) return;
            Bounds b = mesh && sub >= 0 && sub < mesh.subMeshCount ? mesh.GetSubMesh(sub).bounds : new Bounds(new Vector3(0, 3, 0), new Vector3(5, 4, 5));
            var tex = c.HasProperty("baseColorTexture") ? c.GetTexture("baseColorTexture") : c.HasProperty("_BaseMap") ? c.GetTexture("_BaseMap") : null;
            var col = c.HasProperty("baseColorFactor") ? c.GetColor("baseColorFactor") : c.HasProperty("_BaseColor") ? c.GetColor("_BaseColor") : Color.white;
            float cut = c.HasProperty("alphaCutoff") ? c.GetFloat("alphaCutoff") : c.HasProperty("_Cutoff") ? c.GetFloat("_Cutoff") : .5f;
            int q = c.renderQueue;
            c.shader = sh;
            c.SetTexture("baseColorTexture", tex); c.SetColor("baseColorFactor", col); c.SetFloat("alphaCutoff", cut);
            c.SetFloat("_CrownY", b.center.y); c.SetFloat("_CrownR", Mathf.Max(.5f, Mathf.Max(b.extents.x, Mathf.Max(b.extents.y, b.extents.z))));
            c.SetFloat("_Cull", 0); c.renderQueue = q < 2000 ? 2450 : q; c.enableInstancing = true;
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
