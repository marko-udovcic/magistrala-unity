using UnityEngine; using UnityEditor; using System.IO;
/// contact sheet of the imported Tripo props (edit mode): each prefab alone high above the world, 3 views (front +Z faces camera,
/// side from +X, 3/4 from above); lod = which LOD to force
public static class PropSheet {
  public static string Run(string names, string file, int lod, int cell) {
    var list = names.Split(','); int cols = 3; int W = cell * cols, H = cell * list.Length;
    var sheet = new Texture2D(W, H, TextureFormat.RGB24, false);
    var go = new GameObject("propcam"); var cam = go.AddComponent<Camera>(); cam.fieldOfView = 30; cam.nearClipPlane = .05f; cam.farClipPlane = 5000;
    cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.55f, .62f, .7f);
    var rt = new RenderTexture(cell, cell, 24, RenderTextureFormat.ARGB32); rt.antiAliasing = 4; cam.targetTexture = rt;
    var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); ground.transform.position = new Vector3(0, 1500, 0); ground.transform.localScale = Vector3.one * 5;
    var gm = new Material(Shader.Find("Universal Render Pipeline/Lit")); gm.color = new Color(.5f, .5f, .48f); ground.GetComponent<Renderer>().sharedMaterial = gm;
    string log = "";
    for (int i = 0; i < list.Length; i++) {
      var pf = Magistrala.EditorTools.TripoPropImporter.Prefab(list[i]); if (!pf) { log += list[i] + " missing; "; continue; }
      var o = (GameObject)Object.Instantiate(pf); o.transform.position = new Vector3(0, 1500, 0);
      var lg = o.GetComponent<LODGroup>(); if (lg) lg.ForceLOD(lod);
      var b = new Bounds(o.transform.position, Vector3.zero); foreach (var r in o.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
      float R = b.extents.magnitude; float d = R / Mathf.Sin(cam.fieldOfView * .5f * Mathf.Deg2Rad) * 1.05f;
      Vector3[] dirs = { new Vector3(0, .15f, 1), new Vector3(1, .15f, 0), new Vector3(-.8f, .9f, -.8f) };
      for (int v = 0; v < 3; v++) {
        cam.transform.position = b.center + dirs[v].normalized * d; cam.transform.LookAt(b.center);
        cam.Render(); RenderTexture.active = rt;
        sheet.ReadPixels(new Rect(0, 0, cell, cell), v * cell, (list.Length - 1 - i) * cell); RenderTexture.active = null;
      }
      log += $"{list[i]} {b.size.x:F2}x{b.size.y:F2}x{b.size.z:F2}; ";
      Object.DestroyImmediate(o);
    }
    sheet.Apply(); File.WriteAllBytes(file, sheet.EncodeToPNG());
    cam.targetTexture = null; Object.DestroyImmediate(rt); Object.DestroyImmediate(go); Object.DestroyImmediate(ground); Object.DestroyImmediate(sheet);
    return log;
  }
}
