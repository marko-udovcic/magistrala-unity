using UnityEngine;

namespace Magistrala.Traffic
{
    /// Visible signal heads at the D8 stop lines (built by Magistrala ▸ Build signals): each head shows the state the
    /// traffic simulation uses for that stop (TrafficSystem.LightState: 0 green, 1 amber, 2 red), so cars never stop
    /// at a red the player can't see.
    public class TrafficSignals : MonoBehaviour
    {
        [System.Serializable]
        public class Head { public int corridor, stop; public Renderer red, amber, green; }

        public TrafficSystem traffic;
        public Head[] heads;
        public Material redOn, amberOn, greenOn, lensOff;
        float next;
        int[] shown;

        void Update()
        {
            if (!traffic || heads == null || Time.time < next) return;
            next = Time.time + .1f;
            if (shown == null || shown.Length != heads.Length) { shown = new int[heads.Length]; for (int i = 0; i < shown.Length; i++) shown[i] = -1; }
            for (int i = 0; i < heads.Length; i++)
            {
                var h = heads[i]; int s = traffic.LightState(h.corridor, h.stop);
                if (s == shown[i]) continue; shown[i] = s;
                h.red.sharedMaterial = s == 2 ? redOn : lensOff;
                h.amber.sharedMaterial = s == 1 ? amberOn : lensOff;
                h.green.sharedMaterial = s == 0 ? greenOn : lensOff;
            }
        }
    }
}
