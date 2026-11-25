using CounterStrikeSharp.API.Core;
using System;
using System.Drawing;
using WarcraftPlugin.Core;

namespace WarcraftPlugin.Items;

internal class WeaponInsurance : ShopItem
{
    protected override string Name => "Weapon Insurance";
    protected override FormattableString Description => $"Restore your purchased weapons next round after death.";
    internal override int Price { get; set; } = 3500;
    internal override bool IsInstant => true;
    internal override Color Color { get; set; } = Color.Goldenrod;

    internal override void Apply(CCSPlayerController player)
    {
        WeaponInsuranceService.GrantInsurance(player);
    }
}
