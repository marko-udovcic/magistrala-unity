using UnityEngine;
public static class ImgDiff
{
    /// pixels differing by > thr (max channel, 0..255), their bbox, and a x8 difference image
    public static string Run(string a, string b, string outPng, int thr)
    {
        var A = new Texture2D(2, 2); A.LoadImage(System.IO.File.ReadAllBytes(a)); var B = new Texture2D(2, 2); B.LoadImage(System.IO.File.ReadAllBytes(b));
        var pa = A.GetPixels32(); var pb = B.GetPixels32(); var o = new Color32[pa.Length]; int n = 0, x0 = 99999, y0 = 99999, x1 = -1, y1 = -1; long sum = 0;
        for (int i = 0; i < pa.Length; i++)
        {
            int d = Mathf.Max(Mathf.Abs(pa[i].r - pb[i].r), Mathf.Max(Mathf.Abs(pa[i].g - pb[i].g), Mathf.Abs(pa[i].b - pb[i].b))); sum += d;
            byte v = (byte)Mathf.Min(255, d * 8); o[i] = new Color32(v, v, v, 255);
            if (d > thr) { n++; int x = i % A.width, y = A.height - 1 - i / A.width; x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x); y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y); }
        }
        var O = new Texture2D(A.width, A.height); O.SetPixels32(o); O.Apply(); System.IO.File.WriteAllBytes(outPng, O.EncodeToPNG());
        return $"{A.width}x{A.height}: {n} px > {thr} ({100.0 * n / pa.Length:F3} %), mean diff {(double)sum / pa.Length:F3}, bbox x {x0}..{x1} y {y0}..{y1}";
    }
}
public static class ImgCrop
{
    /// side by side crops (x, y from top, w, h) of two images, scaled k times (nearest)
    public static string Run(string a, string b, string outPng, int x, int y, int w, int h, int k)
    {
        var O = new Texture2D(w * k * 2 + 4, h * k); int i = 0;
        foreach (var f in new[] { a, b })
        {
            var T = new Texture2D(2, 2); T.LoadImage(System.IO.File.ReadAllBytes(f));
            for (int yy = 0; yy < h * k; yy++) for (int xx = 0; xx < w * k; xx++)
                O.SetPixel(i * (w * k + 4) + xx, h * k - 1 - yy, T.GetPixel(x + xx / k, T.height - 1 - (y + yy / k)));
            i++;
        }
        O.Apply(); System.IO.File.WriteAllBytes(outPng, O.EncodeToPNG()); return outPng;
    }
}
