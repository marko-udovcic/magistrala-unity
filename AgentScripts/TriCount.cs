using System.Collections.Generic; using System.Linq; using UnityEngine;
public static class TriCount {
  public static string Run() {
    var truck = Object.FindFirstObjectByType<TruckController>(); var c = truck ? truck.transform.position : Vector3.zero;
    var per = new Dictionary<string, (long tri, int n, long triNear, int shadow)>();
    foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)) {
      if (!r.enabled) continue; var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
      var chain = new List<string>(); for (var x = r.transform; x; x = x.parent) chain.Insert(0, x.name.Split(' ')[0]); var root = string.Join("/", chain.Take(Mathf.Min(3, chain.Count - 1 > 0 ? chain.Count - 1 : 1)));
      long t = 0; var m = mf.sharedMesh; for (int s = 0; s < m.subMeshCount; s++) t += (long)m.GetIndexCount(s) / 3;
      float d = Vector3.Distance(r.bounds.ClosestPoint(c), c);
      per.TryGetValue(root, out var v); v.tri += t; v.n++; if (d < 1500) v.triNear += t; if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) v.shadow++; per[root] = v;
    }
    var lods = Object.FindObjectsByType<LODGroup>(FindObjectsSortMode.None).Length;
    var terr = Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None);
    var sb = new System.Text.StringBuilder($"truck {c} LODGroups {lods} terrains {terr.Length} pixErr {(terr.Length>0?terr[0].heightmapPixelError:0)} basemapDist {(terr.Length>0?terr[0].basemapDistance:0)} detailDist {(terr.Length>0?terr[0].detailObjectDistance:0)} treeDist {(terr.Length>0?terr[0].treeDistance:0)}\n");
    foreach (var kv in per.OrderByDescending(k => k.Value.triNear)) sb.AppendLine($"{kv.Key}: renderers {kv.Value.n} tris {kv.Value.tri/1000}k within1.5km {kv.Value.triNear/1000}k shadowCasters {kv.Value.shadow}");
    var tr = Object.FindFirstObjectByType<Magistrala.TreeRenderer>();
    if (tr) { sb.AppendLine($"trees {tr.Count} shadowDist {tr.shadowDistance} impShadow {tr.impShadowDistance}");
      for (int p = 0; p < tr.meshes.Length; p++) if (tr.meshes[p]) sb.AppendLine($"  proto {p} {tr.meshes[p].name} tris {tr.meshes[p].triangles.Length/3} draw {tr.drawDistance[p]} near {tr.nearDistance[p]} imp {(tr.impMeshes!=null&&p<tr.impMeshes.Length&&tr.impMeshes[p]?tr.impMeshes[p].triangles.Length/3:-1)}"); }
    return sb.ToString();
  }
}
