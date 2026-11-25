using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using System;
using System.Drawing;
using WarcraftPlugin.Events.ExtendedEvents;
using WarcraftPlugin.Helpers;

namespace WarcraftPlugin.Items;

internal class DeathboundBulwark : ShopItem
{
    protected override string Name => "Deathbound Bulwark";
    protected override FormattableString Description => $"Reduce weapon damage taken by {(DamageReduction * 100):0.#}%";
    internal override int Price { get; set; } = 4000;
    internal override Color Color { get; set; } = Color.DarkSlateBlue;

    [Configurable]
    internal double DamageReduction { get; set; } = 0.25;

    internal override void Apply(CCSPlayerController player) { }

    internal override void OnPlayerHurt(EventPlayerHurt @event)
    {
        if (@event == null) return;
        var incomingHealth = @event.DmgHealth;
        var incomingArmor = @event.DmgArmor;
        if (incomingHealth <= 0 && incomingArmor <= 0) return;

        var reduction = DamageMath.CalculateReduction(incomingHealth, incomingArmor, DamageReduction);
        @event.IgnoreDamage(reduction.PreventedHealth, reduction.PreventedArmor);
    }
}
