// Traffic signals and road signs of the Kaštela world (Magistrala ▸ Build signals; run after Igra: podaci and Build traffic).
// Everything is generated from data, nothing is placed by hand:
//   signal furniture  the owner's props of the finished junction models (junction-overrides-modeli.json, node `props`, written by
//                     the web importer from krizanja.json `sig` objects: gantry with boards, cantilevers, stop-line posts with
//                     their plates, keep-right bollards). Positions, mount types, board texts and post contents are his; sizes are
//                     the web game's (props/gantrySpec.ts). Every head shows its signal group of the model's plan (lens renderers
//                     switched by TrafficSignals on the TrafficSystem clock — the same one the AI cars stop by); a head of a
//                     dedicated left-turn lane (`mv: left`, `laneMv`) shows the plan's left-turn group (signals.left / SG2).
//   approach signs    STOP / give way at the graph's stop / give_way approaches (graph.json + override layers: node `approaches`)
//                     and at the painted stop lines of the models without a signal plan (41, 42, 45, d11, s01, s13);
//   crossings         the blue pedestrian-crossing sign at the zebras of the unsignalised models and road tiles (krizanja.json /
//                     dionice.json `zebra`), both kerbs, facing each direction;
//   speed limits      where the HUD's limit (GameWorld edges, GameDataBuilder.SpeedLimit) changes along a road, and repeated on
//                     the D8 after each signalised junction;
//   direction boards  the research package's roadside boards and truck-weight discs (road-signs.json);
//   place names       where a numbered road or the old road crosses a settlement boundary (GameWorld place grid, OSM boundaries):
//                     the entry board of the place ahead, its "end of place" board on the back;
//   street names      a blue plate on the corner of every named street that meets another one (not on the D8).
// Static geometry is merged per 256 m chunk into one mesh with one material (the sign atlas, SignArt.cs: faces, steel, black);
// poles, posts and bollards get box colliders.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Magistrala.Game;
using Magistrala.Traffic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public static class SignsBuilder
    {
        const string GEN = WorldBuilder.GEN;
        const float CH = 256;

        class Chunk { public readonly List<Vector3> v = new List<Vector3>(), n = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<int> t = new List<int>(); }
        static Dictionary<Vector2Int, Chunk> chunks;
        static List<(Vector3 c, Quaternion r, Vector3 s)> cols;
        static SignAtlas A;
        static Rect rSteel, rBlack, rWhite, rBollard;
        // audit
        public static readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        public static readonly List<string> log = new List<string>();
        /// every roadside post: kind, game x, z, travel heading (deg, 0 = north) — Logs/znakovi-polozaji.tsv for the audit
        static readonly List<string> where = new List<string>();
        static void Count(string k, int d = 1) { counts.TryGetValue(k, out int c); counts[k] = c + d; }

        // ------------------------------------------------------------------ geometry
        static Chunk At(Vector3 p)
        {
            var k = new Vector2Int(Mathf.FloorToInt(p.x / CH), Mathf.FloorToInt(p.z / CH));
            if (!chunks.TryGetValue(k, out var c)) chunks[k] = c = new Chunk();
            return c;
        }
        static Vector2 UvAt(Rect r, float u, float v) => new Vector2(r.xMin + r.width * u, r.yMin + r.height * v);

        /// a quad facing n (front side towards +n), right / up as the viewer in front sees them
        static void Quad(Vector3 c, Vector3 n, Vector3 up, float w, float h, Rect r, Vector3? anchor = null)
        {
            var k = At(anchor ?? c); var right = Vector3.Cross(up, -n).normalized; up = up.normalized;
            int b = k.v.Count;
            k.v.Add(c - right * w / 2 - up * h / 2); k.v.Add(c - right * w / 2 + up * h / 2); k.v.Add(c + right * w / 2 + up * h / 2); k.v.Add(c + right * w / 2 - up * h / 2);
            for (int i = 0; i < 4; i++) k.n.Add(n.normalized);
            k.uv.Add(UvAt(r, 0, 0)); k.uv.Add(UvAt(r, 0, 1)); k.uv.Add(UvAt(r, 1, 1)); k.uv.Add(UvAt(r, 1, 0));
            Tri(k, b, b + 1, b + 2, n); Tri(k, b, b + 2, b + 3, n);
        }
        static void Tri(Chunk k, int a, int b, int c, Vector3 n)
        {
            if (Vector3.Dot(Vector3.Cross(k.v[b] - k.v[a], k.v[c] - k.v[a]), n) >= 0) { k.t.Add(a); k.t.Add(b); k.t.Add(c); }
            else { k.t.Add(a); k.t.Add(c); k.t.Add(b); }
        }
        /// box: centre, rotation, full size; one solid atlas cell
        static void Box(Vector3 c, Quaternion q, Vector3 s, Rect r, bool collide = false)
        {
            var k = At(c);
            var ax = new[] { q * Vector3.right * s.x / 2, q * Vector3.up * s.y / 2, q * Vector3.forward * s.z / 2 };
            for (int i = 0; i < 3; i++)
                foreach (int sg in new[] { 1, -1 })
                {
                    var n = ax[i] * sg; var a = ax[(i + 1) % 3]; var bb = ax[(i + 2) % 3];
                    int b0 = k.v.Count; var fc = c + n;
                    k.v.Add(fc - a - bb); k.v.Add(fc + a - bb); k.v.Add(fc + a + bb); k.v.Add(fc - a + bb);
                    var nn = n.normalized; for (int j = 0; j < 4; j++) { k.n.Add(nn); k.uv.Add(r.center); }
                    Tri(k, b0, b0 + 1, b0 + 2, nn); Tri(k, b0, b0 + 2, b0 + 3, nn);
                }
            if (collide) cols.Add((c, q, s));
        }
        /// vertical cylinder from base, radius rad, height h; uv: a solid cell, or a texture wrapped around (bollard stripes)
        static void Cyl(Vector3 b, float h, float rad, Rect r, bool wrap = false, int sides = 8)
        {
            var k = At(b); int b0 = k.v.Count;
            for (int i = 0; i <= sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                k.v.Add(b + d * rad); k.v.Add(b + d * rad + Vector3.up * h); k.n.Add(d); k.n.Add(d);
                k.uv.Add(wrap ? UvAt(r, i / (float)sides, 0) : r.center); k.uv.Add(wrap ? UvAt(r, i / (float)sides, 1) : r.center);
            }
            for (int i = 0; i < sides; i++)
            {
                int p = b0 + i * 2;
                Tri(k, p, p + 1, p + 3, k.n[p] + k.n[p + 2]); Tri(k, p, p + 3, p + 2, k.n[p] + k.n[p + 2]);
            }
            // cap
            int c0 = k.v.Count; k.v.Add(b + Vector3.up * h); k.n.Add(Vector3.up); k.uv.Add(wrap ? UvAt(r, .5f, 1) : r.center);
            for (int i = 0; i <= sides; i++) { float a = i * Mathf.PI * 2 / sides; k.v.Add(b + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * rad + Vector3.up * h); k.n.Add(Vector3.up); k.uv.Add(wrap ? UvAt(r, .5f, 1) : r.center); }
            for (int i = 0; i < sides; i++) Tri(k, c0, c0 + 1 + i, c0 + 2 + i, Vector3.up);
        }
        static void Pole(Vector3 b, float h, float rad = .045f)
        {
            Cyl(b, h, rad, rSteel);
            float cw = Mathf.Max(.14f, rad * 2.4f);
            cols.Add((b + Vector3.up * h / 2, Quaternion.identity, new Vector3(cw, h, cw)));
        }
        /// a plate (sign face) centred at c facing traffic that travels along t; its back towards +t: the face's silhouette in
        /// steel, or another face (double-sided street plates, the "end of place" board) — Quad lays every face out as its own
        /// viewer sees it, so a face on the back reads the right way round
        static void Plate(Vector3 c, Vector3 t, string face, string back, float w, float h)
        {
            t.y = 0; t.Normalize();
            Quad(c - t * .012f, -t, Vector3.up, w, h, R(face));
            Quad(c + t * .012f, t, Vector3.up, w, h, R(back));
        }
        /// a face's atlas rect (the first, dry pass paints every face before the atlas is packed: whole texture meanwhile)
        static Rect R(string key) => A.uv.TryGetValue(key, out var r) ? r : new Rect(0, 0, 1, 1);

        // ------------------------------------------------------------------ ground
        static readonly HashSet<Transform> skipRoots = new HashSet<Transform>();
        static float Ground(Vector3 p, float yHint, out bool hit)
        {
            hit = false; float best = yHint;
            var hits = Physics.RaycastAll(new Vector3(p.x, yHint + 5f, p.z), Vector3.down, 25f, ~0, QueryTriggerInteraction.Ignore);
            float bd = float.MaxValue;
            foreach (var h in hits)
            {
                if (h.collider.attachedRigidbody && !h.collider.attachedRigidbody.isKinematic) continue;
                if (h.collider.name.StartsWith("TrafficCar")) continue;
                float d = h.distance; if (d < bd) { bd = d; best = h.point.y; hit = true; }
            }
            return best;
        }

        // ------------------------------------------------------------------ signal heads
        static Mesh lens;
        static Rect rLens;
        static List<TrafficSignals.Head> heads;
        public static Mesh LensMesh()
        {
            string p = SignAtlas.DIR + "/Lens.asset";
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(p);
            if (m) return m;
            m = new Mesh { name = "Lens" };
            var v = new List<Vector3> { Vector3.zero }; var t = new List<int>(); var n = new List<Vector3> { Vector3.back };
            const int N = 14;
            for (int i = 0; i < N; i++) { float a = i * Mathf.PI * 2 / N; v.Add(new Vector3(Mathf.Cos(a) * .5f, Mathf.Sin(a) * .5f, 0)); n.Add(Vector3.back); }
            for (int i = 0; i < N; i++) { t.Add(0); t.Add(1 + (i + 1) % N); t.Add(1 + i); }
            m.SetVertices(v); m.SetNormals(n); m.SetTriangles(t, 0); m.RecalculateBounds();
            AssetDatabase.CreateAsset(m, p);
            return m;
        }
        /// an unlit lens: a dark disc baked into the chunk mesh; the lit one is drawn over it by TrafficSignals (instanced)
        static Matrix4x4 LensAt(Vector3 c, Vector3 t, float d)
        {
            var k = At(c); var n = -t; var right = Vector3.Cross(Vector3.up, t); int b0 = k.v.Count; const int N = 12;
            k.v.Add(c); k.n.Add(n); k.uv.Add(rLens.center);
            for (int i = 0; i < N; i++) { float a = i * Mathf.PI * 2 / N; k.v.Add(c + (right * Mathf.Cos(a) + Vector3.up * Mathf.Sin(a)) * d / 2); k.n.Add(n); k.uv.Add(rLens.center); }
            for (int i = 0; i < N; i++) Tri(k, b0, b0 + 1 + i, b0 + 1 + (i + 1) % N, n);
            return Matrix4x4.TRS(c - t * .004f, Quaternion.LookRotation(t, Vector3.up), Vector3.one * d);
        }
        /// a signal head at c facing traffic travelling along t: vertical (red on top) or horizontal (red on the driver's left)
        static void Head(Vector3 c, Vector3 t, bool vertical, float lensD, string plan, string group)
        {
            t.y = 0; t.Normalize(); var right = Vector3.Cross(Vector3.up, t);
            var q = Quaternion.LookRotation(t, Vector3.up);
            float pitch = lensD * 1.45f, depth = .24f;
            var size = vertical ? new Vector3(lensD * 1.55f, pitch * 3.1f, depth) : new Vector3(pitch * 3.1f, lensD * 1.55f, depth);
            Box(c, q, size, rBlack);
            // white-rimmed back plate (Croatian heads carry one)
            Box(c + t * (depth / 2 + .02f), q, size + new Vector3(.16f, .16f, -depth + .02f), rWhite);
            Box(c + t * (depth / 2 + .035f), q, size + new Vector3(.1f, .1f, -depth + .02f), rBlack);
            Vector3 Off(int i) => vertical ? Vector3.up * (1 - i) * pitch : -right * (1 - i) * pitch; // i 0 red, 1 amber, 2 green
            var front = c - t * (depth / 2 + .006f);
            var h = new TrafficSignals.Head { corridor = -1, planKey = plan, group = group, mRed = LensAt(front + Off(0), t, lensD), mAmber = LensAt(front + Off(1), t, lensD), mGreen = LensAt(front + Off(2), t, lensD) };
            // visors
            for (int i = 0; i < 3; i++) Box(front + Off(i) + Vector3.up * lensD * .55f - t * .09f, q, new Vector3(lensD * 1.05f, .02f, .18f), rBlack);
            heads.Add(h); Count("glave semafora");
        }

        // ------------------------------------------------------------------ data
        class Prop { public string id, kind, model, style, mv; public long approach; public double x, z, yaw; public float height, span; public float[] lanes; public string[] text; public string[] laneMv; public HashSet<string> flags = new HashSet<string>(); public List<(float u, string text, string style)> boards = new List<(float, string, string)>(); }

        static List<Prop> Props(double minX, double maxX)
        {
            var res = new List<Prop>();
            var ov = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "junction-overrides-modeli.json")));
            foreach (var o in MiniJson.L(ov["overrides"]))
            {
                var oo = MiniJson.O(MiniJson.O(o)["overrides"]);
                foreach (var key in new[] { "nodes", "addNodes" })
                    foreach (var nd in MiniJson.L(MiniJson.G(oo, key)) ?? new List<object>())
                        foreach (var po in MiniJson.L(MiniJson.G(MiniJson.O(nd), "props")) ?? new List<object>())
                        {
                            var p = MiniJson.O(po); double x = MiniJson.D(p["x"]); if (x < minX || x > maxX) continue;
                            var pr = new Prop { id = (string)p["id"], kind = (string)p["kind"], x = x, z = MiniJson.D(p["z"]), yaw = MiniJson.D(MiniJson.G(p, "yaw") ?? 0.0) };
                            int cut = pr.id.LastIndexOf('-'); cut = cut > 0 ? pr.id.LastIndexOf('-', cut - 1) : -1; pr.model = cut > 0 ? pr.id.Substring(0, cut) : pr.id;
                            pr.approach = MiniJson.G(p, "approach") is object ap ? (long)MiniJson.D(ap) : 0;
                            pr.height = MiniJson.G(p, "height") is object ht ? MiniJson.F(ht) : 0; pr.span = MiniJson.G(p, "span") is object sp ? MiniJson.F(sp) : 0;
                            pr.style = MiniJson.G(p, "style") as string; pr.mv = MiniJson.G(p, "mv") as string;
                            pr.lanes = (MiniJson.L(MiniJson.G(p, "lanes")) ?? new List<object>()).Select(MiniJson.F).ToArray();
                            pr.text = (MiniJson.L(MiniJson.G(p, "text")) ?? new List<object>()).Select(q => q as string ?? "").ToArray();
                            pr.laneMv = (MiniJson.L(MiniJson.G(p, "laneMv")) ?? new List<object>()).Select(q => q as string).ToArray();
                            foreach (var f in MiniJson.L(MiniJson.G(p, "flags")) ?? new List<object>()) pr.flags.Add((string)f);
                            foreach (var b in MiniJson.L(MiniJson.G(p, "boards")) ?? new List<object>()) { var bo = MiniJson.O(b); pr.boards.Add((MiniJson.F(bo["u"]), MiniJson.G(bo, "text") as string ?? "", MiniJson.G(bo, "style") as string ?? "yellow")); }
                            res.Add(pr);
                        }
            }
            return res;
        }

        class Sig { public string key; public bool plan; public Dictionary<long, string> groups = new Dictionary<long, string>(), left = new Dictionary<long, string>(); public List<(Vector2 p, float a, float w)> stops = new List<(Vector2, float, float)>(); public HashSet<string> planGroups = new HashSet<string>(); }
        static Dictionary<string, Sig> Sigs()
        {
            var res = new Dictionary<string, Sig>();
            var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "model-junctions.json")));
            foreach (var jo in MiniJson.L(root["junctions"]))
            {
                var j = MiniJson.O(jo); var s = new Sig { key = (string)j["key"] }; res[s.key] = s;
                var sig = MiniJson.O(MiniJson.G(j, "signals")); if (sig == null) continue;
                if (MiniJson.G(sig, "plan") is Dictionary<string, object> plan)
                {
                    s.plan = true;
                    foreach (var p in MiniJson.L(plan["phases"])) if (MiniJson.G(MiniJson.O(p), "group") is string g) s.planGroups.Add(g);
                }
                foreach (var kv in MiniJson.O(MiniJson.G(sig, "groups")) ?? new Dictionary<string, object>()) foreach (var e in MiniJson.L(kv.Value)) s.groups[(long)MiniJson.D(e)] = kv.Key;
                foreach (var kv in MiniJson.O(MiniJson.G(sig, "left")) ?? new Dictionary<string, object>()) if (long.TryParse(kv.Key, out long e) && kv.Value is string g) s.left[e] = g;
                foreach (var q in MiniJson.L(MiniJson.G(sig, "stops")) ?? new List<object>()) { var qo = MiniJson.O(q); s.stops.Add((new Vector2(MiniJson.F(qo["x"]), MiniJson.F(qo["z"])), MiniJson.F(qo["a"]), MiniJson.G(qo, "w") is object w ? MiniJson.F(w) : 3.5f)); }
            }
            return res;
        }

        static Vector3 TravelU(double yaw) => new Vector3((float)Math.Sin(yaw), 0, (float)Math.Cos(yaw)); // props: yaw 0 = north, π/2 = east
        static Vector3 DirU(float a) => new Vector3(Mathf.Cos(a), 0, -Mathf.Sin(a));                     // models: a 0 = east, π/2 = south

        // ------------------------------------------------------------------ signal furniture (props)
        static void BuildSignals(List<ModelJ> models, Dictionary<string, Sig> sigs, List<(Vector2 p, Vector2 dir, int plan, string group, string model)> groupStops)
        {
            var props = Props(WorldData.MIN_X, -400);
            var byModel = models.ToDictionary(m => m.key);
            string GroupOf(Prop p, bool leftHead)
            {
                if (!sigs.TryGetValue(p.model, out var s)) return "SG1";
                if (leftHead)
                {
                    if (s.left.TryGetValue(p.approach, out var lg)) return lg;
                    if (s.planGroups.Contains("SG2")) return "SG2";
                }
                if (s.groups.TryGetValue(p.approach, out var g)) return g;
                // approach edge renumbered by a later package (mag-11-19 tiles): the nearest stop line of this model whose direction
                // matches the prop's travel direction
                var t = TravelU(p.yaw); var tg = new Vector2(t.x, -t.z); var pp = new Vector2((float)p.x, (float)p.z);
                string best = null; float bd = 40;
                foreach (var st in groupStops)
                {
                    if (st.model != p.model || Vector2.Dot(st.dir, tg) < .7f) continue;
                    float d = Vector2.Distance(st.p, pp); if (d < bd) { bd = d; best = st.group; }
                }
                if (best == null) { log.Add($"{p.id}: skupina nije nađena (prilaz {p.approach}) → SG1"); return "SG1"; }
                return best;
            }
            float Y(Prop p, Vector3 u) { float yh = byModel.TryGetValue(p.model, out var m) ? (float)m.y : 0; float y = Ground(u, yh, out bool ok); if (!ok) log.Add($"{p.id}: nema tla"); return y; }

            // post stacks: props of one approach standing at the same point share one pole
            var stacks = new Dictionary<string, List<Prop>>();
            foreach (var p in props)
            {
                if (p.kind != "signal_pole" && !p.kind.StartsWith("sign_")) continue;
                string k = $"{p.model}|{Math.Round(p.x / .3)}|{Math.Round(p.z / .3)}";
                if (!stacks.TryGetValue(k, out var l)) stacks[k] = l = new List<Prop>(); l.Add(p);
            }
            foreach (var l in stacks.Values)
            {
                var p0 = l[0]; var b = W.U(p0.x, 0, p0.z); b.y = Y(p0, b); var t = TravelU(p0.yaw);
                float top = 0;
                foreach (var p in l)
                {
                    var c = b + Vector3.up * p.height;
                    switch (p.kind)
                    {
                        case "signal_pole":
                            Head(c - t * .12f, t, true, .2f, p.model, GroupOf(p, p.mv == "left")); top = Mathf.Max(top, p.height + .55f); break;
                        case "sign_priority": Plate(c - t * .08f, t, A.Priority(), A.Back("diamond"), .8f, .8f); top = Mathf.Max(top, p.height + .45f); Count("znak prednost (rekvizit)"); break;
                        case "sign_pedestrian": Plate(c - t * .08f, t, A.Pedestrian(), A.Back("square"), .6f, .6f); top = Mathf.Max(top, p.height + .35f); Count("znak pješački prijelaz (rekvizit)"); break;
                        case "sign_stop": Plate(c - t * .08f, t, A.Stop(), A.Back("oct"), .7f, .7f); top = Mathf.Max(top, p.height + .4f); Count("znak STOP (rekvizit)"); break;
                        case "sign_give_way": Plate(c - t * .08f, t, A.GiveWay(), A.Back("tri"), .9f, .9f); top = Mathf.Max(top, p.height + .5f); Count("znak prednost prolaska (rekvizit)"); break;
                        case "sign_prohibition":
                            Plate(c - t * .08f, t, p.text.Length > 0 && p.text[0].Length > 0 ? A.Disc(p.text[0]) : A.NoLeft(), A.Back("round"), .6f, .6f); top = Mathf.Max(top, p.height + .35f); Count("znak zabrana (rekvizit)"); break;
                        case "sign_mandatory": Plate(c - t * .08f, t, A.Ahead(), A.Back("round"), .6f, .6f); top = Mathf.Max(top, p.height + .35f); Count("znak obvezni smjer (rekvizit)"); break;
                        case "sign_direction":
                            {
                                float w = p.span > 0 ? p.span : 2.4f; string tx = string.Join(" / ", p.text);
                                Plate(c - t * .1f, t, A.Board(tx, p.style ?? "yellow", w, 1.2f), A.Back("rect"), w, 1.2f); top = Mathf.Max(top, p.height + .2f); Count("putokaz (rekvizit)"); break;
                            }
                        default: log.Add($"{p.id}: nepoznata vrsta {p.kind}"); break;
                    }
                }
                Pole(b, Mathf.Max(top, 2.2f), .055f); Count("stup znaka / semafora");
            }

            foreach (var p in props)
            {
                var t = TravelU(p.yaw); var right = Vector3.Cross(Vector3.up, t);
                var o = W.U(p.x, 0, p.z);
                switch (p.kind)
                {
                    case "gantry":
                        {
                            float span = p.span > 0 ? p.span : 12;
                            var L = o - right * span / 2; var R = o + right * span / 2; L.y = Y(p, L); R.y = Y(p, R);
                            float baseY = Mathf.Min(L.y, R.y), H = 7.35f;
                            var q = Quaternion.LookRotation(t, Vector3.up);
                            foreach (var b0 in new[] { L, R })
                            {
                                var b = b0; float hh = H + baseY - b.y;
                                Box(b + Vector3.up * hh / 2, q, new Vector3(.3f, hh, .3f), rSteel, true);
                                Box(b + Vector3.up * .05f, q, new Vector3(.6f, .1f, .6f), rSteel);
                            }
                            // truss: two chords and the posts' ties
                            var mid = (L + R) / 2; mid.y = baseY;
                            foreach (float cy in new[] { 6.45f, 7.2f }) Box(mid + Vector3.up * cy, q, new Vector3(span + .35f, .16f, .16f), rSteel);
                            for (int i = 0; i <= Mathf.Max(2, Mathf.RoundToInt(span / 1.6f)); i++)
                            {
                                float f = i / (float)Mathf.Max(2, Mathf.RoundToInt(span / 1.6f));
                                var pp = Vector3.Lerp(L, R, f); pp.y = baseY + 6.825f;
                                Box(pp, q * Quaternion.Euler(0, 0, i % 2 == 0 ? 35 : -35), new Vector3(.06f, .9f, .06f), rSteel);
                            }
                            bool noHeads = p.flags.Contains("no_heads");
                            string g = GroupOf(p, false);
                            for (int i = 0; i < p.lanes.Length; i++)
                            {
                                var lc = o + right * p.lanes[i]; lc.y = baseY;
                                string tx = i < p.text.Length ? p.text[i] : "";
                                float w = Mathf.Min(2.8f, LaneGap(p.lanes, i, span) - .35f);
                                string st = StyleAt(p.style, i);
                                bool board = tx.Trim().Length > 0;
                                if (board) { Plate(lc + Vector3.up * 6.16f - t * .32f, t, A.Board(tx, st, w, 1.8f), A.Back("rect"), w, 1.8f); Count("ploča na portalu"); }
                                if (!noHeads)
                                {
                                    bool left = i < p.laneMv.Length && p.laneMv[i] == "left";
                                    Head(lc + Vector3.up * (board ? 4.95f : 5.95f) - t * .3f, t, false, .28f, p.model, left ? GroupOf(p, true) : g);
                                    if (board) Box(lc + Vector3.up * 5.18f - t * .3f, q, new Vector3(.05f, .25f, .05f), rSteel);
                                }
                            }
                            if (!noHeads)
                            {
                                if (!p.flags.Contains("no_left_head")) Head(L + Vector3.up * 3.0f - t * .3f + right * .15f, t, true, .2f, p.model, g);
                                if (!p.flags.Contains("no_right_head")) Head(R + Vector3.up * 3.0f - t * .3f - right * .15f, t, true, .2f, p.model, g);
                            }
                            foreach (var b in new[] { L, R })
                            {
                                if (p.flags.Contains("priority")) { Plate(b + Vector3.up * 4.15f - t * .25f, t, A.Priority(), A.Back("diamond"), .8f, .8f); Count("znak prednost (portal)"); }
                                if (p.flags.Contains("pedestrian")) { Plate(b + Vector3.up * 2.05f - t * .25f, t, A.Pedestrian(), A.Back("square"), .6f, .6f); Count("znak pješački prijelaz (portal)"); }
                            }
                            Count("portal"); break;
                        }
                    case "signal_cantilever_right":
                    case "signal_cantilever_left":
                        {
                            var b = o; b.y = Y(p, b);
                            var q = Quaternion.LookRotation(t, Vector3.up);
                            Cyl(b, 6.1f, .13f, rSteel, false, 10); cols.Add((b + Vector3.up * 3.05f, q, new Vector3(.3f, 6.1f, .3f)));
                            Box(b + Vector3.up * .05f, q, new Vector3(.5f, .1f, .5f), rSteel);
                            float reach = 1.5f;
                            foreach (var u in p.lanes) reach = Mathf.Max(reach, Mathf.Abs(u) + .9f);
                            foreach (var bd in p.boards) if (bd.text.Trim().Length > 0) reach = Mathf.Max(reach, Mathf.Abs(bd.u) + 1.5f);
                            float sgn = p.kind.EndsWith("right") ? -1 : 1;
                            var armC = b + right * sgn * reach / 2 + Vector3.up * 5.86f;
                            Box(armC, q, new Vector3(reach, .16f, .16f), rSteel);
                            // brace
                            var b1 = b + Vector3.up * 4.85f; var b2 = b + right * sgn * Mathf.Min(2.4f, reach) + Vector3.up * 5.8f;
                            Box((b1 + b2) / 2, Quaternion.LookRotation(b2 - b1, t) * Quaternion.Euler(90, 0, 0), new Vector3(.07f, (b2 - b1).magnitude, .07f), rSteel);
                            string g = GroupOf(p, false);
                            for (int i = 0; i < p.lanes.Length; i++)
                            {
                                bool left = i < p.laneMv.Length && p.laneMv[i] == "left";
                                bool vert = p.flags.Contains("vertical_heads");
                                Head(b + right * p.lanes[i] + Vector3.up * (vert ? 5.0f : 5.5f) - t * .1f, t, vert, vert ? .3f : .28f, p.model, left ? GroupOf(p, true) : g);
                            }
                            foreach (var bd in p.boards)
                            {
                                if (bd.text.Trim().Length == 0) continue;
                                Plate(b + right * bd.u + Vector3.up * 6.75f - t * .15f, t, A.Board(bd.text, bd.style, 2.4f, 1.4f), A.Back("rect"), 2.4f, 1.4f); Count("ploča na konzoli");
                            }
                            Count("konzola"); break;
                        }
                    case "bollard_keep_right":
                        {
                            var b = o; b.y = Y(p, b);
                            Cyl(b, .95f, .13f, rBollard, true, 12); cols.Add((b + Vector3.up * .5f, Quaternion.identity, new Vector3(.26f, 1f, .26f)));
                            Plate(b + Vector3.up * 1.2f, t, A.KeepRight(), A.Back("round"), .42f, .42f);
                            Box(b + Vector3.up * 1.0f + t * .03f, Quaternion.LookRotation(t), new Vector3(.04f, .14f, .04f), rSteel);
                            Count("stupić obilaženja"); break;
                        }
                }
            }
        }
        /// an AI stop line whose group has no head facing it in the owner's props (01 C: the D8 westbound gantry carries boards only,
        /// `no_heads`, its one head is the left-turn pole) gets a vertical head on a post at its right edge — the research package
        /// (paket-approaches.json) lists a signal_head_vertical on that approach's right post
        static void FallbackHeads(TrafficData data)
        {
            if (data?.corridors == null) return;
            foreach (var c in data.corridors)
            {
                if (c.stops == null) continue;
                foreach (var st in c.stops)
                {
                    int i = Mathf.Clamp(Mathf.RoundToInt(st.s / c.step), 0, c.axis.Length - 2);
                    var p = c.axis[i]; var fwd = c.axis[i + 1] - c.axis[i]; fwd.y = 0; fwd.Normalize();
                    if (heads.Exists(h => h.planKey == st.model && h.group == st.group && ((Vector3)h.mRed.GetColumn(3) - p).magnitude < 45 && Vector3.Dot(((Vector3)h.mRed.GetColumn(2)).normalized, fwd) > .7f)) continue;
                    float hi = -99; for (int k = 0; k < c.maxLanes; k++) { float o = c.laneOff[i * c.maxLanes + k]; if (!float.IsNaN(o)) hi = Mathf.Max(hi, o); }
                    var b = p + c.right[i] * (hi + 1.75f + .9f) + fwd * 1.0f; b.y = Ground(b, p.y, out _);
                    Pole(b, 3.45f, .07f);
                    Head(b + Vector3.up * 2.9f - fwd * .14f, fwd, true, .2f, st.model, st.group);
                    log.Add($"{c.name} {st.model} {st.group}: u rekvizitima nema glave te skupine za AI stop-crtu → zamjenski stup s glavom desno");
                    Count("zamjenska glava (AI stop-crta)");
                }
            }
        }
        /// the stop lines of every signalised approach with their group (TrafficBuilder.Signals over all arms; width from the
        /// model's painted line), for the player's red-light check (TrafficSignals.lines)
        public static List<TrafficSignals.Line> StopLines()
        {
            var models = WorldData.Models(); var sigs = Sigs();
            var (plans, stops) = TrafficBuilder.Signals(models, true);
            var res = new List<TrafficSignals.Line>(); var seen = new HashSet<string>();
            foreach (var st in stops)
            {
                string key = $"{st.model}|{st.p.x:F1}|{st.p.y:F1}"; if (!seen.Add(key)) continue;
                float w = 3.5f;
                if (sigs.TryGetValue(st.model, out var s)) foreach (var q in s.stops) if ((q.p - st.p).magnitude < .5f) w = q.w;
                var m = models.Find(mm => mm.key == st.model);
                var p = W.U(st.p.x, m != null ? m.y : 0, st.p.y); p.y = Ground(p, p.y, out _);
                res.Add(new TrafficSignals.Line { p = p, dir = new Vector3(st.dir.x, 0, -st.dir.y).normalized, hw = w / 2, planKey = plans[st.plan].key, group = st.group });
            }
            return res;
        }
        static float LaneGap(float[] lanes, int i, float span)
        {
            float g = span;
            for (int j = 0; j < lanes.Length; j++) if (j != i) g = Mathf.Min(g, Mathf.Abs(lanes[j] - lanes[i]));
            return lanes.Length > 1 ? g : Mathf.Min(span - 1, 3.2f);
        }
        static string StyleAt(string style, int i)
        {
            if (string.IsNullOrEmpty(style)) return "yellow";
            var parts = style.Split(','); return parts[Mathf.Min(i, parts.Length - 1)].Trim();
        }

        // ------------------------------------------------------------------ roadside signs from the graph / packages
        /// road clearance: segments (Unity x/z) with their half widths (GameWorld edges, estimated; model roads, measured)
        static List<(Vector2 a, Vector2 b, float hw)> roadSegs;
        static Dictionary<Vector2Int, List<int>> segGrid;
        static void IndexRoads(GameWorld gw, List<ModelJ> models)
        {
            roadSegs = new List<(Vector2, Vector2, float)>(); segGrid = new Dictionary<Vector2Int, List<int>>();
            void Add(Vector2 a, Vector2 b, float hw)
            {
                int id = roadSegs.Count; roadSegs.Add((a, b, hw));
                var mn = Vector2.Min(a, b) - Vector2.one * (hw + 4); var mx = Vector2.Max(a, b) + Vector2.one * (hw + 4);
                for (int i = Mathf.FloorToInt(mn.x / 32); i <= Mathf.FloorToInt(mx.x / 32); i++)
                    for (int j = Mathf.FloorToInt(mn.y / 32); j <= Mathf.FloorToInt(mx.y / 32); j++)
                    { var k = new Vector2Int(i, j); if (!segGrid.TryGetValue(k, out var l)) segGrid[k] = l = new List<int>(); l.Add(id); }
            }
            foreach (var e in gw.edges)
            {
                float hw = HalfW(e);
                for (int i = 0; i + 1 < e.pn; i++) { var a = gw.pts[e.p0 + i]; var b = gw.pts[e.p0 + i + 1]; Add(new Vector2(a.x, a.z), new Vector2(b.x, b.z), hw); }
            }
            foreach (var m in models)
                foreach (var r in m.roads)
                {
                    if (r.lvl != 0) continue;
                    for (int i = 0; i + 1 < r.p.Count; i++) { var a = W.U(r.p[i][0], 0, r.p[i][1]); var b = W.U(r.p[i + 1][0], 0, r.p[i + 1][1]); Add(new Vector2(a.x, a.z), new Vector2(b.x, b.z), (float)r.w / 2); }
                }
        }
        static float HalfW(GEdge e) => e.oneway ? Mathf.Max(1, e.lanes) * 3.4f / 2 + .3f : Mathf.Max(2, (int)e.lanes) * 3.1f / 2 + .3f;
        /// how far p is inside the nearest carriageway (> 0: on the road)
        static float OnRoad(Vector3 p, float margin)
        {
            var q = new Vector2(p.x, p.z); float worst = -99;
            if (!segGrid.TryGetValue(new Vector2Int(Mathf.FloorToInt(q.x / 32), Mathf.FloorToInt(q.y / 32)), out var l)) return worst;
            foreach (int id in l)
            {
                var s = roadSegs[id]; var ab = s.b - s.a; float t = Mathf.Clamp01(Vector2.Dot(q - s.a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                float d = (s.a + ab * t - q).magnitude; worst = Mathf.Max(worst, s.hw + margin - d);
            }
            return worst;
        }
        /// a roadside post position: p pushed out along `outward` until clear of every carriageway (≤ 3 m), null when it can't be
        static Vector3? Clear(Vector3 p, Vector3 outward, float margin = .5f)
        {
            outward.y = 0; outward.Normalize();
            for (float s = 0; s <= 3.01f; s += .25f) { var q = p + outward * s; if (OnRoad(q, margin) <= 0) return q; }
            return null;
        }

        static readonly List<(Vector3 p, Vector3 t, string kind)> placed = new List<(Vector3, Vector3, string)>();
        static bool Near(Vector3 p, Vector3 t, string kind, float r)
        {
            foreach (var q in placed) if (q.kind == kind && (q.p - p).sqrMagnitude < r * r && Vector3.Dot(q.t, t) > .5f) return true;
            return false;
        }
        /// one post with plates (top → bottom) at p (base on the ground), facing traffic along t
        static bool Post(Vector3 p, Vector3 t, float yHint, string kind, float dedupe, params (string face, string back, float w, float h)[] plates)
        {
            t.y = 0; t.Normalize();
            var gp = W.G(p); if (gp.x < WorldData.MIN_X || gp.x > -300 || gp.y < -7000 || gp.y > -3000) { Count("preskočeno (izvan svijeta) " + kind); return false; }
            if (Near(p, t, kind, dedupe)) { Count("preskočeno (dvostruko) " + kind); return false; }
            p.y = Ground(p, yHint, out bool ok); if (!ok) { Count("preskočeno (izvan svijeta) " + kind); return false; }
            float y = 2.2f; foreach (var pl in plates) y += pl.h + .05f;
            float top = y; Pole(p, top + .05f);
            float cy = top;
            foreach (var pl in plates) { cy -= pl.h / 2; Plate(p + Vector3.up * cy - t * .07f, t, pl.face, pl.back, pl.w, pl.h); cy -= pl.h / 2 + .05f; }
            placed.Add((p, t, kind)); Count(kind);
            var g = W.G(p); where.Add($"{kind}\t{g.x:F1}\t{g.y:F1}\t{Mathf.Atan2(t.x, t.z) * Mathf.Rad2Deg:F0}"); return true;
        }

        /// point at distance d back from the end of a polyline (Unity), its travel direction
        static (Vector3 p, Vector3 t) Back(List<Vector3> pts, float d)
        {
            for (int i = pts.Count - 1; i > 0; i--)
            {
                var a = pts[i - 1]; var b = pts[i]; float L = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
                if (d <= L || i == 1) { var t = (b - a); t.y = 0; t.Normalize(); return (Vector3.Lerp(b, a, L > 1e-3f ? Mathf.Min(1, d / L) : 0), t); }
                d -= L;
            }
            return (pts[pts.Count - 1], Vector3.forward);
        }
        static (Vector3 p, Vector3 t) Along(List<Vector3> pts, float d)
        {
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[i + 1]; float L = Vector3.Distance(new Vector3(a.x, 0, a.z), new Vector3(b.x, 0, b.z));
                if (d <= L || i + 2 == pts.Count) { var t = (b - a); t.y = 0; t.Normalize(); return (Vector3.Lerp(a, b, L > 1e-3f ? Mathf.Min(1, d / L) : 0), t); }
                d -= L;
            }
            return (pts[0], Vector3.forward);
        }

        static bool InSignalModel(Vector3 u, List<ModelJ> models, Dictionary<string, Sig> sigs, float grow = 0)
        {
            var g = W.G(u);
            foreach (var m in models)
                if (sigs.TryGetValue(m.key, out var s) && s.plan && Math.Abs(g.x - m.x) < m.half + grow && Math.Abs(g.y - m.z) < m.half + grow)
                {
                    // the square is generous: only within ~45 m of a stop line counts as the junction
                    foreach (var st in s.stops) if ((st.p - g).magnitude < 45 + grow) return true;
                }
            return false;
        }

        static void BuildApproachSigns(List<ModelJ> models, Dictionary<string, Sig> sigs)
        {
            // (1) painted stop lines of the models without a signal plan (and the tiles): STOP unless the graph says give way there
            GameDataBuilder.MergedGraph(out var nodes, out var edges, out var closed);
            var ctl = new List<(Vector3 node, Vector3 t, string c)>();
            foreach (var n in nodes.Values)
            {
                if (n.appr == null) continue;
                foreach (var kv in n.appr)
                {
                    if (kv.Value != "give_way" && kv.Value != "stop") continue;
                    if (!edges.TryGetValue(kv.Key, out var e) || e.pts == null || e.pts.Count < 2 || closed.Contains(e.id)) continue;
                    List<Vector3> pts;
                    if (e.b == n.id) pts = e.pts.Select(q => W.U(q[0], q[1], q[2])).ToList();
                    else if (e.a == n.id && !e.oneway) { pts = e.pts.Select(q => W.U(q[0], q[1], q[2])).ToList(); pts.Reverse(); }
                    else continue;
                    var nu = W.U(n.x, n.y, n.z);
                    if (n.x < WorldData.MIN_X || n.x > -300 || n.z < -7000 || n.z > -3000) continue;
                    if (InSignalModel(nu, models, sigs)) continue;
                    // the crossing road's half width: the widest other edge at the node
                    float cross = 3.5f;
                    foreach (var o in edges.Values) if ((o.a == n.id || o.b == n.id) && o.id != e.id) cross = Mathf.Max(cross, o.width > 0 ? (float)o.width / 2 : (o.oneway ? 3.5f : 3.2f));
                    float hw = e.width > 0 ? (float)e.width / 2 : e.oneway ? Mathf.Max(1, e.lanes) * 1.7f : Mathf.Max(2, e.lanes) * 1.55f;
                    if (e.oneway && e.width <= 0) hw = Mathf.Max(hw, 1.8f);
                    float lenE = 0; for (int i = 1; i < pts.Count; i++) lenE += Vector3.Distance(pts[i - 1], pts[i]);
                    float back = Mathf.Min(Mathf.Clamp(cross + 2.5f, 4, 16), lenE * .8f);
                    var (p, t) = Back(pts, back);
                    ctl.Add((nu, t, kv.Value));
                    // the model's own stop line, if one lies there, places the post at its right end
                    Vector3 basePos = p; float lat = hw + .8f;
                    var gp = W.G(p);
                    foreach (var s in sigs.Values)
                        if (!s.plan) foreach (var st in s.stops)
                            {
                                var d = DirU(st.a); if (Vector3.Dot(d, t) < .8f || (st.p - gp).magnitude > 14) continue;
                                basePos = W.U(st.p.x, p.y, st.p.y); lat = st.w / 2 + .7f; break;
                            }
                    var right = Vector3.Cross(Vector3.up, t);
                    var q2 = Clear(basePos + right * lat, right);
                    if (q2 == null) { Count("preskočeno (na cesti) " + kv.Value); continue; }
                    if (kv.Value == "stop") Post(q2.Value, t, p.y, "STOP", 12, (A.Stop(), A.Back("oct"), .7f, .7f));
                    else Post(q2.Value, t, p.y, "trokut (prednost prolaska)", 12, (A.GiveWay(), A.Back("tri"), .9f, .9f));
                }
            }
            // (2) the unsignalised models' painted stop lines the graph has no approach control for
            foreach (var m in models)
            {
                if (!sigs.TryGetValue(m.key, out var s) || s.plan) continue;
                foreach (var st in s.stops)
                {
                    var t = DirU(st.a); var right = Vector3.Cross(Vector3.up, t);
                    var c = W.U(st.p.x, m.y, st.p.y);
                    string kind = "STOP";
                    foreach (var k in ctl) if ((k.node - c).magnitude < 40 && Vector3.Dot(k.t, t) > .8f) { kind = k.c == "stop" ? "STOP" : "trokut (prednost prolaska)"; break; }
                    var q2 = Clear(c + right * (st.w / 2 + .7f), right);
                    if (q2 == null) { Count("preskočeno (na cesti) crta zaustavljanja"); continue; }
                    if (kind == "STOP") Post(q2.Value, t, (float)m.y, "STOP", 12, (A.Stop(), A.Back("oct"), .7f, .7f));
                    else Post(q2.Value, t, (float)m.y, kind, 12, (A.GiveWay(), A.Back("tri"), .9f, .9f));
                }
            }
        }

        /// zebras of the unsignalised junction models and the road tiles (owner's krizanja.json / dionice.json, model frame):
        /// a pedestrian-crossing sign on each kerb, facing each travel direction
        static void BuildCrossingSigns(List<ModelJ> models, Dictionary<string, Sig> sigs)
        {
            var files = new[] { "/Users/macair/Documents/mag-11-19/krizanja3d/krizanja.json", "/Users/macair/Documents/mag-11-20/dionice/dionice.json" };
            var byGlb = new Dictionary<string, ModelJ>();
            foreach (var m in models) byGlb[Path.GetFileNameWithoutExtension(m.glb)] = m;
            foreach (var f in files)
            {
                if (!File.Exists(f)) { log.Add("nema " + f); continue; }
                var root = MiniJson.O(MiniJson.Parse(File.ReadAllText(f)));
                foreach (var jo in MiniJson.L(MiniJson.G(root, "junctions") ?? MiniJson.G(root, "tiles")))
                {
                    var j = MiniJson.O(jo); string file = j["file"] as string;
                    if (file == null || !byGlb.TryGetValue(file, out var m)) continue;
                    if (sigs.TryGetValue(m.key, out var s) && s.plan) continue; // signalised: the owner's props carry the plates
                    foreach (var oo in MiniJson.L(MiniJson.G(j, "objs")) ?? new List<object>())
                    {
                        var o = MiniJson.O(oo); if ((string)o["k"] != "zebra") continue;
                        double lx = MiniJson.D(o["x"]), lz = MiniJson.D(o["z"]);
                        if (m.lin != null) { double nx = m.lin[0] * lx + m.lin[1] * lz, nz = m.lin[2] * lx + m.lin[3] * lz; lx = nx; lz = nz; }
                        var c = W.U(m.x + lx, m.y, m.z + lz); float a = MiniJson.F(o["a"]), w = MiniJson.F(o["w"]), l = MiniJson.G(o, "l") is object lo ? MiniJson.F(lo) : 4;
                        // a = the road's direction; the zebra spans w across it
                        var t = DirU(a); var right = Vector3.Cross(Vector3.up, t);
                        foreach (int sg in new[] { 1, -1 })
                        {
                            var tt = t * sg; var rr = right * sg;
                            var p = c + rr * (w / 2 + .6f) - tt * (l / 2 + .4f);
                            var q2 = Clear(p, rr); if (q2 == null) { Count("preskočeno (na cesti) pješački"); continue; }
                            Post(q2.Value, tt, (float)m.y, "pješački prijelaz", 6, (A.Pedestrian(), A.Back("square"), .6f, .6f));
                        }
                    }
                }
            }
        }

        static void BuildLimits(GameWorld gw, List<ModelJ> models, Dictionary<string, Sig> sigs)
        {
            int nn = gw.nodes.Length;
            var inc = new List<int>[nn]; for (int i = 0; i < nn; i++) inc[i] = new List<int>();
            for (int i = 0; i < gw.edges.Length; i++) { inc[gw.edges[i].a].Add(i); inc[gw.edges[i].b].Add(i); }
            List<Vector3> Pts(GEdge e, bool fwd) { var l = new List<Vector3>(); for (int i = 0; i < e.pn; i++) l.Add(gw.pts[e.p0 + i]); if (!fwd) l.Reverse(); return l; }
            foreach (var e in gw.edges)
            {
                if (e.closed || e.limit == 0 || e.pn < 2) continue;
                foreach (bool fwd in e.oneway ? new[] { true } : new[] { true, false })
                {
                    var pts = Pts(e, fwd); int start = fwd ? e.a : e.b;
                    var (p0, t0) = Along(pts, 0);
                    // the edge that carries the traffic into `start` most nearly straight on
                    GEdge pred = null; float bestDot = .8f;
                    foreach (int oi in inc[start])
                    {
                        var o = gw.edges[oi]; if (o == e || o.closed) continue;
                        bool into = o.b == start || (!o.oneway && o.a == start); if (!into) continue;
                        var op = Pts(o, o.b == start); var (_, ot) = Back(op, 0);
                        float d = Vector3.Dot(ot, t0); if (d > bestDot) { bestDot = d; pred = o; }
                    }
                    if (pred == null) continue;
                    bool d8 = e.road == "D8";
                    bool repeat = d8 && pred.limit == e.limit && InSignalModel(gw.nodes[start], models, sigs, 10) && e.limit >= 60;
                    if (pred.limit == e.limit && !repeat) continue;
                    // (20 km/h: the delivery spurs' default, no sign)
                    if (e.limit < 30) continue;
                    float len = 0; for (int i = 1; i < pts.Count; i++) len += Vector3.Distance(pts[i - 1], pts[i]);
                    if (len < 20) continue;
                    var (p, t) = Along(pts, Mathf.Min(repeat ? 40 : 14, len * .5f));
                    var right = Vector3.Cross(Vector3.up, t);
                    var q2 = Clear(p + right * (HalfW(e) + .9f), right);
                    if (q2 == null) { Count("preskočeno (na cesti) ograničenje"); continue; }
                    float sz = d8 ? .8f : .6f;
                    Post(q2.Value, t, p.y, "ograničenje " + e.limit, 60, (A.Limit(e.limit), A.Back("round"), sz, sz));
                }
            }
        }

        static void BuildPlaceBoards(GameWorld gw)
        {
            var done = new List<(Vector3 p, string a, string b)>();
            foreach (var e in gw.edges)
            {
                if (e.closed || e.pn < 2) continue;
                bool main = e.road == "D8" || (e.road ?? "").StartsWith("Ž") || (e.name ?? "").Contains("Tuđmana");
                if (!main) continue;
                for (int i = 0; i + 1 < e.pn; i++)
                {
                    var a = gw.pts[e.p0 + i]; var b = gw.pts[e.p0 + i + 1];
                    string pa = gw.PlaceAt(a), pb = gw.PlaceAt(b);
                    if (pa == pb || string.IsNullOrEmpty(pa) || string.IsNullOrEmpty(pb)) continue;
                    // the boundary between a and b (place grid: 50 m cells): bisect
                    Vector3 lo = a, hi = b;
                    for (int k = 0; k < 8; k++) { var m = (lo + hi) / 2; if (gw.PlaceAt(m) == pa) lo = m; else hi = m; }
                    var c = (lo + hi) / 2; var t = b - a; t.y = 0; t.Normalize();
                    foreach (int sg in e.oneway ? new[] { 1 } : new[] { 1, -1 })
                    {
                        var tt = t * sg; string into = sg > 0 ? pb : pa, from = sg > 0 ? pa : pb;
                        if (done.Exists(d => (d.p - c).magnitude < 250 && d.a == from && d.b == into)) continue;
                        done.Add((c, from, into));
                        var right = Vector3.Cross(Vector3.up, tt);
                        var q2 = Clear(c + right * (HalfW(e) + 1.2f), right);
                        if (q2 == null) { Count("preskočeno (na cesti) naziv mjesta"); continue; }
                        Post(q2.Value, tt, c.y, "naziv mjesta", 80, (A.Town(into, false), A.Town(into, true), 1.65f, .59f));
                    }
                }
            }
        }

        static void BuildStreetPlates(GameWorld gw)
        {
            int nn = gw.nodes.Length;
            var inc = new List<int>[nn]; for (int i = 0; i < nn; i++) inc[i] = new List<int>();
            for (int i = 0; i < gw.edges.Length; i++) { inc[gw.edges[i].a].Add(i); inc[gw.edges[i].b].Add(i); }
            var shown = new List<(Vector3 p, string name)>();
            for (int ni = 0; ni < nn; ni++)
            {
                if (inc[ni].Count < 3) continue;
                var names = inc[ni].Select(i => gw.edges[i].name ?? "").ToList();
                if (names.Distinct().Count() < 2) continue;
                foreach (int ei in inc[ni])
                {
                    var e = gw.edges[ei]; string nm = e.name ?? "";
                    if (nm.Length < 3 || e.road == "D8" || nm.StartsWith("D8") || nm.StartsWith("Cesta pape") || e.closed || e.pn < 2) continue;
                    var node = gw.nodes[ni];
                    if (shown.Exists(s => s.name == nm && (s.p - node).magnitude < 180)) continue;
                    var pts = new List<Vector3>(); for (int i = 0; i < e.pn; i++) pts.Add(gw.pts[e.p0 + i]); if (e.b == ni) pts.Reverse();
                    float cross = 0; foreach (int oi in inc[ni]) if (oi != ei) cross = Mathf.Max(cross, HalfW(gw.edges[oi]));
                    float len = 0; for (int i = 1; i < pts.Count; i++) len += Vector3.Distance(pts[i - 1], pts[i]);
                    if (len < cross + 4) continue;
                    var (p, t) = Along(pts, cross + 2.5f); // t: out of the junction
                    var right = Vector3.Cross(Vector3.up, t);
                    var q2 = Clear(p + right * (HalfW(e) + .9f), right);
                    if (q2 == null) { Count("preskočeno (na cesti) ulica"); continue; }
                    // the plate stands along the street, readable from the junction road: faces perpendicular to the street
                    if (Post(q2.Value, right, p.y, "ploča s imenom ulice", 25, (A.Street(nm), A.Street(nm), .95f, .24f))) shown.Add((node, nm));
                }
            }
        }

        static void BuildRoadSigns(GameWorld gw)
        {
            var rs = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "road-signs.json")));
            var raw = MiniJson.O(MiniJson.Parse(File.ReadAllText(W.REGION + "graph.json")));
            var rawE = new Dictionary<long, List<object>>();
            foreach (var eo in MiniJson.L(raw["edges"])) { var e = MiniJson.O(eo); rawE[(long)MiniJson.D(e["id"])] = MiniJson.L(e["points"]); }
            var stacks = new Dictionary<string, List<Dictionary<string, object>>>(); var order = new List<string>();
            foreach (var so in MiniJson.L(rs["signs"]))
            {
                var s = MiniJson.O(so); double x = MiniJson.D(s["x"]); if (x < WorldData.MIN_X || x > -400) continue;
                string k = MiniJson.G(s, "stackKey") as string ?? (string)s["id"];
                if (!stacks.TryGetValue(k, out var l)) { stacks[k] = l = new List<Dictionary<string, object>>(); order.Add(k); }
                l.Add(s);
            }
            foreach (var k in order)
            {
                var l = stacks[k]; var s0 = l[0];
                // travel direction: the web edge's tangent at s, times dir
                Vector3 t = Vector3.forward;
                if (rawE.TryGetValue((long)MiniJson.D(s0["edge"]), out var pl))
                {
                    var P = pl.Select(q => MiniJson.L(q)).Select(q => W.U(MiniJson.D(q[0]), MiniJson.D(q[1]), MiniJson.D(q[2]))).ToList();
                    var (_, tt) = Along(P, MiniJson.F(s0["s"])); t = tt * (MiniJson.D(s0["dir"]) < 0 ? -1 : 1);
                }
                var c = W.U(MiniJson.D(s0["x"]), 0, MiniJson.D(s0["z"]));
                // on the nearest road running this way: y and the road's edge
                float bd = 30, y = 0, hw = 3.5f; Vector3 axis = c;
                foreach (var e in gw.edges)
                    for (int i = 0; i + 1 < e.pn; i++)
                    {
                        var a = gw.pts[e.p0 + i]; var b = gw.pts[e.p0 + i + 1]; var ab = b - a; ab.y = 0; if (ab.sqrMagnitude < 1e-4f) continue;
                        float dd = Mathf.Abs(Vector3.Dot(ab.normalized, t)); if (dd < .7f) continue;
                        float tt = Mathf.Clamp01(Vector3.Dot(new Vector3(c.x - a.x, 0, c.z - a.z), ab) / ab.sqrMagnitude); var q = Vector3.Lerp(a, b, tt);
                        float d = new Vector2(q.x - c.x, q.z - c.z).magnitude; if (d < bd) { bd = d; y = q.y; hw = HalfW(e); axis = q; }
                    }
                if (bd >= 30) { Count("preskočeno (nema ceste) putokaz"); continue; }
                var right = Vector3.Cross(Vector3.up, t);
                var q2 = Clear(axis + right * (hw + 1.6f), right);
                if (q2 == null) { Count("preskočeno (na cesti) putokaz"); continue; }
                var plates = new List<(string, string, float, float)>();
                foreach (var s in l)
                {
                    string kind = (string)s["kind"], text = (string)s["text"], style = MiniJson.G(s, "style") as string ?? "yellow";
                    if (kind == "direction")
                    {
                        var lines = text.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                        float h = .3f + .32f * lines.Length;
                        plates.Add((A.Board(string.Join(" / ", lines), style == "prohibition" ? "white" : style, 2.2f, h), A.Back("rect"), 2.2f, h));
                    }
                    else plates.Add((A.Disc(text), A.Back("round"), .7f, .7f));
                }
                Post(q2.Value, t, y, MiniJson.G(s0, "kind") as string == "direction" ? "putokaz (paket)" : "ograničenje mase (paket)", 4, plates.ToArray());
            }
        }

        // ------------------------------------------------------------------ build
        public static Material SignMaterial(Texture2D tex)
        {
            string p = SignAtlas.DIR + "/SignAtlas.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
            m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_AlphaClip", 1); m.SetFloat("_Cutoff", .5f); m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Smoothness", .3f); m.SetFloat("_Metallic", 0); m.SetFloat("_Cull", 2);
            m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
        }

        /// builds everything under World/Signals (heads, TrafficSignals) and World/Signs; returns a summary line
        public static string Build(TrafficSystem ts, Material lensOff, out List<TrafficSignals.Head> outHeads, Transform signalsRoot)
        {
            counts.Clear(); log.Clear(); placed.Clear();
            chunks = new Dictionary<Vector2Int, Chunk>(); cols = new List<(Vector3, Quaternion, Vector3)>();
            heads = new List<TrafficSignals.Head>();
            if (!AssetDatabase.IsValidFolder(SignAtlas.DIR)) AssetDatabase.CreateFolder(GEN, "Signs");
            if (!AssetDatabase.IsValidFolder(SignAtlas.DIR + "/Meshes")) AssetDatabase.CreateFolder(SignAtlas.DIR, "Meshes");
            lens = LensMesh();
            A = new SignAtlas { font = SignFont.Load() };
            var models = WorldData.Models();
            var sigs = Sigs();
            var (_, groupStops) = TrafficBuilder.Signals(models, true);
            var gw = AssetDatabase.LoadAssetAtPath<GameWorld>("Assets/Generated/Game/Resources/" + GameWorld.ResourcePath + ".asset");
            if (!gw) { outHeads = new List<TrafficSignals.Head>(); return "no GameWorld: run Magistrala/Igra: podaci first"; }
            var root = WorldBuilder.Root("Signs"); // (the old signs and their colliders go before the ground is probed)
            IndexRoads(gw, models);

            // pass 1 (dry): every face gets painted (A.uv filled by a provisional pack) — pass 2 builds the geometry with the real uv
            for (int pass = 0; pass < 2; pass++)
            {
                chunks.Clear(); cols.Clear(); heads.Clear(); placed.Clear(); counts.Clear(); log.Clear(); where.Clear();
                A.Solid("steel", SignAtlas.Steel); A.Solid("black", SignAtlas.Dark); A.Solid("white", SignAtlas.White); A.BollardStripes();
                A.Solid("lens", SignAtlas.Hex("121212"));
                rSteel = R("solid:steel"); rBlack = R("solid:black"); rWhite = R("solid:white"); rBollard = R("bollard"); rLens = R("solid:lens");
                BuildSignals(models, sigs, groupStops);
                FallbackHeads(ts.data);
                BuildApproachSigns(models, sigs);
                BuildCrossingSigns(models, sigs);
                BuildLimits(gw, models, sigs);
                BuildPlaceBoards(gw);
                BuildStreetPlates(gw);
                BuildRoadSigns(gw);
                if (pass == 0) { var tex = A.Save(); SignMaterial(tex); }
            }
            var mat = SignMaterial(AssetDatabase.LoadAssetAtPath<Texture2D>(SignAtlas.PNG));

            // meshes per chunk
            foreach (var old in AssetDatabase.FindAssets("t:Mesh", new[] { SignAtlas.DIR + "/Meshes" })) AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(old));
            int tris = 0;
            foreach (var kv in chunks)
            {
                var c = kv.Value; if (c.t.Count == 0) continue;
                var mesh = new Mesh { name = $"signs_{kv.Key.x}_{kv.Key.y}", indexFormat = c.v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                mesh.SetVertices(c.v); mesh.SetNormals(c.n); mesh.SetUVs(0, c.uv); mesh.SetTriangles(c.t, 0); mesh.RecalculateBounds(); mesh.RecalculateTangents();
                AssetDatabase.CreateAsset(mesh, $"{SignAtlas.DIR}/Meshes/{mesh.name}.asset");
                var go = new GameObject(mesh.name); go.transform.SetParent(root.transform, false); go.isStatic = true;
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
                tris += c.t.Count / 3;
            }
            var colRoot = new GameObject("colliders"); colRoot.transform.SetParent(root.transform, false); colRoot.isStatic = true;
            foreach (var (c, q, s) in cols)
            {
                var go = new GameObject("post"); go.transform.SetParent(colRoot.transform, false); go.transform.SetPositionAndRotation(c, q); go.isStatic = true;
                go.AddComponent<BoxCollider>().size = s;
            }
            outHeads = new List<TrafficSignals.Head>(heads);
            var sb = new System.Text.StringBuilder($"[znakovi] {chunks.Count} komada mreže, {tris} trokuta, {cols.Count} sudarača, {heads.Count} glava semafora, atlas {A.uv.Count} lica\n");
            foreach (var kv in counts.OrderBy(k => k.Key)) sb.Append($"  {kv.Key}: {kv.Value}\n");
            foreach (var l in log.Take(60)) sb.Append("  ! ").Append(l).Append('\n');
            File.WriteAllText("Logs/znakovi-izvjestaj.txt", sb.ToString());
            File.WriteAllText("Logs/znakovi-polozaji.tsv", "vrsta\tx\tz\tsmjer\n" + string.Join("\n", where));
            Debug.Log(sb.ToString());
            return sb.ToString();
        }
    }
}
