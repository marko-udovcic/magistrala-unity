using UnityEngine;

namespace Magistrala
{
    /// refreshes the ambient probe / reflection from the procedural sky at start (no baked lighting in the probe)
    public class EnvironmentInit : MonoBehaviour
    {
        void Start() { DynamicGI.UpdateEnvironment(); Application.targetFrameRate = -1; QualitySettings.vSyncCount = 0; }
    }
}
