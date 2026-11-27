// Working side mirrors in the cab view: one small camera per outside mirror (nodes mglass_L / mglass_R of the vehicle
// model), rendered into a low-resolution texture shown on a quad over the mirror glass. Cheap on purpose:
//   256×192 per mirror, each mirror renders every 4th frame (left / right on alternate even frames, none on odd), no shadows, no post, far 120 m,
//   half the LOD bias and no terrain grass / detail meshes (a 256 px picture can't show them),
//   only while this vehicle is the chase camera's target AND the cab view is on, off on the "brzo" quality level.
// The mirror camera sits at the glass, looking along the eye→glass ray reflected about the glass normal (a real mirror).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Collections.Generic;

[DisallowMultipleComponent]
public sealed class MirrorCams : MonoBehaviour
{
    public int width = 256, height = 192;
    public float far = 120f, fov = 28f;
    /// how much of the mirror's width (degrees of view) shows the own vehicle's side
    public float InnerDeg = 6f;
    /// measuring: -mirrors 0 turns them off
    public static bool Off;

    sealed class Mirror
    {
        public Transform glass; public Vector3 centerLocal, normalLocal;
        public Camera cam; public RenderTexture rt; public GameObject quad; public Vector2 size; public Material material;
    }

    TruckController tc; TruckChaseCamera chase;
    readonly System.Collections.Generic.List<Mirror> mirrors = new System.Collections.Generic.List<Mirror>();
    int frame;

    public void Bind(TruckController controller, Transform model)
    {
        tc = controller; mirrors.Clear();
        if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-mirrors") is int i && i >= 0)
        {
            var a = System.Environment.GetCommandLineArgs(); if (i + 1 < a.Length && a[i + 1] == "0") Off = true;
        }
        foreach (var t in model.GetComponentsInChildren<Transform>(true))
            if (t.name == "mglass_L" || t.name == "mglass_R")
            {
                var mf = t.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                var mesh = mf.sharedMesh;
                var vertices = mesh.vertices; var indices = mesh.triangles;
                // A curved/tilted mirror is not aligned to its AABB's thinnest axis. The L4H1 face is tilted
                // ~32 degrees: the old quad through bounds.center intersected the lens, hiding much of the image.
                // Fit the largest face (the optical surface), keeping the quad 4 mm in front of that plane.
                Vector3 n = Vector3.forward, face = mesh.bounds.center; float largest = 0;
                for (int j = 0; j + 2 < indices.Length; j += 3)
                {
                    Vector3 a = vertices[indices[j]], b = vertices[indices[j + 1]], c = vertices[indices[j + 2]];
                    Vector3 cross = Vector3.Cross(b - a, c - a);
                    if (cross.sqrMagnitude <= largest) continue;
                    largest = cross.sqrMagnitude; n = cross.normalized; face = a;
                }
                var eye = t.InverseTransformPoint(tc.transform.TransformPoint(tc.CabinEyeLocal));
                if (Vector3.Dot(eye - face, n) < 0) n = -n;
                var up = Vector3.ProjectOnPlane(t.InverseTransformDirection(tc.transform.up), n).normalized;
                var right = Vector3.Cross(up, n).normalized;
                Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity), max = -min;
                foreach (var v in vertices)
                {
                    var p = new Vector2(Vector3.Dot(v, right), Vector3.Dot(v, up));
                    min = Vector2.Min(min, p); max = Vector2.Max(max, p);
                }
                var middle = (min + max) * .5f;
                var center = right * middle.x + up * middle.y + n * Vector3.Dot(face, n);
                mirrors.Add(new Mirror { glass = t, centerLocal = center, normalLocal = n, size = max - min });
            }
        foreach (var m in mirrors) { Build(m); if (m.quad) m.quad.SetActive(false); }
    }

    void Build(Mirror m)
    {
        m.rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { name = "mirror_" + m.glass.name, antiAliasing = 1 };
        var go = new GameObject("MirrorCam_" + m.glass.name);
        go.transform.SetParent(transform, false);
        m.cam = go.AddComponent<Camera>();
        m.cam.targetTexture = m.rt; m.cam.fieldOfView = fov; m.cam.nearClipPlane = 0.08f; m.cam.farClipPlane = far;
        var main = Camera.main;
        m.cam.cullingMask = (main ? main.cullingMask : ~0) & ~(1 << 2); // mirror display surfaces never render into mirrors
        m.cam.backgroundColor = main ? main.backgroundColor : Color.black;
        m.cam.clearFlags = CameraClearFlags.Skybox;
        m.cam.allowMSAA = false; m.cam.allowHDR = false; m.cam.enabled = false;
        var cd = go.AddComponent<UniversalAdditionalCameraData>();
        cd.renderShadows = false; cd.renderPostProcessing = false; cd.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.None;
        cd.requiresDepthTexture = false; cd.requiresColorTexture = false;
        // the picture: an unlit quad just in front of the glass, sized to the glass, mirrored left-right
        m.quad = GameObject.CreatePrimitive(PrimitiveType.Quad); m.quad.name = "MirrorImage_" + m.glass.name;
        var col = m.quad.GetComponent<Collider>(); if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
        m.quad.layer = 2; // built-in Ignore Raycast; also excluded from both mirror cameras
        m.quad.transform.SetParent(m.glass, false);
        m.quad.transform.localScale = new Vector3(m.size.x * .96f, m.size.y * .96f, 1);
        // URP/Unlit is stripped from the player when no material uses it: fall back to the always-included UI/Default (unlit, textured)
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
        if (!sh) { enabled = false; return; }
        var mat = m.material = new Material(sh) { mainTexture = m.rt };
        mat.mainTextureScale = new Vector2(-1, 1); mat.mainTextureOffset = new Vector2(1, 0);
        m.quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        m.quad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    // per mirror render: coarser LODs and no terrain foliage, restored right after (mirrors were ~0.5 ms GPU of the cab view on the M2)
    void OnEnable() { RenderPipelineManager.beginCameraRendering += BeginCam; RenderPipelineManager.endCameraRendering += EndCam; }
    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeginCam; RenderPipelineManager.endCameraRendering -= EndCam;
        RestoreFoliage();
        foreach (var m in mirrors)
        {
            if (m.cam) m.cam.enabled = false;
            if (m.quad) m.quad.SetActive(false);
        }
    }
    float savedBias; bool cheap; readonly List<Terrain> foliageOff = new List<Terrain>(512);
    readonly List<Terrain> activeTerrains = new List<Terrain>(512);
    bool Mine(Camera c) { foreach (var m in mirrors) if (m.cam == c) return true; return false; }
    void BeginCam(ScriptableRenderContext ctx, Camera c)
    {
        if (cheap || !Mine(c)) return;
        cheap = true; savedBias = QualitySettings.lodBias; QualitySettings.lodBias = savedBias * .5f;
        foliageOff.Clear();
        Terrain.GetActiveTerrains(activeTerrains);
        foreach (var t in activeTerrains) if (t && t.drawTreesAndFoliage) { t.drawTreesAndFoliage = false; foliageOff.Add(t); }
    }
    void EndCam(ScriptableRenderContext ctx, Camera c)
    {
        if (!cheap || !Mine(c)) return;
        RestoreFoliage();
    }

    void RestoreFoliage()
    {
        if (!cheap) return;
        cheap = false; QualitySettings.lodBias = savedBias;
        foreach (var t in foliageOff) if (t) t.drawTreesAndFoliage = true;
        foliageOff.Clear();
    }

    void LateUpdate()
    {
        if (!tc || mirrors.Count == 0) return;
        if (!chase) chase = FindFirstObjectByType<TruckChaseCamera>();
        bool on = !Off && chase && chase.target == tc && chase.CabinView && Magistrala.RenderQuality.Current > 0;
        Place(chase ? chase.transform.position : tc.transform.position, on, false);
    }

    /// editor shots (KabinaShots): place and render both mirrors once from this eye
    public void RenderNow(Vector3 eye) => Place(eye, true, true);

    void Place(Vector3 eye, bool on, bool now)
    {
        for (int i = 0; i < mirrors.Count; i++)
        {
            var m = mirrors[i];
            if (!on) { if (m.cam) { m.cam.enabled = false; m.quad.SetActive(false); } continue; }
            if (!enabled || !m.quad || !m.material) return;
            m.quad.SetActive(true);
            // face the quad toward the eye, a hair in front of the glass
            var c = m.glass.TransformPoint(m.centerLocal);
            var nW = m.glass.TransformDirection(m.normalLocal).normalized;
            if (Vector3.Dot(eye - c, nW) < 0) nW = -nW;
            m.quad.transform.SetPositionAndRotation(c + nW * 0.004f, Quaternion.LookRotation(-nW, transform.up));
            // the reflected view; at most one mirror per frame: L on frames 0, 4, 8…, R on 2, 6, 10… (measured: every 2nd frame cost ~5–11 % in the cab view)
            var refl = Vector3.Reflect((c - eye).normalized, nW);
            // aimed like a driver sets it: straight back along the vehicle, turned outward so the own side is only a strip
            // InnerDeg wide at the inner edge; the pure reflection filled the mirror with the box (with the higher eye of
            // atego-vidljivost almost all of it). The up/down angle stays the reflected one.
            float side = Mathf.Sign(Vector3.Dot(c - transform.position, transform.right));
            float half = Camera.VerticalToHorizontalFieldOfView(fov, (float)width / height) * .5f;
            Vector3 flat = Quaternion.AngleAxis(-side * (half - InnerDeg), transform.up) * -transform.forward;
            float up = Mathf.Clamp(Vector3.Dot(refl, transform.up), -.5f, .5f);
            refl = flat * Mathf.Sqrt(1 - up * up) + transform.up * up;
            m.cam.transform.SetPositionAndRotation(c + nW * .025f, Quaternion.LookRotation(refl, transform.up));
            if (now) m.cam.Render(); else m.cam.enabled = frame % 4 == (i % 2) * 2;
        }
        frame = (frame + 1) & 3;
    }

    void OnDestroy()
    {
        foreach (var m in mirrors)
        {
            if (m.rt) { m.rt.Release(); Destroy(m.rt); }
            if (m.quad)
            {
                var displayed = m.quad.GetComponent<MeshRenderer>().sharedMaterial;
                if (displayed && displayed != m.material) Destroy(displayed); // VehicleLook may have instanced it
                Destroy(m.quad);
            }
            if (m.material) Destroy(m.material);
        }
    }
}
