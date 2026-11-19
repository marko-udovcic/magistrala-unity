using UnityEngine; using System.Linq;
public static class SceneInfo {
  public static string Run(string path) {
    var r = GameObject.Find(path); if (!r) return "no " + path;
    var s = new System.Text.StringBuilder();
    foreach (Transform c in r.transform) {
      var cols = c.GetComponentsInChildren<Collider>(true); var mrs = c.GetComponentsInChildren<MeshRenderer>(true);
      s.Append($"{c.name}: children {c.childCount}, colliders {cols.Length} ({string.Join(",", cols.Select(x=>x.GetType().Name).Distinct())}), renderers {mrs.Length}, comps {string.Join(",", c.GetComponents<Component>().Select(x=>x.GetType().Name))}\n");
    }
    return s.ToString();
  }
}
