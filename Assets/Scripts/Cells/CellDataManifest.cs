// World streaming, phase 2 (docs: Assets/Scripts/Cells/CellData.cs): the index of the per-cell data blobs the bake writes
// (Magistrala/Ćelije: ispeci, Assets/Editor/CellBake.cs) and the shared tables the blobs refer to by index (prop prototypes,
// collider names / physics materials, light-pool meshes). One asset: Assets/Generated/Cells/Resources/MagistralaCells.asset.
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Magistrala.Cells
{
    public class CellDataManifest : ScriptableObject
    {
        public const string ResourceName = "MagistralaCells";
        public int version;
        /// the CellGrid the blobs were cut with (a different grid = a stale bake)
        public float cellSize; public Vector2 gridOrigin;
        /// per non-empty cell: its index and blob (Assets/Generated/Cells/c_<x>_<z>.bytes)
        public Vector2Int[] cells = new Vector2Int[0];
        public TextAsset[] blobs = new TextAsset[0];
        /// systems in the blobs (CellBlob.Sys bits) and per system a fingerprint of the scene data they were baked from
        public int systems;
        public string[] fingerprints = new string[CellBlob.SYSTEMS];
        /// trees (World/Vegetation TreeRenderer): plants per prototype and their mean scale over the whole map (the small-plant
        /// shadow / far caps depend on it, so it must not change with what is loaded)
        public double[] treeMeanScale = new double[0];
        public int treeCount;
        /// props + nature (CellProps): prototypes by index
        public PropProto[] props = new PropProto[0];
        /// colliders (CellColliders): names and physics materials by index
        public string[] colliderNames = new string[0];
        public PhysicsMaterial[] colliderMats = new PhysicsMaterial[0];
        /// street-light pools (StreetLights): the draped meshes by index
        public Mesh[] poolMeshes = new Mesh[0];
        /// per system: instances baked (log / checks)
        public int[] counts = new int[CellBlob.SYSTEMS];
    }

    /// one prop / nature prototype: what one prefab instance draws, LODGroup emulated (fade mode none: the level by the screen
    /// height of the group, as Unity's LODGroup; renderers outside any LOD of the group: level -1, always drawn)
    [Serializable]
    public class PropProto
    {
        public string name;
        /// LODGroup in the instance root's frame: size (m, at scale 1), reference point; screen-relative transition height per level
        /// (empty = no LODGroup: drawn to maxDist)
        public float lodSize; public Vector3 lodRef;
        public float[] lodHeights = new float[0];
        public float maxDist = 1500;
        /// bounds of every part in the root's frame (culling)
        public Bounds bounds;
        public PropPart[] parts = new PropPart[0];
    }

    /// one draw of a prototype: a submesh with its material at a local matrix (relative to the instance root), on one LOD level
    [Serializable]
    public class PropPart
    {
        public int level;
        public Mesh mesh; public int submesh; public Material mat;
        public Matrix4x4 local = Matrix4x4.identity;
        public ShadowCastingMode shadow = ShadowCastingMode.On;
        public bool receive = true;
        public int layer;
        public LightProbeUsage lightProbes = LightProbeUsage.BlendProbes;
        public ReflectionProbeUsage reflProbes = ReflectionProbeUsage.BlendProbes;
    }
}
