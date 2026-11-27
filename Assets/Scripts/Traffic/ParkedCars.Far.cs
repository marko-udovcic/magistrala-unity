// Static far-band lists are built once on page attachment, never collected per car each frame.
// Same 128 m spatial buckets as the near renderer; all models/colours share one 72-triangle proxy.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.Traffic
{
    public partial class ParkedCars
    {
        [Min(300), Tooltip("Proxy draw end in metres, multiplied by the quality distance scale. Default reaches 520 m at brzo.")]
        public float farDistance = 650;
        [Min(1)] public float transitionBand = 45;
        [Min(1)] public float farFadeBand = 80;
        // A/B in the SAME player: restores original range, collection and shadow submissions.
        bool baseline, logStats, farDebug, sampleFarStats;
        int visibleProxyInstances;
        float nearestProxyDistance, farthestProxyDistance;
        Vector4 currentProxyBand;
        float statsTime; int statsFrames; double statsCpu, statsMain, statsShadow, statsFar, statsInstances;
        void LogFarStats(float cpu)
        {
            if (!logStats) return;
            statsFrames++; statsCpu += cpu; statsMain += CallsMain; statsShadow += CallsShadow;
            statsFar += CallsFar; statsInstances += FarInstances; statsTime += Time.unscaledDeltaTime;
            if (statsTime < 5) return;
            string distances = visibleProxyInstances > 0 ? $"{nearestProxyDistance:F1}..{farthestProxyDistance:F1} m XZ" : "none";
            Debug.Log($"[parked] baseline={baseline} cpu={statsCpu / statsFrames:F3} ms/frame main={statsMain / statsFrames:F1} shadow={statsShadow / statsFrames:F1} far={statsFar / statsFrames:F1} proxy instances={statsInstances / statsFrames:F1} | current frame: proxy passed CPU culling={visibleProxyInstances} nearest/farthest={distances}");
            statsFrames = 0; statsCpu = statsMain = statsShadow = statsFar = statsInstances = 0; statsTime = 0;
        }
        sealed class FarBatch
        {
            public Matrix4x4[] matrices;
            public MaterialPropertyBlock props;
            public Bounds bounds;
        }
        Mesh proxyMesh;
        Material proxyMat;
        static readonly int ID_ProxyBand = Shader.PropertyToID("_ProxyBand");
        static readonly int ID_ProxyEye = Shader.PropertyToID("_ProxyEye");
        public static int CallsFar, FarInstances;

        void BuildFar(Page pg)
        {
            // Also used for conservative near-page frustum rejection (including unbaked kinds).
            pg.bounds = new Bounds();
            var batches = new List<FarBatch>();
            for (int b = 0; b < pg.nb; b++)
            {
                var matrices = new List<Matrix4x4>(); var tints = new List<Vector4>();
                Bounds bounds = default; bool first = true;
                void Flush()
                {
                    if (matrices.Count == 0) return;
                    var props = new MaterialPropertyBlock(); props.SetVectorArray("_ParkTint", tints);
                    batches.Add(new FarBatch { matrices = matrices.ToArray(), props = props, bounds = bounds });
                    matrices.Clear(); tints.Clear(); first = true;
                }
                for (int i = pg.bo[b], end = pg.bo[b] + pg.bn[b]; i < end; i++)
                {
                    var kd = data.kinds[pg.kind[i]]; var m = pg.M[i];
                    var cb = new Bounds(new Vector3(m.m03, m.m13 + kd.h * .5f, m.m23), new Vector3(kd.len + kd.halfW * 2, kd.h, kd.len + kd.halfW * 2));
                    if (b == 0 && i == pg.bo[b]) pg.bounds = cb; else pg.bounds.Encapsulate(cb);
                    if (!baked[pg.kind[i]]) continue;
                    // Unit proxy: x width, y height above ground, z length. Preserve the parked yaw and ground height.
                    m.m00 *= kd.halfW * 2; m.m20 *= kd.halfW * 2;
                    m.m11 *= kd.h; m.m02 *= kd.len; m.m22 *= kd.len;
                    matrices.Add(m); tints.Add(pg.tint[i]);
                    if (first) { bounds = cb; first = false; } else bounds.Encapsulate(cb);
                    if (matrices.Count == BATCH) Flush();
                }
                Flush();
            }
            pg.far = batches.ToArray();
        }

        void BeginFar(Vector3 eye, float start, float nearEnd, float farEnd)
        {
            CallsFar = FarInstances = 0;
            if (baseline || Legacy || ForceLevel >= 0) return;
            if (!proxyMat)
            {
                // The asset's Enable Instancing flag is essential at BUILD time: loading a
                // shader and enabling instancing only at runtime let Strip Unused remove
                // INSTANCING_ON. The resulting Metal program had no instance_id/matrix array.
                var template = Resources.Load<Material>("ParkedFar");
                if (!template || !template.enableInstancing)
                {
                    Debug.LogError("[parked] Resources/ParkedFar.mat must exist with Enable Instancing enabled");
                    return;
                }
                proxyMat = new Material(template) { name = "Parked far proxy", enableInstancing = true };
                if (farDebug) proxyMat.EnableKeyword("_PARKEDFARDEBUG");
                proxyMesh = MakeProxy();
                if (logStats || farDebug)
                    Debug.Log($"[parked] far material={template.name} shader={proxyMat.shader.name} supported={proxyMat.shader.isSupported} instancing={proxyMat.enableInstancing} debug={farDebug}");
            }
            proxyMat.SetVector(ID_ProxyEye, eye);
            currentProxyBand = new Vector4(start, nearEnd, Mathf.Max(nearEnd, farEnd - farFadeBand), farEnd);
            proxyMat.SetVector(ID_ProxyBand, currentProxyBand);
        }

        void DrawFar(Page pg, Camera target, Vector3 eye, float start, float end)
        {
            if (baseline || Legacy || ForceLevel >= 0 || !proxyMat || pg.far == null) return;
            foreach (var batch in pg.far)
            {
                var b = batch.bounds;
                var horizontalEye = new Vector3(eye.x, b.center.y, eye.z);
                if (b.SqrDistance(horizontalEye) > end * end) continue;
                // Whole batches inside the near band need no proxy submission. Use 3D here, like ParkedCar's fade.
                var d = b.center - eye; var e = b.extents;
                if ((d.magnitude + e.magnitude) < start) continue;
                if (!GeometryUtility.TestPlanesAABB(planes, b)) continue;
                Graphics.RenderMeshInstanced(new RenderParams(proxyMat) { shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false, worldBounds = b, layer = gameObject.layer, camera = target, matProps = batch.props },
                    proxyMesh, 0, batch.matrices, batch.matrices.Length);
                CallsMain++; CallsFar++; FarInstances += batch.matrices.Length;
                if (sampleFarStats) CountVisibleProxies(batch, eye);
                usedMats.Add(proxyMat);
            }
        }

        // Diagnostic only: walk individual far cars on the 5-second reporting frame,
        // only with -parkedstats enabled.
        // Count instances surviving the same band clipping plus an individual proxy-frustum
        // test, rather than treating every instance in a submitted bucket as visible.
        void CountVisibleProxies(FarBatch batch, Vector3 eye)
        {
            var local = proxyMesh.bounds;
            foreach (var m in batch.matrices)
            {
                var delta = new Vector3(m.m03 - eye.x, m.m13 - eye.y, m.m23 - eye.z);
                float distance = new Vector2(delta.x, delta.z).magnitude;
                float originDistance = delta.magnitude;
                if (originDistance <= currentProxyBand.x || distance >= currentProxyBand.w) continue;
                if (!farDebug)
                {
                    // Bayer thresholds are (i+.5)/16: at least one sample must survive
                    // BOTH the inner and outer clips (including narrow/custom fade bands).
                    float inner = Mathf.Clamp01((currentProxyBand.y - originDistance) / Mathf.Max(.001f, currentProxyBand.y - currentProxyBand.x));
                    float outer = Mathf.Clamp01((currentProxyBand.w - distance) / Mathf.Max(.001f, currentProxyBand.w - currentProxyBand.z));
                    int sample = Mathf.Max(0, Mathf.CeilToInt(inner * 16 - .5f));
                    if (sample > 15 || (sample + .5f) / 16 > outer) continue;
                }
                var e = local.extents;
                var extents = new Vector3(Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m02) * e.z,
                    Mathf.Abs(m.m11) * e.y, Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m22) * e.z);
                if (!GeometryUtility.TestPlanesAABB(planes, new Bounds(m.MultiplyPoint3x4(local.center), extents * 2))) continue;
                visibleProxyInstances++;
                nearestProxyDistance = Mathf.Min(nearestProxyDistance, distance);
                farthestProxyDistance = Mathf.Max(farthestProxyDistance, distance);
            }
        }

        static Mesh MakeProxy()
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>();
            var colours = new List<Color>(); var triangles = new List<int>();
            void Box(Vector3 centre, Vector3 size, Color colour, bool cabin = false)
            {
                foreach (var n in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
                {
                    var u = n.y != 0 ? Vector3.right : Vector3.up; var v = Vector3.Cross(n, u);
                    int offset = vertices.Count;
                    foreach (var q in new[] { -u - v, u - v, u + v, -u + v })
                    {
                        vertices.Add(centre + Vector3.Scale(n + q, size) * .5f); normals.Add(n);
                        // Alpha = paint weight. Cabin roof is paint; its sides are dark glass.
                        colours.Add(cabin && n.y == 0 ? new Color(.08f, .11f, .14f, 0) : colour);
                    }
                    triangles.AddRange(new[] { offset, offset + 1, offset + 2, offset, offset + 2, offset + 3 });
                }
            }
            Box(new Vector3(0, .36f, 0), new Vector3(.94f, .43f, 1), new Color(1, 1, 1, 1));
            Box(new Vector3(0, .76f, -.04f), new Vector3(.8f, .48f, .55f), new Color(1, 1, 1, 1), true);
            foreach (float x in new[] { -.43f, .43f })
                foreach (float z in new[] { -.32f, .32f })
                    Box(new Vector3(x, .14f, z), new Vector3(.14f, .28f, .15f), new Color(.025f, .025f, .025f, 0));
            var mesh = new Mesh { name = "Parked proxy (72 triangles)" };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetColors(colours); mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }

        void OnDestroy()
        {
            void Drop(Object obj)
            {
                if (!obj) return;
                if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
            }
            Drop(proxyMat); Drop(proxyMesh);
            if (fadeMat != null) foreach (var mat in fadeMat) Drop(mat);
        }
    }
}
