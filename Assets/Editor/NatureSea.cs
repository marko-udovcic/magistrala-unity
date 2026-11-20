// Magistrala/Priroda: more (NatureManufacture) — the Coast & Dunes sea shader ("Water Sea Offset", URP 17.3) on our sea grid, as an
// A/B against our own sea (Magistrala/Sea): World/Sea gets "sea near nm" (the same near grid as GraphicsPass.Sea, white vertex colour =
// full opacity, uv0 = game metres) and "sea far nm" (a ring around the near grid out to 15 km, so the far plane under the transparent
// water does not read as a 8 cm deep floor), plus SeaMode (runtime switch, -more nm|nase).
// The package's sea is a R.A.M 3 spline mesh with flow / shore data in uv3 and tessellated Gerstner waves; R.A.M 3 is not ours, so
// here uv3 = 0 (no shore-directed "polar" waves), the waves are cut to a calm bay (a few cm) and the ripple comes from the normals;
// shore foam and the shallow-to-deep colour come from the depth texture (our terrain runs under the sea: real depth).
// Needs the package folder (Assets/NatureManufacture Assets, not in git); without it nothing is built and our sea stays.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.EditorTools
{
    public static class NatureSea
    {
        const string SRC = "Assets/NatureManufacture Assets/Coast Environment/Water Materials/M_R3_Sea.mat";
        const string OUT = WorldBuilder.GEN + "/Sea";
        /// default after a rebuild (the -more switch overrides it in the player)
        public static bool DefaultNM = false; // owner 2026-11-20: keep our sea (the package sea needs R.A.M 3 data); -more nm for A/B
        /// uv3: R.A.M's per-vertex flow; zero made the flow-map normals degenerate (radial streaks from the camera): a slow constant drift
        public static Vector4 Flow = new Vector4(.25f, .15f, 0, 0);
        // the near grid of GraphicsPass.Sea (game x / z)
        const double X0 = -9300, X1 = 1200, Z0 = -6000, Z1 = -2800;

        [MenuItem("Magistrala/Priroda: more (NatureManufacture)")]
        public static string Build()
        {
            var src = AssetDatabase.LoadAssetAtPath<Material>(SRC);
            var root = GameObject.Find("World/Sea");
            if (!src || !root) { var m = "[more] no package sea material or no World/Sea: our sea only"; Debug.Log(m); return m; }
            foreach (var n in new[] { "sea near nm", "sea far nm" }) { var t = root.transform.Find(n); if (t) Object.DestroyImmediate(t.gameObject); }
            var ours = new List<GameObject>(); foreach (Transform c in root.transform) ours.Add(c.gameObject);

            string mp = OUT + "/SeaNM.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(mp);
            if (!mat) { mat = new Material(src); AssetDatabase.CreateAsset(mat, mp); } else { mat.shader = src.shader; mat.CopyPropertiesFromMaterial(src); }
            Tune(mat); EditorUtility.SetDirty(mat);

            // near grid over the bay (as GraphicsPass.Sea: 25 m cells, quads wholly on land skipped), white vertex colour = opaque
            // mask 1, uv0 = game metres / 20, uv3 = 0 (no R.A.M flow)
            const double cell = 25; int nx = (int)((X1 - X0) / cell), nz = (int)((Z1 - Z0) / cell);
            var vs = new List<Vector3>(); var uvs = new List<Vector2>(); var ts = new List<int>();
            for (int j = 0; j <= nz; j++) for (int i = 0; i <= nx; i++) { double gx = X0 + i * cell, gz = Z0 + j * cell; vs.Add(W.U(gx, 0, gz)); uvs.Add(new Vector2((float)gx, (float)-gz) * .05f); }
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++)
                {
                    if (Tiles.Height(X0 + i * cell + cell / 2, Z0 + j * cell + cell / 2) > 4 && Tiles.Height(X0 + i * cell, Z0 + j * cell) > 3 && Tiles.Height(X0 + (i + 1) * cell, Z0 + (j + 1) * cell) > 3) continue;
                    int a = j * (nx + 1) + i; ts.AddRange(new[] { a, a + 1, a + nx + 1, a + 1, a + nx + 2, a + nx + 1 });
                }
            var mesh = new Mesh { name = "SeaNearNM", indexFormat = IndexFormat.UInt32 };
            var cols = new Color[vs.Count]; for (int i = 0; i < cols.Length; i++) cols[i] = Color.white;
            mesh.SetVertices(vs); mesh.SetUVs(0, uvs); mesh.SetColors(cols); var flow = new Vector4[vs.Count]; for (int i = 0; i < flow.Length; i++) flow[i] = Flow; mesh.SetUVs(3, flow); mesh.SetTriangles(ts, 0);
            mesh.RecalculateNormals();
            if (mesh.normals.Length > 0 && mesh.normals[0].y < 0) { var tri = mesh.triangles; for (int k = 0; k < tri.Length; k += 3) { int q = tri[k + 1]; tri[k + 1] = tri[k + 2]; tri[k + 2] = q; } mesh.triangles = tri; mesh.RecalculateNormals(); }
            mesh.RecalculateTangents(); mesh.RecalculateBounds();
            Save(mesh, OUT + "/SeaNearNM.asset");
            var gn = Obj(root, "sea near nm", mesh, mat);

            // ring around the near grid (4 quads), 15 km out, same height
            const double R = 15000; var fv = new List<Vector3>(); var ft = new List<int>();
            void Quad(double ax, double az, double bx, double bz)
            {
                int b = fv.Count;
                fv.Add(W.U(ax, 0, az)); fv.Add(W.U(bx, 0, az)); fv.Add(W.U(bx, 0, bz)); fv.Add(W.U(ax, 0, bz));
                ft.AddRange(new[] { b, b + 2, b + 1, b, b + 3, b + 2 });
            }
            double cx = (X0 + X1) / 2, cz = (Z0 + Z1) / 2;
            Quad(cx - R, cz - R, cx + R, Z0); Quad(cx - R, Z1, cx + R, cz + R); Quad(cx - R, Z0, X0, Z1); Quad(X1, Z0, cx + R, Z1);
            var ring = new Mesh { name = "SeaFarNM" }; ring.SetVertices(fv); ring.SetTriangles(ft, 0);
            var fuv = new Vector2[fv.Count]; var fc = new Color[fv.Count];
            for (int i = 0; i < fv.Count; i++) { var g = W.G(fv[i]); fuv[i] = new Vector2(g.x, -g.y) * .05f; fc[i] = Color.white; }
            ring.SetUVs(0, fuv); ring.SetColors(fc); var rflow = new Vector4[fv.Count]; for (int i = 0; i < rflow.Length; i++) rflow[i] = Flow; ring.SetUVs(3, rflow);
            ring.RecalculateNormals(); if (ring.normals[0].y < 0) { var tri = ring.triangles; for (int k = 0; k < tri.Length; k += 3) { int q = tri[k + 1]; tri[k + 1] = tri[k + 2]; tri[k + 2] = q; } ring.triangles = tri; ring.RecalculateNormals(); }
            ring.RecalculateTangents(); ring.RecalculateBounds();
            Save(ring, OUT + "/SeaFarNM.asset");
            var gf = Obj(root, "sea far nm", ring, mat);

            var sw = root.GetComponent<Magistrala.SeaMode>() ?? root.AddComponent<Magistrala.SeaMode>();
            sw.ours = ours.ToArray(); sw.nmNear = gn; sw.nmFar = gf; sw.useNM = DefaultNM;
            sw.Apply(DefaultNM);
            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            var s = $"[more] NM sea: near {mesh.vertexCount} v, ring, default {(DefaultNM ? "NM" : "ours")}";
            Debug.Log(s); return s;
        }

        static GameObject Obj(GameObject root, string name, Mesh m, Material mat)
        {
            var go = new GameObject(name); go.transform.SetParent(root.transform, false); go.isStatic = true;
            go.AddComponent<MeshFilter>().sharedMesh = m; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true;
            return go;
        }

        /// Kaštela bay: calm (wind sea of a few cm, no swell, no breaking shore waves), clear Adriatic colours
        public static void Tune(Material m)
        {
            void V(string p, float x, int c = 0) { if (!m.HasProperty(p)) return; var q = m.GetVector(p); q[c] = x; m.SetVector(p, q); }
            void F(string p, float x) { if (m.HasProperty(p)) m.SetFloat(p, x); }
            V("_GerstnerDirection_1", .0f);   // polar (shore) waves: none (they need R.A.M's shore direction in uv3)
            V("_Gerstner_1", .04f); V("_Gerstner_2", .05f);           // small waves
            V("_GerstnerDirection_4", .05f); V("_GerstnerDirection_5", .04f); // sea waves
            F("_Small_Wave_Shore_Height_Multiply", 1);
            if (m.HasProperty("_ShalowColor")) m.SetColor("_ShalowColor", new Color(.10f, .30f, .30f, 0));
            if (m.HasProperty("_DeepColor")) m.SetColor("_DeepColor", new Color(.012f, .06f, .12f, 0));
            F("_GlobalTiling", 1);
            // a soft sun path instead of glare (the owner: too bright at midday)
            F("_NMWaterSmoothness", .82f); F("_NMWaterSmoothness_far", .78f); F("_Water_Specular_Max", .35f); F("_Water_Specular_Min", .04f);
            F("_FarNormalPower", .25f);
        }

        static void Save(Object o, string path) { if (File.Exists(path)) AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(o, path); }
    }
}
