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
        /// mag-11-19 delivery points (GameDataBuilder.Tours.cs): the package checkpoint id (-1 = a stops.json stop), its tours
        /// ("pice:u" load / "pice:d" deliver), the largest vehicle allowed ("" = any; "mali" = van or small truck, the waterfronts)
        public int pid = -1;
        public string[] tours = new string[0];
        public string maxVehicle = "";
        public bool Has(string tour, char role) { foreach (var t in tours) if (t.Length == tour.Length + 2 && t.StartsWith(tour) && t[tour.Length + 1] == role) return true; return false; }
    }

    /// a tour type of the package (world_1to1.json tours): cargo, route text, who orders it (firma / fatureta), invoice / cash
    [Serializable]
    public class GTour { public string id, cargo, route, who, payment; public int pickups, drops; }

    [Serializable]
    public class GEdge
    {
        public int a, b; public bool oneway, closed;
        public float length;
        public string name;
        /// road number shown with the name (GameDataBuilder.RoadRef): "D8", "Ž6137"; "" = none (local L roads are left out)
        public string road = "";
        /// points p0 .. p0 + pn - 1 in GameWorld.pts, in travel order a → b
        public int p0, pn;
        public byte lanes;
        /// speed limit km/h (GameDataBuilder.SpeedLimit: OSM maxspeed, else the town defaults), 0 = unknown
        public byte limit;
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
        public GTour[] tours = new GTour[0];
        /// place under every 50 m cell (GameDataBuilder.Places.cs, OSM settlement boundaries): placeGrid[j * placeW + i] indexes
        /// placeNames (0 = none, e.g. the open sea); cell (0, 0) starts at Unity (placeOrigin.x, placeOrigin.y) = (x, z)
        public string[] placeNames = new string[0];
        public byte[] placeGrid = new byte[0];
        public Vector2 placeOrigin; public float placeCell = 50; public int placeW, placeH;

        /// sea under every seaCell m cell (GameDataBuilder.BakeSea: baked ground below the sea plane), one bit per cell, row-major
        /// from Unity (seaOrigin.x, seaOrigin.y) = (x, z); for the HUD minimap
        public byte[] seaBits = new byte[0];
        public Vector2 seaOrigin; public float seaCell = 8; public int seaW, seaH;
        public const string ResourcePath = "MagistralaGame";

        public GStop Stop(string id) => Array.Find(stops, s => s.id == id);
        public int StopIndex(string id) => Array.FindIndex(stops, s => s.id == id);
        public GTour Tour(string id) => Array.Find(tours, t => t.id == id);
        public string PlaceAt(Vector3 u)
        {
            if (placeGrid == null || placeGrid.Length == 0) return "";
            int i = Mathf.FloorToInt((u.x - placeOrigin.x) / placeCell), j = Mathf.FloorToInt((u.z - placeOrigin.y) / placeCell);
            if (i < 0 || j < 0 || i >= placeW || j >= placeH) return "";
            return placeNames[placeGrid[j * placeW + i]];
        }
        public bool SeaAt(int i, int j)
        {
            if (i < 0 || j < 0 || i >= seaW || j >= seaH) return false;
            int k = j * seaW + i; return (seaBits[k >> 3] & (1 << (k & 7))) != 0;
        }
        public float RoadMetres(string from, string to)
        {
            int i = StopIndex(from), j = StopIndex(to);
            return i < 0 || j < 0 ? -1 : dist[i * stops.Length + j];
        }
    }
}
