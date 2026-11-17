// AI traffic data for the Unity probe, written by the editor menu Magistrala/Build traffic (Assets/Editor/TrafficBuilder.cs):
// the D8 lane corridors (one per direction, chained from the finished road models' lane axes), the D8 stop lines of the
// models' signal plans, and the car kinds (meshes / materials of the web game's traffic GLBs, split into instanced parts).
using System;
using UnityEngine;

namespace Magistrala.Traffic
{
    [Serializable]
    public class TrafficPart
    {
        public Mesh mesh; public int sub; public Material mat; public Matrix4x4 local; public bool paint;
        /// lamps: 0 none, 1 tail (brake light), 2 left indicator, 3 right indicator; matOn = the lit variant
        public int lamp; public Material matOn;
    }

    [Serializable]
    public class TrafficKind
    {
        public string name;
        public float len, halfW, h, vMul, a, b, T, weight;
        public TrafficPart[] lod0, lod1;
        /// body paint colours (instanced copies of the kind's paint material) and their spawn weights
        public Material[] paints; public float[] paintW;
        /// far LOD: the lod1 model merged into one mesh (submesh 0 = everything else on a shared colour palette, 1 = paint,
        /// 2 = tail lamps) — three instanced draws per kind instead of ~20; farPaints parallel to paints
        public Mesh farMesh; public Material[] farPaints; public Material farTail, farTailOn;
    }

    [Serializable]
    public class TrafficStop
    {
        /// arc length of the stop line on the corridor (m); signal plan index into TrafficData.plans, group name
        public float s; public int plan; public string group; public string model;
    }

    [Serializable]
    public class TrafficCorridor
    {
        public string name;
        public float step = 2;
        public int maxLanes;
        /// per sample (Unity coordinates): carriageway axis, unit right vector (horizontal), lane count
        public Vector3[] axis;
        public Vector3[] right;
        public byte[] lanes;
        /// [i * maxLanes + k]: lateral offset (m, + = right of travel) of lane k's centre, NaN where lane k doesn't exist; its surface height
        public float[] laneOff;
        public float[] laneY;
        /// curve speed cap (m/s) and the posted limit (m/s)
        public float[] vmax;
        public TrafficStop[] stops;
        public float Length => (axis.Length - 1) * step;
    }

    [Serializable]
    public class TrafficPhase { public string group; public float g0, g1, y0, y1; }

    [Serializable]
    public class TrafficPlan { public string key; public float cycle; public TrafficPhase[] phases; }

    public class TrafficData : ScriptableObject
    {
        public TrafficCorridor[] corridors;
        public TrafficKind[] kinds;
        public TrafficPlan[] plans;
        /// shared palette material of the far LOD meshes
        public Material farPalette;
    }
}
