// The firm truck's tail-lift unload (web: game/firmUnload.ts pose, TruckModel.tsx door panels / lift, PlayerTruck.tsx pallet rows),
// extended to a row-by-row sequence: the roll-up door goes up, the tail lift unfolds level at bed height, then for each pallet row
// (rear first) the pallet truck pulls the row onto the platform, the lift lowers it to the ground, the row is pushed off behind the
// truck (it stays there, in the world) and the lift comes back up. Then the lift folds and the door closes.
// Multi-stop tours (mag-11-19, MagistralaGame.Tours.cs): Begin(n) unloads only the next n rows still in the box (rear first);
// an abort brings back only that stop's rows.
// Nodes of firm_truck.glb (atego.py): door_roll/door_roll_0..6, tail_lift (stowed: rotation.x −90°), load_pallets/load_pallets_0..n.
// Model space = the GLB's (glTFast mirrors x; y and z as in the web, the box rear at +z).
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.Game
{
    public class FirmUnload : MonoBehaviour
    {
        // web constants (TruckModel.tsx): door panel centres rise to ROLL_TOP, then run forward under the roof
        const float RollTop = -0.06f, RollTravel = 2.05f, LiftDrop = 0.9f;
        const float TDoor = 1.25f, TFold = 1.25f, TPull = 0.9f, TDown = 1.0f, TPush = 0.7f, TUp = 0.9f, PushBack = 1.5f;
        const float TRow = TPull + TDown + TPush + TUp;

        struct Panel { public Transform t; public float y0; }
        readonly List<Panel> panels = new List<Panel>();
        Transform lift; Vector3 liftP0;
        Transform[] rows = new Transform[0];
        float[] rowZ, rowBottom;
        float plateZ, plateTop;
        readonly List<GameObject> dropped = new List<GameObject>();

        /// sequence time (s), −1 idle; while closing after an abort the pose eases back to stowed
        public float T { get; private set; } = -1;
        public bool Running => T >= 0;
        public bool Ok => lift != null && panels.Count > 0;
        public float Duration => TDoor + TFold + (seq ?? AllRows()).Length * TRow + TFold + TDoor;
        public int RowsOff { get; private set; }
        public int Rows => rows.Length;
        /// rows of the running (or last) unload
        public int SeqRows => (seq ?? AllRows()).Length;
        /// rows still in the box
        public int RowsIn { get { int n = 0; foreach (var r in rows) if (r.gameObject.activeSelf) n++; return n; } }
        int[] seq; // the rows of this unload, in order (indices into rows)
        int[] AllRows() { var a = new int[rows.Length]; for (int i = 0; i < a.Length; i++) a[i] = i; return a; }
        float abortT = -1; float door0, angle0, drop0;
        float door, angle = -90, drop;

        public static FirmUnload Attach(Transform truck)
        {
            Transform liftT = null;
            foreach (var t in truck.GetComponentsInChildren<Transform>(true)) if (t.name == "tail_lift") { liftT = t; break; }
            if (!liftT) return null;
            var u = liftT.parent.gameObject.GetComponent<FirmUnload>() ?? liftT.parent.gameObject.AddComponent<FirmUnload>();
            u.Init(liftT);
            return u;
        }

        void Init(Transform liftT)
        {
            lift = liftT; liftP0 = lift.localPosition;
            var model = lift.parent;
            var door = model.Find("door_roll");
            panels.Clear();
            if (door) foreach (Transform c in door) if (c.name.StartsWith("door_roll_")) panels.Add(new Panel { t = c, y0 = c.localPosition.y });
            var load = model.Find("load_pallets");
            var rl = new List<Transform>();
            if (load) foreach (Transform c in load) if (c.name.StartsWith("load_pallets_")) rl.Add(c);
            rl.Sort((a, b) => string.CompareOrdinal(a.name, b.name)); // _0 = rear row, unloaded first
            rows = rl.ToArray();
            rowZ = new float[rows.Length]; rowBottom = new float[rows.Length];
            // measured in the model frame: row centre z / bottom, the platform when level (pivot + plate offset, rotation 0)
            for (int i = 0; i < rows.Length; i++)
            {
                var b = ModelBounds(model, rows[i]);
                rowZ[i] = b.center.z; rowBottom[i] = b.size == Vector3.zero ? 0.13f : b.min.y;
            }
            var plate = lift.Find("tail_lift_plate");
            Vector3 po = plate ? plate.localPosition : new Vector3(0, -0.025f, 0.675f);
            plateZ = liftP0.z + po.z; plateTop = liftP0.y + po.y + 0.03f;
            Apply();
        }

        /// bounds of a node's meshes in the model frame (mesh bounds: also for inactive rows)
        static Bounds ModelBounds(Transform model, Transform node)
        {
            var b = new Bounds(); bool any = false; var inv = model.worldToLocalMatrix;
            foreach (var mf in node.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var mb = mf.sharedMesh.bounds; var m = inv * mf.transform.localToWorldMatrix;
                for (int c = 0; c < 8; c++)
                {
                    var p = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1)));
                    if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                }
            }
            return b;
        }

        static float Ramp(float t, float a, float b) { float u = Mathf.Clamp01((t - a) / (b - a)); return u * u * (3 - 2 * u); }
        float RowStart(int k) => TDoor + TFold + k * TRow;

        public void SetLoaded(bool on)
        {
            ClearDropped();
            for (int i = 0; i < rows.Length; i++) { rows[i].gameObject.SetActive(on); rows[i].localPosition = Vector3.zero; }
            RowsOff = 0; seq = null;
        }

        /// a resumed tour: the first `off` rows (rear first) already delivered
        public void SetDelivered(int off) { for (int i = 0; i < rows.Length; i++) rows[i].gameObject.SetActive(i >= off); }

        /// unload the next `count` rows still in the box (−1 = all of them)
        public void Begin(int count = -1)
        {
            var l = new List<int>();
            for (int i = 0; i < rows.Length && (count < 0 || l.Count < count); i++) if (rows[i].gameObject.activeSelf) l.Add(i);
            seq = l.ToArray(); T = 0; abortT = -1; RowsOff = 0;
        }

        /// the truck moved off: ease the lift / door back to stowed, the rows come back into the box (web: an aborted unload's row returns)
        public void Abort()
        {
            if (T < 0) return;
            T = -1; abortT = 0; door0 = door; angle0 = angle; drop0 = drop;
            ClearDropped();
            foreach (int i in seq ?? AllRows()) { rows[i].gameObject.SetActive(true); rows[i].localPosition = Vector3.zero; }
            RowsOff = 0;
        }

        /// pallets left at the stop: gone once the truck is far away (or at the next load)
        public void ClearDropped() { foreach (var g in dropped) if (g) Destroy(g); dropped.Clear(); }

        void Update()
        {
            if (dropped.Count > 0 && dropped[0] && Vector3.Distance(dropped[0].transform.position, transform.position) > 120) ClearDropped();
            if (abortT >= 0)
            {
                abortT += Time.deltaTime; float u = Ramp(abortT, 0, 1.2f);
                door = Mathf.Lerp(door0, 0, Ramp(abortT, 0.6f, 1.2f)); angle = Mathf.Lerp(angle0, -90, Ramp(abortT, 0.2f, 0.9f)); drop = Mathf.Lerp(drop0, 0, Ramp(abortT, 0, 0.5f));
                if (u >= 1) { abortT = -1; door = 0; angle = -90; drop = 0; }
                Apply(); return;
            }
            if (T < 0) return;
            T += Time.deltaTime;
            float D = Duration, t = T;
            door = Ramp(t, 0, TDoor) * (1 - Ramp(t, D - TDoor, D));
            angle = -90 * (1 - Ramp(t, TDoor, TDoor + TFold) * (1 - Ramp(t, D - TDoor - TFold, D - TDoor)));
            drop = 0;
            var sq = seq ?? AllRows();
            for (int q = 0; q < sq.Length; q++)
            {
                int k = sq[q]; float s = RowStart(q);
                float dk = LiftDrop * Ramp(t, s + TPull, s + TPull + TDown) * (1 - Ramp(t, s + TPull + TDown + TPush, s + TRow));
                drop += dk;
                var row = rows[k];
                if (!row.gameObject.activeSelf) continue;
                if (t < s) { row.localPosition = Vector3.zero; continue; }
                // onto the platform (and down to its top at the end of the pull), down with the lift, pushed off behind the truck
                float dz = Ramp(t, s, s + TPull) * (plateZ - rowZ[k]) + Ramp(t, s + TPull + TDown, s + TPull + TDown + TPush) * (PushBack + q * 1.35f); // rows in a line behind the truck
                float dy = Ramp(t, s + TPull * 0.6f, s + TPull) * (plateTop - rowBottom[k]) - LiftDrop * Ramp(t, s + TPull, s + TPull + TDown);
                row.localPosition = new Vector3(0, dy, dz);
                if (t >= s + TPull + TDown + TPush) Drop(k);
            }
            Apply();
            if (T >= D) { T = -1; door = 0; angle = -90; drop = 0; Apply(); }
        }

        /// the row stays at the stop: a world copy settled on the ground, the truck's row hidden
        void Drop(int k)
        {
            var row = rows[k];
            var copy = Instantiate(row.gameObject, row.position, row.rotation);
            copy.name = "isporuceno_" + row.name;
            copy.transform.localScale = row.lossyScale;
            // settle on whatever is below (yard, road), ignoring the truck
            // rowBottom is already in the model frame; load_pallets has a -0.93 m
            // offset. Applying its transform again left delivered pallets floating 0.93 m.
            Vector3 c = row.position; float bottomWorld = lift.parent.TransformPoint(new Vector3(0, rowBottom[k] + row.localPosition.y, 0)).y;
            var hits = Physics.RaycastAll(new Vector3(c.x, bottomWorld + 1.5f, c.z), Vector3.down, 4, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
                if (!h.transform.IsChildOf(transform.root)) { copy.transform.position += Vector3.up * (h.point.y - bottomWorld); break; }
            dropped.Add(copy);
            row.gameObject.SetActive(false); row.localPosition = Vector3.zero;
            RowsOff++;
        }

        void Apply()
        {
            float s = door * RollTravel;
            foreach (var p in panels)
            {
                float u = p.y0 + s;
                if (u > RollTop) { p.t.localPosition = new Vector3(0, RollTop, -(u - RollTop)); p.t.localRotation = Quaternion.Euler(90, 0, 0); }
                else { p.t.localPosition = new Vector3(0, u, 0); p.t.localRotation = Quaternion.identity; }
            }
            if (lift) { lift.localRotation = Quaternion.Euler(angle, 0, 0); lift.localPosition = liftP0 - new Vector3(0, drop, 0); }
        }
    }
}
