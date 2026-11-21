using UnityEngine;

namespace Magistrala.Traffic
{
    /// Visible signal heads of the junction models (built by Magistrala ▸ Build signals from the owner's props in
    /// junction-overrides-modeli.json): each head shows the state of its signal group in the model's signal plan, on the same
    /// clock the traffic simulation uses for the stop lines (TrafficSystem.LightFor: 0 green, 1 amber, 2 red, 3 red + amber),
    /// so cars never stop at a red the player can't see. A head with corridor ≥ 0 (older scenes) follows that corridor stop.
    public class TrafficSignals : MonoBehaviour
    {
        [System.Serializable]
        public class Head
        {
            public int corridor = -1, stop;
            /// TrafficData.plans key ("krizanje-01") and signal group ("SG1") of the head
            public string planKey, group;
            /// the lit lenses' poses (red, amber, green): the dark lenses are part of the signs' static mesh, the lit ones are
            /// drawn over them — every lit lens of the world in three instanced draws (one per colour)
            public Matrix4x4 mRed, mAmber, mGreen;
            [System.NonSerialized] public int plan; [System.NonSerialized] public bool resolved;
        }

        /// the painted stop line of a signalised approach (model signals.stops with its group): the player's truck crossing it
        /// forwards while its group shows red raises RanRed
        [System.Serializable]
        public class Line { public Vector3 p, dir; public float hw; public string planKey, group; [System.NonSerialized] public int plan; [System.NonSerialized] public bool resolved; }

        public TrafficSystem traffic;
        public Head[] heads;
        public Line[] lines;
        /// the player drove through a red light (message for the HUD banner)
        public static event System.Action<string> RanRed;
        Transform truck; Vector3 prevPos; bool havePrev; float findT;
        public Material redOn, amberOn, greenOn, lensOff;
        public Mesh lens;
        float next;
        int[] shown;
        readonly System.Collections.Generic.List<Matrix4x4> litR = new System.Collections.Generic.List<Matrix4x4>(), litA = new System.Collections.Generic.List<Matrix4x4>(), litG = new System.Collections.Generic.List<Matrix4x4>();

        void FixedUpdate()
        {
            if (!traffic || lines == null || lines.Length == 0) return;
            if (truck && !truck.gameObject.activeInHierarchy) truck = null; // vehicle switched (MagistralaGame.Vehicles)
            if (!truck && Time.time > findT) { findT = Time.time + 2; var tc = FindFirstObjectByType<TruckController>(); if (tc) truck = tc.transform; havePrev = false; }
            if (!truck) return;
            var pos = truck.position;
            if (havePrev && (pos - prevPos).sqrMagnitude > 1e-4f && (pos - prevPos).sqrMagnitude < 25)
                foreach (var l in lines)
                {
                    var d = pos - l.p; if (d.sqrMagnitude > 900 || Mathf.Abs(d.y) > 4) continue;
                    float a0 = Vector3.Dot(prevPos - l.p, l.dir), a1 = Vector3.Dot(d, l.dir);
                    if (!(a0 < 0 && a1 >= 0)) continue;
                    var lat = d - l.dir * a1; lat.y = 0; if (lat.magnitude > l.hw + .8f) continue;
                    if (Vector3.Dot(truck.forward, l.dir) < .5f) continue; // forwards over it, not reversing / turning across
                    if (!l.resolved) { l.plan = traffic.PlanIndex(l.planKey); l.resolved = true; }
                    int st = traffic.LightFor(l.plan, l.group);
                    Debug.Log($"[semafor] kamion preko stop-crte {l.planKey} {l.group}: {(st == 0 ? "zeleno" : st == 1 ? "žuto" : st == 3 ? "crveno-žuto" : "crveno")}");
                    if (st >= 2) RanRed?.Invoke("Prošao si kroz crveno!");
                }
            prevPos = pos; havePrev = true;
        }

        void Update()
        {
            if (!traffic || heads == null) return;
            if (Time.time >= next)
            {
                next = Time.time + .1f;
                if (shown == null || shown.Length != heads.Length) { shown = new int[heads.Length]; for (int i = 0; i < shown.Length; i++) shown[i] = -1; }
                bool changed = false;
                for (int i = 0; i < heads.Length; i++)
                {
                    var h = heads[i];
                    if (h.corridor < 0 && !h.resolved) { h.plan = traffic.PlanIndex(h.planKey); h.resolved = true; }
                    int s = h.corridor >= 0 ? traffic.LightState(h.corridor, h.stop) : traffic.LightFor(h.plan, h.group);
                    if (s != shown[i]) { shown[i] = s; changed = true; }
                }
                if (changed)
                {
                    litR.Clear(); litA.Clear(); litG.Clear();
                    for (int i = 0; i < heads.Length; i++)
                    {
                        int s = shown[i];
                        if (s >= 2) litR.Add(heads[i].mRed);
                        if (s == 1 || s == 3) litA.Add(heads[i].mAmber);
                        if (s == 0) litG.Add(heads[i].mGreen);
                    }
                }
            }
            if (!lens) return;
            Draw(litR, redOn); Draw(litA, amberOn); Draw(litG, greenOn);
        }

        void Draw(System.Collections.Generic.List<Matrix4x4> m, Material mat)
        {
            if (m.Count == 0 || !mat) return;
            var rp = new RenderParams(mat) { shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows = false, worldBounds = new Bounds(Vector3.zero, Vector3.one * 30000) };
            Graphics.RenderMeshInstanced(rp, lens, 0, m);
        }
    }
}
