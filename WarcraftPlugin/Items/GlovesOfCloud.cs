using CounterStrikeSharp.API.Core;
using System;
using System.Drawing;
using WarcraftPlugin.Core.Effects.Shared;

namespace WarcraftPlugin.Items;

internal class GlovesOfCloud : ShopItem
{
    protected override string Name => "Gloves of Cloud";
    protected override FormattableString Description => $"Receive a Smoke grenade every {GrenadeInterval}s";
    internal override int Price { get; set; } = 3000;
    internal override Color Color { get; set; } = Color.FromArgb(255, 169, 169, 169); // DarkGray for utility/smoke

    [Configurable]
    internal float GrenadeInterval { get; set; } = 12f;
    [Configurable]
    internal string GrenadeType { get; set; } = "weapon_smokegrenade";

    internal override void Apply(CCSPlayerController player)
    {
        new GrenadeSupplyEffect(player, GrenadeType, GrenadeInterval, Localizer["item.gloves_of_cloud.grenade_name"]).Start();
    }
}
