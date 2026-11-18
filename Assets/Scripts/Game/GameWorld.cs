// Gameplay data of the Kaštela stretch (stops, the drivable road graph, road distances between stops), written by the editor
// menu Magistrala/Igra: podaci (Assets/Editor/GameDataBuilder.cs) from the web game's split-kastela files:
// stops.json, graph.json + junction-overrides*.json (merged as the web loader does), road-closures.json, distances.json.
// All positions are Unity coordinates (x = game x + 4200, z = -(game z) - 5300).
using System;
using UnityEngine;

namespace Magistrala.Game
{
    [Serializable]
    public class GStop
    {
        public string id, name, kind;
        public Vector3 pos;
        /// delivery bay: centre, unit direction towards the ramp / pad end (horizontal, Unity x/z), half length, half width
        public Vector3 bay; public Vector2 bayDir; public float bayHl, bayHw;
        /// graph node index of the spur's pad end (GPS target) and of the access node (distances.json), -1 = none
        public int padNode = -1, accessNode = -1;
    }

    [Serializable]
    public class GEdge
    {
        public int a, b; public bool oneway, closed;
        public float length;
        public string name;
        /// points p0 .. p0 + pn - 1 in GameWorld.pts, in travel order a → b
        public int p0, pn;
        public byte lanes;
    }

    public class GameWorld : ScriptableObject
    {
        public GStop[] stops;
        public Vector3[] nodes;
        public GEdge[] edges;
        public Vector3[] pts;
        /// directed road metres between stops (distances.json, access node to access node): dist[i * stops.Length + j], -1 = none
        public float[] dist;
        public string source;

        public const string ResourcePath = "MagistralaGame";

        public GStop Stop(string id) => Array.Find(stops, s => s.id == id);
        public int StopIndex(string id) => Array.FindIndex(stops, s => s.id == id);
        public float RoadMetres(string from, string to)
        {
            int i = StopIndex(from), j = StopIndex(to);
            return i < 0 || j < 0 ? -1 : dist[i * stops.Length + j];
        }
    }
}
