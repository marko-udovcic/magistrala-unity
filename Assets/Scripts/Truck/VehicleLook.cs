// Player vehicles' exterior look (Marko 25. 11., "koliko blizu BeamNG-a"): the driven vehicles (Atego, Kombi, Galeb) are
// detailed, traffic stays cheap. Applied once per vehicle at TruckLights.Start, on the renderers' material instances (the GLB
// assets stay as imported). Per material name of the three GLBs:
//   paint     clear coat (glTFast "-Clearcoat" graph): firm vehicles a fresh deep coat, the Galeb an old dull one
//   glass     slightly tinted, mirror-smooth, the reflection kept at low alpha (Preserve Specular): see-through to the cab
//   chrome / alu / rims   metal, smooth;   plastics / rubber   satin, not shiny
//   lamps     L_* materials (Atego) get emissive; the Kombi's "Phare" and the Galeb's lamp texels (lamps baked in the body atlas)
//             are split into their own submeshes L_head / L_brake / L_blink* (emissive = their own albedo) so TruckLights drives them
//   contact shadow   a soft dark quad under the body (Magistrala/ContactShadow, multiply): grounds the vehicle on every quality level
// Reflections: the scene's baked reflection probes (17, swapped for the sunset / rain sets by LightPreset / WeatherSystem) — no
// realtime probe following the vehicle: even one 128 px face per frame re-culls and redraws this scene (~2.4 k draws) once more
// per frame, the main-thread cost alone would eat the +0.7 ms budget, and the baked set already moves with the time of day. Shader
// variants the runtime keywords need are held by the materials in Resources/Vehicles/Look (Clearcoat + occlusion for the Galeb body).
// Measuring: -vozlook 0 turns all of this off (A/B in one build).
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala
{
    public static class VehicleLook
    {
        public static readonly bool Off = Arg("-vozlook") == "0";

        struct Profile { public float paintRough, coat, coatRough, boxCoat, glassA; public Color glassTint; }
        static Profile For(string id) => id switch
        {
            // Mate's old TN van: the paint has lost its gloss (chalky white, a thin worn coat)
            "galeb" => new Profile { paintRough = .55f, coat = .35f, coatRough = .32f, boxCoat = .3f, glassA = .22f, glassTint = new Color(.55f, .6f, .6f) },
            // the firm's van: a clean modern white with a deep coat
            "kombi" or "l4h1" => new Profile { paintRough = .32f, coat = 1f, coatRough = .04f, boxCoat = .6f, glassA = .3f, glassTint = new Color(.32f, .36f, .38f) },
            // Atego: clean cab, the box panels a plainer gloss
            _ => new Profile { paintRough = .28f, coat = 1f, coatRough = .05f, boxCoat = .55f, glassA = .28f, glassTint = new Color(.35f, .4f, .42f) },
        };

        static readonly int BaseColor = Shader.PropertyToID("baseColorFactor"), BaseTex = Shader.PropertyToID("baseColorTexture"),
            Metal = Shader.PropertyToID("metallicFactor"), Rough = Shader.PropertyToID("roughnessFactor"),
            Coat = Shader.PropertyToID("clearcoatFactor"), CoatRough = Shader.PropertyToID("clearcoatRoughnessFactor"),
            Emis = Shader.PropertyToID("emissiveFactor"), EmisTex = Shader.PropertyToID("emissiveTexture");

        public static void Apply(Transform root, string id)
        {
            if (Off || !root) return;
            var P = For(id);
            Material coatSrc = null, coatOccSrc = null, glassSrc = null;
            var look = Resources.LoadAll<Material>("Vehicles/Look");
            foreach (var m in look) { if (m.name == "PaintCoat") coatSrc = m; else if (m.name == "PaintCoatOcc") coatOccSrc = m; else if (m.name == "Glass") glassSrc = m; }
            int nPaint = 0, nGlass = 0, nMetal = 0, nPlastic = 0, nLamp = 0; Material lampTemplate = null;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is not MeshRenderer) continue;
                if (r.name == "windscreen_fx") continue; // WindscreenFx's own material
                // lamps baked into a textured part: their own submeshes first (the material array grows)
                if (id == "kombi" && r.name.StartsWith("Phare")) { nLamp += SplitByPosition(r, root); lampTemplate = r.sharedMaterials[0]; }
                if (id == "galeb" && r.name == "body") nLamp += SplitGalebLamps(r, root);
                var mats = r.materials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m) continue;
                    string n = m.name.Replace(" (Instance)", "");
                    if (id == "l4h1") n = L4H1(m, n);
                    if (n.StartsWith("i_") || n.StartsWith("v2_") || n.StartsWith("v3_") || n.StartsWith("M_") || n.StartsWith("cargo") || n.StartsWith("pallet")) continue; // the cab interior / load
                    switch (n)
                    {
                        case "paint": case "firm_white_paint": case "galeb_body":
                        {
                            var src = m.IsKeywordEnabled("_OCCLUSION") ? coatOccSrc : coatSrc;
                            if (!m.shader.name.EndsWith("Clearcoat") && src) { mats[i] = m = Recoat(m, src); changed = true; }
                            m.SetFloat(Rough, P.paintRough); m.SetFloat(Coat, P.coat); m.SetFloat(CoatRough, P.coatRough);
                            if (n == "galeb_body") m.SetFloat(Metal, 0); // (its MR texture's blue = metal: the old paint is no metal)
                            if (id == "galeb") m.SetColor(BaseColor, new Color(.93f, .92f, .88f)); // a faintly yellowed white
                            nPaint++; break;
                        }
                        case "box_panel": case "box_panel_w":
                            m.SetFloat(Coat, P.boxCoat); m.SetFloat(CoatRough, .12f); m.SetFloat(Rough, .38f); nPaint++; break;
                        case "glass": case "Vitre": case "lens_clear":
                            if (glassSrc) { mats[i] = m = Recoat(m, glassSrc); changed = true; }
                            Glass(m, n == "lens_clear" ? .12f : P.glassA, n == "lens_clear" ? new Color(.9f, .92f, .95f) : P.glassTint); nGlass++; break;
                        case "chrome": case "reflector":
                            m.SetFloat(Metal, 1); m.SetFloat(Rough, .06f); nMetal++; break;
                        case "alu": case "rim": case "plain_grille_satin": case "step_plate":
                            m.SetFloat(Metal, 1); m.SetFloat(Rough, id == "galeb" ? .4f : .25f); nMetal++; break;
                        case "mirror": case "outside_mirror_glass":
                            m.SetFloat(Metal, 1); m.SetFloat(Rough, .03f); nMetal++; break;
                        case "black_plastic": case "black_trim": case "grille_dark": case "firm_dark_trim": case "chassis":
                            m.SetFloat(Metal, 0); m.SetFloat(Rough, id == "galeb" ? .7f : .5f);
                            if (!m.GetTexture(BaseTex)) m.SetColor(BaseColor, new Color(.035f, .037f, .04f));
                            nPlastic++; break;
                        case "mudflap": case "tn_rubber": case "tyre":
                            m.SetFloat(Metal, 0); m.SetFloat(Rough, .85f); nPlastic++; break;
                    }
                }
                if (changed) r.materials = mats;
            }
            if (id == "kombi") nLamp += AddTailLamps(root, lampTemplate);
            ContactShadow(root);
            Debug.Log($"[vozlook] {id}: lak {nPaint}, stakla {nGlass}, metal {nMetal}, plastika {nPlastic}, svjetla {nLamp}");
        }

        /// the L4H1 van's materials (BalkanDriver assets-src/kombi-l4h1 k2_build.py) → the shared look classes; its lamp materials
        /// are renamed to TruckLights' L_* (Lights_Front head, Lights_Brake tail / brake, Lights_Reverse, Lights_Indicator L_blink)
        static string L4H1(Material m, string n)
        {
            switch (n)
            {
                case "Lights_Front": m.name = "L_head"; return "L_head";
                case "Lights_Brake": m.name = "L_brake"; return "L_brake";
                case "Lights_Reverse": m.name = "L_reverse"; return "L_reverse";
                case "Lights_Indicator": m.name = "L_blink"; return "L_blink";
                case "Body_Paint": return "paint";
                case "Box_White": return "box_panel";
                case "Glass": return "glass";
                case "Lamp_Lens": return "lens_clear";
                case "Chrome": case "Lamp_Reflector": return "chrome";
                case "Alu_Satin": case "Rim": case "Silver": return "alu";
                case "Mirror_Glass": return "mirror";
                case "Black_Plastic": case "Chassis_Black": return "black_plastic";
                case "Tyre": return "tyre";
                default: return "i_" + n; // interior, gauges, plates, livery: as modelled
            }
        }

        static readonly string[] BlendState = { "_Surface", "_Blend", "_BlendModePreserveSpecular", "_SrcBlend", "_DstBlend", "_SrcBlendAlpha", "_DstBlendAlpha", "_ZWrite", "_AlphaClip" };
        static Material Recoat(Material m, Material src)
        {
            var c = new Material(src) { name = m.name.Replace(" (Instance)", "") };
            c.CopyMatchingPropertiesFromMaterial(m);
            // the template's blend state stays (the glass template: alpha + preserve specular)
            foreach (var p in BlendState) if (src.HasProperty(p)) c.SetFloat(p, src.GetFloat(p));
            c.renderQueue = src.renderQueue;
            return c;
        }

        static void Glass(Material m, float a, Color tint)
        {
            var c = tint; c.a = a;
            // (the template Resources/Vehicles/Look/Glass is alpha blended with "Preserve Specular Lighting": the reflection keeps
            // its full strength over the low alpha, the albedo is premultiplied)
            m.SetColor(BaseColor, c); m.SetFloat(Metal, 0); m.SetFloat(Rough, .02f);
        }

        // ------------------------------------------------------------------ lamps baked into textured parts
        static Material Lamp(Material src, string name)
        {
            var m = new Material(src.shader) { name = name };
            m.SetTexture(BaseTex, src.GetTexture(BaseTex));
            m.SetTexture(EmisTex, src.GetTexture(BaseTex));
            m.SetColor(BaseColor, Color.white); m.SetFloat(Metal, 0); m.SetFloat(Rough, .12f);
            m.SetColor(Emis, Color.black); m.EnableKeyword("_EMISSIVE");
            return m;
        }

        /// the Kombi's lamp unit ("Phare": head lamps in front, tail lamps behind) split front / rear by the model's +Z
        static int SplitByPosition(Renderer r, Transform root)
        {
            var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) return 0;
            var src = mf.sharedMesh; var v = src.vertices; var mats = r.sharedMaterials;
            var toRoot = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
            var keep = new List<int>(); var front = new List<int>(); var rear = new List<int>();
            var subs = new List<int[]>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var t = src.GetTriangles(s);
                if (s != 0) { subs.Add(t); continue; }
                for (int i = 0; i < t.Length; i += 3)
                {
                    float z = toRoot.MultiplyPoint3x4((v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f).z;
                    (z > 0 ? front : rear).AddRange(new[] { t[i], t[i + 1], t[i + 2] });
                }
            }
            if (front.Count == 0) return 0;
            return Rebuild(r, mf, src, mats, new List<(List<int> tri, Material mat)> { (front, Lamp(mats[0], "L_head")), (rear, Lamp(mats[0], "L_brake")) }, subs, keep);
        }

        /// the Galeb's lamps are texels of the body atlas (bottom row of the atlas, the UVs wrap: frac(uv) v < 0.11; amber
        /// u < 0.137, clear 0.137..0.25, red 0.25..0.42): clear in front = head lamps, red behind = tail / brake, amber in front =
        /// indicators (unlit; L_blink). Measured from the GLB (editor scan of the atlas colours by position, 25. 11.).
        static int SplitGalebLamps(Renderer r, Transform root)
        {
            var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) return 0;
            var src = mf.sharedMesh; var mats = r.sharedMaterials; var uv = src.uv; var v = src.vertices;
            if (uv == null || uv.Length == 0) return 0;
            int body = -1; for (int s = 0; s < mats.Length; s++) if (mats[s] && mats[s].name.StartsWith("galeb_body")) body = s;
            if (body < 0) return 0;
            var toRoot = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
            var subs = new List<int[]>(); var keep = new List<int>();
            List<int> head = new List<int>(), tail = new List<int>(), blink = new List<int>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var t = src.GetTriangles(s);
                if (s != body) { subs.Add(t); continue; }
                for (int i = 0; i < t.Length; i += 3)
                {
                    var c = (uv[t[i]] + uv[t[i + 1]] + uv[t[i + 2]]) / 3f; c.x -= Mathf.Floor(c.x); c.y -= Mathf.Floor(c.y);
                    List<int> to = keep;
                    if (c.y < .11f && c.x < .42f)
                    {
                        bool front = toRoot.MultiplyPoint3x4((v[t[i]] + v[t[i + 1]] + v[t[i + 2]]) / 3f).z > 0;
                        if (c.x < .137f) { if (front) to = blink; }
                        else if (c.x < .25f) { if (front) to = head; }
                        else if (!front) to = tail;
                    }
                    to.Add(t[i]); to.Add(t[i + 1]); to.Add(t[i + 2]);
                }
            }
            subs.Insert(body, keep.ToArray());
            if (head.Count + tail.Count == 0) return 0;
            return Rebuild(r, mf, src, mats, new List<(List<int>, Material)> { (head, Lamp(mats[body], "L_head")), (tail, Lamp(mats[body], "L_brake")), (blink, Lamp(mats[body], "L_blink")) }, subs, null);
        }

        /// the Kombi's model has no tail lamps (dark rear pillars): two red lenses on the rear corners, at the pillars' rearmost
        /// face (from the body's vertices), as L_brake (tail glow with the lights, brighter braking)
        static int AddTailLamps(Transform root, Material template)
        {
            if (!template) return 0;
            float minZ = float.MaxValue, maxX = 0, minY = float.MaxValue, maxY = float.MinValue, bodyMinZ = float.MaxValue;
            var all = new List<Vector3>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || !mf.sharedMesh.isReadable || mf.name.StartsWith("wheel") || mf.name == "windscreen_fx" || mf.name.StartsWith("mglass")) continue;
                var M = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var q in mf.sharedMesh.vertices) { var p = M.MultiplyPoint3x4(q); minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); bodyMinZ = Mathf.Min(bodyMinZ, p.z); all.Add(p); }
            }
            if (all.Count == 0) return 0;
            // lamp band: a third to a half of the height; the body's half width at the rear (not the mirrors), then the rearmost
            // point of the corner pillars in that band
            float y0 = minY + (maxY - minY) * .33f, y1 = minY + (maxY - minY) * .52f;
            float xL = 0, xR = 0; // each side on its own (the body is not exactly centred on x = 0)
            foreach (var p in all) if (p.z < bodyMinZ + .4f && p.y > y0 && p.y < y1) { xL = Mathf.Min(xL, p.x); xR = Mathf.Max(xR, p.x); }
            maxX = Mathf.Min(-xL, xR);
            foreach (var p in all) if ((p.x < xL + .2f || p.x > xR - .2f) && p.y > y0 && p.y < y1 && p.z < bodyMinZ + .6f) minZ = Mathf.Min(minZ, p.z);
            if (minZ == float.MaxValue || maxX <= 0) return 0;
            var mat = new Material(template.shader) { name = "L_brake" };
            mat.SetColor(BaseColor, new Color(.55f, .03f, .03f)); mat.SetFloat(Metal, 0); mat.SetFloat(Rough, .1f);
            mat.SetColor(Emis, Color.black); mat.EnableKeyword("_EMISSIVE");
            var mesh = new Mesh { name = "tail_lamp" };
            float w = .065f, h = (y1 - y0) * .42f;
            mesh.vertices = new[] { new Vector3(-w, -h, 0), new Vector3(w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 }; // facing -Z (backwards)
            mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            mesh.RecalculateBounds();
            var model = root.Find("Model") ?? root;
            foreach (float sx in new[] { -1f, 1f })
            {
                var go = new GameObject(sx < 0 ? "tail_L" : "tail_R"); go.transform.SetParent(model, true);
                go.transform.SetPositionAndRotation(root.TransformPoint(new Vector3(sx < 0 ? xL + .11f : xR - .09f, (y0 + y1) * .5f, minZ - .015f)), root.rotation);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
            }
            return 2;
        }

        static int Rebuild(Renderer r, MeshFilter mf, Mesh src, Material[] mats, List<(List<int> tri, Material mat)> extra, List<int[]> subs, List<int> keep)
        {
            var m = Object.Instantiate(src); m.name = src.name + " (lamps)";
            var allSubs = new List<int[]>();
            if (keep != null) allSubs.Add(keep.ToArray()); // SplitByPosition: submesh 0 was all lamp
            allSubs.AddRange(subs);
            var allMats = new List<Material>();
            if (keep != null) allMats.Add(mats[0]);
            for (int i = keep != null ? 1 : 0; i < mats.Length; i++) allMats.Add(mats[i]);
            int n = 0;
            foreach (var e in extra) { if (e.tri.Count == 0) continue; allSubs.Add(e.tri.ToArray()); allMats.Add(e.mat); n += e.tri.Count / 3; }
            m.subMeshCount = allSubs.Count;
            for (int s = 0; s < allSubs.Count; s++) m.SetTriangles(allSubs[s], s, false);
            m.RecalculateBounds();
            mf.sharedMesh = m;
            r.sharedMaterials = allMats.ToArray();
            return n;
        }

        // ------------------------------------------------------------------ contact shadow
        static void ContactShadow(Transform root)
        {
            var mat = Resources.Load<Material>("Vehicles/Look/ContactShadow"); if (!mat) return;
            var model = root.Find("Model") ?? root;
            // the body's footprint in the root frame (renderers of the model, wheels included), ground = lowest wheel point
            var b = new Bounds(); bool any = false;
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name == "windscreen_fx") continue;
                var lb = r.localBounds; var M = root.worldToLocalMatrix * r.transform.localToWorldMatrix;
                for (int i = 0; i < 8; i++)
                {
                    var p = M.MultiplyPoint3x4(lb.center + Vector3.Scale(lb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            if (!any) return;
            var go = new GameObject("contact_shadow"); go.transform.SetParent(model, false);
            go.layer = root.gameObject.layer;
            var w = b.size.x * .58f + .25f; var l = b.size.z * .54f + .3f; var y = b.min.y + .03f;
            var mesh = new Mesh { name = "contact_shadow" };
            mesh.vertices = new[] { new Vector3(-w, 0, -l), new Vector3(w, 0, -l), new Vector3(-w, 0, l), new Vector3(w, 0, l) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false; mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            mr.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            // placed in the root frame (the model may be yawed 180°): centre of the footprint, just above the wheels' contact
            go.transform.position = root.TransformPoint(new Vector3(b.center.x, y, b.center.z));
            go.transform.rotation = root.rotation;
        }

        static string Arg(string k)
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == k) return a[i + 1];
            return null;
        }
    }
}
