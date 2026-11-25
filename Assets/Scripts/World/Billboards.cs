// Billboards (Marko 2026-11-23): our own invented ads on the empty boards and painted walls of the mag-23-11-v2 models.
// Every face is its own renderer named ad_<n> (ad_01, ad_02, ad_03a/b, ad_04a/b, ad_06, ad_07a/b, ad_08a + ad_08b), uv 0..1 over
// the face, u from the reader's left to right (README_11-23-v2, znakovi/reklamna-mjesta.json). Pictures: Assets/Resources/Reklame/<id>.png,
// made by BalkanDriver/assets-src/reklame/gen_reklame.py (not in git, copied in by hand; import settings: Editor/ReklameImport.cs).
// Once after a scene loads: find the ad_ renderers, pick an ad of the face's format (name table, else the aspect of the world bounds),
// give it one shared material per (ad, uv transform). No per-frame work, no component left behind.
//   format L (~1.9 : 1) big boards, P (0.83 : 1) upright board, W (~10 : 1) wall ad_06, Z (59 : 1) the 198 m wall ad_08: a 7.3 : 1 tile
//   repeated ~8 times along u (wrap Repeat). ad_08a carries u 0..0.792 of the whole wall and ad_08b 0.792..1 already in the mesh,
//   so both get the same material; if the imported mesh turns out to have its own 0..1 (readable mesh), the uv is remapped.
//   Other formats: cover fit — the picture keeps its proportion, the few % that don't fit are cropped equally on both sides.
// Shader: URP/Lit (used by many scene materials, so it is in the player); fallback Simple Lit, then the face's own shader.
// Missing pictures: the face is left as it is, one warning in total. -reklame nasumicno = random ad of the right format per face.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Magistrala
{
    public static class Billboards
    {
        public const string ResourceDir = "Reklame/";
        /// set if the importer turns out to mirror the faces (README: u left→right for the reader, v = 0 at the top in glTF; glTFast flips v to Unity's convention)
        public static bool FlipU = false, FlipV = false;

        // ad id, format, aspect of the picture (w / h of the PNG)
        static readonly (string id, char fmt, float aspect)[] Ads =
        {
            ("konoba_kod_pave", 'L', 2048f / 1080), ("otpad_sime", 'L', 2048f / 1080), ("pekara_bura", 'L', 2048f / 1080),
            ("festa_na_rivi", 'L', 2048f / 1080), ("ne_vozi_pijan", 'L', 2048f / 1080), ("basic_prijevoz", 'L', 2048f / 1080),
            ("apartmani_mare", 'L', 2048f / 1080),
            ("vulkanizer_brace", 'P', 1708f / 2048), ("ulje_od_dida", 'P', 1708f / 2048),
            ("zid_autoskola_polako", 'W', 2048f / 208), ("zid_more_nece_pobic", 'W', 2048f / 208),
            ("zid8_sedam_kastela", 'Z', 2048f / 280), ("zid8_basic_prijevoz", 'Z', 2048f / 280),
        };

        // face name -> ad, face width and height (m), u range of this piece within the whole picture
        struct Face { public string ad; public float w, h, u0, u1; }
        static readonly Dictionary<string, Face> Table = new Dictionary<string, Face>
        {
            { "ad_01",  new Face { ad = "pekara_bura",          w = 5.3f,   h = 2.8f,  u0 = 0, u1 = 1 } },
            { "ad_02",  new Face { ad = "vulkanizer_brace",     w = 2.5f,   h = 3.0f,  u0 = 0, u1 = 1 } },
            { "ad_03a", new Face { ad = "konoba_kod_pave",      w = 11.5f,  h = 6.0f,  u0 = 0, u1 = 1 } },
            { "ad_03b", new Face { ad = "basic_prijevoz",       w = 11.5f,  h = 6.0f,  u0 = 0, u1 = 1 } },
            { "ad_04a", new Face { ad = "festa_na_rivi",        w = 9.0f,   h = 5.0f,  u0 = 0, u1 = 1 } },
            { "ad_04b", new Face { ad = "ne_vozi_pijan",        w = 9.0f,   h = 5.0f,  u0 = 0, u1 = 1 } },
            { "ad_06",  new Face { ad = "zid_more_nece_pobic",  w = 28.4f,  h = 2.9f,  u0 = 0, u1 = 1 } },
            { "ad_07a", new Face { ad = "otpad_sime",           w = 11.0f,  h = 5.5f,  u0 = 0, u1 = 1 } },
            { "ad_07b", new Face { ad = "apartmani_mare",       w = 11.0f,  h = 5.5f,  u0 = 0, u1 = 1 } },
            // the long wall: one picture over two models, w = the whole wall (198.5 m)
            { "ad_08a", new Face { ad = "zid8_sedam_kastela",   w = 198.5f, h = 3.35f, u0 = 0,       u1 = 0.7917f } },
            { "ad_08b", new Face { ad = "zid8_sedam_kastela",   w = 198.5f, h = 3.35f, u0 = 0.7917f, u1 = 1 } },
        };

        static readonly Dictionary<string, Texture2D> texCache = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Material> matCache = new Dictionary<string, Material>();
        static Shader shader; static bool shaderLooked, warned, random;
        static readonly System.Random rnd = new System.Random();

        public static int Applied { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var a = System.Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < a.Length; i++) if (a[i] == "-reklame") random = a[i + 1].StartsWith("nasum");
            SceneManager.sceneLoaded -= OnLoaded; SceneManager.sceneLoaded += OnLoaded;
            ApplyAll();
        }

        // an additive scene (a streamed world cell): only its own renderers (a scan of every loaded renderer costs ms per cell)
        static void OnLoaded(Scene s, LoadSceneMode m)
        {
            if (m != LoadSceneMode.Additive) return;
            int n = 0;
            foreach (var g in s.GetRootGameObjects()) foreach (var r in g.GetComponentsInChildren<MeshRenderer>(true)) if (Apply(r)) n++;
            Applied += n;
        }

        /// walks every loaded MeshRenderer once; call again after streaming in new model tiles
        public static void ApplyAll()
        {
            var t0 = Time.realtimeSinceStartup;
            int n = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (Apply(r)) n++;
            Applied += n;
            if (n > 0) Debug.Log($"[reklame] {n} ploha, {(Time.realtimeSinceStartup - t0) * 1000:0} ms");
        }

        /// "ad_03a", "ad_03a.001", "ad_03a (1)", "ad_03a_0" (glTFast primitive child) -> "ad_03a"; null when not an ad face
        public static string FaceName(Transform t)
        {
            for (int k = 0; k < 2 && t; k++, t = t.parent)
            {
                string s = t.name;
                if (s.Length < 5 || !s.StartsWith("ad_") || !char.IsDigit(s[3])) continue;
                int e = 3;
                while (e < s.Length && char.IsDigit(s[e])) e++;
                if (e < s.Length && (s[e] == 'a' || s[e] == 'b') && (e + 1 == s.Length || !char.IsLetter(s[e + 1]))) e++;
                return s.Substring(0, e);
            }
            return null;
        }

        static bool Apply(MeshRenderer r)
        {
            string name = FaceName(r.transform);
            if (name == null) return false;
            var mf = r.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) return false;

            // the face: from the table, else from the world bounds (upright face: height = y, width = horizontal diagonal)
            if (!Table.TryGetValue(name, out var f))
            {
                var b = r.bounds;
                f = new Face { w = Mathf.Max(.01f, Mathf.Sqrt(b.size.x * b.size.x + b.size.z * b.size.z)), h = Mathf.Max(.01f, b.size.y), u0 = 0, u1 = 1 };
            }
            float faceAspect = f.w / f.h;
            char fmt = faceAspect < 1.2f ? 'P' : faceAspect < 4 ? 'L' : faceAspect < 25 ? 'W' : 'Z';

            string ad = f.ad;
            if (random || ad == null || Format(ad) != fmt) ad = Pick(fmt, name);
            var tex = Load(ad);
            if (!tex)
            {
                // the table's ad is missing: any other of the same format
                foreach (var x in Ads) if (x.fmt == fmt && (tex = Load(x.id))) { ad = x.id; break; }
            }
            if (!tex)
            {
                if (!warned) { warned = true; Debug.LogWarning("[reklame] nema slika u Resources/" + ResourceDir + " (assets-src/reklame/gen_reklame.py --unity ...): plohe ad_* ostaju prazne"); }
                return false;
            }
            float texAspect = (float)tex.width / tex.height;

            // uv transform: texcoord = uv * s + o
            Vector2 s = Vector2.one, o = Vector2.zero;
            if (fmt == 'Z')
            {
                // repeated tile along the whole wall; this piece's mesh u already runs f.u0..f.u1 (README), unless the mesh says 0..1
                float n = Mathf.Max(1, Mathf.Round(faceAspect / texAspect));
                if (MeshURange(mf.sharedMesh, out float a0, out float a1) && a1 - a0 > .01f
                    && (Mathf.Abs(a0 - f.u0) > .02f || Mathf.Abs(a1 - f.u1) > .02f))
                {
                    // mesh u a0..a1 must become f.u0..f.u1: u' = f.u0 + (u - a0) * (f.u1 - f.u0) / (a1 - a0)
                    float k = (f.u1 - f.u0) / (a1 - a0);
                    s.x = n * k; o.x = n * (f.u0 - a0 * k);
                }
                else { s.x = n; o.x = 0; }
            }
            else if (faceAspect > texAspect) { s.y = texAspect / faceAspect; o.y = (1 - s.y) * .5f; }   // face wider: crop top / bottom
            else { s.x = faceAspect / texAspect; o.x = (1 - s.x) * .5f; }                                 // face narrower: crop the sides
            if (FlipU) { o.x += s.x; s.x = -s.x; }
            if (FlipV) { o.y += s.y; s.y = -s.y; }

            var mat = GetMaterial(ad, tex, s, o, r.sharedMaterial);
            if (!mat) return false;
            var mats = r.sharedMaterials;
            bool any = false;
            for (int i = 0; i < mats.Length; i++) if (mats[i] && mats[i].name.StartsWith("ad_")) { mats[i] = mat; any = true; }
            if (!any) for (int i = 0; i < mats.Length; i++) mats[i] = mat;
            r.sharedMaterials = mats;
            return true;
        }

        static char Format(string ad) { foreach (var x in Ads) if (x.id == ad) return x.fmt; return '?'; }

        static string Pick(char fmt, string face)
        {
            var l = new List<string>();
            foreach (var x in Ads) if (x.fmt == fmt) l.Add(x.id);
            if (l.Count == 0) return null;
            // random: per session; otherwise steady per face name
            int h = 17; foreach (char c in face) h = h * 31 + c;
            int i = random ? rnd.Next(l.Count) : (h & 0x7fffffff) % l.Count;
            return l[i];
        }

        static Texture2D Load(string id)
        {
            if (id == null) return null;
            if (texCache.TryGetValue(id, out var t)) return t;
            t = Resources.Load<Texture2D>(ResourceDir + id);
            if (t)
            {
                bool tile = id.StartsWith("zid8_");
                t.wrapModeU = tile ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                t.wrapModeV = TextureWrapMode.Clamp;
            }
            texCache[id] = t;
            return t;
        }

        /// u range of the first uv channel, only when the mesh is CPU-readable (imported meshes often are not: then the README is trusted)
        static bool MeshURange(Mesh m, out float u0, out float u1)
        {
            u0 = 0; u1 = 1;
            if (!m.isReadable) return false;
            var uv = new List<Vector2>();
            m.GetUVs(0, uv);
            if (uv.Count == 0) return false;
            u0 = float.MaxValue; u1 = float.MinValue;
            foreach (var p in uv) { if (p.x < u0) u0 = p.x; if (p.x > u1) u1 = p.x; }
            return true;
        }

        static Material GetMaterial(string ad, Texture2D tex, Vector2 s, Vector2 o, Material original)
        {
            string key = $"{ad}|{s.x:0.0000}|{s.y:0.0000}|{o.x:0.0000}|{o.y:0.0000}";
            if (matCache.TryGetValue(key, out var m) && m) return m;
            if (!shaderLooked)
            {
                shaderLooked = true;
                shader = Shader.Find("Universal Render Pipeline/Lit");
                if (!shader) shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            }
            if (shader) m = new Material(shader);
            else if (original) m = new Material(original);   // last resort: the face's own material (whatever the importer gave it)
            else return null;
            m.name = "Reklama " + ad;
            foreach (var p in new[] { "_BaseMap", "_MainTex", "baseColorTexture" })
                if (m.HasProperty(p)) { m.SetTexture(p, tex); m.SetTextureScale(p, s); m.SetTextureOffset(p, o); }
            foreach (var p in new[] { "_BaseColor", "_Color", "baseColorFactor" })
                if (m.HasProperty(p)) m.SetColor(p, Color.white);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", ad.StartsWith("zid") ? .05f : .25f);   // paint on plaster / printed vinyl
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0);
            matCache[key] = m;
            return m;
        }

        /// editor menu (Magistrala/Reklame: pregled): which face would get which ad, without touching the scene
        public static string Describe(MeshRenderer r)
        {
            string name = FaceName(r.transform);
            if (name == null) return null;
            Table.TryGetValue(name, out var f);
            return $"{r.name} ({name}) -> {(f.ad ?? "po omjeru")}{(Load(f.ad) ? "" : "  [NEMA SLIKE]")}";
        }
    }
}
