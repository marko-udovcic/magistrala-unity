// The colliders of the data-only systems (streaming phase 2): parked cars (one box each), props (box / capsule from their prefab,
// the horseman's two boxes), the street-lamp poles (capsules) are shapes in the cell blobs (COLL); a pool of static collider
// GameObjects follows the player: every RefreshSeconds (or after a jump) the shapes within Radius of the driven truck and of the
// camera get a pooled collider, the ones left behind give theirs back. Same size, pose, layer, material and name as the builder's
// collider, so the truck hits a parked car, pallet or lamp exactly as before. The pool grows to the most ever needed at once
// (a big car park: ~200) and is then reused; nothing allocated per frame.
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.Cells
{
    [DefaultExecutionOrder(-250)]
    public class CellColliders : MonoBehaviour, ICellLayer
    {
        public float Radius = 110, RefreshSeconds = .2f, JumpDistance = 25;
        public int Count { get; private set; }
        public int Active { get; private set; }
        public int Pooled => boxes.Count + capsules.Count + spheres.Count;

        struct Shape { public byte type; public bool trigger; public int layer, mat, name; public Vector3 pos, scale, centre, size; public Quaternion rot; }
        sealed class Page { public Shape[] s; public Collider[] used; public Bounds b; public int act; }
        readonly List<Page> pages = new List<Page>();
        readonly Stack<BoxCollider> boxes = new Stack<BoxCollider>();
        readonly Stack<CapsuleCollider> capsules = new Stack<CapsuleCollider>();
        readonly Stack<SphereCollider> spheres = new Stack<SphereCollider>();
        string[] names; PhysicsMaterial[] mats;
        float nextRefresh; Vector3 lastA, lastB;
        readonly System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();

        /// systems whose colliders come from here (the others' are still the scene's: CellData.mask)
        int sysMask = ~0;
        public void Prepare(CellDataManifest m) { names = m.colliderNames; mats = m.colliderMats; if (CellData.I) sysMask = CellData.I.mask; }

        public object Decode(CellSections s)
        {
            int o = s.Find(CellBlob.COLL, out int n); if (o < 0 || n == 0) return null;
            int keep = 0;
            for (int k = 0; k < n; k++) if ((sysMask >> (CellBlob.I(s.b, o + k * CellBlob.COLL_B + 8) >> 8) & 1) != 0) keep++;
            if (keep == 0) return null;
            var pg = new Page { s = new Shape[keep], used = new Collider[keep] }; int w = 0;
            for (int q = 0; q < n; q++)
            {
                int r = o + q * CellBlob.COLL_B; var b = s.b;
                if ((sysMask >> (CellBlob.I(b, r + 8) >> 8) & 1) == 0) continue;
                int k = w++;
                pg.s[k] = new Shape
                {
                    type = (byte)CellBlob.I(b, r), layer = CellBlob.I(b, r + 4), trigger = (CellBlob.I(b, r + 8) & 1) != 0, mat = CellBlob.I(b, r + 12), name = CellBlob.I(b, r + 16),
                    pos = CellBlob.V3(b, r + 20), rot = new Quaternion(CellBlob.F(b, r + 32), CellBlob.F(b, r + 36), CellBlob.F(b, r + 40), CellBlob.F(b, r + 44)),
                    scale = CellBlob.V3(b, r + 48), centre = CellBlob.V3(b, r + 60), size = CellBlob.V3(b, r + 72),
                };
                var bb = new Bounds(pg.s[k].pos, Vector3.one * 2);
                if (k == 0) pg.b = bb; else pg.b.Encapsulate(bb);
            }
            pg.b.Expand(20); // (the shapes' own size: a lamp's pole, a long car)
            return pg;
        }
        public void Attach(Vector2Int cell, object page) { var pg = (Page)page; pages.Add(pg); Count += pg.s.Length; nextRefresh = 0; }
        public void Detach(Vector2Int cell, object page)
        {
            var pg = (Page)page; if (!pages.Remove(pg)) return;
            Count -= pg.s.Length;
            for (int i = 0; i < pg.used.Length; i++) if (pg.used[i]) { Free(pg.s[i].type, pg.used[i]); pg.used[i] = null; Active--; }
            pg.act = 0;
        }
        public void Release(object page) { }

        TruckController truck; float nextTruck;
        Vector3 Focus(out Vector3 cam)
        {
            var c = Camera.main; cam = c ? c.transform.position : Vector3.zero;
            // the driven truck (it changes when Marko switches vehicles: looked up twice a second, FindFirstObjectByType only without a game)
            if (Time.unscaledTime >= nextTruck) { nextTruck = Time.unscaledTime + .5f; truck = Magistrala.Game.MagistralaGame.DrivenTruck(); }
            return truck ? truck.transform.position : cam;
        }

        void Update()
        {
            if (pages.Count == 0) return;
            var a = Focus(out var b);
            bool jump = (a - lastA).sqrMagnitude > JumpDistance * JumpDistance || (b - lastB).sqrMagnitude > JumpDistance * JumpDistance;
            if (!jump && Time.unscaledTime < nextRefresh) return;
            sw.Restart();
            nextRefresh = Time.unscaledTime + RefreshSeconds; lastA = a; lastB = b;
            float r2 = Radius * Radius;
            for (int pi = 0; pi < pages.Count; pi++)
            {
                var pg = pages[pi];
                bool near = pg.b.SqrDistance(a) < r2 || pg.b.SqrDistance(b) < r2;
                if (!near && pg.act == 0) continue;
                var S = pg.s;
                for (int i = 0; i < S.Length; i++)
                {
                    bool want = near && ((S[i].pos - a).sqrMagnitude < r2 || (S[i].pos - b).sqrMagnitude < r2);
                    var u = pg.used[i];
                    if (want == (u != null)) continue;
                    if (!want) { Free(S[i].type, u); pg.used[i] = null; Active--; pg.act--; continue; }
                    pg.used[i] = Take(ref S[i]); Active++; pg.act++;
                }
            }
            sw.Stop(); CellStats.Add(CellStats.COLLIDERS, (float)sw.Elapsed.TotalMilliseconds);
        }

        Collider Take(ref Shape s)
        {
            Collider c;
            switch (s.type)
            {
                case 1:
                    {
                        var cc = capsules.Count > 0 ? capsules.Pop() : New<CapsuleCollider>();
                        cc.center = s.centre; cc.radius = s.size.x; cc.height = s.size.y; cc.direction = (int)s.size.z; c = cc; break;
                    }
                case 2:
                    {
                        var sc = spheres.Count > 0 ? spheres.Pop() : New<SphereCollider>();
                        sc.center = s.centre; sc.radius = s.size.x; c = sc; break;
                    }
                default:
                    {
                        var bc = boxes.Count > 0 ? boxes.Pop() : New<BoxCollider>();
                        bc.center = s.centre; bc.size = s.size; c = bc; break;
                    }
            }
            var go = c.gameObject; var t = c.transform;
            t.SetPositionAndRotation(s.pos, s.rot); t.localScale = s.scale;
            if (go.layer != s.layer) go.layer = s.layer;
            c.isTrigger = s.trigger;
            c.sharedMaterial = mats != null && s.mat >= 0 && s.mat < mats.Length ? mats[s.mat] : null;
            if (names != null && s.name >= 0 && s.name < names.Length) go.name = names[s.name];
            c.enabled = true;
            return c;
        }

        T New<T>() where T : Collider
        {
            var go = new GameObject("collider"); go.transform.SetParent(transform, false);
            var c = go.AddComponent<T>(); c.enabled = false; return c;
        }

        void Free(byte type, Collider c)
        {
            c.enabled = false;
            if (type == 1) capsules.Push((CapsuleCollider)c); else if (type == 2) spheres.Push((SphereCollider)c); else boxes.Push((BoxCollider)c);
        }
    }
}
