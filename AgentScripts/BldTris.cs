using UnityEngine;
/// building triangles drawn from a point (LOD level chosen as in the player: fov 55, QualitySettings.lodBias; all directions, no frustum)
public static class BldTris {
  public static string Run(Vector3 p, float fov = 55) {
    long[] t = new long[3]; long sub1 = 0;
    foreach (var g in GameObject.Find("World/Buildings/town").GetComponentsInChildren<LODGroup>()) {
      var lods = g.GetLODs();
      float d = Vector3.Distance(p, g.transform.TransformPoint(g.localReferencePoint));
      float h = g.size * .5f / (d * Mathf.Tan(fov * .5f * Mathf.Deg2Rad)) * QualitySettings.lodBias;
      int l = lods.Length - 1; for (int i = 0; i < lods.Length; i++) if (h >= lods[i].screenRelativeTransitionHeight) { l = i; break; }
      var m = lods[l].renderers[0].GetComponent<MeshFilter>().sharedMesh; long n = 0; for (int s = 0; s < m.subMeshCount; s++) n += m.GetIndexCount(s) / 3;
      t[l] += n;
    }
    return $"at {p}: {(t[0] + t[1] + t[2]) / 1000}k (L0 {t[0] / 1000}k L1 {t[1] / 1000}k L2 {t[2] / 1000}k) lodBias {QualitySettings.lodBias}";
  }
  public static string All() => Run(new Vector3(1000, 50, -174)) + "\n" + Run(new Vector3(3049, 50, -555)) + "\n" + Run(new Vector3(-300, 50, 161));
}
