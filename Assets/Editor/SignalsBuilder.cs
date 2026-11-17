// Signal heads at the D8 stop lines of the traffic corridors (Magistrala ▸ Build signals; run after Build traffic):
// a galvanised pole on the right verge with a head at 3 m, and a mast arm over the lanes with a second head above
// them. Lamps swap materials from TrafficSignals, following the same signal plans the AI cars obey.
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

        static GameObject Prim(string mesh, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, Material mat, bool shadows = true)
        {
            var go = new GameObject(mesh); go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot); go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>(mesh + ".fbx");
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        [MenuItem("Magistrala/Build signals")]
        public static string Build()
        {
            var ts = Object.FindFirstObjectByType<TrafficSystem>();
            if (!ts || !ts.data) return "no TrafficSystem / data: run Magistrala/Build traffic first";
            var pole = Mat("pole", new Color(.55f, .57f, .58f), .7f, .45f);
            var body = Mat("head", new Color(.04f, .04f, .045f), 0f, .35f);
            var off = Mat("lens_off", new Color(.07f, .07f, .07f), 0f, .9f);
            var red = Mat("red_on", new Color(1f, .1f, .05f), 0f, .9f, new Color(1f, .06f, .02f) * 9f);
            var amber = Mat("amber_on", new Color(1f, .55f, .05f), 0f, .9f, new Color(1f, .42f, .0f) * 8f);
            var green = Mat("green_on", new Color(.1f, 1f, .5f), 0f, .9f, new Color(.08f, 1f, .45f) * 6f);

            var root = WorldBuilder.Root("Signals");
            var heads = new List<TrafficSignals.Head>();
            var data = ts.data;

            void HeadAt(Transform parent, Vector3 pos, Vector3 facing, int ci, int si)
            {
                var rot = Quaternion.LookRotation(facing, Vector3.up); // lamps on the +facing side
                var h = Prim("Cube", parent, pos, rot, new Vector3(.36f, 1.02f, .26f), body);
                h.isStatic = true;
                Renderer Lamp(float dy)
                {
                    var l = Prim("Sphere", parent, pos + Vector3.up * dy + facing * .12f, rot, new Vector3(.22f, .22f, .07f), off, false);
                    return l.GetComponent<Renderer>();
                }
                heads.Add(new TrafficSignals.Head { corridor = ci, stop = si, red = Lamp(.32f), amber = Lamp(0), green = Lamp(-.32f) });
            }

            int n = 0;
            for (int ci = 0; ci < data.corridors.Length; ci++)
            {
                var c = data.corridors[ci]; if (c.stops == null) continue;
                for (int si = 0; si < c.stops.Length; si++)
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt(c.stops[si].s / c.step), 0, c.axis.Length - 2);
                    var fwd = c.axis[i + 1] - c.axis[i]; fwd.y = 0; fwd.Normalize();
                    var right = c.right[i];
                    float lo = float.MaxValue, hi = float.MinValue, y = c.axis[i].y;
                    for (int k = 0; k < c.maxLanes; k++)
                    {
                        float o = c.laneOff[i * c.maxLanes + k]; if (float.IsNaN(o)) continue;
                        if (o < lo) lo = o; if (o > hi) { hi = o; y = c.laneY[i * c.maxLanes + k]; }
                    }
                    if (lo > hi) continue;
                    var g = new GameObject($"signal {c.name} {c.stops[si].model} {c.stops[si].group}").transform; g.SetParent(root.transform, false);
                    // pole just past the stop line on the right verge, so a driver at the line still sees the arm head ahead
                    var basePos = c.axis[i] + right * (hi + 1.75f + 1.1f) + fwd * 1.5f; basePos.y = y;
                    const float H = 6.3f;
                    Prim("Cylinder", g, basePos + Vector3.up * H * .5f, Quaternion.identity, new Vector3(.18f, H * .5f, .18f), pole).isStatic = true;
                    float armLen = (hi + 1.75f + 1.1f) - (lo + hi) * .5f + 1.2f;
                    var armMid = basePos - right * armLen * .5f + Vector3.up * (H - .15f);
                    Prim("Cube", g, armMid, Quaternion.LookRotation(fwd, Vector3.up), new Vector3(armLen, .14f, .14f), pole).isStatic = true;
                    HeadAt(g, basePos + Vector3.up * 3.1f - fwd * .2f, -fwd, ci, si);
                    HeadAt(g, basePos - right * (armLen - 1.2f) + Vector3.up * (H - .75f) - fwd * .2f, -fwd, ci, si);
                    n++;
                }
            }
            var comp = root.AddComponent<TrafficSignals>();
            comp.traffic = ts; comp.heads = heads.ToArray(); comp.redOn = red; comp.amberOn = amber; comp.greenOn = green; comp.lensOff = off;
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene()); EditorSceneManager.SaveOpenScenes();
            string msg = $"[Magistrala] {n} semafora ({heads.Count} glava)"; Debug.Log(msg); return msg;
        }
    }
}
