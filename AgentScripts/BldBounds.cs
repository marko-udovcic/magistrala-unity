using System.Text; using UnityEngine;
/// building positions check: per chunk, bounds of the far level (houses lod2: walls + glass + roofs; town lod1: walls + roofs)
/// and of level 0, written as text lines "name cx cy cz ex ey ez" (metres)
public static class BldBounds {
  public static string Run(string path) {
    var sb = new StringBuilder(); int n = 0;
    var root = GameObject.Find("World/Buildings/town"); if (!root) root = GameObject.Find("Buildings/town");
    foreach (var lg in root.GetComponentsInChildren<LODGroup>()) {
      var lods = lg.GetLODs();
      for (int l = 0; l < lods.Length; l += lods.Length - 1) {
        var m = lods[l].renderers[0].GetComponent<MeshFilter>().sharedMesh; var b = m.bounds;
        if (l == 0) { // level 0: only the walls' footprint and bottom (details may rise above)
          var v = m.vertices; var lo = new Vector3(1e9f,1e9f,1e9f); var hi = -lo;
          foreach (var p in v) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
          b = new Bounds((lo + hi) / 2, hi - lo); b = new Bounds(new Vector3(b.center.x, lo.y, b.center.z), new Vector3(b.size.x, 0, b.size.z));
        }
        sb.AppendLine($"{lg.name}/L{l} {b.center.x:F3} {b.center.y:F3} {b.center.z:F3} {b.extents.x:F3} {b.extents.y:F3} {b.extents.z:F3}"); n++;
      }
    }
    System.IO.File.WriteAllText(path, sb.ToString()); return $"{n} lines -> {path}";
  }
}
