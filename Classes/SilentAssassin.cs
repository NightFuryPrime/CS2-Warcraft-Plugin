using System;
using System.Collections.Generic;
using System.Drawing;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;
using WarcraftPlugin.Events.ExtendedEvents;
using WarcraftPlugin.Core.Effects;
using WarcraftPlugin.Core.Effects.Shared;

namespace WarcraftPlugin.Classes
{
    internal class SilentAssassin : WarcraftClass
    {
        public override string DisplayName => "Silent Assassin";
        public override Color DefaultColor => Color.Gray;

        public override List<string> WeaponWhitelist => ["knife", "c4", "defuse"];

        public override List<IWarcraftAbility> Abilities =>
        [
            new WarcraftAbility("Shrink", "Reduce model size by 15/20/25/30/35%"),
            new WarcraftAbility("Lightweight", "Increase speed and reduce gravity"),
            new WarcraftAbility("Assassin's Blade", "40% chance to add bonus knife damage"),
            new WarcraftCooldownAbility("Ghost Walk", "Completely invisible for 3 seconds", 15f)
        ];

        private readonly float _scaleMultiplier = 0.05f;
        private readonly float _speedMultiplier = 0.07f;
        private readonly float _gravityMultiplier = 0.06f;
        private readonly int _knifeBonusMultiplier = 10;

        public override void Register()
        {
            RegisterHooks()
                .Hook<EventPlayerSpawn>(PlayerSpawn)
                .Hook<EventPlayerHurtOther>(PlayerHurtOther);

            RegisterAbilities()
                .Bind(3, Ultimate);
        }

        public override void OnPlayerSpawned()
        {
            Server.NextFrame(() =>
            {
                if (Player == null || !Player.IsAlive()) return;
                var pawn = Player.PlayerPawn?.Value;
                if (pawn == null) return;

                // apply scale
                int shrinkLevel = WarcraftPlayer.GetAbilityLevel(0);
                if (shrinkLevel > 0)
                {
                    var scale = 1 - (_scaleMultiplier * shrinkLevel);
                    pawn.SetScale(scale);
                }

                // apply speed and gravity
                int lightweight = WarcraftPlayer.GetAbilityLevel(1);
                if (lightweight > 0)
                {
                    pawn.VelocityModifier += _speedMultiplier * lightweight;
                    pawn.GravityScale -= _gravityMultiplier * lightweight;
                }
            });
        }

        private void PlayerSpawn(EventPlayerSpawn spawn) => OnPlayerSpawned();

        private void PlayerHurtOther(EventPlayerHurtOther @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            if (attacker == null || attacker.UserId != Player.UserId || victim == null || !victim.IsAlive() || victim.AllyOf(Player)) return;
            if (@event.Weapon != "knife") return;

            int level = WarcraftPlayer.GetAbilityLevel(2);
            if (level <= 0) return;
            if (Warcraft.RollChance(40))
            {
                int bonus = RandomProvider.Next(1, level * _knifeBonusMultiplier);
                @event.AddBonusDamage(bonus, abilityName: GetAbility(2).DisplayName);
            }
        }

        private void Ultimate()
        {
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3)) return;
            TriggerGhostWalk(3f);
            StartCooldown(3);
        }

        private void TriggerGhostWalk(float duration)
        {
            EffectBuilder.Run(
                Player,
                duration,
                onStart: () =>
                {
                    if (!Player.IsAlive()) return;
                    var pawn = Player.PlayerPawn?.Value;
                    if (pawn == null) return;

                    Player.PrintToCenter(Localizer["rogue.invsible"]);
                    pawn.SetColor(Color.FromArgb(0, 255, 255, 255));
                    Player.AdrenalineSurgeEffect(duration);
                },
                onFinish: () =>
                {
                    if (!Player.IsAlive()) return;
                    Player.GetWarcraftPlayer().GetClass().SetDefaultAppearance();
                    Player.PrintToCenter(Localizer["rogue.visible"]);
                });
        }
    }
}
