// The per-cell blob (Assets/Generated/Cells/Data/c_<x>_<z>.bytes, one per CellGrid cell that has anything): little-endian,
//   header   u32 magic "MGC1", i32 version, i32 cell x, i32 cell z, i32 sections
//   section  u32 id, i32 records, i32 bytes, then the records (fixed size per id, below)
// Records (floats unless said; positions Unity world):
//   TREE  24 B  x y z yaw scale proto            (exactly trees.bytes' record: whatever the tree builders write)
//   PROP  52 B  i32 proto, 3x4 matrix row-major  (CellProps; manifest.props[proto])
//   PARK  40 B  x y z yaw, i32 kind, i32 paint, tint rgba (ParkedCars; tint.w = plate row)
//   LAMP  20 B  x y z yaw, i32 type              (StreetLights)
//   HEAD  12 B  x y z                            (StreetLights: lamp heads, the pooled real lights)
//   POOL  80 B  i32 mesh, i32 layer, 3x4 matrix, world bounds centre 3 + extents 3 (StreetLights: the night light pools)
//   BOAT  40 B  x y z yaw, i32 kind, scale, tint rgba (MooredBoats)
//   COLL  84 B  i32 type (0 box, 1 capsule, 2 sphere), i32 layer, i32 flags (1 trigger), i32 material, i32 name,
//               position 3, rotation 4, lossy scale 3, centre 3, size 3 (box size | capsule radius, height, direction | sphere radius)
using System;
using UnityEngine;

namespace Magistrala.Cells
{
    public static class CellBlob
    {
        public const uint MAGIC = 0x3143474D; // "MGC1"
        public const int VERSION = 1;
        public const int SYSTEMS = 6;
        /// systems (bits of CellData.mask / manifest.systems)
        public const int TREES = 0, PROPS = 1, PARKED = 2, LAMPS = 3, BOATS = 4, COLLIDERS = 5;
        public static readonly string[] SysNames = { "trees", "props", "parked", "lamps", "boats", "colliders" };

        public static uint Id(string s) => (uint)s[0] | (uint)s[1] << 8 | (uint)s[2] << 16 | (uint)s[3] << 24;
        public static readonly uint TREE = Id("TREE"), PROP = Id("PROP"), PARK = Id("PARK"), LAMP = Id("LAMP"), HEAD = Id("HEAD"),
            POOL = Id("POOL"), BOAT = Id("BOAT"), COLL = Id("COLL");
        public const int TREE_B = 24, PROP_B = 52, PARK_B = 40, LAMP_B = 20, HEAD_B = 12, POOL_B = 80, BOAT_B = 40, COLL_B = 84;
        public static int RecordSize(uint id) =>
            id == TREE ? TREE_B : id == PROP ? PROP_B : id == PARK ? PARK_B : id == LAMP ? LAMP_B : id == HEAD ? HEAD_B :
            id == POOL ? POOL_B : id == BOAT ? BOAT_B : id == COLL ? COLL_B : 0;

        // ------------------------------------------------------------------ reading (any thread: no Unity API)
        public static float F(byte[] b, int o) => BitConverter.ToSingle(b, o);
        public static int I(byte[] b, int o) => BitConverter.ToInt32(b, o);
        public static uint U(byte[] b, int o) => BitConverter.ToUInt32(b, o);
        public static Vector3 V3(byte[] b, int o) => new Vector3(F(b, o), F(b, o + 4), F(b, o + 8));
        public static Vector4 V4(byte[] b, int o) => new Vector4(F(b, o), F(b, o + 4), F(b, o + 8), F(b, o + 12));
        /// 3x4 row-major -> Matrix4x4 (last row 0 0 0 1)
        public static Matrix4x4 M34(byte[] b, int o)
        {
            var m = new Matrix4x4();
            m.m00 = F(b, o); m.m01 = F(b, o + 4); m.m02 = F(b, o + 8); m.m03 = F(b, o + 12);
            m.m10 = F(b, o + 16); m.m11 = F(b, o + 20); m.m12 = F(b, o + 24); m.m13 = F(b, o + 28);
            m.m20 = F(b, o + 32); m.m21 = F(b, o + 36); m.m22 = F(b, o + 40); m.m23 = F(b, o + 44);
            m.m33 = 1; return m;
        }

        /// walks the sections of a blob (len bytes of b): per section id, record count, payload offset; false = not a blob
        public static bool Header(byte[] b, int len, out int cx, out int cz, out int sections)
        {
            cx = cz = sections = 0;
            if (len < 20 || U(b, 0) != MAGIC || I(b, 4) != VERSION) return false;
            cx = I(b, 8); cz = I(b, 12); sections = I(b, 16); return true;
        }

        // ------------------------------------------------------------------ writing (editor bake)
        public sealed class Writer
        {
            readonly System.IO.MemoryStream ms = new System.IO.MemoryStream();
            readonly System.IO.BinaryWriter w;
            readonly System.Collections.Generic.List<(uint id, int n, byte[] data)> secs = new System.Collections.Generic.List<(uint, int, byte[])>();
            System.IO.MemoryStream cur; System.IO.BinaryWriter cw; uint curId; int curN;
            public Writer() { w = new System.IO.BinaryWriter(ms); }
            public void Begin(uint id) { End(); curId = id; curN = 0; cur = new System.IO.MemoryStream(); cw = new System.IO.BinaryWriter(cur); }
            public void Rec() => curN++;
            public void F(float v) => cw.Write(v);
            public void I(int v) => cw.Write(v);
            public void V3(Vector3 v) { cw.Write(v.x); cw.Write(v.y); cw.Write(v.z); }
            public void V4(Vector4 v) { cw.Write(v.x); cw.Write(v.y); cw.Write(v.z); cw.Write(v.w); }
            public void Q(Quaternion q) { cw.Write(q.x); cw.Write(q.y); cw.Write(q.z); cw.Write(q.w); }
            public void M34(Matrix4x4 m)
            {
                cw.Write(m.m00); cw.Write(m.m01); cw.Write(m.m02); cw.Write(m.m03);
                cw.Write(m.m10); cw.Write(m.m11); cw.Write(m.m12); cw.Write(m.m13);
                cw.Write(m.m20); cw.Write(m.m21); cw.Write(m.m22); cw.Write(m.m23);
            }
            public void Raw(byte[] b, int o, int n) => cw.Write(b, o, n);
            void End()
            {
                if (cw == null) return;
                cw.Flush(); if (curN > 0) secs.Add((curId, curN, cur.ToArray()));
                cw = null; cur = null;
            }
            public byte[] Finish(int cx, int cz)
            {
                End();
                w.Write(MAGIC); w.Write(VERSION); w.Write(cx); w.Write(cz); w.Write(secs.Count);
                foreach (var s in secs)
                {
                    if (s.data.Length != s.n * RecordSize(s.id)) throw new Exception($"cell {cx},{cz}: section {s.id:X} has {s.data.Length} B for {s.n} records");
                    w.Write(s.id); w.Write(s.n); w.Write(s.data.Length); w.Write(s.data);
                }
                w.Flush(); return ms.ToArray();
            }
            public bool Empty { get { End(); return secs.Count == 0; } }
        }
    }
}
