using System;
using System.Drawing;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using WarcraftPlugin.Helpers;
using WarcraftPlugin.Models;
using WarcraftPlugin.Core.Effects;
using WarcraftPlugin.Core.Effects.Shared;
using System.Collections.Generic;
using WarcraftPlugin.Events.ExtendedEvents;

namespace WarcraftPlugin.Classes
{
    internal class Rogue : WarcraftClass
    {
        private bool _isPlayerInvulnerable;

        public override string DisplayName => "Rogue";
        public override Color DefaultColor => Color.DarkViolet;

        public override List<IWarcraftAbility> Abilities =>
        [
            new WarcraftAbility("Stealth", "Become partially invisible for 1/2/3/4/5 seconds, when killing someone."),
            new WarcraftAbility("Sneak Attack", "When you hit an enemy in the back, you do an aditional 5/10/15/20/25 damage."),
            new WarcraftAbility("Blade Dance", "Knifing grants 12/24/36/48/60 bonus damage and up to 10-50% more speed."),
            new WarcraftCooldownAbility("Smokebomb", "When nearing death, you will automatically drop a smokebomb, letting you cheat death.", 50f)
        ];

        public override void Register()
        {
            RegisterHooks()
                .Hook<EventPlayerHurtOther>(PlayerHurtOther)
                .Hook<EventPlayerHurt>(PlayerHurt)
                .Hook<EventPlayerKilledOther>(PlayerKilledOther)
                .Hook<EventItemEquip>(PlayerItemEquip)
                .Hook<EventPlayerSpawn>(PlayerSpawn);
        }

        public override void OnPlayerSpawned()
        {
            _isPlayerInvulnerable = false;
            Player.Speed = 1;
        }

        private void PlayerSpawn(EventPlayerSpawn spawn) => OnPlayerSpawned();

        private void PlayerHurt(EventPlayerHurt @event)
        {
            if (_isPlayerInvulnerable)
            {
                Player.SetHp(1);
                return;
            }

            if (WarcraftPlayer.GetAbilityLevel(3) < 1 || !IsAbilityReady(3)) return;

            var pawn = Player.PlayerPawn?.Value;
            if (pawn == null) return;

            if (pawn.Health < 0)
            {
                StartCooldown(3);
                _isPlayerInvulnerable = true;
                Player.SetHp(1);
                Player.Speed = 0;
                TriggerInvisibility(5);

                //spawn smoke
                var smoke = Warcraft.SpawnSmoke(pawn.AbsOrigin.Clone().Add(z: 5), pawn, Color.Black);
                smoke?.Teleport(velocity: Vector.Zero);

                Player.ExecuteClientCommand("slot3"); //pull out knife

                var smokeEffect = Warcraft.SpawnParticle(pawn.AbsOrigin.Clone().Add(z: 90), "particles/maps/de_house/house_fireplace.vpcf");
                smokeEffect.SetParent(pawn);

                WarcraftPlugin.Instance.AddTimer(2f, () =>
                {
                    if (!Player.IsValid) return;
                    _isPlayerInvulnerable = false;
                    Player.Speed = 1;
                });
            }
        }

        private void PlayerItemEquip(EventItemEquip @event)
        {
            var pawn = Player.PlayerPawn?.Value;
            if (pawn == null) return;

            var weaponServices = pawn.WeaponServices;
            var activeWeapon = weaponServices?.ActiveWeapon?.Value;
            if (activeWeapon == null) return;

            var activeWeaponName = activeWeapon.DesignerName;
            if (activeWeaponName == "weapon_knife")
            {
                pawn.VelocityModifier = 1 + 0.1f * WarcraftPlayer.GetAbilityLevel(2);
            }
            else
            {
                pawn.VelocityModifier = 1;
            }
        }

        private void SetInvisible()
        {
            var pawn = Player.PlayerPawn?.Value;
            if (pawn == null || pawn.Render.A == 0) return;
            TriggerInvisibility(WarcraftPlayer.GetAbilityLevel(0));
        }

        private void PlayerKilledOther(EventPlayerKilledOther @event)
        {
            if (WarcraftPlayer.GetAbilityLevel(0) > 0)
            {
                SetInvisible();
            }
        }

        private void PlayerHurtOther(EventPlayerHurtOther @event)
        {
            var attacker = @event.Attacker;
            var victim = @event.Userid;
            if (attacker == null || attacker.UserId != Player.UserId || victim == null || !victim.IsAlive()) return;

            if (WarcraftPlayer.GetAbilityLevel(2) > 0) BladeDanceDamage(@event);
            if (WarcraftPlayer.GetAbilityLevel(1) > 0) Backstab(@event);
        }

        private void BladeDanceDamage(EventPlayerHurtOther @event)
        {
            if (@event.Weapon == "knife")
            {
                var damageBonus = WarcraftPlayer.GetAbilityLevel(2) * 12;
                @event.AddBonusDamage(damageBonus, abilityName: GetAbility(2).DisplayName);
                Player.EmitSound("Player.GhostKnifeSwish", volume: 0.2f);
            }
        }

        private void Backstab(EventPlayerHurtOther eventPlayerHurt)
        {
            var attackerPawn = eventPlayerHurt.Attacker?.PlayerPawn?.Value;
            var victimPawn = eventPlayerHurt.Userid?.PlayerPawn?.Value;
            if (attackerPawn == null || victimPawn == null) return;

            var attackerAngle = attackerPawn.GetEyeAngles().Y;
            var victimAngle = victimPawn.GetEyeAngles().Y;

            if (Math.Abs(attackerAngle - victimAngle) <= 50)
            {
                var damageBonus = WarcraftPlayer.GetAbilityLevel(1) * 5;
                eventPlayerHurt.AddBonusDamage(damageBonus, abilityName: GetAbility(1).DisplayName);
                var victimPawnForParticle = eventPlayerHurt.Userid?.PlayerPawn?.Value;
                if (victimPawnForParticle != null)
                {
                    Warcraft.SpawnParticle(victimPawnForParticle.AbsOrigin.Clone().Add(z: 85), "particles/overhead_icon_fx/radio_voice_flash.vpcf", 1);
                }
            }
        }

        private void TriggerInvisibility(float duration)
        {
            EffectBuilder.Run(
                Player,
                duration,
                onStart: () =>
                {
                    if (!Player.IsAlive()) return;
                    Player.PrintToCenter(Localizer["rogue.invsible"]);
                    Player.PlayerPawn.Value.SetColor(Color.FromArgb(0, 255, 255, 255));
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
