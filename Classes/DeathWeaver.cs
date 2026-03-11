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
    internal class DeathWeaver : WarcraftClass
    {
        public override string DisplayName => "Death Weaver";
        public override Color DefaultColor => Color.MediumPurple;

        public override List<IWarcraftAbility> Abilities =>
        [
            new WarcraftAbility("Cripple", "33% chance to slow an enemy for 1/1.5/2/2.5/3 seconds."),
            new WarcraftAbility("Unholy Frenzy", "25% chance to deal 10/20/30/40/50% bonus damage."),
            new WarcraftAbility("Necromancer Master", "50/62/75/88/100% chance to spawn with an assault rifle."),
            new WarcraftCooldownAbility("Raise Skeleton", "80% chance to revive a fallen ally. Cooldown: 60s", 60f)
        ];

        private const float CrippleMinDuration = 1f;
        private const float CrippleDurationStep = 0.5f;
        private const float CrippleMaxDuration = 3f;
        private static readonly int[] RifleSpawnChanceByLevel = { 0, 50, 62, 75, 88, 100 };
        private const double RaiseSkeletonChance = 0.8;

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
            var level = WarcraftPlayer.GetAbilityLevel(2);
            if (level <= 0) return;

            var chanceIndex = Math.Clamp(level, 0, RifleSpawnChanceByLevel.Length - 1);
            var chance = RifleSpawnChanceByLevel[chanceIndex];
            if (Warcraft.RollChance(chance))
            {
                string weapon = Player.Team == CsTeam.CounterTerrorist ? "weapon_m4a1" : "weapon_ak47";
                var gun = Player.GiveWeapon<CCSWeaponBaseGun>(weapon);
                gun.SetColor(Color.Red);
                Player.PrintToChat(" " + Localizer["death_weaver.spawn.rifle"]);
            }
        }

        private void PlayerSpawn(EventPlayerSpawn spawn) => OnPlayerSpawned();

        private void PlayerHurtOther(EventPlayerHurtOther hurt)
        {
            var attacker = hurt.Attacker;
            var victim = hurt.Userid;
            if (attacker == null || attacker.UserId != Player.UserId || victim == null || !victim.IsAlive()) return;

            var crippleLevel = WarcraftPlayer.GetAbilityLevel(0);
            if (crippleLevel > 0 && Warcraft.RollChance(33))
            {
                var duration = MathF.Min(CrippleMaxDuration, CrippleMinDuration + (Math.Max(crippleLevel, 1) - 1) * CrippleDurationStep);
                new CrippleEffect(Player, victim, duration).Start();
            }

            var frenzyLevel = WarcraftPlayer.GetAbilityLevel(1);
            if (frenzyLevel > 0 && Warcraft.RollChance(25))
            {
                var bonusDamage = (int)(hurt.DmgHealth * (0.1f * frenzyLevel));
                if (bonusDamage > 0)
                {
                    hurt.AddBonusDamage(bonusDamage, abilityName: GetAbility(1).DisplayName);
                }
            }
        }

        private void Ultimate()
        {
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3)) return;

            StartCooldown(3);

            if (!Warcraft.RollChance((float)(RaiseSkeletonChance * 100)))
            {
                Player.PrintToChat($" {Localizer["death_weaver.revive.fail"]}");
                return;
            }

            var deadTeamPlayers = Utilities.GetPlayers()
                .Where(x => x.Team == Player.Team && !x.PawnIsAlive && x.IsValid)
                .ToList();
            if (!deadTeamPlayers.Any())
            {
                Player.PrintToChat(" " + Localizer["death_weaver.revive.none"]);
                return;
            }

            var playerToRevive = deadTeamPlayers[RandomProvider.Next(deadTeamPlayers.Count)];
            playerToRevive.Respawn();

            Server.NextFrame(() =>
            {
                if (!playerToRevive.IsValid || !playerToRevive.IsAlive() || !Player.IsValid || !Player.IsAlive()) return;

                var targetPawn = playerToRevive.PlayerPawn?.Value;
                var ownerPawn = Player.PlayerPawn?.Value;
                if (targetPawn == null || ownerPawn == null) return;

                targetPawn.Teleport(Player.CalculatePositionInFront(10, 60), ownerPawn.GetEyeAngles(), new Vector());

                var particle = Warcraft.SpawnParticle(playerToRevive.EyePosition(-60), "particles/explosions_fx/explosion_smokegrenade_init.vpcf", 2);
                particle.SetParent(targetPawn);
            });

            playerToRevive.PrintToChat(" " + Localizer["death_weaver.revive"]);
            Utilities.GetPlayers().ForEach(x =>
                x.PrintToChat(" " + Localizer["death_weaver.revive.other", playerToRevive.GetRealPlayerName(), Player.GetRealPlayerName()]));
        }

        internal class CrippleEffect(CCSPlayerController owner, CCSPlayerController victim, float duration) : WarcraftEffect(owner, duration)
        {
            private readonly CCSPlayerController _victim = victim;
            private float _originalSpeed;
            private float _originalModifier;
            private CParticleSystem _particle;

            public override void OnStart()
            {
                if (!_victim.IsAlive()) return;
                _originalSpeed = _victim.PlayerPawn.Value.MovementServices.Maxspeed;
                _originalModifier = _victim.PlayerPawn.Value.VelocityModifier;
                _victim.PlayerPawn.Value.MovementServices.Maxspeed = _originalSpeed * 0.7f;
                _victim.PlayerPawn.Value.VelocityModifier = _originalModifier * 0.7f;

                Owner.PrintToChat($" {Localizer["death_weaver.cripple", _victim.GetRealPlayerName()]}");
                _victim.PrintToChat($" {Localizer["death_weaver.cripple.victim", Owner.GetRealPlayerName()]}");

                _particle = Warcraft.SpawnParticle(_victim.PlayerPawn.Value.AbsOrigin, "particles/water_impact/water_foam_01c.vpcf", Duration);
                _particle.SetParent(_victim.PlayerPawn.Value);
            }

            public override void OnTick()
            {
                if (!_victim.IsAlive()) { Destroy(); return; }
            }

            public override void OnFinish()
            {
                if (_victim.IsAlive())
                {
                    _victim.PlayerPawn.Value.MovementServices.Maxspeed = _originalSpeed;
                    _victim.PlayerPawn.Value.VelocityModifier = _originalModifier;
                }

                _particle.RemoveIfValid();
            }
        }
    }
}
