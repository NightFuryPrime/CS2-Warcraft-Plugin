using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Drawing;
using WarcraftPlugin.Events.ExtendedEvents;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Items;

internal class BladeMail : ShopItem
{
    protected override string Name => "Blademail";
    protected override FormattableString Description => $"{ReflectChance * 100:0.#}% chance to reflect {ReflectPercent * 100:0.#}% of incoming weapon damage";
    internal override int Price { get; set; } = 10000;
    internal override Color Color { get; set; } = Color.Crimson;

    [Configurable] internal double ReflectChance { get; set; } = 0.5;
    [Configurable] internal double ReflectPercent { get; set; } = 0.33;

    internal override void Apply(CCSPlayerController player) { }

    internal override void OnPlayerHurt(EventPlayerHurt @event)
    {
        var victim = @event.Userid;
        var attacker = @event.Attacker;

        if (!victim.IsValid || attacker == null || !attacker.IsAlive())
            return;
        if (!Warcraft.RollChance((float)(ReflectChance * 100)))
            return;

        var reflect = (int)Math.Round(@event.DmgHealth * ReflectPercent);
        if (reflect <= 0)
            return;

        attacker.TakeDamage(reflect, victim, KillFeedIcon.knife, abilityName: LocalizedName);
        victim.PrintToChat($" {ChatColors.Green}{LocalizedName}{ChatColors.Default}: {ChatColors.Gold}{reflect}{ChatColors.Default} damage returned to {attacker.GetRealPlayerName()}");
    }
}
