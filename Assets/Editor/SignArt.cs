// Sign faces for Magistrala ▸ Build signals (SignsBuilder.cs), painted on the CPU into one texture atlas at build time:
//   shapes   signed-distance fills (anti-aliased edges): discs, rings, octagon, triangles, diamonds, rounded boxes, capsules;
//   text     Inter SemiBold (Assets/Resources/Fonts, SIL OFL 1.1 — has č ć ž š đ) rasterised through a TextMesh Pro font asset made
//            in memory (GlyphRenderMode.SMOOTH: plain coverage bitmaps), scaled with a box filter into the face;
//   atlas    shelf-packed cells (edge pixels repeated into a 6 px gutter so mipmaps don't bleed), written to
//            Assets/Generated/Signs/SignAtlas.png; the cell rectangles (uv) are kept for the mesh builder.
// The faces follow the Croatian sign catalogue in look (Pravilnik o prometnim znakovima: red-rimmed limit discs, STOP octagon,
// give-way triangle, yellow priority diamond, blue pedestrian-crossing square, blue keep-right disc, white place-name boards
// with black text and the red bar of the "end of place" board, direction boards in the colour the research package gives);
// sizes and typeface are approximations, no real brand names or logos.
using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Magistrala.EditorTools
{
    public class SignCanvas
    {
        public readonly int w, h;
        public readonly Color[] px; // row 0 = top, straight alpha, sRGB values
        public SignCanvas(int w, int h) { this.w = w; this.h = h; px = new Color[w * h]; }

        public void Blend(int x, int y, Color c, float a)
        {
            if (x < 0 || y < 0 || x >= w || y >= h || a <= 0) return;
            int i = y * w + x; a *= c.a; var d = px[i];
            float oa = a + d.a * (1 - a); if (oa <= 1e-5f) return;
            px[i] = new Color((c.r * a + d.r * d.a * (1 - a)) / oa, (c.g * a + d.g * d.a * (1 - a)) / oa, (c.b * a + d.b * d.a * (1 - a)) / oa, oa);
        }

        /// fill where the signed distance (px, < 0 inside) says so, anti-aliased over one pixel
        public void Fill(Func<float, float, float> sd, Color c)
        {
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float d = sd(x + .5f, y + .5f);
                    if (d < .5f) Blend(x, y, c, Mathf.Clamp01(.5f - d));
                }
        }
        public void Clear(Color c) { for (int i = 0; i < px.Length; i++) px[i] = c; }
    }

    public static class Sd
    {
        public static Func<float, float, float> Circle(float cx, float cy, float r) => (x, y) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;
        public static Func<float, float, float> Ring(float cx, float cy, float r, float width) => (x, y) => Mathf.Abs(Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - (r - width / 2)) - width / 2;
        public static Func<float, float, float> Box(float cx, float cy, float hw, float hh, float rad = 0) => (x, y) =>
        {
            float qx = Mathf.Abs(x - cx) - hw + rad, qy = Mathf.Abs(y - cy) - hh + rad;
            return new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - rad;
        };
        /// convex polygon (any winding): the largest signed distance to its edge lines
        public static Func<float, float, float> Poly(params Vector2[] p)
        {
            var c = Vector2.zero; foreach (var q in p) c += q; c /= p.Length;
            var n = new Vector2[p.Length]; var o = new float[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                var a = p[i]; var b = p[(i + 1) % p.Length]; var e = (b - a).normalized; var nn = new Vector2(e.y, -e.x);
                if (Vector2.Dot(nn, c - a) > 0) nn = -nn;
                n[i] = nn; o[i] = Vector2.Dot(nn, a);
            }
            return (x, y) => { float d = -1e9f; for (int i = 0; i < n.Length; i++) d = Mathf.Max(d, n[i].x * x + n[i].y * y - o[i]); return d; };
        }
        public static Func<float, float, float> Capsule(float ax, float ay, float bx, float by, float r) => (x, y) =>
        {
            var pa = new Vector2(x - ax, y - ay); var ba = new Vector2(bx - ax, by - ay);
            float t = Mathf.Clamp01(Vector2.Dot(pa, ba) / Mathf.Max(1e-6f, ba.sqrMagnitude));
            return (pa - ba * t).magnitude - r;
        };
        public static Func<float, float, float> Union(params Func<float, float, float>[] f) => (x, y) => { float d = 1e9f; foreach (var g in f) d = Mathf.Min(d, g(x, y)); return d; };
        public static Func<float, float, float> Inset(Func<float, float, float> f, float by) => (x, y) => f(x, y) + by;
        /// regular polygon (n corners, circumradius r, first corner at angle a0)
        public static Vector2[] Regular(float cx, float cy, float r, int n, float a0)
        {
            var p = new Vector2[n]; for (int i = 0; i < n; i++) { float a = a0 + i * Mathf.PI * 2 / n; p[i] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r); }
            return p;
        }
        /// arrow from (x0, y0) to (x1, y1): shaft half width t, head length hl, head half width hw
        public static Func<float, float, float> Arrow(float x0, float y0, float x1, float y1, float t, float hl, float hw)
        {
            var a = new Vector2(x0, y0); var b = new Vector2(x1, y1); var d = (b - a).normalized; var n = new Vector2(-d.y, d.x);
            var hb = b - d * hl;
            var shaft = Poly(a + n * t, hb + d * 1f + n * t, hb + d * 1f - n * t, a - n * t);
            var head = Poly(b, hb + n * hw, hb - n * hw);
            return Union(shaft, head);
        }
    }

    /// text through a TMP font asset made in memory from Inter SemiBold (coverage glyphs, sampled with a box filter)
    public class SignFont
    {
        TMP_FontAsset fa; Color32[] atlas; int aw, ah;
        public float cap, asc, desc;
        public static SignFont Load()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Resources/Fonts/Inter-SemiBold.ttf");
            if (!font) throw new Exception("Inter-SemiBold.ttf missing (Assets/Resources/Fonts)");
            var f = new SignFont();
            f.fa = TMP_FontAsset.CreateFontAsset(font, 128, 6, GlyphRenderMode.SMOOTH, 2048, 2048, AtlasPopulationMode.Dynamic, false);
            f.cap = f.fa.faceInfo.capLine; f.asc = f.fa.faceInfo.ascentLine; f.desc = f.fa.faceInfo.descentLine;
            if (f.cap <= 1) f.cap = 128 * .727f;
            return f;
        }
        void Ensure(string s)
        {
            bool add = false;
            foreach (char ch in s) if (!fa.characterLookupTable.ContainsKey(ch)) { add = true; break; }
            if (!add && atlas != null) return;
            if (add) fa.TryAddCharacters(s, out _);
            var tex = fa.atlasTexture;
            aw = tex.width; ah = tex.height;
            try { atlas = tex.GetPixels32(); }
            catch
            {
                var rt = RenderTexture.GetTemporary(aw, ah, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(tex, rt); var prev = RenderTexture.active; RenderTexture.active = rt;
                var t2 = new Texture2D(aw, ah, TextureFormat.RGBA32, false); t2.ReadPixels(new Rect(0, 0, aw, ah), 0, 0); t2.Apply();
                RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt);
                atlas = t2.GetPixels32(); UnityEngine.Object.DestroyImmediate(t2);
            }
        }
        float Cov(int x, int y) => x < 0 || y < 0 || x >= aw || y >= ah ? 0 : atlas[y * aw + x].a / 255f;

        /// advance width of s in canvas px at cap height capPx
        public float Width(string s, float capPx)
        {
            Ensure(s); float k = capPx / cap, w = 0;
            foreach (char ch in s) if (fa.characterLookupTable.TryGetValue(ch, out var c)) w += c.glyph.metrics.horizontalAdvance * k;
            return w;
        }
        /// draws s with its left end at x and its baseline at y (canvas px, y down), cap height capPx
        public void Draw(SignCanvas cv, string s, float x, float y, float capPx, Color col)
        {
            Ensure(s); float k = capPx / cap; int ss = Mathf.Clamp(Mathf.CeilToInt(1 / k), 1, 4);
            float pen = x;
            foreach (char ch in s)
            {
                if (!fa.characterLookupTable.TryGetValue(ch, out var c)) continue;
                var g = c.glyph; var m = g.metrics; var r = g.glyphRect;
                float left = pen + m.horizontalBearingX * k, top = y - m.horizontalBearingY * k, gw = m.width * k, gh = m.height * k;
                for (int iy = Mathf.FloorToInt(top); iy < Mathf.CeilToInt(top + gh); iy++)
                    for (int ix = Mathf.FloorToInt(left); ix < Mathf.CeilToInt(left + gw); ix++)
                    {
                        float acc = 0;
                        for (int sy = 0; sy < ss; sy++)
                            for (int sx = 0; sx < ss; sx++)
                            {
                                float u = (ix + (sx + .5f) / ss - left) / k, v = (iy + (sy + .5f) / ss - top) / k; // glyph px from its top-left
                                if (u < 0 || v < 0 || u >= m.width || v >= m.height) continue;
                                float ax = r.x + u * r.width / Mathf.Max(1, m.width) - .5f, ay = r.y + (r.height - v * r.height / Mathf.Max(1, m.height)) - .5f;
                                int x0 = Mathf.FloorToInt(ax), y0 = Mathf.FloorToInt(ay); float fx = ax - x0, fy = ay - y0;
                                acc += Mathf.Lerp(Mathf.Lerp(Cov(x0, y0), Cov(x0 + 1, y0), fx), Mathf.Lerp(Cov(x0, y0 + 1), Cov(x0 + 1, y0 + 1), fx), fy);
                            }
                        cv.Blend(ix, iy, col, acc / (ss * ss));
                    }
                pen += m.horizontalAdvance * k;
            }
        }
        /// centred line: shrinks to maxW
        public void Centred(SignCanvas cv, string s, float cx, float baseline, float capPx, float maxW, Color col)
        {
            float w = Width(s, capPx); if (w > maxW && w > 0) { capPx *= maxW / w; w = maxW; }
            Draw(cv, s, cx - w / 2, baseline, capPx, col);
        }
    }

    /// the atlas: faces by key, painted once, packed, saved
    public class SignAtlas
    {
        public const string DIR = WorldBuilder.GEN + "/Signs";
        public const string PNG = DIR + "/SignAtlas.png";
        const int W = 4096, PAD = 6;
        readonly Dictionary<string, SignCanvas> faces = new Dictionary<string, SignCanvas>();
        readonly List<string> order = new List<string>();
        public readonly Dictionary<string, Rect> uv = new Dictionary<string, Rect>();
        public SignFont font;

        public static readonly Color Red = Hex("c8201a"), White = Hex("f4f4f0"), Black = Hex("141516"), Yellow = Hex("f2c21b"), Blue = Hex("1d4f9c"),
            Green = Hex("187a3e"), Steel = Hex("8e9396"), Dark = Hex("1a1b1c"), Grey = Hex("6d7174");
        public static Color Hex(string h) { ColorUtility.TryParseHtmlString("#" + h, out var c); return c; }

        public bool Has(string key) => faces.ContainsKey(key);
        public string Add(string key, Func<SignCanvas> paint)
        {
            if (faces.ContainsKey(key)) return key;
            faces[key] = paint(); order.Add(key); return key;
        }

        /// pack + write the PNG (import: sRGB, mipmaps, trilinear, aniso 8, high-quality compression) and fill uv
        public Texture2D Save()
        {
            if (!AssetDatabase.IsValidFolder(DIR)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Signs");
            // shelves, tallest first
            var keys = new List<string>(order); keys.Sort((a, b) => faces[b].h.CompareTo(faces[a].h));
            var pos = new Dictionary<string, Vector2Int>(); int x = 0, y = 0, shelf = 0;
            foreach (var k in keys)
            {
                var f = faces[k]; int cw = f.w + PAD * 2, ch = f.h + PAD * 2;
                if (x + cw > W) { x = 0; y += shelf; shelf = 0; }
                pos[k] = new Vector2Int(x, y); x += cw; shelf = Mathf.Max(shelf, ch);
            }
            int H = Mathf.NextPowerOfTwo(Mathf.Max(64, y + shelf));
            if (H > 4096)
            {
                var by = new Dictionary<string, int>(); foreach (var k in order) { string pre = k.Split(':')[0]; by.TryGetValue(pre, out int c); by[pre] = c + 1; }
                throw new Exception($"sign atlas overflow ({H} px high): " + string.Join(", ", System.Linq.Enumerable.Select(by, kv => kv.Key + " " + kv.Value)));
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var all = new Color32[W * H];
            foreach (var k in keys)
            {
                var f = faces[k]; var p = pos[k];
                for (int yy = -PAD; yy < f.h + PAD; yy++)
                    for (int xx = -PAD; xx < f.w + PAD; xx++)
                    {
                        var c = f.px[Mathf.Clamp(yy, 0, f.h - 1) * f.w + Mathf.Clamp(xx, 0, f.w - 1)];
                        // texture row 0 = bottom: the face's top row goes to the cell's top
                        int tx = p.x + PAD + xx, ty = H - 1 - (p.y + PAD + yy);
                        all[ty * W + tx] = c;
                    }
                uv[k] = new Rect((p.x + PAD + .5f) / W, (H - (p.y + PAD + f.h) + .5f) / H, (f.w - 1f) / W, (f.h - 1f) / H);
            }
            tex.SetPixels32(all); tex.Apply();
            File.WriteAllBytes(PNG, tex.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(PNG, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(PNG);
            imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.filterMode = FilterMode.Trilinear; imp.anisoLevel = 8;
            imp.wrapMode = TextureWrapMode.Clamp; imp.maxTextureSize = 4096; imp.textureCompression = TextureImporterCompression.CompressedHQ;
            imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = .5f;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(PNG);
        }

        // ------------------------------------------------------------------ faces
        public const int S = 256; // standard signs: 256 px square

        public string Solid(string name, Color c) => Add("solid:" + name, () => { var cv = new SignCanvas(8, 8); cv.Clear(c); return cv; });

        /// speed limit: white disc, red rim (1/10 of the diameter), black number
        public string Limit(int kmh) => Add("limit:" + kmh, () =>
        {
            var cv = new SignCanvas(S, S); float c = S / 2f, r = S / 2f - 3;
            cv.Fill(Sd.Circle(c, c, r), White); cv.Fill(Sd.Ring(c, c, r - 4, r * .17f), Red);
            font.Centred(cv, kmh.ToString(), c, c + 47, 94, r * 1.25f, Black); return cv;
        });
        /// round prohibition disc with a text (truck weight "7 t")
        public string Disc(string text) => Add("disc:" + text, () =>
        {
            var cv = new SignCanvas(S, S); float c = S / 2f, r = S / 2f - 3;
            cv.Fill(Sd.Circle(c, c, r), White); cv.Fill(Sd.Ring(c, c, r - 4, r * .17f), Red);
            font.Centred(cv, text, c, c + 33, 66, r * 1.3f, Black); return cv;
        });
        public string Stop() => Add("stop", () =>
        {
            var cv = new SignCanvas(S, S); float c = S / 2f;
            cv.Fill(Sd.Poly(Sd.Regular(c, c, S / 2f - 2, 8, Mathf.PI / 8)), White);
            cv.Fill(Sd.Poly(Sd.Regular(c, c, S / 2f - 9, 8, Mathf.PI / 8)), Red);
            font.Centred(cv, "STOP", c, c + 27, 54, S * .72f, White); return cv;
        });
        public string GiveWay() => Add("giveway", () =>
        {
            var cv = new SignCanvas(S, S); float t = 14, b = S - 14;
            var outer = Sd.Poly(new Vector2(4, t), new Vector2(S - 4, t), new Vector2(S / 2f, b + 8));
            cv.Fill(outer, Red); cv.Fill(Sd.Inset(outer, 30), White); return cv;
        });
        public string Priority() => Add("priority", () =>
        {
            var cv = new SignCanvas(S, S); float c = S / 2f;
            var d = Sd.Poly(new Vector2(c, 2), new Vector2(S - 2, c), new Vector2(c, S - 2), new Vector2(2, c));
            cv.Fill(d, White); cv.Fill(Sd.Inset(d, 3), Hex("2a2a2a")); cv.Fill(Sd.Inset(d, 7), White); cv.Fill(Sd.Inset(d, 40), Yellow); return cv;
        });
        /// marked pedestrian crossing: blue square, white triangle, black walker on the zebra
        public string Pedestrian() => Add("pedestrian", () =>
        {
            var cv = new SignCanvas(S, S);
            cv.Fill(Sd.Box(S / 2f, S / 2f, S / 2f - 2, S / 2f - 2, 14), White); cv.Fill(Sd.Box(S / 2f, S / 2f, S / 2f - 8, S / 2f - 8, 10), Blue);
            cv.Fill(Sd.Poly(new Vector2(S / 2f, 30), new Vector2(S - 26, S - 34), new Vector2(26, S - 34)), White);
            for (int i = 0; i < 4; i++) cv.Fill(Sd.Box(86 + i * 28, S - 50, 8, 5), Black);
            cv.Fill(Sd.Circle(128, 88, 11), Black);
            cv.Fill(Sd.Capsule(126, 104, 120, 148, 8), Black);
            cv.Fill(Sd.Capsule(120, 148, 104, 182, 6), Black); cv.Fill(Sd.Capsule(120, 148, 140, 180, 6), Black);
            cv.Fill(Sd.Capsule(125, 112, 106, 136, 5), Black); cv.Fill(Sd.Capsule(126, 112, 146, 132, 5), Black);
            return cv;
        });
        /// keep right (the bollards' sign): blue disc, white arrow down to the right
        public string KeepRight() => Add("keepright", () =>
        {
            var cv = new SignCanvas(S, S); float c = S / 2f;
            cv.Fill(Sd.Circle(c, c, c - 2), White); cv.Fill(Sd.Circle(c, c, c - 8), Blue);
            cv.Fill(Sd.Arrow(78, 78, 182, 182, 15, 58, 48), White); return cv;
        });
        /// no left turn (sign_prohibition without a text)
        public string NoLeft() => Add("noleft", () =>
        {
            var cv = new SignCanvas(S, S); float c = S / 2f, r = c - 3;
            cv.Fill(Sd.Circle(c, c, r), White);
            cv.Fill(Sd.Union(Sd.Capsule(150, 196, 150, 118, 14), Sd.Capsule(150, 118, 118, 118, 14)), Black);
            cv.Fill(Sd.Poly(new Vector2(70, 118), new Vector2(118, 82), new Vector2(118, 154)), Black);
            cv.Fill(Sd.Ring(c, c, r - 4, r * .17f), Red);
            cv.Fill(Sd.Capsule(c - r * .62f, c - r * .62f, c + r * .62f, c + r * .62f, 11), Red);
            return cv;
        });
        /// mandatory direction straight on (blue disc, white arrow)
        public string Ahead() => Add("ahead", () =>
        {
            var cv = new SignCanvas(S, S); float c = S / 2f;
            cv.Fill(Sd.Circle(c, c, c - 2), White); cv.Fill(Sd.Circle(c, c, c - 8), Blue);
            cv.Fill(Sd.Arrow(c, 206, c, 50, 16, 60, 50), White); return cv;
        });
        /// the plate backs: the face's silhouette in galvanised grey
        public string Back(string shape) => Add("back:" + shape, () =>
        {
            var cv = new SignCanvas(shape == "rect" ? 16 : S, shape == "rect" ? 16 : S); float c = S / 2f;
            switch (shape)
            {
                case "round": cv.Fill(Sd.Circle(c, c, c - 3), Steel); break;
                case "oct": cv.Fill(Sd.Poly(Sd.Regular(c, c, c - 2, 8, Mathf.PI / 8)), Steel); break;
                case "tri": cv.Fill(Sd.Poly(new Vector2(4, 14), new Vector2(S - 4, 14), new Vector2(S / 2f, S - 6)), Steel); break;
                case "diamond": cv.Fill(Sd.Poly(new Vector2(c, 2), new Vector2(S - 2, c), new Vector2(c, S - 2), new Vector2(2, c)), Steel); break;
                case "square": cv.Fill(Sd.Box(c, c, c - 2, c - 2, 14), Steel); break;
                default: cv.Clear(Steel); break;
            }
            return cv;
        });
        /// yellow / black bollard stripes (u around, v up)
        public string BollardStripes() => Add("bollard", () =>
        {
            var cv = new SignCanvas(64, 128);
            cv.Fill((x, y) => -1, Yellow);
            cv.Fill((x, y) => { float t = ((x + y) % 64 + 64) % 64; return Mathf.Min(Mathf.Abs(t - 16) - 9, 99); }, Black);
            return cv;
        });

        /// one board: text lines ("/" or "|" separated), an optional leading arrow (← ↑ → ↗ ↖); colours by style
        /// (yellow: black on yellow; white: black on white; blue / green: white on blue / green). w × h in metres → px.
        public string Board(string text, string style, float wm, float hm)
        {
            string key = $"board:{style}:{wm:0.0}x{hm:0.0}:{text}";
            return Add(key, () =>
            {
                Color bg = style == "blue" ? Blue : style == "green" ? Green : style == "white" ? White : Yellow;
                Color fg = style == "blue" || style == "green" ? White : Black;
                int pw = Mathf.Clamp(Mathf.RoundToInt(wm * 150), 96, 640), ph = Mathf.Clamp(Mathf.RoundToInt(hm * 150), 48, 400);
                var cv = new SignCanvas(pw, ph);
                cv.Fill(Sd.Box(pw / 2f, ph / 2f, pw / 2f - 1, ph / 2f - 1, 10), bg);
                cv.Fill((x, y) => { float d = Sd.Box(pw / 2f, ph / 2f, pw / 2f - 7, ph / 2f - 7, 7)(x, y); return Mathf.Abs(d) - 3; }, fg);
                string t = text.Trim(); char arrow = '\0';
                if (t.Length > 0 && "←↑→↗↖↓".IndexOf(t[0]) >= 0) { arrow = t[0]; t = t.Substring(1).Trim(); }
                else if (t.Length > 0 && "←↑→↗↖↓".IndexOf(t[t.Length - 1]) >= 0) { arrow = t[t.Length - 1]; t = t.Substring(0, t.Length - 1).Trim(); }
                var lines = new List<string>();
                foreach (var l in t.Split(new[] { " / ", "|", "/" }, StringSplitOptions.RemoveEmptyEntries)) if (l.Trim().Length > 0) lines.Add(l.Trim());
                float margin = 20, ax0 = margin, ax1 = pw - margin;
                if (arrow != '\0')
                {
                    float asz = Mathf.Min(ph - 40, 120), acx = lines.Count == 0 ? pw / 2f : arrow == '→' ? pw - margin - asz / 2 : margin + asz / 2, acy = ph / 2f, hl = asz * .42f, th = asz * .11f, hw = asz * .34f;
                    Vector2 d = arrow == '←' ? new Vector2(-1, 0) : arrow == '→' ? new Vector2(1, 0) : arrow == '↓' ? new Vector2(0, 1) : arrow == '↗' ? new Vector2(.707f, -.707f) : arrow == '↖' ? new Vector2(-.707f, -.707f) : new Vector2(0, -1);
                    var a0 = new Vector2(acx, acy) - d * asz * .45f; var a1 = new Vector2(acx, acy) + d * asz * .45f;
                    cv.Fill(Sd.Arrow(a0.x, a0.y, a1.x, a1.y, th, hl, hw), fg);
                    if (arrow == '→') ax1 -= asz + 10; else ax0 += asz + 10;
                }
                if (lines.Count == 0) return cv;
                float avail = ph - 36, cap = Mathf.Min(avail / (lines.Count * 1.45f), 58), lh = cap * 1.45f;
                float y0 = ph / 2f - lh * (lines.Count - 1) / 2f + cap / 2f;
                for (int i = 0; i < lines.Count; i++)
                {
                    float maxW = ax1 - ax0, w = font.Width(lines[i], cap), c = cap; if (w > maxW) { c = cap * maxW / w; w = maxW; }
                    float x = arrow == '\0' ? (ax0 + ax1) / 2 - w / 2 : ax0;
                    font.Draw(cv, lines[i], x, y0 + i * lh, c, fg);
                }
                return cv;
            });
        }

        /// place-name board (white, black border and text); end = the "end of place" board (red bar across)
        public string Town(string name, bool end) => Add((end ? "townend:" : "town:") + name, () =>
        {
            int pw = 448, ph = 160; var cv = new SignCanvas(pw, ph);
            cv.Fill(Sd.Box(pw / 2f, ph / 2f, pw / 2f - 1, ph / 2f - 1, 10), White);
            cv.Fill((x, y) => Mathf.Abs(Sd.Box(pw / 2f, ph / 2f, pw / 2f - 7, ph / 2f - 7, 6)(x, y)) - 3, Black);
            var lines = name.StartsWith("Kaštel ") ? new[] { "KAŠTEL", name.Substring(7).ToUpperInvariant() } : new[] { name.ToUpperInvariant() };
            float cap = lines.Length > 1 ? 42 : 56, lh = cap * 1.5f, y0 = ph / 2f - lh * (lines.Length - 1) / 2f + cap / 2f;
            for (int i = 0; i < lines.Length; i++) font.Centred(cv, lines[i], pw / 2f, y0 + i * lh, cap, pw - 48, Black);
            if (end) cv.Fill(Sd.Capsule(28, ph - 24, pw - 28, 24, 7), Red);
            return cv;
        });

        /// street-name plate: blue, white border and text
        public string Street(string name) => Add("street:" + name, () =>
        {
            int pw = 320, ph = 80; var cv = new SignCanvas(pw, ph);
            cv.Fill(Sd.Box(pw / 2f, ph / 2f, pw / 2f - 1, ph / 2f - 1, 6), Blue);
            cv.Fill((x, y) => Mathf.Abs(Sd.Box(pw / 2f, ph / 2f, pw / 2f - 5, ph / 2f - 5, 3)(x, y)) - 1.6f, White);
            font.Centred(cv, name, pw / 2f, ph / 2f + 13, 26, pw - 28, White);
            return cv;
        });
    }
}
