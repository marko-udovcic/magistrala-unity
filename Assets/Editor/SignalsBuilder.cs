// Magistrala ▸ Build signals (run after Igra: podaci and Build traffic): the signal furniture of the junction models and every
// road sign (SignsBuilder.cs), and the TrafficSignals component that switches the heads' lenses on the signal plans' clock —
// the same one the AI cars obey. Checks that every AI stop line has a visible head of its group.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Magistrala.Traffic;

namespace Magistrala.EditorTools
{
    public static class SignalsBuilder
    {
        const string GEN = WorldBuilder.GEN;

        static Material Mat(string name, Color c, float metal, float smooth, Color emit = default)
        {
            string p = $"{GEN}/Materials/Signal_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth); m.enableInstancing = true;
            if (emit.maxColorComponent > 0) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emit); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
            EditorUtility.SetDirty(m); return m;
        }

        [MenuItem("Magistrala/Build signals")]
        public static string Build()
        {
            var ts = Object.FindFirstObjectByType<TrafficSystem>();
            if (!ts || !ts.data) return "no TrafficSystem / data: run Magistrala/Build traffic first";
            var off = Mat("lens_off", new Color(.07f, .07f, .07f), 0f, .9f);
            var red = Mat("red_on", new Color(1f, .1f, .05f), 0f, .9f, new Color(1f, .06f, .02f) * 9f);
            var amber = Mat("amber_on", new Color(1f, .55f, .05f), 0f, .9f, new Color(1f, .42f, .0f) * 8f);
            var green = Mat("green_on", new Color(.1f, 1f, .5f), 0f, .9f, new Color(.08f, 1f, .45f) * 6f);

            // the heads, their poles / gantries and every road sign: SignsBuilder (from the owner's props and the graph)
            var root = WorldBuilder.Root("Signals");
            string report = SignsBuilder.Build(ts, off, out var heads, root.transform);
            var comp = root.AddComponent<TrafficSignals>();
            comp.traffic = ts; comp.heads = heads.ToArray(); comp.redOn = red; comp.amberOn = amber; comp.greenOn = green; comp.lensOff = off;
            comp.lines = SignsBuilder.StopLines().ToArray();

            // every stop line the AI cars stop at must have a head of its group facing it nearby (else they'd stop at an unseen red)
            var data = ts.data; int ok = 0; var miss = new List<string>();
            for (int ci = 0; ci < data.corridors.Length; ci++)
            {
                var c = data.corridors[ci]; if (c.stops == null) continue;
                foreach (var st in c.stops)
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt(st.s / c.step), 0, c.axis.Length - 2);
                    var p = c.axis[i]; var fwd = c.axis[i + 1] - c.axis[i]; fwd.y = 0; fwd.Normalize();
                    bool seen = heads.Exists(h => h.planKey == st.model && h.group == st.group && h.red && (h.red.transform.position - p).magnitude < 45
                        && Vector3.Dot(h.red.transform.forward, fwd) > .7f);
                    if (seen) ok++; else miss.Add($"{c.name} {st.model} {st.group} s {st.s:F0}");
                }
            }
            string msg = $"[Magistrala] semafori: {heads.Count} glava; stop-crte AI-ja s vidljivom glavom svoje skupine: {ok}, bez: {miss.Count}" + (miss.Count > 0 ? " (" + string.Join("; ", miss) + ")" : "");
            Debug.Log(msg); System.IO.File.AppendAllText("Logs/znakovi-izvjestaj.txt", msg + "\n");
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); EditorSceneManager.SaveOpenScenes();
            return msg + "\n" + report;
        }
    }
}
