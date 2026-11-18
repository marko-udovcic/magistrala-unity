using UnityEngine;
public static class Tris
{
    public static string Now() => $"tris {UnityEditor.UnityStats.triangles} batches {UnityEditor.UnityStats.batches} shadowCasters {UnityEditor.UnityStats.shadowCasters} frame {Time.frameCount}";
    /// rendered triangles of the building chunks' visible LOD level renderers per camera frustum (main view, no shadows)
    public static string Visible()
    {
        var cam = Camera.main; var pl = GeometryUtility.CalculateFrustumPlanes(cam); long[] t = new long[3]; long tot0 = 0;
        foreach (var g in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
        {
            var lods = g.GetLODs(); if (!GeometryUtility.TestPlanesAABB(pl, lods[0].renderers[0].bounds)) continue;
            float d = Vector3.Distance(cam.transform.position, g.transform.TransformPoint(g.localReferencePoint));
            float h = g.size * .5f / (d * Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad)) * QualitySettings.lodBias;
            int l = lods.Length - 1; for (int i = 0; i < lods.Length; i++) if (h >= lods[i].screenRelativeTransitionHeight) { l = i; break; }
            var m = lods[l].renderers[0].GetComponent<MeshFilter>().sharedMesh; long n = 0; for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3;
            var m0 = lods[0].renderers[0].GetComponent<MeshFilter>().sharedMesh; long n0 = 0; for (int s = 0; s < m0.subMeshCount; s++) n0 += m0.GetIndexCount(s) / 3;
            t[l] += n; tot0 += n0;
        }
        return $"in frustum: full-detail {tot0 / 1000}k -> with LODs {(t[0] + t[1] + t[2]) / 1000}k (L0 {t[0] / 1000}k L1 {t[1] / 1000}k L2 {t[2] / 1000}k)";
    }
}
