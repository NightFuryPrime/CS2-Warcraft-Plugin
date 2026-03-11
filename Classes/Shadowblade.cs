using CounterStrikeSharp.API.Core;
using Vector = CounterStrikeSharp.API.Modules.Utils.Vector;
using CounterStrikeSharp.API.Modules.Utils;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;
using System.Drawing;
using WarcraftPlugin.Core.Effects;
using WarcraftPlugin.Core.Effects.Shared;
using System.Collections.Generic;
using WarcraftPlugin.Events.ExtendedEvents;
using System.Linq;
using System;

namespace WarcraftPlugin.Classes
{
    internal partial class Shadowblade : WarcraftClass
    {
        public override string DisplayName => "Shadowblade";
        public override DefaultClassModel DefaultModel => new()
        {
            TModel = "characters/models/ctm_st6/ctm_st6_variantn.vmdl",
            CTModel = "characters/models/ctm_st6/ctm_st6_variantn.vmdl"
        };

        public override Color DefaultColor => Color.Violet;

        public override List<IWarcraftAbility> Abilities =>
        [
            new WarcraftAbility("Shadowstep", "4/8/12/16/20% chance to teleport behind the attacker when taking damage."),
            new WarcraftAbility("Evasion", "6/12/18/24/30% chance to completely dodge incoming damage."),
            new WarcraftAbility("Venom Strike", "Your attacks poison enemies, dealing 1/2/3/4/5 damage per second for 4s."),
            new WarcraftCooldownAbility("Cloak of Shadows", "Turn invisible and move faster for 6s", 40f)
        ];

        private const float _venomDuration = 4f;
        private readonly float _cloakDuration = 6f;

        public override void Register()
        {
            RegisterHooks()
                .Hook<EventPlayerHurt>(PlayerHurt)
                .Hook<EventPlayerHurtOther>(PlayerHurtOther);

            RegisterAbilities()
                .Bind(3, Ultimate);
        }

        private void PlayerHurt(EventPlayerHurt @event)
        {
            var attacker = @event.Attacker;
            // Evasion: Chance to dodge damage
            if (Warcraft.RollAbilityCheck(WarcraftPlayer.GetAbilityLevel(1), 30))
            {
                Player.PrintToChat(" " + Localizer["shadowblade.evaded", @event.DmgHealth]);
                attacker?.PrintToChat(" " + Localizer["shadowblade.evaded", Player.GetRealPlayerName()]);

                @event.IgnoreDamage();
                Player.PlayerPawn.Value.EmitSound("BulletBy.Subsonic", volume: 0.2f);
                var particle = Warcraft.SpawnParticle(Player.EyePosition(-50), "particles/explosions_fx/explosion_hegrenade_dirt_ground.vpcf");
                particle.SetParent(Player.PlayerPawn.Value);

                return;
            }

            // Shadowstep
            if (attacker != null && attacker.IsValid && attacker.IsAlive() && Warcraft.RollAbilityCheck(WarcraftPlayer.GetAbilityLevel(0), 20))
            {
                if (!TryShadowstepTeleport(attacker))
                {
                    Player.PrintToChat(" " + Localizer["shadowblade.shadowstep.blocked"]);
                }
            }
        }

        private void PlayerHurtOther(EventPlayerHurtOther @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            if (attacker == null || attacker.UserId != Player.UserId || victim == null || !victim.IsAlive() || victim.AllyOf(Player)) return;
            if (!IsWeaponEligible(@event.Weapon)) return;

            var venomLevel = WarcraftPlayer.GetAbilityLevel(2);
            if (venomLevel > 0)
            {
                var activeEffects = WarcraftPlugin.Instance.EffectManager
                    .GetEffectsByType<VenomStrikeEffect>()
                    .Where(x => x.Victim.Handle == victim.Handle && x.Owner?.Handle == Player.Handle)
                    .ToList();

                if (activeEffects.Any())
                {
                    foreach (var effect in activeEffects)
                    {
                        effect.Refresh(_venomDuration, venomLevel);
                    }
                }
                else
                {
                    new VenomStrikeEffect(Player, victim, _venomDuration, venomLevel).Start();
                }
            }
        }

        private static readonly string[] _disallowedWeapons =
        [
            "weapon_hegrenade", "weapon_smokegrenade", "weapon_flashbang", "weapon_decoy",
            "weapon_molotov", "weapon_incgrenade", "weapon_tagrenade", "weapon_breachcharge"
        ];

        private static bool IsWeaponEligible(string weaponName)
        {
            if (string.IsNullOrWhiteSpace(weaponName))
                return true;

            weaponName = weaponName.ToLowerInvariant();
            return !_disallowedWeapons.Any(weaponName.Equals);
        }

        private void Ultimate()
        {
            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3)) return;

            TriggerCloak(_cloakDuration);
            StartCooldown(3);
        }
    }

    internal class VenomStrikeEffect(CCSPlayerController owner, CCSPlayerController victim, float duration, int damage) : WarcraftEffect(owner, duration, destroyOnDeath: false, onTickInterval: 1f)
    {
        public CCSPlayerController Victim = victim;
        private int _damage = damage;
        private int _totalDamage;

        private bool IsVictimValid(out CCSPlayerPawn victimPawn)
        {
            victimPawn = Victim?.PlayerPawn?.Value;
            if (Victim == null || victimPawn == null || !Victim.IsAlive())
            {
                Destroy();
                return false;
            }
            return true;
        }

        public override void OnStart()
        {
            if (!IsVictimValid(out var pawn)) return;
            Warcraft.SpawnParticle(pawn.EyePosition(-10), "particles/critters/chicken/chicken_impact_burst_zombie.vpcf", 1);
        }

        public override void OnTick()
        {
            if (!IsVictimValid(out var pawn)) return;

            Warcraft.SpawnParticle(pawn.EyePosition(-10), "particles/critters/chicken/chicken_impact_burst_zombie.vpcf", 1);
            pawn.EmitSound("Player.DamageFall.Fem", volume: 0.2f);
            var healthBefore = pawn.Health;
            Victim.TakeDamage(_damage, Owner, KillFeedIcon.bayonet, damageType: DamageTypes.Poison);
            var healthAfter = Math.Max(pawn.Health, 0);
            var appliedDamage = Math.Max(0, healthBefore - healthAfter);
            Victim.PrintToChat(" " + Localizer["shadowblade.venomstrike.victim", appliedDamage]);
            var victimName = Victim.GetRealPlayerName();
            Owner.PrintToChat($" {ChatColors.Gold}[Venom]{ChatColors.Default} {victimName} -{appliedDamage} HP");
            _totalDamage += appliedDamage;
        }

        public override void OnFinish()
        {
            if (_totalDamage > 0 && Owner?.IsValid == true)
            {
                var victimName = Victim?.GetRealPlayerName() ?? "target";
                Owner.PrintToChat($" {ChatColors.Green}[Venom] {ChatColors.Default}{victimName} took {_totalDamage} damage.");
            }
        }

        internal void Refresh(float duration, int damage)
        {
            RemainingDuration = duration;
            _damage = Math.Max(_damage, damage);
        }
    }

    partial class Shadowblade
    {
        private static readonly (float Distance, float HorizontalOffset)[] ShadowstepOffsets =
        [
            (-90f, 0f),
            (-75f, 25f),
            (-75f, -25f),
            (-110f, 0f)
        ];

        private static readonly Vector[] ShadowstepClearanceChecks =
        [
            new Vector(30, 0, 0),
            new Vector(-30, 0, 0),
            new Vector(0, 30, 0),
            new Vector(0, -30, 0)
        ];

        private bool TryShadowstepTeleport(CCSPlayerController attacker)
        {
            var attackerPawn = attacker.PlayerPawn?.Value;
            if (attackerPawn == null) return false;

            foreach (var (distance, offset) in ShadowstepOffsets)
            {
                var candidate = attacker.CalculatePositionInFront(distance, attacker.EyeHeight(), offset);
                if (!HasLineOfSight(attacker, candidate) || !HasShadowstepClearance(candidate))
                {
                    continue;
                }

                Player.PlayerPawn.Value.Teleport(candidate, attackerPawn.GetEyeAngles(), Vector.Zero);
                Warcraft.SpawnParticle(Player.PlayerPawn.Value.AbsOrigin, "particles/survival_fx/danger_zone_loop_black.vpcf", 2);
                Player.PlayerPawn.Value.EmitSound("UI.PlayerPingUrgent", volume: 0.2f);

            var chance = WarcraftPlayer.GetAbilityLevel(0) * 4;
                Player.PrintToChat(" " + Localizer["shadowblade.shadowstep.chance", chance]);
                return true;
            }

            return false;
        }

        private static bool HasLineOfSight(CCSPlayerController attacker, Vector destination)
        {
            var attackerPawn = attacker.PlayerPawn?.Value;
            if (attackerPawn == null) return false;

            var enemyPos = Warcraft.RayTrace(destination, attacker.EyePosition());
            return enemyPos != null && attackerPawn.CollisionBox().Contains(enemyPos);
        }

        private bool HasShadowstepClearance(Vector destination)
        {
            var groundCheckStart = destination.Clone().Add(z: 36);
            var groundCheckEnd = destination.Clone().Add(z: -64);
            var groundPos = Warcraft.RayTrace(groundCheckStart, groundCheckEnd);
            if (groundPos == null)
            {
                return false;
            }

            foreach (var offset in ShadowstepClearanceChecks)
            {
                var lateralStart = destination.Clone().Add(z: 24);
                var lateralEnd = lateralStart.Clone().Add(x: offset.X, y: offset.Y, z: offset.Z);
                var hit = Warcraft.RayTrace(lateralStart, lateralEnd);
                if (hit != null && Distance(hit, lateralEnd) > 1f)
                {
                    return false;
                }
            }

            return true;
        }

        private static float Distance(Vector origin, Vector target)
        {
            var dx = origin.X - target.X;
            var dy = origin.Y - target.Y;
            var dz = origin.Z - target.Z;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private void TriggerCloak(float duration)
        {
            EffectBuilder.Run(
                Player,
                duration,
                onStart: () =>
                {
                    if (!Player.IsAlive()) return;
                    Player.PrintToCenter(Localizer["rogue.invsible"]);
                    Player.PrintToChat(" " + Localizer["shadowblade.cloak.active", 100]);
                    Player.PlayerPawn.Value.SetColor(Color.FromArgb(0, 255, 255, 255));
                    Player.AdrenalineSurgeEffect(duration);
                    Player.PlayerPawn.Value.VelocityModifier = 2f;
                },
                onFinish: () =>
                {
                    if (!Player.IsAlive()) return;
                    Player.GetWarcraftPlayer().GetClass().SetDefaultAppearance();
                    Player.PrintToCenter(Localizer["rogue.visible"]);
                    Player.PlayerPawn.Value.VelocityModifier = 1f;
                });
        }
    }
}
