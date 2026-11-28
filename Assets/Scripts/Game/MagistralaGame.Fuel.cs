// Per-vehicle diesel; legacy fuel/fuelK/fuelG fields retain the previous saves.
using UnityEngine;

namespace Magistrala.Game
{
    public partial class MagistralaGame
    {
        /// fuel in the tank, 0–100 %
        /// Galeb: 75 l, 10–14 l/100 km (empty → 1400 kg).
        public const float GalebTank = 75, GalebPer100Empty = 10, GalebPer100Full = 14;
        /// Sprinter: 90 l, 11–15 l/100 km (empty → 1300 kg).
        public const float KombiTank = 90, KombiPer100Empty = 11, KombiPer100Full = 15;
        /// the firm's L4H1 box van (PlayerTruck, id "furgon"): 90 l tank, 11.5 l/100 km empty → 14.5 with 1.1 t (its own payload)
        public const float VanTank = 90, VanPer100Empty = 11.5f, VanPer100Full = 14.5f;
        string FuelId => Truck ? Truck.VehicleId : "furgon";
        public float FuelPct => FuelId == "mali" ? S.fuelM : FuelId == "galeb" ? S.fuelG : FuelId == "sprinter" ? S.fuelK : S.fuel;
        float Tank => FuelId == "mali" ? 60 : FuelId == "galeb" ? GalebTank : FuelId == "sprinter" ? KombiTank : VanTank;
        public float FuelLitres => FuelPct * Tank / 100f;
        /// l/100 km at the current load
        public float Consumption
        {
            get
            {
                float k = Mathf.Clamp01((Truck ? Truck.CargoKg : 0) / (Truck ? Mathf.Max(1, Truck.spec.payloadKg) : 1100));
                return FuelId == "mali" ? Mathf.Lerp(6.5f, 9f, k)
                    : FuelId == "galeb" ? Mathf.Lerp(GalebPer100Empty, GalebPer100Full, k)
                    : FuelId == "sprinter" ? Mathf.Lerp(KombiPer100Empty, KombiPer100Full, k)
                    : Mathf.Lerp(VanPer100Empty, VanPer100Full, k);
            }
        }
        void SetFuelPct(float pct)
        {
            pct = Mathf.Clamp(pct, 0, 100);
            if (FuelId == "mali") S.fuelM = pct; else if (FuelId == "galeb") S.fuelG = pct; else if (FuelId == "sprinter") S.fuelK = pct; else S.fuel = pct;
        }

        void TickFuel(float dt)
        {
            if (!Truck || dt <= 0) return;
            float km = Truck.SpeedKmh / 3600f * dt;
            if (km <= 0) return;
            float litres = km * Consumption / 100f;
            SetFuelPct(FuelPct - litres / Tank * 100f);
        }

        /// TODO (pumpa): call from the Pumpa Kaštela refuelling; returns the litres actually taken
        public float Refuel(float litres)
        {
            float room = Tank - FuelLitres, l = Mathf.Clamp(litres, 0, room);
            SetFuelPct(FuelPct + l / Tank * 100f);
            return l;
        }
    }
}
