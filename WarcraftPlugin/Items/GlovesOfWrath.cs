using CounterStrikeSharp.API.Core;
using System;
using System.Drawing;
using WarcraftPlugin.Core.Effects.Shared;

namespace WarcraftPlugin.Items;

internal class GlovesOfWrath : ShopItem
{
    protected override string Name => "Gloves of Wrath";
    protected override FormattableString Description => $"Receive a HE grenade every {GrenadeInterval}s";
    internal override int Price { get; set; } = 3000;
    internal override Color Color { get; set; } = Color.FromArgb(255, 255, 140, 0); // DarkOrange for offensive/grenade

    [Configurable]
    internal float GrenadeInterval { get; set; } = 12f;
    [Configurable]
    internal string GrenadeType { get; set; } = "weapon_hegrenade";

    internal override void Apply(CCSPlayerController player)
    {
        new GrenadeSupplyEffect(player, GrenadeType, GrenadeInterval, Localizer["item.gloves_of_wrath.grenade_name"]).Start();
    }
}
