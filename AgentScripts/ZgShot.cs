using UnityEngine;
/// close-up of the nearest detailed house to a world point (edit mode): camera d m from the nearest wall corner, looking at it
public static class ZgShot {
  public static string Run(Vector3 p, float d, float h, string file, float yaw = 0) {
    Vector3 best = Vector3.zero; float bd = 1e9f;
    foreach (var mf in GameObject.Find("World/Buildings/town").GetComponentsInChildren<MeshFilter>()) {
      if (!mf.name.StartsWith("houses")) continue; var m = mf.sharedMesh; if (!m.bounds.Intersects(new Bounds(p, Vector3.one * 400))) continue;
      foreach (var v in m.vertices) { float dd = (new Vector2(v.x - p.x, v.z - p.z)).magnitude; if (dd > 12 && dd < bd) { bd = dd; best = v; } }
    }
    var dir = new Vector3(p.x - best.x, 0, p.z - best.z).normalized; dir = Quaternion.Euler(0, yaw, 0) * dir;
    var go = new GameObject("zgcam"); var cam = go.AddComponent<Camera>(); cam.fieldOfView = 55; cam.farClipPlane = 3000; cam.nearClipPlane = .1f;
    go.transform.position = best + dir * d + Vector3.up * h; go.transform.LookAt(best + Vector3.up * (h * .6f));
    var rt = new RenderTexture(1280, 800, 24, RenderTextureFormat.ARGB32); cam.targetTexture = rt; cam.Render();
    RenderTexture.active = rt; var t = new Texture2D(1280, 800, TextureFormat.RGB24, false); t.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); t.Apply();
    System.IO.File.WriteAllBytes(file, t.EncodeToPNG()); RenderTexture.active = null; cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(go); Object.DestroyImmediate(t);
    return $"corner {best} dist {bd:F0} -> {file}";
  }
}
