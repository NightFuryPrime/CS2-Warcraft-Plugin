using System;

namespace WarcraftPlugin.Models
{
    [Flags]
    public enum DamageTypes : uint
    {
        Generic = 0,
        Crush = 1 << 0,
        Bullet = 1 << 1,
        Slash = 1 << 2,
        Burn = 1 << 3,
        Shock = 1 << 4,
        Sonic = 1 << 5,
        EnergyBeam = 1 << 6,
        Explosion = 1 << 7,
        Radiation = 1 << 8,
        Drown = 1 << 9,
        Poison = 1 << 12,
        Acid = 1 << 13,
        CrushVehicle = 1 << 19,
        Fall = 1 << 24,
        Blast = 1 << 20,
    }
}
