// Working side mirrors in the cab view: one small camera per outside mirror (nodes mglass_L / mglass_R of the vehicle
// model), rendered into a low-resolution texture shown on a quad over the mirror glass. Cheap on purpose:
//   256×192 per mirror, each mirror renders every 4th frame (left / right on alternate even frames, none on odd), no shadows, no post, far 160 m,
//   only while this vehicle is the chase camera's target AND the cab view is on, off on the "brzo" quality level.
// The mirror camera sits at the glass, looking along the eye→glass ray reflected about the glass normal (a real mirror).
using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public sealed class MirrorCams : MonoBehaviour
{
    public int width = 256, height = 192;
    public float far = 160f, fov = 28f;
    /// measuring: -mirrors 0 turns them off
    public static bool Off;

    sealed class Mirror
    {
        public Transform glass; public Vector3 centerLocal, normalLocal;
        public Camera cam; public RenderTexture rt; public GameObject quad;
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
                var b = mf.sharedMesh.bounds;
                // the glass is a thin plate: its normal is the bounds' thinnest local axis
                Vector3 e = b.extents; Vector3 n = e.x <= e.y && e.x <= e.z ? Vector3.right : e.y <= e.z ? Vector3.up : Vector3.forward;
                mirrors.Add(new Mirror { glass = t, centerLocal = b.center, normalLocal = n });
            }
    }

    void Build(Mirror m)
    {
        m.rt = new RenderTexture(width, height, 16, RenderTextureFormat.ARGB32) { name = "mirror_" + m.glass.name, antiAliasing = 1 };
        var go = new GameObject("MirrorCam_" + m.glass.name);
        go.transform.SetParent(transform, false);
        m.cam = go.AddComponent<Camera>();
        m.cam.targetTexture = m.rt; m.cam.fieldOfView = fov; m.cam.nearClipPlane = 0.3f; m.cam.farClipPlane = far;
        m.cam.allowMSAA = false; m.cam.allowHDR = false; m.cam.enabled = false;
        var cd = go.AddComponent<UniversalAdditionalCameraData>();
        cd.renderShadows = false; cd.renderPostProcessing = false; cd.antialiasing = AntialiasingMode.None;
        cd.requiresDepthTexture = false; cd.requiresColorTexture = false;
        // the picture: an unlit quad just in front of the glass, sized to the glass, mirrored left-right
        var mf = m.glass.GetComponent<MeshFilter>(); var b = mf.sharedMesh.bounds;
        m.quad = GameObject.CreatePrimitive(PrimitiveType.Quad); m.quad.name = "MirrorImage_" + m.glass.name;
        var col = m.quad.GetComponent<Collider>(); if (Application.isPlaying) Destroy(col); else DestroyImmediate(col);
        m.quad.transform.SetParent(m.glass, false);
        var e = b.size; Vector3 n = m.normalLocal;
        Vector2 size = n == Vector3.right ? new Vector2(e.z, e.y) : n == Vector3.up ? new Vector2(e.x, e.z) : new Vector2(e.x, e.y);
        m.quad.transform.localScale = new Vector3(size.x * .96f, size.y * .96f, 1);
        // URP/Unlit is stripped from the player when no material uses it: fall back to the always-included UI/Default (unlit, textured)
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
        if (!sh) { enabled = false; return; }
        var mat = new Material(sh) { mainTexture = m.rt };
        mat.mainTextureScale = new Vector2(-1, 1); mat.mainTextureOffset = new Vector2(1, 0);
        m.quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
        m.quad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
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
            if (!m.cam) Build(m);
            m.quad.SetActive(true);
            // face the quad toward the eye, a hair in front of the glass
            var c = m.glass.TransformPoint(m.centerLocal);
            var nW = m.glass.TransformDirection(m.normalLocal).normalized;
            if (Vector3.Dot(eye - c, nW) < 0) nW = -nW;
            m.quad.transform.SetPositionAndRotation(c + nW * 0.004f, Quaternion.LookRotation(-nW, transform.up));
            // the reflected view; at most one mirror per frame: L on frames 0, 4, 8…, R on 2, 6, 10… (measured: every 2nd frame cost ~5–11 % in the cab view)
            var refl = Vector3.Reflect((c - eye).normalized, nW);
            m.cam.transform.SetPositionAndRotation(c, Quaternion.LookRotation(refl, transform.up));
            if (now) m.cam.Render(); else m.cam.enabled = frame % 4 == (i % 2) * 2;
        }
        frame++;
    }

    void OnDestroy()
    {
        foreach (var m in mirrors) if (m.rt) { m.rt.Release(); Destroy(m.rt); }
    }
}
