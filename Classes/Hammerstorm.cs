using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using WarcraftPlugin.Core.Effects;
using WarcraftPlugin.Core.Effects.Shared;
using WarcraftPlugin.Events.ExtendedEvents;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Classes
{
    internal class Hammerstorm : WarcraftClass
    {
        private bool _godStrength;
        private int _godStrengthDamage;
        public override string DisplayName => "Hammerstorm";
        public override Color DefaultColor => Color.Gold;

        public override List<IWarcraftAbility> Abilities =>
        [
            new WarcraftAbility("Storm Bolt", "25% chance to stun enemies near your target."),
            new WarcraftAbility("Great Cleave", "25% chance to splash damage to nearby enemies."),
            new WarcraftAbility("Warcry", "Gain bonus health and movement speed."),
            new WarcraftCooldownAbility("Gods Strength", "Increase damage by 25% for 6s", 15f)
        ];

        public override void Register()
        {
            RegisterHooks()
                .Hook<EventPlayerHurtOther>(PlayerHurtOther)
                .Hook<EventPlayerSpawn>(PlayerSpawn);

            RegisterAbilities()
                .Bind(3, Ultimate);
        }

        public override void OnPlayerSpawned()
        {
            Server.NextFrame(() =>
            {
                if (!Player.IsValid || !Player.IsAlive()) return;
                var pawn = Player.PlayerPawn?.Value;
                if (pawn == null) return;

                var level = WarcraftPlayer.GetAbilityLevel(2);
                var speed = 1f;
                if (level > 0)
                {
                    var healthBonus = 10 + (level - 1) * 5; //10/15/20/25/30
                    Player.SetHp(pawn.Health + healthBonus);
                    speed += 0.06f + (level * 0.03f);
                }
                pawn.VelocityModifier = speed;
            });
        }

        private void PlayerSpawn(EventPlayerSpawn @event) => OnPlayerSpawned();

        private void PlayerHurtOther(EventPlayerHurtOther @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            if (attacker == null || attacker.UserId != Player.UserId || victim == null || !victim.IsAlive()) return;

            var stormLevel = WarcraftPlayer.GetAbilityLevel(0);
            if (stormLevel > 0 && Warcraft.RollChance(25))
            {
                var radius = 50 + 10 * stormLevel;
                var stunned = 0;
                foreach (var enemy in Utilities.GetPlayers().Where(x => x.PawnIsAlive && x.Team != Player.Team))
                {
                    var enemyPawn = enemy.PlayerPawn?.Value;
                    var victimPawn = victim.PlayerPawn?.Value;
                    if (enemyPawn == null || victimPawn == null) continue;

                    if ((enemyPawn.AbsOrigin - victimPawn.AbsOrigin).Length() <= radius)
                    {
                        enemy.Stun(0.3f, Player, GetAbility(0).DisplayName);
                        stunned++;
                    }
                }

                if (stunned > 0)
                {
                    Player.PrintToChat($" {ChatColors.Gold}[Storm Bolt]{ChatColors.Default} stunned {stunned} enemies.");
                }

                var victimPawnForParticle = victim.PlayerPawn?.Value;
                if (victimPawnForParticle != null)
                {
                    var particle = Warcraft.SpawnParticle(victim.EyePosition(-20), "particles/ui/ui_experience_award_innerpoint.vpcf", 1);
                    particle.SetParent(victimPawnForParticle);
                }
            }

            var cleaveLevel = WarcraftPlayer.GetAbilityLevel(1);
            if (cleaveLevel > 0 && Warcraft.RollChance(25))
            {
                var splashPct = 0.1f + 0.05f * cleaveLevel;
                var radius = 150;
                float totalBonus = 0;
                foreach (var enemy in Utilities.GetPlayers().Where(x => x.PawnIsAlive && x.Team != Player.Team && x.UserId != victim.UserId))
                {
                    var enemyPawn = enemy.PlayerPawn?.Value;
                    var victimPawn = victim.PlayerPawn?.Value;
                    if (enemyPawn == null || victimPawn == null) continue;

                    if ((enemyPawn.AbsOrigin - victimPawn.AbsOrigin).Length() <= radius)
                    {
                        var bonus = @event.DmgHealth * splashPct;
                        var abilityName = GetAbility(1).DisplayName;
                        enemy.TakeDamage(bonus, Player, KillFeedIcon.hammer, abilityName: abilityName);
                        totalBonus += bonus;
                    }
                }

                if (totalBonus > 0)
                {
                    Player.PrintToChat($" {ChatColors.Gold}[Great Cleave]{ChatColors.Default} dealt {Math.Round(totalBonus, 1)} bonus damage.");
                }

                var victimPawnForParticle = victim.PlayerPawn?.Value;
                if (victimPawnForParticle != null)
                {
                    var particle = Warcraft.SpawnParticle(victim.EyePosition(-20), "particles/explosions_fx/explosion_hegrenade_water_ripple.vpcf", 1);
                    particle.SetParent(victimPawnForParticle);
                }
            }

            if (_godStrength)
            {
                var extra = (int)(@event.DmgHealth * 0.25f);
                if (extra > 0)
                {
                    @event.AddBonusDamage(extra, abilityName: GetAbility(3).DisplayName);
                    _godStrengthDamage += extra;
                }
            }
        }

        private void Ultimate()
        {
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3)) return;

            new GodsStrengthEffect(this, Player, 6f).Start();
            StartCooldown(3);
        }

        internal class GodsStrengthEffect(Hammerstorm cls, CCSPlayerController owner, float duration) : WarcraftEffect(owner, duration)
        {
            private readonly Hammerstorm _class = cls;
            private CParticleSystem _particle;

            public override void OnStart()
            {
                _class._godStrength = true;
                _class._godStrengthDamage = 0;
                Owner.Blind(Duration-3, Color.FromArgb(50, 255, 20, 0));

                var ownerPawn = Owner.PlayerPawn?.Value;
                if (ownerPawn != null)
                {
                    _particle = Warcraft.SpawnParticle(Owner.EyePosition(-20), "particles/maps/de_dust/dust_burning_engine_fire_glow.vpcf", Duration);
                    _particle.SetParent(ownerPawn);
                }
            }

            public override void OnTick() { }

            public override void OnFinish()
            {
                _class._godStrength = false;
                if (_class._godStrengthDamage > 0)
                {
                    Owner.PrintToChat($" {ChatColors.Gold}[Gods Strength]{ChatColors.Default} added {_class._godStrengthDamage} bonus damage.");
                }
                Owner.Unblind();
                _particle.RemoveIfValid();
            }
        }
    }
}
