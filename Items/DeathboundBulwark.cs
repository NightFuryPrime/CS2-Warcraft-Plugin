using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Events;
using System;
using System.Drawing;

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

        var reduction = Math.Clamp(DamageReduction, 0, 0.9);
        if (incomingHealth > 0)
        {
            var reducedHealth = (int)Math.Round(incomingHealth * (1 - reduction));
            @event.DmgHealth = Math.Max(0, reducedHealth);
        }

        if (incomingArmor > 0)
        {
            var reducedArmor = (int)Math.Round(incomingArmor * (1 - reduction));
            @event.DmgArmor = Math.Max(0, reducedArmor);
        }
    }
}
