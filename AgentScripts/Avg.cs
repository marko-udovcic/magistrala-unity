using UnityEngine;
public static class Avg
{
    public static string Run()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var f in new[] { "grass_olive_d.jpg", "soil_ochre_d.jpg", "rock_grey_d.jpg", "PolyHaven/gravel_ground_01_diff.jpg", "PolyHaven/dry_ground_rocks_diff.jpg", "PolyHaven/aerial_grass_rock_diff.jpg" })
        {
            var t = new Texture2D(2, 2); t.LoadImage(System.IO.File.ReadAllBytes("Assets/Textures/" + f)); var px = t.GetPixels(); double r = 0, g = 0, b = 0;
            foreach (var c in px) { r += c.r; g += c.g; b += c.b; }
            r /= px.Length; g /= px.Length; b /= px.Length; sb.AppendLine($"{f} {t.width}x{t.height} srgb avg {r:F2} {g:F2} {b:F2}  -> {(int)(r * 255)},{(int)(g * 255)},{(int)(b * 255)}");
        }
        return sb.ToString();
    }
}
