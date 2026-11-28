// Shared L4H1 material finish for the four player vans; applied once at startup.
// Authored gauges, livery and Galeb wear layers stay intact. Mirrors use the cached optical-plane fit in MirrorCams.
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
            // Mate's old L3H2 van: the paint has lost its gloss (chalky white, a thin worn coat)
            "galebl3h2" => new Profile { paintRough = .42f, coat = .08f, coatRough = .32f, boxCoat = .3f, glassA = .22f, glassTint = new Color(.55f, .6f, .6f) },
            // the firm's van: a clean modern white with a deep coat
            "sprinter" or "mali" or "l4h1" => new Profile { paintRough = .32f, coat = 1f, coatRough = .04f, boxCoat = .6f, glassA = .3f, glassTint = new Color(.32f, .36f, .38f) },
            // Generic fallback finish for editor previews
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
            int nPaint = 0, nGlass = 0, nMetal = 0, nPlastic = 0, nLamp = 0;
            bool lampReflector = PlayerTruck.Modern(id); // the four vans' headlamps are bulbs in chrome bowls behind a clear lens
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is not MeshRenderer) continue;
                if (r.name == "windscreen_fx") continue; // WindscreenFx's own material
                var mats = r.materials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i]; if (!m) continue;
                    string n = m.name.Replace(" (Instance)", "");
                    if (PlayerTruck.Modern(id)) n = VanMaterial(m, n);
                    if (n.StartsWith("L_")) nLamp++;
                    if (n == "L_head" && lampReflector)
                    {
                        // the bulbs / projector lens inside the bowls: no AO either; clear glass (grey, glossy) rather than flat
                        // white paint – the Sprinter's projector was a white disc. TruckLights' emission lights them up.
                        m.renderQueue = LampQueue; m.SetColor(BaseColor, new Color(.42f, .43f, .45f, 1)); m.SetFloat(Metal, .5f); m.SetFloat(Rough, .05f);
                    }
                    if (n.StartsWith("i_") || n.StartsWith("v2_") || n.StartsWith("v3_") || n.StartsWith("M_") || n.StartsWith("cargo") || n.StartsWith("pallet")) continue; // the cab interior / load
                    switch (n)
                    {
                        case "paint": case "firm_white_paint": case "galeb_body":
                        {
                            var src = m.IsKeywordEnabled("_OCCLUSION") ? coatOccSrc : coatSrc;
                            if (!m.shader.name.EndsWith("Clearcoat") && src) { mats[i] = m = Recoat(m, src); changed = true; }
                            m.SetFloat(Rough, P.paintRough); m.SetFloat(Coat, P.coat); m.SetFloat(CoatRough, P.coatRough);
                            if (id == "galebl3h2") m.SetColor(BaseColor, new Color(.80f, .80f, .77f)); // a faintly yellowed white
                            nPaint++; break;
                        }
                        case "box_panel": case "box_panel_w":
                            m.SetFloat(Coat, P.boxCoat); m.SetFloat(CoatRough, .12f); m.SetFloat(Rough, .38f); nPaint++; break;
                        case "glass": case "Vitre": case "lens_clear":
                            if (glassSrc) { mats[i] = m = Recoat(m, glassSrc); changed = true; }
                            // lamp lens: clear polycarbonate – the reflection only, a near-black albedo (28. 11.: the old milky
                            // white .9 at alpha .12 lit up as a flat white veil over the whole lamp, Marko: "svjetla su loša")
                            Glass(m, n == "lens_clear" ? .08f : P.glassA, n == "lens_clear" ? new Color(.03f, .032f, .035f) : P.glassTint); nGlass++; break;
                        case "chrome":
                            m.SetFloat(Metal, 1); m.SetFloat(Rough, .06f); nMetal++; break;
                        case "reflector":
                            // the chrome bowls behind the lens: a bright, slightly soft mirror, drawn just before the lens (queue
                            // 2980, still opaque blending + depth write) so the after-opaque SSAO does not blacken the deep bowls
                            // (they read as dark holes); behind the lens they need no AO, the lens has no motion vectors either
                            m.SetFloat(Metal, 1); m.SetFloat(Rough, .2f); m.SetColor(BaseColor, new Color(.95f, .95f, .95f, 1));
                            m.renderQueue = LampQueue; nMetal++; break;
                        case "alu": case "rim": case "plain_grille_satin": case "step_plate":
                            m.SetFloat(Metal, 1); m.SetFloat(Rough, id == "galebl3h2" ? .4f : .25f); nMetal++; break;
                        case "mirror": case "outside_mirror_glass":
                            m.SetFloat(Metal, 1); m.SetFloat(Rough, .03f); nMetal++; break;
                        case "black_plastic": case "black_trim": case "grille_dark": case "firm_dark_trim": case "chassis":
                            m.SetFloat(Metal, 0); m.SetFloat(Rough, id == "galebl3h2" ? .7f : .5f);
                            if (!m.GetTexture(BaseTex)) m.SetColor(BaseColor, new Color(.035f, .037f, .04f));
                            nPlastic++; break;
                        case "mudflap": case "tn_rubber": case "tyre":
                            m.SetFloat(Metal, 0); m.SetFloat(Rough, .85f); nPlastic++; break;
                    }
                }
                if (changed) r.materials = mats;
            }
            if (lampReflector) HeadReflectors(root);
            ContactShadow(root);
            Debug.Log($"[vozlook] {id}: lak {nPaint}, stakla {nGlass}, metal {nMetal}, plastika {nPlastic}, svjetla {nLamp}");
        }

        /// the L4H1 van's materials (BalkanDriver assets-src/kombi-l4h1 k2_build.py) → the shared look classes; its lamp materials
        /// are renamed to TruckLights' L_* (Lights_Front head, Lights_Brake tail / brake, Lights_Reverse, Lights_Indicator L_blink)
        static string VanMaterial(Material m, string n)
        {
            switch (n)
            {
                case "Lights_Fog": m.name = "L_fog"; return "L_fog";
                case "Lights_Marker": m.name = "L_marker"; return "L_marker";
                case "Lights_Front": m.name = "L_head"; return "L_head";
                case "Lights_Brake": m.name = "L_brake"; return "L_brake";
                case "Lights_Reverse": m.name = "L_reverse"; return "L_reverse";
                case "Lights_Indicator": m.name = "L_blink"; return "L_blink";
                case "Body_Paint": return "paint";
                case "Box_White": return "box_panel";
                case "Glass": return "glass";
                case "Lamp_Lens": return "lens_clear";
                case "Chrome": return "chrome";
                case "Lamp_Reflector": return "reflector";
                case "Alu_Satin": case "Rim": case "Silver": return "alu";
                case "Mirror_Glass": return "mirror";
                case "Black_Plastic": case "Chassis_Black": return "black_plastic";
                case "Tyre": return "tyre";
                default: return "i_" + n; // interior, gauges, plates, livery: as modelled
            }
        }

        /// lamp internals (reflector bowls, bulbs): late in the transparent range, before the lenses (glass template, 3000)
        const int LampQueue = 2980;

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

        // ------------------------------------------------------------------ headlamp reflectors
        /// The vans' Lamp_Reflector submesh holds every lamp's chrome (head, tail, mirror indicators). The bowls around the head
        /// bulbs (L_head) move to their own submesh / material "L_headrefl", so TruckLights can light the whole bowl up with the
        /// dipped beams (before: only the tiny bulbs glowed and the lamp stayed a grey reflection at night). Per renderer, all
        /// LODs; the mesh is copied once at startup (readable glTF meshes only).
        static void HeadReflectors(Transform root)
        {
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                var mf = r.GetComponent<MeshFilter>(); var mesh = mf ? mf.sharedMesh : null; if (!mesh || !mesh.isReadable) continue;
                var mats = r.sharedMaterials; int refl = -1, head = -1; if (mats.Length != mesh.subMeshCount) continue;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i]) continue; string mn = mats[i].name.Replace(" (Instance)", "");
                    if (mn == "Lamp_Reflector") refl = i; else if (mn == "L_head") head = i;
                }
                if (refl < 0 || head < 0) continue;
                // the head bulbs' box (mesh space) grown by 0.3 m: both headlamps' bowls, not the tail lamps or the mirrors
                var hb = mesh.GetSubMesh(head).bounds; hb.Expand(.6f);
                var v = mesh.vertices; var tri = mesh.GetTriangles(refl);
                var keep = new System.Collections.Generic.List<int>(tri.Length); var front = new System.Collections.Generic.List<int>();
                for (int t = 0; t < tri.Length; t += 3)
                {
                    var c = (v[tri[t]] + v[tri[t + 1]] + v[tri[t + 2]]) / 3f;
                    var dst = hb.Contains(c) ? front : keep; dst.Add(tri[t]); dst.Add(tri[t + 1]); dst.Add(tri[t + 2]);
                }
                if (front.Count == 0) continue;
                var copy = Object.Instantiate(mesh); copy.name = mesh.name;
                copy.subMeshCount = mesh.subMeshCount + 1;
                copy.SetTriangles(keep, refl, true); copy.SetTriangles(front, mesh.subMeshCount, true);
                mf.sharedMesh = copy;
                var hm = new Material(mats[refl]) { name = "L_headrefl" };
                var nm = new Material[mats.Length + 1]; System.Array.Copy(mats, nm, mats.Length);
                nm[nm.Length - 1] = hm; r.sharedMaterials = nm; n++;
            }
            if (n > 0) Debug.Log($"[vozlook] reflektori farova: {n} mreža");
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
