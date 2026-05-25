using CounterStrikeSharp.API.Core;
using System;
using System.Drawing;
using WarcraftPlugin.Core.Effects;
using WarcraftPlugin.Helpers;

namespace WarcraftPlugin.Items;

internal class AmuletOfVitality : ShopItem
{
    protected override string Name => "Amulet of Vitality";
    protected override FormattableString Description => $"Increase max HP by {HealthBonus}";
    internal override int Price { get; set; } = 3500;
    internal override Color Color { get; set; } = Color.FromArgb(255, 255, 69, 0); // OrangeRed for vitality/health

    [Configurable]
    internal int HealthBonus { get; set; } = 50;

    internal override void Apply(CCSPlayerController player)
    {
        new AmuletOfVitalityEffect(player, HealthBonus).Start();
    }

    private class AmuletOfVitalityEffect(CCSPlayerController owner, int healthBonus)
        : WarcraftEffect(owner, onTickInterval: 0f)
    {
        public override void OnStart()
        {
            if (!TryGetAliveOwnerPawn(out var pawn)) return;
            SetMaxHealthBonus(Owner, healthBonus);
            RefreshPlayerState(Owner);
            Owner.SetHp(Math.Min(pawn.Health + healthBonus, pawn.MaxHealth));
        }

        public override void OnTick() { }

        public override void OnFinish()
        {
            RemoveStateContributions(Owner);
            RefreshPlayerState(Owner);
        }
    }
}
