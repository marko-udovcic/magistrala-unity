// Menu Magistrala/Igra: podaci, part 4 — data for the HUD panel (TruckHud.cs): the speed limit of every graph edge and a sea mask
// for the minimap.
//   limit   the edge's maxspeed: graph.json (tools/world s03 / s04: OSM `maxspeed`, where OSM has none s04's default_speed),
//           the override layers' added / patched edges (junction-overrides*.json carry it), the streets outside the models
//           (ceste_izvan_modela.json, OSM maxspeed or null). Then:
//             s04's ramp default (60) → 50 (town slip roads);
//             riva / old-core streets (Obala…, Riva, Šetalište…, Brce, Trg…) at the 50 default → 30;
//             none at all → D8 dual carriageway (one-way, 2 lanes) 70, D8 otherwise 50, ramps 50, riva / core 30, streets 50.
//   sea     bit per 8 m cell over the game area (+ 800 m): the baked ground (WorldData Tiles.Height, game frame) below the sea plane (y 0).
using System;
using System.Collections.Generic;
using Magistrala.Game;
using UnityEngine;

namespace Magistrala.EditorTools
{
    public partial class GameDataBuilder
    {
        static bool CoreStreet(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            return n.StartsWith("Obala") || n.StartsWith("Riva") || n.Contains("riva") || n.StartsWith("Šetalište") || n.Contains("šetalište")
                || n.Contains("Brce") || n.StartsWith("Trg") || n.StartsWith("Pjaca") || n == "Mala Mora";
        }

        static int SpeedLimit(E e, Dictionary<string, double> stats)
        {
            string src; int v;
            bool ramp = e.type == "ramp" || (e.type ?? "").EndsWith("_link");
            bool d8 = RoadRef(e.rf) == "D8" || (e.name ?? "").StartsWith("Cesta pape Ivana Pavla");
            if (e.maxspeed > 0)
            {
                v = e.maxspeed; src = "OSM / podaci";
                if (ramp && v == 60) { v = 50; src = "zadano (rampa)"; }
                else if (v == 50 && CoreStreet(e.name)) { v = 30; src = "zadano (riva / jezgra)"; }
            }
            else
            {
                src = "zadano";
                v = d8 ? (e.oneway && e.lanes >= 2 ? 70 : 50) : ramp ? 50 : CoreStreet(e.name) ? 30 : 50;
            }
            v = Mathf.Clamp(v, 10, 130);
            stats.TryGetValue(src, out double L); stats[src] = L + e.length;
            return v;
        }

        static void BakeSea(GameWorld asset)
        {
            // the game area (MinX … MaxX, MinZ … MaxZ in the game frame, as the stops) + 800 m; the graph reaches far beyond
            Vector3 c0 = W.U(MinX - 800, 0, MinZ - 800), c1 = W.U(MaxX + 800, 0, MaxZ + 800);
            float x0 = Mathf.Min(c0.x, c1.x), x1 = Mathf.Max(c0.x, c1.x), z0 = Mathf.Min(c0.z, c1.z), z1 = Mathf.Max(c0.z, c1.z);
            const float C = 8;
            int w = Mathf.CeilToInt((x1 - x0) / C), h = Mathf.CeilToInt((z1 - z0) / C);
            var bits = new byte[(w * h + 7) / 8]; int sea = 0;
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    var g = W.G(new Vector3(x0 + (i + .5f) * C, 0, z0 + (j + .5f) * C));
                    if (Tiles.Height(g.x, g.y) < -0.05) { int k = j * w + i; bits[k >> 3] |= (byte)(1 << (k & 7)); sea++; }
                }
            asset.seaBits = bits; asset.seaOrigin = new Vector2(x0, z0); asset.seaCell = C; asset.seaW = w; asset.seaH = h;
            Debug.Log($"[igra] sea mask {w}×{h} cells of {C} m, {sea * 100f / (w * h):0} % sea");
        }
    }
}
