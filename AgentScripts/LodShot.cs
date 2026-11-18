using UnityEngine;
public static class LodShot
{
    /// -1 = normal LOD selection, 0 = every building chunk at full detail (= the old meshes)
    public static string Force(int lod)
    {
        int n = 0; foreach (var g in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None)) { g.ForceLOD(lod); n++; }
        return $"force {lod} on {n}";
    }
    public static string Pause(bool p) { Time.timeScale = p ? 0 : 1; return "timeScale " + Time.timeScale; }
    public static string Levels()
    {
        var cam = Camera.main; var sb = new System.Text.StringBuilder(); int[] c = new int[3];
        foreach (var g in Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None))
        {
            var lods = g.GetLODs(); float d = Vector3.Distance(cam.transform.position, g.transform.TransformPoint(g.localReferencePoint));
            float h = g.size * .5f / (d * Mathf.Tan(cam.fieldOfView * .5f * Mathf.Deg2Rad)) * QualitySettings.lodBias;
            int l = lods.Length - 1; for (int i = 0; i < lods.Length; i++) if (h >= lods[i].screenRelativeTransitionHeight) { l = i; break; }
            c[l]++;
        }
        return $"cam {cam.transform.position} fov {cam.fieldOfView:F0} bias {QualitySettings.lodBias} levels {c[0]}/{c[1]}/{c[2]}";
    }
}
