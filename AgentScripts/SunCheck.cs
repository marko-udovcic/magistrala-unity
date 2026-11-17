using UnityEngine;
public static class SunCheck
{
    // render the sky toward the light's sun; report where the brightest sky pixel is (deg right / up of the light's sun)
    public static string Check(float extraRot)
    {
        var sun = Object.FindFirstObjectByType<Magistrala.LightPreset>().GetComponent<Light>();
        var m = RenderSettings.skybox; m.SetFloat("_Rotation", ((m.GetFloat("_Rotation") + extraRot) % 360f + 360f) % 360f);
        var go = new GameObject("suncheck"); var cam = go.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.Skybox; cam.cullingMask = 0; cam.fieldOfView = 90; cam.aspect = 1;
        go.transform.position = new Vector3(0, 5000, 0); go.transform.rotation = Quaternion.LookRotation(-sun.transform.forward);
        var rt = new RenderTexture(256, 256, 24, RenderTextureFormat.ARGBFloat); cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var t = new Texture2D(256, 256, TextureFormat.RGBAFloat, false); t.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); RenderTexture.active = null;
        var px = t.GetPixels(); int best = 0; float bl = 0; for (int i = 0; i < px.Length; i++) { float l = px[i].r + px[i].g + px[i].b; if (l > bl) { bl = l; best = i; } }
        float x = (best % 256 + .5f) / 128f - 1, y = (best / 256 + .5f) / 128f - 1;
        Object.DestroyImmediate(go); rt.Release();
        return $"{m.name} rot {m.GetFloat("_Rotation"):F1}: brightest at {Mathf.Atan(x) * Mathf.Rad2Deg:F1}° right, {Mathf.Atan(y) * Mathf.Rad2Deg:F1}° up, lum {bl:F1}";
    }
}
public static class SunScan
{
    public static string Scan(bool golden)
    {
        Object.FindFirstObjectByType<Magistrala.LightPreset>().SetGolden(golden);
        var m = RenderSettings.skybox; float r0 = m.GetFloat("_Rotation"); var sb = new System.Text.StringBuilder();
        for (int k = 0; k < 8; k++) { sb.AppendLine(SunCheck.Check(k == 0 ? 0 : 45)); }
        m.SetFloat("_Rotation", r0); return sb.ToString();
    }
}
