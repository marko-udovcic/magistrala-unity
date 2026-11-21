// Fuel (the HUD panel's pump %): Unity's first fuel model. A 7.5 t box truck, 120 l tank; diesel by distance driven, more with
// the load: 22 l/100 km empty → 30 l/100 km with 3 t on (Kaštela town driving, stop-and-go included), so a working day of
// 30–40 km takes ~8–12 l (7–10 % of the tank). Saved in SaveData.fuel (%, a new game starts at 72 %). Empty: the gauge shows 0 and
// warns; the engine still runs (TODO: cut the engine at 0).
// TODO (pumpa): refuelling at "Pumpa Kaštela" (stops.json kastela_pumpa) – stop at the pump, a key, pay from the cash →
// Refuel(litres). Not in scope of the HUD rework; the hook is ready.
using UnityEngine;

namespace Magistrala.Game
{
    public partial class MagistralaGame
    {
        public const float TankLitres = 120, LitresPer100Empty = 22, LitresPer100Full = 30, FullLoadKg = 3000;
        /// fuel in the tank, 0–100 %
        /// the Galeb (MagistralaGame.Vehicles.cs): 75 l tank, 10 l/100 km empty → 14 with 3 t (SaveData.fuelG)
        public const float GalebTank = 75, GalebPer100Empty = 10, GalebPer100Full = 14;
        public float FuelPct => InGaleb ? S.fuelG : S.fuel;
        float Tank => InGaleb ? GalebTank : TankLitres;
        public float FuelLitres => FuelPct * Tank / 100f;
        /// l/100 km at the current load
        public float Consumption => InGaleb ? Mathf.Lerp(GalebPer100Empty, GalebPer100Full, Mathf.Clamp01(Truck.CargoKg / FullLoadKg))
            : Mathf.Lerp(LitresPer100Empty, LitresPer100Full, Mathf.Clamp01((Truck ? Truck.CargoKg : 0) / FullLoadKg));

        void TickFuel(float dt)
        {
            if (!Truck || dt <= 0) return;
            float km = Truck.SpeedKmh / 3600f * dt;
            if (km <= 0) return;
            float litres = km * Consumption / 100f;
            if (InGaleb) S.fuelG = Mathf.Max(0, S.fuelG - litres / GalebTank * 100f);
            else S.fuel = Mathf.Max(0, S.fuel - litres / TankLitres * 100f);
        }

        /// TODO (pumpa): call from the Pumpa Kaštela refuelling; returns the litres actually taken
        public float Refuel(float litres)
        {
            float room = Tank - FuelLitres, l = Mathf.Clamp(litres, 0, room);
            if (InGaleb) S.fuelG = Mathf.Min(100, S.fuelG + l / GalebTank * 100f);
            else S.fuel = Mathf.Min(100, S.fuel + l / TankLitres * 100f);
            return l;
        }
    }
}
