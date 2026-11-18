// Graphics pass (Marko 2026-11-17: sunny, vibrant Dalmatia): HDRI sky + matched warm sun, colour grading, asphalt / paint
// materials, depth-tinted sea, reflection probes along the D8, terrain detail scatter. CC0 sources in CREDITS.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Magistrala.EditorTools
{
    public static class GraphicsPass
    {
        const string GEN = WorldBuilder.GEN, PH = "Assets/Textures/PolyHaven/";
        const string HDRI = "Assets/Textures/Sky/kloofendal_43d_clear_puresky_4k.hdr";

        static Texture2D Tex(string path, bool normal = false, bool linear = false)
        {
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp != null)
            {
                bool dirty = false;
                if (normal && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; dirty = true; }
                if (linear && imp.sRGBTexture) { imp.sRGBTexture = false; dirty = true; }
                if (imp.anisoLevel < 8) { imp.anisoLevel = 8; dirty = true; }
                if (dirty) imp.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Mat(string name, string shader)
        {
            var sh = Shader.Find(shader); if (!sh) return null;
            string p = $"{GEN}/Materials/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(sh); AssetDatabase.CreateAsset(m, p); }
            m.shader = sh; m.enableInstancing = true;
            return m;
        }

        // ------------------------------------------------------------------ asphalt, paint
        public static Material AsphaltMaterial()
        {
            var m = Mat("AsphaltPBR", "Magistrala/Asphalt"); if (!m) return null;
            // fine aggregate: Poly Haven asphalt_track (clean, no cracks — asphalt_02's tar-sealed cracks repeated every 4 m in a
            // grid across the whole D8); asphalt_02 stays as the worn / cracked layer in patches. Albedo maps are normalised by
            // their linear means (measured: track 0.0112, 02 0.113), so _BaseColor is the mean albedo: the previous look's
            // 0.113 × (.86 .83 .78) ≈ sRGB (.35 .345 .335), a touch darker (.31: without the cracks' dark lines the road read lighter) — sun-baked light-medium warm grey (Marko's GTA V references)
            m.SetTexture("_BaseMap", Tex(PH + "asphalt_track_diff.jpg"));
            m.SetTexture("_BumpMap", Tex(PH + "asphalt_track_nor.jpg", true));
            m.SetTexture("_RoughMap", Tex(PH + "asphalt_track_rough.jpg", false, true));
            m.SetTexture("_WearMap", Tex(PH + "asphalt_02_diff.jpg"));
            m.SetTexture("_WearBump", Tex(PH + "asphalt_02_nor.jpg", true));
            m.SetFloat("_Tiling", 2.2f); m.SetFloat("_BaseMean", .0112f); m.SetFloat("_FineContrast", .8f);
            m.SetFloat("_WearTiling", 5f); m.SetFloat("_WearMean", .113f); m.SetFloat("_WearAmount", .5f); m.SetFloat("_WearScale", 38f);
            m.SetFloat("_BumpScale", .8f);
            m.SetColor("_BaseColor", new Color(.31f, .305f, .297f)); m.SetFloat("_SmoothMin", .1f); m.SetFloat("_SmoothMax", .3f); m.SetFloat("_SheenAmount", .3f); m.SetFloat("_TrackSmooth", .06f); m.SetFloat("_TrackDarken", .16f); m.SetFloat("_PatchAmount", .25f); m.SetFloat("_ColorVar", .12f);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// white road paint, crisp but worn: speckled with asphalt showing through (world uv × 0.2 → 5 m tile)
        public static Material PaintMaterial()
        {
            string tp = "Assets/Textures/paint_worn.png";
            if (!File.Exists(tp))
            {
                int N = 512; var t = new Texture2D(N, N, TextureFormat.RGB24, true);
                var rnd = new System.Random(7);
                for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
                    {
                        float n = Mathf.PerlinNoise(x * .045f, y * .045f) * .6f + Mathf.PerlinNoise(x * .21f + 50, y * .21f) * .4f;
                        float wear = Mathf.Clamp01((n - .62f) * 5f) * .55f + (rnd.NextDouble() < .025 ? .35f : 0);
                        float v = Mathf.Lerp(.95f, .42f, wear);
                        t.SetPixel(x, y, new Color(v, v, v * .99f));
                    }
                File.WriteAllBytes(tp, t.EncodeToPNG()); AssetDatabase.ImportAsset(tp);
            }
            // (albedo ≈ 0.5 linear like real road paint: .93 read as glowing white strips against the 0.1 asphalt in ACES)
            var m = WorldBuilder.Lit("Paint", new Color(.8f, .8f, .78f), .3f, 0, null);
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(tp));
            EditorUtility.SetDirty(m);
            return m;
        }

        // ------------------------------------------------------------------ sky, sun, grading
        [MenuItem("Magistrala/Grafika: nebo + svjetlo + boje")]
        public static void Sky()
        {
            var imp = (TextureImporter)AssetImporter.GetAtPath(HDRI);
            imp.textureShape = TextureImporterShape.Texture2D; imp.isReadable = true; imp.mipmapEnabled = false;
            imp.maxTextureSize = 4096; imp.textureCompression = TextureImporterCompression.Uncompressed; imp.wrapModeU = TextureWrapMode.Repeat; imp.wrapModeV = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(HDRI);
            // brightest pixel = the sun (rows from the bottom in Unity)
            var px = tex.GetPixels(); int W = tex.width, H = tex.height, best = 0; float bl = 0;
            for (int i = 0; i < px.Length; i++) { float l = px[i].r + px[i].g + px[i].b; if (l > bl) { bl = l; best = i; } }
            float u = (best % W + .5f) / W, v = (best / W + .5f) / H;
            // Skybox/Panoramic: u = 0.5 - atan2(z, x) / 2π, v = 1 - acos(y) / π
            float lon = (.5f - u) * 2 * Mathf.PI, yy = Mathf.Cos((1 - v) * Mathf.PI), rr = Mathf.Sqrt(Mathf.Max(0, 1 - yy * yy));
            var dTex = new Vector3(Mathf.Cos(lon) * rr, yy, Mathf.Sin(lon) * rr);
            float elev = Mathf.Asin(yy) * Mathf.Rad2Deg;
            // keep the sun where the probe had it (from the south-south-west, over the sea), at the HDRI's elevation
            const float yaw = 28f;
            var sunRot = Quaternion.Euler(elev, yaw, 0);
            var dWorld = -(sunRot * Vector3.forward);
            float rot = (Mathf.Atan2(dTex.z, dTex.x) - Mathf.Atan2(dWorld.z, dWorld.x)) * Mathf.Rad2Deg;
            imp.isReadable = false; imp.textureCompression = TextureImporterCompression.CompressedHQ; imp.SaveAndReimport();

            var sky = Mat("SkyHDRI", "Skybox/Panoramic");
            sky.SetTexture("_MainTex", AssetDatabase.LoadAssetAtPath<Texture2D>(HDRI));
            sky.SetFloat("_Mapping", 1); sky.SetFloat("_ImageType", 0); sky.SetFloat("_Rotation", (rot % 360 + 360) % 360);
            sky.SetFloat("_Exposure", .9f); sky.SetColor("_Tint", new Color(.5f, .5f, .5f)); // (Tint .5 = neutral)
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;

            var sun = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).First(l => l.type == LightType.Directional);
            sun.transform.rotation = sunRot;
            GraphicsSettings.lightsUseLinearIntensity = true; GraphicsSettings.lightsUseColorTemperature = true;
            sun.useColorTemperature = true; sun.colorTemperature = 5600; sun.color = new Color(1f, .97f, .92f);
            sun.intensity = 2.9f; sun.shadowStrength = .8f;
            // ambient: trilight (the pure-sky HDRI's lower half is near black: undersides and the footbridge went black); reflections stay the HDRI
            RenderSettings.ambientMode = AmbientMode.Trilight; RenderSettings.ambientSkyColor = new Color(.62f, .74f, .95f) * 1.05f;
            RenderSettings.ambientEquatorColor = new Color(.66f, .66f, .62f); RenderSettings.ambientGroundColor = new Color(.42f, .36f, .28f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox; RenderSettings.reflectionIntensity = 1f;
            // light aerial haze (sea / Kozjak), never grey: pale sky blue
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = .00006f;
            RenderSettings.fogColor = new Color(.72f, .78f, .84f); // warm pale haze: hills and sea fade into it
            DynamicGI.UpdateEnvironment();

            // grading: vibrant, warm highlights, lifted blues / greens, no crushed blacks
            var vol = UnityEngine.Object.FindFirstObjectByType<Volume>();
            var prof = vol.sharedProfile;
            T Get<T>() where T : VolumeComponent { if (!prof.TryGet<T>(out var c)) { c = prof.Add<T>(true); AssetDatabase.AddObjectToAsset(c, prof); } c.active = true; return c; }
            Get<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var ca = Get<ColorAdjustments>(); ca.postExposure.Override(.22f); ca.contrast.Override(6f); ca.saturation.Override(14f); ca.colorFilter.Override(Color.white);
            var wb = Get<WhiteBalance>(); wb.temperature.Override(7f); wb.tint.Override(-1f);
            var smh = Get<ShadowsMidtonesHighlights>();
            smh.shadows.Override(new Vector4(.98f, 1.0f, 1.06f, .04f)); smh.midtones.Override(new Vector4(1f, 1f, 1f, 0)); smh.highlights.Override(new Vector4(1.03f, 1.0f, .96f, 0));
            var lgg = Get<LiftGammaGain>(); lgg.lift.Override(new Vector4(1f, 1f, 1f, .02f)); lgg.gain.Override(new Vector4(1.02f, 1.0f, .97f, 0));
            var bl2 = Get<Bloom>(); bl2.threshold.Override(1.45f); bl2.intensity.Override(.22f); bl2.scatter.Override(.6f);
            var vg = Get<Vignette>(); vg.intensity.Override(.12f);
            EditorUtility.SetDirty(prof);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log($"[Magistrala] HDRI sun u {u:F3} v {v:F3} elev {elev:F1}°, sky rotation {rot:F1}°");
        }

        // ------------------------------------------------------------------ sea: depth-tinted grid near the coast + deep plane
        [MenuItem("Magistrala/Grafika: more")]
        public static void Sea()
        {
            var mat = Mat("SeaDepth", "Magistrala/Sea"); if (!mat) { Debug.LogError("Sea shader missing"); return; }
            mat.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/water_n.png"));
            EditorUtility.SetDirty(mat);
            var root = WorldBuilder.Root("Sea");
            Color shallow = new Color(.1f, .62f, .6f), mid = new Color(.04f, .33f, .46f), deep = new Color(.015f, .12f, .27f), foam = new Color(.85f, .88f, .86f);
            Color Tint(double h) { float d = (float)-h; if (d < .35f) return Color.Lerp(foam, shallow, Mathf.Clamp01((d + .2f) / .55f)); return d < 6 ? Color.Lerp(shallow, mid, Mathf.Clamp01(d / 6)) : Color.Lerp(mid, deep, Mathf.Clamp01((d - 6) / 20)); }
            // near grid: game x -9300 … 1200, z -6000 … -2800 (the bay), 25 m cells
            double x0 = -9300, x1 = 1200, z0 = -6000, z1 = -2800, cell = 25;
            int nx = (int)((x1 - x0) / cell), nz = (int)((z1 - z0) / cell);
            var vs = new List<Vector3>(); var cs = new List<Color>(); var ts = new List<int>();
            for (int j = 0; j <= nz; j++) for (int i = 0; i <= nx; i++) { double gx = x0 + i * cell, gz = z0 + j * cell; vs.Add(W.U(gx, 0, gz)); cs.Add(Tint(Tiles.Height(gx, gz))); }
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++)
                {
                    int a = j * (nx + 1) + i;
                    // skip quads wholly on land (well above sea level)
                    if (Tiles.Height(x0 + i * cell + cell / 2, z0 + j * cell + cell / 2) > 4 && Tiles.Height(x0 + i * cell, z0 + j * cell) > 3 && Tiles.Height(x0 + (i + 1) * cell, z0 + (j + 1) * cell) > 3) continue;
                    ts.AddRange(new[] { a, a + 1, a + nx + 1, a + 1, a + nx + 2, a + nx + 1 });
                }
            var mesh = new Mesh { name = "SeaNear", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vs); mesh.SetColors(cs); mesh.SetTriangles(ts, 0); mesh.RecalculateNormals();
            // (winding: game z flipped → make normals point up)
            if (mesh.normals.Length > 0 && mesh.normals[0].y < 0) { var tri = mesh.triangles; for (int k = 0; k < tri.Length; k += 3) { int q = tri[k + 1]; tri[k + 1] = tri[k + 2]; tri[k + 2] = q; } mesh.triangles = tri; mesh.RecalculateNormals(); }
            mesh.RecalculateBounds();
            string mp = GEN + "/Sea/SeaNear.asset"; if (File.Exists(mp)) AssetDatabase.DeleteAsset(mp); AssetDatabase.CreateAsset(mesh, mp);
            var go = new GameObject("sea near"); go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
            // far plane, deep colour, a little lower
            var far = new Mesh { name = "SeaFar" }; float S = 30000; int n = 40;
            var fv = new List<Vector3>(); var fc = new List<Color>(); var ft = new List<int>();
            for (int j = 0; j <= n; j++) for (int i = 0; i <= n; i++) { fv.Add(new Vector3(-S / 2 + S * i / n, -.08f, -S / 2 + S * j / n)); fc.Add(deep); }
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) { int a = j * (n + 1) + i; ft.AddRange(new[] { a, a + n + 1, a + 1, a + 1, a + n + 1, a + n + 2 }); }
            far.SetVertices(fv); far.SetColors(fc); far.SetTriangles(ft, 0); far.RecalculateNormals(); far.RecalculateBounds();
            string fp = GEN + "/Sea/SeaFar.asset"; if (File.Exists(fp)) AssetDatabase.DeleteAsset(fp); AssetDatabase.CreateAsset(far, fp);
            var fgo = new GameObject("sea far"); fgo.transform.SetParent(root.transform, false);
            fgo.AddComponent<MeshFilter>().sharedMesh = far; var fr = fgo.AddComponent<MeshRenderer>(); fr.sharedMaterial = mat; fr.shadowCastingMode = ShadowCastingMode.Off;
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------ reflection probes along the D8 (asphalt catches sky, trees, houses)
        [MenuItem("Magistrala/Grafika: refleksije")]
        public static void Probes()
        {
            var root = WorldBuilder.Root("Probes");
            var path = TruckSetup.EastboundPath();
            if (!AssetDatabase.IsValidFolder(GEN + "/Probes")) AssetDatabase.CreateFolder(GEN, "Probes");
            var models = WorldData.Models(); float acc = 999; Vector2 prev = path[0]; int k = 0;
            foreach (var p in path)
            {
                acc += Vector2.Distance(p, prev); prev = p;
                if (acc < 320) continue; acc = 0;
                double y = double.NaN; foreach (var m in models) { y = m.Surface(p.x, p.y); if (!double.IsNaN(y)) break; }
                if (double.IsNaN(y)) continue;
                var go = new GameObject("probe " + k); go.transform.SetParent(root.transform, false);
                go.transform.position = W.U(p.x, y + 6, p.y);
                var rp = go.AddComponent<ReflectionProbe>();
                rp.mode = ReflectionProbeMode.Baked; rp.size = new Vector3(360, 120, 360); rp.resolution = 128; rp.hdr = true;
                rp.boxProjection = false; rp.blendDistance = 30; rp.importance = 1; rp.farClipPlane = 1500; rp.shadowDistance = 150;
                Lightmapping.BakeReflectionProbe(rp, $"{GEN}/Probes/probe{k}.exr");
                k++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Magistrala] {k} reflection probes");
        }

        // ------------------------------------------------------------------ terrain details near the camera: dry grass tufts, stones, maquis
        static GameObject Flat(string id, string src)
        {
            string dir = GEN + "/Plants", mp = $"{dir}/detail_{id}.mesh.asset", pp = $"{dir}/detail_{id}.prefab";
            var g = AssetDatabase.LoadAssetAtPath<GameObject>(src);
            var combine = new List<CombineInstance>(); var mats = new List<Material>();
            foreach (var mf in g.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<MeshRenderer>(); if (!r || !mf.sharedMesh) continue;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) { combine.Add(new CombineInstance { mesh = mf.sharedMesh, subMeshIndex = s, transform = g.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix }); mats.Add(r.sharedMaterials[Mathf.Min(s, r.sharedMaterials.Length - 1)]); }
            }
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(mp);
            if (!mesh) { mesh = new Mesh { name = "detail_" + id }; AssetDatabase.CreateAsset(mesh, mp); }
            mesh.Clear(); mesh.indexFormat = IndexFormat.UInt32; mesh.CombineMeshes(combine.ToArray(), false, true); mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssets();
            var go = new GameObject("detail_" + id); go.AddComponent<MeshFilter>().sharedMesh = mesh; go.AddComponent<MeshRenderer>().sharedMaterials = mats.ToArray();
            foreach (var mm in mats) if (mm) mm.enableInstancing = true;
            var pf = PrefabUtility.SaveAsPrefabAsset(go, pp); UnityEngine.Object.DestroyImmediate(go);
            return pf;
        }

        [MenuItem("Magistrala/Grafika: detalji terena")]
        public static void Details()
        {
            var grass = Flat("grass", "Assets/Models/Plants/grass.glb");
            var stone = Flat("stone", "Assets/Models/Plants/rock_a.glb");
            var maquis = Flat("maquis", "Assets/Models/Plants/shrub_b.glb");
            DetailPrototype P(GameObject g, float min, float max) => new DetailPrototype
            {
                usePrototypeMesh = true, prototype = g, renderMode = DetailRenderMode.VertexLit, useInstancing = true,
                minWidth = min, maxWidth = max, minHeight = min, maxHeight = max, healthyColor = Color.white, dryColor = new Color(.92f, .88f, .78f), noiseSpread = .3f,
            };
            var protos = new[] { P(grass, .7f, 1.3f), P(stone, .08f, .22f), P(maquis, .45f, .9f) };
            var models = WorldData.Models(); int total = 0;
            foreach (var t in UnityEngine.Object.FindObjectsByType<UnityEngine.Terrain>(FindObjectsSortMode.None))
            {
                var parts = t.name.Split(' ')[1].Split(','); int tx = int.Parse(parts[0]), tz = int.Parse(parts[1]);
                bool core = tx >= WorldBuilder.TX0 && tx <= WorldBuilder.TX1 && tz >= WorldBuilder.TZ0 && tz <= WorldBuilder.TZ1;
                var td = t.terrainData;
                if (!core) { td.detailPrototypes = new DetailPrototype[0]; continue; }
                const int R = 256; // 4 m cells
                td.SetDetailResolution(R, 16); td.detailPrototypes = protos;
                var layers = new int[3][,]; for (int k = 0; k < 3; k++) layers[k] = new int[R, R];
                var A = td.GetAlphamaps(0, 0, td.alphamapWidth, td.alphamapHeight); int ar = td.alphamapWidth;
                var rnd = new System.Random(tx * 31 + tz);
                for (int r = 0; r < R; r++)
                    for (int c = 0; c < R; c++)
                    {
                        double gx = tx * 1024 + (c + .5) * 4, gz = (tz + 1) * 1024 - (r + .5) * 4;
                        double h = Tiles.Height(gx, gz); if (h < .8) continue;
                        if (WorldData.SurfDist(models, gx, gz) < 2.5 || WorldData.FootDist(models, gx, gz) < 1.5 || StreetsBuilder.Covered(gx, gz, 1)) continue; // (graph streets: Build streets' cover mask)
                        int ai = Mathf.Min(ar - 1, r * ar / R), aj = Mathf.Min(ar - 1, c * ar / R);
                        float grassW = A[ai, aj, 0], soilW = A[ai, aj, 1], rockW = A[ai, aj, 2];
                        float n = Mathf.PerlinNoise((float)gx * .05f + 300, (float)gz * .05f + 300);
                        layers[0][r, c] = (int)Math.Round(grassW * (1.2f + 2.5f * n) * (rnd.NextDouble() * .6 + .7));
                        layers[1][r, c] = rnd.NextDouble() < (rockW * .7 + soilW * .25 + .05) ? 1 + (rnd.NextDouble() < rockW ? 1 : 0) : 0;
                        layers[2][r, c] = rnd.NextDouble() < (grassW * .06 + rockW * .05) * (n + .3) ? 1 : 0;
                        total += layers[0][r, c] + layers[1][r, c] + layers[2][r, c];
                    }
                for (int k = 0; k < 3; k++) td.SetDetailLayer(0, 0, k, layers[k]);
                t.detailObjectDistance = 140; t.detailObjectDensity = 1f;
                EditorUtility.SetDirty(td);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[Magistrala] terrain details: {total}");
        }

        /// fixed camera spots for before / after comparison: truck parked on the D8 at game x, chase camera pose
        public static string Shot(float gameX, string file, bool aerial = false)
        {
            var cam = Camera.main; var chase = cam.GetComponent<TruckChaseCamera>();
            if (aerial)
            {
                if (chase) chase.enabled = false;
                cam.transform.position = W.U(-6600, 420, -6150); cam.transform.LookAt(W.U(-4300, 40, -5350));
            }
            else
            {
                var ap = UnityEngine.Object.FindFirstObjectByType<Magistrala.Autopilot>(); var t = ap.transform;
                int best = 0; for (int i = 0; i < ap.path.Length; i++) if (Mathf.Abs(ap.path[i].x - 4200 - gameX) < Mathf.Abs(ap.path[best].x - 4200 - gameX)) best = i;
                var p = ap.path[best]; var dir = ap.path[Mathf.Min(best + 3, ap.path.Length - 1)] - p; dir.y = 0;
                float y = -999; foreach (var h in Physics.RaycastAll(p + Vector3.up * 100, Vector3.down, 200)) if (!h.transform.IsChildOf(t)) y = Mathf.Max(y, h.point.y);
                var rb = t.GetComponent<Rigidbody>(); rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                rb.position = new Vector3(p.x, y + .98f, p.z); rb.rotation = Quaternion.LookRotation(dir); t.SetPositionAndRotation(rb.position, rb.rotation);
                ap.drive = false; var tc = t.GetComponent<TruckController>(); tc.externalInput = true; tc.extThrottle = 0; tc.extBrake = 0; tc.extHandbrake = true; tc.extSteer = 0;
                if (chase) chase.enabled = false;
                cam.transform.position = t.TransformPoint(new Vector3(0, 4.4f, -10.5f)); cam.transform.LookAt(t.TransformPoint(new Vector3(0, 1.2f, 8f)));
            }
            return file;
        }
    }
}
