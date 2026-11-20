using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Magistrala
{
    /// Sea A/B: our opaque depth-tinted sea (GraphicsPass "Grafika: more") or NatureManufacture's Coast & Dunes sea shader
    /// ("Water Sea Offset", transparent: refraction of the opaque texture, depth foam at the shore, calm Kaštela-bay waves)
    /// on the same near grid (NatureSea.cs builds both). Command line `-more nm` / `-more nase` overrides the built default.
    /// The NM sea needs the camera opaque texture (turned on here only while it is shown) and the depth texture (always on).
    public class SeaMode : MonoBehaviour
    {
        public GameObject[] ours; public GameObject nmNear, nmFar;
        public bool useNM;
        public static bool NM { get; private set; }

        void Start()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-more") useNM = args[i + 1].ToLowerInvariant() == "nm";
            Apply(useNM);
        }

        public void Apply(bool nm)
        {
            NM = nm && nmNear;
            if (ours != null) foreach (var o in ours) if (o) o.SetActive(!NM);
            if (nmNear) nmNear.SetActive(NM); if (nmFar) nmFar.SetActive(NM);
            if (!NM || Application.isEditor && !Application.isPlaying) return;
            var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (rp) { rp.supportsCameraOpaqueTexture = true; rp.supportsCameraDepthTexture = true; }
            var cam = Camera.main; var cd = cam ? cam.GetComponent<UniversalAdditionalCameraData>() : null;
            if (cd) { cd.requiresColorOption = CameraOverrideOption.On; cd.requiresDepthOption = CameraOverrideOption.On; }
            Debug.Log("[more] NatureManufacture sea");
        }
    }
}
