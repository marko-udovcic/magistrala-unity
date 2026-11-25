using System;
using System.Collections.Generic;
using UnityEngine;

namespace Magistrala.Streaming
{
    [Flags]
    public enum CellDataKind { None = 0, Trees = 1, Parked = 2, Lamps = 4, Props = 8, Boats = 16, All = 31 }

    [Serializable]
    public sealed class CellBlobs
    {
        // Relative to the producer's per-cell data root; empty means absent. The scheduler does not open these files.
        public string trees = "", parked = "", lamps = "", props = "", boats = "";
        public CellDataKind Available =>
            (string.IsNullOrEmpty(trees) ? CellDataKind.None : CellDataKind.Trees) |
            (string.IsNullOrEmpty(parked) ? CellDataKind.None : CellDataKind.Parked) |
            (string.IsNullOrEmpty(lamps) ? CellDataKind.None : CellDataKind.Lamps) |
            (string.IsNullOrEmpty(props) ? CellDataKind.None : CellDataKind.Props) |
            (string.IsNullOrEmpty(boats) ? CellDataKind.None : CellDataKind.Boats);
    }

    [Serializable]
    public sealed class L0Cell
    {
        public string key;
        public Vector2Int coordinate;
        public Bounds bounds;
        // Prefer a full Build Settings scene path, e.g. Assets/Generated/Cells/c_0_0.unity.
        // Empty is a valid data-only cell. Roots in a nonempty scene MUST be saved inactive.
        public string sceneName = "";
        public long triangleCount;
        public int rendererCount;
        public CellBlobs blobs = new CellBlobs();
    }

    [Serializable]
    public sealed class L1Cell
    {
        public string key;
        public Vector2Int coordinate;
        public Bounds bounds;
        public string sceneName = "", meshName = "";
    }

    [Serializable]
    public sealed class CellManifest
    {
        public const string ResourcePath = "Cells/manifest";
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public bool exampleOnly;
        public float l0Size = CellGrid.L0, l1Size = CellGrid.L1;
        public Bounds worldBounds;
        public L0Cell[] l0 = Array.Empty<L0Cell>();
        public L1Cell[] l1 = Array.Empty<L1Cell>();

        public static bool TryLoad(out CellManifest manifest, out string error)
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (!asset) { manifest = null; error = "missing Resources/Cells/manifest.json"; return false; }
            return TryParse(asset.text, out manifest, out error);
        }

        public static bool TryParse(string json, out CellManifest manifest, out string error)
        {
            manifest = null;
            try
            {
                var value = JsonUtility.FromJson<CellManifest>(json);
                if (value == null) { error = "empty manifest"; return false; }
                if (!value.Validate(out error)) return false;
                manifest = value;
                return true;
            }
            catch (Exception e) { error = "invalid manifest: " + e.Message; return false; }
        }

        public bool Validate(out string error)
        {
            error = null;
            if (version != CurrentVersion || l0Size != CellGrid.L0 || l1Size != CellGrid.L1)
                error = "unsupported manifest version or grid size";
            else if (exampleOnly) error = "example manifest; bake real cells before enabling streaming";
            else if (l0 == null || l0.Length == 0 || l1 == null) error = "manifest requires l0 cells and an l1 array";
            if (error != null) return false;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var scenes = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < l0.Length; i++)
            {
                var c = l0[i];
                if (c == null || c.key != CellGrid.Key(c.coordinate) || !keys.Add(c.key) || !ValidBounds(c.bounds) ||
                    c.bounds.min.x != c.coordinate.x * CellGrid.L0 || c.bounds.min.z != c.coordinate.y * CellGrid.L0 ||
                    c.bounds.size.x != CellGrid.L0 || c.bounds.size.z != CellGrid.L0 ||
                    c.triangleCount < 0 || c.rendererCount < 0 || c.blobs == null ||
                    (!string.IsNullOrEmpty(c.sceneName) && !scenes.Add(c.sceneName)))
                { error = "invalid/duplicate L0 cell at index " + i; return false; }
            }
            for (int i = 0; i < l1.Length; i++)
            {
                var c = l1[i];
                if (c == null || c.key != CellGrid.KeyL1(c.coordinate) || !keys.Add(c.key) || !ValidBounds(c.bounds))
                { error = "invalid/duplicate L1 cell at index " + i; return false; }
            }
            return true;
        }

        static bool ValidBounds(Bounds b) => Finite(b.center.x) && Finite(b.center.y) && Finite(b.center.z) &&
            Finite(b.size.x) && Finite(b.size.y) && Finite(b.size.z) && b.size.x > 0 && b.size.z > 0 && b.size.y >= 0;
        static bool Finite(float x) => !float.IsNaN(x) && !float.IsInfinity(x);
    }
}
