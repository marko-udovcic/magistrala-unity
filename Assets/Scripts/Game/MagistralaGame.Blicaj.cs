// The Blicaj group on Mate's phone (Phone/Blicaj.cs, Phone/MatePhone.Blicaj.cs): the game side.
//   spots: the D8 and the old road (Ž6137) through each Kaštel are the graph points of that road inside the settlement
//   (GameWorld.PlaceAt, the baked OSM boundaries), the one nearest their middle; stop spots take the stop's position.
//   Spots that do not resolve are left out of the posts.
//   reading the group while driving (IDEJE_IGRA.md §7 "Gledanje u mobitel dok voziš je rizik"): opening the app above
//   PhoneRiskKmh has a PhoneRiskChance chance that a patrol sees it – the first time a day a warning, then PhoneFine € from
//   Mate's pocket (the trade account covers what the pocket lacks). SaveData.phoneWarnDay / phoneWarns.
// Test switches: -blicajdemo  eight posts from the last hour and a report of Mate's, unread · -phone blicaj  open the app
//   (with -clockx 20 the day's posts arrive quickly; the log prints each as "[blicaj] hh:mm who: text")
using System.Collections.Generic;
using UnityEngine;
using Magistrala.Game.Phone;

namespace Magistrala.Game
{
    public partial class MagistralaGame
    {
        public const float PhoneRiskKmh = 40, PhoneRiskChance = 0.15f, PhoneFine = 40;

        void SetupBlicaj()
        {
            int ok = 0;
            foreach (var s in BlicajFeed.Spots)
            {
                s.ok = false;
                if (s.road == 'p')
                {
                    var st = World.Stop(s.key);
                    if (st != null) { s.pos = st.pos; s.ok = true; ok++; }
                    continue;
                }
                var pts = new List<Vector3>();
                foreach (var e in World.edges)
                {
                    bool match = s.road == 'd' ? e.road == "D8" : e.road != null && e.road.StartsWith("Ž6137");
                    if (!match) continue;
                    for (int k = 0; k < e.pn; k++) { var p = World.pts[e.p0 + k]; if (World.PlaceAt(p) == s.key) pts.Add(p); }
                }
                if (pts.Count == 0) { Debug.LogWarning($"[blicaj] spot {s.id}: no {(s.road == 'd' ? "D8" : "Ž6137")} in {s.key}"); continue; }
                Vector3 c = Vector3.zero; foreach (var p in pts) c += p; c /= pts.Count;
                float bd = float.MaxValue;
                foreach (var p in pts) { float d = (p - c).sqrMagnitude; if (d < bd) { bd = d; s.pos = p; } }
                s.ok = true; ok++;
            }
            BlicajFeed.Reset();
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
            if (S.phoneWarns == 1) Banner("Patrola te vidila s mobitelom u ruci. Ovaj put samo upozorenje.");
            else
            {
                float fromCash = Mathf.Min(Mathf.Max(0, S.cash), PhoneFine);
                S.cash -= fromCash; S.account -= PhoneFine - fromCash;
                Banner($"Mobitel u vožnji: kazna {PhoneFine:0} € iz tvog džepa.");
            }
            Debug.Log($"[blicaj] seen with the phone at {Truck.SpeedKmh:0} km/h: warning {S.phoneWarns} today");
        }
    }
}
