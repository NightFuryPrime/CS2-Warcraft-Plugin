using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using WarcraftPlugin.Core.Effects;
using WarcraftPlugin.Events.ExtendedEvents;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;

namespace WarcraftPlugin.Classes
{
    internal class SacredWarrior : WarcraftClass
    {
        public override string DisplayName => "Sacred Warrior";
        public override Color DefaultColor => Color.Orange;

        public override List<IWarcraftAbility> Abilities =>
        [
            new WarcraftAbility("Inner Vitality", "Passively recover 1/2/3/4/5 HP. When below 40% you heal twice as fast"),
            new WarcraftAbility("Burning Spear", "Lose 5% max HP, but set enemies ablaze. Deals 1/2/3/4/5 DPS for 3 seconds. Stacks 3 times"),
            new WarcraftAbility("Berserkers Blood", "Gain 1/2/3/4% move speed for each 7 percent of your health missing"),
            new WarcraftCooldownAbility("Life Break", "Damage yourself (20% of max HP) to deal a great amount of damage (40% of victim's max HP)", 40f)
        ];

        public override void Register()
        {
            RegisterHooks()
                .Hook<EventPlayerSpawn>(PlayerSpawn)
                .Hook<EventPlayerHurtOther>(PlayerHurtOther)
                .Hook<EventRoundEnd>(RoundEnd);

            RegisterAbilities()
                .Bind(3, Ultimate);
        }

        private void RoundEnd(EventRoundEnd end)
        {
            // Clean up all effects when round ends
            WarcraftPlugin.Instance.EffectManager.DestroyEffects(Player, EffectDestroyFlags.OnRoundEnd);
        }

        public override void OnPlayerSpawned()
        {
            RunWhenPawnReady(() =>
            {
                var vitality = WarcraftPlayer.GetAbilityLevel(0);
                if (vitality > 0)
                {
                    new InnerVitalityEffect(Player, vitality).Start();
                }

                var berserker = WarcraftPlayer.GetAbilityLevel(2);
                if (berserker > 0)
                {
                    new BerserkersBloodEffect(Player, berserker).Start();
                }
            });
        }

        private void PlayerSpawn(EventPlayerSpawn spawn) => OnPlayerSpawned();

        private void PlayerHurtOther(EventPlayerHurtOther @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            if (attacker == null || attacker.UserId != Player.UserId || victim == null || !victim.IsAlive() || victim.AllyOf(Player)) return;

            var level = WarcraftPlayer.GetAbilityLevel(1);
            if (level > 0)
            {
                var effects = WarcraftPlugin.Instance.EffectManager.GetEffectsByType<BurningSpearEffect>()
                    .Where(x => x.Victim.Handle == victim.Handle && x.Owner.Handle == Player.Handle)
                    .ToList();

                if (effects.Count >= 3)
                {
                    var oldest = effects.OrderBy(e => e.RemainingDuration).First();
                    oldest.Destroy();
                    effects.Remove(oldest);
                }

                new BurningSpearEffect(Player, victim, 3f, level, effects.Count + 1).Start();

                var pawn = Player.PlayerPawn?.Value;
                if (pawn != null)
                {
                    var burnCost = (int)(pawn.MaxHealth * 0.05f);
                    Player.TakeDamage(burnCost, Player, KillFeedIcon.inferno);
                }
            }
        }

        private void Ultimate()
        {
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3)) return;

            var trace = Player.RayTrace();
            if (trace == null) return;

            var target = Utilities.GetPlayers()
                .Where(p => p.PawnIsAlive && !p.AllyOf(Player))
                .OrderBy(p => (p.PlayerPawn.Value.AbsOrigin - trace).Length())
                .FirstOrDefault(p => p.PlayerPawn.Value.CollisionBox().Contains(trace) ||
                                     (p.PlayerPawn.Value.AbsOrigin - trace).Length() <= 50);
            if (target == null) {
                Player.PrintToChat($" {Localizer["effect.no.target", GetAbility(3).DisplayName]}");
                return;
            }

            var pawn = Player.PlayerPawn.Value;
            if (pawn == null) return;
            var selfDamage = (int)(pawn.MaxHealth * 0.2f);
            var damage = (int)(target.PlayerPawn.Value.MaxHealth * 0.4f);

            if (pawn.Health <= selfDamage)
            {
                Player.PrintToChat($" {Localizer["sacred_warrior.lifebreak.nohp"]}");
                return;
            }

            var targetHealthBefore = target.PlayerPawn.Value.Health;
            var playerHealthBefore = pawn.Health;

            target.TakeDamage(damage, Player, KillFeedIcon.inferno);
            Player.SetHp(pawn.Health - selfDamage);

            var dealt = Math.Min(damage, targetHealthBefore);
            var self = Math.Min(selfDamage, playerHealthBefore);

            var targetPawn = target.PlayerPawn?.Value;
            if (targetPawn != null)
            {
                Warcraft.DrawLaserBetween(Player.EyePosition(-10), target.EyePosition(-10), Color.DarkOrange);
                Warcraft.SpawnParticle(targetPawn.AbsOrigin, "particles/explosions_fx/explosion_hegrenade.vpcf", 1);
            }
            if (pawn != null)
            {
                Warcraft.SpawnParticle(pawn.AbsOrigin, "particles/inferno_fx/firework_crate_ground_low_02.vpcf", 1);
            }
            Player.PrintToChat($" {ChatColors.Green}Life Break dealt {dealt} damage and cost {self} HP.");
            target.PrintToChat($" {ChatColors.Red}Life Break hit you for {dealt} damage.");
            StartCooldown(3);
        }
    }

    // Heal should occur every 3 seconds instead of every second
    internal class InnerVitalityEffect(CCSPlayerController owner, int abilityLevel) : WarcraftEffect(owner, destroyOnDeath: true, onTickInterval:3f)
    {
        public override void OnStart() { }
        public override void OnTick()
        {
            if (!Owner.IsAlive()) return;
            var pawn = Owner.PlayerPawn?.Value;
            if (pawn == null) return;

            int heal = abilityLevel;
            if (pawn.Health < pawn.MaxHealth * 0.4f) heal *= 2;
            if (pawn.Health < pawn.MaxHealth)
            {
                Owner.SetHp(Math.Min(pawn.Health + heal, pawn.MaxHealth));
            }
        }
        public override void OnFinish() { }
    }

    internal class BurningSpearEffect(CCSPlayerController owner, CCSPlayerController victim, float duration, int damage, int stack)
        : WarcraftEffect(owner, duration, destroyOnDeath: false, onTickInterval:1f)
    {
        public CCSPlayerController Victim = victim;
        private CParticleSystem _particle;
        private int _totalDamage;
        private readonly int _tickDamage = damage;
        private readonly int _stackNumber = Math.Clamp(stack, 1, 3);

        public override void OnStart()
        {
            if (Victim.IsAlive())
            {
                Owner.PrintToChat($" {Localizer["sacred_warrior.burningspear", Victim.GetRealPlayerName()]}");
                Victim.PrintToChat($" {Localizer["sacred_warrior.burningspear.victim", Owner.GetRealPlayerName()]}");

                var victimPawn = Victim.PlayerPawn?.Value;
                if (victimPawn != null)
                {
                    _particle = Warcraft.SpawnParticle(victimPawn.AbsOrigin, "particles/burning_fx/barrel_burning_engine_fire_static.vpcf", Duration);
                    _particle.SetParent(victimPawn);
                }
            }
        }
        public override void OnTick()
        {
            if (!Victim.IsAlive()) { Destroy(); return; }

            var abilityName = Owner.GetWarcraftPlayer().GetClass().GetAbility(1).DisplayName;
            Victim.TakeDamage(_tickDamage, Owner, KillFeedIcon.inferno, abilityName: abilityName);
            _totalDamage += _tickDamage;
            var victimName = Victim.GetRealPlayerName();
            Owner.PrintToChat($" {ChatColors.Gold}[Burning Spear]{ChatColors.Default} {victimName} (Stack {_stackNumber}) -{_tickDamage} HP");
            Victim.PrintToChat($" {ChatColors.Red}[Burning Spear]{ChatColors.Default} -{_tickDamage} HP (Stack {_stackNumber})");
        }
        public override void OnFinish()
        {
            _particle.RemoveIfValid();
            if (_totalDamage > 0 && Owner?.IsValid == true)
            {
                var victimName = Victim?.GetRealPlayerName() ?? "target";
                Owner.PrintToChat($" {ChatColors.Gold}[Burning Spear]{ChatColors.Default} {victimName} took {_totalDamage} damage.");
            }
        }
    }

    internal class BerserkersBloodEffect(CCSPlayerController owner, int level) : WarcraftEffect(owner, destroyOnDeath: true, onTickInterval:0.5f)
    {
        private float _baseModifier = 1f;
        public override void OnStart()
        {
            if (Owner.IsAlive())
            {
                _baseModifier = Owner.PlayerPawn.Value.VelocityModifier;
            }
        }
        public override void OnTick()
        {
            if (!Owner.IsAlive()) return;
            var pawn = Owner.PlayerPawn?.Value;
            if (pawn == null) return;

            float missing = (pawn.MaxHealth - pawn.Health) / (float)pawn.MaxHealth;
            float stacks = missing / 0.07f;
            float speed = _baseModifier * (1f + level * 0.01f * stacks);
            pawn.VelocityModifier = speed;
        }
        public override void OnFinish()
        {
            if (!Owner.IsAlive()) return;
            var pawn = Owner.PlayerPawn?.Value;
            if (pawn != null)
            {
                pawn.VelocityModifier = _baseModifier;
            }
        }
    }
}
