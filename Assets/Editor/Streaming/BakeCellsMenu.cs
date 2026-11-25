#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Magistrala.Game;
using Magistrala.Streaming;

namespace Magistrala.Editor.Streaming
{
    public static class BakeCellsMenu
    {
        [MenuItem("Magistrala/Streaming: Bake cells (stub)")]
        public static void WriteExampleManifest()
        {
            if (!TryWorldBounds(out var bounds))
            {
                Debug.LogWarning("[stream] no World renderers/terrain or Resources/MagistralaGame nodes for world bounds");
                return;
            }
            string destination = EditorUtility.SaveFilePanel("Write EXAMPLE cell manifest (no scene splitting)",
                Path.GetTempPath(), "manifest.example", "json");
            if (string.IsNullOrEmpty(destination)) return;
            var example = CreateExample(bounds);
            File.WriteAllText(destination, JsonUtility.ToJson(example, true));
            Debug.Log($"[stream] example manifest: {example.l0.Length} L0 / {example.l1.Length} L1 → {destination}; exampleOnly=true, no scenes/blobs baked");
        }

        /// <summary>Pure format example. Replace scene names/blob paths/counts with real bake outputs, then clear exampleOnly.</summary>
        public static CellManifest CreateExample(Bounds bounds)
        {
            var result = new CellManifest { exampleOnly = true, worldBounds = bounds };
            Vector2Int first = CellGrid.Cell(bounds.min);
            // A maximum exactly on a cell boundary belongs to the preceding cell.
            var max = bounds.max;
            Vector2Int last = CellGrid.Cell(new Vector3(max.x - .001f, max.y, max.z - .001f));
            var l0 = new List<L0Cell>();
            var l1 = new List<L1Cell>();
            var parents = new HashSet<Vector2Int>();
            for (int j = first.y; j <= last.y; j++)
                for (int i = first.x; i <= last.x; i++)
                {
                    var coordinate = new Vector2Int(i, j);
                    string key = CellGrid.Key(coordinate);
                    l0.Add(new L0Cell
                    {
                        key = key, coordinate = coordinate,
                        bounds = CellGrid.CellBounds(coordinate, bounds.min.y, bounds.max.y),
                        sceneName = CellGrid.GeneratedRoot + "/" + key + ".unity",
                        // Counts stay zero and blobs empty: the stub must not claim files exist.
                        triangleCount = 0, rendererCount = 0, blobs = new CellBlobs()
                    });
                    var parent = CellGrid.Parent(coordinate);
                    if (!parents.Add(parent)) continue;
                    string parentKey = CellGrid.KeyL1(parent);
                    var parentBounds = new Bounds();
                    parentBounds.SetMinMax(new Vector3(parent.x * CellGrid.L1, bounds.min.y, parent.y * CellGrid.L1),
                        new Vector3((parent.x + 1) * CellGrid.L1, bounds.max.y, (parent.y + 1) * CellGrid.L1));
                    l1.Add(new L1Cell { key = parentKey, coordinate = parent, bounds = parentBounds,
                        sceneName = CellGrid.GeneratedRoot + "/" + parentKey + ".unity", meshName = parentKey });
                }
            result.l0 = l0.ToArray(); result.l1 = l1.ToArray();
            return result;
        }

        static bool TryWorldBounds(out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            var world = GameObject.Find("World");
            if (world)
            {
                var renderers = world.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++) Include(ref bounds, ref any, renderers[i].bounds);
                var terrains = world.GetComponentsInChildren<Terrain>(true);
                for (int i = 0; i < terrains.Length; i++)
                    if (terrains[i].terrainData)
                    {
                        Vector3 size = terrains[i].terrainData.size;
                        Include(ref bounds, ref any, new Bounds(terrains[i].transform.position + size * .5f, size));
                    }
            }
            if (any) return true;
            var graph = Resources.Load<GameWorld>(GameWorld.ResourcePath);
            if (!graph || graph.nodes == null) return false;
            for (int i = 0; i < graph.nodes.Length; i++) Include(ref bounds, ref any, new Bounds(graph.nodes[i], Vector3.one));
            return any;
        }

        static void Include(ref Bounds bounds, ref bool any, Bounds value)
        { if (any) bounds.Encapsulate(value); else { bounds = value; any = true; } }
    }
}
#endif
