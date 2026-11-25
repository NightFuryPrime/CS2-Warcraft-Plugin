using CounterStrikeSharp.API.Core;
using System;
using System.Drawing;
using WarcraftPlugin.Core.Effects.Shared;

namespace WarcraftPlugin.Items;

internal class GlovesOfDazzle : ShopItem
{
    protected override string Name => "Gloves of Dazzle";
    protected override FormattableString Description => $"Receive a Flashbang every {GrenadeInterval}s";
    internal override int Price { get; set; } = 3000;
    internal override Color Color { get; set; } = Color.FromArgb(255, 255, 255, 0); // Yellow for utility/flashbang

    [Configurable]
    internal float GrenadeInterval { get; set; } = 12f;
    [Configurable]
    internal string GrenadeType { get; set; } = "weapon_flashbang";

    internal override void Apply(CCSPlayerController player)
    {
        new GrenadeSupplyEffect(player, GrenadeType, GrenadeInterval, Localizer["item.gloves_of_dazzle.grenade_name"]).Start();
    }
}
