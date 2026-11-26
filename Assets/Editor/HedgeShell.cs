// Živice (25. 11., Marko: "veliki nedostatak na staroj cesti: fale zidići i živice; moraš naći realističnu živicu"):
// the leaf shell of the clipped garden hedges. HouseBuilder.YardHedge / HedgePillar keep the hedge's box in the building
// meshes (the facade material's hedge layer: the dense core, the far look, the hull's shadow and collider); at their near level
// they also place leaf modules here along the box: 1.1 m pieces of a clipped hedge's skin (alpha-clipped leaf cards on both
// faces, over the top and rounded over its edges, a little ragged above the clipped line; the on-wall kind hangs 0.3 m over the
// wall's face), an end cap at each run end, a shell for the cypress columns. The modules are TreeRenderer prototypes
// (instanced, ~24 bytes a metre of hedge, drawn to ~115 m and dithered out before that by Magistrala/Hedge; small-plant
// shadows near the camera): Protos() registers them in Build trees (NaturePass.Begin), Build tile buildings writes the
// placements (Assets/Generated/Props/zivice.bytes) and puts them into trees.bytes (Commit); Build trees puts the saved ones
// back after it rewrote trees.bytes.
// Box size follows the module: Fit() quantises a hedge's height to the nearest module class (ground 1.2 / 1.8 / 2.4 m, on a
// wall 0.9 / 1.3 m) and returns the scale; the box takes the module's width × scale, so the cards sit on its skin.
// Leaves: NatureManufacture Coast Environment (Marko's licence): beach rose leaf sprigs (pittosporum / laurel / privet: dense
// dark rounded leaves), sea buckthorn sprigs (narrow leaves: oleander, cypress), the rose's pink / white flowers on the
// oleanders. Recomposed into one 2048² atlas (Assets/Generated/Nature/zivica_atlas.png, 4 × 4 slots): one material for all.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class HedgeShell
    {
        public enum Sp { Pittosporum, Laurel, Privet, Oleander, OleanderWhite, Cypress }
        public static bool On = true;
        const string CE = NaturePass.PKG + "/Coast Environment/Bushes/Textures/";
        const string OUT = WorldBuilder.GEN + "/Nature";
        public const string ATLAS = OUT + "/zivica_atlas.png", MAT = OUT + "/zivica.mat";

        // ------------------------------------------------------------------ atlas
        // source rects (u0, v0, u1, v1; v from the bottom) → slots 0..15 of a 4 × 4 atlas
        static readonly (bool rose, Vector4 r)[] Src =
        {
            (true, new Vector4(.12f, .63f, .52f, .83f)), (true, new Vector4(.52f, .58f, .94f, .79f)), (true, new Vector4(.10f, .48f, .50f, .68f)),
            (true, new Vector4(.55f, .40f, .95f, .60f)), (true, new Vector4(.12f, .55f, .52f, .75f)),                       // 0-4 rose leaf sprigs
            (false, new Vector4(.527f, .893f, .732f, .99f)), (false, new Vector4(.36f, .756f, .71f, .878f)), (false, new Vector4(.107f, .805f, .361f, .98f)),
            (false, new Vector4(.31f, .014f, .60f, .16f)), (false, new Vector4(.34f, .14f, .47f, .31f)),                     // 5-9 buckthorn sprigs
            (true, new Vector4(.578f, .922f, .734f, .998f)), (true, new Vector4(.84f, .883f, .96f, .937f)),                  // 10-11 pink flowers
            (true, new Vector4(.416f, .939f, .566f, .997f)), (true, new Vector4(.18f, .906f, .27f, .954f)),                  // 12-13 white flowers
        };
        const int SLOTS = 4, SLOT = 512, AN = SLOTS * SLOT;
        static Vector4 SlotUV(int s) { int i = s % SLOTS, j = s / SLOTS; float k = 1f / SLOTS, p = 6f / AN; return new Vector4(i * k + p, j * k + p, (i + 1) * k - p, (j + 1) * k - p); }

        static Texture2D Read(string p) { var t = new Texture2D(2, 2, TextureFormat.RGBA32, false); t.LoadImage(File.ReadAllBytes(p)); return t; }

        public static Material Material()
        {
            var sh = Shader.Find("Magistrala/Hedge"); if (!sh) { Debug.LogError("[zivice] no Magistrala/Hedge shader"); return null; }
            if (!AssetDatabase.IsValidFolder(OUT)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Nature");
            BakeAtlas();
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MAT);
            if (!mat) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, MAT); } else mat.shader = sh;
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(ATLAS));
            mat.SetColor("_BaseColor", Color.white); mat.SetFloat("_Cutoff", .42f);
            mat.SetVector("_Far", new Vector4(88, 114, 0, 0));
            mat.renderQueue = 2450; mat.enableInstancing = true; EditorUtility.SetDirty(mat);
            return mat;
        }

        [MenuItem("Magistrala/Zivice: atlas listova")]
        public static string BakeAtlas()
        {
            var rose = Read(CE + "T_beach_rose_BC_O.png"); var buck = Read(CE + "T_sea_buckthorn_BC_O.png");
            var px = new Color32[AN * AN];
            var fill = new Color32(52, 70, 34, 0); for (int i = 0; i < px.Length; i++) px[i] = fill;
            for (int s = 0; s < Src.Length; s++)
            {
                var (isRose, r) = Src[s]; var src = isRose ? rose : buck;
                // keep the sprig's pixel aspect: fit into the slot, centred
                float wpx = (r.z - r.x) * src.width, hpx = (r.w - r.y) * src.height, sc = Mathf.Max(wpx, hpx) / (SLOT - 12);
                float ow = wpx / sc, oh = hpx / sc;
                int i0 = (s % SLOTS) * SLOT, j0 = (s / SLOTS) * SLOT;
                for (int j = 0; j < SLOT; j++)
                    for (int i = 0; i < SLOT; i++)
                    {
                        float x = i - (SLOT - ow) / 2, y = j - (SLOT - oh) / 2; if (x < 0 || y < 0 || x >= ow || y >= oh) continue;
                        var c = src.GetPixelBilinear(r.x + (r.z - r.x) * (x + .5f) / ow, r.y + (r.w - r.y) * (y + .5f) / oh);
                        // transparent texels carry a leaf green, not the source's white (no white halo in the mips)
                        if (c.a < .5f) c = new Color(fill.r / 255f, fill.g / 255f, fill.b / 255f, c.a);
                        px[(j0 + j) * AN + i0 + i] = c;
                    }
            }
            UnityEngine.Object.DestroyImmediate(rose); UnityEngine.Object.DestroyImmediate(buck);
            var t = new Texture2D(AN, AN, TextureFormat.RGBA32, false); t.SetPixels32(px);
            if (!AssetDatabase.IsValidFolder(OUT)) AssetDatabase.CreateFolder(WorldBuilder.GEN, "Nature");
            File.WriteAllBytes(ATLAS, t.EncodeToPNG()); UnityEngine.Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(ATLAS);
            var imp = (TextureImporter)AssetImporter.GetAtPath(ATLAS);
            imp.sRGBTexture = true; imp.alphaIsTransparency = true; imp.mipmapEnabled = true; imp.mipMapsPreserveCoverage = true; imp.alphaTestReferenceValue = .42f;
            imp.maxTextureSize = 2048; imp.wrapMode = TextureWrapMode.Clamp; imp.SaveAndReimport();
            return "[zivice] atlas " + ATLAS;
        }

        // ------------------------------------------------------------------ species
        struct Look { public int s0, s1; public float card; public Color tint; public float flowers; public int f0, f1; public float lift; }
        static Look Of(Sp sp)
        {
            switch (sp)
            {
                case Sp.Laurel: return new Look { s0 = 0, s1 = 4, card = .62f, tint = new Color(.50f, .62f, .40f), lift = .12f };
                case Sp.Privet: return new Look { s0 = 0, s1 = 4, card = .36f, tint = new Color(.72f, .84f, .55f), lift = .22f };
                case Sp.Oleander: return new Look { s0 = 5, s1 = 9, card = .68f, tint = new Color(.62f, .74f, .50f), flowers = .13f, f0 = 10, f1 = 11, lift = .1f };
                case Sp.OleanderWhite: return new Look { s0 = 5, s1 = 9, card = .68f, tint = new Color(.62f, .74f, .50f), flowers = .11f, f0 = 12, f1 = 13, lift = .1f };
                case Sp.Cypress: return new Look { s0 = 5, s1 = 9, card = .34f, tint = new Color(.36f, .50f, .40f), lift = .05f };
                default: return new Look { s0 = 0, s1 = 4, card = .5f, tint = new Color(.56f, .70f, .46f), lift = .15f }; // pittosporum
            }
        }

        // ------------------------------------------------------------------ module meshes
        class Geo { public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public List<Color> c = new List<Color>(); public List<Vector2> uv = new List<Vector2>(); public List<int> t = new List<int>(); }
        /// species groups (one leaf look each; laurel / privet vary per instance in the shader), module classes
        static int Group(Sp sp) => sp == Sp.Oleander ? 1 : sp == Sp.OleanderWhite ? 2 : sp == Sp.Cypress ? 3 : 0;
        static readonly Sp[] GroupSp = { Sp.Pittosporum, Sp.Oleander, Sp.OleanderWhite, Sp.Cypress };
        static readonly float[] GroundH = { 1.2f, 1.8f, 2.4f }, WallH = { .9f, 1.3f };
        public const float MW = .8f, ML = 1.1f, PITCH = .95f, CAP_H = 1.8f, PILLAR_H = 6f, PILLAR_R = .55f;
        static string IdT(int g, int c) => $"ziv-{g}-t{c}"; static string IdZ(int g, int c) => $"ziv-{g}-z{c}"; static string IdK(int g) => $"ziv-{g}-k";
        const string IdPillar = "ziv-3-stup";
        /// every module id, in a fixed order (the saved placements index it)
        public static readonly string[] Ids = MakeIds();
        static string[] MakeIds()
        {
            var l = new List<string>();
            for (int g = 0; g < 4; g++) { for (int c = 0; c < GroundH.Length; c++) l.Add(IdT(g, c)); for (int c = 0; c < WallH.Length; c++) l.Add(IdZ(g, c)); l.Add(IdK(g)); }
            l.Add(IdPillar); return l.ToArray();
        }

        static void Card(Geo g, System.Random r, Vector3 c, Vector3 nFace, Vector3 t1, float size, int slot, Color tint, float ao, float bendUp)
        {
            // the card's plane: the face's plane turned about its normal, tilted ±40° about an in-plane axis
            Vector3 t2 = Vector3.Cross(nFace, t1).normalized;
            float spin = (float)r.NextDouble() * 360, tilt = ((float)r.NextDouble() - .5f) * 80;
            var q = Quaternion.AngleAxis(spin, nFace); Vector3 a = q * t1, b = q * t2;
            var qt = Quaternion.AngleAxis(tilt, a); b = qt * b; Vector3 cn = Vector3.Cross(a, b).normalized; if (Vector3.Dot(cn, nFace) < 0) cn = -cn;
            float hs = size * .5f * (.8f + .4f * (float)r.NextDouble());
            Vector3 A = a * hs, B = b * hs;
            Vector3 nrm = (nFace * .78f + cn * .22f + Vector3.up * bendUp).normalized;
            var uv = SlotUV(slot); bool flip = r.NextDouble() < .5;
            float u0 = flip ? uv.z : uv.x, u1 = flip ? uv.x : uv.z;
            int i = g.v.Count;
            g.v.Add(c - A - B); g.v.Add(c + A - B); g.v.Add(c + A + B); g.v.Add(c - A + B);
            for (int k = 0; k < 4; k++) g.n.Add(nrm);
            g.uv.Add(new Vector2(u0, uv.y)); g.uv.Add(new Vector2(u1, uv.y)); g.uv.Add(new Vector2(u1, uv.w)); g.uv.Add(new Vector2(u0, uv.w));
            // per card: brightness ±12 %, a little toward yellow (new growth) or blue (old leaves)
            float br = .88f + .24f * (float)r.NextDouble(), hue = ((float)r.NextDouble() - .5f) * .16f;
            var col = new Color(tint.r * br * (1 + hue), tint.g * br * (1 + hue * .3f), tint.b * br * (1 - hue), Mathf.Clamp01(ao));
            for (int k = 0; k < 4; k++) g.c.Add(col);
            g.t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        /// a straight piece of hedge skin in the module frame (x along −ML/2…ML/2, z inward: the outer face at z = −MW/2, y up from
        /// 0 to h); overhang: the outer face's leaves reach this far below 0 (a hedge hanging over a wall's face)
        static void Skin(Geo g, Look L, System.Random r, float h, float overhang)
        {
            float card = L.card, step = card * .55f, hw = MW / 2;
            int Slot() => L.s0 + r.Next(L.s1 - L.s0 + 1);
            int nu = Mathf.Max(1, Mathf.RoundToInt(ML / step));
            for (int iu = 0; iu < nu; iu++)
            {
                float x = -ML / 2 + (iu + .15f + .7f * (float)r.NextDouble()) * ML / nu;
                for (int side = -1; side <= 1; side += 2)
                {
                    var nF = new Vector3(0, 0, side); float yBot = (side < 0 ? -overhang : 0) + .1f;
                    int nv = Mathf.Max(1, Mathf.RoundToInt((h - yBot) / step));
                    for (int iv = 0; iv < nv; iv++)
                    {
                        float y = yBot + (iv + .2f + .6f * (float)r.NextDouble()) * (h - yBot) / nv;
                        float depth = -.03f + .12f * (float)r.NextDouble(), face = y < 0 ? hw + .06f : hw;
                        var c = new Vector3(x, y, side * (face + depth));
                        float up01 = Mathf.Clamp01((y - (h - .3f)) / .3f);
                        float ao = Mathf.Lerp(.5f, 1f, Mathf.Clamp01((y + .3f) / Mathf.Max(.6f, h * .6f))) * (depth < .01f ? .82f : 1f);
                        Card(g, r, c, nF, Vector3.right, card, Slot(), L.tint, ao, up01 * .6f);
                        if (L.flowers > 0 && side < 0 && y > h * .35f && r.NextDouble() < L.flowers * 2.2f)
                            for (int fl = 0, nf = 2 + r.Next(3); fl < nf; fl++)   // a truss of 2–4 flowers (~8 cm each)
                                Card(g, r, c + nF * .09f + new Vector3(((float)r.NextDouble() - .5f) * .14f, ((float)r.NextDouble() - .5f) * .12f, 0), nF, Vector3.right, .09f + .03f * (float)r.NextDouble(), L.f0 + r.Next(L.f1 - L.f0 + 1), Color.white * 1.05f, 1, up01 * .4f);
                    }
                }
                // the top: cards across it, the outer rows rounded over the edges, a few standing proud (an uneven trim)
                int nw = Mathf.Max(2, Mathf.RoundToInt(MW / step) + 1);
                for (int iw = 0; iw < nw; iw++)
                {
                    float z = -hw + MW * (iw + .15f + .7f * (float)r.NextDouble()) / nw, edge = Mathf.Abs(z) / hw;
                    var nF = (Vector3.up + new Vector3(0, 0, Mathf.Sign(z)) * Mathf.SmoothStep(0, .9f, (edge - .55f) / .45f)).normalized;
                    float lift = -.03f + L.lift * (float)r.NextDouble() * (r.NextDouble() < .25 ? 1 : .35f);
                    var c = new Vector3(x + ((float)r.NextDouble() - .5f) * .1f, h + lift - .04f * edge * edge, z * (1 + .06f * edge));
                    Card(g, r, c, nF, Vector3.right, card, Slot(), L.tint * 1.04f, 1, 0);
                    if (L.flowers > 0 && r.NextDouble() < L.flowers * 2.5f)
                        for (int fl = 0, nf = 2 + r.Next(3); fl < nf; fl++)
                            Card(g, r, c + Vector3.up * .08f + new Vector3(((float)r.NextDouble() - .5f) * .14f, 0, ((float)r.NextDouble() - .5f) * .14f), nF, Vector3.right, .09f + .03f * (float)r.NextDouble(), L.f0 + r.Next(L.f1 - L.f0 + 1), Color.white * 1.05f, 1, 0);
                }
            }
        }
        /// an end cap: the face x = 0 facing +x (z −MW/2…MW/2, y 0…h)
        static void Cap(Geo g, Look L, System.Random r, float h)
        {
            float card = L.card, step = card * .55f, hw = MW / 2;
            int nw = Mathf.Max(1, Mathf.RoundToInt(MW / step)), nv = Mathf.Max(1, Mathf.RoundToInt(h / step));
            for (int iw = 0; iw < nw; iw++)
                for (int iv = 0; iv < nv; iv++)
                {
                    float z = -hw + MW * (iw + .5f) / nw, y = .1f + (h - .1f) * (iv + .3f + .4f * (float)r.NextDouble()) / nv;
                    var c = new Vector3(-.02f + .1f * (float)r.NextDouble(), y, z);
                    Card(g, r, c, Vector3.right, Vector3.forward, card, L.s0 + r.Next(L.s1 - L.s0 + 1), L.tint, Mathf.Lerp(.55f, 1, y / h), 0);
                }
        }
        /// a clipped column (a cypress by a gate): radius PILLAR_R at the foot, PILLAR_H tall, a pointed top
        static void Pillar(Geo g, Look L, System.Random r)
        {
            float card = L.card, step = card * .55f, h = PILLAR_H;
            for (float y = .2f; y < h; y += step)
            {
                float rad = PillarR(PILLAR_R, h, y); if (rad < .05f) break;
                int na = Mathf.Max(4, Mathf.RoundToInt(2 * Mathf.PI * rad / step));
                for (int k = 0; k < na; k++)
                {
                    float a = (k + (float)r.NextDouble()) / na * Mathf.PI * 2; var nF = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                    float slope = (PillarR(PILLAR_R, h, y - .2f) - PillarR(PILLAR_R, h, y + .2f)) / .4f; var n2 = (nF + Vector3.up * slope).normalized;
                    var c = nF * (rad + .02f + .08f * (float)r.NextDouble()) + Vector3.up * (y + step * (float)r.NextDouble() * .5f);
                    Card(g, r, c, n2, Vector3.up, card, L.s0 + r.Next(L.s1 - L.s0 + 1), L.tint, Mathf.Lerp(.55f, 1, y / h + .3f), 0);
                }
            }
        }
        public static float PillarR(float r0, float h, float y) { float t = Mathf.Clamp01(y / h); return r0 * (t < .55f ? 1 - .15f * t : Mathf.Sqrt(Mathf.Max(0, 1 - Mathf.Pow((t - .55f) / .45f, 2))) * .92f); }

        static Mesh ToMesh(Geo g, string name)
        {
            var m = new Mesh { name = name };
            m.SetVertices(g.v); m.SetNormals(g.n); m.SetColors(g.c); m.SetUVs(0, g.uv); m.SetTriangles(g.t, 0); m.RecalculateBounds();
            string p = $"{OUT}/{name}.asset"; if (File.Exists(p)) AssetDatabase.DeleteAsset(p); AssetDatabase.CreateAsset(m, p);
            return AssetDatabase.LoadAssetAtPath<Mesh>(p);
        }

        /// the modules as TreeRenderer prototypes (NaturePass.Begin, after the median hedge's)
        public static void Protos(List<NaturePass.Proto> list)
        {
            if (!On) return;
            var mat = Material(); if (!mat) return;
            int tris = 0;
            NaturePass.Proto P(string id, Geo g) { var m = ToMesh(g, id); tris += g.t.Count / 3; return new NaturePass.Proto { id = id, mesh = m, mats = new[] { mat }, draw = 115, near = 1e5f }; }
            for (int gi = 0; gi < 4; gi++)
            {
                var L = Of(GroupSp[gi]);
                for (int c = 0; c < GroundH.Length; c++) { var g = new Geo(); Skin(g, L, new System.Random(1000 * gi + c), GroundH[c], 0); list.Add(P(IdT(gi, c), g)); }
                for (int c = 0; c < WallH.Length; c++) { var g = new Geo(); Skin(g, L, new System.Random(2000 * gi + c), WallH[c], .3f); list.Add(P(IdZ(gi, c), g)); }
                { var g = new Geo(); Cap(g, L, new System.Random(3000 + gi), CAP_H); list.Add(P(IdK(gi), g)); }
            }
            { var g = new Geo(); Pillar(g, Of(Sp.Cypress), new System.Random(4000)); list.Add(P(IdPillar, g)); }
            Debug.Log($"[zivice] {Ids.Length} leaf module prototypes, {tris} tris together");
        }

        // ------------------------------------------------------------------ placement
        /// the module class for a hedge `h` m tall (above the ground, or above its foot on a wall): its id, scale, and the box's width
        /// and height that match it
        public static (string id, float scale, float w, float h) Fit(Sp sp, float h, bool onWall, bool cap = false)
        {
            int g = Group(sp); var H = onWall ? WallH : GroundH; int best = 0;
            for (int c = 1; c < H.Length; c++) if (Mathf.Abs(Mathf.Log(h / H[c])) < Mathf.Abs(Mathf.Log(h / H[best]))) best = c;
            float s = Mathf.Clamp(h / H[best], .75f, 1.3f);
            return (onWall ? IdZ(g, best) : IdT(g, best), s, MW * s * .94f, H[best] * s);
        }
        static readonly List<(Vector3 p, float yaw, float s, int id)> placed = new List<(Vector3, float, float, int)>();
        public static void Begin() => placed.Clear();
        public static int Count => placed.Count;
        static void Put(Vector3 p, float yaw, float s, string id) { int i = Array.IndexOf(Ids, id); if (i >= 0) placed.Add((p, yaw, s, i)); }
        /// yaw (Unity, degrees) that turns the module's +x along e (horizontal)
        static float Yaw(Vector3 e) => Mathf.Atan2(-e.z, e.x) * Mathf.Rad2Deg;

        /// a hedge run (YardHedge, near level): pts on the ground line, inward side (+1: (−e.z, 0, e.x) of the run), the box's mid
        /// line offset `mid`, base `y0` (above the ground at each point; a hedge on a wall: > 0)
        public static void AddRun(List<Vector3> pts, float side, float mid, float y0, Sp sp, float h, bool onWall, int seed)
        {
            if (!On || pts.Count < 2) return;
            var f = Fit(sp, h, onWall); var r = new System.Random(seed * 31 + 7);
            float baseY = onWall ? y0 : -.04f;
            for (int k = 0; k + 1 < pts.Count; k++)
            {
                Vector3 a = pts[k], b = pts[k + 1], e = b - a; e.y = 0; float L = e.magnitude; if (L < .05f) continue; e /= L;
                var sd = new Vector3(-e.z, 0, e.x) * side;
                // module +x along e, +z inward: the yaw of e when the inward side is (−e.z, 0, e.x), else of −e
                float yaw = side > 0 ? Yaw(e) : Yaw(-e);
                int n = Mathf.Max(1, Mathf.CeilToInt((L - .15f) / PITCH));
                for (int i = 0; i < n; i++)
                {
                    float t = (i + .5f) / n; var p = Vector3.Lerp(a, b, t) + sd * mid; p.y += baseY;
                    // ground modules are symmetric: half of them turned round (no repeat along the run)
                    float yw = yaw + (!onWall && r.NextDouble() < .5 ? 180 : 0);
                    Put(p, yw, f.scale * (.97f + .06f * (float)r.NextDouble()), f.id);
                }
            }
            // end caps (only for hedges taller than their wall-top kind: the on-wall ends are short)
            for (int end = 0; end < 2; end++)
            {
                Vector3 p = end == 0 ? pts[0] : pts[pts.Count - 1], o = end == 0 ? pts[1] : pts[pts.Count - 2], e = p - o; e.y = 0; if (e.sqrMagnitude < 1e-4f) continue; e.Normalize();
                var dir = end == 0 ? o - p : p - o; dir.y = 0; dir.Normalize(); var s2 = new Vector3(-dir.z, 0, dir.x) * side;
                var c = p + s2 * mid; c.y += baseY;
                Put(c, Yaw(e), f.h / CAP_H, IdK(Group(sp)));
            }
        }
        public static void AddPillar(Vector3 p, float r0, float h, int seed) { if (On) Put(p + Vector3.down * .05f, seed % 360, h / PILLAR_H, IdPillar); }

        // ------------------------------------------------------------------ into the TreeRenderer
        public const string SAVED = WorldBuilder.GEN + "/Props/zivice.bytes";
        /// Build tile buildings: the placements to SAVED and into the scene's trees.bytes
        public static string Save()
        {
            var b = new List<byte>(placed.Count * 24);
            foreach (var q in placed) foreach (var v in new[] { q.p.x, q.p.y, q.p.z, q.yaw, q.s, (float)q.id }) b.AddRange(BitConverter.GetBytes(v));
            Directory.CreateDirectory(Path.GetDirectoryName(SAVED)); File.WriteAllBytes(SAVED, b.ToArray());
            return Commit();
        }
        /// trees.bytes: the old hedge-module records out, the saved ones in (their prototype index from the TreeRenderer's meshes);
        /// also run at the end of Build trees
        public static string Commit()
        {
            // World/Vegetation's (not the AC units' TreeRenderer under World/Buildings)
            var veg = GameObject.Find("World")?.transform.Find("Vegetation"); var tr = veg ? veg.GetComponent<Magistrala.TreeRenderer>() : null;
            string tp = WorldBuilder.GEN + "/trees.bytes";
            if (!tr || tr.meshes == null || !File.Exists(tp)) return "[zivice] no TreeRenderer / trees.bytes";
            var map = new int[Ids.Length]; int found = 0;
            for (int i = 0; i < Ids.Length; i++) { map[i] = Array.FindIndex(tr.meshes, m => m && m.name == Ids[i]); if (map[i] >= 0) found++; }
            if (found == 0) return "[zivice] the TreeRenderer has no hedge modules: run Magistrala/Build trees, then Build tile buildings";
            var mine = new HashSet<int>(map.Where(i => i >= 0));
            var src = File.ReadAllBytes(tp); var outB = new List<byte>(src.Length + 1024); int removed = 0;
            for (int o = 0; o + 24 <= src.Length; o += 24)
            {
                int proto = (int)BitConverter.ToSingle(src, o + 20); if (mine.Contains(proto)) { removed++; continue; }
                for (int k = 0; k < 24; k++) outB.Add(src[o + k]);
            }
            int added = 0;
            if (File.Exists(SAVED))
            {
                var sv = File.ReadAllBytes(SAVED);
                for (int o = 0; o + 24 <= sv.Length; o += 24)
                {
                    int id = (int)BitConverter.ToSingle(sv, o + 20); if (id < 0 || id >= map.Length || map[id] < 0) continue;
                    for (int k = 0; k < 20; k++) outB.Add(sv[o + k]);
                    outB.AddRange(BitConverter.GetBytes((float)map[id])); added++;
                }
            }
            File.WriteAllBytes(tp, outB.ToArray()); AssetDatabase.ImportAsset(tp);
            tr.data = AssetDatabase.LoadAssetAtPath<TextAsset>(tp); EditorUtility.SetDirty(tr);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(tr.gameObject.scene);
            return $"[zivice] trees.bytes: {removed} old hedge modules out, {added} in ({found}/{Ids.Length} prototypes)";
        }
    }
}
