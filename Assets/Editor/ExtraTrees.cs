// Extra tree prototypes from downloaded GLBs (Assets/Models/Plants, imported by glTFast), appended to the TreeRenderer by
// NaturePass.Begin like the package pines (27. 11.):
//   palma_33 / 34 / 35, palma_lepeza_a / b, tamaris_a / b (28. 11.)   tall Canary date palms, Washingtonia fan palms, tamarisks
//              (Assets/Models/Plants/Palme, see the DEFS) — the riva promenade rows (PalmRows), the surveyed palms (Drvoredi) and
//              the palme.json palms (MapillaryTrees)
//   lisnato_a  tall narrow maple (~18 m native), lisnato_b wide beech (umbrella, ~24 m wide), lisnato_c round maple (~12 m)
//              (MegaTree, CC BY 4.0, Sketchfab) — the broadleaf street / avenue trees (Marko: like Poljička in Split), instead of
//              the crnika / olive fallback for the Mapillary "other broadleaf" crowns and the surveyed "broadleaf" ones
// Each: the GLB flattened into one mesh (submesh per material), pivot moved to the trunk's foot (lowest bark vertices), leaf
// materials (glTF MASK) alpha-clipped on Magistrala/Foliage (two-sided, as TreeProtos.LeafClip), bark a glTF copy with
// instancing; far LOD = the 4-quad impostor baked from the full mesh (TreeProtos.BakeImpostor, as our other prototypes), so the
// 11–21k-triangle meshes are drawn only within `near` m.
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class ExtraTrees
    {
        public class Def { public string id, glb; public float draw, near; /** a trunk added under the crown (m; 0 none), crown scale */ public float trunk, crown = 1; }
        public static readonly Def[] DEFS =
        {
            // the palms and tamarisks of the rivas (28. 11.: PlantCatalog "Realistic HD" Canary date palm 33/40, 34/40, 35/40, Mexican
            // fan palm 8/10, French tamarisk 19/20, 12/20; andre.arq.89 "Washingtonia Robusta Palm_02"; all CC BY 4.0), reduced in
            // Blender (BalkanDriver assets-src/palme-visoke/izvjestaj.md): fronds / fan leaves / branch clusters as alpha-cut cards
            // with their own baked atlas, trunk a low cylinder with baked colour + normal; origin at the trunk's foot, real heights
            // (7.9 / 12.1 / 15.2 / 13.7 / 7.3 / 3.6 / 2.2 m). Placed by PalmRows (riva promenades) and Drvoredi (the surveyed palms)
            new Def { id = "palma_33", glb = PALME + "palma-33.glb", draw = 1000, near = 60 },
            new Def { id = "palma_34", glb = PALME + "palma-34.glb", draw = 1000, near = 60 },
            new Def { id = "palma_35", glb = PALME + "palma-35.glb", draw = 1000, near = 60 },
            new Def { id = "palma_lepeza_a", glb = PALME + "palma-lepeza-a.glb", draw = 1000, near = 60 },
            new Def { id = "palma_lepeza_b", glb = PALME + "palma-lepeza-b.glb", draw = 1000, near = 60 },
            new Def { id = "tamaris_a", glb = PALME + "tamaris-a.glb", draw = 600, near = 45 },
            new Def { id = "tamaris_b", glb = PALME + "tamaris-b.glb", draw = 600, near = 45 },
            new Def { id = "lisnato_a", glb = "Assets/Models/Plants/lisnato_a.glb", draw = 1000, near = 55 },
            new Def { id = "lisnato_b", glb = "Assets/Models/Plants/lisnato_b.glb", draw = 1000, near = 55 },
            new Def { id = "lisnato_c", glb = "Assets/Models/Plants/lisnato_c.glb", draw = 1000, near = 55 },
        };
        const string PALME = "Assets/Models/Plants/Palme/";
        public static bool On = true;
        /// the palm / tamarisk prototypes (27. 11. off: the only palm was a young 2.3 m one without a trunk; 28. 11. on: the tall
        /// Canary / fan palms and the tamarisks of Assets/Models/Plants/Palme)
        public static bool Palms = true;
        const string DIR = WorldBuilder.GEN + "/Plants";

        public static void Protos(List<NaturePass.Proto> extra)
        {
            if (!On) return;
            foreach (var d in DEFS)
            {
                if ((d.id.StartsWith("palma") || d.id.StartsWith("tamaris")) && !Palms) continue;
                try { var p = Make(d); if (p != null) extra.Add(p); }
                catch (System.Exception e) { Debug.LogWarning($"[stabla] {d.id}: {e.Message}"); }
            }
        }

        static bool Leaf(Material m) => m && (m.renderQueue >= 2450 || m.IsKeywordEnabled("_ALPHATEST_ON") || m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")
            || Regex(m.name, "leaf|frond|list|palm_l"));
        static bool Regex(string s, string p) => System.Text.RegularExpressions.Regex.IsMatch(s ?? "", p, System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        public static NaturePass.Proto Make(Def d)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(d.glb);
            if (!src) { Debug.Log($"[stabla] {d.id}: no {d.glb} (skipped)"); return null; }
            if (!AssetDatabase.IsValidFolder(DIR)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Plants");
            var combine = new List<CombineInstance>(); var srcMats = new List<Material>();
            foreach (var mf in src.GetComponentsInChildren<MeshFilter>())
            {
                var mr = mf.GetComponent<MeshRenderer>(); if (!mr || !mf.sharedMesh) continue;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++)
                {
                    combine.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = src.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix });
                    srcMats.Add(mr.sharedMaterials[Mathf.Min(s, mr.sharedMaterials.Length - 1)]);
                }
            }
            if (combine.Count == 0) return null;
            var mesh = new Mesh { name = d.id, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(combine.ToArray(), false, true);
            // upright: the crown (leaf submeshes) above the trunk (the rest) — the Sketchfab exports come in lying on their side
            {
                var vv = mesh.vertices; Vector3 cl = Vector3.zero, cb = Vector3.zero; int nl = 0, nb = 0;
                for (int s = 0; s < mesh.subMeshCount; s++)
                    foreach (int i in mesh.GetTriangles(s)) { if (Leaf(srcMats[s])) { cl += vv[i]; nl++; } else { cb += vv[i]; nb++; } }
                if (nl > 0 && nb > 0)
                {
                    var dd = cl / nl - cb / nb; var ax = Vector3.zero;
                    if (Mathf.Abs(dd.x) >= Mathf.Abs(dd.y) && Mathf.Abs(dd.x) >= Mathf.Abs(dd.z)) ax = new Vector3(Mathf.Sign(dd.x), 0, 0);
                    else if (Mathf.Abs(dd.z) >= Mathf.Abs(dd.y)) ax = new Vector3(0, 0, Mathf.Sign(dd.z));
                    else ax = new Vector3(0, Mathf.Sign(dd.y), 0);
                    if (ax != Vector3.up)
                    {
                        var q = Quaternion.FromToRotation(ax, Vector3.up); var nn = mesh.normals;
                        for (int i = 0; i < vv.Length; i++) { vv[i] = q * vv[i]; if (nn != null && i < nn.Length) nn[i] = q * nn[i]; }
                        mesh.vertices = vv; if (nn != null && nn.Length == vv.Length) mesh.normals = nn; mesh.RecalculateTangents(); mesh.RecalculateBounds();
                        Debug.Log($"[stabla] {d.id}: turned upright ({ax} was up)");
                    }
                }
            }
            // pivot: the trunk's foot (the lowest 40 cm of the non-leaf submeshes), on the ground at its lowest point
            var v = mesh.vertices; float y0 = float.MaxValue;
            var trunk = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++) if (!Leaf(srcMats[s])) trunk.AddRange(mesh.GetTriangles(s));
            if (trunk.Count == 0) trunk.AddRange(Enumerable.Range(0, v.Length));
            foreach (int i in trunk) y0 = Mathf.Min(y0, v[i].y);
            Vector3 foot = Vector3.zero; int n = 0;
            foreach (int i in trunk.Distinct()) if (v[i].y < y0 + .4f) { foot += v[i]; n++; }
            foot = n > 0 ? foot / n : Vector3.zero; foot.y = y0;
            for (int i = 0; i < v.Length; i++) v[i] -= foot;
            mesh.vertices = v; mesh.RecalculateBounds();
            if (d.trunk > 0) AddTrunk(mesh, srcMats, d);
            string mp = $"{DIR}/{d.id}.mesh.asset";
            if (File.Exists(mp)) AssetDatabase.DeleteAsset(mp);
            AssetDatabase.CreateAsset(mesh, mp);
            // materials: leaves alpha-clipped on Foliage, bark an instanced copy
            var mats = new Material[srcMats.Count];
            for (int s = 0; s < mats.Length; s++)
            {
                var m = srcMats[s]; if (!m) continue;
                string matp = $"{DIR}/{d.id}_{s}_{San(m.name)}.mat";
                var c = AssetDatabase.LoadAssetAtPath<Material>(matp);
                if (!c) { c = new Material(m); AssetDatabase.CreateAsset(c, matp); } else { c.shader = m.shader; c.CopyPropertiesFromMaterial(m); }
                c.name = $"{d.id}_{San(m.name)}"; c.enableInstancing = true;
                if (Leaf(m))
                {
                    c.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); c.EnableKeyword("_ALPHATEST_ON");
                    c.SetFloat("_Surface", 0); c.SetFloat("_AlphaClip", 1); c.SetFloat("_ZWrite", 1); c.SetFloat("_SrcBlend", 1); c.SetFloat("_DstBlend", 0);
                    if (c.HasProperty("alphaCutoff") && c.GetFloat("alphaCutoff") <= 0) c.SetFloat("alphaCutoff", .5f);
                    c.SetOverrideTag("RenderType", "TransparentCutout"); c.renderQueue = 2450;
                    TreeProtos.Foliage(c, mesh, s);
                    c.SetFloat("_Cull", 0); c.doubleSidedGI = true;
                }
                EditorUtility.SetDirty(c); mats[s] = c;
            }
            // impostor (as TreeProtos.Impostors)
            string qp = $"{DIR}/{d.id}_imp.mesh.asset", ip = $"{DIR}/{d.id}_imp.mat";
            var quads = AssetDatabase.LoadAssetAtPath<Mesh>(qp); if (!quads) { quads = new Mesh { name = d.id + "_imp" }; AssetDatabase.CreateAsset(quads, qp); }
            var tex = TreeProtos.BakeImpostor(mesh, mats, quads, $"{DIR}/{d.id}_imp.png");
            var im = AssetDatabase.LoadAssetAtPath<Material>(ip); if (!im) { im = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(im, ip); }
            TreeProtos.ImpostorMat(im, tex, quads);
            EditorUtility.SetDirty(quads);
            AssetDatabase.SaveAssets();
            int tris = 0; for (int s = 0; s < mesh.subMeshCount; s++) tris += (int)mesh.GetIndexCount(s) / 3;
            Debug.Log($"[stabla] prototype {d.id}: {tris} tris, {mesh.bounds.size.x:F1} x {mesh.bounds.size.y:F1} x {mesh.bounds.size.z:F1} m native, {mats.Count(Leaf)} leaf materials");
            return new NaturePass.Proto { id = d.id, mesh = mesh, mats = mats, imp = quads, impMat = im, draw = d.draw, near = d.near };
        }

        /// the crown scaled by d.crown and lifted onto a tapered 12-sided trunk of d.trunk m (Canary palm: ~0.8 m thick at the foot,
        /// a little bulge at the base), on the first non-leaf material (the crownshaft's bark texture, v tiled along the height)
        static void AddTrunk(Mesh mesh, List<Material> mats, Def d)
        {
            int sub = 0; for (int s = 0; s < mats.Count; s++) if (!Leaf(mats[s])) { sub = s; break; }
            var V = new List<Vector3>(); var N = new List<Vector3>(); var U = new List<Vector2>();
            mesh.GetVertices(V); mesh.GetNormals(N); mesh.GetUVs(0, U);
            while (N.Count < V.Count) N.Add(Vector3.up); while (U.Count < V.Count) U.Add(Vector2.zero);
            for (int i = 0; i < V.Count; i++) V[i] = V[i] * d.crown + Vector3.up * d.trunk;
            var tris = new List<int>[mesh.subMeshCount]; for (int s = 0; s < tris.Length; s++) tris[s] = new List<int>(mesh.GetTriangles(s));
            const int SIDES = 12, RINGS = 6; float top = d.trunk + .25f * d.crown;
            int b0 = V.Count;
            for (int r = 0; r <= RINGS; r++)
            {
                float t = r / (float)RINGS, y = -.1f + t * (top + .1f);
                float rad = Mathf.Lerp(.42f, .3f, t) + .12f * Mathf.Exp(-t * 14);
                for (int k = 0; k <= SIDES; k++)
                {
                    float a = k * Mathf.PI * 2 / SIDES; var n = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    V.Add(n * rad + Vector3.up * y); N.Add(n); U.Add(new Vector2(k / (float)SIDES, y / 1.2f));
                }
            }
            for (int r = 0; r < RINGS; r++)
                for (int k = 0; k < SIDES; k++)
                {
                    int a = b0 + r * (SIDES + 1) + k, b = a + SIDES + 1;
                    tris[sub].AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            mesh.Clear(); mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(V); mesh.SetNormals(N); mesh.SetUVs(0, U); mesh.subMeshCount = tris.Length;
            for (int s = 0; s < tris.Length; s++) mesh.SetTriangles(tris[s], s);
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
        }

        static string San(string s) => new string((s ?? "m").Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
    }
}
