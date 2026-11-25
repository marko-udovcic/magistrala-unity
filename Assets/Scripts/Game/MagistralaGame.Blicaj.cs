// The Blicaj group on Mate's phone (Phone/Blicaj.cs, Phone/MatePhone.Blicaj.cs): the game side.
//   spots: the D8 and the old road (Ž6137) through each Kaštel are the graph points of that road inside the settlement
//   (GameWorld.PlaceAt, the baked OSM boundaries), the one nearest their middle; stop spots take the stop's position.
//   Spots that do not resolve are left out of the posts.
//   reading the group while driving (IDEJE_IGRA.md §7 "Gledanje u mobitel dok voziš je rizik"): opening the app above
//   PhoneRiskKmh has a PhoneRiskChance chance that a patrol sees it – always only a warning, no fine (Marko, 23. 11.:
//   "može upozorenje"); repeats the same day say so. SaveData.phoneWarnDay / phoneWarns.
// Test switches: -blicajdemo  eight posts from the last hour and a report of Mate's, unread · -phone blicaj  open the app
//   (with -clockx 20 the day's posts arrive quickly; the log prints each as "[blicaj] hh:mm who: text")
using System.Collections.Generic;
using UnityEngine;
using Magistrala.Game.Phone;

namespace Magistrala.Game
{
    public partial class MagistralaGame
    {
        public const float PhoneRiskKmh = 40, PhoneRiskChance = 0.15f;

        void SetupBlicaj()
        {
            int ok = 0;
            foreach (var s in BlicajFeed.Spots)
            {
                s.ok = false;
                if (s.road == 'p')
                {
                    var st = World.Stop(s.key);
                    if (st != null)
                    {
                        s.pos = st.pos; s.ok = true; ok++;
                        string pl = World.PlaceAt(st.pos);
                        s.head = string.IsNullOrEmpty(pl) || st.name.Contains(pl) ? st.name : $"{st.name}, {pl}";
                    }
                    continue;
                }
                var pts = new List<Vector3>(); var names = new List<string>();
                foreach (var e in World.edges)
                {
                    bool match = s.road == 'd' ? e.road == "D8" : e.road != null && e.road.StartsWith("Ž6137");
                    if (!match) continue;
                    for (int k = 0; k < e.pn; k++) { var p = World.pts[e.p0 + k]; if (World.PlaceAt(p) == s.key) { pts.Add(p); names.Add(e.name); } }
                }
                if (pts.Count == 0) { Debug.LogWarning($"[blicaj] spot {s.id}: no {(s.road == 'd' ? "D8" : "Ž6137")} in {s.key}"); continue; }
                Vector3 c = Vector3.zero; foreach (var p in pts) c += p; c /= pts.Count;
                float bd = float.MaxValue;
                string street = "";
                for (int k = 0; k < pts.Count; k++) { float d = (pts[k] - c).sqrMagnitude; if (d < bd) { bd = d; s.pos = pts[k]; street = names[k]; } }
                s.ok = true; ok++;
                // the headline: the settlement and the road there (the old road by its street name)
                s.head = s.road == 'd' ? $"{s.key}, D8" : $"{s.key}, {(string.IsNullOrEmpty(street) ? "stara cesta" : street)}";
            }
            BlicajFeed.Reset();
            foreach (var s in BlicajFeed.Spots) if (s.ok) Debug.Log($"[blicaj] spot {s.id}: \"{s.head}\"");
            Debug.Log($"[blicaj] {ok}/{BlicajFeed.Spots.Length} spots; today's patrol {BlicajFeed.PatrolSpot(Day, 9)?.id} (morning), {BlicajFeed.PatrolSpot(Day, 15)?.id} (afternoon)");

            Phone.truckPos = () => Truck ? TruckCentre() : (Vector3?)null;
            Phone.placeName = () => LocationLabel.Place;
            Phone.blicajOpened = PhoneRisk;
        }

        void PhoneRisk()
        {
            if (!Truck || Truck.SpeedKmh < PhoneRiskKmh || UnityEngine.Random.value >= PhoneRiskChance) return;
            if (S.phoneWarnDay != Day) { S.phoneWarnDay = Day; S.phoneWarns = 0; }
            S.phoneWarns++;
            Banner(S.phoneWarns == 1 ? "Patrola te vidila s mobitelom u ruci. Ovaj put samo upozorenje."
                : $"Opet mobitel u ruci – patrola te upozorila ({S.phoneWarns}. put danas). Pusti mobitel dok voziš.");
            Debug.Log($"[blicaj] seen with the phone at {Truck.SpeedKmh:0} km/h: warning {S.phoneWarns} today");
        }
    }
}
