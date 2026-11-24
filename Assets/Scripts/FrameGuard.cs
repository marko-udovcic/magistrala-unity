using UnityEngine;

namespace Magistrala
{
    /// After a long frame Unity catches physics up with up to Maximum Allowed Timestep (project: 0.333 s) of fixed steps in the
    /// next frame: one 300 ms stall became ~16 truck / PhysX steps → a second and third long frame (HitchDiag: the spikes came
    /// in pairs / triples, FixedUpdate 100–300 ms right after a long frame). 0.1 s caps the catch-up at 5 steps; a longer stall
    /// just slows game time for that one frame (nobody can steer through it anyway).
    public static class FrameGuard
    {
        public const float MaxDelta = 0.1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Init()
        {
            if (Time.maximumDeltaTime > MaxDelta) Time.maximumDeltaTime = MaxDelta;
        }
    }
}
