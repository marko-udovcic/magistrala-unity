using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Magistrala
{
    /// Measuring only: command-line switches that turn GPU costs on / off in a built player, so one build can A/B many
    /// variants. Pipeline-asset switches are applied after every RenderQuality.Set (ApplyPipeline), scene switches once
    /// at start. Unknown / absent switches change nothing.
    ///   -post 0                 camera post-processing off        -bloom 0 / -vig 0 / -split 0 / -wb 0 / -ca 0 / -smh 0 / -tonemap 0
    ///   -aa 0|1|2|3 [-aaq 0..2] antialiasing none/FXAA/SMAA/TAA   -hdr 0      -opaque 0|1  -depth 0    -lut 16|32
    ///   -ssao 0 | half | full | depth | normals | after | before | blur0..2 | samples0..2 (comma list)
    ///   -soft 0..3 (0 = hard shadows)   -casc N   -shd metres   -shres N   -scale F
    ///   -shsmall M  renderers whose bounds are smaller than M metres cast no shadow   -tshadow 0  terrain casts no shadow
    ///   -camhigh H  camera H m up, 1.2 H behind the truck (higher look at the ground)
    ///   -dof 0      orthophoto ground colour off (stock terrain material on the macro tiles)
    ///   -sea 0      -probes 0 (reflection probe blending / box projection off)   -tbase M (terrain basemap distance)
    ///   -golden 1   start in sunset    -shot path.png [-shotat s]  screenshot after s seconds (default 25)
    ///   -burst N    with -shot: N consecutive frames path_00.png.. (shimmer / temporal look while driving)
    ///   -tp gx[,gx2..]  put the truck on the autopilot path at game x (after 1 s, then the next every 12 s) — look at given stretches
    ///   -shots t1,t2,..  with -shot: one screenshot at each time after the scene start, path_t030.png.. (look along the drive in one run)
    ///   -cor ci     before -tp: the autopilot path becomes traffic corridor ci's right lane (0 / 1 D8, 2 / 3 old road east / west)
    ///   -tpxz gx,gz,yaw[;gx,gz,yaw..]  instead of -tp: put the truck at game (x, z) heading yaw° (0 = north, 90 = east), every 12 s
    ///   -near M / -far M  camera clip planes (after TruckChaseCamera.Awake)
    ///   -ssao ...,nq0..2  normals-from-depth reconstruction quality (Low / Medium / High; only with depth)
    ///   -appath gx,gz;gx,gz;..  the autopilot drives this path from its first point ([gpu] truck y every second; -apkmh K cruise)
    ///   -dron       start in drone mode (DroneCamera; F toggles in game)   -dronlet S  the drone flies forward at S × speed (tests / clips)  -dronh H  start H m above the truck
    public class GpuSwitches : MonoBehaviour
    {
        /// -nohud: no HUD panels, minimap or phone (clean video for marketing); -capfps N: fixed game time per frame (Time.captureFramerate) so -burst frames make a smooth N fps video
        public static readonly bool NoHud = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-nohud") >= 0;
        static string[] args;
        static string Arg(string k)
        {
            args ??= System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == k) return args[i + 1];
            return null;
        }
        static bool Off(string k) => Arg(k) == "0";
        static bool TryF(string k, out float f) { f = 0; var s = Arg(k); return s != null && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Attach()
        {
            if (Application.isEditor) return;
            var go = new GameObject("GpuSwitches"); DontDestroyOnLoad(go); go.AddComponent<GpuSwitches>();
        }

        static void SetField(object o, string name, object v)
        {
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(o, v); else Debug.Log($"[gpu] no field {name} on {o.GetType().Name}");
        }

        /// pipeline asset overrides; RenderQuality.Set calls this after it writes its level
        public static void ApplyPipeline(UniversalRenderPipelineAsset rp)
        {
            if (Application.isEditor || !rp) return;
            var sb = new System.Text.StringBuilder("[gpu] pipeline:");
            var cd0 = Camera.main ? Camera.main.GetComponent<UniversalAdditionalCameraData>() : null; // AA is per level too (RenderQuality), so the switch wins here
            if (cd0 && TryF("-aa", out var aa0)) { cd0.antialiasing = (AntialiasingMode)(int)aa0; sb.Append($" aa {aa0}"); }
            if (cd0 && TryF("-aaq", out var aq0)) { cd0.antialiasingQuality = (AntialiasingQuality)(int)aq0; sb.Append($" aaq {aq0}"); }
            if (Off("-hdr")) { rp.supportsHDR = false; sb.Append(" hdr 0"); }
            if (Arg("-opaque") is string op) { rp.supportsCameraOpaqueTexture = op != "0"; sb.Append($" opaque {op}"); }
            if (Off("-depth")) { rp.supportsCameraDepthTexture = false; sb.Append(" depth 0"); }
            if (TryF("-lut", out var lut)) { rp.colorGradingLutSize = (int)lut; sb.Append($" lut {lut}"); }
            if (TryF("-casc", out var c)) { rp.shadowCascadeCount = (int)c; sb.Append($" casc {c}"); }
            if (TryF("-shd", out var d)) { rp.shadowDistance = d; sb.Append($" shd {d}"); }
            if (TryF("-shres", out var r)) { rp.mainLightShadowmapResolution = (int)r; sb.Append($" shres {r}"); }
            if (TryF("-scale", out var s)) { rp.renderScale = s; rp.upscalingFilter = s < 1 ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Auto; sb.Append($" scale {s}"); }
            if (Off("-probes")) { SetField(rp, "m_ReflectionProbeBlending", false); SetField(rp, "m_ReflectionProbeBoxProjection", false); sb.Append(" probes 0"); }
            if (TryF("-soft", out var soft))
            {
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l.type != LightType.Directional) continue;
                    if (soft == 0) l.shadows = LightShadows.Hard;
                    else { var ad = l.GetComponent<UniversalAdditionalLightData>(); if (ad) ad.softShadowQuality = (SoftShadowQuality)(int)soft; }
                }
                sb.Append($" soft {soft}");
            }
            var ssao = Arg("-ssao");
            if (ssao != null)
            {
                var list = GetRendererFeatures(rp);
                if (list != null) foreach (var f in list)
                {
                    if (!(f is ScreenSpaceAmbientOcclusion)) continue;
                    if (ssao == "0") { f.SetActive(false); continue; }
                    f.SetActive(true); // any other value: SSAO on with those settings
                    var st = f.GetType().GetField("m_Settings", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(f);
                    if (st == null) continue;
                    foreach (var w in ssao.Split(','))
                    {
                        if (w == "half") SetField(st, "Downsample", true);
                        else if (w == "after") SetField(st, "AfterOpaque", true);
                        else if (w == "before") SetField(st, "AfterOpaque", false);
                        else if (w == "depth") SetEnum(st, "Source", 0);
                        else if (w == "normals") SetEnum(st, "Source", 1);
                        else if (w == "full") SetField(st, "Downsample", false);
                        else if (w.StartsWith("blur")) SetEnum(st, "BlurQuality", int.Parse(w.Substring(4)));
                        else if (w.StartsWith("samples")) SetEnum(st, "Samples", int.Parse(w.Substring(7)));
                        else if (w == "interleaved") SetEnum(st, "AOMethod", 1);
                        else if (w.StartsWith("nq")) SetEnum(st, "NormalSamples", int.Parse(w.Substring(2)));
                    }
                }
                sb.Append($" ssao {ssao}");
            }
            Debug.Log(sb.ToString());
        }

        static void SetEnum(object o, string name, int v)
        {
            var f = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(o, System.Enum.ToObject(f.FieldType, v)); else Debug.Log($"[gpu] no field {name}");
        }

        static System.Collections.Generic.List<ScriptableRendererFeature> GetRendererFeatures(UniversalRenderPipelineAsset rp)
        {
            var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.Instance | BindingFlags.NonPublic);
            var arr = f?.GetValue(rp) as ScriptableRendererData[];
            if (arr == null || arr.Length == 0 || !arr[0]) { Debug.Log("[gpu] no renderer data"); return null; }
            return arr[0].rendererFeatures;
        }

        float camHigh; // -camhigh H: the camera H m above / 1.2 H behind the truck, looking 25 m ahead (a higher look at the ground)
        float shotAt = 25; string shot; bool shotDone; int burst, burstI; float[] shots; int shotsI; float t0;

        void Start()
        {
            t0 = Time.realtimeSinceStartup;
            var sb = new System.Text.StringBuilder("[gpu] scene:");
            var cam = Camera.main;
            Debug.Log($"[gpu] depth {SystemInfo.GetGraphicsFormat(UnityEngine.Experimental.Rendering.DefaultFormat.DepthStencil)}, reversed Z {SystemInfo.usesReversedZBuffer}, near {(cam ? cam.nearClipPlane : 0)} far {(cam ? cam.farClipPlane : 0)}");
            var cd = cam ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
            if (cd)
            {
                if (Off("-post")) { cd.renderPostProcessing = false; sb.Append(" post 0"); }
                if (TryF("-aa", out var aa)) { cd.antialiasing = (AntialiasingMode)(int)aa; sb.Append($" aa {aa}"); }
                if (TryF("-aaq", out var aq)) { cd.antialiasingQuality = (AntialiasingQuality)(int)aq; sb.Append($" aaq {aq}"); }
            }
            if (cam && TryF("-near", out var nr)) { cam.nearClipPlane = nr; sb.Append($" near {nr}"); }
            if (cam && TryF("-far", out var fr)) { cam.farClipPlane = fr; sb.Append($" far {fr}"); }
            var vol = FindFirstObjectByType<Volume>();
            if (vol)
            {
                var prof = vol.profile;
                foreach (var comp in prof.components)
                {
                    string n = comp is Bloom ? "-bloom" : comp is Vignette ? "-vig" : comp is SplitToning ? "-split" : comp is WhiteBalance ? "-wb"
                        : comp is ColorAdjustments ? "-ca" : comp is ShadowsMidtonesHighlights ? "-smh" : comp is Tonemapping ? "-tonemap" : null;
                    if (n != null && Off(n)) { comp.active = false; sb.Append($" {n} 0"); }
                }
            }
            if (Off("-sea")) foreach (var s in FindObjectsByType<SeaScroll>(FindObjectsSortMode.None)) { s.gameObject.SetActive(false); sb.Append(" sea 0"); }
            if (TryF("-shsmall", out var small))
            {
                int n = 0;
                foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                    if (r.shadowCastingMode == ShadowCastingMode.On && r.bounds.size.magnitude < small) { r.shadowCastingMode = ShadowCastingMode.Off; n++; }
                sb.Append($" shsmall {small}: {n} renderers");
            }
            if (Off("-tshadow")) foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None)) { t.shadowCastingMode = ShadowCastingMode.Off; sb.Append(" tshadow 0"); }
            if (Off("-dof"))
            {
                // A/B of the orthophoto ground colour (OrthoGround.cs): the macro tiles get the stock URP terrain material back
                Material stock = null; int n = 0;
                foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None)) if (t.materialTemplate && !t.materialTemplate.HasProperty("_MacroTex")) { stock = t.materialTemplate; break; }
                foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None)) if (stock && t.materialTemplate && t.materialTemplate.HasProperty("_MacroTex")) { t.materialTemplate = stock; n++; }
                Shader.SetGlobalFloat("_MacroOff", 1); sb.Append($" dof 0: {n} terrains");
            }
            if (TryF("-tbase", out var tb)) foreach (var t in FindObjectsByType<Terrain>(FindObjectsSortMode.None)) { t.basemapDistance = tb; sb.Append($" tbase {tb}"); }
            if (Arg("-golden") == "1") { var lp = FindFirstObjectByType<LightPreset>(); if (lp) { lp.SetGolden(true); sb.Append(" golden"); } }
            if (Arg("-night") == "1") { var lp = FindFirstObjectByType<LightPreset>(); if (lp) { lp.SetNight(true); sb.Append(" night"); } }
            if (TryF("-camhigh", out var ch)) { camHigh = ch; sb.Append($" camhigh {ch}"); }
            shot = Arg("-shot"); if (TryF("-shotat", out var sa)) shotAt = sa; if (TryF("-burst", out var bu)) burst = (int)bu;
            if (TryF("-capfps", out var cf)) { Time.captureFramerate = (int)cf; sb.Append($" capfps {cf}"); }
            if (Arg("-shots") is string sl) shots = System.Array.ConvertAll(sl.Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture));
            if (Arg("-tp") is string tps) StartCoroutine(Tp(System.Array.ConvertAll(tps.Split(','), x => float.Parse(x, System.Globalization.CultureInfo.InvariantCulture))));
            else if (Arg("-cor") != null || Arg("-tpxz") != null || Arg("-appath") != null) StartCoroutine(Tp(new float[0]));
            Debug.Log(sb.ToString());
        }

        System.Collections.IEnumerator Tp(float[] gx)
        {
            yield return new WaitForSeconds(1f);
            var ap = Magistrala.Game.MagistralaGame.DrivenTruck()?.GetComponent<Autopilot>();
            if (ap && TryF("-cor", out var cf))
            {
                var ts = FindFirstObjectByType<Magistrala.Traffic.TrafficSystem>(); int ci = (int)cf;
                if (ts && ts.data && ci >= 0 && ci < ts.data.corridors.Length)
                {
                    var c = ts.data.corridors[ci]; var pts = new Vector3[c.axis.Length];
                    for (int i = 0; i < pts.Length; i++) { float o = c.laneOff[i * c.maxLanes]; var p = c.axis[i] + c.right[i] * (float.IsNaN(o) ? 0 : o); p.y = c.laneY[i * c.maxLanes]; pts[i] = p; }
                    ap.UsePath(pts); if (gx.Length == 0) ap.Teleport(pts[Mathf.Min(40, pts.Length - 1)].x - 4200);
                    Debug.Log($"[gpu] autopilot on corridor {ci} {c.name}, {pts.Length} samples");
                }
            }
            // -appath gx,gz;gx,gz;.. [-apkmh K]: the autopilot drives this path (game x / z, heights from the ground) from its first point
            if (ap && Arg("-appath") is string app)
            {
                var pts = new System.Collections.Generic.List<Vector3>();
                foreach (var t in app.Split(';'))
                {
                    var v = System.Array.ConvertAll(t.Split(','), q => float.Parse(q, System.Globalization.CultureInfo.InvariantCulture));
                    var u = new Vector3(v[0] + 4200, 300, -(v[1] + 5300));
                    if (Physics.Raycast(u, Vector3.down, out var hit, 600, ~0, QueryTriggerInteraction.Ignore)) u.y = hit.point.y; else u.y = 0;
                    pts.Add(u);
                }
                if (pts.Count >= 2)
                {
                    var dense = new System.Collections.Generic.List<Vector3>();
                    for (int i = 0; i + 1 < pts.Count; i++) { int n = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(pts[i], pts[i + 1]) / 2)); for (int k = 0; k < n; k++) dense.Add(Vector3.Lerp(pts[i], pts[i + 1], k / (float)n)); }
                    dense.Add(pts[pts.Count - 1]);
                    var tr = Magistrala.Game.MagistralaGame.DrivenTruck(); var rb = tr ? tr.GetComponent<Rigidbody>() : null;
                    if (tr) { var d = dense[1] - dense[0]; d.y = 0; var p0 = dense[0] + Vector3.up * 1.1f; tr.transform.SetPositionAndRotation(p0, Quaternion.LookRotation(d)); if (rb) { rb.position = p0; rb.rotation = tr.transform.rotation; rb.linearVelocity = Vector3.zero; } }
                    ap.UsePath(dense.ToArray()); if (TryF("-apkmh", out var kmh)) ap.cruiseKmh = kmh; ap.drive = true;
                    Debug.Log($"[gpu] appath {pts.Count} points, {dense.Count} samples");
                    StartCoroutine(TrackY(tr));
                }
            }
            if (Arg("-tpxz") is string xz)
                foreach (var t in xz.Split(';'))
                {
                    var v = System.Array.ConvertAll(t.Split(','), q => float.Parse(q, System.Globalization.CultureInfo.InvariantCulture));
                    var tr = Magistrala.Game.MagistralaGame.DrivenTruck();
                    if (tr)
                    {
                        var u = new Vector3(v[0] + 4200, 300, -(v[1] + 5300));
                        if (Physics.Raycast(u, Vector3.down, out var hit, 600, ~0, QueryTriggerInteraction.Ignore)) u.y = hit.point.y + 1.1f;
                        var rb = tr.GetComponent<Rigidbody>(); if (rb) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                        tr.transform.SetPositionAndRotation(u, Quaternion.Euler(0, v.Length > 2 ? v[2] : 0, 0)); if (rb) { rb.position = u; rb.rotation = tr.transform.rotation; }
                        Debug.Log($"[gpu] tpxz {t} y {u.y:F1}");
                    }
                    yield return new WaitForSeconds(12f);
                }
            foreach (var x in gx) { if (ap) { ap.Teleport(x); Debug.Log($"[gpu] tp {x} at {Time.realtimeSinceStartup:F0}s"); } yield return new WaitForSeconds(12f); }
        }

        /// -appath: the truck's position every second (falls through the ground: [gpu] FALL)
        System.Collections.IEnumerator TrackY(TruckController tr)
        {
            for (int i = 0; i < 90 && tr; i++)
            {
                yield return new WaitForSeconds(1f);
                var p = tr.transform.position; bool ground = Physics.Raycast(p + Vector3.up * 3, Vector3.down, out var h, 40, ~0, QueryTriggerInteraction.Ignore);
                // (gear, rpm, grade along the truck, wheels on the ground and what each stands on: a stall on a slope vs at a step)
                var ws = new System.Text.StringBuilder();
                foreach (var w in tr.GetComponentsInChildren<WheelCollider>()) ws.Append(w.GetGroundHit(out var wh) ? " " + wh.collider.name : " -");
                Debug.Log($"[gpu] truck t={i + 1} x={p.x - 4200:F1} z={-p.z - 5300:F1} y={p.y:F2} v={tr.SpeedKmh:F0} g={tr.Gear} rpm={tr.EngineRpm:F0} grade={tr.transform.forward.y * 100:F0}% wheels={tr.Grounded}:{ws}" + (p.y < -1.5f ? " FALL" : ""));
            }
        }

        // -campose gx,gz,h,yaw,pitch: the camera fixed there (game x / z, h m above the ground, Unity yaw, pitch down in degrees;
        // screenshots of facades / lamps from a chosen spot)
        float[] camPose; float camPoseY = float.NaN;
        void LateUpdate()
        {
            if (camPose == null && Arg("-campose") is string cps) camPose = System.Array.ConvertAll(cps.Split(','), q => float.Parse(q, System.Globalization.CultureInfo.InvariantCulture));
            if (camPose != null && camPose.Length >= 5 && Camera.main)
            {
                var ch2 = Camera.main.GetComponent<TruckChaseCamera>(); if (ch2) ch2.enabled = false;
                var u = new Vector3(camPose[0] + 4200, 0, -(camPose[1] + 5300));
                if (float.IsNaN(camPoseY) && Physics.Raycast(u + Vector3.up * 300, Vector3.down, out var hit, 600, ~0, QueryTriggerInteraction.Ignore)) camPoseY = hit.point.y;
                if (!float.IsNaN(camPoseY)) Camera.main.transform.SetPositionAndRotation(u + Vector3.up * (camPoseY + camPose[2]), Quaternion.Euler(camPose[4], camPose[3], 0));
                return;
            }
            if (camHigh <= 0 || !Camera.main) return;
            var tr = Magistrala.Game.MagistralaGame.DrivenTruck(); if (!tr) return;
            var chase = Camera.main.GetComponent<TruckChaseCamera>(); if (chase) chase.enabled = false;
            var f = tr.transform.forward; f.y = 0; f.Normalize();
            Camera.main.transform.position = tr.transform.position + Vector3.up * camHigh - f * camHigh * 1.2f;
            Camera.main.transform.LookAt(tr.transform.position + f * 25);
        }

        void Update()
        {
            if (shot != null && shots != null)
            {
                if (shotsI < shots.Length && Time.realtimeSinceStartup - t0 > shots[shotsI])
                {
                    var tr = Magistrala.Game.MagistralaGame.DrivenTruck();
                    string f = shot.Replace(".png", $"_t{shots[shotsI]:000}.png"); ScreenCapture.CaptureScreenshot(f);
                    Debug.Log($"[gpu] shot {f} truck x={(tr ? tr.transform.position.x - 4200 : 0):F0} z={(tr ? -tr.transform.position.z - 5300 : 0):F0}"); shotsI++;
                }
                return;
            }
            if (shot == null || shotDone || Time.realtimeSinceStartup <= shotAt) return;
            if (burst <= 1) { shotDone = true; ScreenCapture.CaptureScreenshot(shot); Debug.Log("[gpu] shot " + shot); return; }
            ScreenCapture.CaptureScreenshot(shot.Replace(".png", $"_{burstI:D2}.png"));
            if (++burstI >= burst) { shotDone = true; Debug.Log($"[gpu] burst {burst} " + shot); }
        }
    }
}
