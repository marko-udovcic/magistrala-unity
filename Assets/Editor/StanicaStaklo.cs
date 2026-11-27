// The bus shelter with glass sides (27. 11., Marko: glass side panels): Assets/Models/Props/Stanice/stanica-staklo.glb — our Tripo
// shelter (Assets/Models/Props/Tripo/stanica.glb) with its side / back walls replaced by tinted glass panels in thin metal frames
// (Codex, BalkanDriver assets-src/obrada-2711). Imported by glTFast; here one prefab like TripoPropImporter's "stanica":
//   - the same normalisation as stanica.json (yaw, per-axis scale to 3.8 x 2.5 x 2.0 m, pivot bottom centre), worked out from the
//     original GLB's bounds so the new one lands exactly where the old one stood (falls back to its own bounds if Codex rescaled it);
//   - materials: "Glass" → URP Lit transparent (premultiplied, dark tint, suppressed sky/specular reflections, no refraction, no shadow, two-sided),
//     the rest URP Lit with their glTF textures / factors, GPU instancing;
//   - LODGroup lod0 (shadows) / lod1 (no shadows), culled at 220 m; one box collider (footprint × 0.9).
// PropsBuilder.Stanice uses it instead of the Tripo prefab when the GLB is there.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class StanicaStaklo
    {
        public const string GLB = "Assets/Models/Props/Stanice/stanica-staklo.glb";
        const string ORIG = "Assets/Models/Props/Tripo/stanica.glb";
        const string OUT = "Assets/Generated/Props/Stanice";
        public const string NAME = "stanica-staklo";
        const float YAW = -90, SX = 3.8f, SY = 2.5f, SZ = 2.0f, CULL = 220;

        public static GameObject Prefab() => AssetDatabase.LoadAssetAtPath<GameObject>($"{OUT}/{NAME}.prefab");

        static Bounds RawBounds(string glb)
        {
            var b = File.ReadAllBytes(glb); int jl = System.BitConverter.ToInt32(b, 12);
            var j = MiniJson.O(MiniJson.Parse(System.Text.Encoding.UTF8.GetString(b, 20, jl)));
            var acc = MiniJson.L(j["accessors"]); bool first = true; var bb = new Bounds();
            foreach (var mo in MiniJson.L(j["meshes"]))
                foreach (var po in MiniJson.L(MiniJson.O(mo)["primitives"]))
                {
                    var a = MiniJson.O(acc[(int)MiniJson.D(MiniJson.O(MiniJson.O(po)["attributes"])["POSITION"])]);
                    var mn = MiniJson.L(a["min"]); var mx = MiniJson.L(a["max"]);
                    var lo = new Vector3(-MiniJson.F(mx[0]), MiniJson.F(mn[1]), MiniJson.F(mn[2])); var hi = new Vector3(-MiniJson.F(mn[0]), MiniJson.F(mx[1]), MiniJson.F(mx[2]));
                    if (first) { bb = new Bounds((lo + hi) / 2, hi - lo); first = false; } else { bb.Encapsulate(lo); bb.Encapsulate(hi); }
                }
            return bb;
        }

        static Bounds Rotated(Bounds b, Quaternion r)
        {
            var o = new Bounds(r * b.center, Vector3.zero);
            for (int k = 0; k < 8; k++) o.Encapsulate(r * new Vector3((k & 1) == 0 ? b.min.x : b.max.x, (k & 2) == 0 ? b.min.y : b.max.y, (k & 4) == 0 ? b.min.z : b.max.z));
            return o;
        }

        [MenuItem("Magistrala/Autobusne stanice: uvezi staklenu nadstrešnicu")]
        public static string Build()
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(GLB);
            if (!src) return $"[stanice] nema {GLB} (stara nadstrešnica)";
            if (!AssetDatabase.IsValidFolder("Assets/Generated/Props")) AssetDatabase.CreateFolder("Assets/Generated", "Props");
            if (!AssetDatabase.IsValidFolder(OUT)) AssetDatabase.CreateFolder("Assets/Generated/Props", "Stanice");
            var rot = Quaternion.Euler(0, YAW, 0);
            // flatten (glTFast already mirrored x), then the yaw
            var combine = new List<CombineInstance>(); var srcMats = new List<Material>();
            foreach (var mf in src.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>(); if (!mr || !mf.sharedMesh) continue;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                {
                    combine.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = Matrix4x4.Rotate(rot) * src.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix });
                    srcMats.Add(mr.sharedMaterials[Mathf.Min(s, mr.sharedMaterials.Length - 1)]);
                }
            }
            var mesh = new Mesh { name = NAME, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(combine.ToArray(), false, true); mesh.RecalculateBounds();
            // normalisation: the original's (same raw frame) unless this model is at another scale
            var ob = File.Exists(ORIG) ? Rotated(RawBounds(ORIG), rot) : mesh.bounds;
            var nb = mesh.bounds; bool same = Mathf.Abs(nb.size.x / ob.size.x - 1) < .2f && Mathf.Abs(nb.size.y / ob.size.y - 1) < .2f;
            var b = same ? ob : nb;
            var k = new Vector3(SX / b.size.x, SY / b.size.y, SZ / b.size.z);
            var v = mesh.vertices; var nn = mesh.normals;
            for (int i = 0; i < v.Length; i++)
            {
                var p = v[i]; v[i] = new Vector3((p.x - b.center.x) * k.x, (p.y - b.min.y) * k.y, (p.z - b.center.z) * k.z);
                if (nn != null && i < nn.Length) nn[i] = new Vector3(nn[i].x / k.x, nn[i].y / k.y, nn[i].z / k.z).normalized;
            }
            mesh.vertices = v; if (nn != null && nn.Length == v.Length) mesh.normals = nn;
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            string mp = $"{OUT}/{NAME}.mesh.asset"; if (File.Exists(mp)) AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mesh, mp);

            var mats = new Material[srcMats.Count]; int nGlass = 0;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            for (int s = 0; s < mats.Length; s++)
            {
                var m = srcMats[s]; string nm = m ? m.name : "mat" + s;
                bool glass = nm.ToLowerInvariant().Contains("glass") || nm.ToLowerInvariant().Contains("staklo");
                string path = $"{OUT}/{NAME}_{s}_{new string(nm.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray())}.mat";
                var c = AssetDatabase.LoadAssetAtPath<Material>(path); if (!c) { c = new Material(lit); AssetDatabase.CreateAsset(c, path); }
                c.shader = lit; c.shaderKeywords = new string[0];
                Texture tex = m && m.HasProperty("baseColorTexture") ? m.GetTexture("baseColorTexture") : m && m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
                var col = m && m.HasProperty("baseColorFactor") ? m.GetColor("baseColorFactor") : m && m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white;
                float metal = m && m.HasProperty("metallicFactor") ? m.GetFloat("metallicFactor") : m && m.HasProperty("_Metallic") ? m.GetFloat("_Metallic") : 0;
                float rough = m && m.HasProperty("roughnessFactor") ? m.GetFloat("roughnessFactor") : .6f;
                Texture nrm = m && m.HasProperty("normalTexture") ? m.GetTexture("normalTexture") : m && m.HasProperty("_BumpMap") ? m.GetTexture("_BumpMap") : null;
                if (glass)
                {
                    nGlass++;
                    // Dark green-grey transparent glass. Suppress the broad sky reflection that made it look milky.
                    c.SetFloat("_Surface", 1); c.SetFloat("_Blend", 0); c.SetFloat("_BlendModePreserveSpecular", 1);
                    c.SetFloat("_SrcBlend", (float)BlendMode.One); c.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    c.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); c.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
                    c.SetFloat("_ZWrite", 0); c.SetFloat("_Cull", 0); c.SetFloat("_ReceiveShadows", 1);
                    c.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); c.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                    c.SetOverrideTag("RenderType", "Transparent"); c.renderQueue = (int)RenderQueue.Transparent;
                    c.SetShaderPassEnabled("ShadowCaster", false); c.SetShaderPassEnabled("DepthOnly", false);
                    c.SetFloat("_EnvironmentReflections", 0); c.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
                    c.SetFloat("_SpecularHighlights", 0); c.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
                    c.SetTexture("_BaseMap", null);
                    c.SetColor("_BaseColor", new Color(.12f, .17f, .17f, .16f)); c.SetFloat("_Smoothness", .35f); c.SetFloat("_Metallic", 0);
                }
                else
                {
                    c.SetFloat("_EnvironmentReflections", 1); c.SetFloat("_SpecularHighlights", 1);
                    c.SetFloat("_Surface", 0); c.SetFloat("_ZWrite", 1); c.SetFloat("_Cull", 2); c.renderQueue = -1; c.SetOverrideTag("RenderType", "");
                    c.SetShaderPassEnabled("ShadowCaster", true); c.SetShaderPassEnabled("DepthOnly", true);
                    c.SetTexture("_BaseMap", tex); c.SetColor("_BaseColor", new Color(col.r, col.g, col.b, 1));
                    c.SetFloat("_Smoothness", Mathf.Clamp(1 - rough, .1f, .6f)); c.SetFloat("_Metallic", Mathf.Clamp01(metal));
                    if (nrm) { c.SetTexture("_BumpMap", nrm); c.EnableKeyword("_NORMALMAP"); } else c.SetTexture("_BumpMap", null);
                }
                c.enableInstancing = true; c.name = Path.GetFileNameWithoutExtension(path); EditorUtility.SetDirty(c); mats[s] = c;
            }

            var root = new GameObject(NAME);
            var lods = new List<LOD>(); var size = mesh.bounds.size; float maxDim = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            float[] dist = { .45f * CULL, CULL };
            for (int l = 0; l < 2; l++)
            {
                var g = new GameObject("lod" + l); g.transform.SetParent(root.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = g.AddComponent<MeshRenderer>(); mr.sharedMaterials = mats;
                mr.shadowCastingMode = l == 0 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.lightProbeUsage = LightProbeUsage.BlendProbes; mr.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
                lods.Add(new LOD(Mathf.Clamp(maxDim / (2 * dist[l] * Mathf.Tan(30 * Mathf.Deg2Rad)), .0005f, .99f), new Renderer[] { mr }));
            }
            var lg = root.AddComponent<LODGroup>(); lg.SetLODs(lods.ToArray()); lg.RecalculateBounds(); lg.fadeMode = LODFadeMode.None;
            var bc = root.AddComponent<BoxCollider>(); bc.size = new Vector3(size.x * .9f, size.y, size.z * .9f); bc.center = new Vector3(0, size.y / 2, 0);
            PrefabUtility.SaveAsPrefabAsset(root, $"{OUT}/{NAME}.prefab"); Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            int tris = 0; for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)mesh.GetIndexCount(s) / 3;
            string r = $"[stanice] staklena nadstrešnica: {tris} tris, {mats.Length} materijala ({nGlass} staklo), {size.x:F2} x {size.y:F2} x {size.z:F2} m ({(same ? "mjerilo originala" : "vlastito mjerilo")})";
            Debug.Log(r); return r;
        }
    }
}
