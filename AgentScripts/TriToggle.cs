using System.Linq; using UnityEngine; using UnityEditor;
public static class TriToggle {
  public static string Stats() => $"tris={UnityStats.triangles/1000}k batches={UnityStats.batches} setpass={UnityStats.setPassCalls} shadowCasters={UnityStats.shadowCasters} frame={UnityStats.frameTime*1000:F1} render={UnityStats.renderTime*1000:F1}";
  public static string Set(string what, bool on) {
    switch (what) {
      case "trees": foreach (var t in Object.FindObjectsByType<Magistrala.TreeRenderer>(FindObjectsSortMode.None)) t.enabled = on; break;
      case "terrain": foreach (var t in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None)) t.drawHeightmap = on; break;
      case "shadows": RenderSettings.sun.shadows = on ? LightShadows.Soft : LightShadows.None; break;
      default: var g = GameObject.Find(what); if (!g) { foreach (var x in Resources.FindObjectsOfTypeAll<Transform>()) if (x.name == what.Split('/').Last() && x.gameObject.scene.IsValid()) { g = x.gameObject; break; } } if (g) g.SetActive(on); else return "not found " + what; break;
    }
    return what + " " + on;
  }
}
