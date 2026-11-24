// Shared cell grid for world streaming (plan: streaming-prijedlog.md, phases 1–3). Every builder and the runtime
// scheduler use THIS definition, so per-cell outputs from different menus line up.
//   L0 cells: 512 m squares in Unity world XZ, cell (i, j) = (floor(x / 512), floor(z / 512)), origin Unity (0, 0).
//   L1 cells: 2048 m = 4 × 4 L0 cells, (I, J) = (floor(i / 4), floor(j / 4)).
//   Cell key: "c_{i}_{j}" (L0) / "h_{I}_{J}" (L1); negative indices written as is, e.g. c_-16_10.
//   Output root for baked cell data: Assets/Generated/Cells/ (manifest.json + per-cell files), never in git.
using UnityEngine;

namespace Magistrala.Streaming
{
    public static class CellGrid
    {
        public const float L0 = 512f;
        public const int L1Factor = 4;
        public const float L1 = L0 * L1Factor;
        public const string GeneratedRoot = "Assets/Generated/Cells";

        public static Vector2Int Cell(Vector3 world) => new Vector2Int(Mathf.FloorToInt(world.x / L0), Mathf.FloorToInt(world.z / L0));
        public static Vector2Int Cell(float x, float z) => new Vector2Int(Mathf.FloorToInt(x / L0), Mathf.FloorToInt(z / L0));
        public static Vector2Int Parent(Vector2Int c) => new Vector2Int(Mathf.FloorToInt(c.x / (float)L1Factor), Mathf.FloorToInt(c.y / (float)L1Factor));
        public static Bounds CellBounds(Vector2Int c, float yMin = -50f, float yMax = 800f)
        {
            var b = new Bounds(); b.SetMinMax(new Vector3(c.x * L0, yMin, c.y * L0), new Vector3((c.x + 1) * L0, yMax, (c.y + 1) * L0)); return b;
        }
        public static Vector3 CellCenter(Vector2Int c) => new Vector3((c.x + .5f) * L0, 0, (c.y + .5f) * L0);
        public static string Key(Vector2Int c) => $"c_{c.x}_{c.y}";
        public static string KeyL1(Vector2Int c) => $"h_{c.x}_{c.y}";
    }
}
