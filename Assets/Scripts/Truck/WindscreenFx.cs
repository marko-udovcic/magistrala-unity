// One transparent surface, no camera textures. Wiper history is evaluated analytically in the shader.
using UnityEngine;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class WindscreenFx : MonoBehaviour
{
    const float Sweep = 85f, Rate = 45f / 60f;
    /// measuring: -skip windscreen hides the glass (ProbeBench)
    public static bool Off;
    static readonly int AngleL = Shader.PropertyToID("_WiperAngleL"), AngleR = Shader.PropertyToID("_WiperAngleR");
    static readonly int Phase = Shader.PropertyToID("_Phase"), Recovery = Shader.PropertyToID("_Recovery");
    static readonly int OriginL = Shader.PropertyToID("_WiperOriginL"), OriginR = Shader.PropertyToID("_WiperOriginR");
    static readonly int WiperRight = Shader.PropertyToID("_WiperRight"), WiperUp = Shader.PropertyToID("_WiperUp"), Forward = Shader.PropertyToID("_CabForward");
    static readonly int SweepId = Shader.PropertyToID("_Sweep"), Elapsed = Shader.PropertyToID("_WipeElapsed"), HasWiped = Shader.PropertyToID("_HasWiped");
    TruckController tc;
    TruckChaseCamera chase;
    Transform left, right, model;
    Quaternion left0, right0;
    MeshRenderer glass;
    Material mat;
    // the vehicle's own see-through glass (Atego "glass" alpha 0, Galeb "Vitre"): double-sided transparent glTF Lit over
    // most of the screen from the driver's seat cost ~3 ms GPU on the M2 (kabina-perf, -kabinaab): hidden in the cab view,
    // where windscreen_fx gives the windscreen its look
    readonly System.Collections.Generic.List<Renderer> ownGlass = new System.Collections.Generic.List<Renderer>();
    bool glassHidden;
    float phase, wipeElapsed, recovery = 10f;
    bool moving, wiped;

    public void Bind(TruckController controller, Transform visualModel)
    {
        if (mat) Destroy(mat);
        tc = controller; model = visualModel; left = right = null; glass = null;
        phase = wipeElapsed = 0; moving = wiped = false; recovery = 10f;
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "windscreen_fx") glass = t.GetComponent<MeshRenderer>();
            else if (t.name == "wiper_L") { left = t; left0 = t.localRotation; }
            else if (t.name == "wiper_R") { right = t; right0 = t.localRotation; }
        }
        // Keep the legacy exterior resources intact in the GLB; the new animated pair replaces their rendering.
        if (left && right)
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == "wiper_0" || t.name == "wiper_1")
                    foreach (var r in t.GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        ShowOwnGlass(true); ownGlass.Clear();
        foreach (var r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name == "windscreen_fx" || r.sharedMaterials.Length == 0) continue;
            bool allClear = true; // only renderers that are nothing but transparent material (never hide an opaque part)
            foreach (var m in r.sharedMaterials) if (!m || m.renderQueue <= 2500) { allClear = false; break; }
            if (allClear) ownGlass.Add(r);
        }
        if (!glass) return;
        glass.enabled = false;
        // A Resources material holds a direct shader reference, preserving it in player builds.
        var source = Resources.Load<Material>("Shaders/Windscreen");
        if (!source) { Debug.LogWarning("Windscreen resource material missing.", this); return; }
        mat = new Material(source) { name = "Windscreen (runtime)" };
        mat.SetFloat(SweepId, Sweep * Mathf.Deg2Rad);
        glass.sharedMaterial = mat;
        glass.shadowCastingMode = ShadowCastingMode.Off; glass.receiveShadows = false;
        glass.lightProbeUsage = LightProbeUsage.Off; glass.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    void LateUpdate()
    {
        if (!tc) return;
        if (!chase) chase = FindFirstObjectByType<TruckChaseCamera>();
        bool target = chase && chase.target == tc;
        bool rain = Magistrala.WeatherSystem.Raining;
        // Only the active vehicle needs animation; finish the current cycle to park without a snap.
        bool run = target && rain;
        bool wasMoving = moving;
        if (run) moving = true;
        if (moving)
        {
            wipeElapsed = Mathf.Min(10f, wipeElapsed + Time.deltaTime);
            phase += Time.deltaTime * Rate * Mathf.PI * 2f;
            if (phase >= Mathf.PI * 2f)
            {
                phase %= Mathf.PI * 2f;
                if (!run) { phase = 0; moving = false; }
            }
            wiped = true; recovery = 0;
        }
        else recovery = Mathf.Min(10f, recovery + Time.deltaTime);
        float angle = moving || wasMoving ? Sweep * (.5f - .5f * Mathf.Cos(phase)) : 0f;
        // glTFast mirrors X: negative Unity Z rotates the parked blade up the glass.
        if (moving || wasMoving)
        {
            if (left) left.localRotation = left0 * Quaternion.AngleAxis(-angle, Vector3.forward);
            if (right) right.localRotation = right0 * Quaternion.AngleAxis(-angle, Vector3.forward);
        }
        bool cab = target && chase.CabinView;
        if (cab != glassHidden) ShowOwnGlass(!cab);
        bool on = cab && mat && !Off;
        if (glass && glass.enabled != on) glass.enabled = on;
        if (!on) return;
        mat.SetFloat(AngleL, angle * Mathf.Deg2Rad); mat.SetFloat(AngleR, angle * Mathf.Deg2Rad);
        mat.SetFloat(Elapsed, wipeElapsed);
        mat.SetFloat(Phase, phase); mat.SetFloat(Recovery, recovery); mat.SetFloat(HasWiped, wiped ? 1f : 0f);
        if (left && right)
        {
            mat.SetVector(OriginL, left.position); mat.SetVector(OriginR, right.position);
            // Use the rest frame, independent of this frame's rotating arms.
            Quaternion rest = left.parent ? left.parent.rotation * left0 : left0;
            mat.SetVector(WiperRight, rest * Vector3.left); mat.SetVector(WiperUp, rest * Vector3.up);
        }
        mat.SetVector(Forward, model.TransformDirection(tc.spec.model == "atego" ? Vector3.back : Vector3.forward));
    }

    void ShowOwnGlass(bool show)
    {
        glassHidden = !show;
        foreach (var r in ownGlass) if (r) r.forceRenderingOff = !show;
    }

    void OnDisable()
    {
        ShowOwnGlass(true);
        if (glass) glass.enabled = false;
        if (left) left.localRotation = left0;
        if (right) right.localRotation = right0;
        phase = wipeElapsed = 0; moving = wiped = false; recovery = 10f;
    }
    void OnDestroy() { if (mat) Destroy(mat); }
}
